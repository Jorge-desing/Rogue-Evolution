#!/usr/bin/env python3
"""Apply Material Maker PBR texture maps to a Blender model for hard-surface preview.

Loads the four-map contract (Albedo, MetallicGloss, Normal, Emission) into a
Principled BSDF node setup:
- Albedo: sRGB Base Color.
- MetallicGloss: Non-Color texture; Red channel drives Metallic, Alpha channel
  (Smoothness) is inverted to drive Roughness.
- Normal: Non-Color texture connected via a ShaderNodeNormalMap (OpenGL tangent space).
- Emission: sRGB texture driving Emission Color.
- Preserves the mesh 'Color' RGBA vertex wear attribute, LODs, and all `Socket_*` empties.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]

BLENDER_CANDIDATES = [
    shutil.which("blender"),
    str(REPO_ROOT / ".tools" / "Blender-4.5.14" / "blender-4.5.14-windows-x64" / "blender.exe"),
    r"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe",
]


def find_blender() -> str | None:
    for candidate in BLENDER_CANDIDATES:
        if candidate and Path(candidate).is_file():
            return str(candidate)
    return None


BLENDER_SCRIPT_SNIPPET = """
import sys
import json
from pathlib import Path
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
model_path = Path(argv[0])
texture_dir = Path(argv[1])
prefix = argv[2]
output_blend = Path(argv[3]) if len(argv) > 3 and argv[3] else None
report_path = Path(argv[4]) if len(argv) > 4 and argv[4] else None

bpy.ops.wm.read_factory_settings(use_empty=True)
if model_path.suffix.lower() == ".blend":
    bpy.ops.wm.open_mainfile(filepath=str(model_path))
elif model_path.suffix.lower() == ".fbx":
    bpy.ops.import_scene.fbx(filepath=str(model_path))
else:
    raise RuntimeError(f"Unsupported model extension: {model_path.suffix}")

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH" and not o.name.endswith(("_COLLIDER", "_ShieldBubble"))]
if not meshes:
    raise RuntimeError(f"No valid mesh objects found in {model_path}")

map_paths = {
    "Albedo": texture_dir / f"{prefix}_Albedo.png",
    "MetallicGloss": texture_dir / f"{prefix}_MetallicGloss.png",
    "Normal": texture_dir / f"{prefix}_Normal.png",
    "Emission": texture_dir / f"{prefix}_Emission.png",
}
missing_maps = [str(path) for path in map_paths.values() if not path.is_file()]
if missing_maps:
    raise RuntimeError("Missing required PBR maps: " + ", ".join(missing_maps))

mat_name = f"{prefix}_SpaceMilitary_PBR"
mat = bpy.data.materials.get(mat_name) or bpy.data.materials.new(name=mat_name)
mat.use_nodes = True
nodes = mat.node_tree.nodes
links = mat.node_tree.links

nodes.clear()
out_node = nodes.new("ShaderNodeOutputMaterial")
out_node.location = (400, 0)
bsdf = nodes.new("ShaderNodeBsdfPrincipled")
bsdf.location = (100, 0)
links.new(bsdf.outputs["BSDF"], out_node.inputs["Surface"])
mat["rcl_quality_profile"] = "tactical-hd-v2"
mat["rcl_shader_contract"] = "RogueCommand/VehiclePBR_VertexWear"
mat["rcl_map_contract"] = "Albedo;MetallicGloss;Normal;Emission"
maps_bound = []

def configure_texture_node(node, image, colorspace):
    image.colorspace_settings.name = colorspace
    node.interpolation = "Linear"
    node.extension = "CLIP"
    node.image = image

albedo_path = map_paths["Albedo"]
img_alb = bpy.data.images.load(str(albedo_path))
node_alb = nodes.new("ShaderNodeTexImage")
node_alb.location = (-300, 200)
configure_texture_node(node_alb, img_alb, "sRGB")
links.new(node_alb.outputs["Color"], bsdf.inputs["Base Color"])
maps_bound.append("Albedo")

mg_path = map_paths["MetallicGloss"]
img_mg = bpy.data.images.load(str(mg_path))
node_mg = nodes.new("ShaderNodeTexImage")
node_mg.location = (-600, -100)
configure_texture_node(node_mg, img_mg, "Non-Color")

