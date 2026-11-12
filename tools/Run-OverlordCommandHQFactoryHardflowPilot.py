"""Render and compare an isolated Hardflow pass for the Overlord HQ factory.

The tracked source blend, model FBXs, Unity prefabs, and bundles remain
read-only. Both Hardflow and Blender-native replay candidates are saved under
the ignored artifact directory named below.
"""

from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from overlord_hq_factory_hardflow_recipe import (  # noqa: E402
    DETAIL_PREFIX,
    MARKER_KEY,
    RECIPE_ID,
    apply_overlord_hq_factory_details,
)

SOURCE_BLEND = ROOT / "content/source-art/overlordcommandhqfactory.blend"
MODEL_DIR = ROOT / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models"
OUTPUT_DIR = ROOT / "artifacts/hardflow-wave1-overlord-factory-20261111-r6"
ASSET_PREFIX = "OverlordCommandHQFactory"
SIDEWALLS = (
    "OverlordCommandHQFactoryArmor_Sidewall_L",
    "OverlordCommandHQFactoryArmor_Sidewall_R",
)
ACCESS_RAILS = (
    "OverlordCommandHQFactoryMaintenance_Handrail_Lower",
    "OverlordCommandHQFactoryMaintenance_Handrail_Upper",
    "OverlordCommandHQFactoryMaintenance_RailPost_3",
)
PLATES = tuple(
    f"OverlordCommandHQFactoryArmor_ServicePlate_{side}_{index}"
    for side in ("L", "R")
    for index in (1, 2, 3)
)
CAMERAS = (
    ("front", Vector((0.0, 14.0, 6.0)), 11.5, Vector((0.0, 0.0, 3.0))),
    ("door-side", Vector((-15.0, 0.0, 3.7)), 11.0, Vector((0.0, 0.0, 3.0))),
    ("top", Vector((0.0, 0.0, 17.0)), 10.5, Vector((0.0, 0.0, 3.0))),
    ("isometric", Vector((-12.0, 14.0, 10.0)), 12.0, Vector((0.0, 0.0, 3.0))),
    ("service-closeup", Vector((-4.0, -0.5, 0.5)), 3.7, Vector((-4.0, 0.55, 1.35))),
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def model_meshes() -> list[bpy.types.Object]:
    return [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and obj.name.startswith(ASSET_PREFIX)
        and "_COLLIDER_" not in obj.name
    ]


def geometry_rows(objects: list[bpy.types.Object]) -> dict[str, dict[str, int]]:
    rows = {}
    for obj in objects:
        obj.data.calc_loop_triangles()
        rows[obj.name] = {
            "blenderFaces": len(obj.data.polygons),
            "triangles": len(obj.data.loop_triangles),
        }
    return rows


def totals(rows: dict[str, dict[str, int]]) -> dict[str, int]:
    return {
        "meshObjects": len(rows),
        "blenderFaces": sum(row["blenderFaces"] for row in rows.values()),
        "triangles": sum(row["triangles"] for row in rows.values()),
    }


def mesh_health(objects: list[bpy.types.Object]) -> dict[str, dict[str, int]]:
    health = {}
    for obj in objects:
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        health[obj.name] = {
            "nonManifoldEdges": sum(1 for edge in bm.edges if not edge.is_manifold),
            "degenerateFaces": sum(1 for face in bm.faces if face.calc_area() < 1e-9),
            "looseVertices": sum(1 for vert in bm.verts if not vert.link_edges),
        }
        bm.free()
    return health


def authoring_attribute_diagnostics(objects: list[bpy.types.Object]) -> dict[str, object]:
    rows = {}
    for obj in objects:
        color = obj.data.color_attributes.get("Color")
        channel_ranges = None
        if color is not None and len(color.data):
            channel_ranges = [
                [
                    min(float(row.color[index]) for row in color.data),
                    max(float(row.color[index]) for row in color.data),
                ]
                for index in range(4)
            ]
        rows[obj.name] = {
            "hasMaterial": bool(obj.data.materials and any(obj.data.materials)),
            "uvMapNames": [layer.name for layer in obj.data.uv_layers],
            "hasUvMap": obj.data.uv_layers.get("UVMap") is not None,
            "colorAttribute": None if color is None else {
                "name": color.name,
                "dataType": color.data_type,
                "domain": color.domain,
                "channelRangesRgba": channel_ranges,
            },
        }
    return {
        "meshCount": len(rows),
        "allHaveMaterial": all(row["hasMaterial"] for row in rows.values()),
        "allHaveUvMap": all(row["hasUvMap"] for row in rows.values()),
        "allHaveByteCornerColor": all(
            row["colorAttribute"] is not None
            and row["colorAttribute"]["dataType"] == "BYTE_COLOR"
            and row["colorAttribute"]["domain"] == "CORNER"
            for row in rows.values()
        ),
        "meshes": rows,
    }


def _look_at(camera: bpy.types.Object, target: Vector) -> None:
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()


def render_views(output_dir: Path) -> None:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.display.shading.cavity_ridge_factor = 1.4
    scene.display.shading.cavity_valley_factor = 1.1
    scene.display.shading.show_specular_highlight = True
    if scene.world is None:
        scene.world = bpy.data.worlds.new("RCL_HardflowReviewWorld")
    scene.world.color = (0.035, 0.035, 0.035)
    camera_data = bpy.data.cameras.new("RCL_HardflowReviewCamera")
    camera = bpy.data.objects.new("RCL_HardflowReviewCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    hidden_colliders = []
    for obj in scene.objects:
        if obj.type == "MESH" and "_COLLIDER_" in obj.name:
            hidden_colliders.append((obj, obj.hide_render))
            obj.hide_render = True
    target = Vector((0.0, 0.0, 3.0))
    try:
        for view_name, direction, scale, view_target in CAMERAS:
            camera.location = view_target + direction
            _look_at(camera, view_target)
            camera.data.type = "ORTHO"
            camera.data.ortho_scale = scale
            scene.render.filepath = str(output_dir / f"{view_name}.png")
            bpy.ops.render.render(write_still=True)
    finally:
        for obj, was_hidden in hidden_colliders:
            obj.hide_render = was_hidden
        scene.camera = None
        bpy.data.objects.remove(camera, do_unlink=True)
        bpy.data.cameras.remove(camera_data)


def _surface_samples(obj: bpy.types.Object) -> list[Vector]:
    world = obj.matrix_world
    points = [world @ vertex.co for vertex in obj.data.vertices]
    obj.data.calc_loop_triangles()
    for triangle in obj.data.loop_triangles:
        local = sum((obj.data.vertices[index].co for index in triangle.vertices), Vector()) / 3.0
        points.append(world @ local)
    return points


def _surface_tree(obj: bpy.types.Object) -> BVHTree:
    world = obj.matrix_world
    vertices = [world @ vertex.co for vertex in obj.data.vertices]
    polygons = [list(poly.vertices) for poly in obj.data.polygons if len(poly.vertices) >= 3]
    return BVHTree.FromPolygons(vertices, polygons, all_triangles=False, epsilon=1e-6)


def _capture_surface(obj: bpy.types.Object) -> dict[str, object]:
    return {
        "tree": _surface_tree(obj),
        "samples": _surface_samples(obj),
        "faces": len(obj.data.polygons),
    }


def compare_surfaces(
    hardflow_surfaces: dict[str, dict[str, object]],
    native_objects: list[bpy.types.Object],
    names: tuple[str, ...],
) -> dict[str, object]:
    native_by_name = {obj.name: obj for obj in native_objects}
    rows = {}
    all_distances = []
    for name in names:
        left = hardflow_surfaces.get(name)
        right = native_by_name.get(name)
        if left is None or right is None:
            rows[name] = {"matched": False}
            continue
        left_tree = left["tree"]
        right_tree = _surface_tree(right)
        distances = []
        for point in left["samples"]:
            nearest = right_tree.find_nearest(point)
            if nearest and nearest[3] is not None:
                distances.append(float(nearest[3]))
        for point in _surface_samples(right):
            nearest = left_tree.find_nearest(point)
            if nearest and nearest[3] is not None:
                distances.append(float(nearest[3]))
        maximum = max(distances, default=math.inf)
        rms = math.sqrt(sum(value * value for value in distances) / len(distances)) if distances else math.inf
        rows[name] = {
            "matched": True,
            "hardflowFaces": left["faces"],
            "nativeFaces": len(right.data.polygons),
            "sampleCount": len(distances),
            "maxBidirectionalSurfaceDeviationM": maximum,
            "rmsBidirectionalSurfaceDeviationM": rms,
        }
        all_distances.extend(distances)
    overall_max = max(all_distances, default=math.inf)
    overall_rms = math.sqrt(sum(value * value for value in all_distances) / len(all_distances)) if all_distances else math.inf
    return {
        "method": "bidirectional vertex and face-centroid nearest-surface distance",
        "sampleCount": len(all_distances),
        "maxDeviationM": overall_max,
        "rmsDeviationM": overall_rms,
        "within002m": overall_max <= 0.02,
        "objects": rows,
    }


def _load_source() -> None:
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND), load_ui=False)


