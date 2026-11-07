---
name: rogue-command-harmony-policy
description: "BepInEx 5 + Harmony 2 patching policy for Rogue Command (v1.0.9 / Unity 2021.3.45f2, Mono x64). When creating or auditing Unity game plugins, Harmony patches, assembly identity checks, or IL2CPP/back-end decisions. [Política de BepInEx 5 + Harmony 2 para Rogue Command (v1.0.9 / Unity 2021.3.45f2, Mono x64). Uso: plugins Unity, patches Harmony, verificación de identidad de ensamblados, decisiones backend IL2CPP]"
version: 1.0.0
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, Unity, Mono, BepInEx, Harmony, IL2CPP, patching, SHA256, MVID, assembly verification]
    related_skills: [rogue-command-clean-room-diagnostics, rogue-command-reverse-engineering, rogue-command-game-research, rogue-command-steam-runtime-validation, rogue-command-pack-validation, rogue-command-unit-authoring]
---

# Rogue Command Harmony Policy

Use this skill when creating, reviewing, or auditing BepInEx 5 / Harmony 2 plugins for Rogue Command. It encodes the project's compatibility identity, plugin lifecycle rules, Harmony patch patterns, IL2CPP posture, and assembly verification gates used before any reflected signature or metadata token is relied upon.

## When to Use

- Writing the loader plugin (`RogueCommandExpansionLoader.dll`) or any BepInEx plugin.
- Adding, reviewing, or removing a Harmony patch (`Prefix`, `Postfix`, `Transpiler`, `Finalizer`).
- Deciding Mono vs IL2CPP patching route.
- Verifying `Assembly-CSharp.dll` SHA-256 / MVID before patched methods are used at runtime.
- Choosing pin versions for Rogue Command 1.0.9 / Unity 2021.3.45f2 / BepInEx 5.4.23.5 / Harmony 2.

## Compatibility Identity (verify before every session)

Always recheck this tuple before relying on reflected signatures, CIL metadata tokens, or serialized assets:

- **Game version:** Rogue Command `1.0.9` (marker in `version.txt`, format `9= Version 1.0.9`).
- **Engine version:** Unity `2021.3.45f2` (Mono x64, 64-bit process).
- **Mod loader:** BepInEx `5.4.23.5` (Mono Standalone x64, target `netstandard2.1`).
- **Harmony:** Harmony 2 (BepInEx `BepInEx.Harmony` / `HarmonyX`).
- **Assembly-CSharp.dll SHA-256:** `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`.
- **Assembly-CSharp.dll MVID:** `db0e9462-128d-4296-9900-de18b4bd0c00`.
- **Unity CoreModule SHA-256:** `6305c82c17ffe111a016e8d8ca3e8b2d9203980b4f58cb6e90171e5629994290` (MVID `37d484f4-070f-4518-bfa9-9fa57b7a2871`).
- **Assembly path:** `Rogue Command_Data/Managed/Assembly-CSharp.dll`.

**Do not** infer Mono vs IL2CPP from a guess. Verify: `Assembly-CSharp.dll` in `Managed/` + Unity version `2021.3.45f2` = Mono backend; `GameAssembly.dll` + `il2cpp_data/Metadata/global-metadata.dat` = IL2CPP backend. A Mono backend means raw IL patching via Harmony is straightforward; IL2CPP means code generation + interop barriers.

## BepInEx 5 Plugin Lifecycle

### Entry point contract

- Class must extend `BepInPlugin` / `BaseUnityPlugin` and carry `[BepInPlugin(<GUID>, <Name>, <Version>)]`.
- Load order: `Awake()` for plugin init, `Start()`/later for game-object-level work, `Update()`/`LateUpdate()` for per-frame.
- Use a single static `Instance` with a duplicate-suppression guard (destroy `this.gameObject` on duplicate) so a plugin cannot double-register.
- Set `DontDestroyOnLoad(this.gameObject)` and hide it (`HideFlags.HideAndDontSave`) so the loader survives scene transitions and is not saved with the profile.
- Keep heavy work off `Awake()`; use `Task.Run` with a `CancellationTokenSource` "kill switch" for async server/bridge loops, never `Thread`.
- Always check `Harmony` instantiation throws before patching; a bad `Harmony` ID leaks into later patches.

### Lifecycle phases

