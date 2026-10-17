using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace RogueCommandExpansionLoader.Compatibility
{
    internal static class GameCompatibility
    {
        internal const string SupportedGameVersion = "1.0.9";
        internal const string SupportedUnityVersion = "2021.3.45f2";
        internal const string SupportedAssemblySha256 = "d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b";

        internal static bool TryValidate(string gameRoot, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(gameRoot))
            {
                diagnostic = "The game root path is empty.";
                return false;
            }

            if (IntPtr.Size != 8)
            {
                diagnostic = "The loader requires the 64-bit Unity Mono process.";
                return false;
            }

            if (!string.Equals(Application.unityVersion, SupportedUnityVersion,
                StringComparison.Ordinal))
            {
                diagnostic = "Unsupported Unity version. Expected " + SupportedUnityVersion +
                    "; found " + Application.unityVersion + ".";
                return false;
            }

            string versionPath = Path.Combine(gameRoot, "version.txt");
            if (!File.Exists(versionPath))
            {
                diagnostic = "version.txt was not found; refusing to patch an unidentified game build.";
                return false;
            }

            string versionText;
            try
            {
                versionText = File.ReadAllText(versionPath);
            }
            catch (Exception exception)
            {
                diagnostic = "Could not read version.txt: " + exception.Message;
                return false;
            }

            bool versionMatches = false;
            string[] lines = versionText.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.Equals(lines[i].Trim(), "9= Version " + SupportedGameVersion, StringComparison.Ordinal))
                {
                    versionMatches = true;
                    break;
                }
            }

            if (!versionMatches)
            {
                diagnostic = "Unsupported Rogue Command version marker. Expected " + SupportedGameVersion + ".";
                return false;
            }

            string assemblyPath = Path.Combine(gameRoot, "Rogue Command_Data", "Managed", "Assembly-CSharp.dll");
            if (!File.Exists(assemblyPath))
            {
                diagnostic = "Assembly-CSharp.dll was not found; refusing to install Harmony patches.";
                return false;
            }

            string actualHash;
            try
            {
                using (var stream = File.OpenRead(assemblyPath))
                using (var sha256 = SHA256.Create())
                {
                    actualHash = ToLowerHex(sha256.ComputeHash(stream));
                }
            }
            catch (Exception exception)
            {
                diagnostic = "Could not hash Assembly-CSharp.dll: " + exception.Message;
                return false;
            }

            if (!string.Equals(actualHash, SupportedAssemblySha256, StringComparison.Ordinal))
            {
                diagnostic = "Assembly-CSharp.dll SHA-256 mismatch. Expected " + SupportedAssemblySha256 + "; found " + actualHash + ". Runtime patches are disabled until this build is re-verified.";
                return false;
            }

            return true;
        }

        private static string ToLowerHex(byte[] value)
        {
            var chars = new char[value.Length * 2];
            const string hex = "0123456789abcdef";
            for (int i = 0; i < value.Length; i++)
            {
                chars[i * 2] = hex[value[i] >> 4];
                chars[(i * 2) + 1] = hex[value[i] & 0x0f];
            }

            return new string(chars);
        }
    }
}
