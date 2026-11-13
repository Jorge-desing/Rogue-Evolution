---
name: rogue-command-bepinex-mcp
description: "Control Rogue Command at runtime through the project BepInEx MCP bridge without screenshots, screen captures, or Computer Use. Use for live scene inspection, native UI actions, gameplay commands, diagnostic telemetry, reversible probes, and Harmony 2 runtime patching."
---

# Rogue Command BepInEx MCP

Use this skill as the single workflow for controlling or inspecting the live Rogue Command runtime. It uses the project-maintained MCP bridge and Unity telemetry; it does not require images or desktop automation.

## MCP configuration and tool selection

- The trusted project configuration in `.codex/config.toml` maps the existing `bepinex_mcp` server name to `tools/bepinex_mcp_server.py`. Its project-level entry overrides the global upstream server for this repository. Keep the tool namespace stable so existing workflows continue to address `bepinex_mcp`.
- The server launcher is `scripts/start-server.ps1`. It starts the project bridge with the repository's `.tools/BepInExMCP/.venv`; do not replace the project config with the original `ModdersHelperApp.py` entry.
- Use BepInEx MCP tools for runtime interaction. Do not use screenshots, screen recording, capture tools, Computer Use, or direct mouse/keyboard automation. If telemetry cannot establish a visual claim, report that boundary instead of switching to capture.
- Prefer purpose-built `diagnostics_*` tools. Use the scene/component/type tools and `component_call_method` only when the diagnostics surface lacks the required operation. Resolve current GameObject IDs and exact scene-qualified selectors immediately before use; IDs expire across scenes and sessions.
- For UI, use `diagnostics_ui_invoke_native_button` for `Michsky.UI.Reach.ButtonManager` controls and `diagnostics_ui_click_text` with the exact path from `diagnostics_get_ui_accessibility_census` for Unity `Selectable` controls. Require an unambiguous active target and its native interactability checks.
- For accessible menus and the run director, follow [Native UI and intermission](references/ui-intermission.md). `diagnostics_fast_snapshot` presets `gui` and `intermission` provide bounded discovery reads; they are best-effort multi-read snapshots, so refresh the target census immediately before an action.
- For multi-step operations, use `automation_run_sequence` only with exact typed references to earlier successful steps. A timed-out in-flight action has an unknown result; query its postcondition before retrying.

## Live runtime workflow

1. Read `system_get_game_process_state`. If the game is closed, launch it through Steam only when the user's current request authorizes a live run; never launch the game executable directly. If it is open, ping the bridge and read runtime capabilities before diagnosis.
2. Read game paths and runtime identity. Before reflection, type inspection, or a Harmony patch, verify the current game version, `Assembly-CSharp.dll` SHA-256 and MVID together, plus Unity backend, BepInEx version, architecture, and supported patch types. Treat the values in `rogue-command-harmony-policy` as a pinned baseline, not as proof that the current installation still matches. If the identity differs, stop using remembered signatures/tokens and re-verify the target assembly before continuing.
3. Start from read-only census and snapshots. Before mutating a match, identify the exact stage, entity, selector, and expected postcondition. Use stable `UniqueEntityId` / `entityKeyId` for spawned entities when native `EntityId` values can repeat.
4. Keep existing saves and tutorial progress intact. Never select/reset an empty profile, invoke tutorial completion/reset methods, or edit `tutorialState.txt`. Ordinary continuation uses an existing tutorial-complete profile through native UI. A new-run test requires explicit user authorization and the cloned-profile QA procedure in `docs/research/STEAM-RUNTIME-CONTROL.md`.
5. After a level transition, inspect `LoadingHider`; use the native `Iniciar` control through MCP before interpreting movement or gameplay telemetry. A `Game` scene and `Time.timeScale=1` alone do not prove that gameplay started.
6. Before AI acceptance, call `diagnostics_get_active_stage_ai_association` and resolve the stable active stage ID. Separate stage-declared profile IDs from loader-bound runtime profiles. Do not advance a saved route only to force an unrelated profile; unresolved association means report `UNKNOWN` and stop at that boundary.
7. Use native diagnostic commands for gameplay actions when requested: inspect an active building ghost before placement and keep placement `commit=false` for diagnosis; address movement/attack commands to a uniquely resolved entity; read the resulting state after each mutation. Native placement rules remain authoritative.

## Harmony compatibility and runtime patches

- Read [Rogue Command Harmony Policy](../rogue-command-harmony-policy/SKILL.md) before using Harmony or relying on reflected game signatures. This runtime skill governs MCP operation; the Harmony policy governs plugin/source implementation and patch mechanics.
- Match the full current compatibility identity before patching: game version + `Assembly-CSharp.dll` SHA-256 + MVID. Also confirm Mono versus IL2CPP, CPU architecture, BepInEx and Harmony versions, and patch types through the live runtime capabilities. Do not assume Harmony 2 support merely because the bridge responds.
- Resolve the full target type and exact overload with `type_search` / `type_describe` before `mod_patch_method`. Supply the exact parameter types and only a patch type reported as supported. Do not guess overloads, patch by localized name, or use a transpiler unless the target IL and stack contract have been verified for this exact build.
- Treat `mod_patch_method`, `mod_inject_class`, setters, and gameplay-order tools as runtime mutations. Use the narrowest reversible patch/probe needed for the requested result. Record every registration ID, temporary component, watcher, probe token, and prior value so cleanup is deterministic.
- Verify patch installation through `mod_list_patches` or `diagnostics_get_runtime_hooks`, then verify the requested behavior with a direct runtime postcondition. Remove temporary patches with `mod_remove_patch`; stop renderer probes with their exact returned `token`; remove temporary components/watchers and census them afterward. Do not leave a diagnostic patch active after its requested observation.
- Keep Harmony and BepInEx dependencies out of distributable ZIPs. For source changes, follow the compatibility gate, exception-safety rules, centralized registration pattern, and build validation in the Harmony policy.

## Evidence and cleanup

- Label results as static inspection, local build/validation, or in-game runtime evidence. Tool registration or a connected bridge is not proof that a hook ran or that gameplay started.
- Unity state, Animator values, renderer visibility, frustum tests, and `BakeMesh` samples are telemetry. They can support runtime-state claims but do not prove pixel appearance or animation quality. Use `diagnostics_wait_for_renderer_frame_evidence` for bounded, screenshot-free renderer telemetry when needed; preserve the returned token and confirm cleanup.
- On timeouts or truncated responses, inspect the live postcondition and observer census before retrying. Report cleanup that could not be confirmed.
- Close Rogue Command only when the requested workflow calls for it. Use the MCP graceful-exit path, then verify actual process closure and the required profile/save checkpoint.

## Related project guidance

- [Steam Runtime Validation](../rogue-command-steam-runtime-validation/SKILL.md) — profile, save, Steam Cloud, and reversible-runtime procedure.
- [Clean-Room Diagnostics](../rogue-command-clean-room-diagnostics/SKILL.md) — telemetry and probe contracts.
- [Harmony Policy](../rogue-command-harmony-policy/SKILL.md) — BepInEx 5, Harmony 2, compatibility tuple, and source patch rules.
- `tools/bepinex_mcp_server.py` and `tools/build_bepinex_mcp.py` — project bridge handlers and schemas. Keep them synchronized when changing the tool contract.
- [Native UI and intermission](references/ui-intermission.md) — screenshot-free control selection, reward screens, run-map actions, and transition checks.
