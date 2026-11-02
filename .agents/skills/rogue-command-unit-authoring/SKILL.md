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

- **Scout `weaponRange` floor**: Modded scouts must set `weaponRange` >= 5.5 to avoid INFINITE_KITING_ENVELOPE against slower/shorter-ranged enemies (native scout baseline is 5.5; values below 5.0 let the enemy close and trade). Verify with `--matchup <scout> <native-template>` and check the `INFINITE_KITING_ENVELOPE` flag.

## Declarative Loadout Variants (`units[].variants`)

- Units may optionally declare up to 8 loadout variants (e.g. AP / HE munitions, scout vs siege gear) under `units[].variants`.
- Each variant specifies a unique `id`, optional `name`, `description`, and up to 64 `balanceOverrides`.
- **Identifier Validation**: Variant IDs must be valid identifiers and strictly reject line terminators (`\r`, `\n`, CRLF, U+2028, U+2029).
- **Runtime Selection Invariant**: The loader registers validated variants in `RuntimeUnitVariantRegistry`. `diagnostics_order_apply_unit_loadout` selects a declared variant by ID on a live unit; AP/HE presets are applied first, supported instance stats (`damage1`, `attackCooldown`, `moveSpeed`, `weaponRange`, `maxHealth`, `maxArmor`, `armorProtection`, `maxShield`, `healAmount1`, `sightRadius`, `effectRadius1`, and `skillRange`) are converted from absolute values to native relative modifiers, and stale modifiers are replaced on switching. `Standard` resets the active variant. Unsupported row-only fields fail closed. This path does not claim natural card selection, AI selection, visual/VFX retargeting, or rendering.

## Structured Fields & Graph Validations

- **22+ Sub-Definitions**: `ContentUnitDefinition` features 22+ schema-backed sub-definitions with explicit null-safe deep cloning (`Clone*`) and validation (`Validate*`). Absent a verified native or runtime consumer, unverified structured fields (weapons, armor, healing, stealth, aura, gathering) remain descriptive.
- **Descriptive Armor Contract**: Native `MaxArmor` (armor pool) and `ArmorProtection` (flat damage subtraction) have distinct runtime consumers. Require finite nonnegative armor/shield values and finite `damageReduction` in `[0, 1]`.
- **`MaxArmor` is inert for damage mitigation**: Verified in decompiled `EntityController.TakeDamage` — only `ArmorProtection` (flat subtraction, floor 0.1) and discrete shield charges reduce incoming damage. `MaxArmor`/`CurrentArmor` are a repair pool + visual bar only. Set `maxArmor` to 0 on units without a repair mechanic; raising `armorProtection` to 2+ creates an ARMOR_LOCKOUT against low-damage units (e.g. `damage1=2` → 0.1). Do not model `maxArmor` as EHP in balance calculations.
- **Upgrade & Synergy Graphs**: Iterative graph analysis enforces a 5-edge traversal limit, cycle detection, forward references, and convergent DAG handling.
- **Production Queue Feasibility**: Feasibility mismatches are reported as warning diagnostics and do not invalidate otherwise valid content packs.

