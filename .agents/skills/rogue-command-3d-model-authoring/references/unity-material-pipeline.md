# Unity Material & Shader Pipeline for Rogue Command

This guide explains how Unity 2021.3.45f2 handles materials, shaders, draw-call batching, and visual assets for the Rogue Command Expansion Loader.

---

## 1. Pipeline Architecture & Shader Bundling

Rogue Command executes on Unity's **Universal Render Pipeline (URP)** (`UniversalRenderPipelineAsset` in `globalgamemanagers.assets`). Native game vehicles use untextured geometry rendered via `Shader Graphs/EntityColorShader` (which integrates URP Fog of War buffers and procedural glitter/noise).

The repository-wide Material Maker matrix (`tools/material-maker/rogue-command-material-maker.json`) routes the same image contract into this URP path. `tools/Run-MaterialMakerBake.ps1 -Workflow urp` exports images only, packs `MetallicGloss` as RGB metallic plus A smoothness, and leaves `VehiclePBR_VertexWear` material creation/import to the existing Unity builder. Do not import Material Maker's generated `.mat`; it is not the authority for URP shader keywords, emission activation, or AssetBundle shader bundling.

The authoring project resolves URP 12.1.15. Simple sample materials use `Universal Render Pipeline/Lit`; atlased vehicle materials use the custom URP shader `RogueCommand/VehiclePBR_VertexWear` to read vertex-wear channels. These shaders are included with the AssetBundle. The custom shader does not implement the native `EntityColorShader` fog, distortion, or glitter buffers, and runtime rendering must be confirmed in a Steam-launched game.

### Material Property Matrix

| Property Name | Unity Property Key | Data Type | Value Range | Typical Use in Mod Models |
|---|---|---|---|---|
| **Albedo / Base Color** | `_BaseColor` | `Color` (RGBA) | `0.0` to `1.0` | Primary armor, frame, tires, hazard stripes |
| **Metallic** | `_Metallic` | `float` | `0.0` to `1.0` | `0.08` for rubber, `0.35–0.45` for armor, `0.75–0.80` for gunmetal |
| **Smoothness** | `_Smoothness` | `float` | `0.0` to `1.0` | `0.28–0.34` for rubber, `0.60–0.65` for armor, `0.70–0.75` for machined metal |
| **Emission Color** | `_EmissionColor` | `Color` (HDR) | `RGB * intensity` | Glowing lenses, arc coils, warning beacons |
| **GPU Instancing** | `enableInstancing` | `bool` | `true` / `false` | Enables instancing only when the shader, mesh, and renderer setup support it |

---

## 2. The Critical Emission Activation Rule

> [!IMPORTANT]
> Simply setting `_EmissionColor` on a Unity `Material` does **not** make it glow. The URP Lit and vehicle shaders require the `_EMISSION` keyword to enable emission sampling.

In C# Unity Editor scripts (such as `RogueCommandVisualSampleScaffolder.cs`):

```csharp
private static Material CreateOrUpdateMaterial(
    string name,
    Color color,
    float metallic,
    float smoothness,
    Color? emissionColor = null)
{
    string path = MaterialRoot + "/" + name + ".mat";
    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
    Shader shader = Shader.Find("Universal Render Pipeline/Lit");
    if (shader == null)
    {
        throw new InvalidOperationException("The URP Lit shader is unavailable for " + name + ".");
    }

    if (material == null)
    {
        material = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(material, path);
    }
    else
    {
        material.shader = shader;
    }

    material.color = color;
    material.SetFloat("_Metallic", metallic);
    material.SetFloat("_Smoothness", smoothness);
    material.enableInstancing = true; // Permits instancing when the renderer setup is eligible.

    if (emissionColor.HasValue && emissionColor.Value != Color.black)
    {
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emissionColor.Value);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
    }
    else
    {
        material.DisableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Color.black);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
    }

    EditorUtility.SetDirty(material);
    return material;
}
```

### Emission HDR Values for Key Accents
- **Arc Cyan Energy**: `new Color(0.06f, 0.68f, 0.95f) * 1.8f` (Coils, induction rings)
- **Sensor Glass Optics**: `new Color(0.12f, 0.85f, 0.78f) * 1.6f` (Headlights, lenses)
- **Volatile Amber / Orange**: `new Color(1.0f, 0.32f, 0.05f) * 1.5f` (Explosive charges, warning beacons)

---

## 3. Draw Call Optimization & Material Sharing

In a dense RTS match, 40 to 80 units may occupy the screen simultaneously.

