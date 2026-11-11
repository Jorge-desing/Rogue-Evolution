"""Reusable geometry-only hard-surface presets for Rogue Command assets.

Material Maker/PBR presets remain separate. These recipes create Blender mesh
geometry and use built-in Boolean/bevel primitives so headless builds do not
depend on Hardflow being installed.
"""

from __future__ import annotations

import math
from dataclasses import asdict, dataclass
from typing import Callable, Mapping

import bpy
import bmesh
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

import RogueModelKit as rmk


SERVICE_HATCH_PRESET_ID = "service_hatch_recessed_v1"
ERA_BRICK_PRESET_ID = "era_brick_cassette_v1"


@dataclass(frozen=True)
class ServiceHatchConfig:
    """Dimensions and placement for a horizontal, +Z-facing service hatch.

    ``outer_size`` describes the coaming footprint in local/world XY. The
    generated opening leaves ``frame_width`` on each side, and the lid leaves
    ``panel_clearance`` inside that opening. Positive Y is the front edge;
    the default hinge runs along X on the negative-Y edge.
    """

    center_xy: tuple[float, float]
    outer_size: tuple[float, float]
    support_top_z: float
    recess_depth: float = 0.025
    frame_width: float = 0.015
    panel_clearance: float = 0.003
    panel_thickness: float = 0.016
    frame_thickness: float = 0.012
    hinge_axis: str = "X"
    hinge_side: str = "NEGATIVE_Y"
    fastener_radius: float = 0.006

    def __post_init__(self) -> None:
        if self.hinge_axis.upper() != "X":
            raise ValueError("service_hatch_recessed_v1 currently supports an X-axis hinge")
        if self.hinge_side.upper() not in {"NEGATIVE_Y", "POSITIVE_Y"}:
            raise ValueError("hinge_side must be NEGATIVE_Y or POSITIVE_Y")
        if not all(math.isfinite(value) for value in (
            *self.center_xy, *self.outer_size, self.support_top_z, self.recess_depth,
            self.frame_width, self.panel_clearance, self.panel_thickness,
            self.frame_thickness, self.fastener_radius,
        )):
            raise ValueError("service hatch placement and dimensions must be finite")
        if min(self.outer_size) <= 0.0:
            raise ValueError("outer_size dimensions must be positive")
        if min(self.recess_depth, self.frame_width, self.panel_thickness, self.frame_thickness) <= 0.0:
            raise ValueError("service hatch depths and widths must be positive")
        if self.panel_clearance < 0.0 or self.fastener_radius <= 0.0:
            raise ValueError("panel clearance must be nonnegative and fastener radius positive")
        if self.panel_thickness + 0.001 > self.recess_depth + 1.0e-9:
            raise ValueError("panel must fit in the recess and leave at least 1 mm below its floor")
        if min(self.opening_size) <= self.panel_clearance * 2.0:
            raise ValueError("service hatch frame leaves no room for the lid")
        if min(self.panel_size) <= 0.0:
            raise ValueError("service hatch panel dimensions must be positive")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)

    @property
    def opening_size(self) -> tuple[float, float]:
        return (
            self.outer_size[0] - self.frame_width * 2.0,
            self.outer_size[1] - self.frame_width * 2.0,
        )

    @property
    def panel_size(self) -> tuple[float, float]:
        opening = self.opening_size
        return (
            opening[0] - self.panel_clearance * 2.0,
            opening[1] - self.panel_clearance * 2.0,
        )

    @property
    def floor_z(self) -> float:
        return self.support_top_z - self.recess_depth

    @property
    def panel_bottom_z(self) -> float:
        return self.floor_z + 0.001

    @property
    def panel_top_z(self) -> float:
        return self.panel_bottom_z + self.panel_thickness

    @classmethod
    def from_existing_parts(
        cls,
        existing_parts: Mapping[str, bpy.types.Object],
        support_top_z: float,
        *,
        recess_depth: float = 0.025,
        panel_clearance: float = 0.003,
        panel_thickness: float | None = None,
        frame_thickness: float = 0.012,
        hinge_axis: str = "X",
        hinge_side: str = "NEGATIVE_Y",
    ) -> "ServiceHatchConfig":
        """Derive a compatible config from an existing panel/coaming assembly.

        Required keys are ``panel`` and ``coaming``. The hinge and latch are
        optional for measurement but are checked by the assembly validator.
        Bounds are evaluated in world space, so existing built-in Bulwark parts
        can be described without renaming or rebuilding them.
        """
        panel = existing_parts.get("panel")
        coaming = existing_parts.get("coaming")
        if panel is None or coaming is None:
            raise ValueError("existing_parts must contain mesh objects named by panel and coaming keys")
        panel_min, panel_max = object_world_bounds(panel)
        frame_min, frame_max = object_world_bounds(coaming)
        outer_size = (frame_max.x - frame_min.x, frame_max.y - frame_min.y)
        panel_size = (panel_max.x - panel_min.x, panel_max.y - panel_min.y)
        opening_size = (panel_size[0] + panel_clearance * 2.0,
                        panel_size[1] + panel_clearance * 2.0)
        frame_width = min((outer_size[0] - opening_size[0]) * 0.5,
                          (outer_size[1] - opening_size[1]) * 0.5)
        if frame_width <= 0.0:
            raise ValueError("existing panel/coaming bounds do not leave a positive frame width")
        thickness = panel_thickness if panel_thickness is not None else panel_max.z - panel_min.z
        return cls(
            center_xy=((frame_min.x + frame_max.x) * 0.5, (frame_min.y + frame_max.y) * 0.5),
            outer_size=outer_size,
            support_top_z=float(support_top_z),
            recess_depth=float(recess_depth),
            frame_width=frame_width,
            panel_clearance=float(panel_clearance),
            panel_thickness=float(thickness),
            frame_thickness=float(frame_thickness),
            hinge_axis=hinge_axis,
            hinge_side=hinge_side,
        )


