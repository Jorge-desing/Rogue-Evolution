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
| **Complete Python suite** | `python -m tools.run_python_tests --output artifacts/python-tests/report.json` | LPT-balanced CPython unit workers alongside one serial Blender-subprocess integration worker and one batched sequential Blender (`bpy`) session running in parallel when `--jobs >= 2`; reports every selected module, discovered case, failure, skip and `moduleSeconds` timing. A missing required runtime or failed group returns nonzero. |
| **AssetBundle Audit** | `python tools/audit_unity_bundle.py <bundle>` | Inspects object counts, texture formats, shaders, dependencies, and byte size |
| **Pack Manifest Validator** | `python tools/validate_content_pack.py <pack_dir>` | Validates `content.json` against schema, checks referenced assets & duplicate IDs |
| **Packaging Runner** | `powershell -File tools/Package-Loader.ps1` | Creates release ZIP without test assemblies, PDBs, or editor caches |

---

## 2. Manifest Schema & Parameter Rules

Packaging uses Python with the project jsonschema dependency. After copying the release packs, Package-Loader.ps1 validates each staged manifest against the staged schema and audits references across the staged set before writing the ZIP. If the canonical build provenance gate fails first, regenerate its artifacts before attempting a full package check.

Referenced files must use pack-relative paths. The Python validator rejects traversal, absolute or drive-relative paths, symlinks and reparse points along the reference below the pack root, even with an alternate schema.

Use `python -m tools.validate_content_pack <pack_dir>` for canonical schema and pack file checks. Schema errors stop file inspection. Use `--schema` for an intentional alternate contract.

The Python acceptance runner fails and emits an aggregate report when the selected inventory is empty or a worker returns an invalid report. A successful worker must report a positive integer test count. Use a finite positive per-worker timeout.

Default `--jobs 2` partitions CPython modules using LPT weights from prior `moduleSeconds` durations without splitting their fixtures. Modules in the Blender test directory, with Blender bindings, or explicit Blender subprocess usage are placed together in one serial integration worker. Blender-only (`bpy`) modules run sequentially inside a single Blender session (`bpy.ops.wm.read_factory_settings(use_empty=True)` between modules), concurrently with CPython unit workers when `--jobs >= 2`. Use `--jobs 1` for fully serial comparison or diagnosis, and `--jobs 2..4` to bound unit concurrency. Compare case inventories and outcomes before accepting scheduling changes. Use `read_text_cached` / `parse_ast_cached` in `tests/python/helpers.py` for immutable repo source inspection while keeping `read_json` uncached.

For directed Python iteration use `--mode unit` or `--mode blender`; only the default `all` mode covers both. `unit` also includes existing CPython tests that launch Blender subprocesses. Override `--blender` for an explicitly chosen binary and `--timeout` for the per-worker limit. Reports retain existing asset/contract failures; do not interpret partial-mode success as full-suite acceptance. Shared test fixtures must stay class-scoped and read-only, or return independent copies to mutating cases; dynamic tool modules and mutable temporary files must remain uncached.

