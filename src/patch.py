"""Apply the known SnowLuma 2.1.1 silence and target-entry fixes."""
from pathlib import Path
from datetime import datetime
import argparse
import shutil
import subprocess


def patch(text):
    replacements = {
        'var RECEIVE_STALE_AFTER_MS = 9e4;': 'var RECEIVE_STALE_AFTER_MS = 6e5;',
        'if (silentForMs < 105e3)': 'if (silentForMs < 615e3)',
        # OneKey generates the account-specific entry from index.mjs. Keep
        # that same entry in the supervised child so its monitor actually runs.
        'path.join(root, "index.mjs"),': (
            'path.join(root, path.basename(process.argv[1]) === ".maibot-target-runtime.mjs" && !version '
            '? ".maibot-target-runtime.mjs" : "index.mjs"),'
        ),
    }
    for old, new in replacements.items():
        if text.count(new) == 1 and old not in text:
            continue
        if text.count(old) != 1:
            raise ValueError('Unexpected SnowLuma source: ' + old)
        text = text.replace(old, new)
    return text


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('root', type=Path)
    args = parser.parse_args()
    root = args.root.resolve(strict=True)
    backup = root / 'data/config-backups' / ('snowluma-idle-health-' + datetime.now().strftime('%Y%m%d-%H%M%S'))
    module = root / 'data/local-lab/modules/SnowLuma'
    staged = []
    for name in ('index.mjs', '.maibot-target-runtime.mjs'):
        path = module / name
        text = path.read_text(encoding='utf-8-sig')
        updated = patch(text)
        if updated == text:
            continue
        temporary = path.with_name(name + '.check.mjs')
        temporary.write_text(updated, encoding='utf-8')
        subprocess.run([str(module / 'node.exe'), '--check', str(temporary)], check=True)
        staged.append((path, temporary))
    if staged:
        backup.mkdir(parents=True)
        for path, temporary in staged:
            shutil.copy2(path, backup / path.name)
            temporary.replace(path)
    print('Patched files:', len(staged), 'Backup:', backup)
