"""Build the Wave 7 Gravitas Breaker gravity siege breaker and its PBR atlas."""

from __future__ import annotations

from pathlib import Path
import sys

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

from VisualModelAuthoringSupport import add_box, add_link, run_preset_build


def refine(builder) -> None:
    """Add protected overload conduits and cheek armor to the Wave 7 citadel."""
    p = builder.palette
    for side, tag in ((-1, "L"), (1, "R")):
        add_link(builder, f"GravitasBreakerJoint_OverloadConduit_{tag}", (side * 0.48, -0.35, 1.12), (side * 0.72, 0.64, 1.30), 0.055, 0.042, p.warning)
        add_box(builder, f"GravitasBreakerArmor_CheekPlate_{tag}", (side * 0.88, 0.42, 1.15), (0.18, 0.70, 0.42), p.light_armor)
    builder.damage_smoke_loc = builder.damage_smoke_loc or builder.center_of_mass_loc
    builder.damage_fire_loc = builder.damage_fire_loc or builder.turret_center
    builder.engine_audio_loc = builder.engine_audio_loc or builder.center_of_mass_loc
    builder.weapon_audio_loc = builder.weapon_audio_loc or builder.gun_pivot


def build() -> None:
    run_preset_build(
        [
            "--name", "GravitasBreaker",
            "--preset", "gravitas_breaker",
            "--camo", "splinter",
            "--structural-wear-mask",
            "--hull-greebles",
            "--tactical-markings",
            "--aps-suite",
            "--stowage-kit",
            "--slat-armor",
            "--era-bricks",
            "--thermal-radiators",
            "--suspension-arms",
            "--mantlet-boot",
            "--rig", "--animate",
            "--colliders", "--gameplay-json",
            "--bake-atlas", "--single-material", "--atlas-resolution", "2048",
            "--target-budget", "7500",
            "--multiview", "--render-icons",
            "--blend-path", "content/source-art/gravitas-breaker.blend",
            "--fbx-path", "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/GravitasBreaker.fbx",
            "--card-path", "unity/RogueCommandAssetAuthoring/Assets/TacticalOperations/GravitasBreakerCard.png",
            "--preview-path", "artifacts/previews/GravitasBreaker.png",
            "--report-json", "artifacts/previews/GravitasBreaker-report.json",
        ],
        refine,
    )


if __name__ == "__main__":
    build()
