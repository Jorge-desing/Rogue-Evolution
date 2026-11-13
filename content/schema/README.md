# Content Pack v1

`content.schema.json` defines the `content.json` file read by the loader. `Newtonsoft.Json` deserializes it into plain C# DTOs. Collections use JSON arrays; `customBalance` is the exception, a named numeric object map. Its DTO stores numbers as `double` to preserve JSON numeric precision until validation; integer-backed native balance values are bounded to `16,777,216` because the game's calculation widens them to `Single`.

## Shape

- `visualVariants[0]` is the Level 1 primary chassis. Later entries are Level 2 tactical variants and may declare up to four visual-only `modules`, each with a canonical `slotId` (`ERA`, `APS`, `Sponsons`, or `FuelDrums`) and `assetName`. Module prefabs are loaded as `GameObject`s from the owning unit's `assetBundleId`; each module mounts under the corresponding `RCL_AttachmentGroup_<Type>` beneath the existing `Socket_Hardpoint_<Type>`. A variant may declare at most one module per slot. Modules add no gameplay behavior, and omitted slots retain the Level 1 chassis geometry. The authoring-only `content/source-art/canonical-visual-module-roles.json` maps generated modules to explicit visible engineering roles, separately from their PBR presets. The canonical Unity build refreshes the four existing Level 1 prefab hierarchies, preserves authored FBX sockets and AnimatorControllers, and fills a missing canonical hardpoint only as a fallback before validating uniqueness and pack bindings. Bundle references can be checked with `tools/validate_content_bundle_references.py`.
- The authoring manifest `content/source-art/canonical-visual-modules.json` is generated separately from pack `content.json`. It uses schema 3, pins the schema-2 geometry-role registry and build-policy SHA-256 values, records exactly one module entry per unique `(variantId, slotId)`, and carries the geometry preset/layout plus Blender face and Unity triangle counts per LOD. Pack declarations still contain only the runtime slot and prefab address. Current Level 2 build outputs can carry `geometryBudgetStatus: UNCALIBRATED_USER_AUTHORIZED` while the explicit policy in `content/source-art/canonical-visual-module-build-policy.json` remains enabled; this status is not a performance claim.

- `roles` balance overrides set comma-separated native `UnitRole` flags; `offeredSystemTags` set comma-separated native `SystemTags` flags. They enable native themed reward filters and role/tag-driven hacks or upgrades, while excluding product `Factory`/`All` and preserving both factory roles on production buildings.

- `sample-native-expansion` currently declares visual overlays for all nine products: Sandlance (`PCXBaseTank/Vehicle`), Skirmish Bike (`BombBike/GameObject/BombBike`), Colossus (`ArtilleryTank/Tank`), Ironclad, Atlas, Bastion, and Arachnid (`GatlingWalker/RoboMarine_Mesh`), Overlord (`PCXBaseTank/Vehicle`), and Surge (`LightningTank/PCX TankHolder`). Arachnid and Overlord factories also have visual overlays, and all nine products declare their own cardImageAssetName Sprite. The Ironclad prefab has serialized Animator and Avatar/controller references; its authoring builder validates the source rig and animation-clip inputs. Static root/bundle inspection does not prove runtime animation compatibility, resource selection, or rendering in a Steam match. Overlays replace renderers under the selected native root and do not retarget automatically. A unit may opt into `nativeRigRetarget.mappings[]` (1–64 exact sourcePath/targetPath pairs, relative to the native and visual prefab roots) to copy bind-relative local position and rotation deltas; scale and root motion are excluded, death suspends copying, and pooled reuse restores bind poses. Ironclad's eight paths resolve statically; a live Profile2 check observed 8/8 bound mappings and matching target poses for two leg roots during locomotion. Aim/fire, death, and pooled reuse remain unverified in-game.

Run `.venv\Scripts\python.exe tools/validate_native_visual_roots.py content/packs/sample-native-expansion` before installing a native overlay pack. The tool checks the game's version, assembly SHA-256, MVID, and `resources.assets` SHA-256, then resolves every path through unique direct children and requires a Renderer below the target. It selects a uniquely named scripted GameObject in that serialized file and rejects ambiguous candidates; runtime resource selection still requires a Steam test. Install its pinned Python dependencies from `requirements.txt`.

- `packId`, bundle IDs, unit IDs, stage IDs, AI profile IDs, and module IDs use `namespace:local-id` form. All content IDs must be unique across every installed pack; the loader disables packs that claim the same ID.
- `assetBundleManifestPath` points to the Unity manifest AssetBundle produced alongside content bundles. A data-only pack may set `assetBundles` to `[]` and omit the manifest path. Otherwise, every bundle must be listed; the loader checks exact names against the Unity manifest, computes dependency order from `GetDirectDependencies`, and loads dependencies first. A dependency missing from the declared list disables the pack. A pack may declare up to 128 bundles; each bundle path uses `bundles/<lowercase-name>.bundle`, and bundle names cannot be Windows device names such as `con`, `nul`, or `com1`.
- Bundle asset references use the exact address emitted by Unity's AssetBundle, preferably the project-relative path including its extension. A unique filename stem is also accepted by Unity but can select an unintended asset when names collide; the runtime loader requests the declared Unity type so a Sprite can be selected when the texture file also exposes a Texture2D. Run `tools/validate_content_bundle_references.py` with the pack directory to check declared addresses against the bundle's serialized entries before launching the game.
- A unit can use a custom bundle source (`assetBundleId`, `balanceAssetName`, `prefabAssetName`, and `cardImageAssetName`) or `nativeTemplateId`. The loader clones a custom prefab under an inactive holder, sets the clone's root `EntityController.entityId` to the unit's namespaced ID, and registers only that clone; it leaves the AssetBundle prefab unchanged. The native mode copies a registered balance row, makes a dormant runtime clone of the native prefab, changes its root `EntityController.entityId` to the new namespaced ID, and routes that row to the clone. It reuses the native card image unless `cardImageAssetName` supplies a replacement Sprite from a bundle. For a custom model, a native-template unit may also set `visualAssetName` and `visualRootPath` and provide `assetBundleId`. The visual asset must be a `GameObject` prefab with at least one enabled Renderer and no missing or `MonoBehaviour` components. The relative Transform path must identify exactly one native subtree containing renderers; the loader disables those renderers and attaches an instance of the bundle prefab while keeping the native gameplay hierarchy and components. Renderers without materials inherit the first native `MeshRenderer` material found in the target subtree; authored bundle materials are kept. The model prefab root is placed at local position zero and unit scale; its yaw is corrected by 180 degrees only when the unique `Socket_MuzzlePrimary` is clearly behind `Socket_EngineAudio` (or the `Socket_DamageSmoke` rear-anchor fallback). Missing, ambiguous, or nearly coincident direction sockets retain identity rotation. Place offsets and scale in the child hierarchy. This adds custom geometry; an optional nativeRigRetarget can bridge selected native poses to the custom rig. Declare 1–64 exact {sourcePath,targetPath} mappings, with source paths relative to the native prefab root and target paths relative to the visual prefab root. Both paths must resolve uniquely; target paths must be unique, and sources cannot equal or contain visualRootPath. After Animator evaluation the loader applies each source bind-relative local position and rotation delta to the target bind pose, compensating for the overlay scale on position. It leaves scale and root motion unchanged, leaves unmapped target bones under Animator control, suspends retargeting for death, and restores bind poses on pooled reuse. The current live locomotion check does not cover weapon aim/fire, death, or pooled reuse; verify those behaviors separately. `visualAssetName` and `visualRootPath` must be declared together and only with `nativeTemplateId`. A native unit can use the same bundle for its model and optional card Sprite. Units can declare `isAllowedAsStartingBlueprint: true` to enable the dedicated Stage 1 starting-deck filter. The loader mirrors this flag and the product''s isAllowedForDemo setting to the factory row because the global no-pool fallback filters the factory candidate before resolving its product; the produced-unit row remains ineligible as a normal blueprint reward. Stage `unitIds` name produced units, while the loader adds their dedicated production buildings to the applicable randomized blueprint pools. `isAllowedForAi: true` allows AI rosters to field the produced unit, `isAllowedForDemo: true` enables demo mode eligibility, and `isForSpecialists: true` enables specialist archetype pools. `balanceOverrides` changes allowlisted numeric fields using invariant-culture strings, as well as enums: `rarity` (`Common`, `Rare`, `UltraRare`), `attackType` (`None`, `Single`, `AoE`, `Multi`, `MultiAoE`, `Beam`, `Artillery`, `MultiArtillery`), and `skillType` (`None`, `Toggle`, `Location`, `LocationAoE`, `LocationMultiTargets`, `Self`, `SelfAoE`, `Target`, `TargetMultiTarget`, `TargetAoE`, `SelfMultiTarget`); non-allowlisted fields cannot be changed this way. Native template IDs resolve against the exact game registry at package load. Every added unit also requires a `productionBuilding` with its own namespaced ID, a registered native factory template, optional balance overrides, and an `en-US` building localization. The building ID must differ from its product ID and cannot be shared by another added unit. Stage `unitIds` continue to name produced units; the loader offers their dedicated buildings in the applicable random blueprint pools. AI `unitEntries` also name the produced unit ID.
- A stage can use a custom bundled `StageParametersScriptableObject` (`assetBundleId` and `assetName`) or a loaded native template (`nativeTemplateId`). The loader clones the ScriptableObject before applying the bounded numeric and boolean `parameterOverrides`, so it does not edit the original asset. A native stage variant reuses its template's world references and can list new unit/AI IDs. It may also declare `landscape` to clone a named native `LandscapeGenerator`, set its seed and odd map size, and localize its display name. The loader exposes that generator only while the matching stage's `LevelProgression.CreateLevelMap` runs, then restores progression and AI map lists. `blueprintPoolEntries` adds products to explicitly named native pools: the `poolId` must occur in the cloned template's `BlueprintPoolList`, and each entry accepts 1–64 unit IDs. Native unnamespaced IDs must be registered; namespaced mod IDs must be declared in the pack. The loader clones only each targeted pool and preserves its existing blueprint list and native filter. For a pool named by one or more entries, its mod allowlist is the union of those entries' products, mapped to their factories; it does not inherit `stage.unitIds`. Pools not named by an entry retain the broader `stage.unitIds` eligibility. This scopes factory eligibility to the matching native pool class without weakening the native filter. Mod product IDs in an entry need not be repeated in `stage.unitIds`. For generic rewards without a pool ID, factories declared by either `stage.unitIds` or any pool entry remain stage-eligible. The generated aliases and allowlists are scoped to the specific cloned pool and stage. The loader does not add new native pool definitions or remove native products. This is static implementation and local validation, not proof of in-game offers.

  `parameterOverrides` also accepts `startCountRange` as an invariant-culture `"minimum,maximum"` pair. Both values must be ordered integers from 1 through 64; at runtime the maximum must also fit the final `maxWidth`. This maps to the native `StageParametersScriptableObject.startCountRange` (`Vector2Int`) read by `LevelProgression.CreateLevelMap` when choosing the initial map width. JSON Schema checks the pair syntax; package validation enforces ordering and bounds. This does not prove stage generation in a Steam run.

