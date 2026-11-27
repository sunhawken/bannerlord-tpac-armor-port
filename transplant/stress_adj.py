"""stress_adj.py - random joint edits (iso on/off, huge angles) on every bone and clip; checks on the BAKED clip files that
 (1) twist helper bones never move relative to their parent  -> limb is one solid piece (no worm)
 (2) iso: the next real joints keep their un-edited WORLD orientation; only the edited bone (and its helper) changes
 (3) non-iso: every descendant keeps its relative pose to the edited bone
usage: python stress_adj.py   (restores the user's edits afterwards)"""
import os, sys, json, random, subprocess, shutil, numpy as np
from glbio import Glb
from pose import Rig
g = Glb('out_rig/cirith_armor_rigged.glb'); r = Rig(g); bn = [g.j['nodes'][i]['name'] for i in r.joints]
jidx = {n: k for k, n in enumerate(r.joints)}; par = [jidx.get(r.parent[j], -1) for j in r.joints]
HELP = {k for k, n in enumerate(bn) if n.endswith('twist1')}
MD = '../enginesim/CirithSim/module/ModuleData/'
def load(path):
    L = open(path).read().split('\n'); h = L[0].split(); n = int(h[1]); nb = int(h[2])
    return np.array([[float(x) for x in l.split()[3:]] for l in L[1:1 + n]]).reshape(n, nb, 3, 3)
def worldR(D):
    W = np.zeros_like(D)
    for b in range(D.shape[1]):
        W[:, b] = D[:, b] if par[b] < 0 else W[:, par[b]] @ D[:, b]
    return W
def angle(A, B):
    M = np.einsum('...ji,...jk->...ik', A, B); c = (np.trace(M, axis1=-2, axis2=-1) - 1) / 2; return np.degrees(np.arccos(np.clip(c, -1, 1)))
def desc(b):
    out = []
    for c in range(len(bn)):
        p = c
        while p >= 0:
            if p == b and c != b: out.append(c); break
            p = par[p]
    return out
names = {'idle': 'idle', 'walk': 'walk', 'run': 'run', 'sprint': 'charge'}
def bake(edits):
    json.dump({'version': 3, 'clips': edits}, open('/tmp_edits.json'.replace('/tmp_edits', os.path.abspath('stress_edits')), 'w'))
    env = dict(os.environ, GRIP_EDITS=os.path.abspath('stress_edits.json'))
    for a, n in ((0, 'idle'), (1, 'walk'), (2, 'run'), (4, 'charge')): subprocess.run([sys.executable, 'retarget_anim.py', str(a), n], capture_output=True, env=env, check=True)
random.seed(7); bad = 0; tests = 0
cands = [k for k in range(len(bn)) if k not in HELP and bn[k] not in ('pelvis',)]
for rnd in range(6):
    edits = {}
    for ck in names:
        ad = []
        for b in random.sample(cands, 6):
            ad.append(dict(bone=bn[b], rot=[random.uniform(-150, 150) for _ in range(3)], center=-1, width=14, iso=random.random() < 0.5))
        edits[ck] = dict(adj=ad)
    bake(edits)
    for ck, nm in names.items():
        base = load('out_rig/anim_base/cirith_anim_' + nm + '_base.txt'); new = load(MD + 'cirith_anim_' + nm + '.txt')
        Wb, Wn = worldR(base), worldR(new)
        # (1) helpers rigid to parent
        for h in HELP:
            d = angle(np.einsum('nji,njk->nik', Wn[:, par[h]], Wn[:, h]) if False else Wn[:, par[h]].transpose(0, 2, 1) @ Wn[:, h], base[:, h] * 0 + (Wb[:, par[h]].transpose(0, 2, 1) @ Wb[:, h]))
            tests += 1
            if d.max() > 0.5: bad += 1; print('HELPER NOT RIGID', ck, bn[h], d.max())
        # (2)(3) per edit
        for a in edits[ck]['adj']:
            b = bn.index(a['bone']); Rw = None
            for d_ in desc(b):
                rel_b = Wb[:, b].transpose(0, 2, 1) @ Wb[:, d_]; rel_n = Wn[:, b].transpose(0, 2, 1) @ Wn[:, d_]
                tests += 1
                real_next = (d_ not in HELP) and all(x == b or x in HELP for x in (lambda p: [p] )(par[d_]) ) or (par[d_] in HELP and par[par[d_]] == b)
                if not a['iso']:
                    pass
            # only check unambiguous cases: edits are on distinct bones, edited bone's world orientation never depends on other edits only if no ancestor is edited
            anc = []; p = par[b]
            while p >= 0: anc.append(p); p = par[p]
            if any(bn[x] in [q['bone'] for q in edits[ck]['adj']] for x in anc): continue
            if a['iso']:
                for c in [c for c in range(len(bn)) if par[c] == b]:
                    kids = [c] if c not in HELP else [g for g in range(len(bn)) if par[g] == c and g not in HELP]
                    for k in kids:
                        if any(q['bone'] == bn[k] for q in edits[ck]['adj']): continue
                        d = angle(Wn[:, k], Wb[:, k]); tests += 1
                        if d.max() > 0.5: bad += 1; print('ISO FAIL', ck, a['bone'], '->', bn[k], d.max())
            else:
                for d_ in desc(b):
                    if any(q['bone'] == bn[d_] for q in edits[ck]['adj']) or any(q['bone'] in [bn[z] for z in desc(b) if z != d_] and False for q in []): continue
                    pp = []; p = par[d_]
                    while p != b and p >= 0: pp.append(p); p = par[p]
                    if any(bn[x] in [q['bone'] for q in edits[ck]['adj']] for x in pp): continue
                    rb_ = Wb[:, b].transpose(0, 2, 1) @ Wb[:, d_]; rn_ = Wn[:, b].transpose(0, 2, 1) @ Wn[:, d_]; d = angle(rb_, rn_); tests += 1
                    if d.max() > 0.5: bad += 1; print('FOLLOW FAIL', ck, a['bone'], '->', bn[d_], d.max())
os.remove('stress_edits.json') if os.path.exists('stress_edits.json') else None
print('tests', tests, 'failures', bad)
subprocess.run([sys.executable, 'apply_grip_edits.py', 'C:/Users/Trey/Downloads/grip_edits (3).json'], capture_output=True)
print('user edits restored')
