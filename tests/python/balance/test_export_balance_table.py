"""Tests de exportación de tabla de balance."""
from __future__ import annotations
import csv
import io
import unittest
from tests.python.helpers import load_tool_module

EXPORT = load_tool_module("export_balance_table")
SCAFFOLD = load_tool_module("scaffold_pack")

def _valid_pack() -> dict:
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class ExportBalanceTableTests(unittest.TestCase):
    def test_valid_pack_rows(self) -> None:
        pack = _valid_pack()
        rows = EXPORT.export_balance_table(pack)
        self.assertEqual(1, len(rows))
        row = rows[0]
        self.assertEqual(pack["units"][0]["id"], row["unitId"])
        self.assertEqual("PCXBaseTank", row["nativeTemplateId"])

    def test_custom_balance_overrides_raw(self) -> None:
        pack = _valid_pack()
        unit = pack["units"][0]
        # raw override + customBalance: customBalance wins
        unit["balanceOverrides"] = [{"name": "cost", "value": "100"}]
        unit["customBalance"] = {"Cost": 55, "MaxHealth": 200}
        rows = EXPORT.export_balance_table(pack)
        self.assertEqual(55, rows[0]["Cost"])
        self.assertEqual(200, rows[0]["MaxHealth"])

        unit["productionQueue"] = {"buildTime": 12.5}
        self.assertEqual(12.5, EXPORT.export_balance_table(pack)[0]["ProductionDuration"])
        unit["balanceOverrides"].append({"name": "productionDuration", "value": "7"})
        self.assertEqual(7, EXPORT.export_balance_table(pack)[0]["ProductionDuration"])
        unit["customBalance"]["ProductionDuration"] = 3
        self.assertEqual(3, EXPORT.export_balance_table(pack)[0]["ProductionDuration"])

    def test_raw_override_parsed(self) -> None:
        pack = _valid_pack()
        unit = pack["units"][0]
        unit["balanceOverrides"] = [
            {"name": "cost", "value": "70"},
            {"name": "maxHealth", "value": "130"},
            {"name": "damage1", "value": "4.5"},
        ]
        rows = EXPORT.export_balance_table(pack)
        self.assertEqual(70, rows[0]["Cost"])
        self.assertEqual(130, rows[0]["MaxHealth"])
        self.assertAlmostEqual(4.5, rows[0]["Damage1"])

    def test_structured_fields_included(self) -> None:
        pack = _valid_pack()
        unit = pack["units"][0]
        unit["combatValue"] = 12
        unit["gridSize"] = 2
        rows = EXPORT.export_balance_table(pack)
        self.assertEqual(12, rows[0]["combatValue"])
        self.assertEqual(2, rows[0]["gridSize"])

    def test_empty_pack_empty_rows(self) -> None:
        self.assertEqual([], EXPORT.export_balance_table({}))

    def test_all_25_canonical_and_raw_names_have_columns(self) -> None:
        canonical = {
            "Cost", "ProductionDuration", "MaxCapacity", "MaxHealth", "MaxShield",
            "MaxMana", "MaxRank", "MoveSpeed", "SightRadius", "Damage1", "Damage2",
            "WeaponRange", "Attack1Cooldown", "Attack2Cooldown", "EffectRadius1",
            "EffectRadius2", "SkillRange", "HealAmount1", "HealAmount2",
            "GainCreditsAmount", "SkillManaCost", "Duration1", "SpawnTtl",
            "MaxArmor", "ArmorProtection",
        }
        self.assertEqual(canonical, set(EXPORT.CUSTOM_TO_COLUMN.keys()))
        # Every canonical key must be reachable from its exact raw row field.
        for key, column in EXPORT.CUSTOM_TO_COLUMN.items():
            self.assertIn(column, EXPORT.RAW_TO_COLUMN.values(), key)
        # The two non-obvious raw mappings must stay exact.
        self.assertEqual("Attack1Cooldown", EXPORT.RAW_TO_COLUMN["attackCooldown"])
        self.assertEqual("SpawnTtl", EXPORT.RAW_TO_COLUMN["duration2"])
        self.assertNotIn("ManaRechargePerSecond", EXPORT.CUSTOM_TO_COLUMN)
        self.assertNotIn("ManaRechargePerSecond", EXPORT.RAW_TO_COLUMN.values())

    def test_to_csv_roundtrip(self) -> None:
        pack = _valid_pack()
        rows = EXPORT.export_balance_table(pack)
        text = EXPORT.rows_to_csv(rows)
        reader = list(csv.DictReader(io.StringIO(text)))
        self.assertEqual(1, len(reader))
        self.assertEqual(pack["units"][0]["id"], reader[0]["unitId"])


if __name__ == "__main__":
    unittest.main()
