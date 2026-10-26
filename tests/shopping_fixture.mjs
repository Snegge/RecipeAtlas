// Shared disposable API fixture for shopping notification and browser checks.
import { spawn, spawnSync } from 'node:child_process';
import { mkdtemp, open, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve, join } from 'node:path';
import { createServer } from 'node:net';
import { randomBytes, randomUUID } from 'node:crypto';
import assert from 'node:assert/strict';

export const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
export async function port() {
  const server = createServer();
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const value = server.address().port;
  await new Promise((resolve) => server.close(resolve));
  return value;
}

export async function fixture() {
  const root = resolve(import.meta.dirname, '..');
  const directory = await mkdtemp(join(tmpdir(), 'recipoke-shopping-'));
  const origin = `http://127.0.0.1:${await port()}`;
  const password = randomBytes(24).toString('base64url');
  const environment = { ...process.env, DataDirectory: join(directory, 'data'),
    ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: origin,
    Accounts__PublicAppUrl: 'http://localhost:4200', RecipeText__ApiKey: '' };
  const dll = join(root, 'backend/RecipeAtlas.Api/bin/Debug/net10.0/Recipoke.Api.dll');
  const children = [], logs = [];
  async function close() {
    for (const child of children.toReversed()) {
      if (child.pid && child.exitCode === null && child.signalCode === null) {
        child.kill();
        await Promise.race([new Promise((resolve) => child.once('exit', resolve)), delay(5000)]);
        if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL');
      }
    }
    for (const log of logs) await log.close();
    await rm(directory, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
  }
  async function start(command, args, options = {}) {
    const log = await open(join(directory, `process-${children.length}.log`), 'w');
    logs.push(log);
    const child = spawn(command, args, { cwd: root, env: environment, stdio: ['ignore', log.fd, log.fd], ...options });
    children.push(child);
    await new Promise((resolve, reject) => { child.once('error', reject); child.once('spawn', resolve); });
    return child;
  }
  async function ready(url, child) {
    for (let i = 0; i < 300; i++) {
      assert.equal(child.exitCode, null, 'Test process exited during startup');
      try { const response = await fetch(url); if (response.ok) return await response.json().catch(() => null); } catch {}
      await delay(100);
    }
    throw new Error('Test process readiness timeout');
  }
  let checks = 0;
  function client() {
    let cookie = '';
    return {
      get cookie() { return cookie; },
      async call(method, path, body, expected = 200, headers = {}) {
        const response = await fetch(origin + path, {
          method, headers: { 'X-RecipeAtlas': '1', 'Content-Type': 'application/json', ...(cookie ? { Cookie: cookie } : {}), ...headers },
          ...(body === undefined ? {} : { body: JSON.stringify(body) }),
        });
        assert.equal(response.status, expected, `${method} ${path} status`);
        const setCookie = response.headers.getSetCookie();
        if (setCookie.length) cookie = setCookie.map((entry) => entry.split(';')[0]).join('; ');
        checks++;
        return response.status === 204 ? null : response.json();
      },
    };
  }
  try {
    const setup = spawnSync('dotnet', [dll, '--bootstrap-admin'], { cwd: root, env: environment,
      input: `administrator\nAdministrator\n${password}\n`, encoding: 'utf8' });
    assert.equal(setup.status, 0, 'Synthetic bootstrap failed');
    const api = await start('dotnet', [dll]);
    await ready(origin + '/health', api);
    const admin = client();
    await admin.call('POST', '/api/auth/login', { username: 'administrator', password }, 204);
    async function register(username) {
      const invite = await admin.call('POST', '/api/admin/invitations');
      const token = new URLSearchParams(new URL(invite.link).hash.slice(1)).get('token');
      const actor = client();
      await actor.call('POST', '/api/invitations/redeem', { token, username, displayName: username,
        email: username + '@example.test', password }, 204);
      await actor.call('POST', '/api/auth/login', { username, password }, 204);
      return { actor, user: await actor.call('GET', '/api/auth/me'), username };
    }
    async function createList(owner, name = 'Weekly shopping') {
      const listId = randomUUID();
      await owner.call('POST', '/api/shopping-lists', { listId, name, operationId: randomUUID() });
      return '/api/shopping-lists/' + listId;
    }
    async function share(owner, member, username, path) {
      const state = await owner.call('GET', path);
      await owner.call('POST', path + '/invitations', { username, expectedVersion: state.version, operationId: randomUUID() });
      const invitation = (await member.call('GET', '/api/shopping-list-invitations')).find((i) => path.endsWith(i.listId));
      await member.call('POST', path + '/invitations/' + invitation.id + '/respond',
        { accept: true, expectedVersion: invitation.listVersion, operationId: randomUUID() });
    }
    return { root, directory, origin, password, admin, client, register, createList, share, start, ready, close,
      get checks() { return checks; } };
  } catch (error) { await close(); throw error; }
}
