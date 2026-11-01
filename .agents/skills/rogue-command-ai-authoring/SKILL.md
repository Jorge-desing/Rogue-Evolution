---
name: rogue-command-ai-authoring
description: "Implement or review Rogue Command AI profiles and compiled behavior modules with bounded parameters and lifecycle handling."
---

# Rogue Command AI Authoring

Use this skill when implementing an AI profile, adding a compiled module, or changing how game AI instances receive modules. Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `content/schema/README.md` before changing content or game-facing integration.

## Keep code and pack data separate

- New AI logic belongs in a C# implementation of `IRogueCommandAiModule` compiled with the loader. A content pack selects a stable `moduleId` and passes string-pair parameters; it cannot ship an executable DLL or new game `MonoBehaviour` class.
- Give modules a stable ID and API version, reject unknown/duplicate/out-of-range parameters, and bound time, attempts, target count, and other repeated work.
- Follow initialization, update, and destruction callbacks. If a module fails, disable its behavior and attempt cleanup for all created module states in reverse order.

## Choose profile scope deliberately

- A native-template profile must set `scopeToReferencedStages: true`. The loader creates an isolated `RCL_` alias and calculates the contiguous stage range; validation rejects missing, cross-pack, or non-contiguous references. Native templates stay unchanged.
- Custom AI prefabs must satisfy the schema's active state, reserved `RCL_` name, single `DefaultAiBehaviour`, and serialized data requirements. Do not infer that the Unity Editor can author these components from precompiled game assemblies.
- AI rosters must reference produced unit IDs, never factory IDs. `unitEntries` and `productionBuildingEntries` both use unit IDs; production-building entries grant buildable factories only through their product unit.
- A scoped native profile can only include units present in its referenced stages. Use `stage.unitIds` as the availability boundary before adding roster entries.
- Set `isAllowedForAi` to `true` for every unit included in any AI roster; buildings and command HQs should remain `false`.
- Keep `countAtStart` and `maxLiving` bounded and conservative; production-building entries normally use `maxLiving: 1`.

## AI Behavior Patterns (enriched from game-ai and unity-navmesh)

Source patterns: gamedev-skills/awesome-gamedev-agent-skills `unity-navmesh` and `game-ai` (Apache-2.0). Adapt to Rogue Command's Unity 6 / URP runtime and compiled `IRogueCommandAiModule` boundaries; keep decision, steering, and pathing decoupled.

### Layering

1. **Decide** what to do (FSM/BT/utility) -> intent (target position/action).
2. **Path** how to route (engine NavMesh or bounded A*) -> waypoints.
3. **Steer** how to move (NavMeshAgent or steering forces) -> motion.

Keep modules bounded: timer-gated repaths, capped target counts, and fail-safe cleanup.

### Finite State Machine (FSM)

- States: Idle, Patrol, Chase, Attack, Flee, Dead
- Transitions triggered by: distance to target, health threshold, cooldown timers, line-of-sight
- Implementation: enum-based state with switch in `Update()`, transition checks in `CheckTransitions()`
- One state owns one behavior; keep transitions inside the state, not scattered `if state == ...`
- Bound all timers and counters to prevent runaway loops

### Behavior Tree (BT)

Node taxonomy and status model (`SUCCESS`, `FAILURE`, `RUNNING`):

| Category | Node | Behavior |
|---|---|---|
| Composite | Sequence | Tick children left->right; return on first non-SUCCESS (logical AND) |
| Composite | Selector (Fallback) | Tick children left->right; return on first non-FAILURE (logical OR) |
| Composite | Parallel | Tick all children; succeed/fail on a policy (e.g. N must succeed) |
| Decorator | Inverter | Swap SUCCESS<->FAILURE; pass RUNNING through |
| Decorator | Repeat / RepeatUntilFail | Re-tick a child N times |
| Decorator | Cooldown / Timeout | Gate or time-limit a child |
| Leaf | Condition | Test world/blackboard -> SUCCESS/FAILURE (no side effects) |
| Leaf | Action | Do something; return RUNNING until complete |

Stateful composites must remember the running child index and resume there; naive composites restart from the first child every tick and break multi-frame actions.

