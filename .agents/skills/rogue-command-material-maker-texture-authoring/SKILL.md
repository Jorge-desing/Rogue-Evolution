---
name: rogue-command-material-maker-texture-authoring
description: "Use Material Maker 1.7 for all procedural or painted Rogue Command texture images (required standard tool), adapting its single-submesh paint limit and Unity exports to the project's PBR contract."
---

# Rogue Command Material Maker Texture Authoring

Use this skill when Material Maker is selected for procedural PBR graphs or a supported 3D paint pass. Read [Rogue Command PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md) first; it remains authoritative for the source mesh, channel packing, and Unity import.

## Setup and source protection

- The optional Material Maker 1.7 copy is staged under ignored `.tools/MaterialMaker-1.7/`. To prepare it in another checkout, use `tools/Setup-TextureAuthoringTools.ps1 -MaterialMaker`; the script verifies the pinned release archive SHA-256 and extracts safely.
- Keep the finalized `.blend`, production FBX, UVs, sockets, hierarchy, LODs, and RGBA vertex wear unchanged. Work from a disposable copy under `artifacts/texture-work/<Name>/MaterialMaker/`.
- Material Maker's paint project accepts OBJ/GLB/GLTF/FBX with UVs and a single submesh. If the production FBX has multiple submeshes, use procedural graphs or make a temporary texture-only copy; never change the Unity production mesh to satisfy the paint UI.

## Author and export

1. Freeze UVs and decide whether the task uses procedural graph generation or direct model painting. Confirm the texture resolution and export paths before saving anything to the authoring project.
2. Build or edit a material graph for the observed finish and feature scale. Avoid generic noise that obscures panel layout or material separation at the game's RTS camera distance.
3. Export image files as PNG to the temporary workspace. Do not use generated Unity `.mat` files, shader templates, or mesh exports as Rogue Command assets.
4. Deliver the exact set `<Name>_Albedo.png`, `<Name>_MetallicGloss.png`, `<Name>_Normal.png`, and `<Name>_Emission.png`. Pack metallic in R/G/B and smoothness (`1 - roughness`) in A. Do not pack AO into MetallicGloss; the current shader reads only R and A.
5. Keep auxiliary AO, height, and curvature maps separate unless the in-project shader is changed and verified. If an output target cannot produce the required channel layout reliably, return to Blender's deterministic `--bake-atlas` path or use a separately verified image-packing step.
6. Preserve any Material Maker project/graph needed to reproduce the images under `artifacts/`; it is source art, not a replacement for the Blender model or Unity material builder.

## Headless export (CLI)

Material Maker supports batch export without the GUI:

```
material_maker --export-material --target <engine> -o <output_dir> <input.ptex> --quit
```

- Run with the working directory set to the Material Maker install dir (`.tools/MaterialMaker-1.7/material_maker_1_7_windows/`); relative output paths are resolved against that dir, so prefer absolute `-o` paths.
- `<engine>` is one of `Blender`, `Godot`, `Unity`, `Unreal`. For this project use **`Blender`** — it emits PNGs only (albedo/normal/metallic-gloss/etc.), with no engine-specific `.mat`/`.tres`/`.mm2ue` files to discard. `Unity` target also works but writes a `.mat` we do not use.
- Template strategy: copy an existing example `.ptex` (e.g. `examples/bricks.ptex`) into `artifacts/mm-templates/<Name>.ptex`, edit the graph in the GUI or by JSON, then export. Keep the edited `.ptex` under `artifacts/` as reproducible source art.
- First-time setup is fully automatable: `tools/Run-MaterialMakerBake.ps1` creates the `%APPDATA%/material_maker_2/export_targets/` directory and writes a temporary project export target. Do not create `export_targets` as a regular file.
- Material Maker 1.7 release issue #1506 has a startup race in command-line rendering-device initialization. The runner writes a temporary `override.cfg` beside the ignored executable and autoloads `tools/MaterialMakerWaitDevice.gd`; it waits for `/root/mm_renderer`'s `RenderingDevice` before export, then restores any pre-existing override and export target. This keeps the bake headless and does not require a GUI pass.
- The runner invokes Material Maker with `Start-Process -Wait`, exports scalar PNGs through the temporary `RogueCommand` target, then runs `tools/pack_material_maker_pbr.py` to produce the exact four-map contract. The packer writes metallic to RGB and `1 - roughness` to alpha in `<Name>_MetallicGloss.png`.
- Verify the output directory contains the expected PNGs and dimensions before copying into the Unity texture folder; a silent empty export is possible if the target path is wrong.

## Validation handoff

Run [Rogue Command PBR Texture Validation](../rogue-command-pbr-texture-validation/SKILL.md). Inspect image dimensions, UV coverage, seams, normal convention, and the MetallicGloss alpha channel, then verify effective Unity importer settings after `VehiclePrefabBuilder` reimports the maps. Confirm the original imported mesh still carries RGBA vertex wear and that the prefab uses the project's actual shader and keyword.

## References

- [Material Maker official site](https://materialmaker.org/)
- [Material Maker 1.7 Windows release](https://github.com/RodZill4/material-maker/releases/tag/1.7)
- [3D paint project requirements](https://rodzill4.github.io/material-maker/doc/user_interface_paint_panel.html)
- [Texture and material export documentation](https://rodzill4.github.io/material-maker/doc/export.html)
