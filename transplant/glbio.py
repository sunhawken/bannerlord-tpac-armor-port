"""Minimal GLB reader/writer helpers (numpy) for the armor transplant tools."""
import json, struct, io
import numpy as np
from PIL import Image

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


class Glb:
    def __init__(self, path):
        d = open(path, 'rb').read()
        jl = struct.unpack('<I', d[12:16])[0]
        self.j = json.loads(d[20:20 + jl])
        o = 20 + jl
        bl = struct.unpack('<I', d[o:o + 4])[0]
        self.bin = bytearray(d[o + 8:o + 8 + bl])
        self.path = path

    def accessor(self, i):
        a = self.j['accessors'][i]; bv = self.j['bufferViews'][a['bufferView']]
        n = NC[a['type']]; dt = CT[a['componentType']]
        off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
        stride = bv.get('byteStride')
        cnt = a['count']
        if stride and stride != n * np.dtype(dt).itemsize:
            raw = np.frombuffer(self.bin, dtype=np.uint8, count=stride * cnt, offset=off).reshape(cnt, stride)
            arr = np.ascontiguousarray(raw[:, :n * np.dtype(dt).itemsize]).view(dt).reshape(cnt, n)
        else:
            arr = np.frombuffer(self.bin, dtype=dt, count=cnt * n, offset=off).reshape(cnt, n)
        if a.get('normalized') and dt != np.float32:
            arr = arr.astype(np.float32) / np.iinfo(dt).max
        return arr.copy()

    def image(self, i):
        im = self.j['images'][i]
        if 'bufferView' in im:
            bv = self.j['bufferViews'][im['bufferView']]
            off = bv.get('byteOffset', 0)
            return Image.open(io.BytesIO(bytes(self.bin[off:off + bv['byteLength']]))).convert('RGBA')
        return Image.open(im['uri']).convert('RGBA')

    def material_image(self, mi):
        m = self.j['materials'][mi]
        t = m.get('pbrMetallicRoughness', {}).get('baseColorTexture')
        if t is None: return None
        return self.image(self.j['textures'][t['index']]['source'])

    # ---- skeleton
    def skin_info(self, si=0):
        s = self.j['skins'][si]
        joints = s['joints']
        ibm = self.accessor(s['inverseBindMatrices']).reshape(-1, 4, 4)  # column-major flat -> numpy [i][col][row]
        ibm = np.transpose(ibm, (0, 2, 1))  # now proper column-vector matrices
        names = [self.j['nodes'][n].get('name', str(n)) for n in joints]
        world = np.linalg.inv(ibm)  # joint world (bind) matrices
        pos = world[:, :3, 3]
        return names, joints, ibm, world, pos

    def primitives(self):
        """yield (mesh_name, prim_index, dict) for every primitive"""
        for m in self.j['meshes']:
            for pi, p in enumerate(m['primitives']):
                a = p['attributes']
                d = dict(pos=self.accessor(a['POSITION']), idx=self.accessor(p['indices']).reshape(-1, 3).astype(np.int64),
                         mat=p.get('material'))
                if 'NORMAL' in a: d['nrm'] = self.accessor(a['NORMAL'])
                if 'TEXCOORD_0' in a: d['uv'] = self.accessor(a['TEXCOORD_0'])
                if 'JOINTS_0' in a: d['jnt'] = self.accessor(a['JOINTS_0']).astype(np.int64)
                if 'WEIGHTS_0' in a: d['wgt'] = self.accessor(a['WEIGHTS_0'])
                if 'COLOR_0' in a: d['col'] = self.accessor(a['COLOR_0'])
                yield m['name'], pi, d


class Builder:
    """append-only editor for an existing Glb"""
    def __init__(self, g: Glb):
        self.g = g; j = g.j
        for k in ('accessors', 'bufferViews', 'images', 'textures', 'materials', 'meshes', 'nodes'):
            j.setdefault(k, [])

    def _pad(self):
        while len(self.g.bin) % 4: self.g.bin.append(0)

    def view(self, data: bytes, target=None):
        self._pad(); off = len(self.g.bin); self.g.bin.extend(data)
        bv = {'buffer': 0, 'byteOffset': off, 'byteLength': len(data)}
        if target: bv['target'] = target
        self.g.j['bufferViews'].append(bv); return len(self.g.j['bufferViews']) - 1

    def acc(self, arr, ctype, typ, target=None, minmax=False):
        arr = np.ascontiguousarray(arr)
        n = NC[typ]
        a = {'bufferView': self.view(arr.tobytes(), target), 'componentType': ctype, 'count': int(arr.size // n), 'type': typ}
        if minmax:
            r = arr.reshape(-1, n); a['min'] = r.min(0).astype(float).tolist(); a['max'] = r.max(0).astype(float).tolist()
        self.g.j['accessors'].append(a); return len(self.g.j['accessors']) - 1

    def image(self, im: Image.Image, name):
        b = io.BytesIO(); im.save(b, 'PNG')
        j = self.g.j
        j['images'].append({'bufferView': self.view(b.getvalue()), 'mimeType': 'image/png', 'name': name})
        j.setdefault('samplers', [{'magFilter': 9729, 'minFilter': 9987, 'wrapS': 10497, 'wrapT': 10497}])
        j['textures'].append({'source': len(j['images']) - 1, 'sampler': 0})
        return len(j['textures']) - 1

    def save(self, path):
        j = self.g.j; self._pad()
        j['buffers'] = [{'byteLength': len(self.g.bin)}]
        js = json.dumps(j, separators=(',', ':')).encode()
        js += b' ' * ((4 - len(js) % 4) % 4)
        bn = bytes(self.g.bin)
        with open(path, 'wb') as f:
            f.write(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(bn)))
            f.write(struct.pack('<II', len(js), 0x4E4F534A)); f.write(js)
            f.write(struct.pack('<II', len(bn), 0x004E4942)); f.write(bn)
