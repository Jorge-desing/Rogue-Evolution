"""Integration contract for the Tactical Operations articulated walker bundle."""

from __future__ import annotations

import unittest
from pathlib import Path
from typing import Any

import UnityPy


REPO_ROOT = Path(__file__).resolve().parents[3]
BUNDLE_PATH = REPO_ROOT / "content/packs/tactical-operations/bundles/tactical-operations.bundle"
VISUAL_ROOT_NAME = "ArcLanceWalkerVisual"
REQUIRED_CLIPS = ("Action_Idle", "Action_Walk", "Action_FireRecoil", "Action_Death")
REQUIRED_SOCKETS = (
    "Socket_MuzzlePrimary",
    "Socket_DamageSmoke",
    "Socket_DamageFire",
    "Socket_FootContact_L",
    "Socket_FootContact_R",
)


def _read(pointer: Any) -> Any:
    return pointer.read() if hasattr(pointer, "read") else pointer


def _collect_visual_hierarchy(root_game_object: Any) -> tuple[list[str], list[str]]:
    names: list[str] = []
    component_types: list[str] = []

    def visit(game_object: Any) -> None:
        names.append(game_object.m_Name)
        component_types.extend(
            type(_read(component.component)).__name__ for component in game_object.m_Component
        )
        for component_pair in game_object.m_Component:
            component = _read(component_pair.component)
            if type(component).__name__ != "Transform":
                continue
            for child_pointer in component.m_Children:
                child_transform = _read(child_pointer)
                visit(_read(child_transform.m_GameObject))

    visit(root_game_object)
    return names, component_types


class TacticalWalkerRiggingContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if not BUNDLE_PATH.is_file():
            raise unittest.SkipTest(f"Tactical Operations bundle is missing: {BUNDLE_PATH}")
        cls.env = UnityPy.load(str(BUNDLE_PATH))
        cls.objects = list(cls.env.objects)
        cls.game_objects = {
            obj.path_id: obj.read()
            for obj in cls.objects
            if obj.type.name == "GameObject"
        }
        cls.visual_root = next(
            (game_object for game_object in cls.game_objects.values()
             if game_object.m_Name == VISUAL_ROOT_NAME),
            None,
        )

    def test_arc_lance_visual_is_a_rigged_animated_walker(self) -> None:
        self.assertIsNotNone(self.visual_root, "ArcLanceWalkerVisual must be present in the bundle")
        names, component_types = _collect_visual_hierarchy(self.visual_root)

        self.assertIn("SkinnedMeshRenderer", component_types)
        self.assertEqual(component_types.count("Animator"), 1)
        self.assertIn("Bone_Root", names)
        for socket_name in REQUIRED_SOCKETS:
            with self.subTest(name=socket_name):
                self.assertEqual(names.count(socket_name), 1, f"Expected one unambiguous {socket_name}")
        for clip_name in REQUIRED_CLIPS:
            with self.subTest(clip=clip_name):
                self.assertTrue(
                    any(
                        obj.type.name == "AnimationClip"
                        and clip_name.lower() in getattr(obj.read(), "m_Name", "").lower()
                        for obj in self.objects
                    ),
                    f"AssetBundle must contain animation clip {clip_name}",
                )


if __name__ == "__main__":
    unittest.main()
