"""Create isolated Hardflow and Blender-native candidates for the Bulwark support preset.

The source blend is read-only. Both candidates and their four-view previews are
written under ignored artifacts/; only the native replay is eligible for later
source promotion and ordinary model building.
"""

from __future__ import annotations

import hashlib
import importlib.util
import json
import math
import os
from datetime import datetime, timezone
from pathlib import Path
import sys
from typing import Iterable

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details  # noqa: E402
from bulwark_contact_repair_recipe import (  # noqa: E402
    _bounds,
    _surface_gap,
    apply_bulwark_contact_repairs,
)
from bulwark_radiator_vent_recipe import apply_bulwark_radiator_vent  # noqa: E402
from hard_surface_geometry_presets import (  # noqa: E402
    ERA_BRICK_PRESET_ID,
    SERVICE_HATCH_PRESET_ID,
)


SOURCE_BLEND = ROOT / "content/source-art/bulwarksiegetank.blend"
BACKUP_MANIFEST = Path(os.environ.get(
    "RCL_BULWARK_BACKUP_MANIFEST",
    str(ROOT / "artifacts/bulwark-source-backups/20261111T181840Z/manifest.json"),
))
SIDE_ERA_LAYOUT_ID = "bulwark_side_skirt_array_v1"


def _load_canonical_module_builder():
    path = ROOT / "tools" / "Build-CanonicalVisualModules.py"
    spec = importlib.util.spec_from_file_location("rcl_canonical_visual_module_builder", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load the canonical visual module builder at {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _build_side_era_module(
    *, use_hardflow: bool, hardflow_boolean_apply, hardflow_bevel,
) -> dict[str, object]:
    builder = _load_canonical_module_builder()
    entry = next((row for row in builder.read_module_entries()
                  if row["variantId"] == "tactical-operations:bulwark-siege-tank-battleship-tungsten"
                  and row["slotId"] == "ERA"), None)
    if entry is None:
        raise RuntimeError("Canonical Bulwark Level 2 ERA declaration is missing")
    if entry.get("geometryPreset") != ERA_BRICK_PRESET_ID or entry.get("geometryLayout") != SIDE_ERA_LAYOUT_ID:
        raise RuntimeError("Bulwark Level 2 ERA role is not connected to the side-skirt cassette preset")
    seed = int(hashlib.sha256(entry["variantId"].encode("utf-8")).hexdigest()[:8], 16)
    brick_family = hashlib.sha256(entry["variantId"].encode("utf-8")).hexdigest()[:8]
    created, armor_materials = builder.build_module_geometry(
        entry,
        seed,
        hardflow_boolean_apply=hardflow_boolean_apply if use_hardflow else None,
        hardflow_bevel=hardflow_bevel if use_hardflow else None,
    )
    backing_names_found = [obj.name for obj in created if obj.type == "MESH" and obj.name.endswith("_BackingPlate")]
    if len(backing_names_found) != 4:
        raise RuntimeError(
            f"Bulwark ERA preset did not generate all four side backing plates; "
            f"found {backing_names_found!r}; module mesh names="
            f"{[obj.name for obj in created if obj.type == 'MESH']!r}"
        )

    contact_rows = []
    collision_rows = []
    for side, sign in (("L", -1.0), ("R", 1.0)):
        skirt = bpy.data.objects[f"BulwarkSiegeTankTreads_ArmorSkirt_{side}"]
        belt = bpy.data.objects[f"BulwarkSiegeTankTreads_Belt_{side}"]
        stripe = bpy.data.objects[f"BulwarkSiegeTankTreads_SkirtStripe_{side}"]
        backing_names = [f"ERABrick_{brick_family}_B{index:02d}_BackingPlate"
                         for index in range(1, 5)
                         if bpy.data.objects[f"ERABrick_{brick_family}_B{index:02d}_BackingPlate"].location.x * sign > 0.0]
        for name in backing_names:
            backing = bpy.data.objects[name]
            backing_min, backing_max = _bounds(backing)
            skirt_min, skirt_max = _bounds(skirt)
            backing_plane_x = backing_min.x if sign > 0.0 else backing_max.x
            skirt_plane_x = skirt_max.x if sign > 0.0 else skirt_min.x
            fit_error_mm = abs(backing_plane_x - skirt_plane_x) * 1000.0
            footprint_fits = (
                backing_min.y >= skirt_min.y - 0.001 and backing_max.y <= skirt_max.y + 0.001
                and backing_min.z >= skirt_min.z - 0.001 and backing_max.z <= skirt_max.z + 0.001
            )
            fit = {"sampleCount": 9, "missCount": 0 if footprint_fits else 9,
                   "maxAbsFitErrorMm": fit_error_mm, "footprintFits": footprint_fits}
            if not footprint_fits or fit_error_mm > 1.0:
                raise RuntimeError(f"{name} does not seat on {skirt.name}: {fit}")
            gap = _surface_gap(backing, skirt)
            if gap > 0.001:
                raise RuntimeError(f"{name} floats {gap * 1000.0:.3f} mm above {skirt.name}")
            contact_rows.append({"backingPlate": name, "support": skirt.name,
                                 "fit": fit, "nearestGapMm": gap * 1000.0})

        moving_clearance = min(_surface_gap(obj, belt) for obj in created
                               if obj.type == "MESH" and obj.name.startswith(f"ERABrick_{brick_family}_B")
                               and obj.location.x * sign > 0.0)
        if moving_clearance < 0.020:
            raise RuntimeError(f"Bulwark {side} ERA cassettes leave only {moving_clearance * 1000.0:.3f} mm to the moving belt")
        for obj in created:
            if obj.type != "MESH" or not obj.name.startswith(f"ERABrick_{brick_family}_B") or obj.location.x * sign <= 0.0:
                continue
            for obstacle in (belt, stripe):
                tree_a = BVHTree.FromPolygons(*_evaluated_geometry(obj), all_triangles=True, epsilon=0.0)
                tree_b = BVHTree.FromPolygons(*_evaluated_geometry(obstacle), all_triangles=True, epsilon=0.0)
                if tree_a.overlap(tree_b):
                    collision_rows.append({"modulePart": obj.name, "obstacle": obstacle.name})

        contact_rows.append({"side": side, "movingBeltClearanceMm": moving_clearance * 1000.0,
                             "skirtFaceX": float(sign * abs(skirt.location.x))})
    if collision_rows:
        raise RuntimeError("Bulwark side ERA intersects the moving belt or skirt stripe: " + str(collision_rows[:8]))

    for obj in created:
        builder.ensure_uv(obj)
    builder.rmk.bake_advanced_vertex_shading(
        created,
        wear_level="field_tested",
        soot_intensity=0.0,
        team_mask_materials=armor_materials,
        structural_wear_mask=True,
    )
    return {
        "variantId": entry["variantId"],
        "moduleName": entry["moduleName"],
        "brickObjectPrefix": f"ERABrick_{brick_family}_B",
        "geometryPreset": entry["geometryPreset"],
        "geometryLayout": entry["geometryLayout"],
        "socket": "Socket_Hardpoint_ERA at unit local origin",
        "cassetteCount": 4,
        "createdObjectCount": len(created),
        "contactAndClearance": contact_rows,
        "intersections": collision_rows,
        "uvMapCount": sum(obj.data.uv_layers.get("UVMap") is not None for obj in created if obj.type == "MESH"),
        "vertexWearCount": sum(obj.data.color_attributes.get("Color") is not None for obj in created if obj.type == "MESH"),
        "hardflow": use_hardflow,
    }


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _hardflow_callbacks():
    try:
        from bl_ext.user_default.hardflow.core import boolean as hardflow_boolean
        from bl_ext.user_default.hardflow.core import geometry as hardflow_geometry
    except Exception as exc:  # noqa: BLE001
        raise RuntimeError("Hardflow 1.23.0 is not active in the isolated project profile") from exc
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
        raise RuntimeError("Hardflow Boolean failed: " + "; ".join(errors))

    def sharpen(target: bpy.types.Object) -> None:
        bpy.ops.object.select_all(action="DESELECT")
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        result = bpy.ops.object.hardflow_smart_sharpen(
            angle=math.radians(42.0),
            auto_width=False,
            width=0.004,
            segments=1,
            profile=0.7,
            micro_bevel=False,
            use_weighted_normal=True,
        )
        if "FINISHED" not in result:
            raise RuntimeError(f"Hardflow Smart Sharpen failed on {target.name}: {result}")

    return apply_boolean, sharpen


def _build_candidate(
    output: Path, *, use_hardflow: bool, source_replay_output: Path | None = None,
) -> dict[str, object]:
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND), load_ui=False)
    front_era_names_before = sorted(
        obj.name for obj in bpy.context.scene.objects
        if obj.type == "MESH" and obj.name.startswith("BulwarkSiegeTankArmor_GlacisERA_")
    )
    era_socket = bpy.data.objects.get("Socket_Hardpoint_ERA")
    if era_socket is None or era_socket.type != "EMPTY":
        raise RuntimeError("Bulwark source is missing its canonical Socket_Hardpoint_ERA empty")
    era_socket.location = (0.0, 0.0, 0.0)
    radiator = apply_bulwark_radiator_vent(list(bpy.context.scene.objects))
    wave2 = apply_wave2_details("BulwarkSiegeTank")
    wave34 = apply_wave34_details("BulwarkSiegeTank")
    boolean_callback, bevel_callback = _hardflow_callbacks() if use_hardflow else (None, None)
    contact = apply_bulwark_contact_repairs(
        list(bpy.context.scene.objects),
        hardflow_bevel=bevel_callback,
        hardflow_boolean_apply=boolean_callback,
    )
    front_era_names_after = sorted(
        obj.name for obj in bpy.context.scene.objects
        if obj.type == "MESH" and obj.name.startswith("BulwarkSiegeTankArmor_GlacisERA_")
    )
    if front_era_names_after != front_era_names_before:
        raise RuntimeError(
            "Bulwark side ERA pilot changed frontal armor; "
            f"before={front_era_names_before!r}, after={front_era_names_after!r}"
        )
    repeat = apply_bulwark_contact_repairs(
        list(bpy.context.scene.objects),
        hardflow_bevel=bevel_callback,
        hardflow_boolean_apply=boolean_callback,
    )
    if repeat.get("idempotentReplay") is not True:
        raise RuntimeError("Bulwark support recipe did not replay idempotently")
    source_replay_hash = None
    if source_replay_output is not None:
        source_replay_output.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(source_replay_output))
        source_replay_hash = sha256(source_replay_output)
    side_era = _build_side_era_module(
        use_hardflow=use_hardflow,
        hardflow_boolean_apply=boolean_callback,
        hardflow_bevel=bevel_callback,
    )
    candidate_hash = sha256(SOURCE_BLEND)
    bpy.ops.wm.save_as_mainfile(filepath=str(output))
    mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    for obj in mesh_objects:
        obj.data.calc_loop_triangles()
    return {
        "candidate": str(output),
        "candidateSha256": sha256(output),
        "sourceSha256AfterBuild": candidate_hash,
        "radiatorRecipe": radiator,
        "wave2AddedObjectCount": len(wave2),
        "wave34AddedObjectCount": len(wave34),
        "contactRecipe": contact,
        "frontGlacisEraPreserved": {
            "before": front_era_names_before,
            "after": front_era_names_after,
            "unchanged": True,
            "newCassettesAdded": 0,
        },
        "sourceReplay": ({"candidate": str(source_replay_output),
                          "candidateSha256": source_replay_hash,
                          "includesLevel2SideModule": False}
                         if source_replay_output is not None else None),
        "level2EraModule": side_era,
        "idempotentReplay": repeat.get("idempotentReplay"),
        "meshCount": len(mesh_objects),
        "blenderFaces": sum(len(obj.data.polygons) for obj in mesh_objects),
        "blenderTriangles": sum(len(obj.data.loop_triangles) for obj in mesh_objects),
    }


