using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure primitive DTO describing the runtime readiness of a single loader-registered unit or building.
    /// </summary>
    public readonly struct LoaderUnitAuditRow
    {
        public LoaderUnitAuditRow(
            string unitId,
            string packId,
            string factoryId,
            bool isBuilding,
            bool hasMechanicsProfile,
            int variantCount,
            bool prefabAudited,
            string rigType,
            bool hasAmbiguousSockets,
            int targetPrespawnQuota,
            int prespawnedPoolCount,
            int livingCount,
            string defaultStance,
            string defaultLoadout,
            int startingVeterancyRank)
        {
            UnitId = unitId ?? string.Empty;
            PackId = packId ?? string.Empty;
            FactoryId = factoryId ?? string.Empty;
            IsBuilding = isBuilding;
            HasMechanicsProfile = hasMechanicsProfile;
            VariantCount = variantCount;
            PrefabAudited = prefabAudited;
            RigType = rigType ?? "StaticMesh";
            HasAmbiguousSockets = hasAmbiguousSockets;
            TargetPrespawnQuota = targetPrespawnQuota;
            PrespawnedPoolCount = prespawnedPoolCount;
            LivingCount = livingCount;
            DefaultStance = defaultStance ?? string.Empty;
            DefaultLoadout = defaultLoadout ?? string.Empty;
            StartingVeterancyRank = startingVeterancyRank;
        }

        public string UnitId { get; }
        public string PackId { get; }
        public string FactoryId { get; }
        public bool IsBuilding { get; }
        public bool HasMechanicsProfile { get; }
        public int VariantCount { get; }
        public bool PrefabAudited { get; }
        public string RigType { get; }
        public bool HasAmbiguousSockets { get; }
        public int TargetPrespawnQuota { get; }
        public int PrespawnedPoolCount { get; }
        public int LivingCount { get; }
        public string DefaultStance { get; }
        public string DefaultLoadout { get; }
        public int StartingVeterancyRank { get; }
    }

    /// <summary>
    /// Pure primitive DTO describing the runtime readiness and squad defaults of a loader-registered AI profile.
    /// </summary>
    public readonly struct LoaderAiProfileAuditRow
    {
        public LoaderAiProfileAuditRow(
            string profileId,
            string packId,
            string prefabAssetName,
            int moduleCount,
            int activeInstanceCount,
            string doctrine,
            float aggressionMultiplier,
            string defaultUnitStance,
            string defaultUnitLoadout,
            IReadOnlyList<string> startingUnitAffixes,
            int startingUnitVeterancyRank)
        {
            ProfileId = profileId ?? string.Empty;
            PackId = packId ?? string.Empty;
            PrefabAssetName = prefabAssetName ?? string.Empty;
            ModuleCount = moduleCount;
            ActiveInstanceCount = activeInstanceCount;
            Doctrine = doctrine ?? "Standard";
            AggressionMultiplier = aggressionMultiplier;
            DefaultUnitStance = defaultUnitStance ?? string.Empty;
            DefaultUnitLoadout = defaultUnitLoadout ?? string.Empty;
            StartingUnitAffixes = startingUnitAffixes ?? Array.Empty<string>();
            StartingUnitVeterancyRank = startingUnitVeterancyRank;
        }

        public string ProfileId { get; }
        public string PackId { get; }
        public string PrefabAssetName { get; }
        public int ModuleCount { get; }
        public int ActiveInstanceCount { get; }
        public string Doctrine { get; }
        public float AggressionMultiplier { get; }
        public string DefaultUnitStance { get; }
        public string DefaultUnitLoadout { get; }
        public IReadOnlyList<string> StartingUnitAffixes { get; }
        public int StartingUnitVeterancyRank { get; }
    }

    /// <summary>
    /// Pure primitive census of loader-registered units, buildings, and AI profiles against live runtime state.
    /// Omission counters are placed strictly first to uphold clean-room bounded trace invariants.
    /// </summary>
    public sealed class LoaderRuntimeAuditCensus
    {
        public int OmittedUnitCount { get; }
        public int TotalRegisteredUnits { get; }
        public int OmittedAiProfileCount { get; }
        public int TotalRegisteredAiProfiles { get; }
        public int MechanicsProfileCount { get; }
        public int AuditedPrefabCount { get; }
        public int WarmedPoolUnitCount { get; }
        public int ActiveAiInstanceCount { get; }
        public IReadOnlyList<LoaderUnitAuditRow> UnitRows { get; }
        public IReadOnlyList<LoaderAiProfileAuditRow> AiProfileRows { get; }

        public LoaderRuntimeAuditCensus(
            int omittedUnitCount,
            int totalRegisteredUnits,
            int omittedAiProfileCount,
            int totalRegisteredAiProfiles,
            int mechanicsProfileCount,
            int auditedPrefabCount,
            int warmedPoolUnitCount,
            int activeAiInstanceCount,
            IReadOnlyList<LoaderUnitAuditRow> unitRows,
            IReadOnlyList<LoaderAiProfileAuditRow> aiProfileRows)
        {
            OmittedUnitCount = omittedUnitCount;
            TotalRegisteredUnits = totalRegisteredUnits;
            OmittedAiProfileCount = omittedAiProfileCount;
            TotalRegisteredAiProfiles = totalRegisteredAiProfiles;
            MechanicsProfileCount = mechanicsProfileCount;
            AuditedPrefabCount = auditedPrefabCount;
            WarmedPoolUnitCount = warmedPoolUnitCount;
            ActiveAiInstanceCount = activeAiInstanceCount;
            UnitRows = unitRows ?? new List<LoaderUnitAuditRow>().AsReadOnly();
            AiProfileRows = aiProfileRows ?? new List<LoaderAiProfileAuditRow>().AsReadOnly();
        }

        public static LoaderRuntimeAuditCensus Empty { get; } = new LoaderRuntimeAuditCensus(
            0, 0, 0, 0, 0, 0, 0, 0,
            new List<LoaderUnitAuditRow>().AsReadOnly(),
            new List<LoaderAiProfileAuditRow>().AsReadOnly());
    }

    /// <summary>
    /// Pure primitive result returned when actively reconciling the unit &amp; AI loader runtime state.
    /// </summary>
    public readonly struct LoaderReconciliationResult
    {
        public LoaderReconciliationResult(
            bool success,
            int reconciledUnitCount,
            int warmedPoolCount,
            int attachedMechanicsCount,
            int attachedVisualDirectorCount,
            int appliedAiUnitDefaultsCount,
            int reconciledAiProfileCount,
            string message)
        {
            Success = success;
            ReconciledUnitCount = reconciledUnitCount;
            WarmedPoolCount = warmedPoolCount;
            AttachedMechanicsCount = attachedMechanicsCount;
            AttachedVisualDirectorCount = attachedVisualDirectorCount;
            AppliedAiUnitDefaultsCount = appliedAiUnitDefaultsCount;
            ReconciledAiProfileCount = reconciledAiProfileCount;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public int ReconciledUnitCount { get; }
        public int WarmedPoolCount { get; }
        public int AttachedMechanicsCount { get; }
        public int AttachedVisualDirectorCount { get; }
        public int AppliedAiUnitDefaultsCount { get; }
        public int ReconciledAiProfileCount { get; }
        public string Message { get; }
    }
}