def object_world_bounds(obj: bpy.types.Object) -> tuple[Vector, Vector]:
    """Return the axis-aligned world bounds of a mesh object."""
    if obj is None or obj.type != "MESH":
        raise ValueError("service hatch parts must be mesh objects")
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        Vector(min(point[axis] for point in corners) for axis in range(3)),
        Vector(max(point[axis] for point in corners) for axis in range(3)),
    )


def _mesh_geometry(obj: bpy.types.Object) -> tuple[list[Vector], list[tuple[int, int, int]]]:
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


def _surface_gap(a: bpy.types.Object, b: bpy.types.Object) -> float:
    geometry_a, geometry_b = _mesh_geometry(a), _mesh_geometry(b)
    tree_a = BVHTree.FromPolygons(geometry_a[0], geometry_a[1], all_triangles=True, epsilon=0.0)
    tree_b = BVHTree.FromPolygons(geometry_b[0], geometry_b[1], all_triangles=True, epsilon=0.0)
    best = float("inf")
    for source, target in ((geometry_a[0], tree_b), (geometry_b[0], tree_a)):
        for point in source:
            hit = target.find_nearest(point)
            if hit and hit[3] is not None:
                best = min(best, float(hit[3]))
                if best <= 1e-7:
                    return 0.0
    return best


