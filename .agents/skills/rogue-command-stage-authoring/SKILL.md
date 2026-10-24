---
name: rogue-command-stage-authoring
description: "Design or review Rogue Command stage packs, progression parameters, blueprint pools, worlds, and map references."
---

# Rogue Command Stage Authoring

Use this skill for stage declarations, stage progression, world/map assets, and unit-pool eligibility. Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `content/schema/README.md` before changing a pack.

## Decide whether the work is a variant or a new stage experience

- `nativeTemplateId` clones a native `StageParametersScriptableObject` and applies bounded numeric and boolean overrides. It reuses the template's world and blueprint-pool references; describe it as a progression variant, not a new map.
- A custom stage requires a complete bundled stage asset with valid world, generator/map, and blueprint-pool references. Do not substitute a placeholder bundle when any serialized reference is missing.
- A stage can list `unitIds` and `aiProfileIds`; validate those references before any registry mutation. Blueprint eligibility is a global entity-row flag, and shared pools may make a unit selectable outside the stage that declared it.
- `blueprintPoolEntries` scopes mod products to explicitly named native pools. For a pool named by one or more entries, its mod factory allowlist is the union of those entries; it does not inherit products from `stage.unitIds`. Pools without entries retain the broader `stage.unitIds` eligibility. Product IDs in an entry need not be repeated in `stage.unitIds`. Generic rewards without a pool ID can use factories derived from either source. Native per-pool filters (`blueprintFilter.IsBlueprintValid`) are preserved. Verify pool IDs against the stage template before editing.
- Verified pool IDs by native template: see `references/native-blueprint-pools.md`. `StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit` exposes `BP_CheaperVehicles`, `BP_CheaperRobos`, and `BP_CheaperMechs`; `StageParameters_Pools_Level3` exposes only `BP_CheaperMechs` (lacks `BP_CheaperVehicles`).
- Runtime registration rejects the entire pack if a listed `poolId` is absent from that stage's native template, even when schema and offline harness validation pass. A missing native pool is not a loader failure; use a pool present in the template and probe registration again.

## Stage Parameter Binding Contract & Parameter Overrides

- **Exact Binding Contract (`StageParameterBindingContract`)**: Binds only `blueprintPoolList`, `fillFirstLevelWithCardRewards`, and `putDefaultCardRewardInCenter`.
- **Value-Type Invariant**: `cardReward` is a value type, not `OpenReward`. Do not attempt or infer bindings such as `cardReward.count` or `cardReward.weight` (strictly No-Go).
- **Supported Parameter Overrides**:
  - **Boolean Flags**: `fillFirstLevelWithCardRewards`, `putDefaultCardRewardInCenter`, `fillLastLevelWithShops`.
  - **Numeric & Weight Overrides**: `levelCount` (1–64), `randomSeed`, `startWithCardCount` (0–10), `twoWayBranchingProbability` (finite 0.0–1.0), and reward weights (`cardRewardWeight`, etc.).
- **Map Generation Range Contract**:
  - `startCountRange`: must be an ordered invariant-culture pair of integers 1–64, and its maximum must fit the final stage `maxWidth`.
  - `mapSizeMin` and `mapSizeMax`: define `Vector2Int` odd dimensions in range 1–256.

## Check game-specific generation constraints

- Use `StageGenerationCompatibility` and the offline validation harness. Its preflight follows the pinned game's AI map restrictions, selected World restrictions, progression map lists, active/demo-allowed Worlds, and the distinct default, Heat, and FirstRun profiles.
- Stage-scoped native AI aliases must point to contiguous same-pack stage indices. Do not hardcode indices based only on a sample's current position; earlier packs or native progression rows affect them.
- Procedural `landscape` variants clone a named native `LandscapeGenerator`, set its seed and odd map size, and localize its display name. The loader injects that generator into candidate lists only while the matching stage's `LevelProgression.CreateLevelMap` executes, restored safely by a Harmony finalizer even on exceptions.
- Keep bundle names, dependency declarations, and paths consistent with the schema and Unity builder. Keep generated bundles under the pack's declared `bundles/` directory.

## Evidence boundary

Static preflight and a valid Unity AssetBundle do not prove that a map is generated or playable. Label static inspection and local validation separately from a Steam-launched match. Do not change user saves or Cloud state for a match check; use an isolated, reversible profile and restore temporary installation changes.
