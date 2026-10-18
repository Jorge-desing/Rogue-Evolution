# Rogue Command 3D Model Authoring — Architectural Specification & Reference

**Updated:** 2026-10-18  
**Repository:** Mod Rogue Command  
**Applies to:** `.agents/skills/rogue-command-3d-model-authoring`, `tools/RogueModelKit.py`, `tools/generate_unit_model.py`, `unity/RogueCommandAssetAuthoring/`  
**Tooling Baseline:** Blender 4.5.14 LTS (pinned in `.tools/Blender-4.5.14/`), Unity 2021.3.45f2 (pinned in `unity/RogueCommandAssetAuthoring/`)

---

## 1. Executive Summary & Design Invariants

The 3D authoring pipeline generates procedural, low-poly, high-readability combat vehicles and tactical structures for Rogue Command.

### Core Invariants
- **URP Render Pipeline**: Rogue Command runs strictly on the **Universal Render Pipeline (URP)**. Shipped vehicles use untextured geometry rendered via `Shader Graphs/EntityColorShader`. Modded AssetBundles must embed compiled URP shaders (`RogueCommand/VehiclePBR_VertexWear`).
- **Zero Content Pollution**: Authoring enhancements, new archetypes, and CLI tools do not add unrequested unit balance rows, prefab registrations, or entries into `content.json`.
- **Unity Project Integrity**: The Unity authoring project at `unity/RogueCommandAssetAuthoring/` is strictly preserved, including `ProjectSettings`, package manifests (`Packages/manifest.json`), and version control metadata.
- **Evidence Boundaries**: Blender rendering and offline bundle building constitute local authoring evidence. Runtime viability is proven only during reversible Steam-launched game sessions.

---

## 2. Art Direction & The 3-Tier Visual Read

