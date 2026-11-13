import * as THREE from 'three';
import { loadHuman, MAT } from './human.js';
import { buildCharacter, ARMORS, WEAPONS, BUILDS } from './character.js';
import { AsciiRenderer, makeMaterials, buildGlyphLayer, enableShift, GLYPH, GLYPH_STATS } from './ascii.js';
import { buildTavern } from './tavern.js';
import { buildField, LIMBS, LIMB_NAME } from './field.js';
import { applyPose } from './pose.js';
import { makeActor, animateActor, locomotion, loadMocap, setLimbs, startAttack } from './anim.js';

const $ = id => document.getElementById(id);
const DEF = { sex: 'm', height: 180, build: 1, armor: 'chain', weapon: 'shield' };
let cfg = Object.assign({}, DEF);
try { const s = JSON.parse(localStorage.getItem('ascii_hero3d') || 'null'); if (s) cfg = Object.assign(cfg, s); } catch (e) {}
const CAM0 = { yaw: 0.5, pitch: 0.1, dist: 3.7, ty: 0.95, fov: 36 * Math.PI / 180 };
const cam = Object.assign({}, CAM0);
let autoRotate = true, dirty = true, interacting = false, vel = 0, lastInteract = 0;

// ---------- UI ----------
const ICON = {
  cloth: " _ _ \n/| |\\\n |_| \n | | ", leather: " ___ \n/|#|\\\n |=| \n | | ",
  chain: " ooo \n/o8o\\\n o8o \n | | ", plate: " [=] \n/[#]\\\n [#] \n || ",
  robe: "  ^  \n /o\\ \n/   \\\n/___\\",
  none: "     \n  o  \n /|\\ \n  ^  ", sword: "  |  \n  |  \n -+- \n  o  ",
  shield: " /-\\ \n |+|-\n \\_/|\n    o", axe: " <|  \n  |  \n  |  \n  |  ",
  spear: "  ^  \n  |  \n  |  \n  |  ", staff: "  @  \n  Y  \n  |  \n  |  "
};
document.querySelectorAll('.tabs button').forEach(b => b.onclick = () => {
  document.querySelectorAll('.tabs button').forEach(x => x.classList.toggle('on', x === b));
  document.querySelectorAll('.tab').forEach(t => t.classList.toggle('on', t.id === 'tab-' + b.dataset.tab));
});
$('build').innerHTML = BUILDS.map((b, i) => `<button data-v="${i}">${b}</button>`).join('');
const optHTML = list => list.map(o => `<button class="opt" data-v="${o.id}"><span class="ico">${ICON[o.id] || ''}</span><span class="t"><span class="n">${o.name}</span><span class="d">${o.desc}</span></span></button>`).join('');
$('armors').innerHTML = optHTML(ARMORS); $('weapons').innerHTML = optHTML(WEAPONS);
const sexName = { m: 'Мужчина', f: 'Женщина' };
function syncUI() {
  $('sex').querySelectorAll('button').forEach(b => b.classList.toggle('on', b.dataset.v === cfg.sex));
  $('build').querySelectorAll('button').forEach(b => b.classList.toggle('on', +b.dataset.v === cfg.build));
  $('armors').querySelectorAll('.opt').forEach(b => b.classList.toggle('on', b.dataset.v === cfg.armor));
  $('weapons').querySelectorAll('.opt').forEach(b => b.classList.toggle('on', b.dataset.v === cfg.weapon));
  $('height').value = cfg.height; $('hval').textContent = cfg.height + ' см';
  const a = ARMORS.find(x => x.id === cfg.armor), w = WEAPONS.find(x => x.id === cfg.weapon);
  $('badge').innerHTML = `<b>${sexName[cfg.sex]}</b> · ${cfg.height} см · ${BUILDS[cfg.build]}\n${a.name}\n${w.name}`;
}
let rebuildPending = false;
function setCfg(k, v) {
  const changedShape = k !== 'height';
  cfg[k] = v; syncUI();
  if (changedShape) rebuildPending = true; else applyHeight();
  dirty = true;
  try { localStorage.setItem('ascii_hero3d', JSON.stringify(cfg)); } catch (e) {}
}
$('sex').onclick = e => { const b = e.target.closest('button'); if (b) setCfg('sex', b.dataset.v); };
$('build').onclick = e => { const b = e.target.closest('button'); if (b) setCfg('build', +b.dataset.v); };
$('armors').onclick = e => { const b = e.target.closest('.opt'); if (b) setCfg('armor', b.dataset.v); };
$('weapons').onclick = e => { const b = e.target.closest('.opt'); if (b) setCfg('weapon', b.dataset.v); };
$('height').oninput = e => setCfg('height', +e.target.value);
$('hminus').onclick = () => setCfg('height', Math.max(150, cfg.height - 1));
$('hplus').onclick = () => setCfg('height', Math.min(200, cfg.height + 1));
$('auto').onclick = () => { autoRotate = !autoRotate; $('auto').classList.toggle('on', autoRotate); };
$('reset').onclick = () => { Object.assign(cam, CAM0); };
$('rand').onclick = () => {
  const r = a => a[Math.floor(Math.random() * a.length)];
  cfg.sex = r(['m', 'f']); cfg.height = Math.round((cfg.sex === 'f' ? 155 : 168) + Math.random() * 28);
  cfg.build = Math.floor(Math.random() * 4); cfg.armor = r(ARMORS).id; cfg.weapon = r(WEAPONS).id; setCfg('sex', cfg.sex);
};
function toast(t) { const el = $('toast'); el.textContent = t; el.classList.add('show'); clearTimeout(toast._t); toast._t = setTimeout(() => el.classList.remove('show'), 2600); }
$('done').onclick = () => { try { localStorage.setItem('ascii_hero3d', JSON.stringify(cfg)); } catch (e) {} enterWorld('tavern'); };
$('field').onclick = () => { try { localStorage.setItem('ascii_hero3d', JSON.stringify(cfg)); } catch (e) {} enterWorld('field'); };
$('dmg').onclick = () => damage(10);
$('back').onclick = () => leaveWorld();
// where the characters live: on the models (surface glyphs) or on a screen grid (beta 0.8)
try { const gm = localStorage.getItem('ascii_hero_gmode'); if (gm !== null) GLYPH.uMode.value = +gm; } catch (e) {}
function syncGMode() { const on = GLYPH.uMode.value > 0.5; $('gmode').classList.toggle('on', on); $('gmode').textContent = on ? 'Aa на моделях' : 'Aa сеткой экрана'; }
$('gmode').onclick = () => { GLYPH.uMode.value = GLYPH.uMode.value > 0.5 ? 0 : 1; try { localStorage.setItem('ascii_hero_gmode', GLYPH.uMode.value); } catch (e) {} syncGMode(); resize(); };
syncGMode();
$('lock').onclick = () => {
  lockOn = !lockOn; $('lock').classList.toggle('on', lockOn);
  lockFixed = null;
  if (lockOn && bartender && loc === tavern && bartender.char.group.position.distanceTo(new THREE.Vector3(pos.x, 0, pos.z)) > 10) {
    lockFixed = new THREE.Vector3(pos.x + Math.sin(player.heading) * 20, 0, pos.z + Math.cos(player.heading) * 20);
  }
  toast(lockOn ? 'Фиксация на цели: трактирщик\nСтик — стрейф 8 направлений' : 'Свободное движение');
};
syncUI();

