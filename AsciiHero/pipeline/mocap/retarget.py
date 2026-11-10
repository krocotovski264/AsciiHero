#!/usr/bin/env python3
"""Retarget CMU mocap (BVH, cgspeed conversion) onto the MakeHuman default skeleton.
Output: compact clips (local bone quaternions, root offset, root motion, foot contacts)."""
import numpy as np, json, re, base64, gzip, sys
from scipy.spatial.transform import Rotation as Rot
from scipy.ndimage import gaussian_filter1d

CMU = '/home/claude/mocap/cmu/data'
HM = json.load(open('/home/claude/game3d/build/human.json'))
BONES = HM['bones']; PAR = HM['parents']; BI = {b: i for i, b in enumerate(BONES)}
VAR = next(v for v in HM['variants'] if v['sex'] == 'm' and v['build'] == 1)
HEAD = np.array(VAR['heads']); TAIL = np.array(VAR['tails'])

# ---------------- BVH ----------------
def parse_bvh(path):
    txt = open(path).read().split()
    i = 0; joints = []; stack = []; chans_total = 0
    while txt[i] != 'MOTION':
        t = txt[i]
        if t in ('ROOT', 'JOINT'):
            name = txt[i + 1]; joints.append({'name': name, 'parent': stack[-1] if stack else -1, 'offset': None, 'ch': [], 'end': None}); i += 2
        elif t == 'End':
            joints[stack[-1]]['end_pending'] = True; i += 2; # 'End Site'
            # read { OFFSET x y z }
            assert txt[i] == '{'; assert txt[i + 1] == 'OFFSET'
            joints[stack[-1]]['end'] = np.array([float(x) for x in txt[i + 2:i + 5]]); i += 6
        elif t == '{':
            stack.append(len(joints) - 1); i += 1
        elif t == '}':
            stack.pop(); i += 1
        elif t == 'OFFSET':
            joints[stack[-1]]['offset'] = np.array([float(x) for x in txt[i + 1:i + 4]]); i += 4
        elif t == 'CHANNELS':
            n = int(txt[i + 1]); joints[stack[-1]]['ch'] = txt[i + 2:i + 2 + n]; joints[stack[-1]]['ch0'] = chans_total; chans_total += n; i += 2 + n
        else: i += 1
    i += 1
    nf = int(txt[i + 1]); i += 2
    ft = float(txt[i + 2]); i += 3
    data = np.array([float(x) for x in txt[i:i + nf * chans_total]]).reshape(nf, chans_total)
    return joints, data, ft

def fk_bvh(joints, data):
    nf = len(data); nj = len(joints)
    P = np.zeros((nf, nj, 3)); R = [None] * nj
    for j, J in enumerate(joints):
        ch = J['ch']; c0 = J['ch0']; vals = data[:, c0:c0 + len(ch)]
        rot_names = [c for c in ch if 'rotation' in c]; rot_idx = [ch.index(c) for c in rot_names]
        order = ''.join(c[0] for c in rot_names)
        local = Rot.from_euler(order.upper(), vals[:, rot_idx], degrees=True)
        if J['parent'] < 0:
            pos_idx = [ch.index(c) for c in ('Xposition', 'Yposition', 'Zposition')]
            P[:, j] = vals[:, pos_idx] + J['offset']; R[j] = local
        else:
            p = J['parent']
            P[:, j] = P[:, p] + R[p].apply(np.tile(J['offset'], (nf, 1)))
            R[j] = R[p] * local
    ends = {}
    for j, J in enumerate(joints):
        if J['end'] is not None: ends[J['name']] = P[:, j] + R[j].apply(np.tile(J['end'], (nf, 1)))
    names = [J['name'] for J in joints]
    fk_bvh.R = {n: R[k].as_matrix() for k, n in enumerate(names)}
    return {n: P[:, k] for k, n in enumerate(names)}, ends

# ---------------- target skeleton FK ----------------
class Target:
    def __init__(s):
        s.n = len(BONES); s.local = [np.eye(3) for _ in range(s.n)]
        s.memoR = {}; s.memoP = {}
    def reset(s):
        s.local = [np.eye(3) for _ in range(s.n)]; s.memoR = {}; s.memoP = {}
    def W(s, b):
        if b in s.memoR: return s.memoR[b], s.memoP[b]
        p = PAR[b]
        if p < 0: R = s.local[b]; P = HEAD[b].copy()
        else:
            Rp, Pp = s.W(p); R = Rp @ s.local[b]; P = Pp + Rp @ (HEAD[b] - HEAD[p])
        s.memoR[b] = R; s.memoP[b] = P; return R, P
    def set_local(s, b, M):
        s.local[b] = M
        # invalidate subtree memo
        kill = [b]
        while kill:
            k = kill.pop(); s.memoR.pop(k, None); s.memoP.pop(k, None)
            kill += [c for c in range(s.n) if PAR[c] == k]
    def parentR(s, b):
        return s.W(PAR[b])[0] if PAR[b] >= 0 else np.eye(3)

