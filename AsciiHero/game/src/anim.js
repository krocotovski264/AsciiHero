// Motion Matching locomotion (after Clavet 2015 / D. Holden's open-source Motion-Matching, MIT),
// running on motion capture retargeted to the MakeHuman rig.
//  * critically-damped spring controller predicts the desired trajectory (0.33 / 0.66 / 1.0 s)
//  * every 0.1 s (or on sharp input changes) the database is searched for the frame whose
//    feet, hip velocity and future trajectory best continue the current motion
//  * transitions use inertialization (pose offsets decaying with a 0.1 s half-life)
//  * root motion from the animation, synchronised with the controller ("simulation bone")
//  * two-bone foot IK with contact locking, knee pole from the toe direction
import * as THREE from 'three';
import { MM_META, MM_B64 } from './data_mm.js';

let DB = null;
const LN2 = Math.log(2);
export const RUN_SPEED = 3.0;
const SPRINT_EXTRA = 1.1;
let SWITCH_K = +(globalThis.process && process.env.SK || 0.7);   // a jump must beat the current clip clearly, otherwise poses flicker
export async function loadMocap() {
  if (DB) return;
  const bin = Uint8Array.from(atob(MM_B64), c => c.charCodeAt(0));
  const buf = await new Response(new Blob([bin]).stream().pipeThrough(new DecompressionStream('gzip'))).arrayBuffer();
  const M = MM_META, n = M.n, nb = M.bones.length, NF = M.nf, N2 = n * 2;
  const q16 = new Int16Array(buf, M.q[0], M.q[1] / 2), ro16 = new Int16Array(buf, M.ro[0], M.ro[1] / 2), rm16 = new Int16Array(buf, M.rm[0], M.rm[1] / 2);
  const con8 = new Uint8Array(buf, M.con[0], M.con[1]), f16 = new Int16Array(buf, M.f[0], M.f[1] / 2), val = new Uint8Array(buf, M.valid[0], M.valid[1]), ib = new Uint8Array(buf, M.idleB[0], M.idleB[1]);
  const Q = new Float32Array(N2 * nb * 4), RO = new Float32Array(N2 * 3), RM = new Float32Array(N2 * 3), CON = new Float32Array(N2 * 2), F = new Float32Array(N2 * NF);
  const VALID = new Uint8Array(N2), IDLEB = new Uint8Array(N2), RANGE_END = new Int32Array(N2);
  // bone mirror map
  const sw = M.bones.map(b => { const o = b.endsWith('.L') ? b.slice(0, -2) + '.R' : b.endsWith('.R') ? b.slice(0, -2) + '.L' : b; return M.bones.indexOf(o); });
  for (let f = 0; f < n; f++) {
    for (let k = 0; k < nb; k++) {
      const o = (f * nb + k) * 3, x = q16[o] / 32767, y = q16[o + 1] / 32767, z = q16[o + 2] / 32767, w = Math.sqrt(Math.max(0, 1 - x * x - y * y - z * z));
      const d = (f * nb + k) * 4; Q[d] = x; Q[d + 1] = y; Q[d + 2] = z; Q[d + 3] = w;
      const dm = ((f + n) * nb + sw[k]) * 4; Q[dm] = x; Q[dm + 1] = -y; Q[dm + 2] = -z; Q[dm + 3] = w;   // mirror across the YZ plane
    }
    for (let j = 0; j < 3; j++) { RO[f * 3 + j] = ro16[f * 3 + j] / 4000; RM[f * 3 + j] = rm16[f * 3 + j] / 2000; }
    RO[(f + n) * 3] = -RO[f * 3]; RO[(f + n) * 3 + 1] = RO[f * 3 + 1]; RO[(f + n) * 3 + 2] = RO[f * 3 + 2];
    RM[(f + n) * 3] = RM[f * 3]; RM[(f + n) * 3 + 1] = -RM[f * 3 + 1]; RM[(f + n) * 3 + 2] = -RM[f * 3 + 2];
    CON[f * 2] = con8[f * 2] / 255; CON[f * 2 + 1] = con8[f * 2 + 1] / 255; CON[(f + n) * 2] = CON[f * 2 + 1]; CON[(f + n) * 2 + 1] = CON[f * 2];
    // features: raw = fn/scale + mean ; mirror ; renormalise
    const raw = new Float32Array(NF);
    for (let j = 0; j < NF; j++) { const v = f16[f * NF + j] / 1000; F[f * NF + j] = v; raw[j] = v / M.scale[j] + M.mean[j]; }
    const mr = new Float32Array(NF);
    for (let j = 0; j < 3; j++) { mr[j] = raw[3 + j] * (j === 0 ? -1 : 1); mr[3 + j] = raw[j] * (j === 0 ? -1 : 1); mr[6 + j] = raw[9 + j] * (j === 0 ? -1 : 1); mr[9 + j] = raw[6 + j] * (j === 0 ? -1 : 1); }
    mr[12] = -raw[12]; mr[13] = raw[13]; mr[14] = raw[14];
    for (let k = 0; k < 3; k++) { mr[15 + 2 * k] = -raw[15 + 2 * k]; mr[16 + 2 * k] = raw[16 + 2 * k]; mr[21 + 2 * k] = -raw[21 + 2 * k]; mr[22 + 2 * k] = raw[22 + 2 * k]; }
    for (let j = 0; j < NF; j++) F[(f + n) * NF + j] = (mr[j] - M.mean[j]) * M.scale[j];
    VALID[f] = VALID[f + n] = val[f]; IDLEB[f] = IDLEB[f + n] = ib[f];
  }
  for (const [a, b] of M.ranges) for (let f = a; f < b; f++) { RANGE_END[f] = b - 1; RANGE_END[f + n] = b - 1 + n; }
  DB = { n: N2, nb, NF, Q, RO, RM, CON, F, VALID, IDLEB, RANGE_END, bones: M.bones, fps: M.fps };
  for (const k in LOOPS) prepLoop(LOOPS[k]);
}
export const clipNames = () => [];

const clamp = (x, a, b) => Math.max(a, Math.min(b, x));
const wrap = a => Math.atan2(Math.sin(a), Math.cos(a));
const damp = (h) => 4 * LN2 / (h + 1e-5);
const _v = new THREE.Vector3(), _q = new THREE.Quaternion(), _q2 = new THREE.Quaternion();

// ---------- spring helpers (Holden, "Spring-It-On") ----------
function springChar(x, v, a, vGoal, h, dt) {        // returns [x, v, a] ; critically damped velocity spring with integrated position
  const y = damp(h) / 2, j0 = v - vGoal, j1 = a + j0 * y, e = Math.exp(-y * dt);
  return [e * ((-j1) / (y * y) + (-j0 - j1 * dt) / y) + j1 / (y * y) + j0 / y + vGoal * dt + x, e * (j0 + j1 * dt) + vGoal, e * (a - j1 * y * dt)];
}
function springAng(x, v, goal, h, dt) {
  const y = damp(h) / 2, j0 = wrap(x - goal), j1 = v + j0 * y, e = Math.exp(-y * dt);
  return [e * (j0 + j1 * dt) + goal, e * (v - j1 * y * dt)];
}

// ---------- phase-matched loops (default locomotion) ----------
// The standard way games get smooth movement from a small set of clips: three clean cycles cut from the mocap
// (idle, walk, run) are aligned by foot contact and blended by speed with a shared phase. Game logic moves the body,
// the playback rate follows the real speed so the feet do not slide, and nothing ever jumps between clips.
// idle stances (motion capture): a man standing at ease, a woman with her weight on one leg
const LOOPS = { idle: { a: 10493, b: 10583 }, idleF: { a: 10583, b: 10663 }, walk: { a: 648, b: 685 }, run: { a: 7678, b: 7700 } };
const idleLoop = A => A.char.cfgSex === 'f' ? LOOPS.idleF : LOOPS.idle;
function prepLoop(L) {
  L.len = L.b - L.a; L.T = L.len / DB.fps; L.off = 0;
  let v = 0; for (let f = L.a; f < L.b; f++) v += Math.hypot(DB.RM[f * 3], DB.RM[f * 3 + 1]); L.v = v / L.len;
  // the pelvis offset drifts along a clip (up to half a metre in the run); keep only its cyclic part, or the body jumps at every wrap
  L.dx = DB.RO[L.b * 3] - DB.RO[L.a * 3]; L.dz = DB.RO[L.b * 3 + 2] - DB.RO[L.a * 3 + 2]; L.mx = 0; L.mz = 0;
  for (let i = 0; i < L.len; i++) { L.mx += DB.RO[(L.a + i) * 3] - L.dx * i / L.len; L.mz += DB.RO[(L.a + i) * 3 + 2] - L.dz * i / L.len; }
  L.mx /= L.len; L.mz /= L.len;
  for (let i = 0; i < L.len; i++) { const f = L.a + i, p = L.a + (i + L.len - 1) % L.len; if (DB.CON[p * 2] < 0.5 && DB.CON[f * 2] >= 0.5) { L.off = i; break; } }   // phase 0 = left foot lands
}
const _lq = [], _lq2 = [];
function sampleLoop(L, phase, outQ, outRO, outCon) {
  const pos = ((phase % 1 + 1) % 1 * L.len + L.off) % L.len, nb = DB.nb, W = Math.min(5, L.len / 3);
  samplePose(L.a + pos, outQ, outRO, outCon);
  outRO[0] -= L.dx * pos / L.len + L.mx; outRO[2] -= L.dz * pos / L.len + L.mz;
  if (pos > L.len - W) {                       // cross-fade the end of the cycle into its start: a seamless loop
    const t = (pos - (L.len - W)) / W, u = t * t * (3 - 2 * t);
    if (!_lq.length) for (let k = 0; k < nb; k++) _lq.push(new THREE.Quaternion());
    const ro2 = [0, 0, 0], c2 = [0, 0];
    samplePose(L.a + pos - L.len, _lq, ro2, c2);
    ro2[0] -= L.dx * (pos - L.len) / L.len + L.mx; ro2[2] -= L.dz * (pos - L.len) / L.len + L.mz;
    for (let k = 0; k < nb; k++) outQ[k].slerp(_lq[k], u);
    for (let j = 0; j < 3; j++) outRO[j] += (ro2[j] - outRO[j]) * u;
    if (outCon) for (let j = 0; j < 2; j++) outCon[j] += (c2[j] - outCon[j]) * u;
  }
}
const smooth01 = (a, b, x) => { const t = clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); };
function loopLocomotion(A, dt, dir, myPos) {
  const S = A.sim, v = Math.hypot(S.vx, S.vz), W = LOOPS.walk, R = LOOPS.run;
  const wRun = smooth01(1.6, 2.5, v), wWalk = smooth01(0.04, 0.5, v);
  const T = W.T + (R.T - W.T) * wRun, nat = W.v + (R.v - W.v) * wRun;
  A.phase = ((A.phase || 0) + dt / T * clamp(v / nat, 0.55, 1.5)) % 1;
  A.idlePh = ((A.idlePh || 0) + dt / idleLoop(A).T) % 1;
  A.wWalk = wWalk; A.wRun = wRun;
  // the body is moved by the controller; facing follows it smoothly
  const dx = S.x - myPos.x, dz = S.z - myPos.z;
  A.heading += wrap(S.h - A.heading) * (1 - Math.exp(-dt * LN2 / 0.045));
  A.speed = v; A.narrow = 1; A.vel = Math.hypot(dx, dz) / Math.max(dt, 1e-4);
  A.label = (v < 0.08 ? 'idle' : v < 1.6 ? 'walk ' + v.toFixed(1) + ' m/s' : ((A.sprint || 0) > 0.5 ? 'sprint ' : 'run ') + v.toFixed(1) + ' m/s') + injuryLabel(A);
  return [dx, dz];
}
function loopPose(A) {
  const nb = DB.nb;
  if (!A.q2) { A.q2 = A.outQ.map(() => new THREE.Quaternion()); A.q3 = A.outQ.map(() => new THREE.Quaternion()); }
  const ro2 = [0, 0, 0], ro3 = [0, 0, 0], c2 = [0, 0], c3 = [0, 0], wW = A.wWalk || 0, wR = A.wRun || 0;
  sampleLoop(idleLoop(A), A.idlePh || 0, A.outQ, A.ro, A.con);
  if (wW > 0.001) {
    sampleLoop(LOOPS.walk, A.phase || 0, A.q2, ro2, c2);
    if (wR > 0.001) { sampleLoop(LOOPS.run, A.phase || 0, A.q3, ro3, c3); for (let k = 0; k < nb; k++) A.q2[k].slerp(A.q3[k], wR); for (let j = 0; j < 3; j++) ro2[j] += (ro3[j] - ro2[j]) * wR; for (let j = 0; j < 2; j++) c2[j] += (c3[j] - c2[j]) * wR; }
    for (let k = 0; k < nb; k++) A.outQ[k].slerp(A.q2[k], wW);
    for (let j = 0; j < 3; j++) A.ro[j] += (ro2[j] - A.ro[j]) * wW; for (let j = 0; j < 2; j++) A.con[j] += (c2[j] - A.con[j]) * wW;
  }
}

// ---------- actor ----------
export function makeActor(c) {
  const H = c.human; H.root.updateMatrixWorld(true);
  const nb = MM_META.bones.length;
  const A = {
    char: c, H, baseQ: H.bones.map(b => b.quaternion.clone()), rootPos: H.root.position.clone(), map: MM_META.bones.map(n => H.BI[n]),
    heading: 0, speed: 0, frame: 0, searchT: 0, idleT: 0, idleBreakAt: 9 + Math.random() * 5,
    sim: { x: 0, z: 0, vx: 0, vz: 0, ax: 0, az: 0, h: 0, w: 0, init: false }, lastDir: null,
    off: Array.from({ length: nb }, () => new THREE.Quaternion()), offRO: [0, 0, 0],
    outQ: Array.from({ length: nb }, () => new THREE.Quaternion()), ro: [0, 0, 0], con: [1, 1],
    feet: { L: { locked: false, pos: new THREE.Vector3(), rel: 0 }, R: { locked: false, pos: new THREE.Vector3(), rel: 0 } },
    query: new Float32Array(27), label: 'idle', cost: 0,
    limbs: { head: true, armL: true, armR: true, legL: true, legR: true }, hopPh: 0, crawlPh: 0, vel: 0,
  };
  A.restAnkle = Math.min(localY(H, 'foot.L'), localY(H, 'foot.R'));
  { const u = P(H.bone('neck01')).sub(P(H.bone('root'))); A.restTrunk = Math.atan2(u.z, u.y); const w = P(H.bone('spine03')).sub(P(H.bone('root'))); A.restLower = Math.atan2(w.z, w.y);
    const hb = H.bone('head'), cb = H.bone('spine03'); if (hb && cb) A.headRest = cb.getWorldQuaternion(new THREE.Quaternion()).invert().multiply(hb.getWorldQuaternion(new THREE.Quaternion())); }   // upright trunk angle of the rest pose
  // weapon / shield arms: remember the built pose of the whole arm chain relative to the body
  const gqi = c.group.getWorldQuaternion(new THREE.Quaternion()).invert(); A.armRel = {};
  for (const s of ['L', 'R']) for (const n of ARM_CHAIN) { const b = H.bone(n + '.' + s); if (b) A.armRel[n + '.' + s] = gqi.clone().multiply(b.getWorldQuaternion(new THREE.Quaternion())); }
  if (DB) A.frame = startFrame();
  return A;
}
const localY = (H, n) => H.root.parent.worldToLocal(H.bone(n).getWorldPosition(_v)).y;
function startFrame() { for (let f = 0; f < DB.n; f++) if (DB.IDLEB[f] && DB.VALID[f]) return f + 60; return 0; }

