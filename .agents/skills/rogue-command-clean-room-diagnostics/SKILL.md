---
name: rogue-command-clean-room-diagnostics
description: "Clean-room in-game telemetry, diagnostic censuses, runtime gameplay command director, and BepInEx MCP bridge integration for Rogue Command (v1.0.9 / Unity 2021.3.45f2, Mono x64). [Diagnósticos runtime clean-room, censos de telemetría, director de comandos de gameplay y puente BepInEx MCP para Rogue Command]"
version: 1.7.0
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, clean-room, diagnostics, telemetry, BepInEx, MCP, aggregators, gameplay commands, censuses, multi-agent, hermes-plugin]
    related_skills: [rogue-command-harmony-policy, rogue-command-game-research, rogue-command-reverse-engineering, rogue-command-steam-runtime-validation]
---

# Rogue Command Clean-Room Diagnostics & Multi-Agent MCP Integration

Use this skill when reading in-game state, building runtime telemetry censuses, dispatching tactical gameplay commands, or coordinating multiple autonomous agents (Hermes, Antigravity, Claude, Cursor, VS Code/Windsurf, Codex) communicating with Rogue Command via the BepInEx MCP bridge without visual captures or GUI dependencies.

---

## 1. Clean-Room Invariants

Every diagnostic utility, census DTO, and runtime command MUST follow these rules:

1. **Independent Authoring**: Never copy or decompile proprietary structures, closed palettes, or external mod code. Use open standards (FNV-1a) and independently calibrated data.
2. **Zero External JSON Libraries in Runtime**: Do NOT use `Newtonsoft.Json`, `System.Text.Json`, or third-party serializers in the game loader runtime (`src/RogueCommandExpansionLoader/`). Handcraft manual, string-escaped JSON writers in `AgentDiagnosticsFormatter.cs`.
3. **Decoupled Primitive DTOs**: DTOs and command result structs must contain only primitive CLR types (`string`, `int`, `long`, `float`, `double`, `bool`, `IReadOnlyList<T>`). Never expose `UnityEngine.Color`, `Vector3`, `GameObject`, or `Component` across the DTO interface so that tests run cleanly against minimal stubs.
4. **Omission & Loss Counters First**: In bounded diagnostic collections (rosters, censuses, catalogs, agent sessions), place loss/omission counters (`omittedCount`, `lossFraction`, `omittedSessionCount`, `omittedLockCount`) at the beginning of the serialized JSON and DTO. This prevents tail truncation from hiding data loss.
5. **Fail-Closed Reflection**: When reflecting game assembly members (`AiBehaviourManager`, `Harvest`, `UpdateFogOfWar`, `AudioMixerHelper`), wrap calls in null guards and try-catch blocks. Return explicit failure DTOs (`success: false`) instead of throwing unhandled exceptions.

---

## 2. High-Performance MCP Aggregators

When monitoring match progression or running autonomous agent loops, avoid issuing dozens of single-purpose HTTP requests. Use high-performance aggregators:

| Tool | Format | Purpose |
|---|---|---|
| diagnostics_runtime_snapshot | Composite JSON | Combines BattlefieldCensus, RunProgressionCensus, SystemHealth, MatchSummary, and VisualRenderCensus into a single payload. Ideal for match setup, tick ticks, and end-of-turn audits. |
| diagnostics_runtime_scalars | Flat key-value JSON | Emits high-density numeric telemetry (LiveBattlefieldEntityCount, LivePlayerUnitCount, LiveAiUnitCount, LiveAiBuildingCount, LiveHarvesterCount, RegisteredUnitMechanicsProfileCount, RangeDisplayGeometryMinimumSegments, RangeDisplayGeometryMaximumSegments, RangeDisplayCirclePointCount, LiveSkinnedMeshUnitCount, LiveAnimatedUnitCount, LivePbrShaderUnitCount). Ideal for high-frequency pacing monitors. |
| `diagnostics_get_agent_coordination` | Composite JSON | Emits multi-agent coordination telemetry (`activeSessionCount`, `activeLockCount`, `sessions[]`, `locks[]`). Ideal for checking whether other agents are holding locks before commanding units. |

---

## 3. Diagnostic Censuses

All censuses query live game singletons through fail-closed reflection and format primitive DTOs:

