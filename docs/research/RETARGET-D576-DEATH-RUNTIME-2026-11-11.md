# Native Rig Retarget: d576 Release Death-Frame Pass

## Identity and scope

This pass continued the d576 native-rig retarget validation on the existing tutorial-complete Profile3 QA clone, after the user authorized a fresh run. It did not change loader source. Game identity was Rogue Command 1.0.9, Unity 2021.3.45f2, BepInEx 5.4.23.5 Mono x64; `Assembly-CSharp.dll` SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Release 1.20.6 SHA-256 was `d576ac60e428213dd9d5d6e2be5a2ad7eba2ab3c96c7618ccbad4de3c9c62fcf`.

## New-run loader evidence

The native profile UI selected Profile3, the copied tutorial state remained complete, and the new run read `progressionMode=Default`; it did not enter `FirstRun`. The first custom unit card offer contained `sample-native:scout-tank` (`CardNew`). Choosing it produced a live deck census containing `sample-native:scout-tank-factory` linked to `sample-native:scout-tank`. This verifies the offer and deck registration path, not blueprint placement or factory production.

The active combat scene later resolved to native `StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit`, with no declared or runtime-bound loader AI profile. No loader AI execution was tested or inferred from the card offer.

## Death-frame retarget telemetry

A disposable player-side `sample-native:assault-walker` was spawned at `(0, -110)`. Its Generic Avatar was valid, root motion disabled, and the retarget profile had eight configured and bound mappings. A first probe produced no samples because the actor was outside the Game-camera frustum; it saw 3,134 Game-camera callbacks and 9,044 frustum rejections. After the bridge focused the camera on the actor, an idle preflight confirmed visible, in-frustum renderer samples.

The successful probe used a temporary typed BepInEx helper to set `Animator.updateMode=UnscaledTime` on this disposable actor only, then called `EntityController.TakeDamage(999f, null, true, true)`. The match stayed at `Time.timeScale=0`. This is direct diagnostic damage, not natural combat. The probe was filtered to `Action_Death` and stationary state, stopped after 1.158 unscaled seconds, and recorded 32 rows: eight each from Gunmetal, PrimaryArmor, LightArmor, and Warning. A fifth skinned renderer was outside the probe's four-renderer cap.

All 32 rows reported the `IroncladWalker_Action_Death` clip, `DeathVisualRetirementPending=true`, the retarget profile suspended with all 8/8 mappings still bound, and renderer bounds visible and intersecting the direct Game-camera frustum. Each renderer had seven positive post-baseline sampled `BakeMesh` deltas out of eight rows; peak local deltas were 0.035091 (Gunmetal), 0.010299 (PrimaryArmor), 0.010651 (LightArmor), and 0.035104 (Warning). The sampled normalized clip times were approximately 0.0385–0.0685, so this does not establish full-clip playback or pose quality. After the bounded hold, the actor was absent; the temporary helper and probe observers were absent. `deathVisualRetirementCount` increased from one to two across the two diagnostic actors in this scene.

This establishes structured Animator, lifecycle, camera-frustum, and sampled deformation telemetry. It does not establish natural-combat death, full death-clip playback, perceived appearance, pixels, or gait/death quality.

## Cleanup, profiles, and deployment

Rogue Command exited via `UnityEngine.Application.Quit`; the process was confirmed absent before touching save files. The generated Profile3 `excludedStrings.txt`, `metaSavegame.dat`, `savegame.dat`, and `tutorialState.txt` were preserved under `artifacts/runtime-smoke/retarget-d576-death-pool-20261111/Profile3-generated-run-after-exit/`. With the game closed, all four exact pre-run Profile3 files were restored and `currentProfile.txt` restored to its original single byte `2`. The checkpoint verifier reported exact matches for all 21 profile/selector/Cloud entries, no additions/removals/changes, and `safe_to_finalize=true`. Profile2 remained byte-identical; Steam Cloud was disabled by the user and not changed.

The unchanged Release artifact was redeployed after process closure. Artifact, installed DLL, and pre-deploy backup all hash to `d576ac60e428213dd9d5d6e2be5a2ad7eba2ab3c96c7618ccbad4de3c9c62fcf`. Deployment manifest: `artifacts/deployment-backups/retarget-d576-death-20261111/deployment-manifest.json`.

## Evidence files

- Runtime summary: `artifacts/runtime-smoke/retarget-d576-death-pool-20261111/current-release-retarget-death-summary.json`.
- Full stopped probe rows: `artifacts/runtime-smoke/retarget-d576-death-pool-20261111/renderer-frame-probe-death.json`.
- Before-run, after-run-before-restore, and after-restore checkpoints are in the same runtime directory.
- Preserved generated Profile3 files: `artifacts/runtime-smoke/retarget-d576-death-pool-20261111/Profile3-generated-run-after-exit/`.
