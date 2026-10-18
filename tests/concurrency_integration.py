"""Version contracts, SQLite aggregate atomicity and authorization. No real data or providers."""
from contextlib import closing
from concurrent.futures import ThreadPoolExecutor
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
import uuid

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / 'backend/RecipeAtlas.Api/bin/Debug/net10.0/Recipoke.Api.dll'
DOTNET = os.environ.get('RECIPOKE_DOTNET', 'dotnet')
PNG = b'\x89PNG\r\n\x1a\noriginal synthetic picture'
OTHER_PNG = PNG + b' replacement'
checks = 0


def client():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def tag(version):
    return {'If-Match': '"' + version + '"'}


with tempfile.TemporaryDirectory(prefix='recipoke-concurrency-') as directory:
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        port = sock.getsockname()[1]
    base = f'http://127.0.0.1:{port}'
    password = secrets.token_urlsafe(24)
    env = dict(os.environ, DataDirectory=directory, ASPNETCORE_ENVIRONMENT='Development',
               ASPNETCORE_URLS=base, Accounts__PublicAppUrl='http://localhost:4200', RecipeText__ApiKey='')
    setup = subprocess.run([DOTNET, str(DLL), '--bootstrap-admin'], input=f'administrator\nOwner\n{password}\n',
                           capture_output=True, text=True, cwd=ROOT, env=env)
    assert setup.returncode == 0, 'Synthetic bootstrap failed'
    log = open(Path(directory) / 'api.log', 'w')
    process = subprocess.Popen([DOTNET, str(DLL)], cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)

    def call(actor, method, path, status, body=None, headers=None, code=None):
        global checks
        request_headers = {'X-RecipeAtlas': '1', **(headers or {})}
        if isinstance(body, dict):
            body = json.dumps(body).encode()
            request_headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(base + path, data=body, method=method, headers=request_headers)
        try:
            response = actor.open(request, timeout=40)
        except urllib.error.HTTPError as error:
            response = error
        raw = response.read()
        assert response.status == status, (method, path, response.status, status, raw[:180])
        assert 'no-store' in response.headers.get('Cache-Control', '')
        data = json.loads(raw) if raw and 'json' in response.headers.get('Content-Type', '') else raw
        if code:
            assert data.get('code') == code, (method, path, data.get('code'), code)
            assert not {'version', 'current', 'recipe', 'members', 'cookbook'}.intersection(data), 'Conflict leaked data'
        checks += 1
        return data

    def register(owner, username):
        invitation = call(owner, 'POST', '/api/admin/invitations', 200)
        token = urllib.parse.parse_qs(urllib.parse.urlsplit(invitation['link']).fragment)['token'][0]
        call(public, 'POST', '/api/invitations/redeem', 204, {
            'token': token, 'username': username, 'displayName': username,
            'email': username + '@example.test', 'password': password})
        actor = client()
        call(actor, 'POST', '/api/auth/login', 204, {'username': username, 'password': password})
        return actor, call(actor, 'GET', '/api/auth/me', 200)

    def command(version, **changes):
        return {**copy.deepcopy(payload), 'expectedVersion': version, **changes}

    def share(actor, username, role):
        current = call(owner, 'GET', book_path, 200)
        sent = call(owner, 'POST', book_path + '/members', 200, {
            'username': username, 'role': role, 'expectedVersion': current['version']})
        item = next(i for i in call(actor, 'GET', '/api/cookbook-invitations', 200) if i['cookbookId'] == book['id'])
        assert item['cookbookVersion'] == sent['version']
        call(actor, 'POST', '/api/cookbook-invitations/' + item['id'], 204,
             {'accept': True, 'expectedVersion': item['cookbookVersion']})

    try:
        for _ in range(150):
            if process.poll() is not None:
                raise RuntimeError('Synthetic API exited')
            try:
                with urllib.request.urlopen(base + '/health', timeout=1):
                    break
            except OSError:
                time.sleep(.1)  # Startup readiness only; races below use explicit barriers/interceptors.
        else:
            raise RuntimeError('Synthetic API startup deadline')
        public, owner = client(), client()
        call(owner, 'POST', '/api/auth/login', 204, {'username': 'administrator', 'password': password})
        editor, editor_me = register(owner, 'editor')
        viewer, viewer_me = register(owner, 'viewer')
        outsider, outsider_me = register(owner, 'outsider')
        book = call(owner, 'POST', '/api/cookbooks', 201, {'name': 'Concurrent cookbook'})
        book_path = '/api/cookbooks/' + book['id']
        share(editor, 'editor', 'Editor')
        share(viewer, 'viewer', 'Viewer')
        payload = json.loads((ROOT / 'sample-recipe.json').read_text())
        recipe = call(owner, 'POST', book_path + '/recipes', 201, payload)
        path = book_path + '/recipes/' + recipe['id']
        original = call(editor, 'GET', path, 200)
        book_version = call(owner, 'GET', book_path, 200)['version']
        assert original['version'] == recipe['version'] and uuid.UUID(original['version']).int
        # Explicit wire contracts: old clients never bypass preconditions.
        call(owner, 'PUT', path, 428, payload, code='concurrency_precondition_required')
        for malformed in ['', 'not-a-version', str(uuid.UUID(int=0)), ' ' + recipe['version'], '*']:
            call(owner, 'PUT', path, 400, command(malformed), code='invalid_concurrency_precondition')
        call(owner, 'DELETE', path, 428, code='concurrency_precondition_required')
        for malformed in ['*', 'W/"' + recipe['version'] + '"', recipe['version'],
                          '"' + recipe['version'] + '","' + recipe['version'] + '"']:
            call(owner, 'DELETE', path, 400, headers={'If-Match': malformed}, code='invalid_concurrency_precondition')
        call(owner, 'PUT', path + '/image', 428, PNG, code='concurrency_precondition_required')
        call(viewer, 'PUT', path, 403, payload)  # Permissions take priority over missing/stale versions.
        call(outsider, 'DELETE', path, 404, code='resource_unavailable')
        call(public, 'PUT', path, 401, payload)
        # Two snapshots, one successful aggregate update; rejected child replacements remain atomic.
        first = call(owner, 'PUT', path, 200, command(recipe['version'], title='Owner saved', steps=['Owner first', 'Owner second']))
        call(editor, 'PUT', path, 409, command(original['version'], title='Must not overwrite',
             ingredients=[{'name': 'Must not persist', 'quantity': '9', 'unit': 'g', 'note': None}], steps=['Rejected']),
             code='concurrency_conflict')
        persisted = call(editor, 'GET', path, 200)
        # Existing UTC timestamps are serialized with/without Z depending on EF materialization.
        for key in ['createdAtUtc', 'updatedAtUtc']:
            assert persisted[key].removesuffix('Z') == first[key].removesuffix('Z')
        assert {k: v for k, v in persisted.items() if not k.endswith('AtUtc')} == {k: v for k, v in first.items() if not k.endswith('AtUtc')}
        child = call(editor, 'PUT', path, 200, command(first['version'], title=first['title'], steps=['Child-only change']))
        assert child['version'] != first['version'] and child['steps'] == ['Child-only change']
        photo = call(owner, 'PUT', path + '/image', 200, PNG, tag(child['version']))
        assert photo['version'] != child['version']
        call(editor, 'PUT', path + '/image', 412, OTHER_PNG, tag(child['version']), code='concurrency_conflict')
        call(editor, 'DELETE', path + '/image', 412, headers=tag(child['version']), code='concurrency_conflict')
        call(editor, 'DELETE', path, 412, headers=tag(child['version']), code='concurrency_conflict')
        assert call(editor, 'GET', path + '/image', 200) == PNG
        current = call(editor, 'GET', path, 200)
        assert current['version'] == photo['version'] and current['imageUrl'] == photo['imageUrl']
        reviewed = call(editor, 'PUT', path, 200, command(current['version'], title='Explicitly reviewed'))
        call(owner, 'PUT', path, 409, command(current['version']), code='concurrency_conflict')
        # Independent recipes and cookbook versions do not interfere with recipe writes.
        second = call(editor, 'POST', book_path + '/recipes', 201, payload)
        second_path = book_path + '/recipes/' + second['id']
        call(editor, 'PUT', second_path, 200, command(second['version'], title='Independent recipe'))
        assert call(owner, 'GET', path, 200)['version'] == reviewed['version']
        assert call(owner, 'GET', book_path, 200)['version'] == book_version
        # Cookbooks deliberately share one token across metadata, pending invitations, covers and memberships.
        renamed = call(owner, 'PUT', book_path, 200, {'name': 'Renamed', 'expectedVersion': book_version})
        call(owner, 'PUT', book_path, 409, {'name': 'Stale rename', 'expectedVersion': book_version}, code='concurrency_conflict')
        state = call(owner, 'GET', book_path + '/members', 200)
        assert state['cookbook']['version'] == renamed['version']
        changed = call(owner, 'PUT', book_path + '/members/' + viewer_me['id'], 200,
                       {'role': 'Editor', 'expectedVersion': state['cookbook']['version']})
        call(owner, 'PUT', book_path, 409, {'name': 'Must not erase membership change', 'expectedVersion': renamed['version']}, code='concurrency_conflict')
        call(owner, 'PUT', book_path + '/members/' + viewer_me['id'], 409,
             {'role': 'Viewer', 'expectedVersion': renamed['version']}, code='concurrency_conflict')
        call(owner, 'DELETE', book_path + '/members/' + viewer_me['id'], 412,
             headers=tag(renamed['version']), code='concurrency_conflict')
        call(viewer, 'POST', book_path + '/leave', 409, {'expectedVersion': renamed['version']}, code='concurrency_conflict')
        state = call(owner, 'GET', book_path + '/members', 200)
        assert state['cookbook']['version'] == changed['version']
        assert next(m for m in state['members'] if m['userId'] == viewer_me['id'])['role'] == 'Editor'
        call(viewer, 'POST', book_path + '/leave', 204, {'expectedVersion': state['cookbook']['version']})
        call(viewer, 'GET', book_path, 404, code='resource_unavailable')
        call(viewer, 'PUT', path, 404, command(reviewed['version']), code='resource_unavailable')
        # Simultaneous owner sessions cannot both commit the same displayed version.
        race_version = call(owner, 'GET', book_path, 200)['version']
        owner2 = client()
        call(owner2, 'POST', '/api/auth/login', 204, {'username': 'administrator', 'password': password})
        gate = threading.Barrier(2)
        def racing_rename(actor, name):
            gate.wait(timeout=10)
            request = urllib.request.Request(base + book_path, data=json.dumps({'name': name, 'expectedVersion': race_version}).encode(),
                method='PUT', headers={'X-RecipeAtlas': '1', 'Content-Type': 'application/json'})
            try:
                response = actor.open(request, timeout=40)
            except urllib.error.HTTPError as error:
                response = error
            return response.status, json.loads(response.read())
        with ThreadPoolExecutor(max_workers=2) as pool:
            a = pool.submit(racing_rename, owner, 'First owner session')
            b = pool.submit(racing_rename, owner2, 'Second owner session')
            raced = [a.result(), b.result()]
        assert sorted(status for status, _ in raced) == [200, 409]
        assert next(data for status, data in raced if status == 409)['code'] == 'concurrency_conflict'
        checks += 2
        # Stale cover/deletion changes preserve all current book content.
        latest_book = call(owner, 'GET', book_path, 200)
        cover = call(owner, 'PUT', book_path + '/image', 200, PNG, tag(latest_book['version']))
        call(owner, 'DELETE', book_path + '/image', 412, headers=tag(latest_book['version']), code='concurrency_conflict')
        call(owner, 'DELETE', book_path, 412, headers=tag(latest_book['version']), code='concurrency_conflict')
        assert call(owner, 'GET', cover['imageUrl'], 200) == PNG
        assert call(owner, 'GET', path, 200)['version'] == reviewed['version']
        # Pending invitations are part of the same cookbook consistency boundary.
        before_invite = call(owner, 'GET', book_path, 200)
        for method, suffix, body in [
            ('PUT', '', {'name': 'Missing version'}),
            ('POST', '/members', {'username': 'outsider', 'role': 'Viewer'}),
            ('PUT', '/members/' + editor_me['id'], {'role': 'Viewer'}),
            ('DELETE', '/members/' + editor_me['id'], None),
            ('DELETE', '/image', None), ('DELETE', '', None),
        ]:
            call(owner, method, book_path + suffix, 428, body, code='concurrency_precondition_required')
        call(editor, 'POST', book_path + '/leave', 428, {}, code='concurrency_precondition_required')
        call(owner, 'PUT', book_path + '/image', 428, PNG, code='concurrency_precondition_required')
        sent = call(owner, 'POST', book_path + '/members', 200, {
            'username': 'outsider', 'role': 'Viewer', 'expectedVersion': before_invite['version']})
        pending = next(i for i in call(outsider, 'GET', '/api/cookbook-invitations', 200) if i['cookbookId'] == book['id'])
        assert pending['cookbookVersion'] == sent['version'] != before_invite['version']
        pending_path = '/api/cookbook-invitations/' + pending['id']
        call(outsider, 'POST', pending_path, 428, {'accept': True}, code='concurrency_precondition_required')
        after_rename = call(owner, 'PUT', book_path, 200, {'name': 'Changed invitation cookbook', 'expectedVersion': sent['version']})
        call(outsider, 'POST', pending_path, 409, {'accept': True, 'expectedVersion': pending['cookbookVersion']}, code='concurrency_conflict')
        call(owner, 'DELETE', book_path + '/invitations/' + outsider_me['id'], 412, headers=tag(sent['version']), code='concurrency_conflict')
        assert call(outsider, 'GET', '/api/cookbook-invitations', 200)[0]['id'] == pending['id']
        call(outsider, 'GET', book_path, 404, code='resource_unavailable')
        call(outsider, 'POST', pending_path, 204, {'accept': False, 'expectedVersion': after_rename['version']})
        assert call(owner, 'GET', book_path, 200)['version'] != after_rename['version']
        # Two simultaneous membership commands with one displayed version cannot both commit.
        member_race_version = call(owner, 'GET', book_path, 200)['version']
        membership_gate = threading.Barrier(2)
        def racing_member(actor, username):
            membership_gate.wait(timeout=10)
            request = urllib.request.Request(base + book_path + '/members',
                data=json.dumps({'username': username, 'role': 'Viewer', 'expectedVersion': member_race_version}).encode(),
                method='POST', headers={'X-RecipeAtlas': '1', 'Content-Type': 'application/json'})
            try: response = actor.open(request, timeout=40)
            except urllib.error.HTTPError as error: response = error
            return response.status, json.loads(response.read())
        with ThreadPoolExecutor(max_workers=2) as pool:
            a = pool.submit(racing_member, owner, 'outsider')
            b = pool.submit(racing_member, owner2, 'viewer')
            member_race = [a.result(), b.result()]
        assert sorted(status for status, _ in member_race) == [200, 409]
        assert next(data for status, data in member_race if status == 409)['code'] == 'concurrency_conflict'
        checks += 2
        state = call(owner, 'GET', book_path + '/members', 200)
        assert len([m for m in state['members'] if m['isPending']]) == 1
        assert len({m['userId'] for m in state['members']}) == len(state['members'])
        assert len([m for m in state['members'] if m['role'] == 'Owner']) == 1
        # Downgrade and removal beat stale versions; no server data is included in conflict errors.
        state = call(owner, 'GET', book_path + '/members', 200)
        downgraded = call(owner, 'PUT', book_path + '/members/' + editor_me['id'], 200,
                          {'role': 'Viewer', 'expectedVersion': state['cookbook']['version']})
        call(editor, 'PUT', path, 403, command(original['version']))
        call(owner, 'DELETE', book_path + '/members/' + editor_me['id'], 200, headers=tag(downgraded['version']))
        call(editor, 'GET', path, 404, code='resource_unavailable')
        call(editor, 'PUT', path, 404, command(original['version']), code='resource_unavailable')
        call(owner, 'PUT', '/api/admin/accounts/' + outsider_me['id'] + '/status', 204, {'isActive': False})
        call(outsider, 'PUT', path, 401, command(original['version']))
        # A deleted original is unavailable, not an editable conflict.
        call(owner, 'DELETE', path, 204, headers=tag(reviewed['version']))
        call(owner, 'PUT', path, 404, command(reviewed['version']), code='resource_unavailable')
        with closing(sqlite3.connect(Path(directory) / 'recipes.db')) as db, db:
            assert not db.execute('PRAGMA foreign_key_check').fetchall()
            for table in ['Recipes', 'Cookbooks']:
                tokens = [row[0] for row in db.execute('SELECT Version FROM ' + table)]
                assert all(uuid.UUID(token).int for token in tokens) and len(set(tokens)) == len(tokens)
        print(f'Passed {checks} concurrency HTTP checks: aggregate atomicity, strong preconditions, authorization, reviewed retries and coordinated owner race.')
    finally:
        process.terminate()
        process.wait(timeout=15)
        log.close()
