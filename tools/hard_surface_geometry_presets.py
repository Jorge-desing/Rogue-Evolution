"""Reusable geometry-only hard-surface presets for Rogue Command assets.

Material Maker/PBR presets remain separate. These recipes create Blender mesh
geometry and use built-in Boolean/bevel primitives so headless builds do not
depend on Hardflow being installed.
"""

from __future__ import annotations

import math
from dataclasses import asdict, dataclass
from typing import Callable, Mapping

try:
    import bpy
    import bmesh
    from mathutils import Matrix, Quaternion, Vector
    from mathutils.bvhtree import BVHTree
    import RogueModelKit as rmk
except ImportError:
    bpy = None
    bmesh = None
    Matrix = None
    Quaternion = None
    Vector = None
    BVHTree = None
    rmk = None


SERVICE_HATCH_PRESET_ID = "service_hatch_recessed_v1"
ERA_BRICK_PRESET_ID = "era_brick_cassette_v1"
COAXIAL_SERVICE_CONNECTOR_PRESET_ID = "coaxial_service_connector_recessed_v1"

# Contemporary military hard-surface presets (Estilo militar actual)
APS_HARDKILL_RADAR_PRESET_ID = "aps_hardkill_radar_v1"
FUEL_AUX_JERRYCAN_RACK_PRESET_ID = "fuel_aux_jerrycan_rack_v1"
SLAT_ARMOR_SCREEN_PRESET_ID = "slat_armor_screen_v1"
ORDNANCE_MINE_DISPENSER_PRESET_ID = "ordnance_mine_dispenser_rack_v1"
THERMAL_ARMORED_RADIATOR_PRESET_ID = "thermal_armored_radiator_pack_v1"
AUTOCANNON_REMOTE_SPONSON_PRESET_ID = "autocannon_remote_sponson_pod_v1"

# Futuristic military hard-surface presets (Estilo militar futurista)
APS_PHASE_SHIELD_PRESET_ID = "aps_phase_shield_emitter_v1"
PLASMA_CONTAINMENT_CELL_PRESET_ID = "plasma_containment_cell_v1"
STEALTH_HEX_REACTIVE_TILE_PRESET_ID = "stealth_hex_reactive_tile_v1"
PLASMA_TORPEDO_MAGAZINE_PRESET_ID = "plasma_torpedo_magazine_rack_v1"
TACHYONIC_RADIATOR_ARRAY_PRESET_ID = "tachyonic_radiator_array_v1"
NANITE_REPAIR_DISPENSER_PRESET_ID = "nanite_repair_dispenser_pod_v1"

# Contemporary military hard-surface presets v2 (Estilo militar actual v2)
APS_HARDKILL_RADAR_V2_PRESET_ID = "aps_hardkill_radar_v2"
FUEL_AUX_JERRYCAN_RACK_V2_PRESET_ID = "fuel_aux_jerrycan_rack_v2"
SLAT_ARMOR_SCREEN_V2_PRESET_ID = "slat_armor_screen_v2"
ORDNANCE_MINE_DISPENSER_V2_PRESET_ID = "ordnance_mine_dispenser_rack_v2"
THERMAL_ARMORED_RADIATOR_V2_PRESET_ID = "thermal_armored_radiator_pack_v2"
AUTOCANNON_REMOTE_SPONSON_V2_PRESET_ID = "autocannon_remote_sponson_pod_v2"

# Futuristic military hard-surface presets v2 (Estilo militar futurista v2)
APS_PHASE_SHIELD_V2_PRESET_ID = "aps_phase_shield_emitter_v2"
PLASMA_CONTAINMENT_CELL_V2_PRESET_ID = "plasma_containment_cell_v2"
STEALTH_HEX_REACTIVE_TILE_V2_PRESET_ID = "stealth_hex_reactive_tile_v2"
PLASMA_TORPEDO_MAGAZINE_V2_PRESET_ID = "plasma_torpedo_magazine_rack_v2"
TACHYONIC_RADIATOR_ARRAY_V2_PRESET_ID = "tachyonic_radiator_array_v2"
NANITE_REPAIR_DISPENSER_V2_PRESET_ID = "nanite_repair_dispenser_pod_v2"

# Functional canonical Level 2 presets (Componentes funcionales de nivel 2)
EXCAVATION_DRILL_CARBIDE_BORER_PRESET_ID = "excavation_drill_carbide_borer_v1"
DOCKING_CLAMP_HYDRAULIC_ARM_PRESET_ID = "docking_clamp_hydraulic_arm_v1"
APU_TURBOGENERATOR_SPONSON_PRESET_ID = "apu_turbogenerator_sponson_v1"
SMOKE_GRENADE_SALVO_DISCHARGER_PRESET_ID = "smoke_grenade_salvo_discharger_v1"

PLASMA_EXCAVATION_TORCH_PRESET_ID = "plasma_excavation_torch_v1"
MAGNETIC_TRACTOR_DOCKING_CLAMP_PRESET_ID = "magnetic_tractor_docking_clamp_v1"
GRAVITON_RECOIL_ANCHOR_PRESET_ID = "graviton_recoil_anchor_v1"
OPTRONIC_RECON_SENSOR_MAST_PRESET_ID = "optronic_recon_sensor_mast_v1"


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
        if math.hypot(*self.normal) <= 1.0e-8:
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


def _orientation_frame(normal_vec: tuple[float, float, float], roll_degrees: float = 0.0) -> Quaternion:
    normal = Vector(normal_vec).normalized()
    up = Vector((0.0, 0.0, 1.0))
    if abs(normal.dot(up)) > 0.98:
        up = Vector((0.0, 1.0, 0.0))
    local_y = (up - normal * up.dot(normal)).normalized()
    local_x = local_y.cross(normal).normalized()
    frame = Matrix((local_x, local_y, normal)).transposed().to_quaternion()
    return Quaternion(normal, math.radians(roll_degrees)) @ frame


def _transform_world(center: tuple[float, float, float], frame: Quaternion, local: tuple[float, float, float]) -> Vector:
    return Vector(center) + frame @ Vector(local)


def _apply_hardflow_or_builtin_boolean(
    target: bpy.types.Object,
    cutter: bpy.types.Object,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    modifier_name: str = "RCL_BooleanCut",
) -> str:
    """Apply boolean difference via Hardflow callback if provided, else built-in EXACT."""
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    for obj in (target, cutter):
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        obj.select_set(False)

    if hardflow_boolean_apply is not None:
        solver = hardflow_boolean_apply(target, cutter)
        backend = "Hardflow 1.23.0"
    else:
        modifier = target.modifiers.new(modifier_name, "BOOLEAN")
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
    if cutter.name in bpy.data.objects:
        bpy.data.objects.remove(cutter, do_unlink=True)
    return f"{backend}/{solver}"


def _apply_hardflow_or_builtin_bevel(
    target: bpy.types.Object,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
    *,
    bevel_width: float = 0.003,
    segments: int = 1,
    angle: float = math.radians(42.0),
    modifier_name: str = "RCL_HardflowBevel",
) -> None:
    """Apply edge treatment via Hardflow callback if provided, else built-in ANGLE bevel."""
    if hardflow_bevel is not None:
        hardflow_bevel(target)
    else:
        mod = target.modifiers.new(modifier_name, "BEVEL")
        mod.width = bevel_width
        mod.segments = segments
        mod.limit_method = "ANGLE"
        mod.angle_limit = angle
        mod.harden_normals = True
        mod.use_clamp_overlap = True
        bpy.ops.object.select_all(action="DESELECT")
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        with bpy.context.temp_override(
            object=target,
            active_object=target,
            selected_objects=[target],
            selected_editable_objects=[target],
        ):
            bpy.ops.object.modifier_apply(modifier=mod.name)


# -------------------------------------------------------------------------
# Preset: Recessed coaxial service connector (Futuristic Military Sponson)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class CoaxialServiceConnectorConfig:
    """Dimensions for a retained service port on a flat panel.

    ``center`` is the support-panel center. The panel's thickness axis follows
    ``normal``; ``up`` establishes the in-plane vertical axis. This keeps the
    connector oriented and fitted to its panel instead of using vehicle-space
    coordinates.
    """

    center: tuple[float, float, float]
    normal: tuple[float, float, float] = (0.0, 1.0, 0.0)
    up: tuple[float, float, float] = (0.0, 0.0, 1.0)
    panel_size: tuple[float, float] = (0.46, 0.26)
    panel_thickness: float = 0.08
    seat_radius: float = 0.052
    seat_depth: float = 0.032
    flange_major_radius: float = 0.061
    flange_minor_radius: float = 0.007
    hub_radius: float = 0.028
    hub_depth: float = 0.022
    coupling_radius: float = 0.011
    coupling_depth: float = 0.016
    fastener_offset: float = 0.082
    fastener_radius: float = 0.009
    fastener_depth: float = 0.010
    fastener_surface_offset: float = 0.005

    def __post_init__(self) -> None:
        numeric = (
            *self.center, *self.normal, *self.up, *self.panel_size,
            self.panel_thickness, self.seat_radius, self.seat_depth,
            self.flange_major_radius, self.flange_minor_radius,
            self.hub_radius, self.hub_depth, self.coupling_radius,
            self.coupling_depth, self.fastener_offset, self.fastener_radius,
            self.fastener_depth, self.fastener_surface_offset,
        )
        if not all(math.isfinite(float(value)) for value in numeric):
            raise ValueError("coaxial service connector dimensions must be finite")
        if min(self.panel_size) <= 0.0 or self.panel_thickness <= 0.0:
            raise ValueError("support panel dimensions must be positive")
        if min(self.seat_radius, self.seat_depth, self.flange_major_radius,
               self.flange_minor_radius, self.hub_radius, self.hub_depth,
               self.coupling_radius, self.coupling_depth,
               self.fastener_offset, self.fastener_radius, self.fastener_depth,
               self.fastener_surface_offset) <= 0.0:
            raise ValueError("connector dimensions must be positive")
        if abs(self.fastener_surface_offset - self.fastener_depth * 0.5) > 0.001:
            raise ValueError("panel anchor bolt heads must seat within 1 mm of the panel face")
        if self.seat_depth + 0.018 > self.panel_thickness:
            raise ValueError("the connector seat must leave at least 18 mm of panel backing")
        if self.seat_radius >= self.flange_major_radius - self.flange_minor_radius:
            raise ValueError("the flange must overlap the counterbore shoulder")
        if self.hub_radius >= self.seat_radius or self.coupling_radius >= self.hub_radius:
            raise ValueError("the hub and coupling must step inward from the seat")
        if self.seat_radius <= 0.006:
            raise ValueError("the counterbore must leave positive radius for its recessed insulator")
        if self.seat_depth * 0.52 < self.hub_depth * 0.5 + 0.002:
            raise ValueError("the connector hub must remain at least 2 mm above the counterbore floor")
        flange_outer_radius = self.flange_major_radius + self.flange_minor_radius
        if self.fastener_offset - self.fastener_radius <= flange_outer_radius + 0.003:
            raise ValueError("panel anchor bolts must keep at least 3 mm clearance from the flange")
        maximum_x = max(self.flange_major_radius + self.flange_minor_radius,
                        self.fastener_offset + self.fastener_radius)
        maximum_y = max(self.flange_major_radius + self.flange_minor_radius,
                        self.fastener_offset + self.fastener_radius)
        if maximum_x >= self.panel_size[0] * 0.5 - 0.01 or maximum_y >= self.panel_size[1] * 0.5 - 0.01:
            raise ValueError("the connector and retainers need at least 10 mm panel-edge clearance")
        normal_length = math.sqrt(sum(value * value for value in self.normal))
        up_length = math.sqrt(sum(value * value for value in self.up))
        if normal_length < 1.0e-6 or up_length < 1.0e-6:
            raise ValueError("connector normal and up vectors must be nonzero")
        dot = sum(normal * up for normal, up in zip(self.normal, self.up))
        if abs(dot / (normal_length * up_length)) > 0.98:
            raise ValueError("connector up vector must not be parallel to its normal")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def _service_connector_frame(config: CoaxialServiceConnectorConfig) -> tuple[Quaternion, Vector, Vector, Vector]:
    normal = Vector(config.normal).normalized()
    up_hint = Vector(config.up).normalized()
    right = up_hint.cross(normal).normalized()
    up = normal.cross(right).normalized()
    rotation = Matrix((right, up, normal)).transposed().to_quaternion()
    return rotation, right, up, normal


def _validate_service_connector_panel(
    panel: bpy.types.Object,
    config: CoaxialServiceConnectorConfig,
    rotation: Quaternion,
) -> None:
    if panel is None or panel.type != "MESH":
        raise ValueError("coaxial service connector requires a mesh support panel")
    center = Vector(config.center)
    inverse = rotation.inverted()
    local = [inverse @ ((panel.matrix_world @ Vector(corner)) - center) for corner in panel.bound_box]
    extents = tuple(max(point[index] for point in local) - min(point[index] for point in local)
                    for index in range(3))
    expected = (config.panel_size[0], config.panel_size[1], config.panel_thickness)
    local_center = tuple(
        (max(point[index] for point in local) + min(point[index] for point in local)) * 0.5
        for index in range(3)
    )
    if any(abs(actual - wanted) > 0.003 for actual, wanted in zip(extents, expected)):
        raise ValueError(
            "support panel dimensions do not match the connector's measured local frame: "
            f"actual={tuple(round(value, 5) for value in extents)}, expected={expected}"
        )
    if any(abs(value) > 0.003 for value in local_center):
        raise ValueError(
            "support panel center does not align with the connector seat: "
            f"offset={tuple(round(value, 5) for value in local_center)}"
        )


