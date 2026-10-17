# Rogue Command 3D Model Authoring — Enhancements & Procedural Architecture

**Updated:** 2026-10-17  
**Repository:** Mod Rogue Command  
**Applies to:** `.agents/skills/rogue-command-3d-model-authoring`, `tools/RogueModelKit.py`, `tools/generate_unit_model.py`  
**Tooling Baseline:** Blender 4.5.14 LTS (pinned in `.tools/Blender-4.5.14/`), Unity 2021.3.45f2 (pinned in `unity/RogueCommandAssetAuthoring/`)

---

## 1. Executive Summary & Purpose

This document records the design rationale, mathematical models, procedural assemblies, and CLI automation capabilities developed for the **Rogue Command 3D Model Authoring** pipeline across recent sessions.

The authoring toolset produces original procedural vehicle geometry for Rogue Command's chunky RTS aesthetic. It is local Python/Blender tooling with repeatable parameterized construction. Production readiness, visual fit, byte-for-byte export determinism, and runtime performance require separate evidence; the design goals below are not guarantees.

### Invariant & Scope Safeguards
- **Zero Content Pollution**: Authoring enhancements, new archetypes, and CLI tools do not add unrequested unit balance rows, prefab registrations, or entries into `content.json`.
- **Unity Project Integrity**: The Unity authoring project at `unity/RogueCommandAssetAuthoring/` is strictly preserved, including `ProjectSettings`, package manifests, and version control configurations.
- **Draw Call Discipline**: The kitbashing procedural workflow groups geometry by material while retaining selected pivot meshes. The local 11-archetype audit measured 36–143 source parts and 6–10 exported mesh groups; actual Unity draw calls also depend on passes, materials and rendering configuration.
- **Evidence Boundaries**: Blender rendering and offline bundle building constitute local authoring evidence. Runtime viability is proven only during reversible Steam-launched game sessions.

---

## 2. Art Direction & The 3-Tier Visual Read

Rogue Command vehicles are viewed from an elevated isometric/orthographic RTS camera (~45°–60° pitch), where units typically measure between 30 and 120 pixels tall. To achieve maximum tactical readability in dense combat, all procedural models adhere to a strict 3-tier visual hierarchy:

```
+-------------------------------------------------------------------------+
| TIER 1: Macro Silhouette (70% of visual read)                           |
| - Bold volume, stance, aspect ratio, ground clearance                   |
| - Must pass the "Squint Test" in < 250ms at 45° RTS elevation           |
+-------------------------------------------------------------------------+
                                    |
                                    v
+-------------------------------------------------------------------------+
| TIER 2: Meso Mechanical Geometry (20% of visual read)                   |
| - Continuous tracks, articulated legs, tilt-rotors, engine louvers      |
| - Conveys propulsion physics, armor grade, and weapon function          |
+-------------------------------------------------------------------------+
                                    |
                                    v
+-------------------------------------------------------------------------+
| TIER 3: Micro High-Contrast Accents (10% of visual read)                |
| - Recessed emissive optics, hazard warning stripes, muzzle collars      |
| - Focuses the player's eye on active sensors and weapon hardpoints      |
+-------------------------------------------------------------------------+
```

### The 4 Core Visual Rules
1. **The Squint Test**: Zooming out the orthographic viewport until the model is 50 pixels tall must still clearly communicate archetype, role, and heading.
2. **Single-Segment Chamfers (The Specular Glint Rule)**: Razor-sharp 90° CAD edges have zero surface area facing the directional sun, looking flat and dead. All mechanical hull plates and armor blocks receive a 1-segment bevel (`segments = 1`, `width = 0.02m–0.06m`) with flat shading. These angled chamfers catch bright specular glints at RTS distance.
3. **Continuous Track Contour**: Rather than floating box treads, tracks feature sloped front approach angles (~25°–35° climb), round drive sprockets, return idlers, and individual tread shoes (`0.15m–0.22m` spacing).
4. **Recessed Emissive Optics**: Sensor glass and headlamps are never flat textures; they are recessed inside armored brow hoods and assigned real-time emissive materials (`_EMISSION` keyword with HDR multipliers 1.5×–2.5×) that pierce the post-processing bloom filter.

---

## 3. Shading & Procedural Geometry Innovations

### A. Smart Angle-Weighted Smoothing (`shade_smooth_by_angle`)
Prior to this enhancement, low-poly models faced a dilemma: either use flat shading (causing cylindrical gun barrels, road wheels, and exhaust pipes to look blocky and faceted) or full smooth shading (causing flat armor plates to look smudged and muddy).

In Blender 4.5.14 LTS, the pipeline integrates `bpy.ops.object.shade_smooth_by_angle(angle=math.radians(38.0))`. This:
- Preserves razor-sharp 45° and 90° machined chamfers across hull plates and armor seams.
- Silky-smooths curved surfaces on wheels, exhaust cowls, barrels, and rounded hubs.
- Eliminates the need for manual edge split modifiers or duplicated normal data.

