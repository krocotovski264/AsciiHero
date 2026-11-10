#!/usr/bin/env python3
"""Hand-assembled classes.dex: MainActivity extends Activity, shows a fullscreen WebView
loading file:///android_asset/index.html."""
import struct, zlib, hashlib, sys

CLS = 'Lcom/asciihero/game/MainActivity;'
ACT = 'Landroid/app/Activity;'
URL = 'file:///android_asset/index.html'

# method refs: (class, name, return, params)
METHODS = [
    (ACT, '<init>', 'V', []),
    (ACT, 'onCreate', 'V', ['Landroid/os/Bundle;']),
    (ACT, 'requestWindowFeature', 'Z', ['I']),
    (ACT, 'getWindow', 'Landroid/view/Window;', []),
    (ACT, 'setContentView', 'V', ['Landroid/view/View;']),
    ('Landroid/view/Window;', 'setFlags', 'V', ['I', 'I']),
    ('Landroid/webkit/WebView;', '<init>', 'V', ['Landroid/content/Context;']),
    ('Landroid/webkit/WebView;', 'getSettings', 'Landroid/webkit/WebSettings;', []),
    ('Landroid/webkit/WebView;', 'setBackgroundColor', 'V', ['I']),
    ('Landroid/webkit/WebView;', 'loadUrl', 'V', ['Ljava/lang/String;']),
    ('Landroid/webkit/WebSettings;', 'setJavaScriptEnabled', 'V', ['Z']),
    ('Landroid/webkit/WebSettings;', 'setDomStorageEnabled', 'V', ['Z']),
    ('Landroid/webkit/WebSettings;', 'setAllowFileAccess', 'V', ['Z']),
    ('Landroid/webkit/WebSettings;', 'setAllowFileAccessFromFileURLs', 'V', ['Z']),
    ('Landroid/webkit/WebSettings;', 'setAllowUniversalAccessFromFileURLs', 'V', ['Z']),
    ('Landroid/webkit/WebSettings;', 'setMediaPlaybackRequiresUserGesture', 'V', ['Z']),
    (CLS, '<init>', 'V', []),
    (CLS, 'onCreate', 'V', ['Landroid/os/Bundle;']),
]

def shorty_ch(t): return 'L' if t[0] in 'L[' else t
def shorty(ret, params): return shorty_ch(ret) + ''.join(shorty_ch(p) for p in params)

# ---- collect strings / types / protos ----
types = set([CLS, ACT])
strings = set([URL])
protos = set()
for c, n, r, ps in METHODS:
    types.add(c); types.add(r); types.update(ps); strings.add(n)
    protos.add((r, tuple(ps)))
for (r, ps) in protos: strings.add(shorty(r, list(ps)))
strings.update(types)

def mutf8_key(s): return [ord(ch) for ch in s]
strings = sorted(strings, key=mutf8_key)
S = {s: i for i, s in enumerate(strings)}
types = sorted(types, key=lambda t: S[t])
Tt = {t: i for i, t in enumerate(types)}
protos = sorted(protos, key=lambda p: (Tt[p[0]], [Tt[x] for x in p[1]]))
P = {p: i for i, p in enumerate(protos)}
mrefs = sorted(METHODS, key=lambda m: (Tt[m[0]], S[m[1]], P[(m[2], tuple(m[3]))]))
Mi = {(m[0], m[1], m[2], tuple(m[3])): i for i, m in enumerate(mrefs)}
def mid(c, n, r, ps): return Mi[(c, n, r, tuple(ps))]

def uleb(v):
    out = bytearray()
    while True:
        b = v & 0x7f; v >>= 7
        if v: out.append(b | 0x80)
        else: out.append(b); return bytes(out)

# ---- bytecode ----
def u16(*a): return b''.join(struct.pack('<H', x & 0xffff) for x in a)

def invoke(op, mref, regs):
    n = len(regs); r = list(regs) + [0]*(5-n)
    b0 = op | ((n << 4 | r[4]) << 8)
    b2 = r[0] | (r[1] << 4) | (r[2] << 8) | (r[3] << 12)
    return u16(b0, mref, b2)
def const4(a, lit): return u16(0x12 | ((((lit & 0xf) << 4) | a) << 8))
def const16(a, lit): return u16(0x13 | (a << 8), lit)
def const32(a, lit): return u16(0x14 | (a << 8), lit & 0xffff, (lit >> 16) & 0xffff)
def const_string(a, sidx): return u16(0x1a | (a << 8), sidx)
def new_instance(a, tidx): return u16(0x22 | (a << 8), tidx)
def move_result_obj(a): return u16(0x0c | (a << 8))
RETV = u16(0x0e)
INV_V, INV_S, INV_D = 0x6e, 0x6f, 0x70

