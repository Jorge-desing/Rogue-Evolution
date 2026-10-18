# Content Pack v1

`content.schema.json` defines the `content.json` file read by the loader. Its DTOs are plain serializable C# classes for Unity `JsonUtility`; each collection and parameter map is represented by an array.

## Shape

- The native-unit sample overlays five models on cloned native units: Sandlance on `PCXBaseTank/Vehicle`, Skirmish Strike Bike on `BombBike/GameObject/BombBike`, Colossus on `ArtilleryTank/Tank`, Ironclad on `GatlingWalker/RoboMarine_Mesh`, and Surge on `LightningTank/PCX TankHolder`. These paths were inspected in the pinned game's serialized assets. Each path is relative to the native prefab root; the template name shown here is not part of `visualRootPath`. All five retain native controllers and combat behavior. Verify model spacing, animation compatibility, and rendering in a live match.

Run `.venv\Scripts\python.exe tools/validate_native_visual_roots.py content/packs/sample-native-expansion` before installing a native overlay pack. The tool checks the game's version, assembly SHA-256, MVID, and `resources.assets` SHA-256, then resolves every path through unique direct children and requires a Renderer below the target. It selects a uniquely named scripted GameObject in that serialized file and rejects ambiguous candidates; runtime resource selection still requires a Steam test. Install its pinned Python dependencies from `requirements.txt`.

