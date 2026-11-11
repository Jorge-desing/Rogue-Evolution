---
name: rogue-command-clean-room-diagnostics
description: "Clean-room in-game telemetry, diagnostic censuses, runtime gameplay command director, and BepInEx MCP bridge integration for Rogue Command (v1.0.9 / Unity 2021.3.45f2, Mono x64). [Diagnósticos runtime clean-room, censos de telemetría, director de comandos de gameplay y puente BepInEx MCP para Rogue Command]"
version: 1.16.0
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, clean-room, diagnostics, telemetry, BepInEx, MCP, aggregators, gameplay commands, censuses, multi-agent, hermes-plugin]
    related_skills: [rogue-command-harmony-policy, rogue-command-game-research, rogue-command-reverse-engineering, rogue-command-steam-runtime-validation]
---

# Rogue Command Clean-Room Diagnostics & Multi-Agent MCP Integration

Use this skill to inspect in-game state, read telemetry censuses, dispatch gameplay commands, and coordinate autonomous agents via the BepInEx MCP bridge.

---

## 1. Clean-Room Invariants & C# Zero-Allocation Guidelines

1. **Independent Authoring**: Never copy proprietary structures or external mod code. Use FNV-1a hashing and calibrated data.
2. **Zero External JSON Libraries in Runtime**: Do NOT use `Newtonsoft.Json` or `System.Text.Json` in the plugin runtime (`src/RogueCommandExpansionLoader/`). Use the manual string-escaped writer in `AgentDiagnosticsFormatter.cs`.
3. **Decoupled Primitive DTOs**: DTOs and command results must contain only primitive CLR types (`string`, `int`, `long`, `float`, `double`, `bool`, `IReadOnlyList<T>`). Never expose `UnityEngine.Color`, `Vector3`, `GameObject`, or `Component`.
4. **Omission & Loss Counters First**: In bounded diagnostic collections, place loss/omission counters (`omittedCount`, `lossFraction`, `omittedSessionCount`, `omittedLockCount`) at the start of the serialized JSON and DTO.
5. **Fail-Closed Reflection**: Null-check reflected members, catch exceptions, and return explicit failure DTOs (`success: false`).
6. **Direct Type Fast-Paths**: Test `if (target is EntityController ec)` before reflection. Directly access compiled properties (`ec.Position`, `ec.CurrentHealth`, `ec.MaxHealth`, `ec.CurrentArmor`, `ec.CurrentShield`, `ec.UniqueEntityId`, `ec.EntityId`, `ec.IsDestroyed`, `ec.DestroyHasBeenStarted`, `ec.IsEnemy`) to bypass reflection invocation overhead and avoid `Vector3`/`int` heap boxing.
7. **Dead-Entity Frame Short-Circuiting**: Evaluate death status once per frame in `RuntimeVisualDirector.Update()` (`IsEntityDead()`); when dead, update health transitions and immediately return, skipping redundant calls to audio, locomotion, animator, turret aiming, and antenna wobble.
8. **Thread-Static Argument Buffers**: For reflection fallback paths called frequently, reuse thread-static 1-element argument buffers (e.g. `_singleArgStatusEffect`, `_isEnemyArgs`) instead of allocating `new object[]` per call.
9. **Static Enum & Reflection Caching**: Statically cache array values like `(StatusEffect[])Enum.GetValues(typeof(StatusEffect))`, method parameter counts (`_healParamCount`), and parsed enum dictionary lookups to eliminate per-call heap churn.
10. **Amortized Hazard Tick Guard**: In `RuntimeHazardManager.UpdateTick()`, verify `anyReady` before gathering entities, avoiding candidate list allocations on ~98% of frames where tick intervals are active.
11. **Pre-Sized Collections & List Indexing**: Pre-size `List<T>` capacities from known entity set counts; pre-size `StringBuilder` in `AgentDiagnosticsFormatter.Serialize` and format `IList` via index-based `for` loops to eliminate `IEnumerator` allocations.

---

## 2. High-Performance MCP Aggregators

Use aggregators to minimize HTTP round-trips during agent decision cycles:

| Tool | Format | Purpose & Contents |
|---|---|---|
| `diagnostics_runtime_snapshot` | Composite JSON | Parallel multi-census snapshot with `_meta` timing block. Combines Battlefield, Progression, Health, MatchSummary, and Visuals. Filters via `getters`/`exclude`. |
| `diagnostics_runtime_scalars` | Flat key-value JSON | High-frequency telemetry: 44 scalar counters (living entities, squads, harvesters, skinned meshes, PBR units, range display segments). |
| `diagnostics_runtime_summary` | Compact JSON | Summary metrics and operational status across subsystems. |
| `diagnostics_call_getters` | Batch JSON | Executes a custom list of getters in parallel via `ThreadPoolExecutor`. |
| `diagnostics_list_runtime_getters` | Array JSON | Discovers all runtime getter names available on the bridge. |
| `diagnostics_get_visual_health_summary` | Composite JSON | Bounded single-pass audit of visual quality, surfaces, materials, and sockets (capped at 256 units, 4,096 nodes). |
| `diagnostics_get_combat_health_summary` | Composite JSON | Consolidates 11 combat, stance, affix, and targeting telemetry sources. |
| `diagnostics_get_entity_detail` | Detailed JSON | Deep-dive telemetry for a single unit/building across 11 diagnostic subsystems (`entityKeyId`). |
| `diagnostics_get_agent_lock_status` | JSON | Checks advisory lock ownership, TTL remaining, and session validity. |
| `diagnostics_diff_snapshots` | Diff JSON | Top-level comparison between two JSON snapshots with bounded added/removed/changed entries. |

---

## 3. Diagnostic Censuses Catalog

All censuses query live singletons through fail-closed reflection, format primitive DTOs, and emit omission counters first:

