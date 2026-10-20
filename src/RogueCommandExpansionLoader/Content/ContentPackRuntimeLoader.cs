using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Modules;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>
    /// Discovers and validates data-only packs after the native entity registry is ready, then
    /// keeps their AssetBundles and Unity objects alive for the whole game session.
    /// </summary>
    public sealed class ContentPackRuntimeLoader : IRuntimeContentProvider, IUnitSkillLocalizationRuntimeProvider,
        IRelicLocalizationRuntimeProvider, IStageLandscapeRuntimeProvider,
        IBlueprintPoolRuntimeProvider, IStageFallbackRuntimeProvider,
        IStageOwnedAiPlacementDistanceRuntimeProvider, IBlueprintRewardFactoryScopeProvider
    {
        private const int MaxManifestBytes = 1024 * 1024;
        private const uint GenericRead = 0x80000000;
        private const uint ShareRead = 0x00000001;
        private const uint ShareWrite = 0x00000002;
        private const uint ShareDelete = 0x00000004;
        private const uint OpenExisting = 3;
        private const uint OpenReparsePoint = 0x00200000;
        private const uint BackupSemantics = 0x02000000;
        private const uint SequentialScan = 0x08000000;
        private const int FileAttributeTagInfoClass = 9;
        private const uint DirectoryAttribute = 0x00000010;
        private const uint ReparsePointAttribute = 0x00000400;
        private const BindingFlags InstanceFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags StaticFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private readonly object _gate = new object();
        private readonly string _packsRoot;
        private readonly Assembly _gameAssembly;
        private readonly RelicContentRuntimeRegistrar _relicRegistrar;
        private readonly AiModuleRegistry _moduleRegistry;
        private readonly Action<string> _log;
        private readonly Type _entityRowType;
        private readonly Type _entityBalanceAssetType;
        private readonly Type _entityControllerType;
        private readonly PropertyInfo _localizationLanguageProperty;
        private readonly MethodInfo _blueprintNameMethod;
        private readonly MethodInfo _blueprintDescriptionMethod;
        private readonly MethodInfo _skillDescriptionMethod;
        private readonly Type _stageType;
        private readonly Type _stageParametersType;
        private readonly Type _aiBehaviourType;
        private readonly Type _defaultAiBehaviourType;
        private readonly Type _aiDeckEntryType;
        private readonly Type _aiBaseType;
        private readonly Type _landscapeGeneratorType;
        private readonly Type _worldListType;
        private readonly Type _initMapType;
        private readonly Type _blueprintPoolListType;
        private readonly Type _blueprintPoolType;
        private readonly FieldInfo _entityParametersField;
        private readonly FieldInfo _entityIdField;
        private readonly FieldInfo _factoryForEntityIdField;
        private readonly FieldInfo _nullableStringValueField;
        private readonly FieldInfo _nullableStringHasValueField;
        private readonly FieldInfo _entityRolesField;
        private readonly FieldInfo _entityControllerIdField;
        private readonly FieldInfo _prefabLocationField;
        private readonly FieldInfo _imageLocationField;
        private readonly FieldInfo _inactiveField;
        private readonly FieldInfo _allowedBlueprintField;
        private readonly FieldInfo _allowedStartingBlueprintField;
        private readonly FieldInfo _allowedForAiField;
        private readonly FieldInfo _allowedForDemoField;
        private readonly FieldInfo _isForSpecialistsField;
        private readonly FieldInfo _stageParametersField;
        private readonly FieldInfo _stageMapSizeField;
        private readonly FieldInfo _stageDemoField;
        private readonly FieldInfo _gameBalancingAssetField;
        private readonly FieldInfo _fallbackLandscapeGeneratorsField;
        private readonly FieldInfo _gameStageMapField;
        private readonly FieldInfo _stageMapLevelMapsField;
        private readonly FieldInfo _lastNativeAiStartBuildingDistanceField;
        private readonly PropertyInfo _stageMapCurrentStageProperty;
        private readonly PropertyInfo _currentProgressionProperty;
        private readonly FieldInfo _progressionEnableTutorialField;
        private readonly FieldInfo _progressionTutorialStageField;
        private readonly PropertyInfo _gameTutorialStateProperty;
        private readonly FieldInfo _tutorialMainFinishedField;
        private readonly FieldInfo _gameIsDemoField;
        private readonly FieldInfo _gameAtLeastOneBlueprintAddedByPlayerField;
        private readonly FieldInfo _gameMaxAscensionLevelField;
        private readonly FieldInfo[] _progressionFields;
        private readonly FieldInfo _progressionStagesField;
        private readonly FieldInfo _progressionAisField;
        private readonly FieldInfo _progressionLandscapesField;
        private readonly FieldInfo _stageParametersWorldListField;
        private readonly FieldInfo _stageParametersBlueprintPoolListField;
        private readonly FieldInfo _aiAllowedForDemoField;
        private readonly FieldInfo _aiStageRangeField;
        private readonly FieldInfo _aiAscensionRangeField;
        private readonly FieldInfo _aiRestrictedMapsField;
        private readonly FieldInfo _aiBaseStartingStageField;
        private readonly FieldInfo _aiBaseStartingAscensionField;
        private readonly FieldInfo _worldDeactivatedField;
        private readonly FieldInfo _worldAllowedForDemoField;
        private readonly FieldInfo _worldRestrictedMapsField;
        private readonly FieldInfo _landscapeMapSizeField;
        private readonly FieldInfo _landscapeRandomSeedField;
        private readonly FieldInfo _landscapeAlwaysUseSameSeedField;
        private readonly FieldInfo _landscapeNameLocaIdField;
        private readonly FieldInfo _landscapeDeactivatedField;
        private readonly FieldInfo _landscapeAllowedForDemoField;
        private readonly FieldInfo _landscapeRestrictedWorldsField;
        private readonly FieldInfo _entityIndexField;
        private readonly FieldInfo _nativeEntityRowsField;
        private readonly FieldInfo _aiDeactivatedField;
        private readonly FieldInfo _aiDeckEntryEntityIdField;
        private readonly FieldInfo _aiDeckEntryStartingInAscensionLevelField;
        private readonly FieldInfo _aiDeckEntryCountPerStageField;
        private readonly FieldInfo _aiBasesField;
        private readonly FieldInfo _aiBaseEntitiesInDeckField;
        private readonly FieldInfo _worldsField;
        private readonly FieldInfo _worldGeneratorField;
        private readonly FieldInfo _blueprintPoolsField;
        private readonly FieldInfo _blueprintPoolIdField;
        private readonly FieldInfo _blueprintPoolExclusiveField;
        private readonly FieldInfo _blueprintFilterField;
        private readonly MethodInfo _blueprintPoolBlueprintsMethod;

        private readonly List<object> _entityRows = new List<object>();
        private readonly List<ProgressionContentContribution> _contributions =
            new List<ProgressionContentContribution>();
        private readonly List<AssetBundle> _assetBundles = new List<AssetBundle>();
        private readonly List<FileStream> _assetBundleStreams = new List<FileStream>();
        private readonly List<GameObject> _nativePrefabClones = new List<GameObject>();
        private readonly List<ScriptableObject> _stageParameterClones = new List<ScriptableObject>();
        private readonly List<ScriptableObject> _landscapeClones = new List<ScriptableObject>();
        private readonly List<ScriptableObject> _blueprintPoolClones = new List<ScriptableObject>();
        private readonly List<ScriptableObject> _retainedRelicObjects = new List<ScriptableObject>();
        private readonly List<object> _blueprintPools = new List<object>();
        private readonly BlueprintPoolScopeRegistry _blueprintPoolScopeRegistry =
            new BlueprintPoolScopeRegistry();
        private readonly GlobalBundleRegistry _bundleRegistry = new GlobalBundleRegistry();
        private Task _loadingTask;
        private readonly Dictionary<string, GameObject> _prefabClones = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, UnityEngine.Object> _resourceAssets =
            new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
        private readonly Dictionary<string, LoadedAiProfile> _aiProfiles =
            new Dictionary<string, LoadedAiProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, LoadedAiProfile> _aiProfilesByResourceName =
            new Dictionary<string, LoadedAiProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, LoadedLandscape> _landscapesByLocalizationId =
            new Dictionary<string, LoadedLandscape>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<object, BoundAiRuntimeBehaviour> _boundAiBehaviours =
            new Dictionary<object, BoundAiRuntimeBehaviour>(ReferenceComparer.Instance);
        private readonly Dictionary<string, LoadedUnit> _units =
            new Dictionary<string, LoadedUnit>(StringComparer.Ordinal);
        private readonly Dictionary<string, ContentRelicDefinition> _relicDefinitions =
            new Dictionary<string, ContentRelicDefinition>(StringComparer.Ordinal);
        private readonly HashSet<string> _knownIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _nativeUnitIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _nativeFactoryIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _blueprintUnitIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _startingBlueprintUnitIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<LoadedStage> _stages = new List<LoadedStage>();
        private readonly List<PackCandidate> _packCandidates = new List<PackCandidate>();
        private readonly Queue<string> _recentLogs = new Queue<string>();
        private readonly object _logGate = new object();

        private long _loadedAssetBundleBytes;
        private string _resolvedPacksRoot;
        private string _packRootError;
        private bool _nativeRegistryReady;
        private bool _registryUnavailableLogged;
        private bool _runtimeContentReady;
        private bool _loaded;
        private bool _aiStartDistanceFailureLogged;
        private bool _blueprintRewardScopeFailureLogged;
        private bool _stageMapOrderFailureLogged;
        private object _stageMapSnapshotOwner;
        private List<object> _stageMapVisibleStages;
        private object _pendingStageMapSnapshotOwner;
        private string _pendingStageMapJson;

        public ContentPackRuntimeLoader(
            string packsRoot,
            Assembly gameAssembly,
            AiModuleRegistry moduleRegistry,
            Action<string> logger)
        {
            if (String.IsNullOrWhiteSpace(packsRoot))
            {
                throw new ArgumentException("A content packs directory is required.", "packsRoot");
            }

            _packsRoot = Path.GetFullPath(packsRoot);
            _gameAssembly = gameAssembly ?? throw new ArgumentNullException("gameAssembly");
            _relicRegistrar = new RelicContentRuntimeRegistrar(_gameAssembly);
            _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException("moduleRegistry");
            _log = logger;

            _entityRowType = FindUniqueType("EntityBalancingParameters");
            if (!_entityRowType.IsValueType)
            {
                throw new InvalidOperationException(
                    "EntityBalancingParameters must remain a value type so native-template unit rows can be copied before edits.");
            }

            _entityBalanceAssetType = FindUniqueType("EntityBalancingScriptableObject");
            _entityControllerType = FindUniqueType("EntityController");
            Type locaType = FindUniqueType("Loca");
            _localizationLanguageProperty = locaType.GetProperty("Language", BindingFlags.Public | BindingFlags.Static);
            if (_localizationLanguageProperty == null || _localizationLanguageProperty.PropertyType != typeof(string) ||
                _localizationLanguageProperty.GetGetMethod() == null ||
                !_localizationLanguageProperty.GetGetMethod().IsStatic)
            {
                throw new InvalidOperationException("Loca.Language must be a public static string property.");
            }

            _blueprintNameMethod = RuntimeReflection.FindUniqueMethod(locaType, "BlueprintName",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.IsStatic && method.ReturnType == typeof(string) && parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(string);
                });
            _blueprintDescriptionMethod = RuntimeReflection.FindUniqueMethod(locaType, "BlueprintDescription",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.IsStatic && method.ReturnType == typeof(string) && parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(string);
                });
            _skillDescriptionMethod = RuntimeReflection.FindUniqueMethod(locaType, "SkillDescription",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.IsStatic && method.ReturnType == typeof(string) && parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(string);
                });
            _stageType = FindUniqueType("Stage");
            _stageParametersType = FindUniqueType("StageParametersScriptableObject");
            Type gameBalancingType = FindUniqueType("GameBalancingScriptableObject");
            Type progressionType = FindUniqueType("LevelProgressionParametersScriptableObject");
            Type landscapeGeneratorType = FindUniqueType("LandscapeGenerator");
            _landscapeGeneratorType = landscapeGeneratorType;
            _aiBehaviourType = FindUniqueType("AiBehaviour");
            _defaultAiBehaviourType = FindUniqueType("DefaultAiBehaviour");
            _aiDeckEntryType = FindUniqueType("EntityInAiDeck");
            _worldListType = FindUniqueType("WorldList");
            _initMapType = FindUniqueType("InitMap");
            _blueprintPoolListType = FindUniqueType("BlueprintPoolList");
            _blueprintPoolType = FindUniqueType("BlueprintPool");

            _entityParametersField = FindField(_entityBalanceAssetType, "parameters", InstanceFields);
            if (!IsExactList(_entityParametersField.FieldType, _entityRowType))
            {
                throw new InvalidOperationException(
                    "EntityBalancingScriptableObject.parameters is not List<EntityBalancingParameters>.");
            }

            _entityIdField = RequireField(_entityRowType, "entityId", typeof(string));
            _factoryForEntityIdField = FindField(_entityRowType, "factoryForEntityId", InstanceFields);
            Type nullableStringType = _factoryForEntityIdField.FieldType;
            if (_factoryForEntityIdField.IsStatic || !nullableStringType.IsValueType ||
                _factoryForEntityIdField.IsInitOnly ||
                !String.Equals(nullableStringType.Name, "NullableString", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "EntityBalancingParameters.factoryForEntityId is not the verified NullableString value type.");
            }
            _nullableStringValueField = RequireField(nullableStringType, "value", typeof(string));
            _nullableStringHasValueField = RequireField(nullableStringType, "hasValue", typeof(bool));
            _entityRolesField = FindField(_entityRowType, "roles", InstanceFields);
            if (_entityRolesField.IsStatic || !_entityRolesField.FieldType.IsEnum ||
                !EnumHasName(_entityRolesField.FieldType, "Factory") ||
                !EnumHasName(_entityRolesField.FieldType, "Building"))
            {
                throw new InvalidOperationException(
                    "EntityBalancingParameters.roles must remain an enum containing Building and Factory.");
            }
            _entityControllerIdField = RequireField(_entityControllerType, "entityId", typeof(string));
            _prefabLocationField = RequireField(_entityRowType, "prefabLocation", typeof(string));
            _imageLocationField = RequireField(_entityRowType, "imageLocation", typeof(string));
            _inactiveField = RequireField(_entityRowType, "inactive", typeof(bool));
            _allowedBlueprintField = RequireField(_entityRowType, "isAllowedAsBlueprint", typeof(bool));
            _allowedStartingBlueprintField = RequireField(_entityRowType, "isAllowedAsStartingBlueprint", typeof(bool));
            _allowedForAiField = RequireField(_entityRowType, "isAllowedForAi", typeof(bool));
            _allowedForDemoField = RequireField(_entityRowType, "isAllowedForDemo", typeof(bool));
            _isForSpecialistsField = RequireField(_entityRowType, "isForSpecialists", typeof(bool));
            _stageParametersField = RequireField(_stageType, "stageParameters", _stageParametersType);
            _stageMapSizeField = RequireField(_stageType, "mapSizeRange", typeof(Vector2Int));
            _stageDemoField = RequireField(_stageType, "isPartOfDemo", typeof(bool));
            Type gameBalancingStoreType = FindUniqueType("GameBalancingStore");
            // The game also ships Shapes2D.Game; resolve the verified global type exactly.
            Type gameType = _gameAssembly.GetType("Game", true, false);
            Type stageMapType = FindUniqueType("StageMap");
            Type levelMapType = FindUniqueType("LevelMap");
            _gameBalancingAssetField = FindField(gameBalancingStoreType,
                "_gameBalancingScriptableObject", StaticFields);
            // Game.AtLeastOneBlueprintHasBeenAddedByPlayer is a static field;
            // RequireField defaults to instance fields for serialized data rows.
            _gameAtLeastOneBlueprintAddedByPlayerField = FindField(
                gameType, "AtLeastOneBlueprintHasBeenAddedByPlayer", StaticFields);
            if (_gameAtLeastOneBlueprintAddedByPlayerField.FieldType != typeof(bool) ||
                !_gameAtLeastOneBlueprintAddedByPlayerField.IsStatic ||
                _gameAtLeastOneBlueprintAddedByPlayerField.IsInitOnly)
            {
                throw new InvalidOperationException(
                    "Game.AtLeastOneBlueprintHasBeenAddedByPlayer must remain a static bool.");
            }
            _gameStageMapField = FindField(gameType, "StageMap", StaticFields);
            if (!_gameStageMapField.IsStatic || _gameStageMapField.FieldType != stageMapType)
            {
                throw new InvalidOperationException("Game.StageMap has an unexpected field shape.");
            }
            _stageMapLevelMapsField = FindField(stageMapType, "_levelMaps", InstanceFields);
            if (!IsExactList(_stageMapLevelMapsField.FieldType, levelMapType))
            {
                throw new InvalidOperationException("StageMap._levelMaps is not List<LevelMap>.");
            }

            _stageMapCurrentStageProperty = stageMapType.GetProperty("CurrentStage", InstanceFields);
            MethodInfo currentStageGetter = _stageMapCurrentStageProperty == null
                ? null
                : _stageMapCurrentStageProperty.GetGetMethod(true);
            if (_stageMapCurrentStageProperty == null || _stageMapCurrentStageProperty.PropertyType != typeof(int) ||
                currentStageGetter == null || currentStageGetter.IsStatic ||
                currentStageGetter.GetParameters().Length != 0)
            {
                throw new InvalidOperationException("StageMap.CurrentStage has an unexpected property shape.");
            }

            _fallbackLandscapeGeneratorsField = RequireField(gameBalancingType,
                "fallbackLandscapeGenerators", typeof(List<>).MakeGenericType(_landscapeGeneratorType));
            _lastNativeAiStartBuildingDistanceField = RequireField(gameBalancingType,
                "minDistanceOfOutlyingStartAiBuildingsToPlayerStartStage789", typeof(int));
            _currentProgressionProperty = gameBalancingStoreType.GetProperty(
                "CurrentLevelProgressionParameters", StaticFields);
            if (_currentProgressionProperty == null || _currentProgressionProperty.PropertyType != progressionType ||
                _currentProgressionProperty.GetGetMethod(true) == null ||
                !_currentProgressionProperty.GetGetMethod(true).IsStatic)
            {
                throw new InvalidOperationException("GameBalancingStore.CurrentLevelProgressionParameters has an unexpected shape.");
            }
            _progressionEnableTutorialField = RequireField(progressionType, "enableTutorial", typeof(bool));
            _progressionTutorialStageField = RequireField(progressionType, "tutorialStage", _stageType);
            Type tutorialStateType = FindUniqueType("TutorialState");
            _gameTutorialStateProperty = gameType.GetProperty("TutorialState", StaticFields);
            MethodInfo tutorialStateGetter = _gameTutorialStateProperty == null
                ? null
                : _gameTutorialStateProperty.GetGetMethod(true);
            if (_gameTutorialStateProperty == null || _gameTutorialStateProperty.PropertyType != tutorialStateType ||
                tutorialStateGetter == null || !tutorialStateGetter.IsStatic ||
                tutorialStateGetter.GetParameters().Length != 0)
            {
                throw new InvalidOperationException("Game.TutorialState has an unexpected property shape.");
            }
            _tutorialMainFinishedField = RequireField(tutorialStateType, "IsMainTutorialFinished", typeof(bool));
            if (!_gameBalancingAssetField.IsStatic || _gameBalancingAssetField.FieldType != gameBalancingType)
            {
                throw new InvalidOperationException(
                    "GameBalancingStore._gameBalancingScriptableObject has an unexpected shape.");
            }
            _gameIsDemoField = RequireField(gameBalancingType, "isDemo", typeof(bool));
            _gameMaxAscensionLevelField = RequireField(gameBalancingType, "maxAscensionLevel", typeof(int));

            string[] progressionFieldNames =
            {
                "levelProgressionParameters",
                "levelProgressionParametersHeat",
                "levelProgressionParametersFirstRun"
            };
            _progressionFields = new FieldInfo[progressionFieldNames.Length];
            for (int progressionIndex = 0; progressionIndex < progressionFieldNames.Length; progressionIndex++)
            {
                _progressionFields[progressionIndex] = RequireField(gameBalancingType,
                    progressionFieldNames[progressionIndex], progressionType);
            }

            _progressionStagesField = FindField(progressionType, "stages", InstanceFields);
            _progressionAisField = FindField(progressionType, "defaultAis", InstanceFields);
            _progressionLandscapesField = FindField(progressionType, "defaultLandscapeGenerators", InstanceFields);
            if (!IsExactList(_progressionStagesField.FieldType, _stageType) ||
                !IsExactList(_progressionAisField.FieldType, _defaultAiBehaviourType) ||
                !IsExactList(_progressionLandscapesField.FieldType, landscapeGeneratorType))
            {
                throw new InvalidOperationException(
                    "Level progression stage, AI, or LandscapeGenerator list has an unexpected shape.");
            }

            _stageParametersWorldListField = FindField(_stageParametersType, "worldList", InstanceFields);
            if (_stageParametersWorldListField.FieldType != _worldListType)
            {
                throw new InvalidOperationException(
                    "StageParametersScriptableObject.worldList is not WorldList.");
            }
            _stageParametersBlueprintPoolListField = RequireField(
                _stageParametersType, "blueprintPoolList", _blueprintPoolListType);

            _aiDeactivatedField = RequireField(_aiBehaviourType, "isDeactivated", typeof(bool));
            _aiAllowedForDemoField = RequireField(_aiBehaviourType, "isAllowedForDemo", typeof(bool));
            _aiStageRangeField = RequireField(_aiBehaviourType, "stageRange", typeof(Vector2Int));
            _aiAscensionRangeField = RequireField(_aiBehaviourType, "ascensionRange", typeof(Vector2Int));
            _aiRestrictedMapsField = FindField(_aiBehaviourType, "restrictedToMaps", InstanceFields);
            if (!IsExactList(_aiRestrictedMapsField.FieldType, landscapeGeneratorType))
            {
                throw new InvalidOperationException("AiBehaviour.restrictedToMaps is not List<LandscapeGenerator>.");
            }
            _aiDeckEntryEntityIdField = RequireField(_aiDeckEntryType, "entityId", typeof(string));
            _aiDeckEntryStartingInAscensionLevelField = RequireField(
                _aiDeckEntryType, "startingInAscensionLevel", typeof(int));
            _aiDeckEntryCountPerStageField = FindField(_aiDeckEntryType, "countPerStage", InstanceFields);
            if (!IsExactList(_aiDeckEntryCountPerStageField.FieldType, typeof(Vector2Int)))
            {
                throw new InvalidOperationException("EntityInAiDeck.countPerStage is not List<Vector2Int>.");
            }
            _aiBasesField = FindField(_aiBehaviourType, "aiBases", InstanceFields);
            if (!IsExactList(_aiBasesField.FieldType, FindUniqueType("AiBase")))
            {
                throw new InvalidOperationException("AiBehaviour.aiBases is not List<AiBase>.");
            }
            _aiBaseType = _aiBasesField.FieldType.GetGenericArguments()[0];
            _aiBaseEntitiesInDeckField = FindField(_aiBaseType, "entitiesInAiDeck", InstanceFields);
            if (!IsExactList(_aiBaseEntitiesInDeckField.FieldType, _aiDeckEntryType))
            {
                throw new InvalidOperationException("AiBase.entitiesInAiDeck is not List<EntityInAiDeck>.");
            }

            _worldsField = FindField(_worldListType, "worlds", InstanceFields);
            if (!IsExactList(_worldsField.FieldType, _initMapType))
            {
                throw new InvalidOperationException("WorldList.worlds is not List<InitMap>.");
            }
            _worldGeneratorField = FindField(_initMapType, "worldGenerator", InstanceFields);
            _worldDeactivatedField = RequireField(_initMapType, "isDeactivated", typeof(bool));
            _worldAllowedForDemoField = RequireField(_initMapType, "isAllowedForDemo", typeof(bool));
            _worldRestrictedMapsField = FindField(_initMapType, "restrictedToMaps", InstanceFields);
            if (!IsExactList(_worldRestrictedMapsField.FieldType, landscapeGeneratorType))
            {
                throw new InvalidOperationException("InitMap.restrictedToMaps is not List<LandscapeGenerator>.");
            }
            _landscapeMapSizeField = RequireField(landscapeGeneratorType, "mapSize", typeof(int));
            _landscapeRandomSeedField = RequireField(landscapeGeneratorType, "randomSeed", typeof(int));
            _landscapeAlwaysUseSameSeedField = RequireField(landscapeGeneratorType,
                "alwaysUseSameSeed", typeof(bool));
            _landscapeNameLocaIdField = RequireField(landscapeGeneratorType, "nameLocaId", typeof(string));
            _landscapeDeactivatedField = RequireField(landscapeGeneratorType, "isDeactivated", typeof(bool));
            _landscapeAllowedForDemoField = RequireField(landscapeGeneratorType,
                "isAllowedForDemo", typeof(bool));
            _landscapeRestrictedWorldsField = FindField(landscapeGeneratorType,
                "restrictedToWorlds", InstanceFields);
            if (!IsExactList(_landscapeRestrictedWorldsField.FieldType, _initMapType))
            {
                throw new InvalidOperationException(
                    "LandscapeGenerator.restrictedToWorlds is not List<InitMap>.");
            }
            _aiBaseStartingStageField = RequireField(_aiBaseType, "startingInStage", typeof(int));
            _aiBaseStartingAscensionField = RequireField(_aiBaseType,
                "startingInAscensionLevel", typeof(int));
            _blueprintPoolsField = FindField(_blueprintPoolListType, "blueprintPools", InstanceFields);
            if (!IsExactList(_blueprintPoolsField.FieldType, _blueprintPoolType))
            {
                throw new InvalidOperationException("BlueprintPoolList.blueprintPools is not List<BlueprintPool>.");
            }
            _blueprintPoolIdField = RequireField(_blueprintPoolType, "poolId", typeof(string));
            _blueprintPoolExclusiveField = RequireField(
                _blueprintPoolType, "blueprintsAreExclusive", typeof(bool));
            _blueprintFilterField = FindField(_blueprintPoolType, "blueprintFilter", InstanceFields);
            if (!typeof(ScriptableObject).IsAssignableFrom(_blueprintPoolListType) ||
                !typeof(ScriptableObject).IsAssignableFrom(_blueprintPoolType))
            {
                throw new InvalidOperationException(
                    "BlueprintPoolList and BlueprintPool must remain ScriptableObjects to create stage-scoped clones.");
            }
            _blueprintPoolBlueprintsMethod = RuntimeReflection.FindUniqueMethod(
                _blueprintPoolType, "BlueprintsInPool", delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return !method.IsStatic && parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(int?) &&
                        parameters[1].ParameterType == typeof(bool) &&
                        method.ReturnType == typeof(List<string>);
                });

            Type entityStoreType = FindUniqueType("EntityBalancingStore");
            _entityIndexField = FindField(entityStoreType, "ParameterListIndexOf", StaticFields);
            if (!_entityIndexField.IsStatic || _entityIndexField.FieldType != typeof(Dictionary<string, int>))
            {
                throw new InvalidOperationException(
                    "EntityBalancingStore.ParameterListIndexOf is not Dictionary<string, int>.");
            }

            _nativeEntityRowsField = FindField(entityStoreType, "EntityBalancingParametersList", StaticFields);
            if (!_nativeEntityRowsField.IsStatic || !IsExactList(_nativeEntityRowsField.FieldType, _entityRowType))
            {
                throw new InvalidOperationException(
                    "EntityBalancingStore.EntityBalancingParametersList is not List<EntityBalancingParameters>.");
            }
        }

        public string TargetGameVersion { get { return "1.0.9"; } }

        public void OnEntityRegistryInitialized()
        {
            lock (_gate)
            {
                if (_nativeRegistryReady)
                {
                    return;
                }

                _nativeRegistryReady = true;
                RefreshNativeUnitIds();
            }
        }

        public bool PrepareContentForRuntime()
        {
            lock (_gate)
            {
                if (_runtimeContentReady)
                {
                    return true;
                }

                RefreshNativeUnitIds();
                if (!_nativeRegistryReady)
                {
                    return false;
                }

                if (_loadingTask == null)
                {
                    _loadingTask = LoadAllPacksAsync();
                }

                if (_loadingTask.IsCompleted)
                {
                    try
                    {
                        _loadingTask.GetAwaiter().GetResult();
                        ApplyBlueprintEligibility();
                        _runtimeContentReady = _loaded;
                    }
                    catch (Exception exception)
                    {
                        Log("Critical error during async content loading: " + exception.Message);
                        _runtimeContentReady = false;
                    }
                }

                return _runtimeContentReady;
            }
        }

        public IEnumerable<object> GetEntityParameterRows()
        {
            lock (_gate)
            {
                return _runtimeContentReady ? _entityRows.ToArray() : new object[0];
            }
        }

        public bool TryGetUnitLocalization(string unitId, bool description, out string value)
        {
            lock (_gate)
            {
                LoadedUnit unit;
                if (String.IsNullOrWhiteSpace(unitId) || !_units.TryGetValue(unitId, out unit))
                {
                    value = null;
                    return false;
                }

                string language = null;
                try
                {
                    language = _localizationLanguageProperty.GetValue(null, null) as string;
                }
                catch (Exception exception)
                {
                    Log("No se pudo leer Loca.Language para la unidad " + unitId + ": " +
                        exception.GetBaseException().Message);
                }

                if (String.IsNullOrWhiteSpace(language))
                {
                    language = "en-US";
                }

                return UnitLocalizationResolver.TryResolve(unit.Definition, language, description,
                    delegate(string templateId, bool lookupDescription)
                    {
                        MethodInfo lookup = lookupDescription
                            ? _blueprintDescriptionMethod
                            : _blueprintNameMethod;
                        try
                        {
                            return lookup.Invoke(null, new object[] { templateId }) as string;
                        }
                        catch (Exception exception)
                        {
                            Log("No se pudo heredar la localización de " + templateId + " para " + unitId + ": " +
                                exception.GetBaseException().Message);
                            return null;
                        }
                    }, out value);
            }
        }

        public bool TryGetUnitSkillDescription(string unitId, out string value)
        {
            lock (_gate)
            {
                LoadedUnit unit;
                if (String.IsNullOrWhiteSpace(unitId) || !_units.TryGetValue(unitId, out unit))
                {
                    value = null;
                    return false;
                }

                string language = null;
                try
                {
                    language = _localizationLanguageProperty.GetValue(null, null) as string;
                }
                catch (Exception exception)
                {
                    Log("No se pudo leer Loca.Language para la habilidad de la unidad " + unitId + ": " +
                        exception.GetBaseException().Message);
                }

                if (String.IsNullOrWhiteSpace(language))
                {
                    language = "en-US";
                }

                return UnitLocalizationResolver.TryResolveSkillDescription(unit.Definition, language,
                    delegate(string templateId)
                    {
                        try
                        {
                            return _skillDescriptionMethod.Invoke(null, new object[] { templateId }) as string;
                        }
                        catch (Exception exception)
                        {
                            Log("No se pudo heredar la descripción de habilidad de " + templateId + " para " +
                                unitId + ": " + exception.GetBaseException().Message);
                            return null;
                        }
                    }, out value);
            }
        }

        public bool TryGetRelicLocalization(string relicId, bool description, out string value)
        {
            value = null;
            lock (_gate)
            {
                ContentRelicDefinition relic;
                if (String.IsNullOrWhiteSpace(relicId) || !_relicDefinitions.TryGetValue(relicId, out relic))
                {
                    return false;
                }

                string language = _localizationLanguageProperty.GetValue(null, null) as string;
                if (String.IsNullOrWhiteSpace(language))
                {
                    language = "en-US";
                }

                return RelicLocalizationResolver.TryResolve(relic, language, description, out value);
            }
        }

        public void OnEntityRowsRegistered()
        {
            lock (_gate)
            {
                // Cache filtered mod IDs once per pool so ExclusiveBlueprints does not rescan the
                // complete entity registry every time a random card list is assembled.
                for (int i = 0; i < _blueprintPools.Count; i++)
                {
                    object pool = _blueprintPools[i];
                    if (pool == null || !_blueprintPoolScopeRegistry.IsExclusivePool(pool))
                    {
                        continue;
                    }

                    try
                    {
                        object value = _blueprintPoolBlueprintsMethod.Invoke(pool,
                            new object[] { null, false });
                        _blueprintPoolScopeRegistry.SetEligibleExclusiveUnitIds(
                            pool, value as List<string>);
                    }
                    catch (Exception exception)
                    {
                        _blueprintPoolScopeRegistry.SetEligibleExclusiveUnitIds(pool, null);
                        Log("No se pudo calcular la elegibilidad exclusiva de un BlueprintPool: " +
                            exception.GetBaseException().Message);
                    }
                }

                for (int i = 0; i < _blueprintPools.Count; i++)
                {
                    object pool = _blueprintPools[i];
                    if (pool == null)
                    {
                        continue;
                    }

                    try
                    {
                        MethodInfo refresh = pool.GetType().GetMethod("OnBlueprintFilterChanged", InstanceFields);
                        if (refresh == null || refresh.GetParameters().Length != 0 || refresh.ReturnType != typeof(void))
                        {
                            throw new MissingMethodException(pool.GetType().FullName,
                                "OnBlueprintFilterChanged(): void");
                        }

                        refresh.Invoke(pool, null);
                    }
                    catch (Exception exception)
                    {
                        Log("No se pudo recalcular un BlueprintPool de contenido: " + exception.GetBaseException().Message);
                    }
                }
            }
        }

        public bool TryFindBlueprintPool(string poolId, out object pool)
        {
            lock (_gate)
            {
                return _blueprintPoolScopeRegistry.TryFind(poolId, out pool);
            }
        }

        public bool TryGetBlueprintPoolLocalizationId(string poolId, out string sourcePoolId)
        {
            return _blueprintPoolScopeRegistry.TryGetSourcePoolLocalizationId(poolId, out sourcePoolId);
        }

        public bool TryGetBlueprintPoolId(object pool, out string poolId)
        {
            return _blueprintPoolScopeRegistry.TryGetPoolId(pool, out poolId);
        }

        public object GetFallbackLandscapeSnapshot(object nativeFallbacks)
        {
            lock (_gate)
            {
                object progression = _currentProgressionProperty.GetValue(null, null);
                IList stages = progression == null ? null : _progressionStagesField.GetValue(progression) as IList;
                return StageFallbackSnapshot.Build(nativeFallbacks as IList, stages, delegate(object stage)
                {
                    for (int i = 0; i < _stages.Count; i++)
                    {
                        if (System.Object.ReferenceEquals(_stages[i].RuntimeStage, stage))
                        {
                            return _stages[i].FallbackLandscape;
                        }
                    }

                    return null;
                });
            }
        }

        public bool TryGetOutlyingStartAiBuildingsDistance(out int distance)
        {
            distance = 0;
            try
            {
                lock (_gate)
                {
                    if (!_runtimeContentReady || _stages.Count == 0)
                    {
                        return false;
                    }

                    object stageMap = _gameStageMapField.GetValue(null);
                    if (stageMap == null)
                    {
                        return false;
                    }

                    int stageIndex = (int)_stageMapCurrentStageProperty.GetValue(stageMap, null);
                    if (stageIndex < StageOwnedAiPlacementDistanceResolver.FirstAppendedStageIndex)
                    {
                        return false;
                    }

                    object progression = _currentProgressionProperty.GetValue(null, null);
                    IList stages = progression == null ? null : _progressionStagesField.GetValue(progression) as IList;
                    if (stages == null || stageIndex >= stages.Count || stages[stageIndex] == null)
                    {
                        return false;
                    }

                    object balancing = _gameBalancingAssetField.GetValue(null);
                    if (balancing == null)
                    {
                        return false;
                    }

                    int finalNativeBandDistance =
                        (int)_lastNativeAiStartBuildingDistanceField.GetValue(balancing);
                    return StageOwnedAiPlacementDistanceResolver.TryResolve(
                        stageIndex,
                        stages[stageIndex],
                        _stages,
                        delegate(LoadedStage loadedStage) { return loadedStage.RuntimeStage; },
                        finalNativeBandDistance,
                        out distance);
                }
            }
            catch (Exception exception)
            {
                bool shouldLog;
                lock (_gate)
                {
                    shouldLog = !_aiStartDistanceFailureLogged;
                    _aiStartDistanceFailureLogged = true;
                }

                if (shouldLog)
                {
                    Log("Could not inspect active progression for appended-stage AI building spacing: " +
                        exception.GetBaseException().Message);
                }

                return false;
            }
        }

        public bool TryGetBlueprintPoolDecision(object pool, string unitId, out bool runOriginal)
        {
            lock (_gate)
            {
                LoadedUnit unit;
                if (String.IsNullOrEmpty(unitId) || !_units.TryGetValue(unitId, out unit))
                {
                    runOriginal = true;
                    return false;
                }

                object balancing = _gameBalancingAssetField.GetValue(null);
                bool isDemo = balancing != null && (bool)_gameIsDemoField.GetValue(balancing);
                bool isAllowedForDemo = !isDemo || (bool)_allowedForDemoField.GetValue(unit.BalanceRow);
                bool isStartingBlueprintSelection =
                    !(bool)_gameAtLeastOneBlueprintAddedByPlayerField.GetValue(null);
                bool isAllowedAsStartingBlueprint =
                    (bool)_allowedStartingBlueprintField.GetValue(unit.BalanceRow);
                if (unit.IsProductionBuilding && _blueprintUnitIds.Contains(unitId) &&
                    isStartingBlueprintSelection &&
                    isAllowedAsStartingBlueprint && isAllowedForDemo &&
                    IsNativeInitialStagePool(pool))
                {
                    // Keep the native pool's own BlueprintFilter.IsBlueprintValid check. This
                    // only lets an explicitly starting-eligible factory reach that filter for
                    // the first offer in the first native stage.
                    runOriginal = true;
                    return true;
                }

                runOriginal = _blueprintPoolScopeRegistry.AllowsModUnitInPool(
                    pool, unitId, isDemo, isAllowedForDemo,
                    isStartingBlueprintSelection, isAllowedAsStartingBlueprint);
                return true;
            }
        }

        private void AddInitialStageStartingFactories(HashSet<string> destination)
        {
            if (destination == null)
            {
                return;
            }

            object balancing = _gameBalancingAssetField.GetValue(null);
            bool isDemo = balancing != null && (bool)_gameIsDemoField.GetValue(balancing);
            foreach (LoadedUnit product in _units.Values)
            {
                if (product.IsProductionBuilding || product.Definition == null ||
                    !product.Definition.isAllowedAsStartingBlueprint ||
                    String.IsNullOrWhiteSpace(product.ProductionFactoryId) ||
                    !_blueprintUnitIds.Contains(product.ProductionFactoryId))
                {
                    continue;
                }

                LoadedUnit factory;
                if (!_units.TryGetValue(product.ProductionFactoryId, out factory) ||
                    !factory.IsProductionBuilding ||
                    (isDemo && !(bool)_allowedForDemoField.GetValue(factory.BalanceRow)))
                {
                    continue;
                }

                destination.Add(factory.Id);
            }
        }

        private bool IsNativeInitialStagePool(object pool)
        {
            object stageMap = _gameStageMapField.GetValue(null);
            if (pool == null || stageMap == null ||
                !System.Object.ReferenceEquals(stageMap, _stageMapSnapshotOwner) ||
                _stageMapVisibleStages == null ||
                (int)_stageMapCurrentStageProperty.GetValue(stageMap, null) != 0)
            {
                return false;
            }

            object activeStage = BlueprintRewardStageResolver.ResolveVisibleStage(
                _stageMapVisibleStages, 0);
            if (activeStage == null)
            {
                return false;
            }

            for (int i = 0; i < _stages.Count; i++)
            {
                if (System.Object.ReferenceEquals(_stages[i].RuntimeStage, activeStage))
                {
                    return false;
                }
            }

            UnityEngine.Object stageParameters =
                _stageParametersField.GetValue(activeStage) as UnityEngine.Object;
            if (stageParameters == null)
            {
                return false;
            }

            UnityEngine.Object poolList =
                _stageParametersBlueprintPoolListField.GetValue(stageParameters) as UnityEngine.Object;
            IList pools = poolList == null ? null : _blueprintPoolsField.GetValue(poolList) as IList;
            if (pools == null)
            {
                return false;
            }

            for (int i = 0; i < pools.Count; i++)
            {
                if (System.Object.ReferenceEquals(pools[i], pool))
                {
                    return true;
                }
            }

            return false;
        }

        public void AddExclusiveBlueprints(string otherThanPoolId, HashSet<string> result)
        {
            if (result == null)
            {
                return;
            }

            lock (_gate)
            {
                _blueprintPoolScopeRegistry.AddExclusiveModUnits(otherThanPoolId, result);
            }
        }

        public bool TryGetBlueprintRewardFactoryScope(out BlueprintRewardFactoryScope scope)
        {
            lock (_gate)
            {
                scope = null;
                if (_blueprintUnitIds.Count == 0)
                {
                    return false;
                }

                var stageFactoryIds = new HashSet<string>(StringComparer.Ordinal);
                var factoryByProductId = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (LoadedUnit unit in _units.Values)
                {
                    if (!unit.IsProductionBuilding && !String.IsNullOrWhiteSpace(unit.ProductionFactoryId))
                    {
                        factoryByProductId[unit.Id] = unit.ProductionFactoryId;
                    }
                }

                bool activeStageResolved = false;
                bool activeStageIsPackOwned = false;
                int activeStageIndex = -1;
                string activeStageId = null;
                try
                {
                    if (!_runtimeContentReady)
                    {
                        scope = new BlueprintRewardFactoryScope(
                            _blueprintUnitIds, null, factoryByProductId, activeStageIndex, activeStageId);
                        return true;
                    }

                    object stageMap = _gameStageMapField.GetValue(null);
                    if (System.Object.ReferenceEquals(stageMap, _pendingStageMapSnapshotOwner))
                    {
                        TryRestorePendingStageMapOrder(false);
                    }

                    object activeStage = null;
                    if (stageMap != null && System.Object.ReferenceEquals(stageMap, _stageMapSnapshotOwner) &&
                        _stageMapVisibleStages != null)
                    {
                        activeStageIndex = (int)_stageMapCurrentStageProperty.GetValue(stageMap, null);
                        activeStage = BlueprintRewardStageResolver.ResolveVisibleStage(
                            _stageMapVisibleStages, activeStageIndex);
                    }

                    if (activeStage != null)
                    {
                        activeStageResolved = true;
                        UnityEngine.Object stageAsset = _stageParametersField.GetValue(activeStage) as UnityEngine.Object;
                        if (stageAsset != null && !String.IsNullOrWhiteSpace(stageAsset.name))
                        {
                            activeStageId = "native:" + stageAsset.name;
                        }

                        for (int i = 0; i < _stages.Count; i++)
                        {
                            if (System.Object.ReferenceEquals(_stages[i].RuntimeStage, activeStage))
                            {
                                stageFactoryIds.UnionWith(_stages[i].BlueprintFactoryIds);
                                activeStageId = _stages[i].PackId + ":" + _stages[i].Definition.id;
                                activeStageIsPackOwned = true;
                                break;
                            }
                        }

                        bool isStartingBlueprintSelection =
                            !(bool)_gameAtLeastOneBlueprintAddedByPlayerField.GetValue(null);
                        if (activeStageIndex == 0 && !activeStageIsPackOwned &&
                            isStartingBlueprintSelection)
                        {
                            AddInitialStageStartingFactories(stageFactoryIds);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log("Could not resolve the active progression stage for factory rewards: " +
                        exception.GetBaseException().Message);
                }

                if (!activeStageResolved && !_blueprintRewardScopeFailureLogged)
                {
                    _blueprintRewardScopeFailureLogged = true;
                    Log("Active progression stage could not be resolved; custom factories are omitted from " +
                        "the generic reward fallback until a stage is available.");
                }

                scope = new BlueprintRewardFactoryScope(
                    _blueprintUnitIds, stageFactoryIds, factoryByProductId, activeStageIndex, activeStageId);
                return true;
            }
        }

        public void OnStageMapCreated(object stageMap)
        {
            lock (_gate)
            {
                _pendingStageMapSnapshotOwner = null;
                _pendingStageMapJson = null;
                _stageMapSnapshotOwner = stageMap;
                _stageMapVisibleStages = null;
                List<object> visibleStages;
                if (stageMap == null || !TryBuildVisibleStageSequence(out visibleStages))
                {
                    return;
                }

                IList levelMaps = _stageMapLevelMapsField.GetValue(stageMap) as IList;
                if (levelMaps == null || levelMaps.Count != visibleStages.Count)
                {
                    LogStageMapOrderFailure("Stage map snapshot did not match the game's visible LevelMap count; " +
                        "custom factories will be omitted from generic rewards for this map.");
                    return;
                }

                _stageMapVisibleStages = visibleStages;
            }
        }

        public void OnStageMapDeserialized(object stageMap, string json)
        {
            lock (_gate)
            {
                _stageMapSnapshotOwner = stageMap;
                _stageMapVisibleStages = null;
                if (stageMap == null)
                {
                    _pendingStageMapSnapshotOwner = null;
                    _pendingStageMapJson = null;
                    return;
                }

                _pendingStageMapSnapshotOwner = stageMap;
                _pendingStageMapJson = json;
            }

            // Save loading can run before the first map/entity hook. Prepare content now when the
            // native registry is available; if it is not, the pending restore is retried after the
            // progression hooks apply their contributions and before reward eligibility is used.
            PrepareContentForRuntime();
            lock (_gate) TryRestorePendingStageMapOrder(false);
        }

        public void OnProgressionContentApplied()
        {
            lock (_gate) TryRestorePendingStageMapOrder(true);
        }

        public string AppendStageMapOrder(object stageMap, string json)
        {
            lock (_gate)
            {
                if (json == null || stageMap == null ||
                    !System.Object.ReferenceEquals(stageMap, _stageMapSnapshotOwner))
                {
                    return json;
                }

                TryRestorePendingStageMapOrder(false);

                List<string> stageTokens;
                string updatedJson;
                if (_stageMapVisibleStages != null)
                {
                    if (TryCreateStageOrderTokens(_stageMapVisibleStages, out stageTokens) &&
                        BlueprintRewardStageOrder.TryAppendSerializedOrder(json, stageTokens, out updatedJson))
                    {
                        return updatedJson;
                    }

                    LogStageMapOrderFailure("Could not persist the stage identity sequence; a resumed map will " +
                        "omit custom factories from generic rewards.");
                    return json;
                }

                // Preserve a valid marker while content/progression initialization is deferred.
                // Otherwise a quick save during load would erase the order needed on the next run.
                if (_stageMapVisibleStages == null &&
                    System.Object.ReferenceEquals(stageMap, _pendingStageMapSnapshotOwner) &&
                    BlueprintRewardStageOrder.TryReadSerializedOrder(_pendingStageMapJson, out stageTokens))
                {
                    if (BlueprintRewardStageOrder.TryAppendSerializedOrder(json, stageTokens, out updatedJson))
                    {
                        return updatedJson;
                    }

                    LogStageMapOrderFailure("Could not preserve the pending stage identity sequence; a resumed map will " +
                        "omit custom factories from generic rewards.");
                }

                return json;
            }
        }

        private bool TryBuildVisibleStageSequence(out List<object> visibleStages)
        {
            visibleStages = null;
            object progression = _currentProgressionProperty.GetValue(null, null);
            IList progressionStages = progression == null
                ? null
                : _progressionStagesField.GetValue(progression) as IList;
            if (progression == null || progressionStages == null)
            {
                return false;
            }

            bool enableTutorial = (bool)_progressionEnableTutorialField.GetValue(progression);
            object tutorialState = _gameTutorialStateProperty.GetValue(null, null);
            bool tutorialFinished = tutorialState != null &&
                (bool)_tutorialMainFinishedField.GetValue(tutorialState);
            bool useTutorialStage = enableTutorial && !tutorialFinished;
            object tutorialStage = _progressionTutorialStageField.GetValue(progression);
            object balancing = _gameBalancingAssetField.GetValue(null);
            bool isDemo = balancing != null && (bool)_gameIsDemoField.GetValue(balancing);

            visibleStages = BlueprintRewardStageResolver.BuildVisibleStages(
                progressionStages,
                tutorialStage,
                isDemo,
                useTutorialStage,
                delegate(object stage)
                {
                    return (bool)_stageDemoField.GetValue(stage);
                });
            return visibleStages.Count > 0;
        }

        private bool TryCreateStageOrderTokens(IList stageSequence, out List<string> stageTokens)
        {
            stageTokens = null;
            if (stageSequence == null || stageSequence.Count == 0)
            {
                return false;
            }

            var tokens = new List<string>(stageSequence.Count);
            for (int i = 0; i < stageSequence.Count; i++)
            {
                string token = GetStageOrderToken(stageSequence[i]);
                if (String.IsNullOrWhiteSpace(token))
                {
                    return false;
                }

                tokens.Add(token);
            }

            stageTokens = tokens;
            return true;
        }

        private string GetStageOrderToken(object stage)
        {
            if (stage == null)
            {
                return null;
            }

            for (int i = 0; i < _stages.Count; i++)
            {
                LoadedStage loadedStage = _stages[i];
                if (System.Object.ReferenceEquals(loadedStage.RuntimeStage, stage))
                {
                    string customToken;
                    return BlueprintRewardStageOrder.TryCreateCustomStageToken(
                        loadedStage.PackId, loadedStage.Definition.id, out customToken)
                        ? customToken
                        : null;
                }
            }

            object stageParameters = _stageParametersField.GetValue(stage);
            UnityEngine.Object stageAsset = stageParameters as UnityEngine.Object;
            if (stageAsset == null)
            {
                return null;
            }

            string nativeToken;
            return BlueprintRewardStageOrder.TryCreateNativeStageToken(stageAsset.name, out nativeToken)
                ? nativeToken
                : null;
        }

        private void LogStageMapOrderFailure(string message)
        {
            if (_stageMapOrderFailureLogged)
            {
                return;
            }

            _stageMapOrderFailureLogged = true;
            Log(message);
        }

        private string GetGlobalFallbackBundlePath()
        {
            // In a real scenario, this would read from a mod configuration file.
            return null;
        }

        private bool TryRestorePendingStageMapOrder(bool progressionWasApplied)
        {
            if (_pendingStageMapSnapshotOwner == null || !_runtimeContentReady)
            {
                return false;
            }

            IList levelMaps = _stageMapLevelMapsField.GetValue(_pendingStageMapSnapshotOwner) as IList;
            List<string> savedStageTokens;
            if (levelMaps == null || levelMaps.Count == 0 ||
                !BlueprintRewardStageOrder.TryReadSerializedOrder(_pendingStageMapJson, out savedStageTokens) ||
                savedStageTokens.Count != levelMaps.Count)
            {
                LogStageMapOrderFailure("A saved stage map has no valid loader stage-order marker; " +
                    "custom factories will be omitted from generic rewards for this map.");
                ClearPendingStageMapOrder();
                return false;
            }

            object progression = _currentProgressionProperty.GetValue(null, null);
            IList progressionStages = progression == null
                ? null
                : _progressionStagesField.GetValue(progression) as IList;
            if (progression == null || progressionStages == null)
            {
                return false;
            }

            var candidates = new ArrayList();
            for (int i = 0; i < progressionStages.Count; i++)
            {
                candidates.Add(progressionStages[i]);
            }

            object tutorialStage = _progressionTutorialStageField.GetValue(progression);
            if (tutorialStage != null && !ContainsReference(candidates, tutorialStage))
            {
                candidates.Add(tutorialStage);
            }

            List<object> resolvedStages;
            if (BlueprintRewardStageResolver.TryResolveSavedStageOrder(
                    savedStageTokens,
                    candidates,
                    GetStageOrderToken,
                    out resolvedStages) && resolvedStages.Count == levelMaps.Count)
            {
                _stageMapSnapshotOwner = _pendingStageMapSnapshotOwner;
                _stageMapVisibleStages = resolvedStages;
                ClearPendingStageMapOrder();
                return true;
            }

            if (progressionWasApplied)
            {
                LogStageMapOrderFailure("The saved stage-order marker did not resolve against the loaded content; " +
                    "custom factories will be omitted from generic rewards for this map.");
                ClearPendingStageMapOrder();
            }

            return false;
        }

        private void ClearPendingStageMapOrder()
        {
            _pendingStageMapSnapshotOwner = null;
            _pendingStageMapJson = null;
        }

        public BlueprintRewardFactoryScope CreateFailClosedBlueprintRewardFactoryScope()
        {
            lock (_gate)
            {
                return new BlueprintRewardFactoryScope(_blueprintUnitIds, null);
            }
        }

        public IEnumerable<ProgressionContentContribution> GetProgressionContributions(
            object gameBalancingScriptableObject)
        {
            lock (_gate)
            {
                return _runtimeContentReady
                    ? _contributions.ToArray()
                    : new ProgressionContentContribution[0];
            }
        }

        public bool TryLoadResource(string resourcePath, out UnityEngine.Object asset)
        {
            lock (_gate)
            {
                if (resourcePath != null && _resourceAssets.TryGetValue(resourcePath, out asset) && asset != null)
                {
                    if (asset is GameObject prefab)
                    {
                        if (_prefabClones.TryGetValue(resourcePath, out GameObject clone) && clone != null)
                        {
                            asset = clone;
                            return true;
                        }

                        GameObject newClone = InstantiateDormantPrefab(prefab);
                        if (newClone == null) return false;

                        newClone.name = "RCL_JIT_" + resourcePath.Replace('/', '_');
                        newClone.SetActive(false);
                        newClone.hideFlags = HideFlags.HideAndDontSave;
                        UnityEngine.Object.DontDestroyOnLoad(newClone);

                        _prefabClones[resourcePath] = newClone;
                        asset = newClone;
                        return true;
                    }
                    return true;
                }
            }

            asset = null;
            return false;
        }

        public bool TryGetLandscapeLocalization(string localizationId, out string value)
        {
            lock (_gate)
            {
                LoadedLandscape landscape;
                if (!String.IsNullOrWhiteSpace(localizationId) &&
                    _landscapesByLocalizationId.TryGetValue(localizationId, out landscape))
                {
                    string language = _localizationLanguageProperty.GetValue(null, null) as string;
                    value = FindLandscapeLocalization(landscape.Definition.localizations, language);
                    if (String.IsNullOrWhiteSpace(value))
                    {
                        value = FindLandscapeLocalization(landscape.Definition.localizations, "en-US");
                    }

                    return !String.IsNullOrWhiteSpace(value);
                }
            }

            value = null;
            return false;
        }

        public IDisposable BeginStageLandscapeScope(object stage)
        {
            if (stage == null)
            {
                return null;
            }

            lock (_gate)
            {
                LoadedStage loadedStage = null;
                for (int i = 0; i < _stages.Count; i++)
                {
                    if (System.Object.ReferenceEquals(_stages[i].RuntimeStage, stage))
                    {
                        loadedStage = _stages[i];
                        break;
                    }
                }

                if (loadedStage == null || loadedStage.Landscape == null)
                {
                    return null;
                }

                object balancing = _gameBalancingAssetField.GetValue(null);
                if (balancing == null)
                {
                    throw new InvalidOperationException("GameBalancingStore no contiene una progresión activa.");
                }

                var scope = new StageLandscapeMutation(this);
                bool matchedProgression = false;
                try
                {
                    UnityEngine.Object landscape = loadedStage.Landscape.Asset;
                    for (int progressionIndex = 0; progressionIndex < _progressionFields.Length;
                        progressionIndex++)
                    {
                        object progression = _progressionFields[progressionIndex].GetValue(balancing);
                        if (progression == null)
                        {
                            continue;
                        }

                        IList stages = _progressionStagesField.GetValue(progression) as IList;
                        if (stages == null || !ContainsReference(stages, stage))
                        {
                            continue;
                        }

                        matchedProgression = true;
                        IList landscapes = _progressionLandscapesField.GetValue(progression) as IList;
                        AppendScopedLandscape(landscapes, landscape, scope,
                            "defaultLandscapeGenerators de " + _progressionFields[progressionIndex].Name);

                        IList ais = _progressionAisField.GetValue(progression) as IList;
                        if (ais == null)
                        {
                            throw new InvalidOperationException("defaultAis es nulo en " +
                                _progressionFields[progressionIndex].Name + ".");
                        }

                        for (int aiIndex = 0; aiIndex < ais.Count; aiIndex++)
                        {
                            object ai = ais[aiIndex];
                            if (ai == null)
                            {
                                throw new InvalidOperationException("defaultAis contiene una entrada nula.");
                            }

                            IList restrictedMaps = _aiRestrictedMapsField.GetValue(ai) as IList;
                            AppendScopedLandscape(restrictedMaps, landscape, scope,
                                "restrictedToMaps de AI " + GetObjectName(ai));
                        }
                    }

                    if (!matchedProgression)
                    {
                        throw new InvalidOperationException("La etapa " + loadedStage.Definition.id +
                            " no pertenece a ninguna progresión cargada.");
                    }

                    return scope;
                }
                catch
                {
                    scope.Dispose();
                    throw;
                }
            }
        }

        private static string FindLandscapeLocalization(
            ContentLandscapeLocalizationDefinition[] localizations,
            string language)
        {
            if (localizations == null || String.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            for (int i = 0; i < localizations.Length; i++)
            {
                ContentLandscapeLocalizationDefinition localization = localizations[i];
                if (localization != null && String.Equals(localization.language, language, StringComparison.Ordinal))
                {
                    return localization.name;
                }
            }

            return null;
        }

        private static bool ContainsReference(IList list, object value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (System.Object.ReferenceEquals(list[i], value))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AppendScopedLandscape(
            IList list,
            UnityEngine.Object landscape,
            StageLandscapeMutation scope,
            string source)
        {
            if (list == null)
            {
                throw new InvalidOperationException(source + " es nulo.");
            }

            if (ContainsReference(list, landscape))
            {
                return;
            }

            list.Add(landscape);
            scope.Record(list, landscape);
        }

        private void RestoreStageLandscapeMutation(StageLandscapeMutation scope)
        {
            lock (_gate)
            {
                for (int i = scope.Entries.Count - 1; i >= 0; i--)
                {
                    ScopedLandscapeListEntry entry = scope.Entries[i];
                    for (int itemIndex = entry.List.Count - 1; itemIndex >= 0; itemIndex--)
                    {
                        if (System.Object.ReferenceEquals(entry.List[itemIndex], entry.Landscape))
                        {
                            entry.List.RemoveAt(itemIndex);
                        }
                    }
                }

                scope.Entries.Clear();
            }
        }

        public LoaderDiagnosticsSnapshot GetDiagnosticsSnapshot()
        {
            lock (_gate)
            {
                var packs = new List<PackDiagnosticReport>(_packCandidates.Count + 1);
                int activePackCount = 0;
                int failedPackCount = 0;
                for (int i = 0; i < _packCandidates.Count; i++)
                {
                    PackCandidate candidate = _packCandidates[i];
                    bool loaded = candidate.LoadSucceeded;
                    if (loaded) activePackCount++;
                    else failedPackCount++;

                    ContentPackDefinition definition = candidate.Definition;
                    string packId = definition != null && !String.IsNullOrWhiteSpace(definition.packId)
                        ? definition.packId
                        : candidate.Name;
                    string status = loaded
                        ? "Cargado correctamente."
                        : (String.IsNullOrWhiteSpace(candidate.StatusReason)
                            ? "El paquete fue desactivado o no llegó a cargarse."
                            : candidate.StatusReason);
                    packs.Add(new PackDiagnosticReport(
                        packId,
                        candidate.Name,
                        loaded,
                        status,
                        definition == null || definition.assetBundles == null ? 0 : definition.assetBundles.Length,
                        candidate.LoadedBytes,
                        definition == null || definition.units == null ? 0 : definition.units.Length,
                        definition == null || definition.stages == null ? 0 : definition.stages.Length,
                        definition == null || definition.aiProfiles == null ? 0 : definition.aiProfiles.Length));
                }

                if (!String.IsNullOrWhiteSpace(_packRootError))
                {
                    failedPackCount++;
                    packs.Add(new PackDiagnosticReport(
                        "<content/packs>", "content/packs", false, _packRootError, 0, 0, 0, 0, 0));
                }

                packs.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.FolderName, right.FolderName));

                var units = new List<UnitDiagnosticReport>(_units.Count);
                foreach (LoadedUnit unit in _units.Values)
                {
                    bool allowedAsBlueprint = false;
                    bool allowedAsStartingBlueprint = false;
                    bool allowedForAi = false;
                    bool allowedForDemo = false;
                    bool isForSpecialists = false;
                    try { allowedAsBlueprint = (bool)_allowedBlueprintField.GetValue(unit.BalanceRow); }
                    catch { }
                    try { allowedAsStartingBlueprint = (bool)_allowedStartingBlueprintField.GetValue(unit.BalanceRow); }
                    catch { }
                    try { allowedForAi = (bool)_allowedForAiField.GetValue(unit.BalanceRow); }
                    catch { }
                    try { allowedForDemo = (bool)_allowedForDemoField.GetValue(unit.BalanceRow); }
                    catch { }
                    try { isForSpecialists = (bool)_isForSpecialistsField.GetValue(unit.BalanceRow); }
                    catch { }
                    units.Add(new UnitDiagnosticReport(
                        unit.Id, unit.PackId, unit.PrefabKey, unit.ImageKey, allowedAsBlueprint, allowedAsStartingBlueprint, allowedForAi, allowedForDemo, isForSpecialists));
                }
                units.Sort((left, right) => StringComparer.Ordinal.Compare(left.UnitId, right.UnitId));

                var stages = new List<StageDiagnosticReport>(_stages.Count);
                for (int i = 0; i < _stages.Count; i++)
                {
                    LoadedStage stage = _stages[i];
                    stages.Add(new StageDiagnosticReport(
                        stage.Definition.id,
                        stage.PackId,
                        stage.Definition.mapSizeMin,
                        stage.Definition.mapSizeMax,
                        stage.Definition.unitIds,
                        stage.Definition.aiProfileIds));
                }
                stages.Sort((left, right) => StringComparer.Ordinal.Compare(left.StageId, right.StageId));

                var activeInstances = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (BoundAiRuntimeBehaviour bound in _boundAiBehaviours.Values)
                {
                    if (bound == null || !bound.Initialized || bound.Failed || bound.Destroyed ||
                        String.IsNullOrWhiteSpace(bound.ProfileId))
                    {
                        continue;
                    }

                    int count;
                    activeInstances.TryGetValue(bound.ProfileId, out count);
                    activeInstances[bound.ProfileId] = count + 1;
                }

                var profiles = new List<AiProfileDiagnosticReport>(_aiProfiles.Count);
                foreach (LoadedAiProfile profile in _aiProfiles.Values)
                {
                    int count;
                    activeInstances.TryGetValue(profile.Id, out count);
                    profiles.Add(new AiProfileDiagnosticReport(
                        profile.Id,
                        profile.PackId,
                        profile.ResourceName,
                        profile.ModuleIds,
                        count));
                }
                profiles.Sort((left, right) => StringComparer.Ordinal.Compare(left.ProfileId, right.ProfileId));

                string[] recentLogs;
                lock (_logGate)
                {
                    recentLogs = _recentLogs.ToArray();
                }

                return new LoaderDiagnosticsSnapshot(
                    TargetGameVersion,
                    _nativeRegistryReady,
                    _loaded,
                    activePackCount,
                    failedPackCount,
                    _loadedAssetBundleBytes,
                    packs,
                    units,
                    stages,
                    profiles,
                    recentLogs);
            }
        }

        public IEnumerable<BalanceOverride> GetBalanceOverrides()
        {
            lock (_gate)
            {
                if (!_runtimeContentReady)
                {
                    return new List<BalanceOverride>();
                }

                var overrides = new List<BalanceOverride>();
                foreach (var candidate in _packCandidates)
                {
                    if (candidate == null || candidate.Definition == null)
                    {
                        continue;
                    }

                    if (candidate.Definition.units == null)
                    {
                        continue;
                    }

                    foreach (var unit in candidate.Definition.units)
                    {
                        if (unit == null || unit.customBalance == null)
                        {
                            continue;
                        }

                        overrides.Add(new BalanceOverride
                        {
                            EntityId = unit.id,
                            Parameters = unit.customBalance
                        });
                    }
                }
                return overrides;
            }
        }

        public void OnAiBehaviourInitialized(object gameBehaviour)
        {
            DefaultAiBehaviour behaviour = gameBehaviour as DefaultAiBehaviour;
            LoadedAiProfile profile;
            if (!TryGetAiProfile(behaviour, out profile))
            {
                return;
            }

            var bound = new BoundAiRuntimeBehaviour(null, null, profile.Id);
            bound.Initializing = true;
            lock (_gate)
            {
                BoundAiRuntimeBehaviour existing;
                if (_boundAiBehaviours.TryGetValue(behaviour, out existing))
                {
                    return;
                }

                _boundAiBehaviours.Add(behaviour, bound);
            }

            IRogueCommandAiRuntimeBehaviour runtimeBehaviour;
            string diagnostic;
            bool created;
            try
            {
                created = TryCreateProfileBehaviour(profile, behaviour, bound,
                    out runtimeBehaviour, out diagnostic);
            }
            catch (Exception exception)
            {
                runtimeBehaviour = null;
                diagnostic = "module creation failed unexpectedly: " + exception.GetType().Name + ": " +
                    exception.Message;
                created = false;
            }

            if (!created)
            {
                bool recordFailure = false;
                lock (_gate)
                {
                    BoundAiRuntimeBehaviour existing;
                    if (_boundAiBehaviours.TryGetValue(behaviour, out existing) &&
                        System.Object.ReferenceEquals(existing, bound))
                    {
                        bound.Initializing = false;
                        bound.Failed = true;
                        RemoveAiBehaviourBindingIfCurrentLocked(behaviour, bound);
                        recordFailure = true;
                    }
                }

                if (recordFailure)
                {
                    Log("No se pudo enlazar el conjunto de módulos al AI " +
                        profile.Definition.id + ": " + diagnostic);
                }
                return;
            }

            bool canInitialize = false;
            lock (_gate)
            {
                BoundAiRuntimeBehaviour existing;
                if (_boundAiBehaviours.TryGetValue(behaviour, out existing) &&
                    System.Object.ReferenceEquals(existing, bound) && !bound.Destroyed && !bound.Failed)
                {
                    bound.RuntimeBehaviour = runtimeBehaviour;
                    canInitialize = true;
                }
            }

            if (!canInitialize)
            {
                SafeDestroyAiRuntimeBehaviour(new BoundAiRuntimeBehaviour(runtimeBehaviour, null, profile.Id),
                    profile.Definition.id);
                return;
            }

            try
            {
                bound.RuntimeBehaviour.OnInitialized();
                bool stillRegistered = false;
                lock (_gate)
                {
                    BoundAiRuntimeBehaviour existing;
                    if (_boundAiBehaviours.TryGetValue(behaviour, out existing) &&
                        System.Object.ReferenceEquals(existing, bound) && !bound.Destroyed && !bound.Failed)
                    {
                        bound.Initializing = false;
                        bound.Initialized = true;
                        stillRegistered = true;
                    }
                }

                if (!stillRegistered)
                {
                    SafeDestroyAiRuntimeBehaviour(bound, profile.Definition.id);
                }
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    bound.Initializing = false;
                    bound.Failed = true;
                    RemoveAiBehaviourBindingIfCurrentLocked(behaviour, bound);
                }

                SafeDestroyAiRuntimeBehaviour(bound, profile.Definition.id);
                Log("El módulo AI de " + profile.Definition.id + " falló al inicializar la instancia " +
                    behaviour.gameObject.name + "; se desactiva su despacho: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private bool TryCreateProfileBehaviour(
            LoadedAiProfile profile,
            DefaultAiBehaviour gameBehaviour,
            BoundAiRuntimeBehaviour expectedBinding,
            out IRogueCommandAiRuntimeBehaviour runtimeBehaviour,
            out string diagnostic)
        {
            runtimeBehaviour = null;
            diagnostic = null;
            ContentAiModuleDefinition[] modules = profile.Definition.modules;
            if (modules == null || modules.Length == 0 ||
                modules.Length > ContentPackValidator.MaxAiModulesPerProfile)
            {
                diagnostic = "the profile has an invalid module count.";
                return false;
            }

            var moduleIds = new List<string>(modules.Length);
            var behaviours = new List<IRogueCommandAiRuntimeBehaviour>(modules.Length);
            for (int i = 0; i < modules.Length; i++)
            {
                ContentAiModuleDefinition module = modules[i];
                if (module == null || String.IsNullOrWhiteSpace(module.moduleId))
                {
                    diagnostic = "module entry " + i + " is empty.";
                    DestroyCreatedAiModules(profile.Definition.id, moduleIds, behaviours);
                    return false;
                }

                IRogueCommandAiRuntimeBehaviour moduleBehaviour;
                string moduleDiagnostic;
                if (!_moduleRegistry.TryCreateBehaviour(module.moduleId, gameBehaviour, module.parameters,
                    out moduleBehaviour, out moduleDiagnostic))
                {
                    diagnostic = "module " + module.moduleId + " at index " + i + " could not be created: " +
                        moduleDiagnostic;
                    DestroyCreatedAiModules(profile.Definition.id, moduleIds, behaviours);
                    return false;
                }

                moduleIds.Add(module.moduleId);
                behaviours.Add(moduleBehaviour);

                bool bindingStillCurrent;
                lock (_gate)
                {
                    BoundAiRuntimeBehaviour current;
                    bindingStillCurrent = _boundAiBehaviours.TryGetValue(gameBehaviour, out current) &&
                        System.Object.ReferenceEquals(current, expectedBinding) && !expectedBinding.Destroyed;
                }

                if (!bindingStillCurrent)
                {
                    diagnostic = "the AI instance was destroyed while its modules were being created.";
                    DestroyCreatedAiModules(profile.Definition.id, moduleIds, behaviours);
                    return false;
                }
            }

            runtimeBehaviour = new CompositeAiRuntimeBehaviour(profile.Definition.id, moduleIds, behaviours);
            return true;
        }

        private void DestroyCreatedAiModules(
            string profileId,
            List<string> moduleIds,
            List<IRogueCommandAiRuntimeBehaviour> behaviours)
        {
            if (behaviours.Count == 0)
            {
                return;
            }

            try
            {
                new CompositeAiRuntimeBehaviour(profileId, moduleIds, behaviours).OnDestroyed();
            }
            catch (Exception exception)
            {
                Log("No se pudo limpiar un AI parcial del perfil " + profileId + ": " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        public void OnAiBehaviourUpdated(object gameBehaviour)
        {
            DefaultAiBehaviour behaviour = gameBehaviour as DefaultAiBehaviour;
            LoadedAiProfile profile;
            if (!TryGetAiProfile(behaviour, out profile))
            {
                return;
            }

            BoundAiRuntimeBehaviour bound;
            lock (_gate)
            {
                if (!_boundAiBehaviours.TryGetValue(behaviour, out bound) ||
                    bound.Failed || !bound.Initialized || bound.RuntimeBehaviour == null)
                {
                    return;
                }
            }

            try
            {
                bound.RuntimeBehaviour.Tick();
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    bound.Failed = true;
                    RemoveAiBehaviourBindingIfCurrentLocked(behaviour, bound);
                }

                SafeDestroyAiRuntimeBehaviour(bound, profile.Definition.id);
                Log("El módulo AI de " + profile.Definition.id + " falló durante Tick para la instancia " +
                    behaviour.gameObject.name + "; se desactiva su despacho: " +
                    exception.GetType().Name + ": " + exception.Message);