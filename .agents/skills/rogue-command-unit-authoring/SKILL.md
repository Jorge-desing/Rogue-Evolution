---
name: rogue-command-unit-authoring
description: "Author or review Rogue Command units, including balance variants, model overlays, card sprites, and localized names."
version: 1.2.1
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, unit authoring, balance, variants, factories, blueprints, cards, sprites]
    related_skills: [rogue-command-clean-room-diagnostics, rogue-command-pack-validation, rogue-command-3d-model-authoring, rogue-command-balance-simulation]
---

# Rogue Command Unit Authoring

Authoritative guide for creating and reviewing modded units, balance variants, factory pairings, and reward pool bindings.

---

## 1. Unit Construction Routes

| Route | Definition Mechanism | Asset & Code Contract | Validation & Constraints |
|---|---|---|---|
| **Native Template** | Copies `nativeTemplateId` balance row and native prefab | Uses `balanceOverrides` / `customBalance`; keeps native controller & logic | Run `tools/validate_native_templates.py` to verify ID |
| **Visual Overlay** | Declares `visualAssetName` + `visualRootPath` in bundle | Overlays custom mesh onto native renderer subtree; retains native logic | Run `tools/validate_native_visual_roots.py`. Root must resolve to valid renderer subtree |
| **Custom Prefab** | Full bundled balance asset, prefab, and card sprite | Script-free prefab; requires complete Unity scaffolding | Fail-closed if game-required components cannot be resolved |

- **Factory Invariant:** Every added unit requires its own namespaced `productionBuilding` linking `factoryForEntityId` to the unit. Reward pools offer the factory's blueprint; AI `unitEntries` and `productionBuildingEntries` use `unitId` to reference the produced unit, never the factory. The `entityId` parameter on `sample:scheduled-base-build` is the explicit factory-ID exception.
- **AI Cross-Pack Audit:** `python -m tools.validate_ai_modules content/packs --all --json` checks module and product references across packs. `python -m tools.analyze_ai_profile_coverage <pack_dir> --json` also resolves external products against the repository's declared unit catalog. Both are static checks and do not prove the AI profile or factory scheduler ran in-game.
- **Fail-Closed Factory Registration:** If factory registration throws, remove the registered unit product row before rethrowing to prevent partial provider state.

---

## 2. Balance Precedence, Bounds & Enums

### Precedence Order
$$\text{customBalance} > \text{raw balanceOverrides} > \text{positive finite movement.moveSpeed}$$

### Balance Overrides Contract
- **25 Verified `ChangeableValue` Keys:** Maps directly to `EntityBalancingParameters` row fields (`MaxHealth`, `MoveSpeed`, `Damage1`, `Attack1Cooldown`, `Damage2`, `Attack2Cooldown`, `MaxArmor`, `ArmorProtection`, `Cost`, `BuildTime`, `SightRadius`, `AttackRange`, etc.). Excludes global-only `ManaRechargePerSecond`.
- **Numeric Caps:** Integer fields capped at `16,777,216` (due to native `CalculateResultingIntValue` widening `Int32` to `Single`). Float fields capped at `Single.MaxValue`. Must be finite and nonnegative.
- **Armor Contract:** `MaxArmor` is purely an inert repair pool / visual bar; it does NOT mitigate damage. Only `ArmorProtection` (flat damage subtraction, floor 0.1) reduces damage.
- **Scout Range Floor:** Modded scouts must have `weaponRange >= 5.5` to prevent `INFINITE_KITING_ENVELOPE` against slower enemies.
- **Supported Enums:**
  - `rarity`: `Common`, `Rare`, `UltraRare`.
  - `attackType`: `None`, `Single`, `AoE`, `Multi`, `MultiAoE`, `Beam`, `Artillery`, `MultiArtillery`.
  - `skillType`: `None`, `Toggle`, `Location`, `LocationAoE`, `LocationMultiTargets`, `Self`, `SelfAoE`, `Target`, `TargetMultiTarget`, `TargetAoE`, `SelfMultiTarget`.

