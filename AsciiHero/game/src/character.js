// Assemble a full character: MakeHuman body + body-fitted garments + rigid gear + weapon.
import * as THREE from 'three';
import { Human, MAT } from './human.js';
import { applyPose, handFrameWorld } from './pose.js';

export const BUILDS = ['Худое', 'Обычное', 'Атлетичное', 'Крупное'];
export const ARMORS = [
  { id: 'cloth', name: 'Рубаха путника', desc: 'Льняная рубаха, штаны, сапоги' },
  { id: 'leather', name: 'Кожаная броня', desc: 'Дублёная кожа, наручи, наплечники' },
  { id: 'chain', name: 'Кольчуга', desc: 'Хауберк, кольчужный капюшон, табард' },
  { id: 'plate', name: 'Рыцарские латы', desc: 'Полный доспех, шлем с плюмажем' },
  { id: 'robe', name: 'Мантия мага', desc: 'Длинная мантия с капюшоном' },
];
export const WEAPONS = [
  { id: 'none', name: 'Без оружия', desc: 'Пустые руки' },
  { id: 'sword', name: 'Меч', desc: 'Одноручный стальной клинок' },
  { id: 'shield', name: 'Меч и щит', desc: 'Клинок и каплевидный щит' },
  { id: 'axe', name: 'Боевой топор', desc: 'Секира на длинном древке' },
  { id: 'spear', name: 'Копьё', desc: 'Длинное древко с наконечником' },
  { id: 'staff', name: 'Посох', desc: 'Посох с магическим кристаллом' },
];

// ---------- small procedural mesh helpers ----------
function lathe(profile, seg = 24) { // profile: [[r,y],...]
  return new THREE.LatheGeometry(profile.map(([r, y]) => new THREE.Vector2(r, y)), seg);
}
function bladeGeom(len, w0, w1, thick, tipLen) {
  // diamond-section tapered blade along +Y, base at y=0
  const s = new THREE.Shape();
  s.moveTo(-w0, 0); s.lineTo(-w1, len - tipLen); s.lineTo(0, len); s.lineTo(w1, len - tipLen); s.lineTo(w0, 0); s.lineTo(-w0, 0);
  const g = new THREE.ExtrudeGeometry(s, { depth: thick, bevelEnabled: true, bevelThickness: thick * 0.9, bevelSize: Math.min(w1, w0) * 0.55, bevelSegments: 1, curveSegments: 1 });
  g.translate(0, 0, -thick / 2);
  return g;
}
function box(w, h, d) { return new THREE.BoxGeometry(w, h, d); }
function cyl(r0, r1, h, seg = 12) { return new THREE.CylinderGeometry(r1, r0, h, seg); }

