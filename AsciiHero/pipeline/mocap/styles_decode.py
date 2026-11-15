import base64, numpy as np
s=''.join(open(f'c{i}.txt').read().strip() for i in range(4))
b=base64.b64decode(s)
nib=[]
for x in b: nib += [x>>4, x&15]
meta='FW:1580:1740 FR:530:650 BW:3405:3525 BR:1085:1185 SW:1700:1840 SR:630:730 ID:220:370 Rushed_FR:150:260'.split()
J=['Hips','Spine2','Neck1','Head','LeftArm','LeftForeArm','LeftHand','RightArm','RightForeArm','RightHand','LeftLeg','LeftShin','LeftFoot','LeftToeBase','RightLeg','RightShin','RightFoot','RightToeBase']
p=0; clips={}
def get():
    global p
    v=nib[p]; p+=1
    if v==8:
        if p+4>len(nib): raise EOFError
        u=(nib[p]<<12)|(nib[p+1]<<8)|(nib[p+2]<<4)|nib[p+3]; p+=4
        return u-65536 if u>=32768 else u
    return v-16 if v>8 else v
for m in meta:
    k,a,bb=m.split(':'); n=int(bb)-int(a); Q=np.zeros((n,54),dtype=np.int64); got=0
    try:
        for f in range(n):
            row=np.zeros(54,dtype=np.int64)
            for j in range(54):
                if p>=len(nib): raise EOFError
                d=get()
                pred=0 if f==0 else (Q[f-1,j] if f==1 else 2*Q[f-1,j]-Q[f-2,j])
                row[j]=pred+d
            Q[f]=row; got=f+1
    except EOFError: pass
    clips[k]=(Q[:got]*0.3).reshape(-1,18,3)
    print(k, got, 'of', n, 'hips y %.1f..%.1f' % (clips[k][:,0,1].min(), clips[k][:,0,1].max()), 'head y %.1f' % clips[k][:,3,1].mean())
np.savez('PATH/TO/STYLED/positions.npz', J=np.array(J), **clips)
print('nibbles used', p, 'of', len(nib))
