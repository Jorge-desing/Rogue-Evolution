"""Regression checks for the Bulwark's canonical Level 2 ERA binding."""

from __future__ import annotations

import json
import sys
from pathlib import Path

import unittest

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "tools"))

from hard_surface_geometry_presets import ERA_BRICK_PRESET_ID, GEOMETRY_PRESETS


VARIANT_ID = "tactical-operations:bulwark-siege-tank-battleship-tungsten"
EXPECTED_MODULE_ASSET = (
    "assets/roguecommandexpansionsample/visualmodules/prefabs/"
    "rclmodule_bulwark_siege_tank_battleship_tungsten.prefab"
)
EXPECTED_LAYOUT = "bulwark_side_skirt_array_v1"


class BulwarkEraCanonicalIntegrationTests(unittest.TestCase):
    def test_battleship_variant_declares_one_canonical_era_module(self) -> None:
        pack_path = REPO_ROOT / "content/packs/tactical-operations/content.json"
        pack = json.loads(pack_path.read_text(encoding="utf-8"))
        unit = next(row for row in pack["units"] if row["id"] == "tactical-operations:bulwark-siege-tank")
        variant = next(row for row in unit["visualVariants"] if row["id"] == VARIANT_ID)

        self.assertEqual(
            variant.get("modules"),
            [{"slotId": "ERA", "assetName": EXPECTED_MODULE_ASSET}],
        )

    def test_variant_binds_reusable_era_preset_to_side_skirt_layout(self) -> None:
        roles_path = REPO_ROOT / "content/source-art/canonical-visual-module-roles.json"
        registry = json.loads(roles_path.read_text(encoding="utf-8"))
        rows = [row for row in registry["variantRoles"] if row.get("variantId") == VARIANT_ID]
        self.assertEqual(len(rows), 1)

        row = rows[0]
        self.assertEqual(row["slotId"], "ERA")
        self.assertEqual(row["visualEngineeringRole"], "armor_cassette")
        self.assertEqual(row["geometryPreset"], ERA_BRICK_PRESET_ID)
        self.assertEqual(row["geometryLayout"], EXPECTED_LAYOUT)

        role = registry["roleDefinitions"][row["visualEngineeringRole"]]
        self.assertIn("ERA", role["allowedSlots"])
        self.assertIn(ERA_BRICK_PRESET_ID, GEOMETRY_PRESETS)


if __name__ == "__main__":
    unittest.main()
