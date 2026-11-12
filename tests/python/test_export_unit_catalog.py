"""Tests del exportador de catálogo de unidades."""
from __future__ import annotations
import unittest
from tests.python.helpers import load_tool_module

EC = load_tool_module("export_unit_catalog")
SCAFFOLD = load_tool_module("scaffold_pack")


def _valid_pack():
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class ExportUnitCatalogTests(unittest.TestCase):
    def test_empty_pack(self):
        report = EC.export_unit_catalog({})
        self.assertEqual(0, report["unitCount"])
        self.assertEqual([], report["units"])

    def test_counts_units(self):
        pack = _valid_pack()
        report = EC.export_unit_catalog(pack)
        self.assertEqual(len(pack["units"]), report["unitCount"])
        self.assertTrue(all("id" in u for u in report["units"]))

    def test_stage_mapping(self):
        pack = _valid_pack()
        if not pack.get("units"):
            self.skipTest("no units")
        uid = pack["units"][0]["id"]
        # Fuerza una etapa que ofrezca la unidad.
        stage = {"id": "ns:stage-x", "unitIds": [uid]}
        pack.setdefault("stages", []).append(stage)
        report = EC.export_unit_catalog(pack)
        entry = next(u for u in report["units"] if u["id"] == uid)
        self.assertIn("ns:stage-x", entry["stages"])

    def test_json_serializable(self):
        import json
        pack = _valid_pack()
        report = EC.export_unit_catalog(pack)
        json.dumps(report)


if __name__ == "__main__":
    unittest.main()
