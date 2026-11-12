#!/usr/bin/env python3
"""Compare the Gravitas Level 2 service connector with native Blender replay.

The pilot writes isolated candidate blends and four-view previews under
artifacts/. It does not write FBX, source art, Unity prefabs, or bundles.
"""

from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import addon_utils
import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

CANONICAL_MODULE_PATH = ROOT / "tools" / "Build-CanonicalVisualModules.py"
CANONICAL_MODULE_SPEC = importlib.util.spec_from_file_location("Build_CanonicalVisualModules", CANONICAL_MODULE_PATH)
if CANONICAL_MODULE_SPEC is None or CANONICAL_MODULE_SPEC.loader is None:
    raise RuntimeError(f"Cannot load canonical module builder from {CANONICAL_MODULE_PATH}")
canonical = importlib.util.module_from_spec(CANONICAL_MODULE_SPEC)
sys.modules[CANONICAL_MODULE_SPEC.name] = canonical
CANONICAL_MODULE_SPEC.loader.exec_module(canonical)
import RogueModelKit as rmk
from hard_surface_geometry_presets import (
    COAXIAL_SERVICE_CONNECTOR_PRESET_ID,
    CoaxialServiceConnectorConfig,
)

VARIANT_ID = "tactical-operations:gravitas-breaker-quantum-hyperdrive-alloy"
ROLE_ID = "power_containment"
SLOT_ID = "Sponsons"
CONNECTOR_CONFIG = CoaxialServiceConnectorConfig(center=(0.0, 0.31, 0.0))


def _sha256(path: Path) -> str:
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
        raise RuntimeError("Hardflow 1.23.0 is not active in the project-local Blender profile") from exc
    if not hasattr(bpy.ops.object, "hardflow_smart_sharpen"):
        raise RuntimeError("Hardflow Smart Sharpen is not registered in Blender")

    def apply_boolean(target: bpy.types.Object, cutter: bpy.types.Object) -> str:
        errors: list[str] = []
        for solver in ("EXACT", "FAST"):
            modifier = hardflow_boolean.add_boolean(
                target, cutter, operation="DIFFERENCE", solver=solver)
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
        raise RuntimeError(f"Hardflow Boolean failed with all supported solvers: {errors}")

    def sharpen(target: bpy.types.Object) -> None:
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

    return apply_boolean, sharpen


def _mesh_health(obj: bpy.types.Object) -> dict[str, int | bool | str | None]:
    bm = bmesh.new()
    try:
        bm.from_mesh(obj.data)
        vertex_wear = obj.data.color_attributes.get("Color")
        rgba_rows = [tuple(float(channel) for channel in datum.color) for datum in vertex_wear.data] if vertex_wear else []
        channel_counts = sorted({len(row) for row in rgba_rows})
        finite_rgba = bool(rgba_rows) and all(
            len(row) == 4 and all(math.isfinite(channel) for channel in row)
            for row in rgba_rows
        )
        return {
            "vertices": len(bm.verts),
            "faces": len(bm.faces),
            "nonManifoldEdges": sum(1 for edge in bm.edges if not edge.is_manifold),
            "looseVertices": sum(1 for vertex in bm.verts if not vertex.link_edges),
            "degenerateFaces": sum(1 for face in bm.faces if face.calc_area() <= 1.0e-10),
            "uvMapPresent": obj.data.uv_layers.get("UVMap") is not None,
            "vertexWearPresent": vertex_wear is not None,
            "vertexWearDataType": vertex_wear.data_type if vertex_wear else None,
            "vertexWearDomain": vertex_wear.domain if vertex_wear else None,
            "vertexWearChannelCounts": channel_counts,
            "vertexWearSamples": len(rgba_rows),
            "vertexWearFiniteRgba": finite_rgba,
        }
    finally:
        bm.free()


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


def _surface_samples(geometry: tuple[list[Vector], list[tuple[int, int, int]]]) -> list[Vector]:
    vertices, triangles = geometry
    return vertices + [sum((vertices[index] for index in face), Vector()) / 3.0 for face in triangles]


