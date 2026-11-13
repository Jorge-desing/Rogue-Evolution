"""Hermetic contract tests: intermission/navigator diagnostics tools + press-button envelope fix.

No live game: all bridge access is mocked via SERVER._call_http, following the
fixture style of tests/python/test_bepinex_mcp_diagnostics.py.
"""

from __future__ import annotations

import json
import unittest
from unittest import mock

from tests.python.helpers import load_tool_module

SERVER = load_tool_module("bepinex_mcp_server")

FULL_COMPONENT = "RogueCommandExpansionLoader.Runtime.LoaderDiagnosticsComponent"


def _diag_fake_http(calls, payload=None):
    """Fake bridge for component/call_method diagnostics invocations."""

    def fake_http(endpoint, params=None, **_):
        params = params or {}
        assert endpoint == "component/call_method", endpoint
        method = str(params["methodName"])
        args = json.loads(str(params.get("args", "[]")))
        calls.append({"method": method, "args": args, "params": dict(params)})
        body = payload if payload is not None else {"kind": method, "success": True}
        return json.dumps(
            {
                "status": "ok",
                "message": f"Called '{method}' with {len(args)} args. ReturnValue: {json.dumps(body)}",
            }
        )

    return fake_http


class IntermissionDiagnosticsTests(unittest.TestCase):
    def test_intermission_census_passthrough_with_maxrows(self) -> None:
        calls = []

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(
                SERVER,
                "_call_http",
                side_effect=_diag_fake_http(calls, {"kind": "intermissionCensus", "rows": [1, 2]}),
            ),
        ):
            response = SERVER.execute_tool("diagnostics_get_intermission_census", {"maxRows": 16})

        self.assertFalse(response["isError"])
        self.assertEqual([{"method": "GetIntermissionCensusJson", "args": [16]}], [
            {"method": c["method"], "args": c["args"]} for c in calls
        ])
        self.assertEqual(73, calls[0]["params"]["id"])
        self.assertEqual(FULL_COMPONENT, calls[0]["params"]["componentName"])
        payload = json.loads(response["content"][0]["text"])
        self.assertEqual({"kind": "intermissionCensus", "rows": [1, 2]}, payload)

    def test_intermission_census_defaults_to_64_rows(self) -> None:
        calls = []

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            response = SERVER.execute_tool("diagnostics_get_intermission_census", {})

        self.assertFalse(response["isError"])
        self.assertEqual([64], calls[0]["args"])

    def test_enter_level_forwards_int_level_and_field(self) -> None:
        calls = []

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            response = SERVER.execute_tool(
                "diagnostics_order_enter_level", {"level": 3, "field": 1}
            )

        self.assertFalse(response["isError"])
        self.assertEqual(1, len(calls))
        self.assertEqual("OrderEnterLevelNodeJson", calls[0]["method"])
        self.assertEqual([3, 1], calls[0]["args"])
        self.assertEqual(FULL_COMPONENT, calls[0]["params"]["componentName"])

    def test_enter_level_casts_numeric_strings_to_int(self) -> None:
        calls = []

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            response = SERVER.execute_tool(
                "diagnostics_order_enter_level", {"level": "3", "field": "1"}
            )

        self.assertFalse(response["isError"])
        self.assertEqual([3, 1], calls[0]["args"])

    def test_enter_level_rejects_missing_or_non_numeric_level(self) -> None:
        calls = []

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            for arguments in (
                {},
                {"field": 1},
                {"level": "Level3", "field": 1},
                {"level": "abc", "field": 1},
                {"level": "", "field": 1},
                {"level": None, "field": 1},
                {"level": {}, "field": 1},
                {"level": 3},
                {"level": 3, "field": "Alpha"},
                {"level": 3, "field": None},
            ):
                response = SERVER.execute_tool("diagnostics_order_enter_level", arguments)
                self.assertTrue(response["isError"], arguments)

        self.assertEqual([], calls)

    def test_select_level_node_forwards_int_level_and_field(self) -> None:
        calls = []

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            response = SERVER.execute_tool(
                "diagnostics_order_select_level_node", {"level": 2, "field": 0}
            )

        self.assertFalse(response["isError"])
        self.assertEqual(1, len(calls))
        self.assertEqual("OrderSelectLevelNodeJson", calls[0]["method"])
        self.assertEqual([2, 0], calls[0]["args"])
        self.assertEqual(FULL_COMPONENT, calls[0]["params"]["componentName"])

    def test_select_level_node_rejects_missing_or_non_numeric(self) -> None:
        calls = []

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            for arguments in (
                {},
                {"level": 2},
                {"field": 0},
                {"level": "two", "field": 0},
                {"level": 2, "field": "zero"},
                {"level": None, "field": 0},
            ):
                response = SERVER.execute_tool("diagnostics_order_select_level_node", arguments)
                self.assertTrue(response["isError"], arguments)

        self.assertEqual([], calls)

    def test_unknown_tool_returns_is_error(self) -> None:
        response = SERVER.execute_tool("diagnostics_order_select_level_node_typo", {})
        self.assertTrue(response["isError"])

    def test_navigator_state_getter_passthrough(self) -> None:
        calls = []

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(
                SERVER,
                "_call_http",
                side_effect=_diag_fake_http(calls, {"kind": "navigatorState", "paused": False}),
            ),
        ):
            response = SERVER.execute_tool("diagnostics_get_navigator_state", {})

        self.assertFalse(response["isError"])
        self.assertEqual([{"method": "GetNavigatorStateJson", "args": []}], [
            {"method": c["method"], "args": c["args"]} for c in calls
        ])
        payload = json.loads(response["content"][0]["text"])
        self.assertEqual({"kind": "navigatorState", "paused": False}, payload)

    def test_navigator_pause_forwards_boolean(self) -> None:
        calls = []

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            paused_on = SERVER.execute_tool("diagnostics_order_navigator_pause", {"paused": True})
            paused_off = SERVER.execute_tool("diagnostics_order_navigator_pause", {"paused": False})

        self.assertFalse(paused_on["isError"])
        self.assertFalse(paused_off["isError"])
        self.assertEqual(
            [
                {"method": "SetNavigatorPausedJson", "args": [True]},
                {"method": "SetNavigatorPausedJson", "args": [False]},
            ],
            [{"method": c["method"], "args": c["args"]} for c in calls],
        )

    def test_navigator_pause_rejects_missing_or_non_boolean(self) -> None:
        calls = []

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            for arguments in ({}, {"paused": "true"}, {"paused": 1}):
                response = SERVER.execute_tool("diagnostics_order_navigator_pause", arguments)
                self.assertTrue(response["isError"], arguments)

        self.assertEqual([], calls)


