---
name: rogue-command-3d-model-authoring
description: "Design, create, or refine original 3D vehicle and unit visuals for Rogue Command with Blender and the pinned Unity authoring project. Use alongside unit and AssetBundle skills when the task changes a model or its in-game appearance."
---

# Rogue Command 3D Model Authoring

Use this skill to design, model, texture, and export original, high-readability 3D vehicle models and visual overlays for Rogue Command. It guides the visual pipeline—from procedural low-poly blockout in Blender 4.5.14 LTS to Unity 2021.3.45f2 AssetBundle authoring, material assignment, ImageGen card handoff, and multi-tier verification.

---

## Verified implementation boundaries

Treat current source and pinned Unity import evidence as authoritative. The CLI exposes 19 mobile archetypes, five strategic structure types, 14 presets, and nine weapon choices. `--lod` reports polygon-budget diagnostics; `--lods` builds the LOD chain. The editable `.blend` retains canonical locator empties and antenna parenting, and is saved before vertex-wear baking and material batching. `--camo` builds Blender node-based patterns: `splinter` and `digital` use Voronoi, `tigerstripe` uses wave textures, and `desert`, `urban`, `arctic`, `woodland`, and `jungle` use procedural noise. The helper rejects unsupported style IDs. These node graphs use Object coordinates; continuity across separate meshes depends on compatible origins or a shared reference object. `--biome` selects an authoring-time palette alias when `--camo` is `none`; it does not apply dynamic in-game biome adaptation. Unity appearance requires the relevant atlas to be baked and imported, and is not runtime biome behavior. Vertex-color channels require a compatible Unity shader to produce their intended appearance. Authored archetypes and visual rigs do not grant flight, weapon logic, or native controller behavior to a cloned gameplay unit.

For material import and prefab construction, follow sections 6–7 of [Unity Material Pipeline](references/unity-material-pipeline.md). For exact unprefixed socket names and runtime visual behavior, follow section 4 of [Native Hierarchy & Pivots](references/native-hierarchy-and-pivots.md). The procedural authoring changes and their runtime limits are recorded in sections 9–21 of `docs/research/3D-MODEL-AUTHORING-ENHANCEMENTS.md`.

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

Rogue Command (by **feneq**) features a distinctive chunky, hard-surface low-poly aesthetic combining Kenney-inspired modularity with high-clarity RTS gameplay.

```
+-----------------------------------------------------------------------+
| TIER 1: Macro Silhouette (70% of visual read)                         |
| - Bold volume, aspect ratio, stance, ground clearance                  |
| - Must pass the "Squint Test" in < 250ms at 45° RTS elevation          |
+-----------------------------------------------------------------------+
                                  |
                                  v
+-----------------------------------------------------------------------+
| TIER 2: Meso Mechanical Geometry (20% of visual read)                  |
| - Continuous track loops, turret cheeks, engine louvers, exhaust ports |
| - Conveys unit propulsion, armor grade, and weapon function            |
+-----------------------------------------------------------------------+
                                  |
                                  v
+-----------------------------------------------------------------------+
| TIER 3: Micro High-Contrast Accents (10% of visual read)               |
| - Emissive sensor optics, hazard warning stripes, muzzle collars       |
| - Focuses the player's eye on active sensors and weapon hardpoints     |
+-----------------------------------------------------------------------+
```

### The 4 Core Visual Rules:
1. **The Squint Test**: Zoom out the Blender orthographic viewport until the unit is roughly 50 pixels tall. If its archetype (Scout, Strike Bike, Siege Tank, Mech) and forward direction are not instantly recognizable, simplify the internal geometry and exaggerate the primary silhouette masses.
2. **Single-Segment Chamfers (The Specular Glint Rule)**: Razor-sharp 90° edges have zero surface area facing the sun and look digital and dead. Always apply a 1-segment bevel (`segments = 1`, `width = 0.02m–0.06m`) with flat shading (`polygon.use_smooth = False`). These angled chamfers catch bright specular glints under directional lights at RTS distance.
3. **Continuous Track Contour**: Avoid flat box treads with floating blocks. Model tracks with sloped front approach angles (~25°–35° climb), round return idlers, and individual tread shoes (`0.15m–0.22m` spacing) along the ground and return runs.
4. **Recessed Glowing Optics**: Never leave sensor glass or headlights as flat paint. Recess lenses inside an armored visor hood and activate Unity's `_EMISSION` keyword with HDR multipliers (`1.5×–2.2×`) so they glow through the game's bloom filter.

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

### Core Procedural Generator Suite (`RogueModelKit`)
Use the tested, high-readability procedural assemblies rather than ad-hoc mesh primitives:

