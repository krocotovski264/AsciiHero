set -e
cd /home/claude/game3d
npx esbuild src/main.js --bundle --minify --format=iife --outfile=build/bundle.js --log-level=warning
python3 - <<'P'
t=open('template.html').read(); b=open('build/bundle.js').read().replace('</script','<\\/script')
open('build/index.html','w').write(t.replace('/*__BUNDLE__*/',b))
P
ls -la build/index.html
