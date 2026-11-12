"""Tests de las estadísticas del pack."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

STATS = load_tool_module("content_pack_stats")
SCAFFOLD = load_tool_module("scaffold_pack")


class ContentPackStatsTests(unittest.TestCase):
    def test_counts_and_templates(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")
        stats = STATS.pack_stats(pack)
        self.assertEqual("ns:pack", stats["packId"])
        self.assertEqual(1, stats["counts"]["units"])
        self.assertEqual(1, stats["counts"]["factories"])
        self.assertEqual(1, stats["counts"]["stages"])
        self.assertEqual(1, stats["counts"]["aiProfiles"])
        self.assertEqual({"PCXBaseTank": 1}, stats["unitTemplates"])
        self.assertEqual({"StageParameters_Pools_Level1": 1}, stats["stageTemplates"])

    def test_localization_stats(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        stats = STATS.pack_stats(pack)
        units_loc = stats["localization"]["units"]
        self.assertEqual(1, units_loc["entities"])
        self.assertEqual(1, units_loc["english"])
        self.assertEqual(1, units_loc["bilingual"])
        self.assertEqual(100.0, units_loc["bilingualPercent"])

    def test_ai_modules_counted(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        stats = STATS.pack_stats(pack)
        self.assertEqual({"sample:scheduled-base-build": 1}, stats["aiModules"])


if __name__ == "__main__":
    unittest.main()
