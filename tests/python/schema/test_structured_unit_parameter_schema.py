"""Keep the JSON contract aligned with float32 runtime parsing."""

from __future__ import annotations

import json
import math
import struct
import unittest
from pathlib import Path

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[3]
SCHEMA_PATH = ROOT / "content" / "schema" / "content.schema.json"
SAMPLE_PATH = ROOT / "content" / "packs" / "sample-native-expansion" / "content.json"


def to_float32(value: float) -> float:
    return struct.unpack("!f", struct.pack("!f", value))[0]


def next_float32(value: float) -> float:
    bits = struct.unpack("!I", struct.pack("!f", value))[0]
    return struct.unpack("!f", struct.pack("!I", bits + 1))[0]


class StructuredUnitParameterSchemaTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
        Draft202012Validator.check_schema(cls.schema)
        cls.validator = Draft202012Validator(cls.schema)
        cls.sample = json.loads(SAMPLE_PATH.read_text(encoding="utf-8"))

    def test_card_scale_schema_matches_float32_lower_bound(self) -> None:
        runtime_minimum = to_float32(0.01)
        first_valid_runtime_value = next_float32(runtime_minimum)
        lower_rounding_midpoint = (runtime_minimum + first_valid_runtime_value) / 2.0
        runtime_maximum = to_float32(100.0)
        first_invalid_runtime_value = next_float32(runtime_maximum)
        upper_rounding_midpoint = (runtime_maximum + first_invalid_runtime_value) / 2.0
        probes = (
            0.0100000001,
            lower_rounding_midpoint,
            math.nextafter(lower_rounding_midpoint, math.inf),
            100.0,
            upper_rounding_midpoint,
            math.nextafter(upper_rounding_midpoint, math.inf),
        )

        for value in probes:
            with self.subTest(value=value):
                manifest = json.loads(json.dumps(self.sample))
                manifest["units"][0]["cardModelScalingFactor"] = value
                schema_accepts = self.validator.is_valid(manifest)
                runtime_accepts = to_float32(value) > runtime_minimum and to_float32(value) <= 100.0
                self.assertEqual(
                    schema_accepts,
                    runtime_accepts,
                    f"schema/runtime disagree for cardModelScalingFactor={value}: "
                    f"float32={to_float32(value)}",
                )

    def test_native_grounding_parameters_valid(self) -> None:
        manifest = json.loads(json.dumps(self.sample))
        unit = manifest["units"][0]
        unit["copyUpgradesToEntityId"] = "sample:unit-ironclad"
        unit["roles"] = ["Attacker", "Mech"]
        unit["attackType"] = "Beam"
        unit["skillType"] = "SelfAoE"
        unit["rarity"] = "Rare"
        unit["tech"] = "Mech"
        unit["neededExperienceLevel"] = 5
        unit["systemTags"] = ["Mech", "Armor", "Tank"]
        unit["baselineStatusEffects"] = ["OnFire", "SlowedDown"]
        self.assertTrue(self.validator.is_valid(manifest))

    def test_native_grounding_parameters_invalid_values_rejected(self) -> None:
        invalid_cases = [
            ("copyUpgradesToEntityId", "invalid id with spaces"),
            ("roles", ["Attacker", "Attacker"]),  # duplicate
            ("attackType", "InvalidAttackType"),
            ("skillType", "InvalidSkillType"),
            ("rarity", "SuperRare"),  # native is Common, Rare, UltraRare
            ("tech", "Alien"),
            ("neededExperienceLevel", 101),
            ("neededExperienceLevel", -1),
            ("systemTags", ["InvalidSystemTag"]),
            ("systemTags", ["Mech", "Mech"]),  # duplicate
            ("baselineStatusEffects", ["NotAStatusEffect"]),
            ("baselineStatusEffects", ["OnFire", "OnFire"]),  # duplicate
            ("systemTags", ["Mech"] * 17),  # max 16
            ("baselineStatusEffects", ["OnFire"] * 17),  # max 16
        ]
        for field, invalid_val in invalid_cases:
            with self.subTest(field=field, value=invalid_val):
                manifest = json.loads(json.dumps(self.sample))
                manifest["units"][0][field] = invalid_val
                self.assertFalse(self.validator.is_valid(manifest), f"Expected schema to reject {field}={invalid_val}")

    def test_relic_expanded_card_changes_valid(self) -> None:
        manifest = json.loads(json.dumps(self.sample))
        relic = manifest["relics"][0]
        relic["coinsAmount"] = 150
        relic["inactive"] = False
        relic["cardChanges"] = [
            {
                "valueToChange": "MoveSpeed",
                "operation": "Multiply",
                "value": 1.25,
                "side": "False",
                "cardMustHaveAllOfTheseRoles": ["Mech", "Tank"],
                "cardMustNotHaveOneOfTheseTechs": ["Ancient"],
                "changeableValueHasToBeGreaterZero": True,
            },
            {
                "valueToChange": "Damage1",
                "operation": "Add",
                "value": 5.0,
                "side": "False",
                "onlyForTheseEntityIds": ["sample-native:atlas-crane-mech"],
            },
            {
                "valueToChange": "MaxArmor",
                "operation": "Add",
                "value": 15.0,
                "side": "Irrelevant",
            },
        ]
        self.assertTrue(self.validator.is_valid(manifest))

    def test_relic_expanded_card_changes_invalid_rejected(self) -> None:
        invalid_cases = [
            ("valueToChange", "InvalidStatName"),
            ("coinsAmount", -10),
            ("cardMustHaveAllOfTheseRoles", ["Mech", "Mech"]),  # duplicate
            ("cardMustNotHaveOneOfTheseTechs", ["Laser", "Laser"]),  # duplicate
        ]
        for field, invalid_val in invalid_cases:
            with self.subTest(field=field, value=invalid_val):
                manifest = json.loads(json.dumps(self.sample))
                if field == "coinsAmount":
                    manifest["relics"][0]["coinsAmount"] = invalid_val
                else:
                    manifest["relics"][0]["cardChanges"][0][field] = invalid_val
                self.assertFalse(self.validator.is_valid(manifest), f"Expected schema to reject {field}={invalid_val}")

    def test_production_building_structured_parameters_valid(self) -> None:
        manifest = json.loads(json.dumps(self.sample))
        pb = manifest["units"][0]["productionBuilding"]
        pb["gridSize"] = 2
        pb["maxCapacity"] = 10
        pb["cost"] = 250
        pb["buildTime"] = 12.5
        pb["combatValue"] = 45
        pb["cardModelScalingFactor"] = 1.2
        pb["rarity"] = "Rare"
        pb["tech"] = "Mech"
        pb["neededExperienceLevel"] = 3
        pb["roles"] = ["Building", "Factory", "Defense"]
        pb["systemTags"] = ["Turret", "Armor"]
        self.assertTrue(self.validator.is_valid(manifest))

    def test_production_building_structured_parameters_invalid_rejected(self) -> None:
        invalid_cases = [
            ("gridSize", 0),
            ("gridSize", 3),
            ("maxCapacity", -1),
            ("maxCapacity", 101),
            ("cost", -5),
            ("buildTime", -1.0),
            ("combatValue", -10),
            ("rarity", "Mythic"),
            ("tech", "Alien"),
            ("neededExperienceLevel", 101),
            ("roles", ["Building", "Building"]),  # duplicate
            ("systemTags", ["InvalidTag"]),
        ]
        for field, invalid_val in invalid_cases:
            with self.subTest(field=field, value=invalid_val):
                manifest = json.loads(json.dumps(self.sample))
                manifest["units"][0]["productionBuilding"][field] = invalid_val
                self.assertFalse(self.validator.is_valid(manifest), f"Expected schema to reject productionBuilding.{field}={invalid_val}")

    def test_standalone_buildings_schema_valid(self) -> None:
        manifest = json.loads(json.dumps(self.sample))
        manifest["buildings"] = [
            {
                "id": "sample-native:test-defense-turret",
                "nativeTemplateId": "MachineGunTurret",
                "gridSize": 2,
                "maxCapacity": 4,
                "cost": 150,
                "buildTime": 8.0,
                "combatValue": 25,
                "rarity": "Common",
                "tech": "Tank",
                "neededExperienceLevel": 0,
                "roles": ["Building", "Turret", "Defense"],
                "systemTags": ["Turret", "Armor"],
                "cardModelScalingFactor": 1.0,
                "firePointCount": 2,
                "damage1": 15.0,
                "attackCooldown": 1.0,
                "weaponRange": 14.0,
                "damage2": 30.0,
                "attack2Cooldown": 3.0,
                "attackType": "Single",
                "skillType": "None",
                "isAllowedAsBlueprint": True,
                "isAllowedAsStartingBlueprint": False,
                "isAllowedForAi": True,
                "isAllowedForDemo": True,
                "isForSpecialists": False,
                "localizations": [
                    {
                        "language": "en-US",
                        "name": "Twin Machinegun Turret",
                        "description": "Stationary defense post.",
                    }
                ],
            }
        ]
        self.assertTrue(self.validator.is_valid(manifest))

    def test_standalone_buildings_schema_invalid_rejected(self) -> None:
        invalid_cases = [
            ("id", "invalid id with spaces"),
            ("gridSize", 3),
            ("maxCapacity", -1),
            ("cost", -1),
            ("buildTime", -0.5),
            ("combatValue", -1),
            ("damage1", -5.0),
            ("weaponRange", -1.0),
            ("roles", ["Building", "Building"]),  # duplicate
            ("systemTags", ["InvalidTag"]),
            ("attackType", "InvalidAttackType"),
            ("rarity", "Legendary"),
            ("rotationDegreesPerSecond", 5.0),
            ("collisionRadius", 60.0),
            ("attackWarmUpTime", 15.0),
            ("rangeToChooseNewTargetAutomatically", -5),
            ("prespawnCount", 150),
        ]
        for field, invalid_val in invalid_cases:
            with self.subTest(field=field, value=invalid_val):
                manifest = json.loads(json.dumps(self.sample))
                manifest["buildings"] = [
                    {
                        "id": "sample-native:test-turret",
                        "nativeTemplateId": "MachineGunTurret",
                        field: invalid_val,
                    }
                ]
                self.assertFalse(self.validator.is_valid(manifest), f"Expected schema to reject building.{field}={invalid_val}")

    def test_unit_behavioral_and_audio_schema_valid(self) -> None:
        manifest = json.loads(json.dumps(self.sample))
        unit = manifest["units"][0]
        unit["rotationDegreesPerSecond"] = 180.0
        unit["collisionRadius"] = 2.5
        unit["attackWarmUpTime"] = 0.3
        unit["canAttackWhileMoving"] = True
        unit["shootAutomaticallyOnEnemiesWithinRange"] = True
        unit["defendPositionAggressivelyWhenIdle"] = True
        unit["rangeToChooseNewTargetAutomatically"] = 15
        unit["prespawnCount"] = 10
        unit["selectionSoundAssetName"] = "audio/select.wav"
        unit["engineSoundAssetName"] = "audio/engine_loop.wav"
        unit["weaponSoundAssetName"] = "audio/shot.wav"
        self.assertTrue(self.validator.is_valid(manifest))

    def test_unit_behavioral_schema_invalid_rejected(self) -> None:
        invalid_cases = [
            ("rotationDegreesPerSecond", 9.9),
            ("rotationDegreesPerSecond", 720.1),
            ("collisionRadius", 0.49),
            ("collisionRadius", 50.1),
            ("attackWarmUpTime", -0.1),
            ("attackWarmUpTime", 10.1),
            ("rangeToChooseNewTargetAutomatically", -2),
            ("rangeToChooseNewTargetAutomatically", 1001),
            ("rangeToChooseNewTargetAutomatically", 12.5),
            ("prespawnCount", -1),
            ("prespawnCount", 101),
            ("prespawnCount", 5.5),
            ("canAttackWhileMoving", "true"),
        ]
        for field, invalid_val in invalid_cases:
            with self.subTest(field=field, value=invalid_val):
                manifest = json.loads(json.dumps(self.sample))
                manifest["units"][0][field] = invalid_val
                self.assertFalse(self.validator.is_valid(manifest), f"Expected schema to reject {field}={invalid_val}")

    def test_tactical_operations_pack_validates_against_schema_with_runtime_mechanics(self) -> None:
        tactical_path = ROOT / "content" / "packs" / "tactical-operations" / "content.json"
        tactical_manifest = json.loads(tactical_path.read_text(encoding="utf-8"))
        self.assertTrue(self.validator.is_valid(tactical_manifest))
        units_by_id = {u["id"]: u for u in tactical_manifest["units"]}
        self.assertIn("stealth", units_by_id["tactical-operations:tidebreaker-skirmisher"])
        self.assertIn("experienceReward", units_by_id["tactical-operations:tidebreaker-skirmisher"])
        self.assertIn("synergies", units_by_id["tactical-operations:salvage-hauler"])
        self.assertIn("command", units_by_id["tactical-operations:bulwark-siege-tank"])
        self.assertIn("synergies", units_by_id["tactical-operations:field-medic-rover"])


if __name__ == "__main__":
    unittest.main()
