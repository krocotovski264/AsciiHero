// Poses for the MakeHuman default skeleton. Bones have identity rest orientation.
// Poses are given as world-space aim directions; rotations are solved from them.
import * as THREE from 'three';

const _q = new THREE.Quaternion(), _q2 = new THREE.Quaternion(), _v1 = new THREE.Vector3(), _v2 = new THREE.Vector3();

function worldPos(b, out) { return b.getWorldPosition(out); }
// rotate bone so that the direction head -> (child joint) points along `dir` (world)
function aim(h, name, childName, dir) {
  const b = h.bone(name), c = h.bone(childName); if (!b || !c) return;
  h.root.updateMatrixWorld(true);
  const hp = worldPos(b, new THREE.Vector3()), cp = worldPos(c, new THREE.Vector3());
  const cur = cp.sub(hp).normalize();
  const delta = new THREE.Quaternion().setFromUnitVectors(cur, dir.clone().normalize());
  const bw = b.getWorldQuaternion(new THREE.Quaternion());
  const pw = b.parent.getWorldQuaternion(new THREE.Quaternion());
  b.quaternion.copy(pw.invert().multiply(delta).multiply(bw));
  b.updateMatrixWorld(true);
}
// rotate bone around a world axis
function rotWorld(h, name, axis, ang) {
  const b = h.bone(name); if (!b) return;
  b.updateMatrixWorld(true);
  const delta = new THREE.Quaternion().setFromAxisAngle(axis.clone().normalize(), ang);
  const bw = b.getWorldQuaternion(new THREE.Quaternion());
  const pw = b.parent.getWorldQuaternion(new THREE.Quaternion());
  b.quaternion.copy(pw.invert().multiply(delta).multiply(bw));
  b.updateMatrixWorld(true);
}
// current world-space hand frame
export function handFrameWorld(h, s) {
  h.root.updateMatrixWorld(true);
  const P = n => h.bone(n).getWorldPosition(new THREE.Vector3());
  const W = P('wrist.' + s), F2 = P('finger2-1.' + s), F3 = P('finger3-1.' + s), F5 = P('finger5-1.' + s);
  const f = F3.clone().sub(W).normalize();
  let a = F2.clone().sub(F5).normalize();
  const n = new THREE.Vector3().crossVectors(f, a).normalize();
  a.crossVectors(n, f).normalize();
  return { W, F3, f, a, n };
}
function curl(h, s, amount, thumb) {
  const sign = s === 'L' ? -1 : 1;
  for (let k = 2; k <= 5; k++) {
    for (let j = 1; j <= 3; j++) {
      const fr = handFrameWorld(h, s);
      rotWorld(h, `finger${k}-${j}.${s}`, fr.a, sign * amount * [0, 1.0, 1.3, 0.9][j]);
    }
  }
  if (thumb) {
    const fr = handFrameWorld(h, s);
    rotWorld(h, `finger1-2.${s}`, fr.f, -sign * thumb * 1.3);
    rotWorld(h, `finger1-3.${s}`, fr.a, sign * thumb * 0.7);
  }
}
const V = (x, y, z) => new THREE.Vector3(x, y, z).normalize();
export const POSES = {
  relaxed: { uaL: V(0.28, -1, 0.04), faL: V(0.16, -1, 0.32), uaR: V(-0.28, -1, 0.04), faR: V(-0.16, -1, 0.32), curlL: 0.35, curlR: 0.35, thumb: 0.2 },
  grip:    { uaL: V(0.28, -1, 0.04), faL: V(0.16, -1, 0.32), uaR: V(-0.3, -1, 0.1), faR: V(-0.12, -0.75, 0.75), curlL: 0.35, curlR: 1.15, thumbR: 0.7, thumb: 0.2 },
  shield:  { uaL: V(0.35, -1, 0.25), faL: V(-0.25, -0.15, 1), uaR: V(-0.3, -1, 0.1), faR: V(-0.12, -0.75, 0.75), curlL: 1.1, curlR: 1.15, thumb: 0.6, thumbR: 0.7 },
};
export function applyPose(h, name) {
  const P = POSES[name] || POSES.relaxed;
  for (const b of h.bones) b.quaternion.identity();
  h.root.updateMatrixWorld(true);
  aim(h, 'upperarm01.L', 'lowerarm01.L', P.uaL); aim(h, 'upperarm01.R', 'lowerarm01.R', P.uaR);
  aim(h, 'lowerarm01.L', 'wrist.L', P.faL);       aim(h, 'lowerarm01.R', 'wrist.R', P.faR);
  curl(h, 'L', P.curlL, P.thumbL ?? P.thumb);
  curl(h, 'R', P.curlR, P.thumbR ?? P.thumb);
  // natural stance: knees under the hips, ankles under the knees (MakeHuman rest pose is wide)
  aim(h, 'upperleg01.L', 'lowerleg01.L', V(-0.012, -1, 0.07)); aim(h, 'upperleg01.R', 'lowerleg01.R', V(0.012, -1, 0.07));
  aim(h, 'lowerleg01.L', 'foot.L', V(0.0, -1, -0.04));          aim(h, 'lowerleg01.R', 'foot.R', V(0.0, -1, -0.04));
  h.root.updateMatrixWorld(true);
}
