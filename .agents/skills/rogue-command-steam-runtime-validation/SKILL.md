---
name: rogue-command-steam-runtime-validation
description: "Verify Rogue Command loader or content behavior in a live match launched through Steam, using reversible deployment and explicit evidence boundaries."
---

# Rogue Command Steam Runtime Validation

Use this skill when an acceptance claim requires observing a loader, unit, stage, or AI inside Rogue Command. Offline builds, package checks, and Unity Editor imports belong to `rogue-command-pack-validation`; they are not live-game evidence.

## Protect the Steam installation and profile

- Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, and the current runtime notes in `README.md`. Before using reflected signatures, verify the complete identity tuple: game version, `Assembly-CSharp.dll` SHA-256, and MVID.
- Before every live-game task, also read `docs/research/STEAM-RUNTIME-CONTROL.md`; it is the persistent runbook for the BepInEx MCP control path, profile handling, and cleanup.
- Inspect the Steam/game process and deployment state first. Do not interfere with an already running game. Launch the game through Steam; do not start `Rogue Command.exe` directly.
- Before changing the game folder, record the exact files and configuration that will be touched and their original hashes. Use a reversible test setup, keep the user's save data and Steam Cloud preference intact, and restore temporary deployment changes after the run. Acceptance that skips `FirstRun` may mutate only a dedicated isolated QA profile, never the user's profile.
- If a BepInEx MCP bridge or runtime inspector is available, verify its connected target and use read-only observations first. Use at most one runtime inspector in a test profile and remove it during cleanup. Do not assume a tool is connected because its files are installed locally.
- Prefer BepInEx MCP over Computer Use for controls exposed by the bridge. The bridge has operated in this project without the game window focused; do not request focus or invoke Computer Use solely to click a control when a bridge call can do it.

## BepInEx MCP control path

- Resolve the bridge URL and command shape from the installed `BepInExMCP` plugin, not assumptions. Prefer the current `BepInEx/LogOutput.log` listener line; if absent, verify the base URL and routes in `ModdersHelperApp.py`/`bridge_client.py` and match the listening port to the `Rogue Command.exe` process before any read-only probe. Do not infer a bridge target from unrelated local MCP transport ports; this session confirmed the game bridge at `http://127.0.0.1:8080/mcp/`, not `http://localhost:54321/mcp`.
- Commands are path-based (`http://127.0.0.1:8080/mcp/<command>`), with query parameters for filters such as `componentName`. A JSON body with a `method` field is not the protocol for this bridge.
- Useful commands include `system/ping`, `scene/list_root_gameobjects`, `scene:find_objects_with_component?componentName=...`, `gameobject/inspect_components`, `component/get_details`, `component/get_member`, `component/call_method`, and `mod:inject_class`.
- `scene:find_objects_with_component` returns the component-bearing objects for the queried type. Save the raw response before filtering; DontDestroyOnLoad templates may not appear in scene-root snapshots or name searches.
- `scene/search_gameobjects` is limited and noisy; filter its results by exact names and do not treat UI matches as loader evidence.
- Record the BepInEx log lines that show pack loading and any injected probe. A successful MCP response alone is not evidence that registration occurred.
- **MCP evidence boundary**: the bridge can enumerate scenes, objects, and components, but it does not replace visual confirmation of card rendering. If vision analysis is rate-limited or the bridge cannot drive the UI to the target scene, record the boundary explicitly and do not claim runtime success from menu navigation alone.

## Dedicated QA Profile Policy (Profile2 Invariant)

- **Profile2 Dedicated QA Invariant**: Profile2 is the user's dedicated QA profile and has no tutorial. Do not ask, inspect, infer, gate QA on tutorial status, or propose a bypass; use Profile2 after current save/selector safety checks and do not switch to Profile3.
- **Historical Profile3 QA Caveat**: Historical Steam QA records document an authorized Profile2→Profile3 copy, Profile2 preserved, and a progressed Profile3 run. Do not assume rollback or switch to Profile3; recheck live process, profile, and installation before any runtime work.
- **Save & Profile Safety Checks**:
  - Always resolve the authoritative selector through `ProfileManager.CurrentProfileTextPath` under `Application.persistentDataPath`; do not trust legacy selectors in the installation directory.
  - Before any live run, record the original selector and SHA-256 hashes for every file across all profiles.
  - Preserve saves and backups; never overwrite a save without fresh safety checks and explicit user authorization.
  - Never edit `tutorialState.txt` directly, grant XP, or use Harmony to bypass tutorial state.
  - After the game exits, restore the original selector and verify user profile hashes match baseline.

## Skip FirstRun on Native QA Flow (when applicable)

- When an acceptance run requires an isolated profile without tutorial:
  - Recheck the game version, `Assembly-CSharp.dll` SHA-256, and MVID together before using native methods. On the verified build (`1.0.9`, SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`), invoke `Game.TutorialState.FinishMainTutorial()` and `Game.TutorialState.OnPlayerHasFinishedLevel1()` once when flags are false.
  - Verify both flags and next-run mode before pressing `Start Run`; after transition, verify `Game.CurrentLevelProgressionMode` is not `FirstRun`.
  - Perform tutorial-state changes only via native methods and only within dedicated QA profiles. Restore original selector and temporary installation files after exit.

## Verify the behavior that the claim names

- Define one observable success condition before launching: for example, the pack is accepted, the unit appears in a blueprint pool, `EntityFactory` instantiates it, the card/name/model render, a stage creates a map, or an AI module makes its intended bounded decision.
- Follow the evidence chain in order: plugin loaded; pack discovered and validated; runtime registration reached; target content selected or spawned; intended behavior visible in the match. A successful startup, Harmony hook count, or menu scene proves only the earlier steps it directly records.
- Capture the relevant game log and in-game observation with enough context to identify the pack, unit/stage/profile, and test session. If the game fails before the target scene or behavior, report the boundary and preserve that failure as unknown runtime behavior; do not attribute it to the mod without a control or direct evidence.
- After the run, restore the original deployment files and verify their hashes and process state. Preserve user data and evidence artifacts; do not delete profile data created by the game unless the user specifically asks.

## State the evidence level

Label results as **static inspection**, **local build/validation**, or **in-game runtime evidence**. Only the last can establish the observed live behavior, and only for the exact game build and scenario tested. Record gaps and failures explicitly; do not generalize a single tested unit or profile to every pack.
