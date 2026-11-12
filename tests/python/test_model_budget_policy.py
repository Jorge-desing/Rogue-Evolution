from __future__ import annotations

import json
import hashlib
from pathlib import Path
import tempfile
import unittest

from tools.model_budget_policy import (
    AssetDemand,
    FrameSample,
    ModelBudgetError,
    allocate_scene_budgets,
    calibrate_profile,
    fit_triangle_capacity,
    resolve_asset_budget,
    validate_prevalidated_export_budget,
)
from tools.profile_model_budgets import _read_samples

TEST_LOD_HEIGHTS = (0.54, 0.23, 0.07)


def _valid_export_budget() -> dict[str, object]:
    return {
        "assetId": "Tank",
        "sceneId": "DenseRTS",
        "targetFps": 60.0,
        "resolution": {"width": 1920, "height": 1080},
        "sceneTriangleCapacity": 250_000,
        "sceneAllocationTriangles": 100_000,
        "maxVisibleInstances": 4,
        "sceneBudgetPassed": True,
        "assetVerdict": "PASS",
        "budgetVerdict": "CALIBRATED_FOR_RECORDED_PC_AND_SCENE",
        "lods": [
            {"lod": "LOD0", "screenRelativeTransitionHeight": 0.54, "targetFacesPerInstance": 20_000, "targetTrianglesPerInstance": 30_000},
            {"lod": "LOD1", "screenRelativeTransitionHeight": 0.23, "targetFacesPerInstance": 10_000, "targetTrianglesPerInstance": 15_000},
            {"lod": "LOD2", "screenRelativeTransitionHeight": 0.07, "targetFacesPerInstance": 5_000, "targetTrianglesPerInstance": 8_000},
        ],
    }


