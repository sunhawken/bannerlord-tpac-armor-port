import numpy as np
from collections import defaultdict
def fix(im, pos, uv, idx, W=512, H=256, thr=22, rad=5):
    """UV-seam edges (same 3D edge, different UVs) where one side samples dark atlas border and the other side skin grey: grow the skin colour over the dark side's texels (feathered disc)."""
    px = np.stack([np.clip(uv[:, 0], 0, 1) * (W - 1), np.clip(uv[:, 1], 0, 1) * (H - 1)], 1)
    key = lambda i: tuple(np.round(pos[i], 3)); E = defaultdict(list)
    for t in idx:
        for a, b in ((0, 1), (1, 2), (2, 0)):
            i, j = t[a], t[b]; E[tuple(sorted((key(i), key(j))))].append((i, j))
    def samp(p, r=2):
        x, y = int(round(p[0])), int(round(p[1])); return im[max(y - r, 0):y + r + 1, max(x - r, 0):x + r + 1, :3].reshape(-1, 3).astype(float).mean(0)
    yy, xx = np.mgrid[0:H, 0:W]; fixed = 0; out = im.astype(float).copy()
    for k, l in E.items():
        if len(l) < 2: continue
        (i1, j1), (i2, j2) = l[0], l[1]
        if key(i1) != key(i2): i2, j2 = j2, i2
        if key(i1) != key(i2) or key(j1) != key(j2): continue
        for a, b in ((i1, i2), (j1, j2)):
            if np.linalg.norm(px[a] - px[b]) < 1.5: continue
            ca, cb = samp(px[a]), samp(px[b]); la, lb = ca.mean(), cb.mean()
            for dark, bright, cd, cbright, ld, lbr in ((a, b, ca, cb, la, lb), (b, a, cb, ca, lb, la)):
                if ld < 40 and 45 < lbr < 140 and cbright.max() - cbright.min() < 22 and lbr - ld > thr:
                    d = np.hypot(xx - px[dark][0], yy - px[dark][1]); w = np.clip((rad + 1 - d) / 2.0, 0, 1)
                    darkpx = (im[..., :3].astype(float).mean(2) < 45) & (im[..., :3].max(2).astype(int) - im[..., :3].min(2).astype(int) < 22)    # only dark GREY texels, never gold/gems
                    w = w * darkpx
                    for c in range(3): out[..., c] = out[..., c] * (1 - w) + cbright[c] * w
                    fixed += 1
    return np.clip(out, 0, 255).astype(np.uint8), fixed