class UiPressButtonEnvelopeTests(unittest.TestCase):
    def _press_with_search_payload(self, search_payload, arguments=None):
        calls = []
        click_body = {
            "kind": "uiClickResult",
            "clicked": True,
            "via": "EventSystemRaycast",
            "targetName": "StartButton",
            "topHitName": "StartButton",
            "screenX": 100.0,
            "screenY": 100.0,
            "phases": ["pointerEnter", "pointerDown", "pointerUp", "pointerClick"],
            "message": "clicked StartButton by real coordinates (pointerEnter→pointerDown→pointerUp→pointerClick).",
        }

        def fake_http(endpoint, params=None, method="GET", body_str=None, **_):
            if endpoint == "scene/search_gameobjects":
                return search_payload if isinstance(search_payload, str) else json.dumps(search_payload)
            if endpoint == "component/call_method":
                calls.append({"method": method, "params": dict(params or {}), "body": body_str})
                return json.dumps(
                    {
                        "status": "ok",
                        "message": "Called 'TryUiClickJson' with 2 args. ReturnValue: " + json.dumps(click_body),
                    }
                )
            self.fail(f"Unexpected bridge endpoint: {endpoint}")

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 77),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool(
                "diagnostics_ui_press_button", arguments or {"buttonName": "StartButton"}
            )
        return response, calls

    def test_press_button_accepts_items_envelope(self) -> None:
        response, calls = self._press_with_search_payload(
            {"items": [{"id": 555, "name": "StartButton"}]}
        )
        self.assertFalse(response["isError"])
        payload = json.loads(response["content"][0]["text"])
        self.assertTrue(payload["clicked"])
        self.assertEqual("StartButton", payload["targetName"])
        self.assertEqual(1, len(calls))
        self.assertEqual("TryUiClickJson", calls[0]["params"]["methodName"])
        self.assertEqual(["StartButton", 555], json.loads(calls[0]["params"]["args"]))

    def test_press_button_accepts_objects_envelope_no_regression(self) -> None:
        response, calls = self._press_with_search_payload(
            {"objects": [{"id": 777, "name": "StartButton"}]}
        )
        payload = json.loads(response["content"][0]["text"])
        self.assertTrue(payload["clicked"])
        self.assertEqual(["StartButton", 777], json.loads(calls[0]["params"]["args"]))

    def test_press_button_accepts_results_and_bare_list_envelopes(self) -> None:
        response, calls = self._press_with_search_payload(
            {"results": [{"id": 888, "name": "StartButton"}]}
        )
        payload = json.loads(response["content"][0]["text"])
        self.assertEqual(888, json.loads(calls[0]["params"]["args"])[1])

        response, calls = self._press_with_search_payload([{"id": 999, "name": "StartButton"}])
        payload = json.loads(response["content"][0]["text"])
        self.assertEqual(999, json.loads(calls[0]["params"]["args"])[1])

    def test_press_button_direct_gameobject_id_skips_search(self) -> None:
        searched = []
        calls = []

        def fake_http(endpoint, params=None, method="GET", body_str=None, **_):
            if endpoint == "scene/search_gameobjects":
                searched.append(endpoint)
                return json.dumps({"items": [{"id": 111, "name": "Other"}]})
            if endpoint == "component/call_method":
                calls.append({"method": method, "params": dict(params or {})})
                return json.dumps(
                    {
                        "status": "ok",
                        "message": "Called 'TryUiClickJson' with 2 args. "
                        'ReturnValue: {"kind": "uiClickResult", "clicked": true}',
                    }
                )
            self.fail(f"Unexpected bridge endpoint: {endpoint}")

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 77),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool(
                "diagnostics_ui_press_button", {"gameObjectId": 4242}
            )

        self.assertFalse(response["isError"])
        payload = json.loads(response["content"][0]["text"])
        self.assertTrue(payload["clicked"])
        self.assertEqual([], searched)
        self.assertEqual("TryUiClickJson", calls[0]["params"]["methodName"])
        self.assertEqual(["", 4242], json.loads(calls[0]["params"]["args"]))


