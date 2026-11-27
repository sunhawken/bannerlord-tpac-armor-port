"""dump_probe.py - axis probe weapon: red bar +X (guard) 30 cm, green bar +Y (thin) 20 cm, blue bar +Z (blade) 50 cm, white cube at the origin; solid colour texture bands."""
import struct, numpy as np
from PIL import Image
def box(x0,x1,y0,y1,z0,z1,band):
    P=[];N=[];U=[];I=[]
    faces=[((1,0,0),[(x1,y0,z0),(x1,y1,z0),(x1,y1,z1),(x1,y0,z1)]),((-1,0,0),[(x0,y1,z0),(x0,y0,z0),(x0,y0,z1),(x0,y1,z1)]),
           ((0,1,0),[(x1,y1,z0),(x0,y1,z0),(x0,y1,z1),(x1,y1,z1)]),((0,-1,0),[(x0,y0,z0),(x1,y0,z0),(x1,y0,z1),(x0,y0,z1)]),
           ((0,0,1),[(x0,y0,z1),(x1,y0,z1),(x1,y1,z1),(x0,y1,z1)]),((0,0,-1),[(x0,y1,z0),(x1,y1,z0),(x1,y0,z0),(x0,y0,z0)])]
    for n,q in faces:
        b=len(P); P+=q; N+=[n]*4; U+=[(0.5,(band+0.5)*0.25)]*4; I+=[(b,b+1,b+2),(b,b+2,b+3)]
    return P,N,U,I
parts=[box(0,0.30,-0.01,0.01,-0.01,0.01,0),box(-0.01,0.01,0,0.20,-0.01,0.01,1),box(-0.01,0.01,-0.01,0.01,0,0.50,2),box(-0.03,0.03,-0.03,0.03,-0.03,0.03,3)]
P=[];N=[];U=[];I=[]
for p,n,u,i in parts:
    b=len(P); P+=p; N+=n; U+=u; I+=[(a+b,c+b,d+b) for a,c,d in i]
pos=np.array(P,'<f4'); nrm=np.array(N,'<f4'); uv=np.array(U,'<f4'); idx=np.array(I,'<i4')
with open('out_rig/probe_slots.bin','wb') as f:
    f.write(struct.pack('<i',1)); f.write(struct.pack('<i',1)); b=b'pelvis'; f.write(struct.pack('<i',len(b))+b)
    nb=b'cirith_probe'; f.write(struct.pack('<i',len(nb))+nb); f.write(struct.pack('<ii',len(pos),len(idx)))
    f.write(pos.tobytes()); f.write(nrm.tobytes()); f.write(uv.tobytes()); f.write(np.zeros((len(pos),4),np.uint8).tobytes()); f.write(np.zeros((len(pos),4),np.uint8).tobytes()); f.write(np.full(len(pos),255,np.uint8).tobytes()); f.write(idx.tobytes())
# texture: 4 horizontal bands R,G,B,white on 64x512, DXT5 container
img=np.zeros((512,64,4),np.uint8); cols=[(255,0,0),(0,255,0),(0,0,255),(255,255,255)]
for k,c in enumerate(cols): img[k*128:(k+1)*128,:,:3]=c; img[k*128:(k+1)*128,:,3]=255
Image.fromarray(img,'RGBA').save('../enginesim/tex/probe.png')
src=open('../enginesim/mk_weapon_tex.py').read().replace(r"r'C:\Users\Trey\Downloads\DFG\C_DS_NZ\textures\E_DS_NZ_R.png'","'tex/probe.png'").replace("tex/cirith_sword.dxt5","tex/cirith_probe.dxt5")
open('../enginesim/_mk_probe_tex.py','w').write(src)
print('probe verts',len(pos))