- Besides `movement.moveSpeed`, the top-level unit fields `combatValue`, `gridSize`, `cardModelScalingFactor`, `coinsAmount`, and `firePointCount` are applied to same-named fields on the copied native balance row. The pinned assembly was rechecked for Rogue Command 1.0.9 / Unity 2021.3.45f2 / the recorded `Assembly-CSharp.dll` SHA-256 and MVID; static metadata confirms types `int`, `int`, `float`, `int`, and `int`, respectively. This confirms field shape and loader wiring only, not gameplay meaning or design maxima. Their current schema/validator limits are loader contract guardrails; `cardModelScalingFactor` bounds are checked after conversion to binary32, so the JSON Schema uses exact rounding thresholds to stay aligned with runtime validation. Raw `balanceOverrides` that also set one of these structured fields are rejected to avoid silent precedence.
- Structured unit definitions also accept native-grounded fields: `copyUpgradesToEntityId` (string identifier for upgrade copying), `roles` (array of native `UnitRole` bitmask strings), `attackType` (`AttackType` enum), `skillType` (`SkillType` enum), `rarity` (`Rarity` enum), `tech` (`Tech` enum), `neededExperienceLevel` (integer 0–100 meta-progression requirement), `systemTags` (array of native `SystemTags` bitmask strings, max 16), and `baselineStatusEffects` (array of native `StatusEffect` strings, max 16). The loader reflects these onto the unit's native balance row. Furthermore:
  - For `copyUpgradesToEntityId`, the loader automatically configures `copyUpgradesToEntityId` on each unit's dedicated `productionBuilding` balance row to point to `product.id`. In native Rogue Command, `AssignUpgradeToCardView` checks `EntityBalancingStore.EntityIdUpgradesAreBeingCopiedTo(entityId)` when a card upgrade is chosen for a building; without this link, upgrades picked for a factory card never transfer to its produced units.
  - For `systemTags`, the loader translates the string array to the native `SystemTags` bitmask and populates `row.offeredSystemTags`, automatically propagating the tags to the unit's dedicated factory row as well. In native Rogue Command, `RelicBalancingStore.NeededSystemTags` requires `(OfferedSystemTags(entityId) & neededTags) != 0` for relic offers; this propagation ensures matching archetype relics (Mechs, Armor, Tanks, Laser, AoE) are offered during roguelike runs. Redundant raw `balanceOverrides` defining `offeredSystemTags` when `systemTags` is present are rejected during validation.
