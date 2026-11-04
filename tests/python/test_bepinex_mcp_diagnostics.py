"""Regression tests for the BepInEx runtime diagnostics bridge."""

from __future__ import annotations

import json
import unittest
from unittest import mock

from tests.python.helpers import load_tool_module

SERVER = load_tool_module("bepinex_mcp_server")


class RuntimeDiagnosticsTests(unittest.TestCase):
    def test_runtime_scalars_read_properties_and_return_typed_values(self) -> None:
        property_values = {
            "LiveBattlefieldEntityCount": ("System.Int32", "13"),
            "LivePlayerUnitCount": ("System.Int32", "4"),
            "LiveAiUnitCount": ("System.Int32", "3"),
            "LiveAiBuildingCount": ("System.Int32", "5"),
            "LiveHarvesterCount": ("System.Int32", "1"),
            "RegisteredUnitMechanicsProfileCount": ("System.Int32", "9"),
            "RangeDisplayGeometryMinimumSegments": ("System.Int32", "8"),
            "RangeDisplayGeometryMaximumSegments": ("System.Int32", "256"),
            "RegisteredAgentSessionCount": ("System.Int32", "2"),
            "ActiveAgentLockCount": ("System.Int32", "1"),
            "ActiveAgentMessageCount": ("System.Int32", "3"),
            "ActiveHazardZoneCount": ("System.Int32", "0"),
            "ActiveCommandAuraCount": ("System.Int32", "0"),
            "TrackedVeterancyUnitCount": ("System.Int32", "5"),
            "LiveSkinnedMeshUnitCount": ("System.Int32", "2"),
            "LiveAnimatedUnitCount": ("System.Int32", "2"),
            "LivePbrShaderUnitCount": ("System.Int32", "3"),
            "AuditedUnitPrefabCount": ("System.Int32", "6"),
            "LiveMechanicsDirectorCount": ("System.Int32", "4"),
            "LiveVisualDirectorCount": ("System.Int32", "4"),
            "LiveJammedTurretCount": ("System.Int32", "0"),
            "LiveRegeneratingShieldCount": ("System.Int32", "1"),
        }
        member_reads: list[str] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            params = params or {}
            if endpoint == "component/get_member":
                name = str(params["memberName"])
                member_reads.append(name)
                clr_type, value = property_values[name]
                return json.dumps(
                    {
                        "name": name,
                        "type": clr_type,
                        "value": value,
                        "writable": False,
                        "kind": "property",
                    }
                )
            if endpoint == "component/call_method":
                return (
                    '{"status":"ok", "message":"Called '
                    "'GetRangeCirclePointCount' with 1 args. ReturnValue: 9"
                    '"}'
                )
            self.fail(f"Unexpected bridge endpoint: {endpoint}")

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool("diagnostics_runtime_scalars", {})

        scalars = json.loads(response["content"][0]["text"])
        self.assertFalse(response["isError"])
        self.assertEqual(
            {
                "LiveBattlefieldEntityCount": 13,
                "LivePlayerUnitCount": 4,
                "LiveAiUnitCount": 3,
                "LiveAiBuildingCount": 5,
                "LiveHarvesterCount": 1,
                "RegisteredUnitMechanicsProfileCount": 9,
                "RangeDisplayGeometryMinimumSegments": 8,
                "RangeDisplayGeometryMaximumSegments": 256,
                "RegisteredAgentSessionCount": 2,
                "ActiveAgentLockCount": 1,
                "ActiveAgentMessageCount": 3,
                "ActiveHazardZoneCount": 0,
                "ActiveCommandAuraCount": 0,
                "TrackedVeterancyUnitCount": 5,
                "LiveSkinnedMeshUnitCount": 2,
                "LiveAnimatedUnitCount": 2,
                "LivePbrShaderUnitCount": 3,
                "AuditedUnitPrefabCount": 6,
                "LiveMechanicsDirectorCount": 4,
                "LiveVisualDirectorCount": 4,
                "LiveJammedTurretCount": 0,
                "LiveRegeneratingShieldCount": 1,
                "RangeDisplayCirclePointCount": 9,
            },
            scalars,
        )
        self.assertCountEqual(property_values, member_reads)

    def test_runtime_member_refreshes_component_id_after_stale_object_error(self) -> None:
        calls: list[tuple[str, dict[str, object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            params = params or {}
            calls.append((endpoint, dict(params)))
            if endpoint == "component/get_member" and params["id"] == 12:
                return json.dumps({"error": "GameObject with ID 12 not found"})
            if endpoint == "scene:find_objects_with_component":
                return json.dumps([{"name": "LoaderDiagnostics", "id": 73}])
            if endpoint == "component/get_member" and params["id"] == 73:
                return json.dumps({"name": "LivePlayerUnitCount", "type": "System.Int32", "value": "4"})
            self.fail(f"Unexpected bridge request: {endpoint} {params}")

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 12),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            value = SERVER._call_diagnostics_member("LivePlayerUnitCount")
            self.assertEqual(73, SERVER._DIAGNOSTICS_GO_ID)

        self.assertEqual(4, value)
        self.assertEqual(["component/get_member", "scene:find_objects_with_component", "component/get_member"], [call[0] for call in calls])

    def test_diagnostics_discovery_falls_back_to_search_gameobjects(self) -> None:
        calls: list[str] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            calls.append(endpoint)
            if endpoint == "scene:find_objects_with_component":
                return json.dumps([])
            if endpoint == "scene/search_gameobjects" and params and params.get("componentName") == "LoaderDiagnosticsComponent":
                return json.dumps([{"name": "LoaderDiagnostics", "id": 88}])
            self.fail(f"Unexpected bridge call: {endpoint} {params}")

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", None),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resolved_id = SERVER._get_diagnostics_go_id()
            self.assertEqual(88, resolved_id)
            self.assertEqual(88, SERVER._DIAGNOSTICS_GO_ID)

        self.assertEqual(["scene:find_objects_with_component", "scene/search_gameobjects"], calls)

    def test_bridge_return_parser_handles_escaped_strings_inside_json(self) -> None:
        payload = json.dumps({"text": 'quote: "quoted" } path C:\\folder'})
        raw = (
            '{"status":"ok", "message":"Called \'GetValueJson\' with 0 args. ReturnValue: '
            f'{payload}"}}'
        )
        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", return_value=raw),
        ):
            value = SERVER._call_diagnostics_method("GetValueJson")

        self.assertEqual(json.dumps(json.loads(payload), indent=2), value)

    def test_runtime_snapshot_decodes_bridge_return_values(self) -> None:
        expected = {"kind": "systemHealth", "activePackCount": 3}

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            self.assertEqual(endpoint, "component/call_method")
            params = params or {}
            getter = str(params["methodName"])
            payload = json.dumps(expected, separators=(",", ":"))
            return (
                '{"status":"ok", "message":"Called '
                f"'{getter}' with 0 args. ReturnValue: {payload}"
                '"}'
            )

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool("diagnostics_runtime_snapshot", {})

        snapshot = json.loads(response["content"][0]["text"])
        self.assertFalse(response["isError"])
        self.assertEqual(40, len(snapshot))
        getter_values = {k: v for k, v in snapshot.items() if k != "_meta"}
        self.assertTrue(all(isinstance(value, dict) for value in getter_values.values()))
        self.assertTrue(all(value == expected for value in getter_values.values()))

    def test_runtime_snapshot_passes_safe_arguments_to_bounded_getters(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            self.assertEqual(endpoint, "component/call_method")
            params = params or {}
            method = str(params["methodName"])
            args = json.loads(str(params.get("args", "[]")))
            calls.append((method, args))
            payload = json.dumps({"kind": method})
            return (
                '{"status":"ok", "message":"Called '
                f"'{method}' with {len(args)} args. ReturnValue: {payload}"
                '"}'
            )

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool("diagnostics_runtime_snapshot", {})

        self.assertFalse(response["isError"])
        self.assertEqual([32], next(args for method, args in calls if method == "GetVisualRenderCensusJson"))
        self.assertEqual([64], next(args for method, args in calls if method == "GetLoadedPrefabVisualAuditJson"))
        self.assertEqual([64, 32], next(args for method, args in calls if method == "GetLoaderAuditCensusJson"))
        self.assertEqual([64], next(args for method, args in calls if method == "GetUnitMechanicsCensusJson"))
        self.assertNotIn("GetUnitLoadoutAndPoolCensusJson", [method for method, _ in calls])
        self.assertNotIn("GetVisualMaterialCensusJson", [method for method, _ in calls])
        self.assertNotIn("GetVisualSurfaceCensusJson", [method for method, _ in calls])

    def test_agent_coordination_and_locking_tools(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            params = params or {}
            method = str(params.get("methodName", ""))
            raw_args = json.loads(str(params.get("args", "[]")))
            calls.append((method, raw_args))
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with {len(raw_args)} args. ReturnValue: {json.dumps({'kind': 'agentOperationResult', 'success': True})}"
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resp1 = SERVER.execute_tool("diagnostics_register_agent_session", {"agentId": "hermes-1", "agentRole": "Coordinator", "assignedSector": "North"})
            self.assertFalse(resp1["isError"])

            resp2 = SERVER.execute_tool("diagnostics_ping_agent", {"agentId": "hermes-1", "message": "ping"})
            self.assertFalse(resp2["isError"])

            resp3 = SERVER.execute_tool("diagnostics_post_agent_message", {"senderId": "hermes-1", "recipientId": "*", "messageType": "TacticalIntent", "payload": "Advance"})
            self.assertFalse(resp3["isError"])

            resp4 = SERVER.execute_tool("diagnostics_acquire_agent_lock", {"agentId": "hermes-1", "resourceKey": "unit:command_tank", "durationSeconds": 25.0})
            self.assertFalse(resp4["isError"])

            resp5 = SERVER.execute_tool("diagnostics_acquire_agent_lock_batch", {"agentId": "hermes-1", "resourceKeys": ["unit:scout", "factory:tank_depot"], "durationSeconds": 15.0})
            self.assertFalse(resp5["isError"])

            resp6 = SERVER.execute_tool("diagnostics_release_agent_lock", {"agentId": "hermes-1", "resourceKey": "unit:command_tank"})
            self.assertFalse(resp6["isError"])

            resp7 = SERVER.execute_tool("diagnostics_release_agent_lock_batch", {"agentId": "hermes-1", "resourceKeys": ["unit:scout", "factory:tank_depot"]})
            self.assertFalse(resp7["isError"])

            resp8 = SERVER.execute_tool("diagnostics_deregister_agent_session", {"agentId": "hermes-1"})
            self.assertFalse(resp8["isError"])

            resp9 = SERVER.execute_tool("diagnostics_get_agent_coordination", {})
            self.assertFalse(resp9["isError"])

        self.assertEqual(9, len(calls))
        self.assertEqual("RegisterAgentSessionJson", calls[0][0])
        self.assertEqual(["hermes-1", "Coordinator", "", "North"], calls[0][1])
        self.assertEqual("PingAgentJson", calls[1][0])
        self.assertEqual(["hermes-1", "ping"], calls[1][1])
        self.assertEqual("PostAgentMessageJson", calls[2][0])
        self.assertEqual(["hermes-1", "TacticalIntent", "Advance", "*"], calls[2][1])
        self.assertEqual("AcquireAgentLockJson", calls[3][0])
        self.assertEqual(["hermes-1", "unit:command_tank", 25.0], calls[3][1])
        self.assertEqual("AcquireAgentLockBatchJson", calls[4][0])
        self.assertEqual(["hermes-1", ["unit:scout", "factory:tank_depot"], 15.0], calls[4][1])
        self.assertEqual("ReleaseAgentLockJson", calls[5][0])
        self.assertEqual(["hermes-1", "unit:command_tank"], calls[5][1])
        self.assertEqual("ReleaseAgentLockBatchJson", calls[6][0])
        self.assertEqual(["hermes-1", ["unit:scout", "factory:tank_depot"]], calls[6][1])
        self.assertEqual("DeregisterAgentSessionJson", calls[7][0])
        self.assertEqual(["hermes-1"], calls[7][1])
        self.assertEqual("GetAgentCoordinationCensusJson", calls[8][0])

    def test_visual_census_and_inspection_tools(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            params = params or {}
            method = str(params.get("methodName", ""))
            raw_args = json.loads(str(params.get("args", "[]")))
            calls.append((method, raw_args))
            payload = {"kind": "visualDiagnostic", "unitId": "test:arachnid"}
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with {len(raw_args)} args. ReturnValue: {json.dumps(payload)}"
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resp1 = SERVER.execute_tool("diagnostics_get_visual_census", {"maxTrackedUnits": 16})
            self.assertFalse(resp1["isError"])

            resp2 = SERVER.execute_tool("diagnostics_inspect_entity_visual", {"entityKeyId": "test:arachnid"})
            self.assertFalse(resp2["isError"])

            resp3 = SERVER.execute_tool("diagnostics_get_loaded_unit_visual_audit", {"maxEntries": 32})
            self.assertFalse(resp3["isError"])

        self.assertEqual(3, len(calls))
        self.assertEqual("GetVisualRenderCensusJson", calls[0][0])
        self.assertEqual([16], calls[0][1])
        self.assertEqual("InspectEntityVisualJson", calls[1][0])
        self.assertEqual(["test:arachnid"], calls[1][1])
        self.assertEqual("GetLoadedPrefabVisualAuditJson", calls[2][0])
        self.assertEqual([32], calls[2][1])

    def test_loader_audit_census_and_reconcile_tools(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            params = params or {}
            method = str(params.get("methodName", ""))
            raw_args = json.loads(str(params.get("args", "[]")))
            calls.append((method, raw_args))
            payload = {"kind": "loaderRuntimeAuditCensus", "omittedUnitCount": 0, "omittedAiProfileCount": 0}
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with {len(raw_args)} args. ReturnValue: {json.dumps(payload)}"
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resp1 = SERVER.execute_tool("diagnostics_get_loader_audit_census", {"maxUnits": 24, "maxAiProfiles": 12})
            self.assertFalse(resp1["isError"])

            resp2 = SERVER.execute_tool("diagnostics_order_reconcile_loader_state", {})
            self.assertFalse(resp2["isError"])

        self.assertEqual(2, len(calls))
        self.assertEqual("GetLoaderAuditCensusJson", calls[0][0])
        self.assertEqual([24, 12], calls[0][1])
        self.assertEqual("OrderReconcileLoaderStateJson", calls[1][0])
        self.assertEqual([], calls[1][1])

    def test_generate_agent_configs(self) -> None:
        configs = SERVER.generate_agent_configs()
        self.assertIn("claude", configs)
        self.assertIn("cursor", configs)
        self.assertIn("vscode", configs)
        self.assertIn("hermes", configs)


if __name__ == "__main__":
    unittest.main()