// ---------- scene ----------
const canvas = $('cv');
const AR = new AsciiRenderer(canvas);
const scene = new THREE.Scene();
const pmrem = new THREE.PMREMGenerator(AR.renderer);
const envScene = new THREE.Scene();
envScene.add(new THREE.Mesh(new THREE.SphereGeometry(10, 16, 8), new THREE.MeshBasicMaterial({ color: 0x223040, side: THREE.BackSide })));
const envL = new THREE.Mesh(new THREE.PlaneGeometry(6, 6), new THREE.MeshBasicMaterial({ color: 0xfff2dd })); envL.position.set(4, 6, 5); envL.lookAt(0, 0, 0); envScene.add(envL);
const envL2 = new THREE.Mesh(new THREE.PlaneGeometry(4, 8), new THREE.MeshBasicMaterial({ color: 0x88aaff })); envL2.position.set(-6, 3, -4); envL2.lookAt(0, 0, 0); envScene.add(envL2);
const env = pmrem.fromScene(envScene, 0.04).texture;
const mats = makeMaterials(env);
const actorMats = makeMaterials(env, true);

const hemi = new THREE.HemisphereLight(0xc8d4ff, 0x302418, 0.55); scene.add(hemi);
const key = new THREE.DirectionalLight(0xfff0dc, 3.0);
key.castShadow = true; key.shadow.mapSize.set(1024, 1024);
Object.assign(key.shadow.camera, { left: -1.3, right: 1.3, top: 2.4, bottom: -0.2, near: 0.5, far: 9 });
key.shadow.bias = -0.0006; key.shadow.normalBias = 0.01;
scene.add(key); scene.add(key.target);
const rim = new THREE.DirectionalLight(0x9ab8ff, 2.2); scene.add(rim);
const fill = new THREE.DirectionalLight(0xffd0a0, 0.5); scene.add(fill);

// floor: stone disc with procedural texture
function floorTexture() {
  const S = 512, c = document.createElement('canvas'); c.width = c.height = S; const x = c.getContext('2d');
  x.fillStyle = '#000'; x.fillRect(0, 0, S, S);
  const cx = S / 2, R = S / 2;
  const rp = R * 0.36;
  const g = x.createRadialGradient(cx, cx, 0, cx, cx, rp); g.addColorStop(0, '#5a544a'); g.addColorStop(1, '#3a352e');
  x.fillStyle = g; x.beginPath(); x.arc(cx, cx, rp, 0, Math.PI * 2); x.fill();
  x.strokeStyle = '#2a2620'; x.lineWidth = 3;
  for (const rr of [0.33, 0.2]) { x.beginPath(); x.arc(cx, cx, R * rr, 0, Math.PI * 2); x.stroke(); }
  for (let a = 0; a < 12; a++) { const t = a / 12 * Math.PI * 2; x.beginPath(); x.moveTo(cx + Math.cos(t) * R * 0.2, cx + Math.sin(t) * R * 0.2); x.lineTo(cx + Math.cos(t) * R * 0.33, cx + Math.sin(t) * R * 0.33); x.stroke(); }
  for (let a = 0; a < 6; a++) { const t = a / 6 * Math.PI * 2 + 0.3; x.beginPath(); x.moveTo(cx, cx); x.lineTo(cx + Math.cos(t) * R * 0.2, cx + Math.sin(t) * R * 0.2); x.stroke(); }
  x.strokeStyle = '#8a7c60'; x.lineWidth = 5; x.beginPath(); x.arc(cx, cx, rp - 3, 0, Math.PI * 2); x.stroke();
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; return t;
}
const floorMat = mats.STONE; floorMat.map = floorTexture(); floorMat.color.set(0xffffff); floorMat.needsUpdate = true;
const floor = new THREE.Mesh(new THREE.CircleGeometry(2.6, 64), floorMat);
floor.rotation.x = -Math.PI / 2; floor.receiveShadow = true; scene.add(floor); buildGlyphLayer(floor);