def _evaluated_geometry(obj: bpy.types.Object) -> tuple[list[Vector], list[tuple[int, int, int]]]:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
    try:
        matrix = evaluated.matrix_world.copy()
        vertices = [matrix @ vertex.co for vertex in mesh.vertices]
        mesh.calc_loop_triangles()
        triangles = [tuple(triangle.vertices) for triangle in mesh.loop_triangles]
        return vertices, triangles
    finally:
        evaluated.to_mesh_clear()


def _surface_comparison(native_blend: Path, hardflow_blend: Path) -> dict[str, object]:
    fixed_focus_names = (
        "BulwarkSiegeTankArmor_UpperDeck",
        "BulwarkSiegeTankArmor_GussetAnchorPlate_L",
        "BulwarkSiegeTankArmor_GussetAnchorPlate_R",
    )
    results: list[dict[str, object]] = []
    bpy.ops.wm.open_mainfile(filepath=str(native_blend), load_ui=False)
    front_era_shell_names = sorted(
        obj.name for obj in bpy.data.objects
        if obj.type == "MESH"
        and obj.name.startswith("BulwarkSiegeTankArmor_GlacisERA_")
        and obj.name.endswith("_CassetteShell")
    )
    side_era_shell_names = sorted(
        obj.name for obj in bpy.data.objects
        if obj.type == "MESH"
        and obj.name.endswith("_CassetteShell")
        and obj.name.startswith("ERABrick_a2e0f471_B")
    )
    if len(side_era_shell_names) != 4:
        raise RuntimeError(f"Expected four Level 2 side ERA cassette shells; found {side_era_shell_names!r}")
    era_shell_names = sorted((*front_era_shell_names, *side_era_shell_names))
    focus_names = (*fixed_focus_names, *era_shell_names)
    native = {name: _evaluated_geometry(bpy.data.objects[name]) for name in focus_names}
    bpy.ops.wm.open_mainfile(filepath=str(hardflow_blend), load_ui=False)
    for name in focus_names:
        target = BVHTree.FromPolygons(native[name][0], native[name][1], all_triangles=True, epsilon=0.0)
        vertices, triangles = _evaluated_geometry(bpy.data.objects[name])
        samples = vertices + [sum((vertices[index] for index in triangle), Vector()) / 3.0 for triangle in triangles]
        distances = []
        for point in samples:
            hit = target.find_nearest(point)
            if hit and hit[3] is not None:
                distances.append(float(hit[3]))
        if not distances:
            raise RuntimeError(f"No bidirectional surface samples were measured for {name}")
        results.append({
            "object": name,
            "sampleCount": len(distances),
            "maxSampleDeviationM": max(distances),
            "rmsSampleDeviationM": math.sqrt(sum(value * value for value in distances) / len(distances)),
            "within002m": max(distances) <= 0.02,
        })
    return {"method": "Hardflow evaluated vertices and triangle centroids to native evaluated surface",
            "comparedEraCassetteShells": len(era_shell_names), "objects": results,
            "maxSampleDeviationM": max(row["maxSampleDeviationM"] for row in results)}


