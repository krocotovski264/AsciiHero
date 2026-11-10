#!/usr/bin/env python3
"""Build the full locomotion library (v2) from CMU mocap clips."""
import numpy as np, json, base64, gzip
from scipy.spatial.transform import Rotation as Rot
import retarget as R

CACHE = {}
def clip(fn, mirror=False):
    k = (fn, mirror)
    if k not in CACHE: CACHE[k] = R.process(fn, mirror=mirror)
    return CACHE[k]

LIB = {}
def strikes(c, foot): return R.heel_strikes(c, 0 if foot == 'L' else 1)

def rotate_to_L(Qs, ROs, cons, c, a, b):
    """cyclic shift a loop [a,b) so that frame 0 is a LEFT heel strike"""
    Ls = [i - a for i in strikes(c, 'L') if a <= i < b]
    return (Ls[0] if Ls else 0)

def make_loop(name, fn, a, b, mirror=False, note=''):
    c = clip(fn, mirror)
    s = R.slice_clip(c, a, b, True)
    sh = rotate_to_L(None, None, None, c, a, b)
    if sh:
        for k in ('Q', 'RO', 'fwd', 'lat', 'yawr', 'con'): s[k] = np.roll(s[k], -sh, axis=0)
        # re-align quaternion hemispheres after the roll
        for kk in range(s['Q'].shape[1]):
            for f in range(1, s['n']):
                if np.dot(s['Q'][f, kk], s['Q'][f - 1, kk]) < 0: s['Q'][f, kk] = -s['Q'][f, kk]
    s['speed'] = float(np.mean(s['fwd'])); s['lspeed'] = float(np.mean(s['lat'])); s['yawrate'] = float(np.mean(s['yawr']))
    s['kind'] = 'loop'; LIB[name] = s
    print('%-14s loop %-14s %s[%d,%d) n=%d v=%.2f lat=%.2f yawr=%.2f %s' % (name, fn, 'M' if mirror else ' ', a, b, s['n'], s['speed'], s['lspeed'], s['yawrate'], note))

def make_one(name, fn, a, b, mirror=False, kind='one', note=''):
    c = clip(fn, mirror)
    b = min(b, c['n'] - 1); a = max(0, a)
    s = R.slice_clip(c, a, b, False)
    s['speed'] = float(s['fwd'][0]); s['speedEnd'] = float(s['fwd'][-1]); s['kind'] = kind
    s['startFoot'] = 'L' if s['con'][0, 0] >= s['con'][0, 1] else 'R'
    LIB[name] = s
    print('%-14s %-4s %-14s %s[%d,%d) n=%d v0=%.2f v1=%.2f dyaw=%.0f %s' % (name, kind, fn, 'M' if mirror else ' ', a, b, s['n'], s['speed'], s['speedEnd'], np.degrees(s['dyaw']), note))

def cycle_near(c, frac=0.5, foot='L'):
    st = strikes(c, foot); mid = c['n'] * frac
    best = min(range(len(st) - 1), key=lambda i: abs((st[i] + st[i + 1]) / 2 - mid))
    return st[best], st[best + 1]

def turn_events(c, thr=0.7, pad=5):
    y = c['yawr']; on = np.abs(y) > thr; ev = []; i = 0
    while i < len(on):
        if on[i]:
            j = i
            while j < len(on) and on[j]: j += 1
            if j - i > 5: ev.append((max(0, i - pad), min(len(on) - 1, j + pad)))
            i = j
        else: i += 1
    return ev

# ---------------- 1. core loops ----------------
c = clip('077/77_02.bvh'); make_loop('idle', '077/77_02.bvh', 30, 150, note='standing')
make_one('idleBreak', '040/40_10.bvh', 1150, 1300, kind='idle', note='wait for bus: look around, shift weight')
c = clip('016/16_15.bvh'); make_loop('walk', '016/16_15.bvh', 34, 69)
make_loop('jog', '016/16_35.bvh', 1, 26)
c = clip('009/09_07.bvh'); make_loop('sprint', '009/09_07.bvh', 5, 27)
# ---------------- 2. arcs (veer loops) ----------------
for nm, fn in (('walkArcL', '016/16_11.bvh'), ('walkArcR', '016/16_13.bvh'), ('jogArcL', '016/16_37.bvh'), ('jogArcR', '016/16_39.bvh')):
    c = clip(fn); i = int(np.argmax(np.abs(gauss := np.convolve(c['yawr'], np.ones(9) / 9, 'same'))))
    st = strikes(c, 'L'); pairs = [(st[k], st[k + 1]) for k in range(len(st) - 1)]
    a, b = min(pairs, key=lambda p: abs((p[0] + p[1]) / 2 - i))
    make_loop(nm, fn, a, b)
# ---------------- 3. pivots & turns ----------------
for nm, fn in (('turnInPlaceL', '069/69_17.bvh'), ('turnInPlaceR', '069/69_19.bvh')):
    c = clip(fn); ev = turn_events(c, 0.6)
    # pick the event closest to 90 degrees
    best = min(ev, key=lambda e: abs(abs(c['yaw'][e[1]] - c['yaw'][e[0]]) - np.pi / 2))
    make_one(nm, fn, best[0], best[1] + 4, kind='turn')