const camera = new THREE.PerspectiveCamera(36, 1, 0.05, 30);

// ---------- character ----------
let char = null, player = null;
function applyHeight() {
  if (!char) return;
  const s = cfg.height / 100 / char.height, sxz = 1 + (s - 1) * 0.45;
  char.group.scale.set(sxz, s, sxz);
}
function rebuild() {
  if (char) {
    scene.remove(char.group);
    char.group.traverse(o => { if (o.geometry) o.geometry.dispose(); });
  }
  char = buildCharacter(Object.assign({ shadow: true }, cfg), actorMats);
  // armour pieces (every shell, the weapon, the shield...) can be offset on their own: the dash sends the shadow ahead and the armour after it
  { const byKey = new Map(); char.pieces = [];
    char.group.updateMatrixWorld(true);
    char.group.traverse(o => { if (!o.isMesh || o.material === actorMats.SHADOW || o.material === actorMats.EYE) return;
      const key = o.isSkinnedMesh ? o : o.parent; let pc = byKey.get(key);
      if (!pc) { pc = { shifts: [], delay: 0, dur: 0.15, axis: new THREE.Vector3(0, 1, 0), turns: 1, side: 0 }; byKey.set(key, pc); char.pieces.push(pc);
        // the point the piece tumbles about: its centre, carried by the nearest bone
        if (!o.geometry.boundingBox) o.geometry.computeBoundingBox();
        const c = o.geometry.boundingBox.getCenter(new THREE.Vector3()); if (!o.isSkinnedMesh) c.applyMatrix4(o.matrixWorld);
        let best = null, bd = 1e9; for (const b of char.skeleton.bones) { const d = b.getWorldPosition(new THREE.Vector3()).distanceTo(c); if (d < bd) { bd = d; best = b; } }
        pc.bone = best; pc.off = best.worldToLocal(c.clone()); }
      pc.shifts.push(enableShift(o)); }); }
  buildGlyphLayer(char.group);
  scene.add(char.group);
  char.cfgWeapon = cfg.weapon;
  player = makeActor(char);
  setLimbs(player, body.limbs);
  applyHeight();
  if (mode === 'world') placePlayerInWorld();
}
// ---------- input ----------
const ptr = new Map(); let pinch0 = null, lastTap = 0; let W = 1, H = 1;
const camDrag = { id: null, x: 0, y: 0 };
canvas.addEventListener('pointerdown', e => {
  if (mode === 'world') { if (camDrag.id === null) { camDrag.id = e.pointerId; camDrag.x = e.clientX; camDrag.y = e.clientY; canvas.setPointerCapture(e.pointerId); } return; }
  canvas.setPointerCapture(e.pointerId); ptr.set(e.pointerId, { x: e.clientX, y: e.clientY });
  interacting = true; vel = 0; hideHint();
  if (ptr.size === 2) { const [a, b] = [...ptr.values()]; pinch0 = { d: Math.hypot(a.x - b.x, a.y - b.y), dist: cam.dist, my: (a.y + b.y) / 2, ty: cam.ty }; }
  const now = performance.now(); if (ptr.size === 1 && now - lastTap < 300) Object.assign(cam, CAM0); lastTap = now;
});
canvas.addEventListener('pointermove', e => {
  if (mode === 'world') {
    if (e.pointerId !== camDrag.id) return;
    const dx = e.clientX - camDrag.x, dy = e.clientY - camDrag.y; camDrag.x = e.clientX; camDrag.y = e.clientY;

    return;
  }
  const p = ptr.get(e.pointerId); if (!p) return;
  const dx = e.clientX - p.x, dy = e.clientY - p.y; p.x = e.clientX; p.y = e.clientY;
  if (ptr.size === 1) { const k = 6.0 / Math.max(W, 320); cam.yaw -= dx * k; vel = -dx * k; cam.pitch = Math.max(-0.2, Math.min(0.75, cam.pitch + dy * k * 0.6)); }
  else if (ptr.size === 2 && pinch0) {
    const [a, b] = [...ptr.values()]; const d = Math.hypot(a.x - b.x, a.y - b.y);
    cam.dist = Math.max(0.9, Math.min(6, pinch0.dist * pinch0.d / Math.max(20, d)));
    const my = (a.y + b.y) / 2; cam.ty = Math.max(0.2, Math.min(1.9, pinch0.ty + (my - pinch0.my) / H * cam.dist * 0.8));
  }
  lastInteract = performance.now();
});
const up = e => { if (e.pointerId === camDrag.id) camDrag.id = null; ptr.delete(e.pointerId); if (ptr.size < 2) pinch0 = null; if (ptr.size === 0) { interacting = false; lastInteract = performance.now(); } };
canvas.addEventListener('pointerup', up); canvas.addEventListener('pointercancel', up);
canvas.addEventListener('wheel', e => { e.preventDefault(); cam.dist = Math.max(0.9, Math.min(6, cam.dist * (1 + e.deltaY * 0.001))); }, { passive: false });
let hintHidden = false; function hideHint() { if (!hintHidden) { hintHidden = true; $('hint').style.opacity = 0; } }
setTimeout(hideHint, 6000);

