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
- **Validation Suite**: the latest source audit reports 107/108 checks passed (1 Windows symlink check skipped because the environment lacks the required privilege).

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
- **Unified Atlas UV Unwrapping**: `unwrap_atlas_uvs(objects, island_margin=0.02)` performs parameterized Smart UV projection across all individual subassemblies, packing non-overlapping UV islands into a single $[0, 1] \times [0, 1]$ coordinate square.
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

---

## 15. Novel Procedural Capabilities: Base Structures, Modular Attachments, Battle Damage & Tactical Icon Baker (2026-10-17)

To satisfy the mod architectural rule that *every added unit requires a dedicated production building* and expand authoring into dynamic tactical RTS assets, four novel procedural systems were engineered:

### A. Tactical Structures & Production Factories Suite (`RogueStructureBuilder`)
- **Motivation**: Eliminates dependency on static placeholder cubes for mod factories while adhering to the core modding contract.
- **Architectural Design**:
  - `RogueStructureBuilder` shares the vehicle material palette (`TacticalPalette`), chamfer physics, LOD decimation, and collider generation.
  - Guarantees strict ground clearance invariance ($Z_{\min} = 0.0000\text{m}$) via foundation slab centering at $Z = \text{height}/2$.
- **4 Procedural Structure Types**:
  1. `factory`: Concrete slab base, ribbed industrial production hall, recessed roller bay door, heavy yellow gantry crane with structural crossbeam, dual roof exhaust stacks with ventilation caps, and `Socket_Deploy_Unit` locator.
  2. `defense_turret`: Stepped octagonal concrete bunker pillbox, armored 360° traverse collar, beveled turret cap with vision slit, and integrated weapon mount (`cannon`, `quad_flak`, `railgun`).
  3. `radar_station`: Octagonal comms bunker, roof mounting collar, 4-leg structural lattice mast, and motorized parabolic radar dish with central transceiver feed horn.
  4. `energy_silo`: Heavy reactor vessel, 4 diagonal buttress foundation legs, cylindrical core tank, 3 stepped magnetic containment rings, and 4 high-luminance emissive plasma conduits.
- **CLI Command**:
  ```powershell
  python tools/generate_unit_model.py --structure --building-type factory --name VanguardFactory --colliders --gameplay-json
  ```

### B. Modular Attachments & Hardpoint Customization System
- **Motivation**: Enables procedural unit variations and supports future runtime attachment systems in Unity where players upgrade armor or weapons without replacing the underlying base model prefab.
- **4 Procedural Attachment Assemblies**:
  - `make_era_armor_tiles`: Explosive Reactive Armor (ERA) brick array with standoff bracket mountings (`--attachments era`).
  - `make_aps_hardkill_system`: Active Protection System (APS) dual millimeter-wave radar sensor pods and twin 3-tube countermeasure mortar launchers (`--attachments aps`).
  - `make_sponson_pod`: Armored sponson pod with protective mantlet housing and twin auxiliary autocannons (`--attachments sponson`).
  - `make_aux_fuel_drums`: Rear-mounted cylindrical fuel drums / power cells with dual retaining bracket straps and feed lines (`--attachments fuel_drums`).
- **Detached Asset Export (`--export-detached-attachments`)**:
  - Automatically exports individual attachment models to separate FBXs: `<Name>_Attachment_<Type>.fbx`.
  - Excludes those meshes from the primary model, its baked texture atlas, and generated LOD chain, so disabling the prefab attachment group also removes the visible geometry.
  - Detached exports keep their source materials and UVs; the base model's atlas material is not assigned to them.
- **CLI Command**:
  ```powershell
  python tools/generate_unit_model.py --name VanguardTank --attachments era,aps,sponson,fuel_drums --export-detached-attachments
  ```

