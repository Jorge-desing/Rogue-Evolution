---
name: rogue-command-modding-tools
description: "Use Rogue Command tools for verified asset and pack work."
version: 1.5.1
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, modding, asset pipeline, research, automation]
    related_skills: [rogue-command-reverse-engineering, rogue-command-assetbundle-authoring, rogue-command-vfx-authoring, rogue-command-animation-authoring]
---

# Rogue Command Modding Tools

Authoritative directory of verified asset, pack, and research workflows for Rogue Command.

---

## 1. Environment & Execution Guidelines

- **Python Environment:** Run scripts via `.venv` with `PYTHONPATH=.venv/Lib/site-packages`. Never install dependencies globally into system Python.
- **Import Contract:** From the repository root, run tools that import shared helpers with module syntax (`python -m tools.<module>`). The `tools/` directory is a Python namespace package; direct execution such as `python tools/validate_ai_modules.py` can fail to resolve `tools.*` imports. `tests/python/` loads tool files with `load_tool_module()`.
- **Blender 4.5.14 LTS:** Authoritative geometry, UV, socket, LOD, and vertex-wear generator (`.tools/Blender-4.5.14/.../blender.exe`).
- **Unity 2021.3.45f2:** Authoring project at `unity/RogueCommandAssetAuthoring/`. Standalone from user projects; preserve all `ProjectSettings`.
- **Material Maker 1.7:** Authoritative PBR image generator (`tools/Run-MaterialMakerBake.ps1`). Outputs PNGs only.

---

## 2. Content Pack Authoring & Inspection Tools

| Script / Tool | CLI Invocations & Arguments | Primary Function |
|---|---|---|
| `tools/scaffold_pack.py` | `--name <Name> [--validate]` | Generates minimal valid `content.json` with 1:1 unit/factory pairs |
| `tools/validate_content_pack.py`| `<pack_dir>` | Fast preflight check for manifest paths and duplicate IDs |
| `tools/validate_loader_registry.py` | `[--json]` | Authoritative loader registry validation across all packs |
| `tools/merge_content_packs.py` | `--left <dir> --right <dir> --out-dir <dir>` | Additive manifest merger with collision warnings |
| `tools/diff_content_packs.py` | `--left <dir> --right <dir> [--json]` | Diff analysis of units, stages, and profiles |
| `tools/audit_content_references.py`| `<pack_dir> [--all] [--json]` | Detects dangling IDs, shared factories, and missing `en-US` strings |
| `tools/clone_content_pack.py` | `--pack-dir <dir> --new-id <id>` | Re-prefixes namespaced IDs into a new isolated pack |
| `tools/inspect_content_pack.py`| `<pack_dir> [--json]` | Summarizes factory-unit links, pools, and localizations |
| `tools/validate_ai_modules.py` | `python -m tools.validate_ai_modules <pack_dir> [--all] [--json]` | Validates profiles against module IDs in the C# registry and the parameter mirror |
| `tools/analyze_ai_profile_coverage.py` | `python -m tools.analyze_ai_profile_coverage <pack_dir> [--json]` | Checks AI product `unitId` references against the repository unit catalog, including cross-pack products |
| `tools/analyze_blueprint_pools.py` | `<pack_dir> [--json]` | Scans stage blueprint pool coverage and fallback risks |
| `tools/analyze_relic_synergy.py`| `<pack_dir> [--json]` | Cross-references relic `neededSystemTags` vs unit `offeredSystemTags` |
| `tools/validate_native_templates.py`| `<pack_dir> [--json]` | Verifies `nativeTemplateId` values against native presets |
| `tools/export_balance_table.py`| `<pack_dir> [--json\|--csv]` | Dumps resolved unit balance adhering to loader precedence |
| `tools/lint_content_pack.py` | `<pack_dir> [--all] [--json]` | Quality linter for missing `es-XL`, map size inverted ranges |
| `tools/audit_unity_bundle.py` | `<bundle> [--output out.json]` | Inspects serialized object counts, shaders, and dependencies |

