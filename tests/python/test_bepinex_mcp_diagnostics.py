"""Regression tests for the BepInEx runtime diagnostics bridge."""

from __future__ import annotations

import base64
import json
import io
import os
import tempfile
import subprocess
import unittest
from pathlib import Path
from unittest import mock

from tests.python.helpers import load_tool_module

SERVER = load_tool_module("bepinex_mcp_server")


class RuntimeDiagnosticsTests(unittest.TestCase):
    def test_stdio_process_recovers_invalid_envelope_but_closes_bad_frame(self) -> None:
        command = [SERVER.sys.executable, str(Path(SERVER.__file__).resolve())]
        ping = b'{"jsonrpc":"2.0","id":7,"method":"ping"}\n'
        result = subprocess.run(command, input=b'[]\n' + ping, capture_output=True, timeout=15)
        rows = [json.loads(row) for row in result.stdout.splitlines()]
        self.assertEqual(rows[0]["error"]["code"], -32600)
        self.assertEqual(rows[1]["id"], 7)
        result = subprocess.run(command, input=b'Content-Length: 999999999\r\n\r\n' + ping, capture_output=True, timeout=15)
        rows = [json.loads(row) for row in result.stdout.splitlines()]
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["error"]["code"], -32700)

    def test_invalid_rpc_inputs_do_not_execute_tools(self) -> None:
        bad = [[], None, {"jsonrpc": "1.0", "id": 1, "method": "ping"},
               {"jsonrpc": "2.0", "id": True, "method": "ping"},
               {"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": []},
               {"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"name": "system_ping", "arguments": []}}]
        with mock.patch.object(SERVER, "execute_tool") as execute:
            for request in bad:
                self.assertIn("error", SERVER.handle_request(request))
            self.assertIsNone(SERVER.handle_request({"jsonrpc": "2.0", "method": "tools/call", "params": {"name": "system_ping"}}))
            execute.assert_not_called()
        self.assertEqual(SERVER.handle_request({"jsonrpc": "2.0", "id": 0, "method": "ping"})["id"], 0)

    def test_stdio_rejects_oversized_frame_before_reading_body(self) -> None:
        data = b"Content-Length: 999999999999\r\n\r\n"
        with mock.patch.object(SERVER.sys, "stdin", io.TextIOWrapper(io.BytesIO(data), encoding="utf-8")):
            with self.assertRaises(SERVER.MessageFramingError):
                SERVER._read_framed_message()

    def test_ui_value_dispatch_preserves_types_and_submission(self) -> None:
        for kind, value in (("slider", 0.5), ("toggle", True), ("switch", False), ("dropdown", 2), ("text", "42")):
            with self.subTest(kind=kind), mock.patch.object(SERVER, "_call_diagnostics_method", return_value='{"success":true}') as call:
                result = SERVER.execute_tool("diagnostics_ui_set_value", {"hierarchyPath": "MainMenu::/Canvas/Input", "gameObjectId": 5, "kind": kind, "value": value, "submit": kind == "text"})
                self.assertFalse(result.get("isError", False))
                self.assertEqual("SetUiValueJson", call.call_args.args[0])
                args = call.call_args.args[1]
                self.assertEqual(value, json.loads(base64.b64decode(args[3][7:]).decode("utf-8")))
                self.assertEqual(kind == "text", args[4])

    def test_ui_value_invalid_types_never_call_bridge(self) -> None:
        for kind, value in (("slider", True), ("slider", float("nan")), ("toggle", 1), ("dropdown", True), ("dropdown", 1.5), ("text", "x" * 4097)):
            with self.subTest(kind=kind, value=str(value)[:30]), mock.patch.object(SERVER, "_call_diagnostics_method") as call:
                result = SERVER.execute_tool("diagnostics_ui_set_value", {"hierarchyPath": "MainMenu::/Canvas/Input", "gameObjectId": 5, "kind": kind, "value": value})
                self.assertTrue(result["isError"])
                call.assert_not_called()

    def test_uncertain_sequence_outcomes_stop_even_if_continuation_requested(self) -> None:
        for result in ({"error": "timeout", "completionStatus": "unknown"},
                       {"nativeCallInvoked": True, "effectObserved": False}):
            with self.subTest(result=result), mock.patch.object(SERVER, "_call_diagnostics_method", return_value=json.dumps(result)) as call:
                response = SERVER.execute_tool("automation_run_sequence", {"stopOnFailure": False, "steps": [
                    {"tool": "diagnostics_get_game_time_scale"},
                    {"tool": "diagnostics_get_game_time_scale"},
                ]})
                payload = json.loads(response["content"][0]["text"])
                self.assertTrue(response["isError"])
                self.assertTrue(payload["results"][0]["postconditionInspectionRequired"])
                self.assertEqual(call.call_count, 1)

    def test_diagnostics_calls_never_replay_ambiguous_response_text(self) -> None:
        for result in ({"error": "HTTP Error 500"}, {"success": False, "message": "entity not found"},
                       {"error": "connection failed", "completionStatus": "unknown"},
                       {"status": "ok", "message": "ReturnValue: {\"count\":500}"}):
            with self.subTest(result=result), mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73), \
                    mock.patch.object(SERVER, "_call_http", return_value=json.dumps(result)) as call:
                SERVER._call_diagnostics_method("OrderUnitMoveJson", [])
                self.assertEqual(call.call_count, 1)

    def test_diagnostics_call_retries_only_explicit_invalid_id(self) -> None:
        with mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73), \
                mock.patch.object(SERVER, "_get_diagnostics_go_id", side_effect=[73, 74]), \
                mock.patch.object(SERVER, "_call_http", side_effect=[json.dumps({"error": "Missing or invalid 'id'"}), json.dumps({"status": "ok"})]) as call:
            result = SERVER._call_diagnostics_method("GetValueJson")
        self.assertEqual(call.call_count, 2)
        self.assertEqual(json.loads(result)["status"], "ok")

    def test_stdio_reads_utf8_frames_and_lines_without_losing_boundaries(self) -> None:
        one = json.dumps({"id": 1, "label": "ñ🚀"}, ensure_ascii=False).encode("utf-8")
        two = b'{"id":2}'
        framed = b"Content-Length: " + str(len(one)).encode() + b"\r\n\r\n" + one
        framed += b"Content-Length: " + str(len(two)).encode() + b"\r\n\r\n" + two + b'{"id":3}\n'
        with mock.patch.object(SERVER.sys, "stdin", io.TextIOWrapper(io.BytesIO(framed), encoding="utf-8")):
            self.assertEqual(SERVER._read_framed_message()["label"], "ñ🚀")
            self.assertEqual(SERVER._read_framed_message()["id"], 2)
            self.assertEqual(SERVER._read_framed_message()["id"], 3)
            self.assertIsNone(SERVER._read_framed_message())

    def test_stdio_rejects_incomplete_frame(self) -> None:
        with mock.patch.object(SERVER.sys, "stdin", io.TextIOWrapper(io.BytesIO(b"Content-Length: 20\r\n\r\n{}"), encoding="utf-8")):
            with self.assertRaisesRegex(ValueError, "incomplete message body"):
                SERVER._read_framed_message()

    def test_sequence_checks_gate_subsequent_actions(self) -> None:
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value=json.dumps({"ready": False})) as bridge:
            response = SERVER.execute_tool("automation_run_sequence", {"stopOnFailure": False, "steps": [
                {"tool": "diagnostics_get_game_time_scale", "expect": [{"path": "/ready", "equals": True}]},
                {"tool": "diagnostics_get_game_time_scale"},
            ]})
        data = json.loads(response["content"][0]["text"])
        self.assertTrue(response["isError"])
        self.assertEqual(bridge.call_count, 1)
        self.assertEqual(data["results"][0]["checks"][0]["actual"], False)
        self.assertFalse(data["results"][0]["checks"][0]["matched"])

    def test_sequence_checks_pass_and_fail_closed_on_missing_or_failed_data(self) -> None:
        steps = [{"tool": "diagnostics_get_game_time_scale", "expect": [{"path": "/ready", "equals": True}]},
                 {"tool": "diagnostics_get_game_time_scale"}]
        for payload, count in [({"ready": True}, 2), ({"other": True}, 1), ({"error": "offline"}, 1)]:
            with self.subTest(payload=payload), mock.patch.object(SERVER, "_call_diagnostics_method", return_value=json.dumps(payload)) as bridge:
                response = SERVER.execute_tool("automation_run_sequence", {"stopOnFailure": False, "steps": steps})
                self.assertEqual(bridge.call_count, count)
                self.assertEqual(response["isError"], count == 1)

    def test_sequence_rejects_invalid_check_before_calls(self) -> None:
        with mock.patch.object(SERVER, "_call_diagnostics_method") as bridge:
            response = SERVER.execute_tool("automation_run_sequence", {"steps": [
                {"tool": "diagnostics_get_game_time_scale"},
                {"tool": "diagnostics_get_game_time_scale", "expect": [{"path": "bad", "equals": True}]},
            ]})
        self.assertTrue(response["isError"])
        bridge.assert_not_called()

    def test_bridge_errors_are_mcp_errors_without_reclassifying_nested_data(self) -> None:
        with mock.patch.object(SERVER, "_call_http", return_value=json.dumps({"error": "connection refused"})):
            self.assertTrue(SERVER.execute_tool("system_ping", {})["isError"])
            self.assertTrue(SERVER.execute_tool("system_capabilities", {})["isError"])
        self.assertFalse(SERVER._tool_response(json.dumps({"sections": {"error": "partial"}}))["isError"])

    def test_sequence_validates_later_steps_before_bridge_calls(self) -> None:
        with mock.patch.object(SERVER, "_call_diagnostics_method") as bridge:
            result = SERVER.execute_tool("automation_run_sequence", {"steps": [
                {"tool": "diagnostics_get_game_time_scale"},
                {"tool": "system_ping"},
            ]})
        self.assertTrue(result["isError"])
        bridge.assert_not_called()

    def test_renderer_frame_probe_tools_dispatch_without_image_capture(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_diagnostics_method(method: str, args: list[object] | None = None) -> str:
            calls.append((method, list(args or [])))
            return json.dumps({"success": True, "kind": "rendererFrameProbe", "pixelCapture": False})

        schemas = {tool["name"]: tool["inputSchema"] for tool in SERVER.TOOLS}
        tools = {tool["name"]: tool for tool in SERVER.TOOLS}
        self.assertIn("diagnostics_start_renderer_frame_probe", schemas)
        self.assertIn("diagnostics_read_renderer_frame_probe", schemas)
        self.assertIn("diagnostics_stop_renderer_frame_probe", schemas)
        self.assertIn(
            "RenderPipelineManager.endCameraRendering",
            tools["diagnostics_start_renderer_frame_probe"]["description"],
        )
        self.assertEqual(
            ["entityKeyId"],
            schemas["diagnostics_start_renderer_frame_probe"]["required"],
        )
        self.assertIn("sampleClipContains", schemas["diagnostics_start_renderer_frame_probe"]["properties"])
        self.assertIn("sampleMovementState", schemas["diagnostics_start_renderer_frame_probe"]["properties"])
        self.assertEqual(
            ["Player", "AI"],
            schemas["diagnostics_spawn_entity"]["properties"]["side"]["enum"],
        )

        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=fake_diagnostics_method):
            started = SERVER.execute_tool(
                "diagnostics_start_renderer_frame_probe",
                {
                    "entityKeyId": "307",
                    "rendererNameContains": "Body",
                    "sampleLimit": 8,
                    "sampleClipContains": "Action_Walk",
                    "sampleMovementState": "moving",
                },
            )
            read = SERVER.execute_tool("diagnostics_read_renderer_frame_probe", {"token": "probe-token"})
            stopped = SERVER.execute_tool("diagnostics_stop_renderer_frame_probe", {"token": "probe-token"})
            invalid = SERVER.execute_tool(
                "diagnostics_start_renderer_frame_probe",
                {"entityKeyId": "307", "sampleLimit": "eight"},
            )
            invalid_filter = SERVER.execute_tool(
                "diagnostics_start_renderer_frame_probe",
                {"entityKeyId": "307", "sampleMovementState": "running"},
            )

        self.assertEqual(
            [
                ("StartRendererFrameProbeJson", ["307", "Body", 8, "Action_Walk", "moving"]),
                ("ReadRendererFrameProbeJson", ["probe-token"]),
                ("StopRendererFrameProbeJson", ["probe-token"]),
            ],
            calls,
        )
        for response in (started, read, stopped):
            self.assertFalse(response["isError"])
            self.assertFalse(json.loads(response["content"][0]["text"])["pixelCapture"])
        self.assertTrue(invalid["isError"])
        self.assertTrue(invalid_filter["isError"])

    def test_wait_for_renderer_frame_evidence_matches_distinct_frames_and_stops_probe(self) -> None:
        calls: list[tuple[str, list[object]]] = []
        samples = []
        for frame in (101, 102):
            samples.append({
                "frame": frame,
                "cameraRenderCallbackObserved": True,
                "cameraTargetTextureAssigned": False,
                "rendererEnabled": True,
                "rendererIsVisible": True,
                "boundsIntersectsCameraFrustum": True,
                "layerIncludedByCameraCullingMask": True,
                "activeClip": "IroncladWalker_Action_Walk",
                "isMoving": True,
                "deformationDeltaAvailable": True,
                "maximumLocalVertexDelta": 0.25,
                "retargetPresent": True,
                "retargetBound": True,
                "retargetSuspended": False,
                "boundMappingCount": 8,
            })
        probe = {"success": True, "status": "active", "token": "probe-token", "pixelCapture": False, "renderers": [{"name": "Body", "samples": samples}]}
        stopped_probe = {**probe, "status": "stopped"}

        def fake_diagnostics_method(method: str, args: list[object] | None = None) -> str:
            calls.append((method, list(args or [])))
            if method == "StartRendererFrameProbeJson":
                return json.dumps({"success": True, "token": "probe-token", "pixelCapture": False})
            if method == "ReadRendererFrameProbeJson":
                return json.dumps(probe)
            if method == "StopRendererFrameProbeJson":
                return json.dumps(stopped_probe)
            raise AssertionError(method)

        schemas = {tool["name"]: tool["inputSchema"] for tool in SERVER.TOOLS}
        self.assertIn("diagnostics_wait_for_renderer_frame_evidence", schemas)
        self.assertEqual(["entityKeyId"], schemas["diagnostics_wait_for_renderer_frame_evidence"]["required"])
        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=fake_diagnostics_method):
            response = SERVER.execute_tool("diagnostics_wait_for_renderer_frame_evidence", {
                "entityKeyId": "254",
                "activeClipContains": "Action_Walk",
                "requiredEvidence": ["moving", "deformation", "retargetBound", "visibleToCamera"],
                "minimumMatchingFrames": 2,
                "timeoutSeconds": 1,
            })

        payload = json.loads(response["content"][0]["text"])
        self.assertFalse(response["isError"])
        self.assertTrue(payload["success"])
        self.assertTrue(payload["conditionMet"])
        self.assertTrue(payload["probeStopped"])
        self.assertEqual(2, payload["matchingFrameCount"])
        self.assertEqual([101, 102], [row["frame"] for row in payload["matchingFrames"]])
        self.assertEqual(["moving", "deformation", "retargetBound", "visibleToCamera"], payload["requiredEvidence"])
        self.assertFalse(payload["pixelCapture"])
        self.assertFalse(payload["computerUseRequired"])
        self.assertEqual([
            ("StartRendererFrameProbeJson", ["254", "", 12, "Action_Walk", "moving"]),
            ("ReadRendererFrameProbeJson", ["probe-token"]),
            ("StopRendererFrameProbeJson", ["probe-token"]),
        ], calls)

    def test_wait_for_renderer_frame_evidence_times_out_but_still_removes_observers(self) -> None:
        calls: list[str] = []
        probe = {"success": True, "status": "active", "renderers": [{"name": "Body", "samples": [{
            "frame": 103,
            "cameraRenderCallbackObserved": True,
            "cameraTargetTextureAssigned": False,
            "rendererEnabled": True,
            "activeClip": "IroncladWalker_Action_Walk",
            "isMoving": True,
        }]}]}
        stopped_probe = {**probe, "status": "stopped"}

        def fake_diagnostics_method(method: str, _args: list[object] | None = None) -> str:
            calls.append(method)
            if method == "StartRendererFrameProbeJson":
                return json.dumps({"success": True, "token": "probe-token"})
            if method == "ReadRendererFrameProbeJson":
                return json.dumps(probe)
            if method == "StopRendererFrameProbeJson":
                return json.dumps(stopped_probe)
            raise AssertionError(method)

        clock_value = [0.0]
        def fake_monotonic() -> float:
            clock_value[0] += 0.3
            return clock_value[0]

        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=fake_diagnostics_method), \
                mock.patch.object(SERVER.time, "monotonic", side_effect=fake_monotonic), \
                mock.patch.object(SERVER.time, "sleep", return_value=None):
            response = SERVER.execute_tool("diagnostics_wait_for_renderer_frame_evidence", {
                "entityKeyId": "254",
                "activeClipContains": "Action_Death",
                "requiredEvidence": ["moving"],
                "timeoutSeconds": 1,
            })

        payload = json.loads(response["content"][0]["text"])
        self.assertFalse(response["isError"])
        self.assertFalse(payload["success"])
        self.assertFalse(payload["conditionMet"])
        self.assertTrue(payload["timedOut"])
        self.assertTrue(payload["probeStopped"])
        self.assertIn("StopRendererFrameProbeJson", calls)

    def test_wait_confirms_observer_removal_when_stop_token_is_missing(self) -> None:
        samples = []
        for frame in (201, 202):
            samples.append({
                "frame": frame,
                "cameraRenderCallbackObserved": True,
                "cameraTargetTextureAssigned": False,
                "rendererEnabled": True,
                "rendererIsVisible": True,
                "boundsIntersectsCameraFrustum": True,
                "layerIncludedByCameraCullingMask": True,
                "activeClip": "IroncladWalker_Action_Walk",
                "isMoving": True,
                "deformationDeltaAvailable": True,
                "maximumLocalVertexDelta": 0.1,
                "retargetPresent": True,
                "retargetBound": True,
                "retargetSuspended": False,
                "boundMappingCount": 8,
            })
        probe = {"success": True, "status": "active", "token": "probe-token", "renderers": [{"name": "Body", "samples": samples}]}

        def fake_diagnostics_method(method: str, _args: list[object] | None = None) -> str:
            if method == "StartRendererFrameProbeJson":
                return json.dumps({"success": True, "token": "probe-token"})
            if method == "ReadRendererFrameProbeJson":
                return json.dumps(probe)
            if method == "StopRendererFrameProbeJson":
                return json.dumps({"success": False, "error": "Probe token was not found or has expired."})
            raise AssertionError(method)

        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=fake_diagnostics_method), \
                mock.patch.object(SERVER, "_call_http", return_value="[]"):
            response = SERVER.execute_tool("diagnostics_wait_for_renderer_frame_evidence", {
                "entityKeyId": "254",
                "activeClipContains": "Action_Walk",
                "requiredEvidence": ["moving", "deformation", "retargetBound", "visibleToCamera"],
                "minimumMatchingFrames": 2,
                "timeoutSeconds": 1,
            })

        payload = json.loads(response["content"][0]["text"])
        self.assertTrue(payload["conditionMet"])
        self.assertTrue(payload["probeStopped"])
        self.assertTrue(payload["success"])
        self.assertEqual(0, payload["probeCleanupVerification"]["remainingObserverCount"])
        self.assertIn("stopWarning", payload)

    def test_wait_for_renderer_frame_evidence_rejects_invalid_thresholds_before_start(self) -> None:
        with mock.patch.object(SERVER, "_call_diagnostics_method") as bridge_call:
            response = SERVER.execute_tool("diagnostics_wait_for_renderer_frame_evidence", {
                "entityKeyId": "254",
                "timeoutSeconds": 21,
            })
        self.assertTrue(response["isError"])
        self.assertIn("timeoutSeconds", response["content"][0]["text"])
        bridge_call.assert_not_called()

    def test_sequence_budget_expiry_still_stops_renderer_probe(self) -> None:
        calls: list[str] = []

        def fake_diagnostics_method(method: str, _args: list[object] | None = None) -> str:
            calls.append(method)
            if method == "StartRendererFrameProbeJson":
                return json.dumps({"success": True, "token": "probe-token"})
            if method == "StopRendererFrameProbeJson":
                return json.dumps({"success": True, "status": "stopped", "renderers": [], "pixelCapture": False})
            raise AssertionError(f"Unexpected bridge call after the request budget expired: {method}")

        clock_value = [0.0]
        def fake_monotonic() -> float:
            clock_value[0] += 0.3
            return clock_value[0]

        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=fake_diagnostics_method), \
                mock.patch.object(SERVER.time, "monotonic", side_effect=fake_monotonic), \
                mock.patch.object(SERVER.time, "sleep", return_value=None):
            response = SERVER.execute_tool("automation_run_sequence", {
                "steps": [{
                    "tool": "diagnostics_wait_for_renderer_frame_evidence",
                    "arguments": {"entityKeyId": "254", "timeoutSeconds": 20},
                }],
                "maxDurationSeconds": 1,
            })

        payload = json.loads(response["content"][0]["text"])
        step_result = payload["results"][0]["result"]
        self.assertTrue(response["isError"])
        self.assertTrue(payload["results"][0]["budgetExceeded"])
        self.assertFalse(step_result["conditionMet"])
        self.assertTrue(step_result["timedOut"])
        self.assertTrue(step_result["probeStopped"])
        self.assertEqual(["StartRendererFrameProbeJson", "StopRendererFrameProbeJson"], calls)

    def test_automation_sequence_resolves_typed_values_from_prior_success(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_diagnostics_method(method: str, args: list[object] | None = None) -> str:
            call_args = list(args or [])
            calls.append((method, call_args))
            if method == "SpawnEntityJson":
                return json.dumps({"success": True, "entityKeyId": "sample-native:assault-walker#254", "spawnedX": -45.5})
            if method == "OrderUnitMoveJson":
                return json.dumps({"success": True, "entityKeyId": call_args[0], "targetX": call_args[1], "targetZ": call_args[2]})
            if method == "StartRendererFrameProbeJson":
                return json.dumps({"success": True, "token": "probe-token", "pixelCapture": False})
            if method == "ReadRendererFrameProbeJson":
                return json.dumps({"success": True, "renderers": [{"name": "Body", "samples": [{
                    "frame": 101,
                    "cameraRenderCallbackObserved": True,
                    "cameraTargetTextureAssigned": False,
                    "rendererEnabled": True,
                    "activeClip": "IroncladWalker_Action_Walk",
                    "isMoving": True,
                    "retargetPresent": True,
                    "retargetBound": True,
                    "retargetSuspended": False,
                    "boundMappingCount": 8,
                }]}]})
            if method == "StopRendererFrameProbeJson":
                return json.dumps({"success": True, "status": "stopped", "renderers": [{"name": "Body", "samples": [{
                    "frame": 101,
                    "cameraRenderCallbackObserved": True,
                    "cameraTargetTextureAssigned": False,
                    "rendererEnabled": True,
                    "activeClip": "IroncladWalker_Action_Walk",
                    "isMoving": True,
                    "retargetPresent": True,
                    "retargetBound": True,
                    "retargetSuspended": False,
                    "boundMappingCount": 8,
                }]}], "pixelCapture": False})
            raise AssertionError(method)

        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=fake_diagnostics_method):
            response = SERVER.execute_tool("automation_run_sequence", {
                "steps": [
                    {
                        "tool": "diagnostics_spawn_entity",
                        "arguments": {"entityId": "sample-native:assault-walker", "side": "Player", "targetX": 12, "targetZ": 34},
                    },
                    {
                        "tool": "diagnostics_order_unit_move",
                        "arguments": {
                            "entityKeyId": {"$ref": {"step": 0, "path": "/entityKeyId"}},
                            "destinationX": {"$ref": {"step": 0, "path": "/spawnedX"}},
                            "destinationZ": 90,
                        },
                    },
                    {
                        "tool": "diagnostics_wait_for_renderer_frame_evidence",
                        "arguments": {
                            "entityKeyId": {"$ref": {"step": 0, "path": "/entityKeyId"}},
                            "timeoutSeconds": 2,
                            "activeClipContains": "Action_Walk",
                            "requiredEvidence": ["moving", "retargetBound"],
                        },
                    },
                ],
            })

        payload = json.loads(response["content"][0]["text"])
        self.assertFalse(response["isError"])
        self.assertTrue(payload["success"])
        self.assertEqual(3, payload["completedSteps"])
        self.assertEqual(("OrderUnitMoveJson", ["sample-native:assault-walker#254", -45.5, 90.0]), calls[1])
        self.assertEqual(("StartRendererFrameProbeJson", ["sample-native:assault-walker#254", "", 12, "Action_Walk", "moving"]), calls[2])
        self.assertEqual(("StopRendererFrameProbeJson", ["probe-token"]), calls[-1])
        self.assertTrue(payload["results"][2]["result"]["conditionMet"])
        self.assertTrue(payload["results"][2]["result"]["probeStopped"])

    def test_automation_sequence_rejects_forward_or_failed_step_references(self) -> None:
        calls: list[str] = []
        def fake_diagnostics_method(method: str, _args: list[object] | None = None) -> str:
            calls.append(method)
            if method == "OrderUnitMoveJson":
                return json.dumps({"success": False, "error": "entity not found"})
            raise AssertionError(f"unexpected bridge call: {method}")

        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=fake_diagnostics_method):
            forward = SERVER.execute_tool("automation_run_sequence", {
                "steps": [{
                    "tool": "diagnostics_order_unit_move",
                    "arguments": {
                        "entityKeyId": {"$ref": {"step": 1, "path": "/entityKeyId"}},
                        "destinationX": 1,
                        "destinationZ": 2,
                    },
                }, {"tool": "diagnostics_get_game_time_scale"}],
            })
            failed_source = SERVER.execute_tool("automation_run_sequence", {
                "stopOnFailure": False,
                "steps": [
                    {"tool": "diagnostics_order_unit_move", "arguments": {"entityKeyId": "missing-unit", "destinationX": 1, "destinationZ": 2}},
                    {
                        "tool": "diagnostics_order_unit_move",
                        "arguments": {
                            "entityKeyId": {"$ref": {"step": 0, "path": "/entityKeyId"}},
                            "destinationX": 1,
                            "destinationZ": 2,
                        },
                    },
                ],
            })

        forward_payload = json.loads(forward["content"][0]["text"])
        failed_payload = json.loads(failed_source["content"][0]["text"])
        self.assertTrue(forward["isError"])
        self.assertIn("earlier step", forward_payload["error"])
        self.assertFalse(failed_payload["results"][1]["success"])
        self.assertIn("did not succeed", failed_payload["results"][1]["error"])
        self.assertEqual(["OrderUnitMoveJson"], calls)

    def test_later_invalid_nested_reference_blocks_all_steps(self) -> None:
        with mock.patch.object(SERVER, "_call_diagnostics_method") as bridge:
            response = SERVER.execute_tool("automation_run_sequence", {"steps": [
                {"tool": "diagnostics_order_unit_move", "arguments": {"entityKeyId": "unit", "destinationX": 1, "destinationZ": 2}},
                {"tool": "diagnostics_order_unit_move", "arguments": {"nested": [{"$ref": {"step": 1, "path": "/id"}}]}},
            ]})
        self.assertTrue(response["isError"])
        self.assertIn("earlier step", json.loads(response["content"][0]["text"])["error"])
        bridge.assert_not_called()

    def test_native_michsky_button_activation_checks_target_then_invokes_callback(self) -> None:
        calls: list[tuple[str, dict[str, object]]] = []
        item = {
            "name": "Continue Run",
            "id": 57,
            "scene": "MainMenu",
            "path": "/Canvas%20-%20Main%20Menu[0]/Main%20Content[7]/Continue%20Run[0]",
            "active": True,
        }
        details = {
            "buttonText": {"type": "System.String", "value": "Continuar la partida"},
            "enabled": {"type": "System.Boolean", "value": "True"},
            "isActiveAndEnabled": {"type": "System.Boolean", "value": "True"},
            "isInteractable": {"type": "System.Boolean", "value": "True"},
        }

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            captured = dict(params or {})
            calls.append((endpoint, captured))
            if endpoint == "scene/search_gameobjects":
                return json.dumps({"items": [item], "truncated": False})
            if endpoint == "component/get_details":
                return json.dumps(details)
            if endpoint == "component/call_method":
                return json.dumps({"status": "ok", "message": "Called InvokeOnClick with 0 args."})
            self.fail(f"Unexpected bridge endpoint: {endpoint}")

        schemas = {tool["name"]: tool["inputSchema"] for tool in SERVER.TOOLS}
        self.assertIn("diagnostics_ui_invoke_native_button", schemas)
        native_tool = next(tool for tool in SERVER.TOOLS if tool["name"] == "diagnostics_ui_invoke_native_button")
        self.assertIn("live active scene", native_tool["description"])
        self.assertEqual(
            ["selector", "scene", "expectedButtonText"],
            schemas["diagnostics_ui_invoke_native_button"]["required"],
        )
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value=json.dumps({"activeSceneName": "MainMenu"})), mock.patch.object(SERVER, "_call_http", side_effect=fake_http):
            response = SERVER.execute_tool(
                "diagnostics_ui_invoke_native_button",
                {
                    "selector": "Continue Run",
                    "scene": "MainMenu",
                    "expectedButtonText": "Continuar la partida",
                    "hierarchyPath": "MainMenu::Canvas - Main Menu/Main Content/Continue Run",
                },
            )

        result = json.loads(response["content"][0]["text"])
        self.assertFalse(response["isError"])
        self.assertTrue(result["activated"])
        self.assertEqual("Michsky.UI.Reach.ButtonManager.InvokeOnClick", result["activation"])
        self.assertEqual("Continue Run", result["target"]["name"])
        self.assertEqual(57, result["target"]["id"])
        self.assertEqual(
            ("component/call_method", {"id": 57, "componentName": "Michsky.UI.Reach.ButtonManager", "methodName": "InvokeOnClick", "args": "[]"}),
            calls[-1],
        )

    def test_native_michsky_button_activation_fails_closed_for_ambiguous_or_disabled_targets(self) -> None:
        calls: list[tuple[str, dict[str, object]]] = []
        items = [
            {"name": "ChooseButton", "id": 71, "scene": "MainMenu", "path": "/SelectSavegameOverlay[9]/SaveSlot1[0]/ChooseButton[0]", "active": True},
            {"name": "ChooseButton", "id": 72, "scene": "MainMenu", "path": "/SelectSavegameOverlay[9]/SaveSlot2[1]/ChooseButton[0]", "active": True},
        ]
        details = {
            "buttonText": {"type": "System.String", "value": "Elegir"},
            "enabled": {"type": "System.Boolean", "value": "True"},
            "isActiveAndEnabled": {"type": "System.Boolean", "value": "True"},
            "isInteractable": {"type": "System.Boolean", "value": "True"},
        }

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            captured = dict(params or {})
            calls.append((endpoint, captured))
            if endpoint == "scene/search_gameobjects":
                return json.dumps({"items": items, "truncated": False})
            if endpoint == "component/get_details":
                return json.dumps(details)
            if endpoint == "component/call_method":
                return json.dumps({"status": "ok"})
            self.fail(f"Unexpected bridge endpoint: {endpoint}")

        args = {"selector": "ChooseButton", "scene": "MainMenu", "expectedButtonText": "Elegir"}
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value=json.dumps({"activeSceneName": "MainMenu"})), mock.patch.object(SERVER, "_call_http", side_effect=fake_http):
            ambiguous = SERVER.execute_tool("diagnostics_ui_invoke_native_button", args)
            self.assertFalse(ambiguous["isError"])
            self.assertEqual(2, json.loads(ambiguous["content"][0]["text"])["matchCount"])
            self.assertFalse(any(endpoint == "component/call_method" for endpoint, _ in calls))

            calls.clear()
            details["isInteractable"]["value"] = "False"
            disabled = SERVER.execute_tool(
                "diagnostics_ui_invoke_native_button",
                {**args, "hierarchyPath": "MainMenu::SelectSavegameOverlay/SaveSlot2/ChooseButton"},
            )

        disabled_result = json.loads(disabled["content"][0]["text"])
        self.assertFalse(disabled["isError"])
        self.assertFalse(disabled_result["success"])
        self.assertIn("not interactable", disabled_result["message"])
        self.assertFalse(any(endpoint == "component/call_method" for endpoint, _ in calls))

        calls.clear()
        details["isInteractable"]["value"] = "True"
        details.pop("isActiveAndEnabled")
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value=json.dumps({"activeSceneName": "MainMenu"})), mock.patch.object(SERVER, "_call_http", side_effect=fake_http):
            missing_active_state = SERVER.execute_tool(
                "diagnostics_ui_invoke_native_button",
                {**args, "hierarchyPath": "MainMenu::SelectSavegameOverlay/SaveSlot2/ChooseButton"},
            )

        missing_active_result = json.loads(missing_active_state["content"][0]["text"])
        self.assertFalse(missing_active_state["isError"])
        self.assertFalse(missing_active_result["success"])
        self.assertIn("inactive, disabled, or not interactable", missing_active_result["message"])
        self.assertFalse(any(endpoint == "component/call_method" for endpoint, _ in calls))

        calls.clear()
        with mock.patch.object(SERVER, "_call_diagnostics_method", return_value=json.dumps({"activeSceneName": "Game"})), mock.patch.object(SERVER, "_call_http", side_effect=fake_http):
            inactive_scene = SERVER.execute_tool("diagnostics_ui_invoke_native_button", args)

        inactive_scene_result = json.loads(inactive_scene["content"][0]["text"])
        self.assertFalse(inactive_scene["isError"])
        self.assertFalse(inactive_scene_result["success"])
        self.assertEqual("Game", inactive_scene_result["activeScene"])
        self.assertEqual([], calls)

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
                return json.dumps({"items": [{"name": "LoaderDiagnostics", "gameObjectId": 88}], "truncated": False})
            self.fail(f"Unexpected bridge call: {endpoint} {params}")

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", None),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resolved_id = SERVER._get_diagnostics_go_id()
            self.assertEqual(88, resolved_id)
            self.assertEqual(88, SERVER._DIAGNOSTICS_GO_ID)

        self.assertEqual(["scene:find_objects_with_component", "scene/search_gameobjects"], calls)

    def test_diagnostics_discovery_reads_wrapped_primary_component_results(self) -> None:
        calls: list[str] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            calls.append(endpoint)
            self.assertEqual("scene:find_objects_with_component", endpoint)
            self.assertEqual("LoaderDiagnosticsComponent", (params or {}).get("componentName"))
            return json.dumps({
                "items": [{"name": "LoaderDiagnostics", "id": -4880}],
                "truncated": False,
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", None),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            resolved_id = SERVER._get_diagnostics_go_id()

        self.assertEqual(-4880, resolved_id)
        self.assertEqual(["scene:find_objects_with_component"], calls)

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
        self.assertEqual(49, len(snapshot))
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

    def test_fast_snapshot_gui_and_intermission_presets_include_accessible_ui(self) -> None:
        expected_calls = {
            "gui": {
                "GetGuiStateJson": [],
                "GetGameTimeScaleJson": [],
                "GetCameraCensusJson": [],
                "GetRuntimeHooksStateJson": [],
                "GetUiAccessibilityCensusJson": ["", False, 100],
            },
            "intermission": {
                "GetRunProgressionCensusJson": [],
                "GetIntermissionCensusJson": [64],
                "GetIntermissionGraphJson": ["", 8, 64],
                "GetIntermissionRewardStateJson": [32],
                "GetIntermissionNativePanelStateJson": [32],
                "GetNavigatorStateJson": [],
                "GetUiAccessibilityCensusJson": ["", False, 100],
            },
        }

        for preset, expected in expected_calls.items():
            with self.subTest(preset=preset):
                calls: list[tuple[str, list[object]]] = []

                def fake_diagnostics_method(method: str, args: list[object] | None = None) -> str:
                    calls.append((method, list(args or [])))
                    return json.dumps({"kind": method})

                with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=fake_diagnostics_method):
                    response = SERVER.execute_tool("diagnostics_fast_snapshot", {"preset": preset})

                snapshot = json.loads(response["content"][0]["text"])
                self.assertFalse(response["isError"])
                self.assertEqual(expected, dict(calls))
                self.assertEqual("bestEffort", snapshot["_meta"]["consistency"])
                self.assertEqual(len(expected), snapshot["_meta"]["count"])
                self.assertNotIn("OrderEnterLevelNodeJson", dict(calls))
                self.assertNotIn("OrderChooseIntermissionRewardJson", dict(calls))

        tool = next(tool for tool in SERVER.TOOLS if tool["name"] == "diagnostics_fast_snapshot")
        self.assertEqual(
            ["gui", "match", "combat", "health", "intermission"],
            tool["inputSchema"]["properties"]["preset"]["enum"],
        )
        self.assertIn("best-effort", tool["description"])

    def test_accessible_ui_tool_catalog_documents_native_pointer_controls(self) -> None:
        tools = {tool["name"]: tool for tool in SERVER.TOOLS}
        for name in (
            "diagnostics_get_ui_accessibility_census",
            "diagnostics_ui_click_text",
            "diagnostics_wait_for_ui_text",
            "diagnostics_get_runtime_control_surface",
        ):
            with self.subTest(tool=name):
                self.assertIn("IPointerClickHandler", tools[name]["description"])

        self.assertIn("scene-qualified", tools["diagnostics_get_ui_accessibility_census"]["description"])
        self.assertIn("EventSystem", tools["diagnostics_ui_click_text"]["description"])

    def test_ui_census_source_limits_native_pointer_controls_to_canvas_ui(self) -> None:
        repository = Path(__file__).resolve().parents[2]
        source = (repository / "src/RogueCommandExpansionLoader/Runtime/RuntimeUiAccessibility.cs").read_text(encoding="utf-8")
        self.assertIn("behaviour is IPointerClickHandler && IsCanvasUiControl(behaviour)", source)
        self.assertIn("behaviour.GetComponent<RectTransform>() == null", source)
        self.assertIn("current.GetComponent<Canvas>() != null", source)
        self.assertIn("RuntimeUiClickDirector.TryClickGameObject(target)", source)

    def test_runtime_control_surface_dispatch_is_bounded_read_only_and_typed(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_diagnostics_method(method: str, args: list[object] | None = None) -> str:
            calls.append((method, list(args or [])))
            return json.dumps({"kind": "runtimeControlSurface", "activeSceneName": "IntermissionScreen"})

        with mock.patch.object(SERVER, "_call_diagnostics_method", side_effect=fake_diagnostics_method):
            response = SERVER.execute_tool(
                "diagnostics_get_runtime_control_surface",
                {"query": "Continue", "includeInactive": True, "maxUiResults": 1000},
            )

        self.assertFalse(response["isError"])
        self.assertEqual(
            [("GetRuntimeControlSurfaceJson", ["Continue", True, 100])],
            calls,
        )
        tool = next(tool for tool in SERVER.TOOLS if tool["name"] == "diagnostics_get_runtime_control_surface")
        self.assertEqual({"query", "includeInactive", "maxUiResults"}, set(tool["inputSchema"]["properties"]))
        self.assertIn("single-Unity-thread", tool["description"])

        for invalid in (
            {"includeInactive": "yes"},
            {"maxUiResults": True},
            {"maxUiResults": "many"},
        ):
            with self.subTest(invalid=invalid):
                rejected = SERVER.execute_tool("diagnostics_get_runtime_control_surface", invalid)
                self.assertTrue(rejected["isError"])
        self.assertEqual(1, len(calls), "invalid arguments must not call Unity")

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
        with tempfile.TemporaryDirectory() as tmp, mock.patch.dict(os.environ, {"LOCALAPPDATA": tmp}):
            configs = SERVER.generate_agent_configs(Path(tmp) / "repo")
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
            "diagnostics_order_place_building",
            "diagnostics_inspect_active_building_placement",
            "diagnostics_find_building_placement",
            "diagnostics_run_synthetic_gameplay_scenario",
            "diagnostics_get_control_guide",
            "diagnostics_gui_control",
            "diagnostics_ui_invoke_native_button",
            "diagnostics_ui_click",
            "diagnostics_ui_press_button",
            "diagnostics_get_intermission_census",
            "diagnostics_get_intermission_native_panel_state",
            "diagnostics_order_buy_intermission_shop_offer",
            "diagnostics_order_reroll_intermission_shop",
            "diagnostics_order_remove_intermission_card",
            "diagnostics_order_cancel_intermission_card_removal",
            "diagnostics_order_enter_level",
            "diagnostics_order_select_level_node",
            "diagnostics_get_navigator_state",
            "diagnostics_order_navigator_pause",
            "diagnostics_fast_snapshot",
            "diagnostics_get_runtime_control_surface",
            "diagnostics_get_upgrade_census",
            "diagnostics_get_relic_census",
            "diagnostics_order_apply_visual_variant",
            "diagnostics_get_visual_variant_census",
            "discover_tools",
            "describe_tool",
        ):
            self.assertIn(expected, names)
        self.assertEqual(249, len(names))

    def test_initialize_reports_current_bridge_version(self) -> None:
        response = SERVER.handle_request({
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": {"protocolVersion": "2024-11-05"},
        })
        self.assertEqual("1.6.0", response["result"]["serverInfo"]["version"])

    def test_tools_list_default_is_all_and_paginates(self) -> None:
        full = SERVER.handle_request({"jsonrpc": "2.0", "id": 1, "method": "tools/list", "params": {}})
        tools = full["result"]["tools"]
        self.assertEqual(249, len(tools))
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
        self.assertEqual(len(resp2["result"]["tools"]), 229)
        self.assertLess(len(json.dumps(resp2["result"], separators=(",", ":")).encode("utf-8")), 91000)
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

    def test_diagnostics_order_place_building_routes_read_only_preflight(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            self.assertEqual("component/call_method", endpoint)
            params = params or {}
            method = str(params.get("methodName", ""))
            args = json.loads(str(params.get("args", "[]")))
            calls.append((method, args))
            payload = {
                "success": True,
                "committed": False,
                "entityId": "tactical-operations:field-medic-rover-factory",
                "method": "native-PlaceBuildings-coroutine",
            }
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with {len(args)} args. ReturnValue: {json.dumps(payload)}"
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool("diagnostics_order_place_building", {
                "worldX": -50.0,
                "worldZ": -120.0,
                "commit": False,
            })

        self.assertFalse(response["isError"])
        result = json.loads(response["content"][0]["text"])
        self.assertTrue(result["success"])
        self.assertFalse(result["committed"])
        self.assertEqual("OrderPlaceBuildingJson", calls[0][0])
        self.assertEqual([-50.0, -120.0, False], calls[0][1])

    def test_diagnostics_inspect_active_building_placement_uses_exact_native_ghost(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            self.assertEqual("component/call_method", endpoint)
            params = params or {}
            method = str(params.get("methodName", ""))
            args = json.loads(str(params.get("args", "[]")))
            calls.append((method, args))
            payload = {
                "success": False,
                "committed": False,
                "entityId": "tactical-operations:field-medic-rover-factory",
                "worldX": 5.0,
                "worldZ": -145.0,
                "cellDiagnostics": [{"gridX": 0, "gridY": -15, "multiLayer": True}],
            }
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with {len(args)} args. ReturnValue: {json.dumps(payload)}"
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool("diagnostics_inspect_active_building_placement", {})

        self.assertFalse(response["isError"])
        result = json.loads(response["content"][0]["text"])
        self.assertFalse(result["success"])
        self.assertFalse(result["committed"])
        self.assertEqual("InspectActiveBuildingPlacementJson", calls[0][0])
        self.assertEqual([], calls[0][1])
        self.assertTrue(result["cellDiagnostics"][0]["multiLayer"])

    def test_diagnostics_find_building_placement_routes_bounded_read_only_search(self) -> None:
        calls: list[tuple[str, list[object]]] = []

        def fake_http(endpoint: str, params: dict[str, object] | None = None, **_: object) -> str:
            self.assertEqual("component/call_method", endpoint)
            params = params or {}
            method = str(params.get("methodName", ""))
            args = json.loads(str(params.get("args", "[]")))
            calls.append((method, args))
            payload = {
                "found": True,
                "radiusCells": args[2],
                "candidatesChecked": 12,
                "candidate": {"success": True, "committed": False, "cellDiagnostics": []},
            }
            return json.dumps({
                "status": "ok",
                "message": f"Called '{method}' with {len(args)} args. ReturnValue: {json.dumps(payload)}"
            })

        with (
            mock.patch.object(SERVER, "_DIAGNOSTICS_GO_ID", 73),
            mock.patch.object(SERVER, "_call_http", side_effect=fake_http),
        ):
            response = SERVER.execute_tool("diagnostics_find_building_placement", {
                "originX": 0.0,
                "originZ": -120.0,
                "radiusCells": 8,
            })
            invalid_radius = SERVER.execute_tool("diagnostics_find_building_placement", {
                "originX": 0.0,
                "originZ": -120.0,
                "radiusCells": 13,
            })
            invalid_origin = SERVER.execute_tool("diagnostics_find_building_placement", {
                "originX": float("nan"),
                "originZ": -120.0,
            })

        self.assertFalse(response["isError"])
        result = json.loads(response["content"][0]["text"])
        self.assertTrue(result["found"])
        self.assertFalse(result["candidate"]["committed"])
        self.assertEqual("FindBuildingPlacementJson", calls[0][0])
        self.assertEqual([0.0, -120.0, 8], calls[0][1])
        self.assertTrue(invalid_radius["isError"])
        self.assertTrue(invalid_origin["isError"])
        self.assertEqual(1, len(calls), "invalid arguments must not reach the runtime bridge")

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
        self.assertEqual(249, len(names))
        self.assertEqual(len(names), len(set(names)))
        aliases = [s for s in specs if str(s.get("description", "")).startswith("Alias for")]
        self.assertEqual(20, len(aliases))
        self.assertEqual(229, len(specs) - len(aliases))
        compact = sum(len(json.dumps(s, ensure_ascii=False, separators=(",", ":"))) for s in specs)
        self.assertLess(compact, 96000, f"optimised schemas too large: {compact} bytes")
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
                self.assertTrue(pval.get("type") in ("string", "integer", "number", "boolean", "array", "object") or pval.get("oneOf"), f"{spec['name']}.{pname}")
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
        self.assertEqual(414, total_props)

    def test_empty_schemas_are_compact_but_strict(self) -> None:
        specs = self._load_tool_specs()
        empty = [s for s in specs if not s["parameters"].get("properties")]
        self.assertEqual(83, len(empty))
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
        stage_association = by_desc["diagnostics_get_active_stage_ai_association"]
        self.assertIn("bounded captured stage order", stage_association)
        self.assertIn("separately from live initialized loader AI bindings", stage_association)

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
        self.assertEqual(249, len(tools))
        payload = json.dumps(resp["result"], separators=(",", ":"))
        self.assertLess(len(payload), 96000, f"tools/list payload too large: {len(payload)} bytes")

if __name__ == "__main__":
    unittest.main()
