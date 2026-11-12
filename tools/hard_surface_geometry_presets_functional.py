"""Functional Level 2 hard-surface geometry presets for canonical vehicle roles.

Implements realistic contemporary and futuristic military canonical vehicle components,
directly bound to canonical functional roles from canonical-visual-module-roles.json:
- excavation_drill: Carbide rotary borer (contemporary) & plasma thermal lance (futuristic).
- docking_clamp: Articulated hydraulic grapple (contemporary) & magnetic tractor clamp (futuristic).
- power_containment / utility_sponson: Tactical APU turbogenerator (contemporary) & graviton recoil anchor (futuristic).
- aps_sensor_suite: Multispectral smoke salvo discharger (contemporary) & optronic recon sensor mast (futuristic).
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
    _apply_hardflow_or_builtin_bevel,
    _apply_hardflow_or_builtin_boolean,
    _orientation_frame,
    _transform_world,
)

# Preset ID Constants
EXCAVATION_DRILL_CARBIDE_BORER_PRESET_ID = "excavation_drill_carbide_borer_v1"
DOCKING_CLAMP_HYDRAULIC_ARM_PRESET_ID = "docking_clamp_hydraulic_arm_v1"
APU_TURBOGENERATOR_SPONSON_PRESET_ID = "apu_turbogenerator_sponson_v1"
SMOKE_GRENADE_SALVO_DISCHARGER_PRESET_ID = "smoke_grenade_salvo_discharger_v1"
CIWS_ROTARY_POINT_DEFENSE_PRESET_ID = "ciws_rotary_point_defense_sponson_v1"
REINFORCED_FUEL_DRUM_DROP_RACK_PRESET_ID = "reinforced_fuel_drum_drop_rack_v1"
FIELD_ENGINEERING_CRANE_WINCH_PRESET_ID = "field_engineering_crane_winch_sponson_v1"
MULTISPECTRAL_COUNTERMEASURE_JAMMER_PRESET_ID = "multispectral_countermeasure_jammer_v1"

PLASMA_EXCAVATION_TORCH_PRESET_ID = "plasma_excavation_torch_v1"
MAGNETIC_TRACTOR_DOCKING_CLAMP_PRESET_ID = "magnetic_tractor_docking_clamp_v1"
GRAVITON_RECOIL_ANCHOR_PRESET_ID = "graviton_recoil_anchor_v1"
OPTRONIC_RECON_SENSOR_MAST_PRESET_ID = "optronic_recon_sensor_mast_v1"
QUAD_PULSE_LASER_CIWS_PRESET_ID = "quad_pulse_laser_ciws_sponson_v1"
CRYO_FUSION_CELL_CONTAINMENT_RACK_PRESET_ID = "cryo_fusion_cell_containment_rack_v1"
NANOFABRICATION_REPAIR_PROJECTOR_PRESET_ID = "nanofabrication_repair_projector_sponson_v1"
FLUX_DEFLECTION_SHIELD_PROJECTOR_PRESET_ID = "flux_deflection_shield_projector_v1"


def _make_cone(
    name: str,
    radius_base: float,
    depth: float,
    location: tuple[float, float, float] | Vector,
    material: bpy.types.Material | None,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    vertices: int = 12,
    radius_tip: float = 0.002,
) -> bpy.types.Object:
    return rmk.make_tapered_cylinder(
        name=name,
        radius_base=radius_base,
        radius_tip=radius_tip,
        depth=depth,
        location=location,
        material=material,
        rotation=rotation,
        vertices=vertices,
    )


def _make_sphere(
    name: str,
    radius: float,
    location: tuple[float, float, float] | Vector,
    material: bpy.types.Material | None,
    segments: int = 12,
    ring_count: int = 8,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=ring_count, radius=radius, location=location
    )
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return rmk.finish_mesh(obj, material, bevel=min(0.008, radius * 0.15))


def _make_socket(
    name: str,
    location: tuple[float, float, float] | Vector,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    radius: float = 0.10,
) -> bpy.types.Object:
    bpy.ops.object.empty_add(type="PLAIN_AXES", radius=radius, location=location)
    emp = bpy.context.object
    emp.name = name
    emp.rotation_euler = rotation
    return emp


# =========================================================================
# Preset 1: Rotary Carbide Excavation Borer (Sponsons - Contemporary)
# =========================================================================

@dataclass(frozen=True)
class ExcavationDrillCarbideBorerConfig:
    """Dimensions and placement for a heavy rotary carbide excavation borer."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    base_plate_size: tuple[float, float, float] = (0.34, 0.28, 0.022)
    gearbox_radius: float = 0.095
    gearbox_length: float = 0.160
    drill_base_radius: float = 0.110
    drill_tip_radius: float = 0.032
    drill_length: float = 0.320
    shroud_width: float = 0.300
    fastener_radius: float = 0.007
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.base_plate_size, self.gearbox_radius,
                  self.gearbox_length, self.drill_base_radius, self.drill_tip_radius,
                  self.drill_length, self.shroud_width, self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("ExcavationDrillCarbideBorerConfig parameters must be finite")
        if min(*self.base_plate_size, self.gearbox_radius, self.gearbox_length,
               self.drill_base_radius, self.drill_tip_radius, self.drill_length,
               self.shroud_width, self.fastener_radius) <= 0.0:
            raise ValueError("ExcavationDrillCarbideBorerConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("ExcavationDrillCarbideBorerConfig normal must not be zero")
        if self.drill_base_radius <= self.drill_tip_radius:
            raise ValueError("drill_base_radius must exceed drill_tip_radius for a tapered cutting head")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_excavation_drill_carbide_borer_preset(
    *,
    prefix: str,
    config: ExcavationDrillCarbideBorerConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a heavy rotary carbide excavation borer module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    bw, bd, bh = config.base_plate_size

    # 1. Base Mounting Plate with 6 Flange Bolts
    base_plate = rmk.make_box(
        f"{prefix}_BasePlate",
        config.base_plate_size,
        _transform_world(config.center, frame, (0.0, 0.0, bh * 0.5)),
        hardware_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(base_plate)

    for x_sign in (-1.0, 1.0):
        for y_pos in (-bd * 0.38, 0.0, bd * 0.38):
            bolt = rmk.make_cylinder(
                f"{prefix}_BaseBolt_{x_sign}_{y_pos:.2f}",
                config.fastener_radius,
                0.008,
                _transform_world(config.center, frame, (x_sign * bw * 0.42, y_pos, bh + 0.003)),
                hardware_material,
                rotation=rotation,
                vertices=6,
            )
            created.append(bolt)

    # 2. Structural Trunnion / Gusset Brackets
    for side in (-1.0, 1.0):
        gusset = rmk.make_box(
            f"{prefix}_TrunnionGusset_{side}",
            (0.016, bd * 0.65, 0.085),
            _transform_world(config.center, frame, (side * bw * 0.32, 0.0, bh + 0.042)),
            armor_material,
            bevel=0.004,
            rotation=rotation,
        )
        created.append(gusset)

    # 3. Cylindrical Planetary Gearbox Housing with Longitudinal Cooling Ribs
    gear_z = bh + config.gearbox_length * 0.5 + 0.02
    gearbox = rmk.make_cylinder(
        f"{prefix}_GearboxCasing",
        config.gearbox_radius,
        config.gearbox_length,
        _transform_world(config.center, frame, (0.0, 0.0, gear_z)),
        hardware_material,
        rotation=rotation,
        vertices=16,
        bevel=0.005,
    )
    created.append(gearbox)

    for i in range(6):
        rib_ang = i * (math.pi / 3.0)
        rx = math.cos(rib_ang) * (config.gearbox_radius + 0.004)
        ry = math.sin(rib_ang) * (config.gearbox_radius + 0.004)
        rib = rmk.make_box(
            f"{prefix}_CoolingRib_{i}",
            (0.008, 0.014, config.gearbox_length * 0.85),
            _transform_world(config.center, frame, (rx, ry, gear_z)),
            armor_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.append(rib)

    # 4. Oil Sight Glass with Brass Bezel
    sight_bezel = rmk.make_cylinder(
        f"{prefix}_OilSightBezel",
        0.018,
        0.008,
        _transform_world(config.center, frame, (config.gearbox_radius + 0.006, 0.0, gear_z)),
        hardware_material,
        rotation=rotation,
        vertices=8,
    )
    created.append(sight_bezel)
    sight_lens = rmk.make_cylinder(
        f"{prefix}_OilSightLens",
        0.012,
        0.004,
        _transform_world(config.center, frame, (config.gearbox_radius + 0.010, 0.0, gear_z)),
        accent_material or hardware_material,
        rotation=rotation,
        vertices=8,
    )
    created.append(sight_lens)

    # 5. Dual Radial Hydraulic Drive Motors with 90° Swivel Fittings
    for side in (-1.0, 1.0):
        motor = rmk.make_cylinder(
            f"{prefix}_HydraulicMotor_{side}",
            0.042,
            0.090,
            _transform_world(config.center, frame, (side * (config.gearbox_radius + 0.045), 0.0, bh + 0.070)),
            armor_material,
            rotation=rotation,
            vertices=12,
            bevel=0.003,
        )
        created.append(motor)

        fitting = rmk.make_box(
            f"{prefix}_HydraulicFitting_{side}",
            (0.018, 0.018, 0.022),
            _transform_world(config.center, frame, (side * (config.gearbox_radius + 0.045), 0.038, bh + 0.070)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.append(fitting)

        hose = rmk.make_rod(
            f"{prefix}_HydraulicHose_{side}",
            _transform_world(config.center, frame, (side * (config.gearbox_radius + 0.045), 0.038, bh + 0.070)),
            _transform_world(config.center, frame, (side * bw * 0.28, 0.080, bh + 0.010)),
            0.009,
            hardware_material,
            vertices=8,
        )
        created.append(hose)

    # 6. Torque Reaction Arm with Clevis Mount
    torque_arm = rmk.make_box(
        f"{prefix}_TorqueReactionArm",
        (0.026, 0.042, config.gearbox_length + 0.03),
        _transform_world(config.center, frame, (0.0, -config.gearbox_radius - 0.022, gear_z)),
        armor_material,
        bevel=0.004,
        rotation=rotation,
    )
    created.append(torque_arm)

    # 7. Curved Debris Deflector Shroud & Water Spray Manifold
    shroud_z = gear_z + config.gearbox_length * 0.40
    shroud = rmk.make_box(
        f"{prefix}_DebrisDeflectorShroud",
        (config.shroud_width, 0.018, 0.160),
        _transform_world(config.center, frame, (0.0, config.gearbox_radius + 0.045, shroud_z + 0.05)),
        armor_material,
        bevel=0.005,
        rotation=rotation,
    )
    created.append(shroud)

    # 8. Rotary Cutting Head: Tapered Core with Spiral Flutes & Carbide Pick Teeth
    drill_z_start = bh + config.gearbox_length + 0.025
    drill_z_center = drill_z_start + config.drill_length * 0.5

    drill_cone = _make_cone(
        f"{prefix}_BorerCuttingHead",
        config.drill_base_radius,
        config.drill_length,
        _transform_world(config.center, frame, (0.0, 0.0, drill_z_center)),
        hardware_material,
        rotation=rotation,
        vertices=16,
        radius_tip=config.drill_tip_radius,
    )
    created.append(drill_cone)

    # Spiral Pick Teeth (12 tungsten carbide picks welded in sockets)
    for i in range(12):
        frac = i / 12.0
        tz = drill_z_start + frac * config.drill_length * 0.90
        tang = frac * 4.0 * math.pi
        rad = config.drill_base_radius * (1.0 - frac * 0.65) + 0.008
        px = math.cos(tang) * rad
        py = math.sin(tang) * rad

        pick = rmk.make_box(
            f"{prefix}_CarbidePick_{i}",
            (0.018, 0.016, 0.022),
            _transform_world(config.center, frame, (px, py, tz)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.append(pick)

    # Center Pilot Spear Bit with Chisel Tip
    pilot_tip = _make_cone(
        f"{prefix}_PilotSpearBit",
        config.drill_tip_radius * 0.85,
        0.065,
        _transform_world(config.center, frame, (0.0, 0.0, drill_z_start + config.drill_length + 0.03)),
        hardware_material,
        rotation=rotation,
        vertices=8,
        radius_tip=0.002,
    )
    created.append(pilot_tip)

    if hardflow_bevel:
        for obj in (base_plate, gearbox, shroud):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = EXCAVATION_DRILL_CARBIDE_BORER_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"

    return created


# =========================================================================
# Preset 2: Articulated Hydraulic Docking Clamp (Sponsons - Contemporary)
# =========================================================================

@dataclass(frozen=True)
class DockingClampHydraulicArmConfig:
    """Dimensions and placement for an articulated heavy-duty hydraulic docking clamp."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    base_plate_size: tuple[float, float, float] = (0.32, 0.24, 0.024)
    arm_length: float = 0.280
    arm_width: float = 0.075
    arm_thickness: float = 0.048
    jaw_opening_deg: float = 32.0
    jaw_reach: float = 0.160
    cylinder_radius: float = 0.028
    fastener_radius: float = 0.007
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.base_plate_size, self.arm_length,
                  self.arm_width, self.arm_thickness, self.jaw_opening_deg, self.jaw_reach,
                  self.cylinder_radius, self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("DockingClampHydraulicArmConfig parameters must be finite")
        if min(*self.base_plate_size, self.arm_length, self.arm_width, self.arm_thickness,
               self.jaw_opening_deg, self.jaw_reach, self.cylinder_radius, self.fastener_radius) <= 0.0:
            raise ValueError("DockingClampHydraulicArmConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("DockingClampHydraulicArmConfig normal must not be zero")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_docking_clamp_hydraulic_arm_preset(
    *,
    prefix: str,
    config: DockingClampHydraulicArmConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build an articulated heavy-duty hydraulic docking clamp module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    bw, bd, bh = config.base_plate_size

    # 1. Base Mounting Plate with 4 Corner Fasteners
    base_plate = rmk.make_box(
        f"{prefix}_BasePlate",
        config.base_plate_size,
        _transform_world(config.center, frame, (0.0, 0.0, bh * 0.5)),
        hardware_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(base_plate)

    for xs in (-bw * 0.38, bw * 0.38):
        for ys in (-bd * 0.36, bd * 0.36):
            bolt = rmk.make_cylinder(
                f"{prefix}_AnchorBolt_{xs:.2f}_{ys:.2f}",
                config.fastener_radius,
                0.008,
                _transform_world(config.center, frame, (xs, ys, bh + 0.003)),
                hardware_material,
                rotation=rotation,
                vertices=6,
            )
            created.append(bolt)

    # 2. Emergency Pyrotechnic Release Collar
    pyro_collar = rmk.make_cylinder(
        f"{prefix}_PyroReleaseCollar",
        0.085,
        0.020,
        _transform_world(config.center, frame, (0.0, 0.0, bh + 0.010)),
        hardware_material,
        rotation=rotation,
        vertices=12,
        bevel=0.003,
    )
    created.append(pyro_collar)

    # 3. Heavy Double-Ear Pivot Clevis
    clevis_z = bh + 0.020 + 0.040
    clevis_base = rmk.make_box(
        f"{prefix}_ClevisMount",
        (0.120, 0.090, 0.060),
        _transform_world(config.center, frame, (0.0, 0.0, clevis_z)),
        armor_material,
        bevel=0.004,
        rotation=rotation,
    )
    created.append(clevis_base)

    clevis_pin = rmk.make_cylinder(
        f"{prefix}_MainPivotPin",
        0.018,
        0.130,
        _transform_world(config.center, frame, (0.0, 0.0, clevis_z + 0.015)),
        hardware_material,
        rotation=(frame.to_matrix() @ Matrix.Rotation(math.radians(90.0), 3, "X")).to_euler(),
        vertices=12,
    )
    created.append(clevis_pin)

    # 4. Cantilever Twin-Beam Clamp Arm with Lightening Cutouts
    arm_center_z = clevis_z + config.arm_length * 0.5
    main_arm = rmk.make_box(
        f"{prefix}_CantileverArm",
        (config.arm_width, config.arm_thickness, config.arm_length),
        _transform_world(config.center, frame, (0.0, 0.0, arm_center_z)),
        armor_material,
        bevel=0.005,
        rotation=rotation,
    )
    created.append(main_arm)

    # 5. Dual Opposing Hydraulic Clamping Actuators
    actuator_z = clevis_z + config.arm_length * 0.45
    for side in (-1.0, 1.0):
        cyl = rmk.make_cylinder(
            f"{prefix}_HydraulicCylinder_{side}",
            config.cylinder_radius,
            0.140,
            _transform_world(config.center, frame, (side * 0.065, 0.0, actuator_z)),
            hardware_material,
            rotation=rotation,
            vertices=12,
            bevel=0.003,
        )
        created.append(cyl)

        piston_rod = rmk.make_cylinder(
            f"{prefix}_ChromePistonRod_{side}",
            config.cylinder_radius * 0.52,
            0.080,
            _transform_world(config.center, frame, (side * 0.065, 0.0, actuator_z + 0.090)),
            hardware_material,
            rotation=rotation,
            vertices=10,
        )
        created.append(piston_rod)

    # 6. Twin Articulated Cast Grapple Jaws with Serrated Steel Teeth Pads
    jaw_pivot_z = clevis_z + config.arm_length + 0.020
    jaw_ang = math.radians(config.jaw_opening_deg)

    for side in (-1.0, 1.0):
        jaw_rot = rotation.to_matrix() @ Matrix.Rotation(side * jaw_ang, 3, "Y")
        jaw_x = side * 0.055
        jaw = rmk.make_box(
            f"{prefix}_GrappleJaw_{side}",
            (0.038, 0.042, config.jaw_reach),
            _transform_world(config.center, frame, (jaw_x, 0.0, jaw_pivot_z + config.jaw_reach * 0.45)),
            armor_material,
            bevel=0.004,
            rotation=jaw_rot.to_euler(),
        )
        created.append(jaw)

        # Serrated Teeth Pad
        teeth_pad = rmk.make_box(
            f"{prefix}_SerratedTeethPad_{side}",
            (0.012, 0.036, config.jaw_reach * 0.70),
            _transform_world(config.center, frame, (jaw_x - side * 0.015, 0.0, jaw_pivot_z + config.jaw_reach * 0.45)),
            hardware_material,
            bevel=0.001,
            rotation=jaw_rot.to_euler(),
        )
        created.append(teeth_pad)

    # 7. Central Optronic Docking Guide Pod with Laser Rangefinder Visor
    guide_z = jaw_pivot_z + 0.010
    guide_pod = rmk.make_box(
        f"{prefix}_OptronicGuidePod",
        (0.048, 0.042, 0.040),
        _transform_world(config.center, frame, (0.0, 0.0, guide_z)),
        armor_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(guide_pod)

    laser_lens = rmk.make_cylinder(
        f"{prefix}_LaserRangefinderLens",
        0.012,
        0.008,
        _transform_world(config.center, frame, (0.0, 0.0, guide_z + 0.022)),
        accent_material or hardware_material,
        rotation=rotation,
        vertices=8,
    )
    created.append(laser_lens)

    if hardflow_bevel:
        for obj in (base_plate, main_arm, clevis_base):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = DOCKING_CLAMP_HYDRAULIC_ARM_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"

    return created


# =========================================================================
# Preset 3: Tactical APU Turbogenerator Sponson (Sponsons - Contemporary)
# =========================================================================

@dataclass(frozen=True)
class ApuTurbogeneratorSponsonConfig:
    """Dimensions and placement for a tactical APU turbogenerator sponson pod."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    cowl_size: tuple[float, float, float] = (0.42, 0.32, 0.30)
    intake_width: float = 0.160
    intake_height: float = 0.110
    exhaust_diameter: float = 0.075
    exhaust_length: float = 0.120
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.cowl_size, self.intake_width,
                  self.intake_height, self.exhaust_diameter, self.exhaust_length,
                  self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("ApuTurbogeneratorSponsonConfig parameters must be finite")
        if min(*self.cowl_size, self.intake_width, self.intake_height,
               self.exhaust_diameter, self.exhaust_length, self.fastener_radius) <= 0.0:
            raise ValueError("ApuTurbogeneratorSponsonConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("ApuTurbogeneratorSponsonConfig normal must not be zero")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_apu_turbogenerator_sponson_preset(
    *,
    prefix: str,
    config: ApuTurbogeneratorSponsonConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a tactical APU turbogenerator sponson pod module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    cw, cd, ch = config.cowl_size

    # 1. Armored Sponson Shell with 4 Neoprene Isolation Pucks
    for xs in (-cw * 0.40, cw * 0.40):
        for ys in (-cd * 0.38, cd * 0.38):
            puck = rmk.make_cylinder(
                f"{prefix}_IsolationPuck_{xs:.2f}_{ys:.2f}",
                0.022,
                0.016,
                _transform_world(config.center, frame, (xs, ys, 0.008)),
                hardware_material,
                rotation=rotation,
                vertices=10,
            )
            created.append(puck)

    cowl_body = rmk.make_box(
        f"{prefix}_CowlBody",
        (cw, cd, ch),
        _transform_world(config.center, frame, (0.0, 0.0, 0.016 + ch * 0.5)),
        armor_material,
        bevel=0.006,
        rotation=rotation,
    )
    created.append(cowl_body)

    # 2. Cyclonic Particulate Intake Cowl with Horizontal Debris Louvers
    intake_z = 0.016 + ch * 0.65
    intake_housing = rmk.make_box(
        f"{prefix}_IntakeHousing",
        (config.intake_width + 0.024, 0.045, config.intake_height + 0.024),
        _transform_world(config.center, frame, (-cw * 0.20, cd * 0.5 + 0.022, intake_z)),
        armor_material,
        bevel=0.004,
        rotation=rotation,
    )
    created.append(intake_housing)

    for i in range(4):
        lz = intake_z - config.intake_height * 0.35 + i * (config.intake_height * 0.24)
        louver = rmk.make_box(
            f"{prefix}_IntakeLouver_{i}",
            (config.intake_width, 0.012, 0.008),
            _transform_world(config.center, frame, (-cw * 0.20, cd * 0.5 + 0.038, lz)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.append(louver)

    # Centrifugal Dust Scavenger Ejector Tube
    dust_ejector = rmk.make_cylinder(
        f"{prefix}_DustEjectorTube",
        0.014,
        0.040,
        _transform_world(config.center, frame, (-cw * 0.20, cd * 0.5 + 0.015, intake_z - config.intake_height * 0.55)),
        hardware_material,
        rotation=rotation,
        vertices=8,
    )
    created.append(dust_ejector)

    # 3. Hot Gas Exhaust Diffuser Duct with Thermal Blanket & Clamp Rings
    ex_z = 0.016 + ch * 0.60
    ex_x = cw * 0.24
    exhaust_pipe = rmk.make_cylinder(
        f"{prefix}_ExhaustPipe",
        config.exhaust_diameter * 0.5,
        config.exhaust_length,
        _transform_world(config.center, frame, (ex_x, -cd * 0.45, ex_z)),
        hardware_material,
        rotation=rotation,
        vertices=14,
        bevel=0.003,
    )
    created.append(exhaust_pipe)

    # Thermal Insulation Sleeve Blanket
    blanket = rmk.make_cylinder(
        f"{prefix}_ThermalInsulationBlanket",
        config.exhaust_diameter * 0.58,
        config.exhaust_length * 0.70,
        _transform_world(config.center, frame, (ex_x, -cd * 0.45, ex_z - 0.010)),
        hardware_material,
        rotation=rotation,
        vertices=14,
        bevel=0.002,
    )
    created.append(blanket)

    # 4. Flush Maintenance Hatch with Dual Rotary Compression Latches
    door = rmk.make_box(
        f"{prefix}_MaintenanceHatch",
        (cw * 0.55, 0.012, ch * 0.55),
        _transform_world(config.center, frame, (0.0, -cd * 0.5 - 0.004, 0.016 + ch * 0.45)),
        armor_material,
        bevel=0.002,
        rotation=rotation,
    )
    created.append(door)

    for i, lx in enumerate((-cw * 0.16, cw * 0.16)):
        latch = rmk.make_cylinder(
            f"{prefix}_CompressionLatch_{i}",
            0.014,
            0.010,
            _transform_world(config.center, frame, (lx, -cd * 0.5 - 0.012, 0.016 + ch * 0.45)),
            hardware_material,
            rotation=rotation,
            vertices=6,
        )
        created.append(latch)

    # 5. High-Voltage Tactical Power Terminal Box & Conduits
    term_box = rmk.make_box(
        f"{prefix}_PowerTerminalBox",
        (0.085, 0.065, 0.075),
        _transform_world(config.center, frame, (cw * 0.5 + 0.035, 0.0, 0.016 + ch * 0.35)),
        hardware_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(term_box)

    for i, cy in enumerate((-0.018, 0.018)):
        conduit = rmk.make_rod(
            f"{prefix}_HighVoltageConduit_{i}",
            _transform_world(config.center, frame, (cw * 0.5 + 0.070, cy, 0.016 + ch * 0.35)),
            _transform_world(config.center, frame, (cw * 0.40, cy, 0.020)),
            0.010,
            hardware_material,
            vertices=8,
        )
        created.append(conduit)

    if hardflow_bevel:
        for obj in (cowl_body, intake_housing, term_box):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = APU_TURBOGENERATOR_SPONSON_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"

    return created


# =========================================================================
# Preset 4: Multispectral Smoke Salvo Discharger (APS - Contemporary)
# =========================================================================

@dataclass(frozen=True)
class SmokeGrenadeSalvoDischargerConfig:
    """Dimensions and placement for a multispectral smoke salvo discharger bank."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 0.0, 1.0)
    pedestal_size: tuple[float, float, float] = (0.28, 0.22, 0.035)
    tube_caliber: float = 0.066
    tube_length: float = 0.170
    tube_count: int = 8
    fan_azimuth_spread_deg: float = 48.0
    elevation_deg: float = 38.0
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.pedestal_size, self.tube_caliber,
                  self.tube_length, self.fan_azimuth_spread_deg, self.elevation_deg,
                  self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("SmokeGrenadeSalvoDischargerConfig parameters must be finite")
        if min(*self.pedestal_size, self.tube_caliber, self.tube_length,
               self.fastener_radius) <= 0.0:
            raise ValueError("SmokeGrenadeSalvoDischargerConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("SmokeGrenadeSalvoDischargerConfig normal must not be zero")
        if self.tube_count not in (4, 6, 8):
            raise ValueError("tube_count must be 4, 6, or 8")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_smoke_grenade_salvo_discharger_preset(
    *,
    prefix: str,
    config: SmokeGrenadeSalvoDischargerConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a multispectral smoke salvo discharger bank module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    pw, pd, ph = config.pedestal_size

    # 1. Sloped Cast Pedestal Base with 4 Mounting Fasteners
    pedestal = rmk.make_box(
        f"{prefix}_PedestalBase",
        config.pedestal_size,
        _transform_world(config.center, frame, (0.0, 0.0, ph * 0.5)),
        armor_material,
        bevel=0.004,
        rotation=rotation,
    )
    created.append(pedestal)

    for xs in (-pw * 0.38, pw * 0.38):
        for ys in (-pd * 0.36, pd * 0.36):
            bolt = rmk.make_cylinder(
                f"{prefix}_PedestalBolt_{xs:.2f}_{ys:.2f}",
                config.fastener_radius,
                0.008,
                _transform_world(config.center, frame, (xs, ys, ph + 0.003)),
                hardware_material,
                rotation=rotation,
                vertices=6,
            )
            created.append(bolt)

    # Azimuth Turntable Ring & Traverse Lock Clamp
    turntable = rmk.make_cylinder(
        f"{prefix}_TurntableIndexRing",
        0.090,
        0.016,
        _transform_world(config.center, frame, (0.0, 0.0, ph + 0.008)),
        hardware_material,
        rotation=rotation,
        vertices=16,
        bevel=0.002,
    )
    created.append(turntable)

    # 2. Mortar Tube Cluster Bank (2 tiers of 4 tubes = 8 tubes total)
    cols = 4 if config.tube_count == 8 else config.tube_count // 2
    rows = 2 if config.tube_count in (6, 8) else 1
    elev_rad = math.radians(config.elevation_deg)
    half_fan = math.radians(config.fan_azimuth_spread_deg * 0.5)

    tube_rad = config.tube_caliber * 0.5

    for row in range(rows):
        rz = ph + 0.035 + row * (config.tube_caliber * 1.15)
        ry_offset = -0.025 + row * 0.040

        for col in range(cols):
            # Compute azimuth angle across fan
            col_frac = (col / (cols - 1)) if cols > 1 else 0.5
            azim_ang = -half_fan + col_frac * (2.0 * half_fan)

            # Combined rotation: pitch up by elev_rad, yaw by azim_ang
            tube_rot_mat = (
                rotation.to_matrix()
                @ Matrix.Rotation(azim_ang, 3, "Z")
                @ Matrix.Rotation(-elev_rad, 3, "X")
            )
            tube_euler = tube_rot_mat.to_euler()

            rx = (-0.080 + col * 0.054)
            t_center = _transform_world(config.center, frame, (rx, ry_offset, rz))

            tube = rmk.make_cylinder(
                f"{prefix}_SmokeTube_r{row}_c{col}",
                tube_rad,
                config.tube_length,
                t_center,
                armor_material,
                rotation=tube_euler,
                vertices=12,
                bevel=0.002,
            )
            created.append(tube)

            # Flared Protective Muzzle Lip
            muzzle_lip = rmk.make_cylinder(
                f"{prefix}_MuzzleLip_r{row}_c{col}",
                tube_rad * 1.16,
                0.014,
                _transform_world(config.center, frame, (rx, ry_offset + 0.055, rz + 0.070)),
                hardware_material,
                rotation=tube_euler,
                vertices=12,
            )
            created.append(muzzle_lip)

            # Rubber Blowout Burst-Cap
            cap = rmk.make_cylinder(
                f"{prefix}_BlowoutCap_r{row}_c{col}",
                tube_rad * 0.96,
                0.006,
                _transform_world(config.center, frame, (rx, ry_offset + 0.060, rz + 0.076)),
                hardware_material,
                rotation=tube_euler,
                vertices=10,
            )
            created.append(cap)

            # Rear Breech Ignition Plug
            plug = rmk.make_cylinder(
                f"{prefix}_BreechPlug_r{row}_c{col}",
                tube_rad * 0.45,
                0.016,
                _transform_world(config.center, frame, (rx, ry_offset - 0.055, rz - 0.070)),
                hardware_material,
                rotation=tube_euler,
                vertices=8,
            )
            created.append(plug)

    # 3. Armored Electrical Firing Conduit & Junction Box
    jbox = rmk.make_box(
        f"{prefix}_JunctionBox",
        (0.065, 0.045, 0.038),
        _transform_world(config.center, frame, (0.0, -pd * 0.35, ph + 0.030)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    created.append(jbox)

    status_led = rmk.make_cylinder(
        f"{prefix}_StatusLed",
        0.005,
        0.006,
        _transform_world(config.center, frame, (0.0, -pd * 0.35, ph + 0.052)),
        accent_material or hardware_material,
        rotation=rotation,
        vertices=8,
    )
    created.append(status_led)

    if hardflow_bevel:
        for obj in (pedestal, turntable, jbox):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = SMOKE_GRENADE_SALVO_DISCHARGER_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "APS"

    return created


# =========================================================================
# Preset 5: Quantum Plasma Excavation Torch (Sponsons - Futuristic)
# =========================================================================

@dataclass(frozen=True)
class PlasmaExcavationTorchConfig:
    """Dimensions and placement for a futuristic quantum plasma excavation thermal lance."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    housing_size: tuple[float, float, float] = (0.32, 0.26, 0.20)
    lance_barrel_radius: float = 0.062
    lance_barrel_length: float = 0.280
    confinement_ring_count: int = 3
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.housing_size, self.lance_barrel_radius,
                  self.lance_barrel_length, self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("PlasmaExcavationTorchConfig parameters must be finite")
        if min(*self.housing_size, self.lance_barrel_radius, self.lance_barrel_length,
               self.fastener_radius) <= 0.0:
            raise ValueError("PlasmaExcavationTorchConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("PlasmaExcavationTorchConfig normal must not be zero")
        if self.confinement_ring_count < 2:
            raise ValueError("confinement_ring_count must be at least 2")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_plasma_excavation_torch_preset(
    *,
    prefix: str,
    config: PlasmaExcavationTorchConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic quantum plasma excavation thermal lance module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    hw, hd, hh = config.housing_size

    # 1. Faceted Aerodynamic Sponson Housing with Titanium Skid Plate
    skid_plate = rmk.make_box(
        f"{prefix}_SkidPlate",
        (hw, hd, 0.016),
        _transform_world(config.center, frame, (0.0, 0.0, 0.008)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    created.append(skid_plate)

    housing_body = rmk.make_box(
        f"{prefix}_HousingBody",
        (hw * 0.90, hd * 0.85, hh),
        _transform_world(config.center, frame, (0.0, 0.0, 0.016 + hh * 0.5)),
        armor_material,
        bevel=0.006,
        rotation=rotation,
    )
    created.append(housing_body)

    # 2. Central Magnetic Confinement Plasma Barrel with Internal Swirl Vanes
    barrel_z = 0.016 + hh * 0.5
    barrel = rmk.make_cylinder(
        f"{prefix}_PlasmaConfinementBarrel",
        config.lance_barrel_radius,
        config.lance_barrel_length,
        _transform_world(config.center, frame, (0.0, 0.0, barrel_z + config.lance_barrel_length * 0.35)),
        hardware_material,
        rotation=rotation,
        vertices=16,
        bevel=0.003,
    )
    created.append(barrel)

    # Refractory Thermal Lance Nozzle with Vortex Tips
    nozzle_z = barrel_z + config.lance_barrel_length * 0.85
    nozzle = _make_cone(
        f"{prefix}_ThermalLanceNozzle",
        config.lance_barrel_radius * 1.15,
        0.055,
        _transform_world(config.center, frame, (0.0, 0.0, nozzle_z)),
        accent_material or hardware_material,
        rotation=rotation,
        vertices=12,
        radius_tip=config.lance_barrel_radius * 0.65,
    )
    created.append(nozzle)

    # 3. Stepped Concentric Superconducting Magnetic Rings
    ring_spacing = (config.lance_barrel_length * 0.55) / config.confinement_ring_count
    for i in range(config.confinement_ring_count):
        rz = barrel_z + 0.030 + i * ring_spacing
        ring = rmk.make_torus(
            f"{prefix}_SuperconductingRing_{i}",
            _transform_world(config.center, frame, (0.0, 0.0, rz)),
            major_radius=config.lance_barrel_radius + 0.024,
            minor_radius=0.010,
            material=accent_material or hardware_material,
            rotation=rotation,
            major_segments=16,
            minor_segments=8,
        )
        created.append(ring)

    # 4. Dual Vacuum-Jacketed Cryo Coolant Feed Lines
    for side in (-1.0, 1.0):
        cryo = rmk.make_rod(
            f"{prefix}_CryoCoolantLine_{side}",
            _transform_world(config.center, frame, (side * (hw * 0.35), 0.0, 0.016 + hh * 0.30)),
            _transform_world(config.center, frame, (side * (config.lance_barrel_radius + 0.018), 0.0, barrel_z + 0.040)),
            0.012,
            hardware_material,
            vertices=8,
        )
        created.append(cryo)

    # 5. Diagnostic Luminous Telemetry Waveguide Strip
    diag_strip = rmk.make_box(
        f"{prefix}_DiagnosticWaveguide",
        (0.014, hd * 0.60, 0.008),
        _transform_world(config.center, frame, (0.0, 0.0, 0.016 + hh + 0.004)),
        accent_material or hardware_material,
        bevel=0.001,
        rotation=rotation,
    )
    created.append(diag_strip)

    if hardflow_bevel:
        for obj in (skid_plate, housing_body):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = PLASMA_EXCAVATION_TORCH_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"

    return created


# =========================================================================
# Preset 6: Zero-G Magnetic Tractor Docking Clamp (Sponsons - Futuristic)
# =========================================================================

@dataclass(frozen=True)
class MagneticTractorDockingClampConfig:
    """Dimensions and placement for a futuristic zero-G magnetic tractor docking clamp."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    bracket_size: tuple[float, float, float] = (0.34, 0.28, 0.025)
    tractor_dish_radius: float = 0.088
    tractor_dish_depth: float = 0.035
    pole_shoe_radius: float = 0.030
    capacitor_bank_size: tuple[float, float, float] = (0.060, 0.075, 0.120)
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.bracket_size, self.tractor_dish_radius,
                  self.tractor_dish_depth, self.pole_shoe_radius, *self.capacitor_bank_size,
                  self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("MagneticTractorDockingClampConfig parameters must be finite")
        if min(*self.bracket_size, self.tractor_dish_radius, self.tractor_dish_depth,
               self.pole_shoe_radius, *self.capacitor_bank_size, self.fastener_radius) <= 0.0:
            raise ValueError("MagneticTractorDockingClampConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("MagneticTractorDockingClampConfig normal must not be zero")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_magnetic_tractor_docking_clamp_preset(
    *,
    prefix: str,
    config: MagneticTractorDockingClampConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic zero-G magnetic tractor docking clamp module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    bw, bd, bh = config.bracket_size

    # 1. Faceted Low-Observable Mounting Bracket
    bracket = rmk.make_box(
        f"{prefix}_MountingBracket",
        config.bracket_size,
        _transform_world(config.center, frame, (0.0, 0.0, bh * 0.5)),
        armor_material,
        bevel=0.004,
        rotation=rotation,
    )
    created.append(bracket)

    for xs in (-bw * 0.40, bw * 0.40):
        for ys in (-bd * 0.38, bd * 0.38):
            bolt = rmk.make_cylinder(
                f"{prefix}_AnchorStud_{xs:.2f}_{ys:.2f}",
                config.fastener_radius,
                0.008,
                _transform_world(config.center, frame, (xs, ys, bh + 0.003)),
                hardware_material,
                rotation=rotation,
                vertices=6,
            )
            created.append(bolt)

    # 2. Central Parabolic Magnetic/Gravimetric Tractor Projector Dish
    dish_z = bh + config.tractor_dish_depth * 0.5 + 0.010
    dish = rmk.make_cylinder(
        f"{prefix}_TractorProjectorDish",
        config.tractor_dish_radius,
        config.tractor_dish_depth,
        _transform_world(config.center, frame, (0.0, 0.0, dish_z)),
        hardware_material,
        rotation=rotation,
        vertices=16,
        bevel=0.004,
    )
    created.append(dish)

    focus_node = _make_sphere(
        f"{prefix}_FocusEmitterNode",
        config.tractor_dish_radius * 0.32,
        _transform_world(config.center, frame, (0.0, 0.0, dish_z + config.tractor_dish_depth * 0.6)),
        accent_material or hardware_material,
    )
    created.append(focus_node)

    # 3. 4 Cross-Configured Superconducting Magnetic Pole Shoes
    pole_dist = config.tractor_dish_radius + 0.038
    for i, (dx, dy) in enumerate(((pole_dist, 0.0), (-pole_dist, 0.0), (0.0, pole_dist), (0.0, -pole_dist))):
        shoe = rmk.make_cylinder(
            f"{prefix}_MagneticPoleShoe_{i}",
            config.pole_shoe_radius,
            0.028,
            _transform_world(config.center, frame, (dx, dy, bh + 0.014)),
            hardware_material,
            rotation=rotation,
            vertices=12,
            bevel=0.002,
        )
        created.append(shoe)

        bezel = rmk.make_torus(
            f"{prefix}_CeramicBezel_{i}",
            _transform_world(config.center, frame, (dx, dy, bh + 0.024)),
            major_radius=config.pole_shoe_radius + 0.006,
            minor_radius=0.004,
            material=accent_material or hardware_material,
            rotation=rotation,
            major_segments=12,
            minor_segments=6,
        )
        created.append(bezel)

    # 4. Twin High-Capacity Pulse Capacitor Banks
    cw, cd, ch = config.capacitor_bank_size
    for side in (-1.0, 1.0):
        cap_bank = rmk.make_box(
            f"{prefix}_CapacitorBank_{side}",
            config.capacitor_bank_size,
            _transform_world(config.center, frame, (side * (bw * 0.34), 0.0, bh + ch * 0.5)),
            armor_material,
            bevel=0.003,
            rotation=rotation,
        )
        created.append(cap_bank)

    # 5. Phased Laser Alignment Guidance Array (4 corner emitters)
    for xs in (-bw * 0.36, bw * 0.36):
        for ys in (-bd * 0.34, bd * 0.34):
            laser = rmk.make_cylinder(
                f"{prefix}_AlignmentLaser_{xs:.2f}_{ys:.2f}",
                0.007,
                0.010,
                _transform_world(config.center, frame, (xs, ys, bh + 0.006)),
                accent_material or hardware_material,
                rotation=rotation,
                vertices=8,
            )
            created.append(laser)

    if hardflow_bevel:
        for obj in (bracket, dish):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = MAGNETIC_TRACTOR_DOCKING_CLAMP_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"

    return created


# =========================================================================
# Preset 7: Kinetic & Graviton Recoil Anchor Pod (Sponsons - Futuristic)
# =========================================================================

@dataclass(frozen=True)
class GravitonRecoilAnchorConfig:
    """Dimensions and placement for a futuristic graviton recoil anchor pod."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    housing_size: tuple[float, float, float] = (0.36, 0.28, 0.22)
    spike_radius: float = 0.042
    spike_length: float = 0.260
    scissor_arm_length: float = 0.160
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.housing_size, self.spike_radius,
                  self.spike_length, self.scissor_arm_length, self.fastener_radius,
                  self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("GravitonRecoilAnchorConfig parameters must be finite")
        if min(*self.housing_size, self.spike_radius, self.spike_length,
               self.scissor_arm_length, self.fastener_radius) <= 0.0:
            raise ValueError("GravitonRecoilAnchorConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("GravitonRecoilAnchorConfig normal must not be zero")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_graviton_recoil_anchor_preset(
    *,
    prefix: str,
    config: GravitonRecoilAnchorConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic graviton recoil anchor pod module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    hw, hd, hh = config.housing_size

    # 1. Sloped Armored Housing with Internal Scissor Recess Bay
    housing_body = rmk.make_box(
        f"{prefix}_AnchorHousing",
        config.housing_size,
        _transform_world(config.center, frame, (0.0, 0.0, hh * 0.5)),
        armor_material,
        bevel=0.005,
        rotation=rotation,
    )
    created.append(housing_body)

    # 2. Articulated Scissor Linkage Arms
    for side in (-1.0, 1.0):
        scissor = rmk.make_box(
            f"{prefix}_ScissorLinkage_{side}",
            (0.022, 0.032, config.scissor_arm_length),
            _transform_world(config.center, frame, (side * 0.075, 0.0, hh * 0.45)),
            hardware_material,
            bevel=0.003,
            rotation=rotation,
        )
        created.append(scissor)

        pivot_pin = rmk.make_cylinder(
            f"{prefix}_ScissorPin_{side}",
            0.012,
            0.038,
            _transform_world(config.center, frame, (side * 0.075, 0.0, hh * 0.45 + config.scissor_arm_length * 0.40)),
            hardware_material,
            rotation=(frame.to_matrix() @ Matrix.Rotation(math.radians(90.0), 3, "Y")).to_euler(),
            vertices=8,
        )
        created.append(pivot_pin)

    # 3. Fluted Tungsten Ground Penetrator Spike with Electromagnetic Coupling Coil
    spike_z = hh + config.spike_length * 0.45
    spike = _make_cone(
        f"{prefix}_TungstenGroundSpike",
        config.spike_radius,
        config.spike_length,
        _transform_world(config.center, frame, (0.0, 0.0, spike_z)),
        hardware_material,
        rotation=rotation,
        vertices=12,
        radius_tip=0.003,
    )
    created.append(spike)

    # Coaxial Induction Coil Collar
    coil = rmk.make_torus(
        f"{prefix}_ElectromagneticCoil",
        _transform_world(config.center, frame, (0.0, 0.0, hh + 0.040)),
        major_radius=config.spike_radius + 0.018,
        minor_radius=0.008,
        material=accent_material or hardware_material,
        rotation=rotation,
        major_segments=16,
        minor_segments=8,
    )
    created.append(coil)

    # 4. Shock-Absorption Buffer Piston
    piston = rmk.make_cylinder(
        f"{prefix}_RecoilBufferPiston",
        config.spike_radius * 0.65,
        0.090,
        _transform_world(config.center, frame, (0.0, 0.0, hh * 0.70)),
        hardware_material,
        rotation=rotation,
        vertices=12,
        bevel=0.002,
    )
    created.append(piston)

    # 5. Twin Directional Energy Bleed Exhaust Louvers
    for side in (-1.0, 1.0):
        vent = rmk.make_box(
            f"{prefix}_EnergyBleedVent_{side}",
            (0.016, hd * 0.45, 0.045),
            _transform_world(config.center, frame, (side * (hw * 0.48), 0.0, hh * 0.65)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.append(vent)

    if hardflow_bevel:
        for obj in (housing_body,):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = GRAVITON_RECOIL_ANCHOR_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"

    return created


# =========================================================================
# Preset 8: Optronic Recon & Radar Sensor Mast (APS - Futuristic)
# =========================================================================

@dataclass(frozen=True)
class OptronicReconSensorMastConfig:
    """Dimensions and placement for a futuristic optronic recon & radar sensor mast pod."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 0.0, 1.0)
    well_size: tuple[float, float, float] = (0.30, 0.24, 0.030)
    mast_radius: float = 0.038
    mast_height: float = 0.180
    sensor_head_radius: float = 0.065
    radar_disc_radius: float = 0.075
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.well_size, self.mast_radius,
                  self.mast_height, self.sensor_head_radius, self.radar_disc_radius,
                  self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("OptronicReconSensorMastConfig parameters must be finite")
        if min(*self.well_size, self.mast_radius, self.mast_height,
               self.sensor_head_radius, self.radar_disc_radius, self.fastener_radius) <= 0.0:
            raise ValueError("OptronicReconSensorMastConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("OptronicReconSensorMastConfig normal must not be zero")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_optronic_recon_sensor_mast_preset(
    *,
    prefix: str,
    config: OptronicReconSensorMastConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic optronic recon & radar sensor mast pod module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    ww, wd, wh = config.well_size

    # 1. Faceted Mast Well with Recessed Aperture Flange
    well = rmk.make_box(
        f"{prefix}_MastWellHousing",
        config.well_size,
        _transform_world(config.center, frame, (0.0, 0.0, wh * 0.5)),
        armor_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(well)

    for xs in (-ww * 0.38, ww * 0.38):
        for ys in (-wd * 0.36, wd * 0.36):
            bolt = rmk.make_cylinder(
                f"{prefix}_WellFastener_{xs:.2f}_{ys:.2f}",
                config.fastener_radius,
                0.008,
                _transform_world(config.center, frame, (xs, ys, wh + 0.003)),
                hardware_material,
                rotation=rotation,
                vertices=6,
            )
            created.append(bolt)

    # Sliding Motorized Aperture Visor Doors
    for side in (-1.0, 1.0):
        door = rmk.make_box(
            f"{prefix}_VisorDoor_{side}",
            (ww * 0.38, wd * 0.32, 0.008),
            _transform_world(config.center, frame, (side * ww * 0.22, 0.0, wh + 0.004)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.append(door)

    # 2. Hexagonal Segmented Telescopic Mast
    mast_z = wh + config.mast_height * 0.5
    mast = rmk.make_cylinder(
        f"{prefix}_TelescopicMast",
        config.mast_radius,
        config.mast_height,
        _transform_world(config.center, frame, (0.0, 0.0, mast_z)),
        hardware_material,
        rotation=rotation,
        vertices=6,
        bevel=0.002,
    )
    created.append(mast)

    # 3. 2-Axis Gyro-Stabilized Sensor Turret Sphere
    head_z = wh + config.mast_height + config.sensor_head_radius * 0.85
    sensor_head = _make_sphere(
        f"{prefix}_SensorTurretHead",
        config.sensor_head_radius,
        _transform_world(config.center, frame, (0.0, 0.0, head_z)),
        armor_material,
    )
    created.append(sensor_head)

    # Optical Apertures: FLIR Lens & TV Visor
    flir_rot = (frame.to_matrix() @ Matrix.Rotation(math.radians(90.0), 3, "X")).to_euler()
    flir_lens = rmk.make_cylinder(
        f"{prefix}_ThermalFlirLens",
        config.sensor_head_radius * 0.35,
        0.016,
        _transform_world(config.center, frame, (0.0, config.sensor_head_radius * 0.80, head_z)),
        accent_material or hardware_material,
        rotation=flir_rot,
        vertices=10,
    )
    created.append(flir_lens)

    laser_aperture = rmk.make_cylinder(
        f"{prefix}_LaserDesignatorAperture",
        config.sensor_head_radius * 0.18,
        0.012,
        _transform_world(config.center, frame, (config.sensor_head_radius * 0.40, config.sensor_head_radius * 0.75, head_z)),
        accent_material or hardware_material,
        rotation=flir_rot,
        vertices=8,
    )
    created.append(laser_aperture)

    # 4. Millimeter-Wave Phased Array Radar Disc with Gold Waveguide Feed
    radar_z = head_z + config.sensor_head_radius + 0.020
    radar_disc = rmk.make_cylinder(
        f"{prefix}_RadarDisc",
        config.radar_disc_radius,
        0.012,
        _transform_world(config.center, frame, (0.0, 0.0, radar_z)),
        hardware_material,
        rotation=rotation,
        vertices=16,
        bevel=0.002,
    )
    created.append(radar_disc)

    waveguide = rmk.make_cylinder(
        f"{prefix}_WaveguideFeedHorn",
        0.008,
        0.018,
        _transform_world(config.center, frame, (0.0, 0.0, radar_z + 0.012)),
        accent_material or hardware_material,
        rotation=rotation,
        vertices=8,
    )
    created.append(waveguide)

    if hardflow_bevel:
        for obj in (well,):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = OPTRONIC_RECON_SENSOR_MAST_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "APS"

    return created


# =========================================================================
# Preset 9: CIWS Rotary Point Defense Sponson (Sponsons - Contemporary)
# =========================================================================

@dataclass(frozen=True)
class CiwsRotaryPointDefenseConfig:
    """Dimensions and placement for a 20mm rotary Gatling CIWS sponson."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    turntable_radius: float = 0.120
    turntable_thickness: float = 0.024
    cradle_size: tuple[float, float, float] = (0.24, 0.22, 0.18)
    barrel_length: float = 0.380
    barrel_radius: float = 0.012
    rotor_radius: float = 0.042
    elevation_deg: float = 15.0
    radome_radius: float = 0.048
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, self.turntable_radius, self.turntable_thickness,
                  *self.cradle_size, self.barrel_length, self.barrel_radius, self.rotor_radius,
                  self.elevation_deg, self.radome_radius, self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("CiwsRotaryPointDefenseConfig parameters must be finite")
        if min(self.turntable_radius, self.turntable_thickness, *self.cradle_size,
               self.barrel_length, self.barrel_radius, self.rotor_radius,
               self.radome_radius, self.fastener_radius) <= 0.0:
            raise ValueError("CiwsRotaryPointDefenseConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("CiwsRotaryPointDefenseConfig normal must not be zero")
        if not (-20.0 <= self.elevation_deg <= 85.0):
            raise ValueError("elevation_deg must be within [-20.0, 85.0] degrees")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_ciws_rotary_point_defense_preset(
    *,
    prefix: str,
    config: CiwsRotaryPointDefenseConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a realistic 20mm rotary Gatling CIWS sponson module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    tt_r = config.turntable_radius
    tt_h = config.turntable_thickness
    cw, cd, ch = config.cradle_size

    # 1. Stepped Azimuth Turntable with 8 Mounting Bolts
    turntable = rmk.make_cylinder(
        f"{prefix}_TurntableBase",
        tt_r,
        tt_h,
        _transform_world(config.center, frame, (0.0, 0.0, tt_h * 0.5)),
        hardware_material,
        rotation=rotation,
        vertices=16,
        bevel=0.003,
    )
    created.append(turntable)

    for i in range(8):
        ang = i * (math.pi / 4.0)
        bx = math.cos(ang) * (tt_r * 0.86)
        by = math.sin(ang) * (tt_r * 0.86)
        bolt = rmk.make_cylinder(
            f"{prefix}_FlangeBolt_{i}",
            config.fastener_radius,
            0.008,
            _transform_world(config.center, frame, (bx, by, tt_h + 0.003)),
            hardware_material,
            rotation=rotation,
            vertices=6,
        )
        created.append(bolt)

    # 2. Cradle Cheeks (Left and Right) with Elevation Trunnion Hubs
    cradle_z = tt_h + ch * 0.5
    for side in (-1.0, 1.0):
        cheek = rmk.make_box(
            f"{prefix}_CradleCheek_{side}",
            (0.024, cd * 0.90, ch),
            _transform_world(config.center, frame, (side * (cw * 0.5 - 0.012), 0.0, cradle_z)),
            armor_material,
            bevel=0.004,
            rotation=rotation,
        )
        created.append(cheek)

        trunnion_hub = rmk.make_cylinder(
            f"{prefix}_TrunnionHub_{side}",
            0.038,
            0.012,
            _transform_world(config.center, frame, (side * (cw * 0.5 + 0.004), 0.0, cradle_z + 0.02)),
            hardware_material,
            rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'Y')).to_euler(),
            vertices=12,
            bevel=0.002,
        )
        created.append(trunnion_hub)

    # 3. Elevation Degree Sector Arc
    arc_marker = rmk.make_box(
        f"{prefix}_ElevationQuadrantArc",
        (0.006, 0.055, 0.055),
        _transform_world(config.center, frame, (cw * 0.5 + 0.010, -0.02, cradle_z + 0.02)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    created.append(arc_marker)

    # 4. Elevation Hydraulic Ram
    ram_cyl = rmk.make_cylinder(
        f"{prefix}_ElevationRamCylinder",
        0.018,
        0.090,
        _transform_world(config.center, frame, (0.0, -cd * 0.28, tt_h + 0.045)),
        hardware_material,
        rotation=rotation,
        vertices=10,
        bevel=0.002,
    )
    created.append(ram_cyl)

    ram_rod = rmk.make_cylinder(
        f"{prefix}_ElevationRamRod",
        0.010,
        0.075,
        _transform_world(config.center, frame, (0.0, -cd * 0.28, tt_h + 0.095)),
        accent_material or hardware_material,
        rotation=rotation,
        vertices=8,
    )
    created.append(ram_rod)

    # 5. Rotary Gatling Gun Assembly with Elevation Angle
    elev_rad = math.radians(config.elevation_deg)
    elev_rot_matrix = frame.to_matrix() @ Matrix.Rotation(elev_rad, 3, 'X')
    elev_rotation = elev_rot_matrix.to_euler()

    gun_pivot = _transform_world(config.center, frame, (0.0, 0.0, cradle_z + 0.02))

    # Center Rotor
    rotor = rmk.make_cylinder(
        f"{prefix}_GatlingRotor",
        config.rotor_radius,
        0.120,
        gun_pivot,
        hardware_material,
        rotation=(elev_rot_matrix @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
        vertices=16,
        bevel=0.003,
    )
    created.append(rotor)

    # 6 Symmetrical Barrels
    barrel_cluster_r = config.rotor_radius * 0.70
    barrel_dir = elev_rot_matrix @ Vector((0.0, 1.0, 0.0))
    for b_idx in range(6):
        b_angle = b_idx * (math.pi / 3.0)
        lx = math.cos(b_angle) * barrel_cluster_r
        lz = math.sin(b_angle) * barrel_cluster_r
        b_offset = elev_rot_matrix @ Vector((lx, config.barrel_length * 0.5, lz))
        b_loc = Vector(gun_pivot) + b_offset

        barrel = rmk.make_cylinder(
            f"{prefix}_Barrel_{b_idx}",
            config.barrel_radius,
            config.barrel_length,
            b_loc,
            hardware_material,
            rotation=(elev_rot_matrix @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
            vertices=8,
        )
        created.append(barrel)

    # Mid and Muzzle Clamp Rings
    mid_loc = Vector(gun_pivot) + elev_rot_matrix @ Vector((0.0, config.barrel_length * 0.55, 0.0))
    mid_clamp = rmk.make_cylinder(
        f"{prefix}_BarrelClamp_Mid",
        config.rotor_radius * 0.95,
        0.022,
        mid_loc,
        armor_material,
        rotation=(elev_rot_matrix @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
        vertices=16,
        bevel=0.002,
    )
    created.append(mid_clamp)

    muzzle_loc = Vector(gun_pivot) + elev_rot_matrix @ Vector((0.0, config.barrel_length, 0.0))
    muzzle_suppressor = rmk.make_cylinder(
        f"{prefix}_FlashSuppressorCage",
        config.rotor_radius * 1.02,
        0.045,
        muzzle_loc,
        hardware_material,
        rotation=(elev_rot_matrix @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
        vertices=16,
        bevel=0.003,
    )
    created.append(muzzle_suppressor)

    # 6. Articulated Linkless Feed Chute Segments
    for seg_idx in range(3):
        frac = seg_idx / 2.0
        sy = (frac - 0.5) * (cd * 0.6)
        sz = tt_h + 0.05 + frac * 0.06
        chute_seg = rmk.make_box(
            f"{prefix}_FeedChuteSegment_{seg_idx}",
            (0.042, 0.042, 0.028),
            _transform_world(config.center, frame, (-cw * 0.5 - 0.020, sy, sz)),
            hardware_material,
            bevel=0.003,
            rotation=rotation,
        )
        created.append(chute_seg)

    # 7. Rear Tracker Pylon & Spherical Radar Dome
    pylon_loc = _transform_world(config.center, frame, (0.0, -cd * 0.38, cradle_z + 0.09))
    pylon = rmk.make_box(
        f"{prefix}_TrackerPylon",
        (0.035, 0.045, 0.110),
        pylon_loc,
        armor_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(pylon)

    radome_loc = _transform_world(config.center, frame, (0.0, -cd * 0.38, cradle_z + 0.165))
    radome = _make_sphere(
        f"{prefix}_MonopulseRadome",
        config.radome_radius,
        radome_loc,
        accent_material or armor_material,
        segments=16,
        ring_count=10,
    )
    created.append(radome)

    # 8. Sockets & VFX Hierarchy
    muzzle_tip_world = Vector(gun_pivot) + elev_rot_matrix @ Vector((0.0, config.barrel_length + 0.03, 0.0))
    sock_muzzle = _make_socket("Socket_MuzzlePrimary", muzzle_tip_world, elev_rotation)
    firepoint_0 = _make_socket("FirePoint_0", muzzle_tip_world, elev_rotation)
    firepoint_0.parent = sock_muzzle
    sock_audio = _make_socket("Socket_WeaponAudio", muzzle_tip_world, elev_rotation)
    sock_smoke = _make_socket("Socket_DamageSmoke", _transform_world(config.center, frame, (0.0, 0.0, cradle_z)), rotation)
    created.extend([sock_muzzle, firepoint_0, sock_audio, sock_smoke])

    if hardflow_bevel:
        for obj in (turntable, muzzle_suppressor):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = CIWS_ROTARY_POINT_DEFENSE_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"

    return created


# =========================================================================
# Preset 10: Reinforced Fuel Drum Drop Rack (FuelDrums - Contemporary)
# =========================================================================

@dataclass(frozen=True)
class ReinforcedFuelDrumDropRackConfig:
    """Dimensions and placement for twin reinforced fuel drums in an ejectable drop rack."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    rack_size: tuple[float, float, float] = (0.68, 0.38, 0.36)
    drum_radius: float = 0.125
    drum_length: float = 0.320
    tube_radius: float = 0.011
    strap_width: float = 0.028
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.rack_size, self.drum_radius,
                  self.drum_length, self.tube_radius, self.strap_width,
                  self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("ReinforcedFuelDrumDropRackConfig parameters must be finite")
        if min(*self.rack_size, self.drum_radius, self.drum_length,
               self.tube_radius, self.strap_width, self.fastener_radius) <= 0.0:
            raise ValueError("ReinforcedFuelDrumDropRackConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("ReinforcedFuelDrumDropRackConfig normal must not be zero")
        rw, rd, rh = self.rack_size
        if (self.drum_radius * 4.0) > rw:
            raise ValueError("rack width is too small to fit dual fuel drums side-by-side")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_reinforced_fuel_drum_drop_rack_preset(
    *,
    prefix: str,
    config: ReinforcedFuelDrumDropRackConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a realistic dual reinforced auxiliary fuel drum drop rack."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    rw, rd, rh = config.rack_size
    dr = config.drum_radius
    dl = config.drum_length

    # 1. Structural Tubular Cradle Frame with Lower Skid Runners
    bulkhead = rmk.make_box(
        f"{prefix}_MountingBulkhead",
        (rw * 0.94, 0.016, rh * 0.90),
        _transform_world(config.center, frame, (0.0, -rd * 0.45, rh * 0.5)),
        hardware_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(bulkhead)

    for sx in (-1.0, 1.0):
        for sz in (rh * 0.20, rh * 0.80):
            bolt = rmk.make_cylinder(
                f"{prefix}_MountBolt_{sx}_{sz:.2f}",
                config.fastener_radius,
                0.008,
                _transform_world(config.center, frame, (sx * rw * 0.42, -rd * 0.46, sz)),
                hardware_material,
                rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
                vertices=6,
            )
            created.append(bolt)

    for side in (-1.0, 1.0):
        skid = rmk.make_box(
            f"{prefix}_SkidRunner_{side}",
            (config.tube_radius * 2.2, rd * 0.90, config.tube_radius * 2.2),
            _transform_world(config.center, frame, (side * rw * 0.38, 0.0, config.tube_radius * 1.5)),
            hardware_material,
            bevel=0.003,
            rotation=rotation,
        )
        created.append(skid)

        upright = rmk.make_box(
            f"{prefix}_CornerUpright_{side}",
            (config.tube_radius * 2.0, config.tube_radius * 2.0, rh * 0.70),
            _transform_world(config.center, frame, (side * rw * 0.45, rd * 0.35, rh * 0.40)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.append(upright)

    # 2. Twin 200L Stamped Steel Fuel Drums with Rolling Hoops
    drum_pitch = rw * 0.27
    drum_y = 0.0
    drum_z = dr + config.tube_radius * 3.0
    drum_rot = (frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler()

    for side in (-1.0, 1.0):
        dx = side * drum_pitch
        drum = rmk.make_cylinder(
            f"{prefix}_FuelDrum_{side}",
            dr,
            dl,
            _transform_world(config.center, frame, (dx, drum_y, drum_z)),
            armor_material,
            rotation=drum_rot,
            vertices=16,
            bevel=0.005,
        )
        created.append(drum)

        for hoop_offset in (-dl * 0.25, dl * 0.25):
            hoop = rmk.make_cylinder(
                f"{prefix}_RollingHoop_{side}_{hoop_offset:.2f}",
                dr + 0.006,
                0.016,
                _transform_world(config.center, frame, (dx, drum_y + hoop_offset, drum_z)),
                hardware_material,
                rotation=drum_rot,
                vertices=16,
                bevel=0.002,
            )
            created.append(hoop)

        front_y = drum_y + dl * 0.5 + 0.004
        bung = rmk.make_cylinder(
            f"{prefix}_BungCap_{side}",
            0.024,
            0.010,
            _transform_world(config.center, frame, (dx, front_y, drum_z + dr * 0.45)),
            hardware_material,
            rotation=drum_rot,
            vertices=8,
            bevel=0.002,
        )
        created.append(bung)

        vent = rmk.make_cylinder(
            f"{prefix}_VentCap_{side}",
            0.012,
            0.008,
            _transform_world(config.center, frame, (dx + 0.045, front_y, drum_z + dr * 0.45)),
            accent_material or hardware_material,
            rotation=drum_rot,
            vertices=6,
            bevel=0.001,
        )
        created.append(vent)

        for strap_offset in (-dl * 0.32, dl * 0.32):
            strap = rmk.make_box(
                f"{prefix}_TieDownStrap_{side}_{strap_offset:.2f}",
                (dr * 2.05, config.strap_width, 0.006),
                _transform_world(config.center, frame, (dx, drum_y + strap_offset, drum_z + dr + 0.004)),
                hardware_material,
                bevel=0.001,
                rotation=rotation,
            )
            created.append(strap)

            ratchet = rmk.make_box(
                f"{prefix}_StrapRatchet_{side}_{strap_offset:.2f}",
                (0.028, 0.038, 0.022),
                _transform_world(config.center, frame, (dx + side * (dr * 0.95), drum_y + strap_offset, drum_z + dr * 0.5)),
                hardware_material,
                bevel=0.002,
                rotation=rotation,
            )
            created.append(ratchet)

    # 3. High-Pressure Quick-Disconnect Fuel Transfer Manifold
    manifold_loc = _transform_world(config.center, frame, (0.0, rd * 0.22, drum_z))
    manifold = rmk.make_box(
        f"{prefix}_TransferManifoldBlock",
        (0.080, 0.065, 0.055),
        manifold_loc,
        hardware_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(manifold)

    for side in (-1.0, 1.0):
        valve = rmk.make_cylinder(
            f"{prefix}_ValveHandle_{side}",
            0.012,
            0.035,
            _transform_world(config.center, frame, (side * 0.035, rd * 0.25, drum_z + 0.030)),
            accent_material or hardware_material,
            rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'Y')).to_euler(),
            vertices=8,
        )
        created.append(valve)

        line = rmk.make_cylinder(
            f"{prefix}_BraidedFuelLine_{side}",
            0.009,
            rw * 0.22,
            _transform_world(config.center, frame, (side * rw * 0.12, rd * 0.12, drum_z)),
            hardware_material,
            rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'Y')).to_euler(),
            vertices=8,
        )
        created.append(line)

    # 4. Emergency Pneumatic Jettison Release Claws
    jettison_cyl = rmk.make_cylinder(
        f"{prefix}_JettisonPneumaticCylinder",
        0.022,
        0.095,
        _transform_world(config.center, frame, (0.0, -rd * 0.20, config.tube_radius * 2.0)),
        hardware_material,
        rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
        vertices=10,
        bevel=0.002,
    )
    created.append(jettison_cyl)

    claw = rmk.make_box(
        f"{prefix}_JettisonClawLatch",
        (0.045, 0.030, 0.040),
        _transform_world(config.center, frame, (0.0, -rd * 0.14, config.tube_radius * 2.5)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    created.append(claw)

    # 5. Sockets & VFX Anchors
    sock_fire = _make_socket("Socket_DamageFire", manifold_loc, rotation)
    sock_smoke = _make_socket("Socket_DamageSmoke", _transform_world(config.center, frame, (0.0, 0.0, drum_z + dr)), rotation)
    created.extend([sock_fire, sock_smoke])

    if hardflow_bevel:
        for obj in (bulkhead, manifold):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = REINFORCED_FUEL_DRUM_DROP_RACK_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "FuelDrums"

    return created


# =========================================================================
# Preset 11: Field Engineering Crane & Winch Sponson (Sponsons - Contemporary)
# =========================================================================

@dataclass(frozen=True)
class FieldEngineeringCraneWinchConfig:
    """Dimensions and placement for a combat engineering recovery crane & winch sponson."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    base_plate_size: tuple[float, float, float] = (0.32, 0.30, 0.024)
    turntable_radius: float = 0.110
    boom_base_size: tuple[float, float, float] = (0.09, 0.28, 0.09)
    boom_telescopic_size: tuple[float, float, float] = (0.07, 0.26, 0.07)
    boom_luff_deg: float = 28.0
    winch_radius: float = 0.055
    winch_width: float = 0.090
    hook_radius: float = 0.035
    fastener_radius: float = 0.006
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.base_plate_size, self.turntable_radius,
                  *self.boom_base_size, *self.boom_telescopic_size, self.boom_luff_deg,
                  self.winch_radius, self.winch_width, self.hook_radius,
                  self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("FieldEngineeringCraneWinchConfig parameters must be finite")
        if min(*self.base_plate_size, self.turntable_radius, *self.boom_base_size,
               *self.boom_telescopic_size, self.winch_radius, self.winch_width,
               self.hook_radius, self.fastener_radius) <= 0.0:
            raise ValueError("FieldEngineeringCraneWinchConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("FieldEngineeringCraneWinchConfig normal must not be zero")
        if not (0.0 <= self.boom_luff_deg <= 75.0):
            raise ValueError("boom_luff_deg must be within [0.0, 75.0] degrees")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_field_engineering_crane_winch_preset(
    *,
    prefix: str,
    config: FieldEngineeringCraneWinchConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a realistic combat engineering recovery crane & winch sponson."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    bw, bd, bh = config.base_plate_size

    # 1. Base Mounting Plate with 6 Perimeter Flange Bolts
    base_plate = rmk.make_box(
        f"{prefix}_BaseMountPlate",
        config.base_plate_size,
        _transform_world(config.center, frame, (0.0, 0.0, bh * 0.5)),
        hardware_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(base_plate)

    for i in range(6):
        ang = i * (math.pi / 3.0)
        bx = math.cos(ang) * (config.turntable_radius * 1.15)
        by = math.sin(ang) * (config.turntable_radius * 1.15)
        bolt = rmk.make_cylinder(
            f"{prefix}_MountBolt_{i}",
            config.fastener_radius,
            0.008,
            _transform_world(config.center, frame, (bx, by, bh + 0.003)),
            hardware_material,
            rotation=rotation,
            vertices=6,
        )
        created.append(bolt)

    # 2. Armored Swivel Turntable Housing with Bearing Ring
    tt_z = bh + 0.025
    turntable = rmk.make_cylinder(
        f"{prefix}_TurntableHousing",
        config.turntable_radius,
        0.050,
        _transform_world(config.center, frame, (0.0, 0.0, tt_z)),
        armor_material,
        rotation=rotation,
        vertices=16,
        bevel=0.004,
    )
    created.append(turntable)

    bearing_ring = rmk.make_cylinder(
        f"{prefix}_BearingRing",
        config.turntable_radius * 0.92,
        0.012,
        _transform_world(config.center, frame, (0.0, 0.0, tt_z + 0.026)),
        hardware_material,
        rotation=rotation,
        vertices=16,
    )
    created.append(bearing_ring)

    # 3. Luffing Boom Pivot Trunnions
    trunnion_z = tt_z + 0.050
    for side in (-1.0, 1.0):
        trunnion = rmk.make_box(
            f"{prefix}_BoomTrunnion_{side}",
            (0.018, 0.075, 0.080),
            _transform_world(config.center, frame, (side * (config.boom_base_size[0] * 0.65 + 0.009), -0.02, trunnion_z + 0.02)),
            armor_material,
            bevel=0.003,
            rotation=rotation,
        )
        created.append(trunnion)

    # 4. Box-Section Telescopic Boom Arm Inclined at Luff Angle
    luff_rad = math.radians(config.boom_luff_deg)
    luff_matrix = frame.to_matrix() @ Matrix.Rotation(luff_rad, 3, 'X')
    luff_rot = luff_matrix.to_euler()

    boom_pivot = _transform_world(config.center, frame, (0.0, -0.02, trunnion_z + 0.035))

    # Main Outer Boom
    main_b_len = config.boom_base_size[1]
    main_b_loc = Vector(boom_pivot) + luff_matrix @ Vector((0.0, main_b_len * 0.5, 0.0))
    main_boom = rmk.make_box(
        f"{prefix}_MainBoomSection",
        config.boom_base_size,
        main_b_loc,
        armor_material,
        bevel=0.004,
        rotation=luff_rot,
    )
    created.append(main_boom)

    # Telescopic Inner Boom
    tele_b_len = config.boom_telescopic_size[1]
    tele_b_loc = Vector(boom_pivot) + luff_matrix @ Vector((0.0, main_b_len + tele_b_len * 0.35, 0.0))
    tele_boom = rmk.make_box(
        f"{prefix}_TelescopicBoomSection",
        config.boom_telescopic_size,
        tele_b_loc,
        hardware_material,
        bevel=0.003,
        rotation=luff_rot,
    )
    created.append(tele_boom)

    # Hydraulic Luffing Ram
    ram_base = _transform_world(config.center, frame, (0.0, 0.05, tt_z + 0.03))
    ram_cyl = rmk.make_cylinder(
        f"{prefix}_LuffingRamCylinder",
        0.022,
        0.120,
        ram_base,
        hardware_material,
        rotation=luff_rot,
        vertices=10,
        bevel=0.002,
    )
    created.append(ram_cyl)

    ram_rod = rmk.make_cylinder(
        f"{prefix}_LuffingRamRod",
        0.012,
        0.090,
        Vector(ram_base) + luff_matrix @ Vector((0.0, 0.06, 0.0)),
        accent_material or hardware_material,
        rotation=luff_rot,
        vertices=8,
    )
    created.append(ram_rod)

    # 5. Heavy Hydraulic Cable Winch Drum Behind Trunnions
    winch_z = tt_z + 0.040
    winch_y = -0.08
    winch_drum = rmk.make_cylinder(
        f"{prefix}_WinchDrum",
        config.winch_radius,
        config.winch_width,
        _transform_world(config.center, frame, (0.0, winch_y, winch_z)),
        hardware_material,
        rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'Y')).to_euler(),
        vertices=16,
        bevel=0.003,
    )
    created.append(winch_drum)

    # Coiled Wire Rope Texture Ring
    wire_rope = rmk.make_cylinder(
        f"{prefix}_WoundWireRope",
        config.winch_radius * 0.82,
        config.winch_width * 0.85,
        _transform_world(config.center, frame, (0.0, winch_y, winch_z)),
        armor_material,
        rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'Y')).to_euler(),
        vertices=16,
    )
    created.append(wire_rope)

    # Fairlead Roller Assembly
    fairlead = rmk.make_box(
        f"{prefix}_FairleadRollerFrame",
        (config.winch_width * 1.1, 0.022, 0.038),
        _transform_world(config.center, frame, (0.0, winch_y + 0.05, winch_z + 0.03)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    created.append(fairlead)

    # 6. Snatch Block & Forged Lifting Hook at Tip
    boom_tip = Vector(boom_pivot) + luff_matrix @ Vector((0.0, main_b_len + tele_b_len * 0.70, 0.0))
    snatch_block = rmk.make_box(
        f"{prefix}_SnatchBlockPulley",
        (0.042, 0.055, 0.065),
        boom_tip,
        hardware_material,
        bevel=0.003,
        rotation=luff_rot,
    )
    created.append(snatch_block)

    hook_loc = Vector(boom_tip) + Vector((0.0, 0.0, -config.hook_radius * 1.4))
    hook = rmk.make_cylinder(
        f"{prefix}_ForgedLiftingHook",
        config.hook_radius,
        0.016,
        hook_loc,
        hardware_material,
        rotation=rotation,
        vertices=12,
        bevel=0.004,
    )
    created.append(hook)

    # 7. 3-Spool Hydraulic Valve Control Bank with Levers & Gauge
    valve_loc = _transform_world(config.center, frame, (bw * 0.38, 0.0, tt_z))
    valve_bank = rmk.make_box(
        f"{prefix}_ValveControlBank",
        (0.045, 0.090, 0.055),
        valve_loc,
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    created.append(valve_bank)

    for l_idx in range(3):
        ly = (l_idx - 1.0) * 0.024
        lever = rmk.make_cylinder(
            f"{prefix}_ControlLever_{l_idx}",
            0.004,
            0.032,
            _transform_world(config.center, frame, (bw * 0.38, ly, tt_z + 0.040)),
            accent_material or hardware_material,
            rotation=rotation,
            vertices=6,
        )
        created.append(lever)

    gauge = rmk.make_cylinder(
        f"{prefix}_HydraulicPressureGauge",
        0.014,
        0.008,
        _transform_world(config.center, frame, (bw * 0.38 + 0.024, 0.0, tt_z + 0.015)),
        accent_material or hardware_material,
        rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'Y')).to_euler(),
        vertices=10,
        bevel=0.001,
    )
    created.append(gauge)

    # 8. Sockets & VFX Hierarchy
    sock_smoke = _make_socket("Socket_DamageSmoke", _transform_world(config.center, frame, (0.0, 0.0, tt_z + 0.08)), rotation)
    sock_contact = _make_socket("Socket_FootContact_L", hook_loc, rotation)
    created.extend([sock_smoke, sock_contact])

    if hardflow_bevel:
        for obj in (base_plate, main_boom):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = FIELD_ENGINEERING_CRANE_WINCH_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"

    return created


# =========================================================================
# Preset 12: Multispectral Countermeasure Jammer (APS - Contemporary)
# =========================================================================

@dataclass(frozen=True)
class MultispectralCountermeasureJammerConfig:
    """Dimensions and placement for a multispectral active countermeasure EW jammer."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 0.0, 1.0)
    pedestal_size: tuple[float, float, float] = (0.24, 0.24, 0.045)
    shock_mount_radius: float = 0.018
    jammer_base_radius: float = 0.075
    jammer_tip_radius: float = 0.022
    jammer_height: float = 0.240
    maws_head_size: tuple[float, float, float] = (0.055, 0.065, 0.055)
    servo_body_size: tuple[float, float, float] = (0.08, 0.10, 0.07)
    fastener_radius: float = 0.005
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.pedestal_size, self.shock_mount_radius,
                  self.jammer_base_radius, self.jammer_tip_radius, self.jammer_height,
                  *self.maws_head_size, *self.servo_body_size, self.fastener_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("MultispectralCountermeasureJammerConfig parameters must be finite")
        if min(*self.pedestal_size, self.shock_mount_radius, self.jammer_base_radius,
               self.jammer_tip_radius, self.jammer_height, *self.maws_head_size,
               *self.servo_body_size, self.fastener_radius) <= 0.0:
            raise ValueError("MultispectralCountermeasureJammerConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("MultispectralCountermeasureJammerConfig normal must not be zero")
        if self.jammer_base_radius <= self.jammer_tip_radius:
            raise ValueError("jammer_base_radius must exceed jammer_tip_radius for a conical radome")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_multispectral_countermeasure_jammer_preset(
    *,
    prefix: str,
    config: MultispectralCountermeasureJammerConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a realistic multispectral active countermeasure EW jammer module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    pw, pd, ph = config.pedestal_size

    # 1. Square Pedestal Base Plate
    pedestal = rmk.make_box(
        f"{prefix}_PedestalBasePlate",
        config.pedestal_size,
        _transform_world(config.center, frame, (0.0, 0.0, ph * 0.5)),
        armor_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(pedestal)

    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            mount = rmk.make_cylinder(
                f"{prefix}_ShockMount_{sx}_{sy}",
                config.shock_mount_radius,
                0.016,
                _transform_world(config.center, frame, (sx * pw * 0.38, sy * pd * 0.38, ph + 0.008)),
                hardware_material,
                rotation=rotation,
                vertices=10,
                bevel=0.002,
            )
            created.append(mount)

    # 2. Central Azimuth Servo Gearbox Housing with Finned Body
    servo_z = ph + config.servo_body_size[2] * 0.5 + 0.012
    servo_body = rmk.make_box(
        f"{prefix}_AzimuthServoGearbox",
        config.servo_body_size,
        _transform_world(config.center, frame, (0.0, 0.0, servo_z)),
        armor_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(servo_body)

    for f_idx in range(4):
        fy = (f_idx - 1.5) * 0.022
        fin = rmk.make_box(
            f"{prefix}_ServoCoolingFin_{f_idx}",
            (config.servo_body_size[0] + 0.012, 0.004, config.servo_body_size[2] * 0.80),
            _transform_world(config.center, frame, (0.0, fy, servo_z)),
            hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.append(fin)

    # 3. High-Frequency Conical Dielectric EW Jammer Radome
    radome_z = ph + config.servo_body_size[2] + 0.012 + config.jammer_height * 0.5
    jammer_cone = _make_cone(
        f"{prefix}_ConicalJammerRadome",
        config.jammer_base_radius,
        config.jammer_height,
        _transform_world(config.center, frame, (0.0, 0.0, radome_z)),
        hardware_material,
        rotation=rotation,
        vertices=16,
        radius_tip=config.jammer_tip_radius,
    )
    created.append(jammer_cone)

    for r_idx in range(3):
        r_frac = (r_idx + 1) / 4.0
        rz = radome_z - config.jammer_height * 0.5 + config.jammer_height * r_frac
        r_rad = config.jammer_base_radius * (1.0 - r_frac) + config.jammer_tip_radius * r_frac + 0.004
        ring = rmk.make_cylinder(
            f"{prefix}_RadomeEmissionRing_{r_idx}",
            r_rad,
            0.008,
            _transform_world(config.center, frame, (0.0, 0.0, rz)),
            accent_material or hardware_material,
            rotation=rotation,
            vertices=16,
            bevel=0.001,
        )
        created.append(ring)

    cap_z = ph + config.servo_body_size[2] + 0.012 + config.jammer_height + config.jammer_tip_radius * 0.7
    emitter_cap = _make_sphere(
        f"{prefix}_TopEmitterCap",
        config.jammer_tip_radius * 1.05,
        _transform_world(config.center, frame, (0.0, 0.0, cap_z)),
        accent_material or hardware_material,
        segments=12,
        ring_count=8,
    )
    created.append(emitter_cap)

    # 4. Twin Angled MAWS Sensor Heads on Outrigger Pylons
    mw, md, mh = config.maws_head_size
    for side in (-1.0, 1.0):
        pylon_x = side * (pw * 0.48)
        pylon = rmk.make_cylinder(
            f"{prefix}_MawsPylon_{side}",
            0.014,
            pw * 0.28,
            _transform_world(config.center, frame, (side * (pw * 0.32), 0.0, servo_z)),
            hardware_material,
            rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'Y')).to_euler(),
            vertices=8,
        )
        created.append(pylon)

        maws_head = rmk.make_box(
            f"{prefix}_MawsSensorHead_{side}",
            config.maws_head_size,
            _transform_world(config.center, frame, (pylon_x, 0.0, servo_z + 0.02)),
            armor_material,
            bevel=0.003,
            rotation=rotation,
        )
        created.append(maws_head)

        optic = rmk.make_cylinder(
            f"{prefix}_GermaniumIrOptic_{side}",
            mw * 0.36,
            0.008,
            _transform_world(config.center, frame, (pylon_x, md * 0.52, servo_z + 0.02)),
            accent_material or hardware_material,
            rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
            vertices=12,
            bevel=0.001,
        )
        created.append(optic)

    # 5. Armored Electrical Junction Box with Grounding Braid
    jbox = rmk.make_box(
        f"{prefix}_ElectricalJunctionBox",
        (0.055, 0.045, 0.040),
        _transform_world(config.center, frame, (0.0, -pd * 0.40, ph + 0.022)),
        hardware_material,
        bevel=0.002,
        rotation=rotation,
    )
    created.append(jbox)

    braid = rmk.make_cylinder(
        f"{prefix}_GroundingBraidStrap",
        0.006,
        0.065,
        _transform_world(config.center, frame, (0.0, -pd * 0.44, ph + 0.012)),
        hardware_material,
        rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'Y')).to_euler(),
        vertices=6,
    )
    created.append(braid)

    # 6. Sockets & VFX Hierarchy
    sock_radar = _make_socket("Socket_RadarMast", _transform_world(config.center, frame, (0.0, 0.0, cap_z)), rotation)
    sock_smoke = _make_socket("Socket_DamageSmoke", _transform_world(config.center, frame, (0.0, 0.0, ph + 0.05)), rotation)
    created.extend([sock_radar, sock_smoke])

    if hardflow_bevel:
        for obj in (pedestal, servo_body):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = MULTISPECTRAL_COUNTERMEASURE_JAMMER_PRESET_ID
        obj["rcl_hard_surface_style"] = "contemporary_military"
        obj["rcl_hardpoint_slot"] = "APS"

    return created


# =========================================================================
# Preset 13: Quad Pulse Laser CIWS Sponson (Sponsons - Futuristic)
# =========================================================================

@dataclass(frozen=True)
class QuadPulseLaserCiwsConfig:
    """Dimensions and placement for a futuristic quad pulse laser CIWS sponson."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    base_radius: float = 0.130
    base_thickness: float = 0.025
    cradle_size: tuple[float, float, float] = (0.26, 0.24, 0.16)
    barrel_length: float = 0.360
    barrel_radius: float = 0.016
    elevation_deg: float = 18.0
    targeting_sphere_radius: float = 0.046
    capacitor_fin_count: int = 4
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, self.base_radius, self.base_thickness,
                  *self.cradle_size, self.barrel_length, self.barrel_radius,
                  self.elevation_deg, self.targeting_sphere_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("QuadPulseLaserCiwsConfig parameters must be finite")
        if min(self.base_radius, self.base_thickness, *self.cradle_size,
               self.barrel_length, self.barrel_radius, self.targeting_sphere_radius) <= 0.0:
            raise ValueError("QuadPulseLaserCiwsConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("QuadPulseLaserCiwsConfig normal must not be zero")
        if not (-20.0 <= self.elevation_deg <= 85.0):
            raise ValueError("elevation_deg must be within [-20.0, 85.0] degrees")
        if self.capacitor_fin_count < 2:
            raise ValueError("capacitor_fin_count must be at least 2")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_quad_pulse_laser_ciws_preset(
    *,
    prefix: str,
    config: QuadPulseLaserCiwsConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic quad pulsed laser CIWS sponson module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    br = config.base_radius
    bt = config.base_thickness
    cw, cd, ch = config.cradle_size

    # 1. Faceted Stealth Base Plate with Maglev Traverse Ring
    base_plate = rmk.make_cylinder(
        f"{prefix}_MaglevBasePlate",
        br,
        bt,
        _transform_world(config.center, frame, (0.0, 0.0, bt * 0.5)),
        armor_material,
        rotation=rotation,
        vertices=8,
        bevel=0.004,
    )
    created.append(base_plate)

    traverse_ring = rmk.make_cylinder(
        f"{prefix}_MaglevTraverseRing",
        br * 0.88,
        0.008,
        _transform_world(config.center, frame, (0.0, 0.0, bt + 0.004)),
        accent_material or hardware_material,
        rotation=rotation,
        vertices=16,
    )
    created.append(traverse_ring)

    # 2. Sloped Composite Cradle Housing
    cradle_z = bt + ch * 0.5 + 0.015
    for side in (-1.0, 1.0):
        cradle_side = rmk.make_box(
            f"{prefix}_CradleArmor_{side}",
            (0.026, cd * 0.85, ch),
            _transform_world(config.center, frame, (side * (cw * 0.5 - 0.013), 0.0, cradle_z)),
            armor_material,
            bevel=0.004,
            rotation=rotation,
        )
        created.append(cradle_side)

    # 3. Quad Laser Emitters in 2x2 Matrix with Elevation
    elev_rad = math.radians(config.elevation_deg)
    elev_rot_matrix = frame.to_matrix() @ Matrix.Rotation(elev_rad, 3, 'X')
    elev_rotation = elev_rot_matrix.to_euler()

    gun_pivot = _transform_world(config.center, frame, (0.0, 0.0, cradle_z + 0.02))

    spacing_x = 0.048
    spacing_z = 0.048
    barrel_rot = (elev_rot_matrix @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler()

    for col in (-1, 1):
        for row in (-1, 1):
            lx = col * spacing_x
            lz = row * spacing_z
            b_offset = elev_rot_matrix @ Vector((lx, config.barrel_length * 0.5, lz))
            b_loc = Vector(gun_pivot) + b_offset

            barrel = rmk.make_cylinder(
                f"{prefix}_LaserEmitter_{col}_{row}",
                config.barrel_radius,
                config.barrel_length,
                b_loc,
                hardware_material,
                rotation=barrel_rot,
                vertices=12,
            )
            created.append(barrel)

            # Collimator Ring
            ring_offset = elev_rot_matrix @ Vector((lx, config.barrel_length * 0.75, lz))
            ring = rmk.make_cylinder(
                f"{prefix}_CollimatorRing_{col}_{row}",
                config.barrel_radius * 1.45,
                0.016,
                Vector(gun_pivot) + ring_offset,
                accent_material or hardware_material,
                rotation=barrel_rot,
                vertices=12,
                bevel=0.002,
            )
            created.append(ring)

            # Muzzle Shroud
            muzzle_offset = elev_rot_matrix @ Vector((lx, config.barrel_length, lz))
            muzzle = rmk.make_cylinder(
                f"{prefix}_RefractoryShroud_{col}_{row}",
                config.barrel_radius * 1.30,
                0.035,
                Vector(gun_pivot) + muzzle_offset,
                hardware_material,
                rotation=barrel_rot,
                vertices=8,
                bevel=0.002,
            )
            created.append(muzzle)

    # 4. Flank Planar Capacitor Energy Banks with Glowing Dissipation Fins
    for side in (-1.0, 1.0):
        cap_bank = rmk.make_box(
            f"{prefix}_CapacitorBank_{side}",
            (0.035, cd * 0.65, ch * 0.60),
            _transform_world(config.center, frame, (side * (cw * 0.5 + 0.024), -cd * 0.10, cradle_z)),
            hardware_material,
            bevel=0.003,
            rotation=rotation,
        )
        created.append(cap_bank)

        for f_idx in range(config.capacitor_fin_count):
            fz = (f_idx - (config.capacitor_fin_count - 1) * 0.5) * 0.025
            fin = rmk.make_box(
                f"{prefix}_DissipationFin_{side}_{f_idx}",
                (0.045, cd * 0.55, 0.004),
                _transform_world(config.center, frame, (side * (cw * 0.5 + 0.030), -cd * 0.10, cradle_z + fz)),
                accent_material or hardware_material,
                bevel=0.001,
                rotation=rotation,
            )
            created.append(fin)

    # 5. Dual Cryogenic Liquid Helium / Nitrogen Manifolds
    for side in (-1.0, 1.0):
        pipe = rmk.make_cylinder(
            f"{prefix}_CryoCoolantPipe_{side}",
            0.010,
            cd * 0.70,
            _transform_world(config.center, frame, (side * (cw * 0.40), -cd * 0.15, bt + 0.025)),
            hardware_material,
            rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
            vertices=8,
        )
        created.append(pipe)

    # 6. Gyro-Stabilized Optical Targeting Sphere on Elevated Pylon
    pylon_loc = _transform_world(config.center, frame, (cw * 0.52, -cd * 0.35, cradle_z + 0.08))
    t_pylon = rmk.make_box(
        f"{prefix}_TargetingPylon",
        (0.030, 0.035, 0.120),
        pylon_loc,
        armor_material,
        bevel=0.003,
        rotation=rotation,
    )
    created.append(t_pylon)

    sphere_loc = _transform_world(config.center, frame, (cw * 0.52, -cd * 0.35, cradle_z + 0.16))
    t_sphere = _make_sphere(
        f"{prefix}_TargetingSphere",
        config.targeting_sphere_radius,
        sphere_loc,
        hardware_material,
        segments=16,
        ring_count=10,
    )
    created.append(t_sphere)

    visor = rmk.make_cylinder(
        f"{prefix}_TargetingVisorAperture",
        config.targeting_sphere_radius * 0.45,
        0.008,
        _transform_world(config.center, frame, (cw * 0.52, -cd * 0.35 + config.targeting_sphere_radius * 0.90, cradle_z + 0.16)),
        accent_material or hardware_material,
        rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
        vertices=8,
    )
    created.append(visor)

    # 7. Sockets & VFX Hierarchy
    muzzle_tip_world = Vector(gun_pivot) + elev_rot_matrix @ Vector((0.0, config.barrel_length + 0.02, 0.0))
    sock_muzzle = _make_socket("Socket_MuzzlePrimary", muzzle_tip_world, elev_rotation)
    firepoint_0 = _make_socket("FirePoint_0", Vector(gun_pivot) + elev_rot_matrix @ Vector((-spacing_x, config.barrel_length + 0.02, -spacing_z)), elev_rotation)
    firepoint_0.parent = sock_muzzle
    firepoint_1 = _make_socket("FirePoint_1", Vector(gun_pivot) + elev_rot_matrix @ Vector((spacing_x, config.barrel_length + 0.02, spacing_z)), elev_rotation)
    firepoint_1.parent = sock_muzzle
    sock_audio = _make_socket("Socket_WeaponAudio", muzzle_tip_world, elev_rotation)
    sock_smoke = _make_socket("Socket_DamageSmoke", _transform_world(config.center, frame, (0.0, 0.0, cradle_z)), rotation)
    created.extend([sock_muzzle, firepoint_0, firepoint_1, sock_audio, sock_smoke])

    if hardflow_bevel:
        for obj in (base_plate,):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = QUAD_PULSE_LASER_CIWS_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"

    return created


# =========================================================================
# Preset 14: Cryo Fusion Cell Containment Rack (FuelDrums - Futuristic)
# =========================================================================

@dataclass(frozen=True)
class CryoFusionCellContainmentRackConfig:
    """Dimensions and placement for twin spherical cryogenic fusion fuel cells in a containment rack."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    bulkhead_size: tuple[float, float, float] = (0.66, 0.18, 0.34)
    cell_radius: float = 0.115
    equatorial_ring_width: float = 0.024
    vent_nozzle_radius: float = 0.016
    decoupling_latch_count: int = 4
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.bulkhead_size, self.cell_radius,
                  self.equatorial_ring_width, self.vent_nozzle_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("CryoFusionCellContainmentRackConfig parameters must be finite")
        if min(*self.bulkhead_size, self.cell_radius, self.equatorial_ring_width,
               self.vent_nozzle_radius) <= 0.0:
            raise ValueError("CryoFusionCellContainmentRackConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("CryoFusionCellContainmentRackConfig normal must not be zero")
        if (self.cell_radius * 4.0) > self.bulkhead_size[0]:
            raise ValueError("bulkhead width is too small to fit dual fusion cells")
        if self.decoupling_latch_count < 2:
            raise ValueError("decoupling_latch_count must be at least 2")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_cryo_fusion_cell_containment_rack_preset(
    *,
    prefix: str,
    config: CryoFusionCellContainmentRackConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic cryogenic fusion fuel cell containment rack."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    bw, bd, bh = config.bulkhead_size
    cr = config.cell_radius

    # 1. Faceted Composite Mounting Bulkhead
    bulkhead = rmk.make_box(
        f"{prefix}_CompositeBulkhead",
        config.bulkhead_size,
        _transform_world(config.center, frame, (0.0, -bd * 0.40, bh * 0.5)),
        armor_material,
        bevel=0.005,
        rotation=rotation,
    )
    created.append(bulkhead)

    for i in range(config.decoupling_latch_count):
        frac = (i / (config.decoupling_latch_count - 1) - 0.5) * 2.0
        lx = frac * (bw * 0.42)
        latch = rmk.make_box(
            f"{prefix}_DecouplingLatch_{i}",
            (0.038, 0.035, 0.045),
            _transform_world(config.center, frame, (lx, -bd * 0.45, bh * 0.85)),
            hardware_material,
            bevel=0.002,
            rotation=rotation,
        )
        created.append(latch)

    # 2. Twin Spherical Fusion Cells with Equatorial Rings
    cell_pitch = bw * 0.27
    cell_y = 0.0
    cell_z = cr + 0.040

    for side in (-1.0, 1.0):
        cx = side * cell_pitch
        cell = _make_sphere(
            f"{prefix}_FusionFuelCell_{side}",
            cr,
            _transform_world(config.center, frame, (cx, cell_y, cell_z)),
            armor_material,
            segments=16,
            ring_count=12,
        )
        created.append(cell)

        ring = rmk.make_cylinder(
            f"{prefix}_EquatorialRing_{side}",
            cr + 0.006,
            config.equatorial_ring_width,
            _transform_world(config.center, frame, (cx, cell_y, cell_z)),
            hardware_material,
            rotation=rotation,
            vertices=16,
            bevel=0.002,
        )
        created.append(ring)

        mli_cap = _make_sphere(
            f"{prefix}_MliBlanketCap_{side}",
            cr * 1.02,
            _transform_world(config.center, frame, (cx, cell_y, cell_z + cr * 0.20)),
            accent_material or hardware_material,
            segments=12,
            ring_count=8,
        )
        created.append(mli_cap)

        oled = rmk.make_box(
            f"{prefix}_OledTelemetryPanel_{side}",
            (0.048, 0.008, 0.028),
            _transform_world(config.center, frame, (cx, cell_y + cr * 0.98, cell_z)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.append(oled)

    # 3. Superconducting Inter-Cell Fuel Transfer Bridge Manifold
    bridge_loc = _transform_world(config.center, frame, (0.0, 0.0, cell_z))
    bridge = rmk.make_cylinder(
        f"{prefix}_CryoTransferBridge",
        0.022,
        cell_pitch * 1.8,
        bridge_loc,
        hardware_material,
        rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi * 0.5, 3, 'Y')).to_euler(),
        vertices=12,
        bevel=0.003,
    )
    created.append(bridge)

    for side in (-1.0, 1.0):
        disc = rmk.make_cylinder(
            f"{prefix}_RuptureDisc_{side}",
            0.016,
            0.010,
            _transform_world(config.center, frame, (side * 0.060, 0.0, cell_z + 0.026)),
            accent_material or hardware_material,
            rotation=rotation,
            vertices=8,
        )
        created.append(disc)

    # 4. Ventral Cryogenic Boil-Off Relief Exhaust Nozzles with Deflector Bells
    for side in (-1.0, 1.0):
        cx = side * cell_pitch
        nozzle = _make_cone(
            f"{prefix}_BoilOffNozzle_{side}",
            config.vent_nozzle_radius * 1.4,
            0.045,
            _transform_world(config.center, frame, (cx, cell_y, 0.025)),
            hardware_material,
            rotation=(frame.to_matrix() @ Matrix.Rotation(math.pi, 3, 'X')).to_euler(),
            vertices=12,
            radius_tip=config.vent_nozzle_radius * 0.6,
        )
        created.append(nozzle)

        deflector = rmk.make_box(
            f"{prefix}_FrostDeflector_{side}",
            (config.vent_nozzle_radius * 3.0, 0.006, 0.022),
            _transform_world(config.center, frame, (cx, cell_y + config.vent_nozzle_radius * 1.6, 0.020)),
            armor_material,
            bevel=0.001,
            rotation=rotation,
        )
        created.append(deflector)

    # 5. Sockets & VFX Hierarchy
    sock_fire = _make_socket("Socket_DamageFire", bridge_loc, rotation)
    sock_smoke = _make_socket("Socket_DamageSmoke", _transform_world(config.center, frame, (0.0, 0.0, cell_z + cr)), rotation)
    created.extend([sock_fire, sock_smoke])

    if hardflow_bevel:
        for obj in (bulkhead,):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = CRYO_FUSION_CELL_CONTAINMENT_RACK_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "FuelDrums"

    return created


# =========================================================================
# Preset 15: Nanofabrication Repair Projector (Sponsons - Futuristic)
# =========================================================================

@dataclass(frozen=True)
class NanofabricationRepairProjectorConfig:
    """Dimensions and placement for an articulated nanofabrication and molecular repair projector."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    root_size: tuple[float, float, float] = (0.24, 0.22, 0.050)
    arm_length: float = 0.320
    projector_head_size: tuple[float, float, float] = (0.14, 0.16, 0.11)
    nozzle_radius: float = 0.024
    cartridge_radius: float = 0.038
    cartridge_length: float = 0.180
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.root_size, self.arm_length,
                  *self.projector_head_size, self.nozzle_radius, self.cartridge_radius,
                  self.cartridge_length, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("NanofabricationRepairProjectorConfig parameters must be finite")
        if min(*self.root_size, self.arm_length, *self.projector_head_size,
               self.nozzle_radius, self.cartridge_radius, self.cartridge_length) <= 0.0:
            raise ValueError("NanofabricationRepairProjectorConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("NanofabricationRepairProjectorConfig normal must not be zero")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_nanofabrication_repair_projector_preset(
    *,
    prefix: str,
    config: NanofabricationRepairProjectorConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic articulated nanofabrication repair projector module."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    rw, rd, rh = config.root_size

    # 1. Aerodynamic Sponson Root Base with Magnetic Bearing Turntable
    root_base = rmk.make_box(
        f"{prefix}_GimbalRootBase",
        config.root_size,
        _transform_world(config.center, frame, (0.0, 0.0, rh * 0.5)),
        armor_material,
        bevel=0.004,
        rotation=rotation,
    )
    created.append(root_base)

    bearing_turntable = rmk.make_cylinder(
        f"{prefix}_DirectDriveTurntable",
        rw * 0.40,
        0.016,
        _transform_world(config.center, frame, (0.0, 0.0, rh + 0.008)),
        hardware_material,
        rotation=rotation,
        vertices=16,
        bevel=0.002,
    )
    created.append(bearing_turntable)

    # 2. Articulated Composite Projection Arm
    arm_pivot = _transform_world(config.center, frame, (0.0, -0.02, rh + 0.04))
    arm_reach = config.arm_length
    arm_angle_rad = math.radians(22.0)
    arm_matrix = frame.to_matrix() @ Matrix.Rotation(arm_angle_rad, 3, 'X')
    arm_rot = arm_matrix.to_euler()

    arm_segment_loc = Vector(arm_pivot) + arm_matrix @ Vector((0.0, arm_reach * 0.45, 0.0))
    arm_beam = rmk.make_box(
        f"{prefix}_ArticulatedArmBeam",
        (0.065, arm_reach * 0.85, 0.065),
        arm_segment_loc,
        armor_material,
        bevel=0.004,
        rotation=arm_rot,
    )
    created.append(arm_beam)

    elbow = rmk.make_cylinder(
        f"{prefix}_ElbowPivotJoint",
        0.038,
        0.078,
        Vector(arm_pivot) + arm_matrix @ Vector((0.0, arm_reach * 0.85, 0.0)),
        hardware_material,
        rotation=(arm_matrix @ Matrix.Rotation(math.pi * 0.5, 3, 'Y')).to_euler(),
        vertices=12,
        bevel=0.003,
    )
    created.append(elbow)

    # 3. Coherent Nanofabrication Projector Head
    head_w, head_d, head_h = config.projector_head_size
    head_tip = Vector(arm_pivot) + arm_matrix @ Vector((0.0, arm_reach + head_d * 0.40, 0.0))

    projector_cowl = rmk.make_box(
        f"{prefix}_ProjectorHeadCowl",
        config.projector_head_size,
        head_tip,
        armor_material,
        bevel=0.004,
        rotation=arm_rot,
    )
    created.append(projector_cowl)

    nozzle_loc = Vector(head_tip) + arm_matrix @ Vector((0.0, head_d * 0.52, 0.0))
    nozzle = rmk.make_cylinder(
        f"{prefix}_EmitterNozzleCore",
        config.nozzle_radius,
        0.025,
        nozzle_loc,
        hardware_material,
        rotation=(arm_matrix @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
        vertices=12,
    )
    created.append(nozzle)

    for r_idx in range(2):
        ring_rad = config.nozzle_radius * (1.4 + r_idx * 0.45)
        collimation_ring = rmk.make_cylinder(
            f"{prefix}_CollimationTorus_{r_idx}",
            ring_rad,
            0.010,
            Vector(nozzle_loc) + arm_matrix @ Vector((0.0, 0.012 * r_idx, 0.0)),
            accent_material or hardware_material,
            rotation=(arm_matrix @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
            vertices=16,
            bevel=0.002,
        )
        created.append(collimation_ring)

    # 4. Dual Quick-Swap Solid-State Feedstock Canister Cartridges
    for side in (-1.0, 1.0):
        cx = side * (head_w * 0.5 + config.cartridge_radius * 0.85)
        cart_loc = Vector(head_tip) + arm_matrix @ Vector((cx, -head_d * 0.15, 0.0))
        cartridge = rmk.make_cylinder(
            f"{prefix}_FeedstockCartridge_{side}",
            config.cartridge_radius,
            config.cartridge_length,
            cart_loc,
            hardware_material,
            rotation=(arm_matrix @ Matrix.Rotation(math.pi * 0.5, 3, 'X')).to_euler(),
            vertices=12,
            bevel=0.003,
        )
        created.append(cartridge)

        level_strip = rmk.make_box(
            f"{prefix}_FeedstockLevelStrip_{side}",
            (0.006, config.cartridge_length * 0.70, 0.008),
            Vector(cart_loc) + arm_matrix @ Vector((side * (config.cartridge_radius + 0.002), 0.0, 0.0)),
            accent_material or hardware_material,
            bevel=0.001,
            rotation=arm_rot,
        )
        created.append(level_strip)

    # 5. Lidar Surface Reconstruction Scanner Node
    lidar_loc = Vector(head_tip) + arm_matrix @ Vector((0.0, head_d * 0.20, head_h * 0.52))
    lidar = rmk.make_cylinder(
        f"{prefix}_LidarReconstructionScanner",
        0.022,
        0.016,
        lidar_loc,
        hardware_material,
        rotation=arm_rot,
        vertices=10,
        bevel=0.002,
    )
    created.append(lidar)

    wedge = rmk.make_box(
        f"{prefix}_SapphireOpticalWedge",
        (0.018, 0.018, 0.006),
        Vector(lidar_loc) + arm_matrix @ Vector((0.0, 0.0, 0.010)),
        accent_material or hardware_material,
        bevel=0.001,
        rotation=arm_rot,
    )
    created.append(wedge)

    # 6. Rear Transverse Heat-Pipe Cooling Matrix
    for f_idx in range(4):
        fz = (f_idx - 1.5) * 0.018
        fin_loc = Vector(head_tip) + arm_matrix @ Vector((0.0, -head_d * 0.42, fz))
        fin = rmk.make_box(
            f"{prefix}_HeatPipeRadiatorFin_{f_idx}",
            (head_w * 0.90, 0.004, 0.018),
            fin_loc,
            hardware_material,
            bevel=0.001,
            rotation=arm_rot,
        )
        created.append(fin)

    # 7. Sockets & VFX Hierarchy
    muzzle_tip_world = Vector(nozzle_loc) + arm_matrix @ Vector((0.0, 0.03, 0.0))
    sock_muzzle = _make_socket("Socket_MuzzlePrimary", muzzle_tip_world, arm_rot)
    sock_audio = _make_socket("Socket_WeaponAudio", muzzle_tip_world, arm_rot)
    sock_smoke = _make_socket("Socket_DamageSmoke", _transform_world(config.center, frame, (0.0, 0.0, rh + 0.08)), rotation)
    created.extend([sock_muzzle, sock_audio, sock_smoke])

    if hardflow_bevel:
        for obj in (root_base, projector_cowl):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = NANOFABRICATION_REPAIR_PROJECTOR_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "Sponsons"

    return created


# =========================================================================
# Preset 16: Flux Deflection Shield Projector (APS - Futuristic)
# =========================================================================

@dataclass(frozen=True)
class FluxDeflectionShieldProjectorConfig:
    """Dimensions and placement for a magnetohydrodynamic flux deflection shield projector."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, 0.0, 1.0)
    base_radius: float = 0.140
    base_thickness: float = 0.026
    pinch_chamber_radius: float = 0.065
    pinch_chamber_height: float = 0.140
    field_guide_length: float = 0.150
    field_guide_angle_deg: float = 38.0
    beacon_radius: float = 0.028
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, self.base_radius, self.base_thickness,
                  self.pinch_chamber_radius, self.pinch_chamber_height, self.field_guide_length,
                  self.field_guide_angle_deg, self.beacon_radius, self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("FluxDeflectionShieldProjectorConfig parameters must be finite")
        if min(self.base_radius, self.base_thickness, self.pinch_chamber_radius,
               self.pinch_chamber_height, self.field_guide_length, self.beacon_radius) <= 0.0:
            raise ValueError("FluxDeflectionShieldProjectorConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("FluxDeflectionShieldProjectorConfig normal must not be zero")
        if not (15.0 <= self.field_guide_angle_deg <= 75.0):
            raise ValueError("field_guide_angle_deg must be within [15.0, 75.0] degrees")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def create_flux_deflection_shield_projector_preset(
    *,
    prefix: str,
    config: FluxDeflectionShieldProjectorConfig,
    armor_material: bpy.types.Material | None,
    hardware_material: bpy.types.Material | None,
    accent_material: bpy.types.Material | None = None,
    hardflow_boolean_apply: Callable[[bpy.types.Object, bpy.types.Object], str] | None = None,
    hardflow_bevel: Callable[[bpy.types.Object], None] | None = None,
) -> list[bpy.types.Object]:
    """Build a futuristic magnetohydrodynamic flux deflection shield projector."""
    if not prefix:
        raise ValueError("Preset prefix must not be empty")
    frame = _orientation_frame(config.normal, config.roll_degrees)
    rotation = frame.to_euler()
    created: list[bpy.types.Object] = []

    br = config.base_radius
    bt = config.base_thickness
    pr = config.pinch_chamber_radius
    ph = config.pinch_chamber_height

    # 1. Hexagonal Heavy Composite Base Plate with 6 Grounding Studs
    base_plate = rmk.make_cylinder(
        f"{prefix}_HexBasePlate",
        br,
        bt,
        _transform_world(config.center, frame, (0.0, 0.0, bt * 0.5)),
        armor_material,
        rotation=rotation,
        vertices=6,
        bevel=0.004,
    )
    created.append(base_plate)

    for i in range(6):
        ang = i * (math.pi / 3.0)
        sx = math.cos(ang) * (br * 0.88)
        sy = math.sin(ang) * (br * 0.88)
        stud = rmk.make_cylinder(
            f"{prefix}_GroundStud_{i}",
            0.007,
            0.012,
            _transform_world(config.center, frame, (sx, sy, bt + 0.006)),
            hardware_material,
            rotation=rotation,
            vertices=6,
        )
        created.append(stud)

    # 2. Stepped Cylindrical Magnetic Pinch Chamber with Copper Field Coils
    chamber_z = bt + ph * 0.5
    chamber = rmk.make_cylinder(
        f"{prefix}_MagneticPinchChamber",
        pr,
        ph,
        _transform_world(config.center, frame, (0.0, 0.0, chamber_z)),
        hardware_material,
        rotation=rotation,
        vertices=16,
        bevel=0.003,
    )
    created.append(chamber)

    for c_idx in range(2):
        cz = chamber_z - ph * 0.25 + c_idx * (ph * 0.50)
        coil = rmk.make_cylinder(
            f"{prefix}_SuperconductingFieldCoil_{c_idx}",
            pr + 0.008,
            0.024,
            _transform_world(config.center, frame, (0.0, 0.0, cz)),
            accent_material or hardware_material,
            rotation=rotation,
            vertices=16,
            bevel=0.002,
        )
        created.append(coil)

    # 3. 4 Articulating Electromagnetic Field Deflection Guides (Petals)
    guide_ang_rad = math.radians(config.field_guide_angle_deg)
    gl = config.field_guide_length
    for g_idx in range(4):
        azimuth = g_idx * (math.pi * 0.5)
        az_matrix = frame.to_matrix() @ Matrix.Rotation(azimuth, 3, 'Z')
        petal_matrix = az_matrix @ Matrix.Rotation(guide_ang_rad, 3, 'X')
        petal_rot = petal_matrix.to_euler()

        guide_loc = _transform_world(config.center, frame, (math.cos(azimuth) * (pr + 0.018), math.sin(azimuth) * (pr + 0.018), chamber_z + 0.01))
        guide = rmk.make_box(
            f"{prefix}_FluxFieldGuide_{g_idx}",
            (0.038, 0.022, gl),
            Vector(guide_loc) + petal_matrix @ Vector((0.0, 0.0, gl * 0.45)),
            armor_material,
            bevel=0.003,
            rotation=petal_rot,
        )
        created.append(guide)

        tooth_loc = Vector(guide_loc) + petal_matrix @ Vector((0.0, 0.014, gl * 0.70))
        tooth = rmk.make_box(
            f"{prefix}_CeramicInsulatorTooth_{g_idx}",
            (0.028, 0.012, 0.035),
            tooth_loc,
            accent_material or hardware_material,
            bevel=0.001,
            rotation=petal_rot,
        )
        created.append(tooth)

    # 4. High-Voltage Shielded Busbar Cables with Strain-Relief Boots
    for b_idx in range(2):
        bx = (b_idx - 0.5) * 0.090
        boot = rmk.make_cylinder(
            f"{prefix}_SiliconeStressBoot_{b_idx}",
            0.014,
            0.028,
            _transform_world(config.center, frame, (bx, -pr * 1.35, bt + 0.014)),
            hardware_material,
            rotation=rotation,
            vertices=8,
            bevel=0.002,
        )
        created.append(boot)

        cable = rmk.make_cylinder(
            f"{prefix}_HighVoltageBusbar_{b_idx}",
            0.008,
            0.080,
            _transform_world(config.center, frame, (bx, -pr * 1.35, bt + 0.050)),
            accent_material or hardware_material,
            rotation=rotation,
            vertices=8,
        )
        created.append(cable)

    # 5. Closed-Loop Liquid Helium Return Plenum with Pressure Transducer
    plenum = rmk.make_cylinder(
        f"{prefix}_CryoReturnPlenum",
        pr * 0.75,
        0.022,
        _transform_world(config.center, frame, (0.0, 0.0, bt + ph + 0.011)),
        hardware_material,
        rotation=rotation,
        vertices=12,
        bevel=0.002,
    )
    created.append(plenum)

    transducer = rmk.make_cylinder(
        f"{prefix}_PressureTransducer",
        0.010,
        0.018,
        _transform_world(config.center, frame, (pr * 0.65, 0.0, bt + ph + 0.015)),
        accent_material or hardware_material,
        rotation=rotation,
        vertices=6,
    )
    created.append(transducer)

    # 6. Dorsal Pulsing Shield Beacon Emitter Dome
    beacon_z = bt + ph + 0.022 + config.beacon_radius
    beacon = _make_sphere(
        f"{prefix}_ShieldBeaconDome",
        config.beacon_radius,
        _transform_world(config.center, frame, (0.0, 0.0, beacon_z)),
        accent_material or hardware_material,
        segments=16,
        ring_count=10,
    )
    created.append(beacon)

    # 7. Sockets & VFX Hierarchy
    sock_radar = _make_socket("Socket_RadarMast", _transform_world(config.center, frame, (0.0, 0.0, beacon_z + config.beacon_radius)), rotation)
    sock_smoke = _make_socket("Socket_DamageSmoke", _transform_world(config.center, frame, (0.0, 0.0, bt + 0.05)), rotation)
    created.extend([sock_radar, sock_smoke])

    if hardflow_bevel:
        for obj in (base_plate,):
            hardflow_bevel(obj)

    for obj in created:
        obj["rcl_hard_surface_geometry_preset"] = FLUX_DEFLECTION_SHIELD_PROJECTOR_PRESET_ID
        obj["rcl_hard_surface_style"] = "futuristic_military"
        obj["rcl_hardpoint_slot"] = "APS"

    return created
