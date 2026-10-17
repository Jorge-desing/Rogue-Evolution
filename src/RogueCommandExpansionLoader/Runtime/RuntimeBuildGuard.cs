using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Fail-closed compatibility gate for the statically analyzed Rogue Command build.
    /// The caller supplies the game version detected by the loader; the game's executable file
    /// version is a Unity engine version and cannot be used as the Rogue Command version.
    /// </summary>
    public static class RuntimeBuildGuard
    {
        public const string ExpectedGameVersion = "1.0.9";
        public const string ExpectedAssemblySha256 =
            "d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b";
        public static readonly Guid ExpectedModuleVersionId =
            new Guid("db0e9462-128d-4296-9900-de18b4bd0c00");

        public static bool TryValidate(
            string gameRoot,
            string detectedGameVersion,
            Assembly gameAssembly,
            out string failureReason)
        {
            failureReason = null;

            if (gameAssembly == null)
            {
                failureReason = "Assembly-CSharp is not loaded.";
                return false;
            }

            if (!String.Equals(NormalizeVersion(detectedGameVersion), ExpectedGameVersion,
                StringComparison.Ordinal))
            {
                failureReason = "Expected Rogue Command " + ExpectedGameVersion +
                    "; loader reported '" + (detectedGameVersion ?? "<null>") + "'.";
                return false;
            }

            if (!String.Equals(gameAssembly.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal))
            {
                failureReason = "The supplied game assembly is not Assembly-CSharp.";
                return false;
            }

            if (gameAssembly.ManifestModule.ModuleVersionId != ExpectedModuleVersionId)
            {
                failureReason = "Assembly-CSharp MVID does not match the analyzed Rogue Command build.";
                return false;
            }

            string assemblyPath;
            try
            {
                assemblyPath = gameAssembly.Location;
            }
            catch (Exception exception)
            {
                failureReason = "Could not read Assembly-CSharp location: " + exception.Message;
                return false;
            }

            if (String.IsNullOrEmpty(gameRoot) || String.IsNullOrEmpty(assemblyPath))
            {
                failureReason = "Game root or Assembly-CSharp location is empty.";
                return false;
            }

            string fullRoot;
            string fullAssemblyPath;
            try
            {
                fullRoot = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                fullAssemblyPath = Path.GetFullPath(assemblyPath);
            }
            catch (Exception exception)
            {
                failureReason = "Could not normalize game paths: " + exception.Message;
                return false;
            }

            if (!IsInsideDirectory(fullRoot, fullAssemblyPath))
            {
                failureReason = "Assembly-CSharp is outside the supplied game installation.";
                return false;
            }

            if (!File.Exists(fullAssemblyPath))
            {
                failureReason = "Assembly-CSharp file is missing from the supplied installation.";
                return false;
            }

            string actualHash;
            try
            {
                using (FileStream stream = File.OpenRead(fullAssemblyPath))
                using (SHA256 sha256 = SHA256.Create())
                {
                    actualHash = ToLowerHex(sha256.ComputeHash(stream));
                }
            }
            catch (Exception exception)
            {
                failureReason = "Could not hash Assembly-CSharp: " + exception.Message;
                return false;
            }

            if (!String.Equals(actualHash, ExpectedAssemblySha256, StringComparison.Ordinal))
            {
                failureReason = "Assembly-CSharp SHA-256 does not match the analyzed Rogue Command build.";
                return false;
            }

            return true;
        }

        private static string NormalizeVersion(string version)
        {
            if (String.IsNullOrWhiteSpace(version))
            {
                return String.Empty;
            }

            string normalized = version.Trim();
            if (normalized.StartsWith("Version ", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("Version ".Length).Trim();
            }
            else if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(1).Trim();
            }

            return normalized;
        }

        private static bool IsInsideDirectory(string directory, string candidate)
        {
            string prefix = directory + Path.DirectorySeparatorChar;
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(candidate, directory, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToLowerHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