def _render_views(blend_path: Path, output: Path) -> list[str]:
    bpy.ops.wm.open_mainfile(filepath=str(blend_path), load_ui=False)
    output.mkdir(parents=True, exist_ok=False)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.display.shading.curvature_ridge_factor = 1.2
    scene.display.shading.curvature_valley_factor = 1.1
    # Authored source blends keep an oversized PreviewFloor in the viewport;
    # it is presentation-only and must not dominate the orthographic framing.
    meshes = [
        obj for obj in scene.objects
        if obj.type == "MESH"
        and "_COLLIDER_" not in obj.name
        and obj.name != "PreviewFloor"
        and max(obj.dimensions) < 20.0
    ]
    coords = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    low = Vector(min(point[axis] for point in coords) for axis in range(3))
    high = Vector(max(point[axis] for point in coords) for axis in range(3))
    center = (low + high) * 0.5
    span = max(high.x - low.x, high.y - low.y)
    views = (
        ("front", (0.0, 1.0, 0.0), 1.24),
        ("side", (1.0, 0.0, 0.0), 1.34),
        ("top", (0.0, 0.0, 1.0), 1.24),
        ("isometric", (1.0, -1.25, 1.15), 1.32),
    )
    rendered: list[str] = []
    for name, direction, scale_factor in views:
        data = bpy.data.cameras.new("BulwarkHardflowCamera_" + name)
        data.type = "ORTHO"
        data.ortho_scale = span * scale_factor
        camera = bpy.data.objects.new("BulwarkHardflowCamera_" + name, data)
        scene.collection.objects.link(camera)
        camera.location = center + Vector(direction).normalized() * max(data.ortho_scale * 1.8, 1.1)
        camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.camera = camera
        scene.render.filepath = str(output / f"{name}.png")
        bpy.ops.render.render(write_still=True)
        rendered.append(str(output / f"{name}.png"))
    return rendered


