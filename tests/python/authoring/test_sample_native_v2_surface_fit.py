"""Blender-side Exact Boolean, BVHTree clearance, closed-solid containment, and source-versioning checks for sample-native v2 units & factory."""

from __future__ import annotations

import sys
import unittest
from collections import deque
from pathlib import Path
from typing import Any

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

REPO_ROOT = Path(__file__).resolve().parents[3]
TOOLS_DIR = REPO_ROOT / "tools"
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

V2_FILES: dict[str, tuple[str, str, str]] = {
    "SandlanceScout": ("sandlance-scout.blend", "sandlance-scout-v2.blend", "Build-SandlanceScoutRiggedModel.py"),
    "ColossusSiegePlatform": ("colossus-siege.blend", "colossus-siege-v2.blend", "Build-ColossusSiegeRiggedModel.py"),
    "SkirmishStrikeBike": ("skirmish-strike-bike.blend", "skirmish-strike-bike-v2.blend", "Build-SkirmishStrikeBikeRiggedModel.py"),
    "SurgeArcTank": ("surge-arc-tank.blend", "surge-arc-tank-v2.blend", "Build-SurgeArcTankRiggedModel.py"),
    "OverlordCommandHQ": ("overlord-command-hq.blend", "overlord-command-hq-v2.blend", "Build-OverlordCommandHQRiggedModel.py"),
    "IroncladWalker": ("ironclad-walker.blend", "ironclad-walker-v2.blend", "Build-IroncladWalkerModel.py"),
    "AtlasCraneMech": ("atlas-crane-mech.blend", "atlas-crane-mech-v2.blend", "Build-AtlasCraneMechModel.py"),
    "BastionSiegeMech": ("bastion-siege-mech.blend", "bastion-siege-mech-v2.blend", "Build-BastionSiegeMechModel.py"),
    "ArachnidHexapod": ("arachnid-hexapod.blend", "arachnid-hexapod-v2.blend", "Build-ArachnidHexapodRiggedModel.py"),
    "ArachnidHexapodFactory": ("arachnidhexapodfactory.blend", "arachnidhexapodfactory-v2.blend", "Build-ArachnidHexapodFactoryModel.py"),
}

RECIPE_PROP = "rcl_v2_recipe"

_PARITY_DIRS = (
    Vector((0.731, 0.419, 0.538)).normalized(),
    Vector((-0.517, 0.783, 0.345)).normalized(),
    Vector((0.291, -0.612, 0.735)).normalized(),
)


def _world_bounds(obj: bpy.types.Object) -> tuple[float, float, float, float, float, float]:
    pts = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        min(p.x for p in pts),
        max(p.x for p in pts),
        min(p.y for p in pts),
        max(p.y for p in pts),
        min(p.z for p in pts),
        max(p.z for p in pts),
    )


def _bbox_volume(bounds: tuple[float, float, float, float, float, float]) -> float:
    x0, x1, y0, y1, z0, z1 = bounds
    return max(0.0, x1 - x0) * max(0.0, y1 - y0) * max(0.0, z1 - z0)


def _mesh_bvh(obj: bpy.types.Object) -> BVHTree:
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    return bvh


def _is_closed_solid(obj: bpy.types.Object) -> bool:
    if obj.type != "MESH" or len(obj.data.polygons) < 4:
        return False
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    has_boundary = any(e.is_boundary for e in bm.edges)
    bm.free()
    return not has_boundary


def _point_inside_bvh(point: Vector, bvh: BVHTree) -> bool:
    votes = 0
    for direction in _PARITY_DIRS:
        origin = point
        hits = 0
        for _ in range(64):
            hit_loc, _normal, _idx, _dist = bvh.ray_cast(origin, direction, 100.0)
            if hit_loc is None:
                break
            hits += 1
            origin = hit_loc + direction * 1e-4
        if hits % 2 == 1:
            votes += 1
    return votes >= 2


