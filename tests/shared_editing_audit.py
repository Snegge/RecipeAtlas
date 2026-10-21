"""Focused shared-editing HTTP audit against disposable data and the built API."""
from concurrent.futures import ThreadPoolExecutor
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
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / 'backend/RecipeAtlas.Api/bin/Debug/net10.0/Recipoke.Api.dll'
DOTNET = os.environ.get('RECIPOKE_DOTNET', 'dotnet')
PNG = b'\x89PNG\r\n\x1a\nsynthetic audit image'
FIELDS = ('title', 'description', 'sourceUrl', 'servings', 'ingredients', 'steps')
checks = 0


def client():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def command(recipe, **changes):
    return {**{key: copy.deepcopy(recipe[key]) for key in FIELDS},
            'expectedVersion': recipe['version'], **changes}


def tag(version):
    return {'If-Match': '"' + version + '"'}


with tempfile.TemporaryDirectory(prefix='recipoke-shared-editing-audit-') as directory:
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        port = sock.getsockname()[1]
    base = f'http://127.0.0.1:{port}'
    password = secrets.token_urlsafe(24)
    env = dict(os.environ, DataDirectory=str(Path(directory).resolve()),
               ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS=base,
               Accounts__PublicAppUrl='http://localhost:4200', RecipeText__ApiKey='')
    setup = subprocess.run([DOTNET, str(DLL), '--bootstrap-admin'],
                           input=f'administrator\nAudit owner\n{password}\n',
                           capture_output=True, text=True, cwd=ROOT, env=env)
    assert setup.returncode == 0, 'Disposable bootstrap failed; no user data was selected'
    log = open(Path(directory) / 'api.log', 'w')
    process = subprocess.Popen([DOTNET, str(DLL)], cwd=ROOT, env=env,
                               stdout=log, stderr=subprocess.STDOUT)

    def send(actor, method, path, body=None, headers=None):
        request_headers = {'X-RecipeAtlas': '1', **(headers or {})}
        if isinstance(body, dict):
            body = json.dumps(body).encode()
            request_headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(base + path, data=body, method=method, headers=request_headers)
        try:
            response = actor.open(request, timeout=40)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            raw = response.read()
            assert 'no-store' in response.headers.get('Cache-Control', '')
            data = json.loads(raw) if raw and 'json' in response.headers.get('Content-Type', '') else raw
            return response.status, data

    def call(actor, method, path, status, body=None, headers=None, code=None):
        global checks
        actual, data = send(actor, method, path, body, headers)
        assert actual == status, (method, path, actual, status)
        if code:
            assert data.get('code') == code, (method, path, data.get('code'), code)
            assert not {'version', 'current', 'recipe', 'members', 'cookbook'}.intersection(data)
        checks += 1
        return data

    def register(username):
        invitation = call(owner, 'POST', '/api/admin/invitations', 200)
        token = urllib.parse.parse_qs(urllib.parse.urlsplit(invitation['link']).fragment)['token'][0]
        call(public, 'POST', '/api/invitations/redeem', 204, {
            'token': token, 'username': username, 'displayName': username,
            'email': username + '@example.test', 'password': password})
        actor = client()
        call(actor, 'POST', '/api/auth/login', 204, {'username': username, 'password': password})
        return actor, call(actor, 'GET', '/api/auth/me', 200)

    def invite(actor, username, role):
        before = call(owner, 'GET', book_path, 200)
        sent = call(owner, 'POST', book_path + '/members', 200, {
            'username': username, 'role': role, 'expectedVersion': before['version']})
        after = call(owner, 'GET', book_path, 200)
        assert sent['version'] == after['version'] != before['version']
        assert call(owner, 'GET', path, 200)['version'] == recipe_version
        pending = next(item for item in call(actor, 'GET', '/api/cookbook-invitations', 200)
                       if item['cookbookId'] == book['id'])
        assert pending['cookbookVersion'] == after['version']
        return pending

    def accept(actor, pending):
        call(actor, 'POST', '/api/cookbook-invitations/' + pending['id'], 204,
             {'accept': True, 'expectedVersion': pending['cookbookVersion']})
        after = call(owner, 'GET', book_path, 200)
        assert after['version'] != pending['cookbookVersion']
        assert call(owner, 'GET', path, 200)['version'] == recipe_version
        assert call(actor, 'GET', book_path, 200)['role'] == pending['role']

    try:
        for _ in range(150):
            if process.poll() is not None:
                raise RuntimeError('Disposable audit API exited')
            try:
                with urllib.request.urlopen(base + '/health', timeout=1):
                    break
            except OSError:
                time.sleep(.1)  # Readiness only; concurrent writes use explicit barriers.
        else:
            raise RuntimeError('Disposable audit API startup deadline')

        public, owner = client(), client()
        call(owner, 'POST', '/api/auth/login', 204,
             {'username': 'administrator', 'password': password})
        editor, editor_me = register('audit-editor')
        member, member_me = register('audit-member')
        book = call(owner, 'POST', '/api/cookbooks', 201, {'name': 'Disposable shared audit'})
        book_path = '/api/cookbooks/' + book['id']
        payload = json.loads((ROOT / 'sample-recipe.json').read_text())
        recipe = call(owner, 'POST', book_path + '/recipes', 201, payload)
        path = book_path + '/recipes/' + recipe['id']
        recipe_version = recipe['version']
        accept(editor, invite(editor, 'audit-editor', 'Editor'))

        # Both independent cookie sessions submit the exact recipe version they read.
        owner_snapshot = call(owner, 'GET', path, 200)
        editor_snapshot = call(editor, 'GET', path, 200)
        assert owner_snapshot['version'] == editor_snapshot['version']
        book_version = call(owner, 'GET', book_path, 200)['version']
        gate = threading.Barrier(2)

        def race(actor, snapshot, label):
            body = command(snapshot, title=label,
                           ingredients=[{'name': label, 'quantity': '7', 'unit': 'g', 'note': label}],
                           steps=[label + ' first', label + ' second'])
            gate.wait(timeout=10)
            return send(actor, 'PUT', path, body)

        with ThreadPoolExecutor(max_workers=2) as pool:
            first = pool.submit(race, owner, owner_snapshot, 'Owner winner candidate')
            second = pool.submit(race, editor, editor_snapshot, 'Editor winner candidate')
            raced = [first.result(), second.result()]
        assert sorted(status for status, _ in raced) == [200, 409]
        loser = next(data for status, data in raced if status == 409)
        assert loser['code'] == 'concurrency_conflict'
        assert not {'version', 'current', 'recipe', 'members', 'cookbook'}.intersection(loser)
        winner = next(data for status, data in raced if status == 200)
        current = call(owner, 'GET', path, 200)
        assert current['version'] == winner['version'] != recipe['version']
        assert all(current[key] == winner[key] for key in FIELDS)
        assert current['ingredients'] == [{'name': current['title'], 'quantity': '7',
                                           'unit': 'g', 'note': current['title']}]
        assert current['steps'] == [current['title'] + ' first', current['title'] + ' second']
        checks += 2

        ingredients = copy.deepcopy(current['ingredients'])
        ingredients[0]['quantity'] = '8'
        ingredient_only = call(editor, 'PUT', path, 200, command(current, ingredients=ingredients))
        assert ingredient_only['version'] != current['version']
        assert ingredient_only['ingredients'] == ingredients
        assert all(ingredient_only[key] == current[key] for key in FIELDS if key != 'ingredients')
        current = call(owner, 'GET', path, 200)
        assert current['version'] == ingredient_only['version'] and current['ingredients'] == ingredients

        order_only = call(owner, 'PUT', path, 200, command(current, steps=list(reversed(current['steps']))))
        assert order_only['version'] != current['version']
        assert order_only['steps'] == list(reversed(current['steps']))
        assert all(order_only[key] == current[key] for key in FIELDS if key != 'steps')
        current = call(editor, 'GET', path, 200)
        assert current['version'] == order_only['version'] and current['steps'] == order_only['steps']

        photo = call(owner, 'PUT', path + '/image', 200, PNG, tag(current['version']))
        assert photo['version'] != current['version']
        assert call(editor, 'GET', path + '/image', 200) == PNG
        removed = call(editor, 'DELETE', path + '/image', 200, headers=tag(photo['version']))
        assert removed['version'] != photo['version'] and removed['imageUrl'] is None
        current = call(owner, 'GET', path, 200)
        assert current['version'] == removed['version'] and current['imageUrl'] is None
        call(owner, 'GET', path + '/image', 404)

        # A rejected insert on a recipe with no image cannot leave a detached blob.
        call(editor, 'PUT', path + '/image', 412, PNG, tag(photo['version']), code='concurrency_conflict')
        after_stale_photo = call(owner, 'GET', path, 200)
        assert after_stale_photo['version'] == removed['version'] and after_stale_photo['imageUrl'] is None
        call(owner, 'GET', path + '/image', 404)
        with closing(sqlite3.connect(Path(directory) / 'recipes.db')) as db:
            assert db.execute('SELECT COUNT(*) FROM Images WHERE RecipeId = ?', (recipe['id'].upper(),)).fetchone()[0] == 0
        assert call(owner, 'GET', book_path, 200)['version'] == book_version
        recipe_version = removed['version']

        # Adding, accepting, removing and leaving each change the cookbook only.
        accept(member, invite(member, 'audit-member', 'Viewer'))
        before_remove = call(owner, 'GET', book_path, 200)
        removed_member = call(owner, 'DELETE', book_path + '/members/' + member_me['id'], 200,
                              headers=tag(before_remove['version']))
        after_remove = call(owner, 'GET', book_path, 200)
        assert after_remove['version'] == removed_member['version'] != before_remove['version']
        assert call(owner, 'GET', path, 200)['version'] == recipe_version
        assert not any(item['userId'] == member_me['id'] for item in
                       call(owner, 'GET', book_path + '/members', 200)['members'])
        call(member, 'GET', book_path, 404, code='resource_unavailable')
        accept(member, invite(member, 'audit-member', 'Viewer'))
        before_leave = call(owner, 'GET', book_path, 200)
        call(member, 'POST', book_path + '/leave', 204, {'expectedVersion': before_leave['version']})
        after_leave = call(owner, 'GET', book_path, 200)
        assert after_leave['version'] != before_leave['version']
        assert call(owner, 'GET', path, 200)['version'] == recipe_version
        assert not any(item['userId'] == member_me['id'] for item in
                       call(owner, 'GET', book_path + '/members', 200)['members'])
        call(member, 'GET', book_path, 404, code='resource_unavailable')

        # Two anonymous sessions redeem one account invitation at the same barrier.
        invitation = call(owner, 'POST', '/api/admin/invitations', 200)
        token = urllib.parse.parse_qs(urllib.parse.urlsplit(invitation['link']).fragment)['token'][0]
        redemption_gate = threading.Barrier(2)

        def redeem(username):
            actor = client()
            redemption_gate.wait(timeout=10)
            return username, send(actor, 'POST', '/api/invitations/redeem', {
                'token': token, 'username': username, 'displayName': username,
                'email': username + '@example.test', 'password': password})

        with ThreadPoolExecutor(max_workers=2) as pool:
            first = pool.submit(redeem, 'redemption-first')
            second = pool.submit(redeem, 'redemption-second')
            redemptions = [first.result(), second.result()]
        assert sorted(result[0] for _, result in redemptions) == [204, 400]
        assert next(data for _, (status, data) in redemptions if status == 400)['code'] == 'invalid_invitation'
        registered = next(username for username, (status, _) in redemptions if status == 204)
        rejected = next(username for username, (status, _) in redemptions if status == 400)
        accounts = call(owner, 'GET', '/api/admin/accounts', 200)
        assert sum(account['username'] == registered for account in accounts) == 1
        assert not any(account['username'] == rejected for account in accounts)
        checks += 2
        with closing(sqlite3.connect(Path(directory) / 'recipes.db')) as db:
            assert db.execute('SELECT COUNT(*) FROM Images').fetchone()[0] == 0
            assert not db.execute('PRAGMA foreign_key_check').fetchall()
            assert db.execute("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%OptimisticAggregateVersions'").fetchone()[0] == 1
            assert db.execute('SELECT COUNT(*) FROM Invitations WHERE ConsumedAtUtc IS NOT NULL').fetchone()[0] == 3
        print(f'Passed 10 focused audit scenarios and {checks} HTTP checks: same-recipe owner/editor barrier race, '
              'ingredient-only and ordering-only writes, photo removal and stale insertion rollback, '
              'four membership version transitions, and single-use invitation barrier race.')
    finally:
        process.terminate()
        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=15)
        log.close()
