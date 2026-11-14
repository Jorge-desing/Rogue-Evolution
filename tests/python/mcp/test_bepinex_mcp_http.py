import json
import threading
import unittest
import urllib.request
import urllib.error
import http.client
from unittest import mock
from tools import bepinex_mcp_http as endpoint


class HttpTests(unittest.TestCase):
    def test_overload_does_not_spawn_unbounded_request_threads(self):
        server = endpoint.make_server(0, 'test-token')
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            for _ in range(32):
                self.assertTrue(server.slots.acquire(blocking=False))
            client = http.client.HTTPConnection('127.0.0.1', server.server_port, timeout=3)
            with self.assertRaises((ConnectionError, OSError, http.client.RemoteDisconnected)):
                client.request('POST', '/mcp', body=b'{}')
                client.getresponse()
            client.close()
            for _ in range(32):
                server.slots.release()
        finally:
            server.shutdown()
            server.server_close()
            thread.join()

    def test_stateless_requests_close_and_authentication_is_required(self):
        with mock.patch.object(endpoint, 'ensure_worker'), mock.patch.object(endpoint, 'connect') as connect:
            conn = connect.return_value.__enter__.return_value
            conn.recv_bytes.return_value = b'{"response":{"jsonrpc":"2.0","id":1,"result":{}}}'
            server = endpoint.make_server(0, 'test-token')
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            url = f'http://127.0.0.1:{server.server_port}/mcp'
            try:
                request = urllib.request.Request(url, data=b'{}')
                with self.assertRaises(urllib.error.HTTPError) as denied:
                    urllib.request.urlopen(request)
                self.assertEqual(denied.exception.code, 401)
                client = http.client.HTTPConnection('127.0.0.1', server.server_port, timeout=5)
                client.request('POST', '/mcp', body=b'{}', headers={'Authorization': 'Bearer caf\u00e9'})
                self.assertEqual(client.getresponse().status, 401)
                client.close()
                for _ in range(3):
                    request = urllib.request.Request(url, data=b'{"jsonrpc":"2.0","id":1,"method":"ping"}',
                                                     headers={'Authorization': 'Bearer test-token'})
                    with urllib.request.urlopen(request) as response:
                        self.assertEqual(json.load(response)['id'], 1)
                        self.assertEqual(response.headers['Connection'], 'close')
                        self.assertIsNone(response.headers.get('Mcp-Session-Id'))
                self.assertEqual(conn.send_bytes.call_count, 3)
            finally:
                server.shutdown()
                server.server_close()
                thread.join()
