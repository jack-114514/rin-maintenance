"""Bounded Windows QQ bot repair. No account passwords or remote credentials."""
import argparse
import asyncio
from datetime import datetime, timezone
import hashlib
import json
import logging
from logging.handlers import RotatingFileHandler
import os
from pathlib import Path
import subprocess
import time


def read_json(path, default=None):
    try:
        return json.loads(Path(path).read_text(encoding='utf-8-sig'))
    except FileNotFoundError:
        return default


def write_json(path, data):
    path = Path(path)
    temp = path.with_suffix('.tmp')
    temp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    temp.replace(path)


def decide(snapshot, state, now):
    """Three consecutive bad samples; one action, 10m cooldown, 3/hour."""
    faults = []
    if not snapshot['core_ok']:
        faults.append('core')
    if snapshot['qq'] in ('unreachable', 'degraded'):
        faults.append('snow')
    for kind in ('core', 'snow'):
        state[kind + '_failures'] = state.get(kind + '_failures', 0) + 1 if kind in faults else 0
    state['history'] = [t for t in state.get('history', []) if 0 <= now-t < 3600]
    if snapshot['startup']:
        state['core_failures'] = 0
    if len(state['history']) >= 3:
        return None, 'hourly_limit'
    if state['history'] and now - state['history'][-1] < 600:
        return None, 'cooldown'
    for kind in ('core', 'snow'):
        if state[kind + '_failures'] >= 3:
            return kind, 'consecutive_failures'
    if snapshot['qq'] == 'offline':
        return None, 'needs_qq_login'
    return None, 'healthy' if not faults else 'confirming_failure'


def ps(command, timeout=25):
    result = subprocess.run(['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', command],
        capture_output=True, timeout=timeout, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise RuntimeError('PowerShell operation failed')
    return result.stdout.decode('utf-8-sig', 'replace').strip()


def inventory(root):
    # All process candidates come from CIM. Mutations recheck identity separately.
    script = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); "
    script += "@(Get-CimInstance Win32_Process -Filter \"Name='python.exe' OR Name='node.exe'\" | "
    script += "Select-Object ProcessId,ParentProcessId,ExecutablePath,CommandLine,@{n='Started';e={$_.CreationDate.ToUniversalTime().ToString('o')}}) | ConvertTo-Json -Compress"
    rows = json.loads(ps(script) or '[]')
    if isinstance(rows, dict):
        rows = [rows]
    return [p for p in rows if p.get('ExecutablePath', '').casefold().startswith(str(root).casefold() + '\\')]


async def qq_status(root):
    import aiohttp
    configs = list((root / 'data/local-lab/modules/SnowLuma/config').glob('onebot_*.json'))
    if len(configs) != 1:
        raise RuntimeError('Expected one account configuration')
    config = read_json(configs[0])
    candidates = [x for x in config['networks']['wsServers'] if x.get('host') in ('127.0.0.1', 'localhost') and x.get('port') == 7988]
    if len(candidates) != 1:
        raise RuntimeError('Unrecognized OneBot listener')
    server = candidates[0]
    headers = {'Authorization': 'Bearer ' + server.get('accessToken', '')}
    async with aiohttp.ClientSession(timeout=aiohttp.ClientTimeout(total=8), trust_env=False) as session:
        async with session.ws_connect('ws://127.0.0.1:7988' + (server.get('path') or '/'), headers=headers) as ws:
            await ws.send_json({'action':'get_status', 'params':{}, 'echo':'rin-autorepair-status'})
            for _ in range(12):
                message = await ws.receive_json(timeout=5)
                if message.get('echo') == 'rin-autorepair-status':
                    if message.get('retcode') != 0:
                        raise RuntimeError('OneBot status rejected')
                    data = message.get('data', {})
                    if data.get('online') is not True:
                        return 'offline'
                    return 'healthy' if data.get('good') is True else 'degraded'
    raise RuntimeError('OneBot status missing')


def snapshot(root):
    rows = inventory(root)
    cores = [p for p in rows if p.get('CommandLine', '').endswith(' -m src.runtime')]
    if len(cores) > 1:
        raise RuntimeError('Multiple core workers; manual review required')
    core = cores[0] if cores else None
    now = time.time()
    age = now-datetime.fromisoformat(core['Started'].replace('Z','+00:00')).timestamp() if core else 0
    health = read_json(root / ('data/local-lab/modules/MaiBot/logs/runtime-health/heartbeat-%s.json' % core['ProcessId'])) if core else None
    startup = bool(core and age < 300)
    loop_ok = bool(health and health.get('pid') == core['ProcessId'] and
        0 <= now-health.get('time', 0) < 75 and health.get('loop_silence_seconds', 999) < 120)
    try:
        qq = asyncio.run(qq_status(root))
    except Exception:
        qq = 'unreachable'
    return {'time':datetime.now(timezone.utc).isoformat(), 'core_ok':startup or loop_ok,
            'startup':startup, 'core_pid':core['ProcessId'] if core else None, 'qq':qq}