- Unit sub-definition objects (`movement`, `detection`, `weapons`, `armor`, `healing`, `stealth`, `command`, `experienceReward`, `aura`, `resourceGathering`, `aiBehavior`, `spawn`, `productionQueue`, `resourceCosts`, `statusEffects`, `synergies`, `upgrades`, `experience`, and `visualVariants`) are schema-backed DTOs and package-validated. `aiBehavior.unitCap` and `aiBehavior.combatUnitCap` are loader-owned per-product AI production caps (living plus queued instances; `combatUnitCap` is the stricter cap for products carrying a native combat role) and do not mutate the native global `UnitCap`. `aiBehavior.skillActivationCheckInterval` is a per-entity bridge around the native `EntityController.FixedUpdate` skill gate; `0` preserves the native 0.25-second cadence. Caps are bounded to 32 and intervals to 60 seconds. Positive `movement.moveSpeed` maps to the native per-unit `MoveSpeed` row field. `customBalance` accepts only exact `EntityBalancingStore.ChangeableValue` members that have a per-unit `EntityBalancingParameters` field; its schema lists these row-backed keys and their native integer/float storage kinds; JSON Schema and package validation cap integer-backed `customBalance` and raw `balanceOverrides` values at `16,777,216`; float-backed keys remain bounded to `Single.MaxValue`. The DTO preserves JSON integer precision as `double`, but validation rejects integer-backed balance values above `2^24` to avoid native `Single` rounding. `ManaRechargePerSecond` is intentionally excluded because the pinned game reads it from global `GameBalancingStore`, not the unit row. Package validation rejects unknown aliases, negative or non-finite values, values beyond native `Single` range, and fractional or out-of-range values for integer-backed keys. Balance overrides are written into the incoming unit row before registration, not seeded into the final-value caches; native cache invalidation can therefore recalculate from the row and continue to apply relic/card changes. Precedence is `customBalance` over raw `balanceOverrides`, then structured `movement.moveSpeed`. Other sub-definition fields remain descriptive/validated until a type-specific native or compiled-runtime mapping is implemented. The exception `aiBehavior.aiTag` is reapplied to each attached or pooled-reused unit through the verified native `EntityController.AddAiTag(string)` path. The loader-owned cap/cadence bridge is implemented and does not claim that the native global cap is per-unit. The typed DTO validator rejects NaN and infinities across floating-point unit sub-definition fields while retaining each field's schema-aligned range; signed active-skill effect values and status-effect magnitudes remain permitted, as does visual variant scale zero. Category strings without a schema- or native-backed enum remain open rather than receiving guessed allowlists; `armor.armorValue`, `armor.shieldValue`, and `armor.damageReduction` do not map to native armor stats because `MaxArmor` and `ArmorProtection` have distinct semantics. Armor values must be finite and non-negative; damage reduction must be finite and within `[0,1]`. Do not infer gameplay effects from schema acceptance alone. Raw native field overrides continue to use `balanceOverrides`.
- Optional `units[].variants` declares up to eight runtime-selectable loadouts per unit. Each entry uses a local lowercase ASCII `id` (unique within its parent unit), an optional `loadout` label of `AP` or `HE`, and optional `balanceOverrides` using the same raw row-field names and validation contract as unit overrides (for example, `damage1`, not `Damage1`). The content loader registers these definitions with the runtime variant registry when it registers the unit mechanics profile. `diagnostics_order_apply_unit_loadout` selects a declared variant by its ID: the AP/HE preset is applied first, explicit supported instance fields (`damage1`, `attackCooldown`, `moveSpeed`, `weaponRange`, `maxHealth`, `maxArmor`, `armorProtection`, `maxShield`, `healAmount1`, `sightRadius`, `effectRadius1`, or `skillRange`) override that preset as absolute values converted to native relative modifiers, and switching variants replaces stale modifiers. `Standard` clears the active variant. Unsupported row-only fields fail closed rather than silently mutating the wrong native value. This runtime path does not itself create a natural blueprint offer, alter AI selection, or retarget visuals/VFX; those remain separate acceptance steps requiring their own evidence.
- Offline stage preflight follows the game's map candidates: AI `restrictedToMaps`, then the selected World `restrictedToMaps`, then the progression's available maps. A stage-owned `landscape` is added to those candidates only for its stage during map creation. Because `CreateLevelMap` samples from those lists directly, every in-range AI/World restricted map must be active and demo-allowed when applicable. A selected map must have a non-null `restrictedToWorlds` list because the game reads its count. When that restriction is used, every entry must be non-null and active, and demo generation requires every entry to be demo-allowed; the game may choose any listed World for generation. An empty list means no restriction. These checks model the pinned game build and are not in-game runtime proof.
- An AI profile can use a custom bundled prefab or `nativeTemplateId`, which names a loaded native `DefaultAiBehaviour` GameObject exactly. A native-template profile must set `scopeToReferencedStages: true`: the loader clones the native GameObject under a generated `RCL_` resource name, binds compiled modules only to that clone, leaves the original template unchanged, and sets the clone's stage range to the contiguous indices of stages in the same pack that reference the profile. Unscoped native profiles are rejected so modules cannot alter every use of the shared native AI. This range is calculated against the active progression lists after previously loaded content, so packs do not need to hardcode stage indices. A scoped profile must be used by at least one stage in its declaring pack; another pack cannot widen its scope, and referenced stages must be contiguous. A forced stage-12 Steam diagnostic observed a scoped sample AI clone attached to `DefaultAiBehaviour`; it did not exercise ordinary progression or prove the range across all stages and packs. Custom AI prefabs must start with reserved prefix `RCL_`, be active, contain exactly one `DefaultAiBehaviour`, and have `AiBehaviour.isDeactivated` set to `false`. Rogue Command tags the spawned instance `AI` before its first `Update`; modules execute in manifest order after initialization and each native update, then clean up in reverse order. If a module fails, the loader disables that stack and attempts cleanup for every module. Each module declares `moduleId` and string-pair `parameters`; the registered module validator defines their types and meaning. `sample:scheduled-base-build` uses the game's `DefaultAiBehaviour.CreateBuilding_Static` helper to build a building already present in the active native deck, with cooldown and finite attempt/success limits. `sample:reactive-base-defense` watches for player units attacking an AI building by default; optional `protectedTargetType` adds AI Harvesters or includes both targets. It limits each response by AI base, defense radius, cooldown, and responder count; an optional unitId parameter restricts responses to that exact roster unit. It does not add a native AI deck or change the map layout. `sample:harvester-raider` detects player crystal-gathering harvesters across the map, selects free combat troops from an AI base, and dispatches tactical raid squads directly to attack the player's economic gatherers, enforcing scan intervals, minimum game time, raid squad size, and cooldown. `sample:siege-assault` scans known player buildings or refineries, gathers combat troops from an AI base, and dispatches coordinated assault strike waves to dismantle player fortifications, with configurable target priorities (`ClosestBuilding`, `Refinery`, `Hq`).
- Bundle paths use exactly `bundles/<lowercase-name>.bundle` with forward slashes so the loader, schema, and Unity builder share one format. The validator rejects rooted paths, traversal segments, missing files, reparse points, and DLL files anywhere in the package. Manifest bundles are limited to 64 MiB; each content bundle is limited to 512 MiB; loaded content bundles are limited to 2 GiB across all packs in one game session. The Unity builder applies the per-pack content-bundle limit. It builds chunk-compressed LZ4 output; these on-disk limits do not guarantee peak memory use for externally produced LZMA bundles, which Unity decompresses in memory. The loader keeps each content bundle's input stream open until its AssetBundle is unloaded, as required by Unity for uncompressed or chunk-compressed bundles.

`sample:reactive-base-defense` accepts optional `unitId` (validated game entity ID), `baseIndex` (integer 0–31), `cooldownSeconds` (finite 1–60 seconds), `defenseRadiusCells` (finite 1–64 map cells; default 12), `maxResponders` (integer 1–4; default 2), and `protectedTargetType` (`Buildings`, `Harvesters`, or `BuildingsAndHarvesters`; default `Buildings`). Only live AI-controlled buildings at the configured base are protected by default. When Harvester protection is selected, a target must be a live AI-owned unit with the native `Harvester` role at that base. It reacts to eligible Player units with attack and attack component enabled and a current attack target matching the selected target type, provided the attacker is within `defenseRadiusCells` of that target. Weapon-range state is not required, so units advancing under a valid Attack order can trigger a response before their weapon is in range. The attacker must also be targetable by AI and not carry the `canNotBeAttacked`, `canNotBeTargetedByAi`, or `doNotRegisterGlobally` exclusions. It chooses nearby defenders with enabled movement and attack that are idle or moving, excludes units with pending skill commands or paused command chains, and counts naturally engaged or previously dispatched units toward the cap. It inserts a finite attack-move-to-position command before the selected unit's idle/move order, which the game resumes after that response reaches its destination. Both current native-template sample packs set `scopeToReferencedStages: true`; static inspection found their source AI templates only accept stages 0–1, while appended stages begin at index 9. The loader now derives an isolated alias range for the referenced stages instead of mutating the shared native template. A Steam diagnostic on a forced stage-12 path observed map generation, the scoped profile attachment, and a Skirmish Bike in AI base 0's roster. It did not verify a naturally selected stage, a randomized unit offer, factory production, the model in combat, or the reactive counterattack; those behaviors remain unverified in-game.

