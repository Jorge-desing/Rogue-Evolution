using System;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Resolves the active game localization language without allowing a reflected
    /// getter failure to escape a content hook.
    /// </summary>
    internal static class RuntimeLocalizationLanguage
    {
        public static string GetOrDefault(
            Func<string> getter,
            Action<string> log,
            string context)
        {
            if (getter == null)
            {
                return "en-US";
            }

            try
            {
                string language = getter();
                return string.IsNullOrWhiteSpace(language) ? "en-US" : language;
            }
            catch (Exception exception)
            {
                if (log != null)
                {
                    string detail = exception.GetBaseException().Message ?? string.Empty;
                    log("No se pudo leer Loca.Language para " + (context ?? "contenido") + ": " + detail);
                }

                return "en-US";
            }
        }
    }
}