def rot_between(a, b):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b)
    v = np.cross(a, b); c = np.dot(a, b)
    if c < -0.9999:
        ax = np.cross(a, [1, 0, 0]);
        if np.linalg.norm(ax) < 1e-6: ax = np.cross(a, [0, 1, 0])
        return Rot.from_rotvec(ax / np.linalg.norm(ax) * np.pi).as_matrix()
    vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + vx + vx @ vx * (1 / (1 + c))

def frame(lat, up):
    x = lat / np.linalg.norm(lat); u = up - x * np.dot(up, x); u /= np.linalg.norm(u)
    f = np.cross(x, u); return np.stack([x, u, f], axis=1)

def aim(T, bname, offset, d):
    b = BI[bname]; Rp = T.parentR(b)
    d0 = Rp @ offset
    delta = rot_between(d0, d)
    T.set_local(b, Rp.T @ delta @ Rp)
def full(T, bname, Rw):
    b = BI[bname]; Rp = T.parentR(b); T.set_local(b, Rp.T @ Rw)

H = lambda n: HEAD[BI[n]]
REST = {
    'pelvis': frame(H('upperleg01.L') - H('upperleg01.R'), H('spine03') - H('root')),
    'chest': frame(H('upperarm01.L') - H('upperarm01.R'), H('neck01') - H('spine02')),
    'handL': frame(H('finger3-1.L') - H('wrist.L'), H('finger1-1.L') - H('wrist.L')),
    'handR': frame(H('finger3-1.R') - H('wrist.R'), H('finger1-1.R') - H('wrist.R')),
}
OUT_BONES = ['root', 'spine05', 'spine03', 'spine01', 'neck01', 'head', 'clavicle.L', 'clavicle.R',
             'upperarm01.L', 'upperarm01.R', 'lowerarm01.L', 'lowerarm01.R', 'wrist.L', 'wrist.R',
             'upperleg01.L', 'upperleg01.R', 'lowerleg01.L', 'lowerleg01.R', 'foot.L', 'foot.R']