`sample:harvester-raider` accepts `baseIndex` (integer 0–31), `scanIntervalSeconds` (finite 2–120 seconds; default 10), `cooldownSeconds` (finite 5–300 seconds; default 25), `raidSquadSize` (integer 1–8; default 3), and `minGameTimeSeconds` (finite 0–600 seconds; default 15). It searches `ExistingControllers.Instance.Harvesters` for active player gatherers, selects idle or moving AI combat units stationed at the specified base, and issues attack-move commands directly to harass or eliminate harvesting operations.

`sample:harvester-escort` (`HarvesterEscortAiModule.cs`) watches AI harvesters from the configured base, selects the nearest threatened harvester, and dispatches up to `maxEscorts` idle combat units with tagged attack-move commands. Parameters: `baseIndex` 0–31 (default 0), `escortRadiusCells` 1–64 (default 8), `scanIntervalSeconds` 1–60 (default 5), `maxEscorts` 1–16 (default 3), and `engageAttackers` (default true).

`sample:siege-assault` accepts `baseIndex` (integer 0–31), `cooldownSeconds` (finite 10–300 seconds; default 30), `minSquadSize` (integer 1–12; default 4), `targetPriority` (`ClosestBuilding`, `Refinery`, `Hq`; default `ClosestBuilding`), and `minGameTimeSeconds` (finite 0–600 seconds; default 30). It scans known player buildings (`PlayerBuildingsKnownToAi`, `PlayerRefineries`, or `EngineeringHq`), gathers combat units from the specified AI base, and dispatches coordinated assault strike waves to dismantle player fortifications.

`sample:patrol-and-intercept` (`PatrolAndInterceptAiModule.cs`) performs periodic, bounded waypoint dispatch and interception. Its waypoint candidates are live AI-building `CurrentCell`/`Position` values for the configured base; eligible attackers must target a live AI building at that base and be within `interceptRadiusCells`, and responders must be within `patrolRadiusCells` of the relevant building. Each scan issues at most `maxPatrolUnits` commands total, prioritizing intercepts. Parameters: `baseIndex` 0–31 (default 0), `patrolRadiusCells` 5–32 (default 15), `interceptRadiusCells` 5–32 (default 10), `scanIntervalSeconds` 2–60 (default 8), `maxPatrolUnits` 1–8 (default 3), and optional `patrolMode` (`AttackMove`, default; or `Native`). The default retains tagged `AttackMoveOnPosition` waypoint orders for idle or moving defenders. `Native` selects up to eight distinct, in-radius building cells nearest-first, clears and seeds the controller's patrol positions, then issues the native `EntityCommand.Patrol(0)` command with the module tag only when the current command is `DoNothing` and the private native command queue is verified empty. The pinned `EntityController._commandChain` field is read reflectively and the feature fails closed if that field or its `LinkedList<EntityCommand>` type cannot be verified. This prevents native patrol from stranding any queued command, including a `Move` hidden behind `DoNothing`. Intercepts remain attack-move commands and share the same per-scan cap. Static inspection of the pinned 1.0.9 assembly confirms `EntityCommand.Patrol(int)`, the route-position methods, the queue field, and the patrol command's iteration behavior. Local harness tests use value-type `EntityCommand` and command-queue fixtures and cover queued-Move preservation. No in-game run has verified reflection against the shipped assembly, pathing, route motion, or command resumption; do not present local build/validation as runtime evidence.

`sample:focus-fire` (`FocusFireAiModule.cs`) clusters mobile AI combat units and coordinates attacks against a nearby player target. Parameters: `baseIndex` -1–31 (default -1, meaning any base), `checkIntervalSeconds` 0.5–30 (default 3), `squadRadius` 5–50 cells (default 18), `minUnitsInSquad` 2–12 (default 2), `priorityMode` (`LowestHealth`, `HighestThreat`, or `TargetRole`; default `LowestHealth`), and optional `targetRole` (a supported native `UnitRole` name; default `None`). A `targetRole` is required only to give a role preference; an unknown role is rejected during parameter validation.

`sample:construction-expansion` (`ConstructionExpansionAiModule.cs`) maintains the configured count of one building type for an AI base by adding it to the native construction queue when affordable. Parameters: `baseIndex` 0–31 (default 0), `checkIntervalSeconds` 2–120 seconds (default 12), `buildingId` (non-empty native or registered building ID; default `PCXTurret`), `desiredCount` 1–10 (default 2), `queuePriority` 1–100 (default 10), and `minSurplusCrystals` 0–5000 (default 150). The AI balance must cover the building cost plus the configured surplus; an already queued instance is not enqueued again.

`sample:tactical-regroup` (`TacticalRegroupAiModule.cs`) orders mobile AI combat units below a health threshold to fall back toward the base perimeter; it releases its retreat tracking when the unit heals or has enough nearby eligible reinforcements. Parameters: `baseIndex` -1–31 (default -1, meaning any base), `checkIntervalSeconds` 1–30 (default 4), `healthRetreatThreshold` 0.10–0.60 (default 0.30), `regroupMinSquadSize` 2–10 (default 3), and `regroupDistanceToBase` 5–50 cells (default 20). Reinforcements must be mobile combat units from the same AI base. When the retreat ends, the module cancels only its own tagged retreat command, preserving a newer command from another module.

`sample:squad-formation` (`SquadFormationAiModule.cs`) groups AI escort units around heavy anchor units (Captains) using native `EntityController.SetCaptain` and `RemoveCaptain`. Followers converge around the captain in tactical flocking formations and are released on captain destruction or module disposal. Parameters: `anchorRole` (`Tank`, `Mech`, `Defender`, `FrontalAttacker`, `Unit`; default `"Tank"`), `maxFollowersPerCaptain` 1–16 (default 4), `formationRadiusCells` 1–64 (default 10), `cooldownSeconds` 0.5–60 (default 5), and `baseIndex` -1 to 31 (default -1).

`sample:tactical-skill-activation` (`TacticalSkillActivationAiModule.cs`) monitors AI combat units with active skills and executes them (`ActiveSkillWithoutTarget`, `ActiveSkillOnTarget`, or `ActiveSkillAtPosition`) using `EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand` when enemies enter `enemyTriggerRadiusCells`. Parameters: `enemyTriggerRadiusCells` 1–64 (default 10), `minManaRatio` 0.0–1.0 (default 0.5), `cooldownSeconds` 0.5–60 (default 4), and `baseIndex` -1 to 31 (default -1).

`sample:wave-escalation` (`WaveEscalationAiModule.cs`) drives match escalation and wave progression by incrementing `AiBehaviourManager.CurrentAttackWaveNumber` via `AiBehaviourManager.IncrementAttackWaveNumber_Static()` at configured wave intervals, coordinating scalable assault strike waves against player defensive structures or HQ. Parameters: `waveIntervalSeconds` 5–300 (default 60), `maxWaves` 1–100 (default 10), and `baseIndex` -1 to 31 (default -1).

`sample:threat-prioritization` (`ThreatPrioritizationAiModule.cs`) re-evaluates AI combat target focus on a configurable cadence, prioritizing high-threat enemies (`HighestThreat`), wounded units (`LowestHealth`), or high-damage dealers (`HighestDps`) within engagement radius using hysteresis to prevent target jitter. Parameters: `baseIndex` -1 to 31 (default -1), `checkIntervalSeconds` 0.5 to 30 (default 3), `priorityMode` (`HighestThreat`, `LowestHealth`, or `HighestDps`; default `HighestThreat`), `engagementRadius` 5 to 120 (default 35), `retargetThreshold` 1.0 to 3.0 (default 1.25), and `maxRetargetsPerTick` 1 to 32 (default 8). All shipped AI profiles include this module by default; future AI profiles should also include it as the last module unless the eight-module limit prevents it. Targeting is specialized per allied unit role via `ThreatPrioritizationRoleProfile` (`ThreatPrioritizationRoleProfile.cs`): sniper/scout roles extend engagement range and tolerate distance, melee/tank/frontal roles close range and penalize distance, mass-destruction units keep a heavy single-target focus, and pure support/healer units (`Supporter`/`Heal`) are never retargeted onto enemy combat targets. The profile is pure and testable offline and never mutates control or hostility flags.
- Include empty arrays when a section has no entries. The validator reports package-local diagnostics and does not mutate the registry.

