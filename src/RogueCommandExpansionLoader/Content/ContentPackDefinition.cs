using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Runtime;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>
    /// Unity JsonUtility compatible DTOs for content.json. Keep these as plain
    /// serializable classes with public fields and arrays.
    /// </summary>
    [Serializable]
    public sealed class ContentPackDefinition
    {
        public int schemaVersion;
        public string packId;
        public string assetBundleManifestPath;
        public ContentAssetBundleDefinition[] assetBundles;
        public ContentUnitDefinition[] units;
        public ContentBuildingDefinition[] buildings;
        public ContentStageDefinition[] stages;
        public ContentAiProfileDefinition[] aiProfiles;
        public ContentRelicDefinition[] relics;

        public ContentPackDefinition Clone() => ContentPackCopy.DeepClone(this);
    }

    [Serializable]
    public sealed class ContentAssetBundleDefinition
    {
        public string id;
        public string path;
    }

    [Serializable]
    public sealed class ContentAbilityDefinition
    {
        public string id;
        public float value;
    }

    [Serializable]
    public sealed class ContentAiOverrideDefinition
    {
        public string parameter;
        public float value;
    }

    [Serializable]
    public sealed class ContentActiveSkillDefinition
    {
        public string id;
        public string skillType;
        public float cooldown;
        public float manaCost;
        public float effectValue;
        public string targetOrigin;
        public float? range;
        public float? radius;
        public bool? helpAiToActivate;
        public string aiActivationTrigger;
        public float? aiSkillCooldown;
    }

    [Serializable]
    public sealed class ContentCombatBoostDefinition
    {
        public string id;
        public string statName;
        public float multiplier;
        public float duration;
    }

    [Serializable]
    public sealed class ContentOnHitStatusEffectDefinition
    {
        public string statusEffect;
        public float duration;
        public float chance = 1.0f;
    }

    [Serializable]
    public sealed class ContentUnitUpgradeDefinition
    {
        public string targetUnitId;
        public int requiredExperienceLevel;
        public float cost;
        public string description;
    }

    [Serializable]
    public sealed class ContentUnitExperienceDefinition
    {
        public int experiencePerLevel;
        public int maxLevel;
        public float experienceGainMultiplier;
        public float levelUpBonus;
    }

    [Serializable]
    public sealed class ContentUnitSynergyDefinition
    {
        public string id;
        public string statName;
        public float multiplier;
        public float radius;
        public string[] targetUnitIds;
    }

    [Serializable]
    public sealed class ContentUnitVisualVariantDefinition
    {
        public string id;
        public string visualAssetName;
        public string visualRootPath;
        public string colorTint;
        public float scale;
    }

    [Serializable]
    public sealed class ContentUnitVariantDefinition
    {
        public string id;
        public string loadout;
        public ContentBalanceOverride[] balanceOverrides;
    }

    [Serializable]
    public sealed class ContentUnitSpawnDefinition
    {
        public float spawnRate;
        public int maxSpawnCount;
        public float spawnCooldown;
        public string spawnLocation;
    }

    [Serializable]
    public sealed class ContentUnitStatusEffectDefinition
    {
        public string id;
        public string effectType;
        public float duration;
        public float magnitude;
    }

    [Serializable]
    public sealed class ContentUnitProductionQueueDefinition
    {
        public float buildTime;
        public float resourceCost;
        public int queueSize;
        public int productionPriority;
    }

    [Serializable]
    public sealed class ContentUnitResourceCostDefinition
    {
        public string resourceType;
        public float amount;
    }

    [Serializable]
    public sealed class ContentUnitMovementDefinition
    {
        public float moveSpeed;
        public float turnRate;
        public float acceleration;
    }

    [Serializable]
    public sealed class ContentUnitDetectionDefinition
    {
        public float detectionRange;
        public string sensorType;
        public bool stealthDetection;
    }

    [Serializable]
    public sealed class ContentUnitWeaponDefinition
    {
        public string weaponType;
        public float damage;
        public float range;
        public float fireRate;
    }

    [Serializable]
    public sealed class ContentUnitArmorDefinition
    {
        public string armorType;
        public float armorValue;
        public float shieldValue;
        public float damageReduction;
    }

    [Serializable]
    public sealed class ContentUnitHealingDefinition
    {
        public float healAmount;
        public float healRange;
        public float healRate;
        public float repairAmount;
    }

    [Serializable]
    public sealed class ContentUnitStealthDefinition
    {
        public float stealthLevel;
        public float cloakDuration;
        public float detectionReduction;
    }

    [Serializable]
    public sealed class ContentUnitCommandDefinition
    {
        public float commandRange;
        public string commandType;
        public int maxCommandChain;
    }

    [Serializable]
    public sealed class ContentUnitExperienceRewardDefinition
    {
        public int experienceAmount;
        public string experienceType;
        public float bonusMultiplier;
    }

    [Serializable]
    public sealed class ContentUnitAuraDefinition
    {
        public float auraRadius;
        public string auraType;
        public float buffAmount;
        public bool affectsAllies;
        public bool affectsEnemies;
    }

    [Serializable]
    public sealed class ContentUnitResourceGatheringDefinition
    {
        public string resourceType;
        public float gatherRate;
        public int carryCapacity;
        public float gatherRange;
    }

    [Serializable]
    public sealed class ContentUnitAiBehaviorDefinition
    {
        public int unitCap;
        public int combatUnitCap;
        public float skillActivationCheckInterval;
        public string aiTag;
    }

    [Serializable]
    public sealed class ContentUnitShieldRegenDefinition
    {
        public float rechargeDelay;
        public float rechargeRate;
        public float burstAmount;
    }

    [Serializable]
    public sealed class ContentUnitVampiricLeechDefinition
    {
        public float siphonRatio;
        public bool siphonToShield;
        public float maxSiphonPerHit;
    }

    [Serializable]
    public sealed class ContentUnitTauntDefinition
    {
        public float pulseInterval;
        public float tauntRadius;
        public float duration;
        public int maxTargets;
    }

    [Serializable]
    public sealed class ContentUnitShieldOverloadNovaDefinition
    {
        public float novaRadius;
        public float damage;
        public float stunDuration;
        public float cooldown;
    }

    [Serializable]
    public sealed class ContentUnitDefinition
    {
        public string id;
        public string unitType;
        public Dictionary<string, double> customBalance;
        public string assetBundleId;
        public string balanceAssetName;
        public string prefabAssetName;
        public string cardImageAssetName;
        public string visualAssetName;
        public string visualRootPath;
        public string nativeTemplateId;
        public bool isAllowedAsStartingBlueprint;
        public bool isAllowedForAi;
        public bool isAllowedForDemo;
        public bool isForSpecialists;
        public ContentAbilityDefinition[] abilities;
        public ContentAiOverrideDefinition[] aiOverrides;
        public ContentActiveSkillDefinition[] activeSkills;
        public ContentCombatBoostDefinition[] combatBoosts;
        public ContentUnitUpgradeDefinition[] upgrades;
        public ContentUnitExperienceDefinition experience;
        public ContentUnitSynergyDefinition[] synergies;
        public ContentUnitVisualVariantDefinition[] visualVariants;
        public ContentUnitVariantDefinition[] variants;
        public ContentUnitSpawnDefinition spawn;
        public ContentUnitStatusEffectDefinition[] statusEffects;
        public ContentUnitProductionQueueDefinition productionQueue;
        public ContentUnitResourceCostDefinition[] resourceCosts;
        public ContentUnitMovementDefinition movement;
        public ContentUnitDetectionDefinition detection;
        public ContentUnitWeaponDefinition[] weapons;
        public ContentUnitArmorDefinition armor;
        public ContentUnitHealingDefinition healing;
        public ContentUnitStealthDefinition stealth;
        public ContentUnitCommandDefinition command;
        public ContentUnitExperienceRewardDefinition experienceReward;
        public ContentUnitAuraDefinition aura;
        public ContentUnitResourceGatheringDefinition resourceGathering;
        public ContentUnitAiBehaviorDefinition aiBehavior;
        public ContentUnitShieldRegenDefinition shieldRegeneration;
        public ContentUnitVampiricLeechDefinition vampiricLeech;
        public ContentUnitTauntDefinition tauntPulse;
        public ContentUnitShieldOverloadNovaDefinition shieldOverloadNova;
        public int? combatValue;
        public int? gridSize;
        public int? maxCapacity;
        public float? cardModelScalingFactor;
        public int? coinsAmount;
        public int? firePointCount;
        public string copyUpgradesToEntityId;
        public string[] roles;
        public string attackType;
        public string skillType;
        public string rarity;
        public string tech;
        public int? neededExperienceLevel;
        public string[] systemTags;
        public string[] baselineStatusEffects;
        public float? rotationDegreesPerSecond;
        public float? collisionRadius;
        public float? attackWarmUpTime;
        public bool? canAttackWhileMoving;
        public bool? shootAutomaticallyOnEnemiesWithinRange;
        public bool? defendPositionAggressivelyWhenIdle;
        public int? rangeToChooseNewTargetAutomatically;
        public float? minAttackRange;
        public ContentOnHitStatusEffectDefinition[] onHitStatusEffects;
        public float? criticalHitChance;
        public int? prespawnCount;
        public string defaultTacticalStance;
        public string[] startingAffixes;
        public string defaultLoadout;
        public int? startingVeterancyRank;
        public string selectionSoundAssetName;
        public string engineSoundAssetName;
        public string weaponSoundAssetName;
        public ContentBalanceOverride[] balanceOverrides;
        public ContentUnitLocalizationDefinition[] localizations;
        public ContentProductionBuildingDefinition productionBuilding;
    }

    internal enum BalanceParameterType
    {
        Int,
        Float
    }

    internal readonly struct BalanceParameterDefinition
    {
        public BalanceParameterDefinition(string rowFieldName, BalanceParameterType storageType)
        {
            RowFieldName = rowFieldName;
            StorageType = storageType;
        }

        public string RowFieldName { get; }
        public BalanceParameterType StorageType { get; }
    }

    internal static class UnitBalanceOverrideMapper
    {
        public const int MaximumContiguousSingleExactInteger = 1 << 24;

        // Bind only per-unit ChangeableValue members to the fields read from
        // EntityBalancingParameters by the pinned EntityBalancingStore. Global-only
        // values such as ManaRechargePerSecond must not become per-unit row overrides.
        private static readonly Dictionary<string, BalanceParameterDefinition> BalanceParameters =
            new Dictionary<string, BalanceParameterDefinition>(StringComparer.Ordinal)
            {
                ["Cost"] = new BalanceParameterDefinition("cost", BalanceParameterType.Int),
                ["ProductionDuration"] = new BalanceParameterDefinition("productionDuration", BalanceParameterType.Float),
                ["MaxCapacity"] = new BalanceParameterDefinition("maxCapacity", BalanceParameterType.Int),
                ["MaxHealth"] = new BalanceParameterDefinition("maxHealth", BalanceParameterType.Int),
                ["MaxShield"] = new BalanceParameterDefinition("maxShield", BalanceParameterType.Int),
                ["MaxMana"] = new BalanceParameterDefinition("maxMana", BalanceParameterType.Int),
                ["MaxRank"] = new BalanceParameterDefinition("maxRank", BalanceParameterType.Int),
                ["MoveSpeed"] = new BalanceParameterDefinition("moveSpeed", BalanceParameterType.Float),
                ["SightRadius"] = new BalanceParameterDefinition("sightRadius", BalanceParameterType.Int),
                ["Damage1"] = new BalanceParameterDefinition("damage1", BalanceParameterType.Float),
                ["Damage2"] = new BalanceParameterDefinition("damage2", BalanceParameterType.Float),
                ["WeaponRange"] = new BalanceParameterDefinition("weaponRange", BalanceParameterType.Float),
                ["Attack1Cooldown"] = new BalanceParameterDefinition("attackCooldown", BalanceParameterType.Float),
                ["Attack2Cooldown"] = new BalanceParameterDefinition("attack2Cooldown", BalanceParameterType.Float),
                ["EffectRadius1"] = new BalanceParameterDefinition("effectRadius1", BalanceParameterType.Float),
                ["EffectRadius2"] = new BalanceParameterDefinition("effectRadius2", BalanceParameterType.Float),
                ["SkillRange"] = new BalanceParameterDefinition("skillRange", BalanceParameterType.Int),
                ["HealAmount1"] = new BalanceParameterDefinition("healAmount1", BalanceParameterType.Float),
                ["HealAmount2"] = new BalanceParameterDefinition("healAmount2", BalanceParameterType.Float),
                ["GainCreditsAmount"] = new BalanceParameterDefinition("gainCreditsAmount", BalanceParameterType.Float),
                ["SkillManaCost"] = new BalanceParameterDefinition("skillManaCost", BalanceParameterType.Float),
                ["Duration1"] = new BalanceParameterDefinition("duration1", BalanceParameterType.Float),
                ["SpawnTtl"] = new BalanceParameterDefinition("duration2", BalanceParameterType.Float),
                ["MaxArmor"] = new BalanceParameterDefinition("maxArmor", BalanceParameterType.Float),
                ["ArmorProtection"] = new BalanceParameterDefinition("armorProtection", BalanceParameterType.Float)
            };

        public static Dictionary<string, BalanceParameterDefinition> CreateBalanceParameterMap()
        {
            return new Dictionary<string, BalanceParameterDefinition>(BalanceParameters, StringComparer.Ordinal);
        }

        public static bool TryGetBalanceParameterDefinition(
            string name,
            out BalanceParameterDefinition parameterDefinition)
        {
            if (name == null)
            {
                parameterDefinition = default(BalanceParameterDefinition);
                return false;
            }

            return BalanceParameters.TryGetValue(name, out parameterDefinition);
        }

        public static bool IsNativeIntegerBalanceRowField(string rowFieldName)
        {
            if (rowFieldName == null)
            {
                return false;
            }

            foreach (BalanceParameterDefinition parameterDefinition in BalanceParameters.Values)
            {
                if (parameterDefinition.StorageType == BalanceParameterType.Int &&
                    String.Equals(parameterDefinition.RowFieldName, rowFieldName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryValidateCustomBalanceValue(string name, double value, out string diagnostic)
        {
            if (!TryGetBalanceParameterDefinition(name, out BalanceParameterDefinition parameterDefinition))
            {
                diagnostic = "Custom balance keys must target per-unit ChangeableValue members with a native row field.";
                return false;
            }

            if (Double.IsNaN(value) || Double.IsInfinity(value) || value < 0d)
            {
                diagnostic = "Custom balance values must be finite and non-negative.";
                return false;
            }

            if (parameterDefinition.StorageType == BalanceParameterType.Int &&
                (Math.Truncate(value) != value || value > MaximumContiguousSingleExactInteger))
            {
                diagnostic = "Integer-backed balance fields require whole-number values within the exact native Single integer range.";
                return false;
            }

            if (parameterDefinition.StorageType == BalanceParameterType.Float && value > Single.MaxValue)
            {
                diagnostic = "Float-backed balance values must fit the native Single range.";
                return false;
            }

            diagnostic = null;
            return true;
        }

        public static Dictionary<string, double> CreateCustomBalance(ContentUnitDefinition unit)
        {
            var parameters = new Dictionary<string, double>(StringComparer.Ordinal);
            if (unit == null)
            {
                return parameters;
            }

            if (unit.customBalance != null)
            {
                foreach (KeyValuePair<string, double> parameter in unit.customBalance)
                {
                    if (!TryGetBalanceParameterDefinition(parameter.Key, out _))
                    {
                        throw new InvalidOperationException("Unsupported per-unit balance parameter '" +
                            parameter.Key + "'.");
                    }

                    parameters[parameter.Key] = parameter.Value;
                }
            }

            // Apply only a positive structured speed. A zero in this DTO is
            // indistinguishable from an omitted value; explicit zero belongs in the
            // native balanceOverrides list. Explicit raw row overrides take priority.
            if (unit.movement != null && unit.movement.moveSpeed > 0f &&
                !Single.IsNaN(unit.movement.moveSpeed) && !Single.IsInfinity(unit.movement.moveSpeed) &&
                !parameters.ContainsKey("MoveSpeed") && !HasExplicitRowOverride(unit, "moveSpeed"))
            {
                parameters.Add("MoveSpeed", unit.movement.moveSpeed);
            }

            return parameters;
        }

        public static List<BalanceOverride> CreateBalanceOverridesForLoadedUnits(
            IEnumerable<ContentUnitDefinition> candidateUnits,
            ISet<string> registeredUnitIds)
        {
            var overrides = new List<BalanceOverride>();
            if (candidateUnits == null || registeredUnitIds == null)
            {
                return overrides;
            }

            foreach (ContentUnitDefinition unit in candidateUnits)
            {
                if (unit == null || String.IsNullOrWhiteSpace(unit.id) || !registeredUnitIds.Contains(unit.id))
                {
                    continue;
                }

                Dictionary<string, double> parameters = CreateCustomBalance(unit);
                if (parameters.Count == 0)
                {
                    continue;
                }

                overrides.Add(new BalanceOverride
                {
                    EntityId = unit.id,
                    Parameters = parameters
                });
            }

            return overrides;
        }

        private static bool HasExplicitRowOverride(ContentUnitDefinition unit, string fieldName)
        {
            ContentBalanceOverride[] overrides = unit.balanceOverrides;
            if (overrides == null)
            {
                return false;
            }

            for (int i = 0; i < overrides.Length; i++)
            {
                if (overrides[i] != null && String.Equals(overrides[i].name, fieldName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class ContentProductionBuildingDefinition
    {
        public string id;
        public string nativeTemplateId;
        public string assetBundleId;
        public string cardImageAssetName;
        public string visualAssetName;
        public string visualRootPath;
        public int? gridSize;
        public int? maxCapacity;
        public int? cost;
        public float? buildTime;
        public int? combatValue;
        public string rarity;
        public string tech;
        public int? neededExperienceLevel;
        public string[] roles;
        public string[] systemTags;
        public float? cardModelScalingFactor;
        public ContentBalanceOverride[] balanceOverrides;
        public ContentUnitLocalizationDefinition[] localizations;
    }

    [Serializable]
    public sealed class ContentBuildingDefinition
    {
        public string id;
        public string nativeTemplateId;
        public string assetBundleId;
        public string cardImageAssetName;
        public string visualAssetName;
        public string visualRootPath;
        public int? gridSize;
        public int? maxCapacity;
        public int? cost;
        public float? buildTime;
        public int? combatValue;
        public string rarity;
        public string tech;
        public int? neededExperienceLevel;
        public string[] roles;
        public string[] systemTags;
        public float? cardModelScalingFactor;
        public int? firePointCount;
        public float? damage1;
        public float? attackCooldown;
        public float? weaponRange;
        public float? damage2;
        public float? attack2Cooldown;
        public string attackType;
        public string skillType;
        public int? skillRange;
        public float? skillManaCost;
        public float? healAmount1;
        public float? healAmount2;
        public float? effectRadius1;
        public float? effectRadius2;
        public float? duration1;
        public float? duration2;
        public bool? isAllowedAsBlueprint;
        public bool? isAllowedAsStartingBlueprint;
        public bool? isAllowedForAi;
        public bool? isAllowedForDemo;
        public bool? isForSpecialists;
        public float? rotationDegreesPerSecond;
        public float? collisionRadius;
        public float? attackWarmUpTime;
        public bool? canAttackWhileMoving;
        public bool? shootAutomaticallyOnEnemiesWithinRange;
        public bool? defendPositionAggressivelyWhenIdle;
        public int? rangeToChooseNewTargetAutomatically;
        public float? minAttackRange;
        public ContentOnHitStatusEffectDefinition[] onHitStatusEffects;
        public float? criticalHitChance;
        public int? prespawnCount;
        public string selectionSoundAssetName;
        public string engineSoundAssetName;
        public string weaponSoundAssetName;
        public ContentBalanceOverride[] balanceOverrides;
        public ContentUnitLocalizationDefinition[] localizations;
    }

    [Serializable]
    public sealed class ContentUnitLocalizationDefinition
    {
        public string language;
        public string name;
        public string description;
        public string skillDescription;
    }

    [Serializable]
    public sealed class ContentRelicDefinition
    {
        public string id;
        public string assetBundleId;
        public string imageAssetName;
        public string rarity;
        public string tech;
        public string offeredSystemTags;
        public string neededSystemTags;
        public int coinsAmount;
        public int neededExperienceLevel;
        public bool isAllowedForDemo;
        public bool isForSpecialists;
        public bool inactive;
        public ContentRelicLocalizationDefinition[] localizations;
        public ContentCardChangeDefinition[] cardChanges;
    }

    [Serializable]
    public sealed class ContentRelicLocalizationDefinition
    {
        public string language;
        public string name;
        public string description;
    }

    [Serializable]
    public sealed class ContentCardChangeDefinition
    {
        public string valueToChange;
        public string operation;
        public float value;
        public string side;
        public bool multiplyValueWithCurrentHeatLevel;
        public string multiplyValueWithMetaProgressionMultiplier;
        public string[] onlyForTheseEntityIds;
        public string[] cardMustHaveAllOfTheseRoles;
        public string[] cardMustHaveOneOfTheseRoles;
        public string[] cardMustNotHaveOneOfTheseRoles;
        public string[] cardMustNotHaveOneOfTheseTechs;
        public string[] entityIdsNotAllowed;
        public bool changeableValueHasToBeGreaterZero;
    }

    [Serializable]
    public sealed class ContentBalanceOverride
    {
        public string name;
        public string value;
    }

    [Serializable]
    public sealed class ContentStageDefinition
    {
        public string id;
        public string assetBundleId;
        public string assetName;
        public string nativeTemplateId;
        public ContentStageParameterOverride[] parameterOverrides;
        public int mapSizeMin;
        public int mapSizeMax;
        public bool isPartOfDemo;
        public string[] unitIds;
        public ContentStagePoolEntryDefinition[] blueprintPoolEntries;
        public string[] aiProfileIds;
        public ContentStageLandscapeDefinition landscape;
    }

    [Serializable]
    public sealed class ContentStageLandscapeDefinition
    {
        public string nativeTemplateId;
        public int randomSeed;
        public bool alwaysUseSameSeed;
        public int mapSize;
        public ContentLandscapeLocalizationDefinition[] localizations;
    }

    [Serializable]
    public sealed class ContentLandscapeLocalizationDefinition
    {
        public string language;
        public string name;
    }

    [Serializable]
    public sealed class ContentStageParameterOverride
    {
        public string name;
        public string value;
    }

    [Serializable]
    public sealed class ContentStagePoolEntryDefinition
    {
        public string poolId;
        public string[] unitIds;
    }

#nullable enable
    internal static class StageBlueprintFactoryIds
    {
        public static HashSet<string> CollectLoaderFactoryIds(
            ContentStageDefinition stage,
            Func<string, string?> resolveFactoryId)
        {
            if (stage == null)
            {
                throw new ArgumentNullException(nameof(stage));
            }

            if (resolveFactoryId == null)
            {
                throw new ArgumentNullException(nameof(resolveFactoryId));
            }

            var factoryIds = new HashSet<string>(StringComparer.Ordinal);
            AddLoaderFactoryIds(stage.unitIds, resolveFactoryId, factoryIds);

            ContentStagePoolEntryDefinition[] poolEntries = stage.blueprintPoolEntries;
            for (int i = 0; poolEntries != null && i < poolEntries.Length; i++)
            {
                if (poolEntries[i] != null)
                {
                    AddLoaderFactoryIds(poolEntries[i].unitIds, resolveFactoryId, factoryIds);
                }
            }

            return factoryIds;
        }

        private static void AddLoaderFactoryIds(
            string[] productIds,
            Func<string, string?> resolveFactoryId,
            HashSet<string> factoryIds)
        {
            for (int i = 0; productIds != null && i < productIds.Length; i++)
            {
                if (String.IsNullOrWhiteSpace(productIds[i]))
                {
                    continue;
                }

                string? factoryId = resolveFactoryId(productIds[i]);
                if (!String.IsNullOrWhiteSpace(factoryId))
                {
                    factoryIds.Add(factoryId);
                }
            }
        }
    }

    internal static class StageBlueprintPoolFactoryIds
    {
        public static Dictionary<string, HashSet<string>> CollectByPool(
            IEnumerable<string> stageFactoryIds,
            IEnumerable<string> availablePoolIds,
            ContentStagePoolEntryDefinition[] poolEntries,
            Func<string, string?> resolveFactoryId)
        {
            if (stageFactoryIds == null)
            {
                throw new ArgumentNullException(nameof(stageFactoryIds));
            }

            if (availablePoolIds == null)
            {
                throw new ArgumentNullException(nameof(availablePoolIds));
            }

            if (resolveFactoryId == null)
            {
                throw new ArgumentNullException(nameof(resolveFactoryId));
            }

            var stageIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string stageFactoryId in stageFactoryIds)
            {
                if (!String.IsNullOrWhiteSpace(stageFactoryId))
                {
                    stageIds.Add(stageFactoryId);
                }
            }

            var explicitPoolIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; poolEntries != null && i < poolEntries.Length; i++)
            {
                ContentStagePoolEntryDefinition entry = poolEntries[i];
                if (entry == null)
                {
                    continue;
                }

                if (String.IsNullOrWhiteSpace(entry.poolId))
                {
                    throw new ArgumentException("A stage pool entry must name a pool.", nameof(poolEntries));
                }

                explicitPoolIds.Add(entry.poolId);
            }

            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string poolId in availablePoolIds)
            {
                if (!String.IsNullOrWhiteSpace(poolId) && !result.ContainsKey(poolId))
                {
                    HashSet<string> initialUnitIds = explicitPoolIds.Contains(poolId)
                        ? new HashSet<string>(StringComparer.Ordinal)
                        : new HashSet<string>(stageIds, StringComparer.Ordinal);
                    result.Add(poolId, initialUnitIds);
                }
            }

            for (int i = 0; poolEntries != null && i < poolEntries.Length; i++)
            {
                ContentStagePoolEntryDefinition entry = poolEntries[i];
                if (entry == null)
                {
                    continue;
                }

                HashSet<string>? poolUnitIds;
                if (!result.TryGetValue(entry.poolId, out poolUnitIds) || poolUnitIds == null)
                {
                    poolUnitIds = new HashSet<string>(StringComparer.Ordinal);
                    result[entry.poolId] = poolUnitIds;
                }

                for (int j = 0; entry.unitIds != null && j < entry.unitIds.Length; j++)
                {
                    string productId = entry.unitIds[j];
                    if (String.IsNullOrWhiteSpace(productId))
                    {
                        continue;
                    }

                    string? factoryId = resolveFactoryId(productId);
                    string poolUnitId = String.IsNullOrWhiteSpace(factoryId) ? productId : factoryId;
                    poolUnitIds.Add(poolUnitId);
                }
            }

            return result;
        }
    }
#nullable restore

    [Serializable]
    public sealed class ContentAiProfileDefinition
    {
        public string id;
        public string assetBundleId;
        public string prefabAssetName;
        public string nativeTemplateId;
        public bool scopeToReferencedStages;
        public string[] unitDependencies;
        public ContentAiUnitEntryDefinition[] unitEntries;
        public ContentAiProductionBuildingEntryDefinition[] productionBuildingEntries;
        public ContentAiModuleDefinition[] modules;
        public string doctrine;
        public float? aggressionMultiplier;
    }

    [Serializable]
    public sealed class ContentAiUnitEntryDefinition
    {
        public string unitId;
        public int baseIndex;
        public int startingInAscensionLevel;
        public int countAtStart;
        public int maxLiving;
    }

    [Serializable]
    public sealed class ContentAiProductionBuildingEntryDefinition
    {
        public string unitId;
        public int baseIndex;
        public int startingInAscensionLevel;
        public int maxLiving;
    }

    [Serializable]
    public sealed class ContentAiModuleDefinition
    {
        public string moduleId;
        public ContentModuleParameter[] parameters;
    }

    /// <summary>
    /// Module parameters are string pairs so Unity JsonUtility can deserialize
    /// them without a custom JSON object/dictionary converter. A module defines
    /// the parameter names and parses values using invariant culture.
    /// </summary>
    [Serializable]
    public sealed class ContentModuleParameter
    {
        public string name;
        public string value;
    }

    internal static class ContentPackCopy
    {
        public static ContentPackDefinition DeepClone(ContentPackDefinition source)
        {
            if (source == null)
            {
                return null;
            }

            return new ContentPackDefinition
            {
                schemaVersion = source.schemaVersion,
                packId = source.packId,
                assetBundleManifestPath = source.assetBundleManifestPath,
                assetBundles = CloneArray(source.assetBundles, CloneBundle),
                units = CloneArray(source.units, CloneUnit),
                buildings = CloneArray(source.buildings, CloneBuilding),
                stages = CloneArray(source.stages, CloneStage),
                aiProfiles = CloneArray(source.aiProfiles, CloneAiProfile),
                relics = CloneArray(source.relics, CloneRelic)
            };
        }

        private static TOut[] CloneArray<TIn, TOut>(TIn[] source, Func<TIn, TOut> clone)
        {
            if (source == null)
            {
                return null;
            }

            var result = new TOut[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = clone(source[i]);
            }

            return result;
        }

        private static ContentAssetBundleDefinition CloneBundle(ContentAssetBundleDefinition source)
        {
            return source == null ? null : new ContentAssetBundleDefinition { id = source.id, path = source.path };
        }

        private static ContentUnitDefinition CloneUnit(ContentUnitDefinition source)
        {
            return source == null ? null : new ContentUnitDefinition
            {
                id = source.id,
                unitType = source.unitType,
                customBalance = source.customBalance == null ? null : new Dictionary<string, double>(source.customBalance),
                assetBundleId = source.assetBundleId,
                balanceAssetName = source.balanceAssetName,
                prefabAssetName = source.prefabAssetName,
                cardImageAssetName = source.cardImageAssetName,
                visualAssetName = source.visualAssetName,
                visualRootPath = source.visualRootPath,
                nativeTemplateId = source.nativeTemplateId,
                isAllowedAsStartingBlueprint = source.isAllowedAsStartingBlueprint,
                isAllowedForAi = source.isAllowedForAi,
                isAllowedForDemo = source.isAllowedForDemo,
                isForSpecialists = source.isForSpecialists,
                rotationDegreesPerSecond = source.rotationDegreesPerSecond,
                collisionRadius = source.collisionRadius,
                attackWarmUpTime = source.attackWarmUpTime,
                canAttackWhileMoving = source.canAttackWhileMoving,
                shootAutomaticallyOnEnemiesWithinRange = source.shootAutomaticallyOnEnemiesWithinRange,
                defendPositionAggressivelyWhenIdle = source.defendPositionAggressivelyWhenIdle,
                rangeToChooseNewTargetAutomatically = source.rangeToChooseNewTargetAutomatically,
                minAttackRange = source.minAttackRange,
                onHitStatusEffects = CloneArray(source.onHitStatusEffects, CloneOnHitStatusEffect),
                criticalHitChance = source.criticalHitChance,
                prespawnCount = source.prespawnCount,
                defaultTacticalStance = source.defaultTacticalStance,
                startingAffixes = source.startingAffixes != null ? (string[])source.startingAffixes.Clone() : null,
                defaultLoadout = source.defaultLoadout,
                startingVeterancyRank = source.startingVeterancyRank,
                selectionSoundAssetName = source.selectionSoundAssetName,
                engineSoundAssetName = source.engineSoundAssetName,
                weaponSoundAssetName = source.weaponSoundAssetName,
                balanceOverrides = CloneArray(source.balanceOverrides, CloneBalanceOverride),
                localizations = CloneArray(source.localizations, CloneUnitLocalization),
                abilities = CloneArray(source.abilities, CloneAbility),
                aiOverrides = CloneArray(source.aiOverrides, CloneAiOverride),
                activeSkills = CloneArray(source.activeSkills, CloneActiveSkill),
                combatBoosts = CloneArray(source.combatBoosts, CloneCombatBoost),
                upgrades = CloneArray(source.upgrades, CloneUnitUpgrade),
                experience = CloneUnitExperience(source.experience),
                synergies = CloneArray(source.synergies, CloneUnitSynergy),
                visualVariants = CloneArray(source.visualVariants, CloneUnitVisualVariant),
                variants = CloneArray(source.variants, CloneUnitVariant),
                spawn = CloneUnitSpawn(source.spawn),
                statusEffects = CloneArray(source.statusEffects, CloneUnitStatusEffect),
                productionQueue = CloneUnitProductionQueue(source.productionQueue),
                resourceCosts = CloneArray(source.resourceCosts, CloneUnitResourceCost),
                movement = CloneUnitMovement(source.movement),
                detection = CloneUnitDetection(source.detection),
                weapons = CloneArray(source.weapons, CloneUnitWeapon),
                armor = CloneUnitArmor(source.armor),
                healing = CloneUnitHealing(source.healing),
                stealth = CloneUnitStealth(source.stealth),
                command = CloneUnitCommand(source.command),
                experienceReward = CloneUnitExperienceReward(source.experienceReward),
                aura = CloneUnitAura(source.aura),
                resourceGathering = CloneUnitResourceGathering(source.resourceGathering),
                aiBehavior = CloneUnitAiBehavior(source.aiBehavior),
                shieldRegeneration = CloneShieldRegen(source.shieldRegeneration),
                vampiricLeech = CloneVampiricLeech(source.vampiricLeech),
                tauntPulse = CloneTaunt(source.tauntPulse),
                shieldOverloadNova = CloneShieldOverloadNova(source.shieldOverloadNova),
                combatValue = source.combatValue,
                gridSize = source.gridSize,
                maxCapacity = source.maxCapacity,
                cardModelScalingFactor = source.cardModelScalingFactor,
                coinsAmount = source.coinsAmount,
                firePointCount = source.firePointCount,
                copyUpgradesToEntityId = source.copyUpgradesToEntityId,
                roles = source.roles != null ? (string[])source.roles.Clone() : null,
                attackType = source.attackType,
                skillType = source.skillType,
                rarity = source.rarity,
                tech = source.tech,
                neededExperienceLevel = source.neededExperienceLevel,
                systemTags = source.systemTags != null ? (string[])source.systemTags.Clone() : null,
                baselineStatusEffects = source.baselineStatusEffects != null ? (string[])source.baselineStatusEffects.Clone() : null,
                productionBuilding = CloneProductionBuilding(source.productionBuilding)
            };
        }

        private static ContentAbilityDefinition CloneAbility(ContentAbilityDefinition source)
        {
            return source == null ? null : new ContentAbilityDefinition { id = source.id, value = source.value };
        }

        private static ContentAiOverrideDefinition CloneAiOverride(ContentAiOverrideDefinition source)
        {
            return source == null ? null : new ContentAiOverrideDefinition { parameter = source.parameter, value = source.value };
        }

        private static ContentActiveSkillDefinition CloneActiveSkill(ContentActiveSkillDefinition source)
        {
            return source == null ? null : new ContentActiveSkillDefinition
            {
                id = source.id,
                skillType = source.skillType,
                cooldown = source.cooldown,
                manaCost = source.manaCost,
                effectValue = source.effectValue,
                targetOrigin = source.targetOrigin,
                range = source.range,
                radius = source.radius,
                helpAiToActivate = source.helpAiToActivate,
                aiActivationTrigger = source.aiActivationTrigger,
                aiSkillCooldown = source.aiSkillCooldown
            };
        }

        private static ContentCombatBoostDefinition CloneCombatBoost(ContentCombatBoostDefinition source)
        {
            return source == null ? null : new ContentCombatBoostDefinition { id = source.id, statName = source.statName, multiplier = source.multiplier, duration = source.duration };
        }

        private static ContentOnHitStatusEffectDefinition CloneOnHitStatusEffect(ContentOnHitStatusEffectDefinition source)
        {
            return source == null ? null : new ContentOnHitStatusEffectDefinition
            {
                statusEffect = source.statusEffect,
                duration = source.duration,
                chance = source.chance
            };
        }

        private static ContentUnitUpgradeDefinition CloneUnitUpgrade(ContentUnitUpgradeDefinition source)
        {
            return source == null ? null : new ContentUnitUpgradeDefinition
            {
                targetUnitId = source.targetUnitId,
                requiredExperienceLevel = source.requiredExperienceLevel,
                cost = source.cost,
                description = source.description
            };
        }

        private static ContentUnitExperienceDefinition CloneUnitExperience(ContentUnitExperienceDefinition source)
        {
            return source == null ? null : new ContentUnitExperienceDefinition
            {
                experiencePerLevel = source.experiencePerLevel,
                maxLevel = source.maxLevel,
                experienceGainMultiplier = source.experienceGainMultiplier,
                levelUpBonus = source.levelUpBonus
            };
        }

        private static ContentUnitSynergyDefinition CloneUnitSynergy(ContentUnitSynergyDefinition source)
        {
            return source == null ? null : new ContentUnitSynergyDefinition
            {
                id = source.id,
                statName = source.statName,
                multiplier = source.multiplier,
                radius = source.radius,
                targetUnitIds = source.targetUnitIds == null ? null : (string[])source.targetUnitIds.Clone()
            };
        }

        private static ContentUnitVisualVariantDefinition CloneUnitVisualVariant(ContentUnitVisualVariantDefinition source)
        {
            return source == null ? null : new ContentUnitVisualVariantDefinition
            {
                id = source.id,
                visualAssetName = source.visualAssetName,
                visualRootPath = source.visualRootPath,
                colorTint = source.colorTint,
                scale = source.scale
            };
        }

        private static ContentUnitVariantDefinition CloneUnitVariant(ContentUnitVariantDefinition source)
        {
            return source == null ? null : new ContentUnitVariantDefinition
            {
                id = source.id,
                loadout = source.loadout,
                balanceOverrides = CloneArray(source.balanceOverrides, CloneBalanceOverride)
            };
        }

        private static ContentUnitSpawnDefinition CloneUnitSpawn(ContentUnitSpawnDefinition source)
        {
            return source == null ? null : new ContentUnitSpawnDefinition
            {
                spawnRate = source.spawnRate,
                maxSpawnCount = source.maxSpawnCount,
                spawnCooldown = source.spawnCooldown,
                spawnLocation = source.spawnLocation
            };
        }

        private static ContentUnitStatusEffectDefinition CloneUnitStatusEffect(ContentUnitStatusEffectDefinition source)
        {
            return source == null ? null : new ContentUnitStatusEffectDefinition
            {
                id = source.id,
                effectType = source.effectType,
                duration = source.duration,
                magnitude = source.magnitude
            };
        }

        private static ContentUnitProductionQueueDefinition CloneUnitProductionQueue(ContentUnitProductionQueueDefinition source)
        {
            return source == null ? null : new ContentUnitProductionQueueDefinition
            {
                buildTime = source.buildTime,
                resourceCost = source.resourceCost,
                queueSize = source.queueSize,
                productionPriority = source.productionPriority
            };
        }

        private static ContentUnitResourceCostDefinition CloneUnitResourceCost(ContentUnitResourceCostDefinition source)
        {
            return source == null ? null : new ContentUnitResourceCostDefinition
            {
                resourceType = source.resourceType,
                amount = source.amount
            };
        }

        private static ContentUnitMovementDefinition CloneUnitMovement(ContentUnitMovementDefinition source)
        {
            return source == null ? null : new ContentUnitMovementDefinition
            {
                moveSpeed = source.moveSpeed,
                turnRate = source.turnRate,
                acceleration = source.acceleration
            };
        }

        private static ContentUnitDetectionDefinition CloneUnitDetection(ContentUnitDetectionDefinition source)
        {
            return source == null ? null : new ContentUnitDetectionDefinition
            {
                detectionRange = source.detectionRange,
                sensorType = source.sensorType,
                stealthDetection = source.stealthDetection
            };
        }

        private static ContentUnitWeaponDefinition CloneUnitWeapon(ContentUnitWeaponDefinition source)
        {
            return source == null ? null : new ContentUnitWeaponDefinition
            {
                weaponType = source.weaponType,
                damage = source.damage,
                range = source.range,
                fireRate = source.fireRate
            };
        }

        private static ContentUnitArmorDefinition CloneUnitArmor(ContentUnitArmorDefinition source)
        {
            return source == null ? null : new ContentUnitArmorDefinition
            {
                armorType = source.armorType,
                armorValue = source.armorValue,
                shieldValue = source.shieldValue,
                damageReduction = source.damageReduction
            };
        }

        private static ContentUnitHealingDefinition CloneUnitHealing(ContentUnitHealingDefinition source)
        {
            return source == null ? null : new ContentUnitHealingDefinition
            {
                healAmount = source.healAmount,
                healRange = source.healRange,
                healRate = source.healRate,
                repairAmount = source.repairAmount
            };
        }

        private static ContentUnitStealthDefinition CloneUnitStealth(ContentUnitStealthDefinition source)
        {
            return source == null ? null : new ContentUnitStealthDefinition
            {
                stealthLevel = source.stealthLevel,
                cloakDuration = source.cloakDuration,
                detectionReduction = source.detectionReduction
            };
        }

        private static ContentUnitCommandDefinition CloneUnitCommand(ContentUnitCommandDefinition source)
        {
            return source == null ? null : new ContentUnitCommandDefinition
            {
                commandRange = source.commandRange,
                commandType = source.commandType,
                maxCommandChain = source.maxCommandChain
            };
        }

        private static ContentUnitExperienceRewardDefinition CloneUnitExperienceReward(ContentUnitExperienceRewardDefinition source)
        {
            return source == null ? null : new ContentUnitExperienceRewardDefinition
            {
                experienceAmount = source.experienceAmount,
                experienceType = source.experienceType,
                bonusMultiplier = source.bonusMultiplier
            };
        }

        private static ContentUnitAuraDefinition CloneUnitAura(ContentUnitAuraDefinition source)
        {
            return source == null ? null : new ContentUnitAuraDefinition
            {
                auraRadius = source.auraRadius,
                auraType = source.auraType,
                buffAmount = source.buffAmount,
                affectsAllies = source.affectsAllies,
                affectsEnemies = source.affectsEnemies
            };
        }

        private static ContentUnitResourceGatheringDefinition CloneUnitResourceGathering(ContentUnitResourceGatheringDefinition source)
        {
            return source == null ? null : new ContentUnitResourceGatheringDefinition
            {
                resourceType = source.resourceType,
                gatherRate = source.gatherRate,
                carryCapacity = source.carryCapacity,
                gatherRange = source.gatherRange
            };
        }

        private static ContentUnitAiBehaviorDefinition CloneUnitAiBehavior(ContentUnitAiBehaviorDefinition source)
        {
            return source == null ? null : new ContentUnitAiBehaviorDefinition
            {
                unitCap = source.unitCap,
                combatUnitCap = source.combatUnitCap,
                skillActivationCheckInterval = source.skillActivationCheckInterval,
                aiTag = source.aiTag
            };
        }

        private static ContentUnitShieldRegenDefinition CloneShieldRegen(ContentUnitShieldRegenDefinition source)
        {
            return source == null ? null : new ContentUnitShieldRegenDefinition
            {
                rechargeDelay = source.rechargeDelay,
                rechargeRate = source.rechargeRate,
                burstAmount = source.burstAmount
            };
        }

        private static ContentUnitVampiricLeechDefinition CloneVampiricLeech(ContentUnitVampiricLeechDefinition source)
        {
            return source == null ? null : new ContentUnitVampiricLeechDefinition
            {
                siphonRatio = source.siphonRatio,
                siphonToShield = source.siphonToShield,
                maxSiphonPerHit = source.maxSiphonPerHit
            };
        }

        private static ContentUnitTauntDefinition CloneTaunt(ContentUnitTauntDefinition source)
        {
            return source == null ? null : new ContentUnitTauntDefinition
            {
                pulseInterval = source.pulseInterval,
                tauntRadius = source.tauntRadius,
                duration = source.duration,
                maxTargets = source.maxTargets
            };
        }

        private static ContentUnitShieldOverloadNovaDefinition CloneShieldOverloadNova(ContentUnitShieldOverloadNovaDefinition source)
        {
            return source == null ? null : new ContentUnitShieldOverloadNovaDefinition
            {
                novaRadius = source.novaRadius,
                damage = source.damage,
                stunDuration = source.stunDuration,
                cooldown = source.cooldown
            };
        }

        private static ContentProductionBuildingDefinition CloneProductionBuilding(
            ContentProductionBuildingDefinition source)
        {
            return source == null ? null : new ContentProductionBuildingDefinition
            {
                id = source.id,
                nativeTemplateId = source.nativeTemplateId,
                assetBundleId = source.assetBundleId,
                cardImageAssetName = source.cardImageAssetName,
                visualAssetName = source.visualAssetName,
                visualRootPath = source.visualRootPath,
                gridSize = source.gridSize,
                maxCapacity = source.maxCapacity,
                cost = source.cost,
                buildTime = source.buildTime,
                combatValue = source.combatValue,
                rarity = source.rarity,
                tech = source.tech,
                neededExperienceLevel = source.neededExperienceLevel,
                roles = source.roles != null ? (string[])source.roles.Clone() : null,
                systemTags = source.systemTags != null ? (string[])source.systemTags.Clone() : null,
                cardModelScalingFactor = source.cardModelScalingFactor,
                balanceOverrides = CloneArray(source.balanceOverrides, CloneBalanceOverride),
                localizations = CloneArray(source.localizations, CloneUnitLocalization)
            };
        }

        private static ContentBuildingDefinition CloneBuilding(ContentBuildingDefinition source)
        {
            return source == null ? null : new ContentBuildingDefinition
            {
                id = source.id,
                nativeTemplateId = source.nativeTemplateId,
                assetBundleId = source.assetBundleId,
                cardImageAssetName = source.cardImageAssetName,
                visualAssetName = source.visualAssetName,
                visualRootPath = source.visualRootPath,
                gridSize = source.gridSize,
                maxCapacity = source.maxCapacity,
                cost = source.cost,
                buildTime = source.buildTime,
                combatValue = source.combatValue,
                rarity = source.rarity,
                tech = source.tech,
                neededExperienceLevel = source.neededExperienceLevel,
                roles = source.roles != null ? (string[])source.roles.Clone() : null,
                systemTags = source.systemTags != null ? (string[])source.systemTags.Clone() : null,
                cardModelScalingFactor = source.cardModelScalingFactor,
                firePointCount = source.firePointCount,
                damage1 = source.damage1,
                attackCooldown = source.attackCooldown,
                weaponRange = source.weaponRange,
                damage2 = source.damage2,
                attack2Cooldown = source.attack2Cooldown,
                attackType = source.attackType,
                skillType = source.skillType,
                skillRange = source.skillRange,
                skillManaCost = source.skillManaCost,
                healAmount1 = source.healAmount1,
                healAmount2 = source.healAmount2,
                effectRadius1 = source.effectRadius1,
                effectRadius2 = source.effectRadius2,
                duration1 = source.duration1,
                duration2 = source.duration2,
                isAllowedAsBlueprint = source.isAllowedAsBlueprint,
                isAllowedAsStartingBlueprint = source.isAllowedAsStartingBlueprint,
                isAllowedForAi = source.isAllowedForAi,
                isAllowedForDemo = source.isAllowedForDemo,
                isForSpecialists = source.isForSpecialists,
                rotationDegreesPerSecond = source.rotationDegreesPerSecond,
                collisionRadius = source.collisionRadius,
                attackWarmUpTime = source.attackWarmUpTime,
                canAttackWhileMoving = source.canAttackWhileMoving,
                shootAutomaticallyOnEnemiesWithinRange = source.shootAutomaticallyOnEnemiesWithinRange,
                defendPositionAggressivelyWhenIdle = source.defendPositionAggressivelyWhenIdle,
                rangeToChooseNewTargetAutomatically = source.rangeToChooseNewTargetAutomatically,
                minAttackRange = source.minAttackRange,
                onHitStatusEffects = CloneArray(source.onHitStatusEffects, CloneOnHitStatusEffect),
                criticalHitChance = source.criticalHitChance,
                prespawnCount = source.prespawnCount,
                selectionSoundAssetName = source.selectionSoundAssetName,
                engineSoundAssetName = source.engineSoundAssetName,
                weaponSoundAssetName = source.weaponSoundAssetName,
                balanceOverrides = CloneArray(source.balanceOverrides, CloneBalanceOverride),
                localizations = CloneArray(source.localizations, CloneUnitLocalization)
            };
        }

        private static ContentUnitLocalizationDefinition CloneUnitLocalization(
            ContentUnitLocalizationDefinition source)
        {
            return source == null ? null : new ContentUnitLocalizationDefinition
            {
                language = source.language,
                name = source.name,
                description = source.description,
                skillDescription = source.skillDescription
            };
        }

        private static ContentBalanceOverride CloneBalanceOverride(ContentBalanceOverride source)
        {
            return source == null ? null : new ContentBalanceOverride { name = source.name, value = source.value };
        }

        private static ContentStageDefinition CloneStage(ContentStageDefinition source)
        {
            return source == null ? null : new ContentStageDefinition
            {
                id = source.id,
                assetBundleId = source.assetBundleId,
                assetName = source.assetName,
                nativeTemplateId = source.nativeTemplateId,
                parameterOverrides = CloneArray(source.parameterOverrides, CloneStageParameterOverride),
                mapSizeMin = source.mapSizeMin,
                mapSizeMax = source.mapSizeMax,
                isPartOfDemo = source.isPartOfDemo,
                unitIds = source.unitIds == null ? null : (string[])source.unitIds.Clone(),
                blueprintPoolEntries = CloneArray(source.blueprintPoolEntries, CloneStagePoolEntry),
                aiProfileIds = source.aiProfileIds == null ? null : (string[])source.aiProfileIds.Clone(),
                landscape = CloneLandscape(source.landscape)
            };
        }

        private static ContentStageLandscapeDefinition CloneLandscape(ContentStageLandscapeDefinition source)
        {
            return source == null ? null : new ContentStageLandscapeDefinition
            {
                nativeTemplateId = source.nativeTemplateId,
                randomSeed = source.randomSeed,
                alwaysUseSameSeed = source.alwaysUseSameSeed,
                mapSize = source.mapSize,
                localizations = CloneArray(source.localizations, CloneLandscapeLocalization)
            };
        }

        private static ContentLandscapeLocalizationDefinition CloneLandscapeLocalization(
            ContentLandscapeLocalizationDefinition source)
        {
            return source == null ? null : new ContentLandscapeLocalizationDefinition
            {
                language = source.language,
                name = source.name
            };
        }

        private static ContentStageParameterOverride CloneStageParameterOverride(ContentStageParameterOverride source)
        {
            return source == null ? null : new ContentStageParameterOverride { name = source.name, value = source.value };
        }

        private static ContentStagePoolEntryDefinition CloneStagePoolEntry(ContentStagePoolEntryDefinition source)
        {
            return source == null ? null : new ContentStagePoolEntryDefinition
            {
                poolId = source.poolId,
                unitIds = source.unitIds == null ? null : (string[])source.unitIds.Clone()
            };
        }

        private static ContentAiProfileDefinition CloneAiProfile(ContentAiProfileDefinition source)
        {
            return source == null ? null : new ContentAiProfileDefinition
            {
                id = source.id,
                assetBundleId = source.assetBundleId,
                prefabAssetName = source.prefabAssetName,
                nativeTemplateId = source.nativeTemplateId,
                scopeToReferencedStages = source.scopeToReferencedStages,
                unitDependencies = source.unitDependencies == null
                    ? null
                    : (string[])source.unitDependencies.Clone(),
                unitEntries = CloneArray(source.unitEntries, CloneAiUnitEntry),
                productionBuildingEntries = CloneArray(source.productionBuildingEntries,
                    CloneAiProductionBuildingEntry),
                modules = CloneArray(source.modules, CloneAiModule),
                doctrine = source.doctrine,
                aggressionMultiplier = source.aggressionMultiplier
            };
        }

        private static ContentRelicDefinition CloneRelic(ContentRelicDefinition source)
        {
            return source == null ? null : new ContentRelicDefinition
            {
                id = source.id,
                assetBundleId = source.assetBundleId,
                imageAssetName = source.imageAssetName,
                rarity = source.rarity,
                tech = source.tech,
                offeredSystemTags = source.offeredSystemTags,
                neededSystemTags = source.neededSystemTags,
                coinsAmount = source.coinsAmount,
                neededExperienceLevel = source.neededExperienceLevel,
                isAllowedForDemo = source.isAllowedForDemo,
                isForSpecialists = source.isForSpecialists,
                inactive = source.inactive,
                localizations = CloneArray(source.localizations, CloneRelicLocalization),
                cardChanges = CloneArray(source.cardChanges, CloneCardChange)
            };
        }

        private static ContentRelicLocalizationDefinition CloneRelicLocalization(
            ContentRelicLocalizationDefinition source)
        {
            return source == null ? null : new ContentRelicLocalizationDefinition
            {
                language = source.language,
                name = source.name,
                description = source.description
            };
        }

        private static ContentCardChangeDefinition CloneCardChange(ContentCardChangeDefinition source)
        {
            return source == null ? null : new ContentCardChangeDefinition
            {
                valueToChange = source.valueToChange,
                operation = source.operation,
                value = source.value,
                side = source.side,
                multiplyValueWithCurrentHeatLevel = source.multiplyValueWithCurrentHeatLevel,
                multiplyValueWithMetaProgressionMultiplier = source.multiplyValueWithMetaProgressionMultiplier,
                onlyForTheseEntityIds = source.onlyForTheseEntityIds == null
                    ? null
                    : (string[])source.onlyForTheseEntityIds.Clone(),
                cardMustHaveAllOfTheseRoles = source.cardMustHaveAllOfTheseRoles == null
                    ? null
                    : (string[])source.cardMustHaveAllOfTheseRoles.Clone(),
                cardMustHaveOneOfTheseRoles = source.cardMustHaveOneOfTheseRoles == null
                    ? null
                    : (string[])source.cardMustHaveOneOfTheseRoles.Clone(),
                cardMustNotHaveOneOfTheseRoles = source.cardMustNotHaveOneOfTheseRoles == null
                    ? null
                    : (string[])source.cardMustNotHaveOneOfTheseRoles.Clone(),
                cardMustNotHaveOneOfTheseTechs = source.cardMustNotHaveOneOfTheseTechs == null
                    ? null
                    : (string[])source.cardMustNotHaveOneOfTheseTechs.Clone(),
                entityIdsNotAllowed = source.entityIdsNotAllowed == null
                    ? null
                    : (string[])source.entityIdsNotAllowed.Clone(),
                changeableValueHasToBeGreaterZero = source.changeableValueHasToBeGreaterZero
            };
        }

        private static ContentAiUnitEntryDefinition CloneAiUnitEntry(ContentAiUnitEntryDefinition source)
        {
            return source == null ? null : new ContentAiUnitEntryDefinition
            {
                unitId = source.unitId,
                baseIndex = source.baseIndex,
                startingInAscensionLevel = source.startingInAscensionLevel,
                countAtStart = source.countAtStart,
                maxLiving = source.maxLiving
            };
        }

        private static ContentAiProductionBuildingEntryDefinition CloneAiProductionBuildingEntry(
            ContentAiProductionBuildingEntryDefinition source)
        {
            return source == null ? null : new ContentAiProductionBuildingEntryDefinition
            {
                unitId = source.unitId,
                baseIndex = source.baseIndex,
                startingInAscensionLevel = source.startingInAscensionLevel,
                maxLiving = source.maxLiving
            };
        }

        private static ContentAiModuleDefinition CloneAiModule(ContentAiModuleDefinition source)
        {
            return source == null ? null : new ContentAiModuleDefinition
            {
                moduleId = source.moduleId,
                parameters = CloneArray(source.parameters, CloneParameter)
            };
        }

        private static ContentModuleParameter CloneParameter(ContentModuleParameter source)
        {
            return source == null ? null : new ContentModuleParameter { name = source.name, value = source.value };
        }
    }
}