// ---------- character ----------
export function buildCharacter(cfg, mats) {
  const h = new Human(cfg.sex, cfg.build);
  const skeleton = h.makeSkeleton();
  const group = new THREE.Group();
  const skinned = [];
  const addSkinned = (geom, mat, opts = {}) => {
    const m = new THREE.SkinnedMesh(geom, mats[mat]);
    m.castShadow = true; m.receiveShadow = true; m.frustumCulled = false;
    m.bind(skeleton); group.add(m); skinned.push(m); return m;
  };
  group.add(h.root);
  // ---- key joints / measures (rest pose) ----
  const J = n => h.head(n);
  const hipY = J('upperleg01.L').y, neckY = J('neck01').y, headJ = J('head');
  const eyeY = (J('eye.L').y + J('eye.R').y) / 2, eyeZ = J('eye.L').z;
  const headTop = h.tail('head').y;
  const waistY = (J('spine03').y + J('spine04').y) / 2;
  const R = h.region, T = h.limbT, P = h.pos, N = h.nor;
  const X = i => P[i * 3], Y = i => P[i * 3 + 1], Z = i => P[i * 3 + 2];
  const isArm = i => R[i] === 'upperarm' || R[i] === 'forearm' || R[i] === 'hand';
  const isLeg = i => R[i] === 'thigh' || R[i] === 'shin' || R[i] === 'foot';
  const torso = i => R[i] === 'torso' || R[i] === 'pelvis';
  const isFace = i => R[i] === 'head' && Z(i) > headJ.z + 0.035 && Y(i) < eyeY + 0.035 && Math.abs(X(i)) < 0.075;
  const scalpCut = i => Y(i) > eyeY + 0.045 - Math.max(0, -(Z(i) - headJ.z)) * 1.1;

  // body
  addSkinned(h.partGeometry(0), cfg.shadow ? 'SHADOW' : 'SKIN');   // the hero is a shadow wearing armour
  addSkinned(h.partGeometry(1), 'EYE');

  const shell = (pred, t, mat, o = {}) => addSkinned(h.shell(pred, t, o), mat);
  const A = cfg.armor;
  const helmet = A === 'plate';
  const hooded = A === 'robe' || A === 'chain';

  // ---- hair ----
  if (!helmet && !cfg.shadow) {
    shell(i => R[i] === 'head' && scalpCut(i) && !(Math.abs(X(i)) > 0.068 && Y(i) < eyeY + 0.06 && Z(i) > headJ.z - 0.03), 0.011, 'HAIR', { smooth: 2 });
    if (cfg.sex === 'f' && !hooded) group.add(longHair(h, mats, skeleton, skinned));
    if (cfg.sex === 'm' && cfg.build >= 2) shell(i => R[i] === 'head' && Y(i) < eyeY - 0.045 && Z(i) > headJ.z + 0.0 && !(Y(i) < eyeY - 0.06 && Y(i) > eyeY - 0.075 && Math.abs(X(i)) < 0.028 && Z(i) > headJ.z + 0.07), 0.008, 'HAIR', { smooth: 1 });
  }

  // ---- armor sets ----
  const boots = (from, mat = 'LEATHER', t = 0.013) => shell(i => (R[i] === 'shin' && T[i] > from) || R[i] === 'foot', t, mat, { smooth: 2 });
  const pants = (mat = 'PANTS') => shell(i => (isLeg(i) && T[i] < 0.9 && R[i] !== 'foot') || (R[i] === 'pelvis') || (R[i] === 'torso' && Y(i) < waistY), 0.005, mat, { smooth: 1 });
  const belt = (y, mat = 'LEATHER2', t = 0.02, hw = 0.022) => shell(i => torso(i) && Math.abs(Y(i) - y) < hw, t, mat, { smooth: 2 });
  const beltY = hipY + 0.085;

  if (A === 'cloth') {
    pants();
    shell(i => (torso(i) && Y(i) > hipY + 0.02) || (R[i] === 'neck' && Y(i) < neckY + 0.02) || (R[i] === 'upperarm' && T[i] < 0.5), 0.008, 'LINEN', { smooth: 1 });
    boots(0.55); belt(beltY);
    addBuckle(group, h, mats, beltY, 0.03);
  } else if (A === 'leather') {
    pants();
    shell(i => isArm(i) && T[i] < 0.95 && R[i] !== 'hand', 0.007, 'LINEN', { smooth: 1 });
    shell(i => (torso(i) && Y(i) > hipY - 0.04) || (R[i] === 'neck' && Y(i) < neckY + 0.03) || (R[i] === 'upperarm' && T[i] < 0.18), 0.017, 'LEATHER', { smooth: 3 });
    shell(i => R[i] === 'forearm' && T[i] > 0.58 && T[i] < 0.98, 0.016, 'LEATHER2', { smooth: 2 });
    shell(i => R[i] === 'upperarm' && T[i] < 0.3, 0.03, 'LEATHER2', { smooth: 4 });
    shell(i => R[i] === 'hand', 0.004, 'LEATHER2');
    boots(0.35, 'LEATHER2', 0.015); belt(beltY, 'DARK', 0.026);
    addBuckle(group, h, mats, beltY, 0.036);
    shell(i => torso(i) && Math.abs((Y(i) - hipY) - (X(i) + 0.2) * 1.4) < 0.025 && Y(i) > hipY, 0.024, 'LEATHER2', { smooth: 1 }); // strap
  } else if (A === 'chain') {
    pants();
    shell(i => (torso(i)) || R[i] === 'neck' || (R[i] === 'upperarm') || (R[i] === 'forearm' && T[i] < 0.6) || (R[i] === 'thigh' && T[i] < 0.42), 0.012, 'CHAIN', { smooth: 1 });
    shell(i => R[i] === 'head' && !isFace(i) && Y(i) > eyeY - 0.12, 0.014, 'CHAIN', { smooth: 2 }); // coif
    shell(i => (torso(i) || (R[i] === 'thigh' && T[i] < 0.4)) && Math.abs(X(i)) < 0.115 && Y(i) < neckY - 0.06, 0.03, 'RED', { smooth: 4 }); // tabard
    shell(i => R[i] === 'forearm' && T[i] > 0.6 || R[i] === 'hand', 0.01, 'LEATHER2', { smooth: 1 });
    boots(0.5); belt(beltY + 0.01, 'LEATHER2', 0.04, 0.02);
    addBuckle(group, h, mats, beltY + 0.01, 0.05);
  } else if (A === 'plate') {
    shell(i => torso(i) && Y(i) > hipY - 0.02 || (R[i] === 'neck' && Y(i) < neckY + 0.05), 0.04, 'PLATE', { smooth: 7, reinflate: 0.006 });
    shell(i => (torso(i) && Y(i) < hipY + 0.04) || (R[i] === 'thigh' && T[i] < 0.3), 0.055, 'PLATE', { smooth: 5 }); // faulds/tassets
    shell(i => R[i] === 'upperarm' && T[i] < 0.34 || (torso(i) && Y(i) > neckY - 0.08 && Math.abs(X(i)) > 0.14), 0.06, 'PLATE', { smooth: 6 }); // pauldrons
    shell(i => R[i] === 'upperarm' && T[i] >= 0.3 && T[i] < 0.6, 0.03, 'PLATE', { smooth: 3 });
    shell(i => (R[i] === 'upperarm' || R[i] === 'forearm') && T[i] >= 0.56 && T[i] < 0.66, 0.045, 'PLATE', { smooth: 3 }); // couter
    shell(i => R[i] === 'forearm' && T[i] >= 0.64, 0.03, 'PLATE', { smooth: 3 });
    shell(i => R[i] === 'hand', 0.014, 'PLATE', { smooth: 1 });
    shell(i => R[i] === 'thigh' && T[i] >= 0.28 && T[i] < 0.5, 0.035, 'PLATE', { smooth: 4 });
    shell(i => isLeg(i) && T[i] >= 0.47 && T[i] < 0.6 && R[i] !== 'foot', 0.05, 'PLATE', { smooth: 4 }); // poleyn
    shell(i => R[i] === 'shin' && T[i] >= 0.58, 0.032, 'PLATE', { smooth: 4 });
    shell(i => R[i] === 'foot', 0.022, 'PLATE', { smooth: 2 });
    shell(i => R[i] === 'head' || (R[i] === 'neck' && Y(i) > neckY), 0.04, 'PLATE', { smooth: 10, reinflate: 0.012 }); // helmet
    shell(i => R[i] === 'head' && Math.abs(Y(i) - eyeY) < 0.011 && Z(i) > headJ.z + 0.02, 0.058, 'DARK', { smooth: 1 }); // visor slit
    shell(i => R[i] === 'head' && Math.abs(Y(i) - eyeY) < 0.006 && Math.abs(X(i)) > 0.012 && Math.abs(X(i)) < 0.05 && Z(i) > headJ.z + 0.05, 0.06, 'EYE');
    belt(beltY, 'LEATHER2', 0.06, 0.018);
    addPlume(group, h, mats, headTop);
  } else if (A === 'robe') {
    pants('ROBED');
    shell(i => torso(i) && Y(i) > hipY - 0.05 || R[i] === 'neck' || (isArm(i) && T[i] < 0.86 && R[i] !== 'hand'), 0.018, 'ROBE', { smooth: 3 });
    shell(i => R[i] === 'forearm' && T[i] > 0.72 && T[i] < 0.86, 0.03, 'GOLD', { smooth: 2 });
    shell(i => R[i] === 'head' && !isFace(i) && Y(i) > eyeY - 0.14, 0.03, 'ROBED', { smooth: 5 }); // hood
    group.add(robeSkirt(h, mats, hipY + 0.1, skeleton, skinned));
    belt(beltY, 'GOLD', 0.045, 0.014);
    boots(0.8, 'LEATHER2', 0.01);
  }

  // ---- pose depends on weapon ----
  const W = cfg.weapon;
  applyPose(h, W === 'shield' ? 'shield' : W === 'none' ? 'relaxed' : 'grip');
  if (W !== 'none') addWeapon(group, h, mats, W);
  if (W === 'shield') addShield(group, h, mats);

  return { group, human: h, skeleton, height: h.v.height };
}

