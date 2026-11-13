import json
import os
from pathlib import Path
import tempfile
import unittest
import subprocess
import sys
import shutil

from tests.python.helpers import load_tool_module

RELOAD = load_tool_module("bepinex_mcp_reload")


class ReloadTests(unittest.TestCase):
    def test_stdio_process_observes_code_edit_without_reconnection(self):
        source = Path(__file__).resolve().parents[2] / "tools"
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
            server.write_text("this is invalid python !!!!")
            with self.assertRaises(SyntaxError):
                handler.dispatch({})
            self.assertEqual(1, handler.module.calls)
            server.write_text(template.replace("VERSION", "3"))
            recovered, _ = handler.dispatch({})
            self.assertEqual(3, recovered["version"])

    def test_real_handler_initializes_and_reloads_builder(self):
        handler = RELOAD.ReloadingHandler(Path(__file__).resolve().parents[2] / "tools/bepinex_mcp_server.py")
        response, changed = handler.dispatch({"jsonrpc":"2.0", "id":1, "method":"initialize", "params":{}})
        self.assertTrue(response["result"]["capabilities"]["tools"]["listChanged"])
        response, _ = handler.dispatch({"jsonrpc":"2.0", "id":2, "method":"tools/list", "params":{"includeAliases":False}})
        self.assertEqual(229, len(response["result"]["tools"]))


if __name__ == "__main__":
    unittest.main()
