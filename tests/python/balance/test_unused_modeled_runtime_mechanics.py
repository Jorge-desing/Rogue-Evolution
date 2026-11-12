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

    def test_simulator_imports_manifest_critical_hits_and_shield_recharge(self) -> None:
        skyguard = self.profiles["tactical-operations:skyguard-sam"]
        self.assertAlmostEqual(skyguard.crit_chance, 0.3)

        surge = self.profiles["sample-native:surge-tank"]
        self.assertAlmostEqual(surge.shield_recharge_per_second, 1.5)
        self.assertAlmostEqual(surge.shield_recharge_cooldown, 4.0)

        scout = self.profiles["sample-native:scout-tank"]
        self.assertEqual(scout.shield_recharge_per_second, 0.0)
        self.assertEqual(scout.shield_recharge_cooldown, 0.0)

    def test_arc_lance_leech_restores_health_without_shield_capacity(self) -> None:
        self.assertFalse(
            self.definitions["tactical-operations:arc-lance-walker"]["vampiricLeech"]["siphonToShield"],
            "Arc Lance has no shield capacity, so leech must heal health rather than wasting shield logic",
        )

    def test_new_tactical_units_use_combat_experience_rewards(self) -> None:
        expected = {
            "tactical-operations:sentinel-relay": 2,
            "tactical-operations:gravitas-breaker": 3,
            "tactical-operations:command-relay": 2,
        }
        for unit_id, amount in expected.items():
            with self.subTest(unit_id=unit_id):
                reward = self.definitions[unit_id].get("experienceReward")
                self.assertIsNotNone(reward, f"{unit_id} is missing the implemented combat experience reward")
                self.assertEqual(reward["experienceType"], "Combat")
                self.assertEqual(reward["experienceAmount"], amount)
                self.assertEqual(reward["bonusMultiplier"], 1.0)

    def test_new_tactical_units_use_role_specific_on_hit_controls(self) -> None:
        expected = {
            "tactical-operations:sentinel-relay": ("SlowedDown", 1.5, 0.25),
            "tactical-operations:gravitas-breaker": ("SlowedDown", 2.5, 0.30),
            "tactical-operations:command-relay": ("Marked", 3.0, 0.35),
        }
        for unit_id, (status, duration, chance) in expected.items():
            with self.subTest(unit_id=unit_id):
                effects = self.definitions[unit_id].get("onHitStatusEffects")
                self.assertIsNotNone(effects, f"{unit_id} is missing its loader-backed on-hit control")
                self.assertEqual(len(effects), 1)
                self.assertEqual(effects[0]["statusEffect"], status)
                self.assertEqual(effects[0]["duration"], duration)
                self.assertEqual(effects[0]["chance"], chance)

    def test_siege_and_command_units_declare_loader_backed_variants(self) -> None:
        gravitas = self.definitions["tactical-operations:gravitas-breaker"]
        command = self.definitions["tactical-operations:command-relay"]
        gravitas_variants = {variant["id"]: variant for variant in gravitas.get("variants", [])}
        command_variants = {variant["id"]: variant for variant in command.get("variants", [])}
        self.assertEqual(set(gravitas_variants), {"gravity-ap", "gravity-he"})
        self.assertEqual(set(command_variants), {"relay-ap", "relay-he"})
        self.assertEqual(gravitas["minAttackRange"], 4.5)
        self.assertEqual(gravitas_variants["gravity-ap"]["loadout"], "AP")
        self.assertEqual(gravitas_variants["gravity-he"]["loadout"], "HE")
        self.assertEqual(command_variants["relay-ap"]["loadout"], "AP")
        self.assertEqual(command_variants["relay-he"]["loadout"], "HE")
        self.assertEqual(
            {item["name"]: item["value"] for item in gravitas_variants["gravity-ap"]["balanceOverrides"]},
            {"damage1": "34", "attackCooldown": "4.6"},
        )
        self.assertEqual(
            {item["name"]: item["value"] for item in gravitas_variants["gravity-he"]["balanceOverrides"]},
            {"damage1": "26", "effectRadius1": "1.5"},
        )
        self.assertEqual(
            {item["name"]: item["value"] for item in command_variants["relay-ap"]["balanceOverrides"]},
            {"damage1": "13.5", "attackCooldown": "2.2"},
        )
        self.assertEqual(
            {item["name"]: item["value"] for item in command_variants["relay-he"]["balanceOverrides"]},
            {"damage1": "10.5", "effectRadius1": "0.45"},
        )


if __name__ == "__main__":
    unittest.main()
