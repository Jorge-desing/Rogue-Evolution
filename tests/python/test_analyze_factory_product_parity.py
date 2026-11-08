"""Tests del analizador de paridad fábrica/producto."""
from __future__ import annotations
import unittest
from tests.python.helpers import load_tool_module

PARITY = load_tool_module("analyze_factory_product_parity")
SCAFFOLD = load_tool_module("scaffold_pack")


def _valid_pack():
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class AnalyzeFactoryProductParityTests(unittest.TestCase):
    def test_empty_pack_ok(self):
        report = PARITY.analyze_factory_product_parity({})
        self.assertEqual(0, report["unitCount"])
        self.assertEqual([], report["issues"])

    def test_orphan_factory_flagged(self):
        pack = _valid_pack()
        # Añade un building sin producto.
        pack.setdefault("buildings", [])
        pack["buildings"].append({"id": "ns:orphan-factory"})
        report = PARITY.analyze_factory_product_parity(pack)
        self.assertIn("ns:orphan-factory", report["orphanFactories"])
        self.assertTrue(any("huérfana" in i for i in report["issues"]))

    def test_rarity_mismatch_flagged(self):
        pack = _valid_pack()
        # Fuerza una fábrica y producto con rarity distinta.
        if pack["units"]:
            u = pack["units"][0]
            u["productionBuilding"] = "ns:factory-x"
            u["rarity"] = "Common"
        pack.setdefault("units", []).append({
            "id": "ns:factory-x", "productionBuilding": None,
            "rarity": "Rare", "neededExperienceLevel": 0,
        })
        report = PARITY.analyze_factory_product_parity(pack)
        self.assertTrue(any("rarity" in i for i in report["issues"]))

    def test_duplicate_factory_mapping_flagged(self):
        pack = _valid_pack()
        if len(pack["units"]) < 2:
            pack["units"].append(dict(pack["units"][0]))
            pack["units"][-1]["id"] = "ns:unit-2"
        for u in pack["units"][:2]:
            u["productionBuilding"] = "ns:shared-factory"
        report = PARITY.analyze_factory_product_parity(pack)
        self.assertTrue(any("2 productos" in i for i in report["issues"]))


if __name__ == "__main__":
    unittest.main()