- **Runtime mechanics fields**: `ContentUnitDefinition` supports runtime mechanics fields `shieldRegeneration`, `vampiricLeech`, `tauntPulse`, and `shieldOverloadNova`. When adding or changing one, update all four layers together: schema (`content/schema/content.schema.json`), DTO/deep clone (`ContentPackDefinition` + `ContentPackCopy.Clone*`), validation (`ContentPackValidation.Validate*`), and runtime mapping (`ContentPackRuntimeLoader.RegisterEntityMechanicsProfile` -> `UnitRuntimeMechanicsProfile`). Add corresponding schema and runtime test coverage; do not leave a field wired in only one layer.
- **Runtime mechanics field validation rules** (schema-enforced, learned the hard way):
  - `experienceReward.experienceAmount` must be an **integer** (schema type `integer`); floats like `3.5` fail validation with `is not of type 'integer'`.
  - `vampiricLeech.siphonRatio` must be **> 0 (exclusive)** and ≤ 1.0. For disposable units (e.g. suicide bikes) that must not have leech, set `vampiricLeech` to `null` rather than `siphonRatio: 0` — a zero ratio is rejected.
  - `healing.healAmount` / `repairAmount` and `vampiricLeech.maxSiphonPerHit` are float-backed; `levelUpBonus` is float; `maxLevel` / `experienceAmount` are int-backed.
  - Mechanics must be **cost-scaled and role-aware**, not uniform: tier by cost (T0 ≤80, T1 ≤260, T2 >260) and role (support leans on sustain with lower raw damage scaling; suicide units get no sustain). Uniform application makes cheap units disproportionately powerful.
  - **Augmentation set in play + ChangeableValue mapping** (all wired through `RegisterEntityMechanicsProfile` → `UnitRuntimeMechanicsProfile` → `RuntimeUnitMechanicsDirector`): `healing.healAmount`→MaxHealth, `healing.repairAmount`→MaxArmor, `combatBoosts[].statName` (Damage1)→stat, `experience.levelUpBonus` (rank-up)→MaxHealth, `vampiricLeech.siphonRatio`→Damage1, `experienceReward.experienceAmount` (bounty)→Damage1.
  - **`combatBoosts` apply twice** (timed buff + persistent `CardChangeScriptableObject` modifier), so treat `multiplier` as permanent damage scaling, not transient; `combatBoosts[].duration` is role-aware (melee/support longer, sniper shorter).
  - **Snowball control**: flat-damage `experienceReward` per kill compounds on high-kill bruisers — cap it (×0.6) for `MassDestruction`/`FrontalAttacker` and high-damage snipers; cap rank-up total HP (`levelUpBonus × maxLevel` ≈ 30% T2); scale leech by melee involvement. See the `rogue-command-balance-simulation` skill for the concrete T0/T1/T2 × role budget table applied to the sample units.
- **Runtime director lifecycle reversion**: When a runtime director applies persistent effects to an entity (baseline status effects, command links via `SetCaptain`), `ClearRuntimeMechanics`/`ClearRuntimeState` must revert those effects — not just transient timers. Track applied baseline effects per combat-experience key (`GetCombatExperienceKey`) in a static dictionary; on reuse, revert them via `RemoveStatusEffect` before applying the new profile. For command links, iterate active entities and call `RemoveCaptain` on any follower whose `CaptainOfThisEntity` matches the reused entity. Combat XP is cleared only by the destruction hook (`OnEntityDestroyed`), never on attachment — a live entity may be reattached while retaining valid accumulated XP. XP accumulation and cleanup must share the same key resolver (`UniqueEntityId` when nonnegative, otherwise `GetHashCode()`). Verify with a mutation test: removing cleanup must leak XP into a later instance with the same ID.
- **Field-declaration integrity in large runtime files**: `RuntimeUnitMechanicsDirector.cs` uses many reflected `MethodInfo`/`PropertyInfo` fields. A patch that removes a declaration (e.g., `_isEnemyMethod`) while intending to remove only a usage line causes cascading CS0103 errors across all call sites. After any patch to a file with many static fields, run a build immediately to catch missing declarations before they compound.
- **Deep clone gate**: `ContentPackDefinition` exposes `Clone()` (delegating to `ContentPackCopy.DeepClone`). Any new structured sub-definition must be cloned in `CloneUnit`; tests verify deep independence by mutating the clone and asserting the original is unchanged.
- **JSON formatting preservation**: when modifying `content.json` programmatically, preserve each file's original indentation so diffs stay minimal. `sample-native-expansion/content.json` uses 2-space indentation; `tactical-operations/content.json` uses 4-space indentation. Re-serializing the whole file with a different indent produces a massive diff and breaks review. Prefer per-unit string replacements (anchored on `"id": "<unit-id>"` through the next unit/section) over full re-serialization; if you must re-serialize, match the file's existing indent exactly.

