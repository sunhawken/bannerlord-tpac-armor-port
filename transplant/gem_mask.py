"""Make the black texture around the blue/teal gems on her waist/hips transparent (alpha cut-out), precisely:
  1. find the hip/waist UV islands = main-atlas triangles of the body slot whose centroid is between z=0.78 and z=1.20 m
  2. gem pixels = blue/teal-dominant pixels inside those islands
  3. transparent = near-black pixels inside those islands that are within D px of a gem pixel AND reachable from it through black (so gold trim and the gem stay)
Writes enginesim/tex/atlas1_gems.png (RGBA); mk_atlas.py uses it when present."""
import sys, numpy as np
from PIL import Image
from scipy import ndimage as ndi
from glbio import Glb
D = 9
im = np.asarray(Image.open('../enginesim/tex/atlas1.png').convert('RGBA')).copy(); H, W, _ = im.shape
g = Glb('out_rig/cirith_armor_rigged.glb'); d = {n: dd for n, _, dd in g.primitives()}['cirith_body_armor']
p, uv, f = d['pos'], d['uv'], d['idx']
cz = p[f][:, :, 2].mean(1); sel = f[(cz > 0.78) & (cz < 1.20)]
# rasterise the selected triangles into an island mask (pixel space of the 512x256 atlas)
isl = np.zeros((H, W), bool)
for tri in sel:
    q = np.stack([uv[tri, 0] * (W - 1), uv[tri, 1] * (H - 1)], 1)
    if (q < -2).any() or (q > max(W, H)).any(): continue
    x0, x1 = int(max(q[:, 0].min(), 0)), int(min(q[:, 0].max(), W - 1)); y0, y1 = int(max(q[:, 1].min(), 0)), int(min(q[:, 1].max(), H - 1))
    xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1)); a, b, c = q
    den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
    if abs(den) < 1e-9: continue
    w0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / den; w1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / den; w2 = 1 - w0 - w1
    m = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02); isl[ys[m], xs[m]] = True
isl = ndi.binary_dilation(isl, iterations=1)
R, G, B = [im[..., k].astype(int) for k in range(3)]
gem = (B > R + 35) & (B > 90) & (G > 60) & isl
dark = ((R + G + B) / 3 < 48) & ~gem & isl
dist = ndi.distance_transform_edt(~gem)
# reachable-through-black within D px of a gem: geodesic growth from the gem outline through dark pixels
front = gem.copy(); reach = np.zeros_like(gem)
for _ in range(D):
    front = ndi.binary_dilation(front, structure=np.ones((3, 3))) & (dark | gem); reach |= front
clear = reach & dark
im[clear, 3] = 0
Image.fromarray(im).save('../enginesim/tex/atlas1_gems.png')
print('hip islands px', int(isl.sum()), 'gem px', int(gem.sum()), 'black px made transparent', int(clear.sum()))
# preview: checkerboard under the result, island outlined
chk = (((np.arange(W)[None, :] // 4) + (np.arange(H)[:, None] // 4)) % 2 * 90 + 60).astype(np.uint8)
bg = np.stack([chk] * 3, 2); al = im[..., 3:4] / 255.0; prev = (im[..., :3] * al + bg * (1 - al)).astype(np.uint8)
prev[ndi.binary_dilation(isl) & ~isl] = (255, 0, 255)
Image.fromarray(prev).resize((W * 2, H * 2), Image.NEAREST).save('../enginesim/out/gem_mask_preview.png')