function resize() {
  const r = $('view').getBoundingClientRect(); W = Math.max(1, r.width); H = Math.max(1, r.height);
  const dpr = Math.min(GLYPH.uMode.value > 0.5 ? 2 : 2.5, window.devicePixelRatio || 1);
  const cell = Math.max(4 / dpr, mode === 'world' ? Math.min(1.7, W / 540) : Math.min(2.0, W / 310));
  AR.setSize(W, H, dpr, cell * QUALITY);
  camera.aspect = W / H; camera.updateProjectionMatrix();
}
let QUALITY = 1;
// prefer landscape (works when fullscreen / installed); harmless elsewhere
function tryLandscape() { try { screen.orientation && screen.orientation.lock && screen.orientation.lock('landscape').catch(() => {}); } catch (e) {} }
addEventListener('pointerdown', tryLandscape, { once: true });
const rot = document.getElementById('rotate'); if (rot) document.getElementById('rotOk').onclick = () => document.body.classList.add('portrait-ok');
window.addEventListener('resize', resize); new ResizeObserver(resize).observe($('view'));

// ---------- world (tavern) ----------
let mode = 'editor', tavern = null, bartender = null, field = null, loc = null;
// integrity instead of a health bar: five limbs, each worth 20 %
const body = { hp: 100, limbs: { head: true, armL: true, armR: true, legL: true, legR: true } };
const limbCount = () => LIMBS.filter(l => body.limbs[l]).length;
function damage(pct) {
  if (body.hp <= 0) return;
  body.hp = Math.max(0, body.hp - pct);
  const lost = [];
  while (limbCount() > Math.ceil(body.hp / 20)) { const have = LIMBS.filter(l => body.limbs[l]); const l = have[Math.floor(Math.random() * have.length)]; spawnDebris(l); body.limbs[l] = false; lost.push(LIMB_NAME[l]); }
  if (player) setLimbs(player, body.limbs);
  if (body.hp <= 0) { toast('Доспех разрушен — тень рассеялась'); setTimeout(() => { restoreBody(); if (loc) { pos.copy(loc.spawn); player.sim.init = false; } }, 1800); }
  else toast('Урон −' + pct + '% · целостность ' + body.hp + '%' + (lost.length ? '\nОторвано: ' + lost.join(', ') : ''));
}
// a torn-off limb bursts out of the body in a cloud of smoke and comes apart at the joints: every piece flies its own
// way, tumbles, bounces, lies for a moment and burns away
const debris = [], puffs = [];
const SEG = s => ({
  arm: [['upperarm01.' + s, 'lowerarm01.' + s, 0.05], ['lowerarm01.' + s, 'wrist.' + s, 0.042], ['wrist.' + s, null, 'hand']],
  leg: [['upperleg01.' + s, 'lowerleg01.' + s, 0.078], ['lowerleg01.' + s, 'foot.' + s, 0.058], ['foot.' + s, null, 'foot']] });
