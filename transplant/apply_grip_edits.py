"""apply_grip_edits.py [grip_edits.json] - turns the sword grip editor's file into the DLL's ModuleData/cirith_grip_edits.txt (idle walk run sprint: tx ty tz [m, hand-bone axes] rx ry rz [deg, sword axes]).
Default input: ../grip_editor/grip_edits.json (or the newest grip_edits*.json in Downloads)."""
import json, os, sys, glob
src = sys.argv[1] if len(sys.argv) > 1 else '../grip_editor/grip_edits.json'
if not os.path.exists(src):
    c = sorted(glob.glob(os.path.expanduser('~/Downloads/grip_edits*.json')), key=os.path.getmtime)
    if c: src = c[-1]
j = json.load(open(src)); out = []
for clip in ('idle', 'walk', 'run', 'sprint'):
    e = j.get('clips', {}).get(clip, {}); p = e.get('pos', [0, 0, 0]); r = e.get('rot', [0, 0, 0])
    if any(p) or any(r): out.append(f"{clip} {p[0]/100:.5f} {p[1]/100:.5f} {p[2]/100:.5f} {r[0]:.3f} {r[1]:.3f} {r[2]:.3f}")
dst = '../enginesim/CirithSim/module/ModuleData/cirith_grip_edits.txt'
open(dst, 'w').write('\n'.join(out) + ('\n' if out else ''))
print('read', src, '->', dst, ':', len(out), 'pose(s) edited')
