"""Contracts for previously unused runtime mechanics on modeled units."""

from __future__ import annotations

import json
import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module

SIMULATOR = load_tool_module("simulate_combat_balance")
ROOT = Path(__file__).resolve().parents[3]
PACK_PATHS = (
    ROOT / "content" / "packs" / "sample-native-expansion" / "content.json",
    ROOT / "content" / "packs" / "tactical-operations" / "content.json",
)


class UnusedModeledRuntimeMechanicsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.definitions = {}
        cls.profiles = {}
        for path in PACK_PATHS:
            manifest = json.loads(path.read_text(encoding="utf-8"))
            repository = SIMULATOR.BalanceRepository()
            repository.load_content_pack(path)
            cls.definitions.update({unit["id"]: unit for unit in manifest["units"]})
            cls.profiles.update(repository.profiles)

    def test_sample_support_commander_and_shield_units_use_runtime_mechanics(self) -> None:
        atlas = self.definitions["sample-native:atlas-crane-mech"]
        overlord = self.definitions["sample-native:overlord-command-hq"]
        surge = self.definitions["sample-native:surge-tank"]

        self.assertIsNotNone(
            atlas.get("healing"),
            "Atlas Crane's Heal/Supporter role is not wired to its implemented healing mechanic",
        )
        self.assertGreater(atlas["healing"]["healRange"], 0)
        self.assertGreater(atlas["healing"]["healAmount"], 0)
        self.assertGreater(atlas["healing"]["repairAmount"], 0)
        self.assertIsNotNone(overlord.get("command"), "Overlord Command HQ does not use its implemented command mechanic")
        self.assertEqual(overlord["command"]["commandType"], "Defend")
        self.assertGreater(overlord["command"]["commandRange"], 0)
        self.assertIsNotNone(surge.get("shieldRegeneration"), "Surge Tank has shield capacity but no implemented shield regeneration")
        self.assertIsNotNone(surge.get("shieldOverloadNova"), "Surge Tank has shield capacity but no implemented shield-collapse mechanic")
        self.assertGreater(surge["shieldRegeneration"]["rechargeRate"], 0)
        self.assertGreater(surge["shieldOverloadNova"]["novaRadius"], 0)

    def test_shield_mechanics_only_target_units_with_shield_capacity(self) -> None:
        shield_fields = ("shieldRegeneration", "shieldOverloadNova")
        for unit_id, definition in self.definitions.items():
            declared = [field for field in shield_fields if definition.get(field) is not None]
            if not declared:
                continue
            with self.subTest(unit_id=unit_id, declared=declared):
                self.assertGreater(
                    self.profiles[unit_id].max_shield,
                    0,
                    f"{unit_id} declares {', '.join(declared)} but has maxShield=0",
                )

    def test_arc_lance_leech_restores_health_without_shield_capacity(self) -> None:
        self.assertFalse(
            self.definitions["tactical-operations:arc-lance-walker"]["vampiricLeech"]["siphonToShield"],
            "Arc Lance has no shield capacity, so leech must heal health rather than wasting shield logic",
        )


if __name__ == "__main__":
    unittest.main()
