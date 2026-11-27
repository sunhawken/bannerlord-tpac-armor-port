"""cloth_score.py <shots_dir> [lum_thr] [x0 y0 x1 y1]: centroid + pixel count of the bright, unsaturated cloth/hair pixels (white ponytail / pale dress) inside a window around her, per frame.
After she stops dead (frame ~11) a RIGID mesh keeps a constant centroid; a simulated one keeps moving and then settles."""
import sys, numpy as np
from PIL import Image
d = sys.argv[1]; thr = float(sys.argv[2]) if len(sys.argv) > 2 else 200
f = [float(x) for x in sys.argv[3:7]] if len(sys.argv) > 6 else [0.40, 0.05, 0.60, 0.7]
out = []
for i in range(45):
    try: im = np.asarray(Image.open(f'{d}/mot_{i:02d}.png').convert('RGB')).astype(int)
    except Exception: break
    H, W, _ = im.shape; c = im[int(H * f[1]):int(H * f[3]), int(W * f[0]):int(W * f[2])]
    m = (c.mean(2) > thr) & ((c.max(2) - c.min(2)) < 35); ys, xs = np.where(m)
    out.append((m.sum(), xs.mean() if len(xs) else -1, ys.mean() if len(ys) else -1))
print('frame: n cx cy'); [print(f'{i:2d}: {o[0]:5d} {o[1]:6.1f} {o[2]:6.1f}') for i, o in enumerate(out) if i % 2 == 0 and i >= 8 and i <= 30]
cx = np.array([o[1] for o in out[14:]]); cy = np.array([o[2] for o in out[14:]]); print('after-stop centroid std (frames 14+): cx %.2f cy %.2f' % (cx.std(), cy.std()))