# <init>: registers=1 (p0=v0)
init_code = invoke(INV_D, mid(ACT, '<init>', 'V', []), [0]) + RETV
# onCreate: locals v0..v3, p0=v4 (this), p1=v5 (bundle)
p0, p1 = 4, 5
Zp = ['Z']
ws = 'Landroid/webkit/WebSettings;'
wv = 'Landroid/webkit/WebView;'
oc = b''.join([
    invoke(INV_S, mid(ACT, 'onCreate', 'V', ['Landroid/os/Bundle;']), [p0, p1]),
    const4(0, 1),
    invoke(INV_V, mid(ACT, 'requestWindowFeature', 'Z', ['I']), [p0, 0]),
    invoke(INV_V, mid(ACT, 'getWindow', 'Landroid/view/Window;', []), [p0]),
    move_result_obj(1),
    const16(0, 0x480),
    invoke(INV_V, mid('Landroid/view/Window;', 'setFlags', 'V', ['I', 'I']), [1, 0, 0]),
    new_instance(1, Tt[wv]),
    invoke(INV_D, mid(wv, '<init>', 'V', ['Landroid/content/Context;']), [1, p0]),
    invoke(INV_V, mid(wv, 'getSettings', ws, []), [1]),
    move_result_obj(2),
    const4(0, 1),
    invoke(INV_V, mid(ws, 'setJavaScriptEnabled', 'V', Zp), [2, 0]),
    invoke(INV_V, mid(ws, 'setDomStorageEnabled', 'V', Zp), [2, 0]),
    invoke(INV_V, mid(ws, 'setAllowFileAccess', 'V', Zp), [2, 0]),
    invoke(INV_V, mid(ws, 'setAllowFileAccessFromFileURLs', 'V', Zp), [2, 0]),
    invoke(INV_V, mid(ws, 'setAllowUniversalAccessFromFileURLs', 'V', Zp), [2, 0]),
    const4(3, 0),
    invoke(INV_V, mid(ws, 'setMediaPlaybackRequiresUserGesture', 'V', Zp), [2, 3]),
    const32(0, 0xff06070b),
    invoke(INV_V, mid(wv, 'setBackgroundColor', 'V', ['I']), [1, 0]),
    invoke(INV_V, mid(ACT, 'setContentView', 'V', ['Landroid/view/View;']), [p0, 1]),
    const_string(3, S[URL]),
    invoke(INV_V, mid(wv, 'loadUrl', 'V', ['Ljava/lang/String;']), [1, 3]),
    RETV,
])

def code_item(regs, ins, outs, insns):
    assert len(insns) % 2 == 0
    return struct.pack('<HHHHII', regs, ins, outs, 0, 0, len(insns)//2) + insns

# ---- layout ----
HDR = 0x70
n_str, n_type, n_proto, n_meth = len(strings), len(types), len(protos), len(mrefs)
off = HDR
str_ids_off = off; off += 4*n_str
type_ids_off = off; off += 4*n_type
proto_ids_off = off; off += 12*n_proto
method_ids_off = off; off += 8*n_meth
class_defs_off = off; off += 32
data_off = off

data = bytearray()
def align(n):
    while (data_off + len(data)) % n: data.append(0)
def here(): return data_off + len(data)

# code items (4-aligned)
align(4); init_off = here(); data += code_item(1, 1, 1, init_code)
align(4); oc_off = here(); data += code_item(6, 2, 3, oc)
# type lists (4-aligned)
tl_offs = {}
param_lists = sorted(set(p[1] for p in protos if p[1]), key=lambda l: [Tt[x] for x in l])
for pl in param_lists:
    align(4); tl_offs[pl] = here()
    data += struct.pack('<I', len(pl)) + b''.join(struct.pack('<H', Tt[x]) for x in pl)
# string data
str_data_offs = []
str_data_start = here()
for s in strings:
    str_data_offs.append(here())
    data += uleb(len(s)) + s.encode('utf-8') + b'\0'
# class data
cd_off = here()
direct = [(mid(CLS, '<init>', 'V', []), 0x10001, init_off)]     # public constructor
virtual = [(mid(CLS, 'onCreate', 'V', ['Landroid/os/Bundle;']), 0x0004, oc_off)]  # protected
cd = uleb(0) + uleb(0) + uleb(len(direct)) + uleb(len(virtual))
prev = 0
for m, fl, co in direct: cd += uleb(m - prev) + uleb(fl) + uleb(co); prev = m
prev = 0
for m, fl, co in virtual: cd += uleb(m - prev) + uleb(fl) + uleb(co); prev = m
data += cd
# map list
align(4); map_off = here()
items = [(0x0000, 1, 0), (0x0001, n_str, str_ids_off), (0x0002, n_type, type_ids_off),
         (0x0003, n_proto, proto_ids_off), (0x0005, n_meth, method_ids_off), (0x0006, 1, class_defs_off),
         (0x2001, 2, init_off), (0x1001, len(param_lists), tl_offs[param_lists[0]]),
         (0x2002, n_str, str_data_start), (0x2000, 1, cd_off), (0x1000, 1, map_off)]
items.sort(key=lambda x: x[2])
data += struct.pack('<I', len(items)) + b''.join(struct.pack('<HHII', t, 0, n, o) for t, n, o in items)

file_size = data_off + len(data)
body = bytearray()
for o in str_data_offs: body += struct.pack('<I', o)
for t in types: body += struct.pack('<I', S[t])
for (r, ps) in protos:
    body += struct.pack('<III', S[shorty(r, list(ps))], Tt[r], tl_offs[ps] if ps else 0)
for (c, n, r, ps) in mrefs:
    body += struct.pack('<HHI', Tt[c], P[(r, tuple(ps))], S[n])
body += struct.pack('<IIIIIIII', Tt[CLS], 0x1, Tt[ACT], 0, 0xffffffff, 0, cd_off, 0)
assert HDR + len(body) == data_off
out = bytearray(HDR) + body + data
hdr = struct.pack('<8sI20sIIIIIIIIIIIIIIIIIIII',
    b'dex\n035\0', 0, b'\0'*20, file_size, HDR, 0x12345678, 0, 0, map_off,
    n_str, str_ids_off, n_type, type_ids_off, n_proto, proto_ids_off, 0, 0,
    n_meth, method_ids_off, 1, class_defs_off, len(data), data_off)
out[0:HDR] = hdr
out[12:32] = hashlib.sha1(bytes(out[32:])).digest()
out[8:12] = struct.pack('<I', zlib.adler32(bytes(out[12:])) & 0xffffffff)
open(sys.argv[1], 'wb').write(out)
print('dex bytes', len(out))
