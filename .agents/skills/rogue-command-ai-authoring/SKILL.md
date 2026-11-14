---
name: rogue-command-ai-authoring
description: "Implement or review Rogue Command AI profiles and compiled behavior modules with bounded parameters and lifecycle handling."
---

# Rogue Command AI Authoring

Use this skill when implementing an AI profile, adding a compiled module, or configuring tactical AI behavior.

---

## 1. Core Architecture & Invariants

- **Code vs Pack Boundary**:
  - New AI logic MUST be a compiled C# implementation of `IRogueCommandAiModule` linked in the loader.
  - Content packs select stable `moduleId` strings and pass key-value parameters; packs cannot ship executable DLLs or `MonoBehaviour` classes.
  - Max 8 modules per AI profile. Modules must declare API versions, validate parameter bounds, and clean up in reverse order on failure.
- **Profile Scope & AI Rosters**:
  - Native templates MUST declare `scopeToReferencedStages: true` to generate an isolated `RCL_` alias over contiguous stage ranges.
  - Custom AI prefabs require reserved `RCL_` naming, active state, and a single `DefaultAiBehaviour`.
  - `unitEntries` and `productionBuildingEntries` use `unitId` to reference **produced unit IDs**, never factory IDs. The `entityId` parameter of `sample:scheduled-base-build` is the factory ID.
  - Only units with `isAllowedForAi: true` and present in `stage.unitIds` may be added to AI rosters.
  - Cross-pack load order is established by `unitEntries` and `productionBuildingEntries`; do not duplicate entries in `unitDependencies`.
- **Faction & Hostility Invariant (CRITICAL)**:
  - **NEVER mutate `IsControlledByPlayer` or `IsControlledByAi`**. These flags define hostility for native `IsEnemy()`.
  - Mutating them corrupts friend/foe detection game-wide. Module ownership MUST be tracked via internal state or command tags only.

---

## 2. AI Layering & Decision Frameworks

Support repair considers missing armor even when health is above the configured health threshold. Dispatch excludes the repairer itself and native hostile targets.

1. **Decide** (FSM / Behavior Tree / Utility Scorer) $\rightarrow$ Intent & Target
2. **Path** (NavMesh / Bounded A*) $\rightarrow$ Waypoints (timer-gated repaths, never every frame)
3. **Steer** (`EntityController` movement forces) $\rightarrow$ Motion ($\Delta\text{pos} \cdot dt$)

### Behavior Tree (BT) Taxonomy

| Category | Node | Behavior |
|---|---|---|
| **Composite** | `Sequence` | Ticks children left $\rightarrow$ right; returns on first non-`SUCCESS` (logical AND). |
| **Composite** | `Selector` | Ticks children left $\rightarrow$ right; returns on first non-`FAILURE` (logical OR). |
| **Composite** | `Parallel` | Ticks all children; evaluates policy (e.g., $N$ must succeed). |
| **Decorator** | `Inverter` | Inverts `SUCCESS` $\leftrightarrow$ `FAILURE`; passes `RUNNING` through. |
| **Decorator** | `Repeat` / `Cooldown` | Loops execution $N$ times or enforces a cooldown lockout window. |
| **Leaf** | `Condition` | Pure read of world/blackboard $\rightarrow$ returns `SUCCESS` or `FAILURE` without side effects. |
| **Leaf** | `Action` | Executes behavior; returns `RUNNING` until complete, then `SUCCESS`/`FAILURE`. |

*Stateful composite rule: Always remember the running child index across frames; never restart from child 0 each tick.*

### NavMesh & Dynamic Obstacle Invariants
- Unity 6 component-based baking via `com.unity.ai.navigation` (`NavMeshSurface.BuildNavMesh()`).
- Static & dynamic obstacles use `NavMeshObstacle` with `carving = true` (cuts holes dynamically without full re-bake).
- Destructible structures: on destruction, disable `NavMeshObstacle` to reopen pathways.
- Islands & elevation gaps: bridge using `NavMeshLink` (`OffMeshLink` is deprecated).
- Re-path on a timer (e.g. 0.2s–0.5s) or on destination drift; never call `SetDestination` every frame.