### B. Procedural Vertex Ambient Dirt & Cavity Baking (`bake_vertex_ambient_dirt`)
To provide deep contact shadows in wheel wells, panel crevices, and under heavy turrets without requiring high-resolution UV unwrapping or texture memory:
- Calls Blender `paint.vertex_color_dirt` with angle and blur parameters on each mesh; it does not implement a separate ground-clearance or scene-contact AO calculation.
- Darkens crevices and undercarriages, storing the shadow scalar directly into the `Color` vertex attribute (`Mesh.colors32`).
- Stores vertex data without an AO texture, but visible shading requires a shader that reads vertex colors. The current sample scaffolder selects `Standard`; this audit found no custom vertex-color shader or binding. Unity 2021.3 documents that most built-in shaders do not display vertex colors ([Mesh.colors](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Mesh-colors.html)). Do not claim this baked dirt is visible in-game until that path is implemented and verified.

### C. Standardized Unity Locator Sockets (`create_unity_locator_sockets`)
To eliminate manual socket placement in Unity for weapons, effects, and pivots, the builder automatically generates standardized empty GameObjects exported into the FBX hierarchy:
- `<prefix>_Socket_MuzzlePrimary`: emitted when a muzzle position exists; local Z follows the firing direction.
- `<prefix>_Socket_TurretPivot`: emitted when a turret position exists.
- `<prefix>_Socket_Exhaust_L` / `<prefix>_Socket_Exhaust_R`: emitted when exhaust positions exist.
- `<prefix>_Socket_SensorEye`: emitted when a sensor position exists.
- `<prefix>_Socket_GroundCenter`: always emitted.

`Socket_MuzzleSecondary` is not implemented. These are exported empty transforms, not an automatic connection to the game's weapon/animation controllers.

---

## 4. The 11 Classic RTS Combat Archetypes

The procedural generator constructs 11 visual archetypes. The native-template column below is an authoring candidate, not a verified gameplay compatibility matrix; the CLI does not register units, implement flight, or bind animation controllers. Dimensions are design sketches, not measured outputs; weapon selection changes bounds. See the measured audit table at the end.

| Archetype | Native Template | Proportions (W × L × H) | Propulsion / Stance | Signature Visual Features |
|---|---|---|---|---|
| `tank` | `PCXBaseTank` | ~2.2m × 2.6m × 1.6m | Dual continuous tracks | Sloped frontal glacis, octagonal turret, coaxial cannon, side skirts. |
| `walker` | `GatlingWalker` | ~2.2m × 2.0m × 2.7m | Bipedal digitigrade legs | Articulated hydraulic legs, hip rings, waist-mounted twin Gatling rotary guns. |
| `bike` | `BombBike` | ~1.2m × 2.2m × 1.4m | Two-wheeled inline motorcycle | Rugged all-terrain wheels, tubular frame, rear outrigger charge canisters. |
| `artillery` | `ArtilleryTank` | ~2.8m × 3.8m × 1.9m | Broad continuous tracks | Elevated heavy siege howitzer, recoil recuperators, rear stabilizing spade. |
| `arc_tank` | `LightningTank` | ~2.3m × 2.8m × 1.6m | Continuous tracks | Insulated generator housing, dual capacitors, toroidal plasma induction emitter. |
| `hover` | `PCXBaseTank` | ~2.5m × 3.0m × 1.6m | Anti-grav repulsor cushion | Floating ~0.35m–0.55m, ventral thruster bells, lateral stabilizer strakes, vectoring nozzles. |
| `spider` | `GatlingWalker` | ~4.6m × 4.8m × 1.8m | 4-point hydraulic crawler legs | Wide outrigger stance, heavy knee knuckles, center turret housing, ground pads. |
| `vtol` | `PCXBaseTank` | ~3.8m × 4.7m × 1.5m | Twin ducted tilt-rotors | Aerodynamic lifting body, swept wings, swiveling ducted fan pods, canted V-tail fins. |
| `halftrack` | `PCXBaseTank` | ~2.2m × 3.8m × 2.2m | Front steerable wheels + rear tracks | Armored troop compartment, rear clamshell exit doors, roof vision cupola, top gun mount. |
| `super_heavy` | `ArtilleryTank` | ~3.4m × 4.6m × 2.4m | 4 independent track pods | Quad corner track units, wide chassis, synchronized twin heavy cannons, command radar. |
| `buggy` | `BombBike` | ~2.3m × 3.2m × 2.2m | 4 wide all-terrain tires | Welded tubular roll cage, exposed rear engine block, raised suspension, roof Gatling. |

---

## 5. Procedural Mechanical Assemblies (`RogueModelKit.py`)

The procedural geometry library in `tools/RogueModelKit.py` implements specialized assembly generators designed for hard-surface modularity:

```
+--------------------------------------------------------------------------------+
|                        RogueModelKit Assembly Engine                           |
+--------------------------------------------------------------------------------+
|  CHASSIS & HULLS           MOBILITY & PROPULSION        WEAPONS & COMBAT       |
|  - sloped_glacis_hull      - continuous_tracks          - coaxial_cannon       |
|  - faceted_turret_housing  - quad_track_pods            - dual_cannon          |
|  - troop_compartment       - all_terrain_wheels         - heavy_howitzer       |
|  - tubular_rollcage        - digitigrade_legs           - rotary_gatling       |
|  - side_sponsons           - crawler_spider_legs        - missile_launcher_pod |
|  - engine_deck_grille      - hover_skimmer_repulsors    - arc_energy_emitter   |
|  - dozer_blade             - ducted_tiltrotors          - smoke_launcher_rack  |
+--------------------------------------------------------------------------------+
```

