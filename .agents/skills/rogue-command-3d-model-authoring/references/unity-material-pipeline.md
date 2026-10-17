# Unity Material & Shader Pipeline for Rogue Command

This guide explains how Unity 2021.3.45f2 handles materials, shaders, draw-call batching, and visual assets for the Rogue Command Expansion Loader.

---

## 1. The Unity Standard Shader Invariant

Rogue Command visual assets use Unity's built-in **Standard** shader (`Shader.Find("Standard")`). This ensures compatibility with the game's lighting passes, shadows, and post-processing stack without relying on custom shader bundles or pipeline mismatches.

### Material Property Matrix

| Property Name | Unity Property Key | Data Type | Value Range | Typical Use in Mod Models |
|---|---|---|---|---|
| **Albedo / Base Color** | `_Color` | `Color` (RGBA) | `0.0` to `1.0` | Primary armor, frame, tires, hazard stripes |
| **Metallic** | `_Metallic` | `float` | `0.0` to `1.0` | `0.08` for rubber, `0.35–0.45` for armor, `0.75–0.80` for gunmetal |
| **Smoothness** | `_Glossiness` | `float` | `0.0` to `1.0` | `0.28–0.34` for rubber, `0.60–0.65` for armor, `0.70–0.75` for machined metal |
| **Emission Color** | `_EmissionColor` | `Color` (HDR) | `RGB * intensity` | Glowing lenses, arc coils, warning beacons |
| **GPU Instancing** | `enableInstancing` | `bool` | `true` / `false` | Always set `true` to enable SRP/Standard batching |

---

## 2. The Critical Emission Activation Rule

> [!IMPORTANT]
> Simply setting `_EmissionColor` on a Unity `Material` does **not** make it glow! By default, the Standard shader optimizes out the emission pass if the `_EMISSION` shader keyword is not explicitly enabled.

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
    Shader shader = Shader.Find("Standard");
    if (shader == null)
    {
        throw new InvalidOperationException("Unity's Standard shader is unavailable for " + name + ".");
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
    material.SetFloat("_Glossiness", smoothness);
    material.enableInstancing = true; // Crucial for RTS draw-call batching!

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
  3. `material.enableInstancing = true` allows Unity's batcher to render all meshes sharing the same material in 1–2 GPU instanced calls.

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