- **System Health** (`diagnostics_get_system_health`): Readiness of native registry, active content packs, registered units and AI modules, player and AI credit balances.
- **Threat Prioritization** (`diagnostics_get_threat_prioritization`): Active threat-prioritization behaviors, evaluated enemy units, friendly targets, and dynamic retargeting counters. Each tick also censuses AI units (`AiUnits()`) and allied player units (`PlayerUnits()`), exposing `AutoControlledAlliedUnitCount` / `_lastProcessedAiUnitCount` for diagnostics only; it does **not** mutate control/faction identity (allied units stay `IsControlledByPlayer=true`). An earlier reflection-based control-flag reassignment was reverted in `5b88d65`.
- **Production Queues** (`diagnostics_get_production_census`): Living factories, active production queues, build durations, and progress ratios.
- **Base Integrity** (`diagnostics_get_base_integrity`): Engineering HQ existence, player HQ health ratio, enemy structure count, and average structure health.
- **Battlefield Census** (`diagnostics_get_battlefield_census`): Living units, tactical roles, positions, and current health ratios.
- **Player & AI Rosters** (`diagnostics_get_player_roster`, `diagnostics_get_ai_roster`): Unit lists split by team alignment with orders, health, and threat values.
- **Match Summary** (`diagnostics_get_match_summary`): Unified view combining combat, economy, and AI status.
- **Run Progression** (`diagnostics_get_run_progression`): Current stage, level number, Heat/Ascension level, and offered blueprint reward pools.
- **Selection & Control Groups** (`diagnostics_get_selection_census`): Selected units, active skill availability, focused target, and counts across 10 control groups.
- **Fog of War** (`diagnostics_get_fog_state`): Fog enabled status, update intervals, grid dimensions.
- **Spatial Grid** (`diagnostics_get_grid_metrics`, `diagnostics_evaluate_grid_cell`): Map boundary coordinates, cell traversability, elevation, and hazard status.
- **AI Director** (`diagnostics_get_ai_director_census`): Attack wave counter, categorized command queues (attack, defend, scout, build, produce, other), queue summary, tick duration.
- **Camera Viewport** (`diagnostics_get_camera_census`): Viewport ground boundary min/max X/Z, center coordinates, FOV, orthographic status.
- **Harvester Telemetry** (`diagnostics_get_harvester_census`): Harvester counts, crystal loads, load fractions, mining states (Mining, Returning, Idle).
- **Audio Mixer** (`diagnostics_get_audio_census`): Volume slider levels (0.0 to 1.0) and dB attenuation for Master, Music, Gameplay, and UI.
- **Catalogs** (`diagnostics_get_tech_catalog`, `diagnostics_get_entity_catalog`): Upgrades, research branches, relics, units, buildings, and factories registered in balancing stores.
- **Veterancy & Combat XP** (`diagnostics_get_veterancy_census`): Live combat XP points, rank tiers (0 to 5), and calculated stat bonuses across living units.
- **Command Auras** (`diagnostics_get_command_aura_census`): Active captain command auras, subordinate linkage counts, and aura remaining durations.
- **Environmental Hazard Zones** (`diagnostics_get_hazard_census`): Active battlefield hazard zones (Firestorm, AcidPool, EmpField, CryoMist, NaniteField), trigger metrics, and damage per tick.
- **Unit Loadouts & Pre-Spawn Pools** (`diagnostics_get_unit_loadout_census`): Live unit loadout assignments, active combat variant IDs, unit stat profiles (Damage, Range, Speed, Cooldown), and EntityFactory anti-hitch pre-spawn pool quotas and warmed instances.
- **AI Tactical Doctrine & Deck Reinforcement** (`diagnostics_get_ai_doctrine_census`): Active AI combat doctrine, aggression scaling multiplier, and live deck unit quotas (`countAtStart`, `maxLiving`) per AI base.
- **Tactical Combat Stances** (`diagnostics_get_tactical_stance_census`): Active combat stances (`Standard`, `Aggressive`, `Defensive`, `HoldFire`, `Kiting`), movement speeds, and weapon ranges across living units.
- **Unit Elite Affixes & Traits** (`diagnostics_get_unit_affix_census`): Active elite modifiers (`Swift`, `Juggernaut`, `Overcharged`, `Vampiric`, `Bulwark`, `Volatile`) applied to living units.
- **Multi-Agent Coordination** (`diagnostics_get_agent_coordination`): Active agent sessions, heartbeats, and advisory locks held on entities or camera.
- **Visual Render, Rig & Animation Telemetry** (`diagnostics_get_visual_census`, `diagnostics_inspect_entity_visual`, `diagnostics_get_loaded_unit_visual_audit`): In-game and load-time evidence of 3D asset instantiation, active renderer components (`MeshRenderer`, `SkinnedMeshRenderer`, `ParticleSystemRenderer`), submesh and vertex counts, bounding boxes, assigned shaders (`RogueCommand/VehiclePBR_VertexWear`, `Standard`) and keywords (`_METALLICSPECGLOSSMAP`, `_NORMALMAP`), GPU instancing flags, LOD levels, rig architecture classification (`SkeletalArmature`, `NativeTransformRig`, `StaticVehicle`), root bone and bone count, canonical weapon/VFX/audio sockets (`Socket_MuzzlePrimary`, `FirePoint_0`, `Socket_WeaponAudio`, `Socket_EngineAudio`, `Socket_DamageSmoke`, `Socket_FootContact_*`), socket role ambiguity detection, Animator parameters (`RCL_IsMoving`, `RCL_Fire`, `RCL_Death`), active clips, playback speed, and `RuntimeVisualDirector` damage/jamming/recoil states. `diagnostics_inspect_entity_visual` automatically falls back to inspecting the registered loaded prefab template (`prefab:<entityId>`) when no living battlefield instance matches.
- **Visual Surface & Vertex-Wear Census** (`diagnostics_get_visual_surface_census`): Per-living-unit telemetry of renderer/material counts, vertex counts, shader names, and averaged plus distribution counts for vertex-color channels on `RogueCommand/VehiclePBR_VertexWear` surfaces: R cavity AO/micro-relief (`R <= 0.4`), G edge wear/welds (`G >= 0.7`), B thermal soot/mud (`B >= 0.5`), and A team mask. Omission counter first.
- **Visual Material & Texture Binding Census** (`diagnostics_get_visual_material_census`): Per-living-unit telemetry of renderer/material counts, material and shader names, bound texture names, and counts of materials exposing main/albedo, metallic-gloss, normal, and emission maps, plus shader keywords. Omission counter first.
- **Socket Audit Census** (`diagnostics_get_socket_audit_census`): Per-living-unit telemetry of canonical `Socket_*` transforms (muzzle primary, damage smoke/fire, foot contact, engine/weapon audio, hardpoints, radar, antenna), counts, and ambiguity detection. Omission counter first.
- **Collider Audit Census** (`diagnostics_get_collider_audit_census`): Per-living-unit collider counts (trigger vs solid), collider type names, and rigidbody kinematic state. Omission counter first.
- **Particle VFX Census** (`diagnostics_get_particle_vfx_census`): Per-living-unit particle system counts (active vs total), trail and line renderer counts, and particle system names. Omission counter first.
- **LOD Audit Census** (`diagnostics_get_lod_audit_census`): Per-living-unit LOD group counts, total LOD levels, and renderer names. Omission counter first.
- **Unit & AI Loader Runtime Integration & Audit** (`diagnostics_get_loader_audit_census`, `diagnostics_order_reconcile_loader_state`):
  - Units can declare `defaultTacticalStance`, `defaultLoadout` (`"Standard"`, `"AP"`, `"HE"`, `"Speed"`, `"Siege"`, `"Reinforced"`, or a local `variants[].id`), `startingAffixes`, and `startingVeterancyRank` (`0..5`) in `content.json`, applied automatically on spawn by `RuntimeUnitMechanicsDirector` and reset on pooled reuse.
  - AI profiles can declare squad-wide defaults (`doctrine`, `aggressionMultiplier`, `defaultUnitStance`, `defaultUnitLoadout`, `startingUnitAffixes`, `startingUnitVeterancyRank`) in `content.json`. These apply automatically to AI units on spawn (`RuntimeGameplayCommandDirector.ApplyAiProfileUnitDefaultsToEntity`) and on AI initialization (`ApplyActiveAiUnitDefaultsToAiUnits`) without overriding explicit unit-level defaults.
  - Every loaded unit, production building, and standalone building prefab is audited at load time via `RuntimeVisualCensus.RegisterLoadedPrefabVisualAudit` and exposed through `diagnostics_get_loaded_unit_visual_audit`.
  - AI profiles automatically warm `EntityFactory` pre-spawn pools on initialization (`RuntimeGameplayCommandDirector.TryWarmPrespawnPool`) and can bind the `sample:adaptive-commander` AI module (`AdaptiveCommanderAiModule`) to promote a Squad Commander, pulse command auras, modulate squad tactical stances (`Aggressive`/`Defensive`/`Standard`), apply preferred loadouts, and adapt AI tactical doctrine (`Assault`/`Fortify`).
  - `diagnostics_get_loader_audit_census` (`GetLoaderAuditCensusJson`) audits runtime readiness across all loaded units and AI profiles (balancing row registration, visual prefab audit, rig/animator state, runtime mechanics profile, prespawn pool warming, and active battlefield mechanics).
  - `diagnostics_order_reconcile_loader_state` (`OrderReconcileLoaderStateJson`) actively warms un-warmed `EntityFactory` pre-spawn pools, attaches missing `RuntimeUnitMechanicsDirector` / `RuntimeVisualDirector` components on living modded entities, re-applies unit and AI profile squad defaults, and synchronizes active AI doctrine.

