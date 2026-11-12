using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    internal static class RogueCommandAssetBundleBuilder
    {
        private const string RequiredUnityVersion = "2021.3.45f2";
        private const long MaxManifestBundleBytes = 64L * 1024L * 1024L;
        private const long MaxAssetBundleBytes = 512L * 1024L * 1024L;
        private const long MaxTotalContentBundleBytes = 2L * 1024L * 1024L * 1024L;
        private static readonly Regex BundleNamePattern =
            new Regex(@"^[a-z0-9][a-z0-9._-]*\.bundle\z", RegexOptions.CultureInvariant);
        private static readonly Regex NamespacedIdPattern =
            new Regex(@"^[a-z][a-z0-9._-]*:[a-z0-9][a-z0-9._-]*\z", RegexOptions.CultureInvariant);

        [MenuItem("Rogue Command/Asset Bundles/Build Windows x64 for Content Pack...")]
        private static void BuildWindows64()
        {
            if (!String.Equals(Application.unityVersion, RequiredUnityVersion, StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("Unsupported Unity version",
                    "This project targets Unity " + RequiredUnityVersion + ". Current editor: " +
                    Application.unityVersion + ". Recreate bundles with the game's exact Unity version.", "OK");
                return;
            }

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64))
            {
                EditorUtility.DisplayDialog("Windows support is missing",
                    "Install the Windows Build Support (Mono) module for this Unity editor in Unity Hub.", "OK");
                return;
            }

            string[] bundleNames = AssetDatabase.GetAllAssetBundleNames();
            if (bundleNames == null || bundleNames.Length == 0)
            {
                EditorUtility.DisplayDialog("No AssetBundle labels",
                    "Assign at least one AssetBundle label to the content assets, then try again.", "OK");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string defaultPackRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "content", "packs",
                "sample-expansion"));
            if (!Directory.Exists(defaultPackRoot))
            {
                defaultPackRoot = projectRoot;
            }

            string packRoot = EditorUtility.OpenFolderPanel("Select the content pack folder", defaultPackRoot, "");
            if (String.IsNullOrEmpty(packRoot))
            {
                return;
            }

            string normalizedPackRoot;
            string validationError;
            if (!TryValidatePackageDirectory(packRoot, out normalizedPackRoot, out validationError))
            {
                EditorUtility.DisplayDialog("Unsafe content pack folder", validationError, "OK");
                return;
            }
            packRoot = normalizedPackRoot;

            ContentPackMetadata pack = ReadPackMetadata(packRoot);
            if (pack == null || pack.assetBundles == null || pack.assetBundles.Length == 0)
            {
                EditorUtility.DisplayDialog("Invalid content pack",
                    "The selected folder must contain a valid content.json with at least one assetBundles entry.",
                    "OK");
                return;
            }

            if (!ValidateBundleAssignments(bundleNames, pack, out validationError))
            {
                EditorUtility.DisplayDialog("AssetBundle labels do not match content.json", validationError, "OK");
                return;
            }

            string outputPath;
            if (!TryPrepareOutputDirectory(packRoot, out outputPath, out validationError))
            {
                EditorUtility.DisplayDialog("Unsafe AssetBundle output folder", validationError, "OK");
                return;
            }

            try
            {
                EnsureSafePackageTree(packRoot, outputPath);
                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(outputPath,
                    BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
                if (manifest == null)
                {
                    EditorUtility.DisplayDialog("AssetBundle build failed",
                        "Unity did not return an AssetBundle manifest. Check the Console for details.", "OK");
                    return;
                }

                EnsureSafePackageTree(packRoot, outputPath);
                string generatedManifestPath = Path.Combine(outputPath,
                    new DirectoryInfo(outputPath).Name);
                if (!File.Exists(generatedManifestPath))
                {
                    throw new FileNotFoundException(
                        "Unity returned an AssetBundleManifest but did not write its manifest bundle.",
                        generatedManifestPath);
                }

                ValidateBuiltBundleSizes(outputPath, bundleNames, generatedManifestPath);

                ValidateBuiltBundles(outputPath, bundleNames);

                string manifestFileName = pack.assetBundleManifestPath.Substring("bundles/".Length);
                string manifestDestination = Path.GetFullPath(Path.Combine(outputPath, manifestFileName));
                if (!IsStrictlyInside(packRoot, manifestDestination) ||
                    !IsStrictlyInside(outputPath, manifestDestination))
                {
                    throw new InvalidDataException("The generated manifest destination escapes the selected pack's bundles folder.");
                }

                EnsureSafePackageTree(packRoot, outputPath);
                File.Copy(generatedManifestPath, manifestDestination, true);

                AssetDatabase.Refresh();
                Debug.Log("Built " + bundleNames.Length + " Windows x64 AssetBundle(s) for pack '" +
                    pack.packId + "' at " + outputPath + "; copied manifest bundle to " + manifestDestination + ".");
                EditorUtility.RevealInFinder(outputPath);
                EditorUtility.DisplayDialog("AssetBundle build complete",
                    "Built " + bundleNames.Length + " content bundle(s) and the dependency manifest for " +
                    pack.packId + ".\n\n" + outputPath,
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("AssetBundle build failed", exception.Message, "OK");
            }
        }

        public static void BuildContentPacksByName(params string[] packNames)
        {
            if (!String.Equals(Application.unityVersion, RequiredUnityVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("AssetBundle builds require Unity " + RequiredUnityVersion +
                    "; current editor is " + Application.unityVersion + ".");
            }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64))
            {
                throw new InvalidOperationException("Windows Build Support (Mono) is required for this Unity editor.");
            }
            if (packNames == null || packNames.Length == 0)
            {
                throw new ArgumentException("At least one content pack name is required.", nameof(packNames));
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string repositoryRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
            ModelGeometryBudgetProfile.RequireCalibrated(repositoryRoot);
            string artifactRoot = Path.GetFullPath(Path.Combine(repositoryRoot,
                "artifacts", "canonical-two-level", "content-packs"));
            Directory.CreateDirectory(artifactRoot);
            string artifactDiagnostic;
            if (!HasNoReparsePoints(artifactRoot, out artifactDiagnostic))
            {
                throw new IOException("Unsafe canonical visual module artifact directory: " + artifactDiagnostic);
            }

            for (int packIndex = 0; packIndex < packNames.Length; packIndex++)
            {
                string packName = packNames[packIndex];
                if (String.IsNullOrWhiteSpace(packName) ||
                    !String.Equals(packName, Path.GetFileName(packName), StringComparison.Ordinal) ||
                    packName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    throw new InvalidDataException("Invalid content pack directory name: " + packName);
                }

                string packRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "content", "packs", packName));
                string rootDiagnostic;
                if (!TryValidatePackageDirectory(packRoot, out packRoot, out rootDiagnostic))
                {
                    throw new InvalidDataException("Unsafe content pack directory '" + packName + "': " + rootDiagnostic);
                }

                ContentPackMetadata pack = ReadPackMetadata(packRoot);
                if (pack == null || pack.assetBundles == null || pack.assetBundles.Length == 0)
                {
                    throw new InvalidDataException("Content pack '" + packName + "' has no valid AssetBundle declarations.");
                }

                var bundleNames = new string[pack.assetBundles.Length];
                for (int i = 0; i < pack.assetBundles.Length; i++)
                {
                    if (pack.assetBundles[i] == null || String.IsNullOrWhiteSpace(pack.assetBundles[i].path))
                    {
                        throw new InvalidDataException("Content pack '" + packName + "' has a null or empty bundle declaration.");
                    }
                    bundleNames[i] = Path.GetFileName(pack.assetBundles[i].path);
                }
                string validationError;
                if (!ValidateBundleAssignments(bundleNames, pack, out validationError))
                {
                    throw new InvalidDataException("AssetBundle assignments for '" + packName + "' are invalid: " + validationError);
                }

                string outputPath = Path.GetFullPath(Path.Combine(artifactRoot, packName, "bundles"));
                if (!IsStrictlyInside(artifactRoot, outputPath))
                {
                    throw new InvalidDataException("AssetBundle output folder escapes the canonical visual module artifacts directory.");
                }
                Directory.CreateDirectory(outputPath);

                EnsureSafeArtifactTree(artifactRoot, outputPath);
                var buildDefinitions = new AssetBundleBuild[bundleNames.Length];
                for (int i = 0; i < bundleNames.Length; i++)
                {
                    buildDefinitions[i] = new AssetBundleBuild
                    {
                        assetBundleName = bundleNames[i],
                        assetNames = AssetDatabase.GetAssetPathsFromAssetBundle(bundleNames[i])
                    };
                }

                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(outputPath, buildDefinitions,
                    BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                    BuildTarget.StandaloneWindows64);
                if (manifest == null)
                {
                    throw new InvalidDataException("Unity did not return an AssetBundleManifest for '" + packName + "'.");
                }

                EnsureSafeArtifactTree(artifactRoot, outputPath);
                string generatedManifestPath = Path.Combine(outputPath, new DirectoryInfo(outputPath).Name);
                if (!File.Exists(generatedManifestPath))
                {
                    throw new FileNotFoundException("Unity did not write its generated AssetBundleManifest.", generatedManifestPath);
                }
                ValidateBuiltBundleSizes(outputPath, bundleNames, generatedManifestPath);
                ValidateBuiltBundles(outputPath, bundleNames);

                string manifestFileName = pack.assetBundleManifestPath.Substring("bundles/".Length);
                string manifestDestination = Path.GetFullPath(Path.Combine(outputPath, manifestFileName));
                if (!IsStrictlyInside(artifactRoot, manifestDestination) || !IsStrictlyInside(outputPath, manifestDestination))
                {
                    throw new InvalidDataException("Generated AssetBundleManifest destination escapes the canonical visual module artifacts directory.");
                }
                EnsureSafeArtifactTree(artifactRoot, outputPath);
                File.Copy(generatedManifestPath, manifestDestination, true);
                Debug.Log("[RogueCommandAssetBundleBuilder] Built and reopened " + bundleNames.Length +
                    " bundle(s) for '" + pack.packId + "' at " + outputPath + " (artifact output).");
            }

            AssetDatabase.Refresh();
        }

        private static bool TryValidatePackageDirectory(
            string selectedPath,
            out string packRoot,
            out string diagnostic)
        {
            packRoot = null;
            diagnostic = null;
            try
            {
                packRoot = Path.GetFullPath(selectedPath);
                if (!Directory.Exists(packRoot))
                {
                    diagnostic = "The selected content pack folder does not exist.";
                    return false;
                }

                if (!HasNoReparsePoints(packRoot, out diagnostic))
                {
                    packRoot = null;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "Could not safely resolve the selected content pack folder: " + exception.Message;
                packRoot = null;
                return false;
            }
        }

        private static bool TryPrepareOutputDirectory(
            string packRoot,
            out string outputPath,
            out string diagnostic)
        {
            outputPath = null;
            diagnostic = null;
            try
            {
                outputPath = Path.GetFullPath(Path.Combine(packRoot, "bundles"));
                if (!IsStrictlyInside(packRoot, outputPath))
                {
                    diagnostic = "The AssetBundle output folder must remain inside the selected package.";
                    outputPath = null;
                    return false;
                }

                if (File.Exists(outputPath) && !Directory.Exists(outputPath))
                {
                    diagnostic = "A file already occupies the package's bundles folder path.";
                    outputPath = null;
                    return false;
                }

                if (Directory.Exists(outputPath) && !HasNoReparsePoints(packRoot, out diagnostic))
                {
                    outputPath = null;
                    return false;
                }

                Directory.CreateDirectory(outputPath);
                if (!HasNoReparsePoints(packRoot, out diagnostic))
                {
                    outputPath = null;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "Could not safely prepare the AssetBundle output folder: " + exception.Message;
                outputPath = null;
                return false;
            }
        }

        private static void EnsureSafePackageTree(string packRoot, string outputPath)
        {
            string canonicalOutput = Path.GetFullPath(outputPath);
            if (!IsStrictlyInside(packRoot, canonicalOutput))
            {
                throw new InvalidDataException("AssetBundle output is outside the selected content pack folder.");
            }

            string diagnostic;
            if (!HasNoReparsePoints(packRoot, out diagnostic))
            {
                throw new IOException(diagnostic);
            }
        }

        private static void EnsureSafeArtifactTree(string artifactRoot, string outputPath)
        {
            string canonicalOutput = Path.GetFullPath(outputPath);
            if (!IsStrictlyInside(artifactRoot, canonicalOutput))
            {
                throw new InvalidDataException("AssetBundle output is outside the canonical visual module artifacts directory.");
            }

            string diagnostic;
            if (!HasNoReparsePoints(artifactRoot, out diagnostic))
            {
                throw new IOException(diagnostic);
            }
        }

        private static bool HasNoReparsePoints(string directoryPath, out string diagnostic)
        {
            diagnostic = null;
            string fullDirectory = Path.GetFullPath(directoryPath);
            string volumeRoot = Path.GetPathRoot(fullDirectory);
            if (String.IsNullOrEmpty(volumeRoot))
            {
                diagnostic = "The selected content pack folder has no resolvable volume root.";
                return false;
            }

            string current = volumeRoot;
            try
            {
                RejectReparseDirectory(current);
                string relativeDirectory = fullDirectory.Substring(volumeRoot.Length);
                string[] segments = relativeDirectory.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < segments.Length; i++)
                {
                    current = Path.Combine(current, segments[i]);
                    RejectReparseDirectory(current);
                }

                var pendingDirectories = new Stack<string>();
                pendingDirectories.Push(fullDirectory);
                while (pendingDirectories.Count > 0)
                {
                    string parent = pendingDirectories.Pop();
                    foreach (string entry in Directory.EnumerateFileSystemEntries(parent))
                    {
                        FileAttributes attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            diagnostic = "Content pack paths cannot contain a symbolic link, junction, or other reparse point: " + entry;
                            return false;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pendingDirectories.Push(entry);
                        }
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "Could not verify that the content pack contains no reparse points: " + exception.Message;
                return false;
            }
        }

        private static void RejectReparseDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException("A directory in the selected package path does not exist: " + path);
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Selected content pack paths cannot traverse a symbolic link or junction: " + path);
            }
        }

        private static bool IsStrictlyInside(string directory, string candidate)
        {
            string normalizedDirectory = Path.GetFullPath(directory).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedCandidate = Path.GetFullPath(candidate);
            string prefix = normalizedDirectory + Path.DirectorySeparatorChar;
            return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static ContentPackMetadata ReadPackMetadata(string packRoot)
        {
            string manifestPath = Path.Combine(packRoot, "content.json");
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<ContentPackMetadata>(File.ReadAllText(manifestPath));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not read content pack manifest: " + exception.Message);
                return null;
            }
        }

        private static bool ValidateBundleAssignments(
            string[] bundleNames,
            ContentPackMetadata pack,
            out string diagnostic)
        {
            if (pack.schemaVersion != 1)
            {
                diagnostic = "The content pack schemaVersion must be 1.";
                return false;
            }

            if (String.IsNullOrWhiteSpace(pack.packId) || !NamespacedIdPattern.IsMatch(pack.packId))
            {
                diagnostic = "content.json must declare a namespaced packId.";
                return false;
            }

            if (String.IsNullOrWhiteSpace(pack.assetBundleManifestPath))
            {
                diagnostic = "content.json must declare assetBundleManifestPath for the generated Unity manifest bundle.";
                return false;
            }

            if (pack.assetBundles.Length > 128)
            {
                diagnostic = "A content pack can declare at most 128 AssetBundles.";
                return false;
            }

            string normalizedManifestPath = pack.assetBundleManifestPath;
            if (normalizedManifestPath.IndexOf('\\') >= 0)
            {
                diagnostic = "assetBundleManifestPath must use forward slashes.";
                return false;
            }

            if (!normalizedManifestPath.StartsWith("bundles/", StringComparison.Ordinal) ||
                normalizedManifestPath.IndexOf("/", "bundles/".Length, StringComparison.Ordinal) >= 0)
            {
                diagnostic = "assetBundleManifestPath must use the form 'bundles/<manifest-name>.bundle'.";
                return false;
            }

            string manifestFileName = normalizedManifestPath.Substring("bundles/".Length);
            if (!BundleNamePattern.IsMatch(manifestFileName) || IsReservedWindowsDeviceName(manifestFileName))
            {
                diagnostic = "assetBundleManifestPath must name a lowercase .bundle file directly inside bundles/.";
                return false;
            }

            var declaredNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            var declaredIds = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            declaredIds.Add(pack.packId);
            for (int i = 0; i < pack.assetBundles.Length; i++)
            {
                ContentBundleMetadata bundle = pack.assetBundles[i];
                if (bundle == null || String.IsNullOrWhiteSpace(bundle.path))
                {
                    diagnostic = "Every assetBundles entry must have a path.";
                    return false;
                }

                if (String.IsNullOrWhiteSpace(bundle.id) || !NamespacedIdPattern.IsMatch(bundle.id) ||
                    !declaredIds.Add(bundle.id))
                {
                    diagnostic = "The pack ID and every assetBundles entry must have unique namespaced IDs.";
                    return false;
                }

                string normalizedPath = bundle.path;
                if (normalizedPath.IndexOf('\\') >= 0)
                {
                    diagnostic = "Bundle paths must use forward slashes.";
                    return false;
                }
                if (!normalizedPath.StartsWith("bundles/", StringComparison.Ordinal) ||
                    normalizedPath.IndexOf("/", "bundles/".Length, StringComparison.Ordinal) >= 0)
                {
                    diagnostic = "Bundle paths must use the form 'bundles/<bundle-name>.bundle'.";
                    return false;
                }

                string fileName = normalizedPath.Substring("bundles/".Length);
                if (!BundleNamePattern.IsMatch(fileName) || IsReservedWindowsDeviceName(fileName) ||
                    !declaredNames.Add(fileName))
                {
                    diagnostic = "Bundle paths must have unique lowercase .bundle file names.";
                    return false;
                }
            }

            if (declaredNames.Contains(manifestFileName))
            {
                diagnostic = "assetBundleManifestPath must be different from every content bundle path.";
                return false;
            }

            var assignedNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < bundleNames.Length; i++)
            {
                string bundleName = bundleNames[i];
                if (!BundleNamePattern.IsMatch(bundleName) || IsReservedWindowsDeviceName(bundleName))
                {
                    diagnostic = "Invalid AssetBundle label '" + bundleName +
                        "'. Use a lowercase file name ending in .bundle, with no path separators.";
                    return false;
                }

                string[] assets = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
                if (assets == null || assets.Length == 0)
                {
                    diagnostic = "AssetBundle label '" + bundleName + "' does not contain any assets.";
                    return false;
                }

                for (int assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                {
                    if (String.Equals(Path.GetExtension(assets[assetIndex]), ".dll",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        diagnostic = "Managed assemblies cannot be shipped in content bundles: " + assets[assetIndex];
                        return false;
                    }
                }

                if (!declaredNames.Contains(bundleName) || !assignedNames.Add(bundleName))
                {
                    diagnostic = "AssetBundle label '" + bundleName +
                        "' must match exactly one assetBundles path in content.json.";
                    return false;
                }
            }

            foreach (string declaredName in declaredNames)
            {
                if (!assignedNames.Contains(declaredName))
                {
                    diagnostic = "content.json declares '" + declaredName +
                        "' but no AssetBundle label with that name exists in the project.";
                    return false;
                }
            }

            diagnostic = null;
            return true;
        }

        private static void ValidateBuiltBundleSizes(
            string outputPath,
            string[] bundleNames,
            string generatedManifestPath)
        {
            var manifestInfo = new FileInfo(generatedManifestPath);
            if (manifestInfo.Length <= 0 || manifestInfo.Length > MaxManifestBundleBytes)
            {
                throw new InvalidDataException("The generated Unity manifest bundle is empty or exceeds " +
                    MaxManifestBundleBytes + " bytes.");
            }

            long totalBytes = 0;
            for (int i = 0; i < bundleNames.Length; i++)
            {
                string bundlePath = Path.Combine(outputPath, bundleNames[i]);
                var bundleInfo = new FileInfo(bundlePath);
                if (!bundleInfo.Exists || bundleInfo.Length <= 0 || bundleInfo.Length > MaxAssetBundleBytes)
                {
                    throw new InvalidDataException("Built bundle '" + bundleNames[i] +
                        "' is missing, empty, or exceeds " + MaxAssetBundleBytes + " bytes.");
                }

                if (totalBytes > MaxTotalContentBundleBytes - bundleInfo.Length)
                {
                    throw new InvalidDataException("The content pack exceeds the total AssetBundle limit of " +
                        MaxTotalContentBundleBytes + " bytes.");
                }

                totalBytes += bundleInfo.Length;
            }
        }

        /// <summary>
        /// Reopens each built bundle and logs asset counts/sizes, rejecting
        /// bundles that fail to load or contain zero assets. Mirrors the
        /// post-build validation used by the Tactical/Visual scaffolders.
        /// </summary>
        private static void ValidateBuiltBundles(string outputPath, string[] bundleNames)
        {
            if (bundleNames == null) return;
            for (int i = 0; i < bundleNames.Length; i++)
            {
                string bundlePath = Path.Combine(outputPath, bundleNames[i]);
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    throw new InvalidDataException("Built bundle could not be reopened: " + bundlePath);
                }
                try
                {
                    string[] assetNames = bundle.GetAllAssetNames();
                    if (assetNames == null || assetNames.Length == 0)
                    {
                        throw new InvalidDataException("Built bundle contains no assets: " + bundleNames[i]);
                    }
                    long bytes = new FileInfo(bundlePath).Length;
                    Debug.Log("[RogueCommandAssetBundleBuilder] Built bundle '" + bundleNames[i] + "' (" +
                        assetNames.Length + " assets, " + bytes + " bytes).");
                }
                finally
                {
                    bundle.Unload(true);
                }
            }
        }

        private static bool IsReservedWindowsDeviceName(string fileName)
        {
            int dotIndex = fileName.IndexOf('.');
            string stem = (dotIndex >= 0 ? fileName.Substring(0, dotIndex) : fileName).ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL")
            {
                return true;
            }

            return stem.Length == 4 &&
                (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                stem[3] >= '1' && stem[3] <= '9';
        }

        [Serializable]
        private sealed class ContentPackMetadata
        {
            public int schemaVersion;
            public string packId;
            public string assetBundleManifestPath;
            public ContentBundleMetadata[] assetBundles;
        }

        [Serializable]
        private sealed class ContentBundleMetadata
        {
            public string id;
            public string path;
        }
    }
}