Production-building visuals may also specify `assetBundleId` and `cardImageAssetName`. `assetBundleId` is required when either visual field or `cardImageAssetName` is set; that Sprite address supplies the factory's reward-card image. `visualAssetName` plus `visualRootPath` remain optional, paired overlay fields. A shared building bundle can provide both.



## Standalone Buildings and Structured Building Parameters

Packs can declare standalone buildings (`buildings[]`) and structured parameters on `productionBuilding` without requiring dummy units or opaque raw overrides:

- **Standalone Buildings (`buildings[]`)**: Declares stationary defense posts, turrets, spires, or support structures directly constructed by the player on the placement grid (`Grid1x1` or `Grid2x2`):
  - `id`: Unique namespaced identifier (`namespace:local-id`).
  - `nativeTemplateId`: Registered native building template (e.g. `MachineGunTurret`, `LaserCannonTurret`, `HealingSpire`, `AdvancedArmory`, `WallBlock`).
  - `gridSize`: Placement footprint integer (1 for 1x1, 2 for 2x2).
  - `maxCapacity`: Maximum simultaneous active instances on the field (0–100); enforces native `PlaceBuildings.CanBuildingBePlaced` limit.
  - `cost`: Crystal build cost (non-negative integer).
  - `buildTime`: Construction duration in seconds (non-negative finite float; maps to native `productionDuration`).
  - `combatValue`: Threat/combat weighting value (non-negative integer).
  - `rarity`: Card tier (`Common`, `Rare`, `UltraRare`).
  - `tech`: Tech tree alignment (`None`, `Colorless`, `Mech`, `Tank`, `Ancient`, `Specialist`, `All`).
  - `neededExperienceLevel`: Player profile meta-progression unlock tier (0–100).
  - `roles`: Bitmask array of native `UnitRole` strings (must include `Building`).
  - `systemTags`: Bitmask array of native `SystemTags` strings (up to 16 flags, e.g. `Turret`, `Armor`).
  - `cardModelScalingFactor`: Scaling factor for reward card 3D model render preview (0.01–100.0).
  - `firePointCount`: Number of weapon muzzle points (1–16).
  - **Combat & Weapons**: `damage1`, `attackCooldown`, `weaponRange`, `damage2`, `attack2Cooldown`, `attackType` (`None`, `Single`, `AoE`, `Multi`, `MultiAoE`, `Beam`, `Artillery`, `MultiArtillery`).
  - **Skills & Auras**: `skillType` (`None`, `Toggle`, `Location`, `LocationAoE`, `LocationMultiTargets`, `Self`, `SelfAoE`, `Target`, `TargetMultiTarget`, `TargetAoE`, `SelfMultiTarget`), `skillRange`, `skillManaCost`, `healAmount1`, `healAmount2`, `duration1`, `duration2`, `effectRadius1`, `effectRadius2`.
  - **Blueprint Eligibility**: `isAllowedAsBlueprint` (defaults to `true`), `isAllowedAsStartingBlueprint` (defaults to `false`), `isAllowedForAi` (defaults to `true`), `isAllowedForDemo` (defaults to `true`), `isForSpecialists` (defaults to `false`).
  - **Visuals & Localization**: Optional `assetBundleId`, `cardImageAssetName`, `visualAssetName`, `visualRootPath`, optional `nativeRigRetarget`, `balanceOverrides`, and localized strings (`localizations[]`).

- **Structured `productionBuilding` Parameters**: Production buildings assigned to units (`units[].productionBuilding`) now accept typed fields directly:
  - `gridSize` (1 or 2), `maxCapacity` (0–100), `cost` (integer >= 0), `buildTime` (float >= 0), `combatValue` (integer >= 0), `cardModelScalingFactor` (0.01–100.0), `rarity`, `tech`, `neededExperienceLevel` (0–100), `roles` (preserves `Building` and `Factory`), and `systemTags` (offered tags for archetype relic synergy).
  - `neededExperienceLevel` and `rarity` must match between the unit and its production building to preserve profile unlock consistency. Redundant raw `balanceOverrides` duplicating these structured fields are rejected.

- **Unit `maxCapacity`**: Units (`units[]`) also accept `maxCapacity` (0–100) to cap maximum simultaneous units fielded at runtime.

## Unit & Building Behavioral Controls, Anti-Hitch Pre-Spawning, and Audio Safeguards

Packs can declare advanced behavioral parameters, zero-allocation pre-spawning pools, and spatial audio/VFX hierarchy bindings on units (`units[]`) and standalone buildings (`buildings[]`):

- **Tactical Behavioral Injections**:
  - `rotationDegreesPerSecond`: Angular rotation speed in degrees per second for steering and turret traverse (finite float, 10.0-720.0). Maps to `EntityController.rotationDegreesPerSecond`.
  - `collisionRadius`: Separation avoidance and placement radius (finite float, 0.5-50.0). Maps to `EntityController.collisionRadius`.
  - `attackWarmUpTime`: Pre-attack charge delay before firing (finite float, 0.0-10.0 seconds). Maps to `EntityController.attackWarmUpTime`.
  - `canAttackWhileMoving`: Whether the entity can fire weapons while moving or must halt (boolean). Maps to `EntityController.canAttackWhileMoving`.
  - `shootAutomaticallyOnEnemiesWithinRange`: Autonomous target acquisition against hostiles within detection / weapon range (boolean). Maps to `EntityController.shootAutomaticallyOnEnemiesWithinRange`.
  - `defendPositionAggressivelyWhenIdle`: Guard behavior when idle, engaging encroaching enemies without manual player commands (boolean). Maps to `EntityController.defendPositionAggressivelyWhenIdle`.
  - `rangeToChooseNewTargetAutomatically`: Autonomous retargeting radius limit (integer, -1 to 1000 cells; -1 disables and relies on sight radius). Maps to `EntityController.rangeToChooseNewTargetAutomatically`.

- **Zero-Allocation Anti-Hitch Pre-Spawning Pool**:
  - `prespawnCount`: Pre-spawns and warms a stack of dormant instances in `EntityFactory.PrespawnedEntities` during level preparation (`EntityFactory.SetupPrespawning` and `SetupAiPrespawning`) (integer, 0-100).
  - Eliminates combat frame hitches (>= 50 ms) caused by synchronous AssetBundle / GameObject instantiation during dense waves or rapid player production.

- **Spatial Audio & Asset Auto-Wiring**:
  - `selectionSoundAssetName`: AssetBundle address for the unit selection confirmation clip. Injected into `EntityController.selectionSound`.
  - `engineSoundAssetName`: AssetBundle address for locomotion / engine hum loop. Injected into `EntityFlockingMovement._movementSoundAudioSource.clip`.
  - `weaponSoundAssetName`: AssetBundle address for primary weapon firing sound effect. Injected into `ShootProjectile.shotSoundAudioSource.clip`.

- **VFX & Muzzle Flash Hierarchy Safeguard**:
  - The runtime loader automatically inspects `EntityController.Events` for `ShootProjectile` actions across all dormant prefabs.
  - Native `ShootProjectile.Init()` requires every transform in `firePointsTransform` to possess a child with a `ParticleSystem` component (`child.GetChild(0).GetComponent<ParticleSystem>()`), or it wipes `_muzzleFlashEffects` and disables weapon muzzle flashes.
  - The loader's `EnsureMuzzleFlashHierarchy` detects missing particle systems under `Socket_MuzzlePrimary` / fire points and safely synthesizes dormant fallback `ParticleSystem` components, guaranteeing weapon VFX are preserved.