# ---------------- per clip ----------------
MIRROR_SWAP = lambda n: n.replace('Left', '#').replace('Right', 'Left').replace('#', 'Right').replace('LThumb', '#T').replace('RThumb', 'LThumb').replace('#T', 'RThumb').replace('LHipJoint', '#H').replace('RHipJoint', 'LHipJoint').replace('#H', 'RHipJoint')
def process(fname, step=4, mirror=False):
    joints, data, ft = parse_bvh(f'{CMU}/{fname}')
    P0, E0 = fk_bvh(joints, data[:1]); RW0 = {k: v[0] for k, v in fk_bvh.R.items()}
    P, E = fk_bvh(joints, data[1:]); RW = {k: v[::step] for k, v in fk_bvh.R.items()}
    P = {k: v[::step] for k, v in P.items()}; E = {k: v[::step] for k, v in E.items()}
    nf = len(P['Hips']); dt = ft * step
    # scale: leg length
    Ls = np.linalg.norm(P['LeftLeg'] - P['LeftUpLeg'], axis=1).mean() + np.linalg.norm(P['LeftFoot'] - P['LeftLeg'], axis=1).mean()
    Lt = np.linalg.norm(H('lowerleg01.L') - H('upperleg01.L')) + np.linalg.norm(H('foot.L') - H('lowerleg01.L'))
    sc = Lt / Ls
    P = {k: v * sc for k, v in P.items()}; E = {k: v * sc for k, v in E.items()}
    P0 = {k: v[0] * sc for k, v in P0.items()}; E0 = {k: v[0] * sc for k, v in E0.items()}
    # rest (T-pose) in its own facing frame
    lat0 = P0['LeftUpLeg'] - P0['RightUpLeg']; f0 = np.cross(lat0, [0, 1, 0]); y0 = np.arctan2(f0[0], f0[2])
    def rest_local(v):
        c, s_ = np.cos(-y0), np.sin(-y0); x, y, z = v - P0['Hips']
        return np.array([c * x + s_ * z, y, -s_ * x + c * z])
    R0 = {k: rest_local(v) for k, v in P0.items()}; RE0 = {k: rest_local(v) for k, v in E0.items()}
    if mirror:
        R0 = {MIRROR_SWAP(k): v * [-1, 1, 1] for k, v in R0.items()}; RE0 = {MIRROR_SWAP(k): v * [-1, 1, 1] for k, v in RE0.items()}
    pelv0 = frame(R0['LeftUpLeg'] - R0['RightUpLeg'], R0['Spine1'] - R0['Hips'])
    chest0 = frame(R0['LeftArm'] - R0['RightArm'], R0['Neck1'] - R0['Spine1'])
    def drest(a, b, ends=False):
        return ((RE0 if ends else R0)[b] - R0[a])
    # floor: lowest toe over clip
    floor = min(P['LeftToeBase'][:, 1].min(), P['RightToeBase'][:, 1].min(), P['LeftFoot'][:, 1].min(), P['RightFoot'][:, 1].min())
    for k in P: P[k] = P[k] - [0, floor, 0]
    # trajectory: smoothed hips xz + smoothed facing
    hips = P['Hips']
    traj = gaussian_filter1d(hips[:, [0, 2]], sigma=max(1, 0.35 / dt), axis=0, mode='nearest')
    lat = P['LeftUpLeg'] - P['RightUpLeg'] + 0.6 * (P['LeftArm'] - P['RightArm'])
    lat = gaussian_filter1d(lat, sigma=max(1, 0.35 / dt), axis=0, mode='nearest')
    fwd = np.cross(lat, [0, 1, 0])  # forward = left x up
    yaw = np.unwrap(np.arctan2(fwd[:, 0], fwd[:, 2]))
    # local (facing) frame transform
    def to_local(v, f):
        c, s = np.cos(-yaw[f]), np.sin(-yaw[f])
        x, y, z = v[0] - traj[f, 0], v[1], v[2] - traj[f, 1]
        return np.array([c * x + s * z, y, -s * x + c * z])
    T = Target()
    Q = np.zeros((nf, len(OUT_BONES), 4)); RO = np.zeros((nf, 3)); LOC = np.zeros((nf, 3, 3))
    restRoot = HEAD[BI['root']]
    meanHipY = hips[:, 1].mean()
    for f in range(nf):
        L = {k: to_local(v[f], f) for k, v in P.items()}
        LE = {k: to_local(v[f], f) for k, v in E.items()}
        if mirror:
            L = {MIRROR_SWAP(k): v * [-1, 1, 1] for k, v in L.items()}; LE = {MIRROR_SWAP(k): v * [-1, 1, 1] for k, v in LE.items()}
        T.reset()
        # torso: transfer the real joint rotations of the mocap (relative to its T-pose) - no geometric bias
        cy, sy_ = np.cos(-yaw[f]), np.sin(-yaw[f]); Fy = np.array([[cy, 0, sy_], [0, 1, 0], [-sy_, 0, cy]])
        c0, s0 = np.cos(-y0), np.sin(-y0); Fy0 = np.array([[c0, 0, s0], [0, 1, 0], [-s0, 0, c0]])
        def jd(j):
            jj = MIRROR_SWAP(j) if mirror else j
            D = (Fy @ RW[jj][f]) @ (Fy0 @ RW0[jj]).T
            if mirror: S_ = np.diag([-1.0, 1, 1]); D = S_ @ D @ S_
            return D
        full(T, 'root', jd('Hips'))
        full(T, 'spine05', jd('LowerBack'))
        full(T, 'spine03', jd('Spine'))
        full(T, 'spine01', jd('Spine1'))
        full(T, 'neck01', jd('Neck1'))
        full(T, 'head', jd('Head'))
        for s, S in (('L', 'Left'), ('R', 'Right')):
            aim(T, 'clavicle.' + s, H('upperarm01.' + s) - H('clavicle.' + s), L[S + 'Arm'] - L[S + 'Shoulder'])
            aim(T, 'upperarm01.' + s, H('lowerarm01.' + s) - H('upperarm01.' + s), L[S + 'ForeArm'] - L[S + 'Arm'])
            aim(T, 'lowerarm01.' + s, H('wrist.' + s) - H('lowerarm01.' + s), L[S + 'Hand'] - L[S + 'ForeArm'])
            th = 'LThumb' if s == 'L' else 'RThumb'
            full(T, 'wrist.' + s, frame(LE[S + 'HandIndex1'] - L[S + 'Hand'], LE[th] - L[S + 'Hand']) @ REST['hand' + s].T)
            aim(T, 'upperleg01.' + s, H('lowerleg01.' + s) - H('upperleg01.' + s), L[S + 'Leg'] - L[S + 'UpLeg'])
            aim(T, 'lowerleg01.' + s, H('foot.' + s) - H('lowerleg01.' + s), L[S + 'Foot'] - L[S + 'Leg'])
            aim(T, 'foot.' + s, H('toe3-1.' + s) - H('foot.' + s), L[S + 'ToeBase'] - L[S + 'Foot'])
        for k, bn in enumerate(OUT_BONES):
            q = Rot.from_matrix(T.local[BI[bn]]).as_quat(); Q[f, k] = q if q[3] >= 0 else -q
        LOC[f] = [L['LeftFoot'], L['RightFoot'], L['Hips']]
        # root offset relative to rest (sway / bob), hips local position
        RO[f] = [L['Hips'][0], L['Hips'][1] - meanHipY + (meanHipY - restRoot[1]) * 0, L['Hips'][2]]
    # quaternion continuity
    for k in range(len(OUT_BONES)):
        for f in range(1, nf):
            if np.dot(Q[f, k], Q[f - 1, k]) < 0: Q[f, k] = -Q[f, k]
    # root motion in facing frame
    vel = np.gradient(traj, dt, axis=0)
    fwdv = np.array([vel[f, 0] * np.sin(yaw[f]) + vel[f, 1] * np.cos(yaw[f]) for f in range(nf)])
    latv = np.array([vel[f, 0] * np.cos(yaw[f]) - vel[f, 1] * np.sin(yaw[f]) for f in range(nf)])
    yawr = np.gradient(yaw, dt)
    # contacts
    con = np.zeros((nf, 2))
    for k, S in enumerate(('Left', 'Right')):
        foot = P[S + 'Foot']; toe = P[S + 'ToeBase']
        hv = np.linalg.norm(np.gradient(foot[:, [0, 2]], dt, axis=0), axis=1)
        low = np.minimum(foot[:, 1] - foot[:, 1].min(), (toe[:, 1] - toe[:, 1].min()) * 1.5)
        con[:, k] = ((low < 0.04) & (hv < 0.7)).astype(float)
    ankY = np.stack([P['LeftFoot'][:, 1], P['RightFoot'][:, 1]], 1)
    if mirror:
        latv = -latv; yawr = -yawr; yaw = -yaw; con = con[:, ::-1].copy(); ankY = ankY[:, ::-1].copy()
    if mirror: yaw_m = yaw
    return dict(name=fname, mirror=mirror, LOC=LOC, dt=dt, n=nf, Q=Q, RO=RO, fwd=fwdv, lat=latv, yawr=yawr, yaw=yaw, con=con, ankY=ankY, hipY=meanHipY, traj=traj)