| Census Category | MCP Tool | Scope & Key Output Fields |
|---|---|---|
| **System Health** | `diagnostics_get_system_health` | Registry readiness, pack status, registered unit/AI counts, player/AI credits. |
| **Core Harmony Hooks** | `diagnostics_get_runtime_hooks` | Status of 65 installed patches (with 16 CIL Transpilers across core creation, placement ghosts, initialization, pooling, factory production, command loops, and lifecycle) and 28 simulation hooks/counters (`unitCreation`, `unitReuse`, `placementGhost`, `factoryProduce`, `entityRegistryReset`, `entityRegistered`, `entityUnregistered`, `commandExecuted`, `commandNextStep`, `commandReset`, `commandChainDeleted`, `lastExecutedCommandKind`, plus 16 transpiler counters: `unitCreationTranspiler`, `initEntityControllerTranspiler`, `entityOnInstantiatedTranspiler`, `entityRegisteredTranspiler`, `entityUnregisteredTranspiler`, `unitReuseTranspiler`, `entityReuseOnInstantiatedTranspiler`, `factoryProduceTranspiler`, `entityRegistryResetTranspiler`, `placementGhostTranspiler`, `entityInitTranspiler`, `entityDestroyTranspiler`, `commandExecutedTranspiler`, `commandNextStepTranspiler`, `commandResetTranspiler`, `commandChainDeletedTranspiler`). |
| **Battlefield & Rosters** | `diagnostics_get_battlefield_census`<br>`diagnostics_get_player_roster`<br>`diagnostics_get_ai_roster` | Living units, roles, positions, health ratios, orders, and threat scores split by alignment. Omission counters first. |
| **Combat & Match Summary** | `diagnostics_get_match_summary`<br>`diagnostics_get_base_integrity`<br>`diagnostics_get_threat_prioritization` | Unified combat/economy state, HQ health ratio, enemy structure counts, evaluated hostile threat scores. |
| **Production & Factories** | `diagnostics_get_production_census` | Living factories, production queues, build durations, progress ratios. |
| **Run & Level Progression** | `diagnostics_get_run_progression`<br>`diagnostics_get_level_map_state`<br>`diagnostics_get_navigator_state` | Current stage/level, Heat/Ascension tier, reward pools, LevelMap chosen path, frontier choosable fields, navigator status. |
| **Intermission Deep Reads** | `diagnostics_get_intermission_census`<br>`diagnostics_get_intermission_node`<br>`diagnostics_get_intermission_graph`<br>`diagnostics_get_intermission_reward_state` | OverWorldMap nodes (`level`, `field`, `isChoosable`, rewards, Boss `4:0`), forward routes to Boss, currencies (`coins`, `crystals`, `dropPods`, `rerolls`), and `ChooseCard` offers. |
| **Selection & Controls** | `diagnostics_get_selection_census` | Selected units, skill availability, focused target, and 10 control group counts. |
| **Spatial Grid & Fog** | `diagnostics_get_grid_metrics`<br>`diagnostics_evaluate_grid_cell`<br>`diagnostics_get_fog_state` | Map boundaries, cell traversability, elevation, hazard status, and Fog of War visibility. |
| **Camera & Audio** | `diagnostics_get_camera_census`<br>`diagnostics_get_audio_census`<br>`diagnostics_get_game_time_scale` | Ground viewport min/max coordinates, FOV, mixer volume sliders (Master/Music/Gameplay/UI), simulation time scale. |
| **Economy & Harvesters** | `diagnostics_get_harvester_census` | Harvester counts, crystal loads, load fractions, mining states (Mining/Returning/Idle). |
| **Catalogs** | `diagnostics_get_tech_catalog`<br>`diagnostics_get_entity_catalog` | Registered upgrades, research branches, relics, units, and factory definitions. |
| **Veterancy & Auras** | `diagnostics_get_veterancy_census`<br>`diagnostics_get_command_aura_census` | Unit combat XP, veterancy rank (0–5), stat boosts, active captain auras, follower links, remaining durations. |
| **Hazards & Loadouts** | `diagnostics_get_hazard_census`<br>`diagnostics_get_unit_loadout_census` | Trigger hazard zones (Firestorm, AcidPool, EmpField, CryoMist, NaniteField), active unit variants, stat overrides, and warmed prespawn pools. |
| **Stances & Affixes** | `diagnostics_get_tactical_stance_census`<br>`diagnostics_get_unit_affix_census` | Stances (`Standard`, `Aggressive`, `Defensive`, `HoldFire`, `Kiting`), elite traits (`Swift`, `Juggernaut`, `Overcharged`, `Vampiric`, `Bulwark`, `Volatile`). |
| **AI Architecture & Doctrine** | `diagnostics_get_ai_architecture_census`<br>`diagnostics_get_ai_director_census`<br>`diagnostics_get_ai_doctrine_census` | HUB-D 2.0 blackboard metrics (`AlliedDispersionRadius`, centroids, hazard zones), wave counter, attack queues, AI doctrine & aggression scale. |
| **Visual Render & Rig** | `diagnostics_get_visual_census`<br>`diagnostics_inspect_entity_visual`<br>`diagnostics_get_loaded_unit_visual_audit`<br>`diagnostics_get_unit_mechanics_census` | Active renderers, bounds, shaders (`VehiclePBR_VertexWear`), keywords, rig classification (`SkeletalArmature`, `NativeTransformRig`, `StaticVehicle`), bone count, sockets, Animator clips & parameters. |
| **Canonical Visual Variants** | `diagnostics_get_visual_variant_census`<br>`diagnostics_order_apply_visual_variant` | Per-unit Level 1 primary chassis and Level 2 tactical variant IDs, preset names, tint/scale, declared visual asset/root references, and omission counts. The current runtime applies tint and scale to the authored chassis; the census does not claim a geometry swap. |
| **Surface, Material & LOD** | `diagnostics_get_visual_surface_census`<br>`diagnostics_get_visual_material_census`<br>`diagnostics_get_socket_audit_census`<br>`diagnostics_get_collider_audit_census`<br>`diagnostics_get_particle_vfx_census`<br>`diagnostics_get_lod_audit_census` | Vertex wear channels (R cavity $\le 0.4$, G edge wear $\ge 0.7$, B soot/mud $\ge 0.5$, A team mask), texture bindings, socket roles, colliders, particle systems, and LOD levels. |
| **Performance & Timing** | `diagnostics_get_performance_census`<br>`diagnostics_get_frame_timing_census` | Live FPS, frame times, managed memory, budget verdict (Healthy/Degraded/Critical), windowed stutter/hitch metrics. |
| **Loader & Prespawn Audit** | `diagnostics_get_loader_audit_census` | Audits runtime readiness: balancing rows, visual prefabs, animator state, mechanics profiles, prespawn warming, and active battlefield mechanics. |
| **Multi-Agent Coordination** | `diagnostics_get_agent_coordination` | Active agent sessions, heartbeats, and advisory locks held on entities or camera. |

---

## 4. Runtime Gameplay Command Director

### Screenshot-free MCP control

