"""Balance contract for the six Tactical Operations modded units."""

from __future__ import annotations

import json
import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module

SIMULATOR = load_tool_module("simulate_combat_balance")
ROOT = Path(__file__).resolve().parents[3]
PACK_PATH = ROOT / "content" / "packs" / "tactical-operations" / "content.json"


class TacticalOperationsCalibrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest = json.loads(PACK_PATH.read_text(encoding="utf-8"))
        cls.repository = SIMULATOR.BalanceRepository()
        cls.unit_ids = cls.repository.load_content_pack(PACK_PATH)
        cls.units = cls.repository.profiles
        cls.definitions = {unit["id"]: unit for unit in cls.manifest["units"]}

    def test_six_tactical_unit_profiles_match_calibration_targets(self) -> None:
        expected = {
            "tactical-operations:tidebreaker-skirmisher": {
                "cost": 85.0, "max_health": 140.0, "max_shield": 0.0,
                "armor_protection": 1.0, "move_speed": 14.5, "weapon_range": 3.8,
                "damage1": 5.0, "dps": 8.3333333333,
                "cost_efficiency_index": 32.8037101677,
            },
            "tactical-operations:salvage-hauler": {
                "cost": 250.0, "production_duration": 90.0, "max_health": 110.0,
                "max_shield": 3.0, "armor_protection": 1.0, "move_speed": 12.0,
                "weapon_range": 3.8, "damage1": 2.5, "dps": 5.0,
            },
            "tactical-operations:skyguard-sam": {
                "cost": 320.0, "max_health": 140.0, "move_speed": 8.5,
                "damage1": 24.0, "dps": 17.1428571429,
            },
            "tactical-operations:bulwark-siege-tank": {
                "cost": 360.0, "max_health": 160.0, "armor_protection": 2.0,
                "damage1": 40.0, "attack_cooldown": 4.5, "weapon_range": 14.0,
                "effect_radius1": 1.2, "dps": 17.7777777778,
                "cost_efficiency_index": 14.9652228823,
            },
            "tactical-operations:arc-lance-walker": {
                "cost": 420.0, "max_health": 400.0, "max_shield": 0.0,
                "move_speed": 11.0, "damage1": 5.0, "dps": 6.25,
                "cost_efficiency_index": 11.0433209521,
            },
            "tactical-operations:field-medic-rover": {
                "cost": 240.0, "production_duration": 90.0, "max_health": 100.0,
                "max_shield": 5.0, "armor_protection": 1.0, "move_speed": 12.0,
                "weapon_range": 3.8, "damage1": 2.5, "dps": 5.0,
            },
        }
        self.assertEqual(set(expected), set(self.unit_ids))
        for unit_id, fields in expected.items():
            profile = self.units[unit_id]
            for field, wanted in fields.items():
                actual = profile.dps if field == "dps" else getattr(profile, field)
                with self.subTest(unit_id=unit_id, field=field):
                    self.assertAlmostEqual(actual, wanted, places=5)

    def test_tactical_units_have_no_unbounded_range_speed_matchup(self) -> None:
        simulator = SIMULATOR.CombatSimulator()
        for unit_id in self.unit_ids:
            profile = self.units[unit_id]
            native = self.units[profile.template_id]
            result = simulator.simulate_1v1_duel(profile, native)
            with self.subTest(unit_id=unit_id):
                self.assertFalse(
                    any(flag.startswith("INFINITE_KITING_ENVELOPE") for flag in result.flags),
                    f"{unit_id} has an unbounded matchup against {native.unit_id}: {result.flags}",
                )

    def test_mobile_sam_is_not_tagged_as_a_suicide_bomb(self) -> None:
        definition = self.definitions["tactical-operations:skyguard-sam"]
        tags = next(
            override["value"]
            for override in definition.get("balanceOverrides", [])
            if override.get("name") == "offeredSystemTags"
        )
        self.assertNotIn("Bomb", [tag.strip() for tag in tags.split(",")])

    def test_support_vehicles_have_long_native_production_times(self) -> None:
        for unit_id in (
            "tactical-operations:salvage-hauler",
            "tactical-operations:field-medic-rover",
        ):
            with self.subTest(unit_id=unit_id):
                self.assertGreater(self.units[unit_id].production_duration, 80.0)

    def test_every_tactical_unit_is_available_to_the_declared_ai_roster(self) -> None:
        profile = self.manifest["aiProfiles"][0]
        roster_ids = {entry["unitId"] for entry in profile["unitEntries"]}
        self.assertEqual(set(self.unit_ids), roster_ids)
        self.assertTrue(all(self.definitions[unit_id].get("isAllowedForAi") for unit_id in roster_ids))


if __name__ == "__main__":
    unittest.main()
