from __future__ import annotations

import json
import subprocess
import tempfile
import UnityPy
import re
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[3]
CONTROLLER_PATH = ROOT / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Animations/IroncladWalkerController.controller"
PREFAB_PATH = ROOT / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/IroncladWalkerVisual.prefab"
MATERIAL_ROOT = ROOT / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Materials"
SHADER_PATH = ROOT / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Shaders/VehiclePBR_VertexWear.shader"


def parse_controller(text: str) -> tuple[dict[int, str], dict[int, dict[str, object]], dict[int, dict[str, object]]]:
    states: dict[int, str] = {}
    state_transitions: dict[int, dict[str, object]] = {}
    any_state_transitions: dict[int, dict[str, object]] = {}
    state_transition_ids: dict[int, list[int]] = {}
    any_state_transition_ids: list[int] = []

    for document in re.split(r"(?=--- !u!)", text):
        header = re.match(r"--- !u!(\d+) &(\-?\d+)", document)
        if not header:
            continue
        kind, object_id = header.group(1), int(header.group(2))
        if kind == "1102":
            name_match = re.search(r"(?m)^  m_Name: (.*)$", document)
            states[object_id] = name_match.group(1) if name_match else ""
            transition_block = re.search(
                r"(?ms)^  m_Transitions:\n(.*?)(?=^  m_StateMachineBehaviours:)", document
            )
            state_transition_ids[object_id] = (
                [int(value) for value in re.findall(r"\{fileID: (\-?\d+)\}", transition_block.group(1))]
                if transition_block else []
            )
        elif kind == "1101":
            destination = re.search(r"(?m)^  m_DstState: \{fileID: (\-?\d+)\}$", document)
            conditions = re.findall(
                r"(?m)^  - m_ConditionMode: (\d+)\n    m_ConditionEvent: (.*?)\n", document
            )
            state_transitions[object_id] = {
                "destination": int(destination.group(1)) if destination else 0,
                "conditions": conditions,
                "has_exit_time": bool(re.search(r"(?m)^  m_HasExitTime: 1$", document)),
            }
        elif kind == "1107":
            transition_block = re.search(
                r"(?ms)^  m_AnyStateTransitions:\n(.*?)(?=^  m_EntryTransitions:)", document
            )
            if transition_block:
                any_state_transition_ids.extend(
                    int(value) for value in re.findall(r"\{fileID: (\-?\d+)\}", transition_block.group(1))
                )

    for state_id, transition_ids in state_transition_ids.items():
        for transition_id in transition_ids:
            if transition_id in state_transitions:
                state_transitions[transition_id]["source"] = state_id
    for transition_id in any_state_transition_ids:
        if transition_id in state_transitions:
            any_state_transitions[transition_id] = state_transitions[transition_id]
            any_state_transitions[transition_id]["source"] = "AnyState"
    return states, state_transitions, any_state_transitions


class IroncladWalkerAssetContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.controller_text = CONTROLLER_PATH.read_text(encoding="utf-8")
        # Inspect current sources, rather than reports from a prior local session.
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "actions.json"
            script = Path(directory) / "inspect.py"
            script.write_text("import bpy,json\nfrom pathlib import Path\nbpy.ops.wm.open_mainfile(filepath=" + repr(str(ROOT / "content/source-art/ironclad-walker.blend")) + ")\nactions=[{'name':a.name,'tracks':[{'bone':f.data_path.split(chr(34))[1]} for f in a.fcurves if 'pose.bones[' in f.data_path]} for a in bpy.data.actions]\nPath(" + repr(str(output)) + ").write_text(json.dumps({'actions':actions}))\n", encoding="utf-8")
            subprocess.run([str(ROOT / ".tools/Blender-4.5.14/blender-4.5.14-windows-x64/blender.exe"), "--background", "--python-exit-code", "1", "--python", str(script)], check=True, capture_output=True, timeout=120)
            cls.blend = json.loads(output.read_text())
        env = UnityPy.load(str(ROOT / "content/packs/sample-native-expansion/bundles/sample-native-expansion.bundle"))
        hierarchy, materials = [], []
        for obj in env.objects:
            if obj.type.name == "GameObject":
                data = obj.read()
                hierarchy.append({"gameobject": data.m_Name, "component_types": [pair.component.deref().type.name for pair in data.m_Component]})
            elif obj.type.name == "Material":
                data = obj.read()
                materials.append({"name": data.m_Name, "textures": [{"property": name, "path_id": value.m_Texture.path_id} for name, value in data.m_SavedProperties.m_TexEnvs]})
        cls.bundle = {"hierarchy": hierarchy, "materials": materials}
        cls.prefab_text = PREFAB_PATH.read_text(encoding="utf-8")
        cls.states, cls.transitions, cls.any_transitions = parse_controller(cls.controller_text)

    def test_walking_transition_targets_walk_clip_not_death(self) -> None:
        walk_id = next(key for key, value in self.states.items() if "Action_Walk" in value)
        idle_id = next(
            key for key, value in self.states.items() if value.endswith("_Action_Idle")
        )
        transitions = [
            transition for transition in self.transitions.values()
            if transition.get("source") == idle_id and transition["destination"] == walk_id
        ]
        self.assertTrue(transitions, "Idle must transition to the imported Action_Walk state")
        self.assertIn(("1", "RCL_IsMoving"), transitions[0]["conditions"])

    def test_fire_recoil_returns_to_walk_when_unit_is_moving(self) -> None:
        walk_id = next(key for key, value in self.states.items() if "Action_Walk" in value)
        fire_id = next(
            key for key, value in self.states.items() if value.endswith("_Action_FireRecoil")
        )
        transitions = [
            transition for transition in self.transitions.values()
            if transition["source"] == fire_id and transition["destination"] == walk_id
        ]
        self.assertTrue(transitions, "FireRecoil must return to Action_Walk when RCL_IsMoving remains true")
        self.assertIn(("1", "RCL_IsMoving"), transitions[0]["conditions"])

    def test_walk_source_action_animates_leg_chains(self) -> None:
        walk = next(action for action in self.blend["actions"] if "Action_Walk" in action["name"])
        animated_bones = {track["bone"] for track in walk["tracks"]}
        for bone in ("Bone_Thigh_L", "Bone_Shin_L", "Bone_Foot_L", "Bone_Thigh_R", "Bone_Shin_R", "Bone_Foot_R"):
            with self.subTest(bone=bone):
                self.assertIn(bone, animated_bones)

    def test_bundle_has_exactly_one_runtime_animator(self) -> None:
        root = next(item for item in self.bundle["hierarchy"] if item["gameobject"] == "IroncladWalkerVisual")
        self.assertEqual(root["component_types"].count("Animator"), 1)

    def test_ironclad_batch_materials_have_no_null_pbr_texture_bindings(self) -> None:
        materials = [material for material in self.bundle["materials"] if material["name"].startswith("Ironclad")]
        self.assertEqual(len(materials), 5)
        for material in materials:
            with self.subTest(material=material["name"]):
                texture_bindings = {entry["property"]: entry["path_id"] for entry in material["textures"]}
                self.assertEqual(texture_bindings.get("_BaseMap"), 0)

    def test_shader_base_color_multiplies_vertex_color(self) -> None:
        shader = SHADER_PATH.read_text(encoding="utf-8")
        self.assertIn("input.color", shader)
        self.assertRegex(shader, r"surfaceData\.albedo\s*=\s*albedoAlpha\.rgb\s*\*\s*_BaseColor\.rgb")


if __name__ == "__main__":
    unittest.main()
