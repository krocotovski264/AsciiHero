import * as THREE from 'three';
import { loadHuman } from './human.js';
import { buildCharacter } from './character.js';
import { makeActor, animateActor, locomotion, loadMocap } from './anim.js';
globalThis.window = globalThis;
const mats = new Proxy({}, { get: () => new THREE.MeshBasicMaterial() });
const scen = JSON.parse(process.argv[2] || '[]');
(async () => {
  await loadHuman(); await loadMocap();
  const c = buildCharacter({ sex: 'f', build: 1, height: 170, armor: 'cloth', weapon: 'none' }, mats);
  c.cfgWeapon = 'none'; const scene = new THREE.Scene(); scene.add(c.group);
  const A = makeActor(c); A.mode = process.env.MODE || 'loops'; A.noLife = !!process.env.NOLIFE; const pos = { x: 0, z: 0 }; const dt = 1 / 60; let t = 0;
  const H = c.human, P = n => H.bone(n).getWorldPosition(new THREE.Vector3());
  const loc = v => v.clone().sub(c.group.position).applyAxisAngle(new THREE.Vector3(0, 1, 0), -A.heading);
  for (const [name, dx, dz, mag, secs] of scen) {
    const dir = mag > 0 ? [dx / Math.hypot(dx, dz), dz / Math.hypot(dx, dz)] : null;
    const post = { lo: 0, up: 0, head: 0 }; let trans = 0, lastF = A.frame, n = 0, abdL = 0, abdR = 0, abdMax = 0, feet = 0, jit = { w: 0, a: 0, h: 0 }, sp = 0;
    const hist = { w: [], a: [], h: [] };
    for (let i = 0; i < secs * 60; i++) {
      const p0 = { ...pos };
      const [ddx, ddz] = locomotion(A, dt, dir, mag, null, pos, null); pos.x += ddx; pos.z += ddz;
      c.group.position.set(pos.x, 0, pos.z); c.group.rotation.y = A.heading; c.group.updateMatrixWorld(true);
      animateActor(A, t, dt); c.group.updateMatrixWorld(true); t += dt;
      if (Math.abs(A.frame - lastF - dt * 30) > 0.6) trans++; lastF = A.frame;
      if (i < 45) continue;   // settle
      n++; sp += Math.hypot(ddx, ddz) / dt;
      for (const [s, sg] of [['L', 1], ['R', -1]]) {
        const d = loc(P('lowerarm01.' + s)).sub(loc(P('upperarm01.' + s))).normalize(); const a = Math.atan2(d.x * sg, -d.y) * 57.3;
        if (s === 'L') abdL += a; else abdR += a; abdMax = Math.max(abdMax, a);
      }
      feet += loc(P('foot.L')).x - loc(P('foot.R')).x;
      { const r = loc(P('root')), m = loc(P('spine03')), nk = loc(P('neck01'));
        const ang = (u) => Math.atan2(u.z, u.y) * 57.3;       // + = leaning forward
        const lo = ang(m.clone().sub(r)), up = ang(nk.clone().sub(m));
        const hq = H.bone('head').getWorldQuaternion(new THREE.Quaternion()), f = new THREE.Vector3(0, 0, 1).applyQuaternion(hq).applyAxisAngle(new THREE.Vector3(0, 1, 0), -A.heading);
        post.lo += lo; post.up += up; post.head += Math.asin(Math.max(-1, Math.min(1, f.y))) * 57.3; }
      for (const [k, b] of [['w', 'wrist.R'], ['a', 'foot.L'], ['h', 'head']]) {
        const h = hist[k]; h.push(loc(P(b))); if (h.length > 3) h.shift();
        if (h.length === 3) { const j = h[2].clone().sub(h[1]).sub(h[1].clone().sub(h[0])).length(); jit[k] += j; if (k === 'h' && j > (jit.mx || 0)) { jit.mx = j; jit.ph = A.phase; } }
      }
    }
    const f = x => (x / n).toFixed(1);
    console.log(name.padEnd(12), 'v', f(sp), '| trans/s', (trans / secs).toFixed(1), '| armAbd L', f(abdL), 'R', f(abdR), 'max', abdMax.toFixed(0),
      '| feetX cm', f(feet * 100), '| jitter mm wrist', f(jit.w * 1000), 'foot', f(jit.a * 1000), 'head', f(jit.h * 1000), '| lower spine', f(post.lo), 'upper spine', f(post.up), 'head up', f(post.head), '| head max', ((jit.mx||0)*1000).toFixed(1), 'at phase', (jit.ph||0).toFixed(2), '|', A.label);
  }
})();