Blackboard pattern: shared key-value store on the agent for cross-node communication (`player`, `home`, `path`, `target`, timers). Conditions read it, actions write it; no node holds a hard reference to another. This lets the same `MoveTo` action serve chase, patrol, and flee subtrees.

Hot-reload: serialize tree structure to JSON and reload at runtime when authoring tools support it.

Use BT when many behaviors, priorities, interruption, reuse, or data-driven tuning are needed; use FSM for 2-5 clear states. Many games use an FSM for top-level mode (Idle/Combat/Dead) and a BT inside Combat.

Leaf examples:

```csharp
// Condition leaf: pure test, no side effects.
public sealed class CanSeePlayer : IBtNode
{
    public BtStatus Tick(AiContext ctx, float dt)
        => ctx.Agent.CanSee(ctx.Blackboard.Player) ? BtStatus.Success : BtStatus.Failure;
}

// Action leaf: multi-frame, returns RUNNING until it arrives.
public sealed class MoveTo : IBtNode
{
    private readonly string _key;
    public MoveTo(string key) => _key = key;
    public BtStatus Tick(AiContext ctx, float dt)
    {
        var dest = ctx.Blackboard.Get<Vector3?>(_key);
        if (dest is null) return BtStatus.Failure;
        ctx.Agent.MoveToward(dest.Value, dt);
        return ctx.Agent.Position.DistanceTo(dest.Value) < 4.0f
            ? BtStatus.Success : BtStatus.Running;
    }
}
```

Guard tree:

```
Selector
├── Sequence            # attack branch (highest priority)
│   ├── CanSeePlayer
│   ├── MoveTo(player)
│   └── Attack
└── Patrol              # fallback when nothing else applies
```

Utility AI: score each candidate action with a utility function and pick the highest; use for nuanced trade-offs (heal vs attack vs flee by health/ammo/distance). Harder to debug than a tree.

### Steering Behaviors

Seek, Flee, Arrive, Pursue, Evade, Wander, Obstacle Avoidance. Combine with weighted blending for smooth movement. Rogue Command uses `EntityController` movement; steer toward target position. Always scale motion by `dt`; arrive ramps speed down inside `slowRadius` to stop cleanly. Follow the next waypoint, not the final goal, so agents round corners.

### A* Pathfinding (Unity NavMesh + custom)

- NavMeshAgent for ground units; custom pathfinding for flying units.
- Obstacle avoidance via NavMeshObstacle components.
- Dynamic obstacles: destructible buildings update navmesh at runtime.
- Rogue Command stages use procedurally generated maps; ensure navmesh bakes after stage generation.
- Recompute on a timer or when the goal moves; cache the path; only the waypoint index advances. Never pathfind every frame.

A* correctness (engine-neutral):

- `f(n) = g(n) + h(n)`; priority queue ordered by smallest f; goal checked when popping, not pushing.
- Relaxation: accept an edge only if `newCost < costSoFar[nxt]`.
- Tie-breaker: monotonic counter alongside node so the heap never compares nodes.
- Heuristic must be admissible (never overestimate true remaining cost) for optimal paths; match movement: Manhattan for 4-dir, octile for 8-dir, Euclidean for any-angle.
- `h = 0` degenerates to Dijkstra. Weighted A* (overestimating h) is faster but non-optimal; use deliberately.

Grid adapter sketch:

```csharp
public sealed class GridGraph
{
    private readonly HashSet<(int,int)> _walls;
    private readonly int _w, _h;
    public bool InBounds((int,int) p) => p.Item1 >= 0 && p.Item1 < _w && p.Item2 >= 0 && p.Item2 < _h;
    public bool Passable((int,int) p) => !_walls.Contains(p);
    public IEnumerable<(int,int)> Neighbors((int,int) p)
    {
        var (x,y) = p;
        var c = new[] { (x+1,y),(x-1,y),(x,y+1),(x,y-1) }; // add diagonals for octile
        foreach (var n in c) if (InBounds(n) && Passable(n)) yield return n;
    }
    public float Cost((int,int) a, (int,int) b) => 1.0f; // terrain weight for varied cost
}
```