- `packId`, bundle IDs, unit IDs, stage IDs, AI profile IDs, and module IDs use `namespace:local-id` form. All content IDs must be unique across every installed pack; the loader disables packs that claim the same ID.
- `assetBundleManifestPath` points to the Unity manifest AssetBundle produced alongside content bundles. A data-only pack may set `assetBundles` to `[]` and omit the manifest path. Otherwise, every bundle must be listed; the loader checks exact names against the Unity manifest, computes dependency order from `GetDirectDependencies`, and loads dependencies first. A dependency missing from the declared list disables the pack. A pack may declare up to 128 bundles; each bundle path uses `bundles/<lowercase-name>.bundle`, and bundle names cannot be Windows device names such as `con`, `nul`, or `com1`.
- Bundle asset references use the exact address emitted by Unity's AssetBundle, preferably the project-relative path including its extension. A unique filename stem is also accepted by Unity but can select an unintended asset when names collide; the runtime loader requests the declared Unity type so a Sprite can be selected when the texture file also exposes a Texture2D. Run `tools/validate_content_bundle_references.py` with the pack directory to check declared addresses against the bundle's serialized entries before launching the game.
- A unit can use a custom bundle source (`assetBundleId`, `balanceAssetName`, `prefabAssetName`, and `cardImageAssetName`) or `nativeTemplateId`. The loader clones a custom prefab under an inactive holder, sets the clone's root `EntityController.entityId` to the unit's namespaced ID, and registers only that clone; it leaves the AssetBundle prefab unchanged. The native mode copies a registered balance row, makes a dormant runtime clone of the native prefab, changes its root `EntityController.entityId` to the new namespaced ID, and routes that row to the clone. It reuses the native card image unless `cardImageAssetName` supplies a replacement Sprite from a bundle. For a custom model, a native-template unit may also set `visualAssetName` and `visualRootPath` and provide `assetBundleId`. The visual asset must be a `GameObject` prefab with at least one enabled Renderer and no missing or `MonoBehaviour` components. The relative Transform path must identify exactly one native subtree containing renderers; the loader disables those renderers and attaches an instance of the bundle prefab while keeping the native gameplay hierarchy and components. Renderers without materials inherit the first native `MeshRenderer` material found in the target subtree; authored bundle materials are kept. The model prefab root is placed at local position zero, identity rotation, and unit scale, so place offsets and scale in its child hierarchy. This adds custom geometry but does not map native animations or rig behavior to the new model. If a template's controller depends on that model-specific animation, movement or weapon visuals may not carry over. `visualAssetName` and `visualRootPath` must be declared together and only with `nativeTemplateId`. A native unit can use the same bundle for its model and optional card Sprite. Units can declare `isAllowedAsStartingBlueprint: true` to enable the dedicated Stage 1 starting-deck filter. The loader mirrors this flag and the product''s isAllowedForDemo setting to the factory row because the global no-pool fallback filters the factory candidate before resolving its product; the produced-unit row remains ineligible as a normal blueprint reward. Stage `unitIds` name produced units, while the loader adds their dedicated production buildings to the applicable randomized blueprint pools. `isAllowedForAi: true` allows AI rosters to field the produced unit, `isAllowedForDemo: true` enables demo mode eligibility, and `isForSpecialists: true` enables specialist archetype pools. `balanceOverrides` changes allowlisted numeric fields using invariant-culture strings, as well as enums: `rarity` (`Common`, `Rare`, `UltraRare`), `attackType` (`None`, `Single`, `AoE`, `Multi`, `MultiAoE`, `Beam`, `Artillery`, `MultiArtillery`), and `skillType` (`None`, `Toggle`, `Location`, `LocationAoE`, `LocationMultiTargets`, `Self`, `SelfAoE`, `Target`, `TargetMultiTarget`, `TargetAoE`, `SelfMultiTarget`); non-allowlisted fields cannot be changed this way. Native template IDs resolve against the exact game registry at package load. Every added unit also requires a `productionBuilding` with its own namespaced ID, a registered native factory template, optional balance overrides, and an `en-US` building localization. The building ID must differ from its product ID and cannot be shared by another added unit. Stage `unitIds` continue to name produced units; the loader offers their dedicated buildings in the applicable random blueprint pools. AI `unitEntries` also name the produced unit ID.
- A stage can use a custom bundled `StageParametersScriptableObject` (`assetBundleId` and `assetName`) or a loaded native template (`nativeTemplateId`). The loader clones the ScriptableObject before applying the bounded numeric and boolean `parameterOverrides` (including `levelCount`, `randomSeed`, `startWithCardCount`, `fillFirstLevelWithCardRewards`, `putDefaultCardRewardInCenter`, and `fillLastLevelWithShops`), so it does not edit the original asset. A native stage variant reuses its template's world references and can list new unit/AI IDs. It may also declare `landscape` to clone a named native `LandscapeGenerator`, set its seed and odd map size, and localize its display name. The loader exposes that generator only while the matching stage's `LevelProgression.CreateLevelMap` runs, then a Harmony finalizer restores the progression and AI map lists even if the game throws. This produces a stage-scoped procedural variant of existing terrain rules; it does not define new generation logic or World prefabs. `mapSizeMin` and `mapSizeMax` define the `Vector2Int` range (1–256), and `isPartOfDemo` is copied to the wrapper. Stage `unitIds` name produced-unit IDs; for added units, the loader marks the dedicated production-building row as normally blueprint-eligible. `isAllowedAsStartingBlueprint` remains a separate flag on the produced-unit row and does not make that product a regular reward. When the game uses a `BlueprintPool`, the loader clones that stage's pool list and pools, keeps the original filters, assigns loader-reserved `RCL_POOL_` IDs, and resolves them through Harmony; hooks admit each custom production-building row only through pools cloned for stages that declare it. A generic reward list created without a pool ID still follows the game's global eligibility fallback and may offer any production-building row enabled by a declared stage, even outside its originating stage pool. Exclusive-pool expansion includes only scoped production-building IDs and only across pools cloned for that same stage, so exclusivity cannot leak between stages or into native pools. Native stage parameters, pool assets, and native unit rows remain unchanged. This is static implementation and local validation; additive blueprint selection still needs in-game verification. `aiProfileIds` names compiled AI profiles added to the default, Heat, and FirstRun progression lists. References may target the current pack, a native game unit, or another pack; cross-pack dependencies load first.
- Offline stage preflight follows the game's map candidates: AI `restrictedToMaps`, then the selected World `restrictedToMaps`, then the progression's available maps. A stage-owned `landscape` is added to those candidates only for its stage during map creation. Because `CreateLevelMap` samples from those lists directly, every in-range AI/World restricted map must be active and demo-allowed when applicable. A selected map must have a non-null `restrictedToWorlds` list because the game reads its count. When that restriction is used, every entry must be non-null and active, and demo generation requires every entry to be demo-allowed; the game may choose any listed World for generation. An empty list means no restriction. These checks model the pinned game build and are not in-game runtime proof.
- An AI profile can use a custom bundled prefab or `nativeTemplateId`, which names a loaded native `DefaultAiBehaviour` GameObject exactly. A native-template profile must set `scopeToReferencedStages: true`: the loader clones the native GameObject under a generated `RCL_` resource name, binds compiled modules only to that clone, leaves the original template unchanged, and sets the clone's stage range to the contiguous indices of stages in the same pack that reference the profile. Unscoped native profiles are rejected so modules cannot alter every use of the shared native AI. This range is calculated against the active progression lists after previously loaded content, so packs do not need to hardcode stage indices. A scoped profile must be used by at least one stage in its declaring pack; another pack cannot widen its scope, and referenced stages must be contiguous. The runtime clone/alias mechanism and its range calculation have local build and offline validation only; no in-game behavior has been confirmed. Custom AI prefabs must start with reserved prefix `RCL_`, be active, contain exactly one `DefaultAiBehaviour`, and have `AiBehaviour.isDeactivated` set to `false`. Rogue Command tags the spawned instance `AI` before its first `Update`; modules execute in manifest order after initialization and each native update, then clean up in reverse order. If a module fails, the loader disables that stack and attempts cleanup for every module. Each module declares `moduleId` and string-pair `parameters`; the registered module validator defines their types and meaning. `sample:scheduled-base-build` uses the game's `DefaultAiBehaviour.CreateBuilding_Static` helper to build a building already present in the active native deck, with cooldown and finite attempt/success limits. `sample:reactive-base-defense` watches for player units attacking an AI building and inserts attack-move counterattacks before selected defenders' current command, preserving that command for later resumption. It limits each response by AI base, defense radius, cooldown, and responder count; an optional unitId parameter restricts responses to that exact roster unit. It does not add a native AI deck or change the map layout. `sample:harvester-raider` detects player crystal-gathering harvesters across the map, selects free combat troops from an AI base, and dispatches tactical raid squads directly to attack the player's economic gatherers, enforcing scan intervals, minimum game time, raid squad size, and cooldown. `sample:siege-assault` scans known player buildings or refineries, gathers combat troops from an AI base, and dispatches coordinated assault strike waves to dismantle player fortifications, with configurable target priorities (`ClosestBuilding`, `Refinery`, `Hq`).
- Bundle paths use exactly `bundles/<lowercase-name>.bundle` with forward slashes so the loader, schema, and Unity builder share one format. The validator rejects rooted paths, traversal segments, missing files, reparse points, and DLL files anywhere in the package. Manifest bundles are limited to 64 MiB; each content bundle is limited to 512 MiB; loaded content bundles are limited to 2 GiB across all packs in one game session. The Unity builder applies the per-pack content-bundle limit. It builds chunk-compressed LZ4 output; these on-disk limits do not guarantee peak memory use for externally produced LZMA bundles, which Unity decompresses in memory. The loader keeps each content bundle's input stream open until its AssetBundle is unloaded, as required by Unity for uncompressed or chunk-compressed bundles.

