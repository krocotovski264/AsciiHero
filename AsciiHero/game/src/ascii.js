// GPU ASCII renderer: scene -> low-res color/material + normal/depth -> glyph shader -> bloom.
import * as THREE from 'three';
import { MAT } from './human.js';

// 16 glyphs per ramp, dark -> bright
const RAMPS = [
  " .,:;-=+icoaex%@",   // 0 skin
  " .`',;~^/\\|()%&$",  // 1 hair
  " .,:;-=+*xX#%&@@",   // 2 cloth
  " .,:;!|ilI%&#$@@",   // 3 leather
  " .,:co8oO0Q8@&#@",   // 4 chain
  " .-=+*^#H%MWM@@@",   // 5 plate
  " .-=|+#H/\\%@@@@@", // 6 steel
  " .:|!lI1][}{#%&@",   // 7 wood
  " .:*o$O&%#@@@@@@",   // 8 gold
  " .*+oO0@@@@@@@@@",   // 9 glow
  " ......:::::::::",   // 10 dark
  " ..,,::;;-~=+x%#",   // 11 stone
  "|-/\\_^<>        ",   // 12 edges
];
const MAT_RAMP = {
  [MAT.SKIN]: 0, [MAT.HAIR]: 1, [MAT.LINEN]: 2, [MAT.PANTS]: 2, [MAT.LEATHER]: 3, [MAT.LEATHER2]: 3, [MAT.CHAIN]: 4,
  [MAT.PLATE]: 5, [MAT.STEEL]: 6, [MAT.WOOD]: 7, [MAT.GOLD]: 8, [MAT.GLOW]: 9, [MAT.EYE]: 9, [MAT.DARK]: 10,
  [MAT.RED]: 2, [MAT.ROBE]: 2, [MAT.ROBED]: 2, [MAT.STONE]: 11, 18: 1,
  19: 7, 20: 7, 28: 11, 21: 2, 22: 11, 23: 9, 24: 9, 25: 6, 26: 6, 27: 6,
};
export const MATERIAL_DEFS = {
  SKIN: { id: MAT.SKIN, color: 0xe0a882, rough: 0.6, metal: 0 },
  HAIR: { id: MAT.HAIR, color: 0x5a3418, rough: 0.5, metal: 0 },
  HAIR2: { id: 18, color: 0x6a3c1a, rough: 0.5, metal: 0, side: THREE.DoubleSide },
  LINEN: { id: MAT.LINEN, color: 0xcdbb92, rough: 0.9, metal: 0 },
  PANTS: { id: MAT.PANTS, color: 0x5c5046, rough: 0.9, metal: 0 },
  LEATHER: { id: MAT.LEATHER, color: 0x8f5a2e, rough: 0.6, metal: 0 },
  LEATHER2: { id: MAT.LEATHER2, color: 0x5e3a1e, rough: 0.6, metal: 0 },
  CHAIN: { id: MAT.CHAIN, color: 0x9aa2ae, rough: 0.45, metal: 0.8 },
  PLATE: { id: MAT.PLATE, color: 0x8e96a4, rough: 0.34, metal: 0.85 },
  STEEL: { id: MAT.STEEL, color: 0xb8c2cf, rough: 0.25, metal: 1.0 },
  WOOD: { id: MAT.WOOD, color: 0x7a4f2a, rough: 0.8, metal: 0 },
  GOLD: { id: MAT.GOLD, color: 0xe8b443, rough: 0.3, metal: 1.0 },
  GLOW: { id: MAT.GLOW, color: 0x70e0ff, emissive: 0x60d8ff, rough: 0.2, metal: 0 },
  EYE: { id: MAT.EYE, color: 0xf2e8d8, emissive: 0x332a20, rough: 0.2, metal: 0 },
  DARK: { id: MAT.DARK, color: 0x101014, rough: 1, metal: 0 },
  RED: { id: MAT.RED, color: 0xb02828, rough: 0.85, metal: 0 },
  ROBE: { id: MAT.ROBE, color: 0x3f4fb8, rough: 0.85, metal: 0 },
  ROBED: { id: MAT.ROBED, color: 0x28306e, rough: 0.9, metal: 0 },
  PLANK: { id: 19, color: 0xffffff, rough: 0.85, metal: 0 },
  FLOOR: { id: 28, color: 0x5e5044, rough: 1, metal: 0, lambert: true },
  BEAM: { id: 20, color: 0x4a2e18, rough: 0.9, metal: 0 },
  PLASTER: { id: 21, color: 0xb8a888, rough: 1, metal: 0, lambert: true },
  HEARTH: { id: 22, color: 0xffffff, rough: 1, metal: 0, lambert: true },
  FIRE: { id: 23, color: 0xffa040, emissive: 0xff7a20, rough: 1, metal: 0 },
  MOON: { id: 24, color: 0x4060a0, emissive: 0x2a4a90, rough: 1, metal: 0 },
  GLASS: { id: 25, color: 0x3a7a3a, emissive: 0x0a200a, rough: 0.15, metal: 0.3 },
  GLASS2: { id: 26, color: 0x7a3a18, emissive: 0x200a04, rough: 0.15, metal: 0.3 },
  GLASS3: { id: 27, color: 0x6a6aa8, emissive: 0x101028, rough: 0.15, metal: 0.3 },
  STONE: { id: MAT.STONE, color: 0xffffff, rough: 0.95, metal: 0, lambert: true },
};

