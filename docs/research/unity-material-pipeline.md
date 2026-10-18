# Unity Material Pipeline

This addendum records the Unity material and prefab authoring rules supplied for Rogue Command's Unity 2021.3.45f2 project. Keep exported texture data, importer settings, and material configuration consistent; bundle import alone does not prove runtime rendering.

## 6. PBR Texture Atlas Pipeline & Linear Importer Rules

- The atlas exporter writes `R = metallic` and `A = smoothness = 1 - roughness` to `_MetallicGloss.png`; G and B duplicate metallic for other consumers. `RogueCommand/VehiclePBR_VertexWear` is an URP forward shader and samples those channels when `_METALLICSPECGLOSSMAP` is enabled. Direct sample visuals use `Universal Render Pipeline/Lit`.
- Import `_MetallicGloss.png` as a Default texture with sRGB disabled, alpha sourced from the input, and `alphaIsTransparency` disabled. These settings preserve the packed channel values. Import normal maps as NormalMap textures; import albedo and emission maps as Default textures with sRGB enabled.
- Set `_GlossMapScale` to `1` to preserve the packed smoothness range. The URP atlas shader reads smoothness from the metallic-gloss map alpha and does not use the albedo alpha as its smoothness source.
- `_Metallic` and `_Smoothness` are scalar fallback values when `_METALLICSPECGLOSSMAP` is disabled; they are not multipliers on the metallic-gloss texture. The map path uses R for metallic and multiplies A by `_GlossMapScale` for smoothness.
- When rebuilding a material without a metallic-gloss, normal, or emission map, clear the corresponding texture property and disable its keyword so an older material state cannot retain a stale map. Keep albedo and emission handling separate from the linear material-data texture.


## 7. Automated Prefab Construction

### Current builder behavior (static source inspection)

- `VehiclePrefabBuilder` validates a `_LODs.fbx` asset before selecting it and falls back to `_Atlas.fbx` or the exact single-mesh FBX source with a warning when required LOD renderer groups are missing. The generic model search excludes LOD/damaged/accessory variants. An animated primary FBX remains a deliberate exception: the current standalone LOD export is mesh-only, so selecting it would discard the imported rig and clips.
- `SetupLodGroupIfAvailable` constructs a Unity `LODGroup` from descendant renderers whose names contain exact separator-delimited `LOD0`/`L0` and `LOD1`/`L1` tokens, with `LOD2`/`L2` optional. Only enabled renderers under active-self ancestors with non-empty `MeshFilter` or `SkinnedMeshRenderer` geometry count. It does not preserve an authored `LODGroup` as-is.
- Detached accessories are mounted using the builder's socket-name matching rules. When the matching hardpoint socket exists, the builder parents the FBX root below an identity attachment group; otherwise it warns and parents the accessory under the visual root. In either case, the imported root local position, rotation, and scale are preserved. Exact socket-name enforcement remains a separate runtime-authoring requirement.

### Authoring target

- Implemented in source: validate the `_LODs.fbx` renderer groups before selection, report an invalid path and reason, and fall back to the atlas or exact single-mesh source. This is static source evidence only; Unity import and prefab build remain unverified after the change.
- Implemented in source: retain each detached accessory FBX root's authored local TRS during mounting. This is based on static exporter/builder inspection; accessory FBX imports and prefab construction have not been verified in Unity.
- Keep the current renderer-name conventions explicit in the exporter until the builder can validate imported LOD hierarchies directly.

## 8. URP Compatibility and Bundle Evidence

- Rogue Command 1.0.9 uses URP. Its native units use `Shader Graphs/EntityColorShader`, which also consumes game-specific fog and distortion buffers. The mod's PBR vehicle shader is URP-lit and self-contained; it does not reproduce those native fog, distortion, or glitter effects.
- The Unity authoring project resolves `com.unity.render-pipelines.universal` version `12.1.15`, matching the Unity 2021.3.45f2 installation. The sample material scaffolder assigns `Universal Render Pipeline/Lit`, and the atlas builder requires `RogueCommand/VehiclePBR_VertexWear`; it no longer falls back to Built-in `Standard`.
- Local DLLs under the ignored `Assets/Plugins/GameAssemblies/` directory are reflection-only authoring inputs. Keep them Editor-only and disable Plugin Inspector Auto Reference, because the game's URP/Mathematics/Burst/UI assemblies conflict with UPM copies when automatically referenced by Unity's predefined assemblies. The scaffolder now sets serialized `PluginImporter.isExplicitlyReferenced` through `SerializedObject` and uses `Assembly.LoadFrom` for reflection.
- AssetBundle build/reopen and shader inspection are local validation. Only a Steam-launched in-game session can confirm that Rogue Command selects the URP pass and renders the unit as intended.
