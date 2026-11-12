#!/usr/bin/env python3
"""Run Hardflow and Blender-native pilot for Canonical Level 2 component presets.

Creates an isolated review scene containing all contemporary and futuristic
military hard-surface presets with UV mapping, 4-channel vertex wear, and
optional multi-view preview renders. Outputs are written under ignored artifacts/.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
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


ARTIFACTS_DIR = ROOT / "artifacts" / "model-budget-hardflow-pilot"
PREVIEWS_DIR = ROOT / "artifacts" / "previews" / "canonical-level2-presets"
PILOT_BLEND = ARTIFACTS_DIR / "canonical-level2-presets-pilot.blend"
REPORT_JSON = ARTIFACTS_DIR / "canonical-level2-presets-report.json"


def _hardflow_callbacks():
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
    # 1. Isometric contemporary camera (focused on Y = +1.6 and +3.2 rows)
    bpy.ops.object.camera_add(location=(0.0, -4.2, 5.2), rotation=(math.radians(52.0), 0.0, 0.0))
    cam_contemp = bpy.context.object
    cam_contemp.name = "Cam_Contemporary_Iso"
    cam_contemp.data.type = "PERSP"
    cam_contemp.data.lens = 28.0
    cameras["contemporary_iso"] = cam_contemp

    # 2. Isometric futuristic camera (focused on Y = -1.6 and -3.2 rows)
    bpy.ops.object.camera_add(location=(0.0, -7.4, 5.0), rotation=(math.radians(52.0), 0.0, 0.0))
    cam_future = bpy.context.object
    cam_future.name = "Cam_Futuristic_Iso"
    cam_future.data.type = "PERSP"
    cam_future.data.lens = 28.0
    cameras["futuristic_iso"] = cam_future

    # 3. Top orthographic camera
    bpy.ops.object.camera_add(location=(0.0, 0.0, 14.0), rotation=(0.0, 0.0, 0.0))
    cam_top = bpy.context.object
    cam_top.name = "Cam_All_Top"
    cam_top.data.type = "ORTHO"
    cam_top.data.ortho_scale = 13.5
    cameras["all_top"] = cam_top

    # 4. Front elevation camera
    bpy.ops.object.camera_add(location=(0.0, -11.5, 2.8), rotation=(math.radians(78.0), 0.0, 0.0))
    cam_front = bpy.context.object
    cam_front.name = "Cam_All_Front"
    cam_front.data.type = "PERSP"
    cam_front.data.lens = 32.0
    cameras["all_front"] = cam_front

    return cameras


def render_preview(scene: bpy.types.Scene, camera: bpy.types.Object, output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    scene.camera = camera
    scene.render.resolution_x = 960
    scene.render.resolution_y = 540
    scene.render.filepath = str(output_path)
    bpy.ops.render.render(write_still=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--use-hardflow", action="store_true", help="Use Hardflow 1.23.0 booleans and smart sharpen.")
    parser.add_argument("--render-previews", action="store_true", help="Render camera previews to artifacts/previews.")
    raw_args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(raw_args)


def run_pilot(use_hardflow: bool = False, render_previews: bool = False) -> dict[str, object]:
    ARTIFACTS_DIR.mkdir(parents=True, exist_ok=True)
    PREVIEWS_DIR.mkdir(parents=True, exist_ok=True)

    hf_bool, hf_bevel = (None, None)
    if use_hardflow:
        hf_bool, hf_bevel = _hardflow_callbacks()

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

    all_specs = [
        ("contemporary", contemporary_specs),
        ("futuristic", futuristic_specs),
        ("functional_contemporary", functional_contemporary_specs),
        ("functional_futuristic", functional_futuristic_specs),
    ]

    preset_reports = {}
    all_created_objects: list[bpy.types.Object] = []

    for group_name, specs in all_specs:
        group_collection = bpy.data.collections.new(f"Pilot_Group_{group_name.capitalize()}")
        scene.collection.children.link(group_collection)

        for preset_id, config, armor_mat in specs:
            preset_col = bpy.data.collections.new(f"Preset_{preset_id}")
            group_collection.children.link(preset_col)

            creator = hsg.GEOMETRY_PRESETS[preset_id]
            kwargs = {
                "prefix": f"Pilot_{preset_id}",
                "config": config,
                "armor_material": armor_mat,
                "hardware_material": mat_gunmetal,
                "hardflow_boolean_apply": hf_bool,
                "hardflow_bevel": hf_bevel,
            }
            if "accent_material" in creator.__code__.co_varnames:
                kwargs["accent_material"] = mat_accent

            objects = creator(**kwargs)
            for obj in objects:
                for col in list(obj.users_collection):
                    col.objects.unlink(obj)
                preset_col.objects.link(obj)

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
                "hardflowEnabled": use_hardflow,
                "uvMapPresent": all(o.data.uv_layers.get("UVMap") is not None for o in mesh_objs),
                "vertexWearColorPresent": all(o.data.color_attributes.get("Color") is not None for o in mesh_objs),
            }
            all_created_objects.extend(objects)

    cameras = setup_lighting_and_cameras()
    render_paths = {}

    if render_previews:
        previews = [
            ("contemporary_iso", PREVIEWS_DIR / "canonical_level2_contemporary_presets_iso.png"),
            ("futuristic_iso", PREVIEWS_DIR / "canonical_level2_futuristic_presets_iso.png"),
            ("all_top", PREVIEWS_DIR / "canonical_level2_all_presets_top.png"),
            ("all_front", PREVIEWS_DIR / "canonical_level2_all_presets_front.png"),
        ]
        for cam_key, out_path in previews:
            render_preview(scene, cameras[cam_key], out_path)
            render_paths[cam_key] = str(out_path.relative_to(ROOT))

    bpy.ops.wm.save_as_mainfile(filepath=str(PILOT_BLEND))

    summary = {
        "timestampUtc": datetime.now(timezone.utc).isoformat(),
        "blenderVersion": bpy.app.version_string,
        "useHardflow": use_hardflow,
        "totalPresets": len(preset_reports),
        "contemporaryPresets": sum(1 for p in preset_reports.values() if p["style"] == "contemporary_military"),
        "futuristicPresets": sum(1 for p in preset_reports.values() if p["style"] == "futuristic_military"),
        "totalObjects": len(all_created_objects),
        "sourceBlend": str(PILOT_BLEND.relative_to(ROOT)),
        "renderedPreviews": render_paths,
        "presets": preset_reports,
    }

    REPORT_JSON.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"Pilot blend saved to: {PILOT_BLEND}")
    print(f"Report written to: {REPORT_JSON}")
    return summary


def main() -> None:
    args = parse_args()
    summary = run_pilot(use_hardflow=args.use_hardflow, render_previews=args.render_previews)
    print(f"PASS: Generated {summary['totalPresets']} canonical Level 2 presets ({summary['contemporaryPresets']} contemporary, {summary['futuristicPresets']} futuristic).")


if __name__ == "__main__":
    main()
