import io
import json
import unittest
from unittest import mock
from email.message import Message
from tests.python.helpers import load_tool_module

S = load_tool_module('bepinex_mcp_server')


class BoundaryTests(unittest.TestCase):
    def test_http_response_boundaries(self):
        for body, length, failed in [(b'abcd', '4', False), (b'abcde', None, True),
                                     (b'a', '3', True), (b'\xff', '1', True)]:
            response = io.BytesIO(body)
            response.headers = Message()
            if length is not None:
                response.headers['Content-Length'] = length
            with self.subTest(body=body), mock.patch.object(S, 'MAX_HTTP_RESPONSE_BYTES', 4), \
                    mock.patch.object(S.urllib.request, 'urlopen', return_value=response):
                result = S._call_http('system/ping')
                if failed:
                    self.assertEqual(json.loads(result)['completionStatus'], 'unknown')
                else:
                    self.assertEqual(result, 'abcd')

    def test_expired_budget_never_sends(self):
        token = S._AUTOMATION_DEADLINE.set(0)
        try:
            with mock.patch.object(S.urllib.request, 'urlopen') as send:
                self.assertEqual(json.loads(S._call_http('x'))['completionStatus'], 'not_started')
                send.assert_not_called()
        finally:
            S._AUTOMATION_DEADLINE.reset(token)

    def test_transport_error_without_sequence_is_unknown(self):
        with mock.patch.object(S.urllib.request, 'urlopen', side_effect=S.urllib.error.URLError('offline')):
            self.assertEqual(json.loads(S._call_http('x'))['completionStatus'], 'unknown')

    def test_invalid_arguments_never_reach_game(self):
        for name, args in [('system_ping', {'extra': True}),
                           ('diagnostics_ui_set_value', {'hierarchyPath': 'x', 'gameObjectId': 1, 'kind': 'text', 'value': []}),
                           ('diagnostics_wait_for_renderer_frame_evidence', {}),
                           ('diagnostics_spawn_entity', {'entityId': 'x', 'side': 'bad'}),
                           ('diagnostics_wait_for_renderer_frame_evidence', {'entityKeyId': 'x', 'timeoutSeconds': 21}),
                           ('diagnostics_wait_for_renderer_frame_evidence', {'entityKeyId': 'x', 'requiredEvidence': ['moving', 'moving']})]:
            with self.subTest(name=name), mock.patch.object(S, '_call_http') as send:
                response = S.execute_tool(name, args)
                self.assertTrue(response['isError'])
                self.assertIn('argumentPath', json.loads(response['content'][0]['text']))
                send.assert_not_called()

    def test_sequence_static_and_resolved_validation(self):
        with mock.patch.object(S, '_call_diagnostics_method', return_value='{"value":true}') as send:
            response = S.execute_tool('automation_run_sequence', {'steps': [
                {'tool': 'diagnostics_get_game_time_scale'},
                {'tool': 'diagnostics_get_game_time_scale', 'arguments': {'bad': 1}}]})
            self.assertTrue(response['isError'])
            send.assert_not_called()
            response = S.execute_tool('automation_run_sequence', {'steps': [
                {'tool': 'diagnostics_get_game_time_scale'},
                {'tool': 'diagnostics_wait_for_renderer_frame_evidence', 'arguments': {
                    'entityKeyId': {'$ref': {'step': 0, 'path': '/value'}}}}]})
            self.assertTrue(response['isError'])
            self.assertEqual(send.call_count, 1)

    def test_static_validation_covers_array_and_numeric_limits_without_coercion(self):
        for value in (True, '2', 1.5, [], [1], [1, 1]):
            schema = {'type': 'array', 'minItems': 2, 'maxItems': 3, 'uniqueItems': True,
                      'items': {'type': 'integer', 'minimum': 2, 'maximum': 4}}
            spec = {'name': 'fixture', 'inputSchema': schema}
            with self.subTest(value=value), mock.patch.object(S, 'TOOLS', [spec]), mock.patch.object(S, '_VALIDATORS', {}):
                self.assertIsNotNone(S._validate_tool_arguments('fixture', value))
                self.assertIsNone(S._validate_tool_arguments('fixture', [2, 3]))

    def test_missing_dependency_fails_closed(self):
        with mock.patch.object(S, 'Draft202012Validator', None), mock.patch.object(S, '_call_http') as send:
            self.assertTrue(S.execute_tool('system_ping', {})['isError'])
            send.assert_not_called()
