using System.Text;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Clean-room recreation of the RCM GUI behavior that assigns a stable,
    /// categorical accent color to each textual identifier (pack id, unit id, ...).
    /// The original mod derives the color by hashing the identifier and selecting
    /// one entry from a fixed palette; this loader reproduces that observable
    /// contract independently, without reusing the original code, assets, or the
    /// original palette values.
    /// </summary>
    /// <remarks>
    /// The hash is the public-domain FNV-1a 32-bit algorithm (offset basis
    /// 0x811C9DC5, prime 0x01000193), which is a well-known standard and not a
    /// proprietary detail of the original mod. The palette below is authored
    /// independently for this loader. Output is deterministic for a given
    /// identifier so the same pack/unit keeps a consistent accent color across
    /// sessions.
    /// </remarks>
    public static class IdentifierColorPolicy
    {
        private const uint FnvOffsetBasis = 0x811C9DC5u;
        private const uint FnvPrime = 0x01000193u;

        // Number of entries must be a power of two so the mask below distributes
        // indices evenly across the whole palette.
        private static readonly UnityEngine.Color[] Palette = BuildPalette();

        public static int PaletteSize => Palette.Length;

        /// <summary>
        /// The accent color at a palette index, readable by an agent without a visual capture.
        /// Throws when the index is outside the palette.
        /// </summary>
        public static UnityEngine.Color PaletteColorAt(int index)
        {
            if (index < 0 || index >= Palette.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }

            return Palette[index];
        }

        /// <summary>
        /// FNV-1a 32-bit hash of the identifier's UTF-8 bytes. Pure and total.
        /// </summary>
        public static uint Hash(string identifier)
        {
            uint hash = FnvOffsetBasis;
            if (string.IsNullOrEmpty(identifier))
            {
                return hash;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(identifier);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= FnvPrime;
            }

            return hash;
        }

        /// <summary>
        /// Deterministic palette index in [0, PaletteSize) for the identifier.
        /// </summary>
        public static int PaletteIndex(string identifier)
        {
            return (int)(Hash(identifier) & (uint)(Palette.Length - 1));
        }

        /// <summary>
        /// Resolves the stable accent color assigned to the identifier.
        /// </summary>
        public static UnityEngine.Color Resolve(string identifier)
        {
            return Palette[PaletteIndex(identifier)];
        }

        private static UnityEngine.Color[] BuildPalette()
        {
            return new UnityEngine.Color[]
            {
                new UnityEngine.Color(0.90f, 0.30f, 0.25f), // warm red
                new UnityEngine.Color(0.95f, 0.55f, 0.18f), // orange
                new UnityEngine.Color(0.98f, 0.80f, 0.25f), // amber
                new UnityEngine.Color(0.55f, 0.75f, 0.25f), // olive
                new UnityEngine.Color(0.30f, 0.70f, 0.40f), // green
                new UnityEngine.Color(0.25f, 0.75f, 0.78f), // teal
                new UnityEngine.Color(0.28f, 0.60f, 0.92f), // blue
                new UnityEngine.Color(0.45f, 0.45f, 0.95f), // indigo
                new UnityEngine.Color(0.72f, 0.45f, 0.92f), // violet
                new UnityEngine.Color(0.90f, 0.45f, 0.78f), // pink
                new UnityEngine.Color(0.82f, 0.62f, 0.42f), // tan
                new UnityEngine.Color(0.62f, 0.64f, 0.66f), // grey
                new UnityEngine.Color(0.96f, 0.42f, 0.42f), // coral
                new UnityEngine.Color(0.40f, 0.82f, 0.80f), // aqua
                new UnityEngine.Color(0.98f, 0.68f, 0.45f), // peach
                new UnityEngine.Color(0.78f, 0.82f, 0.38f), // lime
            };
        }
    }
}
