---
name: rogue-command-clean-room-diagnostics
description: "Clean-room in-game telemetry, diagnostic censuses, runtime gameplay command director, and BepInEx MCP bridge integration for Rogue Command (v1.0.9 / Unity 2021.3.45f2, Mono x64). [Diagnósticos runtime clean-room, censos de telemetría, director de comandos de gameplay y puente BepInEx MCP para Rogue Command]"
version: 1.4.0
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
| `diagnostics_runtime_snapshot` | Composite JSON | Combines `BattlefieldCensus`, `RunProgressionCensus`, `SystemHealth`, and `MatchSummary` into a single payload. Ideal for match setup, tick ticks, and end-of-turn audits. |
| `diagnostics_runtime_scalars` | Flat key-value JSON | Emits high-density numeric telemetry (`LiveBattlefieldEntityCount`, `LivePlayerUnitCount`, `LiveAiUnitCount`, `LiveAiBuildingCount`, `LiveHarvesterCount`, `RegisteredUnitMechanicsProfileCount`, `RangeDisplayGeometryMinimumSegments`, `RangeDisplayGeometryMaximumSegments`, `RangeDisplayCirclePointCount`). Ideal for high-frequency pacing monitors. |
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
- `diagnostics_order_spawn_hazard_zone`: Spawns a battlefield environmental hazard zone (`hazardType` `"Firestorm"`/`"AcidPool"`/`"EmpField"`/`"CryoMist"`/`"NaniteField"`, `x`, `z`, `radius`, `durationSeconds`, `tickDamage`) with trigger collider and amortized tick rate (>= 0.5s).
- `diagnostics_order_clear_hazard_zones`: Clears all active hazard zones and colliders.

### Dynamic Unit Loadouts & Anti-Hitch Pre-Spawn Pools
- `diagnostics_order_apply_unit_loadout`: Switches or applies a unit loadout/variant (`entityKeyId`, `loadoutId` `"AP"`/`"HE"`/`"Speed"`/`"Siege"`/`"Reinforced"`/`"Standard"`). Applies stat adjustments (Damage, Range, Cooldown, Speed, Armor) via the native stat modification pipeline and records active variant state.
- `diagnostics_order_warm_prespawn_pool`: Manually warms an EntityFactory pre-spawn pool (`entityId`, `count` 1 to 50), pre-allocating instances to eliminate runtime hitching during mass production.

### AI Tactical Doctrine & Deck Reinforcement
- `diagnostics_order_set_ai_doctrine`: Configures the active AI tactical doctrine (`doctrineName` `"Standard"`/`"Assault"`/`"Siege"`/`"Kiting"`/`"Fortify"`, `aggressionMultiplier` 0.1 to 5.0). Modulates AI update cadence, retarget intervals, and wave aggressiveness.
- `diagnostics_order_inject_ai_deck_unit`: Dynamically reinforces an AI base deck with production quotas (`baseIndex`, `unitId`, `countAtStart`, `maxLiving`), escalating AI capabilities during matches.

### Multi-Agent Coordination & Advisory Locks
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
1. **Hermes Plugin (`bepinex-mcp`)**: Installed at `C:\Users\Alex\AppData\Local\hermes\plugins\bepinex-mcp/` and enabled in `C:\Users\Alex\AppData\Local\hermes\config.yaml` (`plugins.enabled: [bepinex-mcp]`). All 158 tools are discovered as native MCP tools.
2. **Hermes Skill Catalog**: Registered under `C:\Users\Alex\AppData\Local\hermes\skills\rogue-command\rogue-command-clean-room-diagnostics/`.
3. **Hermes Direct CLI Execution**: When running in a shell or terminal without an active MCP connection:
   ```bash
   python tools/bepinex_mcp_server.py --call diagnostics_runtime_snapshot
   python tools/bepinex_mcp_server.py --call diagnostics_ping_agent --args '{"agentId":"hermes-tactician","message":"alive"}'
   python tools/bepinex_mcp_server.py --call diagnostics_acquire_agent_lock --args '{"agentId":"hermes-tactician","resourceKey":"unit:scout_1","durationSeconds":30.0}'
   ```

### 5.2 Antigravity & Google Gemini CLI
- Fully registered under `bepinex_mcp` with 158 generated schemas in `C:\Users\Alex\.gemini\antigravity\mcp\bepinex_mcp/`.
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

---

## 7. Operational Safety Rules

- **Dedicated QA Profile**: Always perform live testing on `Profile2` (the user's designated dedicated QA profile without a tutorial). Never touch user profile saves or bypass tutorial flags on active user profiles.
- **Preserve Unrelated Changes**: Never use `git add -A`. Stage only task-owned files explicitly.
- **Label Evidence Accurately**: Local build and test harness passes are local validation; in-game live MCP responses are runtime evidence.
- **Installed vs. Local DLL Hash**: Before attributing runtime behavior to a source change, compare `BepInEx/plugins/RogueCommandExpansionLoader.dll` SHA-256 against the local build artifact. A mismatch means the running game exercises stale code; deploy the updated DLL or abort the runtime step.
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
