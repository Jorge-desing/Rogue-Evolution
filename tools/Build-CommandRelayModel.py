"""Build the Wave 7 Command Relay tactical relay carrier and its PBR atlas."""

from __future__ import annotations

from pathlib import Path
import sys

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

from VisualModelAuthoringSupport import (
    add_box,
    run_preset_build,
)


def refine(builder) -> None:
    """Add protected C2 shelter panels to the Wave 7 relay carrier."""
    p = builder.palette
    for side, tag in ((-1, "L"), (1, "R")):
        x = side * 0.68
        add_box(builder, f"CommandRelayArmor_C2Panel_{tag}", (x, -0.62, 1.58), (0.10, 1.20, 0.56), p.light_armor)
        add_box(builder, f"CommandRelayWarning_C2Stripe_{tag}", (x + side * 0.055, -0.62, 1.60), (0.018, 0.72, 0.06), p.warning)
    builder.engine_audio_loc = builder.engine_audio_loc or builder.center_of_mass_loc
    builder.weapon_audio_loc = builder.weapon_audio_loc or builder.gun_pivot


def build() -> None:
    run_preset_build(
        [
            "--name", "CommandRelay",
            "--preset", "command_relay",
            "--camo", "digital",
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
