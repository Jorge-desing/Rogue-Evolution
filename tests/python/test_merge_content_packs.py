"""Tests del fusionador de content packs."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module

MERGE = load_tool_module("merge_content_packs")
SCAFFOLD = load_tool_module("scaffold_pack")


class MergeContentPacksTests(unittest.TestCase):
    def _make_pack(self, pack_id: str, unit_slug: str, stage_slug: str = "training-stage", ai_slug: str = "base-defense") -> dict:
        return SCAFFOLD.generate_scaffold(pack_id, unit_slug, stage_slug=stage_slug, ai_slug=ai_slug)

    def test_merge_adds_units_stages_and_profiles(self) -> None:
        left = self._make_pack("left:expansion", "alpha")
        right = self._make_pack("right:expansion", "beta")
        # Make packIds compatible by forcing
        right["packId"] = "left:expansion"

        merged, conflicts = MERGE.merge_packs(left, right)

        self.assertEqual([], conflicts)
        self.assertEqual("left:expansion", merged["packId"])
        unit_ids = [u["id"] for u in merged["units"]]
        self.assertIn("left:alpha", unit_ids)
        self.assertIn("right:beta", unit_ids)
        self.assertEqual(2, len(merged["units"]))
        self.assertEqual(2, len(merged["stages"]))
        self.assertEqual(2, len(merged["aiProfiles"]))

    def test_merge_detects_conflicting_unit_content(self) -> None:
        left = self._make_pack("ns:pack", "alpha")
        right = self._make_pack("ns:pack", "alpha")
        # mutate right's unit
        right["units"][0]["balanceOverrides"] = [{"name": "rarity", "value": "Rare"}]

        merged, conflicts = MERGE.merge_packs(left, right)

        self.assertTrue(conflicts)
        self.assertTrue(any("conflicto" in c for c in conflicts))

    def test_merge_dedups_bundles_and_detects_pack_id_mismatch(self) -> None:
        left = self._make_pack("a:pack", "u1")
        right = self._make_pack("b:pack", "u2")

        merged, conflicts = MERGE.merge_packs(left, right)
        self.assertTrue(conflicts)
        self.assertTrue(any("packId distinto" in c for c in conflicts))

        # forced pack id resolves mismatch
        merged2, conflicts2 = MERGE.merge_packs(left, right, pack_id="merged:pack")
        self.assertEqual([], conflicts2)
        self.assertEqual("merged:pack", merged2["packId"])

    def test_merge_writes_and_validates(self) -> None:
        left = self._make_pack("ns:pack", "alpha", stage_slug="stage-a", ai_slug="ai-a")
        right = self._make_pack("ns:pack", "beta", stage_slug="stage-b", ai_slug="ai-b")

        merged, conflicts = MERGE.merge_packs(left, right)
        self.assertEqual([], conflicts)
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "merged"
            written = MERGE.write_merged(merged, out)
            self.assertTrue(written.is_file())
            data = json.loads(written.read_text(encoding="utf-8"))
            self.assertEqual(2, len(data["units"]))


if __name__ == "__main__":
    unittest.main()
