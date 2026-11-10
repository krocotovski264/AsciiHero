#!/usr/bin/env python3
"""Bake MakeHuman (CC0 targets/base mesh) into compact variants for the web game."""
import numpy as np, json, struct, gzip, base64, collections, os
MH = '/home/claude/assets/mh/makehuman/data'
OUT = '/home/claude/game3d/build'
os.makedirs(OUT, exist_ok=True)

# ---------- base mesh ----------
V = []; groups = collections.defaultdict(list); g = None
for line in open(f'{MH}/3dobjs/base.obj'):
    if line.startswith('v '): V.append([float(x) for x in line.split()[1:4]])
    elif line.startswith('g '): g = line.split()[1]
    elif line.startswith('f '): groups[g].append([int(t.split('/')[0]) - 1 for t in line.split()[1:]])
V = np.array(V, dtype=np.float64)
NV = len(V)

KEEP = ['body', 'helper-l-eye', 'helper-r-eye', 'helper-skirt', 'helper-hair']
PART_ID = {'body': 0, 'helper-l-eye': 1, 'helper-r-eye': 1, 'helper-skirt': 2, 'helper-hair': 3}
keep_idx = sorted(set(i for k in KEEP for f in groups[k] for i in f))
remap = -np.ones(NV, dtype=np.int64); remap[keep_idx] = np.arange(len(keep_idx))
vpart = np.zeros(len(keep_idx), dtype=np.uint8)
tris = []; tri_part = []
for k in KEEP:
    for f in groups[k]:
        a, b, c, d = [int(remap[i]) for i in f]
        tris += [(a, b, c), (a, c, d)]
        tri_part += [PART_ID[k]] * 2
        for i in (a, b, c, d): vpart[i] = PART_ID[k]
tris = np.array(tris, dtype=np.uint16)
print('kept verts', len(keep_idx), 'tris', len(tris))

# ---------- targets ----------
def load_target(path):
    d = np.zeros((NV, 3))
    for line in open(path):
        if not line.strip() or line[0] == '#': continue
        p = line.split(); d[int(p[0])] = [float(p[1]), float(p[2]), float(p[3])]
    return d
TCACHE = {}
def T(name):
    if name not in TCACHE: TCACHE[name] = load_target(f'{MH}/targets/macrodetails/{name}.target')
    return TCACHE[name]

def tri_weights(v):  # min / average / max
    mx = max(0.0, (v - 0.5) * 2); mn = max(0.0, (0.5 - v) * 2); return {'min': mn, 'average': 1 - mx - mn, 'max': mx}

def morph(gender, muscle, weight):
    out = V.copy()
    gw = {'female': 1 - gender, 'male': gender}
    mw = tri_weights(muscle); ww = tri_weights(weight)
    for gk, gv in gw.items():
        if gv <= 0: continue
        for mk, mv in mw.items():
            for wk, wv in ww.items():
                w = gv * mv * wv
                if w > 1e-4: out += w * T(f'universal-{gk}-young-{mk}muscle-{wk}weight')
        for race in ('african', 'asian', 'caucasian'):
            out += gv / 3.0 * T(f'{race}-{gk}-young')
    return out

# ---------- skeleton ----------
sk = json.load(open(f'{MH}/rigs/default.mhskel'))
wfile = json.load(open(f'{MH}/rigs/default_weights.mhw'))['weights']
bones_all = sk['bones']
# keep bones that have weights + their ancestors
used = set(wfile.keys())
def ancestors(b):
    while b:
        yield b; b = bones_all[b]['parent']
keepb = set()
for b in used: keepb.update(ancestors(b))
# topological order
order = []
def visit(b):
    if b in order: return
    p = bones_all[b]['parent']
    if p: visit(p)
    order.append(b)
for b in sorted(keepb): visit(b)
BI = {b: i for i, b in enumerate(order)}
parents = [BI[bones_all[b]['parent']] if bones_all[b]['parent'] else -1 for b in order]
print('bones', len(order))

def joint_pos(P, jname):
    return P[sk['joints'][jname]].mean(axis=0)

# skin weights top-4 for kept verts
vw = collections.defaultdict(list)
for b, lst in wfile.items():
    for vi, w in lst:
        if remap[vi] >= 0: vw[int(remap[vi])].append((w, BI[b]))
nk = len(keep_idx)
SI = np.zeros((nk, 4), dtype=np.uint8); SW = np.zeros((nk, 4), dtype=np.uint8)
for i in range(nk):
    l = sorted(vw.get(i, []), reverse=True)[:4]
    if not l: l = [(1.0, BI['spine05'] if 'spine05' in BI else 0)]
    s = sum(w for w, _ in l)
    ws = [w / s for w, _ in l]
    q = [int(round(w * 255)) for w in ws]; q[0] += 255 - sum(q)
    for k, (w, bi) in enumerate(l): SI[i, k] = bi; SW[i, k] = q[k]

# ---------- variants ----------
BUILDS = [(0.30, 0.15), (0.50, 0.50), (0.85, 0.50), (0.65, 0.92)]  # (muscle, weight)
variants = []
for sex, gval in (('m', 1.0), ('f', 0.0)):
    for bi_, (m, w) in enumerate(BUILDS):
        P = morph(gval, m, w)
        heads = np.array([joint_pos(P, bones_all[b]['head']) for b in order])
        tails = np.array([joint_pos(P, bones_all[b]['tail']) for b in order])
        Pk = P[keep_idx] * 0.1; heads *= 0.1; tails *= 0.1     # decimeters -> meters
        floor = Pk[vpart == 0][:, 1].min()
        Pk[:, 1] -= floor; heads[:, 1] -= floor; tails[:, 1] -= floor
        height = Pk[vpart == 0][:, 1].max()
        variants.append(dict(sex=sex, build=bi_, P=Pk, heads=heads, tails=tails, height=height))
        print(sex, bi_, 'height %.3f m' % height)

# ---------- pack ----------
# positions quantized to 0.25 mm in int16 around center
lo = np.min([v['P'].min(axis=0) for v in variants], axis=0) - 0.01
blob = bytearray()
def add(arr):
    global blob
    while len(blob) % 4: blob.append(0)
    off = len(blob); b = arr.tobytes(); blob += b; return [off, len(b)]
meta = {'nverts': nk, 'ntris': len(tris), 'bones': order, 'parents': parents, 'lo': lo.tolist(), 'q': 0.00025, 'variants': []}
meta['tris'] = add(tris)
meta['vpart'] = add(vpart)
meta['skinIndex'] = add(SI)
meta['skinWeight'] = add(SW)
for v in variants:
    q = np.round((v['P'] - lo) / 0.00025).astype(np.int64)
    assert q.max() < 65535 and q.min() >= 0
    meta['variants'].append({'sex': v['sex'], 'build': v['build'], 'height': round(float(v['height']), 4),
        'pos': add(q.astype(np.uint16)),
        'heads': np.round(v['heads'], 5).tolist(), 'tails': np.round(v['tails'], 5).tolist()})
raw = bytes(blob)
gz = gzip.compress(raw, 9)
open(f'{OUT}/human.bin.gz', 'wb').write(gz)
json.dump(meta, open(f'{OUT}/human.json', 'w'))
print('raw', len(raw), 'gz', len(gz), 'b64', len(base64.b64encode(gz)))