### C. Progressive Battle Damage States (`make_damaged_state`)
- **Motivation**: Fills the visual fidelity gap between pristine undamaged units ($100\%$ HP) and complete destruction wreckage ($0\%$ HP).
- **Procedural Degradation Pipeline**:
  - Clones base vehicle model and applies randomized, bounded combat deformation:
    - Armor plates receive localized inward vertex dents and angular twists simulating heavy kinetic impacts.
    - Turret ring receives a jammed $4^\circ - 6^\circ$ off-axis tilt and traverse disruption.
    - Road wheels and idler sprockets sustain camber displacement and skewed rotation.
    - Damaged parts swap materials to `Combat_Scorched` (roughness 0.92, charred soot albedo) and `Combat_DamagedMetal`.
  - Exports `<Name>_Damaged.fbx` and editable `<name>-damaged.blend`.
- **CLI Command**:
  ```powershell
  python tools/generate_unit_model.py --name VanguardTank --damaged-state
  ```

### D. Tactical Minimap & UI Silhouette Icon Auto-Generator (`render_tactical_icons`)
- **Motivation**: Automatically creates crisp, transparent-background 2D graphical assets for radar minimaps, tactical HUDs, and unit production cards directly from 3D models.
- **Headless Dual-Angle Rendering Engine**:
  - Configures headless Cycles/Eevee render camera with `film_transparent = True`, RGBA color mode, and isolated lighting.
  - **Top-Down Zenith (90°)**: Pure vertical orthographic view isolating vehicle footprint for radar pings and minimap blips (`<Name>_Icon_TopDown_<Size>.png`).
  - **Isometric RTS (45°)**: 45° RTS elevation and azimuth perspective highlighting silhouette and weapon posture for unit selection cards (`<Name>_Icon_Isometric_<Size>.png`).
  - Supports `--icon-size 64` and `--icon-size 128` exported directly into `Assets/.../Models/Textures/Icons/`.
- **CLI Command**:
  ```powershell
  python tools/generate_unit_model.py --name VanguardTank --render-icons --icon-size 64
  ```

---

## 16. In-Game Runtime Automation & Reactive Visual Director (2026-10-17)

To bridge the offline 3D authoring pipeline with live gameplay simulation, the expansion loader injects an autonomous visual management subsystem during match execution:

### A. Autonomous Socket Discovery & In-Game Director (`RuntimeVisualDirector`)
- **Injection Hook**: Intercepts `EntityFactory.InstantiateEntity` via Harmony postfix, dynamically attaching `RuntimeVisualDirector` to any spawned unit or structure possessing authoring sockets (`Socket_*`).
- **Zero-Configuration Operation**:
  1. **Progressive Combat Damage States**:
     - At $\le 50\%$ HP: Automatically mounts smoke particle emitters onto `Socket_DamageSmoke` and activates `<Name>_Damaged` mesh variants. Driven by native `HealthRatio` property (`get_HealthRatio` token `0x060007e4`).
     - At $\le 25\%$ HP: Activates dynamic fire particles and point lighting on `Socket_DamageFire`, and introduces a mechanical tilt on `Socket_TurretPivot` to simulate traverse jamming.
     - At $0\%$ HP: Cleans up transient particle emitters cleanly before garbage collection, confirmed via `StillExists == false`, `IsDestroyed == true`, and native property `DestroyHasBeenStarted == true` (token `0x0600077d`).
  2. **Structure Dynamics**:
     - `radar_station`: Spins `Socket_RadarMast` at true 30 RPM (180°/s) continuously.
     - `factory`: Detects active unit production via `EntityController.InProductionCount > 0` (token `0x060007df`) and `ProductionInfo.productionHasStarted` / `queuedCount > 0`, smoothly sliding `Socket_Deploy_Unit` / `Socket_DeployRamp` bay door upward by 1.4 m during rollout.
     - `defense_turret`: Dynamically tracks hostile combat targets in horizontal azimuth at 120°/s using authoritative targets `CurrentAttackTarget` (token `0x060007fc`), `EntityAttack._attackTarget`, and explicitly filters hostility via `EntityController.IsEnemy` (token `0x06000844`).
  3. **Autonomous Weapon Combat Feedback**:
     - Tracks `EntityAttack._timeOfLastShot` (`Single`) in real time. Whenever a unit fires:
       - `Socket_RecoilPrimary`: Simulates explosive barrel recoil slide with spring-damper recovery.
       - `Socket_MuzzlePrimary`: Triggers high-luminance star-spike muzzle flash bursts.
  4. **Modular Hardpoint Hot-Swapping**:
     - Scans `Socket_Hardpoint_*` and provides programmatic API to toggle attached modular armor (`ERA`, `APS`, `Sponsons`, `FuelDrums`).