---

## 3. Catalog of Compiled AI Modules (23 Modules)

All 23 modules are registered in `AiModuleRegistry.CreateDefault()`:

| Module ID | Role & Core Behavior | Parameters & Valid Ranges | Documented Defaults |
|---|---|---|---|
| `sample:trace-default` | Diagnostic trace logging native AI lifecycle milestones | None | None |
| `sample:scheduled-base-build` | Constructs structures from native AI deck via `CreateBuilding_Static` | `entityId` (optional factory ID), `baseIndex` (0–31), `intervalSeconds` (1–600s), `maxSuccessfulBuilds` (1–32), `maxAttempts` (`maxSuccessfulBuilds`–128) | `baseIndex: 0`, `interval: 8s`, `maxSuccess: 1`, `maxAttempts: 3` |
| `sample:reactive-base-defense` | Dispatches counterattacks when structures or harvesters take damage | `baseIndex` (0–31), `cooldownSeconds` (1–60s), `defenseRadiusCells` (1–64), `maxResponders` (1–4), `protectedTargetType` (`Buildings`, `Harvesters`, `BuildingsAndHarvesters`) | `cooldown: 10s`, `defenseRadius: 12`, `maxResponders: 2`, `type: Buildings` |
| `sample:harvester-raider` | Hunts enemy harvesters to disrupt economy | `baseIndex` (0–31), `scanIntervalSeconds` (2–120s), `cooldownSeconds` (5–300s), `raidSquadSize` (1–8), `minGameTimeSeconds` (0–600s) | `scan: 10s`, `cooldown: 25s`, `squadSize: 3`, `minTime: 15s` |
| `sample:siege-assault` | Coordinates siege assault waves against structures | `baseIndex` (0–31), `cooldownSeconds` (10–300s), `minSquadSize` (1–12), `targetPriority` (`ClosestBuilding`, `Refinery`, `Hq`), `minGameTimeSeconds` (0–600s) | `cooldown: 30s`, `minSquad: 4`, `priority: ClosestBuilding`, `minTime: 30s` |
| `sample:threat-prioritization` | Scores enemies & retargets combatants with hysteresis; uses `ThreatPrioritizationRoleProfile` | `baseIndex` (-1–31), `checkIntervalSeconds` (0.5–30s), `priorityMode` (`HighestThreat`, `LowestHealth`, `HighestDps`), `engagementRadius` (5–120), `retargetThreshold` (1.0–3.0), `maxRetargetsPerTick` (1–32) | `base: -1`, `check: 3s`, `mode: HighestThreat`, `radius: 35`, `threshold: 1.25`, `maxRetargets: 8` |
| `sample:skirmish-kiting` | Tactical kiting for fast ranged units (`MoveSpeed >= minKiteSpeed`, `WeaponRange >= 5.0`) | `baseIndex` (-1–31), `checkIntervalSeconds` (0.2–10s), `minKiteSpeed` (5.0–30.0), `retreatDistanceRatio` (0.2–0.9), `kiteCooldownSeconds` (0.5–30s) | `base: -1`, `check: 1.0s`, `minSpeed: 10.0`, `retreatRatio: 0.6`, `cooldown: 2.0s` |
| `sample:support-repair` | Dispatches dedicated repairers to damaged friendlies | `baseIndex` (-1–31), `scanIntervalSeconds` (0.5–30s), `repairRadiusCells` (5–60), `targetPriority` (`HarvestersFirst`, `FactoriesFirst`, `LowestHealthRatio`), `maxRepairersPerTarget` (1–4) | `base: -1`, `scan: 2.0s`, `radius: 25`, `priority: HarvestersFirst`, `maxRepairers: 2` |
| `sample:counter-battery` | Fast mobile interceptors hunt enemy artillery (`minAttackRange >= threshold`) | `baseIndex` (-1–31), `scanIntervalSeconds` (1.0–30s), `artilleryRangeThreshold` (10.0–50.0), `interceptSquadSize` (1–6) | `base: -1`, `scan: 3.0s`, `threshold: 18.0`, `squadSize: 2` |
| `sample:wave-escalation` | Escalates match pressure via `CurrentAttackWaveNumber` | `baseIndex` (-1–31), `escalationIntervalSeconds` (30–600s), `maxWaveNumber` (1–50), `orderAssaultOnWave` (bool) | `base: -1`, `interval: 120s`, `maxWave: 10`, `orderAssault: true` |
| `sample:squad-formation` | Binds follower units to an anchor captain | `baseIndex` (-1–31), `formationType` (`Wedge`, `Line`, `Column`), `spacingCells` (1–10), `maxFollowers` (1–8) | `base: -1`, `formation: Wedge`, `spacing: 2`, `maxFollowers: 4` |
| `sample:tactical-skill-activation` | Triggers active skills on enemy detection or health drops (`AiActivationTrigger`) | `baseIndex` (-1–31), `checkIntervalSeconds` (0.5–10s), `enemyTriggerRadiusCells` (2–40) | `base: -1`, `check: 1.5s`, `radius: 15` |
| `sample:focus-fire` | Concentrates squad firepower onto high-priority targets | `baseIndex` (-1–31), `evalIntervalSeconds` (0.5–15s), `selectionMode` (`LowestHealth`, `HighestThreat`, `HighestDps`) | `base: -1`, `eval: 2.0s`, `mode: LowestHealth` |
| `sample:construction-expansion` | Expands base perimeter with defensive buildings | `baseIndex` (0–31), `expansionIntervalSeconds` (10–300s), `maxExpansions` (1–10) | `interval: 45s`, `maxExpansions: 3` |
| `sample:tactical-regroup` | Pulls combatants back to rally point upon heavy losses | `baseIndex` (-1–31), `healthRatioThreshold` (0.1–0.7), `regroupRadiusCells` (5–50) | `base: -1`, `threshold: 0.35`, `radius: 15` |
| `sample:factory-reinforcement` | Routes newly produced factory units directly to the frontline | `baseIndex` (0–31), `rallyDistanceThreshold` (5.0–100.0) | `threshold: 25.0` |
| `sample:flanking-strike` | Flanking assault groups maneuver around main battle lines | `baseIndex` (0–31), `flankAngleDegrees` (30–90), `squadSize` (2–6) | `angle: 60`, `squadSize: 3` |
| `sample:retreat-and-regroup` | Pulls critically damaged units back to base repair zones | `baseIndex` (0–31), `retreatHealthRatio` (0.1–0.5), `cooldownSeconds` (5–120s) | `ratio: 0.25`, `cooldown: 20s` |
| `sample:harvester-escort` | Assigns dedicated combat escorts to shadow harvesters | `baseIndex` (0–31), `escortsPerHarvester` (1–3), `leashDistance` (3.0–20.0) | `escorts: 1`, `leashDistance: 8.0` |
| `sample:patrol-and-intercept` | Perimeter patrol; requires `CurrentPlayerCommandName == DoNothing` and empty `_commandChain` queue | None (fails closed on non-empty queue) | None |
| `sample:adaptive-commander` | Promotes commander, pulses command auras, modulates tactical stances & doctrine | None (binds to profile defaults) | Dynamic |
| `sample:tactical-director` | **HUB-D 2.0 Macro Director**: 8-ray safe retreat, base defense, flanking, focus fire with status/affix scoring | `baseIndex` (-1–31), `evalIntervalSeconds` (0.1–10.0), `engagementRadius` (5–120), `defenseRadius` (5–60), `retreatHealthRatio` (0.05–0.80), `maxProposalsPerUnit` (1–4), `enableSurvivalKiting` (bool), `enableBaseDefense` (bool), `enableFocusTargeting` (bool), `enableFlanking` (bool), `minFlankSpeed` (3.0–25.0) | `base: -1`, `eval: 0.5s`, `engagementRadius: 40`, `defenseRadius: 20`, `retreatRatio: 0.30`, `maxProposals: 2`, flags: `true`, `minFlankSpeed: 7.5` |
| `sample:squad-operations-director` | **HUB-D 2.0 Specialist Director**: support/repair logistics (`ScoreSupportOrRepair`), fast counter-battery (`MoveSpeed >= 9.0`), hazard evasion, specialist skills | `baseIndex` (-1–31), `evalIntervalSeconds` (0.20–10.0), `supportRadius` (5.0–80.0), `counterBatteryMinRangeThreshold` (8.0–50.0), `enableSupportLogistics` (bool), `enableCounterBattery` (bool), `enableTacticalSkills` (bool) | `base: -1`, `eval: 0.75s`, `supportRadius: 30.0`, `counterBatteryMinRange: 14.0`, flags: `true` |

