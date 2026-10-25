---
name: rogue-command-unit-authoring
description: "Author or review Rogue Command units, including balance variants, model overlays, card sprites, and localized names."
---

# Rogue Command Unit Authoring

Use this skill when adding or reviewing a unit in this repository. Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `content/schema/README.md` before changing a pack. The handoff is the current source of truth for research, commands, and evidence limits.

## Choose the unit construction path

- A `nativeTemplateId` unit copies a registered game balance row and clones the native prefab. Use `balanceOverrides` only for allowlisted numeric and enum fields. It keeps the native controller and gameplay components.
- Every added unit requires its own `productionBuilding` with a distinct namespaced ID, a native factory template, and an English name. Keep Spanish names in the localization array. The loader must clone and register a new factory row/prefab, set its `factoryForEntityId` to the new unit, and leave native mappings intact. Reward pools offer this new building's blueprint; stage manifests and AI rosters continue to refer to the produced unit. Verify building placement and actual production in Steam before claiming this path works.
- A native visual overlay can set `visualAssetName` and `visualRootPath` with an AssetBundle. The root path must resolve to one native renderer subtree. The loader replaces those renderers and retains the native gameplay hierarchy; it does not retarget rigs or animations.
- Before installing a native visual overlay, run `.venv\Scripts\python.exe tools/validate_native_visual_roots.py <pack-directory>` against the pinned game. Do not copy a visual path between different native templates: ArtilleryTank uses `Tank`, GatlingWalker uses `RoboMarine_Mesh`, and LightningTank uses `PCX TankHolder` on the currently verified asset identity. This static check does not prove live resource selection or rendering.
- A custom-prefab unit needs a complete bundled balance asset, prefab, and card Sprite with the game-required serialized components and references. The Unity Editor's inability to attach the precompiled game scripts is an authoring constraint; fail closed when a complete prefab cannot be produced.
- AssetBundles hold serialized Unity assets, not new executable `MonoBehaviour` code. Put new code in the compiled loader only when the task requires it.

## Balance Overrides, Enums, and Precedence Contract

- **25 Verified Per-Unit Row Mappings**: `customBalance` and `balanceOverrides` map 25 verified per-unit `EntityBalancingStore.ChangeableValue` keys to `EntityBalancingParameters` row fields (`MaxHealth`, `MoveSpeed`, `Damage1`, `Attack1Cooldown`, `Damage2`, `Attack2Cooldown`, `MaxArmor`, `ArmorProtection`, `Cost`, `BuildTime`, `SightRadius`, etc.); global-only `ManaRechargePerSecond` is excluded.
- **Strict Precedence**: `customBalance` > raw `balanceOverrides` > positive finite `movement.moveSpeed`.
- **Row-Based Commit Invariant**: Mutate copied native value rows before registration, and commit to provider caches only after successful registration; never seed final-value caches directly so native cache invalidation and `CardChangeScriptableObject` modifiers compose cleanly.
- **Numeric Bounds**: Integer-backed values are capped at `16,777,216` (due to native `CalculateResultingIntValue` widening row `Int32` to `Single`). Float-backed values are capped at `Single.MaxValue`. Values must be finite and nonnegative.
- **Enum Overrides**: Balance overrides support enums via `Enum.Parse`:
  - `rarity`: `Common`, `Rare`, `UltraRare`.
  - `attackType`: `None`, `Single`, `AoE`, `Multi`, `MultiAoE`, `Beam`, `Artillery`, `MultiArtillery`.
  - `skillType`: `None`, `Toggle`, `Location`, `LocationAoE`, `LocationMultiTargets`, `Self`, `SelfAoE`, `Target`, `TargetMultiTarget`, `TargetAoE`, `SelfMultiTarget`.

## Declarative Loadout Variants (`units[].variants`)

- Units may optionally declare up to 8 loadout variants (e.g. AP / HE munitions, scout vs siege gear) under `units[].variants`.
- Each variant specifies a unique `id`, optional `name`, `description`, and up to 64 `balanceOverrides`.
- **Identifier Validation**: Variant IDs must be valid identifiers and strictly reject line terminators (`\r`, `\n`, CRLF, U+2028, U+2029).
- **Metadata-Only Invariant**: Variants are currently declarative metadata only. They are not applied to native balance rows, runtime unit instances, AI, visuals, or VFX. Loader diagnostics report them under `Variantes declaradas (solo metadatos): N`.

## Structured Fields & Graph Validations