def _combine_geometries(
    geometries: list[tuple[list[Vector], list[tuple[int, int, int]]]],
) -> tuple[list[Vector], list[tuple[int, int, int]]]:
    vertices: list[Vector] = []
    triangles: list[tuple[int, int, int]] = []
    for points, faces in geometries:
        offset = len(vertices)
        vertices.extend(points)
        triangles.extend(tuple(index + offset for index in face) for face in faces)
    return vertices, triangles


def _directed_distances(
    samples: list[Vector],
    vertices: list[Vector],
    triangles: list[tuple[int, int, int]],
) -> list[float]:
    target = BVHTree.FromPolygons(vertices, triangles, all_triangles=True, epsilon=0.0)
    distances = []
    for point in samples:
        hit = target.find_nearest(point)
        if hit and hit[3] is not None:
            distances.append(float(hit[3]))
    return distances


def _sampled_surface_clearance(
    first: bpy.types.Object,
    second: bpy.types.Object,
) -> dict[str, float | bool]:
    first_geometry = _evaluated_geometry(first)
    second_geometry = _evaluated_geometry(second)
    first_tree = BVHTree.FromPolygons(*first_geometry, all_triangles=True, epsilon=0.0)
    second_tree = BVHTree.FromPolygons(*second_geometry, all_triangles=True, epsilon=0.0)
    surface_crossings = bool(first_tree.overlap(second_tree))
    first_samples = _surface_samples(first_geometry)
    second_samples = _surface_samples(second_geometry)
    directed = (
        _directed_distances(first_samples, *second_geometry),
        _directed_distances(second_samples, *first_geometry),
    )
    gaps = [distance for values in directed for distance in values]
    return {
        "sampledSurfaceGapMm": min(gaps) * 1000.0 if gaps else float("inf"),
        "triangleSurfaceCrossing": surface_crossings,
    }


def _render_views(output: Path, module_name: str, meshes: list[bpy.types.Object]) -> list[str]:
    output.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    corners = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    if not corners:
        raise RuntimeError(f"Cannot render an empty candidate module: {module_name}")
    low = Vector(tuple(min(point[index] for point in corners) for index in range(3)))
    high = Vector(tuple(max(point[index] for point in corners) for index in range(3)))
    center = (low + high) * 0.5
    span = max((high - low).x, (high - low).y, (high - low).z, 0.25)
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 960
    scene.render.resolution_y = 540
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.filepath = str(output / "unused.png")
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "Medium High Contrast"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    scene.world.color = (0.025, 0.035, 0.045)

    bpy.ops.object.light_add(type="AREA", location=center + Vector((2.2, -2.8, 3.2)))
    key = bpy.context.object
    key.name = "ServiceConnector_PreviewKey"
    key.data.energy = 480.0
    key.data.shape = "DISK"
    key.data.size = max(span * 1.1, 0.45)
    key.rotation_euler = (center - key.location).to_track_quat("-Z", "Y").to_euler()
    bpy.ops.object.light_add(type="AREA", location=center + Vector((-2.0, 2.0, 1.3)))
    fill = bpy.context.object
    fill.name = "ServiceConnector_PreviewFill"
    fill.data.energy = 210.0
    fill.data.size = max(span * 0.8, 0.35)
    fill.rotation_euler = (center - fill.location).to_track_quat("-Z", "Y").to_euler()

    views = {
        "front": Vector((0.0, 1.0, 0.0)),
        "side": Vector((1.0, 0.0, 0.0)),
        "top": Vector((0.0, 0.0, 1.0)),
        "isometric": Vector((1.0, 1.0, 0.8)).normalized(),
    }
    rendered: list[str] = []
    for name, direction in views.items():
        camera_data = bpy.data.cameras.new(f"ServiceConnectorCamera_{name}")
        camera_data.type = "ORTHO"
        camera_data.ortho_scale = span * 1.55
        camera = bpy.data.objects.new(f"ServiceConnectorCamera_{name}", camera_data)
        scene.collection.objects.link(camera)
        camera.location = center + direction * max(span * 2.2, 0.8)
        camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.camera = camera
        image_path = output / f"{name}.png"
        scene.render.filepath = str(image_path)
        bpy.ops.render.render(write_still=True)
        rendered.append(str(image_path))
    return rendered


