"""Tests del comparador de content packs."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module

DIFF = load_tool_module("diff_content_packs")
SCAFFOLD = load_tool_module("scaffold_pack")


class DiffContentPacksTests(unittest.TestCase):
    def test_diff_detects_added_removed_changed(self) -> None:
        left = SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")
        right = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")

        result = DIFF.diff_packs(left, right)

        self.assertEqual(1, result["units"]["removedCount"])  # alpha removed
        self.assertEqual(1, result["units"]["addedCount"])  # beta added
        self.assertEqual(0, result["units"]["changedCount"])
        self.assertEqual(1, result["stages"]["removedCount"])
        self.assertEqual(1, result["stages"]["addedCount"])
        self.assertGreater(result["totalChangeCount"], 0)

    def test_diff_detects_changed_unit_content(self) -> None:
        left = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        right = json.loads(json.dumps(left))
        right["units"][0]["balanceOverrides"] = [{"name": "rarity", "value": "Rare"}]

        result = DIFF.diff_packs(left, right)

        self.assertEqual(1, result["units"]["changedCount"])
        self.assertIn("ns:alpha", result["units"]["changed"])
        self.assertEqual(0, result["units"]["addedCount"])
        self.assertEqual(0, result["units"]["removedCount"])

    def test_diff_metadata_changes(self) -> None:
        left = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        right = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        right["packId"] = "ns:other"

        result = DIFF.diff_packs(left, right)

        self.assertIn("packId", result["metadataChanges"])
        self.assertEqual(1, result["metadataChangeCount"])

    def test_diff_empty_when_identical(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        result = DIFF.diff_packs(pack, pack)
        self.assertEqual(0, result["totalChangeCount"])


if __name__ == "__main__":
    unittest.main()