- **Bad Pattern**: Each primitive in the FBX uses a unique duplicate material or instance. 70 primitives $\times 50$ units $= 3500$ draw calls, causing frame drops.
- **Good Pattern (Material Key Batching)**:
  1. The authoring project creates exactly 6–8 canonical `.mat` assets in `Assets/.../Materials/`.
  2. The scaffolder maps every imported sub-mesh renderer to one of those canonical materials by name prefix (`ScoutArmor_`, `ScoutGun_`, `ScoutSensor_`, etc.).
  3. `material.enableInstancing = true` permits instancing when mesh, renderer, shader, and property-block conditions are met; it does not guarantee a particular draw-call count.

---

## 4. Prefab Hygiene & Validation Invariants

When the Rogue Command Expansion Loader (`ContentPackRuntimeLoader.cs`) loads a visual prefab from an AssetBundle, it validates strict safety invariants:

1. **Zero `MonoBehaviour` Scripts**:
   - Visual prefabs must contain **no** C# scripts or missing script stubs.
   - Any component inheriting from `MonoBehaviour` triggers an immediate fail-closed `InvalidDataException`.
   - Permitted components: `Transform`, `MeshFilter`, `MeshRenderer`.
2. **Enabled Renderers**:
   - The prefab must contain at least one enabled `Renderer` (`renderer.enabled == true`).
   - Every active renderer must have a non-null material assigned.
3. **Local Identity Root**:
   - The root `GameObject` of the visual prefab should have:
     - `localPosition = (0, 0, 0)`
     - `localRotation = Quaternion.identity`
     - `localScale = (1, 1, 1)`
   - All spatial offsets, ground clearance, and vehicle-specific centering must reside in child transforms.

---

## 5. Card Sprite Authoring Standards

Each playable unit requires a matching card Sprite referenced by `cardImageAssetName`.

1. **Dimensions**: Exactly `128 × 128` pixels.
2. **Texture Import Settings**:
   - `textureType = TextureImporterType.Sprite`
   - `spriteImportMode = SpriteImportMode.Single`
   - `filterMode = FilterMode.Point` (preserves crisp pixel art / low-poly edges)
   - `mipmapEnabled = false` (avoids blurry cards on lower texture settings)
   - `alphaIsTransparency = true`
3. **Card Composition**:
   - **Backdrop**: Deep tactical gradient (e.g., `#0a1216` to `#162a34`).
   - **Border Frame**: 1-pixel inner border with team accent corner cuts.
   - **Vehicle Silhouette**: Angled profile of the unit matching the exact colors of the 3D model.
   - **High-Contrast Focal Highlight**: Bright emissive optic dot or weapon muzzle glint to draw the player's eye in the card draft interface.

---

## 6. PBR Texture Atlas Pipeline & Linear Importer Rules

The pipeline can bake multiple object materials into one PBR texture set and assign one material slot to the atlas mesh. This may reduce material and renderer overhead; it does not guarantee one draw call per unit. The prefab builder prefers an animated primary FBX, then a valid `_LODs.fbx`, and only then the atlas, while Unity passes and render settings also affect runtime submissions.

### A. Texture Maps & Channel Layout

| Texture File | Channels / Layout | Import Setting (`TextureImporter`) | URP Shader Destination |
| :--- | :--- | :--- | :--- |
| `<Name>_Albedo.png` | sRGB Color (Diffuse, Camo, Wear) | `sRGBTexture = true` | `_BaseMap` |
| `<Name>_MetallicGloss.png` | **Linear Non-Color**: Red/Green/Blue = Metallic, **Alpha = Smoothness** ($1.0 - \text{Roughness}$) | `sRGBTexture = false`, `alphaSource = TextureImporterAlphaSource.FromInput` | `_MetallicGlossMap` (Keyword: `_METALLICSPECGLOSSMAP`) |
| `<Name>_Normal.png` | Tangent-space Normal Vector | `textureType = TextureImporterType.NormalMap` | `_BumpMap` (Keyword: `_NORMALMAP`) |
| `<Name>_Emission.png` | sRGB Color (Sensors, Coils, Beacons) | `sRGBTexture = true` | `_EmissionMap` (Keyword: `_EMISSION`) |

### B. Custom URP Atlas Shader Keyword & Multiplier Invariants

When assigning `_MetallicGlossMap` in C# (`VehiclePrefabBuilder.cs`):
```csharp
mat.SetTexture("_MetallicGlossMap", metallicGlossTex);
mat.EnableKeyword("_METALLICSPECGLOSSMAP");
mat.SetFloat("_GlossMapScale", 1.0f); // Preserve the packed Alpha smoothness.
```
If `sRGBTexture` is left `true`, Unity applies gamma-to-linear conversion to the packed material data. The atlas shader reads metallic from R and multiplies A by `_GlossMapScale`; `_Metallic` and `_Smoothness` supply scalar fallback values when the map keyword is disabled.

---

