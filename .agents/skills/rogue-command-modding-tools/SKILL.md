---
name: rogue-command-modding-tools
description: "Use Rogue Command tools for verified asset and pack work."
version: 1.1.0
license: MIT
author: Hermes Agent
metadata:
  hermes:
    tags: [Rogue Command, modding, asset pipeline, research, automation]
    related_skills: [rogue-command-reverse-engineering, rogue-command-assetbundle-authoring, rogue-command-vfx-authoring, rogue-command-animation-authoring]
---

# Rogue Command Modding Tools

Use this skill to select the repository's verified asset, pack, and research workflows for Rogue Command. Generic asset-pipeline examples are not assumed to exist in this checkout; the pinned project tools and their contracts are authoritative.

## When to use

- Preparing or auditing a model, Unity visual prefab, PBR maps, VFX, or AssetBundle.
- Choosing the correct offline validator or inspector for a pack or model.
- Planning evidence collection for a live game claim.

Load the domain skill for detailed authoring: [3D Model Authoring](../rogue-command-3d-model-authoring/SKILL.md), [Mech Rigging](../rogue-command-mech-rigging-authoring/SKILL.md), [PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md), [VFX Authoring](../rogue-command-vfx-authoring/SKILL.md), [Animation Authoring](../rogue-command-animation-authoring/SKILL.md), [AssetBundle Authoring](../rogue-command-assetbundle-authoring/SKILL.md), or [Reverse Engineering](../rogue-command-reverse-engineering/SKILL.md).

## Asset Pipeline

### Learn the target contract first

Before conversion, inspect the game's reference assets and the actual consumer in the current Unity project. Record dimensions, orientation/axes, pivot, alpha, style, texture/channel layout, shader/material, hierarchy, and serialized asset type. Do not assume a generic pipeline or file extension matches the game's contract.

### Project authoring path

- Preserve editable sources under `content/source-art/` and use the repository's deterministic Blender 4.5.14 tools: `tools/Build-*.py`, `tools/generate_unit_model.py`, and `tools/RogueModelKit.py`.
- Author/import/build only in `unity/RogueCommandAssetAuthoring/` using Unity 2021.3.45f2. It is separate from the user's `Rogue Evolution` Unity Hub project; preserve `ProjectSettings`, `Packages`, Version Control metadata, and Cloud settings.
- Keep canonical socket names unprefixed and unique. Search recursively for imported sockets before builders create new ones. Native visual overlays keep the native gameplay hierarchy but do not automatically retarget animations or weapon rigs.
- Keep maps, previews, logs, and diagnostics under ignored `artifacts/`. Put only deliberate distributable outputs under `content/packs/<pack-id>/bundles/`.

### PBR maps and sprite formats

- Use the existing Blender bake for `<Name>_Albedo.png`, `<Name>_MetallicGloss.png`, `<Name>_Normal.png`, and `<Name>_Emission.png`.
- Preserve the project packing: MetallicGloss RGB = metallic, A = smoothness (`1 - roughness`), imported linear with alpha from input; Albedo/Emission are sRGB; Normal is imported as a normal map. Keep mesh vertex-wear RGBA separate from these texture channels.
- ArmorPaint and Material Maker are optional image-only steps. Do not replace the authoritative Blender mesh, UVs, hierarchy, sockets, LODs, or vertex colors with a painter's mesh export.
- Before image conversion, inspect stock game's dimensions, facing, pivot/hotspot, alpha, palette, and format. Follow the current card sprite and image-authoring rules; generic sprite-strip commands are not assumed to be available.

### External generic workflows are not project commands

The `um sprite`, `um render3d`, and `um scan` commands from the universal-modder reference are not verified repository tools. Do not present them as runnable or required unless a separate installation was checked. Do not require fal, Blender MCP, remote generation, Addressables, or a newer Unity Editor for the reproducible pipeline. A generated image or a 3D render is a reference/input, not a validated FBX, prefab, or runtime bundle.

## Research Pipeline

### Game and artifact reconnaissance

1. Read the repository handoff and baseline before inspecting assets or code.
2. Establish target version, Unity/backend, assembly identity, relevant serialized assets, and tool versions from current local evidence.
3. Use UnityPy/UnityDataTool for serialized inventories and the repository's validators for specific contracts; keep decompiled code outside the repository.
4. Record the exact observation and its evidence class in `docs/research/` only when it is durable and verified.

### Rogue Command identity and modding route

- Current documented target: Rogue Command 1.0.9, Unity 2021.3.45f2, Mono x64, BepInEx 5.4.23.5 + Harmony. Recheck before relying on this tuple.
- The loader's supported path combines compiled plugin code for behavior, JSON manifests for declarative content, and Unity AssetBundles for serialized assets. Bundles do not carry new executable `MonoBehaviour` gameplay classes.
- Before relying on reflected signatures or metadata tokens, verify the full tuple together: Rogue Command version, Unity version, `Assembly-CSharp.dll` SHA-256, and MVID. A remembered hash or same-named member is insufficient.
- Keep BepInEx/Harmony/game assemblies out of distributable ZIPs. Only the local development build may reference them.

## Game Automation and Runtime Safety

