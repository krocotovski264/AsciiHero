import json, struct, gzip, numpy as np
from PIL import Image, ImageDraw, ImageFont
# Exports the web build's baked data for Unity (Unity/Assets/Resources: human.bytes, mm.bytes, glyphs atlas).
# Inputs: game/build/human.json + human.bin.gz (from makehuman/bake.py) and game/src/data_mm.js (from mocap/mm_db.py).
import os
HERE=os.path.dirname(os.path.abspath(__file__)); GAME=os.path.join(HERE,'..','game')
OUT=os.path.join(HERE,'..','..','Unity','Assets','Resources')+'/'
def wstr(f, s): b=s.encode('utf-8'); f.write(struct.pack('<B', len(b))); f.write(b)
# ---------- human ----------
M=json.load(open(os.path.join(GAME,'build','human.json'))); raw=gzip.decompress(open(os.path.join(GAME,'build','human.bin.gz'),'rb').read())
def sl(r): return raw[r[0]:r[0]+r[1]]
with open(OUT+'human.bytes','wb') as f:
    f.write(b'AHH1'); f.write(struct.pack('<iiif', M['nverts'], M['ntris'], len(M['bones']), M['q'])); f.write(struct.pack('<3f', *M['lo']))
    for b,p in zip(M['bones'], M['parents']): wstr(f,b); f.write(struct.pack('<i',p))
    f.write(sl(M['tris'])); f.write(sl(M['vpart'])); f.write(sl(M['skinIndex'])); f.write(sl(M['skinWeight']))
    f.write(struct.pack('<i', len(M['variants'])))
    for v in M['variants']:
        f.write(struct.pack('<Bif', ord(v['sex']), v['build'], v['height'])); f.write(sl(v['pos']))
        f.write(np.array(v['heads'],dtype='<f4').tobytes()); f.write(np.array(v['tails'],dtype='<f4').tobytes())
# ---------- motion matching ----------
import re, base64
js=open(os.path.join(GAME,'src','data_mm.js')).read()
meta=json.loads(re.search(r'MM_META=(\{.*?\});\n', js, re.S).group(1)); b64=re.search(r'MM_B64="([^"]+)"', js).group(1)
mraw=gzip.decompress(base64.b64decode(b64))
def ms(r): return mraw[r[0]:r[0]+r[1]]
with open(OUT+'mm.bytes','wb') as f:
    f.write(b'AHM1'); f.write(struct.pack('<iiii', meta['n'], len(meta['bones']), meta['nf'], meta['fps']))
    for b in meta['bones']: wstr(f,b)
    f.write(np.array(meta['mean'],dtype='<f4').tobytes()); f.write(np.array(meta['scale'],dtype='<f4').tobytes())
    f.write(struct.pack('<i', len(meta['ranges'])))
    for a,b,_ in meta['ranges']: f.write(struct.pack('<ii',a,b))
    for k in ('q','ro','rm','con','f','valid','idleB'): f.write(ms(meta[k]))
# ---------- glyph atlas (same ramps as the web version) ----------
RAMPS = [" .,:;-=+icoaex%@"," .`',;~^/\\|()%&$"," .,:;-=+*xX#%&@@"," .,:;!|ilI%&#$@@"," .,:co8oO0Q8@&#@"," .-=+*^#H%MWM@@@",
 " .-=|+#H/\\%@@@@@"," .:|!lI1][}{#%&@"," .:*o$O&%#@@@@@@"," .*+oO0@@@@@@@@@"," ......:::::::::"," ..,,::;;-~=+x%#","|-/\\_^<>        "," .'`,:;~^*+sS$&@"]
gw,gh=24,40; img=Image.new('L',(gw*16,gh*len(RAMPS)),0); d=ImageDraw.Draw(img)
font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf',30)
for ri,r in enumerate(RAMPS):
    for i,ch in enumerate(r[:16]):
        bb=d.textbbox((0,0),ch,font=font); w=bb[2]-bb[0]; h=bb[3]-bb[1]
        d.text((i*gw+(gw-w)/2-bb[0], ri*gh+(gh-h)/2-bb[1]), ch, fill=255, font=font)
img.convert('RGB').save(OUT+'glyphs.bytes', format='PNG')   # PNG bytes, loaded with Texture2D.LoadImage
import os; print({k: os.path.getsize(OUT+k) for k in os.listdir(OUT)})