def create_coaxial_service_connector_preset(
    *,
    prefix: str,
    config: CoaxialServiceConnectorConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    backing_panel: bpy.types.Object | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Cut and furnish one service port, replayable with Blender built-ins.

    The boolean seat is applied to ``backing_panel``. If no panel is supplied,
    a measured support plate is created for preset previews and isolated tests.
    Four retainer bolts secure the flange; the hub, insulator and coupling pin
    are concentric and remain inside the counterbore footprint.
    """
    if not prefix:
        raise ValueError("coaxial service connector prefix must not be empty")
    rotation, right, up, normal = _service_connector_frame(config)
    center = Vector(config.center)
    created: list[bpy.types.Object] = []
    panel = backing_panel
    if panel is None:
        panel = rmk.make_box(
            f"{prefix}_SupportPanel",
            (config.panel_size[0], config.panel_size[1], config.panel_thickness),
            center,
            armor_material,
            bevel=min(0.010, config.panel_thickness * 0.18),
            rotation=rotation.to_euler(),
            bevel_segments=2,
        )
        created.append(panel)
    _validate_service_connector_panel(panel, config, rotation)

    front = center + normal * (config.panel_thickness * 0.5)
    boolean_overshoot = 0.002
    cutter = rmk.make_cylinder(
        f"{prefix}_CounterboreCutter",
        config.seat_radius,
        config.seat_depth + boolean_overshoot,
        front + normal * ((boolean_overshoot - config.seat_depth) * 0.5),
        None,
        rotation=rotation.to_euler(),
        vertices=32,
        bevel=0.001,
        bevel_segments=1,
    )
    backend = _apply_hardflow_or_builtin_boolean(
        panel,
        cutter,
        hardflow_boolean_apply,
        modifier_name=f"{COAXIAL_SERVICE_CONNECTOR_PRESET_ID}_Counterbore",
    )
    panel["rcl_connector_boolean_backend"] = backend

    def face_point(x: float, y: float, normal_offset: float = 0.0) -> Vector:
        return front + right * x + up * y + normal * normal_offset

    recess_floor = front - normal * (config.seat_depth - 0.006)
    dark_cup = rmk.make_cylinder(
        f"{prefix}_RecessedInsulator",
        config.seat_radius - 0.005,
        0.006,
        recess_floor,
        hardware_material,
        rotation=rotation.to_euler(),
        vertices=32,
        bevel=0.001,
        bevel_segments=1,
    )
    hub = rmk.make_cylinder(
        f"{prefix}_ConnectorHub",
        config.hub_radius,
        config.hub_depth,
        front - normal * (config.seat_depth * 0.48),
        accent_material or hardware_material,
        rotation=rotation.to_euler(),
        vertices=24,
        bevel=0.002,
        bevel_segments=1,
    )
    hub_outer_face = -config.seat_depth * 0.48 + config.hub_depth * 0.5
    coupling_center_offset = hub_outer_face + config.coupling_depth * 0.5 - 0.002
    coupling = rmk.make_cylinder(
        f"{prefix}_CouplingPin",
        config.coupling_radius,
        config.coupling_depth,
        front + normal * coupling_center_offset,
        hardware_material,
        rotation=rotation.to_euler(),
        vertices=12,
        bevel=0.001,
        bevel_segments=1,
    )
    flange = rmk.make_torus(
        f"{prefix}_RetainerFlange",
        front + normal * 0.005,
        config.flange_major_radius,
        config.flange_minor_radius,
        armor_material or hardware_material,
        rotation=rotation.to_euler(),
        major_segments=32,
        minor_segments=8,
    )
    inner_rim = rmk.make_torus(
        f"{prefix}_InsulatorRim",
        front - normal * (config.seat_depth * 0.30),
        config.hub_radius + 0.002,
        0.003,
        accent_material or hardware_material,
        rotation=rotation.to_euler(),
        major_segments=24,
        minor_segments=6,
    )
    created.extend((dark_cup, hub, coupling, flange, inner_rim))
    for obj, component_role in (
        (dark_cup, "recessed_insulator"),
        (hub, "connector_hub"),
        (coupling, "coupling_pin"),
        (flange, "retainer_flange"),
        (inner_rim, "insulator_rim"),
    ):
        obj["rcl_service_connector_component"] = component_role

    bolt_positions = (
        ("L", -config.fastener_offset, 0.0),
        ("R", config.fastener_offset, 0.0),
        ("D", 0.0, -config.fastener_offset),
        ("U", 0.0, config.fastener_offset),
    )
    for label, x_offset, up_offset in bolt_positions:
        bolt = rmk.make_cylinder(
            f"{prefix}_PanelAnchorBolt_{label}",
            config.fastener_radius,
            config.fastener_depth,
            face_point(x_offset, up_offset, config.fastener_surface_offset),
            hardware_material,
            rotation=rotation.to_euler(),
            vertices=6,
            bevel=0.001,
            bevel_segments=1,
        )
        bolt["rcl_service_connector_component"] = "panel_anchor_bolt"
        created.append(bolt)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = COAXIAL_SERVICE_CONNECTOR_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_serviceable_hardware"] = "coaxial_power_cell_service_port"
    panel["rcl_hard_surface_geometry_preset"] = COAXIAL_SERVICE_CONNECTOR_PRESET_ID
    panel["rcl_service_connector_component"] = "support_panel"
    if hardflow_bevel is not None:
        for obj in (flange, hub, coupling):
            hardflow_bevel(obj)
    return created


# -------------------------------------------------------------------------
# Preset 1: AESA Hard-Kill Radar & Countermeasure Pod (Contemporary Military APS)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class ApsHardkillRadarConfig:
    """Dimensions and placement for a contemporary hard-kill AESA radar/launcher pod.

    Mounts at Socket_Hardpoint_APS (forward or turret roof). Local +Z is outward
    mount normal, local +Y is forward search/aim axis, local +X is lateral axis.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 0.0, 1.0)
    base_plate_size: tuple[float, float, float] = (0.28, 0.24, 0.015)
    housing_size: tuple[float, float, float] = (0.22, 0.18, 0.16)
    gimbal_height: float = 0.075
    tilt_degrees: float = 18.0
    mortar_caliber: float = 0.076
    mortar_length: float = 0.22
    mortar_elevation_deg: float = 35.0
    mortar_azimuth_spread_deg: float = 16.0
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.base_plate_size, *self.housing_size,
                  self.gimbal_height, self.tilt_degrees, self.mortar_caliber, self.mortar_length,
                  self.mortar_elevation_deg, self.mortar_azimuth_spread_deg, self.fastener_radius,
                  self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("ApsHardkillRadarConfig dimensions and placement must be finite")
        if min(*self.base_plate_size, *self.housing_size, self.gimbal_height, self.mortar_caliber,
               self.mortar_length, self.fastener_radius) <= 0.0:
            raise ValueError("ApsHardkillRadarConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("ApsHardkillRadarConfig normal must not be zero")
        if self.mortar_caliber >= min(self.housing_size[:2]):
            raise ValueError("Mortar caliber exceeds housing bounds")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_aps_hardkill_radar_preset(
    *,
    prefix: str,
    config: ApsHardkillRadarConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a contemporary AESA radar pod with twin mortar countermeasure tubes."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    # 1. Base Mounting Plate (seats flush to hull mount plane at local Z=0)
    base_thick = config.base_plate_size[2]
    base_plate = rmk.make_box(
        f"{prefix}_MountBasePlate",
        config.base_plate_size,
        _transform_world(config.center, frame, (0.0, 0.0, base_thick * 0.5)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(base_plate)

    # 4 Corner Hex Fasteners
    fastener_x = config.base_plate_size[0] * 0.40
    fastener_y = config.base_plate_size[1] * 0.40
    fastener_z = base_thick + 0.004
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            tag = ("L" if sx < 0 else "R") + ("R" if sy < 0 else "F")
            bolt = rmk.make_cylinder(
                f"{prefix}_BaseFastener_{tag}",
                radius=config.fastener_radius,
                depth=0.008,
                location=_transform_world(config.center, frame, (sx * fastener_x, sy * fastener_y, fastener_z)),
                material=hardware_material,
                rotation=rotation,
                vertices=8,
                bevel=0.001,
            )
            created.append(bolt)

    # Chassis Electrical Grounding Stud with Hex Nut
    ground_lug = rmk.make_cylinder(
        f"{prefix}_GroundingLug",
        radius=0.006,
        depth=0.012,
        location=_transform_world(config.center, frame, (-fastener_x * 0.75, fastener_y * 0.78, base_thick + 0.006)),
        material=hardware_material,
        rotation=rotation,
        vertices=6,
        bevel=0.001,
    )
    created.append(ground_lug)

    # 2. Stepped Azimuth Turntable Ring & Gimbal Yoke Pedestal
    turntable_radius = min(config.base_plate_size[0], config.base_plate_size[1]) * 0.38
    turntable = rmk.make_cylinder(
        f"{prefix}_AzimuthTurntable",
        radius=turntable_radius,
        depth=0.008,
        location=_transform_world(config.center, frame, (0.0, 0.0, base_thick + 0.004)),
        material=hardware_material,
        rotation=rotation,
        vertices=16,
        bevel=0.001,
    )
    created.append(turntable)

    gimbal_z = base_thick + 0.008 + config.gimbal_height * 0.5
    gimbal = rmk.make_box(
        f"{prefix}_GimbalYoke",
        (0.16, 0.14, config.gimbal_height),
        _transform_world(config.center, frame, (0.0, 0.0, gimbal_z)),
        hardware_material,
        bevel=0.003,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(gimbal)

    # 3. Radar Housing & Antenna Face
    housing_z = base_thick + 0.008 + config.gimbal_height + config.housing_size[2] * 0.5
    tilt_quat = Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(-config.tilt_degrees))
    combined_frame = frame @ tilt_quat
    combined_rot = combined_frame.to_euler()

    housing_center = _transform_world(config.center, frame, (0.0, 0.015, housing_z))
    housing = rmk.make_box(
        f"{prefix}_RadarHousing",
        config.housing_size,
        housing_center,
        armor_material,
        bevel=0.008,
        rotation=combined_rot,
        bevel_segments=1,
        smooth=False,
    )
    created.append(housing)

    # AESA Face Antenna Plate (recessed on +Y face of housing)
    face_w = config.housing_size[0] * 0.82
    face_h = config.housing_size[2] * 0.72
    face_pos_local = (0.0, config.housing_size[1] * 0.5 - 0.003, 0.0)
    face_pos_world = Vector(housing_center) + combined_frame @ Vector(face_pos_local)
    antenna = rmk.make_box(
        f"{prefix}_AesaAntennaPlate",
        (face_w, 0.010, face_h),
        face_pos_world,
        accent_material or hardware_material,
        bevel=0.002,
        rotation=combined_rot,
        bevel_segments=1,
        smooth=False,
    )
    created.append(antenna)

    # Face plate recess cut into the housing
    recess_cutter = rmk.make_box(
        f"{prefix}_FaceRecessCutter",
        (face_w + 0.004, 0.014, face_h + 0.004),
        face_pos_world,
        None,
        rotation=combined_rot,
    )
    _apply_hardflow_or_builtin_boolean(housing, recess_cutter, hardflow_boolean_apply)

    # AESA Antenna Micro-screws & Quadrant Array Dividers
    for scr_idx, (sx, sz) in enumerate(((-0.42, -0.42), (0.42, -0.42), (-0.42, 0.42), (0.42, 0.42)), 1):
        screw_pos = Vector(face_pos_world) + combined_frame @ Vector((sx * face_w, 0.006, sz * face_h))
        screw = rmk.make_cylinder(
            f"{prefix}_AntennaScrew_{scr_idx}",
            radius=0.003,
            depth=0.004,
            location=screw_pos,
            material=hardware_material,
            rotation=(combined_frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=6,
        )
        created.append(screw)

    divider_h = rmk.make_box(
        f"{prefix}_AntennaDivider_H",
        (face_w * 0.94, 0.003, 0.003),
        Vector(face_pos_world) + combined_frame @ Vector((0.0, 0.005, 0.0)),
        hardware_material,
        bevel=0.0005,
        rotation=combined_rot,
    )
    divider_v = rmk.make_box(
        f"{prefix}_AntennaDivider_V",
        (0.003, 0.003, face_h * 0.94),
        Vector(face_pos_world) + combined_frame @ Vector((0.0, 0.005, 0.0)),
        hardware_material,
        bevel=0.0005,
        rotation=combined_rot,
    )
    created.extend((divider_h, divider_v))

    # Rear Electronics Heat-Sink Dissipation Plate
    rear_pos = Vector(housing_center) + combined_frame @ Vector((0.0, -config.housing_size[1] * 0.5 - 0.004, 0.0))
    heatsink = rmk.make_box(
        f"{prefix}_HeatsinkPlate",
        (config.housing_size[0] * 0.65, 0.008, config.housing_size[2] * 0.60),
        rear_pos,
        hardware_material,
        bevel=0.001,
        rotation=combined_rot,
    )
    created.append(heatsink)

    # 4. Twin Mortar Countermeasure Tubes & Elevation Mechanism
    tube_rad = config.mortar_caliber * 0.5
    tube_depth = config.mortar_length
    tube_offset_x = config.housing_size[0] * 0.5 + tube_rad + 0.015
    for sign, side in ((-1.0, "L"), (1.0, "R")):
        trunnion_pos = Vector(housing_center) + combined_frame @ Vector((sign * (config.housing_size[0] * 0.5 + 0.008), 0.0, 0.0))
        trunnion = rmk.make_cylinder(
            f"{prefix}_Trunnion_{side}",
            radius=0.018,
            depth=0.024,
            location=trunnion_pos,
            material=hardware_material,
            rotation=combined_rot,
            vertices=12,
            bevel=0.001,
        )
        lock_nut = rmk.make_cylinder(
            f"{prefix}_TrunnionLockNut_{side}",
            radius=0.012,
            depth=0.008,
            location=Vector(trunnion_pos) + combined_frame @ Vector((sign * 0.014, 0.0, 0.0)),
            material=hardware_material,
            rotation=(combined_frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=6,
            bevel=0.001,
        )
        created.extend((trunnion, lock_nut))

        # Elevation Sector Clamp Bracket
        bracket_pos = Vector(housing_center) + combined_frame @ Vector((sign * (config.housing_size[0] * 0.5 + 0.004), 0.0, -0.035))
        bracket = rmk.make_box(
            f"{prefix}_ElevationBracket_{side}",
            (0.014, 0.045, 0.040),
            bracket_pos,
            hardware_material,
            bevel=0.001,
            rotation=combined_rot,
        )
        created.append(bracket)

        spread_sign = sign * config.mortar_azimuth_spread_deg
        tube_quat = combined_frame @ Quaternion(Vector((0.0, 0.0, 1.0)), math.radians(spread_sign)) @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(config.mortar_elevation_deg))
        tube_rot = tube_quat.to_euler()

        tube_center = Vector(housing_center) + combined_frame @ Vector((sign * tube_offset_x, 0.04, 0.02))
        tube = rmk.make_cylinder(
            f"{prefix}_CountermeasureTube_{side}",
            radius=tube_rad,
            depth=tube_depth,
            location=tube_center,
            material=hardware_material,
            rotation=tube_rot,
            vertices=14,
            bevel=0.002,
        )
        created.append(tube)

        bore_depth = 0.070
        muzzle_offset = tube_quat @ Vector((0.0, 0.0, tube_depth * 0.5 - bore_depth * 0.5 + 0.001))
        bore_cutter = rmk.make_cylinder(
            f"{prefix}_BoreCutter_{side}",
            radius=tube_rad * 0.78,
            depth=bore_depth,
            location=Vector(tube_center) + muzzle_offset,
            material=None,
            rotation=tube_rot,
            vertices=12,
        )
        _apply_hardflow_or_builtin_boolean(tube, bore_cutter, hardflow_boolean_apply)

        # Flared Muzzle Blast Ring
        muzzle_ring_pos = Vector(tube_center) + tube_quat @ Vector((0.0, 0.0, tube_depth * 0.5 - 0.006))
        muzzle_ring = rmk.make_cylinder(
            f"{prefix}_MuzzleBlastRing_{side}",
            radius=tube_rad * 1.15,
            depth=0.012,
            location=muzzle_ring_pos,
            material=hardware_material,
            rotation=tube_rot,
            vertices=14,
            bevel=0.0015,
        )
        created.append(muzzle_ring)

        # Breech Electrical Ignition Connector Plug
        breech_pos = Vector(tube_center) - tube_quat @ Vector((0.0, 0.0, tube_depth * 0.5 + 0.006))
        igniter = rmk.make_cylinder(
            f"{prefix}_BreechIgniter_{side}",
            radius=0.008,
            depth=0.014,
            location=breech_pos,
            material=hardware_material,
            rotation=tube_rot,
            vertices=8,
            bevel=0.001,
        )
        created.append(igniter)

        collar = rmk.make_box(
            f"{prefix}_TubeClamp_{side}",
            (0.012, tube_rad * 2.4, 0.024),
            Vector(tube_center) - tube_quat @ Vector((0.0, 0.0, 0.03)),
            hardware_material,
            bevel=0.001,
            rotation=tube_rot,
        )
        created.append(collar)

    # 5. Rear Cable/Harness Gland with Bulkhead Lock Collar
    gland_pos = Vector(housing_center) + combined_frame @ Vector((0.0, -config.housing_size[1] * 0.5 - 0.014, -0.02))
    gland = rmk.make_cylinder(
        f"{prefix}_HarnessGland",
        radius=0.015,
        depth=0.028,
        location=gland_pos,
        material=hardware_material,
        rotation=combined_rot,
        vertices=10,
        bevel=0.001,
    )
    gland_nut = rmk.make_cylinder(
        f"{prefix}_GlandLockNut",
        radius=0.020,
        depth=0.008,
        location=Vector(gland_pos) + combined_frame @ Vector((0.0, 0.010, 0.0)),
        material=hardware_material,
        rotation=(combined_frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=6,
        bevel=0.001,
    )
    created.extend((gland, gland_nut))

    _apply_hardflow_or_builtin_bevel(housing, hardflow_bevel, bevel_width=0.004)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = APS_HARDKILL_RADAR_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "APS"
    return created


# -------------------------------------------------------------------------
# Preset 2: Tactical Stamped Jerrycan Skid Basket (Contemporary Military Fuel)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class FuelAuxJerrycanRackConfig:
    """Dimensions and placement for a contemporary tactical jerrycan rack carrier.

    Mounts at Socket_Hardpoint_FuelDrums (rear hull). Local +Z is vertical,
    local +Y is outward/rearward from the hull, local +X is lateral.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    basket_size: tuple[float, float, float] = (0.58, 0.32, 0.38)
    can_dimensions: tuple[float, float, float] = (0.22, 0.14, 0.34)
    tube_radius: float = 0.009
    stiffener_groove_depth: float = 0.003
    stiffener_groove_width: float = 0.008
    strap_width: float = 0.024
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.basket_size, *self.can_dimensions,
                  self.tube_radius, self.stiffener_groove_depth, self.stiffener_groove_width,
                  self.strap_width, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("FuelAuxJerrycanRackConfig dimensions and placement must be finite")
        if min(*self.basket_size, *self.can_dimensions, self.tube_radius,
               self.stiffener_groove_depth, self.stiffener_groove_width, self.strap_width) <= 0.0:
            raise ValueError("FuelAuxJerrycanRackConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("FuelAuxJerrycanRackConfig normal must not be zero")
        if self.can_dimensions[0] * 2.0 > self.basket_size[0] + 0.02:
            raise ValueError("Canisters do not fit horizontally in the basket")
        if self.can_dimensions[1] > self.basket_size[1] + 0.02:
            raise ValueError("Canister depth exceeds basket depth")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_fuel_aux_jerrycan_rack_preset(
    *,
    prefix: str,
    config: FuelAuxJerrycanRackConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a contemporary tactical rack carrying dual stamped-steel jerrycans."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    bw, bd, bh = config.basket_size
    # 1. Base Skid Plate & Rear Standoff Brackets
    floor = rmk.make_box(
        f"{prefix}_BasketFloorPlate",
        (bw, bd, 0.012),
        _transform_world(config.center, frame, (0.0, bd * 0.5, 0.006)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(floor)

    # 2 Lower Skid Runners (ground/debris protection under basket)
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        skid = rmk.make_cylinder(
            f"{prefix}_SkidRunner_{side}",
            radius=config.tube_radius * 0.85,
            depth=bd * 0.92,
            location=_transform_world(config.center, frame, (sx * (bw * 0.38), bd * 0.5, -config.tube_radius * 0.85)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
            bevel=0.001,
        )
        created.append(skid)

    # Standoff Brackets with Mounting Clevis Pins
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        tab = rmk.make_box(
            f"{prefix}_MountBracket_{side}",
            (0.06, 0.025, bh * 0.70),
            _transform_world(config.center, frame, (sx * (bw * 0.40), 0.012, bh * 0.38)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        bolt = rmk.make_cylinder(
            f"{prefix}_MountBolt_{side}",
            radius=0.006,
            depth=0.012,
            location=_transform_world(config.center, frame, (sx * (bw * 0.40), 0.026, bh * 0.65)),
            material=hardware_material,
            rotation=rotation,
            vertices=8,
        )
        clevis_pin = rmk.make_cylinder(
            f"{prefix}_ClevisPin_{side}",
            radius=0.005,
            depth=0.038,
            location=_transform_world(config.center, frame, (sx * (bw * 0.40), 0.012, bh * 0.20)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=8,
            bevel=0.001,
        )
        created.extend((tab, bolt, clevis_pin))

    top_rail_z = bh
    top_rail_front = rmk.make_box(
        f"{prefix}_TopRailFront",
        (bw, config.tube_radius * 2.0, config.tube_radius * 2.0),
        _transform_world(config.center, frame, (0.0, bd - config.tube_radius, top_rail_z)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    front_mid_rail = rmk.make_box(
        f"{prefix}_FrontMidRail",
        (bw - config.tube_radius * 2.0, config.tube_radius * 1.5, config.tube_radius * 1.5),
        _transform_world(config.center, frame, (0.0, bd - config.tube_radius, bh * 0.45)),
        hardware_material,
        bevel=0.0015,
        rotation=rotation,
    )
    created.extend((top_rail_front, front_mid_rail))

    for sx in (-1.0, 1.0):
        side_tag = 'L' if sx < 0 else 'R'
        side_rail = rmk.make_box(
            f"{prefix}_TopRailSide_{side_tag}",
            (config.tube_radius * 2.0, bd, config.tube_radius * 2.0),
            _transform_world(config.center, frame, (sx * (bw * 0.5 - config.tube_radius), bd * 0.5, top_rail_z)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        corner_post = rmk.make_box(
            f"{prefix}_CornerPostFront_{side_tag}",
            (config.tube_radius * 2.0, config.tube_radius * 2.0, bh),
            _transform_world(config.center, frame, (sx * (bw * 0.5 - config.tube_radius), bd - config.tube_radius, bh * 0.5)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.extend((side_rail, corner_post))

    # 2. Dual Jerrycans (positioned at X = -0.14 and X = +0.14)
    cw, cd, ch = config.can_dimensions
    can_spacing_x = cw + 0.04
    for index, sx in enumerate((-0.5, 0.5), 1):
        can_center_local = (sx * can_spacing_x, bd * 0.5, 0.012 + ch * 0.5)
        can_center_world = _transform_world(config.center, frame, can_center_local)

        can = rmk.make_box(
            f"{prefix}_JerrycanBody_{index}",
            config.can_dimensions,
            can_center_world,
            armor_material,
            bevel=0.006,
            rotation=rotation,
            bevel_segments=1,
            smooth=False,
        )
        created.append(can)

        # Stamped Central Perimeter Seam Flange (NATO 2-piece welded shell seam)
        seam_flange = rmk.make_box(
            f"{prefix}_PerimeterFlange_{index}",
            (0.003, cd + 0.008, ch + 0.008),
            can_center_world,
            armor_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.append(seam_flange)

        side_sign = -1.0 if sx < 0 else 1.0
        groove_w = config.stiffener_groove_width
        rib_thick = 0.003
        for diag_idx, angle_deg in enumerate((-38.0, 38.0), 1):
            diag_quat = frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(angle_deg))
            rib_pos = Vector(can_center_world) + frame @ Vector((side_sign * (cw * 0.5 + rib_thick * 0.5), 0.0, 0.0))
            rib = rmk.make_box(
                f"{prefix}_StiffenerRib_{index}_{diag_idx}",
                (rib_thick, cd * 0.70, groove_w),
                rib_pos,
                armor_material,
                bevel=0.001,
                rotation=diag_quat.to_euler(),
            )
            created.append(rib)

        # Recessed filler neck socket cut into top face of canister
        neck_local = (sx * can_spacing_x - 0.04, bd * 0.5 + cd * 0.30, 0.012 + ch)
        neck_world = _transform_world(config.center, frame, neck_local)
        neck_socket_cutter = rmk.make_cylinder(
            f"{prefix}_NeckSocketCutter_{index}",
            radius=0.020,
            depth=0.016,
            location=neck_world,
            material=None,
            rotation=rotation,
            vertices=12,
        )
        _apply_hardflow_or_builtin_boolean(can, neck_socket_cutter, hardflow_boolean_apply)

        # 3-Bar Carry Handle Assembly (center carry bar + 2 buddy-carry bars)
        handle_z = 0.012 + ch + 0.014
        for bar_idx, y_off in enumerate((-0.03, 0.0, 0.03), 1):
            handle_bar = rmk.make_box(
                f"{prefix}_HandleBar_{index}_{bar_idx}",
                (cw * 0.65, 0.008, 0.010),
                _transform_world(config.center, frame, (sx * can_spacing_x, bd * 0.5 + y_off, handle_z)),
                hardware_material,
                bevel=0.001,
                rotation=rotation,
            )
            created.append(handle_bar)

        cap = rmk.make_cylinder(
            f"{prefix}_FillerCap_{index}",
            radius=0.016,
            depth=0.018,
            location=neck_world,
            material=accent_material or hardware_material,
            rotation=rotation,
            vertices=8,
            bevel=0.001,
        )
        cap_hinge = rmk.make_box(
            f"{prefix}_CapHinge_{index}",
            (0.010, 0.008, 0.012),
            Vector(neck_world) + frame @ Vector((0.0, -0.018, 0.006)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        cap_latch = rmk.make_box(
            f"{prefix}_CapLatch_{index}",
            (0.008, 0.012, 0.008),
            Vector(neck_world) + frame @ Vector((0.0, 0.018, 0.006)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((cap, cap_hinge, cap_latch))

        strap = rmk.make_box(
            f"{prefix}_RatchetStrap_{index}",
            (config.strap_width, cd + 0.008, ch + 0.02),
            _transform_world(config.center, frame, (sx * can_spacing_x, bd * 0.5, 0.012 + ch * 0.5)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        buckle = rmk.make_box(
            f"{prefix}_RatchetBuckle_{index}",
            (config.strap_width * 1.4, 0.016, 0.024),
            _transform_world(config.center, frame, (sx * can_spacing_x, bd * 0.5 + cd * 0.52, 0.012 + ch * 0.60)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        ratchet_lever = rmk.make_box(
            f"{prefix}_RatchetLever_{index}",
            (0.008, 0.014, 0.028),
            _transform_world(config.center, frame, (sx * can_spacing_x + config.strap_width * 0.60, bd * 0.5 + cd * 0.54, 0.012 + ch * 0.62)),
            hardware_material,
            bevel=0.001,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(24.0))).to_euler(),
        )
        ratchet_spindle = rmk.make_cylinder(
            f"{prefix}_RatchetSpindle_{index}",
            radius=0.004,
            depth=config.strap_width * 1.5,
            location=_transform_world(config.center, frame, (sx * can_spacing_x, bd * 0.5 + cd * 0.52, 0.012 + ch * 0.60)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=8,
        )
        created.extend((strap, buckle, ratchet_lever, ratchet_spindle))

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = FUEL_AUX_JERRYCAN_RACK_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "FuelDrums"
    return created


# -------------------------------------------------------------------------
# Preset 3: Standoff Slat / Bar Armor Screen (Contemporary Military ERA)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class SlatArmorScreenConfig:
    """Dimensions and placement for a standoff slat/bar armor grid screen.

    Mounts at Socket_Hardpoint_ERA. Local +Y is the outward standoff direction,
    local +Z is vertical, local +X is lateral along the screen width.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 1.0, 0.0)
    screen_size: tuple[float, float] = (0.64, 0.44)
    standoff_distance: float = 0.22
    bar_count: int = 7
    bar_thickness: float = 0.006
    bar_depth: float = 0.024
    frame_bar_size: float = 0.020
    mount_foot_size: float = 0.065
    fastener_radius: float = 0.005
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.screen_size, self.standoff_distance,
                  self.bar_thickness, self.bar_depth, self.frame_bar_size, self.mount_foot_size,
                  self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("SlatArmorScreenConfig dimensions and placement must be finite")
        if min(*self.screen_size, self.standoff_distance, self.bar_thickness, self.bar_depth,
               self.frame_bar_size, self.mount_foot_size, self.fastener_radius) <= 0.0:
            raise ValueError("SlatArmorScreenConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("SlatArmorScreenConfig normal must not be zero")
        if self.bar_count < 3:
            raise ValueError("SlatArmorScreenConfig bar_count must be at least 3")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_slat_armor_screen_preset(
    *,
    prefix: str,
    config: SlatArmorScreenConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a statistical standoff slat/bar armor screen module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    sw, sh = config.screen_size
    standoff = config.standoff_distance
    f_bar = config.frame_bar_size

    # 1. Hull Mount Feet & Fasteners (at Y = 0.0)
    foot_x = sw * 0.35
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        foot = rmk.make_box(
            f"{prefix}_MountFoot_{side}",
            (config.mount_foot_size, 0.012, config.mount_foot_size),
            _transform_world(config.center, frame, (sx * foot_x, 0.006, 0.0)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
            bevel_segments=1,
            smooth=False,
        )
        created.append(foot)

        bolt_d = config.mount_foot_size * 0.36
        for bx in (-1.0, 1.0):
            for bz in (-1.0, 1.0):
                tag = f"{side}_{'L' if bx < 0 else 'R'}{'D' if bz < 0 else 'U'}"
                bolt = rmk.make_cylinder(
                    f"{prefix}_FootBolt_{tag}",
                    radius=config.fastener_radius,
                    depth=0.016,
                    location=_transform_world(config.center, frame, (sx * foot_x + bx * bolt_d, 0.012, bz * bolt_d)),
                    material=hardware_material,
                    rotation=rotation,
                    vertices=8,
                )
                created.append(bolt)

        arm_len = standoff - 0.012
        arm = rmk.make_box(
            f"{prefix}_StandoffArm_{side}",
            (0.024, arm_len, 0.032),
            _transform_world(config.center, frame, (sx * foot_x, 0.012 + arm_len * 0.5, 0.0)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
            bevel_segments=1,
            smooth=False,
        )
        gusset = rmk.make_box(
            f"{prefix}_GussetPlate_{side}",
            (0.008, 0.060, 0.060),
            _transform_world(config.center, frame, (sx * foot_x, standoff - 0.035, 0.032)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        sway_brace = rmk.make_box(
            f"{prefix}_SwayBrace_{side}",
            (0.014, arm_len * 0.65, 0.014),
            _transform_world(config.center, frame, (sx * foot_x, 0.012 + arm_len * 0.32, -sh * 0.18)),
            hardware_material,
            bevel=0.001,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(-24.0))).to_euler(),
        )
        created.extend((arm, gusset, sway_brace))

        # Quick-Release Maintenance Hinge Clevis & Lynchpin
        hinge_clevis = rmk.make_cylinder(
            f"{prefix}_HingeClevis_{side}",
            radius=0.012,
            depth=0.036,
            location=_transform_world(config.center, frame, (sx * foot_x, standoff - 0.016, 0.0)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 0.0, 1.0)), math.radians(90.0))).to_euler(),
            vertices=10,
            bevel=0.001,
        )
        lynch_pin = rmk.make_cylinder(
            f"{prefix}_LynchPin_{side}",
            radius=0.004,
            depth=0.044,
            location=_transform_world(config.center, frame, (sx * foot_x, standoff - 0.016, 0.0)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 0.0, 1.0)), math.radians(90.0))).to_euler(),
            vertices=8,
        )
        lynch_ring = rmk.make_torus(
            f"{prefix}_LynchRing_{side}",
            _transform_world(config.center, frame, (sx * foot_x, standoff - 0.016, 0.022)),
            major_radius=0.009,
            minor_radius=0.002,
            material=hardware_material,
            rotation=rotation,
            major_segments=10,
            minor_segments=6,
        )
        created.extend((hinge_clevis, lynch_pin, lynch_ring))

    # 2. Outer Perimeter Frame (at Y = standoff)
    rail_t = rmk.make_box(
        f"{prefix}_FrameRailTop",
        (sw, config.bar_depth, f_bar),
        _transform_world(config.center, frame, (0.0, standoff, sh * 0.5 - f_bar * 0.5)),
        armor_material,
        bevel=0.002,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    rail_b = rmk.make_box(
        f"{prefix}_FrameRailBottom",
        (sw, config.bar_depth, f_bar),
        _transform_world(config.center, frame, (0.0, standoff, -sh * 0.5 + f_bar * 0.5)),
        armor_material,
        bevel=0.002,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.extend((rail_t, rail_b))

    for sx, side in ((-1.0, "L"), (1.0, "R")):
        side_post = rmk.make_box(
            f"{prefix}_FramePost_{side}",
            (f_bar, config.bar_depth, sh - f_bar * 2.0),
            _transform_world(config.center, frame, (sx * (sw * 0.5 - f_bar * 0.5), standoff, 0.0)),
            armor_material,
            bevel=0.002,
            rotation=rotation,
            bevel_segments=1,
            smooth=False,
        )
        created.append(side_post)

        # Corner Stiffener Gussets
        for sz, vpos in ((-1.0, "Bottom"), (1.0, "Top")):
            corner_gusset = rmk.make_box(
                f"{prefix}_FrameGusset_{side}_{vpos}",
                (f_bar * 1.5, config.bar_depth * 0.70, f_bar * 1.5),
                _transform_world(config.center, frame, (sx * (sw * 0.5 - f_bar * 1.1), standoff, sz * (sh * 0.5 - f_bar * 1.1))),
                hardware_material,
                bevel=0.001,
                rotation=rotation,
            )
            created.append(corner_gusset)

        # Inter-panel Coupling Bracket Lug
        coupling_tab = rmk.make_box(
            f"{prefix}_CouplingTab_{side}",
            (0.016, 0.018, 0.040),
            _transform_world(config.center, frame, (sx * (sw * 0.5 + 0.006), standoff, 0.0)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        coupling_bolt = rmk.make_cylinder(
            f"{prefix}_CouplingBolt_{side}",
            radius=0.004,
            depth=0.022,
            location=_transform_world(config.center, frame, (sx * (sw * 0.5 + 0.006), standoff, 0.0)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=6,
        )
        created.extend((coupling_tab, coupling_bolt))

    # 3. Horizontal Slat Bars with Dihedral Deflection Bevels & Center Stiffener
    inner_h = sh - f_bar * 2.0
    bar_step_z = inner_h / (config.bar_count + 1)
    bar_w = sw - f_bar * 2.0 + 0.004
    for i in range(1, config.bar_count + 1):
        bz = -inner_h * 0.5 + i * bar_step_z
        bar = rmk.make_box(
            f"{prefix}_SlatBar_{i:02d}",
            (bar_w, config.bar_depth, config.bar_thickness),
            _transform_world(config.center, frame, (0.0, standoff, bz)),
            armor_material,
            bevel=0.0015,
            rotation=rotation,
            bevel_segments=1,
            smooth=False,
        )
        created.append(bar)

    center_rib = rmk.make_box(
        f"{prefix}_CenterStiffenerRib",
        (0.012, config.bar_depth * 0.82, inner_h),
        _transform_world(config.center, frame, (0.0, standoff - config.bar_depth * 0.09, 0.0)),
        hardware_material,
        bevel=0.001,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(center_rib)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = SLAT_ARMOR_SCREEN_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "ERA"
    return created


# -------------------------------------------------------------------------
# Preset 4: Faceted Stealth Phase Shield Emitter (Futuristic Military APS)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class ApsPhaseShieldConfig:
    """Dimensions and placement for a futuristic faceted phase shield projector.

    Mounts at Socket_Hardpoint_APS. Local +Z is outward mount normal, local +Y is
    forward projection axis, local +X is lateral axis.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 0.0, 1.0)
    nacelle_size: tuple[float, float, float] = (0.28, 0.38, 0.20)
    lens_radius: float = 0.075
    coil_ring_count: int = 2
    vent_louver_count: int = 4
    conduit_radius: float = 0.014
    facet_angle_deg: float = 45.0
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.nacelle_size, self.lens_radius,
                  self.conduit_radius, self.facet_angle_deg, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("ApsPhaseShieldConfig dimensions and placement must be finite")
        if min(*self.nacelle_size, self.lens_radius, self.conduit_radius, self.facet_angle_deg) <= 0.0:
            raise ValueError("ApsPhaseShieldConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("ApsPhaseShieldConfig normal must not be zero")
        if self.lens_radius * 2.0 >= min(self.nacelle_size[0], self.nacelle_size[2]):
            raise ValueError("Lens diameter exceeds nacelle front aperture")
        if self.vent_louver_count < 1:
            raise ValueError("vent_louver_count must be at least 1")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_aps_phase_shield_preset(
    *,
    prefix: str,
    config: ApsPhaseShieldConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic faceted stealth active phase shield emitter."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    nw, nl, nh = config.nacelle_size

    # 1. Base Mounting Interface Plate with Ceramic Isolator Studs
    base_plate = rmk.make_box(
        f"{prefix}_InterfaceBasePlate",
        (nw * 0.90, nl * 0.85, 0.014),
        _transform_world(config.center, frame, (0.0, 0.0, 0.007)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(base_plate)

    iso_x = nw * 0.38
    iso_y = nl * 0.35
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            iso_tag = ("L" if sx < 0 else "R") + ("R" if sy < 0 else "F")
            stud = rmk.make_cylinder(
                f"{prefix}_IsolatorStud_{iso_tag}",
                radius=0.007,
                depth=0.012,
                location=_transform_world(config.center, frame, (sx * iso_x, sy * iso_y, 0.014 + 0.004)),
                material=hardware_material,
                rotation=rotation,
                vertices=8,
                bevel=0.001,
            )
            created.append(stud)

    # 2. Faceted Stealth Nacelle Shell & Angled Chine Cheekplates
    nacelle_z = 0.014 + nh * 0.5
    nacelle = rmk.make_box(
        f"{prefix}_StealthNacelle",
        config.nacelle_size,
        _transform_world(config.center, frame, (0.0, 0.0, nacelle_z)),
        armor_material,
        bevel=0.018,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(nacelle)

    for sx, side in ((-1.0, "L"), (1.0, "R")):
        cheek = rmk.make_box(
            f"{prefix}_StealthCheekPlate_{side}",
            (0.016, nl * 0.42, nh * 0.78),
            _transform_world(config.center, frame, (sx * (nw * 0.45), nl * 0.18, nacelle_z)),
            armor_material,
            bevel=0.006,
            rotation=(frame @ Quaternion(Vector((0.0, 0.0, 1.0)), math.radians(sx * 14.0))).to_euler(),
        )
        created.append(cheek)

    # 3. Concave Projector Lens Cavity boolean cut
    lens_center_local = (0.0, nl * 0.5 - 0.010, nacelle_z)
    lens_center_world = _transform_world(config.center, frame, lens_center_local)
    cavity_cutter = rmk.make_cylinder(
        f"{prefix}_LensCavityCutter",
        radius=config.lens_radius + 0.006,
        depth=0.040,
        location=lens_center_world,
        material=None,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=16,
    )
    _apply_hardflow_or_builtin_boolean(nacelle, cavity_cutter, hardflow_boolean_apply)

    # Dielectric Emitter Core Lens & Convergent Field Stabilizer Electrodes
    lens = rmk.make_cylinder(
        f"{prefix}_FocusingLensCore",
        radius=config.lens_radius,
        depth=0.024,
        location=_transform_world(config.center, frame, (0.0, nl * 0.5 - 0.016, nacelle_z)),
        material=accent_material or hardware_material,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=16,
        bevel=0.003,
    )
    created.append(lens)

    # 3 Convergent Field Stabilizer Electrodes (120 deg apart around lens aperture)
    for p_idx, angle_deg in enumerate((90.0, 210.0, 330.0), 1):
        ang_rad = math.radians(angle_deg)
        px = math.cos(ang_rad) * (config.lens_radius * 0.88)
        pz = math.sin(ang_rad) * (config.lens_radius * 0.88)
        prong_pos = Vector(lens_center_world) + frame @ Vector((px, 0.008, pz))
        prong = rmk.make_cylinder(
            f"{prefix}_FieldStabilizer_{p_idx}",
            radius=0.007,
            depth=0.022,
            location=prong_pos,
            material=accent_material or hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=8,
            bevel=0.001,
        )
        created.append(prong)

    # Concentric Superconducting Magnetic Coils
    coil_rot = (frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler()
    for c_idx in range(1, config.coil_ring_count + 1):
        major_r = config.lens_radius * (0.65 + c_idx * 0.28)
        coil = rmk.make_torus(
            f"{prefix}_MagneticCoil_{c_idx}",
            _transform_world(config.center, frame, (0.0, nl * 0.5 - 0.025 - c_idx * 0.020, nacelle_z)),
            major_radius=major_r,
            minor_radius=0.009,
            material=hardware_material,
            rotation=coil_rot,
            major_segments=16,
            minor_segments=6,
        )
        created.append(coil)

    # 4. Upper Cryo Dissipation Louvers with Internal Radiator Grilles
    louver_count = config.vent_louver_count
    louver_spacing = (nl * 0.40) / max(louver_count - 1, 1)
    louver_start_y = -nl * 0.15
    for l_idx in range(louver_count):
        ly = louver_start_y + l_idx * louver_spacing
        cutter_pos = _transform_world(config.center, frame, (0.0, ly, nacelle_z + nh * 0.5))
        louver_cutter = rmk.make_box(
            f"{prefix}_LouverCutter_{l_idx + 1}",
            (nw * 0.52, 0.012, 0.016),
            cutter_pos,
            None,
            rotation=rotation,
        )
        _apply_hardflow_or_builtin_boolean(nacelle, louver_cutter, hardflow_boolean_apply)

        grille = rmk.make_box(
            f"{prefix}_LouverGrille_{l_idx + 1}",
            (nw * 0.48, 0.006, 0.004),
            _transform_world(config.center, frame, (0.0, ly, nacelle_z + nh * 0.5 - 0.006)),
            hardware_material,
            bevel=0.0005,
            rotation=rotation,
        )
        created.append(grille)

    # 5. Superconducting Flank Power Conduits & Cryo Feed Lines
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        conduit = rmk.make_cylinder(
            f"{prefix}_PowerConduit_{side}",
            radius=config.conduit_radius,
            depth=nl * 0.75,
            location=_transform_world(config.center, frame, (sx * (nw * 0.48), -nl * 0.05, nacelle_z)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
            bevel=0.001,
        )
        fitting = rmk.make_box(
            f"{prefix}_ConduitFitting_{side}",
            (0.022, 0.035, 0.035),
            _transform_world(config.center, frame, (sx * (nw * 0.48), -nl * 0.40, nacelle_z)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        cryo_line = rmk.make_cylinder(
            f"{prefix}_CryoLine_{side}",
            radius=0.006,
            depth=nl * 0.50,
            location=_transform_world(config.center, frame, (sx * (nw * 0.42), -nl * 0.18, nacelle_z - nh * 0.32)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=8,
            bevel=0.0005,
        )
        created.extend((conduit, fitting, cryo_line))

    _apply_hardflow_or_builtin_bevel(nacelle, hardflow_bevel, bevel_width=0.006)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = APS_PHASE_SHIELD_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "APS"
    return created


# -------------------------------------------------------------------------
# Preset 5: Polyhedral Plasma Magnetic Confinement Vault (Futuristic Sponsons)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class PlasmaContainmentCellConfig:
    """Dimensions and placement for a futuristic polyhedral plasma containment vault.

    Mounts at Socket_Hardpoint_Sponsons. Local +X is outward flank normal,
    local +Y is forward, local +Z is vertical.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (1.0, 0.0, 0.0)
    vault_size: tuple[float, float, float] = (0.54, 0.44, 0.40)
    core_window_radius: float = 0.09
    torus_major_radius: float = 0.16
    torus_minor_radius: float = 0.024
    fin_count: int = 5
    manifold_pipe_radius: float = 0.016
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.vault_size, self.core_window_radius,
                  self.torus_major_radius, self.torus_minor_radius, self.manifold_pipe_radius,
                  self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("PlasmaContainmentCellConfig dimensions and placement must be finite")
        if min(*self.vault_size, self.core_window_radius, self.torus_major_radius,
               self.torus_minor_radius, self.manifold_pipe_radius) <= 0.0:
            raise ValueError("PlasmaContainmentCellConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("PlasmaContainmentCellConfig normal must not be zero")
        if self.fin_count < 1:
            raise ValueError("fin_count must be at least 1")
        if self.core_window_radius * 2.0 >= min(self.vault_size[1], self.vault_size[2]):
            raise ValueError("Window diameter exceeds vault face")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_plasma_containment_cell_preset(
    *,
    prefix: str,
    config: PlasmaContainmentCellConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic polyhedral plasma magnetic confinement vault module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    vw, vd, vh = config.vault_size

    # 1. Armored Polyhedral Vault Shell & Sponson Deflectors
    vault_center = _transform_world(config.center, frame, (0.0, 0.0, vh * 0.5))
    vault = rmk.make_box(
        f"{prefix}_ContainmentVaultShell",
        config.vault_size,
        vault_center,
        armor_material,
        bevel=0.016,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(vault)

    deflector_top = rmk.make_box(
        f"{prefix}_SponsonDeflector_Upper",
        (vw * 0.90, vd * 0.90, 0.022),
        _transform_world(config.center, frame, (0.0, 0.0, vh + 0.011)),
        armor_material,
        bevel=0.006,
        rotation=rotation,
    )
    created.append(deflector_top)

    # 2. Flux Viewport aperture cut on outward face (+X)
    window_cutter = rmk.make_cylinder(
        f"{prefix}_WindowApertureCutter",
        radius=config.core_window_radius + 0.008,
        depth=0.040,
        location=_transform_world(config.center, frame, (vw * 0.5 - 0.010, 0.0, vh * 0.5)),
        material=None,
        rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=16,
    )
    _apply_hardflow_or_builtin_boolean(vault, window_cutter, hardflow_boolean_apply)

    # Bolted Hexagonal Bezel & Core Flux Viewport with Sensor Probes
    bezel_rot = (frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler()
    bezel = rmk.make_cylinder(
        f"{prefix}_FluxWindowBezel",
        radius=config.core_window_radius + 0.020,
        depth=0.016,
        location=_transform_world(config.center, frame, (vw * 0.5 + 0.002, 0.0, vh * 0.5)),
        material=hardware_material,
        rotation=bezel_rot,
        vertices=6,
        bevel=0.002,
    )
    core_glass = rmk.make_cylinder(
        f"{prefix}_FluxWindowCore",
        radius=config.core_window_radius,
        depth=0.012,
        location=_transform_world(config.center, frame, (vw * 0.5 - 0.002, 0.0, vh * 0.5)),
        material=accent_material or hardware_material,
        rotation=bezel_rot,
        vertices=16,
        bevel=0.001,
    )
    retaining_ring = rmk.make_cylinder(
        f"{prefix}_FluxRetainingRing",
        radius=config.core_window_radius * 0.88,
        depth=0.006,
        location=_transform_world(config.center, frame, (vw * 0.5 + 0.004, 0.0, vh * 0.5)),
        material=hardware_material,
        rotation=bezel_rot,
        vertices=16,
        bevel=0.0005,
    )
    created.extend((bezel, core_glass, retaining_ring))

    # 6 Bezel perimeter bolts & 4 Radial Flux Sensor Probes
    for b_idx in range(6):
        b_angle = math.radians(b_idx * 60.0)
        bx = math.cos(b_angle) * (config.core_window_radius + 0.012)
        by = math.sin(b_angle) * (config.core_window_radius + 0.012)
        bolt_pos = Vector(vault_center) + frame @ Vector((vw * 0.5 + 0.010, bx, by))
        b_obj = rmk.make_cylinder(
            f"{prefix}_BezelBolt_{b_idx + 1}",
            radius=0.004,
            depth=0.008,
            location=bolt_pos,
            material=hardware_material,
            rotation=bezel_rot,
            vertices=8,
        )
        created.append(b_obj)

    for p_idx, p_angle in enumerate((45.0, 135.0, 225.0, 315.0), 1):
        ang_rad = math.radians(p_angle)
        px = math.cos(ang_rad) * (config.core_window_radius + 0.024)
        py = math.sin(ang_rad) * (config.core_window_radius + 0.024)
        probe_pos = Vector(vault_center) + frame @ Vector((vw * 0.5 + 0.006, px, py))
        probe = rmk.make_cylinder(
            f"{prefix}_FluxProbe_{p_idx}",
            radius=0.005,
            depth=0.014,
            location=probe_pos,
            material=accent_material or hardware_material,
            rotation=bezel_rot,
            vertices=8,
            bevel=0.0005,
        )
        created.append(probe)

    # 3. Twin Magnetic Confinement Torus Rings & Saddles with Tie-Down Clamps
    torus_rot = (frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler()
    for t_idx, y_off in enumerate((-vd * 0.25, vd * 0.25), 1):
        torus = rmk.make_torus(
            f"{prefix}_MagneticTorus_{t_idx}",
            _transform_world(config.center, frame, (0.0, y_off, vh * 0.5)),
            major_radius=config.torus_major_radius,
            minor_radius=config.torus_minor_radius,
            material=hardware_material,
            rotation=torus_rot,
            major_segments=16,
            minor_segments=6,
        )
        created.append(torus)

        saddle = rmk.make_box(
            f"{prefix}_TorusSaddle_{t_idx}",
            (vw * 1.05, 0.035, config.torus_minor_radius * 2.8),
            _transform_world(config.center, frame, (0.0, y_off, vh * 0.5 + config.torus_major_radius)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        clamp_a = rmk.make_box(
            f"{prefix}_TorusClamp_{t_idx}A",
            (0.014, 0.040, config.torus_minor_radius * 2.4),
            _transform_world(config.center, frame, (-vw * 0.48, y_off, vh * 0.5 + config.torus_major_radius * 0.85)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        clamp_b = rmk.make_box(
            f"{prefix}_TorusClamp_{t_idx}B",
            (0.014, 0.040, config.torus_minor_radius * 2.4),
            _transform_world(config.center, frame, (vw * 0.48, y_off, vh * 0.5 + config.torus_major_radius * 0.85)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((saddle, clamp_a, clamp_b))

    # 4. Cryogenic Coolant Distribution Manifold & Directional Relief Exhaust Nozzles
    manifold = rmk.make_cylinder(
        f"{prefix}_CryoManifoldPipe",
        radius=config.manifold_pipe_radius,
        depth=vd * 0.85,
        location=_transform_world(config.center, frame, (-vw * 0.25, 0.0, vh + 0.02)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=12,
        bevel=0.001,
    )
    created.append(manifold)

    for v_idx, vy in enumerate((-vd * 0.30, vd * 0.30), 1):
        valve = rmk.make_box(
            f"{prefix}_ReliefValve_{v_idx}",
            (0.035, 0.035, 0.045),
            _transform_world(config.center, frame, (-vw * 0.25, vy, vh + 0.045)),
            accent_material or hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        nozzle = rmk.make_cylinder(
            f"{prefix}_ReliefNozzle_{v_idx}",
            radius=0.012,
            depth=0.024,
            location=_transform_world(config.center, frame, (-vw * 0.25, vy, vh + 0.075)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(18.0))).to_euler(),
            vertices=10,
            bevel=0.001,
        )
        created.extend((valve, nozzle))

    # 5. Machined Heat-Sink Cooling Fins on Base with Protective Skid
    fin_count = config.fin_count
    fin_spacing = (vd * 0.70) / max(fin_count - 1, 1)
    for f_idx in range(fin_count):
        fy = -vd * 0.35 + f_idx * fin_spacing
        fin = rmk.make_box(
            f"{prefix}_HeatSinkFin_{f_idx + 1}",
            (vw * 0.75, 0.006, 0.045),
            _transform_world(config.center, frame, (0.0, fy, -0.022)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.append(fin)

    fin_skid = rmk.make_box(
        f"{prefix}_FinSkidGuard",
        (vw * 0.80, vd * 0.75, 0.008),
        _transform_world(config.center, frame, (0.0, 0.0, -0.048)),
        hardware_material,
        bevel=0.0015,
        rotation=rotation,
    )
    created.append(fin_skid)

    # 6. Forged Heavy Lifting Lugs & Rear Optical Diagnostic Port
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        lug = rmk.make_box(
            f"{prefix}_LiftingLug_{side}",
            (0.024, 0.045, 0.055),
            _transform_world(config.center, frame, (sx * (vw * 0.38), -vd * 0.40, vh + 0.020)),
            hardware_material,
            bevel=0.003,
            rotation=rotation,
        )
        created.append(lug)

    diag_port = rmk.make_box(
        f"{prefix}_DiagnosticPort",
        (0.060, 0.010, 0.045),
        _transform_world(config.center, frame, (0.0, -vd * 0.5 - 0.004, vh * 0.40)),
        accent_material or hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    created.append(diag_port)

    _apply_hardflow_or_builtin_bevel(vault, hardflow_bevel, bevel_width=0.005)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = PLASMA_CONTAINMENT_CELL_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"
    return created


# -------------------------------------------------------------------------
# Preset 6: Honeycomb Hexagonal Reactive Tile Array (Futuristic Military ERA)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class StealthHexReactiveTileConfig:
    """Dimensions and placement for a futuristic interlocking hexagonal reactive tile array.

    Mounts at Socket_Hardpoint_ERA. Local +Z is outward support normal.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 0.0, 1.0)
    tile_radius: float = 0.075
    tile_height: float = 0.038
    tile_gap: float = 0.003
    backing_thickness: float = 0.008
    backing_overhang: float = 0.015
    facet_bevel: float = 0.008
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, self.tile_radius, self.tile_height,
                  self.tile_gap, self.backing_thickness, self.backing_overhang,
                  self.facet_bevel, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("StealthHexReactiveTileConfig dimensions and placement must be finite")
        if min(self.tile_radius, self.tile_height, self.tile_gap, self.backing_thickness,
               self.backing_overhang, self.facet_bevel) <= 0.0:
            raise ValueError("StealthHexReactiveTileConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("StealthHexReactiveTileConfig normal must not be zero")
        if self.facet_bevel >= min(self.tile_radius, self.tile_height) * 0.40:
            raise ValueError("facet_bevel is too large for the tile dimensions")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_stealth_hex_reactive_tile_preset(
    *,
    prefix: str,
    config: StealthHexReactiveTileConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic honeycomb interlocking hexagonal reactive tile array."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    r = config.tile_radius
    h = config.tile_height
    th = config.backing_thickness
    dist = math.sqrt(3.0) * r + config.tile_gap
    tile_coords: list[tuple[float, float]] = [(0.0, 0.0)]
    for i in range(6):
        ang = math.radians(30.0 + i * 60.0)
        tile_coords.append((math.cos(ang) * dist, math.sin(ang) * dist))

    # 1. Faceted Hex-Contoured Backing Plate (enveloping all 7 tiles)
    max_x = max(abs(x) for x, y in tile_coords) + r + config.backing_overhang
    max_y = max(abs(y) for x, y in tile_coords) + r + config.backing_overhang
    backing = rmk.make_box(
        f"{prefix}_CompositeBackingPlate",
        (max_x * 2.0, max_y * 2.0, th),
        _transform_world(config.center, frame, (0.0, 0.0, th * 0.5)),
        hardware_material,
        bevel=0.035,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(backing)

    # 2. Hexagonal Reactive Tiles with 2-Tier Stealth Faceting & Optical Diodes
    tile_z = th + h * 0.5
    for idx, (tx, ty) in enumerate(tile_coords, 1):
        tile_center = _transform_world(config.center, frame, (tx, ty, tile_z))
        tile = rmk.make_cylinder(
            f"{prefix}_HexTile_{idx:02d}",
            radius=r,
            depth=h,
            location=tile_center,
            material=armor_material,
            rotation=rotation,
            vertices=6,
            bevel=config.facet_bevel,
            bevel_segments=1,
        )
        created.append(tile)

        # Raised Central Plateau (2-tier stealth faceting)
        plateau = rmk.make_cylinder(
            f"{prefix}_TilePlateau_{idx:02d}",
            radius=r * 0.65,
            depth=0.005,
            location=_transform_world(config.center, frame, (tx, ty, th + h + 0.0025)),
            material=armor_material,
            rotation=rotation,
            vertices=6,
            bevel=0.001,
        )
        # Flush Optical Detonation Status Diode & Brass Retaining Collar
        collar = rmk.make_cylinder(
            f"{prefix}_DiodeCollar_{idx:02d}",
            radius=0.015,
            depth=0.004,
            location=_transform_world(config.center, frame, (tx, ty, th + h + 0.003)),
            material=hardware_material,
            rotation=rotation,
            vertices=8,
            bevel=0.0005,
        )
        diode = rmk.make_cylinder(
            f"{prefix}_SensorDiode_{idx:02d}",
            radius=0.009,
            depth=0.006,
            location=_transform_world(config.center, frame, (tx, ty, th + h + 0.005)),
            material=accent_material or hardware_material,
            rotation=rotation,
            vertices=8,
            bevel=0.001,
        )
        created.extend((plateau, collar, diode))

    # 3. Inter-Tile Valley Captive Fasteners (6 outer valleys)
    for v_idx in range(6):
        v_ang = math.radians(v_idx * 60.0)
        vx = math.cos(v_ang) * (r * 1.55)
        vy = math.sin(v_ang) * (r * 1.55)
        bolt = rmk.make_cylinder(
            f"{prefix}_ValleyMountBolt_{v_idx + 1}",
            radius=0.005,
            depth=th + 0.006,
            location=_transform_world(config.center, frame, (vx, vy, th * 0.5 + 0.003)),
            material=hardware_material,
            rotation=rotation,
            vertices=6,
        )
        created.append(bolt)

    # 4. Perimeter Mounting Lugs with countersunk screw holes
    for lug_idx, (lx, ly) in enumerate((
        (-max_x * 0.85, 0.0), (max_x * 0.85, 0.0),
        (0.0, -max_y * 0.85), (0.0, max_y * 0.85),
    ), 1):
        lug = rmk.make_box(
            f"{prefix}_MountLug_{lug_idx}",
            (0.024, 0.024, th + 0.004),
            _transform_world(config.center, frame, (lx, ly, th * 0.5)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        screw = rmk.make_cylinder(
            f"{prefix}_LugScrew_{lug_idx}",
            radius=0.004,
            depth=0.008,
            location=_transform_world(config.center, frame, (lx, ly, th + 0.003)),
            material=hardware_material,
            rotation=rotation,
            vertices=6,
        )
        created.extend((lug, screw))

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = STEALTH_HEX_REACTIVE_TILE_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "ERA"
    return created


# -------------------------------------------------------------------------
# Preset 7: Modular Anti-Tank Mine Dispenser Rack (Contemporary FuelDrums / Ordnance)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class OrdnanceMineDispenserConfig:
    """Dimensions and placement for a contemporary modular mine dispenser rack.

    Mounts at Socket_Hardpoint_FuelDrums (rear hull/transom). Local +Z is vertical,
    local +Y is outward/rearward launch axis, local +X is lateral.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    rack_size: tuple[float, float, float] = (0.64, 0.38, 0.44)
    cassette_count: int = 4
    tubes_per_cassette: int = 3
    canister_caliber: float = 0.068
    canister_length: float = 0.22
    launch_elevation_deg: float = 38.0
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.rack_size, self.canister_caliber,
                  self.canister_length, self.launch_elevation_deg, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("OrdnanceMineDispenserConfig dimensions and placement must be finite")
        if min(*self.rack_size, self.canister_caliber, self.canister_length) <= 0.0:
            raise ValueError("OrdnanceMineDispenserConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("OrdnanceMineDispenserConfig normal must not be zero")
        if self.cassette_count < 1 or self.tubes_per_cassette < 1:
            raise ValueError("cassette_count and tubes_per_cassette must be at least 1")
        if self.canister_caliber * self.cassette_count >= self.rack_size[0]:
            raise ValueError("Canisters exceed rack width")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_ordnance_mine_dispenser_preset(
    *,
    prefix: str,
    config: OrdnanceMineDispenserConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a contemporary tactical mine dispensing carrier with multi-cassette launch blocks."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    rw, rd, rh = config.rack_size

    # 1. Base Mounting Skid Plate & Deck Fasteners
    base_plate = rmk.make_box(
        f"{prefix}_MountSkidPlate",
        (rw, rd, 0.014),
        _transform_world(config.center, frame, (0.0, rd * 0.5, 0.007)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(base_plate)

    # 4 Corner Hex Mount Anchor Bolts
    for bx in (-1.0, 1.0):
        for by in (0.15, 0.85):
            bolt = rmk.make_cylinder(
                f"{prefix}_MountAnchor_{'L' if bx < 0 else 'R'}_{'F' if by < 0.5 else 'R'}",
                radius=0.006,
                depth=0.012,
                location=_transform_world(config.center, frame, (bx * (rw * 0.44), by * rd, 0.014 + 0.004)),
                material=hardware_material,
                rotation=rotation,
                vertices=8,
            )
            created.append(bolt)

    # 2 Longitudinal Bottom Skid Rails (ground/deck standoff clearance)
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        skid = rmk.make_cylinder(
            f"{prefix}_SkidRunner_{side}",
            radius=0.010,
            depth=rd * 0.92,
            location=_transform_world(config.center, frame, (sx * (rw * 0.42), rd * 0.5, -0.008)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
            bevel=0.001,
        )
        created.append(skid)

    # 2. Welded Tubular Support Frame & Pivot Axle
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        strut = rmk.make_box(
            f"{prefix}_AStrut_{side}",
            (0.024, 0.024, rh * 0.85),
            _transform_world(config.center, frame, (sx * (rw * 0.46), rd * 0.45, rh * 0.42)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        gusset = rmk.make_box(
            f"{prefix}_StrutGusset_{side}",
            (0.008, 0.060, 0.060),
            _transform_world(config.center, frame, (sx * (rw * 0.46), rd * 0.45, 0.040)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((strut, gusset))

    pivot_axle = rmk.make_cylinder(
        f"{prefix}_CassettePivotAxle",
        radius=0.014,
        depth=rw * 0.94,
        location=_transform_world(config.center, frame, (0.0, rd * 0.45, rh * 0.45)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=12,
        bevel=0.001,
    )
    created.append(pivot_axle)

    # 3. Modular Mine Dispenser Cassette Blocks (angled at launch_elevation_deg)
    cassette_w = (rw * 0.88) / config.cassette_count
    cassette_quat = frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(-config.launch_elevation_deg))
    cassette_rot = cassette_quat.to_euler()

    can_rad = config.canister_caliber * 0.5
    for c_idx in range(config.cassette_count):
        cx = -rw * 0.44 + (c_idx + 0.5) * cassette_w
        c_center = _transform_world(config.center, frame, (cx, rd * 0.48, rh * 0.52))

        cassette_box = rmk.make_box(
            f"{prefix}_MineCassette_{c_idx + 1}",
            (cassette_w * 0.88, config.canister_length * 0.85, config.canister_caliber * config.tubes_per_cassette * 1.15),
            c_center,
            armor_material,
            bevel=0.004,
            rotation=cassette_rot,
            bevel_segments=1,
            smooth=False,
        )
        created.append(cassette_box)

        # Launch Tubes inside the cassette
        for t_idx in range(config.tubes_per_cassette):
            tz_off = (t_idx - (config.tubes_per_cassette - 1) * 0.5) * (config.canister_caliber * 1.12)
            tube_pos = Vector(c_center) + cassette_quat @ Vector((0.0, 0.0, tz_off))
            tube = rmk.make_cylinder(
                f"{prefix}_Canister_{c_idx + 1}_{t_idx + 1}",
                radius=can_rad,
                depth=config.canister_length,
                location=tube_pos,
                material=hardware_material,
                rotation=(cassette_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=12,
                bevel=0.0015,
            )
            created.append(tube)

            # Bore cutter
            bore = rmk.make_cylinder(
                f"{prefix}_BoreCutter_{c_idx + 1}_{t_idx + 1}",
                radius=can_rad * 0.82,
                depth=0.060,
                location=Vector(tube_pos) + cassette_quat @ Vector((0.0, config.canister_length * 0.5 - 0.025, 0.0)),
                material=None,
                rotation=(cassette_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=10,
            )
            _apply_hardflow_or_builtin_boolean(tube, bore, hardflow_boolean_apply)

            # Breech Firing Solenoid Socket
            solenoid = rmk.make_cylinder(
                f"{prefix}_FiringSolenoid_{c_idx + 1}_{t_idx + 1}",
                radius=can_rad * 0.40,
                depth=0.016,
                location=Vector(tube_pos) - cassette_quat @ Vector((0.0, config.canister_length * 0.5 + 0.006, 0.0)),
                material=accent_material or hardware_material,
                rotation=(cassette_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=8,
            )
            created.append(solenoid)

    # 4. Emergency Jettison Lever & Umbilical Control Box
    jettison_lever = rmk.make_box(
        f"{prefix}_JettisonLever",
        (0.014, 0.035, 0.065),
        _transform_world(config.center, frame, (rw * 0.48 + 0.010, rd * 0.45, rh * 0.50)),
        accent_material or hardware_material,
        bevel=0.001,
        rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(20.0))).to_euler(),
    )
    control_box = rmk.make_box(
        f"{prefix}_ControlJunctionBox",
        (0.080, 0.045, 0.065),
        _transform_world(config.center, frame, (-rw * 0.38, 0.030, 0.045)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    conduit_gland = rmk.make_cylinder(
        f"{prefix}_ConduitGland",
        radius=0.008,
        depth=0.016,
        location=_transform_world(config.center, frame, (-rw * 0.38, 0.055, 0.045)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=8,
    )
    created.extend((jettison_lever, control_box, conduit_gland))

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = ORDNANCE_MINE_DISPENSER_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "FuelDrums"
    return created


# -------------------------------------------------------------------------
# Preset 8: Heavy Armored Louvered Radiator Sponson (Contemporary Sponsons / Thermal)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class ThermalArmoredRadiatorConfig:
    """Dimensions and placement for a heavy armored louvered radiator sponson pack.

    Mounts at Socket_Hardpoint_Sponsons (hull flank). Local +X is outward
    normal, local +Y is forward, local +Z is vertical.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (1.0, 0.0, 0.0)
    cowl_size: tuple[float, float, float] = (0.34, 0.60, 0.40)
    louver_count: int = 6
    manifold_pipe_radius: float = 0.018
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.cowl_size, self.manifold_pipe_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("ThermalArmoredRadiatorConfig dimensions and placement must be finite")
        if min(*self.cowl_size, self.manifold_pipe_radius) <= 0.0:
            raise ValueError("ThermalArmoredRadiatorConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("ThermalArmoredRadiatorConfig normal must not be zero")
        if self.louver_count < 2:
            raise ValueError("louver_count must be at least 2")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_thermal_armored_radiator_preset(
    *,
    prefix: str,
    config: ThermalArmoredRadiatorConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a heavy armored louvered radiator sponson pack with hydraulic coolant manifolds."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    cw, cd, ch = config.cowl_size

    # 1. Armored Ballistic Cowl Shell & Sloped Deflector Plates
    cowl_center = _transform_world(config.center, frame, (0.0, 0.0, ch * 0.5))
    cowl = rmk.make_box(
        f"{prefix}_BallisticCowlShell",
        config.cowl_size,
        cowl_center,
        armor_material,
        bevel=0.014,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(cowl)

    # Upper and Lower Sloped Ballistic Deflectors
    deflector_top = rmk.make_box(
        f"{prefix}_CowlDeflector_Top",
        (cw * 0.92, cd * 0.88, 0.024),
        _transform_world(config.center, frame, (0.0, 0.0, ch + 0.012)),
        armor_material,
        bevel=0.006,
        rotation=rotation,
    )
    deflector_bottom = rmk.make_box(
        f"{prefix}_CowlDeflector_Bottom",
        (cw * 0.92, cd * 0.88, 0.024),
        _transform_world(config.center, frame, (0.0, 0.0, -0.012)),
        armor_material,
        bevel=0.006,
        rotation=rotation,
    )
    created.extend((deflector_top, deflector_bottom))

    # 2. Radiator Matrix Cavity Cutout (+X face)
    cavity_w = cw * 0.50
    cavity_d = cd * 0.78
    cavity_h = ch * 0.72
    cavity_cutter = rmk.make_box(
        f"{prefix}_RadiatorCavityCutter",
        (cavity_w, cavity_d, cavity_h),
        _transform_world(config.center, frame, (cw * 0.5 - cavity_w * 0.5 + 0.005, 0.0, ch * 0.5)),
        None,
        rotation=rotation,
    )
    _apply_hardflow_or_builtin_boolean(cowl, cavity_cutter, hardflow_boolean_apply)

    # Internal Cooling Matrix Core inside cavity
    core = rmk.make_box(
        f"{prefix}_CoolingMatrixCore",
        (0.012, cavity_d * 0.95, cavity_h * 0.95),
        _transform_world(config.center, frame, (cw * 0.5 - cavity_w * 0.85, 0.0, ch * 0.5)),
        hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    created.append(core)

    # 3. Chevron Ballistic Armor Louvers (protecting the matrix from shrapnel)
    louver_count = config.louver_count
    louver_step = cavity_h / (louver_count + 1)
    for l_idx in range(1, louver_count + 1):
        lz = (ch * 0.5 - cavity_h * 0.5) + l_idx * louver_step
        louver_quat = frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(28.0))
        louver = rmk.make_box(
            f"{prefix}_ArmorLouver_{l_idx}",
            (0.045, cavity_d * 0.98, 0.008),
            _transform_world(config.center, frame, (cw * 0.5 - 0.015, 0.0, lz)),
            armor_material,
            bevel=0.001,
            rotation=louver_quat.to_euler(),
        )
        created.append(louver)

    # 4. Flanged Hydraulic Coolant Manifold Pipes
    for pos_z, pipe_tag in ((ch * 0.85, "Supply"), (ch * 0.15, "Return")):
        pipe = rmk.make_cylinder(
            f"{prefix}_Coolant{pipe_tag}Pipe",
            radius=config.manifold_pipe_radius,
            depth=cd * 0.94,
            location=_transform_world(config.center, frame, (-cw * 0.35, 0.0, pos_z)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=12,
            bevel=0.001,
        )
        created.append(pipe)

        for sy, y_pos in ((-1.0, -cd * 0.38), (1.0, cd * 0.38)):
            flange = rmk.make_cylinder(
                f"{prefix}_{pipe_tag}Flange_{'R' if sy < 0 else 'F'}",
                radius=config.manifold_pipe_radius * 1.55,
                depth=0.014,
                location=_transform_world(config.center, frame, (-cw * 0.35, y_pos, pos_z)),
                material=hardware_material,
                rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=8,
                bevel=0.001,
            )
            created.append(flange)

    # 5. Top Maintenance Access Hatch & Crane Lifting Lugs
    hatch = rmk.make_box(
        f"{prefix}_AccessHatchLid",
        (cw * 0.55, cd * 0.40, 0.012),
        _transform_world(config.center, frame, (0.0, 0.0, ch + 0.026)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    dog_latch_1 = rmk.make_box(
        f"{prefix}_HatchDogLatch_1",
        (0.014, 0.028, 0.012),
        _transform_world(config.center, frame, (0.0, -cd * 0.18, ch + 0.034)),
        accent_material or hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    dog_latch_2 = rmk.make_box(
        f"{prefix}_HatchDogLatch_2",
        (0.014, 0.028, 0.012),
        _transform_world(config.center, frame, (0.0, cd * 0.18, ch + 0.034)),
        accent_material or hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    created.extend((hatch, dog_latch_1, dog_latch_2))

    for sy, side_tag in ((-1.0, "Rear"), (1.0, "Front")):
        lug = rmk.make_box(
            f"{prefix}_LiftingLug_{side_tag}",
            (0.022, 0.040, 0.045),
            _transform_world(config.center, frame, (-cw * 0.32, sy * (cd * 0.42), ch + 0.020)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.append(lug)

    _apply_hardflow_or_builtin_bevel(cowl, hardflow_bevel, bevel_width=0.005)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = THERMAL_ARMORED_RADIATOR_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"
    return created


# -------------------------------------------------------------------------
# Preset 9: Remote 30mm Point-Defense Autocannon Pod (Contemporary Sponsons / Flak)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class AutocannonRemoteSponsonConfig:
    """Dimensions and placement for an automated point-defense 30mm autocannon sponson pod.

    Mounts at Socket_Hardpoint_Sponsons (hull flank). Local +X is outward
    normal, local +Y is forward firing axis, local +Z is vertical.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (1.0, 0.0, 0.0)
    cradle_size: tuple[float, float, float] = (0.24, 0.32, 0.28)
    caliber: float = 0.038
    barrel_length: float = 0.48
    elevation_deg: float = 12.0
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.cradle_size, self.caliber,
                  self.barrel_length, self.elevation_deg, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("AutocannonRemoteSponsonConfig dimensions and placement must be finite")
        if min(*self.cradle_size, self.caliber, self.barrel_length) <= 0.0:
            raise ValueError("AutocannonRemoteSponsonConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("AutocannonRemoteSponsonConfig normal must not be zero")
        if self.caliber * 3.0 >= self.cradle_size[0]:
            raise ValueError("Autocannon caliber exceeds cradle width")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_autocannon_remote_sponson_preset(
    *,
    prefix: str,
    config: AutocannonRemoteSponsonConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a remote 30mm point-defense autocannon sponson pod with ammo magazine and targeting optics."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    cw, cd, ch = config.cradle_size

    # 1. Base Azimuth Turntable Boss & Flange Bolts (flush at X = 0)
    turntable = rmk.make_cylinder(
        f"{prefix}_TurntableBoss",
        radius=min(cd, ch) * 0.45,
        depth=0.020,
        location=_transform_world(config.center, frame, (0.010, 0.0, ch * 0.5)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=16,
        bevel=0.002,
    )
    created.append(turntable)

    for b_idx in range(6):
        b_ang = math.radians(b_idx * 60.0)
        by = math.cos(b_ang) * (min(cd, ch) * 0.38)
        bz = math.sin(b_ang) * (min(cd, ch) * 0.38)
        bolt = rmk.make_cylinder(
            f"{prefix}_TurntableBolt_{b_idx + 1}",
            radius=0.005,
            depth=0.010,
            location=_transform_world(config.center, frame, (0.022, by, ch * 0.5 + bz)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=8,
        )
        created.append(bolt)

    # 2. Elevation Cradle & Receiver Housing
    cradle_center = _transform_world(config.center, frame, (0.020 + cw * 0.5, 0.0, ch * 0.5))
    cradle = rmk.make_box(
        f"{prefix}_ElevationCradle",
        config.cradle_size,
        cradle_center,
        armor_material,
        bevel=0.008,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(cradle)

    # Elevation Frame Tilt
    elev_quat = frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(config.elevation_deg))
    elev_rot = elev_quat.to_euler()

    # Elevation Trunnion Pivot Caps
    for sy, side_tag in ((-1.0, "Rear"), (1.0, "Front")):
        cap = rmk.make_cylinder(
            f"{prefix}_TrunnionCap_{side_tag}",
            radius=0.024,
            depth=0.012,
            location=_transform_world(config.center, frame, (0.020 + cw * 0.5, sy * (cd * 0.5 + 0.006), ch * 0.5)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=12,
            bevel=0.001,
        )
        created.append(cap)

    # Receiver Breech Block
    breech_pos = _transform_world(config.center, frame, (0.020 + cw * 0.5, 0.04, ch * 0.5))
    breech = rmk.make_box(
        f"{prefix}_ReceiverBreech",
        (cw * 0.55, 0.18, 0.12),
        breech_pos,
        hardware_material,
        bevel=0.003,
        rotation=elev_rot,
    )
    created.append(breech)

    # 3. Heavy Rifled Autocannon Barrel & Double-Baffle Muzzle Brake
    barrel_rad = config.caliber * 0.5
    barrel_center = Vector(breech_pos) + elev_quat @ Vector((0.0, 0.09 + config.barrel_length * 0.5, 0.0))
    barrel = rmk.make_cylinder(
        f"{prefix}_AutocannonBarrel",
        radius=barrel_rad,
        depth=config.barrel_length,
        location=barrel_center,
        material=hardware_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=14,
        bevel=0.0015,
    )
    created.append(barrel)

    # Barrel Bore Cutter
    bore_depth = 0.080
    bore = rmk.make_cylinder(
        f"{prefix}_BarrelBoreCutter",
        radius=barrel_rad * 0.76,
        depth=bore_depth,
        location=Vector(barrel_center) + elev_quat @ Vector((0.0, config.barrel_length * 0.5 - bore_depth * 0.5 + 0.001, 0.0)),
        material=None,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=12,
    )
    _apply_hardflow_or_builtin_boolean(barrel, bore, hardflow_boolean_apply)

    # Double-Baffle Muzzle Brake
    muzzle_brake = rmk.make_cylinder(
        f"{prefix}_MuzzleBrake",
        radius=barrel_rad * 1.55,
        depth=0.045,
        location=Vector(barrel_center) + elev_quat @ Vector((0.0, config.barrel_length * 0.5 - 0.022, 0.0)),
        material=hardware_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=12,
        bevel=0.002,
    )
    created.append(muzzle_brake)

    # Thermal Jacket Sleeve
    thermal_sleeve = rmk.make_cylinder(
        f"{prefix}_ThermalSleeve",
        radius=barrel_rad * 1.25,
        depth=config.barrel_length * 0.40,
        location=Vector(barrel_center) - elev_quat @ Vector((0.0, config.barrel_length * 0.15, 0.0)),
        material=armor_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=12,
        bevel=0.001,
    )
    created.append(thermal_sleeve)

    # 4. Conformal Ammunition Magazine & Feed Chute
    mag_pos = _transform_world(config.center, frame, (0.020 + cw * 0.5 + cw * 0.40, -0.06, ch * 0.5))
    magazine = rmk.make_box(
        f"{prefix}_AmmoMagazine",
        (cw * 0.45, 0.16, 0.20),
        mag_pos,
        armor_material,
        bevel=0.004,
        rotation=rotation,
    )
    feed_chute = rmk.make_box(
        f"{prefix}_AmmoFeedChute",
        (0.040, 0.10, 0.060),
        _transform_world(config.center, frame, (0.020 + cw * 0.5 + cw * 0.22, 0.02, ch * 0.5)),
        hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    created.extend((magazine, feed_chute))

    # 5. Coaxial EO/IR Targeting Sensor Ball
    optic_pos = _transform_world(config.center, frame, (0.020 + cw * 0.5 - cw * 0.35, 0.08, ch * 0.5 + 0.08))
    optic_body = rmk.make_cylinder(
        f"{prefix}_TargetingOpticBody",
        radius=0.032,
        depth=0.065,
        location=optic_pos,
        material=hardware_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=12,
        bevel=0.002,
    )
    optic_lens = rmk.make_cylinder(
        f"{prefix}_TargetingLens",
        radius=0.022,
        depth=0.010,
        location=Vector(optic_pos) + elev_quat @ Vector((0.0, 0.033, 0.0)),
        material=accent_material or hardware_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=12,
        bevel=0.001,
    )
    created.extend((optic_body, optic_lens))

    _apply_hardflow_or_builtin_bevel(cradle, hardflow_bevel, bevel_width=0.004)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = AUTOCANNON_REMOTE_SPONSON_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"
    return created


# -------------------------------------------------------------------------
# Preset 10: Quantum Plasma Torpedo Magazine Rack (Futuristic FuelDrums / Ordnance)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class PlasmaTorpedoMagazineConfig:
    """Dimensions and placement for a futuristic quantum plasma torpedo magazine rack.

    Mounts at Socket_Hardpoint_FuelDrums (rear hull). Local +Z is vertical,
    local +Y is outward rearward projection axis, local +X is lateral.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    rack_size: tuple[float, float, float] = (0.64, 0.38, 0.40)
    torpedo_count: int = 3
    torpedo_radius: float = 0.052
    torpedo_length: float = 0.36
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.rack_size, self.torpedo_radius,
                  self.torpedo_length, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("PlasmaTorpedoMagazineConfig dimensions and placement must be finite")
        if min(*self.rack_size, self.torpedo_radius, self.torpedo_length) <= 0.0:
            raise ValueError("PlasmaTorpedoMagazineConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("PlasmaTorpedoMagazineConfig normal must not be zero")
        if self.torpedo_count < 1:
            raise ValueError("torpedo_count must be at least 1")
        if self.torpedo_radius * 2.0 * self.torpedo_count >= self.rack_size[0] + 0.04:
            raise ValueError("Torpedoes do not fit in magazine rack width")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_plasma_torpedo_magazine_preset(
    *,
    prefix: str,
    config: PlasmaTorpedoMagazineConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic magnetic accelerator magazine carrying quantum plasma torpedoes."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    rw, rd, rh = config.rack_size

    # 1. Faceted Stealth Magazine Cradle & Magnetic Guide Rails
    base_plate = rmk.make_box(
        f"{prefix}_StealthCradleBase",
        (rw, rd, 0.016),
        _transform_world(config.center, frame, (0.0, rd * 0.5, 0.008)),
        armor_material,
        bevel=0.006,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(base_plate)

    # 4 Corner High-Flux Mounting Isolators
    for sx in (-1.0, 1.0):
        for sy in (0.15, 0.85):
            stud = rmk.make_cylinder(
                f"{prefix}_IsoMount_{'L' if sx < 0 else 'R'}_{'F' if sy < 0.5 else 'R'}",
                radius=0.008,
                depth=0.014,
                location=_transform_world(config.center, frame, (sx * (rw * 0.44), sy * rd, 0.016 + 0.004)),
                material=hardware_material,
                rotation=rotation,
                vertices=8,
            )
            created.append(stud)

    # 2. Torpedo Bays & Magnetic Accelerator Rails
    step_x = rw / (config.torpedo_count + 1)
    t_rad = config.torpedo_radius
    t_len = config.torpedo_length
    torpedo_rot = (frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler()

    for idx in range(1, config.torpedo_count + 1):
        tx = -rw * 0.5 + idx * step_x
        torpedo_z = 0.016 + t_rad * 1.35
        t_pos = _transform_world(config.center, frame, (tx, rd * 0.5, torpedo_z))

        # Magnetic Guide Rails (pair per torpedo bay)
        for rx_side in (-1.0, 1.0):
            rail = rmk.make_box(
                f"{prefix}_MagRail_{idx}_{'L' if rx_side < 0 else 'R'}",
                (0.012, rd * 0.90, 0.018),
                _transform_world(config.center, frame, (tx + rx_side * (t_rad * 1.15), rd * 0.5, torpedo_z - t_rad * 0.6)),
                hardware_material,
                bevel=0.001,
                rotation=rotation,
            )
            created.append(rail)

        # Torpedo Body
        torpedo = rmk.make_cylinder(
            f"{prefix}_PlasmaTorpedo_{idx}",
            radius=t_rad,
            depth=t_len,
            location=t_pos,
            material=armor_material,
            rotation=torpedo_rot,
            vertices=16,
            bevel=0.004,
        )
        # Tail Flux Propulsion Nozzle
        nozzle = rmk.make_cylinder(
            f"{prefix}_FluxNozzle_{idx}",
            radius=t_rad * 0.72,
            depth=0.035,
            location=Vector(t_pos) - frame @ Vector((0.0, t_len * 0.5 + 0.010, 0.0)),
            material=hardware_material,
            rotation=torpedo_rot,
            vertices=12,
            bevel=0.001,
        )
        created.extend((torpedo, nozzle))

        # Glowing Magnetic Containment Rings
        for r_idx, y_off in enumerate((-t_len * 0.22, t_len * 0.22), 1):
            ring = rmk.make_torus(
                f"{prefix}_ContainmentRing_{idx}_{r_idx}",
                Vector(t_pos) + frame @ Vector((0.0, y_off, 0.0)),
                major_radius=t_rad * 1.05,
                minor_radius=0.005,
                material=accent_material or hardware_material,
                rotation=torpedo_rot,
                major_segments=16,
                minor_segments=6,
            )
            created.append(ring)

        # Vacuum Purge Valve on rail
        valve = rmk.make_cylinder(
            f"{prefix}_PurgeValve_{idx}",
            radius=0.008,
            depth=0.014,
            location=_transform_world(config.center, frame, (tx, 0.04, torpedo_z + t_rad * 1.25)),
            material=hardware_material,
            rotation=rotation,
            vertices=8,
        )
        created.append(valve)

    # 3. Superconducting Power Conduits & Bulkhead Connectors
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        conduit = rmk.make_cylinder(
            f"{prefix}_PowerConduit_{side}",
            radius=0.012,
            depth=rd * 0.88,
            location=_transform_world(config.center, frame, (sx * (rw * 0.48), rd * 0.5, rh * 0.45)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
            bevel=0.001,
        )
        fitting = rmk.make_box(
            f"{prefix}_ConduitBulkhead_{side}",
            (0.024, 0.028, 0.028),
            _transform_world(config.center, frame, (sx * (rw * 0.48), 0.035, rh * 0.45)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((conduit, fitting))

    _apply_hardflow_or_builtin_bevel(base_plate, hardflow_bevel, bevel_width=0.004)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = PLASMA_TORPEDO_MAGAZINE_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "FuelDrums"
    return created


# -------------------------------------------------------------------------
# Preset 11: Tachyonic Superconducting Radiator Array (Futuristic Sponsons / Thermal)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class TachyonicRadiatorArrayConfig:
    """Dimensions and placement for a futuristic tachyonic superconducting radiator array.

    Mounts at Socket_Hardpoint_Sponsons (hull flank). Local +X is outward
    normal, local +Y is forward, local +Z is vertical.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (1.0, 0.0, 0.0)
    array_size: tuple[float, float, float] = (0.34, 0.62, 0.36)
    emitter_fin_count: int = 6
    core_rod_radius: float = 0.022
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.array_size, self.core_rod_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("TachyonicRadiatorArrayConfig dimensions and placement must be finite")
        if min(*self.array_size, self.core_rod_radius) <= 0.0:
            raise ValueError("TachyonicRadiatorArrayConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("TachyonicRadiatorArrayConfig normal must not be zero")
        if self.emitter_fin_count < 2:
            raise ValueError("emitter_fin_count must be at least 2")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_tachyonic_radiator_array_preset(
    *,
    prefix: str,
    config: TachyonicRadiatorArrayConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic tachyonic superconducting radiator array with quantum heat sinks."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    aw, ad, ah = config.array_size

    # 1. Diamond-Profile Stealth Cowl Shell
    cowl_center = _transform_world(config.center, frame, (0.0, 0.0, ah * 0.5))
    cowl = rmk.make_box(
        f"{prefix}_StealthRadiatorCowl",
        config.array_size,
        cowl_center,
        armor_material,
        bevel=0.018,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(cowl)

    # Lateral Emission Cavity Cutout (+X face)
    cav_w = aw * 0.55
    cav_d = ad * 0.82
    cav_h = ah * 0.74
    cutter = rmk.make_box(
        f"{prefix}_TachyonicCavityCutter",
        (cav_w, cav_d, cav_h),
        _transform_world(config.center, frame, (aw * 0.5 - cav_w * 0.5 + 0.005, 0.0, ah * 0.5)),
        None,
        rotation=rotation,
    )
    _apply_hardflow_or_builtin_boolean(cowl, cutter, hardflow_boolean_apply)

    # 2. Superconducting Central Heat Sink Core Rod (emissive accent)
    core = rmk.make_cylinder(
        f"{prefix}_SuperconductingHeatSink",
        radius=config.core_rod_radius,
        depth=cav_d * 0.94,
        location=_transform_world(config.center, frame, (aw * 0.5 - cav_w * 0.65, 0.0, ah * 0.5)),
        material=accent_material or hardware_material,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=16,
        bevel=0.002,
    )
    created.append(core)

    # 3. Forward-Swept Geometric Heat Dissipation Fins
    fin_count = config.emitter_fin_count
    fin_step = cav_d / (fin_count + 1)
    for f_idx in range(1, fin_count + 1):
        fy = -cav_d * 0.5 + f_idx * fin_step
        fin_quat = frame @ Quaternion(Vector((0.0, 0.0, 1.0)), math.radians(24.0))
        fin = rmk.make_box(
            f"{prefix}_TachyonicFin_{f_idx}",
            (cav_w * 0.92, 0.008, cav_h * 0.90),
            _transform_world(config.center, frame, (aw * 0.5 - cav_w * 0.35, fy, ah * 0.5)),
            hardware_material,
            bevel=0.001,
            rotation=fin_quat.to_euler(),
        )
        created.append(fin)

    # 4. Magnetic Field Deflection Emitter Nodes (4 nodes along edges)
    for ny in (-ad * 0.38, ad * 0.38):
        for nz in (ah * 0.20, ah * 0.80):
            node = rmk.make_cylinder(
                f"{prefix}_FieldDeflectionNode_{'F' if ny > 0 else 'R'}_{'T' if nz > ah * 0.5 else 'B'}",
                radius=0.012,
                depth=0.016,
                location=_transform_world(config.center, frame, (aw * 0.5 + 0.004, ny, nz)),
                material=accent_material or hardware_material,
                rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=8,
                bevel=0.001,
            )
            created.append(node)

    # 5. Cryogenic Coolant Distribution Manifold with Magnetic Couplers
    for p_z, tag in ((ah * 0.88, "Top"), (ah * 0.12, "Bottom")):
        pipe = rmk.make_cylinder(
            f"{prefix}_CryoManifoldPipe_{tag}",
            radius=0.012,
            depth=ad * 0.92,
            location=_transform_world(config.center, frame, (-aw * 0.38, 0.0, p_z)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
            bevel=0.001,
        )
        coupler = rmk.make_box(
            f"{prefix}_MagneticCoupler_{tag}",
            (0.028, 0.035, 0.028),
            _transform_world(config.center, frame, (-aw * 0.38, ad * 0.44, p_z)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((pipe, coupler))

    _apply_hardflow_or_builtin_bevel(cowl, hardflow_bevel, bevel_width=0.006)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = TACHYONIC_RADIATOR_ARRAY_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"
    return created


# -------------------------------------------------------------------------
# Preset 12: Autonomous Nanite Hive Repair Dispenser (Futuristic Sponsons / Repair)
# -------------------------------------------------------------------------

@dataclass(frozen=True)
class NaniteRepairDispenserConfig:
    """Dimensions and placement for an autonomous nanite hive repair dispenser pod.

    Mounts at Socket_Hardpoint_Sponsons (hull flank). Local +X is outward
    normal, local +Y is forward dispersal axis, local +Z is vertical.
    """
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (1.0, 0.0, 0.0)
    pod_size: tuple[float, float, float] = (0.32, 0.46, 0.36)
    canister_radius: float = 0.062
    nozzle_count: int = 3
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.pod_size, self.canister_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("NaniteRepairDispenserConfig dimensions and placement must be finite")
        if min(*self.pod_size, self.canister_radius) <= 0.0:
            raise ValueError("NaniteRepairDispenserConfig dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("NaniteRepairDispenserConfig normal must not be zero")
        if self.nozzle_count < 1:
            raise ValueError("nozzle_count must be at least 1")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_nanite_repair_dispenser_preset(
    *,
    prefix: str,
    config: NaniteRepairDispenserConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build an autonomous nanite hive dispenser pod with storage cylinders and dispersal nozzles."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    pw, pd, ph = config.pod_size

    # 1. Faceted Armored Dispenser Pod Housing
    pod_center = _transform_world(config.center, frame, (0.0, 0.0, ph * 0.5))
    housing = rmk.make_box(
        f"{prefix}_DispenserHousing",
        config.pod_size,
        pod_center,
        armor_material,
        bevel=0.014,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(housing)

    # Forward Dispersal Aperture Cutout (+Y face)
    aperture_cutter = rmk.make_box(
        f"{prefix}_ApertureCutter",
        (pw * 0.76, 0.080, ph * 0.60),
        _transform_world(config.center, frame, (0.0, pd * 0.5 - 0.020, ph * 0.5)),
        None,
        rotation=rotation,
    )
    _apply_hardflow_or_builtin_boolean(housing, aperture_cutter, hardflow_boolean_apply)

    # Deployable Aperture Shutter Doors
    for sz, door_tag in ((-1.0, "Lower"), (1.0, "Upper")):
        door = rmk.make_box(
            f"{prefix}_ShutterDoor_{door_tag}",
            (pw * 0.74, 0.012, ph * 0.28),
            _transform_world(config.center, frame, (0.0, pd * 0.5 - 0.010, ph * 0.5 + sz * (ph * 0.22))),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.append(door)

    # 2. Dual Pressurized Nanite Matrix Cylinders with Viewports
    can_rad = config.canister_radius
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        can_pos = _transform_world(config.center, frame, (sx * (pw * 0.28), -pd * 0.10, ph * 0.5))
        canister = rmk.make_cylinder(
            f"{prefix}_NaniteCylinder_{side}",
            radius=can_rad,
            depth=pd * 0.60,
            location=can_pos,
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=14,
            bevel=0.002,
        )
        viewport = rmk.make_cylinder(
            f"{prefix}_NaniteViewport_{side}",
            radius=can_rad * 0.40,
            depth=pd * 0.45,
            location=Vector(can_pos) + frame @ Vector((sx * (can_rad * 0.75), 0.0, 0.0)),
            material=accent_material or hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
            bevel=0.001,
        )
        created.extend((canister, viewport))

    # 3. Convergent Electromagnetic Dispersal Nozzle Array
    nozzle_count = config.nozzle_count
    nozzle_step = (pw * 0.55) / max(nozzle_count - 1, 1)
    for n_idx in range(nozzle_count):
        nx = -pw * 0.275 + n_idx * nozzle_step
        n_pos = _transform_world(config.center, frame, (nx, pd * 0.5 - 0.005, ph * 0.5))
        nozzle = rmk.make_cylinder(
            f"{prefix}_DispersalNozzle_{n_idx + 1}",
            radius=0.016,
            depth=0.038,
            location=n_pos,
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=12,
            bevel=0.0015,
        )
        ring = rmk.make_torus(
            f"{prefix}_NozzleFocusRing_{n_idx + 1}",
            Vector(n_pos) + frame @ Vector((0.0, 0.014, 0.0)),
            major_radius=0.018,
            minor_radius=0.003,
            material=accent_material or hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            major_segments=12,
            minor_segments=5,
        )
        created.extend((nozzle, ring))

    # 4. Fluid Supply Lines & Telemetry Transceiver
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        line = rmk.make_cylinder(
            f"{prefix}_FluidLine_{side}",
            radius=0.007,
            depth=pd * 0.75,
            location=_transform_world(config.center, frame, (sx * (pw * 0.44), -pd * 0.05, ph * 0.22)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=8,
            bevel=0.0005,
        )
        created.append(line)

    transceiver = rmk.make_cylinder(
        f"{prefix}_TelemetryTransceiver",
        radius=0.008,
        depth=0.055,
        location=_transform_world(config.center, frame, (0.0, -pd * 0.38, ph + 0.025)),
        material=hardware_material,
        rotation=rotation,
        vertices=8,
        bevel=0.001,
    )
    indicator = rmk.make_cylinder(
        f"{prefix}_StatusIndicator",
        radius=0.005,
        depth=0.008,
        location=_transform_world(config.center, frame, (0.0, -pd * 0.38, ph + 0.055)),
        material=accent_material or hardware_material,
        rotation=rotation,
        vertices=6,
    )
    created.extend((transceiver, indicator))

    _apply_hardflow_or_builtin_bevel(housing, hardflow_bevel, bevel_width=0.005)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = NANITE_REPAIR_DISPENSER_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"
    return created


from hard_surface_geometry_presets_v2 import (  # noqa: E402
    ApsHardkillRadarV2Config,
    FuelAuxJerrycanRackV2Config,
    SlatArmorScreenV2Config,
    OrdnanceMineDispenserV2Config,
    ThermalArmoredRadiatorV2Config,
    AutocannonRemoteSponsonV2Config,
    ApsPhaseShieldV2Config,
    PlasmaContainmentCellV2Config,
    StealthHexReactiveTileV2Config,
    PlasmaTorpedoMagazineV2Config,
    TachyonicRadiatorArrayV2Config,
    NaniteRepairDispenserV2Config,
    create_aps_hardkill_radar_v2_preset,
    create_fuel_aux_jerrycan_rack_v2_preset,
    create_slat_armor_screen_v2_preset,
    create_ordnance_mine_dispenser_v2_preset,
    create_thermal_armored_radiator_v2_preset,
    create_autocannon_remote_sponson_v2_preset,
    create_aps_phase_shield_v2_preset,
    create_plasma_containment_cell_v2_preset,
    create_stealth_hex_reactive_tile_v2_preset,
    create_plasma_torpedo_magazine_v2_preset,
    create_tachyonic_radiator_array_v2_preset,
    create_nanite_repair_dispenser_v2_preset,
)
from hard_surface_geometry_presets_functional import (  # noqa: E402
    ApuTurbogeneratorSponsonConfig,
    DockingClampHydraulicArmConfig,
    ExcavationDrillCarbideBorerConfig,
    GravitonRecoilAnchorConfig,
    MagneticTractorDockingClampConfig,
    OptronicReconSensorMastConfig,
    PlasmaExcavationTorchConfig,
    SmokeGrenadeSalvoDischargerConfig,
    create_apu_turbogenerator_sponson_preset,
    create_docking_clamp_hydraulic_arm_preset,
    create_excavation_drill_carbide_borer_preset,
    create_graviton_recoil_anchor_preset,
    create_magnetic_tractor_docking_clamp_preset,
    create_optronic_recon_sensor_mast_preset,
    create_plasma_excavation_torch_preset,
    create_smoke_grenade_salvo_discharger_preset,
)


GEOMETRY_PRESETS = {
    SERVICE_HATCH_PRESET_ID: create_service_hatch_preset,
    ERA_BRICK_PRESET_ID: create_era_brick_preset,
    COAXIAL_SERVICE_CONNECTOR_PRESET_ID: create_coaxial_service_connector_preset,
    APS_HARDKILL_RADAR_PRESET_ID: create_aps_hardkill_radar_preset,
    FUEL_AUX_JERRYCAN_RACK_PRESET_ID: create_fuel_aux_jerrycan_rack_preset,
    SLAT_ARMOR_SCREEN_PRESET_ID: create_slat_armor_screen_preset,
    ORDNANCE_MINE_DISPENSER_PRESET_ID: create_ordnance_mine_dispenser_preset,
    THERMAL_ARMORED_RADIATOR_PRESET_ID: create_thermal_armored_radiator_preset,
    AUTOCANNON_REMOTE_SPONSON_PRESET_ID: create_autocannon_remote_sponson_preset,
    APS_PHASE_SHIELD_PRESET_ID: create_aps_phase_shield_preset,
    PLASMA_CONTAINMENT_CELL_PRESET_ID: create_plasma_containment_cell_preset,
    STEALTH_HEX_REACTIVE_TILE_PRESET_ID: create_stealth_hex_reactive_tile_preset,
    PLASMA_TORPEDO_MAGAZINE_PRESET_ID: create_plasma_torpedo_magazine_preset,
    TACHYONIC_RADIATOR_ARRAY_PRESET_ID: create_tachyonic_radiator_array_preset,
    NANITE_REPAIR_DISPENSER_PRESET_ID: create_nanite_repair_dispenser_preset,
    # Polished v2 presets
    APS_HARDKILL_RADAR_V2_PRESET_ID: create_aps_hardkill_radar_v2_preset,
    FUEL_AUX_JERRYCAN_RACK_V2_PRESET_ID: create_fuel_aux_jerrycan_rack_v2_preset,
    SLAT_ARMOR_SCREEN_V2_PRESET_ID: create_slat_armor_screen_v2_preset,
    ORDNANCE_MINE_DISPENSER_V2_PRESET_ID: create_ordnance_mine_dispenser_v2_preset,
    THERMAL_ARMORED_RADIATOR_V2_PRESET_ID: create_thermal_armored_radiator_v2_preset,
    AUTOCANNON_REMOTE_SPONSON_V2_PRESET_ID: create_autocannon_remote_sponson_v2_preset,
    APS_PHASE_SHIELD_V2_PRESET_ID: create_aps_phase_shield_v2_preset,
    PLASMA_CONTAINMENT_CELL_V2_PRESET_ID: create_plasma_containment_cell_v2_preset,
    STEALTH_HEX_REACTIVE_TILE_V2_PRESET_ID: create_stealth_hex_reactive_tile_v2_preset,
    PLASMA_TORPEDO_MAGAZINE_V2_PRESET_ID: create_plasma_torpedo_magazine_v2_preset,
    TACHYONIC_RADIATOR_ARRAY_V2_PRESET_ID: create_tachyonic_radiator_array_v2_preset,
    NANITE_REPAIR_DISPENSER_V2_PRESET_ID: create_nanite_repair_dispenser_v2_preset,
    # Functional Level 2 presets
    EXCAVATION_DRILL_CARBIDE_BORER_PRESET_ID: create_excavation_drill_carbide_borer_preset,
    DOCKING_CLAMP_HYDRAULIC_ARM_PRESET_ID: create_docking_clamp_hydraulic_arm_preset,
    APU_TURBOGENERATOR_SPONSON_PRESET_ID: create_apu_turbogenerator_sponson_preset,
    SMOKE_GRENADE_SALVO_DISCHARGER_PRESET_ID: create_smoke_grenade_salvo_discharger_preset,
    PLASMA_EXCAVATION_TORCH_PRESET_ID: create_plasma_excavation_torch_preset,
    MAGNETIC_TRACTOR_DOCKING_CLAMP_PRESET_ID: create_magnetic_tractor_docking_clamp_preset,
    GRAVITON_RECOIL_ANCHOR_PRESET_ID: create_graviton_recoil_anchor_preset,
    OPTRONIC_RECON_SENSOR_MAST_PRESET_ID: create_optronic_recon_sensor_mast_preset,
}

PRESET_METADATA: dict[str, dict[str, str]] = {
    SERVICE_HATCH_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "Sponsons",
        "displayName": "Recessed Service Access Hatch v1",
        "engineeringRole": "utility_sponson",
    },
    ERA_BRICK_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "ERA",
        "displayName": "Cross-Seamed Explosive Reactive Armor Cassette v1",
        "engineeringRole": "armor_cassette",
    },
    COAXIAL_SERVICE_CONNECTOR_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "Sponsons",
        "displayName": "Recessed Coaxial Power-Cell Service Connector v1",
        "engineeringRole": "power_containment",
    },
    APS_HARDKILL_RADAR_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "APS",
        "displayName": "AESA Hard-Kill Radar & Countermeasure Pod v1",
        "engineeringRole": "aps_hardkill_radar",
    },
    FUEL_AUX_JERRYCAN_RACK_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "FuelDrums",
        "displayName": "Tactical Stamped Jerrycan Skid Basket v1",
        "engineeringRole": "fuel_jerrycan_carrier",
    },
    SLAT_ARMOR_SCREEN_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "ERA",
        "displayName": "Standoff Slat / Bar Armor Screen v1",
        "engineeringRole": "slat_armor_screen",
    },
    ORDNANCE_MINE_DISPENSER_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "FuelDrums",
        "displayName": "Modular Anti-Tank Mine Dispenser Rack v1",
        "engineeringRole": "ordnance_carrier",
    },
    THERMAL_ARMORED_RADIATOR_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "Sponsons",
        "displayName": "Heavy Armored Louvered Radiator Sponson v1",
        "engineeringRole": "thermal_radiator",
    },
    AUTOCANNON_REMOTE_SPONSON_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "Sponsons",
        "displayName": "Remote 30mm Point-Defense Autocannon Pod v1",
        "engineeringRole": "heavy_flak_battery",
    },
    APS_PHASE_SHIELD_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "APS",
        "displayName": "Faceted Stealth Phase Shield Emitter v1",
        "engineeringRole": "aps_phase_shield",
    },
    PLASMA_CONTAINMENT_CELL_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "Sponsons",
        "displayName": "Polyhedral Plasma Magnetic Confinement Vault v1",
        "engineeringRole": "plasma_containment_cell",
    },
    STEALTH_HEX_REACTIVE_TILE_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "ERA",
        "displayName": "Honeycomb Hexagonal Composite Reactive Tile Array v1",
        "engineeringRole": "stealth_hex_reactive_tile",
    },
    PLASMA_TORPEDO_MAGAZINE_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "FuelDrums",
        "displayName": "Quantum Magnetic Torpedo Magazine Rack v1",
        "engineeringRole": "ordnance_carrier",
    },
    TACHYONIC_RADIATOR_ARRAY_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "Sponsons",
        "displayName": "Tachyonic Superconducting Radiator Array v1",
        "engineeringRole": "thermal_radiator",
    },
    NANITE_REPAIR_DISPENSER_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "Sponsons",
        "displayName": "Autonomous Nanite Hive Dispenser Pod v1",
        "engineeringRole": "repair_dispenser",
    },
    # Polished v2 presets metadata
    APS_HARDKILL_RADAR_V2_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "APS",
        "displayName": "AESA Hard-Kill Radar & Quad Countermeasure Pod v2",
        "engineeringRole": "aps_hardkill_radar",
    },
    FUEL_AUX_JERRYCAN_RACK_V2_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "FuelDrums",
        "displayName": "Tactical Stamped Jerrycan Skid Basket v2",
        "engineeringRole": "fuel_jerrycan_carrier",
    },
    SLAT_ARMOR_SCREEN_V2_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "ERA",
        "displayName": "Standoff Knife-Edge Slat Armor Screen v2",
        "engineeringRole": "slat_armor_screen",
    },
    ORDNANCE_MINE_DISPENSER_V2_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "FuelDrums",
        "displayName": "Modular Anti-Tank Mine Dispenser Rack v2",
        "engineeringRole": "ordnance_carrier",
    },
    THERMAL_ARMORED_RADIATOR_V2_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "Sponsons",
        "displayName": "Heavy Armored Louvered Radiator Sponson v2",
        "engineeringRole": "thermal_radiator",
    },
    AUTOCANNON_REMOTE_SPONSON_V2_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "Sponsons",
        "displayName": "Remote 30mm Point-Defense Autocannon Pod v2",
        "engineeringRole": "heavy_flak_battery",
    },
    APS_PHASE_SHIELD_V2_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "APS",
        "displayName": "Faceted Geodesic Phase Shield Emitter v2",
        "engineeringRole": "aps_phase_shield",
    },
    PLASMA_CONTAINMENT_CELL_V2_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "Sponsons",
        "displayName": "Polyhedral Plasma Confinement Vault v2",
        "engineeringRole": "plasma_containment_cell",
    },
    STEALTH_HEX_REACTIVE_TILE_V2_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "ERA",
        "displayName": "Honeycomb Hexagonal Reactive Tile Array v2",
        "engineeringRole": "stealth_hex_reactive_tile",
    },
    PLASMA_TORPEDO_MAGAZINE_V2_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "FuelDrums",
        "displayName": "Quantum Plasma Torpedo Magazine Rack v2",
        "engineeringRole": "ordnance_carrier",
    },
    TACHYONIC_RADIATOR_ARRAY_V2_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "Sponsons",
        "displayName": "Tachyonic Superconducting Radiator Array v2",
        "engineeringRole": "thermal_radiator",
    },
    NANITE_REPAIR_DISPENSER_V2_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "Sponsons",
        "displayName": "Autonomous Nanite Hive Repair Dispenser v2",
        "engineeringRole": "repair_dispenser",
    },
    # Functional Level 2 presets metadata
    EXCAVATION_DRILL_CARBIDE_BORER_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "Sponsons",
        "displayName": "Rotary Carbide Excavation Borer v1",
        "engineeringRole": "excavation_drill",
    },
    DOCKING_CLAMP_HYDRAULIC_ARM_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "Sponsons",
        "displayName": "Articulated Hydraulic Docking Clamp v1",
        "engineeringRole": "docking_clamp",
    },
    APU_TURBOGENERATOR_SPONSON_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "Sponsons",
        "displayName": "Tactical APU Turbogenerator Sponson v1",
        "engineeringRole": "power_containment",
    },
    SMOKE_GRENADE_SALVO_DISCHARGER_PRESET_ID: {
        "style": "contemporary_military",
        "slot": "APS",
        "displayName": "Multispectral Smoke Salvo Discharger Bank v1",
        "engineeringRole": "aps_sensor_suite",
    },
    PLASMA_EXCAVATION_TORCH_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "Sponsons",
        "displayName": "Quantum Plasma Excavation Lance v1",
        "engineeringRole": "excavation_drill",
    },
    MAGNETIC_TRACTOR_DOCKING_CLAMP_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "Sponsons",
        "displayName": "Zero-G Magnetic Tractor Docking Clamp v1",
        "engineeringRole": "docking_clamp",
    },
    GRAVITON_RECOIL_ANCHOR_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "Sponsons",
        "displayName": "Kinetic & Graviton Recoil Anchor Pod v1",
        "engineeringRole": "utility_sponson",
    },
    OPTRONIC_RECON_SENSOR_MAST_PRESET_ID: {
        "style": "futuristic_military",
        "slot": "APS",
        "displayName": "Optronic Recon & Radar Sensor Mast v1",
        "engineeringRole": "aps_sensor_suite",
    },
}

PRESET_STYLES: dict[str, str] = {k: v["style"] for k, v in PRESET_METADATA.items()}
PRESET_SLOTS: dict[str, str] = {k: v["slot"] for k, v in PRESET_METADATA.items()}
