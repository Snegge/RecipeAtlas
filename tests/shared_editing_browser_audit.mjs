// Run the existing browser assertions with optional long-list mobile audit checks.
// Usage: node tests/shared_editing_browser_audit.mjs FRONTEND BACKEND CHROME
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawn } from 'node:child_process';

const [frontendArg, backendArg, chromeArg] = process.argv.slice(2);
assert(frontendArg && backendArg && chromeArg, 'Pass frontend, backend and Chrome paths.');
const frontend = resolve(frontendArg);
let source = await readFile(join(frontend, 'tests/browser-accounts-smoke.mjs'), 'utf8');
const substitutions = [
  ["const frontend = resolve(import.meta.dirname, '..');", `const frontend = ${JSON.stringify(frontend)};`],
];
for (const [before, after] of substitutions) {
  assert.equal(source.split(before).length, 2, 'Runner changed; review the audit adaptation.');
  source = source.replace(before, after);
}
if (process.env.RECIPOKE_AUDIT_MOBILE) {
  assert.equal(source.split('height: 850,').length, 3);
  source = source.replaceAll('height: 850,', 'height: 844,');
  const fixture = "  const payload = JSON.parse(await readFile(join(backend, 'sample-recipe.json'), 'utf8'));";
  assert.equal(source.split(fixture).length, 2);
  source = source.replace(fixture, fixture + `
  payload.ingredients = Array.from({length: 50}, (_, i) => ({
    name: 'Long audit ingredient ' + i, quantity: '1', unit: 'g', note: null
  }));
  payload.steps = Array.from({length: 50}, (_, i) => 'Long audit step ' + i + ': stir carefully.');`);
  const screenshot = "  await recipient.screenshot('mobile-conflict-review');";
  assert.equal(source.split(screenshot).length, 2);
  source = source.replace(screenshot, screenshot + `
  await recipient.resize(390);
  await recipient.wait('innerWidth === 390 && innerHeight === 844');
  check(await recipient.evaluate('document.documentElement.scrollWidth <= innerWidth'), '390x844 long review has no page overflow');
  check(await recipient.evaluate('getComputedStyle(document.querySelector(".comparison")).gridTemplateColumns.split(" ").length === 1'), '390x844 comparison stacks');
  check(await recipient.evaluate('document.querySelector("mat-dialog-content").textContent.includes("Long audit ingredient 49") && document.querySelector("mat-dialog-content").textContent.includes("Long audit step 49")'), 'Long ingredient and step lists remain in comparison');
  check(await recipient.evaluate('[...document.querySelectorAll("mat-dialog-actions button")].every(b => {const r=b.getBoundingClientRect(); return r.top >= 0 && r.bottom <= innerHeight && r.left >= 0 && r.right <= innerWidth;})'), '390x844 review actions are reachable');
  let reachedAction = false;
  for (let i = 0; i < 45; i++) {
    await recipient.key('Tab', 'Tab', 9);
    check(await recipient.evaluate('!!document.activeElement.closest("mat-dialog-container")'), 'Tab stays inside the review dialog');
    if (await recipient.evaluate('!!document.activeElement.closest("mat-dialog-actions")')) { reachedAction = true; break; }
  }
  check(reachedAction, 'Keyboard reaches review actions with long lists');
  await recipient.screenshot('mobile-long-conflict-review-390');
  await recipient.resize(320);`);
}
const directory = await mkdtemp(join(tmpdir(), 'recipoke-audit-runner-'));
try {
  const runner = join(directory, 'browser.mjs');
  await writeFile(runner, source);
  const child = spawn(process.execPath, [runner, resolve(backendArg), resolve(chromeArg)], { stdio: 'inherit' });
  process.exitCode = await new Promise((resolveExit, reject) => {
    child.once('error', reject);
    child.once('exit', (code) => resolveExit(code ?? 1));
  });
} finally {
  await rm(directory, { recursive: true });
}
