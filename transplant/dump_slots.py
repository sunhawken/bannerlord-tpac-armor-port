"""dump rigged Cirith slot meshes -> binary for the C# armorpack verb.  per slot: name, n, nf, then pos,nrm,uv,jnt(u8x4),wgt(u8x4 sum 255),idx(i32)"""
import struct, sys, numpy as np
from glbio import Glb
def zup(a):  # glTF Y-up (Root rotated -90deg about X) -> Bannerlord Z-up:  (x,y,z) -> (x,-z,y)
    a = a.astype(np.float32); return np.stack([a[:, 0], -a[:, 2], a[:, 1]], 1).astype('<f4')

from pose import Rig as _Skel
_sk = _Skel(g_ := Glb('out_rig/cirith_armor_rigged.glb'))
_names = [g_.j['nodes'][i]['name'] for i in _sk.joints]; _W = _sk.world(None)
_B = {n: (lambda p: np.array([p[0], -p[2], p[1]]))(_W[_sk.joints[k]][:3, 3]) for k, n in enumerate(_names)}
ARM_D = {}
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
        ang = lambda v: np.arctan2(v[2], abs(v[0]))                                   # elevation in the x-z plane, outward = +
        if hm.sum() >= 5:
            her = pos[hm].mean(0) - pivot; bone = _B[side + '_hand'] - pivot
            d = ang(bone) - ang(her); ARM_D[side] = d                                  # measured once on the hand slot ...
        elif side in ARM_D: d = ARM_D[side]                                            # ... and applied identically to EVERY slot (body, armlets, trims). Skipping slots
        else: continue                                                                 # without hand verts left the upper arm + orange bracers 19 deg out of place.                                                      # negative = rotate down
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
angle_arms(prims['cirith_hands_armor']['pos'], prims['cirith_hands_armor']['nrm'], prims['cirith_hands_armor']['jnt'], prims['cirith_hands_armor']['wgt'])   # measure ARM_D first
TRIM = {'cirith_body_armor': 'cirith_body_trim_armor', 'cirith_legs_armor': 'cirith_legs_trim_armor', 'cirith_hands_armor': 'cirith_hands_trim_armor'}
_p = {n: d for n, _, d in g.primitives()} if False else None

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
    if n == 'cirith_body_armor' and __import__('os').environ.get('AREOLA_DECAL') == '1':
        # areola decals: a regular polar disc (3 rings x 16 + centre, radius R) per breast, projected onto the skin surface around the apex and pushed 6 mm out along the surface normal,
        # UV-mapped onto the decal tile in the atlas (96x96 at (16,800), disc r = 30 px). Skinned like the skin vertex it lands on.
        R = 0.030; push = 0.006; P_, N_, U_, J_, W_, A_, F_ = [], [], [], [], [], [], []
        pos0, nrm0, idx0, uv0 = d['pos'], d['nrm'], d['idx'], d['uv']; uvp = uv0[idx0].mean(1) * [512, 1024]; fcen = pos0[idx0].mean(1)
        import os as _os
        APEX = [np.array([0.082, -0.092, 1.345]), np.array([-0.082, -0.092, 1.345])]
        if _os.environ.get('PROBES') == '1': APEX = [np.array(v) for v in ([0.0, -0.10, 1.30], [0.0, -0.10, 1.40], [0.10, -0.09, 1.34], [-0.10, -0.09, 1.34], [0.0, -0.08, 1.15], [0.18, -0.05, 1.40])]
        for sg_i, apex in enumerate(APEX):
            sg = 1.0
            cand = [t for t, c, u in zip(idx0, fcen, uvp) if np.linalg.norm(c - apex) < 0.10 and c[1] < -0.02 and not (u[0] < 110 and u[1] > 180)]
            vs = np.unique(np.array(cand)); nz = nrm0[vs][np.linalg.norm(pos0[vs] - apex, axis=1) < 0.05].mean(0); nz /= np.linalg.norm(nz)
            t1 = np.cross(nz, [0, 0, 1.0]); t1 /= np.linalg.norm(t1); t2 = np.cross(nz, t1)
            def hit(o, dr):
                best = None
                for tri in cand:
                    A, B, C = pos0[tri]; e1 = B - A; e2 = C - A; h = np.cross(dr, e2); det = e1 @ h
                    if abs(det) < 1e-12: continue
                    s_ = o - A; u = s_ @ h / det
                    if u < 0 or u > 1: continue
                    q = np.cross(s_, e1); v = dr @ q / det
                    if v < 0 or u + v > 1: continue
                    t = e2 @ q / det
                    if best is None or t < best[0]: best = (t, tri, 1 - u - v, u, v)
                return best
            coords = [(0.0, 0.0)] + [(r * R * np.cos(2 * np.pi * k / 16), r * R * np.sin(2 * np.pi * k / 16)) for r in (0.34, 0.67, 1.0) for k in range(16)]
            base = len(P_) + len(pos0); ok = True
            for (cx, cy) in coords:
                o = apex + t1 * cx + t2 * cy + nz * 0.06; h = hit(o, -nz)
                if h is None: ok = False; break
                t, tri, w0, w1, w2 = h; pp = w0 * pos0[tri[0]] + w1 * pos0[tri[1]] + w2 * pos0[tri[2]]; nn = w0 * nrm0[tri[0]] + w1 * nrm0[tri[1]] + w2 * nrm0[tri[2]]; nn /= np.linalg.norm(nn)
                vi = tri[int(np.argmax([w0, w1, w2]))]
                P_.append(pp + push * nn); N_.append(nn); U_.append([(64.0 + cx / R * 30.0) / 512, (848.0 + cy / R * 30.0) / 1024]); J_.append(d['jnt'][vi]); W_.append(d['wgt'][vi]); A_.append(0)
            if not ok: print('  areola decal side', sg, 'FAILED to project'); continue
            for k in range(16):
                F_.append([base, base + 1 + k, base + 1 + (k + 1) % 16])
                for r in range(2):
                    a0 = base + 1 + r * 16 + k; a1 = base + 1 + r * 16 + (k + 1) % 16; b0 = a0 + 16; b1 = a1 + 16; F_ += [[a0, b0, a1], [a1, b0, b1]]
            # orient faces outward (normal . surface normal > 0)
            for fi in range(len(F_) - 49, len(F_)):
                tri = F_[fi]; pq = np.array([P_[tri[0] - len(pos0)], P_[tri[1] - len(pos0)], P_[tri[2] - len(pos0)]]); fn = np.cross(pq[1] - pq[0], pq[2] - pq[0])
                if fn @ nz < 0: F_[fi] = [tri[0], tri[2], tri[1]]
            print(f'  decal {sg_i}: disc R={R * 100:.1f} cm (diameter {2 * R * 100:.0f} cm) at apex {apex.round(3).tolist()}')
        if P_:
            d['pos'] = np.concatenate([pos0, np.array(P_)]); d['nrm'] = np.concatenate([nrm0, np.array(N_)]); d['uv'] = np.concatenate([uv0, np.array(U_)])
            d['jnt'] = np.concatenate([d['jnt'], np.array(J_)]); d['wgt'] = np.concatenate([d['wgt'], np.array(W_, dtype=d['wgt'].dtype)]); d['idx'] = np.concatenate([idx0, np.array(F_)]); d['al'] = np.concatenate([d['al'], np.zeros(len(P_), np.uint8)])
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
        # wrist sleeve: sliding the hand left a ~5.7 cm gap between the end of the forearm armor and the hand cuff. Close it with a tube of the SAME radius
        # (3.3 cm) from the forearm's distal ring to the hand's proximal ring, skinned over 4 rings forearm bone -> hand bone, so nothing is stretched or deformed.
        bp = {k: angle_arms(prims[k]['pos'], prims[k]['nrm'], prims[k]['jnt'], prims[k]['wgt'])[0] for k in ('cirith_body_armor', 'cirith_body_trim_armor')}
        bj = {k: prims[k] for k in bp}
        P, Nn, U, J, Wt, F = [], [], [], [], [], []
        for sg, side in ((1.0, 'r'), (-1.0, 'l')):
            a = np.array([sg * 0.83, 0.0, -0.55]); a /= np.linalg.norm(a)
            ids = [_names.index(side + '_foretwist'), _names.index(side + '_foretwist1')]
            fa = np.concatenate([bp[k][np.isin(np.take_along_axis(bj[k]['jnt'], np.argmax(bj[k]['wgt'], 1)[:, None], 1)[:, 0], ids) & (bp[k][:, 0] * sg > 0.3)] for k in bp])
            tip = fa[(fa @ a) > (fa @ a).max() - 0.02]; c1 = tip.mean(0)
            # texture the sleeve exactly like the forearm armor it continues: mean UV of the main-atlas forearm verts nearest the tip
            k0 = 'cirith_body_armor'; m0 = np.isin(np.take_along_axis(bj[k0]['jnt'], np.argmax(bj[k0]['wgt'], 1)[:, None], 1)[:, 0], ids) & (bp[k0][:, 0] * sg > 0.3)
            q0 = bp[k0][m0]; near = np.argsort(-(q0 @ a))[:10]; uvt = fix_uv(bj[k0]['uv'], bj[k0]['idx'])[m0][near].mean(0); uvt = np.array([0.332, 0.371 * 0.25])   # her grey skin patch (the forearm-tip UV landed on pure black)
            hm = d['pos'][:, 0] * sg > 0.3; hh = d['pos'][hm]; pr = hh[(hh @ a) < (hh @ a).min() + 0.02]; c2 = pr.mean(0)
            r1 = np.linalg.norm((tip - c1) - ((tip - c1) @ a)[:, None] * a, axis=1).mean(); r2 = np.linalg.norm((pr - c2) - ((pr - c2) @ a)[:, None] * a, axis=1).mean()
            t = c2 - c1; L = np.linalg.norm(t); t /= L
            e1 = np.cross(t, [0, 1.0, 0]); e1 /= np.linalg.norm(e1); e2 = np.cross(t, e1)
            nseg, nring = 16, 4; base = len(P) + len(d['pos']); f_id = _names.index(side + '_foretwist1'); h_id = _names.index(side + '_hand')
            for k in range(nring):
                f = k / (nring - 1); cen = c1 - t * 0.004 + (c2 + t * 0.004 - (c1 - t * 0.004)) * f; rad = (r1 * (1 - f) + r2 * f) * 1.04
                for i in range(nseg):
                    ph = 2 * np.pi * i / nseg; rv = np.cos(ph) * e1 + np.sin(ph) * e2
                    P.append(cen + rad * rv); Nn.append(rv); U.append(uvt)
                    J.append([f_id, h_id, 0, 0]); Wt.append([1 - f, f, 0, 0])
            for k in range(nring - 1):
                for i in range(nseg):
                    a0 = base + k * nseg + i; a1 = base + k * nseg + (i + 1) % nseg; b0 = a0 + nseg; b1 = a1 + nseg
                    for tri in ((a0, b0, a1), (a1, b0, b1)):
                        pn = np.cross(np.array(P[tri[1] - len(d['pos'])]) - np.array(P[tri[0] - len(d['pos'])]), np.array(P[tri[2] - len(d['pos'])]) - np.array(P[tri[0] - len(d['pos'])]))
                        F.append(tri if pn @ np.array(Nn[tri[0] - len(d['pos'])]) > 0 else (tri[0], tri[2], tri[1]))
            print(f'  wrist sleeve {side}: gap {L * 100:.1f} cm, radius {r1 * 100:.1f} -> {r2 * 100:.1f} cm')
        d['pos'] = np.concatenate([d['pos'], np.array(P)]); d['nrm'] = np.concatenate([d['nrm'], np.array(Nn)]); d['uv'] = np.concatenate([d['uv'], np.array(U)])
        d['jnt'] = np.concatenate([d['jnt'], np.array(J)]); d['wgt'] = np.concatenate([d['wgt'], np.array(Wt, dtype=d['wgt'].dtype)]); d['idx'] = np.concatenate([d['idx'], np.array(F)]); d['al'] = np.concatenate([d['al'], np.full(len(P), 255, np.uint8)])
    return d
