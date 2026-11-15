#!/usr/bin/env python3
"""Motion Matching database from CMU mocap (retargeted to MakeHuman).
Features per frame (after D. Holden's open-source Motion-Matching, MIT):
  foot L/R position (3+3), foot L/R velocity (3+3), hip velocity (3),
  trajectory positions at +10/+20/+30 frames (2*3), trajectory facing dirs (2*3)  -> 27 dims"""
import numpy as np, json, base64, gzip, glob
import retarget as R

SPEC = []  # (file, start, end)
# locomotion: a studio-quality set (30 fps, one actor per file): walking with side steps and steps back, running
# (much of it sideways, good for strafing in a fight), sprinting. Exactly 9945 frames, so everything after it keeps
# its frame index.
# a set of styled locomotion in front (neutral style: walking and running forwards, backwards and sideways, standing),
# then the larger set; its walk is shortened by as much, so the frame indices after the block stay as they were
STYLE = ['FW', 'FR', 'BW', 'BR', 'SW', 'SR', 'ID', 'Rushed_FR']
LOCO = [('walk1_subject1.bvh', 60, 4660), ('run1_subject2.bvh', 60, 3260)]
def _sprint_window(n_need):
    try: c = np.load('/tmp/lf_sprint1_subject2.npz')
    except Exception: import ground as _g; c = _g.process('sprint1_subject2.bvh')
    sp = np.hypot(c['fwd'], c['lat']); fast = (sp > 2.8).astype(float)
    cs = np.concatenate([[0], np.cumsum(fast)]); best = max(range(60, len(sp) - n_need), key=lambda a: cs[a + n_need] - cs[a]); return best
H = 10, 20, 30
rows = {k: [] for k in ('Q', 'RO', 'RM', 'CON', 'F')}; ranges = []; valid = []; tags = []
total = 0
import sys as _sys; import styles as s100
for k in STYLE:
    c = s100.clip(k); n = c['n']; yaw = c['yaw']; traj = c['traj']; LOC = c['LOC']; dt = c['dt']
    RO = c['RO'].copy(); hy = c['hipsY']; RO[:, 1] = hy - np.percentile(hy, 95) + 0.01
    vel = np.gradient(LOC, dt, axis=0); F = np.zeros((n, 27))
    for f in range(n):
        F[f, 0:3] = LOC[f, 0]; F[f, 3:6] = LOC[f, 1]; F[f, 6:9] = vel[f, 0]; F[f, 9:12] = vel[f, 1]; F[f, 12:15] = [c['lat'][f], 0, c['fwd'][f]]
        for kk, h in enumerate(H):
            g = min(f + h, n - 1); d = traj[g] - traj[f]; cy, sy = np.cos(-yaw[f]), np.sin(-yaw[f])
            F[f, 15 + 2 * kk:17 + 2 * kk] = [cy * d[0] + sy * d[1], -sy * d[0] + cy * d[1]]
            dy = yaw[g] - yaw[f]; F[f, 21 + 2 * kk:23 + 2 * kk] = [np.sin(dy), np.cos(dy)]
    rows['Q'].append(c['Q']); rows['RO'].append(RO); rows['F'].append(F)
    rows['RM'].append(np.stack([c['fwd'], c['lat'], c['yawr']], 1)); rows['CON'].append(c['con'])
    v = np.ones(n, dtype=bool); v[-31:] = False; v[:2] = False
    valid.append(v); ranges.append([total, total + n, 'style/' + k]); total += n
