"""generate_unit_model.py: CLI and scriptable vehicle generator for Rogue Command.

Usage with Blender 4.5.14 LTS:
    .\\.tools\\Blender-4.5.14\\...\\blender.exe --background --python tools\\generate_unit_model.py -- [options]

Archetypes:
    tank, walker, bike, artillery, arc_tank, hover, spider, vtol, halftrack, super_heavy, buggy,
    flak_anti_air, siege_crawler, drone_carrier, quad_mech, combat_harvester,
    colossus_titan, hover_cruiser, heavy_dropship,
    shield_truck, demolition_breacher, artillery_strider, thermobaric_tank, ew_jammer,
    mobile_sam_launcher, recovery_crawler, mine_layer, sniper_tankette, hexapod_assault, assault_hovercraft,
    bridge_layer, command_hq_rig, flying_wing_bomber, mortar_carrier, siege_drill_mole, heavy_crane_mech,
    nanite_support_rover, gravity_siege_breaker, tactical_relay_carrier, amphibious_assault_tank, plasma_mortar_strider, stealth_recon_skimmer

Weapons:
    cannon, gatling, missile, arc, howitzer, dual_cannon, railgun, quad_flak, plasma_mortar,
    laser_beam, vls_missiles, flamethrower, emp_emitter, nanite_projector, gravity_cannon

Presets:
    siege_breaker, recon_interceptor, gunship_alpha, apc_enforcer, dune_raider,
    sky_sweeper, dreadnought_crawler, hive_carrier, titan_quad, resource_behemoth,
    colossus_titan, sea_lance, sky_dreadnought, orbital_defense,
    beam_destroyer, vls_missile_cruiser,
    aegis_bastion, volatile_runner, longbow_strider, inferno_purger, spectre_jammer,
    bastion_shield_dome, tesla_obelisk,
    skyguard_sam, salvage_hauler, minefield_sower, longshot_tankette, arachnid_hexapod,
    tidebreaker_hovercraft, urban_brawler, arctic_sentinel, night_stalker, armored_hauler,
    early_warning_array, fusion_silo,
    scissor_bridgelayer, overlord_command_hq, wraith_stealth_wing, thunder_mortar_carrier,
    subterranean_mole, atlas_crane_mech, fortified_pillbox, patriot_sam_battery, deep_space_relay,
    crimson_praetorian, cobalt_widowmaker, golden_aegis_command,
    sentinel_relay, gravitas_breaker, command_relay, coastal_dreadnought, plasma_strider, phantom_skimmer,
    emerald_warden, solar_colossus, void_spectre
    (presets may also set default camo, wear, attachments, custom hex colors, and VFX; explicit CLI options win)

Options:
    --name <UnitName>          Vehicle class name (e.g., ColossusSuperHeavy)
    --archetype <type>         Vehicle archetype (default: tank)
    --faction <faction>        Color palette: 'vanguard', 'siege', 'syndicate', 'arctic'
    --camo <pattern>           Camouflage variant: 'none', 'desert', 'urban', 'arctic', 'woodland', 'digital', 'splinter', 'tigerstripe'
    --weapon <type>            Primary weapon subsystem; see --help for exact accepted values
    --preset <preset>          Quick archetype and weapon preset
    --rollcage                 Attach tubular roll cage structure
    --quad-tracks              Attach 4 independent heavy track pods
    --tilt-rotors              Attach dual aerodynamic ducted tilt-rotors
    --dozer                    Attach frontal heavy dozer ram blade
    --skirts                   Attach armored side skirts over track run (default: True)
    --suspension-arms          Attach trailing swing-arm suspension linkage with torsion bosses
    --mantlet-boot             Attach tiered concentric accordion dust boot on cannon mantlet
    --spaced-skirts            Attach segmented multi-panel spaced armor skirts with standoff brackets
    --hydraulic-hoses          Attach catenary hydraulic hoses and braided power conduits
    --hull-greebles            Attach maintenance hatches, louver vents, and lifting lugs
    --tactical-markings        Project tactical chevrons and zenith identification plates
    --hydraulic-pistons        Attach dual-stage telescopic hydraulic actuator rams and clevis pins
    --ammo-chutes              Attach articulated metallic ammunition feed chutes and hoppers
    --aps-suite                Attach Active Protection System (APS) hard-kill pods, AESA radars, and LWR
    --stowage-kit              Attach external turret bustle rack, camo tarp, Jerry cans, and spare tracks
    --slat-armor               Attach anti-HEAT standoff slat/cage bar armor grids
    --era-bricks               Attach staggered Explosive Reactive Armor (ERA) wedge bricks
    --camo-netting             Attach 3D noise-displaced draped camouflage netting and barrel wrap
    --thermal-radiators        Attach armored heat-exchanger radiator banks with glowing emissive fins
    --rws-mount                Attach roof-mounted independent Remote Weapon Station (RWS/CROWS) with EO/IR sensor
    --drone-dock               Attach recessed landing cradle with charging rails, LED markers, and parasitic drone
    --recoil-spade             Attach rear-mounted heavy hydraulic recoil spades / earth anchors (Z ≈ 0.02)
    --barrel-shroud            Attach ventilated thermal barrel jacket with BMesh cooling slots and muzzle brake
    --nanite-dispenser         Attach bilateral armored field nanite repair dispenser pods with inspection sight glasses
    --grav-dampener            Attach gyroscopic gravitational anchor and inertial dampener hub
    --comms-mast               Attach telescopic communications & EW jamming mast with planar blade antenna fins
    --ram-prow                 Attach heavy angled V-shaped kinetic battering ram prow / cowcatcher bumper
    --weighted-normals         Apply weighted normals (default); --no-weighted-normals disables them
    --wreckage-severity <sev>  Damage severity for wreckage: light, medium, catastrophic (default: catastrophic)
    --smoke                    Attach multi-tube smoke discharger banks
    --radar                    Attach command/recon radar dish
    --sponsons                 Attach armored external side sponsons
    --dirt                     Bake procedural vertex ambient dirt/cavity shading (default: True)
    --locators                 Generate Unity locator sockets (default: True)
    --lod                      Print low-poly triangle budget diagnostics and LOD recommendations
    --multiview                Render 4-perspective orthographic sheet (ISO, TOP, FRONT, SIDE)
    --out-dir <path>           Base output directory (default: repo root)
"""

from __future__ import annotations
import argparse
import json
import math
import sys
from pathlib import Path

# Add script directory and repo tools to sys.path so RogueModelKit is found
SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent if (SCRIPT_DIR.parent / "content").exists() else SCRIPT_DIR.parents[3]
sys.path.insert(0, str(SCRIPT_DIR))
sys.path.insert(0, str(REPO_ROOT / "tools"))

import bpy
from mathutils import Vector
import RogueModelKit as rmk


PRESETS = {
    "siege_breaker": {
        "archetype": "super_heavy",
        "weapon": "dual_cannon",
        "faction": "siege",
        "sponsons": True,
        "smoke": True,
        "radar": True,
        "suspension_arms": True,
        "spaced_skirts": True,
        "mantlet_boot": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "stowage_kit": True,
        "slat_armor": True,
        "era_bricks": True,
        "recoil_spade": True,
        "rws_mount": True,
    },
    "recon_interceptor": {
        "archetype": "buggy",
        "weapon": "gatling",
        "faction": "vanguard",
        "rollcage": True,
        "radar": True,
        "tactical_markings": True,
        "ammo_chutes": True,
        "stowage_kit": True,
    },
    "gunship_alpha": {
        "archetype": "vtol",
        "weapon": "missile",
        "faction": "syndicate",
        "tactical_markings": True,
        "aps_suite": True,
    },
    "apc_enforcer": {
        "archetype": "halftrack",
        "weapon": "gatling",
        "faction": "vanguard",
        "smoke": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "ammo_chutes": True,
        "stowage_kit": True,
        "slat_armor": True,
        "rws_mount": True,
    },
    "dune_raider": {
        "archetype": "bike",
        "weapon": "gatling",
        "faction": "siege",
        "ammo_chutes": True,
    },
    "sky_sweeper": {
        "archetype": "flak_anti_air",
        "weapon": "quad_flak",
        "faction": "vanguard",
        "radar": True,
        "outriggers": True,
        "suspension_arms": True,
        "mantlet_boot": True,
        "hull_greebles": True,
        "ammo_chutes": True,
        "hydraulic_pistons": True,
        "barrel_shroud": True,
    },
    "dreadnought_crawler": {
        "archetype": "siege_crawler",
        "weapon": "railgun",
        "faction": "siege",
        "outriggers": True,
        "sponsons": True,
        "suspension_arms": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "hydraulic_pistons": True,
        "aps_suite": True,
        "era_bricks": True,
        "thermal_radiators": True,
        "recoil_spade": True,
    },
    "hive_carrier": {
        "archetype": "drone_carrier",
        "weapon": "missile",
        "faction": "syndicate",
        "radar": True,
        "flight_deck": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "drone_dock": True,
    },
    "titan_quad": {
        "archetype": "quad_mech",
        "weapon": "dual_cannon",
        "faction": "arctic",
        "smoke": True,
        "ik_rig": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "hydraulic_pistons": True,
        "aps_suite": True,
    },
    "resource_behemoth": {
        "archetype": "combat_harvester",
        "weapon": "gatling",
        "faction": "siege",
        "dozer": True,
        "sponsons": True,
        "excavator_drum": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "hydraulic_pistons": True,
        "ammo_chutes": True,
        "stowage_kit": True,
        "slat_armor": True,
    },
    "colossus_titan": {
        "archetype": "colossus_titan",
        "weapon": "dual_cannon",
        "faction": "vanguard",
        "smoke": True,
        "radar": True,
        "antenna": True,
        "ik_rig": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "hydraulic_pistons": True,
        "aps_suite": True,
    },
    "sea_lance": {
        "archetype": "hover_cruiser",
        "weapon": "railgun",
        "faction": "syndicate",
        "sponsons": True,
        "radar": True,
        "antenna": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "thermal_radiators": True,
    },
    "sky_dreadnought": {
        "archetype": "heavy_dropship",
        "weapon": "missile",
        "faction": "siege",
        "smoke": True,
        "antenna": True,
        "tactical_markings": True,
        "aps_suite": True,
    },
    "orbital_defense": {
        "structure": True,
        "building_type": "orbital_silo",
        "faction": "siege",
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "hydraulic_pistons": True,
    },
    "beam_destroyer": {
        "archetype": "super_heavy",
        "weapon": "laser_beam",
        "faction": "syndicate",
        "radar": True,
        "smoke": True,
        "sponsons": True,
        "armor_plating": True,
        "track_sag": 0.05,
        "suspension_arms": True,
        "spaced_skirts": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "thermal_radiators": True,
        "barrel_shroud": True,
    },
    "vls_missile_cruiser": {
        "archetype": "hover_cruiser",
        "weapon": "vls_missiles",
        "faction": "vanguard",
        "radar": True,
        "antenna": True,
        "sponsons": True,
        "armor_plating": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
    },
    "aegis_bastion": {
        "archetype": "shield_truck",
        "weapon": "arc",
        "faction": "vanguard",
        "radar": True,
        "smoke": True,
        "shield_bubble": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "stowage_kit": True,
        "thermal_radiators": True,
    },
    "volatile_runner": {
        "archetype": "demolition_breacher",
        "weapon": "plasma_mortar",
        "faction": "siege",
        "dozer": True,
        "rollcage": True,
        "hydraulic_hoses": True,
        "tactical_markings": True,
        "hydraulic_pistons": True,
        "slat_armor": True,
        "recoil_spade": True,
    },
    "longbow_strider": {
        "archetype": "artillery_strider",
        "weapon": "railgun",
        "faction": "syndicate",
        "outriggers": True,
        "antenna": True,
        "ik_rig": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "hydraulic_pistons": True,
        "camo_netting": True,
        "thermal_radiators": True,
        "recoil_spade": True,
    },
    "inferno_purger": {
        "archetype": "thermobaric_tank",
        "weapon": "flamethrower",
        "faction": "siege",
        "spaced_skirts": True,
        "smoke": True,
        "sponsons": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "stowage_kit": True,
        "era_bricks": True,
        "thermal_radiators": True,
    },
    "spectre_jammer": {
        "archetype": "ew_jammer",
        "weapon": "emp_emitter",
        "faction": "arctic",
        "radar": True,
        "antenna": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "stowage_kit": True,
        "camo_netting": True,
        "drone_dock": True,
    },
    "bastion_shield_dome": {
        "structure": True,
        "building_type": "shield_dome",
        "faction": "vanguard",
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "thermal_radiators": True,
    },
    "tesla_obelisk": {
        "structure": True,
        "building_type": "emp_obelisk",
        "faction": "syndicate",
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "hydraulic_pistons": True,
        "thermal_radiators": True,
    },
    # --- Wave 3: one preset per new archetype ---
    "skyguard_sam": {
        "archetype": "mobile_sam_launcher",
        "weapon": "missile",
        "faction": "vanguard",
        "camo": "digital",
        "wear": "factory_new",
        "hydraulic_pistons": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
    },
    "salvage_hauler": {
        "archetype": "recovery_crawler",
        "weapon": "gatling",
        "faction": "siege",
        "wear": "battle_hardened",
        "dozer": True,
        "hydraulic_hoses": True,
        "hydraulic_pistons": True,
        "hull_greebles": True,
        "stowage_kit": True,
        "slat_armor": True,
        "rws_mount": True,
        "attachments": "fuel_drums",
    },
    "minefield_sower": {
        "archetype": "mine_layer",
        "weapon": "gatling",
        "faction": "siege",
        "camo": "desert",
        "spaced_skirts": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "ammo_chutes": True,
    },
    "longshot_tankette": {
        "archetype": "sniper_tankette",
        "weapon": "cannon",
        "faction": "arctic",
        "camo": "splinter",
        "mantlet_boot": True,
        "track_shoes": True,
        "hull_greebles": True,
        "stowage_kit": True,
        "camo_netting": True,
        "barrel_shroud": True,
    },
    "arachnid_hexapod": {
        "archetype": "hexapod_assault",
        "weapon": "missile",
        "faction": "syndicate",
        "ik_rig": True,
        "hydraulic_hoses": True,
        "hydraulic_pistons": True,
        "hull_greebles": True,
        "aps_suite": True,
    },
    "tidebreaker_hovercraft": {
        "archetype": "assault_hovercraft",
        "weapon": "gatling",
        "faction": "vanguard",
        "camo": "urban",
        "smoke": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "ammo_chutes": True,
        "stowage_kit": True,
    },
    # --- Wave 3: remixes of existing archetypes with Wave 2 suites ---
    "urban_brawler": {
        "archetype": "tank",
        "weapon": "cannon",
        "faction": "vanguard",
        "camo": "urban",
        "wear": "battle_hardened",
        "spaced_skirts": True,
        "cupola": True,
        "hull_greebles": True,
        "aps_suite": True,
        "stowage_kit": True,
        "slat_armor": True,
        "era_bricks": True,
        "rws_mount": True,
        "attachments": "era",
    },
    "arctic_sentinel": {
        "archetype": "walker",
        "weapon": "gatling",
        "faction": "arctic",
        "camo": "arctic",
        "antenna": True,
        "hydraulic_hoses": True,
        "ammo_chutes": True,
        "stowage_kit": True,
        "camo_netting": True,
    },
    "night_stalker": {
        "archetype": "bike",
        "weapon": "gatling",
        "faction": "syndicate",
        "camo": "tigerstripe",
        "wear": "factory_new",
        "ammo_chutes": True,
        "tactical_markings": True,
        "barrel_shroud": True,
    },
    "armored_hauler": {
        "archetype": "halftrack",
        "weapon": "gatling",
        "faction": "siege",
        "camo": "woodland",
        "smoke": True,
        "cupola": True,
        "hull_greebles": True,
        "ammo_chutes": True,
        "stowage_kit": True,
        "slat_armor": True,
        "attachments": "fuel_drums",
    },
    # --- Wave 3: strategic structures ---
    "early_warning_array": {
        "structure": True,
        "building_type": "radar_station",
        "faction": "arctic",
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "camo_netting": True,
        "drone_dock": True,
    },
    "fusion_silo": {
        "structure": True,
        "building_type": "energy_silo",
        "faction": "vanguard",
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "hydraulic_pistons": True,
        "thermal_radiators": True,
    },
    # --- Wave 4: 6 new mobile archetypes ---
    "scissor_bridgelayer": {
        "archetype": "bridge_layer",
        "weapon": "gatling",
        "faction": "siege",
        "camo": "woodland",
        "hydraulic_hoses": True,
        "hydraulic_pistons": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "stowage_kit": True,
    },
    "overlord_command_hq": {
        "archetype": "command_hq_rig",
        "weapon": "gatling",
        "faction": "vanguard",
        "camo": "digital",
        "wear": "factory_new",
        "smoke": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "stowage_kit": True,
        "camo_netting": True,
        "drone_dock": True,
        "rws_mount": True,
    },
    "wraith_stealth_wing": {
        "archetype": "flying_wing_bomber",
        "weapon": "missile",
        "faction": "syndicate",
        "camo": "splinter",
        "wear": "factory_new",
        "tactical_markings": True,
        "aps_suite": True,
        "vfx_thruster": True,
    },
    "thunder_mortar_carrier": {
        "archetype": "mortar_carrier",
        "weapon": "plasma_mortar",
        "faction": "siege",
        "camo": "desert",
        "smoke": True,
        "spaced_skirts": True,
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "ammo_chutes": True,
        "stowage_kit": True,
        "slat_armor": True,
        "camo_netting": True,
        "recoil_spade": True,
    },
    "subterranean_mole": {
        "archetype": "siege_drill_mole",
        "weapon": "flamethrower",
        "faction": "siege",
        "wear": "battle_hardened",
        "hydraulic_hoses": True,
        "hydraulic_pistons": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "stowage_kit": True,
        "era_bricks": True,
        "thermal_radiators": True,
    },
    "atlas_crane_mech": {
        "archetype": "heavy_crane_mech",
        "weapon": "dual_cannon",
        "faction": "arctic",
        "model_scale": 0.75,
        "ik_rig": True,
        "hydraulic_hoses": True,
        "hydraulic_pistons": True,
    },
    # --- Wave 4: 3 new strategic structures ---
    "fortified_pillbox": {
        "structure": True,
        "building_type": "bunker_pillbox",
        "faction": "siege",
        "hull_greebles": True,
        "tactical_markings": True,
        "ammo_chutes": True,
        "stowage_kit": True,
        "slat_armor": True,
        "era_bricks": True,
        "camo_netting": True,
        "rws_mount": True,
        "recoil_spade": True,
    },
    "patriot_sam_battery": {
        "structure": True,
        "building_type": "anti_air_battery",
        "faction": "vanguard",
        "hydraulic_hoses": True,
        "hydraulic_pistons": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "barrel_shroud": True,
    },
    "deep_space_relay": {
        "structure": True,
        "building_type": "comms_relay_tower",
        "faction": "syndicate",
        "hydraulic_hoses": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "thermal_radiators": True,
        "drone_dock": True,
    },
    # --- Wave 4: 3 elite remixes with custom hex palette & VFX overrides ---
    "crimson_praetorian": {
        "archetype": "colossus_titan",
        "weapon": "railgun",
        "faction": "siege",
        "primary_color": "#5a1414",
        "light_armor_color": "#8f2424",
        "emissive_color": "#ffaa00",
        "wear": "battle_hardened",
        "ik_rig": True,
        "smoke": True,
        "radar": True,
        "hydraulic_hoses": True,
        "hydraulic_pistons": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "era_bricks": True,
        "thermal_radiators": True,
        "vfx_flash": True,
    },
    "cobalt_widowmaker": {
        "archetype": "hexapod_assault",
        "weapon": "laser_beam",
        "faction": "syndicate",
        "primary_color": "#142238",
        "light_armor_color": "#29466e",
        "emissive_color": "#00f0ff",
        "wear": "factory_new",
        "ik_rig": True,
        "hydraulic_hoses": True,
        "hydraulic_pistons": True,
        "hull_greebles": True,
        "aps_suite": True,
        "thermal_radiators": True,
        "vfx_flash": True,
    },
    "golden_aegis_command": {
        "archetype": "hover_cruiser",
        "weapon": "vls_missiles",
        "faction": "vanguard",
        "primary_color": "#2b3642",
        "light_armor_color": "#c29b38",
        "emissive_color": "#44ffaa",
        "wear": "pristine",
        "radar": True,
        "sponsons": True,
        "shield_bubble": True,
        "hull_greebles": True,
        "tactical_markings": True,
        "aps_suite": True,
        "era_bricks": True,
        "vfx_thruster": True,
    },
    # --- Wave 7: 6 tactical archetypes & support hardware ---
    "sentinel_relay": {
        "archetype": "nanite_support_rover",
        "weapon": "nanite_projector",
        "faction": "vanguard",
        "wear": "factory_new",
        "nanite_dispenser": True,
        "hydraulic_hoses": True,
        "aps_suite": True,
        "tactical_markings": True,
    },
    "gravitas_breaker": {
        "archetype": "gravity_siege_breaker",
        "weapon": "gravity_cannon",
        "faction": "siege",
        "wear": "battle_hardened",
        "ram_prow": True,
        "grav_dampener": True,
        "era_bricks": True,
        "recoil_spade": True,
        "thermal_radiators": True,
    },
    "command_relay": {
        "archetype": "tactical_relay_carrier",
        "weapon": "missile",
        "faction": "vanguard",
        "wear": "factory_new",
        "comms_mast": True,
        "drone_dock": True,
        "rws_mount": True,
        "stowage_kit": True,
    },
    "coastal_dreadnought": {
        "archetype": "amphibious_assault_tank",
        "weapon": "dual_cannon",
        "faction": "arctic",
        "camo": "arctic",
        "spaced_skirts": True,
        "thermal_radiators": True,
        "smoke": True,
        "tactical_markings": True,
    },
    "plasma_strider": {
        "archetype": "plasma_mortar_strider",
        "weapon": "plasma_mortar",
        "faction": "siege",
        "wear": "battle_hardened",
        "ik_rig": True,
        "recoil_spade": True,
        "hydraulic_hoses": True,
        "hydraulic_pistons": True,
    },
    "phantom_skimmer": {
        "archetype": "stealth_recon_skimmer",
        "weapon": "gatling",
        "faction": "syndicate",
        "camo": "digital",
        "aps_suite": True,
        "camo_netting": True,
        "thermal_radiators": True,
        "vfx_thruster": True,
    },
    # --- Wave 7: 3 elite custom-palette & VFX remixes ---
    "emerald_warden": {
        "archetype": "nanite_support_rover",
        "weapon": "nanite_projector",
        "faction": "vanguard",
        "primary_color": "#163b27",
        "light_armor_color": "#2a5c40",
        "emissive_color": "#00ff88",
        "wear": "factory_new",
        "nanite_dispenser": True,
        "aps_suite": True,
        "vfx_flash": True,
    },
    "solar_colossus": {
        "archetype": "gravity_siege_breaker",
        "weapon": "gravity_cannon",
        "faction": "siege",
        "primary_color": "#4d2800",
        "light_armor_color": "#784208",
        "emissive_color": "#ff9900",
        "wear": "battle_hardened",
        "ram_prow": True,
        "grav_dampener": True,
        "thermal_radiators": True,
        "era_bricks": True,
        "vfx_flash": True,
    },
    "void_spectre": {
        "archetype": "stealth_recon_skimmer",
        "weapon": "laser_beam",
        "faction": "syndicate",
        "primary_color": "#151522",
        "light_armor_color": "#2b2b40",
        "emissive_color": "#9d00ff",
        "wear": "pristine",
        "aps_suite": True,
        "thermal_radiators": True,
        "vfx_thruster": True,
    },

}

