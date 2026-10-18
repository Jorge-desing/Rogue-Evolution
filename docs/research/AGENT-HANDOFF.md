# Rogue Command Content Expansion — Agent Handoff

**Updated:** 2026-10-18  
**Repository:** Project root (tracks `origin/codex/rogue-unit-balance-20261018`)  
**Core Goal:** Create a Rogue Command content expansion mod with original playable units, stages, AI profiles, and 3D visual assets. The external BepInEx loader is infrastructure for registering and running content additively. Prioritize playable content and Steam-launched validation over unrequested loader changes.

---

## 1. Compatibility Identity

The local development environment and loader target an exact pinned game identity:

| Property | Value |
|---|---|
| **Game** | Rogue Command 1.0.9 |
| **Unity** | 2021.3.45f2 (Mono x64 player) |
| **Target Framework** | `netstandard2.1` (required by Unity 2021.2+ and native `Assembly-CSharp.dll`) |
| **`Assembly-CSharp.dll` SHA-256** | `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` |
| **`Assembly-CSharp.dll` MVID** | `db0e9462-128d-4296-9900-de18b4bd0c00` |
| **Game Directory** | `C:\Program Files (x86)\Steam\steamapps\common\Rogue Command` |
| **Steam AppID** | `1461910` |

> [!IMPORTANT]
> Treat Game Version, Assembly SHA-256, and MVID as a single identity. Recheck all three before relying on reflected signatures or metadata tokens.

---

## 2. Implementation Map

| Component / Path | Purpose & Responsibilities |
|---|---|
| `src/RogueCommandExpansionLoader/RogueCommandExpansionPlugin.cs` | BepInEx entry point, compatibility gate, pack discovery, diagnostic logging. |
| `src/RogueCommandExpansionLoader/Runtime/RuntimePatchInstaller.cs` | Installs/removes 26 verified owner-scoped Harmony patches. |
| `src/RogueCommandExpansionLoader/Runtime/RuntimeContentMutator.cs` | Adds unit rows, factory mappings, and changeable-value caches idempotently with rollback. |
| `src/RogueCommandExpansionLoader/Runtime/Visual/RuntimeVisualDirector.cs` | In-game visual management (damage smoke/fire, turret tracking, 30 RPM radar, antenna wobble). |
| `src/RogueCommandExpansionLoader/Content/ContentPackRuntimeLoader.cs` | Validates and loads packs, resolves localizations (`Loca`), clones native templates, and builds scoped blueprint pools. |
| `src/RogueCommandExpansionLoader/Modules/` | Compiled tactical AI modules: `ScheduledBaseBuild`, `ReactiveBaseDefense`, `HarvesterRaider`, `SiegeAssault`. |
| `content/schema/` | Version-1 JSON Schema for content manifests (`content.json`). |
| `content/packs/sample-native-expansion/` | 5 units, 5 factories, 3 stages, 3 AI profiles, script-free visual AssetBundle. |
| `content/packs/sample-reactive-ai/` | Data-only stage and isolated reactive base-defense AI profile. |
| `tests/RogueCommandExpansionLoader.ValidationTests/` | 133 offline harness tests (mocked game stubs, schema, rollback, mutator, AI parsers). |
| `tools/RogueModelKit.py` & `tools/generate_unit_model.py` | Procedural 3D modeling toolkit in Blender 4.5.14 LTS (mirrored in `.agents/skills/`). |
| `unity/RogueCommandAssetAuthoring/` | Unity 2021.3.45f2 project for prefab assembly and AssetBundle compilation. |

---

## 3. Content Contracts & System Invariants

### A. Production Building Invariant
- **Rule**: Every added unit must declare its own namespaced `productionBuilding`.
- **Registration**: The loader registers a distinct factory row and dormant prefab clone, maps that factory exclusively to its produced unit, and offers the factory blueprint through stage-scoped random reward pools.
- **Rosters**: Stage `unitIds` and AI `unitEntries` reference the produced unit ID, never the factory.
- **Sample Units & Factories**: 5 pairs declared in `sample-native-expansion`: Sandlance Scout (`scout-tank`), Skirmish Strike Bike (`skirmish-bike`), Colossus Siege Platform (`heavy-artillery`), Ironclad Gatling Walker (`assault-walker`), Surge Arc Tank (`surge-tank`).