// pose of a (fractional) database frame
function samplePose(fr, outQ, outRO, outCon) {
  const f0 = Math.floor(fr), end = DB.RANGE_END[f0], f1 = Math.min(end, f0 + 1), u = fr - f0, nb = DB.nb, Q = DB.Q;
  for (let k = 0; k < nb; k++) {
    const a = (f0 * nb + k) * 4, b = (f1 * nb + k) * 4;
    _q.set(Q[a], Q[a + 1], Q[a + 2], Q[a + 3]); _q2.set(Q[b], Q[b + 1], Q[b + 2], Q[b + 3]);
    outQ[k].copy(_q).slerp(_q2, u);
  }
  for (let j = 0; j < 3; j++) outRO[j] = DB.RO[f0 * 3 + j] * (1 - u) + DB.RO[f1 * 3 + j] * u;
  if (outCon) for (let j = 0; j < 2; j++) outCon[j] = DB.CON[f0 * 2 + j] * (1 - u) + DB.CON[f1 * 2 + j] * u;
}

// brute-force nearest neighbour over all valid frames
function search(q, exclude0, exclude1) {
  const F = DB.F, NF = DB.NF, V = DB.VALID; let best = -1, bc = Infinity;
  for (let f = 0; f < DB.n; f++) {
    if (!V[f] || (f >= exclude0 && f <= exclude1)) continue;
    let c = 0, o = f * NF;
    for (let j = 0; j < NF; j++) { const d = q[j] - F[o + j]; c += d * d; if (c >= bc) break; }
    if (c < bc) { bc = c; best = f; }
  }
  return [best, bc];
}
function costAt(q, f) { let c = 0; const o = f * DB.NF; for (let j = 0; j < DB.NF; j++) { const d = q[j] - DB.F[o + j]; c += d * d; } return c; }

function transition(A, f) {
  // inertialization: remember the offset between what is shown now and the new pose
  const tmp = A.outQ.map(q => q.clone()), tro = A.ro.slice();
  const newQ = A.outQ.map(() => new THREE.Quaternion()), nro = [0, 0, 0];
  samplePose(f, newQ, nro, null);
  for (let k = 0; k < newQ.length; k++) { A.off[k].copy(tmp[k]).multiply(newQ[k].clone().invert()); if (A.off[k].w < 0) { A.off[k].x *= -1; A.off[k].y *= -1; A.off[k].z *= -1; A.off[k].w *= -1; } }
  for (let j = 0; j < 3; j++) A.offRO[j] = tro[j] - nro[j];
  A.frame = f;
}

// ---------- controller + matching ----------
// dir: [x,z] world or null; mag 0..1; lockPos {x,z} or null; myPos {x,z}; collide(vec) optional. Returns [dx, dz].
export function locomotion(A, dt, dir, mag, lockPos, myPos, collide) {
  if (!DB) return [0, 0];
  const S = A.sim, M = MM_META;
  if (!S.init) { S.x = myPos.x; S.z = myPos.z; S.h = A.heading; S.init = true; }
  // desired velocity / facing
  let speed = 0;
  if (dir) {
    // inside the stick ring: turn on the spot -> stroll -> brisk walk; finger pulled past the ring: run
    const walk = mag < 0.22 ? 0 : 0.75 + 0.65 * clamp((mag - 0.22) / 0.78, 0, 1);
    speed = mag <= 1 ? walk : 1.4 + (RUN_SPEED - 1.4) * clamp(mag - 1, 0, 1);
    // sprint: holding the run for a moment builds into an all-out sprint (and it drops at once when the run is released)
    const vNow = Math.hypot(S.vx, S.vz);
    A.sprintT = mag > 1.6 && vNow > 2.3 && !lockPos ? Math.min(3, (A.sprintT || 0) + dt) : Math.max(0, (A.sprintT || 0) - dt * 4);
    A.sprint = smooth01(0.7, 1.7, A.sprintT);
    if (mag > 1.6) speed += SPRINT_EXTRA * A.sprint;
    if (lockPos) speed = Math.min(speed, 1.7);
    if (A.guardHeld) speed = Math.min(speed, 1.1);          // behind the shield you only walk
    const legs = (A.limbs.legL ? 1 : 0) + (A.limbs.legR ? 1 : 0), arms = (A.limbs.armL ? 1 : 0) + (A.limbs.armR ? 1 : 0);
    if (legs === 1) speed = Math.min(speed, 1.9);                       // hopping on one leg
    if (legs === 0) speed = Math.min(speed, arms === 2 ? 0.5 : arms === 1 ? 0.35 : 0.18);   // crawling
  }
  const vgx = dir ? dir[0] * speed : 0, vgz = dir ? dir[1] * speed : 0;
  let hGoal = S.h;
  if (lockPos) hGoal = Math.atan2(lockPos.x - S.x, lockPos.z - S.z);
  else if (dir) hGoal = Math.atan2(dir[0], dir[1]);
  const HV = 0.15, HR = 0.1;   // snappy: velocity and facing reach the stick in ~0.25 s
  // advance the simulation
  [S.x, S.vx, S.ax] = springChar(S.x, S.vx, S.ax, vgx, HV, dt);
  [S.z, S.vz, S.az] = springChar(S.z, S.vz, S.az, vgz, HV, dt);
  [S.h, S.w] = springAng(S.h, S.w, hGoal, HR, dt);
  if (collide) { const p = { x: S.x, z: S.z }; collide(p); S.x = p.x; S.z = p.z; }
  // keep the simulation close to the character
  const dxs = S.x - myPos.x, dzs = S.z - myPos.z, dl = Math.hypot(dxs, dzs);
  if (dl > 0.25) { S.x = myPos.x + dxs / dl * 0.25; S.z = myPos.z + dzs / dl * 0.25; }

  A.loopMode = A.mode !== 'mm' && !lockPos;
  if (A.loopMode) return loopLocomotion(A, dt, dir, myPos);
  // ---- query: current pose features + predicted trajectory relative to the character ----
  const q = A.query, f0 = Math.floor(A.frame), NF = DB.NF;
  for (let j = 0; j < 15; j++) q[j] = DB.F[f0 * NF + j];
  const ch = Math.cos(-A.heading), sh = Math.sin(-A.heading);
  [1 / 3, 2 / 3, 1].forEach((t, k) => {
    const [px] = springChar(S.x, S.vx, S.ax, vgx, HV, t), [pz] = springChar(S.z, S.vz, S.az, vgz, HV, t);
    const [ph] = springAng(S.h, S.w, hGoal, HR, t);
    const rx = px - myPos.x, rz = pz - myPos.z;
    // local frame: +x = character left, +z = forward
    const lx = ch * rx + sh * rz, lz = -sh * rx + ch * rz;
    const dh = wrap(ph - A.heading);
    const raw = [lx, lz, Math.sin(dh), Math.cos(dh)];
    q[15 + 2 * k] = (raw[0] - M.mean[15 + 2 * k]) * M.scale[15 + 2 * k]; q[16 + 2 * k] = (raw[1] - M.mean[16 + 2 * k]) * M.scale[16 + 2 * k];
    q[21 + 2 * k] = (raw[2] - M.mean[21 + 2 * k]) * M.scale[21 + 2 * k]; q[22 + 2 * k] = (raw[3] - M.mean[22 + 2 * k]) * M.scale[22 + 2 * k];
  });
  // ---- search on a timer, at clip ends, or when the input changes sharply ----
  A.searchT -= dt;
  let force = A.frame + 1 >= DB.RANGE_END[f0];
  if (dir && A.lastDir) { const c = dir[0] * A.lastDir[0] + dir[1] * A.lastDir[1]; if (c < 0.7) force = true; }
  if (!!dir !== !!A.lastDir) force = true;
  A.lastDir = dir ? [dir[0], dir[1]] : null;
  if (A.searchT <= 0 || force) {
    A.searchT = 0.07;
    const cur = costAt(q, f0);
    const [best, bc] = search(q, f0 - 3, f0 + 12);
    if (best >= 0 && (bc < cur * SWITCH_K - 0.05 || force && A.frame + 1 >= DB.RANGE_END[f0])) transition(A, best);
    A.cost = Math.min(cur, bc);
  }
  // ---- idle breaker ----
  const idle = !dir && Math.hypot(S.vx, S.vz) < 0.05;
  A.idleT = idle ? A.idleT + dt : 0;
  if (A.idleT > A.idleBreakAt) {
    A.idleT = 0; A.idleBreakAt = 9 + Math.random() * 6;
    const cands = []; for (let f = 0; f < DB.n; f += 7) if (DB.IDLEB[f] && DB.VALID[f]) cands.push(f);
    transition(A, cands[Math.floor(Math.random() * cands.length)]);
  }
  // ---- advance the animation and apply its root motion ----
  A.frame = Math.min(DB.RANGE_END[Math.floor(A.frame)], A.frame + dt * DB.fps);
  const fi = Math.floor(A.frame);
  const fwd = DB.RM[fi * 3], lat = DB.RM[fi * 3 + 1], yr = DB.RM[fi * 3 + 2];
  A.heading += yr * dt;
  const fx = Math.sin(A.heading), fz = Math.cos(A.heading), lx = Math.cos(A.heading), lz = -Math.sin(A.heading);
  let dx = (fx * fwd + lx * lat) * dt, dz = (fz * fwd + lz * lat) * dt;
  // synchronise the character with the simulation (small, smooth corrections)
  const k = 1 - Math.exp(-dt * LN2 / 0.05);    // the body follows the controller tightly (responsive)
  dx += (S.x - (myPos.x + dx)) * k; dz += (S.z - (myPos.z + dz)) * k;
  A.heading += wrap(S.h - A.heading) * (1 - Math.exp(-dt * LN2 / 0.05));
  A.speed = Math.hypot(fwd, lat);
  A.narrow = 1 - clamp(Math.abs(lat) / 0.5, 0, 1);
  A.label = labelFor(fwd, lat, yr, dir) + injuryLabel(A);
  A.vel = Math.hypot(dx, dz) / Math.max(dt, 1e-4);
  return [dx, dz];
}
function labelFor(fwd, lat, yr, dir) {
  const s = Math.hypot(fwd, lat);
  if (s < 0.15) return Math.abs(yr) > 0.6 ? 'turn in place' : (dir ? 'idle → start' : 'idle');
  const kind = s < 1.5 ? 'walk' : s < 2.4 ? 'jog' : 'sprint';
  if (fwd < -0.3 && Math.abs(lat) < Math.abs(fwd)) return kind + ' backward';
  if (Math.abs(lat) > Math.abs(fwd) * 0.8) return 'strafe ' + (lat > 0 ? 'L' : 'R');
  if (Math.abs(yr) > 1.2) return kind + ' pivot/turn ' + (yr > 0 ? 'L' : 'R');
  if (Math.abs(yr) > 0.35) return kind + ' arc ' + (yr > 0 ? 'L' : 'R');
  return kind + ' ' + s.toFixed(1) + ' m/s';
}

// ---------- pose ----------
export function animateActor(A, t, dt = 1 / 60) {
  const H = A.H, B = H.bones, nb = A.map.length;
  for (let k = 0; k < nb; k++) B[A.map[k]].quaternion.copy(A.baseQ[A.map[k]]);
  H.root.position.copy(A.rootPos);
  if (!DB) return;
  if (A.loopMode) loopPose(A);
  else {
    samplePose(window.__forceFrame ?? A.frame, A.outQ, A.ro, A.con);
    // inertialization offsets decay
    const decay = Math.exp(-dt * LN2 / 0.1);
    for (let k = 0; k < nb; k++) { A.off[k].slerp(_q.identity(), 1 - decay); A.outQ[k].premultiply(A.off[k]); }
    for (let j = 0; j < 3; j++) { A.offRO[j] *= decay; A.ro[j] += A.offRO[j]; }    // keep only the fast part of the stored pelvis offset (it drifts along clips and differs between them)
    if (!A.roAvg) A.roAvg = [A.ro[0], 0, A.ro[2]];
    const kp = 1 - Math.exp(-dt * LN2 / 0.25);
    for (const j of [0, 2]) { if (Math.abs(A.ro[j] - A.roAvg[j]) > 0.12) A.roAvg[j] = A.ro[j]; A.roAvg[j] += (A.ro[j] - A.roAvg[j]) * kp; A.ro[j] -= A.roAvg[j]; }
  }
  attackLayer(A, dt);
  const aw0 = Math.max(A.atkW || 0, A.grdW || 0, A.stW || 0); A.ovW = aw0;
  // weapon layer masks
  const weapon = A.char.cfgWeapon || 'none', names = MM_META.bones;
  for (let k = 0; k < nb; k++) {
    const n = names[k]; let m = 0;
    if (weapon !== 'none' && /(upperarm01|lowerarm01|wrist)\.R/.test(n)) m = (weapon === 'spear' || weapon === 'staff') ? 0.7 : 0.5;
    if (weapon === 'axe' && /(upperarm01|lowerarm01|wrist)\.L/.test(n)) m = 0.5;
    if (weapon === 'shield' && /(upperarm01|lowerarm01|wrist)\.L/.test(n)) m = 0.85;
    if (/wrist/.test(n) && m === 0) m = 0.3;
    if (!(weapon === 'shield' && /\.L$/.test(n))) m *= 1 - aw0; else m *= 1 - (A.bashW || 0);      // a cut takes the arms over completely
    const q = A.outQ[k].clone(); if (m > 0) q.slerp(A.baseQ[A.map[k]], m);
    B[A.map[k]].quaternion.copy(q);
  }
  H.root.position.set(A.rootPos.x + A.ro[0], A.rootPos.y + A.ro[1], A.rootPos.z + A.ro[2]);
  H.root.updateMatrixWorld(true);
  const legsN = (A.limbs.legL ? 1 : 0) + (A.limbs.legR ? 1 : 0);
  if (legsN === 0) crawl(A, dt);
  else {
    if (legsN === 1) hopBody(A, dt);
    pinArms(A);
    if (A.loopMode && !A.noLife) bodyLife(A, t, dt);
    armLean(A);
    relaxArms(A);
    combat(A, dt);
    jointLimits(A);
    footIK(A, dt);
  }
  const wp = A.char.group.userData.weapon; if (wp && wp.userData.orb) { wp.userData.orb.rotation.y = t * 1.4; const s = 1 + 0.08 * Math.sin(t * 3); wp.userData.orb.scale.set(0.8 * s, 1.2 * s, 0.8 * s); }
}