def validate_existing_service_hatch(
    existing_parts: Mapping[str, bpy.types.Object],
    config: ServiceHatchConfig,
    *,
    support: bpy.types.Object | None = None,
    fit_tolerance_m: float = 0.002,
) -> dict[str, object]:
    """Measure an existing hatch assembly against the shared geometry config.

    This supports a model's existing panel/coaming/hinge/latch objects, such as
    Bulwark's four-part hatch, without creating duplicates or replacing its mesh
    data. It returns diagnostics; callers decide whether to move or reshape the
    existing parts using the returned placement/config values.
    """
    required = ("panel", "coaming", "hinge", "latch")
    missing = [key for key in required if existing_parts.get(key) is None]
    if missing:
        raise ValueError("existing hatch assembly is missing required parts: " + ", ".join(missing))
    panel = existing_parts["panel"]
    coaming = existing_parts["coaming"]
    hinge = existing_parts["hinge"]
    latch = existing_parts["latch"]
    parts = {key: existing_parts[key] for key in required}
    for key, obj in parts.items():
        if obj.type != "MESH":
            raise ValueError(f"existing hatch part {key!r} must be a mesh object")

    panel_min, panel_max = object_world_bounds(panel)
    coaming_min, coaming_max = object_world_bounds(coaming)
    hinge_min, hinge_max = object_world_bounds(hinge)
    latch_min, latch_max = object_world_bounds(latch)
    cx, cy = config.center_xy
    half_outer_x, half_outer_y = config.outer_size[0] * 0.5, config.outer_size[1] * 0.5
    tolerance = max(float(fit_tolerance_m), 0.0)
    panel_within_coaming = (
        panel_min.x >= coaming_min.x - tolerance and panel_max.x <= coaming_max.x + tolerance
        and panel_min.y >= coaming_min.y - tolerance and panel_max.y <= coaming_max.y + tolerance
    )
    panel_top_recess_mm = (config.support_top_z - panel_max.z) * 1000.0
    coaming_center_error_m = math.hypot(
        (coaming_min.x + coaming_max.x) * 0.5 - cx,
        (coaming_min.y + coaming_max.y) * 0.5 - cy,
    )
    expected_panel_top = config.panel_top_z
    expected_panel_bottom = config.panel_bottom_z
    panel_z_fit_mm = max(
        abs(panel_min.z - expected_panel_bottom),
        abs(panel_max.z - expected_panel_top),
    ) * 1000.0
    hinge_near_configured_edge = (
        hinge_min.y >= cy - half_outer_y - tolerance
        and hinge_max.y <= cy + half_outer_y + tolerance
        and hinge_min.z <= config.support_top_z + config.frame_thickness + 0.05
    )
    latch_within_service_area = (
        latch_min.x >= cx - half_outer_x - tolerance
        and latch_max.x <= cx + half_outer_x + tolerance
        and latch_min.y >= cy - half_outer_y - tolerance
        and latch_max.y <= cy + half_outer_y + tolerance
    )
    contacts: dict[str, float | None] = {}
    if support is not None:
        contacts["coamingToSupportGapMm"] = _surface_gap(coaming, support) * 1000.0
        contacts["latchToPanelGapMm"] = _surface_gap(latch, panel) * 1000.0
        contacts["hingeToPanelGapMm"] = _surface_gap(hinge, panel) * 1000.0
    checks = {
        "panelWithinCoamingBounds": panel_within_coaming,
        "coamingCenteredOnConfig": coaming_center_error_m <= tolerance,
        "panelRecessedBelowSupport": panel_top_recess_mm >= -tolerance * 1000.0,
        "panelMatchesConfigThicknessAndHeight": panel_z_fit_mm <= tolerance * 1000.0,
        "hingeInsideConfiguredEdgeBounds": hinge_near_configured_edge,
        "latchInsideServiceHatchFootprint": latch_within_service_area,
    }
    if support is not None:
        checks["coamingSeatedOnSupport"] = contacts["coamingToSupportGapMm"] <= tolerance * 1000.0
        checks["hingeContactsPanel"] = contacts["hingeToPanelGapMm"] <= tolerance * 1000.0
        checks["latchContactsPanel"] = contacts["latchToPanelGapMm"] <= tolerance * 1000.0
    return {
        "geometryPreset": SERVICE_HATCH_PRESET_ID,
        "config": config.to_dict(),
        "checks": checks,
        "verdict": "PASS" if all(checks.values()) else "FAIL",
        "measurements": {
            "panelBoundsWorld": [list(panel_min), list(panel_max)],
            "coamingBoundsWorld": [list(coaming_min), list(coaming_max)],
            "hingeBoundsWorld": [list(hinge_min), list(hinge_max)],
            "latchBoundsWorld": [list(latch_min), list(latch_max)],
            "panelTopRecessMm": panel_top_recess_mm,
            "panelConfigFitMm": panel_z_fit_mm,
            "coamingCenterErrorMm": coaming_center_error_m * 1000.0,
            **contacts,
        },
    }


