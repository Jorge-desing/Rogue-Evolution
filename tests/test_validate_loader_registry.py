from __future__ import annotations
import contextlib
import importlib.util
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any
from unittest import mock


def _load_validator() -> Any:
    script_path = Path(__file__).resolve().parents[1] / "tools" / "validate_loader_registry.py"
    spec = importlib.util.spec_from_file_location("validate_loader_registry", script_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load registry validator from {script_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


VALIDATOR = _load_validator()


class LoaderRegistryValidationTests(unittest.TestCase):
    def _run_pack(self, pack: Any) -> tuple[int, str]:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            pack_directory = root / "sample-pack"
            pack_directory.mkdir()
            (pack_directory / "content.json").write_text(
                json.dumps(pack), encoding="utf-8"
            )
            output = io.StringIO()
            with mock.patch.object(VALIDATOR, "PACKS_ROOT", root):
                with contextlib.redirect_stdout(output):
                    exit_code = VALIDATOR.main([])
            return exit_code, output.getvalue()

    def test_invalid_factory_pool_and_ai_ranges_fail_the_tool(self) -> None:
        pack = {
            "units": [
                {"id": "test:unit-a", "productionBuilding": {"id": "test:shared-factory"}},
                {"id": "test:unit-b", "productionBuilding": {"id": "test:shared-factory"}},
            ],
            "stages": [
                {
                    "id": "test:stage-a",
                    "aiProfileIds": ["test:ai"],
                    "blueprintPoolEntries": [
                        {"poolId": "pool-a", "unitIds": ["test:unit-a", "test:unit-a"]}
                    ],
                },
                {"id": "test:stage-b"},
                {"id": "test:stage-c", "aiProfileIds": ["test:ai"]},
            ],
            "aiProfiles": [
                {
                    "id": "test:ai",
                    "scopeToReferencedStages": True,
                }
            ],
        }

        exit_code, output = self._run_pack(pack)

        self.assertEqual(exit_code, 1)
        self.assertIn("duplicate productionBuilding.id=test:shared-factory", output)
        self.assertIn("duplicate unitId=test:unit-a", output)
        self.assertIn("non-contiguous stage range", output)
        self.assertNotIn("PASS checked", output)

    def test_json_report_lists_factories_pool_scopes_and_ai_ranges(self) -> None:
        pack = {
            "packId": "test:pack",
            "units": [
                {"id": "test:unit-a", "productionBuilding": {"id": "test:factory-a"}},
                {"id": "test:unit-b", "productionBuilding": {"id": "test:factory-b"}},
            ],
            "stages": [
                {
                    "id": "test:stage-a",
                    "unitIds": ["test:unit-a"],
                    "aiProfileIds": ["test:ai"],
                    "blueprintPoolEntries": [
                        {"poolId": "pool-a", "unitIds": ["test:unit-b", "native:unit"]}
                    ],
                },
                {"id": "test:stage-b", "aiProfileIds": ["test:ai"]},
            ],
            "aiProfiles": [
                {"id": "test:ai", "scopeToReferencedStages": True},
            ],
        }

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            pack_directory = root / "sample-pack"
            pack_directory.mkdir()
            (pack_directory / "content.json").write_text(
                json.dumps(pack), encoding="utf-8"
            )
            output = io.StringIO()
            with mock.patch.object(VALIDATOR, "PACKS_ROOT", root):
                with contextlib.redirect_stdout(output):
                    exit_code = VALIDATOR.main(["--json"])

        self.assertEqual(exit_code, 0)
        self.assertTrue(output.getvalue().startswith("{"))
        report = json.loads(output.getvalue())
        self.assertEqual(report["summary"], {
            "checkedManifests": 1,
            "issueCount": 0,
            "registryValid": True,
        })
        pack_report = report["packs"][0]
        self.assertEqual(pack_report["unitFactories"], [
            {"unitId": "test:unit-a", "factoryId": "test:factory-a"},
            {"unitId": "test:unit-b", "factoryId": "test:factory-b"},
        ])
        stage_report = pack_report["stages"][0]
        self.assertEqual(stage_report["stageFactoryIds"], [
            "test:factory-a", "test:factory-b",
        ])
        self.assertEqual(stage_report["unlistedPoolFactoryIds"], [
            "test:factory-a",
        ])
        self.assertEqual(stage_report["explicitPoolScopes"], [{
            "poolId": "pool-a",
            "factoryIds": ["test:factory-b"],
            "unitIds": ["native:unit", "test:unit-b"],
        }])
        self.assertEqual(pack_report["aiProfileStageRanges"], [{
            "profileId": "test:ai",
            "indexScope": "pack-local array order",
            "stageIds": ["test:stage-a", "test:stage-b"],
            "stageIndices": [0, 1],
            "stageMin": 0,
            "stageMax": 1,
            "isContiguous": True,
        }])
        self.assertIn(
            "Unlisted pools derive eligibility only from stage.unitIds; blueprintPoolEntries do not broaden them.",
            report["limitations"],
        )
        self.assertIn(
            "Cross-pack stage factory IDs use unique mappings from readable manifests; this static report does not prove runtime loading, map assignment, or offers.",
            report["limitations"],
        )

    def test_json_report_maps_cross_pack_stage_unit_factories(self) -> None:
        stage_pack = {
            "packId": "test:stages",
            "stages": [{"id": "test:stage", "unitIds": ["test:external-unit"]}],
        }
        unit_pack = {
            "packId": "test:units",
            "units": [{
                "id": "test:external-unit",
                "productionBuilding": {"id": "test:external-factory"},
            }],
        }

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            for folder, pack in (("a-stage-pack", stage_pack), ("b-unit-pack", unit_pack)):
                pack_directory = root / folder
                pack_directory.mkdir()
                (pack_directory / "content.json").write_text(
                    json.dumps(pack), encoding="utf-8"
                )
            output = io.StringIO()
            with mock.patch.object(VALIDATOR, "PACKS_ROOT", root):
                with contextlib.redirect_stdout(output):
                    exit_code = VALIDATOR.main(["--json"])

        self.assertEqual(exit_code, 0)
        report = json.loads(output.getvalue())
        stage_pack_report = next(
            item for item in report["packs"] if item["packId"] == "test:stages"
        )
        stage_report = stage_pack_report["stages"][0]
        self.assertEqual(stage_report["stageFactoryIds"], ["test:external-factory"])
        self.assertEqual(stage_report["unlistedPoolFactoryIds"], ["test:external-factory"])

    def test_cross_pack_duplicate_unit_ids_invalidate_registry_report(self) -> None:
        packs = (
            ("alpha", {
                "packId": "test:alpha",
                "units": [{
                    "id": "shared:unit",
                    "productionBuilding": {"id": "test:alpha-factory"},
                }],
            }),
            ("beta", {
                "packId": "test:beta",
                "units": [{
                    "id": "shared:unit",
                    "productionBuilding": {"id": "test:beta-factory"},
                }],
            }),
        )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            for folder, pack in packs:
                pack_directory = root / folder
                pack_directory.mkdir()
                (pack_directory / "content.json").write_text(
                    json.dumps(pack), encoding="utf-8"
                )
            output = io.StringIO()
            with mock.patch.object(VALIDATOR, "PACKS_ROOT", root):
                with contextlib.redirect_stdout(output):
                    exit_code = VALIDATOR.main(["--json"])

        self.assertEqual(exit_code, 1)
        report = json.loads(output.getvalue())
        self.assertFalse(report["summary"]["registryValid"])
        self.assertTrue(any(
            "duplicate unit ID across manifests" in issue["message"] and
            "shared:unit" in issue["message"]
            for issue in report["issues"]
        ))

    def test_duplicate_unit_ids_within_manifest_fail_the_tool(self) -> None:
        pack = {
            "packId": "test:duplicate-units",
            "units": [
                {"id": "shared:unit", "productionBuilding": {"id": "test:factory-a"}},
                {"id": "shared:unit", "productionBuilding": {"id": "test:factory-b"}},
            ],
            "stages": [{"id": "test:stage", "unitIds": ["shared:unit"]}],
        }

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            pack_directory = root / "sample-pack"
            pack_directory.mkdir()
            (pack_directory / "content.json").write_text(json.dumps(pack), encoding="utf-8")
            output = io.StringIO()
            with mock.patch.object(VALIDATOR, "PACKS_ROOT", root):
                with contextlib.redirect_stdout(output):
                    exit_code = VALIDATOR.main(["--json"])

        report = json.loads(output.getvalue())
        self.assertEqual(exit_code, 1)
        self.assertFalse(report["summary"]["registryValid"])
        self.assertTrue(any(
            "duplicate unit ID within manifest" in issue["message"] and
            "shared:unit" in issue["message"]
            for issue in report["issues"]
        ))
        pack_report = report["packs"][0]
        self.assertEqual(pack_report["unitFactories"], [])
        self.assertEqual(pack_report["stages"][0]["stageFactoryIds"], [])
        self.assertEqual(pack_report["stages"][0]["unlistedPoolFactoryIds"], [])

    def test_ambiguous_cross_pack_unit_ids_are_not_mapped_to_a_factory(self) -> None:
        packs = (
            ("alpha", {
                "packId": "test:alpha",
                "units": [
                    {"id": "shared:unit", "productionBuilding": {"id": "test:alpha-factory-a"}},
                    {"id": "shared:unit", "productionBuilding": {"id": "test:alpha-factory-b"}},
                ],
                "stages": [{"id": "test:alpha-stage", "unitIds": ["shared:unit"]}],
            }),
            ("beta", {
                "packId": "test:beta",
                "units": [{
                    "id": "shared:unit",
                    "productionBuilding": {"id": "test:beta-factory"},
                }],
                "stages": [{"id": "test:beta-stage", "unitIds": ["shared:unit"]}],
            }),
        )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            for folder, pack in packs:
                pack_directory = root / folder
                pack_directory.mkdir()
                (pack_directory / "content.json").write_text(
                    json.dumps(pack), encoding="utf-8"
                )
            output = io.StringIO()
            with mock.patch.object(VALIDATOR, "PACKS_ROOT", root):
                with contextlib.redirect_stdout(output):
                    exit_code = VALIDATOR.main(["--json"])

        self.assertEqual(exit_code, 1)
        report = json.loads(output.getvalue())
        for pack_report in report["packs"]:
            self.assertEqual(pack_report["stages"][0]["stageFactoryIds"], [])
            self.assertEqual(pack_report["stages"][0]["unlistedPoolFactoryIds"], [])

    def test_scoped_ai_profiles_without_stage_references_fail(self) -> None:
        pack = {
            "stages": [{"id": "test:unrelated-stage"}],
            "aiProfiles": [
                {"id": "test:ai", "scopeToReferencedStages": True},
            ],
        }

        exit_code, output = self._run_pack(pack)

        self.assertEqual(exit_code, 1)
        self.assertIn("no referenced stages", output)

    def test_invalid_utf8_manifest_is_reported_instead_of_raising(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            pack_directory = root / "broken-pack"
            pack_directory.mkdir()
            (pack_directory / "content.json").write_bytes(b"\xff\xfe")
            output = io.StringIO()
            with mock.patch.object(VALIDATOR, "PACKS_ROOT", root):
                with contextlib.redirect_stdout(output):
                    exit_code = VALIDATOR.main(["--json"])

        self.assertEqual(exit_code, 1)
        report = json.loads(output.getvalue())
        self.assertFalse(report["summary"]["registryValid"])
        self.assertIn("unable to read content.json", report["issues"][0]["message"])

    def test_malformed_registry_structures_fail_closed(self) -> None:
        cases = (
            ([], "manifest root must be a JSON object"),
            ({"units": "not-an-array"}, "units must be an array"),
            ({"units": [None]}, "units[0] must be an object"),
            ({"units": [{"id": "test:unit"}]},
             "units[0].productionBuilding must be an object"),
            ({"units": [{"id": "test:unit", "productionBuilding": {"id": " "}}]},
             "units[0].productionBuilding.id must be a non-empty string"),
            ({"stages": [{"id": "test:stage", "blueprintPoolEntries": "not-an-array"}]},
             "stages[0].blueprintPoolEntries must be an array"),
        )

        for pack, expected_diagnostic in cases:
            with self.subTest(expected_diagnostic=expected_diagnostic):
                exit_code, output = self._run_pack(pack)
                self.assertEqual(exit_code, 1)
                self.assertIn(expected_diagnostic, output)
                self.assertNotIn("PASS checked", output)

    def test_cross_pack_duplicate_factory_ids_invalidate_and_are_not_mapped(self) -> None:
        packs = (
            ("alpha", {
                "packId": "test:alpha",
                "units": [{
                    "id": "test:alpha-unit",
                    "productionBuilding": {"id": "test:shared-factory"},
                }],
                "stages": [{"id": "test:alpha-stage", "unitIds": ["test:alpha-unit"]}],
            }),
            ("beta", {
                "packId": "test:beta",
                "units": [{
                    "id": "test:beta-unit",
                    "productionBuilding": {"id": "test:shared-factory"},
                }],
                "stages": [{"id": "test:beta-stage", "unitIds": ["test:beta-unit"]}],
            }),
        )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            for folder, pack in packs:
                pack_directory = root / folder
                pack_directory.mkdir()
                (pack_directory / "content.json").write_text(
                    json.dumps(pack), encoding="utf-8"
                )
            output = io.StringIO()
            with mock.patch.object(VALIDATOR, "PACKS_ROOT", root):
                with contextlib.redirect_stdout(output):
                    exit_code = VALIDATOR.main(["--json"])

        self.assertEqual(exit_code, 1)
        report = json.loads(output.getvalue())
        self.assertFalse(report["summary"]["registryValid"])
        self.assertTrue(any(
            "duplicate productionBuilding.id across manifests" in issue["message"] and
            "test:shared-factory" in issue["message"]
            for issue in report["issues"]
        ))
        for pack_report in report["packs"]:
            self.assertEqual(pack_report["stages"][0]["stageFactoryIds"], [])

    def test_duplicate_pool_entries_for_one_stage_are_rejected(self) -> None:
        pack = {
            "stages": [{
                "id": "test:stage",
                "blueprintPoolEntries": [
                    {"poolId": "pool-a", "unitIds": ["test:unit-a"]},
                    {"poolId": "pool-a", "unitIds": ["test:unit-b"]},
                ],
            }],
        }

        exit_code, output = self._run_pack(pack)

        self.assertEqual(exit_code, 1)
        self.assertIn("duplicate blueprint pool ID=pool-a", output)
        self.assertNotIn("PASS checked", output)

    def test_ai_profile_nested_entries_require_arrays_objects_and_typed_bounded_fields(self) -> None:
        cases = (
            ({"unitEntries": "not-an-array"}, "aiProfiles[0].unitEntries must be an array"),
            ({"unitEntries": [None]}, "aiProfiles[0].unitEntries[0] must be an object"),
            ({"unitEntries": [{"unitId": " ", "baseIndex": 0, "startingInAscensionLevel": 0, "countAtStart": 1, "maxLiving": 1}]}, "unitEntries[0].unitId must be a non-empty string"),
            ({"unitEntries": [{"unitId": "test:unit", "baseIndex": True, "startingInAscensionLevel": 0, "countAtStart": 1, "maxLiving": 1}]}, "unitEntries[0].baseIndex must be an integer"),
            ({"unitEntries": [{"unitId": "test:unit", "baseIndex": 32, "startingInAscensionLevel": 0, "countAtStart": 1, "maxLiving": 1}]}, "unitEntries[0].baseIndex must be between 0 and 31"),
            ({"unitEntries": [{"unitId": "test:unit", "baseIndex": 0, "startingInAscensionLevel": 20, "countAtStart": 1, "maxLiving": 1}]}, "unitEntries[0].startingInAscensionLevel must be between 0 and 19"),
            ({"unitEntries": [{"unitId": "test:unit", "baseIndex": 0, "startingInAscensionLevel": 0, "countAtStart": 0, "maxLiving": 1}]}, "unitEntries[0].countAtStart must be between 1 and 8"),
            ({"unitEntries": [{"unitId": "test:unit", "baseIndex": 0, "startingInAscensionLevel": 0, "countAtStart": 2, "maxLiving": 1}]}, "unitEntries[0].maxLiving must be at least countAtStart and at most 32"),
            ({"productionBuildingEntries": "not-an-array"}, "aiProfiles[0].productionBuildingEntries must be an array"),
            ({"productionBuildingEntries": [None]}, "aiProfiles[0].productionBuildingEntries[0] must be an object"),
            ({"productionBuildingEntries": [{"unitId": "test:unit", "baseIndex": 0, "startingInAscensionLevel": 0, "maxLiving": True}]}, "productionBuildingEntries[0].maxLiving must be an integer"),
            ({"productionBuildingEntries": [{"unitId": "test:unit", "baseIndex": 0, "startingInAscensionLevel": 0, "maxLiving": 0}]}, "productionBuildingEntries[0].maxLiving must be between 1 and 32"),
            ({"productionBuildingEntries": [{"unitId": "test:unit", "baseIndex": 0, "startingInAscensionLevel": 0, "maxLiving": 33}]}, "productionBuildingEntries[0].maxLiving must be between 1 and 32"),
        )

        for fields, expected_diagnostic in cases:
            with self.subTest(expected_diagnostic=expected_diagnostic):
                pack = {"aiProfiles": [{"id": "test:ai", **fields}]}
                diagnostics = VALIDATOR._validate_registry_structure(pack)
                self.assertTrue(any(expected_diagnostic in item for item in diagnostics), diagnostics)

    def test_ai_profile_nested_entries_valid_boundary_controls_pass_structural_check(self) -> None:
        pack = {"aiProfiles": [{
            "id": "test:ai",
            "unitEntries": [{
                "unitId": "test:unit", "baseIndex": 31,
                "startingInAscensionLevel": 19, "countAtStart": 8, "maxLiving": 32,
            }],
            "productionBuildingEntries": [{
                "unitId": "test:unit", "baseIndex": 0,
                "startingInAscensionLevel": 0, "maxLiving": 1,
            }],
        }]}
        self.assertEqual(VALIDATOR._validate_registry_structure(pack), [])

    def test_valid_manifest_returns_success(self) -> None:
        pack = {
            "units": [
                {"id": "test:unit-a", "productionBuilding": {"id": "test:factory-a"}},
                {"id": "test:unit-b", "productionBuilding": {"id": "test:factory-b"}},
            ],
            "stages": [
                {
                    "id": "test:stage-a",
                    "blueprintPoolEntries": [
                        {"poolId": "pool-a", "unitIds": ["test:unit-a"]}
                    ],
                }
            ],
            "aiProfiles": [],
        }

        exit_code, output = self._run_pack(pack)

        self.assertEqual(exit_code, 0)
        self.assertIn("PASS checked 1 manifests", output)
        self.assertNotIn("FAIL", output)


if __name__ == "__main__":
    unittest.main()
