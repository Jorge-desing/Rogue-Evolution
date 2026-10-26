# Blender-Generated PBR Assets and Unity Integration

Use this reference when a unit needs unique geometry and HD PBR textures produced through Blender and integrated into the Unity authoring project.

## Pipeline

1. **Discover the Blender executable once.** Locate the installed Blender binary by checking the standard installation directories, then use the verified executable for all subsequent commands. Record the result and proceed; do not repeat the discovery probe after it succeeds. Do not hard-code a machine-specific path as a standing requirement.

2. **Run generators through Blender's embedded Python.** Invoke Blender with:
   ```
   blender --background --factory-startup --python-exit-code 1 --python <script> -- <arguments>
   ```
   Use `tools/generate_unit_model.py` for vehicle geometry. Pass `--bake-atlas --atlas-resolution 2048 --single-material` to produce atlas-baked PBR maps. Choose distinct archetypes (tank, walker, buggy, etc.) so each unit has genuinely unique silhouette and material layout.

3. **Preserve the texture-to-geometry pairing.** The single-material atlas output (`Name_Atlas.fbx`) is the file that matches the baked texture set. The separate multi-material FBX (`Name.fbx`) retains per-part material slots and does not automatically align with the atlas maps. Inspect exporter outputs and assign textures only to the matching mesh.

4. **Import new files through Unity before expecting `.meta` files.** Unity creates `.meta` files on import. A missing `.meta` before import is a normal lifecycle state, not a reason to poll unchanged files. Run the Unity Editor import (batch mode or interactive) to generate metadata, then proceed.

5. **Persist materials as assets.** Create `.mat` files through `AssetDatabase.CreateAsset` or `AssetDatabase.SaveAssets` so they survive Unity reloads and can be referenced by prefabs and bundles. A transient `new Material` instance is not a completed material deliverable. On reruns, reuse the existing asset path and update its properties rather than creating duplicates.

6. **Configure texture import semantics.** Set import settings through `TextureImporter`:
   - Albedo: sRGB, sprite or texture type as appropriate.
   - MetallicGloss: linear (`sRGBTexture = false`), `alphaSource = FromInput`, `alphaIsTransparency = false`; metallic in RGB, smoothness in A.
   - Normal: `TextureImporterType.NormalMap`.
   - Emission: sRGB.
   Verify effective import settings after the builder reimports the maps.

7. **Bind materials to prefabs.** Load the persisted `.mat` asset and assign it to the model's renderers. Verify the prefab references the material asset (not an in-memory instance) and that the shader keywords (`_METALLICSPECGLOSSMAP`, `_NORMALMAP`, `_EMISSION`) are enabled when the corresponding maps are present.

8. **Validate before claiming completion.** After material binding:
   - Rebuild the affected AssetBundle with the repository scaffolder.
   - Run `tools/validate_content_bundle_references.py` against the pack.
   - Confirm the bundle contains the prefab with the expected material dependency.
   File existence and image dimensions verify generation only; they do not verify UV alignment, material persistence, shader compatibility, or in-game appearance.

## Evidence Boundaries

- Generating models and baking 2048² PBR maps (albedo, metallic/smoothness, normal, emission) proves that geometry and texture data exist. It does not prove that the textures are correctly applied to the mesh, that materials persist, or that the bundle is valid.
- Assigning a texture to a `new Material` in memory does not prove the material is saved or referenced by the prefab.
- Running the scaffolder's `BuildTacticalOperationsPack` method and validating bundle references proves structural integration. Only a Steam-launched match with the QA profile can confirm visual rendering.
- When a step is only proposed (not executed), label it as a proposed correction, not a completed validation.

## Common Pitfalls

- **Atlas vs. multi-material mismatch.** The single-material atlas FBX and the multi-material FBX are separate exports. Assigning the atlas material to the multi-material mesh (or vice versa) produces wrong or missing textures. Always inspect which FBX the scaffolder loads before binding materials.
- **Transient materials.** Creating a `Material` in memory without `AssetDatabase.CreateAsset` means the material is lost on domain reload and never appears in the bundle. Always persist.
- **Missing `.meta` before import.** Polling for `.meta` files before Unity has imported the assets wastes iterations. Import first, then verify metadata.
- **Reused model placeholders.** Reusing another unit's FBX (e.g., SkyguardSAM.fbx for Bulwark) with a different name is not a unique asset. Label it explicitly as a reused placeholder until unique geometry is authored.
- **Shader keyword omission.** The `VehiclePBR_VertexWear` shader requires `_METALLICSPECGLOSSMAP`, `_NORMALMAP`, and `_EMISSION` keywords to be enabled for the corresponding maps to render. Setting the texture property alone is insufficient.
- **Looping on discovery.** Once a probe establishes a fact (Blender path, file existence, texture dimensions), record the result and perform the next state-changing action. Repeating the same probe with a different label is not a new hypothesis.