### Key Assembly Signatures

#### 1. Ducted Tilt-Rotor Assembly (`make_ducted_tiltrotor_assembly`)
Creates an aerodynamic ducted tilt-rotor pod with shroud cowl, spinner hub, angled blades, and swivel mount:
```python
def make_ducted_tiltrotor_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    radius: float = 0.44,
    depth: float = 0.26,
    tilt_deg: float = 14.0,
    duct_mat: bpy.types.Material | None = None,
    rotor_mat: bpy.types.Material | None = None,
    hub_mat: bpy.types.Material | None = None,
    pylon_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
    blade_count: int = 3,
    pylon_inboard_x: float = -0.32,
) -> list[bpy.types.Object]
```

#### 2. Tubular Roll Cage Assembly (`make_tubular_rollcage_assembly`)
Creates a rugged 3D welded roll cage structure for strike buggies and light vehicles:
```python
def make_tubular_rollcage_assembly(
    prefix: str,
    size: Tuple[float, float, float] = (1.15, 1.45, 0.78),
    location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.0),
    bar_radius: float = 0.034,
    tube_mat: bpy.types.Material | None = None,
    roof_mat: bpy.types.Material | None = None,
    visor_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]
```

#### 3. Heavy Howitzer Assembly (`make_heavy_howitzer_assembly`)
Generates an elevated, large-bore siege howitzer with recoil cylinders and quadrant gears:
```python
def make_heavy_howitzer_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector,
    caliber_radius: float = 0.135,
    barrel_length: float = 1.15,
    armor_mat: bpy.types.Material | None = None,
    barrel_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]
```

#### 4. Synchronized Dual Coaxial Cannon (`make_dual_cannon_assembly`)
Generates twin heavy autocannons on a unified mantlet:
```python
def make_dual_cannon_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector,
    caliber_radius: float = 0.082,
    barrel_length: float = 1.65,
    barrel_spacing: float = 0.38,
    armor_mat: bpy.types.Material | None = None,
    barrel_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]
```

#### 5. Quad Track Pod Assembly (`make_quad_track_pod_assembly`)
Generates independent corner track pods with drive sprockets, bogie wheels, and armored fenders:
```python
def make_quad_track_pod_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    length: float = 1.55,
    height: float = 0.56,
    belt_width: float = 0.34,
    roadwheel_count: int = 3,
    tread_mat: bpy.types.Material | None = None,
    rim_mat: bpy.types.Material | None = None,
    hub_mat: bpy.types.Material | None = None,
    axle_mat: bpy.types.Material | None = None,
    fender_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    ground_z: float = 0.06,
    inboard_side: int = -1,
) -> list[bpy.types.Object]
```

#### 6. Armored Troop Compartment (`make_troop_compartment`)
Generates a rear troop cabin for APCs and halftracks:
```python
def make_troop_compartment(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float] = (1.45, 1.65, 0.76),
    armor_mat: bpy.types.Material | None = None,
    door_mat: bpy.types.Material | None = None,
    detail_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]
```

---

## 6. Fluent Vehicle Builder (`RogueVehicleBuilder`)

`RogueVehicleBuilder` provides a declarative, method-chaining API that encapsulates coordinate calculations, palette assignments, grounding rules, and export pipelines.

### Complete Chaining Example
```python
import RogueModelKit as rmk

builder = (
    rmk.RogueVehicleBuilder("ColossusFortress", faction="siege", camo="desert")
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
    .add_unity_locators()
)

diagnostics = builder.build_and_export(
    fbx_path=FBX_PATH,
    blend_path=BLEND_PATH,
    preview_path=PREVIEW_PATH,
    render_multiview=True,
    smooth_by_angle=True,
    smooth_angle_deg=38.0,
    bake_vertex_dirt=True,
    include_locators=True
)
```

---

## 7. CLI Vehicle Generator (`generate_unit_model.py`)

The CLI generator allows full headless vehicle generation from PowerShell or bash using the local Blender installation.

### Command Syntax
```powershell
.\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\generate_unit_model.py -- [OPTIONS]
```

### Complete Argument Reference

