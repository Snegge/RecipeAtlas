"""Export/verify the Development-only description using a disposable API and SQLite database."""
import argparse
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--check', action='store_true')
args = parser.parse_args()
dll = root / 'backend/RecipeAtlas.Api/bin/Debug/net10.0/Recipoke.Api.dll'
dotnet = os.environ.get('RECIPOKE_DOTNET', 'dotnet')
password = secrets.token_urlsafe(32)
hashed = subprocess.run([dotnet, str(dll), '--hash-password'], input=password+'\n', text=True, capture_output=True, check=True).stdout.strip()
with socket.socket() as sock:
    sock.bind(('127.0.0.1', 0))
    port = sock.getsockname()[1]
base = f'http://127.0.0.1:{port}'
with tempfile.TemporaryDirectory(prefix='recipoke-openapi-') as directory:
    environment = dict(os.environ, ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS=base,
                       DataDirectory=directory, Owner__PasswordHash=hashed, RecipeText__ApiKey='')
    with open(Path(directory)/'api.log', 'w') as log:
        def start():
            process = subprocess.Popen([dotnet, str(dll)], cwd=root, env=environment, stdout=log, stderr=subprocess.STDOUT)
            for _ in range(150):
                if process.poll() is not None:
                    raise RuntimeError('Isolated API exited; inspect startup configuration.')
                try:
                    with urllib.request.urlopen(base+'/health', timeout=1):
                        return process
                except OSError:
                    time.sleep(.1)
            process.terminate(); process.wait(timeout=10)
            raise RuntimeError('API health deadline exceeded')
        process = start()
        try:
            with urllib.request.urlopen(base+'/openapi/v1.json', timeout=10) as response:
                document = json.load(response)
        finally:
            process.terminate(); process.wait(timeout=10)
        # Also prove the documentation route is absent in Production, with isolated data.
        environment['ASPNETCORE_ENVIRONMENT'] = 'Production'
        process = start()
        try:
            try:
                urllib.request.urlopen(base+'/openapi/v1.json', timeout=10)
                raise AssertionError('OpenAPI must not be exposed in Production')
            except urllib.error.HTTPError as error:
                assert error.code == 404, error.code
        finally:
            process.terminate(); process.wait(timeout=10)
document.pop('servers', None)  # Remove the ephemeral test port, never configuration/credentials.
serialized = json.dumps(document, indent=2, sort_keys=True, ensure_ascii=False)+'\n'
target = root/'contracts/recipoke.openapi.json'
if args.check:
    assert target.read_text() == serialized, 'OpenAPI changed: regenerate and coordinate the frontend contract snapshot.'
else:
    target.write_text(serialized)
print('OpenAPI snapshot verified; Development exposed, Production absent.' if args.check else 'OpenAPI exported; Development exposed, Production absent.')
