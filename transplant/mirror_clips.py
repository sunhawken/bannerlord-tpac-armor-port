"""mirror_clips.py - "quick symmetry": mirror the FINAL baked clips (user's joint edits included) left<->right so the sword ends up in the RIGHT hand.
For every frame: world rotation of each bone -> delta against its rest -> reflected across the body's mid plane (M = diag(-1,1,1) in the glTF world) -> applied to the COUNTERPART bone's rest -> local rotations again.
Pelvis lateral travel is negated. Output: ModuleData/cirith_anim_<name>_m.txt (idle walk run charge) and cirith_grip_edits_r.txt (the sword placement per pose, mirrored into the right-hand frame).
usage: python mirror_clips.py   (run after apply_grip_edits.py / retarget_anim.py)"""
import numpy as np, os, json
from glbio import Glb
from pose import Rig
MD = '../enginesim/CirithSim/module/ModuleData/'
gb = Glb('out_rig/cirith_armor_rigged.glb'); rb = Rig(gb); bn = [gb.j['nodes'][i]['name'] for i in rb.joints]
def rot(W):
    R = W[:3, :3].copy(); R /= np.linalg.norm(R, axis=0, keepdims=True); return R
Wr = rb.world(None); Rr = [rot(Wr[j]) for j in rb.joints]; Rroot = rot(Wr[0]) if False else rot(rb.world(None)[0])
jidx = {n: k for k, n in enumerate(rb.joints)}; par = [jidx.get(rb.parent[j], -1) for j in rb.joints]
# Rroot as retarget_anim: rotation of the node above the pelvis (Root)
Rroot = rot(rb.world(None)[0]) if False else None
import sys
sys.argv = ['x']
exec(open('retarget_anim.py').read().split('# Bannerlord rest')[0].split("gk = Glb(SRC)")[0]) if False else None
# Root rotation: exactly as retarget_anim.py: rot(rb.world(None)[0])   (world of the first joint's *own* rest, see below)
Rroot = rot(rb.world(None)[0]) @ np.eye(3)
L = {n: i for i, n in enumerate(bn)}
SW = list(range(len(bn)))
for a, b in (('l_thigh', 'r_thigh'), ('l_calf', 'r_calf'), ('l_foot', 'r_foot'), ('l_toe0', 'r_toe0'), ('l_clavicle', 'r_clavicle'), ('l_upperarm_twist', 'r_upperarm_twist'), ('l_upperarm_twist1', 'r_upperarm_twist1'),
             ('l_foretwist', 'r_foretwist'), ('l_foretwist1', 'r_foretwist1'), ('l_hand', 'r_hand'), ('l_finger0', 'r_finger0')):
    SW[L[a]] = L[b]; SW[L[b]] = L[a]
M = np.diag([-1.0, 1.0, 1.0])
def load(path):
    Ls = open(path).read().split('\n'); h = Ls[0]; n = int(h.split()[1]); nb = int(h.split()[2])
    rows = np.array([[float(x) for x in l.split()] for l in Ls[1:1 + n]]); return h, rows[:, :3], rows[:, 3:].reshape(n, nb, 3, 3)
for name in ('idle', 'walk', 'run', 'charge'):
    h, tr, D = load(MD + f'cirith_anim_{name}.txt'); n = len(D); out = np.zeros_like(D)
    for i in range(n):
        W = [None] * len(bn)
        for b in range(len(bn)): W[b] = (W[par[b]] if par[b] >= 0 else Rroot) @ D[i, b]
        Wn = [None] * len(bn)
        for b in range(len(bn)):
            delta = W[b] @ Rr[b].T; dm = M @ delta @ M; Wn[SW[b]] = dm @ Rr[SW[b]]
        for b in range(len(bn)): out[i, b] = (Wn[par[b]] if par[b] >= 0 else Rroot).T @ Wn[b]
    tr2 = tr.copy(); tr2[:, 2] *= -1
    with open(MD + f'cirith_anim_{name}_m.txt', 'w') as fh:
        fh.write(h + chr(10))
        for i in range(n): fh.write(f'{tr2[i,0]:.5f} {tr2[i,1]:.5f} {tr2[i,2]:.5f} ' + ' '.join(f'{x:.6f}' for x in out[i].ravel()) + chr(10))
    print('mirrored', name, n, 'frames')
# sword placement per pose in the right-hand frame
G = json.load(open('../grip_editor/data/grip_l.json')); Bm = np.stack([G['X'], G['Y'], G['Z']], 1)
def rxyz(d):
    a, b, c = np.radians(d); Rx = np.array([[1, 0, 0], [0, np.cos(a), -np.sin(a)], [0, np.sin(a), np.cos(a)]]); Ry = np.array([[np.cos(b), 0, np.sin(b)], [0, 1, 0], [-np.sin(b), 0, np.cos(b)]]); Rz = np.array([[np.cos(c), -np.sin(c), 0], [np.sin(c), np.cos(c), 0], [0, 0, 1]])
    return Rx @ Ry @ Rz
lines = []
for ln in open(MD + 'cirith_grip_edits.txt').read().split('\n'):
    p = ln.split()
    if len(p) < 7: continue
    t = np.array(p[1:4], float); Re = Bm @ rxyz(np.array(p[4:7], float))
    Wm = M @ (Rr[L['l_hand']] @ Re); Wm[:, 0] *= -1; Brr = Rr[L['r_hand']].T @ Wm; u, _, v = np.linalg.svd(Brr); Brr = u @ v
    tr_ = Rr[L['r_hand']].T @ M @ Rr[L['l_hand']] @ t
    lines.append(p[0] + ' ' + ' '.join(f'{x:.6f}' for x in tr_) + ' ' + ' '.join(f'{x:.6f}' for x in Brr.ravel()))
open(MD + 'cirith_grip_edits_r.txt', 'w').write(chr(10).join(lines) + chr(10)); print('right-hand grips', len(lines))
