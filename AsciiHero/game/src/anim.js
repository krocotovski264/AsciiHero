// Motion Matching locomotion (after Clavet 2015 / D. Holden's open-source Motion-Matching, MIT),
// running on CMU Graphics Lab mocap retargeted to the MakeHuman rig.
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
const LOOPS = { idle: { a: 9019, b: 9064 }, walk: { a: 648, b: 685 }, run: { a: 7678, b: 7700 } };
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
  A.idlePh = ((A.idlePh || 0) + dt / LOOPS.idle.T) % 1;
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
  sampleLoop(LOOPS.idle, A.idlePh || 0, A.outQ, A.ro, A.con);
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
  // weapon layer masks
  const weapon = A.char.cfgWeapon || 'none', names = MM_META.bones;
  for (let k = 0; k < nb; k++) {
    const n = names[k]; let m = 0;
    if (weapon !== 'none' && /(upperarm01|lowerarm01|wrist)\.R/.test(n)) m = (weapon === 'spear' || weapon === 'staff') ? 0.7 : 0.5;
    if (weapon === 'shield' && /(upperarm01|lowerarm01|wrist)\.L/.test(n)) m = 0.85;
    if (/wrist/.test(n) && m === 0) m = 0.3;
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
  const want = 0.01 + 0.07 * wW + 0.19 * wR + 0.1 * sp;      // 4 degrees forward walking, 12 running, 17 in the sprint: one line from hips to head
  { // the pelvis first (a pelvis tipped forward under an upright chest is what reads as an arched back); the legs keep their pose
    const w = P(H.bone('spine03')).sub(P(H.bone('root'))), d = (want - (Math.atan2(w.dot(Z), w.y) - A.restLower)) * 0.8;
    rot('root', X, d); rot('upperleg01.L', X, -d); rot('upperleg01.R', X, -d); }
  { const u = P(H.bone('neck01')).sub(P(H.bone('root'))), cur = Math.atan2(u.dot(Z), u.y) - A.restTrunk;
    const d = want - cur; rot('spine05', X, d * 0.5); rot('spine03', X, d * 0.5); }
  // pelvis: dip under load, slow weight shift when standing
  H.root.position.y -= A.dip; H.root.position.x += idle * 0.012 * Math.sin(t * 0.45); H.root.updateMatrixWorld(true);
  // torso: lean (root, lower and upper spine share it), counter-rotation, breathing
  const twist = (0.07 * wW + 0.07 * wR + 0.08 * sp) * cs;
  for (const [n, f] of [['root', 0.3], ['spine05', 0.3], ['spine03', 0.4]]) { rot(n, X, A.lp * f); rot(n, Z, -A.lr * f); }
  rot('spine05', Y, twist * 0.4 + A.look * 0.15); rot('spine03', Y, twist * 0.6 + A.look * 0.2);
  rot('spine03', X, -0.012 * breath - idle * 0.01 * Math.sin(t * 0.6));
  // head: stays level, looks where the body is turning, drifts a little at rest
  rot('neck01', Y, -twist * 0.5 + A.look * 0.3 + idle * 0.06 * Math.sin(t * 0.37)); rot('head', Y, -twist * 0.35 + A.look * 0.35 + idle * 0.05 * Math.sin(t * 0.23 + 1));
  // head: eyes on the path ahead, a touch below level, whatever the trunk does
  { const hb = H.bone('head'); if (hb) { const f = new THREE.Vector3(0, 0, 1).applyQuaternion(hb.getWorldQuaternion(new THREE.Quaternion()));
      const up = Math.asin(clamp(f.y, -1, 1)), d = (up - (-0.07 + idle * 0.02 * Math.sin(t * 0.8))) * 0.9; rot('neck01', X, d * 0.4); rot('head', X, d * 0.6); } }
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
    const aw = Math.min(1, 0.8 * wR + 0.2 * sp) * (shield ? 0.45 : 1);
    if (aw > 0.01) {
      const u = 0.5 + 0.5 * sw;                              // 0 = arm back, 1 = arm in front
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
// Directions are in the body frame as [right, up, forward]. Each key: time, upper arm, forearm, blade, torso yaw (to the right +).
// 1: from the upper right down to the lower left.  2: flat, from the left to the right.
// A cut, not a chop: the hand leads and the blade trails behind it, the edge is drawn through the target in one long
// arc that never stops at the "hit" - it keeps accelerating through and only then runs out at the far hip or shoulder.
// After the yaw come the lean of the trunk to the side (to the right +) and forwards (+): the body goes with the sword -
// winding up it leans the way it turns, cutting it throws its weight across to the other side.
export const ATTACKS = {
  1: { dur: 0.74, cut: [0.27, 0.54], k: [[0, null],
        [0.2, [0.55, 0.7, -0.45], [0.1, 0.75, -0.65], [0.35, 0.1, -0.95], 0.85, 0.22, -0.12],
        [0.3, [0.5, 0.5, 0.65], [0.25, 0.8, 0.45], [0.5, 0.75, -0.5], 0.5, 0.14, -0.06],
        [0.37, [0.25, 0.1, 0.95], [0, 0.1, 1], [-0.1, 0.7, 0.7], 0, 0, 0.05],
        [0.44, [-0.15, -0.4, 0.9], [-0.55, -0.35, 0.75], [-0.55, 0.05, 0.83], -0.5, -0.15, 0.14],
        [0.52, [-0.45, -0.75, 0.4], [-0.85, -0.45, -0.1], [-0.6, -0.5, -0.65], -0.85, -0.25, 0.18], [0.74, null]] },
  2: { dur: 0.72, cut: [0.27, 0.54], k: [[0, null],
        [0.2, [-0.7, -0.05, 0.55], [-0.9, 0.25, -0.3], [-0.3, 0.25, -0.95], -0.85, -0.22, 0.12],
        [0.3, [-0.3, -0.12, 0.95], [-0.7, 0.1, 0.7], [-0.95, 0.15, -0.15], -0.5, -0.14, 0.08],
        [0.37, [0.1, -0.1, 1], [0.05, 0.05, 1], [-0.45, 0.1, 0.9], 0, 0, 0],
        [0.44, [0.6, -0.12, 0.8], [0.7, 0, 0.7], [0.35, 0.1, 0.93], 0.5, 0.15, -0.08],
        [0.52, [0.95, -0.15, -0.05], [0.8, 0, -0.6], [0.45, 0.1, -0.9], 0.85, 0.25, -0.15], [0.72, null]] } };
export const COMBAT_HOLD = 3;      // seconds the fighting stance is kept after the last cut, block or hit
// starts a cut if the previous one is over (the cooldown is the animation itself). Standing: a random cut, then they
// alternate. Running: the cut that continues the arm's motion - sword arm behind -> 1 (down from above), in front -> 2 (flat).
export function startAttack(A) {
  if (A.atk && A.atk.t < A.atk.dur) return false;
  if (!A.limbs.armR) return false;
  let type;
  if ((A.vel || 0) > 0.5) type = Math.cos((A.phase || 0) * Math.PI * 2) > 0 ? 2 : 1;
  else type = A.lastAtk ? 3 - A.lastAtk : (Math.random() < 0.5 ? 1 : 2);
  A.lastAtk = type; A.atk = { type, t: 0, dur: ATTACKS[type].dur, hits: new Set() }; A.combatT = COMBAT_HOLD;
  return true;
}
function combat(A, dt) {
  const H = A.H, gq = groupQ(A), Y = new THREE.Vector3(0, 1, 0);
  const B = v => new THREE.Vector3(-v[0], v[1], v[2]).applyQuaternion(gq).normalize();      // web: +x is the left side
  const aim = (bn, cn, dirW, w) => { const ba = H.bone(bn), bb = H.bone(cn); if (!ba || !bb || w <= 0) return;
    const q = new THREE.Quaternion().setFromUnitVectors(P(bb).sub(P(ba)).normalize(), dirW); rotateWorld(ba, new THREE.Quaternion().slerp(q, w)); };
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
  const st = A.stance, still = 1 - (A.wWalk || 0);
  A.stanceFeet = st * still;
  if (st > 0.01) {
    const X = new THREE.Vector3(1, 0, 0).applyQuaternion(gq), Z = new THREE.Vector3(0, 0, 1).applyQuaternion(gq);
    const R = (n, ax, ang) => { const b = H.bone(n); if (b) rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(ax, ang)); };
    H.root.position.y -= (0.1 * still + 0.02) * st; H.root.updateMatrixWorld(true);
    // the whole trunk tips forward from the hips (the legs keep their place), then the back rounds over
    R('root', X, 0.13 * st); R('upperleg01.L', X, -0.13 * st); R('upperleg01.R', X, -0.13 * st);
    const hunch = st * (0.6 + 0.4 * still);
    R('spine05', X, 0.15 * hunch); R('spine03', X, 0.2 * hunch); R('neck01', X, -0.3 * hunch);
    for (const [s, sg, on] of [['L', 1, A.limbs.armL], ['R', -1, A.limbs.armR]]) { if (!on) continue; R('upperarm01.' + s, Z, sg * 0.24 * st); R('lowerarm01.' + s, X, -0.3 * st * still); }
  }
  // in the stance the shield is carried out in front of the body
  if (st > 0.01 && (A.char.cfgWeapon || 'none') === 'shield' && A.limbs.armL) {
    aim('upperarm01.L', 'lowerarm01.L', B([-0.28, -0.72, 0.63]), 0.85 * st); aim('lowerarm01.L', 'wrist.L', B([0.3, 0.2, 0.93]), 0.85 * st);
  }
  // guard
  A.guard = (A.guard || 0) + ((A.guardHeld ? 1 : 0) - (A.guard || 0)) * (1 - Math.exp(-dt * LN2 / 0.06));
  const gw = A.guard, shield = (A.char.cfgWeapon || 'none') === 'shield';
  if (gw > 0.01) {
    H.root.position.y -= 0.05 * gw; H.root.updateMatrixWorld(true);
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
    else if (A.limbs.armR) { aim('upperarm01.R', 'lowerarm01.R', B([0.15, -0.55, 0.82]), gw); aim('lowerarm01.R', 'wrist.R', B([-0.5, 0.45, 0.74]), gw); blade(B([-0.6, 0.78, 0.15]), gw); }
  }
  // cut. The power comes from the ground up, link after link, each a little later than the one below it:
  // the legs shift the weight, the hips turn first, the chest follows them, then the shoulder and the arm, and the
  // wrist with the blade comes through last. So the pelvis is evaluated slightly ahead of the chest and the arm.
  const a = A.atk; if (!a || a.t >= a.dur || !A.limbs.armR) { A.plantW = 0; return; }
  a.t += dt; const K = ATTACKS[a.type].k;
  const at = tt => { let i = 0; while (i < K.length - 2 && tt > K[i + 1][0]) i++;
    const k0 = K[i], k1 = K[i + 1], u = clamp((tt - k0[0]) / (k1[0] - k0[0]), 0, 1), s = u * u * (3 - 2 * u);
    // entering and leaving the cut the pose is blended with the body's own; between the keys everything follows one
    // smooth curve through them (Catmull-Rom), so the arm sweeps through without stopping anywhere
    const w = k0[1] && k1[1] ? 1 : k1[1] ? s : 1 - s, on = j => K[clamp(j, 1, K.length - 2)];
    const cr = j => { if (!k0[1] || !k1[1]) return (k1[1] ? k1 : k0)[j];
      const p0 = on(i - 1)[j], p1 = k0[j], p2 = k1[j], p3 = on(i + 2)[j], f = (a0, a1, a2, a3) => 0.5 * (2 * a1 + (a2 - a0) * u + (2 * a0 - 5 * a1 + 4 * a2 - a3) * u * u + (3 * a1 - a0 - 3 * a2 + a3) * u * u * u);
      return typeof p1 === 'number' ? f(p0, p1, p2, p3) : [0, 1, 2].map(c => f(p0[c], p1[c], p2[c], p3[c])); };
    return { w, cr }; };
  const now = at(a.t), lead = at(Math.min(a.dur, a.t + 0.055)), w = now.w, cr = now.cr;
  const stl = 1 - (A.wWalk || 0) * 0.6, yawN = clamp(cr(4) / 0.85, -1, 1), hipYaw = lead.cr(4) * lead.w * 0.42 * stl;
  // feet stay where they were while the pelvis turns over them (footIK re-plants them): the legs take the twist
  A.plant = {}; for (const s of ['L', 'R']) { const fb = H.bone('foot.' + s); if (fb) A.plant[s] = P(fb); }
  A.plantW = w * (1 - (A.wWalk || 0));
  { const rb = H.bone('root'); if (rb) rotateWorld(rb, new THREE.Quaternion().setFromAxisAngle(Y, -hipYaw)); }
  // weight: back and onto the rear side while winding up, then driven forward and across; the knees give as the cut lands
  const through = 1 - yawN * yawN;
  H.root.position.x -= 0.05 * yawN * w * stl; H.root.position.z += (0.25 * cr(6) + 0.045 * through) * w * stl; H.root.position.y -= (0.015 + 0.035 * through) * w;
  H.root.updateMatrixWorld(true);
  twist(cr(4) * w * 0.75);
  { const Zb = new THREE.Vector3(0, 0, 1).applyQuaternion(gq), Xb = new THREE.Vector3(1, 0, 0).applyQuaternion(gq), roll = cr(5) * w, pitch = cr(6) * w;
    for (const n of ['spine05', 'spine03']) { const b = H.bone(n); if (b) { rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(Zb, roll * 0.5)); rotateWorld(b, new THREE.Quaternion().setFromAxisAngle(Xb, pitch * 0.5)); } } }
  aim('upperarm01.R', 'lowerarm01.R', B(cr(1)), w); aim('lowerarm01.R', 'wrist.R', B(cr(2)), w); blade(B(cr(3)), w);
  // the free arm balances the sword arm: forward while the sword is drawn back, thrown back and out as the cut goes through
  if (A.limbs.armL && (A.char.cfgWeapon || 'none') !== 'shield') { aim('upperarm01.L', 'lowerarm01.L', B([-0.4, -0.78, 0.05 + 0.45 * yawN]), 0.7 * w); aim('lowerarm01.L', 'wrist.L', B([-0.15, -0.55, 0.6 + 0.3 * yawN]), 0.6 * w); }
  // while the edge is travelling it leaves a streak in the air
  const g = A.char.group.userData.weapon, C = ATTACKS[a.type].cut;
  if (g && a.t > C[0] && a.t < C[1]) { g.updateWorldMatrix(true, false); (A.trail = A.trail || []).push(new THREE.Vector3(0, 0.8, 0).applyMatrix4(g.matrixWorld), new THREE.Vector3(0, 0.45, 0).applyMatrix4(g.matrixWorld)); }
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
    hinge('upperleg01.' + s, 'lowerleg01.' + s, 'foot.' + s, true, 2.45);      // knee: 0..140 degrees
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
  for (const [s, w] of [['L', weapon === 'shield' ? 1 : 0], ['R', weapon === 'spear' || weapon === 'staff' ? 0.8 : 0.55]]) {
    if (w <= 0 || !A.limbs[s === 'L' ? 'armL' : 'armR']) continue;
    for (const n of ARM_CHAIN) {
      const b = H.bone(n + '.' + s), rel = A.armRel[n + '.' + s]; if (!b || !rel) continue;
      const cur = b.getWorldQuaternion(new THREE.Quaternion()), tgt = gq.clone().multiply(rel);
      cur.slerp(tgt, w);
      b.quaternion.copy(b.parent.getWorldQuaternion(new THREE.Quaternion()).invert().multiply(cur)); b.updateMatrixWorld(true);
    }
  }
}

// ---------- arms ----------
// The mocap actors hold their arms away from the body (marker suits). Bring a hanging arm back to the torso:
// the sideways angle of the upper arm is limited softly, the forward/back swing is untouched.
const ARM_MAX = 0.13, ARM_SOFT = 0.12;
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
    if (!oneLeg && abd <= ARM_MAX) continue;
    const a2 = oneLeg ? Math.max(abd, 0.55 + 0.12 * Math.sin(A.hopPh)) : (ARM_MAX + 0.13 * (A.wRun || 0)) + Math.max(0, abd - ARM_MAX - 0.13 * (A.wRun || 0)) * ARM_SOFT;   // one leg: arms out for balance
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
    else if (A.narrow > 0) {
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
    target.y = Math.max(target.y, groundAnkle);
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
    if (sf > 0.01) rotateWorld(foot, new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), (s === 'L' ? 1 : -1) * 0.3 * sf));     // toes turned out
  }
}
