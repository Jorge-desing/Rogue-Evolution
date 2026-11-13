---
name: rogue-command-pack-validation
description: "Validate Rogue Command content manifests, AssetBundles, loader builds, and install packages before delivery."
license: MIT
metadata:
  version: 1.2.0
  author: Hermes Agent
  hermes:
    tags: [Rogue Command, pack validation, schema, AssetBundles, harness, testing, deployment]
    related_skills: [rogue-command-clean-room-diagnostics, rogue-command-harmony-policy, rogue-command-unit-authoring]
---

# Rogue Command Pack Validation

Authoritative protocol for validating content manifests, AssetBundles, loader builds, and distributable packages.

---

## 1. Validation Commands & Baselines

| Test Suite / Tool | Command | Scope & Success Baseline |
|---|---|---|
| **C# Loader Harness** | `dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .` | **556 passed / 1 skipped** (symlink). Compiles against minimal `UnityEngine` stub; keep pure logic separate from Unity `MonoBehaviour` |
| **Complete Python suite** | `python -m tools.run_python_tests --output artifacts/python-tests/report.json` | Runs two isolated unit workers alongside one serial Blender-integration worker, then bpy modules serially with pinned Blender; reports every selected module, discovered case, failure, skip and timing. A missing required runtime or failed group returns nonzero. |
| **AssetBundle Audit** | `python tools/audit_unity_bundle.py <bundle>` | Inspects object counts, texture formats, shaders, dependencies, and byte size |
| **Pack Manifest Validator** | `python tools/validate_content_pack.py <pack_dir>` | Validates `content.json` against schema, checks referenced assets & duplicate IDs |
| **Packaging Runner** | `powershell -File tools/Package-Loader.ps1` | Creates release ZIP without test assemblies, PDBs, or editor caches |

---

## 2. Manifest Schema & Parameter Rules

Default `--jobs 2` partitions CPython modules without splitting their fixtures. Modules in the Blender test directory, with Blender bindings, or explicit Blender subprocess usage are placed together in one serial integration worker. Blender-only modules run serially after all CPython workers finish. Use `--jobs 1` for serial comparison or diagnosis, and `--jobs 2..4` to bound unit concurrency. Compare case inventories and outcomes before accepting scheduling changes.

For directed Python iteration use `--mode unit` or `--mode blender`; only the default `all` mode covers both. `unit` also includes existing CPython tests that launch Blender subprocesses. Override `--blender` for an explicitly chosen binary and `--timeout` for the per-worker limit. Reports retain existing asset/contract failures; do not interpret partial-mode success as full-suite acceptance. Shared test fixtures must stay class-scoped and read-only, or return independent copies to mutating cases; dynamic tool modules and mutable temporary files must remain uncached.

When removing or consolidating Python tests, compare complete before/after inventories and outcomes, record each removed ID and its rationale, and use isolated mocks to confirm relocated checks still reject regressions. Preserve runtime integrations and existing failures; textual occurrence counts do not establish whether a C# method is unused.

- **Schema Compliance:** Validate `content.json` against Draft 2020-12 rules in `content/schema/content.schema.json`.
- **Unit Variants (`units[].variants`):** Up to 8 loadout variants per unit; max 64 `balanceOverrides` per variant. Variant IDs must reject line terminators (`\r`, `\n`, CRLF, U+2028, U+2029).
- **Numeric Bounds & Precedence:**
  - Integer overrides capped at `16,777,216`; float overrides capped at `Single.MaxValue`.
  - Stat precedence: `customBalance` > raw `balanceOverrides` > positive finite `movement.moveSpeed`.
  - 25 verified `ChangeableValue` keys mapped directly to row fields (excludes global `ManaRechargePerSecond`).
- **Structured Type Safety:** Inspect destination row field types before assigning parameters (e.g. `EntityBalancingParameters.skillRange` is `int`, passing a `float` throws `ArgumentException`). Round or convert explicitly.
- **Relic `CardChange` Side Mapping:** Schema accepts only native `False`, `True`, `Irrelevant`. Runtime maps friendly/player $\rightarrow$ `True`, enemy/hostile $\rightarrow$ `False`, all/neutral $\rightarrow$ `Irrelevant`.
- **Stage Parameters:** `StageParameterBindingContract` binds only `blueprintPoolList`, `fillFirstLevelWithCardRewards`, and `putDefaultCardRewardInCenter`. `cardReward` is a value type (reject `cardReward.count/weight`). `startCountRange` must be an ordered pair (1–64).

---

## 3. Core Engine Harmony & Simulation Invariants

- **53 Installed Hooks:** Validates all registered hooks across Unit Creation, Pool Reuse, Placement Ghost, Factory Produce, Registry Init, Entity Register/Unregister, and Command Loop (`ExecuteCommand`, `ExecuteNextCommand`, `ResetCurrentCommand`, `DeleteCommandChain`).
- **Arbiter Lease Invalidation:** Order cancellation/deletion and tag divergence must trigger `ReleaseLease` or `ReleaseLeaseIfTagDiffers` on `AiCommandArbiterState`.
- **Factory Tracking:** Factory completions tracked by key and by product `EntityId` (`GetFactoryProducedCountByEntityId`).
- **Pre-Spawn Anti-Hitch Quotas:** Modded unit entity pools pre-warmed via `ApplyModPrespawnQuotas` in `RuntimeHooks.cs` to eliminate runtime GC hitching.
- **Standalone Buildings (`buildings[]`):** Supported via `ContentBuildingDefinition` with null-safe deep cloning without requiring unit production bindings.

---

## 4. AssetBundle & Prefab Contracts

