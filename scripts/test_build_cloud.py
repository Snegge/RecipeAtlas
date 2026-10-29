import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import build_cloud


class BuildContextTests(unittest.TestCase):
    def test_staging_excludes_local_data_secrets_and_caches(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            backend, frontend, context = root/'backend', root/'frontend', root/'context'
            for path in [backend/'backend/RecipeAtlas.Api', backend/'backend/Recipoke.Domain',
                         backend/'tools', backend/'deploy', frontend/'src', frontend/'public', context]:
                path.mkdir(parents=True)
            for path in [backend/'tools/requirements-yt-dlp.txt', backend/'deploy/Render.Dockerfile',
                         *[frontend/name for name in ['package.json','package-lock.json','angular.json',
                           'tsconfig.json','tsconfig.app.json','ngsw-config.json']]]:
                path.write_text('synthetic')
            for source in [backend/'backend/RecipeAtlas.Api', frontend/'src']:
                (source/'source.txt').write_text('public')
                for name in ['.env.local','private.pfx','recipes.db','recipes.db-wal','backup.sqlite','dump.bak']:
                    (source/name).write_text('must not be staged')
                for name in ['.git','bin','obj','data','node_modules']:
                    (source/name).mkdir()
                    (source/name/'private.txt').write_text('must not be staged')
            with patch.object(build_cloud, 'ROOT', backend):
                build_cloud.stage(frontend, context)
            files = list(context.rglob('*'))
            self.assertEqual(2, sum(path.name=='source.txt' for path in files))
            self.assertFalse(any(path.is_file() and path.read_text()=='must not be staged' for path in files))


if __name__ == '__main__':
    unittest.main()