---

## 3. Declarative Loadout Variants (`units[].variants`)

- **Capacity & Bounds:** Max 8 variants per unit; max 64 `balanceOverrides` per variant.
- **Identifier Rules:** Variant IDs must be valid identifiers and strictly reject line terminators (`\r`, `\n`, CRLF, U+2028, U+2029).
- **Runtime Application:** `diagnostics_order_apply_unit_loadout` applies variants on live units. Converts absolute instance stats (`damage1`, `attackCooldown`, `moveSpeed`, `weaponRange`, `maxHealth`, `maxArmor`, `armorProtection`, `maxShield`, `sightRadius`) to native relative modifiers. `Standard` resets to baseline.

---

## 4. Structured Fields & Runtime Mechanics

When adding or altering runtime mechanics, update all 4 layers simultaneously: Schema $\rightarrow$ DTO/Clone $\rightarrow$ Validation $\rightarrow$ Runtime Loader.

Native crystal harvesters now map positive `resourceGathering.gatherRate` to row `maxArmor` and `carryCapacity` to `gainCreditsAmount`, after the final row's Harvester role is resolved. Raw row overrides and customBalance win. Crystal/Crystals are the supported native resource names; other resource types and `gatherRange` remain descriptive. This does not add a Harvest action to a prefab that lacks it. Cargo capacity is capped at 2^24 for exact native Single integer storage; zero preserves the template default.

| Mechanic Field | Sub-Properties & Types | Range & Constraints | Behavior & ChangeableValue Mapping |
|---|---|---|---|
| `shieldRegeneration` | `delay` (float), `rate` (float) | Nonnegative floats | Regenerates shield charges out of combat |
| `vampiricLeech` | `siphonRatio` (float), `maxSiphonPerHit` (float) | $0.0 < \text{ratio} \le 1.0$ (exclusive 0). Use `null` if disabled | Heals on hit $\rightarrow$ maps to `Damage1` |
| `tauntPulse` | `radius` (float), `duration` (float) | Positive floats | Draws enemy aggro |
| `shieldOverloadNova`| `radius` (float), `damage` (float) | Positive floats | AoE explosion when shield breaks |
| `damageReflect` | `reflectRatio` (float), `maxReflectPerHit` (float) | $0.0 < \text{ratio} \le 1.0$ (exclusive 0); max $\ge 0$ (`0` = uncapped) | Thorns: reflects fraction of damage taken to hostile attacker via native `TakeDamage`; thread-static depth guard prevents mutual-reflect loops |
| `deathNova` | `novaRadius` (float), `damage` (float), `stunDuration` (float), `slowDuration` (float), `maxTargets` (int) | Radius 1–50; damage 0–500; stun/slow 0–15; targets 0–32 | Deathrattle AoE on `Die` postfix only (`OnEntityKilled`); pooled reuse via `OnEntityDied` never triggers |
| `experienceReward` | `experienceAmount` (int) | Positive integer (floats reject!) | Bounty XP grant $\rightarrow$ maps to `Damage1` |
| `combatBoosts[]` | `statName` (string), `multiplier` (float), `duration` (float) | Finite positive | Timed buff + persistent card modifier; role-aware |

