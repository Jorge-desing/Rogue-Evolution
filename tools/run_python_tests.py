"""Run the complete unittest inventory with CPython and pinned Blender."""
from __future__ import annotations

import argparse
import ast
import json
import math
import os
import signal
import subprocess
import sys
import time
import unittest
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def needs_blender(path: Path) -> bool:
    tree = ast.parse(path.read_text(encoding="utf-8-sig"), filename=str(path))
    return any((isinstance(n, ast.Import) and any(a.name.split('.')[0] in ('bpy', 'mathutils') for a in n.names))
               or (isinstance(n, ast.ImportFrom) and (n.module or '').split('.')[0] in ('bpy', 'mathutils')) for n in ast.walk(tree))


def inventory(root: Path = ROOT) -> dict[str, list[str]]:
    groups = {'unit': [], 'blender': []}
    for path in sorted((root / 'tests/python').rglob('test_*.py')):
        name = '.'.join(path.relative_to(root).with_suffix('').parts)
        groups['blender' if needs_blender(path) else 'unit'].append(name)
    return groups


def partition_unit(modules: list[str], jobs: int, root: Path = ROOT) -> tuple[list[list[str]], list[str]]:
    """Keep Blender subprocess tests exclusive; never split module fixtures."""
    shards = [[] for _ in range(jobs)]
    exclusive = []
    for module in modules:
        path = (root / Path(*module.split('.'))).with_suffix('.py')
        source = path.read_text(encoding='utf-8-sig')
        tree = ast.parse(source, filename=str(path))
        blender_binding = any(
            (isinstance(node, ast.Name) and 'blender' in node.id.lower())
            or (isinstance(node, ast.Attribute) and 'blender' in node.attr.lower())
            for node in ast.walk(tree))
        if '.blender.' in module or blender_binding or ('blender' in source.lower() and 'subprocess' in source):
            exclusive.append(module)
        else:
            min(shards, key=len).append(module)
    return [shard for shard in shards if shard], exclusive


class ReportResult(unittest.TextTestResult):
    def __init__(self, *args):
        super().__init__(*args)
        self.cases = []
        self.preparation = 0.0
        self.current = None

    def startTest(self, test):
        super().startTest(test)
        self.current = {'id': test.id(), 'outcome': 'passed', 'details': []}
        self.started = time.perf_counter()

    def stopTest(self, test):
        self.current['seconds'] = time.perf_counter() - self.started
        self.cases.append(self.current)
        self.current = None
        super().stopTest(test)

    def addFailure(self, test, err):
        self.current['outcome'] = 'failed'
        self.current['details'].append(self._exc_info_to_string(err, test))
        super().addFailure(test, err)

    def addError(self, test, err):
        # Class/module fixture errors can occur outside startTest.
        if self.current is None:
            self.cases.append({'id': test.id(), 'outcome': 'error', 'details': [self._exc_info_to_string(err, test)], 'seconds': 0})
        else:
            self.current['outcome'] = 'error'
            self.current['details'].append(self._exc_info_to_string(err, test))
        super().addError(test, err)

    def addSkip(self, test, reason):
        if self.current is None:
            self.cases.append({'id': test.id(), 'outcome': 'skipped', 'reason': reason, 'seconds': 0})
        else:
            self.current.update(outcome='skipped', reason=reason)
        super().addSkip(test, reason)

    def addExpectedFailure(self, test, err):
        self.current['outcome'] = 'expectedFailure'
        super().addExpectedFailure(test, err)

    def addUnexpectedSuccess(self, test):
        self.current['outcome'] = 'unexpectedSuccess'
        super().addUnexpectedSuccess(test)

    def addSubTest(self, test, subtest, err):
        if err is not None:
            self.current['outcome'] = 'failed'
            self.current['details'].append({'subtest': str(subtest), 'traceback': self._exc_info_to_string(err, test)})
        super().addSubTest(test, subtest, err)


def flatten(suite):
    for item in suite:
        if isinstance(item, unittest.TestSuite):
            yield from flatten(item)
        else:
            yield item.id()


def worker(modules: list[str], output: Path) -> int:
    sys.path.insert(0, str(ROOT))
    started = time.perf_counter()
    loader = unittest.TestLoader()
    suite = loader.loadTestsFromNames(modules)
    ids = list(flatten(suite))
    discovery = time.perf_counter() - started
    result = ReportResult(unittest.runner._WritelnDecorator(sys.stderr), True, 1)
    original = unittest.TestCase._callSetUp
    def timed_setup(case):
        before = time.perf_counter()
        try:
            return original(case)
        finally:
            result.preparation += time.perf_counter() - before
    unittest.TestCase._callSetUp = timed_setup
    before = time.perf_counter()
    try:
        suite.run(result)
    finally:
        unittest.TestCase._callSetUp = original
    elapsed = time.perf_counter() - before
    report = {'modules': modules, 'inventory': ids, 'cases': result.cases,
              'successful': result.wasSuccessful(), 'testsRun': result.testsRun,
              'discoverySeconds': discovery, 'preparationSeconds': result.preparation,
              'executionSeconds': elapsed - result.preparation,
              'fixtureTimingScope': 'preparation measures per-test setUp; class/module fixtures are included in execution',
              'errors': loader.errors}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    return 0 if report['successful'] else 1


