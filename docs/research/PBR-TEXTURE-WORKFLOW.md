# Rogue Command PBR Texture Workflow

This document defines the project-specific handoff from vehicle texturing to Unity. It supplements [3D Model Authoring](../../.agents/skills/rogue-command-3d-model-authoring/SKILL.md), [PBR Texture Authoring](../../.agents/skills/rogue-command-pbr-texture-authoring/SKILL.md), and [PBR Texture Validation](../../.agents/skills/rogue-command-pbr-texture-validation/SKILL.md). The shader, builder, and pinned Unity project remain the compatibility authority.

## Supported authoring choices

| Path | Role | Project decision |
|---|---|---|
| Blender 4.5.14 LTS with `tools/generate_unit_model.py` / `tools/RogueModelKit.py` | Deterministic model and PBR atlas generation | Production baseline; retain the editable `.blend` and use the existing bake flags. |
| ArmorPaint | Optional local painting and image-to-PBR assistance | Implemented as [a project skill](../../.agents/skills/rogue-command-armorpaint-texture-authoring/SKILL.md). The portable app can be staged only from an official archive the user already has access to; its neural models are optional. |
| Material Maker 1.7 | Procedural material graphs and 3D texture painting | Implemented as [a project skill](../../.agents/skills/rogue-command-material-maker-texture-authoring/SKILL.md). The app is already staged under ignored `.tools/`; its paint project requires UVs and a single-submesh model. |
| `blendsmith` | Public agent skill reference | Implemented as a [Rogue Command adaptation](../../.agents/skills/blendsmith/SKILL.md). The GPL-3.0-only upstream skill, templates, references, license, and notice are retained under that skill; the active entrypoint adapts its Blender 4.4 baseline and generic map assumptions to this project. |
| `EL4CTEO/blender-skills` | Public Blender PBR and baking skills | Implemented as a [selective Blender 4.5.14 adaptation](../../.agents/skills/rogue-command-blender-pbr-45/SKILL.md). No unadapted Blender 5.2+ scripts are installed; ORM packing and the unsupported 5.2 `Thin Wall` socket are excluded. |
| Adobe Substance 3D Sampler generative workflows | Optional ideation reference | Do not make beta/service-dependent generation a reproducible build requirement. |

## Repository Material Maker matrix

The canonical configuration is `tools/material-maker/rogue-command-material-maker.json`; run `python tools/validate_material_maker_config.py` before changing it. It configures four image-authoring routes while keeping Blender authoritative for production geometry:

| Workflow | Use | Authority | Downstream |
|---|---|---|---|
| `urp` | Game-bound vehicle maps | Blender FBX/UVs/vertex wear | Unity URP `RogueCommand/VehiclePBR_VertexWear` |
| `blender` | Blender material/preview inspection | Blender `.blend` and original FBX | Blender 4.5.14 image/material preview |
| `three_d` | Disposable UV-frozen paint copy | Blender production mesh | Material Maker images only; never its mesh export |
| `procedural` | Graph-only texture generation | Blender UV contract | Same four-map packer and validation |

Use `tools/Run-MaterialMakerBake.ps1 -Workflow <name>`. The runner clears stale raw/final outputs before every export, writes a workflow manifest beside the maps, passes the `.ptex` and 3D-paint workspace to `audit_material_maker_output.py`, uses the same four-map contract, and never imports Material Maker-generated Unity `.mat` files. The audit report exposes machine-readable URP map metrics (dimensions, MetallicGloss RGB consistency and smoothness alpha, tangent-normal length/Z health, emission coverage), procedural graph metrics (`colorize_metallic`, `edge_detect_emission`, `colorize_emission`, and the `micro_perlin` multi-scale detail stack), and 3D-paint preservation flags (`requiresUv`, `uvFrozen`, `singleSubmeshForPaint`, `paintProxyPresent`, `productionMeshUntouched`, and source hash). The `tactical-hd-v2` graph drives high-frequency micrograin into both tangent normal and roughness so 2048/4096 maps retain material breakup without flattening panel seams. Keep the report under `artifacts/`; it is authoring/local evidence, not runtime render evidence.

