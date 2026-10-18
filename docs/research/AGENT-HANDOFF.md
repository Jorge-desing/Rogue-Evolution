# Rogue Command Content Expansion — Agent Handoff & Technical Baseline

**Updated:** 2026-10-18  
**Repository:** Mod Rogue Command (`c:\Users\Alex\Documents\Mod Rogue Command`)  
**Remote:** `origin` points to `https://github.com/Jorge-desing/Rogue-Evolution.git` (branch `codex/rogue-unit-balance-20261018`)  
**Mission:** Create a modular, content-first expansion mod for Rogue Command featuring original playable units, base production buildings, stages, tactical AI profiles, and custom 3D visuals. BepInEx and Harmony provide loader infrastructure; playable content and runtime stability are the mod's deliverables.

---

## 1. Authoritative Compatibility Identity

Always recheck this identity before relying on reflected signatures, CIL metadata tokens, or serialized assets:
- **Game Version**: Rogue Command `1.0.9` (verified via `version.txt` and `Player.log`)
- **Engine Version**: Unity `2021.3.45f2` (Mono x64)
- **Mod Loader Baseline**: BepInEx `5.4.23.5` (Mono Standalone x64, target framework `netstandard2.1`)
- **Assembly-CSharp.dll SHA-256**: `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`
- **Assembly-CSharp.dll MVID**: `db0e9462-128d-4296-9900-de18b4bd0c00`
- **Managed decompilation fallback**: If REA has no compatible managed pseudocode provider on this host, use [`tools/Decompile-ManagedType.ps1`](../../tools/Decompile-ManagedType.ps1) and the [managed DLL decompilation guide](MANAGED-DLL-DECOMPILATION.md). Keep the version/SHA/MVID check above as the compatibility identity before relying on reflected signatures or metadata tokens.
- **Unity CoreModule SHA-256**: `6305c82c17ffe111a016e8d8ca3e8b2d9203980b4f58cb6e90171e5629994290` (MVID `37d484f4-070f-4518-bfa9-9fa57b7a2871`)
- **Render Pipeline**: Universal Render Pipeline (URP, `UniversalRenderPipelineAsset`, PathID 2768 in `globalgamemanagers.assets`). Shipped vehicles render via untextured submeshes with `Shader Graphs/EntityColorShader`. Modded AssetBundles must embed self-contained compiled shaders (`RogueCommand/VehiclePBR_VertexWear` or `Standard`).

---

## 2. Steam Bridge & Runtime Verification State

### BepInEx MCP Bridge Protocol 2.0
- **Process Discovery Pitfall**: The executable is named `Rogue Command.exe` (with a space). Standard `Get-Process -Name RogueCommand` fails. Always query via exact CimInstance:
  ```powershell
  Get-CimInstance Win32_Process | Where-Object { $_.Name -like "*Rogue*Command*" }
  ```
