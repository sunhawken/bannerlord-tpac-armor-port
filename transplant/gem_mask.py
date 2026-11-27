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
h = {n: dd for n, _, dd in g.primitives()}['cirith_head_armor']   # the necklace/collar with the pendant gem lives in the head slot
cz = p[f][:, :, 2].mean(1); cx = np.abs(p[f][:, :, 0]).mean(1)
# hips/waist band + breast band (centre of the chest only: the shoulder/armlet gems at |x|>0.2 are left alone)
sel = f[((cz > 0.78) & (cz < 1.20)) | ((cz >= 1.20) & (cz < 1.52) & (cx < 0.19))]
# rasterise the selected triangles into an island mask (pixel space of the 512x256 atlas)
hz = h['pos'][h['idx']][:, :, 2].mean(1); hx = np.abs(h['pos'][h['idx']][:, :, 0]).mean(1)
sel_h = h['idx'][(hz > 1.40) & (hz < 1.56) & (hx < 0.16)]
isl = np.zeros((H, W), bool)
for tri, uvs in [(t, uv) for t in sel] + [(t, h['uv']) for t in sel_h]:
    q = np.stack([uvs[tri, 0] * (W - 1), uvs[tri, 1] * (H - 1)], 1)
    if (q < -2).any() or (q > max(W, H)).any(): continue
    x0, x1 = int(max(q[:, 0].min(), 0)), int(min(q[:, 0].max(), W - 1)); y0, y1 = int(max(q[:, 1].min(), 0)), int(min(q[:, 1].max(), H - 1))
    xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1)); a, b, c = q
    den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
    if abs(den) < 1e-9: continue
    w0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / den; w1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / den; w2 = 1 - w0 - w1
    m = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02); isl[ys[m], xs[m]] = True
isl[26:68, 162:204] = True      # pendant diamond on the collar: its UV island is only partly covered by the height bands
isl = ndi.binary_dilation(isl, iterations=1)
R, G, B = [im[..., k].astype(int) for k in range(3)]
gem = (B > R + 35) & (B > 90) & (G > 60) & isl
dark = ((R + G + B) / 3 < 56) & ~gem & isl
dist = ndi.distance_transform_edt(~gem)
# reachable-through-black within D px of a gem: geodesic growth from the gem outline through dark pixels
front = gem.copy(); reach = np.zeros_like(gem)
for _ in range(D):
    front = ndi.binary_dilation(front, structure=np.ones((3, 3))) & (dark | gem); reach |= front
clear = reach & dark
clear[170:, :95] = False       # bead strips / 4-gem cluster (fringe + ponytail rings): thin details, transparency made them vanish (alpha blocks + mips) -> keep opaque like the viewer
clear[0:100, 150:215] = False  # pendant: redone precisely below
import os
sys.path.insert(0, '../enginesim'); from skin_islands import raster as _raster
def _outside_black(rect, tri_sets, name):
    """precise cut: black texels connected to the border of the island's triangle mask through near-black (never across the gold outline) -> transparent"""
    x0, y0, x1, y1 = rect; m = np.zeros((H, W), bool)
    for uvs_, f_ in tri_sets:
        c_ = uvs_[f_].mean(1); sel_ = f_[(c_[:, 0] * (W - 1) > x0) & (c_[:, 0] * (W - 1) < x1) & (c_[:, 1] * (H - 1) > y0) & (c_[:, 1] * (H - 1) < y1)]
        if len(sel_): m |= _raster(sel_, uvs_, H, W)
    m = ndi.binary_dilation(m, iterations=1)
    lum_ = im[..., :3].astype(int).sum(2) / 3.0; sat_ = im[..., :3].max(2).astype(int) - im[..., :3].min(2).astype(int)
    dk = m & (lum_ < 48) & (sat_ < 40)
    seeds = dk & ~ndi.binary_erosion(m, iterations=3)
    fl = ndi.binary_propagation(seeds, mask=dk)
    print(name, 'mask px', int(m.sum()), 'outside-black made transparent', int(fl.sum())); return fl