slots = [(n, merged(n)) for n in ('cirith_body_armor', 'cirith_legs_armor', 'cirith_hands_armor', 'cirith_head_armor')]

# ---- SPARE-BONE JIGGLE RIG (driven by the CirithSim DLL): l_finger0 = both breasts, l_toe0/r_toe0 = glutes (L/R), r_finger0 = ponytail.
# Her meshes do not use the finger0 bones; the boots/legs used toe0, so toe0 -> foot first. Each cloth-alpha vertex gets spare-bone weight = alpha / alpha_max,
# its other influences are scaled by (1 - that); the weakest of the 4 slots is replaced by the spare bone.
def _spare(d, bone_ids, sa):
    jnt = d['jnt'].astype(np.int64).copy(); wgt = d['wgt'].astype(np.float64).copy()
    for i in np.where(sa > 0.004)[0]:
        b = bone_ids[i] if hasattr(bone_ids, '__len__') else bone_ids
        if b < 0: continue
        a = float(min(sa[i], 1.0)); k = int(np.argmin(wgt[i])); wgt[i, k] = 0; wgt[i] /= max(wgt[i].sum(), 1e-9); wgt[i] *= (1 - a); jnt[i, k] = b; wgt[i, k] = a
    d['jnt'] = jnt; d['wgt'] = wgt