- **Tiered Role Scaling:** Scale mechanics by cost tier (T0 $\le 80$, T1 $\le 260$, T2 $> 260$) and role. Melee/support gain sustain; disposable units get zero leech. Cap flat bounty damage (×0.6 for `MassDestruction`).
- **Director Cleanup Invariant:** When entities are destroyed or recycled, `RuntimeUnitMechanicsDirector` must call `RemoveStatusEffect` for baseline effects and `RemoveCaptain` for command links. Combat XP is cleared only on final destruction.
- **Faction presentation:** RuntimeVisualDirector refreshes masked `_TeamColor` from native Player/Ai/AiBuilding material resources at initialization and pool reuse. Explicit non-white variant tints override that default; white/reset returns to faction tint. Missing resources and neutral entities preserve the authored baseline. This changes appearance only, never ownership flags.
- **Command range ownership:** Command pulses skip followers already owned by another captain before counting capacity or applying command buffs. Track linked followers per director; leaving range, losing eligibility, refreshing the profile or clearing runtime state releases only links still owned by that commander. Returning eligible followers can join again. Timed command buffs retain their declared expiry.
- **Healing target selection:** Exclude native `canNotBeHealed` targets from health-pulse priority so they cannot starve healable allies. Armor selection is independent: native `RepairArmor` does not use that health-only flag. Keep native Heal/RepairArmor calls authoritative.
- **Support aura capacity:** Heal/Shield auras count only recipients whose health, shield or fallback armor actually increases toward the sixteen-recipient pulse cap. Healthy, full-shield, unhealable or ineffective native calls must not consume recipient capacity. Buff/Debuff auras retain their bounded status-recipient behavior.
- **Shield mechanic ordering:** Detect shield-overload depletion before local aura/regeneration effects can refill the shield. Record the final post-recharge shield value for the next update, so a recovered shield rearms subsequent breaks. Keep the nova cooldown independent; recharge alone must not fire it.
- **Combat experience:** A single damage event may earn multiple ranks. Consume all full thresholds up to the lower native/profile rank cap, apply each rank's rewards, preserve partial XP below the cap and clear unusable surplus at the cap. Use wide intermediate XP arithmetic; repeated hits at maximum rank grant no further promotion rewards.
- **Shield-priority leech:** `siphonToShield` first restores missing shield, then routes the remaining per-hit siphon budget to native health healing. Subtract observed shield restoration, capped to the requested shield amount, so overflow is preserved without granting the same budget twice. Preserve fractional deficits and `maxSiphonPerHit` across both resources.
- **Timed numeric modifiers:** `ApplyTimedStatChange` accepts all names from the pinned native `EntityController.ChangeableValue` enum, in addition to existing aliases. Unknown/numeric names and non-finite values do not produce a modifier. `AttackSpeed`/`FireRate` convert a positive rate multiplier `m` to cooldown delta `1/m - 1`, including rates below 10%; a nonpositive rate cannot be represented by this conversion. Native per-origin replacement and expiry remain authoritative.

---

## 5. Drafting, Blueprint Pools & Card Sprites

- **Drafting & Roster Flags:**
  - `isAllowedAsStartingBlueprint`: `true` to offer during initial Stage 1 deck draft.
  - `isAllowedForAi`: `true` for combat units, `false` for factories/HQs.
  - `isAllowedForDemo`: `true` for demo run eligibility.
  - `isForSpecialists`: `true` for specialist archetype pools.
- **Card Initialization Order:** Always register unit rows with `EntityBalancingStore` BEFORE injecting card prefabs into reward pools. If `CardNew.Init` queries an unregistered entity, it falls back to an error panel.
- **Card Sprites:** $128 \times 128$ PNG; `FilterMode.Point`, `mipmapEnabled = false`, `spriteImportMode = Single`. Generated via `imagegen` against dark tactical gradient (`#0a1216` to `#162a34`).

---

## 6. References

- [Pack Validation Skill](../rogue-command-pack-validation/SKILL.md) — Schema and registry validation.
- [3D Model Authoring Skill](../rogue-command-3d-model-authoring/SKILL.md) — Visual geometry, sockets, and materials.
- [Clean-Room Diagnostics Skill](../rogue-command-clean-room-diagnostics/SKILL.md) — In-game inspection and command director.
- [Balance Simulation Skill](../rogue-command-balance-simulation/SKILL.md) — Lanchester combat calibration.

## 7. Canonical Two-Level Visual Variants