hp = {n_: dd_ for n_, _, dd_ in g.primitives()}['cirith_head_armor']
sets = [(uv.astype(float), f), (hp['uv'].astype(float), hp['idx'])]
cut_pend = _outside_black((150, 10, 215, 90), sets, 'pendant')
cut_ear = _outside_black((296, 98, 362, 162), sets, 'hoop earrings')
def _outside_black_tris(tri_sets, name, halo=2, edge=3):
    """same flood cut, but for explicit triangle sets (hips / collar). The transparent set keeps a `halo`-px opaque black rim around every non-black texel (gold, teal gems, beads),
    so thin details are never eroded by DXT5 alpha blocks / mip filtering."""
    m = np.zeros((H, W), bool)
    for uvs_, f_ in tri_sets:
        if len(f_): m |= _raster(f_, uvs_, H, W)
    m = ndi.binary_dilation(m, iterations=1)
    lum_ = im[..., :3].astype(int).sum(2) / 3.0; sat_ = im[..., :3].max(2).astype(int) - im[..., :3].min(2).astype(int)
    dk = m & (lum_ < 24) & (sat_ < 30)      # true black only: the dark-grey SKIN of the same islands (lum ~40+) must stay
    seeds = dk & ~ndi.binary_erosion(m, iterations=edge)
    fl = ndi.binary_propagation(seeds, mask=dk)
    keep = ndi.binary_dilation(m & ~dk, iterations=halo)      # opaque rim around gold/gems/beads
    fl &= ~keep
    print(name, 'mask px', int(m.sum()), 'transparent', int(fl.sum())); return fl
_cz = p[f][:, :, 2].mean(1)
hip_sel = f[(_cz > 0.78) & (_cz < 1.20)]
_hz = hp['pos'][hp['idx']][:, :, 2].mean(1); _hx = np.abs(hp['pos'][hp['idx']][:, :, 0]).mean(1)
col_sel = hp['idx'][(_hz > 1.40) & (_hz < 1.56) & (_hx < 0.30)]
cut_hip = _outside_black_tris([(uv.astype(float), hip_sel)], 'hips')
cut_col = _outside_black_tris([(hp['uv'].astype(float), col_sel)], 'collar')
cut_pend = cut_pend | cut_hip | cut_col
clear = clear | cut_pend | cut_ear
im[clear, 3] = 0
# colour of the transparent texels = nearest opaque texel (no dark halo when filtered)
_d, (_iy, _ix) = ndi.distance_transform_edt(clear, return_indices=True)
_cp = (cut_pend | cut_ear)
for k in range(3): im[..., k][_cp] = im[..., k][_iy[_cp], _ix[_cp]]
# lift the chest/torso SKIN so the breast shading is actually visible under the bead net (it sat at lum ~40-65, nearly black in the engine's lighting):
# low-saturation grey pixels in the torso skin block of the atlas get a gamma lift; gold, gems and beads (saturated/bright) are untouched.
sk = np.zeros((H, W), bool); sk[0:256, 100:250] = True
mx = np.maximum.reduce([R, G, B]); mn = np.minimum.reduce([R, G, B]); lum = (R + G + B) / 3.0
skin = sk & ((mx - mn) < 22) & (lum < 150) & (lum > 18) & (im[..., 3] > 0)
lifted = 255.0 * (lum[skin] / 255.0) ** 0.72; gain = lifted / lum[skin]
if os.environ.get('LIFT') == '1':
    for k in range(3): im[..., k][skin] = np.clip(im[..., k][skin] * gain, 0, 255).astype(np.uint8)
print('skin px lifted', int(skin.sum()))

# ---- seamless arms: the arm/shoulder skin islands were NOT lifted (lum ~62 vs torso ~89 -> visible tone step at the shoulder). Same gamma on the skin pixels of every body triangle outside the torso block,
# then pad the lifted skin colour 4 px into the black gutter so bilinear/mip/edge samples never pull in black (dark seam lines).
sys.path.insert(0, '../enginesim'); from skin_islands import raster
cz_ = p[f][:, :, 2].mean(1); cx_ = np.abs(p[f][:, :, 0]).mean(1)
arm_tris = f[(cx_ > 0.17) & (cz_ > 1.2)]
arm_m = raster(arm_tris, uv, H, W)
skin2 = arm_m & ~sk & ((mx - mn) < 22) & (lum < 150) & (lum > 18) & (im[..., 3] > 0)
lift2 = 255.0 * (lum[skin2] / 255.0) ** 0.72; gain2 = lift2 / lum[skin2]
if os.environ.get('LIFT') == '1':
    for k in range(3): im[..., k][skin2] = np.clip(im[..., k][skin2] * gain2, 0, 255).astype(np.uint8)
