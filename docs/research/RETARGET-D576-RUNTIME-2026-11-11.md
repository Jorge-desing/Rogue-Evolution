# Native Rig Retarget: d576 Release Walk Pass

## Scope and build identity

This pass validates the current Release's native-rig retarget path during a real gameplay Move order. It does not modify loader source. Game identity: Rogue Command 1.0.9, Unity 2021.3.45f2, BepInEx 5.4.23.5, Mono x64; `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`; MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Release SHA-256: `d576ac60e428213dd9d5d6e2be5a2ad7eba2ab3c96c7618ccbad4de3c9c62fcf` (loader 1.20.6).

## In-game BepInEx MCP result

The existing tutorial-complete Profile3 QA clone was selected through the native profile UI. The user authorized starting a fresh run; the overwrite dialogue was inspected and confirmed through native UI. No tutorial flag or Profile2 file was edited.

A disposable player-side `sample-native:assault-walker` was spawned at world `(0,-110)`. The visual census reported a valid `IroncladWalkerAvatar`, `SkeletalArmature` with `Bone_Root`, disabled root motion, and eight of eight retarget mappings bound and unsuspended. The mappings transfer the native RoboMarine leg-chain transforms and `RotHor`/`ROTVERT` yaw/pitch sources into the Walker armature.

A native Move order sent the unit to `(20,-110)`. The controller position reached `(20,10,-110)`, `RCL_IsMoving` returned false, and the Animator returned to `IroncladWalker_Action_Idle`. A filtered `Action_Walk` / moving renderer probe recorded 48 rows across four of five skinned renderer batches (12 rows per batch). Every row showed the Walk clip, moving state, visible bounds in the direct Game-camera frustum, and 8/8 bound, unsuspended retarget mappings. The first row per renderer established the comparison baseline; all 44 comparable follow-up rows had positive local `SkinnedMeshRenderer.BakeMesh` vertex deltas.

| Renderer batch | Samples | Comparable deltas | Positive deltas | Peak local delta |
|---|---:|---:|---:|---:|
| `IroncladWalker_Batch_Gunmetal` | 12 | 11 | 11 | 0.4733 |
| `IroncladWalker_Batch_PrimaryArmor` | 12 | 11 | 11 | 0.6118 |
| `IroncladWalker_Batch_LightArmor` | 12 | 11 | 11 | 0.1918 |
| `IroncladWalker_Batch_Warning` | 12 | 11 | 11 | 0.3334 |

This confirms structured Animator, retarget, camera-frustum, and sampled-deformation telemetry. It does not establish pixel appearance, gait quality, production/factory spawning, natural-combat death, or AI behavior.

## Probe and pause caveats

The renderer probe has a 20-second lifetime, a four-renderer cap, and a per-renderer sample limit of 12. This run reached the lifetime cap and reported 848 later eligible samples omitted after quotas filled. Its start JSON returns the handle in `token`; a timely explicit Stop and live component-census verification were not completed after the retained result expired. Do not claim census-verified observer cleanup from this pass. The process closed normally afterward.

`Navigator.Pause()` returned success, but `Navigator.isPaused` continued to read false. `Time.timeScale` read 0 and `scaledTime` remained stable, so the match was effectively frozen. Read effective time values rather than treating the invocation result or pause flag alone as proof.

## Profile preservation and deployment

The post-run Profile3 state, including the generated `metaSavegame.dat` and `savegame.dat`, is preserved under `artifacts/runtime-smoke/retarget-profile3-new-run-20261111/Profile3-generated-run-after-exit/`. The exact four-file Profile3 pre-run backup was restored after the game process closed, and `currentProfile.txt` was restored to the original single byte `2`. The complete checkpoint verifier reported exact profile and selector matches with `safe_to_finalize=true`; Profile2 remained byte-identical. Steam Cloud was disabled by the user and was not changed.

The unchanged Release was redeployed after process closure. Its installed SHA matched the artifact at `d576ac60e428213dd9d5d6e2be5a2ad7eba2ab3c96c7618ccbad4de3c9c62fcf`. The pre-deploy backup manifest is `artifacts/deployment-backups/retarget-current-release-20261111T094410Z/deployment-manifest.json`.

## Evidence files

- Runtime summary: `artifacts/runtime-smoke/retarget-profile3-new-run-20261111/current-release-retarget-summary.json`.
- Bounded frame rows: `artifacts/runtime-smoke/retarget-profile3-new-run-20261111/renderer-frame-probe-walk.json`.
- Profile checkpoints: `profile-checkpoint-before.json`, `profile-checkpoint-after-run-before-restore.json`, and `profile-checkpoint-after-restore.json` in the same directory.