1. **Plugin load:** BepInEx resolves `<GUID>.plugins` manifest, constructs the class, calls `Awake()`.
2. **Compatibility gate:** run `GameCompatibility.TryValidate(gameRoot)` before touching reflected types. Failure = abort patching.
3. **Harmony bootstrap:** create `new Harmony(<unique ID>)`; store for later `Patch()` calls.
4. **Assembly verification:** hash `Assembly-CSharp.dll`; assert SHA-256 and MVID match pinned values.
5. **Patch registration:** `[HarmonyPatch(typeof(T), "MethodName")]` or `harmony.Patch(...)` for each entry point.
6. **Runtime phase:** `Update()`/`LateUpdate()` dispatch main-thread jobs; `OnDisable()`/`OnDestroy()` tear down without killing the game process.
7. **Teardown:** dispose `CancellationTokenSource`, stop HTTP listeners, remove temp components; never call `Application.Quit()` unless the user requests it.

### Rules

- **Never** block the Unity main thread in `Awake()` or patch code that runs on it.
- **Never** install patches before the compatibility/SHA gate passes.
- **Never** let a patch body throw: wrap in try/catch, log, return default.
- **Never** call a game method unless its declaring type, name, parameter count, and calling convention are verified against the pinned assembly.

## Project Patch Registration Pattern

This project uses a centralized installer rather than attribute-based patches. Follow this exact sequence when adding a new hook:

1. **Declare the hook body** in `src/RogueCommandExpansionLoader/Runtime/RuntimeHooks.cs`:
   - Add a `public static void MethodName(...)` entry inside `RuntimeHooks` for the actual logic.
   - Add a matching `public static void MethodName(...)` entry inside `RuntimeHarmonyPatches` that delegates to `RuntimeHooks.MethodName(...)`.
2. **Resolve the target method** in `src/RogueCommandExpansionLoader/Runtime/RuntimePatchInstaller.cs` using `RuntimeReflection.FindUniqueMethod(...)` with a shape predicate.
3. **Resolve the Harmony hook method** with `Hook("MethodName")`.
4. **Register the plan** with `plan.Add(new PatchPlan(targetMethod, prefix, postfix, finalizer))`.
5. **Build and run validation tests**; the test project links `RuntimeHooks.cs` directly.

## Harmony Patching Patterns

Reference: claude-mod-skills `harmony-patch-review` checklist. Patch only the verified target; assert the original method is still present in the pinned assembly.

### Prefix

- Runs **before** the original. Can short-circuit by returning `false` (skip original).
- Use for guards, early returns, and conditional routing.
- Signature pattern: `(MethodInfo __originalMethod, object __instance, Type[] __arguments)`; parameters map to original args; use `ref`/`out` parameters through `HarmonyArguments`.
- Do not allocate collections or run LINQ inside the prefix; the game calls the target frequently.

```csharp
[HarmonyPatch(typeof(Game), nameof(Game.Update))]
[HarmonyPatch(typeof(StageParameters), nameof(StageParameters.Initialize))]
public static class GameInitializePatch
{
    public static bool Prefix(MethodInfo __original, object __instance, object[] __args)
    {
        if (GameCompatibility.Diagnostic != null)
            return false; // hijack only when validated; otherwise delegate to original
        // guard only; do not change behaviour
        return true;
    }
}
```

### Postfix

- Runs **after** the original completes.
- Use for logging, final read-only observations, or altering a `$ref`/`out` return.
- Signature: `(MethodInfo __originalMethod, object __instance, object[] __args, ref ReturnType __result)`.

```csharp
[HarmonyPatch(typeof(ContentStage), nameof(ContentStage.GenerateMap))]
public static class StageGeneratePatch
{
    public static void Postfix(MethodInfo __original, object __instance, object[] __args, ref StageGenerationResult __result)
    {
        // observe / log read-only; mutate only validated ref/out returns
    }
}
```

### Transpiler

- Rewrites the IL of the original method; replaces the body.
- **Always** use `CodeMatcher` (not raw `ILGenerator`/`OpCodes`): it is the project's only supported rewrite path and it fixes index/stack bookkeeping.
- Target the **shortest unique label**; traverse with `MatchEnd`, assert instructions before rewriting.
- Never transpile `async`/`ref`/`out`/generic methods unless you can prove the IL shape; these produce `ObjectCollectedException` or unverifiable code.
- Keep transpiler bodies short: move analysis into a cached helper called lazily.

```csharp
[HarmonyPatch(typeof(EntityController), nameof(EntityController.ChangeArmor))]
public static class ChangeArmorTranspiler
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions, 0);
        // match and rewrite by verified IL shape only
        return matcher; // verified rewrite
    }
}
```

### Finalizer

- Runs last, after all other patches; use for cleanup or ensuring invariants.
- Rarely needed in mod loaders; prefer `Postfix` unless a patch ordering contract demands it.

### Patch priority & ordering

