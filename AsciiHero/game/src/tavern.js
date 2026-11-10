// First location: a medieval tavern. Procedural geometry, lights and colliders.
import * as THREE from 'three';

function planksTexture() {
  const W = 512, H = 512, c = document.createElement('canvas'); c.width = W; c.height = H; const x = c.getContext('2d');
  const rows = 8;
  for (let r = 0; r < rows; r++) {
    const y = r * H / rows;
    let xx = -((r * 137) % 200);
    while (xx < W) {
      const len = 180 + ((r * 31 + xx) % 140);
      const v = 150 + ((r * 53 + xx * 7) % 60);
      x.fillStyle = `rgb(${v},${v * 0.78 | 0},${v * 0.55 | 0})`; x.fillRect(xx, y, len, H / rows);
      x.fillStyle = 'rgba(0,0,0,0.75)'; x.fillRect(xx, y, 3, H / rows);
      // grain
      x.fillStyle = 'rgba(60,30,10,0.25)';
      for (let k = 0; k < 6; k++) x.fillRect(xx + 8, y + 6 + k * 9 + (k * 3) % 5, len - 16, 1);
      xx += len;
    }
    x.fillStyle = 'rgba(0,0,0,0.85)'; x.fillRect(0, y, W, 4);
  }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(3, 3);
  return t;
}
function stoneTexture() {
  const S = 256, c = document.createElement('canvas'); c.width = c.height = S; const x = c.getContext('2d');
  x.fillStyle = '#222'; x.fillRect(0, 0, S, S);
  const rows = 6;
  for (let r = 0; r < rows; r++) {
    let xx = (r % 2) * -30;
    while (xx < S) { const w = 50 + ((r * 17 + xx) % 30); const v = 120 + ((r * 41 + xx * 3) % 70); x.fillStyle = `rgb(${v},${v * 0.95 | 0},${v * 0.88 | 0})`; x.fillRect(xx + 3, r * S / rows + 3, w - 6, S / rows - 6); xx += w; }
  }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.wrapS = t.wrapT = THREE.RepeatWrapping; return t;
}