print('styled frames', total)
LOCO[0] = ('walk1_subject1.bvh', 60, 60 + 4600 - total)
_used = total + sum(b - a for _, a, b in LOCO); _need = 9945 - _used; _sa = _sprint_window(_need); LOCO.append(('sprint1_subject2.bvh', _sa, _sa + _need))
import ground as lafan
for fn, a, b in LOCO:
    cache = '/tmp/lf_' + fn[:-4] + '.npz'
    try: c = dict(np.load(cache)); c['n'] = len(c['Q']); c['dt'] = 1 / 30.0
    except Exception: c = lafan.process(fn)
    n = c['n']; b2 = min(b, n); a2 = min(a, b2 - 1)
    yaw = c['yaw']; traj = c['traj']; LOC = c['LOC']; dt = c['dt']
    RO = c['RO'].copy(); hy = c['hipsY']; RO[:, 1] = hy - np.percentile(hy, 95) + 0.01      # pelvis height: 0 = standing (as the idle stances)
    vel = np.gradient(LOC, dt, axis=0)
    F = np.zeros((n, 27))
    for f in range(n):
        F[f, 0:3] = LOC[f, 0]; F[f, 3:6] = LOC[f, 1]
        F[f, 6:9] = vel[f, 0]; F[f, 9:12] = vel[f, 1]
        F[f, 12:15] = [c['lat'][f], 0, c['fwd'][f]]
        for k, h in enumerate(H):
            g = min(f + h, n - 1); d = traj[g] - traj[f]
            cy, sy = np.cos(-yaw[f]), np.sin(-yaw[f])
            lx, lz = cy * d[0] + sy * d[1], -sy * d[0] + cy * d[1]
            F[f, 15 + 2 * k:17 + 2 * k] = [lx, lz]
            dy = yaw[g] - yaw[f]; F[f, 21 + 2 * k:23 + 2 * k] = [np.sin(dy), np.cos(dy)]
    sl = slice(a2, b2); m = b2 - a2
    rows['Q'].append(c['Q'][sl]); rows['RO'].append(RO[sl]); rows['F'].append(F[sl])
    rows['RM'].append(np.stack([c['fwd'][sl], c['lat'][sl], c['yawr'][sl]], 1)); rows['CON'].append(c['con'][sl])
    v = np.ones(m, dtype=bool); v[-31:] = False; v[:2] = False
    valid.append(v); ranges.append([total, total + m, 'loco/' + fn[:-4]]); total += m
print('locomotion frames', total)
# sword cuts from the Bandai Namco Research Motiondataset (CC BY-NC 4.0): appended after the CMU frames and never
# offered to motion matching (valid = 0); the game plays them by frame index
import bandai, json as _json
ATK = []
for fn, a, b in (('dataset-1_slash_normal_001.bvh', 164, 230), ('dataset-1_slash_normal_002.bvh', 164, 232)):
    c = bandai.process(fn, a, b); m = c['n']
    rows['Q'].append(c['Q']); rows['RO'].append(c['RO']); rows['F'].append(np.zeros((m, 27)))
    rows['RM'].append(np.zeros((m, 3))); rows['CON'].append(c['con'])
    valid.append(np.zeros(m, dtype=bool)); ranges.append([total, total + m, 'cut/' + fn[-7:-4]])
    ATK.append({'a': total, 'n': m, 'blade': np.round(c['blade'], 3).tolist()}); total += m
# guard with the sword: CMU 76_04 "defensive guard pose" - from standing to both hands raised in front of the face, knees giving
c = R.process('076/76_04.bvh'); ga, gb = 26, 48; m = gb - ga
rows['Q'].append(c['Q'][ga:gb]); rows['RO'].append(c['RO'][ga:gb] - [c['RO'][ga][0], 0, c['RO'][ga][2]]); rows['F'].append(np.zeros((m, 27)))
rows['RM'].append(np.zeros((m, 3))); rows['CON'].append(c['con'][ga:gb])
valid.append(np.zeros(m, dtype=bool)); ranges.append([total, total + m, 'guard'])
ATK.append({'a': total, 'n': m, 'guard': True, 'roY': [round(float(c['RO'][ga][1]), 3), round(float(c['RO'][gb - 1][1]), 3)]}); total += m
# attacks from CMU clips: spear, long axe, shield
import cmu_atk
for name, fn, a, b, pk, mode in cmu_atk.SPEC:
    c = cmu_atk.clip(fn, a, b, pk, mode); m = c['n']
    rows['Q'].append(c['Q']); rows['RO'].append(c['RO']); rows['F'].append(np.zeros((m, 27)))
    rows['RM'].append(np.zeros((m, 3))); rows['CON'].append(c['con'])
    valid.append(np.zeros(m, dtype=bool)); ranges.append([total, total + m, name])
    ATK.append({'name': name, 'a': total, 'n': m, 'peak': int(c['peak']), 'blade': np.round(c['blade'], 3).tolist(), 'bladeL': np.round(c['bladeL'], 3).tolist()}); total += m
    print(name, m, 'peak', c['peak'])