- Use a namespaced unit ID and provide an `en-US` display name for custom-prefab units. Pack translations use the exact language IDs in `content/schema/README.md`; runtime resolution tries the active language, then English, then native-template text, and finally a safe ID-derived name.
- **Card initialization ordering**: when a hook or loader injects a modded unit card into a reward pool or menu list, register the entity row with `EntityBalancingStore` before the card's `Init` runs. Native `CardNew.Init` queries entity parameters immediately; if the entity ID is still unknown, the card falls back to the error panel instead of rendering. The safe sequence is: resolve content, register rows, then inject cards.
- **Reflection type resolution**: do not resolve game types by simple name when the assembly contains duplicates. `Assembly.GetType("Game")` can fail with ambiguity because `Game` and `Shapes2D.Game` coexist; use the exact type name with `GetType("Game", false, false)` or another unambiguous qualifier.
- Use `cardImageAssetName` for a bundle Sprite when the unit needs its own card image. Enumerate the bundle with UnityPy (`env.container.keys()`) first, and add the matching `assetBundleId` + `cardImageAssetName` to every production building whose card PNG exists; missing card metadata is a common consistency error. The current Sandlance sample demonstrates a model overlay, icon, balance overrides, and localization; it still inherits `PCXBaseTank` gameplay.
- **Card-image diagnostic shortcut:** when one unit shows a wrong or placeholder card while sibling units render correctly, first compare `cardImageAssetName` in the manifest. If the affected unit has `null` while others reference existing Sprites, the missing metadata is the likely cause before inspecting renderers or runtime card code.
- Keep `productionBuilding` metadata paired: `cardImageAssetName` requires `assetBundleId`, and `visualAssetName` requires `visualRootPath`; set both fields or leave both unset. Enumerate the bundle with UnityPy (`env.container.keys()`) before assigning asset names so every referenced card/visual exists.
- Set unit drafting and roster flags explicitly:
  - `isAllowedAsStartingBlueprint`: `true` to offer unit during starting deck drafting in Stage 1.
  - `isAllowedForAi`: `true` for combat units, `false` for factories/HQs; required for AI rosters.
  - `isAllowedForDemo`: `true` for demo run eligibility.
  - `isForSpecialists`: `true` for specialist archetype pools.
- When scoping products with `blueprintPoolEntries`, a pool named by one or more entries admits only the union of those entries' products; it does not inherit `stage.unitIds`. Pools without entries retain the broader `stage.unitIds` eligibility. Product IDs in an entry need not be repeated in `stage.unitIds`; generic rewards without a pool ID can use factories from either source. Native filters remain in force.
- Native visual roots must point to a renderer-containing subtree, not necessarily the template root. Run `.venv\Scripts\python.exe tools/validate_native_visual_roots.py <pack-directory>` and use the reported child (`Vehicle`, `Tank`, `RoboMarine_Mesh`, `GameObject/BombBike`, `PCX TankHolder`); a root with zero renderers fails validation. Do not copy a visual root between different native templates.
- **Orientation proof limits:** static inspection of Blender axes, FBX `FrontAxis`, prefab transform yaw, and generated sockets cannot prove which way a unit walks at runtime. Only an in-game observation can confirm forward/backward movement. If the user reports reversed walking, record the authored forward axis first, then validate in a live match before rotating models or changing yaw-correction heuristics. When an offline yaw fix is warranted, use the dominant-axis rule in `rogue-command-mech-rigging-authoring`: compare `|Y|` and `|Z|` directly, do not require `|Y| > 2 * |Z|`.
- A stage's `unitIds` names produced units. The loader translates those IDs to their own factory IDs for blueprint eligibility and cloned pool scopes; product rows stay ineligible as blueprint rewards. Cloned stage blueprint pools preserve native per-pool filters (`blueprintFilter.IsBlueprintValid`). A pool named in `blueprintPoolEntries` admits only the union of that pool's entries; only pools without entries retain the broader `stage.unitIds` eligibility. Check generic rewards without a pool ID separately because they use the game's global eligibility fallback.
- Before relying on reflected game methods, recheck the compatibility identity as a whole: Rogue Command version, `Assembly-CSharp.dll` SHA-256, and MVID. `EntityController.InProductionCount` returns `Nullable<int>` (`int?`, token `0x060007df`), while `EntityController.EntityId` is PascalCase (token `0x060007c6`) and AI/production structures use camelCase `entityId`. Unity/bundle authoring must use the pinned Editor version documented in the handoff.

## Factory asset bundle validation and fail-closed registration

- **Explicit bundle-required diagnostic**: When `productionBuilding.cardImageAssetName` or `productionBuilding.visualAssetName` is present, `ContentPackValidation.cs` must emit a dedicated `production-building.asset-bundle-required` diagnostic if `productionBuilding.assetBundleId` is null/whitespace. Relying on `ValidateNamespacedId` alone is insufficient because an empty ID silently produces no bundle-related diagnostic, leaving the pack author without actionable feedback.
- **Fail-closed factory registration**: In `ContentPackRuntimeLoader.cs`, the unit-product row and the factory row are added to `temporaryUnits` in two consecutive calls. If `CreateProductionBuilding` throws or returns null, the product row remains registered. Wrap the factory creation in try/catch, remove the previously added product row on failure, and rethrow. This prevents partial registration where a product exists in the provider without its factory.
- **Test coverage**: Add two harness cases in `Program.cs`:
  1. Factory with asset references but null `assetBundleId` → expect `production-building.asset-bundle-required`.
  2. Factory with asset references pointing to an undeclared bundle ID → expect `reference.bundle-unresolved`.
  Run the narrow test first, then the full harness. Do not change unrelated test expectations.