- **22+ Sub-Definitions**: `ContentUnitDefinition` features 22+ schema-backed sub-definitions with explicit null-safe deep cloning (`Clone*`) and validation (`Validate*`). Absent a verified native or runtime consumer, unverified structured fields (weapons, armor, healing, stealth, aura, gathering) remain descriptive.
- **Descriptive Armor Contract**: Native `MaxArmor` (armor pool) and `ArmorProtection` (flat damage subtraction) have distinct runtime consumers. Require finite nonnegative armor/shield values and finite `damageReduction` in `[0, 1]`.
- **Upgrade & Synergy Graphs**: Iterative graph analysis enforces a 5-edge traversal limit, cycle detection, forward references, and convergent DAG handling.
- **Production Queue Feasibility**: Feasibility mismatches are reported as warning diagnostics and do not invalidate otherwise valid content packs.

## Identity, text, and eligibility

- Use a namespaced unit ID and provide an `en-US` display name for custom-prefab units. Pack translations use the exact language IDs in `content/schema/README.md`; runtime resolution tries the active language, then English, then native-template text, and finally a safe ID-derived name.
- **Card initialization ordering**: when a hook or loader injects a modded unit card into a reward pool or menu list, register the entity row with `EntityBalancingStore` before the card's `Init` runs. Native `CardNew.Init` queries entity parameters immediately; if the entity ID is still unknown, the card falls back to the error panel instead of rendering. The safe sequence is: resolve content, register rows, then inject cards.
- **Reflection type resolution**: do not resolve game types by simple name when the assembly contains duplicates. `Assembly.GetType("Game")` can fail with ambiguity because `Game` and `Shapes2D.Game` coexist; use the exact type name with `GetType("Game", false, false)` or another unambiguous qualifier.
- Use `cardImageAssetName` for a bundle Sprite when the unit needs its own card image. Enumerate the bundle with UnityPy (`env.container.keys()`) first, and add the matching `assetBundleId` + `cardImageAssetName` to every production building whose card PNG exists; missing card metadata is a common consistency error. The current Sandlance sample demonstrates a model overlay, icon, balance overrides, and localization; it still inherits `PCXBaseTank` gameplay.
- Keep `productionBuilding` metadata paired: `cardImageAssetName` requires `assetBundleId`, and `visualAssetName` requires `visualRootPath`; set both fields or leave both unset. Enumerate the bundle with UnityPy (`env.container.keys()`) before assigning asset names so every referenced card/visual exists.
- Set unit drafting and roster flags explicitly:
  - `isAllowedAsStartingBlueprint`: `true` to offer unit during starting deck drafting in Stage 1.
  - `isAllowedForAi`: `true` for combat units, `false` for factories/HQs; required for AI rosters.
  - `isAllowedForDemo`: `true` for demo run eligibility.
  - `isForSpecialists`: `true` for specialist archetype pools.
- When scoping products with `blueprintPoolEntries`, a pool named by one or more entries admits only the union of those entries' products; it does not inherit `stage.unitIds`. Pools without entries retain the broader `stage.unitIds` eligibility. Product IDs in an entry need not be repeated in `stage.unitIds`; generic rewards without a pool ID can use factories from either source. Native filters remain in force.
- Native visual roots must point to a renderer-containing subtree, not necessarily the template root. Run `.venv\Scripts\python.exe tools/validate_native_visual_roots.py <pack-directory>` and use the reported child (`Vehicle`, `Tank`, `RoboMarine_Mesh`, `GameObject/BombBike`, `PCX TankHolder`); a root with zero renderers fails validation. Do not copy a visual root between different native templates.
- A stage's `unitIds` names produced units. The loader translates those IDs to their own factory IDs for blueprint eligibility and cloned pool scopes; product rows stay ineligible as blueprint rewards. Cloned stage blueprint pools preserve native per-pool filters (`blueprintFilter.IsBlueprintValid`). A pool named in `blueprintPoolEntries` admits only the union of that pool's entries; only pools without entries retain the broader `stage.unitIds` eligibility. Check generic rewards without a pool ID separately because they use the game's global eligibility fallback.
- Before relying on reflected game methods, recheck the compatibility identity as a whole: Rogue Command version, `Assembly-CSharp.dll` SHA-256, and MVID. `EntityController.InProductionCount` returns `Nullable<int>` (`int?`, token `0x060007df`), while `EntityController.EntityId` is PascalCase (token `0x060007c6`) and AI/production structures use camelCase `entityId`. Unity/bundle authoring must use the pinned Editor version documented in the handoff.

## Verify honestly

Run the repository's offline validator and Release build when implementation or acceptance criteria call for them. Build the Unity bundle with the repository scaffolder and reopen it in Unity; offline bundle inspection can confirm object names and types. Label these as local validation. Only a Steam-launched match can confirm registration, pool selection, instantiation, and rendering. Use a reversible profile and restore temporary deployment changes after an authorized runtime check.