---

## 3. Asset & Visual Validation Commands

```powershell
# Validate AssetBundle references declared in content manifest
python tools/validate_content_bundle_references.py content/packs/sample-native-expansion

# Validate native visual roots and renderer extent boundaries
python tools/validate_native_visual_roots.py content/packs/sample-native-expansion

# Resolve nativeRigRetarget source and bundle target paths against serialized hierarchies
python -B -m unittest tests.python.authoring.test_native_visual_retarget_preflight

# Validate mech rigging hierarchies and Generic Avatar bone structures
python tools/validate_mech_rigging.py --pack content/packs/sample-native-expansion --json

# Validate VFX hierarchies and ParticleSystem sockets
python tools/validate_vfx_assets.py --pack content/packs/sample-native-expansion --json

# Run complete loader registry audit
python tools/validate_loader_registry.py --json

# Audit all AI module IDs, parameters, and cross-pack roster references
python -m tools.validate_ai_modules content/packs --all --json
python -m tools.analyze_ai_profile_coverage content/packs/sample-reactive-ai --json
```

AI `unitEntries` and `productionBuildingEntries` both use the produced product's `unitId`. The Python module ID source of truth is `AiModuleRegistry.CreateDefault()` via `tools/ai_module_contracts.py`; parameter keys and bounds must still be synchronized with each module's C# parser. Static audits do not prove that a profile is selected or its runtime behavior executes.

---

## 4. Live Runtime & Intermission Progression Reference

