"""Tests de edición de unidades en content packs."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

EDIT = load_tool_module("edit_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


class EditContentPackTests(unittest.TestCase):
    def _pack(self) -> dict:
        return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")

    def test_add_unit_registers_in_stage(self) -> None:
        pack = self._pack()
        other = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
        new_unit = other["units"][0]

        updated, diagnostics = EDIT.add_unit(pack, new_unit, stage_ids=["ns:sa"])

        self.assertEqual([], diagnostics)
        unit_ids = [u["id"] for u in updated["units"]]
        self.assertEqual(2, len(unit_ids))
        self.assertIn("ns:beta", unit_ids)
        self.assertIn("ns:beta", updated["stages"][0]["unitIds"])

    def test_add_duplicate_unit_fails(self) -> None:
        pack = self._pack()
        dup = SCAFFOLD.generate_scaffold("ns:pack", "alpha")["units"][0]
        updated, diagnostics = EDIT.add_unit(pack, dup)
        self.assertTrue(diagnostics)
        self.assertEqual(1, len(updated["units"]))

    def test_remove_unit_purges_references(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.remove_unit(pack, "ns:alpha")

        self.assertTrue(diagnostics)
        self.assertEqual([], updated["units"])
        self.assertNotIn("ns:alpha", updated["stages"][0].get("unitIds", []))
        # AI entries that referenced the product are purged too
        for profile in updated.get("aiProfiles", []):
            for entry in profile.get("unitEntries", []):
                self.assertNotIn("ns:alpha", entry.get("unitIds", entry.get("unitId", "")))

    def test_remove_unknown_unit_fails(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.remove_unit(pack, "ns:ghost")
        self.assertTrue(any("no existe" in d for d in diagnostics))
        self.assertEqual(1, len(updated["units"]))


if __name__ == "__main__":
    unittest.main()