const DEBRIS_SEGS = { head: [['head', null, 'head']], armL: SEG('L').arm, armR: SEG('R').arm, legL: SEG('L').leg, legR: SEG('R').leg };
function spawnPuff(p, size, v, life) {
  const puff = { value: 0.001 };
  const m = new THREE.Mesh(new THREE.SphereGeometry(size, 10, 7), actorMats.SHADOW); m.userData.puff = puff; m.position.copy(p);
  scene.add(m); buildGlyphLayer(m);
  puffs.push({ m, puff, t: 0, life: life || 0.9 + size * 4, v: v ? v.clone() : new THREE.Vector3() });
}
function spawnDebris(l) {
  if (!char || mode !== 'world') return;
  const segs = DEBRIS_SEGS[l], P = n => { const b = n && char.human.bone(n); return b ? b.getWorldPosition(new THREE.Vector3()) : null; };
  const p0 = P(segs[0][0]); if (!p0) return;
  const hv = player ? new THREE.Vector3(player.sim.vx, 0, player.sim.vz) : new THREE.Vector3();
  // outwards from the body; the head goes up and back
  const out = new THREE.Vector3(p0.x - pos.x, 0, p0.z - pos.z); if (out.lengthSq() < 4e-3) out.set(Math.random() - 0.5, 0, Math.random() - 0.5); out.normalize();
  spawnPuff(p0, l === 'head' ? 0.11 : l[0] === 'a' ? 0.085 : 0.11, hv.clone().multiplyScalar(0.5).add(new THREE.Vector3(out.x * 0.5, 0.5, out.z * 0.5)));
  for (const [an, bn, kind] of segs) {
    const a = P(an), b = P(bn); if (!a) continue;
    let geo, r, c = a.clone(); const q = new THREE.Quaternion();
    if (b) { const d = b.clone().sub(a), len = Math.max(0.08, d.length()); geo = new THREE.CylinderGeometry(kind, kind * 0.82, len, 8); r = kind; c.addScaledVector(d, 0.5); q.setFromUnitVectors(new THREE.Vector3(0, -1, 0), d.normalize()); }
    else if (kind === 'head') { geo = new THREE.SphereGeometry(0.13, 10, 8); r = 0.13; }
    else if (kind === 'hand') { geo = new THREE.BoxGeometry(0.085, 0.16, 0.04); r = 0.03; c.y -= 0.07; }
    else { geo = new THREE.BoxGeometry(0.095, 0.075, 0.25); r = 0.04; c.y -= 0.04; }
    const burn = { value: 0 }, m = new THREE.Mesh(geo, actorMats.PLATE); m.userData.burn = burn; m.castShadow = true;
    m.position.copy(c); m.quaternion.copy(q);
    scene.add(m); buildGlyphLayer(m);
    // each piece gets its own direction (a wide fan around "outwards"), speed and spin
    const ang = (Math.random() - 0.5) * 2.2, cs = Math.cos(ang), sn = Math.sin(ang), sp = 1.2 + Math.random() * 2.2;
    const v = new THREE.Vector3((out.x * cs - out.z * sn) * sp, 2.2 + Math.random() * 2.6 + (kind === 'head' ? 1.2 : 0), (out.x * sn + out.z * cs) * sp).add(hv.clone().multiplyScalar(0.6));
    const w = new THREE.Vector3(Math.random() - 0.5, Math.random() - 0.5, Math.random() - 0.5).normalize().multiplyScalar(5 + Math.random() * 11);
    debris.push({ m, burn, v, w, r, t: 0, rest: false, trail: 0.12, life: 5 + Math.random() * 0.8 });
  }
}
function updateDebris(dt) {
  for (let i = debris.length - 1; i >= 0; i--) {
    const d = debris[i]; d.t += dt;
    if (!d.rest) {
      d.v.y -= 9.8 * dt; d.v.multiplyScalar(1 - 0.25 * dt); d.m.position.addScaledVector(d.v, dt);
      const wl = d.w.length(); if (wl > 1e-3) d.m.quaternion.premultiply(new THREE.Quaternion().setFromAxisAngle(d.w.clone().divideScalar(wl), wl * dt));
      if (d.m.position.y < d.r) {
        d.m.position.y = d.r;
        if (d.v.y < 0) d.v.y *= -0.38; d.v.x *= 0.62; d.v.z *= 0.62;
        // on the ground the spin follows the roll and dies out
        d.w.set(d.v.z / Math.max(d.r, 0.04), d.w.y * 0.4, -d.v.x / Math.max(d.r, 0.04)).multiplyScalar(0.5);
        if (Math.abs(d.v.y) < 0.5 && Math.hypot(d.v.x, d.v.z) < 0.25) { d.rest = true; d.v.set(0, 0, 0); }
      }
      // smoke keeps pouring out of the piece while it flies
      d.trail -= dt; if (d.trail <= 0 && d.t < 0.75) { d.trail = 0.22; spawnPuff(d.m.position, 0.05, d.v.clone().multiplyScalar(0.25)); }
    }
    d.burn.value = Math.max(0, Math.min(1, (d.t - 1.6) / (d.life - 2)));          // lies for a moment, then burns for about three seconds
    if (d.t > d.life) { scene.remove(d.m); d.m.traverse(o => { if (o.geometry) o.geometry.dispose(); }); debris.splice(i, 1); }
  }
  for (let i = puffs.length - 1; i >= 0; i--) {
    const f = puffs[i]; f.t += dt; f.v.multiplyScalar(1 - 2.5 * dt); f.m.position.addScaledVector(f.v, dt);
    const u = f.t / f.life; f.puff.value = Math.max(0.001, 1 - (1 - u) * (1 - u));        // fast at first, then hanging in the air
    if (u >= 1) { scene.remove(f.m); f.m.traverse(o => { if (o.geometry) o.geometry.dispose(); }); puffs.splice(i, 1); }
  }
}
function restoreBody() { body.hp = 100; for (const l of LIMBS) body.limbs[l] = true; if (player) setLimbs(player, body.limbs); }
function pickups() {
  if (!loc || !loc.piles || body.hp <= 0) return;
  for (const p of loc.piles) {
    if (Math.hypot(pos.x - p.x, pos.z - p.z) > 0.95) continue;
    const got = [];
    for (const l of LIMBS) if (!body.limbs[l] && loc.take(p, l)) { body.limbs[l] = true; body.hp = Math.min(100, body.hp + 20); got.push(LIMB_NAME[l]); }
    if (got.length) { setLimbs(player, body.limbs); toast('Подобрано: ' + got.join(', ') + '\nЦелостность ' + body.hp + '%'); }
  }
}
const worldLights = new THREE.Group();
const pos = new THREE.Vector3(), camPos = new THREE.Vector3(), camTgt = new THREE.Vector3();
let moonLight = null;
let heading = 0, heroLight = null, lockOn = false, lockFixed = null, labelT = 0;
// Diablo-like isometric camera: fixed diagonal yaw, steep pitch, narrow FOV
// MOBA camera: fixed (no rotation), high and far, looking down at about 55 degrees
const ISO_LOD = 2.5 / 1.5;
const ISO = { yaw: 0, pitch: 0.96, dist: 7.6 * ISO_LOD, fov: 30, yawT: 0, pitchT: 0.96 };
const ISO_FOV = ISO.fov;
const ISO_OFF = new THREE.Vector3();
function updIso() { ISO_OFF.set(Math.sin(ISO.yaw) * Math.cos(ISO.pitch), Math.sin(ISO.pitch), Math.cos(ISO.yaw) * Math.cos(ISO.pitch)).multiplyScalar(ISO.dist); }
updIso();
const editorLights = [hemi, key, rim, fill];
function setupWorldLights() {
  const amb = new THREE.HemisphereLight(0x50465a, 0x18100a, 0.32); worldLights.add(amb);
  heroLight = new THREE.PointLight(0xffe6c8, 3.6, 4.2, 1.7); worldLights.add(heroLight);
  const moon = new THREE.DirectionalLight(0x8aa6ff, 0.9); moon.position.set(1.5, 7.5, -7); moon.target.position.set(0.5, 0, 0.5);
  moon.castShadow = true; moon.shadow.mapSize.set(1024, 1024);
  Object.assign(moon.shadow.camera, { left: -7, right: 7, top: 7, bottom: -7, near: 1, far: 20 });
  moon.shadow.bias = -0.0008; moon.shadow.normalBias = 0.02;
  worldLights.add(moon); worldLights.add(moon.target); moonLight = moon;
}
function placePlayerInWorld() {
  if (!char) return;
  char.group.position.copy(pos); char.group.rotation.y = heading;
}
function enterWorld(which = 'tavern') {
  if (which === 'field' && !field) { if (!heroLight) setupWorldLights(); field = buildField(mats); buildGlyphLayer(field.group); }
  if (!tavern && which === 'tavern') {
    tavern = buildTavern(mats); if (!heroLight) setupWorldLights(); buildGlyphLayer(tavern.group);
    const bt = buildCharacter({ sex: 'm', build: 3, height: 178, armor: 'cloth', weapon: 'none' }, actorMats);
    bt.cfgWeapon = 'none';
    const s = 1.78 / bt.height; bt.group.scale.set(1 + (s - 1) * 0.45, s, 1 + (s - 1) * 0.45);
    bt.group.position.copy(tavern.bartender); buildGlyphLayer(bt.group);
    bartender = makeActor(bt);
    tavern.colliders.push({ x: tavern.bartender.x, z: tavern.bartender.z, r: 0.3 });
  }
  mode = 'world'; document.body.classList.add('world');
  loc = which === 'field' ? field : tavern;
  scene.add(loc.group); scene.add(worldLights); if (loc === tavern) scene.add(bartender.char.group);
  floor.visible = false; editorLights.forEach(l => l.visible = false);
  pos.copy(loc.spawn); heading = Math.PI; player.heading = Math.PI; player.sim.init = false; player.noLock = false; placePlayerInWorld();
  camTgt.set(pos.x, 0.6, pos.z); camPos.copy(camTgt).add(ISO_OFF);
  GLYPH.uLod.value = ISO_LOD;
  camera.fov = ISO_FOV; camera.updateProjectionMatrix(); AR.asciiMat.uniforms.exposure.value = 1.3; AR.asciiMat.uniforms.actorBoost.value = 1;
  $('title').innerHTML = loc === field ? '🌾 ТЕСТОВОЕ ПОЛЕ<small>трава · кучи брони</small>' : '🍺 «ПЬЯНЫЙ ДРАКОН»<small>трактир</small>';
  $('badge').innerHTML = `<b>${sexName[cfg.sex]}</b> · ${cfg.height} см\nСтик — движение`;
  $('hint').style.opacity = 0;
  resize();
}
function leaveWorld() {
  mode = 'editor'; document.body.classList.remove('world');
  scene.remove(loc.group); scene.remove(worldLights); if (bartender) scene.remove(bartender.char.group);
  GLYPH.uHeroA.value.set(0, -99, 0); GLYPH.uHeroB.value.set(0, -99, 0);
  floor.visible = true; editorLights.forEach(l => l.visible = true);
  char.group.position.set(0, 0, 0); char.group.rotation.y = 0; player.speed = 0; player.heading = 0; player.sim.init = false; lockOn = false; $('lock').classList.remove('on');
  GLYPH.uLod.value = 1;
  camera.fov = 36; camera.updateProjectionMatrix(); AR.asciiMat.uniforms.exposure.value = 1.0; AR.asciiMat.uniforms.actorBoost.value = 0;
  $('title').innerHTML = '⚔ СОЗДАНИЕ ГЕРОЯ<small>beta 0.4</small>';
  syncUI(); resize();
}
// joystick
const stickEl = $('stick'), knob = $('knob'); const stick = { x: 0, y: 0, id: null, run: 0 };
function stickMove(e) {
  const r = stickEl.getBoundingClientRect(), R = r.width / 2;
  let dx = e.clientX - (r.left + R), dy = e.clientY - (r.top + R);
  const l = Math.hypot(dx, dy), max = R * 0.78;
  if (l > max) { dx *= max / l; dy *= max / l; }
  knob.style.transform = `translate(${dx}px,${dy}px)`;
  stick.x = dx / max; stick.y = dy / max;
  // run: finger pulled past the ring by about 1 cm or more (1 cm ~ 63 CSS px on a phone)
  stick.run = Math.max(0, Math.min(1, (l - max - 38) / 34));
}
stickEl.addEventListener('pointerdown', e => { stick.id = e.pointerId; stickEl.setPointerCapture(e.pointerId); stickMove(e); e.preventDefault(); });
stickEl.addEventListener('pointermove', e => { if (e.pointerId === stick.id) stickMove(e); });
const stickUp = e => { if (e.pointerId !== stick.id) return; stick.id = null; stick.x = stick.y = 0; stick.run = 0; knob.style.transform = ''; };
stickEl.addEventListener('pointerup', stickUp); stickEl.addEventListener('pointercancel', stickUp);
const keys = {};
addEventListener('keydown', e => { keys[e.code] = true; }); addEventListener('keyup', e => { keys[e.code] = false; });
function collide(p, r) {
  const T = loc;
  for (let it = 0; it < 2; it++) for (const c of T.colliders) {
    if (c.r !== undefined) {
      const dx = p.x - c.x, dz = p.z - c.z, d = Math.hypot(dx, dz), m = c.r + r;
      if (d < m && d > 1e-6) { p.x = c.x + dx / d * m; p.z = c.z + dz / d * m; }
    } else {
      const cx = Math.max(c.minx, Math.min(p.x, c.maxx)), cz = Math.max(c.minz, Math.min(p.z, c.maxz));
      const dx = p.x - cx, dz = p.z - cz, d = Math.hypot(dx, dz);
      if (d < r) {
        if (d > 1e-6) { p.x = cx + dx / d * r; p.z = cz + dz / d * r; }
        else { // inside: push out along smallest axis
          const o = [p.x - c.minx, c.maxx - p.x, p.z - c.minz, c.maxz - p.z]; const k = o.indexOf(Math.min(...o));
          if (k === 0) p.x = c.minx - r; else if (k === 1) p.x = c.maxx + r; else if (k === 2) p.z = c.minz - r; else p.z = c.maxz + r;
        }
      }
    }
  }
  const b = T.bounds; p.x = Math.max(b.minx, Math.min(b.maxx, p.x)); p.z = Math.max(b.minz, Math.min(b.maxz, p.z));
}
// ---------- actions: attack, guard, dash ----------
const act = { guard: false };
const dash = { t: 9, cd: 0, from: new THREE.Vector3(), mist: 0 };
const DASH_DIST = 2.0, DASH_T = 0.1, DASH_ANIM = 0.32;
function doAttack() { if (mode === 'world' && player && dash.t >= DASH_T) startAttack(player); }
function doDash() {
  if (mode !== 'world' || !player || dash.cd > 0 || !(body.limbs.legL || body.limbs.legR)) return;
  // moving: where the stick points; standing: where the hero looks
  const d = act.dir ? new THREE.Vector3(act.dir[0], 0, act.dir[1]) : new THREE.Vector3(Math.sin(player.heading), 0, Math.cos(player.heading));
  dash.dir = d.normalize(); dash.t = 0; dash.from.copy(pos); dash.mist = 0;
  // the armour follows piece by piece, one after another, like a suit assembling itself; each piece tumbles on its way
  const order = char.pieces.slice().sort(() => Math.random() - 0.5), gap = Math.min(0.04, 0.4 / Math.max(1, order.length));
  order.forEach((pc, i) => { pc.delay = DASH_T * 0.6 + i * gap; pc.dur = 0.14 + Math.random() * 0.04;
    pc.axis.set(Math.random() - 0.5, Math.random() - 0.5, Math.random() - 0.5).normalize(); pc.turns = (Math.random() < 0.5 ? -1 : 1) * (1 + Math.floor(Math.random() * 2)); pc.side = (Math.random() - 0.5) * 0.7; });
  dash.anim = DASH_T * 0.6 + (order.length - 1) * gap + 0.18; dash.cd = dash.anim + 0.5;
  player.sim.h = player.heading = Math.atan2(d.x, d.z);
}
function updateDash(dt) {
  dash.cd = Math.max(0, dash.cd - dt);
  if (dash.t > (dash.anim || 0) + 0.05) return;
  if (dash.t < DASH_T) {            // the shadow itself crosses the distance in a tenth of a second
    const k = Math.min(dt, DASH_T - dash.t) / DASH_T, bx = pos.x, bz = pos.z;
    pos.x += dash.dir.x * DASH_DIST * k; pos.z += dash.dir.z * DASH_DIST * k; collide(pos, 0.27);
    player.sim.x += pos.x - bx; player.sim.z += pos.z - bz;
  }
  dash.t += dt;
  const back = dash.from.clone().sub(pos);
  const sideV = new THREE.Vector3(-dash.dir.z, 0, dash.dir.x);
  for (const pc of char.pieces) {
    const u = Math.max(0, Math.min(1, (dash.t - pc.delay) / pc.dur)), e = u * u * (3 - 2 * u), arc = Math.sin(Math.PI * e);
    const piv = pc.bone.localToWorld(pc.off.clone());
    for (const U of pc.shifts) {
      U.shift.value.copy(back).multiplyScalar(1 - e).addScaledVector(sideV, pc.side * arc); U.shift.value.y += 0.3 * arc;     // a low arc, a little to one side
      U.pivot.value.copy(piv); U.spin.value.set(pc.axis.x, pc.axis.y, pc.axis.z, pc.turns * Math.PI * 2 * (1 - e) * (u > 0 ? 1 : 0));
    }
  }
  // mist hangs along the path
  dash.mist -= dt; if (dash.mist <= 0 && dash.t < dash.anim) { dash.mist = 0.035; const k = Math.random();
    spawnPuff(new THREE.Vector3(pos.x + back.x * k, 0.35 + Math.random() * 1.1, pos.z + back.z * k), 0.07, new THREE.Vector3(0, 0.15, 0)); }
}
for (const [id, down, upF] of [['atk', doAttack, null], ['dash', doDash, null], ['def', () => { act.guard = true; }, () => { act.guard = false; }]]) {
  const el = $(id); if (!el) continue;
  el.addEventListener('pointerdown', e => { e.preventDefault(); el.setPointerCapture(e.pointerId); el.classList.add('on'); down(); });
  const up = () => { el.classList.remove('on'); if (upF) upF(); };
  el.addEventListener('pointerup', up); el.addEventListener('pointercancel', up);
}
addEventListener('keydown', e => { if (e.repeat) return; if (e.code === 'KeyJ') doAttack(); if (e.code === 'Space') doDash(); });
function worldUpdate(dt, t) {
  let ix = stick.x, iy = stick.y;
  if (keys.KeyW || keys.ArrowUp) iy = -1; if (keys.KeyS || keys.ArrowDown) iy = 1;
  if (keys.KeyA || keys.ArrowLeft) ix = -1; if (keys.KeyD || keys.ArrowRight) ix = 1;
  let mag = Math.min(1, Math.hypot(ix, iy)); if (mag < 0.12) mag = 0;
  if (mag > 0.85) mag = 1 + Math.max(stick.run, keys.ShiftLeft || keys.ShiftRight ? 1 : 0); else if (stick.run > 0) mag = 1 + stick.run;
  let dir = null;
  if (mag > 0) {
    const ux = ix / Math.hypot(ix, iy), uy = iy / Math.hypot(ix, iy);
    const cs = Math.cos(ISO.yaw), sn = Math.sin(ISO.yaw);
    dir = [ux * cs + uy * sn, -ux * sn + uy * cs];
  }
  // stick: light push = turn on the spot, more = walk -> jog -> sprint (blend tree)
  const lockPos = lockOn ? (lockFixed || (loc === tavern ? bartender.char.group.position : loc.spawn)) : null;
  act.dir = dir; player.guardHeld = act.guard || !!keys.KeyK;
  const [ddx, ddz] = locomotion(player, dt, dir, mag, lockPos, pos, p => collide(p, 0.27));
  pos.x += ddx; pos.z += ddz;
  updateDash(dt);
  if (player.trail && player.trail.length) { for (const q of player.trail) spawnPuff(q, 0.03, null, 0.3); player.trail.length = 0; }
  collide(pos, 0.27);
  heading = player.heading;
  char.group.position.set(pos.x, 0, pos.z);
  char.group.rotation.y = heading;
  loc.update(t); pickups(); updateDebris(dt);
  if (player) GLYPH.uVel.value.set(player.sim.vx, 0, player.sim.vz);
  GLYPH.uHeroA.value.set(pos.x, 0, pos.z); GLYPH.uHeroB.value.lerp(GLYPH.uHeroA.value, Math.min(1, dt * 2.2));
  if (moonLight) { moonLight.position.set(pos.x + 1, 7.5, pos.z - 7.5); moonLight.target.position.set(pos.x, 0, pos.z); }
  labelT -= dt; if (labelT <= 0) { labelT = 0.25; $('badge').innerHTML = `<b>${sexName[cfg.sex]}</b> · ${cfg.height} см\nСтик — движение\nЦелостность: ${body.hp}% · ${player.label}`; }
  if (loc === tavern) { locomotion(bartender, dt, null, 0, null, { x: bartender.char.group.position.x, z: bartender.char.group.position.z }); bartender.heading = 0; bartender.noLock = true; animateActor(bartender, t, dt); }
  // follow camera
  { const d = ISO_OFF.clone().setY(0).normalize(); heroLight.position.set(pos.x + d.x * 1.3 - d.z * 0.5, 2.3, pos.z + d.z * 1.3 + d.x * 0.5); }
  ISO.yaw += (ISO.yawT - ISO.yaw) * Math.min(1, dt * 12); ISO.pitch += (ISO.pitchT - ISO.pitch) * Math.min(1, dt * 12); updIso();
  loc.cutaway(ISO_OFF);
  camTgt.lerp(new THREE.Vector3(pos.x, 0.6, pos.z), Math.min(1, dt * 5));
  const want = camTgt.clone().add(ISO_OFF);
  camPos.copy(want);
  camPos.lerp(want, Math.min(1, dt * 4));
  camera.position.copy(camPos); camera.lookAt(camTgt);
}

