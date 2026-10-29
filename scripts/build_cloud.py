"""Build a combined image from an allowlisted, secret-free temporary context. Never publishes."""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def stage(frontend, destination):
    def ignore(directory, names):
        return [n for n in names if n.startswith('.') or n in {'bin','obj','data','backups','node_modules','dist','test-results','out-tsc'} or n.endswith(('.db','.db-wal','.db-shm','.sqlite','.sqlite3','.pfx','.p12','.pem','.key','.bak'))]
    for name in ['RecipeAtlas.Api','Recipoke.Domain']:
        shutil.copytree(ROOT/'backend'/name, destination/'backend'/'backend'/name, ignore=ignore)
    target=destination/'backend'/'tools';target.mkdir(parents=True)
    shutil.copy2(ROOT/'tools/requirements-yt-dlp.txt',target)
    (destination/'frontend').mkdir()
    for name in ['src','public']:
        shutil.copytree(frontend/name,destination/'frontend'/name,ignore=ignore)
    for name in ['package.json','package-lock.json','angular.json','tsconfig.json','tsconfig.app.json','ngsw-config.json']:
        shutil.copy2(frontend/name,destination/'frontend'/name)
    shutil.copy2(ROOT/'deploy/Render.Dockerfile',destination/'Dockerfile')

def revision(path):
    return subprocess.check_output(['git','-c',f'safe.directory={path.as_posix()}','-C',str(path),'rev-parse','HEAD'],text=True).strip()

if __name__ == '__main__':
    p=argparse.ArgumentParser();p.add_argument('--frontend',type=Path,required=True);p.add_argument('--tag',default='recipoke:local');a=p.parse_args()
    frontend=a.frontend.resolve()
    with tempfile.TemporaryDirectory(prefix='recipoke-build-') as directory:
        stage(frontend,Path(directory))
        subprocess.run(['docker','build','--platform','linux/amd64','--build-arg',f'BACKEND_REVISION={revision(ROOT)}','--build-arg',f'FRONTEND_REVISION={revision(frontend)}','-t',a.tag,directory],check=True)
