from __future__ import annotations
import json
import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any


def _load_simulator() -> Any:
    script_path = Path(__file__).resolve().parents[1] / "tools" / "simulate_combat_balance.py"
    spec = importlib.util.spec_from_file_location("simulate_combat_balance", script_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load simulator from {script_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


SIMULATOR = _load_simulator()


class ContentPackBalanceSimulationTests(unittest.TestCase):
    def _load_unit(self, unit: dict[str, Any]) -> Any:
        manifest = {"units": [unit]}
        with tempfile.TemporaryDirectory() as temp_dir:
            content_path = Path(temp_dir) / "content.json"
            content_path.write_text(json.dumps(manifest), encoding="utf-8")
            repository = SIMULATOR.BalanceRepository()
            self.assertEqual(repository.load_content_pack(content_path), [unit["id"]])
            return repository.profiles[unit["id"]]

    def _assert_manifest_rejected(self, manifest_json: str) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            content_path = Path(temp_dir) / "content.json"
            content_path.write_text(manifest_json, encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "manifest|units|unit|balance|customBalance|movement|finite|number|override"):
                SIMULATOR.BalanceRepository().load_content_pack(content_path)

    def test_rejects_non_finite_json_constants(self) -> None:
        for constant in ("NaN", "Infinity", "-Infinity"):
            with self.subTest(constant=constant):
                self._assert_manifest_rejected(
                    '{"units":[{"id":"test:unit","customBalance":{"MoveSpeed":'
                    + constant
                    + "}}]}"
                )

    def test_rejects_non_finite_numeric_strings(self) -> None:
        for value in ("NaN", "Infinity", "-Infinity"):
            with self.subTest(value=value):
                self._assert_manifest_rejected(
                    json.dumps({"units": [{"id": "test:unit", "customBalance": {"MoveSpeed": value}}]})
                )

    def test_rejects_malformed_movement_speed(self) -> None:
        for value in (None, "not-a-number", "NaN", "Infinity"):
            with self.subTest(value=value):
                self._assert_manifest_rejected(
                    json.dumps({"units": [{"id": "test:unit", "movement": {"moveSpeed": value}}]})
                )

    def test_rejects_invalid_top_level_and_units_shapes(self) -> None:
        for manifest in ("null", "[]", '{"units":null}', '{"units":{}}', '{"units":[null]}', '{"units":[[]]}'):
            with self.subTest(manifest=manifest):
                self._assert_manifest_rejected(manifest)

    def test_rejects_malformed_unit_ids(self) -> None:
        invalid_units = (
            {},
            {"id": None},
            {"id": ""},
            {"id": " "},
            {"id": {"nested": "unit"}},
            {"id": ["test:unit"]},
            {"id": 42},
            {"id": True},
        )
        for unit in invalid_units:
            with self.subTest(unit=unit):
                self._assert_manifest_rejected(json.dumps({"units": [unit]}))

    def test_rejects_malformed_native_template_ids(self) -> None:
        for template_id in (None, "", " ", {"nested": "template"}, ["PCXBaseTank"], 42, True):
            with self.subTest(nativeTemplateId=template_id):
                self._assert_manifest_rejected(
                    json.dumps({"units": [{"id": "test:unit", "nativeTemplateId": template_id}]})
                )

    def test_custom_unit_without_native_template_id_loads(self) -> None:
        profile = self._load_unit({"id": "test:custom-unit"})

        self.assertEqual(profile.unit_id, "test:custom-unit")
        self.assertEqual(profile.source, "mod")
        self.assertIsNone(profile.template_id)

    def test_rejects_malformed_localizations(self) -> None:
        invalid_values = (None, {}, "bad", [], [None], [[]], [{"language": []}], [{"language": "en-US", "name": None}])
        for localizations in invalid_values:
            with self.subTest(localizations=localizations):
                self._assert_manifest_rejected(
                    json.dumps(
                        {
                            "units": [
                                {
                                    "id": "test:unit",
                                    "nativeTemplateId": "PCXBaseTank",
                                    "localizations": localizations,
                                }
                            ]
                        }
                    )
                )

    def test_rejects_non_object_custom_balance_and_movement(self) -> None:
        for key in ("customBalance", "movement"):
            for value in (None, [], "bad", 12):
                with self.subTest(key=key, value=value):
                    self._assert_manifest_rejected(
                        json.dumps({"units": [{"id": "test:unit", key: value}]})
                    )

    def test_rejects_malformed_balance_overrides_and_numeric_values(self) -> None:
        invalid_units = (
            {"balanceOverrides": None},
            {"balanceOverrides": {}},
            {"balanceOverrides": [None]},
            {"balanceOverrides": [{"value": "1"}]},
            {"balanceOverrides": [{"name": "cost"}]},
            {"balanceOverrides": [{"name": "cost", "value": None}]},
            {"balanceOverrides": [{"name": "firePointCount", "value": "nope"}]},
            {"balanceOverrides": [{"name": "cost", "value": "Infinity"}]},
        )
        for fields in invalid_units:
            with self.subTest(fields=fields):
                self._assert_manifest_rejected(json.dumps({"units": [{"id": "test:unit", **fields}]}))

    def test_rejects_booleans_as_numeric_values(self) -> None:
        invalid_units = (
            {"balanceOverrides": [{"name": "moveSpeed", "value": True}]},
            {"customBalance": {"MoveSpeed": True}},
            {"movement": {"moveSpeed": True}},
        )
        for fields in invalid_units:
            with self.subTest(fields=fields):
                self._assert_manifest_rejected(json.dumps({"units": [{"id": "test:unit", **fields}]}))

    def test_custom_balance_overrides_raw_balance_overrides(self) -> None:
        profile = self._load_unit(
            {
                "id": "test:unit",
                "nativeTemplateId": "PCXBaseTank",
                "movement": {"moveSpeed": 16.5},
                "balanceOverrides": [
                    {"name": "cost", "value": "80"},
                    {"name": "maxHealth", "value": "120"},
                    {"name": "moveSpeed", "value": "14.5"},
                    {"name": "damage1", "value": "8"},
                    {"name": "attackCooldown", "value": "0.9"},
                    {"name": "firePointCount", "value": "6"},
                ],
                "customBalance": {
                    "Cost": 65,
                    "MaxHealth": 110,
                    "MoveSpeed": 15.25,
                    "Damage1": 2.5,
                    "Attack1Cooldown": 1.2,
                },
            }
        )

        self.assertEqual(profile.cost, 65)
        self.assertEqual(profile.max_health, 110)
        self.assertEqual(profile.move_speed, 15.25)
        self.assertEqual(profile.damage1, 2.5)
        self.assertEqual(profile.attack_cooldown, 1.2)
        self.assertEqual(profile.fire_point_count, 6)

    def test_raw_secondary_weapon_overrides_update_simulated_dps(self) -> None:
        profile = self._load_unit(
            {
                "id": "test:secondary-weapon",
                "nativeTemplateId": "PCXBaseTank",
                "balanceOverrides": [
                    {"name": "damage2", "value": "9.5"},
                    {"name": "attack2Cooldown", "value": "2.5"},
                ],
            }
        )

        self.assertEqual(profile.damage2, 9.5)
        self.assertEqual(profile.attack2_cooldown, 2.5)
        self.assertAlmostEqual(profile.dps, (profile.damage1 * profile.fire_point_count / profile.attack_cooldown) + 3.8)

    def test_structured_move_speed_is_fallback_after_raw_balance(self) -> None:
        structured_only = self._load_unit(
            {
                "id": "test:structured-speed",
                "nativeTemplateId": "PCXBaseTank",
                "movement": {"moveSpeed": 17.0},
            }
        )
        raw_precedence = self._load_unit(
            {
                "id": "test:raw-speed",
                "nativeTemplateId": "PCXBaseTank",
                "movement": {"moveSpeed": 17.0},
                "balanceOverrides": [{"name": "moveSpeed", "value": "12.25"}],
            }
        )

        self.assertEqual(structured_only.move_speed, 17.0)
        self.assertEqual(raw_precedence.move_speed, 12.25)

    def test_sample_scout_stays_within_light_recon_balance_band(self) -> None:
        repository = SIMULATOR.BalanceRepository()
        pack_path = (
            Path(__file__).resolve().parents[1]
            / "content"
            / "packs"
            / "sample-native-expansion"
            / "content.json"
        )
        loaded_units = repository.load_content_pack(pack_path)
        self.assertIn("sample-native:scout-tank", loaded_units)
        scout = repository.profiles["sample-native:scout-tank"]

        self.assertGreaterEqual(scout.dps, 7.0)
        self.assertLessEqual(scout.dps, 10.0)
        self.assertGreaterEqual(scout.cost_efficiency_index, 30.0)
        self.assertLessEqual(scout.cost_efficiency_index, 45.0)

        manifest = json.loads(pack_path.read_text(encoding="utf-8"))
        scout_definition = next(
            unit for unit in manifest["units"] if unit["id"] == scout.unit_id
        )
        self.assertEqual(scout_definition.get("customBalance", {}).get("Damage1"), 4.0)
        self.assertFalse(
            any(
                override.get("name", "").lower() == "damage1"
                for override in scout_definition.get("balanceOverrides", [])
            )
        )

    def test_sample_heavy_mechs_stay_within_assault_mech_balance_band(self) -> None:
        repository = SIMULATOR.BalanceRepository()
        pack_path = (
            Path(__file__).resolve().parents[1]
            / "content"
            / "packs"
            / "sample-native-expansion"
            / "content.json"
        )
        repository.load_content_pack(pack_path)

        for unit_id in (
            "sample-native:assault-walker",
            "sample-native:atlas-crane-mech",
        ):
            with self.subTest(unit_id=unit_id):
                profile = repository.profiles[unit_id]
                self.assertGreaterEqual(profile.dps, 8.0)
                self.assertLessEqual(profile.dps, 14.0)
                self.assertGreaterEqual(profile.cost_efficiency_index, 10.0)
                self.assertLessEqual(profile.cost_efficiency_index, 18.0)


if __name__ == "__main__":
    unittest.main()
