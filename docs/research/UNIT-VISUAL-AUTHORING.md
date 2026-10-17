# Rogue Command unit visuals

**Updated:** 2026-10-17

**Evidence:** Blender authoring build, Unity Editor bundle build, and offline bundle-reference validation. No match was launched for this visual update.

## Current models

| Unit ID | Native template and visual root | Editable Blender source | Builder | Unity FBX | Source parts → FBX batches | Blender bounds (W × L × H) |
|---|---|---|---|---|---:|---:|
| `sample-native:scout-tank` | `PCXBaseTank` / `Vehicle` | `content/source-art/sandlance-scout.blend` | `tools/Build-SampleScoutModel.py` | `SandlanceScout.fbx` | 78 → 7 | 2.17 × 2.52 × 1.57 m |
| `sample-native:skirmish-bike` | `BombBike` / `GameObject/BombBike` | `content/source-art/skirmish-strike-bike.blend` | `tools/Build-SkirmishStrikeBikeModel.py` | `SkirmishStrikeBike.fbx` | 59 → 7 | 1.20 × 2.20 × 1.37 m |
| `sample-native:heavy-artillery` | `ArtilleryTank` / `Vehicle` | `content/source-art/colossus-siege.blend` | `tools/Build-ColossusSiegeModel.py` | `ColossusSiegePlatform.fbx` | 104 → 8 | 2.58 × 4.74 × 1.86 m |

All Blender sources keep individually editable parts. Export builders duplicate and merge those parts by material so each FBX uses seven or eight mesh batches. Blender's local quality check confirmed ground contact and assigned materials for all three models. The bike was compressed along its forward axis to fit the approximate 2.20 m native length. The Colossus retains a long siege cannon that extends beyond the approximate 3.60 m `ArtilleryTank` length; verify spacing and clipping in a Steam-launched match before calling it runtime-ready.

## ImageGen card sprites

The Unity visual bundle includes three tracked ImageGen-authored PNGs. Each final Blender preview was used as the image reference. Unity imports each as a single Sprite with Point filtering, no mipmaps, Clamp wrap mode, Uncompressed texture data, and a 128 × 128 maximum size. The scaffolder contains no procedural card drawing code and fails if a source PNG is missing or does not import as a 128 × 128 Sprite.

Final prompts used:

- **Sandlance Scout:** “Create a polished square 1:1 unit card illustration for a futuristic RTS, using the attached Blender render as a strict visual reference. Depict the same compact Sandlance scout tank: low angular blue-gray armored hull, two narrow forward cannons, dark continuous tracks with visible road wheels, a tall braced sensor mast, small cyan optics, and restrained amber identification accents. Preserve the model's silhouette, proportions, palette, and weapon count; do not add equipment. Three-quarter front view, centered with generous safe margins so no part is cropped, crisp low-poly hard-surface rendering, clear silhouette at thumbnail size, deep charcoal-to-blue tactical gradient background. No text, no labels, no logo, no frame, no HUD, no watermark.”
- **Skirmish Strike Bike:** “Create a polished square 1:1 unit card illustration for a futuristic RTS, using the attached Blender render as a strict visual reference. Depict the same compact two-wheel Skirmish Strike Bike: long blue-gray armored motorcycle frame, one large front wheel and one rear wheel, twin orange explosive charge canisters along the rear sides, dark metal rails and fork, small cyan headlamp and a single cyan arc induction ring over the center. Preserve the model's recognizable two-wheel silhouette, proportions, and palette; keep both wheels and all gear visible. Three-quarter front view, centered with generous safe margins, crisp low-poly hard-surface rendering, clear silhouette at thumbnail size, deep charcoal-to-blue tactical gradient background. No text, no labels, no logo, no frame, no HUD, no watermark.”
- **Colossus Siege Platform:** “Create a polished square 1:1 unit card illustration for a futuristic RTS, using the attached Blender render as a strict visual reference. Depict the same Colossus heavy siege tank: broad low blue-gray tracked hull, long high-caliber forward cannon with a compact muzzle brake, low angular turret, integrated cyan rangefinder optics, amber side warning stripe, and rear sensor antenna. Preserve its proportions, long-gun silhouette, and restrained palette; do not add equipment. Three-quarter front view, centered with generous safe margins so the full cannon and tracks remain in frame, crisp low-poly hard-surface rendering with readable silhouette at thumbnail size, deep charcoal-to-blue tactical gradient background. No text, no labels, no logo, no frame, no HUD, no watermark.”

## Rebuild and inspect

Run the model builders from the repository root with Blender 4.5.14 LTS:

```powershell
& .\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\Build-SampleScoutModel.py
& .\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\Build-SkirmishStrikeBikeModel.py
& .\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\Build-ColossusSiegeModel.py
```

The previews go to ignored `artifacts/previews/`. Rebuild the script-free overlays and pack bundle with the pinned Unity Editor:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe' -batchmode -nographics `
  -projectPath unity\RogueCommandAssetAuthoring `
  -executeMethod RogueCommandExpansion.Authoring.RogueCommandVisualSampleScaffolder.BuildSampleNativeExpansion `
  -quit -logFile artifacts\unity-visual-build-final.log
.\.venv\Scripts\python.exe .\tools\validate_content_bundle_references.py .\content\packs\sample-native-expansion
```

The 2026-10-17 build reopened the bundle in Unity and validated three script-free prefabs, their renderers/materials, and three card Sprites. UnityPy found 28 GameObjects, 22 MeshFilters/MeshRenderers/meshes, 22 materials, and three 128 × 128 textures/Sprites. The bundle is 521,138 bytes, SHA-256 `234f39ee7824457b54b5abd56917f2b51fda6ddf60c292bf72e9d92bf4789b6f`; its 1,588-byte manifest AssetBundle has SHA-256 `746fbb0584b141bafc85c5e43f73c8176e5b2d976db56b1ad207b1d59dfcb652`. The offline reference validator resolved all six card/prefab paths.

This is local authoring evidence. The earlier Steam observation used a different, smaller bundle and does not validate these updated models or cards in a match. The models also retain native gameplay hierarchies and do not retarget native animations or rigs.
