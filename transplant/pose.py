"""Evaluate a GLB skeleton + animation clip and skin vertices (numpy)."""
import numpy as np
from glbio import Glb


def qmat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def slerp(a, b, t):
    d = np.dot(a, b)
    if d < 0: b = -b; d = -d
    if d > 0.9995:
        r = a + t * (b - a); return r / np.linalg.norm(r)
    th = np.arccos(d); s = np.sin(th)
    return (np.sin((1 - t) * th) * a + np.sin(t * th) * b) / s


class Rig:
    def __init__(self, g: Glb, skin=0):
        self.g = g; j = g.j
        self.nodes = j['nodes']
        n = len(self.nodes)
        self.parent = [-1] * n
        for i, nd in enumerate(self.nodes):
            for c in nd.get('children', []): self.parent[c] = i
        self.t0 = [np.array(nd.get('translation', [0, 0, 0]), float) for nd in self.nodes]
        self.r0 = [np.array(nd.get('rotation', [0, 0, 0, 1]), float) for nd in self.nodes]
        self.s0 = [np.array(nd.get('scale', [1, 1, 1]), float) for nd in self.nodes]
        s = j['skins'][skin]
        self.joints = s['joints']
        ibm = g.accessor(s['inverseBindMatrices']).reshape(-1, 4, 4)
        self.ibm = np.transpose(ibm, (0, 2, 1))
        self.anims = []
        for a in j.get('animations', []):
            ch = []
            for c in a['channels']:
                sm = a['samplers'][c['sampler']]
                ch.append((c['target']['node'], c['target']['path'], g.accessor(sm['input']).ravel(), g.accessor(sm['output'])))
            dur = max((x[2][-1] for x in ch), default=0)
            self.anims.append(dict(name=a.get('name', ''), ch=ch, dur=float(dur)))
        # order nodes parents-first
        order = []; seen = set()
        def visit(i):
            if i in seen: return
            if self.parent[i] >= 0: visit(self.parent[i])
            seen.add(i); order.append(i)
        for i in range(n): visit(i)
        self.order = order

    def world(self, anim=None, t=0.0):
        n = len(self.nodes)
        T = [x.copy() for x in self.t0]; R = [x.copy() for x in self.r0]; S = [x.copy() for x in self.s0]
        if anim is not None:
            a = self.anims[anim]
            for node, path, tin, out in a['ch']:
                tt = t % a['dur'] if a['dur'] > 0 else 0
                k = np.searchsorted(tin, tt, side='right') - 1
                if k < 0: k = 0
                k2 = min(k + 1, len(tin) - 1)
                f = 0.0 if k2 == k else (tt - tin[k]) / max(tin[k2] - tin[k], 1e-9)
                if path == 'translation': T[node] = out[k] * (1 - f) + out[k2] * f
                elif path == 'rotation': R[node] = slerp(out[k], out[k2], f)
                elif path == 'scale': S[node] = out[k] * (1 - f) + out[k2] * f
        W = [None] * n
        for i in self.order:
            M = np.eye(4); M[:3, :3] = qmat(R[i]) * S[i]; M[:3, 3] = T[i]
            W[i] = M if self.parent[i] < 0 else W[self.parent[i]] @ M
        return W

    def skin_matrices(self, anim=None, t=0.0):
        W = self.world(anim, t)
        return np.stack([W[j] @ self.ibm[k] for k, j in enumerate(self.joints)])

    @staticmethod
    def skin(v, jnt, wgt, M):
        out = np.zeros_like(v, dtype=np.float64)
        for k in range(4):
            Mk = M[jnt[:, k]]
            out += wgt[:, k:k + 1] * (np.einsum('nij,nj->ni', Mk[:, :3, :3], v) + Mk[:, :3, 3])
        return out