sep = nodes.new("ShaderNodeSeparateColor")
sep.location = (-350, -100)
links.new(node_mg.outputs["Color"], sep.inputs["Color"])
links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])

inv = nodes.new("ShaderNodeInvert")
inv.location = (-150, -250)
links.new(node_mg.outputs["Alpha"], inv.inputs["Color"])
links.new(inv.outputs["Color"], bsdf.inputs["Roughness"])
maps_bound.append("MetallicGloss")

norm_path = map_paths["Normal"]
img_norm = bpy.data.images.load(str(norm_path))
node_norm = nodes.new("ShaderNodeTexImage")
node_norm.location = (-400, -450)
configure_texture_node(node_norm, img_norm, "Non-Color")

nmap = nodes.new("ShaderNodeNormalMap")
nmap.location = (-150, -450)
links.new(node_norm.outputs["Color"], nmap.inputs["Color"])
links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
maps_bound.append("Normal")

emit_path = map_paths["Emission"]
img_emit = bpy.data.images.load(str(emit_path))
node_emit = nodes.new("ShaderNodeTexImage")
node_emit.location = (-300, -700)
configure_texture_node(node_emit, img_emit, "sRGB")
links.new(node_emit.outputs["Color"], bsdf.inputs["Emission Color"])
if "Emission Strength" in bsdf.inputs:
    bsdf.inputs["Emission Strength"].default_value = 2.0
maps_bound.append("Emission")

for mesh in meshes:
    mesh.data.materials.clear()
    mesh.data.materials.append(mat)

if output_blend is not None:
    output_blend.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(output_blend))

if report_path is not None:
    vertex_wear_attributes = sorted({
        attribute.name
        for mesh in meshes
        for attribute in getattr(mesh.data, "color_attributes", [])
    })
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps({
        "kind": "rogueCommandBlenderPbrPreview",
        "qualityProfile": "tactical-hd-v2",
        "shaderContract": "RogueCommand/VehiclePBR_VertexWear",
        "mapContract": maps_bound,
        "maps": maps_bound,
        "meshCount": len(meshes),
        "socketCount": sum(1 for obj in bpy.context.scene.objects if obj.name.startswith("Socket_")),
        "lodGroupCount": sum(1 for obj in bpy.context.scene.objects if obj.type == "EMPTY" and obj.name.startswith("LOD")),
        "vertexWearAttributes": vertex_wear_attributes,
        "sourceModel": str(model_path),
        "outputBlend": str(output_blend) if output_blend is not None else None,
    }, indent=2), encoding="utf-8")

print(f"APPLIED_MATERIAL_MAKER_TEXTURES: {mat_name} on {len(meshes)} meshes")
"""


def apply_textures(
    model_path: Path,
    texture_dir: Path,
    prefix: str,
    output_blend: Path | None = None,
    report_path: Path | None = None,
) -> bool:
    blender = find_blender()
    if blender is None:
        raise RuntimeError("Blender 4.5 executable not found")

    tmp_script = REPO_ROOT / "artifacts" / "_apply_mm_script.py"
    tmp_script.parent.mkdir(parents=True, exist_ok=True)
    tmp_script.write_text(BLENDER_SCRIPT_SNIPPET, encoding="utf-8")
    try:
        args = [
            blender,
            "-b",
            "--python",
            str(tmp_script),
            "--",
            str(model_path.resolve()),
            str(texture_dir.resolve()),
            prefix,
            str(output_blend.resolve()) if output_blend else "",
            str(report_path.resolve()) if report_path else "",
        ]
        proc = subprocess.run(args, capture_output=True, text=True, timeout=120)
        if proc.returncode != 0 or "APPLIED_MATERIAL_MAKER_TEXTURES" not in proc.stdout:
            print(f"STDOUT:\n{proc.stdout[-1500:]}\nSTDERR:\n{proc.stderr[-1500:]}")
            return False
        return True
    finally:
        tmp_script.unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--texture-dir", type=Path, required=True)
    parser.add_argument("--prefix", required=True)
    parser.add_argument("--output-blend", type=Path, default=None)
    parser.add_argument("--report", type=Path, default=None)
    args = parser.parse_args()

    ok = apply_textures(args.model, args.texture_dir, args.prefix, args.output_blend, args.report)
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
