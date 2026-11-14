"""Regression checks for the Bulwark's canonical Level 2 ERA binding."""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
from pathlib import Path

import unittest

from tests.python.helpers import REPO_ROOT

sys.path.insert(0, str(REPO_ROOT / "tools"))

from hard_surface_geometry_presets import ERA_BRICK_PRESET_ID, GEOMETRY_PRESETS


VARIANT_ID = "tactical-operations:bulwark-siege-tank-battleship-tungsten"
EXPECTED_MODULE_ASSET = (
    "assets/roguecommandexpansionsample/visualmodules/prefabs/"
    "rclmodule_bulwark_siege_tank_battleship_tungsten.prefab"
)
EXPECTED_LAYOUT = "bulwark_side_skirt_array_v2"
CURRENT_BULWARK_FBX = (
    REPO_ROOT
    / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/BulwarkSiegeTank.fbx"
)
CURRENT_BULWARK_FBX_SHA256 = "7a7bbf693bf63b8cb131d1c428b466f3f5dfa5a6d68502f89a0f1c2be2d809e5"


class BulwarkEraCanonicalIntegrationTests(unittest.TestCase):
    def test_battleship_variant_declares_one_canonical_era_module(self) -> None:
        pack_path = REPO_ROOT / "content/packs/tactical-operations/content.json"
        pack = json.loads(pack_path.read_text(encoding="utf-8"))
        unit = next(row for row in pack["units"] if row["id"] == "tactical-operations:bulwark-siege-tank")
        variant = next(row for row in unit["visualVariants"] if row["id"] == VARIANT_ID)

        self.assertEqual(
            variant.get("modules"),
            [{"slotId": "ERA", "assetName": EXPECTED_MODULE_ASSET}],
        )

    def test_variant_binds_reusable_era_preset_to_side_skirt_layout(self) -> None:
        roles_path = REPO_ROOT / "content/source-art/canonical-visual-module-roles.json"
        registry = json.loads(roles_path.read_text(encoding="utf-8"))
        rows = [row for row in registry["variantRoles"] if row.get("variantId") == VARIANT_ID]
        self.assertEqual(len(rows), 1)

        row = rows[0]
        self.assertEqual(row["slotId"], "ERA")
        self.assertEqual(row["visualEngineeringRole"], "armor_cassette")
        self.assertEqual(row["geometryPreset"], ERA_BRICK_PRESET_ID)
        self.assertEqual(row["geometryLayout"], EXPECTED_LAYOUT)

        role = registry["roleDefinitions"][row["visualEngineeringRole"]]
        self.assertIn("ERA", role["allowedSlots"])
        self.assertIn(ERA_BRICK_PRESET_ID, GEOMETRY_PRESETS)

    def test_side_cassettes_contact_the_current_level1_fbx_and_clear_the_belt(self) -> None:
        blender_exe = (
            REPO_ROOT
            / ".tools/Blender-4.5.14/blender-4.5.14-windows-x64/blender.exe"
        )
        if not blender_exe.exists():
            self.skipTest("Blender 4.5.14 executable not found")

        def sha256(path: Path) -> str:
            digest = hashlib.sha256()
            with path.open("rb") as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(chunk)
            return digest.hexdigest()

        source_blend = REPO_ROOT / "content/source-art/bulwarksiegetank.blend"
        source_hash_before = sha256(source_blend)
        fbx_hash_before = sha256(CURRENT_BULWARK_FBX)
        self.assertEqual(fbx_hash_before, CURRENT_BULWARK_FBX_SHA256)

        blender_probe = f"""
import hashlib, json, runpy
from pathlib import Path
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

root = Path.cwd()
builder = runpy.run_path(str(root / "tools/Build-CanonicalVisualModules.py"))
builder["rmk"].clear_scene()
fbx_path = root / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/BulwarkSiegeTank.fbx"
before_import = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(fbx_path), use_anim=False)
host_meshes = [obj for obj in bpy.data.objects if obj not in before_import and obj.type == "MESH"]
host = next(obj for obj in host_meshes if obj.name == "BulwarkSiegeTank_Batch_PrimaryArmor")
belt = next(obj for obj in host_meshes if obj.name == "BulwarkSiegeTank_Batch_Tread")
entry = next(row for row in builder["read_module_entries"]() if row["variantId"] == {VARIANT_ID!r})
seed = int(hashlib.sha256(entry["variantId"].encode("utf-8")).hexdigest()[:8], 16)
module_objects, _ = builder["build_module_geometry"](entry, seed)
bpy.context.view_layer.update()
depsgraph = bpy.context.evaluated_depsgraph_get()
host_tree = BVHTree.FromObject(host, depsgraph)
belt_tree = BVHTree.FromObject(belt, depsgraph)

def world_bounds(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return [min(p[axis] for p in points) for axis in range(3)], [max(p[axis] for p in points) for axis in range(3)]

backing_plates = sorted(
    (obj for obj in module_objects if obj.name.endswith("_BackingPlate")),
    key=lambda obj: obj.name,
)
assert len(backing_plates) == 10, f"expected ten side cassette backing plates, got {{len(backing_plates)}}"
expected_y = (-1.10, -0.55, 0.0, 0.55, 1.10)
for side in (-1.0, 1.0):
    side_backing_plates = [obj for obj in backing_plates if (1.0 if obj.location.x > 0.0 else -1.0) == side]
    assert len(side_backing_plates) == 5, f"expected five ERA backing plates on side {{side}}, got {{len(side_backing_plates)}}"
    actual_y = sorted(round(float(obj.location.y), 3) for obj in side_backing_plates)
    assert actual_y == list(expected_y), f"unexpected Bulwark side cassette positions on {{side}}: {{actual_y}}"
max_fit_error_mm = 0.0
min_belt_clearance_mm = float("inf")
support_samples = 0
maximum_inward_intrusion_mm = 0.0
for backing in backing_plates:
    side = 1.0 if backing.location.x > 0.0 else -1.0
    lo, hi = world_bounds(backing)
    contact_x = lo[0] if side > 0.0 else hi[0]
    prefix = backing.name[:-len("_BackingPlate")]
    cluster = [obj for obj in module_objects if obj.name.startswith(prefix + "_")]
    for part in cluster:
        part_lo, part_hi = world_bounds(part)
        inward = contact_x - part_lo[0] if side > 0.0 else part_hi[0] - contact_x
        maximum_inward_intrusion_mm = max(maximum_inward_intrusion_mm, inward * 1000.0)
    for fy in (0.05, 0.50, 0.95):
        y = lo[1] + (hi[1] - lo[1]) * fy
        for fz in (0.05, 0.50, 0.95):
            z = lo[2] + (hi[2] - lo[2]) * fz
            # Probe just outside the intended backing face, then require the
            # actual imported Level 1 armor surface at the same plane.
            probe = Vector((contact_x + side * 0.010, y, z))
            host_hit = host_tree.find_nearest(probe)
            assert host_hit[0] is not None, f"no Level 1 host hit for {{backing.name}} at {{(y, z)}}"
            fit_error = abs(host_hit[0].x - contact_x) * 1000.0
            normal_alignment = host_hit[1].dot(Vector((side, 0.0, 0.0)))
            max_fit_error_mm = max(max_fit_error_mm, fit_error)
            assert normal_alignment >= 0.99, f"{{backing.name}} did not hit its outward skirt face: {{normal_alignment}}"
            belt_hit = belt_tree.find_nearest(Vector((contact_x, y, z)))
            assert belt_hit[0] is not None, f"tread BVH returned no nearest point near {{backing.name}}"
            min_belt_clearance_mm = min(min_belt_clearance_mm, belt_hit[3] * 1000.0)
            support_samples += 1

belt_overlap_pairs = 0
for obj in module_objects:
    if obj.type != "MESH":
        continue
    part_tree = BVHTree.FromObject(obj, depsgraph)
    if part_tree is not None:
        belt_overlap_pairs += len(part_tree.overlap(belt_tree))

result = {{
    "backingPlateCount": len(backing_plates),
    "backingPlatesPerSide": [sum(1 for obj in backing_plates if obj.location.x < 0.0),
                              sum(1 for obj in backing_plates if obj.location.x > 0.0)],
    "supportSamples": support_samples,
    "maxFitErrorMm": max_fit_error_mm,
    "minBeltClearanceMm": min_belt_clearance_mm,
    "beltOverlapPairs": belt_overlap_pairs,
    "maximumInwardIntrusionMm": maximum_inward_intrusion_mm,
    "frontCassetteObjectsGenerated": sum("GlacisERA" in obj.name for obj in module_objects),
}}
print("__BULWARK_FIT_START__" + json.dumps(result) + "__BULWARK_FIT_END__")
"""
        proc = subprocess.run(
            [
                str(blender_exe),
                "--background",
                "--factory-startup",
                "--python-exit-code",
                "1",
                "--python-expr",
                blender_probe,
            ],
            capture_output=True,
            text=True,
            cwd=str(REPO_ROOT),
        )
        self.assertEqual(
            proc.returncode,
            0,
            f"Blender fit probe failed:\nSTDERR:\n{proc.stderr}\nSTDOUT:\n{proc.stdout}",
        )
        self.assertIn(
            "__BULWARK_FIT_START__",
            proc.stdout,
            f"Blender did not return fit evidence:\nSTDERR:\n{proc.stderr}\nSTDOUT:\n{proc.stdout}",
        )
        report = json.loads(proc.stdout.split("__BULWARK_FIT_START__", 1)[1].split("__BULWARK_FIT_END__", 1)[0])
        self.assertEqual(report["backingPlateCount"], 10)
        self.assertEqual(report["backingPlatesPerSide"], [5, 5])
        self.assertEqual(report["supportSamples"], 90)
        self.assertLessEqual(report["maxFitErrorMm"], 1.0)
        self.assertGreaterEqual(report["minBeltClearanceMm"], 35.0)
        self.assertEqual(report["beltOverlapPairs"], 0)
        self.assertLessEqual(report["maximumInwardIntrusionMm"], 0.1)
        self.assertEqual(report["frontCassetteObjectsGenerated"], 0)
        self.assertEqual(sha256(CURRENT_BULWARK_FBX), fbx_hash_before)
        self.assertEqual(sha256(source_blend), source_hash_before)


if __name__ == "__main__":
    unittest.main()
