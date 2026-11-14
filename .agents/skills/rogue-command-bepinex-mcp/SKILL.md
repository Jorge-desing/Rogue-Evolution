---
name: rogue-command-bepinex-mcp
description: "Control Rogue Command at runtime through the project BepInEx MCP bridge without screenshots, screen captures, or Computer Use. Use for live scene inspection, native UI actions, gameplay commands, diagnostic telemetry, reversible probes, and Harmony 2 runtime patching."
---

# Rogue Command BepInEx MCP

Use this skill as the single workflow for controlling or inspecting the live Rogue Command runtime. It uses the project-maintained MCP bridge and Unity telemetry; it does not require images or desktop automation.

## Fast tool discovery

**Required:** Before working in an area, consult its row in the table below and use the relevant discovery terms to find the current tools for the requested operation. If the task spans multiple areas, consult each applicable row. Repeat discovery when the task moves to another area or the needed capability is unclear; use the live tool descriptions and schemas to select the operation.

The `bepinex_mcp` namespace is the authoritative current inventory. Before deciding a capability is missing, call `discover_tools` with `includeAliases=false` and a short English keyword, then call `describe_tool` with the exact returned name when its parameters are unclear. Search matches literal substrings in names and descriptions, not intent: try several related words, or call `discover_tools` without filters for the complete index. A `category` filter can hide matches; omit it when exploring. Inspect the tool's mutation warning and live preconditions before invoking it. Hidden compatibility aliases do not remove canonical tools.

| Task | First tools and search terms |
| --- | --- |
| Availability and setup | `system_get_game_process_state`, `system_ping`, `system_capabilities`; search `system` or `preflight` |
| Native UI and intermission | `diagnostics_get_runtime_control_surface`, `diagnostics_get_ui_accessibility_census`; search separately for `ui`, `intermission`, `shop`, `reward`, `navigator`, `deck` and `level` |
| Units, commands and construction | Search `entity`, `selection`, `unit`, `production`, `placement`, `building`, `move` and `attack` |
| AI and director | `diagnostics_get_active_stage_ai_association`; search `ai`, `director`, `doctrine`, `threat` and `wave` |
| Economy and gameplay | Search `economy`, `resource`, `card`, `upgrade`, `relic`, `hazard` and `veterancy` |
| Scene, components and reflection | Search `scene`, `gameobject`, `component` and `type`; inspect the exact current selector and overload |
| Harmony and observation | Search `patch`, `inject`, `watch`, `renderer`, `camera`, `visual` and `frame`; follow the Harmony policy before patching |
| Coordination and sequences | Search `automation`, `wait`, `sequence`, `batch`, `agent` and `lock`; read [Automation](references/automation.md) before sequences, waits or recovery |

Use the live index rather than treating this table as exhaustive. `diagnostics_get_catalog` and `diagnostics_list_runtime_getters` enumerate loader diagnostics, while `discover_tools` indexes MCP tools; they answer different questions. An empty filtered search is not evidence that the capability is absent.

## MCP configuration and tool selection



- Tool arguments are validated against the advertised schema before dispatch, including CLI and resolved sequence steps. Static sequence preflight defers exact references only until resolution. Errors identify the tool and argument path; values are not coerced. Install `jsonschema==4.26.0` from project requirements in the server's Python environment; missing validation dependency blocks tools.
- HTTP response bodies are limited to 16 MiB, decoded as strict UTF-8 and checked against valid Content-Length. Transport/body failures after sending report `completionStatus=unknown` even outside sequences; inspect postconditions before retrying. Expired pre-send budgets report `not_started`.

- RPC requests require JSON-RPC 2.0, a method, and object params/arguments. Notifications do not execute tools. Stdio input is bounded to 16 MiB per message and 8 KiB for framed headers; malformed framing closes the transport after a parse error because its boundary cannot be trusted. The reader and its limits reload between messages. A read already blocked on stdin finishes with its previous implementation; the following read adopts the update. Framing errors remain fatal even after class identity changes during reload.

- The stdio host reloads `bepinex_mcp_server.py` and `build_bepinex_mcp.py` between requests when their contents change, retaining the process and connection. Diagnostics IDs are resolved afresh per request. A syntax/import error rejects the request; repair the source and call again, without replaying an uncertain mutation. Catalog changes emit `notifications/tools/list_changed`; whether the client refreshes its exposed schemas must be checked. Changes to the reload host/launcher itself require one process restart, and loader DLL changes still require closing/relaunching the game. Processes started before this host was installed need one initial client reload; do not terminate stdio servers expecting Codex to reconnect automatically.

