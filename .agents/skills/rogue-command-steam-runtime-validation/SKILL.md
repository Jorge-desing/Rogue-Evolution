---
name: rogue-command-steam-runtime-validation
description: "Use when a Rogue Command claim needs live Steam evidence. Run a reversible, fail-closed validation session."
version: 2.4.0
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, Steam, runtime validation, BepInEx, MCP, QA Profile2, evidence, intermission, boss-node, deck-overview, reward-state]
    related_skills: [rogue-command-clean-room-diagnostics, rogue-command-harmony-policy, rogue-command-pack-validation, rogue-command-modding-tools]
---

# Rogue Command Steam Runtime Validation

Runbook for fail-closed, reversible in-game validation sessions on live Rogue Command Steam instances (AppID `1461910`).

---

## 1. Non-Negotiable Safety Gates & Profile Invariants

Stop immediately before mutation if any gate is violated:

1. **Saved-Profile Continuity:** Never start or reset a clean/empty profile; it can reset tutorial progression. Preserve completed tutorial state. For ordinary continuation, select an existing tutorial-complete profile through the native profile UI and continue its saved run. A new-run test requires explicit user authorization and a clone of an existing tutorial-complete source into an authorized QA slot. Checkpoint every profile and selector, preserve the source byte-for-byte, verify both tutorial flags on the clone, select it through native UI, and inspect the overwrite dialog before confirming. Never call tutorial completion/reset methods or edit `tutorialState.txt`. After exit, snapshot generated QA-run files, restore the original QA-slot files and selector, then verify the full profile checkpoint.
2. **Process Ownership:** Verify `Rogue Command.exe` state. If an unowned instance is running, perform read-only inspection only; never restart, deploy over, or kill it without authorization.
3. **Compatibility Identity:** Recheck version (`1.0.9`), Unity (`2021.3.45f2`), `Assembly-CSharp.dll` SHA-256 (`d2da384df...`), and MVID (`db0e9462...`).
4. **Exact Deployment:** Compute SHA-256 of local Release artifacts and compare with deployed files. Mismatch = blocked.
5. **Session-Wide Checkpoint:** Run `tools/runtime_profile_checkpoint.py create` on `Profiles/` while the game is closed. Never proceed without a verified baseline.
6. **Live Bridge Verification:** Confirm MCP listener URL and PID from `BepInEx/LogOutput.log` before issuing commands. Ping exit code 0 does NOT prove Unity bridge execution.
7. **Native Pause Gate:** Prior to mutation, confirm native pause (`Navigator._isPaused=true`, `Time.timeScale=0`, or stable `Time.time`).

---

## 2. Session State Machine

Every runtime session must progress sequentially through these explicit states:

| State | Required Evidence | Allowed Actions | No-Go Condition |
|---|---|---|---|
| `PREFLIGHT` | Git HEAD/status, process list, Steam status, identity tuple | Read-only checks | Unowned process running; identity mismatch |
| `CHECKPOINTED` | Selector hash and profile file hash manifest | Profile inspection | Selector unresolved; checkpoint error |
| `BUILT` | Release artifact SHA-256 and local test suite pass | Build artifacts | Build failed; test failure |
| `DEPLOYED` | Predeploy manifest, backup hash, installed hash == local hash | Deploy while closed | Hash mismatch on readback |
| `BRIDGED` | Live PID, listener URL, ping, 1 bounded Unity read | Read-only bridge calls | Stale target; HTTP error payload |
| `SCENARIO_READY` | Target scene, profile, and component snapshot | Re-resolve IDs on scene change | Component missing or scene mismatch |
| `OBSERVED` | Claim-specific postcondition readback + logs | 1 bounded mutation at a time | Only menu observed or forced injection |
| `EXITED` | Graceful game shutdown; verified process exit | Close process | Game still holds file locks |
| `RESTORED` | Restored deployment hashes == manifest; profile verified | Restore targets | Hash delta or profile alteration |
| `CLOSED` | Sanitized `runtime-evidence.json` with explicit conclusion | Archive session | Incomplete evidence or dangling state |

