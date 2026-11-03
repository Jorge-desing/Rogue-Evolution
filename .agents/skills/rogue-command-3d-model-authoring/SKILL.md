---
name: rogue-command-3d-model-authoring
description: "Design, create, or refine original 3D vehicle and unit visuals for Rogue Command with Blender and the pinned Unity authoring project. Use alongside unit and AssetBundle skills when the task changes a model or its in-game appearance."
---

# Rogue Command 3D Model Authoring

Use this skill to design, model, texture, and export original, high-readability 3D vehicle models and visual overlays for Rogue Command. It guides the visual pipeline—from a deterministic Blender 4.5.14 LTS blockout through mechanically plausible hard-surface refinement, texture authoring, Unity 2021.3.45f2 AssetBundle authoring, material assignment, ImageGen card handoff, and multi-tier verification.

---

## Verified Implementation Boundaries

- **Supported CLI Coverage**: 42 mobile archetypes, 10 strategic structure types, 56 presets, and 15 weapon choices. Hard-surface realism Waves 1–6 provide V-shaped splash ribs, turret bustle baskets, panoramic CITV sights, recovery winches, auxiliary fuel drums, and exhaust deflector cowls. `--track-sag`, `--armor-plating`, and `--ik-rig` provide parametric droop, BMesh panels, and 2-bone IK solver constraints. `--hydraulic-hoses`, `--hull-greebles`, `--tactical-markings`, `--hydraulic-pistons`, `--ammo-chutes`, `--aps-suite`, `--stowage-kit`, `--slat-armor`, `--era-bricks`, `--camo-netting`, `--thermal-radiators`, `--rws-mount`, `--drone-dock`, `--recoil-spade`, and `--barrel-shroud` add optional conduits, deck details, raycasted markings, telescopic actuators, ammunition feed chutes, active protection pods, field stowage, anti-HEAT slat cage grids, staggered ERA wedge bricks, 3D noise-displaced camouflage netting, glowing thermal radiator banks, roof RWS autocannon mounts, parasitic drone docks, grounded recoil earth spades, ventilated barrel shrouds, bilateral nanite repair pods, gyroscopic grav-dampener hubs, telescopic comms & EW masts, and heavy V-prow ram cowcatchers to mobile units and structures. Weighted normals are enabled by default and can be disabled with `--no-weighted-normals`. `--lod` reports polygon budgets; `--lods` builds the LOD chain.
- **Node Patterns & Coordinates**: `--camo` uses procedural Object-coordinate nodes (Voronoi for `splinter`/`digital`, wave for `tigerstripe`, noise for `desert`/`urban`/`arctic`/`woodland`).
- **Biome & Shaders**: `--biome` selects an authoring-time palette alias when `--camo` is `none` (no runtime adaptation). Atlas textures require baking and URP `VehiclePBR_VertexWear` shader.
- **References**: Follow [Art Style & Palette](references/art-style-and-palette.md) for camera-first shape and material rules, [Design Guides Adapted](references/design-guides-adapted.md) for source-backed design checks, [Blender Procedural Techniques](references/blender-procedural-techniques.md) for the refinement sequence and recipes, [Unity Material Pipeline](references/unity-material-pipeline.md), [Native Hierarchy & Pivots](references/native-hierarchy-and-pivots.md), and [3D-MODEL-AUTHORING-ENHANCEMENTS.md](../../../docs/research/3D-MODEL-AUTHORING-ENHANCEMENTS.md).
- **Texture workflow routing**: Use [PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md) for map creation and optional ArmorPaint/Material Maker passes; use [PBR Texture Validation](../rogue-command-pbr-texture-validation/SKILL.md) to check channels, effective Unity import settings, and mesh vertex colors. The shared integration steps are in [PBR-TEXTURE-WORKFLOW.md](../../../docs/research/PBR-TEXTURE-WORKFLOW.md).


## 1. Preserve and Use the Established Authoring Path

Before making any changes, review the project rules and handoffs:
- Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `unity/RogueCommandAssetAuthoring/README.md`.
- Use `rogue-command-unit-authoring` for gameplay balance and native templates, `rogue-command-assetbundle-authoring` for Unity bundle creation and import checks, `rogue-command-pack-validation` for manifest references, and `rogue-command-game-research` for pinned identity/signature inspection. Use `rogue-command-steam-runtime-validation` when the claim requires in-game evidence; follow its reversible deployment and cleanup steps. The local BepInEx MCP skill at `.tools/BepInExMCP/skills/bepinex-mcp/SKILL.md` applies when that bridge is available.
- **Authoring Project Preservation**: Work only inside `unity/RogueCommandAssetAuthoring/`. Preserve `ProjectSettings`, package manifests (`Packages/manifest.json`), Plastic/Unity Version Control metadata, and Unity Cloud configuration. Target **Unity 2021.3.45f2** (Mono, Windows Standalone x64). The user's separate Unity Hub project is not the mod authoring project.
- **Directory Invariants**:
  - Editable Blender sources: `content/source-art/*.blend`
  - Deterministic Python build scripts: `tools/Build-*.py`
  - Unity FBX models: `unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/*.fbx`
  - Visual prefabs and materials: `unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/`
  - High-resolution render previews and temporary diagnostics: ignored `artifacts/previews/` and `artifacts/`
  - Distributable bundles: `content/packs/<pack-id>/bundles/*.bundle`
  - The skill copies of `RogueModelKit.py` and `generate_unit_model.py` must remain byte-identical to their `tools/` sources.
- **Zero Remote Dependencies**: The local Blender executable (`.tools/Blender-4.5.14/.../blender.exe`) and deterministic Python scripts (`bpy`) are the supported authoring tools. Do not require Blender MCP or external 3D subscription services.

---

## 2. Rogue Command Art Direction & Silhouette Design

Aim for **grounded, stylized military sci-fi realism**: each vehicle should read clearly at RTS distance while its visible forms suggest plausible armor, fabrication, propulsion, and maintenance. Keep the project's faction palette and gameplay readability, but do not treat a final model as a pile of beveled cubes or a collection of disconnected primitives. Procedural assemblies are a starting block; refine the visible shell and transitions before export.

### Form-Realism Rules
- **Build a coherent primary shell.** Establish the hull, cabin, turret, or chassis from tapered profiles, compound slopes, rounded shoulders, and recessed wells where the silhouette benefits. Use boxes and cylinders for hidden structure or genuinely box-shaped components, not as the default visible shape for every subsystem.
- **Make parts look assembled.** Give armor plates believable thickness, overlap, attachment lips, and narrow shadow gaps. Seat turrets on rings, wheels in fenders, optics behind protective brows, and guns in mantlets with room for recoil. Avoid coplanar layers, floating panels, and intersections that make the construction look toy-like.
- **Use curvature selectively.** Round or smoothly transition large cast housings, turret cheeks, wheel shoulders, mantlets, engine covers, and sensor shells. Keep planar armor planar when it serves the design. Vary cross-sections and taper long parts; do not make every panel a rectangular slab or every barrel a plain cylinder.
- **Keep detail functional and restrained.** Add seams, fasteners, handles, vents, hoses, and stowage only where a crew, service task, heat path, or mechanical connection explains them. Prefer a few readable details over repeated greeble patterns. Introduce asymmetry only when a system explains it (for example, an offset exhaust, access side, or ammo feed).
- **Judge shape before surface noise.** Review a neutral clay render with grazing light, then a flat black silhouette and the faction materials. Fix faceting, pinching, uniform edge treatment, and primitive-looking joins in the geometry; do not hide them with camouflage, vertex dirt, or extra decals.