WAVE3_PRESETS = (
    "skyguard_sam", "salvage_hauler", "minefield_sower", "longshot_tankette",
    "arachnid_hexapod", "tidebreaker_hovercraft",
    "urban_brawler", "arctic_sentinel", "night_stalker", "armored_hauler",
    "early_warning_array", "fusion_silo",
)

WAVE4_PRESETS = (
    "scissor_bridgelayer", "overlord_command_hq", "wraith_stealth_wing",
    "thunder_mortar_carrier", "subterranean_mole", "atlas_crane_mech",
    "fortified_pillbox", "patriot_sam_battery", "deep_space_relay",
    "crimson_praetorian", "cobalt_widowmaker", "golden_aegis_command",
)

WAVE7_PRESETS = (
    "sentinel_relay", "gravitas_breaker", "command_relay",
    "coastal_dreadnought", "plasma_strider", "phantom_skimmer",
    "emerald_warden", "solar_colossus", "void_spectre",
)


def _validate_in_range(name: str, value: float, min_val: float, max_val: float) -> None:
    """Validate that a CLI float argument is finite and falls within [min_val, max_val]."""
    if not math.isfinite(value) or not (min_val <= value <= max_val):
        raise SystemExit(f"--{name} must be finite and between {min_val} and {max_val}.")


def _calculate_ortho_scale(archetype: str) -> float:
    """Calculates viewport framing extent based on archetype mass and visual profile."""
    if archetype in ("colossus_titan", "hover_cruiser", "heavy_dropship", "flying_wing_bomber"):
        return 8.5
    if archetype in (
        "super_heavy", "siege_crawler", "drone_carrier", "artillery_strider",
        "mobile_sam_launcher", "recovery_crawler", "hexapod_assault",
        "assault_hovercraft", "bridge_layer", "command_hq_rig", "siege_drill_mole",
        "heavy_crane_mech", "nanite_support_rover", "gravity_siege_breaker",
        "tactical_relay_carrier", "amphibious_assault_tank", "plasma_mortar_strider",
        "stealth_recon_skimmer",
    ):
        return 7.2
    if archetype in (
        "artillery", "spider", "vtol", "flak_anti_air", "quad_mech",
        "combat_harvester", "shield_truck", "thermobaric_tank", "ew_jammer",
        "mine_layer", "mortar_carrier",
    ):
        return 6.2
    return 5.6


def parse_args():
    # Blender passes all args after '--' to Python script
    argv = sys.argv
    if "--" in argv:
        script_args = argv[argv.index("--") + 1 :]
    else:
        script_args = []

    parser = argparse.ArgumentParser(description="Generate Rogue Command 3D vehicle models.")
    parser.add_argument("--name", default="CustomRogueVehicle", help="Unit model name")
    parser.add_argument(
        "--archetype",
        default="tank",
        choices=[
            "tank", "walker", "bike", "artillery", "arc_tank", "hover", "spider",
            "vtol", "halftrack", "super_heavy", "buggy",
            "flak_anti_air", "siege_crawler", "drone_carrier", "quad_mech", "combat_harvester",
            "colossus_titan", "hover_cruiser", "heavy_dropship",
            "shield_truck", "demolition_breacher", "artillery_strider", "thermobaric_tank", "ew_jammer",
            "mobile_sam_launcher", "recovery_crawler", "mine_layer", "sniper_tankette", "hexapod_assault", "assault_hovercraft",
            "bridge_layer", "command_hq_rig", "flying_wing_bomber", "mortar_carrier", "siege_drill_mole", "heavy_crane_mech", "nanite_support_rover", "gravity_siege_breaker", "tactical_relay_carrier", "amphibious_assault_tank", "plasma_mortar_strider", "stealth_recon_skimmer"
        ],
        help="Vehicle archetype",
    )
    parser.add_argument(
        "--faction",
        default="vanguard",
        choices=["vanguard", "siege", "syndicate", "arctic"],
        help="Visual 5-tone faction palette",
    )
    parser.add_argument(
        "--camo",
        default="none",
        choices=["none", "desert", "urban", "arctic", "woodland", "digital", "splinter", "tigerstripe"],
        help="Camouflage variant",
    )
    parser.add_argument(
        "--weapon",
        default="cannon",
        choices=[
            "cannon", "gatling", "missile", "arc", "howitzer", "dual_cannon",
            "railgun", "quad_flak", "plasma_mortar", "laser_beam", "vls_missiles",
            "flamethrower", "emp_emitter", "nanite_projector", "gravity_cannon"
        ],
        help="Primary weapon subsystem",
    )
    parser.add_argument(
        "--preset",
        default="none",
        choices=["none", *PRESETS.keys()],
        help="Preconfigured vehicle or strategic-structure loadout",
    )
    parser.add_argument("--rollcage", action="store_true", help="Include tubular roll cage")
    parser.add_argument("--quad-tracks", action="store_true", help="Include 4 independent track pods")
    parser.add_argument("--track-sag", type=float, default=None, help="Parametric catenary track sag depth between road wheels (default: auto for heavy tracks)")
    parser.add_argument("--armor-plating", action="store_true", help="Apply procedural BMesh armor plating with recessed panel gutters")
    parser.add_argument("--ik-rig", action="store_true", help="Configure automated Inverse Kinematics (IK) constraints on walker leg armatures")
    parser.add_argument("--tilt-rotors", action="store_true", help="Include ducted tilt-rotors")
    parser.add_argument("--dozer", action="store_true", help="Include frontal dozer ram blade")
    parser.add_argument("--skirts", action="store_true", default=True, help="Include armored side skirts")
    parser.add_argument("--suspension-arms", action="store_true", help="Attach trailing swing-arm suspension linkage with torsion bosses and coilover dampers")
    parser.add_argument("--mantlet-boot", action="store_true", help="Attach tiered concentric accordion dust boot between gun mantlet and barrel")
    parser.add_argument("--spaced-skirts", action="store_true", help="Attach segmented multi-panel spaced armor skirts with standoff brackets and mudflaps")
    parser.add_argument("--hydraulic-hoses", action="store_true", help="Attach catenary hydraulic hoses and braided power conduits with bulkhead couplers")
    parser.add_argument("--hull-greebles", action="store_true", help="Attach BMesh-inset maintenance hatches, micro-louver vents, and corner lifting lugs")
    parser.add_argument("--tactical-markings", action="store_true", help="Project BVHTree-raycasted floating tactical chevron decals and zenith ID plates")
    parser.add_argument("--hydraulic-pistons", action="store_true", help="Attach dual-stage telescopic hydraulic actuator rams and clevis pin eye-mounts")
    parser.add_argument("--ammo-chutes", action="store_true", help="Attach articulated metallic ammunition feed chutes and ammo hoppers")
    parser.add_argument("--aps-suite", action="store_true", help="Attach Active Protection System (APS) hard-kill pods, 4-quadrant AESA radars, and LWR mast")
    parser.add_argument("--stowage-kit", action="store_true", help="Attach external turret bustle cargo rack, rolled camo tarp, Jerry cans, and spare tracks")
    parser.add_argument("--slat-armor", action="store_true", help="Attach anti-HEAT standoff slat/cage bar armor grids")
    parser.add_argument("--era-bricks", action="store_true", help="Attach staggered Explosive Reactive Armor (ERA) wedge bricks")
    parser.add_argument("--camo-netting", action="store_true", help="Attach 3D noise-displaced draped camouflage netting and barrel wrap")
    parser.add_argument("--thermal-radiators", action="store_true", help="Attach armored heat-exchanger radiator banks with glowing emissive fins")
    parser.add_argument("--rws-mount", action="store_true", help="Attach roof-mounted independent Remote Weapon Station (RWS/CROWS) with stabilized EO/IR optical sensor ball and rapid-fire autocannon")
    parser.add_argument("--drone-dock", action="store_true", help="Attach recessed roof/deck landing cradle with magnetic charging guide rails, alignment runway glow LEDs, and docked parasitic reconnaissance drone")
    parser.add_argument("--recoil-spade", action="store_true", help="Attach rear-mounted heavy hydraulic recoil spades / earth anchors that bite into the terrain plane (Z ≈ 0.02)")
    parser.add_argument("--barrel-shroud", action="store_true", help="Attach ventilated thermal barrel jacket with consolidated BMesh cooling perforation slats, reinforced clamping collars, and multi-baffle muzzle brake")
    parser.add_argument("--nanite-dispenser", action="store_true", help="Attach bilateral armored field nanite repair dispenser pods with inspection sight glasses and spray heads")
    parser.add_argument("--grav-dampener", action="store_true", help="Attach gyroscopic gravitational anchor and inertial dampener hub with counter-rotating magnetic rings and singularity core")
    parser.add_argument("--comms-mast", action="store_true", help="Attach telescopic communications & EW jamming mast with planar blade antenna fins, cross-dipole horns, and satcom dome")
    parser.add_argument("--ram-prow", action="store_true", help="Attach heavy angled V-shaped kinetic battering ram prow / cowcatcher bumper with towing shackles and hazard chevrons")
    parser.add_argument("--weighted-normals", action=argparse.BooleanOptionalAction, default=True, help="Apply face-area weighted custom split normals to eliminate low-poly chamfer shading pinch (default: enabled)")
    parser.add_argument("--smoke", action="store_true", help="Include multi-tube smoke discharger banks")
    parser.add_argument("--radar", action="store_true", help="Include command/recon radar dish")
    parser.add_argument("--sponsons", action="store_true", help="Include armored external side sponsons")
    parser.add_argument("--outriggers", action="store_true", help="Include deployable hydraulic stabilizer outriggers")
    parser.add_argument("--flight-deck", action="store_true", help="Include flight / drone launch deck")
    parser.add_argument("--excavator-drum", action="store_true", help="Include front rotary cutting excavator drum")
    parser.add_argument("--antenna", action="store_true", help="Attach tactical whip antenna with Socket_Antenna")
    parser.add_argument("--cupola", action="store_true", help="Attach commander periscope cupola and split hatch")
    parser.add_argument("--rivets", action="store_true", help="Attach perimeter rivet rows and beveled weld seams")
    parser.add_argument("--track-shoes", action="store_true", help="Attach discrete articulated track shoe links along the ground run")
    parser.add_argument("--biome", default="none", choices=["none", "desert", "arctic", "woodland", "urban"], help="Authoring-time palette alias when --camo is none; does not adapt to runtime map biomes")
    parser.add_argument("--bake-atlas", action="store_true", help="Bake unified PBR texture maps (Albedo, MetallicGloss, Normal, Emission)")
    parser.add_argument("--bake-normals", action="store_true", help="Enable the full PBR atlas bake, which includes the tangent-space normal map")
    parser.add_argument("--camo-scale", type=float, default=1.0, help="Procedural camouflage pattern scale (0.1–10.0; default 1.0)")
    parser.add_argument("--camo-contrast", type=float, default=1.0, help="Procedural camouflage color contrast (0.0–4.0; default 1.0)")
    parser.add_argument("--normal-intensity", type=float, default=1.0, help="Procedural panel-relief bump strength (0.0–4.0; default 1.0)")
    parser.add_argument("--panel-lines", action=argparse.BooleanOptionalAction, default=True, help="Enable or disable procedural panel seam relief (default: enabled)")
    parser.add_argument("--atlas-resolution", type=int, default=1024, choices=[512, 1024, 2048], help="Resolution for baked texture atlas")
    parser.add_argument("--single-material", action="store_true", help="Export an atlas FBX with one material slot; this does not guarantee one Unity draw call")
    parser.add_argument("--rig", action="store_true", help="Generate kinematic Armature bones and bind meshes with rigid vertex groups")
    parser.add_argument("--animate", action="store_true", help="Bake procedural action animation clips (Idle, Drive, FireRecoil, Death) into FBX")
    parser.add_argument("--vfx-flash", action="store_true", help="Attach star-spike muzzle flash mesh to weapon")
    parser.add_argument("--vfx-thruster", action="store_true", help="Attach glowing conical exhaust plumes to engines")
    parser.add_argument("--shield-bubble", action="store_true", help="Attach geodesic energy shield bubble envelope")
    parser.add_argument("--primary-color", default=None, help="Custom hex color for primary armor (e.g. #1f3a52)")
    parser.add_argument("--light-armor-color", default=None, help="Custom hex color for light armor (e.g. #3b5f78)")
    parser.add_argument("--emissive-color", default=None, help="Custom hex color for emissive glow (e.g. #00ffee)")
    parser.add_argument("--locators", action="store_true", default=True, help="Generate Unity locator sockets")
    parser.add_argument("--dirt", action="store_true", default=True, help="Bake procedural vertex ambient dirt/cavity shading")
    parser.add_argument("--wear", default="field_tested", choices=["none", "pristine", "factory_new", "field_tested", "battle_hardened"], help="Procedural surface wear level")
    parser.add_argument("--structural-wear-mask", action="store_true", help="Keep convex-edge wear localized in vertex-color channel G, with flat and concave corners unmarked")
    parser.add_argument("--soot-intensity", type=float, default=1.0, help="Thermal soot intensity around muzzles and exhausts (0.0–2.0; default 1.0)")
    parser.add_argument("--lods", action="store_true", help="Generate multi-level LOD chain (LOD0, LOD1, LOD2) and export _LODs.fbx")
    parser.add_argument("--colliders", action="store_true", help="Generate low-poly physics collision hulls")
    parser.add_argument("--gameplay-json", action="store_true", help="Export companion .gameplay.json specification")
    parser.add_argument("--wreckage", action="store_true", help="Export burned / dislodged turret wreckage variant")
    parser.add_argument("--wreckage-severity", default="catastrophic", choices=["light", "medium", "catastrophic"], help="Wreckage damage severity level (default: catastrophic)")
    parser.add_argument("--damaged-state", action="store_true", help="Export combat-damaged <Name>_Damaged.fbx variant with distorted plates and scorch marks")
    parser.add_argument("--structure", action="store_true", help="Generate a static building or factory instead of a mobile vehicle")
    parser.add_argument("--building-type", default="factory", choices=["factory", "defense_turret", "radar_station", "energy_silo", "orbital_silo", "shield_dome", "emp_obelisk", "bunker_pillbox", "anti_air_battery", "comms_relay_tower"], help="Structure building type")
    parser.add_argument("--attachments", default=None, help="Comma-separated list of modular attachments to apply (e.g. 'era,aps,sponson,fuel_drums')")
    parser.add_argument("--export-detached-attachments", action="store_true", help="Export separate <Name>_Attachment_<Type>.fbx meshes for modular attachment in Unity")
    parser.add_argument("--render-icons", action="store_true", help="Render dual orthographic 2D tactical icons (Top-Down and Isometric) with transparent alpha")
    parser.add_argument("--icon-size", type=int, default=128, choices=[64, 128], help="Resolution for rendered tactical icons")
    parser.add_argument("--icon-elevation", type=float, default=45.0, help="Elevation angle for tactical isometric icon in degrees (default 45.0 for RTS 45-degree angle, or 35.26 for true isometric)")
    parser.add_argument("--target-budget", type=int, default=rmk.DEFAULT_FACE_BUDGET, help="Target face budget for profiling (default: 7500)")
    parser.add_argument("--rts-budget", action="store_true", help="Use lower-detail preset variants for RTS camera polygon budgets")
    parser.add_argument("--model-scale", type=float, default=1.0, help="Uniformly scale authored vehicle geometry and sockets around the ground origin")
    parser.add_argument("--lod", action="store_true", help="Calculate polygon budget and recommend LOD reductions")
    parser.add_argument("--smooth-angle", type=float, default=38.0, help="Smart angle-weighted auto-smooth threshold in degrees")
    parser.add_argument("--multiview", action="store_true", help="Render 4 orthographic views")
    parser.add_argument("--batch-all-archetypes", action="store_true", help="Batch generate all 42 archetypes into the output directory")
    parser.add_argument("--report-json", default=None, help="Path to write detailed structural and budget JSON report")
    parser.add_argument("--dry-run", action="store_true", help="Validate CLI parameters and print archetype plan without building Blender geometry")
    parser.add_argument("--out-dir", default=None, help="Base root directory for outputs")
    parser.add_argument("--blend-path", default=None, help="Explicit .blend output file path")
    parser.add_argument("--fbx-path", default=None, help="Explicit .fbx output file path")
    parser.add_argument("--preview-path", default=None, help="Explicit preview render output file path")
    parser.add_argument("--card-path", default=None, help="Explicit 128x128 tactical card Sprite output file path")

    args = parser.parse_args(script_args)

    def option_was_supplied(option: str) -> bool:
        for argument in script_args:
            supplied_option = argument.split("=", 1)[0]
            if supplied_option == option:
                return True
            if supplied_option.startswith("--") and option.startswith(supplied_option):
                return True
        return False

    # A building type selects the structure generator. Structure presets also
    # select that path while still allowing an explicit --building-type override.
    if option_was_supplied("--building-type"):
        args.structure = True

    # Apply preset overrides for unspecified parameters
    if args.preset and args.preset != "none" and args.preset in PRESETS:
        p = PRESETS[args.preset]
        if "--archetype" not in script_args:
            args.archetype = p.get("archetype", args.archetype)
        if "--weapon" not in script_args:
            args.weapon = p.get("weapon", args.weapon)
        if "--faction" not in script_args:
            args.faction = p.get("faction", args.faction)
        if p.get("structure") and not option_was_supplied("--structure"):
            args.structure = True
        if "building_type" in p and not option_was_supplied("--building-type"):
            args.building_type = p["building_type"]
        if "model_scale" in p and not option_was_supplied("--model-scale"):
            args.model_scale = p["model_scale"]
        for flag in (
            "sponsons", "smoke", "radar", "rollcage", "dozer", "outriggers",
            "flight_deck", "excavator_drum", "antenna", "cupola", "rivets",
            "track_shoes", "armor_plating", "ik_rig", "suspension_arms",
            "mantlet_boot", "spaced_skirts", "shield_bubble",
            "hydraulic_hoses", "hull_greebles", "tactical_markings",
            "hydraulic_pistons", "ammo_chutes", "aps_suite", "stowage_kit",
            "slat_armor", "era_bricks", "camo_netting", "thermal_radiators",
            "rws_mount", "drone_dock", "recoil_spade", "barrel_shroud",
            "vfx_flash", "vfx_thruster",
            "nanite_dispenser", "grav_dampener", "comms_mast", "ram_prow",
        ):
            if p.get(flag) and f"--{flag.replace('_', '-')}" not in script_args:
                setattr(args, flag, True)
        # Presets may also declare default camo, wear, modular attachments, and custom hex colors;
        # explicit CLI options always win.
        if "camo" in p and not option_was_supplied("--camo"):
            args.camo = p["camo"]
        if "wear" in p and not option_was_supplied("--wear"):
            args.wear = p["wear"]
        if "attachments" in p and not option_was_supplied("--attachments"):
            args.attachments = p["attachments"]
        if "primary_color" in p and not option_was_supplied("--primary-color"):
            args.primary_color = p["primary_color"]
        if "light_armor_color" in p and not option_was_supplied("--light-armor-color"):
            args.light_armor_color = p["light_armor_color"]
        if "emissive_color" in p and not option_was_supplied("--emissive-color"):
            args.emissive_color = p["emissive_color"]
        if "track_sag" in p and not option_was_supplied("--track-sag"):
            args.track_sag = p["track_sag"]
        if "wreckage_severity" in p and not option_was_supplied("--wreckage-severity"):
            args.wreckage_severity = p["wreckage_severity"]

    return args


