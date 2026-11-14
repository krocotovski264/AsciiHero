"""Attack clips from CMU mocap (free to use): retargeted with retarget.process, cut to one strike, with the direction of
the weapon per frame in the body frame (x = left, y = up, z = forward)."""
import numpy as np, retarget as R
from scipy.ndimage import gaussian_filter1d
STEP = 4
def clip(fn, a, b, peak, mode):
    c = R.process(fn, step=STEP)
    J, data, ft = R.parse_bvh(R.CMU + '/' + fn); P, E = R.fk_bvh(J, data[1:]); RW = {k: v[::STEP] for k, v in R.fk_bvh.R.items()}
    P = {k: v[::STEP] for k, v in P.items()}
    a4, b4, p4 = a // STEP, b // STEP, peak // STEP
    yaw = c['yaw']
    def loc(v, f):     # world direction -> body frame of frame f (x = left, z = forward)
        cy, sy = np.cos(-yaw[f]), np.sin(-yaw[f]); return np.array([cy * v[0] + sy * v[2], v[1], -sy * v[0] + cy * v[2]])
    n = c['n']; BL = np.zeros((n, 3)); BLL = np.zeros((n, 3)); BLL[:] = [0, -0.985, 0.174]
    if mode == 'hands':
        # a tool held in both hands: at the strike it carries on the line from the chest through the hands; it is rigid
        # in the right hand, so the hand's turning (the cocked wrists of the wind-up) carries it everywhere else
        mid = (P['RightHand'][p4] + P['LeftHand'][p4]) / 2; tool = mid - P['Spine1'][p4]; tool /= np.linalg.norm(tool)
        AL = RW['RightHand'][p4].T @ tool
        for f in range(n): BL[f] = loc(RW['RightHand'][f] @ AL, f)
    elif mode == 'thrust':
        # a thrust: the shaft lies along the striking arm, pointing ahead
        for f in range(n):
            d = loc(P['RightHand'][f] - P['RightArm'][f], f); d /= np.linalg.norm(d) + 1e-9
            d = 0.45 * d + np.array([0, 0.08, 1.0]); BL[f] = d / np.linalg.norm(d)     # mostly level and ahead, tipped by the arm
    elif mode == 'oneR':
        # a short axe in each hand: the right one carries on the line of the forearm at the strike and is rigid in the hand;
        # the left one hangs low and forward at the start and is rigid in the left hand
        tool = P['RightHand'][p4] - P['RightForeArm'][p4]; tool /= np.linalg.norm(tool)
        AL = RW['RightHand'][p4].T @ tool
        for f in range(n): BL[f] = loc(RW['RightHand'][f] @ AL, f)
        cy, sy = np.cos(yaw[a4]), np.sin(yaw[a4]); d = np.array([0, -0.985, 0.174]); dW = np.array([cy * d[0] + sy * d[2], d[1], -sy * d[0] + cy * d[2]])
        ALL = RW['LeftHand'][a4].T @ dW
        for f in range(n): BLL[f] = loc(RW['LeftHand'][f] @ ALL, f)
    else:
        BL[:] = [0.2, 0.3, 0.93]
    BL = gaussian_filter1d(BL, 1.0, axis=0, mode='nearest'); BL /= np.linalg.norm(BL, axis=1)[:, None]
    BLL = gaussian_filter1d(BLL, 1.0, axis=0, mode='nearest'); BLL /= np.linalg.norm(BLL, axis=1)[:, None]
    sl = slice(a4, b4)
    RO = c['RO'][sl].copy(); RO[:, 0] -= RO[0, 0]; RO[:, 2] -= RO[0, 2]
    return dict(Q=c['Q'][sl], RO=RO, con=c['con'][sl], blade=BL[sl], bladeL=BLL[sl], n=b4 - a4, peak=p4 - a4)
# name, file, window start / end and strike frame (120 fps), weapon direction
SPEC = [
    ('spearThrust', '002/02_05.bvh', 1268, 1436, 1364, 'thrust'),
    ('spearHigh', '002/02_05.bvh', 398, 566, 494, 'thrust'),
    ('spearLunge', '144/144_17.bvh', 2137, 2305, 2209, 'thrust'),
    ('spearSwing', '124/124_07.bvh', 922, 1090, 1018, 'hands'),
    ('spearOverhead', '079/79_01.bvh', 29, 197, 125, 'hands'),
    ('tomaChop', '086/86_04.bvh', 5069, 5249, 5177, 'oneR'),
    ('tomaChop2', '086/86_04.bvh', 5622, 5802, 5730, 'oneR'),
    ('tomaBack', '141/141_28.bvh', 30, 150, 94, 'oneR'),
    ('shieldStraight', '143/143_23.bvh', 150, 278, 218, 'none'),
    ('shieldBackhand', '141/141_28.bvh', 30, 150, 94, 'none'),
]
