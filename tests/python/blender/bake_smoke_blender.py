"""Headless smoke test: blender -b --python bake_smoke.py -- <out_dir>"""
import sys
from pathlib import Path

import bpy

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / "tools"))
import RogueModelKit as rmk  # noqa: E402

out = Path(sys.argv[sys.argv.index("--") + 1])
bpy.ops.wm.read_factory_settings(use_empty=True)
mat = bpy.data.materials.new("M")
mat.use_nodes = True
bsdf = mat.node_tree.nodes["Principled BSDF"]
bsdf.inputs["Base Color"].default_value = (0.2, 0.4, 0.1, 1)
bsdf.inputs["Metallic"].default_value = 0.5
bsdf.inputs["Roughness"].default_value = 0.5
objs = []
for i, loc in enumerate([(0, 0, 0), (3, 0, 0)]):
    bpy.ops.mesh.primitive_cube_add(location=loc)
    o = bpy.context.active_object
    o.data.materials.append(mat)
    objs.append(o)
maps = rmk.bake_pbr_texture_atlas(objs, "Smoke", out, resolution=256)
print("BAKED", {k: str(v) for k, v in maps.items()})
