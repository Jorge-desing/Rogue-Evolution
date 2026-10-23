using System;
using System.Globalization;

namespace RogueCommandExpansionLoader.Runtime
{
    public static class UnitVariantDiagnosticsFormatter
    {
        public static string FormatVariantCount(UnitDiagnosticReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            return "Variantes declaradas (solo metadatos): " +
                report.VariantCount.ToString(CultureInfo.InvariantCulture);
        }
    }
}