- **No-Jump Native Progression (Preferred):** Intermission `Continue Run` $\rightarrow$ `LoadScene.LoadLevel()` on conserved map $\rightarrow$ `LoadingHider` active in `Game` scene $\rightarrow$ Click **INICIAR** button (`1008136`) $\rightarrow$ Live match (`timeScale=1`).
- **Boss Node raycast:** Coordinate raycast `diagnostics_order_select_level_node(level=LevelCount, field=0)` on `OverWorldMapBoss`. Never click decorative `BossTriangle`.
- **Boss Node readback:** The graph includes its special node at `(LevelMap.LevelCount, 0)`. `GetIntermissionNodeJson` reports `found=true`, `isBoss=true`, no rewards, and the incoming connections from the preceding level; `isChoosable` is true only at the last pre-boss level.
- **AI Runtime Gate:** `GetAiModulesDiagnosticsJson` is the registered-module catalog, not an active-profile census. Resolve the current stage's AI prefab/profile and verify its live director and roster before running module tests. Do not push an existing saved route forward only to reach a different profile.
- **Configured Stage Order:** Before route navigation, inspect `GetActiveStageAiAssociationJson` and its `stageSequenceRows`. A resolved later configured row can establish declared stage/profile mapping without advancing through unrelated stages. Keep `stageSequenceStatus=partial` when extra returned rows are unresolved; the mapped row is configuration evidence only, and a Game-scene director/roster plus behavior observations are still required for execution claims.
- **Synthetic Advancement (User-Authorized):** `diagnostics_jump_to_level` and `diagnostics_load_next_level` advance state when natural path is blocked; tagged with `synthetic=true`.
- **MCP Quirks:** `component/call_method` requires full type names; `LoaderDiagnostics` ID is scene-dependent (re-resolve via `find_objects_with_component`).
- **Retarget Readback:** `diagnostics_inspect_entity_visual` includes `animation.nativeRigRetarget` state plus up to 16 rows with mapping paths, source/target availability, current local poses, and computed target pose. `omittedMappingCount` precedes `mappings`. Equal counts and a computed pose confirm mapping/math state only. A 2026-11-10 BepInEx MCP sample captured the live FireRecoil clip and matching yaw/pitch poses; death suspension and natural pool reuse remain separate claims.
- **Retarget Static Preflight:** `tools/validate_native_visual_roots.py <pack>` validates each exact native `sourcePath` and declared bundle-prefab `targetPath` after checking the game version/SHA/MVID tuple. The companion test also checks pack-relative bundle path containment. This is static hierarchy evidence only.
- **Saved-Run Restart:** If the current session is in the tutorial or a menu state cannot continue the run, close Rogue Command and relaunch through Steam, then choose an existing tutorial-complete profile with the native profile UI and use Continue Run. Never select/reset a clean profile or edit tutorial files. Profile and scene GameObject IDs are session-dependent; resolve them again through BepInEx MCP.
- **Screenless Runtime Automation:** Use `system_get_game_process_state`, `system_launch_game` (Steam AppID `1461910`, no profile change), `system_wait_for_game_bridge`, `diagnostics_get_ui_accessibility_census`, `diagnostics_wait_for_ui_text`, and `diagnostics_ui_click_text` for app/menu control without focus, Computer Use, or screenshots. `automation_run_sequence` can chain up to 16 diagnostics calls with a shared bounded request/delay budget and failure stop. A timed-out in-flight step is completion-unknown; query its postcondition before retrying. Click by a unique active label or exact census `hierarchyPath` (including unlabeled controls); ambiguity and EventSystem occlusion fail closed. If a needed control/state is not exposed, add MCP telemetry and report the evidence gap instead of using a capture.
- **Native Michsky Buttons:** Some game buttons are `Michsky.UI.Reach.ButtonManager` objects, not Unity `Selectable`s, so the accessibility census/EventSystem click path cannot activate them. Use `diagnostics_ui_invoke_native_button` with exact GameObject `selector`, current active scene, and `expectedButtonText`, plus the exact scene-qualified `hierarchyPath` when names repeat. The bridge resolves exactly one match and requires the GameObject active and `enabled`, `isActiveAndEnabled`, and `isInteractable` all explicitly true before invoking only `ButtonManager.InvokeOnClick`. Missing state, scene mismatch, ambiguity, or a text mismatch fails closed; do not replace this with arbitrary `component/call_method` calls. This native callback path uses no screenshot or Computer Use.
- **MCP Schema Source:** Keep `tools/build_bepinex_mcp.py` schemas and `tools/bepinex_mcp_server.py` handlers in sync. Regenerate MCP JSON schemas after changing tool names/parameters. Runtime UI census/click APIs live in `RuntimeUiAccessibility.cs` and `LoaderDiagnosticsComponent`; the plugin already enables `Application.runInBackground`.
- **Graceful Shutdown:** `diagnostics_request_graceful_exit` requires `confirmExit=true` because Unity may persist save state. Verify actual process closure and restore profile/checkpoint artifacts according to the runtime runbook.
- **Steam Cloud Control:** `diagnostics_get_steam_cloud_state` reads account/app state; `diagnostics_set_steam_cloud_enabled` changes only Rogue Command's per-app toggle and requires explicit user authorization. The wrapper lazily initializes Steamworks for a query and shuts it down only when it owns that initialization. Never change the account-wide toggle or call the setter without the user's request. Restore/verify the selected profile bytes before re-enabling Cloud.
- **Frame-Coherent Retarget Sampling:** Batch the moving flag, current Animator clip, and mapped source/target poses in one bridge frame when possible. A paused `Time.deltaTime == 0` intentionally clears the movement policy, and separate calls may observe different frames.
- For spawned walker QA clones, check world height: `SpawnEntityJson` places them at Y=0; native battlefield targets may be at Y=10. Correct only the temporary test clone before measuring range/aim. The reuse-transpiler counter must advance on an actual pooled reuse; an active non-pooled method call is not a substitute.
- Run lifecycle probes only through BepInEx MCP, and remove temporary Harmony patches/components/watchers before exit. To prove retarget suspension at `Destroy`, place a postfix on `RuntimeHarmonyPatches.OnEntityDestroyTranspilerHook`; inspect `IsSuspended` there because the entity may be unavailable after the original method continues. Label direct diagnostic damage separately from natural combat. For ordinary continuation, restart and select a saved tutorial-complete profile via native UI; after graceful exit restore changed metadata from the hash-matched backup and verify the complete profile checkpoint. See `artifacts/runtime-smoke/restart-saved-profile-20261110T061923/retarget-destroy-entry-follow-up.json`.
- Static inspection on the pinned build shows the factory's `PrespawnedEntities` stack is filled by `PrespawnEntity` and consumed by `InstantiateEntity`; `EntityController.Destroy` decrements its count and destroys the GameObject instead of returning it to the stack. Use that result to separate the verified stack-pop reset path from destroyed-entity cleanup: `artifacts/runtime-smoke/native-retarget-natural-death-20261110T140359Z/native-prespawn-stack-static-evidence.json`.
- If Steam launch stops at `SynchronizingCloud` with `cloudconflict` for a saved profile, preserve the all-profile checkpoint and the selected profile backup, verify the local/cloud file identities, and do not choose a side automatically or launch the game executable directly. Resolve the specific file-source choice with the user before retrying the Steam launch. A 2026-11-10 attempt left all profile checkpoint entries unchanged and did not create `Rogue Command.exe`.
- `SpawnEntityJson` accepts the player side as `Tags.Player` and the AI side as `Tags.Ai` (canonical tag string `AI`), case-insensitively; reject other values instead of silently converting them to Player. On the 2026-11-10 Profile2 run, requesting `Ai` produced native tag `AI` and `IsControlledByAi=true`. The MCP string-ID converter could not invoke `EntityController.IsEnemy(EntityController)`, so hostility was not directly runtime-tested.
- A successful Steam AutoCloud upload at process exit can upload files that you later restore locally. After restoring a selected-profile checkpoint, inspect the matching `cloud_log.txt` and `remotecache.vdf` entries too; local checkpoint equality alone does not prove the remote Cloud copy was restored. The next Steam launch can stop at `cloudconflict` before BepInEx starts. Profile2 evidence and the unresolved remote meta hash are recorded in `artifacts/runtime-smoke/retarget-animation-restart-profile2-20261110T1605Z/profile-restore-after-test.json`.

