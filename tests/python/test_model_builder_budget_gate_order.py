"""Guard filesystem side effects behind calibrated geometry budgets."""

from __future__ import annotations

import ast
from pathlib import Path
import unittest


REPO_ROOT = Path(__file__).resolve().parents[2]
DIRECT_BUILDERS = (
    "Build-ArcLanceWalkerModel.py",
    "Build-AtlasCraneMechModel.py",
    "Build-ArachnidHexapodRiggedModel.py",
    "Build-BastionSiegeMechModel.py",
    "Build-BulwarkSiegeTankModel.py",
    "Build-ColossusSiegeModel.py",
    "Build-ColossusSiegeRiggedModel.py",
    "Build-FieldMedicRoverModel.py",
    "Build-OverlordCommandHQRiggedModel.py",
    "Build-SalvageHaulerModel.py",
    "Build-SampleScoutModel.py",
    "Build-SampleScoutTankModel.py",
    "Build-SandlanceScoutRiggedModel.py",
    "Build-SkirmishStrikeBikeModel.py",
    "Build-SkirmishStrikeBikeRiggedModel.py",
    "Build-SkyguardSAMModel.py",
    "Build-SurgeArcTankModel.py",
    "Build-SurgeArcTankRiggedModel.py",
    "Build-TidebreakerHovercraftModel.py",
)
SOURCE_PRESERVING_BUILDERS = (
    "Build-ArcLanceWalkerModel.py",
    "Build-BulwarkSiegeTankModel.py",
    "Build-FieldMedicRoverModel.py",
    "Build-SalvageHaulerModel.py",
    "Build-SkyguardSAMModel.py",
    "Build-TidebreakerHovercraftModel.py",
)
SIDE_EFFECT_CALLS = {
    "mkdir",
    "save_as_mainfile",
    "export_scene.fbx",
    "write_text",
    "write_bytes",
}


def _dotted_name(node: ast.AST) -> str:
    if isinstance(node, ast.Name):
        return node.id
    if isinstance(node, ast.Attribute):
        parent = _dotted_name(node.value)
        return f"{parent}.{node.attr}" if parent else node.attr
    return ""


def _calls(node: ast.AST) -> list[ast.Call]:
    return sorted(
        (item for item in ast.walk(node) if isinstance(item, ast.Call)),
        key=lambda item: (item.lineno, item.col_offset),
    )


def _build_function(module: ast.Module, path: Path) -> ast.FunctionDef:
    functions = [node for node in module.body if isinstance(node, ast.FunctionDef)]
    functions = [
        function for function in functions
        if any(
            _dotted_name(call.func).endswith((
                "enforce_face_budget_before_export",
                "resolve_geometry_budget",
            ))
            for call in _calls(function)
        )
    ]
    if len(functions) != 1:
        raise AssertionError(f"{path.name}: expected exactly one top-level build_* function")
    return functions[0]


class ModelBuilderBudgetGateOrderTests(unittest.TestCase):
    def test_direct_builder_outputs_follow_geometry_gate(self) -> None:
        for filename in DIRECT_BUILDERS:
            with self.subTest(builder=filename):
                path = REPO_ROOT / "tools" / filename
                module = ast.parse(path.read_text(encoding="utf-8"))
                calls = _calls(_build_function(module, path))
                gate_lines = [
                    call.lineno for call in calls
                    if _dotted_name(call.func).endswith((
                        "enforce_face_budget_before_export",
                        "resolve_geometry_budget",
                    ))
                ]
                self.assertTrue(gate_lines, f"{filename}: missing calibrated geometry gate")
                first_gate = min(gate_lines)
                side_effects = [
                    call for call in calls
                    if any(_dotted_name(call.func).endswith(name) for name in SIDE_EFFECT_CALLS)
                ]
                self.assertTrue(side_effects, f"{filename}: expected output side effects")
                early = [
                    f"{_dotted_name(call.func)} at line {call.lineno}"
                    for call in side_effects if call.lineno < first_gate
                ]
                self.assertEqual(early, [], f"{filename}: writes before geometry gate: {early}")

    def test_recipe_builders_never_save_over_canonical_source(self) -> None:
        for filename in SOURCE_PRESERVING_BUILDERS:
            with self.subTest(builder=filename):
                path = REPO_ROOT / "tools" / filename
                module = ast.parse(path.read_text(encoding="utf-8"))
                calls = _calls(_build_function(module, path))
                source_saves = [
                    call for call in calls
                    if _dotted_name(call.func) == "bpy.ops.wm.save_as_mainfile"
                    and any(
                        isinstance(node, ast.Name) and node.id == "SOURCE_BLEND"
                        for node in ast.walk(call)
                    )
                ]
                self.assertEqual(source_saves, [], f"{filename}: source blend must remain untouched")

    def test_shared_vehicle_and_structure_builders_gate_before_mkdir(self) -> None:
        path = REPO_ROOT / "tools" / "RogueModelKit.py"
        module = ast.parse(path.read_text(encoding="utf-8"))
        classes = {node.name: node for node in module.body if isinstance(node, ast.ClassDef)}
        for class_name in ("RogueVehicleBuilder", "RogueStructureBuilder"):
            with self.subTest(builder=class_name):
                class_node = classes[class_name]
                method = next(
                    node for node in class_node.body
                    if isinstance(node, ast.FunctionDef) and node.name == "build_and_export"
                )
                calls = _calls(method)
                gate_lines = [
                    call.lineno for call in calls
                    if _dotted_name(call.func).endswith("resolve_geometry_budget")
                ]
                mkdir_lines = [
                    call.lineno for call in calls
                    if _dotted_name(call.func).endswith("mkdir")
                ]
                self.assertTrue(gate_lines)
                self.assertTrue(mkdir_lines)
                self.assertGreater(min(mkdir_lines), min(gate_lines))

    def test_ironclad_defers_paths_to_the_shared_gated_builder(self) -> None:
        path = REPO_ROOT / "tools" / "Build-IroncladWalkerModel.py"
        module = ast.parse(path.read_text(encoding="utf-8"))
        builder = next(
            node for node in module.body
            if isinstance(node, ast.FunctionDef) and node.name == "build_ironclad_walker_model"
        )
        early_path_creation = [
            call.lineno for call in _calls(builder)
            if _dotted_name(call.func).endswith("mkdir")
        ]
        self.assertEqual(early_path_creation, [])


if __name__ == "__main__":
    unittest.main()