c = clip('036/36_02.bvh'); ev = turn_events(c, 0.8, 8)
best = max(ev, key=lambda e: abs(c['yaw'][e[1]] - c['yaw'][e[0]]))
make_one('pivot180R', '036/36_02.bvh', best[0], best[1] + 6, kind='turn', note='walk forward, turn around')
make_one('pivot180L', '036/36_02.bvh', best[0], best[1] + 6, mirror=True, kind='turn')
for nm, fn, m in (('walkTurnL', '016/16_17.bvh', False), ('walkTurnR', '016/16_19.bvh', False),
                  ('cutL', '016/16_51.bvh', False), ('cutR', '016/16_51.bvh', True),
                  ('jogTurnL', '016/16_41.bvh', False), ('jogTurnR', '016/16_43.bvh', False)):
    c = clip(fn, m); y = c['yaw'] - c['yaw'][0]; tot = y[-1]
    i0 = int(np.argmax(np.abs(y) > abs(tot) * 0.08)); i1 = int(np.argmax(np.abs(y) > abs(tot) * 0.92))
    L = strikes(c, 'L')
    a = max([i for i in L if i <= i0], default=max(0, i0 - 6)); b = min([i for i in L if i >= i1], default=c['n'] - 1)
    make_one(nm, fn, a, b, mirror=m, kind='turn')
# ---------------- 4. starts & stops ----------------
def start_clip(nm, fn, m=False):
    c = clip(fn, m); v = c['fwd']; cruise = np.percentile(v, 90)
    i0 = int(np.argmax(v > 0.06)); i1 = int(np.argmax(v > cruise * 0.85))
    st = sorted(strikes(c, 'L') + strikes(c, 'R')); b = min([i for i in st if i >= i1], default=c['n'] - 1)
    make_one(nm, fn, i0 - 8, b, mirror=m, kind='start')
start_clip('walkStart', '082/82_08.bvh'); start_clip('jogStart', '104/104_06.bvh'); start_clip('sprintStart', '104/104_53.bvh')
def stop_clip(nm, fn, m=False):
    c = clip(fn, m); v = c['fwd']; vmax = v.max(); ip = int(np.argmax(v))
    idec = int(np.argmax((np.arange(len(v)) > ip) & (v < vmax * 0.85)))
    st = sorted(strikes(c, 'L') + strikes(c, 'R')); a = max([i for i in st if i <= idec], default=max(0, idec - 8))
    stopf = int(np.argmax((np.arange(len(v)) > idec) & (np.abs(v) < 0.05))) or len(v) - 1
    make_one(nm, fn, a, stopf + 14, mirror=m, kind='stop')
for base, fn in (('walkStop', '016/16_33.bvh'), ('jogStop', '104/104_09.bvh'), ('sprintStop', '104/104_56.bvh')):
    stop_clip(base + 'A', fn); stop_clip(base + 'B', fn, True)
# ---------------- 5. strafe / backward ----------------
c = clip('111/111_01.bvh'); a, b = cycle_near(c, 0.45); make_loop('walkBack', '111/111_01.bvh', a, b)
c = clip('069/69_42.bvh')
ok = (c['lat'] > 0.32) & (np.abs(c['yawr']) < 0.35)
st = strikes(c, 'L'); pairs = [(st[k], st[k + 1]) for k in range(len(st) - 1) if ok[st[k]:st[k + 1]].mean() > 0.9]
a, b = pairs[len(pairs) // 2]
make_loop('strafeL', '069/69_42.bvh', a, b); make_loop('strafeR', '069/69_42.bvh', a, b, mirror=True)

# ---------------- pack ----------------
out = {'bones': R.OUT_BONES, 'fps': 30, 'clips': {}}
blob = bytearray()
def put(arr):
    global blob
    while len(blob) % 4: blob.append(0)
    off = len(blob); b = arr.tobytes(); blob += b; return [off, len(b)]
for nm, s in LIB.items():
    q = np.round(s['Q'] * 32767).astype(np.int16)
    meta = {k: (round(float(s[k]), 4) if isinstance(s[k], (float, np.floating)) else s[k]) for k in ('n', 'kind', 'speed', 'dyaw') if k in s}
    for k in ('lspeed', 'yawrate', 'speedEnd'):
        if k in s: meta[k] = round(float(s[k]), 4)
    if 'startFoot' in s: meta['startFoot'] = s['startFoot']
    meta['loop'] = s['kind'] == 'loop'; meta['n'] = int(s['n'])
    meta.update({'q': put(q), 'ro': put(s['RO'].astype(np.float32)), 'rm': put(np.stack([s['fwd'], s['lat'], s['yawr']], 1).astype(np.float32)), 'con': put((s['con'] * 255).astype(np.uint8))})
    out['clips'][nm] = meta
gz = gzip.compress(bytes(blob), 9)
open('/home/claude/game3d/src/data_mocap.js', 'w').write('export const MOCAP_META=' + json.dumps(out, separators=(',', ':')) + ';\nexport const MOCAP_B64="' + base64.b64encode(gz).decode() + '";\n')
print('clips', len(LIB), 'raw', len(blob), 'gz', len(gz))
