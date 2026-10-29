"""Explicitly load a Neon dotenv file and invoke an operator command. No shell evaluation."""
import argparse
import json
import os
from pathlib import Path
import subprocess
from urllib.parse import urlsplit, unquote

def connection(url):
    parsed=urlsplit(url)
    if parsed.scheme not in ('postgres','postgresql') or not parsed.hostname or not parsed.path.strip('/'):
        raise ValueError('Expected a PostgreSQL URL')
    def quote(value):return '"'+str(value).replace('"','""')+'"'
    fields={'Host':parsed.hostname,'Port':parsed.port or 5432,'Database':unquote(parsed.path[1:]),'Username':unquote(parsed.username or ''),'Password':unquote(parsed.password or ''),'SSL Mode':'VerifyFull'}
    if any(any(ord(c)<32 for c in str(value)) for value in fields.values()):
        raise ValueError('Control characters are not supported in connection settings')
    return ';'.join(k+'='+quote(v) for k,v in fields.items())

def environment(path, protection):
    values={}
    for line in path.read_text().splitlines():
        line=line.strip()
        if not line or line.startswith('#'):continue
        key,sep,value=line.partition('=')
        if key not in {'DATABASE_URL','DATABASE_URL_UNPOOLED'}:continue
        if not sep:raise ValueError('Invalid dotenv entry')
        values[key]=value.strip().strip('\"\'')
    result=dict(os.environ,Database__Provider='PostgreSql',Database__Connection=connection(values['DATABASE_URL']),Database__MigrationConnection=connection(values['DATABASE_URL_UNPOOLED']))
    if protection:
        material=json.loads(protection.read_text())
        for key in ['DataProtection__Certificate','DataProtection__Password']:
            value=material[key]
            if not isinstance(value,str) or not value or any(ord(c)<32 for c in value):raise ValueError('Invalid protection material')
            result[key]=value
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--env-file',type=Path,required=True);p.add_argument('--protection',type=Path);p.add_argument('--write-render-env',type=Path);p.add_argument('--public-url');p.add_argument('command',nargs=argparse.REMAINDER);a=p.parse_args()
    command=a.command[1:] if a.command and a.command[0]=='--' else a.command
    if not command and not a.write_render_env:p.error('A command is required after --')
    try:env=environment(a.env_file,a.protection)
    except Exception:raise SystemExit('Invalid local database/protection configuration; secret values omitted.')
    if a.write_render_env:
        url=urlsplit(a.public_url or '')
        if not a.protection or url.scheme!='https' or not url.hostname or url.username or url.password or url.path not in ('','/') or url.query or url.fragment or any(ord(c)<32 for c in (a.public_url or '')):p.error('Export requires --protection and an HTTPS origin in --public-url')
        keys=['Database__Provider','Database__Connection','DataProtection__Certificate','DataProtection__Password']
        with os.fdopen(os.open(a.write_render_env, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), 'w') as output:
            output.write('\n'.join(key+'='+env[key] for key in keys)+'\nAccounts__PublicAppUrl='+a.public_url+'\nPORT=10000\nASPNETCORE_ENVIRONMENT=Production\n')
        print('Render secrets file written. Import privately; do not commit, upload as an artifact or include in an image.')
    else:raise SystemExit(subprocess.run(command,env=env).returncode)