- **Lifecycle & Health**: The bridge HTTP endpoint (`http://localhost:54321/mcp`) is hosted in-process by BepInEx. A ping failure when no process runs is expected. Follow pings with at least one root GameObject or scene read before asserting connection. Consult [STEAM-RUNTIME-CONTROL.md](STEAM-RUNTIME-CONTROL.md).
- **Bridge Query Schema**: The bridge accepts JSON-RPC `unity_query` payloads (`{"query": "FindObjectsOfType", "type": "EntityController"}`) returning serialized hierarchy state, component values, and active scene tags without game engine stall.
- **Verified Startup State**: Steam launch (AppID `1461910`) loads `RogueCommandExpansionLoader.dll` with 26 verified Harmony hooks, registers both sample packs (`sample-native:expansion` and `sample-reactive-ai:expansion`), and transitions cleanly across `MainMenu`, `RunSetup`, `IntermissionScreen`, and `Game`.
- **First-run tutorial start (2026-10-18)**: The user's screenshot showed the `FirstRun` tutorial's `INICIAR` screen. The in-game button's persistent listener resolved to `PlaceMapObjectsBeforePlayerCanStart.OnPlayerHasPressedStart`; it was invoked once through the native Unity `Button`, and `_playerHasNotPressedStartYet` changed to `false`. The temporary button listener inspector was removed and the bridge had no temporary watchers or patches afterward.
- **Pause verification correction (2026-10-18)**: `InGameMenu._isShowing=true` means only that the menu overlay is visible. In the active tutorial, the HUD clock advanced from `00:16` to `01:19` with `Time.timeScale=1`. A fresh runtime probe then read `Time.timeScale=0` and `Time.time=537.5239`; 16.38 seconds later, `Time.time` was still `537.5239` while `Time.unscaledTime` advanced, and two background `PrintWindow` captures still showed the HUD at `03:51`. The foreground HWND was unchanged before/after each capture. The game remains open in `Game` with the menu visible, tutorial `7/26`, and simulation frozen. The live `RclRuntimeTimeControl` type did not expose the `hasFrozen` guard in the runbook sample: a second `Freeze()` overwrote `scaleBefore` from `1` to `0`, so that field was restored to `1` before resuming. The separate `RclPauseStateProbe` was removed; retain the pause helper until the supervised run is resumed. Reusable steps and `tools/Capture-RogueCommand-Background.ps1` are in [STEAM-RUNTIME-CONTROL.md](STEAM-RUNTIME-CONTROL.md).
- **BepInEx MCP repair and live health (2026-10-18)**: `tools/Repair-BepInExMCP.ps1` completed successfully. `pip check` found no broken requirements (Pydantic 2.13.5; MCP 1.20.0), the server entrypoint and FastMCP settings verification passed, and the existing `FastMCPSettings.model_rebuild()` startup fix was present. Protocol 2.0 ping/capabilities and live Unity scene, component, and native UI calls all succeeded, including a fresh `Button.Press()` with a follow-up scene/pause-state read. The repair verified the local MCP environment; it did not modify the game's BepInEx installation. See [STEAM-RUNTIME-CONTROL.md](STEAM-RUNTIME-CONTROL.md) for the recovery and controlled-input procedure.
- **Latest supervised tutorial smoke and current process (2026-10-18)**: The last observed run was scene `Game`, paused at tutorial step 7/26. A native `RoboCrystalMill` was placed at `(-145, 10, 65)` and reported `EntityId=RoboCrystalMill`, `IsBuilding=true`, `IsFactory=true`, `Cost=1000`, and `CurrentHealth=1000`; seven native `RoboCrystalHarvester` entities appeared after the four-second production delay. The abandon confirmation was dismissed with `KeepPlaying`. The game was closed after that earlier session, then relaunched through Steam (AppID `1461910`) on 2026-10-18; fresh bridge ping/capabilities and a live scene read succeeded. It opened at the main menu, so the former step-7 run did not resume. Profile 1 contains `metaSavegame.dat` and `tutorialState.txt`, but no `savegame.dat`; the later RunSetup smoke left all 13 profile files unchanged against the pre-run snapshot. Keep the current process open for the remainder of the active task; close it normally only after the task is complete. Prior live evidence validates native tutorial production only. See [STEAM-RUNTIME-CONTROL.md](STEAM-RUNTIME-CONTROL.md) for pause, input, screenshot recovery, and cleanup procedures.
- **Last mod-content runtime boundary (fresh bridge reads, 2026-10-18)**: During that run, the BepInEx log reported `sample-native:expansion` with 5 units, 5 production buildings, 3 stages, and 3 AI profiles; `sample-reactive-ai:expansion` had 1 stage and 1 AI profile. Live reads found five `RCL_Native_sample-native_*` unit templates and five matching factory templates; the surge tank resource had `entityId=sample-native:surge-tank`, and its factory had `entityId=sample-native:surge-tank-factory`. `DefaultAiBehaviour` reads found the active native PCX tutorial AI and four inactive `RCL_sample_*` templates. `ChooseCard` was absent from this tutorial state. These reads show registration only; modded rewards, factory production, model rendering, custom stage-map selection, and reactive AI decisions remain unverified. A disconnected bridge is expected while the game process is closed. The temporary deployment manifest is `artifacts/runtime-smoke/loader-natural-offer-followup-20261018T125126/deployment-manifest.json`.
- **Fresh RunSetup loader smoke (2026-10-18)**: A new Steam process (PID `41992`) loaded the deployed `0.1.0` plugin, passed protocol 2.0 bridge ping, installed 26 verified hooks, and transitioned from `MainMenu` to `RunSetup`. The log reported `sample-native:expansion` with 5 units, 5 production buildings, 3 stages, and 3 AI profiles; `sample-reactive-ai:expansion` reported 1 stage and 1 AI profile. Pack registration completed without a loader exception. The aggregate line said “10 unidad(es) nuevas,” although the 10 rows include 5 products and 5 production buildings. A subsequent local source build now reports the total as 10 entities (5 units and 5 production buildings); PID `41992` still has the earlier DLL loaded, so the corrected line needs a fresh run. This smoke did not reach `Game`, select a mod stage/profile, produce a mod unit, or observe AI decisions. A background capture showed the black fade still visible while the bridge reported `RunSetup`; do not describe this as playable-content acceptance.
- **Progression slot diagnostics (2026-10-18)**: The loader logs each contribution key and progression field with end-exclusive slot ranges and actual appended counts for `stages` and `DefaultAiBehaviour`. The earlier `stageIndex=1` trace covered native `StageParameters_Pools_Level2` and returned two unmapped-pool queries with no offers; it did not exercise the appended sample-stage pools. The diagnostic change has local build evidence only; a fresh, separately isolated run is needed to observe it.
- **Installed versus rebuilt loader (hashes captured 2026-10-18)**: The reversible pre-test installation backup records the original loader as `5dbc9546b127d7d68ca6ecdebea6268fe4e649d475a7a869ec2f341b7cab1e38`. PID `41992` was launched with the deployed loader `0543e76a28e5afb7a19824ca95ba9f10f53f04896244b06cc4f151b646437c1f`. A fresh local build after the entity-count log correction succeeded with zero warnings/errors and SHA-256 `B86DAE44098ACBB35510667AE86DD88428574FB16AB1519717C6070D1F1650D5`. The existing package hash `a78272e707ded87f39d112619aa5143c7873c276408f4881b19717fbd7ac63c2` was not rebuilt in this turn and must not be treated as containing that DLL. The live process still runs the earlier deployed build; no installation file was changed, so runtime evidence for the corrected log remains pending.
- **Scoped AI template ranges (fresh live component reads, 2026-10-18)**: The `FirstRun` run contained four registered `RCL_` resource templates with `isDeactivated=false`: `sample-native:quick-hunter-extension` range `(9, 11)`, `sample-native:horde-swarm-assault` `(10, 11)`, `sample-native:raider-patrol-strike` `(11, 11)`, and `sample-reactive-ai:base-defense` `(12, 12)`. Their `DefaultAiBehaviour.isActiveAndEnabled` values were all `false` in that tutorial scene. This confirms scoped ranges on those templates only; it does not show a mod stage being selected or an AI module initializing or ticking.
- **Walker visual correction (local AssetBundle build, 2026-10-18)**: The authored Ironclad FBX/prefab has a static hierarchy and no walker rig. Because `visualRootPath` disables native renderers under that subtree, keeping it in the pack would suppress the articulated native GatlingWalker legs. The active sample pack now omits `visualAssetName`/`visualRootPath` for `sample-native:assault-walker`, retains its custom card Sprite, and uses the native articulated unit visual. Unity rebuilt the bundle with four vehicle overlays plus five cards; UnityPy and the offline reference validator confirmed four visuals and nine references. This is local build/serialized-bundle evidence only; the in-game presentation still needs a live match.

