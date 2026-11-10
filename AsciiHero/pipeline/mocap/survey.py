import retarget as R, numpy as np, sys
for fn in sys.argv[1:]:
    try:
        c=R.process(fn)
    except Exception as e:
        print(fn,'ERR',e); continue
    y=np.degrees(c['yaw']-c['yaw'][0]); v=c['fwd']; l=c['lat']
    print('%-14s n=%3d dur=%.1fs fwd med %.2f max %.2f min %.2f | lat med %.2f max %.2f min %.2f | yaw tot %.0f range[%.0f,%.0f] | L%s' % (fn,c['n'],c['n']/30,np.median(v),v.max(),v.min(),np.median(l),l.max(),l.min(),y[-1],y.min(),y.max(),R.heel_strikes(c,0)[:8]))