---

## 4. Runtime Gameplay Command Director

The `RuntimeGameplayCommandDirector` class dispatches safe in-game actions for automated testing:

### Tactical Orders & Stances
- `diagnostics_order_unit_move`: Dispatches a movement order (`entityKeyId`, `targetX`, `targetZ`).
- `diagnostics_order_unit_attack`: Orders an attack on an enemy entity (`entityKeyId`, `targetEntityKeyId`).
- `diagnostics_order_unit_stop`: Halts the entity's active order (`entityKeyId`).
- `diagnostics_order_unit_hold_position`: Commands the unit to hold position and engage targets in range (`entityKeyId`).
- `diagnostics_order_unit_attack_move`: Orders an attack-move toward coordinates (`entityKeyId`, `targetX`, `targetZ`).
- `diagnostics_order_coordinated_assault`: Coordinates a bounded attack against one hostile target using only eligible hostile combat attackers (`targetEntityKeyId`, `attackerEntityKeyIds`, `maxAttackers`).
- `diagnostics_order_retreat`: Orders an evasive retreat away from a threat position (`entityKeyId`, `threatX`, `threatZ`, `distance`).
- `diagnostics_order_unit_patrol`: Dispatches a native patrol route with parallel X/Z waypoints (`entityKeyId`, `waypointX`, `waypointZ`).
- `diagnostics_order_group_command`: Dispatches a command (`Move`, `Stop`, `HoldPosition`, `AttackMove`) across multiple units (`entityKeyIds`, `commandType`, `targetX`, `targetZ`).
- `diagnostics_order_formation_move`: Dispatches a synchronized formation movement (`Line`, `Wedge`, `Column`, `Box`, `Surround`) to multiple units (`entityKeyIds`, `formationType`, `targetX`, `targetZ`, `spacing`).
- `diagnostics_order_unit_tactical_stance`: Assigns a tactical combat stance (`Standard`, `Aggressive`, `Defensive`, `HoldFire`, `Kiting`) to a unit (`entityKeyId`, `stanceName`).
- `diagnostics_order_group_tactical_stance`: Assigns a tactical combat stance across multiple units (`entityKeyIds`, `stanceName`).
- `diagnostics_order_apply_unit_affix`: Grants an elite trait/modifier (`Swift`, `Juggernaut`, `Overcharged`, `Vampiric`, `Bulwark`, `Volatile`) to a unit (`entityKeyId`, `affixName`).
- `diagnostics_order_remove_unit_affix`: Removes an active elite trait from a unit or clears all (`entityKeyId`, `affixName`).
- `diagnostics_order_dispatch_ai_assault_wave`: Manually triggers and coordinates an AI assault wave with elevated aggression (`waveNumber`, `aggressionBoost`, `targetBaseIndex`).

### Active Skills, Status Effects & Vitality Adjustments
- `diagnostics_order_unit_active_skill`: Fires tactical active skills (`entityKeyId`, `targetingMode` `"Self"`/`"TargetUnit"`/`"TargetPosition"`, `targetUnitKeyId`, `targetX`, `targetZ`).
- `diagnostics_apply_status_effect`: Applies effects like `SlowedDown`, `OnFire`, `Stun`, `CombatBoost` (`entityKeyId`, `statusEffectName`, `durationSeconds`).
- `diagnostics_clear_status_effect`: Clears active status effects (`entityKeyId`, `statusEffectName`).
- `diagnostics_adjust_entity_vitality`: Modifies health, armor, or shields directly (`entityKeyId`, `adjustmentType` `"Heal"`/`"RepairArmor"`/`"ChargeShield"`/`"Damage"`, `amount`, `ignoreArmor`).

### Veterancy, Command Auras & Environmental Hazards
- `diagnostics_order_grant_combat_xp`: Grants combat experience (XP) to a unit, promoting rank when thresholds are met (`entityKeyId`, `xpAmount`).
- `diagnostics_order_set_veterancy_level`: Sets a unit's veterancy level directly (`entityKeyId`, `targetLevel`).
- `diagnostics_order_command_aura_pulse`: Dispatches a dynamic command aura pulse linking nearby squad units (`captainKeyId`, `auraType` `"Inspire"`/`"Aegis"`/`"Swiftness"`, `radius`, `durationSeconds`).
- `diagnostics_order_clear_command_aura_links`: Clears command aura links for a captain entity (`captainKeyId`).

### Command Aura Renewal & Link Ownership

When pulsing or renewing a dynamic command aura, track which follower links were **created by that specific aura** (e.g. a `CreatedFollowerLinks` collection per aura state). On expiry or replacement:
- Release **only** the links created by the expiring aura.
- Preserve pre-existing native links (e.g. from `AddCaptain`, hierarchy, or prior native auras) that were not created by this mod's aura.
- Do **not** blindly clear all links for the captain — that clobbers legitimate native relationships and breaks native telemetry.
- Clear links only when the captain's aura is explicitly cleared or the aura state is removed.

