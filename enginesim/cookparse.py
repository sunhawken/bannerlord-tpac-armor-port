import struct, sys
def parse(path, verbose=False):
    b=open(path,'rb').read(); n=len(b)//4; u=list(struct.unpack('<%dI'%n,b[:n*4])); f=list(struct.unpack('<%df'%n,b[:n*4]))
    S=[]  # (name, start_word, count_word_value, nwords)
    P,E,V,K,w4,w5=u[:6]; S.append(('header',0,6)); i=6
    S.append(('aabb',i,8)); i+=8
    assert u[i]==E; S.append(('restlen',i,1+E)); i+=1+E
    assert u[i]==E; S.append(('edges',i,1+E)); i+=1+E
    T=u[i]; S.append(('sets',i,1+T*8)); i+=1+T*8
    nb=u[i]; S.append(('batches2',i,1+2*nb)); i+=1+2*nb
    names=['A_2V','B','C_perm','D_quads','E_free','F','G_bytes','H_vec4','I']
    for nm in names:
        c=u[i]
        # byte-count arrays are in bytes (G): count is bytes, words = c/4
        words = c//4 if nm=='G_bytes' else c
        S.append((nm,i,1+words)); i+=1+words
        if i>=n: break
    S.append(('rest',i,n-i))
    return u,f,S,(P,E,V,K,w4,w5,T,nb)
if __name__=='__main__':
    for p in sys.argv[1:]:
        u,f,S,h=parse(p); print(p,'hdr',h,'words',len(u))
        for nm,st,ln in S: print(f'   {nm:9s} start {st:5d} len {ln:5d}  first {u[st:st+4]}')
