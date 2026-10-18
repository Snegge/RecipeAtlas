"""Run the HTTP suites while retaining SQLite references to detect leaked test connections.

A connection's transaction context does not close it. Unix can unlink an open database,
whereas Windows raises WinError 32. Holding references makes missing close() calls
observable on either platform without relying on garbage collection or retry sleeps.
"""
from pathlib import Path
import runpy
import sqlite3
import sys
import unittest
from unittest.mock import patch

TESTS = Path(__file__).resolve().parent


class SqliteCleanupTests(unittest.TestCase):
    def run_suite(self, filename):
        original_connect = sqlite3.connect
        connections = []

        def connect(*args, **kwargs):
            connection = original_connect(*args, **kwargs)
            connections.append(connection)  # Deliberately prevent GC from hiding missing disposal.
            return connection

        leaked = 0
        self.addCleanup(lambda: [connection.close() for connection in connections])
        with patch.object(sqlite3, 'connect', side_effect=connect):
            runpy.run_path(str(TESTS / filename), run_name='__main__')
        self.assertTrue(connections, 'The suite must inspect real SQLite data.')
        for connection in connections:
            try:
                connection.execute('SELECT 1').fetchone()
            except sqlite3.ProgrammingError as error:
                self.assertIn('closed', str(error).lower())
            else:
                leaked += 1
        self.assertEqual(leaked, 0, f'{filename}: SQLite connections must close before temporary cleanup')

    def test_accounts(self):
        self.run_suite('accounts_integration.py')

    def test_profiles_and_covers(self):
        self.run_suite('ui_support_integration.py')

    def test_concurrent_editing(self):
        self.run_suite('concurrency_integration.py')


if __name__ == '__main__':
    # The suites import their existing versioned request fixtures from this directory.
    sys.path.insert(0, str(TESTS))
    unittest.main()