def _apply_recess_boolean(target: bpy.types.Object, config: ServiceHatchConfig) -> dict[str, object]:
    """Cut the opening into a horizontal support mesh with Blender's Exact solver."""
    if target is None or target.type != "MESH":
        raise ValueError("service hatch recess requires a mesh support target")
    before_min, before_max = object_world_bounds(target)
    opening_x, opening_y = config.opening_size
    cutter_depth = config.recess_depth + 0.010
    cutter_z = config.support_top_z + (0.005 - config.recess_depth) * 0.5
    bpy.ops.mesh.primitive_cube_add(
        size=1.0,
        location=(config.center_xy[0], config.center_xy[1], cutter_z),
    )
    cutter = bpy.context.object
    cutter.name = f"{target.name}_ServiceHatchPreset_Cutter"
    cutter.dimensions = (opening_x, opening_y, cutter_depth)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    modifier = target.modifiers.new(f"{SERVICE_HATCH_PRESET_ID}_ExactRecess", "BOOLEAN")
    modifier.operation = "DIFFERENCE"
    modifier.solver = "EXACT"
    modifier.object = cutter
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    with bpy.context.temp_override(
        object=target,
        active_object=target,
        selected_objects=[target],
        selected_editable_objects=[target],
    ):
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.data.objects.remove(cutter, do_unlink=True)

    bm = bmesh.new()
    bm.from_mesh(target.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1.0e-5)
    bmesh.ops.dissolve_degenerate(bm, dist=1.0e-5, edges=list(bm.edges))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(target.data)
    bm.free()
    target.data.update()
    bpy.context.view_layer.update()

    after_min, after_max = object_world_bounds(target)
    bounds_preserved = all(
        abs(before_min[axis] - after_min[axis]) <= 1.0e-5
        and abs(before_max[axis] - after_max[axis]) <= 1.0e-5
        for axis in range(3)
    )
    if not bounds_preserved:
        raise RuntimeError(f"{SERVICE_HATCH_PRESET_ID} unexpectedly changed {target.name} bounds")
    target["rcl_hard_surface_geometry_preset"] = SERVICE_HATCH_PRESET_ID
    return {
        "solver": "EXACT",
        "openingSize": [opening_x, opening_y],
        "recessDepthMm": config.recess_depth * 1000.0,
        "floorZ": config.floor_z,
        "supportBoundsPreserved": bounds_preserved,
    }