---

## 5. References

- [3D Model Authoring](../rogue-command-3d-model-authoring/SKILL.md) & [Mech Rigging](../rogue-command-mech-rigging-authoring/SKILL.md)
- [PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md) & [VFX Authoring](../rogue-command-vfx-authoring/SKILL.md)
- [Steam Runtime Validation](../rogue-command-steam-runtime-validation/SKILL.md) & [Reverse Engineering](../rogue-command-reverse-engineering/SKILL.md)
- [Pack Validation](../rogue-command-pack-validation/SKILL.md)

## BepInEx retarget death-hold probe

- Continue a saved, tutorial-complete Profile2 run selected through the game's native profile flow. Keep the match frozen at `Time.timeScale=0`; use BepInEx MCP for scene state, diagnostic spawn/damage, observer setup, and cleanup.
- To prove a death visual hold without advancing the saved match, set `Animator.updateMode=UnscaledTime` only on a disposable probe clone before lethal diagnostic damage. Capture the exact clip, clip duration, hold countdown, `DestroyHasBeenStarted`/`IsDestroyed`, and post-hold removal; then remove observer/helper state and verify no clone, patches, or watchers remain. State clearly that this is runtime telemetry, not natural combat or rendered-pixel evidence.
- Cloud conflict handling is part of profile safety. If the user selects Steam Cloud and the run changes local `metaSavegame.dat`, preserve the Cloud-selected bytes and do not allow normal exit/sync to overwrite them until an explicitly approved sync-safe close/restore path is available. Check both the local checkpoint and Steam `remotecache.vdf`/`cloud_log.txt` hashes.
- Evidence for this procedure: `artifacts/runtime-smoke/retarget-death-visual-cloud-20261110170654/death-hold-runtime-result.json`.

### BepInEx renderer-frame MCP tools

