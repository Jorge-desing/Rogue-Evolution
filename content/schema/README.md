# Content Pack v1

`content.schema.json` defines the `content.json` file read by the loader. Its DTOs are plain serializable C# classes for Unity `JsonUtility`; each collection and parameter map is represented by an array.

## Shape

- `packId`, bundle IDs, unit IDs, stage IDs, AI profile IDs, and module IDs use `namespace:local-id` form. All content IDs must be unique across every installed pack; the loader disables packs that claim the same ID.
- `assetBundleManifestPath` points to the Unity manifest AssetBundle produced alongside content bundles. A data-only pack may set `assetBundles` to `[]` and omit the manifest path. Otherwise, every bundle must be listed; the loader checks exact names against the Unity manifest, computes dependency order from `GetDirectDependencies`, and loads dependencies first. A dependency missing from the declared list disables the pack. A pack may declare up to 128 bundles; each bundle path uses `bundles/<lowercase-name>.bundle`, and bundle names cannot be Windows device names such as `con`, `nul`, or `com1`.
- A unit can use a custom bundle source (`assetBundleId`, `balanceAssetName`, `prefabAssetName`, and `cardImageAssetName`) or `nativeTemplateId`. The native mode copies a registered balance row, makes a dormant runtime clone of the native prefab, changes its root `EntityController.entityId` to the new namespaced ID, and routes that row to the clone. It reuses the native card image unless `cardImageAssetName` supplies a replacement Sprite from a bundle. For a custom model, a native-template unit may also set `visualAssetName` and `visualRootPath` and provide `assetBundleId`. The visual asset must be a `GameObject` prefab with at least one enabled Renderer and no missing or `MonoBehaviour` components. The relative Transform path must identify exactly one native subtree containing renderers; the loader disables those renderers and attaches an instance of the bundle prefab while keeping the native gameplay hierarchy and components. Renderers without materials inherit the first native `MeshRenderer` material found in the target subtree; authored bundle materials are kept. The model prefab root is placed at local position zero, identity rotation, and unit scale, so place offsets and scale in its child hierarchy. This adds custom geometry but does not map native animations or rig behavior to the new model. If a template's controller depends on that model-specific animation, movement or weapon visuals may not carry over. `visualAssetName` and `visualRootPath` must be declared together and only with `nativeTemplateId`. A native unit can use the same bundle for its model and optional card Sprite. `balanceOverrides` changes only an allowlist of finite, non-negative numeric fields using invariant-culture strings; enum, flag, ID, prefab, and image fields cannot be changed this way. Native template IDs resolve against the exact game registry at package load.
- A stage can use a custom bundled `StageParametersScriptableObject` (`assetBundleId` and `assetName`) or a loaded native template (`nativeTemplateId`). The loader clones the ScriptableObject before applying the bounded numeric `parameterOverrides`, so it does not edit the original asset. A native stage variant reuses its template's world and blueprint-pool references; it changes progression parameters and can list new unit/AI IDs, but it does not provide a new map layout. Custom maps still need a valid bundled stage asset with world and blueprint-pool references. `mapSizeMin` and `mapSizeMax` define the `Vector2Int` range (1–256), and `isPartOfDemo` is copied to the wrapper. `unitIds` marks new units as blueprint-eligible by setting the game-wide `isAllowedAsBlueprint` row flag; because blueprint pools may be shared and filter against the global entity list, the unit can also become eligible in other native pools whose filters accept it. The stage's pools still filter the available blueprints. `aiProfileIds` names compiled AI profiles added to the default, Heat, and FirstRun progression lists. References may target the current pack, a native game unit, or another pack; cross-pack dependencies load first.
- An AI profile can use a custom bundled prefab or `nativeTemplateId`, which names a loaded native `DefaultAiBehaviour` GameObject exactly. Native mode is an overlay on that native resource: the profile contributes the existing behaviour and binds its compiled modules whenever that native AI is spawned. It does not clone a new AI prefab or change the native deck; only one loader profile may claim a given AI resource name, and its modules affect every use of that template. Custom AI prefabs must start with reserved prefix `RCL_`, be active, contain exactly one `DefaultAiBehaviour`, and have `AiBehaviour.isDeactivated` set to `false`. Rogue Command tags the spawned instance `AI` before its first `Update`; modules execute in manifest order after initialization and each native update, then clean up in reverse order. If a module fails, the loader disables that stack and attempts cleanup for every module. Each module declares `moduleId` and string-pair `parameters`; the registered module validator defines their types and meaning. `sample:scheduled-base-build` uses the game's `DefaultAiBehaviour.CreateBuilding_Static` helper to build a building already present in the active native deck, with cooldown and finite attempt/success limits. `sample:reactive-base-defense` watches for player units attacking an AI building and inserts attack-move counterattacks before selected defenders' current command, preserving that command for later resumption. It limits each response by AI base, defense radius, cooldown, and responder count; it does not add a native AI deck or change the map layout. Both examples overlay native AI templates, so each affects every use of its selected template.
- Bundle paths use exactly `bundles/<lowercase-name>.bundle` with forward slashes so the loader, schema, and Unity builder share one format. The validator rejects rooted paths, traversal segments, missing files, reparse points, and DLL files anywhere in the package. Manifest bundles are limited to 64 MiB; each content bundle is limited to 512 MiB; loaded content bundles are limited to 2 GiB across all packs in one game session. The Unity builder applies the per-pack content-bundle limit. It builds chunk-compressed LZ4 output; these on-disk limits do not guarantee peak memory use for externally produced LZMA bundles, which Unity decompresses in memory. The loader keeps each content bundle's input stream open until its AssetBundle is unloaded, as required by Unity for uncompressed or chunk-compressed bundles.

