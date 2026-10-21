"""CLI regression checks: invalid checkout paths fail clearly and --check never writes."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
CHECKER = ROOT / 'tests/check_shared_contracts.py'
SYNC = ROOT / 'scripts/sync_frontend_contracts.py'


class ContractPathTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='recipoke contract paths ')
        self.addCleanup(self.temporary.cleanup)
        self.frontend = Path(self.temporary.name) / 'frontend checkout'
        self.frontend.mkdir()
        (self.frontend / 'package.json').write_text('{}', encoding='utf-8')

    def run_script(self, script, *arguments):
        # Optimisation must not disable validation (previous assert-based checks did).
        return subprocess.run([sys.executable, '-O', str(script), *map(str, arguments)],
                              cwd=self.temporary.name, capture_output=True, text=True)

    def commands(self):
        return [(CHECKER, []), (SYNC, ['--check'])]

    def snapshot(self):
        return {str(p.relative_to(self.frontend)): p.read_bytes()
                for p in self.frontend.rglob('*') if p.is_file()}

    def populate(self):
        result = self.run_script(SYNC, self.frontend)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_explicit_absolute_path_with_spaces_and_check_is_read_only(self):
        self.populate()
        before = self.snapshot()
        for script, flags in self.commands():
            result = self.run_script(script, self.frontend, *flags)
            self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.snapshot(), before)

    def test_fingerprint_keys_use_portable_forward_slashes(self):
        self.populate()
        manifest = json.loads((self.frontend / 'contracts/quantity-fingerprints.json').read_text(encoding='utf-8'))
        self.assertEqual(set(manifest), {
            'src/app/core/utils/measurement-units.json',
            'src/app/core/utils/fixtures/quantity-cases.json',
        })

    def test_wrong_checkout_reports_resolved_directory_without_traceback(self):
        wrong = self.frontend.parent / 'missing checkout'
        for script, flags in self.commands():
            result = self.run_script(script, wrong, *flags)
            self.assertEqual(result.returncode, 2)
            self.assertIn(str(wrong), result.stderr)
            self.assertIn('package.json', result.stderr)
            self.assertNotIn('Traceback', result.stderr)
        self.assertFalse(wrong.exists())

    def test_missing_contract_does_not_create_files_in_check_mode(self):
        before = self.snapshot()
        for script, flags in self.commands():
            result = self.run_script(script, self.frontend, *flags)
            self.assertEqual(result.returncode, 2)
            self.assertIn('Required contract file is missing', result.stderr)
            self.assertNotIn('Traceback', result.stderr)
        self.assertEqual(self.snapshot(), before)

    def test_drift_fails_without_overwriting_frontend(self):
        self.populate()
        path = self.frontend / 'contracts/recipoke.openapi.json'
        document = json.loads(path.read_text(encoding='utf-8'))
        document['info']['title'] = 'Deliberate drift'
        path.write_text(json.dumps(document), encoding='utf-8')
        before = self.snapshot()
        for script, flags in self.commands():
            result = self.run_script(script, self.frontend, *flags)
            self.assertEqual(result.returncode, 2)
            self.assertNotIn('Traceback', result.stderr)
        self.assertEqual(self.snapshot(), before)


if __name__ == '__main__':
    unittest.main()