- Use `diagnostics_get_ui_accessibility_census` to discover `Selectable` controls, Unity Text/TMP labels, scene-qualified hierarchy paths, active/visible/interactable state, and computed screen bounds. This is transform telemetry, not a screenshot or pixel observation.
- Use `diagnostics_wait_for_ui_text` for bounded state waits and `diagnostics_ui_click_text` by a unique accessible label or exact `hierarchyPath` (needed for unlabeled controls). Clicks go through `RuntimeUiClickDirector`'s real EventSystem raycast and fail closed on ambiguity, inactive/off-screen targets, missing handlers, or occlusion. The census `visible` value is diagnostic metadata; click delivery uses active/interactable/bounds checks and the actual raycast.
- `automation_run_sequence` runs at most 16 diagnostics tools in order, with bounded delays and a shared request-time budget; it excludes recursive sequence calls and synthetic jump/load tools. A timed-out in-flight request has unknown completion, so inspect its postcondition before retrying. Each operation still obeys its own QA profile, evidence, and mutation contract.
- Pass typed values between steps with `{"$ref":{"step":0,"path":"/entityKeyId"}}`. Step indexes are zero-based; the source must be earlier and successful, JSON Pointer values retain their type, and failed/forward/missing-path references stop or fail the dependent step. This supports a single spawn → native move → renderer-evidence wait sequence without copying runtime IDs.
- Use `system_launch_game` for Steam launch, `system_wait_for_game_bridge` for ping plus live scene readiness, and `system_get_game_process_state` to confirm process lifecycle. `diagnostics_request_graceful_exit` requires explicit `confirmExit=true` and normal save/profile checkpoint handling.
- `diagnostics_get_steam_cloud_state` is read-only. `diagnostics_set_steam_cloud_enabled` may change only Rogue Command's per-app Steamworks toggle, only after explicit user authorization; never alter account-wide Cloud. Verify local/remote selected-profile bytes before re-enabling.
- The loader sets `Application.runInBackground=true`; MCP scene/UI operations need no focus. Do not use Computer Use or captures as a fallback. If a state is missing, extend structured bridge telemetry or record the evidence as blocked/unknown.
- For animation sampling, prefer `diagnostics_wait_for_renderer_frame_evidence`: one call polls distinct direct Game-camera frame IDs against `activeClipContains` and `requiredEvidence` (`moving`, `stationary`, `deformation`, `retargetBound`, `visibleToCamera`), then attempts probe cleanup in `finally` after receiving a token. Check `probeStopped`/`stopError`; timeout returns `conditionMet=false`. This is telemetry, not proof of pixel appearance.

Dispatches validated, safe in-game actions for automated testing and agent execution:

### Tactical Movement, Combat & Stances
- `diagnostics_order_unit_move`: Issues movement order (`entityKeyId`, `targetX`, `targetZ`).
- `diagnostics_order_unit_attack`: Orders attack on target (`entityKeyId`, `targetEntityKeyId`).
- `diagnostics_order_unit_stop` / `diagnostics_order_unit_hold_position`: Halts orders or locks position (`entityKeyId`).
- `diagnostics_order_unit_attack_move`: Orders attack-move toward coordinates (`entityKeyId`, `targetX`, `targetZ`).
- `diagnostics_order_coordinated_assault`: Coordinates focused attack with up to `maxAttackers` (`targetEntityKeyId`, `attackerEntityKeyIds`, `maxAttackers`).
- `diagnostics_order_retreat`: Commands evasive retreat away from a threat (`entityKeyId`, `threatX`, `threatZ`, `distance`).
- `diagnostics_order_unit_patrol`: Dispatches native patrol route (`entityKeyId`, `waypointX`, `waypointZ`).
- `diagnostics_order_group_command`: Dispatches command (`Move`, `Stop`, `HoldPosition`, `AttackMove`) across multiple units (`entityKeyIds`, `commandType`, `targetX`, `targetZ`).
- `diagnostics_order_formation_move`: Synchronized formation movement (`Line`, `Wedge`, `Column`, `Box`, `Surround`) (`entityKeyIds`, `formationType`, `targetX`, `targetZ`, `spacing`).
- `diagnostics_order_unit_tactical_stance` / `diagnostics_order_group_tactical_stance`: Applies stance (`Standard`, `Aggressive`, `Defensive`, `HoldFire`, `Kiting`) (`entityKeyId` / `entityKeyIds`, `stanceName`).
- `diagnostics_order_apply_unit_affix` / `diagnostics_order_remove_unit_affix`: Grants or removes elite traits (`Swift`, `Juggernaut`, `Overcharged`, `Vampiric`, `Bulwark`, `Volatile`).
- `diagnostics_order_dispatch_ai_assault_wave`: Triggers coordinated AI assault wave (`waveNumber`, `aggressionBoost`, `targetBaseIndex`).
- `diagnostics_run_synthetic_gameplay_scenario`: Executes an isolated synthetic test seam in one call, verifying reconciliation, spawn, stances, loadouts, affixes, formations, and visuals before cleaning up.

### Intermission & Level Map Navigation
- `diagnostics_order_select_level_node`: Selects a choosable level node via screen coordinate raycast (`level`, `field`).
- `diagnostics_order_enter_level`: Full navigation director: selects choosable node and invokes native `LoadScene.LoadLevel()` (`level`, `field`).
- `diagnostics_order_trigger_load_level`: Direct invocation of `LoadScene.LoadLevel()` for current node or Boss.
- `diagnostics_order_choose_intermission_reward`: Chooses card/reward in `ChooseCard` by `rewardId` or `slotIndex`.
- `diagnostics_order_skip_intermission_reward`: Clicks skip reward button (`ChooseCard.ClickedOnSkipButton`).
- `diagnostics_order_reroll_intermission_reward`: Clicks reroll button (`ChooseCard.ClickedOnRerollButton`).
- `diagnostics_order_assign_intermission_upgrade`: Assigns upgrade in `AssignUpgradeToCardView` to deck card (`entityId`).
- `diagnostics_order_set_deck_overview_open`: Opens or closes `DeckOverview` screen (`open` bool).
- `diagnostics_order_navigator_pause`: Pauses or unpauses navigator simulation (`paused` bool).
- `diagnostics_jump_to_level` / `diagnostics_load_next_level`: Synthetic stage jump or level load.

`GetIntermissionGraphJson` includes the special boss at `(LevelMap.LevelCount, 0)`. `GetIntermissionNodeJson(level, field)` must report that node as `found=true`, `isBoss=true`, with no reward and one field; `isChoosable` follows `IsLastLevelBeforeBoss`, and its incoming count equals the preceding level's field count. The `GetAiModulesDiagnosticsJson` registry census is not evidence of an active stage profile or running AI; pair it with current stage/profile, director, and roster reads before claiming execution.

### Skills, Status Effects & Vitality
- `diagnostics_order_unit_active_skill`: Fires tactical skill (`entityKeyId`, `targetingMode` `"Self"`/`"TargetUnit"`/`"TargetPosition"`, `targetUnitKeyId`, `targetX`, `targetZ`).
- `diagnostics_apply_status_effect` / `diagnostics_clear_status_effect`: Applies/clears effects (`SlowedDown`, `OnFire`, `Stun`, `CombatBoost`) (`entityKeyId`, `statusEffectName`, `durationSeconds`).
- `diagnostics_adjust_entity_vitality`: Direct vitality modification (`entityKeyId`, `adjustmentType` `"Heal"`/`"RepairArmor"`/`"ChargeShield"`/`"Damage"`, `amount`, `ignoreArmor`).

