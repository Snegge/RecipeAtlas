"""Prove migrated aggregate versions accept one HTTP save and reject a stale retry."""
from contextlib import closing
import copy
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
import urllib.parse
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
if len(sys.argv) != 2:
    raise SystemExit('Usage: python tests/shared_editing_upgrade_audit.py /path/to/pre-shared-editing/Recipoke.Api.dll')
OLD = Path(sys.argv[1]).resolve()
NEW = ROOT / 'backend/RecipeAtlas.Api/bin/Debug/net10.0/Recipoke.Api.dll'
DOTNET = os.environ.get('RECIPOKE_DOTNET', 'dotnet')
PNG = b'\x89PNG\r\n\x1a\nsynthetic preserved upgrade image'
FIELDS = ('title', 'description', 'sourceUrl', 'servings', 'ingredients', 'steps')
checks = 0


def client():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def command(recipe, **changes):
    return {**{key: copy.deepcopy(recipe[key]) for key in FIELDS},
            'expectedVersion': recipe['version'], **changes}


def stop(process, log):
    try:
        process.terminate()
        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=15)
    finally:
        log.close()


with tempfile.TemporaryDirectory(prefix='recipoke-shared-upgrade-audit-') as directory:
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        port = sock.getsockname()[1]
    base = f'http://127.0.0.1:{port}'
    password = secrets.token_urlsafe(24)
    env = dict(os.environ, DataDirectory=str(Path(directory).resolve()),
               ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS=base,
               Accounts__PublicAppUrl='http://localhost:4200', RecipeText__ApiKey='')

    def start(dll, label):
        log = open(Path(directory) / (label + '.log'), 'w')
        try:
            process = subprocess.Popen([DOTNET, str(dll)], cwd=ROOT, env=env,
                                       stdout=log, stderr=subprocess.STDOUT)
        except BaseException:
            log.close()
            raise
        try:
            for _ in range(150):
                if process.poll() is not None:
                    raise RuntimeError('Disposable upgrade API exited')
                try:
                    with urllib.request.urlopen(base + '/health', timeout=1):
                        return process, log
                except OSError:
                    time.sleep(.1)
            raise RuntimeError('Disposable upgrade API startup deadline')
        except BaseException:
            stop(process, log)
            raise

    def call(actor, method, path, status, body=None, code=None):
        global checks
        headers = {'X-RecipeAtlas': '1'}
        if isinstance(body, dict):
            body = json.dumps(body).encode()
            headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(base + path, data=body, method=method, headers=headers)
        try:
            response = actor.open(request, timeout=40)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            raw = response.read()
            assert response.status == status, (method, path, response.status, status)
            assert 'no-store' in response.headers.get('Cache-Control', '')
            data = json.loads(raw) if raw and 'json' in response.headers.get('Content-Type', '') else raw
        if code:
            assert data.get('code') == code, (method, path, data.get('code'), code)
            assert not {'version', 'current', 'recipe', 'members', 'cookbook'}.intersection(data)
        checks += 1
        return data

    setup = subprocess.run([DOTNET, str(OLD), '--bootstrap-admin'],
                           input=f'administrator\nUpgrade owner\n{password}\n',
                           capture_output=True, text=True, cwd=ROOT, env=env)
    assert setup.returncode == 0, 'Disposable historical bootstrap failed'
    owner, editor, public = client(), client(), client()
    process, log = start(OLD, 'historical')
    try:
        call(owner, 'POST', '/api/auth/login', 204, {'username': 'administrator', 'password': password})
        invitation = call(owner, 'POST', '/api/admin/invitations', 200)
        token = urllib.parse.parse_qs(urllib.parse.urlsplit(invitation['link']).fragment)['token'][0]
        call(public, 'POST', '/api/invitations/redeem', 204, {
            'token': token, 'username': 'upgrade-editor', 'displayName': 'Existing editor',
            'email': 'upgrade-editor@example.test', 'password': password})
        call(editor, 'POST', '/api/auth/login', 204, {'username': 'upgrade-editor', 'password': password})
        book = call(owner, 'GET', '/api/cookbooks', 200)[0]
        book_path = '/api/cookbooks/' + book['id']
        call(owner, 'PUT', book_path, 204, {'name': 'Existing shared cookbook', 'description': 'Preserved description'})
        call(owner, 'POST', book_path + '/members', 204, {'username': 'upgrade-editor', 'role': 'Editor'})
        pending = call(editor, 'GET', '/api/cookbook-invitations', 200)[0]
        call(editor, 'POST', '/api/cookbook-invitations/' + pending['id'], 204, {'accept': True})
        payload = json.loads((ROOT / 'sample-recipe.json').read_text())
        payload['ingredients'].reverse()
        for ingredient, quantity in zip(payload['ingredients'], ['', '1/2', '2-3', '0.125']):
            ingredient['quantity'] = quantity
        payload['steps'].reverse()
        created = call(owner, 'POST', book_path + '/recipes', 201, payload)
        path = book_path + '/recipes/' + created['id']
        call(owner, 'PUT', path + '/image', 200, PNG)
        before_recipe = call(editor, 'GET', path, 200)
        before_book = call(editor, 'GET', book_path, 200)
        before_members = call(owner, 'GET', book_path + '/members', 200)
        assert all(before_recipe[key] == payload[key] for key in FIELDS)
        assert before_book['role'] == 'Editor'
    finally:
        stop(process, log)

    db_path = Path(directory) / 'recipes.db'
    with closing(sqlite3.connect(db_path)) as db:
        assert all(row[1] != 'Version' for row in db.execute('PRAGMA table_info(Recipes)'))
        assert all(row[1] != 'Version' for row in db.execute('PRAGMA table_info(Cookbooks)'))
        old_migrations = db.execute('SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId').fetchall()

    process, log = start(NEW, 'current')
    try:
        migrated_recipe = call(editor, 'GET', path, 200)
        migrated_book = call(editor, 'GET', book_path, 200)
        assert uuid.UUID(migrated_recipe['version']).int and uuid.UUID(migrated_book['version']).int
        assert migrated_recipe['version'] != migrated_book['version']
        assert {key: value for key, value in migrated_recipe.items() if key != 'version'} == before_recipe
        assert {key: value for key, value in migrated_book.items() if key != 'version'} == before_book
        assert call(owner, 'GET', book_path + '/members', 200)['members'] == before_members
        assert call(editor, 'GET', path + '/image', 200) == PNG

        # A migrated token is usable through the same editor session after restart.
        ingredients = copy.deepcopy(migrated_recipe['ingredients'])
        ingredients[0]['name'] = 'Reviewed salt after upgrade'
        saved_recipe = call(editor, 'PUT', path, 200, command(
            migrated_recipe, title='Saved after upgrade', ingredients=ingredients,
            steps=['Reviewed first step', 'Reviewed final step']))
        assert saved_recipe['version'] != migrated_recipe['version']
        assert saved_recipe['title'] == 'Saved after upgrade'
        assert saved_recipe['ingredients'] == ingredients
        assert saved_recipe['steps'] == ['Reviewed first step', 'Reviewed final step']
        call(owner, 'PUT', path, 409, command(migrated_recipe, title='Rejected stale upgrade',
             ingredients=[{'name': 'Rejected child', 'quantity': '1', 'unit': 'g', 'note': None}],
             steps=['Rejected step']), code='concurrency_conflict')
        persisted = call(owner, 'GET', path, 200)
        assert persisted['version'] == saved_recipe['version']
        assert all(persisted[key] == saved_recipe[key] for key in FIELDS)
        assert persisted['imageUrl'] == migrated_recipe['imageUrl']
        assert call(editor, 'GET', path + '/image', 200) == PNG
        assert call(owner, 'GET', book_path, 200)['version'] == migrated_book['version']

        saved_book = call(owner, 'PUT', book_path, 200, {
            'name': 'Cookbook saved after upgrade', 'description': 'Reviewed description',
            'expectedVersion': migrated_book['version']})
        assert saved_book['version'] != migrated_book['version']
        assert saved_book['name'] == 'Cookbook saved after upgrade'
        assert saved_book['description'] == 'Reviewed description'
        call(owner, 'PUT', book_path, 409, {
            'name': 'Rejected stale cookbook', 'description': 'Rejected description',
            'expectedVersion': migrated_book['version']}, code='concurrency_conflict')
        assert call(owner, 'GET', book_path, 200) == saved_book
        assert call(editor, 'GET', path, 200)['version'] == saved_recipe['version']
        assert call(owner, 'GET', book_path + '/members', 200)['members'] == before_members
    finally:
        stop(process, log)

    with closing(sqlite3.connect(db_path)) as db:
        new_migrations = db.execute('SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId').fetchall()
        assert new_migrations[:-1] == old_migrations
        assert new_migrations[-1][0].endswith('_OptimisticAggregateVersions')
        assert not db.execute('PRAGMA foreign_key_check').fetchall()
    print(f'Passed 3 upgrade audit scenarios and {checks} HTTP checks: representative content, '
          'quantities, order, image and membership preserved; migrated recipe and cookbook tokens '
          'each accept one save and reject a stale retry without overwriting the winner.')
