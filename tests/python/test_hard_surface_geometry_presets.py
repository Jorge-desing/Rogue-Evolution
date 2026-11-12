"""Unit tests for reusable Level 2 hard-surface geometry presets.

Validates config definitions, parameter bounds checking, registry mappings,
and headless geometry generation across contemporary and futuristic military styles.
"""

from __future__ import annotations

import json
import math
import os
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any
import unittest

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "tools"))

from hard_surface_geometry_presets import (
    APS_HARDKILL_RADAR_PRESET_ID,
    APS_HARDKILL_RADAR_V2_PRESET_ID,
    APS_PHASE_SHIELD_PRESET_ID,
    APS_PHASE_SHIELD_V2_PRESET_ID,
    AUTOCANNON_REMOTE_SPONSON_PRESET_ID,
    AUTOCANNON_REMOTE_SPONSON_V2_PRESET_ID,
    COAXIAL_SERVICE_CONNECTOR_PRESET_ID,
    ERA_BRICK_PRESET_ID,
    FUEL_AUX_JERRYCAN_RACK_PRESET_ID,
    FUEL_AUX_JERRYCAN_RACK_V2_PRESET_ID,
    GEOMETRY_PRESETS,
    NANITE_REPAIR_DISPENSER_PRESET_ID,
    NANITE_REPAIR_DISPENSER_V2_PRESET_ID,
    ORDNANCE_MINE_DISPENSER_PRESET_ID,
    ORDNANCE_MINE_DISPENSER_V2_PRESET_ID,
    PLASMA_CONTAINMENT_CELL_PRESET_ID,
    PLASMA_CONTAINMENT_CELL_V2_PRESET_ID,
    PLASMA_TORPEDO_MAGAZINE_PRESET_ID,
    PLASMA_TORPEDO_MAGAZINE_V2_PRESET_ID,
    PRESET_METADATA,
    PRESET_SLOTS,
    PRESET_STYLES,
    SERVICE_HATCH_PRESET_ID,
    SLAT_ARMOR_SCREEN_PRESET_ID,
    SLAT_ARMOR_SCREEN_V2_PRESET_ID,
    STEALTH_HEX_REACTIVE_TILE_PRESET_ID,
    STEALTH_HEX_REACTIVE_TILE_V2_PRESET_ID,
    TACHYONIC_RADIATOR_ARRAY_PRESET_ID,
    TACHYONIC_RADIATOR_ARRAY_V2_PRESET_ID,
    THERMAL_ARMORED_RADIATOR_PRESET_ID,
    THERMAL_ARMORED_RADIATOR_V2_PRESET_ID,
    # Functional Level 2 presets
    APU_TURBOGENERATOR_SPONSON_PRESET_ID,
    CIWS_ROTARY_POINT_DEFENSE_PRESET_ID,
    CRYO_FUSION_CELL_CONTAINMENT_RACK_PRESET_ID,
    DOCKING_CLAMP_HYDRAULIC_ARM_PRESET_ID,
    EXCAVATION_DRILL_CARBIDE_BORER_PRESET_ID,
    FIELD_ENGINEERING_CRANE_WINCH_PRESET_ID,
    FLUX_DEFLECTION_SHIELD_PROJECTOR_PRESET_ID,
    GRAVITON_RECOIL_ANCHOR_PRESET_ID,
    MAGNETIC_TRACTOR_DOCKING_CLAMP_PRESET_ID,
    MULTISPECTRAL_COUNTERMEASURE_JAMMER_PRESET_ID,
    NANOFABRICATION_REPAIR_PROJECTOR_PRESET_ID,
    OPTRONIC_RECON_SENSOR_MAST_PRESET_ID,
    PLASMA_EXCAVATION_TORCH_PRESET_ID,
    QUAD_PULSE_LASER_CIWS_PRESET_ID,
    REINFORCED_FUEL_DRUM_DROP_RACK_PRESET_ID,
    SMOKE_GRENADE_SALVO_DISCHARGER_PRESET_ID,
    ApuTurbogeneratorSponsonConfig,
    ARMOR_ACCESS_PANEL_PRESET_ID,
    ENGINE_DECK_LOUVER_GRILLE_PRESET_ID,
    COMPOSITE_HEX_VENT_PANEL_PRESET_ID,
    CABLE_CONDUIT_JUNCTION_PRESET_ID,
    ArmorAccessPanelConfig,
    EngineDeckLouverGrilleConfig,
    CompositeHexVentPanelConfig,
    CableConduitJunctionConfig,
    CiwsRotaryPointDefenseConfig,
    CryoFusionCellContainmentRackConfig,
    DockingClampHydraulicArmConfig,
    ExcavationDrillCarbideBorerConfig,
    FieldEngineeringCraneWinchConfig,
    FluxDeflectionShieldProjectorConfig,
    GravitonRecoilAnchorConfig,
    MagneticTractorDockingClampConfig,
    MultispectralCountermeasureJammerConfig,
    NanofabricationRepairProjectorConfig,
    OptronicReconSensorMastConfig,
    PlasmaExcavationTorchConfig,
    QuadPulseLaserCiwsConfig,
    ReinforcedFuelDrumDropRackConfig,
    SmokeGrenadeSalvoDischargerConfig,
    ApsHardkillRadarConfig,
    ApsHardkillRadarV2Config,
    ApsPhaseShieldConfig,
    ApsPhaseShieldV2Config,
    AutocannonRemoteSponsonConfig,
    AutocannonRemoteSponsonV2Config,
    CoaxialServiceConnectorConfig,
    EraBrickConfig,
    FuelAuxJerrycanRackConfig,
    FuelAuxJerrycanRackV2Config,
    NaniteRepairDispenserConfig,
    NaniteRepairDispenserV2Config,
    OrdnanceMineDispenserConfig,
    OrdnanceMineDispenserV2Config,
    PlasmaContainmentCellConfig,
    PlasmaContainmentCellV2Config,
    PlasmaTorpedoMagazineConfig,
    PlasmaTorpedoMagazineV2Config,
    ServiceHatchConfig,
    SlatArmorScreenConfig,
    SlatArmorScreenV2Config,
    StealthHexReactiveTileConfig,
    StealthHexReactiveTileV2Config,
    TachyonicRadiatorArrayConfig,
    TachyonicRadiatorArrayV2Config,
    ThermalArmoredRadiatorConfig,
    ThermalArmoredRadiatorV2Config,
)


