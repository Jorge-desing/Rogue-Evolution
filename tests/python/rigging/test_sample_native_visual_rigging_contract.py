"""Serialized visual contracts for sample-native skinned units."""

from __future__ import annotations

import re
import unittest
from pathlib import Path
from typing import Any

import UnityPy


REPO_ROOT = Path(__file__).resolve().parents[3]
BUNDLE_PATH = REPO_ROOT / "content/packs/sample-native-expansion/bundles/sample-native-expansion.bundle"
VISUAL_PREFABS = ("ArachnidHexapodVisual", "OverlordCommandHQVisual")


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


def _movement_transition_destinations(controller_path: Path) -> list[str]:
    text = controller_path.read_text(encoding="utf-8")
    states: dict[str, str] = {}
    movement_destinations: list[str] = []
    documents = re.split(r"(?=^--- !u!)", text, flags=re.MULTILINE)

    for document in documents:
        header = re.search(r"^--- !u!(\\d+) &(-?\\d+)", document, flags=re.MULTILINE)
        if header is None:
            continue
        component_type, file_id = header.groups()
        if component_type == "1102":
            name = re.search(r"^  m_Name: ?(.*)$", document, flags=re.MULTILINE)
            if name is not None:
                states[file_id] = name.group(1).strip()
        elif component_type == "1101" and "m_ConditionEvent: RCL_IsMoving" in document:
            destination = re.search(r"^  m_DstState: \\{fileID: (-?\\d+)", document, flags=re.MULTILINE)
            if destination is not None:
                movement_destinations.append(destination.group(1))

    return [states.get(file_id, "<missing state>") for file_id in movement_destinations]


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
        expected_actions = {
            "ArachnidHexapod": "ArachnidHexapod_Armature|ArachnidHexapod_Action_Walk",
            "OverlordCommandHQ": "OverlordCommandHQ_Armature|OverlordCommandHQ_Action_Drive",
        }
        animation_directory = (
            REPO_ROOT
            / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Animations"
        )

        for unit_name, expected_action in expected_actions.items():
            controller_path = animation_directory / f"{unit_name}Controller.controller"
            with self.subTest(unit=unit_name):
                self.assertIn(
                    expected_action,
                    _movement_targets_from_action_idle(controller_path),
                    f"{unit_name} movement must enter its rig-bound action, not a generic clip",
                )


def _movement_targets_from_action_idle(controller_path: Path) -> list[str]:
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

    targets: list[str] = []
    for state_id, (state_name, transition_ids) in states.items():
        if "Action_Idle" not in state_name:
            continue
        for transition_id in transition_ids:
            destination_id, conditions = transitions.get(transition_id, ("", []))
            if ("1", "RCL_IsMoving") in conditions and destination_id in states:
                targets.append(states[destination_id][0])
    return targets


if __name__ == "__main__":
    unittest.main()
