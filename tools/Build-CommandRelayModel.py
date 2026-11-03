"""Build the original Command Relay C4ISR vehicle and its PBR atlas."""

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
    """Add a distinct command mast, relay dish, and protected C2 shelter details."""
    p = builder.palette
    mast_x, mast_y = -0.58, -1.44
    add_z_axis_post(builder, "CommandRelaySensor_CommandMast", (mast_x, mast_y, 2.10), 0.06, 1.30, p.gunmetal)
    add_y_axis_tube(builder, "CommandRelaySensor_CommandCrossbar", (mast_x, mast_y, 2.72), 0.045, 1.15, p.light_armor)
    add_uv_ellipsoid(builder, "CommandRelaySensor_CommandBeacon", (mast_x, mast_y, 2.86), (0.16, 0.16, 0.11), p.emissive)
    for side, tag in ((-1, "L"), (1, "R")):
        x = side * 0.68
        add_box(builder, f"CommandRelayArmor_C2Panel_{tag}", (x, -0.62, 1.58), (0.10, 1.20, 0.56), p.light_armor)
        add_box(builder, f"CommandRelayWarning_C2Stripe_{tag}", (x + side * 0.055, -0.62, 1.60), (0.018, 0.72, 0.06), p.warning)
        add_link(builder, f"CommandRelayJoint_MastBrace_{tag}", (side * 0.42, -1.05, 1.68), (mast_x, mast_y, 2.20), 0.030, 0.024, p.gunmetal)
    builder.engine_audio_loc = builder.engine_audio_loc or builder.center_of_mass_loc
    builder.weapon_audio_loc = builder.weapon_audio_loc or builder.gun_pivot


def build() -> None:
    run_preset_build(
        [
            "--name", "CommandRelay",
            "--archetype", "command_hq_rig",
            "--weapon", "emp_emitter",
            "--faction", "vanguard",
            "--camo", "digital",
            "--wear", "field_tested",
            "--structural-wear-mask",
            "--hull-greebles",
            "--tactical-markings",
            "--aps-suite",
            "--thermal-radiators",
            "--radar", "--antenna",
            "--rig", "--animate",
            "--colliders", "--gameplay-json",
            "--bake-atlas", "--single-material", "--atlas-resolution", "2048",
            "--target-budget", "7500",
            "--multiview", "--render-icons",
            "--blend-path", "content/source-art/command-relay.blend",
            "--fbx-path", "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/CommandRelay.fbx",
            "--card-path", "unity/RogueCommandAssetAuthoring/Assets/TacticalOperations/CommandRelayCard.png",
            "--preview-path", "artifacts/previews/CommandRelay.png",
            "--report-json", "artifacts/previews/CommandRelay-report.json",
        ],
        refine,
    )


if __name__ == "__main__":
    build()