### B. Dual-Tier Resilient Particle Fallback (`RuntimeVisualVfxFallback`)
- **Tier 1 (Native Engine Discovery & Cloning)**: Directly checks `EntityController.onFireParticleSystem` for unit fire effects and searches memory for loaded Rogue Command particle prefabs (`Smoke`, `Fire`) to guarantee authentic visual harmony with native game art.
- **Tier 2 (Self-Contained Procedural Fallback)**: If native prefabs are uninstantiated, dynamically generates standalone Unity `ParticleSystem` components with soft unlit alpha blending, upwards cone expansion, and color ramps, guaranteeing zero runtime dependency on external asset bundles or proprietary assets.

### C. Low-Level Binary Verification via Reverse Engineer Anything (REA)
The runtime visual director was subjected to binary inspection against `Assembly-CSharp.dll` (SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`) and the compiled loader `RogueCommandExpansionLoader.dll` (SHA-256 `93d62cba0b1ac49db90736d303f6cd0a9b0b98f0d41237daf1265922af222f16`, MVID `a172fe22-39f6-446e-818e-36c240944af9`).
- **Evidence IDs**: `ev_c38acc530bb49ca3bf82a91df86a9c74c5d3ed78ffe88ea57c0aadb74226c975` (loader assembly artifact), `ev_f6327eae071cf0725cfa35ac28bde2d57dff8528bdfd41a668fc7256edafdcf8` (RuntimeVisualDirector members).
- **Corrected Assumptions**: Eliminated fictitious reflection fields (`isProducing`, `DestroyHasBeenStarted` as field) and replaced them with exact CIL properties and authoritative combat clocks (`_timeOfLastShot`).

---

## 17. Static Review Resolutions & Asset Pipeline Rigor (2026-10-17)

Following static code review of the 3D authoring and runtime visual subsystems, the implementation was updated to address integration gaps between Blender, Unity, and the in-game runtime:

### A. PBR Metallic-Gloss Packing & Viewport Material Flow
- **Review Finding**: The texture atlas baker did not bake true metallic maps from Principled BSDF inputs, and the roughness channel was improperly mapped as MetallicGloss without the required smoothness inversion.
- **Solution & Implementation**:
  1. **Dual-Pass Bake & Channel Packing (`bake_pbr_texture_atlas` in `RogueModelKit.py`)**:
     - Metallic is baked via temporary emission shader rerouting using Cycles `type="EMIT"`.
     - Roughness is baked via Cycles `type="ROUGHNESS"`.
     - Output is packed directly into the Unity Standard Shader specification:
       $$\text{RGB} = \text{Metallic}, \quad \text{Alpha} = \text{Smoothness} = 1.0 - \text{Roughness}$$
     - Output is saved as `<Prefix>_MetallicGloss.png`.
  2. **Atlas Material Shader Tree (`create_single_material_atlas_model`)**:
     - Connects `_MetallicGloss.png` texture image node with `colorspace = Non-Color`.
     - Feeds texture Color through `ShaderNodeSeparateColor` with Red channel linked to Principled BSDF `Metallic`.
     - Feeds texture Alpha through `ShaderNodeInvert` linked to Principled BSDF `Roughness`, guaranteeing visual parity between Blender viewports and Unity Standard Shader rendering.

### B. Unity Prefab Builder Collider Conversion & Animator Generation
- **Review Finding**: The Unity prefab builder did not convert authoring collision geometry into Unity `BoxCollider` components, and did not set up `Animator` components for articulated meshes.
- **Solution & Implementation (`VehiclePrefabBuilder.cs`)**:
  1. **Collision Mesh Conversion**:
     - Scans hierarchy for `_COLLIDER` meshes (`_COLLIDER_Hull`, `_COLLIDER_Turret`).
     - Disables visible `Renderer` on collision objects (`r.enabled = false`) and excludes them from atlas material assignment.
     - Adds a native Unity `BoxCollider` sized to `MeshFilter.sharedMesh.bounds` as an approximate collision shape without visual draw calls.
  2. **Automated Animator Setup (`SetupAnimatorIfAvailable`)**:
     - Detects articulated rigs (bones named `Bone_*`, `Armature`), `SkinnedMeshRenderer`, or embedded `AnimationClip`s in the imported FBX.
     - Automatically attaches `Animator` component and binds the FBX avatar.
     - Creates and wires `<UnitName>Controller.controller` in `Assets/.../Animations/`, adding states for all discovered clips with `Action_Idle` (or primary clip) configured as the default state.

### C. Dynamic Combat Lifecycle for Modular Attachments & Damaged Variants
- **Review Finding**: Modular hardpoint attachments and damaged mesh variants were static geometry that did not automatically react to unit health or combat damage.
- **Solution & Implementation (`VehiclePrefabBuilder.cs` & `RuntimeVisualDirector.cs`)**:
  1. **Prefab Hardpoint Auto-Mounting**:
     - `MountModularAttachmentsIfAvailable`: Detects `<UnitName>_Attachment_<Type>.fbx`, normalizes its suffix to the matching `Socket_Hardpoint_<Type>`, and mounts the model at vehicle-root coordinates inside an `RCL_AttachmentGroup_<Type>`. The attachment keeps its own material/UVs; the root-level group is the visual activation target while the physical hardpoint remains in place.
     - `MountDamagedVariantIfAvailable`: Detects `<UnitName>_Damaged.fbx` and instantiates it under the prefab root, initially inactive (`SetActive(false)`).
  2. **Authoritative Health-Driven Lifecycle**:
     - $\le 75\%$ HP: Reactive Armor (`ERA`) hardpoint detonate upon heavy impact, triggering explosive muzzle flash VFX and disabling the armor tile.
     - $\le 50\%$ HP: Switches mesh visibility to `<UnitName>_Damaged` (or applies procedural soot tint via `MaterialPropertyBlock` when no variant is supplied); auxiliary `FuelDrums` jettison and emit smoke.
     - $\le 25\%$ HP: Spawns fire particle emitters, applies mechanical $+4.5^\circ$ seizure tilt on `Socket_TurretPivot`, and burns out Active Protection Systems (`APS`).
     - **Reversible Repairs**: When field repair mechanics restore unit health ($> 50\%$ and $> 75\%$), damaged meshes are deactivated, soot is cleared, turret tilt is reset, fire and smoke are extinguished, and ERA/APS subsystems are restored.

### D. 3-Point RTS Studio Lighting & Calibrated Isometric Elevation
- **Review Finding**: Tactical icon generation lacked dedicated lighting and used an approximate $\approx 30.8^\circ$ pitch angle instead of a true $45.0^\circ$ RTS elevation.
- **Solution & Implementation (`render_tactical_icons` in `RogueModelKit.py` & `generate_unit_model.py`)**:
  1. **Calibrated 3-Point RTS Studio Lighting Rig**:
     - **Key Light**: Sun light from top-left/front (Energy 4.2, warm sunlight `(1.0, 0.98, 0.95)`) for crisp chamfer specular glints.
     - **Fill Light**: Sun light from right/rear (Energy 1.8, cool sky ambient `(0.85, 0.92, 1.0)`) for tread and undercarriage fill.
     - **Rim Light**: Sun light from rear/top (Energy 2.4, pure white `(1.0, 1.0, 1.0)`) for crisp silhouette separation.
     - Cleaned up safely in a `finally` block to prevent lingering lights in user scenes.
  2. **Exact Trigonometric RTS Elevation**:
     - Replaced hardcoded vector offsets with mathematically exact trigonometric positioning:
       $$D_{xy} = M \times 2.2, \quad X = D_{xy} \cos(45^\circ), \quad Y = -D_{xy} \sin(45^\circ), \quad Z = D_{xy} \tan(\theta_{\text{elevation}})$$
     - Parameterized via `elevation_angle_deg` (default $45.0^\circ$ exact) and CLI flag `--icon-elevation <float>`.

### E. Hardpoint, Collider & Animation Integration Corrections
- Detached attachment suffixes are normalized to the prefab hardpoint names (`era` → `ERA`, `aps` → `APS`, `sponson` → `Sponsons`, `fuel_drums` → `FuelDrums`). The exporter removes detached mesh objects from the primary model before material batching, atlas baking, LOD generation, and base collider generation; any previously tracked generated hulls are replaced from the filtered base geometry. The prefab builder mounts them under identity root-level `RCL_AttachmentGroup_<Type>` transforms so vehicle-space object coordinates remain valid; `RuntimeVisualDirector` toggles that group and falls back to the physical hardpoint for older prefabs. With a separate group, ERA/fuel-drum VFX use the active physical socket as their anchor. If an older prefab uses the socket itself as the visual, the runtime captures the socket's world pose and emits VFX under the active entity root before hiding it. Detached materials remain independent of the base atlas. Unknown attachment types fall back to the visual root with a warning.
- The metadata-sized root collider is added only when the imported model has no converted `_COLLIDER` child meshes, avoiding duplicate hull coverage. Generated hull bounds exclude meshes exported separately as detached attachments.
- When the primary FBX contains animation clips, the builder selects it ahead of the static LOD FBX, preserves a valid imported Avatar, configures Idle/Drive clips to loop, and creates locomotion, fire-recoil, and death transitions. `RuntimeVisualDirector` drives those parameters from observed entity movement, the native shot timestamp, and the dead state; fire animation dispatch does not require recoil or muzzle sockets, and loader-prefixed animated units can receive the director without other sockets.
- **Validation status:** these builder and runtime changes have only received static review. Unity import/prefab creation and animation playback in a Steam-launched match remain unverified. Models without embedded clips continue to use the LOD FBX path.

---

## 18. Comprehensive Review Rectifications & Runtime Rigor (2026-10-17)

Following in-depth static and architectural code review of the 3D authoring pipeline, five critical integration discrepancies between Blender output, Unity prefab construction, and the live BepInEx loader runtime director were identified and resolved:

### A. Canonical Socket Naming & Resilient Discovery
- **Discrepancy**: `RogueModelKit.py` previously generated prefixed socket names (`<ModelName>_Socket_DamageSmoke`), while `RuntimeVisualDirector.cs` searched strictly for names beginning with `Socket_` or `RCL_Visual_`. In addition, default vehicle socket generation omitted radar mast/dish mounts and modular armor hardpoints.
- **Resolution**:
  1. In `tools/RogueModelKit.py` and its twin in `.agents/skills/rogue-command-3d-model-authoring/scripts/`, `_add_empty` was updated to assign canonical un-prefixed names (`Socket_DamageSmoke`, `Socket_MuzzlePrimary`, etc.).
  2. `create_unity_locator_sockets` was expanded with parameters `radar_loc`, `hardpoints`, and `include_default_hardpoints=True`, automatically emitting `Socket_RadarMast`, `Socket_RadarDish`, `Socket_Hardpoint_ERA`, `Socket_Hardpoint_APS`, `Socket_Hardpoint_Sponsons`, and `Socket_Hardpoint_FuelDrums`.
  3. Runtime socket discovery is now centralized in `RogueCommandSocketCatalog`: it accepts exact canonical names and legacy `<ModelName>_Socket_<canonical-name>` suffixes only. `RuntimeVisualDirector` no longer chooses turret, radar, deploy, or hardpoint sockets by arbitrary substring matching; explicitly named `RCL_Visual_*` visuals remain a separate attachment signal. If two distinct transforms (including two aliases such as `Socket_TurretPivot` and `Socket_TurretYaw`) map to one behavior role, that role is disabled instead of depending on hierarchy traversal order; other roles on the prefab remain available. An ambiguous detached attachment group does not fall back to the physical hardpoint locator; the locator is used as a legacy visual only if no detached group was authored.

### B. PBR Metallic-Gloss Packing & Linear Importer Pipeline
- **Discrepancy**: The baked `_MetallicGloss.png` texture previously contained roughness in place of packed data; Unity's Standard shader treats `_MetallicGlossMap` as linear data ($R = \text{Metallic}, A = \text{Smoothness}$). In Unity, missing `sRGB = false` flags caused gamma distortion, while omitting `_METALLICGLOSSMAP` prevented texture sampling.
- **Resolution**:
  1. In `RogueModelKit.py`, `bake_pbr_texture_atlas` isolates metallic via emission shader rerouting and inverts roughness ($S = 1.0 - \text{roughness}$), packing $R=M, G=M, B=M, A=S$ into `_MetallicGloss.png`.
  2. In `VehiclePrefabBuilder.cs`, `SetupAtlasTexturesAndMaterial` explicitly configures `TextureImporter` for `_MetallicGloss.png` with:
     - `textureType = TextureImporterType.Default` so the packed channels remain a data texture.
     - `sRGBTexture = false` (linear non-color data)
     - `alphaSource = TextureImporterAlphaSource.FromInput`
     - `alphaIsTransparency = false` so alpha is preserved as smoothness data.
  3. The Standard shader material enables `_METALLICGLOSSMAP`, disables `_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A`, and sets `_GlossMapScale = 1.0f`, so the shader samples metallic from the map's red channel and smoothness from its alpha channel without scaling. In the pinned Unity 2021.3.45f2 Standard shader, `_Metallic` and `_Glossiness` are scalar fallbacks used when the map keyword is disabled; they do not multiply the map values.
  4. Albedo and emission maps are imported as sRGB color textures, while normal maps use the NormalMap importer type. When an optional map is absent, the builder clears its texture property, disables its keyword, and resets the associated material state to prevent a prior build's texture from persisting.

### C. LOD Hierarchy Precedence, Atlas Material Binding & Multi-Asset DB Search
- **Discrepancy**: When both an atlas FBX (`_Atlas.fbx`) and an LOD chain (`_LODs.fbx`) were exported, `VehiclePrefabBuilder.cs` prioritized `_Atlas.fbx`, preventing the creation of Unity's `LODGroup` component. Furthermore, detached attachments and damaged variants were not reliably located across varied folder structures.
- **Resolution**:
  1. `FindModelAsset` now prioritizes `_LODs.fbx` over `_Atlas.fbx`, falling back to standard `.fbx`.
  2. When `_LODs.fbx` is loaded, every discovered LOD renderer (`LOD0`, `LOD1`, `LOD2`) is systematically assigned the shared `atlasMaterial`, and `SetupLodGroupIfAvailable` constructs a functional `LODGroup` with calibrated cull thresholds (50%, 20%, 5%).
  3. `MountModularAttachmentsIfAvailable` and `MountDamagedVariantIfAvailable` refactored to employ `AssetDatabase.FindAssets` scoped across `ModelFolder` and `AssetRoot`, ensuring reliable discovery and automatic parenting of `<Name>_Attachment_<Type>.fbx` and `<Name>_Damaged.fbx` into the prefab.

### D. Runtime Mechanics: Deploy Ramps, True 30 RPM, & Hostility Filtering
- **Discrepancy**: Factory rollout searched for `Deploy_Unit` or `DeployUnit`, while the structure exporter generated `Socket_DeployRamp`. Radar sweep rotated at $30^\circ/\text{s}$ (5 RPM) instead of the documented 30 RPM ($180^\circ/\text{s}$). Turret target tracking lacked explicit hostility verification, risking targeting friendly units.
- **Resolution**:
  1. `make_factory_assembly` in `RogueModelKit.py` now exports both `Socket_Deploy_Unit` and `Socket_DeployRamp`. `RuntimeVisualDirector.cs` accepts `Deploy_Unit`, `DeployUnit`, `DeployRamp`, and `Deploy_Ramp`.
  2. `UpdateRadarSweep()` calibrated from $30^\circ/\text{s}$ to `_radarRpm * 6.0f` ($30 \times 6 = 180^\circ/\text{s}$, achieving exact 30 RPM).
  3. `RuntimeVisualDirector.cs` binds to `EntityController.IsEnemy(EntityController)` (token `0x06000844`) via reflection, implementing `IsHostileTarget(candidate)` to verify that candidates are active, non-destroyed, and hostile before committing turret traverse angles.

### E. Verified CIL Metadata Tokens Against Pinned Assembly
- **Discrepancy**: Earlier documentation cited preliminary tokens for four properties on `EntityController`.
- **Resolution**: Verified through Reflection and REA static inspection against the pinned game binary (`Assembly-CSharp.dll`, SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`):
  - `HealthRatio`: `0x060007e4` (MethodDef: `System.Single get_HealthRatio()`)
  - `DestroyHasBeenStarted`: `0x0600077d` (MethodDef: `System.Boolean get_DestroyHasBeenStarted()`)
  - `InProductionCount`: `0x060007df` (MethodDef: `System.Int32 get_InProductionCount()`)
  - `CurrentAttackTarget`: `0x060007fc` (MethodDef: `EntityController get_CurrentAttackTarget()`)
  - `IsEnemy`: `0x06000844` (MethodDef: `System.Boolean IsEnemy(EntityController)`)
