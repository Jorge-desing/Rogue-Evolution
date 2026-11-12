"""Hard-surface detailing Level 2 presets (boolean cuts, panels, slots, grilles,
bolt circles, weld beads, tubes and cables) in contemporary and futuristic styles.

Every preset is built from the shared detail primitives below, mounts at the
canonical hardpoint origin (unit-local +Z = mount normal) and is script-free.
"""

from __future__ import annotations

import math
from dataclasses import asdict, dataclass
from typing import Callable

try:
    import bpy
    from mathutils import Vector
    import RogueModelKit as rmk
except ImportError:
    bpy = None
    Vector = None
    rmk = None

from hard_surface_geometry_presets import (
    _apply_hardflow_or_builtin_bevel,
    _apply_hardflow_or_builtin_boolean,
    _orientation_frame,
    _transform_world,
)

ARMOR_ACCESS_PANEL_PRESET_ID = "armor_access_panel_bolt_circle_v1"
ENGINE_DECK_LOUVER_GRILLE_PRESET_ID = "engine_deck_louver_grille_v1"
COMPOSITE_HEX_VENT_PANEL_PRESET_ID = "composite_hex_vent_panel_v1"
CABLE_CONDUIT_JUNCTION_PRESET_ID = "cable_conduit_junction_manifold_v1"


@dataclass(frozen=True)
class DetailingConfig:
    """Shared placement/size contract for the detailing presets."""
    center: tuple[float, float, float] = (0.0, 0.0, 0.0)
    normal: tuple[float, float, float] = (0.0, -1.0, 0.0)
    panel_size: tuple[float, float, float] = (0.36, 0.28, 0.030)
    fastener_radius: float = 0.007
    bolt_circle_count: int = 8
    slot_count: int = 6
    roll_degrees: float = 0.0

    def __post_init__(self) -> None:
        values = (*self.center, *self.normal, *self.panel_size, self.fastener_radius,
                  self.roll_degrees)
        if not all(math.isfinite(v) for v in values):
            raise ValueError("DetailingConfig parameters must be finite")
        if min(*self.panel_size, self.fastener_radius) <= 0.0:
            raise ValueError("DetailingConfig physical dimensions must be positive")
        if math.hypot(*self.normal) <= 1.0e-8:
            raise ValueError("DetailingConfig normal must not be zero")
        if self.bolt_circle_count < 4 or self.slot_count < 2:
            raise ValueError("bolt_circle_count must be >= 4 and slot_count >= 2")

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


# Distinct config types so the registry/pilot can key on them.
class ArmorAccessPanelConfig(DetailingConfig):
    pass


class EngineDeckLouverGrilleConfig(DetailingConfig):
    pass


class CompositeHexVentPanelConfig(DetailingConfig):
    pass


class CableConduitJunctionConfig(DetailingConfig):
    pass


# ---------------------------------------------------------------- primitives

class _Ctx:
    def __init__(self, prefix, config, boolean, bevel):
        self.prefix = prefix
        self.config = config
        self.frame = _orientation_frame(config.normal, config.roll_degrees)
        self.rotation = self.frame.to_euler()
        self.created: list = []
        self.boolean = boolean
        self.bevel = bevel

    def w(self, local):
        return _transform_world(self.config.center, self.frame, local)

    def add(self, obj):
        self.created.append(obj)
        return obj

    def box(self, name, size, local, mat, bevel=0.002):
        return self.add(rmk.make_box(f"{self.prefix}_{name}", size, self.w(local), mat,
                                     bevel=bevel, rotation=self.rotation))

    def rod(self, name, a, b, radius, mat, vertices=8):
        return self.add(rmk.make_rod(f"{self.prefix}_{name}", self.w(a), self.w(b), radius,
                                     mat, vertices=vertices))

    def cut(self, target, name, size, local):
        """Boolean-cut a rectangular recess/slot from target (Hardflow or built-in EXACT)."""
        cutter = rmk.make_box(f"{self.prefix}_{name}", size, self.w(local), None, bevel=0.0,
                              rotation=self.rotation)
        _apply_hardflow_or_builtin_boolean(target, cutter, self.boolean)


