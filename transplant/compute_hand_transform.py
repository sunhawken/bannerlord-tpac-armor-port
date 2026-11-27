"""compute_hand_transform.py - the rigid transform T that dump_slots.py applies to the LEFT hand (arm angle about the shoulder + grip-fit slide) when it builds the game mesh,
plus the l_hand bind frame. Written to ../grip_editor/data/hand_transform.json (used to carry sword edits made on the gallery model over to the game mesh)."""
import runpy, io, contextlib, json, numpy as np
with contextlib.redirect_stdout(io.StringIO()): g = runpy.run_path('dump_slots.py')
d = float(g['ARM_D']['l']); phi = d * 1.0 * (-1)                       # angle_arms: phi = d * a * (1 if right else -1), a = 1 for hand vertices
B = g['_B']; pivot = np.array(B['l_upperarm_twist'], float); ob = np.array(B['l_hand'], float)
sg = -1.0; av = np.array([sg * 0.83, 0, -0.55]); av /= np.linalg.norm(av); delta = 0.052 * av + np.array([-0.009 * sg, 0.006, -0.013])
c, s = np.cos(phi), np.sin(phi); Rt = np.array([[c, 0, -s], [0, 1, 0], [s, 0, c]]); ct = pivot - Rt @ pivot + delta
# l_hand bind frame axes (model space), from the engine rest pose (identical to the glb skeleton)
Rb = np.array([[-0.7243795, 0.6873462, -0.053193], [0.05395883, -0.02039402, -0.9983349], [-0.6872864, -0.7260436, -0.02231539]])
json.dump(dict(Rt=Rt.tolist(), ct=ct.tolist(), Rb=Rb.tolist(), ob=ob.tolist(), phi_deg=float(np.degrees(phi)), delta=delta.tolist()), open('../grip_editor/data/hand_transform.json', 'w'), indent=1)
print('left-hand transform: arm rotation %.2f deg about the shoulder, slide %s m' % (np.degrees(phi), np.round(delta, 4)))