- **Runtime Unit Mechanics (`RuntimeUnitMechanicsDirector`)**:
  - `healing`: Autonomous field healing and armor repair pulses (`healAmount`, `healRange`, `healRate`, `repairAmount`) targeting the most damaged friendly unit or building within range via native `EntityController.Heal` and `EntityController.RepairArmor`.
  - `aura`: Passive tactical aura pulses (`auraRadius`, `auraType` in `Buff`/`Debuff`/`Heal`/`Shield`, `buffAmount`, `affectsAllies`, `affectsEnemies`) applying native `StatusEffect` buffs/debuffs (`CombatBoost`, `Robust`, `SlowedDown`, `Marked`), health restoration, or shield recharge (`EntityController.ChargeShield`) to nearby units.
  - `baselineStatusEffects`: Permanent passive `StatusEffect` flags (up to 16 values such as `Armored`, `Robust`, `Rush`, `Regen`) applied at entity instantiation and pool reuse via `EntityController.SetActiveStatusEffect(..., DurationType.Unlimited, 0f)`.
  - `combatBoosts`: Timed combat buffs (`id`, `statName`, `multiplier`, `duration`) triggered when the unit deals damage in combat, applying both visual status effects (`Turbo` for movement speed, `Overclocked` for attack speed, `Robust` for armor/defense, or `CombatBoost`) and exact non-stackable relative stat modifiers via `EntityController.AddSpecificValueChange(..., SpecificValueChange, isStackable: false)`.
  - `experience`: Combat veterancy progression (`experiencePerLevel`, `maxLevel`, `experienceGainMultiplier`, `levelUpBonus`) tracking combat hits and kills per entity instance, promoting the unit via `EntityController.RankUp(1)` and restoring `levelUpBonus * MaxHealth` health and shield upon promotion.
  - `synergies`: Proximity stat buffs (`id`, `statName`, `multiplier`, `radius`, `targetUnitIds`) pulsing every `1.0s` to friendly units matching `targetUnitIds` within `radius` via `EntityController.AddSpecificValueChange` and `StatusEffect.SquadEffect`. The `targetUnitIds` value `"*"` matches any friendly unit ID; it never includes enemies, and each pulse affects at most 12 units.
  - `stealth`: Autonomous out-of-combat cloaking (`stealthLevel`, `cloakDuration`, `detectionReduction`) applying native `StatusEffect.Stealth` (`MakeInvisible()`, layer 25, untargetable by AI) when the unit has not taken damage within the grace window.
  - `command`: Squad command aura (`commandRange`, `commandType` in `Move`/`Attack`/`Defend`/`Support`, `maxCommandChain`) binding nearby friendly units to the commander via `EntityController.SetCaptain(self)` and applying tactical stat buffs (`Damage`, `ArmorProtection`, or `MoveSpeed`).
  - `experienceReward`: Combat kill reward and bounty (`experienceAmount`, `experienceType`, `bonusMultiplier`) depositing crystals into the player's account via `Bank.Deposit("Player", amount)` when a player unit with `experienceReward` scores a kill or when an enemy unit with `experienceReward` is eliminated by the player.

## AI unit entries

An AI profile may optionally declare `unitEntries` to append namespaced units to one `AiBase` in an isolated, stage-scoped native profile. Each entry selects the `aiBases` index, the starting ascension level, the number of units spawned at base setup, and the native `maxLiving` value. The referenced unit must be declared by this pack or another loaded/declared pack; the loader resolves that dependency before loading the profile. Custom AI prefabs and unscoped templates cannot use `unitEntries`, so the native AI template remains unchanged. `unitEntries` may be omitted when unused; sample manifests include an empty array for clarity.

An AI profile may also declare `productionBuildingEntries` to add each referenced product unit's factory to one base in the same isolated, stage-scoped native profile. Each entry names the product in `unitId` and sets `baseIndex`, `startingInAscensionLevel`, and `maxLiving`; the loader resolves the product's registered `productionBuilding.id` and adds that factory to the AI deck with a starting count of zero. The product must set `isAllowedForAi: true`. A compiled module such as `sample:scheduled-base-build` can then target the factory ID in its `entityId` parameter. Cross-pack product references establish load order automatically. Custom AI prefabs and unscoped templates cannot use this field. Static inspection confirms the native deck uses `countPerStage` and exposes starting and maximum counts; construction of a mod factory in a Steam-launched match still requires runtime confirmation.

Custom AI prefabs may optionally declare `unitDependencies` for namespaced units referenced by serialized `AiBase.entitiesInAiDeck` entries. Cross-pack IDs must be declared here so the loader loads the owning unit pack first, independent of pack-directory order. Native game IDs and units declared in the same pack need no dependency entry. The loader rejects a custom prefab whose external deck IDs omit this declaration, even if another pack happened to load first. Dependencies are references only; they do not replace or edit native AI templates.

Static REA inspection of the pinned 1.0.9 assembly found that `AiBehaviour.BuildBaseStartSetup` creates each eligible `EntityInAiDeck` entry at its AI base. Non-building entries flow through `EntityFactory.InstantiateEntity` with the AI tag and receive the base index, so a mod unit added to the isolated profile can join that AI's starting roster. A Steam-launched diagnostic run on 2026-10-17 confirmed stage-map generation, attachment of the scoped sample profile, and a Skirmish Bike in AI base 0's roster. That run used a forced stage setup; it did not verify ordinary roguelike progression, the exact creation path for the observed unit, or a reactive counterattack. Treat the rest as unverified runtime behavior and see `docs/research/AGENT-HANDOFF.md` for the test boundary and next procedure.

## Unit localization

Units may declare `localizations` entries with a game's language ID, a visible `name`, and optional `description` and `skillDescription`. Custom-prefab units must provide an `en-US` name; native-template units may omit localizations and inherit the native template's name and description. If the active language has no pack translation, the loader falls back to `en-US`; additional supported game languages are `de-DE`, `ja-JP`, `ko-KR`, `zh-CN`, `zh-TW`, `fr-FR`, `es-XL`, `pt-BR`, and `ru-RU`. Duplicate or unsupported language IDs disable the package. The loader intercepts `Loca.BlueprintName`, `Loca.BlueprintDescription`, and `Loca.SkillDescription` for registered mod-unit IDs, so namespaced IDs resolve without editing the game's localization resources.

Landscape `localizations` use the same supported language IDs and require an `en-US` name. The loader intercepts `Loca.Global` only for generated landscape localization keys; all other game localization calls continue through the original method.

## Pack-authored relics

A pack may declare `relics` to add passive relic entries to the native relic balance store. Each relic uses a namespaced `id`, an `assetBundleId`, an `imageAssetName` that resolves to a Sprite in that bundle, native rarity/tech/system-tag enum values, an optional `coinsAmount` (granting bonus currency on acquisition, non-negative integer), an optional `inactive` flag (defaults to false), an experience-level threshold, demo/specialist eligibility flags, and localized names and descriptions. Provide an `en-US` localization; other supported languages such as `es-XL` are optional.

