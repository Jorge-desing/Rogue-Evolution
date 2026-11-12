"""The shared Level 2 hard-surface contract covers every reusable factory."""

from __future__ import annotations

import json
import inspect
from pathlib import Path
import sys
import unittest

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import hard_surface_geometry_presets as geometry  # noqa: E402


PACKS = ("sample-native-expansion", "tactical-operations")


class CanonicalGeometryPresetRegistryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.registry = json.loads(
            (REPO_ROOT / "content/source-art/canonical-visual-module-roles.json").read_text(encoding="utf-8")
        )
        cls.bindings = {
            row["geometryPresetId"]: row for row in cls.registry["geometryPresetBindings"]
        }

    def test_registry_matches_the_complete_geometry_factory_catalog(self) -> None:
        self.assertEqual(self.registry["schemaVersion"], 2)
        self.assertEqual(set(self.bindings), set(geometry.GEOMETRY_PRESETS))
        self.assertEqual(set(geometry.PRESET_METADATA), set(geometry.GEOMETRY_PRESETS))
        for preset_id, binding in self.bindings.items():
            metadata = geometry.PRESET_METADATA[preset_id]
            with self.subTest(preset=preset_id):
                self.assertEqual(binding["slotId"], metadata["slot"])
                self.assertEqual(binding["geometryEngineeringRole"], metadata["engineeringRole"])
                self.assertTrue(binding["visualEngineeringRoles"])
                for visual_role in binding["visualEngineeringRoles"]:
                    self.assertIn(visual_role, self.registry["roleDefinitions"])
                    self.assertIn(binding["slotId"], self.registry["roleDefinitions"][visual_role]["allowedSlots"])

    def test_every_pack_level_two_module_has_one_compatible_preset_binding(self) -> None:
        declared: set[tuple[str, str]] = set()
        for pack_name in PACKS:
            pack = json.loads(
                (REPO_ROOT / "content/packs" / pack_name / "content.json").read_text(encoding="utf-8")
            )
            for unit in pack.get("units", []):
                variants = unit.get("visualVariants") or []
                if variants and variants[0].get("modules"):
                    self.fail(f"Level 1 variant declares modules: {variants[0]['id']}")
                for variant in variants[1:]:
                    modules = variant.get("modules") or []
                    self.assertLessEqual(len(modules), 4, variant["id"])
                    slots = [module["slotId"] for module in modules]
                    self.assertEqual(len(slots), len(set(slots)), variant["id"])
                    for module in modules:
                        self.assertTrue(module["assetName"].startswith("assets/"))
                        self.assertTrue(module["assetName"].endswith(".prefab"))
                        key = (variant["id"], module["slotId"])
                        self.assertNotIn(key, declared)
                        declared.add(key)

        roles = {
            (row["variantId"], row["slotId"]): row
            for row in self.registry["variantRoles"]
        }
        self.assertEqual(set(roles), declared)
        self.assertEqual(len(declared), 57)
        self.assertEqual(len({variant_id for variant_id, _ in declared}), 35)
        self.assertEqual({row["geometryPreset"] for row in roles.values()}, set(self.bindings))
        for key, row in roles.items():
            preset = self.bindings[row["geometryPreset"]]
            with self.subTest(variant=key[0], slot=key[1]):
                self.assertEqual(row["slotId"], preset["slotId"])
                self.assertIn(row["visualEngineeringRole"], preset["visualEngineeringRoles"])
                allowed_variants = preset.get("allowedVariantIds") or []
                self.assertTrue(not allowed_variants or row["variantId"] in allowed_variants)

    def test_every_registered_factory_exposes_typed_config_and_generic_defaults(self) -> None:
        specialized = {
            geometry.SERVICE_HATCH_PRESET_ID,
            geometry.ERA_BRICK_PRESET_ID,
            geometry.COAXIAL_SERVICE_CONNECTOR_PRESET_ID,
        }
        for preset_id, creator in geometry.GEOMETRY_PRESETS.items():
            with self.subTest(preset=preset_id):
                parameter = inspect.signature(creator).parameters.get("config")
                self.assertIsNotNone(parameter)
                assert parameter is not None
                self.assertIsNot(parameter.annotation, inspect.Parameter.empty)
                annotation = parameter.annotation
                if isinstance(annotation, str):
                    annotation = annotation.strip("'\"")
                    config_type = creator.__globals__.get(annotation)
                else:
                    config_type = annotation
                self.assertIsInstance(config_type, type)
                assert isinstance(config_type, type)
                required = [
                    item.name
                    for item in inspect.signature(config_type).parameters.values()
                    if item.default is inspect.Parameter.empty
                    and item.kind in (inspect.Parameter.POSITIONAL_ONLY,
                                      inspect.Parameter.POSITIONAL_OR_KEYWORD,
                                      inspect.Parameter.KEYWORD_ONLY)
                ]
                if preset_id in specialized:
                    self.assertTrue(required)
                else:
                    self.assertFalse(required)
                    self.assertIsInstance(config_type(), config_type)

    def test_layout_is_exactly_bound_to_its_variant_slot_role_and_preset(self) -> None:
        layouts = {row["geometryLayoutId"]: row for row in self.registry["geometryLayouts"]}
        bound_layouts = [row for row in self.registry["variantRoles"] if row.get("geometryLayout")]
        self.assertEqual(len(bound_layouts), len(layouts))
        for row in bound_layouts:
            layout = layouts[row["geometryLayout"]]
            with self.subTest(layout=row["geometryLayout"]):
                self.assertEqual(row["variantId"], layout["variantId"])
                self.assertEqual(row["slotId"], layout["slotId"])
                self.assertEqual(row["visualEngineeringRole"], layout["visualEngineeringRole"])
                self.assertEqual(row["geometryPreset"], layout["requiredGeometryPresetId"])


if __name__ == "__main__":
    unittest.main()
