"""Tests de upgrades, landscape, assets e identity en edit_content_pack."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

EDIT = load_tool_module("edit_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


def _upgrade(upgrade_id: str = "ns:upg-a") -> dict:
    return {
        "id": upgrade_id,
        "assetBundleId": "ns:assets",
        "imageAssetName": "img/upg.png",
        "rarity": "Rare",
        "tech": "Mech",
        "offeredSystemTags": "Mech",
        "neededSystemTags": "Mech",
        "neededExperienceLevel": 0,
        "isAllowedForDemo": False,
        "isForSpecialists": False,
        "localizations": [
            {"language": "en-US", "name": "U", "description": "D"},
            {"language": "es-XL", "name": "U", "description": "D"},
        ],
        "cardChanges": [
            {"valueToChange": "Health", "operation": "Multiply", "value": 1.1}
        ],
    }


class UpgradeEditTests(unittest.TestCase):
    def _pack(self) -> dict:
        return SCAFFOLD.generate_scaffold("ns:pack", "alpha")

    def test_add_and_remove_upgrade(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.add_upgrade(pack, _upgrade())
        self.assertEqual([], diagnostics)
        self.assertEqual(1, len(updated["upgrades"]))

        _, d2 = EDIT.add_upgrade(updated, _upgrade())
        self.assertTrue(d2)

        updated2, d3 = EDIT.remove_upgrade(updated, "ns:upg-a")
        self.assertEqual([], d3)
        self.assertEqual(0, len(updated2.get("upgrades", [])))

    def test_set_upgrade_field_and_card_change(self) -> None:
        pack = self._pack()
        updated, _ = EDIT.add_upgrade(pack, _upgrade())

        updated2, d2 = EDIT.set_upgrade_field(updated, "ns:upg-a", "rarity", "UltraRare")
        self.assertEqual([], d2)
        self.assertEqual("UltraRare", updated2["upgrades"][0]["rarity"])

        _, d3 = EDIT.set_upgrade_field(updated, "ns:upg-a", "bogus", 1)
        self.assertTrue(d3)

        updated3, d4 = EDIT.set_upgrade_card_change(
            updated2, "ns:upg-a", "MoveSpeed", "Add", 0.05
        )
        self.assertEqual([], d4)
        changes = {c["valueToChange"]: c for c in updated3["upgrades"][0]["cardChanges"]}
        self.assertIn("MoveSpeed", changes)


class LandscapeEditTests(unittest.TestCase):
    def test_set_stage_landscape(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["stages"][0]["landscape"] = {
            "nativeTemplateId": "27 Circles",
            "randomSeed": 7,
            "alwaysUseSameSeed": True,
            "mapSize": 45,
            "localizations": [{"language": "en-US", "name": "N"}],
        }
        updated, diagnostics = EDIT.set_stage_landscape(
            pack, "ns:training-stage", seed=42, map_size=63
        )
        self.assertEqual([], diagnostics)
        landscape = updated["stages"][0]["landscape"]
        self.assertEqual(42, landscape["randomSeed"])
        self.assertEqual(63, landscape["mapSize"])

        _, d2 = EDIT.set_stage_landscape(pack, "ns:training-stage", map_size=44)
        self.assertTrue(d2)  # device: mapSize must be odd


class AssetsEditTests(unittest.TestCase):
    def test_set_unit_assets(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        updated, diagnostics = EDIT.set_unit_assets(
            pack, "ns:alpha",
            asset_bundle_id="ns:assets",
            card_image="img/card.png",
        )
        self.assertEqual([], diagnostics)
        unit = [u for u in updated["units"] if u["id"] == "ns:alpha"][0]
        self.assertEqual("ns:assets", unit["assetBundleId"])
        self.assertEqual("img/card.png", unit["cardImageAssetName"])


class IdentityEditTests(unittest.TestCase):
    def test_set_pack_id_and_schema_version(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        updated, diagnostics = EDIT.set_pack_id(pack, "ns:renamed")
        self.assertEqual([], diagnostics)
        self.assertEqual("ns:renamed", updated["packId"])

        _, d2 = EDIT.set_pack_id(pack, "bad id")
        self.assertTrue(d2)

        updated2, d3 = EDIT.set_schema_version(pack, 1)
        self.assertEqual([], d3)
        self.assertEqual(1, updated2["schemaVersion"])

        _, d4 = EDIT.set_schema_version(pack, 3)
        self.assertTrue(d4)


if __name__ == "__main__":
    unittest.main()