// ---------- missing limbs ----------
// The hero is a shadow in armour; limbs can be torn off. Bones of a missing limb are scaled to nothing, and the
// rest of the body compensates procedurally on top of the matched mocap pose:
//   one arm   - the torso tilts towards the remaining arm
//   one leg   - hopping: the body rises while the remaining foot is in the air, arms go out for balance
//   no legs   - crawling: prone, pulled along by whichever arms are left
export const LIMB_BONE = { head: 'head', armL: 'upperarm01.L', armR: 'upperarm01.R', legL: 'upperleg01.L', legR: 'upperleg01.R' };
export function setLimbs(A, limbs) {
  Object.assign(A.limbs, limbs);
  for (const k in LIMB_BONE) {
    const b = A.H.bone(LIMB_BONE[k]); if (!b) continue; const on = !!A.limbs[k];
    b.scale.setScalar(on ? 1 : 1e-4);
    b.traverse(o => { if (o.isMesh) o.visible = on; });      // rigid gear on that limb (weapon, shield, plume)
  }
  A.feet.L.locked = A.feet.R.locked = false; A.feet.L.rel = A.feet.R.rel = 0;
}
function injuryLabel(A) {
  const L = A.limbs, miss = []; if (!L.head) miss.push('головы'); if (!L.armL) miss.push('л.руки'); if (!L.armR) miss.push('п.руки'); if (!L.legL) miss.push('л.ноги'); if (!L.legR) miss.push('п.ноги');
  if (!miss.length) return '';
  const legs = (L.legL ? 1 : 0) + (L.legR ? 1 : 0);
  return ' · ' + (legs === 0 ? 'ползком' : legs === 1 ? 'на одной ноге' : 'без') + (legs === 2 ? ' ' + miss.join(', ') : '');
}
const groupQ = A => A.char.group.getWorldQuaternion(new THREE.Quaternion());
function aimBone(H, a, b, dirW) {
  const ba = H.bone(a), bb = H.bone(b); if (!ba || !bb) return;
  rotateWorld(ba, new THREE.Quaternion().setFromUnitVectors(P(bb).sub(P(ba)).normalize(), dirW.clone().normalize()));
}
function armLean(A) {
  const L = A.limbs; if (L.armL === L.armR) return;
  const fwd = new THREE.Vector3(0, 0, 1).applyQuaternion(groupQ(A));
  rotateWorld(A.H.bone('spine03'), new THREE.Quaternion().setFromAxisAngle(fwd, L.armL ? -0.08 : 0.08));
}
function hopBody(A, dt) {
  // A real hop cycle: on the ground the leg sinks and loads (the foot travels from front to back under the body), it
  // drives off with the body thrown forward, in the air the leg is pulled up and swung far forward and the arms lift,
  // then it reaches out, lands ahead of the body and gives again.
  const H = A.H, moving = A.vel > 0.15, side = A.limbs.legL ? 1 : -1, gq = groupQ(A);
  A.hopPh = moving ? (A.hopPh + dt * Math.PI * 2 * (1.5 + 0.5 * A.vel)) % (Math.PI * 2) : 0;      // 0..pi in the air, pi..2pi on the ground
  const sn = Math.sin(A.hopPh), cs = Math.cos(A.hopPh), k = moving ? clamp(A.vel / 1.2, 0.4, 1) : 0;
  A.hopH = moving ? (sn > 0 ? 0.19 * k * sn : 0) : 0;
  A.hopDip = moving ? (sn < 0 ? 0.075 * k * -sn : 0) : 0;
  A.hopStride = 0.2 + 0.11 * A.vel;
  H.root.position.y += A.hopH - A.hopDip; H.root.position.x += side * 0.05;      // weight over the remaining foot
  H.root.updateMatrixWorld(true);
  if (!moving) return;
  const X = new THREE.Vector3(1, 0, 0).applyQuaternion(gq), R = (n, ang) => { const b = H.bone(n); if (b) rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(X, ang)); };
  const lean = (0.07 + 0.12 * cs) * k;                  // thrown forward at take-off, upright (a touch back) on landing
  R('spine05', lean * 0.5); R('spine03', lean * 0.5);
  const lift = Math.max(0, sn) * k;                      // the arms swing up with the jump and drop to load the next one
  for (const [s, on] of [['L', A.limbs.armL], ['R', A.limbs.armR]]) if (on) { R('upperarm01.' + s, -0.55 * lift + 0.2 * Math.max(0, -sn) * k); R('lowerarm01.' + s, -0.4 * lift); }
}
function crawl(A, dt) {
  const H = A.H, gq = groupQ(A), L = A.limbs;
  A.crawlPh = (A.crawlPh + dt * A.vel * Math.PI * 2 / 0.5) % (Math.PI * 2);
  const ph = A.crawlPh, X = new THREE.Vector3(1, 0, 0), Y = new THREE.Vector3(0, 1, 0);
  H.root.position.set(A.rootPos.x, 0.23 + 0.015 * Math.sin(ph * 2), A.rootPos.z);
  H.root.quaternion.premultiply(new THREE.Quaternion().setFromAxisAngle(Y, 0.13 * Math.sin(ph))).premultiply(new THREE.Quaternion().setFromAxisAngle(X, 1.2));
  H.root.updateMatrixWorld(true);
  const right = X.clone().applyQuaternion(gq);
  for (const n of ['neck01', 'head']) if (H.bone(n)) rotateWorld(H.bone(n), new THREE.Quaternion().setFromAxisAngle(right, -0.48));   // look ahead
  const W = (x, y, z) => new THREE.Vector3(x, y, z).applyQuaternion(gq);
  for (const [s, sg, on, off] of [['L', 1, L.armL, 0], ['R', -1, L.armR, Math.PI]]) {
    if (!on) continue;
    const r = (Math.sin(ph + off) + 1) / 2;                 // 1 = reaching forward, 0 = pulled back under the chest
    aimBone(H, 'upperarm01.' + s, 'lowerarm01.' + s, W(sg * (0.45 - 0.15 * r), -0.55 + 0.3 * r, 0.2 + 0.8 * r));
    aimBone(H, 'lowerarm01.' + s, 'wrist.' + s, W(-sg * 0.1, -0.9 + 0.4 * r, -0.1 + 0.95 * r));
  }
}

// ---------- body life (procedural layer over the loops) ----------
// What makes the figure read as a person rather than a cut-out:
//   * the torso leans with acceleration: forward on a start, back on a stop or a sharp reversal (with a dip of the
//     pelvis as the legs take the load), into the curve on a turn; it also leans a little with speed
//   * shoulders counter-rotate against the hips with every step; the head stays level and looks into the turn
//   * arms swing with the gait even when they carry a shield or a weapon, elbows bend more in a run
//   * standing still is not frozen: breathing, a slow shift of weight, the head drifting
function bodyLife(A, t, dt) {
  const H = A.H, S = A.sim, gq = groupQ(A), wW = A.wWalk || 0, wR = A.wRun || 0, L = A.limbs;
  const c = Math.cos(A.heading), sn = Math.sin(A.heading);
  // acceleration in the body frame (web: +x = left, +z = forward)
  const aF = S.ax * sn + S.az * c, aL = S.ax * c - S.az * sn, v = Math.hypot(S.vx, S.vz);
  const k = 1 - Math.exp(-dt * LN2 / 0.11);
  const pitchT = clamp(aF * 0.024, -0.2, 0.17), rollT = clamp(aL * 0.02, -0.11, 0.11);
  A.lp = (A.lp || 0) + (pitchT - (A.lp || 0)) * k; A.lr = (A.lr || 0) + (rollT - (A.lr || 0)) * k;
  const dipT = clamp((Math.hypot(S.ax, S.az) - 3) * 0.008, 0, 0.07); A.dip = (A.dip || 0) + (dipT - (A.dip || 0)) * k;
  const lookT = clamp(S.w * 0.1 + wrap(S.h - A.heading) * 0.6, -0.45, 0.45); A.look = (A.look || 0) + (lookT - (A.look || 0)) * (1 - Math.exp(-dt * LN2 / 0.15));
  const X = new THREE.Vector3(1, 0, 0).applyQuaternion(gq), Y = new THREE.Vector3(0, 1, 0), Z = new THREE.Vector3(0, 0, 1).applyQuaternion(gq);
  const rot = (name, axis, ang) => { const b = H.bone(name); if (b && Math.abs(ang) > 1e-5) rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(axis, ang)); };
  const ph = (A.phase || 0) * Math.PI * 2, cs = Math.cos(ph), breath = Math.sin(t * 1.7), idle = 1 - wW;
  // posture: the mocap runner bends 17 degrees forward and throws the head back; bring the trunk to a normal
  // carriage for the gait (upright at rest, about 3 degrees forward walking, 8 running) before adding the leans
  const sp = A.sprint || 0;
  const kw = 1 - (A.ovW || 0);          // during a cut or the guard the captured posture is left alone
  const want = 0.01 + 0.07 * wW + 0.19 * wR + 0.1 * sp;      // 4 degrees forward walking, 12 running, 17 in the sprint: one line from hips to head
  { // the pelvis first (a pelvis tipped forward under an upright chest is what reads as an arched back); the legs keep their pose
    const w = P(H.bone('spine03')).sub(P(H.bone('root'))), d = (want * 0.3 - (Math.atan2(w.dot(Z), w.y) - A.restLower)) * 0.8 * kw;      // the pelvis stays nearly upright: the lean is made by the spine, not by sticking the seat out
    rot('root', X, d); rot('upperleg01.L', X, -d); rot('upperleg01.R', X, -d); }
  { const u = P(H.bone('neck01')).sub(P(H.bone('root'))), cur = Math.atan2(u.dot(Z), u.y) - A.restTrunk;
    const d = (want - cur) * kw; rot('spine05', X, d * 0.5); rot('spine03', X, d * 0.5); }
  // pelvis: dip under load, slow weight shift when standing
  H.root.position.y -= A.dip; H.root.position.x += idle * 0.012 * Math.sin(t * 0.45); H.root.updateMatrixWorld(true);
  // torso: lean (root, lower and upper spine share it), counter-rotation, breathing
  const twist = (0.07 * wW + 0.07 * wR + 0.08 * sp) * cs;
  for (const [n, f] of [['root', 0.3], ['spine05', 0.3], ['spine03', 0.4]]) { rot(n, X, A.lp * f); rot(n, Z, -A.lr * f); }
  rot('spine05', Y, twist * 0.4 + A.look * 0.15); rot('spine03', Y, twist * 0.6 + A.look * 0.2);
  rot('spine03', X, -0.012 * breath - idle * 0.01 * Math.sin(t * 0.6));
  // head: stays level, looks where the body is turning, drifts a little at rest
  rot('neck01', Y, -twist * 0.5 + A.look * 0.3 + idle * 0.02 * Math.sin(t * 0.13)); rot('head', Y, -twist * 0.35 + A.look * 0.35 + idle * 0.02 * Math.sin(t * 0.09 + 1));
  // head: eyes on the path ahead, a touch below level, whatever the trunk does
  { const hb = H.bone('head'); if (hb) { const f = new THREE.Vector3(0, 0, 1).applyQuaternion(hb.getWorldQuaternion(new THREE.Quaternion()));
      const up = Math.asin(clamp(f.y, -1, 1)), d = (up - (-0.07 + idle * 0.01 * Math.sin(t * 0.3))) * 0.9 * kw; rot('neck01', X, d * 0.4); rot('head', X, d * 0.6); } }
  // arms: swing opposite to the legs (phase 0 = left foot lands, so the right arm is forward)
  const weapon = A.char.cfgWeapon || 'none';
  for (const [s, sg, on] of [['L', 1, L.armL], ['R', -1, L.armR]]) {
    if (!on) continue;
    const carried = s === 'L' ? weapon === 'shield' : weapon !== 'none';
    const shield = s === 'L' && weapon === 'shield';
    // free arms already swing in the mocap; carried arms get the swing here (the shield moves less than a weapon hand)
    const amp = carried ? (shield ? 0.07 * wW + 0.1 * wR : 0.2 * wW + 0.3 * wR) : 0.05 * wW + 0.12 * wR;
    const sw = -sg * cs;                                   // +1 = this arm is forward
    rot('upperarm01.' + s, X, -amp * sw + idle * 0.02 * breath);
    rot('lowerarm01.' + s, X, -(carried && !shield ? 0.1 * wW + 0.5 * wR : 0.25 * wR) * Math.max(0, sw) - (shield ? 0 : 0.35 * wR));
    rot('upperarm01.' + s, Z, sg * (0.03 * breath * idle + 0.04 * wR * Math.abs(cs)));
    // run: compact arm action. The elbow stays sharply bent; in front the fist comes up to the chest (towards the
    // breastbone, not reaching forward), behind the elbow goes only a little past the back and the fist stops at the hip
    const aw = Math.min(1, 0.8 * wR + 0.2 * sp) * (shield ? 0.45 : 1) * kw;
    if (aw > 0.01) {
      const u = 0.5 + 0.5 * sw * (carried && !shield ? 0.5 : 1);     // 0 = arm back, 1 = arm in front; the arm that carries the weapon swings half as far
      // behind: the arm is driven well back and opens almost straight (the elbow keeps only a slight bend), away from the ribs
      const a = -0.95 + 1.05 * u, b = -0.7 + 3.1 * u;        // upper arm and forearm angles from straight down, forwards
      const W3 = (x, y, z) => new THREE.Vector3(x, y, z).applyQuaternion(gq);
      const part = (bn, cn, dirW) => { const ba = H.bone(bn), bb = H.bone(cn); if (!ba || !bb) return;
        const q = new THREE.Quaternion().setFromUnitVectors(P(bb).sub(P(ba)).normalize(), dirW.normalize()); rotateWorld(ba, new THREE.Quaternion().slerp(q, aw)); };
      part('upperarm01.' + s, 'lowerarm01.' + s, W3(sg * (0.3 - 0.07 * u), -Math.cos(a), Math.sin(a)));
      part('lowerarm01.' + s, 'wrist.' + s, W3(sg * (0.12 - 0.62 * u), -Math.cos(b), Math.sin(b)));
    }
  }
}

