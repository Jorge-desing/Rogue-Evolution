---
name: rogue-command-pack-validation
description: "Validate Rogue Command content manifests, AssetBundles, loader builds, and install packages before delivery."
---

# Rogue Command Pack Validation

Use this skill when validating or packaging a Rogue Command loader/content change. Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `content/schema/README.md` for current commands and constraints.

## Validate the source and content

- Parse `content/schema/content.schema.json` and validate each changed `content.json` against Draft 2020-12 schema rules.
- Run both validation test suites when tests are part of the accepted task:
  - C# loader harness: `dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .` (report a skipped symlink check as skipped, not passed).
  - The C# harness compiles a **curated `Compile` file list against a minimal `UnityEngine` stub**, not the whole loader. Grep the test csproj `Compile` list before adding a type to a file the harness compiles: a `MonoBehaviour` or any type referencing live Unity authoring/runtime types (`RuntimeOverlayUi`, `RangeDisplayRuntime`, `GUILayout`, `LineRenderer`, ...) fails to compile there. Keep agent-readable/pure logic (JSON formatters, color policies, snapshot DTOs) in a dependency-free file the csproj registers, and keep the Unity-coupled bridge `MonoBehaviour` in a separate file the plugin compiles but the harness does not. A hand-rolled JSON writer beats a Newtonsoft dependency when the formatter must also live in the stub-only test project.
  - Python registry & balance test suite: `python -B -m unittest discover -s tests -v`.
- **Unit Variant Schema Validation**:
  - `units[].variants` permits up to 8 loadout variants.
  - Each variant allows up to 64 `balanceOverrides`.
  - Variant IDs must be valid identifiers and strictly reject line terminators (`\r`, `\n`, CRLF, U+2028, U+2029).
  - Loader diagnostics report them under `Variantes declaradas (solo metadatos): N`.
- **Numeric Bounds & Precedence Validation**:
  - Integer overrides capped at `16,777,216`; float overrides capped at `Single.MaxValue`.
  - Balance precedence: `customBalance` > raw `balanceOverrides` > positive finite `movement.moveSpeed`.
  - 25 verified per-unit `ChangeableValue` keys mapped directly to row fields, excluding global `ManaRechargePerSecond`.
- **Structured Parameter Type Safety**:
  - When applying structured unit parameters, inspect the destination row field type before assignment. Passing a `float` to an `int`-backed row field (for example `EntityBalancingParameters.skillRange`) throws an unhandled `ArgumentException` that aborts pack loading. Detect the target type and round/convert explicitly.
- **Skill Description Inheritance Guards**:
  - `SkillDescriptionPrefix` and `TryResolveUnitLocalization` can throw on namespaced mod IDs because native `Loca` keys do not contain namespace prefixes. Guard these lookups so a missing native translation degrades to the mod's own description or a safe default instead of aborting initialization.
- **Upgrade & Synergy Graph Analysis**:
  - Validates upgrade and synergy graphs iteratively with a 5-edge traversal limit, cycle detection, forward references, and convergent DAG handling.
  - Production queue feasibility mismatches are reported as warnings without invalidating otherwise valid packs.
- **Stage Parameter & Node Interval Validation**:
  - `StageParameterBindingContract` binds only `blueprintPoolList`, `fillFirstLevelWithCardRewards`, and `putDefaultCardRewardInCenter`. `cardReward` is a value type, not `OpenReward` (reject `cardReward.count/weight`).
  - `startCountRange` must be an ordered invariant-culture integer pair 1–64, bounded by final stage `maxWidth`.
- **Registry Hardening**:
  - Rejects duplicate unit, factory, and pool IDs both intra-pack and cross-pack.
  - Excludes ambiguous cross-pack mappings and safely reports invalid UTF-8 manifests without crashing.