## Asset status and honest reporting

- **Distinguish placeholders from finished assets.** A generated bundle with valid Sprite/GameObject entries is not evidence of HD textures, unique meshes, or correct gameplay. When a unit reuses another unit's model (e.g., SkyguardSAM.fbx for Bulwark), explicitly label it as "reused model — unique authoring pending" in the commit message and validation output. Do not describe a placeholder as a deliverable.
- **Recommend commands only after verification.** Before presenting a shell command as actionable, confirm that the referenced script exists in the repository and its arguments match the documented interface. If the script or its arguments are unverified, label the command as an illustrative proposal, not a recommended workflow.
- **Report validation tiers separately.** Offline validation (bundle references, schema checks, Release builds) proves structural correctness only. Authorized in-game verification (Steam-launched match with the QA profile) is required for registration, pool selection, instantiation, and rendering. Never conflate the two in status reports.

## Blender-generated PBR assets and Unity integration

When a unit needs unique geometry and HD PBR textures produced through Blender and integrated into the Unity authoring project, follow the ordered pipeline in `references/blender-unity-pbr.md`. The key rules:

- **Discover the Blender executable once.** Locate the installed binary by checking standard installation directories, then use the verified executable for all subsequent commands. Record the result and proceed; do not repeat the discovery probe after it succeeds. Do not hard-code a machine-specific path as a standing requirement.
- **Run generators through Blender's embedded Python.** Invoke with `--background --factory-startup --python-exit-code 1 --python <script> -- <arguments>`. Use `tools/generate_unit_model.py` with `--bake-atlas --atlas-resolution 2048 --single-material` for atlas-baked PBR maps. Choose distinct archetypes so each unit has genuinely unique silhouette and material layout.
- **Preserve the texture-to-geometry pairing.** The single-material atlas output (`Name_Atlas.fbx`) matches the baked texture set; the separate multi-material `Name.fbx` does not automatically align. Inspect exporter outputs before assigning one atlas material.
- **Import new files through Unity before expecting `.meta` files.** Missing metadata before import is a lifecycle state, not a reason to poll unchanged files.
- **Persist materials as assets.** Create `.mat` files through `AssetDatabase.CreateAsset` and `SaveAssets`. A transient `new Material` instance is not a completed material deliverable. On reruns, reuse the existing asset path and update properties rather than creating duplicates.
- **Validate before claiming completion.** Rebuild the affected AssetBundle, run `tools/validate_content_bundle_references.py`, and confirm the bundle contains the prefab with the expected material dependency. File existence and image dimensions verify generation only.

## Asset deliverable requirements for Blender-generated PBR work

- Unique geometry and baked texture maps connected to persisted Unity materials and actual prefabs are required. Vertex colors, reused model placeholders, or generated PNGs alone do not satisfy the request.
- When a model reuses another unit's FBX (e.g., SkyguardSAM.fbx for Bulwark), explicitly label it as a reused placeholder until unique geometry is authored.
- After material binding, validate the saved prefab references the material asset (not an in-memory instance) and that shader keywords (`_METALLICSPECGLOSSMAP`, `_NORMALMAP`, `_EMISSION`) are enabled when the corresponding maps are present.

## Loop prevention

- **Advance from evidence instead of repeating probes.** After a check establishes a fact, record the result and perform the next action that changes task state. Repeat a check only after a relevant change or to test a distinct hypothesis; changing an echo label does not constitute a new hypothesis.
- **Treat a user's loop correction as an immediate workflow reset.** Stop the repeated operation, summarize verified completion versus remaining work briefly, and take one bounded corrective action rather than restarting discovery or merely promising to proceed.
- **Honor the requested asset deliverable.** For Blender-generated HD PBR work, deliver unique geometry and baked texture maps connected to persisted Unity materials and the actual prefabs; vertex colors, reused model placeholders, or generated PNGs alone do not satisfy the request.

## Verify honestly

Run the repository's offline validator and Release build when implementation or acceptance criteria call for them. Build the Unity bundle with the repository scaffolder and reopen it in Unity; offline bundle inspection can confirm object names and types. Label these as local validation. Only a Steam-launched match can confirm registration, pool selection, instantiation, and rendering. Use a reversible profile and restore temporary deployment changes after an authorized runtime check.
