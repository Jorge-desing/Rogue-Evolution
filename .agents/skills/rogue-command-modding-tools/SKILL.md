---
name: rogue-command-modding-tools
description: "Use Rogue Command tools for verified asset and pack work."
version: 1.4.0
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

- The local MCP bridge exposes `diagnostics_start_renderer_frame_probe`, `diagnostics_read_renderer_frame_probe`, and `diagnostics_stop_renderer_frame_probe`. Start with an entity key and optional renderer-name filter/sample limit; keep the short-lived token, read structured JSON, then stop to remove observers. `pixelCapture` is always false.
- When changing this feature, update both `tools/bepinex_mcp_server.py` handlers and `tools/build_bepinex_mcp.py` schemas, then run `python -m unittest tests.python.test_bepinex_mcp_diagnostics`. Use the actual bridge for a live JSON parse and sample; static schema tests do not prove Unity telemetry.
- UI controls can be found from the screenshot-free accessibility census and clicked by exact scene-qualified path through the EventSystem raycast. Use `system_get_game_process_state` to verify process closure; do not infer appearance from UI rectangles or renderer telemetry.
- If the connected BepInEx MCP surface exposes only generic bridge tools, call `LoaderDiagnosticsComponent` through `component/call_method` with its full type name, a freshly resolved scene GameObject ID, and JSON-array `args_json`; verify the nested `ReturnValue`. `EntityController.UniqueEntityId` is the per-instance command key when several live units share one `EntityId`. A saved-Profile2 native factory test used this route for `OrderFactoryProduceJson`, movement, and frame telemetry; evidence: `artifacts/runtime-smoke/retarget-factory-production-20261110/factory-retarget-runtime-summary.json`.