| Category | Generator Function | Visual Subsystem Produced |
|---|---|---|
| **Chassis & Turret** | `make_sloped_glacis_hull` | Armored wedge chassis with sloped frontal glacis and belly clearance |
| | `make_faceted_turret_housing` | Octagonal turret shell with sloped mantlet cradle and rear bustle counterweight |
| | `make_troop_compartment` | Armored rear troop cabin for APCs and halftracks with clamshell doors and cupola |
| | `make_tubular_rollcage_assembly` | Heavy tubular roll cage frame with A/B pillars, roof crossbars, and sun visor |
| **Mobility & Legs** | `make_continuous_track_assembly` | Full track unit (articulated belt, road wheels, sprockets, cleats, and fenders) |
| | `make_quad_track_pod_assembly` | Independent continuous track pod for 4-track super-heavy crawlers |
| | `make_all_terrain_wheel_assembly` | Rugged chevron-lug tactical tire, deep-dish rim, planetary hub, and coilover shock |
| | `make_bipedal_digitigrade_leg` | Kinematic walker leg: hip joint, hydraulic thigh, knee armor, ankle struts, 3-toed foot |
| | `make_quad_crawler_leg` | Articulated spider/crawler leg: hip actuator, femur ram, knee knuckle, tibia strut, ground pad |
| | `make_hover_skimmer_assembly` | Anti-grav repulsor cushion, ventral thruster bells, stabilizer strakes, vectoring exhausts |
| | `make_ducted_tiltrotor_assembly` | Aerodynamic ducted tilt-rotor pod with shroud cowl, spinner hub, blades, and swivel mount |
| | `make_outrigger_stabilizer_legs` | Deployable hydraulic stabilizer outrigger arms with vertical rams and pivoting footpads |
| **Heavy Weapons** | `make_coaxial_cannon_assembly` | Coaxially aligned tank gun with breech collar, thermal sleeve, bore evacuator, and muzzle brake |
| | `make_dual_cannon_assembly` | Synchronized twin cannon mantlet with dual rifled barrels, thermal sleeves, and muzzle brakes |
| | `make_heavy_howitzer_assembly` | Large-bore siege mortar / howitzer with reinforced breech, recoil cylinders, and sector elevation gear |
| | `make_rotary_gatling_cannon` | 6-barrel circular Gatling cluster with drive axle, clamp collars, and ammo feed chute |
| | `make_missile_launcher_pod` | Armored pod with upward elevation, recessed tube array, warheads, and backblast vents |
| | `make_arc_energy_emitter` | Futuristic weapon with glowing plasma cathode, induction coils, and convergent discharge prongs |
| | `make_railgun_assembly` | Dual parallel electromagnetic kinetic rails with capacitor energy bank and accelerator core |
| | `make_quad_flak_autocannon_assembly` | High-cadence anti-air 2x2 flak autocannon cluster with flash hiders, ammo feed boxes, and cradle |
| | `make_plasma_mortar_assembly` | Heavy siege mortar with 3 magnetic toroidal accelerator rings, radiator coils, and base collar |
| **Optics & Greebles** | `make_flight_deck_assembly` | Armored flight / launch deck with landing guidance centerline stripes, tie-downs, and catwalks |
| | `make_heavy_excavator_drum` | Industrial rotary cutting excavator drum with tungsten carbide teeth and hydraulic mounting arms |
| | `make_gimbal_sensor_turret` | 2-axis FLIR targeting pod with azimuth base, elevation yoke, and dual optical lenses |
| | `make_recessed_sensor_visor` | Armored brow hood with deep cavity and glowing HDR sensor slit lenses |
| | `make_smoke_launcher_rack` | Angled multi-tube defensive smoke grenade discharger bank on mounting bracket |
| | `make_radar_dish_or_rotodome` | Command/recon radar antenna: parabolic dish with feed horn or elliptical rotodome |
| | `make_side_sponson_assembly` | Armored external ammo panniers or auxiliary fuel drums with retention straps |
| | `make_engine_deck_grille` | Armored engine intake frame with angled cooling louvers |
| | `make_armor_applique_array` | Array of Explosive Reactive Armor (ERA) tiles with standoff mounting bolts |
| | `make_hex_bolt_array` | Low-poly 6-sided bolt heads placed along armor seams and flanges |
| | `make_dozer_blade` | Heavy frontal ram blade with hydraulic push rams |
| | `create_unity_locator_sockets`| Conditional canonical socket families for weapons, pivots, sensors, effects, hardpoints, antennae, and thrusters; it does not emit a fixed socket count |
| | `generate_collision_hulls` | Low-poly physics collision hulls (`<Prefix>_COLLIDER_Hull`, `<Prefix>_COLLIDER_Turret`) |
| | `generate_lod_chain` | Multi-level LOD mesh hierarchy (LOD0 100%, LOD1 50%, LOD2 22%) for Unity `LODGroup` |
| | `make_wreckage_variant` | Procedural burned wreckage model with dislodged turret, drooped barrel, and charred armor |
| | `export_gameplay_metadata` | Companion `.gameplay.json` with `visualRootPath`, collider bounds, turret kinematics, and sockets |

