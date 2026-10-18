# Production buildings for added units

## Invariant and migration

Every unit in a content pack must declare its own `productionBuilding`. The building ID and product ID are distinct namespaced entity IDs. Sharing a building between added units, colliding with an existing ID, or omitting the building disables that pack before registration. Existing version-1 manifests that add units must add this field; data-only stage/AI packs need no change.

`productionBuilding.nativeTemplateId` selects a registered native factory with both `Building` and `Factory` roles and a valid native product. The loader copies its balance row and clones its prefab under an inactive holder, then assigns the new building ID and resource path. It retains the native building geometry, components, and card image. Building names/descriptions are independent translations and require `en-US`; the samples also supply `es-XL`.

The product row has no `factoryForEntityId`. The building row sets `factoryForEntityId` to the added product ID. The entity mutator appends both rows and inserts the inverse `FactoryEntityIdOf[product] = building` mapping. Original native rows, prefabs, and inverse mappings are preserved. This direction matters: setting the product row's field would reverse the relationship.

Both new rows start with `isAllowedAsStartingBlueprint=false`. Stage `unitIds` continues to name products; the loader translates them to factory IDs for reward scopes and blueprint eligibility. Product rows stay `isAllowedAsBlueprint=false`. Native `BlueprintPool.IsInPool(factoryId)` evaluates its product against category filters and returns the factory ID as the reward. The native card flow presents the product and maps it back to its factory when obtaining the blueprint. Rewards without a pool ID retain the game's global eligibility fallback.

AI `unitEntries` names produced units. A native AI's existing serialized deck can contain buildings and factories; its validation must continue to admit those native entries. This is separate from the product-only validation of new `unitEntries` and stage IDs.

## Current sample pairs

| Product ID suffix | Building ID suffix | Native factory template | Factory cost | Product duration |
|---|---|---|---:|---:|
| `scout-tank` | `scout-tank-factory` | `PCXBaseTankFactory` | 500 | 42 s |
| `heavy-artillery` | `heavy-artillery-factory` | `ArtilleryTankFactory` | 800 | 50 s |
| `assault-walker` | `assault-walker-factory` | `GatlingWalkerFactory` | 1100 | 36 s |
| `skirmish-bike` | `skirmish-bike-factory` | `BombBikeFactory` | 280 | 10 s |
| `surge-tank` | `surge-tank-factory` | `LightningTankFactory` | 650 | 30 s |

All sample IDs above use `sample-native:`. The Sandlance factory explicitly permits three buildings; other factory counts inherit their native templates. The product and factory unlock levels now match each other; see the profile-level progression below. Costs, timings, and capacities are explicit in the sample manifest but have not been tuned through a controlled gameplay balance pass.

## Profile-level card unlocks

`neededExperienceLevel` is compared with the persistent profile's experience level, which rises from profile XP when a stage/run is completed or lost. It is unrelated to the current in-run stage number or the selected map node. A card becomes eligible once the profile meets its threshold, but the roguelike reward draw remains random. The first selection at stage 0 is limited to vehicles or small units regardless of starting map node, so that offer is not a complete test of later unlock tiers.

The product and its factory carry the same `neededExperienceLevel` and `rarity`. The static game path queries the product's level when building a pool, while some reward candidate paths inspect the factory row; keeping both values aligned avoids mismatched unlock behavior.

| Profile level | Card | Rarity | Card cost | Durability | Attack and card ability |
|---:|---|---|---:|---|---|
| 0 | Sandlance Scout | Common | 70 | 130 health | AoE, 8 damage at range 6; fast scout |
| 1 | Skirmish Strike Bike | Common | 55 | 75 health | AoE, 32 damage, radius 1.8; dies and summons a Large Mine |
| 2 | Ironclad Gatling Walker | Common | 380 | 420 health, 1 max armor | Single attack, 4 damage, 6 firepoints at range 6 |
| 3 | Surge Arc Tank | Common | 210 | 150 health, 5 shield | Beam, 12 damage at range 4.5 |
| 4 | Colossus Siege Platform | Common | 280 | 160 health, armor protection 2 | Artillery, 55 damage at range 16; toggle movement/attack mode |

The active sample manifest explicitly records 29 balance fields for each product, including zero values for unused shields, secondary weapons, and skill parameters. All five cards have English and Spanish names and descriptions; the Bike and Colossus also have English and Spanish skill text. This makes their card data deliberate rather than inherited from the native templates.

## Static evidence

Target identity: Rogue Command 1.0.9, Unity 2021.3.45f2, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Recheck the full tuple before reusing reflected signatures. REA identity evidence: `ev_eea143512c436bb6579559b439d25e7356d59246194b33404955c85560424097`.

The factory/product metadata comes from the canonical `Balancing - Entity` TextAsset, path ID 13774 in `resources.assets`, rather than the shortened `Mod/exampleEntityBalancing.tsv`. Native decompilation traces `EntityBalancingStore`, `BlueprintPool`, `ChooseCard`, `PlaceBuildings`, `EntityController.Produce` and `UpdateProduction`. Those files are local ignored research output under `artifacts/research/assembly-csharp-decompiled/` in the primary checkout.

