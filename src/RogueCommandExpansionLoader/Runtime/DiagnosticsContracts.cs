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
        public StagePoolScopeDiagnosticReport(string stageId, string poolId, IReadOnlyList<string> factoryIds)
        {
            StageId = stageId ?? string.Empty;
            PoolId = poolId ?? string.Empty;
            if (factoryIds == null || factoryIds.Count == 0)
            {
                FactoryIds = Array.AsReadOnly(new string[0]);
            }
            else
            {
                var copy = new string[factoryIds.Count];
                for (int i = 0; i < factoryIds.Count; i++)
                {
                    copy[i] = factoryIds[i];
                }
                Array.Sort(copy, StringComparer.Ordinal);
                FactoryIds = Array.AsReadOnly(copy);
            }
        }

        public string StageId { get; private set; }
        public string PoolId { get; private set; }
        public IReadOnlyList<string> FactoryIds { get; private set; }
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
            bool isForSpecialists)
        {
            UnitId = unitId ?? string.Empty;
            IsAllowedAsBlueprint = isAllowedAsBlueprint;
            IsAllowedAsStartingBlueprint = isAllowedAsStartingBlueprint;
            IsAllowedForAi = isAllowedForAi;
            IsAllowedForDemo = isAllowedForDemo;
            IsForSpecialists = isForSpecialists;
        }

        public string UnitId { get; private set; }
        public bool IsAllowedAsBlueprint { get; private set; }
        public bool IsAllowedAsStartingBlueprint { get; private set; }
        public bool IsAllowedForAi { get; private set; }
        public bool IsAllowedForDemo { get; private set; }
        public bool IsForSpecialists { get; private set; }
    }

    public sealed class UnitAssetResolutionDiagnosticReport
    {
        public UnitAssetResolutionDiagnosticReport(
            string unitId,
            bool prefabResolved,
            bool cardImageResolved,
            bool visualResolved,
            string statusMessage)
        {
            UnitId = unitId ?? string.Empty;
            PrefabResolved = prefabResolved;
            CardImageResolved = cardImageResolved;
            VisualResolved = visualResolved;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public string UnitId { get; private set; }
        public bool PrefabResolved { get; private set; }
        public bool CardImageResolved { get; private set; }
        public bool VisualResolved { get; private set; }
        public string StatusMessage { get; private set; }
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
                        unit.IsForSpecialists));
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
                bool visualResolved = !visualRequested || Contains(resolvedVisuals, unit.UnitId);
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
                    unit.UnitId, prefabResolved, cardImageResolved, visualResolved, String.Join(" ", statusParts)));
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

            string ids = report.FactoryIds.Count > 0
                ? String.Join(", ", report.FactoryIds)
                : "Ninguno";
            return report.StageId + " / " + report.PoolId + ": " + ids;
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
    }
}