// ---------- combat: two sword cuts and the guard ----------
// The cuts are motion capture retargeted
// to this skeleton and stored at the end of the motion database. 1: from overhead down to the lower left.
// 2: wound up low behind the left side and drawn across to the upper right.
// knots: [game time, clip frame] - the wind-up and the cut are played faster than they were performed.
// cut: the time the edge is travelling. blade: the direction of the blade per clip frame, body frame (x = left, y = up, z = forward).
export const ATTACKS = {
  1: { a: 9945, dur: 0.74, knots: [[0, 2], [0.3, 32], [0.57, 52], [0.74, 62]], cut: [0.4, 0.55], blade: [[0.003,0.417,0.909],[0.003,0.425,0.905],[0.003,0.438,0.899],[0.001,0.458,0.889],[-0.002,0.486,0.874],[-0.006,0.524,0.851],[-0.01,0.572,0.82],[-0.013,0.628,0.778],[-0.015,0.689,0.724],[-0.016,0.754,0.657],[-0.017,0.818,0.575],[-0.017,0.878,0.479],[-0.016,0.928,0.371],[-0.013,0.967,0.254],[-0.009,0.991,0.131],[-0.001,1.0,0.006],[0.01,0.993,-0.118],[0.022,0.971,-0.237],[0.034,0.936,-0.35],[0.044,0.891,-0.452],[0.051,0.838,-0.543],[0.056,0.782,-0.621],[0.059,0.727,-0.684],[0.06,0.677,-0.734],[0.061,0.632,-0.772],[0.06,0.595,-0.802],[0.059,0.564,-0.823],[0.058,0.541,-0.839],[0.058,0.523,-0.851],[0.059,0.508,-0.859],[0.06,0.496,-0.866],[0.061,0.485,-0.872],[0.06,0.475,-0.878],[0.057,0.468,-0.882],[0.048,0.464,-0.885],[0.03,0.466,-0.884],[-0.005,0.48,-0.877],[-0.062,0.516,-0.854],[-0.145,0.583,-0.799],[-0.249,0.686,-0.684],[-0.354,0.808,-0.471],[-0.419,0.898,-0.137],[-0.392,0.874,0.289],[-0.25,0.678,0.691],[-0.044,0.359,0.932],[0.141,0.041,0.989],[0.259,-0.203,0.944],[0.316,-0.371,0.873],[0.336,-0.482,0.809],[0.341,-0.554,0.759],[0.343,-0.597,0.725],[0.342,-0.619,0.707],[0.343,-0.624,0.702],[0.347,-0.616,0.708],[0.355,-0.598,0.719],[0.365,-0.575,0.732],[0.377,-0.549,0.746],[0.389,-0.523,0.758],[0.401,-0.497,0.769],[0.411,-0.474,0.779],[0.418,-0.453,0.787],[0.425,-0.437,0.793],[0.431,-0.423,0.797],[0.436,-0.413,0.8],[0.439,-0.406,0.801],[0.442,-0.402,0.802]] },
  2: { a: 10011, dur: 0.76, knots: [[0, 2], [0.32, 35], [0.59, 53], [0.76, 64]], cut: [0.41, 0.56], blade: [[-0.003,0.412,0.911],[-0.003,0.417,0.909],[-0.005,0.425,0.905],[-0.009,0.438,0.899],[-0.015,0.456,0.89],[-0.022,0.481,0.877],[-0.031,0.515,0.857],[-0.04,0.557,0.83],[-0.048,0.607,0.793],[-0.056,0.664,0.745],[-0.063,0.728,0.683],[-0.069,0.792,0.606],[-0.073,0.855,0.514],[-0.076,0.91,0.407],[-0.079,0.955,0.285],[-0.082,0.985,0.152],[-0.089,0.996,0.011],[-0.1,0.986,-0.132],[-0.117,0.955,-0.271],[-0.137,0.906,-0.401],[-0.158,0.843,-0.515],[-0.176,0.771,-0.612],[-0.191,0.696,-0.692],[-0.202,0.62,-0.758],[-0.21,0.544,-0.813],[-0.215,0.471,-0.856],[-0.214,0.403,-0.89],[-0.206,0.34,-0.918],[-0.189,0.281,-0.941],[-0.162,0.225,-0.961],[-0.126,0.172,-0.977],[-0.082,0.122,-0.989],[-0.031,0.072,-0.997],[0.024,0.018,-1.0],[0.089,-0.046,-0.995],[0.17,-0.12,-0.978],[0.275,-0.203,-0.94],[0.407,-0.286,-0.868],[0.562,-0.358,-0.745],[0.727,-0.408,-0.551],[0.867,-0.419,-0.269],[0.925,-0.368,0.089],[0.859,-0.236,0.455],[0.684,-0.026,0.729],[0.472,0.234,0.85],[0.3,0.495,0.816],[0.204,0.706,0.678],[0.173,0.842,0.51],[0.178,0.913,0.368],[0.194,0.942,0.274],[0.203,0.953,0.226],[0.194,0.958,0.211],[0.17,0.961,0.217],[0.141,0.962,0.234],[0.115,0.96,0.255],[0.096,0.957,0.275],[0.082,0.953,0.292],[0.073,0.949,0.307],[0.069,0.946,0.316],[0.066,0.945,0.322],[0.065,0.943,0.325],[0.063,0.943,0.328],[0.061,0.942,0.331],[0.059,0.941,0.333],[0.057,0.941,0.334],[0.057,0.942,0.332],[0.059,0.943,0.328],[0.06,0.944,0.326]] } };
// 3 and 4 are the same two cuts mirrored (the database keeps a mirrored copy of every frame): overhead down to the lower
// right, and from low behind the right side across to the upper left. With a shield only 1 and 2 are used.
ATTACKS[3] = Object.assign({}, ATTACKS[1], { mirror: true }); ATTACKS[4] = Object.assign({}, ATTACKS[2], { mirror: true });
// guard with the weapon alone (motion capture) - both hands come up in front of the face, the knees give
const GUARD = { a: 10079, n: 22, y0: 0.09 };
const ATK_IN = 0.09, ATK_OUT = 0.2;
export const COMBAT_HOLD = 3;      // seconds the fighting stance is kept after the last cut, block or hit
const _aq = [];
// lays the cut over the pose from locomotion: the whole body when standing, from the waist up when moving
// Procedural attacks of the spear and the staff (no captured motion for them). Keys: time, then in the body frame
// [right, up, forward]: rear (right) upper arm, forearm, leading (left) upper arm, forearm, shaft; torso yaw (to the right +),
// pitch (forward +), pelvis forward (m), pelvis drop (m).
// 5 thrust: the shaft is drawn back level along the right side with the hips turned away, then driven straight out by the
//   legs and the turn of the hips - the lunge carries the body behind the point. 6 sweep: a full turn of the whole body.
ATTACKS[5] = { proc: true, dur: 0.66, cut: [0.26, 0.4], k: [
  [0.0, [0.25, -0.8, 0.3], [0.1, -0.2, 0.97], [-0.3, -0.6, 0.7], [0.25, 0.05, 0.96], [0, 0.03, 1], 0, 0, 0, 0],
  [0.2, [0.3, -0.55, -0.75], [0.05, 0.05, 1], [-0.1, -0.5, 0.85], [0.45, 0.05, 0.9], [0, 0.02, 1], 0.6, -0.05, -0.07, 0.04],
  [0.32, [0.1, -0.15, 0.98], [0, -0.02, 1], [-0.25, -0.35, 0.9], [0.3, -0.1, 0.95], [0, -0.04, 1], -0.5, 0.3, 0.2, 0.1],
  [0.42, [0.1, -0.15, 0.98], [0, -0.02, 1], [-0.25, -0.35, 0.9], [0.3, -0.1, 0.95], [0, -0.04, 1], -0.45, 0.26, 0.17, 0.09],
  [0.66, [0.25, -0.8, 0.3], [0.1, -0.2, 0.97], [-0.3, -0.6, 0.7], [0.25, 0.05, 0.96], [0, 0.03, 1], 0, 0, 0, 0]] };
ATTACKS[6] = { proc: true, dur: 0.86, cut: [0.2, 0.7], around: true, spin: [0.12, 0.74], k: [
  [0.0, [0.3, -0.7, 0.4], [0.3, -0.1, 0.9], [-0.3, -0.6, 0.7], [0.2, 0, 0.98], [0.2, 0.05, 0.98], 0, 0, 0, 0],
  [0.14, [0.75, -0.3, -0.4], [0.95, 0.05, 0.1], [-0.05, -0.45, 0.88], [0.7, 0.05, 0.7], [0.95, 0.08, -0.3], 0.55, 0.05, 0, 0.05],
  [0.4, [0.85, -0.2, 0.3], [0.9, 0.0, 0.42], [-0.1, -0.4, 0.9], [0.6, 0.0, 0.8], [0.92, 0.02, 0.38], 0.1, 0.12, 0, 0.09],
  [0.72, [0.7, -0.3, 0.6], [0.6, -0.05, 0.8], [-0.2, -0.45, 0.87], [0.4, 0, 0.9], [0.75, -0.05, 0.66], -0.35, 0.1, 0, 0.06],
  [0.86, [0.3, -0.7, 0.4], [0.3, -0.1, 0.9], [-0.3, -0.6, 0.7], [0.2, 0, 0.98], [0.2, 0.05, 0.98], 0, 0, 0, 0]] };
