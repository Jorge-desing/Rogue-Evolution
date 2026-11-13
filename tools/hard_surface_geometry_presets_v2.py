"""Reusable Level 2 hard-surface geometry presets (v2 polished generation).

Implements improved and polished v2 mechanical models for contemporary and
futuristic military canonical vehicle components, featuring refined RTS silhouettes,
functional load paths, micro-mechanical articulation, and 4-channel vertex-wear contracts.
"""

from __future__ import annotations

import math
from dataclasses import asdict, dataclass
from typing import Callable, Mapping

try:
    import bpy
    import bmesh
    from mathutils import Matrix, Quaternion, Vector
    import RogueModelKit as rmk
except ImportError:
    bpy = None
    bmesh = None
    Matrix = None
    Quaternion = None
    Vector = None
    rmk = None

from hard_surface_geometry_presets import (
    APS_HARDKILL_RADAR_V2_PRESET_ID,
    APS_PHASE_SHIELD_V2_PRESET_ID,
    AUTOCANNON_REMOTE_SPONSON_V2_PRESET_ID,
    FUEL_AUX_JERRYCAN_RACK_V2_PRESET_ID,
    NANITE_REPAIR_DISPENSER_V2_PRESET_ID,
    ORDNANCE_MINE_DISPENSER_V2_PRESET_ID,
    PLASMA_CONTAINMENT_CELL_V2_PRESET_ID,
    PLASMA_TORPEDO_MAGAZINE_V2_PRESET_ID,
    SLAT_ARMOR_SCREEN_V2_PRESET_ID,
    STEALTH_HEX_REACTIVE_TILE_V2_PRESET_ID,
    TACHYONIC_RADIATOR_ARRAY_V2_PRESET_ID,
    THERMAL_ARMORED_RADIATOR_V2_PRESET_ID,
    _apply_hardflow_or_builtin_bevel,
    _apply_hardflow_or_builtin_boolean,
    _local_y_outward_frame,
    _orientation_frame,
    _side_mount_frame,
    _transform_world,
)


# =========================================================================
# Preset 1: AESA Hard-Kill Radar & Quad Countermeasure Pod v2 (APS Contemporary)
# =========================================================================

