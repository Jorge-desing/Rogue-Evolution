# Unity Material Pipeline

This addendum records the Unity material and prefab authoring rules supplied for Rogue Command's Unity 2021.3.45f2 project. Keep exported texture data, importer settings, and material configuration consistent; bundle import alone does not prove runtime rendering.

## 6. PBR Texture Atlas Pipeline & Linear Importer Rules

- The atlas exporter writes metallic and smoothness data to `_MetallicGloss.png`; smoothness is derived from roughness as `1 - roughness`.
- Import packed material data as linear values. Disable sRGB color conversion for this data texture so the packed values are not gamma transformed.
- Keep the material's shader mode, texture keyword state, and metallic/smoothness multipliers aligned with the packed atlas. Do not infer those settings from the filename alone; validate the generated material against the exporter configuration.
- Keep albedo and emission handling separate from the linear material-data texture.

## 7. Automated Prefab Construction

### Current builder behavior (static source inspection)

- `VehiclePrefabBuilder` prefers a file named `_LODs.fbx` by path priority. It does not inspect that asset for a pre-authored `LODGroup` before selecting it.
- `SetupLodGroupIfAvailable` constructs a Unity `LODGroup` from descendant renderers whose names include `LOD0`/`_L0` and `LOD1`/`_L1`, with `LOD2`/`_L2` optional. It does not preserve an authored `LODGroup` as-is, and it does not validate the FBX hierarchy before selection.
- Detached accessories are mounted using the builder's socket-name matching rules. Preserve authored local position, rotation, and scale; exact socket-name enforcement remains a separate runtime-authoring requirement.

### Authoring target

- Add validation that the `_LODs.fbx` contains distinct, non-empty LOD renderer groups before using it. If invalid, fall back to the single-mesh source and report which asset failed validation.
- Keep the current renderer-name conventions explicit in the exporter until the builder can validate imported LOD hierarchies directly.
