"""Tests del analizador de pools de planos."""
from __future__ import annotations
import unittest
from tests.python.helpers import load_tool_module

POOL = load_tool_module("analyze_blueprint_pools")
SCAFFOLD = load_tool_module("scaffold_pack")

def _valid_pack() -> dict:
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class AnalyzeBlueprintPoolsTests(unittest.TestCase):
    def test_valid_pack_analysis(self) -> None:
        pack = _valid_pack()
        report = POOL.analyze_stage_pools(pack)
        self.assertEqual(pack["packId"], report["packId"])
        self.assertGreaterEqual(report["stageCount"], 1)
        # scaffold stage lists the unit in unitIds; pool entries cover it
        self.assertTrue(len(report["stages"]) >= 1)

    def test_uncovered_unit_detected(self) -> None:
        pack = _valid_pack()
        # Add a unit to stage.unitIds that no pool entry covers
        ghost = "ns:orphan-unit"
        pack["stages"][0]["unitIds"].append(ghost)
        report = POOL.analyze_stage_pools(pack, known_unit_ids={ghost})
        stage = report["stages"][0]
        self.assertIn(ghost, stage.get("uncoveredUnitIds", []))

    def test_pool_entry_with_unknown_unit(self) -> None:
        pack = _valid_pack()
        pack["stages"][0]["blueprintPoolEntries"] = [
            {"poolId": "BP_CheaperVehicles", "unitIds": ["ns:ghost"]}
        ]
        known = {pack["units"][0]["id"]}
        report = POOL.analyze_stage_pools(pack, known_unit_ids=known)
        stage = report["stages"][0]
        self.assertTrue(any("ghost" in i for i in stage.get("issues", [])))

    def test_empty_pool_id_rejected(self) -> None:
        pack = _valid_pack()
        pack["stages"][0]["blueprintPoolEntries"] = [
            {"poolId": "", "unitIds": []}
        ]
        report = POOL.analyze_stage_pools(pack)
        stage = report["stages"][0]
        self.assertTrue(len(stage.get("issues", [])) >= 1)

    def test_empty_pack(self) -> None:
        report = POOL.analyze_stage_pools({})
        self.assertEqual(0, report["stageCount"])
        self.assertEqual([], report["stages"])


if __name__ == "__main__":
    unittest.main()