// Captured strikes for the spear, the long axe and the shield (free mocap, retargeted; see the pipeline). The spear
// thrusts are lunging strikes with the shaft laid along the striking arm; the swings and chops are two-handed swings of a
// long tool; the shield blows are a straight jab of the shield arm (edge first) and a backhand sweep (mirrored).
ATTACKS[7] = { name: 'spearThrust', a: 10101, dur: 0.842, knots: [[0, 0], [0.382, 19.5], [0.582, 27.0], [0.842, 41]], cut: [0.362, 0.622], reach: 2.3, grip2: true, blade: [[0.055,-0.092,0.994],[0.044,-0.104,0.994],[0.036,-0.105,0.994],[0.028,-0.105,0.994],[0.024,-0.106,0.994],[0.023,-0.106,0.994],[0.026,-0.106,0.994],[0.031,-0.107,0.994],[0.038,-0.107,0.994],[0.048,-0.107,0.993],[0.057,-0.107,0.993],[0.062,-0.107,0.992],[0.064,-0.107,0.992],[0.063,-0.107,0.992],[0.059,-0.107,0.993],[0.052,-0.106,0.993],[0.046,-0.106,0.993],[0.039,-0.105,0.994],[0.034,-0.105,0.994],[0.03,-0.102,0.994],[0.027,-0.078,0.997],[0.026,-0.052,0.998],[0.026,-0.022,0.999],[0.026,0.011,1.0],[0.029,0.045,0.999],[0.031,0.075,0.997],[0.032,0.102,0.994],[0.033,0.122,0.992],[0.032,0.137,0.99],[0.03,0.145,0.989],[0.027,0.146,0.989],[0.023,0.142,0.99],[0.018,0.135,0.991],[0.013,0.125,0.992],[0.009,0.113,0.994],[0.005,0.098,0.995],[0.001,0.08,0.997],[-0.002,0.06,0.998],[-0.005,0.038,0.999],[-0.007,0.015,1.0],[-0.009,-0.009,1.0],[-0.009,-0.032,0.999]] };
ATTACKS[8] = { name: 'spearHigh', a: 10143, dur: 0.842, knots: [[0, 0], [0.382, 19.5], [0.582, 27.0], [0.842, 41]], cut: [0.362, 0.622], reach: 2.3, grip2: true, blade: [[-0.03,0.121,0.992],[-0.042,0.09,0.995],[-0.049,0.059,0.997],[-0.053,0.028,0.998],[-0.052,0.0,0.999],[-0.047,-0.027,0.999],[-0.039,-0.049,0.998],[-0.029,-0.067,0.997],[-0.019,-0.08,0.997],[-0.009,-0.088,0.996],[-0.004,-0.09,0.996],[-0.003,-0.088,0.996],[-0.006,-0.082,0.997],[-0.013,-0.071,0.997],[-0.024,-0.057,0.998],[-0.035,-0.039,0.999],[-0.048,-0.016,0.999],[-0.06,0.011,0.998],[-0.07,0.044,0.997],[-0.075,0.078,0.994],[-0.078,0.115,0.99],[-0.076,0.15,0.986],[-0.071,0.185,0.98],[-0.062,0.217,0.974],[-0.052,0.245,0.968],[-0.043,0.269,0.962],[-0.034,0.291,0.956],[-0.027,0.308,0.951],[-0.022,0.322,0.946],[-0.019,0.332,0.943],[-0.019,0.338,0.941],[-0.02,0.341,0.94],[-0.022,0.341,0.94],[-0.025,0.338,0.941],[-0.029,0.334,0.942],[-0.033,0.327,0.945],[-0.039,0.318,0.947],[-0.046,0.307,0.951],[-0.053,0.293,0.955],[-0.061,0.277,0.959],[-0.068,0.257,0.964],[-0.076,0.233,0.97]] };
ATTACKS[9] = { name: 'spearLunge', a: 10185, dur: 0.815, knots: [[0, 0], [0.265, 13.5], [0.465, 21.0], [0.815, 41]], cut: [0.245, 0.505], reach: 2.3, grip2: true, blade: [[-0.035,-0.105,0.994],[-0.047,-0.105,0.993],[-0.063,-0.104,0.993],[-0.077,-0.083,0.994],[-0.091,-0.053,0.994],[-0.102,-0.019,0.995],[-0.109,0.016,0.994],[-0.111,0.054,0.992],[-0.109,0.088,0.99],[-0.103,0.121,0.987],[-0.093,0.151,0.984],[-0.08,0.176,0.981],[-0.064,0.195,0.979],[-0.045,0.209,0.977],[-0.025,0.216,0.976],[-0.004,0.217,0.976],[0.018,0.209,0.978],[0.039,0.196,0.98],[0.059,0.175,0.983],[0.073,0.149,0.986],[0.08,0.117,0.99],[0.076,0.08,0.994],[0.062,0.043,0.997],[0.039,0.01,0.999],[0.009,-0.018,1.0],[-0.023,-0.04,0.999],[-0.053,-0.056,0.997],[-0.079,-0.065,0.995],[-0.098,-0.074,0.992],[-0.111,-0.079,0.991],[-0.113,-0.083,0.99],[-0.11,-0.086,0.99],[-0.104,-0.087,0.991],[-0.101,-0.085,0.991],[-0.099,-0.081,0.992],[-0.097,-0.076,0.992],[-0.097,-0.071,0.993],[-0.097,-0.067,0.993],[-0.097,-0.063,0.993],[-0.096,-0.058,0.994],[-0.094,-0.053,0.994],[-0.093,-0.045,0.995]] };
ATTACKS[10] = { name: 'spearSwing', a: 10227, dur: 0.842, knots: [[0, 0], [0.382, 19.5], [0.582, 27.0], [0.842, 41]], cut: [0.362, 0.622], reach: 2.3, blade: [[0.093,0.342,0.935],[0.087,0.344,0.935],[0.078,0.347,0.934],[0.068,0.351,0.934],[0.054,0.356,0.933],[0.038,0.361,0.932],[0.018,0.366,0.93],[-0.006,0.372,0.928],[-0.034,0.378,0.925],[-0.069,0.384,0.921],[-0.109,0.391,0.914],[-0.159,0.402,0.902],[-0.224,0.42,0.879],[-0.309,0.446,0.84],[-0.416,0.473,0.777],[-0.54,0.49,0.684],[-0.672,0.486,0.559],[-0.795,0.446,0.412],[-0.894,0.358,0.27],[-0.96,0.22,0.175],[-0.983,0.039,0.181],[-0.933,-0.164,0.321],[-0.747,-0.346,0.568],[-0.411,-0.438,0.8],[-0.041,-0.409,0.912],[0.225,-0.252,0.941],[0.371,0.096,0.924],[0.426,0.561,0.71],[0.44,0.824,0.357],[0.435,0.9,0.02],[0.342,0.887,-0.311],[0.169,0.806,-0.568],[-0.007,0.722,-0.692],[-0.167,0.671,-0.723],[-0.279,0.657,-0.701],[-0.277,0.693,-0.665],[-0.153,0.771,-0.619],[0.045,0.852,-0.522],[0.268,0.901,-0.343],[0.458,0.886,-0.08],[0.559,0.801,0.214],[0.561,0.676,0.477]] };
ATTACKS[11] = { name: 'spearSwing', a: 10227, dur: 0.842, knots: [[0, 0], [0.382, 19.5], [0.582, 27.0], [0.842, 41]], cut: [0.362, 0.622], reach: 2.3, mirror: true, blade: [[0.093,0.342,0.935],[0.087,0.344,0.935],[0.078,0.347,0.934],[0.068,0.351,0.934],[0.054,0.356,0.933],[0.038,0.361,0.932],[0.018,0.366,0.93],[-0.006,0.372,0.928],[-0.034,0.378,0.925],[-0.069,0.384,0.921],[-0.109,0.391,0.914],[-0.159,0.402,0.902],[-0.224,0.42,0.879],[-0.309,0.446,0.84],[-0.416,0.473,0.777],[-0.54,0.49,0.684],[-0.672,0.486,0.559],[-0.795,0.446,0.412],[-0.894,0.358,0.27],[-0.96,0.22,0.175],[-0.983,0.039,0.181],[-0.933,-0.164,0.321],[-0.747,-0.346,0.568],[-0.411,-0.438,0.8],[-0.041,-0.409,0.912],[0.225,-0.252,0.941],[0.371,0.096,0.924],[0.426,0.561,0.71],[0.44,0.824,0.357],[0.435,0.9,0.02],[0.342,0.887,-0.311],[0.169,0.806,-0.568],[-0.007,0.722,-0.692],[-0.167,0.671,-0.723],[-0.279,0.657,-0.701],[-0.277,0.693,-0.665],[-0.153,0.771,-0.619],[0.045,0.852,-0.522],[0.268,0.901,-0.343],[0.458,0.886,-0.08],[0.559,0.801,0.214],[0.561,0.676,0.477]] };
ATTACKS[12] = { name: 'spearOverhead', a: 10269, dur: 0.842, knots: [[0, 0], [0.382, 19.5], [0.582, 27.0], [0.842, 41]], cut: [0.362, 0.622], reach: 2.3, blade: [[0.045,-0.979,0.198],[0.085,-0.97,0.229],[0.124,-0.957,0.261],[0.157,-0.946,0.283],[0.191,-0.937,0.293],[0.224,-0.929,0.294],[0.253,-0.922,0.294],[0.263,-0.914,0.309],[0.23,-0.909,0.347],[0.125,-0.908,0.4],[-0.069,-0.889,0.453],[-0.346,-0.79,0.507],[-0.639,-0.535,0.553],[-0.819,-0.138,0.557],[-0.826,0.28,0.489],[-0.705,0.609,0.364],[-0.531,0.817,0.225],[-0.363,0.926,0.106],[-0.227,0.974,0.018],[-0.127,0.991,-0.027],[-0.067,0.998,-0.005],[-0.04,0.992,0.117],[-0.032,0.926,0.376],[-0.025,0.641,0.767],[-0.025,-0.076,0.997],[-0.031,-0.697,0.717],[-0.024,-0.876,0.482],[-0.021,-0.904,0.427],[-0.027,-0.898,0.44],[-0.039,-0.889,0.456],[-0.053,-0.887,0.458],[-0.067,-0.893,0.446],[-0.08,-0.902,0.425],[-0.093,-0.912,0.401],[-0.102,-0.917,0.385],[-0.111,-0.915,0.387],[-0.122,-0.904,0.41],[-0.139,-0.881,0.452],[-0.161,-0.848,0.506],[-0.177,-0.816,0.551],[-0.177,-0.803,0.568],[-0.166,-0.816,0.554]] };
ATTACKS[13] = { name: 'tomaChop', a: 10311, dur: 0.859, knots: [[0, 0], [0.4, 22.5], [0.6, 30.0], [0.859, 44]], cut: [0.38, 0.64], reach: 1.6, dual: true, blade: [[-0.147,-0.555,0.819],[-0.204,-0.507,0.837],[-0.255,-0.448,0.857],[-0.294,-0.38,0.877],[-0.321,-0.307,0.896],[-0.335,-0.235,0.913],[-0.335,-0.168,0.927],[-0.324,-0.111,0.939],[-0.305,-0.066,0.95],[-0.281,-0.034,0.959],[-0.257,-0.016,0.966],[-0.238,-0.012,0.971],[-0.23,-0.017,0.973],[-0.236,-0.023,0.971],[-0.251,-0.014,0.968],[-0.268,0.023,0.963],[-0.281,0.098,0.955],[-0.279,0.212,0.937],[-0.263,0.356,0.897],[-0.235,0.519,0.822],[-0.198,0.691,0.695],[-0.157,0.852,0.5],[-0.124,0.964,0.236],[-0.13,0.99,-0.057],[-0.225,0.933,-0.28],[-0.444,0.837,-0.319],[-0.718,0.692,-0.068],[-0.791,0.427,0.439],[-0.483,0.114,0.868],[-0.039,-0.056,0.998],[0.298,-0.062,0.953],[0.498,0.007,0.867],[0.614,0.086,0.784],[0.688,0.154,0.709],[0.732,0.206,0.65],[0.753,0.244,0.611],[0.759,0.271,0.592],[0.756,0.289,0.588],[0.746,0.298,0.596],[0.733,0.298,0.612],[0.718,0.287,0.634],[0.703,0.267,0.66],[0.685,0.24,0.688],[0.664,0.206,0.719],[0.638,0.168,0.751]], bladeL: [[0.0,-0.985,0.171],[0.017,-0.984,0.179],[0.039,-0.983,0.181],[0.065,-0.981,0.18],[0.093,-0.98,0.176],[0.125,-0.978,0.17],[0.158,-0.974,0.16],[0.192,-0.97,0.147],[0.225,-0.966,0.131],[0.257,-0.96,0.111],[0.285,-0.955,0.085],[0.304,-0.951,0.052],[0.309,-0.951,0.009],[0.295,-0.954,-0.048],[0.259,-0.958,-0.122],[0.202,-0.957,-0.207],[0.132,-0.947,-0.291],[0.063,-0.931,-0.36],[-0.003,-0.914,-0.406],[-0.077,-0.901,-0.426],[-0.156,-0.893,-0.423],[-0.227,-0.888,-0.401],[-0.275,-0.886,-0.373],[-0.292,-0.889,-0.352],[-0.275,-0.9,-0.339],[-0.224,-0.918,-0.327],[-0.159,-0.94,-0.303],[-0.108,-0.958,-0.264],[-0.087,-0.972,-0.218],[-0.085,-0.98,-0.178],[-0.084,-0.985,-0.147],[-0.078,-0.99,-0.119],[-0.065,-0.994,-0.085],[-0.051,-0.997,-0.05],[-0.044,-0.999,-0.018],[-0.044,-0.999,0.009],[-0.047,-0.998,0.03],[-0.057,-0.997,0.043],[-0.069,-0.996,0.049],[-0.084,-0.995,0.047],[-0.104,-0.994,0.04],[-0.123,-0.992,0.028],[-0.137,-0.99,0.011],[-0.149,-0.989,-0.008],[-0.162,-0.986,-0.027]] };
ATTACKS[14] = { name: 'tomaChop2', a: 10356, dur: 0.859, knots: [[0, 0], [0.4, 22.5], [0.6, 30.0], [0.859, 44]], cut: [0.38, 0.64], reach: 1.6, mirror: true, dual: true, blade: [[-0.331,-0.452,0.828],[-0.341,-0.416,0.843],[-0.346,-0.376,0.859],[-0.346,-0.335,0.876],[-0.339,-0.293,0.894],[-0.328,-0.252,0.91],[-0.317,-0.216,0.924],[-0.306,-0.186,0.934],[-0.298,-0.164,0.94],[-0.296,-0.15,0.943],[-0.303,-0.142,0.942],[-0.318,-0.134,0.939],[-0.34,-0.123,0.932],[-0.369,-0.103,0.924],[-0.402,-0.072,0.913],[-0.432,-0.021,0.902],[-0.449,0.056,0.892],[-0.444,0.16,0.882],[-0.414,0.288,0.864],[-0.361,0.434,0.825],[-0.289,0.594,0.75],[-0.209,0.755,0.621],[-0.135,0.894,0.426],[-0.102,0.978,0.18],[-0.166,0.985,-0.053],[-0.371,0.916,-0.153],[-0.662,0.749,0.001],[-0.802,0.421,0.425],[-0.535,-0.002,0.845],[-0.023,-0.262,0.965],[0.41,-0.271,0.871],[0.665,-0.134,0.734],[0.783,0.032,0.622],[0.829,0.17,0.533],[0.845,0.271,0.461],[0.848,0.341,0.404],[0.846,0.386,0.369],[0.841,0.41,0.354],[0.835,0.418,0.357],[0.828,0.416,0.376],[0.819,0.405,0.406],[0.808,0.389,0.442],[0.797,0.368,0.479],[0.784,0.344,0.517],[0.769,0.32,0.553]], bladeL: [[-0.001,-0.985,0.174],[-0.006,-0.984,0.18],[-0.01,-0.983,0.186],[-0.012,-0.981,0.194],[-0.011,-0.979,0.202],[-0.007,-0.977,0.211],[-0.005,-0.976,0.217],[-0.004,-0.976,0.218],[-0.003,-0.977,0.212],[-0.001,-0.98,0.199],[0.001,-0.984,0.178],[0.003,-0.988,0.151],[0.008,-0.993,0.122],[0.014,-0.996,0.089],[0.021,-0.998,0.053],[0.031,-0.999,0.018],[0.039,-0.999,-0.015],[0.044,-0.998,-0.045],[0.042,-0.997,-0.069],[0.033,-0.996,-0.08],[0.019,-0.997,-0.081],[0.002,-0.997,-0.075],[-0.014,-0.998,-0.065],[-0.027,-0.998,-0.055],[-0.038,-0.998,-0.048],[-0.043,-0.998,-0.037],[-0.032,-0.999,-0.014],[-0.001,-1.0,0.021],[0.036,-0.998,0.055],[0.062,-0.995,0.075],[0.079,-0.993,0.084],[0.1,-0.991,0.092],[0.124,-0.987,0.107],[0.142,-0.982,0.124],[0.15,-0.979,0.14],[0.152,-0.977,0.152],[0.15,-0.976,0.157],[0.15,-0.977,0.154],[0.152,-0.978,0.143],[0.155,-0.98,0.126],[0.16,-0.982,0.104],[0.167,-0.983,0.078],[0.17,-0.984,0.051],[0.165,-0.986,0.027],[0.157,-0.988,0.007]] };
ATTACKS[15] = { name: 'tomaBack', a: 10401, dur: 0.611, knots: [[0, 0], [0.225, 11.5], [0.425, 19.0], [0.611, 29]], cut: [0.205, 0.465], reach: 1.6, dual: true, blade: [[-0.145,-0.89,0.433],[-0.155,-0.899,0.409],[-0.173,-0.901,0.398],[-0.214,-0.883,0.419],[-0.295,-0.823,0.486],[-0.424,-0.692,0.584],[-0.58,-0.469,0.666],[-0.718,-0.185,0.671],[-0.806,0.089,0.585],[-0.835,0.295,0.463],[-0.829,0.419,0.371],[-0.815,0.48,0.324],[-0.807,0.505,0.306],[-0.801,0.514,0.307],[-0.783,0.519,0.343],[-0.718,0.532,0.448],[-0.508,0.574,0.642],[-0.08,0.592,0.802],[0.387,0.523,0.76],[0.723,0.398,0.564],[0.868,0.316,0.383],[0.895,0.315,0.316],[0.879,0.35,0.323],[0.852,0.386,0.353],[0.82,0.415,0.394],[0.77,0.435,0.466],[0.655,0.447,0.609],[0.372,0.456,0.809],[-0.112,0.432,0.895],[-0.523,0.371,0.767]], bladeL: [[-0.001,-0.984,0.176],[-0.012,-0.985,0.172],[-0.02,-0.984,0.177],[-0.022,-0.982,0.188],[-0.015,-0.98,0.197],[0.004,-0.981,0.192],[0.033,-0.982,0.188],[0.069,-0.974,0.213],[0.108,-0.958,0.266],[0.146,-0.938,0.316],[0.182,-0.925,0.335],[0.214,-0.923,0.32],[0.241,-0.93,0.278],[0.26,-0.939,0.224],[0.267,-0.948,0.176],[0.268,-0.951,0.153],[0.271,-0.948,0.168],[0.278,-0.935,0.22],[0.282,-0.909,0.308],[0.274,-0.872,0.406],[0.255,-0.84,0.48],[0.237,-0.825,0.513],[0.226,-0.832,0.507],[0.225,-0.854,0.468],[0.228,-0.883,0.411],[0.227,-0.908,0.353],[0.224,-0.923,0.313],[0.22,-0.928,0.301],[0.217,-0.923,0.319],[0.215,-0.913,0.348]] };
ATTACKS[16] = { name: 'tomaBack', a: 10401, dur: 0.611, knots: [[0, 0], [0.225, 11.5], [0.425, 19.0], [0.611, 29]], cut: [0.205, 0.465], reach: 1.6, mirror: true, dual: true, blade: [[-0.145,-0.89,0.433],[-0.155,-0.899,0.409],[-0.173,-0.901,0.398],[-0.214,-0.883,0.419],[-0.295,-0.823,0.486],[-0.424,-0.692,0.584],[-0.58,-0.469,0.666],[-0.718,-0.185,0.671],[-0.806,0.089,0.585],[-0.835,0.295,0.463],[-0.829,0.419,0.371],[-0.815,0.48,0.324],[-0.807,0.505,0.306],[-0.801,0.514,0.307],[-0.783,0.519,0.343],[-0.718,0.532,0.448],[-0.508,0.574,0.642],[-0.08,0.592,0.802],[0.387,0.523,0.76],[0.723,0.398,0.564],[0.868,0.316,0.383],[0.895,0.315,0.316],[0.879,0.35,0.323],[0.852,0.386,0.353],[0.82,0.415,0.394],[0.77,0.435,0.466],[0.655,0.447,0.609],[0.372,0.456,0.809],[-0.112,0.432,0.895],[-0.523,0.371,0.767]], bladeL: [[-0.001,-0.984,0.176],[-0.012,-0.985,0.172],[-0.02,-0.984,0.177],[-0.022,-0.982,0.188],[-0.015,-0.98,0.197],[0.004,-0.981,0.192],[0.033,-0.982,0.188],[0.069,-0.974,0.213],[0.108,-0.958,0.266],[0.146,-0.938,0.316],[0.182,-0.925,0.335],[0.214,-0.923,0.32],[0.241,-0.93,0.278],[0.26,-0.939,0.224],[0.267,-0.948,0.176],[0.268,-0.951,0.153],[0.271,-0.948,0.168],[0.278,-0.935,0.22],[0.282,-0.909,0.308],[0.274,-0.872,0.406],[0.255,-0.84,0.48],[0.237,-0.825,0.513],[0.226,-0.832,0.507],[0.225,-0.854,0.468],[0.228,-0.883,0.411],[0.227,-0.908,0.353],[0.224,-0.923,0.313],[0.22,-0.928,0.301],[0.217,-0.923,0.319],[0.215,-0.913,0.348]] };
ATTACKS[19] = { name: 'tomaChop2', a: 10356, dur: 0.859, knots: [[0, 0], [0.4, 22.5], [0.6, 30.0], [0.859, 44]], cut: [0.38, 0.64], reach: 1.6, dual: true, blade: [[-0.331,-0.452,0.828],[-0.341,-0.416,0.843],[-0.346,-0.376,0.859],[-0.346,-0.335,0.876],[-0.339,-0.293,0.894],[-0.328,-0.252,0.91],[-0.317,-0.216,0.924],[-0.306,-0.186,0.934],[-0.298,-0.164,0.94],[-0.296,-0.15,0.943],[-0.303,-0.142,0.942],[-0.318,-0.134,0.939],[-0.34,-0.123,0.932],[-0.369,-0.103,0.924],[-0.402,-0.072,0.913],[-0.432,-0.021,0.902],[-0.449,0.056,0.892],[-0.444,0.16,0.882],[-0.414,0.288,0.864],[-0.361,0.434,0.825],[-0.289,0.594,0.75],[-0.209,0.755,0.621],[-0.135,0.894,0.426],[-0.102,0.978,0.18],[-0.166,0.985,-0.053],[-0.371,0.916,-0.153],[-0.662,0.749,0.001],[-0.802,0.421,0.425],[-0.535,-0.002,0.845],[-0.023,-0.262,0.965],[0.41,-0.271,0.871],[0.665,-0.134,0.734],[0.783,0.032,0.622],[0.829,0.17,0.533],[0.845,0.271,0.461],[0.848,0.341,0.404],[0.846,0.386,0.369],[0.841,0.41,0.354],[0.835,0.418,0.357],[0.828,0.416,0.376],[0.819,0.405,0.406],[0.808,0.389,0.442],[0.797,0.368,0.479],[0.784,0.344,0.517],[0.769,0.32,0.553]], bladeL: [[-0.001,-0.985,0.174],[-0.006,-0.984,0.18],[-0.01,-0.983,0.186],[-0.012,-0.981,0.194],[-0.011,-0.979,0.202],[-0.007,-0.977,0.211],[-0.005,-0.976,0.217],[-0.004,-0.976,0.218],[-0.003,-0.977,0.212],[-0.001,-0.98,0.199],[0.001,-0.984,0.178],[0.003,-0.988,0.151],[0.008,-0.993,0.122],[0.014,-0.996,0.089],[0.021,-0.998,0.053],[0.031,-0.999,0.018],[0.039,-0.999,-0.015],[0.044,-0.998,-0.045],[0.042,-0.997,-0.069],[0.033,-0.996,-0.08],[0.019,-0.997,-0.081],[0.002,-0.997,-0.075],[-0.014,-0.998,-0.065],[-0.027,-0.998,-0.055],[-0.038,-0.998,-0.048],[-0.043,-0.998,-0.037],[-0.032,-0.999,-0.014],[-0.001,-1.0,0.021],[0.036,-0.998,0.055],[0.062,-0.995,0.075],[0.079,-0.993,0.084],[0.1,-0.991,0.092],[0.124,-0.987,0.107],[0.142,-0.982,0.124],[0.15,-0.979,0.14],[0.152,-0.977,0.152],[0.15,-0.976,0.157],[0.15,-0.977,0.154],[0.152,-0.978,0.143],[0.155,-0.98,0.126],[0.16,-0.982,0.104],[0.167,-0.983,0.078],[0.17,-0.984,0.051],[0.165,-0.986,0.027],[0.157,-0.988,0.007]] };
ATTACKS[17] = { name: 'shieldStraight', a: 10431, dur: 0.649, knots: [[0, 0], [0.245, 12.5], [0.445, 20.0], [0.649, 31]], cut: [0.225, 0.485], reach: 1.4, bash: true, blade: [[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932]] };
ATTACKS[18] = { name: 'shieldBackhand', a: 10463, dur: 0.611, knots: [[0, 0], [0.225, 11.5], [0.425, 19.0], [0.611, 29]], cut: [0.205, 0.465], reach: 1.4, mirror: true, bash: true, blade: [[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932],[0.201,0.301,0.932]] };
// which attacks a weapon has
export function attackList(weapon) {
  return weapon === 'shield' ? [1, 2, 17, 18] : weapon === 'spear' ? [7, 8, 9, 10, 11, 12, 6] : weapon === 'axe' ? [13, 14, 15, 16, 19] : weapon === 'staff' ? [1, 6, 2, 3, 4] : [1, 2, 3, 4];
}
const CHAIN_T0 = 0.07, CHAIN_BLEND = 0.2, STANCE_FRAME = 2;
const _aq2 = [];
const twoHanded = A => { const w = A.char.cfgWeapon || 'none'; return w !== 'shield' && w !== 'none' && w !== 'axe'; };      // (two tomahawks: one in each hand)
// ---------- idle behaviour ----------
// Standing still and left alone, the hero does something now and then (timers since the last input):
//   5 s  (75 %) looks around - eyes and head lead, the neck and a little of the chest follow, a pause at each side
//   10 s (60 %) shifts his weight from foot to foot (mocap), then lifts a foot behind him and looks down at its sole
//   15 s (20 %) turns to the camera, waves (mocap) and turns back
//   20 s        sits down on the ground (mocap) and stays there, looking round every 4-7 s, until something happens;
//               then he gets up (mocap) before he can move
// The dash plays a crouch-and-spring (mocap leap) over the body.
const IDLE = {"shift": {"a": 10663, "n": 120, "minY": -0.007}, "wave": {"a": 10783, "n": 55, "minY": 0.0}, "sitdown": {"a": 10838, "n": 65, "minY": -0.884}, "getup": {"a": 10903, "n": 65, "minY": -0.884}, "leap": {"a": 10968, "n": 38, "minY": -0.164}, "sitM": {"a": 11006, "n": 2, "minY": -0.784}, "sitF": {"a": 11008, "n": 2, "minY": -0.784}};
const smoother = x => { x = clamp(x, 0, 1); return x * x * x * (x * (x * 6 - 15) + 10); };
export function pokeIdle(A) {
  A.idleT = 0; A.idleDue = [];
  const I = A.idle; if (!I) return;
  if (I.kind === 'sit') { if (I.phase !== 'up') { if (I.phase === 'down') { I.g0 = IDLE.getup.n - 1 - Math.min(IDLE.sitdown.n - 1, I.t * 36); I.p = undefined; } I.phase = 'up'; I.t = 0; } } else I.out = true;
}
export const idleBusy = A => !!(A.idle && A.idle.kind === 'sit');
export function startLeap(A) { A.leap = { t: 0 }; }
function lookYaw(t, T, amp) {           // left, hold, right, hold, back
  const k = [[0, 0], [0.16, amp], [0.36, amp], [0.56, -amp], [0.76, -amp], [1, 0]], u = clamp(t / T, 0, 1); let i = 0; while (i < k.length - 2 && u > k[i + 1][0]) i++;
  return k[i][1] + (k[i + 1][1] - k[i][1]) * smoother((u - k[i][0]) / (k[i + 1][0] - k[i][0]));
}
function idleUpdate(A, dt) {
  const busy = (A.vel || 0) > 0.08 || (A.atk && A.atk.t < A.atk.dur) || (A.guard || 0) > 0.05 || (A.combatT || 0) > 0 || !A.limbs.legL || !A.limbs.legR;
  if (busy && !idleBusy(A)) pokeIdle(A);
  const T0 = A.idleT || 0; A.idleT = T0 + dt; A.idleDue = A.idleDue || [];
  for (const [at, kind, ch] of [[5, 'look', 0.75], [10, 'shift', 0.6], [15, 'wave', 0.2], [20, 'sit', 1]]) if (T0 < at && A.idleT >= at && Math.random() < ch) A.idleDue.push(kind);
  if (!A.idle && A.idleDue.length) { const kind = A.idleDue.shift(); A.idle = { kind, t: 0, w: 0, phase: 'down', nextLook: 4 + Math.random() * 3, look: -1 }; }
  const I = A.idle; A.idleYawW = 0; A.idleLook = 0; A.idleLookP = 0; A.footLift = 0; A.idleClips = []; A.sitW = 0;
  if (I) {
    I.t += dt;
    const fade = I.out ? Math.max(0, (I.w || 0) - dt / 0.25) : 1; I.w = fade;
    if (I.kind === 'look') { A.idleLook = lookYaw(I.t, 4.2, 0.95) * fade; A.idleLookP = -0.06 * Math.abs(A.idleLook); if (I.t > 4.2) A.idle = null; }
    else if (I.kind === 'shift') {
      const cw = Math.min(smoother(I.t / 0.5), 1 - smoother((I.t - 4.0) / 0.5)) * fade;
      if (I.t < 4.5) A.idleClips.push({ a: IDLE.shift.a, fr: Math.min(IDLE.shift.n - 1, I.t * 30), w: cw });
      const lt = I.t - 4.1; A.footLift = (lt > 0 ? Math.min(smoother(lt / 0.6), 1 - smoother((lt - 1.9) / 0.6)) : 0) * fade;
      if (I.t > 6.7) A.idle = null;
    } else if (I.kind === 'wave') {
      A.idleYawW = Math.min(smoother(I.t / 0.7), 1 - smoother((I.t - 2.6) / 0.7)) * fade;
      const wt = I.t - 0.6; if (wt > 0 && wt < IDLE.wave.n / 30) A.idleClips.push({ a: IDLE.wave.a, fr: wt * 30, w: Math.min(smoother(wt / 0.25), 1 - smoother((wt - IDLE.wave.n / 30 + 0.3) / 0.3)) * fade, upper: true });      // the arm and trunk only: the feet stay in the stance
      if (I.t > 3.3) A.idle = null;
    } else if (I.kind === 'sit') {
      // down onto the ground with a hand down (mocap), then the sitting pose - a man cross-legged with his hands on his
      // knees, a woman with her legs folded to one side and a hand on the floor - and up again
      const SD = IDLE.sitdown, GU = IDLE.getup, SA = A.char.cfgSex === 'f' ? IDLE.sitF.a : IDLE.sitM.a;
      if (I.phase === 'down') {
        const fr = Math.min(SD.n - 1, I.t * 36), w = smoother(I.t / 0.3);
        A.idleClips.push({ a: SD.a, fr, w }); A.idleClips.push({ a: SA, fr: 0, w: w * smoother((fr - (SD.n - 26)) / 25) }); A.sitW = w;
        if (fr >= SD.n - 1) { I.phase = 'sit'; I.t = 0; I.p = 0; }
      } else if (I.phase === 'sit') {
        A.idleClips.push({ a: SA, fr: 0, w: 1 }); A.sitW = 1;
        I.nextLook -= dt; if (I.nextLook <= 0 && I.look < 0) I.look = 0;
        if (I.look >= 0) { I.look += dt; A.idleLook = lookYaw(I.look, 2.8, 0.7); if (I.look > 2.8) { I.look = -1; I.nextLook = 4 + Math.random() * 3; } }
      } else {
        const fr = Math.min(GU.n - 1, (I.g0 || 0) + I.t * 34.5), end = fr >= GU.n - 1 ? (I.te = (I.te || 0) + dt) : 0;
        const out = 1 - smoother(end / 0.3);
        if (I.p !== undefined) A.idleClips.push({ a: SA, fr: 0, w: out });
        A.idleClips.push({ a: GU.a, fr, w: (I.p !== undefined ? smoother(I.t / 0.5) : 1) * out });
        A.sitW = (1 - smoother((fr - (GU.n - 30)) / 25)) * out;
        if (end > 0.3) { A.idle = null; A.idleT = 0; }
      }
    }
  }
  if (A.leap) { A.leap.t += dt; const u = A.leap.t / 0.45; if (u >= 1) A.leap = null; else A.leapClip = { a: IDLE.leap.a, fr: 5 + u * 26, w: Math.min(smoother(u / 0.15), 1 - smoother((u - 0.75) / 0.25)) }; }
  if (!A.leap) A.leapClip = null;
}

