---
name: rogue-command-harmony-policy
description: "BepInEx 5 + Harmony 2 patching policy for Rogue Command (v1.0.9 / Unity 2021.3.45f2, Mono x64). When creating or auditing Unity game plugins, Harmony patches, assembly identity checks, or IL2CPP/back-end decisions. [Política de BepInEx 5 + Harmony 2 para Rogue Command (v1.0.9 / Unity 2021.3.45f2, Mono x64). Uso: plugins Unity, patches Harmony, verificación de identidad de ensamblados, decisiones backend IL2CPP]"
version: 1.4.1
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

## Core Engine Harmony Patches (65 Installed Hooks & 16 CIL Transpilers)

The loader registers 65 verified hooks across core game simulation systems, including 16 in-place CIL Transpilers:

| Category / Pillar | Target Method | Hook Type | Purpose & Telemetry Counter |
|---|---|---|---|
| **Pillar 1: Production** | `EntityFactory.CreateBuildingPlacementGhost` | Pre/Post + Transpiler | Placement hologram instantiations; stacks `Dup` on return (`placementGhostHookCount`, `placementGhostTranspilerCount`) |
| **Pillar 1: Production** | `EntityFactory.InstantiateEntity` | Postfix + Transpiler | Intercept unit creation, attach directors (`unitCreationHookCount`, `unitCreationTranspilerCount`) |
| **Pillar 1: Production** | `EntityFactory.InitEntityController` | Pre/Post + Transpiler | Prefab/GameObject unit instantiation; stacks `Dup` on return (`initEntityControllerTranspilerCount`) |
| **Pillar 1: Production** | `EntityController.Init` | Transpiler | Full native unit stat/role/grid/visual initialization callback (`entityInitTranspilerCount`) |
| **Pillar 1: Production** | `EntityController.OnHasBeenInstantiated` | Transpiler | Universal unit instantiation callback; attaches directors (`entityOnInstantiatedTranspilerCount`) |
| **Pillar 1: Production** | `EntityController.ResetAndInitForReuse` | Prefix + Transpiler | Object pool recycling; clears stale XP/effects (`unitReuseHookCount`, `unitReuseTranspilerCount`) |
| **Pillar 1: Production** | `EntityController.OnHasBeenInstantiatedForReuse` | Postfix + Transpiler | Universal pooled unit revival callback (`entityReuseOnInstantiatedTranspilerCount`) |
| **Pillar 1: Production** | `EntityController.Produce` | Pre/Post + Transpiler | Factory production dispatch; passes args to director (`factoryProduceHookCount`, `factoryProduceTranspilerCount`) |
| **Pillar 2: Registry** | `ExistingControllers.Init` | Postfix + Transpiler | Reset battlefield registry on scene start (`entityRegistryResetCount`, `entityRegistryResetTranspilerCount`) |
| **Pillar 2: Registry** | `ExistingControllers.OnEntityCreated` | Postfix + Transpiler | Register unit into rosters (`entityRegisteredCount`, `activeRegisteredEntityCount`, `entityRegisteredTranspilerCount`) |
| **Pillar 2: Registry** | `ExistingControllers.OnEntityWillBeDestroyed` | Postfix + Transpiler | Intercept death; unlink aura followers, release leases (`entityUnregisteredCount`, `entityUnregisteredTranspilerCount`) |
| **Pillar 2: Registry** | `EntityController.Destroy` | Postfix + Transpiler | Core unit destruction/death dispatch; unlinks auras, updates tracking (`entityDestroyTranspilerCount`) |
| **Pillar 2: Registry** | `RuntimeUnitMechanicsDirector` Invariants | Internal | Deterministic cleanup of dynamic auras, followers, and status effects upon entity death |
| **Pillar 3: Command Loop** | `EntityController.ExecuteCommand` | Postfix + Transpiler | Intercept orders; store `command.tag` in `GetLastUnitCommandTag()`; notify AI (`commandExecutedCount`, `commandExecutedTranspilerCount`) |
| **Pillar 3: Command Loop** | `EntityController.ExecuteNextCommand` | Postfix + Transpiler | Step through unit command chain (`commandNextStepCount`, `commandNextStepTranspilerCount`) |
| **Pillar 3: Command Loop** | `EntityController.ResetCurrentCommand` | Postfix + Transpiler | Order cancellation; clear unit command tag; invalidate active AI arbiter lease (`commandResetCount`, `commandResetTranspilerCount`) |
| **Pillar 3: Command Loop** | `EntityController.DeleteCommandChain` | Postfix + Transpiler | Clear command queue; release AI arbiter leases (`commandChainDeletedCount`, `commandChainDeletedTranspilerCount`) |
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
- **Use:** Modifying CIL instruction streams in-place at exact bytecode injection points.
- **Implemented Core Transpilers (`RuntimeTranspilerPatches.cs`, 16 Total):**
  - `EntityFactory.InstantiateEntity`: Clones dynamic entity/arg loads and injects `OnEntityInstantiatedTranspilerHook` right after `callvirt OnHasBeenInstantiated`.
  - `EntityFactory.InitEntityController`: Value-returning method (`EntityController`). Injects `OpCodes.Dup` + `call OnInitEntityControllerTranspilerHook` before each `OpCodes.Ret`, preserving the returned instance on the stack.
  - `EntityFactory.CreateBuildingPlacementGhost`: Value-returning method (`GameObject`). Injects `OpCodes.Dup` + `ldarg.0` + `ldarg.1` + `call OnCreateBuildingPlacementGhostTranspilerHook` before each `OpCodes.Ret`.
  - `EntityController.Init`: Injects `ldarg.0` + `ldarg.1` + `call OnEntityInitTranspilerHook` before all `OpCodes.Ret` exit points with label transfer.
  - `EntityController.Destroy`: Injects `ldarg.0..2` + `call OnEntityDestroyTranspilerHook` at method entry (index 0) with label transfer. Require the exact game `typeof(EntityController)` instance `void Destroy(bool, EntityController)` method and the exact static, non-generic `RuntimeHarmonyPatches` callback `void(object, bool, object)` before emitting IL. Throw on target/hook drift or an empty instruction stream so Harmony rolls back instead of silently leaving an unpatched destroy hook.
  - `EntityController.ExecuteCommand`: Injects `ldarg.0` + `call OnEntityExecuteCommandTranspilerHook` at method entry (index 0) with label transfer.
  - `EntityController.ExecuteNextCommand`: Injects `ldarg.0` + `call OnEntityExecuteNextCommandTranspilerHook` before all `OpCodes.Ret` exits.
  - `EntityController.ResetCurrentCommand`: Injects `ldarg.0` + `call OnEntityResetCurrentCommandTranspilerHook` before each `OpCodes.Ret` exit.
  - `EntityController.DeleteCommandChain`: Injects `ldarg.0` + `call OnEntityDeleteCommandChainTranspilerHook` before each `OpCodes.Ret` exit.
  - `EntityController.OnHasBeenInstantiated`: Injects `ldarg.0`, `ldarg.1`, and `call OnEntityHasBeenInstantiatedTranspilerHook` before all `OpCodes.Ret` exits.
  - `EntityController.OnHasBeenInstantiatedForReuse`: Injects `ldarg.0`, `ldarg.1`, and `call OnEntityHasBeenInstantiatedForReuseTranspilerHook` before all `OpCodes.Ret` exits.
  - `EntityController.Produce`: Injects `ldarg.0..3` and `call OnEntityProduceTranspilerHook` before all `OpCodes.Ret` exits.
  - `EntityController.ResetAndInitForReuse`: Injects `OnEntityResetForReuseTranspilerHook` before all `OpCodes.Ret` exit points.
  - `ExistingControllers.Init`: Injects `ldarg.0` and `call OnEntityRegistryResetTranspilerHook` before all `OpCodes.Ret` exit points.
  - `ExistingControllers.OnEntityHasBeenCreated`: Injects `OnEntityRegisteredTranspilerHook` before each `OpCodes.Ret`, transferring return labels to the start of the injected sequence.
  - `ExistingControllers.OnEntityWillBeDestroyed`: Injects `OnEntityWillBeDestroyedTranspilerHook` at method entry (index 0).