`GetAiModulesDiagnosticsJson` reports registry availability and parameterized module IDs; it does not prove a profile is attached to the current stage or that a module is executing. For a live claim, first resolve the actual stage/profile association, then inspect its director, roster, and module-specific counters in `Game`. Do not advance a saved run along a route whose active stage does not include the target profile.

---

## 4. HUB-D 2.0 Coordinated Tactical Architecture

The Hierarchical Utility-Blackboard Director coordinates macro strategy and micro tactics across `sample:tactical-director` and `sample:squad-operations-director`:

### 1. Dynamic Tactical Regroup Geometry (`AiSafeVectorPolicy.ComputeTacticalRetreatDestination`)
- Evaluates an 8-ray candidate fan ($0^\circ, 45^\circ, 90^\circ, 135^\circ, 180^\circ, 225^\circ, 270^\circ, 315^\circ$) opposing the threat vector.
- Avoids hazard zones and artillery dead-zones; biases retreat toward friendly defenses, support units, and group centroid.
- Modulates retreat distance based on `AlliedDispersionRadius` to maintain squad cohesion without clumping.

### 2. Spatial Blackboard Telemetry (`AiBattlefieldBlackboard`)
- `AlliedDispersionRadius`: Root-mean-square distance of combatants from group centroid.
- `FriendlyCentroid` & `HostileCentroid`: Center-of-mass positions driving dual-wing flanking vectors.
- `HazardZones`: Active trigger hazard zones (Firestorm, AcidPool, EmpField, CryoMist, NaniteField) that retreat/kite paths strictly avoid.