// Layers over the pose from locomotion, each the whole body when standing and from the waist up when moving:
// the two-handed fighting stance, the guard, the cut.
function attackLayer(A, dt) {
  A.atkW = 0; A.grdW = 0; A.stW = 0; A.spinYaw = 0; A.bashW = 0;
  const nb = DB.nb, names = MM_META.bones, still = 1 - (A.wWalk || 0);
  if (!_aq.length) for (let k = 0; k < nb; k++) { _aq.push(new THREE.Quaternion()); _aq2.push(new THREE.Quaternion()); }
  const lay = (Q, ro, w, yAbs, bash, upper) => { const wl = upper ? 0 : w * still, shield = (A.char.cfgWeapon || 'none') === 'shield';
    for (let k = 0; k < nb; k++) { const n = names[k]; let wk = w; if (n === 'root' || /leg|foot/.test(n)) wk = wl; else if (shield && /\.L$/.test(n) && !bash) wk = 0; A.outQ[k].slerp(Q[k], wk); }
    A.ro[0] += (ro[0] - A.ro[0]) * wl; A.ro[2] += (ro[2] - A.ro[2]) * wl; A.ro[1] += yAbs ? (ro[1] - A.ro[1]) * wl : ro[1] * wl; };
  // stance: with a weapon in both hands the body takes the guard the cuts start from (hilt low in front, point forward)
  const st = A.stance || 0;
  if (st > 0.01 && twoHanded(A) && A.limbs.armR) { const ro = [0, 0, 0]; samplePose(ATTACKS[1].a + STANCE_FRAME, _aq, ro, null); lay(_aq, ro, st, true); A.stW = st; }
  const g = A.guard || 0;
  if (g > 0.01 && (A.char.cfgWeapon || 'none') !== 'shield' && A.limbs.armR) {
    const ro = [0, 0, 0]; samplePose(GUARD.a + g * (GUARD.n - 1), _aq, ro, null); ro[1] -= GUARD.y0; lay(_aq, ro, g, false); A.grdW = g;
  }
  idleUpdate(A, dt);
  for (const c of [...(A.idleClips || []), A.leapClip]) if (c && c.w > 0.001) { const ro = [0, 0, 0]; samplePose(c.a + c.fr, _aq, ro, null); lay(_aq, ro, c.w, true, false, c.upper); A.stW = Math.max(A.stW, c.w); }
  let a = A.atk; if (!a || a.t >= a.dur || !A.limbs.armR) { A.atkQueued = false; return; }
  // the next cut follows straight on if the button is held or was pressed again: no return to the stance in between
  if (a.t >= a.dur - ATK_OUT && (A.atkQueued || A.atkHeld)) { const from = a; A.atkQueued = false; A.atk = null; startAttack(A); a = A.atk;
    if (!from.C.proc && !a.C.proc) { a.from = { base: from.C.a + (from.C.mirror ? MM_META.n : 0), fr: from.fr, C: from.C }; a.t = CHAIN_T0; } }
  a.t += dt; const C = a.C, t = Math.min(a.t, a.dur);
  const sm = x => { x = clamp(x, 0, 1); return x * x * (3 - 2 * x); }, w = Math.min(a.from ? 1 : sm(t / ATK_IN), sm((a.dur - t) / ATK_OUT));
  A.atkW = w;
  if (C.spin) A.spinYaw = Math.PI * 2 * (() => { const u = clamp((t - C.spin[0]) / (C.spin[1] - C.spin[0]), 0, 1); return u * u * u * (u * (u * 6 - 15) + 10); })() % (Math.PI * 2);
  if (C.proc) return;                              // spear and staff moves are posed on the bones in combat()
  const K = C.knots; let i = 0; while (i < K.length - 2 && t > K[i + 1][0]) i++;
  const fr = K[i][1] + (K[i + 1][1] - K[i][1]) * clamp((t - K[i][0]) / (K[i + 1][0] - K[i][0]), 0, 1);
  const ro = [0, 0, 0]; samplePose(C.a + (C.mirror ? MM_META.n : 0) + fr, _aq, ro, null);
  a.mix = 1;
  if (a.from) {                                    // out of the end of the previous cut into this one
    const m = sm((t - CHAIN_T0) / CHAIN_BLEND), ro2 = [0, 0, 0]; a.mix = m;
    if (m < 1) { samplePose(a.from.base + a.from.fr, _aq2, ro2, null); for (let k = 0; k < nb; k++) _aq[k].copy(_aq2[k].slerp(_aq[k], m)); for (let j = 0; j < 3; j++) ro[j] = ro2[j] + (ro[j] - ro2[j]) * m; }
  }
  lay(_aq, ro, w, true, C.bash); a.fr = fr; A.bashW = C.bash ? w : 0;
}
export function startAttack(A) {
  if (A.atk && A.atk.t < A.atk.dur) { A.atkQueued = true; return false; }      // pressed during a cut: the next one is chained on
  if (!A.limbs.armR) return false;
  // Standing: any attack of the weapon but the last one. Running with a blade: sword arm behind -> an overhead cut, in front -> a drawn one.
  const weapon = A.char.cfgWeapon || 'none', list = attackList(weapon), pick = l => l[Math.floor(Math.random() * l.length)];
  let type;
  if ((A.vel || 0) > 0.5 && weapon !== 'spear' && weapon !== 'staff') { const over = Math.cos((A.phase || 0) * Math.PI * 2) <= 0; type = pick(list.filter(x => (x % 2 === 1) === over)); }
  else type = pick(list.filter(x => x !== A.lastAtk));
  A.lastAtk = type; A.atk = { type, C: ATTACKS[type], t: 0, dur: ATTACKS[type].dur, hits: new Set(), fr: 0 }; A.combatT = COMBAT_HOLD;
  return true;
}
function combat(A, dt) {
  const H = A.H, gq = groupQ(A), Y = new THREE.Vector3(0, 1, 0);
  const B = v => new THREE.Vector3(-v[0], v[1], v[2]).applyQuaternion(gq).normalize();      // web: +x is the left side
  const aim = (bn, cn, dirW, w) => { const ba = H.bone(bn), bb = H.bone(cn); if (!ba || !bb || w <= 0) return;
    const q = new THREE.Quaternion().setFromUnitVectors(P(bb).sub(P(ba)).normalize(), dirW); rotateWorld(ba, new THREE.Quaternion().slerp(q, w)); };
  const bladeL = (dirW, w) => { const g = A.char.group.userData.weaponL, wb = H.bone('wrist.L'); if (!g || !wb || w <= 0) return;
    const cur = new THREE.Vector3(0, 1, 0).applyQuaternion(g.getWorldQuaternion(new THREE.Quaternion()));
    rotateWorld(wb, new THREE.Quaternion().slerp(new THREE.Quaternion().setFromUnitVectors(cur, dirW), w)); };
  const blade = (dirW, w) => { const g = A.char.group.userData.weapon, wb = H.bone('wrist.R'); if (!g || !wb || w <= 0) return;
    const cur = new THREE.Vector3(0, 1, 0).applyQuaternion(g.getWorldQuaternion(new THREE.Quaternion()));
    rotateWorld(wb, new THREE.Quaternion().slerp(new THREE.Quaternion().setFromUnitVectors(cur, dirW), w)); };
  // the whole trunk winds up and unwinds; the head keeps looking ahead
  const twist = a => { for (const [n, f] of [['spine05', 0.4], ['spine03', 0.6], ['neck01', -0.45]]) { const b = H.bone(n); if (b) rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(Y, -a * f)); } };
  // fighting stance: kept for a few seconds after the last cut, block or hit. Feet wider than the shoulders (see footIK),
  // knees bent, the back rounded forward, the arms held a little away from the body
  if (A.guardHeld) A.combatT = COMBAT_HOLD;
  A.combatT = Math.max(0, (A.combatT || 0) - dt);
  A.stance = (A.stance || 0) + ((A.combatT > 0 ? 1 : 0) - (A.stance || 0)) * (1 - Math.exp(-dt * LN2 / 0.14));
  spineFix(A);
  if (A.idleLook) { const Yw = new THREE.Vector3(0, 1, 0), Xw = new THREE.Vector3(1, 0, 0).applyQuaternion(gq);
    for (const [n, f] of [['spine03', 0.12], ['neck01', 0.33], ['head', 0.55]]) { const b = H.bone(n); if (b) { rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(Yw, A.idleLook * f)); } }
    const hb = H.bone('head'); if (hb) rotateWorld(hb, new THREE.Quaternion().setFromAxisAngle(Xw, A.idleLookP)); }
  if (A.footLift > 0.001) {     // looking down behind at the sole of the lifted (right) foot
    const fw = A.footLift, Yw = new THREE.Vector3(0, 1, 0), Xw = new THREE.Vector3(1, 0, 0).applyQuaternion(gq);
    H.root.position.x += 0.05 * fw; H.root.updateMatrixWorld(true);
    for (const [n, f] of [['spine05', 0.2], ['spine03', 0.3], ['neck01', 0.5]]) { const b = H.bone(n); if (b) rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(Yw, -0.9 * fw * f)); }
    for (const [n, f] of [['spine03', 0.2], ['neck01', 0.4], ['head', 0.5]]) { const b = H.bone(n); if (b) rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(Xw, 0.7 * fw * f)); }
  }
  const weaponC = A.char.cfgWeapon || 'none', two = twoHanded(A);
  // (with a weapon in both hands the stance is the captured guard, see attackLayer; the posed one below is for shield and bare hands)
  const st = two ? 0 : A.stance * (1 - (A.ovW || 0)), still = 1 - (A.wWalk || 0);
  A.stanceFeet = st * still;
  if (st > 0.01) {
    const X = new THREE.Vector3(1, 0, 0).applyQuaternion(gq), Z = new THREE.Vector3(0, 0, 1).applyQuaternion(gq);
    const R = (n, ax, ang) => { const b = H.bone(n); if (b) rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(ax, ang)); };
    H.root.position.y -= (0.1 * still + 0.02) * st; H.root.updateMatrixWorld(true);
    const hunch = st * (0.6 + 0.4 * still);
    R('spine05', X, 0.1 * hunch); R('spine03', X, 0.12 * hunch); R('neck01', X, -0.15 * hunch);
    for (const [s, sg, on] of [['L', 1, A.limbs.armL], ['R', -1, A.limbs.armR]]) { if (!on) continue; R('upperarm01.' + s, Z, sg * 0.24 * st); R('lowerarm01.' + s, X, -0.3 * st * still); }
  }
  const free = (1 - (A.atkW || 0)) * (1 - (A.grdW || 0));
  // two-handed stance: the point is carried forward, 12 degrees above level
  if (two && (A.stW || 0) > 0.01) blade(B([0, 0.21, 0.98]), A.stW * free);
  const calm = free * (1 - (A.stW || 0));
  if (weaponC === 'spear' && calm > 0.01) blade(B([0, 0.08, 1]), calm);            // carried level at its middle, point ahead
  if (weaponC === 'staff') {
    // walking staff: upright, its foot on the ground, set down again with every stride of the legs; when running (or
    // fighting) the hand slides to the middle and it is carried level like a spear
    const wR = A.wRun || 0, wW = A.wWalk || 0, th = 0.3 * Math.sin((A.phase || 0) * Math.PI * 2) * wW * (1 - wR);
    const dir = [0, Math.cos(th) * (1 - wR) + 0.1 * wR, Math.sin(th) * (1 - wR) + wR];
    if (calm > 0.01) { blade(B(dir), calm); const up = calm * (1 - wR);
      aim('upperarm01.R', 'lowerarm01.R', B([0.2, -0.82, 0.45]), up); aim('lowerarm01.R', 'wrist.R', B([0.05, 0.3, 0.95]), up); blade(B(dir), calm); }
    const sl = A.char.group.userData.weapon && A.char.group.userData.weapon.userData.slide; if (sl) sl.position.y = sl.userData.y0 + 0.5 * Math.max(wR, 1 - calm);
  }
  // in the stance the shield is carried out in front of the body
  if (st > 0.01 && (A.char.cfgWeapon || 'none') === 'shield' && A.limbs.armL) {
    aim('upperarm01.L', 'lowerarm01.L', B([-0.28, -0.72, 0.63]), 0.85 * st); aim('lowerarm01.L', 'wrist.L', B([0.3, 0.2, 0.93]), 0.85 * st);
  }
  // guard
  A.guard = (A.guard || 0) + ((A.guardHeld ? 1 : 0) - (A.guard || 0)) * (1 - Math.exp(-dt * LN2 / 0.06));
  const gw = A.guard, shield = (A.char.cfgWeapon || 'none') === 'shield';
  if (gw > 0.01) {
    if (shield) { H.root.position.y -= 0.05 * gw; H.root.updateMatrixWorld(true); }
    const X = new THREE.Vector3(1, 0, 0).applyQuaternion(gq); for (const n of ['spine05', 'spine03']) { const b = H.bone(n); if (b) rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(X, 0.06 * gw)); }
    if (shield && A.limbs.armL) {
      // the forearm lies across the chest and the shield is turned to stand upright, squarely facing forward
      aim('upperarm01.L', 'lowerarm01.L', B([-0.3, -0.6, 0.74]), gw); aim('lowerarm01.L', 'wrist.L', B([0.8, 0.25, 0.45]), gw);
      const g = A.char.group.userData.shield, fb = H.bone('lowerarm02.L');
      if (g && fb) {
        const gq2 = () => g.getWorldQuaternion(new THREE.Quaternion());
        const q1 = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 0, 1).applyQuaternion(gq2()), B([0, 0.08, 1]));
        rotateWorld(fb, new THREE.Quaternion().slerp(q1, gw));
        const f = new THREE.Vector3(0, 0, 1).applyQuaternion(gq2()), up = new THREE.Vector3(0, 1, 0).applyQuaternion(gq2());
        const want = Y.clone().addScaledVector(f, -Y.dot(f)).normalize(), ang = Math.atan2(new THREE.Vector3().crossVectors(up, want).dot(f), up.dot(want));
        rotateWorld(fb, new THREE.Quaternion().setFromAxisAngle(f, ang * gw));
      }
    }
    else if (A.limbs.armR) blade(B([-0.8, 0.55, 0.2]), gw);        // the body comes from the captured guard; the blade is laid across in front of the face
  }
  // cut: the body is driven by the captured motion (attackLayer); here the blade is set in the hand and leaves its streak
  const a = A.atk; A.plantW = 0; if (!a || a.t >= a.dur || !(A.atkW > 0)) return;
  const C = a.C, g = C.dual && C.mirror ? A.char.group.userData.weaponL : A.char.group.userData.weapon, w = A.atkW;
  if (C.proc) {
    // spear / staff move: posed from keys, one smooth curve through them
    const K = C.k, t = Math.min(a.t, a.dur); let i = 0; while (i < K.length - 2 && t > K[i + 1][0]) i++;
    const u = clamp((t - K[i][0]) / (K[i + 1][0] - K[i][0]), 0, 1), kk = j => K[clamp(j, 0, K.length - 1)];
    const f = (a0, a1, a2, a3) => 0.5 * (2 * a1 + (a2 - a0) * u + (2 * a0 - 5 * a1 + 4 * a2 - a3) * u * u + (3 * a1 - a0 - 3 * a2 + a3) * u * u * u);
    const cr = j => { const p0 = kk(i - 1)[j], p1 = kk(i)[j], p2 = kk(i + 1)[j], p3 = kk(i + 2)[j]; return typeof p1 === 'number' ? f(p0, p1, p2, p3) : [0, 1, 2].map(c => f(p0[c], p1[c], p2[c], p3[c])); };
    const stl = 1 - (A.wWalk || 0) * 0.6, X = new THREE.Vector3(1, 0, 0).applyQuaternion(gq);
    // the legs and hips carry it: the pelvis goes forward and down into the lunge, the trunk turns and leans behind the shaft
    H.root.position.z += cr(8) * w * stl; H.root.position.y -= cr(9) * w; H.root.updateMatrixWorld(true);
    twist(cr(6) * w); for (const n of ['spine05', 'spine03']) { const b = H.bone(n); if (b) rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(X, cr(7) * w * 0.5)); }
    aim('upperarm01.R', 'lowerarm01.R', B(cr(1)), w); aim('lowerarm01.R', 'wrist.R', B(cr(2)), w);
    if (A.limbs.armL) { aim('upperarm01.L', 'lowerarm01.L', B(cr(3)), w); aim('lowerarm01.L', 'wrist.L', B(cr(4)), w); }
    blade(B(cr(5)), w);
  } else {
    const bl = (CC, fr) => { const f0 = Math.min(CC.blade.length - 1, Math.floor(fr)), f1 = Math.min(CC.blade.length - 1, f0 + 1), u = fr - f0; const d = [0, 1, 2].map(c => CC.blade[f0][c] * (1 - u) + CC.blade[f1][c] * u); if (CC.mirror) d[0] = -d[0]; return d; };
    let bd = bl(C, a.fr); if (a.from && a.mix < 1) { const b0 = bl(a.from.C, a.from.fr); bd = bd.map((v, c) => b0[c] + (v - b0[c]) * a.mix); }
    if (C.dual) {
      // two tomahawks: a mirrored strike is made by the left hand, so the blades swap hands
      const L = (CC, fr) => { const B2 = CC.bladeL, f0 = Math.min(B2.length - 1, Math.floor(fr)), f1 = Math.min(B2.length - 1, f0 + 1), u = fr - f0; const d = [0, 1, 2].map(c => B2[f0][c] * (1 - u) + B2[f1][c] * u); if (CC.mirror) d[0] = -d[0]; return d; };
      const bdl = L(C, a.fr), R3 = v => new THREE.Vector3(v[0], v[1], v[2]).applyQuaternion(gq).normalize();
      if (C.mirror) { blade(R3(bdl), w); bladeL(R3(bd), w); } else { blade(R3(bd), w); bladeL(R3(bdl), w); }
    } else blade(new THREE.Vector3(bd[0], bd[1], bd[2]).applyQuaternion(gq).normalize(), w);
  }
  // a thrust is made with both hands on the shaft: the rear (left) hand takes it 40 cm behind the right one
  if (C.grip2 && A.limbs.armL && g) { const dir = new THREE.Vector3(0, 1, 0).applyQuaternion(g.getWorldQuaternion(new THREE.Quaternion())); armIK(A, 'L', P(H.bone('wrist.R')).addScaledVector(dir, -0.4), w); }
  const tipY = weaponC === 'spear' ? 1.05 : weaponC === 'staff' ? 0.9 : weaponC === 'axe' ? 0.42 : 0.8;
  if (g && a.t > C.cut[0] - 0.05 && a.t < C.cut[1] + 0.03) { g.updateWorldMatrix(true, false); (A.trail = A.trail || []).push(new THREE.Vector3(0, tipY, 0).applyMatrix4(g.matrixWorld), new THREE.Vector3(0, tipY * 0.55, 0).applyMatrix4(g.matrixWorld)); }
}

