"""dump rigged Cirith slot meshes -> binary for the C# armorpack verb.  per slot: name, n, nf, then pos,nrm,uv,jnt(u8x4),wgt(u8x4 sum 255),idx(i32)"""
import struct, sys, numpy as np
from glbio import Glb
def zup(a):  # glTF Y-up (Root rotated -90deg about X) -> Bannerlord Z-up:  (x,y,z) -> (x,-z,y)
    a = a.astype(np.float32); return np.stack([a[:, 0], -a[:, 2], a[:, 1]], 1).astype('<f4')

from pose import Rig as _Skel
_sk = _Skel(g_ := Glb('out_rig/cirith_armor_rigged.glb'))
_names = [g_.j['nodes'][i]['name'] for i in _sk.joints]; _W = _sk.world(None)
_B = {n: (lambda p: np.array([p[0], -p[2], p[1]]))(_W[_sk.joints[k]][:3, 3]) for k, n in enumerate(_names)}
ARM = {'r': ['r_upperarm_twist', 'r_upperarm_twist1', 'r_foretwist', 'r_foretwist1', 'r_hand', 'r_finger0'], 'l': ['l_upperarm_twist', 'l_upperarm_twist1', 'l_foretwist', 'l_foretwist1', 'l_hand', 'l_finger0']}
def angle_arms(pos, nrm, jnt, wgt, log=False):
    """Her arms are modelled horizontal (T-pose); Bannerlord's skeleton rest has them angled ~27 deg down from the chest to the hands. Rotate every arm vertex about the
    shoulder (pivot = upperarm bone) by the angle that lines her arm chain up with the bones; amount = the vertex's arm-bone weight, so the chest stays put and the
    shoulder blends smoothly. Hands then sit on the hand bones, so weapons grip correctly."""
    pos = pos.astype(np.float64).copy(); nrm = nrm.astype(np.float64).copy()
    for side in ('r', 'l'):
        ids = [_names.index(n) for n in ARM[side]]; pivot = _B[ARM[side][0]]
        a = np.zeros(len(pos))
        for k in range(4): a += np.where(np.isin(jnt[:, k], ids), wgt[:, k], 0)
        a += 0.5 * np.where(np.isin(jnt, [_names.index(side + '_clavicle')]), wgt, 0).sum(1)
        a = np.clip(a, 0, 1)
        # target angle: bone chain pivot->hand vs her hand mesh centre (hand-dominant verts of this side)
        dom = np.take_along_axis(jnt, np.argmax(wgt, 1)[:, None], 1)[:, 0]; hm = np.isin(dom, [_names.index(side + '_hand'), _names.index(side + '_finger0')]) & ((pos[:, 0] > 0) == (side == 'r'))
        if hm.sum() < 5: continue
        her = pos[hm].mean(0) - pivot; bone = _B[side + '_hand'] - pivot
        ang = lambda v: np.arctan2(v[2], abs(v[0]))                                   # elevation in the x-z plane, outward = +
        d = ang(bone) - ang(her)                                                      # negative = rotate down
        th = d * a * (1 if side == 'r' else -1)                                       # CCW angle in (x,z) for the right side is -d... handled below
        for arr in (pos, nrm):
            rel = arr - (pivot if arr is pos else 0)
            c, sn = np.cos(th), np.sin(th)
            sg = 1.0 if side == 'r' else -1.0
            x = rel[:, 0] * c - sg * rel[:, 2] * sn * 1.0 * 0 + 0                      # placeholder, replaced below
        # explicit rotation in the x-z plane that lowers the hand: right side hand has dx>0 -> rotate clockwise by |d|; left side dx<0 -> counter-clockwise
        phi = -abs(d) * a * (1 if side == 'r' else -1) * np.sign(-d if d != 0 else 1) * -1
        phi = d * a * (1 if side == 'r' else -1)
        for arr, off in ((pos, pivot), (nrm, 0.0)):
            rel = arr - off; c, sn = np.cos(phi), np.sin(phi)
            nx = rel[:, 0] * c - rel[:, 2] * sn; nz = rel[:, 0] * sn + rel[:, 2] * c
            arr[:, 0] = nx + (off[0] if arr is pos else 0); arr[:, 2] = nz + (off[2] if arr is pos else 0)
        if log:
            hm2 = hm; print(f'  arm {side}: pivot {pivot.round(3)}, rotate {np.degrees(d):.1f} deg, hand centre {(pos[hm2].mean(0)).round(3)} vs hand bone {_B[side + "_hand"].round(3)} -> offset {np.linalg.norm(pos[hm2].mean(0) - _B[side + "_hand"]) * 100:.1f} cm')
    return pos, nrm

