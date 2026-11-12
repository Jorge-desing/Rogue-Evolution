# Clean-Room Utility Recreation and Live Agent-Readable Diagnostics

Use this reference when recreating an RCM utility as clean-room C# in the loader, exposing loader state and telemetry to external agents through the BepInEx MCP bridge without visual captures, or adding in-game diagnostic and tactical gameplay commands.

---

## 1. Clean-Room Extraction Rules

1. **Independent Authoring**: Do not copy or decompile proprietary source code, internal layouts, or closed palettes. Use public standards (such as FNV-1a) and independently authored data.
2. **Zero External Serialization Dependencies**: Never introduce `Newtonsoft.Json`, `System.Text.Json`, or third-party serialization libraries into the game loader runtime. Write minimal, robust manual serializers emitting well-formed `{"key":value}` strings.
3. **Decoupled Primitive DTOs**: Formatter DTOs and command results must use only standard CLR primitive types (`string`, `int`, `long`, `float`, `double`, `bool`, `IReadOnlyList<T>`). Never expose `UnityEngine.Color`, `UnityEngine.Vector3`, `UnityEngine.GameObject`, or `UnityEngine.Component` across the DTO boundary so the test harness can compile against lightweight test stubs without real Unity dependencies.
4. **Omission & Loss Counters First**: In any bounded census or diagnostic report (e.g., entity rosters, production queues, tile samples, tech catalogs), place omission counters (`OmittedEntityCount`, `OmittedHarvesterCount`, `OmittedRelicCount`) at the very top of the DTO before unbounded collections. This guarantees tail truncation in logs or telemetry buffers never conceals data loss.
5. **Fail-Closed Reflection**: When interacting with unexposed game internals (e.g. `AiBehaviourManager`, `Harvest`, `UpdateFogOfWar`, `AudioMixerHelper`), wrap reflection lookups in null checks and try-catch blocks. If a method or property is missing or changes across game versions, return a descriptive failed result (`Success = false`) instead of throwing unhandled exceptions.

---

## 2. Clean-Room FNV-1a Color Policy (Pattern)

When a visual or diagnostic utility requires deterministic, stable color indexing from string identifiers:

1. Implement FNV-1a 32-bit (offset basis `0x811C9DC5`, prime `0x01000193`) over the UTF-16 char sequence.
2. Modulo the hash by the palette length (power of two, e.g. 16) to obtain an index.
3. Use an independently authored 16-entry palette; never replicate external reference palettes.
4. Verify against standard test vectors:
   - `"" → 0x811C9DC5`
   - `"a" → 0xE40C292C`
   - `"foobar" → 0xBF9CF968`
5. Guarantee cross-session determinism: identical inputs produce identical colors.

---

## 3. High-Performance Aggregator Tools

To minimize IPC overhead and latency during live match testing, agents can query high-level aggregators instead of issuing dozens of individual round-trip requests:

- `diagnostics_runtime_snapshot`: Aggregates the four primary match censuses (`BattlefieldCensus`, `RunProgressionCensus`, `SystemHealth`, and `MatchSummary`) into a single composite JSON payload.
- `diagnostics_runtime_scalars`: Returns a compact flat dictionary of scalar integer and float telemetry counters (active entity counts, player units, enemy units, live harvesters, credit balances, wave counter, and timescale) ideal for high-frequency polling loops and pacing monitors.

---

## 4. Diagnostic Censuses Architecture

The loader implements pure runtime censuses querying live game singletons and components:

| Census Class | DTO Name | Game Singleton / Source | Scope |
|---|---|---|---|
| `RuntimeSystemHealthCensus` | `SystemHealthCensus` | Multi-system check | Native registry readiness, active content packs, registered units & AI modules, credit balances. |
| `RuntimeThreatPrioritizationCensus` | `ThreatPrioritizationCensus` | AI threat modules | Active AI threat behaviours, processed targets, retarget counts. |
| `RuntimeProductionCensus` | `ProductionCensus` | `ExistingControllers` | Total factories, producing factories, queued units, production progress. |
| `RuntimeBaseIntegrityCensus` | `BaseIntegrityCensus` | `ExistingControllers` | HQ health ratio, living AI buildings, average structure health. |
| `RuntimeBattlefieldCensus` | `BattlefieldCensus` | `ExistingControllers` | Living combatants, structure counts, tactical roles, positions. |
| `RuntimePlayerRosterCensus` | `PlayerRosterCensus` | `ExistingControllers.PlayerUnits` | Player units, health ratios, coordinates, active orders. |
| `RuntimeAiRosterCensus` | `AiRosterCensus` | `ExistingControllers.AiUnits` | AI units, threat targets, tactical roles, health. |
| `RuntimeMatchSummaryCensus` | `MatchSummaryCensus` | Composite match state | Full battle summary combining units, economy, and combat status. |
| `RuntimeRunProgressionCensus` | `RunProgressionCensus` | Progression singletons | Stage number, level index, ascension/Heat string & level, blueprint pools. |
| `RuntimeSelectionCensus` | `SelectionCensus` | `PlayerUnitController` | Selected units, focused enemy target, active skills, control groups 0-9. |
| `RuntimeFogCensus` | `FogStateCensus` | `UpdateFogOfWar` | Fog enabled state, update intervals, grid resolution. |
| `RuntimeGridCensus` | `GridMetricsCensus` | Native navigation grid | Grid bounds, cell dimensions, traversability, occupancy metrics. |
| `RuntimeAiDirectorCensus` | `AiDirectorCensus` | `AiBehaviourManager` | Wave progression, command counts (attack, defend, scout, build, produce), tick duration. |
| `RuntimeCameraCensus` | `CameraCensus` | `Camera.main`, `CameraZoom` | Viewport ground bounds (min/max X/Z), ground center, FOV, orthographic mode. |
| `RuntimeHarvesterCensus` | `HarvesterCensus` | `ExistingControllers.Harvesters` | Harvester counts, crystal loads, load fractions, mining states. |
| `RuntimeAudioCensus` | `AudioCensus` | `AudioMixer`, `AudioMixerHelper` | Master, Music, Gameplay, UI slider levels and attenuation. |
| `RuntimeTechCatalogCensus` | `TechCatalogCensus` | Balancing stores | Upgrades, research branches, and relics registered in stores. |
| `RuntimeEntityCatalogCensus` | `EntityCatalogCensus` | `EntityBalancingStore` | Units, buildings, and factories registered in the game registry. |

---

## 5. Runtime Gameplay Command Director

The `RuntimeGameplayCommandDirector` class provides fail-closed in-game actions for automated testing agents:

### Tactical Orders & Stances
- `TryOrderUnitMove(entityKeyId, targetX, targetZ)`: Dispatches a movement order.
- `TryOrderUnitAttack(entityKeyId, targetEntityKeyId)`: Dispatches an attack order.
- `TryOrderStop(entityKeyId)`: Immediately halts the entity's active command.
- `TryOrderHoldPosition(entityKeyId)`: Commands the unit to hold ground.
- `TryOrderAttackMove(entityKeyId, targetX, targetZ)`: Orders an attack-move command.

### Active Skills, Status Effects & Vitality
- `TryOrderUnitActiveSkill(entityKeyId, targetingMode, targetUnitKeyId, targetX, targetZ)`: Triggers active abilities across `"Self"`, `"TargetUnit"`, or `"TargetPosition"`.
- `TryApplyStatusEffect(entityKeyId, statusEffectName, durationSeconds)`: Dynamically applies status effects (`SlowedDown`, `OnFire`, `Electrified`, `Stun`, `CombatBoost`).
- `TryClearStatusEffect(entityKeyId, statusEffectName)`: Removes active status effects.
- `TryAdjustEntityVitality(entityKeyId, adjustmentType, amount, ignoreArmor)`: Modifies health, armor, or shield with optional armor penetration.

### Selection & Control Groups
- `TrySelectEntities(keys, additive)`: Selects entities by key ID array or CSV.
- `TryDeselectAll()`: Clears active player selection.
- `TrySelectCategory(category)`: Selects by group (`"CombatUnits"`, `"Harvesters"`, `"IdleHarvesters"`, `"SkillBuildings"`, `"Factories"`).
- `TryAssignControlGroup(groupNumber)` / `TrySelectControlGroup(groupNumber)`: Manages control groups 0 through 9.

### Harvester Operations
- `TryOrderHarvesterHarvest(harvesterKeyId)`: Claims nearest crystal node and starts mining.
- `TryOrderHarvesterReturn(harvesterKeyId)`: Returns cargo to nearest refinery.

### AI Director & Waves
- `TrySetAttackWaveNumber(waveNumber)` / `TryIncrementAttackWave()`: Overrides or steps attack waves.
- `TryToggleAiDebugVisuals(showTags, showCommands)`: Enables/disables AI debug tags in the viewport.