### Profile Isolation & Cleanup Invariants
- **Profile Backups**: Testing uses Profile 1 or temporary profiles. Pre-run files under `Profiles/` must be backed up before matches.
- **Steam Cloud Safeguard**: Steam Cloud is disabled by persistent user instruction. Preserve that setting during tests and cleanup; never toggle it.
- **Package Deployment Integrity**: Distributable ZIPs (`artifacts/packages/RogueCommandExpansionLoader-0.1.0.zip`) contain 9 entries: loader DLL, schemas, and content packs. Never include BepInEx core, Harmony, game assemblies, PDBs, or Unity `Library/` outputs in packages.

---

## 3. Core Architectural Invariants

### A. Production Building Invariant (1:1 Factory Mapping)
Every added unit must declare its own namespaced `productionBuilding`.
- The loader registers an additive factory row and dormant prefab clone, mapping that factory to exactly one produced unit.
- Native factory mappings are strictly preserved.
- The stage offers the factory blueprint in randomized reward pools; player purchase and placement produce the unit.
- Stage `unitIds` and AI `unitEntries` reference the produced unit ID.
- Five product/building pairs are currently declared in `sample-native-expansion`:
  - `sample-native:scout-tank` $\leftrightarrow$ `sample-native:scout-tank-factory`
  - `sample-native:heavy-artillery` $\leftrightarrow$ `sample-native:heavy-artillery-factory`
  - `sample-native:assault-walker` $\leftrightarrow$ `sample-native:assault-walker-factory`
  - `sample-native:skirmish-bike` $\leftrightarrow$ `sample-native:skirmish-bike-factory`
  - `sample-native:surge-tank` $\leftrightarrow$ `sample-native:surge-tank-factory`

### B. Stage-Scoped Blueprint Reward Pools & Roguelike Reward Mechanics
- **Roguelike Reward Selection**: In `ChooseCard.PresentCardsToChoose`, pool-backed rewards query `BlueprintPool.BlueprintsInPool`. The game removes owned/shown cards, seeds `SeededRandom`, and picks available candidates.
- **Pool Scoping**: The loader clones `BlueprintPoolList` and referenced `BlueprintPool` objects only when a declared stage lists custom units (`RCL_POOL_<sha256>`). Custom units appear only in pools scoped to the active stage; native and other stage pools reject them.
- **Filter Preservation**: Native per-pool filters (`blueprintFilter.IsBlueprintValid`) are preserved in cloned pools.
- **Starting Blueprints vs Regular Blueprints**: `isAllowedAsStartingBlueprint` sets the dedicated starting-deck flag on the unit row via `EntityBalancingStore.IsAllowedAsStartingBlueprint`; regular blueprints require stage pool inclusion.
- **Generic Fallback Scoping**: The no-pool reward fallback in `ChooseCard.PresentCardsToChoose` is scoped via `Game.StageMap.CurrentStage`. Only loader-owned factories declared by the active stage are eligible; unrelated mod factories are excluded.

### C. Additive AI Unit Rosters & Command Lifecycle
- **Additive Roster Structure**: `ContentAiProfileDefinition.unitEntries` and `productionBuildingEntries` add namespaced units and factories to `AiBase.entitiesInAiDeck`.
- **Capacity Enforcement**: `ScheduledBaseBuildAiModule` rereads active deck `MaxLiving` before each attempt, counts distinct living AI buildings via `ExistingControllers.AiBuildings()`, and respects capacity bounds.
- **Command Lifecycle Tags**: Harvester Raider (`sample:harvester-raider`) and Siege Assault (`sample:siege-assault`) use active command tags as truth: units executing that module's attack-move are skipped, becoming eligible again when their command tag clears.
- **Harvester Raider Module**: Locates player crystal harvesters (`ExistingControllers.Instance.Harvesters`), selects idle combat units, and dispatches attack-move harassment squads.
- **Siege Assault Module**: Scans discovered player structures (`PlayerRefineries`, `EngineeringHq`), gathers combat squads, and dispatches coordinated strike waves by priority (`ClosestBuilding`, `Refinery`, `Hq`).
- **Reactive Base Defense**: Scans attackers targeting AI base buildings within `defenseRadiusCells` (default 12 cells) and dispatches mobile defenders before weapon firing range.

### D. Engine Hooks, Serialization & TextMeshPro Fallback
- **TextMeshPro In-Memory Fallback**: Rogue Command 1.0.9 does not ship with a serialized `TMP Settings.asset` in `Resources/`. Without a valid instance, TextMeshPro crashes when layout components call `TMP_Settings.defaultStyleSheet`, `linebreakingRules`, and `fallbackFontAssets`. The loader hooks `Resources.Load(string, Type)` for `"TMP Settings"`, creating a dynamic `TMP_Settings` ScriptableObject with initialized `m_defaultStyleSheet` (refreshing `"Normal"` style), `m_linebreakingRules` (dictionaries initialized), empty non-null `fallbackFontAssets`, and assigns static `s_Instance` to eliminate layout crashes.
- **Shared `Resources.Load` Overload Resolution**: On Unity CoreModule (`6305c82c17ffe111a016e8d8ca3e8b2d9203980b4f58cb6e90171e5629994290` / `37d484f4-070f-4518-bfa9-9fa57b7a2871`), generic `Resources.Load<T>(string)` calls `Resources.Load(string, Type)`. Harmony hooks resolve only that single overload; custom assets return only when matching the requested `Type`, otherwise delegating to Unity's original loader.
- **Stage Node Distribution Overrides**: `ContentStageParameterOverride` validates and sets boolean fields on `StageParametersScriptableObject`, including `fillFirstLevelWithCardRewards`, `putDefaultCardRewardInCenter`, and `fillLastLevelWithShops`, plus branching and reward weight floats.
- **Diagnostic Trace Bounds & Loss Counters**: Bounded diagnostic traces (`MaxTraceCharacters = 4096`) place loss/omission counters (`omittedIdObservations`, `omittedPoolQueries`) immediately after `observedSources` before unbounded detail collections (`offeredProducts`, `globalFactories`, `stageFactories`, `pools`), ensuring tail truncation never drops loss diagnostics.
- **Serialized Scene Bootstrap Flow**: In `level0`, `ImageLOGO` (PathID 3) has `ImageFading` (PathID 66) whose `onFadeInEnd` triggers `LoadScene.LoadMainMenu` on `Loader` (PathID 5), transitioning cleanly from `EmptyBootUpScene` to `MainMenu`.