- `automation_run_sequence` accepts exact typed references to prior successful JSON results: `{"$ref":{"step":0,"path":"/entityKeyId"}}`. Step indexes are zero-based JSON Pointers; forward, failed-step, missing-path, malformed, or out-of-range references fail closed. Use this for spawn → command → evidence-wait chains without hand-copying unique entity IDs. In-flight timeout still means completion is unknown; inspect the resulting game state before retrying.
- For one-call animation sampling, use `diagnostics_wait_for_renderer_frame_evidence`. It polls bounded direct Game-camera samples until the requested distinct-frame count and optional clip/evidence signals match, then attempts probe cleanup in `finally` after receiving a valid token. `requiredEvidence` accepts `moving`, `stationary`, `deformation`, `retargetBound`, and `visibleToCamera`; `pixelCapture` stays false. A timeout or bridge cleanup failure must remain explicit in the result.
- The lower-level `diagnostics_start_renderer_frame_probe`, `diagnostics_read_renderer_frame_probe`, and `diagnostics_stop_renderer_frame_probe` remain available for custom timing. The one-call tool uses 12 samples per renderer and polls every 250 ms.
- When changing these tools, update both `tools/bepinex_mcp_server.py` handlers and `tools/build_bepinex_mcp.py` schemas, regenerate with `python tools/build_bepinex_mcp.py`, and run `python -m unittest tests.python.test_bepinex_mcp_diagnostics`. Static schema tests do not prove Unity telemetry; runtime results do not prove pixel appearance or gait quality.
- UI controls can be found from the screenshot-free accessibility census and clicked by exact scene-qualified path through the EventSystem raycast. Use `system_get_game_process_state` to verify process closure; do not infer appearance from UI rectangles or renderer telemetry.
- If the connected BepInEx MCP surface exposes only generic bridge tools, call `LoaderDiagnosticsComponent` through `component/call_method` with its full type name, a freshly resolved scene GameObject ID, and JSON-array `args_json`; verify the nested `ReturnValue`. `EntityController.UniqueEntityId` is the per-instance command key when several live units share one `EntityId`. A saved-Profile2 native factory test used this route for `OrderFactoryProduceJson`, movement, and frame telemetry; evidence: `artifacts/runtime-smoke/retarget-factory-production-20261110/factory-retarget-runtime-summary.json`.

### Screenshot-free native run UI and AI gate

- Use `diagnostics_ui_invoke_native_button` only for a `Michsky.UI.Reach.ButtonManager`. When supplying `hierarchyPath`, include the scene qualifier (`Scene::/Root/...`); an unqualified path will not match the scene-qualified search result. The handler still verifies exact name, text, active state, and interactability before invoking the native callback.
- Unity `Selectable` controls, including the RunSetup opener's `ConfirmButton`, use `diagnostics_ui_click_text` with the exact scene-qualified path from `diagnostics_get_ui_accessibility_census`; this performs the real EventSystem raycast/pointer sequence without screenshots or Computer Use.
- Before confirming a new run, preserve a full profile checkpoint and read the overwrite dialog's exact title/body/confirm/cancel text. Afterward, close through BepInEx MCP, verify process exit, restore the active profile from its verified backup, and require an exact checkpoint match.
- `diagnostics_get_ai_modules` and the loader audit are registry/readiness observations. For an AI runtime claim, first resolve a stable stage/template/profile ID. A localized `BattlePreviewWidget` enemy label alone cannot establish which profile is attached; stop and report `UNKNOWN` if no direct mapping exists. Profile2 evidence: `artifacts/runtime-smoke/ai-scheduled-build-20261111T012405Z/live-ai-runtime-pass-summary.json`.

### Active stage AI association diagnostic

