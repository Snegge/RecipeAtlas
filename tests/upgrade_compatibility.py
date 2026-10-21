"""Optional cross-binary account upgrade. All content, credentials and databases are synthetic."""
from contextlib import closing
import hashlib
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import socket
import sqlite3
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
if len(sys.argv) != 2:
    raise SystemExit('Usage: python3 tests/upgrade_compatibility.py /path/to/single-owner/Recipoke.Api.dll')
old = Path(sys.argv[1]).resolve()
new = ROOT / 'backend/RecipeAtlas.Api/bin/Debug/net10.0/Recipoke.Api.dll'
dotnet = os.environ.get('RECIPOKE_DOTNET', 'dotnet')
password, new_password, recovered_password = [secrets.token_urlsafe(32) for _ in range(3)]
hash_value = subprocess.run([dotnet, str(old), '--hash-password'], input=password + '\n', text=True,
                            capture_output=True, check=True).stdout.strip()
with socket.socket() as sock:
    sock.bind(('127.0.0.1', 0))
    port = sock.getsockname()[1]
base = f'http://127.0.0.1:{port}'
jar = http.cookiejar.CookieJar()
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))


def request(method, path, expected, data=None, headers=None):
    headers = {'X-RecipeAtlas': '1', **(headers or {})}
    if isinstance(data, dict):
        data = json.dumps(data).encode()
        headers['Content-Type'] = 'application/json'
    req = urllib.request.Request(base + path, data=data, method=method, headers=headers)
    try:
        response = client.open(req, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    body = response.read()
    assert response.status == expected, (path, response.status, expected, body[:200])
    return json.loads(body) if 'json' in response.headers.get('Content-Type', '') and body else body


with tempfile.TemporaryDirectory(prefix='recipoke-upgrade-') as temporary:
    data = Path(temporary) / 'data'
    environment = dict(os.environ, ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS=base,
                       DataDirectory=str(data), Owner__PasswordHash=hash_value, RecipeText__ApiKey='')
    counter = 0

    def start(dll):
        global counter
        counter += 1
        log_path = Path(temporary) / f'{counter}.log'
        log = log_path.open('w')
        process = subprocess.Popen([dotnet, str(dll)], cwd=ROOT / 'backend/RecipeAtlas.Api',
                                   env=environment, stdout=log, stderr=subprocess.STDOUT)
        for _ in range(100):
            if process.poll() is not None:
                raise RuntimeError('API exited: ' + log_path.read_text()[-1500:])
            try:
                with urllib.request.urlopen(base + '/health', timeout=1) as response:
                    assert json.load(response) == {'status': 'ok'}
                    return process, log
            except OSError:
                time.sleep(.1)
        process.terminate()
        process.wait(timeout=10)
        log.close()
        raise RuntimeError('API not healthy')

    def stop(process, log):
        process.terminate()
        process.wait(timeout=10)
        log.close()

    def command(flag, values):
        result = subprocess.run([dotnet, str(new), flag], input='\n'.join(values) + '\n', text=True,
                                capture_output=True, env=environment, cwd=ROOT / 'backend/RecipeAtlas.Api')
        assert result.returncode == 0, result.stderr
        assert all(secret not in result.stdout + result.stderr for secret in [password, new_password, recovered_password])
        return result.stdout

    def history():
        with closing(sqlite3.connect(data / 'recipes.db')) as db, db:
            return db.execute('SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId').fetchall()

    def key_hashes():
        return {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (data / 'keys').iterdir()}

    def old_content():
        with closing(sqlite3.connect(data / 'recipes.db')) as db, db:
            # Compare every previous column, including IDs, quantities, order, dates, image bytes and references.
            return {table: (columns, db.execute(f'SELECT {",".join(columns)} FROM "{table}" ORDER BY 1').fetchall())
                    for table, columns in old_columns.items()}

    p, log = start(old)
    try:
        request('POST', '/api/auth/login', 204, {'password': password})
        assert any(c.name == 'RecipeAtlas.Session' for c in jar)
        recipe_input = json.loads((ROOT / 'sample-recipe.json').read_text())
        recipe = request('POST', '/api/recipes', 201, recipe_input)
        image = b'\x89PNG\r\n\x1a\n' + b'compatibility-image'
        request('PUT', f'/api/recipes/{recipe["id"]}/image', 200, image)
        before = request('GET', f'/api/recipes/{recipe["id"]}', 200)
    finally:
        stop(p, log)
    with closing(sqlite3.connect(data / 'recipes.db')) as db, db:
        old_columns = {table: [row[1] for row in db.execute(f'PRAGMA table_info("{table}")')]
                       for table in ['Recipes', 'Ingredient', 'RecipeStep', 'Images']}
    original = old_content()
    old_history, keys = history(), key_hashes()
    p, log = start(new)
    try:
        request('GET', '/api/auth/me', 503)
        request('GET', f'/api/recipes/{recipe["id"]}', 503)
    finally:
        stop(p, log)
    assert old_content() == original
    command('--bootstrap-admin', ['upgrade-admin', 'Upgrade administrator', new_password])
    assert 'already complete' in command('--bootstrap-admin', [])
    p, log = start(new)
    try:
        request('GET', '/api/auth/me', 401)  # A real, previously valid owner cookie is ignored.
        request('POST', '/api/auth/login', 401, {'username': 'upgrade-admin', 'password': password})
        request('POST', '/api/auth/login', 204, {'username': 'upgrade-admin', 'password': new_password})
        assert request('GET', '/api/auth/me', 200)['isAdministrator']
        books = request('GET', '/api/cookbooks', 200)
        assert len(books) == 1 and books[0]['role'] == 'Owner'
        path = f'/api/cookbooks/{books[0]["id"]}/recipes/{recipe["id"]}'
        after = request('GET', path, 200)
        assert after['imageUrl'] == path + '/image'
        assert {k: v for k, v in before.items() if k not in ('imageUrl', 'version')} == {k: v for k, v in after.items() if k not in ('imageUrl', 'version')}
        assert request('GET', path + '/image', 200) == image
        assert request('GET', path.rsplit('/', 1)[0], 200)['total'] == 1
        request('GET', f'/api/recipes/{recipe["id"]}', 404)
    finally:
        stop(p, log)
    assert old_content() == original
    assert history()[:len(old_history)] == old_history and len(history()) == len(old_history) + 3
    assert key_hashes() == keys
    command('--recover-admin', ['upgrade-admin', recovered_password])
    p, log = start(new)
    try:
        request('GET', '/api/auth/me', 401)  # Server-side recovery invalidates prior Identity sessions too.
        request('POST', '/api/auth/login', 401, {'username': 'upgrade-admin', 'password': new_password})
        request('POST', '/api/auth/login', 204, {'username': 'upgrade-admin', 'password': recovered_password})
        request('GET', path, 200)
    finally:
        stop(p, log)
    environment['ASPNETCORE_ENVIRONMENT'] = 'Production'
    p, log = start(new)
    try:
        request('POST', '/api/auth/login', 204, {'username': 'upgrade-admin', 'password': recovered_password})
        assert any(c.name == 'Recipoke.Session' and c.secure and (c.has_nonstandard_attr('HttpOnly') or c.has_nonstandard_attr('httponly')) and
                   str(c.get_nonstandard_attr('samesite', c.get_nonstandard_attr('SameSite', ''))).lower() == 'strict' for c in jar)
    finally:
        stop(p, log)
    print('Cross-binary upgrade passed: all legacy content/IDs/order/quantities/image bytes preserved, history extended, key ring unchanged, setup closed/retry safe, owner cookies rejected, new login and recovery usable, Production cookie Secure/HttpOnly/SameSite=Strict.')