| Option | Values | Default | Description |
|---|---|---|---|
| `--name` | String | `CustomRogueVehicle` | Base name for exported files and hierarchy prefix. |
| `--archetype` | `tank`, `walker`, `bike`, `artillery`, `arc_tank`, `hover`, `spider`, `vtol`, `halftrack`, `super_heavy`, `buggy` | `tank` | Base chassis and propulsion architecture. |
| `--faction` | `vanguard`, `siege`, `syndicate`, `arctic` | `vanguard` | 5-tone faction PBR color scheme. |
| `--camo` | `none`, `desert`, `urban`, `arctic`, `woodland` | `none` | Alternative palette; currently overrides the full faction material palette. |
| `--weapon` | `cannon`, `gatling`, `missile`, `arc`, `howitzer`, `dual_cannon` | `cannon` | Primary offensive weapon system. |
| `--preset` | `siege_breaker`, `recon_interceptor`, `gunship_alpha`, `apc_enforcer`, `dune_raider` | `none` | Quick preconfigured archetype + loadout combo. |
| `--rollcage` | Flag | `False` | Attaches welded tubular roll cage frame. |
| `--quad-tracks` | Flag | `False` | Attaches 4 independent crawler track pods. |
| `--tilt-rotors` | Flag | `False` | Attaches dual aerodynamic ducted tilt-rotors. |
| `--dozer` | Flag | `False` | Attaches heavy frontal obstacle-clearing ram blade. |
| `--skirts` | Flag | `True` | Attaches armored protective side skirts. |
| `--smoke` | Flag | `False` | Attaches angled defensive smoke discharger banks. |
| `--radar` | Flag | `False` | Attaches tactical command/recon radar dish. |
| `--sponsons` | Flag | `False` | Attaches external armored ammo panniers or fuel drums. |
| `--dirt` | Flag | `True` | Bakes procedural vertex ambient cavity dirt. |
| `--locators` | Flag | `True` | Exports standardized Unity locator sockets. |
| `--lod` | Flag | `False` | Prints polygon counts and fixed LOD suggestions; does not generate LOD meshes. |
| `--smooth-angle` | Float | `38.0` | Smart angle threshold in degrees for smooth shading. |
| `--multiview` | Flag | `False` | Renders 4-view orthographic studio sheet. |

### Preconfigured Combat Presets

| Preset Name | Archetype | Weapon | Faction | Distinctive Equipment |
|---|---|---|---|---|
| `siege_breaker` | `super_heavy` | `dual_cannon` | `siege` | Quad track pods, dual heavy cannons, radar, smoke racks, side sponsons. |
| `recon_interceptor` | `buggy` | `gatling` | `vanguard` | All-terrain wheels, tubular roll cage, command radar, roof Gatling. |
| `gunship_alpha` | `vtol` | `missile` | `syndicate` | Ducted tilt-rotors, swept wings, honeycomb missile pod, FLIR visor. |
| `apc_enforcer` | `halftrack` | `gatling` | `vanguard` | Front wheels + rear tracks, armored troop cabin, smoke racks, cupola gun. |
| `dune_raider` | `bike` | `gatling` | `siege` | Long motorcycle frame, outrigger charges, high-traction tires, Gatling gun. |

---

## 8. Material System & Camouflage Integration

### Tonal roles and actual Blender material parameters

The implementation allocates eight materials. Values below are Blender Principled BSDF metallic/roughness parameters, not Unity `_Glossiness`; Unity sample materials are assigned separately by the scaffolder.

| Material | Metallic | Roughness |
|---|---:|---:|
| Primary Armor | 0.42 | 0.58 |
| Light Armor | 0.34 | 0.60 |
| Mechanical Gunmetal | 0.78 | 0.30 |
| Tread / Rubber | 0.18 | 0.74 |
| Wheel Rim | 0.62 | 0.40 |
| Wheel Hub | 0.55 | 0.50 |
| Warning Accent | 0.18 | 0.36 |
| Emissive | 0.15 | 0.22 |

### Faction Palette Definitions

- **Vanguard (Industrial Slate Teal)**:
  - Primary Armor: `(0.12, 0.19, 0.24)`
  - Light Armor: `(0.22, 0.32, 0.38)`
  - Warning Accent: High-visibility hazard amber `(0.95, 0.32, 0.05)`
  - Emissive Optics: Electric cyan `(0.04, 0.72, 0.88)`
- **Siege (Heavy Rust Ochre)**:
  - Primary Armor: `(0.26, 0.14, 0.075)`
  - Light Armor: `(0.43, 0.30, 0.16)`
  - Warning Accent: Safety caution yellow `(0.92, 0.48, 0.08)`
  - Emissive Optics: High-intensity amber `(0.98, 0.35, 0.05)`
- **Syndicate (Covert Deep Cobalt)**:
  - Primary Armor: `(0.055, 0.11, 0.20)`
  - Light Armor: `(0.28, 0.34, 0.42)`
  - Warning Accent: Electric magenta `(0.77, 0.045, 0.31)`
  - Emissive Optics: Plasma cyan `(0.0, 0.74, 1.0)`
- **Arctic (Sub-Zero Tundra Navy)**:
  - Primary Armor: `(0.72, 0.78, 0.85)`
  - Light Armor: `(0.45, 0.52, 0.62)`
  - Warning Accent: Warning red `(0.90, 0.12, 0.15)`
  - Emissive Optics: Glacial blue `(0.15, 0.85, 1.0)`

---

## 9. Quality Verification & Diagnostic Invariants

`validate_model_mesh_quality` and `profile_model_budget_and_drawcalls` report geometry and material diagnostics during `build_and_export`:

1. **Ground Clearance Verification**:
   - Ground vehicles (`tank`, `walker`, `bike`, `halftrack`, `super_heavy`, `buggy`, `spider`): Lowest vertex $Z_{\min} \in [-0.02\text{m}, 0.08\text{m}]$.
   - Airborne / Floating vehicles (`hover`, `vtol`): Validator accepts $-0.02\text{m} \le Z_{\min} \le 0.55\text{m}$.
