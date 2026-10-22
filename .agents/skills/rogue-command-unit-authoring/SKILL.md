---
name: rogue-command-unit-authoring
description: "Author or review Rogue Command units, including balance variants, model overlays, card sprites, and localized names."
---

# Rogue Command Unit Authoring

Use this skill when adding or reviewing a unit in this repository. Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, and `content/schema/README.md` before changing a pack. The handoff is the current source of truth for research, commands, and evidence limits.

## Choose the unit construction path

- A `nativeTemplateId` unit copies a registered game balance row and clones the native prefab. Use `balanceOverrides` only for allowlisted numeric fields. It keeps the native controller and gameplay components.
- Every added unit requires its own `productionBuilding` with a distinct namespaced ID, a native factory template, and an English name. Keep Spanish names in the localization array. The loader must clone and register a new factory row/prefab, set its `factoryForEntityId` to the new unit, and leave native mappings intact. Reward pools offer this new building's blueprint; stage manifests and AI rosters continue to refer to the produced unit. Verify building placement and actual production in Steam before claiming this path works.
- A native visual overlay can set `visualAssetName` and `visualRootPath` with an AssetBundle. The root path must resolve to one native renderer subtree. The loader replaces those renderers and retains the native gameplay hierarchy; it does not retarget rigs or animations.
- Before installing a native visual overlay, run `.venv\Scripts\python.exe tools/validate_native_visual_roots.py <pack-directory>` against the pinned game. Do not copy a visual path between different native templates: ArtilleryTank uses `Tank`, GatlingWalker uses `RoboMarine_Mesh`, and LightningTank uses `PCX TankHolder` on the currently verified asset identity. This static check does not prove live resource selection or rendering.
- A custom-prefab unit needs a complete bundled balance asset, prefab, and card Sprite with the game-required serialized components and references. The Unity Editor's inability to attach the precompiled game scripts is an authoring constraint; fail closed when a complete prefab cannot be produced.
- AssetBundles hold serialized Unity assets, not new executable `MonoBehaviour` code. Put new code in the compiled loader only when the task requires it.

## Identity, text, and eligibility

- Use a namespaced unit ID and provide an `en-US` display name for custom-prefab units. Pack translations use the exact language IDs in `content/schema/README.md`; runtime resolution tries the active language, then English, then native-template text, and finally a safe ID-derived name.
- Use `cardImageAssetName` for a bundle Sprite when the unit needs its own card image. Enumerate the bundle with UnityPy (`env.container.keys()`) first, and add the matching `assetBundleId` + `cardImageAssetName` to every production building whose card PNG exists; missing card metadata is a common consistency error. The current Sandlance sample demonstrates a model overlay, icon, balance overrides, and localization; it still inherits `PCXBaseTank` gameplay.
- Keep `productionBuilding` metadata paired: `cardImageAssetName` requires `assetBundleId`, and `visualAssetName` requires `visualRootPath`; set both fields or leave both unset. Enumerate the bundle with UnityPy (`env.container.keys()`) before assigning asset names so every referenced card/visual exists.
- Set `isAllowedForAi` explicitly for every unit (`true` for mobile combat units, `false` for buildings or command HQs). Leaving it null makes eligibility implicit and can fail production-building eligibility checks.
- When scoping products with `blueprintPoolEntries`, remember that a listed pool receives only its listed products, while unlisted pools retain the broad `stage.unitIds` set. Product IDs in an entry need not be repeated in `stage.unitIds`, but `stage.unitIds` still governs broader stage eligibility.
- Native visual roots must point to a renderer-containing subtree, not necessarily the template root. Run `.venv\\Scripts\\python.exe tools/validate_native_visual_roots.py <pack-directory>` and use the reported child (`Vehicle`, `Tank`, `RoboMarine_Mesh`, `GameObject/BombBike`, `PCX TankHolder`); a root with zero renderers fails validation. Do not copy a visual root between different native templates.
- A stage's `unitIds` names produced units. The loader translates those IDs to their own factory IDs for blueprint eligibility and cloned pool scopes; product rows stay ineligible as blueprint rewards. Cloned stage blueprint pools preserve native per-pool filters (`blueprintFilter.IsBlueprintValid`); eligibility is stage-wide across cloned pools subject to each retained filter. Check generic rewards without a pool ID separately because they use the game's global eligibility fallback.
- Before relying on reflected game methods, recheck the compatibility identity as a whole: Rogue Command version, `Assembly-CSharp.dll` SHA-256, and MVID. `EntityController.InProductionCount` returns `Nullable<int>` (`int?`, token `0x060007df`), while `EntityController.EntityId` is PascalCase (token `0x060007c6`) and AI/production structures use camelCase `entityId`. Unity/bundle authoring must use the pinned Editor version documented in the handoff.

## Verify honestly

Run the repository's offline validator and Release build when implementation or acceptance criteria call for them. Build the Unity bundle with the repository scaffolder and reopen it in Unity; offline bundle inspection can confirm object names and types. Label these as local validation. Only a Steam-launched match can confirm registration, pool selection, instantiation, and rendering. Use a reversible profile and restore temporary deployment changes after an authorized runtime check.