Each `cardChanges` entry defines a mutation using the following fields:
- `valueToChange`: Supported for all 26 canonical native `ChangeableValue` attributes: `MaxHealth` (and alias `Health`), `MoveSpeed`, `AttackDamage1`, `AttackRange1`, `AttackInterval1`, `AttackDamage2`, `AttackRange2`, `AttackInterval2`, `AttackDamage3`, `AttackRange3`, `AttackInterval3`, `HealingAuraHealthPerSecond`, `HealingAuraRadius`, `PassiveHealthRegenerationPerSecond`, `MaxShield`, `ShieldRechargePerSecond`, `ShieldRechargeDelayInSeconds`, `ArmorProtection`, `PassiveDamageReflectPercentage`, `PassiveDamageReflectDirectDamage`, `CoinsPerHarvest`, `HarvestTime`, `CoinsPerSecond`, `VisionRange`, `DetectionRange`, and `ManaRechargePerSecond`.
- `operation`: Calculation operator (`PercentAddition`, `AddValue`, `SubtractValue`, `PercentSubtraction`).
- `value`: Finite numeric delta/modifier.
- `side`: Target faction. `"False"` targets player units (`!IsAllowedForAi`), `"True"` targets enemy/AI units (`IsAllowedForAi`), and `"Irrelevant"` applies globally to both player and AI units.
- `onlyForTheseEntityIds`: (Optional) Explicit array of namespaced entity IDs to restrict the card change. When omitted or empty, the change applies broadly across eligible units meeting the role/tech/side conditions.
- `entityIdsNotAllowed`: (Optional) Array of namespaced entity IDs explicitly excluded from receiving this card change.
- `cardMustHaveAllOfTheseRoles`: (Optional) Array of native `UnitRole` bitmask strings; the target card must have every declared role.
- `cardMustHaveOneOfTheseRoles`: (Optional) Array of native `UnitRole` bitmask strings; the target card must possess at least one of these roles.
- `cardMustNotHaveOneOfTheseRoles`: (Optional) Array of native `UnitRole` bitmask strings; target cards with any of these roles are excluded.
- `cardMustNotHaveOneOfTheseTechs`: (Optional) Array of native `Tech` enum strings; target cards with any of these techs are excluded.
- `changeableValueHasToBeGreaterZero`: (Optional) Boolean guard requiring the base parameter value to be strictly positive (> 0) before applying the change.