### Draw-Call Optimization via Material Batching & Smart Smoothing
Kitbashing produces 50–120 individual objects. Unbatched exports can create many Unity renderers and render submissions. Pass compatible geometry through `batch_and_join_by_material(objects, prefix, preserve_names, smooth_by_angle=True, smooth_angle_deg=38.0)` before export:
- **Material Batching**: Combines compatible meshes by PBR material while preserving essential animated pivots (`TurretBase`, `MainBarrel`). Output mesh groups depend on input materials and preserved objects. This Blender-side step does not establish the final Unity draw-call count.
- **Smart Angle-Weighted Smoothing**: Uses Blender 4.5's `shade_smooth_by_angle` to produce silky smooth round barrels, road wheels, hubs, and exhaust pipes, while preserving razor-sharp 45°/90° chamfers on flat armor plates.
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
    .set_archetype("super_heavy")
    .set_wear_level("battle_hardened")
    .add_sloped_hull(length=3.8, width=2.8, height=1.05)
    .add_quad_track_pods(pod_length=1.65, pod_height=0.62)
    .add_engine_deck(slat_count=5, with_exhausts=True)
    .add_turret(size=(1.85, 2.1, 0.58), center_y=0.15)
    .add_dual_cannon(caliber=0.082, length=1.65, spacing=0.38, elevation_deg=11.0)
    .add_sensor_visor()
    .add_radar_dish()
    .add_side_sponsons()
    .add_smoke_launchers(tube_count=3)
    .add_spring_antenna()
    .add_colliders(include_turret=True)
    .add_unity_locators()
)

# Automated validation, lightmap UVs, vertex cavity dirt, smart smoothing, .blend, batched FBX
diagnostics = builder.build_and_export(
    fbx_path=FBX_PATH,
    blend_path=BLEND_PATH,
    preview_path=PREVIEW_PATH,
    render_multiview=True,
    smooth_by_angle=True,
    smooth_angle_deg=38.0,
    bake_vertex_dirt=True,
    include_locators=True,
    generate_lods=True,
    generate_colliders=True,
    wear_level="battle_hardened",
    gameplay_meta_path=GAMEPLAY_JSON_PATH,
    wreckage_fbx_path=WRECKAGE_FBX_PATH,
    visual_root_path="Vehicle",
    profile_budget=True,
    target_budget=4500,
)
```

### Scriptable CLI Tool (`generate_unit_model.py`)
Run headless vehicle generation directly from the command line using Blender and the CLI generator script:

```powershell
.\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\generate_unit_model.py -- `
    --name ColossusFortress `
    --archetype super_heavy `
    --faction siege `
    --camo desert `
    --weapon dual_cannon `
    --wear battle_hardened `
    --sponsons `
    --smoke `
    --radar `
    --dirt `
    --locators `
    --lods `
    --colliders `
    --gameplay-json `
    --wreckage `
    --lod `
    --multiview
```

- **Mobile Archetypes (19)**: `tank`, `walker`, `bike`, `artillery`, `arc_tank`, `hover`, `spider`, `vtol`, `halftrack`, `super_heavy`, `buggy`, `flak_anti_air`, `siege_crawler`, `drone_carrier`, `quad_mech`, `combat_harvester`, `colossus_titan`, `hover_cruiser`, and `heavy_dropship`.
- **Strategic Structure Types (5)**: `factory`, `defense_turret`, `radar_station`, `energy_silo`, and `orbital_silo`. These are generated through the structure builder, separate from the 19 mobile archetypes.
- **Weapons (9 CLI values)**: `cannon`, `gatling`, `missile`, `arc`, `howitzer`, `dual_cannon`, `railgun`, `quad_flak`, and `plasma_mortar`. These are the exact values accepted by `--weapon`; descriptive names such as autocannon or missile pod are not CLI aliases.
- **Factions**: `vanguard` (slate teal), `siege` (rust ochre), `syndicate` (deep cobalt), `arctic` (tundra navy).
- **Camouflage Variants**: `digital`, `splinter`, and `tigerstripe` build procedural Voronoi/wave color patterns; `desert`, `urban`, `arctic`, and `woodland` use a procedural noise pattern with faction colors. These Blender node graphs can be baked to the atlas. They do not adapt to the map's runtime biome.
- **Biome Palette Alias**: `--biome desert|arctic|woodland|urban` applies the corresponding authoring-time color palette only when `--camo` remains `none`; it does not adapt to a map's runtime biome.
- **Surface Wear Levels**: `pristine` (factory clean), `field_tested` (moderate edge scratches and road grime), `battle_hardened` (heavy combat gouges, exhaust soot, and cavity dirt).
- **Presets (14 Total)**:
  - `siege_breaker`: Heavy assault vehicle preset for breaking fortified positions.
  - `recon_interceptor`: Fast recon and interception vehicle preset.
  - `gunship_alpha`: VTOL gunship preset.
  - `apc_enforcer`: Armored personnel carrier preset.
  - `dune_raider`: Fast desert raider preset.
  - `colossus_titan`: Colossal bipedal war walker with dual heavy cannons, pauldrons, radar, and whip antenna.
  - `sea_lance`: Syndicate heavy hover cruiser gunboat with railgun turret, sponsons, and recon radar.
  - `sky_dreadnought`: Heavy VTOL assault dropship with multi-tube missile pods, tilt-rotors, and whip antenna.
  - `orbital_defense`: Strategic planetary defense structure with orbital launch silo and gantry tower; selects the structure route and defaults to `orbital_silo`.
  - `sky_sweeper`: Flak anti-air with quad flak autocannons, radar dish, and outriggers.
  - `dreadnought_crawler`: Siege crawler with heavy electromagnetic railgun and 4 outriggers.
  - `hive_carrier`: Drone carrier with wide flight deck, bridge tower, rotodome, and missile defense.
  - `titan_quad`: Quad mech with heavy rotary gatling guns and articulated spider legs.
  - `resource_behemoth`: Combat harvester with front rotary excavator drum and ore hoppers.
