import json
from pathlib import Path
import tempfile
import unittest
import subprocess
import sys
from cloud_env import connection, environment

class ConnectionTests(unittest.TestCase):
    def test_escaped_credentials_and_verified_tls(self):
        value=connection('postgresql://user:p%3B%22%40word@host.example/db%20name?sslmode=require')
        self.assertIn('Password="p;""@word"',value)
        self.assertIn('SSL Mode="VerifyFull"',value)
        self.assertIn('Database="db name"',value)

    def test_invalid(self):
        for value in ['https://host/path','postgresql://host/','postgresql://user:p%0Aevil@host/db']:
            with self.assertRaises(ValueError):connection(value)

    def test_explicit_parser_does_not_evaluate_or_load_unrelated_settings(self):
        with tempfile.TemporaryDirectory() as directory:
            directory=Path(directory)
            config=directory/'local.env'; protection=directory/'protection.json'
            config.write_text("DATABASE_URL='postgresql://user:$(echo-never-run)@pooler.example/db'\nDATABASE_URL_UNPOOLED=postgresql://user:pass@direct.example/db\nIGNORED=not-an-environment-variable\n")
            protection.write_text(json.dumps({'DataProtection__Certificate':'synthetic','DataProtection__Password':'synthetic','Database__Provider':'Unexpected'}))
            env=environment(config,protection)
            self.assertIn('$(echo-never-run)',env['Database__Connection'])
            self.assertIn('direct.example',env['Database__MigrationConnection'])
            self.assertEqual('PostgreSql',env['Database__Provider'])
            self.assertNotIn('IGNORED',env)

    def test_render_export_contains_only_runtime_settings_and_never_overwrites(self):
        with tempfile.TemporaryDirectory() as directory:
            directory=Path(directory)
            config=directory/'local.env'; protection=directory/'protection.json'; output=directory/'render.env'
            config.write_text('DATABASE_URL=postgresql://user:synthetic@pooler.example/db\nDATABASE_URL_UNPOOLED=postgresql://user:synthetic@direct.example/db\n')
            protection.write_text(json.dumps({'DataProtection__Certificate':'synthetic','DataProtection__Password':'synthetic'}))
            command=[sys.executable,str(Path(__file__).with_name('cloud_env.py')),'--env-file',str(config),
                     '--protection',str(protection),'--public-url','https://recipoke.example','--write-render-env',str(output)]
            result=subprocess.run(command,capture_output=True)
            self.assertEqual(0,result.returncode)
            self.assertNotIn(b'synthetic',result.stdout+result.stderr)
            contents=output.read_text()
            self.assertIn('Database__Connection=',contents)
            self.assertNotIn('MigrationConnection',contents)
            self.assertNotIn('direct.example',contents)
            self.assertNotEqual(0,subprocess.run(command,capture_output=True).returncode)
            self.assertEqual(contents,output.read_text())

if __name__=='__main__':unittest.main()
