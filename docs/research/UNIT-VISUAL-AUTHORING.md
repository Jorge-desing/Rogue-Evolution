# Rogue Command unit visuals

**Updated:** 2026-10-18

**Evidence:** Blender authoring build, Unity Editor bundle build, and offline bundle-reference validation. No match was launched for this visual update.

## Current models

| Unit ID | Native template and visual root | Editable Blender source | Builder | Unity FBX | Source parts → FBX batches | Blender bounds (W × L × H) |
|---|---|---|---|---|---:|---:|
| `sample-native:scout-tank` | `PCXBaseTank` / `Vehicle` | `content/source-art/sandlance-scout.blend` | `tools/Build-SampleScoutModel.py` | `SandlanceScout.fbx` | 78 → 7 | 2.17 × 2.52 × 1.57 m |
| `sample-native:skirmish-bike` | `BombBike` / `GameObject/BombBike` | `content/source-art/skirmish-strike-bike.blend` | `tools/Build-SkirmishStrikeBikeModel.py` | `SkirmishStrikeBike.fbx` | 59 → 7 | 1.20 × 2.20 × 1.37 m |
| `sample-native:heavy-artillery` | `ArtilleryTank` / `Vehicle` | `content/source-art/colossus-siege.blend` | `tools/Build-ColossusSiegeModel.py` | `ColossusSiegePlatform.fbx` | 104 → 8 | 2.58 × 4.74 × 1.86 m |
| `sample-native:assault-walker` | `GatlingWalker` / native articulated visual | `content/source-art/ironclad-walker.blend` | `tools/Build-IroncladWalkerModel.py` | `IroncladWalker.fbx` authoring prototype only | 74 → 5 | 1.83 × 1.98 × 2.45 m |
| `sample-native:surge-tank` | `LightningTank` / `Vehicle` | `content/source-art/surge-arc-tank.blend` | `tools/Build-SurgeArcTankModel.py` | `SurgeArcTank.fbx` | 94 → 6 | 1.99 × 2.78 × 1.52 m |

All Blender sources keep individually editable parts. Export builders duplicate and merge those parts by material, producing five to eight FBX batches. Blender's local quality checks confirmed ground contact and assigned materials for all five models. The Ironclad uses a reinforced torso, shoulder armor, recessed optics, and paired waist-height Gatling cannons; its 1.83 × 1.98 × 2.45 m bounds fit the approximate `GatlingWalker` envelope. Its current FBX is static, though, and would replace the native walker renderer while discarding visible leg articulation. The active pack therefore keeps the native `GatlingWalker` visual and omits `IroncladWalkerVisual.prefab` from the bundle until the custom model has a supported rig. The Surge Arc Tank adds an insulated generator, twin capacitors, and a forward-facing induction ring centered at the documented LightningTank weapon pivot; its 1.99 × 2.78 × 1.52 m bounds fit the approximate 2.3 × 2.8 × 1.6 m LightningTank envelope. The Colossus retains a long siege cannon that extends beyond the approximate 3.60 m `ArtilleryTank` length; verify spacing and clipping in a Steam-launched match before calling it runtime-ready.

## ImageGen card sprites

The Unity visual bundle includes five tracked ImageGen-authored PNGs, one for every added unit. Each unit's Blender preview was used to preserve its silhouette; the new Ironclad Walker and Surge Arc Tank images also used the Colossus Siege card for lighting and background style. Their source PNGs are 1254 × 1254: `IroncladWalkerCard.png` SHA-256 `68877148ce681f92a810398d492c456bcb97ef411b35f8d6871e2d1a02a4784a`, and `SurgeArcTankCard.png` SHA-256 `db7283d8c2542a8ec6f0074cdf5e4c41500df2769ec4b1b3fe9677e45455b003`. Unity imports each PNG as a single Sprite with Point filtering, no mipmaps, Clamp wrap mode, Uncompressed texture data, and a 128 × 128 maximum size. The scaffolder contains no procedural card drawing code and fails if a source PNG is missing or does not import as a 128 × 128 Sprite.

Final prompts used:

- **Sandlance Scout:** “Create a polished square 1:1 unit card illustration for a futuristic RTS, using the attached Blender render as a strict visual reference. Depict the same compact Sandlance scout tank: low angular blue-gray armored hull, two narrow forward cannons, dark continuous tracks with visible road wheels, a tall braced sensor mast, small cyan optics, and restrained amber identification accents. Preserve the model's silhouette, proportions, palette, and weapon count; do not add equipment. Three-quarter front view, centered with generous safe margins so no part is cropped, crisp low-poly hard-surface rendering, clear silhouette at thumbnail size, deep charcoal-to-blue tactical gradient background. No text, no labels, no logo, no frame, no HUD, no watermark.”
- **Skirmish Strike Bike:** “Create a polished square 1:1 unit card illustration for a futuristic RTS, using the attached Blender render as a strict visual reference. Depict the same compact two-wheel Skirmish Strike Bike: long blue-gray armored motorcycle frame, one large front wheel and one rear wheel, twin orange explosive charge canisters along the rear sides, dark metal rails and fork, small cyan headlamp and a single cyan arc induction ring over the center. Preserve the model's recognizable two-wheel silhouette, proportions, and palette; keep both wheels and all gear visible. Three-quarter front view, centered with generous safe margins, crisp low-poly hard-surface rendering, clear silhouette at thumbnail size, deep charcoal-to-blue tactical gradient background. No text, no labels, no logo, no frame, no HUD, no watermark.”
- **Colossus Siege Platform:** “Create a polished square 1:1 unit card illustration for a futuristic RTS, using the attached Blender render as a strict visual reference. Depict the same Colossus heavy siege tank: broad low blue-gray tracked hull, long high-caliber forward cannon with a compact muzzle brake, low angular turret, integrated cyan rangefinder optics, amber side warning stripe, and rear sensor antenna. Preserve its proportions, long-gun silhouette, and restrained palette; do not add equipment. Three-quarter front view, centered with generous safe margins so the full cannon and tracks remain in frame, crisp low-poly hard-surface rendering with readable silhouette at thumbnail size, deep charcoal-to-blue tactical gradient background. No text, no labels, no logo, no frame, no HUD, no watermark.”
- **Ironclad Walker:** “Create a new square 1:1 polished RTS unit-card illustration. Use the first reference strictly for the Ironclad Walker's identity and exact silhouette: a squat bipedal gray-blue armored combat walker with a compact reinforced torso, broad rectangular shoulder armor, small cyan binocular optics, two waist-height arm-mounted Gatling cannons with orange circular muzzles, dark mechanical legs, and two wide separate feet. Preserve the distinctive proportions and equipment from that model; do not turn it into a tank or add weapons, antennae, limbs, or accessories. Use the second reference only for the visual treatment: realistic polished hard-surface 3D rendering, cool metallic highlights, restrained amber accents, and a deep charcoal-to-navy tactical gradient background. Three-quarter front view from slightly above. Show the entire walker including both cannon muzzles, both legs, and both feet, centered with generous clear space around every edge (about 18 percent safe margin); keep the subject smaller rather than cropping any part. Strong readable silhouette at small thumbnail size. No text, labels, logo, card frame, HUD, border, watermark, extra units, or environmental props.”
- **Surge Arc Tank:** “Create a new square 1:1 polished RTS unit-card illustration. Use the first reference strictly for the Surge Arc Tank's identity and exact silhouette: a compact low gray-blue tracked tank with broad angular hull, two dark continuous tracks, cyan optics, and a distinctive bright cyan circular induction ring mounted horizontally on top of the turret, with small magenta/pink insulator tabs around the ring. Preserve that visible ring and the model's proportions; do not add a cannon, missiles, antenna, or extra equipment. Use the second reference only for the visual treatment: polished hard-surface 3D rendering, cool metallic highlights, restrained color accents, and a deep charcoal-to-navy tactical gradient background. Three-quarter front view from slightly above, with the cyan induction ring clearly visible. Show the entire tank, both track assemblies, and full ring, centered with generous clear space around every edge (about 18 percent safe margin); keep the subject smaller rather than cropping any part. Strong readable silhouette at small thumbnail size. No text, labels, logo, card frame, HUD, border, watermark, extra units, or environmental props.”

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

The 2026-10-18 Unity 2021.3.45f2 build now reopens and validates four visual prefabs and five 128 × 128 Sprite assets. UnityPy independently confirms the bundle has only Sandlance, Skirmish Bike, Colossus, and Surge visual roots; the Ironclad Walker card remains but its static visual prefab is absent. The offline bundle-reference validator resolves all nine declared visual/card references. The bundle is 705,745 bytes with SHA-256 `a1811b8ef4a4043ebd9f4ebf6f003e6e77ffe5e2955e07bd63e23e742c136376`; the manifest is 1,588 bytes with SHA-256 `23df8982d61cc24b09f70baa9fe4680b60e4eaf08f4eec2557a8b1b17a8559ef`. Unity compilation emitted one pre-existing warning in `RogueCommandSamplePackScaffolder.cs` about `foundInPlugins`; the visual bundle build completed successfully.

This is local authoring and static bundle evidence. No Steam-launched match has validated that the card images appear in randomized offers or that the model overlays render during combat. These overlays retain native gameplay hierarchies and do not retarget native animations or rigs.
