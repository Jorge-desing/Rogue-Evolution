---
name: rogue-command-3d-model-authoring
description: "Design, create, or refine original 3D vehicle and unit visuals for Rogue Command with Blender and the pinned Unity authoring project. Use alongside unit and AssetBundle skills when the task changes a model or its in-game appearance."
---

# Rogue Command 3D Model Authoring

Guides procedural modeling in Blender 4.5.14 LTS, Unity 2021.3.45f2 AssetBundle authoring, URP shading, skeletal mech rigging, and multi-system socket integration for Rogue Command.

---

## 1. Directory Invariants & Tooling Baseline

- **Blender 4.5.14 LTS**: Pinned at `.tools/Blender-4.5.14/blender-4.5.14-windows-x64/blender.exe`.
- **Unity 2021.3.45f2**: Pinned at `unity/RogueCommandAssetAuthoring/` (Mono, Windows Standalone x64). Preserve `ProjectSettings` and package manifests.
- **Editable Blender Sources**: `content/source-art/*.blend`
- **Output Unity FBXs**: `unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/*.fbx`
- **Distributable Bundles**: `content/packs/<pack-id>/bundles/*.bundle`
- **Twin Script Parity Invariant**: Maintain 100% byte-for-byte binary parity between `tools/` and `.agents/skills/.../scripts/` (verified via `fc.exe /b`).

---

## 2. Art Direction & The 3-Tier Visual Read

Vehicles are viewed from an elevated isometric RTS camera (~45°–60° pitch, 30–120 pixels tall).
- **Tier 1: Macro Silhouette (70%)**: Bold stance and volume. Must pass the "Squint Test" in < 250ms at 50px scale.
- **Tier 2: Meso Mechanical Geometry (20%)**: Continuous tracks, articulated legs, tilt-rotors, engine louvers.
- **Tier 3: Micro High-Contrast Accents (10%)**: Recessed emissive optics, hazard warning stripes, muzzle collars.
- **Single-Segment Chamfers (The Specular Glint Rule)**: Mechanical plates receive 1-segment bevels (`segments = 1`, `width = 0.02m–0.06m`) with flat shading to catch bright specular glints.
- **5-Tone Structural Palette**: Primary Armor (40–45%), Light Armor (20–25%), Mechanical Gunmetal (15–20%), Tread Rubber (10–15%), Warning Accent (3–5%), Emissive Tech (1–3%, HDR ×1.5–2.5).

---

## 3. Shading & Socket Integration Contracts

### A. Topological BMesh RGBA Vertex Wear (`VehiclePBR_VertexWear`)
- **Channel R (Cavity AO)**: $1.0$ on zenith/open surfaces; $0.25 - 0.40$ on undersides and recesses ($\Delta > 10^{-4}$).
- **Channel G (Edge Wear)**: $0.7 - 1.0$ on sharp convex edges ($\ge 35^\circ$, $\Delta < -10^{-4}$); strictly $0.0$ on flat/concave seams.
- **Channel B (Thermal Soot)**: $0.0$ clean metal body; $1.0$ at weapon muzzles ($R = 0.60\text{m}$) and exhausts ($R = 0.45\text{m}$) with $(1 - d/R)^2$ falloff.
- **Channel A (Team Mask)**: $1.0$ on primary and light armor plates; $0.0$ on mechanical parts.

### B. Canonical Multi-System Sockets (`Socket_*`)
Emits exact, unprefixed empty transforms (+Z forward, +Y up):
- `Socket_MuzzlePrimary`: Nests child empty `FirePoint_0` for native `ShootProjectile.Init()` particle discovery (`child.GetChild(0).GetComponent<ParticleSystem>()`).
- `Socket_EngineAudio`: Over engine deck for `EntityFlockingMovement._movementSoundAudioSource`.
- `Socket_WeaponAudio`: At primary muzzle for `ShootProjectile.shotSoundAudioSource`.
- `Socket_FootContact_L/R` (and `_FL/FR/RL/RR`): Ground-plane ($Z = 0.0$) contact anchors for dust VFX/SFX.
- `Socket_DamageSmoke`: $\le 50\%$ HP (exhausts with turret-ring fallback).
- `Socket_DamageFire`: $\le 25\%$ HP (near reactor/fuel).
- `Socket_TurretPivot`: 120°/s azimuth traverse; `Socket_RadarMast`: 30 RPM ($180^\circ/\text{s}$) sweep.
- `Socket_Antenna`: Inertial spring-damper wobble; `Socket_Deploy_Unit`: Factory rollout bay door.
- `Socket_Hardpoint_ERA/APS/Sponsons/FuelDrums`: Modular upgrade attachments.

### C. Skeletal Mech Rigging & In-Place Locomotion
- **Armature Hierarchy**: Bipeds (8 bones: `Bone_Root`, `Bone_Pelvis`, `Bone_Hull`, `Bone_Thigh_L/R`, `Bone_Shin_L/R`, `Bone_Foot_L/R`, `Bone_TurretYaw`, `Bone_TurretPitch`, `Bone_BarrelRecoil`); Quadrupeds (10 bones: `Bone_Thigh_FL/FR/RL/RR`, `Bone_Shin_*`, `Bone_Foot_*`).
- **Rigid Weighting**: Discrete weight $1.0$ per armor component assigned on unbatched source meshes before `batch_and_join_by_material`.
- **In-Place Locomotion Invariant**: $\Delta\text{pos} = (0, 0, 0)$ on `Bone_Root` across all frames of `Action_Walk`.
- **5 Action Clips**: `Action_Idle`, `Action_Walk`, `Action_Drive`, `Action_FireRecoil`, `Action_Death`.
- **Mecanim Contract**: Generic Avatars driven by `RuntimeVisualDirector` via `RCL_IsMoving`, `RCL_Fire`, and `RCL_Death`.

