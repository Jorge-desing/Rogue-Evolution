"""Tests del informe de economía y etapas."""

from __future__ import annotations

import json
import unittest

from tests.python.helpers import SAMPLE_NATIVE_PATH, load_tool_module

ECONOMY = load_tool_module("economy_report")
SCAFFOLD = load_tool_module("scaffold_pack")


def _unit_with_stats(pack: dict) -> dict:
    pack["units"][0]["balanceOverrides"] = [
        {"name": "cost", "value": "100"},
        {"name": "productionDuration", "value": "50"},
        {"name": "maxHealth", "value": "200"},
        {"name": "maxShield", "value": "50"},
        {"name": "damage1", "value": "10"},
        {"name": "attackCooldown", "value": "2.0"},
        {"name": "damage2", "value": "0"},
        {"name": "attack2Cooldown", "value": "0"},
    ]
    return pack


class EconomyReportTests(unittest.TestCase):
    def test_counts_before_details(self) -> None:
        pack = _unit_with_stats(SCAFFOLD.generate_scaffold("ns:pack", "alpha"))
        report = ECONOMY.economy_report(pack)
        self.assertEqual(1, report["counts"]["units"])
        unit = report["units"][0]
        self.assertAlmostEqual(250.0, unit["ehp"])
        self.assertAlmostEqual(5.0, unit["dps1"])
        self.assertAlmostEqual(5.0, unit["dpsTotal"])
        self.assertAlmostEqual(0.05, unit["dpsPerCost"])
        self.assertEqual(100.0, report["totals"]["avgCost"])

    def test_zero_cooldown_yields_zero_dps(self) -> None:
        pack = _unit_with_stats(SCAFFOLD.generate_scaffold("ns:pack", "alpha"))
        pack["units"][0]["balanceOverrides"][5] = {"name": "attackCooldown", "value": "0"}
        report = ECONOMY.economy_report(pack)
        self.assertEqual(0.0, report["units"][0]["dps1"])

    def test_stage_economy(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        report = ECONOMY.stage_economy(pack)
        self.assertEqual(1, report["counts"]["stages"])
        stage = report["stages"][0]
        self.assertEqual(pack["stages"][0]["id"], stage["stageId"])
        self.assertEqual(1, stage["unitCount"])

    def test_empty_pack(self) -> None:
        report = ECONOMY.economy_report({})
        self.assertEqual(0, report["counts"]["units"])
        self.assertEqual([], report["units"])
        self.assertIsNone(report["totals"]["avgCost"])
        stage_report = ECONOMY.stage_economy({})
        self.assertEqual(0, stage_report["counts"]["stages"])

    def test_real_pack(self) -> None:
        pack = json.loads(SAMPLE_NATIVE_PATH.read_text(encoding="utf-8"))
        report = ECONOMY.economy_report(pack)
        self.assertEqual(9, report["counts"]["units"])
        scout = next(u for u in report["units"] if "scout" in u["unitId"])
        self.assertEqual(70.0, scout["cost"])
        stage_report = ECONOMY.stage_economy(pack)
        self.assertEqual(3, stage_report["counts"]["stages"])
        training = next(
            s for s in stage_report["stages"]
            if s["stageId"] == "sample-native:training-variant"
        )
        self.assertEqual(160.0, training["coinReward"])


if __name__ == "__main__":
    unittest.main()