### Camera, Audio & Environment
- `TryMoveCamera(targetX, targetZ)`: Pans camera viewport.
- `TryFocusCameraOnEntity(entityKeyId)`: Centers camera on an entity.
- `TrySetCameraZoom(fieldOfView)`: Adjusts camera FOV.
- `TrySetAudioVolume(channel, volumeSlider)`: Sets normalized volume slider for Master, Music, Gameplay, UI.
- `TrySetFogOfWar(disableFog)` / `TryRevealFogOfWar()`: Toggles or reveals Fog of War.
- `TrySetGameTimeScale(timeScale)` / `GetGameTimeScale()`: Sets or reads game simulation speed.
- `TryGrantCredits(amount, isPlayer)`: Directly adjusts economy balance.
- `TryOrderProduction(factoryKeyId, instant, forFree)`: Triggers factory production.
- `TrySpawnEntity(entityId, side, targetX, targetZ, customName)`: Instantiates entities into match.
- `TryTriggerMatchEnd(win)`: Simulates match victory/defeat.
- `TryApplyCardChange(request)`: Injects data-driven stat changes via `EntityBalancingStore.AddInGameCardChange`.

---

## 6. Live BepInEx MCP Bridge Protocol

### Bridge Discovery Invariant (Critical)
The BepInEx MCP bridge's `find_objects_with_component` uses `Resources.FindObjectsOfTypeAll` but filters by `gameObject.scene.name != null`.
- **DontDestroyOnLoad Trap**: Objects marked with `DontDestroyOnLoad` have a `null` scene name, making them invisible to `find_objects_with_component` even if the server log reports them.
- **Rule**: Always attach `LoaderDiagnosticsComponent` to a GameObject in a normal active scene (not `DontDestroyOnLoad`). Re-instantiate it on each scene load via `SceneManager.sceneLoaded`. The component can safely maintain references to persistent singletons held on the plugin's background root.

### Query Parameter Protocol
When calling component methods via BepInEx MCP HTTP bridge:
- Format: `POST /mcp/call_method?componentName=LoaderDiagnosticsComponent&methodName=<Method>&args=<JSON_ARRAY>`
- Parameter types (`string`, `int`, `float`, `bool`) are parsed culture-invariantly (`CultureInfo.InvariantCulture`).

### Batching & Profiles
- `diagnostics_batch_read`: Accepts a list of diagnostic getter names and returns all results in a single payload.
- `diagnostics_batch_execute`: Runs a series of command invocations sequentially and atomically.
- `watch_component_member`: Establishes background polling hooks on component values with change notifications.

---

## 7. Extending the Bridge: The Three-Part Pattern

When introducing a new clean-room utility or telemetry system:

1. **Pure Formatter Serializer** (`AgentDiagnosticsFormatter.cs`):
   - Dependency-free class using primitive DTOs.
   - Implement `SerializeX(...)` returning JSON string.
   - Include a `"kind"` discriminator field.
   - Verify it compiles in the test harness without Unity references.
2. **Bridge Surface** (`LoaderDiagnosticsComponent.cs`):
   - Expose a public method `GetXJson(...)` returning `string`.
   - Perform null-safe delegation to the pure formatter or command director.
3. **Unit Validation Test** (`RuntimeAndDiagnosticsValidationTests.cs`):
   - Write a unit test exercising the serializer and verifying JSON key-value pairs via `JsonDocument.Parse`.
   - Test command success and graceful error paths.

---

## 8. Verification & Deployment Workflow

1. **Compilation**: Verify plugin Release builds cleanly (`0 warnings, 0 errors`).
2. **C# Test Harness**: Run `ValidationTests` (expect 405/406 passed, 1 symlink skip).
3. **Python Suite**: Run `tools/run_python_checks.ps1` (expect 86/86 passed, 4 manifests valid, whitespace check clean).
4. **Backup & Deployment**: Backup existing plugin DLL, copy newly built DLL to `BepInEx/plugins/`, and launch game on dedicated QA profile (`Profile2`).
5. **MCP Verification**: Call `system_ping`, `diagnostics_get_system_health`, and `diagnostics_runtime_snapshot` to confirm live bridge availability.
6. **Evidence Labeling**: Clearly label findings as static inspection, local validation, or in-game runtime evidence.
