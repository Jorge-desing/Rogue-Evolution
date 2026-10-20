using System;
using System.Collections.Generic;

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
        public ContentStageDefinition[] stages;
        public ContentAiProfileDefinition[] aiProfiles;
        public ContentRelicDefinition[] relics;
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
    public sealed class ContentUnitDefinition
    {
        public string id;
        public string unitType;
        public Dictionary<string, float> customBalance;
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
        public ContentUnitSpawnDefinition spawn;
        public ContentUnitStatusEffectDefinition[] statusEffects;
        public ContentUnitProductionQueueDefinition productionQueue;
        public ContentBalanceOverride[] balanceOverrides;
        public ContentUnitLocalizationDefinition[] localizations;
        public ContentProductionBuildingDefinition productionBuilding;
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
        public int neededExperienceLevel;
        public bool isAllowedForDemo;
        public bool isForSpecialists;
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
                customBalance = source.customBalance == null ? null : new Dictionary<string, float>(source.customBalance),
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
                spawn = CloneUnitSpawn(source.spawn),
                statusEffects = CloneArray(source.statusEffects, CloneUnitStatusEffect),
                productionQueue = CloneUnitProductionQueue(source.productionQueue),
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
            return source == null ? null : new ContentActiveSkillDefinition { id = source.id, skillType = source.skillType, cooldown = source.cooldown, manaCost = source.manaCost, effectValue = source.effectValue };
        }

        private static ContentCombatBoostDefinition CloneCombatBoost(ContentCombatBoostDefinition source)
        {
            return source == null ? null : new ContentCombatBoostDefinition { id = source.id, statName = source.statName, multiplier = source.multiplier, duration = source.duration };
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
                modules = CloneArray(source.modules, CloneAiModule)
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
                neededExperienceLevel = source.neededExperienceLevel,
                isAllowedForDemo = source.isAllowedForDemo,
                isForSpecialists = source.isForSpecialists,
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
                    : (string[])source.onlyForTheseEntityIds.Clone()
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