export function buildTavern(mats) {
  const g = new THREE.Group();
  const colliders = []; // {minx,maxx,minz,maxz} | {x,z,r}
  const lights = [];
  const flames = [];
  let cur = g;
  const M = (geo, mat, x, y, z, opt = {}) => {
    const m = new THREE.Mesh(geo, mat); m.position.set(x, y, z);
    if (opt.ry) m.rotation.y = opt.ry; if (opt.rx) m.rotation.x = opt.rx; if (opt.rz) m.rotation.z = opt.rz;
    m.castShadow = opt.cast !== false; m.receiveShadow = true; cur.add(m); return m;
  };
  const B = (w, h, d, mat, x, y, z, opt) => M(new THREE.BoxGeometry(w, h, d), mat, x, y + h / 2, z, opt);
  const C = (r0, r1, h, mat, x, y, z, seg = 14, opt) => M(new THREE.CylinderGeometry(r1, r0, h, seg), mat, x, y + h / 2, z, opt);
  const boxCol = (x, z, w, d) => colliders.push({ minx: x - w / 2, maxx: x + w / 2, minz: z - d / 2, maxz: z + d / 2 });

  // materials (clones with textures)
  const plank = mats.PLANK; plank.map = planksTexture(); plank.color.set(0x8a7058); plank.needsUpdate = true;
  const stoneM = mats.HEARTH; stoneM.map = stoneTexture(); stoneM.map.repeat.set(2, 2); stoneM.needsUpdate = true;

  // ---- room shell ----
  const RX = 5, RZ = 4, RH = 3.2;
  const floorM = mats.FLOOR; floorM.map = plank.map; floorM.color.set(0x8a7864); floorM.needsUpdate = true;
  M(new THREE.PlaneGeometry(RX * 2, RZ * 2), floorM, 0, 0, 0, { rx: -Math.PI / 2, cast: false });
  // walls: each side has a tall version and a low "cut-away" version (shown when the camera is outside that wall)
  const walls = {};
  const mkWall = (key, outward) => { const tall = new THREE.Group(), low = new THREE.Group(); g.add(tall); g.add(low); low.visible = false; walls[key] = { tall, low, n: outward }; return walls[key]; };
  const WN = mkWall('N', new THREE.Vector3(0, 0, -1)), WS = mkWall('S', new THREE.Vector3(0, 0, 1));
  const WW = mkWall('W', new THREE.Vector3(-1, 0, 0)), WE = mkWall('E', new THREE.Vector3(1, 0, 0));
  const wallSet = (W, horiz, c) => {
    // horiz: wall runs along x at z=c ; else along z at x=c
    cur = W.tall;
    if (horiz) { B(RX * 2 + 0.5, RH, 0.25, mats.PLASTER, 0, 0, c); for (const x of [-5, -2.5, 0, 2.5, 5]) B(0.22, RH, 0.12, mats.BEAM, x, 0, c - Math.sign(c) * 0.15); B(RX * 2, 0.2, 0.14, mats.BEAM, 0, 2.3, c - Math.sign(c) * 0.15); B(RX * 2, 0.18, 0.14, mats.BEAM, 0, 0, c - Math.sign(c) * 0.15); }
    else { B(0.25, RH, RZ * 2 + 0.5, mats.PLASTER, c, 0, 0); for (const z of [-4, -1.5, 1.5, 4]) B(0.12, RH, 0.22, mats.BEAM, c - Math.sign(c) * 0.15, 0, z); B(0.14, 0.2, RZ * 2, mats.BEAM, c - Math.sign(c) * 0.15, 2.3, 0); }
    cur = W.low;
    if (horiz) { B(RX * 2 + 0.5, 0.42, 0.25, mats.PLASTER, 0, 0, c); B(RX * 2 + 0.5, 0.08, 0.32, mats.BEAM, 0, 0.42, c); }
    else { B(0.25, 0.42, RZ * 2 + 0.5, mats.PLASTER, c, 0, 0); B(0.32, 0.08, RZ * 2 + 0.5, mats.BEAM, c, 0.42, 0); }
    cur = g;
  };
  wallSet(WN, true, -RZ - 0.125); wallSet(WS, true, RZ + 0.125);
  wallSet(WW, false, -RX - 0.125); wallSet(WE, false, RX + 0.125);
  cur = WN.tall;
  for (const x of [-2.5, 0, 2.5]) M(new THREE.BoxGeometry(0.1, 0.1, 1.8), mats.BEAM, x - 0.6, 1.3, -RZ + 0.05, { rz: 0.7 });

  // windows (moonlight)
  for (const x of [-1.25, 3.75]) {
    B(1.0, 1.1, 0.05, mats.MOON, x, 1.15, -RZ + 0.01, { cast: false });
    B(1.12, 0.08, 0.1, mats.BEAM, x, 1.1, -RZ + 0.05); B(1.12, 0.08, 0.1, mats.BEAM, x, 2.22, -RZ + 0.05);
    B(0.08, 1.12, 0.1, mats.BEAM, x - 0.53, 1.12, -RZ + 0.05); B(0.08, 1.12, 0.1, mats.BEAM, x + 0.53, 1.12, -RZ + 0.05);
    B(0.05, 1.1, 0.08, mats.BEAM, x, 1.12, -RZ + 0.06); B(1.0, 0.05, 0.08, mats.BEAM, x, 1.65, -RZ + 0.06);
  }
  // door (north-west)
  B(1.0, 2.05, 0.08, mats.WOOD, -3.7, 0, -RZ + 0.06);
  for (let k = 0; k < 3; k++) B(1.0, 0.07, 0.1, mats.STEEL, -3.7, 0.35 + k * 0.65, -RZ + 0.09);
  M(new THREE.TorusGeometry(0.06, 0.012, 6, 12), mats.GOLD, -3.35, 1.0, -RZ + 0.13);
  B(1.2, 0.14, 0.14, mats.BEAM, -3.7, 2.05, -RZ + 0.07);

  cur = g;
  // ---- bar counter ----
  const bx = 1.3, bz = -2.35, bw = 4.4;
  B(bw, 1.0, 0.6, plank, bx, 0, bz);
  B(bw + 0.12, 0.07, 0.78, mats.BEAM, bx, 1.0, bz);
  for (let k = 0; k <= 6; k++) B(0.08, 1.0, 0.05, mats.BEAM, bx - bw / 2 + k * bw / 6, 0, bz + 0.31);
  M(new THREE.CylinderGeometry(0.025, 0.025, bw, 8), mats.GOLD, bx, 0.18, bz + 0.45, { rz: Math.PI / 2 });
  boxCol(bx, bz, bw + 0.15, 0.85);
  // stools
  for (let k = 0; k < 4; k++) {
    const sx = bx - 1.5 + k * 1.0, sz = bz + 0.85;
    C(0.19, 0.19, 0.06, mats.WOOD, sx, 0.66, sz);
    for (let l = 0; l < 3; l++) { const a = l * 2.094 + k; M(new THREE.CylinderGeometry(0.022, 0.03, 0.68, 6), mats.WOOD, sx + Math.cos(a) * 0.12, 0.33, sz + Math.sin(a) * 0.12, { rz: Math.cos(a) * 0.12, rx: -Math.sin(a) * 0.12 }); }
    colliders.push({ x: sx, z: sz, r: 0.22 });
  }
  // kegs on the bar
  for (const kx of [bx - 1.6, bx + 1.7]) {
    const keg = M(barrelGeom(0.22, 0.5), mats.WOOD, kx, 1.31, bz - 0.05, { rz: Math.PI / 2 });
    for (const o of [-0.17, 0.17]) M(new THREE.TorusGeometry(0.205, 0.012, 6, 18), mats.STEEL, kx + o, 1.31, bz - 0.05, { ry: Math.PI / 2 });
    C(0.02, 0.02, 0.1, mats.GOLD, kx + 0.27, 1.22, bz - 0.05);
    B(0.3, 0.12, 0.16, mats.BEAM, kx, 1.07, bz - 0.05);
  }
  // mugs on the bar
  for (let k = 0; k < 5; k++) mug(g, mats, bx - 1.0 + k * 0.45 + (k % 2) * 0.1, 1.07, bz + 0.1 - (k % 2) * 0.15);

  // back shelves with bottles
  cur = WN.tall;
  for (const sy of [1.35, 1.85]) {
    B(4.2, 0.05, 0.32, mats.BEAM, bx, sy, -RZ + 0.2);
    for (let k = 0; k < 13; k++) {
      const x = bx - 2.0 + k * 0.32 + ((k * 7) % 3) * 0.03; const h = 0.22 + ((k * 13) % 4) * 0.03;
      const mat = [mats.GLASS, mats.GLASS2, mats.GLASS, mats.GLASS3][(k + (sy > 1.5 ? 1 : 0)) % 4];
      C(0.045, 0.045, h, mat, x, sy + 0.05, -RZ + 0.2, 8, { cast: false });
      C(0.045, 0.014, 0.08, mat, x, sy + 0.05 + h, -RZ + 0.2, 8, { cast: false });
    }
  }
  for (const sx of [bx - 2.1, bx + 2.1]) B(0.07, 1.3, 0.3, mats.BEAM, sx, 1.0, -RZ + 0.2);

  cur = g;
  // barrels in the corner
  const barrels = [[4.35, -3.45, 0], [3.65, -3.45, 0], [4.35, -2.75, 0], [4.0, -3.45, 1]];
  for (const [x, z, up] of barrels) {
    const y = up ? 0.95 : 0;
    M(barrelGeom(0.32, 0.9), mats.WOOD, x, y + 0.45, z);
    for (const o of [-0.3, 0, 0.3]) M(new THREE.TorusGeometry(o === 0 ? 0.335 : 0.305, 0.014, 6, 20), mats.STEEL, x, y + 0.45 + o, z, { rx: Math.PI / 2 });
    if (!up) colliders.push({ x, z, r: 0.36 });
  }

  // ---- fireplace (west wall) ----
  const fx = -RX + 0.45, fz = 0.6;
  B(0.7, 1.4, 0.5, stoneM, fx, 0, fz - 0.85); B(0.7, 1.4, 0.5, stoneM, fx, 0, fz + 0.85);
  cur = WW.tall;
  B(0.8, 0.55, 2.3, stoneM, fx, 1.4, fz);
  B(0.95, 0.12, 2.5, mats.BEAM, fx + 0.05, 1.95, fz);
  B(0.55, 1.15, 1.3, stoneM, fx - 0.05, 2.05, fz);
  cur = g;
  B(0.5, 1.4, 1.2, mats.DARK, fx - 0.22, 0, fz, { cast: false });
  B(0.9, 0.08, 2.0, stoneM, fx + 0.1, 0, fz);
  boxCol(fx, fz, 0.95, 2.4);
  for (let k = 0; k < 3; k++) M(new THREE.CylinderGeometry(0.07, 0.07, 0.75, 8), mats.WOOD, fx - 0.05, 0.16 + (k === 2 ? 0.11 : 0), fz + (k - 1) * 0.12, { rx: Math.PI / 2, ry: (k - 1) * 0.4 });
  for (let k = 0; k < 7; k++) {
    const f = M(new THREE.ConeGeometry(0.09 + (k % 3) * 0.03, 0.45 + (k % 2) * 0.2, 6), mats.FIRE, fx - 0.03 + ((k * 7) % 3 - 1) * 0.06, 0.45, fz + (k - 3) * 0.1, { cast: false });
    f.userData.base = f.position.clone(); f.userData.k = k; flames.push(f);
  }
  const fireLight = new THREE.PointLight(0xff8a30, 14, 8, 2); fireLight.position.set(fx + 0.6, 0.8, fz); g.add(fireLight); lights.push({ l: fireLight, base: 14, kind: 'fire' });
  // crossed swords above the fireplace
  cur = WW.tall;
  for (const s of [-1, 1]) {
    const sw = new THREE.Group();
    sw.add(Object.assign(new THREE.Mesh(new THREE.BoxGeometry(0.02, 0.9, 0.06), mats.STEEL), {}));
    const gd = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.04, 0.22), mats.GOLD); gd.position.y = -0.33; sw.add(gd);
    sw.position.set(fx + 0.3, 2.55, fz); sw.rotation.x = s * 0.7; WW.tall.add(sw);
  }
  const shieldDeco = M(new THREE.CylinderGeometry(0.28, 0.28, 0.04, 20), mats.RED, fx + 0.33, 2.55, fz, { rz: Math.PI / 2 });
  M(new THREE.SphereGeometry(0.06, 10, 6), mats.STEEL, fx + 0.36, 2.55, fz);

  cur = g;
  // ---- tables ----
  const tables = [[-1.6, 1.4], [1.6, 1.2], [-2.0, -1.3], [3.6, 2.0]];
  tables.forEach(([x, z], ti) => {
    C(0.62, 0.62, 0.07, plank, x, 0.72, z, 20);
    C(0.07, 0.08, 0.72, mats.BEAM, x, 0, z, 8);
    C(0.32, 0.32, 0.05, mats.BEAM, x, 0, z, 12);
    colliders.push({ x, z, r: 0.68 });
    // stools around
    for (let k = 0; k < 3; k++) {
      const a = k * 2.094 + ti * 0.7, sx = x + Math.cos(a) * 0.95, sz = z + Math.sin(a) * 0.95;
      C(0.2, 0.2, 0.06, mats.WOOD, sx, 0.44, sz);
      for (let l = 0; l < 3; l++) { const b = l * 2.094 + a; M(new THREE.CylinderGeometry(0.022, 0.028, 0.45, 6), mats.WOOD, sx + Math.cos(b) * 0.12, 0.22, sz + Math.sin(b) * 0.12); }
      colliders.push({ x: sx, z: sz, r: 0.22 });
    }
    mug(g, mats, x + 0.2, 0.79, z - 0.1); mug(g, mats, x - 0.25, 0.79, z + 0.15);
    // plate
    C(0.13, 0.11, 0.015, mats.PLASTER, x + 0.05, 0.79, z + 0.25, 16);
    // candle
    C(0.03, 0.03, 0.14, mats.LINEN, x - 0.05, 0.79, z - 0.25, 8);
    const fl = M(new THREE.ConeGeometry(0.018, 0.05, 6), mats.FIRE, x - 0.05, 0.96, z - 0.25, { cast: false });
    fl.userData.base = fl.position.clone(); fl.userData.k = 10 + ti; flames.push(fl);
    const cl = new THREE.PointLight(0xffb060, 1.5, 2.2, 2); cl.position.set(x - 0.05, 1.05, z - 0.25); g.add(cl); lights.push({ l: cl, base: 1.5, kind: 'candle' });
  });

  // rug
  M(new THREE.PlaneGeometry(3.2, 2.0), mats.GOLD, 0, 0.004, 0.25, { rx: -Math.PI / 2, cast: false });
  M(new THREE.PlaneGeometry(3.0, 1.8), mats.RED, 0, 0.007, 0.25, { rx: -Math.PI / 2, cast: false });

  // stairs up along the east wall
  for (let k = 0; k < 9; k++) B(0.95, 0.24 * (k + 1), 0.32, plank, RX - 0.5, 0, 3.7 - k * 0.32);
  B(0.06, 1.0, 3.0, mats.BEAM, RX - 1.0, 1.1, 2.4, { rx: 0.62 });
  boxCol(RX - 0.5, 2.4, 1.05, 2.95);

  // hanging lanterns
  for (const [x, z] of [[-0.8, 0.0], [2.2, -0.4], [-2.6, 2.6]]) {
    C(0.008, 0.008, 0.8, mats.STEEL, x, 2.4, z, 4, { cast: false });
    B(0.16, 0.22, 0.16, mats.FIRE, x, 2.18, z, { cast: false });
    B(0.2, 0.03, 0.2, mats.STEEL, x, 2.4, z, { cast: false }); B(0.2, 0.03, 0.2, mats.STEEL, x, 2.15, z, { cast: false });
    const l = new THREE.PointLight(0xffa850, 3.4, 4.5, 2); l.position.set(x, 2.1, z); g.add(l); lights.push({ l, base: 3.4, kind: 'lantern' });
  }

  // world bounds
  const bounds = { minx: -RX + 0.32, maxx: RX - 0.32, minz: -RZ + 0.35, maxz: RZ - 0.3 };

  function update(t) {
    for (const L of lights) {
      const f = L.kind === 'fire' ? 0.82 + 0.12 * Math.sin(t * 13.1) + 0.08 * Math.sin(t * 7.3 + 1) + 0.06 * Math.sin(t * 23.7)
        : L.kind === 'candle' ? 0.9 + 0.1 * Math.sin(t * 17 + L.base) : 0.95 + 0.05 * Math.sin(t * 3.1);
      L.l.intensity = L.base * f;
    }
    for (const f of flames) {
      const k = f.userData.k;
      const s = 0.85 + 0.25 * Math.sin(t * (9 + k) + k * 1.7);
      f.scale.set(1, s, 1);
      f.position.x = f.userData.base.x + 0.012 * Math.sin(t * 11 + k);
    }
  }
  function cutaway(camOff) {
    const d = camOff.clone().setY(0).normalize();
    for (const k in walls) { const W = walls[k]; const near = W.n.dot(d) > 0.25; W.tall.visible = !near; W.low.visible = near; }
  }
  return { group: g, colliders, bounds, update, cutaway, spawn: new THREE.Vector3(0.6, 0, 2.8), bartender: new THREE.Vector3(bx + 0.2, 0, -3.25) };
}

function barrelGeom(r, h) {
  const pts = []; for (let k = 0; k <= 10; k++) { const t = k / 10; const y = (t - 0.5) * h; pts.push(new THREE.Vector2(r * (0.86 + 0.14 * Math.sin(t * Math.PI)), y)); }
  pts.unshift(new THREE.Vector2(0, -h / 2)); pts.push(new THREE.Vector2(0, h / 2));
  return new THREE.LatheGeometry(pts, 18);
}
function mug(g, mats, x, y, z) {
  const m = new THREE.Mesh(new THREE.CylinderGeometry(0.045, 0.04, 0.11, 10), mats.WOOD); m.position.set(x, y + 0.055, z); m.castShadow = true; g.add(m);
  const h = new THREE.Mesh(new THREE.TorusGeometry(0.03, 0.008, 5, 10, Math.PI), mats.WOOD); h.position.set(x + 0.045, y + 0.06, z); h.rotation.z = -Math.PI / 2; g.add(h);
  const foam = new THREE.Mesh(new THREE.CylinderGeometry(0.042, 0.042, 0.015, 10), mats.LINEN); foam.position.set(x, y + 0.11, z); g.add(foam);
}