### Veterancy, Auras, Hazards & Loadouts
- `diagnostics_order_grant_combat_xp` / `diagnostics_order_set_veterancy_level`: Grants XP or sets rank 0–5 (`entityKeyId`, `xpAmount`/`targetLevel`).
- `diagnostics_order_command_aura_pulse` / `diagnostics_order_clear_command_aura_links`: Dispatches aura pulse (`Inspire`, `Aegis`, `Swiftness`) or clears links (`captainKeyId`, `auraType`, `radius`, `durationSeconds`).
- `diagnostics_order_spawn_hazard_zone` / `diagnostics_order_clear_hazard_zones`: Spawns/clears hazard zones (`Firestorm`, `AcidPool`, `EmpField`, `CryoMist`, `NaniteField`).
- `diagnostics_order_apply_unit_loadout`: Switches unit variant (`"Standard"`, `"AP"`, `"HE"`, `"Speed"`, `"Siege"`, `"Reinforced"`).
- `diagnostics_order_apply_visual_variant`: Applies visual variant overlay (`entityKeyId`, `variantId`).
- `diagnostics_get_visual_variant_census`: Returns a bounded, deterministic unit matrix. Each row identifies its Level 1 primary chassis and Level 2 tactical variants, including preset/tint/scale and declared visual references. `omittedUnitCount` and `omittedVariantCount` precede the unit collection; `runtimeEffect` and `formAuthority` keep tint/scale application distinct from authored geometry.
- `diagnostics_order_warm_prespawn_pool`: Warms EntityFactory pool to prevent runtime hitches (`entityId`, `count` 1–50).
- `diagnostics_order_set_ai_doctrine`: Configures AI doctrine (`"Standard"`, `"Assault"`, `"Siege"`, `"Kiting"`, `"Fortify"`, `aggressionMultiplier` 0.1–5.0).
- `diagnostics_order_inject_ai_deck_unit`: Dynamically reinforces AI deck with unit quotas (`baseIndex`, `unitId`, `countAtStart`, `maxLiving`).
- `diagnostics_order_reconcile_loader_state`: Warms unwarmed pools, attaches missing directors, and reapplies profile squad defaults.

### Selection, Harvesters, Economy & Environment
- `diagnostics_select_units` / `diagnostics_deselect_all`: Controls unit selection by CSV key list or clears selection.
- `diagnostics_select_category`: Selects units by role (`"CombatUnits"`, `"Harvesters"`, `"IdleHarvesters"`, `"SkillBuildings"`, `"Factories"`).
- `diagnostics_assign_control_group` / `diagnostics_select_control_group`: Manages control groups 0 through 9.
- `diagnostics_order_harvester_harvest` / `diagnostics_order_harvester_return`: Directs harvesters to mine or return to refinery.
- `diagnostics_set_attack_wave_number` / `diagnostics_increment_attack_wave`: Overrides/steps AI attack waves.
- `diagnostics_toggle_ai_debug_visuals`: Toggles on-screen tactical command and role tags (`showTags`, `showCommands`).
- `diagnostics_move_camera` / `diagnostics_focus_camera_on_entity` / `diagnostics_set_camera_zoom`: Viewport navigation and zoom.
- `diagnostics_set_audio_volume`: Sets normalized volume (0.0–1.0) for Master, Music, Gameplay, UI.
- `diagnostics_set_fog_of_war` / `diagnostics_reveal_fog_of_war`: Toggles fog or reveals areas.
- `diagnostics_set_game_time_scale`: Controls simulation speed.
- `diagnostics_grant_credits` / `diagnostics_order_production` / `diagnostics_spawn_entity` / `diagnostics_trigger_match_end`: Economy and entity spawning.
- `diagnostics_apply_card_change` / `diagnostics_preview_card_change`: Applies/previews data-driven card modifications.
- `diagnostics_order_unit_visual_feedback`: Triggers visual feedback (recoil, hit impact, turret jam, bay door, footsteps).
- `diagnostics_order_unit_attachment`: Toggles visual modular attachments (`Armor`, `Shield`, `Radar`, `APS`).

### Multi-Agent Coordination Tools
- `diagnostics_register_agent_session`: Registers agent session (`agentId`, `agentRole`, `notes`).
- `diagnostics_ping_agent`: Heartbeat ping updating agent activity timestamp (`agentId`, `message`).
- `diagnostics_acquire_agent_lock` / `diagnostics_release_agent_lock`: Acquires/releases advisory locks (`agentId`, `resourceKey`, `durationSeconds`).

---

## 5. Command Director Fail-Closed Invariants

- **Finite Number Validation**: Validate every float coordinate, radius, and duration for `NaN`/infinity before dispatch; return `success: false`.
- **Nullable Hostility Delegates**: Write `unit.IsEnemy == null || !unit.IsEnemy(target)`. Null-check delegates before calling; catch exceptions.
- **Link Ownership on Command Auras**: Track `CreatedFollowerLinks` per aura state. On expiration or replacement, release **only** aura-created links; never clobber native captain relationships.
- **Intermission Selection Idempotency**: Call `IsAlreadySelected(rows, level, field)` before coordinate raycasting to avoid duplicate clicks after camera pan.
- **Intermission Frontier Semantics**: When `isLastLevelBeforeBoss` is true, the node frontier is legitimately empty; fail-closed behavior (`selected: false`) is correct.

---

## 6. Extending Clean-Room Diagnostics (The Three-Part Pattern)

When adding a new diagnostic census or command endpoint:
1. **Pure Formatter / DTO** (`AgentDiagnosticsFormatter.cs`): Declare pure primitive DTO; implement manual JSON serializer placing omission counters first. Store raw numbers; `WriteFiniteNumber` handles `NaN`/infinity safely.
2. **Bridge Method** (`LoaderDiagnosticsComponent.cs`): Add public MonoBehaviour method returning JSON string with null-safe delegation.
3. **Validation Unit Test** (`RuntimeAndDiagnosticsValidationTests.cs`): Add schema and fail-closed regression tests.
4. **MCP Registration**: Register tool in `tools/build_bepinex_mcp.py` and handler in `tools/bepinex_mcp_server.py`. Run `python tools/build_bepinex_mcp.py` and `python tools/bepinex_mcp_server.py --generate-configs`.
5. **UnityEngineTestStub**: If reading textures, colliders, or renderers, add stubs (`Texture`, `Collider.isTrigger`, `Rigidbody.isKinematic`) in `UnityEngineTestStub.cs`.

---

## 7. Multi-Agent Advisory Locking Protocol