### B. Blueprint Pools, Drafting & Progression
- **Stage Scoping**: Native `BlueprintPoolList` and `BlueprintPool` objects are cloned deterministically per stage (`RCL_POOL_<sha256>`). Custom units appear only in pools belonging to stages that declare them.
- **Initial Blueprint Eligibility**: Static `Game.AtLeastOneBlueprintHasBeenAddedByPlayer` governs the first reward draft. Units with `isAllowedAsStartingBlueprint: false` are excluded from stage-0 drafting.
- **Profile Card Unlock Tiers**: Progression unlocks depend on persistent **profile experience** (levels 0–4), not in-run stage index. Unlocks: Sandlance (L0), Bike (L1), Ironclad (L2), Surge (L3), Colossus (L4).
- **Generic Reward Fallback**: In `ChooseCard.PresentCardsToChoose`, unmapped reward pools and no-pool reward choices filter out mod factories declared only by other stages.

### C. Procedural 3D Modeling & URP Shaders
- **Render Pipeline Invariant**: Rogue Command runs strictly on the **Universal Render Pipeline (URP)**. Shipped vehicles use untextured geometry with `Shader Graphs/EntityColorShader`. Mod AssetBundles must embed compiled URP shaders (`RogueCommand/VehiclePBR_VertexWear`).
- **Topological BMesh RGBA Vertex Color Contract**:
  - **Channel R (Cavity AO)**: $1.0$ on zenith/open faces; $0.25 - 0.40$ on undersides and wheel recesses.
  - **Channel G (Edge Wear)**: $0.0$ on flat faces and concave seams; $0.7 - 1.0$ on sharp convex edges (dihedral angle $\ge 35^\circ$).
  - **Channel B (Thermal Soot)**: $0.0$ default clean base metal; scales to $1.0$ at muzzles ($R = 0.60\text{ m}$) and exhausts ($R = 0.45\text{ m}$) with quadratic dropoff $(1 - d/R)^2$.
  - **Channel A (Team Tint Mask)**: $1.0$ on primary and light armor plates; $0.0$ on mechanical parts.
- **Single-Material Export**: Single-material FBX (`_Atlas.fbx`) guarantees one material slot on the exported mesh, not a guaranteed single draw call.

### D. Multi-System Anchors & Locators (`Socket_*`)
- **Naming**: Always canonical unprefixed names (`Socket_*`). Never prefix with vehicle name.
- **VFX Hierarchy**: Muzzle locators (`Socket_MuzzlePrimary`) must nest empty `FirePoint_0` (and `FirePoint_1` for secondaries) to satisfy native `ShootProjectile.Init()` discovery (`child.GetChild(0).GetComponent<ParticleSystem>()`).
- **Spatial 3D Audio**: `Socket_EngineAudio` on engine deck for `EntityFlockingMovement._movementSoundAudioSource`; `Socket_WeaponAudio` at primary muzzle for `ShootProjectile.shotSoundAudioSource`.
- **Ground Contacts**: Bipeds emit `Socket_FootContact_L/R` at $Z = 0.0$; quadrupeds emit `_FL/FR/RL/RR`.
- **Tiered Damage**: `Socket_DamageSmoke` ($\le 50\%$ HP, with turret-ring fallback) and `Socket_DamageFire` ($\le 25\%$ HP).

### E. Skeletal Mech Rigging & In-Place Locomotion
- **Rig Paradigms**: Supports native hierarchical transform rigs (`RotHor/ROTVERT/LEGS`) or skeletal armatures (`Bone_Root`, `Bone_Pelvis`, `Bone_Thigh_*`, `Bone_Shin_*`, `Bone_Foot_*`, `Bone_TurretYaw`, `Bone_TurretPitch`, `Bone_BarrelRecoil`).
- **In-Place Invariant**: RTS grid physics (`EntityFlockingMovement`) drives position. Walk clips must strictly enforce $\Delta\text{pos} = (0, 0, 0)$ on `Bone_Root` to eliminate double-stepping artifacts.
- **Rigid Weighting**: Discrete $1.0$ vertex weighting per armor plate assigned on unbatched meshes *before* material batching prevents rubbery joints in Unity.
- **Mecanim Contract**: Generic Avatars driven by `RCL_IsMoving` (Bool), `RCL_Fire` (Trigger), `RCL_Death` (Trigger) across 5 standard actions (`Action_Idle`, `Action_Walk`, `Action_Drive`, `Action_FireRecoil`, `Action_Death`).

### F. Verified CIL Metadata Tokens (`Assembly-CSharp.dll`)
Static REA inspection verified the following exact tokens against the pinned game binary:
- `EntityController.get_HealthRatio`: `0x060007e4` (`Single`)
- `EntityController.get_DestroyHasBeenStarted`: `0x0600077d` (`Boolean`)
- `EntityController.get_InProductionCount`: `0x060007df` (`Nullable<Int32>`)
- `EntityController.get_CurrentAttackTarget`: `0x060007fc` (`EntityController`)
- `EntityController.IsEnemy`: `0x06000844` (`Boolean(EntityController)`)
- `EntityAttack._attackTarget`: `0x040004a2`
- `EntityAttack._timeOfLastShot`: `0x040004ab` (`Single`)
- `EntityController.onFireParticleSystem`: `0x040004e1` (`ParticleSystem`)