- Keep `visualVariants[0]` as the Level 1 primary chassis. Level 2 entries are same-unit tactical visual variants; put optional visual-only modules on those entries. The first variant may not declare modules.
- A module entry contains only `slotId` and `assetName`. Valid slots are `ERA`, `APS`, `Sponsons`, and `FuelDrums`; allow at most one module per slot and four per variant. The unit's `assetBundleId` owns every module asset, so do not add a per-module bundle field.
- Modules change silhouette and equipment appearance only. Preserve unit IDs, balance, factory/product links, cards, and native gameplay controller. An omitted module leaves the primary chassis visible at that hardpoint.
- Keep the engineering hierarchy explicit: the primary chassis carries its functional form language; a tactical variant adds discrete equipment at canonical hardpoints instead of replacing the chassis or duplicating a unit definition. Check `tools/Bind-CanonicalVisualModules.py --check` after editing the canonical matrix.
- For generated Level 2 equipment, add an authoring-only role row to `content/source-art/canonical-visual-module-roles.json` for each `(variantId, slotId)`. The role describes visible mechanical assemblies, not gameplay; keep PBR presets as surface data. The full Unity build validates that the declared module and role match one unique hardpoint socket on the actual Level 1 prefab.
- Sandlance Scout, Colossus Siege Platform, Skirmish Strike Bike, and Surge Arc Tank each export `Socket_Hardpoint_ERA`, `APS`, `Sponsons`, and `FuelDrums` from both source and rigged Blender builders. Keep their single coordinate source in `tools/CanonicalUnitHardpoints.py`; builders create unique empty sockets from that mapping and export them with the chassis. The Unity scaffold can fill missing sockets from model bounds for old inputs, but a new authored FBX should carry the physical mount points itself.

## Native Visual Animation Retarget

- `nativeRigRetarget` is optional and valid only with a native template plus `assetBundleId`, `visualAssetName`, and `visualRootPath`. Declare 1–64 mappings. Each `sourcePath` is relative to the native prefab root; each `targetPath` is relative to the visual prefab root.
- Paths resolve by exact, unique Transform names. Target paths must be unique; sources may be reused but cannot equal or be an ancestor of `visualRootPath`, because the visual overlay already inherits that transform.
- The runtime bridge copies mapped bind-relative local position and rotation deltas after Animator evaluation, compensates positions for the uniform overlay scale, and never copies scale/root motion. Unmapped target transforms remain Animator-controlled. The `EntityController.Destroy` transpiler requires the exact game instance signature `void Destroy(bool, EntityController)` and its static non-generic callback `void(object, bool, object)`; drift or empty IL fails Harmony installation. It suspends copying at method entry, with the visual director as a dead-state fallback. Pooled reuse restores bind poses and animator state through the existing reuse transpiler route.
- Treat this as a mapped-pose bridge, not a general Humanoid retargeter. Check native and bundle paths against serialized hierarchies. A 2026-11-10 Profile2 check observed Ironclad locomotion with 8/8 mappings bound and matching target/computed poses for two leg roots. A later saved-Profile1 BepInEx MCP sample captured `Action_FireRecoil` and matching computed yaw/pitch target poses. A saved-Profile2 Destroy-entry probe then observed the retarget profile bound 8/8 and suspended after the callback, before the object disappeared. It used direct diagnostic damage; it does not prove natural-combat death or rendered death appearance. Static inspection shows native Destroy does not return dead objects to the pre-spawn stack, so validate its stack-pop reset separately. Runtime trace: `artifacts/runtime-smoke/restart-saved-profile-20261110T061923/retarget-destroy-entry-follow-up.json`; static factory trace: `artifacts/runtime-smoke/native-retarget-natural-death-20261110T140359Z/native-prespawn-stack-static-evidence.json`.
- Local C# coverage checks retarget binding and lifecycle guards, including ambiguous path rejection and reuse reset; those checks do not replace a Steam runtime observation.
- Runtime spawning must canonicalize accepted side names to the game's actual tags: `Tags.Ai` is `AI` and `Tags.Player` is `Player`. Reject unknown side values rather than silently treating them as Player. A Profile2 BepInEx spawn with requested side `Ai` returned tag `AI` and `IsControlledByAi=true`; direct `EntityController.IsEnemy` invocation was not verified by the MCP bridge.
- `tools/validate_native_visual_roots.py <pack>` checks each mapping's `sourcePath` against the pinned installed native hierarchy and `targetPath` against the exact declared AssetBundle prefab container. It reports static hierarchy resolution only. `diagnostics_inspect_entity_visual` reports `animation.nativeRigRetarget` state and up to 16 mapping rows containing source/target paths, availability, current local poses, and computed target pose. `omittedMappingCount` precedes `mappings`. Match configured/bound counts, then compare live source and target poses during locomotion/fire; the computed pose is only a math reference, and death suspension/pool reuse need separate observations.