- **Optimization & Engine Flags**:
  - `--lods`: Generates multi-level LOD chain (LOD0 100%, LOD1 50%, LOD2 22%) and exports `_LODs.fbx`.
  - `--colliders`: Generates low-poly physics collision boxes (`_COLLIDER_Hull`, `_COLLIDER_Turret`).
  - `--gameplay-json`: Exports companion `.gameplay.json` with kinematics, collider bounds, sockets, and damage triggers.
  - `--wreckage`: Exports battle-damaged / burned wreckage variant `_Wreckage.fbx` and `-wreckage.blend`.
  - `--target-budget <int>`: Sets target triangle budget for subsystem profiling (default: 4000).
  - `--bake-atlas`: Bakes Albedo, MetallicGloss, Normal, and Emission maps. A non-`none` camouflage choice also requests an atlas.
  - `--bake-normals`: Requests the same full PBR atlas bake; the pipeline always emits the normal map with the atlas.
  - `--single-material`: Exports `<Name>_Atlas.fbx` with one material slot and enables atlas baking. This does not guarantee one Unity draw call.
  - `--camo-scale <0.1–10.0>` and `--camo-contrast <0.0–4.0>`: Adjust procedural pattern scale and color separation. Defaults are `1.0`.
  - `--panel-lines` / `--no-panel-lines`: Enable or disable the procedural Blender bump relief (default enabled); `--normal-intensity <0.0–4.0>` sets its strength. Bake the atlas to carry this surface detail into Unity textures.
  - `--report-json <path>`: Exports structural diagnostics with polygon counts, estimated batching/draw-call data, LODs, colliders, and subsystem breakdown.
  - `--dry-run`: Validates CLI parameters and prints planned vehicle configuration without running Blender meshing operations.
  - `--batch-all-archetypes`: Builds all 19 archetypes sequentially into the output directory with a tabular console summary.
- **Micro-Detail & Greeble CLI Flags**:
  - `--antenna`: Adds flexible whip antenna with stepped spring coils and canonical `Socket_Antenna` (with runtime spring-damper wobble dynamics).
  - `--cupola`: Adds armored commander's vision cupola with periscopes and split hatch.
  - `--rivets`: Adds perimeter rivet rows and beveled weld seams along armor plate edges.
  - `--track-shoes`: Adds discrete geometric track shoe links along the ground run.
  - `--biome <name>`: Selects an authoring-time palette alias (`desert`, `arctic`, `woodland`, `urban`) when `--camo` is `none`; no runtime map adaptation is performed.
