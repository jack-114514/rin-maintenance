"""Windows subprocess integration: exit 42 and only reap matching children."""
import ctypes
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest


@unittest.skipUnless(sys.platform == 'win32', 'Windows process supervision')
class RestartTests(unittest.TestCase):
    def test_exit_code_and_child_scope(self):
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel.OpenProcess.restype = ctypes.c_void_p
        kernel.GetExitCodeProcess.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong)]
        kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        def alive(pid):
            handle = kernel.OpenProcess(0x1000, False, pid)
            if not handle:
                return False
            code = ctypes.c_ulong()
            try:
                return bool(kernel.GetExitCodeProcess(handle, ctypes.byref(code))) and code.value == 259
            finally:
                kernel.CloseHandle(handle)

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / 'src/plugin_runtime/runner'
            package.mkdir(parents=True)
            (package / 'runner_main.py').write_text('import time; time.sleep(90)')
            module = str(Path(__file__).with_name('runtime_guard.py').resolve())
            code = '''
import importlib.util, json, subprocess, sys, time
from pathlib import Path
spec=importlib.util.spec_from_file_location('guard', sys.argv[1])
guard=importlib.util.module_from_spec(spec); spec.loader.exec_module(guard)
plugin=subprocess.Popen([sys.executable,'-m','src.plugin_runtime.runner.runner_main'],creationflags=subprocess.CREATE_NO_WINDOW)
decoy=subprocess.Popen([sys.executable,'-c','import time; time.sleep(90)'],creationflags=subprocess.CREATE_NO_WINDOW)
Path('children.json').write_text(json.dumps([plugin.pid, decoy.pid]))
class Loop:
 def is_closed(self): return False
 def call_soon_threadsafe(self, callback): pass
guard.LoopGuard(Loop(), 'health', lambda: False, interval=.05, dump_after=.2, restart_after=.4, startup_grace=0).start()
time.sleep(30)
'''
            parent = subprocess.Popen([sys.executable, '-c', code, module], cwd=root,
                creationflags=subprocess.CREATE_NO_WINDOW, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            children = []
            try:
                for _ in range(100):
                    if (root / 'children.json').exists():
                        children = json.loads((root / 'children.json').read_text())
                        break
                    time.sleep(.05)
                self.assertEqual(len(children), 2)
                self.assertEqual(parent.wait(timeout=20), 42)
                self.assertFalse(alive(children[0]), 'plugin child must be reaped')
                self.assertTrue(alive(children[1]), 'unrelated child must survive')
                self.assertTrue(list((root / 'health').glob('stalled-*.log')))
            finally:
                if parent.poll() is None:
                    parent.kill(); parent.wait()
                for pid in children:
                    if alive(pid):
                        subprocess.run(['taskkill', '/PID', str(pid), '/F'], stdout=subprocess.DEVNULL,
                                       stderr=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)


if __name__ == '__main__':
    unittest.main()
