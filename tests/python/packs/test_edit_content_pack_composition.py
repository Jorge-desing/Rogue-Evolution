"""Tests de las operaciones de composición y auto-sanación de edit_content_pack."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

EDIT = load_tool_module("edit_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


class CompositionEditTests(unittest.TestCase):
    def _pack(self) -> dict:
        return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")

    def test_extract_units_to_pack(self) -> None:
        pack = self._pack()
        extracted, diagnostics = EDIT.extract_units_to_pack(pack, ["ns:alpha"])
        self.assertEqual([], diagnostics)
        self.assertEqual(["ns:alpha"], [u["id"] for u in extracted["units"]])
        self.assertEqual("ns:pack", extracted["packId"])
        # stage/AI kept only if they reference extracted units
        self.assertTrue(extracted["stages"])

    def test_extract_unknown_unit_fails(self) -> None:
        pack = self._pack()
        _, diagnostics = EDIT.extract_units_to_pack(pack, ["ns:ghost"])
        self.assertTrue(diagnostics)

    def test_import_units_from_pack(self) -> None:
        pack = self._pack()
        other = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
        updated, diagnostics = EDIT.import_units(pack, other, ["ns:beta"])
        self.assertEqual([], diagnostics)
        self.assertIn("ns:beta", [u["id"] for u in updated["units"]])

        _, d2 = EDIT.import_units(pack, other, ["ns:alpha"])
        self.assertTrue(d2)  # already exists

    def test_purge_dangling_references(self) -> None:
        pack = self._pack()
        pack["stages"][0]["unitIds"].append("ns:ghost")
        pack["stages"][0]["blueprintPoolEntries"] = [
            {"poolId": "P1", "unitIds": ["ns:ghost", "ns:alpha"]}
        ]
        pack["stages"][0]["aiProfileIds"].append("ns:ghost-profile")

        updated, cleared = EDIT.purge_dangling_references(pack)
        # ghost purged
        self.assertNotIn("ns:ghost", updated["stages"][0]["unitIds"])
        self.assertNotIn("ns:ghost", updated["stages"][0]["blueprintPoolEntries"][0]["unitIds"])
        self.assertNotIn("ns:ghost-profile", updated["stages"][0]["aiProfileIds"])
        self.assertIn("ns:ghost", cleared)

    def test_rename_stage_and_profile(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.rename_stage(pack, "ns:sa", "ns:frontline")
        self.assertEqual([], diagnostics)
        self.assertIn("ns:frontline", [s["id"] for s in updated["stages"]])

        updated2, d2 = EDIT.rename_ai_profile(updated, "ns:aa", "ns:guard")
        self.assertEqual([], d2)
        self.assertIn("ns:guard", [p["id"] for p in updated2["aiProfiles"]])
        self.assertIn("ns:guard", updated2["stages"][0]["aiProfileIds"])

    def test_bulk_set_balance(self) -> None:
        pack = self._pack()
        other = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
        pack["units"].append(other["units"][0])

        updated, diagnostics = EDIT.bulk_set_balance(pack, "rarity", "Epic")
        self.assertEqual([], diagnostics)
        for unit in updated["units"]:
            values = {b["name"]: b["value"] for b in unit["balanceOverrides"]}
            self.assertEqual("Epic", values["rarity"])

    def test_clone_stage_and_profile(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.clone_stage(pack, "ns:sa", "ns:clone")
        self.assertEqual([], diagnostics)
        self.assertEqual(2, len(updated["stages"]))

        updated2, d2 = EDIT.clone_ai_profile(updated, "ns:aa", "ns:clone-ai")
        self.assertEqual([], d2)
        self.assertEqual(2, len(updated2["aiProfiles"]))


if __name__ == "__main__":
    unittest.main()
