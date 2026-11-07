using System;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>Resolves a pack upgrade's localized name or description with an en-US fallback.</summary>
    public static class UpgradeLocalizationResolver
    {
        public static bool TryResolve(
            ContentUpgradeDefinition upgrade,
            string language,
            bool description,
            out string value)
        {
            value = null;
            if (upgrade == null || String.IsNullOrWhiteSpace(upgrade.id))
            {
                return false;
            }

            string translated = Find(upgrade.localizations, language, description);
            if (String.IsNullOrWhiteSpace(translated))
            {
                translated = Find(upgrade.localizations, "en-US", description);
            }

            if (String.IsNullOrWhiteSpace(translated))
            {
                return false;
            }

            value = translated;
            return true;
        }

        private static string Find(
            ContentUpgradeLocalizationDefinition[] localizations,
            string language,
            bool description)
        {
            if (localizations == null || String.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            for (int i = 0; i < localizations.Length; i++)
            {
                ContentUpgradeLocalizationDefinition localization = localizations[i];
                if (localization != null &&
                    String.Equals(localization.language, language, StringComparison.Ordinal))
                {
                    return description ? localization.description : localization.name;
                }
            }

            return null;
        }
    }
}
