"""Install the independently tested loop guard into one existing runtime."""
import argparse
import ast
from datetime import datetime
from pathlib import Path
import shutil

parser = argparse.ArgumentParser()
parser.add_argument('root', type=Path)
args = parser.parse_args()
root = args.root.resolve(strict=True)
candidates = list((root / 'data/local-lab/modules/MaiBot/.onekey/environments').glob('*/Lib/site-packages/src/common/runtime_loop.py'))
if len(candidates) != 1:
    raise SystemExit('Expected exactly one installed MaiBot environment')
target = candidates[0]
text = target.read_text(encoding='utf-8-sig')
anchor = '    _main_loop = loop'
if 'from src.common.rin_runtime_guard import observe' not in text:
    if text.count(anchor) != 1:
        raise SystemExit('Unexpected runtime_loop.py source')
    updated = text.replace(anchor, anchor + '\n    from src.common.rin_runtime_guard import observe\n    observe(loop)')
else:
    updated = text
guard = Path(__file__).with_name('runtime_guard.py').read_text(encoding='utf-8-sig')
ast.parse(updated)
ast.parse(guard)
backup = root / 'data/config-backups' / ('runtime-guard-' + datetime.now().strftime('%Y%m%d-%H%M%S'))
backup.mkdir(parents=True)
shutil.copy2(target, backup / target.name)
guard_target = target.with_name('rin_runtime_guard.py')
if guard_target.exists():
    shutil.copy2(guard_target, backup / guard_target.name)
guard_target.write_text(guard, encoding='utf-8')
target.write_text(updated, encoding='utf-8')
print('Installed; restart MaiBot through WebUI to activate. Backup:', backup)
