"""combine C_DS_NZ atlas0 (512x256 opaque, main parts) and atlas1 (512x512 alpha-masked trims) into ONE 512x1024 RGBA atlas -> DXT5 with mips"""
import struct, sys, numpy as np
from PIL import Image
import os
a0 = Image.open('tex/atlas1_gems.png' if os.path.exists('tex/atlas1_gems.png') else 'tex/atlas1.png').convert('RGBA')   # gems version = black around the hip gems cut out (transform/gem_mask.py)
a1 = Image.open('tex/atlas2.png').convert('RGBA')
# layout (512x1024): atlas0 rows 0..255 | 16px gutter replicating atlas0's last row | atlas1 rows 272..783 | rest replicates atlas1's last row.
# The gutters stop bilinear/mip filtering from bleeding the orange trim atlas into the bottom edge of the main atlas (her fingers sit there).
atlas = Image.new('RGBA', (512, 1024), (0, 0, 0, 255)); atlas.paste(a0, (0, 0))
last0 = a0.crop((0, 255, 512, 256)); [atlas.paste(last0, (0, 256 + k)) for k in range(16)]
atlas.paste(a1, (0, 272)); last1 = a1.crop((0, 511, 512, 512)); [atlas.paste(last1, (0, 784 + k)) for k in range(240)]
# areola decal tile: 96x96 at atlas (16,800) - hard-edged near-black disc (r=30 px) with Venice detail, lighter nipple centre; everything outside the disc is alpha 0
import numpy as np
yy, xx = np.mgrid[0:96, 0:96]; rr = np.sqrt((xx - 47.5) ** 2 + (yy - 47.5) ** 2) / 30.0
det = np.ones((96, 96))
if os.path.exists('tex/areola_detail.png'):
    sw = np.asarray(Image.open('tex/areola_detail.png').convert('L').resize((96, 96), Image.LANCZOS)).astype(float); det = np.clip(0.55 + 0.45 * sw / max(sw.mean(), 1e-3), 0.4, 1.6)
rgb = (np.array([10, 7, 7], float)[None, None, :] * det[..., None] * (1 + 1.6 * np.clip(1 - rr / 0.30, 0, 1)[..., None]))
tile = np.dstack([np.clip(rgb, 0, 255), np.where(rr <= 1.0, 255, 0)]).astype(np.uint8)
atlas.paste(Image.fromarray(tile, 'RGBA'), (16, 800))
atlas.save('tex/cirith_atlas.png')

def enc_color(rgb):                                        # rgb (n,16,3) float -> (n,) uint64 8-byte DXT1 colour block
    lo = rgb.min(1); hi = rgb.max(1)
    q = lambda c: ((np.clip(np.round(c[:, 0] / 255 * 31), 0, 31).astype(np.uint32) << 11) | (np.clip(np.round(c[:, 1] / 255 * 63), 0, 63).astype(np.uint32) << 5) | np.clip(np.round(c[:, 2] / 255 * 31), 0, 31).astype(np.uint32))
    d565 = lambda v: np.stack([((v >> 11) & 31) * 255 / 31, ((v >> 5) & 63) * 255 / 63, (v & 31) * 255 / 31], 1)
    c0 = q(hi); c1 = q(lo); sw = c0 < c1; c0, c1 = np.where(sw, c1, c0), np.where(sw, c0, c1); same = c0 == c1
    p0 = d565(c0); p1 = d565(c1); pal = np.stack([p0, p1, (2 * p0 + p1) / 3, (p0 + 2 * p1) / 3], 1)
    idx = (((rgb[:, :, None, :] - pal[:, None]) ** 2).sum(-1)).argmin(2).astype(np.uint32); idx[same] = 0
    bits = np.zeros(len(rgb), np.uint32)
    for k in range(16): bits |= idx[:, k] << (2 * k)
    return np.stack([c0 | (c1 << 16), bits], 1).astype('<u4')            # (n,2) u32 = 8 bytes

def enc_alpha(al):                                         # al (n,16) float -> (n,8) uint8 BC4-style alpha block
    a0 = al.max(1); a1 = al.min(1); a0i = np.round(a0).astype(np.int32); a1i = np.round(a1).astype(np.int32)
    flat = a0i == a1i
    # mode with a0 > a1: 8 interpolated values
    pal = np.stack([a0i + (a1i - a0i) * k / 7.0 for k in range(8)], 1); pal[:, 0] = a0i; pal[:, 1] = a1i
    pal = np.stack([a0i, a1i] + [((7 - k) * a0i + k * a1i) / 7.0 for k in range(1, 7)], 1)
    idx = np.abs(al[:, :, None] - pal[:, None]).argmin(2).astype(np.uint64); idx[flat] = 0
    bits = np.zeros(len(al), np.uint64)
    for k in range(16): bits |= idx[:, k] << np.uint64(3 * k)
    out = np.zeros((len(al), 8), np.uint8); out[:, 0] = a0i; out[:, 1] = a1i
    for b in range(6): out[:, 2 + b] = ((bits >> np.uint64(8 * b)) & np.uint64(255)).astype(np.uint8)
    return out

def enc_dxt5(img):
    a = np.asarray(img.convert('RGBA'), np.float32); h, w, _ = a.shape; ph, pw = (h + 3) // 4 * 4, (w + 3) // 4 * 4
    if (ph, pw) != (h, w): a = np.pad(a, ((0, ph - h), (0, pw - w), (0, 0)), mode='edge')
    b = a.reshape(ph // 4, 4, pw // 4, 4, 4).transpose(0, 2, 1, 3, 4).reshape(-1, 16, 4)
    col = enc_color(b[:, :, :3]); al = enc_alpha(b[:, :, 3])
    return np.concatenate([al, col.view(np.uint8).reshape(len(b), 8)], 1).tobytes()           # 16 bytes per block: alpha then colour

mips = []; cur = atlas
while True:
    mips.append(enc_dxt5(cur))
    if cur.size == (1, 1): break
    cur = cur.resize((max(cur.size[0] // 2, 1), max(cur.size[1] // 2, 1)), Image.BOX)
with open('tex/cirith_main.dxt5', 'wb') as f:
    f.write(struct.pack('<III', 512, 1024, len(mips)))
    for m in mips: f.write(struct.pack('<I', len(m)) + m)
print('atlas 512x1024', len(mips), 'mips', sum(len(m) for m in mips), 'bytes')