### 3. Bitmask Telemetry (`AiUnitSnapshot`, zero-heap projection)
- **Status Flags** (`AiCombatStatusFlags`): `Stunned = 1 << 0`, `Vulnerable = 1 << 1`, `Slowed = 1 << 2`, `Burning = 1 << 3`, `Overclocked = 1 << 4`.
- **Affix Flags** (`AiAffixFlags`): `Overcharged = 1 << 0`, `Vampiric = 1 << 1`, `Volatile = 1 << 2`, `Bulwark = 1 << 3`, `Juggernaut = 1 << 4`, `Swift = 1 << 5`.

### 4. Dynamic Combat Target Utility Scoring (`TacticalUtilityScorer.ScoreCombatTarget`)
- **Status Vulnerabilities**: `Stunned` (+40% score), `Vulnerable` (+50% score), `Slowed` (+20% score), `Burning` (+15% score).
- **Elite Affix Synergies**: Prioritizes high-burst offensive traits (`Overcharged`, `Vampiric`, `Volatile`); dampens priority against high-HP defensive tanks (`Bulwark`, `Juggernaut`) while healthy.
- **Anti-Overkill Damping (`ScoreCombatTargetWithOverkill`)**: Dampens target utility when incoming allied DPS exceeds effective target HP + shields.

