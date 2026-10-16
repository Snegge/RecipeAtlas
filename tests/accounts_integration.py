"""Invitation, session and cookbook isolation checks. Synthetic SQLite; no external providers."""
import base64
from concurrent.futures import ThreadPoolExecutor
import hashlib
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import socket
import sqlite3
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / 'backend/RecipeAtlas.Api/bin/Debug/net10.0/Recipoke.Api.dll'
DOTNET = os.environ.get('RECIPOKE_DOTNET', 'dotnet')
checks = 0
password = secrets.token_urlsafe(24)


def client():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


with tempfile.TemporaryDirectory(prefix='recipoke-accounts-') as data:
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0)); port = sock.getsockname()[1]
    base = f'http://127.0.0.1:{port}'
    environment = dict(os.environ, ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS=base,
                       DataDirectory=data, Accounts__PublicAppUrl='https://recipoke.example', RecipeText__ApiKey='')
    log = open(Path(data) / 'api.log', 'w')
    process = None

    def call(opener, method, path, expected, body=None, csrf=True, headers=None):
        global checks
        request_headers = dict(headers or {})
        if csrf: request_headers['X-RecipeAtlas'] = '1'
        if isinstance(body, dict):
            body = json.dumps(body).encode(); request_headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(base + path, data=body, method=method, headers=request_headers)
        try: response = opener.open(request, timeout=40)
        except urllib.error.HTTPError as error: response = error
        raw = response.read()
        assert response.code == expected, (method, path, response.code, expected)
        assert 'no-store' in response.headers.get('Cache-Control', ''), 'Private API responses must not be cached'
        assert response.headers.get('Referrer-Policy') == 'no-referrer'
        checks += 1
        return json.loads(raw) if raw and 'json' in response.headers.get('Content-Type', '') else raw

    def start():
        global process
        process = subprocess.Popen([DOTNET, str(DLL)], cwd=ROOT, env=environment, stdout=log, stderr=subprocess.STDOUT)
        for _ in range(150):
            if process.poll() is not None: raise RuntimeError('API exited; inspect isolated startup')
            try:
                with urllib.request.urlopen(base + '/health', timeout=1): return
            except OSError: time.sleep(.1)
        raise RuntimeError('API startup deadline')

    def stop():
        global process
        if process and process.poll() is None: process.terminate(); process.wait(timeout=10)
        process = None

    def restart():
        stop(); start()

    def invite(admin):
        created = call(admin, 'POST', '/api/admin/invitations', 200, headers={'Host':'untrusted.example'})
        assert created['link'].startswith('https://recipoke.example/invite#token=')
        token = urllib.parse.parse_qs(urllib.parse.urlsplit(created['link']).fragment)['token'][0]
        return created['invitation']['id'], token

    def register(admin, username):
        invitation_id, token = invite(admin)
        public = client()
        first = call(public, 'POST', '/api/invitations/validate', 200, {'token': token})
        assert first['status'] == 'Pending'
        assert call(public, 'POST', '/api/invitations/validate', 200, {'token': token})['status'] == 'Pending'
        call(public, 'POST', '/api/invitations/redeem', 204,
             {'token': token, 'username': username, 'displayName': username, 'password': password})
        user = client()
        call(user, 'POST', '/api/auth/login', 204, {'username': username.upper(), 'password': password})
        return user, call(user, 'GET', '/api/auth/me', 200)

    try:
        start()
        public = client()
        call(public, 'GET', '/api/cookbooks', 503)
        stop()
        result = subprocess.run([DOTNET, str(DLL), '--bootstrap-admin'], input=f'admin\nAdministrator\n{password}\n',
                                text=True, capture_output=True, env=environment, cwd=ROOT)
        assert result.returncode == 0, 'Bootstrap failed'
        again = subprocess.run([DOTNET, str(DLL), '--bootstrap-admin'], input='', text=True, capture_output=True, env=environment, cwd=ROOT)
        assert again.returncode == 0 and 'already complete' in again.stdout
        start()
        call(public, 'POST', '/api/auth/register', 404, {'username': 'intruder', 'password': password})
        call(public, 'POST', '/api/invitations/redeem', 400, {'username': 'intruder', 'displayName': 'Intruder', 'password': password})
        call(public, 'POST', '/api/auth/login', 403, {'username': 'admin', 'password': password}, csrf=False)
        admin = client(); call(admin, 'POST', '/api/auth/login', 204, {'username': 'admin', 'password': password})
        admin_me = call(admin, 'GET', '/api/auth/me', 200)
        owner2, owner_me = register(admin, 'owner2')
        editor, editor_me = register(admin, 'editor')
        viewer, viewer_me = register(admin, 'viewer')
        stranger, stranger_me = register(admin, 'stranger')
        assert not owner_me['isAdministrator']
        call(editor, 'POST', '/api/admin/invitations', 403)
        call(viewer, 'GET', '/api/admin/accounts', 403)
        books = call(owner2, 'GET', '/api/cookbooks', 200); assert len(books) == 1 and books[0]['role'] == 'Owner'
        book = books[0]['id']; prefix = f'/api/cookbooks/{book}'
        call(admin, 'GET', prefix, 404)  # Administration is not cookbook ownership.
        call(owner2, 'POST', prefix + '/members', 204, {'username': 'EDITOR', 'role': 'Editor'})
        call(owner2, 'POST', prefix + '/members', 204, {'username': 'viewer', 'role': 'Viewer'})
        call(owner2, 'POST', prefix + '/members', 409, {'username': 'editor', 'role': 'Viewer'})
        call(owner2, 'POST', prefix + '/members', 400, {'username': 'stranger', 'role': 'Owner'})
        call(owner2, 'POST', prefix + '/members', 404, {'username': 'edit', 'role': 'Viewer'})
        call(editor, 'GET', prefix + '/members', 403)
        call(viewer, 'PUT', prefix, 403, {'name': 'Denied'})
        call(owner2, 'POST', prefix + '/leave', 403)
        call(owner2, 'DELETE', prefix + '/members/' + owner_me['id'], 403)
        call(owner2, 'PUT', prefix, 204, {'name': 'Shared cookbook'})
        cookbook2 = call(editor, 'POST', '/api/cookbooks', 201, {'name': 'Editor private'})['id']
        payload = json.loads((ROOT / 'sample-recipe.json').read_text())
        recipe = call(editor, 'POST', prefix + '/recipes', 201, payload)
        recipe_path = prefix + '/recipes/' + recipe['id']
        png = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWZkAAAAASUVORK5CYII=')
        call(editor, 'PUT', recipe_path + '/image', 200, png)
        assert call(viewer, 'GET', recipe_path + '/image', 200) == png
        call(viewer, 'PUT', recipe_path, 403, payload)
        call(viewer, 'DELETE', recipe_path, 403)
        call(viewer, 'DELETE', recipe_path + '/image', 403)
        call(stranger, 'GET', recipe_path, 404)
        call(stranger, 'GET', recipe_path + '/image', 404, headers={'If-None-Match': '*', 'If-Modified-Since': 'Wed, 21 Oct 2037 07:28:00 GMT'})
        call(public, 'GET', recipe_path + '/image', 401)
        call(admin, 'GET', recipe_path, 404)
        call(editor, 'GET', f'/api/cookbooks/{cookbook2}/recipes/' + recipe['id'], 404)
        assert call(stranger, 'GET', '/api/cookbooks', 200)[0]['id'] != book
        call(stranger, 'GET', prefix + '/recipes?search=pasta', 404)
        call(editor, 'PUT', recipe_path, 200, {**payload, 'cookbookId': cookbook2})
        assert call(editor, 'GET', recipe_path, 200)['id'] == recipe['id']
        call(editor, 'GET', '/api/recipes/' + recipe['id'], 404)
        # Import remains draft-only and does not require a writable cookbook.
        missing_ai = call(viewer, 'POST', '/api/recipes/import', 503, {'text': 'Eggs\n2 eggs\nCook.'})
        assert missing_ai['code'] == 'ai_not_configured'
        call(owner2, 'PUT', prefix + '/members/' + editor_me['id'], 204, {'role': 'Viewer'})
        call(editor, 'PUT', recipe_path, 403, payload)
        call(owner2, 'DELETE', prefix + '/members/' + editor_me['id'], 204)
        call(editor, 'GET', recipe_path, 404)
        call(viewer, 'POST', prefix + '/leave', 204)
        call(viewer, 'GET', prefix, 404)
        # Refresh limit windows without dropping cookies/keys/database.
        restart()
        second_session = client(); call(second_session, 'POST', '/api/auth/login', 204, {'username': 'owner2', 'password': password})
        call(owner2, 'POST', '/api/auth/password', 400, {'currentPassword': 'wrong', 'newPassword': password + '-new'})
        call(owner2, 'POST', '/api/auth/password', 204, {'currentPassword': password, 'newPassword': password + '-new'})
        call(second_session, 'GET', '/api/auth/me', 401)
        call(owner2, 'GET', '/api/auth/me', 200)
        call(admin, 'PUT', '/api/admin/accounts/' + owner_me['id'] + '/status', 204, {'isActive': False})
        call(owner2, 'GET', '/api/auth/me', 401)
        call(second_session, 'POST', '/api/auth/login', 401, {'username': 'owner2', 'password': password + '-new'})
        call(admin, 'PUT', '/api/admin/accounts/' + owner_me['id'] + '/status', 204, {'isActive': True})
        call(owner2, 'GET', '/api/auth/me', 401)
        call(owner2, 'POST', '/api/auth/login', 204, {'username': 'owner2', 'password': password + '-new'})
        call(admin, 'PUT', '/api/admin/accounts/' + admin_me['id'] + '/status', 403, {'isActive': False})
        call(admin, 'PUT', '/api/admin/accounts/' + viewer_me['id'] + '/status', 400, {})
        # Active members keep access when their owner is deactivated.
        call(owner2, 'POST', prefix + '/members', 204, {'username': 'viewer', 'role': 'Viewer'})
        call(admin, 'PUT', '/api/admin/accounts/' + owner_me['id'] + '/status', 204, {'isActive': False})
        call(viewer, 'GET', recipe_path, 200)
        call(editor, 'POST', f'/api/cookbooks/{cookbook2}/members', 404, {'username':'owner2','role':'Viewer'})
        call(stranger, 'POST', '/api/auth/logout', 204)
        call(stranger, 'GET', '/api/auth/me', 401)
        # Invitation terminal states, duplicate username rollback, one-time secrets.
        restart()
        invitation_id, token = invite(admin)
        attempt = {'token': token, 'username': 'VIEWER', 'displayName': 'Duplicate', 'password': password}
        call(public, 'POST', '/api/invitations/redeem', 400, {**attempt,'username':'privileged','isAdministrator':True})
        call(public, 'POST', '/api/invitations/redeem', 400, attempt)
        assert call(public, 'POST', '/api/invitations/validate', 200, {'token': token})['status'] == 'Pending'
        attempt['username'] = 'replacement'
        call(public, 'POST', '/api/invitations/redeem', 204, attempt)
        call(public, 'POST', '/api/invitations/redeem', 400, attempt)
        assert call(public, 'POST', '/api/invitations/validate', 200, {'token': token})['status'] == 'Consumed'
        call(admin, 'DELETE', '/api/admin/invitations/' + invitation_id, 409)
        revoked_id, revoked = invite(admin)
        call(editor, 'DELETE', '/api/admin/invitations/' + revoked_id, 403)
        call(admin, 'DELETE', '/api/admin/invitations/' + revoked_id, 204)
        assert call(public, 'POST', '/api/invitations/validate', 200, {'token': revoked})['status'] == 'Revoked'
        call(public, 'POST', '/api/invitations/redeem', 400, {**attempt, 'token': revoked, 'username': 'revoked'})
        expired_id, expired = invite(admin)
        with sqlite3.connect(Path(data) / 'recipes.db') as db:
            db.execute('UPDATE Invitations SET ExpiresAtUtc=0 WHERE Id=?', (expired_id.upper(),))
            hashes = [row[0] for row in db.execute('SELECT TokenHash FROM Invitations')]
            assert all(len(value) == 64 and token not in value for value in hashes)
            assert hashlib.sha256(token.encode()).hexdigest().upper() in hashes
        assert call(public, 'POST', '/api/invitations/validate', 200, {'token': expired})['status'] == 'Expired'
        call(public, 'POST', '/api/invitations/redeem', 400, {**attempt, 'token': expired, 'username': 'expired'})
        listing = call(admin, 'GET', '/api/admin/invitations', 200)
        assert all(set(item) == {'id', 'createdAtUtc', 'expiresAtUtc', 'status'} for item in listing)
        # Concurrent attempts, including revocation: the first committed transaction wins.
        race_id, race_token = invite(admin)
        def redeem_race(username):
            data_json = {'token': race_token, 'username': username, 'displayName': username, 'password': password}
            request = urllib.request.Request(base + '/api/invitations/redeem', data=json.dumps(data_json).encode(),
                                             headers={'Content-Type': 'application/json', 'X-RecipeAtlas': '1'})
            try: return urllib.request.urlopen(request, timeout=40).status
            except urllib.error.HTTPError as error: return error.code
        with ThreadPoolExecutor(2) as pool: statuses = list(pool.map(redeem_race, ['race-one', 'race-two']))
        assert sorted(statuses) == [204, 400], statuses
        with sqlite3.connect(Path(data) / 'recipes.db') as db:
            assert db.execute("SELECT COUNT(*) FROM AspNetUsers WHERE UserName IN ('race-one','race-two')").fetchone()[0] == 1
            assert db.execute('SELECT COUNT(*) FROM Cookbooks').fetchone()[0] == db.execute('SELECT COUNT(*) FROM AspNetUsers').fetchone()[0] + 1  # editor created an extra cookbook
        # Revocation and redemption share the same immediate transaction boundary.
        revoke_race_id, revoke_race_token = invite(admin)
        def raw_request(opener, method, path, body=None):
            headers={'X-RecipeAtlas':'1'}
            if body is not None: body=json.dumps(body).encode();headers['Content-Type']='application/json'
            req=urllib.request.Request(base+path,data=body,method=method,headers=headers)
            try:
                with opener.open(req,timeout=40) as response:return response.status
            except urllib.error.HTTPError as error:return error.code
        with ThreadPoolExecutor(2) as pool:
            redemption=pool.submit(raw_request,client(),'POST','/api/invitations/redeem',
                                   {'token':revoke_race_token,'username':'revoke-race','displayName':'Race','password':password})
            revocation=pool.submit(raw_request,admin,'DELETE','/api/admin/invitations/'+revoke_race_id)
            result=(redemption.result(),revocation.result())
        assert result in [(204,409),(400,204)], result
        final=call(public,'POST','/api/invitations/validate',200,{'token':revoke_race_token})['status']
        assert final==('Consumed' if result[0]==204 else 'Revoked')
        # Unique normalized names also survive simultaneous independent invitations.
        _, first_token=invite(admin);_, second_token=invite(admin)
        with ThreadPoolExecutor(2) as pool:
            futures=[pool.submit(raw_request,client(),'POST','/api/invitations/redeem',
                                 {'token':tok,'username':name,'displayName':'Duplicate race','password':password})
                     for tok,name in [(first_token,'unique-race'),(second_token,'UNIQUE-RACE')]]
            duplicate_results=[f.result() for f in futures]
        assert sorted(duplicate_results)==[204,400], duplicate_results
        remaining=first_token if duplicate_results[0]==400 else second_token
        assert call(public,'POST','/api/invitations/validate',200,{'token':remaining})['status']=='Pending'
        # Framework lockout remains effective when the global limiter is reset.
        restart()
        for _ in range(5): call(public, 'POST', '/api/auth/login', 401, {'username': 'editor', 'password': 'wrong'})
        call(public, 'POST', '/api/auth/login', 401, {'username': 'editor', 'password': password})
        restart()
        for _ in range(30):call(public,'POST','/api/invitations/validate',200,{'token':token})
        call(public,'POST','/api/invitations/validate',429,{'token':token})
        for _ in range(5):call(public,'POST','/api/auth/login',401,{'username':'admin','password':'wrong'})
        call(public,'POST','/api/auth/login',401,{'username':'admin','password':password})
        log.flush()
        assert all(secret not in (Path(data)/'api.log').read_text() for secret in [password,token,race_token,revoked,expired])
        print(f'Passed {checks} account/cookbook HTTP checks plus concurrent redemption, uniqueness and token-storage assertions.')
    finally:
        stop(); log.close()