// Shared uniforms of the surface-glyph look (mode 1): characters live ON the models, in surface coordinates
// (attribute glyphUV, metres), so they wrap around limbs, follow folds and keep their size at any screen resolution.
export const GLYPH = { tGlyph: { value: null }, uRows: { value: RAMPS.length }, uExposure: { value: 1 }, uMode: { value: 1 } };
let ATLAS = null;
export function glyphAtlas() { if (!ATLAS) { ATLAS = buildAtlas(); GLYPH.tGlyph.value = ATLAS; } return ATLAS; }
// glyph cell (width, height) in metres per material family
const CELL_ACTOR = new THREE.Vector2(0.017, 0.028), CELL_PROP = new THREE.Vector2(0.03, 0.05), CELL_BIG = new THREE.Vector2(0.045, 0.075);
const BIG = new Set(['FLOOR', 'PLASTER', 'PLANK', 'HEARTH', 'STONE', 'BEAM']);

const GLYPH_VERT_HEAD = 'attribute vec2 glyphUV;\nvarying vec2 vGlyph;\n';
const GLYPH_FRAG_HEAD = `uniform sampler2D tGlyph; uniform float uRows, uExposure, uMode, uRow, uActor, uFill; uniform vec2 uCell;
varying vec2 vGlyph;
`;
const glyphFragTail = (id) => `
  if (uMode > 0.5) {
    vec3 gcol = pow(max(gl_FragColor.rgb, 0.0), vec3(1.0 / 2.2));
    float glum = dot(gcol, vec3(0.299, 0.587, 0.114));
    glum = clamp(glum * 1.15 * uExposure * (1.0 + 0.55 * uActor) + 0.06 * uActor, 0.0, 1.0);
    vec2 g = vGlyph / uCell;
    vec2 gcell = floor(g), gloc = fract(g);
    vec2 gx = dFdx(g), gy = dFdy(g);
    // brightness at the centre of this glyph cell (linear extrapolation in screen space), so a whole glyph uses one symbol
    float gdet = gx.x * gy.y - gx.y * gy.x;
    vec2 goff = gcell + 0.5 - g;
    vec2 gso = abs(gdet) > 1e-9 ? vec2(gy.y * goff.x - gy.x * goff.y, -gx.y * goff.x + gx.x * goff.y) / gdet : vec2(0.0);
    gso = clamp(gso, vec2(-6.0), vec2(6.0));
    float glc = clamp(glum + dFdx(glum) * gso.x + dFdy(glum) * gso.y, 0.0, 1.0);
    float gh = fract(sin(dot(gcell, vec2(12.9898, 78.233))) * 43758.5453);
    float gi = floor(clamp(pow(glc, 0.85) + (gh - 0.5) * 0.09, 0.0, 1.0) * 15.99);
    if (gi < 1.0 && glc > 0.02) gi = 1.0;
    vec2 gl2 = clamp(gloc, 0.06, 0.94);
    vec2 gs = vec2(1.0 / 16.0, 1.0 / uRows);
    vec2 gauv = vec2((gi + gl2.x) * gs.x, 1.0 - (uRow + 1.0) * gs.y + gl2.y * gs.y);
    float gmask = textureGrad(tGlyph, gauv, gx * gs, gy * gs).r;
    float gmx = max(max(gcol.r, gcol.g), gcol.b);
    vec3 ghue = gcol / max(gmx, 1e-3);
    vec3 gout = ghue * (0.30 + 0.85 * glc) * gmask + gcol * uFill;
    if (uRow > 8.5 && uRow < 9.5) gout = (gcol * 1.3 + 0.15) * max(gmask, 0.35);
    gl_FragColor = vec4(gout, 1.0);
  } else {
    gl_FragColor.a = ${(id / 255).toFixed(6)};
  }
}`;

