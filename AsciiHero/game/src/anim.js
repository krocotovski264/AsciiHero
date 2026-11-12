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
  A.heading += wrap(S.h - A.heading) * (1 - Math.exp(-dt * LN2 / 0.03));
  A.speed = v; A.narrow = 1; A.vel = Math.hypot(dx, dz) / Math.max(dt, 1e-4);
  A.label = (v < 0.08 ? 'idle' : v < 1.6 ? 'walk ' + v.toFixed(1) + ' m/s' : 'run ' + v.toFixed(1) + ' m/s') + injuryLabel(A);
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
    if (lockPos) speed = Math.min(speed, 1.7);
    const legs = (A.limbs.legL ? 1 : 0) + (A.limbs.legR ? 1 : 0), arms = (A.limbs.armL ? 1 : 0) + (A.limbs.armR ? 1 : 0);
    if (legs === 1) speed = Math.min(speed, 1.15);                       // hopping on one leg
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
    for (let j = 0; j < 3; j++) { A.offRO[j] *= decay; A.ro[j] += A.offRO[j]; }
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
    armLean(A);
    relaxArms(A);
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
  const H = A.H, moving = A.vel > 0.15, side = A.limbs.legL ? 1 : -1;
  A.hopPh = moving ? (A.hopPh + dt * Math.PI * 2 * (1.7 + 0.5 * A.vel)) % (Math.PI * 2) : 0;
  A.hopH = moving ? 0.10 * Math.max(0, Math.sin(A.hopPh)) : 0;
  H.root.position.y += A.hopH; H.root.position.x += side * 0.05;      // weight over the remaining foot
  H.root.updateMatrixWorld(true);
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

// ---------- weapon arms ----------
// An arm that carries something keeps the pose it was built with, relative to the body: fully for the shield
// (it stays in front of the body instead of following the mocap arm swing), mostly for a weapon.
// The whole chain is set in world space, so the mocap collar bone cannot swing the arm out sideways.
const ARM_CHAIN = ['upperarm01', 'upperarm02', 'lowerarm01', 'lowerarm02', 'wrist'];
function pinArms(A) {
  const weapon = A.char.cfgWeapon || 'none'; if (weapon === 'none') return;
  const gq = groupQ(A), H = A.H;
  for (const [s, w] of [['L', weapon === 'shield' ? 1 : 0], ['R', weapon === 'spear' || weapon === 'staff' ? 0.85 : 0.65]]) {
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
    const a2 = oneLeg ? Math.max(abd, 0.55 + 0.12 * Math.sin(A.hopPh)) : ARM_MAX + (abd - ARM_MAX) * ARM_SOFT;   // one leg: arms out for balance
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
  const moving = !A.noLock && !A.loopMode && A.speed > 0.08;
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
    if (F.rel > 0) { target.x += dxl * F.rel; target.z += dzl * F.rel; }
    else if (A.narrow > 0) {
      // feet under the hips: the mocap stance is wider than a relaxed one (not while side-stepping)
      const gq = g.getWorldQuaternion(new THREE.Quaternion()), al = ap.clone().sub(g.position).applyQuaternion(gq.clone().invert());
      const lim = Math.abs(hp.clone().sub(g.position).applyQuaternion(gq.clone().invert()).x), ax = Math.abs(al.x);
      if (ax > lim) { const nx = Math.sign(al.x) * (lim + (ax - lim) * 0.1); al.x += (nx - al.x) * A.narrow; const w = al.applyQuaternion(gq).add(g.position); target.x = w.x; target.z = w.z; }
    }
    if (oneLeg) {
      // hopping: the foot stays under the body, swings forward in the air and back on the ground
      const gq = groupQ(A), sg = s === 'L' ? 1 : -1, mv = A.vel > 0.15 ? 1 : 0;
      const w = new THREE.Vector3(sg * 0.03, 0, -0.13 * Math.cos(A.hopPh) * mv).applyQuaternion(gq).add(g.position);
      target.set(w.x, groundAnkle + 0.55 * (A.hopH || 0) * scaleY, w.z); F.locked = false; F.rel = 0;
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
    const perp = perpK.add(perpT); if (perp.lengthSq() < 1e-10) continue; perp.normalize();
    const a = Math.acos(clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1));
    const newK = hp.clone().addScaledVector(toT, Math.cos(a) * l1).addScaledVector(perp, Math.sin(a) * l1);
    rotateWorld(hip, new THREE.Quaternion().setFromUnitVectors(kd.normalize(), newK.sub(hp).normalize()));
    const kp2 = P(knee), ap2 = P(foot);
    rotateWorld(knee, new THREE.Quaternion().setFromUnitVectors(ap2.sub(kp2).normalize(), target.clone().sub(kp2).normalize()));
    const fpw = foot.parent.getWorldQuaternion(new THREE.Quaternion()).invert();
    foot.quaternion.copy(fpw.multiply(footQ)); foot.updateMatrixWorld(true);
  }
}
