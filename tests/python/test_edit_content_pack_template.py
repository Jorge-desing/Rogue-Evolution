"""Tests de set_unit_template en edit_content_pack."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

EDIT = load_tool_module("edit_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


class SetUnitTemplateTests(unittest.TestCase):
    def _pack(self) -> dict:
        return SCAFFOLD.generate_scaffold("ns:pack", "alpha")

    def test_set_known_template(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.set_unit_template(pack, "ns:alpha", "ArtilleryTank")
        self.assertEqual([], diagnostics)
        self.assertEqual("ArtilleryTank", updated["units"][0]["nativeTemplateId"])

    def test_set_unknown_template_fails(self) -> None:
        pack = self._pack()
        _, diagnostics = EDIT.set_unit_template(pack, "ns:alpha", "FlyingTank")
        self.assertTrue(diagnostics)

    def test_set_factory_template(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.set_unit_template(
            pack, "ns:alpha", "ArtilleryTankFactory", factory=True
        )
        self.assertEqual([], diagnostics)
        self.assertEqual(
            "ArtilleryTankFactory",
            updated["units"][0]["productionBuilding"]["nativeTemplateId"],
        )

    def test_batch_set_template(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.apply_edits(pack, [
            {"op": "set-template", "unitId": "ns:alpha", "templateId": "GatlingWalker"}
        ])
        self.assertEqual([], diagnostics)
        self.assertEqual("GatlingWalker", updated["units"][0]["nativeTemplateId"])


if __name__ == "__main__":
    unittest.main()