- BepInEx MCP 1.4.5 adds `diagnostics_get_active_stage_ai_association` (239-tool schema catalog). It is also included in the fast `match` snapshot and runtime snapshot getter set.
- Call it before advancing a saved route for AI acceptance. It resolves the active `StageMap` entry against the captured progression sequence and the persisted `rclStageOrderV1` identity; custom stages return their qualified ID and declared `aiProfileIds`, while native stages return the native StageParameters asset ID. It never maps a localized preview label to an ID.
- Read configured IDs (`declaredAiProfileIds`) separately from initialized loader-bound behaviours (`runtimeBoundAiProfiles`, counts, and status). Empty runtime bindings at intermission do not describe combat AI behavior. An unresolved stage identity is a stop condition, not a reason to guess from preview text.
- The same read-only tool now adds `stageSequenceStatus`, `stageSequenceIdentitySource`, `stageSequenceActiveIndex`, failure/omission counters, and bounded `stageSequenceRows` with stable IDs and per-stage declared profiles. Update its description in `tools/build_bepinex_mcp.py`; no new handler is needed because the existing `LoaderDiagnosticsComponent.GetActiveStageAiAssociationJson()` supplies the payload. Regenerate the local schema, noting that connected hosts do not hot-reload it.
- Regenerate local schemas with `python tools/build_bepinex_mcp.py`. This creates the catalog but does not cause an already connected MCP host to reload it automatically.

### Renderer frame capture filters and cleanup (MCP 1.4.6)

- `diagnostics_start_renderer_frame_probe` accepts `sampleClipContains` and `sampleMovementState` (`any`, `moving`, `stationary`). Its C# diagnostic API filters camera frames before `BakeMesh` and before consuming the bounded sample cap; the wait tool maps `activeClipContains` and movement evidence to these filters.
- Keep `tools/build_bepinex_mcp.py`, `tools/bepinex_mcp_server.py`, `LoaderDiagnosticsComponent.StartRendererFrameProbeJson`, and the generated 239-tool schema catalog synchronized. Re-run `python -m unittest tests.python.test_bepinex_mcp_diagnostics` and `python -m py_compile tools/build_bepinex_mcp.py tools/bepinex_mcp_server.py` when changing the contract.
- Stop is idempotent while the stopped snapshot remains retained. The wait tool verifies an unavailable token through a complete `RuntimeRendererFrameProbeBehaviour` component census and does not claim success when observers remain or the census fails.
- Before retrying a start whose response timed out or was truncated, census live probe components because Unity may have created an observer before the caller lost the result. Use `sampleClipContains` and `sampleMovementState` to filter before `BakeMesh` and the per-renderer cap; always stop by token and verify the observer census is empty.
- Exact installed Release SHA `fe08d495c3236a174c31e89bfae236c79f6af42541404770845a1043934f1856` was re-exercised through the connected BepInEx MCP runtime: 32 filtered Walk/moving samples across four renderers, 8/8 retarget bindings in sampled rows, positive post-baseline `BakeMesh` deltas observed, and clean stop/census. Report: `artifacts/runtime-smoke/retarget-final-release-20261111T050500Z/final-release-retarget-summary.json`. This does not prove pixel appearance or gait quality.

### Screenshot-free guaranteed reward inspection (2026-11-11)

- Through BepInEx MCP `component/call_method`, call `RogueCommandExpansionLoader.Runtime.LoaderDiagnosticsComponent.GetIntermissionRewardStateJson(Int32 maxCards)` and `GetIntermissionNodeJson(Int32 level, Int32 field)`. Use a small card cap (for example 10) and retain the active reward, slot IDs/names, and `blueprintPoolId` together so a fallback is not mistaken for a theme match.
- Resolve the offered `CardNew` and read `EntityId`, its title `Text.text`, `factoryImage.sprite` and `Image.enabled`. Search the card subtree for `SkinnedMeshRenderer`; read `enabled` and `isVisible`. Neither `Renderer.isVisible` nor a non-null sprite is proof of captured pixels.
- When a pass only validates offer generation, leave the reward unselected and exit through the native game route. Never start a clean/empty profile. For an explicitly authorized new run, checkpoint all profiles and the selector, preserve a tutorial-complete source byte-for-byte, back up the authorized QA destination, and clone the complete source while the game is closed. Verify both tutorial flags on the clone, select it through native UI, read any overwrite dialog before confirming, snapshot generated files after exit, restore the original destination and selector, and verify the full checkpoint. Never call tutorial completion/reset methods or edit `tutorialState.txt`.
- In the 2026-11-11 Profile3 AI pass, the configured row 13 named `sample-reactive-ai:expansion:sample-reactive-ai:counterstrike-lane` declared `sample-reactive-ai:base-defense`, while the active Game stage was native row 0 with zero loader-bound instances. A registered module count or nonzero generic hook counters do not prove the configured profile ran; see `docs/research/AI-REACTIVE-PROFILE3-RUNTIME-2026-11-11.md`.

