"""Shopping HTTP contracts and concurrent retries against disposable real SQLite."""
from concurrent.futures import ThreadPoolExecutor
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
DLL = Path(os.environ.get('RECIPOKE_DLL', str(ROOT / 'backend/RecipeAtlas.Api/bin/Debug/net10.0/Recipoke.Api.dll')))
DOTNET = os.environ.get('RECIPOKE_DOTNET', 'dotnet')


def uid():
    return str(uuid.uuid4())


def client():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def main():
    checks = 0
    with tempfile.TemporaryDirectory(prefix='recipoke-shopping-http-') as directory:
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0))
            base = f'http://127.0.0.1:{sock.getsockname()[1]}'
        password = secrets.token_urlsafe(24)
        environment = dict(os.environ, DataDirectory=directory, ASPNETCORE_ENVIRONMENT='Development',
                           ASPNETCORE_URLS=base, Accounts__PublicAppUrl='http://localhost:4200', RecipeText__ApiKey='')
        setup = subprocess.run([DOTNET, str(DLL), '--bootstrap-admin'], cwd=ROOT, env=environment,
                               input=f'administrator\nOwner\n{password}\n', text=True, capture_output=True)
        assert setup.returncode == 0, 'Synthetic bootstrap failed'
        process = None
        with open(Path(directory) / 'api.log', 'w') as log:
            def start():
                running = subprocess.Popen([DOTNET, str(DLL)], cwd=ROOT, env=environment,
                                           stdout=log, stderr=subprocess.STDOUT)
                for _ in range(150):
                    if running.poll() is not None:
                        raise AssertionError('Synthetic API exited')
                    try:
                        with urllib.request.urlopen(base + '/health', timeout=1):
                            return running
                    except OSError:
                        time.sleep(.1)  # Readiness only; concurrent actions below use a barrier.
                running.terminate()
                running.wait(timeout=10)
                raise AssertionError('Synthetic API startup deadline')

            def call(actor, method, path, status, body=None, headers=None, code=None):
                nonlocal checks
                request_headers = {'X-RecipeAtlas': '1', **(headers or {})}
                data = None
                if body is not None:
                    data = json.dumps(body).encode()
                    request_headers['Content-Type'] = 'application/json'
                request = urllib.request.Request(base + path, data=data, method=method, headers=request_headers)
                try:
                    response = actor.open(request, timeout=30)
                except urllib.error.HTTPError as error:
                    response = error
                with response:
                    raw = response.read()
                    # Do not include response bodies: some setup responses contain invitation secrets.
                    assert response.status == status, (method, path, 'actual', response.status, 'expected', status)
                    assert 'no-store' in response.headers.get('Cache-Control', '')
                    result = json.loads(raw) if raw and 'json' in response.headers.get('Content-Type', '') else None
                if code:
                    assert result.get('code') == code, (path, result.get('code'), code)
                    assert not {'version', 'current', 'items', 'members', 'list'}.intersection(result)
                checks += 1
                return result

            def register(admin, name):
                invitation = call(admin, 'POST', '/api/admin/invitations', 200)
                token = urllib.parse.parse_qs(urllib.parse.urlsplit(invitation['link']).fragment)['token'][0]
                call(public, 'POST', '/api/invitations/redeem', 204, {
                    'token': token, 'username': name, 'displayName': name, 'email': name + '@example.test', 'password': password})
                actor = client()
                call(actor, 'POST', '/api/auth/login', 204, {'username': name, 'password': password})
                return actor, call(actor, 'GET', '/api/auth/me', 200)

            def deletion_headers(version, operation=None):
                return {'If-Match': '"' + version + '"', 'X-Operation-Id': operation or uid()}

            try:
                process = start()
                public, admin = client(), client()
                call(admin, 'POST', '/api/auth/login', 204, {'username': 'administrator', 'password': password})
                negotiation = call(admin, 'POST', '/api/shopping-events/negotiate?negotiateVersion=1', 200)
                assert any(x['transport'] == 'LongPolling' for x in negotiation['availableTransports'])
                owner, owner_user = register(admin, 'shopowner')
                member, member_user = register(admin, 'shopmember')
                other, _ = register(admin, 'shopother')
                owner2 = client()
                call(owner2, 'POST', '/api/auth/login', 204, {'username': 'shopowner', 'password': password})

                list_id = uid()
                path = '/api/shopping-lists/' + list_id
                creation = {'listId': list_id, 'name': 'Household', 'operationId': uid()}
                call(owner, 'POST', '/api/shopping-lists', 200, creation)
                call(owner, 'POST', '/api/shopping-lists', 200, creation)
                state = call(owner, 'GET', path, 200)
                assert state['name'] == 'Household' and state['role'] == 'Owner'
                assert len(call(owner, 'GET', '/api/shopping-lists', 200)) == 1
                call(admin, 'GET', path, 404)
                call(member, 'GET', path, 404)
                call(public, 'GET', path, 401)
                call(owner, 'POST', '/api/shopping-lists', 409, {**creation, 'name': 'Different'}, code='operation_id_reused')
                call(owner, 'POST', path + '/invitations', 200,
                     {'username': 'shopmember', 'expectedVersion': state['version'], 'operationId': uid()})
                pending = call(member, 'GET', '/api/shopping-list-invitations', 200)[0]
                call(member, 'GET', path, 404)
                call(member, 'POST', path + '/invitations/' + pending['id'] + '/respond', 400,
                     {'expectedVersion': pending['listVersion'], 'operationId': uid()})
                assert call(member, 'GET', '/api/shopping-list-invitations', 200)[0] == pending
                call(member, 'POST', path + '/invitations/' + pending['id'] + '/respond', 200,
                     {'accept': True, 'expectedVersion': pending['listVersion'], 'operationId': uid()})
                state = call(member, 'GET', path, 200)
                assert state['role'] == 'Member'
                administrative_version = state['version']
                call(member, 'PUT', path, 403, {'name': 'Forbidden', 'expectedVersion': state['version'], 'operationId': uid()})

                # Concurrent different additions and simultaneous retries of one addition.
                gate = threading.Barrier(2)
                add = {'id': uid(), 'text': '2 cartons of milk', 'operationId': uid()}
                def race_add(actor, body):
                    gate.wait(timeout=10)
                    return call(actor, 'POST', path + '/items', 200, body)
                with ThreadPoolExecutor(max_workers=2) as pool:
                    a = pool.submit(race_add, owner, add)
                    b = pool.submit(race_add, owner2, add)
                    first, repeated = a.result(), b.result()
                assert first == repeated
                gate = threading.Barrier(2)
                with ThreadPoolExecutor(max_workers=2) as pool:
                    a = pool.submit(race_add, owner, {'id': uid(), 'text': 'Bread', 'operationId': uid()})
                    b = pool.submit(race_add, member, {'id': uid(), 'text': 'Apples', 'operationId': uid()})
                    a.result(), b.result()
                state = call(owner, 'GET', path, 200)
                assert len(state['items']) == 3
                item = next(x for x in state['items'] if x['id'] == add['id'])
                item_path = path + '/items/' + item['id']
                token = uid()
                call(owner, 'POST', item_path + '/reservation', 200, {'token': token, 'expectedVersion': item['version']})
                call(member, 'POST', item_path + '/reservation', 409, {'token': uid(), 'expectedVersion': item['version']}, code='shopping_item_reserved')
                call(member, 'PUT', item_path + '/purchased', 409,
                     {'purchased': True, 'expectedVersion': item['version'], 'operationId': uid()}, code='shopping_item_reserved')
                call(member, 'DELETE', item_path, 409, headers=deletion_headers(item['version']), code='shopping_item_reserved')
                save = {'text': 'Oat milk', 'expectedVersion': item['version'], 'token': token, 'operationId': uid()}
                saved = call(owner, 'PUT', item_path, 200, save)
                call(owner, 'PUT', item_path, 200, save)  # Response loss retry after reservation release.
                state = call(owner, 'GET', path, 200)
                assert state['version'] == administrative_version, 'Item writes changed the administrative version'
                updated = next(x for x in state['items'] if x['id'] == item['id'])
                assert updated['text'] == 'Oat milk' and updated['version'] != item['version']
                call(owner, 'PUT', item_path + '/purchased', 428, {'purchased': True, 'operationId': uid()})
                call(owner, 'DELETE', item_path, 400, headers={'If-Match': '*', 'X-Operation-Id': uid()})
                call(owner, 'PUT', item_path + '/purchased', 409,
                     {'purchased': True, 'expectedVersion': item['version'], 'operationId': uid()}, code='concurrency_conflict')
                check_command = {'purchased': True, 'expectedVersion': updated['version'], 'operationId': uid()}
                checked = call(member, 'PUT', item_path + '/purchased', 200, check_command)['item']
                call(owner, 'PUT', item_path + '/purchased', 400,
                     {'expectedVersion': checked['version'], 'operationId': uid()})
                unchanged = call(owner, 'GET', path, 200)
                assert next(x for x in unchanged['items'] if x['id'] == item['id']) == checked
                call(owner, 'PUT', item_path + '/purchased', 200,
                     {'purchased': False, 'expectedVersion': checked['version'], 'operationId': uid()})
                call(member, 'PUT', item_path + '/purchased', 200, check_command)
                state = call(owner, 'GET', path, 200)
                current = next(x for x in state['items'] if x['id'] == item['id'])
                assert not current['purchased'], 'Historical checkbox receipt reapplied its effect'

                delete_id = uid()
                headers = deletion_headers(current['version'], delete_id)
                removed = call(owner, 'DELETE', item_path, 200, headers=headers)
                assert removed['undoUntilUtc']
                call(member, 'POST', item_path + '/restore', 403, {'deletionOperationId': delete_id, 'operationId': uid()})
                restored = call(owner, 'POST', item_path + '/restore', 200,
                                {'deletionOperationId': delete_id, 'operationId': uid()})
                call(owner, 'DELETE', item_path, 200, headers=headers)
                state = call(owner, 'GET', path, 200)
                assert any(x['id'] == item['id'] for x in state['items']), 'Retried delete removed restored item'

                # Restart after a committed action: SQLite receipt and session keys survive.
                process.terminate()
                process.wait(timeout=10)
                process = start()
                call(owner, 'POST', path + '/items', 200, add)
                assert len(call(owner, 'GET', path, 200)['items']) == 3
                other_list = uid()
                call(other, 'POST', '/api/shopping-lists', 200, {'listId': other_list, 'name': 'Other', 'operationId': uid()})
                call(other, 'PUT', '/api/shopping-lists/' + other_list + '/items/' + item['id'] + '/purchased', 404,
                     {'purchased': True, 'expectedVersion': restored['item']['version'], 'operationId': uid()})

                state = call(owner, 'GET', path, 200)
                call(owner, 'POST', path + '/transfer', 200,
                     {'newOwnerUserId': member_user['id'], 'expectedVersion': state['version'], 'operationId': uid()})
                state = call(member, 'GET', path, 200)
                assert state['role'] == 'Owner' and sum(x['role'] == 'Owner' for x in state['members']) == 1
                call(owner, 'DELETE', path, 403, headers=deletion_headers(state['version']))
                call(member, 'DELETE', path + '/members/' + owner_user['id'], 200, headers=deletion_headers(state['version']))
                call(owner, 'POST', path + '/items', 404, add)
                call(owner, 'GET', path, 404)

                # Whole-list owner deletion explicitly overrides an active item reservation.
                state = call(member, 'GET', path, 200)
                item = state['items'][0]
                call(member, 'POST', path + '/items/' + item['id'] + '/reservation', 200,
                     {'token': uid(), 'expectedVersion': item['version']})
                call(member, 'DELETE', path, 204, headers=deletion_headers(state['version']))
                call(member, 'GET', path, 404)
                call(owner, 'POST', '/api/shopping-lists', 404, creation)
                print(f'Passed {checks} shopping HTTP checks: SQLite, invitations, reservations, Undo, retries, restart and permissions.')
            finally:
                if process is not None and process.poll() is None:
                    process.terminate()
                    process.wait(timeout=10)


if __name__ == '__main__':
    main()