// ---------- two-bone arm IK ----------
// brings the hand of arm s to target (world), blended by w; the elbow keeps bending the way it already bends
function armIK(A, s, target, w) {
  const H = A.H, up = H.bone('upperarm01.' + s), lo = H.bone('lowerarm01.' + s), wr = H.bone('wrist.' + s); if (!up || !lo || !wr || w <= 0.001) return;
  const sp = P(up), ep = P(lo), wp = P(wr), l1 = ep.distanceTo(sp), l2 = wp.distanceTo(ep), tgt = wp.clone().lerp(target, w);
  const toT = tgt.clone().sub(sp), d = clamp(toT.length(), Math.abs(l1 - l2) + 0.01, (l1 + l2) * 0.999); toT.normalize();
  const kd = ep.clone().sub(sp), perp = kd.clone().addScaledVector(toT, -kd.dot(toT)); if (perp.lengthSq() < 1e-8) perp.set(0, -1, 0); perp.normalize();
  const an = Math.acos(clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1));
  const ne = sp.clone().addScaledVector(toT, Math.cos(an) * l1).addScaledVector(perp, Math.sin(an) * l1);
  rotateWorld(up, new THREE.Quaternion().setFromUnitVectors(kd.normalize(), ne.sub(sp).normalize()));
  const ep2 = P(lo), wp2 = P(wr); rotateWorld(lo, new THREE.Quaternion().setFromUnitVectors(wp2.sub(ep2).normalize(), tgt.clone().sub(ep2).normalize()));
}