Pack-authored relic changes must set `multiplyValueWithCurrentHeatLevel` to `false`; Heat scaling is rejected until the per-level balance budget is validated. Leave `multiplyValueWithMetaProgressionMultiplier` absent when the change should not scale with meta progression. Relic images are loaded as typed Sprites from the declared bundle, and localized names/descriptions use the active language with an `en-US` fallback.

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
      "isAllowedForAi": true,
      "localizations": [
        { "language": "en-US", "name": "Sandlance Scout", "description": "A fast reconnaissance tank." },
        { "language": "es-XL", "name": "Explorador Sandlance", "description": "Un tanque rápido de reconocimiento." }
      ],
      "balanceOverrides": [
        { "name": "cost", "value": "70" },
        { "name": "maxHealth", "value": "130" },
        { "name": "moveSpeed", "value": "1.1" }
      ],
      "productionBuilding": {
        "id": "example:scout-tank-factory",
        "nativeTemplateId": "PCXBaseTankFactory",
        "balanceOverrides": [
          { "name": "cost", "value": "500" }
        ],
        "localizations": [
          { "language": "en-US", "name": "Scout Assembly Plant" }
        ]
      }
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
      "unitEntries": [
        {
          "unitId": "example:scout-tank",
          "baseIndex": 0,
          "startingInAscensionLevel": 0,
          "countAtStart": 1,
          "maxLiving": 2
        }
      ],
      "productionBuildingEntries": [
        {
          "unitId": "example:scout-tank",
          "baseIndex": 0,
          "startingInAscensionLevel": 0,
          "maxLiving": 1
        }
      ],
      "modules": [
        {
          "moduleId": "sample:scheduled-base-build",
          "parameters": [
            { "name": "entityId", "value": "example:scout-tank-factory" },
            { "name": "baseIndex", "value": "0" },
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

`productionBuilding` can optionally replace the native factory's mesh with a visual-only prefab by declaring `assetBundleId`, `visualAssetName`, and `visualRootPath` together. The bundle ID must name an `assetBundles` entry; the asset reference must resolve to a script-free GameObject prefab with an enabled Renderer; and the path must identify exactly one subtree in the cloned native factory. The loader hides only `MeshRenderer` and `SkinnedMeshRenderer` components in that subtree, then attaches the authored visual at its origin, so native factory roles, particles, lights, and other components remain. Choose a subtree that contains only the intended factory geometry: mesh renderers such as native build indicators inside the selected subtree are hidden too.

## Profile-level card unlocks

`neededExperienceLevel` on a unit is the minimum persistent profile experience level required before its card can enter reward candidates. The game adds persistent profile experience when the run ends: losing a run awards experience from previously won encounters and minibosses, while a full victory also adds the run-win bonus. Clearing an intermediate stage advances the run but does not immediately change persistent profile experience. The profile level is not the current stage index, map node, or run progression counter. The package validator requires the unit and its `productionBuilding` to each declare the same `neededExperienceLevel` and `rarity`; some reward paths inspect the factory row, while stage pools validate the associated product row.

An unlock makes the card eligible for randomized offers once the profile reaches its threshold. It does not guarantee that the card appears in the next offer. In particular, the first selection at stage 0 is constrained to vehicles or small units regardless of the chosen starting map node; validate later offers against their actual pool and the profile's current level.

Every added product should explicitly set its card-facing balance values instead of inheriting unknown values from its native template. Include rarity, profile unlock level, card cost, production time, capacity, health/shield/armor, speed and sight, attack type, damage, firepoints, range, cooldown, effect radius, and relevant skill type/range/duration/cost fields. Use `localizations.name` and `localizations.description` for card copy. Optional `skillDescription` supplies localized ability text; the resolver tries the selected language, then `en-US`, then the native template's `Loca.SkillDescription` text. This field describes an existing compiled or native ability and cannot define new executable behavior.

## Initial blueprint eligibility in stage pools

The game marks the first card offer while `Game.AtLeastOneBlueprintHasBeenAddedByPlayer` is false. The no-pool candidate path already applies the start-only filter. A product with `isAllowedAsStartingBlueprint: true` may enter the first offer at native stage index 0, even though pack stages are appended later; the loader allows its factory through the current native pool's original filter without changing or replacing the native stage or pool. The loader also applies this flag to custom production-building candidates when the first offer comes from a cloned stage pool. The field is independent of `neededExperienceLevel`, which controls persistent profile XP. Later offers retain the normal stage-pool rules. Native pool candidates continue through the game's filter.


## Advanced Runtime Unit Mechanics

Packs can declare advanced, native-grounded runtime mechanics on units (`units[]`) that leverage deep engine reflection into `EntityController` and the `StatusEffect` system:

### 1. Autonomous Regenerative Shield Generator (`shieldRegeneration`)
- **Schema:**
  - `rechargeDelay` (number, 0–60s): Delay in seconds out of combat (measured against native `EntityController.TimeOfLastDamage`) before shields begin recharging.
  - `rechargeRate` (number, 0–1000/s): Shield points restored per second while out of combat.
  - `burstAmount` (number, 0–500): Optional instant burst of shield restored upon initiation of the recharge cycle.
- **Runtime behavior:** Invokes native `EntityController.ChargeShield`. Re-activates dormant shield visual GameObjects upon reaching > 0 shield, caps at `MaxShield`, and fires native `Event.OnShieldFullyRecharged` when full.

### 2. Vampiric Nanite Siphon / Life Leech (`vampiricLeech`)
- **Schema:**
  - `siphonRatio` (number, 0.0–1.0): Fraction of outgoing damage siphoned back to the attacker.
  - `siphonToShield` (boolean): When true and the unit's shield is damaged, restores shield first before healing health.
  - `maxSiphonPerHit` (number, >= 0): Maximum points restored per damage tick to prevent runaway healing.
- **Runtime behavior:** Triggered during `OnEntityDealtDamage`. Restores shield or health and pulses native `StatusEffect.Vampirism` (`0x10000`) for visual feedback.

### 3. Taunt Aggro Field / Threat Magnet (`tauntPulse`)
- **Schema:**
  - `pulseInterval` (number, 0.5–60s): Interval between taunt pulses.
  - `tauntRadius` (number, 1.0–60m): Radius within which enemies are provoked.
  - `duration` (number, 0–30s): Duration of the provoked taunt state.
  - `maxTargets` (integer, 0–32): Maximum enemy combat units forced to retarget per pulse.
- **Runtime behavior:** Periodically grants `StatusEffect.Defender` to the unit, discovers nearby hostile combat units within `tauntRadius`, forces them to immediately retarget the tank via native `EntityController.Attack(self)`, and applies `StatusEffect.Taunt` (`0x400000`).

### 4. Shield Overload Nova (`shieldOverloadNova`)
- **Schema:**
  - `novaRadius` (number, 1.0–50m): Blast radius of the EMP nova.
  - `damage` (number, 0–500): Damage dealt to surrounding hostile units upon shield collapse.
  - `stunDuration` (number, 0–15s): Duration of EMP stun and slow applied to surrounding hostiles.
  - `cooldown` (number, 1–120s): Internal cooldown before another overload nova can trigger.
- **Runtime behavior:** Monitors shield transitions. When a unit's shield breaks (`> 0` to `<= 0`), emits an EMP shockwave dealing area damage, applying `StatusEffect.Stun` and `StatusEffect.SlowedDown` to surrounding enemies within `novaRadius`.

### 5. Timed native status effects (`statusEffects`)
- Entries with a non-empty native `StatusEffect` name and a positive finite duration are applied; other effect-type strings remain valid descriptive metadata and are ignored by this native bridge. The runtime clamps applied durations to `0.05`–`300` seconds and processes at most 16 entries per unit attachment.
- The loader applies recognized statuses to the unit through `EntityController.SetActiveStatusEffect(..., DurationType.Seconds, duration)` once when the runtime profile attaches. The native controller owns expiration; `magnitude` remains preserved as declarative metadata because the pinned native status API exposes no separate magnitude parameter, so the loader does not invent a stat modifier.
- When an EntityFactory controller is reused, the loader removes the timed statuses it applied before attaching the next profile, preventing effects from leaking between units. This is compiled loader runtime behavior and still requires a Steam match to prove the effect on a natural production path.

### 6. Hierarchical Utility-Blackboard Director AI (`sample:tactical-director`)
- A coordinated AI module implementing `ICoordinatedAiRuntimeBehaviour` on top of the HUB-D 3-layer architecture (`AiBattlefieldBlackboard` -> `StrategicDoctrinePolicy` & `TacticalUtilityScorer` -> `AiCommandArbiterPolicy`).
- **Parameters** (all optional):
  - `baseIndex` (integer, `-1`–`31`; default `-1` for all AI bases)
  - `evalIntervalSeconds` (finite number, `0.1`–`10.0`; default `0.5`)
  - `engagementRadius` (finite number, `5.0`–`120.0`; default `40.0`)
  - `defenseRadius` (finite number, `5.0`–`60.0`; default `20.0`)
  - `retreatHealthRatio` (finite number, `0.05`–`0.80`; default `0.30`)
  - `maxProposalsPerUnit` (integer, `1`–`4`; default `2`)
  - `enableSurvivalKiting` (boolean; default `true`)
  - `enableBaseDefense` (boolean; default `true`)
  - `enableFocusTargeting` (boolean; default `true`)
  - `enableFlanking` (boolean; default `true`)
  - `minFlankSpeed` (finite number, `3.0`–`25.0`; default `7.5`)
- **Runtime behavior:** Evaluates Lanchester force balance (`Fortify`, `Assault`, `Skirmish`), proposes `SurvivalKite` vectors via 8-ray `AiSafeVectorPolicy.ComputeTacticalRetreatDestination` biasing retreat towards friendly defensive structures, support repairers, or the allied cluster centroid (tracked by `AlliedDispersionRadius`), coordinates dynamic dual-wing `FlankingManeuver` strikes for mobile combat units around hostile centroids, triggers ready active tactical skills (`TacticalSkill`), and scores `CombatTargeting` targets with status effect vulnerabilities (`Stunned`, `Vulnerable`, `Slowed`, `Burning`, `Overclocked`), affix synergies (`Overcharged`, `Vampiric`, `Volatile`, `Bulwark`, `Juggernaut`, `Swift`), and anti-overkill damping (`ScoreCombatTargetWithOverkill`) before central arbitration in `CompositeAiRuntimeBehaviour`.

### 7. Squad Operations Director AI (`sample:squad-operations-director`)
- Specialist HUB-D 2.0 coordinated AI module implementing `ICoordinatedAiRuntimeBehaviour` for support/repair logistics, counter-battery interception, and specialist active skills.
- **Parameters** (all optional):
  - `baseIndex` (integer, `-1`–`31`; default `-1`)
  - `evalIntervalSeconds` (finite number, `0.20`–`10.0`; default `0.75`)
  - `supportRadius` (finite number, `5.0`–`80.0`; default `30.0`)
  - `counterBatteryMinRangeThreshold` (finite number, `8.0`–`50.0`; default `14.0`)
  - `enableSupportLogistics` (boolean; default `true`)
  - `enableCounterBattery` (boolean; default `true`)
  - `enableTacticalSkills` (boolean; default `true`)
- **Runtime behavior:** Directs support/engineer units out of harmful hazard zones and toward damaged friendly harvesters, buildings, and frontline units (`ScoreSupportOrRepair`), dispatches fast interceptors (`MoveSpeed >= 9.0`) against long-range enemy artillery, and triggers specialist tactical skills through the shared `AiCommandArbiterPolicy`.

### 8. Damage Reflect / Thorns (`damageReflect`)
- **Schema:**
  - `reflectRatio` (number, 0 exclusive–1.0): Fraction of damage taken reflected back to the hostile attacker.
  - `maxReflectPerHit` (number, ≥0): Maximum reflected damage per hit (`0` = uncapped, mirroring `vampiricLeech.maxSiphonPerHit`).
- **Runtime behavior:** Runs inside `RuntimeUnitMechanicsDirector.OnEntityDealtDamage` (victim side) after attacker-side mechanics. It only fires when the victim is alive, the attacker is its enemy (`IsEnemy`), and the victim's profile carries an active reflect. Reflected damage is delivered through native `EntityController.TakeDamage` and therefore respects native shield/armor. A thread-static depth guard (`_reflectDepth`) guarantees reflected damage never triggers another reflect, so mutual thorns cannot loop. Pooled reuse is unaffected (reflect is stateless).

### 9. Death Nova / Deathrattle (`deathNova`)
- **Schema:**
  - `novaRadius` (number, 1.0–50m): Blast radius around the death position (same bounds/scaling as `shieldOverloadNova`).
  - `damage` (number, 0–500): Area damage to hostile units.
  - `stunDuration` (number, 0–15s): `StatusEffect.Stun` applied to hostile units (`0` = none).
  - `slowDuration` (number, 0–15s): `StatusEffect.SlowedDown` applied to hostile units (`0` = none).
  - `maxTargets` (integer, 0–32): Maximum hostile targets hit (`0` = bounded default 32).
- **Runtime behavior:** Triggered exclusively from `RuntimeUnitMechanicsDirector.OnEntityKilled`, which is called only by the `EntityController.Die` postfix. `OnEntityDied` remains the cleanup/reuse path and never emits a nova, so pooled reuse cannot trigger it. The nova excludes the dying unit itself, allies, and out-of-radius units, and delivers damage via native `TakeDamage`.
