"""Observe main-loop progress independently of the WebUI thread."""
import faulthandler
import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import time


def restart_worker():
    # A frozen host cannot run normal async plugin shutdown. Reap only its own
    # Python plugin workers before using the existing supervisor restart code.
    if os.name == 'nt':
        executable = sys.executable.replace("'", "''")
        command = (
            "$parentId=%d; $expected='%s'; " % (os.getpid(), executable)
            + "$children=Get-CimInstance Win32_Process -Filter ('ParentProcessId='+$parentId); "
            + "foreach($child in $children){if($child.ExecutablePath -eq $expected -and "
            + "$child.CommandLine -match ' -m src\\.plugin_runtime\\.runner\\.runner_main$'){"
            + "$current=Get-CimInstance Win32_Process -Filter ('ProcessId='+$child.ProcessId); "
            + "if($current.ParentProcessId -eq $parentId -and $current.CreationDate -eq $child.CreationDate){"
            + "Stop-Process -Id $child.ProcessId -ErrorAction SilentlyContinue}}}"
        )
        try:
            subprocess.run(['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', command],
                           timeout=12, creationflags=subprocess.CREATE_NO_WINDOW,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except (OSError, subprocess.TimeoutExpired):
            pass
    os._exit(42)


class LoopGuard:
    def __init__(self, loop, directory, stopping, *, interval=15, dump_after=120,
                 restart_after=180, startup_grace=300, restart=None, enabled=None):
        self.loop = loop
        self.directory = Path(directory)
        self.stopping = stopping
        self.interval = interval
        self.dump_after = dump_after
        self.restart_after = restart_after
        self.startup_grace = startup_grace
        self.restart = restart or restart_worker
        self.enabled = enabled or (lambda: True)
        self.started = self.last_ack = time.monotonic()
        self.armed = False
        self.dumped = False
        self.done = threading.Event()

    def ack(self):
        self.last_ack = time.monotonic()
        self.armed = True
        self.dumped = False

    def start(self):
        self.directory.mkdir(parents=True, exist_ok=True)
        self.thread = threading.Thread(target=self.run, name='rin-main-loop-guard', daemon=True)
        self.thread.start()
        return self

    def stop(self):
        self.done.set()

    def run(self):
        while not self.done.is_set():
            if self.loop.is_closed() or self.stopping():
                return
            try:
                self.loop.call_soon_threadsafe(self.ack)
            except RuntimeError:
                return
            if self.done.wait(self.interval):
                return
            if self.stopping() or self.loop.is_closed():
                return
            now = time.monotonic()
            silent = now - self.last_ack
            active = now - self.started >= self.startup_grace
            try:
                record = {'pid': os.getpid(), 'time': time.time(), 'loop_silence_seconds': round(silent, 3),
                          'armed': self.armed, 'startup_grace_complete': active,
                          'automatic_recovery_enabled': self.enabled()}
                temp = self.directory / ('heartbeat-%s.tmp' % os.getpid())
                temp.write_text(json.dumps(record), encoding='utf-8')
                temp.replace(self.directory / ('heartbeat-%s.json' % os.getpid()))
                if active and silent >= self.dump_after and not self.dumped and self.enabled():
                    with (self.directory / ('stalled-%s-%s.log' % (os.getpid(), int(time.time())))).open('a') as stream:
                        stream.write('Main loop unresponsive for %.1fs; thread stacks follow.\n' % silent)
                        stream.flush()
                        faulthandler.dump_traceback(file=stream, all_threads=True)
                    self.dumped = True
            except OSError:
                # Disk trouble must not disable loop supervision.
                pass
            if active and silent >= self.restart_after and self.enabled():
                self.restart()
                return


_guard = None


def observe(loop):
    global _guard
    if _guard is not None:
        _guard.stop()
        _guard = None
    if loop is not None and os.environ.get('MAIBOT_WORKER_PROCESS') == '1':
        from src.common.paths import get_instance_root
        from src.common.shutdown import is_shutdown_requested
        root = get_instance_root()
        def enabled():
            try:
                lease = json.loads((root.parents[3] / 'data/auto-repair/gui-repair.json').read_text(encoding='utf-8-sig'))
                return lease.get('enabled') is True and 0 <= time.time()-lease.get('time', 0) < 90
            except (OSError, ValueError, TypeError):
                return False
        _guard = LoopGuard(loop, root / 'logs/runtime-health', is_shutdown_requested, enabled=enabled).start()
