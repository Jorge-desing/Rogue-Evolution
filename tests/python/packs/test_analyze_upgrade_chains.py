"""Tests del analizador de cadenas de mejora."""
from __future__ import annotations
import unittest
from tests.python.helpers import load_tool_module

UC = load_tool_module("analyze_upgrade_chains")
SCAFFOLD = load_tool_module("scaffold_pack")


def _valid_pack():
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class AnalyzeUpgradeChainsTests(unittest.TestCase):
    def test_empty_pack_ok(self):
        report = UC.analyze_upgrade_chains({})
        self.assertEqual(0, report["upgradeCount"])
        self.assertEqual([], report["issues"])

    def test_orphan_upgrade_flagged(self):
        pack = _valid_pack()
        pack["upgrades"] = [{
            "id": "ns:up-a", "targetUnitId": "ns:does-not-exist",
            "requiredExperienceLevel": 1, "cost": 10,
            "localizations": [{"language": "en-US", "name": "U", "description": "d"}],
            "cardChanges": [],
        }]
        report = UC.analyze_upgrade_chains(pack)
        self.assertIn("ns:up-a", report["orphanUpgrades"])
        self.assertTrue(any("targetUnitId no resuelve" in i for i in report["issues"]))

    def test_level_out_of_range_flagged(self):
        pack = _valid_pack()
        uid = pack["units"][0]["id"]
        pack["upgrades"] = [{
            "id": "ns:up-b", "targetUnitId": uid,
            "requiredExperienceLevel": 200, "cost": 10,
            "localizations": [{"language": "en-US", "name": "U", "description": "d"}],
            "cardChanges": [],
        }]
        report = UC.analyze_upgrade_chains(pack)
        self.assertTrue(any("requiredExperienceLevel fuera de rango" in i for i in report["issues"]))

    def test_negative_cost_flagged(self):
        pack = _valid_pack()
        uid = pack["units"][0]["id"]
        pack["upgrades"] = [{
            "id": "ns:up-c", "targetUnitId": uid,
            "requiredExperienceLevel": 1, "cost": -5,
            "localizations": [{"language": "en-US", "name": "U", "description": "d"}],
            "cardChanges": [],
        }]
        report = UC.analyze_upgrade_chains(pack)
        self.assertTrue(any("cost inválido" in i for i in report["issues"]))

    def test_duplicate_upgrade_flagged(self):
        pack = _valid_pack()
        uid = pack["units"][0]["id"]
        entry = {
            "id": "ns:up-d", "targetUnitId": uid,
            "requiredExperienceLevel": 1, "cost": 10,
            "localizations": [{"language": "en-US", "name": "U", "description": "d"}],
            "cardChanges": [],
        }
        pack["upgrades"] = [dict(entry), dict(entry)]
        report = UC.analyze_upgrade_chains(pack)
        self.assertTrue(any("duplicado" in i for i in report["issues"]))


if __name__ == "__main__":
    unittest.main()