When multiple autonomous agents operate on the same match (e.g., economy manager + combat tactician):
1. **Register Session**: Call `diagnostics_register_agent_session` with `agentId` and `agentRole`.
2. **Reserve Resource**: Call `diagnostics_acquire_agent_lock(agentId, "unit:<keyId>", durationSeconds=30)` before issuing commands.
3. **Verify Lock**: If lock returns `false`, check `diagnostics_get_agent_coordination` to wait or coordinate.
4. **Release Lock**: Call `diagnostics_release_agent_lock` immediately upon completing tactical orders.

---

## 8. Operational Safety & Scene Placement Rules

- **Saved-Profile Continuity**: For normal continuation testing, restart and select an existing tutorial-complete saved profile through the game's native profile UI; never reset or start clean because that re-triggers the tutorial. Preserve its save and restore any test-modified save/meta files after exit. A separate no-save QA profile is for tests that explicitly bypass `FirstRun`.
- **Stage Sequence Read**: At Intermission, inspect `stageSequenceRows` before selecting a route node. A resolved configured row can identify a later stage's declared AI profile without advancing; it remains configuration evidence, not AI execution. Preserve a `partial` warning when the endpoint returns unresolved rows beyond `totalStageSequenceCount`. For an authorized tutorial-complete clone/new run, snapshot the post-exit QA slot before restoring its original files and selector, then require an exact all-profile checkpoint; record a missing post-run `savegame.dat` instead of assuming the run persisted.
- **Preserve Unrelated Work**: Stage only task-owned files explicitly. Never run `git add -A`.
- **Installed DLL Parity**: Ensure `BepInEx/plugins/RogueCommandExpansionLoader.dll` SHA-256 matches the local Release build artifact before live validation.
- **Scene-Local Diagnostics Host**: `LoaderDiagnosticsComponent` is intentionally scene-local. On additive loads (`sceneLoaded` fires before `activeSceneChanged`), `EnsureDiagnosticsComponent` moves the GameObject via `SceneManager.MoveGameObjectToScene`.
- **MCP Exit Codes**: The Python MCP server returns exit code 0 even on JSON error payloads (`{"status": "error"}`). Always verify JSON payload contents.

---

## 9. Visual Render, Rig & Animation Verification Contracts

Live in-game visual verification requires checking `diagnostics_get_visual_census` and `diagnostics_inspect_entity_visual`:
- **Active Renderers**: `activeRendererCount > 0`, `isVisible == true`, non-zero bounding box ($X, Y, Z > 0.05\text{m}$).
- **Shader & Material Verification**: Shaders must match `RogueCommand/VehiclePBR_VertexWear` or `Standard`; keywords `_METALLICSPECGLOSSMAP` and `_NORMALMAP` verified.
- **Rig Hierarchy**:
  - `SkeletalArmature`: Bone hierarchy with `Bone_Root`, `Bone_Pelvis`, limbs, and `boneCount >= 4`.
  - `NativeTransformRig`: Discrete Unity transforms (`RotHor`, `ROTVERT`, `LEGS`).
  - `StaticVehicle`: Rigid vehicle mesh without armatures.
- **Canonical Sockets**: `Socket_MuzzlePrimary` (with nested `FirePoint_0`), `Socket_EngineAudio`, `Socket_WeaponAudio`, `Socket_FootContact_*` ($Z = 0.0$), `Socket_DamageSmoke`, `Socket_DamageFire`. Zero ambiguous socket roles (`hasAmbiguousRoles == false`).
- **Generic Avatar & Animator**: `hasAnimator == true`, `isAvatarValid == true`, standard parameters bound (`RCL_IsMoving`, `RCL_Fire`, `RCL_Death`).
- **RuntimeVisualDirector**: Reflects dynamic state (`isDamagedActive` at $\le 50\%$ HP, `isCriticalActive` at $\le 25\%$ HP, `isTurretJammed`, `currentRecoilOffset`, `doorOpenProgress`).
- **Native animation retarget**: `diagnostics_inspect_entity_visual` exposes state plus up to 16 `mappings`, each with exact paths, availability, current source/target local poses, and the computed target pose. `omittedMappingCount` is serialized before `mappings`. For a live unit, require `present == true`, `isBound == true`, matching configured/bound counts, and `isSuspended == false`; during movement/fire compare source motion with target motion and the computed pose. The `EntityController.Destroy` transpiler entry hook should set `isSuspended == true` before native destruction continues; its original-method and callback stack signatures are validated before IL injection, while the visual director's dead-state check remains a fallback. The computed pose is a math reference; telemetry alone does not establish rendered motion. Verify death appearance and pooled reuse separately.
- **Vertex-Wear Channels**: R cavity/AO ($\le 0.4$), G edge wear ($\ge 0.7$), B soot/mud ($\ge 0.5$), A team mask.

## Frame-Coherent Retarget Sampling

- Use one `run_bridge_batch` for `Animator.GetBool("RCL_IsMoving")`, the current clip, and mapped poses when possible. Calls issued separately may sample different Unity frames. The movement policy holds a positive state for 0.12 scaled seconds to bridge intermittent ticks; `Time.timeScale = 0` makes `Time.deltaTime` zero and clears the hold, so a paused false readback is not proof of a retarget defect. Briefly unpause only for a bounded sample, then restore the prior pause state.
- The renderer-frame probe reads the Animator's actual `RCL_IsMoving` bool together with clip/time, direct Game-camera callback state, retarget counts, and bounded `BakeMesh` vertex deltas. If a disposable motion helper is needed, label the result synthetic transform motion; it does not prove native pathfinding. No probe field is pixel/rendered-image evidence. If the stop call reports an expired token, query for remaining `RuntimeRendererFrameProbeBehaviour` components before claiming cleanup.

## Native Retarget Runtime Follow-up (2026-11-10)