When removing or consolidating Python tests, compare complete before/after inventories and outcomes, record each removed ID and its rationale, and use isolated mocks to confirm relocated checks still reject regressions. Preserve runtime integrations and existing failures; textual occurrence counts do not establish whether a C# method is unused. For suite-wide cleanup, inventory the current checkout by module and case before selecting candidates; nested worktrees and staged third-party tools are separate scopes. A case without direct assert calls may validate through a helper or expected exception. Remove weaker source checks only after identifying the same source section and a retained stronger predicate; matching comments or implementation names does not prove the claimed behavior.

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
- For the complete current Blender/Material Maker/Unity module rebuild, run `tools/Build-CanonicalVisualModules.ps1` (10-phase fail-closed order): `level1-models` (`-RebuildLevelOneModels`) → `level1-textures` (`-RebuildLevelOneTextures`) → `blender` → `textures` → `unity` → `unity-audit` → `staging-and-pack-validation` → `animation-matrix` → `bundle-promotion` → `package-loader` (`-PackageLoader`). It supports `-Incremental` (provenance cache in `artifacts/canonical-level2-cache.json` via `Get-CanonicalPhaseInputHash` / `Test-CanonicalPhaseCacheHit` / `Save-CanonicalPhaseCache` in `tools/CanonicalVisualBuildProvenance.ps1` across all 5 build phases including `level1-models`, `level1-textures`, and `unity` artifact hydration with SHA-256 verification and upstream invalidation), `-SkipBlender`, `-SkipTextures`, `-SkipLevelOneRefresh`, `-SkipUnityBuild`, `-SkipNativeVisualValidation`, `-SkipAnimationMatrix`, `-NoPromoteBundles`, `-RebuildLevelOneModels`, `-RebuildLevelOneTextures`, `-PackageLoader`, `-RestoreBundlesFrom`, `-DryRun`, `-Packs`, `-GameRoot`, `-TextureSizePower` (`9..12`, default `11` = `2048`), `-MaxParallelExports` (`1..8`), `-PackWorkers` (`1..8`), and `-ArtifactRoot`.
- In full runs, `Build-CanonicalVisualModules.ps1` runs batch Material Maker audits (`python -m tools.audit_material_maker_output --directory <dir> --prefixes <list...> --workflow urp --output-dir <ArtifactRoot>/logs --output <batchJson>`, writing per-prefix `mm-audit-<prefix>.json`, returning `materialMakerBatchQualityReport`, and failing closed with exit `2` on `qualityGate.passed == false`, while preserving single-prefix `--prefix` mode), launches a single Unity `2021.3.45f2` batchmode session via `CanonicalVisualModuleBuilder.RefreshAndBuildCanonicalModules` (or `BuildCanonicalModules` / `RefreshCanonicalLevelOneVisuals` when one half is skipped; or hydrates verified bundles/logs from cached `metadata.artifactRoot`), runs `tools.audit_unity_build --strict` to `<ArtifactRoot>/logs/unity-audit.json`, stages and validates packs/references plus automatic `tools/validate_native_visual_roots.py <stagedRoot> --game-root $GameRoot` when `$GameRoot\version.txt` exists for packs declaring `visualRootPath`/`nativeRigRetarget` (unless `-SkipNativeVisualValidation`), gates promotion on `tools.run_animation_validation_matrix --strict-loops` (unless `-SkipAnimationMatrix`), promotes validated bundles to `content/packs/*/bundles/` with SHA-256-verified backups in `<ArtifactRoot>/prior-bundles-backup/` via `Invoke-CanonicalBundlePromotion` (unless `-NoPromoteBundles` or `-SkipUnityBuild`), optionally runs `tools/Package-Loader.ps1` (`-PackageLoader`), automatically restores backups via `Restore-CanonicalBundlePromotion` in `catch` if `bundle-promotion` or `package-loader` fails after promotion begins, and writes `<ArtifactRoot>/build-summary.json` (including `audits.nativeVisualRoots`, `promotion`, `packageLoader`, and `rollback`) in `try/finally` (unless `-DryRun`). Use standalone `Build-CanonicalVisualModules.ps1 -RestoreBundlesFrom <backupRoot> [-Packs ...] [-DryRun]` to invoke `Restore-CanonicalBundlePromotion` directly (restoring `existed=true` bundles and removing `existed=false` bundles with SHA-256 checks).
- The check rejects duplicate or noncanonical sockets as well as missing required sockets. Audit both fresh generated packs under `artifacts/canonical-level2-<UTC>/validation-packs/`. The latest authorized live build is `artifacts/canonical-level2-20261115T001842Z/`; after future module or pipeline changes, revalidate a fresh stage and compare bundle hashes before treating versioned copies as current.

## Native Rig Retarget Manifest Checks

