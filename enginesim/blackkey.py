"""blackkey.py - 'black means transparent' cut-out for BOTH source atlases (main parts + orange trims), recipe from the user's prompt:
 1. alpha = 0 where max(R,G,B) < T (T=16, pure black) or the texture's own alpha is < 128
 2. one dilation pass over the halo: also clear texels within 1 px of a cleared texel with max(R,G,B) < 72 and low saturation (gold/teal/orange texels are never eaten)
 3. colour bleed: every transparent texel gets the colour of its nearest opaque texel (no dark rims after bilinear/mip filtering)
 Exemptions: the painted areola (near black by design). Output: tex/atlas1_cut.png, tex/atlas2_cut.png (+ out/blackkey_*_magenta.png for looking at the result)
usage: python blackkey.py   (run transplant/gem_mask.py first so atlas1_gems.png holds the areola)"""
import numpy as np, sys
from PIL import Image
from scipy import ndimage as ndi
T = 16; HALO_MAX = 72; HALO_SAT = 45
def cut(src, dst, exempt=None):
    im = np.asarray(Image.open(src).convert('RGBA')).copy(); H, W = im.shape[:2]
    mx = im[..., :3].max(2).astype(int); mn = im[..., :3].min(2).astype(int)
    clear = (mx < T) | (im[..., 3] < 128)
    if exempt is not None: clear &= ~exempt
    near = ndi.binary_dilation(clear, structure=np.ones((3, 3), bool), iterations=1)
    clear |= near & (mx < HALO_MAX) & ((mx - mn) < HALO_SAT) & (im[..., 3] >= 128 if exempt is None else True)
    if exempt is not None: clear &= ~exempt
    im[..., 3] = np.where(clear, 0, 255)
    _, (iy, ix) = ndi.distance_transform_edt(clear, return_indices=True)
    for k in range(3): im[..., k] = im[iy, ix][..., k] * clear + im[..., k] * (~clear)
    Image.fromarray(im).save(dst)
    mag = im.copy(); mag[im[..., 3] == 0, :3] = (255, 0, 255)
    Image.fromarray(mag[..., :3]).resize((W * 2, H * 2), Image.NEAREST).save('out/blackkey_' + dst.split('/')[-1].replace('.png', '') + '_magenta.png')
    print(dst, 'transparent %.1f %%' % (100 * clear.mean()), 'texels', int(clear.sum()))
yy, xx = np.mgrid[0:256, 0:512]
areola = (((xx - 138) / 16.0) ** 2 + ((yy - 115) / 13.0) ** 2) <= 1.0      # painted areola ellipse (gem_mask.py: centre 138,115 radii 12x9.5) + margin
base = np.asarray(Image.open('tex/atlas1.png').convert('RGBA')).copy(); gem = np.asarray(Image.open('tex/atlas1_gems.png').convert('RGBA'))
base[areola] = gem[areola]; Image.fromarray(base).save('tex/atlas1_base_areola.png')          # ORIGINAL skin + the painted areola only
face = (xx >= 243) & (xx <= 290) & (yy >= 46) & (yy <= 140)      # face island: eyes, brows, lips, nostrils are real dark detail -> own alpha only (opaque)
cut('tex/atlas1_base_areola.png', 'tex/atlas1_cut.png', exempt=areola | face)
cut('tex/atlas2.png', 'tex/atlas2_cut.png')