### Runtime animation probe boundary

- Use `diagnostics_wait_for_renderer_frame_evidence` to wait for distinct direct Game-camera samples matching a disposable unit's clip, movement, retarget binding, visibility, and optional positive `BakeMesh` delta. It attempts to stop observers in `finally` after receiving a valid token; check cleanup status. It uses no screenshot; this is runtime telemetry, not proof of production registration, AI selection, natural combat, gait quality, or pixel appearance. The lower-level start/read/stop probe remains available for custom timing.
- The wait now filters clip and movement state inside Unity before sampling, preventing early Idle frames from consuming the cap. Stop is idempotent for the briefly retained result; if a token is unavailable, require an empty live `RuntimeRendererFrameProbeBehaviour` census before marking cleanup confirmed.
- In `automation_run_sequence`, pass a successful spawn result's unique key as `{"$ref":{"step":0,"path":"/entityKeyId"}}` to movement and renderer-wait steps. References preserve the JSON value type and fail closed for forward, failed, or missing result paths.
- If the test uses a saved run, spawn only a disposable actor, preserve the tutorial-complete profile, pause the match, and restore the selected profile checkpoint after normal game exit.
- To test a factory-linked visual, diagnostic-spawn only the factory and then invoke its native `Produce` path; confirm the resulting unit's `originEntityId` matches that factory and command it by `UniqueEntityId`. The Profile2 Ironclad sample verified 8/8 retarget bindings through locomotion and HoldPosition-to-Idle, but the factory itself was diagnostic-spawned, so this is not ordinary blueprint placement or AI production evidence. Preserve the checkpoint and verify every profile hash after exit: `artifacts/runtime-smoke/retarget-factory-production-20261110/factory-retarget-runtime-summary.json`.

### Guaranteed mod card offers (2026-11-11)

- Starting drafts may include a mod unit when `isAllowedAsStartingBlueprint` is true or its `neededExperienceLevel` is zero or lower. Never inject an ordinary production building; standalone building products remain an explicit exception.
- Filter XP, demo eligibility, and already-owned/offered product or factory IDs before applying pool roles. Prefer a role found in the pool ID; if no eligible themed unit remains, use an eligible general mod unit. Never reintroduce a candidate rejected by an earlier filter. A native draft that already contains a mod product needs no replacement.
- Use the product unit ID in the card list. The `ChooseCard` prefix preserves the original list length and replaces one random slot only when needed; keep registration and native card initialization intact so the normal prefab, localized title, and image sprite path run.
- For screenshot-free live checks, read the reward/node through BepInEx diagnostics, then inspect `CardNew.EntityId`, title text, `Image.sprite`, and the card model's `Renderer.enabled`/`Renderer.isVisible`. These Unity states support runtime rendering diagnosis but do not prove pixels or appearance quality.

### Production-building placement acceptance