- **Subsystem & Palette CLI Flags**:
  - `--outriggers`: Adds deployable hydraulic stabilizer outriggers.
  - `--flight-deck`: Adds armored flight / drone launch deck with guidance stripes.
  - `--excavator-drum`: Adds heavy front rotary cutting excavator drum.
  - `--primary-color <hex>`: Custom hex override for primary armor (e.g. `#1f3a52`).
  - `--light-armor-color <hex>`: Custom hex override for light armor (e.g. `#3b5f78`).
  - `--emissive-color <hex>`: Custom hex override for emissive glow (e.g. `#00ffee`).
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
    mesh_smooth_type="OFF",
    path_mode="AUTO",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=False,
    add_leaf_bones=False,
    use_custom_props=False
)
```

*See tested geometric algorithms and recipes in [blender-procedural-techniques.md](references/blender-procedural-techniques.md).*

---

## 5. Native Hierarchy & Template Alignment

Rogue Command gameplay components (`EntityController`, weapons, combat physics) live on native prefabs. Mod models are non-destructive visual overlays loaded via `visualRootPath`.

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

Follow this strict progression when creating or modifying 3D models:

1. **Blender Visual Preview**:
   - Run the Python builder in Blender:
     ```powershell
     .\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\Build-<UnitName>Model.py
     ```
   - Inspect `artifacts/previews/<unit-name>.png` for a complete uncropped silhouette, ground contact, bevel specular highlights, and emissive accents.
2. **Unity Scaffolding & AssetBundle Build**:
   - Run the visual scaffolder to build and reopen the bundle in Unity 2021.3.45f2.
   - Verify zero missing scripts, valid mesh renderers, and zero null material slots.
3. **Offline Reference Audit**:
   - Run `tools/validate_content_bundle_references.py content/packs/<pack-id>` to verify all manifest asset names resolve within the built AssetBundle.
   - Run `dotnet run --artifacts-path artifacts/validation-tests --project tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj -- .` to ensure registry, balance, and localization invariants pass and keep build outputs in ignored `artifacts/`.
4. **Steam Runtime Validation**:
   - Static inspection, Blender rendering, and Unity reopening provide **local authoring evidence**.
   - Only a Rogue Command session launched through Steam can prove that the model loads, instantiates without clipping, and renders correctly during combat. Record runtime evidence in `docs/research/AGENT-HANDOFF.md` using the categories established there and follow the BepInEx MCP skill when available.

---

## 9. Advanced Procedural Capabilities (Atlas Baking, Rigging, VFX & Unity Bridge)

The extended authoring pipeline provides four production-grade optimization and gameplay pillars:

### A. Texture Atlas Baking & Single-Material Export (`--bake-atlas`, `--single-material`)
- **Non-Overlapping Atlas UV Unwrapping**: `unwrap_atlas_uvs(objects, island_margin=0.02)` projects smart UV coordinates with parameterized island padding.
- **Fast Cycles PBR Baking**: Headless 1-sample Cycles baking generates four synchronized PBR texture maps in `<Project>/Models/Textures/`:
  - `<Name>_Albedo.png`: Diffuse color map with faction palette, camouflage patterns, and wear baked in.
  - `<Name>_MetallicGloss.png`: Linear PBR data with RGB metallic and alpha smoothness, consumed by the custom URP vehicle shader.
  - `<Name>_Normal.png`: Tangent-space normal map baking geometric bevels and surface relief.
  - `<Name>_Emission.png`: Isolated emissive mask for glowing sensors, plasma cores, and optics.
- **Single-Material Mesh Output**: `--single-material` joins batched geometry into `<Name>_Atlas.fbx` with one material slot. This does not guarantee one draw call per unit: the prefab builder prefers an animated primary FBX, then a valid `_LODs.fbx`, and only then the atlas; Unity passes and render settings also affect runtime submissions.

### B. Skeletal Armature Rigging & Procedural Animation Clips (`--rig`, `--animate`)
- **Kinematic Bone Hierarchy**: `generate_procedural_armature(model_name, objects)` builds a complete mechanical armature:
  `Bone_Root` $\rightarrow$ `Bone_Hull` $\rightarrow$ `Bone_TurretYaw` $\rightarrow$ `Bone_TurretPitch` $\rightarrow$ `Bone_BarrelRecoil` (with suspension bones when applicable).
- **Zero-Stretching Rigid Vertex Weights**: Vertices for each mechanical assembly are assigned strictly to their corresponding vertex group with weight `1.0`, ensuring zero elastic deformation during weapon traverse or chassis recoil.
- **Baked NLA Action Clips**: Keyframes four procedural RTS animations directly inside the FBX:
  1. `Action_Idle`: Subtle engine vibration and idle chassis breathing.
  2. `Action_Drive`: Longitudinal chassis pitch suspension sway and track bounce.
  3. `Action_FireRecoil`: Instantaneous backward barrel recoil slide with progressive damper recovery.
  4. `Action_Death`: Catastrophic chassis sag, suspension collapse, and turret tilt.

### C. Procedural VFX & Energy Shields (`--vfx-flash`, `--vfx-thruster`, `--shield-bubble`)
- **Muzzle Flash Meshes (`make_muzzle_flash_mesh`)**: 12-vertex star-spike conical blast geometry with high-intensity emissive material aligned to `Socket_MuzzlePrimary`.
- **Exhaust Plumes (`make_thruster_plume_mesh`)**: Conical emissive jet plumes for hovercraft cushions, VTOL lift ducts, and rocket boosters.
- **Geodesic Shield Bubble Envelopes (`make_shield_bubble_mesh`)**: Subdivided geodesic bubble envelope tagged as `<Name>_ShieldBubble` for energy barrier visuals. Automatically excluded from ground contact tests.

### D. Unity Prefab & AssetBundle Bridge (`VehiclePrefabBuilder.cs`)
- **Automated C# Editor Scaffolder**: `unity/RogueCommandAssetAuthoring/Assets/Editor/VehiclePrefabBuilder.cs` bridges procedural Blender output and `.gameplay.json` into game-ready Unity prefabs.
- **Automatic Asset Wiring**:
  - Selects the primary FBX when it contains animation clips, otherwise a valid `_LODs.fbx`, then `<Name>_Atlas.fbx`, the primary FBX, or the legacy asset path. A single-material atlas is not always the selected prefab source.
  - Configures TextureImporters (e.g. `TextureImporterType.NormalMap`).
  - Requires `RogueCommand/VehiclePBR_VertexWear` for atlas materials and fails with an actionable error if that URP shader is unavailable; it does not fall back to Built-in Standard. It configures material textures and emission keywords. Instancing is enabled only on the no-atlas material path and is not a batching guarantee.
  - Converts `_COLLIDER` mesh renderers to box colliders and disables those renderers. If none were converted, it adds the compound `BoxCollider` bounds from `.gameplay.json` (translating Blender Z-up to Unity Y-up).
  - Reuses matching socket transforms found recursively in the FBX hierarchy; it creates a root-level fallback only when no matching socket exists.
  - Generates `LODGroup` with transition thresholds (0.50, 0.20, 0.05) if multi-level LOD meshes are present.
- **Headless Batchmode Execution**:
  ```powershell
  & "C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe" -batchmode -quit -projectPath "unity\RogueCommandAssetAuthoring" -executeMethod RogueCommandExpansion.Authoring.VehiclePrefabBuilder.BuildPrefabFromCommandLine -gameplayJson "artifacts\<Name>.gameplay.json"
  ```

---

## 10. Novel Authoring Capabilities (Tactical Structures, Attachments, Damage States & Icon Baker)

The toolchain introduces four novel procedural capabilities expanding authoring beyond mobile vehicle units into RTS base infrastructure, modular unit customization, battle degradation, and tactical UI assets:

### A. Tactical Structures & Production Factories Suite (`--structure`, `--building-type`)
Addresses the strict modding rule: *Every added unit must declare its own namespaced `productionBuilding`*.
- **`RogueStructureBuilder`**: Procedural construction system sharing the core PBR material engine, bevel physics, and batching pipeline with strict ground contact invariance ($Z_{\min} = 0.0000\text{m}$).
- **Supported Building Types**:
  1. `factory`: Heavy industrial production facility featuring a reinforced concrete foundation slab, primary factory hall with corrugated siding, blast-resistant rolling bay door, heavy yellow gantry crane with structural crossbeam, and dual roof ventilation/exhaust stacks with rain caps. Includes `Socket_Deploy_Unit` spawn empty.
  2. `defense_turret`: Static defense installation featuring a stepped octagonal concrete bunker base, armored traverse collar, rotating turret cap with sensor slit, and integrated heavy weapon mount (`cannon`, `quad_flak`, `railgun`).
  3. `radar_station`: Strategic recon and intelligence installation featuring an octagonal command bunker, reinforced roof collar, four-leg structural lattice mast, and motorized parabolic radar dish array with feeder horn.
  4. `energy_silo`: Heavy reactor and resource containment vessel featuring a foundation slab, 4 heavy diagonal buttress support legs, central cylindrical reactor vessel, 3 stepped magnetic containment rings, and quad emissive plasma conduit channels.
  5. `orbital_silo`: Strategic orbital defense installation with a reinforced launch aperture, blast doors, umbilical gantry, and exhaust trench.
- **CLI Usage**:
  ```powershell
  python tools/generate_unit_model.py --structure --building-type factory --name VanguardFactory --colliders --gameplay-json
  ```

### B. Modular Attachments & Hardpoint Customization System (`--attachments`, `--export-detached-attachments`)
Parametric armor and tactical upgrade assemblies designed for runtime modularity or static unit variants:
- **Available Attachment Types**:
  - `era`: Explosive Reactive Armor (ERA) brick matrix mounted on angled standoff brackets along glacis plates or turret cheeks (`make_era_armor_tiles`).
  - `aps`: Active Protection System (APS) hardkill suite comprising dual multi-angle millimeter-wave radar detection pods and twin 3-tube rapid countermeasure mortar launchers (`make_aps_hardkill_system`).
  - `sponson`: Flank-mounted armored weapon sponson pod with protective mantlet housing and auxiliary autocannon barrels (`make_sponson_pod`).
  - `fuel_drums`: Rear-mounted twin cylindrical auxiliary fuel drums or high-capacity capacitor cells with armored retaining straps and feeder fuel lines (`make_aux_fuel_drums`).
- **Detached Asset Export (`--export-detached-attachments`)**: Exports individual modular FBXs (`<Name>_Attachment_<Type>.fbx`) and excludes those meshes from the base FBX, its atlas, LOD chain, and generated base collision hulls. Existing tracked hulls are regenerated from base geometry when necessary. Detached parts keep their source materials/UVs; Unity mounts them in an identity root-level `RCL_AttachmentGroup_<Type>` using vehicle-space FBX transforms, and the runtime director toggles that group while the physical hardpoint remains in place. For older prefabs where a hardpoint socket itself is the visual, ERA/fuel VFX are emitted from an active entity-root parent at the socket's captured world pose before the socket is hidden.
- **CLI Usage**:
  ```powershell
  python tools/generate_unit_model.py --name VanguardTank --attachments era,aps,sponson,fuel_drums --export-detached-attachments
  ```

### C. Progressive Battle Damage States (`--damaged-state`)
Provides visual representation of combat degradation at intermediate health ($25\% - 50\%$ HP) prior to complete wreckage:
- **`make_damaged_state`**: Generates `<Name>_Damaged.fbx` and editable `<name>-damaged.blend`:
  - **Dented & Distorted Armor Plates**: Glacis and side skirt panels receive randomized angular deformation and inward vertex displacements representing heavy kinetic impact craters.
  - **Jammed & Tilted Turret**: Turret ring sustains traumatic yaw jam with a $4^\circ - 6^\circ$ off-axis tilt and traverse disruption.
  - **Displaced Suspension & Road Wheels**: Road wheels or idlers receive lateral camber distortion and track pin displacement.
  - **Procedural Scorched & Fractured Materials**: Overwrites damaged surfaces with `Combat_Scorched` (charred soot, darkened albedo, heightened roughness) and `Combat_DamagedMetal` materials while preserving intact components.
- **CLI Usage**:
  ```powershell
  python tools/generate_unit_model.py --name VanguardTank --damaged-state
  ```

### D. Tactical Minimap & UI Silhouette Icon Auto-Generator (`--render-icons`, `--icon-size`, `--icon-elevation`)
Automated headless rendering pipeline generating transparent alpha PNG tactical icons for RTS HUDs, unit production cards, and minimap blips:
- **`render_tactical_icons`**: Sets headless Cycles/Eevee camera with `film_transparent = True`, RGBA output, and a calibrated 3-point RTS studio lighting rig (Key Sun 4.2 energy, Fill Sun 1.8 energy, Rim Sun 2.4 energy).
- **Dual Orthographic Projections**:
  1. `Top-Down Zenith (90°)`: Pure vertical orthographic projection highlighting the vehicle's planar perimeter footprint, ideal for minimap blips, radar sweeps, and tactical map symbols.
  2. `Isometric RTS (Exact Elevation)`: Mathematically exact trigonometric RTS projection ($Z = D_{xy} \tan(\theta)$, default $45.0^\circ$ elevation, $45^\circ$ azimuth) framing the complete silhouette, ideal for unit cards, selection HUDs, and tech tree icons. Custom elevation angles can be configured with `--icon-elevation <float>`.
- **Resolution Scaling**: Supports `--icon-size 64` (minimap) and `--icon-size 128` (HUD card) exported directly to `<Project>/Models/Textures/Icons/`.
- **CLI Usage**:
  ```powershell
  python tools/generate_unit_model.py --name VanguardTank --render-icons --icon-size 64 --icon-elevation 45.0
  ```

---

## 11. RuntimeVisualDirector Integration (Static Implementation; Steam Validation Pending)

The loader contains a `RuntimeVisualDirector` component (`RogueCommandExpansionLoader.Runtime.Visual.RuntimeVisualDirector`) that is attached by a Harmony postfix on `EntityFactory.InstantiateEntity` when a spawned object exposes supported sockets or animation state. It requires no serialized custom MonoBehaviour in AssetBundles and resolves pinned game members verified through Reverse Engineer Anything (REA). Static source and binary inspection do not prove Unity prefab import, visual animation, or behavior in a Steam-launched match; treat the table below as implemented code paths that still require runtime validation.

### A. Socket-to-Gameplay Runtime Binding Map

| Authored Socket | Intended Runtime Behavior in the Current Code |
| :--- | :--- |
| `Socket_TurretPivot` | **Procedural Target Tracking**: Source code tracks the entity's hostile target in horizontal azimuth at 120°/s using native target properties (`CurrentAttackTarget`, `EntityAttack._attackTarget`, and `CurrentFollowTarget`). It applies the configured low-health tilt at $\le 25\%$ HP. `Socket_TurretYaw` remains a legacy alias; do not emit both names for one role. |
| `Socket_RadarMast` | **Radar Sweep**: Source code rotates this role around its local up axis at 30 RPM. `Socket_RadarDish` remains a legacy alias; do not emit both names for one role. |
| `Socket_DamageSmoke` | **50% HP Damage Transition**: Spawns volumetric smoke particles when native `HealthRatio <= 0.50f` and switches to `<Name>_Damaged` mesh (or applies procedural soot tint via `MaterialPropertyBlock` if no variant exists). Jettisons auxiliary `FuelDrums`. |
| `Socket_DamageFire` | **25% HP Critical Seizure**: Spawns flame particles (cloning native `EntityController.onFireParticleSystem`) and dynamic point light, burns out APS, and tilts `Socket_TurretPivot` to simulate traverse ring seizure. |
| `Socket_Deploy_Unit` | **Factory Rollout Animation**: Detects active unit production via `InProductionCount > 0` and `productionHasStarted` / `queuedCount > 0`, smoothly sliding bay door upward by 1.4 m during production rollout. |
| `Socket_RecoilPrimary` | **Physical Barrel Recoil**: Autonomous explosive linear slide backward (0.22 m) upon weapon firing triggered by tracking `EntityAttack._timeOfLastShot`, recovering smoothly via spring-damper lerp. |
| `Socket_MuzzlePrimary` | **Combat Flash Feedback**: Pulses the authored star-spike mesh (or procedural flash burst) for 0.07 s synchronously with `_timeOfLastShot`. |
| `Socket_Hardpoint_<Type>` | **Dynamic Armor & Hardpoint Lifecycle**: Automatically mounts detached attachments (`ERA`, `APS`, `Sponsons`, `FuelDrums`). Detonates ERA tiles on heavy impact ($\le 75\%$ HP) and restores them upon field repairs ($> 75\%$ HP). |

### B. Resilient Visual Particle Fallback (`RuntimeVisualVfxFallback`)
Combat particle systems (smoke, fire, muzzle flash) utilize a resilient dual-tier engine:
1. **Tier 1 (Native Engine Discovery & Cloning)**: Directly clones `EntityController.onFireParticleSystem` for entity fires and searches memory for loaded Rogue Command particle prefabs (`Smoke`, `Fire`) to guarantee authentic visual harmony with native game art.
2. **Tier 2 (Self-Contained Procedural Fallback)**: If native prefabs are uninstantiated, dynamically generates standalone Unity `ParticleSystem` components with soft unlit alpha blending, upwards cone expansion, and color ramps.

### C. Binary Verification via REA
Verified against `Assembly-CSharp.dll` (SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`, Evidence ID: `ev_eea143512c436bb6579559b439d25e7356d59246194b33404955c85560424097`, Member Evidence: `ev_49344cfdc968be6996f6c1d4f83a49c8d853dd4ff305661523dda1b6e739fb6c`) and loader `RogueCommandExpansionLoader.dll` (SHA-256 `495b8f0666db0914893b5b28d9cb7dd8bf0ddfb9a020d3082359e9e6ce999b29`, MVID `77ebdb64-bfc5-4f52-a3b5-f442daf03949`, Evidence ID: `ev_dc9b898fc2552c8ab3b2e691082815ba0ad7e3db370fbc3c4a62fb397d5a28f8`).