def heel_strikes(c, side=0):
    x = c['con'][:, side]; return [i for i in range(1, len(x)) if x[i] > 0.5 and x[i - 1] < 0.5]

if __name__ == '__main__':
    for fn in sys.argv[1:]:
        c = process(fn)
        hs = heel_strikes(c, 0); hsR = heel_strikes(c, 1)
        print(fn, 'frames', c['n'], 'speed fwd mean %.2f max %.2f' % (np.median(c['fwd']), c['fwd'].max()),
              'yaw change %.0f deg' % np.degrees(c['yaw'][-1] - c['yaw'][0]), 'L strikes', hs, 'R', hsR, 'hipY %.2f' % c['hipY'])

# ---------------- export library ----------------
def qmul(a, b):
    return (Rot.from_quat(a) * Rot.from_quat(b)).as_quat()

def slice_clip(c, a, b, loop):
    Q = c['Q'][a:b + (1 if not loop else 0)].copy(); RO = c['RO'][a:b + (1 if not loop else 0)].copy()
    n = len(Q)
    if loop:
        # distribute the mismatch between frame b and frame a over the cycle
        for k in range(Q.shape[1]):
            qa = Rot.from_quat(c['Q'][a, k]); qb = Rot.from_quat(c['Q'][b, k])
            err = (qb * qa.inv()).as_rotvec()
            for f in range(n):
                Q[f, k] = (Rot.from_rotvec(-err * f / n) * Rot.from_quat(Q[f, k])).as_quat()
        dro = c['RO'][b] - c['RO'][a]
        for f in range(n): RO[f] -= dro * f / n
    for k in range(Q.shape[1]):
        for f in range(n):
            if Q[f, k, 3] < 0 and f == 0: Q[f, k] = -Q[f, k]
            if f > 0 and np.dot(Q[f, k], Q[f - 1, k]) < 0: Q[f, k] = -Q[f, k]
    sl = slice(a, a + n)
    return dict(n=n, loop=loop, Q=Q, RO=RO, fwd=c['fwd'][sl], lat=c['lat'][sl], yawr=c['yawr'][sl], con=c['con'][sl],
                dyaw=float(c['yaw'][min(a + n - 1, c['n'] - 1)] - c['yaw'][a]))

