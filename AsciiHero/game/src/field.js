// Test field: open ground the size of four taverns (20 x 16 m), a few trees and rocks, grass that reacts to the
// hero, and ten piles of armour with random contents.
import * as THREE from 'three';
import { makeGrass } from './ascii.js';

export const LIMBS = ['head', 'armL', 'armR', 'legL', 'legR'];
export const LIMB_NAME = { head: 'голова', armL: 'левая рука', armR: 'правая рука', legL: 'левая нога', legR: 'правая нога' };
function rng(seed) { let a = seed >>> 0; return () => { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }

export function buildField(mats) {
  const g = new THREE.Group(), R = rng(20261112), colliders = [], piles = [];
  const HX = 10, HZ = 8;
  const M = (geo, mat, x, y, z, cast = true) => { const m = new THREE.Mesh(geo, mat); m.position.set(x, y, z); m.castShadow = cast; m.receiveShadow = true; g.add(m); return m; };
  const ground = M(new THREE.PlaneGeometry(HX * 2 + 6, HZ * 2 + 6), mats.GROUND, 0, 0, 0, false); ground.rotation.x = -Math.PI / 2;
  g.add(new THREE.HemisphereLight(0x8fa4d8, 0x1a2414, 0.5));
  const spawn = new THREE.Vector3(0, 0, 0);
  const free = (x, z, r) => Math.hypot(x - spawn.x, z - spawn.z) > 1.5 + r && colliders.every(c => Math.hypot(x - c.x, z - c.z) > c.r + r + 0.5) && piles.every(p => Math.hypot(x - p.x, z - p.z) > 1.6 + r);
  const place = (r, margin = 1) => { for (let k = 0; k < 200; k++) { const x = (R() * 2 - 1) * (HX - margin), z = (R() * 2 - 1) * (HZ - margin); if (free(x, z, r)) return [x, z]; } return [0, 0]; };
  // trees
  for (let i = 0; i < 7; i++) {
    const [x, z] = place(0.4, 1.5), h = 1.5 + R() * 0.8, r = 0.11 + R() * 0.06;
    M(new THREE.CylinderGeometry(r * 0.7, r, h, 8), mats.WOOD, x, h / 2, z);
    for (let k = 0; k < 3; k++) M(new THREE.ConeGeometry(0.62 - k * 0.15, 0.9, 9), mats.LEAF, x, h + 0.25 + k * 0.55, z);
    colliders.push({ x, z, r: r + 0.2 });
  }
  // rocks
  for (let i = 0; i < 12; i++) {
    const s = 0.25 + R() * 0.5, [x, z] = place(s, 0.8);
    const m = M(new THREE.SphereGeometry(s, 9, 6), mats.ROCK, x, s * 0.35, z); m.scale.set(1 + R() * 0.5, 0.55 + R() * 0.3, 1 + R() * 0.4); m.rotation.y = R() * 6;
    colliders.push({ x, z, r: s * 1.05 });
  }
  // armour piles: a breastplate marks the spot, the limbs that the pile contains lie around it
  const patterns = [[], [], ['armL'], ['armR'], ['legL'], ['legR'], ['head'], ['armL', 'legR'], ['head', 'armR'], ['legL', 'legR', 'armL']];
  for (let i = 0; i < 10; i++) {
    const [x, z] = place(0.6, 1.2), missing = patterns[i], pile = { x, z, items: {}, group: new THREE.Group() };
    pile.group.position.set(x, 0, z); pile.group.rotation.y = R() * 6; g.add(pile.group);
    const P = (geo, mat, px, py, pz, rx = 0, rz = 0) => { const m = new THREE.Mesh(geo, mat); m.position.set(px, py, pz); m.rotation.set(rx, 0, rz); m.castShadow = true; m.receiveShadow = true; pile.group.add(m); return m; };
    P(new THREE.SphereGeometry(0.2, 10, 6, 0, Math.PI * 2, 0, Math.PI / 2), mats.PLATE, 0, 0.02, 0).scale.set(1, 0.8, 1.3);
    P(new THREE.BoxGeometry(0.34, 0.04, 0.05), mats.LEATHER2, 0, 0.12, 0.2);
    const mk = {
      head: () => { const m = P(new THREE.SphereGeometry(0.13, 10, 8), mats.PLATE, 0.3, 0.13, 0.28); P(new THREE.BoxGeometry(0.14, 0.02, 0.03), mats.DARK, 0.3, 0.15, 0.4).userData.of = m; return m; },
      armL: () => P(new THREE.CylinderGeometry(0.05, 0.04, 0.6, 8), mats.PLATE, -0.38, 0.06, 0.1, Math.PI / 2, 0.5),
      armR: () => P(new THREE.CylinderGeometry(0.05, 0.04, 0.6, 8), mats.PLATE, 0.42, 0.06, -0.15, Math.PI / 2, -0.7),
      legL: () => P(new THREE.CylinderGeometry(0.075, 0.055, 0.85, 8), mats.PLATE, -0.25, 0.08, -0.42, Math.PI / 2, 1.3),
      legR: () => P(new THREE.CylinderGeometry(0.075, 0.055, 0.85, 8), mats.PLATE, 0.15, 0.08, -0.55, Math.PI / 2, 1.75),
    };
    for (const l of LIMBS) if (!missing.includes(l)) pile.items[l] = mk[l]();
    piles.push(pile);
  }
  // grass
  const pts = [], seeds = [], N = Math.round(HX * 2 * HZ * 2 * 60);
  for (let i = 0; i < N; i++) {
    const x = (R() * 2 - 1) * (HX + 2), z = (R() * 2 - 1) * (HZ + 2);
    if (colliders.some(c => Math.hypot(x - c.x, z - c.z) < c.r * 0.8)) continue;
    pts.push(x, 0, z); seeds.push(R());
  }
  g.add(makeGrass(new Float32Array(pts), new Float32Array(seeds)));
  const bounds = { minx: -HX + 0.3, maxx: HX - 0.3, minz: -HZ + 0.3, maxz: HZ - 0.3 };
  // take the piece of this pile for limb `l`; returns true if there was one
  const take = (pile, l) => { const m = pile.items[l]; if (!m) return false; m.visible = false; pile.group.traverse(o => { if (o.userData.of === m) o.visible = false; }); delete pile.items[l]; return true; };
  return { group: g, colliders, bounds, piles, take, spawn, update() {}, cutaway() {}, name: 'field' };
}
