"""Tests de las operaciones avanzadas de edición de content packs."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

EDIT = load_tool_module("edit_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


class AdvancedEditTests(unittest.TestCase):
    def _pack(self) -> dict:
        return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")

    def test_set_unit_flag(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.set_unit_flag(
            pack, "ns:alpha", "isAllowedAsStartingBlueprint", True
        )
        self.assertEqual([], diagnostics)
        unit = [u for u in updated["units"] if u["id"] == "ns:alpha"][0]
        self.assertTrue(unit["isAllowedAsStartingBlueprint"])

    def test_set_unit_flag_rejects_unknown_flag(self) -> None:
        pack = self._pack()
        _, diagnostics = EDIT.set_unit_flag(pack, "ns:alpha", "isGodMode", True)
        self.assertTrue(diagnostics)

    def test_set_balance_override_upserts(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.set_balance_override(
            pack, "ns:alpha", "rarity", "Rare"
        )
        self.assertEqual([], diagnostics)
        unit = [u for u in updated["units"] if u["id"] == "ns:alpha"][0]
        values = {b["name"]: b["value"] for b in unit["balanceOverrides"]}
        self.assertEqual("Rare", values["rarity"])

        # factory balance
        updated2, d2 = EDIT.set_balance_override(
            updated, "ns:alpha", "maxCapacity", "5", factory=True
        )
        self.assertEqual([], d2)
        factory = updated2["units"][0]["productionBuilding"]
        fvalues = {b["name"]: b["value"] for b in factory["balanceOverrides"]}
        self.assertEqual("5", fvalues["maxCapacity"])

    def test_remove_balance_override(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.remove_balance_override(pack, "ns:alpha", "rarity")
        self.assertEqual([], diagnostics)
        unit = [u for u in updated["units"] if u["id"] == "ns:alpha"][0]
        names = [b["name"] for b in unit["balanceOverrides"]]
        self.assertNotIn("rarity", names)

        _, d2 = EDIT.remove_balance_override(pack, "ns:ghost", "rarity")
        self.assertTrue(d2)

    def test_set_stage_map_size_validated(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.set_stage_map_size(pack, "ns:sa", 30, 90)
        self.assertEqual([], diagnostics)
        stage = updated["stages"][0]
        self.assertEqual(30, stage["mapSizeMin"])
        self.assertEqual(90, stage["mapSizeMax"])

        _, d2 = EDIT.set_stage_map_size(pack, "ns:sa", 90, 30)
        self.assertTrue(d2)  # min > max rejected

    def test_add_pool_entry_creates_or_updates(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.add_pool_entry(
            pack, "ns:sa", "P1", ["ns:alpha"]
        )
        self.assertEqual([], diagnostics)
        pools = updated["stages"][0]["blueprintPoolEntries"]
        self.assertEqual(1, len(pools))
        self.assertEqual(["ns:alpha"], pools[0]["unitIds"])

        # update existing pool
        updated2, d2 = EDIT.add_pool_entry(updated, "ns:sa", "P1", ["ns:alpha"])
        self.assertEqual([], d2)
        pools2 = updated2["stages"][0]["blueprintPoolEntries"]
        self.assertEqual(["ns:alpha"], pools2[0]["unitIds"])

    def test_remove_pool_entry(self) -> None:
        pack = self._pack()
        pack["stages"][0]["blueprintPoolEntries"] = [
            {"poolId": "P1", "unitIds": ["ns:alpha"]}
        ]
        updated, diagnostics = EDIT.remove_pool_entry(pack, "ns:sa", "P1")
        self.assertEqual([], diagnostics)
        self.assertEqual([], updated["stages"][0]["blueprintPoolEntries"])

        _, d2 = EDIT.remove_pool_entry(pack, "ns:sa", "P9")
        self.assertTrue(d2)

    def test_set_ai_module_param_upserts(self) -> None:
        pack = self._pack()
        profile = pack["aiProfiles"][0]
        module_id = profile["modules"][0]["moduleId"]

        updated, diagnostics = EDIT.set_ai_module_param(
            pack, "ns:aa", module_id, "intervalSeconds", "6"
        )
        self.assertEqual([], diagnostics)
        params = {p["name"]: p["value"] for p in updated["aiProfiles"][0]["modules"][0]["parameters"]}
        self.assertEqual("6", params["intervalSeconds"])

        _, d2 = EDIT.set_ai_module_param(pack, "ns:aa", "sample:ghost", "x", "1")
        self.assertTrue(d2)

    def test_remove_unit_purges_module_params(self) -> None:
        pack = self._pack()
        # scaffold AI module references the factory via entityId param
        factory_id = pack["units"][0]["productionBuilding"]["id"]
        updated, _ = EDIT.remove_unit(pack, "ns:alpha")
        profile = updated["aiProfiles"][0]
        params_str = str(profile.get("modules", {}))
        self.assertNotIn(factory_id, params_str)


if __name__ == "__main__":
    unittest.main()
