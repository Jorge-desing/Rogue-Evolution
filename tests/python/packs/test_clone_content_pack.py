"""Tests del clonador de content packs."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

CLONE = load_tool_module("clone_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


class CloneContentPackTests(unittest.TestCase):
    def test_clone_remaps_units_stages_and_profiles(self) -> None:
        pack = SCAFFOLD.generate_scaffold(
            "old:pack", "alpha", stage_slug="sa", ai_slug="aa"
        )
        cloned, diagnostics = CLONE.clone_pack(pack, "new:pack")

        self.assertEqual([], diagnostics)
        self.assertEqual("new:pack", cloned["packId"])
        unit_ids = [u["id"] for u in cloned["units"]]
        self.assertEqual(["new:alpha"], unit_ids)
        self.assertEqual("new:alpha-factory", cloned["units"][0]["productionBuilding"]["id"])
        self.assertEqual(["new:alpha"], cloned["stages"][0]["unitIds"])
        self.assertEqual(["new:aa"], cloned["stages"][0]["aiProfileIds"])

    def test_clone_returns_diagnostics_for_invalid_pack_id(self) -> None:
        pack = SCAFFOLD.generate_scaffold("old:pack", "alpha")
        cloned, diagnostics = CLONE.clone_pack(pack, "pack sin namespace")
        self.assertEqual({}, cloned)
        self.assertTrue(diagnostics)

    def test_clone_preserves_native_templates(self) -> None:
        pack = SCAFFOLD.generate_scaffold("old:pack", "alpha")
        cloned, _ = CLONE.clone_pack(pack, "new:pack")
        self.assertEqual("PCXBaseTank", cloned["units"][0]["nativeTemplateId"])
        # template names are not namespaced, they must stay untouched
        self.assertEqual("PCXBaseTankFactory", cloned["units"][0]["productionBuilding"]["nativeTemplateId"])


if __name__ == "__main__":
    unittest.main()
