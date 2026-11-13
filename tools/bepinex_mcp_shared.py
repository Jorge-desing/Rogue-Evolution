"""Stdio adapters backed by one serialized, authenticated local MCP worker."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import time
from multiprocessing.connection import Client, Listener
from multiprocessing import AuthenticationError

ROOT = Path(__file__).resolve().parents[1]
_STARTED = []


def identity(state: Path):
    key = hashlib.sha256(str(state.resolve()).encode()).hexdigest()[:24]
    if os.name == "nt":
        return r"\\.\pipe\rogue-mcp-" + key, "AF_PIPE"
    return str(state / "worker.sock"), "AF_UNIX"


def connect(state):
    address, family = identity(state)
    return Client(address, family=family, authkey=(state / "auth.key").read_bytes())


def ensure_worker(state):
    state.mkdir(parents=True, exist_ok=True)
    try:
        with connect(state) as conn:
            conn.send_bytes(b'{"control":"ping"}')
            return json.loads(conn.recv_bytes())["pid"]
    except (OSError, EOFError):
        pass
    # The OS releases this startup lock even if the starting adapter crashes.
    with (state / "startup.lock").open("a+b") as lock:
        lock.seek(0)
        lock.write(b"0")
        lock.flush()
        if os.name == "nt":
            import msvcrt
            lock.seek(0)
            msvcrt.locking(lock.fileno(), msvcrt.LK_LOCK, 1)
        else:
            import fcntl
            fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            if not (state / "auth.key").exists():
                (state / "auth.key").write_bytes(secrets.token_bytes(32))
            try:
                with connect(state) as conn:
                    conn.send_bytes(b'{"control":"ping"}')
                    return json.loads(conn.recv_bytes())["pid"]
            except (OSError, EOFError):
                pass
            flags = subprocess.CREATE_NO_WINDOW | subprocess.DETACHED_PROCESS if os.name == "nt" else 0
            with (state / "worker.log").open("ab") as log:
                process = subprocess.Popen([sys.executable, str(Path(__file__).resolve()), "--worker", "--state", str(state)],
                                 stdin=subprocess.DEVNULL, stdout=log, stderr=log,
                                 creationflags=flags, start_new_session=os.name != "nt", cwd=ROOT)
                _STARTED.append(process)
            for _ in range(100):
                try:
                    with connect(state) as conn:
                        conn.send_bytes(b'{"control":"ping"}')
                        return json.loads(conn.recv_bytes())["pid"]
                except (OSError, EOFError):
                    time.sleep(.05)
            raise RuntimeError("Shared MCP worker did not start; inspect " + str(state / "worker.log"))
        finally:
            if os.name == "nt":
                lock.seek(0)
                msvcrt.locking(lock.fileno(), msvcrt.LK_UNLCK, 1)


def worker(state):
    from bepinex_mcp_reload import ReloadingHandler
    handler = ReloadingHandler(ROOT / "tools/bepinex_mcp_server.py")
    address, family = identity(state)
    with Listener(address, family=family, authkey=(state / "auth.key").read_bytes()) as listener:
        while True:
            try:
                with listener.accept() as conn:
                    request = json.loads(conn.recv_bytes(16 * 1024 * 1024))
                    if not isinstance(request, dict):
                        conn.send_bytes(b'{"error":"Invalid worker request"}')
                        continue
                    if request.get("control") == "ping":
                        conn.send_bytes(json.dumps({"pid": os.getpid()}).encode())
                        continue
                    if request.get("control") == "stop":
                        conn.send_bytes(b'{}')
                        return
                    if "message" not in request:
                        conn.send_bytes(b'{"error":"Missing worker message"}')
                        continue
                    message = request["message"]
                    try:
                        response, _ = handler.dispatch(message)
                        result = {"response": response, "catalog": hashlib.sha256(handler.catalog.encode()).hexdigest(), "pid": os.getpid()}
                    except Exception as exc:
                        result = {"response": {"jsonrpc": "2.0", "id": message.get("id") if isinstance(message, dict) else None,
                                               "error": {"code": -32603, "message": str(exc)}}}
                    conn.send_bytes(json.dumps(result).encode())
            except (OSError, EOFError, ValueError, UnicodeError, AuthenticationError):
                # A disconnected adapter never causes a replay of its request.
                continue


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--worker", action="store_true")
    parser.add_argument("--state", type=Path)
    args = parser.parse_args()
    os.environ.setdefault("BEPINEX_MCP_TOOL_FILTER", "includeAliases=false")
    settings = os.environ.get("BEPINEX_MCP_URL", "default") + os.environ.get("BEPINEX_MCP_TOOL_FILTER", "")
    state = args.state or ROOT / "artifacts/mcp-shared" / hashlib.sha256(settings.encode()).hexdigest()[:16]
    if args.worker:
        worker(state)
        return 0
    ensure_worker(state)
    from bepinex_mcp_reload import ReloadingHandler
    reader = ReloadingHandler(ROOT / "tools/bepinex_mcp_server.py")
    catalog = None
    while True:
        message = None
        try:
            message = reader.read_message()
            if message is None:
                return 0
            # Reconnect per request; never retry an already submitted request.
            ensure_worker(state)
            with connect(state) as conn:
                conn.send_bytes(json.dumps({"message": message}).encode())
                result = json.loads(conn.recv_bytes(32 * 1024 * 1024))
            if catalog is not None and result.get("catalog") != catalog:
                print(json.dumps({"jsonrpc": "2.0", "method": "notifications/tools/list_changed"}), flush=True)
            catalog = result.get("catalog", catalog)
            response = result["response"]
        except Exception as exc:
            response = {"jsonrpc": "2.0", "id": message.get("id") if isinstance(message, dict) else None,
                        "error": {"code": -32603, "message": "Shared transport error; inspect postconditions before retrying: " + str(exc)}}
            if getattr(exc, "fatal_framing", False):
                print(json.dumps(response), flush=True)
                return 1
        if response is not None:
            print(json.dumps(response), flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
