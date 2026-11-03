"""Apply Wave 2/3/4 hard-surface details to the two manually authored mechs.

Atlas and Bastion have no tracked source builder. This script is the explicit,
reversible bridge for those sources: it adds only missing authored details,
rig-binds the new meshes with rigid weights, saves the source, and reexports the
existing FBX path without changing gameplay metadata or prefab references.
"""

from __future__ import annotations

import sys
from pathlib import Path

import bpy

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))
import RogueModelKit as rmk
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details

UNITS = (
    ("AtlasCraneMech", "atlas-crane-mech.blend", "AtlasCraneMech.fbx", None),
    ("BastionSiegeMech", "bastion-siege-mech.blend", "BastionSiegeMech.fbx", "quad"),
)
MODEL_DIR = REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "RogueCommandExpansionSample" / "Models"


def apply_unit(unit_name: str, source_name: str, fbx_name: str, leg_type: str | None) -> None:
    source = REPO_ROOT / "content" / "source-art" / source_name
    fbx_path = MODEL_DIR / fbx_name
    if not source.is_file():
        raise FileNotFoundError(source)
    bpy.ops.wm.open_mainfile(filepath=str(source))
    new_objects = apply_wave2_details(unit_name)
    new_objects.extend(apply_wave34_details(unit_name))
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    if len(armatures) != 1:
        raise ValueError(f"{unit_name} expected one authored armature, found {len(armatures)}")
    armature = armatures[0]
    if new_objects:
        rmk.assign_rigid_mesh_weights(new_objects, prefix=unit_name, armature_obj=armature, leg_type=leg_type)
        bpy.context.view_layer.update()
        bpy.ops.wm.save_as_mainfile(filepath=str(source))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH" and not obj.name.lower().startswith("preview")]
    if unit_name == "AtlasCraneMech":
        export_meshes = rmk.batch_and_join_by_material(
            meshes,
            prefix=unit_name,
            smooth_by_angle=True,
            weighted_normals=True,
            unity_batch_names=True,
        )
    else:
        export_meshes = meshes
    empties = [obj for obj in bpy.context.scene.objects if obj.type == "EMPTY" and not obj.name.lower().startswith("preview")]
    rmk.export_unity_fbx(fbx_path, export_meshes + [armature] + empties, include_empties=True, include_armature=True, bake_anim=True)
    print(f"{unit_name}: added {len(new_objects)} Wave 2/3/4 meshes; exported {fbx_path}")


if __name__ == "__main__":
    for unit in UNITS:
        apply_unit(*unit)