- Install a refreshed native-retarget pack as one artifact set: `content.json`, `assetbundle-manifest.bundle`, and its referenced content bundle. Record and compare each installed SHA-256 with the canonical validated output; changing only the manifest can leave the game loading a stale prefab hierarchy.
- Validate `nativeRigRetarget.mappings[]` as 1–64 `{sourcePath,targetPath}` pairs, present only on a native-template visual overlay. Paths are relative to different roots: the native unit prefab and the custom visual prefab.
- Reject empty, rooted, backslash, whitespace-only, `.`/`..`, duplicate target, and source paths equal to or above the visual root. Runtime binding requires each exact path to resolve once.
- Run `tools/validate_native_visual_roots.py <pack> [--game-root <dir>]` (invoked automatically during `Build-CanonicalVisualModules.ps1` phase 7 when `$GameRoot\version.txt` exists, unless `-SkipNativeVisualValidation`) to resolve every source path in the pinned game's `resources.assets` and every target path under the exact GameObject container entry for `visualAssetName` in the unit's declared bundle. The companion `tests.python.authoring.test_native_visual_retarget_preflight` protects this source/target check and pack-relative bundle path containment. These checks do not prove that the mapped motion works in a Steam match.
- A 2026-11-10 BepInEx MCP readback observed an 8/8-bound Ironclad clone in `Action_FireRecoil` with mapped yaw/pitch targets matching computed poses. This is runtime transform telemetry, not rendered appearance; death appearance and natural pooled reuse are still unknown.

For internal subcase cleanup, retain distinct boundaries, types and before/after states; consolidate identical positive controls outside invalid-input loops. Deterministic mandatory fixtures must fail when missing rather than skip.

When two cases use identical inputs and call the same function, consolidate their distinct assertions before removing one. Keep direct tests of separate public entry points even when their implementations delegate to the same helper. Validating an unchanged baseline sample again is redundant when class setup already enforces it.

The Surge Arc Tank source check retains the suspension envelope assertion; the former 7,000-polygon guard is obsolete under the global diagnostic-only geometry policy. Do not reinstate geometry build gates as test expectations.

Ironclad asset tests inspect the current source blend with pinned Blender and current bundle with UnityPy; never depend on ignored reports from a previous session. Structural profile expectations must follow reviewed hardware removals (Arachnid, Sentinel and Command Relay), including exact absence checks. Missing rigged bundle clips require repairing the FBX/avatar and rebuilding, not weakening animation assertions.

General offline reviews must inspect auxiliary validator coverage as well as suite status: healthy default values and swallowed decode exceptions can conceal unsupported serialized fields. Audio/modifier audits now have explicit read-error/non-finite regressions; audit JSON is local evidence only.

Material Maker config validation and the explicit four-map/shader assertions share one test on the same configuration; retain both the validator result and independent literal assertions in that test.

In snapshot tests, an exact-argument lookup using `next` already fails when the method is absent; repeating a separate presence assertion for the same call is redundant. Preserve independent presence checks for methods without argument lookups.

For registry tests, an exact ID-filtered entry lookup used to verify product mapping already fails when the factory is absent; preserve the independent unit-ID check.

An earlier successful `row["unitId"]` value assertion already establishes the key exists in that same row; preserve its value check when removing the redundant membership check.

The scaffold statistics localization and AI-module assertions share one `pack_stats` call on the same generated input; retain both independent output checks.

Unit-catalog scaffold count and JSON-serializability checks share one exported report; keep both checks when consolidating their identical input scenario.

Scope exact-body duplicate scans to discoverable test methods. Identical `pass` bodies nested inside a test fixture may deliberately produce different outcomes through skip decorators or fixture setup; retain them when they exercise distinct runner states.

`purge-dangling` in the pack editor requires explicit confirmed-invalid `unitIds` and `profileIds` (CLI: `--dangling-unit-ids`, `--dangling-profile-ids`); an empty selection removes nothing. Resolve native and cross-pack IDs first, and inspect emptied blueprint pools before writing.

The editor routes `rename-stage`, `clone-stage`, `rename-ai-profile`, and `clone-ai-profile` through CLI and batch; supply a namespaced `newId`. The advertised extract/import helpers are Python-only and their dependency closure is not yet established.

The Python composition helpers return independent extracted data and reject duplicate or cross-kind unit/factory IDs during import. They do not yet resolve full dependency closure or copy bundle files; review the resulting manifest and assets before use.

Unit import includes asset bundle declarations referenced by imported units, factories, stages and profiles, and rejects incompatible bundle, stage, profile and manifest declarations atomically. This changes manifest data only; bundle files and full dependency closure still require separate handling.

`tools.bundle_manifest_dependencies.resolve_bundle_dependencies` reads the Unity manifest with UnityPy and resolves a dependency-first transitive bundle list, rejecting missing bundles, malformed indexes and cycles. It is not yet connected to composition or file transfer.

