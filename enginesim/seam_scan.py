import numpy as np, sys
from PIL import Image
sys.path.insert(0,'.')
from glbio import Glb
from collections import defaultdict
im=np.asarray(Image.open('../enginesim/tex/atlas1_gems.png').convert('RGB')).astype(float)
g=Glb('out_rig/cirith_armor_rigged.glb'); d={n:dd for n,_,dd in g.primitives()}['cirith_body_armor']
uv=d['uv'].astype(float); pos=d['pos']; idx=d['idx']
px=np.stack([np.clip(uv[:,0],0,1)*511, np.clip(uv[:,1],0,1)*255],1)
def samp(p,r=2):
    x,y=int(round(p[0])),int(round(p[1])); return im[max(y-r,0):y+r+1, max(x-r,0):x+r+1].reshape(-1,3).mean(0)
key=lambda i: tuple(np.round(pos[i],3))
E=defaultdict(list)
for t in idx:
    for a,b in ((0,1),(1,2),(2,0)):
        i,j=t[a],t[b]; k=tuple(sorted((key(i),key(j)))); E[k].append((i,j))
rows=[]
for k,l in E.items():
    if len(l)<2: continue
    (i1,j1),(i2,j2)=l[0],l[1]
    if key(i1)!=key(i2): i2,j2=j2,i2
    if key(i1)!=key(i2) or key(j1)!=key(j2): continue
    du=max(np.linalg.norm(px[i1]-px[i2]),np.linalg.norm(px[j1]-px[j2]))
    if du<1.5: continue
    c1=(samp(px[i1])+samp(px[j1]))/2; c2=(samp(px[i2])+samp(px[j2]))/2
    mid=(pos[i1]+pos[j1])/2; rows.append((np.abs(c1-c2).mean(),mid,c1,c2,px[i1],px[i2]))
rows.sort(key=lambda r:-r[0]); print(len(rows),'seam edges')
for r in rows[:25]: print(round(r[0],1),r[1].round(3),r[2].round(0),r[3].round(0),r[4].round(0),r[5].round(0))
import pickle; pickle.dump(rows,open('../enginesim/out/seams.pkl','wb'))