// planar / cylindrical glyph coordinates for rigid props (object space, metres)
export function addGlyphUV(root) {
  root.traverse(o => {
    if (!o.isMesh || !o.geometry || o.geometry.getAttribute('glyphUV')) return;
    const g = o.geometry, P = g.getAttribute('position'); if (!P) return;
    if (!g.getAttribute('normal')) g.computeVertexNormals();
    const N = g.getAttribute('normal'), n = P.count, uv = new Float32Array(n * 2);
    const cyl = /Lathe|Cylinder|Cone|Capsule/.test(g.type) || g.userData.glyphCyl;
    let rm = 0; if (cyl) { for (let i = 0; i < n; i++) rm += Math.hypot(P.getX(i), P.getZ(i)); rm = Math.max(0.01, rm / n); }
    for (let i = 0; i < n; i++) {
      const x = P.getX(i), y = P.getY(i), z = P.getZ(i), nx = N.getX(i), ny = N.getY(i), nz = N.getZ(i);
      const ax = Math.abs(nx), ay = Math.abs(ny), az = Math.abs(nz);
      if (cyl && ay < 0.7) { uv[i * 2] = Math.atan2(x, z) * rm; uv[i * 2 + 1] = y; }
      else if (ay >= ax && ay >= az) { uv[i * 2] = x; uv[i * 2 + 1] = ny > 0 ? -z : z; }
      else if (ax >= az) { uv[i * 2] = nx > 0 ? -z : z; uv[i * 2 + 1] = y; }
      else { uv[i * 2] = nz > 0 ? x : -x; uv[i * 2 + 1] = y; }
    }
    g.setAttribute('glyphUV', new THREE.BufferAttribute(uv, 2));
  });
}

export function makeMaterials(envMap, actor = false) {
  const out = {};
  glyphAtlas();
  for (const [k, d] of Object.entries(MATERIAL_DEFS)) {
    const m = d.lambert ? new THREE.MeshLambertMaterial({ color: d.color, emissive: d.emissive || 0 }) : new THREE.MeshStandardMaterial({ color: d.color, roughness: d.rough, metalness: d.metal, emissive: d.emissive || 0, side: d.side || THREE.FrontSide, envMap: envMap || null, envMapIntensity: 0.9 });
    const id = (MAT_RAMP[d.id] !== undefined ? d.id : 0) + 1 + (actor ? 128 : 0);
    m.userData.matId = id;
    const row = MAT_RAMP[d.id] !== undefined ? MAT_RAMP[d.id] : 0;
    const own = { uRow: { value: row }, uActor: { value: actor ? 1 : 0 }, uFill: { value: actor ? 0.05 : 0.07 },
      uCell: { value: actor ? CELL_ACTOR : BIG.has(k) ? CELL_BIG : CELL_PROP } };
    m.userData.glyph = own;
    m.onBeforeCompile = (sh) => {
      Object.assign(sh.uniforms, GLYPH, own);
      sh.vertexShader = GLYPH_VERT_HEAD + sh.vertexShader.replace('#include <begin_vertex>', '#include <begin_vertex>\n  vGlyph = glyphUV;');
      sh.fragmentShader = GLYPH_FRAG_HEAD + sh.fragmentShader.replace(/}\s*$/, glyphFragTail(id));
    };
    m.customProgramCacheKey = () => 'mat' + id;
    out[k] = m;
  }
  return out;
}

