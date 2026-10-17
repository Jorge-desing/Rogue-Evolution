using System;

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
    }

    [Serializable]
    public sealed class ContentAssetBundleDefinition
    {
        public string id;
        public string path;
    }

    [Serializable]
    public sealed class ContentUnitDefinition
    {
        public string id;
        public string assetBundleId;
        public string balanceAssetName;
        public string prefabAssetName;
        public string cardImageAssetName;
        public string visualAssetName;
        public string visualRootPath;
        public string nativeTemplateId;
        public ContentBalanceOverride[] balanceOverrides;
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
        public ContentAiModuleDefinition[] modules;
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
                aiProfiles = CloneArray(source.aiProfiles, CloneAiProfile)
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
                assetBundleId = source.assetBundleId,
                balanceAssetName = source.balanceAssetName,
                prefabAssetName = source.prefabAssetName,
                cardImageAssetName = source.cardImageAssetName,
                visualAssetName = source.visualAssetName,
                visualRootPath = source.visualRootPath,
                nativeTemplateId = source.nativeTemplateId,
                balanceOverrides = CloneArray(source.balanceOverrides, CloneBalanceOverride)
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
                aiProfileIds = source.aiProfileIds == null ? null : (string[])source.aiProfileIds.Clone()
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
                modules = CloneArray(source.modules, CloneAiModule)
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
