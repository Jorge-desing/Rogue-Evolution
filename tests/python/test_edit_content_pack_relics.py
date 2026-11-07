"""Tests de relics, bundles y duplicación en edit_content_pack."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

EDIT = load_tool_module("edit_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


def _relic(relic_id: str = "ns:relic-a", ally: str = "Health") -> dict:
    return {
        "id": relic_id,
        "rarity": "Rare",
        "tech": "Mech",
        "offeredSystemTags": "Mech",
        "neededSystemTags": "Mech",
        "neededExperienceLevel": 0,
        "isAllowedForDemo": False,
        "isForSpecialists": False,
        "localizations": [
            {"language": "en-US", "name": "R", "description": "D"},
            {"language": "es-XL", "name": "R", "description": "D"},
        ],
        "cardChanges": [
            {"valueToChange": ally, "operation": "Multiply", "value": 1.1}
        ],
    }


class RelicEditTests(unittest.TestCase):
    def _pack(self) -> dict:
        return SCAFFOLD.generate_scaffold("ns:pack", "alpha")

    def test_add_and_remove_relic(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.add_relic(pack, _relic())
        self.assertEqual([], diagnostics)
        self.assertEqual(1, len(updated["relics"]))

        _, d2 = EDIT.add_relic(updated, _relic())
        self.assertTrue(d2)  # duplicate

        updated2, d3 = EDIT.remove_relic(updated, "ns:relic-a")
        self.assertEqual([], d3)
        self.assertEqual(0, len(updated2.get("relics", [])))

    def test_set_relic_card_change_upserts(self) -> None:
        pack = self._pack()
        pack = EDIT.add_relic(pack, _relic())[0]
        updated, diagnostics = EDIT.set_relic_card_change(
            pack, "ns:relic-a", "MoveSpeed", "Add", 0.05
        )
        self.assertEqual([], diagnostics)
        changes = {c["valueToChange"]: c for c in updated["relics"][0]["cardChanges"]}
        self.assertIn("Health", changes)
        self.assertIn("MoveSpeed", changes)

        # update existing
        updated2, d2 = EDIT.set_relic_card_change(
            updated, "ns:relic-a", "Health", "Multiply", 1.2
        )
        self.assertEqual([], d2)
        health = [c for c in updated2["relics"][0]["cardChanges"]
                  if c["valueToChange"] == "Health"][0]
        self.assertEqual(1.2, health["value"])

    def test_set_relic_card_change_unknown_relic(self) -> None:
        pack = self._pack()
        _, diagnostics = EDIT.set_relic_card_change(pack, "ns:ghost", "Health", "Add", 1)
        self.assertTrue(diagnostics)


class BundleEditTests(unittest.TestCase):
    def _pack(self) -> dict:
        return SCAFFOLD.generate_scaffold("ns:pack", "alpha")

    def test_add_and_remove_bundle(self) -> None:
        pack = self._pack()
        bundle = {"id": "ns:assets", "path": "bundles/ns.bundle"}
        updated, diagnostics = EDIT.add_bundle(pack, bundle)
        self.assertEqual([], diagnostics)
        self.assertEqual(1, len(updated["assetBundles"]))

        _, d2 = EDIT.add_bundle(updated, bundle)
        self.assertTrue(d2)  # duplicate

        updated2, d3 = EDIT.remove_bundle(updated, "ns:assets")
        self.assertEqual([], d3)
        self.assertEqual([], updated2.get("assetBundles", []))


class DuplicateUnitTests(unittest.TestCase):
    def test_duplicate_unit_new_id(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        updated, diagnostics = EDIT.duplicate_unit(pack, "ns:alpha", "ns:alpha-2")
        self.assertEqual([], diagnostics)
        ids = [u["id"] for u in updated["units"]]
        self.assertEqual(["ns:alpha", "ns:alpha-2"], ids)
        factories = [u["productionBuilding"]["id"] for u in updated["units"]]
        self.assertIn("ns:alpha-2-factory", factories)

    def test_duplicate_to_existing_fails(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        _, diagnostics = EDIT.duplicate_unit(pack, "ns:alpha", "ns:alpha")
        self.assertTrue(diagnostics)

    def test_duplicate_unknown_fails(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        _, diagnostics = EDIT.duplicate_unit(pack, "ns:ghost", "ns:x")
        self.assertTrue(diagnostics)


if __name__ == "__main__":
    unittest.main()
