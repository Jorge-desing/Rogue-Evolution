"""Serialized visual contracts for sample-native skinned units."""

from __future__ import annotations

import re
import unittest
from pathlib import Path
from typing import Any

import UnityPy


REPO_ROOT = Path(__file__).resolve().parents[3]
BUNDLE_PATH = REPO_ROOT / "content/packs/sample-native-expansion/bundles/sample-native-expansion.bundle"
VISUAL_UNITS = {
    "ArachnidHexapod": "ArachnidHexapodVisual",
    "OverlordCommandHQ": "OverlordCommandHQVisual",
    "AtlasCraneMech": "AtlasCraneMechVisual",
    "BastionSiegeMech": "BastionSiegeMechVisual",
    "ColossusSiegePlatform": "ColossusSiegeVisual",
    "IroncladWalker": "IroncladWalkerVisual",
    "SandlanceScout": "ScoutTankVisual",
    "SkirmishStrikeBike": "SkirmishBikeVisual",
    "SurgeArcTank": "SurgeArcTankVisual",
}
VISUAL_PREFABS = tuple(VISUAL_UNITS.values())
MOVEMENT_ACTIONS = {
    "ArachnidHexapod": "ArachnidHexapod_Armature|ArachnidHexapod_Action_Walk",
    "OverlordCommandHQ": "OverlordCommandHQ_Armature|OverlordCommandHQ_Action_Drive",
    "AtlasCraneMech": "AtlasCraneMech_Armature|AtlasCraneMech_Action_Walk",
    "BastionSiegeMech": "Walk",
    "ColossusSiegePlatform": "ColossusSiegePlatform_Armature|ColossusSiegePlatform_Action_Drive",
    "IroncladWalker": "IroncladWalker_Armature|IroncladWalker_Action_Walk",
    "SandlanceScout": "SandlanceScout_Armature|SandlanceScout_Action_Drive",
    "SkirmishStrikeBike": "SkirmishStrikeBike_Armature|SkirmishStrikeBike_Action_Drive",
    "SurgeArcTank": "SurgeArcTank_Armature|SurgeArcTank_Action_Drive",
}
IDLE_ACTIONS = {
    unit_name: ("Idle" if unit_name == "BastionSiegeMech" else f"{unit_name}_Armature|{unit_name}_Action_Idle")
    for unit_name in VISUAL_UNITS
}


def _read(pointer: Any) -> Any:
    return pointer.read() if hasattr(pointer, "read") else pointer


def _collect_components(root: Any) -> list[str]:
    component_types: list[str] = []

    def visit(game_object: Any) -> None:
        component_types.extend(type(_read(pair.component)).__name__ for pair in game_object.m_Component)
        for pair in game_object.m_Component:
            component = _read(pair.component)
            if type(component).__name__ != "Transform":
                continue
            for child_pointer in component.m_Children:
                visit(_read(_read(child_pointer).m_GameObject))

    visit(root)
    return component_types



class SampleNativeVisualRiggingContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if not BUNDLE_PATH.is_file():
            raise unittest.SkipTest(f"sample-native bundle is missing: {BUNDLE_PATH}")
        cls.objects = list(UnityPy.load(str(BUNDLE_PATH)).objects)
        cls.prefabs = {
            obj.read().m_Name: obj.read()
            for obj in cls.objects
            if obj.type.name == "GameObject" and obj.read().m_Name in VISUAL_PREFABS
        }

    def test_rigged_visuals_have_one_animator_for_the_skeletal_hierarchy(self) -> None:
        for prefab_name in VISUAL_PREFABS:
            with self.subTest(prefab=prefab_name):
                self.assertIn(prefab_name, self.prefabs, f"{prefab_name} must be present in the sample bundle")
                components = _collect_components(self.prefabs[prefab_name])
                self.assertIn("SkinnedMeshRenderer", components)
                self.assertEqual(
                    components.count("Animator"),
                    1,
                    f"{prefab_name} must have exactly one Animator driving its rig",
                )

    def test_movement_state_uses_each_rigged_visuals_action_clip(self) -> None:
        animation_directory = (
            REPO_ROOT
            / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Animations"
        )

        for unit_name, expected_action in MOVEMENT_ACTIONS.items():
            controller_path = animation_directory / f"{unit_name}Controller.controller"
            expected_idle = IDLE_ACTIONS[unit_name]
            with self.subTest(unit=unit_name):
                self.assertEqual(
                    [expected_action],
                    _movement_targets_from_action_idle(controller_path, unit_name),
                    f"{unit_name} movement must enter its rig-bound action, not a generic clip",
                )
                self.assertEqual(
                    [expected_idle],
                    _movement_return_targets(controller_path, expected_action),
                    f"{unit_name} rigged movement must return only to its rigged idle action",
                )

    def test_each_rigged_movement_clip_is_in_the_sample_bundle(self) -> None:
        bundle_clips = {obj.read().m_Name for obj in self.objects if obj.type.name == "AnimationClip"}
        for unit_name, expected_action in MOVEMENT_ACTIONS.items():
            with self.subTest(unit=unit_name):
                self.assertIn(
                    expected_action,
                    bundle_clips,
                    f"{unit_name} movement clip must be imported into the sample bundle",
                )

    def test_each_visual_fbx_imports_its_locomotion_clips_as_loops(self) -> None:
        model_directory = (
            REPO_ROOT / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models"
        )

        for unit_name in VISUAL_UNITS:
            importer_meta = model_directory / f"{unit_name}.fbx.meta"
            with self.subTest(unit=unit_name):
                self.assertTrue(importer_meta.is_file(), f"{unit_name} FBX importer metadata must exist")
                text = importer_meta.read_text(encoding="utf-8")
                clip_section = re.search(
                    r"(?ms)^    clipAnimations:\n(.*?)(?=^  [A-Za-z]|\Z)", text
                )
                self.assertIsNotNone(clip_section, f"{unit_name} must persist explicit clip import settings")
                clip_blocks = re.split(r"(?=^    - serializedVersion:)", clip_section.group(1), flags=re.MULTILINE)
                looping_locomotion = [
                    block
                    for block in clip_blocks
                    if re.search(r"(?m)^      name: .*?(?:Idle|Walk|Drive)", block)
                    and re.search(r"(?m)^      loopTime: 1$", block)
                ]
                self.assertTrue(
                    looping_locomotion,
                    f"{unit_name} must loop its imported Idle, Walk, or Drive clip",
                )


def _parse_animator_controller(
    controller_path: Path,
) -> tuple[dict[str, tuple[str, list[str]]], dict[str, tuple[str, list[tuple[str, str]]]]]:
    documents = re.split(r"(?=^--- !u!)", controller_path.read_text(encoding="utf-8"), flags=re.MULTILINE)
    states: dict[str, tuple[str, list[str]]] = {}
    transitions: dict[str, tuple[str, list[tuple[str, str]]]] = {}

    for document in documents:
        header = re.search(r"^--- !u!(\d+) &(-?\d+)", document, flags=re.MULTILINE)
        if header is None:
            continue
        component_type, file_id = header.groups()
        if component_type == "1102":
            name = re.search(r"^  m_Name: ?(.*)$", document, flags=re.MULTILINE)
            transition_block = re.search(
                r"(?ms)^  m_Transitions:\n(.*?)(?=^  m_StateMachineBehaviours:)", document
            )
            transition_ids = (
                re.findall(r"\{fileID: (-?\d+)\}", transition_block.group(1))
                if transition_block is not None else []
            )
            states[file_id] = (name.group(1).strip() if name else "", transition_ids)
        elif component_type == "1101":
            destination = re.search(r"^  m_DstState: \{fileID: (-?\d+)", document, flags=re.MULTILINE)
            conditions = re.findall(
                r"(?m)^  - m_ConditionMode: (\d+)\n    m_ConditionEvent: (.*?)\n", document
            )
            transitions[file_id] = (destination.group(1) if destination else "", conditions)
    return states, transitions


def _movement_targets_from_action_idle(controller_path: Path, unit_name: str) -> list[str]:
    states, transitions = _parse_animator_controller(controller_path)
    idle_name = IDLE_ACTIONS[unit_name]
    idle_ids = [file_id for file_id, (name, _) in states.items() if name == idle_name]
    targets: list[str] = []
    for idle_id in idle_ids:
        for transition_id in states[idle_id][1]:
            destination_id, conditions = transitions.get(transition_id, ("", []))
            if ("1", "RCL_IsMoving") in conditions and destination_id in states:
                targets.append(states[destination_id][0])
    return targets


def _movement_return_targets(controller_path: Path, movement_state_name: str) -> list[str]:
    states, transitions = _parse_animator_controller(controller_path)
    movement_ids = [file_id for file_id, (name, _) in states.items() if name == movement_state_name]
    targets: list[str] = []
    for movement_id in movement_ids:
        for transition_id in states[movement_id][1]:
            destination_id, conditions = transitions.get(transition_id, ("", []))
            if ("2", "RCL_IsMoving") in conditions and destination_id in states:
                targets.append(states[destination_id][0])
    return targets


if __name__ == "__main__":
    unittest.main()