- Schema enum and C# allowlist must agree: if `content.schema.json` advertises an `activeSkill.skillType` (or other enum) that `ContentPackValidation.cs` rejects (or the runtime does not handle), content authors can pass schema validation but fail at load. When adding or renaming a skill type, update both the schema enum and the C# allowlist in the same change, and confirm the runtime actually dispatches the new type. Conversely, when a type is in the schema but unsupported at runtime, remove it from the schema enum rather than widening the allowlist without runtime support.
- Schema and offline harness validation do not prove that a `blueprintPoolEntries` pool ID exists in the native stage template. A missing native pool disables the whole pack during runtime registration, so verify pool IDs with generated stage references or a live BepInEx MCP registration probe before delivery.
- Native stage templates differ in available pools: `StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit` supports `BP_CheaperVehicles`, `BP_CheaperRobos`, and `BP_CheaperMechs`, while the tested `StageParameters_Pools_Level3` template supports only `BP_CheaperMechs`. Treat any pool ID as invalid until a runtime registration probe or generated stage reference confirms it.
- Build the loader in Release. For Unity assets, use the repository's Unity 2021.3.45f2 authoring project and builder, then reopen the generated AssetBundle in Unity. Verify that bundles embed their own compiled shaders (`VehiclePBR_VertexWear` or `Standard`) for URP execution, since native game assets do not provide built-in shaders. UnityPy can independently list serialized objects offline, but cannot establish runtime compatibility.
- Before assigning card or visual asset names, enumerate the bundle with UnityPy (`env.container.keys()`) and confirm each name exists; validate `productionBuilding` card/visual pairs against the schema and run `tools/validate_native_visual_roots.py` for every unit visual root. Report visual-root checks as local static evidence, not rendering proof.
- Temporary Unity Editor probe scripts used to verify AssetBundle addresses or asset resolution are validation aids, not permanent authoring artifacts. Remove both the `.cs` and `.meta` files from `Assets/Editor/` after verification; do not ship them in source control or leave them in the authoring project.
- In diagnostic trace and telemetry formatting, ensure bounded outputs (e.g. `MaxTraceCharacters = 4096`) place omission/loss counters before unbounded-detail collections (`offeredProducts`, candidates, pools) so tail truncation never hides lost observations.
- When editing `content.json` programmatically, preserve the file's original line endings and indentation and use targeted text insertion; full JSON re-serialization can rewrite the whole manifest, change every line, and create huge noisy diffs. Verify with `git diff --stat` before committing.
- Rebuild the install archive with `tools/Package-Loader.ps1`. Inspect its actual entries: confirm the intended plugin, manifests, bundles, and readmes are present, and that game assemblies, BepInEx, Harmony, PDBs, and Unity editor output are absent.

## Deploy a built package to a local game installation

- Confirm no game process is running before writing into `BepInEx/`.
- Back up the existing plugin DLL and content directory before overwriting them.
- Deploy the package by extracting it into the game root, preserving `BepInEx/plugins/...`.
- The plugin DLL must end at `BepInEx/plugins/RogueCommandExpansionLoader.dll`; do not leave a stale duplicate under `BepInEx/plugins/RogueCommandExpansionLoader/RogueCommandExpansionLoader.dll`.
- `tools/Package-Loader.ps1` packages only `sample-native-expansion` and `sample-reactive-ai`. Compare its entries with the intended tracked packs; if an intended pack such as `tactical-operations` is omitted, deploy that pack explicitly from the repository and verify its files or hashes.
- Record profile-file hashes before and after deployment and verify they are unchanged.
- Deployment verification (ZIP integrity, plugin hash, content files, unchanged profiles) is local installation evidence only. It is not proof of runtime registration, gameplay, or a fix for profile persistence.

## Protect the repository and runtime

- Keep generated build output under ignored `artifacts/`. Inspect `.gitignore` and `git status`; stage only task-owned source, documentation, and distributable bundle inputs. Do not stage Unity `Library/`, generated editor caches, local game assemblies, or user workspace metadata.
- Do not install the package into the game as part of offline validation. When runtime validation is explicitly part of the task, use Steam and a reversible test profile, then restore temporary deployment changes.
- For a requested commit/push, review the staged names and diff, fetch and compare the current tracking remote branch (`codex/rogue-unit-balance-20261018`) immediately before pushing, and verify the remote SHA afterward. Do not create a pull request unless requested.

## Report what each check proves

Separate static inspection, local build/validation, and in-game evidence. Schema validity, unit tests, bundle reopening, and ZIP inspection are local evidence; they do not prove content registration or playability in Rogue Command.