### E. Persistent Profile Unlock Progression & Savegame Architecture
- **Savegame Progression Store**: Rogue Command tracks unlocked blueprints, mutators, and cards persistently in `Profiles/Profile<n>/savegame.dat` via `ProfileUnlockedCardStore` (token `0x060004ad`).
- **Blueprint Unlock Verification**: In `ChooseCard.PresentCardsToChoose`, native cards query `ProfileUnlocks.IsBlueprintUnlocked(string)` before inclusion in regular pools.
- **Modded Blueprints Invariant**: Expansion units must not corrupt or irreversibly overwrite `savegame.dat`. When `isAllowedAsStartingBlueprint` is true, the factory blueprint is granted to the player's starting selection without mutating persistent profile unlock arrays.
- **Profile Isolation Protocol**: Automated test sessions must operate against isolated test profiles (`Profile 1` or temporary scratch profiles). Pre-run hashes of `savegame.dat` must be captured and restored following session termination to maintain baseline integrity.

---

## 4. REA Verified Reflection Signatures & CIL Tokens

Verified against pinned `Assembly-CSharp.dll` (`d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` / `db0e9462-128d-4296-9900-de18b4bd0c00`):

| Member Name | Declaring Type | Token | CIL Type / Signature | Verified Runtime Purpose |
|---|---|---|---|---|
| `get_HealthRatio` | `EntityController` | `0x060007e4` | `Single get_HealthRatio()` | Damage smoke (50%) and fire (25%) thresholds |
| `get_DestroyHasBeenStarted`| `EntityController` | `0x0600077d` | `Boolean get_DestroyHasBeenStarted()` | Validates death before destroying emitters |
| `get_InProductionCount` | `EntityController` | `0x060007df` | `Nullable<Int32> get_InProductionCount()` | Factory bay door opening during rollout |
| `get_CurrentAttackTarget` | `EntityController` | `0x060007fc` | `EntityController get_CurrentAttackTarget()`| Combat target for turret aiming |
| `IsEnemy` | `EntityController` | `0x06000844` | `Boolean IsEnemy(EntityController)` | Hostility filter before committing turret traverse |
| `get_EntityId` | `EntityController` | `0x060007c6` | `String get_EntityId()` (PascalCase) | Root entity identity |
| `_attackTarget` | `EntityAttack` | `0x040004a2` | `EntityController _attackTarget` | Authoritative attack target field |
| `_timeOfLastShot` | `EntityAttack` | `0x040004ab` | `Single _timeOfLastShot` | Ballistic clock driving recoil and muzzle flash |
| `onFireParticleSystem` | `EntityController` | `0x040004e1` | `ParticleSystem onFireParticleSystem` | Native fire particle discovery for fallback |
| `entityId` | `EntityInAiDeck` | `0x0400005b` | `String entityId` (camelCase) | Serialized AI deck unit references |
| `entityId` | `ProductionInfo` | `0x04000603` | `String entityId` (camelCase) | Production building product mapping |
| `IsAllowedAsStartingBlueprint` | `EntityBalancingStore` | `0x060004be` | `Boolean IsAllowedAsStartingBlueprint(String)` | Validates starting deck eligibility |
| `get_BlueprintsInPool` | `BlueprintPool` | `0x060000a2` | `List<String> get_BlueprintsInPool()` | Returns available pool blueprint IDs |
| `PresentCardsToChoose` | `ChooseCard` | `0x0600032e` | `Void PresentCardsToChoose(StageParameters)` | Roguelike reward selection entrypoint |
| `entitiesInAiDeck` | `AiBase` | `0x0400004e` | `List<EntityInAiDeck> entitiesInAiDeck` | Additive AI roster storage |
| `AiBuildings` | `ExistingControllers` | `0x060008d5` | `List<EntityController> AiBuildings()` | Living AI structure count for deck capacity |

### REA Evidence Ledger
- Parent Assembly Evidence: `ev_f31810ef788eb949c380fdc4129aebd5483da71763ee7836d3cede7d17a51572`
- Managed Projection: `ev_2f13567aa172a4942d778f6b6867261f2dddb513f172d95a76d039990f59681d`
- `EntityFactory` Members: `ev_8a69ec6d37445a4d4e95874af8b5c6047173f4519e4ea0268427f56a147b484b`
- `EntityBalancingStore` Value Types: `ev_6b1a3269bade89c9ebcdbc1435ad23d190c5f177c8c48771b58c176612f6544b`
- `EntityController` CIL Metadata: `ev_b8d4657ac454129250b202e6eb31305b15f679364f692c6adb08284ca2d40d64`
- Expansion Loader Binary: `ev_31e0021f23ea3178482412845099e35d6ac107e99f377ac02d042edf812c19fb`

---

## 5. Content Expansion Roster