def _bolt_circle(ctx, name, radius, z, count, bolt_radius, mat, phase=0.0):
    for i in range(count):
        a = phase + 2.0 * math.pi * i / count
        ctx.add(rmk.make_cylinder(
            f"{ctx.prefix}_{name}_{i}", bolt_radius, bolt_radius * 1.1,
            ctx.w((radius * math.cos(a), radius * math.sin(a), z)), mat,
            rotation=ctx.rotation, vertices=6, bevel=bolt_radius * 0.18))


def _bolt_rect(ctx, name, hx, hy, z, bolt_radius, mat, per_side=3):
    n = 0
    for sx in (-1.0, 1.0):
        for k in range(per_side):
            y = (k / (per_side - 1) - 0.5) * 2.0 * hy
            ctx.add(rmk.make_cylinder(f"{ctx.prefix}_{name}_{n}", bolt_radius, bolt_radius * 1.1,
                                      ctx.w((sx * hx, y, z)), mat, rotation=ctx.rotation,
                                      vertices=6, bevel=bolt_radius * 0.18))
            n += 1


def _weld_bead(ctx, name, a, b, radius, mat, beads=None):
    """Staggered stitch-weld bead (overlapping dabs) between two local points."""
    a, b = Vector(a), Vector(b)
    beads = beads or max(4, int((b - a).length / (radius * 2.6)))
    for i in range(beads):
        t0, t1 = i / beads, (i + 0.78) / beads
        ctx.rod(f"{name}_{i}", tuple(a.lerp(b, t0)), tuple(a.lerp(b, min(t1, 1.0))),
                radius, mat, vertices=6)


def _cable_run(ctx, name, points, radius, mat, clip_mat=None, clip_every=2):
    """Polyline tube/cable with joint spheres and optional retaining clips."""
    for i in range(len(points) - 1):
        ctx.rod(f"{name}_{i}", points[i], points[i + 1], radius, mat)
        if i > 0:
            bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=radius * 1.05,
                                                 location=ctx.w(points[i]))
            joint = bpy.context.object
            joint.name = f"{ctx.prefix}_{name}_Joint_{i}"
            ctx.add(rmk.finish_mesh(joint, mat, smooth=True))
        if clip_mat is not None and i % clip_every == 0:
            mid = tuple((Vector(points[i]) + Vector(points[i + 1])) * 0.5)
            ctx.box(f"{name}_Clip_{i}", (radius * 3.4, radius * 3.4, radius * 1.2),
                    (mid[0], mid[1], mid[2]), clip_mat, bevel=0.0008)


def _finish(ctx, preset_id, style, slot, bevel_targets):
    if ctx.bevel:
        for obj in bevel_targets:
            ctx.bevel(obj)
    for obj in ctx.created:
        obj["rcl_hard_surface_geometry_preset"] = preset_id
        obj["rcl_hard_surface_style"] = style
        obj["rcl_hardpoint_slot"] = slot
    return ctx.created


def _check(prefix):
    if not prefix:
        raise ValueError("Preset prefix must not be empty")


# ------------------------------------------------- 1. contemporary access panel

