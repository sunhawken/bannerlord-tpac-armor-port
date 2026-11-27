"""export_game_mesh.py - the EXACT geometry the game uses (out_rig/slots.bin: body, legs, hands, head incl. trims, wrist sleeves, angled arms, hand slide, baked ponytail) + the exact atlas -> grip_editor/data/game_mesh.json + game_atlas.png"""
import struct, json, shutil, numpy as np
b = open('out_rig/slots.bin', 'rb').read(); o = 0; ns, nj = struct.unpack_from('<ii', b, o); o += 8; jn = []
for _ in range(nj): l = struct.unpack_from('<i', b, o)[0]; jn.append(b[o + 4:o + 4 + l].decode()); o += 4 + l
slots = []
for _ in range(ns):
    l = struct.unpack_from('<i', b, o)[0]; nm = b[o + 4:o + 4 + l].decode(); o += 4 + l; N, F = struct.unpack_from('<ii', b, o); o += 8
    pos = np.frombuffer(b, '<f4', N * 3, o).copy(); o += N * 12; nrm = np.frombuffer(b, '<f4', N * 3, o).copy(); o += N * 12; uv = np.frombuffer(b, '<f4', N * 2, o).copy(); o += N * 8
    jj = np.frombuffer(b, np.uint8, N * 4, o).copy(); o += N * 4; ww = np.frombuffer(b, np.uint8, N * 4, o).astype(float) / 255; o += N * 4; al = np.frombuffer(b, np.uint8, N, o).copy(); o += N
    idx = np.frombuffer(b, '<i4', F * 3, o).copy(); o += F * 12
    slots.append(dict(name=nm, pos=np.round(pos, 5).tolist(), nrm=np.round(nrm, 4).tolist(), uv=np.round(uv, 5).tolist(), jnt=jj.tolist(), wgt=np.round(ww, 4).tolist(), idx=idx.tolist()))
json.dump(dict(joints=jn, slots=slots), open('../grip_editor/data/game_mesh.json', 'w'))
shutil.copy('../enginesim/tex/cirith_atlas.png', '../grip_editor/data/game_atlas.png')
print('exported', [(s['name'], len(s['pos']) // 3) for s in slots])