### 5 Custom Units (`sample-native-expansion`)
All units provide English (`en-US`) and Spanish (`es-XL`) localizations, custom models, and card Sprites:
1. `scout-tank` (Sandlance Scout): Fast recon flanker, cost 70, HP 130, speed 14.5, sight 8. Template: `PCXBaseTank`. Factory: `scout-tank-factory`, Card: `Sprites/ScoutTankCard`.
2. `heavy-artillery` (Colossus Siege): Long-range siege bombarder, cost 300, HP 160, speed 9.0, range 16, cooldown 4.0, AoE 1.5, armor 2. Template: `ArtilleryTank`. Factory: `heavy-artillery-factory`, Card: `Sprites/HeavyArtilleryCard`.
3. `assault-walker` (Ironclad Walker): Heavy anti-chaff walker, cost 380, HP 420, speed 12.0, damage 4, cooldown 0.4, range 6, armor 1.0. Template: `GatlingWalker`. Factory: `assault-walker-factory`, Card: `Sprites/AssaultWalkerCard`.
4. `skirmish-bike` (Skirmish Strike Bike): High-mobility raider, cost 55, HP 75, speed 24.0, damage 32, AoE 1.8. Template: `BombBike`. Factory: `skirmish-bike-factory`, Card: `Sprites/SkirmishBikeCard`.
5. `surge-tank` (Surge Arc Tank): Electromagnetic disruption tank, cost 210, HP 150, speed 13.0, damage 12, range 4.5, cooldown 1.8, armor 2. Template: `LightningTank`. Factory: `surge-tank-factory`, Card: `Sprites/SurgeTankCard`.

### Factory Blueprint and Visual Scaffolding Rules
- Factory prefabs must provide standard 1:1 footprint bounds ($3.0\text{m} \times 3.0\text{m}$) with `Socket_Deploy_Unit` transform positioned at the door threshold.
- Units instantiate dormant at origin until factory build cycle finishes, triggering rollout slide animation.

### 3 Progressive Stages & AI Profiles
- **Stages**: `training-variant` (8 levels, reward 160), `fortified-frontline` (10 levels, reward 240, starting crystal 80), `core-battleground` (12 levels, reward 350, crystal 150).
- **AI Profiles**: `quick-hunter-extension` (rapid build interval 8s), `horde-swarm-assault` (swarm interval 12s), `raider-patrol-strike` (patrol strike interval 10s).
- **Reactive AI Pack (`sample-reactive-ai`)**: `sample:reactive-base-defense` module intercepting attackers on AI bases within `defenseRadiusCells` (default 12 cells).

---

## 6. Specialized Authoring Skills Portfolio

The repository equips 13 local skills under `.agents/skills/`:

| Skill Directory | Domain & Role | Primary Files & Tools | Key Invariants & Contracts |
|---|---|---|---|
| `rogue-command-3d-model-authoring` | Procedural 3D vehicles & structures | `RogueModelKit.py`, `generate_unit_model.py` | BMesh topological RGBA wear, soot $B=0.0 \to 1.0$, canonical sockets |
| `rogue-command-mech-rigging-authoring`| Skeletal rigging & animators | `biped-quadruped-rigging-guide.md`, `gait-cycle.md` | Rigid weighting $1.0$ before join, in-place $\Delta\text{pos} = (0, 0, 0)$ |
| `rogue-command-balance-simulation` | Combat balance & Lanchester math | `tools/simulate_combat_balance.py`, `content/packs/sample-native-expansion/content.json`, skill reference matrix | CEI bands (Scout 30–45, MBT 12–20, Siege 6–16, Mech 10–18), no armor lockouts or unreviewed kiting envelopes |
| `rogue-command-mutator-hazard-authoring`| Relics, mutators & hazards | `native-card-change-and-relic-schema.md` | 26 `ChangeableValue` attributes, trigger colliders ($\ge 0.5\text{s}$ tick) |
| `rogue-command-vfx-authoring` | URP particles & shaders | `vfx-prefab-recipe-book.md`, `urp-shaders.md` | Soft unlit alpha particles, dual-tier fallback in `RuntimeVisualVfxFallback` |
| `rogue-command-audio-authoring` | Spatial 3D audio & loops | `native-audio-integration-points.md` | `Socket_EngineAudio`, `Socket_WeaponAudio`, logarithmic roll-off (5–45m) |
| `rogue-command-unit-authoring` | Unit definitions & balance rows | `content/packs/sample-native-expansion/` | Additive rows, 1:1 factory mapping, bilingual localization |
| `rogue-command-stage-authoring` | Stage progression & reward pools | `StageParameters_*.asset`, `StageMap` | Cloned pool scoping, preserved native filters, Heat progression |
| `rogue-command-ai-authoring` | Tactical AI profiles & modules | `HarvesterRaiderAiModule`, `SiegeAssaultAiModule`| Deck capacity via `ExistingControllers`, command tags as truth |
| `rogue-command-assetbundle-authoring` | Unity AssetBundle creation | `Assets/Editor/RogueCommandVisualScaffolder.cs` | Standalone x64 bundles, script-free prefabs, URP shaders |
| `rogue-command-pack-validation` | Schema & manifest validation | `tests/RogueCommandExpansionLoader.ValidationTests`| JSON Schema Draft 2020-12, path traversal & bundle audits |
| `rogue-command-game-research` | REA assembly & asset inspection | `tools/asset_inventory.py`, `inspect_unity_objects` | Pinned SHA-256/MVID identity, CIL token verification |
| `rogue-command-steam-runtime-validation`| Live Steam testing & telemetry | `STEAM-RUNTIME-CONTROL.md`, BepInEx MCP | Reversible deployment, profile isolation, Steam Cloud toggle |

### Domain Technical Details

