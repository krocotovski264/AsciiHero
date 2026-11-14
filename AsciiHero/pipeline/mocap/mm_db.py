#!/usr/bin/env python3
"""Motion Matching database from CMU mocap (retargeted to MakeHuman).
Features per frame (after D. Holden's open-source Motion-Matching, MIT):
  foot L/R position (3+3), foot L/R velocity (3+3), hip velocity (3),
  trajectory positions at +10/+20/+30 frames (2*3), trajectory facing dirs (2*3)  -> 27 dims"""
import numpy as np, json, base64, gzip, glob
import retarget as R

SPEC = []  # (file, start, end)
def add(fn, a=0, b=None): SPEC.append((fn, a, b))
for n in '11 12 13 14 15 16 17 18 19 20 21 22 25 26 27 29 31 33 35 36 37 39 41 42 43 44 45 46 47 57 58 08'.split(): add(f'016/16_{n}.bvh')
for n, a, b in (('02', 0, 300), ('06', 0, 420), ('07', 0, 420), ('12', 0, 420), ('13', 0, 420), ('16', 0, 300), ('18', 0, 300), ('21', 0, 300),
                ('25', 0, 300), ('36', 0, 400), ('39', 0, 400), ('44', 0, 420), ('50', 0, 400)): add(f'069/69_{n}.bvh', a, b)
for n in '06 08 09 10 53 55 56 57'.split(): add(f'104/104_{n}.bvh')
for n in '01 02 03 04'.split(): add(f'143/143_{n}.bvh')
for n in '01 02 03 04 05 06 12'.split(): add(f'009/09_{n}.bvh')
add('036/36_02.bvh', 0, 420); add('111/111_01.bvh'); add('077/77_02.bvh'); add('040/40_10.bvh', 1000, 1400); add('035/35_01.bvh'); add('082/82_08.bvh', 200, 425)

H = 10, 20, 30
rows = {k: [] for k in ('Q', 'RO', 'RM', 'CON', 'F')}; ranges = []; valid = []; tags = []
total = 0
for fn, a, b in SPEC:
    for mirror in (False,):
        try: c = R.process(fn, mirror=mirror)
        except Exception as e: print('skip', fn, e); continue
        n = c['n']; b2 = n if b is None else min(b, n); a2 = min(a, b2 - 1)
        yaw = c['yaw']; traj = c['traj']
        if mirror: pass
        LOC = c['LOC']; dt = c['dt']
        vel = np.gradient(LOC, dt, axis=0)
        F = np.zeros((n, 27))
        for f in range(n):
            F[f, 0:3] = LOC[f, 0]; F[f, 3:6] = LOC[f, 1]
            F[f, 6:9] = vel[f, 0]; F[f, 9:12] = vel[f, 1]
            F[f, 12:15] = [c['lat'][f], 0, c['fwd'][f]]   # hip/root velocity in local frame (x = left)
            for k, h in enumerate(H):
                g = min(f + h, n - 1)
                d = traj[g] - traj[f]
                y0 = (-yaw[f]) if mirror else yaw[f]        # yaw array was negated for mirrored clips
                cy, sy = np.cos(-y0), np.sin(-y0)
                lx, lz = cy * d[0] + sy * d[1], -sy * d[0] + cy * d[1]
                if mirror: lx = -lx
                F[f, 15 + 2 * k:17 + 2 * k] = [lx, lz]
                dy = yaw[g] - yaw[f]
                F[f, 21 + 2 * k:23 + 2 * k] = [np.sin(dy), np.cos(dy)]
        sl = slice(a2, b2)
        m = b2 - a2
        rows['Q'].append(c['Q'][sl]); rows['RO'].append(c['RO'][sl]); rows['F'].append(F[sl])
        rows['RM'].append(np.stack([c['fwd'][sl], c['lat'][sl], c['yawr'][sl]], 1)); rows['CON'].append(c['con'][sl])
        v = np.ones(m, dtype=bool); v[-31:] = False; v[:2] = False
        valid.append(v); ranges.append([total, total + m, fn + ('M' if mirror else '')]); total += m
# sword cuts: appended after the locomotion frames and never
# offered to motion matching (valid = 0); the game plays them by frame index
import cuts as bandai, json as _json
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
#   sitdown 111_07 "get up from floor", played backwards: lowering onto the ground with a hand down
#   sitM    built by hand (author.py): sitting cross-legged, hands on the knees
#   sitF    built by hand: legs folded to one side, the other hand on the floor
#   getup   111_07 the same get-up forwards
#   leap    83_42  the crouch and spring of the dash
IDLE = {}
def best_loop(Q, lo, hi, W):
    best = None
    for a in range(lo, hi - W):
        e = 2 * np.arccos(np.abs(np.sum(Q[a] * Q[a + W], -1)).clip(0, 1)).sum()
        if best is None or e < best[0]: best = (e, a)
    return best[1]
for name, fn, a4, b4, rev in (('idleM', '142/142_13.bvh', 1199, 1289, 0), ('idleF', '113/113_21.bvh', 30, 130, 0), ('shift', '139/139_02.bvh', 0, 120, 0),
                              ('wave', '141/141_16.bvh', 1, 56, 0), ('sitdown', '111/111_07.bvh', 372, 437, 1),
                              ('getup', '111/111_07.bvh', 372, 437, 0), ('leap', '083/83_42.bvh', 67, 105, 0)):
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
import author
for name, kind in (('sitM', 'lotus'), ('sitF', 'side')):
    q, ro = author.pose(kind); m = 2
    rows['Q'].append(np.repeat(q[None], m, 0)); rows['RO'].append(np.repeat(ro[None], m, 0)); rows['F'].append(np.zeros((m, 27)))
    rows['RM'].append(np.zeros((m, 3))); rows['CON'].append(np.ones((m, 2)))
    valid.append(np.zeros(m, dtype=bool)); ranges.append([total, total + m, name])
    IDLE[name] = {'a': total, 'n': m, 'minY': round(float(ro[1]), 3)}; total += m
_json.dump(IDLE, open('idle.json', 'w'))
_json.dump(ATK, open('atk.json', 'w'))
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
    if nm.startswith('040/40_10'): idleB[s0:s1] = 1
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
open('../../game/src/data_mm.js', 'w').write('export const MM_META=' + json.dumps(meta, separators=(',', ':')) + ';\nexport const MM_B64="' + base64.b64encode(gz).decode() + '";\n')
print('raw', len(blob), 'gz', len(gz), 'b64', len(gz) * 4 // 3)