`tools.pack_bundle_transfer` currently provides an intermediate full-manifest plan and copy helper. It compares all declared bundle names with Unity's manifest, checks pack-relative paths and existing-file hashes, and rolls back newly copied files on ordinary failures. It is not yet wired into composition and does not establish safety against concurrent path replacement; do not use it as the final package writer until that is addressed.

Composition now computes a conservative local dependency closure across units, factories, stage pools and AI profiles. Imports preserve all declarations of a needed source Unity manifest, and reject destination bundle sets needing a combined manifest. External references, transactional file writing and CLI/batch remain unresolved; do not treat it as complete package composition.

Composition closure includes local AI profiles that reference a selected unit or its factory and their associated stages, even when the stage does not directly list the unit. Review the complete connected selection; external references and package publication still require resolution.

`extract-units` now has an experimental CLI/batch route that stages a new output directory, copies the complete source Unity bundle set, and validates schema and manifest before publication. External dependency resolution and concurrent path replacement protection remain open; do not treat this as completed composition. Import routing remains unimplemented.

`import-units` now has experimental CLI (`--source-pack`, `--unit-ids`) and batch (`sourcePath`, `unitIds`) routes using a newly staged output pack and copying complete compatible bundle sets. The Unity manifest reader loads bytes to release Windows file handles. External dependency resolution, manifest rebuilding for distinct bundle sets and concurrent path replacement protection remain unfinished; do not treat composition as complete.

Composition accepts repeated `--dependency-pack` arguments and rejects missing namespaced providers and duplicate IDs across supplied packs. The dependency analyzer now includes synergy targets, AI unitDependencies and entityId/unitId module parameters, and reports ambiguous providers. Native unnamespaced IDs are not verified against the game registry; reference coverage and safe publication remain incomplete.

`merge_bundle_manifests` can build a disjoint combined Unity manifest while preserving hashes and dependency edges. An isolated Unity 2021.3.45f2 Editor probe loaded the combined sample-native/tactical manifest and read both hashes and dependency lists (`artifacts/composition-manifest-study/unity-check.log`); this is local Editor evidence, not in-game acceptance. The merge is not yet wired into package composition; duplicate bundle names are rejected.

Composition now combines disjoint Unity manifests in the staging directory and copies both complete bundle sets; names duplicated across different manifests are rejected. A real sample-native plus tactical import passed schema and exact manifest-name checks. Dependency-bearing synthetic manifest edges were preserved in a local test; in-game acceptance and concurrent path safety remain open.

Composed pack publication now uses an atomic no-replace rename (Windows `os.rename`, Linux `renameat2` with `RENAME_NOREPLACE`); existing destinations are preserved even if they appear just before publication. Source path opening still requires race-resistant handles before the workflow is production-ready.

`tools.secure_pack_read.open_pack_file` opens pack manifests and source bundle files through non-reparse directory handles. On Windows directories are held without delete sharing during the read and the file without write/delete sharing; POSIX uses directory-relative `O_NOFOLLOW` handles. The source bundle transfer uses this helper. The isolated destination still needs a complete concurrency review.

The external dependency analyzer also tracks `units[].upgrades[].targetUnitId` and `upgrades[].mustNotHaveOneOfTheseEntityIds` at their actual schema locations.

Bundle-transfer rollback checks the original file identity and expected hash before removing completed copies; incomplete or changed files are retained and reported. This is not yet race-free: the check and unlink remain separate, and temporary-directory cleanup needs separate review.

The composed writer retains staging directories after success or failure instead of recursively deleting potentially changed files. On failure the diagnostic includes the staging path. Cleanup must be explicit after inspecting ownership and contents; atomic destination-path protection remains under review.

On bundle-copy failure, all newly created files and directories are retained and listed; rollback does not unlink by pathname because a concurrent replacement could occur after an identity check. Inspect the retained staging path before manual cleanup.

Bundle transfer creates destination files exclusively through `open_pack_file(..., create=True)`: Windows holds non-reparse parent directories without delete sharing and opens the file without sharing; POSIX uses directory-relative `O_NOFOLLOW|O_EXCL`. Parent directory creation, staging publication and cleanup still require their own concurrency review.

Staged bundle, manifest and content.json creation now uses exclusive guarded handles. Directory creation, existing-file comparisons, manifest source reads and publication root still require a complete concurrency audit.