`sample:reactive-base-defense` accepts optional `unitId` (validated game entity ID), `baseIndex` (integer 0–31), `cooldownSeconds` (finite 1–60 seconds), `defenseRadiusCells` (finite 1–64 map cells; default 12), and `maxResponders` (integer 1–4; default 2). It reacts to eligible Player units with attack and attack component enabled and a current attack target that remains a live AI building at that base, provided the attacker is within `defenseRadiusCells` of the target building. Weapon-range state is not required, so units advancing under a valid Attack order can trigger a response before their weapon is in range. The attacker must also be targetable by AI and not carry the `canNotBeAttacked`, `canNotBeTargetedByAi`, or `doNotRegisterGlobally` exclusions. It chooses nearby defenders with enabled movement and attack that are idle or moving, excludes units with pending skill commands or paused command chains, and counts naturally engaged or previously dispatched units toward the cap. It inserts a finite attack-move-to-position command before the selected unit's idle/move order, which the game resumes after that response reaches its destination. Both current native-template sample packs set `scopeToReferencedStages: true`; static inspection found their source AI templates only accept stages 0–1, while appended stages begin at index 9. The loader now derives an isolated alias range for the referenced stages instead of mutating the shared native template. This is a local-build/static-preflight hypothesis only; none of the unit, stage, or AI sample content has been confirmed in a live match.