class TestHardSurfacePresetConfigs(unittest.TestCase):
    """Test configuration dataclasses, bounds checking, and validation invariants."""

    def test_aps_hardkill_radar_config_valid(self) -> None:
        cfg = ApsHardkillRadarConfig(
            center=(0.1, -0.2, 0.5),
            normal=(0.0, 0.0, 1.0),
            base_plate_size=(0.30, 0.25, 0.02),
        )
        data = cfg.to_dict()
        self.assertEqual(data["mortar_caliber"], 0.076)
        self.assertEqual(data["mortar_elevation_deg"], 35.0)

    def test_aps_hardkill_radar_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            ApsHardkillRadarConfig(mortar_caliber=-0.05)
        with self.assertRaises(ValueError):
            ApsHardkillRadarConfig(normal=(0.0, 0.0, 0.0))
        with self.assertRaises(ValueError):
            ApsHardkillRadarConfig(housing_size=(0.10, 0.10, float("nan")))

    def test_fuel_aux_jerrycan_rack_config_valid(self) -> None:
        cfg = FuelAuxJerrycanRackConfig(
            center=(0.0, -1.2, 0.6),
            normal=(0.0, -1.0, 0.0),
            basket_size=(0.60, 0.35, 0.40),
        )
        data = cfg.to_dict()
        self.assertEqual(data["can_dimensions"], (0.22, 0.14, 0.34))
        self.assertEqual(data["tube_radius"], 0.009)

    def test_fuel_aux_jerrycan_rack_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            FuelAuxJerrycanRackConfig(basket_size=(0.20, 0.10, 0.10))  # Cans don't fit
        with self.assertRaises(ValueError):
            FuelAuxJerrycanRackConfig(stiffener_groove_depth=-0.01)

    def test_slat_armor_screen_config_valid(self) -> None:
        cfg = SlatArmorScreenConfig(
            center=(0.0, 1.5, 0.4),
            normal=(0.0, 1.0, 0.0),
            screen_size=(0.70, 0.50),
            standoff_distance=0.25,
            bar_count=8,
        )
        data = cfg.to_dict()
        self.assertEqual(data["bar_count"], 8)
        self.assertEqual(data["standoff_distance"], 0.25)

    def test_slat_armor_screen_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            SlatArmorScreenConfig(bar_count=1)
        with self.assertRaises(ValueError):
            SlatArmorScreenConfig(standoff_distance=-0.1)

    def test_ordnance_mine_dispenser_config_valid(self) -> None:
        cfg = OrdnanceMineDispenserConfig(
            center=(0.0, 0.0, 0.0),
            rack_size=(0.64, 0.38, 0.44),
            cassette_count=4,
            tubes_per_cassette=3,
        )
        data = cfg.to_dict()
        self.assertEqual(data["cassette_count"], 4)
        self.assertEqual(data["tubes_per_cassette"], 3)
        self.assertEqual(data["canister_caliber"], 0.068)

    def test_ordnance_mine_dispenser_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            OrdnanceMineDispenserConfig(canister_caliber=-0.05)
        with self.assertRaises(ValueError):
            OrdnanceMineDispenserConfig(cassette_count=0)
        with self.assertRaises(ValueError):
            OrdnanceMineDispenserConfig(normal=(0.0, 0.0, 0.0))

    def test_thermal_armored_radiator_config_valid(self) -> None:
        cfg = ThermalArmoredRadiatorConfig(
            center=(0.0, 0.0, 0.0),
            cowl_size=(0.34, 0.60, 0.40),
            louver_count=6,
        )
        data = cfg.to_dict()
        self.assertEqual(data["louver_count"], 6)
        self.assertEqual(data["manifold_pipe_radius"], 0.018)

    def test_thermal_armored_radiator_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            ThermalArmoredRadiatorConfig(cowl_size=(-0.1, 0.5, 0.3))
        with self.assertRaises(ValueError):
            ThermalArmoredRadiatorConfig(louver_count=1)
        with self.assertRaises(ValueError):
            ThermalArmoredRadiatorConfig(manifold_pipe_radius=0.0)

    def test_autocannon_remote_sponson_config_valid(self) -> None:
        cfg = AutocannonRemoteSponsonConfig(
            center=(0.0, 0.0, 0.0),
            cradle_size=(0.28, 0.34, 0.26),
            caliber=0.030,
            barrel_length=0.48,
        )
        data = cfg.to_dict()
        self.assertEqual(data["caliber"], 0.030)
        self.assertEqual(data["barrel_length"], 0.48)
        self.assertEqual(data["elevation_deg"], 12.0)

    def test_autocannon_remote_sponson_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            AutocannonRemoteSponsonConfig(caliber=-0.01)
        with self.assertRaises(ValueError):
            AutocannonRemoteSponsonConfig(barrel_length=0.0)
        with self.assertRaises(ValueError):
            AutocannonRemoteSponsonConfig(normal=(0.0, 0.0, 0.0))

    def test_aps_phase_shield_config_valid(self) -> None:
        cfg = ApsPhaseShieldConfig(
            center=(0.0, 0.0, 0.8),
            lens_radius=0.08,
            vent_louver_count=5,
        )
        data = cfg.to_dict()
        self.assertEqual(data["lens_radius"], 0.08)
        self.assertEqual(data["vent_louver_count"], 5)

    def test_aps_phase_shield_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            ApsPhaseShieldConfig(lens_radius=0.25)  # Exceeds nacelle aperture
        with self.assertRaises(ValueError):
            ApsPhaseShieldConfig(vent_louver_count=0)

    def test_plasma_containment_cell_config_valid(self) -> None:
        cfg = PlasmaContainmentCellConfig(
            center=(1.2, 0.0, 0.4),
            core_window_radius=0.08,
            fin_count=6,
        )
        data = cfg.to_dict()
        self.assertEqual(data["core_window_radius"], 0.08)
        self.assertEqual(data["fin_count"], 6)

    def test_plasma_containment_cell_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            PlasmaContainmentCellConfig(core_window_radius=0.35)  # Exceeds face
        with self.assertRaises(ValueError):
            PlasmaContainmentCellConfig(normal=(0.0, 0.0, 0.0))

    def test_stealth_hex_reactive_tile_config_valid(self) -> None:
        cfg = StealthHexReactiveTileConfig(
            tile_radius=0.08,
            tile_height=0.04,
            tile_gap=0.004,
        )
        data = cfg.to_dict()
        self.assertEqual(data["tile_radius"], 0.08)
        self.assertEqual(data["tile_height"], 0.04)

    def test_stealth_hex_reactive_tile_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            StealthHexReactiveTileConfig(facet_bevel=0.05)  # Too large for tile
        with self.assertRaises(ValueError):
            StealthHexReactiveTileConfig(tile_gap=-0.002)

    def test_plasma_torpedo_magazine_config_valid(self) -> None:
        cfg = PlasmaTorpedoMagazineConfig(
            center=(0.0, 0.0, 0.0),
            rack_size=(0.64, 0.38, 0.40),
            torpedo_count=3,
        )
        data = cfg.to_dict()
        self.assertEqual(data["torpedo_count"], 3)
        self.assertEqual(data["torpedo_radius"], 0.052)
        self.assertEqual(data["torpedo_length"], 0.36)

    def test_plasma_torpedo_magazine_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            PlasmaTorpedoMagazineConfig(torpedo_count=0)
        with self.assertRaises(ValueError):
            PlasmaTorpedoMagazineConfig(torpedo_length=-0.2)
        with self.assertRaises(ValueError):
            PlasmaTorpedoMagazineConfig(rack_size=(0.10, 0.10, 0.10))  # Torpedoes don't fit

    def test_tachyonic_radiator_array_config_valid(self) -> None:
        cfg = TachyonicRadiatorArrayConfig(
            center=(0.0, 0.0, 0.0),
            array_size=(0.34, 0.62, 0.36),
            emitter_fin_count=6,
        )
        data = cfg.to_dict()
        self.assertEqual(data["emitter_fin_count"], 6)
        self.assertEqual(data["core_rod_radius"], 0.022)

    def test_tachyonic_radiator_array_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            TachyonicRadiatorArrayConfig(emitter_fin_count=1)
        with self.assertRaises(ValueError):
            TachyonicRadiatorArrayConfig(core_rod_radius=-0.01)
        with self.assertRaises(ValueError):
            TachyonicRadiatorArrayConfig(normal=(0.0, 0.0, 0.0))

    def test_nanite_repair_dispenser_config_valid(self) -> None:
        cfg = NaniteRepairDispenserConfig(
            center=(0.0, 0.0, 0.0),
            pod_size=(0.32, 0.52, 0.38),
            nozzle_count=3,
        )
        data = cfg.to_dict()
        self.assertEqual(data["nozzle_count"], 3)
        self.assertEqual(data["canister_radius"], 0.062)

    def test_nanite_repair_dispenser_config_invalid(self) -> None:
        with self.assertRaises(ValueError):
            NaniteRepairDispenserConfig(nozzle_count=0)
        with self.assertRaises(ValueError):
            NaniteRepairDispenserConfig(canister_radius=-0.01)
        with self.assertRaises(ValueError):
            NaniteRepairDispenserConfig(normal=(0.0, 0.0, 0.0))

    def test_coaxial_service_connector_config_valid_and_rejects_unsafe_fit(self) -> None:
        config = CoaxialServiceConnectorConfig(center=(0.0, 0.31, 0.0))
        self.assertEqual(config.seat_depth, 0.032)
        self.assertEqual(config.panel_size, (0.46, 0.26))
        self.assertEqual(config.to_dict()["normal"], (0.0, 1.0, 0.0))
        with self.assertRaisesRegex(ValueError, "18 mm"):
            CoaxialServiceConnectorConfig(center=(0.0, 0.0, 0.0), panel_thickness=0.045)
        with self.assertRaisesRegex(ValueError, "parallel"):
            CoaxialServiceConnectorConfig(center=(0.0, 0.0, 0.0), up=(0.0, 1.0, 0.0))
        with self.assertRaisesRegex(ValueError, "edge clearance"):
            CoaxialServiceConnectorConfig(center=(0.0, 0.0, 0.0), panel_size=(0.12, 0.12))
        with self.assertRaisesRegex(ValueError, "counterbore floor"):
            CoaxialServiceConnectorConfig(center=(0.0, 0.0, 0.0), seat_depth=0.001)
        with self.assertRaisesRegex(ValueError, "3 mm clearance"):
            CoaxialServiceConnectorConfig(center=(0.0, 0.0, 0.0), fastener_offset=0.075)

    # V2 Config Tests
    def test_aps_hardkill_radar_v2_config_valid_and_invalid(self) -> None:
        cfg = ApsHardkillRadarV2Config(mortar_count=4, mortar_caliber=0.076)
        self.assertEqual(cfg.mortar_count, 4)
        with self.assertRaises(ValueError):
            ApsHardkillRadarV2Config(mortar_count=1)
        with self.assertRaises(ValueError):
            ApsHardkillRadarV2Config(normal=(0.0, 0.0, 0.0))

    def test_fuel_aux_jerrycan_rack_v2_config_valid_and_invalid(self) -> None:
        cfg = FuelAuxJerrycanRackV2Config(basket_size=(0.60, 0.34, 0.40))
        self.assertEqual(cfg.basket_size, (0.60, 0.34, 0.40))
        with self.assertRaises(ValueError):
            FuelAuxJerrycanRackV2Config(basket_size=(0.10, 0.10, 0.10))

    def test_slat_armor_screen_v2_config_valid_and_invalid(self) -> None:
        cfg = SlatArmorScreenV2Config(bar_count=8, standoff_distance=0.24)
        self.assertEqual(cfg.bar_count, 8)
        with self.assertRaises(ValueError):
            SlatArmorScreenV2Config(bar_count=1)

    def test_ordnance_mine_dispenser_v2_config_valid_and_invalid(self) -> None:
        cfg = OrdnanceMineDispenserV2Config(cassette_count=4, tubes_per_cassette=3)
        self.assertEqual(cfg.cassette_count, 4)
        with self.assertRaises(ValueError):
            OrdnanceMineDispenserV2Config(cassette_count=0)

    def test_thermal_armored_radiator_v2_config_valid_and_invalid(self) -> None:
        cfg = ThermalArmoredRadiatorV2Config(louver_count=6)
        self.assertEqual(cfg.louver_count, 6)
        with self.assertRaises(ValueError):
            ThermalArmoredRadiatorV2Config(louver_count=1)

    def test_autocannon_remote_sponson_v2_config_valid_and_invalid(self) -> None:
        cfg = AutocannonRemoteSponsonV2Config(caliber=0.038, barrel_length=0.50)
        self.assertEqual(cfg.caliber, 0.038)
        with self.assertRaises(ValueError):
            AutocannonRemoteSponsonV2Config(caliber=-0.01)

    def test_aps_phase_shield_v2_config_valid_and_invalid(self) -> None:
        cfg = ApsPhaseShieldV2Config(lens_radius=0.082)
        self.assertEqual(cfg.lens_radius, 0.082)
        with self.assertRaises(ValueError):
            ApsPhaseShieldV2Config(lens_radius=0.25)

    def test_plasma_containment_cell_v2_config_valid_and_invalid(self) -> None:
        cfg = PlasmaContainmentCellV2Config(core_window_radius=0.095)
        self.assertEqual(cfg.core_window_radius, 0.095)
        with self.assertRaises(ValueError):
            PlasmaContainmentCellV2Config(core_window_radius=0.35)

    def test_stealth_hex_reactive_tile_v2_config_valid_and_invalid(self) -> None:
        cfg = StealthHexReactiveTileV2Config(tile_radius=0.078)
        self.assertEqual(cfg.tile_radius, 0.078)
        with self.assertRaises(ValueError):
            StealthHexReactiveTileV2Config(tile_radius=-0.01)

    def test_plasma_torpedo_magazine_v2_config_valid_and_invalid(self) -> None:
        cfg = PlasmaTorpedoMagazineV2Config(torpedo_count=3)
        self.assertEqual(cfg.torpedo_count, 3)
        with self.assertRaises(ValueError):
            PlasmaTorpedoMagazineV2Config(torpedo_count=0)

    def test_tachyonic_radiator_array_v2_config_valid_and_invalid(self) -> None:
        cfg = TachyonicRadiatorArrayV2Config(emitter_fin_count=8)
        self.assertEqual(cfg.emitter_fin_count, 8)
        with self.assertRaises(ValueError):
            TachyonicRadiatorArrayV2Config(emitter_fin_count=1)

    def test_nanite_repair_dispenser_v2_config_valid_and_invalid(self) -> None:
        cfg = NaniteRepairDispenserV2Config(nozzle_count=4)
        self.assertEqual(cfg.nozzle_count, 4)
        with self.assertRaises(ValueError):
            NaniteRepairDispenserV2Config(nozzle_count=0)

    def test_excavation_drill_carbide_borer_config_valid_and_invalid(self) -> None:
        cfg = ExcavationDrillCarbideBorerConfig(drill_base_radius=0.12, drill_tip_radius=0.04)
        self.assertEqual(cfg.drill_base_radius, 0.12)
        with self.assertRaises(ValueError):
            ExcavationDrillCarbideBorerConfig(drill_base_radius=0.02, drill_tip_radius=0.05)
        with self.assertRaises(ValueError):
            ExcavationDrillCarbideBorerConfig(gearbox_radius=-0.1)

    def test_docking_clamp_hydraulic_arm_config_valid_and_invalid(self) -> None:
        cfg = DockingClampHydraulicArmConfig(arm_length=0.30, jaw_opening_deg=35.0)
        self.assertEqual(cfg.arm_length, 0.30)
        with self.assertRaises(ValueError):
            DockingClampHydraulicArmConfig(arm_length=-0.2)
        with self.assertRaises(ValueError):
            DockingClampHydraulicArmConfig(normal=(0.0, 0.0, 0.0))

    def test_apu_turbogenerator_sponson_config_valid_and_invalid(self) -> None:
        cfg = ApuTurbogeneratorSponsonConfig(exhaust_diameter=0.08)
        self.assertEqual(cfg.exhaust_diameter, 0.08)
        with self.assertRaises(ValueError):
            ApuTurbogeneratorSponsonConfig(cowl_size=(0.0, 0.2, 0.2))

    def test_smoke_grenade_salvo_discharger_config_valid_and_invalid(self) -> None:
        cfg = SmokeGrenadeSalvoDischargerConfig(tube_count=8)
        self.assertEqual(cfg.tube_count, 8)
        with self.assertRaises(ValueError):
            SmokeGrenadeSalvoDischargerConfig(tube_count=5)

    def test_plasma_excavation_torch_config_valid_and_invalid(self) -> None:
        cfg = PlasmaExcavationTorchConfig(confinement_ring_count=3)
        self.assertEqual(cfg.confinement_ring_count, 3)
        with self.assertRaises(ValueError):
            PlasmaExcavationTorchConfig(confinement_ring_count=1)

    def test_magnetic_tractor_docking_clamp_config_valid_and_invalid(self) -> None:
        cfg = MagneticTractorDockingClampConfig(tractor_dish_radius=0.09)
        self.assertEqual(cfg.tractor_dish_radius, 0.09)
        with self.assertRaises(ValueError):
            MagneticTractorDockingClampConfig(tractor_dish_radius=-0.05)

    def test_graviton_recoil_anchor_config_valid_and_invalid(self) -> None:
        cfg = GravitonRecoilAnchorConfig(spike_length=0.28)
        self.assertEqual(cfg.spike_length, 0.28)
        with self.assertRaises(ValueError):
            GravitonRecoilAnchorConfig(spike_length=0.0)

    def test_optronic_recon_sensor_mast_config_valid_and_invalid(self) -> None:
        cfg = OptronicReconSensorMastConfig(radar_disc_radius=0.08)
        self.assertEqual(cfg.radar_disc_radius, 0.08)
        with self.assertRaises(ValueError):
            OptronicReconSensorMastConfig(mast_height=-0.1)

    def test_service_hatch_config_valid_and_invalid(self) -> None:
        cfg = ServiceHatchConfig(center_xy=(0.0, 0.0), outer_size=(0.40, 0.30), support_top_z=0.50)
        self.assertEqual(cfg.recess_depth, 0.025)
        self.assertEqual(cfg.frame_width, 0.015)
        with self.assertRaises(ValueError):
            ServiceHatchConfig(center_xy=(0.0, 0.0), outer_size=(-0.40, 0.30), support_top_z=0.50)
        with self.assertRaises(ValueError):
            ServiceHatchConfig(center_xy=(0.0, 0.0), outer_size=(0.40, 0.30), support_top_z=0.50, hinge_axis="Z")
        with self.assertRaises(ValueError):
            ServiceHatchConfig(center_xy=(0.0, 0.0), outer_size=(0.40, 0.30), support_top_z=0.50, hinge_side="INVALID")

    def test_era_brick_config_valid_and_invalid(self) -> None:
        cfg = EraBrickConfig(center=(0.0, 0.0, 0.0), normal=(0.0, 0.0, 1.0))
        self.assertEqual(cfg.size, (0.22, 0.13, 0.055))
        self.assertEqual(cfg.backing_overhang, 0.010)
        with self.assertRaises(ValueError):
            EraBrickConfig(center=(0.0, 0.0, 0.0), normal=(0.0, 0.0, 0.0))
        with self.assertRaises(ValueError):
            EraBrickConfig(center=(0.0, 0.0, 0.0), normal=(0.0, 0.0, 1.0), size=(-0.2, 0.1, 0.05))

    def test_ciws_rotary_point_defense_config_valid_and_invalid(self) -> None:
        cfg = CiwsRotaryPointDefenseConfig(barrel_length=0.40)
        self.assertEqual(cfg.barrel_length, 0.40)
        with self.assertRaises(ValueError):
            CiwsRotaryPointDefenseConfig(barrel_length=-0.1)
        with self.assertRaises(ValueError):
            CiwsRotaryPointDefenseConfig(elevation_deg=95.0)

    def test_reinforced_fuel_drum_drop_rack_config_valid_and_invalid(self) -> None:
        cfg = ReinforcedFuelDrumDropRackConfig(drum_radius=0.12)
        self.assertEqual(cfg.drum_radius, 0.12)
        with self.assertRaises(ValueError):
            ReinforcedFuelDrumDropRackConfig(rack_size=(0.20, 0.20, 0.20))
        with self.assertRaises(ValueError):
            ReinforcedFuelDrumDropRackConfig(drum_radius=-0.05)

    def test_field_engineering_crane_winch_config_valid_and_invalid(self) -> None:
        cfg = FieldEngineeringCraneWinchConfig(boom_luff_deg=35.0)
        self.assertEqual(cfg.boom_luff_deg, 35.0)
        with self.assertRaises(ValueError):
            FieldEngineeringCraneWinchConfig(boom_luff_deg=-10.0)
        with self.assertRaises(ValueError):
            FieldEngineeringCraneWinchConfig(turntable_radius=0.0)

    def test_multispectral_countermeasure_jammer_config_valid_and_invalid(self) -> None:
        cfg = MultispectralCountermeasureJammerConfig(jammer_height=0.25)
        self.assertEqual(cfg.jammer_height, 0.25)
        with self.assertRaises(ValueError):
            MultispectralCountermeasureJammerConfig(jammer_base_radius=0.01, jammer_tip_radius=0.05)
        with self.assertRaises(ValueError):
            MultispectralCountermeasureJammerConfig(normal=(0.0, 0.0, 0.0))

    def test_quad_pulse_laser_ciws_config_valid_and_invalid(self) -> None:
        cfg = QuadPulseLaserCiwsConfig(barrel_length=0.35)
        self.assertEqual(cfg.barrel_length, 0.35)
        with self.assertRaises(ValueError):
            QuadPulseLaserCiwsConfig(capacitor_fin_count=1)
        with self.assertRaises(ValueError):
            QuadPulseLaserCiwsConfig(elevation_deg=-30.0)

    def test_cryo_fusion_cell_containment_rack_config_valid_and_invalid(self) -> None:
        cfg = CryoFusionCellContainmentRackConfig(cell_radius=0.11)
        self.assertEqual(cfg.cell_radius, 0.11)
        with self.assertRaises(ValueError):
            CryoFusionCellContainmentRackConfig(bulkhead_size=(0.20, 0.10, 0.10))
        with self.assertRaises(ValueError):
            CryoFusionCellContainmentRackConfig(decoupling_latch_count=1)

    def test_nanofabrication_repair_projector_config_valid_and_invalid(self) -> None:
        cfg = NanofabricationRepairProjectorConfig(arm_length=0.30)
        self.assertEqual(cfg.arm_length, 0.30)
        with self.assertRaises(ValueError):
            NanofabricationRepairProjectorConfig(arm_length=-0.2)
        with self.assertRaises(ValueError):
            NanofabricationRepairProjectorConfig(nozzle_radius=0.0)

    def test_flux_deflection_shield_projector_config_valid_and_invalid(self) -> None:
        cfg = FluxDeflectionShieldProjectorConfig(field_guide_angle_deg=40.0)
        self.assertEqual(cfg.field_guide_angle_deg, 40.0)
        with self.assertRaises(ValueError):
            FluxDeflectionShieldProjectorConfig(field_guide_angle_deg=10.0)
        with self.assertRaises(ValueError):
            FluxDeflectionShieldProjectorConfig(base_radius=0.0)

    def test_detailing_configs_valid_and_invalid(self) -> None:
        cfg = ArmorAccessPanelConfig(bolt_circle_count=8)
        self.assertEqual(cfg.bolt_circle_count, 8)
        self.assertEqual(cfg.to_dict()['bolt_circle_count'], 8)
        with self.assertRaises(ValueError):
            ArmorAccessPanelConfig(bolt_circle_count=2)
        with self.assertRaises(ValueError):
            ArmorAccessPanelConfig(panel_size=(0.0, 0.2, 0.03))
        with self.assertRaises(ValueError):
            EngineDeckLouverGrilleConfig(slot_count=1)
        with self.assertRaises(ValueError):
            CompositeHexVentPanelConfig(normal=(0.0, 0.0, 0.0))
        with self.assertRaises(ValueError):
            CableConduitJunctionConfig(fastener_radius=-0.01)