- While `RuntimeVisualDirector` resolves members dynamically by name, maintaining exact documented tokens ensures future binary patches and compatibility audits can detect layout changes immediately.


---

## 19. Next-Gen Tactical Generation Suite: Colossus Titan, Hover Cruiser, Heavy Dropship, Orbital Silo & In-Game Procedural Dynamics (2026-10-18)

To deliver next-generation visual fidelity, RTS tactical depth, and live procedural simulation, the authoring toolkit (`tools/RogueModelKit.py`, `tools/generate_unit_model.py`, and twin skill scripts) and the in-game runtime director (`RuntimeVisualDirector.cs`) were expanded across four major architectural pillars:

### A. Micro-Detail & Visual Fidelity Enhancement Suite
To eliminate low-poly visual monotony and catch high-contrast specular highlights in RTS isometric cameras, four new procedural micro-assemblies were introduced:
1. **Flexible Whip Antenna (`make_whip_antenna` / `add_whip_antenna`)**:
   - Generates an armored mounting bracket, stepped helical spring coils (3 tiers), and an angled carbon-composite mast ($7^\circ - 8^\circ$ backward lean) terminating in a high-visibility hazard tip.
   - Automatically defines `Socket_Antenna` locator, parenting the antenna geometry so the runtime director can apply inertial sway without mesh deformation.
