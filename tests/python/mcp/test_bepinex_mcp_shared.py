import ctypes
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
import shutil

from tests.python.helpers import REPO_ROOT
from tools.bepinex_mcp_shared import ensure_worker, connect, identity, _STARTED
from multiprocessing.connection import Client
from multiprocessing import AuthenticationError


def _wait_for_pid_exit(pid: int, state: Path, timeout: float = 10.0) -> None:
    """Wait for worker and launcher processes to release state files before temp cleanup."""
    for process in list(_STARTED):
        process.wait(timeout=timeout)
    _STARTED.clear()
    if os.name == "nt":
        synchronize = 0x00100000
        kernel32 = ctypes.windll.kernel32
        handle = kernel32.OpenProcess(synchronize, False, int(pid))
        if handle:
            try:
                kernel32.WaitForSingleObject(handle, int(timeout * 1000))
            finally:
                kernel32.CloseHandle(handle)
        log_path = state / "worker.log"
        deadline = time.monotonic() + min(timeout, 2.0)
        while log_path.exists():
            try:
                log_path.unlink()
                break
            except PermissionError:
                if time.monotonic() >= deadline:
                    raise
                time.sleep(0.01)


class SharedTests(unittest.TestCase):
    def test_worker_survives_rejected_auth_and_malformed_requests(self):
        with tempfile.TemporaryDirectory() as tmp:
            state = Path(tmp)
            pid = ensure_worker(state)
            try:
                address, family = identity(state)
                with self.assertRaises((AuthenticationError, EOFError, OSError)):
                    Client(address, family=family, authkey=b'wrong-key')
                for data in (b'[]', b'{}', b'not-json'):
                    with connect(state) as conn:
                        conn.send_bytes(data)
                        if data != b'not-json':
                            self.assertIn('error', json.loads(conn.recv_bytes()))
                self.assertEqual(pid, ensure_worker(state))
            finally:
                with connect(state) as conn:
                    conn.send_bytes(b'{"control":"stop"}')
                    conn.recv_bytes()
                _wait_for_pid_exit(pid, state)

    @unittest.skipUnless(shutil.which('node') and sys.platform == 'win32', 'Windows Node adapter')
    def test_hidden_node_adapter_roundtrip_and_clean_exit(self):
        script = REPO_ROOT / 'tools/bepinex_mcp_client.cjs'
        result = subprocess.run([shutil.which('node'), str(script)],
                                input='{"jsonrpc":"2.0","id":9,"method":"ping"}\n',
                                capture_output=True, text=True, timeout=15,
                                creationflags=subprocess.CREATE_NO_WINDOW)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)['id'], 9)

    def test_two_clients_share_worker_and_keep_it_after_disconnect(self):
        with tempfile.TemporaryDirectory() as tmp:
            state = Path(tmp)
            clients = []
            try:
                script = REPO_ROOT / 'tools/bepinex_mcp_shared.py'
                for _ in range(2):
                    clients.append(subprocess.Popen([sys.executable, str(script), '--state', str(state)], stdin=subprocess.PIPE,
                                                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8'))
                pid = ensure_worker(state)
                self.assertEqual(pid, ensure_worker(state))
                def call(client, index):
                    client.stdin.write(json.dumps({'jsonrpc': '2.0', 'id': index, 'method': 'tools/list', 'params': {'includeAliases': False}}) + '\n')
                    client.stdin.flush()
                    return json.loads(client.stdout.readline())
                self.assertGreater(len(call(clients[0], 1)['result']['tools']), 200)
                self.assertEqual(call(clients[1], 2)['id'], 2)
                clients[0].stdin.close()
                clients[0].wait(timeout=10)
                self.assertEqual(call(clients[1], 3)['id'], 3)
                self.assertEqual(pid, ensure_worker(state))
            finally:
                for client in clients:
                    if not client.stdin.closed:
                        client.stdin.close()
                    client.wait(timeout=10)
                    client.stdout.close()
                    client.stderr.close()
                with connect(state) as conn:
                    conn.send_bytes(b'{"control":"stop"}')
                    conn.recv_bytes()
                _wait_for_pid_exit(pid, state)

