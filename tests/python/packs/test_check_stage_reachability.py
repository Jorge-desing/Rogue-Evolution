"""Tests del analizador de alcance de etapas."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

REACH = load_tool_module("check_stage_reachability")
SCAFFOLD = load_tool_module("scaffold_pack")


class ReachabilityTests(unittest.TestCase):
    def test_offered_units_per_stage(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        report = REACH.analyze_reachability(pack)
        self.assertEqual(1, report["stageCount"])
        self.assertEqual(["ns:alpha"], report["stages"][0]["offeredUnitIds"])
        self.assertEqual([], report["unreachableUnitIds"])

    def test_unreachable_units_reported(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        other = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
        pack["units"].append(other["units"][0])
        report = REACH.analyze_reachability(pack)
        self.assertEqual(["ns:beta"], report["unreachableUnitIds"])
        self.assertEqual(1, report["unreachableCount"])

    def test_pool_offered_units_count(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["stages"][0]["blueprintPoolEntries"] = [
            {"poolId": "P1", "unitIds": ["ns:alpha"]}
        ]
        report = REACH.analyze_reachability(pack)
        self.assertEqual(["ns:alpha"], report["stages"][0]["offeredUnitIds"])

    def test_orphan_offered_ids(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["stages"][0]["unitIds"].append("ns:ghost")
        report = REACH.analyze_reachability(pack)
        stage = report["stages"][0]
        self.assertEqual(["ns:ghost"], stage["orphanOfferedIds"])


if __name__ == "__main__":
    unittest.main()