| Property / Method | Declaring Type | Verified CIL Token | Functionality in RuntimeVisualDirector |
| :--- | :--- | :--- | :--- |
| `Single HealthRatio` | `EntityController` | `0x060007e4` | Normalizes entity health to drive 50% smoke, 25% fire, and turret tilt damage states |
| `Boolean DestroyHasBeenStarted` | `EntityController` | `0x0600077d` | Confirms catastrophic entity demise to trigger zero-allocation particle teardown |
| `Nullable<Int32> InProductionCount` | `EntityController` | `0x060007df` | Queries factory production queue (`valuetype:173<i4>`) to drive animated rollout bay doors |
| `EntityController CurrentAttackTarget` | `EntityController` | `0x060007fc` | Primary weapon target resolution for procedural turret traverse tracking |
| `Boolean IsEnemy(EntityController)` | `EntityController` | `0x06000844` | Authoritative hostility check to ensure turrets only target hostile opposing entities |

---

## References & Specialized Guides

- [Art Style & Palette Guide](references/art-style-and-palette.md) — 3-tier silhouette hierarchy, 5-tone palette sets, bevel physics, and greeble standards.
- [Blender Procedural Techniques](references/blender-procedural-techniques.md) — Parametric `bpy` recipes for chamfered hulls, continuous tracks, stepped road wheels, and engine grilles.
- [Unity Material Pipeline](references/unity-material-pipeline.md) — URP material configuration, emission keywords, GPU instancing limits, and card sprite rules.
- [Native Hierarchy & Pivots](references/native-hierarchy-and-pivots.md) — Transform hierarchy maps for `PCXBaseTank`, `BombBike`, `ArtilleryTank`, `GatlingWalker`, and `LightningTank`.