- Before attributing a failed factory placement to unit registration or prefab geometry, inspect the active native ghost footprint cell by cell. A crystal or `WorldObject.Building` cell is a real grid blocker; secondary fog and refinery distance are separate native gates. Do not bypass `BuildingPlacementHelper.IsPlacementValid` with Harmony.
- A Profile3 1.20.10 recheck found a crystal under one Sentinel-factory footprint cell, three `Building` cells at another attempted site, fog on all four, and no accepted site among 313 radius-12 candidates. The run had zero Player factories, so it did not prove a legal Player-refinery range. Use a known Player-refinery stage and preflight a clear full footprint before claiming ordinary building placement. Evidence: `artifacts/runtime-smoke/retarget-fire-1.20.10-20261111T1824/placement-obstruction-recheck-20261111T1837Z.json`.

- Current Release live retarget evidence (SHA-256 `2E740D387F600CAF0023ED7254F9F41ECF9E69E34E548328BFF147D250BC3DFB`): a disposable native-template Ironclad Walker reported 8/8 mappings during native Move/Action_Walk and during diagnostic Death retirement. Walk rows were moving and unsuspended; Death rows were stationary and suspended while still bound. The test stage declared no loader AI profiles, so it does not establish AI use, factory/blueprint production, natural combat, or animation quality. Report: `docs/research/RETARGET-CURRENT-RELEASE-RUNTIME-2026-11-11.md`.
- d576 Profile3 new-run reward evidence (2026-11-11): the native IntermissionScreen offered `sample-native:scout-tank` as a `CardNew`; choosing it produced a deck census containing `sample-native:scout-tank-factory` linked to `sample-native:scout-tank`. This verifies the in-run custom card offer and deck registration path only; no ordinary placement or factory production was exercised. The active combat stage was native and declared no loader AI profiles. Evidence: `docs/research/RETARGET-D576-DEATH-RUNTIME-2026-11-11.md`.
- d576 factory-product retarget follow-up (2026-11-11): a diagnostic-spawned `sample-native:assault-walker-factory` used native `OrderFactoryProduceJson` to create a Walker whose origin ID matched the factory. The order was instant/free, so this confirms origin/product linkage and a test actor path only; it does not validate ordinary placement, costs, blueprint availability, or AI production. After the `LoadingHider` opener was invoked, the product moved and emitted Action_Walk renderer telemetry. Evidence: `docs/research/RETARGET-D576-FACTORY-WALK-RUNTIME-2026-11-11.md`.

## Native production-building placement checks (2026-11-11)

- A custom `productionBuilding` remains subject to the game's native footprint occupancy, height-layer, fog, refinery-distance, and builder-base spacing rules. A clear-looking preview is not enough; inspect the active ghost with `diagnostics_inspect_active_building_placement` and read its per-cell blocker reasons before diagnosing the pack or prefab.
- Keep the inspection and `diagnostics_find_building_placement` read-only. Never patch around `BuildingPlacementHelper.IsPlacementValid` or queue a factory on occupied cells to make a QA attempt pass. A search can find no site when a global native requirement fails even after a local obstruction is cleared.
- A concrete Profile3 placement pass found the Sentinel factory footprint over two cells occupied by the newly placed RoboCrystalMill and two cells containing crystals. The accepted `(-175,-165)` site passed all native cell and spacing checks before commit; the mill and factory then appeared in the player factory census, and the mill produced eight harvesters. This is live placement evidence from the previously installed C1AE DLL; the occupant-identity fields added in 1.20.10 are not proven live by that run. See `docs/research/PLACEMENT-OBSTRUCTION-IDENTITY-2026-11-11.md`.
- Loader 1.20.10 per-cell `occupantEntities` was read in a live native preflight. One map `Building` cell plus secondary fog across the full four-cell Sentinel footprint caused rejection; all occupant arrays were empty, so only the field's empty behavior is verified and non-empty live-controller identities still need a matching scenario. Do not bypass native occupancy or fog checks. Separately, unique command keys were verified live on 1.20.10 for same-product Walker diagnostics. These diagnostic results do not establish accepted 1.20.10 placement, production, or AI execution.
