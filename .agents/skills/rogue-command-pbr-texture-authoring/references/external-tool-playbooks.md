# External PBR Texture Tool Playbooks

Use this reference only when an atlas uses ArmorPaint or Material Maker. The production FBX still comes from the Blender source, and the project-specific contract in the parent skill remains authoritative.

## Shared setup and handoff

- Keep the finalized `.blend`, source FBX, UV layout, canonical sockets, hierarchy, and RGBA mesh `Color` attribute unchanged.
- Work from a disposable texture-tool copy under `artifacts/texture-work/<Name>/<tool>/`. Never save a tool-exported mesh over the Blender source or Unity model.
- Blender 4.5.14 `--bake-atlas` remains the first reproducible output. External work is an optional art pass, not a replacement for the generator or a build dependency.
- Stage optional applications with `tools/Setup-TextureAuthoringTools.ps1`. The script verifies Material Maker 1.7 against the pinned release SHA-256. ArmorPaint can only be staged from an official archive the user already has access to; its distributed desktop binary is offered through purchase access.
- Copy only the four final PNGs into the task-owned Unity texture folder. Do not import a tool-created `.mat`; `VehiclePrefabBuilder` and the project shader define material wiring.

## ArmorPaint

- Import the finalized UV mesh only after the UV layout is frozen. The manual supports `.fbx` and `.blend` input and custom presets with per-channel swizzling.
- Use the `Unity` preset only as a starting point: it packs metallic, occlusion, and smoothness. Configure a custom preset that writes the project's `<Name>_MetallicGloss.png` as RGB = metallic and A = `1 - roughness` (smoothness). Do not store occlusion in that alpha or rely on a generic preset's channel layout.
- Export `<Name>_Albedo.png`, `<Name>_MetallicGloss.png`, `<Name>_Normal.png`, and `<Name>_Emission.png`. Author metallic and emission deliberately; `Image to PBR` does not create this project's complete set by itself.
- ArmorPaint neural models run locally after the user downloads them. They are optional; do not require remote generation or these model files for an atlas to pass.

## Material Maker 1.7

- Use Material Maker for procedural PBR graphs or its paint project. The paint project accepts OBJ, GLB, GLTF, and FBX, and requires UV coordinates plus a single submesh. Its procedural graph route does not require changing the authoritative FBX.
- If the source FBX has multiple submeshes, do not alter the production mesh to satisfy the paint-project limit. Use the procedural texture route or a temporary single-submesh texture-only copy and discard that copy after image export.
- Export PNG images only. Ignore engine `.mat` output and any mesh export. Retain the graph/project file as source art only when it is needed to reproduce the texture.
- Material Maker represents Metallic and Roughness separately. For the project map, pack metallic into R/G/B and invert roughness into A for smoothness. If channel packing is uncertain, return to Blender's deterministic bake rather than importing a generic Unity material.

## Final validation

Run the parent PBR validation skill after the tool pass. In Unity, rebuild through `VehiclePrefabBuilder` and verify effective importer settings, alpha retention, texture bindings, shader keyword/properties, and the original mesh's vertex-color attribute. Tool viewport appearance alone is not Unity or in-game evidence.

## Official references

- [ArmorPaint manual: import, custom export presets, and CLI](https://armorpaint.org/manual)
- [ArmorPaint download and distribution](https://armorpaint.org/download)
- [Material Maker 1.7 release](https://github.com/RodZill4/material-maker/releases/tag/1.7)
- [Material Maker paint-project mesh requirements](https://rodzill4.github.io/material-maker/doc/user_interface_paint_panel.html)
- [Material Maker image and Unity export behavior](https://rodzill4.github.io/material-maker/doc/export.html)