Pitfall: Renewing an aura without tracking ownership either (a) leaves stale links from the expired aura attached to the captain, or (b) releases native links that the game depends on. Always distinguish created links from pre-existing ones before releasing.

### Command Director Fail-Closed Rules

- `TryOrderAttackMove` must resolve and validate the requested entity before calling native `AttackMove_Static`; a missing key must fail instead of commanding the previously selected unit.
- Hostility guards must be written as `unit.IsEnemy == null || !unit.IsEnemy(target)`; the inverted `unit.IsEnemy != null && !unit.IsEnemy(target)` form skips hostility when the delegate is null.
- Candidate filters that invoke `source.IsEnemy(candidate)` or `attacker.IsEnemy(target)` must check `== null` first; otherwise a null delegate throws and can abort the whole sweep or assault.
- Clamp floating-point distances, radii, durations, and magnitudes with `Mathf.Clamp` and preserve the clamped `float`; do not truncate through `int`.
- Validate every float coordinate, radius, magnitude, and duration for `NaN`/infinity before reflection or command dispatch, returning `success: false`.
- Game-speed, spawn, camera, audio, aura, doctrine, patrol, hazard, and group commands each need their own finite guard at method entry; `Mathf.Clamp(NaN, ...)` is not a substitute and can propagate `NaN`.
- Group commands with non-finite targets should fail before iterating members, even though the member commands also guard, so no partial work or confusing per-unit failures occur.
- `IsEnemy` delegates are nullable and can throw; check `== null` before invocation and wrap candidate evaluation in try/catch so one unusable candidate is omitted instead of aborting the sweep or assault.
- `SetEnumField` is best-effort and swallows parse failures; validate enum names against the decompiled type instead of relying on runtime no-ops.
- Map relic `CardChange.side` labels to the native `Side` enum in both `RuntimeCardChangeApplier` and `RelicContentRuntimeRegistrar.SetEnumField`; content validation accepts only the native labels `False`, `True`, and `Irrelevant`.
- Compute `omitted*Count` from requested versus dispatched/selected counts, not from already-filtered candidate counts.
- Test stubs whose `MoveTo` mutates `Position` invalidate assertions that compare a returned destination against `unit.Position` after the call; capture the original position before invoking the command.
- When a test uses an entity key, match the key type the assertion expects: unique numeric IDs resolve to the target but `TargetEntityKeyId`/result keys are normalized from the input string, so assert against the same identifier passed in (entity ID string vs numeric key).
### AI Module Eligibility and Hostility

- AI modules that dispatch combat units must filter candidates through `IsControlledByAi`, `IsUnit`, `IsCombatUnit`, `CanAttackAndIsAttackEnabled`, and `CanMoveAndMovementIsEnabled` before issuing orders. Support or repair modules should require the relevant `IsUnit`/`CanMove` constraints instead of combat-attack eligibility.
- Treat reflected `IsEnemy` as nullable: use `unit.IsEnemy == null || !unit.IsEnemy(target)`, and catch exceptions so one unusable candidate is omitted instead of aborting the whole sweep or assault.
- Do not promote player-roster entries to hostile targets unless hostility is positively established against a real source unit; never use a squad centroid or `Vector3` as the source.
- Counter-battery artillery classification requires `minAttackRange >= ThreatTriggerDistance`; a merely positive `minAttackRange` is not sufficient.
- `SquadFormationAiModule` must track only mod-created captain links and release those on destruction; never call `ReleaseAllCaptains` over native links.

### AssetBundle Registry

- `GlobalBundleRegistry.LoadBundleAsync(string, string)` must validate `bundleId` and `path` before locking.
- On a race where another caller registers the same `bundleId`, unload the duplicate bundle with `Unload(false)` before returning the existing instance; otherwise the path overload leaks an AssetBundle.
- Keep the stream and path overloads behaviorally aligned for duplicate handling.
- Treat `UnloadAll` as terminal for all returned bundle handles; do not use a handle after it has been unloaded.

### Native CardChange Contract

- Decompile `CardChangeScriptableObject` before changing `RuntimeCardChangeApplier`; do not assume field names or CLR types from the mod DTO.
- `side` is the `Side` enum (`False`, `True`, `Irrelevant`), not a faction string: map player/friendly/allied to `False`, enemy/AI/hostile to `True`, and all/any/neutral to `Irrelevant`.
- `multiplyValueWithMetaProgressionMultiplier` is a `string` multiplier key, not a `bool`; passing `false` compiles only if mistyped and silently leaves the field unset.
- `SetEnumField` is best-effort and swallows parse failures; validate enum names against the decompiled type instead of relying on runtime no-ops.
- `valueToChange` must be an exact `EntityBalancingStore.ChangeableValue` name.

### Environmental Hazards

- `diagnostics_order_spawn_hazard_zone`: Spawns a battlefield environmental hazard zone (`hazardType` `"Firestorm"`/`"AcidPool"`/`"EmpField"`/`"CryoMist"`/`"NaniteField"`, `x`, `z`, `radius`, `durationSeconds`, `tickDamage`) with trigger collider and amortized tick rate (>= 0.5s).
- `diagnostics_order_clear_hazard_zones`: Clears all active hazard zones and colliders.

### Dynamic Unit Loadouts & Anti-Hitch Pre-Spawn Pools
- `diagnostics_order_apply_unit_loadout`: Switches or applies a unit loadout/variant (`entityKeyId`, `loadoutId` `"AP"`/`"HE"`/`"Speed"`/`"Siege"`/`"Reinforced"`/`"Standard"`). Applies stat adjustments (Damage, Range, Cooldown, Speed, Armor) via the native stat modification pipeline and records active variant state.
- `diagnostics_order_warm_prespawn_pool`: Manually warms an EntityFactory pre-spawn pool (`entityId`, `count` 1 to 50), pre-allocating instances to eliminate runtime hitching during mass production.