`sample:harvester-raider` accepts `baseIndex` (integer 0–31), `scanIntervalSeconds` (finite 2–120 seconds; default 10), `cooldownSeconds` (finite 5–300 seconds; default 25), `raidSquadSize` (integer 1–8; default 3), and `minGameTimeSeconds` (finite 0–600 seconds; default 15). It searches `ExistingControllers.Instance.Harvesters` for active player gatherers, selects idle or moving AI combat units stationed at the specified base, and issues attack-move commands directly to harass or eliminate harvesting operations.

`sample:siege-assault` accepts `baseIndex` (integer 0–31), `cooldownSeconds` (finite 10–300 seconds; default 30), `minSquadSize` (integer 1–12; default 4), `targetPriority` (`ClosestBuilding`, `Refinery`, `Hq`; default `ClosestBuilding`), and `minGameTimeSeconds` (finite 0–600 seconds; default 30). It scans known player buildings (`PlayerBuildingsKnownToAi`, `PlayerRefineries`, or `EngineeringHq`), gathers combat units from the specified AI base, and dispatches coordinated assault strike waves to dismantle player fortifications.
- Include empty arrays when a section has no entries. The validator reports package-local diagnostics and does not mutate the registry.

## AI unit entries

An AI profile may optionally declare `unitEntries` to append namespaced units to one `AiBase` in an isolated, stage-scoped native profile. Each entry selects the `aiBases` index, the starting ascension level, the number of units spawned at base setup, and the native `maxLiving` value. The referenced unit must be declared by this pack or another loaded/declared pack; the loader resolves that dependency before loading the profile. Custom AI prefabs and unscoped templates cannot use `unitEntries`, so the native AI template remains unchanged. `unitEntries` may be omitted when unused; sample manifests include an empty array for clarity.

Custom AI prefabs may optionally declare `unitDependencies` for namespaced units referenced by serialized `AiBase.entitiesInAiDeck` entries. Cross-pack IDs must be declared here so the loader loads the owning unit pack first, independent of pack-directory order. Native game IDs and units declared in the same pack need no dependency entry. The loader rejects a custom prefab whose external deck IDs omit this declaration, even if another pack happened to load first. Dependencies are references only; they do not replace or edit native AI templates.

Static REA inspection of the pinned 1.0.9 assembly found that `AiBehaviour.BuildBaseStartSetup` creates each eligible `EntityInAiDeck` entry at its AI base. Non-building entries flow through `EntityFactory.InstantiateEntity` with the AI tag and receive the base index, so a mod unit added to the isolated profile can join that AI's starting roster. This is static inspection only; the sample pack has not yet been verified in a Steam-launched match.

## Unit localization

Units may declare `localizations` entries with a game's language ID, a visible `name`, and an optional `description`. Custom-prefab units must provide an `en-US` name; native-template units may omit localizations and inherit the native template's name and description. If the active language has no pack translation, the loader falls back to `en-US`; additional supported game languages are `de-DE`, `ja-JP`, `ko-KR`, `zh-CN`, `zh-TW`, `fr-FR`, `es-XL`, `pt-BR`, and `ru-RU`. Duplicate or unsupported language IDs disable the package. The loader intercepts `Loca.BlueprintName` and `Loca.BlueprintDescription` for registered mod-unit IDs, so namespaced IDs resolve without editing the game's localization resources.

Landscape `localizations` use the same supported language IDs and require an `en-US` name. The loader intercepts `Loca.Global` only for generated landscape localization keys; all other game localization calls continue through the original method.

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
      "cardImageAssetName": "assets/example/scouttankcard.png",
      "localizations": [
        { "language": "en-US", "name": "Sandlance Scout", "description": "A fast reconnaissance tank." },
        { "language": "es-XL", "name": "Explorador Sandlance", "description": "Un tanque rápido de reconocimiento." }
      ],
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
      "scopeToReferencedStages": true,
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