## 7. Automated Prefab Construction (`VehiclePrefabBuilder.cs`)

The C# editor scaffolder automatically constructs game-ready prefabs from Blender exports and `.gameplay.json`:

1. **Model Discovery Precedence**:
   - Uses the animated `<Name>.fbx` first when it has animation clips; otherwise it uses a valid `<Name>_LODs.fbx`, then `<Name>_Atlas.fbx`, the primary FBX, or the legacy asset path.
   - When `_LODs.fbx` is loaded, every discovered child renderer (`LOD0`, `LOD1`, `LOD2`) is assigned the shared `atlasMaterial`.
   - `LODGroup` is created with standard RTS thresholds: LOD0 (50%), LOD1 (20%), LOD2 (5% cull).
2. **Modular Attachments & Damaged Variants**:
   - Scans using `AssetDatabase.FindAssets` across model folders for `<Name>_Attachment_<Type>.fbx` and `<Name>_Damaged.fbx`.
   - Mounts attachments under `RCL_AttachmentGroup_<Type>` at the vehicle root.
   - Mounts damaged variant under prefab root as an inactive object (`SetActive(false)`), activated at runtime by `RuntimeVisualDirector` when `HealthRatio <= 0.50f`.
3. **Collision Box Scaffolding**:
   - Converts `_COLLIDER_Hull` and `_COLLIDER_Turret` meshes to invisible `BoxCollider` components (`renderer.enabled = false`).
   - If no child collider meshes exist, generates an approximate `BoxCollider` from `.gameplay.json` bounds.

---

## 8. Custom Weighted Normals & MikkTSpace Tangent Contract

Preserve the Blender-authored shading so intended armor planes, formed curves, and edge transitions survive the Unity URP import:

1. **ModelImporter Configuration Invariants**:
   - `importer.importNormals = ModelImporterNormals.Import`
     - Preserve Blender-authored split normals on these FBX assets; do not switch to `Calculate` unless the source intentionally omits normals and that asset's shading is reviewed again.
   - If Unity must calculate normals, set `importer.normalCalculationMode = ModelImporterNormalCalculationMode.MikkTSpace` and visually check the result.
   - `importer.importTangents = ModelImporterTangents.CalculateMikkTSpace`
     - Unity generates tangents in the MikkTSpace basis used by the project's normal-map workflow. This aligns the tangent convention; UV padding, triangulation, and map content can still produce visible seams and must be checked on the target mesh.

2. **Per-surface shading review**:
   - Keep authored planar armor faces stable and preserve smooth normals on parts designed as curved or cast.
   - Use one-segment chamfers for crisp plate breaks and additional segments only where the radius is visible and appropriate.
   - If an imported vehicle shows faceted banding or bent specular gradients on broad faces, inspect the source normals, FBX export, and effective importer settings. Do not force every surface to read as a mirror or smooth every armor boundary.

---

## 9. Surface Detail and Decal Support

The repository's VehiclePBR_VertexWear shader declares _DetailBumpMap, _DetailBumpScale, and _DetailTiling and combines a detail normal during shading. This confirms shader-side capability only. Inspect the actual material and builder before relying on a detail map; the default four-map contract does not add a fifth texture, and a default shader property is not proof that a production material binds a custom image.

The current Unity authoring project has no confirmed Decal Renderer Feature or D-Buffer setup. Do not author workflows that depend on projector decals until the active URP Renderer asset is inspected and the feature is tested in the pinned Unity version. For stencils, welds, and wear today, use the existing atlas, authored geometry, or mesh vertex colors within their current contracts.

Micro-normal detail should remain subtle and should not obscure panel boundaries or unit readability. Inspect the actual material response at RTS scale and verify normal-map import settings. Do not add extra AO, roughness, height, or decal channels without a separate verified shader/importer change.
## 10. Modular Trim Sheets & Instance Eligibility

For massive strategic factories, refineries, and complex vehicle fleets, trim sheets maximize visual fidelity while minimizing draw calls:

1. **Trim Sheet Atlas Design**:
   - A single 2K or 4K PBR material atlas hosts horizontal detail bands:
     - Structural I-beam flanges, bolted plate joints, corrugated cladding, weld seams, warning chevrons, and cable raceways.
2. **UV Authoring**:
   - Trim-sheet mapping is optional for repeated modules such as factory walls, trusses, and piping supports. Use the project's verified Blender workflow unless an add-on has been checked for license, Blender 4.5.14 compatibility, install path, and reproducible export.
3. **URP GPU Instancing**:
   - `enableInstancing = true` makes eligible renderers available for instancing when mesh, material, shader, and property-block conditions allow it; it does not guarantee a draw-call reduction. Measure the actual prefab and renderer setup in Unity.