def create_armor_access_panel_preset(*, prefix, config: ArmorAccessPanelConfig, armor_material, hardware_material,
                                     accent_material=None, hardflow_boolean_apply=None,
                                     hardflow_bevel=None):
    """Recessed armour access panel: boolean pocket, stitched weld frame, bolt circle,
    drain slots and a conduit pigtail."""
    _check(prefix)
    c = _Ctx(prefix, config, hardflow_boolean_apply, hardflow_bevel)
    pw, pd, ph = config.panel_size
    accent = accent_material or hardware_material
    base = c.box("BackingPlate", (pw, pd, ph), (0, 0, ph * 0.5), armor_material, 0.003)
    # Boolean-cut service pocket plus drain slots.
    c.cut(base, "PocketCutter", (pw * 0.62, pd * 0.58, ph * 0.5), (0, 0, ph * 0.95))
    cover = c.box("CoverPlate", (pw * 0.56, pd * 0.52, ph * 0.22), (0, 0, ph * 0.70 + ph * 0.11),
                  armor_material, 0.002)
    for i in range(config.slot_count):
        x = (i / (config.slot_count - 1) - 0.5) * pw * 0.40
        c.cut(cover, f"DrainSlotCutter_{i}", (0.010, pd * 0.12, ph * 0.12), (x, -pd * 0.18, ph * 0.92))
    # Stitch-weld frame around the backing plate.
    z = ph + 0.002
    hx, hy = pw * 0.5, pd * 0.5
    for n, (a, b) in enumerate((((-hx, -hy, z), (hx, -hy, z)), ((-hx, hy, z), (hx, hy, z)),
                                ((-hx, -hy, z), (-hx, hy, z)), ((hx, -hy, z), (hx, hy, z)))):
        _weld_bead(c, f"WeldBead{n}", a, b, 0.0024, hardware_material)
    # Bolt circle on a round inspection boss + perimeter bolts.
    boss = c.add(rmk.make_cylinder(f"{prefix}_InspectionBoss", pd * 0.15, 0.016,
                                   c.w((pw * 0.08, 0, ph * 0.92 + 0.008)), hardware_material,
                                   rotation=c.rotation, vertices=20, bevel=0.002))
    for i in range(config.bolt_circle_count):
        a = 2.0 * math.pi * i / config.bolt_circle_count
        c.add(rmk.make_cylinder(f"{prefix}_BossBolt_{i}", config.fastener_radius * 0.8, 0.007,
                                c.w((pw * 0.08 + pd * 0.115 * math.cos(a), pd * 0.115 * math.sin(a), ph * 0.92 + 0.0195)),
                                hardware_material, rotation=c.rotation, vertices=6))
    _bolt_rect(c, "FrameBolt", hx * 0.88, hy * 0.82, ph + 0.004, config.fastener_radius * 0.8,
               hardware_material)
    # Hinge barrel and ground strap.
    c.rod("HingeBarrel", (-pw * 0.30, hy + 0.006, ph + 0.012), (pw * 0.30, hy + 0.006, ph + 0.012),
          0.008, hardware_material, vertices=10)
    _cable_run(c, "GroundStrap", [(hx * 0.9, -hy * 0.9, z), (hx * 1.05, -hy * 0.9, ph + 0.03),
                                  (hx * 1.05, -hy * 0.6, ph + 0.03)], 0.004, hardware_material)
    return _finish(c, ARMOR_ACCESS_PANEL_PRESET_ID, "contemporary_military", "Sponsons",
                   (base, cover, boss))


# ------------------------------------------------ 2. contemporary louver grille

def create_engine_deck_louver_grille_preset(*, prefix, config: EngineDeckLouverGrilleConfig, armor_material, hardware_material,
                                            accent_material=None, hardflow_boolean_apply=None,
                                            hardflow_bevel=None):
    """Engine-deck grille: boolean-cut slot field, angled louvers, mesh bars, bolted
    frame, weld seams and a braided hydraulic hose."""
    _check(prefix)
    c = _Ctx(prefix, config, hardflow_boolean_apply, hardflow_bevel)
    pw, pd, ph = config.panel_size
    accent = accent_material or hardware_material
    deck = c.box("GrilleDeck", (pw, pd, ph), (0, 0, ph * 0.5), armor_material, 0.003)
    n = config.slot_count + 2
    slot_w = pw * 0.84 / (n * 1.8)
    for i in range(n):
        x = (i / (n - 1) - 0.5) * pw * 0.76
        c.cut(deck, f"SlotCutter_{i}", (slot_w, pd * 0.62, ph * 2.0), (x, 0, ph * 0.5))
        # Angled louver blade inside each slot (30 degrees) and anti-debris bar.
        louver = rmk.make_box(f"{prefix}_Louver_{i}", (slot_w * 1.5, pd * 0.60, 0.004),
                              c.w((x, 0, ph * 0.55)), hardware_material, bevel=0.0006,
                              rotation=(c.rotation.x, c.rotation.y + math.radians(30.0), c.rotation.z))
        c.add(louver)
    for k in (-1.0, 1.0):
        c.box(f"MeshBar_{int(k)}", (pw * 0.80, 0.006, 0.006), (0, k * pd * 0.18, ph + 0.003),
              hardware_material, 0.0008)
    frame_z = ph + 0.003
    _bolt_rect(c, "FrameBolt", pw * 0.46, pd * 0.44, frame_z, config.fastener_radius, hardware_material,
               per_side=4)
    for s, (a, b) in enumerate((((-pw * .5, -pd * .5, frame_z), (pw * .5, -pd * .5, frame_z)),
                                ((-pw * .5, pd * .5, frame_z), (pw * .5, pd * .5, frame_z)))):
        _weld_bead(c, f"SeamWeld{s}", a, b, 0.0032, hardware_material)
    _cable_run(c, "HydraulicHose", [(pw * 0.50, -pd * 0.30, ph * 0.6), (pw * 0.58, -pd * 0.30, ph + 0.05),
                                    (pw * 0.58, 0.0, ph + 0.07), (pw * 0.50, pd * 0.28, ph + 0.03)],
               0.009, hardware_material, hardware_material)
    return _finish(c, ENGINE_DECK_LOUVER_GRILLE_PRESET_ID, "contemporary_military", "Sponsons",
                   (deck,))


