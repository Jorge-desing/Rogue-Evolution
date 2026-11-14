"""PBR authoring contract: vertex wear, MetallicGloss atlas, and import readiness.

Guards that every Rogue Command authoring entry point (RogueModelKit,
generate_unit_model, Build-* builders) runs the current PBR pipeline:

- RGBA vertex wear (R cavity AO, G structural edge wear, B thermal soot,
  A team mask) via ``bake_advanced_vertex_shading`` with the structural mask.
- The four-map atlas bake (Albedo, MetallicGloss, Normal, Emission) into
  ``Models/Textures`` plus the single-material ``_Atlas.fbx`` consumed by
  ``VehiclePrefabBuilder`` for its URP TextureImporter contract.
- CLI/builder forwarding of the wear and atlas switches on both the
  vehicle and structure paths.

These are static source-contract checks (no Blender runtime required);
Blender bake behavior itself is covered by
``tests/python/blender/test_blender_bake_contract.py``.
"""

from __future__ import annotations

import ast
import unittest
from pathlib import Path

from tests.python.helpers import TOOLS_DIR, parse_ast_cached, read_text_cached

TOOLS = TOOLS_DIR
RMK = TOOLS / "RogueModelKit.py"
GUM = TOOLS / "generate_unit_model.py"

RIGGED_BUILDERS = [
    "Build-ArcLanceWalkerModel.py",
    "Build-BulwarkSiegeTankModel.py",
    "Build-ColossusSiegeRiggedModel.py",
    "Build-FieldMedicRoverModel.py",
    "Build-OverlordCommandHQRiggedModel.py",
    "Build-SalvageHaulerModel.py",
    "Build-SandlanceScoutRiggedModel.py",
    "Build-SkirmishStrikeBikeRiggedModel.py",
    "Build-SkyguardSAMModel.py",
    "Build-SurgeArcTankRiggedModel.py",
    "Build-TidebreakerHovercraftModel.py",
    "Build-ArachnidHexapodRiggedModel.py",
]

MANUAL_BUILDERS = [
    "Build-SampleScoutModel.py",
    "Build-ColossusSiegeModel.py",
    "Build-SkirmishStrikeBikeModel.py",
    "Build-SurgeArcTankModel.py",
]

PRESET_BUILDERS = [
    "Build-ArachnidHexapodModel.py",
    "Build-OverlordCommandHQModel.py",
    "Build-CommandRelayModel.py",
    "Build-GravitasBreakerModel.py",
    "Build-SentinelRelayModel.py",
]


def _function_arg_names(path: Path, class_name: str, function_name: str) -> set[str]:
    tree = parse_ast_cached(path)
    for node in tree.body:
        if isinstance(node, ast.ClassDef) and node.name == class_name:
            for item in node.body:
                if isinstance(item, ast.FunctionDef) and item.name == function_name:
                    return {arg.arg for arg in item.args.args} | {
                        arg.arg for arg in item.args.kwonlyargs
                    }
    return set()


def _module_function_arg_names(path: Path, function_name: str) -> set[str]:
    tree = parse_ast_cached(path)
    for node in tree.body:
        if isinstance(node, ast.FunctionDef) and node.name == function_name:
            return {arg.arg for arg in node.args.args} | {
                arg.arg for arg in node.args.kwonlyargs
            }
    return set()


class PbrAuthoringContractTests(unittest.TestCase):
    def test_structure_builder_accepts_structural_wear_mask(self) -> None:
        source = read_text_cached(RMK)
        args = _function_arg_names(RMK, "RogueStructureBuilder", "build_and_export")
        self.assertIn("structural_wear_mask", args)
        structure_slice = source[source.index("class RogueStructureBuilder") :]
        self.assertIn("structural_wear_mask=structural_wear_mask", structure_slice)

    def test_vehicle_builder_accepts_structural_wear_mask(self) -> None:
        args = _function_arg_names(RMK, "RogueVehicleBuilder", "build_and_export")
        self.assertIn("structural_wear_mask", args)

    def test_generate_structure_forwards_structural_wear_mask(self) -> None:
        source = read_text_cached(GUM)
        args = _module_function_arg_names(GUM, "build_structure_model")
        self.assertIn("structural_wear_mask", args)
        structure_call = source[source.index("res = build_structure_model(") :]
        structure_call = structure_call[: structure_call.index("res = build_vehicle_model(")]
        self.assertIn("structural_wear_mask=args.structural_wear_mask", structure_call)
        vehicle_call = source[source.index("res = build_vehicle_model(") :]
        vehicle_call = vehicle_call[: vehicle_call.index("if args.report_json")]
        self.assertIn("structural_wear_mask=args.structural_wear_mask", vehicle_call)

    def test_ironclad_builds_metallic_gloss_atlas(self) -> None:
        source = (TOOLS / "Build-IroncladWalkerModel.py").read_text(encoding="utf-8")
        self.assertIn("bake_atlas=True", source)
        self.assertIn("single_material_atlas=True", source)

    def test_rigged_builders_bake_vertex_wear_before_batching(self) -> None:
        for name in RIGGED_BUILDERS:
            with self.subTest(builder=name):
                source = (TOOLS / name).read_text(encoding="utf-8")
                self.assertIn("bake_advanced_vertex_shading", source)
                self.assertIn("structural_wear_mask=True", source)
                self.assertLess(
                    source.index("bake_advanced_vertex_shading"),
                    source.index("batch_and_join_by_material"),
                )

    def test_arachnid_rigged_refreshes_pbr_atlas(self) -> None:
        source = (TOOLS / "Build-ArachnidHexapodRiggedModel.py").read_text(encoding="utf-8")
        self.assertIn("bake_pbr_texture_atlas", source)
        self.assertIn("create_single_material_atlas_model", source)

    def test_manual_builders_bake_vertex_wear_before_saving_source(self) -> None:
        for name in MANUAL_BUILDERS:
            with self.subTest(builder=name):
                source = (TOOLS / name).read_text(encoding="utf-8")
                self.assertIn("bake_advanced_vertex_shading", source)
                self.assertIn("structural_wear_mask=True", source)
                self.assertLess(
                    source.index("bake_advanced_vertex_shading"),
                    source.index("bpy.ops.wm.save_as_mainfile"),
                )

    def test_preset_builders_keep_pbr_flags(self) -> None:
        for name in PRESET_BUILDERS:
            with self.subTest(builder=name):
                source = (TOOLS / name).read_text(encoding="utf-8")
                self.assertIn("--structural-wear-mask", source)
                self.assertIn("--bake-atlas", source)


if __name__ == "__main__":
    unittest.main()
