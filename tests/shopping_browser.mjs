// Two isolated Chromium contexts; only synthetic accounts and disposable SQLite.
import { writeFile, mkdir } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import assert from 'node:assert/strict';
import { fixture, port, delay } from './shopping_fixture.mjs';

assert(process.argv[2] && process.argv[3], 'Usage: node tests/shopping_browser.mjs FRONTEND CHROMIUM');
const frontend = resolve(process.argv[2]), chromium = process.argv[3];
const test = await fixture();
const sockets = [];
let checks = 0;
const check = (value, label) => { assert(value, label); checks++; };
try {
  const origin = `http://127.0.0.1:${await port()}`, debugPort = await port();
  const proxy = join(test.directory, 'proxy.json');
  await writeFile(proxy, JSON.stringify({ '/api/**': { target: test.origin, secure: false } }));
  const angular = await test.start(process.execPath, [join(frontend, 'node_modules/@angular/cli/bin/ng.js'),
    'serve', '--host', '127.0.0.1', '--port', new URL(origin).port, '--proxy-config', proxy], { cwd: frontend });
  await test.ready(origin, angular);
  const chrome = await test.start(chromium, ['--headless', '--no-sandbox', '--disable-gpu', '--no-first-run',
    '--no-default-browser-check', `--remote-debugging-port=${debugPort}`, `--user-data-dir=${join(test.directory, 'chrome')}`, 'about:blank']);
  const version = await test.ready(`http://127.0.0.1:${debugPort}/json/version`, chrome);
  const socket = new WebSocket(version.webSocketDebuggerUrl); sockets.push(socket);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let sequence = 0;
  const pending = new Map();
  socket.onmessage = ({ data }) => {
    const response = JSON.parse(data), call = pending.get(response.id);
    if (!call) return;
    pending.delete(response.id); clearTimeout(call.timeout);
    response.error ? call.reject(new Error('CDP request failed')) : call.resolve(response.result);
  };
  const cdp = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
    const id = ++sequence;
    const timeout = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout ${method}`)); }, 15000);
    pending.set(id, { resolve, reject, timeout });
    socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
  });
  async function page(account) {
    const { browserContextId } = await cdp('Target.createBrowserContext');
    const { targetId } = await cdp('Target.createTarget', { url: 'about:blank', browserContextId });
    const { sessionId } = await cdp('Target.attachToTarget', { targetId, flatten: true });
    const send = (method, params) => cdp(method, params, sessionId);
    await send('Page.enable'); await send('Runtime.enable');
    await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
    const evaluate = async expression => {
      const response = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      assert(!response.exceptionDetails, 'Browser expression failed (details omitted to protect synthetic credentials)');
      return response.result.value;
    };
    const wait = async expression => {
      for (let i = 0; i < 160; i++) { if (await evaluate(expression)) return; await delay(50); }
      throw new Error('Browser condition not satisfied: ' + expression.slice(0, 150));
    };
    const go = async path => { await send('Page.navigate', { url: origin + path }); await wait('!!document.querySelector("app-root")'); };
    const click = selector => evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});if(!e||e.disabled)throw Error('control unavailable');e.click();})()`);
    const fill = (selector, value) => evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.focus();e.value=${JSON.stringify(value)};e.dispatchEvent(new Event('input',{bubbles:true}));})()`);
    await go('/login');
    await evaluate(`fetch('/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json','X-RecipeAtlas':'1'},body:JSON.stringify(${JSON.stringify({username: account.username, password: test.password})})}).then(r=>{if(!r.ok)throw Error('login failed')})`);
    await go('/shopping-lists');
    await wait('!!document.querySelector("[data-testid=list-name]")');
    return { send, evaluate, wait, go, click, fill };
  }
  const owner = await test.register('browserowner'), member = await test.register('browsermember');
  const a = await page(owner), b = await page(member);
  await a.fill('[data-testid=list-name]', 'Our weekly groceries');
  await a.click('[data-testid=create-list]');
  await a.wait('!!document.querySelector("[data-testid=shopping-card]")');
  const listId = await a.evaluate('document.querySelector("[data-testid=shopping-card]").dataset.listId');
  const path = '/api/shopping-lists/' + listId;
  check(!!listId, 'Overview creates and previews named list');
  await a.click('[data-testid=shopping-card]');
  await a.wait('!!document.querySelector("[data-testid=add-item]")');
  await a.click('[data-testid=list-settings]');
  await a.fill('[data-testid=invite-username]', member.username);
  await a.click('[data-testid=invite-member]');
  await a.wait('document.body.textContent.includes("Pending invitations")');
  await b.go('/shopping-lists');
  await b.wait('!!document.querySelector("[data-testid=accept-invitation]")');
  await b.click('[data-testid=accept-invitation]');
  await b.wait('!!document.querySelector("[data-testid=shopping-card]")');
  await b.click('[data-testid=shopping-card]');
  await b.wait('!!document.querySelector("[data-testid=add-item]")');
  check(true, 'Owner invitation and member acceptance work through the UI');
  await a.click('[data-testid=list-settings]');

  for (const text of ['Milk', 'Bread']) {
    await a.fill('[data-testid=add-item]', text);
    await a.click('[data-testid=add-item-button]');
    await a.wait(`document.querySelector('[data-testid=add-item]').value === ''`);
  }
  await b.wait('document.querySelectorAll("[data-testid=shopping-item]").length===2');
  check(true, 'Second phone sees additions without a manual refresh');
  const state = await owner.actor.call('GET', path);
  const milk = state.items.find(item => item.text === 'Milk').id;
  const bread = state.items.find(item => item.text === 'Bread').id;
  const row = id => `[data-testid=shopping-item][data-item-id="${id}"]`;
  await a.click(row(milk) + ' [data-testid=edit-item]');
  await a.wait('!!document.querySelector("[data-testid=item-draft]:not([readonly])")');
  await b.wait(`document.querySelector(${JSON.stringify(row(milk) + ' input[type=checkbox]')}).disabled`);
  check(await b.evaluate(`document.querySelector(${JSON.stringify(row(milk) + ' [data-testid=remove-item]')}).disabled`), 'Remote edit prevents checkbox and deletion');
  await a.fill('[data-testid=item-draft]', 'Oat milk');
  await a.evaluate('document.querySelector("[data-testid=add-item]").focus()');
  await b.wait(`document.querySelector(${JSON.stringify(row(milk))}).textContent.includes('Oat milk')`);
  check(true, 'Blur autosave reaches the second phone');
  await b.wait(`!document.querySelector(${JSON.stringify(row(milk) + ' input[type=checkbox]')}).disabled`);
  await b.evaluate(`document.querySelector(${JSON.stringify(row(milk) + ' input[type=checkbox]')}).focus()`);
  await b.send('Input.dispatchKeyEvent', { type: 'keyDown', key: ' ', code: 'Space', windowsVirtualKeyCode: 32 });
  await b.send('Input.dispatchKeyEvent', { type: 'keyUp', key: ' ', code: 'Space', windowsVirtualKeyCode: 32 });
  await a.wait(`document.querySelector(${JSON.stringify(row(milk) + ' input[type=checkbox]')}).checked`);
  check(true, 'Purchased state is visible on both phones');
  check(await b.evaluate('document.activeElement === document.querySelector("[data-testid=add-item]")'),
    'Keyboard checkbox action leaves focus on enabled quick entry');
  await a.click(row(bread) + ' [data-testid=remove-item]');
  await a.wait('!!document.querySelector("[data-testid=undo-item]")');
  await b.wait(`!document.querySelector(${JSON.stringify(row(bread))})`);
  await a.click('[data-testid=undo-item]');
  await b.wait(`!!document.querySelector(${JSON.stringify(row(bread))})`);
  check(true, 'Removal and Undo restore the same item on both phones');
  check(await a.evaluate('document.documentElement.scrollWidth <= innerWidth'), '390px detail has no horizontal overflow');

  await a.evaluate('document.querySelector("[data-testid=add-item]").focus()');
  await a.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 });
  await a.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 });
  check(await a.evaluate('document.activeElement !== document.body'), 'Keyboard Tab reaches a control');

  const screenshots = join(test.root, 'docs/verification/screenshots');
  await mkdir(screenshots, { recursive: true });
  const image = await a.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  await writeFile(join(screenshots, 'shopping-list-mobile.png'), Buffer.from(image.data, 'base64'));

  // Owner deletion overrides a member's active edit, but the draft remains copyable.
  await b.click(row(bread) + ' [data-testid=edit-item]');
  await b.wait('!!document.querySelector("[data-testid=item-draft]:not([readonly])")');
  await b.fill('[data-testid=item-draft]', 'Bread for Saturday');
  await a.click('[data-testid=list-settings]');
  await a.click('[data-testid=delete-list]');
  await a.wait('!!document.querySelector("mat-dialog-container")');
  await a.wait('!!document.activeElement.closest("mat-dialog-container")');
  check(await a.evaluate('!!document.activeElement.closest("mat-dialog-container")'), 'Delete confirmation receives keyboard focus');
  await a.evaluate('[...document.querySelectorAll("mat-dialog-container button")].find(e=>e.textContent.includes("Cancel")).click()');
  await a.wait('!document.querySelector("mat-dialog-container")');
  check((await owner.actor.call('GET', path)).id === listId, 'Cancelling whole-list deletion keeps the list');
  await a.click('[data-testid=delete-list]');
  await a.wait('!!document.querySelector("mat-dialog-container")');
  await a.evaluate('[...document.querySelectorAll("mat-dialog-container button")].find(e=>e.textContent.includes("Delete list")).click()');
  await a.wait('!!document.querySelector("[data-testid=create-list]")');
  check(true, 'Owner deletion navigates successfully despite its own access-lost notification');
  await b.wait('document.body.textContent.includes("unavailable")');
  check(await b.evaluate('[...document.querySelectorAll("input,textarea")].some(e=>e.value.includes("Bread for Saturday"))'),
    'Whole-list deletion preserves unsaved draft text');
  console.log(`Passed ${checks} real Chromium shopping checks with two isolated contexts at 390×844.`);
} finally {
  for (const socket of sockets) socket.close();
  await test.close();
}
