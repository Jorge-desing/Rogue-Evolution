"""Bounded read-only automation, including executable skill recipes."""
import json
import re
import unittest
from pathlib import Path
from unittest import mock

from tests.python.helpers import load_tool_module

SERVER = load_tool_module("bepinex_mcp_server")
QUERY = "diagnostics_get_game_time_scale"


class Clock:
    def __init__(self):
        self.now = 0.0

    def monotonic(self):
        return self.now

    def sleep(self, seconds):
        self.now += seconds


class StateWaitTests(unittest.TestCase):
    def setUp(self):
        self.clock = Clock()
        self.enterContext(mock.patch.object(SERVER.time, "monotonic", self.clock.monotonic))
        self.enterContext(mock.patch.object(SERVER.time, "sleep", self.clock.sleep))

    def wait(self, **overrides):
        args = {"tool": QUERY, "expect": [{"path": "/ready", "equals": True}], "timeoutSeconds": 0.5}
        args.update(overrides)
        response = SERVER.execute_tool("automation_wait_for_state", args)
        return response, json.loads(response["content"][0]["text"])

    def test_immediate_and_delayed_match(self):
        for rows in ([{"ready": True}], [{}, {"ready": 1}, {"ready": True}]):
            with self.subTest(rows=rows), mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=[json.dumps(row) for row in rows]) as call:
                self.clock.now = 0
                response, payload = self.wait(timeoutSeconds=1)
                self.assertFalse(response.get("isError", False))
                self.assertEqual(payload["attempts"], len(rows))
                self.assertEqual(call.call_count, len(rows))
                self.assertEqual(payload["reason"], "matched")
                self.assertEqual(payload["lastObservation"], {"ready": True})

    def test_stable_samples_reset_after_transient_match(self):
        rows = [True, False, True, True]
        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=[json.dumps({"ready": value}) for value in rows]):
            response, payload = self.wait(stableSamples=2, timeoutSeconds=2)
            self.assertFalse(response.get("isError", False))
            self.assertEqual(4, payload["attempts"])
            self.assertEqual(2, payload["consecutiveMatches"])
        self.clock.now = 0
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value='{"ready":true}'):
            response, payload = self.wait(stableSamples=3)
            self.assertTrue(response["isError"])
            self.assertEqual("timeout", payload["reason"])
            self.assertEqual(2, payload["consecutiveMatches"])

    def test_reward_wait_forwards_bounds_and_requires_stable_queue_state(self):
        rows = [{"queue": {"remaining": 1}}, {"queue": {"remaining": 0}}, {"queue": {"remaining": 0}}]
        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=[json.dumps(row) for row in rows]) as call:
            response, payload = self.wait(tool="diagnostics_get_intermission_reward_state", arguments={"maxCards": 16}, expect=[{"path": "/queue/remaining", "equals": 0}], stableSamples=2, timeoutSeconds=2)
            self.assertFalse(response.get("isError", False))
            self.assertEqual(3, payload["attempts"])
            self.assertEqual([mock.call("GetIntermissionRewardStateJson", [16])] * 3, call.call_args_list)
        with mock.patch.object(SERVER, "_call_diagnostics_method") as call:
            self.assertTrue(self.wait(tool="diagnostics_get_intermission_reward_state", arguments={"maxCards": "16"})[0]["isError"])
            call.assert_not_called()

    def test_intermission_preconditions_and_dry_run_never_replay_actions(self):
        for action, args, getter, native in [
            ("diagnostics_order_choose_intermission_reward", {"rewardId": "test:card"}, "GetIntermissionRewardStateJson", "OrderChooseIntermissionRewardJson"),
            ("diagnostics_order_buy_intermission_shop_offer", {"offerId": "offer-1"}, "GetIntermissionNativePanelStateJson", "OrderBuyIntermissionShopOfferJson"),
        ]:
            for dry, value, expected_calls in [(True, True, 1), (False, False, 1), (False, True, 2)]:
                with self.subTest(action=action, dry=dry, value=value), mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=['{"ready":true}', '{"success":true}']) as call:
                    response = SERVER.execute_tool(action, dict(args, dryRun=dry, preconditions={"/ready": value}))
                    self.assertEqual(expected_calls, call.call_count)
                    self.assertEqual(getter, call.call_args_list[0].args[0])
                    if expected_calls == 2:
                        self.assertEqual(native, call.call_args_list[1].args[0])
                    self.assertEqual(not value, bool(response.get("isError")))
            for state in ('{"error":"failed"}', '[]', '{"ready":NaN}'):
                with mock.patch.object(SERVER, "_call_diagnostics_method", return_value=state) as call:
                    self.assertTrue(SERVER.execute_tool(action, dict(args, dryRun=True))["isError"])
                    self.assertEqual(1, call.call_count)
            with mock.patch.object(SERVER, "_call_diagnostics_method") as call:
                self.assertTrue(SERVER.execute_tool(action, dict(args, preconditions={"bad": True}))["isError"])
                call.assert_not_called()

    def test_failed_intermission_guard_stops_sequence_with_continuation(self):
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value='{"ready":false}') as call:
            response = SERVER.execute_tool("automation_run_sequence", {"stopOnFailure": False, "steps": [
                {"tool": "diagnostics_order_buy_intermission_shop_offer", "arguments": {"offerId": "offer-1", "preconditions": {"/ready": True}}},
                {"tool": "diagnostics_get_navigator_state"}]})
            self.assertTrue(response["isError"])
            self.assertEqual(1, call.call_count)

    def test_native_shop_exit_dispatches_once_and_rejects_invalid_ids(self):
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value='{"success":false,"nativeCallInvoked":true,"effectObserved":false}') as call:
            response = SERVER.execute_tool("diagnostics_order_leave_intermission_shop", {"gameObjectId": 42})
            call.assert_called_once_with("OrderLeaveIntermissionShopJson", [42])
            self.assertFalse(json.loads(response["content"][0]["text"])["effectObserved"])
        with mock.patch.object(SERVER, "_call_diagnostics_method") as call:
            for args in ({}, {"gameObjectId": True}, {"gameObjectId": "42"}):
                self.assertTrue(SERVER.execute_tool("diagnostics_order_leave_intermission_shop", args)["isError"])
            call.assert_not_called()

    def test_shop_exit_wait_requires_stable_map_without_redispatch(self):
        tool = "diagnostics_order_leave_intermission_shop"
        invoked = '{"success":false,"nativeCallInvoked":true,"effectObserved":false}'
        ready = '{"isIntermissionActive":true,"nativePanelState":{"isShopActive":false}}'
        pending = '{"isIntermissionActive":false,"nativePanelState":{"isShopActive":true}}'
        for samples, timeout, expected in [([ready, pending, ready, ready], 2, True), ([pending, ready], 0.5, False)]:
            self.clock.now = 0
            with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=[invoked, *samples]) as call:
                result = SERVER.execute_tool(tool, {"gameObjectId": 42, "waitSeconds": timeout})
            payload = json.loads(result["content"][0]["text"])
            self.assertIs(payload["effectObserved"], expected)
            self.assertTrue(payload["nativeCallInvoked"])
            self.assertEqual(mock.call("OrderLeaveIntermissionShopJson", [42]), call.call_args_list[0])
            self.assertEqual(["GetRuntimeControlSurfaceJson"] * len(samples), [entry.args[0] for entry in call.call_args_list[1:]])

    def test_missing_and_type_mismatch_timeout(self):
        for row in ({}, {"ready": 1}, {"ready": "true"}):
            with self.subTest(row=row), mock.patch.object(SERVER, "_call_diagnostics_method", return_value=json.dumps(row)):
                self.clock.now = 0
                response, payload = self.wait()
                self.assertTrue(response["isError"])
                self.assertEqual(payload["reason"], "timeout")
                self.assertEqual(payload["attempts"], 2)
                self.assertEqual(self.clock.now, 0.5)

    def test_invalid_target_arguments_and_limits_do_not_call_bridge(self):
        invalid = [{"stableSamples": 0}, {"stableSamples": 9}, {"stableSamples": True}, {"tool": "diagnostics_ui_set_value"}, {"arguments": {"typo": 1}},
                   {"timeoutSeconds": 0}, {"timeoutSeconds": 31}, {"pollIntervalMs": 99},
                   {"pollIntervalMs": 2001}, {"pollIntervalMs": True}, {"expect": []},
                   {"timeoutSeconds": float("nan")}, {"timeoutSeconds": float("inf")},
                   {"expect": [{"path": "/bad~2", "equals": True}]}]
        with mock.patch.object(SERVER, "_call_diagnostics_method") as call:
            for args in invalid:
                with self.subTest(args=args):
                    self.assertTrue(self.wait(**args)[0]["isError"])
            call.assert_not_called()

    def test_bad_reply_and_transport_stop_after_one_read(self):
        for raw in ('garbage', '[]', '{"ready":NaN}', '{"error":"timeout","completionStatus":"unknown"}', '{"success":false}'):
            with self.subTest(raw=raw), mock.patch.object(SERVER, "_call_diagnostics_method", return_value=raw) as call:
                self.assertTrue(self.wait()[0]["isError"])
                self.assertEqual(call.call_count, 1)

    def test_failed_queried_section_cannot_match_but_unrelated_error_is_allowed(self):
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value='{"ui":{"error":"failed","ready":true},"ready":true}'):
            self.assertTrue(self.wait(expect=[{"path": "/ui/ready", "equals": True}])[0]["isError"])
            self.assertFalse(self.wait()[0].get("isError", False))

    def test_pointer_escaping_lists_and_null(self):
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value='{"a/b":[{"~key":null}]}'):
            self.assertEqual(self.wait(expect=[{"path": "/a~1b/0/~0key", "equals": None}])[1]["reason"], "matched")

    def test_selected_failed_subtree_cannot_equal_error_object(self):
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value='{"ui":{"nested":{"error":"failed"}}}'):
            self.assertTrue(self.wait(expect=[{"path": "/ui", "equals": {"nested": {"error": "failed"}}}])[0]["isError"])

    def test_allowlist_matches_schema_and_canonical_catalog_stays_bounded(self):
        spec = next(tool for tool in SERVER.TOOLS if tool["name"] == "automation_wait_for_state")
        self.assertEqual(set(spec["inputSchema"]["properties"]["tool"]["enum"]), SERVER.WAIT_STATE_TOOLS)
        canonical = [tool for tool in SERVER.TOOLS if not tool["description"].startswith("Alias for")]
        self.assertEqual(len(canonical), 231)
        self.assertLess(len(json.dumps({"tools": canonical}, separators=(",", ":")).encode("utf-8")), 91000)

    def test_local_wait_timeout_stops_sequence_before_outer_budget(self):
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value='{}') as call:
            response = SERVER.execute_tool("automation_run_sequence", {"stopOnFailure": False, "steps": [
                {"tool": "automation_wait_for_state", "arguments": {"tool": QUERY, "expect": [{"path": "/ready", "equals": True}], "timeoutSeconds": 0.1}},
                {"tool": "diagnostics_get_navigator_state"}]})
            payload = json.loads(response["content"][0]["text"])
            self.assertEqual(payload["stopReason"], {"code": "wait_failed", "step": 0})
            self.assertEqual(call.call_count, 1)

    def test_late_success_does_not_escape_deadline_and_context_restores(self):
        def late(*args):
            self.assertEqual(SERVER._AUTOMATION_DEADLINE.get(), 0.5)
            self.clock.now = 0.6
            return '{"ready":true}'
        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=late):
            self.assertEqual(self.wait()[1]["reason"], "timeout")
        self.assertIsNone(SERVER._AUTOMATION_DEADLINE.get())

    def test_sequence_budget_stops_wait_and_next_action_even_with_continuation(self):
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value='{"ready":false}') as call:
            response = SERVER.execute_tool("automation_run_sequence", {"maxDurationSeconds": 1, "stopOnFailure": False, "steps": [
                {"tool": "automation_wait_for_state", "arguments": {"tool": QUERY, "expect": [{"path": "/ready", "equals": True}]}},
                {"tool": "diagnostics_get_navigator_state"}]})
            payload = json.loads(response["content"][0]["text"])
            self.assertTrue(response["isError"])
            self.assertEqual(payload["completedSteps"], 1)
            self.assertEqual(payload["results"][0]["result"]["reason"], "sequence_budget_exhausted")
            self.assertEqual(payload["stopReason"]["code"], "sequence_budget_exhausted")
            self.assertEqual(call.call_count, 4)

    def test_sequence_prevalidates_wait_target_before_any_call(self):
        with mock.patch.object(SERVER, "_call_diagnostics_method") as call:
            response = SERVER.execute_tool("automation_run_sequence", {"steps": [
                {"tool": QUERY}, {"tool": "automation_wait_for_state", "arguments": {"tool": "diagnostics_ui_click", "expect": [{"path": "/success", "equals": True}]}}]})
            self.assertTrue(response["isError"])
            call.assert_not_called()

    def test_skill_examples_validate_and_slider_recipe_runs_with_typed_refs(self):
        doc = Path(__file__).resolve().parents[2] / ".agents/skills/rogue-command-bepinex-mcp/references/automation.md"
        examples = [json.loads(block) for block in re.findall(r"```json\n(.*?)\n```", doc.read_text(encoding="utf-8"), re.S)]
        self.assertEqual(len(examples), 3)
        for example in examples:
            self.assertIsNone(SERVER._validate_tool_arguments(example["tool"], example["arguments"]))
        candidate = {"hierarchyPath": "MainMenu::/Canvas/Options/Slider", "gameObjectId": 23, "value": 0.25}
        census = {"success": True, "totalCandidateCount": 1, "candidates": [candidate]}
        def bridge(method, args=None):
            if method == "GetUiAccessibilityCensusJson":
                return json.dumps(census)
            self.assertEqual(method, "SetUiValueJson")
            self.assertIs(type(args[1]), int)
            self.assertEqual(args[1], 23)
            candidate["value"] = 0.5
            return '{"success":true,"nativeCallInvoked":true,"effectObserved":true}'
        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=bridge):
            result = SERVER.execute_tool(examples[1]["tool"], examples[1]["arguments"])
            payload = json.loads(result["content"][0]["text"])
            self.assertTrue(payload["success"], payload)
            self.assertIsNone(payload["stopReason"])