`sample:reactive-base-defense` accepts `baseIndex` (integer 0–31), `cooldownSeconds` (finite 1–60 seconds), `defenseRadiusCells` (finite 1–64 map cells; default 12), and `maxResponders` (integer 1–4; default 2). It reacts only to player units whose current attack target is an AI building at that base, is targetable by the AI, and is in range. It chooses nearby, attack-capable defenders that are idle or moving, excludes units with pending skill commands or paused command chains, and counts naturally engaged or previously dispatched units toward the cap. It inserts a finite attack-move-to-position command before the selected unit's idle/move order, which the game resumes after that response reaches its destination. This tactical behavior has local build and parser/manifest validation only; verify it in a Steam-launched match before treating its live behavior as proven.
- Include empty arrays when a section has no entries. The validator reports package-local diagnostics and does not mutate the registry.

## Example

```json
{
  "schemaVersion": 1,
  "packId": "example:expansion",
  "assetBundleManifestPath": "bundles/assetbundle-manifest.bundle",
  "assetBundles": [
    {
      "id": "example:assets",
      "path": "bundles/expansion.bundle"
    }
  ],
  "units": [
    {
      "id": "example:scout-tank",
      "nativeTemplateId": "PCXBaseTank",
      "assetBundleId": "example:assets",
      "cardImageAssetName": "ScoutTankCard",
      "balanceOverrides": [
        { "name": "cost", "value": "70" },
        { "name": "maxHealth", "value": "130" },
        { "name": "moveSpeed", "value": "1.1" }
      ]
    }
  ],
  "stages": [
    {
      "id": "example:training-stage",
      "nativeTemplateId": "StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit",
      "parameterOverrides": [
        { "name": "levelCount", "value": "8" }
      ],
      "mapSizeMin": 8,
      "mapSizeMax": 12,
      "isPartOfDemo": false,
      "unitIds": ["example:scout-tank"],
      "aiProfileIds": ["example:guard-ai"]
    }
  ],
  "aiProfiles": [
    {
      "id": "example:guard-ai",
      "nativeTemplateId": "_01_ Quick Hunter Enemy Var",
      "modules": [
        {
          "moduleId": "sample:scheduled-base-build",
          "parameters": [
            { "name": "intervalSeconds", "value": "8" },
            { "name": "maxSuccessfulBuilds", "value": "1" },
            { "name": "maxAttempts", "value": "3" }
          ]
        }
      ]
    }
  ]
}
```
