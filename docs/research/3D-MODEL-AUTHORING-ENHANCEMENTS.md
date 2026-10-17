# Rogue Command 3D Model Authoring — Enhancements & Procedural Architecture

**Updated:** 2026-10-17  
**Repository:** Mod Rogue Command  
**Applies to:** `.agents/skills/rogue-command-3d-model-authoring`, `tools/RogueModelKit.py`, `tools/generate_unit_model.py`  
**Tooling Baseline:** Blender 4.5.14 LTS (pinned in `.tools/Blender-4.5.14/`), Unity 2021.3.45f2 (pinned in `unity/RogueCommandAssetAuthoring/`)

---

## 1. Executive Summary & Purpose

This document records the design rationale, mathematical models, procedural assemblies, and CLI automation capabilities developed for the **Rogue Command 3D Model Authoring** pipeline across recent sessions.

The primary objective of this authoring toolset is to produce original, production-grade 3D vehicle models and visual overlays that seamlessly blend with Rogue Command's chunky, low-poly hard-surface RTS aesthetic (**feneq** art direction). The authoring engine is completely deterministic, 100% script-driven in Python via Blender's `bpy` API, and runs locally without external cloud dependencies, paid services, or proprietary 3D generation subscriptions.

### Invariant & Scope Safeguards
- **Zero Content Pollution**: Authoring enhancements, new archetypes, and CLI tools do not add unrequested unit balance rows, prefab registrations, or entries into `content.json`.
- **Unity Project Integrity**: The Unity authoring project at `unity/RogueCommandAssetAuthoring/` is strictly preserved, including `ProjectSettings`, package manifests, and version control configurations.
- **Draw Call Discipline**: The kitbashing procedural workflow combines 50–120 individual mechanical parts into 4–6 unified submeshes batched by material, ensuring low draw calls in Unity.
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
- Calculates vertex normal angles against neighboring geometry and local ground clearance.
- Darkens crevices and undercarriages, storing the shadow scalar directly into the `Color` vertex attribute (`Mesh.colors32`).
- Seamlessly readable by Unity's Standard Shader or custom low-poly unlit shaders with zero runtime texture memory overhead.

### C. Standardized Unity Locator Sockets (`create_unity_locator_sockets`)
To eliminate manual socket placement in Unity for weapons, effects, and pivots, the builder automatically generates standardized empty GameObjects exported into the FBX hierarchy:
- `Socket_MuzzlePrimary`: Primary barrel tip / missile exit point oriented along firing vector.
- `Socket_MuzzleSecondary`: Secondary coaxial or pintle weapon socket.
- `Socket_TurretPivot`: Root transform for turret rotation and pitch controllers.
- `Socket_Exhaust_L` / `Socket_Exhaust_R`: Engine exhaust pipe coordinates for smoke/fire particles.
- `Socket_SensorOptics`: Targeting visor coordinates for gimbal rotation and scanning beams.

---

## 4. The 11 Classic RTS Combat Archetypes

The procedural generator supports 11 distinct RTS combat archetypes, covering the full spectrum of armored warfare:

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
    location: Tuple[float, float, float] | Vector,
    length: float = 1.85,
    width: float = 1.25,
    height: float = 0.95,
    tube_radius: float = 0.038,
    frame_mat: bpy.types.Material | None = None,
    roof_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    with_roof_plate: bool = True,
    with_visor: bool = True,
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
    ground_z: float = 0.0,
    inboard_side: int = 1,
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
    accent_mat: bpy.types.Material | None = None,
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
| `--camo` | `none`, `desert`, `urban`, `arctic`, `woodland` | `none` | Camouflage modifier applied to armor plates. |
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
| `--lod` | Flag | `False` | Prints polygon budget analysis and LOD recommendations. |
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

### The 5-Tone Material Architecture

| Material Role | PBR Metallic | PBR Smoothness | Surface Description |
|---|---|---|---|
| **Primary Armor** | 0.35–0.45 | 0.60–0.65 | Main hull plates, sponsons, lower chassis. |
| **Light Armor** | 0.30–0.35 | 0.58–0.62 | Turret cheeks, upper deck, hatch covers. |
| **Mechanical Gunmetal** | 0.74–0.80 | 0.68–0.75 | Cannon barrels, roll bars, suspension, axles. |
| **Tread / Rubber** | 0.08–0.18 | 0.28–0.34 | Track shoes, road wheels, tire rubber. |
| **Warning Accent** | 0.16–0.20 | 0.60–0.65 | High-contrast identification stripes, battery rails. |
| **Emissive Tech/Optics** | 0.15–0.25 | 0.75–0.85 | **HDR ×1.5–2.5** sensor lenses, headlamps, arc discharge rings. |