The verified starter field is `EntityBalancingParameters.isAllowedAsStartingBlueprint`; `isStartingBlueprint` is a filter parameter name, not the row field. A runtime startup attempt exposed that naming error; the corrected loader started successfully.

## Local validation, 2026-10-17

- Release build: zero errors and warnings.
- Contract/registry harness before the later placement-distance patch: 89/90 passed, one Windows symlink privilege skip.
- All three source manifests pass the JSON Schema.
- Read-only native visual preflight passes all five overlay paths after checking the compatibility tuple and `resources.assets` hash.
- ZIP inspected: plugin, two installable packs, bundles, contract/readmes; no game assemblies, BepInEx/Harmony runtime dependencies, or PDBs.
- Static code review covered IDs, factory roles/product relation, clone cleanup, registration, blueprint translation and AI dependencies. Review found a native AI deck regression while separating products/factories; it was corrected before the successful runtime attempt.

## Steam runtime evidence, 2026-10-17

A reversible test launched via Steam AppID 1461910 with the user's already-disabled Cloud setting. The tested package SHA-256 was `ce3460680ffe9965e55366151c954433fff4ef2f6ee56e5c8e99a907cb5dc865`. This production test predates the later appended-stage placement-distance patch.

Observed in the live game:

1. Loader installed 18 hooks; both packs loaded, including five new products and five separate production buildings.
2. All five `FactoryEntityId(product)` and `ProductEntityId(factory)` pairs matched. Products were not blueprint rewards; all five factories were eligible and were not starting blueprints. Spanish building names resolved.
3. The five original native inverse factory mappings remained unchanged.
4. Pool queries for appended stage indices 9/10/11/12 admitted exactly the custom factories for their declared products. Stage 11's union contained all five; stage 12 admitted Sandlance and Bike factories. Original category filters and localized pool tooltips still applied.
5. A directed jump loaded stage 12 using native `LoadScene.LoadLevel()` once. Five buildings were placed through `BuildingPlacementHelper.CreateAndPlaceBuilding` at valid native placement positions near the player.
6. Called native `Produce(instantProduction:false, forFree:true, doNotTriggerHasProducedEvent:false)` once per factory. All five queues started and later reached zero with `productionHasStarted=false`; all five product IDs were active in the Game scene with `Player` tags and enabled renderers.
7. A screenshot after dismissing the native start overlay shows the placed factory cluster in the active stage. It does not establish animation/spacing quality of every product.

This validates directed native placement/production and pool eligibility. It does not prove that a complete ordinary run naturally offered every random reward, that a player bought/placed every factory through UI, or that the reactive AI issued a counterattack decision. Factory placement and production were invoked through diagnostic calls without charging the test bank.

The first placement probe used an offset beyond the current map bounds and threw in native WorldGrid. Retrying from the existing player position let native placement choose valid cells; that probe mistake is not a loader placement defect. The stage also logged the native hardcoded `Unknown stage 12` placement-distance fallback, addressed separately by the appended-stage compatibility patch.

Local evidence is preserved in the primary checkout's ignored `artifacts/runtime-smoke/current-gameplay-validation-20261017/`: `installed-package-production-corrected.json`, `production-responses.json`, `production-completed.json`, `factory-pool-coverage.json`, `LogOutput.production-runtime.log`, `Player.production-runtime.log`, `RclFactoryAcceptance.cs`, and `production-buildings.png`. Logs/screenshots stay local and are not Git inputs. The test game was closed; all 13 baseline files (plugin/content, log, profile, Steam cache) were restored and hash-verified using `Restore-ProductionSession.ps1`. Steam may update its cache metadata afterward; preserve fresh metadata in a new test backup rather than treating that as profile loss.


## Final appended-stage spacing check

The final package SHA-256 is `0220a326cd287243b644f7d5f1314ce00799023309117c5a8fdb96d381f1f6ea`. It adds a 19th Harmony hook for the exact native static `int MinDistanceOfOutlyingStartAiBuildingsToPlayerStart()` method. For stage indices 9 and above, the provider overrides it only when the active progression slot matches one of this loader's registered stages by object reference. It reads the configured final native spacing band from the active balancing object; native stages and foreign stages continue through the original method. This is an extension policy for appended stages, not a claim that their original templates mandate late-game spacing.

A separate Steam run of that final package observed stage 12 returning distance 29, equal to the configured native final band. Its fallback snapshot had 13 slots while the native backing list retained nine. The Game scene loaded and the reactive AI initialized; the complete captured Player log contained zero `Unknown stage` entries. Full five-factory production was observed in the preceding package, before this spacing-only integration. No product, factory or manifest changes were made between those two successful tests.

The first spacing startup found both global `Game` and `Shapes2D.Game` in the game assembly. The provider now resolves the full global type name exactly rather than selecting by ambiguous short name. The successful final run verifies that correction. `LogOutput.placement-runtime.log`, `Player.placement-runtime.log`, `placement-distance-verified.json`, and `installed-package-placement-corrected.json` preserve this follow-up evidence. The game was closed and 13 baseline files were restored and hash-verified with `Restore-PlacementSession.ps1`.

The portable spacing tests cover native indices, unrelated stages and a loader-owned stage with a non-default configured distance of 37. They are included in the final 89/90 passing harness result (one Windows symlink privilege skip). Final ZIP inspection found nine entries with only the loader DLL as executable code.