def run_process(command: list[str], output: Path, timeout: float) -> dict:
    started = time.perf_counter()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.unlink(missing_ok=True)
    try:
        process = subprocess.Popen(command, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   text=True, encoding='utf-8', errors='replace', start_new_session=os.name != 'nt')
        try:
            stdout, stderr = process.communicate(timeout=timeout)
        except subprocess.TimeoutExpired:
            if os.name == 'nt':
                subprocess.run(['taskkill.exe', '/PID', str(process.pid), '/T', '/F'], capture_output=True)
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.communicate()
            raise
        log = output.with_suffix('.log')
        log.write_text(stdout + '\n' + stderr, encoding='utf-8')
        if not output.exists():
            return {'successful': False, 'error': 'Worker exited without a report', 'exitCode': process.returncode, 'log': str(log), 'wallSeconds': time.perf_counter() - started}
        report = json.loads(output.read_text(encoding='utf-8'))
        if not isinstance(report, dict) or type(report.get('successful')) is not bool:
            return {'successful': False, 'error': 'Invalid worker report: expected an object with a boolean successful field',
                    'exitCode': process.returncode, 'log': str(log), 'wallSeconds': time.perf_counter() - started}
        if report['successful'] and (type(report.get('testsRun')) is not int or report['testsRun'] < 1):
            return {'successful': False, 'error': 'Invalid worker report: successful worker ran no tests',
                    'exitCode': process.returncode, 'log': str(log), 'wallSeconds': time.perf_counter() - started}
        report.update(exitCode=process.returncode, wallSeconds=time.perf_counter() - started, log=str(log))
        report['successful'] = report['successful'] and process.returncode == 0
        return report
    except (subprocess.TimeoutExpired, OSError, ValueError, KeyError) as exc:
        return {'successful': False, 'error': str(exc), 'wallSeconds': time.perf_counter() - started}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode', choices=('all', 'unit', 'blender'), default='all')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/python-tests/report.json')
    parser.add_argument('--blender', type=Path, default=ROOT / '.tools/Blender-4.5.14/blender-4.5.14-windows-x64/blender.exe')
    parser.add_argument('--timeout', type=float, default=600)
    parser.add_argument('--jobs', type=int, default=2, help='Unit workers (1-4), plus one serial Blender-integration worker. 1 selects the fully serial reference.')
    parser.add_argument('--worker', nargs='+', help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    if args.worker:
        return worker(args.worker, args.output)
    if not math.isfinite(args.timeout) or args.timeout <= 0:
        parser.error('--timeout must be finite and positive')
    if not 1 <= args.jobs <= 4:
        parser.error('--jobs must be in [1, 4]')
    args.output = args.output.resolve()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    started = time.perf_counter()
    try:
        groups = inventory()
    except (OSError, SyntaxError) as exc:
        args.output.write_text(json.dumps({'successful': False, 'error': str(exc), 'phase': 'discovery'}), encoding='utf-8')
        return 1
    reports = []
    script = str(Path(__file__).resolve())
    if args.mode in ('all', 'unit') and groups['unit']:
        if args.jobs == 1:
            shards, exclusive = [groups['unit']], []
        else:
            shards, exclusive = partition_unit(groups['unit'], args.jobs)
        def execute_unit(item):
            index, modules = item
            destination = args.output.with_name(args.output.stem + f'-unit-{index}.json')
            return {'group': 'unit', 'modules': modules, **run_process([sys.executable, script, '--output', str(destination), '--worker', *modules], destination, args.timeout)}
        # A single integration worker owns every Blender subprocess launch;
        # pure unit shards can progress while it runs. Never overlap two
        # Blender integration modules, nor split a class/module fixture.
        tasks = list(enumerate(shards))
        if exclusive:
            tasks.append((len(shards), exclusive))
        with ThreadPoolExecutor(max_workers=args.jobs + bool(exclusive)) as executor:
            reports.extend(executor.map(execute_unit, tasks))
    if args.mode in ('all', 'blender'):
        for index, module in enumerate(groups['blender']):
            destination = args.output.with_name(args.output.stem + f'-blender-{index}.json')
            destination.unlink(missing_ok=True)
            if not args.blender.is_file():
                reports.append({'group': 'blender', 'modules': [module], 'successful': False, 'error': f'Blender missing: {args.blender}'})
                continue
            command = [str(args.blender), '--background', '--factory-startup', '--python-exit-code', '1', '--python', script, '--', '--output', str(destination), '--worker', module]
            reports.append({'group': 'blender', 'modules': [module], **run_process(command, destination, args.timeout)})
    report = {'mode': args.mode, 'jobs': args.jobs, 'moduleInventory': groups, 'groups': reports,
              'wallSeconds': time.perf_counter() - started, 'successful': bool(reports) and all(r['successful'] for r in reports)}
    if not reports:
        report['error'] = 'No test modules selected'
    args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({'successful': report['successful'], 'wallSeconds': report['wallSeconds'], 'report': str(args.output)}))
    return 0 if report['successful'] else 1


if __name__ == '__main__':
    raise SystemExit(main(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else None))
