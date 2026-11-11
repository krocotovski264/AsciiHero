// MakeHuman-based character: decode baked variants, skeleton, skinning, garments.
import * as THREE from 'three';
import { HUMAN_META, HUMAN_B64 } from './data_human.js';

export const MAT = {
  SKIN: 0, HAIR: 1, LINEN: 2, PANTS: 3, LEATHER: 4, CHAIN: 5, PLATE: 6, STEEL: 7,
  WOOD: 8, GOLD: 9, GLOW: 10, EYE: 11, DARK: 12, RED: 13, ROBE: 14, ROBED: 15, STONE: 16, LEATHER2: 17,
};

let RAW = null;
const CACHE = {};
export async function loadHuman() {
  if (RAW) return;
  const bin = Uint8Array.from(atob(HUMAN_B64), c => c.charCodeAt(0));
  const ds = new DecompressionStream('gzip');
  const buf = await new Response(new Blob([bin]).stream().pipeThrough(ds)).arrayBuffer();
  RAW = buf;
}
const view = (T, [off, len]) => new T(RAW, off, len / T.BYTES_PER_ELEMENT);

export function variantFor(sex, build) {
  return HUMAN_META.variants.find(v => v.sex === sex && v.build === build);
}

// ---------- geometry helpers ----------
function vertexNormals(pos, idx, n) {
  const nor = new Float32Array(n * 3);
  for (let t = 0; t < idx.length; t += 3) {
    const a = idx[t] * 3, b = idx[t + 1] * 3, c = idx[t + 2] * 3;
    const ux = pos[b] - pos[a], uy = pos[b + 1] - pos[a + 1], uz = pos[b + 2] - pos[a + 2];
    const vx = pos[c] - pos[a], vy = pos[c + 1] - pos[a + 1], vz = pos[c + 2] - pos[a + 2];
    const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
    for (const k of [a, b, c]) { nor[k] += nx; nor[k + 1] += ny; nor[k + 2] += nz; }
  }
  for (let i = 0; i < n; i++) {
    const l = Math.hypot(nor[i * 3], nor[i * 3 + 1], nor[i * 3 + 2]) || 1;
    nor[i * 3] /= l; nor[i * 3 + 1] /= l; nor[i * 3 + 2] /= l;
  }
  return nor;
}
function adjacency(idx, n) {
  const adj = Array.from({ length: n }, () => new Set());
  for (let t = 0; t < idx.length; t += 3) {
    const a = idx[t], b = idx[t + 1], c = idx[t + 2];
    adj[a].add(b); adj[a].add(c); adj[b].add(a); adj[b].add(c); adj[c].add(a); adj[c].add(b);
  }
  return adj.map(s => Int32Array.from(s));
}
function smooth(pos, adj, verts, iters, lambda = 0.5) {
  const tmp = new Float32Array(pos.length);
  for (let it = 0; it < iters; it++) {
    tmp.set(pos);
    for (const i of verts) {
      const a = adj[i]; if (!a.length) continue;
      let x = 0, y = 0, z = 0;
      for (const j of a) { x += tmp[j * 3]; y += tmp[j * 3 + 1]; z += tmp[j * 3 + 2]; }
      x /= a.length; y /= a.length; z /= a.length;
      pos[i * 3] += (x - pos[i * 3]) * lambda; pos[i * 3 + 1] += (y - pos[i * 3 + 1]) * lambda; pos[i * 3 + 2] += (z - pos[i * 3 + 2]) * lambda;
    }
  }
}

