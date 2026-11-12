"""Regression tests for the project VFX skill's bundled validator copy."""

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[3]
VALIDATOR_PATH = (
    REPO_ROOT
    / ".agents"
    / "skills"
    / "rogue-command-vfx-authoring"
    / "scripts"
    / "validate_vfx_assets.py"
)
SPEC = importlib.util.spec_from_file_location("rogue_command_skill_vfx_validator", VALIDATOR_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Could not load VFX validator copy from {VALIDATOR_PATH}")
VALIDATOR = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = VALIDATOR
SPEC.loader.exec_module(VALIDATOR)


class Pointer:
    def __init__(self, value: Any) -> None:
        self.value = value

    def read(self) -> Any:
        return self.value


class Transform:
    def __init__(self, name: str, father: Any = None) -> None:
        self.m_GameObject = Pointer(SimpleNamespace(m_Name=name))
        self.m_Father = father
        self.m_Children: list[Any] = []


class SkillVfxValidatorCopyTests(unittest.TestCase):
    def test_skill_validator_copy_matches_tool_entrypoint(self) -> None:
        tool_path = REPO_ROOT / "tools" / "validate_vfx_assets.py"

        self.assertEqual(VALIDATOR_PATH.read_text(encoding="utf-8"), tool_path.read_text(encoding="utf-8"))

    def _game_object(self, transform: Transform) -> Any:
        component = SimpleNamespace(component=Pointer(transform))
        return SimpleNamespace(m_Component=[component])

    def test_root_game_object_is_included(self) -> None:
        root = Transform("UnitVisual")

        self.assertTrue(VALIDATOR.VfxAssetValidator._is_root_game_object(self._game_object(root)))

    def test_nested_visual_named_game_object_is_excluded(self) -> None:
        nested = Transform("NestedVisual", father=SimpleNamespace(path_id=42))

        self.assertFalse(VALIDATOR.VfxAssetValidator._is_root_game_object(self._game_object(nested)))

    def test_root_game_object_accepts_direct_transform_component(self) -> None:
        root = Transform("UnitVisual")
        game_object = SimpleNamespace(m_Component=[SimpleNamespace(component=root)])

        self.assertTrue(VALIDATOR.VfxAssetValidator._is_root_game_object(game_object))

    def test_child_walk_accepts_direct_transform_children(self) -> None:
        root = Transform("UnitVisual")
        child = Transform("Socket_DamageSmoke", father=SimpleNamespace(path_id=1))
        root.m_Children.append(child)
        names: set[str] = set()

        VALIDATOR.VfxAssetValidator()._collect_child_names(root, names)

        self.assertEqual(names, {"UnitVisual", "Socket_DamageSmoke"})


if __name__ == "__main__":
    unittest.main()
