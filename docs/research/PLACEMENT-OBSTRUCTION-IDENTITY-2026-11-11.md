# Placement obstruction and runtime entity identity follow-up — 2026-11-11

## Result

The placement rejection was physical. The active Sentinel relay factory footprint covered two cells occupied by the newly placed RoboCrystalMill and two cells containing crystals. The native placement checks rejected that footprint. Moving to the first candidate accepted by the native preflight allowed the factory to be placed; no Harmony placement bypass was used.

## Live runtime evidence

- Environment: Rogue Command 1.0.9, Unity 2021.3.45f2, BepInEx 5.4.23.5. `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`.
- Runtime used the previously installed loader SHA-256 `C1AE90B31A58DCA68C941C05D4E1FE0ED4C9A8D97C103529FC433767E9655428` and the saved, tutorial-complete QA Profile3 run through BepInEx MCP. No clean profile or tutorial reset was used.
- RoboCrystalMill ghost at `(-165,-195)`, cost 1000. Its four footprint cells were `Ground` with no blockers; native placement completed and the player factory census showed the mill. After a bounded native build interval it produced eight player-owned RoboCrystalHarvesters. Credits decreased from 3225 to 2225.
- Sentinel factory ghost at `(-165,-185)`, cost 520. Two cells, `(-17,-19)` and `(-16,-19)`, were occupied by the RoboCrystalMill footprint; the other two, `(-17,-18)` and `(-16,-18)`, contained crystals. The read-only radius-12 search checked 18 candidate positions and found `(-175,-165)` accepted by the native cell, fog, refinery-distance, and builder-spacing checks. The factory was placed there and appeared in the player factory census.
- The old bridge reported each blocked category and reason but did not identify the live building occupying a cell. That gap motivated the new `occupantEntities` rows. Static crystals remain represented by their native cell category when they have no `EntityController`.

## Source and local build

- Loader 1.20.10 adds bounded per-cell occupant identity (`entityId`, `entityKeyId`, `uniqueEntityId`, GameObject name, building/unit flags, and footprint size) for known live entities. Omitted occupant counts are surfaced before detailed rows.
- Runtime command and visual-census selectors now use positive `EntityController.UniqueEntityId` keys. Numeric keys resolve by unique ID; legacy entity IDs or object names are accepted only when exactly one live controller matches, otherwise resolution returns no entity. This addresses the duplicate native `EntityId` that made a prior Attack probe resolve a same-ID unit at different coordinates.
- Release build succeeded with 0 warnings and 0 errors. Artifact SHA-256: `81573CA10D6B78C4B8E4ED9985F3987F17FCF1EBC9D6A19475B6C7547EC545B7`.
- The new per-occupant fields and unique-key retarget behavior are not live-verified by the older C1AE runtime pass. Keep that evidence boundary until the exact 1.20.10 artifact is exercised through BepInEx MCP.

## Profile and installation cleanup

- After the live placement run, the game exited normally. Generated Profile3 files were preserved in `artifacts/runtime-smoke/placement-obstruction-correction-20261111T165154Z/Profile3-generated-after-placement/`; the exact four-file Profile3 backup and original selector byte `2` were restored.
- `runtime_profile_checkpoint.py verify` reported exact file matches, no additions/removals/changes, selector match, and `safe_to_finalize=true`. Profile2 and tutorial data were unchanged; Steam Cloud settings were not changed.
- Deployment backup and post-copy hash verification are recorded in `artifacts/deployment-backups/placement-obstruction-identity-20261111/deployment-manifest.json`.
- The installed DLL backup hash is `c1ae90b31a58dca68c941c05d4e1fe0ed4c9a8d97c103529fc433767e9655428`; the built and deployed 1.20.10 DLL hash is `81573ca10d6b78c4b8e4ed9985f3987f17fcf1ebc9d6a19475b6c7547ec545b7`. Post-copy verification matched exactly. The game process was closed for deployment.

## Exact 1.20.10 runtime retarget check

- Launched Rogue Command after deployment and selected the existing, tutorial-complete Profile3 save through its native profile controls. Continued its saved run; no clean profile or tutorial reset was used. A fresh all-profile checkpoint was captured before launch.
- The saved run was paused in the native Game scene at boss level 4. Two diagnostic `sample-native:assault-walker` instances were spawned with the same native `EntityId`, but their `UniqueEntityId` values and returned command keys were distinct: `278` and `279`.
- `OrderUnitMoveJson("sample-native:assault-walker", ...)` failed closed while both same-ID actors were live. `OrderUnitMoveJson("278", 120, -130)` succeeded and the first actor's transform advanced from `(130,-140)` to `(120.06,-130.01)`. A second Move by key `278` produced the filtered Walk sample; the other actor retained key `279`.
- The stopped probe recorded 8 rows (2 each across four Ironclad Walker renderers), all on `Action_Walk`, moving and visible/in-frustum, with 8/8 retarget mappings bound and unsuspended. Each renderer had one comparable positive local-vertex `BakeMesh` delta. A following HoldPosition command by `278` returned its Animator to `Action_Idle` after a bounded update.
- Probe `StopRendererFrameProbeJson` succeeded and `find_objects_with_component(RuntimeRendererFrameProbeBehaviour)` returned an empty census. Match time scale was restored to `0` after sampling. No image or Computer Use path was used.
- Runtime summary: `artifacts/runtime-smoke/unique-key-retarget-release-20261111T173856Z/unique-key-retarget-summary.json`. The 350 ms initial window produced no matching rows; a 1.5 s bounded Walk window produced the reported eight rows.
- After normal process exit, generated Profile3 files were preserved under `artifacts/runtime-smoke/unique-key-retarget-release-20261111T173856Z/Profile3-generated-after-exit/`. The exact four-file Profile3 backup and selector byte `2` were restored. The original all-profile checkpoint passed with no added, missing, or changed files and `safe_to_finalize=true`.
- The runtime process started after the 1.20.10 DLL was installed and its verified artifact SHA is recorded above. This live check confirms unique-key command/retarget attribution on that Release; it did not exercise ordinary native production or custom AI behavior.

## Evidence boundary

Placement and native factory-production evidence in this report came from the previously installed C1AE artifact. Unique-key command/retarget behavior was also verified live on the exact deployed 1.20.10 Release. The new per-cell `occupantEntities` fields remain source/build evidence because that placement payload has not yet been read from 1.20.10 in-game. No new Harmony patch or transpiler was added because the placement failure followed native occupancy rules and no Harmony lifecycle regression was isolated.
