// Exercise a failed browser launch through the real runner, including resource cleanup.
// Usage: node tests/shared_editing_browser_startup_test.mjs FRONTEND BACKEND
import assert from 'node:assert/strict';
import { mkdtemp, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawn } from 'node:child_process';

const [frontend, backend] = process.argv.slice(2);
assert(frontend && backend, 'Pass frontend and backend roots.');
const directory = await mkdtemp(join(tmpdir(), 'recipoke-launch-test-'));
try {
  const child = spawn(process.execPath, [
    resolve(frontend, 'tests/browser-accounts-smoke.mjs'),
    resolve(backend),
    join(directory, 'missing-browser.exe'),
  ], { env: { ...process.env, TEMP: directory, TMP: directory, TMPDIR: directory } });
  let output = '';
  child.stdout.on('data', (chunk) => { output += chunk; });
  child.stderr.on('data', (chunk) => { output += chunk; });
  const code = await new Promise((resolveExit, reject) => {
    child.once('error', reject);
    child.once('exit', resolveExit);
  });
  assert.notEqual(code, 0, 'A missing browser must fail the runner.');
  assert.match(output, /ENOENT/, 'The failed launch must remain observable.');
  assert.doesNotMatch(output, /Unhandled 'error' event/, 'Launch failure must reach cleanup.');
  const remaining = (await readdir(directory)).filter((name) => name.startsWith('recipoke-browser-'));
  assert.deepEqual(remaining, [], 'Runner must remove its temporary database/profile/logs.');
  console.log('Passed missing-browser launch and temporary-resource cleanup regression.');
} finally {
  await rm(directory, { recursive: true, force: true });
}