// ---------- extras ----------
function attach(h, boneName, obj, worldMatrix) {
  const b = h.bone(boneName); b.updateMatrixWorld(true);
  const inv = new THREE.Matrix4().copy(b.matrixWorld).invert();
  obj.matrixAutoUpdate = false;
  obj.matrix.copy(inv.multiply(worldMatrix));
  b.add(obj);
}
function mesh(g, mat) { const m = new THREE.Mesh(g, mat); m.castShadow = true; m.receiveShadow = true; return m; }

function addBuckle(group, h, mats, y, out) {
  // front buckle at belt height (attached to pelvis-ish bone)
  const sp = h.head('spine04') || h.head('spine05');
  // find front surface z at that height
  let zmax = -1;
  for (let i = 0; i < h.n; i++) if (h.vpart[i] === 0 && Math.abs(h.pos[i * 3 + 1] - y) < 0.01 && Math.abs(h.pos[i * 3]) < 0.03) zmax = Math.max(zmax, h.pos[i * 3 + 2]);
  const g = new THREE.Group();
  const frame = mesh(new THREE.TorusGeometry(0.024, 0.006, 6, 14), mats.GOLD); frame.scale.set(1, 0.8, 1); g.add(frame);
  const pin = mesh(box(0.004, 0.03, 0.006), mats.GOLD); g.add(pin);
  const mtx = new THREE.Matrix4().makeTranslation(0, y, zmax + out * 0.9);
  attach(h, 'spine05', g, mtx);
}
function addPlume(group, h, mats, top) {
  const g = new THREE.Group();
  const crest = mesh(new THREE.TorusGeometry(0.13, 0.012, 6, 24, Math.PI * 0.9), mats.PLATE);
  crest.rotation.y = Math.PI / 2; crest.rotation.z = Math.PI * 0.05; crest.position.set(0, -0.08, -0.01); g.add(crest);
  for (let k = 0; k < 9; k++) {
    const t = k / 8, a = Math.PI * (0.15 + 0.75 * t);
    const f = mesh(new THREE.SphereGeometry(0.035 + 0.02 * Math.sin(t * Math.PI), 8, 6), mats.RED);
    f.scale.set(0.45, 1.4, 1); f.position.set(0, -0.08 + Math.sin(a) * 0.17, -0.01 - Math.cos(a) * 0.17); f.rotation.x = -a + Math.PI / 2;
    g.add(f);
  }
  const hc = h.head('head');
  attach(h, 'head', g, new THREE.Matrix4().makeTranslation(0, top + 0.02, hc.z - 0.01));
}