- Launch/observe Rogue Command only through Steam and follow `docs/research/STEAM-RUNTIME-CONTROL.md` plus the Steam Runtime Validation skill. Check process and bridge independently; tools installed locally do not prove a live connection.
- Begin with read-only bridge observations. Do not use mutating RPC calls, runtime code injection, or Harmony patches as proof of ordinary mod behavior.
- Verify pause using native pause state or `Time.timeScale == 0` / stable scaled clock; a visible menu is not proof of pause.
- Before live deployment, record hashes for every touched installation file, the authoritative selector, and each file in each profile. Use the dedicated authorized QA profile and preserve all saves; restore deployment and selector and verify hashes after the game exits.
- Keep Steam Cloud in its instructed state. Never edit `tutorialState.txt` directly or infer safe profile state from stale notes.

## Pack and Asset Validation

### Python tooling and test loading

- Run `./tools/run_python_checks.sh` from the repository root for the supported Python verification path. It selects the project `.venv`, clears inherited `PYTHONPATH` so Hermes' cp314 packages cannot shadow cp312 wheels, runs `tests/python`, then checks bundle references, the loader registry, and whitespace. Do not substitute a system interpreter or install dependencies globally.
- Tests in `tests/python/` load scripts from `tools/` through `load_tool_module()` in `tests/python/helpers.py`; do not use direct `from tools.X import Y` imports because `tools/` is not a Python package.
- With UnityPy 1.25.4, bundle dependencies are stored on the serialized `AssetBundle` object's `m_Dependencies`; `env.file.dependencies` is not a `BundleFile` API.
- `tools/audit_unity_bundle.py <bundle> [--output report.json]` reports serialized object counts, shader/texture metadata, bundle size, and declared dependencies. It is offline inventory only; it does not prove Unity import, resource selection, rendering, or gameplay.
- `tools/validate_content_pack.py <pack-dir>` is a quick manifest/path/duplicate-ID preflight. It uses an embedded simplified schema, not the authoritative `content/schema/content.schema.json`; although `--schema` appears in its CLI help, the current implementation does not consume that argument. Do not rely on it for full schema or loader-contract validation; use the repository schema checks, `tools/validate_loader_registry.py`, and the C# harness as appropriate.
- `tools/profile_hash_audit.py <profile-dir> <manifest.json>` compares one profile directory with `profileFiles` entries and reports added, missing, and changed files. It is read-only; keep checkpoint manifests and reports under ignored `artifacts/`, include all profile files in the checkpoint, and do not commit or publish profile data or hashes.
- `tools/extract_game_strings.py <binary> [--min-length N] [--output strings.txt]` extracts printable ASCII and zero-paired UTF-16LE strings. Treat output as a search aid, not decompilation or semantic/runtime evidence; keep generated reports under ignored `artifacts/`.
- Keep the project dependency set in `requirements.txt` and use the checked-in runner to select the interpreter. Do not install packages into Hermes or another global Python environment to repair project tests.

Run these commands from the repository root via `terminal` with the project virtual environment; explicit `PYTHONPATH` prevents globally visible Hermes packages from shadowing local dependencies. Use `--help` for exact current options.

```text
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_content_bundle_references.py content/packs/sample-native-expansion")
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_native_visual_roots.py content/packs/sample-native-expansion")
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_mech_rigging.py --pack content/packs/sample-native-expansion --json")
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_vfx_assets.py --pack content/packs/sample-native-expansion --json")
terminal(command="PYTHONPATH=.venv/Lib/site-packages .venv/Scripts/python.exe tools/validate_loader_registry.py --json")
```

Interpret each tool only within its implementation: bundle references check declared serialized names/types; visual-root validation checks native path uniqueness/renderers against the local game's assets; rigging/VFX validators check only their documented structural rules; registry validation checks manifests, not in-game registration. Preserve real diagnostics and exit codes.

For a serialized inventory, use the pinned UnityDataTool 2.4.0 (`.tools/UnityDataTool-2.4.0/UnityDataTool.exe`) or UnityPy in the project venv. These inspect bundle contents offline; neither proves runtime selection or rendering.

## Evidence and Showcase

- Record outcomes as **static inspection**, **local build/validation**, or **in-game runtime evidence**. A clean build, parsed bundle, passed validator, plugin-loaded line, or registered resource name does not prove gameplay, animation, or rendering.
- For screenshots/recording, identify the game version, pack, unit/effect, scene, and test path. Do not stage a forced spawn or modified state as evidence of a natural offer or ordinary behavior.
- Update `docs/research/AGENT-HANDOFF.md` only with durable, verified findings and retain the narrower scenario and evidence boundary.

## References

- [Skill search and adoption workflow](references/skill-search-and-adoption.md) — evaluate external skills without assuming compatibility or local installation.
- [Game research](../rogue-command-game-research/SKILL.md)
- [Steam runtime validation](../rogue-command-steam-runtime-validation/SKILL.md)
- [AssetBundle authoring](../rogue-command-assetbundle-authoring/SKILL.md)
- [VFX authoring](../rogue-command-vfx-authoring/SKILL.md)
- [Animation authoring](../rogue-command-animation-authoring/SKILL.md)
- [Reverse engineering](../rogue-command-reverse-engineering/SKILL.md)
- [Content authoring tools](../../../docs/research/CONTENT-AUTHORING-TOOLS.md)