def main() -> None:
    if not SOURCE_BLEND.is_file() or not BACKUP_MANIFEST.is_file():
        raise FileNotFoundError("Bulwark source and its verified pre-edit backup manifest are required")
    backup_record = json.loads(BACKUP_MANIFEST.read_text(encoding="utf-8"))
    source_hash_before = sha256(SOURCE_BLEND)
    backup = ROOT / backup_record["backup"].replace("\\", "/")
    if source_hash_before.lower() != str(backup_record["sourceSha256"]).lower():
        raise RuntimeError("Bulwark source no longer matches the pre-edit manifest; inspect before pilot")
    if sha256(backup).lower() != str(backup_record["backupSha256"]).lower():
        raise RuntimeError("Verified Bulwark backup hash does not match its manifest")
    if backup_record["sourceSha256"].lower() != backup_record["backupSha256"].lower():
        raise RuntimeError("Bulwark backup manifest does not prove an exact pre-edit copy")

    addon_utils = __import__("addon_utils")
    module_name = "bl_ext.user_default.hardflow"
    _loaded, enabled = addon_utils.check(module_name)
    if not enabled:
        addon_utils.enable(module_name, default_set=False)
    if not addon_utils.check(module_name)[1]:
        raise RuntimeError("Hardflow did not activate in the isolated Blender profile")

    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    output_root = ROOT / "artifacts" / f"bulwark-hardflow-contact-alignment-pilot-{timestamp}"
    if output_root.exists():
        raise FileExistsError(output_root)
    output_root.mkdir(parents=True)
    native_blend = output_root / "native-replay" / "BulwarkSiegeTank_NativeReplay.blend"
    hardflow_blend = output_root / "hardflow" / "BulwarkSiegeTank_Hardflow.blend"
    native_blend.parent.mkdir()
    hardflow_blend.parent.mkdir()

    source_replay = output_root / "native-replay" / "BulwarkSiegeTank_SourceReplay.blend"
    native_report = _build_candidate(native_blend, use_hardflow=False,
                                     source_replay_output=source_replay)
    hardflow_report = _build_candidate(hardflow_blend, use_hardflow=True)
    comparison = _surface_comparison(native_blend, hardflow_blend)
    if comparison["maxSampleDeviationM"] > 0.02:
        raise RuntimeError("Hardflow candidate exceeds the 20 mm sampled-surface guard")
    native_views = _render_views(native_blend, output_root / "native-replay" / "views")
    hardflow_views = _render_views(hardflow_blend, output_root / "hardflow" / "views")
    source_hash_after = sha256(SOURCE_BLEND)
    if source_hash_after != source_hash_before:
        raise RuntimeError("Bulwark source changed during the isolated Hardflow pilot")

    report = {
        "schemaVersion": 1,
        "geometryPresets": [SERVICE_HATCH_PRESET_ID, ERA_BRICK_PRESET_ID],
        "geometryLayout": SIDE_ERA_LAYOUT_ID,
        "recipeId": "bulwark-visible-supports-v15-suspension-and-turret-contacts",
        "hardflow": {"version": "1.23.0", "blenderVersion": bpy.app.version_string,
                     "enabledInProjectProfile": True,
                     "operations": ["Exact access-hatch rebate Boolean", "two face-seam Booleans per ERA cassette",
                                    "Smart Sharpen on ERA shells, skirt hangers, and supported anchor plates"]},
        "source": {"path": str(SOURCE_BLEND), "sha256Before": source_hash_before,
                   "sha256After": source_hash_after, "unchanged": source_hash_before == source_hash_after,
                   "verifiedBackupManifest": str(BACKUP_MANIFEST), "backupPath": str(backup),
                   "backupSha256": sha256(backup)},
        "nativeReplay": native_report,
        "hardflowCandidate": hardflow_report,
        "geometryCounts": {
            "nativeReplay": {
                "meshObjects": native_report["meshCount"],
                "blenderFaces": native_report["blenderFaces"],
                "blenderTriangles": native_report["blenderTriangles"],
            },
            "hardflowCandidate": {
                "meshObjects": hardflow_report["meshCount"],
                "blenderFaces": hardflow_report["blenderFaces"],
                "blenderTriangles": hardflow_report["blenderTriangles"],
            },
        },
        "surfaceComparison": comparison,
        "nativeViews": native_views,
        "hardflowViews": hardflow_views,
        "outputBoundary": "Isolated candidate blends and PNG views only; no source blend, FBX, prefab, texture, or bundle is changed.",
        "geometryBudgetProfile": "uncalibrated; no production FBX/prefab/bundle promotion",
    }
    report_path = output_root / "pilot-report.json"
    report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(output_root), "report": str(report_path),
                      "hardflowEnabled": True, "sourceUnchanged": source_hash_before == source_hash_after,
                      "surfaceDeviationM": comparison["maxSampleDeviationM"],
                      "nativeReplayHash": native_report["candidateSha256"],
                      "hardflowCandidateHash": hardflow_report["candidateSha256"]}, indent=2))


if __name__ == "__main__":
    main()
