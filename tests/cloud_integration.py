"""Disposable PostgreSQL + replacement containers. Never reads dotenv or contacts Neon."""
import urllib.parse
import argparse,concurrent.futures,json,os,secrets,subprocess,tempfile,time,uuid
from pathlib import Path
from urllib.request import Request,urlopen
from urllib.error import HTTPError
ROOT=Path(__file__).resolve().parents[1]

def run(*args,**kwargs):
    result = subprocess.run(list(args),capture_output=True,timeout=300,**kwargs)
    if result.returncode and args[0]=='node':
        print(result.stderr.decode(errors='replace')) # Browser/event harnesses never print credentials.
    result.check_returncode()
    return result.stdout

def main(image,frontend,chromium):
    suffix=secrets.token_hex(6); network='recipoke-test-'+suffix; database=network+'-db'; app=network+'-app'
    password=secrets.token_urlsafe(24)
    with tempfile.TemporaryDirectory(prefix='recipoke-cloud-test-') as directory:
        directory=Path(directory); environment=dict(os.environ,POSTGRES_PASSWORD=password)
        def docker(*args,**kwargs):return run('docker',*args,**kwargs)
        try:
            docker('network','create',network)
            docker('run','-d','--name',database,'--network',network,'--network-alias','postgres','-e','POSTGRES_PASSWORD','-e','POSTGRES_DB=recipoke','postgres:18',env=environment)
            for _ in range(120):
                if subprocess.run(['docker','exec',database,'pg_isready','-U','postgres'],capture_output=True).returncode==0:break
                time.sleep(1)
            else:raise AssertionError('PostgreSQL startup timed out')
            protection=directory/'protection.json'
            run('dotnet',str(ROOT/'backend/RecipeAtlas.Api/bin/Release/net10.0/Recipoke.Api.dll'),'--generate-protection-certificate',str(protection))
            settings=json.loads(protection.read_text())
            connection=f'Host=postgres;Database=recipoke;Username=postgres;Password={password};SSL Mode=Disable'
            settings.update(Database__Provider='PostgreSql',Database__Connection=connection,Database__MigrationConnection=connection,Accounts__PublicAppUrl='https://recipoke.example',RecipeText__ApiKey='')
            envfile=directory/'runtime.env';envfile.write_text('\n'.join(k+'='+v for k,v in settings.items()));envfile.chmod(0o600)
            for override, message in [
                ('Database__Provider=Unexpected','must be Sqlite or PostgreSql'),
                ('Database__Connection=Password=synthetic-never-log;UnknownSetting=x','Invalid Npgsql syntax'),
                ('Database__Connection=Host=remote.invalid;Database=test;SSL Mode=Require','requires SSL Mode=VerifyFull'),
                ('DataProtection__Certificate=synthetic-never-log','base64 PKCS#12'),
                ('Accounts__PublicAppUrl=https://example.test/untrusted-path','must be an HTTPS origin')]:
                failure=subprocess.run(['docker','run','--rm','--network',network,'--env-file',str(envfile),'-e',override,image],capture_output=True,timeout=30)
                assert failure.returncode!=0 and message.encode() in failure.stderr
                assert b'synthetic-never-log' not in failure.stdout+failure.stderr
            command=['run','--rm','--network',network,'--env-file',str(envfile),image]
            # Startup must never initialize an empty cloud database implicitly.
            missing = subprocess.run(['docker',*command],capture_output=True)
            assert missing.returncode != 0 and b'Schema upgrade required' in missing.stderr
            with concurrent.futures.ThreadPoolExecutor(2) as executor:
                list(executor.map(lambda _:docker(*command,'--migrate'),range(2)))
            docker(*command,'--migrate');docker(*command,'--migration-status')
            docker('run','--rm','-i','--network',network,'--env-file',str(envfile),image,'--bootstrap-admin',input=f'admin\nAdministrator\n{password}\n'.encode())
            cookies={};checks=0
            def start():
                nonlocal base
                docker('run','-d','--name',app,'--network',network,'--env-file',str(envfile),'-p','127.0.0.1::10000',image)
                mapping=docker('port',app,'10000').decode().strip();base='http://'+mapping
                for _ in range(90):
                    try:
                        with urlopen(base+'/health',timeout=2) as r:
                            if r.status==200:return
                    except OSError:time.sleep(1)
                raise AssertionError('Application startup timed out')
            def call(method,path,status=200,body=None,actor='admin',headers=None):
                nonlocal checks
                h={'X-RecipeAtlas':'1',**(headers or {})}
                if cookies.get(actor):h['Cookie']=cookies[actor]
                if isinstance(body,dict):body=json.dumps(body).encode();h['Content-Type']='application/json'
                try:r=urlopen(Request(base+path,method=method,data=body,headers=h),timeout=40)
                except HTTPError as e:r=e
                with r:
                    assert r.status in (status if isinstance(status,tuple) else (status,)),(method,path,r.status,status)
                    if path.startswith('/api'):assert 'no-store' in r.headers.get('Cache-Control','')
                    if r.headers.get('Set-Cookie'):
                        cookie=r.headers['Set-Cookie'];assert 'secure' in cookie.lower();cookies[actor]=cookie.split(';')[0]
                    data=r.read();checks+=1
                    return json.loads(data) if 'json' in r.headers.get('Content-Type','') and data else data
            base='';start()
            if chromium:
                print(run('node',str(ROOT/'tests/cloud_browser.mjs'),base,chromium).decode().strip())
            else:print('Chromium check skipped: pass --chromium /path/to/browser to enable it.')
            call('POST','/api/auth/login',204,{'username':'admin','password':password})
            call('GET','/api/auth/me')
            index=call('GET','/cookbooks');assert b'<app-root>' in index
            for route in ['/login','/settings','/cookbooks/example/recipes','/shopping-lists/example','/invite?token=synthetic']:
                with urlopen(base+route) as response:
                    assert response.headers['Cache-Control']=='no-cache'
                    assert response.headers['Referrer-Policy']=='no-referrer'
                    assert b'<app-root>' in response.read()
            for asset, mime in [('/ngsw-worker.js','javascript'),('/ngsw.json','json'),('/manifest.webmanifest','manifest')]:
                with urlopen(base+asset) as response:
                    assert mime in response.headers['Content-Type']
                    assert response.headers['Cache-Control']==('public,max-age=86400' if 'manifest' in asset else 'no-cache')
            manifest=call('GET','/manifest.webmanifest');assert manifest['name']=='Recipoke'
            for path in ['/api/unknown','/api/shopping-events/missing','/api/photos/missing','/missing.js']:
                assert b'<app-root>' not in call('GET',path,404)
            worker=call('GET','/ngsw.json');assert not worker['dataGroups'];assert not any('/api' in u for g in worker['assetGroups'] for u in g['urls'])
            script=next(u for g in worker['assetGroups'] for u in g['urls'] if u.startswith('/main-'))
            with urlopen(base+script) as response:
                assert response.headers['Cache-Control']=='public,max-age=31536000,immutable'
            assert call('GET','/api/auth/me',401,actor='anonymous') is not None
            book=call('POST','/api/cookbooks',201,{'name':'Cloud test'})
            bookpath='/api/cookbooks/'+book['id'];payload=json.loads((ROOT/'sample-recipe.json').read_text())
            recipe=call('POST',bookpath+'/recipes',201,payload);path=bookpath+'/recipes/'+recipe['id']
            newer=call('PUT',path,200,{**payload,'title':'Updated','expectedVersion':recipe['version']})
            call('PUT',path,409,{**payload,'expectedVersion':recipe['version']})
            png=b'\x89PNG\r\n\x1a\nsynthetic picture'
            call('PUT',path+'/image',200,png,headers={'If-Match':'"'+newer['version']+'"','Content-Type':'image/png'})
            assert call('GET',path+'/image')==png
            invitation=call('POST','/api/admin/invitations')
            token=urllib.parse.parse_qs(urllib.parse.urlsplit(invitation['link']).fragment)['token'][0]
            call('POST','/api/invitations/redeem',204,{'token':token,'username':'member','displayName':'Member','email':'member@example.test','password':password},actor='member')
            call('POST','/api/auth/login',204,{'username':'member','password':password},actor='member')
            member=call('GET','/api/auth/me',actor='member')
            call('GET',path,404,actor='member')
            bookversion=call('GET',bookpath)['version']
            call('POST',bookpath+'/members',200,{'username':'member','role':'Editor','expectedVersion':bookversion})
            invite=call('GET','/api/cookbook-invitations',actor='member')[0]
            call('POST','/api/cookbook-invitations/'+invite['id'],204,{'accept':True,'expectedVersion':invite['cookbookVersion']},actor='member')
            bookversion=call('GET',bookpath)['version']; recipeversion=call('GET',path)['version']
            with concurrent.futures.ThreadPoolExecutor(2) as executor:
                a=executor.submit(call,'PUT',bookpath+'/members/'+member['id'],200,{'role':'Viewer','expectedVersion':bookversion})
                b=executor.submit(call,'PUT',path,(200,403),{**payload,'title':'Updated','expectedVersion':recipeversion},'member')
                a.result();b.result()
            call('PUT',path,403,{**payload,'expectedVersion':call('GET',path)['version']},actor='member')
            listid=str(uuid.uuid4());listpath='/api/shopping-lists/'+listid
            creation={'listId':listid,'name':'Groceries','operationId':str(uuid.uuid4())}
            call('POST','/api/shopping-lists',200,creation)
            item={'id':str(uuid.uuid4()),'text':'Milk','operationId':str(uuid.uuid4())}
            with concurrent.futures.ThreadPoolExecutor(2) as executor:
                results=list(executor.map(lambda _:call('POST',listpath+'/items',200,item),range(2)))
            assert results[0]==results[1];assert len(call('GET',listpath)['items'])==1
            call('POST',listpath+'/items',409,{**item,'text':'Different'})
            call('POST','/api/shopping-events/negotiate?negotiateVersion=1',200)
            event_result=run('node',str(ROOT/'tests/cloud_events.mjs'),str(frontend),input=json.dumps({'origin':base,'cookie':cookies['admin'],'listId':listid}).encode())
            print(event_result.decode().strip())
            # The only shared state across instances is PostgreSQL and the runtime certificate.
            docker('rm','-f',app);start()
            call('GET','/api/auth/me');assert call('GET',path)['title']=='Updated';assert call('GET',path+'/image')==png
            assert len(call('GET',listpath)['items'])==3
            keys=docker('exec',database,'psql','-U','postgres','-d','recipoke','-Atc','SELECT "Xml" FROM "DataProtectionKeys"').decode()
            assert 'encryptedSecret' in keys and '<masterKey' not in keys
            rotated=directory/'rotated.json'
            run('dotnet',str(ROOT/'backend/RecipeAtlas.Api/bin/Release/net10.0/Recipoke.Api.dll'),'--generate-protection-certificate',str(rotated))
            settings['DataProtection__PreviousCertificates__0__Certificate']=settings['DataProtection__Certificate']
            settings['DataProtection__PreviousCertificates__0__Password']=settings['DataProtection__Password']
            settings.update(json.loads(rotated.read_text()))
            envfile.write_text('\n'.join(k+'='+v for k,v in settings.items()))
            docker('rm','-f',app);start();call('GET','/api/auth/me')
            call('POST','/api/auth/login',204,{'username':'admin','password':password},actor='second')
            call('POST','/api/auth/password',204,{'currentPassword':password,'newPassword':secrets.token_urlsafe(24)},actor='second')
            docker('rm','-f',app);start();call('GET','/api/auth/me',401)
            files=docker('run','--rm','--entrypoint','find',image,'/app','-type','f').decode()
            assert not any(x.endswith(('.db','.pfx','.env.local')) or '/.git/' in x for x in files.splitlines())
            assert docker('image','inspect',image,'--format','{{.Config.User}}').strip() not in (b'',b'root',b'0')
            print(f'Passed {checks} cloud HTTP checks, PostgreSQL migration/no-op upgrade, concurrent idempotency, encrypted keys, replacement cookies/data, certificate rotation and revocation.')
        finally:
            for name in [app,database]:subprocess.run(['docker','rm','-f',name],capture_output=True)
            subprocess.run(['docker','network','rm',network],capture_output=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--image',default='recipoke:cloud-test');p.add_argument('--frontend',type=Path,required=True);p.add_argument('--chromium');a=p.parse_args();main(a.image,a.frontend.resolve(),a.chromium)
