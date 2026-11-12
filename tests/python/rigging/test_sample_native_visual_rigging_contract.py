"""Serialized visual contracts for sample-native skinned units."""

from __future__ import annotations

import json
import re
import unittest
from pathlib import Path
from typing import Any

import UnityPy


REPO_ROOT = Path(__file__).resolve().parents[3]
BUNDLE_PATH = REPO_ROOT / "content/packs/sample-native-expansion/bundles/sample-native-expansion.bundle"
SCAFFOLDER_PATH = (
    REPO_ROOT
    / "unity/RogueCommandAssetAuthoring/Assets/Editor/RogueCommandVisualSampleScaffolder.cs"
)
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
# Units with wheeled/tracked locomotion use Action_Drive; legged walkers use Action_Walk
MOVEMENT_ACTIONS = {
    "ArachnidHexapod": "ArachnidHexapod_Armature|ArachnidHexapod_Action_Walk",
    "OverlordCommandHQ": "OverlordCommandHQ_Armature|OverlordCommandHQ_Action_Drive",
    "AtlasCraneMech": "AtlasCraneMech_Armature|AtlasCraneMech_Action_Walk",
    "BastionSiegeMech": "BastionSiegeMech_Armature|BastionSiegeMech_Action_Walk",
    "ColossusSiegePlatform": "ColossusSiegePlatform_Armature|ColossusSiegePlatform_Action_Drive",
    "IroncladWalker": "IroncladWalker_Armature|IroncladWalker_Action_Walk",
    "SandlanceScout": "SandlanceScout_Armature|SandlanceScout_Action_Drive",
    "SkirmishStrikeBike": "SkirmishStrikeBike_Armature|SkirmishStrikeBike_Action_Drive",
    "SurgeArcTank": "SurgeArcTank_Armature|SurgeArcTank_Action_Drive",
}
IDLE_ACTIONS = {
    unit_name: f"{unit_name}_Armature|{unit_name}_Action_Idle"
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

    def test_visual_only_rigged_prefabs_rebuild_their_animator_graph(self) -> None:
        """Visual-only rigged units must use the same body-driven Animator builder."""
        text = SCAFFOLDER_PATH.read_text(encoding="utf-8")
        self.assertIn("RefreshRiggedPbrPrefabAnimator", text)
        self.assertIn("ArachnidHexapodVisualPrefabPath", text)
        self.assertIn("OverlordCommandHQVisualPrefabPath", text)

    def test_walker_visual_prefabs_disable_root_motion(self) -> None:
        prefab_paths = {
            "ArachnidHexapod": REPO_ROOT
            / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/VisualOnly/Prefabs/ArachnidHexapodVisual.prefab",
            "IroncladWalker": REPO_ROOT
            / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/IroncladWalkerVisual.prefab",
        }

        for unit_name, prefab_path in prefab_paths.items():
            with self.subTest(unit=unit_name):
                text = prefab_path.read_text(encoding="utf-8")
                self.assertRegex(
                    text,
                    r"(?m)^  m_ApplyRootMotion: 0$",
                    f"{unit_name} uses EntityFlockingMovement and must keep its visual Animator in-place",
                )

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

    def test_each_fire_recoil_clip_has_serialized_dynamic_curve_payload(self) -> None:
        fire_clips = {
            obj.read().m_Name: obj.read()
            for obj in self.objects
            if obj.type.name == "AnimationClip" and "FireRecoil" in obj.read().m_Name
        }
        for unit_name in VISUAL_UNITS:
            expected_action = f"{unit_name}_Armature|{unit_name}_Action_FireRecoil"
            with self.subTest(unit=unit_name):
                self.assertIn(expected_action, fire_clips)
                clip = fire_clips[expected_action]
                payload = clip.m_MuscleClip.m_Clip.data
                dynamic_curve_count = (
                    (payload.m_StreamedClip.curveCount or 0)
                    + (payload.m_DenseClip.m_CurveCount or 0)
                )
                self.assertGreater(
                    dynamic_curve_count,
                    0,
                    f"{unit_name} FireRecoil must serialize changing curve samples, not constants only",
                )
                self.assertGreaterEqual(
                    len(clip.m_ClipBindingConstant.genericBindings),
                    4,
                    f"{unit_name} FireRecoil must bind body and weapon transform channels",
                )

    def test_each_visual_fbx_imports_all_locomotion_clips_as_loops(self) -> None:
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
                locomotion_clips = {}
                for block in clip_blocks:
                    name_match = re.search(r"(?m)^      name: (.*)$", block)
                    loop_match = re.search(r"(?m)^      loopTime: ([01])$", block)
                    if name_match is None or loop_match is None:
                        continue
                    clip_name = name_match.group(1)
                    if clip_name in {"Idle", "Walk", "Drive"} or clip_name.rsplit("|", 1)[-1].endswith(
                        ("_Action_Idle", "_Action_Walk", "_Action_Drive")
                    ):
                        locomotion_clips[clip_name] = loop_match.group(1)
                self.assertTrue(
                    locomotion_clips,
                    f"{unit_name} must persist rig-bound Idle, Walk, or Drive clip import settings",
                )
                self.assertTrue(
                    all(loop_time == "1" for loop_time in locomotion_clips.values()),
                    f"{unit_name} rig-bound locomotion clips must all loop: {locomotion_clips}",
                )

    def test_surge_card_preview_scale_fits_selection_card(self) -> None:
        manifest_path = REPO_ROOT / "content/packs/sample-native-expansion/content.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        surge_tank = next(unit for unit in manifest["units"] if unit["id"] == "sample-native:surge-tank")

        self.assertLessEqual(
            surge_tank["cardModelScalingFactor"],
            0.5,
            "The Surge Arc Tank preview must be scaled down so it does not obscure neighboring selection cards.",
        )

    def test_imported_fire_and_death_actions_do_not_loop(self) -> None:
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
                action_loops = {}
                for block in clip_blocks:
                    name_match = re.search(r"(?m)^      name: (.*)$", block)
                    loop_match = re.search(r"(?m)^      loopTime: ([01])$", block)
                    if name_match is None or loop_match is None:
                        continue
                    clip_name = name_match.group(1)
                    if re.search(r"(?:Action_)?(?:FireRecoil|Death)$", clip_name):
                        action_loops[clip_name] = loop_match.group(1)
                self.assertTrue(
                    action_loops,
                    f"{unit_name} must persist rig-bound FireRecoil and Death clip import settings",
                )
                self.assertTrue(
                    all(loop_time == "0" for loop_time in action_loops.values()),
                    f"{unit_name} fire/death action clips must not loop: {action_loops}",
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