def create_service_hatch_preset(
    *,
    prefix: str,
    config: ServiceHatchConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    cut_target: bpy.types.Object | None = None,
) -> list[bpy.types.Object]:
    """Build a seated, recessed hatch with coaming, hinge, latch, and fasteners.

    The support cut is optional. Canonical utility-sponson builds pass their
    generated pannier mesh; a Bulwark recipe that already cut the rebate can
    call this with ``cut_target=None`` and use ``config_from_existing_parts`` /
    ``validate_existing_service_hatch`` to share the same fit rules without
    replacing its authored four-part hierarchy.
    """
    if not prefix:
        raise ValueError("service hatch prefix must not be empty")
    if cut_target is not None:
        _apply_recess_boolean(cut_target, config)

    cx, cy = config.center_xy
    outer_x, outer_y = config.outer_size
    opening_x, opening_y = config.opening_size
    frame = config.frame_width
    frame_z = config.support_top_z + config.frame_thickness * 0.5
    frame_half_x, frame_half_y = outer_x * 0.5, outer_y * 0.5
    frame_centers = (
        ("FrameRail_Left", (cx - frame_half_x + frame * 0.5, cy), (frame, outer_y)),
        ("FrameRail_Right", (cx + frame_half_x - frame * 0.5, cy), (frame, outer_y)),
        ("FrameRail_Front", (cx, cy + frame_half_y - frame * 0.5), (opening_x, frame)),
        ("FrameRail_Rear", (cx, cy - frame_half_y + frame * 0.5), (opening_x, frame)),
    )
    created: list[bpy.types.Object] = []
    for suffix, center_xy, size_xy in frame_centers:
        obj = rmk.make_box(
            f"{prefix}_{suffix}",
            (size_xy[0], size_xy[1], config.frame_thickness),
            (center_xy[0], center_xy[1], frame_z),
            armor_material,
            bevel=0.002,
            bevel_segments=1,
            smooth=False,
        )
        created.append(obj)

    panel_z = config.panel_bottom_z + config.panel_thickness * 0.5
    panel = rmk.make_box(
        f"{prefix}_Lid",
        (config.panel_size[0], config.panel_size[1], config.panel_thickness),
        (cx, cy, panel_z),
        armor_material,
        bevel=0.003,
        bevel_segments=1,
        smooth=False,
    )
    created.append(panel)

    hinge_sign = -1.0 if config.hinge_side.upper() == "NEGATIVE_Y" else 1.0
    hinge_y = cy + hinge_sign * (frame_half_y - frame * 0.4)
    hinge_z = config.support_top_z + 0.007
    leaf_offset_y = hinge_sign * 0.018
    hinge_x_positions = (-outer_x * 0.20, outer_x * 0.20)
    for index, x_offset in enumerate(hinge_x_positions, 1):
        x = cx + x_offset
        body_leaf = rmk.make_box(
            f"{prefix}_HingeBodyLeaf_{index}",
            (outer_x * 0.22, 0.024, 0.006),
            (x, hinge_y + leaf_offset_y, config.support_top_z + 0.003),
            hardware_material,
            bevel=0.001,
            bevel_segments=1,
            smooth=False,
        )
        lid_leaf = rmk.make_box(
            f"{prefix}_HingeLidLeaf_{index}",
            (outer_x * 0.22, 0.026, 0.006),
            (x, hinge_y - hinge_sign * 0.012, config.panel_top_z + 0.005),
            hardware_material,
            bevel=0.001,
            bevel_segments=1,
            smooth=False,
        )
        barrel = rmk.make_cylinder(
            f"{prefix}_HingeBarrel_{index}",
            radius=0.008,
            depth=outer_x * 0.22,
            location=(x, hinge_y, hinge_z),
            material=hardware_material,
            rotation=(0.0, math.pi * 0.5, 0.0),
            vertices=12,
            bevel=0.001,
        )
        created.extend((body_leaf, lid_leaf, barrel))

    latch_sign = -hinge_sign
    latch_y = cy + latch_sign * (frame_half_y - frame * 0.4)
    keeper = rmk.make_box(
        f"{prefix}_DogLatchKeeper",
        (outer_x * 0.22, 0.022, 0.008),
        (cx, latch_y + latch_sign * 0.012, config.support_top_z + 0.004),
        hardware_material,
        bevel=0.001,
        bevel_segments=1,
        smooth=False,
    )
    lever = rmk.make_box(
        f"{prefix}_DogLatchLever",
        (outer_x * 0.24, 0.014, 0.008),
        (cx, latch_y - latch_sign * 0.006, config.panel_top_z + 0.004),
        hardware_material,
        bevel=0.001,
        bevel_segments=1,
        smooth=False,
    )
    pivot = rmk.make_cylinder(
        f"{prefix}_DogLatchPivot",
        radius=0.006,
        depth=0.008,
        location=(cx + outer_x * 0.08, latch_y - latch_sign * 0.006, config.panel_top_z + 0.006),
        material=hardware_material,
        vertices=10,
        bevel=0.001,
    )
    created.extend((keeper, lever, pivot))

    fastener_x = frame_half_x - frame * 0.5
    fastener_y = frame_half_y - frame * 0.5
    fastener_z = config.support_top_z + config.frame_thickness + 0.003
    for x_sign in (-1.0, 1.0):
        for y_sign in (-1.0, 1.0):
            fastener = rmk.make_cylinder(
                f"{prefix}_FrameFastener_{'L' if x_sign < 0.0 else 'R'}_{'R' if y_sign < 0.0 else 'F'}",
                radius=config.fastener_radius,
                depth=0.006,
                location=(cx + x_sign * fastener_x, cy + y_sign * fastener_y, fastener_z),
                material=hardware_material,
                vertices=10,
                bevel=0.001,
            )
            created.append(fastener)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = SERVICE_HATCH_PRESET_ID
    return created


def apply_service_hatch_to_existing_parts(
    existing_parts: Mapping[str, bpy.types.Object],
    config: ServiceHatchConfig,
    *,
    support: bpy.types.Object | None = None,
) -> dict[str, object]:
    """Validate and label an existing panel/coaming/hinge/latch assembly.

    This is the non-destructive route for existing Bulwark geometry. It does
    not duplicate or replace the four named meshes; callers may use the config
    and report to position or dimension them in their own idempotent recipe.
    """
    report = validate_existing_service_hatch(existing_parts, config, support=support)
    if report["verdict"] == "PASS":
        for obj in existing_parts.values():
            if obj is not None and obj.type == "MESH":
                obj["rcl_hard_surface_geometry_preset"] = SERVICE_HATCH_PRESET_ID
    return report


