---
name: rogue-command-pack-validation
description: "Validate Rogue Command content manifests, AssetBundles, loader builds, and install packages before delivery."
version: 1.2.0
license: MIT
author: Hermes Agent
metadata:
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
| **Python Registry & Balance** | `python -B -m unittest discover -s tests -v` | **479 passed / 1 skipped** out of 480 checks |
| **AssetBundle Audit** | `python tools/audit_unity_bundle.py <bundle>` | Inspects object counts, texture formats, shaders, dependencies, and byte size |
| **Pack Manifest Validator** | `python tools/validate_content_pack.py <pack_dir>` | Validates `content.json` against schema, checks referenced assets & duplicate IDs |
| **Packaging Runner** | `powershell -File tools/Package-Loader.ps1` | Creates release ZIP without test assemblies, PDBs, or editor caches |

---

## 2. Manifest Schema & Parameter Rules

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
