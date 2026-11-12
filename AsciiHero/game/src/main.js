import * as THREE from 'three';
import { loadHuman, MAT } from './human.js';
import { buildCharacter, ARMORS, WEAPONS, BUILDS } from './character.js';
import { AsciiRenderer, makeMaterials, buildGlyphLayer, GLYPH, GLYPH_STATS } from './ascii.js';
import { buildTavern } from './tavern.js';
import { applyPose } from './pose.js';
import { makeActor, animateActor, locomotion, loadMocap } from './anim.js';

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
$('done').onclick = () => { try { localStorage.setItem('ascii_hero3d', JSON.stringify(cfg)); } catch (e) {} enterWorld(); };
$('back').onclick = () => leaveWorld();
// where the characters live: on the models (surface glyphs) or on a screen grid (beta 0.8)
try { const gm = localStorage.getItem('ascii_hero_gmode'); if (gm !== null) GLYPH.uMode.value = +gm; } catch (e) {}
function syncGMode() { const on = GLYPH.uMode.value > 0.5; $('gmode').classList.toggle('on', on); $('gmode').textContent = on ? 'Aa на моделях' : 'Aa сеткой экрана'; }
$('gmode').onclick = () => { GLYPH.uMode.value = GLYPH.uMode.value > 0.5 ? 0 : 1; try { localStorage.setItem('ascii_hero_gmode', GLYPH.uMode.value); } catch (e) {} syncGMode(); resize(); };
syncGMode();
$('lock').onclick = () => {
  lockOn = !lockOn; $('lock').classList.toggle('on', lockOn);
  lockFixed = null;
  if (lockOn && bartender && bartender.char.group.position.distanceTo(new THREE.Vector3(pos.x, 0, pos.z)) > 10) {
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
  char = buildCharacter(cfg, actorMats);
  buildGlyphLayer(char.group);
  scene.add(char.group);
  char.cfgWeapon = cfg.weapon;
  player = makeActor(char);
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
    ISO.yawT -= dx * 5.0 / Math.max(W, 320);
    ISO.pitchT = Math.max(0.55, Math.min(1.35, ISO.pitchT + dy * 2.5 / Math.max(H, 240)));
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
let mode = 'editor', tavern = null, bartender = null;
const worldLights = new THREE.Group();
const pos = new THREE.Vector3(), camPos = new THREE.Vector3(), camTgt = new THREE.Vector3();
let heading = 0, heroLight = null, lockOn = false, lockFixed = null, labelT = 0;
// Diablo-like isometric camera: fixed diagonal yaw, steep pitch, narrow FOV
const ISO = { yaw: Math.PI / 4, pitch: 0.86, dist: 7.6, fov: 30, yawT: Math.PI / 4, pitchT: 0.86 };
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
  worldLights.add(moon); worldLights.add(moon.target);
}
function placePlayerInWorld() {
  if (!char) return;
  char.group.position.copy(pos); char.group.rotation.y = heading;
}
function enterWorld() {
  if (!tavern) {
    tavern = buildTavern(mats); setupWorldLights(); buildGlyphLayer(tavern.group);
    const bt = buildCharacter({ sex: 'm', build: 3, height: 178, armor: 'cloth', weapon: 'none' }, actorMats);
    bt.cfgWeapon = 'none';
    const s = 1.78 / bt.height; bt.group.scale.set(1 + (s - 1) * 0.45, s, 1 + (s - 1) * 0.45);
    bt.group.position.copy(tavern.bartender); buildGlyphLayer(bt.group);
    bartender = makeActor(bt);
    tavern.colliders.push({ x: tavern.bartender.x, z: tavern.bartender.z, r: 0.3 });
  }
  mode = 'world'; document.body.classList.add('world');
  scene.add(tavern.group); scene.add(worldLights); scene.add(bartender.char.group);
  floor.visible = false; editorLights.forEach(l => l.visible = false);
  pos.copy(tavern.spawn); heading = Math.PI; player.heading = Math.PI; player.sim.init = false; player.noLock = false; placePlayerInWorld();
  camTgt.set(pos.x, 0.6, pos.z); camPos.copy(camTgt).add(ISO_OFF);
  camera.fov = ISO_FOV; camera.updateProjectionMatrix(); AR.asciiMat.uniforms.exposure.value = 1.3; AR.asciiMat.uniforms.actorBoost.value = 1;
  $('title').innerHTML = '🍺 «ПЬЯНЫЙ ДРАКОН»<small>трактир</small>';
  $('badge').innerHTML = `<b>${sexName[cfg.sex]}</b> · ${cfg.height} см\nСтик — движение · свайп — камера`;
  $('hint').style.opacity = 0;
  resize();
}
function leaveWorld() {
  mode = 'editor'; document.body.classList.remove('world');
  scene.remove(tavern.group); scene.remove(worldLights); scene.remove(bartender.char.group);
  floor.visible = true; editorLights.forEach(l => l.visible = true);
  char.group.position.set(0, 0, 0); char.group.rotation.y = 0; player.speed = 0; player.heading = 0; player.sim.init = false; lockOn = false; $('lock').classList.remove('on');
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
  const T = tavern;
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
  const lockPos = lockOn ? (lockFixed || bartender.char.group.position) : null;
  const [ddx, ddz] = locomotion(player, dt, dir, mag, lockPos, pos, p => collide(p, 0.27));
  pos.x += ddx; pos.z += ddz;
  collide(pos, 0.27);
  heading = player.heading;
  char.group.position.set(pos.x, 0, pos.z);
  char.group.rotation.y = heading;
  tavern.update(t);
  labelT -= dt; if (labelT <= 0) { labelT = 0.25; $('badge').innerHTML = `<b>${sexName[cfg.sex]}</b> · ${cfg.height} см\nСтик — движение · свайп — камера\nАнимация: ${player.label}`; }
  locomotion(bartender, dt, null, 0, null, { x: bartender.char.group.position.x, z: bartender.char.group.position.z }); bartender.heading = 0; bartender.noLock = true;
  animateActor(bartender, t, dt);
  // follow camera
  { const d = ISO_OFF.clone().setY(0).normalize(); heroLight.position.set(pos.x + d.x * 1.3 - d.z * 0.5, 2.3, pos.z + d.z * 1.3 + d.x * 0.5); }
  ISO.yaw += (ISO.yawT - ISO.yaw) * Math.min(1, dt * 12); ISO.pitch += (ISO.pitchT - ISO.pitch) * Math.min(1, dt * 12); updIso();
  tavern.cutaway(ISO_OFF);
  camTgt.lerp(new THREE.Vector3(pos.x, 0.6, pos.z), Math.min(1, dt * 5));
  const want = camTgt.clone().add(ISO_OFF);
  camPos.copy(want);
  camPos.lerp(want, Math.min(1, dt * 4));
  camera.position.copy(camPos); camera.lookAt(camTgt);
}

// ---------- loop ----------
let lastT = performance.now(), fpsAcc = 0, fpsN = 0;
function frame(now) {
  const dt = Math.min(0.1, (now - lastT) / 1000); lastT = now;
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
window.__dbg = { cam, setCfg, enterWorld, leaveWorld, stick, get pos() { return pos; }, get player() { return player; }, get auto() { return autoRotate; }, set auto(v) { autoRotate = v; }, AR };
(async () => {
  try {
    await loadHuman(); await loadMocap();
    rebuild(); resize(); $('loading').style.display = 'none';
    requestAnimationFrame(frame);
  } catch (e) { $('loading').textContent = 'Ошибка загрузки: ' + e.message; console.error(e); }
})();