#### 1. 3D Model Authoring & BMesh Vertex Shading
- **Topological BMesh Vertex Wear**: Dihedral angle analysis ($\ge 35^\circ$) and normal convexity projection ($\Delta = (v_{other} - v_{edge}) \cdot \vec{n}_{f1}$) maps bright steel wear $G \in [0.7, 1.0]$ to convex edges and cavity AO $R \in [0.25, 0.40]$ to concave seams.
- **Soot Shading**: Channel B defaulted to $0.0$ (clean metal), scaling to $1.0$ at muzzles ($R = 0.60\text{m}$) and exhausts ($R = 0.45\text{m}$) with quadratic dropoff $(1 - d/R)^2$.
- **Procedural Enhancements**: Parametric catenary track sag curve $z(t) = height - s \cdot \sin(\pi \cdot t)$ (`--track-sag`), procedural BMesh faceted armor paneling (`--armor-plating`), and automated 2-bone IK solver constraints on `Bone_Shin_*` (`--ik-rig`).
- **Weapon & Preset Roster**: 16 CLI presets (including `beam_destroyer` and `vls_missile_cruiser`) and 11 procedural weapon assemblies (featuring `laser_beam` and `vls_missiles`).
- **Multi-System Sockets**: Sockets use canonical names with nested `FirePoint_0` under `Socket_MuzzlePrimary`, `Socket_EngineAudio`, `Socket_WeaponAudio`, and `Socket_FootContact_*` at $Z = 0.0$.
- Detailed architecture: [3D-MODEL-AUTHORING-ENHANCEMENTS.md](3D-MODEL-AUTHORING-ENHANCEMENTS.md).

#### 2. Mech & Walker Rigging
- Articulated skeletal armatures for bipedal walkers (8 bones) and quadrupeds (10 bones).
- Rigid vertex weighting ($1.0$ per armor plate) assigned on unbatched source meshes before `batch_and_join_by_material`.
- **In-Place Invariant**: $\Delta\text{pos} = (0, 0, 0)$ on `Bone_Root` across all frames of `Action_Walk`.
- Bakes 5 action clips (`Action_Idle`, `Action_Walk`, `Action_Drive`, `Action_FireRecoil`, `Action_Death`) wired to Generic Mecanim Animators.

#### 3. Combat Balance & Tactical Simulation
- `tools/simulate_combat_balance.py` provides discrete 1v1 and squad matchups across 651 native rows and expansion units. The CEI is `sqrt(DPS * BaselineEHP) / (CrystalCost + 0.3 * ProductionDuration) * 100`.
- The skill calibration matrix targets CEI bands: Scout 30–45, Main Battle Tank 12–20, Siege 6–16, and Mech 10–18.
- Flags armor lockouts: Ironclad Walker armor calibrated to 1.0 (CEI 14.9); Colossus cost calibrated to 300 (CEI 15.8).
- The refreshed 2026-10-18 `--all-mod-units` run reports CEI 39.8 for Sandlance (inside the 30–45 scout band) and no infinite-kiting flags after its range was lowered to 3.5. A 100-trial squad simulation of two Sandlance Scouts (140 credits) vs one `PCXBaseTank` (200 credits) reports 100% Scout wins with both Scouts surviving at 83.4% average HP and raises `ASYMMETRIC_SUPERIORITY`. Treat that as a static balance warning: this simulator uses `damage1` cooldown events, not full attack delivery or live target-selection behavior. No further stat retuning was made from this approximation; confirm the matchup in a controlled match before calling it balanced.

#### 4. Mutators, Relics & Battlefield Hazards
- Integrates with native `CardChangeScriptableObject` targeting the 26 canonical `ChangeableValue` attributes:
  `Health`, `Armor`, `Speed`, `Damage`, `AttackRange`, `Cooldown`, `SightRange`, `BuildTime`, `CrystalCost`, `HealPerSecond`, `ShieldCapacity`, `EnergyMax`, `EnergyRecharge`, `CritChance`, `CritMultiplier`, `SplashRadius`, `Knockback`, `Accuracy`, `Evasion`, `LifeSteal`, `SlowResistance`, `StunDuration`, `DotDamage`, `DotDuration`, `ProductionSpeed`, `RadarRange`.
- Registered into `RelicBalancingStore` or `ManageStartCardChanges`. Environmental hazards use trigger colliders (`isTrigger = true`) with amortized tick rates ($\ge 0.5\text{s}$). Dynamic Heat scaling limited to $\le 3\%$ major combat stat per level, maximum $+15\%$ `AttackRange`.

#### 5. VFX & Audio Authoring
- URP ParticleSystem prefabs use authored alpha materials where present. `RuntimeVisualVfxFallback` searches URP particle shaders first, then older/mobile and sprite fallbacks, and shares the fallback material across low-count emitters.
- Spatial 3D audio anchored to `Socket_EngineAudio` and `Socket_WeaponAudio` with logarithmic roll-off (min 5m, max 45m).

---

## 7. Steam Runtime Smoke & Control Procedures

When verifying loader or content behavior in a live Steam session:
1. **Preflight Health Check**: First locate `Rogue Command.exe` via `Get-CimInstance Win32_Process`, then ping the BepInEx MCP bridge via protocol 2.0. The bridge is in-process: no game process means no live bridge endpoint, so launch through Steam before diagnosing a ping failure. If the process exists and ping still fails, inspect the installed bridge files/log and repair the deployment; do not stop after reporting that it failed.
2. **Profile Isolation**: Resolve the active profile through `ProfileManager.CurrentProfileTextPath` under `Application.persistentDataPath`, back up its existing files, and check for `savegame.dat` there. Do not assume a selector under the game installation is authoritative.
3. **Steam Cloud Safeguard**: Preserve the user's existing Steam Cloud preference. For this project it is disabled by persistent user instruction; do not toggle it during a test.
4. **Reversible Deployment**: Back up the eight runtime files in the table below and `LogOutput.log` before deploying new plugin or bundle builds. The ZIP also contains a root-level `INSTALL.txt`; it is package documentation and is not copied into the game for a controlled smoke test.
5. **Post-Run Restoration**: Close the game normally and confirm its process has exited; verify and restore all eight runtime files to their original SHA-256 hashes and restore the original BepInEx log. Restore pre-existing profile files from the session backup if the test changed them, preserve files created during the test, and restore only the selector under `persistentDataPath` if that test explicitly changed it. Never toggle Steam Cloud as cleanup.
6. **Repeated UI Failures**: After repeated failed inputs or transitions, stop retrying the same action. Use the user's screenshot skill to capture the current desktop/game to the temp directory, inspect the visible scene, focus, dialogs, and overlay, then choose a different next action. Prefer bridge state for scene/object facts and the screenshot for visual focus or UI diagnosis.
7. **Background Game Captures**: `GetWindowRect` plus `CopyFromScreen` captures the pixels currently visible on the desktop; it does not capture an occluded window. On Windows, use `PrintWindow(hwnd, memoryDC, 2)` and read the returned GDI bitmap to capture the game's window without calling `SetForegroundWindow`. Record the foreground window handle before and after and verify that it is unchanged. In this session it returned a 1920×1080 image while the game was behind Codex. A black fade can be the actual game UI state; pair the image with a read-only bridge scene/object query instead of retrying capture or input blindly.