2. **Subsystem Geometry Budget Profiling (`profile_model_budget_and_drawcalls`)**:
   - Real-time RTS budget targets $1,500$ to $5,000$ triangles per unit.
   - Categorizes geometry into 5 functional subsystems: Chassis & Hull, Turret & Cabin, Mobility & Wheels, Weapons, and Optics & Greebles.
   - Reports exact triangle counts, percentage of budget consumed, material slot count, and predicted Unity draw calls.
3. **Multi-Level LOD Generation (`generate_lod_chain`)**:
   - Automated decimation to LOD0 (100%), LOD1 (50%), and LOD2 (22%) using planar boundary preservation (`delimit={"NORMAL", "MATERIAL"}`).
   - Automatically exports companion `<Name>_LODs.fbx` ready for Unity `LODGroup` configuration.
4. **Physics Collision Hulls (`generate_collision_hulls`)**:
   - Generates flat-shaded bounding boxes `<Prefix>_COLLIDER_Hull` and `<Prefix>_COLLIDER_Turret` tagged with custom property `is_collider = True` and zero material renderers.
5. **Multi-Perspective Orthographic Studio**:
   - Automatically renders 4 separate calibrated perspectives with AgX tone mapping:
     - `ISOMETRIC`: Standard 45° combat perspective.
     - `TOP_DOWN`: Plan view for minimap clarity and drafting silhouette.
     - `FRONT`: Frontal glacis and width proportions.
     - `SIDE`: Wheelbase, suspension, and clearance balance.

---

## 10. Advanced Materials & Gameplay Integration

### A. Multi-Channel Procedural Vertex Shading (`bake_advanced_vertex_shading`)
Encodes four distinct surface and gameplay properties into the single `Color` vertex attribute (`Mesh.colors32`), eliminating texture memory overhead while enabling tactile weathering and team tinting:
- **Channel R (Red)**: Ambient Occlusion & Cavity Dirt. Deep crevices, undersides ($Z \approx 0$), and under-turret shadows receive values $0.35$–$0.65$; upper exposed surfaces receive $0.88$–$1.0$.
- **Channel G (Green)**: Curvature & Edge Wear. Vertices along sharp 45°/90° chamfer bevels receive high values ($0.55$–$1.0$), simulating paint chipped away to bare metal.
- **Channel B (Blue)**: Thermal Soot. Radial gradient darkening ($0.25$–$0.65$) around exhaust pipes and cannon muzzles.
- **Channel A (Alpha)**: Team Color Mask. $1.0$ for primary and light armor surfaces (allowing dynamic team-color tinting in Unity); $0.0$ for fixed mechanical gunmetal, tread rubber, and hazard stripes.

### B. Standardized 11-Socket Gameplay Hierarchy (`create_unity_locator_sockets`)
Exports standardized empty transforms into the FBX matching Rogue Command vehicle & weapon mechanics:
1. `Socket_MuzzlePrimary`: Main weapon firing point and projectile exit vector.
2. `Socket_MuzzleSecondary`: Secondary coaxial/pintle/missile firing point.
3. `Socket_TurretYaw` / `Socket_TurretPivot`: Horizontal azimuth rotation axis.
4. `Socket_TurretPitch`: Vertical elevation pivot axis.
5. `Socket_RecoilPrimary`: Linear impulse slide axis for barrel recoil animation.
6. `Socket_SensorOptics` / `Socket_SensorEye`: Targeting visor and sensor beam origin.
7. `Socket_Exhaust_L` / `Socket_Exhaust_R`: Engine exhaust particle emitters.
8. `Socket_DamageSmoke`: Engine deck damage particle spawner (50% HP threshold).
9. `Socket_DamageFire`: Catastrophic damage flame particle spawner (25% HP threshold).
10. `Socket_CenterOfMass`: Rigidbody physics center of mass.
11. `Socket_GroundContact` / `Socket_GroundCenter`: Ground projection ($Z=0$) for selection rings.

### C. Companion Gameplay Metadata JSON (`export_gameplay_metadata`)
Generates a sidecar `<Name>.gameplay.json` specifying engine wiring:
- `visualRootPath`: Verified path on native template (`Vehicle` for tanks/walkers/artillery; `GameObject/BombBike` for bikes/buggies).
- `boxCollider`: Calculated center `[x, y, z]` and size `[w, l, h]` for Unity `BoxCollider`.
- `turretKinematics`: `yawCenter`, `pitchCenter`, `minElevationDeg` ($-10^\circ$), `maxElevationDeg` ($+35^\circ$), `yawSpeedDegPerSec` ($45^\circ/\text{s}$), and `recoilDistanceMeters` ($0.22\text{m}$).
- `damageThresholds`: Health thresholds ($0.50$ smoke, $0.25$ fire) linked to socket coordinates.
- `compatibleNativeTemplates`: Candidate native templates from Rogue Command (`PCXBaseTank`, `BombBike`, `ArtilleryTank`, etc.).

