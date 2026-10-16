"""Backend is authoritative; explicitly synchronise a checked-out frontend without network access."""
import argparse
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('frontend', type=Path, help='Actual frontend directory containing package.json; '
                    'relative paths use the current working directory.')
parser.add_argument('--check', action='store_true', help='Compare only; never create or modify files.')
args = parser.parse_args()
frontend = args.frontend.expanduser().resolve()
if not (frontend / 'package.json').is_file():
    parser.error(f'Frontend checkout not found at {frontend}. Pass the actual frontend directory '
                 'containing package.json; ../RecipeAtlasFrontend is only a sibling-layout example.')


def read_json(path):
    try:
        return json.loads(path.read_text(encoding='utf-8'))
    except FileNotFoundError:
        parser.error(f'Required contract file is missing: {path}. Check the checkout path and pull '
                     'the matching main versions of both repositories. --check does not synchronise files.')
    except (OSError, ValueError) as error:
        parser.error(f'Cannot read contract {path}: {error}')


pairs = [
    (root / 'backend/Recipoke.Domain/Quantities/MeasurementUnits.json', frontend / 'src/app/core/utils/measurement-units.json'),
    (root / 'tests/fixtures/quantity-cases.json', frontend / 'src/app/core/utils/fixtures/quantity-cases.json'),
    (root / 'contracts/recipoke.openapi.json', frontend / 'contracts/recipoke.openapi.json'),
]
# Read source contracts before writing any target file.
values = [(source, target, read_json(source)) for source, target in pairs]
fingerprints = {}
for source, target, value in values:
    if args.check:
        if value != read_json(target):
            parser.error(f'Contract drift: {target}. Review the coordinated repository changes '
                         'before running synchronisation without --check.')
    else:
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or read_json(target) != value:
            target.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    if source.name != 'recipoke.openapi.json':
        canonical = json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False)
        fingerprints[str(target.relative_to(frontend))] = hashlib.sha256(canonical.encode()).hexdigest()
manifest = frontend / 'contracts/quantity-fingerprints.json'
if args.check:
    if read_json(manifest) != fingerprints:
        parser.error('Quantity fingerprints need synchronisation; review the coordinated changes first.')
else:
    manifest.write_text(json.dumps(fingerprints, indent=2) + '\n', encoding='utf-8')
print('Cross-repository contracts and quantity fixtures match.' if args.check else 'Frontend contracts synchronised; review both repository diffs.')
