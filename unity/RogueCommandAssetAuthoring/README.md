# Rogue Command Asset Authoring

This is a standalone Unity project for preparing Windows x64 AssetBundles used by the Rogue Command Expansion Loader. It is separate from the `Rogue Evolution` Unity Hub project.

## Add to Unity Hub

Choose the folder based on the Hub action you opened:

- **Add > Add project from disk:** select the project root folder `unity/RogueCommandAssetAuthoring` itself. It contains `Assets`, `Packages`, and `ProjectSettings/ProjectVersion.txt`.
- **Import Projects / scan a folder:** select the parent `unity`, which contains the `RogueCommandAssetAuthoring` project folder. This scan searches inside the selected parent; choosing the project folder itself makes the scan report that it found no projects.

Open it with Unity **2021.3.45f2**. The exact editor version is recorded in `ProjectSettings/ProjectVersion.txt` because the game uses that Unity release. Ensure **Windows Build Support (Mono)** is installed for this editor version.

The authoring project keeps local development copies of the game's managed DLLs under `Assets/Plugins/GameAssemblies/`. The scaffolder accepts only the verified `Assembly-CSharp.dll` SHA-256 and MVID and marks these DLLs Editor-only. The root `.gitignore` exclusion was checked with Git; `ignore.conf` contains matching Unity Version Control patterns, but no local Plastic CLI was available to verify their evaluation. These DLLs are development references only and are never bundled or distributed. No extracted commercial game assets are included.

The sample scaffolder creates the sample card Sprite but does not synthesize balance/stage ScriptableObjects or unit, AI, and map prefabs. It requires complete game-native balance and stage templates plus authored prefabs that preserve Rogue Command's native component graphs and references. See `Assets/README.md` for those requirements. In the tested Unity 2021.3.45f2 scratch project, importing the verified assembly plus its managed dependencies still did not let the Editor attach `EntityController`; the preflight now fails with a nonzero batch exit before writing assets or bundles. A valid sample bundle remains blocked on a prefab pipeline Unity can serialize.

## Build a pack's AssetBundles

1. Assign each content bundle's exact file name (for example, `sample-expansion.bundle`) as an AssetBundle label to its assets in the Project window. A pack can declare multiple bundles; references between them are resolved from Unity's generated manifest.
2. In Unity, choose **Rogue Command > Asset Bundles > Build Windows x64 for Content Pack...**.
3. Select the pack folder containing its `content.json`. The tool checks that the labels exactly match the declared `assetBundles` paths, rejects managed DLL assets, and builds chunk-compressed LZ4 bundles for `StandaloneWindows64` into the pack's `bundles/` directory. It copies Unity's generated manifest AssetBundle to the path declared by `assetBundleManifestPath` and checks the declared size limits.

Bundle outputs are generated under the repository's `content/packs/` tree and are ignored by Git. The sample manifest expects `content/packs/sample-expansion/bundles/sample-expansion.bundle`; no bundle is currently present there. Earlier placeholder prefabs and tiny false-success bundles are preserved under the ignored `artifacts/unverified-authoring-output/2026-10-17/` audit folder, outside the Unity project and pack directory.

The builder refuses to run from another Unity version. Do not ship bundles produced by a newer editor to this game build. The runtime's file-size limits bound the bytes it accepts from disk; they do not guarantee peak memory use for externally produced LZMA bundles, which Unity decompresses in memory. Use this builder's LZ4 output for authored packs.