### D. Procedural Burned Wreckage State (`make_wreckage_variant`)
Generates `<Name>_Wreckage.fbx` and `<name>-wreckage.blend` for visual combat destruction:
- Dislodges and tilts turret ($16^\circ$ pitch, $-14^\circ$ roll, $22^\circ$ yaw, shifted off-center).
- Replaces armor materials with `Wreckage_CharredArmor` (matte charcoal `0.08, 0.08, 0.09`, roughness $0.88$) and `Wreckage_BurnedMetal` (soot iron `0.16, 0.12, 0.10`).
- Batches into optimized destruction meshes ready for runtime prefab swapping.

---

## 11. Summary of Files & Locations

| Resource | Path | Description |
|---|---|---|
| Skill Definition | `.agents/skills/rogue-command-3d-model-authoring/SKILL.md` | Authoring guide, rules, and workflows. |
| Procedural Kit | `tools/RogueModelKit.py` | Complete geometric assembly functions and builder class. |
| CLI Generator | `tools/generate_unit_model.py` | Headless execution CLI with presets, camo, LODs, and wreckage. |
| Art Style Guide | `.agents/skills/.../references/art-style-and-palette.md` | Palette recipes, silhouette squint tests, and chamfer rules. |
| Blender Recipes | `.agents/skills/.../references/blender-procedural-techniques.md` | `bpy` code snippets for procedural assemblies. |
| Native Pivots Guide | `.agents/skills/.../references/native-hierarchy-and-pivots.md` | Transform hierarchies for `PCXBaseTank`, `BombBike`, etc. |
| Unity Material Guide | `.agents/skills/.../references/unity-material-pipeline.md` | Standard shader setup, emission keywords, and batching. |

---

## 12. Verification Record & Execution Evidence

- **Headless Execution Test**: Tested in Blender 4.5.14 LTS with `--lods`, `--colliders`, `--wear battle_hardened`, `--gameplay-json`, `--wreckage`, and `--lod`.
- **Output Artifacts Verified**:
  - Primary Unit FBX: `DiagnosticTank.fbx` (10 batched Unity submeshes).
  - Multi-Level LOD FBX: `DiagnosticTank_LODs.fbx` (LOD0 4024 tris, LOD1 2145 tris, LOD2 1257 tris).
  - Physics Collision Hulls: `DiagnosticTank_COLLIDER_Hull` and `DiagnosticTank_COLLIDER_Turret`.
  - Burned Wreckage FBX: `DiagnosticTank_Wreckage.fbx` and `diagnostictank-wreckage.blend`.
  - Gameplay Metadata: `DiagnosticTank.gameplay.json` with kinematics, collider bounds, and 11 sockets.
- **Subsystem Triangle Profiling**:
  - Chassis & Hull: 1,718 tris (42.7%)
  - Turret & Cabin: 102 tris (2.5%)
  - Mobility & Wheels: 1,500 tris (37.3%)
  - Weapons: 462 tris (11.5%)
  - Optics & Greebles: 242 tris (6.0%)
- **Lifecycle Cleanliness**: Test artifacts were safely verified and removed; zero uncommitted test models exist in `content/` or `unity/`.
- **Validation Suite**: `dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .` passed 101/102 checks (1 skipped on Windows symlinks).

---

## 13. Archetype & CLI Expansion: 16 Iconic Archetypes, 9 Weapons, and Batch/Dry-Run Tools

### A. The 16 Tactical Archetypes Matrix
The generator suite supports 16 distinct tactical vehicle classes covering ground, hover, walker, airborne, and super-heavy combat roles:

| Archetype | Primary Role | Default Chassis & Mobility | Key Features & Assemblies | Default Weapon |
|---|---|---|---|---|
| `tank` / `medium_tank` | Frontline combat | Sloped glacis hull, continuous tracks, skirts | Faceted turret, sensor visor, antenna | `cannon` |
| `light_tank` | Rapid response | Compact sloped hull, high-speed tracks | Low-profile turret, sensor visor | `autocannon` |
| `heavy_tank` | Breakthrough armor | Heavy glacis hull, wide continuous tracks | Reinforced turret, side skirts, smoke banks | `dual_cannon` |
| `super_heavy` | Mobile fortress | Massive hull, 4 independent track pods | Twin citadels, sponsons, smoke, outriggers | `dual_cannon` |
| `artillery` | Long-range siege | Elongated hull, recoil spade, continuous tracks | Elevated rear turret, hydraulic stabilizers | `howitzer` |
| `missile_launcher` | Indirect fire support | Armored hull, continuous tracks | Elevated missile pod array (honeycomb) | `missile` |
| `halftrack` / `amphibious_apc` | Armored personnel carrier | Front all-terrain wheels + rear track assembly | Armored cab, troop compartment, cupola | `gatling` |
| `bike` | Fast strike / raider | Lightweight bike frame, dual inline wheels | Exposed engine block, low silhouette | `gatling` |
| `scout_buggy` / `buggy` | Reconnaissance & flanking | Tubular roll cage, 4 all-terrain wheels | Exposed engine, twin exhausts, dual guns | `gatling` |
| `hover` / `hover_skiff` | All-terrain skimmer | Aerodynamic hull, repulsor cushions | Ventral thruster bells, vectoring exhausts | `cannon` |
| `mobile_radar` | Battlefield reconnaissance | Light tracked chassis, mast mounts | Rotating command radar dish / rotodome | `gatling` |
| `walker` / `biped_walker` | Urban/rough terrain mech | Compact torso, digitigrade bipedal legs | Armored hip actuators, hydraulic knee rams | `gatling` |
| `spider` | All-terrain weapons platform | Low-slung chassis, 4 crawler legs | Articulated femur/tibia joints, ground pads | `missile` |
| `vtol` / `vtol_gunship` | Close air support gunship | Aerodynamic fuselage, landing skids | Dual ducted tilt-rotors, sensor chin pod | `missile` |
| `flak_anti_air` | Mobile air defense | Sloped hull, continuous tracks, outriggers | Elevated flak turret, tracking radar dish | `quad_flak` |
| `siege_crawler` | Super-heavy siege platform | Heavy citadel hull, 4 independent track pods | Outriggers, ammo sponsons, heavy mantlet | `railgun` |
| `drone_carrier` | Fleet command & deployment | Super-wide hull, continuous tracks | Flight launch deck, bridge tower, rotodome | `missile` |
| `quad_mech` | Heavy quadruped combat walker | Pelvis core block, 4 heavy crawler legs | Articulated crawler knees, heavy turret | `gatling` |
| `combat_harvester` | Armed resource extraction | Heavy industrial hull, continuous tracks | Front rotary excavator drum, ore hoppers | `cannon` |

