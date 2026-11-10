---
name: rogue-command-steam-runtime-validation
description: "Use when a Rogue Command claim needs live Steam evidence. Run a reversible, fail-closed validation session."
version: 2.3.0
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

1. **Saved-Profile Continuity:** Never start or reset a clean/new profile for a run that must preserve completed tutorial progress; doing so re-triggers the tutorial. For ordinary continuation testing, restart the game and select an existing saved profile through the native profile-management UI, verify its tutorial remains completed, and use its saved run normally. Do not call tutorial methods or edit `tutorialState.txt`. Use a distinct isolated QA profile with no saved run only when a test specifically requires bypassing `FirstRun`; complete tutorial progression normally through the game before that test. If switching the authoritative selector, checkpoint its value and every user-profile file first. Back up a continued run and restore any test-modified save/meta files after exit; leave the user's explicitly selected profile active.
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
- Follow the runbook's Steam launch. For ordinary continuation tests, restart and use an existing tutorial-complete saved profile through the native UI; reserve the isolated no-save QA procedure for tests that truly bypass `FirstRun`. If the MCP bridge or direct postconditions are unavailable, report runtime status as `BLOCKED` or `UNKNOWN` and retain local-build/bundle evidence separately.

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
