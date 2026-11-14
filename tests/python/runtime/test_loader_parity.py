"""Loader parity guard: schema customBalance keys and structured bounds match C#."""
from pathlib import Path
import json
import re
import unittest

from tests.python.helpers import REPO_ROOT

SCHEMA = REPO_ROOT / "content" / "schema" / "content.schema.json"
DEFS = REPO_ROOT / "src" / "RogueCommandExpansionLoader" / "Content" / "ContentPackDefinition.cs"
VALIDATION = REPO_ROOT / "src" / "RogueCommandExpansionLoader" / "Content" / "ContentPackValidation.cs"


def _find_first(obj, key):
    if isinstance(obj, dict):
        for k, v in obj.items():
            if k == key:
                return v
            r = _find_first(v, key)
            if r is not None:
                return r
    elif isinstance(obj, list):
        for x in obj:
            r = _find_first(x, key)
            if r is not None:
                return r
    return None


def _find_all(obj, key):
    out = []
    if isinstance(obj, dict):
        for k, v in obj.items():
            if k == key:
                out.append(v)
            out.extend(_find_all(v, key))
    elif isinstance(obj, list):
        for x in obj:
            out.extend(_find_all(x, key))
    return out


class TestLoaderParity(unittest.TestCase):
    def test_custom_balance_keys_match(self):
        schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
        cb = _find_first(schema, "customBalance")
        self.assertIsNotNone(cb, "missing customBalance in schema")
        schema_keys = set(cb["properties"].keys())
        cs_text = DEFS.read_text(encoding="utf-8")
        entries = re.findall(
            r'new BalanceParameterDefinition\("(\w+)",\s*(\d+),\s*"(\w+)",\s*BalanceParameterType\.(\w+)(?:,\s*"[^"]*")?\)',
            cs_text,
        )
        cs_keys = {entry[0] for entry in entries}
        self.assertEqual(schema_keys, cs_keys, "ParamMap/schema customBalance drift")
        # Global-only value must stay excluded from per-unit map
        self.assertNotIn("ManaRechargePerSecond", cs_keys)
        self.assertNotIn("ManaRechargePerSecond", schema_keys)
        self.assertEqual(len(cs_keys), 25, "expected 25 per-unit keys")
        self.assertEqual(len(entries), 25, "expected 25 canonical mapping entries")
        ordinals = {int(entry[1]) for entry in entries}
        self.assertEqual(ordinals, set(range(26)) - {23}, "enum ordinals must skip global-only 23")
        rows = {entry[0]: entry[2] for entry in entries}
        self.assertEqual(rows["Attack1Cooldown"], "attackCooldown")
        self.assertEqual(rows["SpawnTtl"], "duration2")

    def test_card_model_scaling_bounds(self):
        schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
        defs = _find_all(schema, "cardModelScalingFactor")
        self.assertGreater(len(defs), 0, "missing cardModelScalingFactor in schema")
        for d in defs:
            self.assertAlmostEqual(d["exclusiveMinimum"], 0.01000000024214387)
            self.assertAlmostEqual(d["maximum"], 100.00000381469727)
        val = VALIDATION.read_text(encoding="utf-8")
        # C# string check: (0.01, 100.0]
        self.assertIn("(0.01, 100.0]", val)

    def test_fire_point_and_grid_and_capacity_bounds(self):
        schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
        val = VALIDATION.read_text(encoding="utf-8")
        for name, mn, mx in [("firePointCount", 1, 64), ("gridSize", 1, 16), ("maxCapacity", 0, 100)]:
            defs = _find_all(schema, name)
            self.assertGreater(len(defs), 0, f"missing {name} in schema")
            # At least one definition must carry the expected min/max (buildings use enum for gridSize)
            has_bounds = any(
                d.get("minimum") == mn and d.get("maximum") == mx for d in defs
            )
            has_enum = any(d.get("enum") == [1, 2] for d in defs) if name == "gridSize" else False
            self.assertTrue(has_bounds or has_enum, f"{name} schema bounds drift: {defs}")
            # C# text contains the bound phrasing
            if name == "firePointCount":
                self.assertIn("between 1 and 64", val)
            elif name == "maxCapacity":
                self.assertIn("between 0 and 100", val)
            else:
                self.assertIn("between 1 and 16", val)


if __name__ == "__main__":
    unittest.main()