// ---------- spine ----------
// The back is rounded forward and the pelvis tucked under (no hollow lower back, no seat pushed out): a little at rest,
// clearly in the run and in the fighting stance. The head is brought back up so the eyes stay on the path.
function spineFix(A) {
  const H = A.H, X = new THREE.Vector3(1, 0, 0).applyQuaternion(groupQ(A));
  const f = (0.07 + 0.2 * (A.wRun || 0) + 0.06 * (A.sprint || 0) + 0.12 * (A.stance || 0)) * (1 - 0.6 * (A.atkW || 0));
  const R = (n, ang) => { const b = H.bone(n); if (b) rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(X, ang)); };
  R('root', -0.35 * f); R('upperleg01.L', 0.35 * f); R('upperleg01.R', 0.35 * f);
  R('spine05', 0.6 * f); R('spine03', 0.75 * f); R('neck01', -0.5 * f); R('head', -0.3 * f);
}

// ---------- anatomical joint limits ----------
// Knees and elbows are hinges: they never bend the wrong way and never fold further than a real joint can.
// The head cannot turn or nod further than a neck allows.
function jointLimits(A) {
  const H = A.H, gq = groupQ(A), Z = new THREE.Vector3(0, 0, 1).applyQuaternion(gq);
  const hinge = (an, bn, cn, fwdBlocked, maxFlex) => {
    const ba = H.bone(an), bb = H.bone(bn), bc = H.bone(cn); if (!ba || !bb || !bc) return;
    const pa = P(ba), pb = P(bb), pc = P(bc), t = pb.clone().sub(pa).normalize(), s = pc.clone().sub(pb).normalize();
    let s2 = s.clone();
    if (fwdBlocked) {                       // knee: the shin may fold back, never forwards past the line of the thigh
      const f = Z.clone().addScaledVector(t, -Z.dot(t)); if (f.lengthSq() > 1e-4) { f.normalize(); const c = s2.dot(f); if (c > 0) s2.addScaledVector(f, -c).normalize(); }
    }
    const flex = Math.acos(clamp(s2.dot(t), -1, 1));
    if (flex > maxFlex) { const ax = new THREE.Vector3().crossVectors(t, s2); if (ax.lengthSq() > 1e-8) s2 = t.clone().applyAxisAngle(ax.normalize(), maxFlex); }
    if (s2.distanceToSquared(s) > 1e-8) rotateWorld(bb, new THREE.Quaternion().setFromUnitVectors(s, s2));
  };
  for (const s of ['L', 'R']) {
    if ((A.sitW || 0) < 0.5) hinge('upperleg01.' + s, 'lowerleg01.' + s, 'foot.' + s, true, 2.45);      // knee: 0..140 degrees (let go when sitting cross-legged)
    hinge('upperarm01.' + s, 'lowerarm01.' + s, 'wrist.' + s, false, 2.53);    // elbow: up to 145 degrees
  }
  // neck: the head stays within 70 degrees of turn and 45 degrees of nod or tilt from the chest
  const hb = H.bone('head'), cb = H.bone('spine03');
  if (hb && cb) {
    const rel = cb.getWorldQuaternion(new THREE.Quaternion()).invert().multiply(hb.getWorldQuaternion(new THREE.Quaternion()));
    if (A.headRest) { const d = A.headRest.clone().invert().multiply(rel); const ang = 2 * Math.acos(clamp(Math.abs(d.w), 0, 1));
      if (ang > 1.0) { const fix = new THREE.Quaternion().slerp(d.clone().invert(), 1 - 1.0 / ang); hb.quaternion.multiply(fix); hb.updateMatrixWorld(true); } }
  }
}

// ---------- weapon arms ----------
// An arm that carries something keeps the pose it was built with, relative to the body: fully for the shield
// (it stays in front of the body instead of following the mocap arm swing), mostly for a weapon.
// The whole chain is set in world space, so the mocap collar bone cannot swing the arm out sideways.
const ARM_CHAIN = ['upperarm01', 'upperarm02', 'lowerarm01', 'lowerarm02', 'wrist'];
function pinArms(A) {
  const weapon = A.char.cfgWeapon || 'none'; if (weapon === 'none') return;
  const gq = groupQ(A), H = A.H;
  for (const [s, w] of [['L', weapon === 'shield' ? 1 : weapon === 'axe' ? 0.55 : 0], ['R', weapon === 'spear' || weapon === 'staff' ? 0.8 : 0.55]]) {
    if (w <= 0 || !A.limbs[s === 'L' ? 'armL' : 'armR']) continue;
    const wp = s === 'R' || weapon === 'axe' ? w * (1 - (A.ovW || 0)) : w * (1 - (A.bashW || 0)); if (wp <= 0.001) continue;
    for (const n of ARM_CHAIN) {
      const b = H.bone(n + '.' + s), rel = A.armRel[n + '.' + s]; if (!b || !rel) continue;
      const cur = b.getWorldQuaternion(new THREE.Quaternion()), tgt = gq.clone().multiply(rel);
      cur.slerp(tgt, wp);
      b.quaternion.copy(b.parent.getWorldQuaternion(new THREE.Quaternion()).invert().multiply(cur)); b.updateMatrixWorld(true);
    }
  }
}

// ---------- arms ----------
// The mocap actors hold their arms away from the body (marker suits). Bring a hanging arm back to the torso:
// the sideways angle of the upper arm is limited softly, the forward/back swing is untouched.
const ARM_MAX = 0.45, ARM_SOFT = 0.3, ARM_MIN = 0.2;     // ARM_MIN: the elbows never lie against the trunk
function relaxArms(A) {
  const H = A.H, g = A.char.group, weapon = A.char.cfgWeapon || 'none';
  const gq = g.getWorldQuaternion(new THREE.Quaternion()), gi = gq.clone().invert();
  for (const s of ['L', 'R']) {
    if (s === 'L' && weapon === 'shield') continue;
    if (s === 'R' && weapon !== 'none') continue;
    if (!A.limbs[s === 'L' ? 'armL' : 'armR']) continue;
    const up = H.bone('upperarm01.' + s), el = H.bone('lowerarm01.' + s); if (!up || !el) continue;
    const dW = P(el).sub(P(up)).normalize(), d = dW.clone().applyQuaternion(gi);
    const sign = s === 'L' ? 1 : -1, o = d.x * sign, r = Math.hypot(o, d.y), abd = Math.atan2(o, -d.y);
    const oneLeg = A.limbs.legL !== A.limbs.legR;
    if ((A.ovW || 0) > 0.3) continue;            // a cut or the guard raises the arms on purpose
    if (!oneLeg && abd >= ARM_MIN && abd <= ARM_MAX + 0.13 * (A.wRun || 0)) continue;
    const a2 = oneLeg ? Math.max(abd, 0.55 + 0.12 * Math.sin(A.hopPh)) : abd < ARM_MIN ? ARM_MIN : (ARM_MAX + 0.13 * (A.wRun || 0)) + Math.max(0, abd - ARM_MAX - 0.13 * (A.wRun || 0)) * ARM_SOFT;   // one leg: arms out for balance
    const d2 = new THREE.Vector3(Math.sin(a2) * r * sign, -Math.cos(a2) * r, d.z).normalize().applyQuaternion(gq);
    rotateWorld(up, new THREE.Quaternion().setFromUnitVectors(dW, d2));
  }
}

// ---------- foot IK ----------
function rotateWorld(b, delta) {
  b.getWorldQuaternion(_q); const pw = b.parent.getWorldQuaternion(_q2).invert();
  b.quaternion.copy(pw.multiply(delta).multiply(_q)); b.updateMatrixWorld(true);
}
const P = b => b.getWorldPosition(new THREE.Vector3());
function footIK(A, dt) {
  const H = A.H, g = A.char.group;
  g.updateMatrixWorld(true);
  const scaleY = g.scale.y, groundAnkle = g.position.y + A.restAnkle * scaleY;
  const moving = false;   // foot locking is off: a foot pinned while the body moved on made the knees buckle
  const info = {}; let drop = 0;
  const oneLeg = A.limbs.legL !== A.limbs.legR;
  for (const s of ['L', 'R']) {
    if (!A.limbs[s === 'L' ? 'legL' : 'legR']) { info[s] = null; continue; }
    const hip = H.bone('upperleg01.' + s), knee = H.bone('lowerleg01.' + s), foot = H.bone('foot.' + s), toe = H.bone('toe3-1.' + s);
    const hp = P(hip), kp = P(knee), ap = P(foot);
    const F = A.feet[s], c = A.con[s === 'L' ? 0 : 1];
    if (moving && c > 0.6) {
      if (!F.locked) { F.locked = true; F.pos.set(ap.x, 0, ap.z); }
      F.rel = 1;
    } else { F.locked = false; F.rel = Math.max(0, F.rel - dt / 0.1); }
    // never pull a foot far away from where the animation wants it (prevents stretched / broken legs)
    const dxl = F.pos.x - ap.x, dzl = F.pos.z - ap.z;
    if (F.rel > 0 && dxl * dxl + dzl * dzl > 0.16 * 0.16) { F.locked = false; F.rel = 0; }
    const target = ap.clone();
    if ((A.plantW || 0) > 0.01 && A.plant && A.plant[s]) { target.x += (A.plant[s].x - target.x) * A.plantW; target.z += (A.plant[s].z - target.z) * A.plantW; }
    if (F.rel > 0) { target.x += dxl * F.rel; target.z += dzl * F.rel; }
    else if (A.narrow > 0 && !((A.ovW || 0) > 0.3)) {
      // feet under the hips: the mocap stance is wider than a relaxed one (not while side-stepping)
      const gq = g.getWorldQuaternion(new THREE.Quaternion()), al = ap.clone().sub(g.position).applyQuaternion(gq.clone().invert());
      const lim = Math.abs(hp.clone().sub(g.position).applyQuaternion(gq.clone().invert()).x), ax = Math.abs(al.x);
      let moved = false;
      if (ax > lim) { const nx = Math.sign(al.x) * (lim + (ax - lim) * 0.1); al.x += (nx - al.x) * A.narrow; moved = true; }
      // fighting stance: feet a little wider than the shoulders, the left one half a step ahead
      const sf = A.stanceFeet || 0; if (sf > 0.01) { const sg = s === 'L' ? 1 : -1; al.x += (sg * (lim + 0.21) - al.x) * sf; al.z += sg * 0.1 * sf; moved = true; }
      if (moved) { const w = al.applyQuaternion(gq).add(g.position); target.x = w.x; target.z = w.z; }
    }
    if (oneLeg) {
      // hopping: the foot stays under the body, swings forward in the air and back on the ground
      const gq = groupQ(A), sg = s === 'L' ? 1 : -1, mv = A.vel > 0.15 ? 1 : 0;
      const w = new THREE.Vector3(sg * 0.03, 0, -(A.hopStride || 0.13) * Math.cos(A.hopPh) * mv).applyQuaternion(gq).add(g.position);
      // in the air the foot is pulled up under the body (more than the body rises), on the ground it stays down
      target.set(w.x, groundAnkle + (0.5 * (A.hopH || 0) + 0.14 * Math.max(0, Math.sin(A.hopPh)) * mv) * scaleY, w.z); F.locked = false; F.rel = 0;
    }
    if (s === 'R' && (A.footLift || 0) > 0.001) { const fw = A.footLift, o = new THREE.Vector3(-0.04, 0, -0.2).applyQuaternion(groupQ(A)); target.x += o.x * fw; target.z += o.z * fw; target.y += 0.32 * fw * scaleY; }
    target.y = Math.max(target.y, groundAnkle);
    if ((A.sitW || 0) > 0.001) target.lerp(ap, A.sitW);       // sitting: the legs keep the pose they were given
    const l1 = kp.distanceTo(hp), l2 = ap.distanceTo(kp), over = target.distanceTo(hp) - (l1 + l2) * 0.99;
    if (F.rel > 0.5 && over > 0) drop = Math.max(drop, over);
    info[s] = { hip, knee, foot, toe, target, l1, l2 };
  }
  if (drop > 0) { H.root.position.y -= Math.min(drop, 0.05) / scaleY; H.root.updateMatrixWorld(true); }
  for (const s of ['L', 'R']) {
    if (!info[s]) continue;
    const { hip, knee, foot, toe, target, l1, l2 } = info[s];
    const footQ = foot.getWorldQuaternion(new THREE.Quaternion());
    const hp = P(hip), kp = P(knee), ap = P(foot);
    if (ap.distanceToSquared(target) < 1e-7) continue;
    const toT = target.clone().sub(hp); const d = clamp(toT.length(), Math.abs(l1 - l2) + 0.01, (l1 + l2) * 0.995); toT.normalize();
    // bend plane: current knee offset, biased towards the direction the toes point (knee over toes)
    const kd = kp.clone().sub(hp);
    const perpK = kd.clone().addScaledVector(toT, -kd.dot(toT));
    const tdir = P(toe).sub(ap); const perpT = tdir.addScaledVector(toT, -tdir.dot(toT));
    if (perpT.lengthSq() > 1e-8) perpT.normalize().multiplyScalar(Math.max(perpK.length(), 0.02) * 0.6);
    const perp = perpK.add(perpT);
    // fighting stance: the knees open outwards over the feet (the thighs turn out), not only the shins
    const sf = A.stanceFeet || 0;
    if (sf > 0.01) { const o = new THREE.Vector3(s === 'L' ? 1 : -1, 0, 0.25).applyQuaternion(groupQ(A)); o.addScaledVector(toT, -o.dot(toT)); if (o.lengthSq() > 1e-8) perp.addScaledVector(o.normalize(), Math.max(perp.length(), 0.03) * 1.6 * sf); }
    if (perp.lengthSq() < 1e-10) continue; perp.normalize();
    const a = Math.acos(clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1));
    const newK = hp.clone().addScaledVector(toT, Math.cos(a) * l1).addScaledVector(perp, Math.sin(a) * l1);
    rotateWorld(hip, new THREE.Quaternion().setFromUnitVectors(kd.normalize(), newK.sub(hp).normalize()));
    const kp2 = P(knee), ap2 = P(foot);
    rotateWorld(knee, new THREE.Quaternion().setFromUnitVectors(ap2.sub(kp2).normalize(), target.clone().sub(kp2).normalize()));
    const fpw = foot.parent.getWorldQuaternion(new THREE.Quaternion()).invert();
    foot.quaternion.copy(fpw.multiply(footQ)); foot.updateMatrixWorld(true);
    if (s === 'R' && (A.footLift || 0) > 0.001) rotateWorld(foot, new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0).applyQuaternion(groupQ(A)), 1.1 * A.footLift));     // toes down, the sole turned up behind
    if (sf > 0.01) rotateWorld(foot, new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), (s === 'L' ? 1 : -1) * 0.3 * sf));     // toes turned out
  }
}