_ALLOWED_TELESCOPING_PAIRS = (
    ("ClevisPin", "ClevisEye"),
    ("ClevisPin", "PelvisBlock"),
    ("ClevisPin", "ThighArmor"),
    ("ClevisPin", "HipActuator"),
    ("ClevisPin", "FemurBoom"),
    ("ClevisEye", "ThighArmor"),
    ("ClevisEye", "FemurBoom"),
    ("HydraulicRod", "LoadStrut"),
    ("HydraulicRod", "FemurBoom"),
    ("HydraulicRam", "FemurBoom"),
    ("InnerRamRod", "FemurBoom"),
    ("PressureRing", "FemurBoom"),
    ("WiperSealCollar", "FemurBoom"),
    ("FeedThroatCollar", "GatlingBracer"),
    ("Pelvis_", "ArmoredCarapace"),
    ("ThoraxCollar", "ArmoredCarapace"),
    ("MidHip", "HipFairing"),
)


def _is_allowed_telescoping_pair(inner_name: str, outer_name: str) -> bool:
    return any(a in inner_name and b in outer_name for a, b in _ALLOWED_TELESCOPING_PAIRS)


def _find_invalid_closed_solid_containments(
    render_objs: list[bpy.types.Object],
    bvh_cache: dict[str, BVHTree],
    bounds_cache: dict[str, tuple[float, float, float, float, float, float]],
) -> list[str]:
    closed_objs = [o for o in render_objs if _is_closed_solid(o)]
    volumes = {o.name: _bbox_volume(bounds_cache[o.name]) for o in closed_objs}
    invalid: list[str] = []
    tol = 0.002

    for inner in closed_objs:
        ix0, ix1, iy0, iy1, iz0, iz1 = bounds_cache[inner.name]
        ivol = volumes[inner.name]
        if ivol <= 0.0:
            continue
        mw = inner.matrix_world
        verts = inner.data.vertices
        if not verts:
            continue
        step = max(1, len(verts) // 128)
        sample_pts = [mw @ verts[i].co for i in range(0, len(verts), step)]
        center_pt = Vector(((ix0 + ix1) * 0.5, (iy0 + iy1) * 0.5, (iz0 + iz1) * 0.5))

        for outer in closed_objs:
            if outer.name == inner.name or volumes[outer.name] <= ivol * 1.05:
                continue
            if _is_allowed_telescoping_pair(inner.name, outer.name):
                continue
            ox0, ox1, oy0, oy1, oz0, oz1 = bounds_cache[outer.name]
            if (
                ix0 < ox0 - tol
                or ix1 > ox1 + tol
                or iy0 < oy0 - tol
                or iy1 > oy1 + tol
                or iz0 < oz0 - tol
                or iz1 > oz1 + tol
            ):
                continue

            outer_bvh = bvh_cache[outer.name]
            inner_bvh = bvh_cache[inner.name]
            surfaces_overlap = bool(inner_bvh.overlap(outer_bvh))

            if not surfaces_overlap:
                if _point_inside_bvh(center_pt, outer_bvh) and _point_inside_bvh(sample_pts[0], outer_bvh):
                    invalid.append(f"{inner.name} fully contained inside {outer.name}")
                continue

            # Even with boundary contact, flag if every sampled vertex is strictly inside outer
            strictly_inside = 0
            for pt in sample_pts:
                _loc, _n, _idx, dist = outer_bvh.find_nearest(pt)
                if dist is not None and dist > 1e-4 and _point_inside_bvh(pt, outer_bvh):
                    strictly_inside += 1
            if strictly_inside == len(sample_pts):
                invalid.append(f"{inner.name} buried inside {outer.name}")

    return invalid


def audit_sample_native_v2_scene(unit_name: str) -> dict[str, Any]:
    """Verify v2 recipe tags, UVs/Colors, 1 material, no .001 duplicates, zero tire overlaps, connectivity, and containment."""
    for arm in bpy.data.objects:
        if arm.type == "ARMATURE":
            arm.data.pose_position = "REST"
    bpy.context.view_layer.update()

    render_objs = [
        o for o in bpy.data.objects
        if o.type == "MESH"
        and "_COLLIDER" not in o.name
        and not o.name.endswith("_ShieldBubble")
        and not o.name.lower().startswith("preview")
    ]
    missing_uv: list[str] = []
    missing_color: list[str] = []
    bad_mat_count: list[str] = []
    duplicate_suffixes: list[str] = []
    tagged_recipes: dict[str, str] = {}

    for o in render_objs:
        if o.name.endswith((".001", ".002", ".003")):
            duplicate_suffixes.append(o.name)
        if not o.data.uv_layers or "UVMap" not in o.data.uv_layers:
            missing_uv.append(o.name)
        ca = o.data.color_attributes.get("Color")
        if ca is None or ca.domain != "CORNER" or ca.data_type != "BYTE_COLOR":
            missing_color.append(o.name)
        if len(o.data.materials) != 1 or o.data.materials[0] is None:
            bad_mat_count.append(f"{o.name}({len(o.data.materials)})")
        if RECIPE_PROP in o:
            tagged_recipes[o.name] = str(o[RECIPE_PROP])

    bvh_cache: dict[str, BVHTree] = {o.name: _mesh_bvh(o) for o in render_objs}
    bounds_cache: dict[str, tuple[float, float, float, float, float, float]] = {
        o.name: _world_bounds(o) for o in render_objs
    }

    wheel_objs = [o for o in render_objs if any(k in o.name for k in ("TireCarcass", "TreadBlocks"))]
    body_objs = [
        o for o in render_objs
        if any(k in o.name for k in ("Fender_", "_Stay_", "TraumaCabShell", "ForwardCab", "Dozer_", "EarthSpade", "ArmorSkirt"))
        and "Mudflap" not in o.name
    ]
    tire_overlaps: list[str] = []
    for b in body_objs:
        bbvh = bvh_cache[b.name]
        for w in wheel_objs:
            if bvh_cache[w.name].overlap(bbvh):
                tire_overlaps.append(f"{b.name} <-> {w.name}")

    # Hull BFS connectivity audit: every renderable mesh must connect to the chassis/torso/platform root within 8 mm
    disconnected_from_hull: list[str] = []
    root_obj = next(
        (
            o for o in render_objs
            if o.name.endswith((
                "Armor_Torso",
                "Armor_Hull",
                "Armor_SpineChassis",
                "Armor_ChassisFrame",
                "Armor_Chassis",
                "Armor_MainHull",
                "MainPlatform",
                "BasePlatform",
            ))
        ),
        None,
    )
    if root_obj is None and render_objs:
        root_obj = max(render_objs, key=lambda o: _bbox_volume(bounds_cache[o.name]))

    if root_obj is not None:
        pts_cache: dict[str, list[Vector]] = {}
        for o in render_objs:
            mw = o.matrix_world
            pts = [mw @ v.co for v in o.data.vertices]
            for p in o.data.polygons:
                pts.append(mw @ p.center)
            pts_cache[o.name] = pts

        def _pair_dist(a_name: str, b_name: str) -> float:
            if bvh_cache[a_name].overlap(bvh_cache[b_name]):
                return 0.0
            best = 999.0
            b_bvh = bvh_cache[b_name]
            for v in pts_cache[a_name]:
                loc, _n, _idx, d = b_bvh.find_nearest(v)
                if loc is not None and d < best:
                    best = d
            a_bvh = bvh_cache[a_name]
            for v in pts_cache[b_name]:
                loc, _n, _idx, d = a_bvh.find_nearest(v)
                if loc is not None and d < best:
                    best = d
            return best

        names = [o.name for o in render_objs]
        adj: dict[str, list[str]] = {n: [] for n in names}
        for i, a_name in enumerate(names):
            ax0, ax1, ay0, ay1, az0, az1 = bounds_cache[a_name]
            for b_name in names[i + 1 :]:
                bx0, bx1, by0, by1, bz0, bz1 = bounds_cache[b_name]
                if (
                    bx0 > ax1 + 0.02
                    or ax0 > bx1 + 0.02
                    or by0 > ay1 + 0.02
                    or ay0 > by1 + 0.02
                    or bz0 > az1 + 0.02
                    or az0 > bz1 + 0.02
                ):
                    continue
                if _pair_dist(a_name, b_name) <= 0.008:
                    adj[a_name].append(b_name)
                    adj[b_name].append(a_name)

        visited = {root_obj.name}
        queue = deque([root_obj.name])
        while queue:
            cur = queue.popleft()
            for nxt in adj[cur]:
                if nxt not in visited:
                    visited.add(nxt)
                    queue.append(nxt)
        disconnected_from_hull = sorted(n for n in names if n not in visited)

    invalid_containment = _find_invalid_closed_solid_containments(render_objs, bvh_cache, bounds_cache)

    return {
        "unit": unit_name,
        "mesh_count": len(render_objs),
        "total_faces": sum(len(o.data.polygons) for o in render_objs),
        "tagged_v2_objects": len(tagged_recipes),
        "missing_uv": missing_uv,
        "missing_color": missing_color,
        "bad_mat_count": bad_mat_count,
        "duplicate_suffixes": duplicate_suffixes,
        "tire_overlaps": tire_overlaps,
        "disconnected_from_hull": disconnected_from_hull,
        "invalid_containment": invalid_containment,
        "passed": (
            not missing_uv
            and not missing_color
            and not bad_mat_count
            and not duplicate_suffixes
            and not tire_overlaps
            and not disconnected_from_hull
            and not invalid_containment
            and len(tagged_recipes) >= 3
        ),
    }


class SampleNativeV2SurfaceFitTests(unittest.TestCase):
    @classmethod
    def tearDownClass(cls) -> None:
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_v2_blends_and_builders_preserve_v1_sources(self) -> None:
        source_dir = REPO_ROOT / "content" / "source-art"
        for unit, (v1_name, v2_name, builder_name) in V2_FILES.items():
            with self.subTest(unit=unit):
                self.assertTrue((source_dir / v1_name).is_file(), f"Missing preserved v1 source: {v1_name}")
                self.assertTrue((source_dir / v2_name).is_file(), f"Missing v2 source: {v2_name}")
                builder_text = (TOOLS_DIR / builder_name).read_text(encoding="utf-8-sig")
                self.assertIn(v2_name, builder_text, f"{builder_name} must point to {v2_name}")

    def test_all_ten_v2_blends_pass_boolean_attribute_connectivity_and_containment_audit(self) -> None:
        source_dir = REPO_ROOT / "content" / "source-art"
        for unit, (_v1_name, v2_name, _builder) in V2_FILES.items():
            with self.subTest(unit=unit):
                bpy.ops.wm.open_mainfile(filepath=str(source_dir / v2_name))
                audit = audit_sample_native_v2_scene(unit)
                self.assertEqual(audit["missing_uv"], [], f"{unit} missing UVMap")
                self.assertEqual(audit["missing_color"], [], f"{unit} missing CORNER BYTE_COLOR Color")
                self.assertEqual(audit["bad_mat_count"], [], f"{unit} multi/empty material meshes")
                self.assertEqual(audit["duplicate_suffixes"], [], f"{unit} .001 duplicates")
                self.assertEqual(audit["tire_overlaps"], [], f"{unit} tire/body overlaps")
                self.assertEqual(audit["disconnected_from_hull"], [], f"{unit} disconnected parts from hull")
                self.assertEqual(audit["invalid_containment"], [], f"{unit} invalid closed-solid containment")
                self.assertGreaterEqual(audit["tagged_v2_objects"], 3, f"{unit} missing Exact Boolean/v2 recipe tags")
                self.assertTrue(audit["passed"], f"{unit} audit failed: {audit}")

    def test_bastion_siege_mech_v2_resolves_floating_ring_dampers_and_hatch(self) -> None:
        bpy.ops.wm.open_mainfile(filepath=str(REPO_ROOT / "content" / "source-art" / "bastion-siege-mech-v2.blend"))
        render_objs = [
            o for o in bpy.data.objects
            if o.type == "MESH"
            and "_COLLIDER" not in o.name
            and not o.name.endswith("_ShieldBubble")
            and not o.name.lower().startswith("preview")
        ]
        names = {o.name for o in render_objs}
        self.assertFalse(
            any(n.startswith(("BastionArmor_TurretRingGuard_", "BastionSiegeMechArmor_TurretRingGuard_")) for n in names),
            "Bastion floating TurretRingGuard cluster must be removed in v2",
        )
        for obj in render_objs:
            if any(tok in obj.name for tok in ("LiftingLug_", "LugWeldPad_", "Gusset_")):
                _x0, _x1, _y0, _y1, z0, _z1 = _world_bounds(obj)
                self.assertLess(z0, 2.0, f"Floating duplicate roof detail {obj.name} above Z=2.0m (z0={z0:.3f})")

        dampers = [o for o in render_objs if "CarapaceDamper" in o.name]
        self.assertGreaterEqual(len(dampers), 1, "Expected Bastion CarapaceDamper meshes")
        for damper in dampers:
            groups = [g.name for g in damper.vertex_groups]
            self.assertNotIn("Bone_BarrelRecoil", groups, f"{damper.name} must not bind to Bone_BarrelRecoil")
            if groups:
                self.assertEqual(["Bone_Hull"], groups, f"{damper.name} must bind strictly to Bone_Hull")

        turret_roof = bpy.data.objects.get("BastionSiegeMechTurret_BaseShell")
        hatch_coaming = bpy.data.objects.get("BastionSiegeMechArmor_AccessHatch_CoamingRim")
        if turret_roof is not None and hatch_coaming is not None:
            roof_bvh = _mesh_bvh(turret_roof)
            mw = hatch_coaming.matrix_world
            min_gap = min(
                roof_bvh.find_nearest(mw @ v.co)[3]
                for v in hatch_coaming.data.vertices
                if roof_bvh.find_nearest(mw @ v.co)[3] is not None
            )
            self.assertLessEqual(min_gap, 0.0025, f"Bastion hatch coaming gap from turret roof too large: {min_gap:.4f}m")

    def test_atlas_crane_mech_v2_resolves_hatch_clearance_foot_clevis_and_thigh_pistons(self) -> None:
        bpy.ops.wm.open_mainfile(filepath=str(REPO_ROOT / "content" / "source-art" / "atlas-crane-mech-v2.blend"))
        render_objs = [
            o for o in bpy.data.objects
            if o.type == "MESH"
            and "_COLLIDER" not in o.name
            and not o.name.endswith("_ShieldBubble")
            and not o.name.lower().startswith("preview")
        ]
        names = {o.name for o in render_objs}
        self.assertFalse(
            any(n.startswith(("AtlasCraneMechArmor_LiftingLug_", "AtlasCraneMechArmor_LugWeldPad_", "AtlasCraneMechArmor_Gusset_")) for n in names),
            "Unsupported Atlas floating lugs/pads/gussets must be omitted in v2",
        )

        for side in ("L", "R"):
            clevis = bpy.data.objects.get(f"AtlasCraneMechLeg_AnkleClevis_{side}")
            self.assertIsNotNone(clevis, f"Missing AtlasCraneMechLeg_AnkleClevis_{side}")
            self.assertEqual([f"Bone_Foot_{side}"], [g.name for g in clevis.vertex_groups])

            foot_support = bpy.data.objects.get(f"AtlasCraneMechLeg_FootAnkleSupport_{side}")
            if foot_support is not None:
                self.assertEqual([f"Bone_Foot_{side}"], [g.name for g in foot_support.vertex_groups])

            pistons = [o for o in render_objs if o.name.startswith(f"AtlasCraneMechPiston_Leg_{side}_")]
            for piston in pistons:
                self.assertEqual(
                    [f"Bone_Thigh_{side}"],
                    [g.name for g in piston.vertex_groups],
                    f"{piston.name} must bind to Bone_Thigh_{side}",
                )

            hip_actuators = [o for o in render_objs if o.name.startswith(f"AtlasCraneMechLeg_HipActuator_{side}_")]
            for act in hip_actuators:
                self.assertEqual(
                    [f"Bone_Thigh_{side}"],
                    [g.name for g in act.vertex_groups],
                    f"{act.name} must bind to Bone_Thigh_{side}",
                )

        coaming = bpy.data.objects.get("AtlasCraneMechArmor_AccessHatch_CoamingRim")
        turret_shell = bpy.data.objects.get("AtlasCraneMechTurret_BaseShell")
        mantlet = next((o for o in render_objs if "Mantlet" in o.name), None)
        if coaming is not None and turret_shell is not None:
            shell_bvh = _mesh_bvh(turret_shell)
            coam_bvh = _mesh_bvh(coaming)
            if coam_bvh.overlap(shell_bvh):
                min_gap = 0.0
            else:
                mw = coaming.matrix_world
                min_gap = min(
                    shell_bvh.find_nearest(mw @ v.co)[3]
                    for v in coaming.data.vertices
                    if shell_bvh.find_nearest(mw @ v.co)[3] is not None
                )
            self.assertLessEqual(min_gap, 0.0025, f"Atlas hatch coaming must seat on turret roof (gap={min_gap:.4f}m)")
            if mantlet is not None:
                self.assertFalse(coam_bvh.overlap(_mesh_bvh(mantlet)), "Atlas hatch coaming must not touch mantlet")


if __name__ == "__main__":
    argv = [sys.argv[0]] + (sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else [])
    unittest.main(argv=argv)
