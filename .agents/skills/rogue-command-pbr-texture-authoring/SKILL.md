---
name: rogue-command-pbr-texture-authoring
description: "Author or revise PBR atlas textures for Rogue Command units while preserving the Blender, Unity importer, shader, and vertex-color contracts."
---

# Rogue Command PBR Texture Authoring

Use this skill when creating, editing, rebaking, or preparing PBR texture atlases for Rogue Command vehicles and structures.

Before authoring, read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `README.md`, `unity/RogueCommandAssetAuthoring/README.md`, [the PBR workflow](../../../docs/research/PBR-TEXTURE-WORKFLOW.md), [3D Model Authoring](../rogue-command-3d-model-authoring/SKILL.md), and [Unity Material Pipeline](../rogue-command-3d-model-authoring/references/unity-material-pipeline.md). Inspect the current working tree and preserve unrelated edits and assets.

## Supported Workflow

- Blender **4.5.14 LTS** and the repository's deterministic `bpy` pipeline are the reproducible production baseline. Start from the existing `.blend` and the generator's `--bake-atlas` path; use `--single-material` only when the intended export requires `<Name>_Atlas.fbx`. Read the current CLI help before selecting additional switches.
- ArmorPaint and Material Maker are optional local art passes after UVs are final. ArmorPaint's distributed Windows binary requires purchase access; stage an already accessible official archive with `tools/Setup-TextureAuthoringTools.ps1 -ArmorPaintArchivePath <zip>`. Material Maker 1.7 is staged with `tools/Setup-TextureAuthoringTools.ps1 -MaterialMaker`. Neither application is a build dependency.
- For ArmorPaint, read [the project ArmorPaint skill](../rogue-command-armorpaint-texture-authoring/SKILL.md); for Material Maker, read [the project Material Maker skill](../rogue-command-material-maker-texture-authoring/SKILL.md). Both produce image inputs only and defer all final channel, mesh, and Unity decisions to this skill.
- Use [the `blendsmith` Rogue Command adaptation](../blendsmith/SKILL.md) only as a supplementary editable-asset workflow. Its GPL-3.0-only upstream snapshot and license are retained under that skill's `references/upstream/` and root `LICENSE`; the local entrypoint adapts its Blender 4.4 assumptions and generic maps to this project's contracts.
- Use [the Blender 4.5 PBR adaptation](../rogue-command-blender-pbr-45/SKILL.md) for selected PBR and baking practices from EL4CTEO's Blender skills. Do not install or run the upstream Blender 5.2+ scripts unchanged; the local deterministic generator and this project's maps remain authoritative.
- Do not make remote generative services necessary for texture creation or acceptance. Generated images may be used as references, but production outputs must be reviewable and stored as project assets.
- For condensed handoff limits and official product documentation, read [the external-tool playbooks](references/external-tool-playbooks.md). These tools supply texture images; Blender remains the source of the production mesh and Unity builder remains responsible for importer settings and material wiring.

## Required Texture Set

Write maps to the Unity authoring project's existing model texture folder using the exact unit name and suffixes expected by `VehiclePrefabBuilder`:

| File | Content contract | Unity import |
|---|---|---|
| `<Name>_Albedo.png` | Base color / camouflage / authored paint | Default, sRGB on |
| `<Name>_MetallicGloss.png` | RGB = metallic; A = smoothness (`1 - roughness`) | Default, sRGB off, alpha from input, transparency processing off |
| `<Name>_Normal.png` | Tangent-space normal map consistent with the Blender bake | Normal Map import type |
| `<Name>_Emission.png` | Emissive color for authored lights and energy details | Default, sRGB on |

The current `RogueCommand/VehiclePBR_VertexWear` shader samples metallic from `MetallicGloss.R` and smoothness from `.A * _GlossMapScale`. Keep G/B equal to R for compatibility with the deterministic bake, even though this shader does not read those channels. AO from external tools is not consumed by this map contract; do not silently pack it into MetallicGloss.

## Authoring Steps

1. Confirm the source model, final UV layout, atlas resolution, target texture folder, and current texture set. Preserve a recoverable `.blend` source and work on a copy when changing an existing model.
2. Produce the deterministic Blender atlas first. Confirm the output files, dimensions, naming, and that the bake completed without warnings that invalidate a map.
3. If an art pass is useful, use the corresponding ArmorPaint or Material Maker skill and keep the session on a disposable texture-only copy. Export **textures only**. Preserve the Blender-produced FBX as the authoritative Unity mesh; re-exporting a mesh from an external painter requires a separate check of hierarchy, sockets, UVs, LODs, and vertex colors.
4. Pack the custom metallic/smoothness output as RGB metallic and A smoothness. Set or verify metallic in R and alpha as `1 - roughness`; do not assume an application's default Unity preset uses this project's channel layout. Author emission deliberately. The tool's AO, height, or auxiliary maps can remain separate unless a verified project shader consumes them.
5. Put the four named PNGs in the existing Unity authoring texture folder. Keep generated intermediates under `artifacts/` until they are ready to replace a specific task-owned asset.
6. Rebuild the relevant visual prefab with the existing Unity builder and continue with the AssetBundle authoring skill when a bundle is in scope. Verify effective importer settings after the builder/reimport; do not rely only on `.meta` text.

## Preservation Rules

- Never overwrite the source `.blend`, change generator CLI behavior, alter the AssetBundle contract, or modify unrelated model files as part of a texture-only task.
- Preserve the mesh `Color` vertex attribute RGBA: R cavity AO, G edge wear, B thermal soot, A team mask. These values are mesh vertex colors, not texture-map channels. Do not replace them with ArmorPaint AO or vertex-color exports.
- Preserve canonical unprefixed sockets and use the Blender FBX as the model imported by the builder. Do not add socket duplicates in Unity.
- Keep the Unity authoring project version and settings intact. Do not change the user's separate `Rogue Evolution` project.

## Completion Report

List the model and maps changed, the authoring application/version, channel packing, output paths, and checks performed. Label evidence as static inspection, local Unity import/build, or in-game runtime. A successful bake or bundle build does not prove the final in-game appearance.