def resolve_path(p: str | Path | None, base: Path) -> Path | None:
    if p is None:
        return None
    path_obj = Path(p)
    return path_obj if path_obj.is_absolute() else (base / path_obj).resolve()


ALL_ARCHETYPES = [
    "tank", "walker", "bike", "artillery", "arc_tank", "hover", "spider",
    "vtol", "halftrack", "super_heavy", "buggy",
    "flak_anti_air", "siege_crawler", "drone_carrier", "quad_mech", "combat_harvester",
    "colossus_titan", "hover_cruiser", "heavy_dropship",
    "shield_truck", "demolition_breacher", "artillery_strider", "thermobaric_tank", "ew_jammer",
    "mobile_sam_launcher", "recovery_crawler", "mine_layer", "sniper_tankette", "hexapod_assault", "assault_hovercraft",
    "bridge_layer", "command_hq_rig", "flying_wing_bomber", "mortar_carrier", "siege_drill_mole", "heavy_crane_mech", "nanite_support_rover", "gravity_siege_breaker", "tactical_relay_carrier", "amphibious_assault_tank", "plasma_mortar_strider", "stealth_recon_skimmer"
]

ARCHETYPE_DEFAULT_WEAPONS = {
    "tank": "cannon",
    "walker": "gatling",
    "bike": "gatling",
    "artillery": "howitzer",
    "arc_tank": "arc",
    "hover": "cannon",
    "spider": "missile",
    "vtol": "missile",
    "halftrack": "gatling",
    "super_heavy": "dual_cannon",
    "buggy": "gatling",
    "flak_anti_air": "quad_flak",
    "siege_crawler": "railgun",
    "drone_carrier": "missile",
    "quad_mech": "dual_cannon",
    "combat_harvester": "gatling",
    "colossus_titan": "dual_cannon",
    "hover_cruiser": "railgun",
    "heavy_dropship": "missile",
    "shield_truck": "arc",
    "demolition_breacher": "plasma_mortar",
    "artillery_strider": "railgun",
    "thermobaric_tank": "flamethrower",
    "ew_jammer": "emp_emitter",
    "mobile_sam_launcher": "missile",
    "recovery_crawler": "gatling",
    "mine_layer": "gatling",
    "sniper_tankette": "cannon",
    "hexapod_assault": "missile",
    "assault_hovercraft": "gatling",
    "bridge_layer": "gatling",
    "command_hq_rig": "gatling",
    "flying_wing_bomber": "missile",
    "mortar_carrier": "plasma_mortar",
    "siege_drill_mole": "flamethrower",
    "heavy_crane_mech": "dual_cannon",
    "nanite_support_rover": "nanite_projector",
    "gravity_siege_breaker": "gravity_cannon",
    "tactical_relay_carrier": "missile",
    "amphibious_assault_tank": "dual_cannon",
    "plasma_mortar_strider": "plasma_mortar",
    "stealth_recon_skimmer": "gatling",
}


