---
name: rogue-command-modding-tools
description: "Use Rogue Command tools for verified asset and pack work."
version: 1.3.0
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
- **Import Contract:** `tests/python/` scripts load `tools/` modules via `load_tool_module()` (`tools/` is not a Python package).
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
| `tools/validate_ai_modules.py` | `<pack_dir> [--all] [--json]` | Validates AI profiles against the 23-module registry |
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

# Validate mech rigging hierarchies and Generic Avatar bone structures
python tools/validate_mech_rigging.py --pack content/packs/sample-native-expansion --json

# Validate VFX hierarchies and ParticleSystem sockets
python tools/validate_vfx_assets.py --pack content/packs/sample-native-expansion --json

# Run complete loader registry audit
python tools/validate_loader_registry.py --json
```

---

## 4. Live Runtime & Intermission Progression Reference

- **No-Jump Native Progression (Preferred):** Intermission `Continue Run` $\rightarrow$ `LoadScene.LoadLevel()` on conserved map $\rightarrow$ `LoadingHider` active in `Game` scene $\rightarrow$ Click **INICIAR** button (`1008136`) $\rightarrow$ Live match (`timeScale=1`).
- **Boss Node raycast:** Coordinate raycast `diagnostics_order_select_level_node(level=LevelCount, field=0)` on `OverWorldMapBoss`. Never click decorative `BossTriangle`.
- **Synthetic Advancement (User-Authorized):** `diagnostics_jump_to_level` and `diagnostics_load_next_level` advance state when natural path is blocked; tagged with `synthetic=true`.
- **MCP Quirks:** `component/call_method` requires full type names; `LoaderDiagnostics` ID is scene-dependent (re-resolve via `find_objects_with_component`).

---

## 5. References

- [3D Model Authoring](../rogue-command-3d-model-authoring/SKILL.md) & [Mech Rigging](../rogue-command-mech-rigging-authoring/SKILL.md)
- [PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md) & [VFX Authoring](../rogue-command-vfx-authoring/SKILL.md)
- [Steam Runtime Validation](../rogue-command-steam-runtime-validation/SKILL.md) & [Reverse Engineering](../rogue-command-reverse-engineering/SKILL.md)
- [Pack Validation](../rogue-command-pack-validation/SKILL.md)
