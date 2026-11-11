# Current Release multi-clip retarget validation — 2026-11-11

## Scope and identity

The installed Release artifact was exercised through BepInEx MCP in the authorized Profile3 QA clone. No screenshots or Computer Use were used. The target was `sample-native:assault-walker`, spawned with `SpawnEntityJson`; it was a diagnostic runtime spawn, not a factory-produced unit. No plugin source changed during this pass.

Compatibility identity: Rogue Command `1.0.9`, Unity `2021.3.45f2`, BepInEx `5.4.23.5`, `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Release `1.20.9` SHA-256 was `c1ae90b31a58dca68c941c05d4e1fe0ed4c9a8d97c103529fc433767e9655428`.

## Runtime findings

The bounded renderer-frame probe sampled four batches at eight rows per batch. It reports Animator state and sampled `SkinnedMeshRenderer.BakeMesh` deltas; it does not read pixels.

| Clip | Trigger/filter | Retarget state | Sampled mesh result |
|---|---|---|---|
| `Action_Walk` | Native Move; `Action_Walk` + moving | 32/32 rows bound 8/8, unsuspended | 28/28 comparable deltas positive across four batches (7/8 each) |
| `Action_Idle` | HoldPosition; `Action_Idle` + stationary | 32/32 rows bound 8/8, unsuspended | 28 deltas available, all zero; stable idle pose |
| `Action_FireRecoil` | Native Attack; `Action_FireRecoil` | 32/32 rows bound 8/8, unsuspended | 28/28 comparable deltas positive across four batches (7/8 each) |
| `Action_Death` | Direct diagnostic lethal damage; `Action_Death` | 32/32 rows pending retirement, suspended, and still bound 8/8 | 28/28 comparable deltas positive across four batches (7/8 each) |

The target had one additional skinned batch beyond the probe's four-renderer cap. The Animator reported root motion disabled. The Death probe was started while the actor was still registered, then the disposable actor received 500 diagnostic damage with armor ignored. This is death-clip and lifecycle telemetry, not natural-combat evidence.

The FireRecoil sample showed the expected clip and deformation. The command's entity key `PCXNanoHunter` was not unique: the native Attack result resolved a target at `(109.244293, -67.9342957)`, while the temporary QA target had been spawned at `(-25, 90)`. Do not attribute the clip to that temporary target without a unique target identity.

Full bounded summary: `artifacts/runtime-smoke/retarget-current-release-20261111/retarget-c1ae-multiclip-summary.json`.

## Placement blocker

The earlier field-medic factory ghost at world `(15, -65)` overlapped occupied building cells `(1, -7)`, `(2, -7)`, and `(2, -6)`. Each reported `worldObject=Building` and `occupiedByBuildingRampOrObstacle`; the placement was not committed and no credits were spent. A read-only radius-12 search checked 313 candidates and found none passing all native checks. After closing the deck panel there was no active placement controller or ghost, so no further placement was attempted. No Harmony bypass was added.

## Cleanup and deployment

Every renderer probe token was explicitly stopped and game time was returned to `0` after each sample window. Rogue Command exited normally. The generated Profile3 files were retained under `artifacts/runtime-smoke/retarget-current-release-20261111/Profile3-generated-after-retarget-c1ae-20261111/`; all original Profile3 files were restored from the verified pre-clone backup, and `currentProfile.txt` was restored to byte `2`. `runtime_profile_checkpoint.py verify` reported exact profile-file matches, no additions or changes, selector match, and `safe_to_finalize=true`. Profile2 and tutorial state were unchanged; Steam Cloud settings were not changed.

With the game closed, the deployed DLL was backed up and the matching Release artifact was recopied. Pre-deploy backup, release, and post-deploy SHA-256 values all matched in `artifacts/runtime-smoke/retarget-current-release-20261111/deployment-manifest-c1ae-20261111.json`.

## Evidence limits

These results establish live Animator, retarget-binding, lifecycle, and sampled skeletal-deformation telemetry on the current Release. They do not establish visual gait quality, recoil readability, death-pose quality, pixels, normal blueprint placement, factory production, or AI scheduling.
