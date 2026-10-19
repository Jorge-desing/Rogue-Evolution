# Rogue Command PBR Texture Workflow

This document defines the project-specific handoff from vehicle texturing to Unity. It supplements [3D Model Authoring](../../.agents/skills/rogue-command-3d-model-authoring/SKILL.md), [PBR Texture Authoring](../../.agents/skills/rogue-command-pbr-texture-authoring/SKILL.md), and [PBR Texture Validation](../../.agents/skills/rogue-command-pbr-texture-validation/SKILL.md). The shader, builder, and pinned Unity project remain the compatibility authority.

## Supported authoring choices

| Path | Role | Project decision |
|---|---|---|
| Blender 4.5.14 LTS with `tools/generate_unit_model.py` / `tools/RogueModelKit.py` | Deterministic model and PBR atlas generation | Production baseline; retain the editable `.blend` and use the existing bake flags. |
| ArmorPaint | Optional local painting and image-to-PBR assistance | Optional texture-only pass after UVs are final. Verify custom channel packing. Its neural nodes require separately downloaded model files; the manual recommends at least 6 GB VRAM. |
| Material Maker | Procedural material graph authoring | Optional source of texture images. Direct model painting is experimental; do not depend on its Unity material export for this project's custom shader. |
| `blendsmith` | Public agent skill reference | Do not copy or install as-is. It is GPL-3.0-only and targets Blender 4.4; preserve license boundaries and Rogue Command contracts. |
| `EL4CTEO/blender-skills` | Public agent skill reference | Do not install directly into this workflow because it targets Blender 5.2+, while the project pins Blender 4.5.14. |
| Adobe Substance 3D Sampler generative workflows | Optional ideation reference | Do not make beta/service-dependent generation a reproducible build requirement. |

These adaptations are project-local skills, not a vendored copy of external skill text. No third-party texturing application is installed or required by this workflow.

## Texture contract

`VehiclePrefabBuilder` resolves the atlas maps by exact unit name under the Unity author's model texture folder:

| File | Content | Import contract |
|---|---|---|
| `<Name>_Albedo.png` | Base color, camouflage, and paint | Default texture, sRGB on |
| `<Name>_MetallicGloss.png` | RGB = metallic; A = smoothness (`1 - roughness`) | Default texture, linear (`sRGBTexture = false`), alpha from input, alpha is not transparency |
| `<Name>_Normal.png` | Tangent-space normal | `TextureImporterType.NormalMap` |
| `<Name>_Emission.png` | Emissive areas | Default texture, sRGB on |

The custom shader samples only MetallicGloss.R for metallic and MetallicGloss.A for smoothness, scaled by `_GlossMapScale`. Keep G/B equal to R to match current Blender bakes. The shader does not consume an AO channel in this texture. Vertex-wear RGBA remains on the mesh `Color` attribute: R cavity AO, G edge wear, B thermal soot, A team mask. Do not merge these separate contracts.

The current shader keyword is `_METALLICSPECGLOSSMAP`; its source and `VehiclePrefabBuilder` must be consulted before changing material settings. With the map enabled, the builder sets `_GlossMapScale`, `_Metallic`, and `_Smoothness` to `1.0`. Preserve the input alpha and validate the effective Unity importer state after the builder rebuilds or reimports the texture; a stale `.meta` file is not proof of the final state.

## Recommended flow

1. **Freeze the model inputs.** Confirm the source `.blend`, finalized UVs, atlas resolution, texture names, and whether the Unity output needs `_Atlas.fbx`. Preserve sockets, hierarchy, vertex colors, and source files.
2. **Create the baseline maps in Blender.** Use the current deterministic `--bake-atlas` workflow. Keep generated intermediates in `artifacts/` until a specific texture asset is ready for integration. Do not change the generator CLI or AssetBundle manifest contract for a texture pass.
3. **Optional art pass.** If a painted or generated texture improves readability, send the finalized UV mesh to ArmorPaint or create procedural images in Material Maker. Export only texture images. Explicitly pack metallic to RGB and smoothness to A; do not trust a generic Unity preset to match the shader. Keep AO, height, and auxiliary maps separate unless an in-project shader is verified to consume them.
4. **Integrate the four named maps.** Copy only task-owned texture outputs into the Unity authoring project's model texture folder. Keep Blender's source FBX as the authoritative mesh. Never replace mesh vertex wear with texture AO or an external tool's vertex colors.
5. **Rebuild and inspect in Unity 2021.3.45f2.** Run the existing `VehiclePrefabBuilder`, then inspect the effective import settings, material texture references, shader keyword, packed MetallicGloss channels, normal response, emission, UV padding, and mesh vertex colors. The builder may repair import settings during its run.
6. **Check scale and evidence.** Review the unit at RTS camera scale and check atlas seams at close range. Record the result as static inspection or local Unity validation. A live-game appearance claim requires a separate Steam-launched runtime review.

## Acceptance checklist

- All four maps exist with exact suffixes and matching dimensions.
- UV islands have sufficient padding and no visible seams, missing regions, or accidental overlaps.
- Metallic is in `MetallicGloss.R`; smoothness (`1 - roughness`) is in `MetallicGloss.A`; the texture is linear and alpha is preserved.
- Albedo/Emission are sRGB and Normal is imported as a normal map.
- Prefab material uses the expected shader and map keyword, and the mesh retains its RGBA vertex colors and required sockets.
- Unit color, material separation, and emissive accents remain legible at RTS scale.
- Evidence level is stated accurately: static, local Unity, or in-game runtime.

## References

- [ArmorPaint manual](https://armorpaint.org/manual)
- [Material Maker](https://materialmaker.org/) and [Unity export documentation](https://rodzill4.github.io/material-maker/doc/export.html)
- [blendsmith](https://github.com/brianchenanimator/blendsmith)
- [EL4CTEO/blender-skills](https://github.com/EL4CTEO/blender-skills)
- [Adobe Substance 3D Sampler generative workflows](https://experienceleague.adobe.com/en/docs/substance-3d-sampler/using/features-and-workflows/generative-workflows)
