#!/usr/bin/env python3
"""Render unit-scale, close inspection views for canonical Level 2 modules.

Run with the pinned Blender 4.5.14 executable after
Build-CanonicalVisualModules.py. Inputs are read-only; outputs belong under
ignored artifacts/. The orthographic frame fills 95 percent of the image by
default so small mechanisms and contact gaps remain easy to inspect.
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path
from typing import Any, get_type_hints

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "content/source-art/canonical-visual-modules.json"


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default=str(MANIFEST))
    parser.add_argument("--output", default="artifacts/canonical-two-level/modules-close-views")
    parser.add_argument("--module", action="append", default=[],
                        help="Exact moduleName; may be repeated. Default: every manifest module.")
    parser.add_argument("--detail-clusters", action="store_true",
                        help="Also render separately named B01/B02-style mesh clusters at close scale.")
    parser.add_argument("--fill", type=float, default=0.95,
                        help="Fraction of image occupied by the fitted module (0.50–0.98).")
    parser.add_argument("--resolution-x", type=int, default=1600)
    parser.add_argument("--resolution-y", type=int, default=1200)
    parser.add_argument("--angles", default="front,reverse,isometric,top",
                        help="Comma-separated view names from front,reverse,isometric,top.")
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def bounds(objects: list[bpy.types.Object]) -> tuple[Vector, Vector, list[Vector]]:
    corners = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    if not corners:
        raise ValueError("Module contains no renderable mesh geometry")
    low = Vector(tuple(min(point[axis] for point in corners) for axis in range(3)))
    high = Vector(tuple(max(point[axis] for point in corners) for axis in range(3)))
    return low, high, corners


def remove_imported(objects: list[bpy.types.Object]) -> None:
    for obj in objects:
        data, obj_type = obj.data, obj.type
        bpy.data.objects.remove(obj, do_unlink=True)
        if data and data.users == 0:
            if obj_type == "MESH":
                bpy.data.meshes.remove(data)
            elif obj_type == "ARMATURE":
                bpy.data.armatures.remove(data)


def main() -> None:
    args = arguments()
    if not 0.50 <= args.fill <= 0.98:
        raise ValueError("--fill must be between 0.50 and 0.98 to keep a visible safety margin")
    if args.resolution_x <= 0 or args.resolution_y <= 0:
        raise ValueError("Render resolution must be positive")
    angle_names = [value.strip().lower() for value in args.angles.split(",") if value.strip()]
    valid_angles = {"front", "reverse", "isometric", "top"}
    if not angle_names or any(value not in valid_angles for value in angle_names):
        raise ValueError("--angles must be a comma-separated subset of front,reverse,isometric,top")

    sys.path.insert(0, str(ROOT / "tools"))
    import hard_surface_geometry_presets as hsg

    manifest_path = Path(args.manifest)
    if not manifest_path.is_absolute():
        manifest_path = ROOT / manifest_path
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    entries: list[dict[str, Any]] = manifest["modules"]
    if args.module:
        requested = set(args.module)
        entries = [row for row in entries if row["moduleName"] in requested]
        found = {row["moduleName"] for row in entries}
        missing = sorted(requested - found)
        if missing:
            raise ValueError("Unknown moduleName values: " + ", ".join(missing))
    if not entries:
        raise ValueError("No canonical Level 2 modules selected")

    output = Path(args.output)
    if not output.is_absolute():
        output = ROOT / output
    output.mkdir(parents=True, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = args.resolution_x
    scene.render.resolution_y = args.resolution_y
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.studio_light = "paint.sl"
    scene.display.shading.color_type = "OBJECT"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.display.shading.curvature_ridge_factor = 1.35
    scene.display.shading.curvature_valley_factor = 1.1
    scene.display.shading.show_object_outline = True
    scene.display.shading.background_type = "WORLD"
    scene.world = bpy.data.worlds.new("__canonical_module_close_view_world")
    scene.world.color = (0.025, 0.032, 0.043)

    camera_data = bpy.data.cameras.new("__canonical_module_close_view_camera_data")
    camera_data.type = "ORTHO"
    camera_data.sensor_fit = "VERTICAL"
    camera = bpy.data.objects.new("__canonical_module_close_view_camera", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    aspect = args.resolution_x / args.resolution_y
    rows: list[dict[str, Any]] = []

    def frame_camera(target: Vector, corners: list[Vector], direction: Vector) -> None:
        direction.normalize()
        camera.location = target + direction * 25.0
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
        bpy.context.view_layer.update()
        rotation = camera.rotation_euler.to_quaternion()
        right = rotation @ Vector((1.0, 0.0, 0.0))
        up = rotation @ Vector((0.0, 1.0, 0.0))
        half_width = max(abs((point - target).dot(right)) for point in corners)
        half_height = max(abs((point - target).dot(up)) for point in corners)
        camera_data.ortho_scale = max(
            (2.0 * half_height) / args.fill,
            (2.0 * half_width) / (aspect * args.fill),
            0.01,
        )

    for index, entry in enumerate(entries, 1):
        fbx_path = ROOT / entry["fbxFilePath"]
        if not fbx_path.is_file():
            raise FileNotFoundError(fbx_path)
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(fbx_path), use_anim=False)
        imported = [obj for obj in bpy.data.objects if obj not in before]
        meshes = [obj for obj in imported if obj.type == "MESH"]
        if not meshes:
            raise RuntimeError(f"No mesh imported from {fbx_path}")
        for mesh_index, obj in enumerate(meshes):
            obj.color = (0.38, 0.52, 0.68, 1.0) if mesh_index % 2 == 0 else (0.52, 0.59, 0.66, 1.0)
        low, high, corners = bounds(meshes)
        center = (low + high) * 0.5
        creator = hsg.GEOMETRY_PRESETS[entry["geometryPreset"]]
        config_type = get_type_hints(creator).get("config")
        try:
            config = config_type() if config_type is not None else None
        except TypeError:
            config = None
        outward = getattr(config, "normal", None)
        if outward is None:
            outward = (1.0, 0.0, 0.0) if entry.get("geometryLayout") == "bulwark_side_skirt_array_v2" else (
                (0.0, 0.0, 1.0) if entry["slotId"] in {"APS", "ERA"} else (1.0, 0.0, 0.0)
            )
        facing = Vector(outward)
        directions = {
            "front": facing,
            "reverse": -facing,
            "isometric": facing + Vector((0.38, -0.34, 0.52)),
            "top": Vector((0.05, -0.06, 1.0)),
        }
        for angle in angle_names:
            frame_camera(center, corners, directions[angle].copy())
            output_path = output / f"{index:02d}_{entry['moduleName'].lower()}_{angle}.png"
            scene.render.filepath = str(output_path)
            bpy.ops.render.render(write_still=True)
            rows.append({
                "moduleName": entry["moduleName"],
                "variantId": entry["variantId"],
                "slotId": entry["slotId"],
                "geometryPreset": entry["geometryPreset"],
                "geometryLayout": entry.get("geometryLayout", ""),
                "angle": angle,
                "viewType": "module",
                "detailGroup": "",
                "meshCount": len(meshes),
                "fill": args.fill,
                "boundsMin": [float(value) for value in low],
                "boundsMax": [float(value) for value in high],
                "view": output_path.relative_to(ROOT).as_posix(),
                "fbx": fbx_path.relative_to(ROOT).as_posix(),
            })

        if args.detail_clusters:
            if entry.get("geometryLayout") != "bulwark_side_skirt_array_v2":
                raise ValueError("--detail-clusters currently supports bulwark_side_skirt_array_v2 only")
            side_positions_y = (-1.10, -0.55, 0.0, 0.55, 1.10)
            detail_count = 0
            for side_sign, side_name in ((-1.0, "port"), (1.0, "starboard")):
                for cassette_index, cassette_y in enumerate(side_positions_y, 1):
                    cluster_center = Vector((side_sign * 1.312, cassette_y, 0.40))
                    cluster_corners = [
                        cluster_center + Vector((dx, dy, dz))
                        for dx in (-0.12, 0.12)
                        for dy in (-0.15, 0.15)
                        for dz in (-0.09, 0.09)
                    ]
                    outward = Vector((side_sign, 0.0, 0.0))
                    cluster_directions = {
                        "front": outward,
                        "reverse": -outward,
                        "isometric": outward + Vector((0.0, -0.34, 0.52)),
                        "top": Vector((0.05, -0.06, 1.0)),
                    }
                    detail_id = f"{side_name}_B{cassette_index:02d}"
                    detail_count += 1
                    cluster_low = cluster_center - Vector((0.12, 0.15, 0.09))
                    cluster_high = cluster_center + Vector((0.12, 0.15, 0.09))
                    for angle in angle_names:
                        frame_camera(cluster_center, cluster_corners, cluster_directions[angle].copy())
                        output_path = output / f"{index:02d}_{entry['moduleName'].lower()}_{detail_id.lower()}_{angle}.png"
                        scene.render.filepath = str(output_path)
                        bpy.ops.render.render(write_still=True)
                        rows.append({
                            "moduleName": entry["moduleName"],
                            "variantId": entry["variantId"],
                            "slotId": entry["slotId"],
                            "geometryPreset": entry["geometryPreset"],
                            "geometryLayout": entry.get("geometryLayout", ""),
                            "angle": angle,
                            "viewType": "detail_cluster",
                            "detailGroup": detail_id,
                            "meshCount": len(meshes),
                            "fill": args.fill,
                            "boundsMin": [float(value) for value in cluster_low],
                            "boundsMax": [float(value) for value in cluster_high],
                            "view": output_path.relative_to(ROOT).as_posix(),
                            "fbx": fbx_path.relative_to(ROOT).as_posix(),
                        })
                    print(
                        f"[{index}/{len(entries)}] {entry['moduleName']} {detail_id} "
                        f"({len(angle_names)} close views)",
                        flush=True,
                    )
            if detail_count != 10:
                raise ValueError(f"Expected ten Bulwark cassette clusters, rendered {detail_count}")
        remove_imported(imported)
        print(f"[{index}/{len(entries)}] {entry['moduleName']} ({len(angle_names)} close views)", flush=True)

    index_path = output / "index.csv"
    with index_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)
    print(json.dumps({"moduleCount": len(entries), "viewCount": len(rows),
                      "fill": args.fill, "index": str(index_path)}, indent=2))


if __name__ == "__main__":
    main()