function longHair(h, mats, skeleton, skinned) {
  // curtain of hair behind the head, from crown to below the shoulders
  const hc = h.head('head'), top = h.tail('head').y, nk = h.head('neck01');
  let rx = 0, rz = 0; // head radii
  for (let i = 0; i < h.n; i++) if (h.region[i] === 'head' && Math.abs(h.pos[i * 3 + 1] - (top - 0.1)) < 0.01) { rx = Math.max(rx, Math.abs(h.pos[i * 3])); rz = Math.max(rz, Math.abs(h.pos[i * 3 + 2] - hc.z)); }
  const rows = 26, segs = 36, y0 = top - 0.06, y1 = nk.y - 0.26;
  const pos = [], idx = [];
  for (let r = 0; r <= rows; r++) {
    const t = r / rows, y = y0 + (y1 - y0) * t;
    const flare = 1 + 0.25 * Math.min(1, t * 2) + 0.1 * t;
    for (let s = 0; s <= segs; s++) {
      const a = Math.PI * (-0.1 + 1.2 * s / segs); // around the back only
      const wav = 1 + 0.06 * Math.sin(s * 1.9 + t * 7) + 0.04 * Math.sin(s * 5.3);
      const x = Math.cos(a) * (rx + 0.022) * flare * wav, z = hc.z - 0.008 - Math.sin(a) * (rz + 0.03) * flare * wav * (1 - 0.25 * t);
      pos.push(x, y - 0.03 * Math.sin(s * 2.7) * t, z);
    }
  }
  for (let r = 0; r < rows; r++) for (let s = 0; s < segs; s++) {
    const a = r * (segs + 1) + s, b = a + segs + 1;
    idx.push(a, b, a + 1, a + 1, b, b + 1);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); g.setIndex(idx); g.computeVertexNormals();
  const m = mesh(g, mats.HAIR2);
  const holder = new THREE.Group(); holder.add(m);
  attach(h, 'head', holder, new THREE.Matrix4());
  return new THREE.Group();
}