print('arm skin px lifted', int(skin2.sum()))
allm = raster(f, uv, H, W)
lum2 = im[..., :3].astype(int).sum(2) / 3.0; sat2 = im[..., :3].max(2).astype(int) - im[..., :3].min(2).astype(int)
sk_all = (skin | skin2) & (sat2 < 22) & (lum2 > 22)
dist, (iy, ix) = ndi.distance_transform_edt(~sk_all, return_indices=True)
gut = (~allm) & (dist <= 4) & (lum2 < 14) & (im[..., 3] > 0)
for k in range(3): im[..., k][gut] = im[..., k][iy[gut], ix[gut]]
print('gutter px padded with skin', int(gut.sum()))
from seam_fix import fix as _seamfix
im[..., :3], _nf = _seamfix(im, p, uv.astype(float), f)[0][..., :3], 0
print('seam-edge dark texels repainted (see seam_fix.py)')

# ---- very dark, bigger areolas. Both breasts share one UV spot (the body UVs are mirrored): the front-most breast vertex sits at atlas px (218,76).
# measured texel density there: 0.36 cm/px along u, 0.30 cm/px along v -> an areola ~6.5 cm across = ellipse radii ~9 x 11 px. Detail comes from the Venice swatch if present.
import os
cx0, cy0, rx, ry = 138.0, 115.0, 12.0, 9.5     # front breast UV island (verified: nearby verts have y>0.098 = front). The old (218,76) spot was on the BACK (y=-0.075).
yy, xx = np.mgrid[0:H, 0:W]
rr = np.sqrt(((xx - cx0) / rx) ** 2 + ((yy - cy0) / ry) ** 2)
edge = np.clip((1.08 - rr) / 0.18, 0, 1)                       # fairly crisp rim
detail = np.ones((H, W))
dp = '../enginesim/tex/areola_detail.png'
if os.path.exists(dp):
    sw = np.asarray(Image.open(dp).convert('L').resize((64, 64), Image.LANCZOS)).astype(float); sw = sw / max(sw.mean(), 1e-3)
    u = np.clip(((xx - (cx0 - rx * 1.3)) / (rx * 2.6) * 63).astype(int), 0, 63); v = np.clip(((yy - (cy0 - ry * 1.3)) / (ry * 2.6) * 63).astype(int), 0, 63)
    detail = np.clip(0.75 + 0.25 * sw[v, u], 0.5, 1.5)
else:
    rng = np.random.default_rng(7); n = rng.normal(1, 0.18, (H, W)); detail = np.clip(ndi.gaussian_filter(n, 0.7), 0.6, 1.4)
nip = np.clip((1.0 - rr / 0.28), 0, 1)                         # tiny raised nipple centre, a touch lighter than the areola
base = np.array([5, 3, 3], float)             # near black
areola = base[None, None, :] * detail[..., None] * (1 + 0.55 * nip[..., None])
a = edge[..., None]
im[..., :3] = np.clip(im[..., :3] * (1 - a) + areola * a, 0, 255).astype(np.uint8)
im[..., 3] = np.where(edge > 0, 255, im[..., 3])
print('areola painted at', (cx0, cy0), 'radii', (rx, ry), 'detail from', 'venice' if os.path.exists(dp) else 'procedural')
Image.fromarray(im).save('../enginesim/tex/atlas1_gems.png')
print('hip islands px', int(isl.sum()), 'gem px', int(gem.sum()), 'black px made transparent', int(clear.sum()))
# preview: checkerboard under the result, island outlined
chk = (((np.arange(W)[None, :] // 4) + (np.arange(H)[:, None] // 4)) % 2 * 90 + 60).astype(np.uint8)
bg = np.stack([chk] * 3, 2); al = im[..., 3:4] / 255.0; prev = (im[..., :3] * al + bg * (1 - al)).astype(np.uint8)
prev[ndi.binary_dilation(isl) & ~isl] = (255, 0, 255)
Image.fromarray(prev).resize((W * 2, H * 2), Image.NEAREST).save('../enginesim/out/gem_mask_preview.png')
