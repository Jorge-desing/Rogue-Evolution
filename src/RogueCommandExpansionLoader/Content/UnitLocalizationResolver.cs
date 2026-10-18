using System;
using System.Globalization;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>
    /// Resolves a mod unit's visible text, falling back to the native template where a pack does
    /// not provide a translation for the active language.
    /// </summary>
    public static class UnitLocalizationResolver
    {
        public static bool TryResolve(
            ContentUnitDefinition unit,
            string language,
            bool description,
            Func<string, bool, string> nativeTemplateLookup,
            out string value)
        {
            value = null;
            if (unit == null || String.IsNullOrWhiteSpace(unit.id))
            {
                return false;
            }

            string translated = Find(unit.localizations, language, description);
            if (String.IsNullOrWhiteSpace(translated))
            {
                translated = Find(unit.localizations, "en-US", description);
            }

            if (!String.IsNullOrWhiteSpace(translated))
            {
                value = translated;
                return true;
            }

            if (!String.IsNullOrWhiteSpace(unit.nativeTemplateId) && nativeTemplateLookup != null)
            {
                translated = nativeTemplateLookup(unit.nativeTemplateId, description);
                if (!String.IsNullOrWhiteSpace(translated))
                {
                    value = translated;
                    return true;
                }
            }

            value = description ? String.Empty : Humanize(unit.id);
            return true;
        }

        public static bool TryResolveSkillDescription(
            ContentUnitDefinition unit,
            string language,
            Func<string, string> nativeTemplateLookup,
            out string value)
        {
            value = null;
            if (unit == null || String.IsNullOrWhiteSpace(unit.id))
            {
                return false;
            }

            string translated = FindSkillDescription(unit.localizations, language);
            if (String.IsNullOrWhiteSpace(translated))
            {
                translated = FindSkillDescription(unit.localizations, "en-US");
            }

            if (!String.IsNullOrWhiteSpace(translated))
            {
                value = translated;
                return true;
            }

            if (!String.IsNullOrWhiteSpace(unit.nativeTemplateId) && nativeTemplateLookup != null)
            {
                translated = nativeTemplateLookup(unit.nativeTemplateId);
                if (!String.IsNullOrWhiteSpace(translated))
                {
                    value = translated;
                    return true;
                }
            }

            value = String.Empty;
            return true;
        }

        private static string Find(
            ContentUnitLocalizationDefinition[] localizations,
            string language,
            bool description)
        {
            if (localizations == null || String.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            for (int i = 0; i < localizations.Length; i++)
            {
                ContentUnitLocalizationDefinition localization = localizations[i];
                if (localization != null &&
                    String.Equals(localization.language, language, StringComparison.Ordinal))
                {
                    return description ? localization.description : localization.name;
                }
            }

            return null;
        }

        private static string FindSkillDescription(
            ContentUnitLocalizationDefinition[] localizations,
            string language)
        {
            if (localizations == null || String.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            for (int i = 0; i < localizations.Length; i++)
            {
                ContentUnitLocalizationDefinition localization = localizations[i];
                if (localization != null &&
                    String.Equals(localization.language, language, StringComparison.Ordinal))
                {
                    return localization.skillDescription;
                }
            }

            return null;
        }

        private static string Humanize(string id)
        {
            int separatorIndex = id.IndexOf(':');
            string label = separatorIndex >= 0 ? id.Substring(separatorIndex + 1) : id;
            label = label.Replace('-', ' ').Replace('_', ' ');
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(label);
        }
    }
}
