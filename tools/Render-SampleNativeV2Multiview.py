"""Render 4-angle multiview contact sheets (Front, Side, Top, Isometric) for all 10 sample-native v2 blends."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import bpy
from mathutils import Vector

REPO_ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = REPO_ROOT / "artifacts" / "previews"

UNITS = (
    ("SandlanceScout", "sandlance-scout-v2.blend"),
    ("ColossusSiegePlatform", "colossus-siege-v2.blend"),
    ("SkirmishStrikeBike", "skirmish-strike-bike-v2.blend"),
    ("SurgeArcTank", "surge-arc-tank-v2.blend"),
    ("OverlordCommandHQ", "overlord-command-hq-v2.blend"),
    ("IroncladWalker", "ironclad-walker-v2.blend"),
    ("AtlasCraneMech", "atlas-crane-mech-v2.blend"),
    ("BastionSiegeMech", "bastion-siege-mech-v2.blend"),
    ("ArachnidHexapod", "arachnid-hexapod-v2.blend"),
    ("ArachnidHexapodFactory", "arachnidhexapodfactory-v2.blend"),
)


def _scene_bounds() -> tuple[Vector, float]:
    pts: list[Vector] = []
    for o in bpy.data.objects:
        if (
            o.type == "MESH"
            and "_COLLIDER" not in o.name
            and not o.name.endswith("_ShieldBubble")
            and not o.name.lower().startswith("preview")
        ):
            pts.extend(o.matrix_world @ Vector(c) for c in o.bound_box)
    if not pts:
        return Vector((0.0, 0.0, 0.5)), 2.0
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return (mn + mx) * 0.5, max((mx - mn).length, 1.0)


def _render_view(cam_obj: bpy.types.Object, eye: Vector, target: Vector, out_path: Path) -> None:
    cam_obj.location = eye
    direction = (target - eye).normalized()
    cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.render.filepath = str(out_path)
    bpy.ops.render.render(write_still=True)


def render_unit_multiview(unit: str, blend_name: str) -> Path:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(REPO_ROOT / "content" / "source-art" / blend_name))
    for obj in bpy.data.objects:
        if obj.type == "ARMATURE":
            obj.data.pose_position = "REST"
        if (
            "_COLLIDER" in obj.name
            or obj.name.endswith("_ShieldBubble")
            or obj.name.lower().startswith("preview")
        ):
            obj.hide_render = True
            obj.hide_viewport = True
    bpy.context.view_layer.update()

    center, span = _scene_bounds()
    dist = span * 1.45

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 480
    scene.render.image_settings.file_format = "PNG"

    cam_data = bpy.data.cameras.new("MultiviewCam")
    cam_data.lens = 45.0
    cam_obj = bpy.data.objects.new("MultiviewCam", cam_data)
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj

    views = (
        ("front", Vector((0.0, dist, 0.25 * span))),
        ("side", Vector((dist, 0.0, 0.22 * span))),
        ("top", Vector((0.0, -0.05 * dist, dist * 1.15))),
        ("iso", Vector((dist * 0.68, dist * 0.72, dist * 0.55))),
    )
    panel_paths: list[Path] = []
    for label, offset in views:
        p = OUT_DIR / f"_tmp_{unit}_{label}.png"
        _render_view(cam_obj, center + offset, center, p)
        panel_paths.append(p)

    # Combine 2x2 contact sheet using Blender image buffers (no external packages required)
    w, h = 640, 480
    grid = bpy.data.images.new(f"{unit}_MultiviewGrid", width=w * 2, height=h * 2, alpha=False)
    buf = [0.12] * (w * 2 * h * 2 * 4)
    offsets = ((0, h), (w, h), (0, 0), (w, 0))
    for p, (ox, oy) in zip(panel_paths, offsets):
        img = bpy.data.images.load(str(p), check_existing=False)
        px = list(img.pixels[:])
        for row in range(h):
            src_start = row * w * 4
            dst_start = ((oy + row) * (w * 2) + ox) * 4
            buf[dst_start:dst_start + w * 4] = px[src_start:src_start + w * 4]
        bpy.data.images.remove(img)
        p.unlink(missing_ok=True)

    grid.pixels.foreach_set(buf)
    final_path = OUT_DIR / f"{unit}_v2_multiview.png"
    grid.filepath_raw = str(final_path)
    grid.file_format = "PNG"
    grid.save()
    bpy.data.images.remove(grid)
    print(f"[multiview] Saved {final_path}")
    return final_path


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("units", nargs="*", help="Subset of asset names to render (default: all 10).")
    args = parser.parse_args(argv)

    unit_map = dict(UNITS)
    selected = args.units or [u for u, _b in UNITS]
    for unit in selected:
        if unit not in unit_map:
            raise KeyError(f"Unknown sample-native v2 asset: {unit}")
        render_unit_multiview(unit, unit_map[unit])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