---

## 4. Repository-Local Skills Catalog

| Skill | Primary Scope & Role |
|---|---|
| `rogue-command-unit-authoring` | Unit balancing, native templates, localizations (`en-US`, `es-XL`), and card display stats. |
| `rogue-command-3d-model-authoring` | Procedural 3D vehicles (19 archetypes, 9 weapons), Blender generation, and URP shaders. |
| `rogue-command-vfx-authoring` | Particle systems, muzzle flash hierarchy, damage smoke/fire, and URP unlit shaders. |
| `rogue-command-audio-authoring` | Mono 3D audio spatialization, weapon pitch jitter (+/-5%), and engine looping audio. |
| `rogue-command-mech-rigging-authoring` | Armatures, rigid vertex weighting, in-place locomotion, and Generic Animators. |
| `rogue-command-balance-simulation` | Lanchester combat simulation, discrete shields, armor floor (0.1), and CEI calibrations. |
| `rogue-command-mutator-hazard-authoring` | Native `CardChangeScriptableObject` mutators, relics, and trigger hazards. |
| `rogue-command-stage-authoring` | Stage progression, world/map selection, and blueprint pool scoping. |
| `rogue-command-ai-authoring` | Compiled AI modules (`ScheduledBaseBuild`, `ReactiveBaseDefense`, `HarvesterRaider`, `SiegeAssault`). |
| `rogue-command-assetbundle-authoring` | Unity 2021.3.45f2 AssetBundle compilation, card Sprites, and prefab validation. |
| `rogue-command-pack-validation` | Schema validation, path safety, and offline test harness audits. |
| `rogue-command-game-research` | REA reverse-engineering, assembly token verification, and compatibility tracking. |
| `rogue-command-steam-runtime-validation` | Reversible Steam runtime validation, bridge MCP, and safe profile isolation. |

---

## 5. Build, Validate, and Package Runbook

Execute all commands from the repository root in PowerShell:

```powershell
# 1. Compile Release Loader DLL
dotnet build src/RogueCommandExpansionLoader/RogueCommandExpansionLoader.csproj --configuration Release --verbosity minimal

# 2. Run Offline Test Suite (132/133 tests passing; 1 skipped for Windows symlink privilege)
dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .

# 3. Build Staged Distributable ZIP under artifacts/packages/
.\tools\Package-Loader.ps1

# 4. Verify Twin Script Parity (must return 0 differences)
fc.exe /b tools\RogueModelKit.py .agents\skills\rogue-command-3d-model-authoring\scripts\RogueModelKit.py
fc.exe /b tools\generate_unit_model.py .agents\skills\rogue-command-3d-model-authoring\scripts\generate_unit_model.py
```

> [!WARNING]
> Keep BepInEx, Harmony, and game assemblies out of the distributable ZIP. Only local development builds may reference them.

---

## 6. Runtime Evidence & Steam Testing Boundaries

- **Evidence Boundaries**:
  - *Static Inspection*: Assembly metadata, CIL tokens, REA projections, serialized YAML.
  - *Local Build / Validation*: Compiler success, AssetBundle build, 132 passing harness tests.
  - *Runtime Evidence*: Live observations inside a Steam-launched match (`Rogue Command.exe` AppID `1461910`).
- **Verified Runtime Milestones**:
  - BepInEx MCP bridge connected on loopback (Protocol 2.0).
  - Loader initialized cleanly with 26 verified Harmony hooks and 0 error lines.
  - Content packs (`sample-native-expansion` and `sample-reactive-ai`) loaded and registered 10 product/factory prefabs under `DontDestroyOnLoad`.
  - Profile 1 and Profile 3 isolation verified with zero persistent save pollution; Steam Cloud remained disabled during tests.
- **Process Discovery Note**: The game executable contains a space: `Rogue Command.exe`. In PowerShell, query via `Get-CimInstance Win32_Process -Filter "Name = 'Rogue Command.exe'"`.

---

## 7. Next Implementation Milestones

1. **Live Combat Validation**: Execute a reversible Steam-launched match to verify in-combat unit instantiation, visual overlay alignment, and weapon firing feedback for the 5 sample expansion units.
2. **AI Combat Decision Verification**: Validate that `sample:reactive-base-defense` and `sample:harvester-raider` trigger autonomous counterattacks during dense combat.
3. **Stage Map Progression**: Verify natural progression through appended stages (indices 9–12) and confirmation of reward card drafting across profiles.
