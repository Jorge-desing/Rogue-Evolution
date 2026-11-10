---
name: rogue-command-pbr-texture-validation
description: "Audit Rogue Command PBR texture maps, Unity importer settings, vertex colors, and atlas material wiring against the project's shader contract."
---

# Rogue Command PBR Texture Validation

Use this skill to review texture atlases before or after importing them into the Rogue Command Unity authoring project.

When maps came from ArmorPaint or Material Maker, also read the [external-tool playbooks](../rogue-command-pbr-texture-authoring/references/external-tool-playbooks.md) for the applicable export limits. Treat tool previews and generated Unity `.mat` files as source-art evidence only; validate the PNG channels and the project's actual shader/importer after rebuild.

For tool-specific source review, also read [ArmorPaint Texture Authoring](../rogue-command-armorpaint-texture-authoring/SKILL.md) or [Material Maker Texture Authoring](../rogue-command-material-maker-texture-authoring/SKILL.md). For maps produced with Blender, use the [Blender 4.5 PBR adaptation](../rogue-command-blender-pbr-45/SKILL.md) alongside the required project bake workflow.

Before reviewing, read `AGENTS.md`, `docs/research/AGENT-HANDOFF.md`, `unity/RogueCommandAssetAuthoring/README.md`, [the PBR workflow](../../../docs/research/PBR-TEXTURE-WORKFLOW.md), [3D Model Authoring](../rogue-command-3d-model-authoring/SKILL.md), [Unity Material Pipeline](../rogue-command-3d-model-authoring/references/unity-material-pipeline.md), and [AssetBundle Authoring](../rogue-command-assetbundle-authoring/SKILL.md) if bundle changes are part of the request. Keep unrelated working-tree changes intact.

## Validation Checklist

### 1. Source maps

- Confirm the model has the expected `<Name>_Albedo.png`, `<Name>_MetallicGloss.png`, `<Name>_Normal.png`, and `<Name>_Emission.png` files in the texture directory selected by `VehiclePrefabBuilder`.
- Confirm all maps use the same atlas dimensions and match the finalized mesh UV layout. Inspect island padding, seams, empty borders, flipped or missing regions, and legibility at RTS camera scale.
- Inspect `MetallicGloss` as linear data: R contains metallic, A contains smoothness (`1 - roughness`), G/B match R for consistency with the repository bake. Verify that the alpha channel exists and contains the intended data; a PNG preview that ignores alpha is insufficient.
- Confirm Albedo and Emission are authored as color data and the Normal map is a tangent-space normal map in the expected convention. Do not assume an external tool's defaults match Blender or Unity.
- Check emissive regions separately from base color. AO, roughness, height, and auxiliary maps from external tools must not be mistaken for channels consumed by the Rogue Command shader.

### 2. Mesh preservation

- Import the authoritative FBX produced from the Blender source, not a texture-tool mesh export, unless the request explicitly includes a model migration.
- Confirm the imported Unity mesh retains the four-channel vertex color attribute used by `VehiclePBR_VertexWear`: R cavity AO, G edge wear, B thermal soot, A team mask.
- Confirm canonical sockets, hierarchy, UVs, and LOD selection are intact if the FBX changed. Avoid creating duplicate sockets under the prefab visual root.

### 3. Effective Unity import and material

After the existing `VehiclePrefabBuilder` runs and Unity reimports the textures, inspect the effective `TextureImporter` state:

| Map | Required effective settings |
|---|---|
| Albedo | `TextureImporterType.Default`, `sRGBTexture = true` |
| MetallicGloss | `TextureImporterType.Default`, `sRGBTexture = false`, `alphaSource = FromInput`, `alphaIsTransparency = false` |
| Normal | `TextureImporterType.NormalMap` |
| Emission | `TextureImporterType.Default`, `sRGBTexture = true` |

Then confirm the prefab material references the expected maps and the self-contained `RogueCommand/VehiclePBR_VertexWear` shader. Its current map variant uses `_METALLICSPECGLOSSMAP`, reads Metallic from R and smoothness from A multiplied by `_GlossMapScale`, and samples the mesh's `input.color` for vertex wear. The builder sets `_GlossMapScale`, `_Metallic`, and `_Smoothness` to `1.0` when the map is present. Verify the current shader and builder source if either changes; do not copy a keyword or property name from a generic preset.

The builder may correct stale `.meta` settings during a rebuild. Report the post-reimport importer state, not only the serialized metadata file. Check the imported/compressed result and alpha preservation after the builder has run.

### 4. Shader compile and normal-map sanity

- Treat shader source inspection as static evidence only. During the pinned Unity Editor build, check the final shader-compiler/import output and the reopened AssetBundle's shader/material dependencies. A transient `Shader Unsupported ... All subshaders removed` message during initial asset import is not by itself proof that the final bundle shader failed; require successful target-variant compilation and a bundle reopen check, and report any remaining compiler warnings.
- Check normal-map channel statistics and view the map as data, but do not reject a near-neutral tangent-space normal solely because it has little pixel variation: simple faceted geometry may carry its shape in mesh normals. If a map is unexpectedly flat, compare it to the Blender bake input and atlas UVs, confirm `_NORMALMAP` and `_BumpMap` are actually bound, and inspect the imported model/material in Unity before changing or rebaking production maps.
- If a material must be rebound from an unsupported shader, verify every available map is copied to the replacement shader's matching property and its matching keyword is enabled. Confirm property compatibility from the destination shader; texture presence or keyword flags alone do not prove correct rendering.

### 5. Visual evidence and handoff

- Record whether each check is static source/map inspection or local Unity editor/import validation. A local build or bundle inspection is not in-game evidence.
- A claim about appearance in a live match requires a supervised, Steam-launched runtime review. Do not start or alter the live game as a side effect of a texture audit.
- If an issue is found, identify the exact map, channel, UV region, importer setting, mesh attribute, or material binding. Route map authoring to `rogue-command-pbr-texture-authoring`, model/socket changes to `rogue-command-3d-model-authoring`, and bundle construction to `rogue-command-assetbundle-authoring`.

## Acceptance Criteria

A texture set passes the local authoring contract when all four files have expected names and compatible dimensions, metallic and smoothness occupy the expected channels, the Unity importer preserves linear data and alpha, the prefab material binds the maps to the intended shader, and the imported mesh retains RGBA vertex colors. Mark UV seam quality and final color appearance as visually reviewed only when inspected in Unity; mark in-game appearance only after an actual runtime check.

## Canonical Module Import Checks

- `CanonicalVisualModuleBuilder` checks all four maps are 2048x2048, verifies effective importer color-space/alpha/normal settings after reimport, builds the `RogueCommand/VehiclePBR_VertexWear` material variant, and confirms imported module meshes retain one RGBA vertex color per vertex.
- It also rejects scripts/gameplay components in module prefabs and reopens the AssetBundles to verify their serialized `assetName` keys. Run `tools/validate_content_bundle_references.py` on each built pack so every `visualVariants[].modules[].assetName` resolves in the unit's declared bundle.
- Rebuild through `tools/Build-CanonicalVisualModules.ps1` before import/reference acceptance. Run the reference validator against `artifacts/canonical-two-level/validation-packs/`; versioned bundles under `content/packs/*/bundles/` may be earlier snapshots.
- These are local Unity/import and serialized-bundle checks. A contact sheet or passing bundle audit does not establish that a live unit selected the expected variant or rendered it in a Steam match.
