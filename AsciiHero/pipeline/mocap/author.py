"""Hand-built poses on our skeleton (from its rest), for what no capture covers: sitting cross-legged (man),
sitting with the legs folded to one side and a hand on the floor (woman). Output like retarget.process: Q (local
quaternions of OUT_BONES) and RO (pelvis offset, y relative to standing)."""
import numpy as np
from scipy.spatial.transform import Rotation as Rot
import retarget as R
H = R.H; BI = R.BI
def nz(v): v = np.asarray(v, float); return v / np.linalg.norm(v)
def rotx(a): return Rot.from_euler('x', a).as_matrix()
def roty(a): return Rot.from_euler('y', a).as_matrix()
def rotz(a): return Rot.from_euler('z', a).as_matrix()
def L(n1, n2): return np.linalg.norm(H(n2) - H(n1))
def two_bone(T, a, b, c, target, pole):
    """aim bone a and b so that the end c reaches target; the middle joint bends towards pole"""
    Pa = T.W(BI[a])[1]; l1 = np.linalg.norm(H(b) - H(a)); l2 = np.linalg.norm(H(c) - H(b))
    d = target - Pa; D = np.clip(np.linalg.norm(d), abs(l1 - l2) + 1e-3, (l1 + l2) * 0.999); u = d / np.linalg.norm(d)
    p = pole - u * np.dot(pole, u); p = nz(p)
    ca = (l1 * l1 + D * D - l2 * l2) / (2 * l1 * D); sa = np.sqrt(max(0, 1 - ca * ca))
    mid = Pa + u * ca * l1 + p * sa * l1
    R.aim(T, a, H(b) - H(a), mid - Pa)
    Pb = T.W(BI[b])[1]; R.aim(T, b, H(c) - H(b), Pa + u * D - Pb)
    return mid
def leg(T, s, kneeDir, kneeY, ankleT, toeDir, g):
    """thigh towards the knee (direction, height above the ground fixed), shin towards the ankle target"""
    hip = T.W(BI['upperleg01.' + s])[1]; l1 = L('upperleg01.' + s, 'lowerleg01.' + s); l2 = L('lowerleg01.' + s, 'foot.' + s)
    d = nz(kneeDir); dy = (g + kneeY - hip[1]) / l1; h = np.sqrt(max(0, 1 - dy * dy)); hd = nz([d[0], 0, d[2]])
    knee = hip + (hd * h + np.array([0, dy, 0])) * l1
    R.aim(T, 'upperleg01.' + s, H('lowerleg01.' + s) - H('upperleg01.' + s), knee - hip)
    kp = T.W(BI['lowerleg01.' + s])[1]; a = np.array(ankleT, float); a[1] = g + ankleT[1]
    ad = nz(a - kp); R.aim(T, 'lowerleg01.' + s, H('foot.' + s) - H('lowerleg01.' + s), ad)
    R.aim(T, 'foot.' + s, H('toe3-1.' + s) - H('foot.' + s), nz(toeDir))
    return kp, kp + ad * l2
def pose(kind):
    T = R.Target(); T.reset()
    hipY = 0.135                                     # hip joints above the ground when sitting
    drop = H('upperleg01.L')[1] - hipY               # how far the body goes down
    g = drop                                         # the ground, in the skeleton's standing frame
    if kind == 'lotus':
        R.full(T, 'root', rotx(-0.12))               # pelvis rolled back a little (sitting on the sit bones)
        R.full(T, 'spine05', rotx(0.10)); R.full(T, 'spine03', rotx(0.05)); R.full(T, 'spine01', rotx(0.02))
        R.full(T, 'neck01', rotx(0.0)); R.full(T, 'head', rotx(0.06))
        kn = {}
        for s, sg, top in (('L', 1, 0), ('R', -1, 1)):
            hip = T.W(BI['upperleg01.' + s])[1]
            # thigh out to the side and forward, knee low; shin across in front, the ankle under the other knee
            kn[s], an = leg(T, s, [sg * 0.80, 0, 0.60], 0.10 + 0.03 * top, [-sg * 0.16, 0.07 + 0.05 * top, hip[2] + 0.22 + 0.06 * top],
                            [-sg * 0.8, -0.25, 0.35], g)
        for s, sg in (('L', 1), ('R', -1)):
            # hands rest on the knees, elbows a little out
            two_bone(T, 'upperarm01.' + s, 'lowerarm01.' + s, 'wrist.' + s, kn[s] + [-sg * 0.05, 0.09, 0.0], nz([sg * 0.7, 0, -0.7]))
    else:   # side sit: legs folded to the left, the right hand on the floor
        R.full(T, 'root', roty(0.25) @ rotz(-0.10) @ rotx(-0.08))
        R.full(T, 'spine05', rotz(-0.05)); R.full(T, 'spine03', roty(-0.12) @ rotz(-0.06)); R.full(T, 'spine01', roty(-0.08))
        R.full(T, 'neck01', rotz(0.08)); R.full(T, 'head', roty(-0.15) @ rotz(0.10))
        # left leg: thigh forward-left on the ground, shin folded back alongside, the foot behind the hip
        hl = T.W(BI['upperleg01.L'])[1]
        knL, anL = leg(T, 'L', [0.55, 0, 0.83], 0.09, [hl[0] + 0.42, 0.06, hl[2] - 0.12], [0.25, -0.3, -0.92], g)
        # right leg: thigh forward, shin folded back towards the left, the foot tucked in front of the left knee
        hr = T.W(BI['upperleg01.R'])[1]
        knR, anR = leg(T, 'R', [0.18, 0, 0.98], 0.09, [hr[0] + 0.40, 0.07, hr[2] + 0.08], [0.75, -0.3, -0.55], g)
        # right hand flat on the floor beside the hip, arm nearly straight
        hipR = T.W(BI['upperleg01.R'])[1]
        two_bone(T, 'upperarm01.R', 'lowerarm01.R', 'wrist.R', np.array([hipR[0] - 0.30, g + 0.06, hipR[2] - 0.08]), nz([0.0, 0.2, -1]))
        R.full(T, 'wrist.R', T.W(BI['lowerarm01.R'])[0] @ rotx(0.0))
        # left hand resting on the left thigh
        hp = T.W(BI['upperleg01.L'])[1]
        two_bone(T, 'upperarm01.L', 'lowerarm01.L', 'wrist.L', hp + (knL - hp) * 0.75 + [0, 0.10, 0], nz([0.7, 0, -0.7]))
    Q = np.zeros((len(R.OUT_BONES), 4))
    for k, bn in enumerate(R.OUT_BONES):
        q = Rot.from_matrix(T.local[BI[bn]]).as_quat(); Q[k] = q if q[3] >= 0 else -q
    RO = np.array([0, -drop, 0])
    return Q, RO
if __name__ == '__main__':
    import viz, sys
    from scipy.spatial.transform import Rotation as Rt
    vs = []
    for kind, sex in (('lotus', 'm'), ('side', 'f')):
        Q, RO = pose(kind); V, Hh, _ = viz.variant(sex, 1)
        loc = {b: Rt.from_quat(Q[k]).as_matrix() for k, b in enumerate(R.OUT_BONES)}
        Rw, P = viz.world(loc, Hh); Vp = viz.skin(V, Hh, Rw, P) + RO
        for y in (0, 45, 90): vs.append((Vp, P, y, f'{kind} {y}'))
    viz.render(vs, '/tmp/sitpose.png')
