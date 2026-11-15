"""Night sky map for the water's reflection (as RPGs do it: the reflected view ray looks the sky up in a map).
Upper hemisphere, equirectangular: u = azimuth (atan2(x, z) / 2pi + 0.5), v = elevation / 90 degrees (0 = horizon).
Contents: a deep night gradient, the Milky Way with dust lanes, a few thousand stars of different brightness and
colour, the moon (maria, craters with bright rims, darker limb) with a soft halo, at the direction the game's moon
light comes from. Written as a PNG (linear values, read as display colours by the shader)."""
import numpy as np, sys
from PIL import Image
W, H = 2048, 512
rng = np.random.default_rng(20261115)
u = (np.arange(W) + 0.5) / W; v = (np.arange(H) + 0.5) / H
az = (u - 0.5) * 2 * np.pi; el = v * np.pi / 2
AZ, EL = np.meshgrid(az, el)                     # rows = elevation from the horizon (row 0) up to the zenith
D = np.stack([np.cos(EL) * np.sin(AZ), np.sin(EL), np.cos(EL) * np.cos(AZ)], -1)
def noise(shape_freq, octaves=5, seed=1):
    """fractal value noise on the sphere, sampled through direction"""
    r = np.random.default_rng(seed); out = np.zeros(D.shape[:2]); amp = 1; tot = 0
    for o in range(octaves):
        f = shape_freq * 2 ** o; G = r.random((int(f * 2) + 3,) * 3)
        P = (D + 1) * f; i = np.floor(P).astype(int); t = P - i; t = t * t * (3 - 2 * t)
        def g(dx, dy, dz): return G[i[..., 0] + dx, i[..., 1] + dy, i[..., 2] + dz]
        x00 = g(0,0,0)*(1-t[...,0]) + g(1,0,0)*t[...,0]; x10 = g(0,1,0)*(1-t[...,0]) + g(1,1,0)*t[...,0]
        x01 = g(0,0,1)*(1-t[...,0]) + g(1,0,1)*t[...,0]; x11 = g(0,1,1)*(1-t[...,0]) + g(1,1,1)*t[...,0]
        y0 = x00*(1-t[...,1]) + x10*t[...,1]; y1 = x01*(1-t[...,1]) + x11*t[...,1]
        out += amp * (y0*(1-t[...,2]) + y1*t[...,2]); tot += amp; amp *= 0.5
    return out / tot
# sky: darker at the zenith, a faint glow near the horizon
img = np.zeros((H, W, 3))
hz = np.exp(-EL / 0.35)[..., None]
img += np.array([0.010, 0.014, 0.034]) * (1 - hz) + np.array([0.030, 0.040, 0.075]) * hz
# the Milky Way: a band along a tilted great circle, cloudy, with dark lanes
pole = np.array([0.55, 0.45, 0.70]); pole /= np.linalg.norm(pole)
band = np.exp(-(D @ pole) ** 2 / 0.035)
cloud = noise(3.0, 6, 3); lanes = noise(7.0, 4, 9)
mw = band * np.clip(cloud * 1.6 - 0.35, 0, 1) * (1 - 0.75 * np.clip((lanes - 0.45) * 4, 0, 1) * band)
img += mw[..., None] * np.array([0.10, 0.10, 0.13])
# stars: more of them in the band; magnitude by a power law; colour from blue-white to orange
N = 9000
dirs = rng.normal(size=(N * 3, 3)); dirs /= np.linalg.norm(dirs, axis=1, keepdims=True); dirs = dirs[dirs[:, 1] > 0.02]
pb = 0.35 + 0.65 * np.exp(-(dirs @ pole) ** 2 / 0.06); dirs = dirs[rng.random(len(dirs)) < pb][:N]
mag = rng.pareto(2.2, len(dirs)) + 1; b = np.clip(0.12 * mag, 0, 1.6)
temp = rng.random(len(dirs)); cols = np.where(temp[:, None] < 0.6, [0.82, 0.88, 1.0], np.where(temp[:, None] < 0.88, [1.0, 0.97, 0.9], [1.0, 0.8, 0.6]))
for d, bb, c in zip(dirs, b, cols):
    e = np.arcsin(d[1]); a = np.arctan2(d[0], d[2])
    x = (a / (2 * np.pi) + 0.5) * W; y = e / (np.pi / 2) * H
    sx = max(0.7, 1.0 / max(np.cos(e), 0.15)) * (0.7 + 0.5 * min(bb, 1)); sy = 0.7 + 0.5 * min(bb, 1)    # (wider near the zenith: the map stretches there)
    x0, x1 = int(x - 3 * sx), int(x + 3 * sx) + 1; y0, y1 = max(0, int(y - 3 * sy)), min(H, int(y + 3 * sy) + 1)
    for yy in range(y0, y1):
        for xx in range(x0, x1):
            g = np.exp(-(((xx + 0.5 - x) / sx) ** 2 + ((yy + 0.5 - y) / sy) ** 2))
            img[yy, xx % W] += c * bb * g
# the moon, where its light comes from in the game
M = np.array([-0.12, 0.788, -0.604]); M /= np.linalg.norm(M)
ang = np.arccos(np.clip(D @ M, -1, 1)); R0 = 0.062
img += np.array([0.42, 0.44, 0.52])[None, None] * (np.exp(-ang / 0.09) * 0.35 + np.exp(-ang / 0.25) * 0.12)[..., None]      # halo
inside = ang < R0
# disc coordinates: offset from the centre, in units of the radius
up = np.array([0, 1, 0]) - M * M[1]; up /= np.linalg.norm(up); rt = np.cross(up, M)
px = (D @ rt) / np.sin(R0); py = (D @ up) / np.sin(R0); rr = np.sqrt(px ** 2 + py ** 2)
mare = noise(9.0, 5, 21)                              # the dark "seas"
lum = 0.95 - 0.45 * np.clip((mare - 0.45) * 3.0, 0, 1)
for k in range(38):                                    # craters: a darker floor, a bright rim
    cx, cy = rng.uniform(-0.9, 0.9, 2); cr = rng.uniform(0.04, 0.17)
    if cx * cx + cy * cy > 0.85: continue
    dd = np.sqrt((px - cx) ** 2 + (py - cy) ** 2) / cr
    lum = lum - 0.07 * np.exp(-dd ** 4) + 0.07 * np.exp(-((dd - 1) / 0.25) ** 2)
lum = lum * (0.55 + 0.45 * np.sqrt(np.clip(1 - rr ** 2, 0, 1)))       # the limb is darker
edge = np.clip((R0 - ang) / 0.004, 0, 1)
moonc = np.clip(lum, 0, 1.2)[..., None] * np.array([1.0, 0.97, 0.86]) * 1.25
img = np.where(inside[..., None], img * (1 - edge[..., None]) + moonc * edge[..., None], img)
out = np.clip(img, 0, 1)
Image.fromarray((out * 255 + 0.5).astype(np.uint8)).save(sys.argv[1] if len(sys.argv) > 1 else '/tmp/skymap.png', optimize=True)
print('ok', out.max(), out.mean())