### Controlled Steam Runtime Verification Protocol (AI & Card Rewards)
When validating mod content during live Steam sessions, follow these precise experimental protocols:
- **Reactive AI Counterattack Dispatch**:
  1. Confirm simulation time advances (`Time.timeScale == 1.0f`).
  2. Use a naturally obtained live Player combat unit with `canNotBeAttacked = false`, `canNotBeTargetedByAi = false`, and `doNotRegisterGlobally = false`; do not use the debug/test spawner.
  3. Issue a native Attack order targeting an AI building whose `aiBaseIndex` matches the active profile.
  4. Ensure `CurrentAttackTarget` references the target building while the attacker is within `defenseRadiusCells` (default 12 cells).
  5. Ensure AI base 0 has an uncommitted combat unit with attack and movement enabled, no current target, no paused skill activation, and an idle or Move command within `defenseRadiusCells`.
  6. Confirm the defender transitions to the attacker's position and initiates weapon firing. Do not claim reactive behavior from profile attachment alone.
- **Stage Blueprint Reward Sampling**:
  1. Progression route maps sample rewards from active stage pools via `ChooseCard.PresentCardsToChoose`.
  2. Modded factory blueprints appear only when active stage definitions declare their unit IDs.
  3. When an offer appears, verify that placing the factory deducts crystals, starts rollout upon completion, opens factory doors via `InProductionCount > 0`, and instantiates the unit with custom 3D visuals.

### Content-First Expansion Philosophy & Boundary Guarantees
- **Native World Progression Variants**: Modded stages currently operate as progression variants on native world templates (`27 Circles`, `MainSceneDesert`, etc.), injecting custom seeds, parameters, and reward pools additively.
- **Prefab & Script Containment**: The authoring project at `unity/RogueCommandAssetAuthoring/` cannot attach compiled game scripts from `Assembly-CSharp.dll` inside the Editor. Visual prefabs must remain script-free, relying entirely on `RuntimeVisualDirector` attached dynamically by the loader.
- **Release Packaging Invariant**: Rebuild the release DLL, validate manifests/schemas, build the 9-entry ZIP via `Package-Loader.ps1`, and verify hashes before distributing.

---

### Authoritative 8-File Reversible Runtime Deployment Table

The package has nine ZIP entries. Eight are runtime files under `BepInEx`; `INSTALL.txt` is a separate package document. During a controlled runtime test, extract only these eight runtime entries to the game root. The content directory is nested beneath the plugin's own directory; there are no `pack.json` files or generated loader config in this package.

| Target Index | Deployment Destination in Steam Installation | Source File / Origin | Post-Run Verification |
|---|---|---|---|
| 1 | `BepInEx/plugins/RogueCommandExpansionLoader.dll` | `artifacts/plugin/RogueCommandExpansionLoader.dll` | Restore baseline SHA-256 |
| 2 | `BepInEx/plugins/RogueCommandExpansionLoader/content/packs/README.txt` | Generated by `tools/Package-Loader.ps1` | Restore baseline SHA-256 |
| 3 | `BepInEx/plugins/RogueCommandExpansionLoader/content/packs/sample-native-expansion/content.json` | `content/packs/sample-native-expansion/content.json` | Restore baseline SHA-256 |
| 4 | `BepInEx/plugins/RogueCommandExpansionLoader/content/packs/sample-native-expansion/bundles/assetbundle-manifest.bundle` | Native sample bundle manifest | Restore baseline SHA-256 |
| 5 | `BepInEx/plugins/RogueCommandExpansionLoader/content/packs/sample-native-expansion/bundles/sample-native-expansion.bundle` | `content/packs/sample-native-expansion/bundles/sample-native-expansion.bundle` | Restore baseline SHA-256 |
| 6 | `BepInEx/plugins/RogueCommandExpansionLoader/content/packs/sample-reactive-ai/content.json` | `content/packs/sample-reactive-ai/content.json` | Restore baseline SHA-256 |
| 7 | `BepInEx/plugins/RogueCommandExpansionLoader/content/schema/content.schema.json` | `content/schema/content.schema.json` | Restore baseline SHA-256 |
| 8 | `BepInEx/plugins/RogueCommandExpansionLoader/content/schema/README.md` | `content/schema/README.md` | Restore baseline SHA-256 |

```powershell
# Example: verify the loader after restoring the recorded baseline
Get-FileHash "C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\BepInEx\plugins\RogueCommandExpansionLoader.dll" -Algorithm SHA256
```

---

## 8. Development & Packaging Workflow