*Verdict classifications:* `PASS` (direct evidence observed), `FAIL` (assertion contradicted), `BLOCKED` (prerequisite failed), `UNKNOWN` (target behavior unreached).

---

## 3. Authoritative Verification Commands

```powershell
# 1. Preflight status & commit tracking
git status --short --branch; git rev-parse HEAD

# 2. Complete Profile Checkpoint (Execute while game process is CLOSED)
python tools/runtime_profile_checkpoint.py create `
  --profiles-root "$env:USERPROFILE\AppData\LocalLow\feneq\Rogue Command\Profiles" `
  --selector "$env:USERPROFILE\AppData\LocalLow\feneq\Rogue Command\Profiles\currentProfile.txt" `
  --output artifacts/runtime-smoke/<session-id>/profile-checkpoint.json

# 3. Post-Run Checkpoint Verification (Allows only explained Profile2 autosave)
python tools/runtime_profile_checkpoint.py verify `
  --manifest artifacts/runtime-smoke/<session-id>/profile-checkpoint.json `
  --profiles-root "$env:USERPROFILE\AppData\LocalLow\feneq\Rogue Command\Profiles" `
  --selector "$env:USERPROFILE\AppData\LocalLow\feneq\Rogue Command\Profiles\currentProfile.txt" `
  --allow-relative "Profile2/metaSavegame.dat"