- On a saved Profile1 boss run, `InspectEntityVisualJson` captured `Action_FireRecoil`, a triggered fire parameter, positive recoil, 8/8 mappings, and yaw/pitch target poses matching the computed poses. This is BepInEx runtime telemetry, not rendered appearance proof.
- A following Profile2 pass installed a temporary BepInEx postfix on `RuntimeHarmonyPatches.OnEntityDestroyTranspilerHook`. It read `IsSuspended=true` with 8/8 mappings still bound after the hook returned and before native `Destroy` continued. Two actors used direct diagnostic damage, not natural combat; this proves the lifecycle boundary only, not a death clip or rendered death appearance. Both test entities, the postfix, and its helper component were removed. Trace: `artifacts/runtime-smoke/restart-saved-profile-20261110T061923/retarget-destroy-entry-follow-up.json`.
- Static inspection of the pinned factory code found that `PrespawnEntity` fills the native pre-spawn stack, `InstantiateEntity` pops from it, and `EntityController.Destroy` decrements the prespawn count and calls `UnityEngine.Object.Destroy`; it does not push the destroyed instance back. Do not plan a natural death-to-stack-return assertion for this route. Static trace: `artifacts/runtime-smoke/native-retarget-natural-death-20261110T140359Z/native-prespawn-stack-static-evidence.json`.
- A Profile2 retarget probe observed `TriggerDeathAnimation` before `SuspendForDeath` at the native destroy hook, but used direct diagnostic damage and did not observe rendered frames. Keep the call-order result separate from natural death and rendered appearance: `artifacts/runtime-smoke/retarget-animation-restart-profile2-20261110T1605Z/destroy-hook-method-events.json`.
- Ordinary continuation must restart Rogue Command and select an existing saved profile through the native profile UI; never start a clean profile or touch tutorial flags/files. In this pass Profile2 tutorial completion, selector `2`, and savegame hash were preserved. After exit, the changed `Profile2/metaSavegame.dat` was saved as evidence, restored from its pre-run backup, and the all-profile checkpoint matched exactly. Keep test actors disposable and pause at `timeScale=0` between samples.

## Canonical Two-Level Variant Runtime Check

- For a clean-room variant check, use the existing `diagnostics_order_apply_visual_variant` command on a known entity and a declared Level 2 variant. Capture the returned variant/module count and `diagnostics_get_visual_census` / `diagnostics_inspect_entity_visual` readback, then apply `ResetVisualVariant` and confirm the Level 1 appearance, original transform scale, tint property blocks, and hardpoint group active states are restored.
- Confirm each mounted module sits under the corresponding canonical hardpoint group and that omitted slots remain empty. An apply/reset response without entity visual readback is not proof of the mounted appearance.
- Runtime status includes `modules=<count>` and `RuntimeVisualDirector.ActiveVisualModuleCount`; use these as supporting telemetry alongside the visual census. Static registry and AssetBundle checks are not live runtime proof.

### Death-visual retirement counters

- `GetRuntimeHooksStateJson()` now exposes `entityDestroyGameObjectTranspilerCount` for terminal callback invocations and `deathVisualRetirementCount` for successful visual holds; `entityDestroyTranspilerCount` remains the Destroy-entry callback count. Reset all three with the diagnostic reset path.
- A bounded event observer should capture direct-damage setup, trigger success, `DeathVisualRetirementPending`, clip name/length/time, target existence, and final destruction. Report telemetry separately from rendered pixels and natural combat. Include cleanup state for the observer, probe, Harmony patches, watchers, and `RuntimeVisualDirector` count. Example: `artifacts/runtime-smoke/retarget-death-visual-cloud-20261110170654/death-hold-runtime-result.json`.

### URP renderer frame probe

- Use `diagnostics_start_renderer_frame_probe`, `diagnostics_read_renderer_frame_probe`, and `diagnostics_stop_renderer_frame_probe` for short, screenshot-free renderer telemetry. The observer samples at most four skinned renderers, for at most 20 seconds, on `RenderPipelineManager.endCameraRendering` callbacks from `CameraType.Game` cameras without a `targetTexture`; it reports camera, layer, and frustum gates plus sampled local vertices from `SkinnedMeshRenderer.BakeMesh` (up to 128 vertices per sample).
- `boundsCenter` and `boundsSize` are JSON numeric arrays `[x,y,z]`. Parse each payload as strict JSON. A non-zero `maximumLocalVertexDelta` shows sampled mesh deformation only; callbacks, clip state, frustum inclusion, and `pixelCapture=false` do not prove rendered pixels or visible appearance. Always stop the probe and report omitted samples.

### Native Michsky UI callbacks

- The screenshot-free `diagnostics_ui_invoke_native_button` adapter handles exact `Michsky.UI.Reach.ButtonManager` controls that are not Unity `Selectable`s. Require exact GameObject name, the current active scene, expected `buttonText`, and an optional exact scene-qualified path when names repeat. The bridge must resolve one control and read active, enabled, `isActiveAndEnabled`, and `isInteractable` as true before calling the fixed `InvokeOnClick` callback. Unknown state, scene mismatch, inactive controls, mismatched text, truncation, or ambiguity means no callback.
- Live Profile2 proof: `Continue Run` matched `buttonText="Continuar la partida"` in `MainMenu`; `InvokeOnClick` returned success and the saved run advanced to `IntermissionScreen`, level 3, stage 0. This validates the native callback and state transition without screenshots; it does not prove the rendered menu appearance. Bridge handler/schema tests belong in `tests/python/test_bepinex_mcp_diagnostics.py`.

### Native-pathfinding retarget sample

- A diagnostic-spawned Ironclad Walker received a native Move command during a bounded 1.203-second unpaused window, reached its destination, and returned to `Time.timeScale=0`. The renderer probe saw 24 direct Game-camera frames, visible/in-frustum rendering state, 8/8 retarget mappings, `RCL_IsMoving=true` in 18/24 samples, and both Walk/Idle clips; a following HoldPosition sample saw the moving flag false for all 24 samples and returned to Idle. This is native pathfinding and Animator/retarget telemetry on a diagnostic clone, not production/factory/AI or rendered-pixel proof. The movement probe token expired before explicit stop; closing the game terminated its runtime observers. Traces: `artifacts/runtime-smoke/retarget-native-pathfinding-20261110/native-pathfinding-frame-result.json` and `native-pathfinding-held-idle-result.json`.

### Factory-produced retarget sample

- To validate the product path, spawn only the test factory with `SpawnEntityJson`, then issue `OrderFactoryProduceJson`; verify the product's `originEntityId` equals the factory ID. When duplicate unit IDs exist, use `EntityController.UniqueEntityId` as `entityKeyId`; `EntityId` alone is ambiguous.
- On saved Profile2, native production created `sample-native:assault-walker` (unique ID 254) from `sample-native:assault-walker-factory`. Its `NativeRigRetargetProfile` was bound 8/8 and unsuspended. Four skinned renderers each recorded 8/8 direct Game-camera samples with `RCL_IsMoving=true`, `Action_Walk`, visible bounds, and nonzero sampled local vertex deltas. HoldPosition returned to Idle with both movement flags false. This verifies factory output, locomotion telemetry, and retarget binding; it does not establish normal blueprint placement, AI scheduling, gait quality, or rendered pixels. Summary: `artifacts/runtime-smoke/retarget-factory-production-20261110/factory-retarget-runtime-summary.json`.
- `RuntimeRendererFrameProbe` uses unscaled camera callbacks and fills its per-renderer sample cap while the game is paused. Start it after the unit is moving, then pause only after the bounded capture; starting it in paused Idle can consume the sample budget before movement.

### AI stage association and runtime evidence