def _nearest_alpha(src_pos, src_a, dst_pos, maxd=0.02):
    from scipy.spatial import cKDTree
    t = cKDTree(src_pos[src_a > 0]); dist, ix = t.query(dst_pos); a = src_a[src_a > 0][ix]; return np.where(dist < maxd, a, 0.0)
NB = {n: _names.index(n) for n in ('l_finger0', 'r_finger0', 'l_toe0', 'r_toe0', 'l_foot', 'r_foot')}
if __import__('os').environ.get('NOSPARE') != '1':
    for n, d in slots:
        if n in ('cirith_legs_armor',):
            for t_, f_ in (('l_toe0', 'l_foot'), ('r_toe0', 'r_foot')): d['jnt'] = np.where(d['jnt'] == NB[t_], NB[f_], d['jnt'])
        if n == 'cirith_body_armor':
            N0 = len(prims[n]['pos']); al = d['al'].astype(np.float64); sa = np.zeros(len(d['pos'])); sa[:N0] = np.clip(al[:N0] / 96.0, 0, 1)
            sa[N0:] = _nearest_alpha(d['pos'][:N0], sa[:N0], d['pos'][N0:])          # trim (gems...) follows the nearest cloth vertex
            z = d['pos'][:, 2]; x = d['pos'][:, 0]
            br = np.where(z > 1.15, NB['l_finger0'], np.where(x > 0, NB['r_toe0'], NB['l_toe0']))   # breasts z>1.15, glutes below (r = +x)
            br = np.where((z > 1.15) | (z < 1.1), br, -1)
            _spare(d, br, sa); print('  spare rig body: verts with spare weight', int((sa > 0.004).sum()))
        if n == 'cirith_head_armor':
            al = d['al'].astype(np.float64); sa = np.clip(al / al.max(), 0, 1); _spare(d, NB['r_finger0'], sa); print('  spare rig head (ponytail):', int((sa > 0.004).sum()))
            # pivot/length of the ponytail for the DLL (same formula as hang_ponytail)
            m = (d['pos'][:, 2] > 1.78) & (d['pos'][:, 1] < -0.015); root = np.array([0.0, d['pos'][m][:, 1].max(), d['pos'][m][:, 2].min()])
            open('../enginesim/CirithSim/module/ModuleData/cirith_spare.txt', 'w').write(f"pivot {root[0]:.5f} {root[1]:.5f} {root[2]:.5f}" + chr(10))
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
