"""apply_grip_edits.py [grip_edits.json] - turns the sword grip editor's file into the DLL's ModuleData/cirith_grip_edits.txt (idle walk run sprint: tx ty tz [m, hand-bone axes] rx ry rz [deg, sword axes]).
version 2 files were made on the ORIGINAL gallery model: they are carried onto the game mesh first (the game's hand is rotated 19.2 deg about the shoulder and slid ~5 cm, see compute_hand_transform.py).
version 3 files were made on the exact game mesh and are used as they are.  Also writes grip_editor/data/start_edits.json (v3) = what the editor opens with."""
import json, os, sys, glob
import numpy as np
src = sys.argv[1] if len(sys.argv) > 1 else '../grip_editor/grip_edits.json'
if not os.path.exists(src):
    c = sorted(glob.glob(os.path.expanduser('~/Downloads/grip_edits*.json')), key=os.path.getmtime)
    if c: src = c[-1]
j = json.load(open(src)); ver = j.get('version', 2)
G = json.load(open('../grip_editor/data/grip_l.json')); Bm = np.stack([G['X'], G['Y'], G['Z']], 1)
T = json.load(open('../grip_editor/data/hand_transform.json')); Rt = np.array(T['Rt']); ct = np.array(T['ct']); Rb = np.array(T['Rb']); ob = np.array(T['ob'])
def rxyz(d):
    a, b, c = np.radians(d); Rx = np.array([[1, 0, 0], [0, np.cos(a), -np.sin(a)], [0, np.sin(a), np.cos(a)]]); Ry = np.array([[np.cos(b), 0, np.sin(b)], [0, 1, 0], [-np.sin(b), 0, np.cos(b)]]); Rz = np.array([[np.cos(c), -np.sin(c), 0], [np.sin(c), np.cos(c), 0], [0, 0, 1]])
    return Rx @ Ry @ Rz
def euler(R):
    b = np.arcsin(np.clip(R[0, 2], -1, 1)); a = np.arctan2(-R[1, 2], R[2, 2]); c = np.arctan2(-R[0, 1], R[0, 0]); return np.degrees([a, b, c])
def carry(p_cm, r_deg):
    Rold = Bm @ rxyz(r_deg); told = np.array(p_cm) / 100.0
    Rnew = Rb.T @ Rt @ Rb @ Rold                                                  # the fist moved by T in the bind pose, the sword moves with it
    tnew = Rb.T @ (Rt @ (ob + Rb @ told) + ct - ob)
    return (tnew * 100).tolist(), euler(Bm.T @ Rnew).tolist()
out = []; v3 = {'version': 3, 'note': 'made on the exact game mesh (carried from a version 2 file)' if ver == 2 else 'version 3', 'clips': {}}
for clip in ('idle', 'walk', 'run', 'sprint'):
    e = j.get('clips', {}).get(clip, {}); p = e.get('pos', [0, 0, 0]); r = e.get('rot', [0, 0, 0])
    if ver == 2 and (any(p) or any(r)): p, r = carry(p, r)
    if any(abs(x) > 1e-6 for x in list(p) + list(r)):
        out.append(f"{clip} {p[0]/100:.5f} {p[1]/100:.5f} {p[2]/100:.5f} {r[0]:.3f} {r[1]:.3f} {r[2]:.3f}"); v3['clips'][clip] = dict(pos=[round(x, 2) for x in p], rot=[round(x, 1) for x in r])
dst = '../enginesim/CirithSim/module/ModuleData/cirith_grip_edits.txt'
open(dst, 'w').write('\n'.join(out) + ('\n' if out else '')); json.dump(v3, open('../grip_editor/data/start_edits.json', 'w'), indent=1)
print('read', src, '(version', ver, ') ->', dst, ':', len(out), 'pose(s)'); print(open(dst).read())
