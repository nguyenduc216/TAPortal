"""Metadata-only regression checks; all writes stay in temporary directories."""
import contextlib
import importlib.util
import io
import json
import pathlib
import shutil
import tempfile
import unittest
from unittest.mock import patch

SOURCE = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('codec', SOURCE / 'tools' / 'codec.py')
codec = importlib.util.module_from_spec(spec)
spec.loader.exec_module(codec)

class CodecTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        meta = self.root / '.codec'
        shutil.copytree(SOURCE / 'templates', meta / 'templates')
        cfg = json.loads((SOURCE / 'config.json').read_text(encoding='utf-8'))
        cfg['deploy']['targets'] = {name: str(pathlib.PureWindowsPath(self.root) / 'publish' / ('deployed-' + name)) for name in cfg['deploy']['targets']}
        (meta / 'config.json').write_text(json.dumps(cfg), encoding='utf-8')
        for name in ('features', 'changes', 'reports'):
            (meta / name).mkdir()
        self.root_patch = patch.object(codec, 'ROOT', self.root)
        self.meta_patch = patch.object(codec, 'META', meta)
        self.root_patch.start()
        self.meta_patch.start()
        self.addCleanup(self.root_patch.stop)
        self.addCleanup(self.meta_patch.stop)

    def test_append_preserves_records_and_files_and_retries_collision(self):
        fixed_now = codec.dt.datetime.now(codec.dt.timezone.utc)
        with contextlib.redirect_stdout(io.StringIO()), patch.object(codec.dt, 'datetime', wraps=codec.dt.datetime) as clock, patch.object(codec.secrets, 'token_hex', side_effect=['AAAAAA', 'AAAAAA', 'BBBBBB']):
            clock.now.return_value = fixed_now
            codec.new('CODEC-WORKFLOW', 'First')
            history = codec.META / 'features/CODEC-WORKFLOW/history.json'
            before = json.loads(history.read_text())
            files = {p: p.read_bytes() for d in ('changes', 'reports') for p in (codec.META / d).glob('*.md')}
            codec.new('CODEC-WORKFLOW', 'Second')
            after = json.loads(history.read_text())
            self.assertEqual(after[:-1], before)
            self.assertNotEqual(after[0]['change_id'], after[1]['change_id'])
            for p, content in files.items():
                self.assertEqual(p.read_bytes(), content)
            self.assertEqual(codec.validate(), 0)

    def test_rejects_unsafe_feature_id(self):
        with self.assertRaises(ValueError):
            codec.new('../ESCAPE', 'Invalid')
        self.assertEqual(list((codec.META / 'features').iterdir()), [])

    def test_rejects_policy_regressions(self):
        path = codec.META / 'config.json'
        original = json.loads(path.read_text())
        mutations = [('database', 'name', 'OtherDB'), ('database', 'execute_mutating_sql_allowed', True), ('deploy', 'require_approval', False), ('deploy', 'require_iis_preflight', False), ('deploy', 'live_iis_overwrite_requires_separate_approval', False), ('deploy', 'targets', {'TACustomerPortal.Web': r'C:\wrong'}), ('git', 'force_push_allowed', True), ('history', 'append_only', False)]
        for section, key, value in mutations:
            with self.subTest(key=key), contextlib.redirect_stdout(io.StringIO()):
                cfg = json.loads(json.dumps(original))
                cfg[section][key] = value
                path.write_text(json.dumps(cfg))
                self.assertEqual(codec.validate(), 1)

if __name__ == '__main__':
    unittest.main()
