from contextlib import closing
from versioned_requests import with_current_version
"""Consent, profiles, covers and destructive-use-case regression tests. Only disposable SQLite."""
from concurrent.futures import ThreadPoolExecutor
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import socket
import sqlite3
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
DLL=ROOT/'backend/RecipeAtlas.Api/bin/Debug/net10.0/Recipoke.Api.dll'
DOTNET=os.environ.get('RECIPOKE_DOTNET','dotnet')
checks=0
password=secrets.token_urlsafe(24)
PNG=b'\x89PNG\r\n\x1a\n'+b'isolated-picture'

def client():return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
with tempfile.TemporaryDirectory(prefix='recipoke-ui-support-') as directory:
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
    base=f'http://127.0.0.1:{port}'
    env=dict(os.environ,ASPNETCORE_ENVIRONMENT='Development',ASPNETCORE_URLS=base,DataDirectory=directory,Accounts__PublicAppUrl='http://localhost:4200',RecipeText__ApiKey='')
    setup=subprocess.run([DOTNET,str(DLL),'--bootstrap-admin'],input=f'administrator\nAdministrator\n{password}\n',text=True,capture_output=True,env=env,cwd=ROOT)
    assert setup.returncode==0,'Synthetic setup failed'
    log=open(Path(directory)/'api.log','w')
    process=subprocess.Popen([DOTNET,str(DLL)],cwd=ROOT,env=env,stdout=log,stderr=subprocess.STDOUT)
    def call(opener,method,path,expected,body=None,headers=None):
        global checks
        request_headers={'X-RecipeAtlas':'1',**(headers or {})}
        body, request_headers = with_current_version(opener, base, method, path, body, request_headers)
        if isinstance(body,dict):body=json.dumps(body).encode();request_headers['Content-Type']='application/json'
        req=urllib.request.Request(base+path,data=body,method=method,headers=request_headers)
        try:response=opener.open(req,timeout=40)
        except urllib.error.HTTPError as error:response=error
        raw=response.read()
        assert response.status==expected,(method,path,response.status,expected,raw[:150] if expected!=500 else '')
        assert 'no-store' in response.headers.get('Cache-Control','')
        checks+=1
        return json.loads(raw) if raw and 'json' in response.headers.get('Content-Type','') else raw
    def invite_account(admin,username,email=None):
        invitation=call(admin,'POST','/api/admin/invitations',200)
        token=urllib.parse.parse_qs(urllib.parse.urlsplit(invitation['link']).fragment)['token'][0]
        call(public,'POST','/api/invitations/redeem',204,{'token':token,'username':username,'displayName':username,'password':password,'email':email or username+'@example.test'})
        user=client();call(user,'POST','/api/auth/login',204,{'username':username,'password':password})
        return user,call(user,'GET','/api/auth/me',200)
    def accept(user,book):
        invitations=call(user,'GET','/api/cookbook-invitations',200)
        item=next(x for x in invitations if x['cookbookId']==book)
        call(user,'POST','/api/cookbook-invitations/'+item['id'],204,{'accept':True})
    try:
        for _ in range(150):
            if process.poll() is not None:raise RuntimeError('Synthetic API exited')
            try:
                with urllib.request.urlopen(base+'/health',timeout=1):break
            except OSError:time.sleep(.1)
        else:raise RuntimeError('Synthetic API not ready')
        public=client();admin=client()
        call(admin,'POST','/api/auth/login',204,{'username':'administrator','password':password})
        old=call(admin,'GET','/api/auth/me',200);assert old['email'] is None and old['avatarUrl'] is None
        profile=call(admin,'PUT','/api/account/profile',200,{'displayName':'  New name  ','email':' Admin@example.test '})
        assert profile['displayName']=='New name' and profile['email']=='Admin@example.test'
        call(admin,'PUT','/api/account/profile',400,{'displayName':'Should not save','email':'invalid'})
        assert call(admin,'GET','/api/auth/me',200)['displayName']=='New name'
        picture=call(admin,'PUT','/api/account/avatar',200,PNG)
        assert picture['avatarUrl'] and call(admin,'GET',picture['avatarUrl'],200)==PNG
        call(public,'GET',picture['avatarUrl'],401)
        call(admin,'PUT','/api/account/avatar',415,b'<svg/>')
        viewer,viewer_me=invite_account(admin,'viewer');editor,editor_me=invite_account(admin,'editor');stranger,stranger_me=invite_account(admin,'stranger')
        call(stranger,'GET',picture['avatarUrl'],404)
        call(viewer,'PUT','/api/account/profile',400,{'displayName':'Other','email':'admin@EXAMPLE.TEST'})
        call(viewer,'GET','/api/admin/accounts',403)
        book=call(admin,'POST','/api/cookbooks',201,{'name':'Main book','description':'  Private recipes  ','members':[{'username':'viewer','role':'Viewer'}]})
        book_id=book['id'];prefix='/api/cookbooks/'+book_id
        assert book['description']=='Private recipes'
        call(viewer,'GET',prefix,404) # An invitation is not membership.
        call(admin,'POST',prefix+'/members',409,{'username':'VIEWER','role':'Editor'})
        cover=call(admin,'PUT',prefix+'/image',200,PNG)
        assert cover['imageUrl'] and call(viewer,'GET',cover['imageUrl'],200)==PNG # Explicit invitation preview only.
        call(viewer,'GET',picture['avatarUrl'],200)
        call(viewer,'GET',prefix+'/recipes',404)
        call(stranger,'GET',cover['imageUrl'],404)
        members=call(admin,'GET',prefix+'/members',200)['members']
        assert next(x for x in members if x['userId']==viewer_me['id'])['isPending']
        # Seven pending invites are served five at a time; responding reveals the next one.
        for number in range(6):call(admin,'POST','/api/cookbooks',201,{'name':'Queued '+str(number),'members':[{'username':'viewer','role':'Viewer'}]})
        inbox=call(viewer,'GET','/api/cookbook-invitations',200);assert len(inbox)==5
        invitation=next(x for x in inbox if x['cookbookId']==book_id)
        call(stranger,'POST','/api/cookbook-invitations/'+invitation['id'],404,{'accept':True})
        call(viewer,'POST','/api/cookbook-invitations/'+invitation['id'],204,{'accept':True})
        call(viewer,'POST','/api/cookbook-invitations/'+invitation['id'],404,{'accept':True})
        assert call(viewer,'GET',prefix,200)['role']=='Viewer'
        call(viewer,'PUT',prefix+'/image',403,PNG)
        inbox2=call(viewer,'GET','/api/cookbook-invitations',200);assert len(inbox2)==5 and set(x['id'] for x in inbox2)-set(x['id'] for x in inbox)
        declined=inbox2[0]
        call(viewer,'POST','/api/cookbook-invitations/'+declined['id'],204,{'accept':False})
        assert len(call(viewer,'GET','/api/cookbook-invitations',200))==5
        call(viewer,'GET','/api/cookbooks/'+declined['cookbookId'],404)
        call(admin,'POST','/api/cookbooks/'+declined['cookbookId']+'/members',200,{'username':'viewer','role':'Viewer'})
        call(admin,'DELETE','/api/cookbooks/'+declined['cookbookId']+'/invitations/'+viewer_me['id'],200)
        # Failed initial invitations never leave a partially created cookbook.
        before=len(call(admin,'GET','/api/cookbooks',200))
        call(admin,'POST','/api/cookbooks',404,{'name':'Must roll back','members':[{'username':'no-such-user','role':'Viewer'}]})
        call(admin,'POST','/api/cookbooks',409,{'name':'Duplicate staged','members':[{'username':'viewer','role':'Viewer'},{'username':'VIEWER','role':'Editor'}]})
        call(admin,'POST','/api/cookbooks',400,{'name':'Malformed staged','members':[None]})
        assert len(call(admin,'GET','/api/cookbooks',200))==before
        call(admin,'POST',prefix+'/members',400,{'username':'stranger','role':'Owner'})
        call(admin,'POST',prefix+'/members',200,{'username':'editor','role':'Editor'});accept(editor,book_id)
        payload=json.loads((ROOT/'sample-recipe.json').read_text())
        recipe=call(editor,'POST',prefix+'/recipes',201,payload)
        recipe_path=prefix+'/recipes/'+recipe['id'];call(editor,'PUT',recipe_path+'/image',200,PNG)
        call(editor,'DELETE',prefix,403);call(stranger,'DELETE',prefix,404)
        # Existing co-members can read profile pictures; outsiders still cannot.
        uploaded=call(viewer,'PUT','/api/account/avatar',200,PNG)
        call(editor,'GET',uploaded['avatarUrl'],200);call(stranger,'GET',uploaded['avatarUrl'],404)
        # Force failure after the recipes delete. The whole deletion must roll back.
        with closing(sqlite3.connect(Path(directory)/'recipes.db')) as db, db:
            db.execute("CREATE TRIGGER RejectMembershipDeletion BEFORE DELETE ON Memberships BEGIN SELECT RAISE(ABORT,'Synthetic delete failure'); END")
        call(admin,'DELETE',prefix,500)
        call(admin,'GET',recipe_path,200);assert call(admin,'GET',recipe_path+'/image',200)==PNG
        assert call(admin,'GET',prefix,200)['recipeCount']==1
        with closing(sqlite3.connect(Path(directory)/'recipes.db')) as db, db:db.execute('DROP TRIGGER RejectMembershipDeletion')
        call(admin,'DELETE',prefix,204)
        call(admin,'GET',prefix,404);call(viewer,'GET',recipe_path,404);call(viewer,'GET',cover['imageUrl'],404)
        with closing(sqlite3.connect(Path(directory)/'recipes.db')) as db, db:
            assert db.execute('SELECT COUNT(*) FROM Recipes').fetchone()[0]==0
            assert db.execute('SELECT COUNT(*) FROM Images').fetchone()[0]==0
            assert db.execute('SELECT COUNT(*) FROM CookbookImages').fetchone()[0]==0
            assert db.execute('SELECT COUNT(*) FROM ProfileImages').fetchone()[0]==2
            assert not db.execute('PRAGMA foreign_key_check').fetchall()
        # Profile picture changes produce a fresh URL, without a session reset.
        replacement=call(admin,'PUT','/api/account/avatar',200,PNG)
        assert replacement['avatarUrl']!=picture['avatarUrl']
        call(admin,'DELETE','/api/account/avatar',204)
        assert call(admin,'GET','/api/auth/me',200)['avatarUrl'] is None
        print(f'Passed {checks} UI-support HTTP checks: consent/queue, metadata, private media, email uniqueness and atomic cookbook deletion.')
    finally:
        process.terminate();process.wait(timeout=15);log.close()