### Local Build and Validation Commands
```powershell
# 1. Compile C# Loader and run complete validation suite (132 checks)
dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .

# 2. Rebuild Release Plugin DLL and create distributable 9-entry ZIP
powershell -ExecutionPolicy Bypass -File tools/Package-Loader.ps1

# 3. Verify twin script binary parity (0 byte difference required)
fc.exe /b tools\RogueModelKit.py .agents\skills\rogue-command-3d-model-authoring\scripts\RogueModelKit.py
fc.exe /b tools\generate_unit_model.py .agents\skills\rogue-command-3d-model-authoring\scripts\generate_unit_model.py

# 4. Blender 4.5.14 dry-run archetype preflight & procedural features
# Supports --suspension-arms, --mantlet-boot, --spaced-skirts, and --wreckage-severity <light|medium|catastrophic>
.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools/generate_unit_model.py -- --batch-all-archetypes --dry-run
```

### Unity 2021.3.45f2 Authoring Project Architecture
- **Location**: `unity/RogueCommandAssetAuthoring/`
- **Engine Baseline**: Unity `2021.3.45f2` (Mono Standalone x64, URP).
- **Preserved Metadata**: `ProjectSettings/ProjectSettings.asset`, `ProjectSettings/TagManager.asset`, `Packages/manifest.json`, and Plastic SCM (`.plastic/`, `ProjectSettings/PlasticSCM.asset`). Never delete or overwrite with external project settings.
- **Headless Prefab Scaffolding Command**:
  ```powershell
  & "C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe" `
    -batchmode -quit `
    -projectPath "unity\RogueCommandAssetAuthoring" `
    -executeMethod RogueCommandExpansion.Authoring.VehiclePrefabBuilder.BuildPrefabFromCommandLine `
    -gameplayJson "artifacts\ColossusFortress.gameplay.json"
  ```
- **AssetBundle Build Command**:
  ```powershell
  & "C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe" `
    -batchmode -quit `
    -projectPath "unity\RogueCommandAssetAuthoring" `
    -executeMethod RogueCommandExpansion.Authoring.AssetBundleBuilder.BuildAssetBundles
  ```

### Research Commands for Asset & Runtime Inspection
```powershell
# Scan game serialized assets and extract progression objects
.venv\Scripts\python.exe tools\asset_inventory.py `
  --asset-root "C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\Rogue Command_Data" `
  --output-dir docs\research\generated

.venv\Scripts\python.exe tools\inspect_unity_objects.py `
  --game-root "C:\Program Files (x86)\Steam\steamapps\common\Rogue Command" `
  --asset-file resources.assets `
  --path-id 384970 --path-id 384977 --path-id 384981 `
  --output docs\research\generated\progression-objects.json

# Run balance simulator
.venv\Scripts\python.exe tools\simulate_combat_balance.py --all-mod-units
```

### Evidence Boundaries & Verification Discipline
- **Static Inspection**: Repository files, Python AST, managed assemblies via REA, Unity serialized asset inspection via UnityPy. Does not establish runtime execution.
- **Local Validation**: C# build output, validation test suite execution, ZIP archive audits, Blender background dry runs. Proves code correctness and packaging rules.
- **In-Game Runtime Evidence**: Observations from a live game session launched through Steam (AppID `1461910`). Only live evidence proves match functionality, card offers, factory production, and visual rendering.

### Git & Branch Policy
- Work committed on `codex/rogue-unit-balance-20261018` tracking `origin/codex/rogue-unit-balance-20261018`.
- Stage only task-owned paths explicitly (`git add <files>`); never use bulk `git add -A`.
- Keep BepInEx, Harmony, and game assemblies out of Git and distributable ZIPs.

---

## 9. Next Implementation Milestones & Task Roadmap

The generated unit models, prefab authoring outputs, compiled visual bundle, and distributable package already exist in the workspace under `unity/RogueCommandAssetAuthoring/` and ignored `artifacts/`. Do not repeat generation/export/package steps unless their source assets change. These files and prior local validation do not establish that they render or play correctly in a Steam run.

1. **Runtime acceptance setup**: Rogue Command is open from Steam in scene `Game` (PID `41992`, protocol 2.0 bridge live, 2026-10-18). The native `INICIAR` button was pressed once; the tutorial reached step 7 and `Entendido` advanced `FinishedTutorialsCount` to `7/26`; `TutorialController._isShowingTutorial=false`. The pause overlay alone did not stop the run: `InGameMenu._isShowing=true` while the HUD clock advanced and `Time.timeScale=1`. The temporary runtime helper then set `Time.timeScale=0`; the current recheck confirms `Time.timeScale=0`, `Time.time` unchanged at `537.5239` over 16.38 seconds, and the HUD clock stable at `03:51` in two no-focus captures. The simulation is now frozen and the overlay remains visible. `ChooseCard` is absent, so no randomized offer has been observed. The active selector is Profile 1; its pre-run files and all other profiles were backed up under `artifacts/runtime-smoke/baseline-20261018-160435`. Keep this process open and paused between controlled gameplay interactions; continue the reversible run from the current in-game state. Restore saved profile files after controlled acceptance and close normally only when the task is complete.
2. **Stage rewards and production acceptance**: Reach an eligible stage reward in a controlled Steam run; observe one of the stage-owned factory blueprints in a randomized offer, purchase/place it, and verify the matching namespaced produced unit and its visual. Confirm unrelated native factory mappings and native per-pool filters remain intact.
3. **Custom stage map acceptance**: Verify a declared mod stage appears in the progression/map flow with its configured world, map-size range, reward distribution, and stage-scoped AI roster. A loaded stage resource or log line alone is not sufficient.
4. **AI behavior acceptance**: Start a stage using a mod profile and observe initialization/tick of its compiled module, additive `unitEntries`/`productionBuildingEntries`, capacity-aware production, and its expected command dispatch. For reactive defense, capture an actual interceptor response to an attacker within the configured radius.
5. **Final review and delivery**: After runtime evidence is collected, restore the temporary deployment from its manifest by hash, inspect the package manifest, run the requested validation/build workflow, review only task-owned changes, then commit and push after verifying the tracked remote branch. Keep static inspection, local validation, and live Steam evidence labeled separately.