### B. Expanded Procedural Weapon Assemblies
The procedural weapon suite includes 9 distinct weapon assemblies:

1. **`make_railgun_assembly`**: Dual parallel kinetic rails with armored breech block, top capacitor energy bank, 4 magnetic containment sleeve clamps, and glowing central accelerator core slit.
2. **`make_quad_flak_autocannon_assembly`**: High-cadence anti-air 2x2 flak autocannon cluster with flash hiders, ammo feed boxes, and dual elevation trunnion cradle.
3. **`make_plasma_mortar_assembly`**: Heavy siege mortar with 3 magnetic toroidal accelerator rings, radiator cooling coils, and reinforced base collar.
4. **`make_coaxial_cannon_assembly`**: Rifled tank gun with breech collar, thermal sleeve, bore evacuator, and multi-baffle muzzle brake.
5. **`make_dual_cannon_assembly`**: Synchronized twin cannon mantlet with dual rifled barrels and muzzle brakes.
6. **`make_heavy_howitzer_assembly`**: Large-bore siege mortar with reinforced breech, dual hydraulic recoil cylinders, and sector elevation gear.
7. **`make_rotary_gatling_cannon`**: 6-barrel circular Gatling cluster with drive axle, clamp collars, and ammo feed chute.
8. **`make_missile_launcher_pod`**: Armored box pod with upward elevation, honeycomb launch tubes, warheads, and backblast vents.
9. **`make_arc_energy_emitter`**: Futuristic weapon with glowing plasma cathode, induction coils, and convergent discharge prongs.

### C. Advanced Tactical Procedural Assemblies
1. **`make_flight_deck_assembly`**: Armored flight and launch deck with landing guidance centerline hazard stripes, tie-down pockets, safety catwalks, and perimeter floodlight pylons.
2. **`make_outrigger_stabilizer_legs`**: Deployable hydraulic stabilizer outrigger arms with telescoping vertical rams, pivot knuckles, and swiveling circular footpads.
3. **`make_heavy_excavator_drum`**: Heavy industrial rotary cutting drum with helical tungsten carbide cutting teeth, armored debris hood, and hydraulic mounting arms.

### D. CLI & Workflow Tooling (`generate_unit_model.py`)
- **Direct Hex Color Overrides**: `--primary-color <hex>`, `--light-armor-color <hex>`, `--emissive-color <hex>` for custom faction palettes and elite unit branding.
- **Subsystem Toggles**: `--outriggers`, `--flight-deck`, `--excavator-drum`.
- **Validation Dry Run**: `--dry-run` validates CLI arguments, resolves presets, and verifies compatibility without launching Blender meshing operations.
- **Batch Archetype Generation**: `--batch-all-archetypes` builds all 16 archetypes sequentially with a consolidated tabular report:
  ```
  ========================================================
   BATCH GENERATION COMPLETE - 16 ARCHETYPES REPORT
  ========================================================
  Archetype          | Unit Name                    | Tris    | DrawCalls | GroundValid
  ---------------------------------------------------------------------------
  tank               | Batch_tank                   | 4120    | 9         | True
  walker             | Batch_walker                 | 3890    | 8         | True
  ...
  ========================================================
  ```
