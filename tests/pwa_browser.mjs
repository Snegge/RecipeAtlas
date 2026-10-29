// Production service-worker smoke; disposable backend, no external providers.
import { readFile, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { join, resolve, extname } from 'node:path';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
import { fixture, port, delay } from './shopping_fixture.mjs';
const frontend = resolve(process.argv[2]), chromium = process.argv[3];
const test = await fixture(), sockets = [];
let buildB = false, mutationRequests = 0, networkUnavailable = false;
const base = join(frontend, 'dist/recipoke');
const server = createServer(async (req,res) => {
 try {
  if (networkUnavailable) { req.socket.destroy(); return; }
  const path = new URL(req.url, 'http://localhost').pathname;
  if (path === '/api' || path.startsWith('/api/') || path === '/health') {
   if (!['GET','HEAD'].includes(req.method)) mutationRequests++;
   const body = []; for await (const chunk of req) body.push(chunk);
   const response = await fetch(test.origin + req.url, {method:req.method, headers:req.headers,
    ...(!['GET','HEAD'].includes(req.method) ? {body:Buffer.concat(body)} : {})});
   res.writeHead(response.status, Object.fromEntries(response.headers));
   res.end(Buffer.from(await response.arrayBuffer())); return;
  }
  let file = path === '/' || !extname(path) ? '/index.html' : path;
  if (file.includes('..')) { res.writeHead(400);res.end();return; }
  let bytes = await readFile(join(base,file));
  if (buildB && file === '/ngsw.json') {
   const manifest = JSON.parse(bytes); manifest.appData = {testVersion:'B'};
   const index = (await readFile(join(base,'index.html'),'utf8')).replace('</head>', '<meta name="pwa-test-version" content="B"></head>');
   manifest.hashTable['/index.html'] = createHash('sha1').update(index).digest('hex');
   bytes = Buffer.from(JSON.stringify(manifest));
  }
  if (buildB && file === '/index.html') bytes = Buffer.from(bytes.toString().replace('</head>', '<meta name="pwa-test-version" content="B"></head>'));
  res.setHeader('Cache-Control','no-cache');
  res.setHeader('Content-Type', {'.html':'text/html','.js':'text/javascript','.css':'text/css','.json':'application/json','.webmanifest':'application/manifest+json','.png':'image/png','.svg':'image/svg+xml'}[extname(file)] || 'application/octet-stream');
  res.end(bytes);
 } catch { res.writeHead(404); res.end(); }
});
await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
try {
 const origin = `http://127.0.0.1:${server.address().port}`, debugPort = await port();
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

 const {targetId} = await cdp('Target.createTarget',{url:'about:blank'});
 const {sessionId} = await cdp('Target.attachToTarget',{targetId,flatten:true});
 const send = (method,params={}) => cdp(method,params,sessionId);
 await send('Page.enable'); await send('Runtime.enable'); await send('Network.enable');
 const evaluate = async expression => {
  const r = await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});
  assert(!r.exceptionDetails, JSON.stringify(r.exceptionDetails)); return r.result.value;
 };
 const wait = async expression => {
  for(let i=0;i<600;i++){if(await evaluate(expression))return;await delay(100);}
  throw Error('Timeout: '+expression);
 };
 await send('Page.navigate',{url:origin+'/login'});
 await wait('!!document.querySelector("app-root button")');
 await wait('!!navigator.serviceWorker.controller');
 assert.equal(await evaluate('fetch("/manifest.webmanifest").then(r=>r.json()).then(m=>m.name)'), 'Recipoke');
 assert.equal(await evaluate('fetch("/api/not-a-route").then(r=>r.status)'),404);

 for (const [name,size] of [['icon-192',192],['icon-512',512],['maskable-512',512],['apple-touch-icon',180]]) {
  assert.equal(await evaluate(`new Promise((resolve,reject)=>{const i=new Image();i.onload=()=>resolve(i.naturalWidth===${size}&&i.naturalHeight===${size});i.onerror=reject;i.src='/icons/${name}.png';})`),true);
 }
 assert.equal(await evaluate(`fetch('/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json','X-RecipeAtlas':'1'},body:JSON.stringify(${JSON.stringify({username:'administrator',password:test.password})})}).then(r=>r.status)`),204);
 assert.equal(await evaluate(`fetch('/api/auth/me').then(r=>r.status)`),200);
 await send('Page.navigate',{url:origin+'/settings'});
 await wait('!!document.querySelector("app-settings button")');
 assert.equal(await evaluate(`!!document.querySelector('.pwa-status button')`),false);
 await evaluate(`document.querySelector('app-settings button').click()`);
 await wait(`document.querySelector('app-settings').textContent.includes('On iPhone or iPad')`);
 for (const width of [1280,390,320]) {
  await send('Emulation.setDeviceMetricsOverride',{width,height:844,deviceScaleFactor:1,mobile:width<600});
  assert.equal(await evaluate('document.documentElement.scrollWidth <= innerWidth'),true);
  if (process.env.RECIPOKE_SCREENSHOTS) {
   await evaluate(`new Promise(resolve=>{window.scrollTo(0,0);document.querySelector('mat-sidenav-content').scrollTop=0;requestAnimationFrame(()=>requestAnimationFrame(resolve));})`);
   const screenshot = await send('Page.captureScreenshot', {format:'png'});
   await writeFile(join(process.env.RECIPOKE_SCREENSHOTS, `settings-${width}.png`), Buffer.from(screenshot.data,'base64'));
  }
 }
 const installability = await send('Page.getInstallabilityErrors');
 assert.deepEqual(installability.installabilityErrors, []);
 await evaluate(`(()=>{const e=new Event('beforeinstallprompt',{cancelable:true});e.prompt=async()=>{window.installPromptUsed=true};window.dispatchEvent(e);Array.from(document.querySelectorAll('button')).find(b=>b.textContent.includes('Install Recipoke')).click();})()`);
 await wait('window.installPromptUsed === true');
 await evaluate(`window.dispatchEvent(new Event('appinstalled'))`);
 await wait(`document.querySelector('app-settings').textContent.includes("You're using the installed app.")`);
 assert.equal(await evaluate(`!!document.querySelector('app-settings button')`),false);

 const writesBeforeOffline = mutationRequests;
 await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
 networkUnavailable = true;
 await send('Network.emulateNetworkConditions',{offline:true,latency:0,downloadThroughput:0,uploadThroughput:0});
 await send('Page.navigate',{url:origin+'/cookbooks'});
 await wait('document.body.textContent.includes("Connection required")');
 for(const path of ['/api/auth/me','/api/cookbooks/1/image','/api/shopping-events/negotiate']) {
  assert.equal(await evaluate(`fetch(${JSON.stringify(path)}).then(r=>!r.ok && !r.headers.get('content-type')?.includes('text/html')).catch(()=>true)`),true);
 }
 assert.equal(await evaluate(`fetch('/api/auth/logout',{method:'POST',headers:{'X-RecipeAtlas':'1'}}).then(r=>!r.ok).catch(()=>true)`),true);
 assert.equal(mutationRequests,writesBeforeOffline);
 const keys = await evaluate('caches.keys().then(async keys=>(await Promise.all(keys.map(k=>caches.open(k).then(c=>c.keys()).then(rs=>rs.map(r=>r.url))))).flat())');
 assert(!keys.some(k=>new URL(k).pathname.startsWith('/api/')));
 assert(!keys.some(k=>new URL(k).search));
 networkUnavailable = false;
 await send('Network.emulateNetworkConditions',{offline:false,latency:0,downloadThroughput:-1,uploadThroughput:-1});
 await send('Page.navigate',{url:origin+'/login'});
 await wait('!!document.querySelector("app-root button")');
 assert.equal(mutationRequests,writesBeforeOffline, 'offline mutation was not replayed');
 buildB = true;
 await evaluate(`navigator.serviceWorker.controller.postMessage({action:'CHECK_FOR_UPDATES',nonce:12345})`);
 await wait('document.body.textContent.includes("A new version is ready")');
 assert.equal(await evaluate('location.pathname'),'/login');
 await evaluate(`Array.from(document.querySelectorAll('button')).find(b=>b.textContent.trim()==='Reload').click()`);
 await wait('!!document.querySelector("app-root button") && !document.body.textContent.includes("A new version is ready")');
 assert.equal(await evaluate(`document.querySelector("[name=pwa-test-version]")?.content`), 'B');
 console.log('PASS: production Chromium desktop/mobile shell, manifest, offline navigation, network-only API/photo/hub, cache privacy, A/B update notification and explicit reload');
} finally { for(const s of sockets)s.close(); await new Promise(r=>server.close(r)); await test.close(); }