- **Self-Contained Shaders:** AssetBundles must embed compiled shaders (`VehiclePBR_VertexWear` or `Standard`) for URP execution; base game assets do not provide built-in shaders.
- **Address & Name Verification:** Enumerate bundle container keys (`env.container.keys()`) with UnityPy; every unit model, icon sprite, and building mesh must resolve to a valid asset.
- **Script-Free Prefabs:** Visual prefabs must contain zero custom `MonoBehaviour` scripts.
- **Diagnostic Trace Formatting:** In bounded outputs (e.g. `MaxTraceCharacters = 4096`), place omission/loss counters at the head of the trace so tail truncation never masks dropped observations.

---

## 5. Deployment & Repository Protection

- **Deployment Destination:** Plugin DLL must install to `BepInEx/plugins/RogueCommandExpansionLoader.dll` (never nested in a subfolder).
- **Process Closed Check:** Verify `Rogue Command.exe` is completely terminated before modifying `BepInEx/` or profile directories.
- **Manifest Restoration:** Pre-record deployed hashes and restore only task-owned targets; verify profile file hashes remain unchanged.
- **Staging Boundaries:** Never stage `artifacts/`, `Library/`, editor caches, or game assemblies. Stage only task-owned code, docs, and assets.

---

## 6. References

- [Clean-Room Diagnostics Skill](../rogue-command-clean-room-diagnostics/SKILL.md) — Telemetry queries and live MCP bridge.
- [Harmony Policy Skill](../rogue-command-harmony-policy/SKILL.md) — Installed hook contracts and safety rules.
- [Unit Authoring Skill](../rogue-command-unit-authoring/SKILL.md) — Balance archetypes and production building templates.
- `content/schema/content.schema.json` & `README.md` — Authoritative schema definitions.

## Canonical Visual Module Validation

- `visualVariants[0]` is Level 1 and must not declare modules. Later variants may declare up to four module references, one each for `ERA`, `APS`, `Sponsons`, and `FuelDrums`; a module has `slotId` plus `assetName`, and resolves through the owning unit's `assetBundleId`.
- Canonical Level 2 authoring binds each runtime `(variantId, slotId)` to a geometry preset in `content/source-art/canonical-visual-module-roles.json`; keep `geometryPreset` separate from the Material Maker `preset` and out of pack JSON. The schema-3 generated manifest pins the registry and build-policy SHA-256 values and must cover every declaration exactly once. The user-authorized no-calibration-limits policy applies to all geometry build outputs; preserve `UNCALIBRATED_USER_AUTHORIZED` and do not present it as performance validation.
- Run the schema/content validator and `tools/validate_content_bundle_references.py` for both the prefab references and every `visualVariants[].modules[].assetName`. Confirm the AssetBundle's serialized container keys with UnityPy or the Unity reopen check; do not infer them from source filenames alone.
- `tools/Bind-CanonicalVisualModules.py --check` enforces the current authored matrix (18 Level 1 primaries and 57 Level 2 modules). This matrix check is project content validation, not runtime behavior evidence.
- The canonical build also requires exact authoring-role coverage from `content/source-art/canonical-visual-module-roles.json`, matching pack `modules[].slotId`/`assetName` to the generated manifest, and exactly one required hardpoint socket in the owning Level 1 prefab. These are build-time hierarchy checks; they do not prove variant application in a game run.
- For fit-sensitive modules, validate the imported Level 1 FBX actually referenced by the prefab as well as the source blend. The current Bulwark side ERA regression imports the FBX pinned by SHA-256, checks all four backing plates against the armor and tread geometry, and fails if the host changes without a reviewed recipe update. A render based only on the newer `.blend` does not validate the stale Unity FBX.
- For the complete current Blender/Material Maker/Unity module rebuild, run `tools/Build-CanonicalVisualModules.ps1`; it refreshes four Level 1 visual prefabs before validating Level 2 slot bindings. The check rejects duplicate or noncanonical sockets as well as missing required sockets. Then audit both fresh generated packs under `artifacts/canonical-level2-<UTC>/validation-packs/`. The latest authorized build is `artifacts/canonical-level2-20261113T002430Z/`; its four declared bundles were synchronized to `content/packs/*/bundles/` and passed current pack/reference and bundle-hash checks. After future module changes, revalidate a fresh stage and compare bundle hashes before treating versioned copies as current.

## Native Rig Retarget Manifest Checks

- Install a refreshed native-retarget pack as one artifact set: `content.json`, `assetbundle-manifest.bundle`, and its referenced content bundle. Record and compare each installed SHA-256 with the canonical validated output; changing only the manifest can leave the game loading a stale prefab hierarchy.
- Validate `nativeRigRetarget.mappings[]` as 1–64 `{sourcePath,targetPath}` pairs, present only on a native-template visual overlay. Paths are relative to different roots: the native unit prefab and the custom visual prefab.
- Reject empty, rooted, backslash, whitespace-only, `.`/`..`, duplicate target, and source paths equal to or above the visual root. Runtime binding requires each exact path to resolve once.
- Run `tools/validate_native_visual_roots.py <pack>` to resolve every source path in the pinned game's `resources.assets` and every target path under the exact GameObject container entry for `visualAssetName` in the unit's declared bundle. The companion `tests.python.authoring.test_native_visual_retarget_preflight` protects this source/target check and pack-relative bundle path containment. These checks do not prove that the mapped motion works in a Steam match.
- A 2026-11-10 BepInEx MCP readback observed an 8/8-bound Ironclad clone in `Action_FireRecoil` with mapped yaw/pitch targets matching computed poses. This is runtime transform telemetry, not rendered appearance; death appearance and natural pooled reuse are still unknown.
