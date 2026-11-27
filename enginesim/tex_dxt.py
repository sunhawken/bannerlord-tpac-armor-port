"""PNG -> DXT1 with full mip chain, as one binary: u32 w, u32 h, u32 mips, then for each mip: u32 size + blocks."""
import struct, sys, numpy as np
from PIL import Image

def enc_dxt1(img):
    a = np.asarray(img.convert('RGB'), np.float32); h, w, _ = a.shape
    pw, ph = (w + 3) // 4 * 4, (h + 3) // 4 * 4
    if (pw, ph) != (w, h): a = np.pad(a, ((0, ph - h), (0, pw - w), (0, 0)), mode='edge')
    b = a.reshape(ph // 4, 4, pw // 4, 4, 3).transpose(0, 2, 1, 3, 4).reshape(-1, 16, 3)
    lo = b.min(1); hi = b.max(1)
    def q565(c): return ((np.clip(np.round(c[:, 0] / 255 * 31), 0, 31).astype(np.uint32) << 11) | (np.clip(np.round(c[:, 1] / 255 * 63), 0, 63).astype(np.uint32) << 5) | np.clip(np.round(c[:, 2] / 255 * 31), 0, 31).astype(np.uint32))
    def d565(v): return np.stack([((v >> 11) & 31) * 255 / 31, ((v >> 5) & 63) * 255 / 63, (v & 31) * 255 / 31], 1)
    c0 = q565(hi); c1 = q565(lo)
    sw = c0 < c1; c0, c1 = np.where(sw, c1, c0), np.where(sw, c0, c1)        # c0 >= c1 -> 4-colour mode
    same = c0 == c1
    p0 = d565(c0); p1 = d565(c1)
    pal = np.stack([p0, p1, (2 * p0 + p1) / 3, (p0 + 2 * p1) / 3], 1)           # (n,4,3)
    d = ((b[:, :, None, :] - pal[:, None, :, :]) ** 2).sum(-1)                 # (n,16,4)
    idx = d.argmin(2).astype(np.uint32); idx[same] = 0
    bits = np.zeros(len(b), np.uint32)
    for k in range(16): bits |= idx[:, k] << (2 * k)
    out = np.zeros((len(b), 3), np.uint32); out[:, 0] = c0 | (c1 << 16); out[:, 1] = bits
    return np.stack([c0 | (c1 << 16), bits], 1).astype('<u4').tobytes()

def main(src, dst):
    img = Image.open(src).convert('RGB'); w, h = img.size
    mips = []; cur = img
    while True:
        mips.append(enc_dxt1(cur))
        if cur.size == (1, 1): break
        cur = cur.resize((max(cur.size[0] // 2, 1), max(cur.size[1] // 2, 1)), Image.LANCZOS)
    with open(dst, 'wb') as f:
        f.write(struct.pack('<III', w, h, len(mips)))
        for m in mips: f.write(struct.pack('<I', len(m)) + m)
    print(src, w, h, len(mips), 'mips', sum(len(m) for m in mips), 'bytes')

if __name__ == '__main__': main(sys.argv[1], sys.argv[2])
