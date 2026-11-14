"""Writes the attack tables for the web prototype (atk_gen.js) and Unity (atk_gen.cs) from atk.json."""
import json, numpy as np
A = json.load(open('atk.json')); by = {x.get('name'): x for x in A if 'name' in x}
# type, clip, mirrored, reach (m)
TYPES = [(7, 'spearThrust', 0, 2.3), (8, 'spearHigh', 0, 2.3), (9, 'spearLunge', 0, 2.3), (10, 'spearSwing', 0, 2.3), (11, 'spearSwing', 1, 2.3), (12, 'spearOverhead', 0, 2.3),
         (13, 'tomaChop', 0, 1.6), (14, 'tomaChop2', 1, 1.6), (15, 'tomaBack', 0, 1.6), (16, 'tomaBack', 1, 1.6), (19, 'tomaChop2', 0, 1.6),
         (17, 'shieldStraight', 0, 1.4), (18, 'shieldBackhand', 1, 1.4)]
js, cs = [], []
for t, nm, mir, reach in TYPES:
    c = by[nm]; n = c['n']; pk = c['peak']
    f1 = max(1.0, pk - 4.5); f2 = min(n - 1.5, pk + 3.0)
    tw = min(0.4, max(0.18, f1 / 30 / 1.7)); ts = (f2 - f1) / 30 / 1.25; tr = min(0.35, max(0.15, (n - 1 - f2) / 30 / 1.8))
    kt = [0, round(tw, 3), round(tw + ts, 3), round(tw + ts + tr, 3)]; kf = [0, f1, f2, n - 1]
    cut = [round(tw - 0.02, 3), round(tw + ts + 0.04, 3)]
    bash = nm.startswith('shield'); grip2 = nm in ('spearThrust', 'spearHigh', 'spearLunge'); dual = nm.startswith('toma')
    bl = np.array(c['blade']); bll = np.array(c['bladeL'])
    if grip2: bl[:, 1] = np.clip(bl[:, 1] + 0.2, -0.1, 0.4); bl /= np.linalg.norm(bl, axis=1)[:, None]      # spear tip carried a little up
    bl = np.round(bl, 3).tolist(); bll = np.round(bll, 3).tolist()
    flags = (", mirror: true" if mir else "") + (", bash: true" if bash else "") + (", grip2: true" if grip2 else "") + (", dual: true" if dual else "")
    js.append(f"ATTACKS[{t}] = {{ name: '{nm}', a: {c['a']}, dur: {kt[-1]}, knots: {json.dumps([[a, b] for a, b in zip(kt, kf)])}, cut: {json.dumps(cut)}, reach: {reach}{flags}, blade: {json.dumps(bl, separators=(',', ':'))}"
              + (f", bladeL: {json.dumps(bll, separators=(',', ':'))}" if dual else "") + " };")
    f3 = lambda arr: ', '.join('%gf, %gf, %gf' % (-x, y, z) for x, y, z in arr)
    b = lambda v: 'true' if v else 'false'
    cs.append(f"            A[{t}] = new AtkClip {{ a = {c['a']}, dur = {kt[-1]}f, cut0 = {cut[0]}f, cut1 = {cut[1]}f, reach = {reach}f, mirror = {b(mir)}, bash = {b(bash)}, grip2 = {b(grip2)}, dual = {b(dual)}, "
              f"kt = new[] {{ {', '.join('%gf' % x for x in kt)} }}, kf = new[] {{ {', '.join('%gf' % x for x in kf)} }},\n                blade = new[] {{ {f3(bl)} }}"
              + (f",\n                bladeL = new[] {{ {f3(bll)} }}" if dual else "") + " };")
open('atk_gen.js', 'w').write('\n'.join(js) + '\n'); open('atk_gen.cs', 'w').write('\n'.join(cs) + '\n')
print('ok', len(js))
