# Plan: Rogue Command Modding Opportunities — Unit Loader, Stages, AI

## Goal
Extend the Rogue Command expansion loader with additional modding capabilities for unit definitions, stage parameters, and AI modules beyond what is currently implemented.

## Current Context / Assumptions
- Repository: `C:/Users/Alex/Documents/Mod Rogue Command`
- Branch: `codex/rogue-unit-balance-20261018` (worktree is intentionally dirty; preserve unrelated changes and stage only task-owned paths).
- Existing `ContentUnitDefinition` DTOs and deep clones cover more fields than the game's native balance enum. DTO/schema acceptance alone does not imply runtime gameplay behavior.
- Only map fields with verified native semantics through the explicit `RuntimeContentMutator.ParamMap`; do not pass arbitrary DTO names to reflection.
- The game's `StageParametersScriptableObject.startCountRange` is a native `Vector2Int`; a dedicated range parser/application path is required because generic scalar overrides cannot handle structs. Require ordered integers 1–64 and cap the maximum by final `maxWidth`.
- `maxAscensionLevel`, `aiBuildDistance`, `cardRewardCount`, and `shopCardCount` were not found on the verified stage type. `relicRewardWeight` is a real native field already covered by scalar overrides.
- Public research (search-result snippets; page extraction was unavailable in this session): the Rogue Command developer's July 2025 update announcement describes editing units/buildings and XP unlocks; Nexus lists an existing BepInEx unit-stat mod. This supports unit customization as an opportunity but does not establish stage or AI APIs or implementation behavior. Sources: https://www.reddit.com/r/RealTimeStrategy/comments/1lt0esp/we_just_released_our_10th_big_early_access_update/ and https://www.nexusmods.com/roguecommand/mods/1 .
- Current local checks after agent review: Release plugin and Release test-project builds passed with 0 warnings/errors; Release harness passed 175/176, with one Windows symlink-privilege skip. Draft 2020-12 validation of a representative unit manifest reported 0 errors and rejected an unknown nested field. No in-game run was performed.
- Game compatibility: Rogue Command 1.0.9, Unity 2021.3.45f2, Assembly-CSharp.dll SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`.

## Architecture / Proposed Approach
Use three vertical slices: schema-aligned unit DTOs with only verified native balance mappings, a dedicated `startCountRange` struct override, and compiled AI modules selected by bounded profile parameters. Keep static inspection, local build/validation, and in-game evidence separate.

## Step-by-Step Tasks

### Task 1: Keep unit schema aligned and map one verified balance value
**Files:** `content/schema/content.schema.json`, `content/schema/README.md`, `src/RogueCommandExpansionLoader/Content/ContentPackDefinition.cs`, `src/RogueCommandExpansionLoader/Content/ContentPackValidation.cs`, `src/RogueCommandExpansionLoader/Content/ContentPackRuntimeLoader.cs`, `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs`.
**Scope:** Schema-back all existing unit DTO fields. Map only positive finite `movement.moveSpeed` to native `MoveSpeed` through the balance cache. Preserve `customBalance` values and suppress the derived value when explicit `customBalance.MoveSpeed` or raw `balanceOverrides` `moveSpeed` exists. Reject non-finite movement values. Other DTOs (including weapons, healing, stealth, AI behavior, and visual variants) remain descriptive/validated until their native semantics and runtime consumers are verified.
**Verification:** Test schema coverage, mapping/preference order, and invalid floats. Do not invent ChangeableValue enum members or reflect arbitrary DTO names.

### Task 2: Add native stage start-count range override
**Files:**
- `src/RogueCommandExpansionLoader/Content/ContentPackValidation.cs` (`TryValidateStageParameterOverrideValue`)
- `src/RogueCommandExpansionLoader/Content/ContentPackRuntimeLoader.cs` (stage override application)
- `content/schema/content.schema.json`
**Scope:** Support the verified native `startCountRange` `Vector2Int` using a dedicated invariant-culture `"minimum,maximum"` parser. Both integers must be ordered and in `[1,64]`; at runtime also require `maximum <= final maxWidth`. Keep the established scalar allowlist unchanged. Do not add `maxAscensionLevel`, `aiBuildDistance`, `cardRewardCount`, or `shopCardCount` as fake fields; `relicRewardWeight` is already a valid existing scalar.
**Verification:** Tests reject malformed, fractional, reversed, out-of-bounds, and too-wide ranges; test a valid range through the runtime parser/application path.

### Task 3: Add New Compiled AI Module — "sample:patrol-and-intercept"
**Files:**
- New: `src/RogueCommandExpansionLoader/Modules/PatrolAndInterceptAiModule.cs`
- New: `src/RogueCommandExpansionLoader/Modules/PatrolAndInterceptParameters.cs`
- Modify: `src/RogueCommandExpansionLoader/RogueCommandExpansionPlugin.cs` (register)
- Tests: `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs`
**Scope:** Module parameters: `baseIndex` (0–31), `patrolRadiusCells` (5–32, default 15), `interceptRadiusCells` (5–32, default 10), `scanIntervalSeconds` (2–60, default 8), and `maxPatrolUnits` (1–8, default 3). Interception reuses the existing reactive-defense pattern. Waypoints, if retained, must use live AI-building positions and bounded `AttackMoveOnPosition` dispatch; the existing API does not prove safe repeated patrol semantics.
**Verification:** Parser defaults/bounds/unknown/duplicate tests pass; local registration/build checks pass. Do not claim in-game patrol behavior without Steam evidence.

### Task 4: Validate complete contracts
**Files:** `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs`, `content/schema/content.schema.json`.
**Scope:** Cover schema fields, safe unit mapping, stage range, and AI parser behavior. Include:
- Null/empty parameters → defaults applied
- Boundary values (min/max) accepted
- Out-of-bounds rejected with diagnostic
- Unknown parameter rejected
- Duplicate parameter rejected
- Null input array handled
**Pattern:** Mirror existing `siege-assault AI parameter parser` tests.

### Task 5: Run full validation and prepare delivery
**Commands:**
```bash
dotnet build src/RogueCommandExpansionLoader/RogueCommandExpansionLoader.csproj -c Release -v:minimal
dotnet build -c Release --artifacts-path artifacts/validation-tests tests/RogueCommandExpansionLoader.ValidationTests/RogueCommandExpansionLoader.ValidationTests.csproj
dotnet exec artifacts/validation-tests/bin/RogueCommandExpansionLoader.ValidationTests/release/RogueCommandExpansionLoader.ValidationTests.dll .
```
**Expected:** 0 warnings/errors; report actual test PASS/FAIL/SKIP counts. Stage only reviewed task-owned files; verify upstream/remote before push and verify the pushed SHA.

## Tests / Validation (TDD Cycle per Task)
Each task follows RED→GREEN→REFACTOR:
1. **Write failing test** in `Program.cs` (or new test file) for the new behavior.
2. **Run test** → confirm failure (RED).
3. **Implement minimal code** in target file(s) to pass.
4. **Run test** → confirm pass (GREEN).
5. **Run full harness** → no regressions.
6. **Refactor** if needed (keep tests green).
7. **Commit** task-owned changes.

## Risks, Tradeoffs, Open Questions
- **Risk**: DTO fields such as `weapons.damage`, `armor.armorValue`, and `resourceGathering.gatherRate` look like balance values but are not proven direct native fields. Leave unmapped until verified and tested.
- **Risk**: `startCountRange` is a struct field, so parser, clone, runtime application, and `maxWidth` validation need to remain consistent.
- **Risk**: `AttackMoveOnPosition` and `AiBuildings()` are present in adjacent code, but they do not establish safe repeating patrol behavior. Preserve a static-only evidence label until a Steam-launched test observes dispatch and route behavior.
- **Open Question**: Which remaining unit DTOs warrant a future specialized runtime consumer outside the native balance enum (e.g. visual variants, effects, aura, gathering)?

## Deliverable
Markdown plan at `.hermes/plans/2026-10-20_164500-rogue-modding-opportunities.md`.