2. **Armored Commander Cupola (`make_crew_cupola_hatch` / `add_crew_cupola`)**:
   - Octagonal beveled vision cupola with 5 armored periscope slits and glowing emissive sensor prisms.
   - Split circular hatch doors with mechanical pivot hinge blocks.
3. **Perimeter Rivets & Beveled Weld Seams (`make_rivets_and_weld_seams` / `add_rivets_and_welds`)**:
   - High-contrast hex bolt heads and low-poly beveled weld seam strips placed along glacis plate edges and hull joints, catching specular highlights under directional sun illumination.
4. **Articulated Track Shoe Cleats (`make_articulated_track_shoes` / `add_track_shoes`)**:
   - Discrete, volumetric track shoes with center guide horns and cross-link pins along ground contact runs, transforming continuous tread meshes into mechanical assemblies.

### B. 4 New Tactical Archetypes
Expanding the vehicle and structure roster to **19 Total Archetypes**:
1. **Colossus Titan (`colossus_titan`)**:
   - Heavy bipedal assault war walker. Features massive digitigrade bipedal legs ($2.30\text{ m}$ hip height, $1.35\text{ m}$ spacing), reinforced pelvis core, citadel torso, shoulder pauldrons, dual rear cooling exhaust stacks, and heavy dual cannon or gatling armament.