### AI Tactical Doctrine & Deck Reinforcement
- `diagnostics_order_set_ai_doctrine`: Configures the active AI tactical doctrine (`doctrineName` `"Standard"`/`"Assault"`/`"Siege"`/`"Kiting"`/`"Fortify"`, `aggressionMultiplier` 0.1 to 5.0). Modulates AI update cadence, retarget intervals, and wave aggressiveness.
- `diagnostics_order_inject_ai_deck_unit`: Dynamically reinforces an AI base deck with production quotas (`baseIndex`, `unitId`, `countAtStart`, `maxLiving`), escalating AI capabilities during matches.

### Multi-Agent Coordination & Advisory Locks
- **Visual Render, Rig & Animation Telemetry** (`diagnostics_get_visual_census`, `diagnostics_inspect_entity_visual`, `diagnostics_get_loaded_unit_visual_audit`): In-game and load-time evidence of 3D asset instantiation, active renderer components (`MeshRenderer`, `SkinnedMeshRenderer`, `ParticleSystemRenderer`), submesh and vertex counts, bounding boxes, assigned shaders (`RogueCommand/VehiclePBR_VertexWear`, `Standard`) and keywords (`_METALLICSPECGLOSSMAP`, `_NORMALMAP`), GPU instancing flags, LOD levels, rig architecture classification (`SkeletalArmature`, `NativeTransformRig`, `StaticVehicle`), root bone and bone count, canonical weapon/VFX/audio sockets (`Socket_MuzzlePrimary`, `FirePoint_0`, `Socket_WeaponAudio`, `Socket_EngineAudio`, `Socket_DamageSmoke`, `Socket_FootContact_*`), socket role ambiguity detection, Animator parameters (`RCL_IsMoving`, `RCL_Fire`, `RCL_Death`), active clips, playback speed, and `RuntimeVisualDirector` damage/jamming/recoil states. `diagnostics_inspect_entity_visual` automatically falls back to inspecting the registered loaded prefab template (`prefab:<entityId>`) when no living battlefield instance matches.
- **Unit & AI Loader Runtime Integration & Audit** (`diagnostics_get_loader_audit_census`, `diagnostics_order_reconcile_loader_state`):
  - Units can declare `defaultTacticalStance`, `defaultLoadout` (`"Standard"`, `"AP"`, `"HE"`, `"Speed"`, `"Siege"`, `"Reinforced"`, or a local `variants[].id`), `startingAffixes`, and `startingVeterancyRank` (`0..5`) in `content.json`, applied automatically on spawn by `RuntimeUnitMechanicsDirector` and reset on pooled reuse.
  - AI profiles can declare squad-wide defaults (`doctrine`, `aggressionMultiplier`, `defaultUnitStance`, `defaultUnitLoadout`, `startingUnitAffixes`, `startingUnitVeterancyRank`) in `content.json`. These apply automatically to AI units on spawn (`RuntimeGameplayCommandDirector.ApplyAiProfileUnitDefaultsToEntity`) and on AI initialization (`ApplyActiveAiUnitDefaultsToAiUnits`) without overriding explicit unit-level defaults.
  - Every loaded unit, production building, and standalone building prefab is audited at load time via `RuntimeVisualCensus.RegisterLoadedPrefabVisualAudit` and exposed through `diagnostics_get_loaded_unit_visual_audit`.
  - AI profiles automatically warm `EntityFactory` pre-spawn pools on initialization (`RuntimeGameplayCommandDirector.TryWarmPrespawnPool`) and can bind the `sample:adaptive-commander` AI module (`AdaptiveCommanderAiModule`) to promote a Squad Commander, pulse command auras, modulate squad tactical stances (`Aggressive`/`Defensive`/`Standard`), apply preferred loadouts, and adapt AI tactical doctrine (`Assault`/`Fortify`).
  - `diagnostics_get_loader_audit_census` (`GetLoaderAuditCensusJson`) audits runtime readiness across all loaded units and AI profiles (balancing row registration, visual prefab audit, rig/animator state, runtime mechanics profile, prespawn pool warming, and active battlefield mechanics).
  - `diagnostics_order_reconcile_loader_state` (`OrderReconcileLoaderStateJson`) actively warms un-warmed `EntityFactory` pre-spawn pools, attaches missing `RuntimeUnitMechanicsDirector` / `RuntimeVisualDirector` components on living modded entities, re-applies unit and AI profile squad defaults, and synchronizes active AI doctrine.
- `diagnostics_register_agent_session`: Registers or updates an agent session in runtime (`agentId`, `agentRole`, `notes`).
- `diagnostics_ping_agent`: Heartbeat ping to verify bridge health and update agent activity timestamp (`agentId`, `message`).
- `diagnostics_acquire_agent_lock`: Acquires or renews an advisory lock on a resource (`agentId`, `resourceKey`, `durationSeconds`). Fails if already locked by a different agent until expired.
- `diagnostics_release_agent_lock`: Explicitly releases an advisory lock before TTL expiry (`agentId`, `resourceKey`).

### Selection & Control Groups
- `diagnostics_select_units`: Selects entities by CSV or key list (`entityKeysCsv`, `additive`).
- `diagnostics_deselect_all`: Clears active player selection.
- `diagnostics_select_category`: Selects units by category (`"CombatUnits"`, `"Harvesters"`, `"IdleHarvesters"`, `"SkillBuildings"`, `"Factories"`).
- `diagnostics_assign_control_group` / `diagnostics_select_control_group`: Manages control groups 0 through 9.

### Harvesters & AI Director
- `diagnostics_order_harvester_harvest`: Orders a harvester to claim the nearest crystal node and commence extraction (`harvesterKeyId`).
- `diagnostics_order_harvester_return`: Orders a harvester to deliver payload to nearest refinery (`harvesterKeyId`).
- `diagnostics_set_attack_wave_number` / `diagnostics_increment_attack_wave`: Overrides or steps AI attack waves.
- `diagnostics_toggle_ai_debug_visuals`: Toggles AI tactical command and role tags on screen (`showTags`, `showCommands`).

### Camera, Audio, Environment & Economy
- `diagnostics_move_camera` / `diagnostics_focus_camera_on_entity` / `diagnostics_set_camera_zoom`: Viewport navigation and FOV adjustments.
- `diagnostics_set_audio_volume`: Normalized slider volume adjustment for Master, Music, Gameplay, UI.
- `diagnostics_set_fog_of_war` / `diagnostics_reveal_fog_of_war`: Controls Fog of War visibility.
- `diagnostics_set_game_time_scale` / `diagnostics_get_game_time_scale`: Controls simulation speed.
- `diagnostics_grant_credits` / `diagnostics_order_production` / `diagnostics_spawn_entity` / `diagnostics_trigger_match_end`: Economy grants, factory orders, unit spawning, match completion.
- `diagnostics_apply_card_change` / `diagnostics_preview_card_change`: In-game data-driven card changes routed through native `EntityBalancingStore`.