- Codex and Antigravity use the stateless HTTP endpoint `http://127.0.0.1:18764/mcp`, hosted by `tools/bepinex_mcp_http.py`. No stdio adapter or MCP session is allocated per chat; each HTTP response closes its connection. The authenticated loopback frontend serializes calls through the shared worker. Local credentials stay in the global client configs and ignored `artifacts/mcp-http/token`; never commit them. The worker owns `includeAliases=false`.
- Authentication failures and malformed internal requests are rejected without terminating the shared worker. The HTTP frontend handles non-ASCII invalid authorization as a rejected credential. Changes to the worker or HTTP host itself require restarting the respective process; server handler/schema changes remain reloadable.
- The HTTP frontend caps concurrent connections at 32, times out slow request bodies after 10 seconds and rejects requests waiting over 120 seconds for the execution gate as not started. Saturated connections may close without a response. A lost response after dispatch remains uncertain: inspect postconditions before retrying.
- Check host registration with `codex mcp list` and `codex mcp get bepinex_mcp`. Registration alone does not establish tool availability. If tools are absent, verify the filter and launcher, then test MCP initialization and `tools/list` directly through the configured launcher. Compare that result with the current task's tool inventory and relevant Codex startup logs; distinguish server failure from a client that is not ready. A new chat may also lack the tools, so do not present opening one as a confirmed fix. Report the observed error and unresolved cause; do not replace the server entry or switch to the upstream server without evidence.
- If both global and project configuration define `bepinex_mcp`, compare their complete effective launch arguments, working directory, and filter. An isolated app-server inventory can establish that it loads the catalog, but cannot establish that the existing Desktop chat reconnected. The checked-in absolute path targets this user's checkout; adjust it for other checkouts.
- For Antigravity, `python -m tools.sync_antigravity_bepinex --check` compares the installed global skill and tool schemas with this checkout; omit `--check` to sync, backing up changed existing files under `artifacts/antigravity-backups/`. The global copy links back to this checkout for related skills and the launcher. Confirm Antigravity's MCP configuration points to this checkout's server; file synchronization alone does not prove client discovery.
- Top-level JSON `error` responses and failed automation sequences are marked `isError=true`; inspect response content for partial section failures and uncertain postconditions. A successful catalog handshake does not prove a live game connection.
- Use BepInEx MCP tools for runtime interaction. Do not use screenshots, screen recording, capture tools, Computer Use, or direct mouse/keyboard automation. If telemetry cannot establish a visual claim, report that boundary instead of switching to capture.
- Prefer purpose-built `diagnostics_*` tools. Use the scene/component/type tools and `component_call_method` only when the diagnostics surface lacks the required operation. Resolve current GameObject IDs and exact scene-qualified selectors immediately before use; IDs expire across scenes and sessions.
- For UI, use `diagnostics_ui_invoke_native_button` for `Michsky.UI.Reach.ButtonManager` controls and `diagnostics_ui_click_text` with the exact path from `diagnostics_get_ui_accessibility_census` for Unity `Selectable` or canvas-backed `IPointerClickHandler` controls. Require an unambiguous active target and its native interactability checks.
- Start UI discovery with `diagnostics_get_runtime_control_surface` when available. It reads the active scene, Unity frame, and accessible UI on one Unity-thread call, and includes a bounded native Shop/RemoveCardView panel state; run, route, reward/deck, and navigator sections appear on an active intermission scene. Section failures remain isolated in the returned payload. `diagnostics_fast_snapshot` presets `gui` and `intermission` remain useful broader best-effort multi-read views; refresh the target census immediately before an action.
- For accessible menus and the run director, follow [Native UI and intermission](references/ui-intermission.md).
- The UI census reports values for Unity UI sliders/toggles/dropdowns, Michsky switches/dropdowns and TMP fields. Use `diagnostics_ui_set_value` with the current exact path, ID and typed value; see the UI reference for native event semantics and limits.
- Native Shop purchases/rerolls and RemoveCardView actions use the dedicated `diagnostics_get_intermission_native_panel_state` census and typed native actions. Treat `nativeCallInvoked` and `effectObserved` separately; if a native call was issued without an observed postcondition, refresh state before taking another action. No Harmony patch is used for these controls.
- For multi-step operations, `automation_run_sequence` checks all steps and the shape and ordering of nested references before execution; referenced values still resolve at runtime and must come from successful earlier steps. Add `expect: [{"path":"/field","equals":true}]` to a census or action step to require exact JSON equality before proceeding. A missing path, mismatch, failed guarded call, unknown completion, unobserved native effect, or exhausted budget stops the sequence even with `stopOnFailure=false`. Inspect `checks`, `postconditionInspectionRequired`, and the original result; after an uncertain action query its postcondition before retrying. Checks are snapshots, not locks or proof of future state.

## Agent automation

Discover → observe → act → wait/verify → decide the next step. **Required:** Read [Automation](references/automation.md) before using sequences, state waits or recovery. Use `automation_wait_for_state` for bounded read-only conditions instead of fixed delays; use native action validation for every mutation. Intermission reward/deck queries are permitted read-only state-wait targets; see the intermission reference for postcondition selection. State waits can require `stableSamples` consecutive matches (1–8); choose multiple samples for transient UI readiness and inspect the returned streak on timeout. Session/lock tools coordinate cooperating agents only; advisory locks do not enforce exclusion.

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
- For geometry calibration, record GetBattlefieldCensusJson immediately before and after every capture and bind both counts to its hashed schema-v3 report. A probe-ready flag does not prove the scene population stayed controlled. If player, AI, or building counts change, retain the raw report but exclude it from the matched capacity fit; require at least three stable reports across two geometry loads before updating the calibrated profile. Calibration is diagnostic and does not gate builders.

## Related project guidance

- [Steam Runtime Validation](../rogue-command-steam-runtime-validation/SKILL.md) — profile, save, Steam Cloud, and reversible-runtime procedure.
- [Clean-Room Diagnostics](../rogue-command-clean-room-diagnostics/SKILL.md) — telemetry and probe contracts.
- [Harmony Policy](../rogue-command-harmony-policy/SKILL.md) — BepInEx 5, Harmony 2, compatibility tuple, and source patch rules.
- `tools/bepinex_mcp_server.py` and `tools/build_bepinex_mcp.py` — project bridge handlers and schemas. Keep them synchronized when changing the tool contract.
- [Native UI and intermission](references/ui-intermission.md) — screenshot-free control selection, reward screens, run-map actions, and transition checks.

Snapshot regression tests verify presence and exact bounded arguments together for visual material and surface censuses; the GameTimeScale presence assertion remains independent.

Reward choice and Shop purchase controls support optional fresh-state `preconditions` and read-only `dryRun`; consult the intermission reference before using them. Dry runs do not establish native action validity.
