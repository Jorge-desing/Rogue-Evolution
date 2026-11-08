"""Tests del validador de filtros de pools de blueprints."""
from __future__ import annotations
import unittest
from tests.python.helpers import load_tool_module

CBF = load_tool_module("check_stage_blueprint_filters")
SCAFFOLD = load_tool_module("scaffold_pack")


def _valid_pack():
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class CheckStageBlueprintFiltersTests(unittest.TestCase):
    def test_empty_pack_ok(self):
        report = CBF.check_stage_blueprint_filters({})
        self.assertEqual(0, report["poolCount"])
        self.assertEqual([], report["issues"])

    def test_empty_pool_unitids_flagged(self):
        pack = _valid_pack()
        pack["stages"][0]["blueprintPoolEntries"] = [{"poolId": "p1", "unitIds": []}]
        report = CBF.check_stage_blueprint_filters(pack)
        self.assertTrue(any("unitIds vacío" in i for i in report["issues"]))

    def test_duplicate_unitid_flagged(self):
        pack = _valid_pack()
        if not pack["stages"][0].get("blueprintPoolEntries"):
            pack["stages"][0]["blueprintPoolEntries"] = [{"poolId": "p1", "unitIds": ["ns:x", "ns:x"]}]
        else:
            pack["stages"][0]["blueprintPoolEntries"][0]["unitIds"] = ["ns:x", "ns:x"]
        report = CBF.check_stage_blueprint_filters(pack)
        self.assertTrue(any("duplicado" in i for i in report["issues"]))

    def test_unresolved_unit_flagged(self):
        pack = _valid_pack()
        if not pack["stages"][0].get("blueprintPoolEntries"):
            pack["stages"][0]["blueprintPoolEntries"] = [{"poolId": "p1", "unitIds": ["ns:nope"]}]
        else:
            pack["stages"][0]["blueprintPoolEntries"][0]["unitIds"] = ["ns:nope"]
        report = CBF.check_stage_blueprint_filters(pack)
        self.assertTrue(any("no resuelve" in i for i in report["issues"]))

    def test_empty_poolid_flagged(self):
        pack = _valid_pack()
        if not pack["stages"][0].get("blueprintPoolEntries"):
            pack["stages"][0]["blueprintPoolEntries"] = [{"poolId": "", "unitIds": ["ns:x"]}]
        else:
            pack["stages"][0]["blueprintPoolEntries"][0]["poolId"] = ""
        report = CBF.check_stage_blueprint_filters(pack)
        self.assertTrue(any("poolId vacío" in i for i in report["issues"]))


if __name__ == "__main__":
    unittest.main()
