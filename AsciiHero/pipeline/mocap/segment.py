# Cut the Motion Matching database into named locomotion clips for the Unity Animator.
import numpy as np, json
from seg_load import load
D = load(); q = D['q']; rm = D['rm']; con = D['con']; R = D['ranges']; n = D['n']
s = np.hypot(rm[:, 0], rm[:, 1]); yr = rm[:, 2]           # three.js convention: yr > 0 = turning left
rid = np.zeros(n, int)
for i, (a, b) in enumerate(R): rid[a:b] = i
def same(a, b): return 0 <= a and b < n and rid[a] == rid[b]
w = np.sqrt(np.clip(1 - (q ** 2).sum(-1), 0, 1))
Q4 = np.concatenate([q, w[..., None]], -1)
def pdiff(a, b): return float((1 - np.abs((Q4[a] * Q4[b]).sum(-1))).sum())
out = []
def add(name, a, b, loop, mirror=False, note=''):
    sp = float(s[a:b + 1].mean()); yaw = float(np.degrees(yr[a:b + 1].sum() / 30))
    out.append(dict(name=name, start=int(a), end=int(b), loop=loop, mirror=mirror, speed=round(sp, 3), yaw=round(yaw * (-1 if mirror else 1), 1)))
    print(f'{name:16s} {a:5d}-{b:5d} ({(b-a+1)/30:.2f}s) loop={loop} mirror={mirror} v={sp:.2f} yaw={yaw*(-1 if mirror else 1):+.0f} {note}')

def best_loop(lo, hi, yrr, Lr=(18, 48), extra=None):
    best = None
    for a in range(n - 60):
        if not (lo <= s[a] <= hi): continue
        for L in range(Lr[0], Lr[1]):
            b = a + L
            if not same(a, b): break
            seg = slice(a, b + 1)
            if s[seg].min() < lo * 0.85 or s[seg].max() > hi * 1.15: continue
            my = yr[seg].mean()
            if not (yrr[0] <= my <= yrr[1]): continue
            if np.abs(rm[seg, 1]).mean() > 0.35: continue
            e = pdiff(a, b) + 0.5 * abs(s[a] - s[b]) + 0.3 * abs(yr[a] - yr[b])
            if extra: e += extra(seg)
            if best is None or e < best[0]: best = (e, a, b)
    return best

bands = {'Walk': (0.9, 1.75), 'Jog': (1.8, 2.9), 'Sprint': (2.95, 5.5)}
loops = {}
for nm, (lo, hi) in bands.items():
    r = best_loop(lo, hi, (-0.12, 0.12)); loops[nm] = r
    if r: add(nm, r[1], r[2], True, note='err %.2f' % r[0])
for nm, (lo, hi) in bands.items():
    r = best_loop(lo, hi, (0.45, 3.0), extra=lambda seg: -0.4 * yr[seg].mean())
    if r:
        add(nm + 'LeanL', r[1], r[2], True, note='err %.2f' % r[0]); add(nm + 'LeanR', r[1], r[2], True, True)
    else:
        r = best_loop(lo, hi, (-3.0, -0.45), extra=lambda seg: 0.4 * yr[seg].mean())
        if r: add(nm + 'LeanR', r[1], r[2], True); add(nm + 'LeanL', r[1], r[2], True, True)
        else: print('no lean for', nm)

# idle loop
best = None
for a in range(n - 100):
    if s[a] > 0.1: continue
    for L in (45, 60, 75, 90):
        b = a + L
        if not same(a, b) or s[a:b + 1].max() > 0.1 or np.abs(yr[a:b + 1]).max() > 0.2: continue
        e = pdiff(a, b) + 0.05 * np.abs(np.diff(Q4[a:b + 1], axis=0)).sum() / L
        if best is None or e < best[0]: best = (e, a, b)
add('Idle', best[1], best[2], True)
# idle breaker = tagged frames
ib = np.where(D['idleB'] > 0)[0]; add('IdleBreaker', int(ib[0]) + 150, int(ib[0]) + 299, False)   # clip 40_10, frames 1150-1300

