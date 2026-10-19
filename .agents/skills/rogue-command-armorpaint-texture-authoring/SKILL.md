---
name: rogue-command-armorpaint-texture-authoring
description: "Use ArmorPaint for an optional local painting pass on finalized Rogue Command UVs, exporting only maps that match the project's PBR and Unity channel contract."
---

# Rogue Command ArmorPaint Texture Authoring

Use this skill when ArmorPaint is selected for painting or texture refinement. Read [Rogue Command PBR Texture Authoring](../rogue-command-pbr-texture-authoring/SKILL.md) first; that skill owns the authoritative mesh, output names, channel layout, and Unity handoff.

## Setup and source protection

- Use only a locally accessible official ArmorPaint Windows archive. The project setup script stages that archive under ignored `.tools/` and verifies safe extraction: `tools/Setup-TextureAuthoringTools.ps1 -ArmorPaintArchivePath <official-zip>`.
- Do not fetch or bypass purchase access to obtain the portable binary. If the archive is unavailable, continue with Blender 4.5.14 or Material Maker.
- Start from finalized UVs and an exported copy of the Blender mesh under `artifacts/texture-work/<Name>/ArmorPaint/`. Preserve the source `.blend`, production FBX, canonical sockets, hierarchy, LODs, and mesh `Color` RGBA vertex-wear attribute.
- Do not edit or re-export the Unity mesh from ArmorPaint. Its mesh export is for separate, explicitly authorized model migrations only.

## Painting and export

1. Import the finalized FBX or blend copy and check UV orientation, island alignment, seams, and model scale before painting. Do not unwrap, merge, decimate, recenter, or otherwise change production geometry in this pass.
2. Set the agreed atlas resolution and enough padding to keep islands separated after mipmapping and compression. Paint base color, surface response, and emissive details deliberately at the intended RTS viewing scale.
3. Create or select a custom texture preset. The generic Unity preset packs metallic, occlusion, and smoothness; it does not match this project's shader contract.
4. Export the required files only: `<Name>_Albedo.png`, `<Name>_MetallicGloss.png`, `<Name>_Normal.png`, and `<Name>_Emission.png`. In MetallicGloss, set R/G/B to metallic and A to smoothness (`1 - roughness`). Do not pack AO in this texture; the shader does not consume AO there.
5. Use ArmorPaint's texture export padding and inspect the alpha channel independently of the RGB preview. Do not infer correct packing from the viewport or preset name. Keep `.arm` work files and intermediates under `artifacts/` when they are needed to reproduce the pass.
6. Do not make neural nodes or downloaded AI models a production dependency. If used for ideation, review and author the final maps locally; the project workflow must remain reproducible without those models or remote services.

## Validation handoff

Send only the four image files into the task-owned Unity texture folder, then run [Rogue Command PBR Texture Validation](../rogue-command-pbr-texture-validation/SKILL.md). Confirm matching dimensions, metallic in R, smoothness in A, alpha retention, linear MetallicGloss import, the existing builder's material wiring, and unchanged mesh vertex colors. Report ArmorPaint preview results as source-art review; only Unity import or a supervised game run supports the corresponding stronger visual claim.

## References

- [Official ArmorPaint manual](https://armorpaint.org/manual)
- [Official ArmorPaint download page](https://armorpaint.org/download)