Performance: shrink graph first (merge open areas, waypoint graphs), cap pathfinding work per frame (budget N searches, queue rest), flow field (one Dijkstra from goal) for many agents to one goal.

Prefer engine navmesh for 3D arbitrary geometry, dynamic obstacle avoidance, agent radius/height; use A* directly for discrete grid/graph or full cost control.

### Unity NavMesh API Patterns

Version note: Unity 6 baking is component-based via AI Navigation package (`com.unity.ai.navigation`); old Navigation window is gone. Runtime `NavMeshAgent`/`NavMesh` API stays in `UnityEngine.AI`.

Core workflow:

1. Install AI Navigation package.
2. Bake: add `NavMeshSurface` to static level geometry, set agent type/area, click Bake. Re-bake when geometry/modifiers/settings change.
3. Add `NavMeshAgent` to each moving NPC; radius/height/speed must match baked agent type; spawn on baked mesh.
4. Drive with `SetDestination(targetPos)`; agent steers and avoids other agents. Detect arrival via `remainingDistance`/`pathPending`.
5. Dynamic blockers via `NavMeshObstacle` (carving).
6. Verify with AI Navigation overlay and `NavMeshPath.status`.

Chase/seek:

```csharp
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public sealed class Chaser : MonoBehaviour
{
    [SerializeField] private Transform target;
    private NavMeshAgent _agent;
    private void Awake() => _agent = GetComponent<NavMeshAgent>();
    private void Update()
    {
        if (target) _agent.SetDestination(target.position); // re-path toward target
    }
    private bool HasArrived() =>
        !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance;
}
```

Reachability check:

```csharp
using UnityEngine.AI;
public bool CanReach(NavMeshAgent agent, Vector3 destination)
{
    var path = new NavMeshPath();
    agent.CalculatePath(destination, path);
    return path.status == NavMeshPathStatus.PathComplete; // vs Partial / Invalid
}
```

Runtime bake (procedural/streamed levels):

```csharp
using Unity.AI.Navigation;
[SerializeField] private NavMeshSurface surface;
public void RebuildNav() => surface.BuildNavMesh(); // after spawning level geometry
```

Snap agent to mesh if not on it: `NavMesh.SamplePosition`.

Dynamic obstacle (carving): add `NavMeshObstacle` with `carving = true` to door/crate; while present it cuts a hole so agents route around; remove/disable to reopen path without re-bake.

Bridge separate mesh islands with `NavMeshLink` (`Unity.AI.Navigation`): assign Start/End Transforms, Width (0 = point-to-point), toggle Bidirectional. Replaces deprecated built-in `OffMeshLink`.

Pitfalls:

- Agent not on baked mesh / no surface baked -> warps to origin; bake and spawn on mesh.
- Runtime-spawned obstacles need `NavMeshObstacle` carving or `surface.BuildNavMesh()` re-bake.
- `SetDestination` every frame is wasteful; re-path on timer (e.g. 0.2s).
- Agent radius/height mismatch with baked agent type -> stuck/float.
- Agents jitter -> tune `avoidancePriority`/quality, or use obstacle for static blockers.
- `CalculatePath` returns `PathPartial` when destination is on a different baked island; bridge with `NavMeshLink` or extend geometry.
- `OffMeshLink` deprecated in AI Navigation 2.0; use `NavMeshLink`.

### Obstacle Avoidance (dynamic navmesh updates for destructible buildings)

- Static blockers: `NavMeshObstacle` with carving for doors/crates.
- Destructible buildings in Rogue Command: when a building is destroyed, either remove/disable its `NavMeshObstacle` (reopen path) or re-bake affected `NavMeshSurface` via `BuildNavMesh()` if geometry changes.
- For dynamic runtime geometry changes, prefer carving obstacles for discrete objects; full re-bake for structural changes.
- Ensure navmesh bakes after procedural stage generation before spawning AI agents.
- Add stuck/timeout checks: if an agent fails to progress, force repath or state change.

### Rogue Command AI Module Pattern

- Modules implement `IRogueCommandAiModule` with `Initialize`, `Update`, `Destroy` callbacks.
- String-pair parameters bound at load time; reject unknown/duplicate/out-of-range.
- Fail-safe: disable module on exception, cleanup in reverse order.
- Bound time, attempts, target count, and repeated work.
- Static evidence only: in-game runtime proof required for acceptance.

