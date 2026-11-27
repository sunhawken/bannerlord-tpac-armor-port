"""Export Bannerlord's own cloth collision capsules (Native/ModuleData/cloth_bodies.xml, human_skeleton bodies used by capes/cloaks/heads)
to gallery/cloth_colliders.json so the viewer's cloth preview collides with the same shapes the game uses.
Capsule = starts at frame position, runs `length` along the frame's first axis, radius r0 -> r1, attached to a bone (game space, metres, Z-up)."""
import json, os
import xml.etree.ElementTree as ET
SRC = r'T:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\Native\ModuleData\cloth_bodies.xml'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'gallery', 'cloth_colliders.json')
WANT = ('cape_body', 'detailed_head')
root = ET.parse(SRC).getroot()
caps = []
for body in root.findall('body'):
    if body.get('name') not in WANT or body.get('owner_skeleton') != 'human_skeleton': continue
    for c in body.iter('capsule'):
        fr = [float(x) for x in c.get('frame').split(',')]
        L = float(c.get('length')); pts = c.findall('./points/point')
        if len(pts) < 2: continue
        bone = None
        for b in pts[0].iter('bone'): bone = b.get('name'); break
        if not bone: continue
        ax = fr[0:3]; p0 = fr[12:15]; p1 = [p0[i] + ax[i] * L for i in range(3)]
        caps.append(dict(set=body.get('name'), name=c.get('name'), bone=bone, p0=p0, p1=p1, r0=float(pts[0].get('radius')), r1=float(pts[1].get('radius'))))
json.dump(caps, open(OUT, 'w'), indent=1)
print(len(caps), 'capsules ->', os.path.abspath(OUT))