- `diagnostics_get_loader_audit_census` can show registered profiles and active instance counts; `diagnostics_get_ai_modules` reports registered modules. Neither is stage-attachment or module-execution evidence.
- Require a stable live stage/template/profile identifier before treating a route as an AI test target. `BattlePreviewWidget.EnemyInfoText` is localized display text and may not map to `nativeTemplateId`; without a verified mapping, stop progression and label the association `UNKNOWN`.
- In `IntermissionScreen`, zero director/roster/factory counts do not establish battle behavior. Continue evidence collection only after entering `Game` on a stage that is directly tied to the target profile, then capture its director, roster, scheduled-build events, queue, and produced-unit factory origin.
- The 2026-11-11 Profile2 probe read `Patrulla Slime PCX` in the preview but had no stable stage/profile ID. It stopped before another route node; no scheduled build or AI production was observed. Evidence: `artifacts/runtime-smoke/ai-scheduled-build-20261111T012405Z/live-ai-runtime-pass-summary.json`.

### Active stage AI association endpoint

- `LoaderDiagnosticsComponent.GetActiveStageAiAssociationJson()` delegates stable identity resolution to `ContentPackRuntimeLoader.GetActiveStageAiAssociationDiagnostic()` and serializes the result through `AgentDiagnosticsFormatter.SerializeActiveStageAiAssociation()`. The local MCP tool is `diagnostics_get_active_stage_ai_association` (server 1.4.5); include it in schema generation and the `match` snapshot when changing bridge surfaces.
- Resolve the current `StageMap` index against the captured loader progression sequence or persisted `rclStageOrderV1`. Return a qualified custom stage ID or native StageParameters asset ID. Fail closed with an explicit identity status/reason when sequence, index, or asset identity cannot be resolved. Do not use localized preview text.
- Keep stage-declared profile IDs separate from initialized loader-bound profile IDs and instance counts. Put omission counters before collections in bounded JSON. A resolved association is configuration evidence; zero runtime bindings at intermission is not evidence of AI execution or absence of later combat behavior.
- `GetStageAiAssociationSequenceDiagnostic()` exposes at most 64 captured StageMap identities to `stageSequenceRows`, including explicit unknown rows and `partial`/`unavailable` reasons. The sequence is configured progression evidence; runtime profile bindings remain a separate observation. Keep all omission counters before `stageSequenceRows` and do not infer identity from localized UI text.
- Runtime evidence from 2026-11-11: Profile2 resolved to native stage `native:StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit` at intermission, with no loader-declared IDs or loader-bound runtime profiles. The route was not advanced. See `artifacts/runtime-smoke/active-stage-ai-association-20261111T0218Z/runtime-evidence.json`.

### Filtered animation probe cleanup (2026-11-11)

- `diagnostics_wait_for_renderer_frame_evidence` now passes `activeClipContains` and `moving`/`stationary` into Unity sampling. Nonmatching frames do not use the per-renderer cap; positive deformation, binding, and visible-camera gates still apply to recorded samples.
- `StopRendererFrameProbeJson` is idempotent while its bounded stopped snapshot is retained for up to 30 seconds. If a stop token is unavailable, the wait tool searches for `RuntimeRendererFrameProbeBehaviour` and confirms cleanup only when the complete live component census is empty; otherwise preserve a failed cleanup result.
- On 2026-11-11, cloned QA Profile3 recorded 48 `Action_Walk` samples across four renderers, 8/8 bindings and direct-camera visibility in all frames, and positive `BakeMesh` delta in 44/48. A second stop succeeded and a live component search returned no observers. This is structured runtime telemetry, not pixels or AI execution: `artifacts/runtime-smoke/retarget-frame-wait-qa-profile3-20261111T030525Z/renderer-frame-filtered-idempotent-sequence-20261111T040237Z.json`.
- If a probe-start call times out or its result is truncated after Unity may have created the observer, census `RuntimeRendererFrameProbeBehaviour` before retrying; do not create duplicate probes. Prefer the five-argument filtered start (`sampleClipContains`, `sampleMovementState`) after a native Move is active so paused Idle frames do not consume the quota.
- Exact Release SHA `fe08d495c3236a174c31e89bfae236c79f6af42541404770845a1043934f1856` was re-exercised on Profile3: 32 filtered Walk/moving samples, 8 per renderer across four renderers, sampled 8/8 unsuspended retarget mappings, and observed positive post-baseline `BakeMesh` deltas. Stop and observer census both confirmed cleanup. The retained report is a bounded summary, not a raw frame dump: `artifacts/runtime-smoke/retarget-final-release-20261111T050500Z/final-release-retarget-summary.json`.

### Intermission guaranteed-card evidence (2026-11-11)

- `GetIntermissionRewardStateJson(maxCards)` reports whether `ChooseCard` is active/clickable and returns bounded offered-card rows; pair it with `GetIntermissionNodeJson(level, field)` for the exact pool. A BepInEx log line naming the injected product and replaced ID distinguishes the Harmony replacement from an unmodified natural draw.
- Check the card's localized name, `Image.sprite`, renderer enabled states, and Unity `Renderer.isVisible` through structured MCP reads. Report those as runtime component telemetry, never as pixel evidence. For the command relay sample, 14/14 card-preview skinned renderers were enabled/visible and the factory sprite reference was non-null; the chosen general fallback carried `Vehicle` role on `BP_CheaperRobos`.
- Keep the reward open if selection is outside scope. Intermission offer evidence says nothing about stage AI execution. Start a new run only from a QA clone that preserves completed tutorial state, and after native game exit restore the original QA profile/selector and require an exact all-profile checkpoint match.

### Current Release retarget Walk and Death probe (2026-11-11)

