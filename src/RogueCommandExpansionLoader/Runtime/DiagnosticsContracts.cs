using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    public sealed class PackDiagnosticReport
    {
        public PackDiagnosticReport(
            string packId,
            string folderName,
            bool isLoaded,
            string statusMessage,
            int bundleCount,
            long totalBundleBytes,
            int unitCount,
            int stageCount,
            int aiProfileCount)
        {
            PackId = packId ?? string.Empty;
            FolderName = folderName ?? string.Empty;
            IsLoaded = isLoaded;
            StatusMessage = statusMessage ?? string.Empty;
            BundleCount = bundleCount;
            TotalBundleBytes = totalBundleBytes;
            UnitCount = unitCount;
            StageCount = stageCount;
            AiProfileCount = aiProfileCount;
        }

        public string PackId { get; private set; }
        public string FolderName { get; private set; }
        public bool IsLoaded { get; private set; }
        public string StatusMessage { get; private set; }
        public int BundleCount { get; private set; }
        public long TotalBundleBytes { get; private set; }
        public int UnitCount { get; private set; }
        public int StageCount { get; private set; }
        public int AiProfileCount { get; private set; }
    }

    public sealed class UnitDiagnosticReport
    {
        public UnitDiagnosticReport(
            string unitId,
            string packId,
            string prefabKey,
            string imageKey,
            bool isAllowedAsBlueprint,
            bool isAllowedAsStartingBlueprint = false,
            bool isAllowedForAi = false,
            bool isAllowedForDemo = false,
            bool isForSpecialists = false,
            string eligibilityReadError = null,
            int variantCount = 0)
        {
            UnitId = unitId ?? string.Empty;
            PackId = packId ?? string.Empty;
            PrefabKey = prefabKey ?? string.Empty;
            ImageKey = imageKey ?? string.Empty;
            IsAllowedAsBlueprint = isAllowedAsBlueprint;
            IsAllowedAsStartingBlueprint = isAllowedAsStartingBlueprint;
            IsAllowedForAi = isAllowedForAi;
            IsAllowedForDemo = isAllowedForDemo;
            IsForSpecialists = isForSpecialists;
            EligibilityReadError = eligibilityReadError ?? string.Empty;
            VariantCount = variantCount;
        }

        public string UnitId { get; private set; }
        public string PackId { get; private set; }
        public string PrefabKey { get; private set; }
        public string ImageKey { get; private set; }
        public bool IsAllowedAsBlueprint { get; private set; }
        public bool IsAllowedAsStartingBlueprint { get; private set; }
        public bool IsAllowedForAi { get; private set; }
        public bool IsAllowedForDemo { get; private set; }
        public bool IsForSpecialists { get; private set; }
        public string EligibilityReadError { get; private set; }
        public int VariantCount { get; private set; }
    }

    public sealed class StageDiagnosticReport
    {
        public StageDiagnosticReport(
            string stageId,
            string packId,
            int mapSizeMin,
            int mapSizeMax,
            string[] unitIds,
            string[] aiProfileIds)
        {
            StageId = stageId ?? string.Empty;
            PackId = packId ?? string.Empty;
            MapSizeMin = mapSizeMin;
            MapSizeMax = mapSizeMax;
            UnitIds = Array.AsReadOnly(unitIds == null ? new string[0] : (string[])unitIds.Clone());
            AiProfileIds = Array.AsReadOnly(aiProfileIds == null ? new string[0] : (string[])aiProfileIds.Clone());
        }

        public string StageId { get; private set; }
        public string PackId { get; private set; }
        public int MapSizeMin { get; private set; }
        public int MapSizeMax { get; private set; }
        public IReadOnlyList<string> UnitIds { get; private set; }
        public IReadOnlyList<string> AiProfileIds { get; private set; }
    }

    /// <summary>
    /// Stable active stage identity from the loader's StageMap sequence, plus the AI profile IDs
    /// explicitly declared by a custom stage. Native localized preview labels are not identities.
    /// </summary>
    public sealed class ActiveStageAiAssociationDiagnostic
    {
        public ActiveStageAiAssociationDiagnostic(
            bool stageIdentityResolved,
            int stageMapIndex,
            string stageId,
            string packId,
            string stageKind,
            string identitySource,
            string failureReason,
            IEnumerable<string> declaredAiProfileIds)
        {
            StageIdentityResolved = stageIdentityResolved;
            StageMapIndex = stageMapIndex;
            StageId = stageId ?? string.Empty;
            PackId = packId ?? string.Empty;
            StageKind = stageKind ?? string.Empty;
            IdentitySource = identitySource ?? string.Empty;
            FailureReason = failureReason ?? string.Empty;

            var profileIds = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (declaredAiProfileIds != null)
            {
                foreach (string profileId in declaredAiProfileIds)
                {
                    if (!String.IsNullOrWhiteSpace(profileId) && seen.Add(profileId))
                    {
                        profileIds.Add(profileId);
                    }
                }
            }
            DeclaredAiProfileIds = Array.AsReadOnly(profileIds.ToArray());
        }

        public bool StageIdentityResolved { get; private set; }
        public int StageMapIndex { get; private set; }
        public string StageId { get; private set; }
        public string PackId { get; private set; }
        public string StageKind { get; private set; }
        public string IdentitySource { get; private set; }
        public string FailureReason { get; private set; }
        public IReadOnlyList<string> DeclaredAiProfileIds { get; private set; }

        public static ActiveStageAiAssociationDiagnostic Unavailable(
            string reason,
            int stageMapIndex = -1,
            string identitySource = "unknown")
        {
            return new ActiveStageAiAssociationDiagnostic(
                false,
                stageMapIndex,
                string.Empty,
                string.Empty,
                string.Empty,
                identitySource,
                reason,
                null);
        }
    }

    public sealed class AiProfileDiagnosticReport
    {
        public AiProfileDiagnosticReport(
            string profileId,
            string packId,
            string prefabAssetName,
            string[] moduleIds,
            int activeAiInstances)
        {
            ProfileId = profileId ?? string.Empty;
            PackId = packId ?? string.Empty;
            PrefabAssetName = prefabAssetName ?? string.Empty;
            ModuleIds = Array.AsReadOnly(moduleIds == null ? new string[0] : (string[])moduleIds.Clone());
            ActiveAiInstances = activeAiInstances;
        }

        public string ProfileId { get; private set; }
        public string PackId { get; private set; }
        public string PrefabAssetName { get; private set; }
        public IReadOnlyList<string> ModuleIds { get; private set; }
        public int ActiveAiInstances { get; private set; }
    }

    public sealed class UnitFactoryDiagnosticReport
    {
        public UnitFactoryDiagnosticReport(string unitId, string factoryId, bool isLoaded, string statusMessage)
        {
            UnitId = unitId ?? string.Empty;
            FactoryId = factoryId ?? string.Empty;
            IsLoaded = isLoaded;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public string UnitId { get; private set; }
        public string FactoryId { get; private set; }
        public bool IsLoaded { get; private set; }
        public string StatusMessage { get; private set; }
    }

    public sealed class StagePoolScopeDiagnosticReport
    {
        public StagePoolScopeDiagnosticReport(string stageId, string poolId, IReadOnlyList<string> candidateIds)
        {
            StageId = stageId ?? string.Empty;
            PoolId = poolId ?? string.Empty;
            if (candidateIds == null || candidateIds.Count == 0)
            {
                CandidateIds = Array.AsReadOnly(new string[0]);
            }
            else
            {
                var copy = new string[candidateIds.Count];
                for (int i = 0; i < candidateIds.Count; i++)
                {
                    copy[i] = candidateIds[i];
                }
                Array.Sort(copy, StringComparer.Ordinal);
                CandidateIds = Array.AsReadOnly(copy);
            }
        }

        public string StageId { get; private set; }
        public string PoolId { get; private set; }
        public IReadOnlyList<string> CandidateIds { get; private set; }
    }

    public sealed class AiProfileStageRangeDiagnosticReport
    {
        public AiProfileStageRangeDiagnosticReport(string profileId, int stageMin, int stageMax, bool isContiguous)
        {
            ProfileId = profileId ?? string.Empty;
            StageMin = stageMin;
            StageMax = stageMax;
            IsContiguous = isContiguous;
        }

        public string ProfileId { get; private set; }
        public int StageMin { get; private set; }
        public int StageMax { get; private set; }
        public bool IsContiguous { get; private set; }
    }

    public sealed class UnitEligibilityDiagnosticReport
    {
        public UnitEligibilityDiagnosticReport(
            string unitId,
            bool isAllowedAsBlueprint,
            bool isAllowedAsStartingBlueprint,
            bool isAllowedForAi,
            bool isAllowedForDemo,
            bool isForSpecialists,
            string eligibilityReadError = null)
        {
            UnitId = unitId ?? string.Empty;
            IsAllowedAsBlueprint = isAllowedAsBlueprint;
            IsAllowedAsStartingBlueprint = isAllowedAsStartingBlueprint;
            IsAllowedForAi = isAllowedForAi;
            IsAllowedForDemo = isAllowedForDemo;
            IsForSpecialists = isForSpecialists;
            EligibilityReadError = eligibilityReadError ?? string.Empty;
            IsResolved = String.IsNullOrWhiteSpace(EligibilityReadError);
        }

        public string UnitId { get; private set; }
        public bool IsAllowedAsBlueprint { get; private set; }
        public bool IsAllowedAsStartingBlueprint { get; private set; }
        public bool IsAllowedForAi { get; private set; }
        public bool IsAllowedForDemo { get; private set; }
        public bool IsForSpecialists { get; private set; }
        public string EligibilityReadError { get; private set; }
        public bool IsResolved { get; private set; }
    }

    public enum UnitAssetResolutionSeverity
    {
        Muted,
        Success,
        Warning,
        Error
    }

    public sealed class UnitAssetResolutionDiagnosticReport
    {
        public UnitAssetResolutionDiagnosticReport(
            string unitId,
            bool prefabResolved,
            bool cardImageResolved,
            bool visualRequested,
            bool visualResolved,
            string statusMessage)
        {
            UnitId = unitId ?? string.Empty;
            PrefabResolved = prefabResolved;
            CardImageResolved = cardImageResolved;
            VisualRequested = visualRequested;
            VisualResolved = visualResolved;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public string UnitId { get; private set; }
        public bool PrefabResolved { get; private set; }
        public bool CardImageResolved { get; private set; }
        public bool VisualRequested { get; private set; }
        public bool VisualResolved { get; private set; }
        public string StatusMessage { get; private set; }
    }

    public sealed class UnitLocalizationCoverageDiagnosticReport
    {
        public UnitLocalizationCoverageDiagnosticReport(
            string unitId,
            IReadOnlyList<string> availableLanguages,
            bool hasEnglish,
            bool hasNativeTemplate)
        {
            UnitId = unitId ?? string.Empty;
            int languageCount = availableLanguages != null ? availableLanguages.Count : 0;
            var languageCopy = new string[languageCount];
            for (int i = 0; i < languageCount; i++)
            {
                languageCopy[i] = availableLanguages[i];
            }
            AvailableLanguages = Array.AsReadOnly(languageCopy);
            HasEnglish = hasEnglish;
            HasNativeTemplate = hasNativeTemplate;
        }

        public string UnitId { get; private set; }
        public IReadOnlyList<string> AvailableLanguages { get; private set; }
        public bool HasEnglish { get; private set; }
        public bool HasNativeTemplate { get; private set; }
    }

    public sealed class LoaderDiagnosticsSnapshot
    {
        public LoaderDiagnosticsSnapshot(
            string targetGameVersion,
            bool isNativeRegistryReady,
            bool isLoaded,
            int activePackCount,
            int failedPackCount,
            long totalBundleBytes,
            IReadOnlyList<PackDiagnosticReport> packs,
            IReadOnlyList<UnitDiagnosticReport> units,
            IReadOnlyList<StageDiagnosticReport> stages,
            IReadOnlyList<AiProfileDiagnosticReport> aiProfiles,
            IReadOnlyList<UnitFactoryDiagnosticReport> unitFactories,
            IReadOnlyList<StagePoolScopeDiagnosticReport> stagePoolScopes,
            IReadOnlyList<AiProfileStageRangeDiagnosticReport> aiProfileStageRanges,
            IReadOnlyList<string> recentLogs)
            : this(
                targetGameVersion,
                isNativeRegistryReady,
                isLoaded,
                activePackCount,
                failedPackCount,
                totalBundleBytes,
                packs,
                units,
                stages,
                aiProfiles,
                unitFactories,
                stagePoolScopes,
                aiProfileStageRanges,
                null,
                null,
                null,
                recentLogs)
        {
        }

        public LoaderDiagnosticsSnapshot(
            string targetGameVersion,
            bool isNativeRegistryReady,
            bool isLoaded,
            int activePackCount,
            int failedPackCount,
            long totalBundleBytes,
            IReadOnlyList<PackDiagnosticReport> packs,
            IReadOnlyList<UnitDiagnosticReport> units,
            IReadOnlyList<StageDiagnosticReport> stages,
            IReadOnlyList<AiProfileDiagnosticReport> aiProfiles,
            IReadOnlyList<UnitFactoryDiagnosticReport> unitFactories,
            IReadOnlyList<StagePoolScopeDiagnosticReport> stagePoolScopes,
            IReadOnlyList<AiProfileStageRangeDiagnosticReport> aiProfileStageRanges,
            IReadOnlyList<UnitEligibilityDiagnosticReport> unitEligibilities,
            IReadOnlyList<UnitAssetResolutionDiagnosticReport> unitAssetResolutions,
            IReadOnlyList<UnitLocalizationCoverageDiagnosticReport> unitLocalizationCoverage,
            IReadOnlyList<string> recentLogs)
        {
            TargetGameVersion = targetGameVersion ?? string.Empty;
            IsNativeRegistryReady = isNativeRegistryReady;
            IsLoaded = isLoaded;
            ActivePackCount = activePackCount;
            FailedPackCount = failedPackCount;
            TotalBundleBytes = totalBundleBytes;
            Packs = CopyReadOnly(packs);
            Units = CopyReadOnly(units);
            Stages = CopyReadOnly(stages);
            AiProfiles = CopyReadOnly(aiProfiles);
            UnitFactories = CopyReadOnly(unitFactories);
            StagePoolScopes = CopyReadOnly(stagePoolScopes);
            AiProfileStageRanges = CopyReadOnly(aiProfileStageRanges);
            UnitEligibilities = CopyReadOnly(unitEligibilities);
            UnitAssetResolutions = CopyReadOnly(unitAssetResolutions);
            UnitLocalizationCoverage = CopyReadOnly(unitLocalizationCoverage);
            RecentLogs = CopyReadOnly(recentLogs);
        }

        public string TargetGameVersion { get; private set; }
        public bool IsNativeRegistryReady { get; private set; }
        public bool IsLoaded { get; private set; }
        public int ActivePackCount { get; private set; }
        public int FailedPackCount { get; private set; }
        public long TotalBundleBytes { get; private set; }
        public IReadOnlyList<PackDiagnosticReport> Packs { get; private set; }
        public IReadOnlyList<UnitDiagnosticReport> Units { get; private set; }
        public IReadOnlyList<StageDiagnosticReport> Stages { get; private set; }
        public IReadOnlyList<AiProfileDiagnosticReport> AiProfiles { get; private set; }
        public IReadOnlyList<UnitFactoryDiagnosticReport> UnitFactories { get; private set; }
        public IReadOnlyList<StagePoolScopeDiagnosticReport> StagePoolScopes { get; private set; }
        public IReadOnlyList<AiProfileStageRangeDiagnosticReport> AiProfileStageRanges { get; private set; }
        public IReadOnlyList<UnitEligibilityDiagnosticReport> UnitEligibilities { get; private set; }
        public IReadOnlyList<UnitAssetResolutionDiagnosticReport> UnitAssetResolutions { get; private set; }
        public IReadOnlyList<UnitLocalizationCoverageDiagnosticReport> UnitLocalizationCoverage { get; private set; }
        public IReadOnlyList<string> RecentLogs { get; private set; }

        private static IReadOnlyList<T> CopyReadOnly<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.AsReadOnly(new T[0]);
            }

            var copy = new T[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return Array.AsReadOnly(copy);
        }
    }

    public static class LoaderUnitDiagnosticsBuilder
    {
        public static IReadOnlyList<UnitEligibilityDiagnosticReport> CreateEligibilityReports(
            IReadOnlyList<UnitDiagnosticReport> units)
        {
            var reports = new List<UnitEligibilityDiagnosticReport>();
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    UnitDiagnosticReport unit = units[i];
                    if (unit == null)
                    {
                        continue;
                    }

                    reports.Add(new UnitEligibilityDiagnosticReport(
                        unit.UnitId,
                        unit.IsAllowedAsBlueprint,
                        unit.IsAllowedAsStartingBlueprint,
                        unit.IsAllowedForAi,
                        unit.IsAllowedForDemo,
                        unit.IsForSpecialists,
                        unit.EligibilityReadError));
                }
            }

            reports.Sort((left, right) => StringComparer.Ordinal.Compare(left.UnitId, right.UnitId));
            return reports.AsReadOnly();
        }

        public static IReadOnlyList<UnitAssetResolutionDiagnosticReport> CreateAssetResolutionReports(
            IReadOnlyList<UnitDiagnosticReport> units,
            ISet<string> resolvedResources,
            ISet<string> visualRequests,
            ISet<string> resolvedVisuals)
        {
            var reports = new List<UnitAssetResolutionDiagnosticReport>();
            if (units == null)
            {
                return reports.AsReadOnly();
            }

            for (int i = 0; i < units.Count; i++)
            {
                UnitDiagnosticReport unit = units[i];
                if (unit == null)
                {
                    continue;
                }

                bool prefabResolved = Contains(resolvedResources, unit.PrefabKey);
                bool cardImageResolved = Contains(resolvedResources, unit.ImageKey);
                bool cardImagePathUnverified = !cardImageResolved &&
                    !String.IsNullOrWhiteSpace(unit.ImageKey) &&
                    !unit.ImageKey.StartsWith("RogueCommandExpansionLoader/", StringComparison.Ordinal);
                bool visualRequested = Contains(visualRequests, unit.UnitId);
                bool visualResolved = visualRequested && Contains(resolvedVisuals, unit.UnitId);
                var missing = new List<string>();
                if (!prefabResolved) missing.Add("prefab");
                if (!cardImageResolved && !cardImagePathUnverified) missing.Add("card image");
                if (visualRequested && !visualResolved) missing.Add("visual");

                var statusParts = new List<string>();
                if (missing.Count > 0)
                {
                    statusParts.Add("Missing: " + String.Join(", ", missing) + ".");
                }
                if (cardImagePathUnverified)
                {
                    statusParts.Add("Native card image path not independently resolved by loader.");
                }
                if (statusParts.Count == 0)
                {
                    statusParts.Add(visualRequested
                        ? "All requested assets resolved."
                        : "Prefab and card image resolved; custom visual not requested.");
                }

                reports.Add(new UnitAssetResolutionDiagnosticReport(
                    unit.UnitId, prefabResolved, cardImageResolved, visualRequested,
                    visualResolved, String.Join(" ", statusParts)));
            }

            reports.Sort((left, right) => StringComparer.Ordinal.Compare(left.UnitId, right.UnitId));
            return reports.AsReadOnly();
        }

        public static UnitAssetResolutionSeverity GetAssetResolutionSeverity(
            UnitAssetResolutionDiagnosticReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (report.StatusMessage.StartsWith("Missing:", StringComparison.Ordinal))
            {
                return UnitAssetResolutionSeverity.Error;
            }

            if (report.StatusMessage.IndexOf("not independently resolved", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return UnitAssetResolutionSeverity.Warning;
            }

            return report.VisualRequested
                ? UnitAssetResolutionSeverity.Success
                : UnitAssetResolutionSeverity.Muted;
        }

        public static IReadOnlyList<UnitLocalizationCoverageDiagnosticReport> CreateLocalizationCoverageReports(
            IReadOnlyList<UnitDiagnosticReport> units,
            Func<string, IReadOnlyList<string>> languageResolver,
            Func<string, bool> hasNativeTemplateResolver)
        {
            var reports = new List<UnitLocalizationCoverageDiagnosticReport>();
            if (units == null)
            {
                return reports.AsReadOnly();
            }

            for (int i = 0; i < units.Count; i++)
            {
                UnitDiagnosticReport unit = units[i];
                if (unit == null)
                {
                    continue;
                }

                IReadOnlyList<string> availableLanguages = languageResolver != null
                    ? (languageResolver(unit.UnitId) ?? new List<string>())
                    : new List<string>();

                bool hasEnglish = false;
                foreach (string lang in availableLanguages)
                {
                    if (String.Equals(lang, "en-US", StringComparison.Ordinal))
                    {
                        hasEnglish = true;
                        break;
                    }
                }

                bool hasNativeTemplate = hasNativeTemplateResolver != null
                    ? hasNativeTemplateResolver(unit.UnitId)
                    : false;

                reports.Add(new UnitLocalizationCoverageDiagnosticReport(
                    unit.UnitId, availableLanguages, hasEnglish, hasNativeTemplate));
            }

            reports.Sort((left, right) => StringComparer.Ordinal.Compare(left.UnitId, right.UnitId));
            return reports.AsReadOnly();
        }

        private static bool Contains(ISet<string> values, string value)
        {
            return values != null && !String.IsNullOrWhiteSpace(value) && values.Contains(value);
        }
    }

    public static class LoaderDiagnosticsFormatter
    {
        public static string FormatUnitFactory(UnitFactoryDiagnosticReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (String.IsNullOrWhiteSpace(report.FactoryId))
            {
                return report.UnitId + " - " + report.StatusMessage;
            }

            return report.UnitId + " -> " + report.FactoryId + " (" + report.StatusMessage + ")";
        }

        public static string FormatStagePoolScope(StagePoolScopeDiagnosticReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            string ids = report.CandidateIds.Count > 0
                ? String.Join(", ", report.CandidateIds)
                : "Ninguno";
            return report.StageId + " / " + report.PoolId + " candidates: " + ids;
        }

        public static string FormatAiProfileStageRange(AiProfileStageRangeDiagnosticReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            string status = report.IsContiguous ? "contiguous" : "not contiguous";
            return report.ProfileId + ": " + report.StageMin + "-" + report.StageMax + " (" + status + ")";
        }

        public static string FormatUnitEligibility(UnitEligibilityDiagnosticReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (!report.IsResolved)
            {
                return report.UnitId + " [Eligibility=Unknown; failed fields=" +
                    report.EligibilityReadError + "]";
            }

            return report.UnitId + " [Blueprint=" + report.IsAllowedAsBlueprint +
                ", StartingBlueprint=" + report.IsAllowedAsStartingBlueprint +
                ", AI=" + report.IsAllowedForAi + ", Demo=" + report.IsAllowedForDemo +
                ", Specialist=" + report.IsForSpecialists + "]";
        }

        public static string FormatUnitAssetResolution(UnitAssetResolutionDiagnosticReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            return report.UnitId + " [Prefab=" + report.PrefabResolved +
                ", Card=" + report.CardImageResolved + ", Visual=" + report.VisualResolved +
                "] " + report.StatusMessage;
        }

        public static string FormatUnitLocalizationCoverage(UnitLocalizationCoverageDiagnosticReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            string langs = report.AvailableLanguages.Count > 0
                ? String.Join(", ", report.AvailableLanguages)
                : "none";
            return report.UnitId + " [Languages=" + langs +
                ", English=" + report.HasEnglish +
                ", NativeTemplate=" + report.HasNativeTemplate + "]";
        }
    }
}