---

## 5. Multi-Agent & Cross-Platform Integration

The BepInEx MCP server is built with universal cross-agent compatibility supporting stdio newline-delimited JSON and Content-Length HTTP-style framing.

### 5.1 Hermes Integration (Native Plugin & Skills)
Hermes agents can communicate with the bridge using three distinct channels:
1. **Hermes Plugin (`bepinex-mcp`)**: Installed at `C:\Users\Alex\AppData\Local\hermes\plugins\bepinex-mcp/` and enabled in `C:\Users\Alex\AppData\Local\hermes\config.yaml` (`plugins.enabled: [bepinex-mcp]`). All 182 tools are discovered as native MCP tools.
2. **Hermes Skill Catalog**: Registered under `C:\Users\Alex\AppData\Local\hermes\skills\rogue-command\rogue-command-clean-room-diagnostics/`.
3. **Hermes Direct CLI Execution**: When running in a shell or terminal without an active MCP connection:
   ```bash
| diagnostics_runtime_snapshot | Composite JSON | Combines BattlefieldCensus, RunProgressionCensus, SystemHealth, MatchSummary, and VisualRenderCensus into a single payload. Ideal for match setup, tick ticks, and end-of-turn audits. |
   python tools/bepinex_mcp_server.py --call diagnostics_ping_agent --args '{"agentId":"hermes-tactician","message":"alive"}'
   python tools/bepinex_mcp_server.py --call diagnostics_acquire_agent_lock --args '{"agentId":"hermes-tactician","resourceKey":"unit:scout_1","durationSeconds":30.0}'
   ```

### 5.2 Antigravity & Google Gemini CLI
- Fully registered under `bepinex_mcp` with 182 generated schemas in `C:\Users\Alex\.gemini\antigravity\mcp\bepinex_mcp/`.
- Call eagerly or lazily using `call_mcp_tool(ServerName="bepinex_mcp", ToolName="...", Arguments={...})`.

### 5.3 Anthropic Claude Desktop
- Configuration snippet located at `artifacts/mcp-configs/claude_desktop_config.json`:
  ```json
  {
    "mcpServers": {
      "bepinex_mcp": {
        "command": "python",
        "args": ["C:/Users/Alex/Documents/Mod Rogue Command/tools/bepinex_mcp_server.py"],
        "env": {
          "BEPINEX_MCP_URL": "http://127.0.0.1:8080/mcp/"
        }
      }
    }
  }
  ```

### 5.4 Cursor IDE & VS Code / Windsurf
- Cursor configuration generated at `.cursor/mcp.json` and `artifacts/mcp-configs/cursor_mcp.json`.
- VS Code / Windsurf configuration generated at `.vscode/mcp.json` and `artifacts/mcp-configs/vscode_mcp.json`.

### 5.5 Multi-Agent Advisory Locking Protocol
When two or more autonomous agents collaborate on the same match (e.g. Hermes managing strategic economy and base construction while Antigravity controls micro-combat):
1. **Session Announcement**: Call `diagnostics_register_agent_session` with your `agentId` and `agentRole`.
2. **Resource Reservation**: Before sending move/attack orders to a unit or adjusting the camera, call `diagnostics_acquire_agent_lock(agentId, "unit:<keyId>", durationSeconds=30)`.
3. **Lock Check**: If the lock call returns `success: false`, check `diagnostics_get_agent_coordination` to coordinate with the owning agent.
4. **Order Execution & Release**: Perform tactical micro and immediately call `diagnostics_release_agent_lock`.

---

## 6. Extending Clean-Room Diagnostics (The Three-Part Pattern)

### Test harness execution

- Build both projects first: `dotnet build src/RogueCommandExpansionLoader/RogueCommandExpansionLoader.csproj -c Release --no-incremental --artifacts-path artifacts/dotnet-build` and `dotnet build tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj -c Release --no-incremental --artifacts-path artifacts/dotnet-build`.
- Run the full harness: `dotnet exec artifacts/dotnet-build/bin/RogueCommandExpansionLoader.ValidationTests/release/RogueCommandExpansionLoader.ValidationTests.dll "C:/Users/Alex/Documents/Mod Rogue Command"` (pass the repo root as the single argument).
- Run a single suite: append `--suite <SuiteName>` (e.g., `--suite RuntimeAndDiagnostics`). The suite name must be passed after the repo root, not before it; passing it as a positional argument before the root causes a manifest-not-found error.
- The harness reports `N/M validation checks passed; K skipped.` The skip is typically a Windows symlink-privilege test and is not a failure.
- For TDD regression detection: temporarily mutate the source, rebuild, run only the new test with `--suite`, confirm it fails with the expected message, then restore the source and verify SHA-256 matches the pre-mutation hash.

When adding any new telemetry or command endpoint:

1. **Pure Formatter / DTO** (`AgentDiagnosticsFormatter.cs`):
   - Declare the pure struct/class DTO with primitive fields.
   - Implement manual JSON serialization emitting a `"kind"` discriminator and placing loss/omission counters strictly first.
   - For numeric fields, store the raw `float`/`double` in the view dictionary. `Serialize` -> `WriteValue` already routes `float`/`double` through `WriteFiniteNumber(StringBuilder, ...)`, which emits `null` for `NaN`/infinity and preserves finite precision. Do **not** call `WriteFiniteNumber(value)` as a dictionary value: it returns `void` and will not compile, and pre-converting loses the finite-formatting path.
2. **Bridge Method** (`LoaderDiagnosticsComponent.cs`):
   - Add a public MonoBehaviour method returning a JSON string.
   - Perform null-safe delegation to the DTO collector or `RuntimeGameplayCommandDirector`.
3. **Validation Unit Test** (`RuntimeAndDiagnosticsValidationTests.cs`):
   - Add a test verifying JSON schema structure, values, and fail-closed error handling.
   - Re-run the validation test harness (`dotnet run --project tests/... -c Release`).
