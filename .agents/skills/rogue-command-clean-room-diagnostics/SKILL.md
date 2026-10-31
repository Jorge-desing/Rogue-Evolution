---
name: rogue-command-clean-room-diagnostics
description: "Clean-room in-game telemetry, diagnostic censuses, runtime gameplay command director, and BepInEx MCP bridge integration for Rogue Command (v1.0.9 / Unity 2021.3.45f2, Mono x64). [Diagnósticos runtime clean-room, censos de telemetría, director de comandos de gameplay y puente BepInEx MCP para Rogue Command]"
version: 1.0.0
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, clean-room, diagnostics, telemetry, BepInEx, MCP, aggregators, gameplay commands, censuses]
    related_skills: [rogue-command-harmony-policy, rogue-command-game-research, rogue-command-reverse-engineering, rogue-command-steam-runtime-validation]
---

# Rogue Command Clean-Room Diagnostics & MCP Integration

Use this skill when reading in-game state, building runtime telemetry censuses, dispatching tactical gameplay commands, or communicating with Rogue Command via the BepInEx MCP HTTP bridge (`http://127.0.0.1:8080/mcp/`) without visual captures or GUI dependencies.

---

## 1. Clean-Room Invariants

Every diagnostic utility, census DTO, and runtime command MUST follow these rules:

1. **Independent Authoring**: Never copy or decompile proprietary structures, closed palettes, or external mod code. Use open standards (FNV-1a) and independently calibrated data.
2. **Zero External JSON Libraries in Runtime**: Do NOT use `Newtonsoft.Json`, `System.Text.Json`, or third-party serializers in the game loader runtime (`src/RogueCommandExpansionLoader/`). Handcraft manual, string-escaped JSON writers in `AgentDiagnosticsFormatter.cs`.
3. **Decoupled Primitive DTOs**: DTOs and command result structs must contain only primitive CLR types (`string`, `int`, `long`, `float`, `double`, `bool`, `IReadOnlyList<T>`). Never expose `UnityEngine.Color`, `Vector3`, `GameObject`, or `Component` across the DTO interface so that tests run cleanly against minimal stubs.
4. **Omission & Loss Counters First**: In bounded diagnostic collections (rosters, censuses, catalogs), place loss/omission counters (`omittedCount`, `lossFraction`) at the beginning of the serialized JSON and DTO. This prevents tail truncation from hiding data loss.
5. **Fail-Closed Reflection**: When reflecting game assembly members (`AiBehaviourManager`, `Harvest`, `UpdateFogOfWar`, `AudioMixerHelper`), wrap calls in null guards and try-catch blocks. Return explicit failure DTOs (`success: false`) instead of throwing unhandled exceptions.

---

## 2. High-Performance MCP Aggregators

When monitoring match progression or running autonomous agent loops, avoid issuing dozens of single-purpose HTTP requests. Use high-performance aggregators:

| Tool | Format | Purpose |
|---|---|---|
| `diagnostics_runtime_snapshot` | Composite JSON | Combines `BattlefieldCensus`, `RunProgressionCensus`, `SystemHealth`, and `MatchSummary` into a single payload. Ideal for match setup, tick ticks, and end-of-turn audits. |
| `diagnostics_runtime_scalars` | Flat key-value JSON | Emits high-density numeric telemetry (`LiveBattlefieldEntityCount`, `LivePlayerUnitCount`, `LiveAiUnitCount`, `LiveAiBuildingCount`, `LiveHarvesterCount`, `RegisteredUnitMechanicsProfileCount`, `RangeDisplayGeometryMinimumSegments`, `RangeDisplayGeometryMaximumSegments`, `RangeDisplayCirclePointCount`). Ideal for high-frequency pacing monitors. |

---

## 3. Diagnostic Censuses

All censuses query live game singletons through fail-closed reflection and format primitive DTOs:

- **System Health** (`diagnostics_get_system_health`): Readiness of native registry, active content packs, registered units and AI modules, player and AI credit balances.
- **Threat Prioritization** (`diagnostics_get_threat_prioritization`): Active threat-prioritization behaviors, evaluated enemy units, friendly targets, and dynamic retargeting counters.
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

---

## 4. Runtime Gameplay Command Director

The `RuntimeGameplayCommandDirector` class dispatches safe in-game actions for automated testing:

### Tactical Orders & Stances
- `diagnostics_order_unit_move`: Dispatches a movement order (`entityKeyId`, `targetX`, `targetZ`).
- `diagnostics_order_unit_attack`: Orders an attack on an enemy entity (`entityKeyId`, `targetEntityKeyId`).
- `diagnostics_order_unit_stop`: Halts the entity's active order (`entityKeyId`).
- `diagnostics_order_unit_hold_position`: Commands the unit to hold position and engage targets in range (`entityKeyId`).
- `diagnostics_order_unit_attack_move`: Orders an attack-move toward coordinates (`entityKeyId`, `targetX`, `targetZ`).

### Active Skills, Status Effects & Vitality Adjustments
- `diagnostics_order_unit_active_skill`: Fires tactical active skills (`entityKeyId`, `targetingMode` `"Self"`/`"TargetUnit"`/`"TargetPosition"`, `targetUnitKeyId`, `targetX`, `targetZ`).
- `diagnostics_apply_status_effect`: Applies effects like `SlowedDown`, `OnFire`, `Electrified`, `Stun`, `CombatBoost` (`entityKeyId`, `statusEffectName`, `durationSeconds`).
- `diagnostics_clear_status_effect`: Clears active status effects (`entityKeyId`, `statusEffectName`).
- `diagnostics_adjust_entity_vitality`: Modifies health, armor, or shields directly (`entityKeyId`, `adjustmentType` `"Heal"`/`"RepairArmor"`/`"ChargeShield"`/`"Damage"`, `amount`, `ignoreArmor`).

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

## 5. BepInEx MCP Server Integration

The BepInEx MCP server is built and synchronized via:
- `tools/build_bepinex_mcp.py`: Central registry of tool specs; outputs tool schemas to `C:\Users\Alex\.gemini\antigravity\mcp\bepinex_mcp\`.
- `tools/bepinex_mcp_server.py`: Fast MCP JSON-RPC server implementing all 128 tools, batch execution, watchers, and mod profiles.

### Bridge Discovery via Active Scene GameObject
- **The DontDestroyOnLoad Pitfall**: The BepInEx MCP bridge filters out objects whose `scene.name` is null. Components on `DontDestroyOnLoad` GameObjects are therefore invisible to `scene_find_objects_with_component`.
- **Solution**: Always instantiate `LoaderDiagnosticsComponent` on a regular GameObject in the active loaded scene (`SceneManager.sceneLoaded`). The component can hold references to persistent singletons on the plugin root.

### Query Parameter Protocol & Argument Marshaling
Calls to `call_component_method` or MCP tools pass arguments formatted as JSON arrays or URI query parameters. Types are marshaled culture-invariantly (`CultureInfo.InvariantCulture`):
```text
POST /mcp/call_method?componentName=LoaderDiagnosticsComponent&methodName=OrderUnitMoveJson&args=["unit_scout_1",15.0,22.5]
```

### Batching & Profiles
- `diagnostics_batch_read`: Executes an array of diagnostic getter calls in a single IPC cycle.
- `diagnostics_batch_execute`: Runs multiple command director operations sequentially and atomically.
- `save_mod_profile` / `activate_mod_profile`: Persists and applies diagnostic configurations.

---

## 6. Extending Clean-Room Diagnostics (The Three-Part Pattern)

When adding any new telemetry or command endpoint:

1. **Pure Formatter / DTO** (`AgentDiagnosticsFormatter.cs`):
   - Declare the pure struct/class DTO with primitive fields.
   - Implement manual JSON serialization emitting a `"kind"` discriminator.
2. **Bridge Method** (`LoaderDiagnosticsComponent.cs`):
   - Add a public MonoBehaviour method returning a JSON string.
   - Perform null-safe delegation to the DTO collector or `RuntimeGameplayCommandDirector`.
3. **Validation Unit Test** (`RuntimeAndDiagnosticsValidationTests.cs`):
   - Add a test verifying JSON schema structure, values, and fail-closed error handling.
   - Re-run the validation test harness (`dotnet run --project tests/... -c Release`).

---

## 7. Operational Safety Rules

- **Dedicated QA Profile**: Always perform live testing on `Profile2` (the user's designated dedicated QA profile without a tutorial). Never touch user profile saves or bypass tutorial flags on active user profiles.
- **Preserve Unrelated Changes**: Never use `git add -A`. Stage only task-owned files explicitly.
- **Label Evidence Accurately**: Local build and test harness passes are local validation; in-game live MCP responses are runtime evidence.