def build_vehicle_model(
    name: str,
    archetype: str,
    faction: str,
    camo: str,
    weapon: str,
    wear: str,
    out_root: Path,
    soot_intensity: float = 1.0,
    blend_path: Path | None = None,
    fbx_path: Path | None = None,
    preview_path: Path | None = None,
    skirts: bool = True,
    rollcage: bool = False,
    quad_tracks: bool = False,
    tilt_rotors: bool = False,
    dozer: bool = False,
    smoke: bool = False,
    radar: bool = False,
    sponsons: bool = False,
    outriggers: bool = False,
    flight_deck: bool = False,
    excavator_drum: bool = False,
    antenna: bool = False,
    cupola: bool = False,
    rivets: bool = False,
    track_shoes: bool = False,
    biome: str | None = None,
    primary_color: str | None = None,
    light_armor_color: str | None = None,
    emissive_color: str | None = None,
    locators: bool = True,
    dirt: bool = True,
    lods: bool = False,
    colliders: bool = False,
    gameplay_json: bool = False,
    wreckage: bool = False,
    target_budget: int = rmk.DEFAULT_FACE_BUDGET,
    lod_diagnostics: bool = False,
    smooth_angle: float = 38.0,
    multiview: bool = False,
    bake_atlas: bool = False,
    atlas_resolution: int = 1024,
    single_material: bool = False,
    rig: bool = False,
    animate: bool = False,
    vfx_flash: bool = False,
    vfx_thruster: bool = False,
    shield_bubble: bool = False,
    attachments: str | None = None,
    export_detached_attachments: bool = False,
    damaged_state: bool = False,
    render_icons: bool = False,
    icon_size: int = 128,
    icon_elevation: float = 45.0,
    camo_scale: float = 1.0,
    camo_contrast: float = 1.0,
    normal_intensity: float = 1.0,
    panel_lines: bool = True,
    bake_normals: bool = False,
    track_sag: float | None = None,
    armor_plating: bool = False,
    ik_rig: bool = False,
    suspension_arms: bool = False,
    mantlet_boot: bool = False,
    spaced_skirts: bool = False,
    hydraulic_hoses: bool = False,
    hull_greebles: bool = False,
    tactical_markings: bool = False,
    hydraulic_pistons: bool = False,
    ammo_chutes: bool = False,
    aps_suite: bool = False,
    stowage_kit: bool = False,
    slat_armor: bool = False,
    era_bricks: bool = False,
    camo_netting: bool = False,
    thermal_radiators: bool = False,
    rws_mount: bool = False,
    drone_dock: bool = False,
    recoil_spade: bool = False,
    barrel_shroud: bool = False,
    nanite_dispenser: bool = False,
    grav_dampener: bool = False,
    comms_mast: bool = False,
    ram_prow: bool = False,
    weighted_normals: bool = True,
    wreckage_severity: str = "catastrophic",
    model_scale: float = 1.0,
    card_path: Path | None = None,
    structural_wear_mask: bool = False,
    rts_budget: bool = False,
) -> dict:
    rmk.clear_scene()

    if rts_budget and archetype == "mobile_sam_launcher":
        hydraulic_pistons = False
        hull_greebles = False
        tactical_markings = False
    elif rts_budget and archetype == "recovery_crawler":
        hydraulic_hoses = False
        hydraulic_pistons = False
        hull_greebles = False
        stowage_kit = False
        slat_armor = False
        rws_mount = False
        attachments = None

    blend_file = blend_path or (out_root / "content" / "source-art" / f"{name.lower()}.blend")
    fbx_file = fbx_path or (
        out_root
        / "unity"
        / "RogueCommandAssetAuthoring"
        / "Assets"
        / "RogueCommandExpansionSample"
        / "Models"
        / f"{name}.fbx"
    )
    preview_file = preview_path or (out_root / "artifacts" / "previews" / f"{name.lower()}.png")
    p_override = rmk.hex_to_rgb(primary_color) if primary_color else None
    l_override = rmk.hex_to_rgb(light_armor_color) if light_armor_color else None
    e_override = rmk.hex_to_rgb(emissive_color) if emissive_color else None

    if biome and biome != "none" and camo == "none":
        camo = biome

    builder = rmk.RogueVehicleBuilder(
        name,
        faction=faction,
        camo=camo,
        primary_override=p_override,
        light_override=l_override,
        emissive_override=e_override,
        camo_scale=camo_scale,
        camo_contrast=camo_contrast,
        enable_normal_relief=panel_lines,
        normal_intensity=normal_intensity,
    )
    builder.set_archetype(archetype)
    builder.set_wear_level(wear)
    builder.suspension_arms = suspension_arms
    builder.mantlet_boot = mantlet_boot
    builder.spaced_skirts = spaced_skirts
    builder.wreckage_severity = wreckage_severity

    if track_sag is not None:
        builder.track_sag = track_sag
    elif archetype in ("super_heavy", "siege_crawler") or name.lower() in ("beam_destroyer", "siege_breaker", "dreadnought_crawler"):
        builder.track_sag = 0.045
    else:
        builder.track_sag = 0.0

    builder.with_ik = ik_rig

    gameplay_meta_path = (fbx_file.parent / f"{name}.gameplay.json") if gameplay_json else None
    wreckage_fbx_path = (fbx_file.parent / f"{name}_Wreckage.fbx") if wreckage else None
    damaged_fbx_path = (fbx_file.parent / f"{name}_Damaged.fbx") if damaged_state else None

    # 1. Mobility and Chassis by Archetype
    if archetype == "walker":
        builder.add_bipedal_legs(hip_height=1.45, hip_spacing_x=0.88)
        builder.add_cast_pelvis(location=(0.0, -0.02, 1.40), width=0.92, depth=0.65, height=0.42)
        builder.add_mech_torso(location=(0.0, 0.02, 1.96), width=1.14, length=0.74, height=0.78)
        builder.add_lifting_lugs()
        builder.add_sensor_visor()

    elif archetype == "bike":
        builder.add_all_terrain_wheels(wheelbase_y=1.95, track_width_x=0.0, wheel_radius=0.38, wheel_width=0.22)
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_SpineFrame", (0.34, 1.85, 0.38), (0.0, -0.05, 0.72), builder.palette.primary_armor, 0.04),
            rmk.make_box(f"{name}Armor_EngineBlock", (0.46, 0.68, 0.42), (0.0, -0.18, 0.82), builder.palette.gunmetal, 0.035),
            rmk.make_box(f"{name}Armor_CockpitCowl", (0.42, 0.58, 0.32), (0.0, 0.45, 1.05), builder.palette.light_armor, 0.03,
                          (math.radians(-16), 0.0, 0.0)),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CowlCollar", (0.0, 0.45, 0.89), (0.42, 0.58), height=0.035, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_rod(f"{name}Joint_Fork_L", Vector((-0.18, 0.45, 1.05)), Vector((-0.16, 0.98, 0.38)), 0.035, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_Fork_R", Vector((0.18, 0.45, 1.05)), Vector((0.16, 0.98, 0.38)), 0.035, builder.palette.gunmetal),
            rmk.make_box(f"{name}Joint_Handlebars", (0.64, 0.06, 0.05), (0.0, 0.38, 1.18), builder.palette.gunmetal, 0.01),
            rmk.make_rod(f"{name}Joint_Swingarm_L", Vector((-0.18, -0.18, 0.65)), Vector((-0.16, -0.98, 0.38)), 0.038, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_Swingarm_R", Vector((0.18, -0.18, 0.65)), Vector((0.16, -0.98, 0.38)), 0.038, builder.palette.gunmetal),
        ])
        builder.add_sensor_visor()
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            builder.objects.append(rmk.make_box(
                f"{name}Armor_SidePod_{side_tag}",
                (0.18, 0.58, 0.28), (side * 0.36, -0.12, 0.72), builder.palette.primary_armor, 0.02
            ))

    elif archetype == "artillery":
        builder.add_sloped_hull(length=3.4, width=2.6, height=0.96)
        builder.add_deck_edge_weld_seams()
        builder.add_continuous_tracks(length=3.2, height=0.68, belt_width=0.36, roadwheels=6, with_skirts=skirts)
        builder.add_final_drive_housings()
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_interlocking_gussets()
        builder.add_splash_rib()
        builder.add_driver_periscope_hood()
        builder.add_access_hatches()
        builder.add_weld_seams([
            ((-1.20, 0.48, 0.72), (-1.05, 1.65, 0.38)),
            ((1.20, 0.48, 0.72), (1.05, 1.65, 0.38)),
        ])
        builder.add_fuel_drums()
        builder.add_turret(size=(1.5, 1.7, 0.48), center_y=0.12)
        builder.add_turret_ring_guard()
        builder.add_weapon_trunnions(spacing_x=0.60, bracket_width=0.14)
        builder.add_crew_cupola()
        builder.add_commander_sight()
        builder.add_bustle_basket()
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            builder.objects.extend([
                rmk.make_box(f"{name}Armor_RecoilSpade_{side_tag}", (0.20, 0.24, 0.26),
                             (side * 0.62, -1.62, 0.56), builder.palette.primary_armor, 0.025),
                rmk.make_box(f"{name}Armor_RecoilBlade_{side_tag}", (0.26, 0.08, 0.32),
                             (side * 0.62, -1.78, 0.36), builder.palette.gunmetal, 0.02,
                             rotation=(math.radians(22), 0.0, 0.0)),
            ])
        builder.add_spring_antenna()

    elif archetype == "arc_tank":
        builder.add_sloped_hull(length=2.8, width=2.3, height=0.86)
        builder.add_deck_edge_weld_seams()
        builder.add_continuous_tracks(length=2.7, height=0.58, belt_width=0.32, roadwheels=5, with_skirts=skirts)
        builder.add_final_drive_housings()
        builder.add_engine_deck(slat_count=3, with_exhausts=False)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_splash_rib()
        builder.add_driver_periscope_hood()
        builder.add_glacis_weld_seams()
        builder.add_fuel_drums()
        builder.add_turret(size=(1.3, 1.4, 0.42), center_y=0.0)
        builder.add_turret_ring_guard()
        builder.add_crew_cupola()
        builder.add_bustle_basket()
        builder.add_arc_emitter(core_radius=0.22, prongs=3)
        builder.add_sensor_visor()

    elif archetype == "hover":
        builder.add_sloped_hull(length=2.9, width=2.5, height=0.78)
        builder.add_deck_edge_weld_seams()
        builder.add_hover_skimmer(float_height=0.48)
        builder.add_turret(size=(1.35, 1.5, 0.42), center_y=0.06)
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "spider":
        builder.add_sloped_hull(length=2.6, width=2.4, height=0.82)
        builder.add_deck_edge_weld_seams()
        builder.objects.append(rmk.make_transitional_coaming_collar(
            f"{name}Armor_WaistCollar", (0.0, 0.0, 0.78), (1.50, 1.60), height=0.045, collar_mat=builder.palette.gunmetal
        ))
        builder.add_quad_crawler_legs(knee_height=0.98, reach_x=1.12, reach_y=0.88)
        builder.add_turret(size=(1.45, 1.55, 0.44), center_y=0.04)
        builder.add_sensor_visor()

    elif archetype == "vtol":
        builder.is_hover = True
        builder.objects.extend(rmk.make_aerodynamic_fuselage_assembly(
            f"{name}Armor", length=2.60, width=1.35, height=0.58, location=(0.0, 0.10, 0.72),
            primary_mat=builder.palette.primary_armor, canopy_mat=builder.palette.light_armor,
            intake_mat=builder.palette.gunmetal, exhaust_mat=builder.palette.warning,
            with_wing_fairings=True
        ))
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_TailBoom", (0.34, 1.65, 0.26), (0.0, -1.85, 0.82), builder.palette.primary_armor, 0.03),
            rmk.make_box(f"{name}Armor_HorizStab", (1.15, 0.38, 0.06), (0.0, -2.50, 0.88), builder.palette.light_armor, 0.015),
            rmk.make_box(f"{name}Armor_VertFin_L", (0.05, 0.42, 0.48), (-0.42, -2.55, 1.05), builder.palette.primary_armor, 0.015,
                         rotation=(0.0, math.radians(-18), 0.0)),
            rmk.make_box(f"{name}Armor_VertFin_R", (0.05, 0.42, 0.48), (0.42, -2.55, 1.05), builder.palette.primary_armor, 0.015,
                         rotation=(0.0, math.radians(18), 0.0)),
            rmk.make_box(f"{name}Armor_WingSponson_L", (0.85, 0.52, 0.14), (-1.05, 0.15, 0.72), builder.palette.light_armor, 0.025),
            rmk.make_box(f"{name}Armor_WingSponson_R", (0.85, 0.52, 0.14), (1.05, 0.15, 0.72), builder.palette.light_armor, 0.025),
            rmk.make_box(f"{name}Joint_LandingSkid_L", (0.08, 1.85, 0.05), (-0.55, 0.05, 0.06), builder.palette.gunmetal, 0.01),
            rmk.make_box(f"{name}Joint_LandingSkid_R", (0.08, 1.85, 0.05), (0.55, 0.05, 0.06), builder.palette.gunmetal, 0.01),
            rmk.make_rod(f"{name}Joint_SkidStrut_FL", (-0.55, 0.75, 0.08), (-0.52, 0.65, 0.45), 0.032, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_SkidStrut_FR", (0.55, 0.75, 0.08), (0.52, 0.65, 0.45), 0.032, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_SkidStrut_RL", (-0.55, -0.65, 0.08), (-0.52, -0.55, 0.45), 0.032, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_SkidStrut_RR", (0.55, -0.65, 0.08), (0.52, -0.55, 0.45), 0.032, builder.palette.gunmetal),
        ])
        builder.add_sensor_visor()
        builder.add_tiltrotors(location_offset=(1.45, 0.15, 0.75), radius=0.44, tilt_deg=14.0)
        builder.turret_center = Vector((0.0, 1.05, 0.45))

    elif archetype == "halftrack":
        wheel_z = builder.ground_clearance + 0.38 + 0.035
        front_wheel_locs = []
        for side in (-1, 1):
            tag = "L" if side < 0 else "R"
            w_loc = (side * 0.88, 1.25, wheel_z)
            front_wheel_locs.append(w_loc)
            builder.objects.extend(rmk.make_all_terrain_wheel_assembly(
                f"{name}Wheel_Front_{tag}",
                location=w_loc,
                radius=0.38, width=0.26,
                tire_mat=builder.palette.tread, rim_mat=builder.palette.wheel_rim,
                hub_mat=builder.palette.gunmetal, accent_mat=builder.palette.warning
            ))
        builder.objects.append(rmk.make_box(
            f"{name}Joint_FrontAxle", (1.65, 0.14, 0.12), (0.0, 1.25, wheel_z), builder.palette.gunmetal, 0.02
        ))
        builder.add_wheel_fenders(front_wheel_locs, radius=0.38, width=0.26)
        for side in (-1, 1):
            builder.objects.extend(rmk.make_continuous_track_assembly(
                f"{name}Treads",
                x_pos=side * 0.92,
                length=1.95, height=0.58, belt_width=0.30,
                roadwheel_count=4,
                tread_mat=builder.palette.tread,
                rim_mat=builder.palette.wheel_rim,
                hub_mat=builder.palette.wheel_hub,
                axle_mat=builder.palette.gunmetal,
                fender_mat=builder.palette.light_armor,
                skirt_mat=builder.palette.primary_armor if skirts else None,
                accent_mat=builder.palette.warning,
                ground_z=builder.ground_clearance,
                center_y=-0.55
            ))
        builder.objects.extend([
            rmk.make_sloped_glacis_hull(
                f"{name}Armor_Hood",
                length_front=0.85, length_rear=0.65,
                width_front=1.20, width_rear=1.35,
                height_lower=builder.ground_clearance + 0.35, height_upper=0.92,
                material=builder.palette.primary_armor, bevel=0.035
            ),
            rmk.make_box(f"{name}Armor_Cab", (1.38, 0.88, 0.62), (0.0, 0.20, 1.05), builder.palette.primary_armor, 0.035),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CabCollar", (0.0, 0.20, 0.74), (1.38, 0.88), height=0.045, collar_mat=builder.palette.gunmetal
            ),
        ])
        builder.objects.extend(
            rmk.make_recessed_sensor_visor(
                f"{name}Sensor_CabVisor",
                location=(0.0, 0.62, 1.18),
                size=(0.58, 0.12, 0.15),
                armor_mat=builder.palette.light_armor, lens_mat=builder.palette.emissive, cavity_mat=builder.palette.gunmetal
            )
        )
        builder.add_troop_compartment(size=(1.42, 1.65, 0.74), location=(0.0, -0.95, 0.88))
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_recovery_winch()
        builder.add_turret(size=(1.05, 1.15, 0.35), center_y=0.20)
        builder.add_spring_antenna()

    elif archetype == "super_heavy":
        builder.add_sloped_hull(length=3.8, width=2.8, height=1.05, bevel=0.055)
        builder.add_deck_edge_weld_seams()
        builder.objects.extend([
            rmk.make_box(
                f"{name}Armor_Superstructure",
                (2.1, 2.7, 0.34), (0.0, -0.10, 1.25), builder.palette.primary_armor, 0.04
            ),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_SuperstructureCollar", (0.0, -0.10, 1.08), (2.1, 2.7), height=0.045, collar_mat=builder.palette.gunmetal
            ),
        ])
        builder.add_quad_track_pods(pod_length=1.65, pod_height=0.62, track_spacing_x=2.55, pod_spacing_y=2.0)
        builder.add_final_drive_housings(radius=0.22, depth=0.14)
        builder.add_engine_deck(slat_count=5, with_exhausts=True)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_interlocking_gussets()
        builder.add_splash_rib()
        builder.add_driver_periscope_hood()
        builder.add_access_hatches()
        builder.add_weld_seams([
            ((-1.32, 0.55, 0.78), (-1.15, 1.85, 0.42)),
            ((1.32, 0.55, 0.78), (1.15, 1.85, 0.42)),
        ])
        builder.add_fuel_drums()
        builder.add_era_bricks()
        builder.add_turret(size=(1.85, 2.1, 0.58), center_y=0.15)
        builder.add_turret_ring_guard()
        builder.add_crew_cupola()
        builder.add_commander_sight()
        builder.add_bustle_basket()
        builder.add_sensor_visor()
        builder.add_radar_dish()
        builder.add_armor_applique((-0.95, 1.95, 0.95), (0.95, 1.95, 0.95), count=6)
        builder.add_spring_antenna()

    elif archetype == "buggy":
        builder.add_all_terrain_wheels(
            wheelbase_y=2.2, track_width_x=1.85, wheel_radius=0.42, wheel_width=0.30, with_fenders=True
        )
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_SpineChassis", (0.85, 2.4, 0.28), (0.0, -0.05, 0.58), builder.palette.primary_armor, 0.035),
            rmk.make_box(f"{name}Armor_FrontNose", (0.72, 0.85, 0.26), (0.0, 1.05, 0.65), builder.palette.light_armor, 0.03,
                         rotation=(math.radians(-14), 0.0, 0.0)),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_FrontNoseCollar", (0.0, 1.05, 0.54), (0.72, 0.85), height=0.035, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_box(f"{name}Armor_EngineBlock", (0.78, 0.88, 0.46), (0.0, -0.88, 0.78), builder.palette.gunmetal, 0.03),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_EngineCollar", (0.0, -0.88, 0.56), (0.78, 0.88), height=0.035, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_rod(f"{name}Joint_ShockAbsorber_L", (-0.42, 0.85, 0.58), (-0.85, 1.10, 0.42), 0.035, builder.palette.warning),
            rmk.make_rod(f"{name}Joint_ShockAbsorber_R", (0.42, 0.85, 0.58), (0.85, 1.10, 0.42), 0.035, builder.palette.warning),
            rmk.make_cylinder(f"{name}Armor_Exhaust_L", 0.055, 0.42, (-0.28, -1.25, 0.95), builder.palette.gunmetal,
                              rotation=(math.radians(-24), 0.0, 0.0), vertices=10, bevel=0.008),
            rmk.make_cylinder(f"{name}Armor_Exhaust_R", 0.055, 0.42, (0.28, -1.25, 0.95), builder.palette.gunmetal,
                              rotation=(math.radians(-24), 0.0, 0.0), vertices=10, bevel=0.008),
        ])
        builder.add_rollcage(size=(1.15, 1.5, 0.82), location=(0.0, 0.02, 1.02))
        builder.add_sensor_visor()
        builder.turret_center = Vector((0.0, 0.05, 1.45))

    elif archetype == "flak_anti_air":
        builder.add_sloped_hull(length=3.2, width=2.4, height=0.90)
        builder.add_deck_edge_weld_seams()
        builder.add_continuous_tracks(length=3.0, height=0.64, belt_width=0.34, roadwheels=5, with_skirts=skirts)
        builder.add_final_drive_housings()
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_splash_rib()
        builder.add_driver_periscope_hood()
        builder.add_glacis_weld_seams()
        builder.add_turret(size=(1.5, 1.65, 0.52), center_y=0.04)
        builder.add_turret_ring_guard()
        builder.add_crew_cupola()
        builder.add_commander_sight()
        builder.add_bustle_basket()
        builder.add_sensor_visor()
        builder.add_radar_dish(is_rotodome=False)
        builder.add_outriggers(count=2, reach_x=0.55)
        builder.add_spring_antenna()

    elif archetype == "siege_crawler":
        builder.add_sloped_hull(length=4.2, width=3.0, height=1.15, bevel=0.055)
        builder.add_deck_edge_weld_seams()
        builder.objects.extend([
            rmk.make_box(
                f"{name}Armor_Citadel",
                (2.3, 2.9, 0.42), (0.0, -0.05, 1.35), builder.palette.primary_armor, 0.045
            ),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CitadelCollar", (0.0, -0.05, 1.14), (2.3, 2.9), height=0.045, collar_mat=builder.palette.gunmetal
            ),
        ])
        builder.add_quad_track_pods(pod_length=1.85, pod_height=0.68, track_spacing_x=2.75, pod_spacing_y=2.3)
        builder.add_engine_deck(slat_count=6, with_exhausts=True)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_interlocking_gussets()
        builder.add_turret(size=(2.0, 2.3, 0.62), center_y=0.18)
        builder.add_sensor_visor()
        builder.add_outriggers(count=4, reach_x=0.85)
        builder.add_side_sponsons()
        builder.add_spring_antenna()

    elif archetype == "drone_carrier":
        builder.add_sloped_hull(length=4.4, width=3.2, height=0.92, bevel=0.045)
        builder.add_deck_edge_weld_seams()
        builder.add_continuous_tracks(length=4.1, height=0.68, belt_width=0.40, roadwheels=7, with_skirts=skirts)
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_splash_rib()
        builder.add_glacis_weld_seams()
        builder.add_commander_sight()
        builder.add_flight_deck(size=(2.10, 3.20, 0.22))
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_BridgeTower", (0.55, 1.25, 0.68), (1.18, -0.15, 1.48), builder.palette.primary_armor, 0.035),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_TowerCollar", (1.18, -0.15, 1.14), (0.55, 1.25), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_box(f"{name}Armor_BridgeRoof", (0.62, 1.32, 0.12), (1.18, -0.15, 1.84), builder.palette.light_armor, 0.02),
        ])
        builder.objects.extend(rmk.make_recessed_sensor_visor(
            f"{name}Sensor_BridgeVisor",
            location=(1.18, 0.48, 1.62),
            size=(0.42, 0.10, 0.14),
            armor_mat=builder.palette.light_armor, lens_mat=builder.palette.emissive, cavity_mat=builder.palette.gunmetal
        ))
        builder.add_radar_dish(is_rotodome=True, location=(1.18, -0.42, 2.05))
        builder.turret_center = Vector((-0.88, 1.15, 1.15))

    elif archetype == "quad_mech":
        builder.add_sloped_hull(length=2.8, width=2.5, height=0.88, bevel=0.05)
        builder.add_deck_edge_weld_seams()
        builder.add_cast_pelvis(location=(0.0, 0.0, 1.05), width=1.40, depth=1.80, height=0.48)
        builder.objects.append(rmk.make_transitional_coaming_collar(
            f"{name}Armor_WaistCollar", (0.0, 0.0, 0.81), (1.40, 1.80), height=0.045, collar_mat=builder.palette.gunmetal
        ))
        builder.add_quad_crawler_legs(knee_height=1.25, reach_x=1.35, reach_y=1.10)
        builder.add_lifting_lugs()
        builder.add_commander_sight()
        builder.add_era_bricks()
        builder.add_turret(size=(1.55, 1.7, 0.48), center_y=0.10)
        builder.add_crew_cupola()
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "combat_harvester":
        builder.add_sloped_hull(length=3.6, width=2.7, height=1.05, bevel=0.045)
        builder.add_deck_edge_weld_seams()
        builder.add_access_hatches()
        builder.add_continuous_tracks(length=3.3, height=0.68, belt_width=0.38, roadwheels=6, with_skirts=skirts)
        builder.add_final_drive_housings(radius=0.22)
        builder.add_engine_deck(slat_count=5, with_exhausts=True)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_interlocking_gussets()
        builder.add_access_hatches()
        builder.add_recovery_winch()
        builder.add_commander_sight()
        builder.add_exhaust_deflector()
        builder.add_exhaust_heat_shield()
        builder.add_excavator_drum(width=2.5, radius=0.34)
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            builder.objects.extend([
                rmk.make_box(
                    f"{name}Armor_OreHopper_{side_tag}",
                    (0.42, 1.65, 0.65), (side * 1.32, -0.42, 1.05), builder.palette.primary_armor, 0.035
                ),
                rmk.make_transitional_coaming_collar(
                    f"{name}Armor_HopperCollar_{side_tag}", (side * 1.32, -0.42, 0.72), (0.42, 1.65), height=0.045, collar_mat=builder.palette.gunmetal
                ),
            ])
        builder.add_turret(size=(1.15, 1.25, 0.38), center_y=0.25)
        builder.add_turret_ring_guard()
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "colossus_titan":
        builder.add_bipedal_legs(
            hip_height=2.30,
            hip_spacing_x=1.35,
            total_height=2.35,
        )
        builder.add_cast_pelvis(location=(0.0, -0.05, 2.25), width=1.50, depth=0.95, height=0.55)
        builder.add_mech_torso(location=(0.0, 0.02, 3.05), width=2.05, length=1.35, height=1.20)
        builder.add_lifting_lugs()
        builder.add_interlocking_gussets(locations=[(-0.85, -0.05, 2.85), (0.85, -0.05, 2.85)])
        builder.add_commander_sight()
        builder.add_era_bricks()
        builder.add_turret(size=(1.60, 1.75, 0.55), center_y=0.10)
        builder.turret_center = Vector((0.0, 0.10, 3.75))
        builder.add_sensor_visor()
        builder.add_whip_antenna()
        builder.add_radar_dish(is_rotodome=False)

    elif archetype == "hover_cruiser":
        builder.is_hover = True
        builder.add_sloped_hull(length=4.6, width=2.8, height=0.85, bevel=0.06)
        builder.add_deck_edge_weld_seams()
        builder.add_hover_skimmer(float_height=0.45)
        builder.objects.extend([
            rmk.make_sloped_glacis_hull(
                f"{name}Armor_BowProw",
                length_front=1.25, length_rear=0.75,
                width_front=1.10, width_rear=2.65,
                height_lower=builder.ground_clearance + 0.35, height_upper=0.92,
                material=builder.palette.primary_armor, bevel=0.04
            ),
            rmk.make_box(f"{name}Armor_ConningTower", (0.95, 1.45, 0.68), (0.0, -0.65, 1.28), builder.palette.light_armor, 0.035),
            rmk.make_box(f"{name}Armor_BridgeRoof", (1.05, 1.55, 0.12), (0.0, -0.65, 1.68), builder.palette.gunmetal, 0.02),
            rmk.make_cylinder(f"{name}Engine_JetPod_L", 0.22, 1.15, (-1.15, -2.05, 0.72), builder.palette.gunmetal, rotation=(math.radians(90), 0.0, 0.0)),
            rmk.make_cylinder(f"{name}Engine_JetPod_R", 0.22, 1.15, (1.15, -2.05, 0.72), builder.palette.gunmetal, rotation=(math.radians(90), 0.0, 0.0)),
        ])
        builder.add_turret(size=(1.75, 2.05, 0.52), center_y=0.45)
        builder.add_sensor_visor()
        builder.add_radar_dish(is_rotodome=True, location=(0.0, -0.95, 1.95))
        builder.add_whip_antenna()

    elif archetype == "heavy_dropship":
        builder.is_hover = True
        builder.objects.extend(rmk.make_aerodynamic_fuselage_assembly(
            f"{name}Armor", length=3.80, width=2.10, height=0.95, location=(0.0, 0.15, 1.15),
            primary_mat=builder.palette.primary_armor, canopy_mat=builder.palette.light_armor,
            intake_mat=builder.palette.gunmetal, exhaust_mat=builder.palette.warning,
            with_wing_fairings=True
        ))
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_RampDoor", (1.55, 0.12, 0.75), (0.0, -1.78, 1.05), builder.palette.gunmetal, 0.02, rotation=(math.radians(16), 0.0, 0.0)),
            rmk.make_box(f"{name}Armor_HighWing_L", (1.45, 0.85, 0.18), (-1.65, 0.25, 1.55), builder.palette.light_armor, 0.03),
            rmk.make_box(f"{name}Armor_HighWing_R", (1.45, 0.85, 0.18), (1.65, 0.25, 1.55), builder.palette.light_armor, 0.03),
            rmk.make_box(f"{name}Joint_LandingSkid_L", (0.16, 2.85, 0.08), (-0.95, 0.15, 0.08), builder.palette.gunmetal, 0.02),
            rmk.make_box(f"{name}Joint_LandingSkid_R", (0.16, 2.85, 0.08), (0.95, 0.15, 0.08), builder.palette.gunmetal, 0.02),
            rmk.make_rod(f"{name}Joint_SkidStrut_FL", (-0.95, 1.15, 0.10), (-0.90, 0.95, 0.72), 0.045, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_SkidStrut_FR", (0.95, 1.15, 0.10), (0.90, 0.95, 0.72), 0.045, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_SkidStrut_RL", (-0.95, -0.85, 0.10), (-0.90, -0.65, 0.72), 0.045, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_SkidStrut_RR", (0.95, -0.85, 0.10), (0.90, -0.65, 0.72), 0.045, builder.palette.gunmetal),
        ])
        builder.add_sensor_visor()
        builder.add_tiltrotors(location_offset=(2.35, 0.25, 1.55), radius=0.62, tilt_deg=12.0)
        builder.turret_center = Vector((0.0, 1.85, 0.65))

    elif archetype == "shield_truck":
        builder.hull_length = 3.5
        builder.hull_width = 2.2
        builder.hull_deck_z = 0.88
        builder.add_all_terrain_wheels(
            wheelbase_y=2.45, track_width_x=1.95, wheel_radius=0.40, wheel_width=0.28, middle_axle=True, with_fenders=True
        )
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_ChassisFrame", (1.65, 3.45, 0.36), (0.0, 0.0, 0.56), builder.palette.gunmetal, 0.03),
            rmk.make_box(f"{name}Armor_ForwardCab", (1.82, 1.10, 0.78), (0.0, 1.15, 1.05), builder.palette.primary_armor, 0.04),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CabCollar", (0.0, 1.15, 0.74), (1.82, 1.10), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_box(f"{name}Armor_GeneratorBed", (1.92, 2.05, 0.56), (0.0, -0.65, 0.95), builder.palette.primary_armor, 0.04),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_BedCollar", (0.0, -0.65, 0.74), (1.92, 2.05), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_cylinder(f"{name}Armor_ProjectorPedestal", 0.68, 0.42, (0.0, -0.65, 1.40), builder.palette.light_armor, vertices=12, bevel=0.03),
            rmk.make_torus(f"{name}Armor_ShieldCoilRing", (0.0, -0.65, 1.65), major_radius=0.72, minor_radius=0.08, material=builder.palette.emissive, major_segments=14, minor_segments=6),
            rmk.make_cylinder(f"{name}Armor_ShieldDomeCore", 0.52, 0.26, (0.0, -0.65, 1.72), builder.palette.emissive, vertices=12, bevel=0.05),
        ])
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            builder.objects.append(rmk.make_box(
                f"{name}Armor_CapacitorBank_{stag}",
                (0.28, 1.45, 0.44), (side * 1.05, -0.65, 0.96), builder.palette.light_armor, 0.025
            ))
        builder.objects.extend(rmk.make_recessed_sensor_visor(
            f"{name}Sensor_CabVisor",
            location=(0.0, 1.70, 1.18),
            size=(1.15, 0.12, 0.18),
            armor_mat=builder.palette.light_armor, lens_mat=builder.palette.emissive, cavity_mat=builder.palette.gunmetal
        ))
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_recovery_winch()
        builder.add_turret(size=(1.15, 1.25, 0.36), center_y=1.10)
        builder.turret_center = Vector((0.0, 1.10, 1.62))
        builder.add_spring_antenna()

    elif archetype == "demolition_breacher":
        builder.add_sloped_hull(length=2.45, width=2.05, height=0.74, bevel=0.04)
        builder.add_deck_edge_weld_seams()
        builder.add_access_hatches()
        builder.add_continuous_tracks(length=2.35, height=0.56, belt_width=0.32, roadwheels=4, with_skirts=skirts)
        builder.add_final_drive_housings(radius=0.18)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_recovery_winch()
        builder.add_splash_rib()
        builder.add_driver_periscope_hood()
        builder.add_glacis_weld_seams()
        builder.add_era_bricks()
        builder.objects.extend([
            rmk.make_cylinder(
                f"{name}Armor_VolatileCoreDrum", 0.56, 1.35,
                (0.0, -0.18, 1.05), builder.palette.warning,
                rotation=(0.0, math.radians(90), 0.0), vertices=14, bevel=0.03
            ),
            rmk.make_cylinder(
                f"{name}Armor_CoreGlowBand", 0.58, 0.32,
                (0.0, -0.18, 1.05), builder.palette.emissive,
                rotation=(0.0, math.radians(90), 0.0), vertices=14, bevel=0.01
            ),
            rmk.make_box(f"{name}Armor_BreacherProw", (1.85, 0.42, 0.38), (0.0, 1.25, 0.55), builder.palette.primary_armor, 0.03),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_ProwCollar", (0.0, 1.25, 0.36), (1.85, 0.42), height=0.035, collar_mat=builder.palette.gunmetal
            ),
        ])
        for side in (-1, 0, 1):
            builder.objects.append(rmk.make_rod(
                f"{name}Armor_DetonatorSpike_{side+2}",
                (side * 0.55, 1.35, 0.55), (side * 0.55, 1.78, 0.55),
                0.045, builder.palette.gunmetal, vertices=6
            ))
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_interlocking_gussets()
        builder.add_turret(size=(0.95, 1.05, 0.32), center_y=0.52)
        builder.add_crew_cupola()
        builder.add_sensor_visor()

    elif archetype == "artillery_strider":
        builder.add_bipedal_legs(hip_height=1.78, hip_spacing_x=1.02, total_height=1.85)
        builder.hull_length = 2.2
        builder.hull_width = 1.6
        builder.hull_deck_z = 2.25
        builder.add_cast_pelvis(location=(0.0, -0.04, 1.72), width=1.05, depth=0.78, height=0.44)
        builder.add_mech_torso(location=(0.0, -0.05, 2.26), width=1.28, length=1.35, height=0.68)
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_CounterweightBustle", (0.96, 0.68, 0.52), (0.0, -0.92, 2.30), builder.palette.light_armor, 0.05),
            rmk.make_cylinder(f"{name}Sensor_OpticMast", 0.14, 0.55, (-0.48, 0.35, 2.78), builder.palette.gunmetal, vertices=10, bevel=0.015),
        ])
        builder.add_lifting_lugs()
        builder.add_turret(size=(1.25, 1.55, 0.44), center_y=0.05)
        builder.turret_center = Vector((0.0, 0.05, 2.75))
        builder.add_sensor_visor()
        builder.add_whip_antenna()

    elif archetype == "thermobaric_tank":
        builder.add_sloped_hull(length=3.25, width=2.55, height=0.92, bevel=0.045)
        builder.add_deck_edge_weld_seams()
        builder.add_access_hatches()
        builder.add_continuous_tracks(length=3.05, height=0.64, belt_width=0.36, roadwheels=6, with_skirts=skirts)
        builder.add_final_drive_housings()
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            builder.objects.extend([
                rmk.make_cylinder(
                    f"{name}Armor_CryoFuelTank_{stag}", 0.28, 1.45,
                    (side * 0.68, -0.68, 1.18), builder.palette.warning,
                    rotation=(math.radians(90), 0.0, 0.0), vertices=12, bevel=0.025
                ),
                rmk.make_box(
                    f"{name}Armor_TankGuard_{stag}",
                    (0.62, 1.52, 0.12), (side * 0.68, -0.68, 1.44),
                    builder.palette.light_armor, 0.02
                ),
                rmk.make_rod(
                    f"{name}Armor_TransferPipe_{stag}",
                    (side * 0.52, 0.02, 1.18), (side * 0.38, 0.35, 1.26),
                    0.045, builder.palette.gunmetal, vertices=8
                ),
            ])
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_splash_rib()
        builder.add_glacis_weld_seams()
        builder.add_turret(size=(1.50, 1.65, 0.46), center_y=0.22)
        builder.add_turret_ring_guard()
        builder.add_crew_cupola()
        builder.add_commander_sight()
        builder.add_bustle_basket()
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "ew_jammer":
        builder.hull_length = 3.35
        builder.hull_width = 2.25
        builder.hull_deck_z = 0.90
        builder.add_all_terrain_wheels(
            wheelbase_y=2.35, track_width_x=1.95, wheel_radius=0.39, wheel_width=0.28, middle_axle=True, with_fenders=True
        )
        builder.objects.extend([
            rmk.make_sloped_glacis_hull(
                f"{name}Armor_Hull",
                length_front=1.68, length_rear=1.65,
                width_front=1.10, width_rear=1.18,
                height_lower=builder.ground_clearance + 0.20, height_upper=0.90,
                material=builder.palette.primary_armor, bevel=0.04
            ),
            rmk.make_box(f"{name}Armor_EWShelter", (1.76, 1.95, 0.62), (0.0, -0.35, 1.16), builder.palette.light_armor, 0.035),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_ShelterCollar", (0.0, -0.35, 0.85), (1.76, 1.95), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_cylinder(f"{name}Sensor_InterferenceMast", 0.16, 0.78, (0.0, -0.78, 1.75), builder.palette.gunmetal, vertices=10, bevel=0.015),
        ])
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            builder.objects.extend([
                rmk.make_box(
                    f"{name}Sensor_PhasedArrayPanel_{stag}",
                    (0.12, 0.88, 0.46), (side * 0.96, -0.35, 1.22),
                    builder.palette.emissive, 0.015, rotation=(0.0, math.radians(side * -12), 0.0)
                ),
                rmk.make_box(
                    f"{name}Armor_CoolingLouvers_{stag}",
                    (0.16, 0.75, 0.32), (side * 0.92, 0.65, 0.78),
                    builder.palette.gunmetal, 0.015
                ),
            ])
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_turret(size=(1.22, 1.35, 0.38), center_y=0.42)
        builder.turret_center = Vector((0.0, 0.42, 1.58))
        builder.add_sensor_visor()
        builder.add_whip_antenna()

    elif archetype == "mobile_sam_launcher":
        # 6x6 wheeled SAM carrier with a rear erector-launcher of sealed canisters.
        builder.hull_length = 3.6
        builder.hull_width = 2.2
        builder.hull_deck_z = 0.90
        builder.add_all_terrain_wheels(
            wheelbase_y=2.55, track_width_x=1.95, wheel_radius=0.40, wheel_width=0.28,
            middle_axle=True, lug_count=6 if rts_budget else 10, bolt_count=0 if rts_budget else 6, with_fenders=True
        )
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_ChassisFrame", (1.62, 3.55, 0.34), (0.0, 0.0, 0.56), builder.palette.gunmetal, 0.03),
            rmk.make_box(f"{name}Armor_ForwardCab", (1.80, 1.05, 0.74), (0.0, 1.28, 1.02), builder.palette.primary_armor, 0.04),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CabCollar", (0.0, 1.28, 0.65), (1.80, 1.05), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_box(f"{name}Armor_LauncherBed", (1.72, 1.95, 0.22), (0.0, -0.62, 0.86), builder.palette.light_armor, 0.025),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_BedCollar", (0.0, -0.62, 0.75), (1.72, 1.95), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_box(f"{name}Armor_ErectorCradle", (1.30, 0.32, 0.30), (0.0, -1.42, 1.08), builder.palette.gunmetal, 0.02),
        ])
        erector_rot = (math.radians(-52), 0.0, 0.0)
        for idx, cx in enumerate((-0.44, 0.0, 0.44)):
            builder.objects.extend([
                rmk.make_cylinder(f"{name}Missile_SAMCanister_{idx}", 0.17, 2.05, (cx, -0.78, 1.72),
                                  builder.palette.primary_armor, rotation=erector_rot, vertices=12, bevel=0.015),
                rmk.make_cylinder(f"{name}Missile_SAMCap_{idx}", 0.175, 0.05, (cx, -0.15, 2.52),
                                  builder.palette.warning, rotation=erector_rot, vertices=12, bevel=0.005),
            ])
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            builder.objects.extend([
                rmk.make_rod(f"{name}Joint_ErectorRam_{stag}", (side * 0.72, -1.15, 0.95), (side * 0.72, -0.55, 1.65),
                             0.05, builder.palette.wheel_rim, vertices=8),
                rmk.make_box(f"{name}Armor_StabilizerJack_{stag}", (0.16, 0.16, 0.48), (side * 0.98, -1.55, 0.30),
                             builder.palette.gunmetal, 0.015),
            ])
        builder.objects.extend(rmk.make_recessed_sensor_visor(
            f"{name}Sensor_CabVisor", location=(0.0, 1.82, 1.16), size=(1.10, 0.12, 0.17),
            armor_mat=builder.palette.light_armor, lens_mat=builder.palette.emissive, cavity_mat=builder.palette.gunmetal
        ))
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_turret(size=(0.95, 1.00, 0.30), center_y=1.20)
        builder.turret_center = Vector((0.0, 1.20, 1.55))
        builder.add_radar_dish(is_rotodome=False)
        builder.add_spring_antenna()

    elif archetype == "recovery_crawler":
        # Armored recovery vehicle: tracked hull, slewing crane boom, winch drum and earth spade.
        builder.add_sloped_hull(length=3.4, width=2.6, height=0.96, bevel=0.045)
        builder.add_deck_edge_weld_seams()
        builder.add_access_hatches()
        builder.add_continuous_tracks(length=3.2, height=0.66, belt_width=0.36, roadwheels=6, with_skirts=skirts)
        builder.add_final_drive_housings(radius=0.21)
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_CraneSlewBase", (0.85, 0.85, 0.28), (0.62, 0.55, 1.18), builder.palette.light_armor, 0.03),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CraneCollar", (0.62, 0.55, 1.04), (0.85, 0.85), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_rod(f"{name}Joint_CraneBoom", (0.62, 0.55, 1.32), (0.62, -1.15, 2.55), 0.11, builder.palette.warning, vertices=8),
            rmk.make_rod(f"{name}Joint_CraneJib", (0.62, -1.15, 2.55), (0.62, -1.75, 2.20), 0.07, builder.palette.warning, vertices=8),
            rmk.make_rod(f"{name}Joint_CraneHoistCable", (0.62, -1.75, 2.20), (0.62, -1.75, 1.35), 0.015, builder.palette.gunmetal, vertices=6),
            rmk.make_box(f"{name}Joint_CraneHook", (0.14, 0.08, 0.20), (0.62, -1.75, 1.25), builder.palette.gunmetal, 0.01),
            rmk.make_cylinder(f"{name}Joint_WinchDrum", 0.20, 1.20, (0.0, 1.45, 0.82), builder.palette.gunmetal,
                              rotation=(0.0, math.radians(90), 0.0), vertices=12, bevel=0.01),
            rmk.make_box(f"{name}Armor_EarthSpade", (2.30, 0.16, 0.52), (0.0, 1.82, 0.30), builder.palette.primary_armor, 0.025,
                         rotation=(math.radians(-18), 0.0, 0.0)),
            rmk.make_box(f"{name}Armor_ToolLocker", (0.75, 1.30, 0.38), (-0.70, -0.45, 1.14), builder.palette.light_armor, 0.025),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_LockerCollar", (-0.70, -0.45, 0.95), (0.75, 1.30), height=0.045, collar_mat=builder.palette.gunmetal
            ),
        ])
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_recovery_winch()
        builder.add_turret(size=(0.95, 1.00, 0.32), center_y=0.40)
        builder.add_turret_ring_guard()
        builder.add_crew_cupola()
        builder.turret_center = Vector((-0.55, 0.40, 1.30))
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "mine_layer":
        # Tracked mine layer: rear hopper, angled dispensing chute and racked AT mine discs.
        builder.add_sloped_hull(length=3.2, width=2.5, height=0.92, bevel=0.045)
        builder.add_deck_edge_weld_seams()
        builder.add_access_hatches()
        builder.add_continuous_tracks(length=3.05, height=0.64, belt_width=0.34, roadwheels=5, with_skirts=skirts)
        builder.add_final_drive_housings()
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_splash_rib()
        builder.add_driver_periscope_hood()
        builder.add_glacis_weld_seams()
        builder.add_exhaust_deflector()
        builder.add_exhaust_heat_shield()
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_MineHopper", (1.70, 1.30, 0.62), (0.0, -0.80, 1.22), builder.palette.primary_armor, 0.035),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_HopperCollar", (0.0, -0.80, 0.91), (1.70, 1.30), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_box(f"{name}Armor_HopperLid", (1.76, 1.36, 0.08), (0.0, -0.80, 1.57), builder.palette.light_armor, 0.015),
            rmk.make_box(f"{name}Joint_DispenserChute", (0.62, 1.05, 0.10), (0.0, -1.95, 0.62), builder.palette.gunmetal, 0.015,
                         rotation=(math.radians(-32), 0.0, 0.0)),
        ])
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            builder.objects.append(rmk.make_box(
                f"{name}Armor_ChuteRail_{stag}", (0.05, 1.05, 0.16), (side * 0.33, -1.95, 0.68),
                builder.palette.warning, 0.008, rotation=(math.radians(-32), 0.0, 0.0)
            ))
            for idx in range(3):
                builder.objects.append(rmk.make_cylinder(
                    f"{name}Armor_ATMine_{stag}{idx}", 0.16, 0.08, (side * 1.02, -0.30 - idx * 0.38, 1.02),
                    builder.palette.warning, rotation=(0.0, math.radians(90), 0.0), vertices=12, bevel=0.01
                ))
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_turret(size=(1.05, 1.15, 0.34), center_y=0.62)
        builder.add_crew_cupola()
        builder.turret_center = Vector((0.0, 0.62, 1.30))
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "sniper_tankette":
        # Low-silhouette two-crew tankette with a long-barrel precision gun.
        builder.add_sloped_hull(length=2.35, width=1.95, height=0.62, bevel=0.035)
        builder.add_deck_edge_weld_seams()
        builder.add_continuous_tracks(length=2.25, height=0.50, belt_width=0.28, roadwheels=4, with_skirts=skirts)
        builder.add_final_drive_housings(radius=0.16, depth=0.10)
        builder.add_engine_deck(slat_count=3, with_exhausts=True)
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_CasemateShield", (1.10, 0.18, 0.30), (0.0, 0.62, 0.88), builder.palette.light_armor, 0.02,
                         rotation=(math.radians(-24), 0.0, 0.0)),
            rmk.make_cylinder(f"{name}Sensor_ThermalSight", 0.08, 0.26, (-0.38, 0.30, 1.12), builder.palette.gunmetal,
                              rotation=(math.radians(90), 0.0, 0.0), vertices=10, bevel=0.008),
        ])
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_turret(size=(1.05, 1.15, 0.28), center_y=0.05)
        builder.add_turret_ring_guard()
        builder.add_crew_cupola()
        builder.add_sensor_visor()

    elif archetype == "hexapod_assault":
        # Six-legged assault crawler: four rigged crawler legs plus a static middle leg pair bound to the body.
        builder.add_sloped_hull(length=3.0, width=2.4, height=0.84, bevel=0.05)
        builder.add_deck_edge_weld_seams()
        builder.objects.extend([
            rmk.make_box(
                f"{name}Armor_ThoraxCore", (1.45, 1.95, 0.42), (0.0, 0.0, 1.02), builder.palette.primary_armor, 0.04
            ),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_ThoraxCollar", (0.0, 0.0, 0.81), (1.45, 1.95), height=0.045, collar_mat=builder.palette.gunmetal
            ),
        ])
        builder.add_quad_crawler_legs(knee_height=1.05, reach_x=1.25, reach_y=1.05)
        for side in (-1, 1):
            stag = "ML" if side < 0 else "MR"
            hip = Vector((side * builder.hull_width * 0.48, 0.0, 0.95))
            knee = Vector((side * (builder.hull_width * 0.48 + 0.85), 0.0, 1.30))
            foot = Vector((side * (builder.hull_width * 0.48 + 1.30), 0.0, 0.08))
            builder.objects.extend([
                rmk.make_cylinder(f"{name}Joint_MidHip_{stag}", 0.16, 0.22, tuple(hip), builder.palette.gunmetal,
                                  rotation=(0.0, math.radians(90), 0.0), vertices=10, bevel=0.012),
                rmk.make_rod(f"{name}Armor_MidThigh_{stag}", hip, knee, 0.10, builder.palette.light_armor, vertices=8),
                rmk.make_rod(f"{name}Armor_MidShin_{stag}", knee, foot, 0.075, builder.palette.primary_armor, vertices=8),
                rmk.make_cylinder(f"{name}Joint_MidFootPad_{stag}", 0.15, 0.08, (foot.x, foot.y, 0.04),
                                  builder.palette.tread, vertices=10, bevel=0.01),
            ])
        builder.add_turret(size=(1.45, 1.55, 0.44), center_y=0.08)
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "assault_hovercraft":
        # Amphibious assault hovercraft: inflated skirt, bow ramp and twin ducted rear propellers.
        builder.is_hover = True
        builder.add_sloped_hull(length=3.9, width=2.7, height=0.80, bevel=0.05)
        builder.add_deck_edge_weld_seams()
        builder.add_hover_skimmer(float_height=0.42)
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_BowRamp", (1.55, 0.12, 0.62), (0.0, 2.02, 0.82), builder.palette.light_armor, 0.02,
                         rotation=(math.radians(28), 0.0, 0.0)),
            rmk.make_box(f"{name}Armor_PilotHouse", (0.90, 0.95, 0.55), (-0.70, 0.55, 1.35), builder.palette.light_armor, 0.03),
        ])
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            duct_loc = (side * 0.78, -1.85, 1.55)
            builder.objects.extend([
                rmk.make_torus(f"{name}Engine_PropDuct_{stag}", duct_loc, major_radius=0.55, minor_radius=0.07,
                               material=builder.palette.primary_armor, rotation=(math.radians(90), 0.0, 0.0),
                               major_segments=16, minor_segments=6),
                rmk.make_box(f"{name}Engine_PropBlade_{stag}", (0.95, 0.04, 0.10), duct_loc, builder.palette.gunmetal, 0.01,
                             rotation=(0.0, math.radians(30), 0.0)),
                rmk.make_box(f"{name}Joint_PropPylon_{stag}", (0.12, 0.20, 0.62), (side * 0.78, -1.85, 0.98),
                             builder.palette.gunmetal, 0.015),
                rmk.make_box(f"{name}Armor_Rudder_{stag}", (0.05, 0.28, 0.70), (side * 0.78, -2.25, 1.55),
                             builder.palette.warning, 0.01),
            ])
        builder.add_turret(size=(1.15, 1.25, 0.36), center_y=0.85)
        builder.add_sensor_visor()
        builder.add_whip_antenna()

    elif archetype == "bridge_layer":
        # Tracked scissor-bridge layer (AVLB) with hydraulic launch boom and twin folded treadway spans.
        builder.add_sloped_hull(length=3.6, width=2.65, height=0.92, bevel=0.045)
        builder.add_deck_edge_weld_seams()
        builder.add_access_hatches()
        builder.add_continuous_tracks(length=3.4, height=0.66, belt_width=0.38, roadwheels=6, with_skirts=skirts)
        builder.add_final_drive_housings(radius=0.22)
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_recovery_winch()
        builder.add_splash_rib()
        builder.add_driver_periscope_hood()
        builder.add_glacis_weld_seams()
        builder.add_exhaust_deflector()
        builder.add_exhaust_heat_shield()
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_LaunchCradle", (1.85, 0.55, 0.36), (0.0, 1.48, 1.05), builder.palette.gunmetal, 0.025),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CradleCollar", (0.0, 1.48, 0.87), (1.85, 0.55), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_rod(f"{name}Joint_LaunchBoom_L", (-0.65, 0.85, 1.05), (-0.65, 1.75, 1.68), 0.065, builder.palette.warning, vertices=8),
            rmk.make_rod(f"{name}Joint_LaunchBoom_R", (0.65, 0.85, 1.05), (0.65, 1.75, 1.68), 0.065, builder.palette.warning, vertices=8),
            rmk.make_cylinder(f"{name}Joint_ScissorHingePin", 0.14, 2.35, (0.0, 1.78, 1.62), builder.palette.gunmetal,
                              rotation=(0.0, math.radians(90.0), 0.0), vertices=12, bevel=0.015),
        ])
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            tx = side * 0.72
            builder.objects.extend([
                rmk.make_box(f"{name}Armor_BridgeSpanLower_{stag}", (0.78, 3.45, 0.18), (tx, 0.05, 1.38), builder.palette.primary_armor, 0.025),
                rmk.make_box(f"{name}Armor_BridgeSpanUpper_{stag}", (0.78, 3.45, 0.18), (tx, 0.05, 1.66), builder.palette.light_armor, 0.025),
                rmk.make_box(f"{name}Armor_ApproachRamp_{stag}", (0.74, 0.65, 0.10), (tx, -1.85, 1.52), builder.palette.warning, 0.015,
                             rotation=(math.radians(18.0), 0.0, 0.0)),
            ])
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_turret(size=(0.88, 0.96, 0.28), center_y=0.95)
        builder.add_crew_cupola()
        builder.turret_center = Vector((0.0, 0.95, 1.92))
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "command_hq_rig":
        # 8x8 heavy C4ISR mobile command HQ truck with expandable tactical shelter, satcom dome, and mast.
        builder.hull_length = 4.1
        builder.hull_width = 2.35
        builder.hull_deck_z = 0.94
        builder.add_all_terrain_wheels(
            wheelbase_y=2.85, track_width_x=2.05, wheel_radius=0.41, wheel_width=0.30, middle_axle=True, with_fenders=True
        )
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            builder.objects.extend(rmk.make_all_terrain_wheel_assembly(
                f"{name}Wheel_MidFront_{stag}",
                location=(side * 1.025, 0.52, builder.ground_clearance + 0.41 + 0.035),
                radius=0.41, width=0.30,
                tire_mat=builder.palette.tread, rim_mat=builder.palette.wheel_rim,
                hub_mat=builder.palette.gunmetal, accent_mat=builder.palette.warning
            ))
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_ChassisSpine", (1.75, 4.05, 0.36), (0.0, 0.0, 0.58), builder.palette.gunmetal, 0.03),
            rmk.make_box(f"{name}Armor_ArmoredCab", (2.05, 1.22, 0.84), (0.0, 1.38, 1.08), builder.palette.primary_armor, 0.04),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CabCollar", (0.0, 1.38, 0.66), (2.05, 1.22), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_box(f"{name}Armor_CommandShelter", (2.18, 2.45, 0.96), (0.0, -0.62, 1.24), builder.palette.primary_armor, 0.045),
            rmk.make_box(f"{name}Armor_SlideOutBay_L", (0.24, 1.65, 0.68), (-1.18, -0.62, 1.24), builder.palette.light_armor, 0.025),
            rmk.make_box(f"{name}Armor_SlideOutBay_R", (0.24, 1.65, 0.68), (1.18, -0.62, 1.24), builder.palette.light_armor, 0.025),
            rmk.make_cylinder(f"{name}Sensor_SatcomRadome", 0.46, 0.34, (0.0, -0.35, 1.88), builder.palette.light_armor, vertices=12, bevel=0.04),
            rmk.make_cylinder(f"{name}Sensor_TelescopicMast", 0.12, 0.95, (-0.58, -1.45, 2.15), builder.palette.gunmetal, vertices=10, bevel=0.015),
            rmk.make_box(f"{name}Sensor_UplinkCrossbar", (0.85, 0.10, 0.12), (-0.58, -1.45, 2.62), builder.palette.emissive, 0.015),
        ])
        builder.objects.extend(rmk.make_recessed_sensor_visor(
            f"{name}Sensor_CabVisor", location=(0.0, 2.00, 1.22), size=(1.28, 0.12, 0.18),
            armor_mat=builder.palette.light_armor, lens_mat=builder.palette.emissive, cavity_mat=builder.palette.gunmetal
        ))
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_interlocking_gussets()
        builder.add_recovery_winch()
        builder.add_turret(size=(1.05, 1.12, 0.32), center_y=1.32)
        builder.turret_center = Vector((0.0, 1.32, 1.66))
        builder.add_radar_dish(is_rotodome=True, location=(0.45, -1.25, 2.05))
        builder.add_whip_antenna()

    elif archetype == "flying_wing_bomber":
        # Low-observable delta flying-wing strike craft with swept wings, dorsal intakes, and landing skids.
        builder.is_hover = True
        builder.hull_length = 3.2
        builder.hull_width = 4.2
        builder.hull_deck_z = 1.05
        builder.objects.extend([
            rmk.make_sloped_glacis_hull(
                f"{name}Armor_CentralBlendBody",
                length_front=1.75, length_rear=1.45,
                width_front=1.35, width_rear=1.85,
                height_lower=0.58, height_upper=1.08,
                material=builder.palette.primary_armor, bevel=0.04
            ),
            rmk.make_box(f"{name}Armor_StealthCanopy", (0.92, 1.05, 0.26), (0.0, 0.72, 1.14), builder.palette.emissive, 0.03,
                         rotation=(math.radians(-12.0), 0.0, 0.0)),
            rmk.make_box(f"{name}Armor_BombBayDoors", (0.95, 1.45, 0.08), (0.0, -0.05, 0.54), builder.palette.gunmetal, 0.015),
        ])
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            builder.objects.extend([
                rmk.make_box(
                    f"{name}Armor_SweptWing_{stag}", (1.65, 1.45, 0.18),
                    (side * 1.32, -0.22, 0.82), builder.palette.primary_armor, 0.025,
                    rotation=(0.0, math.radians(side * -4.0), math.radians(side * -28.0))
                ),
                rmk.make_box(
                    f"{name}Armor_WingtipElevon_{stag}", (0.68, 0.52, 0.08),
                    (side * 2.02, -0.78, 0.78), builder.palette.light_armor, 0.015,
                    rotation=(0.0, 0.0, math.radians(side * -28.0))
                ),
                rmk.make_box(
                    f"{name}Engine_DorsalIntake_{stag}", (0.38, 0.88, 0.22),
                    (side * 0.48, -0.35, 1.12), builder.palette.gunmetal, 0.02
                ),
                rmk.make_cylinder(
                    f"{name}Engine_StealthNozzle_{stag}", 0.16, 0.35,
                    (side * 0.48, -1.42, 0.88), builder.palette.emissive,
                    rotation=(math.radians(90.0), 0.0, 0.0), vertices=10, bevel=0.015
                ),
                rmk.make_box(f"{name}Joint_LandingSkid_{stag}", (0.10, 1.45, 0.05), (side * 0.58, 0.0, 0.04), builder.palette.gunmetal, 0.01),
                rmk.make_rod(f"{name}Joint_SkidStrutF_{stag}", (side * 0.58, 0.52, 0.06), (side * 0.54, 0.45, 0.58), 0.032, builder.palette.gunmetal),
                rmk.make_rod(f"{name}Joint_SkidStrutR_{stag}", (side * 0.58, -0.52, 0.06), (side * 0.54, -0.45, 0.58), 0.032, builder.palette.gunmetal),
            ])
        builder.turret_center = Vector((0.0, 0.65, 0.52))
        builder.add_sensor_visor()

    elif archetype == "mortar_carrier":
        # Open-hatch tracked heavy siege mortar carrier with split roof doors, baseplate, and ammo racks.
        builder.add_sloped_hull(length=3.15, width=2.48, height=0.96, bevel=0.045)
        builder.add_deck_edge_weld_seams()
        builder.add_access_hatches()
        builder.add_continuous_tracks(length=2.98, height=0.64, belt_width=0.35, roadwheels=5, with_skirts=skirts)
        builder.add_final_drive_housings()
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_splash_rib()
        builder.add_driver_periscope_hood()
        builder.add_glacis_weld_seams()
        builder.add_exhaust_deflector()
        builder.add_exhaust_heat_shield()
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_CasemateSuperstructure", (1.92, 2.05, 0.48), (0.0, -0.22, 1.16), builder.palette.primary_armor, 0.035),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CasemateCollar", (0.0, -0.22, 0.92), (1.92, 2.05), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_cylinder(f"{name}Joint_MortarTurntable", 0.68, 0.16, (0.0, -0.25, 1.18), builder.palette.gunmetal, vertices=12, bevel=0.015),
        ])
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            builder.objects.extend([
                rmk.make_box(
                    f"{name}Armor_OpenRoofHatch_{stag}", (0.72, 1.48, 0.08),
                    (side * 0.98, -0.25, 1.56), builder.palette.light_armor, 0.015,
                    rotation=(0.0, math.radians(side * -62.0), 0.0)
                ),
                rmk.make_box(
                    f"{name}Armor_RearRecoilSpade_{stag}", (0.24, 0.28, 0.34),
                    (side * 0.65, -1.62, 0.42), builder.palette.gunmetal, 0.02,
                    rotation=(math.radians(24.0), 0.0, 0.0)
                ),
            ])
            for idx in range(3):
                builder.objects.append(rmk.make_cylinder(
                    f"{name}Armor_MortarShell_{stag}{idx}", 0.085, 0.48,
                    (side * 0.72, -0.68 + idx * 0.32, 1.32), builder.palette.warning,
                    vertices=10, bevel=0.01
                ))
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_turret(size=(1.12, 1.18, 0.26), center_y=-0.20)
        builder.add_turret_ring_guard()
        builder.add_crew_cupola()
        builder.turret_center = Vector((0.0, -0.20, 1.38))
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "siege_drill_mole":
        # Subterranean assault bore vehicle with stepped conical front auger drill, side vents, and heavy tracks.
        builder.add_sloped_hull(length=3.35, width=2.62, height=0.98, bevel=0.045)
        builder.add_deck_edge_weld_seams()
        builder.add_access_hatches()
        builder.add_continuous_tracks(length=3.15, height=0.68, belt_width=0.38, roadwheels=6, with_skirts=skirts)
        builder.add_final_drive_housings(radius=0.22)
        builder.add_engine_deck(slat_count=5, with_exhausts=True)
        # Multi-stage stepped conical borer head + helical cutter rings along +Y prow
        builder.objects.append(rmk.make_cylinder(
            f"{name}Joint_DrillGearboxCollar", 0.72, 0.32,
            (0.0, 1.72, 0.74), builder.palette.gunmetal,
            rotation=(math.radians(90.0), 0.0, 0.0), vertices=14, bevel=0.02
        ))
        drill_stages = [
            (0.64, 0.36, 1.98),
            (0.50, 0.34, 2.28),
            (0.35, 0.32, 2.55),
            (0.18, 0.30, 2.80),
        ]
        for idx, (r_s, d_s, y_s) in enumerate(drill_stages, start=1):
            builder.objects.extend([
                rmk.make_cylinder(
                    f"{name}Armor_BoreCone_{idx}", r_s, d_s,
                    (0.0, y_s, 0.74), builder.palette.wheel_rim if idx % 2 == 0 else builder.palette.gunmetal,
                    rotation=(math.radians(90.0), 0.0, math.radians(idx * 22.5)),
                    vertices=12, bevel=0.015
                ),
                rmk.make_torus(
                    f"{name}Armor_AugerFlightRing_{idx}", (0.0, y_s + 0.06, 0.74),
                    major_radius=r_s * 1.04, minor_radius=0.045,
                    material=builder.palette.warning,
                    rotation=(math.radians(82.0), math.radians(8.0), 0.0),
                    major_segments=12, minor_segments=6
                ),
            ])
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_turret(size=(1.22, 1.32, 0.36), center_y=0.05)
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "heavy_crane_mech":
        # Industrial & siege bipedal walker with dorsal fusion reactor, shoulder gantry crane, and hydraulic claws.
        builder.add_bipedal_legs(hip_height=1.95, hip_spacing_x=1.18, total_height=2.00)
        builder.hull_length = 2.4
        builder.hull_width = 1.9
        builder.hull_deck_z = 2.65
        builder.add_cast_pelvis(location=(0.0, -0.04, 1.90), width=1.28, depth=0.86, height=0.48)
        builder.add_mech_torso(location=(0.0, 0.04, 2.55), width=1.68, length=1.22, height=0.96)
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_RollCageCanopy", (1.12, 0.55, 0.58), (0.0, 0.62, 2.58), builder.palette.warning, 0.045),
            rmk.make_cylinder(f"{name}Armor_DorsalReactorDrum", 0.46, 1.25, (0.0, -0.72, 2.62), builder.palette.gunmetal,
                              rotation=(0.0, math.radians(90.0), 0.0), vertices=12, bevel=0.03),
            rmk.make_rod(f"{name}Joint_ShoulderCraneJib", (-0.72, -0.15, 3.05), (-0.72, 1.25, 3.48), 0.085, builder.palette.warning, vertices=8),
        ])
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            sx = side * 1.12
            builder.objects.extend([
                rmk.make_box(f"{name}Joint_ShoulderMount_{stag}", (0.44, 0.58, 0.44), (sx, 0.08, 2.75), builder.palette.light_armor, 0.04),
                rmk.make_rod(f"{name}Armor_ArmBoom_{stag}", (sx, 0.15, 2.68), (sx * 1.05, 0.92, 2.15), 0.11, builder.palette.gunmetal, vertices=8),
                rmk.make_box(f"{name}Armor_ClawPincerUpper_{stag}", (0.28, 0.48, 0.10), (sx * 1.05, 1.18, 2.22), builder.palette.warning, 0.015,
                             rotation=(math.radians(-14.0), 0.0, 0.0)),
                rmk.make_box(f"{name}Armor_ClawPincerLower_{stag}", (0.28, 0.48, 0.10), (sx * 1.05, 1.18, 2.06), builder.palette.warning, 0.015,
                             rotation=(math.radians(14.0), 0.0, 0.0)),
            ])
        # The compact rear-set turret places both cannon firepoints at the native
        # GatlingWalker muzzle envelope after the preset's 0.75 model scale.
        builder.add_lifting_lugs()
        builder.add_interlocking_gussets(locations=[(-0.75, -0.05, 2.35), (0.75, -0.05, 2.35)])
        builder.add_turret(size=(1.28, 1.42, 0.42), center_y=-0.40)
        builder.turret_center = Vector((0.0, -0.40, 2.145))
        builder.add_sensor_visor()
        builder.add_whip_antenna()

    elif archetype == "nanite_support_rover":
        # The Wave 7 tread-lug profile extends below the tire carcass; leave
        # an explicit margin so every exported lug remains above Z=0.
        wheel_z = builder.ground_clearance + 0.43 + 0.035
        rover_wheel_locs = []
        for side in (-1, 1):
            tag = "L" if side < 0 else "R"
            for y_off, ytag in ((1.05, "F"), (-1.05, "R")):
                w_loc = (side * 0.95, y_off, wheel_z)
                rover_wheel_locs.append(w_loc)
                builder.objects.extend(rmk.make_all_terrain_wheel_assembly(
                    f"{name}Wheel_{ytag}_{tag}",
                    location=w_loc,
                    radius=0.42, width=0.28,
                    tire_mat=builder.palette.tread, rim_mat=builder.palette.wheel_rim,
                    hub_mat=builder.palette.gunmetal, accent_mat=builder.palette.warning
                ))
        builder.objects.append(rmk.make_box(
            f"{name}Joint_FrontAxle", (1.75, 0.16, 0.14), (0.0, 1.05, wheel_z), builder.palette.gunmetal, 0.02
        ))
        builder.objects.append(rmk.make_box(
            f"{name}Joint_RearAxle", (1.75, 0.16, 0.14), (0.0, -1.05, wheel_z), builder.palette.gunmetal, 0.02
        ))
        builder.objects.extend([
            rmk.make_sloped_glacis_hull(
                f"{name}Armor_Chassis",
                length_front=1.20, length_rear=1.35,
                width_front=1.35, width_rear=1.45,
                height_lower=builder.ground_clearance + 0.28, height_upper=0.76,
                material=builder.palette.primary_armor, bevel=0.035
            ),
            rmk.make_box(
                f"{name}Armor_CabCowl",
                (1.18, 0.95, 0.42), (0.0, 0.35, 1.02),
                builder.palette.light_armor, bevel=0.03,
                rotation=(math.radians(-14), 0.0, 0.0)
            ),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_Cab", (0.0, 0.35, 0.81), (1.18, 0.95), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_box(
                f"{name}Armor_EquipmentBay",
                (1.24, 1.15, 0.48), (0.0, -0.65, 0.98),
                builder.palette.primary_armor, bevel=0.03
            ),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_Bay", (0.0, -0.65, 0.74), (1.24, 1.15), height=0.045, collar_mat=builder.palette.gunmetal
            ),
        ])
        builder.add_wheel_fenders(rover_wheel_locs, radius=0.42, width=0.28)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_turret(size=(1.10, 1.25, 0.36), center_y=0.10)
        builder.add_sensor_visor()
        builder.add_nanite_dispenser(scale=0.92, spacing_x=1.75)

    elif archetype == "gravity_siege_breaker":
        builder.add_continuous_tracks(
            length=3.8, height=0.74, belt_width=0.42, roadwheels=6, with_skirts=skirts
        )
        builder.add_final_drive_housings(radius=0.24, depth=0.15)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_interlocking_gussets()
        builder.add_splash_rib()
        builder.add_driver_periscope_hood()
        builder.add_glacis_weld_seams()
        builder.add_access_hatches()
        builder.add_commander_sight()
        builder.add_era_bricks()
        builder.objects.extend([
            rmk.make_sloped_glacis_hull(
                f"{name}Armor_CitadelLower",
                length_front=1.75, length_rear=1.65,
                width_front=2.10, width_rear=2.15,
                height_lower=builder.ground_clearance + 0.38, height_upper=0.95,
                material=builder.palette.primary_armor, bevel=0.045
            ),
            rmk.make_box(
                f"{name}Armor_UpperCasemate",
                (1.95, 2.40, 0.55), (0.0, -0.15, 1.25),
                builder.palette.light_armor, bevel=0.04
            ),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CasemateCollar", (0.0, -0.15, 0.97), (1.95, 2.40), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_box(
                f"{name}Armor_RearPowerCore",
                (1.45, 0.85, 0.65), (0.0, -1.35, 1.28),
                builder.palette.gunmetal, bevel=0.035
            ),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_PowerCoreCollar", (0.0, -1.35, 0.95), (1.45, 0.85), height=0.045, collar_mat=builder.palette.gunmetal
            ),
        ])
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_interlocking_gussets()
        builder.add_turret(size=(1.65, 1.95, 0.48), center_y=0.15)
        builder.add_turret_ring_guard()
        builder.add_ram_prow(width=2.5, height=0.60, forward_reach=0.48)
        builder.add_grav_dampener(radius=0.38, height=0.25, location=(0.0, -0.85, 1.55))
        builder.add_engine_deck(slat_count=4, with_exhausts=True)

    elif archetype == "tactical_relay_carrier":
        wheel_z = builder.ground_clearance + 0.40 + 0.035
        wheel_y_positions = [(1.45, "F"), (-0.15, "M"), (-1.55, "R")]
        relay_wheel_locs = []
        for side in (-1, 1):
            tag = "L" if side < 0 else "R"
            for y_off, ytag in wheel_y_positions:
                w_loc = (side * 0.96, y_off, wheel_z)
                relay_wheel_locs.append(w_loc)
                builder.objects.extend(rmk.make_all_terrain_wheel_assembly(
                    f"{name}Wheel_{ytag}_{tag}",
                    location=w_loc,
                    radius=0.40, width=0.26,
                    tire_mat=builder.palette.tread, rim_mat=builder.palette.wheel_rim,
                    hub_mat=builder.palette.gunmetal, accent_mat=builder.palette.warning
                ))
        for y_off, ytag in wheel_y_positions:
            builder.objects.append(rmk.make_box(
                f"{name}Joint_Axle_{ytag}", (1.75, 0.14, 0.12), (0.0, y_off, wheel_z), builder.palette.gunmetal, 0.02
            ))
        builder.objects.extend([
            rmk.make_sloped_glacis_hull(
                f"{name}Armor_Chassis",
                length_front=1.75, length_rear=2.05,
                width_front=1.45, width_rear=1.55,
                height_lower=builder.ground_clearance + 0.35, height_upper=0.82,
                material=builder.palette.primary_armor, bevel=0.038
            ),
            rmk.make_box(
                f"{name}Armor_ForwardCab",
                (1.35, 1.15, 0.58), (0.0, 1.05, 1.12),
                builder.palette.light_armor, bevel=0.03,
                rotation=(math.radians(-12), 0.0, 0.0)
            ),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CabCollar", (0.0, 1.05, 0.83), (1.35, 1.15), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_box(
                f"{name}Armor_ElectronicsShelter",
                (1.48, 2.15, 0.72), (0.0, -0.65, 1.22),
                builder.palette.primary_armor, bevel=0.035
            ),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_ShelterCollar", (0.0, -0.65, 0.86), (1.48, 2.15), height=0.045, collar_mat=builder.palette.gunmetal
            ),
        ])
        builder.add_wheel_fenders(relay_wheel_locs, radius=0.40, width=0.26)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_turret(size=(1.25, 1.35, 0.40), center_y=0.15)
        builder.add_comms_mast(height=1.65, location=(-0.35, -1.25, 1.58))
        builder.add_sensor_visor()

    elif archetype == "amphibious_assault_tank":
        builder.add_continuous_tracks(
            length=3.6, height=0.66, belt_width=0.36, roadwheels=5, with_skirts=skirts
        )
        builder.add_final_drive_housings(radius=0.20)
        builder.objects.extend([
            rmk.make_sloped_glacis_hull(
                f"{name}Armor_AmphibHull",
                length_front=1.65, length_rear=1.55,
                width_front=1.75, width_rear=1.85,
                height_lower=builder.ground_clearance + 0.32, height_upper=0.88,
                material=builder.palette.primary_armor, bevel=0.04
            ),
            rmk.make_box(
                f"{name}Armor_BowProw",
                (1.65, 0.75, 0.45), (0.0, 1.75, builder.ground_clearance + 0.48),
                builder.palette.light_armor, bevel=0.035,
                rotation=(math.radians(-18), 0.0, 0.0)
            ),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_BowCollar", (0.0, 1.75, builder.ground_clearance + 0.25), (1.65, 0.75), height=0.045, collar_mat=builder.palette.gunmetal
            ),
        ])
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            jet_x = side * 0.72
            builder.objects.extend([
                rmk.make_cylinder(
                    f"{name}Waterjet_Tunnel_{stag}",
                    radius=0.18, depth=0.45,
                    location=(jet_x, -1.85, builder.ground_clearance + 0.28),
                    material=builder.palette.gunmetal,
                    vertices=12, bevel=0.015,
                    rotation=(math.radians(90), 0.0, 0.0)
                ),
                rmk.make_cylinder(
                    f"{name}Waterjet_Nozzle_{stag}",
                    radius=0.14, depth=0.12,
                    location=(jet_x, -2.10, builder.ground_clearance + 0.28),
                    material=builder.palette.wheel_rim,
                    vertices=10, bevel=0.008,
                    rotation=(math.radians(90), 0.0, 0.0)
                ),
            ])
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_splash_rib()
        builder.add_driver_periscope_hood()
        builder.add_glacis_weld_seams()
        builder.add_access_hatches()
        builder.add_turret(size=(1.55, 1.75, 0.46), center_y=0.08)
        builder.add_turret_ring_guard()
        builder.add_crew_cupola()
        builder.add_bustle_basket()
        builder.add_sensor_visor()
        builder.add_engine_deck(slat_count=3, with_exhausts=False)

    elif archetype == "plasma_mortar_strider":
        builder.add_sloped_hull(length=2.8, width=2.6, height=0.92)
        builder.add_deck_edge_weld_seams()
        builder.add_quad_crawler_legs(knee_height=1.20, reach_x=1.35, reach_y=1.15)
        builder.objects.extend([
            rmk.make_box(
                f"{name}Armor_CruciformCenter",
                (1.85, 1.85, 0.52), (0.0, 0.0, 1.25),
                builder.palette.primary_armor, bevel=0.04
            ),
            rmk.make_transitional_coaming_collar(
                f"{name}Armor_CenterCollar", (0.0, 0.0, 0.99), (1.85, 1.85), height=0.045, collar_mat=builder.palette.gunmetal
            ),
            rmk.make_box(
                f"{name}Armor_TurntablePedestal",
                (1.45, 1.45, 0.24), (0.0, 0.0, 1.55),
                builder.palette.gunmetal, bevel=0.025
            ),
        ])
        builder.add_turret(size=(1.60, 1.75, 0.52), center_y=0.0)
        builder.add_recoil_spade(width=2.1, depth=0.85, deployed=True, location=(0.0, -1.65, 0.45))

    elif archetype == "stealth_recon_skimmer":
        builder.add_hover_skimmer(float_height=0.35)
        builder.objects.extend([
            rmk.make_sloped_glacis_hull(
                f"{name}Armor_StealthGlacis",
                length_front=1.45, length_rear=1.35,
                width_front=1.55, width_rear=1.65,
                height_lower=builder.ground_clearance + 0.22, height_upper=0.55,
                material=builder.palette.primary_armor, bevel=0.035
            ),
            rmk.make_box(
                f"{name}Armor_StealthChine_L",
                (0.45, 2.20, 0.12), (-0.95, 0.0, 0.48),
                builder.palette.light_armor, bevel=0.02,
                rotation=(0.0, math.radians(-16), 0.0)
            ),
            rmk.make_box(
                f"{name}Armor_StealthChine_R",
                (0.45, 2.20, 0.12), (0.95, 0.0, 0.48),
                builder.palette.light_armor, bevel=0.02,
                rotation=(0.0, math.radians(16), 0.0)
            ),
            rmk.make_box(
                f"{name}Armor_StealthCanopy",
                (0.85, 1.15, 0.28), (0.0, 0.35, 0.68),
                builder.palette.gunmetal, bevel=0.025,
                rotation=(math.radians(-18), 0.0, 0.0)
            ),
        ])
        builder.add_turret(size=(1.10, 1.25, 0.32), center_y=0.0)
        builder.add_sensor_visor()

    else:  # standard tank
        builder.add_sloped_hull(length=3.0, width=2.4, height=0.88)
        builder.add_deck_edge_weld_seams()
        builder.add_continuous_tracks(length=2.85, height=0.62, belt_width=0.34, roadwheels=5, with_skirts=skirts)
        builder.add_final_drive_housings()
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_towing_shackles()
        builder.add_lifting_lugs()
        builder.add_interlocking_gussets()
        builder.add_splash_rib()
        builder.add_driver_periscope_hood()
        builder.add_access_hatches()
        builder.add_weld_seams([
            ((-1.10, 0.45, 0.65), (-0.95, 1.45, 0.35)),
            ((1.10, 0.45, 0.65), (0.95, 1.45, 0.35)),
        ])
        builder.add_turret(size=(1.4, 1.6, 0.45), center_y=0.08)
        builder.add_turret_ring_guard()
        builder.add_crew_cupola()
        builder.add_commander_sight()
        builder.add_bustle_basket()
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    # 2. Weapons
    if archetype not in ("arc_tank",):
        if weapon == "railgun":
            builder.add_railgun(rail_length=2.40, rail_height=0.28, rail_width=0.40, elevation_deg=8.0)
            builder.add_weapon_trunnions(spacing_x=0.56, bracket_width=0.14)
        elif weapon == "nanite_projector":
            builder.add_nanite_projector(head_radius=0.16, length=1.25, elevation_deg=6.0)
        elif weapon == "gravity_cannon":
            builder.add_gravity_cannon(barrel_length=2.25, bore_radius=0.14, ring_count=4, elevation_deg=5.0)
            builder.add_weapon_trunnions(spacing_x=0.58, bracket_width=0.14)
        elif weapon == "laser_beam":
            builder.add_beam_laser(barrel_length=2.10, caliber=0.08, elevation_deg=6.0)
            builder.add_weapon_trunnions(spacing_x=0.50, bracket_width=0.12)
        elif weapon == "vls_missiles":
            builder.add_vls_missile_array(rows=2, cols=4)
        elif weapon == "flamethrower":
            builder.add_flamethrower(caliber=0.088, length=1.45, elevation_deg=6.0)
        elif weapon == "emp_emitter":
            builder.add_emp_emitter(core_radius=0.14, length=1.55, elevation_deg=5.0)
        elif weapon == "quad_flak":
            builder.add_quad_flak(barrel_length=1.65, caliber_radius=0.038, elevation_deg=34.0)
            builder.add_weapon_trunnions(spacing_x=0.54, bracket_width=0.12)
        elif weapon == "plasma_mortar":
            builder.add_plasma_mortar(caliber=0.175, length=0.95, elevation_deg=42.0)
            builder.add_weapon_trunnions(spacing_x=0.60, bracket_width=0.15)
        elif weapon == "howitzer":
            builder.add_howitzer(caliber=0.135, length=1.15, elevation_deg=26.0)
            builder.add_weapon_trunnions(spacing_x=0.58, bracket_width=0.14)
        elif weapon == "dual_cannon":
            if archetype == "heavy_crane_mech":
                builder.add_dual_cannon(caliber=0.082, length=0.60, spacing=0.38, elevation_deg=11.0)
            else:
                builder.add_dual_cannon(caliber=0.082, length=1.65, spacing=0.38, elevation_deg=11.0)
                builder.add_weapon_trunnions(spacing_x=0.62, bracket_width=0.14)
        elif weapon == "gatling":
            if archetype in ("walker", "bike", "colossus_titan", "artillery_strider", "heavy_crane_mech"):
                gatling_offsets = {
                    "colossus_titan": (1.35, 3.25),
                    "heavy_crane_mech": (1.15, 2.85),
                    "artillery_strider": (0.95, 2.45),
                }
                p_x, p_z = gatling_offsets.get(archetype, (0.75, 1.25))
                builder.add_rotary_gatling(side=-1, pivot_offset=(p_x, 0.35, p_z))
                builder.add_rotary_gatling(side=1, pivot_offset=(p_x, 0.35, p_z))
            elif archetype in ("vtol", "heavy_dropship", "flying_wing_bomber"):
                builder.add_rotary_gatling(side=0, pivot_offset=(0.0, 1.25 if archetype != "heavy_dropship" else 2.15, 0.42 if archetype != "heavy_dropship" else 0.65))
            elif archetype in ("buggy", "halftrack", "shield_truck", "ew_jammer", "assault_hovercraft", "command_hq_rig"):
                builder.add_rotary_gatling(side=0, pivot_offset=(0.0, 0.25, 1.45))
            else:
                builder.add_rotary_gatling(side=1)
        elif weapon == "missile":
            if archetype in ("vtol", "heavy_dropship", "flying_wing_bomber"):
                for side in (-1, 1):
                    side_tag = "L" if side < 0 else "R"
                    pod = rmk.make_missile_launcher_pod(
                        f"{name}MissilePod_{side_tag}",
                        location=(side * (0.95 if archetype != "heavy_dropship" else 1.35), 0.35, 0.58 if archetype != "heavy_dropship" else 1.15),
                        size=(0.42, 0.75, 0.35),
                        rows=2, cols=2, elevation_deg=8.0,
                        armor_mat=builder.palette.primary_armor, cavity_mat=builder.palette.gunmetal,
                        warhead_mat=builder.palette.warning, accent_mat=builder.palette.warning
                    )
                    builder.objects.extend(pod)
                builder.gun_pivot = Vector((0.0, 1.10, 0.58 if archetype != "heavy_dropship" else 1.15))
                builder.gun_dir = Vector((0.0, 1.0, 0.0))
            else:
                builder.add_missile_pod(rows=2, cols=3, elevation_deg=16.0)
        elif weapon == "arc":
            builder.add_arc_emitter(core_radius=0.20, prongs=3)
        elif archetype == "sniper_tankette":
            # Long-barrel precision gun: thinner caliber, ~40% longer tube, flatter elevation.
            builder.add_cannon(caliber=0.072, length=2.45, elevation_deg=4.0)
            builder.add_weapon_trunnions(spacing_x=0.46, bracket_width=0.10)
        else:  # cannon
            builder.add_cannon(caliber=0.095, length=1.70, elevation_deg=11.0)
            builder.add_weapon_trunnions(spacing_x=0.52, bracket_width=0.12)

    # 3. Optional Subsystems & Greebles
    if rollcage and archetype not in ("buggy",):
        builder.add_rollcage()

    if quad_tracks and archetype not in ("super_heavy", "siege_crawler"):
        builder.add_quad_track_pods()

    if tilt_rotors and archetype not in ("vtol", "heavy_dropship"):
        builder.add_tiltrotors()

    if dozer and archetype in ("tank", "artillery", "halftrack", "super_heavy", "combat_harvester", "demolition_breacher", "thermobaric_tank", "recovery_crawler", "mine_layer", "bridge_layer", "mortar_carrier"):
        builder.add_dozer_blade(width=builder.hull_width * 1.05, height=0.44)

    if smoke and archetype in ("tank", "artillery", "hover", "spider", "halftrack", "super_heavy", "quad_mech", "flak_anti_air", "colossus_titan", "hover_cruiser", "shield_truck", "thermobaric_tank", "ew_jammer", "mobile_sam_launcher", "recovery_crawler", "mine_layer", "sniper_tankette", "hexapod_assault", "assault_hovercraft", "bridge_layer", "command_hq_rig", "mortar_carrier", "siege_drill_mole", "heavy_crane_mech", "nanite_support_rover", "gravity_siege_breaker", "tactical_relay_carrier", "amphibious_assault_tank", "plasma_mortar_strider", "stealth_recon_skimmer"):
        builder.add_smoke_launchers(tube_count=3)

    if radar:
        builder.add_radar_dish()

    if sponsons and archetype in ("tank", "artillery", "super_heavy", "siege_crawler", "combat_harvester", "hover_cruiser", "thermobaric_tank", "shield_truck", "recovery_crawler", "mine_layer", "assault_hovercraft", "bridge_layer", "command_hq_rig", "mortar_carrier", "siege_drill_mole"):
        builder.add_side_sponsons()

    if outriggers and archetype not in ("flak_anti_air", "siege_crawler"):
        builder.add_outriggers(count=4 if archetype in ("super_heavy", "siege_crawler", "colossus_titan", "command_hq_rig") else 2)

    if flight_deck and archetype not in ("drone_carrier",):
        builder.add_flight_deck()

    if excavator_drum and archetype not in ("combat_harvester",):
        builder.add_excavator_drum()

    if antenna and archetype not in ("colossus_titan", "hover_cruiser", "artillery_strider", "ew_jammer", "assault_hovercraft", "command_hq_rig", "heavy_crane_mech"):
        builder.add_whip_antenna()

    if cupola:
        builder.add_crew_cupola()

    if rivets:
        builder.add_rivets_and_welds()

    if track_shoes and archetype in ("tank", "artillery", "arc_tank", "halftrack", "super_heavy", "flak_anti_air", "drone_carrier", "combat_harvester", "demolition_breacher", "thermobaric_tank", "recovery_crawler", "mine_layer", "sniper_tankette", "bridge_layer", "mortar_carrier", "siege_drill_mole"):
        builder.add_track_shoes()

    if armor_plating:
        builder.add_armor_plating()

    if hydraulic_hoses:
        builder.add_hydraulic_hoses()

    if hull_greebles:
        builder.add_hull_greebles()

    if tactical_markings:
        builder.add_tactical_markings()

    if hydraulic_pistons:
        builder.add_hydraulic_pistons()

    if ammo_chutes:
        builder.add_ammo_belt_chutes()

    if aps_suite:
        builder.add_aps_hardkill_suite()

    if stowage_kit:
        builder.add_tactical_stowage()

    if slat_armor:
        builder.add_slat_armor()

    if era_bricks:
        builder.add_era_bricks()

    if camo_netting:
        builder.add_camo_netting()

    if thermal_radiators:
        builder.add_thermal_radiators()

    if rws_mount:
        builder.add_rws_mount()

    if drone_dock:
        builder.add_drone_dock()

    if recoil_spade:
        builder.add_recoil_spade()

    if barrel_shroud:
        builder.add_barrel_shroud()
    if nanite_dispenser and archetype != "nanite_support_rover":
        builder.add_nanite_dispenser()
    if grav_dampener and archetype != "gravity_siege_breaker":
        builder.add_grav_dampener()
    if comms_mast and archetype != "tactical_relay_carrier":
        builder.add_comms_mast()
    if ram_prow and archetype != "gravity_siege_breaker":
        builder.add_ram_prow()


    # 3. Modular Attachments
    if attachments:
        att_list = [a.strip() for a in attachments.split(",") if a.strip()]
        builder.add_attachments(att_list)

    if archetype == "heavy_crane_mech":
        # Position failure effects and movement audio on the dorsal reactor; the
        # muzzle audio follows the paired cannon center and foot contacts stay at Z=0.
        builder.damage_smoke_loc = Vector((0.0, -0.72, 2.68))
        builder.damage_fire_loc = Vector((0.0, -0.72, 2.50))
        builder.engine_audio_loc = Vector((0.0, -0.72, 2.68))
        builder.weapon_audio_loc = Vector(builder.gun_pivot)

    builder.apply_uniform_scale(model_scale)

    # 4. Build, Validate, and Export
    res = builder.build_and_export(
        fbx_path=fbx_file,
        blend_path=blend_file,
        preview_path=preview_file,
        card_path=card_path,
        render_multiview=multiview,
        ortho_scale=_calculate_ortho_scale(archetype),
        smooth_by_angle=True,
        smooth_angle_deg=smooth_angle,
        weighted_normals=weighted_normals,
        bake_vertex_dirt=dirt,
        include_locators=locators,
        generate_lods=lods,
        generate_colliders=colliders,
        wear_level=wear,
        soot_intensity=soot_intensity,
        gameplay_meta_path=gameplay_meta_path,
        wreckage_fbx_path=wreckage_fbx_path,
        damaged_fbx_path=damaged_fbx_path,
        target_budget=target_budget,
        include_vfx_flash=vfx_flash,
        include_vfx_thruster=vfx_thruster,
        include_shield_bubble=shield_bubble,
        include_rig=rig or ik_rig,
        include_ik=ik_rig,
        include_animations=animate,
        bake_atlas=bake_atlas or bake_normals or single_material or (camo and camo != "none"),
        atlas_resolution=atlas_resolution,
        single_material_atlas=single_material,
        export_detached_attachments=export_detached_attachments,
        render_icons=render_icons,
        icon_resolutions=(icon_size,),
        icon_elevation=icon_elevation,
        wreckage_severity=wreckage_severity,
        structural_wear_mask=structural_wear_mask,
        unity_batch_names=(archetype == "heavy_crane_mech"),
    )

    if lod_diagnostics:
        print("\n--- Low-Poly Geometry Budget & LOD Diagnostics ---")
        true_triangles = res.get("profile_report", {}).get("total_triangles", res["total_polygons"])
        print(f"LOD0 (Current): {res['total_polygons']} faces / {true_triangles} triangles | {res['total_vertices']} verts (RTS Combat Camera)")
        print(f"  Target Face Budget: 1,500 - 7,500 faces (Current: {round(res['total_polygons'] / max(1, target_budget) * 100, 1)}% of budget)")
        print("LOD Recommendations:")
        print(f"  LOD1 (~1,200 tris): Merge track cleats & road wheel rims into single cylinder silhouettes.")
        print(f"  LOD2 (~500 tris): Collapse greebles, antennas, and multi-barrel clusters into convex hulls.")
        print("---------------------------------------------------\n")

    print("\n========================================================")
    print(f"Generated Rogue Command Vehicle: {name}")
    print(f"  Archetype: {archetype} | Faction: {faction} | Camo: {camo} | Weapon: {weapon}")
    print(f"  Polygons: {res['total_polygons']} | Vertices: {res['total_vertices']}")
    print(f"  Batched Unity Meshes: {res['exported_batched_objects']}")
    print(f"  Ground Contact Valid: {res['ground_contact_valid']}")
    print(f"  Source Blend: {blend_file}")
    print(f"  Unity FBX: {fbx_file}")
    print(f"  Preview Image: {preview_file}")
    if card_path is not None:
        print(f"  Tactical Card Sprite: {card_path}")
    print("========================================================\n")

    return res