class ModelBudgetPolicyTests(unittest.TestCase):
    def test_prevalidated_export_budget_requires_a_passing_matching_asset_allocation(self):
        validate_prevalidated_export_budget(_valid_export_budget(), expected_asset_id="Tank")

        with self.assertRaisesRegex(ModelBudgetError, "resolved calibrated geometry budget"):
            validate_prevalidated_export_budget(None)
        with self.assertRaisesRegex(ModelBudgetError, "does not match"):
            validate_prevalidated_export_budget(_valid_export_budget(), expected_asset_id="OtherTank")

        failed = _valid_export_budget()
        failed["assetVerdict"] = "LOD0_REDUCTION_REQUIRED"
        with self.assertRaisesRegex(ModelBudgetError, "allocation has not passed"):
            validate_prevalidated_export_budget(failed)

    def test_prevalidated_export_budget_rejects_invalid_lod_transitions(self):
        budget = _valid_export_budget()
        budget["lods"][2]["screenRelativeTransitionHeight"] = 0.23
        with self.assertRaisesRegex(ModelBudgetError, "thresholds must descend"):
            validate_prevalidated_export_budget(budget)

    def test_capacity_is_derived_from_measured_frame_time(self):
        result = fit_triangle_capacity(
            [
                FrameSample(0, 8.0),
                FrameSample(100_000, 12.0),
                FrameSample(200_000, 16.0),
            ],
            target_fps=60.0,
        )
        self.assertEqual(result["sceneTriangleCapacity"], 216_666)
        self.assertAlmostEqual(result["targetFrameTimeMs"], 1000.0 / 60.0)

    def test_capacity_rejects_uncalibrated_or_infeasible_samples(self):
        with self.assertRaises(ModelBudgetError):
            fit_triangle_capacity([FrameSample(10, 10), FrameSample(20, 11)])
        with self.assertRaises(ModelBudgetError):
            fit_triangle_capacity(
                [FrameSample(0, 18), FrameSample(100, 19), FrameSample(200, 20)],
                target_fps=60,
            )

    def test_measured_envelope_uses_a_reserved_observed_range_without_extrapolation(self):
        result = fit_triangle_capacity(
            [FrameSample(110_175, 12.093465), FrameSample(401_462, 12.1293), FrameSample(401_462, 12.1604)],
            capacity_model={
                "method": "conservative-measured-incremental-envelope-v1",
                "reserveFraction": 0.25,
            },
        )
        self.assertEqual(result["sceneTriangleCapacity"], 218_465)
        self.assertEqual(result["capacityScope"], "incremental-asset-geometry-above-lowest-observed-load")
        self.assertEqual(result["observedTriangleRange"], 291_287)
        self.assertEqual(result["reservedTriangleHeadroom"], 72_822)
        self.assertFalse(result["extrapolationUsed"])
        self.assertEqual(result["fitMethod"], "conservative-measured-incremental-envelope-v1")

        with self.assertRaisesRegex(ModelBudgetError, "exceed the 16.667 ms target"):
            fit_triangle_capacity(
                [FrameSample(100, 16.8), FrameSample(200, 16.7), FrameSample(300, 16.6)],
                capacity_model={
                    "method": "conservative-measured-incremental-envelope-v1",
                    "reserveFraction": 0.25,
                },
            )

    def test_runtime_capture_is_normalized_for_profile_calibration(self):
        report = {
            "schema": "rogue-command/runtime-geometry-sample/v3",
            "status": "complete",
            "captureStartedUtc": "2026-11-11T00:00:00Z",
            "scene": "Game",
            "unityVersion": "2021.3.45f2",
            "camera": {
                "name": "Main Camera", "pixelWidth": 1920, "pixelHeight": 1080,
                "cameraType": "Game", "usesRenderTexture": False,
                "fieldOfView": 26.0, "orthographic": False, "orthographicSize": 42.0,
                "aspect": 1920.0 / 1080.0, "cullingMask": -1,
                "position": [0.0, 10.0, 20.0], "rotationEuler": [45.0, 0.0, 0.0],
            },
            "capture": {
                "requestedFrames": 120,
                "sampleCount": 120,
                "timeScaleAtStart": 1.0,
                "timeScaleStayedPositive": True,
                "cameraStable": True,
                "geometryLoadStable": True,
                "geometryStabilityChecks": 4,
                "geometrySignature": 123456789,
                "candidateMeshRenderers": 5,
                "frameIntervalMetric": "UnityEngine.Time.unscaledDeltaTime",
                "frameIntervalUnit": "ms",
                "geometryMetric": "cameraFrustumMeshTriangles",
                "countedMeshRenderers": 5,
                "unsupportedMeshRenderers": 0,
            },
            "summary": {"cameraFrustumMeshTriangles": 185_000},
            "fitSample": {
                "triangles": 185_000,
                "p95UnscaledFrameIntervalMs": 13.5,
                "calibrationReady": True,
            },
            "frameIntervalsMs": [13.5] * 120,
        }
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "capture.json"
            path.write_text(json.dumps(report), encoding="utf-8")
            rows = _read_samples(path)

        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["triangles"], 185_000)
        self.assertEqual(rows[0]["p95UnscaledFrameIntervalMs"], 13.5)
        self.assertEqual(rows[0]["measurementSource"], "runtime-model-budget-probe-v3")
        self.assertEqual(rows[0]["unityVersion"], "2021.3.45f2")

        intermission = json.loads(json.dumps(report))
        intermission["scene"] = "IntermissionScreen"
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "intermission.json"
            path.write_text(json.dumps(intermission), encoding="utf-8")
            with self.assertRaisesRegex(ModelBudgetError, "menus, intermissions"):
                _read_samples(path)

        with tempfile.TemporaryDirectory() as temp_dir:
            bundle_dir = Path(temp_dir)
            raw_path = bundle_dir / "capture.json"
            raw_bytes = json.dumps(report).encode("utf-8")
            raw_path.write_bytes(raw_bytes)
            camera = report["camera"]
            bundle = {
                "schema": "rogue-command/runtime-geometry-evidence/v1",
                "context": {
                    "sceneReference": {
                        "sceneId": "dense-gameplay", "unitySceneName": "Game", "level": 4,
                    },
                    "cameraReference": {
                        "name": camera["name"], "pixelWidth": camera["pixelWidth"],
                        "pixelHeight": camera["pixelHeight"], "fieldOfView": camera["fieldOfView"],
                        "orthographic": camera["orthographic"], "orthographicSize": camera["orthographicSize"],
                        "aspect": camera["aspect"], "cullingMask": camera["cullingMask"],
                        "position": camera["position"], "rotationEuler": camera["rotationEuler"],
                    },
                    "sceneMix": "fixed gameplay scene with the measured vehicle mix",
                    "capacityEligible": True,
                },
                "reports": [{
                    "reportPath": "capture.json",
                    "sha256": hashlib.sha256(raw_bytes).hexdigest(),
                    "captureGroup": "fixed-mix",
                }],
            }
            bundle_path = bundle_dir / "evidence.json"
            bundle_path.write_text(json.dumps(bundle), encoding="utf-8")
            bundle_rows = _read_samples(bundle_path)
            self.assertEqual(bundle_rows[0]["evidenceReportSha256"], hashlib.sha256(raw_bytes).hexdigest())
            self.assertEqual(bundle_rows[0]["measurementContext"]["sceneReference"]["sceneId"], "dense-gameplay")
            raw_path.write_bytes(raw_bytes + b" ")
            with self.assertRaisesRegex(ModelBudgetError, "SHA-256 mismatch"):
                _read_samples(bundle_path)

        different_view = json.loads(json.dumps(report))
        different_view["camera"]["position"][0] += 1.0
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "captures.json"
            path.write_text(json.dumps({"reports": [report, different_view]}), encoding="utf-8")
            with self.assertRaisesRegex(ModelBudgetError, "one scene, camera pose"):
                _read_samples(path)

        unstable_load = json.loads(json.dumps(report))
        unstable_load["capture"]["geometryLoadStable"] = False
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "unstable.json"
            path.write_text(json.dumps(unstable_load), encoding="utf-8")
            with self.assertRaisesRegex(ModelBudgetError, "camera, or geometry load changed"):
                _read_samples(path)

    def test_runtime_capture_rejects_missing_or_inconsistent_geometry_signature(self):
        report = {
            "schema": "rogue-command/runtime-geometry-sample/v3",
            "status": "complete",
            "captureStartedUtc": "2026-11-11T00:00:00Z",
            "scene": "Game",
            "unityVersion": "2021.3.45f2",
            "camera": {
                "name": "Main Camera", "pixelWidth": 1920, "pixelHeight": 1080,
                "cameraType": "Game", "usesRenderTexture": False,
                "fieldOfView": 26.0, "orthographic": False, "orthographicSize": 42.0,
                "aspect": 1920.0 / 1080.0, "cullingMask": -1,
                "position": [0.0, 10.0, 20.0], "rotationEuler": [45.0, 0.0, 0.0],
            },
            "capture": {
                "requestedFrames": 120, "sampleCount": 120, "timeScaleAtStart": 1.0,
                "timeScaleStayedPositive": True, "cameraStable": True, "geometryLoadStable": True,
                "geometryStabilityChecks": 4, "geometrySignature": 123456789,
                "candidateMeshRenderers": 5, "countedMeshRenderers": 5, "unsupportedMeshRenderers": 0,
                "frameIntervalMetric": "UnityEngine.Time.unscaledDeltaTime", "frameIntervalUnit": "ms",
                "geometryMetric": "cameraFrustumMeshTriangles",
            },
            "summary": {"cameraFrustumMeshTriangles": 185_000},
            "fitSample": {"triangles": 185_000, "p95UnscaledFrameIntervalMs": 13.5, "calibrationReady": True},
            "frameIntervalsMs": [13.5] * 120,
        }
        invalid_reports = []
        missing_signature = json.loads(json.dumps(report))
        del missing_signature["capture"]["geometrySignature"]
        invalid_reports.append((missing_signature, "stability check metadata is invalid"))
        unreconciled_census = json.loads(json.dumps(report))
        unreconciled_census["capture"]["candidateMeshRenderers"] = 6
        invalid_reports.append((unreconciled_census, "does not reconcile candidate"))

        for index, (invalid, message) in enumerate(invalid_reports):
            with self.subTest(index=index), tempfile.TemporaryDirectory() as temp_dir:
                path = Path(temp_dir) / "capture.json"
                path.write_text(json.dumps(invalid), encoding="utf-8")
                with self.assertRaisesRegex(ModelBudgetError, message):
                    _read_samples(path)

    def test_runtime_capture_rejects_paused_or_incomplete_runs(self):
        report = {
            "schema": "rogue-command/runtime-geometry-sample/v3",
            "status": "complete",
            "scene": "Game",
            "camera": {"name": "Main Camera", "pixelWidth": 1920, "pixelHeight": 1080},
            "capture": {
                "requestedFrames": 120,
                "sampleCount": 120,
                "timeScaleAtStart": 0.0,
                "frameIntervalMetric": "UnityEngine.Time.unscaledDeltaTime",
                "frameIntervalUnit": "ms",
                "geometryMetric": "cameraFrustumMeshTriangles",
            },
            "fitSample": {"triangles": 185_000, "p95UnscaledFrameIntervalMs": 13.5, "calibrationReady": False},
            "frameIntervalsMs": [13.5] * 120,
        }
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "capture.json"
            path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(ModelBudgetError, "not eligible for calibration"):
                _read_samples(path)

    def test_profile_sample_input_rejects_unverified_raw_rows(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "samples.json"
            path.write_text(json.dumps({"samples": [
                {"triangles": 100_000, "p95UnscaledFrameIntervalMs": 10.0},
            ]}), encoding="utf-8")
            with self.assertRaisesRegex(ModelBudgetError, "schema-v3 runtime report"):
                _read_samples(path)

    def test_project_profile_keeps_the_required_sixty_fps_target(self):
        result = calibrate_profile({"schemaVersion": 1, "targetFps": 30})
        self.assertEqual(result["calibrationStatus"], "uncalibrated")
        self.assertIn("must target 60 FPS", result["calibrationFailureReason"])

    def test_profile_rejects_pending_camera_or_scene_metadata(self):
        result = calibrate_profile({
            "schemaVersion": 1,
            "targetFps": 60,
            "calibration": {
                "resolutionWidth": 1920,
                "resolutionHeight": 1080,
                "cameraReference": "pending: identify camera",
                "sceneMix": "dense armored RTS scene",
                "maximumVisibleInstances": 20,
                "samples": [
                    {"triangles": 0, "p95UnscaledFrameIntervalMs": 8},
                    {"triangles": 100_000, "p95UnscaledFrameIntervalMs": 12},
                    {"triangles": 200_000, "p95UnscaledFrameIntervalMs": 16},
                ],
            },
        })
        self.assertEqual(result["calibrationStatus"], "uncalibrated")
        self.assertIn("cameraReference and sceneMix are required", result["calibrationFailureReason"])

    def test_uncontrolled_measured_envelope_is_retained_as_evidence_but_not_calibrated(self):
        result = calibrate_profile({
            "schemaVersion": 1,
            "targetFps": 60,
            "calibration": {
                "resolutionWidth": 1920,
                "resolutionHeight": 1080,
                "cameraReference": "Main Camera21 / fixed 1920x1080 pose",
                "sceneMix": "Level 4 boss scene with four measured vehicle types",
                "maximumVisibleInstances": 12,
                "capacityModel": {
                    "method": "conservative-measured-incremental-envelope-v1",
                    "reserveFraction": 0.25,
                },
                "measurementContext": {
                    "capacityEligible": False,
                    "capacityIneligibilityReason": "scene population changed between low and high loads",
                },
                "samples": [
                    {"triangles": 110_175, "p95UnscaledFrameIntervalMs": 12.093465},
                    {"triangles": 401_462, "p95UnscaledFrameIntervalMs": 12.1293},
                    {"triangles": 401_462, "p95UnscaledFrameIntervalMs": 12.1604},
                ],
            },
        })
        self.assertEqual(result["calibrationStatus"], "uncalibrated")
        self.assertEqual(result["calibrationFailureReason"], "scene population changed between low and high loads")
        self.assertEqual(result["measurementCapacityEstimate"]["sceneTriangleCapacity"], 218_465)
        self.assertNotIn("calibrationFit", result)
        forced_calibrated = dict(result)
        forced_calibrated["calibrationStatus"] = "calibrated"
        from tools.model_budget_policy import build_budget_report
        with self.assertRaisesRegex(ModelBudgetError, "evidence-only until their load pair is controlled"):
            build_budget_report(forced_calibrated)

    def test_scene_allocation_obeys_aggregate_capacity_and_lod_order(self):
        report = allocate_scene_budgets(
            120_000,
            [
                AssetDemand("tank", 2_000, 3_000, 8, 0.35, 1.5, TEST_LOD_HEIGHTS),
                AssetDemand("relay", 1_000, 1_200, 4, 0.25, 1.0, TEST_LOD_HEIGHTS),
            ],
        )
        self.assertTrue(report["sceneBudgetPassed"])
        totals = report["sceneTriangleBudgetByLod"]
        self.assertLessEqual(totals["LOD0"], 120_000)
        self.assertLessEqual(totals["LOD1"], totals["LOD0"])
        self.assertLessEqual(totals["LOD2"], totals["LOD1"])

    def test_more_visible_instances_reduce_per_instance_allowance(self):
        one = allocate_scene_budgets(100_000, [
            AssetDemand("tank", 10_000, 20_000, 1, 0.4, 1.0, TEST_LOD_HEIGHTS),
        ])
        many = allocate_scene_budgets(100_000, [
            AssetDemand("tank", 10_000, 20_000, 10, 0.4, 1.0, TEST_LOD_HEIGHTS),
        ])
        one_budget = one["assets"][0]["lods"][0]["targetTrianglesPerInstance"]
        many_budget = many["assets"][0]["lods"][0]["targetTrianglesPerInstance"]
        self.assertLess(many_budget, one_budget)

    def test_small_screen_footprint_reduces_allocated_budget(self):
        report = allocate_scene_budgets(
            20_000,
            [
                AssetDemand("large", 10_000, 20_000, 1, 0.6, 1.0, TEST_LOD_HEIGHTS),
                AssetDemand("small", 10_000, 20_000, 1, 0.2, 1.0, TEST_LOD_HEIGHTS),
            ],
        )
        budgets = {row["assetId"]: row["sceneAllocationTriangles"] for row in report["assets"]}
        self.assertGreater(budgets["large"], budgets["small"])

    def test_aggregate_scene_lod_demand_never_exceeds_a_tiny_capacity(self):
        report = allocate_scene_budgets(
            1,
            [
                AssetDemand("one", 100, 100, 1, 0.5, 1.0, TEST_LOD_HEIGHTS),
                AssetDemand("two", 100, 100, 1, 0.5, 1.0, TEST_LOD_HEIGHTS),
                AssetDemand("three", 100, 100, 1, 0.5, 1.0, TEST_LOD_HEIGHTS),
            ],
        )
        self.assertTrue(report["sceneBudgetPassed"])
        self.assertLessEqual(sum(report["sceneTriangleBudgetByLod"].values()) // 3, 1)
        self.assertLessEqual(report["sceneTriangleBudgetByLod"]["LOD0"], 1)

    def test_profile_requires_camera_scene_and_visible_instance_metadata(self):
        profile = {
            "schemaVersion": 1,
            "calibrationStatus": "calibrated",
            "targetFps": 60,
            "calibration": {
                "resolutionWidth": 1920,
                "resolutionHeight": 1080,
                "samples": [
                    {"triangles": 0, "p95UnscaledFrameIntervalMs": 8},
                    {"triangles": 100_000, "p95UnscaledFrameIntervalMs": 12},
                    {"triangles": 200_000, "p95UnscaledFrameIntervalMs": 16},
                ],
            },
            "scenes": [{"sceneId": "dense", "assets": []}],
        }
        with self.assertRaisesRegex(ModelBudgetError, "cameraReference"):
            from tools.model_budget_policy import build_budget_report
            build_budget_report(profile)

    def test_scene_demands_require_an_explicit_role_priority(self):
        profile = {
            "schemaVersion": 1,
            "calibrationStatus": "calibrated",
            "targetFps": 60,
            "calibration": {
                "resolutionWidth": 1920,
                "resolutionHeight": 1080,
                "cameraReference": "reference camera",
                "sceneMix": "dense mixed scene",
                "maximumVisibleInstances": 1,
                "samples": [
                    {"triangles": 0, "p95UnscaledFrameIntervalMs": 8},
                    {"triangles": 100_000, "p95UnscaledFrameIntervalMs": 12},
                    {"triangles": 200_000, "p95UnscaledFrameIntervalMs": 16},
                ],
            },
            "scenes": [{"sceneId": "dense", "assets": [{
                "assetId": "tank", "sourceFaces": 10_000,
                "sourceTriangles": 20_000, "maxVisibleInstances": 1,
                "screenHeightFraction": 0.5,
                "lodScreenHeights": list(TEST_LOD_HEIGHTS),
            }]}],
        }
        from tools.model_budget_policy import build_budget_report
        with self.assertRaisesRegex(ModelBudgetError, "rolePriority is required"):
            build_budget_report(profile)

    def test_scene_demands_cannot_exceed_the_profiled_visible_instance_total(self):
        profile = {
            "schemaVersion": 1,
            "calibrationStatus": "calibrated",
            "targetFps": 60,
            "calibration": {
                "resolutionWidth": 1920, "resolutionHeight": 1080,
                "cameraReference": "dense RTS camera", "sceneMix": "mixed vehicles",
                "maximumVisibleInstances": 1,
                "samples": [
                    {"triangles": 0, "p95UnscaledFrameIntervalMs": 8},
                    {"triangles": 100_000, "p95UnscaledFrameIntervalMs": 12},
                    {"triangles": 200_000, "p95UnscaledFrameIntervalMs": 16},
                ],
            },
            "scenes": [{"sceneId": "dense", "assets": [
                {"assetId": "one", "sourceFaces": 100, "sourceTriangles": 200,
                 "maxVisibleInstances": 1, "screenHeightFraction": 0.5,
                 "rolePriority": 1.0, "lodScreenHeights": list(TEST_LOD_HEIGHTS)},
                {"assetId": "two", "sourceFaces": 100, "sourceTriangles": 200,
                 "maxVisibleInstances": 1, "screenHeightFraction": 0.5,
                 "rolePriority": 1.0, "lodScreenHeights": list(TEST_LOD_HEIGHTS)},
            ]}],
        }
        from tools.model_budget_policy import build_budget_report
        with self.assertRaisesRegex(ModelBudgetError, "above calibration.maximumVisibleInstances"):
            build_budget_report(profile)

    def test_profile_demands_must_match_inventory_source_hash_and_geometry(self):
        from tools.model_budget_policy import build_budget_report
        profile = {
            "schemaVersion": 1,
            "calibrationStatus": "calibrated",
            "targetFps": 60,
            "calibration": {
                "resolutionWidth": 1920, "resolutionHeight": 1080,
                "cameraReference": "dense RTS camera", "sceneMix": "mixed vehicles",
                "maximumVisibleInstances": 1,
                "samples": [
                    {"triangles": 0, "p95UnscaledFrameIntervalMs": 8},
                    {"triangles": 100_000, "p95UnscaledFrameIntervalMs": 12},
                    {"triangles": 200_000, "p95UnscaledFrameIntervalMs": 16},
                ],
            },
            "assetInventory": {"assets": [{
                "assetId": "tank", "sourceFaces": 100, "sourceTriangles": 200,
                "sha256": "a" * 64,
            }]},
            "scenes": [{"sceneId": "dense", "assets": [{
                "assetId": "tank", "sourceFaces": 100, "sourceTriangles": 200,
                "sourceSha256": "a" * 64,
                "maxVisibleInstances": 1, "screenHeightFraction": 0.5,
                "rolePriority": 1.0, "lodScreenHeights": list(TEST_LOD_HEIGHTS),
            }]}],
        }
        self.assertTrue(build_budget_report(profile)["scenes"][0]["sceneBudgetPassed"])
        profile["scenes"][0]["assets"][0]["sourceSha256"] = "b" * 64
        with self.assertRaisesRegex(ModelBudgetError, "sourceSha256 does not match assetInventory"):
            build_budget_report(profile)

    def test_weighting_accounts_for_instance_count_screen_area_and_role(self):
        report = allocate_scene_budgets(
            80_000,
            [
                AssetDemand("hero", 20_000, 40_000, 1, 0.45, 1.5, TEST_LOD_HEIGHTS),
                AssetDemand("crowd", 20_000, 40_000, 12, 0.20, 0.7, TEST_LOD_HEIGHTS),
            ],
        )
        budgets = {row["assetId"]: row["sceneAllocationTriangles"] for row in report["assets"]}
        self.assertGreater(budgets["hero"], 0)
        self.assertGreater(budgets["crowd"], budgets["hero"])
        self.assertLessEqual(report["sceneTriangleBudgetByLod"]["LOD0"], 80_000)

    def test_shared_asset_uses_tightest_scene_allocation(self):
        profile = {
            "schemaVersion": 1,
            "calibrationStatus": "calibrated",
            "targetFps": 60,
            "calibration": {
                "resolutionWidth": 1920,
                "resolutionHeight": 1080,
                "cameraReference": "dense RTS 45 degree camera",
                "sceneMix": "mixed armor and structures",
                "maximumVisibleInstances": 12,
                "samples": [
                    {"triangles": 0, "p95UnscaledFrameIntervalMs": 8},
                    {"triangles": 100_000, "p95UnscaledFrameIntervalMs": 12},
                    {"triangles": 200_000, "p95UnscaledFrameIntervalMs": 16},
                ],
            },
            "scenes": [
                {"sceneId": "solo", "assets": [{
                    "assetId": "tank", "sourceFaces": 10_000,
                    "sourceTriangles": 500_000, "maxVisibleInstances": 1,
                    "screenHeightFraction": 0.5, "rolePriority": 1.0,
                    "lodScreenHeights": list(TEST_LOD_HEIGHTS),
                }]},
                {"sceneId": "crowded", "assets": [{
                    "assetId": "tank", "sourceFaces": 10_000,
                    "sourceTriangles": 500_000, "maxVisibleInstances": 2,
                    "screenHeightFraction": 0.5, "rolePriority": 1.0,
                    "lodScreenHeights": list(TEST_LOD_HEIGHTS),
                }]},
            ],
        }
        budget = resolve_asset_budget(profile, "tank")
        self.assertEqual(budget["sceneId"], "worst-case:crowded,solo")
        self.assertEqual(budget["sceneAllocations"][0]["sceneId"], "solo")
        self.assertEqual(len(budget["sceneAllocations"]), 2)
        self.assertEqual(
            budget["lods"][0]["targetTrianglesPerInstance"],
            min(
                row["lods"][0]["targetTrianglesPerInstance"]
                for row in budget["sceneAllocations"]
            ),
        )

    def test_invalid_scene_demand_cannot_be_marked_calibrated(self):
        profile = {
            "schemaVersion": 1,
            "targetFps": 60,
            "calibration": {
                "resolutionWidth": 1920,
                "resolutionHeight": 1080,
                "cameraReference": "reference camera",
                "sceneMix": "mixed scene",
                "maximumVisibleInstances": 1,
                "samples": [
                    {"triangles": 0, "p95UnscaledFrameIntervalMs": 8},
                    {"triangles": 100_000, "p95UnscaledFrameIntervalMs": 12},
                    {"triangles": 200_000, "p95UnscaledFrameIntervalMs": 16},
                ],
            },
            "scenes": [{"sceneId": "empty", "assets": []}],
        }
        result = calibrate_profile(profile)
        self.assertEqual(result["calibrationStatus"], "uncalibrated")
        self.assertIn("at least one asset", result["calibrationFailureReason"])


if __name__ == "__main__":
    unittest.main()