@dataclass(frozen=True)
class EraBrickConfig:
    """Placement and scale for a single reusable bolt-on ERA cassette.

    The origin is the center of the backing plate's contact face. Local +Z is
    the outward support normal, local +Y follows the projected world-up axis,
    and local +X completes a right-handed frame. ``size`` is shell width,
    height, and outward depth; the support plate overhangs the shell.
    """

    center: tuple[float, float, float]
    normal: tuple[float, float, float]
    size: tuple[float, float, float] = (0.22, 0.13, 0.055)
    backing_overhang: float = 0.010
    backing_thickness: float = 0.010
    seam_width: float = 0.004
    seam_depth: float = 0.003
    edge_margin: float = 0.012
    corner_bevel: float = 0.006
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.size, self.backing_overhang,
                  self.backing_thickness, self.seam_width, self.seam_depth,
                  self.edge_margin, self.corner_bevel, self.fastener_radius,
                  self.roll_degrees)
        if not all(math.isfinite(value) for value in values):
            raise ValueError("ERA brick placement and dimensions must be finite")
        if min(self.size) <= 0.0:
            raise ValueError("ERA brick width, height, and depth must be positive")
        if Vector(self.normal).length <= 1.0e-8:
            raise ValueError("ERA brick support normal must not be zero")
        if min(self.backing_overhang, self.backing_thickness, self.seam_width,
               self.seam_depth, self.corner_bevel, self.fastener_radius) <= 0.0:
            raise ValueError("ERA brick backing, seam, bevel, and fastener dimensions must be positive")
        width, height, depth = self.size
        if self.seam_width * 2.0 + self.edge_margin * 2.0 >= min(width, height):
            raise ValueError("ERA brick is too small for its recessed cross seams and edge margin")
        if self.seam_depth >= depth * 0.35:
            raise ValueError("ERA cross seams must remain shallow cuts in the cassette shell")
        if self.corner_bevel >= min(width, height, depth) * 0.24:
            raise ValueError("ERA cassette bevel is too large for the brick dimensions")
        if self.fastener_radius * 2.0 >= min(width, height) * 0.22:
            raise ValueError("ERA fasteners are too large for this cassette")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def _era_frame(config: EraBrickConfig) -> Quaternion:
    normal = Vector(config.normal).normalized()
    up = Vector((0.0, 0.0, 1.0))
    if abs(normal.dot(up)) > 0.98:
        up = Vector((0.0, 1.0, 0.0))
    local_y = (up - normal * up.dot(normal)).normalized()
    local_x = local_y.cross(normal).normalized()
    frame = Matrix((local_x, local_y, normal)).transposed().to_quaternion()
    return Quaternion(normal, math.radians(config.roll_degrees)) @ frame


def _era_world(config: EraBrickConfig, frame: Quaternion,
               local: tuple[float, float, float]) -> Vector:
    return Vector(config.center) + frame @ Vector(local)


def _era_boolean(target: bpy.types.Object, cutter: bpy.types.Object,
                 hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None) -> str:
    if hardflow_boolean_apply is not None:
        solver = hardflow_boolean_apply(target, cutter)
        backend = "Hardflow 1.23.0"
    else:
        modifier = target.modifiers.new(f"{ERA_BRICK_PRESET_ID}_ExactSeam", "BOOLEAN")
        modifier.operation = "DIFFERENCE"
        modifier.solver = "EXACT"
        modifier.object = cutter
        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action="DESELECT")
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        with bpy.context.temp_override(
            object=target,
            active_object=target,
            selected_objects=[target],
            selected_editable_objects=[target],
        ):
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        bm = bmesh.new()
        bm.from_mesh(target.data)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1.0e-5)
        bmesh.ops.dissolve_degenerate(bm, dist=1.0e-5, edges=list(bm.edges))
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(target.data)
        bm.free()
        target.data.update()
        solver = "EXACT"
        backend = "Blender built-in"
    bpy.data.objects.remove(cutter, do_unlink=True)
    return f"{backend}/{solver}"


