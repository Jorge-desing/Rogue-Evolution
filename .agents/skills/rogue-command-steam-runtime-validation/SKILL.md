---
name: rogue-command-steam-runtime-validation
description: "Use when a Rogue Command claim needs live Steam evidence. Run a reversible, fail-closed validation session."
version: 2.1.0
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, Steam, runtime validation, BepInEx, MCP, QA Profile2, evidence, intermission, boss-node, deck-overview, reward-state]
    related_skills: [rogue-command-clean-room-diagnostics, rogue-command-harmony-policy, rogue-command-pack-validation, rogue-command-modding-tools]
---

# Rogue Command Steam Runtime Validation

Use this skill when the acceptance claim names behavior inside a live Rogue Command process: loader startup, pack registration, an offer, selection, building placement, production, entity instantiation, rendering, stage generation, intermission navigation, sub-screen reward selection, or AI decisions. Static inspection, Release builds, Unity imports, AssetBundle audits, and CLI harnesses are useful prerequisites but are **not** in-game evidence.

This skill is an operational gate, not a launch script. It must fail closed when the target process, compatibility identity, installed artifact, profile, bridge, scene, or postcondition is ambiguous.

## 1. Non-negotiable safety gates

Stop before mutation when any gate is false:

1. **Repository and evidence scope** — Read `AGENTS.md`, `README.md`, `docs/research/AGENT-HANDOFF.md`, `docs/research/STEAM-RUNTIME-CONTROL.md`, and this skill. Keep reports, logs, screenshots, checkpoints, and backups under ignored `artifacts/runtime-smoke/<session-id>/`. Never commit saves, BepInEx, Harmony, game assemblies, credentials, or raw secrets.
2. **Process ownership** — Resolve the actual process named `Rogue Command.exe` and Steam parent/launch state. If a game instance is already running, perform read-only inspection only; do not restart it, redeploy over it, switch profiles, or abandon its run.
3. **Compatibility identity** — Before using a reflected type, method, field, metadata token, or native tutorial API, verify the complete tuple together: Rogue Command version, Unity version, `Assembly-CSharp.dll` SHA-256, and MVID. The historical baseline (`1.0.9`, Unity `2021.3.45f2`, SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`) is a lookup hint only; it is not current evidence.
4. **Exact deployment** — Build the artifact to test, hash it, and compare it with every installed target before launch. A successful copy is not enough. A mismatch means the run exercises stale code and must be classified as blocked until corrected.
5. **Profile isolation** — Use the user's designated `Profile2` QA profile after resolving the authoritative selector through `ProfileManager.CurrentProfileTextPath` under `Application.persistentDataPath`. Do not switch to Profile3, edit `tutorialState.txt`, alter the selector while the game is running, or overwrite a save without a fresh checkpoint and explicit authorization. Profile2 is the established QA profile; do not gate its use on a repeated tutorial check or invent a bypass.
6. **Checkpoint completeness** — With the game closed, checkpoint the selector and every file below the authoritative profiles directory. Use `tools/runtime_profile_checkpoint.py`; the older `tools/profile_hash_audit.py` audits only one profile and is not sufficient for a session-wide checkpoint.
7. **Bridge identity** — Resolve the BepInEx MCP listener from the current game log or verified bridge source, then match it to the live process. A local MCP server, a stale `Listening` line, a successful ping, or an exit code of zero does not establish a live Unity target.
8. **Pause and mutation** — Read first. Before any action that advances or mutates a run, confirm native pause state (`Navigator._isPaused=true`) and `Time.timeScale=0`, or record two stable scaled-clock observations. A visible menu is not proof of pause.

## 2. Session state machine

Use one unique session directory and record a JSON event/result for every transition. Do not skip states or report a later state when an earlier gate failed.

| State | Required evidence | Allowed actions | No-go condition |
|---|---|---|---|
| `PREFLIGHT` | repo HEAD/branch/status, process list, Steam state, game identity, task claim | read-only filesystem/process checks | running process is not owned/understood, identity incomplete |
| `CHECKPOINTED` | selector value/hash and all profile file hashes | no profile mutation | selector cannot be resolved or checkpoint is partial |
| `BUILT` | fresh Release artifact hash and relevant local test output | no deployment yet | build/test failed or artifact path is ambiguous |
| `DEPLOYED` | per-target backup, predeploy manifest, installed hashes equal local hashes | replace only declared targets while game is closed | target list or hash read-back differs |
| `BRIDGED` | live PID, listener endpoint, ping/capabilities, one bounded Unity read | read-only bridge calls | payload is an error, target is stale, or only ping succeeds |
| `SCENARIO_READY` | exact scene/profile/object/component and precondition snapshot | resolve IDs again after scene changes | name/type/scene/active state/member values disagree |
| `OBSERVED` | claim-specific postcondition plus log/bridge/screenshot correlation | one bounded mutation at a time | only menu navigation, template presence, or diagnostic injection observed |
| `EXITED` | graceful game exit and process absence | restore only after process exit | process still owns an installation/profile file |
| `RESTORED` | deployment hashes, selector, and profile verification | no further game calls | restore mismatch or unexpected profile delta |
| `CLOSED` | sanitized evidence index and explicit conclusion | finish | any unclassified failure or missing evidence boundary |

Use `BLOCKED` for a failed prerequisite, `UNKNOWN` when the target behavior was not reached, `FAIL` for a reached assertion that contradicted its expectation, and `PASS` only for a reached assertion with direct evidence. `NOT_TESTED` is not a pass.

## 3. Preflight and checkpoint commands

Commands below are examples of the repository's verified interfaces. Substitute the current paths; do not copy historical absolute paths or hashes into a new session.

```text
# From the repository root; keep output under artifacts/.
git status --short --branch
git rev-parse HEAD

# All-profile checkpoint. This does not modify the game tree.
PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe \
  tools/runtime_profile_checkpoint.py create \
  --profiles-root <authoritative-persistent-data>/Profiles \
  --selector <authoritative-persistent-data>/Profiles/currentProfile.txt \
  --output artifacts/runtime-smoke/<session-id>/profile-checkpoint.json

# Discover the current first-class bridge tool set instead of relying on stale counts.
PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe \
  tools/bepinex_mcp_server.py --list-tools
```

The checkpoint command records raw selector text, size, SHA-256, and every regular file below the profile root. It refuses symlinks, unstable files, unsafe manifest paths, and overwriting an existing checkpoint. After the run, verify with the same authoritative paths:

```text
PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe \
  tools/runtime_profile_checkpoint.py verify \
  --manifest artifacts/runtime-smoke/<session-id>/profile-checkpoint.json \
  --profiles-root <authoritative-persistent-data>/Profiles \
  --selector <authoritative-persistent-data>/Profiles/currentProfile.txt
```

Append `--allow-relative Profile2/metaSavegame.dat` only when the run actually advanced the designated QA profile and the evidence explains that autosave. It does not permit selector changes, `savegame.dat` changes, tutorial-state changes, additions, deletions, or changes in other profiles. If the run did not advance, omit the allow-list and require an exact match.

## 4. BepInEx MCP control path

- Resolve the bridge URL and command shape from the installed `BepInExMCP` plugin, not assumptions. Prefer the current `BepInEx/LogOutput.log` listener line; if absent, verify the base URL and routes in `ModdersHelperApp.py`/`bridge_client.py` and match the listening port to the `Rogue Command.exe` process before any read-only probe. Do not infer a bridge target from unrelated local MCP transport ports; verify the base URL and listening port from `BepInEx/LogOutput.log` or `ModdersHelperApp.py`/`bridge_client.py` before any read-only probe.
- A bridge ping timeout when no `Rogue Command.exe` process is running is expected behavior, not a failure. The presence of `BepInExMCP.dll` and a “Listening” line in `BepInEx/LogOutput.log` only prove the plugin loaded on a previous run; they do not prove a live Unity bridge is currently connected. Recheck the game process and bridge together before diagnosing connectivity.
- Commands are path-based (`http://127.0.0.1:8080/mcp/<command>`), with query parameters for filters such as `componentName`. A JSON body with a `method` field is not the protocol for this bridge.
- **`component/call_method` protocol**: ALL parameters must be passed as URL query parameters, NOT in a JSON POST body. The required params are `id` (game object id), `componentName` (full type name), `methodName` (exact method name), and optional `args` (JSON array, URL-encoded). Example: `POST /mcp/component/call_method?id=878082&componentName=Michsky.UI.Reach.ButtonManager&methodName=InvokeOnClick&args=%5B%5D`. Sending them in a JSON body returns "Missing or invalid 'id' parameter". Param names are `methodName` (not `method`) and `componentName` (not `component`).
- **`diagnostics_ui_press_button` name search is fragile**: `buttonName`/`selector` lookups can return "button has no id" even for controls that exist (the server's search-passthrough envelope handling collapses empties). Resolve the id first with `search_gameobjects` MCP tool, then click via `call_component_method` on `TryUiClickJson("[<name>, <id>]")`. Do not keep retrying `diagnostics_ui_press_button` by name.
- **LoaderDiagnostics identity changes across scenes**: the `LoaderDiagnostics` GameObject id resolved in MainMenu (`-4880`) is stale in IntermissionScreen (`-67812`); calling into the stale id returns HTTP 500 "Ensure Rogue Command is running." Re-resolve the id via `find_objects_with_component` after every scene change. The short `componentName=LoaderDiagnosticsComponent` is not a substitute for the full type name in `call_component_method` — use `RogueCommandExpansionLoader.Runtime.LoaderDiagnosticsComponent`.
- **Loading-transition non-determinism — do not loop it**: After calling a synthetic/state-falsifying `LoadLevel()` seam, the game may show the `Cargando...`/loading screen and stall indefinitely with no log error, `LoadingHider` never appearing and the census unchanged. Root cause (static trace, `docs/research/LOADLEVEL-PRECONDITIONS.md`): post-jump `LoadNextLevel` waits without timeout on `InitMap.MapIsReady_Static()` and the only known `StartInit_Static` caller runs solely for boss flow — so the no-jump path below (conserved map, world already generated) is preferred; treat the jump+coroutine+`LoadLevel` chain as a stall-prone last resort, not a stable chain: stop re-issuing the load call; classify as `NOT_TRANSITIONED` and continue HUD/MCP work only from the confirmed post-transition state.
- **Synthetic state advancement (user-authorized 2026-11-08)**: The user explicitly authorized falsifying game state for runtime-validation automation. The loader ships two sanctioned, fail-closed seams: `diagnostics_jump_to_level(level, finished?)` → `LoaderDiagnosticsComponent.TryJumpToLevelJson` (reflection on `StageMap.JumpTo`, int overload only; bool overload is absent) and `diagnostics_load_next_level` → `TryLoadNextLevelJson` (reflection on `LoadScene.StartCoroutine("LoadNextLevel")`). Every result carries `kind=syntheticJumpResult` and `synthetic=true`, the before/after stage/level, and never touches profile/save files. The authorization is recorded in `docs/research/AGENT-HANDOFF.md` (the equivalent `AGENTS.md` edit was blocked by the protected-file guard; silence is not consent). Use only under the Profile2 checkpoint discipline; label these results clearly as synthetic in every evidence file. The native `TryEnterLevelNodeWorkflow` (deterministic choosable-node picker) remains the first choice; on a conserved Intermission map (world already generated) a single `LoadLevel()` succeeds before any synthetic seam; use the seams only when neither the natural node workflow nor the no-jump `LoadLevel()` transitions.
- **Verified live entry chains from Intermission Screen (2026-11-08)**:
  1. *Standard level progression (conserved map, no-jump)*: `Continue Run` (Michsky UI click) → single native `LoadScene.LoadLevel()` method call via `component/call_method` (component name `LoadScene`, GO `LoadNextLevel`) on the conserved map → census advances one level (stage 0 / level 3→4) with `LoadingHider` active in `Game` scene (GO `LoadingHider`, verified with `interactable` Button `1008136` under it) → click its **INICIAR** Unity UI button (GO `Button` under `LoadingHider`, is NOT the Michsky map `LaunchButton`) → live gameplay (`GetNavigatorStateJson` timeScale=1; verified 46/46 snapshot getters, 15/15 non-snapshot getters, 14 battlefield entities, AI 12 buildings, 2 factories producing).
  2. *Boss level entry (coordinate selection)*: Locate `OverWorldMapBoss` (at `level = LevelCount, field = 0, isBoss = true`) → select via coordinate raycast (`diagnostics_order_select_level_node` level=4 field=0) observing `isCurrentNodeOfPath = true` → single native `LoadScene.LoadLevel()` call → transition to `Game` scene with `isBoss = true` and 13 battlefield entities.
  - *Pitfall - Decorative `BossTriangle` vs Interactive `OverWorldMapBoss`*: Map `BossTriangle` objects carry only `MeshFilter` and `MeshRenderer` components and have no interactive collider or selection logic — never click them as a progression step. Always target `OverWorldMapBoss` which hosts the selection collider and native event dispatch.
  - *Pitfall - Never trigger concurrent `LoadNextLevel` coroutines*: In Rogue Command, clicking an overworld map node (`OverWorldMapEnemy/Boss.OnMouseDown`) already dispatches `_clickListener.OnClickedOnOverworldMapNode() -> LoadScene.LoadLevel()`, which sets `LoadScene.NextLevelIsLoading_Static() = true` and yields one frame before starting `StartCoroutine("LoadNextLevel")`. If any diagnostic tool concurrently triggers a second `StartCoroutine("LoadNextLevel")` (bypassing `_nextLevelIsLoading`), Unity loads the `Game` scene additively twice in parallel, which creates duplicate `MovementManager` singletons and crashes inside `Unity.Jobs.JobHandle:ScheduleBatchedJobsAndComplete`. Always gate transitions with `RuntimeIntermissionCensus.IsNextLevelLoading()`.
  - *Historical note*: the older `JumpTo(4) → LoadNextLevel` coroutine → `LoadLevel()` chain transitioned once in ~5 fresh-process attempts and stall-remaindered in the others (empty `Cargando...` bar, `LoadingHider` never populated, no errors in BepInEx/Player logs) because post-jump `MapIsReady` never becomes true — do not loop it; see `docs/research/LOADLEVEL-PRECONDITIONS.md`.
  - *Pause nuance*: `Navigator.Pause()` returns `isPaused=true` but a later live read may report `_isPaused=false` with `timeScale=0`; the accepted pause proof is a stable scaled clock (`Time.time`) across separated reads while `unscaledTime` advances.
- **Intermission telemetry & sub-screen action suite (2026-11-08)**:
  - `diagnostics_get_intermission_census`: Scans all `LevelMapNode` instances plus `OverWorldMapBoss` (`level = LevelCount, field = 0`), reporting node coordinates, choosable state, rest/shop/reward flags, and incoming/outgoing edge connections.
  - `diagnostics_get_intermission_graph`: Returns a forward-navigable DAG of the campaign map, computing branch factors, route progression, and recommending path trajectories toward specific rewards (e.g. `Coins`, `Card`, `Upgrade`, `Relic`, `Research`, `DropPod`).
  - `diagnostics_get_intermission_reward_state`: Reads live persistent currencies (`currentCoins`, `currentStartCrystals`, `currentDropPodCount`, `currentRerollCount`, `currentResearchPoints`), open reward queues, `ChooseCard` offered slots, and the complete deck card inventory (`DeckCardInfo` with attached upgrades and factory references).
  - `diagnostics_order_select_level_node`: Activates nodes via coordinate raycasting (`WorldToScreenPoint` + `Physics.Raycast` → `SendMessage OnMouseEnter/OnMouseDown`), verified for regular nodes and boss nodes without relying on fragile UI element searches.
  - `diagnostics_order_set_deck_overview_open`: Safely controls `DeckOverview` modal visibility (`ModalWindowManager`), verified with live read-backs.
  - *Fail-closed reward actions*: `diagnostics_order_choose_card_reward`, `diagnostics_order_skip_card_reward`, `diagnostics_order_reroll_card_reward`, and `diagnostics_order_assign_card_upgrade` enforce strict safety gates. When sub-screens are inactive or slots are invalid, they fail closed immediately, returning `success=false` and leaving game state untouched.
- **UnityExplorer toggle**: F7 opens the UnityExplorer UI in-game. Useful for inspecting Animator states, SkinnedMeshRenderer bones, and transform hierarchies at runtime.
- Useful commands include `system/ping`, `system/capabilities`, `scene/list_root_gameobjects`, `scene:find_objects_with_component?componentName=...`, `scene/search_gameobjects?name=...&includeInactive=true&limit=...`, `gameobject/inspect_components`, `component/get_details`, `component/get_member`, `component/call_method`, and `mod:inject_class`.
- `scene:find_objects_with_component` returns the component-bearing objects for the queried type. Save the raw response before filtering; DontDestroyOnLoad templates may not appear in scene-root snapshots or name searches.
- **DontDestroyOnLoad pitfall**: the bridge's `find_objects_with_component` internally calls `Resources.FindObjectsOfTypeAll` (which DOES find DontDestroyOnLoad components) but then filters with `gameObject.scene.name != null`. DontDestroyOnLoad objects have a null scene name, so they are silently excluded and the HTTP response is an empty array `[]` even though the server log says "Found N objects". To make a component agent-readable, attach it to a GameObject in a normal scene (not DontDestroyOnLoad) and recreate it on each scene load. The component can still reference persistent objects (provider, overlay, range display) held on the plugin's DontDestroyOnLoad GameObject.
- **`scene/search_gameobjects` componentName filter**: `HasComponentV2` compares `c.GetType().ToString()` (the FULL type name, e.g. `RogueCommandExpansionLoader.Runtime.LoaderDiagnosticsComponent`) against the `componentName` argument. Passing the short name (e.g. `LoaderDiagnosticsComponent`) returns zero matches. Use the full type name, or prefer `scene:find_objects_with_component` which resolves via `FindType` (short name works).
- **`component/call_method` response shape**: the bridge returns `{"status":"ok", "message":"Called 'MethodName' with N args. ReturnValue: <JSON>"}`. The actual method return value is embedded inside the `message` string after `ReturnValue: `. Parse it out of the message text; do not expect a top-level `result` or `returnValue` field.
- **`component/call_method` wrong-component 500**: Invoking `component/call_method` with a `componentName` that does not match the actual component attached to the GameObject (e.g., `Michsky.UI.Reach.ButtonManager` when the real component is a different type) returns HTTP 500 with no useful payload. Always inspect components first (`gameobject/inspect_components`) and use the exact full type name returned; a single mismatch is a safety stop.
- **Unity type imports**: `new GameObject(...)` requires `using UnityEngine;` in addition to `using UnityEngine.SceneManagement;`. Omitting it produces CS0246 at build time.
- `scene/search_gameobjects` is limited and noisy; filter its results by exact names, scene, and active state, and do not treat UI matches as loader evidence.
- When resolving a native control, require exact agreement on name, scene, active state, expected component type, and expected member values before invoking any mutating method. A single mismatch is a safety stop.
- Record the BepInEx log lines that show pack loading and any injected probe. A successful MCP response alone is not evidence that registration occurred.
- **MCP exit code 0 is not functional success**: The bridge and its Python wrapper return process exit code 0 even when the JSON payload contains `{"error": ...}` or `{"status": "error"}`. Always inspect the payload content and verify the observable effect (component present in scene, census populated, order applied and restored). A ping returning `{"status": "ok"}` only proves the bridge is alive, not that diagnostics or commands work. This is especially important for `find_objects_with_component` (which can return `[]` with code 0) and for `component/call_method` (which can return a 500 with a misleading code 0 from the wrapper).
- **MCP evidence boundary**: the bridge can enumerate scenes, objects, and components, but it does not replace visual confirmation of card rendering. If vision analysis is rate-limited or the bridge cannot drive the UI to the target scene, record the boundary explicitly and do not claim runtime success from menu navigation alone.
- **First-class MCP tool suite & aggregators**: Prefer the tools exposed by `tools/bepinex_mcp_server.py` (223 tools: 203 canonical + 20 aliases, across 186 schemas) and documented in `rogue-command-clean-room-diagnostics` rather than constructing manual calls. Use `diagnostics_runtime_snapshot` for a bounded composite read, `diagnostics_runtime_scalars` for polling, and `diagnostics_list_runtime_getters`/`--list-tools` to discover the current surface. Do not hardcode historical tool or census totals.

## 5. Deployment and artifact binding

Before touching the Steam installation, create `artifacts/runtime-smoke/<session-id>/predeploy-manifest.json`. Each entry must include:

- absolute target path and reason it is task-owned;
- whether the path is being added, replaced, or removed;
- pre-run existence, byte length, and SHA-256;
- local source artifact, byte length, and SHA-256;
- post-copy SHA-256 and the process-closed check that permitted the copy.

Declare the complete target set before copying. Typical targets are the loader DLL, the exact content manifest/bundles under test, and a log/probe file only when the task explicitly requires it. Do not replace unrelated plugins, BepInEx core files, the game assembly, Steam files, or user saves.

Build into a fresh ignored directory, for example `artifacts/runtime-smoke/<session-id>/plugin-build/`, and bind the session to the source commit and artifact hashes. If source, build, or bundle changes after deployment, invalidate the session and redeploy; never attribute observations to a newer source revision retroactively.

- The all-profile checkpoint command in section 3 records the authoritative selector and every profile file. Do not replace it with a partial list or a historical file count.
- Before launching, verify the installed deployment hashes match the local build artifacts you intend to test. If the installed plugin or bundle hashes differ from the current repo build, the runtime test would exercise stale code; update the deployment first or abort the runtime step until the hashes match.
- After the run, restore only the entries that the test changed; verify restored hashes match the manifest before continuing. Entries the test did not touch must remain unchanged.
- Preserve the manifest and any captured log excerpts as evidence; do not delete them during cleanup.

## 6. Game exit and restore sequence

- Verify `Rogue Command.exe` is not running before restoring deployment files or touching profile metadata. A forced termination during native shutdown can leave the installation or profile in an inconsistent state.
- When closing the game through a native UI control, resolve the exact control first, confirm its label and interactable state, then invoke its click method once. Do not retry the same control if the first call did not exit; inspect the scene/process state instead of blind-retrying.
- **Reliable close via inject-class**: When no UI close control is available or the game is in a non-interactive state, use `mod_inject_class` with a minimal MonoBehaviour whose `Awake` calls `Application.Quit()`. Pass the full C# source as the `classCode` parameter (not `code`). Attach to an existing GameObject via `attachToGameObjectId` or let it create its own. This is more reliable than clicking UI buttons because it does not depend on scene hierarchy or interactable state. The injected class is destroyed on exit; no cleanup needed.
- After exit, rerun the predeploy manifest restoration and hash verification. Only after hashes pass should you touch selector files or resume runtime work.
- **Profile verification**: use the all-profile checkpoint verifier; do not assume a fixed number of files or profiles. Only a justified `Profile2/metaSavegame.dat` autosave delta may be allowed, and only when the run actually advanced Profile2. Selector changes, `savegame.dat`, tutorial state, added/missing files, and changes in any other profile are unexpected.

## 7. Dedicated QA Profile Policy (Profile2 Invariant)

- **Profile2 Dedicated QA Invariant**: Profile2 is the user's dedicated QA profile and has no tutorial. Do not ask, inspect, infer, gate QA on tutorial status, or propose a bypass; use Profile2 after current save/selector safety checks and do not switch to Profile3.
- **No profile switching**: historical Profile3 copies and rollback notes are not an executable procedure. Never switch to Profile3 or infer that an old QA snapshot is safe; resolve the live selector and checkpoint again.
- **Save & Profile Safety Checks**:
  - Always resolve the authoritative selector through `ProfileManager.CurrentProfileTextPath` under `Application.persistentDataPath`; do not trust legacy selectors in the installation directory.
  - Before any live run, record the original selector and SHA-256 hashes for every file across all profiles.
  - Preserve saves and backups; never overwrite a save without fresh safety checks and explicit user authorization.
  - Never edit `tutorialState.txt` directly, grant XP, or use Harmony to bypass tutorial state.
  - After the game exits, restore the original selector and verify user profile hashes match baseline.

## 8. FirstRun policy

Profile2 is the established QA profile and is used directly. Do not add a tutorial bypass, edit tutorial state, grant XP, or call native tutorial methods merely to make a test easier. If a future acceptance task genuinely requires a new no-save profile, stop and obtain explicit authorization for a newly isolated profile created through the native profile-management flow; reverify the full identity tuple first, and never use Profile3 or the user's active profile as a shortcut.

### Profile2 safety preflight

Before changing files, launching Steam, or starting a run, perform and record these checks from current state:

1. Resolve the authoritative selector through `ProfileManager.CurrentProfileTextPath` under `Application.persistentDataPath`, and confirm that its value selects `Profile2` (the native selector text may be `2`).
2. Record whether `Profile2` contains `savegame.dat` or another saved run from the current all-profile checkpoint. A fresh no-save acceptance or any flow that would bypass `FirstRun` requires `Profile2` to have no saved run; if a save exists, stop rather than deleting or overwriting it. A read-only or explicitly authorized continuation must preserve the save and document that it is not an isolated fresh-run result.
3. When a separate user-active profile is known or a selector change is contemplated, demonstrate that the QA target is distinct from it. If selector ownership, active-profile identity, or save safety cannot be established, stop.
4. Do not change the selector, switch profiles, launch a bypass, or start a new run until the applicable selector, isolation, and save-safety checks are demonstrated. Never use `Profile3`.

## 9. Claim-specific acceptance matrix

Define the claim before launching and collect only the evidence needed to prove it. A lower row never proves a higher row.

| Claim | Minimum direct evidence | Does **not** prove it |
|---|---|---|
| Plugin loads | exact deployed DLL hash, BepInEx load line, no startup exception | local build, hook count, installed file alone |
| Pack registers | load line naming the pack plus runtime registry/census with expected IDs | manifest parses, inactive template, ping |
| Blueprint is offered | rendered/selected `ChooseCard` data naming the target factory/product and stage/pool context | eligible pool inspection, one native offer, forced callback |
| Selection/placement works | native UI selection and placed building with expected ID, scene, and postcondition | registry row, diagnostic spawn, direct factory call |
| Production works | target factory queue, progress, completion, and target product instance linked to the factory | placing a native factory, order response, inactive prefab |
| Visual rendering works | live target instance with active renderer, non-zero bounds, expected material/shader/sockets, and visual confirmation when required | AssetBundle inventory, prefab audit, inactive template |
| Stage/map works | stage selection plus generated map/scene and expected stage identity in the live run | cloned stage row, jump helper, loaded stage list |
| Intermission graph / route planning | live `diagnostics_get_intermission_graph` DAG showing nodes, edges, choosable level start, and candidate path to target/boss node | static `LevelMap` scene inspection, node counts without edge analysis, or menu scene |
| Intermission sub-screen state & directors | `diagnostics_get_intermission_reward_state` reading live currencies (`coins`, `crystals`, `dropPods`), deck cards, open queues, plus confirmed modal toggling (`DeckOverview` open/closed) or fail-closed gate responses when inactive | inspecting `ModalWindowManager` prefab templates, blind click coordinates, or assuming default player balances |
| Intermission node selection & level transition | coordinate raycast `diagnostics_order_select_level_node` returning `selected=true` with confirmed `IsCurrentNodeOfPath`, followed by native `LoadLevel()` transition into `Game` scene with `LoadingHider` and `INICIAR` button verification | raycast click on decorative `BossTriangle`, synthetic jump without `MapIsReady`, or level count increment in UI text without `Game` scene transition |
| AI behavior works | compatible live AI profile/roster, module tick/decision trace, bounded order, and postcondition on the target | module registration, profile attachment, manual diagnostic order |
| Stat/mechanic consumption works | spawned target instance and before/after native read or behavior tied to the target row | row/cache probe, native unit behavior, unrelated unit |

For randomized offers, record the actual offer and stage. Absence from one sample is inconclusive; repeat only through a safe native path and record the number of eligible observations. For forced diagnostic spawns or injected probes, label the result `synthetic-seam` and do not promote it to natural offer/production/AI evidence.

## 10. Safe runtime interaction

1. Start with read-only bridge observations: process/PID, scene, profile selector, loader health, pack registry, roster, and claim-specific preconditions.
2. Resolve the exact native control or target entity. Require agreement on name, scene, active state, component type, identity, hostility/ownership, and relevant member values.
3. Pause natively before unattended inspection or mutation. Prefer `Navigator.Pause()`/`Navigator.Unpause()` on the verified identity. Confirm `_isPaused` and `Time.timeScale=0`; take a second scaled-clock sample when the operation is long. Do not call `ResumeAndRemove()` on a temporary time-scale helper while native pause is active.
4. Perform one bounded action. Record the request, resolved target, response payload, and immediate read-back. Never use direct field edits, `tutorialState.txt`, profile-file edits, Harmony patches, or XP grants as substitutes for the named gameplay path.
5. Re-read the observable postcondition. If it fails, stop and classify the result; do not loop with the same call. Remove temporary probes/components and verify their absence.
6. Before exit, restore native pause/control state deliberately. If the run advanced Profile2, preserve the new QA autosave but document the allowed `metaSavegame.dat` delta; never silently restore or delete user data.

When no native close control is available, use one minimal `mod_inject_class` `MonoBehaviour` with `classCode` (not `code`) that calls `Application.Quit()` in `Awake`. Verify process exit before restoring files. A successful injection is only a close mechanism, not gameplay evidence.

## 11. Verify the behavior that the claim names

- Define one observable success condition before launching: for example, the pack is accepted, the unit appears in a blueprint pool, `EntityFactory` instantiates it, the card/name/model render, a stage creates a map, or an AI module makes its intended bounded decision.
- Follow the evidence chain in order: plugin loaded; pack discovered and validated; runtime registration reached; target content selected or spawned; intended behavior visible in the match. A successful startup, Harmony hook count, or menu scene proves only the earlier steps it directly records.
- Capture the relevant game log and in-game observation with enough context to identify the pack, unit/stage/profile, and test session. If the game fails before the target scene or behavior, report the boundary and preserve that failure as unknown runtime behavior; do not attribute it to the mod without a control or direct evidence.
- After the run, restore the original deployment files and verify their hashes and process state. Preserve user data and evidence artifacts; do not delete profile data created by the game unless the user specifically asks.

## 12. Exit, restoration, and final verification

- Do not restore an installation file or selector while `Rogue Command.exe` is running. Wait for process absence and record the exit result.
- Restore only the declared deployment entries. Verify every restored hash against `predeploy-manifest.json`; preserve logs and evidence even when the original log file must be restored.
- Restore the authoritative selector to the exact pre-run value and verify its hash. Do not touch legacy selectors in the Steam installation as a substitute.
- Run the all-profile checkpoint verifier. Treat any unexpected change, selector mismatch, missing file, or added file as a cleanup failure. `Profile2/metaSavegame.dat` is the only normally allowed delta, and only when justified by actual QA progression.
- Confirm temporary `Rcl*` probes, watcher components, injected classes, and inspectors are absent from the current scene and persistent objects. Do not leave Unity Editor probe scripts or `.meta` files in the authoring project.
- Write a sanitized `runtime-evidence.json` using the contract in `references/runtime-evidence-contract.md`. It must contain the tested commit/artifact hashes, identity tuple, profile checkpoint result, bridge/PID, scenario assertion results, restoration result, evidence level, and explicit unknowns.

## 13. Temporary Unity probe cleanup

- Temporary Unity Editor scripts used to inspect AssetBundle addresses or asset resolution must be removed from the authoring project after verification. Delete both the `.cs` file and its `.meta` file; do not leave probe scripts in `Assets/Editor/`.
- A probe script that compiles and runs successfully in batchmode is not a permanent authoring artifact. Its only valid outcomes are the observed evidence and cleanup.

## 14. Evidence boundaries and reporting

Every result must separate:

- **Static inspection** — source, schema, decompilation, or serialized asset facts.
- **Local build/validation** — compiler, harness, Python tests, Unity batchmode, or offline bundle checks.
- **In-game Steam runtime evidence** — observations from the exact launched process, exact deployed hashes, exact profile, exact scene, and exact scenario.

Report the highest claim actually reached, not the highest intended. A clean build, successful AssetBundle import, loaded plugin, Harmony hook count, live ping, runtime census, inactive prefab, stage jump, injected class, or native-only unit does not prove natural content behavior. If the run stops before the claim, say `UNKNOWN` or `BLOCKED`, preserve the evidence, and do not attribute the boundary to the loader without a control or direct failure.

### Verified Session Evidence References
- `artifacts/runtime-smoke/intermission-functions-live-20261108/`: Live Steam Profile2 evidence verifying `diagnostics_get_intermission_census` scanning `OverWorldMapBoss` (17 nodes, level 4 field 0, instanceId 982874), `diagnostics_get_intermission_graph` forward route planning, `diagnostics_get_intermission_reward_state` (400 coins, 3000 start crystals, 3 drop pods, 3 deck cards including `sample-native:arachnid-hexapod-factory` with `JaggedPlating`), `diagnostics_order_set_deck_overview_open` modal toggling, fail-closed gates on sub-screen actions (`choose`, `skip`, `reroll`, `assign_upgrade`), and coordinate raycast node selection of `OverWorldMapBoss` (`IsCurrentNodeOfPath`).
- `artifacts/runtime-smoke/live-reads-20261108-nojump/`: Live Steam Profile2 evidence verifying the native no-jump progression entry chain from conserved Intermission map (`Continue Run` -> native `LoadScene.LoadLevel()` -> `LoadingHider` active in `Game` scene -> click **INICIAR** button `1008136` -> live gameplay with `timeScale=1`, 14 battlefield entities, AI 12 buildings, 2 factories producing, and 46/46 snapshot + 15/15 non-snapshot telemetry getters).

## 15. End-of-session checklist

- [ ] Current checkout, source commit, build artifacts, and installed target hashes recorded.
- [ ] Game launched through Steam; no direct executable launch.
- [ ] Complete game identity tuple verified before reflected calls.
- [ ] Profile2 selected through the authoritative selector; no tutorial bypass or direct save edit.
- [ ] All-profile checkpoint created before launch and verified after exit.
- [ ] Bridge endpoint matched to the live game process; payloads inspected for semantic errors.
- [ ] Native pause/clock state proven before unattended actions.
- [ ] Claim-specific postcondition observed and read back.
- [ ] Temporary probes/inspectors removed; process exited before restore.
- [ ] Deployment and selector restored by hash; only an explicitly justified Profile2 `metaSavegame.dat` delta remains.
- [ ] Evidence JSON states `PASS`, `FAIL`, `BLOCKED`, `UNKNOWN`, or `NOT_TESTED` per assertion.

## References

- [Runtime evidence contract](references/runtime-evidence-contract.md)
- [Steam runtime control runbook](../../../docs/research/STEAM-RUNTIME-CONTROL.md)
- [Clean-room diagnostics and MCP tools](../rogue-command-clean-room-diagnostics/SKILL.md)
- [Pack validation](../rogue-command-pack-validation/SKILL.md)
- [Modding tools & telemetry suite](../rogue-command-modding-tools/SKILL.md)