### Registered AI Modules

The loader includes 20 compiled AI modules registered in `AiModuleRegistry.CreateDefault()` (available for content packs, with a per-profile limit of up to 8 modules):

1. **`sample:trace-default`**:
   - Diagnostic trace module monitoring native AI lifecycle events and logging execution milestones.
2. **`sample:scheduled-base-build`**:
   - Uses `DefaultAiBehaviour.CreateBuilding_Static` to construct a building already present in the active native deck.
   - Parameters: `entityId` (namespaced string), `intervalSeconds` (finite 1–300s), `cooldownSeconds` (finite 0–300s), `maxAttempts` (integer 1–100), `maxSuccessfulBuilds` (integer 1–100).
3. **`sample:reactive-base-defense`**:
   - Intercepts player units attacking AI structures and inserts attack-move counterattacks before selected defenders' current commands.
   - Parameters: `baseIndex` (0–31), `cooldownSeconds` (1–60s), `defenseRadiusCells` (1–64; default 12), `maxResponders` (1–4; default 2), `protectedTargetType` (`Buildings` [default], `Harvesters`, `BuildingsAndHarvesters`).
4. **`sample:harvester-raider`**:
   - Actively hunts player crystal gatherers to disrupt enemy economy.
   - Parameters: `baseIndex` (0–31), `scanIntervalSeconds` (2–120s; default 10s), `cooldownSeconds` (5–300s; default 25s), `raidSquadSize` (1–8; default 3), `minGameTimeSeconds` (0–600s; default 15s).
   - Scans `ExistingControllers.Instance.Harvesters`, gathers combat units from the AI base, and dispatches attack-move orders directly against player harvesters.
5. **`sample:siege-assault`**:
   - Coordinates multi-unit assault waves against player fortifications and infrastructure.
   - Parameters: `baseIndex` (0–31), `cooldownSeconds` (10–300s; default 30s), `minSquadSize` (1–12; default 4), `targetPriority` (`ClosestBuilding` [default], `Refinery`, `Hq`), `minGameTimeSeconds` (0–600s; default 30s).
   - Scans known player structures (`PlayerBuildingsKnownToAi`, `PlayerRefineries`, or `EngineeringHq`), gathers available combat troops, and coordinates offensive siege strikes.
6. **`sample:threat-prioritization`**:
   - Scores hostile player units and retargets AI combat units with hysteresis and a per-tick command budget.
   - Parameters: `baseIndex` (-1–31; default -1), `checkIntervalSeconds` (0.5–30s; default 3s), `priorityMode` (`HighestThreat` [default], `LowestHealth`, `HighestDps`; normalized to canonical PascalCase), `engagementRadius` (5–120; default 35), `retargetThreshold` (1.0–3.0; default 1.25), `maxRetargetsPerTick` (1–32; default 8).
   - Filters candidates with `unit.IsEnemy(candidate)` and re-checks hostility on the current target before hysteresis; consumes the retarget budget before native dispatch via `EntityCommand.AttackMoveOnTarget` with `ExecuteBeforeCurrentCommand`.
   - Each tick also censuses AI units (`AiUnits()`) and allied player units (`PlayerUnits()`), exposing `AutoControlledAlliedUnitCount` / `_lastProcessedAiUnitCount` for diagnostics only. It does **not** mutate control/faction identity: allied units stay `IsControlledByPlayer=true` so `IsEnemy` and sibling modules keep treating the player force as hostile. An earlier reflection-based `IsControlledByPlayer`/`IsControlledByAi` reassignment was reverted in `5b88d65` because those flags define hostility and are consumed elsewhere; track module ownership in state or command tags, never in those flags.
   - Targeting is specialized per allied unit role through the pure, offline-testable `ThreatPrioritizationRoleProfile` (`ThreatPrioritizationRoleProfile.cs`, added as a Compile link in the test project): sniper/scout roles extend engagement range and tolerate distance, melee/tank/frontal roles close range and penalize distance, mass-destruction units keep a heavy single-target focus, and pure support/healer units (`Supporter`/`Heal`) are never retargeted onto enemy combat targets (`EngagesCombat == false`). The profile is resolved from `unit.HasRole(...)` and never mutates control or hostility flags; default/generic units keep the historical composite behavior (`Default` profile).
