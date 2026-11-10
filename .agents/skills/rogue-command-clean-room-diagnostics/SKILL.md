---
name: rogue-command-clean-room-diagnostics
description: "Clean-room in-game telemetry, diagnostic censuses, runtime gameplay command director, and BepInEx MCP bridge integration for Rogue Command (v1.0.9 / Unity 2021.3.45f2, Mono x64). [Diagnósticos runtime clean-room, censos de telemetría, director de comandos de gameplay y puente BepInEx MCP para Rogue Command]"
version: 1.13.0
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

- **Dedicated QA Profile**: Always test on `Profile2` (the dedicated QA profile without a tutorial). Never touch active player saves.
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
- **Native animation retarget**: `diagnostics_inspect_entity_visual` exposes `animation.nativeRigRetarget` with `present`, `isBound`, `isSuspended`, `configuredMappingCount`, and `boundMappingCount`. For a live unit, require `present == true`, `isBound == true`, matching configured/bound counts, and `isSuspended == false`; compare source/target Transform snapshots across movement and firing to prove pose transfer, because binding state alone is not visual proof.
- **Vertex-Wear Channels**: R cavity/AO ($\le 0.4$), G edge wear ($\ge 0.7$), B soot/mud ($\ge 0.5$), A team mask.

## Canonical Two-Level Variant Runtime Check

- For a clean-room variant check, use the existing `diagnostics_order_apply_visual_variant` command on a known entity and a declared Level 2 variant. Capture the returned variant/module count and `diagnostics_get_visual_census` / `diagnostics_inspect_entity_visual` readback, then apply `ResetVisualVariant` and confirm the Level 1 appearance, original transform scale, tint property blocks, and hardpoint group active states are restored.
- Confirm each mounted module sits under the corresponding canonical hardpoint group and that omitted slots remain empty. An apply/reset response without entity visual readback is not proof of the mounted appearance.
- Runtime status includes `modules=<count>` and `RuntimeVisualDirector.ActiveVisualModuleCount`; use these as supporting telemetry alongside the visual census. Static registry and AssetBundle checks are not live runtime proof.
