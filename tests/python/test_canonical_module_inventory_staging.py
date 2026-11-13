from __future__ import annotations

import ast
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from tools.canonical_module_inventory_staging import (
    ModelBudgetError,
    sha256_file,
    stage_candidate_inventory,
    validate_candidate_triangle_count,
)
from tools.profile_model_budgets import _read_samples


class CanonicalModuleInventoryStagingTests(unittest.TestCase):
    def _write_profile_and_bundle(self, root: Path, *, capacity_eligible: bool = True) -> tuple[Path, Path]:
        camera = {
            "name": "Main Camera",
            "pixelWidth": 1920,
            "pixelHeight": 1080,
            "cameraType": "Game",
            "usesRenderTexture": False,
            "fieldOfView": 26.0,
            "orthographic": False,
            "orthographicSize": 42.0,
            "aspect": 1920.0 / 1080.0,
            "cullingMask": -1,
            "position": [0.0, 10.0, 20.0],
            "rotationEuler": [45.0, 0.0, 0.0],
        }
        scene_reference = {"sceneId": "dense-gameplay", "unitySceneName": "Game", "level": 4}
        scene_mix = "fixed entity population and camera in dense gameplay"
        context = {
            "sceneReference": scene_reference,
            "cameraReference": {key: camera[key] for key in (
                "name", "pixelWidth", "pixelHeight", "fieldOfView", "orthographic",
                "orthographicSize", "aspect", "cullingMask", "position", "rotationEuler",
            )},
            "sceneMix": scene_mix,
            "maximumVisibleInstances": 4,
            "capacityEligible": capacity_eligible,
            "capturePairing": {"status": "controlled-geometry-load" if capacity_eligible else "uncontrolled"},
        }
        reports = []
        for index, (triangles, p95) in enumerate(((10_000, 10.0), (20_000, 12.0), (20_000, 12.2))):
            report = {
                "schema": "rogue-command/runtime-geometry-sample/v3",
                "status": "complete",
                "captureStartedUtc": f"2026-11-12T00:00:0{index}Z",
                "scene": "Game",
                "unityVersion": "2021.3.45f2",
                "camera": camera,
                "capture": {
                    "requestedFrames": 120,
                    "sampleCount": 120,
                    "timeScaleAtStart": 1.0,
                    "timeScaleStayedPositive": True,
                    "cameraStable": True,
                    "geometryLoadStable": True,
                    "geometryStabilityChecks": 4,
                    "geometrySignature": 1000 + index,
                    "candidateMeshRenderers": 3,
                    "countedMeshRenderers": 3,
                    "unsupportedMeshRenderers": 0,
                    "frameIntervalMetric": "UnityEngine.Time.unscaledDeltaTime",
                    "frameIntervalUnit": "ms",
                    "geometryMetric": "cameraFrustumMeshTriangles",
                },
                "summary": {"cameraFrustumMeshTriangles": triangles},
                "fitSample": {
                    "triangles": triangles,
                    "p95UnscaledFrameIntervalMs": p95,
                    "calibrationReady": True,
                },
                "frameIntervalsMs": [p95] * 120,
            }
            report_path = root / f"capture-{index}.json"
            raw = json.dumps(report, separators=(",", ":")).encode("utf-8")
            report_path.write_bytes(raw)
            reports.append({
                "reportPath": report_path.name,
                "sha256": hashlib.sha256(raw).hexdigest(),
                "captureGroup": "controlled-low" if index == 0 else "controlled-high",
                "entityCensus": {"total": 7, "player": 1, "aiUnits": 3, "aiBuildings": 3},
            })

        evidence_path = root / "runtime-evidence.json"
        evidence_path.write_text(json.dumps({
            "schema": "rogue-command/runtime-geometry-evidence/v1",
            "context": context,
            "reports": reports,
        }), encoding="utf-8")
        samples = _read_samples(evidence_path)
        profile = {
            "schemaVersion": 1,
            "profileId": "inventory-staging-test",
            "calibrationStatus": "uncalibrated",
            "targetFps": 60.0,
            "calibration": {
                "resolutionWidth": 1920,
                "resolutionHeight": 1080,
                "cameraReference": "Main Camera, 1920x1080, display-backed Game camera.",
                "sceneMix": scene_mix,
                "maximumVisibleInstances": 4,
                "capacityModel": {
                    "method": "conservative-measured-incremental-envelope-v1",
                    "reserveFraction": 0.25,
                },
                "sceneReference": scene_reference,
                "measurementContext": context,
                "samples": samples,
            },
            "scenes": [],
            "assetInventory": {"assets": []},
        }
        profile_path = root / "profile.json"
        profile_path.write_text(json.dumps(profile), encoding="utf-8")
        return profile_path, evidence_path

    def _entry(self, asset_id: str = "RCLModule_demo_test") -> dict[str, str]:
        return {
            "moduleName": asset_id,
            "variantId": "tactical-operations:demo-test",
            "unitId": "tactical-operations:demo",
            "slotId": "APS",
            "preset": "test-preset",
            "geometryPreset": "test-geometry-v1",
            "geometryLayout": "test-layout-v1",
            "visualEngineeringRole": "aps_sensor_suite",
        }

    def test_ineligible_measurement_refuses_before_directory_or_candidate_write(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            profile, evidence = self._write_profile_and_bundle(root, capacity_eligible=False)
            stage = root / "artifacts" / "inventory" / "run-1"
            calls: list[str] = []

            def should_not_build(entry, candidate_path, gate):
                calls.append(str(candidate_path))
                raise AssertionError("candidate export callback must not run")

            with self.assertRaisesRegex(ModelBudgetError, "not eligible for capacity staging"):
                stage_candidate_inventory(
                    repo_root=root,
                    staging_root=stage,
                    profile_path=profile,
                    evidence_bundle_path=evidence,
                    entries=[self._entry()],
                    build_candidate=should_not_build,
                )
            self.assertFalse(stage.exists())
            self.assertEqual(calls, [])

    def test_candidate_inventory_keeps_inspector_counts_and_file_sha_exact(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            profile, evidence = self._write_profile_and_bundle(root)
            stage = root / "artifacts" / "canonical-level2" / "inventory-run"
            profile_before = profile.read_bytes()

            def build_candidate(entry, candidate_path, gate):
                validate_candidate_triangle_count(300, gate, entry["moduleName"])
                candidate_path.parent.mkdir(parents=True, exist_ok=True)
                candidate_path.write_bytes(b"deterministic candidate FBX payload")
                return {
                    "assetId": entry["moduleName"],
                    "sourceFaces": 20,
                    "sourceTriangles": 30,
                    "meshObjects": 2,
                    "vertices": 42,
                    "sha256": sha256_file(candidate_path),
                }

            manifest = stage_candidate_inventory(
                repo_root=root,
                staging_root=stage,
                profile_path=profile,
                evidence_bundle_path=evidence,
                entries=[self._entry()],
                build_candidate=build_candidate,
            )

            self.assertEqual(manifest["status"], "candidate-inventory-ready")
            self.assertFalse(manifest["promotionAllowed"])
            self.assertFalse(manifest["normalBuildAllowed"])
            self.assertEqual(manifest["capacityGate"]["sceneTriangleCapacity"], 7_500)
            row = manifest["assetInventory"]["assets"][0]
            candidate = root / row["candidateFbxPath"]
            self.assertEqual(row["sourceFaces"], 20)
            self.assertEqual(row["sourceTriangles"], 30)
            self.assertEqual(row["sha256"], hashlib.sha256(candidate.read_bytes()).hexdigest())
            self.assertEqual(row["sourcePath"], "artifacts/canonical-level2/inventory-run/fbx/RCLModule_demo_test.fbx")
            self.assertEqual(profile.read_bytes(), profile_before)
            self.assertTrue((stage / "candidate-inventory.json").is_file())

    def test_module_over_measured_capacity_is_rejected_before_export_callback_writes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            profile, evidence = self._write_profile_and_bundle(root)
            stage = root / "artifacts" / "inventory" / "over-capacity"
            writes: list[str] = []

            def build_candidate(entry, candidate_path, gate):
                validate_candidate_triangle_count(gate.scene_triangle_capacity + 1, gate, entry["moduleName"])
                writes.append(str(candidate_path))
                candidate_path.parent.mkdir(parents=True, exist_ok=True)
                candidate_path.write_bytes(b"should never be written")
                raise AssertionError("over-capacity candidate must stop before export")

            with self.assertRaisesRegex(ModelBudgetError, "above the measured scene capacity"):
                stage_candidate_inventory(
                    repo_root=root,
                    staging_root=stage,
                    profile_path=profile,
                    evidence_bundle_path=evidence,
                    entries=[self._entry()],
                    build_candidate=build_candidate,
                )
            self.assertEqual(writes, [])
            self.assertFalse((stage / "fbx" / "RCLModule_demo_test.fbx").exists())
            self.assertEqual(json.loads((stage / "candidate-inventory.json").read_text(encoding="utf-8"))["status"], "failed")

    def test_output_location_must_be_new_and_inside_artifacts(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            profile, evidence = self._write_profile_and_bundle(root)
            outside = root / "production" / "fbx"
            with self.assertRaisesRegex(ModelBudgetError, "only under repository artifacts"):
                stage_candidate_inventory(
                    repo_root=root,
                    staging_root=outside,
                    profile_path=profile,
                    evidence_bundle_path=evidence,
                    entries=[self._entry()],
                    build_candidate=lambda *_: self.fail("outside path must be rejected before export"),
                )
            self.assertFalse(outside.exists())

            existing = root / "artifacts" / "inventory" / "existing"
            existing.mkdir(parents=True)
            marker = existing / "keep.txt"
            marker.write_text("preserve", encoding="utf-8")
            with self.assertRaisesRegex(FileExistsError, "already exists"):
                stage_candidate_inventory(
                    repo_root=root,
                    staging_root=existing,
                    profile_path=profile,
                    evidence_bundle_path=evidence,
                    entries=[self._entry()],
                    build_candidate=lambda *_: self.fail("existing path must not be overwritten"),
                )
            self.assertEqual(marker.read_text(encoding="utf-8"), "preserve")

    def test_builder_routes_staging_before_the_normal_production_build(self):
        builder_path = Path(__file__).resolve().parents[2] / "tools" / "Build-CanonicalVisualModules.py"
        module = ast.parse(builder_path.read_text(encoding="utf-8"))
        main = next(node for node in module.body if isinstance(node, ast.FunctionDef) and node.name == "main")
        staging_call = next(
            node for node in ast.walk(main)
            if isinstance(node, ast.Call) and isinstance(node.func, ast.Name)
            and node.func.id == "stage_candidate_inventory"
        )
        production_call = next(
            node for node in ast.walk(main)
            if isinstance(node, ast.Call) and isinstance(node.func, ast.Name)
            and node.func.id == "build_all"
        )
        stage_branch = next(
            node for node in main.body
            if isinstance(node, ast.If) and isinstance(node.test, ast.Attribute)
            and node.test.attr == "inventory_staging"
        )
        self.assertLess(staging_call.lineno, production_call.lineno)
        self.assertIn(staging_call, list(ast.walk(stage_branch)))
        self.assertTrue(any(isinstance(node, ast.Return) for node in ast.walk(stage_branch)))

    def test_candidate_builder_checks_capacity_before_export_and_inspects_after_export(self):
        builder_path = Path(__file__).resolve().parents[2] / "tools" / "Build-CanonicalVisualModules.py"
        module = ast.parse(builder_path.read_text(encoding="utf-8"))
        candidate_builder = next(
            node for node in module.body
            if isinstance(node, ast.FunctionDef) and node.name == "build_candidate_module_fbx"
        )
        calls = [node for node in ast.walk(candidate_builder) if isinstance(node, ast.Call)]
        call_line = {}
        for node in calls:
            if isinstance(node.func, ast.Name):
                call_line[node.func.id] = node.lineno
            elif isinstance(node.func, ast.Attribute):
                call_line[node.func.attr] = node.lineno
        self.assertLess(call_line["validate_candidate_triangle_count"], call_line["export_inventory_candidate_fbx"])
        self.assertLess(call_line["export_inventory_candidate_fbx"], call_line["inspect_fbx"])
        self.assertIn("prepare_module_export_geometry", call_line)


if __name__ == "__main__":
    unittest.main()