- Harmony applies patches in registration order; use `[HarmonyPriority]` on static methods to control order when multiple plugins patch the same target.
- Set `priority: HarmonyPriority.First`, `Last`, or an explicit `int` before relying on call order.
- Never assume the game's own patch is called before yours; verify the target's call chain in decompiled IL.

### Patch safety checklist (from claude-mod-skills)

1. Target method exists in the **pinned** assembly (compile-time reflection assertion, not a runtime guess).
2. Prefix/Patch body does not throw; catches and logs all exceptions.
3. No allocations, LINQ, or string interpolation in hot paths.
4. `ref`/`out` parameters handled through `HarmonyArguments` only if signature is verified.
5. Transpiler uses `CodeMatcher`; no raw IL rewriting.
6. Patch priority set when multiple plugins target the same method.
7. Original method not bypassed unless the patch explicitly returns `false` (Prefix) or is a verified transpiler rewrite.

### C# editing safety (patch tool)

- When inserting new members with a find-and-replace patch immediately **before** a method or property declaration, anchor the match on the **entire declaration line plus its opening brace and first statement**, not on a trailing snippet that the fuzzy matcher can extend into the next declaration. The matcher has repeatedly consumed the following method signature, leaving an orphaned `{`/`return` and a broken file.
- After every patch batch, run `dotnet build` (plugin + test project) before the next edit; a structurally broken file still reports the previous error count as 0 only if it was not recompiled. Treat "0 errors" as proof only when the file you just edited was part of the build.
- If a patch leaves an orphaned brace or signature, do not attempt a third blind patch on the same region; rewrite the enclosing method with `write_file` (or restore from git) and re-apply cleanly.
- A patch that already applied will fail on a re-issue: re-sending the identical `old_string` returns "Could not find a match" (the text is already gone) and the error's "Did you mean one of these sections" hint shows the NEW, already-applied content. When the tool also flags "byte-identical to an earlier call" or "Nth consecutive identical call," the edit is done — confirm with `read_file` and move on; never loop retries on the same arguments.

### Optional target resolution (cross-version robustness)

When adding hooks for native methods whose signatures may differ across game versions or whose presence is uncertain (e.g. `EntityController.Die`, `Heal`, `RepairArmor`):

- Resolve the target with a **defensive wrapper** (`TryFindUniqueMethod`) that catches exceptions from `RuntimeReflection.FindUniqueMethod` and returns `null` on miss, instead of letting the miss throw and abort the whole patch plan.
- Treat the patch wrapper method and the Harmony postfix as **optional**: only register them when both the target `MethodInfo` and the wrapper `MethodInfo` are non-null.
- Gate the entire optional block behind a `if (target == null || wrapper == null) { return; }` (or skip) so a single missing target leaves every other verified patch intact.
- Keep the wrapper body a no-op-safe extension point (`try/catch` + log) so a runtime surprise in the hook never propagates to the game.
- **Do not** promote a missing optional target to a hard failure; only the pinned-identity gate (`GameCompatibility.TryValidate`) is a hard abort.

Pattern:

```csharp
MethodInfo target = TryFindUniqueMethod(type, "Die", m => m.GetParameters().Length == 0);
MethodInfo wrapper = target == null ? null : typeof(Hooks).GetMethod("DiePostfix");
if (target == null || wrapper == null)
{
    // optional: skip without disabling the rest of the plan
    return;
}
harmony.Patch(target, postfix: new HarmonyMethod(wrapper));
```

### `Harmony.Patch()` return value (counting installed hooks)

- `harmony.Patch(original, prefix, postfix, transpiler, finalizer, ...)` returns a `MethodInfo` (the patched original), **not** a `bool`. A `null` return means the patch was not applied.
- To count or list installed hooks, assign the result and check non-null: `MethodInfo patched = harmony.Patch(...); if (patched != null) { names.Add(...); count++; }`. Writing `if (harmony.Patch(...))` is `CS0029` (cannot implicitly convert `MethodInfo` to `bool`) — it will not compile, so a hook-count getter that relies on it silently fails at build time rather than reporting a wrong count at runtime.
- Reset the count and name list in the same teardown that clears the `Harmony` instance; re-install accumulates into a fresh zeroed list, never into the previous run's.

## Test Compilation Constraint

`tests/RogueCommandExpansionLoader.ValidationTests/*.csproj` links `RuntimeHooks.cs` as a compile item but does **not** reference the game assembly. Any code in `RuntimeHooks.cs` that uses `Game.`, `ProfileManager.`, or other game types directly will fail to build in the test project.

