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
        # Cost comes from balanceOverrides (scaffold does not set cost) -> None
        self.assertIn("unitId", row)

    def test_custom_balance_overrides_raw(self) -> None:
        pack = _valid_pack()
        unit = pack["units"][0]
        # raw override + customBalance: customBalance wins
        unit["balanceOverrides"] = [{"name": "cost", "value": "100"}]
        unit["customBalance"] = {"Cost": 55, "MaxHealth": 200}
        rows = EXPORT.export_balance_table(pack)
        self.assertEqual(55, rows[0]["Cost"])
        self.assertEqual(200, rows[0]["MaxHealth"])

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

    def test_to_csv_roundtrip(self) -> None:
        pack = _valid_pack()
        rows = EXPORT.export_balance_table(pack)
        text = EXPORT.rows_to_csv(rows)
        reader = list(csv.DictReader(io.StringIO(text)))
        self.assertEqual(1, len(reader))
        self.assertEqual(pack["units"][0]["id"], reader[0]["unitId"])


if __name__ == "__main__":
    unittest.main()