function robeSkirt(h, mats, yTop, skeleton, skinned) {
  // lofted skirt from hips to floor, sized from body cross sections
  const rings = 22, segs = 48, pos = [], idx = [];
  const P = h.pos;
  const ext = (y) => { let x0 = 0, z0 = 0, z1 = 0; for (let i = 0; i < h.n; i++) if (h.vpart[i] === 0 && Math.abs(P[i * 3 + 1] - y) < 0.012 && (h.region[i] === 'torso' || h.region[i] === 'pelvis' || h.region[i] === 'thigh' || h.region[i] === 'shin')) { x0 = Math.max(x0, Math.abs(P[i * 3])); z0 = Math.min(z0, P[i * 3 + 2]); z1 = Math.max(z1, P[i * 3 + 2]); } return [x0, z0, z1]; };
  let prev = ext(yTop);
  for (let r = 0; r <= rings; r++) {
    const t = r / rings, y = yTop - (yTop - 0.02) * t;
    let [ex, z0, z1] = ext(y);
    if (ex < prev[0] * 0.9) { ex = prev[0]; z0 = prev[1]; z1 = prev[2]; }
    prev = [Math.max(ex, prev[0]), Math.min(z0, prev[1]), Math.max(z1, prev[2])];
    const flare = 0.025 + 0.11 * t * t;
    const rx = prev[0] + flare, rz = (prev[2] - prev[1]) / 2 + flare * 0.9, cz = (prev[2] + prev[1]) / 2;
    for (let s = 0; s <= segs; s++) {
      const a = 2 * Math.PI * s / segs;
      const fold = 1 + (0.012 + 0.05 * t) * Math.sin(a * 9 + t * 2) / Math.max(rx, 0.1);
      pos.push(Math.cos(a) * rx * fold, y, cz + Math.sin(a) * rz * fold);
    }
  }
  for (let r = 0; r < rings; r++) for (let s = 0; s < segs; s++) { const a = r * (segs + 1) + s, b = a + segs + 1; idx.push(a, a + 1, b, a + 1, b + 1, b); }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); g.setIndex(idx); g.computeVertexNormals();
  g.userData.glyphCyl = true;
  const m = mesh(g, mats.ROBE); m.material.side = THREE.DoubleSide;
  const hem = mesh(new THREE.TorusGeometry(1, 0.012, 4, 48), mats.GOLD);
  const last = pos.length / 3 - segs - 1;
  hem.scale.set(1, 1, 1);
  const holder = new THREE.Group(); holder.add(m);
  attach(h, 'spine05', holder, new THREE.Matrix4());
  return new THREE.Group();
}