4. **MCP Tool Registration**:
   - Add tool spec to `tools/build_bepinex_mcp.py` and handler in `tools/bepinex_mcp_server.py`.
   - Run `python tools/build_bepinex_mcp.py` to regenerate all schemas.
   - Run `python tools/bepinex_mcp_server.py --generate-configs` to keep agent manifests in sync.

### 6.1 Census additions that read Unity materials/textures

- When a new census reads `Material.mainTexture` or `Material.GetTexture(...)`, add a `Texture` stub class with `name`/`width`/`height` to `UnityEngineTestStub.cs` and add `mainTexture`/`GetTexture`/`SetTexture` members to the `Material` stub, otherwise the offline harness cannot construct the row.
- When a census reads `Collider`, `Rigidbody`, `TrailRenderer`, or `LineRenderer`, add corresponding stub classes to `UnityEngineTestStub.cs` (with the properties the census reads, e.g. `isTrigger`, `isKinematic`) before running the harness.
- Add the new `Runtime*.cs` file to `tests/.../RogueCommandExpansionLoader.ValidationTests.csproj` as an explicit `<Compile Include>` entry (the test project does not glob `src/`); the plugin project does glob and needs no entry.
- Keep the new tool count consistent across the skill, generated schemas, and agent manifests.

---

## 7. Operational Safety Rules

