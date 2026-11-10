import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec=importlib.util.spec_from_file_location('repair',Path(__file__).with_name('auto_repair.py'))
repair=importlib.util.module_from_spec(spec); spec.loader.exec_module(repair)


class PolicyTests(unittest.TestCase):
    def check(self, core=True, qq='healthy', startup=False):
        return {'core_ok':core,'qq':qq,'startup':startup}

    def test_healthy_idle(self):
        state={}
        for t in range(0,3600,60):
            self.assertEqual(repair.decide(self.check(),state,t),(None,'healthy'))

    def test_confirmation_and_recovery_reset(self):
        state={}
        self.assertIsNone(repair.decide(self.check(False),state,0)[0])
        self.assertIsNone(repair.decide(self.check(False),state,60)[0])
        self.assertEqual(repair.decide(self.check(False),state,120)[0],'core')
        repair.decide(self.check(),state,180)
        self.assertEqual(state['core_failures'],0)

    def test_qq_fault(self):
        state={}
        for t in (0,60,120): action,_=repair.decide(self.check(qq='degraded'),state,t)
        self.assertEqual(action,'snow')

    def test_no_restart_on_login_required(self):
        state={}
        for t in range(0,1200,60):
            self.assertEqual(repair.decide(self.check(qq='offline'),state,t),(None,'needs_qq_login'))

    def test_startup_grace(self):
        state={}
        for t in (0,60,120): self.assertIsNone(repair.decide(self.check(False,startup=True),state,t)[0])

    def test_cooldown_and_budget(self):
        state={'history':[0]}
        for t in (60,120,180): self.assertIsNone(repair.decide(self.check(False),state,t)[0])
        self.assertEqual(repair.decide(self.check(False),state,601)[0],'core')
        state['history']=[100,200,300]
        self.assertEqual(repair.decide(self.check(False),state,1000),(None,'hourly_limit'))
        self.assertEqual(repair.decide(self.check(False),state,4000)[0],'core')


class RestoreTests(unittest.TestCase):
    def test_known_original_restored_unknown_preserved_and_payload_checked(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp); bundle=root/'bundle'; bundle.mkdir()
            (bundle/'good').write_bytes(b'fixed')
            (root/'source').write_bytes(b'original')
            item={'target':'source','payload':'good','kind':'core',
                'good_sha256':hashlib.sha256(b'fixed').hexdigest(),
                'allowed_originals':[hashlib.sha256(b'original').hexdigest()]}
            (bundle/'manifest.json').write_text(json.dumps({'files':[item]}))
            self.assertEqual(repair.audit_payload(root,bundle,False),(['core'],[]))
            self.assertEqual((root/'source').read_bytes(),b'original')
            repair.audit_payload(root,bundle,True)
            self.assertEqual((root/'source').read_bytes(),b'fixed')
            self.assertTrue(list((bundle/'backups').glob('*/source')))
            (root/'source').write_bytes(b'new unknown version')
            self.assertEqual(repair.audit_payload(root,bundle,True),([],['source']))
            self.assertEqual((root/'source').read_bytes(),b'new unknown version')
            (bundle/'good').write_bytes(b'corrupt')
            with self.assertRaises(ValueError): repair.audit_payload(root,bundle,True)


if __name__=='__main__': unittest.main()
