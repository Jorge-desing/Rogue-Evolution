---
name: rogue-command-harmony-policy
description: "BepInEx 5 + Harmony 2 patching policy for Rogue Command (v1.0.9 / Unity 2021.3.45f2, Mono x64). When creating or auditing Unity game plugins, Harmony patches, assembly identity checks, or IL2CPP/back-end decisions. [Política de BepInEx 5 + Harmony 2 para Rogue Command (v1.0.9 / Unity 2021.3.45f2, Mono x64). Uso: plugins Unity, patches Harmony, verificación de identidad de ensamblados, decisiones backend IL2CPP]"
version: 1.2.0
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, Unity, Mono, BepInEx, Harmony, IL2CPP, patching, SHA256, MVID, assembly verification]
    related_skills: [rogue-command-clean-room-diagnostics, rogue-command-reverse-engineering, rogue-command-game-research, rogue-command-steam-runtime-validation, rogue-command-pack-validation, rogue-command-unit-authoring]
---

# Rogue Command Harmony Policy

Authoritative policy for creating, reviewing, and auditing BepInEx 5 / Harmony 2 patches in Rogue Command.

## Compatibility Identity (Verify Before Every Session)

| Component | Pinned Identity / Signature | Verification Rule |
|---|---|---|
| **Game Version** | `1.0.9` | `version.txt` (`9= Version 1.0.9`) |
| **Engine** | Unity `2021.3.45f2` (Mono x64, 64-bit) | `Managed/Assembly-CSharp.dll` presence |
| **Loader** | BepInEx `5.4.23.5` (Mono Standalone x64, netstandard2.1) | `BepInEx/core/BepInEx.dll` |
| **Harmony** | Harmony 2 (`BepInEx.Harmony` / `HarmonyX`) | Bundled with BepInEx 5 |
| **Assembly-CSharp SHA-256** | `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` | `SHA256.HashData(File.ReadAllBytes(...))` |
| **Assembly-CSharp MVID** | `db0e9462-128d-4296-9900-de18b4bd0c00` | Assembly header GUID (`0x00000001`) |
| **Unity CoreModule SHA-256** | `6305c82c17ffe111a016e8d8ca3e8b2d9203980b4f58cb6e90171e5629994290` | MVID `37d484f4-070f-4518-bfa9-9fa57b7a2871` |
| **Backend Verification** | Mono backend: `Managed/Assembly-CSharp.dll`. If future IL2CPP: `GameAssembly.dll` + `global-metadata.dat`. |

---

## BepInEx 5 Plugin Lifecycle & Safety Rules

### Lifecycle Phases
1. **Plugin Bootstrap:** `[BepInPlugin(GUID, Name, Version)]` on class extending `BaseUnityPlugin`. BepInEx invokes `Awake()`.
2. **Compatibility Gate:** Call `GameCompatibility.TryValidate(gameRoot)` before touching reflected types. Failure = abort all patching.
3. **Assembly Verification:** Check SHA-256 and MVID against pinned identity.
4. **Centralized Patch Registration:** Execute `RuntimePatchInstaller.Install(...)` plan; store `Harmony` instance.
5. **Runtime Phase:** Dispatch main-thread work from `Update()`/`LateUpdate()`.
6. **Teardown:** Dispose async cancellation sources and listeners on `OnDestroy()`. Never call `Application.Quit()` during cleanup.

### Hard Constraints
- **Singleton Guard:** Use static `Instance` with duplicate destruction (`Destroy(this.gameObject)`) to prevent double-load.
- **Persistence & Hiding:** Mark loader GameObject with `DontDestroyOnLoad(gameObject)` and `HideFlags.HideAndDontSave`.
- **Non-blocking `Awake()`:** Never block the main thread. Bridge server loops must run on `Task.Run` with `CancellationTokenSource`.
- **Patch Exception Safety:** Patch bodies must NEVER throw into native code; wrap in `try/catch`, log, and return safe defaults.
- **Test Assembly Isolation:** `RuntimeHooks.cs` is compiled by tests without game DLL references. Never use direct game types (`using GameNamespace;`). Resolve all game types via `RuntimeReflection.FindUniqueType(...)`.

---

## Centralized Patch Registration Pattern

Patches use a programmatic registration plan rather than static attributes:

1. **Logic Declaration (`RuntimeHooks.cs`):** Declare `public static void MethodName(...)` with hook implementation.
2. **Harmony Shim (`RuntimeHarmonyPatches.cs`):** Declare shim delegating to `RuntimeHooks.MethodName(...)`.
3. **Target Resolution (`RuntimePatchInstaller.cs`):** Resolve target via `RuntimeReflection.FindUniqueMethod(type, name, predicate)`.
4. **Hook Resolution:** Resolve shim method via `Hook(nameof(RuntimeHarmonyPatches.MethodName))`.
5. **Plan Registration:** `plan.Add(new PatchPlan(targetMethod, prefix, postfix, finalizer))`.
6. **Execution & Metric Query:** All installed hooks and counters are inspectable live via MCP `diagnostics_get_runtime_hooks`.

---

## Core Engine Harmony Patches (53 Installed Hooks)

The loader registers 53 verified hooks across core game simulation systems:

| Category / Pillar | Target Method | Hook Type | Purpose & Telemetry Counter |
|---|---|---|---|
| **Pillar 1: Production** | `BuildingPlacement.CreateBuildingPlacementGhost` | Postfix | Track placement holograms; populate `GetLastPlacementGhostEntityId()` (`placementGhostHookCount`) |
| **Pillar 1: Production** | `EntityFactory.InstantiateEntity` | Postfix | Intercept unit creation, attach directors (`unitCreationHookCount`) |
| **Pillar 1: Production** | `EntityController.ResetAndInitForReuse` | Prefix/Postfix | Handle object pool recycling; clear registered effects/XP (`unitReuseHookCount`) |
| **Pillar 1: Production** | `EntityController.Produce` | Postfix | Track factory production completion by factory and entityId (`factoryProduceHookCount`) |
| **Pillar 2: Registry** | `ExistingControllers.Init` | Postfix | Reset battlefield registry on scene start; notify AI behaviour (`entityRegistryResetCount`) |
| **Pillar 2: Registry** | `ExistingControllers.OnEntityCreated` | Postfix | Register unit into player/AI rosters (`entityRegisteredCount`, `activeRegisteredEntityCount`) |
| **Pillar 2: Registry** | `ExistingControllers.OnEntityWillBeDestroyed` | Postfix | Intercept death; unlink aura followers, release AI arbiter leases (`entityUnregisteredCount`) |
| **Pillar 2: Registry** | `RuntimeUnitMechanicsDirector` Invariants | Internal | Deterministic cleanup of dynamic auras, followers, and status effects upon entity death |
| **Pillar 3: Command Loop** | `EntityController.ExecuteCommand` | Postfix | Intercept orders; store `command.tag` in `GetLastUnitCommandTag()`; notify AI (`commandExecutedCount`) |
| **Pillar 3: Command Loop** | `EntityController.ExecuteNextCommand` | Postfix | Step through unit command chain (`commandNextStepCount`) |
| **Pillar 3: Command Loop** | `EntityController.ResetCurrentCommand` | Postfix | Order cancellation; clear unit command tag; invalidate active AI arbiter lease (`commandResetCount`) |
| **Pillar 3: Command Loop** | `EntityController.DeleteCommandChain` | Postfix | Clear command queue; release AI arbiter leases (`commandChainDeletedCount`) |
| **Balancing & Stages** | `StageParameters.Initialize` / `StageMap.*` | Pre/Post | Reward pool injection, stage generation parameters, blueprint filters |
| **Combat & Mechanics** | `EntityController.ChangeArmor` / `Die` / `Heal` | Pre/Post | Status effects, damage reduction, armor repairs, veterancy XP grants |

---

## Patching Patterns & Rules

### 1. Prefix
- **Use:** Early guards, argument validation, conditional suppression (return `false` to skip original).
- **Rule:** Do NOT allocate collections, run LINQ, or use string interpolation on hot execution paths.
- **Signature:** `public static bool Prefix(MethodInfo __originalMethod, object __instance, object[] __args)`

### 2. Postfix
- **Use:** Read-only observation, telemetry counters, modifying `ref ReturnType __result`.
- **Signature:** `public static void Postfix(MethodInfo __originalMethod, object __instance, object[] __args, ref ReturnType __result)`

### 3. Transpiler
- **Use:** Modifying IL instruction streams in-place.
- **Rule:** ALWAYS use `CodeMatcher` (never raw `ILGenerator`/`OpCodes`). Target the shortest unique label sequence. Never transpile `async`, `ref`, or `out` generic methods.

### 4. Optional Target Resolution (Cross-Version Robustness)
- Use `TryFindUniqueMethod` defensive wrapper returning `null` on miss instead of throwing.
- Register optional patches only when both target and wrapper `MethodInfo` are non-null:
  ```csharp
  MethodInfo target = TryFindUniqueMethod(type, "OptionalMethod", p => p.GetParameters().Length == 0);
  if (target != null && wrapper != null) { harmony.Patch(target, postfix: new HarmonyMethod(wrapper)); }
  ```
- **Return Type Invariant:** `harmony.Patch(...)` returns `MethodInfo` (the patched original), NOT `bool`. Checking `if (harmony.Patch(...))` causes compilation error `CS0029`.

### 5. C# Editing Safety Rules
- Match entire declaration lines including opening brace and first statement to prevent fuzzy-match truncation.
- Recompile with `dotnet build` after every edit batch before proceeding.
- If a patch leaves orphaned braces, restore via git or overwrite whole methods with `write_file`.

---

## Evidence Boundaries

| Evidence Tier | Source | Proves | Does NOT Prove |
|---|---|---|---|
| **Static Inspection** | Repo files, decompiled IL, assets | Method signatures, tokens, structure | Runtime behavior or registration |
| **Local Build / Validation** | `dotnet build`, test harness, Python | Compile correctness, hook list | In-game execution or match flow |
| **In-Game Steam Evidence** | Live Steam process (AppID `1461910`) | Active hook execution, live AI/commands | Universal cross-version behavior |

---

## References

- [Clean-Room Diagnostics Skill](../rogue-command-clean-room-diagnostics/SKILL.md) — Telemetry censuses, runtime director, MCP bridge tools.
- [Reverse Engineering Skill](../rogue-command-reverse-engineering/SKILL.md) — REA inspection, metadata tokens, member signatures.
- [Steam Runtime Validation Skill](../rogue-command-steam-runtime-validation/SKILL.md) — Reversible live validation runbook.
- `docs/research/BASELINE.md` & `AGENT-HANDOFF.md` — Verified commands and environment contracts.