def _build_candidate(
    entry: dict[str, object],
    output: Path,
    *,
    hardflow_boolean_apply,
    hardflow_bevel,
) -> dict[str, object]:
    rmk.clear_scene()
    suffix = "Hardflow" if hardflow_boolean_apply is not None else "NativeReplay"
    module_entry = dict(entry)
    module_name = str(entry["moduleName"]) + "_" + suffix
    module_entry["moduleName"] = module_name
    seed = int(hashlib.sha256(str(entry["variantId"]).encode("utf-8")).hexdigest()[:8], 16)
    objects, armor_materials = canonical.build_module_geometry(
        module_entry,
        seed,
        hardflow_boolean_apply=hardflow_boolean_apply,
        hardflow_bevel=hardflow_bevel,
    )
    collection = bpy.data.collections.new(module_name + "_Pilot")
    bpy.context.scene.collection.children.link(collection)
    canonical.place_in_collection(objects, collection)
    mesh_objects = [obj for obj in objects if obj.type == "MESH"]
    for obj in mesh_objects:
        canonical.ensure_uv(obj)
        rmk.apply_weighted_normals(obj, weight=50, keep_sharp=True, apply_modifier=True)
    rmk.bake_advanced_vertex_shading(
        mesh_objects,
        wear_level="field_tested",
        soot_intensity=0.0,
        team_mask_materials=armor_materials,
        structural_wear_mask=True,
    )

    output.mkdir(parents=True, exist_ok=False)
    views = _render_views(output / "views", module_name, mesh_objects)
    panels = [
        obj for obj in mesh_objects
        if obj.get("rcl_canonical_component_role") == "gravitas_power_containment_service_panel"
    ]
    if len(panels) != 1:
        raise RuntimeError(f"Expected one service panel for {module_name}, found {[obj.name for obj in panels]}")
    panel = panels[0]
    connector_objects = [
        obj for obj in mesh_objects
        if obj.get("rcl_service_connector_component") in {
            "support_panel", "recessed_insulator", "connector_hub", "coupling_pin",
            "retainer_flange", "insulator_rim", "panel_anchor_bolt",
        }
    ]
    if len(connector_objects) != 10:
        raise RuntimeError(f"Expected support panel plus nine connector components, found {len(connector_objects)}")
    adjacent_roles = {
        "coolantFeed": "gravitas_power_containment_coolant_feed",
        "containmentBrace": "gravitas_power_containment_brace",
    }
    adjacent_clearances: dict[str, dict[str, object]] = {}
    connector_details = [obj for obj in connector_objects if obj is not panel]
    for label, component_role in adjacent_roles.items():
        adjacent = [obj for obj in mesh_objects if obj.get("rcl_canonical_component_role") == component_role]
        pair_rows = [
            {
                "connectorComponent": connector_obj.get("rcl_service_connector_component"),
                "adjacentObject": adjacent_obj.name,
                **_sampled_surface_clearance(connector_obj, adjacent_obj),
            }
            for connector_obj in connector_details
            for adjacent_obj in adjacent
        ]
        crossings = sum(bool(row["triangleSurfaceCrossing"]) for row in pair_rows)
        adjacent_clearances[label] = {
            "pairCount": len(pair_rows),
            "minimumSampledSurfaceGapMm": min(
                (float(row["sampledSurfaceGapMm"]) for row in pair_rows), default=float("inf")),
            "triangleSurfaceCrossings": crossings,
            "pairs": pair_rows,
        }
        if crossings:
            raise RuntimeError(f"Service connector intersects Gravitas {label} geometry")

    connector_geometry = _combine_geometries(
        [_evaluated_geometry(obj) for obj in connector_objects])
    mesh_rows = {obj.name: _mesh_health(obj) for obj in mesh_objects}
    triangle_count = 0
    for obj in mesh_objects:
        obj.data.calc_loop_triangles()
        triangle_count += len(obj.data.loop_triangles)
    report = {
        "candidate": suffix,
        "moduleName": module_name,
        "variantId": entry["variantId"],
        "geometryPreset": entry["geometryPreset"],
        "geometryLayout": entry["geometryLayout"],
        "visualEngineeringRole": entry["visualEngineeringRole"],
        "slotId": entry["slotId"],
        "servicePanelObjectName": panel.name,
        "connectorObjectNames": [obj.name for obj in connector_objects],
        "connectorObjectCount": len(connector_objects),
        "connectorComponents": [
            {
                "role": obj.get("rcl_service_connector_component", "unclassified"),
                "objectName": obj.name,
            }
            for obj in connector_objects
        ],
        "meshObjects": len(mesh_objects),
        "blenderFaces": sum(len(obj.data.polygons) for obj in mesh_objects),
        "blenderTriangles": triangle_count,
        "meshHealth": mesh_rows,
        "allMeshesHealthy": all(
            row["nonManifoldEdges"] == 0 and row["looseVertices"] == 0 and row["degenerateFaces"] == 0
            for row in mesh_rows.values()
        ),
        "allMeshesHaveUvAndVertexWear": all(
            row["uvMapPresent"] and row["vertexWearPresent"] and
            row["vertexWearDataType"] == "BYTE_COLOR" and row["vertexWearDomain"] == "CORNER" and
            row["vertexWearChannelCounts"] == [4] and row["vertexWearFiniteRgba"]
            for row in mesh_rows.values()
        ),
        "servicePortFit": {
            "panelSizeMeters": list(CONNECTOR_CONFIG.panel_size),
            "panelThicknessMeters": CONNECTOR_CONFIG.panel_thickness,
            "counterboreDepthMeters": CONNECTOR_CONFIG.seat_depth,
            "remainingCounterboreBackingMm": (
                CONNECTOR_CONFIG.panel_thickness - CONNECTOR_CONFIG.seat_depth
            ) * 1000.0,
            "minimumPanelEdgeClearanceMm": min(
                CONNECTOR_CONFIG.panel_size[0] * 0.5 - max(
                    CONNECTOR_CONFIG.flange_major_radius + CONNECTOR_CONFIG.flange_minor_radius,
                    CONNECTOR_CONFIG.fastener_offset + CONNECTOR_CONFIG.fastener_radius,
                ),
                CONNECTOR_CONFIG.panel_size[1] * 0.5 - max(
                    CONNECTOR_CONFIG.flange_major_radius + CONNECTOR_CONFIG.flange_minor_radius,
                    CONNECTOR_CONFIG.fastener_offset + CONNECTOR_CONFIG.fastener_radius,
                ),
            ) * 1000.0,
            "panelAnchorBoltToFlangeRadialClearanceMm": (
                CONNECTOR_CONFIG.fastener_offset - CONNECTOR_CONFIG.fastener_radius -
                CONNECTOR_CONFIG.flange_major_radius - CONNECTOR_CONFIG.flange_minor_radius
            ) * 1000.0,
            "panelAnchorBoltSeatOffsetMm": (
                CONNECTOR_CONFIG.fastener_surface_offset - CONNECTOR_CONFIG.fastener_depth * 0.5
            ) * 1000.0,
            "adjacentComponentClearances": adjacent_clearances,
            "booleanBackend": panel.get("rcl_connector_boolean_backend", "unknown"),
        },
        "connectorSurfaceGeometry": connector_geometry,
        "previews": views,
        "blendPath": str(output / f"{module_name}.blend"),
    }
    bpy.ops.wm.save_as_mainfile(filepath=report["blendPath"])
    report["blendSha256"] = _sha256(Path(report["blendPath"]))
    report.pop("connectorSurfaceGeometry")
    return report


