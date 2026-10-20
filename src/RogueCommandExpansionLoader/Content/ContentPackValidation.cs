using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>
    /// Read-only view of IDs already known to the loader. Implementations must
    /// represent a stable snapshot for the duration of one validation call.
    /// </summary>
    public interface IContentRegistrySnapshot
    {
        bool ContainsId(string id);
        bool ContainsUnit(string id);
        bool ContainsAiProfile(string id);
    }

    /// <summary>
    /// Validates one AI module and its string parameters against the runtime's
    /// registered module definitions. Returning false rejects this package.
    /// The diagnostic should explain the unknown module or invalid parameter.
    /// </summary>
    public interface IAiModuleValidator
    {
        bool TryValidate(string moduleId, ContentModuleParameter[] parameters, out string diagnostic);
    }

    public sealed class ContentDiagnostic
    {
        public ContentDiagnostic(string code, string path, string message)
        {
            Code = code;
            Path = path;
            Message = message;
        }

        public string Code { get; private set; }
        public string Path { get; private set; }
        public string Message { get; private set; }

        public override string ToString()
        {
            return Code + " at " + Path + ": " + Message;
        }
    }

    public sealed class ContentPackValidationResult
    {
        private readonly ContentPackDefinition pack;
        private readonly List<ContentDiagnostic> diagnostics;

        internal ContentPackValidationResult(ContentPackDefinition pack, List<ContentDiagnostic> diagnostics)
        {
            this.pack = ContentPackCopy.DeepClone(pack);
            this.diagnostics = diagnostics;
        }

        public ContentPackDefinition Pack { get { return ContentPackCopy.DeepClone(pack); } }
        public bool IsValid { get { return diagnostics.Count == 0; } }
        public IReadOnlyList<ContentDiagnostic> Diagnostics { get { return diagnostics.AsReadOnly(); } }
    }

    /// <summary>
    /// Performs package-local validation only. It never writes to, or mutates,
    /// the supplied registry snapshot and does not load AssetBundles or code.
    /// </summary>
    public static class ContentPackValidator
    {
        public const int SupportedSchemaVersion = 1;

        internal static bool DeclaresAiUnitDependency(ContentAiProfileDefinition profile, string unitId)
        {
            if (profile == null || String.IsNullOrWhiteSpace(unitId))
            {
                return false;
            }

            foreach (string dependency in profile.unitDependencies ?? new string[0])
            {
                if (String.Equals(dependency, unitId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            // unitEntries already establishes pack load order, before these entries
            // are appended to the cloned native deck and the complete deck is checked.
            foreach (ContentAiUnitEntryDefinition entry in profile.unitEntries ?? new ContentAiUnitEntryDefinition[0])
            {
                if (entry != null && String.Equals(entry.unitId, unitId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            // Production-building deck entries name the produced unit so the dependency
            // loads both its balance row and the factory row that the profile will add.
            foreach (ContentAiProductionBuildingEntryDefinition entry in
                profile.productionBuildingEntries ?? new ContentAiProductionBuildingEntryDefinition[0])
            {
                if (entry != null && String.Equals(entry.unitId, unitId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool PrefabReferenceMatchesObjectName(string assetReference, string objectName)
        {
            if (String.IsNullOrWhiteSpace(assetReference) || String.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            // AssetBundle paths identify an object independently from the GameObject's
            // serialized root name. Unity has already resolved the typed asset by this point.
            if (assetReference.IndexOf('/') >= 0 || assetReference.IndexOf('\\') >= 0 ||
                assetReference.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return String.Equals(assetReference, objectName, StringComparison.Ordinal);
        }
        public const int MaxAssetBundlesPerPack = 128;
        public const int MaxBalanceOverridesPerDefinition = 64;
        public const int MaxLocalizationsPerDefinition = 10;
        public const int MaxNeededExperienceLevel = 10000;
        public const int MaxAiModulesPerProfile = 8;
        public const int MaxAiUnitEntriesPerProfile = 32;
        public const int MaxAiProductionBuildingEntriesPerProfile = 32;
        public const int MaxAiUnitDependenciesPerProfile = 64;
        public const int MaxRelicsPerPack = 128;
        public const int MaxAiUnitCountAtStart = 8;
        public const int MaxAiUnitMaxLiving = 32;
        public const int MaxAiBaseIndex = 31;
        public const int MaxAiUnitStartingAscensionLevel = 19;
        public const long MaxManifestBundleBytes = 64L * 1024L * 1024L;
        public const long MaxAssetBundleBytes = 512L * 1024L * 1024L;
        public const long MaxTotalContentBundleBytes = 2L * 1024L * 1024L * 1024L;

        private static readonly StringComparer IdComparer = StringComparer.Ordinal;
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly HashSet<string> SupportedUnitLocalizationLanguages = new HashSet<string>(
            new[] { "en-US", "de-DE", "ja-JP", "ko-KR", "zh-CN", "zh-TW", "fr-FR", "es-XL", "pt-BR", "ru-RU" },
            StringComparer.Ordinal);
        private static readonly HashSet<string> SupportedUnitTypes = new HashSet<string>(
            new[] { "Ground", "Air", "Support", "Stationary" }, StringComparer.Ordinal);
        private static readonly HashSet<string> SupportedRelicRarities = new HashSet<string>(
            new[] { "Common", "Rare", "UltraRare" }, StringComparer.Ordinal);
        private static readonly HashSet<string> SupportedRelicTechs = new HashSet<string>(
            new[] { "None", "Colorless", "Mech", "Tank", "Ancient", "Specialist", "All" },
            StringComparer.Ordinal);
        private static readonly HashSet<string> SupportedRelicSystemTags = new HashSet<string>(
            new[] { "None", "Melee", "Shield", "Spawn", "Armor", "Mech", "Robo", "Heal", "Turret", "Fire",
                "Bounty", "Panic", "Slow", "Stun", "Teleport", "AoE", "ManaUse", "Rush", "Turbo", "Strength",
                "Robust", "SkillRangeUse", "Bomb", "Tank", "Vehicle", "EffectDuration", "SpawnDuration",
                "CriticalHits", "Stealth", "Zombo", "Sniper", "Regen", "EconomySupport", "All" },
            StringComparer.Ordinal);

        public static ContentPackValidationResult Validate(
            ContentPackDefinition pack,
            string packageRoot,
            IContentRegistrySnapshot registrySnapshot,
            IAiModuleValidator aiModuleValidator)
        {
            var diagnostics = new List<ContentDiagnostic>();
            if (pack == null)
            {
                Add(diagnostics, "pack.required", "content.json", "The content pack definition is missing.");
                return new ContentPackValidationResult(null, diagnostics);
            }

            if (registrySnapshot == null)
            {
                Add(diagnostics, "registry.snapshot-required", "content.json", "A read-only registry snapshot is required to check ID collisions and external references.");
            }

            if (pack.schemaVersion != SupportedSchemaVersion)
            {
                Add(diagnostics, "schema.unsupported-version", "schemaVersion", "Only content schema version " + SupportedSchemaVersion + " is supported.");
            }

            var assetBundles = RequireArray(pack.assetBundles, "assetBundles", diagnostics);
            var units = RequireArray(pack.units, "units", diagnostics);
            var stages = RequireArray(pack.stages, "stages", diagnostics);
            var aiProfiles = RequireArray(pack.aiProfiles, "aiProfiles", diagnostics);
            ContentRelicDefinition[] relics = pack.relics ?? new ContentRelicDefinition[0];

            if (units.Length == 0 && stages.Length == 0 && aiProfiles.Length == 0 && relics.Length == 0)
            {
                Add(diagnostics, "content.empty", "content.json",
                    "A content pack must declare at least one unit, stage, or AI profile.");
            }

            if (assetBundles.Length > MaxAssetBundlesPerPack)
            {
                Add(diagnostics, "bundle.count-exceeds-limit", "assetBundles",
                    "A content pack can declare at most " + MaxAssetBundlesPerPack + " AssetBundles.");
            }

            string normalizedRoot = GetPackageRoot(packageRoot, diagnostics);
            if (normalizedRoot != null)
            {
                ScanForDllsAndReparsePoints(normalizedRoot, diagnostics);
            }

            var allIds = new HashSet<string>(IdComparer);
            var bundleIds = new HashSet<string>(IdComparer);
            var unitIds = new HashSet<string>(IdComparer);
            var productionBuildingIds = new HashSet<string>(IdComparer);
            var aiProfileIds = new HashSet<string>(IdComparer);
            var aiResourceNames = new HashSet<string>(StringComparer.Ordinal);
            var landscapeResourceNames = new HashSet<string>(StringComparer.Ordinal);
            var bundlePaths = new HashSet<string>(PathComparer);

            RegisterId(pack.packId, "packId", "package", allIds, registrySnapshot, diagnostics);

            if (assetBundles.Length == 0)
            {
                if (!String.IsNullOrWhiteSpace(pack.assetBundleManifestPath))
                {
                    Add(diagnostics, "bundle.manifest-unexpected", "assetBundleManifestPath",
                        "A data-only pack must omit assetBundleManifestPath when it declares no AssetBundles.");
                }
            }
            else
            {
                ValidateBundleOutputPath(pack.assetBundleManifestPath, "assetBundleManifestPath", diagnostics);
                string manifestResolvedPath;
                if (TryResolvePackageFile(normalizedRoot, pack.assetBundleManifestPath,
                        "assetBundleManifestPath", diagnostics, out manifestResolvedPath))
                {
                    string canonicalManifestPath = ToCanonicalRelativePath(normalizedRoot, manifestResolvedPath);
                    if (!bundlePaths.Add(canonicalManifestPath))
                    {
                        Add(diagnostics, "bundle.manifest-path-duplicate", "assetBundleManifestPath",
                            "The AssetBundle manifest bundle must use a different file path from every content bundle.");
                    }

                    if (ContainsReparsePoint(normalizedRoot, manifestResolvedPath))
                    {
                        Add(diagnostics, "path.reparse-point", "assetBundleManifestPath",
                            "The AssetBundle manifest path cannot traverse symbolic links, junctions, or other reparse points.");
                    }
                    else if (!File.Exists(manifestResolvedPath))
                    {
                        Add(diagnostics, "bundle.manifest-missing", "assetBundleManifestPath",
                            "The Unity AssetBundle manifest file does not exist inside the package.");
                    }
                }
            }

            for (int i = 0; i < assetBundles.Length; i++)
            {
                var bundle = assetBundles[i];
                string itemPath = "assetBundles[" + i + "]";
                if (bundle == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Asset bundle entries cannot be null.");
                    continue;
                }

                RegisterId(bundle.id, itemPath + ".id", "asset bundle", allIds, registrySnapshot, diagnostics);
                if (IsNamespacedId(bundle.id))
                {
                    bundleIds.Add(bundle.id);
                }

                string resolvedPath;
                ValidateBundleOutputPath(bundle.path, itemPath + ".path", diagnostics);
                if (TryResolvePackageFile(normalizedRoot, bundle.path, itemPath + ".path", diagnostics, out resolvedPath))
                {
                    string canonicalRelativePath = ToCanonicalRelativePath(normalizedRoot, resolvedPath);
                    if (!bundlePaths.Add(canonicalRelativePath))
                    {
                        Add(diagnostics, "bundle.path-duplicate", itemPath + ".path", "Each asset bundle must use a distinct package file path.");
                    }

                    if (ContainsReparsePoint(normalizedRoot, resolvedPath))
                    {
                        Add(diagnostics, "path.reparse-point", itemPath + ".path", "Asset bundle paths cannot traverse symbolic links, junctions, or other reparse points.");
                    }
                    else if (!File.Exists(resolvedPath))
                    {
                        Add(diagnostics, "bundle.file-missing", itemPath + ".path", "The asset bundle file does not exist inside the package.");
                    }
                }
            }

            for (int i = 0; i < units.Length; i++)
            {
                var unit = units[i];
                string itemPath = "units[" + i + "]";
                if (unit == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Unit entries cannot be null.");
                    continue;
                }

                RegisterId(unit.id, itemPath + ".id", "unit", allIds, registrySnapshot, diagnostics);
                if (!String.IsNullOrWhiteSpace(unit.unitType) && !SupportedUnitTypes.Contains(unit.unitType))
                {
                    Add(diagnostics, "unit.type-invalid", itemPath + ".unitType", "Unit type must be one of the game's supported unit types.");
                }
                if (IsNamespacedId(unit.id))
                {
                    unitIds.Add(unit.id);
                }

                bool hasNativeTemplate = !String.IsNullOrWhiteSpace(unit.nativeTemplateId);
                if (hasNativeTemplate)
                {
                    ValidateAssetName(unit.nativeTemplateId, itemPath + ".nativeTemplateId", diagnostics);
                    if (!String.IsNullOrWhiteSpace(unit.balanceAssetName) ||
                        !String.IsNullOrWhiteSpace(unit.prefabAssetName))
                    {
                        Add(diagnostics, "unit.source-conflict", itemPath,
                            "Choose either nativeTemplateId or the balance/prefab bundle assets, not both.");
                    }

                    bool hasVisualAsset = !String.IsNullOrWhiteSpace(unit.visualAssetName);
                    bool hasVisualRoot = !String.IsNullOrWhiteSpace(unit.visualRootPath);
                    if (hasVisualAsset != hasVisualRoot)
                    {
                        Add(diagnostics, "unit.visual-source-invalid", itemPath,
                            "A native-template visual requires both visualAssetName and visualRootPath.");
                    }

                    if (hasVisualAsset)
                    {
                        ValidateAssetName(unit.visualAssetName, itemPath + ".visualAssetName", diagnostics);
                        ValidateVisualRootPath(unit.visualRootPath, itemPath + ".visualRootPath", diagnostics);
                    }

                    if (!String.IsNullOrWhiteSpace(unit.cardImageAssetName) || hasVisualAsset)
                    {
                        ValidateBundleReference(unit.assetBundleId, itemPath + ".assetBundleId", bundleIds, diagnostics);
                        if (!String.IsNullOrWhiteSpace(unit.cardImageAssetName))
                        {
                            ValidateAssetName(unit.cardImageAssetName, itemPath + ".cardImageAssetName", diagnostics);
                        }
                    }
                    else if (!String.IsNullOrWhiteSpace(unit.assetBundleId))
                    {
                        Add(diagnostics, "unit.bundle-unused", itemPath + ".assetBundleId",
                            "A native-template unit only declares a bundle when it supplies cardImageAssetName or a visual overlay.");
                    }
                }
                else
                {
                    ValidateBundleReference(unit.assetBundleId, itemPath + ".assetBundleId", bundleIds, diagnostics);
                    ValidateAssetName(unit.balanceAssetName, itemPath + ".balanceAssetName", diagnostics);
                    ValidateAssetName(unit.prefabAssetName, itemPath + ".prefabAssetName", diagnostics);
                    ValidateAssetName(unit.cardImageAssetName, itemPath + ".cardImageAssetName", diagnostics);
                    if (!String.IsNullOrWhiteSpace(unit.visualAssetName) ||
                        !String.IsNullOrWhiteSpace(unit.visualRootPath))
                    {
                        Add(diagnostics, "unit.visual-source-conflict", itemPath,
                            "visualAssetName and visualRootPath are only valid with nativeTemplateId.");
                    }
                }

                ValidateUnitLocalizations(unit, itemPath + ".localizations", hasNativeTemplate, diagnostics);
                ValidateBalanceOverrides(unit.balanceOverrides, itemPath + ".balanceOverrides", "unit", "unit", diagnostics);
                ValidateAbilities(unit.abilities, itemPath + ".abilities", diagnostics);
                ValidateAiOverrides(unit.aiOverrides, itemPath + ".aiOverrides", diagnostics);
                ValidateActiveSkills(unit.activeSkills, itemPath + ".activeSkills", diagnostics);
                ValidateCombatBoosts(unit.combatBoosts, itemPath + ".combatBoosts", diagnostics);
                ValidateUnitUpgrades(unit.upgrades, itemPath + ".upgrades", allIds, diagnostics);
                ValidateUnitExperience(unit.experience, itemPath + ".experience", diagnostics);
                ValidateUnitSynergies(unit.synergies, itemPath + ".synergies", allIds, diagnostics);
                ValidateUnitVisualVariants(unit.visualVariants, itemPath + ".visualVariants", bundleIds, diagnostics);
                ValidateUnitSpawn(unit.spawn, itemPath + ".spawn", diagnostics);
                ValidateUnitStatusEffects(unit.statusEffects, itemPath + ".statusEffects", diagnostics);
                ValidateUnitProductionQueue(unit.productionQueue, itemPath + ".productionQueue", diagnostics);
                ValidateUnitResourceCosts(unit.resourceCosts, itemPath + ".resourceCosts", diagnostics);
                ValidateUnitMovement(unit.movement, itemPath + ".movement", diagnostics);
                ValidateUnitDetection(unit.detection, itemPath + ".detection", diagnostics);
                ValidateUnitWeapons(unit.weapons, itemPath + ".weapons", diagnostics);
                ValidateUnitArmor(unit.armor, itemPath + ".armor", diagnostics);
                ValidateUnitHealing(unit.healing, itemPath + ".healing", diagnostics);
                ValidateUnitStealth(unit.stealth, itemPath + ".stealth", diagnostics);
                ValidateProductionBuilding(unit.productionBuilding, itemPath + ".productionBuilding",
                    allIds, productionBuildingIds, bundleIds, registrySnapshot, diagnostics);
                ValidateProfileCardEligibility(unit, itemPath, diagnostics);
            }

            for (int i = 0; i < aiProfiles.Length; i++)
            {
                var profile = aiProfiles[i];
                string itemPath = "aiProfiles[" + i + "]";
                if (profile == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "AI profile entries cannot be null.");
                    continue;
                }

                RegisterId(profile.id, itemPath + ".id", "AI profile", allIds, registrySnapshot, diagnostics);
                if (IsNamespacedId(profile.id))
                {
                    aiProfileIds.Add(profile.id);
                }

                bool hasNativeTemplate = !String.IsNullOrWhiteSpace(profile.nativeTemplateId);
                string resourceName;
                if (hasNativeTemplate)
                {
                    ValidateAssetName(profile.nativeTemplateId, itemPath + ".nativeTemplateId", diagnostics);
                    if (!String.IsNullOrWhiteSpace(profile.assetBundleId) ||
                        !String.IsNullOrWhiteSpace(profile.prefabAssetName))
                    {
                        Add(diagnostics, "ai-profile.source-conflict", itemPath,
                            "Choose either nativeTemplateId or the bundle prefab source, not both.");
                    }

                    if (profile.scopeToReferencedStages)
                    {
                        if (!TryBuildScopedAiResourceName(profile.id, out resourceName))
                        {
                            Add(diagnostics, "ai-profile.scoped-resource-name-invalid", itemPath + ".id",
                                "A stage-scoped AI profile ID must produce a reserved RCL_ resource name of at most 96 ASCII characters.");
                        }

                        bool referencedByPackStage = false;
                        for (int stageIndex = 0; stageIndex < stages.Length && !referencedByPackStage; stageIndex++)
                        {
                            ContentStageDefinition stage = stages[stageIndex];
                            string[] stageProfileIds = stage == null || stage.aiProfileIds == null
                                ? new string[0]
                                : stage.aiProfileIds;
                            for (int profileIndex = 0; profileIndex < stageProfileIds.Length; profileIndex++)
                            {
                                if (String.Equals(stageProfileIds[profileIndex], profile.id, StringComparison.Ordinal))
                                {
                                    referencedByPackStage = true;
                                    break;
                                }
                            }
                        }

                        if (!referencedByPackStage)
                        {
                            Add(diagnostics, "ai-profile.scoped-stage-required", itemPath + ".scopeToReferencedStages",
                                "A stage-scoped native AI profile must be referenced by at least one stage in the same pack.");
                        }
                    }
                    else
                    {
                        Add(diagnostics, "ai-profile.native-scope-required", itemPath + ".scopeToReferencedStages",
                            "A native-template AI profile must use scopeToReferencedStages so its compiled modules bind only to an isolated clone.");
                        resourceName = profile.nativeTemplateId;
                    }
                }
                else
                {
                    if (profile.scopeToReferencedStages)
                    {
                        Add(diagnostics, "ai-profile.scoped-native-required", itemPath + ".scopeToReferencedStages",
                            "scopeToReferencedStages is only supported with nativeTemplateId.");
                    }

                    ValidateBundleReference(profile.assetBundleId, itemPath + ".assetBundleId", bundleIds, diagnostics);
                    ValidateAssetName(profile.prefabAssetName, itemPath + ".prefabAssetName", diagnostics);
                    if (!IsAiResourceName(profile.prefabAssetName))
                    {
                        Add(diagnostics, "ai-profile.resource-name-invalid", itemPath + ".prefabAssetName",
                            "AI prefab names must start with reserved prefix RCL_ and contain only ASCII letters, digits, and underscores.");
                    }

                    resourceName = profile.prefabAssetName;
                }

                if (!String.IsNullOrWhiteSpace(resourceName) && !aiResourceNames.Add(resourceName))
                {
                    Add(diagnostics, "ai-profile.resource-name-duplicate", itemPath,
                        "AI resource names must be unique within a content pack.");
                }

                string[] unitDependencies = profile.unitDependencies ?? new string[0];
                if (unitDependencies.Length > MaxAiUnitDependenciesPerProfile)
                {
                    Add(diagnostics, "ai-profile.unit-dependencies-limit", itemPath + ".unitDependencies",
                        "An AI profile can declare at most " + MaxAiUnitDependenciesPerProfile +
                        " unit dependencies.");
                }

                ValidateReferences(unitDependencies, itemPath + ".unitDependencies",
                    unitIds, registrySnapshot, true, diagnostics);

                ContentAiUnitEntryDefinition[] unitEntries = profile.unitEntries ??
                    new ContentAiUnitEntryDefinition[0];
                ContentAiProductionBuildingEntryDefinition[] productionBuildingEntries =
                    profile.productionBuildingEntries ?? new ContentAiProductionBuildingEntryDefinition[0];
                if (unitEntries.Length > MaxAiUnitEntriesPerProfile)
                {
                    Add(diagnostics, "ai-profile.unit-entries-limit", itemPath + ".unitEntries",
                        "An AI profile can add at most " + MaxAiUnitEntriesPerProfile + " unit entries.");
                }

                if (productionBuildingEntries.Length > MaxAiProductionBuildingEntriesPerProfile)
                {
                    Add(diagnostics, "ai-profile.production-building-entries-limit",
                        itemPath + ".productionBuildingEntries",
                        "An AI profile can add at most " + MaxAiProductionBuildingEntriesPerProfile +
                        " production-building entries.");
                }

                if ((unitEntries.Length > 0 || productionBuildingEntries.Length > 0) &&
                    (!hasNativeTemplate || !profile.scopeToReferencedStages))
                {
                    string extensionPath = unitEntries.Length > 0
                        ? itemPath + ".unitEntries"
                        : itemPath + ".productionBuildingEntries";
                    Add(diagnostics, "ai-profile.unit-entries-require-scoped-native",
                        extensionPath,
                        "AI unit and production-building entries can only extend an isolated, stage-scoped native-template profile.");
                }

                var referencedAiUnits = new List<string>(unitEntries.Length);
                for (int unitIndex = 0; unitIndex < unitEntries.Length; unitIndex++)
                {
                    ContentAiUnitEntryDefinition unitEntry = unitEntries[unitIndex];
                    string unitPath = itemPath + ".unitEntries[" + unitIndex + "]";
                    if (unitEntry == null)
                    {
                        Add(diagnostics, "entry.null", unitPath, "AI unit entries cannot be null.");
                        continue;
                    }

                    referencedAiUnits.Add(unitEntry.unitId);
                    if (unitEntry.baseIndex < 0 || unitEntry.baseIndex > MaxAiBaseIndex)
                    {
                        Add(diagnostics, "ai-unit-entry.base-index-invalid", unitPath + ".baseIndex",
                            "AI base indices must be between 0 and " + MaxAiBaseIndex + ".");
                    }

                    if (unitEntry.startingInAscensionLevel < 0 ||
                        unitEntry.startingInAscensionLevel > MaxAiUnitStartingAscensionLevel)
                    {
                        Add(diagnostics, "ai-unit-entry.ascension-invalid",
                            unitPath + ".startingInAscensionLevel",
                            "Starting ascension must be between 0 and " + MaxAiUnitStartingAscensionLevel + ".");
                    }

                    if (unitEntry.countAtStart < 1 || unitEntry.countAtStart > MaxAiUnitCountAtStart)
                    {
                        Add(diagnostics, "ai-unit-entry.count-at-start-invalid", unitPath + ".countAtStart",
                            "AI unit entries must start with 1 to " + MaxAiUnitCountAtStart + " units.");
                    }

                    if (unitEntry.maxLiving < unitEntry.countAtStart ||
                        unitEntry.maxLiving > MaxAiUnitMaxLiving)
                    {
                        Add(diagnostics, "ai-unit-entry.max-living-invalid", unitPath + ".maxLiving",
                            "maxLiving must be at least countAtStart and at most " + MaxAiUnitMaxLiving + ".");
                    }
                }

                ValidateReferences(referencedAiUnits.ToArray(), itemPath + ".unitEntries",
                    unitIds, registrySnapshot, true, diagnostics);

                var referencedProductionProducts = new List<string>(productionBuildingEntries.Length);
                var productionBuildingEntryKeys = new HashSet<string>(StringComparer.Ordinal);
                for (int buildingIndex = 0; buildingIndex < productionBuildingEntries.Length; buildingIndex++)
                {
                    ContentAiProductionBuildingEntryDefinition entry = productionBuildingEntries[buildingIndex];
                    string entryPath = itemPath + ".productionBuildingEntries[" + buildingIndex + "]";
                    if (entry == null)
                    {
                        Add(diagnostics, "entry.null", entryPath,
                            "AI production-building entries cannot be null.");
                        continue;
                    }

                    referencedProductionProducts.Add(entry.unitId);
                    if (entry.baseIndex < 0 || entry.baseIndex > MaxAiBaseIndex)
                    {
                        Add(diagnostics, "ai-production-building-entry.base-index-invalid",
                            entryPath + ".baseIndex",
                            "AI base indices must be between 0 and " + MaxAiBaseIndex + ".");
                    }

                    if (entry.startingInAscensionLevel < 0 ||
                        entry.startingInAscensionLevel > MaxAiUnitStartingAscensionLevel)
                    {
                        Add(diagnostics, "ai-production-building-entry.ascension-invalid",
                            entryPath + ".startingInAscensionLevel",
                            "Starting ascension must be between 0 and " + MaxAiUnitStartingAscensionLevel + ".");
                    }

                    if (entry.maxLiving < 1 || entry.maxLiving > MaxAiUnitMaxLiving)
                    {
                        Add(diagnostics, "ai-production-building-entry.max-living-invalid",
                            entryPath + ".maxLiving",
                            "maxLiving must be between 1 and " + MaxAiUnitMaxLiving + ".");
                    }

                    if (!String.IsNullOrWhiteSpace(entry.unitId))
                    {
                        string key = entry.baseIndex.ToString(CultureInfo.InvariantCulture) + "\0" + entry.unitId;
                        if (!productionBuildingEntryKeys.Add(key))
                        {
                            Add(diagnostics, "reference.duplicate", entryPath + ".unitId",
                                "A production building can only be added once per AI base.");
                        }
                    }
                }

                ValidateReferences(referencedProductionProducts.ToArray(),
                    itemPath + ".productionBuildingEntries", unitIds, registrySnapshot, true, diagnostics);

                var modules = RequireArray(profile.modules, itemPath + ".modules", diagnostics);
                if (modules.Length == 0)
                {
                    Add(diagnostics, "ai-profile.modules-empty", itemPath + ".modules",
                        "AI profiles must declare at least one compiled module.");
                }
                else if (modules.Length > MaxAiModulesPerProfile)
                {
                    Add(diagnostics, "ai-profile.modules-limit", itemPath + ".modules",
                        "An AI profile can declare at most " + MaxAiModulesPerProfile + " compiled modules.");
                }

                for (int moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
                {
                    ValidateAiModule(modules[moduleIndex], itemPath + ".modules[" + moduleIndex + "]", aiModuleValidator, diagnostics);
                }
            }

            if (relics.Length > MaxRelicsPerPack)
            {
                Add(diagnostics, "relic.count-exceeds-limit", "relics",
                    "A content pack can declare at most " + MaxRelicsPerPack + " relics.");
            }

            for (int i = 0; i < relics.Length; i++)
            {
                ValidateRelic(relics[i], "relics[" + i + "]", allIds, bundleIds, unitIds,
                    registrySnapshot, diagnostics);
            }

            for (int i = 0; i < stages.Length; i++)
            {
                var stage = stages[i];
                string itemPath = "stages[" + i + "]";
                if (stage == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Stage entries cannot be null.");
                    continue;
                }

                RegisterId(stage.id, itemPath + ".id", "stage", allIds, registrySnapshot, diagnostics);
                bool hasNativeTemplate = !String.IsNullOrWhiteSpace(stage.nativeTemplateId);
                if (hasNativeTemplate)
                {
                    ValidateAssetName(stage.nativeTemplateId, itemPath + ".nativeTemplateId", diagnostics);
                    if (!String.IsNullOrWhiteSpace(stage.assetBundleId) ||
                        !String.IsNullOrWhiteSpace(stage.assetName))
                    {
                        Add(diagnostics, "stage.source-conflict", itemPath,
                            "Choose either nativeTemplateId or the bundle asset source, not both.");
                    }
                }
                else
                {
                    ValidateBundleReference(stage.assetBundleId, itemPath + ".assetBundleId", bundleIds, diagnostics);
                    ValidateAssetName(stage.assetName, itemPath + ".assetName", diagnostics);
                }

                ValidateStageParameterOverrides(stage.parameterOverrides, itemPath + ".parameterOverrides", diagnostics);
                if (stage.mapSizeMin < 1 || stage.mapSizeMin > 256)
                {
                    Add(diagnostics, "stage.map-size-min-invalid", itemPath + ".mapSizeMin", "mapSizeMin must be between 1 and 256.");
                }

                if (stage.mapSizeMax < 1 || stage.mapSizeMax > 256 || stage.mapSizeMax < stage.mapSizeMin)
                {
                    Add(diagnostics, "stage.map-size-max-invalid", itemPath + ".mapSizeMax", "mapSizeMax must be between mapSizeMin and 256.");
                }

                ValidateStageLandscape(stage, itemPath + ".landscape", diagnostics);
                if (stage.landscape != null)
                {
                    string landscapeResourceName;
                    if (TryBuildLandscapeResourceName(stage.id, out landscapeResourceName) &&
                        !landscapeResourceNames.Add(landscapeResourceName))
                    {
                        Add(diagnostics, "stage.landscape-resource-name-duplicate", itemPath + ".landscape",
                            "Stage IDs must produce unique LandscapeGenerator resource names within a pack.");
                    }
                }

                var stageUnits = RequireArray(stage.unitIds, itemPath + ".unitIds", diagnostics);
                ValidateReferences(stageUnits, itemPath + ".unitIds", unitIds, registrySnapshot, true, diagnostics);

                var stageAiProfiles = RequireArray(stage.aiProfileIds, itemPath + ".aiProfileIds", diagnostics);
                ValidateReferences(stageAiProfiles, itemPath + ".aiProfileIds", aiProfileIds, registrySnapshot, false, diagnostics);
            }

            return new ContentPackValidationResult(pack, diagnostics);
        }

        internal static bool TryBuildScopedAiResourceName(string profileId, out string resourceName)
        {
            resourceName = null;
            if (String.IsNullOrWhiteSpace(profileId))
            {
                return false;
            }

            var builder = new System.Text.StringBuilder("RCL_");
            for (int i = 0; i < profileId.Length; i++)
            {
                char value = profileId[i];
                bool asciiLetter = (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z');
                bool asciiDigit = value >= '0' && value <= '9';
                builder.Append(asciiLetter || asciiDigit ? value : '_');
            }

            if (builder.Length < 5 || builder.Length > 96)
            {
                return false;
            }

            resourceName = builder.ToString();
            return true;
        }

        internal static bool TryBuildLandscapeResourceName(string stageId, out string resourceName)
        {
            resourceName = null;
            if (String.IsNullOrWhiteSpace(stageId))
            {
                return false;
            }

            var builder = new System.Text.StringBuilder("RCL_Landscape_");
            for (int i = 0; i < stageId.Length; i++)
            {
                char value = stageId[i];
                bool asciiLetter = (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z');
                bool asciiDigit = value >= '0' && value <= '9';
                builder.Append(asciiLetter || asciiDigit ? value : '_');
            }

            if (builder.Length < 16 || builder.Length > 96)
            {
                return false;
            }

            resourceName = builder.ToString();
            return true;
        }

        private static void ValidateStageLandscape(
            ContentStageDefinition stage,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            ContentStageLandscapeDefinition landscape = stage.landscape;
            if (landscape == null)
            {
                return;
            }

            ValidateAssetName(landscape.nativeTemplateId, path + ".nativeTemplateId", diagnostics);
            string resourceName;
            if (!TryBuildLandscapeResourceName(stage.id, out resourceName))
            {
                Add(diagnostics, "stage.landscape-resource-name-invalid", path,
                    "The stage ID must produce a reserved RCL_Landscape_ resource name of at most 96 ASCII characters.");
            }

            if (landscape.randomSeed < 0 || landscape.randomSeed > 99998)
            {
                Add(diagnostics, "stage.landscape-seed-invalid", path + ".randomSeed",
                    "Landscape seeds must be between 0 and 99998.");
            }

            if (landscape.mapSize < 1 || landscape.mapSize > 255 || landscape.mapSize % 2 == 0)
            {
                Add(diagnostics, "stage.landscape-map-size-invalid", path + ".mapSize",
                    "Landscape mapSize must be an odd integer between 1 and 255.");
            }
            else if (landscape.mapSize < stage.mapSizeMin || landscape.mapSize > stage.mapSizeMax)
            {
                Add(diagnostics, "stage.landscape-map-size-out-of-range", path + ".mapSize",
                    "Landscape mapSize must fit the containing stage's mapSizeMin and mapSizeMax.");
            }

            ContentLandscapeLocalizationDefinition[] localizations = landscape.localizations;
            if (localizations == null || localizations.Length == 0)
            {
                Add(diagnostics, "stage.landscape-localization-required", path + ".localizations",
                    "A custom landscape must declare an en-US display name.");
                return;
            }

            var languages = new HashSet<string>(StringComparer.Ordinal);
            bool hasEnglishName = false;
            for (int i = 0; i < localizations.Length; i++)
            {
                ContentLandscapeLocalizationDefinition localization = localizations[i];
                string itemPath = path + ".localizations[" + i + "]";
                if (localization == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Landscape localization entries cannot be null.");
                    continue;
                }

                if (String.IsNullOrWhiteSpace(localization.language) ||
                    !SupportedUnitLocalizationLanguages.Contains(localization.language))
                {
                    Add(diagnostics, "stage.landscape-localization-language-unsupported", itemPath + ".language",
                        "Landscape localization language must match one of the game's supported language IDs exactly.");
                }
                else if (!languages.Add(localization.language))
                {
                    Add(diagnostics, "stage.landscape-localization-language-duplicate", itemPath + ".language",
                        "A landscape can declare at most one localization for each language.");
                }

                if (String.IsNullOrWhiteSpace(localization.name))
                {
                    Add(diagnostics, "stage.landscape-localization-name-required", itemPath + ".name",
                        "Every landscape localization must include a non-empty display name.");
                }
                else if (String.Equals(localization.language, "en-US", StringComparison.Ordinal))
                {
                    hasEnglishName = true;
                }
            }

            if (!hasEnglishName)
            {
                Add(diagnostics, "stage.landscape-localization-english-name-required", path + ".localizations",
                    "Landscape localizations must include a non-empty en-US display name for fallback.");
            }
        }

        private static T[] RequireArray<T>(T[] value, string path, List<ContentDiagnostic> diagnostics)
        {
            if (value != null)
            {
                return value;
            }

            Add(diagnostics, "array.required", path, "This field is required; use an empty array when there are no entries.");
            return new T[0];
        }

        private static void RegisterId(
            string id,
            string path,
            string itemKind,
            HashSet<string> allIds,
            IContentRegistrySnapshot registrySnapshot,
            List<ContentDiagnostic> diagnostics)
        {
            ValidateNamespacedId(id, path, diagnostics);
            if (!IsNamespacedId(id))
            {
                return;
            }

            if (!allIds.Add(id))
            {
                Add(diagnostics, "id.duplicate-in-package", path, "The ID is already used by another definition in this package.");
            }

            if (registrySnapshot != null)
            {
                try
                {
                    if (registrySnapshot.ContainsId(id))
                    {
                        Add(diagnostics, "id.collision", path, "The " + itemKind + " ID is already present in the registry snapshot.");
                    }
                }
                catch (Exception exception)
                {
                    Add(diagnostics, "registry.snapshot-failed", path, "Could not query the registry snapshot: " + exception.Message);
                }
            }
        }

        private static void RegisterProductionBuildingId(
            string id,
            string path,
            HashSet<string> allIds,
            HashSet<string> productionBuildingIds,
            IContentRegistrySnapshot registrySnapshot,
            List<ContentDiagnostic> diagnostics)
        {
            ValidateNamespacedId(id, path, diagnostics);
            if (!IsNamespacedId(id))
            {
                return;
            }

            bool repeatedProductionBuildingId = !productionBuildingIds.Add(id);
            if (!allIds.Add(id))
            {
                if (repeatedProductionBuildingId)
                {
                    Add(diagnostics, "production-building.id-shared", path,
                        "Each unit must declare a distinct production building ID; production buildings cannot be shared between units.");
                }
                else
                {
                    Add(diagnostics, "id.duplicate-in-package", path,
                        "The ID is already used by another definition in this package.");
                }
            }

            if (registrySnapshot != null)
            {
                try
                {
                    if (registrySnapshot.ContainsId(id))
                    {
                        Add(diagnostics, "id.collision", path,
                            "The production building ID is already present in the registry snapshot.");
                    }
                }
                catch (Exception exception)
                {
                    Add(diagnostics, "registry.snapshot-failed", path,
                        "Could not query the registry snapshot: " + exception.Message);
                }
            }
        }

        private static void ValidateProductionBuilding(
            ContentProductionBuildingDefinition productionBuilding,
            string path,
            HashSet<string> allIds,
            HashSet<string> productionBuildingIds,
            HashSet<string> bundleIds,
            IContentRegistrySnapshot registrySnapshot,
            List<ContentDiagnostic> diagnostics)
        {
            if (productionBuilding == null)
            {
                Add(diagnostics, "production-building.required", path,
                    "Every unit must declare its own production building.");
                return;
            }

            RegisterProductionBuildingId(productionBuilding.id, path + ".id", allIds,
                productionBuildingIds, registrySnapshot, diagnostics);

            // Whether this native template is actually a Factory-role entity can only
            // be checked against the live game registry by ContentPackRuntimeLoader.
            ValidateAssetName(productionBuilding.nativeTemplateId,
                path + ".nativeTemplateId", diagnostics);
            if (!String.IsNullOrWhiteSpace(productionBuilding.nativeTemplateId) &&
                productionBuilding.nativeTemplateId.Length > 256)
            {
                Add(diagnostics, "production-building.template-id-too-long",
                    path + ".nativeTemplateId", "Native production building template IDs cannot exceed 256 characters.");
            }

            bool hasVisualAsset = !String.IsNullOrWhiteSpace(productionBuilding.visualAssetName);
            bool hasVisualRoot = !String.IsNullOrWhiteSpace(productionBuilding.visualRootPath);
            if (hasVisualAsset != hasVisualRoot)
            {
                Add(diagnostics, "production-building.visual-source-invalid", path,
                    "A production-building visual requires visualAssetName and visualRootPath together.");
            }

            if (hasVisualAsset || hasVisualRoot ||
                !String.IsNullOrWhiteSpace(productionBuilding.cardImageAssetName))
            {
                ValidateBundleReference(productionBuilding.assetBundleId, path + ".assetBundleId",
                    bundleIds, diagnostics);
                if (!String.IsNullOrWhiteSpace(productionBuilding.cardImageAssetName))
                {
                    ValidateAssetName(productionBuilding.cardImageAssetName,
                        path + ".cardImageAssetName", diagnostics);
                }

                if (hasVisualAsset)
                {
                    ValidateAssetName(productionBuilding.visualAssetName,
                        path + ".visualAssetName", diagnostics);
                }

                if (hasVisualRoot)
                {
                    ValidateVisualRootPath(productionBuilding.visualRootPath,
                        path + ".visualRootPath", diagnostics,
                        "production-building.visual-root-path-invalid");
                }
            }
            else if (!String.IsNullOrWhiteSpace(productionBuilding.assetBundleId))
            {
                Add(diagnostics, "production-building.bundle-unused", path + ".assetBundleId",
                    "A production building only declares a bundle when it supplies a card image or visual overlay.");
            }

            ValidateBalanceOverrides(productionBuilding.balanceOverrides,
                path + ".balanceOverrides", "production-building", "production building", diagnostics);
            ValidateLocalizations(productionBuilding.localizations, path + ".localizations",
                false, "production-building", "Production building", diagnostics);
        }

        private static void ValidateProfileCardEligibility(
            ContentUnitDefinition unit,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            ContentProductionBuildingDefinition building = unit.productionBuilding;
            if (building == null)
            {
                return;
            }

            string productLevel;
            string buildingLevel;
            bool hasProductLevel = TryGetBalanceOverride(
                unit.balanceOverrides, "neededExperienceLevel", out productLevel);
            bool hasBuildingLevel = TryGetBalanceOverride(
                building.balanceOverrides, "neededExperienceLevel", out buildingLevel);
            if (!hasProductLevel || !hasBuildingLevel)
            {
                Add(diagnostics, "unit.profile-unlock-required", path,
                    "A unit and its production building must each declare neededExperienceLevel so both reward query paths use the profile unlock tier.");
            }
            else
            {
                int productLevelValue;
                int buildingLevelValue;
                bool productLevelValid = Int32.TryParse(productLevel, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out productLevelValue);
                bool buildingLevelValid = Int32.TryParse(buildingLevel, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out buildingLevelValue);
                if (productLevelValid && buildingLevelValid && productLevelValue != buildingLevelValue)
                {
                    Add(diagnostics, "unit.profile-unlock-mismatch",
                        path + ".productionBuilding.balanceOverrides.neededExperienceLevel",
                        "The production building and its unit must use the same persistent profile unlock level.");
                }
            }

            string productRarity;
            string buildingRarity;
            bool hasProductRarity = TryGetBalanceOverride(unit.balanceOverrides, "rarity", out productRarity);
            bool hasBuildingRarity = TryGetBalanceOverride(
                building.balanceOverrides, "rarity", out buildingRarity);
            if (!hasProductRarity || !hasBuildingRarity)
            {
                Add(diagnostics, "unit.card-rarity-required", path,
                    "A unit and its production building must each declare rarity so both reward query paths use the same card tier.");
            }
            else if (!String.Equals(productRarity, buildingRarity, StringComparison.Ordinal))
            {
                Add(diagnostics, "unit.card-rarity-mismatch",
                    path + ".productionBuilding.balanceOverrides.rarity",
                    "The production building and its unit must use the same card rarity.");
            }
        }

        private static bool TryGetBalanceOverride(
            ContentBalanceOverride[] overrides,
            string name,
            out string value)
        {
            if (overrides != null)
            {
                for (int i = 0; i < overrides.Length; i++)
                {
                    ContentBalanceOverride entry = overrides[i];
                    if (entry != null && String.Equals(entry.name, name, StringComparison.Ordinal))
                    {
                        value = entry.value;
                        return true;
                    }
                }
            }

            value = null;
            return false;
        }

        private static void ValidateNamespacedId(string id, string path, List<ContentDiagnostic> diagnostics)
        {
            if (!IsNamespacedId(id))
            {
                Add(diagnostics, "id.invalid", path, "IDs must use the form namespace:local-id with lowercase ASCII letters, digits, dots, underscores, or hyphens.");
            }
        }

        private static bool IsNamespacedId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            int separator = id.IndexOf(':');
            if (separator < 1 || separator != id.LastIndexOf(':') || separator == id.Length - 1)
            {
                return false;
            }

            if (!IsAsciiLowerLetter(id[0]) || !IsNamespaceTail(id, 1, separator))
            {
                return false;
            }

            if (!IsIdCharacter(id[separator + 1], true))
            {
                return false;
            }

            for (int i = separator + 2; i < id.Length; i++)
            {
                if (!IsIdCharacter(id[i], false))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsNamespaceTail(string value, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                char c = value[i];
                if (!IsAsciiLowerLetter(c) && !IsAsciiDigit(c) && c != '.' && c != '_' && c != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsIdCharacter(char value, bool firstLocalCharacter)
        {
            return IsAsciiLowerLetter(value) || IsAsciiDigit(value) || (!firstLocalCharacter && (value == '.' || value == '_' || value == '-'));
        }

        private static bool IsAsciiLowerLetter(char value)
        {
            return value >= 'a' && value <= 'z';
        }

        private static bool IsAsciiDigit(char value)
        {
            return value >= '0' && value <= '9';
        }

        private static void ValidateBundleReference(string bundleId, string path, HashSet<string> bundleIds, List<ContentDiagnostic> diagnostics)
        {
            ValidateNamespacedId(bundleId, path, diagnostics);
            if (IsNamespacedId(bundleId) && !bundleIds.Contains(bundleId))
            {
                Add(diagnostics, "reference.bundle-unresolved", path, "Asset bundle references must resolve to a bundle declared in this package.");
            }
        }

        private static void ValidateAssetName(string assetName, string path, List<ContentDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(assetName))
            {
                Add(diagnostics, "asset-name.required", path, "Asset names must be non-empty.");
                return;
            }

            if (assetName.StartsWith("/", StringComparison.Ordinal) ||
                assetName.EndsWith("/", StringComparison.Ordinal) ||
                assetName.IndexOf('\\') >= 0 || assetName.IndexOf(':') >= 0)
            {
                Add(diagnostics, "asset.name-invalid", path,
                    "Asset names must be relative Unity asset paths using forward slashes.");
                return;
            }

            string[] segments = assetName.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(segments[i]) || segments[i] == "." || segments[i] == "..")
                {
                    Add(diagnostics, "asset.name-invalid", path,
                        "Asset names cannot contain empty, current-directory, or parent-directory path segments.");
                    return;
                }
            }
        }

        private static void ValidateVisualRootPath(
            string value,
            string path,
            List<ContentDiagnostic> diagnostics,
            string diagnosticCode = "unit.visual-root-path-invalid")
        {
            if (String.IsNullOrWhiteSpace(value) || value.StartsWith("/", StringComparison.Ordinal) ||
                value.EndsWith("/", StringComparison.Ordinal) || value.IndexOf('\\') >= 0)
            {
                Add(diagnostics, diagnosticCode, path,
                    "visualRootPath must be a relative Unity Transform path using forward slashes.");
                return;
            }

            string[] segments = value.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (String.IsNullOrWhiteSpace(segments[i]) || segments[i] == "." || segments[i] == "..")
                {
                    Add(diagnostics, diagnosticCode, path,
                        "visualRootPath cannot contain empty, current-directory, or parent-directory segments.");
                    return;
                }
            }
        }

        private static void ValidateBalanceOverrides(
            ContentBalanceOverride[] overrides,
            string path,
            string codePrefix,
            string ownerLabel,
            List<ContentDiagnostic> diagnostics)
        {
            if (overrides == null)
            {
                return;
            }

            if (overrides.Length > MaxBalanceOverridesPerDefinition)
            {
                Add(diagnostics, codePrefix + ".balance-overrides-limit", path,
                    "A " + ownerLabel + " can declare at most " + MaxBalanceOverridesPerDefinition + " balance overrides.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < overrides.Length; i++)
            {
                ContentBalanceOverride balanceOverride = overrides[i];
                string itemPath = path + "[" + i + "]";
                if (balanceOverride == null)
                {
                    Add(diagnostics, "entry.null", itemPath,
                        "Balance override entries for a " + ownerLabel + " cannot be null.");
                    continue;
                }

                if (String.IsNullOrWhiteSpace(balanceOverride.name) ||
                    !names.Add(balanceOverride.name))
                {
                    Add(diagnostics, codePrefix + ".balance-override-name-invalid", itemPath + ".name",
                        "Balance override names must be non-empty and unique within the " + ownerLabel + ".");
                }

                if (!TryValidateBalanceOverrideValue(balanceOverride.name, balanceOverride.value))
                {
                    Add(diagnostics, codePrefix + ".balance-override-invalid", itemPath,
                        "The override must target an allowlisted numeric balance field and contain a finite, non-negative invariant-culture value.");
                }
            }
        }

        private static void ValidateAbilities(
            ContentAbilityDefinition[] abilities,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (abilities == null)
            {
                return;
            }
            for (int i = 0; i < abilities.Length; i++)
            {
                var ability = abilities[i];
                string itemPath = path + "[" + i + "]";
                if (ability == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Ability entries cannot be null.");
                    continue;
                }
                ValidateNamespacedId(ability.id, itemPath + ".id", diagnostics);
                if (float.IsNaN(ability.value) || float.IsInfinity(ability.value))
                {
                    Add(diagnostics, "ability.value-invalid", itemPath + ".value", "Ability value must be a finite number.");
                }
            }
        }

        private static void ValidateAiOverrides(
            ContentAiOverrideDefinition[] overrides,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (overrides == null)
            {
                return;
            }
            for (int i = 0; i < overrides.Length; i++)
            {
                var ovr = overrides[i];
                string itemPath = path + "[" + i + "]";
                if (ovr == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "AI override entries cannot be null.");
                    continue;
                }
                if (String.IsNullOrWhiteSpace(ovr.parameter))
                {
                    Add(diagnostics, "ai-override.parameter-required", itemPath + ".parameter", "AI override parameter must be non-empty.");
                }
                if (ovr.value == null)
                {
                    Add(diagnostics, "ai-override.value-required", itemPath + ".value", "AI override value must be provided.");
                }
            }
        }
        private static void ValidateActiveSkills(
            ContentActiveSkillDefinition[] skills,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (skills == null) return;
            for (int i = 0; i < skills.Length; i++)
            {
                var skill = skills[i];
                string itemPath = path + "[" + i + "]";
                if (skill == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Active skill entries cannot be null.");
                    continue;
                }
                ValidateNamespacedId(skill.id, itemPath + ".id", diagnostics);
                if (String.IsNullOrWhiteSpace(skill.skillType) ||
                    (skill.skillType != "Position" && skill.skillType != "Target" && skill.skillType != "Self"))
                {
                    Add(diagnostics, "active-skill.type-invalid", itemPath + ".skillType",
                        "Active skill type must be Position, Target, or Self.");
                }
                if (skill.cooldown < 0 || skill.cooldown > 300)
                {
                    Add(diagnostics, "active-skill.cooldown-invalid", itemPath + ".cooldown",
                        "Active skill cooldown must be between 0 and 300.");
                }
                if (skill.manaCost < 0 || skill.manaCost > 1000)
                {
                    Add(diagnostics, "active-skill.manaCost-invalid", itemPath + ".manaCost",
                        "Active skill mana cost must be between 0 and 1000.");
                }
            }
        }

        private static void ValidateCombatBoosts(
            ContentCombatBoostDefinition[] boosts,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (boosts == null) return;
            for (int i = 0; i < boosts.Length; i++)
            {
                var boost = boosts[i];
                string itemPath = path + "[" + i + "]";
                if (boost == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Combat boost entries cannot be null.");
                    continue;
                }
                ValidateNamespacedId(boost.id, itemPath + ".id", diagnostics);
                if (String.IsNullOrWhiteSpace(boost.statName))
                {
                    Add(diagnostics, "combat-boost.statName-required", itemPath + ".statName",
                        "Combat boost stat name must be non-empty.");
                }
                if (boost.multiplier < 0 || boost.multiplier > 10)
                {
                    Add(diagnostics, "combat-boost.multiplier-invalid", itemPath + ".multiplier",
                        "Combat boost multiplier must be between 0 and 10.");
                }
                if (boost.duration < 0 || boost.duration > 300)
                {
                    Add(diagnostics, "combat-boost.duration-invalid", itemPath + ".duration",
                        "Combat boost duration must be between 0 and 300.");
                }
            }
        }
        private static void ValidateUnitUpgrades(
            ContentUnitUpgradeDefinition[] upgrades,
            string path,
            HashSet<string> allIds,
            List<ContentDiagnostic> diagnostics)
        {
            if (upgrades == null) return;
            for (int i = 0; i < upgrades.Length; i++)
            {
                var upgrade = upgrades[i];
                string itemPath = path + "[" + i + "]";
                if (upgrade == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Upgrade entries cannot be null.");
                    continue;
                }
                if (String.IsNullOrWhiteSpace(upgrade.targetUnitId))
                {
                    Add(diagnostics, "unit-upgrade.target-required", itemPath + ".targetUnitId",
                        "Upgrade target unit ID must be non-empty.");
                }
                else if (!allIds.Contains(upgrade.targetUnitId))
                {
                    Add(diagnostics, "unit-upgrade.target-unknown", itemPath + ".targetUnitId",
                        "Upgrade target unit must be a declared or native unit ID.");
                }
                if (upgrade.requiredExperienceLevel < 0)
                {
                    Add(diagnostics, "unit-upgrade.level-invalid", itemPath + ".requiredExperienceLevel",
                        "Required experience level must be non-negative.");
                }
                if (upgrade.cost < 0)
                {
                    Add(diagnostics, "unit-upgrade.cost-invalid", itemPath + ".cost",
                        "Upgrade cost must be non-negative.");
                }
            }
        }

        private static void ValidateUnitExperience(
            ContentUnitExperienceDefinition experience,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (experience == null) return;
            if (experience.experiencePerLevel < 0)
            {
                Add(diagnostics, "unit-experience.per-level-invalid", path + ".experiencePerLevel",
                    "Experience per level must be non-negative.");
            }
            if (experience.maxLevel < 0 || experience.maxLevel > 100)
            {
                Add(diagnostics, "unit-experience.max-level-invalid", path + ".maxLevel",
                    "Max level must be between 0 and 100.");
            }
            if (experience.experienceGainMultiplier < 0 || experience.experienceGainMultiplier > 10)
            {
                Add(diagnostics, "unit-experience.gain-multiplier-invalid", path + ".experienceGainMultiplier",
                    "Experience gain multiplier must be between 0 and 10.");
            }
            if (experience.levelUpBonus < 0)
            {
                Add(diagnostics, "unit-experience.level-up-bonus-invalid", path + ".levelUpBonus",
                    "Level up bonus must be non-negative.");
            }
        }

        private static void ValidateUnitSynergies(
            ContentUnitSynergyDefinition[] synergies,
            string path,
            HashSet<string> allIds,
            List<ContentDiagnostic> diagnostics)
        {
            if (synergies == null) return;
            for (int i = 0; i < synergies.Length; i++)
            {
                var synergy = synergies[i];
                string itemPath = path + "[" + i + "]";
                if (synergy == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Synergy entries cannot be null.");
                    continue;
                }
                ValidateNamespacedId(synergy.id, itemPath + ".id", diagnostics);
                if (String.IsNullOrWhiteSpace(synergy.statName))
                {
                    Add(diagnostics, "unit-synergy.statName-required", itemPath + ".statName",
                        "Synergy stat name must be non-empty.");
                }
                if (synergy.multiplier < 0 || synergy.multiplier > 10)
                {
                    Add(diagnostics, "unit-synergy.multiplier-invalid", itemPath + ".multiplier",
                        "Synergy multiplier must be between 0 and 10.");
                }
                if (synergy.radius < 0)
                {
                    Add(diagnostics, "unit-synergy.radius-invalid", itemPath + ".radius",
                        "Synergy radius must be non-negative.");
                }
                if (synergy.targetUnitIds != null)
                {
                    for (int j = 0; j < synergy.targetUnitIds.Length; j++)
                    {
                        if (String.IsNullOrWhiteSpace(synergy.targetUnitIds[j]) ||
                            !allIds.Contains(synergy.targetUnitIds[j]))
                        {
                            Add(diagnostics, "unit-synergy.target-unknown", itemPath + ".targetUnitIds[" + j + "]",
                                "Synergy target unit must be a declared or native unit ID.");
                        }
                    }
                }
            }
        }

        private static void ValidateUnitVisualVariants(
            ContentUnitVisualVariantDefinition[] variants,
            string path,
            HashSet<string> bundleIds,
            List<ContentDiagnostic> diagnostics)
        {
            if (variants == null) return;
            for (int i = 0; i < variants.Length; i++)
            {
                var variant = variants[i];
                string itemPath = path + "[" + i + "]";
                if (variant == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Visual variant entries cannot be null.");
                    continue;
                }
                ValidateNamespacedId(variant.id, itemPath + ".id", diagnostics);
                if (!String.IsNullOrWhiteSpace(variant.visualAssetName))
                {
                    ValidateAssetName(variant.visualAssetName, itemPath + ".visualAssetName", diagnostics);
                }
                if (!String.IsNullOrWhiteSpace(variant.visualRootPath))
                {
                    ValidateVisualRootPath(variant.visualRootPath, itemPath + ".visualRootPath", diagnostics);
                }
                if (variant.scale < 0 || variant.scale > 10)
                {
                    Add(diagnostics, "unit-visual-variant.scale-invalid", itemPath + ".scale",
                        "Visual variant scale must be between 0 and 10.");
                }
            }
        }

        private static void ValidateUnitSpawn(
            ContentUnitSpawnDefinition spawn,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (spawn == null) return;
            if (spawn.spawnRate < 0)
            {
                Add(diagnostics, "unit-spawn.rate-invalid", path + ".spawnRate",
                    "Spawn rate must be non-negative.");
            }
            if (spawn.maxSpawnCount < 0)
            {
                Add(diagnostics, "unit-spawn.max-count-invalid", path + ".maxSpawnCount",
                    "Max spawn count must be non-negative.");
            }
            if (spawn.spawnCooldown < 0)
            {
                Add(diagnostics, "unit-spawn.cooldown-invalid", path + ".spawnCooldown",
                    "Spawn cooldown must be non-negative.");
            }
        }

        private static void ValidateUnitStatusEffects(
            ContentUnitStatusEffectDefinition[] effects,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                string itemPath = path + "[" + i + "]";
                if (effect == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Status effect entries cannot be null.");
                    continue;
                }
                ValidateNamespacedId(effect.id, itemPath + ".id", diagnostics);
                if (String.IsNullOrWhiteSpace(effect.effectType))
                {
                    Add(diagnostics, "unit-status-effect.type-required", itemPath + ".effectType",
                        "Status effect type must be non-empty.");
                }
                if (effect.duration < 0)
                {
                    Add(diagnostics, "unit-status-effect.duration-invalid", itemPath + ".duration",
                        "Status effect duration must be non-negative.");
                }
            }
        }

        private static void ValidateUnitProductionQueue(
            ContentUnitProductionQueueDefinition queue,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (queue == null) return;
            if (queue.buildTime < 0)
            {
                Add(diagnostics, "unit-production-queue.build-time-invalid", path + ".buildTime",
                    "Build time must be non-negative.");
            }
            if (queue.resourceCost < 0)
            {
                Add(diagnostics, "unit-production-queue.resource-cost-invalid", path + ".resourceCost",
                    "Resource cost must be non-negative.");
            }
            if (queue.queueSize < 0)
            {
                Add(diagnostics, "unit-production-queue.queue-size-invalid", path + ".queueSize",
                    "Queue size must be non-negative.");
            }
            if (queue.productionPriority < 0)
            {
                Add(diagnostics, "unit-production-queue.priority-invalid", path + ".productionPriority",
                    "Production priority must be non-negative.");
            }
        }

        private static void ValidateUnitResourceCosts(
            ContentUnitResourceCostDefinition[] costs,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (costs == null) return;
            for (int i = 0; i < costs.Length; i++)
            {
                var cost = costs[i];
                string itemPath = path + "[" + i + "]";
                if (cost == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Resource cost entries cannot be null.");
                    continue;
                }
                if (String.IsNullOrWhiteSpace(cost.resourceType))
                {
                    Add(diagnostics, "unit-resource-cost.type-required", itemPath + ".resourceType",
                        "Resource type must be non-empty.");
                }
                if (cost.amount < 0)
                {
                    Add(diagnostics, "unit-resource-cost.amount-invalid", itemPath + ".amount",
                        "Resource amount must be non-negative.");
                }
            }
        }

        private static void ValidateUnitMovement(
            ContentUnitMovementDefinition movement,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (movement == null) return;
            if (movement.moveSpeed < 0)
            {
                Add(diagnostics, "unit-movement.speed-invalid", path + ".moveSpeed",
                    "Move speed must be non-negative.");
            }
            if (movement.turnRate < 0)
            {
                Add(diagnostics, "unit-movement.turn-rate-invalid", path + ".turnRate",
                    "Turn rate must be non-negative.");
            }
            if (movement.acceleration < 0)
            {
                Add(diagnostics, "unit-movement.acceleration-invalid", path + ".acceleration",
                    "Acceleration must be non-negative.");
            }
        }

        private static void ValidateUnitDetection(
            ContentUnitDetectionDefinition detection,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (detection == null) return;
            if (detection.detectionRange < 0)
            {
                Add(diagnostics, "unit-detection.range-invalid", path + ".detectionRange",
                    "Detection range must be non-negative.");
            }
            if (!String.IsNullOrWhiteSpace(detection.sensorType) &&
                detection.sensorType != "Visual" && detection.sensorType != "Radar" &&
                detection.sensorType != "Sonar" && detection.sensorType != "Thermal")
            {
                Add(diagnostics, "unit-detection.sensor-type-invalid", path + ".sensorType",
                    "Sensor type must be Visual, Radar, Sonar, or Thermal.");
            }
        }

        private static void ValidateUnitWeapons(
            ContentUnitWeaponDefinition[] weapons,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (weapons == null) return;
            for (int i = 0; i < weapons.Length; i++)
            {
                var weapon = weapons[i];
                string itemPath = path + "[" + i + "]";
                if (weapon == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Weapon entries cannot be null.");
                    continue;
                }
                if (String.IsNullOrWhiteSpace(weapon.weaponType))
                {
                    Add(diagnostics, "unit-weapon.type-required", itemPath + ".weaponType",
                        "Weapon type must be non-empty.");
                }
                if (weapon.damage < 0)
                {
                    Add(diagnostics, "unit-weapon.damage-invalid", itemPath + ".damage",
                        "Weapon damage must be non-negative.");
                }
                if (weapon.range < 0)
                {
                    Add(diagnostics, "unit-weapon.range-invalid", itemPath + ".range",
                        "Weapon range must be non-negative.");
                }
                if (weapon.fireRate < 0)
                {
                    Add(diagnostics, "unit-weapon.fire-rate-invalid", itemPath + ".fireRate",
                        "Weapon fire rate must be non-negative.");
                }
            }
        }

        private static void ValidateUnitArmor(
            ContentUnitArmorDefinition armor,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (armor == null) return;
            if (!String.IsNullOrWhiteSpace(armor.armorType) &&
                armor.armorType != "Light" && armor.armorType != "Medium" &&
                armor.armorType != "Heavy" && armor.armorType != "Energy")
            {
                Add(diagnostics, "unit-armor.type-invalid", path + ".armorType",
                    "Armor type must be Light, Medium, Heavy, or Energy.");
            }
            if (armor.armorValue < 0)
            {
                Add(diagnostics, "unit-armor.value-invalid", path + ".armorValue",
                    "Armor value must be non-negative.");
            }
            if (armor.shieldValue < 0)
            {
                Add(diagnostics, "unit-armor.shield-invalid", path + ".shieldValue",
                    "Shield value must be non-negative.");
            }
            if (armor.damageReduction < 0 || armor.damageReduction > 1)
            {
                Add(diagnostics, "unit-armor.reduction-invalid", path + ".damageReduction",
                    "Damage reduction must be between 0 and 1.");
            }
        }

        private static void ValidateUnitHealing(
            ContentUnitHealingDefinition healing,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (healing == null) return;
            if (healing.healAmount < 0)
            {
                Add(diagnostics, "unit-healing.amount-invalid", path + ".healAmount",
                    "Heal amount must be non-negative.");
            }
            if (healing.healRange < 0)
            {
                Add(diagnostics, "unit-healing.range-invalid", path + ".healRange",
                    "Heal range must be non-negative.");
            }
            if (healing.healRate < 0)
            {
                Add(diagnostics, "unit-healing.rate-invalid", path + ".healRate",
                    "Heal rate must be non-negative.");
            }
            if (healing.repairAmount < 0)
            {
                Add(diagnostics, "unit-healing.repair-invalid", path + ".repairAmount",
                    "Repair amount must be non-negative.");
            }
        }

        private static void ValidateUnitStealth(
            ContentUnitStealthDefinition stealth,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (stealth == null) return;
            if (stealth.stealthLevel < 0 || stealth.stealthLevel > 10)
            {
                Add(diagnostics, "unit-stealth.level-invalid", path + ".stealthLevel",
                    "Stealth level must be between 0 and 10.");
            }
            if (stealth.cloakDuration < 0)
            {
                Add(diagnostics, "unit-stealth.duration-invalid", path + ".cloakDuration",
                    "Cloak duration must be non-negative.");
            }
            if (stealth.detectionReduction < 0 || stealth.detectionReduction > 1)
            {
                Add(diagnostics, "unit-stealth.reduction-invalid", path + ".detectionReduction",
                    "Detection reduction must be between 0 and 1.");
            }
        }

        private static void ValidateUnitLocalizations(
            ContentUnitDefinition unit,
            string path,
            bool hasNativeTemplate,
            List<ContentDiagnostic> diagnostics)
        {
            ValidateLocalizations(unit.localizations, path, hasNativeTemplate,
                "unit", "Unit", diagnostics);
        }

        private static void ValidateRelic(
            ContentRelicDefinition relic,
            string path,
            HashSet<string> allIds,
            HashSet<string> bundleIds,
            HashSet<string> unitIds,
            IContentRegistrySnapshot registrySnapshot,
            List<ContentDiagnostic> diagnostics)
        {
            if (relic == null)
            {
                Add(diagnostics, "entry.null", path, "Relic entries cannot be null.");
                return;
            }

            RegisterId(relic.id, path + ".id", "relic", allIds, registrySnapshot, diagnostics);
            ValidateBundleReference(relic.assetBundleId, path + ".assetBundleId", bundleIds, diagnostics);
            ValidateAssetName(relic.imageAssetName, path + ".imageAssetName", diagnostics);

            if (String.IsNullOrWhiteSpace(relic.rarity) || !SupportedRelicRarities.Contains(relic.rarity))
            {
                Add(diagnostics, "relic.rarity-invalid", path + ".rarity",
                    "Relic rarity must be one of the game's supported rarity IDs.");
            }

            if (String.IsNullOrWhiteSpace(relic.tech) || !SupportedRelicTechs.Contains(relic.tech))
            {
                Add(diagnostics, "relic.tech-invalid", path + ".tech",
                    "Relic tech must be one of the game's supported tech IDs.");
            }

            if (String.IsNullOrWhiteSpace(relic.offeredSystemTags) ||
                !SupportedRelicSystemTags.Contains(relic.offeredSystemTags))
            {
                Add(diagnostics, "relic.offered-system-tags-invalid", path + ".offeredSystemTags",
                    "Relic offeredSystemTags must be one supported system tag.");
            }

            if (String.IsNullOrWhiteSpace(relic.neededSystemTags) ||
                !SupportedRelicSystemTags.Contains(relic.neededSystemTags))
            {
                Add(diagnostics, "relic.needed-system-tags-invalid", path + ".neededSystemTags",
                    "Relic neededSystemTags must be one supported system tag.");
            }

            if (relic.neededExperienceLevel < 0 || relic.neededExperienceLevel > MaxNeededExperienceLevel)
            {
                Add(diagnostics, "relic.experience-level-invalid", path + ".neededExperienceLevel",
                    "Relic experience level must be between 0 and " + MaxNeededExperienceLevel + ".");
            }

            ValidateRelicLocalizations(relic.localizations, path + ".localizations", diagnostics);

            ContentCardChangeDefinition[] changes = RequireArray(relic.cardChanges,
                path + ".cardChanges", diagnostics);
            if (changes.Length == 0)
            {
                Add(diagnostics, "relic.card-change-required", path + ".cardChanges",
                    "A relic must declare at least one native card change.");
            }

            if (changes.Length > 16)
            {
                Add(diagnostics, "relic.card-change-limit", path + ".cardChanges",
                    "A relic can declare at most 16 card changes.");
            }

            for (int i = 0; i < changes.Length; i++)
            {
                ContentCardChangeDefinition change = changes[i];
                string changePath = path + ".cardChanges[" + i + "]";
                if (change == null)
                {
                    Add(diagnostics, "entry.null", changePath, "Card change entries cannot be null.");
                    continue;
                }

                if (!String.Equals(change.valueToChange, "Health", StringComparison.Ordinal))
                {
                    Add(diagnostics, "relic.change-value-unsupported", changePath + ".valueToChange",
                        "Data-only relics currently support the Health changeable value (mapped to native MaxHealth).");
                }

                if (!String.Equals(change.operation, "Add", StringComparison.Ordinal) &&
                    !String.Equals(change.operation, "Multiply", StringComparison.Ordinal))
                {
                    Add(diagnostics, "relic.change-operation-invalid", changePath + ".operation",
                        "Card change operation must be Add or Multiply.");
                }

                if (Single.IsNaN(change.value) || Single.IsInfinity(change.value) ||
                    change.value <= 0f || change.value > 10000f)
                {
                    Add(diagnostics, "relic.change-value-invalid", changePath + ".value",
                        "Card change value must be greater than 0 and at most 10000.");
                }

                if (change.multiplyValueWithCurrentHeatLevel)
                {
                    Add(diagnostics, "relic.heat-scaling-unsupported",
                        changePath + ".multiplyValueWithCurrentHeatLevel",
                        "Pack-authored relic changes must keep Heat scaling disabled until the per-level balance budget is validated.");
                }

                if (!String.Equals(change.side, "False", StringComparison.Ordinal) &&
                    !String.Equals(change.side, "True", StringComparison.Ordinal) &&
                    !String.Equals(change.side, "Irrelevant", StringComparison.Ordinal))
                {
                    Add(diagnostics, "relic.change-side-invalid", changePath + ".side",
                        "Card change side must match the game's False, True, or Irrelevant value.");
                }

                if (change.multiplyValueWithMetaProgressionMultiplier != null &&
                    String.IsNullOrWhiteSpace(change.multiplyValueWithMetaProgressionMultiplier))
                {
                    Add(diagnostics, "relic.meta-progression-invalid",
                        changePath + ".multiplyValueWithMetaProgressionMultiplier",
                        "A meta progression multiplier key must be non-empty when provided.");
                }

                string[] targetIds = RequireArray(change.onlyForTheseEntityIds,
                    changePath + ".onlyForTheseEntityIds", diagnostics);
                if (targetIds.Length == 0)
                {
                    Add(diagnostics, "relic.change-target-required", changePath + ".onlyForTheseEntityIds",
                        "Every card change must target at least one namespaced unit ID.");
                }

                if (targetIds.Length > 128)
                {
                    Add(diagnostics, "relic.change-target-limit", changePath + ".onlyForTheseEntityIds",
                        "A card change can target at most 128 unit IDs.");
                }

                ValidateReferences(targetIds, changePath + ".onlyForTheseEntityIds",
                    unitIds, registrySnapshot, true, diagnostics);
            }
        }

        private static void ValidateRelicLocalizations(
            ContentRelicLocalizationDefinition[] localizations,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (localizations == null || localizations.Length == 0)
            {
                Add(diagnostics, "relic.localization-required", path,
                    "A relic must provide at least its en-US name and description.");
                return;
            }

            if (localizations.Length > MaxLocalizationsPerDefinition)
            {
                Add(diagnostics, "relic.localization-limit", path,
                    "A relic can declare at most " + MaxLocalizationsPerDefinition + " localizations.");
            }

            var languages = new HashSet<string>(StringComparer.Ordinal);
            bool hasEnglish = false;
            for (int i = 0; i < localizations.Length; i++)
            {
                ContentRelicLocalizationDefinition localization = localizations[i];
                string itemPath = path + "[" + i + "]";
                if (localization == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Relic localization entries cannot be null.");
                    continue;
                }

                if (String.IsNullOrWhiteSpace(localization.language) ||
                    !SupportedUnitLocalizationLanguages.Contains(localization.language))
                {
                    Add(diagnostics, "relic.localization-language-unsupported", itemPath + ".language",
                        "Relic localization language must match one of the game's supported language IDs exactly.");
                }
                else if (!languages.Add(localization.language))
                {
                    Add(diagnostics, "relic.localization-language-duplicate", itemPath + ".language",
                        "A relic can declare at most one localization for each language.");
                }

                if (String.IsNullOrWhiteSpace(localization.name))
                {
                    Add(diagnostics, "relic.localization-name-required", itemPath + ".name",
                        "Every relic localization must include a non-empty display name.");
                }
                else if (String.Equals(localization.language, "en-US", StringComparison.Ordinal))
                {
                    hasEnglish = true;
                }

                if (String.IsNullOrWhiteSpace(localization.description))
                {
                    Add(diagnostics, "relic.localization-description-required", itemPath + ".description",
                        "Every relic localization must include a non-empty description.");
                }
            }

            if (!hasEnglish)
            {
                Add(diagnostics, "relic.localization-english-required", path,
                    "Relic localizations must include an en-US name and description for fallback.");
            }
        }

        private static void ValidateLocalizations(
            ContentUnitLocalizationDefinition[] localizations,
            string path,
            bool hasNativeTemplate,
            string codePrefix,
            string ownerLabel,
            List<ContentDiagnostic> diagnostics)
        {
            if (localizations == null || localizations.Length == 0)
            {
                if (!hasNativeTemplate)
                {
                    Add(diagnostics, codePrefix + ".localization-required", path,
                        "A " + ownerLabel.ToLowerInvariant() + " must declare an en-US display name.");
                }

                return;
            }

            if (localizations.Length > MaxLocalizationsPerDefinition)
            {
                Add(diagnostics, codePrefix + ".localization-limit", path,
                    "A " + ownerLabel.ToLowerInvariant() + " can declare at most " + MaxLocalizationsPerDefinition + " localizations.");
            }

            var languages = new HashSet<string>(StringComparer.Ordinal);
            bool hasEnglishName = false;
            for (int i = 0; i < localizations.Length; i++)
            {
                ContentUnitLocalizationDefinition localization = localizations[i];
                string itemPath = path + "[" + i + "]";
                if (localization == null)
                {
                    Add(diagnostics, "entry.null", itemPath,
                        ownerLabel + " localization entries cannot be null.");
                    continue;
                }

                if (String.IsNullOrWhiteSpace(localization.language) ||
                    !SupportedUnitLocalizationLanguages.Contains(localization.language))
                {
                    Add(diagnostics, codePrefix + ".localization-language-unsupported", itemPath + ".language",
                        ownerLabel + " localization language must match one of the game's supported language IDs exactly.");
                }
                else if (!languages.Add(localization.language))
                {
                    Add(diagnostics, codePrefix + ".localization-language-duplicate", itemPath + ".language",
                        "A " + ownerLabel.ToLowerInvariant() + " can declare at most one localization for each language.");
                }

                if (String.IsNullOrWhiteSpace(localization.name))
                {
                    Add(diagnostics, codePrefix + ".localization-name-required", itemPath + ".name",
                        "Every " + ownerLabel.ToLowerInvariant() + " localization must include a non-empty display name.");
                }
                else if (String.Equals(localization.language, "en-US", StringComparison.Ordinal))
                {
                    hasEnglishName = true;
                }

                if (localization.description != null && String.IsNullOrWhiteSpace(localization.description))
                {
                    Add(diagnostics, codePrefix + ".localization-description-invalid", itemPath + ".description",
                        "Optional localization descriptions must be non-empty when provided.");
                }

                if (localization.skillDescription != null && String.IsNullOrWhiteSpace(localization.skillDescription))
                {
                    Add(diagnostics, codePrefix + ".localization-skill-description-invalid",
                        itemPath + ".skillDescription",
                        "Optional skill descriptions must be non-empty when provided.");
                }
            }

            if (!hasEnglishName)
            {
                Add(diagnostics, codePrefix + ".localization-english-name-required", path,
                    ownerLabel + " localizations must include a non-empty en-US display name for fallback.");
            }
        }

        private static bool TryValidateBalanceOverrideValue(string name, string value)
        {
            if (String.IsNullOrWhiteSpace(name) || String.IsNullOrEmpty(value) ||
                !String.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                value[0] == '+' || value[0] == '-')
            {
                return false;
            }

            switch (name)
            {
                case "coinsAmount":
                case "cost":
                case "maxCapacity":
                case "combatValue":
                case "maxHealth":
                case "maxShield":
                case "maxMana":
                case "maxRank":
                case "sightRadius":
                case "skillRange":
                case "firePointCount":
                case "gridSize":
                    int integerValue;
                    return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out integerValue) && integerValue >= 0;
                case "neededExperienceLevel":
                    int experienceLevel;
                    return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out experienceLevel) && experienceLevel >= 0 &&
                        experienceLevel <= MaxNeededExperienceLevel;
                case "productionDuration":
                case "cardModelScalingFactor":
                case "maxArmor":
                case "armorProtection":
                case "moveSpeed":
                case "damage1":
                case "damage2":
                case "weaponRange":
                case "attackCooldown":
                case "attack2Cooldown":
                case "effectRadius1":
                case "effectRadius2":
                case "healAmount1":
                case "healAmount2":
                case "gainCreditsAmount":
                case "skillManaCost":
                case "duration1":
                case "duration2":
                    float floatValue;
                    return Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out floatValue) && !Single.IsNaN(floatValue) && !Single.IsInfinity(floatValue) &&
                        floatValue >= 0f;
                case "rarity":
                    return String.Equals(value, "Common", StringComparison.Ordinal) ||
                           String.Equals(value, "Rare", StringComparison.Ordinal) ||
                           String.Equals(value, "UltraRare", StringComparison.Ordinal);
                case "attackType":
                    return String.Equals(value, "None", StringComparison.Ordinal) ||
                           String.Equals(value, "Single", StringComparison.Ordinal) ||
                           String.Equals(value, "AoE", StringComparison.Ordinal) ||
                           String.Equals(value, "Multi", StringComparison.Ordinal) ||
                           String.Equals(value, "MultiAoE", StringComparison.Ordinal) ||
                           String.Equals(value, "Beam", StringComparison.Ordinal) ||
                           String.Equals(value, "Artillery", StringComparison.Ordinal) ||
                           String.Equals(value, "MultiArtillery", StringComparison.Ordinal);
                case "skillType":
                    return String.Equals(value, "None", StringComparison.Ordinal) ||
                           String.Equals(value, "Toggle", StringComparison.Ordinal) ||
                           String.Equals(value, "Location", StringComparison.Ordinal) ||
                           String.Equals(value, "LocationAoE", StringComparison.Ordinal) ||
                           String.Equals(value, "LocationMultiTargets", StringComparison.Ordinal) ||
                           String.Equals(value, "Self", StringComparison.Ordinal) ||
                           String.Equals(value, "SelfAoE", StringComparison.Ordinal) ||
                           String.Equals(value, "Target", StringComparison.Ordinal) ||
                           String.Equals(value, "TargetMultiTarget", StringComparison.Ordinal) ||
                           String.Equals(value, "TargetAoE", StringComparison.Ordinal) ||
                           String.Equals(value, "SelfMultiTarget", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private static void ValidateStageParameterOverrides(
            ContentStageParameterOverride[] overrides,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (overrides == null)
            {
                return;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < overrides.Length; i++)
            {
                ContentStageParameterOverride stageOverride = overrides[i];
                string itemPath = path + "[" + i + "]";
                if (stageOverride == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Stage parameter overrides cannot be null.");
                    continue;
                }

                if (String.IsNullOrWhiteSpace(stageOverride.name) || !names.Add(stageOverride.name))
                {
                    Add(diagnostics, "stage.parameter-override-name-invalid", itemPath + ".name",
                        "Stage parameter override names must be non-empty and unique within the stage.");
                }

                if (!TryValidateStageParameterOverrideValue(stageOverride.name, stageOverride.value))
                {
                    Add(diagnostics, "stage.parameter-override-invalid", itemPath,
                        "The override must target an allowlisted finite, non-negative stage parameter and use invariant-culture text.");
                }
            }
        }

        private static bool TryValidateStageParameterOverrideValue(string name, string value)
        {
            if (String.IsNullOrWhiteSpace(name) || String.IsNullOrEmpty(value) ||
                !String.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                value[0] == '+' || value[0] == '-')
            {
                return false;
            }

            switch (name)
            {
                case "fillFirstLevelWithCardRewards":
                case "putDefaultCardRewardInCenter":
                case "fillLastLevelWithShops":
                    return String.Equals(value, "true", StringComparison.Ordinal) ||
                           String.Equals(value, "false", StringComparison.Ordinal);
                case "levelCount":
                case "maxWidth":
                case "coinReward":
                case "startCrystalReward":
                case "researchPointsReward":
                    int integerValue;
                    if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integerValue))
                    {
                        return false;
                    }

                    if (name == "levelCount") return integerValue >= 1 && integerValue <= 32;
                    if (name == "maxWidth") return integerValue >= 1 && integerValue <= 64;
                    return integerValue >= 0 && integerValue <= 1000000;
                case "twoWayBranchingProbability":
                case "shopWeight":
                case "restSiteWeight":
                case "cardRewardWeight":
                case "upgradeRewardWeight":
                case "relicRewardWeight":
                case "researchRewardWeight":
                case "coinRewardWeight":
                case "startCrystalRewardWeight":
                case "researchPointsRewardWeight":
                case "dropRewardWeight":
                    float floatValue;
                    if (!Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue) ||
                        Single.IsNaN(floatValue) || Single.IsInfinity(floatValue) || floatValue < 0f)
                    {
                        return false;
                    }

                    if (name == "twoWayBranchingProbability") return floatValue <= 1f;
                    return floatValue <= 1000f;
                default:
                    return false;
            }
        }

        private static bool IsAiResourceName(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || !value.StartsWith("RCL_", StringComparison.Ordinal) ||
                value.Length <= 4 || value.Length > 96)
            {
                return false;
            }

            for (int i = 4; i < value.Length; i++)
            {
                char character = value[i];
                bool valid = character >= 'A' && character <= 'Z' ||
                    character >= 'a' && character <= 'z' ||
                    character >= '0' && character <= '9' || character == '_';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidateReferences(
            string[] references,
            string path,
            HashSet<string> localIds,
            IContentRegistrySnapshot registrySnapshot,
            bool units,
            List<ContentDiagnostic> diagnostics)
        {
            var seen = new HashSet<string>(IdComparer);
            for (int i = 0; i < references.Length; i++)
            {
                string id = references[i];
                string itemPath = path + "[" + i + "]";
                ValidateNamespacedId(id, itemPath, diagnostics);
                if (!IsNamespacedId(id))
                {
                    continue;
                }

                if (!seen.Add(id))
                {
                    Add(diagnostics, "reference.duplicate", itemPath, "The same reference appears more than once in this list.");
                }

                if (localIds.Contains(id))
                {
                    continue;
                }

                bool exists = false;
                if (registrySnapshot != null)
                {
                    try
                    {
                        exists = units ? registrySnapshot.ContainsUnit(id) : registrySnapshot.ContainsAiProfile(id);
                    }
                    catch (Exception exception)
                    {
                        Add(diagnostics, "registry.snapshot-failed", itemPath, "Could not resolve the reference in the registry snapshot: " + exception.Message);
                    }
                }

                if (!exists)
                {
                    Add(diagnostics, "reference.unresolved", itemPath, "The reference does not resolve to a matching definition in this package or registry snapshot.");
                }
            }
        }

        private static void ValidateAiModule(ContentAiModuleDefinition module, string path, IAiModuleValidator validator, List<ContentDiagnostic> diagnostics)
        {
            if (module == null)
            {
                Add(diagnostics, "entry.null", path, "AI module entries cannot be null.");
                return;
            }

            ValidateNamespacedId(module.moduleId, path + ".moduleId", diagnostics);
            var parameters = RequireArray(module.parameters, path + ".parameters", diagnostics);
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                string parameterPath = path + ".parameters[" + i + "]";
                if (parameter == null)
                {
                    Add(diagnostics, "entry.null", parameterPath, "Module parameter entries cannot be null.");
                    continue;
                }

                if (!IsParameterName(parameter.name))
                {
                    Add(diagnostics, "module.parameter-name-invalid", parameterPath + ".name", "Parameter names must start with an ASCII letter and contain only letters, digits, dots, underscores, or hyphens.");
                }
                else if (!names.Add(parameter.name))
                {
                    Add(diagnostics, "module.parameter-duplicate", parameterPath + ".name", "Parameter names must be unique within a module.");
                }

                if (parameter.value == null)
                {
                    Add(diagnostics, "module.parameter-value-required", parameterPath + ".value", "Parameter values must be strings; use an empty string when the module permits it.");
                }
            }

            if (validator == null)
            {
                Add(diagnostics, "module.validator-required", path + ".moduleId", "An AI module validator is required to verify module IDs and parameters.");
                return;
            }

            try
            {
                string message;
                if (!validator.TryValidate(module.moduleId, parameters, out message))
                {
                    Add(diagnostics, "module.invalid", path, string.IsNullOrWhiteSpace(message) ? "The module ID or its parameters were rejected." : message);
                }
            }
            catch (Exception exception)
            {
                Add(diagnostics, "module.validator-failed", path, "AI module validation failed: " + exception.Message);
            }
        }

        private static bool IsParameterName(string value)
        {
            if (string.IsNullOrEmpty(value) || !IsAsciiLetter(value[0]))
            {
                return false;
            }

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!IsAsciiLetter(c) && !IsAsciiDigit(c) && c != '.' && c != '_' && c != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAsciiLetter(char value)
        {
            return (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z');
        }

        private static string GetPackageRoot(string packageRoot, List<ContentDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(packageRoot))
            {
                Add(diagnostics, "package-root.required", "packageRoot", "A package directory is required.");
                return null;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(packageRoot);
            }
            catch (Exception exception)
            {
                Add(diagnostics, "package-root.invalid", "packageRoot", "The package directory path is invalid: " + exception.Message);
                return null;
            }

            if (!Directory.Exists(fullPath))
            {
                Add(diagnostics, "package-root.missing", "packageRoot", "The package directory does not exist.");
                return null;
            }

            return fullPath;
        }

        private static bool TryResolvePackageFile(string packageRoot, string relativePath, string diagnosticPath, List<ContentDiagnostic> diagnostics, out string resolvedPath)
        {
            resolvedPath = null;
            string pathError;
            if (!IsSafeRelativePath(relativePath, out pathError))
            {
                Add(diagnostics, "path.invalid", diagnosticPath, pathError);
                return false;
            }

            if (packageRoot == null)
            {
                return false;
            }

            try
            {
                string platformRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(packageRoot, platformRelativePath));
                string rootPrefix = EnsureTrailingSeparator(packageRoot);
                if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    Add(diagnostics, "path.outside-package", diagnosticPath, "The resolved path must remain inside the package directory.");
                    return false;
                }

                resolvedPath = candidate;
                return true;
            }
            catch (Exception exception)
            {
                Add(diagnostics, "path.invalid", diagnosticPath, "Could not resolve the package-relative path: " + exception.Message);
                return false;
            }
        }

        private static bool IsSafeRelativePath(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Asset bundle paths must be non-empty relative paths.";
                return false;
            }

            if (path.IndexOf('\\') >= 0 || path[0] == '/' || Path.IsPathRooted(path))
            {
                error = "Use a relative path with forward slashes; rooted paths and backslashes are not allowed.";
                return false;
            }

            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    error = "Paths cannot contain empty, current-directory, or parent-directory segments.";
                    return false;
                }

                if (segment[segment.Length - 1] == '.' || segment[segment.Length - 1] == ' ')
                {
                    error = "Path segments cannot end in a dot or space.";
                    return false;
                }

                for (int characterIndex = 0; characterIndex < segment.Length; characterIndex++)
                {
                    char character = segment[characterIndex];
                    if (char.IsControl(character) || character == ':' || character == '<' || character == '>' || character == '"' || character == '|' || character == '?' || character == '*')
                    {
                        error = "Path segments contain a character that is unsafe or invalid on Windows.";
                        return false;
                    }
                }

                if (IsReservedWindowsDeviceName(segment))
                {
                    error = "Windows device names cannot be used as path segments.";
                    return false;
                }
            }

            return true;
        }

        private static void ValidateBundleOutputPath(string path, string diagnosticPath, List<ContentDiagnostic> diagnostics)
        {
            string fileName;
            if (!TryGetBundleFileName(path, out fileName))
            {
                Add(diagnostics, "bundle.path-format-invalid", diagnosticPath,
                    "Bundle files must use the exact form 'bundles/<lowercase-name>.bundle' with no extra directories or backslashes.");
            }
        }

        internal static bool TryGetBundleFileName(string path, out string fileName)
        {
            fileName = null;
            const string bundleDirectory = "bundles/";
            const string extension = ".bundle";
            if (string.IsNullOrEmpty(path) || path.IndexOf('\\') >= 0 ||
                !path.StartsWith(bundleDirectory, StringComparison.Ordinal))
            {
                return false;
            }

            string candidate = path.Substring(bundleDirectory.Length);
            if (candidate.IndexOf('/') >= 0 || candidate.Length <= extension.Length ||
                !candidate.EndsWith(extension, StringComparison.Ordinal))
            {
                return false;
            }

            if (!IsAsciiLowerLetter(candidate[0]) && !IsAsciiDigit(candidate[0]))
            {
                return false;
            }

            int stemLength = candidate.Length - extension.Length;
            for (int i = 0; i < stemLength; i++)
            {
                char value = candidate[i];
                if (!IsAsciiLowerLetter(value) && !IsAsciiDigit(value) && value != '.' && value != '_' && value != '-')
                {
                    return false;
                }
            }

            if (IsReservedWindowsDeviceName(candidate))
            {
                return false;
            }

            fileName = candidate;
            return true;
        }

        private static bool IsReservedWindowsDeviceName(string segment)
        {
            int dotIndex = segment.IndexOf('.');
            string stem = (dotIndex >= 0 ? segment.Substring(0, dotIndex) : segment).ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL")
            {
                return true;
            }

            if (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '1' && stem[3] <= '9')
            {
                return true;
            }

            return false;
        }

        private static bool ContainsReparsePoint(string packageRoot, string fullPath)
        {
            try
            {
                FileAttributes rootAttributes = File.GetAttributes(packageRoot);
                if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                string prefix = EnsureTrailingSeparator(packageRoot);
                if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string remaining = fullPath.Substring(prefix.Length);
                string[] segments = remaining.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                string current = packageRoot;
                for (int i = 0; i < segments.Length; i++)
                {
                    current = Path.Combine(current, segments[i]);
                    if (!File.Exists(current) && !Directory.Exists(current))
                    {
                        return false;
                    }

                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                // A missing/unreadable target is diagnosed by the file-presence
                // check; treat uncertainty here as unsafe and fail closed.
                return true;
            }
        }

        private static void ScanForDllsAndReparsePoints(string packageRoot, List<ContentDiagnostic> diagnostics)
        {
            try
            {
                if ((File.GetAttributes(packageRoot) & FileAttributes.ReparsePoint) != 0)
                {
                    Add(diagnostics, "path.reparse-root", "packageRoot", "The package directory cannot itself be a symbolic link, junction, or other reparse point.");
                    return;
                }

                var directories = new Stack<string>();
                directories.Push(packageRoot);
                while (directories.Count > 0)
                {
                    string directory = directories.Pop();
                    IEnumerable<string> entries;
                    try
                    {
                        entries = Directory.EnumerateFileSystemEntries(directory);
                    }
                    catch (Exception exception)
                    {
                        Add(diagnostics, "package.scan-failed", ToCanonicalRelativePath(packageRoot, directory), "Could not inspect package contents: " + exception.Message);
                        continue;
                    }

                    foreach (string entry in entries)
                    {
                        FileAttributes attributes;
                        try
                        {
                            attributes = File.GetAttributes(entry);
                        }
                        catch (Exception exception)
                        {
                            Add(diagnostics, "package.scan-failed", ToCanonicalRelativePath(packageRoot, entry), "Could not inspect package entry: " + exception.Message);
                            continue;
                        }

                        string relativePath = ToCanonicalRelativePath(packageRoot, entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            Add(diagnostics, "path.reparse-point", relativePath, "Package contents cannot contain symbolic links, junctions, or other reparse points.");
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            directories.Push(entry);
                        }
                        else if (string.Equals(Path.GetExtension(entry), ".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            Add(diagnostics, "package.dll-forbidden", relativePath, "Content packs cannot contain DLL files.");
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Add(diagnostics, "package.scan-failed", "packageRoot", "Could not inspect package contents: " + exception.Message);
            }
        }

        private static string ToCanonicalRelativePath(string packageRoot, string fullPath)
        {
            if (string.IsNullOrEmpty(packageRoot) || string.IsNullOrEmpty(fullPath))
            {
                return string.Empty;
            }

            string prefix = EnsureTrailingSeparator(packageRoot);
            if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            }

            return fullPath;
        }

        private static string EnsureTrailingSeparator(string path)
        {
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed + Path.DirectorySeparatorChar;
        }

        private static void Add(List<ContentDiagnostic> diagnostics, string code, string path, string message)
        {
            diagnostics.Add(new ContentDiagnostic(code, path, message));
        }
    }
}