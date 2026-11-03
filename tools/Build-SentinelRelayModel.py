"""Build the original Sentinel Relay support rover and its PBR atlas."""

from __future__ import annotations

from pathlib import Path
import sys

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

from VisualModelAuthoringSupport import (
    add_box,
    add_link,
    add_uv_ellipsoid,
    add_y_axis_tube,
    add_z_axis_post,
    run_preset_build,
)


def refine(builder) -> None:
    """Add a readable relay mast and shield-service hardware to the support chassis."""
    p = builder.palette
    mast_x, mast_y = 0.0, -0.72
    add_z_axis_post(builder, "SentinelRelaySensor_RelayMast", (mast_x, mast_y, 1.72), 0.075, 1.35, p.gunmetal)
    add_y_axis_tube(builder, "SentinelRelaySensor_RelayCrossbar", (mast_x, mast_y, 2.34), 0.055, 0.95, p.light_armor)
    add_uv_ellipsoid(builder, "SentinelRelaySensor_RelayBeacon", (mast_x, mast_y, 2.48), (0.18, 0.18, 0.14), p.emissive)
    for side, tag in ((-1, "L"), (1, "R")):
        x = side * 0.83
        add_box(builder, f"SentinelRelayArmor_ShieldServicePanel_{tag}", (x, -0.38, 1.10), (0.12, 0.95, 0.46), p.light_armor)
        add_box(builder, f"SentinelRelayWarning_ServiceStripe_{tag}", (x + side * 0.066, -0.38, 1.12), (0.018, 0.56, 0.07), p.warning)
        add_link(builder, f"SentinelRelayJoint_RelayBrace_{tag}", (side * 0.46, -0.68, 1.25), (x, mast_y, 1.85), 0.035, 0.028, p.gunmetal)
    builder.engine_audio_loc = builder.engine_audio_loc or builder.center_of_mass_loc
    builder.weapon_audio_loc = builder.weapon_audio_loc or builder.gun_pivot


def build() -> None:
    run_preset_build(
        [
            "--name", "SentinelRelay",
            "--archetype", "shield_truck",
            "--weapon", "arc",
            "--faction", "vanguard",
            "--camo", "none",
            "--primary-color", "#244B56",
            "--light-armor-color", "#69A9A4",
            "--emissive-color", "#54F0DE",
            "--wear", "field_tested",
            "--structural-wear-mask",
            "--hull-greebles",
            "--tactical-markings",
            "--thermal-radiators",
            "--rig", "--animate",
            "--colliders", "--gameplay-json",
            "--bake-atlas", "--single-material", "--atlas-resolution", "2048",
            "--target-budget", "6500",
            "--multiview", "--render-icons",
            "--blend-path", "content/source-art/sentinel-relay.blend",
            "--fbx-path", "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/SentinelRelay.fbx",
            "--card-path", "unity/RogueCommandAssetAuthoring/Assets/TacticalOperations/SentinelRelayCard.png",
            "--preview-path", "artifacts/previews/SentinelRelay.png",
            "--report-json", "artifacts/previews/SentinelRelay-report.json",
        ],
        refine,
    )


if __name__ == "__main__":
    build()
