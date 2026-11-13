"""Stateless loopback MCP endpoint; no per-chat stdio processes or sessions."""
from __future__ import annotations

import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import secrets
import threading

try:
    from .bepinex_mcp_shared import ROOT, connect, ensure_worker
except ImportError:
    from bepinex_mcp_shared import ROOT, connect, ensure_worker

STATE = ROOT / 'artifacts/mcp-http'
PORT = 18764


def make_server(port=PORT, token=None):
    STATE.mkdir(parents=True, exist_ok=True)
    key = STATE / 'token'
    if token is None:
        if not key.exists():
            key.write_text(secrets.token_urlsafe(32), encoding='ascii')
        token = key.read_text(encoding='ascii')
    os.environ.setdefault('BEPINEX_MCP_TOOL_FILTER', 'includeAliases=false')
    settings = os.environ.get('BEPINEX_MCP_URL', 'default') + os.environ['BEPINEX_MCP_TOOL_FILTER']
    worker_state = ROOT / 'artifacts/mcp-shared' / hashlib.sha256(settings.encode()).hexdigest()[:16]
    gate = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = 'HTTP/1.1'

        def setup(self):
            super().setup()
            self.connection.settimeout(10)
        def log_message(self, *args):
            pass

        def reply(self, status, value=None):
            body = json.dumps(value).encode() if value is not None else b''
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Transfer-Encoding', 'chunked')
            self.send_header('Connection', 'close')
            self.end_headers()
            if body:
                self.wfile.write(f'{len(body):x}\r\n'.encode() + body + b'\r\n')
            self.wfile.write(b'0\r\n\r\n')
            self.close_connection = True

        def allowed(self):
            if self.path != '/mcp':
                self.reply(404)
                return False
            origin = self.headers.get('Origin')
            if origin and origin not in ('http://localhost', f'http://localhost:{port}', f'http://127.0.0.1:{port}'):
                self.reply(403)
                return False
            if not secrets.compare_digest(self.headers.get('Authorization', ''), 'Bearer ' + token):
                length = self.headers.get('Content-Length', '0')
                if length.isdigit() and int(length) <= 16 * 1024 * 1024:
                    self.rfile.read(int(length))
                self.reply(401)
                return False
            return True

        def do_GET(self):
            if self.allowed():
                self.reply(405)

        def do_DELETE(self):
            if self.allowed():
                self.reply(200)

        def do_POST(self):
            if not self.allowed():
                return
            try:
                length = int(self.headers.get('Content-Length', '-1'))
                if not 0 <= length <= 16 * 1024 * 1024:
                    self.reply(413)
                    return
                self.connection.settimeout(150)
                raw = self.rfile.read(length)
                if len(raw) != length:
                    raise ValueError('incomplete body')
                message = json.loads(raw.decode('utf-8'))
            except (ValueError, UnicodeError):
                self.reply(400)
                return
            try:
                with gate:
                    ensure_worker(worker_state)
                    with connect(worker_state) as conn:
                        conn.send_bytes(json.dumps({'message': message}).encode())
                        result = json.loads(conn.recv_bytes(32 * 1024 * 1024))
                response = result['response']
                self.reply(202 if response is None else 200, response)
            except Exception:
                # A lost response never triggers automatic replay.
                self.reply(200, {'jsonrpc': '2.0', 'id': message.get('id') if isinstance(message, dict) else None,
                                 'error': {'code': -32603, 'message': 'Shared backend unavailable; inspect postconditions before retrying'}})

    class Server(ThreadingHTTPServer):
        allow_reuse_address = False

    server = Server(('127.0.0.1', port), Handler)
    server.daemon_threads = True
    return server


if __name__ == '__main__':
    make_server().serve_forever()