class TestHardSurfacePresetRegistry(unittest.TestCase):
    """Test registry completeness and style/slot coverage."""

    def test_all_expected_presets_registered(self) -> None:
        expected = {
            SERVICE_HATCH_PRESET_ID,
            ERA_BRICK_PRESET_ID,
            COAXIAL_SERVICE_CONNECTOR_PRESET_ID,
            APS_HARDKILL_RADAR_PRESET_ID,
            FUEL_AUX_JERRYCAN_RACK_PRESET_ID,
            SLAT_ARMOR_SCREEN_PRESET_ID,
            ORDNANCE_MINE_DISPENSER_PRESET_ID,
            THERMAL_ARMORED_RADIATOR_PRESET_ID,
            AUTOCANNON_REMOTE_SPONSON_PRESET_ID,
            APS_PHASE_SHIELD_PRESET_ID,
            PLASMA_CONTAINMENT_CELL_PRESET_ID,
            STEALTH_HEX_REACTIVE_TILE_PRESET_ID,
            PLASMA_TORPEDO_MAGAZINE_PRESET_ID,
            TACHYONIC_RADIATOR_ARRAY_PRESET_ID,
            NANITE_REPAIR_DISPENSER_PRESET_ID,
            # V2 presets
            APS_HARDKILL_RADAR_V2_PRESET_ID,
            FUEL_AUX_JERRYCAN_RACK_V2_PRESET_ID,
            SLAT_ARMOR_SCREEN_V2_PRESET_ID,
            ORDNANCE_MINE_DISPENSER_V2_PRESET_ID,
            THERMAL_ARMORED_RADIATOR_V2_PRESET_ID,
            AUTOCANNON_REMOTE_SPONSON_V2_PRESET_ID,
            APS_PHASE_SHIELD_V2_PRESET_ID,
            PLASMA_CONTAINMENT_CELL_V2_PRESET_ID,
            STEALTH_HEX_REACTIVE_TILE_V2_PRESET_ID,
            PLASMA_TORPEDO_MAGAZINE_V2_PRESET_ID,
            TACHYONIC_RADIATOR_ARRAY_V2_PRESET_ID,
            NANITE_REPAIR_DISPENSER_V2_PRESET_ID,
            # Functional Level 2 presets
            EXCAVATION_DRILL_CARBIDE_BORER_PRESET_ID,
            DOCKING_CLAMP_HYDRAULIC_ARM_PRESET_ID,
            APU_TURBOGENERATOR_SPONSON_PRESET_ID,
            SMOKE_GRENADE_SALVO_DISCHARGER_PRESET_ID,
            PLASMA_EXCAVATION_TORCH_PRESET_ID,
            MAGNETIC_TRACTOR_DOCKING_CLAMP_PRESET_ID,
            GRAVITON_RECOIL_ANCHOR_PRESET_ID,
            OPTRONIC_RECON_SENSOR_MAST_PRESET_ID,
            CIWS_ROTARY_POINT_DEFENSE_PRESET_ID,
            REINFORCED_FUEL_DRUM_DROP_RACK_PRESET_ID,
            FIELD_ENGINEERING_CRANE_WINCH_PRESET_ID,
            MULTISPECTRAL_COUNTERMEASURE_JAMMER_PRESET_ID,
            QUAD_PULSE_LASER_CIWS_PRESET_ID,
            CRYO_FUSION_CELL_CONTAINMENT_RACK_PRESET_ID,
            NANOFABRICATION_REPAIR_PROJECTOR_PRESET_ID,
            FLUX_DEFLECTION_SHIELD_PROJECTOR_PRESET_ID,
            # Detailing presets
            ARMOR_ACCESS_PANEL_PRESET_ID,
            ENGINE_DECK_LOUVER_GRILLE_PRESET_ID,
            COMPOSITE_HEX_VENT_PANEL_PRESET_ID,
            CABLE_CONDUIT_JUNCTION_PRESET_ID,
        }
        self.assertEqual(set(GEOMETRY_PRESETS.keys()), expected)
        self.assertEqual(set(PRESET_METADATA.keys()), expected)
        self.assertEqual(set(PRESET_STYLES.keys()), expected)
        self.assertEqual(set(PRESET_SLOTS.keys()), expected)

    def test_style_distribution(self) -> None:
        contemporary = [k for k, v in PRESET_STYLES.items() if v == "contemporary_military"]
        futuristic = [k for k, v in PRESET_STYLES.items() if v == "futuristic_military"]
        self.assertGreaterEqual(len(contemporary), 6)
        self.assertGreaterEqual(len(futuristic), 6)

    def test_canonical_slot_coverage(self) -> None:
        slots = set(PRESET_SLOTS.values())
        self.assertTrue({"APS", "FuelDrums", "Sponsons", "ERA"}.issubset(slots))


