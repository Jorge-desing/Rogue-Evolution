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
            "make_tapered_cylinder",
            "make_weld_seam_bead",
            "make_cast_pelvis_assembly",
            "make_articulated_mech_torso",
            "make_sculpted_wheel_fender_assembly",
            "make_transitional_coaming_collar",
            "make_aerodynamic_fuselage_assembly",
            "make_towing_shackle_assembly",
            "make_crane_lifting_lugs",
            "make_interlocking_armor_gusset",
            "make_glacis_splash_rib",
            "make_bustle_stowage_basket",
            "make_panoramic_commander_sight",
            "make_recovery_winch_assembly",
            "make_auxiliary_fuel_drum_assembly",
            "make_exhaust_deflector_cowl",
            "make_turret_ring_guard",
            "make_driver_periscope_hood",
            "make_perforated_exhaust_heat_shield",
            "make_weapon_trunnion_bracket_assembly",
            "make_final_drive_housing_assembly",
            "make_armored_access_hatch_assembly",
            "make_track_tensioner_idler_yoke_assembly",
            "make_coaxial_gun_port_assembly",
            "make_pioneer_tool_rack_assembly",
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
            "add_interlocking_gussets",
            "add_splash_rib",
            "add_bustle_basket",
            "add_commander_sight",
            "add_recovery_winch",
            "add_fuel_drums",
            "add_exhaust_deflector",
            "add_weld_seams",
            "add_glacis_weld_seams",
            "add_deck_edge_weld_seams",
            "add_turret_ring_guard",
            "add_driver_periscope_hood",
            "add_exhaust_heat_shield",
            "add_weapon_trunnions",
            "add_final_drive_housings",
            "add_access_hatches",
            "add_track_tensioners",
            "add_coaxial_weapon_ports",
            "add_pioneer_tool_racks",
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
        self.assertGreaterEqual(generator_source.count("builder.add_interlocking_gussets"), 8)
        self.assertGreaterEqual(generator_source.count("builder.add_splash_rib"), 10)
        self.assertGreaterEqual(generator_source.count("builder.add_weld_seams"), 3)
        self.assertGreaterEqual(generator_source.count("builder.add_glacis_weld_seams"), 10)
        self.assertGreaterEqual(generator_source.count("builder.add_deck_edge_weld_seams"), 20)
        self.assertGreaterEqual(generator_source.count("builder.add_bustle_basket"), 6)
        self.assertGreaterEqual(generator_source.count("builder.add_commander_sight"), 8)
        self.assertGreaterEqual(generator_source.count("builder.add_recovery_winch"), 6)
        self.assertGreaterEqual(generator_source.count("builder.add_fuel_drums"), 3)
        self.assertGreaterEqual(generator_source.count("builder.add_era_bricks"), 4)
        self.assertGreaterEqual(generator_source.count("builder.add_exhaust_deflector"), 4)
        self.assertGreaterEqual(generator_source.count("builder.add_turret_ring_guard"), 8)
        self.assertGreaterEqual(generator_source.count("builder.add_driver_periscope_hood"), 10)
        self.assertGreaterEqual(generator_source.count("builder.add_exhaust_heat_shield"), 4)
        self.assertGreaterEqual(generator_source.count("builder.add_weapon_trunnions"), 8)
        self.assertGreaterEqual(generator_source.count("builder.add_final_drive_housings"), 12)
        self.assertGreaterEqual(generator_source.count("builder.add_access_hatches"), 10)
        self.assertGreaterEqual(generator_source.count("builder.add_track_tensioners"), 12)
        self.assertGreaterEqual(generator_source.count("builder.add_coaxial_weapon_ports"), 8)
        self.assertGreaterEqual(generator_source.count("builder.add_pioneer_tool_racks"), 10)

    def test_shader_and_builder_wire_steel_micro_detail(self) -> None:
        """The detail normal texture, shader sampler, and prefab builder binding must stay in sync."""
        shader = (REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "RogueCommandExpansionSample"
                  / "Shaders" / "VehiclePBR_VertexWear.shader").read_text(encoding="utf-8")
        self.assertIn("BlendNormalsRNM", shader)
        self.assertIn("UnpackNormalScale(detailPacked", shader)
        self.assertIn("_ThermalTemperingIntensity", shader)
        self.assertIn("temperMask", shader)
        self.assertIn("_CreviceDustIntensity", shader)
        builder = (REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "Editor"
                   / "VehiclePrefabBuilder.cs").read_text(encoding="utf-8")
        self.assertIn("RCL_SteelMicroDetail_Normal.png", builder)
        self.assertIn('"_DetailBumpMap"', builder)
        tactical = (REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "Editor"
                   / "TacticalOperationsPackScaffolder.cs").read_text(encoding="utf-8")
        self.assertIn("RCL_SteelMicroDetail_Normal.png", tactical)
        self.assertIn('SetTexture("_DetailBumpMap"', tactical)
        texture = (REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "RogueCommandExpansionSample"
                   / "Textures" / "Detail" / "RCL_SteelMicroDetail_Normal.png")
        self.assertTrue(texture.is_file(), f"Missing {texture}")

    def test_surge_arc_tank_envelope_allows_f2_suspension_clearance(self) -> None:
        """The custom Surge builder must leave room for f2cd4cb track details."""
        source = (REPO_ROOT / "tools" / "Build-SurgeArcTankModel.py").read_text(encoding="utf-8")
        self.assertIn(
            'bounds["length_y"] > 3.0',
            source,
            "Surge Arc Tank envelope rejects the f2cd4cb suspension/mudflap extent",
        )
        self.assertIn(
            'diagnostics["total_polygons"] > 7000',
            source,
            "Surge Arc Tank polygon guard rejects the completed Wave 2 detail set",
        )

    def test_custom_visual_exporters_match_unity_batch_prefixes(self) -> None:
        """Manual source exporters must use the prefixes expected by the Unity scaffolder."""
        expected = {
            "Build-SampleScoutModel.py": "SandlanceScout",
            "Build-ColossusSiegeModel.py": "ColossusSiegePlatform",
            "Build-SkirmishStrikeBikeModel.py": "SkirmishStrikeBike",
        }
        for filename, prefix in expected.items():
            with self.subTest(builder=filename):
                source = (REPO_ROOT / "tools" / filename).read_text(encoding="utf-8")
                self.assertIn(f'merge_meshes_by_material(model_objects, "{prefix}"', source)
        manual = (REPO_ROOT / "tools" / "Apply-ManualUnitWave2Details.py").read_text(encoding="utf-8")
        self.assertIn('prefix=unit_name', manual)
        self.assertIn('include_empties=True', manual)

    def test_active_units_have_wave2_visual_profiles(self) -> None:
        """Every custom active unit has an explicit f2cd4cb/8d31bc1 integration profile."""
        support = REPO_ROOT / "tools" / "ActiveUnitVisualEnhancements.py"
        self.assertTrue(support.is_file(), f"Missing {support}")
        namespace = {}
        tree = ast.parse(support.read_text(encoding="utf-8"), filename=str(support))
        profile_assignment = next(
            node for node in tree.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "ACTIVE_UNIT_WAVE2_PROFILES" for target in node.targets)
        )
        profiles = ast.literal_eval(profile_assignment.value)
        required = {
            "SandlanceScout": {"towing", "lifting", "suspension"},
            "ColossusSiegePlatform": {"towing", "lifting", "suspension"},
            "IroncladWalker": {"lifting"},
            "AtlasCraneMech": {"lifting"},
            "BastionSiegeMech": {"lifting", "crawler"},
            "ArachnidHexapod": {"lifting", "crawler", "missile"},
            "OverlordCommandHQ": {"towing", "lifting", "suspension", "fenders", "coaming"},
            "SkirmishStrikeBike": {"coaming"},
            "SurgeArcTank": {"towing", "lifting", "suspension"},
            "TidebreakerHovercraft": {"hover"},
            "SalvageHauler": {"towing", "lifting"},
            "SkyguardSAM": {"towing", "lifting", "missile"},
            "BulwarkSiegeTank": {"towing", "lifting"},
            "ArcLanceWalker": {"lifting"},
            "FieldMedicRover": {"towing", "lifting", "fenders"},
            "SentinelRelay": {"towing", "lifting"},
            "GravitasBreaker": {"towing", "lifting"},
            "CommandRelay": {"towing", "lifting"},
        }
        self.assertEqual(set(required), set(profiles), "Wave 2 profile coverage drifted from active units")
        for unit, features in required.items():
            with self.subTest(unit=unit):
                self.assertTrue(features.issubset(set(profiles[unit]["features"])))

    def test_active_units_have_wave34_anchor_profiles(self) -> None:
        """Every active visual unit declares the 55442dd structural-detail profile it consumes."""
        support = REPO_ROOT / "tools" / "ActiveUnitVisualEnhancements.py"
        tree = ast.parse(support.read_text(encoding="utf-8"), filename=str(support))
        profile_assignment = next(
            (
                node for node in tree.body
                if isinstance(node, ast.Assign)
                and any(
                    isinstance(target, ast.Name)
                    and target.id == "ACTIVE_UNIT_WAVE34_PROFILES"
                    for target in node.targets
                )
            ),
            None,
        )
        self.assertIsNotNone(profile_assignment, "ACTIVE_UNIT_WAVE34_PROFILES assignment missing")
        assert profile_assignment is not None
        profiles = ast.literal_eval(profile_assignment.value)
        required = {
            "SandlanceScout": {"sponson_bridges", "engine_cavity", "antenna_collar", "gussets"},
            "ColossusSiegePlatform": {"sponson_bridges", "engine_cavity", "antenna_collar", "gussets"},
            "IroncladWalker": {"leg_mounts", "gatling_mounts"},
            "AtlasCraneMech": {"leg_mounts", "gussets"},
            "BastionSiegeMech": {"gussets"},
            "ArachnidHexapod": {"missile_mounts", "gussets"},
            "OverlordCommandHQ": {"drive_axles", "gatling_mounts", "radar_mount", "antenna_collar", "smoke_brackets", "gussets"},
            "SkirmishStrikeBike": {"drive_axles", "engine_cavity", "gussets"},
            "SurgeArcTank": {"sponson_bridges", "arc_mount", "engine_cavity", "gussets"},
            "TidebreakerHovercraft": {"gatling_mounts", "antenna_collar"},
            "SalvageHauler": {"gatling_mounts", "antenna_collar"},
            "SkyguardSAM": {"drive_axles", "missile_mounts", "radar_mount", "antenna_collar", "gussets"},
            "BulwarkSiegeTank": {"sponson_bridges", "engine_cavity", "gussets"},
            "ArcLanceWalker": {"leg_mounts", "arc_mount", "gussets"},
            "FieldMedicRover": {"drive_axles", "antenna_collar", "gussets"},
            "SentinelRelay": {"drive_axles", "antenna_collar", "gussets"},
            "GravitasBreaker": {"sponson_bridges", "engine_cavity", "gussets"},
            "CommandRelay": {"drive_axles", "radar_mount", "antenna_collar", "gussets"},
        }
        self.assertEqual(set(required), set(profiles), "Wave 3/4 profile coverage drifted from active units")
        for unit, features in required.items():
            with self.subTest(unit=unit):
                self.assertTrue(features.issubset(set(profiles[unit]["features"])))
                if "engine_cavity" in features:
                    self.assertIn("hull_width", profiles[unit])
                    self.assertIn("hull_length", profiles[unit])
                    self.assertIn("deck_z", profiles[unit])

    def test_active_unit_builders_apply_wave34_details(self) -> None:
        """All active builders and the manual bridge consume the shared Wave 3/4 overlay."""
        builder_names = [
            "Build-SampleScoutModel.py",
            "Build-ColossusSiegeModel.py",
            "Build-IroncladWalkerModel.py",
            "Build-SkirmishStrikeBikeModel.py",
            "Build-SurgeArcTankModel.py",
            "Build-ArachnidHexapodModel.py",
            "Build-OverlordCommandHQModel.py",
            "Build-TidebreakerHovercraftModel.py",
            "Build-SalvageHaulerModel.py",
            "Build-SkyguardSAMModel.py",
            "Build-BulwarkSiegeTankModel.py",
            "Build-ArcLanceWalkerModel.py",
            "Build-FieldMedicRoverModel.py",
            "Build-SentinelRelayModel.py",
            "Build-GravitasBreakerModel.py",
            "Build-CommandRelayModel.py",
            "Apply-ManualUnitWave2Details.py",
        ]
        for filename in builder_names:
            with self.subTest(builder=filename):
                source = (REPO_ROOT / "tools" / filename).read_text(encoding="utf-8")
                self.assertIn("apply_wave34_details", source)

    def test_colossus_wave34_envelope_allows_antenna_clearance(self) -> None:
        """Colossus keeps enough vertical room for its authored antenna and structural details."""
        source = (REPO_ROOT / "tools" / "Build-ColossusSiegeModel.py").read_text(encoding="utf-8")
        self.assertIn('bounds["height_z"] > 2.0', source)

    def test_generator_batch_help_matches_current_archetype_catalog(self) -> None:
        """The public batch flag must advertise all 42 current mobile archetypes."""
        source = TOOLS_GUM.read_text(encoding="utf-8")
        self.assertIn(
            'help="Batch generate all 42 archetypes into the output directory"',
            source,
        )
        self.assertNotIn("Batch generate all 36 archetypes", source)

if __name__ == "__main__":
    unittest.main()
