"""mk_weapon_tex.py - her weapon texture (E_DS_NZ_R.png, 64x512) -> tex/cirith_sword.dxt5 (DXT5 + full mip chain, same container as cirith_main.dxt5). Pixels are NOT edited."""
import struct, sys, numpy as np
from PIL import Image
src = open('mk_atlas.py', encoding='utf-8').read(); a = src.index('def enc_color'); b = src.index('mips = []')
exec(compile(src[a:b], 'enc', 'exec'))            # reuse the encoder functions of mk_atlas.py (enc_color / enc_alpha / enc_dxt5)
img = Image.open(r'C:\Users\Trey\Downloads\DFG\C_DS_NZ\textures\E_DS_NZ_R.png').convert('RGBA')
mips = []; cur = img
while True:
    mips.append(enc_dxt5(cur))
    if cur.size == (1, 1): break
    cur = cur.resize((max(cur.size[0] // 2, 1), max(cur.size[1] // 2, 1)), Image.BOX)
with open('tex/cirith_sword.dxt5', 'wb') as f:
    f.write(struct.pack('<III', img.size[0], img.size[1], len(mips)))
    for m in mips: f.write(struct.pack('<I', len(m)) + m)
print('sword texture', img.size, len(mips), 'mips')
