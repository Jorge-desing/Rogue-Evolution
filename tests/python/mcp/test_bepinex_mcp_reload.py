import json
import os
from pathlib import Path
import tempfile
import unittest
import subprocess
import sys
import shutil

from tests.python.helpers import REPO_ROOT, load_tool_module

RELOAD = load_tool_module("bepinex_mcp_reload")


class ReloadTests(unittest.TestCase):
    def test_stdio_process_observes_code_edit_without_reconnection(self):
        source = REPO_ROOT / "tools"
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for name in ("bepinex_mcp_server.py", "bepinex_mcp_reload.py", "build_bepinex_mcp.py"):
                shutil.copy2(source / name, root / name)
            process = subprocess.Popen([sys.executable, str(root / "bepinex_mcp_server.py")], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
            try:
                def initialize(index):
                    process.stdin.write(json.dumps({"jsonrpc":"2.0", "id":index, "method":"initialize", "params":{}}) + "\n")
                    process.stdin.flush()
                    return json.loads(process.stdout.readline())
                first = initialize(1)
                pid = process.pid
                server = root / "bepinex_mcp_server.py"
                code = server.read_text(encoding="utf-8")
                server.write_text(code.replace('SERVER_VERSION = "1.6.0"', 'SERVER_VERSION = "reload-test"'), encoding="utf-8")
                second = initialize(2)
                self.assertEqual("reload-test", second["result"]["serverInfo"]["version"])
                self.assertNotEqual(first["result"]["serverInfo"]["version"], second["result"]["serverInfo"]["version"])
                self.assertEqual(pid, process.pid)
                self.assertIsNone(process.poll())
                process.stdin.write(json.dumps({"jsonrpc": "2.0", "id": 20, "method": "tools/call", "params": {"name": "system_ping", "arguments": {"extra": True}}}) + "\n")
                process.stdin.flush()
                invalid = json.loads(process.stdout.readline())
                self.assertTrue(invalid["result"]["isError"])
                self.assertIn("argumentPath", json.loads(invalid["result"]["content"][0]["text"]))
                self.assertIn("result", initialize(21))
                builder = root / "build_bepinex_mcp.py"
                builder.write_text(builder.read_text(encoding="utf-8") + '\nfor spec in TOOL_SPECS:\n    if spec["name"] == "system_ping":\n        spec["parameters"] = {"type":"object", "required":["marker"]}\n', encoding="utf-8")
                process.stdin.write(json.dumps({"jsonrpc": "2.0", "id": 22, "method": "tools/call", "params": {"name": "system_ping", "arguments": {}}}) + "\n")
                process.stdin.flush()
                row = json.loads(process.stdout.readline())
                if "method" in row:
                    row = json.loads(process.stdout.readline())
                self.assertIn("marker", row["result"]["content"][0]["text"])
                # Restore catalog before the existing reader test.
                builder.write_bytes((source / "build_bepinex_mcp.py").read_bytes())
                process.stdin.write(json.dumps({"jsonrpc": "2.0", "id": 23, "method": "ping"}) + "\n")
                process.stdin.flush()
                row = json.loads(process.stdout.readline())
                if "method" in row:
                    row = json.loads(process.stdout.readline())
                self.assertEqual(23, row["id"])
                # The currently blocked reader may finish one message using
                # old code; after dispatch the next read must use new code.
                code = server.read_text(encoding="utf-8")
                code = code.replace('return json.loads(line.decode("utf-8"))',
                                    'return {**json.loads(line.decode("utf-8")), "id": 99}')
                server.write_text(code, encoding="utf-8")
                initialize(3)
                reader_response = initialize(4)
                self.assertEqual(99, reader_response["id"])
                self.assertEqual(pid, process.pid)
                self.assertIsNone(process.poll())
            finally:
                process.stdin.close()
                process.wait(timeout=15)
                process.stdout.close()
                process.stderr.close()

    def test_reload_keeps_pid_and_resets_instance_cache_without_replaying(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            server = root / "bepinex_mcp_server.py"
            builder = root / "build_bepinex_mcp.py"
            builder.write_text("TOOL_SPECS = []\n")
            template = '''import os
TOOLS = [{"name":"toolVERSION"}]
_DIAGNOSTICS_GO_ID = 999
calls = 0
def handle_request(message):
    global calls, _DIAGNOSTICS_GO_ID
    calls += 1
    prior = _DIAGNOSTICS_GO_ID
    _DIAGNOSTICS_GO_ID = 123
    return {"version": VERSION, "pid": os.getpid(), "calls": calls, "cache": prior}
'''
            server.write_text(template.replace("VERSION", "1"))
            handler = RELOAD.ReloadingHandler(server)
            first, changed = handler.dispatch({})
            self.assertFalse(changed)
            second, _ = handler.dispatch({})
            self.assertEqual(2, second["calls"])
            self.assertIsNone(second["cache"])
            server.write_text(template.replace("VERSION", "2"))
            third, changed = handler.dispatch({})
            self.assertTrue(changed)
            self.assertEqual((2, os.getpid(), 1), (third["version"], third["pid"], third["calls"]))
            self.assertEqual(first["pid"], third["pid"])
            server.write_text(template.replace("VERSION", "4") + '\ndef _read_framed_message(): return {}\n')
            self.assertEqual({}, handler.read_message())
            pending, changed = handler.dispatch({})
            self.assertTrue(changed)
            self.assertEqual(4, pending["version"])
            server.write_text("this is invalid python !!!!")
            with self.assertRaises(SyntaxError):
                handler.dispatch({})
            self.assertEqual(1, handler.module.calls)
            server.write_text(template.replace("VERSION", "3"))
            recovered, _ = handler.dispatch({})
            self.assertEqual(3, recovered["version"])

    def test_real_handler_initializes_and_reloads_builder(self):
        handler = RELOAD.ReloadingHandler(REPO_ROOT / "tools/bepinex_mcp_server.py")
        response, changed = handler.dispatch({"jsonrpc":"2.0", "id":1, "method":"initialize", "params":{}})
        self.assertTrue(response["result"]["capabilities"]["tools"]["listChanged"])
        response, _ = handler.dispatch({"jsonrpc":"2.0", "id":2, "method":"tools/list", "params":{"includeAliases":False}})
        self.assertEqual(231, len(response["result"]["tools"]))


if __name__ == "__main__":
    unittest.main()