class UiClickTests(unittest.TestCase):
    def test_ui_click_point_mode_uses_screen_coordinates(self) -> None:
        calls = []

        def fake_http(endpoint, params=None, **_):
            self.assertEqual("component/call_method", endpoint)
            calls.append({"params": dict(params or {})})
            return json.dumps(
                {
                    "status": "ok",
                    "message": "Called 'TryUiClickPointJson' with 2 args. "
                    'ReturnValue: {"kind": "uiClickResult", "clicked": true, "screenX": 640, "screenY": 360}',
                }
            )

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 77),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool("diagnostics_ui_click", {"x": 640, "y": 360})

        self.assertFalse(response["isError"])
        payload = json.loads(response["content"][0]["text"])
        self.assertTrue(payload["clicked"])
        self.assertEqual("TryUiClickPointJson", calls[0]["params"]["methodName"])
        self.assertEqual([640, 360], json.loads(calls[0]["params"]["args"]))

    def test_ui_click_target_mode_resolves_then_clicks_by_coordinates(self) -> None:
        calls = []

        def fake_http(endpoint, params=None, **_):
            if endpoint == "scene/search_gameobjects":
                return json.dumps({"items": [{"id": 314, "name": "ConfirmButton"}]})
            self.assertEqual("component/call_method", endpoint)
            calls.append({"params": dict(params or {})})
            return json.dumps(
                {
                    "status": "ok",
                    "message": "Called 'TryUiClickJson' with 2 args. "
                    'ReturnValue: {"kind": "uiClickResult", "clicked": true}',
                }
            )

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 77),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool("diagnostics_ui_click", {"buttonName": "ConfirmButton"})

        self.assertFalse(response["isError"])
        self.assertTrue(json.loads(response["content"][0]["text"])["clicked"])
        self.assertEqual("TryUiClickJson", calls[0]["params"]["methodName"])
        self.assertEqual(["ConfirmButton", 314], json.loads(calls[0]["params"]["args"]))

    def test_ui_click_requires_a_target_or_point(self) -> None:
        response = SERVER.execute_tool("diagnostics_ui_click", {})
        self.assertTrue(response["isError"])
        payload = json.loads(response["content"][0]["text"])
        self.assertIn("buttonName", payload["message"])