2. **Hover Cruiser (`hover_cruiser`)**:
   - Heavy amphibious assault gunboat and skimmer. Streamlined hydrodynamic sloped bow prow ($4.6\text{ m}$ length), dual flank repulsor cushions, armored conning tower, bridge roof, twin rear jet thruster nacelles, and forward superfiring railgun or cannon turret.
3. **Heavy Dropship (`heavy_dropship`)**:
   - Heavy VTOL tactical transport and gunship. Features reinforced cargo fuselage ($3.8\text{ m}$ length), cockpit nose, rear clamshell loading ramp, high wings with ducted vector tilt-rotors, heavy articulated landing gear skids, and multi-tube missile pods.
4. **Orbital Silo Structure (`orbital_silo` via `make_orbital_silo_assembly` / `build_orbital_silo`)**:
   - Strategic planetary defense and rocket launch installation. Octagonal foundation pit, blast berm, inner launch aperture, dual book-opening blast doors with hydraulic rams, vertical umbilical gantry tower, warning beacon, and exhaust trench. Emits canonical `Socket_MuzzlePrimary`, `Socket_Deploy_Unit`, and `Socket_RadarMast`.

### C. Procedural Camouflage & Environmental Biome System
- **Advanced Camouflage Patterns**: Added `digital` (high-contrast pixelated dual-tone), `splinter` (angular geometric fracture camo), and `tigerstripe` (horizontal brushstroke tactical camo) to `get_faction_palette` alongside `desert`, `urban`, `arctic`, and `woodland`.
- **4 Dedicated Presets**: Added `colossus_titan`, `sea_lance`, `sky_dreadnought`, and `orbital_defense` to `PRESETS`.
- **Environmental Biomes**: Added CLI flag `--biome <desert|arctic|woodland|urban>` for automated environmental weathering and texturing.

