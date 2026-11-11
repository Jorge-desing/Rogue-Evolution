# d576 factory-produced walker retarget Walk runtime pass — 2026-11-11

## Scope and compatibility identity

This pass exercised the existing Release retarget pipeline on a factory-produced Ironclad Walker through BepInEx MCP. No loader source changed and no rebuild was required. Rogue Command 1.0.9, Unity 2021.3.45f2, BepInEx 5.4.23.5 Mono x64; `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Plugin 1.20.6 Release SHA-256 `d576ac60e428213dd9d5d6e2be5a2ad7eba2ab3c96c7618ccbad4de3c9c62fcf`.

The user authorized a new native-UI run. The run used the existing tutorial-complete Profile3 slot; the overwrite dialog was inspected before confirmation. Profile2 was not selected or modified, tutorial flags were not edited, and Steam Cloud remained disabled as set by the user.

## Runtime observation

The run followed native reward and node progression to Game stage 0, level 4. The active stage resolved to a native stage with no declared or runtime-bound loader AI profile. A diagnostic-spawned player-side `sample-native:assault-walker-factory` at `(-100, 130)` received a native `OrderFactoryProduceJson` request with `instant=true` and `forFree=true`. Product instance 196 identified its origin as `sample-native:assault-walker-factory` and began at `(-98, 10, 142.26)`.

After the boss transition, `LoadingHider` was active in the `Game` scene with an interactable `Iniciar` button. Move calls issued while this opener was present did not establish gameplay movement. The native button was invoked through the EventSystem; then a native Move order moved product 196 to `(-79.99, 10, 120.01)`.

The filtered renderer probe used `Action_Walk` and `moving=true`, sampled four of the unit's five skinned renderers, and returned 32 aggregate rows. Each sampled renderer contributed eight rows. All rows reported the Animator active, visible and in the direct Game-camera frustum, with all 8/8 retarget mappings bound and unsuspended. Seven of eight comparable `BakeMesh` deltas per renderer were positive. Maximum sampled local vertex deltas were Gunmetal `1.95392549`, PrimaryArmor `2.470551`, LightArmor `1.07192588`, and Warning `1.61303139`. The probe stopped after 2.2545166 unscaled seconds, and a component census found no remaining renderer probe observer.

The aggregate is saved at `artifacts/runtime-smoke/retarget-d576-factory-profile3-20261111T034727/renderer-frame-probe-factory-walk.json`. Raw sample rows were not retained. A separate initial factory test at the map edge left its produced units outside the useful map interior; that attempt was discarded and the in-bounds producer above supplied the measured sample.

## Profile and installation cleanup

After graceful game exit, the generated Profile3 run files were snapshotted under the same artifact directory. The four original Profile3 files and selector byte `2` were restored. `runtime_profile_checkpoint.py verify` reported `profileFilesExactMatch=true`, `selectorMatch=true`, no added, missing, or changed profile files, and `safe_to_finalize=true`; the saved output is `profile-checkpoint-after-restore.json`. Profile2 and all tutorial flags remained unchanged.

## Evidence limits

This is in-game structured Animator, visibility, retarget-binding, and sampled skeletal-deformation evidence for a native Move order on a factory product. Factory placement and production were diagnostic-spawned and instant/free. This does not prove ordinary blueprint placement, normal-cost production, natural AI scheduling, loader AI execution, gait quality, or pixel appearance. No screenshot or pixel capture was taken.