# idle stances (male, female) and idle actions. All retargeted from the actor's T-pose (see retarget.py).
#   idleM   142_13 "relaxed" - a man standing at ease, arms hanging clear of the body
#   idleF   113_21 "standing still" - a woman, weight on one leg
#   shift   139_02 "shifting weight" from foot to foot
#   wave    141_16 "wave hello" (the game lays it on the upper body only)
#   sitdown a 30 fps ground set: from standing, bending, a hand reaching down, onto the seat, knees up
#   sitM    built by hand (author.py): sitting cross-legged, hands on the knees
#   sitF    built by hand: legs folded to one side, the other hand on the floor
#   getup   the same set: from sitting, rocking forward over the feet and standing up
#   leap    83_42  the crouch and spring of the dash
IDLE = {}
def best_loop(Q, lo, hi, W):
    best = None
    for a in range(lo, hi - W):
        e = 2 * np.arccos(np.abs(np.sum(Q[a] * Q[a + W], -1)).clip(0, 1)).sum()
        if best is None or e < best[0]: best = (e, a)
    return best[1]
for name, fn, a4, b4, rev in (('idleM', '142/142_13.bvh', 1199, 1289, 0), ('idleF', '113/113_21.bvh', 30, 130, 0), ('shift', '139/139_02.bvh', 0, 120, 0),
                              ('wave', '141/141_16.bvh', 1, 56, 0), ('leap', '083/83_42.bvh', 67, 105, 0)):
    c = R.process(fn)
    if name == 'idleF': a4 = best_loop(c['Q'], a4, b4, 80); b4 = a4 + 80
    m = b4 - a4; sl = slice(a4, b4)
    RO = c['RO'][sl].copy(); RO[:, 1] += c['hipY'] - c['standY']         # pelvis height relative to the actor standing
    Qs = c['Q'][sl].copy(); CONs = c['con'][sl].copy()
    if name in ('idleM', 'idleF'): RO[:, 1] -= RO[:, 1].mean()        # stances play like the locomotion loops (height about the mean)
    elif name in ('shift', 'wave', 'leap'): RO[:, 1] -= RO[0, 1]
    if rev: RO = RO[::-1].copy(); Qs = Qs[::-1].copy(); CONs = CONs[::-1].copy()
    loop = name.startswith('idle') or name == 'sitloop'
    if loop: RO[:, 0] -= RO[:, 0].mean(); RO[:, 2] -= RO[:, 2].mean()
    elif name == 'getup': RO[:, 0] -= RO[-1, 0]; RO[:, 2] -= RO[-1, 2]        # ends standing where the stance is
    else: RO[:, 0] -= RO[0, 0]; RO[:, 2] -= RO[0, 2]
    rows['Q'].append(Qs); rows['RO'].append(RO); rows['F'].append(np.zeros((m, 27)))
    rows['RM'].append(np.zeros((m, 3))); rows['CON'].append(CONs)
    valid.append(np.zeros(m, dtype=bool)); ranges.append([total, total + m, name])
    IDLE[name] = {'a': total, 'n': m, 'minY': round(float(RO[:, 1].min()), 3)}; total += m
    print(name, fn, a4, b4, m, 'minY %.2f' % RO[:, 1].min(), 'y0 %.2f y1 %.2f' % (RO[0, 1], RO[-1, 1]))
import ground as lafan
for name, fn, a, b in (('sitdown', 'multipleActions1_subject1.bvh', 123, 173), ('getup', 'multipleActions1_subject3.bvh', 911, 945)):
    c = lafan.process(fn, a, b); m = c['n']; RO = c['RO'].copy()
    ref = 0 if name == 'sitdown' else -1
    RO[:, 1] -= RO[ref, 1]; RO[:, 0] -= RO[ref, 0]; RO[:, 2] -= RO[ref, 2]
    rows['Q'].append(c['Q']); rows['RO'].append(RO); rows['F'].append(np.zeros((m, 27)))
    rows['RM'].append(np.zeros((m, 3))); rows['CON'].append(c['con'])
    valid.append(np.zeros(m, dtype=bool)); ranges.append([total, total + m, name])
    IDLE[name] = {'a': total, 'n': m, 'minY': round(float(RO[:, 1].min()), 3)}; total += m
    print(name, fn, a, b, m, 'y0 %.2f y1 %.2f' % (RO[0, 1], RO[-1, 1]))
