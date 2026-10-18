"""Optional real-binary upgrade from accounts/cookbooks before UI metadata. Never uses user data."""
import hashlib
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import socket
import sqlite3
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
ROOT=Path(__file__).resolve().parents[1]
if len(sys.argv)!=2:raise SystemExit('Usage: python3 tests/ui_upgrade.py /path/to/pre-ui-publish/Recipoke.Api.dll')
old=Path(sys.argv[1]).resolve();new=ROOT/'backend/RecipeAtlas.Api/bin/Debug/net10.0/Recipoke.Api.dll'
dotnet=os.environ.get('RECIPOKE_DOTNET','dotnet');password=secrets.token_urlsafe(24)
with socket.socket() as sock:sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
base=f'http://127.0.0.1:{port}'
def client():return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
def call(opener,method,path,status,body=None):
    headers={'X-RecipeAtlas':'1'}
    if isinstance(body,dict):body=json.dumps(body).encode();headers['Content-Type']='application/json'
    req=urllib.request.Request(base+path,data=body,method=method,headers=headers)
    try:response=opener.open(req,timeout=15)
    except urllib.error.HTTPError as error:response=error
    raw=response.read();assert response.status==status,(method,path,response.status,status)
    return json.loads(raw) if raw and 'json' in response.headers.get('Content-Type','') else raw
with tempfile.TemporaryDirectory(prefix='recipoke-ui-upgrade-') as directory:
    env=dict(os.environ,DataDirectory=directory,ASPNETCORE_ENVIRONMENT='Development',ASPNETCORE_URLS=base,Accounts__PublicAppUrl='http://localhost:4200',RecipeText__ApiKey='')
    def start(dll):
        log=open(Path(directory)/(dll.parent.name+'.log'),'w')
        process=subprocess.Popen([dotnet,str(dll)],cwd=ROOT,env=env,stdout=log,stderr=subprocess.STDOUT)
        for _ in range(150):
            if process.poll() is not None:raise RuntimeError('Synthetic upgrade API exited')
            try:
                with urllib.request.urlopen(base+'/health',timeout=1):return process,log
            except OSError:time.sleep(.1)
        process.terminate();process.wait(timeout=10);log.close();raise RuntimeError('Synthetic upgrade startup failed')
    def stop(process,log):process.terminate();process.wait(timeout=10);log.close()
    setup=subprocess.run([dotnet,str(old),'--bootstrap-admin'],input=f'administrator\nAdministrator\n{password}\n',text=True,capture_output=True,env=env,cwd=ROOT)
    assert setup.returncode==0,'Old accounts-version bootstrap failed'
    admin,member,public=client(),client(),client()
    process,log=start(old)
    try:
        call(admin,'POST','/api/auth/login',204,{'username':'administrator','password':password})
        old_me=call(admin,'GET','/api/auth/me',200)
        invitation=call(admin,'POST','/api/admin/invitations',200)
        token=urllib.parse.parse_qs(urllib.parse.urlsplit(invitation['link']).fragment)['token'][0]
        call(public,'POST','/api/invitations/redeem',204,{'token':token,'username':'existing-member','displayName':'Existing Member','password':password})
        call(member,'POST','/api/auth/login',204,{'username':'existing-member','password':password})
        book=call(admin,'GET','/api/cookbooks',200)[0];prefix='/api/cookbooks/'+book['id']
        call(admin,'POST',prefix+'/members',204,{'username':'existing-member','role':'Viewer'})
        recipe=call(admin,'POST',prefix+'/recipes',201,json.loads((ROOT/'sample-recipe.json').read_text()))
        path=prefix+'/recipes/'+recipe['id'];png=b'\x89PNG\r\n\x1a\n'+b'legacy image'
        call(admin,'PUT',path+'/image',200,png);before=call(member,'GET',path,200)
    finally:stop(process,log)
    db_path=Path(directory)/'recipes.db'
    with sqlite3.connect(db_path) as db:
        tables=[row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%'")]
        columns={table:[row[1] for row in db.execute('PRAGMA table_info("'+table+'")')] for table in tables}
        original={table:db.execute('SELECT '+','.join('"'+c+'"' for c in names)+' FROM "'+table+'" ORDER BY 1').fetchall() for table,names in columns.items()}
        history=db.execute('SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId').fetchall()
    keys={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (Path(directory)/'keys').iterdir()}
    process,log=start(new)
    try:
        current=call(admin,'GET','/api/auth/me',200)
        assert current['id']==old_me['id'] and current['email'] is None and current['avatarUrl'] is None
        after=call(member,'GET',path,200);after.pop('version')
        assert after==before and call(member,'GET',path+'/image',200)==png
        assert call(member,'GET',prefix,200)['role']=='Viewer'
        assert call(member,'GET','/api/cookbook-invitations',200)==[] # Existing sharing is not converted to pending.
        details=call(admin,'GET',prefix,200);assert details['description'] is None and details['imageUrl'] is None and details['recipeCount']==1
    finally:stop(process,log)
    with sqlite3.connect(db_path) as db:
        for table,names in columns.items():assert db.execute('SELECT '+','.join('"'+c+'"' for c in names)+' FROM "'+table+'" ORDER BY 1').fetchall()==original[table],table+' changed'
        upgraded=db.execute('SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId').fetchall()
        assert upgraded[:len(history)]==history and len(upgraded)==len(history)+2
        assert upgraded[-2][0].endswith('_CookbookInvitationsAndProfiles') and upgraded[-1][0].endswith('_OptimisticAggregateVersions')
        assert not db.execute('PRAGMA foreign_key_check').fetchall()
    assert keys=={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (Path(directory)/'keys').iterdir()}
    print('UI upgrade passed: existing Identity sessions, users, memberships, every previous column, recipes, quantity strings, image bytes and key ring preserved; two additive migrations.')
