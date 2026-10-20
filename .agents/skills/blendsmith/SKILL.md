---
name: blendsmith
license: GPL-3.0-only
description: "Use this Rogue Command adaptation for reference-led Blender vehicle assets, editable PBR materials, finalized UVs, texture baking, and a verifiable source handoff."
metadata:
  upstream_version: "2026.09.011"
  upstream_commit: "b46f037aa8c9596fc40c2eafb2c782ed9c0d63ac"
---

# Blendsmith — Rogue Command Adaptation

This active entrypoint adapts the public blendsmith workflow to Rogue Command. The original upstream skill and its references are preserved under [references/upstream](references/upstream/), with the GPL-3.0-only license and provenance notice in this folder. Treat that snapshot as source material, not as a second set of active project rules. This entrypoint and the repository's Rogue Command skills define the adapted behavior.

## Project authority and version

- Before authoring, read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, `unity/RogueCommandAssetAuthoring/README.md`, [Rogue Command 3D Model Authoring](../rogue-command-3d-model-authoring/SKILL.md), and [Rogue Command PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md). Read the PBR validation skill when checking imports or final maps.
- Use the pinned **Blender 4.5.14 LTS** executable and current repository generator. The upstream workflow baseline is Blender 4.4; do not change this project to 4.4 or assume Blender 5.x APIs.
- Reuse user-confirmed specifications and proceed within the authorized task. Ask only for a consequential missing requirement that cannot be resolved from the project or existing session. Communicate in the user's language; do not impose the upstream English-only worklog or intake rules.
- The local Rogue Command skills override generic asset conventions. In particular, follow the canonical socket names, FBX hierarchy, vertex-wear channels, LOD selection, and task-owned output boundaries from `AGENTS.md` and the local 3D skill.

## Reference-led asset and texture workflow

1. Inspect supplied references and the current Blender source. Separate observed features from estimates, and preserve any approved geometry, material, UV, rig, or output decisions.
2. Keep a directly editable `.blend` source and use a versioned copy for requested revisions. Design materials for the actual vehicle surface and RTS viewing scale instead of adding undirected noise or detail.
3. Finish UVs, smoothing, custom normals, modifier evaluation, and the target triangulation before tangent-space baking. Keep the high and low surfaces, bake proxies, UVs, and normal basis aligned. Inspect padding, overlaps, seams, and projections before accepting the maps.
4. Use the repository's deterministic Blender 4.5.14 `--bake-atlas` pipeline for production atlases. Distinguish same-mesh material baking from high-to-low projection and preserve the editable source-to-atlas relationship.
5. Deliver `<Name>_Albedo.png`, `<Name>_MetallicGloss.png`, `<Name>_Normal.png`, and `<Name>_Emission.png`. MetallicGloss packs metallic into RGB and smoothness (`1 - roughness`) into A. Do not deliver the upstream default of separate metallic and roughness maps as though they were the Rogue Command texture set; do not pack AO into MetallicGloss.
6. Preserve the mesh `Color` attribute as RGBA vertex wear (R cavity AO, G edge wear, B thermal soot, A team mask). Texture AO and a painter's vertex-color export do not replace it.
7. Reopen the saved `.blend` when model changes are in scope. For image-only edits, keep the `.blend` and authoritative FBX unchanged. Validate the final maps and effective Unity importer settings separately; a successful bake does not prove in-game appearance.

## Scope boundaries

- Do not add generic confirmation or approval gates after the user has authorized the change and the existing contract is clear.
- Do not add upstream worklog templates or English-only policy to a repository task unless the user asks for them.
- Do not make changes outside the requested model, maps, or documentation. Report unrelated source defects without silently expanding the task.
- Use [Rogue Command PBR Texture Validation](../rogue-command-pbr-texture-validation/SKILL.md) for post-import checks, and distinguish static inspection, local Unity import/build, and in-game runtime evidence.
