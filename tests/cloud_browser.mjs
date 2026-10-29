// Real production worker in a fresh Chromium profile; no user credentials or browser storage.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [origin, chromium] = process.argv.slice(2);
assert(origin && chromium, 'Usage: node tests/cloud_browser.mjs ORIGIN CHROMIUM');
const directory = await mkdtemp(join(tmpdir(), 'recipoke-cloud-browser-'));
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const browser = spawn(chromium, ['--headless', '--no-sandbox', '--disable-gpu', '--no-first-run',
  '--remote-debugging-port=0', `--user-data-dir=${directory}`, 'about:blank'], { stdio: 'ignore', windowsHide: true });
const exited = new Promise(resolve => browser.once('exit', resolve));
let socket;
try {
  let port;
  for (let i = 0; i < 100; i++) {
    try { port = (await readFile(join(directory, 'DevToolsActivePort'), 'utf8')).split('\n')[0]; break; }
    catch { await delay(100); }
  }
  assert(port, 'Chromium debugging endpoint did not start');
  const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
  socket = new WebSocket(version.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let sequence = 0;
  const pending = new Map();
  socket.onmessage = ({ data }) => {
    const response = JSON.parse(data), call = pending.get(response.id);
    if (!call) return;
    pending.delete(response.id); clearTimeout(call.timer);
    response.error ? call.reject(new Error(response.error.message)) : call.resolve(response.result);
  };
  const cdp = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 20000);
    pending.set(id, { resolve, reject, timer });
    socket.send(JSON.stringify({ id, method, params, sessionId }));
  });
  for (const width of [1280, 390]) {
    const { browserContextId } = await cdp('Target.createBrowserContext');
    const { targetId } = await cdp('Target.createTarget', { url: 'about:blank', browserContextId });
    const { sessionId } = await cdp('Target.attachToTarget', { targetId, flatten: true });
    const send = (method, params) => cdp(method, params, sessionId);
    const evaluate = async expression => {
      const response = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      assert(!response.exceptionDetails, 'Browser expression failed: ' + expression);
      return response.result.value;
    };
    const wait = async expression => {
      for (let i = 0; i < 400; i++) { if (await evaluate(expression)) return; await delay(100); }
      throw new Error('Browser condition failed: ' + expression + ' state=' + JSON.stringify(await evaluate('({url:location.href,online:navigator.onLine,text:document.body.innerText.slice(0,800)})')));
    };
    await send('Page.enable'); await send('Network.enable');
    await send('Emulation.setDeviceMetricsOverride', { width, height: 844, deviceScaleFactor: 1, mobile: width < 500 });
    await send('Page.navigate', { url: origin + '/login' });
    await wait('!!navigator.serviceWorker?.controller && !!document.querySelector("input")');
    assert.equal(await evaluate('fetch("/api/auth/me").then(r=>r.status)'), 401);
    const cached = await evaluate('(async()=>{const result=[];for(const key of await caches.keys())for(const request of await (await caches.open(key)).keys())result.push(request.url);return result;})()');
    assert(cached.some(url => url.endsWith('/index.html')), 'Shell actually cached');
    assert(!cached.some(url => new URL(url).pathname.startsWith('/api')), 'No private API cache entries');
    await send('Network.emulateNetworkConditions', { offline: true, latency: 0, downloadThroughput: 0, uploadThroughput: 0 });
    await send('Page.navigate', { url: origin + '/settings' });
    await wait('!!document.querySelector("app-root input") && /connection required/i.test(document.body.innerText)');
    for (const path of ['/api/auth/me', '/api/photos/missing', '/api/shopping-events/negotiate']) {
      const result = await evaluate(`fetch(${JSON.stringify(path)}).then(async r=>({status:r.status,html:(await r.text()).includes('<app-root>')})).catch(()=>({status:0,html:false}))`);
      assert(!result.html && result.status !== 200, 'Offline API must not receive shell HTML');
    }
    const write = await evaluate('fetch("/api/shopping-lists",{method:"POST",headers:{"X-RecipeAtlas":"1","Content-Type":"application/json"},body:"{}"}).then(r=>r.status).catch(()=>0)');
    assert(write === 0 || write >= 400, 'No successful offline mutation');
    await cdp('Target.disposeBrowserContext', { browserContextId });
  }
  console.log('Passed production Chromium desktop/mobile shell, worker caching, offline navigation and private-route checks.');
} finally {
  socket?.close(); browser.kill();
  await exited;
  await rm(directory, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
}