---

## 4. Headless CLI Tooling (`generate_unit_model.py`)

```powershell
.\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\generate_unit_model.py -- [OPTIONS]
```

### Core CLI Options
| Argument | Description & Values |
|---|---|
| `--name <str>` | Model name and hierarchy prefix (default: `CustomRogueVehicle`). |
| `--archetype <str>` | 19 archetypes: `tank`, `walker`, `bike`, `artillery`, `arc_tank`, `hover`, `spider`, `vtol`, `halftrack`, `super_heavy`, `buggy`, `flak_anti_air`, `siege_crawler`, `drone_carrier`, `quad_mech`, `combat_harvester`, `colossus_titan`, `hover_cruiser`, `heavy_dropship`. |
| `--weapon <str>` | 9 weapons: `cannon`, `gatling`, `missile`, `arc`, `howitzer`, `dual_cannon`, `railgun`, `quad_flak`, `plasma_mortar`. |
| `--faction <str>` | `vanguard` (slate teal), `siege` (rust ochre), `syndicate` (deep cobalt), `arctic` (tundra navy). |
| `--wear <str>` | `none`, `pristine`, `factory_new`, `field_tested`, `battle_hardened`. |
| `--soot-intensity <float>` | Muzzle/exhaust thermal soot scalar ($0.0 - 2.0$, default: $1.0$). |
| `--rig` / `--animate` | Builds skeletal armature and bakes 5 standard action clips. |
| `--lods` | Exports multi-level LOD chain `_LODs.fbx` (100%, 50%, 22%). |
| `--colliders` | Exports flat-shaded collision boxes `<Name>_COLLIDER_*`. |
| `--gameplay-json` | Exports `<Name>.gameplay.json` sidecar for Unity prefab construction. |
| `--structure` | Switches to structure generation (`factory`, `defense_turret`, `radar_station`, `energy_silo`, `orbital_silo`). |
| `--attachments <list>` | Modular armor hardpoints (`era`, `aps`, `sponson`, `fuel_drums`). |
| `--export-detached-attachments` | Exports detached `<Name>_Attachment_<Type>.fbx` assets. |
| `--damaged-state` | Exports battle-degraded `<Name>_Damaged.fbx` ($25\% - 50\%$ HP state). |
| `--render-icons` | Bakes 64x64 and 128x128 transparent PNG icons (Top-Down 90° & Isometric 45°). |
| `--batch-all-archetypes` | Builds all 19 archetypes sequentially. Supports `--dry-run` for instant validation. |

---

## 5. Unity Prefab Bridge & Runtime Director

### A. Prefab Construction (`VehiclePrefabBuilder.cs`)
1. **LODGroup & Model Selection**: Prioritizes animated primary FBX first, then `_LODs.fbx`, then `_Atlas.fbx`.
2. **Recursive Sockets**: Uses `FindTransformRecursive` to discover existing authored sockets without spawning duplicates.
3. **Collision Conversion**: Disables visible `Renderer` on `_COLLIDER_*` meshes and attaches native `BoxCollider` components.
4. **Mecanim Controller**: Binds Generic Avatar and creates `<Name>Controller.controller` wiring movement, fire, and death transitions.

### B. In-Game Runtime Director (`RuntimeVisualDirector.cs`)
Verified against `Assembly-CSharp.dll` (SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`):

| Property / Method | Declaring Type | Verified CIL Token | Functionality in RuntimeVisualDirector |
|---|---|---|---|
| `Single HealthRatio` | `EntityController` | `0x060007e4` | Normalizes health to drive 50% smoke, 25% fire, and turret tilt damage states |
| `Boolean DestroyHasBeenStarted` | `EntityController` | `0x0600077d` | Confirms catastrophic entity demise to trigger clean particle teardown |
| `Nullable<Int32> InProductionCount` | `EntityController` | `0x060007df` | Queries factory production queue to drive animated rollout bay doors |
| `EntityController CurrentAttackTarget` | `EntityController` | `0x060007fc` | Primary weapon target resolution for procedural turret traverse tracking |
| `Boolean IsEnemy(EntityController)` | `EntityController` | `0x06000844` | Authoritative hostility check to ensure turrets only target hostile opposing entities |

---

## 6. References & Guides

- [Art Style & Palette Guide](references/art-style-and-palette.md) — 3-tier silhouette hierarchy, 5-tone palette sets, bevel physics, and greeble standards.
- [Blender Procedural Techniques](references/blender-procedural-techniques.md) — Coordinate invariants, orthonormal framing math, and procedural generator catalog.
- [Unity Material Pipeline](references/unity-material-pipeline.md) — URP material configuration, linear metallic-gloss import, emission keywords, and card sprite rules.
- [Native Hierarchy & Pivots](references/native-hierarchy-and-pivots.md) — Transform hierarchy maps for `PCXBaseTank`, `BombBike`, `ArtilleryTank`, `GatlingWalker`, and `LightningTank`.