def create_era_brick_preset(
    *,
    prefix: str,
    config: EraBrickConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build one contacted, cross-seamed ERA cassette with retained fasteners.

    Hardflow callbacks are authoring-only. Passing no callbacks deterministically
    replays the exact Boolean cuts and edge treatment with Blender operations.
    The backing plate starts at the declared support plane and remains flush to
    it; the face seams are shallow enough that the shell stays one solid part.
    """
    if not prefix:
        raise ValueError("ERA brick prefix must not be empty")
    frame = _era_frame(config)
    rotation = frame.to_euler()
    width, height, depth = config.size
    backing_z = config.backing_thickness
    shell_top_z = backing_z + depth
    backing = rmk.make_box(
        f"{prefix}_BackingPlate",
        (width + 2.0 * config.backing_overhang,
         height + 2.0 * config.backing_overhang,
         config.backing_thickness),
        _era_world(config, frame, (0.0, 0.0, backing_z * 0.5)),
        hardware_material,
        bevel=min(config.corner_bevel * 0.5, config.backing_thickness * 0.20),
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    shell = rmk.make_box(
        f"{prefix}_CassetteShell",
        config.size,
        _era_world(config, frame, (0.0, 0.0, backing_z + depth * 0.5)),
        armor_material,
        bevel=config.corner_bevel,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created = [backing, shell]
    shell["rcl_era_boolean_backend"] = "Blender built-in/EXACT"

    seam_depth = config.seam_depth
    seam_z = shell_top_z - seam_depth * 0.5 + 1.0e-5
    seam_dimensions = (
        (width - config.edge_margin * 2.0, config.seam_width, seam_depth + 2.0e-5),
        (config.seam_width, height - config.edge_margin * 2.0, seam_depth + 2.0e-5),
    )
    boolean_backends = []
    for index, dimensions in enumerate(seam_dimensions, 1):
        cutter = bpy.data.meshes.new(f"{prefix}_FaceSeamCutter_{index}_Mesh")
        vertices = [(-0.5, -0.5, -0.5), (0.5, -0.5, -0.5),
                    (0.5, 0.5, -0.5), (-0.5, 0.5, -0.5),
                    (-0.5, -0.5, 0.5), (0.5, -0.5, 0.5),
                    (0.5, 0.5, 0.5), (-0.5, 0.5, 0.5)]
        faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
                 (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        cutter.from_pydata(vertices, [], faces)
        cutter.update()
        cutter_obj = bpy.data.objects.new(f"{prefix}_FaceSeamCutter_{index}", cutter)
        bpy.context.collection.objects.link(cutter_obj)
        cutter_obj.location = _era_world(config, frame, (0.0, 0.0, seam_z))
        cutter_obj.rotation_euler = rotation
        cutter_obj.dimensions = dimensions
        bpy.context.view_layer.objects.active = cutter_obj
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        boolean_backends.append(_era_boolean(shell, cutter_obj, hardflow_boolean_apply))
    shell["rcl_era_boolean_backend"] = "; ".join(boolean_backends)
    if hardflow_bevel is not None:
        hardflow_bevel(shell)

    # Side keepers are recessed 2 mm into the shell and then stand 7 mm proud;
    # their contact is deliberate and the corner bolts secure the same edge.
    keeper_width = 0.010
    keeper_height = height * 0.72
    for sign, side in ((-1.0, "L"), (1.0, "R")):
        keeper = rmk.make_box(
            f"{prefix}_SideKeeper_{side}",
            (keeper_width, keeper_height, 0.010),
            _era_world(config, frame, (sign * (width * 0.46), 0.0, shell_top_z + 0.003)),
            hardware_material,
            bevel=min(0.002, keeper_width * 0.20),
            rotation=rotation,
            bevel_segments=1,
            smooth=False,
        )
        created.append(keeper)

    bolt_x = width * 0.37
    bolt_y = height * 0.34
    for x_sign, side in ((-1.0, "L"), (1.0, "R")):
        for y_sign, end in ((-1.0, "R"), (1.0, "F")):
            bolt = rmk.make_cylinder(
                f"{prefix}_RetainerBolt_{side}_{end}",
                radius=config.fastener_radius,
                depth=0.010,
                location=_era_world(config, frame,
                                    (x_sign * bolt_x, y_sign * bolt_y, shell_top_z + 0.004)),
                material=hardware_material,
                rotation=rotation,
                vertices=8,
                bevel=0.001,
                bevel_segments=1,
            )
            created.append(bolt)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = ERA_BRICK_PRESET_ID
        obj["rcl_era_cassette_role"] = "modular_explosive_reactive_armor"
    return created


GEOMETRY_PRESETS = {
    SERVICE_HATCH_PRESET_ID: create_service_hatch_preset,
    ERA_BRICK_PRESET_ID: create_era_brick_preset,
}
