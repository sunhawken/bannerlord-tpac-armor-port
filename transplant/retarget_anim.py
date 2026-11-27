"""retarget_anim.py - bake one of Cirith's KUF animations (C_DS_NZ.glb, default anim 4 = CHARGE / STEP_FORWARD) onto Bannerlord's 28-bone human_skeleton.
Method: per mapped bone, world-space rotation change of the KUF bone (anim * rest^-1) is applied to the Bannerlord bone's rest world rotation, then converted to
parent-relative local rotations (rotations only; the pelvis also gets the vertical bob, scaled by the pelvis-height ratio). Un-mapped bones (twist helpers, toes, finger0,
ponytail) keep their rest local rotation.  Output: enginesim/CirithSim/module/ModuleData/cirith_anim_' + OUTNAME + '.json  { fps, frames:[[ [9 floats] x28 ]...], pelvis_dz:[...] }
usage: python retarget_anim.py [anim_index]"""
import os, sys, json, numpy as np
from glbio import Glb
from pose import Rig, qmat

ANIM = int(sys.argv[1]) if len(sys.argv) > 1 else 4
OUTNAME = sys.argv[2] if len(sys.argv) > 2 else 'charge'
MIRROR = len(sys.argv) > 3 and sys.argv[3] == 'm'   # 'm': mirrored clip - Cirith L -> Bannerlord l and R -> r (the sword hand of the clip becomes Bannerlord's RIGHT/combat hand)
SRC = r'C:\Users\Trey\Downloads\DFG\bare\C_DS_NZ.glb'          # read only
gk = Glb(SRC); rk = Rig(gk); gb = Glb('out_rig/cirith_armor_rigged.glb'); rb = Rig(gb)
kn = [gk.j['nodes'][i]['name'] for i in rk.joints]; bn = [gb.j['nodes'][i]['name'] for i in rb.joints]
# KUF joint index -> Bannerlord bone index (Cirith 'L' = Bannerlord r, see rigfit.py)
MAP = {1: 0, 2: 9, 3: 10, 4: 11, 5: 12, 6: 13, 13: 21, 14: 22, 15: 24, 16: 26, 17: 14, 18: 15, 19: 17, 20: 19, 21: 5, 22: 6, 23: 7, 24: 1, 25: 2, 26: 3}
if MIRROR: MAP = {1: 0, 2: 9, 3: 10, 4: 11, 5: 12, 6: 13, 13: 14, 14: 15, 15: 17, 16: 19, 17: 21, 18: 22, 19: 24, 20: 26, 21: 1, 22: 2, 23: 3, 24: 5, 25: 6, 26: 7}
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
S = np.diag([1.0, 1.0, -1.0]) if MIRROR else np.diag([-1.0, 1.0, -1.0])      # KUF faces +z, Bannerlord faces -z (glTF world): her model is turned 180 deg about the vertical axis (Cirith L -> Bannerlord r): deltas transform as Q R Q
frames = []; dz = []; rootpos = []; footz = []; handp = []
GRIPC = None
# ---- user edits from the grip editor (grip_editor/grip_edits.json): extra rotation per pose and bone, degrees, Euler XYZ (R = Rx Ry Rz), post-multiplied onto the bone's local rotation
# adjustments: [{bone, rot:[x,y,z] deg (Euler XYZ, R = Rx Ry Rz, post-multiplied onto the bone's local rotation), center:-1 = whole animation | frame (60 fps), width:frames}]
ADJ = []
_ef = os.environ.get('GRIP_EDITS', '../grip_editor/grip_edits.json')
if os.path.exists(_ef) and not MIRROR:
    _clipkey = {'charge': 'sprint'}.get(OUTNAME, OUTNAME)
    ADJ = [x for x in json.load(open(_ef)).get('clips', {}).get(_clipkey, {}).get('adj', []) if x.get('bone') in bn]
    if ADJ: print('joint adjustments applied to', _clipkey, [(x['bone'], x['rot'], x.get('center', -1)) for x in ADJ])