7. **`sample:skirmish-kiting`**:
   - Tactical harassment module that activates for fast, long-range units (`MoveSpeed >= MinKiteSpeed`, `WeaponRange >= 5.0`).
   - Parameters: `baseIndex` (-1–31; default -1), `checkIntervalSeconds` (0.2–10s; default 1.0s), `minKiteSpeed` (5.0–30.0; default 10.0), `retreatDistanceRatio` (0.2–0.9; default 0.6), `kiteCooldownSeconds` (0.5–30s; default 2.0s).
   - Monitors encroaching hostiles and commands retreat moves opposite the threat vector when enemies enter the retreat distance ratio of weapon range.
8. **`sample:support-repair`**:
   - Autonomous logistics and field repair module directing dedicated support units to damaged friendlies.
   - Parameters: `baseIndex` (-1–31; default -1), `scanIntervalSeconds` (0.5–30s; default 2.0s), `repairRadiusCells` (5–60; default 25), `targetPriority` (`HarvestersFirst` [default], `FactoriesFirst`, `LowestHealthRatio`), `maxRepairersPerTarget` (1–4; default 2).
   - Scans damaged allies, prioritizes Harvesters and production factories, and queues repair moves.
9. **`sample:counter-battery`**:
   - Dispatches fast, mobile interceptors against long-range player artillery and siege weapons.
   - Parameters: `baseIndex` (-1–31; default -1), `scanIntervalSeconds` (1.0–30s; default 3.0s), `artilleryRangeThreshold` (10.0–50.0; default 18.0), `interceptSquadSize` (1–6; default 2).
   - Pinpoints hostile units exceeding the range threshold and assigns high-speed units to neutralize them.
10. **`sample:wave-escalation`**:
    - Periodically increments the AI attack wave counter and coordinates timed offensive pushes.
    - Parameters: `baseIndex` (-1–31; default -1), `escalationIntervalSeconds` (30–600s; default 120s), `maxWaveNumber` (1–50; default 10), `orderAssaultOnWave` (true/false; default true).
    - Interfaces with `AiBehaviourManager.CurrentAttackWaveNumber` to escalate match pressure.
11. **`sample:squad-formation`**:
    - Binds follower combat units to an anchor captain unit with designated geometric offsets.
    - Parameters: `baseIndex` (-1–31; default -1), `formationType` (`Wedge` [default], `Line`, `Column`), `spacingCells` (1–10; default 2), `maxFollowers` (1–8; default 4).
    - Automatically unbinds followers when captain is destroyed.
12. **`sample:tactical-skill-activation`**:
    - Triggers entity active tactical skills when enemy units are detected in range or upon health drops.
    - Parameters: `baseIndex` (-1–31; default -1), `checkIntervalSeconds` (0.5–10s; default 1.5s), `enemyTriggerRadiusCells` (2–40; default 15).
    - Evaluates `UnitRuntimeMechanicsProfile.AiActivationTrigger` (`EnemiesInRange` vs `LowHealth`); triggers self-buffs or target-directed skills accordingly.
13. **`sample:focus-fire`**:
    - Coordinates multiple allied combatants to concentrate fire on single high-priority targets.
    - Parameters: `baseIndex` (-1–31; default -1), `evalIntervalSeconds` (0.5–15s; default 2.0s), `selectionMode` (`LowestHealth` [default], `HighestThreat`, `HighestDps`).
14. **`sample:construction-expansion`**:
    - Manages dynamic AI base expansion and defensive perimeter construction.
    - Parameters: `baseIndex` (0–31), `expansionIntervalSeconds` (10–300s; default 45s), `maxExpansions` (1–10; default 3).
15. **`sample:tactical-regroup`**:
    - Pulls combatants back to a safe rally point when squad casualties exceed a configurable threshold.
    - Parameters: `baseIndex` (-1–31; default -1), `healthRatioThreshold` (0.1–0.7; default 0.35), `regroupRadiusCells` (5–50; default 15).