# turns in place
def turn(target, sign):
    best = None
    for a in range(n - 90):
        if s[a] > 0.3 or abs(yr[a]) > 0.35: continue
        acc = 0
        for b in range(a + 1, min(n, a + 120)):
            if not same(a, b) or s[b] > 0.5: break
            acc += yr[b] / 30
            d = np.degrees(acc) * sign
            if d >= target - 12 and abs(yr[b]) < 0.5:
                e = abs(d - target) * 0.05 + (b - a) / 30 + s[a:b + 1].mean() * 2
                if best is None or e < best[0]: best = (e, a, min(b + 6, n - 1))
                break
    return best
for tgt in (90, 180):
    for sign, side, other in ((1, 'L', 'R'), (-1, 'R', 'L')):
        r = turn(tgt, sign)
        if r: add(f'Turn{tgt}{side}', r[1], r[2], False); add(f'Turn{tgt}{other}', r[1], r[2], False, True); break
    else: print('no turn', tgt)

# stops / starts
def stops(lo, hi):
    best = None
    for t in range(15, n):
        if not (s[t] < 0.12 and s[t - 1] >= 0.12): continue
        a = t
        while a > 0 and same(a - 1, t) and s[a - 1] < lo: a -= 1
        if not same(a - 12, t) or not (lo <= s[a - 12] <= hi * 1.1) or s[a - 12:a].max() > hi * 1.1: continue
        dec = t - a
        if dec > 40: continue
        b = t
        while b + 1 < n and same(t, b + 1) and b - t < 24 and s[b + 1] < 0.12: b += 1
        if b - t < 10: continue
        e = dec / 30 + abs(yr[a - 12:b].mean())
        if best is None or e < best[0]: best = (e, a - 12, b)
    return best
for nm, (lo, hi) in (('Walk', (0.9, 1.75)), ('Jog', (1.8, 2.7)), ('Sprint', (2.7, 5.5))):
    r = stops(lo, hi)
    if r: add(nm + 'Stop', r[1], r[2], False)
    else: print('no stop', nm)
def starts(lo, hi):
    best = None
    for t in range(1, n - 40):
        if not (s[t] >= 0.12 and s[t - 1] < 0.12): continue
        b = t
        while b + 1 < n and same(t, b + 1) and s[b] < lo and b - t < 45: b += 1
        if s[b] < lo or b - t >= 45 or not same(t - 6, b + 10) or s[b:b + 10].max() > hi * 1.2: continue
        e = (b - t) / 30 + abs(yr[t:b].mean())
        if best is None or e < best[0]: best = (e, t - 6, b + 10)
    return best
for nm, lo, hi in (('WalkStart', 0.9, 1.75), ('RunStart', 1.8, 4.0)):
    r = starts(lo, hi)
    if r: add(nm, r[1], r[2], False)
    else: print('no', nm)
# run pivot 180
best = None
for a in range(n - 60):
    if s[a] < 1.8: continue
    acc = 0
    for b in range(a + 1, min(n, a + 50)):
        if not same(a, b): break
        acc += yr[b] / 30
        if abs(np.degrees(acc)) >= 160:
            if s[b] < 0.9: break
            e = (b - a) / 30 - s[a] * 0.1
            if best is None or e < best[0]: best = (e, a, min(b + 8, n - 1), np.sign(acc))
            break
if best:
    side, other = ('L', 'R') if best[3] > 0 else ('R', 'L')
    add('RunPivot180' + side, best[1], best[2], False); add('RunPivot180' + other, best[1], best[2], False, True)
json.dump(dict(fps=30, frames=n, clips=out), open(__import__('os').path.join(__import__('os').path.dirname(__file__), '../../../Unity/Assets/Scripts/Editor/LocomotionClips.json'), 'w'), indent=1)
print(len(out), 'clips')
