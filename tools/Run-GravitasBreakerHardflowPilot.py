"""Create an isolated, four-view Hardflow pilot from the local Gravitas source.

The working ``content/source-art`` blend and tracked FBX files are read-only in
this workflow. The candidate and image evidence stay under ignored artifacts.
The geometry recipe is also replayed by Build-GravitasBreakerModel.py using
Blender's built-in Boolean and bevel operators, so Hardflow remains optional.
"""

from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
import bmesh


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from gravitas_breaker_hardflow_recipe import apply_gravitas_breaker_details  # noqa: E402


SOURCE_BLEND = ROOT / "content/source-art/gravitas-breaker.blend"
MODEL_DIR = ROOT / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models"
OUTPUT_DIR = ROOT / "artifacts/model-budget-hardflow-pilot"
TARGET_NAMES = (
    "GravitasBreakerArmor_RearPowerCore",
    "GravitasBreakerTurret_BaseShell",
    "GravitasBreakerGravCannon_Gravity_BoreTube",
)
CAMERAS = (
    ("front", "PreviewCamera_FRONT"),
    ("side", "PreviewCamera_SIDE"),
    ("top", "PreviewCamera_TOP_DOWN"),
    ("isometric", "PreviewCamera_ISOMETRIC"),
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def mesh_counts(objects) -> dict[str, dict[str, int]]:
    rows: dict[str, dict[str, int]] = {}
    for obj in objects:
        if obj.type != "MESH" or "_COLLIDER_" in obj.name:
            continue
        obj.data.calc_loop_triangles()
        rows[obj.name] = {
            "blenderFaces": len(obj.data.polygons),
            "unityTriangles": len(obj.data.loop_triangles),
        }
    return rows


def geometry_totals(rows: dict[str, dict[str, int]]) -> dict[str, int]:
    return {
        "blenderFaces": sum(value["blenderFaces"] for value in rows.values()),
        "unityTriangles": sum(value["unityTriangles"] for value in rows.values()),
        "meshObjects": len(rows),
    }


def gravitas_asset_meshes(objects):
    return [
        obj for obj in objects
        if obj.type == "MESH"
        and obj.name.startswith("GravitasBreaker")
        and "_COLLIDER_" not in obj.name
    ]


def evaluated_mesh_counts(objects) -> dict[str, dict[str, int]]:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    rows: dict[str, dict[str, int]] = {}
    for obj in objects:
        if obj.type != "MESH" or "_COLLIDER_" in obj.name:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            mesh.calc_loop_triangles()
            rows[obj.name] = {
                "blenderFaces": len(mesh.polygons),
                "unityTriangles": len(mesh.loop_triangles),
            }
        finally:
            evaluated.to_mesh_clear()
    return rows


def mesh_health(objects) -> dict[str, dict[str, int]]:
    rows: dict[str, dict[str, int]] = {}
    for obj in objects:
        if obj.type != "MESH" or "_COLLIDER_" in obj.name:
            continue
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        rows[obj.name] = {
            "nonManifoldEdges": sum(1 for edge in bm.edges if not edge.is_manifold),
            "degenerateFaces": sum(1 for face in bm.faces if face.calc_area() < 1e-9),
            "looseVertices": sum(1 for vert in bm.verts if not vert.link_edges),
        }
        bm.free()
    return rows


def render_views(output_dir: Path) -> None:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.use_file_extension = True

    for view_name, camera_name in CAMERAS:
        camera = bpy.data.objects.get(camera_name)
        if camera is None or camera.type != "CAMERA":
            raise ValueError(f"Missing reference view camera: {camera_name}")
        scene.camera = camera
        scene.render.filepath = str(output_dir / f"{view_name}.png")
        bpy.ops.render.render(write_still=True)


def set_collider_render_visibility(hidden: bool) -> dict[str, bool]:
    previous: dict[str, bool] = {}
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH" and "_COLLIDER_" in obj.name:
            previous[obj.name] = obj.hide_render
            obj.hide_render = hidden
    return previous


def restore_render_visibility(previous: dict[str, bool]) -> None:
    for name, hidden in previous.items():
        obj = bpy.data.objects.get(name)
        if obj is not None:
            obj.hide_render = hidden


def main() -> None:
    if not SOURCE_BLEND.is_file():
        raise FileNotFoundError(SOURCE_BLEND)
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    tracked_assets = [
        MODEL_DIR / "GravitasBreaker.fbx",
        MODEL_DIR / "GravitasBreaker_Atlas.fbx",
        MODEL_DIR / "GravitasBreaker.gameplay.json",
    ]
    watched_hashes = {str(path.relative_to(ROOT)): sha256(path) for path in [SOURCE_BLEND, *tracked_assets] if path.is_file()}

    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))
    scene = bpy.context.scene
    targets = [bpy.data.objects.get(name) for name in TARGET_NAMES]
    missing = [name for name, obj in zip(TARGET_NAMES, targets) if obj is None or obj.type != "MESH"]
    if missing:
        raise ValueError("Source blend does not contain recipe target meshes: " + ", ".join(missing))
    all_objects = list(scene.objects)
    materials = {material.name: material for material in bpy.data.materials if material}
    armor = materials.get("GravitasBreaker_PrimaryArmor")
    gunmetal = materials.get("GravitasBreaker_Gunmetal")
    if armor is None or gunmetal is None:
        raise ValueError("Source blend is missing Gravitas primary armor or gunmetal materials")

    for name, camera_name in CAMERAS:
        camera = bpy.data.objects.get(camera_name)
        if camera is None or camera.type != "CAMERA":
            raise ValueError(f"Missing reference view camera: {camera_name}")
    baseline_dir = OUTPUT_DIR / "baseline"
    baseline_dir.mkdir(exist_ok=True)
    collider_visibility = set_collider_render_visibility(hidden=True)
    render_views(baseline_dir)

    try:
        from bl_ext.user_default.hardflow.core import boolean as hardflow_boolean
        from bl_ext.user_default.hardflow.core import geometry as hardflow_geometry
    except Exception as exc:  # noqa: BLE001 - make a missing isolated extension explicit
        raise RuntimeError("Hardflow 1.23.0 is not active in the project Blender profile") from exc
    if not hasattr(bpy.ops.object, "hardflow_smart_sharpen"):
        raise RuntimeError("Hardflow Smart Sharpen operator is not registered")

    def apply_hardflow_boolean(target, cutter) -> str:
        for solver in ("EXACT", "FAST"):
            modifier = hardflow_boolean.add_boolean(
                target, cutter, operation="DIFFERENCE", solver=solver
            )
            if len(target.modifiers) > 1:
                target.modifiers.move(len(target.modifiers) - 1, 0)
            try:
                with bpy.context.temp_override(
                    object=target,
                    active_object=target,
                    selected_objects=[target],
                    selected_editable_objects=[target],
                ):
                    bpy.ops.object.modifier_apply(modifier=modifier.name)
            except RuntimeError:
                leftover = target.modifiers.get(modifier.name)
                if leftover is not None:
                    target.modifiers.remove(leftover)
                continue
            hardflow_geometry.cleanup_mesh(target)
            return solver
        raise RuntimeError(f"Hardflow Boolean failed for {target.name} / {cutter.name}")

    def apply_hardflow_sharpen(target) -> None:
        bpy.ops.object.select_all(action="DESELECT")
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        result = bpy.ops.object.hardflow_smart_sharpen(
            angle=math.radians(42.0),
            auto_width=False,
            width=0.004,
            segments=1,
            profile=0.7,
            micro_bevel=True,
            micro_width=0.0015,
            micro_segments=1,
            micro_angle=math.radians(45.0),
            use_weighted_normal=True,
        )
        if "FINISHED" not in result:
            raise RuntimeError(f"Hardflow Smart Sharpen failed for {target.name}: {result}")

    before_targets = mesh_counts(targets)
    before_whole_model = mesh_counts(gravitas_asset_meshes(all_objects))
    before_health = {target.name: hardflow_boolean.mesh_health(target) for target in targets}
    report = apply_gravitas_breaker_details(
        all_objects,
        armor_material=armor,
        mechanical_material=gunmetal,
        apply_boolean=apply_hardflow_boolean,
        apply_bevel=apply_hardflow_sharpen,
    )
    after_targets = mesh_counts(targets)
    after_whole_model = mesh_counts(gravitas_asset_meshes(all_objects))
    after_health = {target.name: hardflow_boolean.mesh_health(target) for target in targets}
    report["baselineTargetGeometry"] = before_targets
    report["pilotTargetGeometry"] = after_targets
    baseline_totals = geometry_totals(before_whole_model)
    pilot_totals = geometry_totals(after_whole_model)
    report["baselineWholeModelGeometry"] = baseline_totals
    report["pilotWholeModelGeometry"] = pilot_totals
    report["wholeModelGeometryDelta"] = {
        key: pilot_totals[key] - baseline_totals[key]
        for key in ("blenderFaces", "unityTriangles", "meshObjects")
    }
    report["evaluatedPilotTargetGeometry"] = evaluated_mesh_counts(targets)
    report["baselineTargetMeshHealth"] = before_health
    report["pilotTargetMeshHealth"] = after_health
    report["hardflowVersion"] = "1.23.0"
    report["sourceBlend"] = str(SOURCE_BLEND.relative_to(ROOT))
    report["sourceBlendSha256"] = watched_hashes[str(SOURCE_BLEND.relative_to(ROOT))]
    report["buildReplay"] = "tools/Build-GravitasBreakerModel.py uses the same recipe with Blender built-ins"
    report["productionPromoted"] = False
    report["budgetProfileStatus"] = "uncalibrated; prefab/package generation remains gated"

    render_views(OUTPUT_DIR)
    restore_render_visibility(collider_visibility)
    candidate = OUTPUT_DIR / "GravitasBreaker_HardflowPilot.blend"
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(candidate))

    # Replay the same coordinates through the builder's Blender built-ins in a
    # freshly loaded source scene. This proves the accepted edit does not need
    # Hardflow during normal headless build/export.
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))
    scene = bpy.context.scene
    all_objects = list(scene.objects)
    targets = [bpy.data.objects[name] for name in TARGET_NAMES]
    armor = bpy.data.materials["GravitasBreaker_PrimaryArmor"]
    gunmetal = bpy.data.materials["GravitasBreaker_Gunmetal"]
    before_builtin = mesh_counts(targets)
    standard_replay = apply_gravitas_breaker_details(
        all_objects,
        armor_material=armor,
        mechanical_material=gunmetal,
    )
    standard_replay["targetGeometry"] = mesh_counts(targets)
    standard_replay["evaluatedTargetGeometry"] = evaluated_mesh_counts(targets)
    standard_replay["baselineTargetGeometry"] = before_builtin
    standard_replay["targetMeshHealth"] = mesh_health(targets)
    report["standardBuilderReplay"] = standard_replay
    report["standardReplayMatchesPilotRecipe"] = (
        len(standard_replay["booleanCuts"]) == len(report["booleanCuts"])
        and standard_replay["addedDetailObjects"] == report["addedDetailObjects"]
    )
    if not report["standardReplayMatchesPilotRecipe"]:
        raise RuntimeError("The standard Blender builder replay did not match the Hardflow pilot recipe")
    final_hashes = {str(path.relative_to(ROOT)): sha256(path) for path in [SOURCE_BLEND, *tracked_assets] if path.is_file()}
    if final_hashes != watched_hashes:
        raise RuntimeError("A production source or tracked Gravitas asset changed during the isolated pilot")
    report["sourceAndTrackedAssetSha256"] = watched_hashes
    report["sourceAndTrackedAssetHashesUnchanged"] = True
    report_path = OUTPUT_DIR / "pilot-report.json"
    report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({
        "pilotReport": str(report_path),
        "candidateBlend": str(candidate),
        "baselineViews": [str(baseline_dir / f"{name}.png") for name, _ in CAMERAS],
        "pilotViews": [str(OUTPUT_DIR / f"{name}.png") for name, _ in CAMERAS],
        "sourceAndTrackedAssetHashesUnchanged": True,
        "targetMeshCount": len(TARGET_NAMES),
        "booleanCutCount": len(report["booleanCuts"]),
        "addedDetailObjectCount": report["addedDetailObjectCount"],
    }, indent=2))


if __name__ == "__main__":
    main()