16. **`sample:factory-reinforcement`**:
    - Automatically routes newly produced factory units directly to bolster active front-line combat squads.
    - Parameters: `baseIndex` (0–31), `rallyDistanceThreshold` (5.0–100.0; default 25.0).
17. **`sample:flanking-strike`**:
    - Coordinates secondary assault groups to navigate around primary battle lines and strike flanks or rear infrastructure.
    - Parameters: `baseIndex` (0–31), `flankAngleDegrees` (30–90; default 60), `squadSize` (2–6; default 3).
18. **`sample:retreat-and-regroup`**:
    - Pulls severely damaged units back to base repair zones.
    - Parameters: `baseIndex` (0–31), `retreatHealthRatio` (0.1–0.5; default 0.25), `cooldownSeconds` (5–120s; default 20s).
19. **`sample:harvester-escort`**:
    - Assigns dedicated combat escorts to shadow and protect friendly harvesters during resource collection runs.
    - Parameters: `baseIndex` (0–31), `escortsPerHarvester` (1–3; default 1), `leashDistance` (3.0–20.0; default 8.0).
20. **`sample:patrol-and-intercept`**:
    - Manages tactical perimeter patrol and interception. `AttackMove` remains the default mode.
    - Opt-in native Patrol route motion requires `CurrentPlayerCommandName == DoNothing` AND verified empty private `_commandChain` queue (`LinkedList<EntityCommand>`), failing closed otherwise to preserve queued orders.

### Runtime mechanics profile consumption

- Some module behavior depends on per-unit runtime mechanics (e.g. `aiActivationTrigger` on the first active skill). The trigger lives in content (`unit.activeSkills[0].aiActivationTrigger`) and must be copied by `ContentPackRuntimeLoader` into `UnitRuntimeMechanicsProfile.AiActivationTrigger` when the profile is created; the module then reads it via `RuntimeUnitMechanicsDirector.TryGetProfile(unit.EntityId, out ...)`.
- Always default to `"EnemiesInRange"` when the profile is missing or the trigger is blank, so existing packs and units without the field behave exactly as before.
- Only count a non-default trigger as mechanics in `UnitRuntimeMechanicsProfile.HasAnyMechanics` (i.e. `!string.Equals(trigger, "EnemiesInRange", OrdinalIgnoreCase)`); otherwise every unit with an active skill would be flagged as having mechanics and could change load/offer behavior.
- In tests, initialize the profile directly (`new UnitRuntimeMechanicsProfile { EntityId = ..., AiActivationTrigger = "LowHealth" }`) and `RegisterProfile` it; reflection-based assignment is fragile and fails to compile against partial test views.
- When a trigger bypasses the usual enemy-in-range gate (e.g. `LowHealth` for self skills), keep the health check (`CurrentHealth / MaxHealth <= 0.5`) before any command dispatch, and only skip the enemy search for `TargetOrigin.Self` skills; non-self skills still require a nearby enemy within `enemyTriggerRadiusCells`.

### Implementation checklist for a new compiled module

1. Create `Modules/<Name>Parameters.cs` with a parser that rejects unknown, duplicate, and out-of-range values, plus documented defaults.
2. Write parser tests in `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs` before implementing the parser; expect the RED failure, then make them GREEN.
3. Create `Modules/<Name>AiModule.cs` implementing `IRogueCommandAiModule`; keep the runtime behaviour private and timer-gated.
4. Add `<Compile Include=\"...\">` entries for both new source files in `RogueCommandExpansionLoader.ValidationTests.csproj`; otherwise the offline harness will not see the module.
5. Register the module in `RogueCommandExpansionPlugin.Awake()` before content packs load.
6. Document the module and its parameter bounds in `content/schema/README.md`.
7. Re-run the full validation harness and a Release build before committing.

Pitfalls:

- The validation harness compiles source files through explicit `Compile` links; a new module that builds in the plugin but is absent from the test project's links will leave parser tests unable to compile.
- Command dispatch must be guarded by null checks and wrapped in try/catch; a single native call failure must not abort the whole module tick.
- Tag every issued command and skip units already tagged with the same tag; otherwise a timer-gated scan can repeatedly stack orders on the same units.
- Use `ExistingControllers` as the source of truth for AI units, harvesters, and player units; do not cache entity lists across ticks.
- Keep scan intervals and target counts bounded and fail closed on missing native fields or incompatible types.
- Do not mutate `IsControlledByPlayer`/`IsControlledByAi` for **any** reason — including to "apply" a module to allied units. These flags define hostility and are consumed by `IsEnemy` and sibling modules; reassigning them silently converts the player force into an AI-controlled enemy (or vice versa) and breaks every hostility check downstream. An earlier attempt to reassign them via reflection on REA-verified setters had to be reverted for exactly this reason. Track module ownership in state or command tags; census allied units for diagnostics only.
- Filter AI target candidates with `unit.IsEnemy(candidate)` and re-check hostility on the current target before applying hysteresis; never assume player-side entities are hostile after control-flag changes.
- Require both `IsCombatUnit` and `CanAttackAndIsAttackEnabled` for AI combat-unit eligibility; avoid inverted `!A && !B` conditions.
- Consume retarget/command budgets before native dispatch so exceptions cannot bypass caps.
- AI profiles allow at most 8 modules. When adding a standard module such as `sample:threat-prioritization` to every profile, append it as the last module and verify each profile stays within the limit; profiles already at 8 must have a module removed or consolidated first.
- When adding a target field to the test `EntityCommand` stub, update every factory that carries a target (`Attack`, `AttackMoveOnTarget`, `ActiveSkillOnTarget`, etc.) consistently.

### Integration with Existing AI Authoring

- `AiBase.entitiesInAiDeck` includes mobile units and buildings.
- `unitEntries` on native profiles establish cross-pack load order.
- `countAtStart` controls starting units; `maxLiving` bounds native deck field.
- Stage-scoped native AI uses isolated `RCL_` alias and contiguous stage range.

### References

- gamedev-skills game-ai: FSM/BT/steering/A* (Apache-2.0)
- gamedev-skills unity-navmesh: Unity 6 NavMesh API (Apache-2.0)
- Unity NavMesh documentation: https://docs.unity3d.com/Manual/nav-NavigationSystem.html
- AI Navigation package manual: https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/index.html
- Rogue Command AI module contract: `src/RogueCommandExpansionLoader/Ai/IRogueCommandAiModule.cs`

## Be precise about behavior

- Before replacing or looping a persistent native order, verify both the current command and queued command chain: a current `DoNothing` can coexist with a queued `Move`, and a looping native Patrol may not resume it. If the queue is private, pin the complete game identity and exact declaring type/member signature before reflection, fail closed on mismatch, and add a queued-order regression test; stub tests do not validate runtime reflection.
- When a module makes protected target classes configurable, test that the default still excludes newly supported classes and that each class in a combined mode triggers the expected dispatch. Parser acceptance alone does not prove selection; keep offline harness results separate from in-game evidence.
- `AiBase.entitiesInAiDeck` can contain mobile units as well as buildings. Static inspection of the pinned 1.0.9 assembly found that `AiBehaviour.BuildBaseStartSetup` sends non-building entries through `EntityFactory.InstantiateEntity`, tags them as AI, and assigns their AI base index. Add mod units through `unitEntries` on a `scopeToReferencedStages: true` native profile so only its isolated clone changes; the loader validates the unit reference and target base index and resolves cross-pack unit dependencies. `countAtStart` controls starting units; `maxLiving` preserves the native deck field and is bounded against it. This is static and offline-validation evidence, not in-game proof.
- `unitEntries` establishes cross-pack load order itself. The full runtime deck check must accept that declaration after appending those entries; do not require the same ID again in `unitDependencies`. External IDs authored directly into a custom AI prefab's serialized deck still require `unitDependencies`.

The scheduled-build sample constructs buildings already present in the native AI deck; it does not add a deck. The reactive-defense sample dispatches bounded counterattacks; it does not prove a new strategy works. Treat every module as unverified in-game until a Steam-launched match shows the intended decision and lifecycle. Use static inspection and offline parser/registry checks first, then a reversible test profile for authorized runtime validation.
