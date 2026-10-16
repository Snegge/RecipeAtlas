"""Compare contracts with an explicitly located frontend checkout, without modifying files."""
import argparse
import json
from pathlib import Path

backend = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument(
    'frontend', nargs='?', type=Path, default=backend.parent / 'RecipeAtlasFrontend',
    help='Frontend directory containing package.json; relative paths use the current working directory. '
         'Default: the sibling RecipeAtlasFrontend checkout.',
)
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
                     'the matching main versions of both repositories; this check does not create files.')
    except (OSError, ValueError) as error:
        parser.error(f'Cannot read contract {path}: {error}')


pairs = [
    (backend / 'contracts/recipoke.openapi.json', frontend / 'contracts/recipoke.openapi.json'),
    (backend / 'tests/fixtures/quantity-cases.json', frontend / 'src/app/core/utils/fixtures/quantity-cases.json'),
    (backend / 'backend/Recipoke.Domain/Quantities/MeasurementUnits.json', frontend / 'src/app/core/utils/measurement-units.json'),
]
for source, target in pairs:
    if read_json(source) != read_json(target):
        parser.error(f'Contract copies diverged: {source.name}. Review both repository versions '
                     'before deliberately synchronising contracts.')
print('OpenAPI, quantity cases and unit definitions match across both repositories.')