**Rule:** when a hook needs game types, resolve them through reflection inside the hook body using `RuntimeReflection.FindUniqueType(mutator.GameAssembly, "TypeName")` and `BindingFlags`. Do not add `using GameNamespace;` or direct field/method calls on game types in `RuntimeHooks.cs`.

## IL2CPP Considerations

- Rogue Command 1.0.9 ships **Mono** (verified: `Assembly-CSharp.dll` in `Managed/`). IL2CPP is not the current backend, but the policy must remain posture-aware.
- **If a future update migrates to IL2CPP:** `Assembly-CSharp.dll` disappears from `Managed/`; the compatible identity becomes `GameAssembly.dll` + `<Game>_Data/il2cpp_data/Metadata/global-metadata.dat`. Raw Harmony IL patching no longer works; the loader must switch to generated interop (BepInEx `Il2CppInterop` / `MonoMod.RuntimeDetours` interop layer).
- Reference: `bepinex-il2cpp-modding` skill and `universal-modder/hwa` `unity.md` engine reference for the IL2CPP route (interop, metadata, `ObjectCollectedException` handling).
- When verifying the backend, check **both** markers: Mono = `Assembly-CSharp.dll` + Unity version; IL2CPP = `GameAssembly.dll` + `global-metadata.dat`. Do not assume one or the other.

## Assembly Verification

### SHA-256

- Hash `Assembly-CSharp.dll` (and the Unity CoreModule DLL) and compare against the pinned SHA-256 tuple above.
- Implementation: `SHA256.HashData(File.ReadAllBytes(path))` compared with `StringComparison.Ordinal` to the stored hex string.
- Failure is a **hard abort**: the loader must stop patching and log `Assembly-CSharp.dll SHA-256 mismatch`.
- Source of truth: `GameCompatibility.TryValidate()` / `src/RogueCommandExpansionLoader/Compatibility/GameCompatibility.cs`.

### MVID

- MVID (Managed Virtual Method Dispatch table) is a per-build GUID that changes when the assembly is rebuilt.
- Verify with `peverify /headers`, `ildasm`, or REA `inspect_managed_artifact` (MVID field).
- If a patch reconciles against a cached MVID, re-verify after any game update; the MVID changes on rebuilds.

### Version pinning summary

| Component | Pinned value |
|---|---|
| Rogue Command | `1.0.9` |
| Unity | `2021.3.45f2` |
| BepInEx | `5.4.23.5` (Mono Standalone x64, netstandard2.1) |
| Harmony | 2 |
| Assembly-CSharp.dll SHA-256 | `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b` |
| Assembly-CSharp.dll MVID | `db0e9462-128d-4296-9900-de18b4bd0c00` |
| Unity CoreModule SHA-256 | `6305c82c17ffe111a016e8d8ca3e8b2d9203980b4f58cb6e90171e5629994290` |

## Evidence Boundaries

- **Static inspection:** repository files, decompiled IL, serialized Unity assets. Does not establish runtime behavior.
- **Local build/validation:** `dotnet build`/`dotnet run --project tests/...`, ZIP/archive audits, Harmony install count. Proves code correctness and loader correctness, not match behavior.
- **In-game runtime evidence:** a live Steam-launched session (AppID `1461910`) that observes the patched behavior. Only this tier can prove registration, production, or AI execution; a successful start does not.

Label every finding and every claim with exactly one tier. Do not promote static inspection or local validation to runtime evidence.

## Reference Documentation

- [Clean-room utility recreation and live agent-readable diagnostics](references/clean-room-live-diagnostics.md) — FNV-1a color policy, manual JSON writer, primitive DTOs, MCP bridge discovery via scene GameObject, 128-tool BepInEx MCP suite, command director, runtime censuses, aggregators, and deployment verification workflow.
- [Rogue Command Clean-Room Diagnostics Skill](../rogue-command-clean-room-diagnostics/SKILL.md) — Authoritative skill for clean-room telemetry, gameplay command director, and BepInEx MCP integration.
- `docs/research/BASELINE.md` — compatibility baseline.
- `docs/research/AGENT-HANDOFF.md` — agent handoff and verified commands.
- `docs/research/STEAM-RUNTIME-CONTROL.md` — Steam/BepInEx runtime, profile isolation, and reversible deployment.
- `tools/Decompile-ManagedType.ps1` — pinned ILSpy decompilation script.
- `docs/research/MANAGED-DLL-DECOMPILATION.md` — managed DLL decompilation guide.
- claude-mod-skills (`ing-gom/claude-mod-skills`) — `harmony-patch-review`, `mod-bootstrap`, `mod-assembly-map`, `mod-perf-guard`, `mod-hot-reload`, `ilspy-decompile`.
