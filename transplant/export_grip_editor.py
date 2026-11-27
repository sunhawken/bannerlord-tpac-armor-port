"""export_grip_editor.py - writes grip_editor/data/clips.json (the four retargeted clips) + sword.json (her sword mesh in Bannerlord sword space) for the sword-grip editor."""
import json, numpy as np
D = 'out_rig/anim_base/'
clips = {}
names = None
for label, f in (('idle', 'idle'), ('walk', 'walk'), ('run', 'run'), ('sprint', 'charge')):
    L = open(D + f'cirith_anim_{f}_base.txt').read().splitlines(); h = L[0].split(); n = int(h[1]); nb = int(h[2])
    dz = []; rot = []
    for i in range(n):
        p = L[i + 1].split(); dz.append(round(float(p[0]), 5)); rot.extend(round(float(x), 5) for x in p[3:3 + nb * 9])
    clips[label] = dict(n=n, nb=nb, dz=dz, rot=rot)
# T-pose clip: every bone at its rest local rotation (the engine rest pose = Bannerlord's T-pose)
from pose import Rig as _Rig, qmat as _qmat
from glbio import Glb as _Glb
_g = _Glb('out_rig/cirith_armor_rigged.glb'); _r = _Rig(_g); _rot = []
for _j in _r.joints: _rot.extend(round(float(x), 5) for x in _qmat(_r.r0[_j]).ravel())
clips['tpose'] = dict(n=1, nb=28, dz=[0.0], rot=_rot)
json.dump(dict(fps=60, clips=clips), open('../grip_editor/data/clips.json', 'w'))
from glbio import Glb
g = Glb(r'C:\Users\Trey\Downloads\DFG\weapons\E_DS_NZ_R.glb'); _, _, d = list(g.primitives())[0]
M = lambda a: np.stack([a[:, 1], a[:, 2], a[:, 0]], 1)
pos = (M(d['pos'].astype(float)) * 0.01); nrm = M(d['nrm'].astype(float))
json.dump(dict(pos=np.round(pos, 5).ravel().tolist(), nrm=np.round(nrm, 4).ravel().tolist(), uv=np.round(d['uv'].astype(float), 5).ravel().tolist(), idx=d['idx'].astype(int).ravel().tolist()), open('../grip_editor/data/sword.json', 'w'))
print('exported', {k: v['n'] for k, v in clips.items()}, 'frames; sword verts', len(pos))
