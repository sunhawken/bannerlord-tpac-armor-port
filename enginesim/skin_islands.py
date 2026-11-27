import numpy as np, sys
from PIL import Image
from scipy import ndimage as ndi
sys.path.insert(0,'.')
from glbio import Glb
def raster(tris, uvs, H=256, W=512):
    m=np.zeros((H,W),bool)
    for tri in tris:
        q=np.stack([uvs[tri,0]*(W-1),uvs[tri,1]*(H-1)],1)
        if (q<-2).any() or (q>max(W,H)).any(): continue
        x0,x1=int(max(q[:,0].min(),0)),int(min(q[:,0].max(),W-1)); y0,y1=int(max(q[:,1].min(),0)),int(min(q[:,1].max(),H-1))
        xs,ys=np.meshgrid(np.arange(x0,x1+1),np.arange(y0,y1+1)); a,b,c=q
        den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(den)<1e-9: continue
        w0=((b[1]-c[1])*(xs-c[0])+(c[0]-b[0])*(ys-c[1]))/den; w1=((c[1]-a[1])*(xs-c[0])+(a[0]-c[0])*(ys-c[1]))/den; w2=1-w0-w1
        k=(w0>=-0.02)&(w1>=-0.02)&(w2>=-0.02); m[ys[k],xs[k]]=True
    return m
if __name__=='__main__':
    g=Glb('out_rig/cirith_armor_rigged.glb'); d={n:dd for n,_,dd in g.primitives()}['cirith_body_armor']
    p,uv,f=d['pos'],d['uv'].astype(float),d['idx']; cz=p[f][:,:,2].mean(1); cx=np.abs(p[f][:,:,0]).mean(1)
    im=np.asarray(Image.open('../enginesim/tex/atlas1_gems.png').convert('RGBA')).astype(float); R,G,B=im[...,0],im[...,1],im[...,2]; lum=(R+G+B)/3; sat=im[...,:3].max(2)-im[...,:3].min(2)
    skin=(sat<22)&(lum>18)&(im[...,3]>0)
    for nm,sel in (('torso',(cz>1.2)&(cz<1.5)&(cx<0.19)),('upper arm x.17-.4',(cx>0.17)&(cx<0.4)&(cz>1.2)),('arm x>.4',(cx>0.4)),('lower torso',(cz<1.2)&(cx<0.19)&(cz>0.9))):
        m=raster(f[sel],uv)&skin; print(nm,'tris',sel.sum(),'skin px',m.sum(),'mean lum',lum[m].mean().round(1),'median',np.median(lum[m]).round(1))