Staged manifest reads and bundle comparisons use guarded pack-file handles; the manifest merger accepts bytes so it does not reopen a checked path. Directory creation and publication root stability remain under review.

Windows directory guards require `GENERIC_READ`: a zero-access CreateFile handle did not prevent directory renames despite excluding delete sharing. The guarded pack reader now uses read access. Output-root publication still needs its own handle-based integration.

The composed writer holds its output parent and ancestors through non-reparse directory handles during staging and publication. On Windows GENERIC_READ without delete sharing blocks renaming that parent. POSIX directory handles reject symlinks but publication still uses pathnames, so concurrent ancestor rename protection is not established there.

Composition creates each staging child while holding its parent directory and checks the child through a non-reparse handle before writing. Windows parent renames are blocked while held; POSIX path-based creation and publication still need race-resistant directory-relative operations.

Dependency analysis verifies that unit references resolve to unit definitions and stage AI profile references resolve to profiles; an ID defined only as another kind is reported unresolved. Generic entity references and native unnamespaced IDs still need their own registry-aware checks.

Typed dependency resolution treats `sample:scheduled-base-build` `entityId` as a production factory and card-change `onlyForTheseEntityIds`/`entityIdsNotAllowed` as unit references, matching loader validation. Other generic entity fields still require separate analysis.

After successful data-only composition the empty staging parent is removed with `rmdir`; when source copies remain, the writer reports the retained staging path as a warning. No recursive cleanup occurs.

`check_pack_dependencies` reports unnamespaced references encountered in typed unit/profile/factory fields under `unverifiedNativeReferences`; an empty unresolved list alone does not verify those IDs against the native registry.

Composition rejects duplicate `packId` values between the result and `--dependency-pack` inputs, even if their entity IDs differ. Dependency packs remain separate runtime requirements and must be installed alongside the composed result.

The composition CLI prints the transitive `packId` list that must be installed separately and warns about unverified native references in the composed pack. This report does not copy dependency packs.

Composed output includes `composition-dependencies.json` with required external pack IDs and unverified native references. This is an agent-facing report, not a loader-enforced dependency declaration; install the listed packs separately.

`composition-dependencies.json` includes `contentSha256`, calculated from the exact written `content.json` bytes. Compare it with the current file before relying on the report; this is provenance, not automatic enforcement or proof that dependency packs are installed.

A present `composition-dependencies.json` is checked by `tools.validate_content_pack` against the exact `content.json` SHA-256 and pack ID; malformed, stale or linked reports fail validation. This detects stale reports but does not verify that external packs are installed or authenticate the report.

Dependency analysis also reports native `copyUpgradesToEntityId` and upgrade/relic `mustNotHaveOneOfTheseEntityIds` as unverified entity references; namespaced values require a unit, production factory, or standalone building provider. These reports do not prove native IDs exist.

Composition carries referenced standalone buildings in its local closure, includes their bundle declarations during import and rejects conflicting destination definitions. Other reference families and destination race protection still require review.

Composition rejects overlapping selected unit, factory and standalone-building IDs, as well as factories shared by multiple selected units, before emitting a pack.

Composition selects relics and upgrades whose card changes explicitly target selected units or factories, closes over their referenced entities and bundles, and rejects conflicting card definitions. Filter-only card effects are included conservatively because native roles and side cannot be ruled out offline. Imported cards may also affect other destination entities; inspect their scope.

During composed-pack writing, the staging parent is held through a non-reparse handle until publication; on Windows a concurrent rename of that parent is blocked. The child-stage replacement race and POSIX pathname publication remain unaddressed; retained failed files still require inspection rather than automatic recursive rollback.

New `pack` and `source-N` staging directories are created exclusively; preexisting children cause failure and are preserved. This does not prevent replacement after creation.

Windows handle-based stage rename requires DELETE access. Current guarded directory opens omit FILE_SHARE_DELETE, so a long-lived DELETE-capable stage handle conflicts with subsequent guarded opens (WinError 32). Coordinate sharing on the exact guarded stage without relaxing ancestor protections before implementing handle publication; a standalone rename prototype is not integration evidence.

A scoped stage share-delete prototype passed isolated batch tests but failed batch output assertions when the complete composition module ran; it was removed. Reproduce against the whole module and inspect published paths and order-dependent state before reuse.
