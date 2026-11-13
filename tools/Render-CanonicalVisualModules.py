#!/usr/bin/env python3
"""Render a labeled top-down contact sheet of the canonical Level 2 modules.

Run with the pinned Blender 4.5.14 executable after
Build-CanonicalVisualModules.py. All scene changes are in memory; the source
blend and FBXs are read-only inputs for this review render.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any

import bpy
from mathutils import Matrix, Vector


ROOT = Path(__file__).resolve().parents[1]
SOURCE_BLEND = ROOT / "content/source-art/canonical-visual-modules.blend"
MANIFEST = ROOT / "content/source-art/canonical-visual-modules.json"


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default="artifacts/canonical-two-level/modules-contact-sheet.png")
    parser.add_argument("--columns", type=int, default=5)
    parser.add_argument("--tile-size", type=int, default=480)
    parser.add_argument("--view", choices=("top", "side", "isometric"), default="top")
    parser.add_argument("--module", help="Render one manifest module name or geometry preset id")
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def area_light(name: str, location: tuple[float, float, float], power: float, size: float) -> None:
    light_data = bpy.data.lights.new(name, "AREA")
    light_data.energy = power
    light_data.shape = "DISK"
    light_data.size = size
    light = bpy.data.objects.new(name, light_data)
    bpy.context.scene.collection.objects.link(light)
    light.location = location


def neutral_material(name: str, color: tuple[float, float, float], roughness: float = 0.75) -> bpy.types.Material:
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Roughness"].default_value = roughness
    return material


def object_bounds(objects: list[bpy.types.Object]) -> tuple[Vector, Vector]:
    corners = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    if not corners:
        raise ValueError("Module collection contains no renderable geometry")
    minimum = Vector((min(p.x for p in corners), min(p.y for p in corners), min(p.z for p in corners)))
    maximum = Vector((max(p.x for p in corners), max(p.y for p in corners), max(p.z for p in corners)))
    return minimum, maximum


def main() -> None:
    args = arguments()
    entries: list[dict[str, Any]] = json.loads(MANIFEST.read_text(encoding="utf-8"))["modules"]
    if not entries:
        raise ValueError("Canonical visual module manifest is empty")
    if args.columns <= 0 or args.tile_size <= 0:
        raise ValueError("Contact sheet columns and tile size must be positive")
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))
    if args.module:
        entries = [
            entry for entry in entries
            if entry["moduleName"] == args.module or entry.get("geometryPreset") == args.module
        ]
        if len(entries) != 1:
            raise ValueError(f"--module must resolve exactly one canonical module, found {len(entries)}")
    bpy.context.scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene = bpy.context.scene
    scene.render.resolution_x = args.columns * args.tile_size
    rows = math.ceil(len(entries) / args.columns)
    scene.render.resolution_y = rows * args.tile_size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "Medium High Contrast"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    scene.render.image_settings.color_depth = "8"

    cell = 3.0
    grid_width = args.columns * cell
    grid_height = rows * cell
    camera_data = bpy.data.cameras.new("RCLModuleContactCamera")
    camera_data.type = "ORTHO"
    camera_data.sensor_fit = "VERTICAL"
    camera_data.ortho_scale = grid_height
    camera = bpy.data.objects.new("RCLModuleContactCamera", camera_data)
    scene.collection.objects.link(camera)
    if args.view == "top":
        camera.location = (0.0, 0.0, max(grid_width, grid_height) * 2.5)
        camera.rotation_euler = (0.0, 0.0, 0.0)
    elif args.view == "side":
        camera.location = (grid_height * 1.5, 0.0, grid_height * 1.5)
        camera.rotation_euler = (Vector((0.0, 0.0, 0.0)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    else:
        camera.location = (0.0, -grid_height * 1.5, grid_height * 1.5)
        camera.rotation_euler = (Vector((0.0, 0.0, 0.0)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = camera

    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    if background:
        background.inputs["Color"].default_value = (0.025, 0.032, 0.045, 1.0)
        background.inputs["Strength"].default_value = 0.35
    light_scale = (grid_height / 36.0) ** 2
    area_light("RCLContactKey", (-grid_width * 0.25, grid_height * 0.18, grid_height), 9000.0 * light_scale, grid_width * 0.8)
    area_light("RCLContactFill", (grid_width * 0.3, -grid_height * 0.12, grid_height * 0.7), 4500.0 * light_scale, grid_width * 0.65)

    tile_materials = [
        neutral_material("RCLContactTileDark", (0.075, 0.09, 0.112)),
        neutral_material("RCLContactTileLight", (0.095, 0.112, 0.135)),
    ]
    # Hide all module source and nested LOD objects first. Each tile then shows
    # only its direct LOD0 source meshes, with no overlaps from other modules.
    for collection in bpy.data.collections:
        if collection.name.startswith("RCLModule_"):
            collection.hide_render = True
            for obj in collection.objects:
                if obj.type == "MESH":
                    obj.hide_render = True
    for index, entry in enumerate(entries):
        column = index % args.columns
        row = index // args.columns
        center_x = (column - (args.columns - 1) / 2.0) * cell
        center_y = ((rows - 1) / 2.0 - row) * cell
        collection = bpy.data.collections.get(entry["moduleName"])
        if collection is None:
            # Blender truncates collection identifiers to 63 characters. Pick
            # the longest direct-mesh collection that is a prefix of the
            # manifest name; nested LOD collections include an extra suffix.
            candidates = [
                item for item in bpy.data.collections
                if item.name.startswith("RCLModule_")
                and entry["moduleName"].startswith(item.name)
                and any(obj.type == "MESH" for obj in item.objects)
            ]
            if candidates:
                collection = max(candidates, key=lambda item: len(item.name))
        if collection is None:
            raise ValueError(f"Source blend has no collection for {entry['moduleName']}")
        objects = [obj for obj in collection.objects if obj.type == "MESH"]
        if not objects:
            raise ValueError(f"Source collection has no mesh objects: {entry['moduleName']}")
        minimum, maximum = object_bounds(objects)
        center = (minimum + maximum) * 0.5
        footprint = max(maximum.x - minimum.x, maximum.y - minimum.y, 0.001)
        preview_scale = min((cell * 0.72) / footprint, 6.0)
        scale_about_center = Matrix.Translation(center) @ Matrix.Scale(preview_scale, 4) @ Matrix.Translation(-center)
        for obj in objects:
            obj.matrix_world = scale_about_center @ obj.matrix_world
        bpy.context.view_layer.update()
        minimum, maximum = object_bounds(objects)
        offset = Vector((center_x - (minimum.x + maximum.x) / 2.0,
                         center_y - (minimum.y + maximum.y) / 2.0,
                         -minimum.z + 0.02))
        for obj in objects:
            obj.location += offset
            obj.hide_render = False
        collection.hide_render = False

        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(center_x, center_y, 0.0))
        tile = bpy.context.object
        tile.name = f"RCLContactTile_{index:02d}"
        tile.dimensions = (cell * 0.96, cell * 0.96, 0.07)
        tile.location.z = -0.035
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        tile.data.materials.append(tile_materials[(column + row) % 2])

    scene.render.filepath = str((ROOT / args.output).resolve())
    Path(scene.render.filepath).parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.render.render(write_still=True)
    print(f"Rendered {len(entries)} canonical visual modules to {scene.render.filepath}", flush=True)


if __name__ == "__main__":
    main()
