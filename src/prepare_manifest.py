"""Capture exact known fixes and their original hashes once at deployment."""
import hashlib
import json
from pathlib import Path
import shutil

root=Path(r'C:\DeepSeek-QQ')
directory=root/'data/auto-repair'
directory.mkdir(parents=True,exist_ok=True)
manifest=directory/'manifest.json'
if manifest.exists():
    raise SystemExit('Manifest already exists; preserved')
payload=directory/'payload'
payload.mkdir(exist_ok=True)
common='data/local-lab/modules/MaiBot/.onekey/environments/<your-environment-id>/Lib/site-packages/src/common/'
entries=[
 ('data/local-lab/modules/SnowLuma/index.mjs','data/config-backups/<your-snowluma-backup>/index.mjs','snow','index.mjs'),
 ('data/local-lab/modules/SnowLuma/.maibot-target-runtime.mjs','data/config-backups/<your-snowluma-backup>/.maibot-target-runtime.mjs','snow','target.mjs'),
 (common+'runtime_loop.py','data/config-backups/<your-maibot-backup>/runtime_loop.py','core','runtime_loop.py'),
 (common+'rin_runtime_guard.py',None,'core','rin_runtime_guard.py'),
]
files=[]
for target,original,kind,name in entries:
    source=root/target
    shutil.copy2(source,payload/name)
    files.append({'target':target,'payload':'payload/'+name,'kind':kind,
        'good_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
        'allowed_originals':[hashlib.sha256((root/original).read_bytes()).hexdigest() if original else None]})
manifest.write_text(json.dumps({'files':files},indent=2),encoding='utf-8')
print('Prepared four known-good payloads and original checksums')