def _rxyz(d):
    a, b, c = np.radians(d); Rx = np.array([[1, 0, 0], [0, np.cos(a), -np.sin(a)], [0, np.sin(a), np.cos(a)]]); Ry = np.array([[np.cos(b), 0, np.sin(b)], [0, 1, 0], [-np.sin(b), 0, np.cos(b)]]); Rz = np.array([[np.cos(c), -np.sin(c), 0], [np.sin(c), np.cos(c), 0], [0, 0, 1]])
    return Rx @ Ry @ Rz
# grip frame of the sword in the Bannerlord l_hand bone frame, from her ORIGINAL geometry: in her files the weapon is the identity child of her sword-hand node (blade = hand +x, thin = +z, width = +y);
# Bannerlord sword axes (X guard, Y thin, Z blade) = (her y, z, x)  ->  B = Rb_rest^T * S * Rk_rest * E  (columns = sword axes in the hand frame). The DLL attaches the sword with this exact frame, so no wrist correction is needed.
if not MIRROR:
    E_ = np.stack([[0, 1, 0], [0, 0, 1], [1, 0, 0]], 1).astype(float)
    Bn = Rb_rest[19].T @ S @ Rk_rest[20] @ E_; u_, _, vt_ = np.linalg.svd(Bn); Bn = u_ @ vt_
    open('../enginesim/CirithSim/module/ModuleData/cirith_grip.txt', 'w').write(' '.join(f'{x:.6f}' for x in Bn.ravel()) + chr(10))
    json.dump(dict(X=Bn[:, 0].round(6).tolist(), Y=Bn[:, 1].round(6).tolist(), Z=Bn[:, 2].round(6).tolist()), open('../grip_editor/data/grip_l.json', 'w'))
    print('sword grip in l_hand frame: blade', Bn[:, 2].round(3), 'thin', Bn[:, 1].round(3), 'guard', Bn[:, 0].round(3), 'det', round(float(np.linalg.det(Bn)), 3))
Wrest_local_t = [np.array(gb.j['nodes'][j].get('translation', [0, 0, 0]), float) for j in rb.joints]
for f in range(nfr):
    t = f / FPS * (dur / (nfr / FPS)) if False else f / FPS
    Rk, Pk = world_rots(rk, rk.joints, ANIM, t)
    Wd = [None] * len(bn)                                         # desired world rotation of every Bannerlord bone
    for b in range(len(bn)):                                      # joints are listed parents-first
        if b in INV:
            k = INV[b]; delta = Rk[k] @ Rk_rest[k].T; delta = S @ delta @ S; Wd[b] = delta @ Rb_rest[b]
            if GRIPC is not None and b == 19: Wd[b] = Wd[b] @ GRIPC
        else:
            Wd[b] = (Wd[par[b]] if par[b] >= 0 else Rroot) @ (Rb_rest[par[b]].T @ Rb_rest[b] if par[b] >= 0 else np.eye(3))
    loc = []
    for b in range(len(bn)):
        P = Wd[par[b]] if par[b] >= 0 else Rroot
        Rl = P.T @ Wd[b]
        loc.append(Rl.ravel().tolist())               # local rotation (3x3 row-major; columns = images of the local axes)
    frames.append(loc)
    handp.append(Pk[20] - Pk[1]); rootpos.append(Pk[1].copy()); footz.append(float((Pk[23] - Pk[1])[2]))
    # pelvis vertical bob (glTF Y is up): bob relative to rest, scaled
    dz.append(float((Pk[1][1] - Pk_rest[1][1]) * SCALE))
dz = np.array(dz); dz -= dz.mean()                                # keep the cycle centred on the rest height
out = dict(anim=ANIM, name=a['name'], fps=FPS, scale=SCALE, bones=bn, frames=frames, pelvis_dz=dz.round(5).tolist())
pass
rp = np.array(rootpos); disp = (rp[-1] - rp[0]) * SCALE; hs = float(np.hypot(disp[0], disp[2])) / (dur * (nfr - 1) / nfr) if nfr > 1 else 0.0
hp_ = np.array(handp); hsp = np.linalg.norm(np.diff(hp_, axis=0), axis=1) * SCALE * FPS; hsp[:4] = 0; hsp = np.convolve(hsp, np.ones(3) / 3, mode='same'); pk_ = int(np.argmax(hsp)); thr_ = 0.4 * hsp[pk_]; wins = []; k_ = 0
while k_ < len(hsp):
    if hsp[k_] >= thr_:
        k2 = k_
        while k2 + 1 < len(hsp) and hsp[k2 + 1] >= thr_: k2 += 1
        if k2 - k_ >= 1: wins.append((k_ / FPS, (k2 + 1) / FPS))
        k_ = k2 + 1
    else: k_ += 1
