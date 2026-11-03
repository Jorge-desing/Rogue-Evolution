"""Tests verifying byte-for-byte twin script parity and Wave 7 catalog invariants for RogueModelKit and generate_unit_model."""

import ast
from pathlib import Path
import unittest

REPO_ROOT = Path(__file__).resolve().parents[3]
TOOLS_RMK = REPO_ROOT / "tools" / "RogueModelKit.py"
SKILL_RMK = REPO_ROOT / ".agents" / "skills" / "rogue-command-3d-model-authoring" / "scripts" / "RogueModelKit.py"

TOOLS_GUM = REPO_ROOT / "tools" / "generate_unit_model.py"
SKILL_GUM = REPO_ROOT / ".agents" / "skills" / "rogue-command-3d-model-authoring" / "scripts" / "generate_unit_model.py"


class TestModelKitSkillCopy(unittest.TestCase):
    """Verifies that the canonical authoring tools match their skill copies exactly."""

    def test_rogue_model_kit_byte_parity(self) -> None:
        """RogueModelKit.py in tools/ and in skill scripts/ must be byte-for-byte identical."""
        self.assertTrue(TOOLS_RMK.is_file(), f"Missing {TOOLS_RMK}")
        self.assertTrue(SKILL_RMK.is_file(), f"Missing {SKILL_RMK}")
        tools_bytes = TOOLS_RMK.read_bytes()
        skill_bytes = SKILL_RMK.read_bytes()
        self.assertEqual(
            len(tools_bytes),
            len(skill_bytes),
            f"File sizes differ: tools={len(tools_bytes)} vs skill={len(skill_bytes)}",
        )
        self.assertEqual(tools_bytes, skill_bytes, "RogueModelKit.py differs from skill twin copy.")

    def test_generate_unit_model_byte_parity(self) -> None:
        """generate_unit_model.py in tools/ and in skill scripts/ must be byte-for-byte identical."""
        self.assertTrue(TOOLS_GUM.is_file(), f"Missing {TOOLS_GUM}")
        self.assertTrue(SKILL_GUM.is_file(), f"Missing {SKILL_GUM}")
        tools_bytes = TOOLS_GUM.read_bytes()
        skill_bytes = SKILL_GUM.read_bytes()
        self.assertEqual(
            len(tools_bytes),
            len(skill_bytes),
            f"File sizes differ: tools={len(tools_bytes)} vs skill={len(skill_bytes)}",
        )
        self.assertEqual(tools_bytes, skill_bytes, "generate_unit_model.py differs from skill twin copy.")

    def test_wave7_archetypes_and_defaults(self) -> None:
        """generate_unit_model.py must register all 42 mobile vehicle archetypes without duplicates."""
        gum_text = TOOLS_GUM.read_text(encoding="utf-8")
        parsed = ast.parse(gum_text, filename=str(TOOLS_GUM))

        all_archetypes = None
        default_weapons = None
        presets = None

        for stmt in parsed.body:
            if isinstance(stmt, ast.Assign):
                for target in stmt.targets:
                    if isinstance(target, ast.Name):
                        if target.id == "ALL_ARCHETYPES" and isinstance(stmt.value, ast.List):
                            all_archetypes = [elt.value for elt in stmt.value.elts if isinstance(elt, ast.Constant)]
                        elif target.id == "ARCHETYPE_DEFAULT_WEAPONS" and isinstance(stmt.value, ast.Dict):
                            default_weapons = {
                                k.value: v.value
                                for k, v in zip(stmt.value.keys, stmt.value.values)
                                if isinstance(k, ast.Constant) and isinstance(v, ast.Constant)
                            }
                        elif target.id == "PRESETS" and isinstance(stmt.value, ast.Dict):
                            presets = {
                                k.value: v
                                for k, v in zip(stmt.value.keys, stmt.value.values)
                                if isinstance(k, ast.Constant)
                            }

        self.assertIsNotNone(all_archetypes, "ALL_ARCHETYPES list not found in generate_unit_model.py")
        self.assertIsNotNone(default_weapons, "ARCHETYPE_DEFAULT_WEAPONS dict not found in generate_unit_model.py")
        self.assertIsNotNone(presets, "PRESETS dict not found in generate_unit_model.py")

        self.assertEqual(len(all_archetypes), 42, f"Expected 42 archetypes, got {len(all_archetypes)}")
        self.assertEqual(len(set(all_archetypes)), 42, "ALL_ARCHETYPES contains duplicate entries")

        wave7_archetypes = [
            "nanite_support_rover",
            "gravity_siege_breaker",
            "tactical_relay_carrier",
            "amphibious_assault_tank",
            "plasma_mortar_strider",
            "stealth_recon_skimmer",
        ]
        for arch in wave7_archetypes:
            self.assertIn(arch, all_archetypes, f"Wave 7 archetype {arch} missing from ALL_ARCHETYPES")
            self.assertIn(arch, default_weapons, f"Wave 7 archetype {arch} missing from ARCHETYPE_DEFAULT_WEAPONS")

        self.assertEqual(len(presets), 56, f"Expected 56 presets, got {len(presets)}")
        wave7_presets = [
            "sentinel_relay",
            "gravitas_breaker",
            "command_relay",
            "coastal_dreadnought",
            "plasma_strider",
            "phantom_skimmer",
            "emerald_warden",
            "solar_colossus",
            "void_spectre",
        ]
        for preset in wave7_presets:
            self.assertIn(preset, presets, f"Wave 7 preset {preset} missing from PRESETS")

    def test_wave7_assemblies_and_builder_methods(self) -> None:
        """RogueModelKit.py must contain all 6 Wave 7 procedural assemblies and builder chaining methods."""
        rmk_text = TOOLS_RMK.read_text(encoding="utf-8")
        parsed = ast.parse(rmk_text, filename=str(TOOLS_RMK))

        func_names = set()
        vehicle_builder_methods = set()
        structure_builder_methods = set()

        for stmt in parsed.body:
            if isinstance(stmt, ast.FunctionDef):
                func_names.add(stmt.name)
            elif isinstance(stmt, ast.ClassDef):
                if stmt.name == "RogueVehicleBuilder":
                    for item in stmt.body:
                        if isinstance(item, ast.FunctionDef):
                            vehicle_builder_methods.add(item.name)
                elif stmt.name == "RogueStructureBuilder":
                    for item in stmt.body:
                        if isinstance(item, ast.FunctionDef):
                            structure_builder_methods.add(item.name)

        required_assemblies = [
            "make_nanite_projector_assembly",
            "make_gravity_cannon_assembly",
            "make_nanite_repair_dispenser_assembly",
            "make_grav_dampener_hub_assembly",
            "make_comms_ew_mast_assembly",
            "make_reinforced_ram_prow_assembly",
            "normalize_arachnid_middle_leg_orientation",
        ]
        for assembly in required_assemblies:
            self.assertIn(assembly, func_names, f"Required assembly {assembly} missing from RogueModelKit.py")

        required_vehicle_methods = [
            "add_nanite_projector",
            "add_gravity_cannon",
            "add_nanite_dispenser",
            "add_grav_dampener",
            "add_comms_mast",
            "add_ram_prow",
        ]
        for method in required_vehicle_methods:
            self.assertIn(method, vehicle_builder_methods, f"Method {method} missing from RogueVehicleBuilder")

        required_structure_methods = [
            "add_nanite_dispenser",
            "add_grav_dampener",
            "add_comms_mast",
            "add_ram_prow",
        ]
        for method in required_structure_methods:
            self.assertIn(method, structure_builder_methods, f"Method {method} missing from RogueStructureBuilder")

    def test_tactical_operations_builders_select_wave7_presets(self) -> None:
        """The three new unit builders must consume the Wave 7 preset improvements."""
        expected = {
            "Build-SentinelRelayModel.py": "sentinel_relay",
            "Build-GravitasBreakerModel.py": "gravitas_breaker",
            "Build-CommandRelayModel.py": "command_relay",
        }
        builders_dir = REPO_ROOT / "tools"
        for filename, preset in expected.items():
            with self.subTest(builder=filename):
                source = (builders_dir / filename).read_text(encoding="utf-8")
                self.assertIn(f'"--preset", "{preset}"', source)
                self.assertNotIn('"--archetype"', source)
                self.assertNotIn('"--weapon"', source)

    def test_nanite_support_rover_wheel_lugs_clear_ground_plane(self) -> None:
        """Wave 7 support-rover wheels leave margin for the generated tread lugs."""
        generator_source = TOOLS_GUM.read_text(encoding="utf-8")
        self.assertIn(
            "wheel_z = builder.ground_clearance + 0.43 + 0.035",
            generator_source,
        )

    def test_hard_surface_realism_assemblies_and_methods(self) -> None:
        """RogueModelKit.py must contain the anti-primitive hard-surface realism assemblies and builder chaining methods."""
        rmk_text = TOOLS_RMK.read_text(encoding="utf-8")
        parsed = ast.parse(rmk_text, filename=str(TOOLS_RMK))

        func_names = set()
        vehicle_builder_methods = set()

        for stmt in parsed.body:
            if isinstance(stmt, ast.FunctionDef):
                func_names.add(stmt.name)
            elif isinstance(stmt, ast.ClassDef) and stmt.name == "RogueVehicleBuilder":
                for item in stmt.body:
                    if isinstance(item, ast.FunctionDef):
                        vehicle_builder_methods.add(item.name)

        required_realism_assemblies = [
            "make_cast_pelvis_assembly",
            "make_articulated_mech_torso",
            "make_sculpted_wheel_fender_assembly",
            "make_transitional_coaming_collar",
            "make_aerodynamic_fuselage_assembly",
            "make_towing_shackle_assembly",
            "make_crane_lifting_lugs",
        ]
        for assembly in required_realism_assemblies:
            self.assertIn(assembly, func_names, f"Realism assembly {assembly} missing from RogueModelKit.py")

        required_builder_methods = [
            "add_cast_pelvis",
            "add_mech_torso",
            "add_wheel_fenders",
            "add_transitional_coaming",
            "add_aerodynamic_fuselage",
            "add_towing_shackles",
            "add_lifting_lugs",
        ]
        for method in required_builder_methods:
            self.assertIn(method, vehicle_builder_methods, f"Method {method} missing from RogueVehicleBuilder")

    def test_archetypes_use_hard_surface_realism_assemblies(self) -> None:
        """Archetypes must consume the new anti-primitive hard-surface realism assemblies."""
        generator_source = TOOLS_GUM.read_text(encoding="utf-8")
        self.assertGreaterEqual(generator_source.count("builder.add_cast_pelvis"), 4)
        self.assertGreaterEqual(generator_source.count("builder.add_mech_torso"), 4)
        self.assertGreaterEqual(generator_source.count("builder.add_wheel_fenders"), 3)
        self.assertGreaterEqual(generator_source.count("with_fenders=True"), 4)
        self.assertGreaterEqual(generator_source.count("make_transitional_coaming_collar"), 15)
        self.assertGreaterEqual(generator_source.count("make_aerodynamic_fuselage_assembly"), 2)
        self.assertGreaterEqual(generator_source.count("builder.add_towing_shackles"), 20)
        self.assertGreaterEqual(generator_source.count("builder.add_lifting_lugs"), 25)
        self.assertGreaterEqual(generator_source.count("builder.add_crew_cupola"), 12)
    def test_surge_arc_tank_envelope_allows_f2_suspension_clearance(self) -> None:
        """The custom Surge builder must leave room for f2cd4cb track details."""
        source = (REPO_ROOT / "tools" / "Build-SurgeArcTankModel.py").read_text(encoding="utf-8")
        self.assertIn(
            'bounds["length_y"] > 3.0',
            source,
            "Surge Arc Tank envelope rejects the f2cd4cb suspension/mudflap extent",
        )
        self.assertIn(
            'diagnostics["total_polygons"] > 6500',
            source,
            "Surge Arc Tank polygon guard rejects the f2cd4cb suspension details",
        )


if __name__ == "__main__":
    unittest.main()