- **`netstandard2.1` / Mono Type Invariant (CS7069 Fix):** In `netstandard2.1` plugins referencing .NET Framework `0Harmony.dll`, accessing `instruction.labels` or `new CodeMatcher(instructions, generator)` directly triggers `error CS7069` (`Label` or `ILGenerator` in `mscorlib` not found). Manipulate `List<CodeInstruction>` directly using reflection-based `TransferLabels(src, dst)` and `ClearLabels(inst)` via `System.Collections.IList` on the `labels` field. Never transpile `async`, `ref`, or `out` generic methods.

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

For live AI evidence, separate module registration from stage activation: a 23-module registry census alone proves availability only. Confirm the current stage's AI profile and observe its director/roster plus module-specific effects or counters in `Game`. An intermission boss-node read proves route telemetry only, not AI execution.

---

## References

- [Clean-Room Diagnostics Skill](../rogue-command-clean-room-diagnostics/SKILL.md) — Telemetry censuses, runtime director, MCP bridge tools.
- [Reverse Engineering Skill](../rogue-command-reverse-engineering/SKILL.md) — REA inspection, metadata tokens, member signatures.
- [Steam Runtime Validation Skill](../rogue-command-steam-runtime-validation/SKILL.md) — Reversible live validation runbook.
- `docs/research/BASELINE.md` & `AGENT-HANDOFF.md` — Verified commands and environment contracts.