print('strike windows (weapon-hand speed >= 40 pct of peak ' + f'{hsp[pk_]:.1f} m/s, relative to pelvis):', [(round(a, 2), round(b, 2)) for a, b in wins])
strike_s, strike_e = (wins[0] if wins else (-1, -1))
natv = 2 * (max(footz) - min(footz)) * SCALE / dur   # body speed implied by the gait (2 steps per cycle, step = foot peak-to-peak)
print(f'gait: foot peak-to-peak {(max(footz)-min(footz))*SCALE:.2f} m -> natural locomotion speed ~{natv:.2f} m/s')
print(f'root (pelvis) travel over the clip: {np.round(disp, 3).tolist()} m -> {hs:.2f} m/s horizontal ({"ROOT MOTION" if hs > 0.3 else "in place"})')
print('wrote cirith_anim_' + OUTNAME + '.json', nfr, 'frames x', len(bn), 'bones; pelvis bob range', dz.min().round(3), dz.max().round(3))

# ---- resample to 60 fps (slerp) and write the plain-text version the DLL reads:  "fps N nbones" then per frame: dz r00..r22 x28
from scipy.spatial.transform import Rotation as Rot, Slerp
F = np.array(frames).reshape(nfr, len(bn), 3, 3); NT = int(round(nfr / FPS * 60)); tt = np.arange(NT) / 60.0; src_t = np.arange(nfr) / FPS
tt = np.clip(tt, 0, src_t[-1]); dense = np.zeros((NT, len(bn), 3, 3))
for b in range(len(bn)):
    dense[:, b] = Slerp(src_t, Rot.from_matrix(F[:, b])) (tt).as_matrix()
dzd = np.interp(tt, src_t, dz)
# cumulative pelvis travel relative to frame 0: forward (KUF +z = Bannerlord forward) and lateral (KUF x mirrored)
rp0 = rp - rp[0]; rfw = np.interp(tt, src_t, rp0[:, 2]) * SCALE; rlat = -np.interp(tt, src_t, rp0[:, 0]) * SCALE
def _write(path, D):
    with open(path, 'w') as fh:
        fh.write(f'60 {NT} {len(bn)} {hs:.3f} {natv:.3f} {len(wins)}' + ''.join(f' {a:.3f} {b:.3f}' for a, b in wins) + chr(10))
        for i in range(NT): fh.write(f'{dzd[i]:.5f} {rfw[i]:.5f} {rlat[i]:.5f} ' + ' '.join(f'{x:.6f}' for x in D[i].ravel()) + chr(10))
if not MIRROR: _write('out_rig/anim_base/cirith_anim_' + OUTNAME + '_base.txt', dense)      # raw retarget = what the editor starts from
for adj in ADJ:                                                                                   # bake the editor's joint adjustments (same maths as the editor preview)
    bi = bn.index(adj['bone']); c = adj.get('center', -1); wd = max(float(adj.get('width', 12)), 1.0); R = np.array(adj['rot'], float)
    for i in range(NT):
        if c is None or c < 0: w = 1.0
        else:
            d = abs(i - c); d = min(d, NT - d); w = float(np.exp(-0.5 * (d / (wd / 2.0)) ** 2))
        if w > 1e-3: dense[i, bi] = dense[i, bi] @ _rxyz(R * w)
with open('../enginesim/CirithSim/module/ModuleData/cirith_anim_' + OUTNAME + '.txt', 'w') as fh:
    fh.write(f'60 {NT} {len(bn)} {hs:.3f} {natv:.3f} {len(wins)}' + ''.join(f' {a:.3f} {b:.3f}' for a, b in wins) + chr(10))
    for i in range(NT): fh.write(f'{dzd[i]:.5f} {rfw[i]:.5f} {rlat[i]:.5f} ' + ' '.join(f'{x:.6f}' for x in dense[i].ravel()) + chr(10))
print('wrote cirith_anim_' + OUTNAME + '.txt', NT, 'frames @60fps')
