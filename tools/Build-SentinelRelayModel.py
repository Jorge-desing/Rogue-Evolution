"""Build the Wave 7 Sentinel Relay nanite support rover and its PBR atlas."""

from __future__ import annotations

from pathlib import Path
import math
import sys

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

from ActiveUnitVisualEnhancements import apply_wave34_details
import RogueModelKit as rmk
from VisualModelAuthoringSupport import (
    add_box,
    add_link,
    add_uv_ellipsoid,
    add_y_axis_tube,
    add_z_axis_post,
    remove_objects,
    run_preset_build,
    simplify_all_terrain_wheels,
    simplify_low_value_bevel_details,
)

SENTINEL_RELAY_CAB_COWL_STATIONS = (
    (-0.125, 0.0, 1.02, 0.49, 0.18),
    (0.05, 0.0, 1.02, 0.58, 0.21),
    (0.65, 0.0, 1.02, 0.59, 0.21),
    (0.825, 0.0, 0.98, 0.48, 0.17),
)
SENTINEL_RELAY_EQUIPMENT_BAY_STATIONS = (
    (-1.225, 0.0, 0.98, 0.54, 0.21),
    (-1.05, 0.0, 0.98, 0.62, 0.24),
    (-0.25, 0.0, 0.98, 0.62, 0.24),
    (-0.075, 0.0, 0.94, 0.54, 0.20),
)


def add_cowl_service_panels(builder):
    """Replace floating bay covers with compact, fitted cowl service panels."""
    p = builder.palette
    remove_objects(builder, lambda name: (
        name.startswith("SentinelRelayArmor_ShieldServicePanel_")
        or name.startswith("SentinelRelayWarning_ServiceStripe_")
    ))
    added = []
    for side, tag in ((-1, "L"), (1, "R")):
        added.append(add_box(
            builder,
            f"SentinelRelayArmor_CowlServicePanel_{tag}",
            (side * 0.61, 0.68, 1.02),
            (0.05, 0.22, 0.18),
            p.light_armor,
        ))
        added.append(add_box(
            builder,
            f"SentinelRelayWarning_CowlServiceStripe_{tag}",
            (side * 0.640, 0.68, 1.085),
            (0.010, 0.13, 0.018),
            p.warning,
        ))
        for index, (y, z) in enumerate(((0.59, 0.96), (0.59, 1.08), (0.77, 0.96), (0.77, 1.08)), start=1):
            added.append(rmk.make_cylinder(
                f"SentinelRelayFastener_CowlPanel_{tag}_{index}",
                radius=0.009,
                depth=0.010,
                location=(side * 0.639, y, z),
                material=p.gunmetal,
                vertices=8,
                bevel=0.001,
                rotation=(0.0, math.radians(90.0), 0.0),
            ))
            builder.objects.append(added[-1])
    return added


def replace_relay_braces(builder, material):
    """Attach each diagonal relay brace between its bay shell and mast stage."""
    remove_objects(builder, lambda name: name.startswith("SentinelRelayJoint_RelayBrace_"))
    added = []
    for side, tag in ((-1, "L"), (1, "R")):
        added.append(add_link(
            builder,
            f"SentinelRelayJoint_RelayBrace_{tag}",
            (side * 0.58843, -0.72, 1.10732),
            (side * 0.06, -0.72, 1.78),
            0.035,
            0.028,
            material,
        ))
    return added


def remove_unseated_sentinel_clusters(builder):
    """Remove detached pioneer racks and gussets until a host mount is proven."""
    return remove_objects(builder, lambda name: (
        name.startswith("SentinelRelayArmor_Gusset_")
        or name.startswith("SentinelRelayPioneerRack_")
        or name.startswith("SentinelRelayArmor_PioneerRack_")
    ))


def refine(builder) -> None:
    builder.locomotion_only = False  # Idle/FireRecoil/Death restaurados (ataque por RCL_Fire)
    """Add a readable relay mast and shield-service hardware to the support chassis."""
    p = builder.palette
    for name, stations, material in (
        ("SentinelRelayArmor_CabCowl", SENTINEL_RELAY_CAB_COWL_STATIONS, p.light_armor),
        ("SentinelRelayArmor_EquipmentBay", SENTINEL_RELAY_EQUIPMENT_BAY_STATIONS, p.primary_armor),
    ):
        remove_objects(builder, lambda object_name, target=name: object_name == target)
        builder.objects.append(rmk.make_lofted_superellipse_shell(
            name, stations, material,
            axis="Y", segments=16, exponent=0.66, smooth=True,
        ))
    mast_x, mast_y = 0.0, -0.72
    add_z_axis_post(builder, "SentinelRelaySensor_RelayMast", (mast_x, mast_y, 1.55), 0.060, 0.98, p.gunmetal)
    add_y_axis_tube(builder, "SentinelRelaySensor_RelayCrossbar", (mast_x, mast_y, 2.02), 0.045, 0.68, p.light_armor)
    add_uv_ellipsoid(builder, "SentinelRelaySensor_RelayBeacon", (mast_x, mast_y, 2.12), (0.13, 0.13, 0.10), p.emissive)
    add_cowl_service_panels(builder)
    replace_relay_braces(builder, p.gunmetal)
    builder.objects.extend(apply_wave34_details(
        "SentinelRelay",
        objects=builder.objects,
        materials={"armor": p.primary_armor, "gunmetal": p.gunmetal},
    ))
    remove_unseated_sentinel_clusters(builder)
    builder.engine_audio_loc = builder.engine_audio_loc or builder.center_of_mass_loc
    builder.weapon_audio_loc = builder.weapon_audio_loc or builder.gun_pivot
    simplify_all_terrain_wheels(builder)
    simplify_low_value_bevel_details(builder)


def build() -> None:
    run_preset_build(
        [
            "--name", "SentinelRelay",
            "--preset", "sentinel_relay",
            "--camo", "none",
            "--primary-color", "#244B56",
            "--light-armor-color", "#69A9A4",
            "--emissive-color", "#54F0DE",
            "--wear", "field_tested",
            "--structural-wear-mask",
            "--rig", "--animate",
            "--colliders", "--gameplay-json",
            "--bake-atlas", "--single-material", "--atlas-resolution", "2048",
            "--multiview",
            "--blend-path", "content/source-art/sentinel-relay.blend",
            "--fbx-path", "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/SentinelRelay.fbx",
            "--preview-path", "artifacts/previews/SentinelRelay.png",
            "--report-json", "artifacts/previews/SentinelRelay-report.json",
        ],
        refine,
    )


if __name__ == "__main__":
    build()
