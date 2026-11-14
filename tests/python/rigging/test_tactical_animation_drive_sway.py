"""Contrato de locomoción más densa para el pack tactical-operations.

Verifica por inspección estática de RogueModelKit (sin Blender) que:
- ``Action_Drive`` de vehículos incluye un sway lateral real, no solo bob Z.
- El pico no-hover llega a 0,025 m y la depresión no baja de -0,012 m.
- ``Action_Walk`` bípedo declara claves intermedias f5/f15/f25/f35 para que
  la fase de contacto/paso no dependa solo de interpolación entre 10 frames.
"""

from __future__ import annotations

import ast
import re
import unittest

from tests.python.helpers import TOOLS_DIR, parse_ast_cached, read_text_cached


MODEL_KIT = TOOLS_DIR / "RogueModelKit.py"


def _func_source() -> str:
    text = read_text_cached(MODEL_KIT)
    tree = parse_ast_cached(MODEL_KIT)
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


def _hull_location_tuples(drive: str) -> list[tuple[float, float, float]]:
    tuples: list[tuple[float, float, float]] = []
    for match in re.finditer(
        r'set_bone_key\(\s*"Bone_Hull"\s*,\s*"location"\s*,\s*\d+\s*,\s*\(([^)]+)\)',
        drive,
    ):
        values = [float(part.strip()) for part in match.group(1).split(",")]
        if len(values) == 3:
            tuples.append((values[0], values[1], values[2]))
    return tuples


class TacticalAnimationDriveSwayTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.src = _func_source()
        cls.drive = _section(cls.src, "# Wheeled or Tracked vehicle", "# 3. Action_FireRecoil")

    def test_drive_has_lateral_hull_sway(self) -> None:
        tuples = _hull_location_tuples(self.drive)
        self.assertTrue(tuples, "Drive debe keyear Bone_Hull en location")
        x_values = [abs(location[0]) for location in tuples]
        self.assertGreaterEqual(
            max(x_values),
            0.008,
            "Action_Drive debe incluir sway lateral >=0,008 m para leerse como marcha a distancia RTS",
        )

    def test_drive_peak_and_trough_read_at_rts_distance(self) -> None:
        z_values = [abs(location[2]) for location in _hull_location_tuples(self.drive)]
        self.assertGreaterEqual(max(z_values), 0.025)
        z_signed = [location[2] for location in _hull_location_tuples(self.drive)]
        self.assertGreaterEqual(
            min(z_signed),
            -0.012,
            "La depresión del Drive no debe bajar el hull más de 0,012 m para evitar clipping con el terreno",
        )


class TacticalAnimationBipedDensityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.src = _func_source()
        cls.walk_biped = _section(cls.src, 'if leg_type == "biped":', 'elif leg_type == "quad":')

    def _gait_frames(self, variable: str) -> set[int]:
        section = _section(self.walk_biped, f"{variable} = {{", "        }")
        return {int(match.group(1)) for match in re.finditer(r"^\s+(\d+):", section, flags=re.M)}

    def test_left_gait_has_contact_phase_intermediates(self) -> None:
        frames = self._gait_frames("biped_gait_left")
        self.assertTrue({5, 15, 25, 35}.issubset(frames))

    def test_right_gait_has_contact_phase_intermediates(self) -> None:
        frames = self._gait_frames("biped_gait_right")
        self.assertTrue({5, 15, 25, 35}.issubset(frames))


if __name__ == "__main__":
    unittest.main()
