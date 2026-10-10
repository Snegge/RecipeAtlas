"""Ensure the two independent repositories keep identical quantity/unit contracts."""
import json
from pathlib import Path
import sys
backend = Path(__file__).resolve().parents[1]
frontend = Path(sys.argv[1] if len(sys.argv) > 1 else backend.parent / 'RecipeAtlasFrontend')
pairs = [
    (backend / 'tests/fixtures/quantity-cases.json', frontend / 'src/app/core/utils/fixtures/quantity-cases.json'),
    (backend / 'backend/RecipeAtlas.Api/MeasurementUnits.json', frontend / 'src/app/core/utils/measurement-units.json'),
]
for a, b in pairs:
    assert json.loads(a.read_text()) == json.loads(b.read_text()), f'Contract copies diverged: {a.name}'
print('Shared quantity cases and unit definitions match across both repositories.')