def _materials() -> tuple[bpy.types.Material, bpy.types.Material]:
    armor = bpy.data.materials.get("OverlordCommandHQFactory_PrimaryArmor")
    gunmetal = bpy.data.materials.get("OverlordCommandHQFactory_Gunmetal")
    if armor is None or gunmetal is None:
        raise ValueError("Factory source is missing its primary-armor or gunmetal material")
    return armor, gunmetal


def _hardflow_callbacks():
    try:
        from bl_ext.user_default.hardflow.core import boolean as hardflow_boolean
        from bl_ext.user_default.hardflow.core import geometry as hardflow_geometry
    except Exception as exc:  # noqa: BLE001
        raise RuntimeError("Hardflow 1.23.0 is not active in the project-local Blender profile") from exc
    if not hasattr(bpy.ops.object, "hardflow_smart_sharpen"):
        raise RuntimeError("Hardflow Smart Sharpen operator is not registered")

    def hardflow_boolean_apply(target: bpy.types.Object, cutter: bpy.types.Object) -> str:
        errors = []
        for solver in ("EXACT", "FAST"):
            modifier = hardflow_boolean.add_boolean(
                target, cutter, operation="DIFFERENCE", solver=solver
            )
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

    def hardflow_sharpen(target: bpy.types.Object) -> None:
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
            raise RuntimeError(f"Hardflow Smart Sharpen failed on {target.name}: {result}")

    return hardflow_boolean_apply, hardflow_sharpen