def build_structure_model(
    name: str,
    building_type: str = "factory",
    faction: str = "vanguard",
    camo: str = "none",
    wear: str = "field_tested",
    soot_intensity: float = 1.0,
    out_root: Path = REPO_ROOT,
    blend_path: Path | None = None,
    fbx_path: Path | None = None,
    preview_path: Path | None = None,
    primary_color: str | None = None,
    light_armor_color: str | None = None,
    emissive_color: str | None = None,
    lods: bool = False,
    colliders: bool = False,
    gameplay_json: bool = False,
    render_icons: bool = False,
    icon_size: int = 128,
    icon_elevation: float = 45.0,
    camo_scale: float = 1.0,
    camo_contrast: float = 1.0,
    normal_intensity: float = 1.0,
    panel_lines: bool = True,
    bake_atlas: bool = False,
    atlas_resolution: int = 1024,
    single_material: bool = False,
    hydraulic_hoses: bool = False,
    hull_greebles: bool = False,
    tactical_markings: bool = False,
    hydraulic_pistons: bool = False,
    ammo_chutes: bool = False,
    aps_suite: bool = False,
    stowage_kit: bool = False,
    slat_armor: bool = False,
    era_bricks: bool = False,
    camo_netting: bool = False,
    thermal_radiators: bool = False,
    rws_mount: bool = False,
    drone_dock: bool = False,
    recoil_spade: bool = False,
    barrel_shroud: bool = False,
    nanite_dispenser: bool = False,
    grav_dampener: bool = False,
    comms_mast: bool = False,
    ram_prow: bool = False,
    weighted_normals: bool = True,
    structural_wear_mask: bool = False,
) -> dict:
    rmk.clear_scene()

    blend_file = blend_path or (out_root / "content" / "source-art" / f"{name.lower()}.blend")
    fbx_file = fbx_path or (
        out_root
        / "unity"
        / "RogueCommandAssetAuthoring"
        / "Assets"
        / "RogueCommandExpansionSample"
        / "Models"
        / f"{name}.fbx"
    )
    preview_file = preview_path or (out_root / "artifacts" / "previews" / f"{name.lower()}.png")

    p_override = rmk.hex_to_rgb(primary_color) if primary_color else None
    l_override = rmk.hex_to_rgb(light_armor_color) if light_armor_color else None
    e_override = rmk.hex_to_rgb(emissive_color) if emissive_color else None

    builder = rmk.RogueStructureBuilder(
        name,
        building_type=building_type,
        faction=faction,
        camo=camo,
        primary_override=p_override,
        light_override=l_override,
        emissive_override=e_override,
        camo_scale=camo_scale,
        camo_contrast=camo_contrast,
        enable_normal_relief=panel_lines,
        normal_intensity=normal_intensity,
    )

    bt = building_type.lower()
    if bt == "factory":
        builder.build_factory()
    elif bt in ("defense_turret", "turret"):
        builder.build_defense_turret()
    elif bt in ("radar_station", "radar"):
        builder.build_radar_station()
    elif bt in ("energy_silo", "silo", "reactor"):
        builder.build_energy_silo()
    elif bt in ("orbital_silo", "orbital"):
        builder.build_orbital_silo()
    elif bt in ("shield_dome", "dome"):
        builder.build_shield_dome()
    elif bt in ("emp_obelisk", "obelisk"):
        builder.build_emp_obelisk()
    elif bt in ("bunker_pillbox", "pillbox", "bunker"):
        builder.build_bunker_pillbox()
    elif bt in ("anti_air_battery", "sam_battery", "aa_battery"):
        builder.build_anti_air_battery()
    elif bt in ("comms_relay_tower", "relay_tower", "comms_tower"):
        builder.build_comms_relay_tower()
    else:
        builder.build_factory()

    if hydraulic_hoses:
        builder.add_hydraulic_hoses()

    if hull_greebles:
        builder.add_hull_greebles()

    if tactical_markings:
        builder.add_tactical_markings()

    if hydraulic_pistons:
        builder.add_hydraulic_pistons()

    if ammo_chutes:
        builder.add_ammo_belt_chutes()

    if aps_suite:
        builder.add_aps_hardkill_suite()

    if stowage_kit:
        builder.add_tactical_stowage()

    if slat_armor:
        builder.add_slat_armor()

    if era_bricks:
        builder.add_era_bricks()

    if camo_netting:
        builder.add_camo_netting()

    if thermal_radiators:
        builder.add_thermal_radiators()

    if rws_mount:
        builder.add_rws_mount()

    if drone_dock:
        builder.add_drone_dock()

    if recoil_spade:
        builder.add_recoil_spade()

    if barrel_shroud:
        builder.add_barrel_shroud()
    if nanite_dispenser and archetype != "nanite_support_rover":
        builder.add_nanite_dispenser()
    if grav_dampener and archetype != "gravity_siege_breaker":
        builder.add_grav_dampener()
    if comms_mast and archetype != "tactical_relay_carrier":
        builder.add_comms_mast()
    if ram_prow and archetype != "gravity_siege_breaker":
        builder.add_ram_prow()


    gameplay_meta_path = (fbx_file.parent / f"{name}.gameplay.json") if gameplay_json else None

    res = builder.build_and_export(
        fbx_path=fbx_file,
        blend_path=blend_file,
        preview_path=preview_file,
        generate_lods=lods,
        generate_colliders=colliders,
        gameplay_meta_path=gameplay_meta_path,
        render_icons=render_icons,
        icon_resolutions=(icon_size,),
        icon_elevation=icon_elevation,
        bake_atlas=bake_atlas or single_material or (camo and camo != "none"),
        atlas_resolution=atlas_resolution,
        single_material_atlas=single_material,
        weighted_normals=weighted_normals,
        wear_level=wear,
        soot_intensity=soot_intensity,
        structural_wear_mask=structural_wear_mask,
    )

    print("\n========================================================")
    print(f"Generated Rogue Command Structure: {name}")
    print(f"  Building Type: {building_type} | Faction: {faction} | Camo: {camo}")
    print(f"  Polygons: {res['total_polygons']} | Vertices: {res['total_vertices']}")
    print(f"  Batched Unity Meshes: {res['exported_batched_objects']}")
    print(f"  Ground Contact Valid: {res['ground_contact_valid']}")
    print(f"  Source Blend: {blend_file}")
    print(f"  Unity FBX: {fbx_file}")
    print(f"  Preview Image: {preview_file}")
    print("========================================================\n")

    return res