@dataclass(frozen=True)
class ApsHardkillRadarV2Config:
    """Dimensions and placement for a polished AESA radar & quad countermeasure pod v2."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 0.0, 1.0)
    base_plate_size: tuple[float, float, float] = (0.30, 0.26, 0.016)
    housing_size: tuple[float, float, float] = (0.24, 0.20, 0.17)
    mortar_count: int = 4
    mortar_caliber: float = 0.076
    mortar_length: float = 0.22
    mortar_elevation_deg: float = 35.0
    mortar_azimuth_spread_deg: float = 16.0
    gimbal_height: float = 0.080
    tilt_degrees: float = 18.0
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.base_plate_size, *self.housing_size,
                  self.mortar_caliber, self.mortar_length, self.mortar_elevation_deg,
                  self.mortar_azimuth_spread_deg, self.gimbal_height, self.tilt_degrees,
                  self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("ApsHardkillRadarV2Config parameters must be finite")
        if min(*self.base_plate_size, *self.housing_size, self.mortar_caliber,
               self.mortar_length, self.gimbal_height, self.fastener_radius) <= 0.0:
            raise ValueError("ApsHardkillRadarV2Config physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("ApsHardkillRadarV2Config normal must not be zero")
        if self.mortar_count < 2:
            raise ValueError("mortar_count must be at least 2")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_aps_hardkill_radar_v2_preset(
    *,
    prefix: str,
    config: ApsHardkillRadarV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished AESA radar & quad countermeasure pod v2."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    bw, bd, bh = config.base_plate_size
    hw, hd, hh = config.housing_size

    # 1. Base Mounting Plate with 4 Counterbore Fasteners
    base_plate = rmk.make_box(
        f"{prefix}_BasePlate_v2",
        config.base_plate_size,
        _transform_world(config.center, frame, (0.0, 0.0, bh * 0.5)),
        hardware_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(base_plate)

    fx = bw * 0.40
    fy = bd * 0.40
    for x_sign in (-1.0, 1.0):
        for y_sign in (-1.0, 1.0):
            bolt = rmk.make_cylinder(
                f"{prefix}_MountBolt_{'L' if x_sign < 0 else 'R'}_{'R' if y_sign < 0 else 'F'}",
                radius=config.fastener_radius,
                depth=0.010,
                location=_transform_world(config.center, frame, (x_sign * fx, y_sign * fy, bh + 0.003)),
                material=hardware_material,
                rotation=rotation,
                vertices=8,
            )
            created.append(bolt)

    # 2. Stepped Turntable with Traverse Ring Gear & Grounding Braid
    turntable_r = min(bw, bd) * 0.42
    turntable = rmk.make_cylinder(
        f"{prefix}_AzimuthTurntable_v2",
        radius=turntable_r,
        depth=0.024,
        location=_transform_world(config.center, frame, (0.0, 0.0, bh + 0.012)),
        material=hardware_material,
        rotation=rotation,
        vertices=16,
        bevel=0.002,
    )
    # Ring gear teeth on turntable perimeter
    gear_ring = rmk.make_cylinder(
        f"{prefix}_TraverseGearRing",
        radius=turntable_r + 0.006,
        depth=0.012,
        location=_transform_world(config.center, frame, (0.0, 0.0, bh + 0.012)),
        material=hardware_material,
        rotation=rotation,
        vertices=24,
    )
    ground_lug = rmk.make_box(
        f"{prefix}_GroundingLugBraid",
        (0.018, 0.024, 0.014),
        _transform_world(config.center, frame, (-turntable_r * 0.90, -turntable_r * 0.35, bh + 0.012)),
        hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    created.extend((turntable, gear_ring, ground_lug))

    # 3. Yoke and Hydraulic Elevation Actuator Cylinder
    yoke_z = bh + 0.024
    yoke_base = rmk.make_box(
        f"{prefix}_YokeCrossBeam_v2",
        (hw * 1.08, hd * 0.42, 0.026),
        _transform_world(config.center, frame, (0.0, 0.0, yoke_z + 0.013)),
        hardware_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(yoke_base)

    yoke_arm_w = 0.026
    yoke_arm_h = config.gimbal_height
    trunnion_z = yoke_z + yoke_arm_h
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        arm = rmk.make_box(
            f"{prefix}_YokeArm_{side}_v2",
            (yoke_arm_w, hd * 0.38, yoke_arm_h),
            _transform_world(config.center, frame, (sx * (hw * 0.5 + yoke_arm_w * 0.5), 0.0, yoke_z + yoke_arm_h * 0.5)),
            hardware_material,
            bevel=0.004,
            rotation=rotation,
        )
        cap = rmk.make_cylinder(
            f"{prefix}_TrunnionCap_{side}_v2",
            radius=0.020,
            depth=0.012,
            location=_transform_world(config.center, frame, (sx * (hw * 0.5 + yoke_arm_w + 0.006), 0.0, trunnion_z)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=12,
            bevel=0.001,
        )
        created.extend((arm, cap))

    # Hydraulic Elevation Actuator (connects yoke base to housing underside)
    hyd_body = rmk.make_cylinder(
        f"{prefix}_HydraulicElevationCylinder",
        radius=0.010,
        depth=0.045,
        location=_transform_world(config.center, frame, (0.0, -hd * 0.30, yoke_z + 0.030)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(35.0))).to_euler(),
        vertices=10,
    )
    hyd_rod = rmk.make_cylinder(
        f"{prefix}_HydraulicElevationPistonRod",
        radius=0.005,
        depth=0.035,
        location=_transform_world(config.center, frame, (0.0, -hd * 0.20, yoke_z + 0.052)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(35.0))).to_euler(),
        vertices=8,
    )
    created.extend((hyd_body, hyd_rod))

    # 4. AESA Radar Housing with Bezel Gasket & Micro-Emitter Subarrays
    tilt_quat = frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(config.tilt_degrees))
    tilt_rot = tilt_quat.to_euler()
    housing_pos = _transform_world(config.center, frame, (0.0, 0.0, trunnion_z))

    housing = rmk.make_box(
        f"{prefix}_RadarHousing_v2",
        config.housing_size,
        housing_pos,
        armor_material,
        bevel=0.012,
        rotation=tilt_rot,
        bevel_segments=1,
        smooth=False,
    )
    created.append(housing)

    # Perimeter silicone weather-seal gasket around radome
    gasket = rmk.make_box(
        f"{prefix}_RadomeGasket_v2",
        (hw * 0.94, 0.008, hh * 0.94),
        Vector(housing_pos) + tilt_quat @ Vector((0.0, hd * 0.5 + 0.003, 0.0)),
        hardware_material,
        bevel=0.001,
        rotation=tilt_rot,
    )
    created.append(gasket)

    # 4 Quadrant Micro-Emitter Array Plates (raised phased elements)
    qw = (hw * 0.40)
    qh = (hh * 0.40)
    for qx, qside in ((-1.0, "L"), (1.0, "R")):
        for qz, qvert in ((-1.0, "B"), (1.0, "T")):
            q_plate = rmk.make_box(
                f"{prefix}_AesaSubArray_{qside}_{qvert}",
                (qw * 0.92, 0.006, qh * 0.92),
                Vector(housing_pos) + tilt_quat @ Vector((qx * (qw * 0.52), hd * 0.5 + 0.007, qz * (qh * 0.52))),
                accent_material or hardware_material,
                bevel=0.001,
                rotation=tilt_rot,
            )
            created.append(q_plate)

    # Rear Finned Heat Sink & Lifting Eye
    heatsink = rmk.make_box(
        f"{prefix}_RearHeatSink_v2",
        (hw * 0.88, 0.024, hh * 0.85),
        Vector(housing_pos) + tilt_quat @ Vector((0.0, -hd * 0.5 - 0.012, 0.0)),
        hardware_material,
        bevel=0.002,
        rotation=tilt_rot,
    )
    lifting_eye = rmk.make_torus(
        f"{prefix}_RadomeLiftingEye",
        Vector(housing_pos) + tilt_quat @ Vector((0.0, 0.0, hh * 0.5 + 0.020)),
        major_radius=0.016,
        minor_radius=0.004,
        material=hardware_material,
        rotation=tilt_rot,
    )
    conduit_gland = rmk.make_cylinder(
        f"{prefix}_ArmoredConduitGland_v2",
        radius=0.012,
        depth=0.035,
        location=Vector(housing_pos) + tilt_quat @ Vector((-hw * 0.35, -hd * 0.45, -hh * 0.45)),
        material=hardware_material,
        rotation=tilt_rot,
        vertices=8,
    )
    created.extend((heatsink, lifting_eye, conduit_gland))

    # 5. Quad Countermeasure Cluster (2x2) with Blast Deflector Shield
    mortar_rad = config.mortar_caliber * 0.5
    elev_rad = math.radians(config.mortar_elevation_deg)
    yaw_spread_rad = math.radians(config.mortar_azimuth_spread_deg)

    # Curved Blast Deflector Plate between mortar cluster and radome
    blast_shield = rmk.make_box(
        f"{prefix}_BackblastDeflectorPlate",
        (0.010, hd * 0.85, hh * 0.95),
        _transform_world(config.center, frame, (hw * 0.5 + 0.040, 0.0, trunnion_z)),
        armor_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(blast_shield)

    # 4 Launch Tubes in 2x2 arrangement on starboard side
    tube_dx = (hw * 0.5 + 0.080)
    for row_idx, rz_off in enumerate((-0.045, 0.045), 1):
        for col_idx, cy_off in enumerate((-0.045, 0.045), 1):
            m_quat = frame @ Quaternion(Vector((0.0, 0.0, 1.0)), -yaw_spread_rad * (1 if col_idx == 2 else -1)) @ \
                             Quaternion(Vector((1.0, 0.0, 0.0)), -elev_rad)
            m_pos = _transform_world(config.center, frame, (tube_dx + (col_idx - 1.5) * 0.055, cy_off, trunnion_z + rz_off))

            tube = rmk.make_cylinder(
                f"{prefix}_MortarTube_{row_idx}_{col_idx}_v2",
                radius=mortar_rad,
                depth=config.mortar_length,
                location=m_pos,
                material=hardware_material,
                rotation=(m_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=14,
                bevel=0.002,
            )
            created.append(tube)

            # Bore cutter
            bore = rmk.make_cylinder(
                f"{prefix}_BoreCutter_{row_idx}_{col_idx}",
                radius=mortar_rad * 0.78,
                depth=0.080,
                location=Vector(m_pos) + m_quat @ Vector((0.0, config.mortar_length * 0.5 - 0.038, 0.0)),
                material=None,
                rotation=(m_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=12,
            )
            _apply_hardflow_or_builtin_boolean(tube, bore, hardflow_boolean_apply)

            # Breech ring & ignition harness bus plug
            breech = rmk.make_cylinder(
                f"{prefix}_MortarBreech_{row_idx}_{col_idx}",
                radius=mortar_rad * 1.18,
                depth=0.024,
                location=Vector(m_pos) - m_quat @ Vector((0.0, config.mortar_length * 0.5 - 0.010, 0.0)),
                material=hardware_material,
                rotation=(m_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=12,
            )
            created.append(breech)

    _apply_hardflow_or_builtin_bevel(housing, hardflow_bevel, bevel_width=0.004)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = APS_HARDKILL_RADAR_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "APS"
    return created


# =========================================================================
# Preset 2: Tactical Jerrycan Skid Basket v2 (FuelDrums Contemporary)
# =========================================================================

@dataclass(frozen=True)
class FuelAuxJerrycanRackV2Config:
    """Dimensions and placement for a polished tactical jerrycan basket v2."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    basket_size: tuple[float, float, float] = (0.60, 0.34, 0.40)
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
            raise ValueError("FuelAuxJerrycanRackV2Config dimensions must be finite")
        if min(*self.basket_size, *self.can_dimensions, self.tube_radius,
               self.stiffener_groove_depth, self.stiffener_groove_width, self.strap_width) <= 0.0:
            raise ValueError("FuelAuxJerrycanRackV2Config dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("FuelAuxJerrycanRackV2Config normal must not be zero")
        bw, bd, bh = self.basket_size
        cw, cd, ch = self.can_dimensions
        if cw * 2.0 > bw or cd > bd or ch > bh + 0.10:
            raise ValueError("Cans do not fit within basket footprint")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_fuel_aux_jerrycan_rack_v2_preset(
    *,
    prefix: str,
    config: FuelAuxJerrycanRackV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished tactical jerrycan basket v2 with vibration pucks and hinged gate."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _local_y_outward_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    bw, bd, bh = config.basket_size
    cw, cd, ch = config.can_dimensions
    tr = config.tube_radius

    # 1. 4 Hull Mounting Feet with Neoprene Rubber Vibration Isolation Pucks
    foot_x = bw * 0.44
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        foot = rmk.make_box(
            f"{prefix}_MountFoot_{side}_v2",
            (0.045, 0.020, 0.050),
            _transform_world(config.center, frame, (sx * foot_x, 0.010, bh * 0.5)),
            hardware_material,
            bevel=0.003,
            rotation=rotation,
        )
        puck = rmk.make_box(
            f"{prefix}_VibrationPuck_{side}_v2",
            (0.048, 0.008, 0.052),
            _transform_world(config.center, frame, (sx * foot_x, -0.004, bh * 0.5)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((foot, puck))

    # Upper diagonal sway brace struts
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        sway = rmk.make_cylinder(
            f"{prefix}_SwayBrace_{side}_v2",
            radius=0.007,
            depth=bd * 0.95,
            location=_transform_world(config.center, frame, (sx * (bw * 0.46), bd * 0.45, bh * 0.70)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(35.0))).to_euler(),
            vertices=8,
        )
        created.append(sway)

    # 2. Tubular Basket Frame with Perforated Slotted Floor Grid
    floor_grid = rmk.make_box(
        f"{prefix}_BasketFloorGrid_v2",
        (bw * 0.94, bd * 0.92, 0.006),
        _transform_world(config.center, frame, (0.0, bd * 0.5, 0.003)),
        hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    created.append(floor_grid)

    # Perimeter tubular rails (bottom rails, top rails, vertical posts)
    for z_pos, z_tag in ((0.012, "Bottom"), (bh * 0.50, "Mid"), (bh, "Top")):
        front_rail = rmk.make_cylinder(
            f"{prefix}_Rail_{z_tag}_Front_v2",
            radius=tr,
            depth=bw,
            location=_transform_world(config.center, frame, (0.0, bd, z_pos)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
        )
        back_rail = rmk.make_cylinder(
            f"{prefix}_Rail_{z_tag}_Back_v2",
            radius=tr,
            depth=bw,
            location=_transform_world(config.center, frame, (0.0, 0.020, z_pos)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
        )
        created.extend((front_rail, back_rail))

    for sx, side in ((-1.0, "L"), (1.0, "R")):
        post_f = rmk.make_cylinder(
            f"{prefix}_Post_{side}_Front_v2",
            radius=tr,
            depth=bh,
            location=_transform_world(config.center, frame, (sx * (bw * 0.48), bd, bh * 0.5)),
            material=hardware_material,
            rotation=rotation,
            vertices=10,
        )
        post_b = rmk.make_cylinder(
            f"{prefix}_Post_{side}_Back_v2",
            radius=tr,
            depth=bh,
            location=_transform_world(config.center, frame, (sx * (bw * 0.48), 0.020, bh * 0.5)),
            material=hardware_material,
            rotation=rotation,
            vertices=10,
        )
        side_rail = rmk.make_cylinder(
            f"{prefix}_SideRail_{side}_Top_v2",
            radius=tr,
            depth=bd - 0.020,
            location=_transform_world(config.center, frame, (sx * (bw * 0.48), bd * 0.5 + 0.010, bh)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
        )
        created.extend((post_f, post_b, side_rail))

    # 3. Two Refined Jerrycans with Stamped X-Embossments & Crimp Weld Flanges
    for can_idx, (sx, can_tag) in enumerate(((-1.0, "L"), (1.0, "R")), 1):
        can_pos = _transform_world(config.center, frame, (sx * (cw * 0.54), bd * 0.5 + 0.005, ch * 0.5 + 0.008))
        body = rmk.make_box(
            f"{prefix}_JerrycanBody_{can_tag}_v2",
            (cw, cd, ch),
            can_pos,
            armor_material,
            bevel=0.010,
            rotation=rotation,
            bevel_segments=1,
            smooth=False,
        )
        # Perimeter stamped crimp weld flange (2-halves welded pressed joint)
        flange = rmk.make_box(
            f"{prefix}_WeldFlange_{can_tag}_v2",
            (cw + 0.008, 0.005, ch + 0.006),
            can_pos,
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        # 3-bar carry handle
        handle = rmk.make_box(
            f"{prefix}_CarryHandle_{can_tag}_v2",
            (cw * 0.72, cd * 0.65, 0.038),
            Vector(can_pos) + frame @ Vector((0.0, 0.0, ch * 0.5 + 0.019)),
            hardware_material,
            bevel=0.003,
            rotation=rotation,
        )
        # Cam-lock filler neck & secondary vent screw cap
        spout = rmk.make_cylinder(
            f"{prefix}_FillerNeck_{can_tag}_v2",
            radius=0.018,
            depth=0.032,
            location=Vector(can_pos) + frame @ Vector((cw * 0.28, -cd * 0.20, ch * 0.5 + 0.024)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(22.0))).to_euler(),
            vertices=12,
            bevel=0.002,
        )
        vent_cap = rmk.make_cylinder(
            f"{prefix}_VentCap_{can_tag}_v2",
            radius=0.008,
            depth=0.016,
            location=Vector(can_pos) + frame @ Vector((-cw * 0.28, -cd * 0.20, ch * 0.5 + 0.016)),
            material=hardware_material,
            rotation=rotation,
            vertices=8,
        )
        # Stamped fuel ID tag (flanged brass label)
        id_tag = rmk.make_box(
            f"{prefix}_FuelIdTag_{can_tag}",
            (0.040, 0.004, 0.025),
            Vector(can_pos) + frame @ Vector((0.0, cd * 0.5 + 0.003, ch * 0.25)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((body, flange, handle, spout, vent_cap, id_tag))

    # 4. Front Hinged Retaining Crossbar Gate & Tie-Down Straps with Ratchets
    gate = rmk.make_box(
        f"{prefix}_RetainingGateBar_v2",
        (bw * 0.90, 0.016, 0.032),
        _transform_world(config.center, frame, (0.0, bd + 0.012, bh * 0.65)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    star_knob = rmk.make_cylinder(
        f"{prefix}_GateClampStarKnob_v2",
        radius=0.016,
        depth=0.018,
        location=_transform_world(config.center, frame, (bw * 0.44, bd + 0.022, bh * 0.65)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=6,
    )
    ground_clip = rmk.make_box(
        f"{prefix}_GroundingWireClip",
        (0.014, 0.035, 0.020),
        _transform_world(config.center, frame, (-bw * 0.48 - 0.008, bd * 0.35, bh * 0.45)),
        accent_material or hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    created.extend((gate, star_knob, ground_clip))

    for sx in (-cw * 0.54, cw * 0.54):
        strap = rmk.make_box(
            f"{prefix}_HoldDownStrap_v2",
            (config.strap_width, cd * 1.05, 0.004),
            _transform_world(config.center, frame, (sx, bd * 0.5, ch + 0.008)),
            hardware_material,
            rotation=rotation,
        )
        ratchet = rmk.make_box(
            f"{prefix}_StrapRatchetBuckle_v2",
            (config.strap_width * 1.25, 0.030, 0.022),
            _transform_world(config.center, frame, (sx, bd + 0.006, ch * 0.85)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((strap, ratchet))

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = FUEL_AUX_JERRYCAN_RACK_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "FuelDrums"
    return created


# =========================================================================
# Preset 3: Standoff Knife-Edge Slat Armor Screen v2 (ERA Contemporary)
# =========================================================================

@dataclass(frozen=True)
class SlatArmorScreenV2Config:
    """Dimensions and placement for a polished standoff slat armor screen v2."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 1.0, 0.0)
    screen_size: tuple[float, float] = (0.68, 0.46)
    standoff_distance: float = 0.24
    bar_count: int = 8
    bar_thickness: float = 0.008
    bar_depth: float = 0.026
    frame_bar_size: float = 0.022
    mount_foot_size: float = 0.068
    fastener_radius: float = 0.005
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.screen_size, self.standoff_distance,
                  self.bar_thickness, self.bar_depth, self.frame_bar_size,
                  self.mount_foot_size, self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("SlatArmorScreenV2Config dimensions must be finite")
        if min(*self.screen_size, self.standoff_distance, self.bar_thickness,
               self.bar_depth, self.frame_bar_size, self.mount_foot_size, self.fastener_radius) <= 0.0:
            raise ValueError("SlatArmorScreenV2Config dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("SlatArmorScreenV2Config normal must not be zero")
        if self.bar_count < 2:
            raise ValueError("bar_count must be at least 2")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_slat_armor_screen_v2_preset(
    *,
    prefix: str,
    config: SlatArmorScreenV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished standoff slat armor screen v2 with knife-edge bars and maintenance hinges."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _local_y_outward_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    sw, sh = config.screen_size
    fbs = config.frame_bar_size
    standoff_y = config.standoff_distance

    # 1. 4 Standoff Mounting Pads with Neoprene Vibration Bumpers
    mfs = config.mount_foot_size
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        for sz, vert in ((-1.0, "B"), (1.0, "T")):
            pad_pos = _transform_world(config.center, frame, (sx * (sw * 0.40), 0.005, sz * (sh * 0.38)))
            pad = rmk.make_box(
                f"{prefix}_MountPad_{side}_{vert}_v2",
                (mfs, 0.010, mfs),
                pad_pos,
                hardware_material,
                bevel=0.002,
                rotation=rotation,
            )
            # Neoprene shock absorption bumper on rear
            bumper = rmk.make_box(
                f"{prefix}_RubberBumper_{side}_{vert}",
                (mfs * 0.60, 0.014, mfs * 0.60),
                Vector(pad_pos) + frame @ Vector((0.0, -0.007, 0.0)),
                hardware_material,
                bevel=0.001,
                rotation=rotation,
            )
            created.extend((pad, bumper))

    # 2. Articulated Linkage Hinge Arms & Diagonal Sway Struts
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        for sz, vert in ((-1.0, "B"), (1.0, "T")):
            arm = rmk.make_box(
                f"{prefix}_ArticulatedStandoffArm_{side}_{vert}_v2",
                (0.020, standoff_y * 0.85, 0.028),
                _transform_world(config.center, frame, (sx * (sw * 0.40), standoff_y * 0.48, sz * (sh * 0.38))),
                hardware_material,
                bevel=0.002,
                rotation=rotation,
            )
            # Pivot knuckle with grease fitting
            knuckle = rmk.make_cylinder(
                f"{prefix}_HingeKnuckle_{side}_{vert}",
                radius=0.014,
                depth=0.024,
                location=_transform_world(config.center, frame, (sx * (sw * 0.40), standoff_y * 0.88, sz * (sh * 0.38))),
                material=hardware_material,
                rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=10,
                bevel=0.001,
            )
            # Captive safety lynchpin with pull ring
            ring = rmk.make_torus(
                f"{prefix}_LynchpinRing_{side}_{vert}",
                _transform_world(config.center, frame, (sx * (sw * 0.40 + 0.016), standoff_y * 0.88, sz * (sh * 0.38))),
                major_radius=0.010,
                minor_radius=0.002,
                material=accent_material or hardware_material,
                rotation=rotation,
            )
            created.extend((arm, knuckle, ring))

    # Heavy diagonal locking sway struts
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        sway = rmk.make_cylinder(
            f"{prefix}_LockingSwayStrut_{side}_v2",
            radius=0.008,
            depth=standoff_y * 1.15,
            location=_transform_world(config.center, frame, (sx * (sw * 0.28), standoff_y * 0.50, 0.0)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(38.0))).to_euler(),
            vertices=8,
        )
        created.append(sway)

    # 3. Outer Box Frame with Corner Gusset Braces & Coupling Tabs
    frame_y = standoff_y
    top_bar = rmk.make_box(
        f"{prefix}_FrameTop_v2",
        (sw, fbs, fbs),
        _transform_world(config.center, frame, (0.0, frame_y, sh * 0.5)),
        armor_material,
        bevel=0.003,
        rotation=rotation,
    )
    bot_bar = rmk.make_box(
        f"{prefix}_FrameBottom_v2",
        (sw, fbs, fbs),
        _transform_world(config.center, frame, (0.0, frame_y, -sh * 0.5)),
        armor_material,
        bevel=0.003,
        rotation=rotation,
    )
    side_l = rmk.make_box(
        f"{prefix}_FrameSide_L_v2",
        (fbs, fbs, sh),
        _transform_world(config.center, frame, (-sw * 0.5 + fbs * 0.5, frame_y, 0.0)),
        armor_material,
        bevel=0.003,
        rotation=rotation,
    )
    side_r = rmk.make_box(
        f"{prefix}_FrameSide_R_v2",
        (fbs, fbs, sh),
        _transform_world(config.center, frame, (sw * 0.5 - fbs * 0.5, frame_y, 0.0)),
        armor_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.extend((top_bar, bot_bar, side_l, side_r))

    # Corner triangular reinforcement gussets
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        for sz, vert in ((-1.0, "B"), (1.0, "T")):
            gusset = rmk.make_box(
                f"{prefix}_CornerGusset_{side}_{vert}_v2",
                (0.045, 0.006, 0.045),
                _transform_world(config.center, frame, (sx * (sw * 0.5 - fbs * 1.5), frame_y, sz * (sh * 0.5 - fbs * 1.5))),
                hardware_material,
                bevel=0.001,
                rotation=rotation,
            )
            created.append(gusset)

    # 4. Vertical Notched Uprights (Center and Quarter-Span Stiffeners)
    for x_off, tag in ((0.0, "Center"), (-sw * 0.25, "Quarter_L"), (sw * 0.25, "Quarter_R")):
        upright = rmk.make_box(
            f"{prefix}_Upright_{tag}_v2",
            (0.012, config.bar_depth * 1.15, sh * 0.96),
            _transform_world(config.center, frame, (x_off, frame_y, 0.0)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.append(upright)

    # 5. Knife-Edge Deflection Slat Bars (45° projectile deflection wedge profile)
    bar_count = config.bar_count
    bar_step = (sh - fbs * 2.0) / (bar_count + 1)
    for b_idx in range(1, bar_count + 1):
        bz = -sh * 0.5 + fbs + b_idx * bar_step
        # Bar with chamfered leading edge (knife edge)
        bar = rmk.make_box(
            f"{prefix}_KnifeSlatBar_{b_idx}_v2",
            (sw * 0.98, config.bar_depth, config.bar_thickness),
            _transform_world(config.center, frame, (0.0, frame_y, bz)),
            armor_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.append(bar)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = SLAT_ARMOR_SCREEN_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "ERA"
    return created


# =========================================================================
# Preset 4: Modular Anti-Tank Mine Dispenser Rack v2 (FuelDrums Contemporary)
# =========================================================================

@dataclass(frozen=True)
class OrdnanceMineDispenserV2Config:
    """Dimensions and placement for a polished modular mine dispenser rack v2."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    rack_size: tuple[float, float, float] = (0.66, 0.40, 0.46)
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
            raise ValueError("OrdnanceMineDispenserV2Config dimensions must be finite")
        if min(*self.rack_size, self.canister_caliber, self.canister_length) <= 0.0:
            raise ValueError("OrdnanceMineDispenserV2Config dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("OrdnanceMineDispenserV2Config normal must not be zero")
        if self.cassette_count < 1 or self.tubes_per_cassette < 1:
            raise ValueError("cassette_count and tubes_per_cassette must be at least 1")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_ordnance_mine_dispenser_v2_preset(
    *,
    prefix: str,
    config: OrdnanceMineDispenserV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished modular mine dispenser rack v2 with backblast shield and quick-release clamps."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _local_y_outward_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    rw, rd, rh = config.rack_size

    # 1. Base Skid Plate with Sloped Front Deflector Apron & Runner Skids
    base_plate = rmk.make_box(
        f"{prefix}_MountBasePlate_v2",
        (rw, rd, 0.016),
        _transform_world(config.center, frame, (0.0, rd * 0.5, 0.008)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    apron = rmk.make_box(
        f"{prefix}_DeflectorApron_v2",
        (rw * 0.96, 0.045, 0.010),
        _transform_world(config.center, frame, (0.0, 0.020, 0.018)),
        armor_material,
        bevel=0.002,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(35.0))).to_euler(),
    )
    created.extend((base_plate, apron))

    # Dual bottom runner skids
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        skid = rmk.make_cylinder(
            f"{prefix}_RunnerSkid_{side}_v2",
            radius=0.012,
            depth=rd * 0.94,
            location=_transform_world(config.center, frame, (sx * (rw * 0.44), rd * 0.5, -0.010)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
        )
        created.append(skid)

    # 2. Armored Backblast Deflector Shield underneath launch cassettes
    backblast_shield = rmk.make_box(
        f"{prefix}_BackblastShield_v2",
        (rw * 0.90, rd * 0.70, 0.012),
        _transform_world(config.center, frame, (0.0, rd * 0.45, rh * 0.18)),
        armor_material,
        bevel=0.003,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(15.0))).to_euler(),
    )
    created.append(backblast_shield)

    # 3. Welded A-Frame Uprights with Central Axle & Elevation Quadrant Sector
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        strut = rmk.make_box(
            f"{prefix}_AStrut_{side}_v2",
            (0.026, 0.026, rh * 0.88),
            _transform_world(config.center, frame, (sx * (rw * 0.46), rd * 0.45, rh * 0.44)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        gusset = rmk.make_box(
            f"{prefix}_StrutGusset_{side}_v2",
            (0.010, 0.070, 0.070),
            _transform_world(config.center, frame, (sx * (rw * 0.46), rd * 0.45, 0.045)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((strut, gusset))

    pivot_axle = rmk.make_cylinder(
        f"{prefix}_PivotAxle_v2",
        radius=0.016,
        depth=rw * 0.96,
        location=_transform_world(config.center, frame, (0.0, rd * 0.45, rh * 0.48)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=14,
    )
    # Graduated elevation quadrant sector arc
    sector_arc = rmk.make_cylinder(
        f"{prefix}_ElevationQuadrantArc",
        radius=0.065,
        depth=0.012,
        location=_transform_world(config.center, frame, (rw * 0.42, rd * 0.45, rh * 0.48)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=12,
    )
    lock_wheel = rmk.make_cylinder(
        f"{prefix}_ElevationLockHandwheel",
        radius=0.024,
        depth=0.014,
        location=_transform_world(config.center, frame, (rw * 0.44, rd * 0.48, rh * 0.48)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=8,
    )
    created.extend((pivot_axle, sector_arc, lock_wheel))

    # 4. Modular Dispenser Cassettes (x4) with Quick-Release Clamps & Muzzle Caps
    cassette_w = (rw * 0.88) / config.cassette_count
    cassette_quat = frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(-config.launch_elevation_deg))
    cassette_rot = cassette_quat.to_euler()
    can_rad = config.canister_caliber * 0.5

    for c_idx in range(config.cassette_count):
        cx = -rw * 0.44 + (c_idx + 0.5) * cassette_w
        c_center = _transform_world(config.center, frame, (cx, rd * 0.48, rh * 0.54))

        cassette_box = rmk.make_box(
            f"{prefix}_CassetteBox_{c_idx + 1}_v2",
            (cassette_w * 0.88, config.canister_length * 0.85, config.canister_caliber * config.tubes_per_cassette * 1.15),
            c_center,
            armor_material,
            bevel=0.004,
            rotation=cassette_rot,
            bevel_segments=1,
            smooth=False,
        )
        # Quick-release toggle clamp with yellow safety pin on top of cassette
        clamp = rmk.make_box(
            f"{prefix}_ToggleClamp_{c_idx + 1}",
            (0.018, 0.040, 0.014),
            Vector(c_center) + cassette_quat @ Vector((0.0, 0.0, config.canister_caliber * config.tubes_per_cassette * 0.60)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=cassette_rot,
        )
        created.extend((cassette_box, clamp))

        # Canister tubes inside cassette
        for t_idx in range(config.tubes_per_cassette):
            tz_off = (t_idx - (config.tubes_per_cassette - 1) * 0.5) * (config.canister_caliber * 1.12)
            tube_pos = Vector(c_center) + cassette_quat @ Vector((0.0, 0.0, tz_off))

            tube = rmk.make_cylinder(
                f"{prefix}_CanisterTube_{c_idx + 1}_{t_idx + 1}_v2",
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
                f"{prefix}_BoreCut_{c_idx + 1}_{t_idx + 1}",
                radius=can_rad * 0.80,
                depth=0.060,
                location=Vector(tube_pos) + cassette_quat @ Vector((0.0, config.canister_length * 0.5 - 0.025, 0.0)),
                material=None,
                rotation=(cassette_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=10,
            )
            _apply_hardflow_or_builtin_boolean(tube, bore, hardflow_boolean_apply)

            # Rubber blowout burst-cap over muzzle
            burst_cap = rmk.make_cylinder(
                f"{prefix}_BurstCap_{c_idx + 1}_{t_idx + 1}",
                radius=can_rad * 1.05,
                depth=0.008,
                location=Vector(tube_pos) + cassette_quat @ Vector((0.0, config.canister_length * 0.5 + 0.003, 0.0)),
                material=hardware_material,
                rotation=(cassette_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=10,
                bevel=0.001,
            )
            created.append(burst_cap)

            # Breech firing solenoid plug
            solenoid = rmk.make_cylinder(
                f"{prefix}_FiringSolenoid_{c_idx + 1}_{t_idx + 1}_v2",
                radius=can_rad * 0.40,
                depth=0.016,
                location=Vector(tube_pos) - cassette_quat @ Vector((0.0, config.canister_length * 0.5 + 0.006, 0.0)),
                material=accent_material or hardware_material,
                rotation=(cassette_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=8,
            )
            created.append(solenoid)

    # 5. Junction Box with Status Indicator & Emergency Jettison Piston
    control_box = rmk.make_box(
        f"{prefix}_ControlJunctionBox_v2",
        (0.085, 0.050, 0.070),
        _transform_world(config.center, frame, (-rw * 0.38, 0.035, 0.050)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    status_led = rmk.make_cylinder(
        f"{prefix}_JunctionStatusLED",
        radius=0.004,
        depth=0.006,
        location=_transform_world(config.center, frame, (-rw * 0.38, 0.062, 0.070)),
        material=accent_material or hardware_material,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=6,
    )
    jettison_lever = rmk.make_box(
        f"{prefix}_JettisonHandle_v2",
        (0.016, 0.040, 0.075),
        _transform_world(config.center, frame, (rw * 0.48 + 0.012, rd * 0.45, rh * 0.52)),
        accent_material or hardware_material,
        bevel=0.001,
        rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(22.0))).to_euler(),
    )
    jettison_cylinder = rmk.make_cylinder(
        f"{prefix}_PneumaticJettisonRam",
        radius=0.012,
        depth=0.055,
        location=_transform_world(config.center, frame, (rw * 0.48 + 0.012, rd * 0.38, rh * 0.40)),
        material=hardware_material,
        rotation=rotation,
        vertices=10,
    )
    created.extend((control_box, status_led, jettison_lever, jettison_cylinder))

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = ORDNANCE_MINE_DISPENSER_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "FuelDrums"
    return created


# =========================================================================
# Preset 5: Heavy Armored Louvered Radiator Sponson v2 (Sponsons Contemporary)
# =========================================================================

@dataclass(frozen=True)
class ThermalArmoredRadiatorV2Config:
    """Flank radiator dimensions; local +X is outward, +Y forward, and +Z vertical."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (1.0, 0.0, 0.0)
    cowl_size: tuple[float, float, float] = (0.36, 0.62, 0.42)
    louver_count: int = 6
    manifold_pipe_radius: float = 0.020
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.cowl_size, self.manifold_pipe_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("ThermalArmoredRadiatorV2Config dimensions must be finite")
        if min(*self.cowl_size, self.manifold_pipe_radius) <= 0.0:
            raise ValueError("ThermalArmoredRadiatorV2Config dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("ThermalArmoredRadiatorV2Config normal must not be zero")
        if self.louver_count < 2:
            raise ValueError("louver_count must be at least 2")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_thermal_armored_radiator_v2_preset(
    *,
    prefix: str,
    config: ThermalArmoredRadiatorV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished heavy armored radiator sponson pack v2 with auxiliary fan cowls and flanged valves."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _side_mount_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    cw, cd, ch = config.cowl_size

    # 1. Armored Ballistic Cowl Shell & Deflector Plates with Perimeter Rubber Seal
    cowl_center = _transform_world(config.center, frame, (0.0, 0.0, ch * 0.5))
    cowl = rmk.make_box(
        f"{prefix}_CowlShell_v2",
        config.cowl_size,
        cowl_center,
        armor_material,
        bevel=0.016,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(cowl)

    # Upper and Lower Sloped Ballistic Deflectors with Crane Lifting Lugs
    deflector_top = rmk.make_box(
        f"{prefix}_CowlDeflector_Top_v2",
        (cw * 0.94, cd * 0.88, 0.026),
        _transform_world(config.center, frame, (0.0, 0.0, ch + 0.013)),
        armor_material,
        bevel=0.006,
        rotation=rotation,
    )
    deflector_bottom = rmk.make_box(
        f"{prefix}_CowlDeflector_Bottom_v2",
        (cw * 0.94, cd * 0.88, 0.026),
        _transform_world(config.center, frame, (0.0, 0.0, -0.013)),
        armor_material,
        bevel=0.006,
        rotation=rotation,
    )
    created.extend((deflector_top, deflector_bottom))

    for tag_z, lz in (("Top", ch + 0.030), ("Bottom", -0.030)):
        lug = rmk.make_box(
            f"{prefix}_CraneLug_{tag_z}_v2",
            (0.024, 0.050, 0.035),
            _transform_world(config.center, frame, (0.0, cd * 0.35, lz)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.append(lug)

    # 2. Radiator Matrix Cavity Cutout (+X face)
    cavity_w = cw * 0.52
    cavity_d = cd * 0.80
    cavity_h = ch * 0.74
    cavity_cutter = rmk.make_box(
        f"{prefix}_CavityCutter_v2",
        (cavity_w, cavity_d, cavity_h),
        _transform_world(config.center, frame, (cw * 0.5 - cavity_w * 0.5 + 0.005, 0.0, ch * 0.5)),
        None,
        rotation=rotation,
    )
    _apply_hardflow_or_builtin_boolean(cowl, cavity_cutter, hardflow_boolean_apply)

    # Internal Cooling Matrix Core inside cavity
    core = rmk.make_box(
        f"{prefix}_CoolingCore_v2",
        (0.014, cavity_d * 0.96, cavity_h * 0.96),
        _transform_world(config.center, frame, (cw * 0.5 - cavity_w * 0.82, 0.0, ch * 0.5)),
        hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    created.append(core)

    # 3. Dual Auxiliary Forced-Air Cooling Fan Nacelles behind core
    fan_r = cavity_h * 0.22
    for fy_idx, fy_off in enumerate((-cavity_d * 0.25, cavity_d * 0.25), 1):
        fan_cowl = rmk.make_cylinder(
            f"{prefix}_AuxFanCowl_{fy_idx}",
            radius=fan_r,
            depth=0.016,
            location=_transform_world(config.center, frame, (cw * 0.5 - cavity_w * 0.92, fy_off, ch * 0.5)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=16,
            bevel=0.001,
        )
        fan_hub = rmk.make_cylinder(
            f"{prefix}_FanHub_{fy_idx}",
            radius=fan_r * 0.35,
            depth=0.020,
            location=_transform_world(config.center, frame, (cw * 0.5 - cavity_w * 0.92, fy_off, ch * 0.5)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
        )
        created.extend((fan_cowl, fan_hub))

    # 4. Chevron Armor Louvers with Debris Screens
    louver_count = config.louver_count
    louver_step = cavity_h / (louver_count + 1)
    for l_idx in range(1, louver_count + 1):
        lz = (ch * 0.5 - cavity_h * 0.5) + l_idx * louver_step
        louver_quat = frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(30.0))
        louver = rmk.make_box(
            f"{prefix}_ChevronLouver_{l_idx}_v2",
            (0.048, cavity_d * 0.98, 0.009),
            _transform_world(config.center, frame, (cw * 0.5 - 0.015, 0.0, lz)),
            armor_material,
            bevel=0.001,
            rotation=louver_quat.to_euler(),
        )
        created.append(louver)

    # 5. Flanged Hydraulic Pipes with Emergency Quarter-Turn Ball Valves
    for pos_z, pipe_tag in ((ch * 0.85, "Supply"), (ch * 0.15, "Return")):
        pipe = rmk.make_cylinder(
            f"{prefix}_CoolantPipe_{pipe_tag}_v2",
            radius=config.manifold_pipe_radius,
            depth=cd * 0.94,
            location=_transform_world(config.center, frame, (-cw * 0.35, 0.0, pos_z)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=12,
        )
        # 4-bolt pipe flange
        flange = rmk.make_cylinder(
            f"{prefix}_PipeFlange_{pipe_tag}",
            radius=config.manifold_pipe_radius * 1.55,
            depth=0.012,
            location=_transform_world(config.center, frame, (-cw * 0.35, cd * 0.40, pos_z)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=12,
            bevel=0.001,
        )
        # Emergency shutoff quarter-turn ball valve handle
        valve_handle = rmk.make_box(
            f"{prefix}_BallValveHandle_{pipe_tag}",
            (0.012, 0.045, 0.010),
            _transform_world(config.center, frame, (-cw * 0.35 + 0.025, cd * 0.30, pos_z)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((pipe, flange, valve_handle))

    # 6. Flush Maintenance Access Hatch with Rotary Latches
    hatch_y = -cd * 0.5 - 0.003
    hatch = rmk.make_box(
        f"{prefix}_MaintenanceHatch_v2",
        (cw * 0.70, 0.010, ch * 0.50),
        _transform_world(config.center, frame, (0.0, hatch_y, ch * 0.50)),
        armor_material,
        bevel=0.002,
        rotation=rotation,
    )
    for hx in (-cw * 0.22, cw * 0.22):
        latch = rmk.make_cylinder(
            f"{prefix}_RotaryLatch",
            radius=0.008,
            depth=0.006,
            location=_transform_world(config.center, frame, (hx, hatch_y - 0.003, ch * 0.50)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=8,
        )
        created.append(latch)
    created.append(hatch)

    _apply_hardflow_or_builtin_bevel(cowl, hardflow_bevel, bevel_width=0.005)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = THERMAL_ARMORED_RADIATOR_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"
    return created


# =========================================================================
# Preset 6: Remote 30mm Point-Defense Autocannon Pod v2 (Sponsons Contemporary)
# =========================================================================

@dataclass(frozen=True)
class AutocannonRemoteSponsonV2Config:
    """Dimensions and placement for a polished remote 30mm autocannon pod v2."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (1.0, 0.0, 0.0)
    cradle_size: tuple[float, float, float] = (0.26, 0.34, 0.30)
    caliber: float = 0.038
    barrel_length: float = 0.50
    elevation_deg: float = 12.0
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.cradle_size, self.caliber,
                  self.barrel_length, self.elevation_deg, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("AutocannonRemoteSponsonV2Config dimensions must be finite")
        if min(*self.cradle_size, self.caliber, self.barrel_length) <= 0.0:
            raise ValueError("AutocannonRemoteSponsonV2Config dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("AutocannonRemoteSponsonV2Config normal must not be zero")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_autocannon_remote_sponson_v2_preset(
    *,
    prefix: str,
    config: AutocannonRemoteSponsonV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished remote 30mm autocannon pod v2 with mantlet shield and linkless feed chute."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _side_mount_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    cw, cd, ch = config.cradle_size

    # 1. Base Azimuth Turntable Boss with Traverse Ring Gear & Flange Bolts
    turntable_r = min(cd, ch) * 0.46
    turntable = rmk.make_cylinder(
        f"{prefix}_TurntableBoss_v2",
        radius=turntable_r,
        depth=0.022,
        location=_transform_world(config.center, frame, (0.011, 0.0, ch * 0.5)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=16,
        bevel=0.002,
    )
    gear_ring = rmk.make_cylinder(
        f"{prefix}_TraverseGearRing",
        radius=turntable_r + 0.005,
        depth=0.010,
        location=_transform_world(config.center, frame, (0.011, 0.0, ch * 0.5)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=24,
    )
    created.extend((turntable, gear_ring))

    # 8 Perimeter Flange Bolts
    for b_idx in range(8):
        b_ang = math.radians(b_idx * 45.0)
        by = math.cos(b_ang) * (turntable_r * 0.88)
        bz = math.sin(b_ang) * (turntable_r * 0.88)
        bolt = rmk.make_cylinder(
            f"{prefix}_FlangeBolt_{b_idx + 1}",
            radius=0.005,
            depth=0.010,
            location=_transform_world(config.center, frame, (0.022, by, ch * 0.5 + bz)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=8,
        )
        created.append(bolt)

    # 2. Elevation Cradle & Curved Ballistic Sponson Mantlet Shield
    cradle_center = _transform_world(config.center, frame, (0.022 + cw * 0.5, 0.0, ch * 0.5))
    cradle = rmk.make_box(
        f"{prefix}_Cradle_v2",
        config.cradle_size,
        cradle_center,
        armor_material,
        bevel=0.010,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(cradle)

    # Elevation Frame Tilt
    elev_quat = frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(config.elevation_deg))
    elev_rot = elev_quat.to_euler()

    # Curved Sponson Mantlet Shield attached to elevating mass
    mantlet = rmk.make_cylinder(
        f"{prefix}_CurvedBallisticMantlet",
        radius=ch * 0.48,
        depth=cw * 0.90,
        location=_transform_world(config.center, frame, (0.022 + cw * 0.5, 0.020, ch * 0.5)),
        material=armor_material,
        rotation=(elev_quat @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=16,
        bevel=0.003,
    )
    created.append(mantlet)

    # Heavy Trunnion Bearing Pillow Blocks
    for sy, side_tag in ((-1.0, "Rear"), (1.0, "Front")):
        cap = rmk.make_cylinder(
            f"{prefix}_TrunnionCap_{side_tag}_v2",
            radius=0.026,
            depth=0.014,
            location=_transform_world(config.center, frame, (0.022 + cw * 0.5, sy * (cd * 0.5 + 0.007), ch * 0.5)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=12,
            bevel=0.001,
        )
        created.append(cap)

    # Receiver Breech Block with Recoil Buffer Spring Housing
    breech_pos = _transform_world(config.center, frame, (0.022 + cw * 0.5, 0.04, ch * 0.5))
    breech = rmk.make_box(
        f"{prefix}_ReceiverBreech_v2",
        (cw * 0.55, 0.19, 0.13),
        breech_pos,
        hardware_material,
        bevel=0.003,
        rotation=elev_rot,
    )
    buffer_spring = rmk.make_cylinder(
        f"{prefix}_RecoilBufferHousing",
        radius=0.014,
        depth=0.080,
        location=Vector(breech_pos) - elev_quat @ Vector((0.0, 0.12, 0.0)),
        material=hardware_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=10,
    )
    created.extend((breech, buffer_spring))

    # 3. Fluted 30mm Autocannon Barrel, Slotted Muzzle Brake & Perforated Sleeve
    barrel_rad = config.caliber * 0.5
    barrel_center = Vector(breech_pos) + elev_quat @ Vector((0.0, 0.095 + config.barrel_length * 0.5, 0.0))
    barrel = rmk.make_cylinder(
        f"{prefix}_FlutedBarrel_v2",
        radius=barrel_rad,
        depth=config.barrel_length,
        location=barrel_center,
        material=hardware_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=16,
        bevel=0.0015,
    )
    created.append(barrel)

    # Bore Cutter
    bore_depth = 0.080
    bore = rmk.make_cylinder(
        f"{prefix}_BoreCut",
        radius=barrel_rad * 0.76,
        depth=bore_depth,
        location=Vector(barrel_center) + elev_quat @ Vector((0.0, config.barrel_length * 0.5 - bore_depth * 0.5 + 0.001, 0.0)),
        material=None,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=12,
    )
    _apply_hardflow_or_builtin_boolean(barrel, bore, hardflow_boolean_apply)

    # Double-Baffle Slotted Muzzle Brake
    muzzle_brake = rmk.make_cylinder(
        f"{prefix}_DoubleBaffleBrake_v2",
        radius=barrel_rad * 1.60,
        depth=0.048,
        location=Vector(barrel_center) + elev_quat @ Vector((0.0, config.barrel_length * 0.5 - 0.024, 0.0)),
        material=hardware_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=14,
        bevel=0.002,
    )
    # Perforated Thermal Jacket Sleeve
    thermal_sleeve = rmk.make_cylinder(
        f"{prefix}_ThermalJacketSleeve_v2",
        radius=barrel_rad * 1.28,
        depth=config.barrel_length * 0.42,
        location=Vector(barrel_center) - elev_quat @ Vector((0.0, config.barrel_length * 0.12, 0.0)),
        material=armor_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=12,
        bevel=0.001,
    )
    created.extend((muzzle_brake, thermal_sleeve))

    # 4. Ammunition Magazine, Articulated Feed Chute & Ejection Port
    mag_pos = _transform_world(config.center, frame, (0.022 + cw * 0.5 + cw * 0.42, -0.06, ch * 0.5))
    magazine = rmk.make_box(
        f"{prefix}_AmmoMag_v2",
        (cw * 0.46, 0.17, 0.22),
        mag_pos,
        armor_material,
        bevel=0.004,
        rotation=rotation,
    )
    # Articulated flexible link chute with individual segment rings
    feed_chute = rmk.make_box(
        f"{prefix}_LinklessFeedChute_v2",
        (0.042, 0.11, 0.065),
        _transform_world(config.center, frame, (0.022 + cw * 0.5 + cw * 0.22, 0.02, ch * 0.5)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    # Downward spent cartridge case ejection chute
    eject_chute = rmk.make_box(
        f"{prefix}_SpentCaseEjectionChute",
        (cw * 0.35, 0.050, 0.040),
        _transform_world(config.center, frame, (0.022 + cw * 0.5, 0.04, ch * 0.5 - 0.08)),
        hardware_material,
        bevel=0.001,
        rotation=elev_rot,
    )
    created.extend((magazine, feed_chute, eject_chute))

    # 5. Stabilized Dual-Sensor Fire Control Suite (FLIR + Daylight / LRF)
    optic_pos = _transform_world(config.center, frame, (0.022 + cw * 0.5 - cw * 0.36, 0.08, ch * 0.5 + 0.09))
    optic_turret = rmk.make_cylinder(
        f"{prefix}_DualSensorTurret_v2",
        radius=0.035,
        depth=0.070,
        location=optic_pos,
        material=hardware_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=14,
        bevel=0.002,
    )
    flir_lens = rmk.make_cylinder(
        f"{prefix}_FLIRThermalAperture",
        radius=0.020,
        depth=0.010,
        location=Vector(optic_pos) + elev_quat @ Vector((-0.010, 0.036, 0.0)),
        material=accent_material or hardware_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=12,
    )
    daylight_lens = rmk.make_cylinder(
        f"{prefix}_DaylightLaserAperture",
        radius=0.012,
        depth=0.010,
        location=Vector(optic_pos) + elev_quat @ Vector((0.014, 0.036, 0.0)),
        material=accent_material or hardware_material,
        rotation=(elev_quat @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=10,
    )
    created.extend((optic_turret, flir_lens, daylight_lens))

    _apply_hardflow_or_builtin_bevel(cradle, hardflow_bevel, bevel_width=0.004)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = AUTOCANNON_REMOTE_SPONSON_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"
    return created


# =========================================================================
# Preset 7: Faceted Geodesic Phase Shield Emitter v2 (APS Futuristic)
# =========================================================================

@dataclass(frozen=True)
class ApsPhaseShieldV2Config:
    """Dimensions and placement for a polished faceted phase shield emitter v2."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 0.0, 1.0)
    nacelle_size: tuple[float, float, float] = (0.30, 0.40, 0.22)
    lens_radius: float = 0.082
    coil_ring_count: int = 3
    vent_louver_count: int = 5
    conduit_radius: float = 0.015
    facet_angle_deg: float = 45.0
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.nacelle_size, self.lens_radius,
                  self.conduit_radius, self.facet_angle_deg, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("ApsPhaseShieldV2Config parameters must be finite")
        if min(*self.nacelle_size, self.lens_radius, self.conduit_radius) <= 0.0:
            raise ValueError("ApsPhaseShieldV2Config dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("ApsPhaseShieldV2Config normal must not be zero")
        if self.lens_radius >= min(self.nacelle_size[:2]) * 0.50:
            raise ValueError("lens_radius exceeds nacelle aperture")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_aps_phase_shield_v2_preset(
    *,
    prefix: str,
    config: ApsPhaseShieldV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished faceted phase shield emitter v2 with geodesic photonic crystal lens."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    nw, nd, nh = config.nacelle_size

    # 1. Faceted Wedge Nacelle with Razor Chine & Recessed RAM Sawtooth Seams
    nacelle = rmk.make_box(
        f"{prefix}_StealthNacelle_v2",
        config.nacelle_size,
        _transform_world(config.center, frame, (0.0, 0.0, nh * 0.5)),
        armor_material,
        bevel=0.018,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(nacelle)

    # Chine cheekplates
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        chine = rmk.make_box(
            f"{prefix}_ChinePlate_{side}_v2",
            (0.024, nd * 0.88, nh * 0.72),
            _transform_world(config.center, frame, (sx * (nw * 0.5 + 0.010), 0.0, nh * 0.45)),
            hardware_material,
            bevel=0.004,
            rotation=rotation,
        )
        created.append(chine)

    # 2. Concave Parabolic Emitter Cavity Cutout
    aperture_r = config.lens_radius + 0.022
    cavity_cutter = rmk.make_cylinder(
        f"{prefix}_ParabolicCavityCutter",
        radius=aperture_r,
        depth=0.065,
        location=_transform_world(config.center, frame, (0.0, nd * 0.40, nh * 0.5)),
        material=None,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=16,
    )
    _apply_hardflow_or_builtin_boolean(nacelle, cavity_cutter, hardflow_boolean_apply)

    # 3. Geodesic Photonic Crystal Lens Core & Collimation Rings
    lens_pos = _transform_world(config.center, frame, (0.0, nd * 0.40 - 0.015, nh * 0.5))
    lens_rot = (frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler()

    photonic_lens = rmk.make_cylinder(
        f"{prefix}_PhotonicCrystalLens_v2",
        radius=config.lens_radius,
        depth=0.018,
        location=lens_pos,
        material=accent_material or hardware_material,
        rotation=lens_rot,
        vertices=12,
        bevel=0.002,
    )
    created.append(photonic_lens)

    # 3 Concentric Stepped Superconducting Magnetic Collimation Rings
    for r_idx in range(config.coil_ring_count):
        r_scale = 1.05 + r_idx * 0.12
        ring = rmk.make_torus(
            f"{prefix}_CollimationCoil_{r_idx + 1}_v2",
            Vector(lens_pos) + frame @ Vector((0.0, r_idx * 0.008, 0.0)),
            major_radius=config.lens_radius * r_scale,
            minor_radius=0.004,
            material=hardware_material,
            rotation=lens_rot,
        )
        created.append(ring)

    # 4 Convergent Tungsten-Rhenium Arc Needles at 90° intervals
    for a_idx, ang_deg in enumerate((0.0, 90.0, 180.0, 270.0), 1):
        arad = math.radians(ang_deg)
        ax = math.cos(arad) * (config.lens_radius * 1.05)
        az = math.sin(arad) * (config.lens_radius * 1.05)
        needle = rmk.make_cylinder(
            f"{prefix}_ArcNeedle_{a_idx}",
            radius=0.003,
            depth=0.024,
            location=Vector(lens_pos) + frame @ Vector((ax, 0.010, az)),
            material=accent_material or hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 0.0, 1.0)), -arad)).to_euler(),
            vertices=8,
        )
        created.append(needle)

    # 4. Liquid-Helium Heat Pipes to Upper Cryo Dissipation Louvers
    for p_idx, px in enumerate((-nw * 0.28, nw * 0.28), 1):
        pipe = rmk.make_cylinder(
            f"{prefix}_HeliumHeatPipe_{p_idx}",
            radius=0.007,
            depth=nd * 0.65,
            location=_transform_world(config.center, frame, (px, 0.0, nh + 0.008)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=8,
        )
        created.append(pipe)

    # Translucent spine diagnostic waveguide strip
    waveguide = rmk.make_box(
        f"{prefix}_DiagnosticWaveguide_v2",
        (0.018, nd * 0.70, 0.008),
        _transform_world(config.center, frame, (0.0, -nd * 0.05, nh + 0.004)),
        accent_material or hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    created.append(waveguide)

    # Armored coaxial power terminal with bayonet lock
    power_term = rmk.make_cylinder(
        f"{prefix}_CoaxialPowerTerminal_v2",
        radius=config.conduit_radius,
        depth=0.040,
        location=_transform_world(config.center, frame, (0.0, -nd * 0.5 - 0.015, nh * 0.45)),
        material=hardware_material,
        rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=10,
        bevel=0.001,
    )
    created.append(power_term)

    _apply_hardflow_or_builtin_bevel(nacelle, hardflow_bevel, bevel_width=0.005)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = APS_PHASE_SHIELD_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "APS"
    return created


# =========================================================================
# Preset 8: Polyhedral Plasma Confinement Vault v2 (Sponsons Futuristic)
# =========================================================================

@dataclass(frozen=True)
class PlasmaContainmentCellV2Config:
    """Dimensions and placement for a polished plasma containment vault v2."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (1.0, 0.0, 0.0)
    vault_size: tuple[float, float, float] = (0.56, 0.46, 0.42)
    core_window_radius: float = 0.095
    torus_major_radius: float = 0.17
    torus_minor_radius: float = 0.024
    fin_count: int = 6
    manifold_pipe_radius: float = 0.016
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.vault_size, self.core_window_radius,
                  self.torus_major_radius, self.torus_minor_radius, self.manifold_pipe_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("PlasmaContainmentCellV2Config parameters must be finite")
        if min(*self.vault_size, self.core_window_radius, self.torus_major_radius,
               self.torus_minor_radius, self.manifold_pipe_radius) <= 0.0:
            raise ValueError("PlasmaContainmentCellV2Config dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("PlasmaContainmentCellV2Config normal must not be zero")
        if self.core_window_radius >= min(self.vault_size[1:]) * 0.45:
            raise ValueError("core_window_radius is too large for vault face")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_plasma_containment_cell_v2_preset(
    *,
    prefix: str,
    config: PlasmaContainmentCellV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished plasma containment vault v2 with quad coils and triple manifold."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _side_mount_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    vw, vd, vh = config.vault_size

    # 1. Polyhedral Armored Vault Shell & Titanium-Carbide Skid Plate
    vault_center = _transform_world(config.center, frame, (0.0, 0.0, vh * 0.5))
    vault = rmk.make_box(
        f"{prefix}_VaultShell_v2",
        config.vault_size,
        vault_center,
        armor_material,
        bevel=0.016,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(vault)

    # 4 Corner Forged Lifting Eyes
    for sx in (-vw * 0.40, vw * 0.40):
        for sy in (-vd * 0.40, vd * 0.40):
            eye = rmk.make_torus(
                f"{prefix}_CornerLiftingEye",
                _transform_world(config.center, frame, (sx, sy, vh + 0.015)),
                major_radius=0.015,
                minor_radius=0.004,
                material=hardware_material,
                rotation=rotation,
            )
            created.append(eye)

    # 2. Viewport Aperture Cut & Bolted 12-Flange with 6 Radial Flux Probes
    bezel_rot = (frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler()
    window_cutter = rmk.make_cylinder(
        f"{prefix}_WindowCut_v2",
        radius=config.core_window_radius + 0.010,
        depth=0.050,
        location=_transform_world(config.center, frame, (vw * 0.5 - 0.010, 0.0, vh * 0.5)),
        material=None,
        rotation=bezel_rot,
        vertices=16,
    )
    _apply_hardflow_or_builtin_boolean(vault, window_cutter, hardflow_boolean_apply)

    bezel = rmk.make_cylinder(
        f"{prefix}_FluxBezel_v2",
        radius=config.core_window_radius + 0.024,
        depth=0.018,
        location=_transform_world(config.center, frame, (vw * 0.5 + 0.003, 0.0, vh * 0.5)),
        material=hardware_material,
        rotation=bezel_rot,
        vertices=8,
        bevel=0.002,
    )
    core_glass = rmk.make_cylinder(
        f"{prefix}_QuartzCore_v2",
        radius=config.core_window_radius,
        depth=0.014,
        location=_transform_world(config.center, frame, (vw * 0.5 - 0.002, 0.0, vh * 0.5)),
        material=accent_material or hardware_material,
        rotation=bezel_rot,
        vertices=16,
    )
    created.extend((bezel, core_glass))

    # 6 Radial Electromagnetic Flux Probes with luminous tips
    for p_idx in range(6):
        ang_rad = math.radians(p_idx * 60.0)
        px = math.cos(ang_rad) * (config.core_window_radius + 0.016)
        py = math.sin(ang_rad) * (config.core_window_radius + 0.016)
        probe = rmk.make_cylinder(
            f"{prefix}_FluxProbe_{p_idx + 1}_v2",
            radius=0.006,
            depth=0.016,
            location=Vector(vault_center) + frame @ Vector((vw * 0.5 + 0.008, px, py)),
            material=accent_material or hardware_material,
            rotation=bezel_rot,
            vertices=8,
        )
        created.append(probe)

    # 3. Quad Magnetic Confinement Coils (2 Primary Toroidal + 2 Helical Poloidal)
    torus_rot = (frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler()
    for t_idx, y_off in enumerate((-vd * 0.28, vd * 0.28), 1):
        torus = rmk.make_torus(
            f"{prefix}_TorusCoil_{t_idx}_v2",
            _transform_world(config.center, frame, (0.0, y_off, vh * 0.5)),
            major_radius=config.torus_major_radius,
            minor_radius=config.torus_minor_radius,
            material=hardware_material,
            rotation=torus_rot,
        )
        saddle = rmk.make_box(
            f"{prefix}_TorusSaddle_{t_idx}_v2",
            (vw * 1.06, 0.038, config.torus_minor_radius * 2.8),
            _transform_world(config.center, frame, (0.0, y_off, vh * 0.5 + config.torus_major_radius)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.extend((torus, saddle))

    # 4. Triple Cryo Manifold & Dual 20° Emergency Relief Exhaust Vents
    for m_idx, mz_off in enumerate((0.0, -0.040, 0.040), 1):
        manifold = rmk.make_cylinder(
            f"{prefix}_TripleCryoPipe_{m_idx}",
            radius=config.manifold_pipe_radius * 0.85,
            depth=vd * 0.88,
            location=_transform_world(config.center, frame, (-vw * 0.25, 0.0, vh + 0.02 + mz_off * 0.5)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
        )
        created.append(manifold)

    for v_idx, vy in enumerate((-vd * 0.30, vd * 0.30), 1):
        nozzle = rmk.make_cylinder(
            f"{prefix}_ReliefNozzle_{v_idx}_v2",
            radius=0.014,
            depth=0.028,
            location=_transform_world(config.center, frame, (-vw * 0.25, vy, vh + 0.075)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(20.0))).to_euler(),
            vertices=10,
        )
        created.append(nozzle)

    # 5. Machined Base Heat-Sink Fins with Rock-Guard Underplate
    fin_count = config.fin_count
    fin_step = (vd * 0.72) / max(fin_count - 1, 1)
    for f_idx in range(fin_count):
        fy = -vd * 0.36 + f_idx * fin_step
        fin = rmk.make_box(
            f"{prefix}_HeatSinkFin_{f_idx + 1}_v2",
            (vw * 0.76, 0.007, 0.045),
            _transform_world(config.center, frame, (0.0, fy, -0.022)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.append(fin)

    rock_guard = rmk.make_box(
        f"{prefix}_RockGuardUnderplate",
        (vw * 0.82, vd * 0.76, 0.010),
        _transform_world(config.center, frame, (0.0, 0.0, -0.050)),
        armor_material,
        bevel=0.002,
        rotation=rotation,
    )
    created.append(rock_guard)

    _apply_hardflow_or_builtin_bevel(vault, hardflow_bevel, bevel_width=0.005)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = PLASMA_CONTAINMENT_CELL_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"
    return created


# =========================================================================
# Preset 9: Honeycomb Hexagonal Reactive Tile Array v2 (ERA Futuristic)
# =========================================================================

@dataclass(frozen=True)
class StealthHexReactiveTileV2Config:
    """Dimensions and placement for a polished honeycomb hex reactive tile array v2."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 0.0, 1.0)
    tile_radius: float = 0.078
    tile_height: float = 0.040
    tile_gap: float = 0.003
    backing_thickness: float = 0.009
    backing_overhang: float = 0.016
    facet_bevel: float = 0.009
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, self.tile_radius, self.tile_height,
                  self.tile_gap, self.backing_thickness, self.backing_overhang,
                  self.facet_bevel, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("StealthHexReactiveTileV2Config dimensions must be finite")
        if min(self.tile_radius, self.tile_height, self.tile_gap, self.backing_thickness,
               self.backing_overhang, self.facet_bevel) <= 0.0:
            raise ValueError("StealthHexReactiveTileV2Config dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("StealthHexReactiveTileV2Config normal must not be zero")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_stealth_hex_reactive_tile_v2_preset(
    *,
    prefix: str,
    config: StealthHexReactiveTileV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished honeycomb hex reactive tile array v2 with 3-tier ballistic structure and dual sensors."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    r = config.tile_radius
    h = config.tile_height
    gap = config.tile_gap
    step = r * math.sqrt(3.0) + gap

    # 1. 7 Interlocking Hexagonal Tile Positions (1 center + 6 ring)
    hex_offsets = [(0.0, 0.0)]
    for i in range(6):
        ang = math.radians(i * 60.0 + 30.0)
        hex_offsets.append((math.cos(ang) * step, math.sin(ang) * step))

    # 8-Sided Faceted Backing Plate with CNC Weight-Reduction Pockets
    backing_r = step * 1.62 + config.backing_overhang
    backing = rmk.make_cylinder(
        f"{prefix}_FacetedBacking_v2",
        radius=backing_r,
        depth=config.backing_thickness,
        location=_transform_world(config.center, frame, (0.0, 0.0, config.backing_thickness * 0.5)),
        material=hardware_material,
        bevel=0.002,
        rotation=rotation,
        vertices=8,
    )
    created.append(backing)

    # 2. 7 Composite Hex Tiles with 3-Tier Ballistic Sandwich & Dual Sensor Diodes
    tile_z = config.backing_thickness + h * 0.5
    for t_idx, (hx, hy) in enumerate(hex_offsets, 1):
        tile_pos = _transform_world(config.center, frame, (hx, hy, tile_z))

        # Base reactive charge layer
        core_tile = rmk.make_cylinder(
            f"{prefix}_HexCore_{t_idx}_v2",
            radius=r,
            depth=h * 0.70,
            location=tile_pos,
            material=armor_material,
            rotation=rotation,
            vertices=6,
            bevel=0.003,
        )
        # Outer composite stealth cap with 6-micro-chine plateau
        cap_tile = rmk.make_cylinder(
            f"{prefix}_StealthCap_{t_idx}_v2",
            radius=r * 0.85,
            depth=h * 0.35,
            location=Vector(tile_pos) + frame @ Vector((0.0, 0.0, h * 0.36)),
            material=armor_material,
            rotation=rotation,
            vertices=6,
            bevel=config.facet_bevel * 0.60,
        )
        # Dual sensors: IR trigger photodiode + micro-radar millimeter-wave node
        sensor_ir = rmk.make_cylinder(
            f"{prefix}_SensorIR_{t_idx}",
            radius=0.005,
            depth=0.008,
            location=Vector(tile_pos) + frame @ Vector((-r * 0.28, 0.0, h * 0.52)),
            material=accent_material or hardware_material,
            rotation=rotation,
            vertices=8,
        )
        sensor_rf = rmk.make_cylinder(
            f"{prefix}_SensorRF_{t_idx}",
            radius=0.005,
            depth=0.008,
            location=Vector(tile_pos) + frame @ Vector((r * 0.28, 0.0, h * 0.52)),
            material=accent_material or hardware_material,
            rotation=rotation,
            vertices=8,
        )
        # Central recessed hex-socket captive mount pin
        pin = rmk.make_cylinder(
            f"{prefix}_CaptivePin_{t_idx}",
            radius=0.007,
            depth=0.010,
            location=Vector(tile_pos) + frame @ Vector((0.0, 0.0, h * 0.52)),
            material=hardware_material,
            rotation=rotation,
            vertices=6,
        )
        created.extend((core_tile, cap_tile, sensor_ir, sensor_rf, pin))

    # 3. 6 Inter-Tile Valley Captive Fasteners
    for f_idx in range(6):
        fang = math.radians(f_idx * 60.0)
        fx = math.cos(fang) * (step * 0.58)
        fy = math.sin(fang) * (step * 0.58)
        fastener = rmk.make_cylinder(
            f"{prefix}_ValleyFastener_{f_idx + 1}_v2",
            radius=0.006,
            depth=0.016,
            location=_transform_world(config.center, frame, (fx, fy, config.backing_thickness + 0.008)),
            material=hardware_material,
            rotation=rotation,
            vertices=8,
        )
        created.append(fastener)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = STEALTH_HEX_REACTIVE_TILE_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "ERA"
    return created


# =========================================================================
# Preset 10: Quantum Plasma Torpedo Magazine Rack v2 (FuelDrums Futuristic)
# =========================================================================

@dataclass(frozen=True)
class PlasmaTorpedoMagazineV2Config:
    """Dimensions and placement for a polished quantum torpedo magazine rack v2."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    rack_size: tuple[float, float, float] = (0.66, 0.40, 0.42)
    torpedo_count: int = 3
    torpedo_radius: float = 0.052
    torpedo_length: float = 0.38
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.rack_size, self.torpedo_radius,
                  self.torpedo_length, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("PlasmaTorpedoMagazineV2Config dimensions must be finite")
        if min(*self.rack_size, self.torpedo_radius, self.torpedo_length) <= 0.0:
            raise ValueError("PlasmaTorpedoMagazineV2Config dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("PlasmaTorpedoMagazineV2Config normal must not be zero")
        if self.torpedo_count < 1:
            raise ValueError("torpedo_count must be at least 1")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_plasma_torpedo_magazine_v2_preset(
    *,
    prefix: str,
    config: PlasmaTorpedoMagazineV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished quantum torpedo magazine rack v2 with linear induction accelerator rails."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _local_y_outward_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    rw, rd, rh = config.rack_size

    # 1. Faceted Stealth Base Mounting Plate with Decoupling Anchors
    base_plate = rmk.make_box(
        f"{prefix}_StealthBase_v2",
        (rw, rd, 0.018),
        _transform_world(config.center, frame, (0.0, rd * 0.5, 0.009)),
        hardware_material,
        bevel=0.004,
        rotation=rotation,
    )
    # Arched Ballistic Armor Canopy Shielding Top Attack
    canopy = rmk.make_box(
        f"{prefix}_BallisticCanopy_v2",
        (rw * 0.96, rd * 0.85, 0.016),
        _transform_world(config.center, frame, (0.0, rd * 0.5, rh * 0.90)),
        armor_material,
        bevel=0.006,
        rotation=rotation,
    )
    created.extend((base_plate, canopy))

    # 2. Linear Induction Accelerator Rails & Stator Coil Blocks
    rail_spacing = rw / (config.torpedo_count + 1)
    for r_idx in range(1, config.torpedo_count + 1):
        rx = -rw * 0.5 + r_idx * rail_spacing
        rail = rmk.make_box(
            f"{prefix}_AcceleratorRail_{r_idx}_v2",
            (0.024, rd * 0.92, 0.040),
            _transform_world(config.center, frame, (rx, rd * 0.5, 0.038)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        # Copper induction stator coil blocks along rail
        stator = rmk.make_box(
            f"{prefix}_StatorCoils_{r_idx}",
            (0.032, rd * 0.75, 0.016),
            _transform_world(config.center, frame, (rx, rd * 0.5, 0.055)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((rail, stator))

    # 3. 3 Quantum Plasma Torpedoes with Glowing Containment Rings & Vectoring Bells
    t_rad = config.torpedo_radius
    for t_idx in range(1, config.torpedo_count + 1):
        tx = -rw * 0.5 + t_idx * rail_spacing
        torp_pos = _transform_world(config.center, frame, (tx, rd * 0.5, rh * 0.42))

        # Main torpedo body
        torpedo = rmk.make_cylinder(
            f"{prefix}_TorpedoCasing_{t_idx}_v2",
            radius=t_rad,
            depth=config.torpedo_length,
            location=torp_pos,
            material=armor_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=16,
            bevel=0.002,
        )
        # Kinetic penetrator ogive nose cone
        nose = rmk.make_cylinder(
            f"{prefix}_TorpedoNose_{t_idx}_v2",
            radius=t_rad * 0.85,
            depth=0.045,
            location=Vector(torp_pos) + frame @ Vector((0.0, config.torpedo_length * 0.5 + 0.022, 0.0)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=12,
            bevel=0.003,
        )
        # 3 Glowing Magnetic Containment Rings
        for c_idx, cy_off in enumerate((-0.08, 0.0, 0.08), 1):
            ring = rmk.make_cylinder(
                f"{prefix}_ContainmentRing_{t_idx}_{c_idx}",
                radius=t_rad * 1.05,
                depth=0.012,
                location=Vector(torp_pos) + frame @ Vector((0.0, cy_off, 0.0)),
                material=accent_material or hardware_material,
                rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=14,
            )
            created.append(ring)

        # Vectoring Thrust Nozzle Bell
        nozzle = rmk.make_cylinder(
            f"{prefix}_ThrustNozzle_{t_idx}_v2",
            radius=t_rad * 0.75,
            depth=0.028,
            location=Vector(torp_pos) - frame @ Vector((0.0, config.torpedo_length * 0.5 + 0.012, 0.0)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=12,
            bevel=0.002,
        )
        created.extend((torpedo, nose, nozzle))

    # 4. Superconducting Power Conduits & Emergency Plasma Dump Valve
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        conduit = rmk.make_cylinder(
            f"{prefix}_SuperconductingConduit_{side}_v2",
            radius=0.014,
            depth=rd * 0.88,
            location=_transform_world(config.center, frame, (sx * (rw * 0.48), rd * 0.5, rh * 0.45)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
        )
        created.append(conduit)

    dump_valve = rmk.make_box(
        f"{prefix}_EmergencyPlasmaDumpValve",
        (0.045, 0.035, 0.045),
        _transform_world(config.center, frame, (0.0, 0.035, rh * 0.45)),
        accent_material or hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    created.append(dump_valve)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = PLASMA_TORPEDO_MAGAZINE_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "FuelDrums"
    return created


# =========================================================================
# Preset 11: Tachyonic Superconducting Radiator Array v2 (Sponsons Futuristic)
# =========================================================================

@dataclass(frozen=True)
class TachyonicRadiatorArrayV2Config:
    """Flank radiator dimensions; local +X is outward, +Y forward, and +Z vertical."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (1.0, 0.0, 0.0)
    array_size: tuple[float, float, float] = (0.36, 0.64, 0.38)
    emitter_fin_count: int = 8
    core_rod_radius: float = 0.020
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.array_size, self.core_rod_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("TachyonicRadiatorArrayV2Config dimensions must be finite")
        if min(*self.array_size, self.core_rod_radius) <= 0.0:
            raise ValueError("TachyonicRadiatorArrayV2Config dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("TachyonicRadiatorArrayV2Config normal must not be zero")
        if self.emitter_fin_count < 2:
            raise ValueError("emitter_fin_count must be at least 2")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_tachyonic_radiator_array_v2_preset(
    *,
    prefix: str,
    config: TachyonicRadiatorArrayV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished tachyonic radiator array v2 with dual resonance rods and 8 serrated fins."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _side_mount_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    aw, ad, ah = config.array_size

    # 1. Diamond-Profile Stealth Cowl Shell with Variable-Geometry Louvers
    cowl_center = _transform_world(config.center, frame, (0.0, 0.0, ah * 0.5))
    cowl = rmk.make_box(
        f"{prefix}_StealthCowl_v2",
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
    cav_w = aw * 0.56
    cav_d = ad * 0.84
    cav_h = ah * 0.76
    cutter = rmk.make_box(
        f"{prefix}_CavityCut_v2",
        (cav_w, cav_d, cav_h),
        _transform_world(config.center, frame, (aw * 0.5 - cav_w * 0.5 + 0.005, 0.0, ah * 0.5)),
        None,
        rotation=rotation,
    )
    _apply_hardflow_or_builtin_boolean(cowl, cutter, hardflow_boolean_apply)

    # 2. Dual Superconducting Quantum Resonance Cores (twin parallel rods)
    for r_idx, rz_off in enumerate((-ah * 0.16, ah * 0.16), 1):
        core = rmk.make_cylinder(
            f"{prefix}_ResonanceCore_{r_idx}_v2",
            radius=config.core_rod_radius,
            depth=cav_d * 0.94,
            location=_transform_world(config.center, frame, (aw * 0.5 - cav_w * 0.65, 0.0, ah * 0.5 + rz_off)),
            material=accent_material or hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=14,
            bevel=0.002,
        )
        created.append(core)

    # 3. 8 Forward-Swept Serrated Heat Dissipation Fins
    fin_count = config.emitter_fin_count
    fin_step = cav_d / (fin_count + 1)
    for f_idx in range(1, fin_count + 1):
        fy = -cav_d * 0.5 + f_idx * fin_step
        fin_quat = frame @ Quaternion(Vector((0.0, 0.0, 1.0)), math.radians(26.0))
        fin = rmk.make_box(
            f"{prefix}_SerratedFin_{f_idx}_v2",
            (cav_w * 0.94, 0.008, cav_h * 0.92),
            _transform_world(config.center, frame, (aw * 0.5 - cav_w * 0.35, fy, ah * 0.5)),
            hardware_material,
            bevel=0.001,
            rotation=fin_quat.to_euler(),
        )
        created.append(fin)

    # 4. 6-Node Magnetic Field Deflection Emitter Array
    for ny in (-ad * 0.38, 0.0, ad * 0.38):
        for nz in (ah * 0.20, ah * 0.80):
            node = rmk.make_cylinder(
                f"{prefix}_DeflectionNode_{'F' if ny > 0.01 else 'M' if abs(ny) < 0.01 else 'R'}_{'T' if nz > ah * 0.5 else 'B'}_v2",
                radius=0.013,
                depth=0.018,
                location=_transform_world(config.center, frame, (aw * 0.5 + 0.004, ny, nz)),
                material=accent_material or hardware_material,
                rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
                vertices=8,
            )
            created.append(node)

    # 5. Cryo Distribution Manifold with Digital Temperature Telemetry Node
    for p_z, tag in ((ah * 0.88, "Top"), (ah * 0.12, "Bottom")):
        pipe = rmk.make_cylinder(
            f"{prefix}_CryoPipe_{tag}_v2",
            radius=0.013,
            depth=ad * 0.92,
            location=_transform_world(config.center, frame, (-aw * 0.38, 0.0, p_z)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=10,
        )
        coupler = rmk.make_box(
            f"{prefix}_MagneticCoupler_{tag}_v2",
            (0.028, 0.038, 0.028),
            _transform_world(config.center, frame, (-aw * 0.38, ad * 0.44, p_z)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((pipe, coupler))

    temp_sensor = rmk.make_cylinder(
        f"{prefix}_DigitalTempTelemetryNode",
        radius=0.008,
        depth=0.012,
        location=_transform_world(config.center, frame, (-aw * 0.40, -ad * 0.40, ah * 0.5)),
        material=accent_material or hardware_material,
        rotation=(frame @ Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(90.0))).to_euler(),
        vertices=8,
    )
    created.append(temp_sensor)

    _apply_hardflow_or_builtin_bevel(cowl, hardflow_bevel, bevel_width=0.006)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = TACHYONIC_RADIATOR_ARRAY_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"
    return created


# =========================================================================
# Preset 12: Autonomous Nanite Hive Repair Dispenser v2 (Sponsons Futuristic)
# =========================================================================

@dataclass(frozen=True)
class NaniteRepairDispenserV2Config:
    """Dimensions and placement for a polished nanite repair dispenser pod v2."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (1.0, 0.0, 0.0)
    pod_size: tuple[float, float, float] = (0.34, 0.48, 0.38)
    canister_radius: float = 0.062
    nozzle_count: int = 4
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.pod_size, self.canister_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("NaniteRepairDispenserV2Config dimensions must be finite")
        if min(*self.pod_size, self.canister_radius) <= 0.0:
            raise ValueError("NaniteRepairDispenserV2Config dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("NaniteRepairDispenserV2Config normal must not be zero")
        if self.nozzle_count < 1:
            raise ValueError("nozzle_count must be at least 1")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_nanite_repair_dispenser_v2_preset(
    *,
    prefix: str,
    config: NaniteRepairDispenserV2Config,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a polished nanite repair dispenser pod v2 with 4-nozzle manifold and micro-drone bay."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _local_y_outward_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    pw, pd, ph = config.pod_size

    # 1. Composite Aerodynamic Sponson Housing
    housing = rmk.make_box(
        f"{prefix}_PodHousing_v2",
        config.pod_size,
        _transform_world(config.center, frame, (0.0, 0.0, ph * 0.5)),
        armor_material,
        bevel=0.015,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    created.append(housing)

    # 2. Quad-Petal Motorized Aperture Shutter Doors (+Y forward face)
    for sx, side in ((-1.0, "L"), (1.0, "R")):
        for sz, vert in ((-1.0, "B"), (1.0, "T")):
            door = rmk.make_box(
                f"{prefix}_IrisShutter_{side}_{vert}",
                (pw * 0.44, 0.012, ph * 0.42),
                _transform_world(config.center, frame, (sx * (pw * 0.23), pd * 0.5 + 0.005, ph * 0.5 + sz * (ph * 0.22))),
                hardware_material,
                bevel=0.002,
                rotation=rotation,
            )
            created.append(door)

    # 3. Twin Filament-Wound Nanite Flasks with Transparent Inspection Viewports
    for cx, c_tag in ((-pw * 0.26, "L"), (pw * 0.26, "R")):
        flask = rmk.make_cylinder(
            f"{prefix}_NaniteFlask_{c_tag}_v2",
            radius=config.canister_radius,
            depth=pd * 0.80,
            location=_transform_world(config.center, frame, (cx, 0.0, ph * 0.5)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=14,
            bevel=0.002,
        )
        # Transparent illuminated optical inspection gauge
        gauge = rmk.make_box(
            f"{prefix}_InspectionGauge_{c_tag}",
            (0.010, pd * 0.60, 0.024),
            _transform_world(config.center, frame, (cx + (0.025 if cx > 0 else -0.025), 0.0, ph * 0.5)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.extend((flask, gauge))

    # 4. Convergent 4-Nozzle Dispersal Manifold with Magnetic Induction Rings
    for n_idx, nx in enumerate((-pw * 0.28, -pw * 0.09, pw * 0.09, pw * 0.28), 1):
        nozzle = rmk.make_cylinder(
            f"{prefix}_DispersalNozzle_{n_idx}_v2",
            radius=0.014,
            depth=0.040,
            location=_transform_world(config.center, frame, (nx, pd * 0.5 + 0.020, ph * 0.40)),
            material=hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=12,
            bevel=0.002,
        )
        induction_ring = rmk.make_cylinder(
            f"{prefix}_InductionRing_{n_idx}",
            radius=0.019,
            depth=0.010,
            location=_transform_world(config.center, frame, (nx, pd * 0.5 + 0.024, ph * 0.40)),
            material=accent_material or hardware_material,
            rotation=(frame @ Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(90.0))).to_euler(),
            vertices=12,
        )
        created.extend((nozzle, induction_ring))

    # 5. Auxiliary Micro-Drone Hive Bay above nozzles
    drone_bay = rmk.make_box(
        f"{prefix}_MicroDroneHiveBay",
        (pw * 0.60, 0.015, 0.045),
        _transform_world(config.center, frame, (0.0, pd * 0.5 + 0.008, ph * 0.72)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    created.append(drone_bay)

    # 6. Phased-Array Telemetry Panel & 4-Segment Diagnostic LED Bar
    phased_panel = rmk.make_box(
        f"{prefix}_PhasedTelemetryPanel",
        (pw * 0.65, pd * 0.35, 0.010),
        _transform_world(config.center, frame, (0.0, -pd * 0.20, ph + 0.005)),
        hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    led_bar = rmk.make_box(
        f"{prefix}_DiagnosticLedBar",
        (0.010, 0.060, 0.015),
        _transform_world(config.center, frame, (pw * 0.5 + 0.004, -pd * 0.15, ph * 0.60)),
        accent_material or hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    created.extend((phased_panel, led_bar))

    _apply_hardflow_or_builtin_bevel(housing, hardflow_bevel, bevel_width=0.005)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = NANITE_REPAIR_DISPENSER_V2_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"
    return created
