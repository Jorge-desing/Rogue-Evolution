---
name: rogue-command-steam-runtime-validation
description: "Verify Rogue Command loader or content behavior in a live match launched through Steam, using reversible deployment and explicit evidence boundaries."
---

# Rogue Command Steam Runtime Validation

Use this skill when an acceptance claim requires observing a loader, unit, stage, or AI inside Rogue Command. Offline builds, package checks, and Unity Editor imports belong to `rogue-command-pack-validation`; they are not live-game evidence.

## Protect the Steam installation and profile

- Read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, and the current runtime notes in `README.md`. Before using reflected signatures, verify the complete identity tuple: game version, `Assembly-CSharp.dll` SHA-256, and MVID.
- Inspect the Steam/game process and deployment state first. Do not interfere with an already running game. Launch the game through Steam; do not start `RogueCommand.exe` directly.
- Before changing the game folder, record the exact files and configuration that will be touched and their original hashes. Use a reversible test setup, keep the user's saves and Steam Cloud data intact, and restore temporary deployment changes after the run. Never use an unverified clone or copied profile as proof of a Steam-launched run.
- If a BepInEx MCP bridge or runtime inspector is available, verify its connected target and use read-only observations first. Use at most one runtime inspector in a test profile and remove it during cleanup. Do not assume a tool is connected because its files are installed locally.

## Verify the behavior that the claim names

- Define one observable success condition before launching: for example, the pack is accepted, the unit appears in a blueprint pool, `EntityFactory` instantiates it, the card/name/model render, a stage creates a map, or an AI module makes its intended bounded decision.
- Follow the evidence chain in order: plugin loaded; pack discovered and validated; runtime registration reached; target content selected or spawned; intended behavior visible in the match. A successful startup, Harmony hook count, or menu scene proves only the earlier steps it directly records.
- Capture the relevant game log and in-game observation with enough context to identify the pack, unit/stage/profile, and test session. If the game fails before the target scene or behavior, report the boundary and preserve that failure as unknown runtime behavior; do not attribute it to the mod without a control or direct evidence.
- After the run, restore the original deployment files and verify their hashes and process state. Preserve user data and evidence artifacts; do not delete profile data created by the game unless the user specifically asks.

## State the evidence level

Label results as **static inspection**, **local build/validation**, or **in-game runtime evidence**. Only the last can establish the observed live behavior, and only for the exact game build and scenario tested. Record gaps and failures explicitly; do not generalize a single tested unit or profile to every pack.