def main():
    args = parse_args()
    _validate_in_range("camo-scale", args.camo_scale, 0.1, 10.0)
    _validate_in_range("camo-contrast", args.camo_contrast, 0.0, 4.0)
    _validate_in_range("normal-intensity", args.normal_intensity, 0.0, 4.0)
    _validate_in_range("soot-intensity", args.soot_intensity, 0.0, 2.0)
    _validate_in_range("model-scale", args.model_scale, 0.1, 10.0)
    out_root = resolve_path(args.out_dir, REPO_ROOT) or REPO_ROOT

    if args.dry_run:
        if args.structure:
            print("\n=== DRY RUN: Rogue Command Structure Planning ===")
            print(f"Structure Name: {args.name}")
            print(f"Building Type: {args.building_type}")
            structure_camo = args.camo if args.camo != "none" else args.biome
            print(f"Faction: {args.faction} | Palette: {structure_camo} | Wear: {args.wear} | Soot: {args.soot_intensity}")
            print(f"Surface Detail: camo scale={args.camo_scale}, camo contrast={args.camo_contrast}, panel relief={args.panel_lines}, normal intensity={args.normal_intensity}")
            print("Outputs Planned:")
            print(f"  FBX: {args.fbx_path or (out_root / 'unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models' / f'{args.name}.fbx')}")
            print(f"  Blend: {args.blend_path or (out_root / 'content/source-art' / f'{args.name.lower()}.blend')}")
            print(f"  Preview: {args.preview_path or (out_root / 'artifacts/previews' / f'{args.name.lower()}.png')}")
            if args.gameplay_json:
                print(f"  Gameplay JSON: {args.name}.gameplay.json (visualRootPath: 'Building')")
            if args.lods:
                print(f"  LOD FBX: {args.name}_LODs.fbx")
            if args.colliders:
                print(f"  Colliders: {args.name}_COLLIDER_Base, {args.name}_COLLIDER_Roof")
            if args.render_icons:
                print(f"  Tactical Icons: {args.icon_size}x{args.icon_size} Top-Down and Isometric")
            print(f"  Authoring Details: HydraulicHoses={args.hydraulic_hoses}, HullGreebles={args.hull_greebles}, TacticalMarkings={args.tactical_markings}, HydraulicPistons={args.hydraulic_pistons}, AmmoChutes={args.ammo_chutes}, APSSuite={args.aps_suite}, StowageKit={args.stowage_kit}, SlatArmor={args.slat_armor}, ERABricks={args.era_bricks}, CamoNetting={args.camo_netting}, ThermalRadiators={args.thermal_radiators}, RWSMount={args.rws_mount}, DroneDock={args.drone_dock}, RecoilSpade={args.recoil_spade}, BarrelShroud={args.barrel_shroud}, NaniteDispenser={args.nanite_dispenser}, GravDampener={args.grav_dampener}, CommsMast={args.comms_mast}, RamProw={args.ram_prow}, WeightedNormals={args.weighted_normals}")
            if args.bake_atlas or args.bake_normals or args.single_material or structure_camo != "none":
                print(f"  Texture Atlas: {args.atlas_resolution}x{args.atlas_resolution} PBR (Albedo, MetallicGloss, Normal, Emission)")
            if args.single_material:
                print(f"  Single-material Atlas FBX: {args.name}_Atlas.fbx (one material slot; Unity draw-call count depends on prefab selection and rendering)")
            print("=== Dry run complete. No meshes created. ===\n")
            return

        if args.batch_all_archetypes:
            print("\n========================================================")
            print(f" DRY RUN: BATCH PLANNING ALL {len(ALL_ARCHETYPES)} ARCHETYPES")
            print("========================================================\n")
            for arch in ALL_ARCHETYPES:
                arch_title = arch.title().replace("_", "")
                unit_name = f"{args.name}_{arch_title}"
                def_weapon = ARCHETYPE_DEFAULT_WEAPONS.get(arch, "cannon") if args.weapon == "cannon" else args.weapon
                is_walker = arch in ("walker", "colossus_titan", "artillery_strider", "heavy_crane_mech")
                is_quad = arch in ("spider", "quad_mech", "siege_crawler", "hexapod_assault", "plasma_mortar_strider")
                rig_desc = "Biped Armature (8 bones)" if is_walker else ("Quad Armature (10 bones)" if is_quad else "Standard Rig (6 bones)")
                print(f"- {arch:<20} -> {unit_name:<30} | Weapon: {def_weapon:<14} | Rig: {rig_desc}")
            print("\n=== Dry run complete. No meshes created. ===\n")
            return

        print("\n=== DRY RUN: Rogue Command Vehicle Planning ===")
        print(f"Unit Name: {args.name}")
        print(f"Archetype: {args.archetype}")
        vehicle_camo = args.camo if args.camo != "none" else args.biome
        print(f"Faction: {args.faction} | Palette: {vehicle_camo} | Wear: {args.wear} | Soot: {args.soot_intensity}")
        print(f"Surface Detail: camo scale={args.camo_scale}, camo contrast={args.camo_contrast}, panel relief={args.panel_lines}, normal intensity={args.normal_intensity}")
        print(f"Model Scale: {args.model_scale}")
        print(f"RTS Polygon Budget Variant: {args.rts_budget}")
        print(f"Primary Weapon: {args.weapon}")
        print(f"Subsystems: Skirts={args.skirts}, Rollcage={args.rollcage}, QuadTracks={args.quad_tracks}, TiltRotors={args.tilt_rotors}, Dozer={args.dozer}, Smoke={args.smoke}, Radar={args.radar}, Sponsons={args.sponsons}, Outriggers={args.outriggers}, FlightDeck={args.flight_deck}, ExcavatorDrum={args.excavator_drum}")
        print(f"Micro-Details: Antenna={args.antenna}, Cupola={args.cupola}, Rivets={args.rivets}, TrackShoes={args.track_shoes}, SuspensionArms={args.suspension_arms}, MantletBoot={args.mantlet_boot}, SpacedSkirts={args.spaced_skirts}")
        print(f"Authoring Details: HydraulicHoses={args.hydraulic_hoses}, HullGreebles={args.hull_greebles}, TacticalMarkings={args.tactical_markings}, HydraulicPistons={args.hydraulic_pistons}, AmmoChutes={args.ammo_chutes}, APSSuite={args.aps_suite}, StowageKit={args.stowage_kit}, SlatArmor={args.slat_armor}, ERABricks={args.era_bricks}, CamoNetting={args.camo_netting}, ThermalRadiators={args.thermal_radiators}, RWSMount={args.rws_mount}, DroneDock={args.drone_dock}, RecoilSpade={args.recoil_spade}, BarrelShroud={args.barrel_shroud}, NaniteDispenser={args.nanite_dispenser}, GravDampener={args.grav_dampener}, CommsMast={args.comms_mast}, RamProw={args.ram_prow}, WeightedNormals={args.weighted_normals}")
        if args.attachments:
            print(f"Modular Attachments: {args.attachments} (Detached FBX: {args.export_detached_attachments})")
        if args.primary_color:
            print(f"Custom Primary Color: {args.primary_color}")
        if args.light_armor_color:
            print(f"Custom Light Armor Color: {args.light_armor_color}")
        if args.emissive_color:
            print(f"Custom Emissive Color: {args.emissive_color}")
        print("Outputs Planned:")
        print(f"  FBX: {args.fbx_path or (out_root / 'unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models' / f'{args.name}.fbx')}")
        print(f"  Blend: {args.blend_path or (out_root / 'content/source-art' / f'{args.name.lower()}.blend')}")
        print(f"  Preview: {args.preview_path or (out_root / 'artifacts/previews' / f'{args.name.lower()}.png')}")
        if args.gameplay_json:
            print(f"  Gameplay JSON: {args.name}.gameplay.json")
        if args.lods:
            print(f"  LOD FBX: {args.name}_LODs.fbx")
        if args.colliders:
            print(f"  Colliders: {args.name}_COLLIDER_Hull, {args.name}_COLLIDER_Turret")
        if args.wreckage:
            print(f"  Wreckage FBX: {args.name}_Wreckage.fbx (Severity: {args.wreckage_severity})")
        if args.damaged_state:
            print(f"  Damaged State FBX: {args.name}_Damaged.fbx")
        atlas_requested = args.bake_atlas or args.bake_normals or args.single_material or vehicle_camo != "none"
        if atlas_requested:
            print(f"  Texture Atlas: {args.atlas_resolution}x{args.atlas_resolution} PBR (Albedo, MetallicGloss, Normal, Emission)")
        if args.single_material:
            print(f"  Single-material Atlas FBX: {args.name}_Atlas.fbx (one material slot; Unity draw-call count depends on prefab selection and rendering)")
        if args.rig:
            if args.archetype in ("walker", "colossus_titan", "artillery_strider", "heavy_crane_mech"):
                bones_desc = "Bone_Root, Bone_Pelvis, Bone_Thigh_L/R, Bone_Shin_L/R, Bone_Foot_L/R, Bone_TurretYaw, Bone_TurretPitch, Bone_BarrelRecoil"
            elif args.archetype in ("spider", "quad_mech", "siege_crawler", "hexapod_assault"):
                bones_desc = "Bone_Root, Bone_Hull, Bone_Pelvis, Bone_Thigh_FL/FR/RL/RR, Bone_Shin_*, Bone_Foot_*, Bone_TurretYaw, Bone_TurretPitch, Bone_BarrelRecoil"
            else:
                bones_desc = "Bone_Root, Bone_Hull, Bone_Pelvis, Bone_TurretYaw, Bone_TurretPitch, Bone_BarrelRecoil"
            print(f"  Armature Rig: {args.name}_Armature ({bones_desc})")
        if args.animate:
            walk_desc = "Action_Walk (In-Place 40-frame gait), Action_Drive (suspension)" if args.archetype in ("walker", "colossus_titan", "artillery_strider", "heavy_crane_mech", "spider", "quad_mech", "siege_crawler", "hexapod_assault") else "Action_Drive, Action_Walk"
            print(f"  Baked Action Clips: Action_Idle, {walk_desc}, Action_FireRecoil, Action_Death")
        if args.vfx_flash:
            print(f"  VFX: Muzzle flash star-spike")
        if args.vfx_thruster:
            print(f"  VFX: Exhaust thruster plume cones")
        if args.shield_bubble:
            print(f"  VFX: Geodesic shield bubble envelope")
        if args.track_sag is not None:
            print(f"  Track Sag: {args.track_sag}m")
        if args.armor_plating:
            print("  Armor Plating: Procedural BMesh with recessed gutters")
        if args.ik_rig:
            print("  IK Rigging: Automated 2-bone IK constraints on leg chains")
        if args.render_icons:
            print(f"  Tactical Icons: {args.icon_size}x{args.icon_size} Top-Down and Isometric")
        print("=== Dry run complete. No meshes created. ===\n")
        return

    if args.batch_all_archetypes:
        print("\n========================================================")
        print(f" BATCH GENERATING ALL {len(ALL_ARCHETYPES)} ROGUE COMMAND ARCHETYPES")
        print("========================================================\n")
        results = []
        for arch in ALL_ARCHETYPES:
            arch_title = arch.title().replace("_", "")
            unit_name = f"{args.name}_{arch_title}"
            def_weapon = ARCHETYPE_DEFAULT_WEAPONS.get(arch, "cannon") if args.weapon == "cannon" else args.weapon
            print(f"\n[Batch {len(results)+1}/{len(ALL_ARCHETYPES)}] Building archetype: {arch} -> {unit_name} (Weapon: {def_weapon})")
            res = build_vehicle_model(
                name=unit_name,
                archetype=arch,
                faction=args.faction,
                camo=args.camo,
                weapon=def_weapon,
                wear=args.wear,
                out_root=out_root,
                soot_intensity=args.soot_intensity,
                skirts=args.skirts,
                rollcage=args.rollcage,
                quad_tracks=args.quad_tracks,
                tilt_rotors=args.tilt_rotors,
                dozer=args.dozer,
                smoke=args.smoke,
                radar=args.radar,
                sponsons=args.sponsons,
                outriggers=args.outriggers,
                flight_deck=args.flight_deck,
                excavator_drum=args.excavator_drum,
                antenna=args.antenna,
                cupola=args.cupola,
                rivets=args.rivets,
                track_shoes=args.track_shoes,
                biome=args.biome,
                primary_color=args.primary_color,
                light_armor_color=args.light_armor_color,
                emissive_color=args.emissive_color,
                locators=args.locators,
                dirt=args.dirt,
                lods=args.lods,
                colliders=args.colliders,
                gameplay_json=args.gameplay_json,
                wreckage=args.wreckage,
                target_budget=args.target_budget,
                lod_diagnostics=args.lod,
                smooth_angle=args.smooth_angle,
                multiview=args.multiview,
                camo_scale=args.camo_scale,
                camo_contrast=args.camo_contrast,
                normal_intensity=args.normal_intensity,
                panel_lines=args.panel_lines,
                bake_normals=args.bake_normals,
                bake_atlas=args.bake_atlas,
                atlas_resolution=args.atlas_resolution,
                single_material=args.single_material,
                track_sag=args.track_sag,
                armor_plating=args.armor_plating,
                ik_rig=args.ik_rig,
                suspension_arms=args.suspension_arms,
                mantlet_boot=args.mantlet_boot,
                spaced_skirts=args.spaced_skirts,
                hydraulic_hoses=args.hydraulic_hoses,
                hull_greebles=args.hull_greebles,
                tactical_markings=args.tactical_markings,
                hydraulic_pistons=args.hydraulic_pistons,
                ammo_chutes=args.ammo_chutes,
                aps_suite=args.aps_suite,
                stowage_kit=args.stowage_kit,
                slat_armor=args.slat_armor,
                era_bricks=args.era_bricks,
                camo_netting=args.camo_netting,
                thermal_radiators=args.thermal_radiators,
                rws_mount=args.rws_mount,
                drone_dock=args.drone_dock,
                recoil_spade=args.recoil_spade,
                barrel_shroud=args.barrel_shroud,
                weighted_normals=args.weighted_normals,
                wreckage_severity=args.wreckage_severity,
            )
            results.append({
                "archetype": arch,
                "name": unit_name,
                "faces": res["total_polygons"],
                "triangles": res.get("profile_report", {}).get("total_triangles", res["total_polygons"]),
                "draw_calls": res["exported_batched_objects"],
                "valid": res["ground_contact_valid"],
            })

        print("\n========================================================")
        print(f" BATCH GENERATION COMPLETE - {len(ALL_ARCHETYPES)} ARCHETYPES REPORT")
        print("========================================================")
        print(f"{'Archetype':<18} | {'Unit Name':<28} | {'Faces':<7} | {'Triangles':<10} | {'DrawCalls':<9} | {'GroundValid'}")
        print("-" * 92)
        for r in results:
            print(f"{r['archetype']:<18} | {r['name']:<28} | {r['faces']:<7} | {r['triangles']:<10} | {r['draw_calls']:<9} | {r['valid']}")
        print("========================================================\n")
        return

    # Single unit/structure generation
    blend_path = resolve_path(args.blend_path, REPO_ROOT)
    fbx_path = resolve_path(args.fbx_path, REPO_ROOT)
    preview_path = resolve_path(args.preview_path, REPO_ROOT)
    card_path = resolve_path(args.card_path, REPO_ROOT)

    # Execution: Structure or Vehicle
    if args.structure:
        structure_camo = args.camo if args.camo != "none" else args.biome
        res = build_structure_model(
            name=args.name,
            building_type=args.building_type,
            faction=args.faction,
            camo=structure_camo,
            out_root=out_root,
            blend_path=blend_path,
            fbx_path=fbx_path,
            preview_path=preview_path,
            wear=args.wear,
            soot_intensity=args.soot_intensity,
            primary_color=args.primary_color,
            light_armor_color=args.light_armor_color,
            emissive_color=args.emissive_color,
            lods=args.lods,
            colliders=args.colliders,
            gameplay_json=args.gameplay_json,
            render_icons=args.render_icons,
            icon_size=args.icon_size,
            icon_elevation=args.icon_elevation,
            camo_scale=args.camo_scale,
            camo_contrast=args.camo_contrast,
            normal_intensity=args.normal_intensity,
            panel_lines=args.panel_lines,
            bake_atlas=args.bake_atlas or args.bake_normals,
            atlas_resolution=args.atlas_resolution,
            single_material=args.single_material,
            hydraulic_hoses=args.hydraulic_hoses,
            hull_greebles=args.hull_greebles,
            tactical_markings=args.tactical_markings,
            hydraulic_pistons=args.hydraulic_pistons,
            ammo_chutes=args.ammo_chutes,
            aps_suite=args.aps_suite,
            stowage_kit=args.stowage_kit,
            slat_armor=args.slat_armor,
            era_bricks=args.era_bricks,
            camo_netting=args.camo_netting,
            thermal_radiators=args.thermal_radiators,
            rws_mount=args.rws_mount,
            drone_dock=args.drone_dock,
            recoil_spade=args.recoil_spade,
            barrel_shroud=args.barrel_shroud,
            weighted_normals=args.weighted_normals,
            structural_wear_mask=args.structural_wear_mask,
        )
    else:
        res = build_vehicle_model(
            name=args.name,
            archetype=args.archetype,
            faction=args.faction,
            camo=args.camo,
            weapon=args.weapon,
            wear=args.wear,
            out_root=out_root,
            soot_intensity=args.soot_intensity,
            blend_path=blend_path,
            fbx_path=fbx_path,
            preview_path=preview_path,
            skirts=args.skirts,
            rollcage=args.rollcage,
            quad_tracks=args.quad_tracks,
            tilt_rotors=args.tilt_rotors,
            dozer=args.dozer,
            smoke=args.smoke,
            radar=args.radar,
            sponsons=args.sponsons,
            outriggers=args.outriggers,
            flight_deck=args.flight_deck,
            excavator_drum=args.excavator_drum,
            antenna=args.antenna,
            cupola=args.cupola,
            rivets=args.rivets,
            track_shoes=args.track_shoes,
            biome=args.biome,
            primary_color=args.primary_color,
            light_armor_color=args.light_armor_color,
            emissive_color=args.emissive_color,
            locators=args.locators,
            dirt=args.dirt,
            lods=args.lods,
            colliders=args.colliders,
            gameplay_json=args.gameplay_json,
            wreckage=args.wreckage,
            target_budget=args.target_budget,
            lod_diagnostics=args.lod,
            smooth_angle=args.smooth_angle,
            multiview=args.multiview,
            bake_atlas=args.bake_atlas,
            bake_normals=args.bake_normals,
            camo_scale=args.camo_scale,
            camo_contrast=args.camo_contrast,
            normal_intensity=args.normal_intensity,
            panel_lines=args.panel_lines,
            atlas_resolution=args.atlas_resolution,
            single_material=args.single_material,
            rig=args.rig,
            animate=args.animate,
            vfx_flash=args.vfx_flash,
            vfx_thruster=args.vfx_thruster,
            shield_bubble=args.shield_bubble,
            attachments=args.attachments,
            export_detached_attachments=args.export_detached_attachments,
            damaged_state=args.damaged_state,
            render_icons=args.render_icons,
            icon_size=args.icon_size,
            icon_elevation=args.icon_elevation,
            track_sag=args.track_sag,
            armor_plating=args.armor_plating,
            ik_rig=args.ik_rig,
            suspension_arms=args.suspension_arms,
            mantlet_boot=args.mantlet_boot,
            spaced_skirts=args.spaced_skirts,
            hydraulic_hoses=args.hydraulic_hoses,
            hull_greebles=args.hull_greebles,
            tactical_markings=args.tactical_markings,
            hydraulic_pistons=args.hydraulic_pistons,
            ammo_chutes=args.ammo_chutes,
            aps_suite=args.aps_suite,
            stowage_kit=args.stowage_kit,
            slat_armor=args.slat_armor,
            era_bricks=args.era_bricks,
            camo_netting=args.camo_netting,
            thermal_radiators=args.thermal_radiators,
            rws_mount=args.rws_mount,
            drone_dock=args.drone_dock,
            recoil_spade=args.recoil_spade,
            barrel_shroud=args.barrel_shroud,
            weighted_normals=args.weighted_normals,
            wreckage_severity=args.wreckage_severity,
            model_scale=args.model_scale,
            card_path=card_path,
            structural_wear_mask=args.structural_wear_mask or args.archetype == "heavy_crane_mech",
            rts_budget=args.rts_budget,
        )

    if args.report_json:
        report_path = resolve_path(args.report_json, REPO_ROOT)
        report_data = {
            "name": args.name,
            "archetype": args.archetype,
            "faction": args.faction,
            "camo": args.camo,
            "weapon": args.weapon,
            "wear_level": args.wear,
            "model_scale": args.model_scale,
            "weighted_normals": res.get("weighted_normals", True),
            "total_faces": res["total_polygons"],
            "total_triangles": res.get("profile_report", {}).get("total_triangles", res["total_polygons"]),
            "total_vertices": res["total_vertices"],
            "predicted_draw_calls": res["exported_batched_objects"],
            "ground_contact_valid": res["ground_contact_valid"],
            "subsystem_breakdown": res.get("profile_report", {}).get("subsystems", {}),
            "profile_report": res.get("profile_report", {}),
            "lod_levels": res.get("lod_levels", {}),
            "colliders": res.get("colliders", []),
            "budget_diagnostics": res.get("budget_diagnostics", {}),
        }
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report_data, indent=2), encoding="utf-8")
        print(f"  Structural Report JSON: {report_path}")


if __name__ == "__main__":
    main()
