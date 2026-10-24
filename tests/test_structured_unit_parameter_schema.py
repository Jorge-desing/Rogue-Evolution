"""Keep the JSON contract aligned with float32 runtime parsing."""

from __future__ import annotations

import json
import math
import struct
import unittest
from pathlib import Path

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]
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



if __name__ == "__main__":
    unittest.main()
