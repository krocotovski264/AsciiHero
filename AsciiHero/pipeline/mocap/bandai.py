#!/usr/bin/env python3
"""Retarget Bandai Namco Research Motiondataset clips (BVH, CC BY-NC 4.0) onto the MakeHuman skeleton, in the same
format as retarget.process(). The clips are in place (hips fixed), facing +Z, +X = left."""
import numpy as np
from scipy.spatial.transform import Rotation as Rot, Slerp
from scipy.ndimage import gaussian_filter1d
import retarget as R
D = '/home/claude/bandai/repo/dataset/Bandai-Namco-Research-Motiondataset-1/data/'
H = R.H; BI = R.BI

def slerpM(A, B, t):
    return Slerp([0, 1], Rot.from_matrix(np.stack([A, B])))(t).as_matrix()

def process(fname, a, b):
    J, data, ft = R.parse_bvh(D + fname)
    R.fk_bvh(J, data[160:161]); g = np.array([0, 0.4, 0.92]); AL = R.fk_bvh.R['Hand_R'][0].T @ (g / np.linalg.norm(g))      # guard: the point at throat height ahead
    P, E = R.fk_bvh(J, data[a:b]); RW = {k: v.copy() for k, v in R.fk_bvh.R.items()}
    nf = b - a
    Ls = np.linalg.norm(P['LowerLeg_L'] - P['UpperLeg_L'], axis=1).mean() + np.linalg.norm(P['Foot_L'] - P['LowerLeg_L'], axis=1).mean()
    Lt = np.linalg.norm(H('lowerleg01.L') - H('upperleg01.L')) + np.linalg.norm(H('foot.L') - H('lowerleg01.L'))
    sc = Lt / Ls
    P = {k: v * sc for k, v in P.items()}; E = {k: v * sc for k, v in E.items()}
    hip0 = P['Hips'][0].copy()
    T = R.Target(); Q = np.zeros((nf, len(R.OUT_BONES), 4)); RO = np.zeros((nf, 3)); LOC = np.zeros((nf, 3, 3)); BL = np.zeros((nf, 3))
    restLeg = H('root')[1] - H('foot.L')[1]
    low = np.minimum(P['Foot_L'][:, 1], P['Foot_R'][:, 1]); low = gaussian_filter1d(low, 1.5, mode='nearest')
    for f in range(nf):
        L = {k: v[f] - [hip0[0], 0, hip0[2]] for k, v in P.items()}
        T.reset()
        pel = R.frame(L['UpperLeg_L'] - L['UpperLeg_R'], L['Chest'] - L['Hips']) @ R.REST['pelvis'].T
        che = R.frame(L['UpperArm_L'] - L['UpperArm_R'], L['Neck'] - L['Spine']) @ R.REST['chest'].T
        hw = RW['Head'][f]; hed = np.stack([hw[:, 2], hw[:, 0], hw[:, 1]], axis=1)       # head joint: x = up, y = forward, z = left
        R.full(T, 'root', pel)
        R.full(T, 'spine05', slerpM(pel, che, 0.4)); R.full(T, 'spine03', slerpM(pel, che, 0.8)); R.full(T, 'spine01', che)
        R.full(T, 'neck01', slerpM(che, hed, 0.5)); R.full(T, 'head', hed)
        for s in 'LR':
            R.aim(T, 'clavicle.' + s, H('upperarm01.' + s) - H('clavicle.' + s), L['UpperArm_' + s] - L['Shoulder_' + s])
            R.aim(T, 'upperarm01.' + s, H('lowerarm01.' + s) - H('upperarm01.' + s), L['LowerArm_' + s] - L['UpperArm_' + s])
            R.aim(T, 'lowerarm01.' + s, H('wrist.' + s) - H('lowerarm01.' + s), L['Hand_' + s] - L['LowerArm_' + s])
            R.aim(T, 'upperleg01.' + s, H('lowerleg01.' + s) - H('upperleg01.' + s), L['LowerLeg_' + s] - L['UpperLeg_' + s])
            R.aim(T, 'lowerleg01.' + s, H('foot.' + s) - H('lowerleg01.' + s), L['Foot_' + s] - L['LowerLeg_' + s])
            R.aim(T, 'foot.' + s, H('toe3-1.' + s) - H('foot.' + s), L['Toes_' + s] - L['Foot_' + s])
        for k, bn in enumerate(R.OUT_BONES):
            q = Rot.from_matrix(T.local[BI[bn]]).as_quat(); Q[f, k] = q if q[3] >= 0 else -q
        LOC[f] = [L['Foot_L'], L['Foot_R'], L['Hips']]
        # pelvis height: the data keeps the hips fixed and lets the feet rise instead when the body sinks into the lunge
        RO[f] = [0, (L['Hips'][1] - low[f]) - restLeg, 0]
        # the blade is rigid in the right hand: its direction in the hand's frame is fixed by the guard pose the clip starts in
        BL[f] = RW['Hand_R'][f] @ AL
    for k in range(len(R.OUT_BONES)):
        for f in range(1, nf):
            if np.dot(Q[f, k], Q[f - 1, k]) < 0: Q[f, k] = -Q[f, k]
    BL = gaussian_filter1d(BL, 1.2, axis=0, mode='nearest'); BL /= np.linalg.norm(BL, axis=1)[:, None]
    z = np.zeros(nf)
    hand = (P['Hand_R'] - hip0 * [1, 0, 1])
    return dict(name=fname, mirror=False, LOC=LOC, dt=ft, n=nf, Q=Q, RO=RO, fwd=z, lat=z, yawr=z, yaw=z, con=np.ones((nf, 2)), traj=np.zeros((nf, 2)), blade=BL, hand=hand)

if __name__ == '__main__':
    for fn, a, b in (('dataset-1_slash_normal_001.bvh', 160, 232), ('dataset-1_slash_normal_002.bvh', 160, 240)):
        c = process(fn, a, b); v = np.linalg.norm(np.gradient(c['hand'], c['dt'], axis=0), axis=1)
        print(fn, c['n'], 'RO y min %.3f max %.3f' % (c['RO'][:, 1].min(), c['RO'][:, 1].max()))
        print(' hand speed:', ' '.join('%d:%.1f' % (i, v[i]) for i in range(0, c['n'], 3)))
        print(' hand y:', ' '.join('%d:%.2f' % (i, c['hand'][i, 1]) for i in range(0, c['n'], 6)))
        print(' blade(left,up,fwd):', ' | '.join('%d: %.2f %.2f %.2f' % (i, *c['blade'][i]) for i in range(0, c['n'], 8)))
