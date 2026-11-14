"""Procedural & F-Curve unit animation algorithms for Rogue Command (Python + Blender 4.5.14).

Provides:
1. Quadratic Bezier step-arc trajectories with strict Z=0 stance grounding for
   biped, quadruped, and hexapod tripod gaits.
2. Offline Second-Order Dynamics (f, z, r) curve synthesis matching the runtime
   C# ``SecondOrderDynamics`` solver.
3. Deterministic baked F-Curve micro-vibration / secondary oscillation on
   non-root bones inside Blender ``Action`` objects while strictly preserving
   the ``Bone_Root = (0,0,0)`` In-Place Locomotion Invariant and contract anchor frames.
"""

from __future__ import annotations

import math
from typing import Any, Iterable, Sequence


def evaluate_quadratic_bezier_point(
    t: float,
    p0: tuple[float, float, float],
    p1: tuple[float, float, float],
    p2: tuple[float, float, float],
) -> tuple[float, float, float]:
    """Evaluates B(t) = (1-t)^2 P0 + 2(1-t)t P1 + t^2 P2 for t clamped in [0, 1]."""
    u = max(0.0, min(1.0, float(t)))
    one_minus_u = 1.0 - u
    w0 = one_minus_u * one_minus_u
    w1 = 2.0 * one_minus_u * u
    w2 = u * u
    return (
        w0 * p0[0] + w1 * p1[0] + w2 * p2[0],
        w0 * p0[1] + w1 * p1[1] + w2 * p2[1],
        w0 * p0[2] + w1 * p1[2] + w2 * p2[2],
    )


def get_gait_phase_offsets(leg_type: str | None) -> dict[str, float]:
    """Returns canonical normalized [0, 1) phase offsets per leg tag."""
    normalized = (leg_type or "").strip().lower()
    if normalized == "biped":
        return {"L": 0.5, "R": 0.0}
    if normalized in ("quad", "quadruped", "hexapod"):
        return {
            "FL": 0.5,
            "RR": 0.5,
            "ML": 0.0,
            "FR": 0.0,
            "RL": 0.0,
            "MR": 0.5,
        }
    return {}


def sample_foot_step_arc_frames(
    frames: Sequence[int] = (1, 5, 10, 15, 20, 25, 30, 35, 40),
    cycle_length: int = 40,
    phase_offset: float = 0.0,
    duty_factor: float = 0.5,
    step_height: float = 0.09,
    stride_forward: float = 0.04,
) -> dict[int, tuple[float, float, float]]:
    """Samples in-place Bezier foot lift (swing) and grounded Z=0 contact (stance).

    In the stance interval (phase in [0, duty_factor)), the foot stays strictly on
    Z=0.0 with zero ground penetration. In the swing interval ([duty_factor, 1.0)),
    a quadratic Bezier arc lifts the foot smoothly up to ``step_height`` at
    mid-swing and sets it back down at Z=0.0 cleanly for loop closure.
    """
    if cycle_length <= 1:
        raise ValueError(f"cycle_length must be > 1, got {cycle_length}")
    if not 0.1 <= duty_factor <= 0.9:
        raise ValueError(f"duty_factor must be in [0.1, 0.9], got {duty_factor}")

    safe_height = max(0.0, float(step_height))
    safe_stride = max(0.0, float(stride_forward))
    p0 = (0.0, 0.0, 0.0)
    # Apex control point at 2 * step_height ensures B(0.5).z == step_height and B(0.5).y == 0.5 * stride_forward.
    p1 = (0.0, safe_stride, safe_height * 2.0)
    p2 = (0.0, 0.0, 0.0)

    out: dict[int, tuple[float, float, float]] = {}
    span = float(cycle_length - 1)
    swing_duration = 1.0 - duty_factor

    for frame in frames:
        wrapped_offset = 0 if int(frame) == cycle_length else (int(frame) - 1)
        base_phase = ((float(wrapped_offset) / span) + phase_offset) % 1.0
        if base_phase < duty_factor:
            # Grounded stance phase: Z stays strictly 0.0 with zero local foot slide.
            out[int(frame)] = (0.0, 0.0, 0.0)
        else:
            swing_u = (base_phase - duty_factor) / swing_duration
            pt = evaluate_quadratic_bezier_point(swing_u, p0, p1, p2)
            out[int(frame)] = (round(pt[0], 5), round(pt[1], 5), max(0.0, round(pt[2], 5)))

    return out