// ---------- loop ----------
let lastT = performance.now(), fpsAcc = 0, fpsN = 0;
function frame(now) {
  const dt = Math.min(0.1, (now - lastT) / 1000) * (window.__ts === undefined ? 1 : window.__ts); lastT = now;
  if (rebuildPending) { rebuildPending = false; rebuild(); }
  if (!interacting && mode === 'editor') {
    if (Math.abs(vel) > 1e-4) { cam.yaw += vel; vel *= Math.pow(0.04, dt); }
    else if (autoRotate && now - lastInteract > 1500) cam.yaw += dt * 0.4;
  }
  const t = now / 1000;
  if (mode === 'world') { worldUpdate(dt, t); animateActor(player, t, dt); }
  else {
  if (player) { player.noLock = true; locomotion(player, dt, null, 0, null, { x: 0, z: 0 }); player.heading = 0; player.sim.x = player.sim.z = 0; player.sim.h = 0; }
  animateActor(player, t, dt);
  const cp = Math.cos(cam.pitch), sp = Math.sin(cam.pitch);
  camera.position.set(Math.sin(cam.yaw) * cp * cam.dist, cam.ty + sp * cam.dist, Math.cos(cam.yaw) * cp * cam.dist);
  camera.lookAt(0, cam.ty, 0);
  const yaw = cam.yaw;
  const rot = (x, y, z) => new THREE.Vector3(x * Math.cos(yaw) + z * Math.sin(yaw), y, -x * Math.sin(yaw) + z * Math.cos(yaw));
  key.position.copy(rot(2.2, 4.0, 2.6)); key.target.position.set(0, 0.9, 0);
  rim.position.copy(rot(-2.5, 2.2, -3.0));
  fill.position.copy(rot(-2.5, 0.8, 2.0));
  }
  AR.depthRef = mode === 'world' ? ISO.dist : cam.dist;
  if (window.__plain) { AR.renderer.setRenderTarget(null); AR.renderer.render(scene, camera); } else AR.render(scene, camera, t);
  fpsAcc += dt; fpsN++;
  if (fpsAcc > 1) { $('fps').textContent = Math.round(fpsN / fpsAcc) + ' fps · ' + (GLYPH.uMode.value > 0.5 ? GLYPH_STATS.count + ' симв.' : AR.cols + '×' + AR.rows); fpsAcc = 0; fpsN = 0; }
  requestAnimationFrame(frame);
}
window.__dbg = { damage, body, doAttack, doDash, act, dash, ISO, debris, puffs, get loc() { return loc; }, cam, setCfg, enterWorld, leaveWorld, stick, get pos() { return pos; }, get player() { return player; }, get auto() { return autoRotate; }, set auto(v) { autoRotate = v; }, AR };
(async () => {
  try {
    await loadHuman(); await loadMocap();
    rebuild(); resize(); $('loading').style.display = 'none';
    requestAnimationFrame(frame);
  } catch (e) { $('loading').textContent = 'Ошибка загрузки: ' + e.message; console.error(e); }
})();
