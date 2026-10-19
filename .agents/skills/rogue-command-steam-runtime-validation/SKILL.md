---
name: rogue-command-steam-runtime-validation
description: "Verify Rogue Command loader or content behavior in a live match launched through Steam, using reversible deployment and explicit evidence boundaries."
---

# Rogue Command Steam Runtime Validation

Use this skill when an acceptance claim requires observing a loader, unit, stage, or AI inside Rogue Command. Offline builds, package checks, and Unity Editor imports belong to `rogue-command-pack-validation`; they are not live-game evidence.

## Protect the Steam installation and profile

- Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, and the current runtime notes in `README.md`. Before using reflected signatures, verify the complete identity tuple: game version, `Assembly-CSharp.dll` SHA-256, and MVID.
- Before every live-game task, also read `docs/research/STEAM-RUNTIME-CONTROL.md`; it is the persistent runbook for the BepInEx MCP control path, profile handling, and cleanup.
- Inspect the Steam/game process and deployment state first. Do not interfere with an already running game. Launch the game through Steam; do not start `RogueCommand.exe` directly.
- Before changing the game folder, record the exact files and configuration that will be touched and their original hashes. Use a reversible test setup, keep the user's save data and Steam Cloud preference intact, and restore temporary deployment changes after the run. The only profile mutation in the standard non-tutorial acceptance flow is the requested native tutorial-completion state described below.
- If a BepInEx MCP bridge or runtime inspector is available, verify its connected target and use read-only observations first. Use at most one runtime inspector in a test profile and remove it during cleanup. Do not assume a tool is connected because its files are installed locally.
- Prefer BepInEx MCP over Computer Use for controls exposed by the bridge. The bridge has operated in this project without the game window focused; do not request focus or invoke Computer Use solely to click a control when a bridge call can do it.

## Skip FirstRun on the active user profile

- For Steam acceptance runs that do not test the tutorial, mark the tutorial complete on the currently selected user profile before starting a run. Resolve the real selector through `ProfileManager.CurrentProfileTextPath` under `Application.persistentDataPath`; do not switch to a scratch profile, create a QA profile, or change profile selection for this bypass.
- Perform the change from `MainMenu` or `RunSetup`, before a run is active. Record the selector, hash every existing file in the active profile, and preserve a copy of `tutorialState.txt` if present. Do not abandon, reset, or overwrite an active run to reach this step. Preserve all profile files except the tutorial state written by the native API.
- Recheck the game version, `Assembly-CSharp.dll` SHA-256, and MVID together before using the native methods. On the currently verified build (`1.0.9`, SHA-256 `d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b`, MVID `db0e9462-128d-4296-9900-de18b4bd0c00`), call `Game.TutorialState.FinishMainTutorial()` and `Game.TutorialState.OnPlayerHasFinishedLevel1()` once when their flags are false. These persist the two tutorial gates; `FinishMainTutorial()` also runs the game's `AutoFinishCustomTutorialsWithMainTutorial()` routine. The verified `GameBalancingStore.DetermineLevelProgressionModeForNewRun()` then selects `Default` or `Heat` instead of `FirstRun` when those gates are complete.
- Verify both flags and the next-run mode before pressing `Start Run`; after the transition, verify `Game.CurrentLevelProgressionMode` is not `FirstRun`. If the mode remains `FirstRun`, stop and re-check the profile and build identity instead of advancing the tutorial. If the identity differs, inspect the exact signatures and effects before invoking anything.
- This is the requested persistent change to the active profile. Keep the tutorial flags completed after acceptance; retain the pre-change copy for recovery if the user later asks, but do not restore it during ordinary cleanup. Never edit or delete `tutorialState.txt` directly, grant XP, reset a profile, or use Harmony to bypass the tutorial. Restore temporary installation files and remove any temporary runtime helper as usual.

## Verify the behavior that the claim names

- Define one observable success condition before launching: for example, the pack is accepted, the unit appears in a blueprint pool, `EntityFactory` instantiates it, the card/name/model render, a stage creates a map, or an AI module makes its intended bounded decision.
- Follow the evidence chain in order: plugin loaded; pack discovered and validated; runtime registration reached; target content selected or spawned; intended behavior visible in the match. A successful startup, Harmony hook count, or menu scene proves only the earlier steps it directly records.
- Capture the relevant game log and in-game observation with enough context to identify the pack, unit/stage/profile, and test session. If the game fails before the target scene or behavior, report the boundary and preserve that failure as unknown runtime behavior; do not attribute it to the mod without a control or direct evidence.
- After the run, restore the original deployment files and verify their hashes and process state. Preserve user data and evidence artifacts; do not delete profile data created by the game unless the user specifically asks.

## State the evidence level

Label results as **static inspection**, **local build/validation**, or **in-game runtime evidence**. Only the last can establish the observed live behavior, and only for the exact game build and scenario tested. Record gaps and failures explicitly; do not generalize a single tested unit or profile to every pack.