// weapon in the right hand
function addWeapon(group, h, mats, W) {
  const fr = handFrameWorld(h, 'R');
  // grip centre: inside the curled fingers
  const grip = fr.W.clone().addScaledVector(fr.f, 0.075).addScaledVector(fr.n, -0.028);
  const up = fr.a.clone(); // weapon axis: out of the thumb side of the fist, so the blade is carried pointing up
  const fwd = new THREE.Vector3().crossVectors(up, fr.f).normalize();
  const side = new THREE.Vector3().crossVectors(fwd, up).normalize();
  const basis = new THREE.Matrix4().makeBasis(side, up, fwd).setPosition(grip);
  const g = new THREE.Group();
  if (W === 'sword' || W === 'shield') {
    g.add(Object.assign(mesh(cyl(0.015, 0.014, 0.11, 10), mats.LEATHER2), {}));
    const pom = mesh(new THREE.SphereGeometry(0.024, 10, 8), mats.GOLD); pom.position.y = -0.07; g.add(pom);
    const guard = mesh(box(0.2, 0.018, 0.03), mats.GOLD); guard.position.y = 0.065; g.add(guard);
    for (const sx of [-1, 1]) { const e = mesh(new THREE.SphereGeometry(0.016, 8, 6), mats.GOLD); e.position.set(sx * 0.1, 0.065, 0); g.add(e); }
    const bl = mesh(bladeGeom(0.78, 0.024, 0.019, 0.004, 0.1), mats.STEEL); bl.position.y = 0.075; g.add(bl);
    const fuller = mesh(box(0.008, 0.5, 0.0112), mats.DARK); fuller.position.y = 0.37; g.add(fuller);
  } else if (W === 'axe') {
    const haft = mesh(cyl(0.017, 0.015, 0.95, 10), mats.WOOD); haft.position.y = 0.3; g.add(haft);
    const s = new THREE.Shape();
    s.moveTo(0, -0.04); s.lineTo(0.05, -0.035); s.quadraticCurveTo(0.13, -0.09, 0.2, -0.13); s.quadraticCurveTo(0.17, 0.0, 0.2, 0.13); s.quadraticCurveTo(0.13, 0.09, 0.05, 0.035); s.lineTo(0, 0.04); s.lineTo(0, -0.04);
    const head = mesh(new THREE.ExtrudeGeometry(s, { depth: 0.012, bevelEnabled: true, bevelThickness: 0.006, bevelSize: 0.004, bevelSegments: 1 }), mats.STEEL);
    head.position.set(0.012, 0.66, -0.006); g.add(head);
    const spike = mesh(new THREE.ConeGeometry(0.02, 0.09, 6), mats.STEEL); spike.rotation.z = Math.PI / 2; spike.position.set(-0.06, 0.66, 0); g.add(spike);
    const cap = mesh(cyl(0.024, 0.024, 0.1, 8), mats.STEEL); cap.position.y = 0.66; g.add(cap);
    const wrap = mesh(cyl(0.02, 0.02, 0.16, 10), mats.LEATHER2); g.add(wrap);
  } else if (W === 'spear') {
    const shaft = mesh(cyl(0.015, 0.013, 2.1, 10), mats.WOOD); shaft.position.y = 0.55; g.add(shaft);
    const tip = mesh(bladeGeom(0.24, 0.03, 0.032, 0.006, 0.14), mats.STEEL); tip.position.y = 1.63; g.add(tip);
    const sock = mesh(cyl(0.02, 0.014, 0.08, 10), mats.STEEL); sock.position.y = 1.6; g.add(sock);
    const tas = mesh(cyl(0.03, 0.01, 0.07, 8), mats.RED); tas.position.y = 1.53; g.add(tas);
    const butt = mesh(new THREE.ConeGeometry(0.016, 0.06, 8), mats.STEEL); butt.rotation.x = Math.PI; butt.position.y = -0.52; g.add(butt);
  } else if (W === 'staff') {
    const pts = []; for (let k = 0; k <= 16; k++) { const t = k / 16; pts.push(new THREE.Vector3(0.012 * Math.sin(t * 9), -0.5 + t * 1.75, 0.012 * Math.cos(t * 7))); }
    const shaft = mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 40, 0.02, 8), mats.WOOD); g.add(shaft);
    for (let k = 0; k < 4; k++) { const a = k * Math.PI / 2; const c = new THREE.CatmullRomCurve3([new THREE.Vector3(0, 1.2, 0), new THREE.Vector3(Math.cos(a) * 0.05, 1.3, Math.sin(a) * 0.05), new THREE.Vector3(Math.cos(a) * 0.035, 1.42, Math.sin(a) * 0.035)]); g.add(mesh(new THREE.TubeGeometry(c, 8, 0.009, 5), mats.WOOD)); }
    const orb = mesh(new THREE.OctahedronGeometry(0.05, 1), mats.GLOW); orb.position.y = 1.33; orb.scale.set(0.8, 1.2, 0.8); g.add(orb);
    g.userData.orb = orb;
    const ring = mesh(new THREE.TorusGeometry(0.025, 0.006, 6, 16), mats.GOLD); ring.rotation.x = Math.PI / 2; ring.position.y = 1.2; g.add(ring);
  }
  attach(h, 'wrist.R', g, basis);
  group.userData.weapon = g;
}

