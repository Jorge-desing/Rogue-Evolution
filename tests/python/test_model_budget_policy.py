from __future__ import annotations

import json
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
)
from tools.profile_model_budgets import _read_samples

TEST_LOD_HEIGHTS = (0.54, 0.23, 0.07)


class ModelBudgetPolicyTests(unittest.TestCase):
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

    def test_runtime_capture_is_normalized_for_profile_calibration(self):
        report = {
            "schema": "rogue-command/runtime-geometry-sample/v2",
            "status": "complete",
            "captureStartedUtc": "2026-11-11T00:00:00Z",
            "scene": "Game",
            "unityVersion": "2021.3.45f2",
            "camera": {
                "name": "Main Camera", "pixelWidth": 1920, "pixelHeight": 1080,
                "fieldOfView": 26.0, "orthographic": False, "orthographicSize": 42.0,
                "position": [0.0, 10.0, 20.0], "rotationEuler": [45.0, 0.0, 0.0],
            },
            "capture": {
                "requestedFrames": 120,
                "sampleCount": 120,
                "timeScaleAtStart": 1.0,
                "frameIntervalMetric": "UnityEngine.Time.unscaledDeltaTime",
                "frameIntervalUnit": "ms",
                "geometryMetric": "cameraFrustumMeshTriangles",
                "countedMeshRenderers": 5,
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
        self.assertEqual(rows[0]["measurementSource"], "runtime-model-budget-probe-v2")
        self.assertEqual(rows[0]["unityVersion"], "2021.3.45f2")

        different_view = json.loads(json.dumps(report))
        different_view["camera"]["position"][0] += 1.0
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "captures.json"
            path.write_text(json.dumps({"reports": [report, different_view]}), encoding="utf-8")
            with self.assertRaisesRegex(ModelBudgetError, "one scene, camera pose"):
                _read_samples(path)

    def test_runtime_capture_rejects_paused_or_incomplete_runs(self):
        report = {
            "schema": "rogue-command/runtime-geometry-sample/v2",
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
            with self.assertRaisesRegex(ModelBudgetError, "schema-v2 runtime report"):
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
