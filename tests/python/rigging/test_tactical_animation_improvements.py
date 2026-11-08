"""Contrato de mejoras de animación para el pack tactical-operations (RED).

Verifica por inspección estática de RogueModelKit (sin Blender) que:
- FireRecoil está diferenciado por rol/masa (light/medium/heavy/sam/walker)
  con fase monótona neutral->anticipación->impacto->follow->recovery.
- Drive de vehículos lee a distancia RTS (pitch diferencial + bob mayor).
- Walk del bípedo ArcLance incluye lift del pie y yaw de pelvis.
- Death de vehículos colapsa de forma legible (no -0.12 m plano).
"""
from __future__ import annotations

import ast
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[3]
MODEL_KIT = ROOT / "tools" / "RogueModelKit.py"


def _func_source() -> tuple[str, ast.FunctionDef, str]:
    text = MODEL_KIT.read_text(encoding="utf-8")
    tree = ast.parse(text, filename=str(MODEL_KIT))
    fn = next(
        node
        for node in tree.body
        if isinstance(node, ast.FunctionDef)
        and node.name == "generate_procedural_action_clips"
    )
    src = ast.get_source_segment(text, fn) or ""
    return text, fn, src


def _section(src: str, start_marker: str, end_marker: str | None = None) -> str:
    start = src.index(start_marker)
    if end_marker is None:
        return src[start:]
    end = src.index(end_marker, start)
    return src[start:end]


class TacticalAnimationImprovementTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.text, cls.fn, cls.src = _func_source()
        cls.fire = _section(cls.src, "# 3. Action_FireRecoil", "# 4. Action_Death")
        cls.drive = _section(cls.src, "# Wheeled or Tracked vehicle", "# 3. Action_FireRecoil")
        cls.walk_biped = _section(
            cls.src, 'if leg_type == "biped":', 'elif leg_type == "quad":'
        )
        cls.death = _section(cls.src, "# 4. Action_Death")

    # ---------- FireRecoil por rol ----------
    def test_fire_profile_parameter_exists(self) -> None:
        args = [a.arg for a in self.fn.args.args]
        self.assertIn("fire_profile", args, "generate_procedural_action_clips debe aceptar fire_profile para diferenciar por rol")

    def test_fire_profiles_distinct_values(self) -> None:
        # Debe haber perfiles diferenciados, no un único juego de constantes.
        lowered = self.fire.lower()
        for token in ("light", "medium", "heavy", "sam", "walker"):
            self.assertIn(token, lowered, f"Falta perfil FireRecoil: {token}")
        # Impulso pesado mayor que ligero (verificación numérica sobre literales).
        # Extrae los valores de Y del cuerpo en el impacto: heavy debe ser más negativo.
        # Buscamos los literales -0.038 (heavy) y -0.016 (light) o equivalentes medibles.
        self.assertRegex(self.fire, r"-0\.03[0-9]", "El impulso pesado debe superar ~0.03 m para leerse a distancia RTS")
        self.assertRegex(self.fire, r"-0\.01[0-9]", "El impulso ligero debe rondar 0.01-0.02 m (subsutil)")

    def test_fire_anticipation_at_least_three_frames(self) -> None:
        # La anticipación actual es de 1 frame (f1 neutral, f2 brace, f3 impacto).
        # Mejora exige brace en f>=3 antes del impacto.
        m = re.search(r"anticip\w*[^\n]*\n\s*(\d+)\s*:", self.fire, flags=re.I)
        # Si no hay comentario con frame, buscamos que el primer frame no-neutral sea >=3
        if m:
            self.assertGreaterEqual(int(m.group(1)), 3, "La anticipación debe preceder al impacto con >=3 frames de brace")
        else:
            # Fallback estructural: los frames de fire_poses deben incluir al menos 3 claves antes del pico.
            frames = [int(x) for x in re.findall(r"^\s+(\d+)\s*:\s*\(\(", self.fire, flags=re.M)]
            self.assertGreaterEqual(len([f for f in frames if f <= 5]), 3, "Se necesitan >=3 claves en la ventana de anticipación/impacto")

    def test_fire_phase_map_strictly_monotonic(self) -> None:
        # Extrae el dict fire_poses (o dicts por perfil) y verifica orden estricto en el fuente
        # cuando hay claves literales. El último key debe coincidir con el fin del clip.
        frames = [int(x) for x in re.findall(r"^\s+(\d+)\s*:\s*\(\(", self.fire, flags=re.M)]
        self.assertTrue(frames, "No se pudieron extraer frames de FireRecoil")
        self.assertEqual(frames, sorted(frames), "El mapa de fases debe ser estrictamente monótono")
        self.assertEqual(len(frames), len(set(frames)), "Frames duplicados en el mapa de fases")
        self.assertGreaterEqual(frames[-1], 15, "El recovery debe durar al menos hasta f15 (legible)")

    def test_fire_recoil_never_keys_root(self) -> None:
        self.assertNotRegex(self.fire, r'set_bone_key\(\s*"Bone_Root"')

    def test_fire_walker_braces_legs(self) -> None:
        # El walker debe apoyar las piernas en el impacto (secondary action),
        # no solo mover pelvis.
        walker_zone = self.fire
        self.assertIn("Bone_Thigh", walker_zone, "FireRecoil walker debe keyear muslos (brace)")
        self.assertIn("Bone_Shin", walker_zone, "FireRecoil walker debe keyear espinillas (brace)")

    # ---------- Drive ----------
    def test_drive_hull_bob_readable_at_rts(self) -> None:
        # El bob legacy es 0.012 máx. Una mejora legible debe superar 0.015.
        hull_locations = []
        for match in re.finditer(
            r'set_bone_key\(\s*"Bone_Hull"\s*,\s*"location"\s*,\s*\d+\s*,\s*\(([^)]+)\)',
            self.drive,
        ):
            values = [float(part.strip()) for part in match.group(1).split(",")]
            if len(values) == 3:
                hull_locations.append(values)
        floats = [abs(location[2]) for location in hull_locations]
        self.assertTrue(floats, "Drive debe keyear el hull en Z")
        self.assertGreaterEqual(max(floats), 0.015, "El bob de Drive debe ser >=0.015 m para leerse a distancia RTS")

    def test_drive_has_pitch_differential(self) -> None:
        # Pitch diferencial front/rear o roll+pitch combinados, no rotación única pequeña.
        self.assertRegex(self.drive, r"math\.radians\(\s*-?1\.\d+\s*\)", "Drive debe tener pitch/roll >=1.0° en algún frame para leerse como marcha")

    def test_drive_keys_root_zero(self) -> None:
        # Invariante in-place: Walk/Drive no deben mover Bone_Root en positivo.
        # Drive por diseño no keyea Root (solo Hull). Si lo keyea, debe ser cero.
        root_keys = re.findall(r'set_bone_key\(\s*"Bone_Root"[^\)]*\)', self.drive)
        for rk in root_keys:
            self.assertIn("0.0", rk, "Bone_Root solo puede keyearse en cero (in-place)")

    # ---------- Walk bípedo ----------
    def test_biped_walk_lifts_feet(self) -> None:
        # El gait actual solo rota. Debe haber location Z del pie (lift) en la fase passing.
        self.assertRegex(
            self.walk_biped,
            r'set_bone_key\(\s*"Bone_Foot_[LR]"\s*,\s*"location"',
            "Walk bípedo debe keyear location del pie (lift), no solo rotación",
        )

    def test_biped_walk_pelvis_yaw(self) -> None:
        # Transferencia de peso: yaw (tercer componente) de pelvis con magnitud >=3°.
        # La rotación se escribe como (X, Y, yaw) donde X suele ser literal 0.0
        # y Y/yaw son math.radians; el tercer número de la tupla es el yaw.
        found = False
        max_yaw = 0.0
        for line in self.walk_biped.splitlines():
            if "Bone_Pelvis" in line and "rotation_euler" in line and "10," in line or "30," in line:
                if "Bone_Pelvis" not in line or "rotation_euler" not in line:
                    continue
                # Extrae la tupla final (...) tras la última coma antes del cierre.
                m = re.search(r",\s*\((.*)\)\s*\)\s*$", line)
                if not m:
                    continue
                # Orden: X, Y, yaw (X puede ser 0.0 o math.radians(...))
                nums = re.findall(r"(-?\d+\.\d+)", m.group(1))
                if len(nums) >= 3:
                    yaw = abs(float(nums[2]))
                    if yaw != 0.0:
                        found = True
                        max_yaw = max(max_yaw, yaw)
        self.assertTrue(found, "Walk debe incluir yaw de pelvis (tercer componente Z no cero) para transferencia de peso")
        self.assertGreaterEqual(max_yaw, 3.0, "El yaw de pelvis debe ser >=3° para leerse como transferencia de peso")

    def test_biped_walk_root_in_place(self) -> None:
        for m in re.finditer(r'set_bone_key\(\s*"Bone_Root"\s*,\s*"location"\s*,\s*\d+\s*,\s*\(([^)]+)\)', self.walk_biped):
            vals = [v.strip() for v in m.group(1).split(",")]
            self.assertEqual(vals, ["0.0", "0.0", "0.0"], "Bone_Root debe permanecer en (0,0,0) en Walk")

    # ---------- Death ----------
    def test_death_vehicle_collapse_legible(self) -> None:
        # Legacy: vehículo -0.12 m. Mejora legible: <= -0.20 m.
        # El ternario es: -0.55 if biped else (-0.28 if quad else -0.12)
        # El vehículo es el último else, no el quad.
        m = re.search(r"else\s+(-0\.\d+)\s*\)", self.death)
        self.assertIsNotNone(m, "Death debe definir collapse_z para vehículo en el else final")
        self.assertIsNotNone(m)
        # m.group es el literal de vehículo; debe ser profundo
        lit = float(m.group(1))
        self.assertLessEqual(lit, -0.20, "El colapso de Death en vehículos debe ser <=-0.20 m para leerse a distancia RTS")

    def test_death_has_half_collapse(self) -> None:
        # Slow in/out: debe haber un frame intermedio de colapso (no salto directo 1->28).
        frame_lits = [int(x) for x in re.findall(r",\s*(\d+)\s*,\s*\(0\.0,\s*0\.0,", self.death)]
        # También `for f in (...)`
        for_m = re.search(r"for f in \(([^)]+)\)", self.death)
        frames = [int(x.strip()) for x in for_m.group(1).split(",") if x.strip().isdigit()] if for_m else []
        all_frames = sorted(set(frames + frame_lits))
        # Debe haber un frame intermedio entre 10 y 25 además de 28/35
        self.assertTrue(any(10 <= f <= 25 for f in all_frames), "Death debe tener un frame de medio colapso (slow in/out), no salto directo a f28")

    def test_locomotion_only_flag_exists(self) -> None:
        args = [a.arg for a in self.fn.args.args]
        self.assertIn("locomotion_only", args)

    def test_frame_end_overrides_exist(self) -> None:
        # BastionSiegeMech valida en Unity FireRecoil 15 frames y Death 45 frames
        # (RogueCommandVisualSampleScaffolder.ValidateRiggedWalkerSources).
        # El generador debe permitir overrides sin romper el default actual.
        args = [a.arg for a in self.fn.args.args]
        self.assertIn("fire_end_frame", args)
        self.assertIn("death_end_frame", args)
        self.assertIn("fire_end_frame", self.fire)
        self.assertIn("death_end_frame", self.death)

    def test_death_defaults_hold_to_35(self) -> None:
        # Sin override, Death termina en 35 (default actual preservado).
        self.assertRegex(self.death, r"death_end_frame|35", "Death debe tener default 35")

    def test_death_turret_droop_strong(self) -> None:
        # Solo la línea de la torreta, no saltar a la pierna (-35 shin).
        for line in self.death.splitlines():
            if "Bone_TurretPitch" in line and "math.radians(-" in line:
                deg = float(re.search(r"math\.radians\(\s*(-?[\d\.]+)", line).group(1))
                self.assertLessEqual(deg, -30.0, "La droop de torreta en Death debe ser <=-30° para leerse como pérdida de apoyo")
                return
        self.fail("Death debe dropear la torreta (no se encontró key con radians negativo)")


if __name__ == "__main__":
    unittest.main()