### The 3-Tier Readability Pyramid
- **Tier 1 (Macro Silhouette, priority ~65%)**: Proportion, stance, ground clearance, and the largest hull/turret masses. Must remain identifiable at RTS elevation; the 250ms glance is a local review heuristic.
- **Tier 2 (Meso Mechanical Construction, priority ~25%)**: Coherent track or suspension assemblies, hull transitions, armor overlaps, weapon mounts, cooling, and access systems.
- **Tier 3 (Micro Accents, priority ~10%)**: Optics, fasteners, seams, markings, and localized wear. These must not substitute for a well-resolved primary form.

The 65/25/10 split is a local visual-priority heuristic, not a measured requirement from the cited design sources.

### Core Visual Checks
1. **The Silhouette Test**: Review a solid-black model from the elevated gameplay camera and the major front/side/top views, without material or lighting cues. At the project's roughly 50-pixel review size, the archetype, role, and heading must remain recognizable; the pixel size and 250ms glance target are local heuristics, not external standards. Refine proportions and the outer contour before adding details.
2. **Scale-Aware Edge Treatment**: Do not bevel every edge uniformly. Use a narrow chamfer on a deliberately crisp armor edge and a multi-segment radius on a rounded or rolled form when it affects the silhouette or highlight. Choose widths in proportion to the part; preserve broad, clean planes and avoid oversized toy-like bevels.
3. **Continuous Track Contour**: Avoid flat box treads with floating blocks. Model tracks with sloped front approach angles (~25°–35° climb), round return idlers, and individual tread shoes (`0.15m–0.22m` spacing) along the ground and return runs. Show enough wheel, suspension, and fender depth to explain how the track is carried.
4. **Integrated Functional Details**: Recess lenses inside protective hoods, connect visible hoses to endpoints, and show plausible supports for antennas, weapon mounts, exhausts, and external stores. Activate Unity's `_EMISSION` keyword only for actual light sources or energized elements.
5. **Two-Scale Review**: Inspect a clay turntable from front, side, top, and the elevated gameplay view, then inspect the unit around 50 pixels tall. Correct silhouette tangencies and shading pinches at close range without adding details that disappear or alias at gameplay scale. See [Design Guides Adapted](references/design-guides-adapted.md) for the source-backed review sequence and the limits of each check.

*See detailed breakdown in [art-style-and-palette.md](references/art-style-and-palette.md).*

---

## 3. The 5-Tone Material Palette Standard

Do not use random palettes or noisy textures. Every vehicle must follow the disciplined 5-tone structural palette:

| Material Role | Proportion | Surface Description | Metallic | Smoothness | Emission |
|---|---|---|---|---|---|
| **Primary Armor** | 40–45% | Main hull plates, sponsons, lower chassis | 0.35–0.45 | 0.60–0.65 | None |
| **Light Armor** | 20–25% | Turret cheeks, upper deck, hatch covers | 0.30–0.35 | 0.58–0.62 | None |
| **Mechanical Gunmetal** | 15–20% | Cannon barrels, roll bars, suspension, axles | 0.74–0.80 | 0.68–0.75 | None |
| **Tread / Rubber** | 10–15% | Track shoes, road wheels, tire rubber | 0.08–0.18 | 0.28–0.34 | None |
| **Warning Accent** | 3–5% | High-contrast identification stripes, battery rails | 0.16–0.20 | 0.60–0.65 | None |
| **Emissive Tech/Optics** | 1–3% | Sensor lenses, headlamps, arc discharge rings | 0.15–0.25 | 0.75–0.85 | **HDR ×1.5–2.5** |

*See faction color presets and hex codes in [art-style-and-palette.md](references/art-style-and-palette.md).*

---

## 4. Blender Procedural & Deterministic Modeling

All production models must be script-reproducible using Blender's Python API (`bpy`) and the authoritative toolkit in `tools/RogueModelKit.py` (and `.agents/skills/rogue-command-3d-model-authoring/scripts/RogueModelKit.py`).

### Coordinate System Invariants
- **Forward**: Blender `+Y` $\rightarrow$ Unity `+Z`
- **Up**: Blender `+Z` $\rightarrow$ Unity `+Y`
- **Right / Starboard**: Blender `+X` $\rightarrow$ Unity `+X`
- **Ground Contact**: Lowest vertex of treads/tires/feet must rest exactly on $Z \ge 0.0$ ($0.0\text{m} \le Z_{\min} \le 0.08\text{m}$).
- **Local Origin**: `(0.0, 0.0, 0.0)` at ground center.
- Use `--structural-wear-mask` on articulated industrial models to keep channel G edge wear localized to convex corners while leaving flat and concave corners unmarked; this optional argument preserves the generator's existing CLI defaults.

### Core Procedural Generator Suite (`RogueModelKit`)
Use the tested assemblies to establish proportions, mechanical layout, and repeatable subsystems. Treat generated hulls and housings as a **functional blockout**, not a mandatory final surface style: refine visible macro forms with tapered profiles, compound transitions, and controlled curvature when the result reads as a stack of primitives. Follow the reference → blockout → silhouette review → form refinement → shading/material → export review sequence in [Blender Procedural Techniques](references/blender-procedural-techniques.md). The current 7,500-face value is Rogue Command's authoring budget for profiling, not a Unity-prescribed limit or a quota to fill; inspect the budget report and record Blender faces and exported Unity triangles separately. Preserve gameplay pivots, canonical sockets, hierarchy, and vertex colors during refinement.

