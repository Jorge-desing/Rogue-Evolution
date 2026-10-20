using System;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>Resolves a pack relic's localized name or description with an en-US fallback.</summary>
    public static class RelicLocalizationResolver
    {
        public static bool TryResolve(
            ContentRelicDefinition relic,
            string language,
            bool description,
            out string value)
        {
            value = null;
            if (relic == null || String.IsNullOrWhiteSpace(relic.id))
            {
                return false;
            }

            string translated = Find(relic.localizations, language, description);
            if (String.IsNullOrWhiteSpace(translated))
            {
                translated = Find(relic.localizations, "en-US", description);
            }

            if (String.IsNullOrWhiteSpace(translated))
            {
                return false;
            }

            value = translated;
            return true;
        }

        private static string Find(
            ContentRelicLocalizationDefinition[] localizations,
            string language,
            bool description)
        {
            if (localizations == null || String.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            for (int i = 0; i < localizations.Length; i++)
            {
                ContentRelicLocalizationDefinition localization = localizations[i];
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