### Existing pooled reuse route for native visual retargeting

- Keep both existing reuse routes; do not add another Harmony target. `EntityFactory.InstantiateEntity` can pop an `EntityController` from its native stack pool and then calls the regular `OnHasBeenInstantiated` callback rather than `OnHasBeenInstantiatedForReuse`. Its existing factory transpiler calls `RuntimeHooks.OnEntityInstantiatedTranspiler` after that factory initialization; route the entity through `RuntimeVisualDirector.OnEntityReused` there so both new instances and stack-popped instances restore Animator parameters, mapped bind poses, and suspension state. The explicit `OnHasBeenInstantiatedForReuse` callback remains wired to the same visual reset method for its separate native route.
- The installed `EntityController.Destroy` CIL transpiler invokes `OnEntityDestroyTranspiler` at method entry; suspend `NativeRigRetargetProfile` there before native destruction proceeds. On the pinned build, `EntityController.TakeDamage` calls this `Destroy` method. Keep `RuntimeVisualDirector.Update`'s dead-state check as a fallback, and recheck the complete game identity before relying on these members.
- The destroy transpiler validates `__originalMethod` against the exact game `EntityController` type and validates the exact static, non-generic callback before injecting `ldarg.0..2`; signature drift or empty IL must fail Harmony installation and roll back instead of silently returning untouched IL.
- Resolve serialized source/target Transform paths and bind poses on the cloned unit instance. The retarget component performs local pose copying in `LateUpdate`; both existing post-initialization callbacks reset animator state and mapped bind poses before the next active lifetime.
- Recheck the full game compatibility tuple before relying on reflected hooks. The C# harness covers callback dispatch. A 2026-11-10 Profile2 BepInEx run separately exercised a native stack-pool pop: the same GameObject returned with its retarget profile bound 8/8, unsuspended, at bind pose, and with movement false. This verifies the factory stack-pop reset route for that build; it does not establish rendered animation or a natural death-return cycle.

### Native Rig Retarget Lifecycle Evidence

- The exact `EntityController.Destroy` transpiler is locally covered for suspension dispatch and signature drift. Inspect `isSuspended` at the Destroy-entry callback boundary because the object may disappear before any later read. Separate diagnostic-damage lifecycle evidence from natural-combat death and rendered appearance; verify natural pooled reuse separately.
- In an earlier 2026-11-10 saved-Profile1 BepInEx pass, the lethal-test `EntityController.Destroy` counter increased from 16 to 17 but the GameObject disappeared before `IsSuspended` could be read. A later Profile2 pass directly observed suspension at the callback boundary; see the following bullet.
- In a following saved-Profile2 pass, a temporary postfix on `RuntimeHarmonyPatches.OnEntityDestroyTranspilerHook` captured two callbacks after `SuspendForDeath` returned and before the original `Destroy` continued. Both profiles remained bound 8/8 with `IsSuspended=true`; both objects then disappeared. These were direct diagnostic-damage destructions, not natural combat death. Do not infer death animation or rendering from the lifecycle state. Trace: `artifacts/runtime-smoke/restart-saved-profile-20261110T061923/retarget-destroy-entry-follow-up.json`.
- Static inspection of the pinned `Assembly-CSharp.dll` shows `PrespawnEntity` pushes into `EntityFactory.PrespawnedEntities`, `InstantiateEntity` pops, and `EntityController.Destroy` decrements the prespawn count then calls `UnityEngine.Object.Destroy` without a stack push. A natural death-to-this-stack return is not an expected native route; keep destroyed-instance cleanup separate from stack-pop reset. Static trace: `artifacts/runtime-smoke/native-retarget-natural-death-20261110T140359Z/native-prespawn-stack-static-evidence.json`.
- The same pass captured `Action_FireRecoil`, `RCL_Fire=triggered`, positive recoil, and matching computed yaw/pitch target poses. The live reuse counter stayed 0. A direct `OnHasBeenInstantiatedForReuse(false)` call on an active non-pooled entity threw `TargetInvocationException`; do not treat it as pool evidence.
- When `EntityController.Destroy` has `withoutTriggeringActions == false`, `OnEntityDestroyTranspilerHook` calls `RuntimeVisualDirector.TriggerDeathAnimationForDestruction` before `NativeRigRetargetProfile.SuspendForDeath`. The Profile2 BepInEx event order and hook-postfix state confirmed the trigger flag was set and the 8/8 profile was suspended before the native destroy continued. The actor was removed by direct diagnostic lethal damage; this confirms call ordering, not natural-combat death or rendered animation. Trace: `artifacts/runtime-smoke/retarget-animation-restart-profile2-20261110T1605Z/destroy-hook-method-events.json`.
- The later 2026-11-10 saved-Profile2 pass found a real `EntityFactory.PrespawnedEntities` entry and called `SpawnEntityJson` to pop it. BepInEx confirmed the same GameObject instance before and after; after reuse its seeded suspended profile was bound 8/8 and unsuspended, the maximum mapped target-pose delta was 0, and `RCL_IsMoving` was false. The object was preconditioned through a temporary BepInEx helper, so this is native stack-pop reset evidence, not a natural death-return test or a rendered-image claim. Full trace: `artifacts/runtime-smoke/native-retarget-pool-route-20261110T123828Z/native-pool-reuse-evidence.json`.
- Exact final Release SHA `fe08d495c3236a174c31e89bfae236c79f6af42541404770845a1043934f1856` was re-exercised after deployment. On the native `EntityController` retarget path, a disposable Ironclad Walker under native Move produced filtered `Action_Walk`/moving samples with 8/8 mappings bound and unsuspended; sampled post-baseline `BakeMesh` deltas were positive. The observer was stopped and its component census was empty. This is live Animator/retarget telemetry, not proof of Harmony callback coverage, natural AI, rendered gait quality, or pixels. Summary: `artifacts/runtime-smoke/retarget-final-release-20261111T050500Z/final-release-retarget-summary.json`.

