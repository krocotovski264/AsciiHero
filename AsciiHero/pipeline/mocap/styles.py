"""Styled locomotion clips given as world joint positions at 30 fps (see decode.py), retargeted onto the game
skeleton through ground.from_positions."""
import numpy as np, sys
import ground as lafan
MAP = {'Hips': 'Hips', 'Spine1': 'Spine2', 'Neck': 'Neck1', 'Head': 'Head', 'LeftArm': 'LeftArm', 'LeftForeArm': 'LeftForeArm', 'LeftHand': 'LeftHand',
       'RightArm': 'RightArm', 'RightForeArm': 'RightForeArm', 'RightHand': 'RightHand', 'LeftUpLeg': 'LeftLeg', 'LeftLeg': 'LeftShin', 'LeftFoot': 'LeftFoot',
       'LeftToe': 'LeftToeBase', 'RightUpLeg': 'RightLeg', 'RightLeg': 'RightShin', 'RightFoot': 'RightFoot', 'RightToe': 'RightToeBase'}
D = np.load('PATH/TO/STYLED/positions.npz'); J = list(D['J'])
def clip(name):
    X = D[name]; P = {k: X[:, J.index(v)].astype(float) for k, v in MAP.items()}
    return lafan.from_positions(P, 1 / 30.0, name)
if __name__ == '__main__':
    for k in ['FW', 'FR', 'BW', 'BR', 'SW', 'SR', 'ID', 'Rushed_FR']:
        c = clip(k); print(k, c['n'], 'fwd %.2f lat %.2f yawr %.2f' % (np.median(c['fwd']), np.median(c['lat']), np.median(np.abs(c['yawr']))), 'contacts L %.2f' % c['con'][:, 0].mean())
