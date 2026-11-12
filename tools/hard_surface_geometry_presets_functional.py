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

PLASMA_EXCAVATION_TORCH_PRESET_ID = "plasma_excavation_torch_v1"
MAGNETIC_TRACTOR_DOCKING_CLAMP_PRESET_ID = "magnetic_tractor_docking_clamp_v1"
GRAVITON_RECOIL_ANCHOR_PRESET_ID = "graviton_recoil_anchor_v1"
OPTRONIC_RECON_SENSOR_MAST_PRESET_ID = "optronic_recon_sensor_mast_v1"


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
