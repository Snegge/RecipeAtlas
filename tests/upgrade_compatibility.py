"""Optional real-binary upgrade check. Always uses temporary synthetic data and test credentials."""
import sys, os, secrets, subprocess, tempfile, time, urllib.request, urllib.error, http.cookiejar, json, socket, sqlite3, hashlib
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
if len(sys.argv) != 2:
 raise SystemExit('Usage: python3 tests/upgrade_compatibility.py /path/to/pre-refactor/Recipoke.Api.dll')
old=Path(sys.argv[1]).resolve()
new=ROOT/'backend/RecipeAtlas.Api/bin/Debug/net10.0/Recipoke.Api.dll'
dotnet=os.environ.get('RECIPOKE_DOTNET', 'dotnet')
password=secrets.token_urlsafe(32)
hash_value=subprocess.run([dotnet,str(old),'--hash-password'],input=password+'\n',text=True,capture_output=True,check=True).stdout.strip()
with socket.socket() as sock:
 sock.bind(('127.0.0.1',0)); port=sock.getsockname()[1]
base=f'http://127.0.0.1:{port}'
jar=http.cookiejar.CookieJar(); client=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
def request(method,path,expected,data=None):
 headers={'X-RecipeAtlas':'1'}
 if isinstance(data,dict): data=json.dumps(data).encode(); headers['Content-Type']='application/json'
 req=urllib.request.Request(base+path,data=data,method=method,headers=headers)
 try: response=client.open(req,timeout=10)
 except urllib.error.HTTPError as error: response=error
 body=response.read(); assert response.status==expected,(path,response.status,expected,body[:200])
 return json.loads(body) if 'json' in response.headers.get('Content-Type','') and body else body
with tempfile.TemporaryDirectory(prefix='recipoke-upgrade-') as temporary:
 data=Path(temporary)/'data'
 environment=dict(os.environ,ASPNETCORE_ENVIRONMENT='Development',ASPNETCORE_URLS=base,DataDirectory=str(data),Owner__PasswordHash=hash_value,RecipeText__ApiKey='')
 def start(dll):
  log=open(Path(temporary)/(dll.stem+'.log'),'w')
  process=subprocess.Popen([dotnet,str(dll)],cwd=ROOT/'backend/RecipeAtlas.Api',env=environment,stdout=log,stderr=subprocess.STDOUT)
  for _ in range(100):
   if process.poll() is not None: raise RuntimeError('API exited: '+dll.stem)
   try:
    with urllib.request.urlopen(base+'/health',timeout=1) as r:
     assert json.load(r)=={'status':'ok'}; return process,log
   except OSError: time.sleep(.1)
  process.terminate(); process.wait(timeout=10); log.close(); raise RuntimeError('API not healthy')
 def stop(process,log): process.terminate(); process.wait(timeout=10); log.close()
 p,log=start(old)
 try:
  request('POST','/api/auth/login',204,{'password':password})
  assert any(c.name=='RecipeAtlas.Session' and c.path=='/api' for c in jar)
  recipe_input=json.loads((ROOT/'sample-recipe.json').read_text())
  recipe=request('POST','/api/recipes',201,recipe_input)
  image=b'\x89PNG\r\n\x1a\n'+b'compatibility-image'
  request('PUT',f'/api/recipes/{recipe["id"]}/image',200,image)
  before=request('GET',f'/api/recipes/{recipe["id"]}',200)
 finally: stop(p,log)
 def history():
  with sqlite3.connect(data/'recipes.db') as db: return db.execute('SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId').fetchall()
 old_history=history()
 keys={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (data/'keys').iterdir()}
 p,log=start(new)
 try:
  assert request('GET','/api/auth/me',200)=={'name':'owner'}
  assert request('GET',f'/api/recipes/{recipe["id"]}',200)==before
  assert request('GET',f'/api/recipes/{recipe["id"]}/image',200)==image
  assert request('GET','/api/recipes',200)['total']==1
  request('PUT',f'/api/recipes/{recipe["id"]}',200,recipe_input)
 finally: stop(p,log)
 assert history()==old_history
 assert keys=={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (data/'keys').iterdir()}
 print('Cross-version upgrade passed: legacy owner hash, existing cookie, unchanged key ring, recipes/ingredients/steps, image bytes/routes, legacy write header and unchanged migration history.')