### Faction Palette Definitions

- **Vanguard (Industrial Slate Teal)**:
  - Primary Armor: `(0.18, 0.24, 0.28)`
  - Light Armor: `(0.32, 0.38, 0.42)`
  - Warning Accent: High-visibility hazard amber `(0.85, 0.52, 0.10)`
  - Emissive Optics: Electric cyan `(0.12, 0.85, 0.95)`
- **Siege (Heavy Rust Ochre)**:
  - Primary Armor: `(0.28, 0.19, 0.14)`
  - Light Armor: `(0.42, 0.30, 0.22)`
  - Warning Accent: Safety caution yellow `(0.92, 0.72, 0.12)`
  - Emissive Optics: High-intensity amber `(0.95, 0.48, 0.08)`
- **Syndicate (Covert Deep Cobalt)**:
  - Primary Armor: `(0.12, 0.14, 0.22)`
  - Light Armor: `(0.22, 0.26, 0.36)`
  - Warning Accent: Crimson alert red `(0.88, 0.18, 0.15)`
  - Emissive Optics: Plasma violet `(0.72, 0.25, 0.95)`
- **Arctic (Sub-Zero Tundra Navy)**:
  - Primary Armor: `(0.35, 0.39, 0.44)`
  - Light Armor: `(0.55, 0.58, 0.62)`
  - Warning Accent: Safety orange `(0.90, 0.42, 0.08)`
  - Emissive Optics: Glacial blue `(0.20, 0.65, 0.98)`

---

## 9. Quality Verification & Diagnostic Invariants

Before any model is exported to FBX or built into an AssetBundle, `validate_model_mesh_quality` evaluates four geometric invariants:

1. **Ground Clearance Verification**:
   - Ground vehicles (`tank`, `walker`, `bike`, `halftrack`, `super_heavy`, `buggy`, `spider`): Lowest vertex $Z_{\min} \in [-0.02\text{m}, 0.08\text{m}]$.
   - Airborne / Floating vehicles (`hover`, `vtol`): Lowest vertex $Z_{\min} \le 0.55\text{m}$.
2. **Polygon Budget Health**:
   - Real-time RTS budget targets $1,500$ to $5,000$ triangles per unit.
   - The `--lod` CLI flag computes triangle distributions across chassis, weapons, and wheels, warning if any assembly exceeds budget.
3. **Material Slot Hygiene**:
   - Verifies that zero meshes contain empty or unassigned material slots.
4. **Multi-Perspective Orthographic Studio**:
   - Automatically renders 4 perspectives with AgX tone mapping:
     - `ISOMETRIC`: Standard 45° combat perspective.
     - `TOP_DOWN`: Plan view for minimap clarity and drafting silhouette.
     - `FRONT`: Frontal glacis and width proportions.
     - `SIDE`: Wheelbase, suspension, and clearance balance.

---

## 10. Summary of Files & Locations

| Resource | Path | Description |
|---|---|---|
| Skill Definition | `.agents/skills/rogue-command-3d-model-authoring/SKILL.md` | Authoring guide, rules, and workflows. |
| Procedural Kit | `tools/RogueModelKit.py` | Complete geometric assembly functions and builder class. |
| CLI Generator | `tools/generate_unit_model.py` | Headless execution CLI with presets, camo, and LOD checks. |
| Art Style Guide | `.agents/skills/.../references/art-style-and-palette.md` | Palette recipes, silhouette squint tests, and chamfer rules. |
| Blender Recipes | `.agents/skills/.../references/blender-procedural-techniques.md` | `bpy` code snippets for procedural assemblies. |
| Native Pivots Guide | `.agents/skills/.../references/native-hierarchy-and-pivots.md` | Transform hierarchies for `PCXBaseTank`, `BombBike`, etc. |
| Unity Material Guide | `.agents/skills/.../references/unity-material-pipeline.md` | Standard shader setup, emission keywords, and batching. |