function addShield(group, h, mats) {
  h.root.updateMatrixWorld(true);
  const p = n => h.bone(n).getWorldPosition(new THREE.Vector3());
  const e = p('lowerarm01.L'), w = p('wrist.L');
  const d = w.clone().sub(e).normalize();
  let out = new THREE.Vector3(0, 0, 1); out.addScaledVector(d, -out.dot(d)).normalize();
  const up = new THREE.Vector3(0, 1, 0); up.addScaledVector(out, -up.dot(out)).normalize();
  const side = new THREE.Vector3().crossVectors(up, out);
  const c = e.clone().lerp(w, 0.5).addScaledVector(out, 0.07).addScaledVector(up, -0.03);
  const g = new THREE.Group();
  // heater shield profile
  const s = new THREE.Shape();
  s.moveTo(-0.24, 0.26); s.lineTo(0.24, 0.26); s.lineTo(0.24, 0.02); s.quadraticCurveTo(0.22, -0.26, 0, -0.42); s.quadraticCurveTo(-0.22, -0.26, -0.24, 0.02); s.lineTo(-0.24, 0.26);
  const body = new THREE.ExtrudeGeometry(s, { depth: 0.02, bevelEnabled: true, bevelThickness: 0.012, bevelSize: 0.014, bevelSegments: 2, curveSegments: 10 });
  // bend slightly
  const pa = body.attributes.position; for (let i = 0; i < pa.count; i++) { const x = pa.getX(i); pa.setZ(i, pa.getZ(i) - x * x * 0.6); } body.computeVertexNormals();
  g.add(mesh(body, mats.RED));
  const rim = new THREE.Shape(); const pts = s.getPoints(30); rim.setFromPoints(pts);
  const rimG = new THREE.ExtrudeGeometry(new THREE.Shape(pts.map(v => v.clone().multiplyScalar(1.03))), { depth: 0.008, bevelEnabled: false });
  rimG.translate(0, 0, -0.006); const pr = rimG.attributes.position; for (let i = 0; i < pr.count; i++) { const x = pr.getX(i); pr.setZ(i, pr.getZ(i) - x * x * 0.6); } rimG.computeVertexNormals();
  g.add(mesh(rimG, mats.STEEL));
  const boss = mesh(new THREE.SphereGeometry(0.05, 12, 8, 0, Math.PI * 2, 0, Math.PI / 2), mats.STEEL); boss.rotation.x = Math.PI / 2; boss.position.set(0, 0.0, 0.035); g.add(boss);
  const cv = mesh(box(0.05, 0.6, 0.01), mats.GOLD); cv.position.set(0, -0.06, 0.036); g.add(cv);
  const ch = mesh(box(0.4, 0.05, 0.01), mats.GOLD); ch.position.set(0, 0.08, 0.034); g.add(ch);
  const m = new THREE.Matrix4().makeBasis(side, up, out).setPosition(c);
  attach(h, 'lowerarm02.L', g, m);
}
