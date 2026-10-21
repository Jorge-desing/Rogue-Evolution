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

## AI Behavior Patterns (enriched from game-ai and unity-navmesh)

### Finite State Machine (FSM)
- States: Idle, Patrol, Chase, Attack, Flee, Dead
- Transitions triggered by: distance to target, health threshold, cooldown timers, line-of-sight
- Implementation: enum-based state with switch in `Update()`, transition checks in `CheckTransitions()`
- Bound all timers and counters to prevent runaway loops

### Behavior Tree (BT)
- Nodes: Selector (OR), Sequence (AND), Action, Condition, Decorator
- Blackboard pattern: shared key-value store for cross-node communication
- Hot-reload: serialize tree structure to JSON, reload at runtime
- Use for complex AI that FSM cannot express cleanly (e.g., multi-priority defense)

### Steering Behaviors
- Seek, Flee, Arrive, Pursue, Evade, Wander, Obstacle Avoidance
- Combine with weighted blending for smooth movement
- Rogue Command uses `EntityController` movement; steer toward target position

### A* Pathfinding (Unity NavMesh)
- NavMeshAgent for ground units; custom pathfinding for flying units
- Obstacle avoidance via NavMeshObstacle components
- Dynamic obstacles: destructible buildings update navmesh at runtime
- Rogue Command stages use procedurally generated maps; ensure navmesh bakes after stage generation

### Utility AI
- Score-based action selection: each action has a utility function
- Consider: distance, health, ammo, threat level, objective priority
- Use for AI that needs to choose between multiple valid actions

### Rogue Command AI Module Pattern
- Modules implement `IRogueCommandAiModule` with `Initialize`, `Update`, `Destroy` callbacks
- String-pair parameters bound at load time
- Fail-safe: disable module on exception, cleanup in reverse order
- Static evidence only: in-game runtime proof required for acceptance

### Integration with Existing AI Authoring
- `AiBase.entitiesInAiDeck` includes mobile units and buildings
- `unitEntries` on native profiles establish cross-pack load order
- `countAtStart` controls starting units; `maxLiving` bounds native deck field
- Stage-scoped native AI uses isolated `RCL_` alias and contiguous stage range

### References
- gamedev-skills game-ai: FSM/BT/steering/pathfinding (Apache-2.0)
- Unity NavMesh documentation: https://docs.unity3d.com/Manual/nav-NavigationSystem.html
- Rogue Command AI module contract: `src/RogueCommandExpansionLoader/Ai/IRogueCommandAiModule.cs`

## Be precise about behavior

- Before replacing or looping a persistent native order, verify both the current command and queued command chain: a current `DoNothing` can coexist with a queued `Move`, and a looping native Patrol may not resume it. If the queue is private, pin the complete game identity and exact declaring type/member signature before reflection, fail closed on mismatch, and add a queued-order regression test; stub tests do not validate runtime reflection.
- When a module makes protected target classes configurable, test that the default still excludes newly supported classes and that each class in a combined mode triggers the expected dispatch. Parser acceptance alone does not prove selection; keep offline harness results separate from in-game evidence.
- `AiBase.entitiesInAiDeck` can contain mobile units as well as buildings. Static inspection of the pinned 1.0.9 assembly found that `AiBehaviour.BuildBaseStartSetup` sends non-building entries through `EntityFactory.InstantiateEntity`, tags them as AI, and assigns their AI base index. Add mod units through `unitEntries` on a `scopeToReferencedStages: true` native profile so only its isolated clone changes; the loader validates the unit reference and target base index and resolves cross-pack unit dependencies. `countAtStart` controls starting units; `maxLiving` preserves the native deck field and is bounded against it. This is static and offline-validation evidence, not in-game proof.
- `unitEntries` establishes cross-pack load order itself. The full runtime deck check must accept that declaration after appending those entries; do not require the same ID again in `unitDependencies`. External IDs authored directly into a custom AI prefab's serialized deck still require `unitDependencies`.

The scheduled-build sample constructs buildings already present in the native AI deck; it does not add a deck. The reactive-defense sample dispatches bounded counterattacks; it does not prove a new strategy works. Treat every module as unverified in-game until a Steam-launched match shows the intended decision and lifecycle. Use static inspection and offline parser/registry checks first, then a reversible test profile for authorized runtime validation.