Rogue Command vehicles are viewed from an elevated isometric RTS camera (~45°–60° pitch, 30–120 pixels tall). Models adhere to a strict 3-tier visual hierarchy:

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
| - Focuses player attention on active sensors and weapon hardpoints      |
+-------------------------------------------------------------------------+
```

### The 4 Core Visual Rules
1. **The Squint Test**: Zooming out the orthographic viewport until the model is 50 pixels tall must communicate archetype, role, and heading within 250ms.
2. **Single-Segment Chamfers (Specular Glint Rule)**: Razor-sharp 90° CAD edges look flat and dead. Mechanical hull plates and armor blocks receive 1-segment bevels (`segments = 1`, `width = 0.02m–0.06m`) with flat shading to catch bright specular glints at RTS distance.
3. **Continuous Track Contour**: Treads feature sloped front approach angles (~25°–35° climb), round drive sprockets, return idlers, and individual tread shoes (`0.15m–0.22m` spacing).
4. **Recessed Emissive Optics**: Sensor glass and headlamps are recessed inside armored brow hoods with real-time emissive materials (`_EMISSION` keyword with HDR multipliers 1.5×–2.5×) that pierce the post-processing bloom filter.

---

## 3. Shading, Texturing & Vertex Color Contracts

### A. Topological BMesh RGBA Vertex Color Contract
Encodes four surface signals into `Mesh.colors32` using BMesh topological connectivity analysis (`bake_advanced_vertex_shading`):
- **Detection Algorithm**: Calculates dihedral edge angles (`edge.calc_face_angle() >= 35°`) and projects neighboring face normal:
  $$\Delta = (v_{other} - v_{edge}) \cdot \vec{n}_{f1}$$
- **Channel R (Cavity AO / Oclusion)**: $\Delta > 1\times 10^{-4}$ marks concave creases and recesses ($R \in [0.25, 0.40]$). Open/zenith faces receive $1.0$.
- **Channel G (Edge Wear / Desgaste)**: $\Delta < -1\times 10^{-4}$ identifies convex corners, receiving bright steel albedo, metallic $+0.6$, and smoothness $+0.75$ ($G \in [0.7, 1.0]$). Concave and flat faces are strictly $0.0$.
- **Channel B (Thermal Soot / Hollín)**: Base vehicle metal is clean ($B = 0.0$). Scales up to $1.0$ at weapon muzzles ($R = 0.60\text{ m}$) and engine exhausts ($R = 0.45\text{ m}$) with quadratic falloff $(1 - d/R)^2$.
- **Channel A (Team Mask)**: $1.0$ on primary and light armor plates for runtime player/faction tinting; $0.0$ on mechanical parts (gunmetal, treads, hydraulics, optics).

### B. PBR Texture Atlas Baking (`bake_pbr_texture_atlas`)
- **Fast Cycles Baking**: Bakes unified PBR maps into `Assets/RogueCommandExpansionSample/Models/Textures/`:
  - `Albedo`: BaseColor with faction palette or camouflage pattern.
  - `MetallicGloss`: Packed linear data texture ($R = \text{Metallic}, G = \text{Metallic}, B = \text{Metallic}, A = \text{Smoothness} = 1.0 - \text{Roughness}$).
  - `Normal`: Tangent-space normal vector map.
  - `Emission`: Optics, plasma conduits, and indicator lights.
- **Single-Material Export (`_Atlas.fbx`)**: Joins batched parts into a unified mesh with one material slot. (Does not guarantee 1 draw call in Unity).

---

## 4. Multi-System Anchors & Locators (`Socket_*`)

`create_unity_locator_sockets` exports canonical, unprefixed empty transforms (+Z forward, +Y up):

| Socket Identifier | System / Role | Placement & Hierarchy Details |
|---|---|---|
| `Socket_MuzzlePrimary` | Primary Weapon | At weapon muzzle. **Nests child empty `FirePoint_0`** for native `ShootProjectile.Init()`. |
| `Socket_MuzzleSecondary` | Secondary Weapon | At secondary weapon muzzle. Nests child empty `FirePoint_1`. |
| `Socket_EngineAudio` | Propulsion SFX | Centered over engine deck / exhaust midpoint for `EntityFlockingMovement._movementSoundAudioSource`. |
| `Socket_WeaponAudio` | Firing SFX | Positioned at primary weapon muzzle for `ShootProjectile.shotSoundAudioSource`. |
| `Socket_FootContact_L/R` | Biped Locomotion | Positioned under left/right foot pads at ground plane $Z = 0.0$ for step dust VFX/SFX. |
| `Socket_FootContact_FL/FR/RL/RR` | Quad Locomotion | Positioned under 4 crawler foot pads at ground plane $Z = 0.0$. |
| `Socket_DamageSmoke` | $\le 50\%$ HP VFX | Centered on exhaust manifold, with fallback to turret ring / upper hull when exhausts absent. |
| `Socket_DamageFire` | $\le 25\%$ HP VFX | Positioned near fuel cells or reactor core. |
| `Socket_TurretPivot` | Azimuth Traverse | At turret ring rotation center. (Aliased to `Socket_TurretYaw`). |
| `Socket_RadarMast` | Tactical Radar | At radar mast mounting collar. Sweeps at 30 RPM ($180^\circ/\text{s}$). |
| `Socket_Antenna` | Comms Whip | At antenna base spring mount. Driven by inertial spring-damper wobble. |
| `Socket_ThrusterVector_L/R` | VTOL Propulsion | Left/right tilt-rotor pivot, vectoring forward dynamically with flight velocity. |
| `Socket_Hardpoint_ERA` | Reactive Armor | Glacis and turret cheek mounts for explosive reactive armor tiles. |
| `Socket_Hardpoint_APS` | Active Protection | Turret roof mounts for active countermeasure radar/mortar systems. |
| `Socket_Hardpoint_Sponsons` | Flank Sponsons | Lateral chassis mounts for auxiliary weapon pods or ammo bins. |
| `Socket_Hardpoint_FuelDrums`| Auxiliary Fuel | Aft hull mounts for jettisonable auxiliary fuel drums. |

---

## 5. Skeletal Mech Armatures & In-Place Locomotion

### A. Parametric Armature Creation (`generate_procedural_armature`)
- **Biped Walkers (`walker`, `colossus_titan`)**: 8-bone hierarchy (`Bone_Root`, `Bone_Pelvis`, `Bone_Hull`, `Bone_Thigh_L/R`, `Bone_Shin_L/R`, `Bone_Foot_L/R`, `Bone_TurretYaw`, `Bone_TurretPitch`, `Bone_BarrelRecoil`).
- **Quadrupeds (`spider`, `quad_mech`, `siege_crawler`)**: 10-bone hierarchy (`Bone_Root`, `Bone_Hull`, `Bone_Pelvis`, `Bone_Thigh_FL/FR/RL/RR`, `Bone_Shin_*`, `Bone_Foot_*`, `Bone_TurretYaw`, `Bone_TurretPitch`, `Bone_BarrelRecoil`).
- **Rigid Weighting**: Discrete weight $1.0$ per armor component assigned on unbatched source meshes before `batch_and_join_by_material`. Ensures 100% weight retention across joined FBX meshes without rubbery joint distortion.

### B. In-Place Locomotion Invariant & Action Clips (`generate_procedural_action_clips`)
- **The In-Place Invariant**: In RTS games where translation is handled by grid physics (`EntityFlockingMovement`), `Bone_Root` delta must remain strictly $(0, 0, 0)$ across all frames of `Action_Walk` to prevent double-stepping and cyclic snap-back.
- **5 Standard Baked Action Clips**:
  1. `Action_Idle` (30 frames loop): Mechanical idling rumble and suspension breathing.
  2. `Action_Walk` (40 frames loop): In-place mechanical gait cycle ($\Delta\text{pos} = (0, 0, 0)$ on `Bone_Root`).
  3. `Action_Drive` (30 frames loop): Longitudinal suspension pitch and bounce for wheeled/tracked units.
  4. `Action_FireRecoil` (20 frames): Rapid ballistic recoil slide with progressive spring recovery.
  5. `Action_Death` (35 frames): Catastrophic suspension sag, knee buckle, and turret droop.
- **Mecanim Animator Parameters**: Unity Model Importer configures Generic Avatars driven by `RuntimeVisualDirector` via `RCL_IsMoving` (Bool), `RCL_Fire` (Trigger), and `RCL_Death` (Trigger).

---

## 6. Full 19 Tactical Archetypes & 9 Weapons Matrix

| Archetype | Class / Stance | Default Weapon | Rig Type | Signature Visual Features |
|---|---|---|---|---|
| `tank` | Tracked Frontline | `cannon` | Standard | Sloped frontal glacis, faceted turret, side skirts. |
| `walker` | Biped Digitigrade | `gatling` | Biped Armature | Articulated hydraulic legs, hip rings, twin rotary Gatlings. |
| `bike` | Inline Two-Wheeler | `gatling` | Standard | Tubular motorcycle frame, all-terrain tires, outrigger charges. |
| `artillery` | Heavy Tracked | `howitzer` | Standard | Elevated heavy siege howitzer, recoil recuperators, rear spade. |
| `arc_tank` | Disruption Tracked | `arc` | Standard | Insulated generator, dual capacitors, toroidal plasma emitter. |
| `hover` | Repulsor Skimmer | `cannon` | Standard | Ventral thruster bells, lateral strakes, vectoring exhausts. |
| `spider` | 4-Leg Crawler | `missile` | Quad Armature | Low-slung chassis, articulated crawler legs, ground pads. |
| `vtol` | Ducted Gunship | `missile` | Standard | Swept wings, dual swiveling tilt-rotors, sensor chin pod. |
| `halftrack` | Front Wheel + Track | `gatling` | Standard | Armored troop compartment, rear clamshell doors, cupola. |
| `super_heavy` | Mobile Fortress | `dual_cannon` | Standard | Quad independent track pods, massive hull, command radar. |
| `buggy` | Recon Wheeled | `gatling` | Standard | Welded tubular roll cage, raised suspension, roof gun. |
| `flak_anti_air`| Air Defense Tracked | `quad_flak` | Standard | Elevated 2x2 autocannon cluster, tracking radar dish. |
| `siege_crawler`| Super-Heavy Siege | `railgun` | Quad Armature | Quad track pods, hydraulic outriggers, massive railgun mantlet. |
| `drone_carrier`| Fleet Command | `missile` | Standard | Super-wide hull, flight launch deck, command island tower. |
| `quad_mech` | Heavy Quad Walker | `dual_cannon` | Quad Armature | Pelvis core block, 4 heavy crawler knees, heavy twin turret. |
| `combat_harvester`| Armed Harvester | `gatling` | Standard | Front rotary cutting drum, ore hoppers, armored cab. |
| `colossus_titan` | Heavy Assault Walker | `dual_cannon` | Biped Armature | Digitigrade legs ($2.3\text{m}$ hip height), citadel torso, twin cannons. |
| `hover_cruiser` | Amphibious Gunboat | `railgun` | Standard | Hydrodynamic bow, flank repulsors, superfiring railgun turret. |
| `heavy_dropship`| Tactical Transport | `missile` | Standard | High-wing VTOL, cargo fuselage, rear ramp, ducted tilt-rotors. |

### 9 Procedural Weapon Assemblies
`make_coaxial_cannon_assembly`, `make_dual_cannon_assembly`, `make_heavy_howitzer_assembly`, `make_rotary_gatling_cannon`, `make_missile_launcher_pod`, `make_arc_energy_emitter`, `make_railgun_assembly`, `make_quad_flak_autocannon_assembly`, `make_plasma_mortar_assembly`.

---

## 7. Tactical Structures Suite (`RogueStructureBuilder`)

Guarantees strict ground clearance invariance ($Z_{\min} = 0.0000\text{ m}$) and generates standardized factory colliders:
1. `factory`: Concrete slab base, ribbed production hall, roller bay door, yellow gantry crane, roof exhaust stacks, and `Socket_Deploy_Unit` / `Socket_DeployRamp`.
2. `defense_turret`: Stepped octagonal concrete bunker, 360° traverse collar, beveled turret cap, integrated weapon mount (`cannon`, `quad_flak`, `railgun`).
3. `radar_station`: Octagonal comms bunker, structural lattice mast, motorized parabolic radar dish (30 RPM).
4. `energy_silo`: Heavy reactor vessel, diagonal foundation buttresses, magnetic containment rings, plasma conduits.
5. `orbital_silo`: Strategic launch pit, blast berm, dual hydraulic blast doors, umbilical gantry tower, warning beacon.

---

## 8. Modular Attachments, Damaged States & Tactical Icons

- **Modular Hardpoints (`--attachments era,aps,sponson,fuel_drums`)**:
  - `make_era_armor_tiles`: Explosive reactive armor bricks.
  - `make_aps_hardkill_system`: Active protection radar sensor pods and mortar tubes.
  - `make_sponson_pod`: Side armor sponsons with auxiliary autocannons.
  - `make_aux_fuel_drums`: Aft cylindrical fuel drums / power cells.
  - `--export-detached-attachments`: Exports individual `<Name>_Attachment_<Type>.fbx` models excluded from base atlas/LODs.
- **Progressive Battle Damage (`--damaged-state`)**:
  - Exports `<Name>_Damaged.fbx` with dented armor plates, jammed $4^\circ–6^\circ$ turret tilt, displaced road wheels, and `Combat_Scorched` materials.
- **Tactical Minimap & HUD Icons (`--render-icons`)**:
  - Dual-angle transparent PNG renderer at 64x64 and 128x128:
    - **Top-Down (90°)**: Pure vertical footprint for minimap blips (`<Name>_Icon_TopDown_<Size>.png`).
    - **Isometric RTS (45°)**: Trigonometrically calibrated RTS perspective at $45.0^\circ$ elevation (`<Name>_Icon_Isometric_<Size>.png`).

---

## 9. Headless CLI Tooling (`generate_unit_model.py`)

Run headless via local Blender 4.5.14 LTS:

```powershell
.\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\generate_unit_model.py -- [OPTIONS]
```

### Core CLI Arguments
- `--name <str>`: Model name and output filename prefix (default: `CustomRogueVehicle`).
- `--archetype <str>`: One of the 19 supported archetypes (default: `tank`).
- `--weapon <str>`: Primary weapon (`cannon`, `gatling`, `missile`, `arc`, `howitzer`, `dual_cannon`, `railgun`, `quad_flak`, `plasma_mortar`).
- `--faction <vanguard|siege|syndicate|arctic>`: 5-tone faction PBR palette.
- `--wear <none|pristine|factory_new|field_tested|battle_hardened>`: Vertex wear and chipping intensity.
- `--soot-intensity <float>`: Multiplier ($0.0 - 2.0$) for muzzle and exhaust thermal soot.
- `--rig` / `--animate`: Generates parametric skeletal armatures and bakes 5 standard action clips.
- `--lods`: Generates multi-level LOD FBX chain (`_LODs.fbx`, LOD0 100%, LOD1 50%, LOD2 22%).
- `--colliders`: Generates flat-shaded `<Name>_COLLIDER_*` meshes.
- `--gameplay-json`: Exports `<Name>.gameplay.json` sidecar declaring collider bounds, kinematics, and sockets.
- `--batch-all-archetypes`: Sequentially builds all 19 archetypes. Supports `--dry-run` for fast planning validation.

---

## 10. Unity Prefab Bridge & Runtime Visual Automation

### A. Prefab Builder Pipeline (`VehiclePrefabBuilder.cs`)
1. **LODGroup & Model Selection**: Prioritizes animated primary FBX first, then `_LODs.fbx`, then `_Atlas.fbx`. Configures `LODGroup` transitions (50%, 20%, 5% cull).
2. **Recursive Socket Resolution**: Uses `FindTransformRecursive` to discover existing authored sockets without spawning duplicate transforms under the visual root.
3. **Collision Conversion**: Disables visible `Renderer` on `_COLLIDER_*` meshes and attaches native `BoxCollider` components.
4. **Automated Animator Setup**: Binds Generic Avatar, creates `<Name>Controller.controller`, loops `Action_Idle`/`Action_Drive`, and configures transitions for `Action_FireRecoil` and `Action_Death`.

### B. In-Game Runtime Director (`RuntimeVisualDirector.cs`)
1. **Combat Damage Automation**:
   - $\le 75\%$ HP: Triggers ERA reactive armor detonation.
   - $\le 50\%$ HP: Mounts smoke emitters onto `Socket_DamageSmoke` and activates `<Name>_Damaged` mesh variant.
   - $\le 25\%$ HP: Mounts fire emitters onto `Socket_DamageFire` and tilts turret $+4.5^\circ$ to simulate traverse jamming.
   - Reversible: Restores pristine visuals if unit health recovers.
2. **Kinematic Dynamics**:
   - Radar Sweep: Rotates `Socket_RadarMast` at exact 30 RPM ($180^\circ/\text{s}$).
   - Factory Rollout: Detects `EntityController.InProductionCount > 0` and slides bay door upward by $1.4\text{ m}$.
   - Turret Target Tracking: Tracks hostile combat targets in horizontal azimuth (120°/s) using `EntityController.IsEnemy` (token `0x06000844`) to reject friendly or destroyed units.
   - Flexible Antenna Sway: Computes chassis acceleration $\vec{a} = \Delta \vec{v} / \Delta t$ and drives a damped harmonic oscillator swaying `Socket_Antenna`.