// ---------- body build ----------
export class Human {
  constructor(sex, build) {
    const M = HUMAN_META;
    this.meta = M; this.v = variantFor(sex, build); this.sex = sex; this.build = build;
    const n = M.nverts; this.n = n;
    const q = view(Uint16Array, this.v.pos);
    const pos = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) for (let k = 0; k < 3; k++) pos[i * 3 + k] = M.lo[k] + q[i * 3 + k] * M.q;
    this.pos = pos;
    this.tris = view(Uint16Array, M.tris);
    this.vpart = view(Uint8Array, M.vpart);
    this.skinIndex = view(Uint8Array, M.skinIndex);
    this.skinWeight = view(Uint8Array, M.skinWeight);
    this.heads = this.v.heads.map(h => new THREE.Vector3(...h));
    this.tails = this.v.tails.map(h => new THREE.Vector3(...h));
    this.BI = {}; M.bones.forEach((b, i) => this.BI[b] = i);
    // body-only triangle list
    const bodyT = [], partT = { 0: [], 1: [], 2: [], 3: [] };
    for (let t = 0; t < this.tris.length; t += 3) {
      const p = this.vpart[this.tris[t]];
      partT[p].push(this.tris[t], this.tris[t + 1], this.tris[t + 2]);
    }
    this.partTris = partT;
    const key = sex + build;
    const C = CACHE[key];
    if (C) { this.nor = C.nor; this.adj = C.adj; this.region = C.region; this.side = C.side; this.limbT = C.limbT; this.gUV = C.gUV; }
    else {
      this.nor = vertexNormals(pos, Uint32Array.from(partT[0]), n);
      this.adj = adjacency(Uint32Array.from(partT[0]), n);
      this.computeRegions();
      this.computeGlyphUV();
      CACHE[key] = { nor: this.nor, adj: this.adj, region: this.region, side: this.side, limbT: this.limbT, gUV: this.gUV };
    }
  }
  head(b) { return this.heads[this.BI[b]]; }
  tail(b) { return this.tails[this.BI[b]]; }

  // per-vertex region + limb parameter
  computeRegions() {
    const n = this.n, B = this.meta.bones, si = this.skinIndex, sw = this.skinWeight;
    const groupOf = (name) => {
      if (/^(finger|metacarpal|wrist)/.test(name)) return 'hand';
      if (/^lowerarm/.test(name)) return 'forearm';
      if (/^(upperarm|shoulder01)/.test(name)) return 'upperarm';
      if (/^(toe|foot)/.test(name)) return 'foot';
      if (/^lowerleg/.test(name)) return 'shin';
      if (/^upperleg/.test(name)) return 'thigh';
      if (/^(neck)/.test(name)) return 'neck';
      if (/^(head|jaw|eye|oculi|orbicularis|oris|risorius|levator|temporalis|special0[3-6]|tongue)/.test(name)) return 'head';
      if (/^pelvis/.test(name)) return 'pelvis';
      return 'torso';
    };
    const groups = B.map(groupOf);
    this.region = new Array(n);
    this.side = new Int8Array(n);
    this.limbT = new Float32Array(n);
    const P = this.pos;
    const L = (a, b) => [this.head(a), this.head(b)];
    const armL = L('upperarm01.L', 'wrist.L'), armR = L('upperarm01.R', 'wrist.R');
    const legL = L('upperleg01.L', 'foot.L'), legR = L('upperleg01.R', 'foot.R');
    const proj = (x, y, z, [a, b]) => {
      const dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z;
      return ((x - a.x) * dx + (y - a.y) * dy + (z - a.z) * dz) / (dx * dx + dy * dy + dz * dz);
    };
    for (let i = 0; i < n; i++) {
      const acc = {};
      for (let k = 0; k < 4; k++) { const g = groups[si[i * 4 + k]]; acc[g] = (acc[g] || 0) + sw[i * 4 + k]; }
      let best = 'torso', bw = -1; for (const g in acc) if (acc[g] > bw) { bw = acc[g]; best = g; }
      this.region[i] = best;
      const x = P[i * 3], y = P[i * 3 + 1], z = P[i * 3 + 2];
      this.side[i] = x >= 0 ? 1 : -1;
      if (best === 'upperarm' || best === 'forearm' || best === 'hand') this.limbT[i] = proj(x, y, z, x >= 0 ? armL : armR);
      else if (best === 'thigh' || best === 'shin' || best === 'foot') this.limbT[i] = proj(x, y, z, x >= 0 ? legL : legR);
      else this.limbT[i] = 0;
    }
  }

  // Surface glyph coordinates (metres) in the rest pose: every body part gets a cylindrical frame around its bone
  // (rings of characters wrap around arms, legs and torso), the head a spherical one (concentric rings on the crown).
  // u runs left->right seen from outside, v runs "up" (towards the joint the limb hangs from).
  computeGlyphUV() {
    const n = this.n, P = this.pos, R = this.region, uv = new Float32Array(n * 2);
    const H = b => this.head(b);
    const seg = { upperarm: ['upperarm01', 'lowerarm01'], forearm: ['lowerarm01', 'wrist'], hand: ['wrist', 'finger3-1'],
      thigh: ['upperleg01', 'lowerleg01'], shin: ['lowerleg01', 'foot'], foot: ['foot', 'toe3-1'] };
    const V = THREE.Vector3, tmp = new V(), r = new V(), e1 = new V(), e2 = new V();
    const frames = {};
    const frameFor = (reg, s) => {
      const k = reg + s; if (frames[k]) return frames[k];
      let A, D, sph = false;
      if (seg[reg]) { const a = H(seg[reg][0] + '.' + s), b = H(seg[reg][1] + '.' + s); A = b.clone(); D = a.clone().sub(b).normalize(); }
      else if (reg === 'head') { const h = H('head'); A = new V(0, (h.y + this.tail('head').y) / 2, h.z + 0.01); D = new V(0, 1, 0); sph = true; }
      else { const sp = H('spine03'); A = new V(0, 0, sp.z); D = new V(0, 1, 0); }
      const f = new V(0, 0, 1); if (Math.abs(f.dot(D)) > 0.9) f.set(0, 1, 0);
      const E1 = f.addScaledVector(D, -f.dot(D)).normalize(), E2 = new V().crossVectors(D, E1);
      return (frames[k] = { A, D, E1, E2, sph, rsum: 0, rn: 0 });
    };
    const fr = new Array(n);
    for (let i = 0; i < n; i++) {
      const reg0 = R[i], reg = seg[reg0] || reg0 === 'head' ? reg0 : 'trunk', s = P[i * 3] >= 0 ? 'L' : 'R';
      const F = frameFor(reg, seg[reg] ? s : ''); fr[i] = F;
      tmp.set(P[i * 3], P[i * 3 + 1], P[i * 3 + 2]).sub(F.A);
      if (F.sph) F.rsum += tmp.length(); else { r.copy(tmp).addScaledVector(F.D, -tmp.dot(F.D)); F.rsum += r.length(); }
      F.rn++;
    }
    for (let i = 0; i < n; i++) {
      const F = fr[i], Rm = F.rsum / Math.max(1, F.rn);
      tmp.set(P[i * 3], P[i * 3 + 1], P[i * 3 + 2]).sub(F.A);
      if (F.sph) {
        const l = tmp.length() || 1, polar = Math.acos(Math.max(-1, Math.min(1, tmp.dot(F.D) / l)));
        const az = Math.atan2(tmp.dot(F.E2), tmp.dot(F.E1));
        uv[i * 2] = az * Rm * Math.sin(Math.max(0.35, polar)); uv[i * 2 + 1] = (Math.PI / 2 - polar) * Rm;
      } else {
        const t = tmp.dot(F.D); r.copy(tmp).addScaledVector(F.D, -t);
        uv[i * 2] = Math.atan2(r.dot(F.E2), r.dot(F.E1)) * Rm; uv[i * 2 + 1] = t;
      }
    }
    this.gUV = uv;
  }

  // garment shell: select body faces by predicate(vertex) -> offset along normal
  shell(pred, thick, opts = {}) {
    const n = this.n, P = this.pos, N = this.nor, T = this.partTris[0];
    const sel = new Uint8Array(n);
    for (let i = 0; i < n; i++) if (this.vpart[i] === 0 && pred(i)) sel[i] = 1;
    const tris = [];
    for (let t = 0; t < T.length; t += 3) {
      const a = T[t], b = T[t + 1], c = T[t + 2];
      if (opts.any ? (sel[a] || sel[b] || sel[c]) : (sel[a] && sel[b] && sel[c])) tris.push(a, b, c);
    }
    const used = new Set(tris);
    const pos = new Float32Array(P);
    const th = typeof thick === 'function' ? thick : () => thick;
    for (const i of used) { const d = th(i); pos[i * 3] += N[i * 3] * d; pos[i * 3 + 1] += N[i * 3 + 1] * d; pos[i * 3 + 2] += N[i * 3 + 2] * d; }
    if (opts.smooth) {
      const inner = [...used];
      smooth(pos, this.adj, inner, opts.smooth, 0.5);
      // re-inflate a bit to compensate shrinkage
      if (opts.reinflate) for (const i of inner) { const d = opts.reinflate; pos[i * 3] += N[i * 3] * d; pos[i * 3 + 1] += N[i * 3 + 1] * d; pos[i * 3 + 2] += N[i * 3 + 2] * d; }
    }
    return this.makeGeometry(pos, tris, opts);
  }
  // geometry over the shared vertex buffer subset (compacted)
  makeGeometry(posAll, tris, opts = {}) {
    const map = new Map(); const out = [];
    for (const v of tris) { if (!map.has(v)) map.set(v, map.size); out.push(map.get(v)); }
    const m = map.size;
    const pos = new Float32Array(m * 3), si = new Uint16Array(m * 4), sw = new Float32Array(m * 4), uv = new Float32Array(m * 2), gl = new Float32Array(m * 2);
    for (const [v, j] of map) {
      pos[j * 3] = posAll[v * 3]; pos[j * 3 + 1] = posAll[v * 3 + 1]; pos[j * 3 + 2] = posAll[v * 3 + 2];
      for (let k = 0; k < 4; k++) { si[j * 4 + k] = this.skinIndex[v * 4 + k]; sw[j * 4 + k] = this.skinWeight[v * 4 + k] / 255; }
      uv[j * 2] = this.limbT[v]; uv[j * 2 + 1] = posAll[v * 3 + 1];
      gl[j * 2] = this.gUV[v * 2]; gl[j * 2 + 1] = this.gUV[v * 2 + 1];
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('skinIndex', new THREE.BufferAttribute(si, 4));
    g.setAttribute('skinWeight', new THREE.BufferAttribute(sw, 4));
    g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    g.setAttribute('glyphUV', new THREE.BufferAttribute(gl, 2));
    g.setIndex(out.length > 65535 ? new THREE.Uint32BufferAttribute(out, 1) : new THREE.Uint16BufferAttribute(out, 1));
    if (opts.flip) { const ix = g.index.array; for (let t = 0; t < ix.length; t += 3) { const s = ix[t]; ix[t] = ix[t + 1]; ix[t + 1] = s; } }
    g.computeVertexNormals();
    return g;
  }
  partGeometry(part, offset = 0) {
    const T = this.partTris[part];
    let pos = this.pos;
    if (offset) {
      const nor = vertexNormals(this.pos, Uint32Array.from(T), this.n);
      pos = new Float32Array(this.pos);
      const used = new Set(T);
      for (const i of used) for (let k = 0; k < 3; k++) pos[i * 3 + k] += nor[i * 3 + k] * offset;
    }
    return this.makeGeometry(pos, T);
  }

  // skeleton with identity rest orientations
  makeSkeleton() {
    const M = this.meta, bones = [];
    M.bones.forEach((name, i) => {
      const b = new THREE.Bone(); b.name = name;
      const h = this.heads[i], p = M.parents[i];
      if (p >= 0) { b.position.copy(h).sub(this.heads[p]); bones[p].add(b); } else b.position.copy(h);
      bones.push(b);
    });
    this.bones = bones;
    this.root = bones[0];
    this.root.updateMatrixWorld(true);
    this.skeleton = new THREE.Skeleton(bones);
    return this.skeleton;
  }
  bone(name) { return this.bones[this.BI[name]]; }
}
