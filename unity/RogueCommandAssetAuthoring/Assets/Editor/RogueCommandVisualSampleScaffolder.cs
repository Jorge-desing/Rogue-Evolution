using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    /// <summary>
    /// Builds a script-free sample model bundle that can be overlaid onto a native unit prefab.
    /// </summary>
    internal static class RogueCommandVisualSampleScaffolder
    {
        private const string RequiredUnityVersion = "2021.3.45f2";
        private const string AssetRoot = "Assets/RogueCommandExpansionSample";
        private const string PrefabPath = AssetRoot + "/ScoutTankVisual.prefab";
        private const string MeshPath = AssetRoot + "/ScoutTankVisualCube.asset";
        private const string BundleOutputPath = AssetRoot + "/BundleOutput";
        private const string BundleName = "sample-native-expansion.bundle";

        [MenuItem("Rogue Command/Asset Bundles/Build Sample Native Visual Pack")]
        public static void BuildSampleNativeExpansion()
        {
            try
            {
                if (!String.Equals(Application.unityVersion, RequiredUnityVersion, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("This sample must be built with Unity " +
                        RequiredUnityVersion + "; current Editor is " + Application.unityVersion + ".");
                }

                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,
                    BuildTarget.StandaloneWindows64))
                {
                    throw new InvalidOperationException(
                        "Install the Unity Windows Build Support module before building this sample.");
                }

                string prefabPath = CreateSampleVisual();
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string outputPath = Path.GetFullPath(Path.Combine(projectRoot, BundleOutputPath));
                Directory.CreateDirectory(outputPath);

                var build = new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = new[] { prefabPath }
                };
                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(outputPath,
                    new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
                    BuildTarget.StandaloneWindows64);
                if (manifest == null)
                {
                    throw new InvalidOperationException("Unity did not produce an AssetBundle manifest.");
                }

                string[] bundleNames = manifest.GetAllAssetBundles();
                if (bundleNames == null || bundleNames.Length != 1 ||
                    !String.Equals(bundleNames[0], BundleName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Unity's AssetBundle manifest does not contain exactly " +
                        BundleName + ".");
                }

                ValidateBuiltVisualBundle(Path.Combine(outputPath, BundleName));

                string packRoot = ResolvePackRoot(projectRoot);
                string packBundles = Path.Combine(packRoot, "bundles");
                Directory.CreateDirectory(packBundles);
                CopyRequiredFile(Path.Combine(outputPath, BundleName),
                    Path.Combine(packBundles, BundleName));
                string generatedManifest = Path.Combine(outputPath, new DirectoryInfo(outputPath).Name);
                CopyRequiredFile(generatedManifest,
                    Path.Combine(packBundles, "assetbundle-manifest.bundle"));

                AssetDatabase.Refresh();
                Debug.Log("Built the script-free " + BundleName + " visual overlay for sample-native:expansion at " +
                    packBundles + ". The game controller remains on the native unit prefab.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
                else
                {
                    EditorUtility.DisplayDialog("Sample visual bundle build failed", exception.Message, "OK");
                }
            }
        }

        private static string CreateSampleVisual()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, AssetRoot)));
            AssetDatabase.Refresh();

            Mesh cube = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (cube == null)
            {
                GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    cube = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
                    cube.name = "ScoutTankVisualCube";
                    AssetDatabase.CreateAsset(cube, MeshPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(primitive);
                }
            }

            var root = new GameObject("ScoutTankVisual");
            try
            {
                AddBox(root.transform, cube, "Hull", new Vector3(0f, 0.45f, 0f),
                    new Vector3(1.8f, 0.45f, 2.4f));
                AddBox(root.transform, cube, "Turret", new Vector3(0f, 0.82f, -0.1f),
                    new Vector3(0.95f, 0.36f, 0.95f));
                AddBox(root.transform, cube, "Cannon", new Vector3(0f, 0.84f, 0.8f),
                    new Vector3(0.18f, 0.18f, 1.35f));
                AddBox(root.transform, cube, "LeftTrack", new Vector3(-0.98f, 0.32f, 0f),
                    new Vector3(0.32f, 0.42f, 2.25f));
                AddBox(root.transform, cube, "RightTrack", new Vector3(0.98f, 0.32f, 0f),
                    new Vector3(0.32f, 0.42f, 2.25f));

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Unity could not save " + PrefabPath + ".");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetImporter importer = AssetImporter.GetAtPath(PrefabPath);
            if (importer == null)
            {
                throw new FileNotFoundException("Unity did not import " + PrefabPath + ".");
            }

            importer.assetBundleName = BundleName;
            importer.assetBundleVariant = String.Empty;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return PrefabPath;
        }

        private static void ValidateBuiltVisualBundle(string bundlePath)
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                throw new InvalidDataException("Unity could not reopen the sample visual AssetBundle.");
            }

            try
            {
                string[] assetNames = bundle.GetAllAssetNames();
                string prefabAssetPath = null;
                for (int i = 0; i < assetNames.Length; i++)
                {
                    if (assetNames[i].EndsWith("/scouttankvisual.prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        prefabAssetPath = assetNames[i];
                        break;
                    }
                }

                if (prefabAssetPath == null)
                {
                    throw new InvalidDataException("The sample visual prefab is not listed in its AssetBundle.");
                }

                GameObject prefab = bundle.LoadAsset<GameObject>(prefabAssetPath);
                if (prefab == null || !String.Equals(prefab.name, "ScoutTankVisual", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The sample visual prefab could not be loaded by its AssetBundle path.");
                }

                Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    throw new InvalidDataException("The sample visual prefab has no renderers.");
                }

                Component[] components = prefab.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] == null || components[i] is MonoBehaviour)
                    {
                        throw new InvalidDataException("The sample visual bundle contains a missing or scripted component.");
                    }
                }
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static void AddBox(
            Transform parent,
            Mesh cube,
            string name,
            Vector3 localPosition,
            Vector3 localScale)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.AddComponent<MeshFilter>().sharedMesh = cube;
            part.AddComponent<MeshRenderer>();
        }

        private static string ResolvePackRoot(string projectRoot)
        {
            string configuredRoot = Environment.GetEnvironmentVariable("RCL_PACK_ROOT");
            string packRoot = String.IsNullOrWhiteSpace(configuredRoot)
                ? Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "content", "packs",
                    "sample-native-expansion"))
                : Path.GetFullPath(configuredRoot);
            if (!Directory.Exists(packRoot) || !File.Exists(Path.Combine(packRoot, "content.json")))
            {
                throw new DirectoryNotFoundException("The sample-native-expansion pack was not found at " +
                    packRoot + ". Set RCL_PACK_ROOT to its folder when running this command from a clone.");
            }

            return packRoot;
        }

        private static void CopyRequiredFile(string source, string destination)
        {
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Unity did not write the expected AssetBundle output.", source);
            }

            File.Copy(source, destination, true);
        }
    }
}