- Release SHA `2E740D387F600CAF0023ED7254F9F41ECF9E69E34E548328BFF147D250BC3DFB` was exercised on the reused QA Profile3 run. Native Move/Walk produced 32 filtered rows (8 each on four sampled renderers); all rows reported moving, Action_Walk, visible/in-frustum camera state, and retarget 8/8 bound and unsuspended. The Walker has five skinned renderers; the probe cap is four.
- Direct diagnostic lethal damage through a temporary typed helper produced 32 stationary Death rows with retirement pending, retarget suspended but bound 8/8, and sampled positive BakeMesh deltas in 7/8 rows per renderer. The scene retirement counter reached one; scene-wide destroy counters reached two. No natural-combat or pixel evidence follows from this probe.
- Verify the full game identity before relying on signatures. Generic MCP component calls could not convert AnimatorUpdateMode enum values or bind a null EntityController argument in this pass; a narrowly scoped typed helper on the disposable actor supplied the calls. Remove/census the helper and renderer observer, close normally, restore the selector through native profile UI, and compare profile checkpoints. Exact run data: `docs/research/RETARGET-CURRENT-RELEASE-RUNTIME-2026-11-11.md`.
- d576 Profile3 retarget pass: 48 filtered samples covered four renderers (12 each); all 48 were visible Action_Walk/moving frames with 8/8 unsuspended mappings, and all 44 comparable post-baseline deltas were positive. The probe auto-stopped at its 20-second cap; its start result field is `token` (not `sessionToken`). Store it immediately, stop before the cap, and census observers if the retained token expires. In this pass the explicit stop came too late for retained output, so no live component-census cleanup claim is made; normal process exit followed. `Navigator.Pause()` returned success while `isPaused` read false; confirm effective pause via `Time.timeScale` and stable `scaledTime`. Profile3 and selector restoration passed the complete checkpoint. See `docs/research/RETARGET-D576-RUNTIME-2026-11-11.md`.
- d576 death-frame follow-up (2026-11-11): a first filtered probe on the off-screen walker had 3,134 Game-camera callbacks but zero renderer samples and 9,044 frustum rejections; census found no remaining observers after expiry. Focusing the camera on the disposable actor made a preflight idle probe visible/in-frustum. The retry captured 32 early `Action_Death` rows (8 each across four of five skinned batches), all stationary with death retirement pending, suspended-but-bound 8/8 mappings, and visible/in-frustum; all four batches had positive sampled vertex deltas. It stopped at 1.16 unscaled seconds, then helper, actor, and observers were absent. A successful probe stop plus a live component census establishes observer cleanup; callbacks alone do not establish sample visibility. This was diagnostic lethal damage, not natural combat or pixel/death-quality evidence. Summary and full rows: `artifacts/runtime-smoke/retarget-d576-death-pool-20261111/`.
- In this pass the active scene was `Game`, progression mode `Default`, and the active stage resolved to native `StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit` with zero declared or runtime-bound loader AI profiles. Do not infer AI execution from the unit-card offer or registered profile census.
- d576 factory-produced Walk pass (2026-11-11): after a level transition, an active `LoadingHider` remained in the `Game` scene. Move calls before invoking its `Iniciar` button through the EventSystem did not count as gameplay movement. After the opener cleared, native Move generated 32 filtered Walk rows across four renderer batches; all were visible/in-frustum with 8/8 mappings bound, and each batch had 7/8 positive comparable BakeMesh deltas. The fifth renderer was outside the probe cap. Evidence: `docs/research/RETARGET-D576-FACTORY-WALK-RUNTIME-2026-11-11.md`.

### Native building-placement cell evidence (2026-11-11)

- Prefer the no-argument `diagnostics_inspect_active_building_placement` tool when a blueprint ghost is active. It captures the ghost footprint and native preflight in one main-thread call, avoiding coordinate drift and all screenshot/Computer Use paths. `success=false` and `committed=false` are expected for diagnostics.
- Read each `cellDiagnostics` row: `worldObject=Building` plus `occupiedByBuildingRampOrObstacle` proves occupied grid cells; `multiLayer` and `differentHeightLayer` identify local terrain-layer conflicts. Separate those local blockers from global refinery-distance or builder-spacing failures. Never infer the cause from the generic preview tint.
- Runtime QA on Profile3, level 4, stage 0 found the active field-medic factory footprint over buildings at grid `(1,-7)`, `(2,-7)`, and `(2,-6)`. The read-only radius-12 search checked 313 candidates and found no location passing every native rule. No building was queued and credits were unchanged. This is evidence for that run and radius only.
- If a footprint cell reports `worldObject=Building` or `occupiedByBuildingRampOrObstacle`, treat it as a real local blocker: do not commit or add a Harmony bypass. Reposition the native ghost and preflight the entire footprint again. If the placement controller is inactive, there is no current ghost to move; report that state instead of inferring a legal location.
- Exact 1.20.10 preflight at `(-165,-195)` returned one static `Building` cell at `(-17,-20)`, three `Ground` cells, and secondary fog on all four; the new `occupantEntities` field was present but empty on every row. Report the native blocker category and fog, not an invented entity identity. Non-empty live-controller occupant rows remain unverified. After a UI click, confirm the active `_blueprintInfo` because the click result alone may leave the old ghost selected.
- Pause evidence boundary: if `SetNavigatorPausedJson(true)` succeeds but the later `GetNavigatorStateJson` says `isPaused=false`, do not claim the native pause flag. In the 1.20.10 placement pass, `timeScale=0` and unchanged `scaledTime` across about four seconds proved the scaled simulation was frozen; `_isPaused` remained false.

### Retarget probe and entity identity lessons (2026-11-11)

- Start the filtered Death probe before lethal diagnostic damage. A destroyed actor can remain visually held while no longer resolving through `ExistingControllers`; a post-death probe start by entity key then fails. With `Animator.updateMode=Normal`, `Time.timeScale=0` also freezes the clip; use a bounded unpaused capture window or a disposable actor-local unscaled mode, then return the match to pause.
- `SpawnEntityJson` custom names do not guarantee unique `EntityController.EntityId` values. Before attributing an Attack result to a temporary target, compare the returned `targetX/targetZ` with its actual transform; repeated native IDs can resolve an existing unit elsewhere. The C1AE multi-clip sample and exact limitation are in `docs/research/RETARGET-C1AE-MULTICLIP-RUNTIME-2026-11-11.md`.
- Loader 1.20.10 adds `occupantEntities` per cell for matching live `ExistingControllers`, with native ID, unique runtime key, GameObject name, and footprint size. `omittedOccupantEntityCount` precedes detailed rows. Crystals/static objects remain identified by the cell's native category unless represented by a live controller.
- On the prior installed C1AE artifact, the Sentinel factory ghost overlapped the new RoboCrystalMill on two cells and crystal objects on two others. A read-only search checked 18 positions and found the first accepted site at `(-175,-165)`; native placement then committed it for 520 credits. The mill at `(-165,-195)` passed its four-cell preflight, cost 1000, and produced eight player-owned harvesters after the native build cycle. This is placement/runtime evidence for that saved Profile3 run only; see `docs/research/PLACEMENT-OBSTRUCTION-IDENTITY-2026-11-11.md`.
- Per-cell `occupantEntities` remain source/build evidence; they were not exposed by the previous C1AE placement run. Stable command keys were separately live-verified on the deployed 1.20.10 SHA `81573ca10d6b78c4b8e4ed9985f3987f17fcf1ebc9d6a19475b6c7547ec545b7`: two Walker instances shared the native `EntityId`, unique keys were 278/279, ambiguous lookup was rejected, and key 278 produced 8 filtered Walk rows across four visible renderer batches with unsuspended 8/8 bindings and positive comparable deltas. The probe stopped and the observer census was empty. Evidence: `artifacts/runtime-smoke/unique-key-retarget-release-20261111T173856Z/unique-key-retarget-summary.json`.
