"""dump_weapon.py - her KUF weapon E_DS_NZ_R.glb -> out_rig/weapon_slots.bin (same container as slots.bin, slot name 'cirith_sword', no skinning).
KUF weapon space: blade along +x, flat in x-y, thin in z, game cm, grip at the origin. Bannerlord one-handed sword space (vanilla vlandia_onehanded_sword_a): blade +Z, guard/flat across X, thin along Y, metres, grip at origin.
Mapping (proper rotation, cyclic): BL (X, Y, Z) = (y, z, x) * 0.01."""
import struct, numpy as np
from glbio import Glb
g = Glb(r'C:\Users\Trey\Downloads\DFG\weapons\E_DS_NZ_R.glb'); n_, _, d = list(g.primitives())[0]
M = lambda a: np.stack([a[:, 1], a[:, 2], a[:, 0]], 1)
pos = (M(d['pos'].astype(np.float64)) * 0.01).astype('<f4'); nrm = M(d['nrm'].astype(np.float64)); nrm = (nrm / np.linalg.norm(nrm, axis=1, keepdims=True)).astype('<f4')
uv = d['uv'].astype('<f4'); idx = d['idx'].astype('<i4')
# winding: a proper rotation keeps it, but verify normals agree with face normals
fn = np.cross(pos[idx[:, 1]] - pos[idx[:, 0]], pos[idx[:, 2]] - pos[idx[:, 0]]); agree = float(np.mean(np.einsum('ij,ij->i', fn, nrm[idx[:, 0]]) > 0)); print('face/normal agreement', round(agree, 3))
if agree < 0.5: idx = idx[:, [0, 2, 1]]; print('flipped winding')
N, F = len(pos), len(idx)
with open('out_rig/weapon_slots.bin', 'wb') as f:
    f.write(struct.pack('<i', 1)); f.write(struct.pack('<i', 1)); b = b'pelvis'; f.write(struct.pack('<i', len(b)) + b)
    nb = b'cirith_sword'; f.write(struct.pack('<i', len(nb)) + nb); f.write(struct.pack('<ii', N, F))
    f.write(pos.tobytes()); f.write(nrm.tobytes()); f.write(uv.tobytes()); f.write(np.zeros((N, 4), np.uint8).tobytes()); f.write(np.zeros((N, 4), np.uint8).tobytes()); f.write(np.full(N, 255, np.uint8).tobytes()); f.write(idx.tobytes())
print('weapon', N, 'verts', F, 'faces; bbox (m)', pos.min(0).round(3), pos.max(0).round(3))