def first_after(lst, x): return next(i for i in lst if i >= x)
def last_before(lst, x): return max(i for i in lst if i <= x)

def build():
    lib = {}
    def add(name, src, a, b, loop):
        c = CL[src]; s = slice_clip(c, a, b, loop)
        s['speed'] = float(np.mean(s['fwd'])) if loop else float(s['fwd'][0])
        lib[name] = s
        print('%-10s %s [%d,%d) n=%d speed=%.2f dyaw=%.0f' % (name, src, a, b, s['n'], s['speed'], np.degrees(s['dyaw'])))
    names = ['016/16_15.bvh', '016/16_35.bvh', '016/16_55.bvh', '016/16_17.bvh', '016/16_19.bvh', '016/16_41.bvh', '016/16_43.bvh', '016/16_33.bvh', '016/16_57.bvh']
    global CL; CL = {n: process(n) for n in names}
    hs = lambda n: heel_strikes(CL[n], 0)
    add('walk', '016/16_15.bvh', 34, 69, True)
    add('jog', '016/16_35.bvh', 1, 26, True)
    add('run', '016/16_55.bvh', 9, 27, True)
    # turns: from the left strike before the yaw starts changing to the one after it settles
    for nm, src in (('walkTurnL', '016/16_17.bvh'), ('walkTurnR', '016/16_19.bvh'), ('runTurnL', '016/16_41.bvh'), ('runTurnR', '016/16_43.bvh')):
        c = CL[src]; y = c['yaw'] - c['yaw'][0]; tot = y[-1]
        i0 = int(np.argmax(np.abs(y) > abs(tot) * 0.08)); i1 = int(np.argmax(np.abs(y) > abs(tot) * 0.92))
        L = hs(src)
        a = last_before(L, i0) if any(i <= i0 for i in L) else max(0, i0 - 6)
        b = first_after(L, i1) if any(i >= i1 for i in L) else c['n'] - 1
        add(nm, src, a, b, False)
    # stops: from the left strike before deceleration to the end of the clip
    for nm, src in (('walkStop', '016/16_33.bvh'), ('runStop', '016/16_57.bvh')):
        c = CL[src]; v = c['fwd']; vmax = v.max()
        idec = int(np.argmax((np.arange(len(v)) > np.argmax(v)) & (v < vmax * 0.85)))
        L = hs(src); a = last_before(L, idec) if any(i <= idec for i in L) else 0
        stopf = int(np.argmax((np.arange(len(v)) > idec) & (np.abs(v) < 0.05))) or len(v) - 1
        b = min(c['n'] - 1, stopf + 12)
        add(nm, src, a, b, False)
    # pack
    out = {'bones': OUT_BONES, 'fps': 30, 'clips': {}}
    blob = bytearray()
    def put(arr):
        nonlocal blob
        while len(blob) % 4: blob.append(0)
        off = len(blob); b = arr.tobytes(); blob += b; return [off, len(b)]
    for nm, s in lib.items():
        q = np.round(s['Q'] * 32767).astype(np.int16)
        out['clips'][nm] = {'n': s['n'], 'loop': s['loop'], 'speed': round(s['speed'], 3), 'dyaw': round(s['dyaw'], 4),
            'q': put(q), 'ro': put(s['RO'].astype(np.float32)),
            'rm': put(np.stack([s['fwd'], s['lat'], s['yawr']], 1).astype(np.float32)),
            'con': put((s['con'] * 255).astype(np.uint8))}
    gz = gzip.compress(bytes(blob), 9)
    js = 'export const MOCAP_META=' + json.dumps(out, separators=(',', ':')) + ';\nexport const MOCAP_B64="' + base64.b64encode(gz).decode() + '";\n'
    open('/home/claude/game3d/src/data_mocap.js', 'w').write(js)
    print('mocap raw', len(blob), 'gz', len(gz))

if __name__ == '__main__' and sys.argv[1:] == ['build']:
    pass
