import numpy as np, struct, json
def load(p=__import__('os').path.join(__import__('os').path.dirname(__file__), '../../../Unity/Assets/Resources/mm.bytes')):
    b=open(p,'rb').read(); o=4
    n,nb,nf,fps=struct.unpack_from('<4i',b,o); o+=16
    bones=[]
    for i in range(nb):
        L=b[o]; o+=1; bones.append(b[o:o+L].decode()); o+=L
    mean=np.frombuffer(b,'<f4',nf,o); o+=nf*4; scale=np.frombuffer(b,'<f4',nf,o); o+=nf*4
    nr=struct.unpack_from('<i',b,o)[0]; o+=4
    ranges=np.frombuffer(b,'<i4',nr*2,o).reshape(-1,2); o+=nr*8
    q=np.frombuffer(b,'<i2',n*nb*3,o).reshape(n,nb,3)/32767.; o+=n*nb*6
    ro=np.frombuffer(b,'<i2',n*3,o).reshape(n,3)/4000.; o+=n*6
    rm=np.frombuffer(b,'<i2',n*3,o).reshape(n,3)/2000.; o+=n*6
    con=np.frombuffer(b,'u1',n*2,o).reshape(n,2)/255.; o+=n*2
    o+=n*nf*2
    val=np.frombuffer(b,'u1',n,o); o+=n; ib=np.frombuffer(b,'u1',n,o)
    return dict(n=n,nb=nb,fps=fps,bones=bones,ranges=ranges,q=q,ro=ro,rm=rm,con=con,valid=val,idleB=ib)
if __name__=='__main__':
    D=load(); print(D['n'],D['fps'],len(D['ranges']))
    rm=D['rm']; s=np.hypot(rm[:,0],rm[:,1])
    for a,b in D['ranges']:
        ss=s[a:b]; yr=rm[a:b,2]
        print(a,b,b-a,'spd %.2f-%.2f mean %.2f'%(ss.min(),ss.max(),ss.mean()),'yaw tot %.0f'%np.degrees(yr.sum()/30), 'maxyr %.1f'%np.abs(yr).max())
