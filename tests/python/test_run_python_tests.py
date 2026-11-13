import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

from tools import run_python_tests as runner


class RunnerTests(unittest.TestCase):
    def test_dynamic_modules_and_changed_json_remain_isolated(self):
        from tests.python import helpers
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            script = folder / 'isolated.py'
            script.write_text('state = []\n')
            with mock.patch.object(helpers, 'TOOLS_DIR', folder):
                first = helpers.load_tool_module('_runner_isolation', 'isolated.py')
                first.state.append('changed')
                second = helpers.load_tool_module('_runner_isolation', 'isolated.py')
                self.assertEqual([], second.state)
                self.assertIsNot(first, second)
            sys.modules.pop('_runner_isolation', None)
            fixture = folder / 'fixture.json'
            fixture.write_text('{"value":1}')
            self.assertEqual(1, helpers.read_json(fixture)['value'])
            fixture.write_text('{"value":2}')
            self.assertEqual(2, helpers.read_json(fixture)['value'])

    def test_routes_blender_imports_and_preserves_module_inventory(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            folder = root / 'tests/python'
            folder.mkdir(parents=True)
            (folder / 'test_one.py').write_text('import bpy\n')
            (folder / 'test_two.py').write_text('import unittest\n')
            self.assertEqual({'unit': ['tests.python.test_two'], 'blender': ['tests.python.test_one']}, runner.inventory(root))

    def test_process_failures_timeout_and_missing_binary_are_reported(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'worker.json'
            for command in ([sys.executable, '-c', 'raise SystemExit(3)'], ['nonexistent-python-test-program-unique']):
                self.assertFalse(runner.run_process(command, path, 3)['successful'])
            self.assertFalse(runner.run_process([sys.executable, '-c', 'import time; time.sleep(5)'], path, 0.05)['successful'])
            path.write_text('{"successful":true}')
            report = runner.run_process([sys.executable, '-c', 'raise SystemExit(0)'], path, 3)
            self.assertFalse(report['successful'])  # cannot reuse stale report

    def test_worker_records_all_cases_and_fixture_errors(self):
        class Sample(unittest.TestCase):
            def test_ok(self): pass
            def test_bad(self): self.assertTrue(False)
            @unittest.skip('explicit reason')
            def test_skip(self): pass
        class Broken(unittest.TestCase):
            @classmethod
            def setUpClass(cls): raise RuntimeError('fixture failure')
            def test_never(self): pass
        suite = unittest.TestSuite([unittest.defaultTestLoader.loadTestsFromTestCase(Sample), unittest.defaultTestLoader.loadTestsFromTestCase(Broken)])
        result = runner.ReportResult(unittest.runner._WritelnDecorator(io.StringIO()), True, 0)
        suite.run(result)
        self.assertFalse(result.wasSuccessful())
        self.assertEqual(['error', 'failed', 'passed', 'skipped'], sorted(x['outcome'] for x in result.cases))

    def test_missing_blender_is_failure_for_each_selected_module(self):
        with tempfile.TemporaryDirectory() as temp, mock.patch.object(runner, 'inventory', return_value={'unit': [], 'blender': ['tests.python.test_fake']}):
            out = Path(temp) / 'report.json'
            self.assertEqual(1, runner.main(['--mode', 'blender', '--blender', str(Path(temp) / 'missing'), '--output', str(out)]))
            report = json.loads(out.read_text())
            self.assertFalse(report['successful'])
            self.assertEqual(['tests.python.test_fake'], report['groups'][0]['modules'])


if __name__ == '__main__':
    unittest.main()
