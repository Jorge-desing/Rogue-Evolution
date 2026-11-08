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
            "LiveVisualQualityPassCount": ("System.Int32", "3"),
            "LiveVisualQualityIssueCount": ("System.Int32", "0"),
            "LiveVertexWearSampleCount": ("System.Int32", "1200"),
            "LiveTotalUpgradeCount": ("System.Int32", "12"),
            "LiveActiveUpgradeCount": ("System.Int32", "10"),
            "LiveModdedUpgradeCount": ("System.Int32", "2"),
            "LiveTotalRelicCount": ("System.Int32", "8"),
            "LiveActiveRelicCount": ("System.Int32", "7"),
            "LiveModdedRelicCount": ("System.Int32", "1"),
            "AuditedUnitPrefabCount": ("System.Int32", "6"),
            "LiveMechanicsDirectorCount": ("System.Int32", "4"),
            "LiveVisualDirectorCount": ("System.Int32", "4"),
            "LiveJammedTurretCount": ("System.Int32", "0"),
            "LiveRegeneratingShieldCount": ("System.Int32", "1"),
            # Expanded scalars (already-available LoaderDiagnosticsComponent properties).
            "ActivePackCount": ("System.Int32", "3"),
            "FailedPackCount": ("System.Int32", "0"),
            "RegisteredUnitCount": ("System.Int32", "18"),
            "RegisteredStageCount": ("System.Int32", "5"),
            "RegisteredAiProfileCount": ("System.Int32", "5"),
            "RegisteredAiModuleCount": ("System.Int32", "7"),
            "RegisteredRuntimeCombatXpCount": ("System.Int32", "2"),
            "RuntimeInstalledHookCount": ("System.Int32", "38"),
            "LivePlayerCredits": ("System.Int32", "500"),
            "LiveAiCredits": ("System.Int32", "300"),
            "LiveAttackWaveNumber": ("System.Int32", "2"),
            "LiveCombatActiveUnitCount": ("System.Int32", "4"),
            "LiveUnitsWithStatusEffectsCount": ("System.Int32", "1"),
            "LiveTotalFactoryCount": ("System.Int32", "6"),
            "LiveActiveProducingFactoryCount": ("System.Int32", "1"),
            "LiveQueuedUnitCount": ("System.Int32", "2"),
            "LiveHasPlayerHq": ("System.Boolean", "true"),
            "LivePlayerHqHealthRatio": ("System.Single", "0.85"),
            "LiveAiStructureHealthRatio": ("System.Single", "0.72"),
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
                "LiveVisualQualityPassCount": 3,
                "LiveVisualQualityIssueCount": 0,
                "LiveVertexWearSampleCount": 1200,
                "LiveTotalUpgradeCount": 12,
                "LiveActiveUpgradeCount": 10,
                "LiveModdedUpgradeCount": 2,
                "LiveTotalRelicCount": 8,
                "LiveActiveRelicCount": 7,
                "LiveModdedRelicCount": 1,
                "AuditedUnitPrefabCount": 6,
                "LiveMechanicsDirectorCount": 4,
                "LiveVisualDirectorCount": 4,
                "LiveJammedTurretCount": 0,
                "LiveRegeneratingShieldCount": 1,
                "ActivePackCount": 3,
                "FailedPackCount": 0,
                "RegisteredUnitCount": 18,
                "RegisteredStageCount": 5,
                "RegisteredAiProfileCount": 5,
                "RegisteredAiModuleCount": 7,
                "RegisteredRuntimeCombatXpCount": 2,
                "RuntimeInstalledHookCount": 38,
                "LivePlayerCredits": 500,
                "LiveAiCredits": 300,
                "LiveAttackWaveNumber": 2,
                "LiveCombatActiveUnitCount": 4,
                "LiveUnitsWithStatusEffectsCount": 1,
                "LiveTotalFactoryCount": 6,
                "LiveActiveProducingFactoryCount": 1,
                "LiveQueuedUnitCount": 2,
                "LiveHasPlayerHq": True,
                "LivePlayerHqHealthRatio": 0.85,
                "LiveAiStructureHealthRatio": 0.72,
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

    def test_group_array_commands_use_csv_bridge_overloads(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            self.assertEqual(endpoint, "component/call_method")
            params = params or {}
            method = str(params["methodName"])
            args = json.loads(str(params.get("args", "[]")))
            calls.append((method, args))
            payload = json.dumps({"kind": "groupCommandResult", "success": True})
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with {len(args)} args. ReturnValue: {payload}",
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            formation = SERVER.execute_tool(
                "diagnostics_order_formation_move",
                {
                    "entityKeyIds": ["unit:alpha", "unit:beta"],
                    "formationType": "Wedge",
                    "targetX": 10.0,
                    "targetZ": 20.0,
                    "spacing": 4.0,
                },
            )
            stance = SERVER.execute_tool(
                "diagnostics_order_group_tactical_stance",
                {"entityKeyIds": ["unit:alpha", "unit:beta"], "stanceName": "Defensive"},
            )

        self.assertFalse(formation["isError"])
        self.assertFalse(stance["isError"])
        self.assertEqual(
            (
                "OrderFormationMoveCsvJson",
                ["unit:alpha;unit:beta", "Wedge", 10.0, 20.0, 4.0],
            ),
            calls[0],
        )
        self.assertEqual(
            ("OrderGroupTacticalStanceCsvJson", ["unit:alpha;unit:beta", "Defensive"]),
            calls[1],
        )

    def test_synthetic_gameplay_scenario_forwards_bounded_primitive_arguments(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            self.assertEqual(endpoint, "component/call_method")
            params = params or {}
            method = str(params["methodName"])
            args = json.loads(str(params.get("args", "[]")))
            calls.append((method, args))
            payload = json.dumps({"kind": "syntheticGameplayScenario", "success": True})
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with {len(args)} args. ReturnValue: {payload}",
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool(
                "diagnostics_run_synthetic_gameplay_scenario",
                {
                    "unitIdsCsv": "sample-native:scout-tank,sample-native:arachnid-hexapod",
                    "targetX": -10.0,
                    "targetZ": 145.0,
                    "formationType": "Wedge",
                    "spacing": 4.0,
                },
            )

        self.assertFalse(response["isError"])
        self.assertEqual(
            (
                "RunSyntheticGameplayScenarioJson",
                [
                    "sample-native:scout-tank;sample-native:arachnid-hexapod",
                    -10.0,
                    145.0,
                    "Wedge",
                    4.0,
                ],
            ),
            calls[0],
        )

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
        self.assertEqual(47, len(snapshot))
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
        self.assertEqual([64], next(args for method, args in calls if method == "GetVisualQualityCensusJson"))
        self.assertEqual([64], next(args for method, args in calls if method == "GetLoadedPrefabVisualAuditJson"))
        self.assertEqual([64, 32], next(args for method, args in calls if method == "GetLoaderAuditCensusJson"))
        self.assertEqual([64], next(args for method, args in calls if method == "GetUnitMechanicsCensusJson"))
        self.assertEqual([64], next(args for method, args in calls if method == "GetVisualSurfaceCensusJson"))
        self.assertEqual([64], next(args for method, args in calls if method == "GetVisualMaterialCensusJson"))
        self.assertEqual([64], next(args for method, args in calls if method == "GetSocketAuditCensusJson"))
        self.assertEqual([64], next(args for method, args in calls if method == "GetIntermissionCensusJson"))
        self.assertEqual([], next(args for method, args in calls if method == "GetNavigatorStateJson"))
        self.assertNotIn("GetUnitLoadoutAndPoolCensusJson", [method for method, _ in calls])
        self.assertIn("GetVisualMaterialCensusJson", [method for method, _ in calls])
        self.assertIn("GetVisualSurfaceCensusJson", [method for method, _ in calls])
        self.assertIn("GetGameTimeScaleJson", [method for method, _ in calls])

    def test_call_getters_passes_safe_args_to_optional_param_getters(self) -> None:
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
            response = SERVER.execute_tool(
                "diagnostics_call_getters",
                {"getters": ["GetRelicCensusJson", "GetUpgradeCensusJson", "GetVisualVariantCensusJson"]},
            )

        self.assertFalse(response["isError"])
        self.assertEqual([64], next(args for method, args in calls if method == "GetRelicCensusJson"))
        self.assertEqual([64], next(args for method, args in calls if method == "GetUpgradeCensusJson"))
        self.assertEqual([64], next(args for method, args in calls if method == "GetVisualVariantCensusJson"))

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
        self.assertEqual("AcquireAgentLockBatchCsvJson", calls[4][0])
        self.assertEqual(["hermes-1", "unit:scout;factory:tank_depot", 15.0], calls[4][1])
        self.assertEqual("ReleaseAgentLockJson", calls[5][0])
        self.assertEqual(["hermes-1", "unit:command_tank"], calls[5][1])
        self.assertEqual("ReleaseAgentLockBatchCsvJson", calls[6][0])
        self.assertEqual(["hermes-1", "unit:scout;factory:tank_depot"], calls[6][1])
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

            resp4 = SERVER.execute_tool("diagnostics_get_visual_surface_census", {"maxTrackedUnits": 16})
            self.assertFalse(resp4["isError"])

            resp5 = SERVER.execute_tool("diagnostics_get_visual_material_census", {"maxTrackedUnits": 16})
            self.assertFalse(resp5["isError"])

            resp6 = SERVER.execute_tool("diagnostics_get_socket_audit_census", {"maxTrackedUnits": 16})
            self.assertFalse(resp6["isError"])

        self.assertEqual(6, len(calls))
        self.assertEqual("GetVisualRenderCensusJson", calls[0][0])
        self.assertEqual([16], calls[0][1])
        self.assertEqual("InspectEntityVisualJson", calls[1][0])
        self.assertEqual(["test:arachnid"], calls[1][1])
        self.assertEqual("GetLoadedPrefabVisualAuditJson", calls[2][0])
        self.assertEqual([32], calls[2][1])
        self.assertEqual("GetVisualSurfaceCensusJson", calls[3][0])
        self.assertEqual([16], calls[3][1])
        self.assertEqual("GetVisualMaterialCensusJson", calls[4][0])
        self.assertEqual([16], calls[4][1])
        self.assertEqual("GetSocketAuditCensusJson", calls[5][0])
        self.assertEqual([16], calls[5][1])

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

    def test_tool_specs_have_unique_names_and_evolved_tools(self) -> None:
        from pathlib import Path as _P
        import importlib.util as _ilu
        import sys as _sys
        _spec = _ilu.spec_from_file_location(
            "build_bepinex_mcp_check", _P(__file__).resolve().parents[2] / "tools" / "build_bepinex_mcp.py"
        )
        assert _spec is not None and _spec.loader is not None
        _mod = _ilu.module_from_spec(_spec)
        _sys.modules[_spec.name] = _mod
        _spec.loader.exec_module(_mod)
        names = [s["name"] for s in _mod.TOOL_SPECS]
        self.assertEqual(len(names), len(set(names)), f"Duplicate tool specs: {[n for n in set(names) if names.count(n) > 1]}")
        # Evolved bridge: dedup keeps unique count; new summaries/detail/lock tools present.
        for expected in (
            "diagnostics_get_visual_health_summary",
            "diagnostics_get_combat_health_summary",
            "diagnostics_get_entity_detail",
            "diagnostics_get_agent_lock_status",
            "diagnostics_get_visual_surface_census",
            "diagnostics_get_visual_material_census",
            "diagnostics_get_game_time_scale",
            "diagnostics_preflight",
            "diagnostics_get_gui_state",
            "diagnostics_order_batch",
            "diagnostics_run_synthetic_gameplay_scenario",
            "diagnostics_get_control_guide",
            "diagnostics_gui_control",
            "diagnostics_ui_click",
            "diagnostics_ui_press_button",
            "diagnostics_get_intermission_census",
            "diagnostics_order_enter_level",
            "diagnostics_order_select_level_node",
            "diagnostics_get_navigator_state",
            "diagnostics_order_navigator_pause",
            "diagnostics_fast_snapshot",
            "diagnostics_get_upgrade_census",
            "diagnostics_get_relic_census",
            "diagnostics_order_apply_visual_variant",
            "diagnostics_get_visual_variant_census",
            "discover_tools",
            "describe_tool",
        ):
            self.assertIn(expected, names)
        self.assertEqual(223, len(names))

    def test_tools_list_default_is_all_and_paginates(self) -> None:
        full = SERVER.handle_request({"jsonrpc": "2.0", "id": 1, "method": "tools/list", "params": {}})
        tools = full["result"]["tools"]
        self.assertEqual(223, len(tools))
        page = SERVER.handle_request({"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {"limit": 10}})
        self.assertEqual(10, len(page["result"]["tools"]))
        cursor = page["result"]["nextCursor"]
        page2 = SERVER.handle_request({"jsonrpc": "2.0", "id": 3, "method": "tools/list", "params": {"cursor": cursor, "limit": 10}})
        self.assertEqual(10, len(page2["result"]["tools"]))
        self.assertNotEqual(page["result"]["tools"][0]["name"], page2["result"]["tools"][0]["name"])

    def test_tools_list_filters_by_category_and_query(self) -> None:
        resp = SERVER.handle_request({"jsonrpc": "2.0", "id": 1, "method": "tools/list", "params": {"category": "diagnostics", "query": "gui"}})
        names = [t["name"] for t in resp["result"]["tools"]]
        self.assertTrue(len(names) >= 1)
        self.assertTrue(len(names) >= 1)
        resp2 = SERVER.handle_request({"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {"includeAliases": False}})
        self.assertLess(len(resp2["result"]["tools"]), 205)
        self.assertNotIn("ping_unity_bridge", [t["name"] for t in resp2["result"]["tools"]])
        self.assertIn("system_ping", [t["name"] for t in resp2["result"]["tools"]])

    def test_discover_tools_returns_lightweight_index(self) -> None:
        resp = SERVER.execute_tool("discover_tools", {"query": "gui", "includeAliases": False})
        self.assertFalse(resp["isError"])
        data = json.loads(resp["content"][0]["text"])
        self.assertGreaterEqual(data["total"], 1)
        self.assertTrue(all("inputSchema" not in t for t in data["tools"]))
        self.assertTrue(all("gui" in t["name"] or "gui" in t["description"].lower() for t in data["tools"]))

    def test_describe_tool_returns_full_schema_on_demand(self) -> None:
        resp = SERVER.execute_tool("describe_tool", {"name": "diagnostics_get_gui_state"})
        self.assertFalse(resp["isError"])
        data = json.loads(resp["content"][0]["text"])
        self.assertEqual(data["name"], "diagnostics_get_gui_state")
        self.assertIn("inputSchema", data)

    def test_visual_health_summary_forwards_to_bridge(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            params = params or {}
            method = str(params.get("methodName", ""))
            raw_args = json.loads(str(params.get("args", "[]")))
            calls.append((method, raw_args))
            payload = {"kind": "visualHealthSummary", "omittedUnitCount": 0, "trackedUnitCount": 2}
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with {len(raw_args)} args. ReturnValue: {json.dumps(payload)}"
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resp = SERVER.execute_tool("diagnostics_get_visual_health_summary", {})
            self.assertFalse(resp["isError"])
            data = json.loads(resp["content"][0]["text"])
            self.assertEqual("visualHealthSummary", data["kind"])
            resp2 = SERVER.execute_tool("diagnostics_get_visual_health_summary", {"maxTrackedUnits": 16})
            self.assertFalse(resp2["isError"])

        self.assertEqual(2, len(calls))
        self.assertEqual("GetVisualHealthSummaryJson", calls[0][0])
        self.assertEqual([64], calls[0][1])
        self.assertEqual("GetVisualHealthSummaryJson", calls[1][0])
        self.assertEqual([16], calls[1][1])

    def test_combat_health_summary_aggregates_getters(self) -> None:
        calls: list[str] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            params = params or {}
            method = str(params.get("methodName", ""))
            calls.append(method)
            payload = {"kind": method, "count": 1}
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with 0 args. ReturnValue: {json.dumps(payload)}"
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resp = SERVER.execute_tool("diagnostics_get_combat_health_summary", {})
            self.assertFalse(resp["isError"])
            data = json.loads(resp["content"][0]["text"])

        expected_getters = [
            "GetBattlefieldCensusJson", "GetPlayerRosterJson", "GetAiRosterJson",
            "GetProductionCensusJson", "GetBaseIntegrityCensusJson",
            "GetAiDirectorCensusJson", "GetHazardCensusJson",
            "GetVeterancyCensusJson", "GetCombatStatusCensusJson",
            "GetEconomyMatchCensusJson", "GetSystemHealthJson",
        ]
        for getter in expected_getters:
            self.assertIn(getter, data)
            self.assertIn(getter, calls)
        self.assertIn("_meta", data)
        self.assertEqual(len(expected_getters), data["_meta"]["getterCount"])

    def test_entity_detail_filters_rows_by_entity_key(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            params = params or {}
            method = str(params.get("methodName", ""))
            raw_args = json.loads(str(params.get("args", "[]")))
            calls.append((method, raw_args))
            if method in ("InspectEntityVisualJson", "GetEntityBalanceJson"):
                payload = {"entityKeyId": "test:arachnid", "kind": method}
            elif method == "GetUnitMechanicsCensusJson":
                payload = {"rows": [{"entityKeyId": "test:arachnid"}, {"entityKeyId": "other"}]}
            else:
                payload = {"playerUnitRows": [{"entityKeyId": "test:arachnid", "healthRatio": 0.8}, {"entityKeyId": "other", "healthRatio": 0.5}], "kind": method}
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with {len(raw_args)} args. ReturnValue: {json.dumps(payload)}"
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resp = SERVER.execute_tool("diagnostics_get_entity_detail", {"entityKeyId": "test:arachnid"})
            self.assertFalse(resp["isError"])
            data = json.loads(resp["content"][0]["text"])

        self.assertEqual("test:arachnid", data["entityKeyId"])
        self.assertTrue(data["found"])
        self.assertGreater(data["matchedRowCount"], 0)
        # Battlefield rows must be filtered to only the requested entity.
        battlefield_section = data["sections"]["GetBattlefieldCensusJson"]
        self.assertEqual([{"entityKeyId": "test:arachnid", "healthRatio": 0.8}], battlefield_section["matchedRows"])
        # All expected getters were consulted.
        called_methods = {m for m, _ in calls}
        for getter in ("GetBattlefieldCensusJson", "GetPlayerRosterJson", "GetAiRosterJson", "GetUnitThreatAssessmentJson", "GetTacticalStanceCensusJson", "GetUnitAffixCensusJson", "GetVeterancyCensusJson", "GetUnitLoadoutCensusJson", "InspectEntityVisualJson", "GetEntityBalanceJson"):
            self.assertIn(getter, called_methods)

    def test_agent_lock_status_reports_ownership(self) -> None:
        coordination = {
            "kind": "agentCoordinationCensus",
            "omittedSessionCount": 0,
            "omittedLockCount": 0,
            "omittedMessageCount": 0,
            "activeSessionCount": 1,
            "activeLockCount": 1,
            "sessions": [{"agentId": "hermes-1", "status": "Active", "assignedSector": "Global"}],
            "locks": [{"resourceKey": "unit:scout_1", "ownerAgentId": "hermes-1", "resourceCategory": "Unit", "remainingSeconds": 12.5, "contentionCount": 2}],
            "messages": [],
        }

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            params = params or {}
            method = str(params.get("methodName", ""))
            self.assertEqual("GetAgentCoordinationCensusJson", method)
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with 0 args. ReturnValue: {json.dumps(coordination)}"
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resp = SERVER.execute_tool("diagnostics_get_agent_lock_status", {"agentId": "hermes-1", "resourceKey": "unit:scout_1"})
            self.assertFalse(resp["isError"])
            data = json.loads(resp["content"][0]["text"])
            self.assertTrue(data["sessionActive"])
            self.assertTrue(data["locked"])
            self.assertEqual("hermes-1", data["ownerAgentId"])
            self.assertEqual(12.5, data["remainingSeconds"])
            self.assertEqual(2, data["contentionCount"])

            resp2 = SERVER.execute_tool("diagnostics_get_agent_lock_status", {"agentId": "hermes-1", "resourceKey": "unit:other"})
            self.assertFalse(resp2["isError"])
            data2 = json.loads(resp2["content"][0]["text"])
            self.assertFalse(data2["locked"])

    def test_diagnostics_preflight_parallel_checks(self) -> None:
        calls: list[str] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            calls.append(endpoint)
            params = params or {}
            if endpoint == "system/ping":
                return json.dumps({"status": "ok"})
            if endpoint == "system/capabilities":
                return json.dumps({"unityVersion": "2021.3.45f2"})
            if endpoint == "scene:find_objects_with_component":
                return json.dumps([{"name": "LoaderDiagnostics", "id": 73}])
            if endpoint == "component/call_method":
                method = str(params.get("methodName", ""))
                payload = {"kind": method, "ok": True}
                if method == "GetSystemHealthJson":
                    payload = {"isNativeRegistryReady": True, "activePackCount": 3}
                if method == "GetGameTimeScaleJson":
                    payload = {"timeScale": 1.0}
                return json.dumps({
                    "status": "ok",
                    "message": f"Called '{method}' with 0 args. ReturnValue: {json.dumps(payload)}"
                })
            self.fail(f"Unexpected bridge endpoint: {endpoint}")

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resp = SERVER.execute_tool("diagnostics_preflight", {})
            self.assertFalse(resp["isError"])
            data = json.loads(resp["content"][0]["text"])

        self.assertTrue(data["ready"])
        self.assertTrue(data["bridgeAlive"])
        self.assertTrue(data["componentFound"])
        self.assertEqual(73, data["componentGoId"])
        self.assertIn("checks", data)
        self.assertIn("systemHealth", data)
        self.assertIn("timeScale", data)
        self.assertIn("_meta", data)
        for endpoint in ("system/ping", "system/capabilities", "scene:find_objects_with_component"):
            self.assertIn(endpoint, calls)

    def test_diagnostics_preflight_unavailable_bridge(self) -> None:
        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            return json.dumps({"error": "Failed to communicate with BepInEx MCP"})

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", None),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resp = SERVER.execute_tool("diagnostics_preflight", {})
            self.assertFalse(resp["isError"])
            data = json.loads(resp["content"][0]["text"])
        self.assertFalse(data["ready"])
        self.assertFalse(data["bridgeAlive"])
        self.assertFalse(data["componentFound"])

    def test_diagnostics_get_gui_state_returns_overlay_and_camera(self) -> None:
        member_values = {
            "OverlayOpen": ("System.Boolean", "true"),
            "OverlayReady": ("System.Boolean", "true"),
            "RangeDisplayReady": ("System.Boolean", "true"),
            "IsRuntimeHooksInstalled": ("System.Boolean", "true"),
        }

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            params = params or {}
            if endpoint == "component/get_member":
                name = str(params["memberName"])
                clr_type, value = member_values[name]
                return json.dumps({"name": name, "type": clr_type, "value": value})
            if endpoint == "component/call_method":
                method = str(params.get("methodName", ""))
                if method == "GetGameTimeScaleJson":
                    payload = {"timeScale": 1.0}
                elif method == "GetCameraCensusJson":
                    payload = {"fieldOfView": 50.0, "centerX": 0.0, "centerZ": 0.0}
                else:
                    payload = {"kind": method}
                return json.dumps({
                    "status": "ok",
                    "message": f"Called '{method}' with 0 args. ReturnValue: {json.dumps(payload)}"
                })
            self.fail(f"Unexpected bridge endpoint: {endpoint}")

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resp = SERVER.execute_tool("diagnostics_get_gui_state", {})
            self.assertFalse(resp["isError"])
            data = json.loads(resp["content"][0]["text"])
        self.assertTrue(data["overlayOpen"])
        self.assertTrue(data["overlayReady"])
        self.assertTrue(data["rangeDisplayReady"])
        self.assertTrue(data["hooksInstalled"])
        self.assertEqual(1.0, data["timeScale"]["timeScale"])
        self.assertEqual(50.0, data["camera"]["fieldOfView"])

    def test_diagnostics_order_batch_executes_parallel_and_stops(self) -> None:
        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            params = params or {}
            method = str(params.get("methodName", ""))
            raw_args = json.loads(str(params.get("args", "[]")))
            payload = {"kind": "commandResult", "success": True, "method": method}
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with {len(raw_args)} args. ReturnValue: {json.dumps(payload)}"
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resp = SERVER.execute_tool("diagnostics_order_batch", {
                "operations": [
                    {"tool": "diagnostics_order_unit_move", "arguments": {"entityKeyId": "test:a", "destinationX": 10.0, "destinationZ": 20.0}},
                    {"tool": "diagnostics_order_unit_stop", "arguments": {"entityKeyId": "test:a"}},
                ]
            })
            self.assertFalse(resp["isError"])
            data = json.loads(resp["content"][0]["text"])
            self.assertEqual(2, data["_meta"]["count"])
            self.assertEqual(2, data["_meta"]["successCount"])
            self.assertEqual(0, data["_meta"]["errorCount"])
            self.assertEqual(2, len(data["results"]))
            tools = {r["tool"] for r in data["results"]}
            self.assertIn("diagnostics_order_unit_move", tools)
            self.assertIn("diagnostics_order_unit_stop", tools)

            # Nested batch is rejected, not recursed.
            resp2 = SERVER.execute_tool("diagnostics_order_batch", {
                "operations": [{"tool": "diagnostics_order_batch", "arguments": {}}]
            })
            self.assertFalse(resp2["isError"])
            data2 = json.loads(resp2["content"][0]["text"])
            self.assertEqual(1, data2["_meta"]["errorCount"])

    def test_diagnostics_get_control_guide_returns_recipes(self) -> None:
        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=lambda *a, **k: (_ for _ in ()).throw(AssertionError("control_guide must not call bridge"))),
        ):
            resp = SERVER.execute_tool("diagnostics_get_control_guide", {})
            self.assertFalse(resp["isError"])
            data = json.loads(resp["content"][0]["text"])
        self.assertEqual("Profile2", data["profile"])
        self.assertIn("preflight", data)
        self.assertIn("control", data)
        self.assertIn("cleanup", data)
        guide_text = json.dumps(data)
        self.assertIn("timeScale", guide_text)
        self.assertIn("diagnostics_preflight", guide_text)
        self.assertIn("diagnostics_get_gui_state", guide_text)

    def test_performance_census_tool_forwards_without_arguments(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            self.assertEqual(endpoint, "component/call_method")
            params = params or {}
            method = str(params["methodName"])
            args = json.loads(str(params.get("args", "[]")))
            calls.append((method, args))
            payload = json.dumps({"kind": "performanceCensus", "verdict": "Healthy"})
            return (
                '{"status":"ok", "message":"Called '
                f"'{method}' with {len(args)} args. ReturnValue: {payload}"
                '"}'
            )

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool("diagnostics_get_performance_census", {})

        self.assertFalse(response["isError"])
        self.assertEqual([("GetPerformanceCensusJson", [])], calls)
        payload = json.loads(response["content"][0]["text"])
        self.assertEqual("Healthy", payload["verdict"])

    def test_frame_timing_census_tool_forwards_without_arguments(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            self.assertEqual(endpoint, "component/call_method")
            params = params or {}
            method = str(params["methodName"])
            args = json.loads(str(params.get("args", "[]")))
            calls.append((method, args))
            payload = json.dumps({"kind": "frameTimingCensus", "verdict": "Smooth"})
            return (
                '{"status":"ok", "message":"Called '
                f"'{method}' with {len(args)} args. ReturnValue: {payload}"
                '"}'
            )

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool("diagnostics_get_frame_timing_census", {})

        self.assertFalse(response["isError"])
        self.assertEqual([("GetFrameTimingCensusJson", [])], calls)
        payload = json.loads(response["content"][0]["text"])
        self.assertEqual("Smooth", payload["verdict"])

    def test_diff_snapshots_tool_forwards_bounded_arguments(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            self.assertEqual(endpoint, "component/call_method")
            params = params or {}
            method = str(params["methodName"])
            args = json.loads(str(params.get("args", "[]")))
            calls.append((method, args))
            payload = json.dumps({"isValid": True, "totalChangeCount": 1})
            return (
                '{"status":"ok", "message":"Called '
                f"'{method}' with {len(args)} args. ReturnValue: {payload}"
                '"}'
            )

        before = json.dumps({"units": 18})
        after = json.dumps({"units": 19})
        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool(
                "diagnostics_diff_snapshots",
                {"beforeJson": before, "afterJson": after, "maxChanges": 8},
            )

        self.assertFalse(response["isError"])
        self.assertEqual([("GetSnapshotDiffJson", [before, after, 8])], calls)
        payload = json.loads(response["content"][0]["text"])
        self.assertTrue(payload["isValid"])
        self.assertEqual(1, payload["totalChangeCount"])

    def _load_tool_specs(self) -> list:
        from pathlib import Path as _P
        import importlib.util as _ilu
        import sys as _sys

        _spec = _ilu.spec_from_file_location(
            "build_bepinex_mcp_opt",
            _P(__file__).resolve().parents[2] / "tools" / "build_bepinex_mcp.py",
        )
        assert _spec is not None and _spec.loader is not None
        _mod = _ilu.module_from_spec(_spec)
        _sys.modules[_spec.name] = _mod
        _spec.loader.exec_module(_mod)
        return list(_mod.TOOL_SPECS)

    def test_optimised_schemas_preserve_all_tools_and_reduce_bytes(self) -> None:
        specs = self._load_tool_specs()
        names = [s["name"] for s in specs]
        self.assertEqual(223, len(names))
        self.assertEqual(len(names), len(set(names)))
        aliases = [s for s in specs if str(s.get("description", "")).startswith("Alias for")]
        self.assertEqual(20, len(aliases))
        self.assertEqual(203, len(specs) - len(aliases))
        compact = sum(len(json.dumps(s, ensure_ascii=False, separators=(",", ":"))) for s in specs)
        self.assertLess(compact, 82000, f"optimised schemas too large: {compact} bytes")
        # Strictness and shape preserved for every tool.
        for spec in specs:
            params = spec["parameters"]
            self.assertEqual("object", params["type"], spec["name"])
            self.assertIs(False, params["additionalProperties"], spec["name"])
            required = params.get("required") or []
            properties = params.get("properties") or {}
            for req in required:
                self.assertIn(req, properties, f"{spec['name']} required {req} missing from properties")
            for pname, pval in properties.items():
                self.assertIn(pval.get("type"), ("string", "integer", "number", "boolean", "array", "object"), f"{spec['name']}.{pname}")
        # Enums preserved (no functionality lost via stringly-typed enums).
        by_name = {s["name"]: s for s in specs}
        self.assertEqual(
            ["prefix", "postfix", "transpiler", "finalizer"],
            by_name["mod_patch_method"]["parameters"]["properties"]["patchType"]["enum"],
        )
        self.assertEqual(
            ["Heal", "RepairArmor", "ChargeShield", "Damage"],
            by_name["diagnostics_adjust_vitality"]["parameters"]["properties"]["adjustmentType"]["enum"],
        )
        self.assertEqual(
            ["Firestorm", "AcidPool", "EmpField", "CryoMist", "NaniteField"],
            by_name["diagnostics_order_spawn_hazard_zone"]["parameters"]["properties"]["hazardType"]["enum"],
        )
        self.assertEqual(
            ["Master", "Music", "Gameplay", "UI"],
            by_name["diagnostics_set_audio_volume"]["parameters"]["properties"]["channel"]["enum"],
        )
        self.assertEqual(
            ["WeaponRecoil", "JamTurret", "UnjamTurret", "OpenBayDoors", "CloseBayDoors", "FootstepPulse"],
            by_name["diagnostics_order_unit_visual_feedback"]["parameters"]["properties"]["feedbackType"]["enum"],
        )
        self.assertEqual(
            ["Line", "Wedge", "Column", "Box", "Surround"],
            by_name["diagnostics_run_synthetic_gameplay_scenario"]["parameters"]["properties"]["formationType"]["enum"],
        )
        # Required lists unchanged for representative tools.
        self.assertEqual(["id", "componentName"], by_name["component_get_details"]["parameters"]["required"])
        self.assertEqual(["componentName"], by_name["scene_find_objects_with_component"]["parameters"]["required"])
        self.assertEqual(["entityKeyId", "stanceName"], by_name["diagnostics_order_unit_tactical_stance"]["parameters"]["required"])
        for tool in ("diagnostics_order_enter_level", "diagnostics_order_select_level_node"):
            props = by_name[tool]["parameters"]["properties"]
            self.assertEqual(["level", "field"], by_name[tool]["parameters"]["required"])
            self.assertEqual("integer", props["level"]["type"])
            self.assertEqual("integer", props["field"]["type"])
            self.assertIn("IntermissionNodeRow", props["level"]["description"])
        self.assertIn("IntermissionNodeRow", by_name["diagnostics_get_intermission_census"]["description"])
        total_props = sum(len(s["parameters"].get("properties") or {}) for s in specs)
        self.assertEqual(358, total_props)

    def test_empty_schemas_are_compact_but_strict(self) -> None:
        specs = self._load_tool_specs()
        empty = [s for s in specs if not s["parameters"].get("properties")]
        self.assertEqual(76, len(empty))
        for spec in empty:
            self.assertEqual(
                {"type": "object", "additionalProperties": False},
                spec["parameters"],
                spec["name"],
            )
        payload = json.dumps(empty[0]["parameters"], separators=(",", ":"))
        self.assertEqual('{"type":"object","additionalProperties":false}', payload)

    def test_descriptions_strip_provenance_boilerplate_and_keep_details(self) -> None:
        specs = self._load_tool_specs()
        for spec in specs:
            desc = str(spec["description"])
            self.assertNotIn("LoaderDiagnosticsComponent", desc, spec["name"])
            self.assertNotIn("via LoaderDiagnosticsComponent", desc, spec["name"])
            self.assertNotIn("from LoaderDiagnosticsComponent", desc, spec["name"])
        by_desc = {s["name"]: str(s["description"]) for s in specs}
        self.assertIn("Line, Wedge, Column, Box, Surround", by_desc["diagnostics_order_formation_move"])
        self.assertIn("parallel X/Z", by_desc["diagnostics_order_unit_patrol"])
        self.assertIn("by name", by_desc["diagnostics_call_getter"])
        self.assertIn("GetPlayerRosterJson", by_desc["diagnostics_call_getter"])
        self.assertIn("'All'", by_desc["diagnostics_order_remove_unit_affix"])
        self.assertIn("native balancing pipeline", by_desc["diagnostics_apply_card_change"])
        self.assertIn("Swift", by_desc["diagnostics_order_apply_unit_affix"])
        self.assertIn("18+", by_desc["diagnostics_get_catalog"])
        self.assertTrue(by_desc["diagnostics_get_system_health"].startswith("Queries unified live system health"))
        self.assertNotIn("directly from", by_desc["diagnostics_get_system_health"])

    def test_alias_schemas_match_canonical_and_cover_selector_json(self) -> None:
        specs = self._load_tool_specs()
        by_name = {s["name"]: s for s in specs}
        aliases = [s for s in specs if str(s.get("description", "")).startswith("Alias for")]
        self.assertEqual(20, len(aliases))
        for alias in aliases:
            canon_name = str(alias["description"])[len("Alias for "):].split(":", 1)[0].strip()
            self.assertIn(canon_name, by_name, alias["name"])
            self.assertEqual(
                by_name[canon_name]["parameters"],
                alias["parameters"],
                f"alias {alias['name']} drifted from canonical {canon_name}",
            )
        self.assertIn("selector_json", by_name["scene_resolve_selector"]["parameters"]["properties"])
        self.assertIn("selector_json", by_name["resolve_gameobject_selector"]["parameters"]["properties"])
        self.assertEqual(
            "Component type name.",
            by_name["scene_find_objects_with_component"]["parameters"]["properties"]["componentName"]["description"],
        )

    def test_aliases_route_to_same_endpoint_as_canonical(self) -> None:
        cases = [
            ("system_ping", "ping_unity_bridge", "system/ping"),
            ("system_capabilities", "get_runtime_capabilities", "system/capabilities"),
            ("scene_search_gameobjects", "search_gameobjects", "scene/search_gameobjects"),
            ("component_get_details", "get_component_details", "component/get_details"),
            ("scene_find_objects_with_component", "find_objects_with_component", "scene:find_objects_with_component"),
        ]
        for canonical, alias, endpoint in cases:
            args: dict[str, object] = {}
            if canonical == "scene_search_gameobjects":
                args = {"name": "Hero"}
            elif canonical == "component_get_details":
                args = {"id": 1, "componentName": "Transform"}
            elif canonical == "scene_find_objects_with_component":
                args = {"componentName": "Transform"}
            with mock.patch.object(SERVER, "_call_http", return_value=json.dumps({"status": "ok"})) as fake:
                resp = SERVER.execute_tool(canonical, dict(args))
                self.assertFalse(resp["isError"], canonical)
                self.assertEqual(endpoint, fake.call_args[0][0], canonical)
            with mock.patch.object(SERVER, "_call_http", return_value=json.dumps({"status": "ok"})) as fake:
                resp = SERVER.execute_tool(alias, dict(args))
                self.assertFalse(resp["isError"], alias)
                self.assertEqual(endpoint, fake.call_args[0][0], alias)

    def test_property_shortening_preserves_types_and_tools_list_compact(self) -> None:
        specs = self._load_tool_specs()
        by_name = {s["name"]: s for s in specs}
        self.assertEqual(
            "GameObject ID.",
            by_name["component_get_details"]["parameters"]["properties"]["id"]["description"],
        )
        self.assertEqual(
            "integer",
            by_name["component_get_details"]["parameters"]["properties"]["id"]["type"],
        )
        blob = json.dumps(specs, ensure_ascii=False)
        self.assertNotIn("GameObject instance ID.", blob)
        self.assertNotIn("Parent GameObject instance ID.", blob)
        resp = SERVER.handle_request({"jsonrpc": "2.0", "id": 1, "method": "tools/list", "params": {}})
        tools = resp["result"]["tools"]
        self.assertEqual(223, len(tools))
        payload = json.dumps(resp["result"], separators=(",", ":"))
        self.assertLess(len(payload), 82500, f"tools/list payload too large: {len(payload)} bytes")

if __name__ == "__main__":
    unittest.main()