class TestHardSurfacePresetBlenderGeneration(unittest.TestCase):
    """Test creating all Level 2 presets in headless Blender."""

    @classmethod
    def setUpClass(cls) -> None:
        cls.blender_exe = REPO_ROOT / ".tools/Blender-4.5.14/blender-4.5.14-windows-x64/blender.exe"
        if not cls.blender_exe.exists():
            raise unittest.SkipTest("Blender 4.5.14 executable not found")

    def test_generate_all_presets_in_blender(self) -> None:
        presets_to_test = [
            (APS_HARDKILL_RADAR_PRESET_ID, "ApsHardkillRadarConfig(center=(0.0, 0.0, 0.0))"),
            (FUEL_AUX_JERRYCAN_RACK_PRESET_ID, "FuelAuxJerrycanRackConfig(center=(1.5, 0.0, 0.0))"),
            (SLAT_ARMOR_SCREEN_PRESET_ID, "SlatArmorScreenConfig(center=(3.0, 0.0, 0.0))"),
            (ORDNANCE_MINE_DISPENSER_PRESET_ID, "OrdnanceMineDispenserConfig(center=(4.5, 0.0, 0.0))"),
            (THERMAL_ARMORED_RADIATOR_PRESET_ID, "ThermalArmoredRadiatorConfig(center=(6.0, 0.0, 0.0))"),
            (AUTOCANNON_REMOTE_SPONSON_PRESET_ID, "AutocannonRemoteSponsonConfig(center=(7.5, 0.0, 0.0))"),
            (APS_PHASE_SHIELD_PRESET_ID, "ApsPhaseShieldConfig(center=(0.0, 2.0, 0.0))"),
            (PLASMA_CONTAINMENT_CELL_PRESET_ID, "PlasmaContainmentCellConfig(center=(1.5, 2.0, 0.0))"),
            (STEALTH_HEX_REACTIVE_TILE_PRESET_ID, "StealthHexReactiveTileConfig(center=(3.0, 2.0, 0.0))"),
            (PLASMA_TORPEDO_MAGAZINE_PRESET_ID, "PlasmaTorpedoMagazineConfig(center=(4.5, 2.0, 0.0))"),
            (TACHYONIC_RADIATOR_ARRAY_PRESET_ID, "TachyonicRadiatorArrayConfig(center=(6.0, 2.0, 0.0))"),
            (NANITE_REPAIR_DISPENSER_PRESET_ID, "NaniteRepairDispenserConfig(center=(7.5, 2.0, 0.0))"),
            (COAXIAL_SERVICE_CONNECTOR_PRESET_ID, "CoaxialServiceConnectorConfig(center=(0.0, 4.0, 0.0))"),
            (APS_HARDKILL_RADAR_V2_PRESET_ID, "ApsHardkillRadarV2Config(center=(0.0, 6.0, 0.0))"),
            (FUEL_AUX_JERRYCAN_RACK_V2_PRESET_ID, "FuelAuxJerrycanRackV2Config(center=(1.5, 6.0, 0.0))"),
            (SLAT_ARMOR_SCREEN_V2_PRESET_ID, "SlatArmorScreenV2Config(center=(3.0, 6.0, 0.0))"),
            (ORDNANCE_MINE_DISPENSER_V2_PRESET_ID, "OrdnanceMineDispenserV2Config(center=(4.5, 6.0, 0.0))"),
            (THERMAL_ARMORED_RADIATOR_V2_PRESET_ID, "ThermalArmoredRadiatorV2Config(center=(6.0, 6.0, 0.0))"),
            (AUTOCANNON_REMOTE_SPONSON_V2_PRESET_ID, "AutocannonRemoteSponsonV2Config(center=(7.5, 6.0, 0.0))"),
            (APS_PHASE_SHIELD_V2_PRESET_ID, "ApsPhaseShieldV2Config(center=(0.0, 8.0, 0.0))"),
            (PLASMA_CONTAINMENT_CELL_V2_PRESET_ID, "PlasmaContainmentCellV2Config(center=(1.5, 8.0, 0.0))"),
            (STEALTH_HEX_REACTIVE_TILE_V2_PRESET_ID, "StealthHexReactiveTileV2Config(center=(3.0, 8.0, 0.0))"),
            (PLASMA_TORPEDO_MAGAZINE_V2_PRESET_ID, "PlasmaTorpedoMagazineV2Config(center=(4.5, 8.0, 0.0))"),
            (TACHYONIC_RADIATOR_ARRAY_V2_PRESET_ID, "TachyonicRadiatorArrayV2Config(center=(6.0, 8.0, 0.0))"),
            (NANITE_REPAIR_DISPENSER_V2_PRESET_ID, "NaniteRepairDispenserV2Config(center=(7.5, 8.0, 0.0))"),
            (EXCAVATION_DRILL_CARBIDE_BORER_PRESET_ID, "ExcavationDrillCarbideBorerConfig(center=(0.0, 10.0, 0.0))"),
            (DOCKING_CLAMP_HYDRAULIC_ARM_PRESET_ID, "DockingClampHydraulicArmConfig(center=(1.5, 10.0, 0.0))"),
            (APU_TURBOGENERATOR_SPONSON_PRESET_ID, "ApuTurbogeneratorSponsonConfig(center=(3.0, 10.0, 0.0))"),
            (SMOKE_GRENADE_SALVO_DISCHARGER_PRESET_ID, "SmokeGrenadeSalvoDischargerConfig(center=(4.5, 10.0, 0.0))"),
            (PLASMA_EXCAVATION_TORCH_PRESET_ID, "PlasmaExcavationTorchConfig(center=(6.0, 10.0, 0.0))"),
            (MAGNETIC_TRACTOR_DOCKING_CLAMP_PRESET_ID, "MagneticTractorDockingClampConfig(center=(7.5, 10.0, 0.0))"),
            (GRAVITON_RECOIL_ANCHOR_PRESET_ID, "GravitonRecoilAnchorConfig(center=(9.0, 10.0, 0.0))"),
            (OPTRONIC_RECON_SENSOR_MAST_PRESET_ID, "OptronicReconSensorMastConfig(center=(10.5, 10.0, 0.0))"),
            (SERVICE_HATCH_PRESET_ID, "ServiceHatchConfig(center_xy=(0.0, 0.0), outer_size=(0.4, 0.3), support_top_z=0.5)"),
            (ERA_BRICK_PRESET_ID, "EraBrickConfig(center=(0.0, 0.0, 0.0), normal=(0.0, 0.0, 1.0))"),
            (CIWS_ROTARY_POINT_DEFENSE_PRESET_ID, "CiwsRotaryPointDefenseConfig(center=(0.0, 12.0, 0.0))"),
            (REINFORCED_FUEL_DRUM_DROP_RACK_PRESET_ID, "ReinforcedFuelDrumDropRackConfig(center=(1.5, 12.0, 0.0))"),
            (FIELD_ENGINEERING_CRANE_WINCH_PRESET_ID, "FieldEngineeringCraneWinchConfig(center=(3.0, 12.0, 0.0))"),
            (MULTISPECTRAL_COUNTERMEASURE_JAMMER_PRESET_ID, "MultispectralCountermeasureJammerConfig(center=(4.5, 12.0, 0.0))"),
            (QUAD_PULSE_LASER_CIWS_PRESET_ID, "QuadPulseLaserCiwsConfig(center=(6.0, 12.0, 0.0))"),
            (CRYO_FUSION_CELL_CONTAINMENT_RACK_PRESET_ID, "CryoFusionCellContainmentRackConfig(center=(7.5, 12.0, 0.0))"),
            (NANOFABRICATION_REPAIR_PROJECTOR_PRESET_ID, "NanofabricationRepairProjectorConfig(center=(9.0, 12.0, 0.0))"),
            (FLUX_DEFLECTION_SHIELD_PROJECTOR_PRESET_ID, "FluxDeflectionShieldProjectorConfig(center=(10.5, 12.0, 0.0))"),
            (ARMOR_ACCESS_PANEL_PRESET_ID, "ArmorAccessPanelConfig(center=(0.0, 14.0, 0.0))"),
            (ENGINE_DECK_LOUVER_GRILLE_PRESET_ID, "EngineDeckLouverGrilleConfig(center=(1.5, 14.0, 0.0))"),
            (COMPOSITE_HEX_VENT_PANEL_PRESET_ID, "CompositeHexVentPanelConfig(center=(3.0, 14.0, 0.0))"),
            (CABLE_CONDUIT_JUNCTION_PRESET_ID, "CableConduitJunctionConfig(center=(4.5, 14.0, 0.0))"),
        ]

        def run_worker_chunk(worker_idx: int, chunk: list[tuple[str, str]]) -> dict[str, Any]:
            items_code = ",\n".join(f"    ({repr(pid)}, hsg.{cfg_expr})" for pid, cfg_expr in chunk)
            preamble = ""
            if worker_idx == 0:
                preamble = (
                    "misaligned_panel = rmk.make_box('MisalignedServicePanel', (0.46, 0.08, 0.26), (0.0, 4.005, 0.0), gunmetal, bevel=0.0)\n"
                    "try:\n"
                    "    hsg.create_coaxial_service_connector_preset(prefix='MisalignedConnector', config=hsg.CoaxialServiceConnectorConfig(center=(0.0, 4.0, 0.0)), armor_material=armor, hardware_material=gunmetal, backing_panel=misaligned_panel)\n"
                    "except ValueError as exc:\n"
                    "    assert 'center' in str(exc), f'unexpected misalignment rejection: {exc}'\n"
                    "else:\n"
                    "    raise AssertionError('off-center service connector was accepted')\n"
                    "finally:\n"
                    "    p_data = misaligned_panel.data if misaligned_panel and misaligned_panel.type == 'MESH' else None\n"
                    "    bpy.data.objects.remove(misaligned_panel, do_unlink=True)\n"
                    "    if p_data and p_data.users == 0:\n"
                    "        bpy.data.meshes.remove(p_data)\n"
                )
            script = (
                "import json, sys\n"
                "from pathlib import Path\n"
                "sys.path.insert(0, 'tools')\n"
                "import bpy\n"
                "import RogueModelKit as rmk\n"
                "import hard_surface_geometry_presets as hsg\n"
                "\n"
                "rmk.clear_scene()\n"
                "armor = rmk.make_material('TestArmor', (0.3, 0.35, 0.4), metallic=0.7)\n"
                "gunmetal = rmk.make_material('TestGunmetal', (0.15, 0.15, 0.15), metallic=0.9)\n"
                "accent = rmk.make_material('TestAccent', (0.2, 0.7, 0.9), metallic=0.5)\n"
                "armor.use_fake_user = True\n"
                "gunmetal.use_fake_user = True\n"
                "accent.use_fake_user = True\n"
                f"{preamble}\n"
                "results = {}\n"
                f"presets_to_test = [\n{items_code}\n]\n"
                "for preset_id, config in presets_to_test:\n"
                "    creator = hsg.GEOMETRY_PRESETS[preset_id]\n"
                "    kwargs = {'prefix': f'Test_{preset_id}', 'config': config, 'armor_material': armor, 'hardware_material': gunmetal}\n"
                "    if 'accent_material' in creator.__code__.co_varnames:\n"
                "        kwargs['accent_material'] = accent\n"
                "    objs = creator(**kwargs)\n"
                "    mesh_objs = [o for o in objs if o.type == 'MESH']\n"
                "    if preset_id == hsg.COAXIAL_SERVICE_CONNECTOR_PRESET_ID:\n"
                "        bolts = [o for o in objs if o.get('rcl_service_connector_component') == 'panel_anchor_bolt']\n"
                "        assert len(bolts) == 4, f'expected four panel anchor bolts, got {len(bolts)}'\n"
                "        frame, right, up, normal = hsg._service_connector_frame(config)\n"
                "        face = hsg.Vector(config.center) + normal * (config.panel_thickness * 0.5)\n"
                "        offsets = sorted((round(abs((o.location - face).dot(right)), 6), round(abs((o.location - face).dot(up)), 6)) for o in bolts)\n"
                "        expected_offsets = sorted([(config.fastener_offset, 0.0), (config.fastener_offset, 0.0), (0.0, config.fastener_offset), (0.0, config.fastener_offset)])\n"
                "        assert offsets == expected_offsets, f'retainers do not sit at the four cardinal flange points: {offsets}'\n"
                "    total_verts = sum(len(o.data.vertices) for o in mesh_objs)\n"
                "    total_polys = sum(len(o.data.polygons) for o in mesh_objs)\n"
                "    all_tagged = all(o.get('rcl_hard_surface_geometry_preset') == preset_id for o in objs)\n"
                "    results[preset_id] = {\n"
                "        'object_count': len(objs),\n"
                "        'mesh_count': len(mesh_objs),\n"
                "        'total_verts': total_verts,\n"
                "        'total_polys': total_polys,\n"
                "        'all_tagged': all_tagged,\n"
                "        'panel_anchor_bolt_count': len([o for o in objs if o.get('rcl_service_connector_component') == 'panel_anchor_bolt']),\n"
                "    }\n"
                "    for o in objs:\n"
                "        o_type = o.type\n"
                "        d = o.data\n"
                "        bpy.data.objects.remove(o, do_unlink=True)\n"
                "        if d and d.users == 0:\n"
                "            if o_type == 'MESH':\n"
                "                bpy.data.meshes.remove(d)\n"
                "            elif o_type == 'CURVE':\n"
                "                bpy.data.curves.remove(d)\n"
                "print('__JSON_START__' + json.dumps(results) + '__JSON_END__')\n"
            )
            proc = subprocess.run(
                [str(self.blender_exe), "--background", "--factory-startup", "--python-expr", script],
                capture_output=True,
                text=True,
                cwd=str(REPO_ROOT),
            )
            self.assertEqual(proc.returncode, 0, f"Blender execution failed:\n{proc.stderr}\n{proc.stdout}")
            self.assertIn(
                "__JSON_START__",
                proc.stdout,
                f"Blender did not emit preset results.\nSTDERR:\n{proc.stderr}\nSTDOUT:\n{proc.stdout}",
            )
            json_str = proc.stdout.split("__JSON_START__")[1].split("__JSON_END__")[0]
            return json.loads(json_str)

        num_workers = min(6, max(1, os.cpu_count() or 1))
        chunks = [
            (w, [presets_to_test[i] for i in range(w, len(presets_to_test), num_workers)])
            for w in range(num_workers)
        ]
        chunks = [c for c in chunks if c[1]]

        with ThreadPoolExecutor(max_workers=len(chunks)) as executor:
            chunk_results = list(executor.map(lambda c: run_worker_chunk(c[0], c[1]), chunks))

        data: dict[str, Any] = {}
        for chunk_res in chunk_results:
            data.update(chunk_res)

        self.assertEqual(set(data.keys()), set(GEOMETRY_PRESETS.keys()))

        for preset_id, info in data.items():
            with self.subTest(preset=preset_id):
                self.assertGreater(info["object_count"], 0)
                self.assertGreater(info["mesh_count"], 0)
                self.assertGreater(info["total_verts"], 50)
                self.assertGreater(info["total_polys"], 30)
                self.assertTrue(info["all_tagged"], f"Objects for {preset_id} were not properly tagged")
                if preset_id == COAXIAL_SERVICE_CONNECTOR_PRESET_ID:
                    self.assertEqual(info["panel_anchor_bolt_count"], 4)


if __name__ == "__main__":
    unittest.main()
