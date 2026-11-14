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
    def test_parallel_partition_preserves_modules_and_isolates_blender(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            folder = root / 'tests/python'
            folder.mkdir(parents=True)
            for name, source in [('test_a', 'import unittest'), ('test_b', 'import unittest'), ('test_blender', 'import subprocess\nBLENDER = "pinned"')]:
                (folder / (name + '.py')).write_text(source)
            modules = ['tests.python.' + name for name in ('test_a', 'test_b', 'test_blender')]
            (folder / 'blender').mkdir()
            (folder / 'blender/test_indirect.py').write_text('TOOL.apply_textures()')
            modules.append('tests.python.blender.test_indirect')
            shards, exclusive = runner.partition_unit(modules, 2, root)
            self.assertEqual(modules[2:], exclusive)
            flattened = [module for shard in shards for module in shard] + exclusive
            self.assertCountEqual(modules, flattened)
            self.assertEqual(len(modules), len(set(flattened)))

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

    def test_worker_rejects_invalid_or_empty_success_reports(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'worker.json'
            for raw in ('[]', '{"successful":"false"}', '{"successful":true,"testsRun":0}'):
                with self.subTest(raw=raw):
                    command = [sys.executable, '-c', "from pathlib import Path; import sys; Path(sys.argv[1]).write_text(sys.argv[2])", str(path), raw]
                    report = runner.run_process(command, path, 3)
                    self.assertFalse(report['successful'])
                    self.assertEqual(0, report['exitCode'])
                    self.assertTrue(Path(report['log']).is_file())

    def test_empty_inventory_records_failure_in_each_mode(self):
        with tempfile.TemporaryDirectory() as temp, mock.patch.object(runner, 'inventory', return_value={'unit': [], 'blender': []}):
            out = Path(temp) / 'report.json'
            for mode in ('all', 'unit', 'blender'):
                with self.subTest(mode=mode):
                    self.assertEqual(1, runner.main(['--mode', mode, '--output', str(out)]))
                    self.assertEqual('No test modules selected', json.loads(out.read_text())['error'])

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

    def test_partition_unit_balances_shards_by_weights(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            folder = root / 'tests/python'
            folder.mkdir(parents=True)
            modules = []
            for name in ('test_heavy_a', 'test_heavy_b', 'test_light_c', 'test_light_d'):
                (folder / (name + '.py')).write_text('import unittest')
                modules.append('tests.python.' + name)
            weights = {
                'tests.python.test_heavy_a': 10.0,
                'tests.python.test_heavy_b': 10.0,
                'tests.python.test_light_c': 0.5,
                'tests.python.test_light_d': 0.5,
            }
            shards, exclusive = runner.partition_unit(modules, 2, root, weights=weights)
            self.assertEqual([], exclusive)
            self.assertEqual(2, len(shards))
            # LPT places one heavy and one light test into each shard.
            self.assertEqual(10.5, sum(weights[m] for m in shards[0]))
            self.assertEqual(10.5, sum(weights[m] for m in shards[1]))

    def test_worker_isolates_multiple_blender_modules_in_one_session(self):
        with tempfile.TemporaryDirectory() as temp:
            out = Path(temp) / 'worker.json'
            calls = []
            fake_bpy = mock.MagicMock()
            fake_bpy.ops.wm.read_factory_settings.side_effect = lambda **kw: calls.append(kw)
            fake_loader = mock.MagicMock()
            fake_loader.errors = []
            fake_loader.loadTestsFromNames.side_effect = lambda names: unittest.TestSuite()
            with mock.patch.dict(sys.modules, {'bpy': fake_bpy}), mock.patch.object(unittest, 'TestLoader', return_value=fake_loader):
                code = runner.worker(['mod.first', 'mod.second', 'mod.third'], out)
            self.assertEqual(0, code)
            self.assertEqual([{'use_empty': True}, {'use_empty': True}], calls)
            report = json.loads(out.read_text())
            self.assertEqual(['mod.first', 'mod.second', 'mod.third'], report['modules'])


if __name__ == '__main__':
    unittest.main()