### 5. Closed-Loop Command Loop & Arbiter Synchronization
- **Priority Tiers**: `SurvivalKite` (50) > `TacticalSkill` (40) > `BaseAndHarvesterDefense` (30) > `CombatTargeting` (20) > `FlankingManeuver` (15) > `SiegeAndFormation` (10).
- **Reactive Lease Invalidation**: When native `ResetCurrentCommand` or `DeleteCommandChain` executes, Harmony hooks call `CompositeAiRuntimeBehaviour.NotifyUnitLeaseInvalidated(unitId)`, releasing the lease in `AiCommandArbiterState` instantly.
- **Command Tag Mismatch Release**: If an external command with a different tag executes on a unit, `NotifyUnitCommandExecuted(unitId, tag)` releases the lease (`ReleaseLeaseIfTagDiffers`).
- **Perception Tag Reflection**: `RuntimeAiBattlefieldCollector` queries `GetLastUnitCommandTag(entityKey)` to populate `AiUnitSnapshot.CurrentCommandTag`.
- **Battlefield Reset**: On match start/restart, `ExistingControllers.Init` invokes `NotifyBattlefieldRegistryReset()`, clearing all leases across active composites.

---

## 5. Role-Profile Specialization & Runtime Mechanics

- **`ThreatPrioritizationRoleProfile`** (pure `readonly struct` in `ThreatPrioritizationRoleProfile.cs`):
  - Resolvers: `Resolve(UnitRole role)` (bitwise, pure) and `ResolveFor(EntityController unit)` (uses `unit.HasRole()`).
  - Snipers extend engagement range; melee units close distance; supporters (`Supporter`/`Heal`) have `EngagesCombat == false` and are never retargeted onto enemies.
- **`UnitRuntimeMechanicsProfile.AiActivationTrigger`**:
  - Configured via `unit.activeSkills[0].aiActivationTrigger` (`"EnemiesInRange"` [default] vs `"LowHealth"`).
  - `"LowHealth"` triggers when `CurrentHealth / MaxHealth <= 0.5`. For `TargetOrigin.Self` skills, enemy proximity check is bypassed.

---

## 6. Implementation Checklist & Critical Pitfalls

1. **Parameters Class**: Create `Modules/<Name>Parameters.cs` validating types, ranges, duplicates, and defaults.
2. **Offline Tests First**: Write parser and boundary tests in `ValidationTests/Program.cs` before module logic.
3. **Module Class**: Implement `IRogueCommandAiModule` in `Modules/<Name>AiModule.cs`; keep runtime behavior private and timer-gated.
4. **Compile Links**: Add `<Compile Include="...">` in `RogueCommandExpansionLoader.ValidationTests.csproj`.
5. **Registry Binding**: Register in `AiModuleRegistry.CreateDefault()`.
6. **Schema Documentation**: Document parameters in `content/schema/README.md`.
7. **Verification**: Run `dotnet build -c Release` and C# / Python test suites before committing.

### AI Pack Audit Commands

- Run `python -m tools.validate_ai_modules content/packs --all --json` to validate all pack profiles against compiled module IDs and their parameter contracts.
- Run `python -m tools.analyze_ai_profile_coverage <pack-dir> --json` to check roster product IDs. Its CLI resolves cross-pack products against the repository's declared unit catalog; its pure function accepts `known_unit_ids` for isolated fixtures.
- Module IDs are read from `AiModuleRegistry.CreateDefault()` by `tools/ai_module_contracts.py`, so do not maintain another Python module-ID list. If adding a module, still synchronize its accepted parameter names and bounds in the validator with its C# parser.
- These commands are static content checks. They do not prove the AI profile was selected or its scheduler ran in a live match.

### Critical Pitfalls
- **Combat Eligibility**: Require `IsControlledByAi`, `IsUnit`, `IsCombatUnit`, `CanAttackAndIsAttackEnabled`, and `CanMoveAndMovementIsEnabled`.
- **Enemy Filtering**: Always wrap candidate evaluation in null-safe `unit.IsEnemy(candidate)`; treat reflected delegates defensively.
- **Command Tags**: Tag every issued command (`cmd.tag`); skip units already executing that tag to prevent stacking orders.
- **ExistingControllers**: Always read `ExistingControllers.Instance` each tick; never cache entity collections across frames.
- **Budgets Before Dispatch**: Consume retargeting and command budgets before native dispatch so failures cannot cause infinite retries.
- **Counter-Battery**: Artillery detection requires `minAttackRange >= ThreatTriggerDistance`, not just a positive range.
- **Squad Formation**: Only track mod-created captain links; never invoke `ReleaseAllCaptains` over native links.

