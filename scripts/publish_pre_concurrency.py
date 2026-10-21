"""Rebuild the pinned completed baseline for disposable concurrency-upgrade verification.

Uses this repository's history; no second checkout, private cross-repository access or user data.
"""
import argparse
from pathlib import Path
import subprocess
import tarfile
import tempfile

BASELINE = '0ce197661b0760d1300cce3efcff097715f2e492'
ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('output', type=Path)
args = parser.parse_args()
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
if any(output.iterdir()):
    parser.error('Use an empty output directory; existing files are never replaced.')
with tempfile.TemporaryDirectory(prefix='recipoke-upgrade-source-') as temporary:
    source = Path(temporary)
    archive = source / 'baseline.tar'
    with archive.open('wb') as file:
        subprocess.run(['git', 'archive', BASELINE], cwd=ROOT, stdout=file, check=True)
    with tarfile.open(archive) as file:
        file.extractall(source, filter='data')
    subprocess.run(['dotnet', 'restore', '--locked-mode'], cwd=source, check=True)
    subprocess.run(['dotnet', 'publish', 'backend/RecipeAtlas.Api', '--no-restore', '-c', 'Debug', '-o', str(output)], cwd=source, check=True)
print(output / 'Recipoke.Api.dll')