- **Dedicated QA Profile**: Always perform live testing on `Profile2` (the user's designated dedicated QA profile without a tutorial). Never touch user profile saves or bypass tutorial flags on active user profiles.
- **Preserve Unrelated Changes**: Never use `git add -A`. Stage only task-owned files explicitly.
- **Label Evidence Accurately**: Local build and test harness passes are local validation; in-game live MCP responses are runtime evidence.
- **Installed vs. Local DLL Hash**: Before attributing runtime behavior to a source change, compare `BepInEx/plugins/RogueCommandExpansionLoader.dll` SHA-256 against the local build artifact. A mismatch means the running game exercises stale code; deploy the updated DLL or abort the runtime step.
- **Session-End Deploy**: At the end of every session that changes the loader, rebuild in Release and copy `artifacts/plugin/RogueCommandExpansionLoader.dll` to `BepInEx/plugins/RogueCommandExpansionLoader.dll`, verifying SHA-256 matches. This keeps the game folder current for the next run.
- **Time Scale Is Not Pause**: `currentTimeScale == 0.1` is slow motion, not a confirmed pause. Reversible mutation smokes must require `currentTimeScale == 0` (and ideally a stable scaled clock across two reads) before issuing orders.
- **Profile Checkpoint Scope**: The Profile2 checkpoint covers `currentProfile.txt` plus `Profile1`, `Profile2`, `Profile4`, and `Profile5` (16 files). `Profile3` is not in the checkpoint. Compare the full file set, not just Profile2, after runtime work.

---

## 8. Diagnostics Host Scene Placement (Lifecycle)

The MCP bridge's `LoaderDiagnosticsComponent` is intentionally **scene-local** (never `DontDestroyOnLoad`) and recreated per scene. On **additive** scene loads, Unity fires `sceneLoaded` **before** `activeSceneChanged`, so `new GameObject("LoaderDiagnostics")` lands in the still-active previous scene and is destroyed when that scene unloads. `OnActiveSceneChanged` must therefore also (re)create or relocate the host.

Pattern:
- `EnsureDiagnosticsComponent(provider, targetScene)` accepts the target `Scene`; create the host there, and if `new GameObject` lands elsewhere, `SceneManager.MoveGameObjectToScene` it before adding the component. Abort (and destroy the transient host) if the move fails.
- Keep the existing `_diagnostics == null` guard and null-reset for idempotency; also reinitialize when the host's scene differs from the target.
- `ShouldEnsureOnSceneLoaded(hasHost, loadedSceneName, isSingleLoad, hostAlreadyInLoadedScene)`: on an additive load with an existing host, only re-ensure when the loaded scene is `Game` (or the host's own scene); unrelated additive scenes must not displace the host.
- Guard `provider == null` and `!scene.IsValid() || !scene.isLoaded` before touching Unity objects.

Pitfalls:
- A successful MCP response or a single isolated `find_objects_with_component` hit does **not** prove the host survived a scene transition. Recheck after the transition and after the previous scene unloads.
- The installed plugin DLL hash can differ from the local build. Before attributing runtime behavior to a source change, compare `BepInEx/plugins/RogueCommandExpansionLoader.dll` SHA-256 against the local build artifact; a mismatch means the runtime exercises stale code.
- Test the ordering with a stub where the active scene differs from the loaded scene; a stub that always returns the same active scene hides the additive-load bug.
- **Tool naming**: the Python MCP server exposes `find_objects_with_component` (no `scene:` prefix) and `scene:find_objects_with_component` (HTTP route). Use the unprefixed name when calling via the Python server; the short component name works because it resolves via `FindType`.
- **`mod_inject_class` parameter**: pass the full C# source as `classCode` (not `code`). The class must compile standalone. A minimal MonoBehaviour with `Application.Quit()` in `Awake` is the reliable close pattern when no UI button is available.

### 8.1 MCP exit code 0 is not functional success

The BepInEx MCP server and the Python wrapper return process exit code 0 even when the JSON payload contains `{"error": ...}` or `{"status": "error"}`. Always inspect the payload content and, when possible, verify the observable effect (component present in scene, census populated, order applied and restored). A ping returning `{"status": "ok"}` only proves the bridge is alive, not that diagnostics or commands work.

### 8.2 TDD regression-detection via temporary mutation

When adding a regression test for a lifecycle or ordering bug, temporarily mutate the source to reintroduce the bug (e.g., change `return host.scene.handle == targetScene.handle;` to `return false;`), rebuild, run only the new test, and confirm it **fails** with the expected message. Then restore the source (verify SHA-256 matches the pre-mutation hash) and rebuild. This proves the test detects the regression rather than passing vacuously. Document the expected failure message in the test or skill so future maintainers know what a regression looks like.

### 8.3 Full DLL deploy-test-restore cycle

When runtime evidence requires a code change that is not yet deployed:

1. **Predeploy manifest**: Record target path, size, and SHA-256 of the installed DLL; back up the installed DLL.
2. **Deploy**: Copy the local build over the installed DLL; verify hashes match.
3. **Launch**: Start the game through Steam (never the exe directly). Wait for bridge ping.
4. **Verify component in Game scene**: Call `find_objects_with_component` after the Game scene loads. A result of 1 (not 0) confirms the host survived the transition.
5. **Test diagnostics**: Call `diagnostics_get_system_health` or the specific census. A populated response confirms the bridge works in-game.
6. **Restore**: Close the game (via `mod_inject_class` with `Application.Quit()` if no UI close is available). Restore the original DLL from backup. Verify restored hash matches the manifest.
7. **Profile check**: Verify the profile selector and file hashes against the checkpoint. `metaSavegame.dat` changes are expected; flag only other changes.

Pitfall: The installed DLL hash can differ from the local build even after a successful build. Always compare before attributing runtime behavior to a source change.
---

## 9. Visual Render, Rig & Animation In-Game Evidence Contract

A unit's visual presence and correct rigging/animation in a live match cannot be inferred from clean compile, asset bundle manifests, or data-row registration alone. Autonomous agents must verify runtime visual evidence using diagnostics_get_visual_census and diagnostics_inspect_entity_visual:

### 9.1 Render Evidence
- **Active Renderer**: ctiveRendererCount > 0 and isVisible == true when in camera view frustum.
- **Renderer Classification**:
endererType confirms expected component (SkinnedMeshRenderer for rigged walkers, MeshRenderer for static vehicle chassis/turrets, ParticleSystemRenderer for exhaust/muzzle VFX).
- **Geometry Bounds**: Non-zero bounding box ounds (, Y, Z > 0.05\text{m}$) and realistic vertex/submesh counts.
- **Shader & Material Verification**:
  - materialCount >= 1
  - Shaders must match modded URP variants: RogueCommand/VehiclePBR_VertexWear or Standard.
  - Shader keywords must confirm PBR channels: _METALLICSPECGLOSSMAP and _NORMALMAP.
  - enableGPUInstancing == true verified where material batching is intended.

### 9.2 Rig & Hierarchy Evidence
- **Rig Architecture**:
  - SkeletalArmature: Bone hierarchy with Bone_Root, Bone_Pelvis, and limbs with oneCount >= 4.
  - NativeTransformRig: Discrete Unity transforms (RotHor, ROTVERT, LEGS).
  - StaticVehicle: Rigid vehicle body without bone armatures.
- **Canonical Sockets**:
  - Primary weapon: Socket_MuzzlePrimary containing nested FirePoint_0 child for projectile origin and muzzle flash.
  - Positional 3D audio: Socket_EngineAudio parented over engine deck and Socket_WeaponAudio parented at primary muzzle.
  - Locomotion contact: Socket_FootContact_L/R (or _FL/FR/RL/RR) at ground level  = 0.0$ for walkers.
  - Damage smoke/fire anchors: Socket_DamageSmoke and Socket_DamageFire.
- **Zero Socket Role Ambiguity**: hasAmbiguousRoles == false (mbiguousRoles list empty); multiple transforms claiming the same socket role disable runtime directors.

### 9.3 Animation & Visual Director Evidence
- **Generic Avatar & Animator**: hasAnimator == true, nimatorEnabled == true, and isAvatarValid == true.
- **Playback Telemetry**: ctiveClipName non-empty, clipLength > 0,
ormalizedTime progressing across game ticks, and playbackSpeed > 0.
- **Motion Parameters**: Standard parameters bound:
  - RCL_IsMoving (bool) toggling with flocking velocity.
  - RCL_Fire (trigger) firing on primary attack pulses.
  - RCL_Death (trigger) transitioning to death ragdoll/collapse clip.
- **RuntimeVisualDirector**: When attached, exposes real-time state:
  - isDamagedActive: triggers when health $\le 50\%$.
  - isCriticalActive: triggers when health $\le 25\%$.
  - isTurretJammed: jamming flag during weapon malfunctions.
  - currentRecoilOffset: barrel displacement during fire recoil.
  - doorOpenProgress: deploy/bay doors opening ratio ($[0.0, 1.0]$).


### 9.4 Runtime Unit Mechanics & Visual Director Synchronization

Units with active combat mechanics (`RuntimeUnitMechanicsDirector`) and visual representations (`RuntimeVisualDirector`) synchronize state dynamically during live matches:
- **Status Effect Reactions in `Update()`**:
  - `Stun`: Jams weapon turret, freezes locomotion animation and halts radar traverse.
  - `OnFire`: Triggers damage fire/smoke emitters and procedural soot vertex color blending.
  - `CombatBoost` / `Overclocked` / `Turbo`: Multiplies radar mast rotation speed to $2.5\times$ RPM.
- **Combat Tactical Stance Modulations**:
  - `HoldFire`: Suppresses weapon traverse and fire recoil animations.
  - `Aggressive`: Increases turret traverse speed by $+50\%$.
  - `Defensive`: Auto-deploys defensive modular attachments (`Armor`, `Shield`, `APS`).
- **Telemetry & Diagnostic Censuses**:
  - `diagnostics_get_unit_mechanics_census`: Snapshots living unit mechanics, stances, veterancy, shield regeneration, critical hit chance, and visual director overrides (with `omittedUnitCount` placed strictly first).
  - `diagnostics_order_unit_visual_feedback`: Commands localized weapon recoil, critical hit impacts, turret jamming overrides, bay door toggles, and footstep ground pulses.
  - `diagnostics_order_unit_attachment`: Toggles visual hardpoint attachments (`Armor`, `Shield`, `Radar`, `APS`) dynamically.

### 9.5 Visual Surface & Vertex-Wear Census Implementation

- Channel thresholds are contract-derived, not cosmetic: R <= 0.4 counts micro-relief/cavity, G >= 0.7 counts weld/edge wear, and B >= 0.5 counts mud/thermal soot.
- Unity test stubs must expose `Mesh.colors` (and `colors32` when used) before asserting vertex-color channels; otherwise the census has no readable vertex-wear data.
- Guard nullable `sharedMaterials` and inactive renderers returned by `GetComponentsInChildren<Renderer>(true)`.
- Keep `omittedUnitCount` before renderer, material, vertex, shader, and channel details.