### Typed helper for retarget lifecycle diagnostics (2026-11-11)

- The generic `set_component_value` bridge rejected both string and numeric writes for AnimatorUpdateMode, and generic `call_component_method` could not bind a null EntityController reference for `TakeDamage(float, EntityController, bool, bool)`. Avoid repeated coercion guesses. For a disposable QA actor, use a temporary typed BepInEx helper that resolves the exact method and enum type, records the prior value, applies only the actor-scoped change, and reports the invocation result.
- Keep a direct-diagnostic lethal call labeled separately from natural combat. Afterward verify actor removal, temporary helper and renderer-observer component censuses, pause state, graceful process exit, and profile checkpoint. Current Release details: `docs/research/RETARGET-CURRENT-RELEASE-RUNTIME-2026-11-11.md`.
- With the generic BepInEx bridge, `StartRendererFrameProbeJson` returns its handle as `token`; pass that exact value to Read/Stop and do not assume `sessionToken`. The probe allows at most four renderers and 20 seconds, so save the token before any wait and stop before the runtime cap. On the d576 Profile3 retarget pass the probe auto-stopped before a retained-token census; record that limit instead of claiming observer-census cleanup. See `docs/research/RETARGET-D576-RUNTIME-2026-11-11.md`.
- When only the generic BepInEx MCP bridge is connected, call `LoaderDiagnosticsComponent` directly with `call_component_method`; no screenshot/UI tool is needed for structured runtime evidence. Focus the Game camera on the disposable actor before a renderer-frame probe: high camera callback counts with zero samples and high frustum rejection only describe an off-screen target. The d576 death retry used the returned `token`, stopped after 1.16 unscaled seconds, and confirmed an empty observer census. The probe clamps per-renderer sample limits to a minimum of two.
- A typed injected helper compiled against the pinned Mono game API can set `AnimatorUpdateMode.UnscaledTime` only on the disposable actor and call the exact `TakeDamage(float, EntityController, bool, bool)` overload when generic bridge argument conversion cannot represent the enum or null controller. Read and record the prior Animator mode first; remove/census the helper after use. Direct diagnostic damage remains separate from natural combat. Evidence: `docs/research/RETARGET-D576-DEATH-RUNTIME-2026-11-11.md`.
- After a native level transition, verify the `LoadingHider` button through the scene-qualified BepInEx census and invoke the real `Iniciar` control through the EventSystem. Do not infer movement from `Game` scene plus `timeScale=1`; the d576 factory-product sample moved only after this opener was cleared. See `docs/research/RETARGET-D576-FACTORY-WALK-RUNTIME-2026-11-11.md`.

### Active building ghost MCP inspection (1.4.9 / loader 1.20.9)

- `diagnostics_inspect_active_building_placement` takes no arguments. It reads the selected native blueprint's live placement ghost and preflights that exact footprint in one Unity-thread call; it returns snapped world position, per-cell occupancy/layer/fog facts, and the native rejection reason without a screenshot, placement, or credit spend.
- `diagnostics_order_place_building` defaults to `commit=false`; keep it read-only for diagnosis. `diagnostics_find_building_placement` performs a bounded Manhattan search (0–12 cells) and never commits. A no-result search may reflect a global native rule such as refinery distance, while its candidate cell rows describe local footprint blockers.