def hang_ponytail(pos, nrm):
    """Her ponytail rests as a rope pointing up-and-back (viewer cloth sim drops it). The engine has no cloth here, so bake a hanging pose:
    keep the root where it is, bend the rope's centreline progressively down (parallel-transport the cross-sections), nothing else moves."""
    pos = pos.astype(np.float64).copy(); nrm = nrm.astype(np.float64).copy()
    m = (pos[:, 2] > 1.78) & (pos[:, 1] < -0.015)
    if m.sum() < 20: return pos, nrm
    root = np.array([0.0, pos[m][:, 1].max(), pos[m][:, 2].min()]); tip = pos[m][np.argmax(np.linalg.norm(pos[m] - root, axis=1))]
    t0 = (tip - root); L = np.linalg.norm(t0); t0 /= L
    s_ = (pos - root) @ t0                                   # distance along the old axis
    idx = np.where(m)[0]; s = np.clip(s_[idx], 0, None)
    # old centreline (mean of verts per bin), new centreline by integrating the new tangent
    nb = 40; edges = np.linspace(0, L * 1.001, nb + 1); cen = np.zeros((nb + 1, 3)); cen[0] = root
    def tang(u):                                              # u = s/L in 0..1 -> new tangent
        w = np.clip(u / 0.45, 0, 1); w = w * w * (3 - 2 * w)
        t1 = np.array([0.0, -0.12, -1.0]); t1 /= np.linalg.norm(t1)
        t = (1 - w) * t0 + w * t1; return t / np.linalg.norm(t)
    new_c = [root.copy()]; ds = L / nb
    for k in range(nb): new_c.append(new_c[-1] + tang((k + 0.5) / nb) * ds)
    new_c = np.array(new_c)
    old_c = root + np.outer(np.linspace(0, L, nb + 1), t0)   # straight old axis (rope is ~straight)
    # rope isn't exactly on the axis -> offset each vertex relative to the old axis, carried by the rotation that maps t0 -> tang(u)
    def rot(a, b):
        v = np.cross(a, b); c = float(a @ b)
        if np.linalg.norm(v) < 1e-9: return np.eye(3) if c > 0 else -np.eye(3)
        K = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]]); return np.eye(3) + K + K @ K / (1 + c)
    for i in idx:
        u = float(np.clip(s_[i] / L, 0, 1)); f = u * nb; k = min(int(f), nb - 1); fr = f - k
        c_old = old_c[k] * (1 - fr) + old_c[k + 1] * fr; c_new = new_c[k] * (1 - fr) + new_c[k + 1] * fr
        R = rot(t0, tang(u)); pos[i] = c_new + R @ (pos[i] - c_old); nrm[i] = R @ nrm[i]
    return pos, nrm

g = Glb('out_rig/cirith_armor_rigged.glb')
prims = {n: d for n, _, d in g.primitives()}
TRIM = {'cirith_body_armor': 'cirith_body_trim_armor', 'cirith_legs_armor': 'cirith_legs_trim_armor', 'cirith_hands_armor': 'cirith_hands_trim_armor'}
def fix_uv(uv, idx):
    """a few vertices carry garbage UVs (u=-71, v=117...). The viewer's REPEAT sampler hides them; in the merged atlas they smear every colour across their triangles.
    Replace each by the mean UV of the in-range vertices that share a triangle with it."""
    uv = uv.astype(np.float64).copy(); bad = ((uv < -0.02) | (uv > 1.02)).any(1)
    for i in np.where(bad)[0]:
        nb = np.unique(idx[(idx == i).any(1)]); nb = nb[~bad[nb]]
        uv[i] = uv[nb].mean(0) if len(nb) else np.clip(uv[i], 0, 1)
    return np.clip(uv, 0.0, 1.0)

