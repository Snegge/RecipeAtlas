"""Read-only regression: the real coordinated checkout must pass the documented check on Windows."""
import json
from pathlib import Path
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
FRONTEND = Path(sys.argv.pop(1)).resolve() if len(sys.argv) > 1 else ROOT.parent / 'RecipeAtlasFrontend'


class SharedContractAudit(unittest.TestCase):
    def test_documented_check_accepts_matching_contracts(self):
        manifest = json.loads((FRONTEND / 'contracts/quantity-fingerprints.json').read_text(encoding='utf-8'))
        generated_keys = [str((FRONTEND / name).relative_to(FRONTEND)) for name in manifest]
        result = subprocess.run(
            [sys.executable, str(ROOT / 'scripts/sync_frontend_contracts.py'), str(FRONTEND), '--check'],
            capture_output=True, text=True, cwd=ROOT,
        )
        self.assertEqual(result.returncode, 0, {
            'error': result.stderr, 'manifest_keys': list(manifest), 'platform_keys': generated_keys,
        })


if __name__ == '__main__':
    unittest.main()