# ------------------------------------------------- 3. futuristic hex vent panel

def create_composite_hex_vent_panel_preset(*, prefix, config: CompositeHexVentPanelConfig, armor_material, hardware_material,
                                           accent_material=None, hardflow_boolean_apply=None,
                                           hardflow_bevel=None):
    """Composite plate with boolean-cut hexagonal vent field, flush fastener ring, seam
    light-guide and fibre-optic trunk."""
    _check(prefix)
    c = _Ctx(prefix, config, hardflow_boolean_apply, hardflow_bevel)
    pw, pd, ph = config.panel_size
    accent = accent_material or hardware_material
    plate = c.box("CompositePlate", (pw, pd, ph), (0, 0, ph * 0.5), armor_material, 0.004)
    rows, cols, pitch = 3, max(3, config.slot_count // 2 + 1), min(pw, pd) * 0.16
    for r in range(rows):
        for q in range(cols):
            x = (q - (cols - 1) * 0.5) * pitch * 1.1 + (pitch * 0.55 if r % 2 else 0.0) - (pitch * 0.275 if cols % 2 == 0 else 0)
            y = (r - 1) * pitch * 0.95
            hexcut = rmk.make_cylinder(f"{prefix}_HexCutter_{r}_{q}", pitch * 0.40, ph * 2.0,
                                       c.w((x, y, ph * 0.5)), None, rotation=c.rotation,
                                       vertices=6, bevel=0.0)
            _apply_hardflow_or_builtin_boolean(plate, hexcut, c.boolean)
            for e in range(6):
                a0, a1 = math.pi / 6 + e * math.pi / 3, math.pi / 6 + (e + 1) * math.pi / 3
                rr = pitch * 0.43
                c.rod(f"HexRim_{r}_{q}_{e}", (x + rr * math.cos(a0), y + rr * math.sin(a0), ph + 0.0015),
                      (x + rr * math.cos(a1), y + rr * math.sin(a1), ph + 0.0015), 0.0018, hardware_material, vertices=4)
            c.box(f"BaffleBack_{r}_{q}", (pitch * 0.7, pitch * 0.7, 0.003), (x, y, 0.0015), hardware_material, 0.0)
    _bolt_circle(c, "FlushRingBolt", min(pw, pd) * 0.46, ph + 0.002, config.bolt_circle_count * 2,
                 config.fastener_radius * 0.7, hardware_material)
    c.box("SeamLightGuide", (pw * 0.9, 0.008, 0.004), (0, -pd * 0.46, ph + 0.003), accent, 0.001)
    c.box("SeamLightGuideB", (pw * 0.9, 0.008, 0.004), (0, pd * 0.46, ph + 0.003), accent, 0.001)
    _weld_bead(c, "LaserWeld", (-pw * 0.42, -pd * 0.36, ph + 0.0015), (pw * 0.42, -pd * 0.36, ph + 0.0015), 0.0016,
               hardware_material, beads=18)
    _cable_run(c, "FibreTrunk", [(-pw * 0.44, pd * 0.36, ph + 0.006), (-pw * 0.44, -pd * 0.10, ph + 0.006),
                                 (-pw * 0.30, -pd * 0.26, ph + 0.006), (pw * 0.30, -pd * 0.26, ph + 0.006)],
               0.005, hardware_material, hardware_material)
    return _finish(c, COMPOSITE_HEX_VENT_PANEL_PRESET_ID, "futuristic_military", "Sponsons",
                   (plate,))


# ----------------------------------------- 4. futuristic conduit junction

def create_cable_conduit_junction_preset(*, prefix, config: CableConduitJunctionConfig, armor_material, hardware_material,
                                         accent_material=None, hardflow_boolean_apply=None,
                                         hardflow_bevel=None):
    """Armoured power/data junction manifold: flanged bolt-circle ports, rigid tubes with
    elbows, braided cables with clips, cut cable gland slots and weld fillets."""
    _check(prefix)
    c = _Ctx(prefix, config, hardflow_boolean_apply, hardflow_bevel)
    pw, pd, ph = config.panel_size
    accent = accent_material or hardware_material
    base = c.box("MountPlate", (pw, pd, ph * 0.6), (0, 0, ph * 0.3), armor_material, 0.003)
    body = c.box("ManifoldBody", (pw * 0.46, pd * 0.50, ph * 3.0), (0, 0, ph * 0.6 + ph * 1.5),
                 armor_material, 0.004)
    c.cut(body, "GlandSlotCutter", (pw * 0.30, 0.010, ph * 0.9), (0, -pd * 0.25, ph * 2.2))
    ports = [(-1.0, 0.0), (1.0, 0.0), (0.0, 1.0)]
    top = ph * 0.6 + ph * 1.5
    for i, (dx, dy) in enumerate(ports):
        pos = (dx * pw * 0.13, dy * pd * 0.13, top + ph * 1.5)
        c.add(rmk.make_cylinder(f"{prefix}_PortFlange_{i}", pd * 0.10, 0.012, c.w(pos),
                                hardware_material, rotation=c.rotation, vertices=18, bevel=0.0015))
        _bolt_circle(c, f"PortBolt{i}", pd * 0.078, top + ph * 1.5 + 0.008, 6, config.fastener_radius * 0.8,
                     hardware_material, phase=i * 0.3)
        out = (dx * pw * 0.40, dy * pd * 0.38, top + ph * 1.5 + 0.04)
        gland = (out[0], out[1], ph * 0.6)
        c.add(rmk.make_cylinder(f"{prefix}_PlateGland_{i}", 0.020, 0.014, c.w((gland[0], gland[1], ph * 0.6 + 0.007)),
                                hardware_material, rotation=c.rotation, vertices=10, bevel=0.0015))
        _cable_run(c, f"Tube{i}", [pos, (pos[0], pos[1], out[2]), out, (gland[0], gland[1], ph * 0.6 + 0.014)],
                   0.012 if i < 2 else 0.007, hardware_material, hardware_material)
    for s, a in enumerate((-1.0, 1.0)):
        _weld_bead(c, f"FilletWeld{s}", (a * (pw * 0.23 + 0.002), -pd * 0.25, ph * 0.6 + 0.002),
                   (a * (pw * 0.23 + 0.002), pd * 0.25, ph * 0.6 + 0.002), 0.0028, hardware_material)
    _bolt_rect(c, "BaseBolt", pw * 0.46, pd * 0.42, ph * 0.6 + 0.003, config.fastener_radius,
               hardware_material, per_side=2)
    return _finish(c, CABLE_CONDUIT_JUNCTION_PRESET_ID, "futuristic_military", "Sponsons",
                   (base, body))


DETAILING_PRESETS = {
    ARMOR_ACCESS_PANEL_PRESET_ID: create_armor_access_panel_preset,
    ENGINE_DECK_LOUVER_GRILLE_PRESET_ID: create_engine_deck_louver_grille_preset,
    COMPOSITE_HEX_VENT_PANEL_PRESET_ID: create_composite_hex_vent_panel_preset,
    CABLE_CONDUIT_JUNCTION_PRESET_ID: create_cable_conduit_junction_preset,
}

DETAILING_METADATA = {
    ARMOR_ACCESS_PANEL_PRESET_ID: {
        "style": "contemporary_military", "slot": "Sponsons",
        "displayName": "Armoured Access Panel with Bolt Circle v1",
        "engineeringRole": "utility_sponson"},
    ENGINE_DECK_LOUVER_GRILLE_PRESET_ID: {
        "style": "contemporary_military", "slot": "Sponsons",
        "displayName": "Engine Deck Louver Grille v1",
        "engineeringRole": "thermal_radiator"},
    COMPOSITE_HEX_VENT_PANEL_PRESET_ID: {
        "style": "futuristic_military", "slot": "Sponsons",
        "displayName": "Composite Hex Vent Panel v1",
        "engineeringRole": "thermal_radiator"},
    CABLE_CONDUIT_JUNCTION_PRESET_ID: {
        "style": "futuristic_military", "slot": "Sponsons",
        "displayName": "Armoured Cable & Conduit Junction Manifold v1",
        "engineeringRole": "power_containment"},
}