import author
for name, kind in (('sitM', 'lotus'), ('sitF', 'side')):
    q, ro = author.pose(kind); m = 2
    rows['Q'].append(np.repeat(q[None], m, 0)); rows['RO'].append(np.repeat(ro[None], m, 0)); rows['F'].append(np.zeros((m, 27)))
    rows['RM'].append(np.zeros((m, 3))); rows['CON'].append(np.ones((m, 2)))
    valid.append(np.zeros(m, dtype=bool)); ranges.append([total, total + m, name])
    IDLE[name] = {'a': total, 'n': m, 'minY': round(float(ro[1]), 3)}; total += m
_json.dump(IDLE, open('/home/claude/mocap/idle.json', 'w'))
_json.dump(ATK, open('/home/claude/mocap/atk.json', 'w'))
print('frames', total)
Q = np.concatenate(rows['Q']); RO = np.concatenate(rows['RO']); RM = np.concatenate(rows['RM']); CON = np.concatenate(rows['CON']); F = np.concatenate(rows['F']); V = np.concatenate(valid)
# hemisphere continuity inside each range
for s0, s1, _ in ranges:
    for k in range(Q.shape[1]):
        for f in range(s0 + 1, s1):
            if np.dot(Q[f, k], Q[f - 1, k]) < 0: Q[f, k] = -Q[f, k]
# normalisation (per feature group: mean / std, then weight)
groups = [(0, 6, 0.75), (6, 12, 1.0), (12, 15, 1.0), (15, 21, 1.0), (21, 27, 1.5)]
mean = F[V].mean(0); scale = np.ones(27)
for a, b, w in groups:
    sd = F[V][:, a:b].std(0).mean() + 1e-6; scale[a:b] = w / sd
FN = (F - mean) * scale
# idle-breaker frames (40_10 segment) are tagged so the runtime can pick them occasionally
idleB = np.zeros(total, dtype=np.uint8)
for s0, s1, nm in ranges:
    pass
_sp = np.hypot(RM[:9945, 0], RM[:9945, 1]); _st = np.convolve((_sp < 0.08).astype(float), np.ones(90), 'valid')
_ib = int(np.argmax(_st)); idleB[_ib:_ib + 90] = 1; print('idle breaker', _ib, _st[_ib])
blob = bytearray()
def put(arr):
    global blob
    while len(blob) % 4: blob.append(0)
    off = len(blob); bb = arr.tobytes(); blob += bb; return [off, len(bb)]
Qp = Q.copy(); Qp[Qp[..., 3] < 0] *= -1                       # w >= 0 -> store xyz only
meta = {'bones': R.OUT_BONES, 'fps': 30, 'n': int(total), 'nf': 27, 'mean': mean.tolist(), 'scale': scale.tolist(), 'ranges': ranges,
        'q': put(np.round(Qp[..., :3] * 32767).astype(np.int16)), 'ro': put(np.round(RO * 4000).astype(np.int16)), 'rm': put(np.round(RM * 2000).astype(np.int16)),
        'con': put((CON * 255).astype(np.uint8)), 'f': put(np.round(np.clip(FN, -32, 32) * 1000).astype(np.int16)), 'valid': put(V.astype(np.uint8)), 'idleB': put(idleB)}
gz = gzip.compress(bytes(blob), 9)
open('/home/claude/game3d/src/data_mm.js', 'w').write('export const MM_META=' + json.dumps(meta, separators=(',', ':')) + ';\nexport const MM_B64="' + base64.b64encode(gz).decode() + '";\n')
print('raw', len(blob), 'gz', len(gz), 'b64', len(gz) * 4 // 3)
