"""retarget_anim.py - bake one of Cirith's KUF animations (C_DS_NZ.glb, default anim 4 = CHARGE / STEP_FORWARD) onto Bannerlord's 28-bone human_skeleton.
Method: per mapped bone, world-space rotation change of the KUF bone (anim * rest^-1) is applied to the Bannerlord bone's rest world rotation, then converted to
parent-relative local rotations (rotations only; the pelvis also gets the vertical bob, scaled by the pelvis-height ratio). Un-mapped bones (twist helpers, toes, finger0,
ponytail) keep their rest local rotation.  Output: enginesim/CirithSim/module/ModuleData/cirith_charge.json  { fps, frames:[[ [9 floats] x28 ]...], pelvis_dz:[...] }
usage: python retarget_anim.py [anim_index]"""
import sys, json, numpy as np
from glbio import Glb
from pose import Rig, qmat

ANIM = int(sys.argv[1]) if len(sys.argv) > 1 else 4
SRC = r'C:\Users\Trey\Downloads\DFG\bare\C_DS_NZ.glb'          # read only
gk = Glb(SRC); rk = Rig(gk); gb = Glb('out_rig/cirith_armor_rigged.glb'); rb = Rig(gb)
kn = [gk.j['nodes'][i]['name'] for i in rk.joints]; bn = [gb.j['nodes'][i]['name'] for i in rb.joints]
# KUF joint index -> Bannerlord bone index (Cirith 'L' = Bannerlord r, see rigfit.py)
MAP = {1: 0, 2: 9, 3: 10, 4: 11, 5: 12, 6: 13, 13: 21, 14: 22, 15: 24, 16: 26, 17: 14, 18: 15, 19: 17, 20: 19, 21: 5, 22: 6, 23: 7, 24: 1, 25: 2, 26: 3}
INV = {v: k for k, v in MAP.items()}

def rot(W):
    R = W[:3, :3].copy(); R /= np.linalg.norm(R, axis=0, keepdims=True); return R

def world_rots(rig, joints, anim, t):
    W = rig.world(anim, t); return [rot(W[j]) for j in joints], [W[j][:3, 3] for j in joints]

# Bannerlord rest
Rb_rest, Pb_rest = world_rots(rb, rb.joints, None, 0)
Rk_rest, Pk_rest = world_rots(rk, rk.joints, None, 0)
# parent (joint ordinal) of each Bannerlord bone
jidx = {n: k for k, n in enumerate(rb.joints)}
par = [jidx.get(rb.parent[j], -1) for j in rb.joints]
Rroot = rot(rb.world(None)[0])                                   # Root node (Z-up -> Y-up) rotation
# scale ratio (Bannerlord / KUF) from pelvis height above the feet
hk = Pk_rest[1][1] - Pk_rest[23][1]; hb = Pb_rest[0][1] - Pb_rest[3][1]; SCALE = hb / hk
a = rk.anims[ANIM]; dur = a['dur']; FPS = 30.0; nfr = max(2, int(round(dur * FPS)))
print('anim', ANIM, a['name'], 'dur', dur, 'frames', nfr, 'scale', round(SCALE, 4), 'KUF pelvis h', round(hk, 2), 'BL', round(hb, 3))
S = np.diag([-1.0, 1.0, -1.0])      # KUF faces +z, Bannerlord faces -z (glTF world): her model is turned 180 deg about the vertical axis (Cirith L -> Bannerlord r): deltas transform as Q R Q
frames = []; dz = []
Wrest_local_t = [np.array(gb.j['nodes'][j].get('translation', [0, 0, 0]), float) for j in rb.joints]
for f in range(nfr):
    t = f / FPS * (dur / (nfr / FPS)) if False else f / FPS
    Rk, Pk = world_rots(rk, rk.joints, ANIM, t)
    Wd = [None] * len(bn)                                         # desired world rotation of every Bannerlord bone
    for b in range(len(bn)):                                      # joints are listed parents-first
        if b in INV:
            k = INV[b]; delta = Rk[k] @ Rk_rest[k].T; delta = S @ delta @ S; Wd[b] = delta @ Rb_rest[b]
        else:
            Wd[b] = (Wd[par[b]] if par[b] >= 0 else Rroot) @ (Rb_rest[par[b]].T @ Rb_rest[b] if par[b] >= 0 else np.eye(3))
    loc = []
    for b in range(len(bn)):
        P = Wd[par[b]] if par[b] >= 0 else Rroot
        loc.append((P.T @ Wd[b]).ravel().tolist())               # local rotation (3x3 row-major; columns = images of the local axes)
    frames.append(loc)
    # pelvis vertical bob (glTF Y is up): bob relative to rest, scaled
    dz.append(float((Pk[1][1] - Pk_rest[1][1]) * SCALE))
dz = np.array(dz); dz -= dz.mean()                                # keep the cycle centred on the rest height
out = dict(anim=ANIM, name=a['name'], fps=FPS, scale=SCALE, bones=bn, frames=frames, pelvis_dz=dz.round(5).tolist())
pass
print('wrote cirith_charge.json', nfr, 'frames x', len(bn), 'bones; pelvis bob range', dz.min().round(3), dz.max().round(3))

# ---- resample to 60 fps (slerp) and write the plain-text version the DLL reads:  "fps N nbones" then per frame: dz r00..r22 x28
from scipy.spatial.transform import Rotation as Rot, Slerp
F = np.array(frames).reshape(nfr, len(bn), 3, 3); NT = int(round(nfr / FPS * 60)); tt = np.arange(NT) / 60.0; src_t = np.arange(nfr) / FPS
tt = np.clip(tt, 0, src_t[-1]); dense = np.zeros((NT, len(bn), 3, 3))
for b in range(len(bn)):
    dense[:, b] = Slerp(src_t, Rot.from_matrix(F[:, b])) (tt).as_matrix()
dzd = np.interp(tt, src_t, dz)
with open('../enginesim/CirithSim/module/ModuleData/cirith_charge.txt', 'w') as fh:
    fh.write(f'60 {NT} {len(bn)}\n')
    for i in range(NT): fh.write(f'{dzd[i]:.5f} ' + ' '.join(f'{x:.6f}' for x in dense[i].ravel()) + '\n')
print('wrote cirith_charge.txt', NT, 'frames @60fps')