| Subsystem | Key Assemblies in `RogueModelKit` | Geometric Output & Capabilities |
|---|---|---|
| **Chassis & Cabins** | `make_sloped_glacis_hull`, `make_faceted_turret_housing`, `make_aerodynamic_fuselage_assembly`, `make_troop_compartment`, `make_tubular_rollcage_assembly`, `make_cast_pelvis_assembly`, `make_articulated_mech_torso`, `make_sculpted_wheel_fender_assembly`, `make_transitional_coaming_collar`, `make_towing_shackle_assembly`, `make_crane_lifting_lugs`, `make_interlocking_armor_gusset` | Tapered glacis, compound faceted ballistic turrets with race collars, aerodynamic aircraft fuselages with wing fillets, cast pelvis gimbals, monocoque mech torsos, sculpted wheel arch fenders, transitional mounting coamings, armored troop bays, welded roll cages |
| **Mobility & Legs** | `make_continuous_track_assembly`, `make_quad_track_pod_assembly`, `make_all_terrain_wheel_assembly`, `make_bipedal_digitigrade_leg`, `make_quad_crawler_leg`, `make_hover_skimmer_assembly`, `make_ducted_tiltrotor_assembly`, `make_outrigger_stabilizer_legs`, `make_swing_arm_suspension_linkage`, `make_spaced_armor_side_skirts`, `make_telescopic_piston_assembly` | Continuous tracks, 4-track pods, 4x4/6x6/8x8 off-road chevron tires, biped/spider legs, hover skirts, tilt-rotors, outriggers, trailing suspension arms, spaced armor skirts, dual-stage telescopic hydraulic piston rams |
| **Weapons (15)** | `make_nanite_projector_assembly`, `make_gravity_cannon_assembly`, `make_coaxial_cannon_assembly`, `make_dual_cannon_assembly`, `make_heavy_howitzer_assembly`, `make_rotary_gatling_cannon`, `make_missile_launcher_pod`, `make_arc_energy_emitter`, `make_railgun_assembly`, `make_quad_flak_autocannon_assembly`, `make_plasma_mortar_assembly`, `make_collimated_beam_laser_assembly`, `make_vls_vertical_missile_array`, `make_flamethrower_assembly`, `make_emp_emitter_assembly`, `make_mantlet_dust_boot`, `make_ammo_belt_chute_assembly` | Cannons, dual guns, siege mortars, 6-barrel gatlings, rocket arrays, arc prongs, railguns, flak clusters, plasma mortars, beam lasers, VLS missile arrays, thermobaric flamethrowers, directional EMP emitters, accordion mantlet boots, articulated metallic ammo feed chutes |
| **Greebles & Structures** | `make_recessed_sensor_visor`, `make_gimbal_sensor_turret`, `make_radar_dish_or_rotodome`, `make_smoke_launcher_rack`, `make_side_sponson_assembly`, `make_dozer_blade`, `make_flight_deck_assembly`, `make_heavy_excavator_drum`, `make_shield_dome_assembly`, `make_emp_obelisk_assembly`, `make_bunker_pillbox_assembly`, `make_anti_air_battery_assembly`, `make_comms_relay_tower_assembly`, `make_catenary_cable_assembly`, `make_bmesh_hull_greebles`, `make_tactical_decal_plates`, `make_aps_hardkill_suite`, `make_tactical_stowage_kit`, `make_slat_cage_armor_assembly`, `make_era_reactive_bricks_assembly`, `make_camo_netting_assembly`, `make_thermal_radiator_assembly`, `make_remote_weapon_station_assembly`, `make_drone_docking_bay_assembly`, `make_recoil_earth_spade_assembly`, `make_perforated_barrel_shroud_assembly` | Recessed visor hoods, FLIR gimbals, radar rotodomes, smoke racks, sponsons, ERA bricks, dozer blades, flight decks, tactical shield domes, EMP obelisks, casemate pillboxes, SAM/CIWS batteries, C4ISR relay towers, catenary hydraulic hoses, BMesh-inset hatches/vents/lugs, BVHTree-raycasted tactical decal plates, hard-kill APS launchers & LWR optics, turret bustle stowage racks & spare track links, anti-HEAT slat cage armor grids, staggered ERA wedge bricks, 3D noise-displaced camouflage netting & barrel wrap, glowing thermal radiator banks, roof RWS micro-turrets, parasitic recon drone cradles, grounded recoil earth spade anchors, ventilated thermal barrel shrouds with multi-baffle muzzle brakes, bilateral nanite repair dispensers, gyroscopic grav-dampener hubs, telescopic comms/EW jamming masts, and heavy V-shaped kinetic ram prows |
| **Pipeline & Rigging** | `batch_and_join_by_material`, `apply_weighted_normals`, `bake_advanced_vertex_shading`, `generate_lod_chain`, `generate_collision_hulls`, `export_gameplay_metadata`, `make_wreckage_variant`, `make_thrown_track_debris`, `create_unity_locator_sockets` | Batched submeshes, face-area weighted split normals, RGBA wear baking, LOD0–2 chains, box colliders, `.gameplay.json`, thrown track debris, archetype-aware wreckage models, sockets |

*See exact arguments and recipes in [blender-procedural-techniques.md](references/blender-procedural-techniques.md).*