def _save_candidate(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(path))


def main() -> None:
    if not SOURCE_BLEND.is_file():
        raise FileNotFoundError(SOURCE_BLEND)
    if OUTPUT_DIR.exists():
        raise FileExistsError(
            f"Refusing to overwrite existing pilot evidence at {OUTPUT_DIR}; choose a new output directory"
        )
    tracked_assets = [
        MODEL_DIR / "OverlordCommandHQFactory.fbx",
        MODEL_DIR / "OverlordCommandHQFactory_Atlas.fbx",
        MODEL_DIR / "OverlordCommandHQFactory.gameplay.json",
    ]
    watched = [SOURCE_BLEND, *[path for path in tracked_assets if path.is_file()]]
    before_hashes = {str(path.relative_to(ROOT)): sha256(path) for path in watched}
    _load_source()
    if bpy.context.scene.get(MARKER_KEY) == RECIPE_ID:
        raise RuntimeError(
            "The source already contains the promoted Hardflow recipe. Recreate a pilot from the verified baseline backup instead."
        )
    OUTPUT_DIR.mkdir(parents=True)
    baseline_dir = OUTPUT_DIR / "baseline"
    hardflow_dir = OUTPUT_DIR / "hardflow"
    native_dir = OUTPUT_DIR / "native-replay"
    for folder in (baseline_dir, hardflow_dir, native_dir):
        folder.mkdir()

    baseline_objects = model_meshes()
    baseline_geometry = totals(geometry_rows(baseline_objects))
    render_views(baseline_dir)
    armor, gunmetal = _materials()
    hardflow_boolean_apply, hardflow_sharpen = _hardflow_callbacks()
    recipe = apply_overlord_hq_factory_details(
        list(bpy.context.scene.objects),
        gunmetal_material=gunmetal,
        apply_boolean=hardflow_boolean_apply,
        apply_sharpen=hardflow_sharpen,
    )
    hardflow_meshes = model_meshes()
    affected_names = (*SIDEWALLS, *PLATES, *ACCESS_RAILS, *recipe["addedObjects"])
    hardflow_geometry = totals(geometry_rows(hardflow_meshes))
    hardflow_surfaces = {
        name: _capture_surface(bpy.data.objects[name])
        for name in affected_names
    }
    hardflow_health = mesh_health(
        [bpy.data.objects[name] for name in (*SIDEWALLS, *ACCESS_RAILS, *recipe["addedObjects"])]
    )
    attribute_names = (*SIDEWALLS, *PLATES, *ACCESS_RAILS, *recipe["addedObjects"])
    attribute_diagnostics = authoring_attribute_diagnostics(
        [bpy.data.objects[name] for name in attribute_names]
    )
    render_views(hardflow_dir)
    hardflow_path = hardflow_dir / "OverlordCommandHQFactory_HardflowCandidate.blend"
    _save_candidate(hardflow_path)

    # Reload the exact baseline and replay the dimensions with Blender-native
    # operations. Compare evaluated surfaces, not operation labels or counts.
    _load_source()
    _materials()
    native_recipe = apply_overlord_hq_factory_details(
        list(bpy.context.scene.objects),
        gunmetal_material=bpy.data.materials["OverlordCommandHQFactory_Gunmetal"],
    )
    native_meshes = model_meshes()
    native_geometry = totals(geometry_rows(native_meshes))
    native_health = mesh_health(
        [bpy.data.objects[name] for name in (*SIDEWALLS, *ACCESS_RAILS, *native_recipe["addedObjects"])]
    )
    surface_comparison = compare_surfaces(
        hardflow_surfaces,
        native_meshes,
        affected_names,
    )
    native_path = native_dir / "OverlordCommandHQFactory_NativeReplay.blend"
    _save_candidate(native_path)

    after_hashes = {str(path.relative_to(ROOT)): sha256(path) for path in watched}
    source_and_models_unchanged = before_hashes == after_hashes
    target_mesh_health_passed = all(
        values["nonManifoldEdges"] == 0
        and values["degenerateFaces"] == 0
        and values["looseVertices"] == 0
        for values in hardflow_health.values()
    )
    authoring_attributes_passed = (
        attribute_diagnostics["allHaveMaterial"]
        and attribute_diagnostics["allHaveUvMap"]
        and attribute_diagnostics["allHaveByteCornerColor"]
    )
    geometry_authoring_passed = (
        source_and_models_unchanged
        and target_mesh_health_passed
        and authoring_attributes_passed
        and surface_comparison["within002m"]
    )

    pilot_data = {
        "schemaVersion": 1,
        "assetId": "OverlordCommandHQFactory",
        "evidenceClass": "local Blender authoring validation",
        "hardflowVersion": "1.23.0",
        "blenderVersion": bpy.app.version_string,
        "sourceBlend": str(SOURCE_BLEND.relative_to(ROOT)),
        "outputDirectory": str(OUTPUT_DIR.relative_to(ROOT)),
        "sourceAndTrackedHashesBefore": before_hashes,
        "sourceAndTrackedHashesAfter": after_hashes,
        "pilotRecipe": recipe,
        "nativeReplayRecipe": native_recipe,
        "baselineGeometry": baseline_geometry,
        "hardflowGeometry": hardflow_geometry,
        "nativeReplayGeometry": native_geometry,
        "hardflowTargetMeshHealth": hardflow_health,
        "nativeReplayTargetMeshHealth": native_health,
        "vertexWearAndUvContract": attribute_diagnostics,
        "geometryDeltaFromBaseline": {
            key: hardflow_geometry[key] - baseline_geometry[key]
            for key in ("meshObjects", "blenderFaces", "triangles")
        },
        "hardflowVsNativeSurface": surface_comparison,
        "sourceAndTrackedModelsUnchanged": source_and_models_unchanged,
        "affectedMeshHealthPassed": target_mesh_health_passed,
        "materialUvAndVertexWearPassed": authoring_attributes_passed,
        "geometryAuthoringVerdict": "PASS_CANDIDATE" if geometry_authoring_passed else "FAIL",
        "budgetStatus": "uncalibrated; candidate is not promoted to FBX/prefab/bundle",
        "views": {
            "baseline": [str((baseline_dir / f"{name}.png").relative_to(ROOT)) for name, _, _, _ in CAMERAS],
            "hardflow": [str((hardflow_dir / f"{name}.png").relative_to(ROOT)) for name, _, _, _ in CAMERAS],
        },
        "candidateBlendFiles": [
            str(hardflow_path.relative_to(ROOT)),
            str(native_path.relative_to(ROOT)),
        ],
    }
    report_path = OUTPUT_DIR / "pilot-report.json"
    report_path.write_text(json.dumps(pilot_data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"[HARDFLOW] report={report_path}")
    print(json.dumps({
        "baseline": baseline_geometry,
        "hardflow": hardflow_geometry,
        "nativeReplay": native_geometry,
        "surfaceDeviationM": surface_comparison["maxDeviationM"],
        "surfaceWithin0_02m": surface_comparison["within002m"],
        "meshHealthPassed": target_mesh_health_passed,
        "authoringDataPassed": authoring_attributes_passed,
        "geometryAuthoringVerdict": "PASS_CANDIDATE" if geometry_authoring_passed else "FAIL",
        "sourceHashesUnchanged": source_and_models_unchanged,
        "candidate": str(hardflow_path),
    }, ensure_ascii=False))
    if not geometry_authoring_passed:
        raise RuntimeError(f"Hardflow factory candidate failed local geometry checks; see {report_path}")


if __name__ == "__main__":
    main()
