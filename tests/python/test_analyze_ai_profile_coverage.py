"""Tests del analizador de cobertura de perfiles AI."""
from __future__ import annotations
import unittest
from tests.python.helpers import load_tool_module

AIC = load_tool_module("analyze_ai_profile_coverage")
SCAFFOLD = load_tool_module("scaffold_pack")


def _valid_pack():
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class AnalyzeAiProfileCoverageTests(unittest.TestCase):
    def test_empty_pack_ok(self):
        report = AIC.analyze_ai_profile_coverage({})
        self.assertEqual(0, report["profileCount"])
        self.assertEqual([], report["issues"])

    def test_unknown_module_flagged(self):
        pack = _valid_pack()
        if not pack.get("aiProfiles"):
            self.skipTest("no ai profiles in scaffold")
        pack["aiProfiles"][0]["modules"] = [{"moduleId": "sample:nope", "parameters": []}]
        report = AIC.analyze_ai_profile_coverage(pack)
        self.assertTrue(any("moduleId no registrado" in i for i in report["issues"]))

    def test_duplicate_module_flagged(self):
        pack = _valid_pack()
        if not pack.get("aiProfiles"):
            self.skipTest("no ai profiles in scaffold")
        pack["aiProfiles"][0]["modules"] = [
            {"moduleId": "sample:trace-default", "parameters": []},
            {"moduleId": "sample:trace-default", "parameters": []},
        ]
        report = AIC.analyze_ai_profile_coverage(pack)
        self.assertTrue(any("módulo duplicado" in i for i in report["issues"]))

    def test_unit_entry_unresolved_flagged(self):
        pack = _valid_pack()
        if not pack.get("aiProfiles"):
            self.skipTest("no ai profiles in scaffold")
        pack["aiProfiles"][0]["unitEntries"] = [{"unitId": "ns:nope"}]
        report = AIC.analyze_ai_profile_coverage(pack)
        self.assertTrue(any("unitEntry unitId no resuelve" in i for i in report["issues"]))

    def test_cross_pack_product_unit_id_resolves(self):
        pack = _valid_pack()
        profile = pack["aiProfiles"][0]
        profile["unitEntries"] = [{"unitId": "external:scout", "baseIndex": 0}]
        profile["productionBuildingEntries"] = [{"unitId": "external:scout", "baseIndex": 0}]
        report = AIC.analyze_ai_profile_coverage(
            pack,
            known_unit_ids={pack["units"][0]["id"], "external:scout"},
        )
        self.assertEqual([], report["issues"])

    def test_tactical_director_modules_are_registered(self):
        pack = _valid_pack()
        if not pack.get("aiProfiles"):
            self.skipTest("no ai profiles in scaffold")
        pack["aiProfiles"][0]["modules"] = [
            {"moduleId": "sample:tactical-director", "parameters": []},
            {"moduleId": "sample:squad-operations-director", "parameters": []},
        ]
        report = AIC.analyze_ai_profile_coverage(pack)
        self.assertEqual([], report["issues"])

    def test_registered_module_count(self):
        report = AIC.analyze_ai_profile_coverage({})
        self.assertEqual(23, report["registeredModuleCount"])


if __name__ == "__main__":
    unittest.main()