class JumpToLevelTests(unittest.TestCase):
    """diagnostics_jump_to_level delegates to TryJumpToLevelJson with the
    (level, finished) pair; the in-game result is labeled synthetic."""

    def test_jump_to_level_passes_level_and_finished(self) -> None:
        calls = []

        def fake_http(endpoint, params=None, **_):
            self.assertEqual("component/call_method", endpoint)
            calls.append({"params": dict(params or {})})
            return json.dumps(
                {
                    "status": "ok",
                    "message": "Called 'TryJumpToLevelJson' with 2 args. "
                    'ReturnValue: {"kind": "syntheticJumpResult", "success": true, '
                    '"synthetic": true, "method": "JumpTo(bool,int)", "level": 4, "finished": false}',
                }
            )

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 77),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool(
                "diagnostics_jump_to_level", {"level": 4}
            )

        self.assertFalse(response["isError"])
        payload = json.loads(response["content"][0]["text"])
        self.assertTrue(payload["success"])
        self.assertTrue(payload["synthetic"])
        self.assertEqual("syntheticJumpResult", payload["kind"])
        self.assertEqual("JumpTo(bool,int)", payload["method"])
        self.assertEqual(4, payload["level"])
        self.assertFalse(payload["finished"])
        self.assertEqual("TryJumpToLevelJson", calls[0]["params"]["methodName"])
        self.assertEqual([4, False], json.loads(calls[0]["params"]["args"]))

    def test_jump_to_level_finished_passthrough(self) -> None:
        calls = []

        def fake_http(endpoint, params=None, **_):
            self.assertEqual("component/call_method", endpoint)
            calls.append({"params": dict(params or {})})
            return json.dumps(
                {
                    "status": "ok",
                    "message": "Called 'TryJumpToLevelJson' with 2 args. "
                    'ReturnValue: {"kind": "syntheticJumpResult", "success": true, '
                    '"synthetic": true, "level": 3, "finished": true}',
                }
            )

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 77),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool(
                "diagnostics_jump_to_level", {"level": 3, "finished": True}
            )

        self.assertFalse(response["isError"])
        self.assertEqual([3, True], json.loads(calls[0]["params"]["args"]))

    def test_jump_to_level_requires_integer_level(self) -> None:
        response = SERVER.execute_tool("diagnostics_jump_to_level", {})
        self.assertTrue(response["isError"])
        payload = json.loads(response["content"][0]["text"])
        self.assertIn("level", payload["error"])

    def test_load_next_level_delegates_with_no_args(self) -> None:
        calls = []

        def fake_http(endpoint, params=None, **_):
            self.assertEqual("component/call_method", endpoint)
            calls.append({"params": dict(params or {})})
            return json.dumps(
                {
                    "status": "ok",
                    "message": "Called 'TryLoadNextLevelJson' with 0 args. "
                    'ReturnValue: {"kind": "syntheticJumpResult", "success": true, '
                    '"synthetic": true, "method": "LoadScene.LoadNextLevel", "level": -1}',
                }
            )

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 77),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool("diagnostics_load_next_level", {})

        self.assertFalse(response["isError"])
        payload = json.loads(response["content"][0]["text"])
        self.assertTrue(payload["success"])
        self.assertTrue(payload["synthetic"])
        self.assertEqual("LoadScene.LoadNextLevel", payload["method"])
        self.assertEqual("TryLoadNextLevelJson", calls[0]["params"]["methodName"])
        self.assertEqual([], json.loads(calls[0]["params"]["args"]))

    def test_level_map_state_and_node_inspect_passthrough(self) -> None:
        calls = []
        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 77),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            r1 = SERVER.execute_tool("diagnostics_get_level_map_state", {})
            r2 = SERVER.execute_tool("diagnostics_get_intermission_node", {"level": 2, "field": 1})
            r3 = SERVER.execute_tool("diagnostics_get_intermission_node", {"level": "bad", "field": 1})

        self.assertFalse(r1["isError"])
        self.assertFalse(r2["isError"])
        self.assertTrue(r3["isError"])
        self.assertEqual(
            [
                {"method": "GetLevelMapStateJson", "args": []},
                {"method": "GetIntermissionNodeJson", "args": [2, 1]},
            ],
            [{"method": c["method"], "args": c["args"]} for c in calls],
        )

    def test_intermission_graph_and_reward_state_passthrough(self) -> None:
        calls = []
        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 77),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            r_graph_default = SERVER.execute_tool("diagnostics_get_intermission_graph", {})
            r_graph_custom = SERVER.execute_tool(
                "diagnostics_get_intermission_graph",
                {"preferredRewardType": "RelicReward", "maxRoutes": 4, "maxNodes": 20},
            )
            r_reward_default = SERVER.execute_tool("diagnostics_get_intermission_reward_state", {})
            r_reward_custom = SERVER.execute_tool("diagnostics_get_intermission_reward_state", {"maxCards": 12})

        self.assertFalse(r_graph_default["isError"])
        self.assertFalse(r_graph_custom["isError"])
        self.assertFalse(r_reward_default["isError"])
        self.assertFalse(r_reward_custom["isError"])
        self.assertEqual(
            [
                {"method": "GetIntermissionGraphJson", "args": ["", 8, 64]},
                {"method": "GetIntermissionGraphJson", "args": ["RelicReward", 4, 20]},
                {"method": "GetIntermissionRewardStateJson", "args": [32]},
                {"method": "GetIntermissionRewardStateJson", "args": [12]},
            ],
            [{"method": c["method"], "args": c["args"]} for c in calls],
        )

    def test_native_shop_and_removal_panel_tools(self) -> None:
        calls = []
        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 77),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            r_state = SERVER.execute_tool("diagnostics_get_intermission_native_panel_state", {"maxRows": 12})
            r_state_bad = SERVER.execute_tool("diagnostics_get_intermission_native_panel_state", {"maxRows": "bad"})
            r_buy = SERVER.execute_tool("diagnostics_order_buy_intermission_shop_offer", {"offerId": "RelicCard:123"})
            r_buy_bad = SERVER.execute_tool("diagnostics_order_buy_intermission_shop_offer", {"offerId": " "})
            r_reroll = SERVER.execute_tool("diagnostics_order_reroll_intermission_shop", {})
            r_remove = SERVER.execute_tool("diagnostics_order_remove_intermission_card", {"entityId": "Factory_Tank"})
            r_remove_bad = SERVER.execute_tool("diagnostics_order_remove_intermission_card", {"entityId": ""})
            r_cancel = SERVER.execute_tool("diagnostics_order_cancel_intermission_card_removal", {})

        self.assertFalse(r_state["isError"])
        self.assertTrue(r_state_bad["isError"])
        self.assertFalse(r_buy["isError"])
        self.assertTrue(r_buy_bad["isError"])
        self.assertFalse(r_reroll["isError"])
        self.assertFalse(r_remove["isError"])
        self.assertTrue(r_remove_bad["isError"])
        self.assertFalse(r_cancel["isError"])
        self.assertEqual(
            [
                {"method": "GetIntermissionNativePanelStateJson", "args": [12]},
                {"method": "OrderBuyIntermissionShopOfferJson", "args": ["RelicCard:123"]},
                {"method": "OrderRerollIntermissionShopJson", "args": []},
                {"method": "OrderRemoveIntermissionCardJson", "args": ["Factory_Tank"]},
                {"method": "OrderCancelIntermissionCardRemovalJson", "args": []},
            ],
            [{"method": c["method"], "args": c["args"]} for c in calls],
        )

    def test_intermission_action_tools_passthrough_and_validation(self) -> None:
        calls = []
        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 77),
            mock.patch.object(SERVER, "_call_http", side_effect=_diag_fake_http(calls)),
        ):
            r_load = SERVER.execute_tool("diagnostics_order_trigger_load_level", {})
            r_choose = SERVER.execute_tool(
                "diagnostics_order_choose_intermission_reward",
                {"rewardId": "Factory_Tank", "slotIndex": 1},
            )
            r_choose_bad = SERVER.execute_tool(
                "diagnostics_order_choose_intermission_reward",
                {"slotIndex": "bad"},
            )
            r_skip = SERVER.execute_tool("diagnostics_order_skip_intermission_reward", {})
            r_reroll = SERVER.execute_tool("diagnostics_order_reroll_intermission_reward", {})
            r_upg = SERVER.execute_tool(
                "diagnostics_order_assign_intermission_upgrade",
                {"entityId": "Unit_Tank", "cancel": False},
            )
            r_deck_open = SERVER.execute_tool("diagnostics_order_set_deck_overview_open", {"open": True})
            r_deck_bad = SERVER.execute_tool("diagnostics_order_set_deck_overview_open", {"open": "yes"})

        self.assertFalse(r_load["isError"])
        self.assertFalse(r_choose["isError"])
        self.assertTrue(r_choose_bad["isError"])
        self.assertFalse(r_skip["isError"])
        self.assertFalse(r_reroll["isError"])
        self.assertFalse(r_upg["isError"])
        self.assertFalse(r_deck_open["isError"])
        self.assertTrue(r_deck_bad["isError"])
        self.assertEqual(
            [
                {"method": "OrderTriggerLoadLevelJson", "args": []},
                {"method": "OrderChooseIntermissionRewardJson", "args": ["Factory_Tank", 1]},
                {"method": "OrderSkipIntermissionRewardJson", "args": []},
                {"method": "OrderRerollIntermissionRewardJson", "args": []},
                {"method": "OrderAssignIntermissionUpgradeJson", "args": ["Unit_Tank", False]},
                {"method": "OrderSetDeckOverviewOpenJson", "args": [True]},
            ],
            [{"method": c["method"], "args": c["args"]} for c in calls],
        )


if __name__ == "__main__":
    unittest.main()
