import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import shutil

from tools.bepinex_mcp_shared import ensure_worker, connect, _STARTED


class SharedTests(unittest.TestCase):
    @unittest.skipUnless(shutil.which('node') and sys.platform == 'win32', 'Windows Node adapter')
    def test_hidden_node_adapter_roundtrip_and_clean_exit(self):
        script = Path(__file__).resolve().parents[2] / 'tools/bepinex_mcp_client.cjs'
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
                script = Path(__file__).resolve().parents[2] / 'tools/bepinex_mcp_shared.py'
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
                for process in _STARTED:
                    if process.pid == pid:
                        process.wait(timeout=10)