def main() -> None:
    if not bpy.app.version_string.startswith("4.5.14"):
        raise RuntimeError(f"Pinned Blender 4.5.14 is required, got {bpy.app.version_string}")
    module_name = "bl_ext.user_default.hardflow"
    _loaded, enabled = addon_utils.check(module_name)
    if not enabled:
        addon_utils.enable(module_name, default_set=False)
    if not addon_utils.check(module_name)[1]:
        raise RuntimeError("Hardflow did not activate in the isolated project profile")

    entries = canonical.read_module_entries()
    matches = [entry for entry in entries if entry["variantId"] == VARIANT_ID and entry["slotId"] == SLOT_ID]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one Gravitas Breaker Level 2 Sponsons entry; found {len(matches)}")
    entry = matches[0]
    if entry["geometryPreset"] != COAXIAL_SERVICE_CONNECTOR_PRESET_ID or entry["visualEngineeringRole"] != ROLE_ID:
        raise RuntimeError("The Gravitas Level 2 power-containment module is not bound to the service connector preset")

    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    output_root = ROOT / "artifacts" / f"gravitas-power-service-connector-hardflow-{stamp}"
    output_root.mkdir(parents=True, exist_ok=False)
    hardflow_boolean, hardflow_bevel = _hardflow_callbacks()
    native = _build_candidate(
        entry, output_root / "native-replay", hardflow_boolean_apply=None, hardflow_bevel=None)
    hardflow = _build_candidate(
        entry, output_root / "hardflow", hardflow_boolean_apply=hardflow_boolean,
        hardflow_bevel=hardflow_bevel)

    # Reopen each candidate file to compare the whole connector assembly, including
    # the support panel and all fitted hardware, using evaluated world-space meshes.
    bpy.ops.wm.open_mainfile(filepath=native["blendPath"], load_ui=False)
    native_geometry = _combine_geometries([
        _evaluated_geometry(bpy.data.objects[name]) for name in native["connectorObjectNames"]
    ])
    bpy.ops.wm.open_mainfile(filepath=hardflow["blendPath"], load_ui=False)
    hardflow_geometry = _combine_geometries([
        _evaluated_geometry(bpy.data.objects[name]) for name in hardflow["connectorObjectNames"]
    ])
    native_samples = _surface_samples(native_geometry)
    hardflow_samples = _surface_samples(hardflow_geometry)
    deviations = (
        _directed_distances(native_samples, *hardflow_geometry),
        _directed_distances(hardflow_samples, *native_geometry),
    )
    all_deviations = [value for direction in deviations for value in direction]
    comparison = {
        "method": "bidirectional evaluated world-space vertex and triangle-centroid distance across support panel and connector assembly",
        "sampleCount": len(all_deviations),
        "maxDeviationMm": max(all_deviations) * 1000.0,
        "within2Mm": max(all_deviations) <= 0.002,
    }
    if not comparison["within2Mm"]:
        raise RuntimeError("Hardflow service-connector assembly exceeded the 2 mm sampled-surface guard")
    if not native["allMeshesHealthy"] or not hardflow["allMeshesHealthy"]:
        raise RuntimeError("Service-connector candidate has unhealthy mesh topology")
    if not native["allMeshesHaveUvAndVertexWear"] or not hardflow["allMeshesHaveUvAndVertexWear"]:
        raise RuntimeError("Service-connector candidate lost UVMap or Color vertex-wear data")

    report = {
        "schemaVersion": 1,
        "createdAtUtc": datetime.now(timezone.utc).isoformat(),
        "variantId": VARIANT_ID,
        "geometryPreset": COAXIAL_SERVICE_CONNECTOR_PRESET_ID,
        "geometryRole": ROLE_ID,
        "slotId": SLOT_ID,
        "hardflow": {
            "version": "1.23.0",
            "blenderVersion": bpy.app.version_string,
            "enabledInProjectProfile": True,
            "operation": "EXACT recessed counterbore Boolean on the existing service panel",
        },
        "sourceInputs": {
            "roleRegistrySha256": _sha256(ROOT / "content/source-art/canonical-visual-module-roles.json"),
            "packManifestSha256": _sha256(ROOT / "content/packs/tactical-operations/content.json"),
            "presetGeneratorSha256": _sha256(ROOT / "tools/hard_surface_geometry_presets.py"),
            "canonicalBuilderSha256": _sha256(ROOT / "tools/Build-CanonicalVisualModules.py"),
            "pilotScriptSha256": _sha256(Path(__file__).resolve()),
        },
        "nativeReplay": native,
        "hardflowCandidate": hardflow,
        "surfaceComparison": comparison,
        "geometryBudgetProfile": "uncalibrated; candidate only, no production FBX/prefab/bundle promotion",
        "outputBoundary": "Isolated module blends and PNG previews only; no source blend, FBX, prefab, texture, or bundle is changed.",
    }
    report_path = output_root / "pilot-report.json"
    report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"PASS: Hardflow and native Gravitas module candidates saved under {output_root}")
    print(f"Surface deviation: {comparison['maxDeviationMm']:.3f} mm; report: {report_path}")


if __name__ == "__main__":
    main()