```

---

## 4. BepInEx MCP Protocol & Execution Rules

- **HTTP Query Parameter Protocol:** `POST /mcp/component/call_method` requires ALL arguments as URL query parameters (`id`, `componentName`, `methodName`, `args`). NEVER send them in a JSON body.
- **Dynamic Identity Across Scenes:** Component IDs change per scene load (e.g. `LoaderDiagnosticsComponent` ID `-4880` in `MainMenu` becomes `-67812` in `IntermissionScreen`). Always re-resolve IDs via `find_objects_with_component` using full type name `RogueCommandExpansionLoader.Runtime.LoaderDiagnosticsComponent`.
- **Response Parsing:** Method return values are embedded inside the `message` string: `{"status":"ok", "message":"Called 'X' with N args. ReturnValue: <JSON>"}`.
- **Screenless UI Flow:** Discover Unity `Selectable` controls and labels with `diagnostics_get_ui_accessibility_census`, wait with `diagnostics_wait_for_ui_text`, and click by unique label or exact `hierarchyPath` with `diagnostics_ui_click_text` (supports unlabeled controls). Clicks use the real EventSystem/raycast route; the census does not capture pixels. Sequence bounded diagnostics actions with `automation_run_sequence`.
- **Process Lifecycle:** Use `system_get_game_process_state` to inspect PIDs, `system_launch_game` to start through Steam AppID `1461910` without changing the saved-profile selector, and `system_wait_for_game_bridge` to require a live scene query after ping. `diagnostics_request_graceful_exit` requires `confirmExit=true`; verify process closure and profile hashes after exit.
- **No Computer Use / Captures:** The loader enables `Application.runInBackground`. Do not switch to Computer Use or screenshots when MCP observations fail; retry bounded scene/UI queries, add telemetry, or classify the claim as `BLOCKED`/`UNKNOWN`.
- **Steam Cloud API:** `diagnostics_get_steam_cloud_state` reads account/app state through `SteamRemoteStorage.IsCloudEnabledForAccount/IsCloudEnabledForApp`. If Steamworks was not initialized by the game, the wrapper calls `SteamAPI.Init()` and shuts it down only if the wrapper initialized it. `diagnostics_set_steam_cloud_enabled(enabled, explicitUserAuthorization=true)` changes only this app's toggle, only after the user explicitly authorizes it. Never touch the account-wide toggle. Restore the chosen file source, verify Profile2/checkpoint, then re-enable Cloud and confirm its app state.

### Intermission Progression & Level Transition Chains
1. **Conserved Map Native Progression (Preferred / Zero Stall):**
   - Click `Continue Run` $\rightarrow$ Invoke `LoadScene.LoadLevel()` on `LoadNextLevel` GameObject.
   - Wait for `LoadingHider` in `Game` scene $\rightarrow$ Click its **INICIAR** button (ID `1008136`, child of `LoadingHider`, NOT the map launch button).
   - Confirmed result: Live match gameplay (`timeScale=1`, battlefield entities populated).
2. **Boss Node Progression:**
   - Coordinate raycast via `diagnostics_order_select_level_node(level=LevelCount, field=0)` on `OverWorldMapBoss`.
   - Never click `BossTriangle` (decorative mesh with no collider).
   - Gate with `RuntimeIntermissionCensus.IsNextLevelLoading()` to avoid duplicate coroutines that crash `MovementManager`.
3. **Synthetic Jump Seams (User-Authorized Fallback):**
   - `diagnostics_jump_to_level(level)` and `diagnostics_load_next_level` provide fail-closed advancement tagged with `synthetic=true`. Use only if natural progression is obstructed.

- Before an AI runtime sample, resolve the current stage's AI prefab/profile and its module list. A registry count such as `GetAiModulesDiagnosticsJson.registeredModuleCount` is availability only. If the existing saved route does not activate the target profile, stop at the native progression boundary and report the behavior as unverified instead of advancing solely to force that profile. `GetAiDirectorCensusJson.isAiDirectorAvailable` and `GetAiRosterJson` describe the live scene and should be captured alongside module-specific counters.
- The final boss is the special graph coordinate `(LevelMap.LevelCount, 0)`. `GetIntermissionNodeJson` now identifies it with `isBoss=true`; use read-only graph/node census before any selection or load action.

---

## 5. Claim-Specific Acceptance Matrix

| Acceptance Claim | Minimum Direct Evidence Required | Disallowed / Insufficient Proof |
|---|---|---|
| **Plugin Loads** | Deployed DLL hash match + BepInEx log line with 0 errors | Local build pass alone |
| **Pack Registers** | Loader log line naming pack + registered ID in runtime census | Manifest validation alone |
| **Blueprint Offer** | Live `ChooseCard` rendered slots with target factory/product | Pool inspection or forced spawn |
| **Building Placement** | Live placed building entity with target `EntityId` and bounds | Spawn command response alone |
| **Factory Production** | Queue tick, completion event, and produced unit linked to factory | Native factory placement alone |
| **Unit Rendering** | Live instance with active renderer, non-zero bounds, valid materials | Inactive prefab or bundle audit |
| **Native animation retarget** | `diagnostics_inspect_entity_visual` reports bound/unsuspended state and matching mapping counts; inspect the bounded source/target/computed local pose rows during locomotion/fire, then observe death suspension and pool-reuse resume | Static paths, profile presence, equal counts, or computed pose values without observed in-game pose changes |
| **Intermission Graph** | `diagnostics_get_intermission_graph` DAG showing nodes & paths | Static scene hierarchy search |
| **Sub-screen Actions** | `diagnostics_get_intermission_reward_state` currency readback | Assuming default player balances |
| **Level Transition** | Node selection + `LoadLevel()` + `LoadingHider` **INICIAR** click | Level count text change alone |
| **Tactical AI** | Profile tick trace, evaluated candidate, dispatched bounded order | Attaching profile without trace |
| **Geometry calibration** | At least three complete schema-v3 probe reports from one dense gameplay scene, camera pose, Unity version, and resolution, captured unpaused over at least 120 rendered frames each; each report requires a display-backed `Game` camera, positive time scale throughout, stable camera settings, and an unchanged enabled-renderer/LOD signature checked every 30 frames. Bind raw reports in `rogue-command/runtime-geometry-evidence/v1` with SHA-256, scene/level/stage/mode, camera and entity-mix context. A flat response may produce only a non-extrapolated candidate using 75% of the measured incremental triangle range and 25% reserve; the profile stays uncalibrated if the low/high population was not controlled or any asset demand is missing/mismatched. `calibrationReady` validates one capture only. | Paused or unstable captures, menu/intermission captures used as dense-scene calibration, mixed camera/viewpoint reports, unverified report hashes, uncontrolled low/high scene populations presented as a controlled capacity pair, loader's one-frame census, or frustum estimates presented as exact GPU draws or CPU/GPU timing |

---

## 6. End-of-Session Verification Checklist

- [ ] Checkout branch, commit hash, and Release artifact SHA-256 recorded.
- [ ] Existing saved profile selected through native profile UI after restart; no clean/new profile or tutorial bypass used.
- [ ] Selector and all-profile hashes checkpointed before any profile switch; tutorial completion and the selected save verified.
- [ ] BepInEx MCP listener URL matched to live PID; response payloads checked for errors.
- [ ] Native pause confirmed prior to automated inspections.
- [ ] Claim postconditions observed and recorded in `artifacts/runtime-smoke/<session-id>/`.
- [ ] Process gracefully exited; verified process termination before restoring files.
- [ ] Any test-modified save/meta files restored from the pre-run backup; tutorial and unrelated profile hashes verified, with the explicitly selected profile left active.
- [ ] `runtime-evidence.json` generated with explicit verdict (`PASS`, `FAIL`, `BLOCKED`, `UNKNOWN`).

## Two-Level Variant Acceptance

- After confirming the installed Release DLL hash and pack registration, select a known unit through the existing diagnostics path and run `diagnostics_order_apply_visual_variant` for a declared Level 2 variant. Verify the target entity's visual census shows the selected variant and module count, and inspect the matching canonical hardpoint groups.
- Apply the primary/reset state and verify module instances are removed while the original chassis, scale, tint blocks, and group active state return. Keep screenshots/logs and verdict under `artifacts/runtime-smoke/<session-id>/`; distinguish a successful command response from direct renderer/appearance evidence.
- Follow the runbook's Steam launch. For ordinary continuation tests, restart and use an existing tutorial-complete saved profile through native UI. For an authorized new run, clone an existing completed profile into an authorized QA slot and confirm the overwrite only after reading the native dialog. Never use a clean profile or bypass `FirstRun`. If the MCP bridge or direct postconditions are unavailable, report runtime status as `BLOCKED` or `UNKNOWN` and retain local-build/bundle evidence separately.

### Saved-profile retarget pass (2026-11-10)

- The user required an existing saved profile after restart. Profile1 was selected through the native UI with tutorial completion already true; the resumed route was level 4 boss. No clean slot, tutorial bypass, or tutorial-file edit was used.
- After `LoadScene.QuitGame`, restore test-modified metadata from the pre-run backup and restore the selector to its captured original value. This pass restored Profile1 metadata and selector `2`; all profile hashes matched the 09:38 preflight checkpoint. Keep the current default QA-profile procedure for future acceptance runs.

### Destroy-entry retarget follow-up (2026-11-10)

- Restart through Steam, select the existing tutorial-complete Profile2 with the native profile UI, and continue its saved run. Do not use a clean profile or tutorial bypass. This pass continued the save into Game level 4 with `mainTutorialIsFinished=true`, `playerHasFinishedLevel1=true`, selector `2`, and unchanged `savegame.dat` / `tutorialState.txt` hashes.
- A temporary BepInEx Harmony postfix captured the retarget profile after `SuspendForDeath` returned but before `EntityController.Destroy` continued: two direct-diagnostic-damage cases remained bound 8/8 and reported `IsSuspended=true`. This is not natural-combat or death-animation evidence. Remove the temporary Harmony patch/component and verify no test actors remain.
- After graceful exit, compare all profile files to the pre-run checkpoint. This run changed only `Profile2/metaSavegame.dat`; preserve its post-run bytes as evidence, restore the matching pre-run backup with the game closed, then rerun checkpoint verification. The restored selector and all profile files matched exactly. Trace: `artifacts/runtime-smoke/restart-saved-profile-20261110T061923/retarget-destroy-entry-follow-up.json`.

### Steam Cloud source conflict guard (2026-11-10)

- A Steam launch can stop at `SynchronizingCloud` / `cloudconflict` before it creates the game process. Do not try to bypass this by launching the executable directly or by silently choosing local/cloud data.
- Keep the all-profile checkpoint and a backup of the selected saved profile. Record the conflicted path and hashes, verify that no profile bytes changed, then ask the user which source Steam should retain. After their choice, re-check the selector, tutorial flags, and full profile checkpoint before continuing. Evidence for the 2026-11-10 Profile2 metadata conflict is in `artifacts/runtime-smoke/native-retarget-natural-death-20261110T140359Z/steam-cloud-launch-block.json`.
- AutoCloud may upload modified save metadata during the normal game-exit path. If restoring the selected Cloud snapshot locally after exit, verify both `cloud_log.txt` and that file's `remotecache.vdf` SHA-1; do not treat a byte-exact local profile restore as proof the remote copy also reverted. In the 2026-11-10 Profile2 run Steam uploaded `metaSavegame.dat` at change 228 before the original Cloud-selected bytes were restored locally. A corrective relaunch then stopped at `cloudconflict` before creating the game process. Keep the selected bytes safe and resolve the file-source conflict with the user before another sync. Evidence: `artifacts/runtime-smoke/retarget-animation-restart-profile2-20261110T1605Z/profile-restore-after-test.json`.

### Cloud-selected Profile2 death-visual test (2026-11-10)

- The user selected Steam Cloud as authoritative for the existing Profile2 run. The test resumed that saved run without tutorial calls or tutorial-file edits, then changed only local `Profile2/metaSavegame.dat`; `savegame.dat`, `tutorialState.txt`, and selector `2` matched the pre-run checkpoint while the process remained open.
- Preserve the Cloud-selected metadata backup and remote hash. A normal game exit can AutoCloud-upload changed local metadata; do not close/sync or restore files while the chosen Cloud source could be overwritten unless the user explicitly approves the sync-control step. No final post-exit checkpoint or remote restoration is claimed until that is resolved.
- Full runtime and profile caveats: `artifacts/runtime-smoke/retarget-death-visual-cloud-20261110170654/death-hold-runtime-result.json`.

### Native pre-spawn stack versus death

- Static CIL for the pinned build shows `EntityFactory.PrespawnEntity` pushes into `PrespawnedEntities`, `InstantiateEntity` pops and initializes the unit, and `EntityController.Destroy` decrements the prespawn count and calls `UnityEngine.Object.Destroy` without pushing the dead object back. Do not require a natural death-to-stack-return test for this native route; validate stack-pop reset independently from death suspension/animation. Evidence: `artifacts/runtime-smoke/native-retarget-natural-death-20261110T140359Z/native-prespawn-stack-static-evidence.json`.

### Screenshot-free retarget renderer probe

- Checkpoint all profiles and the selector, launch through Steam without changing the selector, select the existing saved profile through native UI, and verify the live run before sampling. Pause at `Time.timeScale=0`; focus the camera on a disposable entity and set `Animator.updateMode=UnscaledTime` only on that clone if animation must advance while paused.
- Start/read/stop the bounded renderer-frame probe. It records direct Game-camera callbacks, frustum/layer gates, Animator and retarget state, and sampled `BakeMesh` deltas; it captures no pixels. Close with `diagnostics_request_graceful_exit(confirmExit=true)`, verify process exit, restore changed profile bytes from the checkpoint backup, then require exact all-profile and selector matches.
- The 2026-11-10 pass used existing Profile2, kept the tutorial complete and selector `2`, restored `metaSavegame.dat` to SHA-256 `9047CD412770724B9736815D6B1CC6C1ABE1419E43DA686AD579B7CBD0965090`, and verified the full checkpoint. Steam Cloud was already disabled by the user; no remote Cloud state was changed or verified. Runtime trace: `artifacts/runtime-smoke/retarget-camera-render-probe-20261110/renderer-frame-probe-final.json`.

### Profile2 AI route gate (2026-11-11)

- Keep the existing selector `2`, verify Profile2 tutorial flags, checkpoint every profile, and back up the active Profile2 files before a reversible new-run validation. Read any overwrite confirmation's title, body, and exact confirm/cancel labels before invoking it.
- Navigate through the native RunSetup/story flow with BepInEx MCP. The run opener used a Unity `Selectable` at `RunSetup::RunOpenerStoryCanvas/CONTENTBackgroundImageEngineer/CONTENTTEXTStoryBoxEngineerHope/ConfirmButton`; use the exact accessibility-census path with `diagnostics_ui_click_text` for this class of control.
- A preview label such as `Patrulla Slime PCX` is not a stable profile/template ID. If the live stage/profile link cannot be read, classify association as `UNKNOWN` and do not advance to another map node just to find a target profile.
- After the game is closed, restore every backed-up Profile2 file and verify the all-profile checkpoint and original selector. This pass restored Profile2 byte-for-byte, kept tutorial flags true, and made no Cloud-setting change. It found no live AI director/roster/factory and did not prove scheduled construction. Evidence: `artifacts/runtime-smoke/ai-scheduled-build-20261111T012405Z/`.

### Stable stage association read (2026-11-11)

- Before advancing a saved route for an AI test, call `diagnostics_get_active_stage_ai_association`. Require `stageIdentityStatus=resolved` and record `qualifiedStageId` plus `identitySource`; do not infer an ID from localized preview text.
- Record stage-declared IDs (`declaredAiProfileIds`) separately from initialized loader-bound runtime profiles and counts. A native stage with no loader-declared IDs and an intermission with zero runtime bindings do not establish combat AI behavior. Continue to the battle runtime gate only when a stage/profile target is directly resolved and the user-approved scenario requires progression.
- The 2026-11-11 read resumed existing Profile2 with completed tutorial flags, stopped at intermission, and did not advance a route node. The stable stage resolved to native `StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit`; no AI execution was measured. The full all-profile checkpoint matched exactly and Steam Cloud settings were not changed. Evidence: `artifacts/runtime-smoke/active-stage-ai-association-20261111T0218Z/runtime-evidence.json`.
- The endpoint returns the bounded captured stage order in `stageSequenceRows`, with `resolved`/`partial`/`unavailable` status, stable IDs, declared profile IDs, and omission counters. Resolve only a target row whose identity is `resolved` and whose index is within `totalStageSequenceCount`. A partial sequence with a resolved target row supports that row's declared configuration while the full sequence remains partial; an unresolved or absent target stays `UNKNOWN`. Do not select unrelated nodes just to obtain a battle sample.
- The authorized 2026-11-11 Profile3 clone/new-run attempt selected several map nodes but did not reveal a stable AI-profile association or reach Game-scene AI evidence; classify it as inconclusive and do not repeat route progression for discovery. Profile2 and its tutorial state remained unchanged, selector `2` was restored, only QA Profile3 run files changed, the all-profile checkpoint passed, and Cloud settings were untouched: `artifacts/runtime-smoke/ai-counterstrike-runtime-20261111/profile-checkpoint-after-pass.json`.
- A later authorized Profile3 new-run read identified configured stage index 13 as `sample-reactive-ai:expansion:sample-reactive-ai:counterstrike-lane`, declaring `sample-reactive-ai:base-defense`. The active stage was native index 0 with no declared or runtime-bound profiles. The endpoint returned 32 rows for 14 configured stages: indices 0–13 resolved, while 14–31 were unresolved, so overall status remained `partial`. No route was selected and no AI behavior was observed. After exit, Profile3 had no `savegame.dat`; its three surviving files were snapshotted, the original four-file QA backup and selector `2` were restored, and the complete checkpoint matched exactly. Evidence: `artifacts/runtime-smoke/profile3-new-run-20261111/ai-profile-association-runtime-summary.json` and `Profile3-generated-run-after-exit/manifest.json`.
- Post-deploy smoke on 2026-11-11 reached `MainMenu` and BepInEx MCP responded, but the bridge and game process exited after `ProfilePanel.SetCurrentProfile(3)` was called on the inactive save overlay. Profile3 `savegame.dat`, `metaSavegame.dat`, and completed tutorial state were unchanged; only the selector changed temporarily and was restored to exact checkpoint value `2`, after which the all-profile checkpoint matched. Do not treat that hidden-component method call as an active UI click; use the active native control if another smoke is authorized. The new stage-sequence payload was not read in-game in this attempt. Report: `artifacts/runtime-smoke/stage-ai-sequence-mcp-20261111/runtime-smoke-summary.json`.

### Cloned QA profile for filtered retarget validation (2026-11-11)

- When the user explicitly authorizes a disposable clone, checkpoint every profile and the selector first, preserve the source profile byte-for-byte, clone only to an authorized slot, and verify tutorial completion before launch. Select the clone and continue/start through native UI; do not use a clean profile or edit tutorial flags.
- A clone may contain the source's saved run. If testing a new run, read the native overwrite dialogue before confirming; snapshot the generated QA files after exit, restore the destination slot from its original backup and restore the original selector. Never reset tutorial state to make the slot usable.
- This pass used Profile3 cloned from Profile2, with selector `3` and both tutorial completion flags true. After the live test, Rogue Command exited normally; Profile3 was restored from its pre-clone backup, its generated run save was preserved under the runtime artifact directory, the selector returned to `2`, and `runtime_profile_checkpoint.py verify` reported exact profile and selector matches with `safe_to_finalize=true`. Steam Cloud was not changed.
- Clone-and-new-run variant, only when the user explicitly authorizes it: preserve a pre-clone backup of the destination slot, copy the verified source profile files, select the clone through native profile UI, and start the new run through native UI. Keep the source profile and tutorial flags unchanged. The authorized 2026-11-11 pass left Profile3 as the requested QA clone with its new run; it restored Profile2 byte-for-byte and selector `2`, verified all non-QA profiles against the post-clone checkpoint, and retained the original Profile3 files under `artifacts/runtime-smoke/retarget-final-release-20261111T050500Z/Profile3-pre-clone-backup/`. Steam Cloud was already disabled by the user and was not changed.
- Exact final Release SHA `fe08d495c3236a174c31e89bfae236c79f6af42541404770845a1043934f1856` was exercised through BepInEx MCP in that Profile3 run. The native Move / filtered Walk sample returned 32 rows (8 per each of four skinned batches) with sampled 8/8 unsuspended retarget mappings and positive post-baseline vertex deltas observed. This supports live structured retarget/deformation telemetry only, not pixel appearance, gait quality, or AI execution. Profile and selector readback: `artifacts/runtime-smoke/retarget-final-release-20261111T050500Z/profile-checkpoint-after-runtime.json`; runtime summary: `artifacts/runtime-smoke/retarget-final-release-20261111T050500Z/final-release-retarget-summary.json`.
- Current Release SHA `2E740D387F600CAF0023ED7254F9F41ECF9E69E34E548328BFF147D250BC3DFB` was re-exercised in the existing Profile3 QA run using only BepInEx MCP. Native profile UI returned the selector to Profile2 before graceful exit. The original and final all-profile checkpoints both had selector `2`; only the authorized QA run's Profile3 `metaSavegame.dat` differed from the initial checkpoint, and all four Profile2 files including tutorial state were byte-identical. The final checkpoint passed `runtime_profile_checkpoint.py verify` with `safe_to_finalize=true`. Steam Cloud settings were not changed. Evidence: `docs/research/RETARGET-CURRENT-RELEASE-RUNTIME-2026-11-11.md`.
- The current Release produced filtered Walk and diagnostic-Death telemetry across four of five skinned renderers. Death used a temporary typed helper and direct lethal diagnostic damage; it is not natural-combat evidence. The helper and frame observer were absent after cleanup; no screenshots or pixel claims were made.
- d576 Release SHA-256 `d576ac60e428213dd9d5d6e2be5a2ad7eba2ab3c96c7618ccbad4de3c9c62fcf` was exercised on the existing tutorial-complete Profile3 QA clone after a user-authorized native new run. Profile2 and both tutorial flags were preserved; the generated Profile3 files were snapshotted, the original Profile3 files and selector `2` restored, and the all-profile checkpoint matched exactly. BepInEx MCP recorded 48 filtered Walk rows over four renderers with 8/8 unsuspended retarget mappings and 44/44 positive comparable BakeMesh deltas. The probe reached its time cap before a retained-token stop/census; do not report observer-census cleanup. `Navigator.Pause()` success also disagreed with the `isPaused` readback, while `Time.timeScale=0` and stable scaled time verified the freeze. Evidence: `docs/research/RETARGET-D576-RUNTIME-2026-11-11.md`.
- d576 death follow-up (2026-11-11): after the user-authorized new run on Profile3, generated save/meta files were copied to `artifacts/runtime-smoke/retarget-d576-death-pool-20261111/Profile3-generated-run-after-exit/`. With the game closed, the four exact pre-run Profile3 files and one-byte selector `2` were restored; the 21-entry checkpoint verifier reported exact matches, no additions/removals, and `safe_to_finalize=true`. Profile2 and the user-disabled Steam Cloud setting remained unchanged. See `docs/research/RETARGET-D576-DEATH-RUNTIME-2026-11-11.md`.
- d576 factory-produced Walk follow-up (2026-11-11): use the tutorial-complete Profile3 QA slot only after checkpointing all profiles and reading the native overwrite dialog. The active `LoadingHider` must be cleared through its real `Iniciar` button before treating Move as gameplay. After exit, Profile3 and selector `2` matched the original all-profile checkpoint exactly; Profile2 and tutorial flags were unchanged. See `docs/research/RETARGET-D576-FACTORY-WALK-RUNTIME-2026-11-11.md`.
- Current Release C1AE multi-clip pass used the authorized tutorial-complete Profile3 clone. After normal exit, generated Profile3 files were snapshotted, the four original Profile3 files and selector byte `2` were restored, and the all-profile checkpoint passed exact verification. Profile2/tutorial data and the user-disabled Steam Cloud setting were unchanged. The installed Release was backed up before recopy and post-copy SHA-256 matched the artifact. See `docs/research/RETARGET-C1AE-MULTICLIP-RUNTIME-2026-11-11.md`.
- Placement-obstruction follow-up used the already saved, tutorial-complete Profile3 run and the previously installed C1AE SHA `C1AE90B31A58DCA68C941C05D4E1FE0ED4C9A8D97C103529FC433767E9655428`. The Sentinel footprint overlapped two mill cells and two crystal cells; a native-accepted site at `(-175,-165)` was then committed, and RoboCrystalMill produced eight harvesters. The game exited normally; generated Profile3 files were retained, the exact QA backup and selector byte `2` were restored, and the complete checkpoint verified with `safe_to_finalize=true`. Steam Cloud settings were not changed. Report: `docs/research/PLACEMENT-OBSTRUCTION-IDENTITY-2026-11-11.md`.
- Loader 1.20.10 Release SHA `81573ca10d6b78c4b8e4ed9985f3987f17fcf1ebc9d6a19475b6c7547ec545b7` was built, deployed, and live-checked through BepInEx MCP. Two same-`EntityId` Walkers received keys 278/279; ambiguous string lookup failed closed; key 278 completed a native Move/Action_Walk telemetry sample with 8/8 unsuspended mappings across four visible renderers. The probe stopped and component census was empty. The exact tutorial-complete Profile3 pre-run files and selector byte `2` were restored; the all-profile checkpoint passed `safe_to_finalize=true`. Occupant identities added to placement cells remain runtime-unverified. See `artifacts/runtime-smoke/unique-key-retarget-release-20261111T173856Z/unique-key-retarget-summary.json` and `docs/research/PLACEMENT-OBSTRUCTION-IDENTITY-2026-11-11.md`.

### Exact 1.20.10 placement obstruction read (2026-11-11)

- The existing tutorial-complete Profile3 saved run was selected through the native profile UI. Its pre-run save backup and all-profile checkpoint were verified; after normal exit, Profile3 and selector `2` were restored exactly. No clean profile, tutorial reset, or Cloud setting change was used.
- Read-only Sentinel preflight at `(-165,-195)` returned one `Building` footprint cell at grid `(-17,-20)`, three `Ground` cells, and secondary fog on all four. The 1.20.10 payload included `occupantEntities=[]` for every cell, so no matching live `EntityController` identity was surfaced for the map blocker. The field and empty-row behavior are live-verified; non-empty rows are not.
- `SetNavigatorPausedJson(true)` returned success, but the subsequent native state flag remained `isPaused=false`. `timeScale=0` and identical `scaledTime` about four seconds apart support only the claim that scaled simulation time was frozen; do not report the native pause flag as verified.
- Cleanup evidence is in `artifacts/runtime-smoke/placement-obstruction-identity-20261111T1800/`; complete report: `docs/research/PLACEMENT-OBSTRUCTION-IDENTITY-2026-11-11.md`.