### Terminal destroy call interception

- The native `EntityController.Destroy(bool, EntityController)` entry hook still runs first for death-trigger setup and retarget suspension. Its exact CIL has one terminal `UnityEngine.Object.Destroy(UnityEngine.Object)` call; validate that unique terminal call and replace only its method operand with the same-signature loader callback. Preserve the original call opcode, stack shape, labels, and exception blocks. If the target, callback signature, or call count drifts, fail installation rather than allowing an unsafe patch.
- The terminal callback holds destruction only when `DestroyHasBeenStarted`/`IsDestroyed`, a successful death trigger, the director, and a valid `Action_Death` clip/controller are present. Otherwise call native `UnityEngine.Object.Destroy` immediately. During the hold, disable gameplay behaviours/colliders and freeze physics while leaving Animator/director active; retire after clip duration adjusted for Animator speed plus the bounded grace interval. Increment `entityDestroyGameObjectTranspilerCount` when the callback runs and `deathVisualRetirementCount` only when a hold begins.
- Live Profile2 evidence used direct diagnostic lethal damage to a disposable clone and observed the death clip exceed normalized time 1 before destruction. The clone Animator alone used `UnscaledTime` to progress while the match stayed at `timeScale=0`. This proves callback/retirement telemetry, not natural combat or rendered pixels: `artifacts/runtime-smoke/retarget-death-visual-cloud-20261110170654/death-hold-runtime-result.json`.

### Guaranteed mod reward prefix (2026-11-11)

- Resolve `ChooseCard.InstantiateCardsWithDelay(List<string>)` by the full instance/`IEnumerator`/single-`List<string>` shape. The prefix must preserve native initialization and mutate the existing list in place; do not replace the coroutine or change the card count.
- `RuntimeHooks.InstantiateCardsWithDelayPrefix` delegates to `EnsureGuaranteedModCardOption`. Leave a natural offer intact if it already contains a mod product; otherwise choose one eligible product and replace one slot. Keep the pure policy separate from reflection so XP, starting, demo, role, and duplicate filters can be validated without game assemblies.
- Live evidence for the current Release showed the prefix replacing `RoboClawBot` with `tactical-operations:command-relay` in slot 2. This confirms hook execution and a populated `CardNew`; renderer telemetry is not pixel evidence. Report a nonmatching pool role as general fallback, not a thematic match.

### Current Release native rig destruction retarget evidence (2026-11-11)

- Release SHA-256 `2E740D387F600CAF0023ED7254F9F41ECF9E69E34E548328BFF147D250BC3DFB` was exercised against the pinned game identity. During direct diagnostic lethal damage to a disposable Ironclad Walker, the death visual entered pending retirement while its retarget profile was suspended and remained bound 8/8. `deathVisualRetirementCount` reached one; `entityDestroyTranspilerCount` and `entityDestroyGameObjectTranspilerCount` were each two for the whole QA scene, so do not attribute every callback to that actor. The actor later disappeared. This supports live lifecycle-hook telemetry, not natural combat, Harmony coverage on all destroy routes, or rendered pose quality. Evidence and identity: `docs/research/RETARGET-CURRENT-RELEASE-RUNTIME-2026-11-11.md`.