def merged(n):
    import os
    hands = (n == 'cirith_hands_armor') and os.environ.get('NATIVE_HANDS') == '1'      # off by default: HER converted hands are used (native Bannerlord hands were a rejected experiment)
    if hands:   # replace her mitten-shaped hands by Bannerlord's own finger hands (already on the skeleton's A-pose), skinned to her dark-grey skin patch of the atlas
        h = prims['hands_female_a']; d = dict(pos=h['pos'].astype(np.float64), nrm=h['nrm'].astype(np.float64), jnt=h['jnt'], wgt=h['wgt'], idx=h['idx'])
        uv0 = h['uv'].astype(np.float64); uv0 = uv0 - np.floor(uv0)
        d['uv'] = np.stack([0.293 + uv0[:, 0] * 0.078, (0.3125 + uv0[:, 1] * 0.117) * 0.25], 1); d['al'] = np.full(len(d['pos']), 255, np.uint8)
    else:
        d = dict(prims[n]); d['pos'], d['nrm'] = angle_arms(d['pos'], d['nrm'], d['jnt'], d['wgt'])
        d['uv'] = fix_uv(d['uv'], d['idx'])
        d['uv'][:, 1] = d['uv'][:, 1] * 0.25          # main atlas = top quarter of the 512x1024 combined atlas
        col = d.get('col'); d['al'] = np.full(len(d['pos']), 255, np.uint8) if col is None else (np.clip(np.asarray(col)[:, 3] * (255.0 if np.asarray(col).dtype.kind == 'f' else 1.0), 0, 255)).astype(np.uint8)
    t = TRIM.get(n)
    import os
    if t and n == 'cirith_hands_armor' and os.environ.get('NOTRIM_HANDS') == '1': t = None
    if t:
        e = dict(prims[t]); e['pos'], e['nrm'] = angle_arms(e['pos'], e['nrm'], e['jnt'], e['wgt'])
        off = len(d['pos']); uv = e['uv'].astype(np.float64).copy(); uv[:, 1] = 272.0 / 1024 + uv[:, 1] * 0.5            # trim atlas (alpha-masked) = next half
        for k in ('pos', 'nrm', 'jnt', 'wgt'): d[k] = np.concatenate([d[k], e[k]])
        d['uv'] = np.concatenate([d['uv'], uv]); d['idx'] = np.concatenate([d['idx'], e['idx'] + off]); d['al'] = np.concatenate([d['al'], np.full(len(e['pos']), 0 if d['al'].min() < 255 else 255, np.uint8)])
    if n == 'cirith_hands_armor' and not hands:
        # grip fit: Bannerlord puts the weapon in the palm of ITS hand; her hand sat ~5 cm closer to the wrist (floating above the grab point).
        # Slide each hand along the arm direction so its proximal end matches the native hand's (measured against hands_female_a), plus the small perpendicular offset.
        for sg in (1.0, -1.0):
            m = d['pos'][:, 0] * sg > 0.3
            a = np.array([sg * 0.83, 0.0, -0.55]); a /= np.linalg.norm(a)
            delta = 0.052 * a + np.array([-0.009 * sg, 0.006, -0.013])
            d['pos'][m] += delta
        print('  grip fit: hands slid ~5 cm along the arm toward the grip point')
    return d
slots = [(n, merged(n)) for n in ('cirith_body_armor', 'cirith_legs_armor', 'cirith_hands_armor', 'cirith_head_armor')]
with open('out_rig/slots.bin', 'wb') as f:
    sk = g.j['skins'][0]; jn = [g.j['nodes'][i]['name'] for i in sk['joints']]
    f.write(struct.pack('<i', len(slots)))
    f.write(struct.pack('<i', len(jn)))
    for x in jn: b = x.encode(); f.write(struct.pack('<i', len(b)) + b)
    for n, d in slots:
        nb = n.encode(); f.write(struct.pack('<i', len(nb)) + nb)
        if n == 'cirith_head_armor': d['pos'], d['nrm'] = hang_ponytail(d['pos'], d['nrm'])
        N = len(d['pos']); F = len(d['idx']); f.write(struct.pack('<ii', N, F))
        f.write(d['pos'].astype('<f4').tobytes()); f.write(d['nrm'].astype('<f4').tobytes()); f.write(d['uv'].astype('<f4').tobytes())
        w = d['wgt'].astype(np.float64); w = w / np.maximum(w.sum(1, keepdims=True), 1e-9)
        q = np.floor(w * 255 + 0.5).astype(np.int32); q[np.arange(N), q.argmax(1)] += 255 - q.sum(1)
        al = d['al']
        f.write(d['jnt'].astype(np.uint8).tobytes()); f.write(q.astype(np.uint8).tobytes()); f.write(al.tobytes()); f.write(d['idx'].astype('<i4').tobytes())
        print(n, N, F, 'alpha min/max/mean', int(al.min()), int(al.max()), round(float(al.mean()), 1))
