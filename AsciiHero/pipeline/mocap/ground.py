"""Retarget clips of a 30 fps BVH set with a plain 22-joint skeleton (Hips, Spine..Spine2, Neck, Head, Left/Right
Shoulder, Arm, ForeArm, Hand, UpLeg, Leg, Foot, Toe) onto our skeleton, same output as retarget.process().
There is no T-pose frame in these files, so the trunk and head are set from geometric frames (hips, shoulders, neck)
and the limbs by direction; collarbones keep our own line; elbows are kept clear of the trunk."""
import numpy as np
from scipy.spatial.transform import Rotation as Rot, Slerp
from scipy.ndimage import gaussian_filter1d
import retarget as R
D = 'PATH/TO/GROUND/BVH/'      # folder with the 30 fps clips
H = R.H; BI = R.BI
def slerpM(A, B, t): return Slerp([0, 1], Rot.from_matrix(np.stack([A, B])))(t).as_matrix()
def process(fname, a=0, b=None):
    J, data, ft = R.parse_bvh(D + fname)
    b = len(data) if b is None else min(b, len(data))
    P, E = R.fk_bvh(J, data[a:b]); nf = b - a
    Ls = np.linalg.norm(P['LeftLeg'] - P['LeftUpLeg'], axis=1).mean() + np.linalg.norm(P['LeftFoot'] - P['LeftLeg'], axis=1).mean()
    Lt = np.linalg.norm(H('lowerleg01.L') - H('upperleg01.L')) + np.linalg.norm(H('foot.L') - H('lowerleg01.L'))
    sc = Lt / Ls; P = {k: v * sc for k, v in P.items()}
    floor = min(P['LeftToe'][:, 1].min(), P['RightToe'][:, 1].min(), P['LeftFoot'][:, 1].min(), P['RightFoot'][:, 1].min())
    # standing height of the hips: leg length + ankle height (the actor standing straight)
    standY = Ls * sc + (P['LeftFoot'][:, 1] - floor).min() + np.linalg.norm(P['LeftUpLeg'][0] - P['Hips'][0]) * 0.3
    for k in P: P[k] = P[k] - [0, floor, 0]
    hips = P['Hips']; dt = ft
    traj = gaussian_filter1d(hips[:, [0, 2]], sigma=max(1, 0.35 / dt), axis=0, mode='nearest')
    lat = P['LeftUpLeg'] - P['RightUpLeg'] + 0.6 * (P['LeftArm'] - P['RightArm'])
    lat = gaussian_filter1d(lat, sigma=max(1, 0.35 / dt), axis=0, mode='nearest')
    fwd = np.cross(lat, [0, 1, 0]); yaw = np.unwrap(np.arctan2(fwd[:, 0], fwd[:, 2]))
    def loc(v, f):
        c, s = np.cos(-yaw[f]), np.sin(-yaw[f]); x, y, z = v[0] - traj[f, 0], v[1], v[2] - traj[f, 1]
        return np.array([c * x + s * z, y, -s * x + c * z])
    T = R.Target(); Q = np.zeros((nf, len(R.OUT_BONES), 4)); RO = np.zeros((nf, 3)); LOC = np.zeros((nf, 3, 3))
    neckRest = H('head') - H('neck01')
    for f in range(nf):
        L = {k: loc(v[f], f) for k, v in P.items()}
        T.reset()
        pel = R.frame(L['LeftUpLeg'] - L['RightUpLeg'], L['Spine1'] - L['Hips']) @ R.REST['pelvis'].T
        che = R.frame(L['LeftArm'] - L['RightArm'], L['Neck'] - L['Spine1']) @ R.REST['chest'].T
        hed = R.rot_between(che @ neckRest, L['Head'] - L['Neck']) @ che
        R.full(T, 'root', pel)
        R.full(T, 'spine05', slerpM(pel, che, 0.4)); R.full(T, 'spine03', slerpM(pel, che, 0.8)); R.full(T, 'spine01', che)
        R.full(T, 'neck01', slerpM(che, hed, 0.5)); R.full(T, 'head', hed)
        for s, S in (('L', 'Left'), ('R', 'Right')):
            R.aim(T, 'upperarm01.' + s, H('lowerarm01.' + s) - H('upperarm01.' + s), L[S + 'ForeArm'] - L[S + 'Arm'])
            R.aim(T, 'lowerarm01.' + s, H('wrist.' + s) - H('lowerarm01.' + s), L[S + 'Hand'] - L[S + 'ForeArm'])
            R.aim(T, 'upperleg01.' + s, H('lowerleg01.' + s) - H('upperleg01.' + s), L[S + 'Leg'] - L[S + 'UpLeg'])
            R.aim(T, 'lowerleg01.' + s, H('foot.' + s) - H('lowerleg01.' + s), L[S + 'Foot'] - L[S + 'Leg'])
            R.aim(T, 'foot.' + s, H('toe3-1.' + s) - H('foot.' + s), L[S + 'Toe'] - L[S + 'Foot'])
        R.clear_elbows(T)
        for k, bn in enumerate(R.OUT_BONES):
            q = Rot.from_matrix(T.local[BI[bn]]).as_quat(); Q[f, k] = q if q[3] >= 0 else -q
        LOC[f] = [L['LeftFoot'], L['RightFoot'], L['Hips']]
        RO[f] = [L['Hips'][0], L['Hips'][1], L['Hips'][2]]
    for k in range(len(R.OUT_BONES)):
        for f in range(1, nf):
            if np.dot(Q[f, k], Q[f - 1, k]) < 0: Q[f, k] = -Q[f, k]
    hipY = 0.0
    # RO y: hips height relative to standing (same meaning as retarget's RO + hipY - standY)
    RO[:, 1] = RO[:, 1] - standY
    z = np.zeros(nf)
    return dict(name=fname, mirror=False, LOC=LOC, dt=dt, n=nf, Q=Q, RO=RO, fwd=z, lat=z, yawr=z, yaw=yaw, con=np.ones((nf, 2)),
                traj=traj, hipY=0.0, standY=0.0, P=P)
