import asyncio
import importlib.util
from pathlib import Path
import tempfile
import time
import unittest

spec = importlib.util.spec_from_file_location('guard', Path(__file__).with_name('runtime_guard.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class GuardTests(unittest.IsolatedAsyncioTestCase):
    async def test_manual_pause_suppresses_automatic_actions(self):
        with tempfile.TemporaryDirectory() as directory:
            calls = []
            guard = module.LoopGuard(asyncio.get_running_loop(), directory, lambda: False,
                interval=.02, dump_after=.05, restart_after=.1, startup_grace=0,
                enabled=lambda: False, restart=lambda: calls.append('restart')).start()
            await asyncio.sleep(.04)
            time.sleep(.2)
            guard.stop()
            await asyncio.to_thread(guard.thread.join, 1)
            self.assertFalse(calls)
            self.assertFalse(list(Path(directory).glob('stalled-*')))

    async def test_healthy_loop_and_shutdown(self):
        with tempfile.TemporaryDirectory() as directory:
            calls = []
            stopped = [False]
            guard = module.LoopGuard(asyncio.get_running_loop(), directory, lambda: stopped[0],
                interval=.02, dump_after=.1, restart_after=.2, startup_grace=0,
                restart=lambda: calls.append('restart')).start()
            await asyncio.sleep(.3)
            self.assertFalse(calls)
            stopped[0] = True
            time.sleep(.25)
            guard.stop()
            await asyncio.to_thread(guard.thread.join, 1)
            self.assertFalse(calls)
            self.assertFalse(list(Path(directory).glob('stalled-*')))

    async def test_stall_captures_evidence_before_restart(self):
        with tempfile.TemporaryDirectory() as directory:
            calls = []
            guard = module.LoopGuard(asyncio.get_running_loop(), directory, lambda: False,
                interval=.02, dump_after=.1, restart_after=.2, startup_grace=0,
                restart=lambda: calls.append(list(Path(directory).glob('stalled-*')))).start()
            await asyncio.sleep(.06)
            time.sleep(.3)  # Reproduce a synchronous main-loop hang.
            guard.stop()
            await asyncio.to_thread(guard.thread.join, 1)
            self.assertEqual(len(calls), 1)
            self.assertEqual(len(calls[0]), 1)
            self.assertIn('test_stall_captures_evidence', calls[0][0].read_text())

    async def test_startup_grace(self):
        with tempfile.TemporaryDirectory() as directory:
            calls = []
            guard = module.LoopGuard(asyncio.get_running_loop(), directory, lambda: False,
                interval=.02, dump_after=.05, restart_after=.1, startup_grace=1,
                restart=lambda: calls.append('restart')).start()
            time.sleep(.25)
            guard.stop()
            await asyncio.to_thread(guard.thread.join, 1)
            self.assertFalse(calls)


if __name__ == '__main__':
    unittest.main()
