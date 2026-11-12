# Rogue Command Asset Authoring

This is a standalone Unity project for preparing Windows x64 AssetBundles used by the Rogue Command Expansion Loader. It is separate from the `Rogue Evolution` Unity Hub project.

## Add to Unity Hub

Choose the folder based on the Hub action you opened:

- **Add > Add project from disk:** select the project root folder `unity/RogueCommandAssetAuthoring` itself. It contains `Assets`, `Packages`, and `ProjectSettings/ProjectVersion.txt`.
- **Import Projects / scan a folder:** select the parent `unity`, which contains the `RogueCommandAssetAuthoring` project folder. This scan searches inside the selected parent; choosing the project folder itself makes the scan report that it found no projects.

Open it with Unity **2021.3.45f2**. The exact editor version is recorded in `ProjectSettings/ProjectVersion.txt` because the game uses that Unity release. Ensure **Windows Build Support (Mono)** is installed for this editor version.

The authoring project keeps local development copies of the game's managed DLLs under `Assets/Plugins/GameAssemblies/`. The scaffolder accepts only the verified `Assembly-CSharp.dll` SHA-256 and MVID and marks these DLLs Editor-only. The root `.gitignore` exclusion was checked with Git; `ignore.conf` contains matching Unity Version Control patterns, but no local Plastic CLI was available to verify their evaluation. These DLLs are development references only and are never bundled or distributed. No extracted commercial game assets are included.

The visual sample scaffolder imports Sandlance, Skirmish Bike, Colossus, rigged Ironclad Walker, Atlas Crane Mech, Bastion Siege Mech, and Surge Arc Tank FBXs (seven model sources) plus seven 128 × 128 card PNGs. The Ironclad and Atlas sources must import as Generic skeletal rigs with valid Avatars, a bound skinned mesh, and `Action_Idle`, `Action_Walk` or `Action_Drive`, `Action_FireRecoil`, and `Action_Death` clips. The builder keeps the native unit's visual root outside each overlay, creates unique canonical sockets and nested muzzle ParticleSystems, and drives the overlay Animator with `RCL_IsMoving`, `RCL_Fire`, and `RCL_Death`. It creates script-free visual prefabs, assigns Unity materials by FBX batch name, builds a bundle, reopens it, and checks renderers, materials, components, sockets, animation parameters, and card Sprites. It does not synthesize game balance or stage ScriptableObjects, AI behavior, maps, or native gameplay prefabs. Full game-native prefab authoring still requires serialized component references that Unity Editor could not create from the precompiled Assembly-CSharp.dll in the inspected scratch workflow. The separate visual bundle succeeds without attaching game scripts to these prefabs.

## PBR texture authoring

Blender 4.5.14 and the repository atlas bake remain the repeatable source for `<Name>_Albedo.png`, `<Name>_MetallicGloss.png`, `<Name>_Normal.png`, and `<Name>_Emission.png`. Material Maker 1.7 is the required external image-authoring tool for every mod texture pass; export texture images only and keep the Blender FBX authoritative. ArmorPaint remains optional. MetallicGloss uses metallic in RGB and smoothness (`1 - roughness`) in A, imported as linear data with alpha from input. Keep the mesh RGBA vertex-wear colors separate. Follow the [PBR texture workflow](../../docs/research/PBR-TEXTURE-WORKFLOW.md) and its authoring and validation skills. The builder may repair importer settings on rebuild, so inspect the effective Unity settings afterward.

## Build a pack's AssetBundles

1. Assign each content bundle's exact file name (for example, `sample-expansion.bundle`) as an AssetBundle label to its assets in the Project window. A pack can declare multiple bundles; references between them are resolved from Unity's generated manifest.
2. In Unity, choose **Rogue Command > Asset Bundles > Build Windows x64 for Content Pack...**.
3. Select the pack folder containing its `content.json`. The tool checks that the labels exactly match the declared `assetBundles` paths, rejects managed DLL assets, and builds chunk-compressed LZ4 bundles for `StandaloneWindows64` into the pack's `bundles/` directory. It copies Unity's generated manifest AssetBundle to the path declared by `assetBundleManifestPath` and checks the declared size limits.

The versioned `content/packs/sample-native-expansion/bundles/` files are the pack's previous build snapshot. Rebuild before validation or packaging with `tools/Build-CanonicalVisualModules.ps1`: it regenerates the 35 Level 2 FBXs with pinned Blender 4.5.14, bakes 28 shared four-map PBR sets with Material Maker 1.7, builds with Unity 2021.3.45f2, and stages fresh `sample-native-expansion` and `tactical-operations` outputs under ignored `artifacts/canonical-two-level/`. Its build manifest records the source fingerprint and bundle hashes; `tools/Package-Loader.ps1` rejects changed sources, stale staging, or modified bundles. The current canonical build contains the nine native-template unit/factory visuals and 19 Level 2 modules for `sample-native-expansion`, plus 16 Level 2 modules for `tactical-operations`. Unity import, bundle reopen, and reference validation are local evidence, not proof of in-game loading, animation, or rendering. The separate `sample-expansion` pack still has no authored gameplay bundle; its declared bundle and manifest files are absent, and earlier placeholder outputs under ignored `artifacts/unverified-authoring-output/2026-10-17/` are not installable.


The builder refuses to run from another Unity version. Do not ship bundles produced by a newer editor to this game build. The runtime's file-size limits bound the bytes it accepts from disk; they do not guarantee peak memory use for externally produced LZMA bundles, which Unity decompresses in memory. Use this builder's LZ4 output for authored packs.
