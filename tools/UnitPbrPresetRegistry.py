"""Authoritative Unit-to-PBR Preset Registry for Rogue Command.

Maps all 44 Material Maker / PBR space-military tactical presets across all units
in all content packs:
- `content/packs/sample-expansion/content.json` (sample:scout-tank)
- `content/packs/sample-native-expansion/content.json` (9 native-template units)
- `content/packs/tactical-operations/content.json` (9 tactical-operations units)

Every preset is strictly grounded in vehicle roles, weapon envelopes, battlefield
doctrine, and authoring source assets under `content/source-art/`.
"""

from __future__ import annotations

import importlib.util
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Mapping, Optional, Sequence, Tuple

REPO_ROOT = Path(__file__).resolve().parents[1]
TEMPLATE_PREPARE_PATH = REPO_ROOT / "tools" / "Prepare-MaterialMakerTacticalTemplate.py"


def _load_available_presets() -> Mapping[str, dict]:
    """Load the PRESETS dictionary from Prepare-MaterialMakerTacticalTemplate.py."""
    spec = importlib.util.spec_from_file_location(
        "prepare_mm_tactical_template", TEMPLATE_PREPARE_PATH
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {TEMPLATE_PREPARE_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.PRESETS


@dataclass(frozen=True)
class UnitPbrProfile:
    """Thematic PBR preset configuration for a content pack unit."""

    unit_id: str
    pack_id: str
    model_name: str
    canonical_source_art: str
    primary_preset: str
    tactical_variant_presets: Mapping[str, str] = field(default_factory=dict)
    role_doctrine: str = ""
    primary_color_tint: str = "#808080"
    variant_color_tints: Mapping[str, str] = field(default_factory=dict)


# Authoritative Registry of all units across all packs and their 44 preset mappings
UNIT_PBR_REGISTRY: Mapping[str, UnitPbrProfile] = {
    # -------------------------------------------------------------------------
    # Pack: sample-expansion (1 unit)
    # -------------------------------------------------------------------------
    "sample:scout-tank": UnitPbrProfile(
        unit_id="sample:scout-tank",
        pack_id="sample-expansion",
        model_name="ScoutTank",
        canonical_source_art="content/source-art/sandlance-scout.blend",
        primary_preset="gunmetal_chassis",
        tactical_variant_presets={
            "desert": "desert_camo",
            "martian": "martian_iron_rust",
        },
        role_doctrine="Light frontline scouting and perimeter patrol.",
        primary_color_tint="#565f69",
        variant_color_tints={
            "desert": "#c2a278",
            "martian": "#8b4528",
        },
    ),
    # -------------------------------------------------------------------------
    # Pack: sample-native-expansion (9 units)
    # -------------------------------------------------------------------------
    "sample-native:scout-tank": UnitPbrProfile(
        unit_id="sample-native:scout-tank",
        pack_id="sample-native-expansion",
        model_name="SandlanceScout",
        canonical_source_art="content/source-art/sandlancescout-rigged.blend",
        primary_preset="olive_armor",
        tactical_variant_presets={
            "gunmetal": "gunmetal_chassis",
            "desert": "desert_camo",
            "sniper-camo": "longshot_sniper_camo",
        },
        role_doctrine="High-mobility recon, rapid outpost flanker, and forward sniper escort.",
        primary_color_tint="#4d5c43",
        variant_color_tints={
            "gunmetal": "#4c555d",
            "desert": "#b89f76",
            "sniper-camo": "#36453b",
        },
    ),
    "sample-native:heavy-artillery": UnitPbrProfile(
        unit_id="sample-native:heavy-artillery",
        pack_id="sample-native-expansion",
        model_name="ColossusSiegePlatform",
        canonical_source_art="content/source-art/colossussiegeplatform-rigged.blend",
        primary_preset="hypervelocity_railgun_cermet",
        tactical_variant_presets={
            "flak-armor": "tungsten_heavy_flak_plating",
            "subterranean": "subterranean_mole_borer",
            "martian": "martian_iron_rust",
            "tungsten": "battleship_tungsten",
        },
        role_doctrine="Extreme-range kinetic bombardier and planetary bombardment platform.",
        primary_color_tint="#3d444d",
        variant_color_tints={
            "flak-armor": "#464a4e",
            "subterranean": "#50443a",
            "martian": "#7a3e26",
            "tungsten": "#35393d",
        },
    ),
    "sample-native:assault-walker": UnitPbrProfile(
        unit_id="sample-native:assault-walker",
        pack_id="sample-native-expansion",
        model_name="IroncladWalker",
        canonical_source_art="content/source-art/ironclad-walker.blend",
        primary_preset="exoskeleton_reactive_scale",
        tactical_variant_presets={
            "reentry-ablative": "plasma_cinder_ablative",
            "drop-pod-cermet": "orbital_drop_pod_cermet",
        },
        role_doctrine="Articulated all-terrain frontline combat walker and drop-assault shock unit.",
        primary_color_tint="#525b63",
        variant_color_tints={
            "reentry-ablative": "#2d2826",
            "drop-pod-cermet": "#41474e",
        },
    ),
    "sample-native:atlas-crane-mech": UnitPbrProfile(
        unit_id="sample-native:atlas-crane-mech",
        pack_id="sample-native-expansion",
        model_name="AtlasCraneMech",
        canonical_source_art="content/source-art/atlas-crane-mech.blend",
        primary_preset="hazard_industrial",
        tactical_variant_presets={
            "zero-g-dock": "zero_g_docking_alloy",
        },
        role_doctrine="Heavy logistical engineering, field fabrication support, and orbital construction.",
        primary_color_tint="#c89228",
        variant_color_tints={
            "zero-g-dock": "#6c7784",
        },
    ),
    "sample-native:bastion-siege-mech": UnitPbrProfile(
        unit_id="sample-native:bastion-siege-mech",
        pack_id="sample-native-expansion",
        model_name="BastionSiegeMech",
        canonical_source_art="content/source-art/bastion-siege-mech.blend",
        primary_preset="graphene_ablative_lattice",
        tactical_variant_presets={
            "radar-absorbent": "stealth_radar_absorbent_ferrite",
            "subzero-cryo": "cryo_glacier_camo",
        },
        role_doctrine="Anchored heavy fortification siege breaker and fortified perimeter anchor.",
        primary_color_tint="#2e343b",
        variant_color_tints={
            "radar-absorbent": "#1e2124",
            "subzero-cryo": "#7292a6",
        },
    ),
    "sample-native:arachnid-hexapod": UnitPbrProfile(
        unit_id="sample-native:arachnid-hexapod",
        pack_id="sample-native-expansion",
        model_name="ArachnidHexapod",
        canonical_source_art="content/source-art/arachnidhexapod-rigged.blend",
        primary_preset="void_stalker_metamaterial",
        tactical_variant_presets={
            "pulsar-shield": "pulsar_magnetic_shield",
            "void-composite": "stealth_void_composite",
        },
        role_doctrine="Multi-legged all-terrain hunter-killer, ambush missile platform, and phase predator.",
        primary_color_tint="#25292e",
        variant_color_tints={
            "pulsar-shield": "#1c3247",
            "void-composite": "#181a1d",
        },
    ),
    "sample-native:overlord-command-hq": UnitPbrProfile(
        unit_id="sample-native:overlord-command-hq",
        pack_id="sample-native-expansion",
        model_name="OverlordCommandHQ",
        canonical_source_art="content/source-art/overlordcommandhq-rigged.blend",
        primary_preset="fusion_reactor_core_plating",
        tactical_variant_presets={
            "solar-radiator": "solar_gold_radiator",
        },
        role_doctrine="Strategic theatre command hub, tactical aura coordinator, and power nexus.",
        primary_color_tint="#3a4149",
        variant_color_tints={
            "solar-radiator": "#d19f32",
        },
    ),
    "sample-native:skirmish-bike": UnitPbrProfile(
        unit_id="sample-native:skirmish-bike",
        pack_id="sample-native-expansion",
        model_name="SkirmishStrikeBike",
        canonical_source_art="content/source-art/skirmishstrikebike-rigged.blend",
        primary_preset="obsidian_strike_interceptor",
        tactical_variant_presets={
            "cyber-nanoweave": "cyber_nanoweave",
            "arctic-ceramic": "cerberus_white_ceramic",
        },
        role_doctrine="Ultra-fast strike interdiction, harassment raider, and electronic disruption.",
        primary_color_tint="#1f2226",
        variant_color_tints={
            "cyber-nanoweave": "#151e28",
            "arctic-ceramic": "#e1e6eb",
        },
    ),
    "sample-native:surge-tank": UnitPbrProfile(
        unit_id="sample-native:surge-tank",
        pack_id="sample-native-expansion",
        model_name="SurgeArcTank",
        canonical_source_art="content/source-art/surgearctank-rigged.blend",
        primary_preset="arc_discharge_conduit",
        tactical_variant_presets={
            "ion-thruster": "ion_thruster_titanium_grate",
            "antimatter-lattice": "antimatter_containment_lattice",
        },
        role_doctrine="Directed-energy electro-static assault vehicle and arc discharge vanguard.",
        primary_color_tint="#2a333f",
        variant_color_tints={
            "ion-thruster": "#425363",
            "antimatter-lattice": "#1f182c",
        },
    ),
    # -------------------------------------------------------------------------
    # Pack: tactical-operations (9 units)
    # -------------------------------------------------------------------------
    "tactical-operations:tidebreaker-skirmisher": UnitPbrProfile(
        unit_id="tactical-operations:tidebreaker-skirmisher",
        pack_id="tactical-operations",
        model_name="TidebreakerHovercraft",
        canonical_source_art="content/source-art/tidebreakerhovercraft-rigged.blend",
        primary_preset="anodized_cobalt",
        tactical_variant_presets={
            "tachyon-radiator": "tachyon_warp_radiator",
            "cryo-glacier": "cryo_glacier_camo",
        },
        role_doctrine="Amphibious hover raider, delta skirmisher, and high-speed littoral assault.",
        primary_color_tint="#2c4563",
        variant_color_tints={
            "tachyon-radiator": "#1f334a",
            "cryo-glacier": "#6c8899",
        },
    ),
    "tactical-operations:salvage-hauler": UnitPbrProfile(
        unit_id="tactical-operations:salvage-hauler",
        pack_id="tactical-operations",
        model_name="SalvageHauler",
        canonical_source_art="content/source-art/salvagehauler-rigged.blend",
        primary_preset="salvage_reclamation_ironclad",
        tactical_variant_presets={
            "hazard-eva": "hazard_industrial",
            "minefield-carrier": "minefield_ordnance_carrier",
        },
        role_doctrine="Battlefield wreckage reclamation, scrap recovery, and combat minelayer platform.",
        primary_color_tint="#484e55",
        variant_color_tints={
            "hazard-eva": "#c28e2b",
            "minefield-carrier": "#424b3e",
        },
    ),
    "tactical-operations:skyguard-sam": UnitPbrProfile(
        unit_id="tactical-operations:skyguard-sam",
        pack_id="tactical-operations",
        model_name="SkyguardSAM",
        canonical_source_art="content/source-art/skyguardsam-rigged.blend",
        primary_preset="orbital_titanium",
        tactical_variant_presets={
            "solar-shield": "solar_flare_radiation_shield",
            "sniper-camo": "longshot_sniper_camo",
        },
        role_doctrine="Surface-to-air anti-aerospace interdiction, missile air umbrella, and tracking radar.",
        primary_color_tint="#545f6a",
        variant_color_tints={
            "solar-shield": "#6b5832",
            "sniper-camo": "#36453b",
        },
    ),
    "tactical-operations:bulwark-siege-tank": UnitPbrProfile(
        unit_id="tactical-operations:bulwark-siege-tank",
        pack_id="tactical-operations",
        model_name="BulwarkSiegeTank",
        canonical_source_art="content/source-art/bulwarksiegetank-rigged.blend",
        primary_preset="dreadnought_adamantine_hull",
        tactical_variant_presets={
            "battleship-tungsten": "battleship_tungsten",
            "ordnance-carrier": "minefield_ordnance_carrier",
        },
        role_doctrine="Ultra-heavy breakthrough tank, adamantine citadel armor, and immovable spearhead.",
        primary_color_tint="#32363b",
        variant_color_tints={
            "battleship-tungsten": "#464a4f",
            "ordnance-carrier": "#3f473c",
        },
    ),
    "tactical-operations:arc-lance-walker": UnitPbrProfile(
        unit_id="tactical-operations:arc-lance-walker",
        pack_id="tactical-operations",
        model_name="ArcLanceWalker",
        canonical_source_art="content/source-art/arclancewalker-rigged.blend",
        primary_preset="arc_discharge_conduit",
        tactical_variant_presets={
            "plasma-barrier": "plasma_arc_containment",
        },
        role_doctrine="Bipedal high-voltage arc lance walker, armor piercing dielectric discharge unit.",
        primary_color_tint="#232d3a",
        variant_color_tints={
            "plasma-barrier": "#1c222b",
        },
    ),
    "tactical-operations:field-medic-rover": UnitPbrProfile(
        unit_id="tactical-operations:field-medic-rover",
        pack_id="tactical-operations",
        model_name="FieldMedicRover",
        canonical_source_art="content/source-art/fieldmedicrover-rigged.blend",
        primary_preset="combat_medic_bioceramic",
        tactical_variant_presets={
            "nanite-chitin": "nanite_repair_chitin",
        },
        role_doctrine="Autonomous tactical triage ambulance, field repair, and nanite restoration.",
        primary_color_tint="#d4ded8",
        variant_color_tints={
            "nanite-chitin": "#385247",
        },
    ),
    "tactical-operations:sentinel-relay": UnitPbrProfile(
        unit_id="tactical-operations:sentinel-relay",
        pack_id="tactical-operations",
        model_name="SentinelRelay",
        canonical_source_art="content/source-art/sentinel-relay.blend",
        primary_preset="orbital_phased_array_comm",
        tactical_variant_presets={
            "void-stealth": "stealth_void_composite",
            "beryllium-mirror": "beryllium_mirror_hull",
        },
        role_doctrine="Deep-space phased array surveillance, optical radar mast, and battlefield intelligence.",
        primary_color_tint="#343840",
        variant_color_tints={
            "void-stealth": "#1b1d21",
            "beryllium-mirror": "#848d94",
        },
    ),
    "tactical-operations:gravitas-breaker": UnitPbrProfile(
        unit_id="tactical-operations:gravitas-breaker",
        pack_id="tactical-operations",
        model_name="GravitasBreaker",
        canonical_source_art="content/source-art/gravitas-breaker.blend",
        primary_preset="gravitas_singularity_chassis",
        tactical_variant_presets={
            "quantum-hyperdrive": "quantum_hyperdrive_alloy",
            "cryogenic-fuel": "cryogenic_fuel_shroud",
        },
        role_doctrine="Graviton distortion cannon, singularity shockwave generator, and vortex projector.",
        primary_color_tint="#2c2f37",
        variant_color_tints={
            "quantum-hyperdrive": "#1c3848",
            "cryogenic-fuel": "#2b4b5e",
        },
    ),
    "tactical-operations:command-relay": UnitPbrProfile(
        unit_id="tactical-operations:command-relay",
        pack_id="tactical-operations",
        model_name="CommandRelay",
        canonical_source_art="content/source-art/command-relay.blend",
        primary_preset="neosteel_command_citadel",
        tactical_variant_presets={
            "beryllium-shield": "beryllium_mirror_hull",
            "cyber-nanoweave": "cyber_nanoweave",
        },
        role_doctrine="Strategic fleet command node, planetary communications spire, and tactical director.",
        primary_color_tint="#3b4047",
        variant_color_tints={
            "beryllium-shield": "#828b92",
            "cyber-nanoweave": "#1b2129",
        },
    ),
}


def get_all_mapped_presets() -> Sequence[str]:
    """Return all distinct preset keys referenced across all unit profiles."""
    used = set()
    for profile in UNIT_PBR_REGISTRY.values():
        used.add(profile.primary_preset)
        for var_preset in profile.tactical_variant_presets.values():
            used.add(var_preset)
    return sorted(used)


def verify_all_presets_mapped() -> Tuple[bool, Sequence[str], Sequence[str]]:
    """Verify that every preset in Prepare-MaterialMakerTacticalTemplate.py is mapped to at least one unit.

    Returns:
        (is_valid, unmapped_presets, mapped_presets)
    """
    all_available = set(_load_available_presets().keys())
    mapped = set(get_all_mapped_presets())
    unmapped = sorted(all_available - mapped)
    return len(unmapped) == 0, unmapped, sorted(mapped)


def get_units_for_preset(preset_name: str) -> List[Tuple[str, str]]:
    """Find all (unit_id, role_type) associations for a given preset."""
    matches = []
    for uid, profile in UNIT_PBR_REGISTRY.items():
        if profile.primary_preset == preset_name:
            matches.append((uid, "primary"))
        for var_name, p_name in profile.tactical_variant_presets.items():
            if p_name == preset_name:
                matches.append((uid, f"variant:{var_name}"))
    return matches


def get_unit_profile(unit_id: str) -> Optional[UnitPbrProfile]:
    """Retrieve the PBR profile for a unit by namespaced ID."""
    return UNIT_PBR_REGISTRY.get(unit_id)


def get_pack_units(pack_id: str) -> List[UnitPbrProfile]:
    """Retrieve all unit PBR profiles belonging to a given content pack."""
    return [p for p in UNIT_PBR_REGISTRY.values() if p.pack_id == pack_id]


if __name__ == "__main__":
    valid, unmapped, mapped = verify_all_presets_mapped()
    total_available = len(_load_available_presets())
    print(f"Authoritative Unit PBR Registry Audit:")
    print(f"  Total units mapped: {len(UNIT_PBR_REGISTRY)}")
    print(f"  Total presets available: {total_available}")
    print(f"  Total presets mapped to units: {len(mapped)}")
    if unmapped:
        print(f"  WARNING: {len(unmapped)} unmapped presets: {unmapped}")
    else:
        print(f"  SUCCESS: 100% of all {total_available} presets are mapped and integrated across mod units!")