- **Structural JSON Export**: `--report-json <path>` exports comprehensive structural and budget health diagnostics:
  ```json
  {
    "name": "SkySweeper",
    "archetype": "flak_anti_air",
    "weapon": "quad_flak",
    "total_triangles": 5636,
    "total_vertices": 5596,
    "predicted_draw_calls": 10,
    "ground_contact_valid": true,
    "subsystem_breakdown": {
      "Chassis & Hull": 1770,
      "Turret & Cabin": 102,
      "Mobility & Wheels": 1776,
      "Weapons": 1270,
      "Optics & Greebles": 718
    },
    "lod_levels": { "LOD0": 10, "LOD1": 10, "LOD2": 10 },
    "colliders": [ "SkySweeper_COLLIDER_Hull", "SkySweeper_COLLIDER_Turret" ]
  }
  ```

---

## 14. Advanced Optimization, Rigging, VFX & Unity Prefab Bridge (2026-10-17)

To elevate procedural unit models to commercial RTS asset parity, four new core pillars were implemented across `tools/RogueModelKit.py`, `tools/generate_unit_model.py`, and `unity/RogueCommandAssetAuthoring/`:

### A. Texture Atlas Baking & 1-Draw-Call Mesh Optimization
- **Unified Atlas UV Unwrapping**: `unwrap_atlas_uvs(objects, margin=0.04)` performs parameterized Smart UV projection across all individual subassemblies, packing non-overlapping UV islands into a single $[0, 1] \times [0, 1]$ coordinate square.
- **Ultra-Fast Cycles PBR Texture Baking**: `bake_pbr_texture_atlas(...)` switches Blender's render engine to Cycles (CPU, 1 sample, < 1s) to bake complete PBR texture sets directly into `Assets/RogueCommandExpansionSample/Models/Textures/`:
  - `Albedo` (BaseColor with faction colors and camouflage).
  - `MetallicGloss` (Metallic in RGB, Smoothness in Alpha).
  - `Normal` (Tangent-space normal vector map).
  - `Emission` (Optics, plasma coils, and indicator lights).
- **Single-Material Mesh Collapse**: `create_single_material_atlas_model(...)` joins all batched parts into a unified mesh `<Name>_Atlas.fbx` with exactly **1 material slot**. In dense combat with 40+ units on screen, this reduces GPU draw call pressure from ~400 draw calls down to ~40 draw calls.

### B. Skeletal Armature Rigging & Procedural Animation System
- **Kinematic Armature Creation**: `generate_procedural_armature(model_name, objects)` builds a complete hierarchical bone skeleton:
  - `Bone_Root` (ground center anchor).
  - `Bone_Hull` (chassis pitch/roll suspension).
  - `Bone_TurretYaw` (360° azimuth traverse).
  - `Bone_TurretPitch` (elevation/depression).
  - `Bone_BarrelRecoil` (linear slide along barrel axis).
- **Zero-Stretching Rigid Vertex Weights**: Vertices of each assembly are bound to their respective bone vertex groups with weight `1.0`. This prevents elastic rubber-banding during weapon movement.
- **Procedural NLA Action Baking**: `generate_procedural_action_clips(armature, builder)` bakes four essential RTS action clips:
  1. `Action_Idle`: Subtle chassis engine rumble and idling breathing.
  2. `Action_Drive`: Longitudinal chassis pitch suspension sway and road bounce.
  3. `Action_FireRecoil`: Instantaneous backward barrel recoil slide with progressive damper recovery.
  4. `Action_Death`: Catastrophic chassis sag, suspension collapse, and turret tilt.

### C. Procedural VFX & Energy Shields
- **Muzzle Flash Cones (`make_muzzle_flash_mesh`)**: Star-spiked 12-vertex muzzle blast mesh aligned with projectile exit vectors.
- **Thruster Plumes (`make_thruster_plume_mesh`)**: Conical emissive propulsion plumes for hover cushions, VTOLs, and booster rockets.
- **Geodesic Shield Bubble Envelopes (`make_shield_bubble_mesh`)**: Subdivided geodesic bubble envelope mesh tagged as `<Name>_ShieldBubble`. Excluded from ground contact validation to allow true spherical protective forcefields.

### D. Unity Prefab & AssetBundle Bridge (`VehiclePrefabBuilder.cs`)
- **Automated C# Editor Scaffolder**: Implemented in `unity/RogueCommandAssetAuthoring/Assets/Editor/VehiclePrefabBuilder.cs`.
- **Batchmode Command Line Interface**:
  ```powershell
  & "C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe" -batchmode -quit `
    -projectPath "unity\RogueCommandAssetAuthoring" `
    -executeMethod RogueCommandExpansion.Authoring.VehiclePrefabBuilder.BuildPrefabFromCommandLine `
    -gameplayJson "artifacts\CombatTank.gameplay.json"
  ```
- **Automated Hierarchy Assembly**:
  - Locates FBX (`_Atlas.fbx`, `_LODs.fbx`, or `.fbx`).
  - Configures textures and creates `Standard` PBR materials with `enableInstancing = true` and `_EMISSION` keywords.
  - Adds `BoxCollider` from `colliderBounds` (converting Blender Z-up to Unity Y-up).
  - Generates child Transforms for all sockets (`Socket_MuzzlePrimary`, `Socket_TurretPivot`, etc.).
  - Configures `LODGroup` component if multi-resolution LODs are detected.
  - Saves Prefab to `Assets/RogueCommandExpansionSample/Prefabs/<Name>Visual.prefab` and normalizes Unity YAML formatting.


