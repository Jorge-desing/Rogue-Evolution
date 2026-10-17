# Rogue Command Asset Authoring

This is a standalone Unity project for preparing Windows x64 AssetBundles used by the Rogue Command Expansion Loader. It is separate from the `Rogue Evolution` Unity Hub project.

## Add to Unity Hub

Choose the folder based on the Hub action you opened:

- **Add > Add project from disk:** select the project root folder `unity/RogueCommandAssetAuthoring` itself. It contains `Assets`, `Packages`, and `ProjectSettings/ProjectVersion.txt`.
- **Import Projects / scan a folder:** select the parent `unity`, which contains the `RogueCommandAssetAuthoring` project folder. This scan searches inside the selected parent; choosing the project folder itself makes the scan report that it found no projects.

Open it with Unity **2021.3.45f2**. The exact editor version is recorded in `ProjectSettings/ProjectVersion.txt` because the game uses that Unity release. Ensure **Windows Build Support (Mono)** is installed for this editor version.

The authoring project keeps local development copies of the game's managed DLLs under `Assets/Plugins/GameAssemblies/`. The scaffolder accepts only the verified `Assembly-CSharp.dll` SHA-256 and MVID and marks these DLLs Editor-only. The root `.gitignore` exclusion was checked with Git; `ignore.conf` contains matching Unity Version Control patterns, but no local Plastic CLI was available to verify their evaluation. These DLLs are development references only and are never bundled or distributed. No extracted commercial game assets are included.

The visual sample scaffolder imports the authored Sandlance, Skirmish Bike, and Colossus FBXs and three ImageGen-authored card PNGs. It creates script-free visual prefabs, assigns Unity materials by FBX batch name, builds a bundle, reopens it, and checks mesh renderers, materials, components, and 128 × 128 Sprite textures. It does not synthesize game balance or stage ScriptableObjects, AI behavior, maps, or native gameplay prefabs. Full game-native prefab authoring still requires serialized component references that Unity Editor could not create from the precompiled Assembly-CSharp.dll in the inspected scratch workflow. The separate visual bundle succeeds without attaching game scripts to these prefabs.

## Build a pack's AssetBundles

1. Assign each content bundle's exact file name (for example, `sample-expansion.bundle`) as an AssetBundle label to its assets in the Project window. A pack can declare multiple bundles; references between them are resolved from Unity's generated manifest.
2. In Unity, choose **Rogue Command > Asset Bundles > Build Windows x64 for Content Pack...**.
3. Select the pack folder containing its `content.json`. The tool checks that the labels exactly match the declared `assetBundles` paths, rejects managed DLL assets, and builds chunk-compressed LZ4 bundles for `StandaloneWindows64` into the pack's `bundles/` directory. It copies Unity's generated manifest AssetBundle to the path declared by `assetBundleManifestPath` and checks the declared size limits.

The visual sample builder writes regenerable build intermediates under the ignored artifacts directory, then copies the distributable sample-native-expansion.bundle and its manifest into content/packs/sample-native-expansion/bundles/. Those pack files are versioned. The separate sample-expansion package still has no authored full gameplay prefab bundle; earlier placeholder outputs remain under ignored artifacts/unverified-authoring-output/2026-10-17/ and are not installable.

The builder refuses to run from another Unity version. Do not ship bundles produced by a newer editor to this game build. The runtime's file-size limits bound the bytes it accepts from disk; they do not guarantee peak memory use for externally produced LZMA bundles, which Unity decompresses in memory. Use this builder's LZ4 output for authored packs.
