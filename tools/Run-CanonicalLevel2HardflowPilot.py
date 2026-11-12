#!/usr/bin/env python3
"""Run Hardflow and Blender-native pilot for Canonical Level 2 component presets.

Creates an isolated review scene containing all contemporary and futuristic
military hard-surface presets with optional UV mapping, 4-channel vertex wear,
and multi-view preview renders. Geometry-only previews omit the more expensive
authoring passes. Outputs are written under ignored artifacts/.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import inspect
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import RogueModelKit as rmk
import hard_surface_geometry_presets as hsg
import hard_surface_geometry_presets_detailing as hsgd


ARTIFACTS_DIR = ROOT / "artifacts" / "model-budget-hardflow-pilot"
PREVIEWS_DIR = ROOT / "artifacts" / "previews" / "canonical-level2-presets"
PILOT_BLEND = ARTIFACTS_DIR / "canonical-level2-presets-pilot.blend"
REPORT_JSON = ARTIFACTS_DIR / "canonical-level2-presets-report.json"
PILOT_GROUP_NAMES = (
    "contemporary",
    "futuristic",
    "functional_contemporary",
    "functional_futuristic",
    "detailing",
    "legacy_contemporary",
    "legacy_futuristic",
    "baseline_variant_contemporary",
    "baseline_variant_futuristic",
)


def _hardflow_callbacks(usage: dict[str, object]):
    try:
        from bl_ext.user_default.hardflow.core import boolean as hardflow_boolean
        from bl_ext.user_default.hardflow.core import geometry as hardflow_geometry
    except Exception as exc:  # noqa: BLE001
        raise RuntimeError("Hardflow 1.23.0 is not active in the project-local profile") from exc

    if not hasattr(bpy.ops.object, "hardflow_smart_sharpen"):
        raise RuntimeError("Hardflow Smart Sharpen is not registered in Blender")

    def apply_boolean(target: bpy.types.Object, cutter: bpy.types.Object) -> str:
        errors: list[str] = []
        for solver in ("EXACT", "FAST"):
            modifier = hardflow_boolean.add_boolean(target, cutter, operation="DIFFERENCE", solver=solver)
            bpy.ops.object.select_all(action="DESELECT")
            target.select_set(True)
            bpy.context.view_layer.objects.active = target
            try:
                with bpy.context.temp_override(
                    object=target,
                    active_object=target,
                    selected_objects=[target],
                    selected_editable_objects=[target],
                ):
                    bpy.ops.object.modifier_apply(modifier=modifier.name)
            except Exception as exc:  # noqa: BLE001
                errors.append(f"{solver}: {exc}")
                leftover = target.modifiers.get(modifier.name)
                if leftover is not None:
                    target.modifiers.remove(leftover)
                continue
            hardflow_geometry.cleanup_mesh(target)
            usage["booleanApplyCount"] = int(usage["booleanApplyCount"]) + 1
            usage["booleanSolvers"].append(solver)
            return solver
        raise RuntimeError(f"Hardflow boolean failed: {errors}")

    def apply_bevel(target: bpy.types.Object) -> None:
        bpy.ops.object.select_all(action="DESELECT")
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        with bpy.context.temp_override(
            object=target,
            active_object=target,
            selected_objects=[target],
            selected_editable_objects=[target],
        ):
            bpy.ops.object.hardflow_smart_sharpen()
        usage["smartSharpenCount"] = int(usage["smartSharpenCount"]) + 1

    return apply_boolean, apply_bevel


def ensure_uv_and_normals(objects: list[bpy.types.Object]) -> None:
    for obj in objects:
        if obj.type != "MESH":
            continue
        if not obj.data.uv_layers:
            bpy.ops.object.select_all(action="DESELECT")
            obj.select_set(True)
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.018)
            bpy.ops.object.mode_set(mode="OBJECT")
            obj.select_set(False)
        rmk.apply_weighted_normals(obj, weight=50, keep_sharp=True, apply_modifier=True)


def setup_lighting_and_cameras() -> dict[str, bpy.types.Object]:
    # Key sun light
    bpy.ops.object.light_add(type="SUN", location=(4.0, -5.0, 8.0))
    sun = bpy.context.object
    sun.name = "Pilot_KeySun"
    sun.data.energy = 2.4
    sun.rotation_euler = (math.radians(45.0), math.radians(20.0), math.radians(35.0))

    # Fill sun light
    bpy.ops.object.light_add(type="SUN", location=(-5.0, 4.0, 5.0))
    fill = bpy.context.object
    fill.name = "Pilot_FillSun"
    fill.data.energy = 1.0
    fill.rotation_euler = (math.radians(55.0), math.radians(-15.0), math.radians(-125.0))

    cameras = {}
    # 1. Isometric contemporary camera, framed over its complete preset rows.
    target = Vector((0.0, 3.2, 0.4))
    location = Vector((0.0, -2.0, 6.2))
    bpy.ops.object.camera_add(location=location)
    cam_contemp = bpy.context.object
    cam_contemp.name = "Cam_Contemporary_Iso"
    cam_contemp.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()
    cam_contemp.data.type = "ORTHO"
    cam_contemp.data.ortho_scale = 10.5
    cameras["contemporary_iso"] = cam_contemp

    # 2. Isometric futuristic camera, framed over its complete preset rows.
    target = Vector((0.0, -3.2, 0.4))
    location = Vector((0.0, -8.2, 6.2))
    bpy.ops.object.camera_add(location=location)
    cam_future = bpy.context.object
    cam_future.name = "Cam_Futuristic_Iso"
    cam_future.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()
    cam_future.data.type = "ORTHO"
    cam_future.data.ortho_scale = 10.5
    cameras["futuristic_iso"] = cam_future

    # 3. Top orthographic camera
    bpy.ops.object.camera_add(location=(0.0, 0.0, 14.0), rotation=(0.0, 0.0, 0.0))
    cam_top = bpy.context.object
    cam_top.name = "Cam_All_Top"
    cam_top.data.type = "ORTHO"
    cam_top.data.ortho_scale = 24.0
    cameras["all_top"] = cam_top

    # 4. Front elevation camera
    bpy.ops.object.camera_add(location=(0.0, -11.5, 2.8), rotation=(math.radians(78.0), 0.0, 0.0))
    cam_front = bpy.context.object
    cam_front.name = "Cam_All_Front"
    cam_front.data.type = "PERSP"
    cam_front.data.lens = 32.0
    cameras["all_front"] = cam_front

    # 5/6. Detailing row close-ups (presets face -Y at y=6.4).
    target = Vector((0.0, 6.4, 0.4))
    location = Vector((0.0, 1.7, 3.5))
    bpy.ops.object.camera_add(location=location)
    cam_det = bpy.context.object
    cam_det.name = "Cam_Detailing_Front"
    cam_det.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()
    cam_det.data.type = "ORTHO"
    cam_det.data.ortho_scale = 7.0
    cameras["detailing_front"] = cam_det
    location = Vector((-4.0, 3.0, 3.4))
    bpy.ops.object.camera_add(location=location)
    cam_det_iso = bpy.context.object
    cam_det_iso.name = "Cam_Detailing_Oblique"
    cam_det_iso.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()
    cam_det_iso.data.type = "ORTHO"
    cam_det_iso.data.ortho_scale = 8.0
    cameras["detailing_oblique"] = cam_det_iso

    for camera_name, target, offset in (
        ("legacy_contemporary_iso", Vector((0.0, 8.0, 0.3)), Vector((0.0, -3.8, 4.8))),
        ("legacy_futuristic_iso", Vector((0.0, -8.0, 0.3)), Vector((0.0, -3.8, 4.8))),
        ("baseline_variant_contemporary_iso", Vector((0.0, 10.4, 0.3)), Vector((0.0, -3.8, 4.8))),
        ("baseline_variant_futuristic_iso", Vector((0.0, -10.4, 0.3)), Vector((0.0, -3.8, 4.8))),
    ):
        bpy.ops.object.camera_add(location=target + offset)
        camera = bpy.context.object
        camera.name = "Cam_" + camera_name
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
        camera.data.type = "ORTHO"
        camera.data.ortho_scale = 9.5
        cameras[camera_name] = camera

    return cameras


def render_preview(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    output_path: Path,
    resolution: tuple[int, int] = (960, 540),
) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    scene.camera = camera
    scene.render.resolution_x, scene.render.resolution_y = resolution
    scene.render.filepath = str(output_path)
    bpy.ops.render.render(write_still=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--use-hardflow", action="store_true", help="Use Hardflow 1.23.0 booleans and smart sharpen.")
    parser.add_argument("--fast-preview", action="store_true", help="Skip UV and vertex-wear authoring for a geometry-only review scene.")
    parser.add_argument("--render-previews", action="store_true", help="Render camera previews to artifacts/previews.")
    parser.add_argument("--group", choices=PILOT_GROUP_NAMES, action="append", help="Limit the pilot to one or more named preset families.")
    raw_args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(raw_args)


def run_pilot(
    use_hardflow: bool = False,
    render_previews: bool = False,
    fast_preview: bool = False,
    selected_groups: list[str] | None = None,
) -> dict[str, object]:
    ARTIFACTS_DIR.mkdir(parents=True, exist_ok=True)
    PREVIEWS_DIR.mkdir(parents=True, exist_ok=True)

    hf_bool, hf_bevel = (None, None)
    hardflow_usage: dict[str, object] = {
        "booleanApplyCount": 0,
        "booleanSolvers": [],
        "smartSharpenCount": 0,
    }
    if use_hardflow:
        hf_bool, hf_bevel = _hardflow_callbacks(hardflow_usage)

    rmk.clear_scene()
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT" if hasattr(bpy.types, "RenderEngine") else "BLENDER_EEVEE"

    # Tactical PBR Materials
    mat_armor = rmk.make_material("Pilot_PrimaryArmor", (0.34, 0.38, 0.42), metallic=0.74, roughness=0.34)
    mat_future_armor = rmk.make_material("Pilot_StealthArmor", (0.16, 0.18, 0.22), metallic=0.82, roughness=0.28)
    mat_gunmetal = rmk.make_material("Pilot_HardwareGunmetal", (0.13, 0.16, 0.19), metallic=0.91, roughness=0.27)
    mat_accent = rmk.make_material("Pilot_OpticAccent", (0.12, 0.72, 0.88), metallic=0.52, roughness=0.24,
                                  emission_color=(0.12, 0.72, 0.88), emission_strength=0.8)

    # Contemporary Military Presets v2 (6 canonical Level 2 presets)
    contemporary_specs = [
        (
            hsg.APS_HARDKILL_RADAR_V2_PRESET_ID,
            hsg.ApsHardkillRadarV2Config(center=(-3.75, 1.6, 0.0)),
            mat_armor,
        ),
        (
            hsg.FUEL_AUX_JERRYCAN_RACK_V2_PRESET_ID,
            hsg.FuelAuxJerrycanRackV2Config(center=(-2.25, 1.6, 0.0)),
            mat_armor,
        ),
        (
            hsg.SLAT_ARMOR_SCREEN_V2_PRESET_ID,
            hsg.SlatArmorScreenV2Config(center=(-0.75, 1.6, 0.0)),
            mat_armor,
        ),
        (
            hsg.ORDNANCE_MINE_DISPENSER_V2_PRESET_ID,
            hsg.OrdnanceMineDispenserV2Config(center=(0.75, 1.6, 0.0)),
            mat_armor,
        ),
        (
            hsg.THERMAL_ARMORED_RADIATOR_V2_PRESET_ID,
            hsg.ThermalArmoredRadiatorV2Config(center=(2.25, 1.6, 0.0)),
            mat_armor,
        ),
        (
            hsg.AUTOCANNON_REMOTE_SPONSON_V2_PRESET_ID,
            hsg.AutocannonRemoteSponsonV2Config(center=(3.75, 1.6, 0.0)),
            mat_armor,
        ),
    ]

    # Futuristic Military Presets v2, including the Gravitas service-port detail.
    futuristic_specs = [
        (
            hsg.APS_PHASE_SHIELD_V2_PRESET_ID,
            hsg.ApsPhaseShieldV2Config(center=(-3.75, -1.6, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.PLASMA_CONTAINMENT_CELL_V2_PRESET_ID,
            hsg.PlasmaContainmentCellV2Config(center=(-2.25, -1.6, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.STEALTH_HEX_REACTIVE_TILE_V2_PRESET_ID,
            hsg.StealthHexReactiveTileV2Config(center=(-0.75, -1.6, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.PLASMA_TORPEDO_MAGAZINE_V2_PRESET_ID,
            hsg.PlasmaTorpedoMagazineV2Config(center=(0.75, -1.6, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.TACHYONIC_RADIATOR_ARRAY_V2_PRESET_ID,
            hsg.TachyonicRadiatorArrayV2Config(center=(2.25, -1.6, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.NANITE_REPAIR_DISPENSER_V2_PRESET_ID,
            hsg.NaniteRepairDispenserV2Config(center=(3.75, -1.6, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.COAXIAL_SERVICE_CONNECTOR_PRESET_ID,
            hsg.CoaxialServiceConnectorConfig(center=(0.0, -6.4, 0.0)),
            mat_future_armor,
        ),
    ]

    # Functional Canonical Presets (Contemporary & Futuristic)
    functional_contemporary_specs = [
        (
            hsg.EXCAVATION_DRILL_CARBIDE_BORER_PRESET_ID,
            hsg.ExcavationDrillCarbideBorerConfig(center=(-2.25, 3.2, 0.0)),
            mat_armor,
        ),
        (
            hsg.DOCKING_CLAMP_HYDRAULIC_ARM_PRESET_ID,
            hsg.DockingClampHydraulicArmConfig(center=(-0.75, 3.2, 0.0)),
            mat_armor,
        ),
        (
            hsg.APU_TURBOGENERATOR_SPONSON_PRESET_ID,
            hsg.ApuTurbogeneratorSponsonConfig(center=(0.75, 3.2, 0.0)),
            mat_armor,
        ),
        (
            hsg.SMOKE_GRENADE_SALVO_DISCHARGER_PRESET_ID,
            hsg.SmokeGrenadeSalvoDischargerConfig(center=(2.25, 3.2, 0.0)),
            mat_armor,
        ),
        (
            hsg.CIWS_ROTARY_POINT_DEFENSE_PRESET_ID,
            hsg.CiwsRotaryPointDefenseConfig(center=(-2.25, 4.8, 0.0)),
            mat_armor,
        ),
        (
            hsg.REINFORCED_FUEL_DRUM_DROP_RACK_PRESET_ID,
            hsg.ReinforcedFuelDrumDropRackConfig(center=(-0.75, 4.8, 0.0)),
            mat_armor,
        ),
        (
            hsg.FIELD_ENGINEERING_CRANE_WINCH_PRESET_ID,
            hsg.FieldEngineeringCraneWinchConfig(center=(0.75, 4.8, 0.0)),
            mat_armor,
        ),
        (
            hsg.MULTISPECTRAL_COUNTERMEASURE_JAMMER_PRESET_ID,
            hsg.MultispectralCountermeasureJammerConfig(center=(2.25, 4.8, 0.0)),
            mat_armor,
        ),
    ]

    functional_futuristic_specs = [
        (
            hsg.PLASMA_EXCAVATION_TORCH_PRESET_ID,
            hsg.PlasmaExcavationTorchConfig(center=(-2.25, -3.2, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.MAGNETIC_TRACTOR_DOCKING_CLAMP_PRESET_ID,
            hsg.MagneticTractorDockingClampConfig(center=(-0.75, -3.2, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.GRAVITON_RECOIL_ANCHOR_PRESET_ID,
            hsg.GravitonRecoilAnchorConfig(center=(0.75, -3.2, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.OPTRONIC_RECON_SENSOR_MAST_PRESET_ID,
            hsg.OptronicReconSensorMastConfig(center=(2.25, -3.2, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.QUAD_PULSE_LASER_CIWS_PRESET_ID,
            hsg.QuadPulseLaserCiwsConfig(center=(-2.25, -4.8, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.CRYO_FUSION_CELL_CONTAINMENT_RACK_PRESET_ID,
            hsg.CryoFusionCellContainmentRackConfig(center=(-0.75, -4.8, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.NANOFABRICATION_REPAIR_PROJECTOR_PRESET_ID,
            hsg.NanofabricationRepairProjectorConfig(center=(0.75, -4.8, 0.0)),
            mat_future_armor,
        ),
        (
            hsg.FLUX_DEFLECTION_SHIELD_PROJECTOR_PRESET_ID,
            hsg.FluxDeflectionShieldProjectorConfig(center=(2.25, -4.8, 0.0)),
            mat_future_armor,
        ),
    ]

    # Baseline geometry presets omitted by the initial v2/functional pilot.
    # These remain separate catalog options from the polished v2 variants.
    legacy_contemporary_specs = [
        (hsg.SERVICE_HATCH_PRESET_ID,
         hsg.ServiceHatchConfig(center_xy=(-3.75, 8.0), outer_size=(0.34, 0.36), support_top_z=0.0), mat_armor),
        (hsg.ERA_BRICK_PRESET_ID,
         hsg.EraBrickConfig(center=(-2.25, 8.0, 0.0), normal=(0.0, 0.0, 1.0)), mat_armor),
        (hsg.APS_PHASE_SHIELD_PRESET_ID,
         hsg.ApsPhaseShieldConfig(center=(-0.75, 8.0, 0.0)), mat_armor),
        (hsg.FUEL_AUX_JERRYCAN_RACK_PRESET_ID,
         hsg.FuelAuxJerrycanRackConfig(center=(0.75, 8.0, 0.0)), mat_armor),
        (hsg.SLAT_ARMOR_SCREEN_PRESET_ID,
         hsg.SlatArmorScreenConfig(center=(2.25, 8.0, 0.0)), mat_armor),
        (hsg.THERMAL_ARMORED_RADIATOR_PRESET_ID,
         hsg.ThermalArmoredRadiatorConfig(center=(3.75, 8.0, 0.0)), mat_armor),
    ]
    legacy_futuristic_specs = [
        (hsg.STEALTH_HEX_REACTIVE_TILE_PRESET_ID,
         hsg.StealthHexReactiveTileConfig(center=(-0.75, -8.0, 0.0)), mat_future_armor),
    ]

    # Baseline alternatives to the v2 preset families shown in their own rows,
    # so none are hidden behind another mesh in the all-catalog review scene.
    baseline_variant_contemporary_specs = [
        (hsg.APS_HARDKILL_RADAR_PRESET_ID,
         hsg.ApsHardkillRadarConfig(center=(-2.5, 10.4, 0.0)), mat_armor),
        (hsg.ORDNANCE_MINE_DISPENSER_PRESET_ID,
         hsg.OrdnanceMineDispenserConfig(center=(0.0, 10.4, 0.0)), mat_armor),
        (hsg.AUTOCANNON_REMOTE_SPONSON_PRESET_ID,
         hsg.AutocannonRemoteSponsonConfig(center=(2.5, 10.4, 0.0)), mat_armor),
    ]
    baseline_variant_futuristic_specs = [
        (hsg.PLASMA_CONTAINMENT_CELL_PRESET_ID,
         hsg.PlasmaContainmentCellConfig(center=(-3.75, -10.4, 0.0)), mat_future_armor),
        (hsg.PLASMA_TORPEDO_MAGAZINE_PRESET_ID,
         hsg.PlasmaTorpedoMagazineConfig(center=(-1.25, -10.4, 0.0)), mat_future_armor),
        (hsg.TACHYONIC_RADIATOR_ARRAY_PRESET_ID,
         hsg.TachyonicRadiatorArrayConfig(center=(1.25, -10.4, 0.0)), mat_future_armor),
        (hsg.NANITE_REPAIR_DISPENSER_PRESET_ID,
         hsg.NaniteRepairDispenserConfig(center=(3.75, -10.4, 0.0)), mat_future_armor),
    ]

    # Hard-surface detailing presets (booleans, panels, slots, grilles, bolt circles,
    # weld beads, tubes and cables): contemporary at x<0, futuristic at x>0.
    detailing_specs = [
        (hsgd.ARMOR_ACCESS_PANEL_PRESET_ID, hsgd.ArmorAccessPanelConfig(center=(-2.25, 6.4, 0.4)), mat_armor),
        (hsgd.ENGINE_DECK_LOUVER_GRILLE_PRESET_ID, hsgd.EngineDeckLouverGrilleConfig(center=(-0.75, 6.4, 0.4)), mat_armor),
        (hsgd.COMPOSITE_HEX_VENT_PANEL_PRESET_ID, hsgd.CompositeHexVentPanelConfig(center=(0.75, 6.4, 0.4)), mat_future_armor),
        (hsgd.CABLE_CONDUIT_JUNCTION_PRESET_ID, hsgd.CableConduitJunctionConfig(center=(2.25, 6.4, 0.4)), mat_future_armor),
    ]

    all_specs = [
        ("contemporary", contemporary_specs),
        ("futuristic", futuristic_specs),
        ("functional_contemporary", functional_contemporary_specs),
        ("functional_futuristic", functional_futuristic_specs),
        ("detailing", detailing_specs),
        ("legacy_contemporary", legacy_contemporary_specs),
        ("legacy_futuristic", legacy_futuristic_specs),
        ("baseline_variant_contemporary", baseline_variant_contemporary_specs),
        ("baseline_variant_futuristic", baseline_variant_futuristic_specs),
    ]
    catalog_preset_ids = {
        preset_id
        for _group_name, specs in all_specs
        for preset_id, _config, _material in specs
    }
    if catalog_preset_ids != set(hsg.GEOMETRY_PRESETS):
        missing = sorted(set(hsg.GEOMETRY_PRESETS) - catalog_preset_ids)
        extra = sorted(catalog_preset_ids - set(hsg.GEOMETRY_PRESETS))
        raise RuntimeError(f"Preset pilot specification differs from the geometry catalog: missing={missing}, extra={extra}")
    if selected_groups:
        unknown_groups = sorted(set(selected_groups) - set(PILOT_GROUP_NAMES))
        if unknown_groups:
            raise ValueError(f"Unknown preset pilot groups: {unknown_groups}")
        selected_group_set = set(selected_groups)
        specs_to_run = [(name, specs) for name, specs in all_specs if name in selected_group_set]
    else:
        selected_group_set = None
        specs_to_run = all_specs
    expected_preset_ids = {
        preset_id
        for _group_name, specs in specs_to_run
        for preset_id, _config, _material in specs
    }
    artifact_suffix = "" if selected_group_set is None else "-" + "-".join(sorted(selected_group_set))
    pilot_blend_path = ARTIFACTS_DIR / f"canonical-level2-presets{artifact_suffix}-pilot.blend"
    report_json_path = ARTIFACTS_DIR / f"canonical-level2-presets{artifact_suffix}-report.json"

    preset_reports = {}
    all_created_objects: list[bpy.types.Object] = []
    group_collections: dict[str, bpy.types.Collection] = {}

    for group_name, specs in specs_to_run:
        group_collection = bpy.data.collections.new(f"Pilot_Group_{group_name.capitalize()}")
        scene.collection.children.link(group_collection)
        group_collections[group_name] = group_collection

        for preset_id, config, armor_mat in specs:
            preset_col = bpy.data.collections.new(f"Preset_{preset_id}")
            group_collection.children.link(preset_col)

            creator = hsg.GEOMETRY_PRESETS[preset_id]
            kwargs = {
                "prefix": f"Pilot_{preset_id}",
                "config": config,
                "armor_material": armor_mat,
                "hardware_material": mat_gunmetal,
            }
            parameters = inspect.signature(creator).parameters
            hardflow_callback_supported = use_hardflow and (
                "hardflow_boolean_apply" in parameters or "hardflow_bevel" in parameters
            )
            hardflow_before = {
                "booleanApplyCount": int(hardflow_usage["booleanApplyCount"]),
                "smartSharpenCount": int(hardflow_usage["smartSharpenCount"]),
                "booleanSolverCount": len(hardflow_usage["booleanSolvers"]),
            }
            for key, value in (
                ("accent_material", mat_accent),
                ("hardflow_boolean_apply", hf_bool),
                ("hardflow_bevel", hf_bevel),
            ):
                if key in parameters:
                    kwargs[key] = value

            objects = creator(**kwargs)
            for obj in objects:
                for col in list(obj.users_collection):
                    col.objects.unlink(obj)
                preset_col.objects.link(obj)

            if not fast_preview:
                ensure_uv_and_normals(objects)

            # Localized thermal soot & weathering anchors for specific engineering roles
            soot_intensity = 0.0
            muzzle_loc = None
            exhaust_locs = None
            if preset_id in (hsg.APS_HARDKILL_RADAR_PRESET_ID, hsg.APS_HARDKILL_RADAR_V2_PRESET_ID):
                muzzle_loc = Vector(config.center) + Vector((0.0, 0.16, 0.28))
                soot_intensity = 0.85
            elif preset_id in (hsg.ORDNANCE_MINE_DISPENSER_PRESET_ID, hsg.ORDNANCE_MINE_DISPENSER_V2_PRESET_ID):
                muzzle_loc = Vector(config.center) + Vector((0.0, 0.22, 0.25))
                soot_intensity = 0.75
            elif preset_id in (hsg.AUTOCANNON_REMOTE_SPONSON_PRESET_ID, hsg.AUTOCANNON_REMOTE_SPONSON_V2_PRESET_ID):
                muzzle_loc = Vector(config.center) + Vector((0.0, 0.42, 0.20))
                soot_intensity = 0.90
            elif preset_id in (hsg.PLASMA_CONTAINMENT_CELL_PRESET_ID, hsg.PLASMA_CONTAINMENT_CELL_V2_PRESET_ID):
                exhaust_locs = [
                    Vector(config.center) + Vector((-0.135, -0.132, 0.475)),
                    Vector(config.center) + Vector((-0.135, 0.132, 0.475)),
                ]
                soot_intensity = 0.70
            elif preset_id in (hsg.PLASMA_TORPEDO_MAGAZINE_PRESET_ID, hsg.PLASMA_TORPEDO_MAGAZINE_V2_PRESET_ID):
                exhaust_locs = [
                    Vector(config.center) + Vector((0.0, -0.18, 0.16)),
                ]
                soot_intensity = 0.75
            elif preset_id in (hsg.TACHYONIC_RADIATOR_ARRAY_PRESET_ID, hsg.TACHYONIC_RADIATOR_ARRAY_V2_PRESET_ID):
                exhaust_locs = [
                    Vector(config.center) + Vector((0.08, 0.0, 0.18)),
                ]
                soot_intensity = 0.45
            elif preset_id in (hsg.NANITE_REPAIR_DISPENSER_PRESET_ID, hsg.NANITE_REPAIR_DISPENSER_V2_PRESET_ID):
                muzzle_loc = Vector(config.center) + Vector((0.0, 0.24, 0.15))
                soot_intensity = 0.50
            elif preset_id == hsg.EXCAVATION_DRILL_CARBIDE_BORER_PRESET_ID:
                muzzle_loc = Vector(config.center) + Vector((0.0, -0.32, 0.12))
                soot_intensity = 0.65
            elif preset_id == hsg.APU_TURBOGENERATOR_SPONSON_PRESET_ID:
                exhaust_locs = [Vector(config.center) + Vector((0.10, -0.15, 0.20))]
                soot_intensity = 0.85
            elif preset_id == hsg.SMOKE_GRENADE_SALVO_DISCHARGER_PRESET_ID:
                muzzle_loc = Vector(config.center) + Vector((0.0, 0.05, 0.18))
                soot_intensity = 0.80
            elif preset_id == hsg.PLASMA_EXCAVATION_TORCH_PRESET_ID:
                muzzle_loc = Vector(config.center) + Vector((0.0, -0.28, 0.14))
                soot_intensity = 0.60
            elif preset_id == hsg.GRAVITON_RECOIL_ANCHOR_PRESET_ID:
                exhaust_locs = [
                    Vector(config.center) + Vector((-0.16, 0.0, 0.14)),
                    Vector(config.center) + Vector((0.16, 0.0, 0.14)),
                ]
                soot_intensity = 0.55

            if not fast_preview:
                rmk.bake_advanced_vertex_shading(
                    objects,
                    wear_level="field_tested",
                    soot_intensity=soot_intensity,
                    muzzle_loc=muzzle_loc,
                    exhaust_locs=exhaust_locs,
                    team_mask_materials=[armor_mat],
                    structural_wear_mask=True,
                )

            mesh_objs = [o for o in objects if o.type == "MESH"]
            verts = sum(len(o.data.vertices) for o in mesh_objs)
            polys = sum(len(o.data.polygons) for o in mesh_objs)
            tris = sum(len(o.data.loop_triangles) if o.data.loop_triangles else len(o.data.polygons) * 2 for o in mesh_objs)

            # Measure overall bounding box
            all_pts = [o.matrix_world @ Vector(c) for o in mesh_objs for c in o.bound_box]
            min_pt = Vector((min(p[i] for p in all_pts) for i in range(3)))
            max_pt = Vector((max(p[i] for p in all_pts) for i in range(3)))
            dims = max_pt - min_pt
            boolean_count = int(hardflow_usage["booleanApplyCount"]) - hardflow_before["booleanApplyCount"]
            sharpen_count = int(hardflow_usage["smartSharpenCount"]) - hardflow_before["smartSharpenCount"]
            boolean_solvers = hardflow_usage["booleanSolvers"][hardflow_before["booleanSolverCount"]:]

            preset_reports[preset_id] = {
                "presetId": preset_id,
                "style": hsg.PRESET_STYLES[preset_id],
                "canonicalSlot": hsg.PRESET_SLOTS[preset_id],
                "displayName": hsg.PRESET_METADATA[preset_id]["displayName"],
                "engineeringRole": hsg.PRESET_METADATA[preset_id]["engineeringRole"],
                "config": config.to_dict(),
                "objectCount": len(objects),
                "meshCount": len(mesh_objs),
                "totalVertices": verts,
                "totalPolygons": polys,
                "estimatedTriangles": tris,
                "worldDimensions": [round(dims.x, 4), round(dims.y, 4), round(dims.z, 4)],
                "hardflowCallbackSupported": hardflow_callback_supported,
                "hardflowUsed": boolean_count > 0 or sharpen_count > 0,
                "hardflowBooleanOperations": boolean_count,
                "hardflowBooleanSolvers": boolean_solvers,
                "hardflowSmartSharpenOperations": sharpen_count,
                "previewMode": "geometry_only" if fast_preview else "full_authoring",
                "uvMapPresent": all(o.data.uv_layers.get("UVMap") is not None for o in mesh_objs),
                "vertexWearColorPresent": all(o.data.color_attributes.get("Color") is not None for o in mesh_objs),
            }
            all_created_objects.extend(objects)

    cameras = setup_lighting_and_cameras()
    if set(preset_reports) != expected_preset_ids:
        missing = sorted(expected_preset_ids - set(preset_reports))
        extra = sorted(set(preset_reports) - expected_preset_ids)
        raise RuntimeError(f"Preset pilot does not cover the complete geometry catalog: missing={missing}, extra={extra}")
    render_paths = {}

    if render_previews:
        previews = [
            ("contemporary_iso", PREVIEWS_DIR / "canonical_level2_contemporary_presets_iso.png"),
            ("futuristic_iso", PREVIEWS_DIR / "canonical_level2_futuristic_presets_iso.png"),
            ("all_top", PREVIEWS_DIR / "canonical_level2_all_presets_top.png"),
            ("all_front", PREVIEWS_DIR / "canonical_level2_all_presets_front.png"),
            ("detailing_front", PREVIEWS_DIR / "canonical_level2_detailing_front.png"),
            ("detailing_oblique", PREVIEWS_DIR / "canonical_level2_detailing_oblique.png"),
            ("legacy_contemporary_iso", PREVIEWS_DIR / "canonical_level2_legacy_contemporary_iso.png"),
            ("legacy_futuristic_iso", PREVIEWS_DIR / "canonical_level2_legacy_futuristic_iso.png"),
            ("baseline_variant_contemporary_iso", PREVIEWS_DIR / "canonical_level2_baseline_variant_contemporary_iso.png"),
            ("baseline_variant_futuristic_iso", PREVIEWS_DIR / "canonical_level2_baseline_variant_futuristic_iso.png"),
        ]
        preview_groups = {
            "contemporary_iso": {"contemporary", "functional_contemporary"},
            "futuristic_iso": {"futuristic", "functional_futuristic"},
            "detailing_front": {"detailing"},
            "detailing_oblique": {"detailing"},
            "legacy_contemporary_iso": {"legacy_contemporary"},
            "legacy_futuristic_iso": {"legacy_futuristic"},
            "baseline_variant_contemporary_iso": {"baseline_variant_contemporary"},
            "baseline_variant_futuristic_iso": {"baseline_variant_futuristic"},
        }
        if selected_group_set is not None:
            previews = [
                (camera_key, output_path)
                for camera_key, output_path in previews
                if preview_groups.get(camera_key, set()) & selected_group_set
            ]
        for cam_key, out_path in previews:
            resolution = (1200, 1200) if cam_key == "all_top" else (960, 540)
            visible_groups = preview_groups.get(cam_key)
            previous_visibility = {
                name: collection.hide_render for name, collection in group_collections.items()
            }
            try:
                if visible_groups is not None:
                    for name, collection in group_collections.items():
                        collection.hide_render = name not in visible_groups
                render_preview(scene, cameras[cam_key], out_path, resolution=resolution)
            finally:
                for name, collection in group_collections.items():
                    collection.hide_render = previous_visibility[name]
            render_paths[cam_key] = str(out_path.relative_to(ROOT))

    bpy.ops.wm.save_as_mainfile(filepath=str(pilot_blend_path))

    summary = {
        "timestampUtc": datetime.now(timezone.utc).isoformat(),
        "blenderVersion": bpy.app.version_string,
        "useHardflow": use_hardflow,
        "previewMode": "geometry_only" if fast_preview else "full_authoring",
        "selectedGroups": sorted(selected_group_set) if selected_group_set is not None else list(PILOT_GROUP_NAMES),
        "totalPresets": len(preset_reports),
        "catalogPresetCount": len(catalog_preset_ids),
        "catalogCoverage": len(preset_reports) / len(catalog_preset_ids),
        "completeCatalog": set(preset_reports) == catalog_preset_ids,
        "contemporaryPresets": sum(1 for p in preset_reports.values() if p["style"] == "contemporary_military"),
        "futuristicPresets": sum(1 for p in preset_reports.values() if p["style"] == "futuristic_military"),
        "totalObjects": len(all_created_objects),
        "sourceBlend": str(pilot_blend_path.relative_to(ROOT)),
        "renderedPreviews": render_paths,
        "presets": preset_reports,
    }

    report_json_path.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"Pilot blend saved to: {pilot_blend_path}")
    print(f"Report written to: {report_json_path}")
    return summary


def main() -> None:
    args = parse_args()
    summary = run_pilot(
        use_hardflow=args.use_hardflow,
        render_previews=args.render_previews,
        fast_preview=args.fast_preview,
        selected_groups=args.group,
    )
    print(
        f"PASS: Generated {summary['totalPresets']} canonical Level 2 presets "
        f"({summary['catalogCoverage']:.1%} catalog coverage; complete={summary['completeCatalog']})."
    )


if __name__ == "__main__":
    main()