### D. In-Game Runtime Automation & Procedural Dynamics (`RuntimeVisualDirector.cs`)
The expansion loader was enhanced with dynamic secondary animation and environmental adaptation:
1. **Inertial Spring-Damper Antenna Wobble (`UpdateAntennaWobble`)**:
   - Discovers `Socket_Antenna` dynamically on spawned units.
   - Computes vehicle chassis linear acceleration vector: $\vec{a} = (\vec{v}_t - \vec{v}_{t-1}) / \Delta t$.
   - Drives a 2D damped harmonic oscillator simulating physical inertia:
     $$\ddot{\theta} = -\omega^2 \theta - 2\zeta\omega \dot{\theta} - k_{\text{accel}} (\vec{a} \cdot \hat{u})$$
   - Rotates `Socket_Antenna` local transform in pitch and roll, causing flexible whip antennas to sway convincingly during starting, stopping, and turning.
2. **VTOL Vector Thruster Pitching (`UpdateVtolThrusterVectoring`)**:
   - Discovers `Socket_ThrusterVector_L` and `Socket_ThrusterVector_R`.
   - Reads vehicle local horizontal velocity, pitching thrusters dynamically forward up to $28^\circ$ when cruising and neutral during hover.
3. **Environmental Biome Pipeline**:
   - Biome selection (`--biome <desert|arctic|woodland|urban>`) provides deterministic authoring-time palette and weathering baking directly into model textures and vertex colors, avoiding unverified runtime scene heuristic tinting (as native combat takes place inside the unified `Game` scene without distinct per-biome scene names).
