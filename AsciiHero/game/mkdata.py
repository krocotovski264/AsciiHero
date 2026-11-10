import json,base64
meta=json.load(open('build/human.json')); b64=base64.b64encode(open('build/human.bin.gz','rb').read()).decode()
open('src/data_human.js','w').write('export const HUMAN_META='+json.dumps(meta,separators=(',',':'))+';\nexport const HUMAN_B64="'+b64+'";\n')
