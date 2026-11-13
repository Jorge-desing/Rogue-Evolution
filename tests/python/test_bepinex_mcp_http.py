import json
import threading
import unittest
import urllib.request
import urllib.error
from unittest import mock
from tools import bepinex_mcp_http as endpoint


class HttpTests(unittest.TestCase):
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