### Draw-Call Optimization via Material Batching & Smart Smoothing
Kitbashing produces 50–120 individual objects. Unbatched exports can create many Unity renderers and render submissions. Pass compatible geometry through `batch_and_join_by_material(objects, prefix, preserve_names, smooth_by_angle=True, smooth_angle_deg=38.0)` before export:
- **Material Batching**: Combines compatible meshes by PBR material while preserving essential animated pivots (`TurretBase`, `MainBarrel`). Output mesh groups depend on input materials and preserved objects. This Blender-side step does not establish the final Unity draw-call count.
- **Smart Angle-Weighted Smoothing**: Uses Blender 4.5's `shade_smooth_by_angle` to produce silky smooth round barrels, road wheels, hubs, and exhaust pipes, while preserving razor-sharp 45°/90° chamfers on flat armor plates.
- **Weighted Normals**: `batch_and_join_by_material(..., weighted_normals=True, normal_weight=50)` follows angle smoothing with Blender's Weighted Normal modifier (`mode="FACE_AREA"`, `keep_sharp=True`) to reduce shading pinch across broad beveled plates while keeping sharp edges. The weight is clamped to 1–100; mesh-only objects apply the modifier, while armature meshes keep it in the modifier stack. A dry-run validates batch planning only and does not verify rendered shading.
- **Multi-Channel Procedural Vertex Shading**: Invokes `bake_advanced_vertex_shading(objects, wear_level, exhaust_locs, muzzle_loc)` storing RGBA directly into the `Color` vertex attribute (`Mesh.colors32`):
  - **R (Red)**: Ambient Occlusion / Cavity Dirt (contact shadows in wheel wells, louvers, and undersides).
  - **G (Green)**: Curvature / Edge Wear (bright scratch glints on 45°/90° chamfer edges, simulating chipped paint).
  - **B (Blue)**: Thermal Soot / Weathering (darkening gradient around exhausts and weapon muzzles).
  - **A (Alpha)**: Optional team-tint mask (1.0 applies the shader's `_TeamColor`, 0.0 leaves the material color unchanged). The game currently has no faction-color binding for this property; it must be assigned by the author or another integration.
- **Multi-Level LOD Chains**: Invokes `generate_lod_chain(batched_objects)` with planar boundary preservation (`delimit={"NORMAL", "MATERIAL"}`), exporting a companion `_LODs.fbx` with LOD0 (100%), LOD1 (50%), and LOD2 (22%) meshes.
- **Physics Collision Hulls**: Invokes `generate_collision_hulls(objects)` to export flat-shaded bounding boxes `<Prefix>_COLLIDER_Hull` and `<Prefix>_COLLIDER_Turret` tagged with `is_collider = True`.

### Fluent Assembly with `RogueVehicleBuilder`
For rapid, reproducible vehicle generation without manual coordinate arithmetic, use the declarative `RogueVehicleBuilder`:

```python
import RogueModelKit as rmk

builder = (
    rmk.RogueVehicleBuilder("ColossusFortress", faction="siege", camo="desert")
    .set_archetype("super_heavy").set_wear_level("battle_hardened")
    .add_sloped_hull(3.8, 2.8, 1.05).add_quad_track_pods(1.65, 0.62)
    .add_engine_deck(slat_count=5, with_exhausts=True)
    .add_turret((1.85, 2.1, 0.58), center_y=0.15)
    .add_dual_cannon(caliber=0.082, length=1.65, spacing=0.38)
    .add_sensor_visor().add_radar_dish().add_side_sponsons()
    .add_hydraulic_hoses().add_hull_greebles().add_tactical_markings()
    .add_slat_armor().add_era_bricks().add_thermal_radiators()
    .add_colliders(include_turret=True).add_unity_locators()
)
diagnostics = builder.build_and_export(
    fbx_path=FBX_PATH, blend_path=BLEND_PATH, preview_path=PREVIEW_PATH,
    render_multiview=True, smooth_by_angle=True, weighted_normals=True,
    bake_vertex_dirt=True, include_locators=True, generate_lods=True,
    generate_colliders=True, gameplay_meta_path=GAMEPLAY_JSON_PATH, target_budget=4500
)
```

### Wave 3/4 Structural Anchor Contract

The current `RogueModelKit` closes the most common anti-floating gaps in generator-backed authoring. These details are emitted by the existing fluent methods; do not add detached cubes in a preset or builder to imitate them:

- `.add_continuous_tracks()` adds `Armor_SponsonBridge_L/R` floor bridges and keeps suspension/mudflap hardware tied to the track assembly.
- `.add_all_terrain_wheels()` adds `Chassis_AxleBeam_*`, `Chassis_Differential_*`, and `Chassis_ShockStrut_*` between wheel hubs and chassis rails.
- `.add_bipedal_legs()` / `make_bipedal_digitigrade_leg()` adds `HipFlange_*`, `CalfArmor_*`, and `AnkleClevis_*` at the pelvis, calf, and foot pivots.
- `.add_engine_deck()` adds a recessed `Engine_RadiatorCavity` beneath the louvers; weapon and sensor helpers add their load-bearing `Gatling_PintleSaddle_*`, missile trunnion cradle/axle, `Arc_PedestalCollar`, `Sensor_AntennaDeckCollar`, `Smoke_MountBracket_*`, `Radar_DeckPylon`, and `Sponson_BridgeArm_*` components.
- `.add_interlocking_gussets()` uses `make_interlocking_armor_gusset()` for triangular web plates at hull/superstructure joints.

When refreshing a generator-backed source, invoke its `--preset` without conflicting explicit `--archetype`/`--weapon` overrides, pass `--bake-atlas` when the source owns PBR maps, and verify the expected anchor names in the resulting `.blend` before promoting the FBX or preview prefab. Keep manual Atlas/Bastion authoring on their dedicated bridge; never replace it with a generic preset.

### Scriptable CLI Tool (`generate_unit_model.py`)
Run headless vehicle generation directly from the command line using Blender and the CLI generator script:

```powershell
.\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\generate_unit_model.py -- `
    --name ColossusFortress --archetype super_heavy --faction siege --camo desert --weapon dual_cannon `
    --wear battle_hardened --sponsons --smoke --radar --dirt --locators --lods --colliders --gameplay-json --wreckage --multiview
```

- **Mobile Archetypes (42)**: `tank`, `walker`, `bike`, `artillery`, `arc_tank`, `hover`, `spider`, `vtol`, `halftrack`, `super_heavy`, `buggy`, `flak_anti_air`, `siege_crawler`, `drone_carrier`, `quad_mech`, `combat_harvester`, `colossus_titan`, `hover_cruiser`, `heavy_dropship`, `shield_truck`, `demolition_breacher`, `artillery_strider`, `thermobaric_tank`, `ew_jammer`, Wave 3: `mobile_sam_launcher`, `recovery_crawler`, `mine_layer`, `sniper_tankette`, `hexapod_assault`, `assault_hovercraft`, Wave 4: `bridge_layer`, `command_hq_rig`, `flying_wing_bomber`, `mortar_carrier`, `siege_drill_mole`, `heavy_crane_mech`, and Wave 7: `nanite_support_rover`, `gravity_siege_breaker`, `tactical_relay_carrier`, `amphibious_assault_tank`, `plasma_mortar_strider`, `stealth_recon_skimmer`.
- **Strategic Structure Types (10)**: `factory`, `defense_turret`, `radar_station`, `energy_silo`, `orbital_silo`, `shield_dome`, `emp_obelisk`, `bunker_pillbox`, `anti_air_battery`, and `comms_relay_tower`. These are generated through `RogueStructureBuilder`, separate from the 42 mobile archetypes.
- **Weapons (15 CLI values)**: `cannon`, `gatling`, `missile`, `arc`, `howitzer`, `dual_cannon`, `railgun`, `quad_flak`, `plasma_mortar`, `laser_beam`, `vls_missiles`, `flamethrower`, `emp_emitter`, `nanite_projector`, and `gravity_cannon`.
- **Factions**: `vanguard` (slate teal), `siege` (rust ochre), `syndicate` (deep cobalt), `arctic` (tundra navy).
- **Camouflage Variants**: `digital`, `splinter`, and `tigerstripe` build procedural Voronoi/wave color patterns; `desert`, `urban`, `arctic`, and `woodland` use a procedural noise pattern with faction colors. These Blender node graphs can be baked to the atlas. They do not adapt to the map's runtime biome.
- **Biome Palette Alias**: `--biome desert|arctic|woodland|urban` applies the corresponding authoring-time color palette only when `--camo` remains `none`; it does not adapt to a map's runtime biome.
- **Surface Wear Levels**: `pristine` (factory clean), `field_tested` (moderate edge scratches and road grime), `battle_hardened` (heavy combat gouges, exhaust soot, and cavity dirt).
- **Presets (56 Total)**:

| Preset ID | Base Archetype / Building | Weapons & Key Attachments | Tactical Visual Identity |
|---|---|---|---|
| `siege_breaker` | `super_heavy` | Dual cannon, dust boot, suspension arms, spaced skirts, sponsons, slat armor, ERA bricks | Breakthrough fortress tank |
| `recon_interceptor` | `buggy` | Rotary gatling, roll cage, radar | High-mobility scout vehicle |
| `gunship_alpha` | `vtol` | Missile pods, ducted tilt-rotors | Aerodynamic attack VTOL |
| `apc_enforcer` | `halftrack` | Rotary gatling, troop cabin, smoke, slat armor | Mechanized infantry transport |
| `dune_raider` | `bike` | Rotary gatling, spine chassis | Fast strike motorcycle |
| `colossus_titan` | `colossus_titan` | Dual cannons, IK rig, radar, antenna | Heavy bipedal combat titan |
| `sea_lance` | `hover_cruiser` | Railgun turret, sponsons, radar, thermal radiators | Heavy assault hovercraft |
| `sky_dreadnought` | `heavy_dropship` | Missile pods, ducted tilt-rotors | Heavy gunship / airborne lander |
| `orbital_defense` | `orbital_silo` | Orbital silo aperture, gantry | Strategic planetary defense base |
| `sky_sweeper` | `flak_anti_air` | Quad flak autocannon, dust boot, suspension arms, outriggers | Area denial anti-aircraft platform |
| `dreadnought_crawler` | `siege_crawler`| Heavy railgun, quad track pods, suspension arms, outriggers, ERA bricks, thermal radiators | Super-heavy mobile siege platform |
| `hive_carrier` | `drone_carrier` | Flight deck, rotodome, bridge | Autonomous drone command carrier |
| `titan_quad` | `quad_mech` | Dual cannons, 4 spider legs, IK rig | Articulated heavy quad walker |
| `resource_behemoth` | `combat_harvester`| Rotary cutting drum, ore hoppers, slat armor | Heavy armed resource excavator |
| `beam_destroyer` | `super_heavy` | Collimated beam laser, suspension arms, spaced skirts, sponsons, thermal radiators | Direct-energy combat destroyer |
| `vls_missile_cruiser`| `hover_cruiser`| Vertical launch missile array, conning tower | Guided stand-off missile cruiser |
| `aegis_bastion` | `shield_truck` | Arc emitter, 6x6 chassis, projector dome, shield bubble, thermal radiators | Mobile tactical shield generator truck |
| `volatile_runner` | `demolition_breacher`| Plasma mortar, volatile core drum, detonator spikes, dozer, slat armor | High-speed armored demolition breacher |
| `longbow_strider` | `artillery_strider`| Railgun, long digitigrade legs, outriggers, IK rig, camo netting, thermal radiators | Long-range bipedal sniper walker |
| `inferno_purger` | `thermobaric_tank` | Flamethrower, twin cryo/fuel tanks, spaced skirts, sponsons, ERA bricks, thermal radiators | Heavy thermobaric flame assault tank |
| `spectre_jammer` | `ew_jammer` | EMP emitter, 6x6 chassis, phased-array panels, radar, camo netting | Electronic warfare & EMP disruptor truck |
| `bastion_shield_dome`| `shield_dome` | Converging emitter pylons, field coils, dome core, thermal radiators | Stationary area shield generator |
| `tesla_obelisk` | `emp_obelisk` | Segmented conductive spire, discharge rings, emitter crown, thermal radiators | Fortified EMP suppression obelisk |
| `skyguard_sam` | `mobile_sam_launcher` | 6x6 chassis, erector-launcher canisters with rams, radar, APS; digital camo, factory-new | Mobile surface-to-air missile battery |
| `salvage_hauler` | `recovery_crawler` | Crane boom, winch drum, earth spade, dozer, stowage, slat armor, fuel drums; battle-hardened | Armored recovery vehicle |
| `minefield_sower` | `mine_layer` | Mine hopper, dispensing chute, racked AT mines, ammo chutes; desert camo | Tracked area-denial mine layer |
| `longshot_tankette` | `sniper_tankette` | Long 2.45 m precision gun, dust boot, track shoes, camo netting; splinter camo | Low-silhouette sniper tankette |
| `arachnid_hexapod` | `hexapod_assault` | 4 rigged crawler legs + static middle pair, IK rig, missile pod, APS | Six-legged assault crawler |
| `tidebreaker_hovercraft` | `assault_hovercraft` | Skirt, bow ramp, twin ducted props and rudders, gatling; urban camo | Amphibious assault hovercraft |
| `urban_brawler` | `tank` | Cannon, spaced skirts, cupola, APS, stowage, slat armor, ERA bricks; urban, battle-hardened | City-fighting MBT remix |
| `arctic_sentinel` | `walker` | Twin gatlings, ammo chutes, stowage, antenna, camo netting; arctic camo | Cold-weather sentinel walker remix |
| `night_stalker` | `bike` | Gatling, ammo chutes, markings; tigerstripe, factory-new | Night raider bike remix |
| `armored_hauler` | `halftrack` | Gatling, cupola, smoke, ammo chutes, stowage, slat armor, fuel drums; woodland camo | Logistics halftrack remix |
| `early_warning_array` | `radar_station` | Radar station with APS, hoses, greebles, camo netting | Strategic early-warning radar |
| `fusion_silo` | `energy_silo` | Energy silo with hydraulic pistons, hoses, greebles, thermal radiators | Strategic power silo |
| `scissor_bridgelayer` | `bridge_layer` | Scissor bridge spans, hydraulic launch boom, gatling; woodland camo | Tracked combat engineering bridge layer |
| `overlord_command_hq` | `command_hq_rig` | 8x8 chassis, expandable C4ISR shelter, satcom radome, mast, APS, camo netting; digital camo | Mobile theater command & control truck |
| `wraith_stealth_wing` | `flying_wing_bomber` | Delta blended wing, internal bomb bay, dorsal intakes, missile pods, thruster VFX; splinter camo | Low-observable tactical strike bomber |
| `thunder_mortar_carrier` | `mortar_carrier` | Open casemate hatches, turntable plasma mortar, ammo racks, slat armor, camo netting; desert camo | Tracked heavy siege mortar carrier |
| `subterranean_mole` | `siege_drill_mole` | 4-stage conical auger bore, flamethrower turret, pistons, stowage, ERA bricks, thermal radiators; battle-hardened | Subterranean assault bore vehicle |
| `atlas_crane_mech` | `heavy_crane_mech` | Bipedal industrial walker, dorsal reactor, gantry crane, hydraulic pincer claws, dual cannon, thermal radiators | Heavy siege & recovery crane mech |
| `fortified_pillbox` | `bunker_pillbox` | Octagonal casemate, firing embrasure, twin barrels, observation cupola, slat armor, ERA bricks, camo netting | Fortified frontline casemate pillbox |
| `patriot_sam_battery` | `anti_air_battery` | Azimuth turret, twin 4-cell SAM pods, planar fire-control radar, APS | Strategic SAM & CIWS air-defense battery |
| `deep_space_relay` | `comms_relay_tower` | Tapered lattice truss mast, bilateral microwave drums, omni beacon spire, thermal radiators | C4ISR deep-space telemetry relay tower |
| `crimson_praetorian` | `colossus_titan` | Railgun, custom crimson/gold hex palette (`#5a1414`/`#8f2424`/`#ffaa00`), IK rig, APS, ERA bricks, thermal radiators, muzzle VFX | Elite Praetorian guard titan remix |
| `cobalt_widowmaker` | `hexapod_assault` | Beam laser, custom midnight-cobalt/cyan hex palette (`#142238`/`#29466e`/`#00f0ff`), IK rig, APS, thermal radiators | Elite direct-energy hexapod remix |
| `golden_aegis_command` | `hover_cruiser` | VLS missiles, custom slate/gold hex palette (`#2b3642`/`#c29b38`/`#44ffaa`), shield bubble, ERA bricks, thruster VFX | Flagship command hover cruiser remix |

- **Preset defaults for camo, wear, attachments, custom hex colors, and VFX**: A preset may declare `camo`, `wear`, `attachments` (comma list of `era`, `aps`, `sponson`, `fuel_drums`), `primary_color`, `light_armor_color`, `emissive_color`, `vfx_flash`, `vfx_thruster`, and any of the Wave 2 / Wave 5 boolean subsystem flags (`slat_armor`, `era_bricks`, `camo_netting`, `thermal_radiators`). These apply only when the matching CLI option was not supplied. `--preset` choices are derived from `PRESETS` keys, so a new preset only needs one dictionary entry.
- **Hexapod rig boundary**: `hexapod_assault` reuses the quad crawler leg rig; the middle leg pair is static geometry bound to the body bone (no per-leg IK). It does not add foot-contact sockets for the middle pair to avoid socket-role ambiguity.

- **CLI Flag Groups**:
  - *Geometry & Optimization*: `--lods` (LOD0-2 chain), `--colliders` (`_COLLIDER_Hull/Turret`), `--gameplay-json` (kinematic metadata), `--wreckage` (battle-destroyed model), `--wreckage-severity <light|medium|catastrophic>` (destruction severity), `--target-budget <int>` (poly profiling), `--track-sag` (parametric catenary track sag), `--armor-plating` (procedural BMesh faceted panels), `--ik-rig` (automated 2-bone IK solver controls), `--weighted-normals` (default on; `--no-weighted-normals` disables the pass).
  - *Textures & Shading*: `--bake-atlas` (PBR atlas bake), `--bake-normals`, `--single-material` (`<Name>_Atlas.fbx`), `--camo <type>`, `--camo-scale`, `--camo-contrast`, `--panel-lines` (procedural bump), `--normal-intensity`.
  - *Attachments & Greebles*: `--suspension-arms` (trailing-arm torsion linkages), `--mantlet-boot` (open torus pleat rings around cannon-family barrels; supports cannon, dual cannon, howitzer, railgun, quad flak, plasma mortar, and flamethrower), `--spaced-skirts` (segmented standoff composite skirts), `--hydraulic-hoses` (catenary hoses with couplers), `--hull-greebles` (maintenance hatches, vents, and lifting lugs), `--tactical-markings` (BVH-snapped side stripes and zenith IDs), `--hydraulic-pistons` (telescopic hydraulic actuators), `--ammo-chutes` (articulated metallic feed chutes), `--aps-suite` (hard-kill APS launchers & LWR), `--stowage-kit` (bustle rack, tarp, Jerry cans, spare tracks), `--slat-armor` (anti-HEAT bar armor grids), `--era-bricks` (staggered ERA wedge blocks), `--camo-netting` (3D noise-displaced camouflage canopy & barrel wrap), `--thermal-radiators` (armored heat-exchanger radiator banks with glowing fins), `--nanite-dispenser` (bilateral armored nanite repair dispenser pods), `--grav-dampener` (gyroscopic grav-dampener hub with singularity core), `--comms-mast` (telescopic comms & EW jamming mast), `--ram-prow` (reinforced V-shaped kinetic battering ram prow), `--antenna` (whip antenna), `--cupola` (commander hatch), `--rivets` (seam bolts), `--track-shoes` (individual cleats), `--outriggers` (stabilizer rams), `--flight-deck`, `--excavator-drum`.
  - *Palette Overrides*: `--primary-color <hex>`, `--light-armor-color <hex>`, `--emissive-color <hex>`, `--biome <desert|arctic|woodland|urban>`.
  - *Execution Controls*: `--dry-run` (validation without meshing), `--batch-all-archetypes` (build all 42 archetypes), `--report-json <path>`, `--lod`, `--multiview`.
- **Outputs**: Generates editable `.blend`, Unity-optimized `.fbx` with lightmap UVs, 4-channel vertex color shading, canonical unprefixed `Socket_*` empties, batched submeshes, collision hulls, companion `.gameplay.json`, and 4-view orthographic preview sheet.

### Automated Quality & Subsystem Profiling Diagnostics
Always invoke `validate_model_mesh_quality(model_objects)` and `profile_model_budget_and_drawcalls(model_objects)` prior to export. They verify:
- **Ground Contact Check**: Confirms $Z_{\min} \in [-0.02\text{m}, 0.08\text{m}]$ for ground units ($Z_{\min} \le 0.55\text{m}$ for hover/vtol).
- **Subsystem Triangle Distribution**: Categorizes geometry into Chassis & Hull, Turret & Cabin, Mobility & Wheels, Weapons, and Optics & Greebles, reporting percentage of budget consumed.
- **Material Slot Hygiene**: Flags any object with missing or null PBR material assignments.

### Multi-Perspective RTS Studio
Use `setup_multi_view_studio(model_objects, preview_dir, base_name, ortho_scale, render_all=True)` to render 4 calibrated orthographic perspectives simultaneously. The cameras center on the model bounds and expand the orthographic scale when needed so the full unit remains inside the frame:
- **`ISOMETRIC`**: $45^\circ$ RTS combat camera (the gameplay primary read).
- **`TOP_DOWN`**: Direct vertical plan view (minimap readability and deck balance).
- **`FRONT`**: Head-on silhouette readability and glacis slope verification.
- **`SIDE`**: Profile balance, ground clearance, and suspension proportions.

### FBX Export Parameters
In your Python builder script, export selected meshes with:
```python
bpy.ops.export_scene.fbx(
    filepath=str(FBX_PATH),
    use_selection=True,
    object_types={"MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_NONE",
    use_mesh_modifiers=True,
    mesh_smooth_type="FACE",
    path_mode="AUTO",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=False,
    add_leaf_bones=False,
    use_custom_props=False
)
```

*See tested geometric algorithms and recipes in [blender-procedural-techniques.md](references/blender-procedural-techniques.md).*

**Important**: `bake_space_transform=False` is required for rigged FBX exports. With `True`, Blender bakes the axis conversion (Z-up → Y-up) into vertex/bone transforms, which corrupts bind-pose matrices for skinned meshes — the SkinnedMeshRenderer reports zero `localBounds` (Extents 0,0,0) and the mesh collapses in-game. With `False`, Unity handles axis conversion at import time, preserving correct bind poses. This was verified via runtime inspection: `True` → degenerate bounds; `False` → correct bounds. This is especially critical for hexapods and other non-standard rigs.

---

## 5. Native Hierarchy & Template Alignment

Rogue Command gameplay components (`EntityController`, weapons, combat physics) live on native prefabs. Mod models are non-destructive visual overlays loaded via `visualRootPath`. The loader calibrates attached visuals from combined native and overlay renderer bounds, excluding collider meshes, antennae, and `Socket_*` anywhere in the renderer transform ancestry; it applies a bounded uniform ratio and rejects an overlay with no usable renderer extent (`<= 0.001`) instead of leaving it at scale 1. It uses conservative unit/building fallback extents when native renderer bounds are unavailable. The overlay instance root scale is runtime-controlled, so keep authored offsets and any model-specific scale in descendant transforms rather than relying on the prefab root scale. Do not add a universal Blender-to-Unity `100x` compensation or a narrow scale clamp; verify the actual model against its native renderer bounds.

### Template Dimension & Hierarchy Matrix

| Native Template | Mod Unit Example | `visualRootPath` | Collider Bounds (W × H × L) | Key Pivot Alignment |
|---|---|---|---|---|
| `PCXBaseTank` | Sandlance Scout | `"Vehicle"` | ~2.4m × 1.6m × 2.8m | Twin barrels at `X = ±0.14`, `Y ≈ 1.0` |
| `BombBike` | Skirmish Strike Bike | `"GameObject/BombBike"` | ~1.2m × 1.4m × 2.2m | Wheelbase: Rear `Z = -0.78`, Front `Z = +0.75` |
| `ArtilleryTank` | Colossus Siege Platform | `"Vehicle"` | ~2.8m × 1.9m × 3.6m | Long elevated barrel at ~20° elevation |
| `GatlingWalker` | Ironclad Walker | `"Vehicle"` | ~2.2m × 2.7m × 2.0m | Underslung rotary guns at waist level |
| `LightningTank` | Surge Arc Tank | `"Vehicle"` | ~2.3m × 1.6m × 2.8m | Toroidal core centered at `(0, -0.1, 1.2)` |

*See full hierarchy trees and calibration rules in [native-hierarchy-and-pivots.md](references/native-hierarchy-and-pivots.md).*

---

## 6. Unity Material Pipeline & Draw-Call Optimization

When the Unity project scaffolds the prefab and builds the AssetBundle:

1. **URP Shader Compatibility**: Rogue Command uses URP. Use `Universal Render Pipeline/Lit` for simple sample materials and `RogueCommand/VehiclePBR_VertexWear` for atlas materials with RGBA vertex-wear channels. Never use Built-in `Standard` for a game-bound material.
2. **The Emission Invariant**: Setting `_EmissionColor` is not enough; you must explicitly call `material.EnableKeyword("_EMISSION")` and set `globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive`.
3. **GPU Instancing**: The prefab builder sets `material.enableInstancing = true` on materials without an atlas. This opts materials into Unity GPU instancing when the shader, render path, and renderer setup support it; it does not guarantee batching or a lower draw-call count.
4. **Script-Free Prefabs**: Visual prefabs must contain **no** `MonoBehaviour` components. The loader enforces fail-closed rejection on any missing or custom scripts.

*See C# implementation details in [unity-material-pipeline.md](references/unity-material-pipeline.md).*

---

## 7. Card Sprite Authoring Standards

Each playable unit requires a matching `128 × 128` pixel card Sprite referenced by `cardImageAssetName`.

- For every new or refreshed 2D raster card, icon, or sprite, use the installed built-in `imagegen` skill. Use the final Blender preview as a visual reference and preserve the authored model's silhouette and palette. Do not draw these images procedurally in C#, Python, SVG, canvas, or Pillow.
- Keep the generated PNG in the Unity authoring project's tracked `Assets/RogueCommandExpansionSample/` tree. Unity import settings may cap the runtime texture at 128 × 128; validate that the Sprite in the built AssetBundle uses that size. Record the final ImageGen prompt with the asset change so another agent can reproduce the intent.

- **Texture Import**: `FilterMode.Point`, `mipmapEnabled = false`, `spriteImportMode = Single`.
- **Composition**: Angled profile of the vehicle rendered or drawn against a deep tactical dark gradient (`#0a1216` to `#162a34`).
- **Color Fidelity**: Use the exact color values from the 3D model's materials so the card immediately prepares the player for what appears on the field.
- **Focal Glint**: Add a 2–3 pixel bright emissive optic or weapon glint to draw the eye during card drafting.

---

## 8. Multi-Stage Validation & Evidence Boundaries

1. **Blender Visual Preview**: Run `blender.exe --background --python tools\Build-<UnitName>Model.py`. Inspect `artifacts/previews/<unit-name>.png` for silhouette, ground contact ($Z \ge 0.0$), and specular bevels.
2. **Unity Scaffolding**: Build AssetBundle in Unity 2021.3.45f2; verify zero missing scripts, valid mesh renderers, and zero null material slots.
3. **Offline Reference Audit**: Run `tools/validate_content_bundle_references.py` and `dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .` (132 tests passing).
4. **Steam Runtime Validation**: Only a live Steam session (AppID `1461910`) verifies card offers, factory production, and combat rendering. Follow reversible deployment and record evidence in `docs/research/AGENT-HANDOFF.md`.

---

## 9. Advanced Procedural Capabilities (Atlas Baking, Rigging, VFX & Unity Bridge)

- **Texture Atlas Baking & Single-Material Export (`--bake-atlas`, `--single-material`)**:
  - Non-overlapping UV unwrapping (`unwrap_atlas_uvs`, island margin 0.02). Headless Cycles 1-sample baking generates `<Name>_Albedo.png`, `<Name>_MetallicGloss.png` (linear RGB metallic + alpha smoothness), `<Name>_Normal.png`, and `<Name>_Emission.png`.
  - Joins batched geometry into `<Name>_Atlas.fbx` with single material slot.

### Optional External Texture Passes

- Blender 4.5.14 and the deterministic `--bake-atlas` workflow remain the production baseline. ArmorPaint and Material Maker are optional local texture steps, not build dependencies; stage them with `tools/Setup-TextureAuthoringTools.ps1` when available.
- After the UV layout is final, import the FBX or Blender mesh into ArmorPaint or Material Maker and use the application only to author texture images. Material Maker 1.7 supports a paint project when the model has UVs and a single submesh; otherwise use its procedural graph path or a disposable texture-only copy. Build the Unity prefab and AssetBundle from the original Blender export to preserve canonical sockets and mesh hierarchy; after Unity import, verify that the mesh still exposes its RGBA vertex-color attribute. Do not accept an external mesh export without separately checking those contracts.
- ArmorPaint's neural nodes run locally after their model files have been downloaded; its manual recommends at least 6 GB of VRAM. `Image to PBR` derives base color, occlusion, roughness, normal, and height from a color image. This does not provide the project's complete texture set by itself: set Metallic and Emission deliberately and preserve all four expected maps. See the [ArmorPaint manual](https://armorpaint.org/manual).
- Export the maps with a custom preset or graph and preserve the existing names: `<Name>_Albedo.png`, `<Name>_MetallicGloss.png`, `<Name>_Normal.png`, and `<Name>_Emission.png`. ArmorPaint's Unity preset packs Metallic, Occlusion, and Smoothness; confirm its swizzle or use a custom preset so the project shader receives Metallic in R and Smoothness (`1 - roughness`) in A. The shader does not consume AO from this map. The Unity importer must keep MetallicGloss linear (`sRGBTexture = false`, alpha from input); Albedo and Emission remain sRGB and Normal uses `TextureImporterType.NormalMap`. `VehiclePrefabBuilder` applies these import settings during rebuild, so verify the effective importer settings and texture after Unity reimports it. See [Unity Material Pipeline](references/unity-material-pipeline.md).
- Do not bake ArmorPaint's occlusion into the project's vertex-wear R channel or overwrite any RGBA vertex colors. Keep the existing cavity AO, edge wear, thermal soot, and team-mask authoring contracts on the source mesh.
- For the complete authoring and post-import checklist, follow [PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md) and [PBR Texture Validation](../rogue-command-pbr-texture-validation/SKILL.md).

- **Skeletal Armature Rigging & Procedural Animations (`--rig`, `--animate`)**:
  - Armature hierarchy: `Bone_Root` $\rightarrow$ `Bone_Hull` $\rightarrow$ `Bone_TurretYaw` $\rightarrow$ `Bone_TurretPitch` $\rightarrow$ `Bone_BarrelRecoil`. Rigid weights ($1.0$) per plate.
  - Bakes 4 RTS action clips: `Action_Idle`, `Action_Drive`, `Action_FireRecoil`, and `Action_Death`.
- **Procedural VFX Meshes (`--vfx-flash`, `--vfx-thruster`, `--shield-bubble`)**:
  - Star-spike muzzle flashes, conical thruster plumes, and geodesic shield bubble envelopes (`<Name>_ShieldBubble`).
- **Unity Prefab Scaffolder (`VehiclePrefabBuilder.cs`)**:
  - Bridges Blender FBX and `.gameplay.json` into prefabs. Configures TextureImporters, applies `RogueCommand/VehiclePBR_VertexWear` shader, converts `_COLLIDER` meshes to box colliders, reuses nested socket transforms, and builds `LODGroup`.
  - Headless command:
    ```powershell
    & "C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe" -batchmode -quit `
      -projectPath "unity\RogueCommandAssetAuthoring" `
      -executeMethod RogueCommandExpansion.Authoring.VehiclePrefabBuilder.BuildPrefabFromCommandLine -gameplayJson "<path>"
    ```

---

## 10. Novel Authoring Capabilities (Tactical Structures, Attachments, Damage States & Icon Baker)

The toolchain introduces four novel procedural capabilities expanding authoring beyond mobile vehicle units into RTS base infrastructure, modular unit customization, battle degradation, and tactical UI assets:

### A. Tactical Structures Suite (`--structure`, `--building-type`)
Addresses the strict modding rule: *Every added unit must declare its own namespaced `productionBuilding`*.
- **`RogueStructureBuilder`**: Procedural construction sharing core PBR materials and bevel physics with $Z_{\min} = 0.0\text{m}$.
- **Supported Types**: `factory` (production hall, gantry crane, bay door, exhaust stacks, `Socket_Deploy_Unit`), `defense_turret` (octagonal concrete bunker, rotating cap, heavy weapon), `radar_station` (command bunker, mast, parabolic dish), `energy_silo` (cylindrical reactor, containment rings, plasma channels), `orbital_silo` (launch aperture, gantry, exhaust trench).
- **CLI**: `python tools/generate_unit_model.py --structure --building-type factory --name VanguardFactory --colliders --gameplay-json`

### B. Modular Attachments & Hardpoint Customization (`--attachments`, `--export-detached-attachments`)
- **Attachment Types**: `era` (Explosive Reactive Armor tiles), `aps` (Active Protection System radar pods and countermeasure mortars), `sponson` (flank weapon pods with mantlet housing), `fuel_drums` (rear auxiliary tanks/capacitors with retaining straps).
- **Detached Asset Export (`--export-detached-attachments`)**: Exports `<Name>_Attachment_<Type>.fbx` detached from base FBX. Unity mounts them in `RCL_AttachmentGroup_<Type>` at captured world pose, allowing `RuntimeVisualDirector` to dynamically detonate or jettison them without mesh distortion.
- **CLI**: `python tools/generate_unit_model.py --name VanguardTank --attachments era,aps,sponson,fuel_drums --export-detached-attachments`

### C. Progressive Battle Damage States (`--damaged-state`)
Visual combat degradation at intermediate health ($25\% - 50\%$ HP) before complete wreckage:
- Generates `<Name>_Damaged.fbx` and editable `<name>-damaged.blend`: dented/distorted armor plates, jammed/tilted turret ring ($4^\circ - 6^\circ$ off-axis), displaced road wheels, and `Combat_Scorched` materials.
- **CLI**: `python tools/generate_unit_model.py --name VanguardTank --damaged-state`

### D. Tactical Minimap & UI Silhouette Icons (`--render-icons`)
Headless Cycles/Eevee camera with transparent background and calibrated 3-point RTS studio lighting:
- **Dual Projections**: `Top-Down Zenith (90°)` (minimap blips, planar footprint) and `Isometric RTS` (exact $45^\circ$ elevation for unit cards and tech tree icons).
- **CLI**: `python tools/generate_unit_model.py --name VanguardTank --render-icons --icon-size 64 --icon-elevation 45.0`

---

## 11. RuntimeVisualDirector Integration (Static Implementation; Steam Validation Pending)

`RuntimeVisualDirector` (`RogueCommandExpansionLoader.Runtime.Visual.RuntimeVisualDirector`) attaches via Harmony postfix on `EntityFactory.InstantiateEntity` when spawned objects expose canonical sockets. Requires no serialized MonoBehaviours in bundles.

### A. Socket-to-Gameplay Runtime Binding Map

| Authored Socket | Intended Runtime Behavior in the Current Code |
| :--- | :--- |
| `Socket_TurretPivot` | **Target Tracking**: Traverses azimuth at 120°/s towards hostile target (`CurrentAttackTarget`). Applies $5^\circ$ tilt at $\le 25\%$ HP. |
| `Socket_RadarMast` | **Radar Sweep**: Continuous rotation around local up axis at 30 RPM. |
| `Socket_DamageSmoke` | **50% HP Transition**: Spawns volumetric smoke when `HealthRatio <= 0.50f`; switches to `<Name>_Damaged` mesh and jettisons `FuelDrums`. |
| `Socket_DamageFire` | **25% HP Critical**: Spawns flame particles and dynamic point light, burns out APS, and tilts `Socket_TurretPivot`. |
| `Socket_Deploy_Unit` | **Factory Rollout**: Detects `InProductionCount > 0`, sliding bay door upward 1.4 m during rollout. |
| `Socket_RecoilPrimary` | **Barrel Recoil**: Linear slide backward (0.22 m) upon shot fired (`_timeOfLastShot`), recovering via spring-damper lerp. |
| `Socket_MuzzlePrimary` | **Combat Flash**: Pulses authored star-spike mesh for 0.07 s synchronously with `_timeOfLastShot`. |
| `Socket_Hardpoint_<Type>` | **Hardpoint Lifecycle**: Mounts detached attachments; detonates ERA at $\le 75\%$ HP and restores on field repair. |

### B. Resilient Visual Particle Fallback (`RuntimeVisualVfxFallback`)
1. **Tier 1 (Native Engine Discovery)**: Clones `EntityController.onFireParticleSystem` and searches memory for loaded native particle prefabs (`Smoke`, `Fire`) to guarantee visual unity.
2. **Tier 2 (Procedural Fallback)**: Dynamically generates standalone Unity `ParticleSystem` with soft unlit alpha blending and color ramps if native prefabs are unavailable.

### C. Binary Verification via REA
Verified against pinned `Assembly-CSharp.dll` (`d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` / `db0e9462-128d-4296-9900-de18b4bd0c00`):
- `EntityController.HealthRatio` (`0x060007e4`): Triggers 50% smoke and 25% fire damage transitions.
- `EntityController.DestroyHasBeenStarted` (`0x0600077d`): Confirms entity death for zero-allocation particle teardown.
- `EntityController.InProductionCount` (`0x060007df`): Queries factory queue to drive animated rollout bay doors.
- `EntityController.CurrentAttackTarget` (`0x060007fc`): Primary weapon target resolution for procedural turret traverse.
- `EntityController.IsEnemy` (`0x06000844`): Authoritative hostility check to ensure turrets only target enemies.

*Full member signatures and evidence ledger: [AGENT-HANDOFF.md](../../../docs/research/AGENT-HANDOFF.md#4-rea-verified-reflection-signatures--cil-tokens).*

---

## 12. Anti-Ring Ballistic Realism & Two-Tier Shader Wear Overhaul

To eliminate toy-like constant-radius cylinders and repetitive stacked rings ("tubos rectos pegados y demasiados anillos"), the authoring pipeline enforces authentic military ballistic and electromagnetic engineering:

### A. Tapered Conical Frustum Primitive (`make_tapered_cylinder`)
- Uses `bpy.ops.mesh.primitive_cone_add(radius1, radius2, depth)` with directional tracking quaternions.
- Provides true continuous taper ($1.25\times$ breech down to $0.92\times$ muzzle caliber) on cannon and howitzer barrels, double-conical bore evacuators, and electrode prongs.

### B. Anti-Ring Weapon Assemblies
- **Cannons & Howitzers**: Breech reinforce tapers into main tube; double-conical aerodynamic evacuator fairings (no cylindrical steps/rings); slotted multi-baffle muzzle brakes with gas venting windows.
- **Plasma Mortar**: Four longitudinal structural stator rail beams (`StatorRail_Top`, `_Bottom`, `_L`, `_R`) with linear induction channels, replacing concentric floating rings.
- **Collimated Beam Laser**: Faceted hexagonal jacket (`HexCollimationShroud`) and longitudinal heat-sink fin rails, replacing toroid donut rings.
- **Arc Energy Emitter**: Octagonal ceramic dielectric housing, heavy rear transformer insulator, and dual convergent discharge prongs, replacing repetitive coils.
- **Mantlet Dust Boot**: Dual conical accordion pleat pairs (`PleatConeA`, `PleatConeB`) forming continuous flexible bellows instead of floating toruses.

### C. Two-Tier Vertex Wear & Carbon Soot Extinction (`VehiclePBR_VertexWear.shader`)
- **Two-Tier Wear**: Dark oxidized primer boundary outline (`#2c2724`, `wear 0.12-0.50`) transitioning into bare bright hardened alloy steel ridge (`#c7ccd6`, `wear > 0.42`), simulating true multi-layer paint chipping.
- **Carbon Soot Specular Extinction**: Soot scaling suppresses specular smoothness (`smoothness *= (1.0 - soot * 0.65)`) and reduces metallic reflection (`metallic -= soot * 0.40`), ensuring muzzles and exhausts render as velvety carbon-matte.

---

### D. Procedural Weld Seams, Weighted Normals & World-Space Mud (Wave 8)
- **Scalloped Weld Seams (`make_weld_seam_bead` / `.add_weld_seams()`)**: Generates physical weld beads along plate joints with alternating fish-scale puddle cones and 45° smoothing.
- **Weighted Normals Export**: `merge_meshes_by_material` applies `shade_smooth_by_angle(38°)` and `apply_weighted_normals(weight=60, keep_sharp=True)` on every exported batch.
- **Ground Mud & Dust**: `VehiclePBR_VertexWear.shader` procedurally accumulates terrain dust/mud on chassis and wheels below `_MudHeight` (0.55m).
- **RNM Micro-Detail**: `BlendNormalsRNM` blends micro-steel texture with Toksvig anti-shimmering at distance.
- **Addon Tooling**: `tools/Setup-BlenderAuthoringAddons.ps1` configures `EdgeFlow`, `DreamUV`, and `Ucupaint` in Blender 4.5.14.

---

## References & Specialized Guides

- [Art Style & Palette Guide](references/art-style-and-palette.md) — grounded form language, mechanical construction checks, faction palettes, scale-aware edge treatment, and LOD/readability rules.
- [Blender Procedural Techniques](references/blender-procedural-techniques.md) — Parametric `bpy` recipes for chamfered hulls, continuous tracks, stepped road wheels, and engine grilles.
- [Unity Material Pipeline](references/unity-material-pipeline.md) — URP material configuration, emission keywords, GPU instancing limits, and card sprite rules.
- [Native Hierarchy & Pivots](references/native-hierarchy-and-pivots.md) — Transform hierarchy maps for `PCXBaseTank`, `BombBike`, `ArtilleryTank`, `GatlingWalker`, and `LightningTank`.
