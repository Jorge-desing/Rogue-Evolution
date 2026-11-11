# Native Rig Retarget: Current Release Runtime Pass

## Scope and build identity

This pass re-exercised the current Release loader against the Ironclad Walker native-rig retarget path. No loader source changed and no rebuild was needed. Game identity: Rogue Command 1.0.9, Unity 2021.3.45f2, BepInEx 5.4.23.5, Mono x64; Assembly-CSharp SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`; MVID `db0e9462-128d-4296-9900-de18b4bd0c00`. Release SHA-256: `2E740D387F600CAF0023ED7254F9F41ECF9E69E34E548328BFF147D250BC3DFB`.

The loader DLL was deployed after the game process closed. A pre-deploy copy and manifest were recorded under `artifacts/deployment-backups/`; the backup SHA matched the prior installed file, and the post-copy installed SHA matched the Release artifact.

## Live BepInEx MCP evidence

The run used the existing QA Profile3 and its saved run. Profile3 was selected through the game's native profile menu; the selector was returned to Profile2 through the same menu before a graceful exit. The stage resolved to native `StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit`, with zero loader-declared AI profiles and zero runtime-bound loader AI profiles. This pass provides no mod-AI execution evidence.

A disposable `sample-native:assault-walker` was spawned on the player side. Its rig was `SkeletalArmature`, its avatar was valid, root motion was disabled, eight of eight retarget mappings were bound, and five skinned renderers were present. The bounded frame probe sampled four renderers, eight rows apiece. During a native Move order, every sampled row reported `Action_Walk`, `RCL_IsMoving=true`, bound and unsuspended 8/8 mappings, and visible/in-frustum camera state. Seven of eight frames per sampled renderer had a positive post-baseline `BakeMesh` vertex delta. The fifth renderer was omitted because of the probe's four-renderer cap.

For the death path, a temporary typed BepInEx helper applied `EntityController.TakeDamage(999f, null, true, true)` to that disposable actor and changed only that actor's Animator update mode from Normal to UnscaledTime so the clip could advance while the match was paused. The 32 rows across four sampled renderers reported `IroncladWalker_Action_Death`, stationary movement state, pending retirement, suspended retarget copying with mappings still bound 8/8, and visible/in-frustum camera state. Seven of eight rows per renderer had positive sampled vertex deltas. The scene death-retirement counter reached one; scene-wide destroy transpiler counters were two. The actor disappeared, and censuses found no temporary helper or renderer-probe component remaining.

## Save preservation and limits

The original and final checkpoints both recorded selector `2`. All four Profile2 files, including `tutorialState.txt`, matched their original SHA-256 values. The only change against the initial all-profile checkpoint was the authorized QA run's `Profile3/metaSavegame.dat`. The final checkpoint at `artifacts/runtime-smoke/retarget-resume-20261111/profile-checkpoint-after-final-exit.json` verifies the final profile tree and selector exactly. Steam Cloud settings were not changed in this pass.

This is structured in-game Animator, retarget, camera-frustum, lifecycle, and sampled-deformation telemetry. No screenshot or pixel capture was used. Walk-cycle quality, death-pose quality, natural-combat death, ordinary blueprint placement, and mod AI execution remain unverified. The diagnostic lethal-damage route does not establish natural combat behavior.

## Evidence files

- Full structured runtime summary: `artifacts/runtime-smoke/retarget-resume-20261111/current-release-retarget-runtime-summary.json`.
- Initial and final profile checkpoints: `artifacts/runtime-smoke/retarget-resume-20261111/profile-checkpoint-before.json` and `profile-checkpoint-after-final-exit.json`.
- Deployment backup manifest: `artifacts/deployment-backups/RogueCommandExpansionLoader-20261111T065923Z.manifest.json`.
