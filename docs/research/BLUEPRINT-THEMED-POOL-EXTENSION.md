# Rogue Command: roles, tags, and themed blueprint pools

## Finding

Rogue Command's built-in mod manifest only supports TSV balance overrides for existing rows; new units, stages, and AI need the expansion loader. A useful extension point between those layers is the game's native blueprint classification metadata: `EntityBalancingParameters.roles` (`UnitRole`) and `offeredSystemTags` (`SystemTags`). The game uses roles/tags to form themed blueprint pools and select role/tag-specific hacks and upgrades. Adding these flags as data lets the loader participate in native drafting without inventing a parallel reward system or writing custom Harmony behavior for classification.

The official [Early Access Update #9](https://store.steampowered.com/news/app/1461910/view/497198852915331114) describes themed pools such as tanks, burning units, healers, and turret/support/armored buildings. The [Blueprint overview](https://roguecommand.wiki.gg/wiki/Blueprint) lists the card roles and explains their relationship to abilities. The game artifact was independently checked by static inspection: Rogue Command 1.0.9, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00` (REA evidence `ev_b923e23cfec30be565c58056206d348d2714b1d81204fa26364cbbcce0bd6463` and `ev_412aea7737d399df4b68f81909b5866049848cae5bf2c07c48b18db4e3d25b18`). Static type/member inspection confirmed the row fields; pinned ILSpy output confirmed the exact `UnitRole` and `SystemTags` enum member names and their flags representation.

## Implemented

- Content schema and validator accept comma-separated `roles` and `offeredSystemTags` balance overrides, reject unknown/duplicate/empty flags, and reject `Factory`/`All` on a product row. A production-building override must retain both `Building` and `Factory` if it sets roles.
- The runtime combines the flags into the pinned game enums and applies them to cloned balance rows only. Native registry rows, native factories, and their original mappings remain unchanged.
- The nine sample products declare gameplay-facing roles/tags: Scout/Vehicle, artillery/Tank+Sniper, walkers/Mech, command vehicle/Supporter, bike/Bomb, and arc tank/Tank with stun/shield tags. Stage `unitIds` still reference product IDs; native pool filters stay intact and the existing stage-scoped factory eligibility supplies the reward cards.
- AI continues to consume the registered product IDs through the existing isolated `unitEntries`/`productionBuildingEntries` and compiled modules. Classification is metadata for the game's existing systems, not newly executable AI.

## Evidence limits

- **Static inspection:** the pinned game declares the two enum fields and flags; official notes document themed pools. Neither proves that a particular modded card enters a specific in-game pool.
- **Local validation:** schema validation, package validation tests, and Release build exercise parsing, validation, enum combination, and detached-row assignment. The Windows symlink test is skipped because the environment lacks symlink privilege.
- **In-game runtime:** not performed for this change. A Steam match must still show a themed reward pool offering a classified mod factory and validate purchase/production and AI behavior before making gameplay-acceptance claims.

## Queries

- `Rogue Command official update themed blueprint pools tags roles AI modding`
- `Rogue Command Steam news AI enemy stages update 2025 2026`
- `site:roguecommand.wiki.gg/wiki Rogue Command AI map stage blueprint pool tags role`
