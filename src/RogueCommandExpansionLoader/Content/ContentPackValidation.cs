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

    public enum DiagnosticSeverity
    {
        Error,
        Warning,
        Info
    }

    public sealed class ContentDiagnostic
    {
        public ContentDiagnostic(string code, string path, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error)
        {
            Code = code;
            Path = path;
            Message = message;
            Severity = severity;
        }

        public string Code { get; private set; }
        public string Path { get; private set; }
        public string Message { get; private set; }
        public DiagnosticSeverity Severity { get; private set; }

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
        public bool IsValid
        {
            get
            {
                foreach (ContentDiagnostic diagnostic in diagnostics)
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
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
        public const int MaxUnitVariantsPerUnit = 8;
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
        private static readonly HashSet<string> SupportedUnitRarities = new HashSet<string>(
            new[] { "Common", "Rare", "UltraRare" }, StringComparer.Ordinal);
        private static readonly HashSet<string> SupportedUnitTechs = new HashSet<string>(
            new[] { "None", "Colorless", "Mech", "Tank", "Ancient", "Specialist", "All" },
            StringComparer.Ordinal);
        private static readonly HashSet<string> SupportedAttackTypes = new HashSet<string>(
            new[] { "None", "Single", "AoE", "Multi", "MultiAoE", "Beam", "Artillery", "MultiArtillery" },
            StringComparer.Ordinal);
        private static readonly HashSet<string> SupportedSkillTypes = new HashSet<string>(
            new[] { "None", "Toggle", "Location", "LocationAoE", "LocationMultiTargets", "Self", "SelfAoE", "Target", "TargetMultiTarget", "TargetAoE", "SelfMultiTarget" },
            StringComparer.Ordinal);
        private static readonly HashSet<string> SupportedUnitRoles = new HashSet<string>(
            new[] {
                "None", "Spawn", "Harvester", "FrontalAttacker", "MassDestruction", "Fire", "Sniper", "Supporter",
                "Rock", "Robo", "Spawner", "PCXCard", "Building", "Factory", "Turret", "Refinery", "Unit",
                "Heal", "Specialist", "Skill", "Engineer", "Bomb", "Scout", "Chest", "Tank", "Vehicle",
                "Mech", "Tree", "Melee", "Research", "Crystal", "Builder", "Drop", "All"
            }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SupportedUnitSystemTags = new HashSet<string>(
            new[] {
                "Melee", "Shield", "Spawn", "Armor", "Mech", "Robo", "Heal", "Turret", "Fire",
                "Bounty", "Panic", "Slow", "Stun", "Teleport", "AoE", "ManaUse", "Rush", "Turbo", "Strength",
                "Robust", "SkillRangeUse", "Bomb", "Tank", "Vehicle", "EffectDuration", "SpawnDuration",
                "CriticalHits", "Stealth", "Zombo", "Sniper", "Regen", "EconomySupport"
            }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SupportedUnitStatusEffects = new HashSet<string>(
            new[] {
                "CombatBoost", "Defender", "SquadEffect", "Turbo", "SlowedDown", "Stun", "OnFire", "Spikes",
                "CriticallyHitting", "Battered", "Reaper", "Regen", "Rage", "Attacker", "Invincible", "Leaking",
                "Vampirism", "Robust", "Marked", "Rush", "Solo", "Stealth", "Taunt", "Armored", "AttackDeactivated",
                "Panic", "Overclocked"
            }, StringComparer.OrdinalIgnoreCase);
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
        private static readonly HashSet<string> SupportedRelicChangeableValues = new HashSet<string>(
            new[] {
                "Health", "Cost", "ProductionDuration", "MaxCapacity", "MaxHealth", "MaxShield", "MaxMana",
                "MaxRank", "MoveSpeed", "SightRadius", "Damage1", "Damage2", "WeaponRange", "Attack1Cooldown",
                "Attack2Cooldown", "EffectRadius1", "EffectRadius2", "SkillRange", "HealAmount1", "HealAmount2",
                "GainCreditsAmount", "SkillManaCost", "Duration1", "SpawnTtl", "ManaRechargePerSecond",
                "MaxArmor", "ArmorProtection"
            }, StringComparer.Ordinal);

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
                if (units[i] != null)
                {
                    RegisterId(units[i].id, "units[" + i + "].id", "unit", allIds, registrySnapshot, diagnostics);
                    if (IsNamespacedId(units[i].id))
                    {
                        unitIds.Add(units[i].id);
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

                if (!String.IsNullOrWhiteSpace(unit.unitType) && !SupportedUnitTypes.Contains(unit.unitType))
                {
                    Add(diagnostics, "unit.type-invalid", itemPath + ".unitType", "Unit type must be one of the game's supported unit types.");
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
                ValidateUnitBalanceOverrides(unit, itemPath + ".balanceOverrides", diagnostics);
                ValidateUnitVariants(unit.variants, itemPath + ".variants", diagnostics);
                ValidateUnitCustomBalance(unit.customBalance, itemPath + ".customBalance", diagnostics);
                ValidateAbilities(unit.abilities, itemPath + ".abilities", diagnostics);
                ValidateAiOverrides(unit.aiOverrides, itemPath + ".aiOverrides", diagnostics);
                ValidateActiveSkills(unit.activeSkills, itemPath + ".activeSkills", diagnostics);
                ValidateCombatBoosts(unit.combatBoosts, itemPath + ".combatBoosts", diagnostics);
                ValidateUnitUpgrades(unit.upgrades, itemPath + ".upgrades", unitIds, registrySnapshot, diagnostics);
                ValidateUnitExperience(unit.experience, itemPath + ".experience", diagnostics);
                ValidateUnitSynergies(unit.synergies, itemPath + ".synergies", unitIds, registrySnapshot, diagnostics);
                ValidateUnitVisualVariants(unit.visualVariants, itemPath + ".visualVariants", bundleIds, diagnostics);
                ValidateUnitSpawn(unit.spawn, itemPath + ".spawn", diagnostics);
                ValidateUnitStatusEffects(unit.statusEffects, itemPath + ".statusEffects", diagnostics);
                ValidateUnitProductionQueue(unit.productionQueue, itemPath + ".productionQueue", diagnostics);
                ValidateUnitProductionQueueFeasibility(unit.productionQueue, itemPath + ".productionQueue", diagnostics);
                ValidateUnitResourceCosts(unit.resourceCosts, itemPath + ".resourceCosts", diagnostics);
                ValidateUnitMovement(unit.movement, itemPath + ".movement", diagnostics);
                ValidateUnitDetection(unit.detection, itemPath + ".detection", diagnostics);
                ValidateUnitWeapons(unit.weapons, itemPath + ".weapons", diagnostics);
                ValidateUnitArmor(unit.armor, itemPath + ".armor", diagnostics);
                ValidateUnitHealing(unit.healing, itemPath + ".healing", diagnostics);
                ValidateUnitStealth(unit.stealth, itemPath + ".stealth", diagnostics);
                ValidateUnitCommand(unit.command, itemPath + ".command", diagnostics);
                ValidateUnitExperienceReward(unit.experienceReward, itemPath + ".experienceReward", diagnostics);
                ValidateUnitAura(unit.aura, itemPath + ".aura", diagnostics);
                ValidateUnitResourceGathering(unit.resourceGathering, itemPath + ".resourceGathering", diagnostics);
                ValidateUnitAiBehavior(unit.aiBehavior, itemPath + ".aiBehavior", diagnostics);
                ValidateUnitBalancingParameters(unit, itemPath, diagnostics);
                ValidateProductionBuilding(unit.productionBuilding, itemPath + ".productionBuilding",
                                    allIds, productionBuildingIds, bundleIds, registrySnapshot, diagnostics);
                ValidateProfileCardEligibility(unit, itemPath, diagnostics);
            }

            ValidateUnitUpgradeGraph(units, "units", diagnostics);
            ValidateUnitSynergyGraph(units, "units", diagnostics);

            ValidateUnitProductionBuildingIdsUnique(units, "units", diagnostics);

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

                ValidateAiProfileStageRangeContiguity(profile, itemPath, stages, diagnostics);

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

                ValidateStagePoolEntries(stage.blueprintPoolEntries,
                    itemPath + ".blueprintPoolEntries", unitIds, registrySnapshot, diagnostics);

                ValidateStageBlueprintPoolEntriesUnique(stage, itemPath, diagnostics);

                var stageAiProfiles = RequireArray(stage.aiProfileIds, itemPath + ".aiProfileIds", diagnostics);
                ValidateReferences(stageAiProfiles, itemPath + ".aiProfileIds", aiProfileIds, registrySnapshot, false, diagnostics);
            }

            return new ContentPackValidationResult(pack, diagnostics);
        }

        private static void ValidateStagePoolEntries(
            ContentStagePoolEntryDefinition[] entries,
            string path,
            HashSet<string> localUnitIds,
            IContentRegistrySnapshot registrySnapshot,
            List<ContentDiagnostic> diagnostics)
        {
            if (entries == null)
            {
                return;
            }

            if (entries.Length > 64)
            {
                Add(diagnostics, "stage.blueprint-pool-entries-limit", path,
                    "A stage can override at most 64 native blueprint pools.");
            }

            var seenPools = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                ContentStagePoolEntryDefinition entry = entries[i];
                string entryPath = path + "[" + i + "]";
                if (entry == null)
                {
                    Add(diagnostics, "entry.null", entryPath, "Blueprint pool entries cannot be null.");
                    continue;
                }

                if (String.IsNullOrWhiteSpace(entry.poolId) ||
                    !String.Equals(entry.poolId, entry.poolId.Trim(), StringComparison.Ordinal) ||
                    entry.poolId.Length > 128 || !seenPools.Add(entry.poolId))
                {
                    Add(diagnostics, "stage.blueprint-pool-id-invalid", entryPath + ".poolId",
                        "Pool IDs must be non-empty, trimmed, at most 128 characters, and unique per stage.");
                }

                string[] unitIds = RequireArray(entry.unitIds, entryPath + ".unitIds", diagnostics);
                if (unitIds.Length == 0 || unitIds.Length > 64)
                {
                    Add(diagnostics, "stage.blueprint-pool-units-limit", entryPath + ".unitIds",
                        "A blueprint pool override must reference between 1 and 64 product units.");
                }

                ValidateStagePoolUnitReferences(unitIds, entryPath + ".unitIds", localUnitIds,
                    registrySnapshot, diagnostics);
            }
        }

        private static void ValidateStagePoolUnitReferences(
            string[] ids,
            string path,
            HashSet<string> localUnitIds,
            IContentRegistrySnapshot registrySnapshot,
            List<ContentDiagnostic> diagnostics)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; ids != null && i < ids.Length; i++)
            {
                string id = ids[i];
                if (String.IsNullOrWhiteSpace(id) || !String.Equals(id, id.Trim(), StringComparison.Ordinal) ||
                    id.Length > 128 || !seen.Add(id))
                {
                    Add(diagnostics, "stage.blueprint-pool-unit-id-invalid", path + "[" + i + "]",
                        "Pool unit IDs must be non-empty, trimmed, at most 128 characters, and unique.");
                    continue;
                }

                if (localUnitIds.Contains(id))
                {
                    continue;
                }

                if (IsNamespacedId(id))
                {
                    Add(diagnostics, "reference.unit-unresolved", path + "[" + i + "]",
                        "Mod unit ID '" + id + "' must be declared by this pack.");
                    continue;
                }
                if (registrySnapshot != null && registrySnapshot.ContainsUnit(id))
                {
                    continue;
                }

                Add(diagnostics, "reference.unit-unresolved", path + "[" + i + "]",
                    "Unit ID '" + id + "' is neither declared by this pack nor registered by the game.");
            }
        }

        internal static void ValidateStageBlueprintPoolEntriesUnique(
            ContentStageDefinition stage,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (stage == null || stage.blueprintPoolEntries == null)
            {
                return;
            }

            for (int i = 0; i < stage.blueprintPoolEntries.Length; i++)
            {
                ContentStagePoolEntryDefinition entry = stage.blueprintPoolEntries[i];
                if (entry == null || entry.unitIds == null)
                {
                    continue;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < entry.unitIds.Length; j++)
                {
                    string unitId = entry.unitIds[j];
                    if (String.IsNullOrWhiteSpace(unitId))
                    {
                        continue;
                    }

                    if (!seen.Add(unitId))
                    {
                        Add(diagnostics, "stage.blueprint-pool-unit-id-duplicate",
                            path + "[" + i + "].unitIds[" + j + "]",
                            "A stage pool entry must not repeat the same unit ID.");
                    }
                }
            }
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
                path + ".balanceOverrides", "production-building", "production building", diagnostics, true);
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
            if (unit.neededExperienceLevel.HasValue)
            {
                hasProductLevel = true;
                productLevel = unit.neededExperienceLevel.Value.ToString(CultureInfo.InvariantCulture);
            }
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
            if (!String.IsNullOrEmpty(unit.rarity))
            {
                hasProductRarity = true;
                productRarity = unit.rarity;
            }
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

        internal static void ValidateUnitProductionBuildingIdsUnique(
            ContentUnitDefinition[] units,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (units == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < units.Length; i++)
            {
                ContentUnitDefinition unit = units[i];
                if (unit == null)
                {
                    continue;
                }

                string factoryId = unit.productionBuilding != null
                    ? unit.productionBuilding.id
                    : null;
                if (String.IsNullOrWhiteSpace(factoryId))
                {
                    continue;
                }

                if (!seen.Add(factoryId))
                {
                    Add(diagnostics, "unit.production-building.id-duplicate",
                        path + "[" + i + "].productionBuilding.id",
                        "Each unit's production building must use a distinct ID within the pack.");
                }
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
            List<ContentDiagnostic> diagnostics,
            bool isFactoryRow)
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

                if (!TryValidateBalanceOverrideValue(balanceOverride.name, balanceOverride.value, ownerLabel, isFactoryRow))
                {
                    Add(diagnostics, codePrefix + ".balance-override-invalid", itemPath,
                        "The override must target an allowlisted balance field and contain a valid invariant-culture value.");
                }
            }
        }

        private static void ValidateUnitBalanceOverrides(
            ContentUnitDefinition unit,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (unit == null || unit.balanceOverrides == null)
            {
                return;
            }

            var parameterMap = UnitBalanceOverrideMapper.CreateBalanceParameterMap();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < unit.balanceOverrides.Length; i++)
            {
                ContentBalanceOverride balanceOverride = unit.balanceOverrides[i];
                string itemPath = path + "[" + i + "]";
                if (balanceOverride == null)
                {
                    Add(diagnostics, "entry.null", itemPath,
                        "Balance override entries for a unit cannot be null.");
                    continue;
                }

                if (String.IsNullOrWhiteSpace(balanceOverride.name) ||
                    !names.Add(balanceOverride.name))
                {
                    Add(diagnostics, "unit.balance-override-name-invalid", itemPath + ".name",
                        "Balance override names must be non-empty and unique within the unit.");
                }

                if (unit.customBalance != null)
                {
                    foreach (var parameter in parameterMap)
                    {
                        if (String.Equals(parameter.Value.RowFieldName, balanceOverride.name, StringComparison.Ordinal) &&
                            unit.customBalance.ContainsKey(parameter.Key))
                        {
                            Add(diagnostics, "unit.balance-override-redundant-with-custom", itemPath,
                                "This balance override is redundant because customBalance already defines the same parameter.");
                            break;
                        }
                    }
                }

                bool structuredParameterAlreadySet =
                    (balanceOverride.name == "combatValue" && unit.combatValue.HasValue) ||
                    (balanceOverride.name == "gridSize" && unit.gridSize.HasValue) ||
                    (balanceOverride.name == "cardModelScalingFactor" && unit.cardModelScalingFactor.HasValue) ||
                    (balanceOverride.name == "coinsAmount" && unit.coinsAmount.HasValue) ||
                    (balanceOverride.name == "firePointCount" && unit.firePointCount.HasValue) ||
                    (balanceOverride.name == "copyUpgradesToEntityId" && !String.IsNullOrEmpty(unit.copyUpgradesToEntityId)) ||
                    (balanceOverride.name == "attackType" && !String.IsNullOrEmpty(unit.attackType)) ||
                    (balanceOverride.name == "skillType" && !String.IsNullOrEmpty(unit.skillType)) ||
                    (balanceOverride.name == "rarity" && !String.IsNullOrEmpty(unit.rarity)) ||
                    (balanceOverride.name == "tech" && !String.IsNullOrEmpty(unit.tech)) ||
                    (balanceOverride.name == "neededExperienceLevel" && unit.neededExperienceLevel.HasValue) ||
                    (balanceOverride.name == "offeredSystemTags" && unit.systemTags != null);
                if (structuredParameterAlreadySet)
                {
                    Add(diagnostics, "unit.balance-override-redundant-with-structured", itemPath,
                        "This balance override duplicates a structured unit field; declare the value only once.");
                }

                if (!TryValidateBalanceOverrideValue(balanceOverride.name, balanceOverride.value, "unit", false))
                {
                    Add(diagnostics, "unit.balance-override-invalid", itemPath,
                        "The override must target an allowlisted balance field and contain a valid invariant-culture value.");
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
                if (float.IsNaN(ovr.value) || float.IsInfinity(ovr.value))
                {
                    Add(diagnostics, "ai-override.value-invalid", itemPath + ".value",
                        "AI override value must be a finite number.");
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
                if (!IsFiniteSingle(skill.cooldown) || skill.cooldown < 0 || skill.cooldown > 300)
                {
                    Add(diagnostics, "active-skill.cooldown-invalid", itemPath + ".cooldown",
                        "Active skill cooldown must be finite and between 0 and 300.");
                }
                if (!IsFiniteSingle(skill.manaCost) || skill.manaCost < 0 || skill.manaCost > 1000)
                {
                    Add(diagnostics, "active-skill.manaCost-invalid", itemPath + ".manaCost",
                        "Active skill mana cost must be finite and between 0 and 1000.");
                }
                ValidateActiveSkillEffectValue(skill, itemPath, diagnostics);
            }
        }

        private static void ValidateActiveSkillEffectValue(
            ContentActiveSkillDefinition skill,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (skill == null)
            {
                return;
            }

            if (Single.IsNaN(skill.effectValue) || Single.IsInfinity(skill.effectValue))
            {
                Add(diagnostics, "active-skill.effect-value-invalid", path + ".effectValue",
                    "Active skill effect value must be finite.");
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
                if (!IsFiniteSingle(boost.multiplier) || boost.multiplier < 0 || boost.multiplier > 10)
                {
                    Add(diagnostics, "combat-boost.multiplier-invalid", itemPath + ".multiplier",
                        "Combat boost multiplier must be finite and between 0 and 10.");
                }
                if (!IsFiniteSingle(boost.duration) || boost.duration < 0 || boost.duration > 300)
                {
                    Add(diagnostics, "combat-boost.duration-invalid", itemPath + ".duration",
                        "Combat boost duration must be finite and between 0 and 300.");
                }
            }
        }
        private static void ValidateUnitUpgrades(
            ContentUnitUpgradeDefinition[] upgrades,
            string path,
            HashSet<string> localUnitIds,
            IContentRegistrySnapshot registrySnapshot,
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
                else if (!localUnitIds.Contains(upgrade.targetUnitId) &&
                    (registrySnapshot == null || !registrySnapshot.ContainsUnit(upgrade.targetUnitId)))
                {
                    Add(diagnostics, "unit-upgrade.target-unknown", itemPath + ".targetUnitId",
                        "Upgrade target unit must be a declared or native unit ID.");
                }
                if (upgrade.requiredExperienceLevel < 0)
                {
                    Add(diagnostics, "unit-upgrade.level-invalid", itemPath + ".requiredExperienceLevel",
                        "Required experience level must be non-negative.");
                }
                if (!IsFiniteSingle(upgrade.cost) || upgrade.cost < 0)
                {
                    Add(diagnostics, "unit-upgrade.cost-invalid", itemPath + ".cost",
                        "Upgrade cost must be finite and non-negative.");
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
            if (!IsFiniteSingle(experience.experienceGainMultiplier) ||
                experience.experienceGainMultiplier < 0 || experience.experienceGainMultiplier > 10)
            {
                Add(diagnostics, "unit-experience.gain-multiplier-invalid", path + ".experienceGainMultiplier",
                    "Experience gain multiplier must be finite and between 0 and 10.");
            }
            if (!IsFiniteSingle(experience.levelUpBonus) || experience.levelUpBonus < 0)
            {
                Add(diagnostics, "unit-experience.level-up-bonus-invalid", path + ".levelUpBonus",
                    "Level up bonus must be finite and non-negative.");
            }
        }

        private static void ValidateUnitSynergies(
            ContentUnitSynergyDefinition[] synergies,
            string path,
            HashSet<string> localUnitIds,
            IContentRegistrySnapshot registrySnapshot,
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
                if (!IsFiniteSingle(synergy.multiplier) || synergy.multiplier < 0 || synergy.multiplier > 10)
                {
                    Add(diagnostics, "unit-synergy.multiplier-invalid", itemPath + ".multiplier",
                        "Synergy multiplier must be finite and between 0 and 10.");
                }
                ValidateSynergyRadius(synergy, itemPath, diagnostics);
                if (synergy.targetUnitIds != null)
                {
                    for (int j = 0; j < synergy.targetUnitIds.Length; j++)
                    {
                        if (String.IsNullOrWhiteSpace(synergy.targetUnitIds[j]) ||
                            (!localUnitIds.Contains(synergy.targetUnitIds[j]) &&
                                (registrySnapshot == null || !registrySnapshot.ContainsUnit(synergy.targetUnitIds[j]))))
                        {
                            Add(diagnostics, "unit-synergy.target-unknown", itemPath + ".targetUnitIds[" + j + "]",
                                "Synergy target unit must be a declared or native unit ID.");
                        }
                    }
                }
            }
        }

        private const int MaxUpgradeChainDepth = 5;
        private const int MaxSynergyChainDepth = 5;

        private sealed class UnitReferenceGraphFrame
        {
            public UnitReferenceGraphFrame(string id) { Id = id; }
            public string Id;
            public int NextTarget;
            public int MaximumChildDepth;
        }

        private static void AnalyzeUnitReferenceGraph(ContentUnitDefinition[] units,
            Dictionary<string, List<string>> graph, string path, string cycleCode,
            string depthCode, int maxDepth, bool depthOnCycles, List<ContentDiagnostic> diagnostics)
        {
            var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < units.Length; i++)
                if (units[i] != null && !String.IsNullOrWhiteSpace(units[i].id) && !indexes.ContainsKey(units[i].id))
                    indexes.Add(units[i].id, i);

            var state = new byte[units.Length];
            var depths = new int[units.Length];
            var cycles = new bool[units.Length];
            var stack = new List<UnitReferenceGraphFrame>();
            for (int root = 0; root < units.Length; root++)
            {
                if (units[root] == null || String.IsNullOrWhiteSpace(units[root].id) || state[root] != 0) continue;
                state[root] = 1;
                stack.Add(new UnitReferenceGraphFrame(units[root].id));
                while (stack.Count != 0)
                {
                    UnitReferenceGraphFrame frame = stack[stack.Count - 1];
                    if (graph.TryGetValue(frame.Id, out List<string> targets) && frame.NextTarget < targets.Count)
                    {
                        string target = targets[frame.NextTarget++];
                        if (!indexes.TryGetValue(target, out int child)) continue;
                        if (state[child] == 0)
                        {
                            state[child] = 1;
                            stack.Add(new UnitReferenceGraphFrame(target));
                            continue;
                        }
                        if (state[child] == 1)
                        {
                            for (int j = stack.Count - 1; j >= 0; j--)
                            {
                                cycles[indexes[stack[j].Id]] = true;
                                if (String.Equals(stack[j].Id, target, StringComparison.Ordinal)) break;
                            }
                            continue;
                        }
                        int parent = indexes[frame.Id];
                        cycles[parent] |= cycles[child];
                        frame.MaximumChildDepth = Math.Max(frame.MaximumChildDepth, 1 + depths[child]);
                        continue;
                    }

                    int completed = indexes[frame.Id];
                    depths[completed] = frame.MaximumChildDepth;
                    state[completed] = 2;
                    stack.RemoveAt(stack.Count - 1);
                    if (stack.Count != 0)
                    {
                        UnitReferenceGraphFrame parent = stack[stack.Count - 1];
                        int parentIndex = indexes[parent.Id];
                        cycles[parentIndex] |= cycles[completed];
                        parent.MaximumChildDepth = Math.Max(parent.MaximumChildDepth, 1 + depths[completed]);
                    }
                }
            }

            bool upgrade = String.Equals(cycleCode, "unit-upgrade.cycle-detected", StringComparison.Ordinal);
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] == null || String.IsNullOrWhiteSpace(units[i].id) || !graph.ContainsKey(units[i].id)) continue;
                string itemPath = path + "[" + i + "]." + (upgrade ? "upgrades" : "synergies");
                if (cycles[i]) Add(diagnostics, cycleCode, itemPath,
                    upgrade ? "Unit upgrade chain contains a circular reference." : "Unit synergy graph contains a circular reference.");
                if (depths[i] > maxDepth && (!cycles[i] || depthOnCycles))
                    Add(diagnostics, depthCode, itemPath,
                        (upgrade ? "Unit upgrade chain" : "Unit synergy chain") + " exceeds maximum depth of " + maxDepth + ".");
            }
        }

        private static void ValidateUnitUpgradeGraph(
            ContentUnitDefinition[] units,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (units == null)
            {
                return;
            }

            var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < units.Length; i++)
            {
                ContentUnitDefinition unit = units[i];
                if (unit == null || String.IsNullOrWhiteSpace(unit.id))
                {
                    continue;
                }

                foreach (ContentUnitUpgradeDefinition upgrade in unit.upgrades ?? new ContentUnitUpgradeDefinition[0])
                {
                    if (upgrade == null || String.IsNullOrWhiteSpace(upgrade.targetUnitId))
                    {
                        continue;
                    }

                    if (!adjacency.TryGetValue(unit.id, out List<string> targets))
                    {
                        targets = new List<string>();
                        adjacency[unit.id] = targets;
                    }

                    targets.Add(upgrade.targetUnitId);
                }
            }

            AnalyzeUnitReferenceGraph(units, adjacency, path, "unit-upgrade.cycle-detected",
                "unit-upgrade.depth-exceeded", MaxUpgradeChainDepth, depthOnCycles: true, diagnostics);
        }


        private static void ValidateUnitSynergyGraph(
            ContentUnitDefinition[] units,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (units == null)
            {
                return;
            }

            var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < units.Length; i++)
            {
                ContentUnitDefinition unit = units[i];
                if (unit == null || String.IsNullOrWhiteSpace(unit.id))
                {
                    continue;
                }

                foreach (ContentUnitSynergyDefinition synergy in unit.synergies ?? new ContentUnitSynergyDefinition[0])
                {
                    if (synergy == null || synergy.targetUnitIds == null)
                    {
                        continue;
                    }

                    foreach (string target in synergy.targetUnitIds)
                    {
                        if (String.IsNullOrWhiteSpace(target))
                        {
                            continue;
                        }

                        if (!adjacency.TryGetValue(unit.id, out List<string> targets))
                        {
                            targets = new List<string>();
                            adjacency[unit.id] = targets;
                        }

                        targets.Add(target);
                    }
                }
            }

            AnalyzeUnitReferenceGraph(units, adjacency, path, "unit-synergy.cycle-detected",
                "unit-synergy.depth-exceeded", MaxSynergyChainDepth, depthOnCycles: false, diagnostics);
        }


        private static void ValidateUnitProductionQueueFeasibility(
            ContentUnitProductionQueueDefinition queue,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (queue == null)
            {
                return;
            }

            bool hasBuildTime = queue.buildTime > 0;
            bool hasResourceCost = queue.resourceCost > 0;
            bool hasQueueSize = queue.queueSize > 0;

            if (hasBuildTime != hasResourceCost)
            {
                Add(diagnostics, "unit-production-queue.resource-cost-required",
                    path, "Production queue must specify both buildTime and resourceCost, or neither.",
                    DiagnosticSeverity.Warning);
            }

            if (hasBuildTime && !hasQueueSize)
            {
                Add(diagnostics, "unit-production-queue.queue-size-required",
                    path, "Production queue must specify queueSize when buildTime is positive.",
                    DiagnosticSeverity.Warning);
            }

            if (hasQueueSize && !hasBuildTime)
            {
                Add(diagnostics, "unit-production-queue.build-time-required",
                    path, "Production queue must specify buildTime when queueSize is positive.",
                    DiagnosticSeverity.Warning);
            }
        }


        private static void ValidateUnitVariants(
            ContentUnitVariantDefinition[] variants,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (variants == null)
            {
                return;
            }

            if (variants.Length > MaxUnitVariantsPerUnit)
            {
                Add(diagnostics, "unit.variants-limit", path,
                    "A unit can declare at most " + MaxUnitVariantsPerUnit + " loadout variants.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < variants.Length; i++)
            {
                ContentUnitVariantDefinition variant = variants[i];
                string itemPath = path + "[" + i + "]";
                if (variant == null)
                {
                    Add(diagnostics, "entry.null", itemPath, "Unit loadout variants cannot be null.");
                    continue;
                }

                if (!IsValidUnitVariantId(variant.id) || !ids.Add(variant.id))
                {
                    Add(diagnostics, "unit.variant-id-invalid", itemPath + ".id",
                        "Variant IDs must be unique lowercase ASCII local IDs of at most 64 characters.");
                }

                if (variant.loadout != null && variant.loadout != "AP" && variant.loadout != "HE")
                {
                    Add(diagnostics, "unit.variant-loadout-invalid", itemPath + ".loadout",
                        "A declared variant loadout must be AP or HE.");
                }

                ValidateBalanceOverrides(variant.balanceOverrides, itemPath + ".balanceOverrides",
                    "unit.variant", "unit", diagnostics, false);
            }
        }

        private static bool IsValidUnitVariantId(string id)
        {
            if (String.IsNullOrEmpty(id) || id.Length > 64 || id[0] < 'a' || id[0] > 'z')
            {
                return false;
            }

            for (int i = 1; i < id.Length; i++)
            {
                char c = id[i];
                if ((c < 'a' || c > 'z') && (c < '0' || c > '9') &&
                    c != '.' && c != '_' && c != '-')
                {
                    return false;
                }
            }

            return true;
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
                ValidateVisualVariantScale(variant, itemPath, diagnostics);
            }
        }

        private static void ValidateUnitSpawn(
            ContentUnitSpawnDefinition spawn,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (spawn == null) return;
            if (!IsFiniteSingle(spawn.spawnRate) || spawn.spawnRate < 0)
            {
                Add(diagnostics, "unit-spawn.rate-invalid", path + ".spawnRate",
                    "Spawn rate must be finite and non-negative.");
            }
            if (spawn.maxSpawnCount < 0)
            {
                Add(diagnostics, "unit-spawn.max-count-invalid", path + ".maxSpawnCount",
                    "Max spawn count must be non-negative.");
            }
            if (!IsFiniteSingle(spawn.spawnCooldown) || spawn.spawnCooldown < 0)
            {
                Add(diagnostics, "unit-spawn.cooldown-invalid", path + ".spawnCooldown",
                    "Spawn cooldown must be finite and non-negative.");
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
                if (!IsFiniteSingle(effect.duration) || effect.duration < 0)
                {
                    Add(diagnostics, "unit-status-effect.duration-invalid", itemPath + ".duration",
                        "Status effect duration must be finite and non-negative.");
                }
                ValidateStatusEffectMagnitude(effect, itemPath, diagnostics);
            }
        }

        private static void ValidateUnitProductionQueue(
            ContentUnitProductionQueueDefinition queue,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (queue == null) return;
            if (!IsFiniteSingle(queue.buildTime) || queue.buildTime < 0)
            {
                Add(diagnostics, "unit-production-queue.build-time-invalid", path + ".buildTime",
                    "Build time must be finite and non-negative.");
            }
            if (!IsFiniteSingle(queue.resourceCost) || queue.resourceCost < 0)
            {
                Add(diagnostics, "unit-production-queue.resource-cost-invalid", path + ".resourceCost",
                    "Resource cost must be finite and non-negative.");
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
                if (!IsFiniteSingle(cost.amount) || cost.amount < 0)
                {
                    Add(diagnostics, "unit-resource-cost.amount-invalid", itemPath + ".amount",
                        "Resource amount must be finite and non-negative.");
                }
            }
        }

        private static void ValidateUnitMovement(
            ContentUnitMovementDefinition movement,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (movement == null) return;
            if (Single.IsNaN(movement.moveSpeed) || Single.IsInfinity(movement.moveSpeed) ||
                movement.moveSpeed < 0)
            {
                Add(diagnostics, "unit-movement.speed-invalid", path + ".moveSpeed",
                    "Move speed must be finite and non-negative.");
            }
            if (Single.IsNaN(movement.turnRate) || Single.IsInfinity(movement.turnRate) ||
                movement.turnRate < 0)
            {
                Add(diagnostics, "unit-movement.turn-rate-invalid", path + ".turnRate",
                    "Turn rate must be finite and non-negative.");
            }
            if (Single.IsNaN(movement.acceleration) || Single.IsInfinity(movement.acceleration) ||
                movement.acceleration < 0)
            {
                Add(diagnostics, "unit-movement.acceleration-invalid", path + ".acceleration",
                    "Acceleration must be finite and non-negative.");
            }
        }

        private static void ValidateUnitCustomBalance(
            Dictionary<string, double> customBalance,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (customBalance == null) return;
            foreach (KeyValuePair<string, double> parameter in customBalance)
            {
                if (!UnitBalanceOverrideMapper.TryValidateCustomBalanceValue(
                        parameter.Key, parameter.Value, out string diagnostic))
                {
                    string parameterPath = path + "." + (String.IsNullOrEmpty(parameter.Key) ? "<empty>" : parameter.Key);
                    string code = UnitBalanceOverrideMapper.TryGetBalanceParameterDefinition(parameter.Key, out _)
                        ? "unit-custom-balance.value-invalid"
                        : "unit-custom-balance.parameter-invalid";
                    Add(diagnostics, code, parameterPath, diagnostic);
                }
            }
        }

        private static void ValidateUnitDetection(
            ContentUnitDetectionDefinition detection,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (detection == null) return;
            if (!IsFiniteSingle(detection.detectionRange) || detection.detectionRange < 0)
            {
                Add(diagnostics, "unit-detection.range-invalid", path + ".detectionRange",
                    "Detection range must be finite and non-negative.");
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
                if (!IsFiniteSingle(weapon.damage) || weapon.damage < 0)
                {
                    Add(diagnostics, "unit-weapon.damage-invalid", itemPath + ".damage",
                        "Weapon damage must be finite and non-negative.");
                }
                if (!IsFiniteSingle(weapon.range) || weapon.range < 0)
                {
                    Add(diagnostics, "unit-weapon.range-invalid", itemPath + ".range",
                        "Weapon range must be finite and non-negative.");
                }
                if (!IsFiniteSingle(weapon.fireRate) || weapon.fireRate < 0)
                {
                    Add(diagnostics, "unit-weapon.fire-rate-invalid", itemPath + ".fireRate",
                        "Weapon fire rate must be finite and non-negative.");
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
            if (Single.IsNaN(armor.armorValue) || Single.IsInfinity(armor.armorValue) || armor.armorValue < 0)
            {
                Add(diagnostics, "unit-armor.value-invalid", path + ".armorValue",
                    "Armor value must be finite and non-negative.");
            }
            if (Single.IsNaN(armor.shieldValue) || Single.IsInfinity(armor.shieldValue) || armor.shieldValue < 0)
            {
                Add(diagnostics, "unit-armor.shield-invalid", path + ".shieldValue",
                    "Shield value must be finite and non-negative.");
            }
            if (Single.IsNaN(armor.damageReduction) || Single.IsInfinity(armor.damageReduction) ||
                armor.damageReduction < 0 || armor.damageReduction > 1)
            {
                Add(diagnostics, "unit-armor.reduction-invalid", path + ".damageReduction",
                    "Damage reduction must be finite and between 0 and 1.");
            }
        }

        private static void ValidateUnitHealing(
            ContentUnitHealingDefinition healing,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (healing == null) return;
            if (!IsFiniteSingle(healing.healAmount) || healing.healAmount < 0)
            {
                Add(diagnostics, "unit-healing.amount-invalid", path + ".healAmount",
                    "Heal amount must be finite and non-negative.");
            }
            if (!IsFiniteSingle(healing.healRange) || healing.healRange < 0)
            {
                Add(diagnostics, "unit-healing.range-invalid", path + ".healRange",
                    "Heal range must be finite and non-negative.");
            }
            if (!IsFiniteSingle(healing.healRate) || healing.healRate < 0)
            {
                Add(diagnostics, "unit-healing.rate-invalid", path + ".healRate",
                    "Heal rate must be finite and non-negative.");
            }
            if (!IsFiniteSingle(healing.repairAmount) || healing.repairAmount < 0)
            {
                Add(diagnostics, "unit-healing.repair-invalid", path + ".repairAmount",
                    "Repair amount must be finite and non-negative.");
            }
        }

        private static void ValidateUnitStealth(
            ContentUnitStealthDefinition stealth,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (stealth == null) return;
            if (!IsFiniteSingle(stealth.stealthLevel) || stealth.stealthLevel < 0 || stealth.stealthLevel > 10)
            {
                Add(diagnostics, "unit-stealth.level-invalid", path + ".stealthLevel",
                    "Stealth level must be finite and between 0 and 10.");
            }
            if (!IsFiniteSingle(stealth.cloakDuration) || stealth.cloakDuration < 0)
            {
                Add(diagnostics, "unit-stealth.duration-invalid", path + ".cloakDuration",
                    "Cloak duration must be finite and non-negative.");
            }
            if (!IsFiniteSingle(stealth.detectionReduction) ||
                stealth.detectionReduction < 0 || stealth.detectionReduction > 1)
            {
                Add(diagnostics, "unit-stealth.reduction-invalid", path + ".detectionReduction",
                    "Detection reduction must be finite and between 0 and 1.");
            }
        }

        private static void ValidateUnitCommand(
            ContentUnitCommandDefinition command,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (command == null) return;
            if (!IsFiniteSingle(command.commandRange) || command.commandRange < 0)
            {
                Add(diagnostics, "unit-command.range-invalid", path + ".commandRange",
                    "Command range must be finite and non-negative.");
            }
            if (!String.IsNullOrWhiteSpace(command.commandType) &&
                command.commandType != "Move" && command.commandType != "Attack" &&
                command.commandType != "Defend" && command.commandType != "Support")
            {
                Add(diagnostics, "unit-command.type-invalid", path + ".commandType",
                    "Command type must be Move, Attack, Defend, or Support.");
            }
            if (command.maxCommandChain < 0)
            {
                Add(diagnostics, "unit-command.chain-invalid", path + ".maxCommandChain",
                    "Max command chain must be non-negative.");
            }
        }

        private static void ValidateUnitExperienceReward(
            ContentUnitExperienceRewardDefinition reward,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (reward == null) return;
            if (reward.experienceAmount < 0)
            {
                Add(diagnostics, "unit-experience-reward.amount-invalid", path + ".experienceAmount",
                    "Experience amount must be non-negative.");
            }
            if (reward.experienceType != null && String.IsNullOrWhiteSpace(reward.experienceType))
            {
                Add(diagnostics, "unit-experience-reward.type-invalid", path + ".experienceType",
                    "Experience type must be non-empty when provided.");
            }
            if (!IsFiniteSingle(reward.bonusMultiplier) || reward.bonusMultiplier < 0)
            {
                Add(diagnostics, "unit-experience-reward.bonus-multiplier-invalid", path + ".bonusMultiplier",
                    "Bonus multiplier must be finite and non-negative.");
            }
        }

        private static void ValidateUnitAura(
            ContentUnitAuraDefinition aura,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (aura == null) return;
            if (!IsFiniteSingle(aura.auraRadius) || aura.auraRadius < 0)
            {
                Add(diagnostics, "unit-aura.radius-invalid", path + ".auraRadius",
                    "Aura radius must be finite and non-negative.");
            }
            if (!String.IsNullOrWhiteSpace(aura.auraType) &&
                aura.auraType != "Buff" && aura.auraType != "Debuff" &&
                aura.auraType != "Heal" && aura.auraType != "Shield")
            {
                Add(diagnostics, "unit-aura.type-invalid", path + ".auraType",
                    "Aura type must be Buff, Debuff, Heal, or Shield.");
            }
            if (!IsFiniteSingle(aura.buffAmount) || aura.buffAmount < 0)
            {
                Add(diagnostics, "unit-aura.buff-invalid", path + ".buffAmount",
                    "Buff amount must be finite and non-negative.");
            }
        }

        private static void ValidateUnitResourceGathering(
            ContentUnitResourceGatheringDefinition gathering,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (gathering == null) return;
            if (String.IsNullOrWhiteSpace(gathering.resourceType))
            {
                Add(diagnostics, "unit-gathering.type-required", path + ".resourceType",
                    "Resource type is required.");
            }
            if (!IsFiniteSingle(gathering.gatherRate) || gathering.gatherRate < 0)
            {
                Add(diagnostics, "unit-gathering.rate-invalid", path + ".gatherRate",
                    "Gather rate must be finite and non-negative.");
            }
            if (gathering.carryCapacity < 0)
            {
                Add(diagnostics, "unit-gathering.capacity-invalid", path + ".carryCapacity",
                    "Carry capacity must be non-negative.");
            }
            if (!IsFiniteSingle(gathering.gatherRange) || gathering.gatherRange < 0)
            {
                Add(diagnostics, "unit-gathering.range-invalid", path + ".gatherRange",
                    "Gather range must be finite and non-negative.");
            }
        }

        private static void ValidateUnitAiBehavior(
            ContentUnitAiBehaviorDefinition aiBehavior,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (aiBehavior == null) return;
            if (aiBehavior.unitCap < 0)
            {
                Add(diagnostics, "unit-ai.cap-invalid", path + ".unitCap",
                    "Unit cap must be non-negative.");
            }
            if (aiBehavior.combatUnitCap < 0)
            {
                Add(diagnostics, "unit-ai.combat-cap-invalid", path + ".combatUnitCap",
                    "Combat unit cap must be non-negative.");
            }
            if (!IsFiniteSingle(aiBehavior.skillActivationCheckInterval) ||
                aiBehavior.skillActivationCheckInterval < 0)
            {
                Add(diagnostics, "unit-ai.interval-invalid", path + ".skillActivationCheckInterval",
                    "Skill activation check interval must be finite and non-negative.");
            }
        }

        private static void ValidateUnitBalancingParameters(
            ContentUnitDefinition unit,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (unit == null)
            {
                return;
            }

            if (unit.combatValue.HasValue)
            {
                if (unit.combatValue.Value < 0 ||
                    unit.combatValue.Value > UnitBalanceOverrideMapper.MaximumContiguousSingleExactInteger)
                {
                    Add(diagnostics, "unit.combatValue-invalid", path + ".combatValue",
                        "combatValue must be a non-negative integer <= " +
                        UnitBalanceOverrideMapper.MaximumContiguousSingleExactInteger + ".");
                }
            }

            if (unit.gridSize.HasValue)
            {
                if (unit.gridSize.Value < 1 || unit.gridSize.Value > 16)
                {
                    Add(diagnostics, "unit.gridSize-invalid", path + ".gridSize",
                        "gridSize must be an integer between 1 and 16.");
                }
            }

            if (unit.cardModelScalingFactor.HasValue)
            {
                float factor = unit.cardModelScalingFactor.Value;
                if (Single.IsNaN(factor) || Single.IsInfinity(factor) || factor <= 0.01f || factor > 100f)
                {
                    Add(diagnostics, "unit.cardModelScalingFactor-invalid", path + ".cardModelScalingFactor",
                        "cardModelScalingFactor must be a finite positive number in range (0.01, 100.0].");
                }
            }

            if (unit.coinsAmount.HasValue)
            {
                if (unit.coinsAmount.Value < 0 ||
                    unit.coinsAmount.Value > UnitBalanceOverrideMapper.MaximumContiguousSingleExactInteger)
                {
                    Add(diagnostics, "unit.coinsAmount-invalid", path + ".coinsAmount",
                        "coinsAmount must be a non-negative integer <= " +
                        UnitBalanceOverrideMapper.MaximumContiguousSingleExactInteger + ".");
                }
            }

            if (unit.firePointCount.HasValue)
            {
                if (unit.firePointCount.Value < 1 || unit.firePointCount.Value > 64)
                {
                    Add(diagnostics, "unit.firePointCount-invalid", path + ".firePointCount",
                        "firePointCount must be an integer between 1 and 64.");
                }
            }

            if (!String.IsNullOrEmpty(unit.copyUpgradesToEntityId))
            {
                if (String.IsNullOrWhiteSpace(unit.copyUpgradesToEntityId) ||
                    String.Equals(unit.copyUpgradesToEntityId, unit.id, StringComparison.Ordinal))
                {
                    Add(diagnostics, "unit.copyUpgradesToEntityId-invalid", path + ".copyUpgradesToEntityId",
                        "copyUpgradesToEntityId must be a valid non-empty identifier distinct from the unit's own ID.");
                }
            }

            if (unit.roles != null)
            {
                if (unit.roles.Length > 16)
                {
                    Add(diagnostics, "unit.roles-invalid", path + ".roles",
                        "roles list cannot exceed 16 elements.");
                }

                HashSet<string> seenRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < unit.roles.Length; i++)
                {
                    string role = unit.roles[i];
                    string rolePath = path + ".roles[" + i + "]";
                    if (String.IsNullOrWhiteSpace(role) || !SupportedUnitRoles.Contains(role))
                    {
                        Add(diagnostics, "unit.roles-invalid", rolePath,
                            "Each role must be a supported UnitRole name.");
                    }
                    else if (!seenRoles.Add(role))
                    {
                        Add(diagnostics, "unit.roles-invalid", rolePath,
                            "Duplicate role '" + role + "' is not permitted.");
                    }
                }
            }

            if (!String.IsNullOrEmpty(unit.attackType))
            {
                if (!SupportedAttackTypes.Contains(unit.attackType))
                {
                    Add(diagnostics, "unit.attackType-invalid", path + ".attackType",
                        "attackType must match a supported native AttackType enum name.");
                }
            }

            if (!String.IsNullOrEmpty(unit.skillType))
            {
                if (!SupportedSkillTypes.Contains(unit.skillType))
                {
                    Add(diagnostics, "unit.skillType-invalid", path + ".skillType",
                        "skillType must match a supported native SkillType enum name.");
                }
            }

            if (!String.IsNullOrEmpty(unit.rarity))
            {
                if (!SupportedUnitRarities.Contains(unit.rarity))
                {
                    Add(diagnostics, "unit.rarity-invalid", path + ".rarity",
                        "rarity must be 'Common', 'Rare', or 'UltraRare'.");
                }
            }

            if (!String.IsNullOrEmpty(unit.tech))
            {
                if (!SupportedUnitTechs.Contains(unit.tech))
                {
                    Add(diagnostics, "unit.tech-invalid", path + ".tech",
                        "tech must match a supported native Tech enum name.");
                }
            }

            if (unit.neededExperienceLevel.HasValue)
            {
                if (unit.neededExperienceLevel.Value < 0 || unit.neededExperienceLevel.Value > 100)
                {
                    Add(diagnostics, "unit.neededExperienceLevel-invalid", path + ".neededExperienceLevel",
                        "neededExperienceLevel must be an integer between 0 and 100.");
                }
            }

            if (unit.systemTags != null)
            {
                if (unit.systemTags.Length > 16)
                {
                    Add(diagnostics, "unit.systemTags-invalid", path + ".systemTags",
                        "systemTags list cannot exceed 16 elements.");
                }

                HashSet<string> seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < unit.systemTags.Length; i++)
                {
                    string tag = unit.systemTags[i];
                    string tagPath = path + ".systemTags[" + i + "]";
                    if (String.IsNullOrWhiteSpace(tag) || !SupportedUnitSystemTags.Contains(tag))
                    {
                        Add(diagnostics, "unit.systemTags-invalid", tagPath,
                            "Each systemTag must match a supported native SystemTags enum flag.");
                    }
                    else if (!seenTags.Add(tag))
                    {
                        Add(diagnostics, "unit.systemTags-duplicate", tagPath,
                            "Duplicate systemTag '" + tag + "' is not permitted.");
                    }
                }
            }

            if (unit.baselineStatusEffects != null)
            {
                if (unit.baselineStatusEffects.Length > 16)
                {
                    Add(diagnostics, "unit.baselineStatusEffects-invalid", path + ".baselineStatusEffects",
                        "baselineStatusEffects list cannot exceed 16 elements.");
                }

                HashSet<string> seenEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < unit.baselineStatusEffects.Length; i++)
                {
                    string effect = unit.baselineStatusEffects[i];
                    string effectPath = path + ".baselineStatusEffects[" + i + "]";
                    if (String.IsNullOrWhiteSpace(effect) || !SupportedUnitStatusEffects.Contains(effect))
                    {
                        Add(diagnostics, "unit.baselineStatusEffects-invalid", effectPath,
                            "Each baselineStatusEffect must match a supported native StatusEffect enum flag.");
                    }
                    else if (!seenEffects.Add(effect))
                    {
                        Add(diagnostics, "unit.baselineStatusEffects-duplicate", effectPath,
                            "Duplicate baselineStatusEffect '" + effect + "' is not permitted.");
                    }
                }
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

            if (relic.coinsAmount < 0)
            {
                Add(diagnostics, "relic.coins-amount-invalid", path + ".coinsAmount",
                    "Relic coins amount cannot be negative.");
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

                if (String.IsNullOrWhiteSpace(change.valueToChange) ||
                    !SupportedRelicChangeableValues.Contains(change.valueToChange))
                {
                    Add(diagnostics, "relic.change-value-unsupported", changePath + ".valueToChange",
                        "Card change valueToChange must be one of the game's supported ChangeableValue attributes.");
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

                ValidateRelicRoleFilter(change.cardMustHaveAllOfTheseRoles, changePath + ".cardMustHaveAllOfTheseRoles", diagnostics);
                ValidateRelicRoleFilter(change.cardMustHaveOneOfTheseRoles, changePath + ".cardMustHaveOneOfTheseRoles", diagnostics);
                ValidateRelicRoleFilter(change.cardMustNotHaveOneOfTheseRoles, changePath + ".cardMustNotHaveOneOfTheseRoles", diagnostics);

                if (change.cardMustNotHaveOneOfTheseTechs != null)
                {
                    if (change.cardMustNotHaveOneOfTheseTechs.Length > 16)
                    {
                        Add(diagnostics, "relic.tech-filter-limit", changePath + ".cardMustNotHaveOneOfTheseTechs",
                            "A card change can declare at most 16 excluded tech flags.");
                    }
                    var seenTechs = new HashSet<string>(StringComparer.Ordinal);
                    for (int t = 0; t < change.cardMustNotHaveOneOfTheseTechs.Length; t++)
                    {
                        string techName = change.cardMustNotHaveOneOfTheseTechs[t];
                        if (String.IsNullOrWhiteSpace(techName) || !SupportedUnitTechs.Contains(techName))
                        {
                            Add(diagnostics, "relic.tech-filter-invalid", changePath + ".cardMustNotHaveOneOfTheseTechs[" + t + "]",
                                "Excluded tech must match one of the supported Tech values.");
                        }
                        else if (!seenTechs.Add(techName))
                        {
                            Add(diagnostics, "relic.tech-filter-duplicate", changePath + ".cardMustNotHaveOneOfTheseTechs[" + t + "]",
                                "Duplicate tech '" + techName + "' in excluded tech list.");
                        }
                    }
                }

                bool hasTargetIds = change.onlyForTheseEntityIds != null && change.onlyForTheseEntityIds.Length > 0;
                bool hasRoleFilter = (change.cardMustHaveAllOfTheseRoles != null && change.cardMustHaveAllOfTheseRoles.Length > 0) ||
                                     (change.cardMustHaveOneOfTheseRoles != null && change.cardMustHaveOneOfTheseRoles.Length > 0);
                bool hasSideFilter = String.Equals(change.side, "False", StringComparison.Ordinal) ||
                                     String.Equals(change.side, "True", StringComparison.Ordinal);

                if (hasTargetIds)
                {
                    if (change.onlyForTheseEntityIds.Length > 128)
                    {
                        Add(diagnostics, "relic.change-target-limit", changePath + ".onlyForTheseEntityIds",
                            "A card change can target at most 128 unit IDs.");
                    }

                    ValidateReferences(change.onlyForTheseEntityIds, changePath + ".onlyForTheseEntityIds",
                        unitIds, registrySnapshot, true, diagnostics);
                }
                else if (!hasRoleFilter && !hasSideFilter && !String.Equals(change.side, "Irrelevant", StringComparison.Ordinal))
                {
                    Add(diagnostics, "relic.change-target-required", changePath + ".onlyForTheseEntityIds",
                        "Every card change must specify target unit IDs, role filters, or a target side.");
                }

                if (change.entityIdsNotAllowed != null && change.entityIdsNotAllowed.Length > 0)
                {
                    if (change.entityIdsNotAllowed.Length > 128)
                    {
                        Add(diagnostics, "relic.excluded-target-limit", changePath + ".entityIdsNotAllowed",
                            "A card change can exclude at most 128 unit IDs.");
                    }

                    ValidateReferences(change.entityIdsNotAllowed, changePath + ".entityIdsNotAllowed",
                        unitIds, registrySnapshot, true, diagnostics);
                }
            }
        }

        private static void ValidateRelicRoleFilter(
            string[] roles,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (roles == null)
            {
                return;
            }

            if (roles.Length > 16)
            {
                Add(diagnostics, "relic.role-filter-limit", path,
                    "A card change can declare at most 16 role flags per filter.");
            }

            var seenRoles = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < roles.Length; i++)
            {
                string role = roles[i];
                if (String.IsNullOrWhiteSpace(role) || !SupportedUnitRoles.Contains(role))
                {
                    Add(diagnostics, "relic.role-filter-invalid", path + "[" + i + "]",
                        "Relic role filter must be one of the supported UnitRole values.");
                }
                else if (!seenRoles.Add(role))
                {
                    Add(diagnostics, "relic.role-filter-duplicate", path + "[" + i + "]",
                        "Duplicate role '" + role + "' in relic role filter.");
                }
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

        private static bool TryValidateBalanceOverrideValue(string name, string value, string ownerLabel, bool isFactoryRow)
        {
            if (String.IsNullOrWhiteSpace(name) || String.IsNullOrEmpty(value) ||
                !String.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                value[0] == '+' || value[0] == '-')
            {
                return false;
            }

            switch (name)
            {
                case "roles":
                case "offeredSystemTags":
                    return BalanceOverrideFlags.TryParse(name, value,
                        String.Equals(ownerLabel, "unit", StringComparison.Ordinal),
                        isFactoryRow, out _, out _);
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
                        out integerValue) && integerValue >= 0 &&
                        (!UnitBalanceOverrideMapper.IsNativeIntegerBalanceRowField(name) ||
                            integerValue <= UnitBalanceOverrideMapper.MaximumContiguousSingleExactInteger);
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
                case "startCountRange":
                {
                    int minimum;
                    int maximum;
                    return StageParameterOverrideParser.TryParseStartCountRange(value, out minimum, out maximum);
                }
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

        internal static void ValidateAiProfileStageRangeContiguity(
            ContentAiProfileDefinition profile,
            string path,
            ContentStageDefinition[] stages,
            List<ContentDiagnostic> diagnostics)
        {
            if (profile == null || !profile.scopeToReferencedStages || stages == null ||
                String.IsNullOrWhiteSpace(profile.id))
            {
                return;
            }

            var indices = new List<int>();
            for (int i = 0; i < stages.Length; i++)
            {
                ContentStageDefinition stage = stages[i];
                if (stage == null || stage.aiProfileIds == null)
                {
                    continue;
                }

                for (int j = 0; j < stage.aiProfileIds.Length; j++)
                {
                    if (String.Equals(stage.aiProfileIds[j], profile.id, StringComparison.Ordinal))
                    {
                        indices.Add(i);
                        break;
                    }
                }
            }

            indices.Sort();
            for (int i = 1; i < indices.Count; i++)
            {
                if (indices[i] != indices[i - 1] + 1)
                {
                    Add(diagnostics, "ai-profile.stage-range-not-contiguous",
                        path,
                        "A stage-scoped AI profile must reference a contiguous range of stage indices.");
                    break;
                }
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

        private static void ValidateStatusEffectMagnitude(
            ContentUnitStatusEffectDefinition effect,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (effect == null)
            {
                return;
            }

            if (Single.IsNaN(effect.magnitude) || Single.IsInfinity(effect.magnitude))
            {
                Add(diagnostics, "unit-status-effect.magnitude-invalid", path + ".magnitude",
                    "Status effect magnitude must be finite.");
            }
        }

        private static void ValidateVisualVariantScale(
            ContentUnitVisualVariantDefinition variant,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (variant == null)
            {
                return;
            }

            if (Single.IsNaN(variant.scale) || Single.IsInfinity(variant.scale) ||
                variant.scale < 0 || variant.scale > 10)
            {
                Add(diagnostics, "unit-visual-variant.scale-invalid", path + ".scale",
                    "Visual variant scale must be finite and between 0 and 10.");
            }
        }

        private static void ValidateSynergyRadius(
            ContentUnitSynergyDefinition synergy,
            string path,
            List<ContentDiagnostic> diagnostics)
        {
            if (synergy == null)
            {
                return;
            }

            if (Single.IsNaN(synergy.radius) || Single.IsInfinity(synergy.radius) || synergy.radius < 0)
            {
                Add(diagnostics, "unit-synergy.radius-invalid", path + ".radius",
                    "Synergy radius must be finite and non-negative.");
            }
        }

        private static bool IsFiniteSingle(float value)
        {
            return !Single.IsNaN(value) && !Single.IsInfinity(value);
        }

        private static void Add(List<ContentDiagnostic> diagnostics, string code, string path, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error)
        {
            diagnostics.Add(new ContentDiagnostic(code, path, message, severity));
        }
    }
}