## Unity and AssetBundle diagnostic report

After a Unity batch build, generate a combined report instead of relying on the process exit code alone:

```bash
PYTHONPATH=".venv/Lib/site-packages" .venv/Scripts/python.exe \
  tools/audit_unity_build.py \
  --log artifacts/unity-visual-build-final.log \
  --bundle content/packs/sample-native-expansion/bundles/sample-native-expansion.bundle \
  --output artifacts/unity-visual-build-final-diagnostic.json \
  --strict
```

The report separates three evidence layers: Unity batch-log success, offline AssetBundle serialization, and Steam runtime (which remains unknown until a Steam-launched match). It records the Unity version, success/fatal/licensing diagnostics, build duration, bundle size and SHA-256, serialized object totals, shader resolution, texture dimensions/color space, material and URP indicators, mesh/renderer/Animator/animation/particle/LOD counts, and the legacy bundle quality gate. Licensing-client errors are retained as diagnostics but do not fail a build when the builder emits an explicit success marker; missing shutdown markers remain visible through `batchmode_completed`. UnityPy shader names may be unresolved on some serialized shader variants, so `shader_names_unresolved_count` must not be reported as proof that a shader rendered in-game.

Run the same audit for Tactical Operations or other packs by repeating `--bundle`. Keep JSON reports in ignored `artifacts/`; they are local Unity/AssetBundle evidence, not gameplay or render proof.

Material Maker 1.7 and its release ZIP are staged under ignored `.tools/`; the ZIP SHA-256 matches the documented pin `DEB4416BC939861D48097A866A8B2BF0363C29FF64874F2E04478658FF900808`. ArmorPaint's binary is not staged because this workspace has no user-provided official archive. The blendsmith source snapshot is included under its GPL-3.0-only license; EL4CTEO is adapted as new project-specific instructions with its MIT license retained for attribution, and none of its scripts are copied.


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
3. **Material Maker texture pass.** After the UVs are final, follow the Material Maker project skill for every mod texture pass; use its procedural graph or texture-only paint path with a disposable copy when the production FBX has multiple submeshes. Use `-SizePower 11` for the validated 2048×2048 hero path and keep the `tactical-hd-v2` multi-scale detail graph enabled. `-SizePower 12` is accepted only when the quality report confirms an actual 4096 output; the current renderer may cap the device at 2048 and strict mode rejects that mismatch. Export images only, never replace the authoritative Blender mesh. The automated runner creates the export target, waits for the rendering device, and packs metallic to RGB and smoothness to A; do not trust a generic Unity preset or export default. ArmorPaint remains an optional supplemental art tool only.
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
- [ArmorPaint official download page](https://armorpaint.org/download)
- [Material Maker](https://materialmaker.org/) and [Unity export documentation](https://rodzill4.github.io/material-maker/doc/export.html)
- [Material Maker 1.7 release](https://github.com/RodZill4/material-maker/releases/tag/1.7) and [3D paint project requirements](https://rodzill4.github.io/material-maker/doc/user_interface_paint_panel.html)
- [blendsmith](https://github.com/brianchenanimator/blendsmith)
- [EL4CTEO/blender-skills](https://github.com/EL4CTEO/blender-skills)
- [blendsmith source commit used for the local adaptation](https://github.com/brianchenanimator/blendsmith/tree/b46f037aa8c9596fc40c2eafb2c782ed9c0d63ac)
- [EL4CTEO/blender-skills source commit reviewed for the local adaptation](https://github.com/EL4CTEO/blender-skills/tree/71d031dab8bc6729504d11a78cbe91945401045f)
- [Adobe Substance 3D Sampler generative workflows](https://experienceleague.adobe.com/en/docs/substance-3d-sampler/using/features-and-workflows/generative-workflows)