const ND_MAT = new THREE.ShaderMaterial({
  vertexShader: `
    #include <common>
    #include <skinning_pars_vertex>
    varying vec3 vN; varying float vZ;
    void main(){
      #include <skinbase_vertex>
      #include <begin_vertex>
      #include <beginnormal_vertex>
      #include <skinnormal_vertex>
      #include <skinning_vertex>
      vec4 mv = modelViewMatrix * vec4(transformed,1.0);
      vN = normalize(normalMatrix * objectNormal);
      vZ = -mv.z;
      gl_Position = projectionMatrix * mv;
    }`,
  fragmentShader: `varying vec3 vN; varying float vZ; void main(){ gl_FragColor = vec4(normalize(vN)*0.5+0.5, vZ/12.0); }`,
  side: THREE.DoubleSide,
});

function buildAtlas() {
  const gw = 24, gh = 40, cols = 16, rows = RAMPS.length;
  const c = document.createElement('canvas'); c.width = gw * cols; c.height = gh * rows;
  const x = c.getContext('2d');
  x.fillStyle = '#000'; x.fillRect(0, 0, c.width, c.height);
  x.fillStyle = '#fff'; x.textAlign = 'center'; x.textBaseline = 'middle';
  x.font = `bold ${gh * 0.86}px "Courier New", ui-monospace, monospace`;
  RAMPS.forEach((r, ri) => { for (let i = 0; i < 16; i++) x.fillText(r[i] || ' ', i * gw + gw / 2, ri * gh + gh * 0.54); });
  const t = new THREE.CanvasTexture(c); t.minFilter = THREE.LinearMipMapLinearFilter; t.magFilter = THREE.LinearFilter; t.generateMipmaps = true;
  return t;
}

const FS_VERT = `varying vec2 vUv; void main(){ vUv = uv; gl_Position = vec4(position.xy,0.,1.); }`;

