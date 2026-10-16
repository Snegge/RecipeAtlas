"""Backend is authoritative; explicitly synchronise a checked-out frontend without network access."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('frontend', type=Path)
parser.add_argument('--check', action='store_true')
args = parser.parse_args()
frontend = args.frontend.resolve()
assert (frontend/'package.json').is_file(), 'Pass the existing frontend checkout root.'
pairs = [
    (root/'backend/Recipoke.Domain/Quantities/MeasurementUnits.json', frontend/'src/app/core/utils/measurement-units.json'),
    (root/'tests/fixtures/quantity-cases.json', frontend/'src/app/core/utils/fixtures/quantity-cases.json'),
    (root/'contracts/recipoke.openapi.json', frontend/'contracts/recipoke.openapi.json')]
fingerprints = {}
for source, target in pairs:
    value = json.loads(source.read_text())
    if args.check:
        assert value == json.loads(target.read_text()), f'Contract drift: {target}'
    else:
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or json.loads(target.read_text()) != value:
            target.write_text(json.dumps(value, indent=2, ensure_ascii=False)+'\n')
    if source.name != 'recipoke.openapi.json':
        canonical = json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False)
        fingerprints[str(target.relative_to(frontend))] = hashlib.sha256(canonical.encode()).hexdigest()
manifest = frontend/'contracts/quantity-fingerprints.json'
if args.check:
    assert json.loads(manifest.read_text()) == fingerprints, 'Quantity fingerprints need synchronisation.'
else:
    manifest.write_text(json.dumps(fingerprints, indent=2)+'\n')
print('Cross-repository contracts and quantity fixtures match.' if args.check else 'Frontend contracts synchronised; review both repository diffs.')
