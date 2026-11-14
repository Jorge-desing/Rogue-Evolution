# External PBR Texture Tool Playbooks

Use this reference when an atlas uses ArmorPaint, Material Maker, or an adapted external Blender workflow. The production FBX still comes from the Blender source, and the project-specific contract in the parent skill remains authoritative.

## Shared setup and handoff

For finish variation, controlled microdetail and optional manual refinements, use [compatible texture recipes](compatible-texture-recipes.md). It identifies the current export and validation path for each technique; application feature availability alone does not establish integration.

- Keep the finalized `.blend`, source FBX, UV layout, canonical sockets, hierarchy, and RGBA mesh `Color` attribute unchanged.
- Work from a disposable texture-tool copy under `artifacts/texture-work/<Name>/<tool>/`. Never save a tool-exported mesh over the Blender source or Unity model.
- Blender 4.5.14 `--bake-atlas` remains the first reproducible geometry/UV output. Material Maker is the required image-authoring pass, not a replacement for the generator or a build dependency.
- Material Maker 1.7 is the required local texture application; stage it with `tools/Setup-TextureAuthoringTools.ps1 -MaterialMaker` and use `tools/Run-MaterialMakerBake.ps1` for the automated export. ArmorPaint can only be staged from an official archive the user already has access to; its distributed desktop binary is offered through purchase access.
- Copy only the four final PNGs into the task-owned Unity texture folder. Do not import a tool-created `.mat`; `VehiclePrefabBuilder` and the project shader define material wiring.

## ArmorPaint

- Import the finalized UV mesh only after the UV layout is frozen. The manual supports `.fbx` and `.blend` input and custom presets with per-channel swizzling.
- Use the `Unity` preset only as a starting point: it packs metallic, occlusion, and smoothness. Configure a custom preset that writes the project's `<Name>_MetallicGloss.png` as RGB = metallic and A = `1 - roughness` (smoothness). Do not store occlusion in that alpha or rely on a generic preset's channel layout.
- Export `<Name>_Albedo.png`, `<Name>_MetallicGloss.png`, `<Name>_Normal.png`, and `<Name>_Emission.png`. Author metallic and emission deliberately; `Image to PBR` does not create this project's complete set by itself.
- Verify the local ArmorPaint version and a disposable channel export before optional painting; this manual path has no repository painting automation. Neural generation is outside the integrated recipes.

## Material Maker 1.7

- Use Material Maker for procedural PBR graphs or its paint project. The paint project accepts OBJ, GLB, GLTF, and FBX, and requires UV coordinates plus a single submesh. Its procedural graph route does not require changing the authoritative FBX.
- If the source FBX has multiple submeshes, do not alter the production mesh to satisfy the paint-project limit. Use the procedural texture route or a temporary single-submesh texture-only copy and discard that copy after image export.
- Export PNG images only. Ignore engine `.mat` output and any mesh export. Retain the graph/project file as source art only when it is needed to reproduce the texture.
- Material Maker represents Metallic and Roughness separately. Use the existing scalar export and packer to put metallic into R/G/B and inverted roughness into A for smoothness. If that pass fails, preserve the Blender baseline and report the failure; a baseline bake does not replace the required Material Maker pass.

## Adapted Blender agent workflows

- `blendsmith` is available as a local Rogue Command adaptation at [`.agents/skills/blendsmith/SKILL.md`](../../blendsmith/SKILL.md). Its GPL-3.0-only upstream snapshot and license are retained in that skill. The active adapter replaces Blender 4.4 and generic separate Metallic/Roughness map assumptions with Blender 4.5.14, the local four-map set, MetallicGloss R/A packing, and Rogue Command mesh/socket/vertex-color rules.
- The PBR material and baking ideas from [EL4CTEO/blender-skills](https://github.com/EL4CTEO/blender-skills) are adapted in [Rogue Command Blender PBR 4.5](../../rogue-command-blender-pbr-45/SKILL.md). The upstream repository targets Blender 5.2+; its scripts and ORM output are not installed. This project adaptation keeps the existing `--bake-atlas` generator as the production route and uses only version-checked concepts such as color-space separation, projection checks, padding, and tangent-basis consistency.
- Use [Rogue Command ArmorPaint Texture Authoring](../../rogue-command-armorpaint-texture-authoring/SKILL.md) or [Rogue Command Material Maker Texture Authoring](../../rogue-command-material-maker-texture-authoring/SKILL.md) for operational tool steps. The shared checks below summarize their common handoff; the main PBR skill remains the contract authority.

## Final validation

Run the parent PBR validation skill after the tool pass. In Unity, rebuild through `VehiclePrefabBuilder` and verify effective importer settings, alpha retention, texture bindings, shader keyword/properties, and the original mesh's vertex-color attribute. Tool viewport appearance alone is not Unity or in-game evidence.

## Official references

- [ArmorPaint manual: import, custom export presets, and CLI](https://armorpaint.org/manual)
- [ArmorPaint download and distribution](https://armorpaint.org/download)
- [Material Maker 1.7 release](https://github.com/RodZill4/material-maker/releases/tag/1.7)
- [Material Maker paint-project mesh requirements](https://rodzill4.github.io/material-maker/doc/user_interface_paint_panel.html)
- [Material Maker image and Unity export behavior](https://rodzill4.github.io/material-maker/doc/export.html)
- [blendsmith upstream skill snapshot](../../blendsmith/references/upstream/SKILL.md) (GPL-3.0-only; adapted locally)
- [EL4CTEO Blender skills](https://github.com/EL4CTEO/blender-skills) (Blender 5.2+ upstream; selected PBR/bake guidance adapted locally)
