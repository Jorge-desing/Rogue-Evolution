# Rogue Command unit visuals

**Updated:** 2026-10-17

**Evidence:** Blender authoring build, Unity Editor bundle build, and offline bundle-reference validation. No match was launched for this visual update.

## Current models

| Unit ID | Native template and visual root | Editable Blender source | Builder | Unity FBX | Source parts → FBX batches | Blender bounds (W × L × H) |
|---|---|---|---|---|---:|---:|
| `sample-native:scout-tank` | `PCXBaseTank` / `Vehicle` | `content/source-art/sandlance-scout.blend` | `tools/Build-SampleScoutModel.py` | `SandlanceScout.fbx` | 78 → 7 | 2.17 × 2.52 × 1.57 m |
| `sample-native:skirmish-bike` | `BombBike` / `GameObject/BombBike` | `content/source-art/skirmish-strike-bike.blend` | `tools/Build-SkirmishStrikeBikeModel.py` | `SkirmishStrikeBike.fbx` | 59 → 7 | 1.20 × 2.20 × 1.37 m |
| `sample-native:heavy-artillery` | `ArtilleryTank` / `Vehicle` | `content/source-art/colossus-siege.blend` | `tools/Build-ColossusSiegeModel.py` | `ColossusSiegePlatform.fbx` | 104 → 8 | 2.58 × 4.74 × 1.86 m |
| `sample-native:assault-walker` | `GatlingWalker` / `Vehicle` | `content/source-art/ironclad-walker.blend` | `tools/Build-IroncladWalkerModel.py` | `IroncladWalker.fbx` | 74 → 5 | 1.83 × 1.98 × 2.45 m |
| `sample-native:surge-tank` | `LightningTank` / `Vehicle` | `content/source-art/surge-arc-tank.blend` | `tools/Build-SurgeArcTankModel.py` | `SurgeArcTank.fbx` | 94 → 6 | 1.99 × 2.78 × 1.52 m |

All Blender sources keep individually editable parts. Export builders duplicate and merge those parts by material, producing five to eight FBX batches. Blender's local quality checks confirmed ground contact and assigned materials for all five models. The Ironclad uses a reinforced torso, shoulder armor, recessed optics, and paired waist-height Gatling cannons; its 1.83 × 1.98 × 2.45 m bounds fit the approximate `GatlingWalker` envelope. The Surge Arc Tank adds an insulated generator, twin capacitors, and a forward-facing induction ring centered at the documented LightningTank weapon pivot; its 1.99 × 2.78 × 1.52 m bounds fit the approximate 2.3 × 2.8 × 1.6 m LightningTank envelope. The Colossus retains a long siege cannon that extends beyond the approximate 3.60 m `ArtilleryTank` length; verify spacing and clipping in a Steam-launched match before calling it runtime-ready.

## ImageGen card sprites

The Unity visual bundle includes three tracked ImageGen-authored PNGs. Each final Blender preview was used as the image reference. Unity imports each as a single Sprite with Point filtering, no mipmaps, Clamp wrap mode, Uncompressed texture data, and a 128 × 128 maximum size. The scaffolder contains no procedural card drawing code and fails if a source PNG is missing or does not import as a 128 × 128 Sprite. Ironclad and Surge Arc Tank deliberately keep the native `GatlingWalker` and `LightningTank` cards: their manifest entries omit `cardImageAssetName`; the Surge model update generated no new 2D image.

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
& .\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\Build-IroncladWalkerModel.py
& .\.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe --background --python tools\Build-SurgeArcTankModel.py
```

The Ironclad and Surge Arc Tank builders validate their meshes and bounds, save the editable sources and FBXs, and skip preview rendering so they do not alter existing renders or framing.

The previews go to ignored `artifacts/previews/`. Rebuild the script-free overlays and pack bundle with the pinned Unity Editor:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe' -batchmode -nographics `
  -projectPath unity\RogueCommandAssetAuthoring `
  -executeMethod RogueCommandExpansion.Authoring.RogueCommandVisualSampleScaffolder.BuildSampleNativeExpansion `
  -quit -logFile artifacts\unity-visual-build-final.log
.\.venv\Scripts\python.exe .\tools\validate_content_bundle_references.py .\content\packs\sample-native-expansion
```

The 2026-10-17 Surge Arc Tank build reopened the bundle in Unity and validated five script-free prefabs, all renderers/materials/components, and the three card Sprites. UnityPy found 43 GameObjects/Transforms, 33 MeshFilters, 33 MeshRenderers/meshes, 33 materials, zero MonoBehaviours, and three 128 × 128 textures/Sprites. The bundle is 774,549 bytes, SHA-256 `856873b3b92e0c65eb59c42753d130a93baa04ab24978b8e26a88063891f4fef`; its 1,588-byte manifest AssetBundle has SHA-256 `8e10a57f635c91c973d6348622baa902ba520a3901d4609fe838a38cdc97835b`. The offline reference validator resolved all eight manifest bundle references.

This is local authoring evidence. No Steam-launched match has validated the current bundle or the Surge Arc Tank's visual alignment. These overlays retain native gameplay hierarchies and do not retarget native animations or rigs.
