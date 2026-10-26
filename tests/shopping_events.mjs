import { createRequire } from 'node:module';
import { resolve, join } from 'node:path';
import { randomUUID } from 'node:crypto';
import assert from 'node:assert/strict';
import { fixture } from './shopping_fixture.mjs';

const frontend = resolve(process.argv[2] ?? '../RecipeAtlasFrontend');
const { HubConnectionBuilder, HttpTransportType, LogLevel } = createRequire(join(frontend, 'package.json'))('@microsoft/signalr');
const test = await fixture();
const connections = [];
let checks = 0;
function event(connection, name, expected) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => { connection.off(name, receive); reject(new Error(`Missing ${name} notification`)); }, 8000);
    function receive(id) {
      if (id !== expected) return;
      clearTimeout(timeout); connection.off(name, receive); resolve();
    }
    connection.on(name, receive);
  });
}
async function connect(actor) {
  const connection = new HubConnectionBuilder().withUrl(test.origin + '/api/shopping-events', {
    transport: HttpTransportType.LongPolling, headers: { Cookie: actor.cookie, 'X-RecipeAtlas': '1' },
  }).configureLogging(LogLevel.None).build();
  connections.push(connection);
  await connection.start();
  return connection;
}
try {
  const owner = await test.register('eventowner');
  const member = await test.register('eventmember');
  const outsider = await test.register('eventoutsider');
  const path = await test.createList(owner.actor);
  const listId = path.split('/').at(-1);
  await test.share(owner.actor, member.actor, member.username, path);
  const ownerEvents = await connect(owner.actor), memberEvents = await connect(member.actor), outsiderEvents = await connect(outsider.actor);
  await ownerEvents.invoke('Watch', listId);
  await memberEvents.invoke('Watch', listId);
  await assert.rejects(outsiderEvents.invoke('Watch', listId)); checks++;
  const changed = event(memberEvents, 'Changed', listId);
  await owner.actor.call('POST', path + '/items', { id: randomUUID(), text: 'Milk', operationId: randomUUID() });
  await changed; checks++;
  const state = await owner.actor.call('GET', path);
  const lost = event(memberEvents, 'AccessLost', listId);
  await owner.actor.call('DELETE', path + '/members/' + member.user.id, undefined, 200,
    { 'If-Match': `"${state.version}"`, 'X-Operation-Id': randomUUID() });
  await lost; checks++;
  await assert.rejects(memberEvents.invoke('Watch', listId)); checks++;
  let forbiddenMessages = 0;
  memberEvents.on('Changed', () => forbiddenMessages++);
  const nextChanged = event(ownerEvents, 'Changed', listId);
  await owner.actor.call('POST', path + '/items', { id: randomUUID(), text: 'Bread', operationId: randomUUID() });
  await nextChanged;
  assert.equal(forbiddenMessages, 0); checks++;

  // A cookie-authenticated live connection must not retain authority after stamp changes.
  await test.share(owner.actor, member.actor, member.username, path);
  await memberEvents.invoke('Watch', listId);
  await test.admin.call('PUT', '/api/admin/accounts/' + member.user.id + '/status', { isActive: false }, 204);
  const afterDeactivation = event(ownerEvents, 'Changed', listId);
  await owner.actor.call('POST', path + '/items', { id: randomUUID(), text: 'Apples', operationId: randomUUID() });
  await afterDeactivation;
  assert.equal(forbiddenMessages, 0); checks++;
  await assert.rejects(memberEvents.invoke('Watch', listId)); checks++;

  // Unwatch and reconnect require explicit fresh authorization, no persisted group grants.
  await ownerEvents.stop();
  await ownerEvents.start();
  await ownerEvents.invoke('Watch', listId);
  const refreshed = event(ownerEvents, 'Changed', listId);
  await owner.actor.call('POST', path + '/items', { id: randomUUID(), text: 'Eggs', operationId: randomUUID() });
  await refreshed; checks++;
  console.log(`Passed ${checks} live SignalR checks and ${test.checks} isolated HTTP checks; removal, deactivation and reconnect enforced.`);
} finally {
  await Promise.allSettled(connections.map((connection) => connection.stop()));
  await test.close();
}
