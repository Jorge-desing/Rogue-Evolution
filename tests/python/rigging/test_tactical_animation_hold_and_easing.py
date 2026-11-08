"""Contrato de suavizado adicional para clips tácticos (RED).

Verifica por inspección estática de RogueModelKit que:
- ``Action_Idle`` usa una envolvente de entrada/salida suave (frames 8 y 22)
  además del pico central, sin mover ``Bone_Root``.
- ``Action_Death`` comienza con un asentamiento temprano al ~12% antes del
  semicollapse de f16, de modo que el colapso entra lento y no salta.
"""

from __future__ import annotations

import ast
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[3]
MODEL_KIT = ROOT / "tools" / "RogueModelKit.py"


def _func_source() -> str:
    text = MODEL_KIT.read_text(encoding="utf-8")
    tree = ast.parse(text, filename=str(MODEL_KIT))
    fn = next(
        node
        for node in tree.body
        if isinstance(node, ast.FunctionDef)
        and node.name == "generate_procedural_action_clips"
    )
    return ast.get_source_segment(text, fn) or ""


def _section(src: str, start_marker: str, end_marker: str | None = None) -> str:
    start = src.index(start_marker)
    if end_marker is None:
        return src[start:]
    end = src.index(end_marker, start)
    return src[start:end]


class TacticalAnimationEasingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.src = _func_source()
        cls.idle = _section(cls.src, "# 1. Action_Idle", "# 2. Locomotion")
        cls.death = _section(cls.src, "# 4. Action_Death")

    def test_idle_has_slow_in_and_slow_out_shoulder_keys(self) -> None:
        for frame in (8, 22):
            self.assertRegex(
                self.idle,
                rf'set_bone_key\(base_body,\s*"location",\s*{frame},',
                f"Idle necesita un key de hombro en f{frame} para slow in/out",
            )

    def test_idle_never_moves_root(self) -> None:
        self.assertNotRegex(self.idle, r'set_bone_key\(\s*"Bone_Root"')

    def test_death_has_early_settle_before_half_collapse(self) -> None:
        self.assertRegex(self.death, r"for f in \(8,\s*16,\s*28,")
        self.assertIn("0.12", self.death)


if __name__ == "__main__":
    unittest.main()