### Runtime profile-selection evidence

- `GetAiModulesDiagnosticsJson` and registered-profile rows prove registry contents only. They do not prove the stage selected a profile or that its module ran.
- Confirm the live stage/profile association with a stable stage, template, or profile ID before advancing a saved run. `BattlePreviewWidget.EnemyInfoText` is a localized display label; do not infer a `nativeTemplateId` or profile ID from the label without a verified mapping.
- In `IntermissionScreen`, an unavailable AI director and zero AI roster/factory counts mean no battle runtime is being observed. If the target association remains unknown, stop before selecting another route node and report `UNKNOWN`.
- Scheduled-build acceptance requires a `Game` scene with the target profile active, then observed build/queue evidence and an AI-produced unit whose `originEntityId` matches its registered factory. The 2026-11-11 Profile2 probe did not reach this evidence gate; see `artifacts/runtime-smoke/ai-scheduled-build-20261111T012405Z/live-ai-runtime-pass-summary.json`.

### Active stage association diagnostic

- Use `diagnostics_get_active_stage_ai_association` to read the stable active stage ID before choosing a saved route node as an AI test. Custom-stage `declaredAiProfileIds` are configuration from the resolved stage definition; `runtimeBoundAiProfiles` and instance counts describe initialized loader-bound AI behaviours observed in the current scene.
- Keep those fields separate. A native stage can have no loader-declared profile IDs, and an intermission with zero runtime bindings does not establish whether battle AI executes. The endpoint reports association and binding state, not tactical decisions, scheduler ticks, or production.
- `identitySource` records whether identity came from the live progression sequence or persisted `rclStageOrderV1`. If `stageIdentityStatus` is unresolved, stop and report `UNKNOWN`; never substitute localized enemy preview text.
- The endpoint also returns bounded `stageSequenceRows` from the captured StageMap order. Each row carries stable custom/native identity and custom-stage declared IDs; sequence status, source, active index, failure reason, and omission count precede the rows. A partial/unavailable sequence or a target absent from a complete sequence is not a reason to click through unrelated route nodes.
- Inspect resolved rows within `totalStageSequenceCount` before navigating. A later row can establish a declared stage/profile configuration while the active stage is native; it still does not prove that profile is active or that its module executed. If extra rows outside the configured count are unresolved, retain the sequence's `partial` status and do not describe the whole sequence as resolved.
- In the authorized 2026-11-11 Profile3 new-run pass, configured row 13 resolved to `sample-reactive-ai:expansion:sample-reactive-ai:counterstrike-lane` and declared `sample-reactive-ai:base-defense`, but the active Game stage was native row 0 (`StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit`) with zero loader-bound profile instances. The 14 configured rows were resolved; the endpoint's extra rows 14–31 remained unresolved, so overall sequence status was `partial`. Treat row 13 as configuration evidence only; this pass did not execute the reactive profile. Evidence: `docs/research/AI-REACTIVE-PROFILE3-RUNTIME-2026-11-11.md`.
- That native Game stage exposed director/roster/production census (14 AI units, 11 AI buildings, five AI factories, one active production, one queued unit); these are native AI baseline observations, not evidence that the custom profile, custom unit, or custom AI modules ran. The loader audit showed four registered profile modules and `activeInstanceCount=0` for the target profile.
- Runtime command and visual-census selectors use the unique live `EntityController.UniqueEntityId` when supplied as a numeric key. On deployed loader 1.20.10, two same-product Walkers resolved to keys 278/279; a duplicate native `EntityId` order failed closed and a Move by key 278 succeeded. This improves diagnostic attribution only; the run did not exercise a custom AI profile or prove AI execution. Evidence: `artifacts/runtime-smoke/unique-key-retarget-release-20261111T173856Z/unique-key-retarget-summary.json`.