def evaluate_second_order_response(
    targets: Sequence[float],
    dt: float,
    f: float = 4.2,
    z: float = 0.58,
    r: float = 1.2,
    initial_value: float | None = None,
) -> list[float]:
    """Offline semi-implicit Euler second-order dynamics solver matching C#."""
    if not targets:
        return []
    if dt <= 1e-6:
        raise ValueError(f"dt must be positive, got {dt}")

    safe_f = max(0.05, float(f))
    safe_z = max(0.0, float(z))
    safe_r = float(r)

    w = 2.0 * math.pi * safe_f
    k1 = safe_z / (math.pi * safe_f)
    k2 = 1.0 / (w * w)
    k3 = (safe_r * safe_z) / w

    y = float(targets[0] if initial_value is None else initial_value)
    xp = y
    yd = 0.0
    result: list[float] = []

    k2_stable = max(k2, (dt * dt * 0.5) + (dt * k1 * 0.5), dt * k1)
    for x in targets:
        xf = float(x)
        xd = (xf - xp) / dt
        xp = xf
        y = y + dt * yd
        yd = yd + dt * (xf + k3 * xd - y - k1 * yd) / k2_stable
        result.append(y)
    return result


def evaluate_micro_vibration_envelope(
    frame: int,
    frame_start: int,
    frame_end: int,
    anchor_frames: Iterable[int] = (),
) -> float:
    """Returns a [0, 1] envelope that vanishes smoothly at clip boundaries and anchors."""
    if frame <= frame_start or frame >= frame_end or frame_end <= frame_start:
        return 0.0
    anchors = sorted({int(frame_start), *(int(a) for a in anchor_frames), int(frame_end)})
    for idx in range(len(anchors) - 1):
        left = anchors[idx]
        right = anchors[idx + 1]
        if frame == left or frame == right:
            return 0.0
        if left < frame < right:
            u = float(frame - left) / float(right - left)
            return math.sin(math.pi * u)
    u = float(frame - frame_start) / float(frame_end - frame_start)
    return math.sin(math.pi * u)


def apply_baked_fcurve_micro_vibration_bpy(
    armature_obj: Any,
    action: Any,
    bone_names: Sequence[str],
    frame_start: int,
    frame_end: int,
    amplitude_rad: float = 0.008,
    frequency_cycles: float = 3.0,
    anchor_frames: Iterable[int] = (),
    fps: float = 24.0,
) -> int:
    """Adds deterministic baked secondary oscillation keyframes inside Blender.

    Never writes to ``Bone_Root`` and skips missing pose bones or any anchor
    frame in ``anchor_frames`` so existing keyframe contracts stay exact.
    Returns the number of inserted keyframes.
    """
    if armature_obj is None or action is None or not getattr(armature_obj, "pose", None):
        return 0

    import mathutils  # Available when running inside Blender bpy

    prev_action = armature_obj.animation_data.action if armature_obj.animation_data else None
    if not armature_obj.animation_data:
        armature_obj.animation_data_create()
    armature_obj.animation_data.action = action

    inserted = 0
    span = max(1, frame_end - frame_start)
    anchor_set = {int(frame_start), *(int(a) for a in anchor_frames), int(frame_end)}

    try:
        for bone_idx, bone_name in enumerate(bone_names):
            if not bone_name or bone_name.strip().lower() == "bone_root":
                continue
            pbone = armature_obj.pose.bones.get(bone_name)
            if pbone is None:
                continue

            pbone.rotation_mode = "XYZ"
            phase_shift = bone_idx * 1.15
            for frame in range(frame_start + 1, frame_end):
                if frame in anchor_set:
                    continue
                env = evaluate_micro_vibration_envelope(
                    frame, frame_start, frame_end, anchor_set
                )
                if env <= 1e-4:
                    continue
                u = float(frame - frame_start) / float(span)
                osc = math.sin((2.0 * math.pi * frequency_cycles * u) + phase_shift)
                pitch = osc * amplitude_rad * env
                roll = math.cos((2.0 * math.pi * frequency_cycles * 0.5 * u) + phase_shift) * (amplitude_rad * 0.5) * env
                pbone.rotation_euler = mathutils.Euler((pitch, 0.0, roll), "XYZ")
                pbone.keyframe_insert(data_path="rotation_euler", frame=frame)
                inserted += 1
    finally:
        if prev_action is not None:
            armature_obj.animation_data.action = prev_action

    return inserted