def audit_payload(root, directory, apply):
    """Only replace exact known originals with saved, hashed, verified fixes."""
    root = root.resolve()
    manifest = read_json(directory / 'manifest.json')
    needed, unknown = [], []
    for item in manifest['files']:
        target = (root / item['target']).resolve()
        if not target.is_relative_to(root):
            raise ValueError('Manifest path escapes project')
        source = directory / item['payload']
        good = source.read_bytes()
        if hashlib.sha256(good).hexdigest() != item['good_sha256']:
            raise ValueError('Repair payload checksum mismatch')
        previous = target.read_bytes() if target.exists() else None
        digest = hashlib.sha256(previous).hexdigest() if previous is not None else None
        if digest == item['good_sha256']:
            continue
        if digest not in item['allowed_originals']:
            unknown.append(item['target'])
            continue
        needed.append(item['kind'])
        if apply:
            # Recheck against a concurrent upgrade before the atomic replacement.
            if (target.read_bytes() if target.exists() else None) != previous:
                raise RuntimeError('Concurrent source modification')
            backup = directory / 'backups' / str(time.time_ns())
            backup.mkdir(parents=True)
            if previous is not None:
                (backup / target.name).write_bytes(previous)
            temp = target.with_name(target.name + '.autorepair.tmp')
            temp.write_bytes(good)
            temp.replace(target)
    return needed, unknown


def recover(root, kind):
    # The PowerShell helper is deployed beside this file and validates PIDs,
    # creation times, executable paths and parent ownership before acting.
    helper = Path(__file__).with_name('recover.ps1')
    result = subprocess.run(['powershell.exe','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass',
                             '-File',str(helper),'-Root',str(root),'-Kind',kind],
                            capture_output=True, timeout=40, creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise RuntimeError('Scoped recovery failed')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path(r'C:\DeepSeek-QQ'))
    parser.add_argument('--dry-run', action='store_true')
    parser.add_argument('--manual', action='store_true')
    parser.add_argument('--repair', choices=['core','snow'])
    args = parser.parse_args()
    root = args.root.resolve(strict=True)
    directory = root / 'data/auto-repair'
    directory.mkdir(parents=True, exist_ok=True)
    log = logging.getLogger('rin-auto-repair')
    log.setLevel(logging.INFO)
    handler = RotatingFileHandler(directory / 'repair.log', maxBytes=1024*1024, backupCount=3, encoding='utf-8')
    handler.setFormatter(logging.Formatter('%(asctime)s %(message)s'))
    log.addHandler(handler)
    if args.repair and not args.manual:
        parser.error('--repair requires --manual')
    if not args.manual:
        lease = read_json(directory/'gui-repair.json', {})
        if lease.get('enabled') is not True or not 0 <= time.time()-lease.get('time', 0) < 90:
            return
    # Cross-process byte lock; releases automatically if this invocation crashes.
    import msvcrt
    with (directory/'run.lock').open('a+b') as lock:
        lock.seek(0); lock.write(b'0'); lock.flush(); lock.seek(0)
        try:
            msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError:
            return
        state = read_json(directory/'state.json', {})
        try:
            needed, unknown = audit_payload(root, directory, False)
            current = snapshot(root)
            action, reason = decide(current, state, time.time())
            if args.repair and reason not in ('cooldown','hourly_limit'):
                action, reason = args.repair, 'manual_repair'
            # A previously applied repair must still be activated after a failure/reboot.
            pending = state.get('pending_activation')
            if not action and (needed or pending) and reason not in ('cooldown','hourly_limit'):
                action, reason = (pending or needed[0]), 'activate_known_patch'
            current.update(action=action, reason=reason, unknown_sources=unknown, dry_run=args.dry_run)
            write_json(directory/'last_status.json', current)
            print(json.dumps(current, ensure_ascii=False), flush=True)
            if args.dry_run:
                return
            if action:
                state['history'].append(time.time())  # Persist budget before side effects.
                state['pending_activation'] = action
                write_json(directory/'state.json', state)
                write_json(directory/('incident-%s.json' % time.time_ns()), current)
                applied, unknown = audit_payload(root, directory, True)
                recover(root, action)
                state.pop('pending_activation', None)
                state[action+'_failures'] = 0
                # Both types may have been restored; schedule the other activation later.
                others = [x for x in applied if x != action]
                if others:
                    state['pending_activation'] = others[0]
                log.info('recovery_requested kind=%s reason=%s', action, reason)
            elif reason != state.get('last_reason') or unknown:
                log.info('status=%s qq=%s core_ok=%s unknown_source_count=%s',reason,current['qq'],current['core_ok'],len(unknown))
            state['last_reason'] = reason
            write_json(directory/'state.json', state)
        except Exception as exc:
            log.error('check_failed type=%s', type(exc).__name__)
            raise SystemExit(1) from None


if __name__ == '__main__':
    main()