export class AsciiRenderer {
  constructor(canvas) {
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: false, alpha: false, powerPreference: 'high-performance' });
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.setClearColor(0x000000, 0);
    this.rtCol = new THREE.WebGLRenderTarget(2, 2, { type: THREE.HalfFloatType, magFilter: THREE.LinearFilter, minFilter: THREE.LinearFilter, depthBuffer: true });
    this.rtND = new THREE.WebGLRenderTarget(2, 2, { type: THREE.HalfFloatType, magFilter: THREE.NearestFilter, minFilter: THREE.NearestFilter, depthBuffer: true });
    this.rtAscii = new THREE.WebGLRenderTarget(2, 2, { magFilter: THREE.LinearFilter, minFilter: THREE.LinearFilter });
    this.rtB1 = new THREE.WebGLRenderTarget(2, 2, { type: THREE.HalfFloatType, magFilter: THREE.LinearFilter, minFilter: THREE.LinearFilter });
    this.rtB2 = this.rtB1.clone();
    this.atlas = glyphAtlas();
    this.rtFull = new THREE.WebGLRenderTarget(2, 2, { magFilter: THREE.LinearFilter, minFilter: THREE.LinearFilter, depthBuffer: true, samples: 0 });
    const rampMap = new Float32Array(32).fill(0);
    for (const [k, v] of Object.entries(MAT_RAMP)) rampMap[+k + 1] = v;
    this.asciiMat = new THREE.ShaderMaterial({
      uniforms: {
        tCol: { value: this.rtCol.texture }, tND: { value: this.rtND.texture }, tAtlas: { value: this.atlas },
        grid: { value: new THREE.Vector2() }, cellPx: { value: new THREE.Vector2() }, nRows: { value: RAMPS.length },
        rampMap: { value: rampMap }, time: { value: 0 }, edgeK: { value: 1 }, exposure: { value: 1 }, actorBoost: { value: 0 },
      },
      vertexShader: FS_VERT,
      fragmentShader: `
        precision highp float;
        uniform sampler2D tCol, tND, tAtlas; uniform vec2 grid, cellPx; uniform float nRows; uniform float rampMap[32]; uniform float time, edgeK, exposure, actorBoost;
        float lumOf(vec3 c){ return dot(c, vec3(0.299,0.587,0.114)); }
        void main(){
          vec2 fc = gl_FragCoord.xy;
          vec2 cell = floor(fc / cellPx);
          vec2 uv = (cell + 0.5) / grid;
          vec4 c = texture2D(tCol, uv);
          vec4 nd = texture2D(tND, uv);
          float id = floor(texture2D(tCol, uv + 0.25 / grid).a * 255.0 + 0.5);
          vec3 col = pow(max(c.rgb, 0.0), vec3(1.0/2.2));
          if (id < 0.5) { gl_FragColor = vec4(0.0,0.0,0.0,1.0); return; }
          float isA = step(127.5, id); id -= 128.0 * isA; float actor = isA * actorBoost;
          float row = rampMap[int(id)];
          float lum = clamp(lumOf(col) * 1.15 * exposure * (1.0 + 0.55 * actor) + 0.06 * actor, 0.0, 1.0);
          // edge detection on depth (silhouettes + creases)
          vec2 px = 1.0 / grid;
          float d0 = nd.a;
          float dl = texture2D(tND, uv - vec2(px.x,0.)).a, dr = texture2D(tND, uv + vec2(px.x,0.)).a;
          float dd = texture2D(tND, uv - vec2(0.,px.y)).a, du = texture2D(tND, uv + vec2(0.,px.y)).a;
          dl = dl < 1e-4 ? 1.0 : dl; dr = dr < 1e-4 ? 1.0 : dr; dd = dd < 1e-4 ? 1.0 : dd; du = du < 1e-4 ? 1.0 : du;
          float gx = dr - dl, gy = du - dd;
          float lap = abs(dl + dr - 2.0*d0) + abs(du + dd - 2.0*d0);
          float e = lap / max(d0, 1e-3);
          vec3 n0 = nd.rgb*2.-1.;
          vec3 nl = texture2D(tND, uv - vec2(px.x,0.)).rgb*2.-1., nu = texture2D(tND, uv + vec2(0.,px.y)).rgb*2.-1.;
          float crease = (1.0 - dot(n0,nl)) + (1.0 - dot(n0,nu));
          float gi;
          bool isEdge = (e > 0.12 || crease > 1.1) && edgeK > 0.5 && row < 9.5 && row != 11.0;
          if (isEdge) {
            row = 12.0;
            float ax = abs(gx), ay = abs(gy);
            if (e <= 0.12) { vec3 nn = n0 - nl; ax = abs(nn.x); ay = abs(nn.y); gx = nn.x; gy = nn.y; }
            if (ax > ay * 2.2) gi = 0.0; else if (ay > ax * 2.2) gi = 1.0; else gi = (gx * gy > 0.0) ? 3.0 : 2.0;
            lum = max(lum, 0.72 + 0.2 * actor);
          } else {
            float hsh = fract(sin(dot(cell, vec2(12.9898, 78.233))) * 43758.5453);
            gi = floor(clamp(pow(lum, 0.85) + (hsh - 0.5) * 0.09, 0.0, 1.0) * 15.99);
            if (row < 9.5 && row != 11.0 && gi < 1.0 && lum > 0.02) gi = 1.0;
          }
          vec2 local = clamp(fract(fc / cellPx), 0.08, 0.92);
          vec2 auv = vec2((gi + local.x) / 16.0, 1.0 - (row + 1.0) / nRows + local.y / nRows);
          float mask = texture2D(tAtlas, auv).r;
          float m = max(max(col.r, col.g), col.b);
          vec3 hue = col / max(m, 1e-3);
          vec3 outc = hue * (0.30 + 0.85 * lum);
          if (row > 8.5 && row < 9.5) outc = col * 1.3 + 0.15;
          gl_FragColor = vec4(outc * mask, 1.0);
        }`,
      depthTest: false, depthWrite: false,
    });
    // blur + composite
    this.blurMat = new THREE.ShaderMaterial({
      uniforms: { tSrc: { value: null }, dir: { value: new THREE.Vector2() } },
      vertexShader: FS_VERT,
      fragmentShader: `uniform sampler2D tSrc; uniform vec2 dir; varying vec2 vUv;
        void main(){ vec3 s = texture2D(tSrc, vUv).rgb*0.227;
          s += (texture2D(tSrc, vUv+dir*1.384).rgb + texture2D(tSrc, vUv-dir*1.384).rgb)*0.316;
          s += (texture2D(tSrc, vUv+dir*3.230).rgb + texture2D(tSrc, vUv-dir*3.230).rgb)*0.070;
          gl_FragColor = vec4(s,1.); }`,
      depthTest: false, depthWrite: false,
    });
    this.compMat = new THREE.ShaderMaterial({
      uniforms: { tA: { value: this.rtAscii.texture }, tB: { value: this.rtB2.texture }, bloom: { value: 0.55 } },
      vertexShader: FS_VERT,
      fragmentShader: `uniform sampler2D tA, tB; uniform float bloom; varying vec2 vUv;
        void main(){ vec3 a = texture2D(tA, vUv).rgb; vec3 b = texture2D(tB, vUv).rgb;
          vec3 bg = mix(vec3(0.018,0.02,0.032), vec3(0.05,0.055,0.08), smoothstep(1.1,0.0,length(vUv-vec2(0.5,0.55))*1.6));
          gl_FragColor = vec4(bg + a + b*bloom*1.6, 1.); }`,
      depthTest: false, depthWrite: false,
    });
    this.quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2));
    this.fsScene = new THREE.Scene(); this.fsScene.add(this.quad);
    this.fsCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  }
  setSize(wCss, hCss, dpr, cellCss) {
    this.renderer.setPixelRatio(dpr);
    this.renderer.setSize(wCss, hCss, false);
    const W = Math.round(wCss * dpr), H = Math.round(hCss * dpr);
    const cw = cellCss * dpr, ch = cw / 0.6;
    const cols = Math.floor(W / cw), rows = Math.floor(H / ch);
    this.cols = cols; this.rows = rows;
    this.asciiMat.uniforms.grid.value.set(cols, rows);
    this.asciiMat.uniforms.cellPx.value.set(W / cols, H / rows);
    this.rtCol.setSize(cols * 2, rows * 2);
    this.rtND.setSize(cols, rows);
    this.rtAscii.setSize(W, H);
    this.rtFull.setSize(W, H);
    this.rtB1.setSize(Math.ceil(W / 4), Math.ceil(H / 4)); this.rtB2.setSize(Math.ceil(W / 4), Math.ceil(H / 4));
    this.W = W; this.H = H;
  }
  render(scene, camera, t) {
    const r = this.renderer;
    this.asciiMat.uniforms.time.value = t;
    r.setClearColor(0x000000, 0);
    let src;
    GLYPH.uExposure.value = this.asciiMat.uniforms.exposure.value;
    if (GLYPH.uMode.value > 0.5) {
      // surface glyphs: the models themselves are drawn with characters at full resolution
      r.setRenderTarget(this.rtFull); r.clear(); r.render(scene, camera);
      src = this.rtFull;
    } else {
      // screen grid (beta 0.8): low-res render -> glyph per screen cell
      r.setRenderTarget(this.rtCol); r.clear(); r.render(scene, camera);
      scene.overrideMaterial = ND_MAT;
      r.setRenderTarget(this.rtND); r.setClearColor(0x000000, 0); r.clear(); r.render(scene, camera);
      scene.overrideMaterial = null;
      this.quad.material = this.asciiMat; r.setRenderTarget(this.rtAscii); r.render(this.fsScene, this.fsCam);
      src = this.rtAscii;
    }
    this.compMat.uniforms.tA.value = src.texture;
    // bloom
    this.quad.material = this.blurMat;
    const bw = this.rtB1.width, bh = this.rtB1.height;
    this.blurMat.uniforms.tSrc.value = src.texture; this.blurMat.uniforms.dir.value.set(1.6 / bw, 0); r.setRenderTarget(this.rtB1); r.render(this.fsScene, this.fsCam);
    this.blurMat.uniforms.tSrc.value = this.rtB1.texture; this.blurMat.uniforms.dir.value.set(0, 1.6 / bh); r.setRenderTarget(this.rtB2); r.render(this.fsScene, this.fsCam);
    this.blurMat.uniforms.tSrc.value = this.rtB2.texture; this.blurMat.uniforms.dir.value.set(3.2 / bw, 0); r.setRenderTarget(this.rtB1); r.render(this.fsScene, this.fsCam);
    this.blurMat.uniforms.tSrc.value = this.rtB1.texture; this.blurMat.uniforms.dir.value.set(0, 3.2 / bh); r.setRenderTarget(this.rtB2); r.render(this.fsScene, this.fsCam);
    this.quad.material = this.compMat; r.setRenderTarget(null); r.render(this.fsScene, this.fsCam);
  }
}
