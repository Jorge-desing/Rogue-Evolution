using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
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
        IStageOwnedAiPlacementDistanceRuntimeProvider, IBlueprintRewardFactoryScopeProvider,
        IPrespawnRuntimeProvider, IGuaranteedModCardRewardProvider, IGlobalBlueprintCandidateProvider
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

        private static readonly Dictionary<string, float> _minAttackRanges =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public static bool TryGetMinAttackRange(string entityId, out float minRange)
        {
            if (string.IsNullOrEmpty(entityId))
            {
                minRange = 0f;
                return false;
            }
            lock (_minAttackRanges)
            {
                return _minAttackRanges.TryGetValue(entityId, out minRange);
            }
        }

        public static void RegisterMinAttackRange(string entityId, float minRange)
        {
            if (!string.IsNullOrEmpty(entityId) && minRange > 0f)
            {
                lock (_minAttackRanges)
                {
                    _minAttackRanges[entityId] = minRange;
                }
            }
        }

        public static void ClearMinAttackRanges()
        {
            lock (_minAttackRanges)
            {
                _minAttackRanges.Clear();
            }
        }

        private static readonly Dictionary<string, List<ContentOnHitStatusEffectDefinition>> _onHitStatusEffects =
            new Dictionary<string, List<ContentOnHitStatusEffectDefinition>>(StringComparer.Ordinal);

        public static bool TryGetOnHitStatusEffects(string entityId, out List<ContentOnHitStatusEffectDefinition> effects)
        {
            if (string.IsNullOrEmpty(entityId))
            {
                effects = null;
                return false;
            }
            lock (_onHitStatusEffects)
            {
                return _onHitStatusEffects.TryGetValue(entityId, out effects);
            }
        }

        public static void RegisterOnHitStatusEffects(string entityId, ContentOnHitStatusEffectDefinition[] effects)
        {
            if (!string.IsNullOrEmpty(entityId) && effects != null && effects.Length > 0)
            {
                var list = new List<ContentOnHitStatusEffectDefinition>(effects.Length);
                for (int i = 0; i < effects.Length; i++)
                {
                    if (effects[i] != null) list.Add(effects[i]);
                }
                lock (_onHitStatusEffects)
                {
                    _onHitStatusEffects[entityId] = list;
                }
            }
        }

        public static void ClearOnHitStatusEffects()
        {
            lock (_onHitStatusEffects)
            {
                _onHitStatusEffects.Clear();
            }
        }

        private readonly object _gate = new object();
        private readonly string _packsRoot;
        private readonly Assembly _gameAssembly;
        private readonly RelicContentRuntimeRegistrar _relicRegistrar;
        private readonly AiModuleRegistry _moduleRegistry;
        private readonly Action<string> _log;
        private readonly RuntimeMutationManager _mutationManager;
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
        private readonly FieldInfo _rotationDegreesPerSecondField;
        private readonly FieldInfo _collisionRadiusField;
        private readonly FieldInfo _attackWarmUpTimeField;
        private readonly FieldInfo _canAttackWhileMovingField;
        private readonly FieldInfo _shootAutomaticallyField;
        private readonly FieldInfo _defendPositionAggressivelyField;
        private readonly FieldInfo _rangeToChooseNewTargetField;
        private readonly FieldInfo _selectionSoundField;
        private readonly FieldInfo _movementSoundAudioSourceField;
        private readonly FieldInfo _hasActiveSkillField;
        private readonly FieldInfo _activeSkillOrProductionField;
        private readonly FieldInfo _activeSkillTypeField;
        private readonly FieldInfo _helpAiToActivateSkillField;
        private readonly FieldInfo _aiSkillCooldownField;
        private readonly FieldInfo _skillInfoField;
        private readonly FieldInfo _conditionToActivateSkillField;
        private readonly FieldInfo _entityIdentifiersField;
        private readonly Type _critModType;
        private readonly MethodInfo _addCriticalHitChanceBonusMethod;
        private readonly Type _skillInfoType;
        private readonly Type _targetOriginType;
        private readonly Type _skillActivationConditionType;
        private readonly Type _entityIdentifierType;
        private readonly FieldInfo _prefabLocationField;
        private readonly FieldInfo _imageLocationField;
        private readonly FieldInfo _inactiveField;
        private readonly FieldInfo _allowedBlueprintField;
        private readonly FieldInfo _allowedStartingBlueprintField;
        private readonly FieldInfo _allowedForAiField;
        private readonly FieldInfo _allowedForDemoField;
        private readonly FieldInfo _isForSpecialistsField;
        private readonly FieldInfo _combatValueField;
        private readonly FieldInfo _gridSizeField;
        private readonly FieldInfo _cardModelScalingFactorField;
        private readonly FieldInfo _coinsAmountField;
        private readonly FieldInfo _firePointCountField;
        private readonly FieldInfo _copyUpgradesToEntityIdField;
        private readonly FieldInfo _attackTypeField;
        private readonly FieldInfo _skillTypeField;
        private readonly FieldInfo _rarityField;
        private readonly FieldInfo _techField;
        private readonly FieldInfo _neededExperienceLevelField;
        private readonly FieldInfo _offeredSystemTagsField;
        private readonly FieldInfo _costField;
        private readonly FieldInfo _productionDurationField;
        private readonly FieldInfo _maxCapacityField;
        private readonly FieldInfo _damage1Field;
        private readonly FieldInfo _damage2Field;
        private readonly FieldInfo _weaponRangeField;
        private readonly FieldInfo _attackCooldownField;
        private readonly FieldInfo _attack2CooldownField;
        private readonly FieldInfo _effectRadius1Field;
        private readonly FieldInfo _effectRadius2Field;
        private readonly FieldInfo _skillRangeField;
        private readonly FieldInfo _healAmount1Field;
        private readonly FieldInfo _healAmount2Field;
        private readonly FieldInfo _gainCreditsAmountField;
        private readonly FieldInfo _skillManaCostField;
        private readonly FieldInfo _duration1Field;
        private readonly FieldInfo _duration2Field;
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
        private readonly FieldInfo _stageParametersFirstLevelCardsField;
        private readonly FieldInfo _stageParametersDefaultCenterField;
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
        private int _mutationSuccessCount;
        private int _mutationFailureCount;
        private string _resolvedPacksRoot;
        private string _packRootError;
        private bool _nativeRegistryReady;
        private bool _registryUnavailableLogged;
        private bool _runtimeContentReady;
        private bool _loaded;
        private Task _loadingTask;
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
            _mutationManager = new RuntimeMutationManager(_log ?? (msg => { }));

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
            _rotationDegreesPerSecondField = _entityControllerType.GetField("rotationDegreesPerSecond", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _collisionRadiusField = _entityControllerType.GetField("collisionRadius", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _attackWarmUpTimeField = _entityControllerType.GetField("attackWarmUpTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _canAttackWhileMovingField = _entityControllerType.GetField("canAttackWhileMoving", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _shootAutomaticallyField = _entityControllerType.GetField("shootAutomaticallyOnEnemiesWithinRange", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _defendPositionAggressivelyField = _entityControllerType.GetField("defendPositionAggressivelyWhenIdle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _rangeToChooseNewTargetField = _entityControllerType.GetField("rangeToChooseNewTargetAutomatically", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _selectionSoundField = _entityControllerType.GetField("selectionSound", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _movementSoundAudioSourceField = _entityControllerType.GetField("movementSoundAudioSource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _hasActiveSkillField = _entityControllerType.GetField("hasActiveSkill", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _activeSkillOrProductionField = _entityControllerType.GetField("activeSkillOrProduction", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _activeSkillTypeField = _entityControllerType.GetField("activeSkillType", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _helpAiToActivateSkillField = _entityControllerType.GetField("helpAiToActivateSkill", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _aiSkillCooldownField = _entityControllerType.GetField("aiSkillCooldown", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _skillInfoField = _entityControllerType.GetField("_skillInfo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _conditionToActivateSkillField = _entityControllerType.GetField("conditionToActivateSkill", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _entityIdentifiersField = _entityControllerType.GetField("entityIdentifiers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _skillInfoType = _gameAssembly.GetType("SkillInfo", false);
            _targetOriginType = _gameAssembly.GetType("TargetOrigin", false);
            _skillActivationConditionType = _gameAssembly.GetType("SkillActivationCondition", false);
            _entityIdentifierType = _gameAssembly.GetType("EntityIdentifier", false);
            CounterBatteryAiModule.MinAttackRangeResolver = id => TryGetMinAttackRange(id, out float r) ? (float?)r : null;
            Runtime.RuntimeHooks.MinAttackRangeResolver = id => TryGetMinAttackRange(id, out float r) ? (float?)r : null;
            Runtime.RuntimeHooks.OnHitStatusEffectsResolver = id => TryGetOnHitStatusEffects(id, out List<ContentOnHitStatusEffectDefinition> fx) ? fx : null;
            _critModType = _gameAssembly.GetType("CriticalHitChanceModifier", false);
            if (_critModType != null && _entityControllerType != null)
            {
                _addCriticalHitChanceBonusMethod = _entityControllerType.GetMethod(
                    "AddCriticalHitChanceBonus",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new Type[] { _critModType },
                    null);
            }
            _prefabLocationField = RequireField(_entityRowType, "prefabLocation", typeof(string));
            _imageLocationField = RequireField(_entityRowType, "imageLocation", typeof(string));
            _inactiveField = RequireField(_entityRowType, "inactive", typeof(bool));
            _allowedBlueprintField = RequireField(_entityRowType, "isAllowedAsBlueprint", typeof(bool));
            _allowedStartingBlueprintField = RequireField(_entityRowType, "isAllowedAsStartingBlueprint", typeof(bool));
            _allowedForAiField = RequireField(_entityRowType, "isAllowedForAi", typeof(bool));
            _allowedForDemoField = RequireField(_entityRowType, "isAllowedForDemo", typeof(bool));
            _isForSpecialistsField = RequireField(_entityRowType, "isForSpecialists", typeof(bool));
            _combatValueField = RequireField(_entityRowType, "combatValue", typeof(int));
            _gridSizeField = RequireField(_entityRowType, "gridSize", typeof(int));
            _cardModelScalingFactorField = RequireField(_entityRowType, "cardModelScalingFactor", typeof(float));
            _coinsAmountField = RequireField(_entityRowType, "coinsAmount", typeof(int));
            _firePointCountField = RequireField(_entityRowType, "firePointCount", typeof(int));
            _copyUpgradesToEntityIdField = RequireField(_entityRowType, "copyUpgradesToEntityId", nullableStringType);
            _attackTypeField = FindField(_entityRowType, "attackType", InstanceFields);
            _skillTypeField = FindField(_entityRowType, "skillType", InstanceFields);
            _rarityField = FindField(_entityRowType, "rarity", InstanceFields);
            _techField = FindField(_entityRowType, "tech", InstanceFields);
            _neededExperienceLevelField = RequireField(_entityRowType, "neededExperienceLevel", typeof(int));
            _offeredSystemTagsField = FindField(_entityRowType, "offeredSystemTags", InstanceFields);
            _costField = RequireField(_entityRowType, "cost", typeof(int));
            _productionDurationField = RequireField(_entityRowType, "productionDuration", typeof(float));
            _maxCapacityField = RequireField(_entityRowType, "maxCapacity", typeof(int));
            _damage1Field = FindField(_entityRowType, "damage1", InstanceFields);
            _damage2Field = FindField(_entityRowType, "damage2", InstanceFields);
            _weaponRangeField = FindField(_entityRowType, "weaponRange", InstanceFields);
            _attackCooldownField = FindField(_entityRowType, "attackCooldown", InstanceFields);
            _attack2CooldownField = FindField(_entityRowType, "attack2Cooldown", InstanceFields);
            _effectRadius1Field = FindField(_entityRowType, "effectRadius1", InstanceFields);
            _effectRadius2Field = FindField(_entityRowType, "effectRadius2", InstanceFields);
            _skillRangeField = FindField(_entityRowType, "skillRange", InstanceFields);
            _healAmount1Field = FindField(_entityRowType, "healAmount1", InstanceFields);
            _healAmount2Field = FindField(_entityRowType, "healAmount2", InstanceFields);
            _gainCreditsAmountField = FindField(_entityRowType, "gainCreditsAmount", InstanceFields);
            _skillManaCostField = FindField(_entityRowType, "skillManaCost", InstanceFields);
            _duration1Field = FindField(_entityRowType, "duration1", InstanceFields);
            _duration2Field = FindField(_entityRowType, "duration2", InstanceFields);
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
            StageParameterBindingContract stageParameterBindings = StageParameterBindingContract.Resolve(
                _stageParametersType, _blueprintPoolListType, InstanceFields);
            _stageParametersBlueprintPoolListField = stageParameterBindings.BlueprintPoolListField;
            _stageParametersFirstLevelCardsField = stageParameterBindings.FirstLevelCardsField;
            _stageParametersDefaultCenterField = stageParameterBindings.DefaultCenterField;

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
                if (_loadingTask == null)
                {
                    _loadingTask = LoadAllPacksAsync();
                }
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

        private string GetProductIdForFactory(string factoryId)
        {
            if (String.IsNullOrWhiteSpace(factoryId))
            {
                return null;
            }

            foreach (LoadedUnit u in _units.Values)
            {
                if (!u.IsProductionBuilding && String.Equals(u.ProductionFactoryId, factoryId, StringComparison.Ordinal))
                {
                    return u.Id;
                }
            }

            return null;
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
                    (bool)_allowedStartingBlueprintField.GetValue(unit.BalanceRow) ||
                    (unit.Definition != null && (unit.Definition.neededExperienceLevel ?? 0) <= 0);
                if (isStartingBlueprintSelection && !isAllowedAsStartingBlueprint)
                {
                    runOriginal = false;
                    return true;
                }

                if (isDemo && !isAllowedForDemo)
                {
                    runOriginal = false;
                    return true;
                }

                if (_blueprintPoolScopeRegistry.ContainsPool(pool))
                {
                    string productId = !unit.IsProductionBuilding && !String.IsNullOrWhiteSpace(unit.ProductionFactoryId)
                        ? unit.Id
                        : (unit.IsProductionBuilding ? GetProductIdForFactory(unit.Id) : unit.Id);

                    runOriginal = _blueprintPoolScopeRegistry.AllowsModUnitInPool(
                        pool, unitId, isDemo, isAllowedForDemo,
                        isStartingBlueprintSelection, isAllowedAsStartingBlueprint) ||
                        (!String.IsNullOrEmpty(productId) && _blueprintPoolScopeRegistry.AllowsModUnitInPool(
                        pool, productId, isDemo, isAllowedForDemo,
                        isStartingBlueprintSelection, isAllowedAsStartingBlueprint));
                    return true;
                }

                if (_blueprintUnitIds.Contains(unitId) ||
                    (!String.IsNullOrWhiteSpace(unit.ProductionFactoryId) && _blueprintUnitIds.Contains(unit.ProductionFactoryId)))
                {
                    runOriginal = true;
                    return true;
                }

                runOriginal = false;
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
            foreach (LoadedUnit unit in _units.Values)
            {
                if (!unit.IsProductionBuilding && !String.IsNullOrWhiteSpace(unit.ProductionFactoryId))
                {
                    if (unit.Definition != null &&
                        (unit.Definition.isAllowedAsStartingBlueprint || (unit.Definition.neededExperienceLevel ?? 0) <= 0) &&
                        _blueprintUnitIds.Contains(unit.ProductionFactoryId))
                    {
                        LoadedUnit factory;
                        if (_units.TryGetValue(unit.ProductionFactoryId, out factory) &&
                            factory.IsProductionBuilding &&
                            (!isDemo || (bool)_allowedForDemoField.GetValue(factory.BalanceRow)))
                        {
                            destination.Add(factory.Id);
                        }
                    }
                }
                else if (unit.IsStandaloneBuilding)
                {
                    object startingVal = _allowedStartingBlueprintField.GetValue(unit.BalanceRow);
                    bool isStarting = (startingVal is bool && (bool)startingVal) ||
                                      (unit.Definition != null && (unit.Definition.neededExperienceLevel ?? 0) <= 0);
                    if (isStarting)
                    {
                        if (_blueprintUnitIds.Contains(unit.Id) &&
                            (!isDemo || (bool)_allowedForDemoField.GetValue(unit.BalanceRow)))
                        {
                            destination.Add(unit.Id);
                        }
                    }
                }
            }
        }

        public bool IsModUnitProduct(string productId)
        {
            if (String.IsNullOrEmpty(productId))
            {
                return false;
            }

            lock (_gate)
            {
                LoadedUnit unit;
                if (_units.TryGetValue(productId, out unit))
                {
                    return !unit.IsProductionBuilding || unit.IsStandaloneBuilding;
                }

                if (_blueprintUnitIds.Contains(productId))
                {
                    return true;
                }

                return false;
            }
        }

        public bool TryGetGuaranteedModRewardProduct(
            string poolId,
            bool isStartingSelection,
            int currentXpLevel,
            ICollection<string> excludedProductIds,
            out string modProductId)
        {
            lock (_gate)
            {
                modProductId = null;
                if (_units.Count == 0)
                {
                    return false;
                }

                object balancing = _gameBalancingAssetField != null ? _gameBalancingAssetField.GetValue(null) : null;
                bool isDemo = balancing != null && _gameIsDemoField != null && (bool)_gameIsDemoField.GetValue(balancing);

                List<LoadedUnit> matchedPoolCandidates = new List<LoadedUnit>();
                List<LoadedUnit> generalCandidates = new List<LoadedUnit>();
                List<LoadedUnit> fallbackCandidates = new List<LoadedUnit>();

                foreach (LoadedUnit unit in _units.Values)
                {
                    if (unit.IsProductionBuilding && !unit.IsStandaloneBuilding)
                    {
                        continue;
                    }

                    if (isDemo && _allowedForDemoField != null && !(bool)_allowedForDemoField.GetValue(unit.BalanceRow))
                    {
                        continue;
                    }

                    int neededXp = 0;
                    if (unit.Definition != null && unit.Definition.neededExperienceLevel.HasValue)
                    {
                        neededXp = unit.Definition.neededExperienceLevel.Value;
                    }

                    if (isStartingSelection)
                    {
                        bool isStartingEligible = (unit.Definition != null && unit.Definition.isAllowedAsStartingBlueprint) ||
                                                  neededXp <= 0;
                        if (!isStartingEligible)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (neededXp > currentXpLevel)
                        {
                            continue;
                        }
                    }

                    fallbackCandidates.Add(unit);

                    bool isExcluded = excludedProductIds != null &&
                        (excludedProductIds.Contains(unit.Id) ||
                         (!String.IsNullOrEmpty(unit.ProductionFactoryId) && excludedProductIds.Contains(unit.ProductionFactoryId)));

                    if (isExcluded)
                    {
                        continue;
                    }

                    generalCandidates.Add(unit);

                    if (!String.IsNullOrEmpty(poolId) && UnitMatchesPoolTheme(unit, poolId))
                    {
                        matchedPoolCandidates.Add(unit);
                    }
                }

                List<LoadedUnit> selectedList = matchedPoolCandidates.Count > 0
                    ? matchedPoolCandidates
                    : (generalCandidates.Count > 0 ? generalCandidates : fallbackCandidates);

                if (selectedList.Count == 0)
                {
                    return false;
                }

                int index = new System.Random().Next(0, selectedList.Count);
                modProductId = selectedList[index].Id;
                return true;
            }
        }

        public IEnumerable<string> GetEligibleBlueprintFactories(
            int rarity,
            bool? startingBlueprints,
            bool demoBlueprintsOnly,
            int? maxNeededExperienceLevel)
        {
            lock (_gate)
            {
                var result = new List<string>();
                foreach (LoadedUnit unit in _units.Values)
                {
                    if (!unit.IsProductionBuilding && !unit.IsStandaloneBuilding)
                    {
                        continue;
                    }

                    if (!_blueprintUnitIds.Contains(unit.Id))
                    {
                        continue;
                    }

                    if (_inactiveField != null && (bool)_inactiveField.GetValue(unit.BalanceRow))
                    {
                        continue;
                    }

                    if (demoBlueprintsOnly && _allowedForDemoField != null && !(bool)_allowedForDemoField.GetValue(unit.BalanceRow))
                    {
                        continue;
                    }

                    if (_rarityField != null && rarity >= 0)
                    {
                        object rowRarity = _rarityField.GetValue(unit.BalanceRow);
                        if (rowRarity != null && Convert.ToInt32(rowRarity) != rarity)
                        {
                            continue;
                        }
                    }

                    int neededXp = 0;
                    if (unit.Definition != null && unit.Definition.neededExperienceLevel.HasValue)
                    {
                        neededXp = unit.Definition.neededExperienceLevel.Value;
                    }
                    else if (_neededExperienceLevelField != null)
                    {
                        object xpVal = _neededExperienceLevelField.GetValue(unit.BalanceRow);
                        if (xpVal is int)
                        {
                            neededXp = (int)xpVal;
                        }
                    }

                    if (maxNeededExperienceLevel.HasValue && neededXp > maxNeededExperienceLevel.Value)
                    {
                        continue;
                    }

                    if (startingBlueprints.HasValue)
                    {
                        bool isStartingEligible = false;
                        if (_allowedStartingBlueprintField != null)
                        {
                            object val = _allowedStartingBlueprintField.GetValue(unit.BalanceRow);
                            if (val is bool && (bool)val)
                            {
                                isStartingEligible = true;
                            }
                        }

                        if (!isStartingEligible && unit.Definition != null)
                        {
                            isStartingEligible = unit.Definition.isAllowedAsStartingBlueprint || neededXp <= 0;
                        }

                        if (startingBlueprints.Value && !isStartingEligible)
                        {
                            continue;
                        }
                    }

                    result.Add(unit.Id);
                }

                return result;
            }
        }

        private static bool UnitMatchesPoolTheme(LoadedUnit unit, string poolId)
        {
            if (unit == null || String.IsNullOrEmpty(poolId) || unit.Definition == null)
            {
                return false;
            }

            string[] roles = unit.Definition.roles;
            if (roles == null || roles.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < roles.Length; i++)
            {
                string role = roles[i];
                if (!String.IsNullOrEmpty(role) && poolId.IndexOf(role, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
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
                        if (isStartingBlueprintSelection)
                        {
                            AddInitialStageStartingFactories(stageFactoryIds);
                        }
                        else if (!activeStageIsPackOwned)
                        {
                            stageFactoryIds.UnionWith(_blueprintUnitIds);
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
                    var eligibilityReadErrors = new List<string>();
                    try { allowedAsBlueprint = (bool)_allowedBlueprintField.GetValue(unit.BalanceRow); }
                    catch { eligibilityReadErrors.Add("AllowedAsBlueprint"); }
                    try { allowedAsStartingBlueprint = (bool)_allowedStartingBlueprintField.GetValue(unit.BalanceRow); }
                    catch { eligibilityReadErrors.Add("AllowedAsStartingBlueprint"); }
                    try { allowedForAi = (bool)_allowedForAiField.GetValue(unit.BalanceRow); }
                    catch { eligibilityReadErrors.Add("AllowedForAi"); }
                    try { allowedForDemo = (bool)_allowedForDemoField.GetValue(unit.BalanceRow); }
                    catch { eligibilityReadErrors.Add("AllowedForDemo"); }
                    try { isForSpecialists = (bool)_isForSpecialistsField.GetValue(unit.BalanceRow); }
                    catch { eligibilityReadErrors.Add("IsForSpecialists"); }
                    // Count declarations on the loaded product only; no loadout is selected or applied here.
                    int variantCount = !unit.IsProductionBuilding && unit.Definition != null &&
                        unit.Definition.variants != null ? unit.Definition.variants.Length : 0;
                    units.Add(new UnitDiagnosticReport(
                        unit.Id, unit.PackId, unit.PrefabKey, unit.ImageKey, allowedAsBlueprint,
                        allowedAsStartingBlueprint, allowedForAi, allowedForDemo, isForSpecialists,
                        String.Join(", ", eligibilityReadErrors), variantCount));
                }
                units.Sort((left, right) => StringComparer.Ordinal.Compare(left.UnitId, right.UnitId));

                var resolvedResourceKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, UnityEngine.Object> resource in _resourceAssets)
                {
                    if (!String.IsNullOrWhiteSpace(resource.Key) && resource.Value != null)
                    {
                        resolvedResourceKeys.Add(resource.Key);
                    }
                }

                var visualRequests = new HashSet<string>(StringComparer.Ordinal);
                var resolvedVisuals = new HashSet<string>(StringComparer.Ordinal);
                foreach (LoadedUnit unit in _units.Values)
                {
                    if (unit.Definition != null &&
                        !String.IsNullOrWhiteSpace(unit.Definition.nativeTemplateId) &&
                        !String.IsNullOrWhiteSpace(unit.Definition.visualAssetName))
                    {
                        visualRequests.Add(unit.Id);
                        // A unit is committed only after its requested native-template visual has loaded and validated.
                        resolvedVisuals.Add(unit.Id);
                    }
                }

                IReadOnlyList<UnitEligibilityDiagnosticReport> unitEligibilities =
                    LoaderUnitDiagnosticsBuilder.CreateEligibilityReports(units);
                var unitDefinitionsById = new Dictionary<string, ContentUnitDefinition>(StringComparer.Ordinal);
                foreach (LoadedUnit unit in _units.Values)
                {
                    if (unit != null && !String.IsNullOrWhiteSpace(unit.Id))
                    {
                        unitDefinitionsById[unit.Id] = unit.Definition;
                    }
                }
                IReadOnlyList<UnitLocalizationCoverageDiagnosticReport> unitLocalizationCoverage =
                    LoaderUnitDiagnosticsBuilder.CreateLocalizationCoverageReports(
                        units,
                        id =>
                        {
                            ContentUnitDefinition definition;
                            if (!unitDefinitionsById.TryGetValue(id, out definition) ||
                                definition == null || definition.localizations == null)
                            {
                                return Array.Empty<string>();
                            }

                            var languages = new SortedSet<string>(StringComparer.Ordinal);
                            foreach (ContentUnitLocalizationDefinition localization in definition.localizations)
                            {
                                if (localization != null && !String.IsNullOrWhiteSpace(localization.language))
                                {
                                    languages.Add(localization.language);
                                }
                            }
                            return languages.ToArray();
                        },
                        id =>
                        {
                            ContentUnitDefinition definition;
                            return unitDefinitionsById.TryGetValue(id, out definition) &&
                                definition != null && !String.IsNullOrWhiteSpace(definition.nativeTemplateId);
                        });
                IReadOnlyList<UnitAssetResolutionDiagnosticReport> unitAssetResolutions = null;

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

                var unitFactories = new List<UnitFactoryDiagnosticReport>(_units.Count);
                foreach (LoadedUnit unit in _units.Values)
                {
                    string factoryId = unit.ProductionFactoryId ?? string.Empty;
                    bool isLoaded = !string.IsNullOrEmpty(factoryId) &&
                        (_units.ContainsKey(factoryId) || _nativeUnitIds.Contains(factoryId));
                    string status = unit.IsProductionBuilding
                        ? "Production building."
                        : (isLoaded ? "Factory loaded." : "Factory missing.");
                    unitFactories.Add(new UnitFactoryDiagnosticReport(unit.Id, factoryId, isLoaded, status));
                }
                unitFactories.Sort((left, right) => StringComparer.Ordinal.Compare(left.UnitId, right.UnitId));

                unitAssetResolutions = LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                    units, resolvedResourceKeys, visualRequests, resolvedVisuals);

                var stagePoolScopes = new List<StagePoolScopeDiagnosticReport>();
                foreach (LoadedStage stage in _stages)
                {
                    foreach (BlueprintPoolScopeRegistration scope in stage.BlueprintPoolScopes)
                    {
                        stagePoolScopes.Add(new StagePoolScopeDiagnosticReport(
                            stage.Definition.id,
                            scope.PoolId,
                            scope.UnitIds));
                    }
                }
                stagePoolScopes.Sort((left, right) =>
                {
                    int cmp = StringComparer.Ordinal.Compare(left.StageId, right.StageId);
                    if (cmp != 0) return cmp;
                    return StringComparer.Ordinal.Compare(left.PoolId, right.PoolId);
                });

                var aiProfileStageRanges = new List<AiProfileStageRangeDiagnosticReport>();
                foreach (LoadedAiProfile profile in _aiProfiles.Values)
                {
                    if (profile.ScopeToReferencedStages)
                    {
                        aiProfileStageRanges.Add(new AiProfileStageRangeDiagnosticReport(
                            profile.Id,
                            profile.StageMin,
                            profile.StageMax,
                            true));
                    }
                }
                aiProfileStageRanges.Sort((left, right) => StringComparer.Ordinal.Compare(left.ProfileId, right.ProfileId));

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
                    unitFactories,
                    stagePoolScopes,
                    aiProfileStageRanges,
                    unitEligibilities,
                    unitAssetResolutions,
                    unitLocalizationCoverage,
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

                var candidateUnits = new List<ContentUnitDefinition>();
                foreach (var candidate in _packCandidates)
                {
                    if (candidate == null || candidate.Disabled || !candidate.LoadSucceeded ||
                        candidate.ValidatedDefinition == null)
                    {
                        continue;
                    }

                    if (candidate.ValidatedDefinition.units == null)
                    {
                        continue;
                    }

                    candidateUnits.AddRange(candidate.ValidatedDefinition.units);
                }

                var registeredUnitIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (LoadedUnit unit in _units.Values)
                {
                    if (unit != null && !unit.IsProductionBuilding && !String.IsNullOrWhiteSpace(unit.Id))
                    {
                        registeredUnitIds.Add(unit.Id);
                    }
                }

                return UnitBalanceOverrideMapper.CreateBalanceOverridesForLoadedUnits(
                    candidateUnits, registeredUnitIds);
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
                // Apply AI overrides from units referenced in this profile
                if (profile.Definition.unitEntries != null)
                {
                    foreach (var entry in profile.Definition.unitEntries)
                    {
                        if (entry == null || String.IsNullOrWhiteSpace(entry.unitId))
                        {
                            continue;
                        }
                        LoadedUnit unit;
                        if (_units.TryGetValue(entry.unitId, out unit) && unit != null &&
                            unit.Definition != null && unit.Definition.aiOverrides != null)
                        {
                            foreach (var ovr in unit.Definition.aiOverrides)
                            {
                                if (ovr == null || String.IsNullOrWhiteSpace(ovr.parameter))
                                {
                                    continue;
                                }
                                if (_mutationManager.MutateActiveAiBehaviour(behaviour, ovr.parameter, ovr.value))
                                {
                                    _mutationSuccessCount++;
                                }
                                else
                                {
                                    _mutationFailureCount++;
                                }
                            }
                        }
                    }
                }
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
            }
        }

        public void OnAiBehaviourDestroyed(object gameBehaviour)
        {
            if (gameBehaviour == null)
            {
                return;
            }

            BoundAiRuntimeBehaviour bound;
            lock (_gate)
            {
                if (!_boundAiBehaviours.TryGetValue(gameBehaviour, out bound))
                {
                    return;
                }

                _boundAiBehaviours.Remove(gameBehaviour);
            }

            SafeDestroyAiRuntimeBehaviour(bound, "<AI>");
        }

        /// <summary>
        /// Cleans compiled module state when the plugin component is destroyed. Loaded packs and
        /// Unity assets remain game-session registrations; this does not promise hot-unload support.
        /// </summary>
        public void ShutdownRuntimeBehaviours()
        {
            var behaviours = new List<object>();
            lock (_gate)
            {
                foreach (KeyValuePair<object, BoundAiRuntimeBehaviour> binding in _boundAiBehaviours)
                {
                    behaviours.Add(binding.Key);
                }
            }

            for (int i = 0; i < behaviours.Count; i++)
            {
                OnAiBehaviourDestroyed(behaviours[i]);
            }
        }

        private void RemoveAiBehaviourBindingIfCurrentLocked(
            DefaultAiBehaviour behaviour,
            BoundAiRuntimeBehaviour expected)
        {
            BoundAiRuntimeBehaviour current;
            if (_boundAiBehaviours.TryGetValue(behaviour, out current) &&
                System.Object.ReferenceEquals(current, expected))
            {
                _boundAiBehaviours.Remove(behaviour);
            }
        }

        private bool TryGetAiProfile(DefaultAiBehaviour behaviour, out LoadedAiProfile profile)
        {
            profile = null;
            // LoadScene sets the spawned instance's AI tag after Instantiate and before
            // the first Update-driven Init hook; the prefab asset itself need not carry it.
            if (behaviour == null || behaviour.gameObject == null ||
                !String.Equals(behaviour.gameObject.tag, "AI", StringComparison.Ordinal))
            {
                return false;
            }

            string aiResourceName = behaviour.gameObject.name;
            const string unityCloneSuffix = "(Clone)";
            if (aiResourceName.EndsWith(unityCloneSuffix, StringComparison.Ordinal))
            {
                aiResourceName = aiResourceName.Substring(0, aiResourceName.Length - unityCloneSuffix.Length);
            }

            lock (_gate)
            {
                return _aiProfilesByResourceName.TryGetValue(aiResourceName, out profile);
            }
        }

        private void SafeDestroyAiRuntimeBehaviour(BoundAiRuntimeBehaviour bound, string profileId)
        {
            if (bound == null)
            {
                return;
            }

            lock (_gate)
            {
                if (bound.Destroyed)
                {
                    return;
                }

                bound.Destroyed = true;
            }

            if (bound.RuntimeBehaviour == null)
            {
                return;
            }

            try
            {
                bound.RuntimeBehaviour.OnDestroyed();
            }
            catch (Exception exception)
            {
                Log("El módulo AI no pudo limpiar la instancia " + profileId + ": " + exception.Message);
            }
        }

        private void RefreshNativeUnitIds()
        {
            _nativeUnitIds.Clear();
            _nativeFactoryIds.Clear();
            ClearMinAttackRanges();
            IDictionary index = _entityIndexField.GetValue(null) as IDictionary;
            if (index == null)
            {
                if (!_registryUnavailableLogged)
                {
                    Log("El índice nativo de unidades aún no está inicializado; se aplaza la carga de paquetes.");
                    _registryUnavailableLogged = true;
                }

                _nativeRegistryReady = false;
                return;
            }

            _nativeRegistryReady = true;
            _registryUnavailableLogged = false;
            IList rows = _nativeEntityRowsField.GetValue(null) as IList;

            foreach (object key in index.Keys)
            {
                string id = key as string;
                if (!String.IsNullOrWhiteSpace(id))
                {
                    _nativeUnitIds.Add(id);
                    try
                    {
                        if (rows == null || !index.Contains(id))
                        {
                            continue;
                        }

                        int rowIndex = Convert.ToInt32(index[id], CultureInfo.InvariantCulture);
                        if (rowIndex < 0 || rowIndex >= rows.Count)
                        {
                            continue;
                        }

                        object row = rows[rowIndex];
                        if (row == null || row.GetType() != _entityRowType)
                        {
                            continue;
                        }

                        object nullableString = _factoryForEntityIdField.GetValue(row);
                        if (nullableString != null &&
                            (bool)_nullableStringHasValueField.GetValue(nullableString))
                        {
                            _nativeFactoryIds.Add(id);
                        }
                    }
                    catch (Exception exception)
                    {
                        Log("No se pudo clasificar el ID nativo " + id + " como producto o fábrica: " +
                            exception.GetBaseException().Message);
                    }
                }
            }
        }

        private async Task LoadAllPacksAsync()
        {
            if (_loaded || !_nativeRegistryReady)
            {
                return;
            }

            _loaded = true;
            _packRootError = null;
            _packCandidates.Clear();
            try
            {
                if (!HasNoReparsePointsInPath(_packsRoot, out string rootFailure))
                {
                    throw new InvalidDataException("La raíz de paquetes no es segura: " + rootFailure);
                }

                Directory.CreateDirectory(_packsRoot);
                if (!HasNoReparsePointsInPath(_packsRoot, out rootFailure))
                {
                    throw new InvalidDataException("La raíz de paquetes dejó de ser segura: " + rootFailure);
                }

                using (SafeFileHandle rootHandle = OpenDirectoryHandle(_packsRoot, out rootFailure))
                {
                    if (rootHandle == null)
                    {
                        throw new InvalidDataException("No se pudo fijar la raíz de paquetes: " + rootFailure);
                    }

                    _resolvedPacksRoot = GetFinalPath(rootHandle);
                }

                string[] packageDirectories = Directory.GetDirectories(_packsRoot);
                Array.Sort(packageDirectories, StringComparer.OrdinalIgnoreCase);
                var candidates = new List<PackCandidate>();
                for (int i = 0; i < packageDirectories.Length; i++)
                {
                    ContentPackDefinition definition;
                    string packageName = Path.GetFileName(packageDirectories[i]);
                    string readFailure;
                    if (TryReadPackDefinition(packageDirectories[i], packageName, out definition, out readFailure))
                    {
                        var candidate = new PackCandidate(packageDirectories[i], packageName, definition);
                        _packCandidates.Add(candidate);
                        candidates.Add(candidate);
                    }
                    else
                    {
                        _packCandidates.Add(new PackCandidate(packageDirectories[i], packageName, null)
                        {
                            Disabled = true,
                            StatusReason = readFailure
                        });
                    }
                }

                DisableCrossPackIdCollisions(candidates);
                Dictionary<string, PackCandidate> unitOwners;
                Dictionary<string, PackCandidate> profileOwners;
                HashSet<string> declaredUnitIds;
                HashSet<string> declaredProfileIds;
                BuildDeclaredContentIndex(candidates, out unitOwners, out profileOwners,
                    out declaredUnitIds, out declaredProfileIds);

                for (int i = 0; i < candidates.Count; i++)
                {
                    PackCandidate candidate = candidates[i];
                    if (candidate.Disabled)
                    {
                        continue;
                    }

                    ContentPackValidationResult validation = ContentPackValidator.Validate(
                        candidate.Definition, candidate.Root,
                        new RegistrySnapshot(this, declaredUnitIds, declaredProfileIds), _moduleRegistry);
                    if (!validation.IsValid)
                    {
                        candidate.Disabled = true;
                        var messages = new string[validation.Diagnostics.Count];
                        for (int diagnosticIndex = 0; diagnosticIndex < validation.Diagnostics.Count; diagnosticIndex++)
                        {
                            messages[diagnosticIndex] = validation.Diagnostics[diagnosticIndex].ToString();
                        }
                        candidate.StatusReason = String.Join(" | ", messages);
                        LogDiagnostics(candidate.Name, validation.Diagnostics);
                        continue;
                    }

                    candidate.ValidatedDefinition = validation.Pack;
                }

                DisableCrossPackUnitGraphViolations(candidates);
                for (int i = 0; i < candidates.Count; i++)
                {
                    PackCandidate candidate = candidates[i];
                    if (!candidate.Disabled && candidate.ValidatedDefinition != null)
                    {
                        ResolvePackDependencies(candidate, unitOwners, profileOwners);
                    }
                }

                await LoadPacksInDependencyOrderAsync(candidates);

                int productionBuildingCount = 0;
                int productUnitCount;
                int contributionCount;
                lock (_gate)
                {
                    foreach (LoadedUnit unit in _units.Values)
                    {
                        if (unit.IsProductionBuilding)
                        {
                            productionBuildingCount++;
                        }
                    }

                    productUnitCount = _units.Count - productionBuildingCount;
                    contributionCount = _contributions.Count;
                }

                Log("Carga finalizada: " + contributionCount + " paquete(s) de progresión y " +
                    (productUnitCount + productionBuildingCount) + " entidad(es) nuevas (" + productUnitCount + " unidad(es) y " +
                    productionBuildingCount + " edificio(s) de producción).");
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _loaded = false;
                    _packRootError = exception.GetType().Name + ": " + exception.Message;
                }
                Log("No se pudo recorrer la carpeta de paquetes: " + _packRootError);
            }
        }

        private bool TryReadPackDefinition(
            string packageRoot,
            string packageName,
            out ContentPackDefinition definition,
            out string diagnostic)
        {
            definition = null;
            diagnostic = null;
            string manifestPath = Path.Combine(packageRoot, "content.json");
            if (!IsRegularPackageFile(packageRoot, manifestPath, out string pathFailure))
            {
                diagnostic = pathFailure;
                Log("Paquete '" + packageName + "' desactivado: " + diagnostic);
                return false;
            }

            try
            {
                FileStream manifestStream;
                if (!TryOpenContainedFile(packageRoot, manifestPath, out manifestStream, out pathFailure))
                {
                    diagnostic = pathFailure;
                    Log("Paquete '" + packageName + "' desactivado: " + diagnostic);
                    return false;
                }

                using (var stream = manifestStream)
                {
                    if (stream.Length <= 0 || stream.Length > MaxManifestBytes)
                    {
                        diagnostic = "content.json está vacío o supera 1 MiB.";
                        Log("Paquete '" + packageName + "' desactivado: " + diagnostic);
                        return false;
                    }

                    using (var reader = new StreamReader(stream))
                    {
                        definition = JsonConvert.DeserializeObject<ContentPackDefinition>(reader.ReadToEnd());
                    }
                }

                if (definition == null)
                {
                    diagnostic = "content.json no contiene un objeto de paquete.";
                    Log("Paquete '" + packageName + "' desactivado: " + diagnostic);
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "no se pudo leer content.json: " + exception.Message;
                Log("Paquete '" + packageName + "' desactivado: " + diagnostic);
                return false;
            }
        }

        private void DisableCrossPackIdCollisions(List<PackCandidate> candidates)
        {
            var claims = new Dictionary<string, List<PackCandidate>>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                PackCandidate candidate = candidates[i];
                var localIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in EnumerateDefinitionIds(candidate.Definition))
                {
                    if (String.IsNullOrWhiteSpace(id) || !localIds.Add(id))
                    {
                        continue;
                    }

                    List<PackCandidate> owners;
                    if (!claims.TryGetValue(id, out owners))
                    {
                        owners = new List<PackCandidate>();
                        claims.Add(id, owners);
                    }

                    owners.Add(candidate);
                }
            }

            foreach (KeyValuePair<string, List<PackCandidate>> claim in claims)
            {
                if (claim.Value.Count < 2)
                {
                    continue;
                }

                for (int i = 0; i < claim.Value.Count; i++)
                {
                    PackCandidate candidate = claim.Value[i];
                    if (!candidate.Disabled)
                    {
                        candidate.Disabled = true;
                        candidate.StatusReason = "el ID '" + claim.Key + "' también está declarado por otro paquete.";
                        Log("Paquete '" + candidate.Name + "' desactivado: " + candidate.StatusReason);
                    }
                }
            }
        }

        private void DisableCrossPackUnitGraphViolations(List<PackCandidate> candidates)
        {
            var validatedCandidates = new List<PackCandidate>();
            var validatedPacks = new List<ContentPackDefinition>();
            for (int i = 0; i < candidates.Count; i++)
            {
                PackCandidate candidate = candidates[i];
                if (candidate == null || candidate.Disabled || candidate.ValidatedDefinition == null)
                {
                    continue;
                }

                validatedCandidates.Add(candidate);
                validatedPacks.Add(candidate.ValidatedDefinition);
            }

            IReadOnlyList<CrossPackUnitGraphIssue> issues =
                ContentPackValidator.ValidateCrossPackUnitReferenceGraphs(validatedPacks);
            for (int issueIndex = 0; issueIndex < issues.Count; issueIndex++)
            {
                CrossPackUnitGraphIssue issue = issues[issueIndex];
                PackCandidate owner = null;
                int ownerCount = 0;
                for (int candidateIndex = 0; candidateIndex < validatedCandidates.Count; candidateIndex++)
                {
                    PackCandidate candidate = validatedCandidates[candidateIndex];
                    if (String.Equals(candidate.ValidatedDefinition.packId, issue.PackId, StringComparison.Ordinal))
                    {
                        owner = candidate;
                        ownerCount++;
                    }
                }

                if (ownerCount != 1 || owner == null)
                {
                    string failure = "No se pudo resolver de forma única el paquete propietario del diagnóstico " +
                        issue.PackId + "; se desactivan todos los paquetes validados por seguridad.";
                    for (int candidateIndex = 0; candidateIndex < validatedCandidates.Count; candidateIndex++)
                    {
                        PackCandidate candidate = validatedCandidates[candidateIndex];
                        candidate.Disabled = true;
                        candidate.StatusReason = AppendStatusReason(candidate.StatusReason, failure);
                        Log("Paquete '" + candidate.Name + "' desactivado: " + failure);
                    }
                    return;
                }

                owner.Disabled = true;
                owner.StatusReason = AppendStatusReason(owner.StatusReason, issue.Diagnostic.ToString());
                Log("Paquete '" + owner.Name + "' desactivado: " + issue.Diagnostic);
            }
        }

        private static string AppendStatusReason(string currentReason, string additionalReason)
        {
            if (String.IsNullOrWhiteSpace(currentReason))
            {
                return additionalReason;
            }

            return currentReason + " | " + additionalReason;
        }

        private static IEnumerable<string> EnumerateDefinitionIds(ContentPackDefinition definition)
        {
            if (definition == null)
            {
                yield break;
            }

            yield return definition.packId;
            if (definition.assetBundles != null)
            {
                for (int i = 0; i < definition.assetBundles.Length; i++)
                {
                    if (definition.assetBundles[i] != null) yield return definition.assetBundles[i].id;
                }
            }

            if (definition.units != null)
            {
                int unitCount = definition.units == null ? 0 : definition.units.Length;
                for (int i = 0; i < unitCount; i++)
                {
                    ContentUnitDefinition unit = definition.units[i];
                    if (unit == null)
                    {
                        continue;
                    }

                    yield return unit.id;
                    if (unit.productionBuilding != null)
                    {
                        yield return unit.productionBuilding.id;
                    }
                }
            }

            if (definition.buildings != null)
            {
                for (int i = 0; i < definition.buildings.Length; i++)
                {
                    ContentBuildingDefinition building = definition.buildings[i];
                    if (building != null && !String.IsNullOrWhiteSpace(building.id))
                    {
                        yield return building.id;
                    }
                }
            }

            if (definition.stages != null)
            {
                for (int i = 0; i < definition.stages.Length; i++)
                {
                    if (definition.stages[i] != null) yield return definition.stages[i].id;
                }
            }

            if (definition.aiProfiles != null)
            {
                for (int i = 0; i < definition.aiProfiles.Length; i++)
                {
                    if (definition.aiProfiles[i] != null) yield return definition.aiProfiles[i].id;
                }
            }
        }

        private static void BuildDeclaredContentIndex(
            List<PackCandidate> candidates,
            out Dictionary<string, PackCandidate> unitOwners,
            out Dictionary<string, PackCandidate> profileOwners,
            out HashSet<string> declaredUnitIds,
            out HashSet<string> declaredProfileIds)
        {
            var unitClaims = new Dictionary<string, List<PackCandidate>>(StringComparer.Ordinal);
            var profileClaims = new Dictionary<string, List<PackCandidate>>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                PackCandidate candidate = candidates[i];
                if (candidate.Definition.units != null)
                {
                    for (int unitIndex = 0; unitIndex < candidate.Definition.units.Length; unitIndex++)
                    {
                        ContentUnitDefinition unit = candidate.Definition.units[unitIndex];
                        if (unit != null) AddClaim(unitClaims, unit.id, candidate);
                    }
                }

                if (candidate.Definition.aiProfiles != null)
                {
                    for (int profileIndex = 0; profileIndex < candidate.Definition.aiProfiles.Length; profileIndex++)
                    {
                        ContentAiProfileDefinition profile = candidate.Definition.aiProfiles[profileIndex];
                        if (profile != null) AddClaim(profileClaims, profile.id, candidate);
                    }
                }
            }

            unitOwners = GetUnambiguousOwners(unitClaims);
            profileOwners = GetUnambiguousOwners(profileClaims);
            declaredUnitIds = new HashSet<string>(unitOwners.Keys, StringComparer.Ordinal);
            declaredProfileIds = new HashSet<string>(profileOwners.Keys, StringComparer.Ordinal);
        }

        private static void AddClaim(
            Dictionary<string, List<PackCandidate>> claims,
            string id,
            PackCandidate candidate)
        {
            if (String.IsNullOrWhiteSpace(id))
            {
                return;
            }

            List<PackCandidate> owners;
            if (!claims.TryGetValue(id, out owners))
            {
                owners = new List<PackCandidate>();
                claims.Add(id, owners);
            }

            if (!owners.Contains(candidate))
            {
                owners.Add(candidate);
            }
        }

        private static Dictionary<string, PackCandidate> GetUnambiguousOwners(
            Dictionary<string, List<PackCandidate>> claims)
        {
            var owners = new Dictionary<string, PackCandidate>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<PackCandidate>> claim in claims)
            {
                if (claim.Value.Count == 1)
                {
                    owners.Add(claim.Key, claim.Value[0]);
                }
            }

            return owners;
        }

        private static void ResolvePackDependencies(
            PackCandidate candidate,
            Dictionary<string, PackCandidate> unitOwners,
            Dictionary<string, PackCandidate> profileOwners)
        {
            ContentStageDefinition[] stages = candidate.ValidatedDefinition.stages;
            for (int i = 0; i < stages.Length; i++)
            {
                ContentStageDefinition stage = stages[i];
                if (stage == null)
                {
                    continue;
                }

                string[] unitIds = stage.unitIds ?? new string[0];
                for (int j = 0; j < unitIds.Length; j++)
                {
                    PackCandidate owner;
                    if (unitOwners.TryGetValue(unitIds[j], out owner) && !System.Object.ReferenceEquals(owner, candidate))
                    {
                        candidate.Dependencies.Add(owner);
                    }
                }

                string[] profileIds = stage.aiProfileIds ?? new string[0];
                for (int j = 0; j < profileIds.Length; j++)
                {
                    PackCandidate owner;
                    if (profileOwners.TryGetValue(profileIds[j], out owner) && !System.Object.ReferenceEquals(owner, candidate))
                    {
                        candidate.Dependencies.Add(owner);
                    }
                }
            }

            ContentAiProfileDefinition[] profiles = candidate.ValidatedDefinition.aiProfiles;
            for (int i = 0; i < profiles.Length; i++)
            {
                ContentAiProfileDefinition profile = profiles[i];
                if (profile == null)
                {
                    continue;
                }

                string[] unitDependencies = profile.unitDependencies ?? new string[0];
                for (int j = 0; j < unitDependencies.Length; j++)
                {
                    string unitId = unitDependencies[j];
                    if (String.IsNullOrWhiteSpace(unitId))
                    {
                        continue;
                    }

                    PackCandidate owner;
                    if (unitOwners.TryGetValue(unitId, out owner) &&
                        !System.Object.ReferenceEquals(owner, candidate))
                    {
                        candidate.Dependencies.Add(owner);
                    }
                }

                ContentAiUnitEntryDefinition[] unitEntries = profile.unitEntries ??
                    new ContentAiUnitEntryDefinition[0];
                for (int j = 0; j < unitEntries.Length; j++)
                {
                    ContentAiUnitEntryDefinition unitEntry = unitEntries[j];
                    if (unitEntry == null || String.IsNullOrWhiteSpace(unitEntry.unitId))
                    {
                        continue;
                    }

                    PackCandidate owner;
                    if (unitOwners.TryGetValue(unitEntry.unitId, out owner) &&
                        !System.Object.ReferenceEquals(owner, candidate))
                    {
                        candidate.Dependencies.Add(owner);
                    }
                }

                ContentAiProductionBuildingEntryDefinition[] buildingEntries =
                    profile.productionBuildingEntries ?? new ContentAiProductionBuildingEntryDefinition[0];
                for (int j = 0; j < buildingEntries.Length; j++)
                {
                    ContentAiProductionBuildingEntryDefinition buildingEntry = buildingEntries[j];
                    if (buildingEntry == null || String.IsNullOrWhiteSpace(buildingEntry.unitId))
                    {
                        continue;
                    }

                    PackCandidate owner;
                    if (unitOwners.TryGetValue(buildingEntry.unitId, out owner) &&
                        !System.Object.ReferenceEquals(owner, candidate))
                    {
                        candidate.Dependencies.Add(owner);
                    }
                }
            }
        }

        private async Task LoadPacksInDependencyOrderAsync(List<PackCandidate> candidates)
        {
            var pending = new List<PackCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].Disabled && candidates[i].ValidatedDefinition != null)
                {
                    pending.Add(candidates[i]);
                }
            }

            while (pending.Count > 0)
            {
                bool advanced = false;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    PackCandidate candidate = pending[i];
                    PackCandidate failedDependency = null;
                    bool waitingForDependency = false;
                    foreach (PackCandidate dependency in candidate.Dependencies)
                    {
                        if (dependency.Disabled || dependency.ValidatedDefinition == null ||
                            dependency.LoadAttempted && !dependency.LoadSucceeded)
                        {
                            failedDependency = dependency;
                            break;
                        }

                        if (!dependency.LoadSucceeded)
                        {
                            waitingForDependency = true;
                        }
                    }

                    if (failedDependency != null)
                    {
                        lock (_gate)
                        {
                            candidate.Disabled = true;
                            candidate.StatusReason = "depende del paquete '" + failedDependency.Name + "', que no pudo cargarse.";
                        }
                        Log("Paquete '" + candidate.Name + "' desactivado: " + candidate.StatusReason);
                        pending.RemoveAt(i);
                        advanced = true;
                        continue;
                    }

                    if (waitingForDependency)
                    {
                        continue;
                    }

                    lock (_gate)
                    {
                        candidate.LoadAttempted = true;
                    }

                    bool loadSucceeded = await LoadOnePackAsync(candidate);
                    lock (_gate)
                    {
                        candidate.LoadSucceeded = loadSucceeded;
                        if (!loadSucceeded)
                        {
                            candidate.Disabled = true;
                        }
                        else
                        {
                            candidate.StatusReason = null;
                        }
                    }

                    pending.RemoveAt(i);
                    advanced = true;
                }

                if (!advanced)
                {
                    lock (_gate)
                    {
                        for (int i = 0; i < pending.Count; i++)
                        {
                            pending[i].Disabled = true;
                            pending[i].StatusReason = "se detectó una dependencia circular entre paquetes.";
                        }
                    }

                    for (int i = 0; i < pending.Count; i++)
                    {
                        Log("Paquete '" + pending[i].Name + "' desactivado: " + pending[i].StatusReason);
                    }

                    break;
                }
            }
        }

        private async Task<bool> LoadOnePackAsync(PackCandidate candidate)
        {
            string packageRoot = candidate.Root;
            string packageName = candidate.Name;
            ContentPackDefinition definition = candidate.ValidatedDefinition;
            if (definition == null)
            {
                return false;
            }

            var temporaryBundles = new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
            var temporaryBundleStreams = new List<FileStream>();
            var temporaryResources = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
            var temporaryNativePrefabs = new List<GameObject>();
            var temporaryStageParameterClones = new List<ScriptableObject>();
            var temporaryBlueprintPoolClones = new List<ScriptableObject>();
            var temporaryLandscapeClones = new List<ScriptableObject>();
            var temporaryUnits = new List<LoadedUnit>();
            var temporaryStages = new List<LoadedStage>();
            var temporaryProfiles = new List<LoadedAiProfile>();
            HashSet<string> reservedBlueprintPoolIds = null;
            AssetBundle manifestBundle = null;
            FileStream manifestStream = null;
            string manifestBundleRegistryKey = null;
            long packBundleBytes = 0;
            string pathFailure;

            try
            {
                var loadOrder = new List<ContentAssetBundleDefinition>();
                if (definition.assetBundles.Length > 0)
                {
                    string manifestFullPath;
                    if (!TryResolveBundleFile(packageRoot, definition.assetBundleManifestPath,
                            out manifestFullPath, out pathFailure))
                    {
                        throw new InvalidDataException("Manifiesto de AssetBundles: " + pathFailure);
                    }

                    if (!TryOpenContainedFile(packageRoot, manifestFullPath, out manifestStream, out pathFailure))
                    {
                        throw new InvalidDataException("Manifiesto de AssetBundles: " + pathFailure);
                    }

                    ValidateBundleStreamSize(manifestStream, definition.assetBundleManifestPath,
                        ContentPackValidator.MaxManifestBundleBytes, "manifiesto");
                    manifestBundleRegistryKey = BundleRegistryKey(
                        definition.packId, "manifest:" + definition.assetBundleManifestPath);
                    manifestBundle = await _bundleRegistry.LoadBundleAsync(
                        manifestBundleRegistryKey, manifestStream);

                    if (manifestBundle == null)
                    {
                        throw new InvalidDataException("No se pudo abrir el AssetBundle de manifiesto '" +
                            definition.assetBundleManifestPath + "'.");
                    }

                    AssetBundleManifest unityManifest = manifestBundle.LoadAsset<AssetBundleManifest>(
                        "AssetBundleManifest");
                    if (unityManifest == null)
                    {
                        throw new InvalidDataException("El bundle '" + definition.assetBundleManifestPath +
                            "' no contiene AssetBundleManifest.");
                    }

                    loadOrder = GetBundleLoadOrder(unityManifest, definition.assetBundles, packageName);
                    _bundleRegistry.RemoveBundle(manifestBundleRegistryKey, manifestBundle);
                    manifestBundle.Unload(true);
                    manifestBundle = null;
                    manifestStream.Dispose();
                    manifestStream = null;
                }

                for (int i = 0; i < loadOrder.Count; i++)
                {
                    ContentAssetBundleDefinition bundleDefinition = loadOrder[i];
                    string fullPath;
                    if (!TryResolveBundleFile(packageRoot, bundleDefinition.path, out fullPath, out pathFailure))
                    {
                        throw new InvalidDataException(pathFailure);
                    }

                    FileStream stream;
                    if (!TryOpenContainedFile(packageRoot, fullPath, out stream, out pathFailure))
                    {
                        _log?.Invoke("Warning: Bundle file missing at '" + fullPath + "'; attempting global fallback if configured.");

                        // Implement global-fallback mechanism
                        string fallbackPath = GetGlobalFallbackBundlePath();
                        if (!String.IsNullOrWhiteSpace(fallbackPath) && TryOpenContainedFile(packageRoot, fallbackPath, out stream, out pathFailure))
                        {
                            _log?.Invoke("Successfully loaded fallback bundle: " + fallbackPath);
                        }
                        else
                        {
                            throw new InvalidDataException(pathFailure);
                        }
                    }

                    temporaryBundleStreams.Add(stream);
                    long bundleBytes = ValidateBundleStreamSize(stream, bundleDefinition.path,
                        ContentPackValidator.MaxAssetBundleBytes, "AssetBundle");
                    if (_loadedAssetBundleBytes > ContentPackValidator.MaxTotalContentBundleBytes ||
                        packBundleBytes > ContentPackValidator.MaxTotalContentBundleBytes - _loadedAssetBundleBytes ||
                        bundleBytes > ContentPackValidator.MaxTotalContentBundleBytes -
                            _loadedAssetBundleBytes - packBundleBytes)
                    {
                        throw new InvalidDataException("La carga de contenido superaría el límite global de " +
                            ContentPackValidator.MaxTotalContentBundleBytes + " bytes en AssetBundles.");
                    }

                    packBundleBytes += bundleBytes;
                    AssetBundle bundle = await _bundleRegistry.LoadBundleAsync(
                        BundleRegistryKey(definition.packId, bundleDefinition.id), stream);
                    if (bundle == null)
                    {
                        throw new InvalidDataException("AssetBundle no válido: " + bundleDefinition.path);
                    }

                    temporaryBundles.Add(bundleDefinition.id, bundle);
                }

                for (int i = 0; i < definition.units.Length; i++)
                {
                    ContentUnitDefinition unit = definition.units[i];
                    if (unit.productionBuilding == null)
                    {
                        throw new InvalidDataException("La unidad " + unit.id + " en '" + packageName +
                            "' debe declarar productionBuilding.");
                    }

                    bool usesNativeTemplate = !String.IsNullOrWhiteSpace(unit.nativeTemplateId);
                    object row;
                    string prefabKey;
                    string imageKey;
                    if (usesNativeTemplate)
                    {
                        row = CopyNativeBalanceRow(unit.nativeTemplateId, packageName, unit.id);
                        string nativePrefabPath = _prefabLocationField.GetValue(row) as string;
                        string nativeImagePath = _imageLocationField.GetValue(row) as string;
                        if (String.IsNullOrWhiteSpace(nativePrefabPath) || String.IsNullOrWhiteSpace(nativeImagePath))
                        {
                            throw new InvalidDataException("La unidad plantilla '" + unit.nativeTemplateId +
                                "' no contiene rutas nativas de prefab e imagen válidas.");
                        }

                        GameObject nativeTemplate = Resources.Load<GameObject>(nativePrefabPath);
                        if (nativeTemplate == null)
                        {
                            nativeTemplate = Resources.Load<GameObject>(nativePrefabPath.ToLowerInvariant());
                        }

                        if (nativeTemplate == null)
                        {
                            throw new InvalidDataException("La unidad " + unit.id + " no pudo cargar el prefab nativo '" +
                                nativePrefabPath + "' de la plantilla " + unit.nativeTemplateId + ".");
                        }
                        ValidateUnitPrefab(nativeTemplate, unit, packageName, false);

                        GameObject visualTemplate = null;
                        if (!String.IsNullOrWhiteSpace(unit.visualAssetName))
                        {
                            AssetBundle visualBundle = GetBundle(temporaryBundles, unit.assetBundleId, packageName);
                            visualTemplate = LoadRequiredAsset(visualBundle, unit.visualAssetName,
                                typeof(GameObject), "visual-only GameObject prefab", packageName) as GameObject;
                            ValidateVisualPrefab(visualTemplate, unit, packageName);
                        }

                        GameObject nativePrefabClone = InstantiateDormantPrefab(nativeTemplate);
                        if (nativePrefabClone == null)
                        {
                            throw new InvalidDataException("No se pudo clonar el prefab nativo de " + unit.id + ".");
                        }
                        temporaryNativePrefabs.Add(nativePrefabClone);
                        if (visualTemplate != null)
                        {
                            ApplyVisualOverlay(nativePrefabClone, visualTemplate, unit, packageName);
                        }

                        EntityController clonedController = nativePrefabClone.GetComponent<EntityController>();
                        if (clonedController == null)
                        {
                            throw new InvalidDataException("El clon nativo de " + unit.id +
                                " perdió su EntityController raíz.");
                        }

                        EntityControllerIdentity.Assign(_entityControllerIdField, clonedController, unit.id,
                            "Unidad " + unit.id + " en '" + packageName + "'");
                        ApplyEntityBehavioralParameters(nativePrefabClone, unit.rotationDegreesPerSecond, unit.collisionRadius,
                            unit.attackWarmUpTime, unit.canAttackWhileMoving, unit.shootAutomaticallyOnEnemiesWithinRange,
                            unit.defendPositionAggressivelyWhenIdle, unit.rangeToChooseNewTargetAutomatically, unit.minAttackRange, unit.id);
                        ApplyEntityActiveSkills(nativePrefabClone, unit.activeSkills, unit.id);
                        ApplyEntityCombatEnhancements(nativePrefabClone, unit.onHitStatusEffects, unit.criticalHitChance, unit.id);
                        AssetBundle audioBundle = !String.IsNullOrWhiteSpace(unit.assetBundleId)
                            ? GetBundle(temporaryBundles, unit.assetBundleId, packageName)
                            : null;
                        ApplyEntityAudioBindings(nativePrefabClone, audioBundle, unit.selectionSoundAssetName,
                            unit.engineSoundAssetName, unit.weaponSoundAssetName, unit.id, packageName);
                        EnsureMuzzleFlashHierarchy(nativePrefabClone);

                        nativePrefabClone.name = "RCL_Native_" + unit.id.Replace(':', '_');
                        nativePrefabClone.SetActive(false);
                        nativePrefabClone.hideFlags = HideFlags.HideAndDontSave;
                        UnityEngine.Object.DontDestroyOnLoad(nativePrefabClone);
                        prefabKey = ResourceKey(unit.id, "prefab");
                        temporaryResources.Add(prefabKey, nativePrefabClone);
                        imageKey = nativeImagePath;
                        if (!String.IsNullOrWhiteSpace(unit.cardImageAssetName))
                        {
                            AssetBundle cardBundle = GetBundle(temporaryBundles, unit.assetBundleId, packageName);
                            Sprite cardImage = LoadRequiredAsset(cardBundle, unit.cardImageAssetName,
                                typeof(Sprite), "Sprite card image", packageName) as Sprite;
                            imageKey = ResourceKey(unit.id, "image");
                            temporaryResources.Add(imageKey, cardImage);
                        }

                        ApplyBalanceOverrides(row, unit.balanceOverrides, packageName, unit.id, true, false);
                        _entityIdField.SetValue(row, unit.id);
                        _prefabLocationField.SetValue(row, prefabKey);
                        _imageLocationField.SetValue(row, imageKey);
                        _inactiveField.SetValue(row, false);
                        _allowedForAiField.SetValue(row, unit.isAllowedForAi);
                        _allowedForDemoField.SetValue(row, unit.isAllowedForDemo);
                        _isForSpecialistsField.SetValue(row, unit.isForSpecialists);
                        ApplyStructuredUnitParameters(row, unit);
                    }
                    else
                    {
                        AssetBundle bundle = GetBundle(temporaryBundles, unit.assetBundleId, packageName);
                        UnityEngine.Object balanceAsset = LoadRequiredAsset(bundle, unit.balanceAssetName,
                            _entityBalanceAssetType, "EntityBalancingScriptableObject", packageName);
                        var prefab = LoadRequiredAsset(bundle, unit.prefabAssetName,
                            typeof(GameObject), "GameObject prefab", packageName) as GameObject;
                        Sprite cardImage = LoadRequiredAsset(bundle, unit.cardImageAssetName,
                            typeof(Sprite), "Sprite card image", packageName) as Sprite;
                        ValidateUnitPrefab(prefab, unit, packageName, true);
                        row = ReadSingleBalanceRow(balanceAsset, packageName, unit.id);

                        GameObject customPrefabClone = InstantiateDormantPrefab(prefab);
                        if (customPrefabClone == null)
                        {
                            throw new InvalidDataException("No se pudo clonar el prefab propio de " + unit.id + ".");
                        }
                        temporaryNativePrefabs.Add(customPrefabClone);
                        EntityController customController = customPrefabClone.GetComponent<EntityController>();
                        EntityControllerIdentity.Assign(_entityControllerIdField, customController, unit.id,
                            "Unidad " + unit.id + " en '" + packageName + "'");
                        ApplyEntityBehavioralParameters(customPrefabClone, unit.rotationDegreesPerSecond, unit.collisionRadius,
                            unit.attackWarmUpTime, unit.canAttackWhileMoving, unit.shootAutomaticallyOnEnemiesWithinRange,
                            unit.defendPositionAggressivelyWhenIdle, unit.rangeToChooseNewTargetAutomatically, unit.minAttackRange, unit.id);
                        ApplyEntityActiveSkills(customPrefabClone, unit.activeSkills, unit.id);
                        ApplyEntityCombatEnhancements(customPrefabClone, unit.onHitStatusEffects, unit.criticalHitChance, unit.id);
                        ApplyEntityAudioBindings(customPrefabClone, bundle, unit.selectionSoundAssetName,
                            unit.engineSoundAssetName, unit.weaponSoundAssetName, unit.id, packageName);
                        EnsureMuzzleFlashHierarchy(customPrefabClone);

                        customPrefabClone.name = "RCL_Custom_" + unit.id.Replace(':', '_');
                        customPrefabClone.SetActive(false);
                        customPrefabClone.hideFlags = HideFlags.HideAndDontSave;
                        UnityEngine.Object.DontDestroyOnLoad(customPrefabClone);

                        prefabKey = ResourceKey(unit.id, "prefab");
                        imageKey = ResourceKey(unit.id, "image");
                        ApplyBalanceOverrides(row, unit.balanceOverrides, packageName, unit.id, true, false);
                        _entityIdField.SetValue(row, unit.id);
                        _prefabLocationField.SetValue(row, prefabKey);
                        _imageLocationField.SetValue(row, imageKey);
                        _inactiveField.SetValue(row, false);
                        _allowedForAiField.SetValue(row, unit.isAllowedForAi);
                        _allowedForDemoField.SetValue(row, unit.isAllowedForDemo);
                        _isForSpecialistsField.SetValue(row, unit.isForSpecialists);
                        ApplyStructuredUnitParameters(row, unit);

                        temporaryResources.Add(prefabKey, customPrefabClone);
                        temporaryResources.Add(imageKey, cardImage);
                    }

                    if (HasRoleFlag(row, "Factory") || HasRoleFlag(row, "Building"))
                    {
                        throw new InvalidDataException("La fila de unidad " + unit.id + " en '" + packageName +
                            "' tiene un rol de edificio; debe ser un producto. Declara la fábrica en productionBuilding.");
                    }

                    _factoryForEntityIdField.SetValue(row, CreateEmptyNullableString());
                    _allowedBlueprintField.SetValue(row, false);
                    _allowedStartingBlueprintField.SetValue(row, false);
                    int unitPrespawn = unit.prespawnCount ?? ((unit.systemTags != null && Array.Exists(unit.systemTags, t => String.Equals(t, "Spawn", StringComparison.OrdinalIgnoreCase))) || HasRoleFlag(row, "Spawn") ? 50 : 20);
                    LoadedUnit productUnit = new LoadedUnit(unit.id, definition.packId, prefabKey, imageKey,
                        row, unit, false, unit.productionBuilding.id, unitPrespawn, false);
                    temporaryUnits.Add(productUnit);
                    try
                    {
                        LoadedUnit factoryUnit = CreateProductionBuilding(unit, unit.productionBuilding, definition.packId,
                            packageName, temporaryBundles, temporaryNativePrefabs, temporaryResources);
                        if (factoryUnit == null)
                        {
                            temporaryUnits.Remove(productUnit);
                            throw new InvalidDataException("CreateProductionBuilding returned null for " + unit.id);
                        }
                        temporaryUnits.Add(factoryUnit);
                    }
                    catch
                    {
                        temporaryUnits.Remove(productUnit);
                        throw;
                    }
                }

                if (definition.buildings != null)
                {
                    for (int i = 0; i < definition.buildings.Length; i++)
                    {
                        ContentBuildingDefinition building = definition.buildings[i];
                        if (building != null)
                        {
                            temporaryUnits.Add(CreateStandaloneBuilding(building, definition.packId,
                                packageName, temporaryBundles, temporaryNativePrefabs, temporaryResources));
                        }
                    }
                }

                for (int i = 0; i < definition.stages.Length; i++)
                {
                    ContentStageDefinition stage = definition.stages[i];
                    UnityEngine.Object parameters;
                    UnityEngine.Object nativeSourceParameters = null;
                    if (!String.IsNullOrWhiteSpace(stage.nativeTemplateId))
                    {
                        UnityEngine.Object nativeParameters = FindNativeStageTemplate(
                            stage.nativeTemplateId, packageName, stage.id);
                        nativeSourceParameters = nativeParameters;
                        parameters = UnityEngine.Object.Instantiate(nativeParameters);
                        if (parameters == null || !_stageParametersType.IsInstanceOfType(parameters))
                        {
                            throw new InvalidDataException("No se pudo clonar la plantilla de etapa " +
                                stage.nativeTemplateId + " para " + stage.id + ".");
                        }

                        temporaryStageParameterClones.Add((ScriptableObject)parameters);
                    }
                    else
                    {
                        AssetBundle bundle = GetBundle(temporaryBundles, stage.assetBundleId, packageName);
                        UnityEngine.Object stageAsset = LoadRequiredAsset(bundle, stage.assetName,
                            _stageParametersType, "StageParametersScriptableObject", packageName);
                        parameters = UnityEngine.Object.Instantiate(stageAsset);
                        if (parameters == null || !_stageParametersType.IsInstanceOfType(parameters))
                        {
                            throw new InvalidDataException("No se pudo clonar el asset de etapa " +
                                stage.assetName + " para " + stage.id + ".");
                        }

                        temporaryStageParameterClones.Add((ScriptableObject)parameters);
                    }

                    parameters.name = "RCL_Stage_" + stage.id.Replace(':', '_');
                    parameters.hideFlags = HideFlags.HideAndDontSave;
                    ApplyStageParameterOverrides(parameters, stage.parameterOverrides, packageName, stage.id);
                    List<object> stageWorlds;
                    List<object> pools = ValidateStageParameters(parameters, packageName, stage.id,
                        out stageWorlds);
                    ScriptableObject stagePoolList = _stageParametersBlueprintPoolListField
                        .GetValue(parameters) as ScriptableObject;
                    IList poolList = stagePoolList == null
                        ? null
                        : _blueprintPoolsField.GetValue(stagePoolList) as IList;
                    if (poolList == null)
                    {
                        throw new InvalidDataException("La etapa " + stage.id + " tiene una lista de pools inválida.");
                    }
                    var availablePoolIds = new HashSet<string>(StringComparer.Ordinal);
                    for (int poolIndex = 0; poolIndex < pools.Count; poolIndex++)
                    {
                        availablePoolIds.Add(_blueprintPoolIdField.GetValue(pools[poolIndex]) as string);
                    }
                    HashSet<string> scopedUnitIds = GetStageModUnitIds(
                        stage, temporaryUnits, availablePoolIds);
                    Dictionary<string, HashSet<string>> unitsByPool = GetStagePoolUnitIds(
                        stage, scopedUnitIds, temporaryUnits, availablePoolIds);
                    var stagePoolScopes = new List<BlueprintPoolScopeRegistration>();
                    if (scopedUnitIds.Count > 0 ||
                        (stage.blueprintPoolEntries != null && stage.blueprintPoolEntries.Length > 0))
                    {
                        if (reservedBlueprintPoolIds == null)
                        {
                            reservedBlueprintPoolIds = LoadNativeBlueprintPoolIds();
                        }

                        ScriptableObject clonedPoolList = UnityEngine.Object.Instantiate(stagePoolList);
                        if (clonedPoolList == null)
                        {
                            throw new InvalidDataException("No se pudo clonar la lista de BlueprintPool de " + stage.id + ".");
                        }

                        clonedPoolList.name = "RCL_BlueprintPoolList_" + stage.id.Replace(':', '_');
                        clonedPoolList.hideFlags = HideFlags.HideAndDontSave;
                        temporaryBlueprintPoolClones.Add(clonedPoolList);
                        _stageParametersBlueprintPoolListField.SetValue(parameters, clonedPoolList);
                        poolList = _blueprintPoolsField.GetValue(clonedPoolList) as IList;
                        if (poolList == null)
                        {
                            throw new InvalidDataException("La lista de BlueprintPool clonada para " + stage.id + " es inválida.");
                        }

                        var clonedPools = new List<object>(poolList.Count);
                        for (int poolIndex = 0; poolIndex < pools.Count; poolIndex++)
                        {
                            object pool = pools[poolIndex];
                            string sourcePoolId = _blueprintPoolIdField.GetValue(pool) as string;
                            string rootSourcePoolId;
                            if (_blueprintPoolScopeRegistry.TryGetSourcePoolLocalizationId(sourcePoolId, out rootSourcePoolId) &&
                                !String.IsNullOrWhiteSpace(rootSourcePoolId))
                            {
                                sourcePoolId = rootSourcePoolId;
                            }
                            HashSet<string> additions;
                            if (!unitsByPool.TryGetValue(sourcePoolId, out additions) || additions.Count == 0)
                            {
                                clonedPools.Add(pool);
                                continue;
                            }
                            UnityEngine.Object poolObject = pool as UnityEngine.Object;
                            if (poolObject == null) throw new InvalidDataException("Invalid native pool " + sourcePoolId);
                            UnityEngine.Object clone = UnityEngine.Object.Instantiate(poolObject);
                            if (clone == null)
                            {
                                throw new InvalidDataException("No se pudo clonar el pool " + sourcePoolId + ".");
                            }
                            temporaryBlueprintPoolClones.Add((ScriptableObject)clone);
                            clone.hideFlags = HideFlags.HideAndDontSave;
                            string scopedPoolId = CreateScopedBlueprintPoolId(definition.packId, stage.id,
                                sourcePoolId, reservedBlueprintPoolIds);
                            _blueprintPoolIdField.SetValue(clone, scopedPoolId);
                            clone.name = "RCL_BlueprintPool_" + scopedPoolId.Substring("RCL_POOL_".Length, 16);
                            List<string> cloneAllowed = _blueprintPoolBlueprintsMethod.Invoke(clone,
                                new object[] { null, false }) as List<string>;
                            if (cloneAllowed == null)
                            {
                                throw new InvalidDataException("Native pool " + sourcePoolId +
                                    " did not return a blueprint list.");
                            }
                            HashSet<string> configuredUnitIds = unitsByPool[sourcePoolId];
                            foreach (string unitId in additions)
                            {
                                if (!configuredUnitIds.Contains(unitId)) continue;
                                if (!cloneAllowed.Contains(unitId)) cloneAllowed.Add(unitId);
                            }
                            var registration = new BlueprintPoolScopeRegistration(scopedPoolId, stage.id,
                                clone, additions, (bool)_blueprintPoolExclusiveField.GetValue(clone),
                                sourcePoolId);
                            if (registration.BlueprintsAreExclusive)
                            {
                                registration.SetEligibleExclusiveUnitIds(additions);
                            }
                            stagePoolScopes.Add(registration);
                            poolList[poolIndex] = clone;
                            clonedPools.Add(clone);
                        }
                        pools = clonedPools;
                    }

                    LoadedLandscape landscape = stage.landscape == null
                        ? null
                        : CreateStageLandscape(stage, packageName, stageWorlds, temporaryResources,
                            temporaryLandscapeClones);
                    object runtimeStage = CreateStage(stage, parameters, packageName);
                    UnityEngine.Object fallback = ResolveStageFallback(stage, nativeSourceParameters, stageWorlds, packageName);
                    temporaryStages.Add(new LoadedStage(stage, definition.packId, runtimeStage,
                        pools, stageWorlds, landscape, stagePoolScopes, scopedUnitIds, fallback));
                }

                Dictionary<string, Vector2Int> scopedAiStageRanges = BuildScopedAiStageRanges(
                    temporaryStages, definition.aiProfiles, packageName);
                for (int i = 0; i < definition.aiProfiles.Length; i++)
                {
                    ContentAiProfileDefinition profile = definition.aiProfiles[i];
                    GameObject prefab;
                    DefaultAiBehaviour[] nativeBehaviours;
                    string resourceName;
                    if (!String.IsNullOrWhiteSpace(profile.nativeTemplateId))
                    {
                        DefaultAiBehaviour nativeBehaviour = FindNativeAiTemplate(
                            profile.nativeTemplateId, packageName, profile.id);
                        prefab = nativeBehaviour.gameObject;
                        nativeBehaviours = new[] { nativeBehaviour };
                        resourceName = profile.nativeTemplateId;
                        if (profile.scopeToReferencedStages)
                        {
                            Vector2Int stageRange;
                            if (!scopedAiStageRanges.TryGetValue(profile.id, out stageRange))
                            {
                                throw new InvalidDataException("El perfil AI " + profile.id +
                                    " no tiene un rango de etapas de pack calculado.");
                            }

                            if (!ContentPackValidator.TryBuildScopedAiResourceName(profile.id, out resourceName))
                            {
                                throw new InvalidDataException("El ID del perfil AI " + profile.id +
                                    " no produce un alias de recurso RCL_ válido.");
                            }

                            string scopedResourcePath = "AI/" + resourceName;
                            if (Resources.Load<GameObject>(scopedResourcePath) != null)
                            {
                                throw new InvalidDataException("El alias de recurso AI '" + scopedResourcePath +
                                    "' ya existe en el juego.");
                            }

                            GameObject templateHolder;
                            GameObject scopedPrefab = InstantiateResourceTemplate(prefab, out templateHolder);
                            if (scopedPrefab == null || templateHolder == null)
                            {
                                throw new InvalidDataException("No se pudo clonar de forma aislada el template AI " +
                                    profile.nativeTemplateId + " para " + profile.id + ".");
                            }

                            temporaryNativePrefabs.Add(templateHolder);
                            scopedPrefab.name = resourceName;
                            scopedPrefab.hideFlags = HideFlags.HideAndDontSave;
                            DefaultAiBehaviour scopedBehaviour = scopedPrefab.GetComponent<DefaultAiBehaviour>();
                            if (scopedBehaviour == null)
                            {
                                throw new InvalidDataException("El clon del perfil AI " + profile.id +
                                    " perdió su DefaultAiBehaviour raíz.");
                            }

                            _aiStageRangeField.SetValue(scopedBehaviour, stageRange);
                            prefab = scopedPrefab;
                            nativeBehaviours = new[] { scopedBehaviour };
                            UnityEngine.Object.DontDestroyOnLoad(templateHolder);
                        }
                    }
                    else
                    {
                        AssetBundle bundle = GetBundle(temporaryBundles, profile.assetBundleId, packageName);
                        prefab = LoadRequiredAsset(bundle, profile.prefabAssetName,
                            typeof(GameObject), "active AI GameObject prefab", packageName) as GameObject;
                        nativeBehaviours = prefab == null ? new DefaultAiBehaviour[0] : prefab.GetComponents<DefaultAiBehaviour>();
                        resourceName = profile.prefabAssetName;
                    }

                    if (prefab == null || !prefab.activeSelf)
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id +
                            ": el prefab debe estar activo para que el clon creado por Rogue Command ejecute su ciclo de vida.");
                    }

                    if (!String.Equals(prefab.name, resourceName, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id + ": el nombre interno del prefab '" +
                            prefab.name + "' no coincide con el nombre de recurso esperado '" + resourceName + "'.");
                    }

                    ValidateNoMissingPrefabComponents(prefab, packageName, "Perfil AI " + profile.id);

                    Component[] aiComponents = prefab.GetComponents(_defaultAiBehaviourType);
                    if (aiComponents == null || aiComponents.Length != 1 || !(aiComponents[0] is DefaultAiBehaviour))
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id +
                            ": el prefab debe contener exactamente un componente DefaultAiBehaviour en la raíz.");
                    }

                    if (nativeBehaviours == null || nativeBehaviours.Length != 1)
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id +
                            ": el prefab debe contener exactamente un componente DefaultAiBehaviour.");
                    }

                    if ((bool)_aiDeactivatedField.GetValue(nativeBehaviours[0]))
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id +
                            ": AiBehaviour.isDeactivated está activado; el juego excluirá este perfil al seleccionar AI.");
                    }

                    IList aiBases = _aiBasesField.GetValue(nativeBehaviours[0]) as IList;
                    if (aiBases == null || aiBases.Count == 0)
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id +
                            ": AiBehaviour.aiBases debe contener al menos una base configurada.");
                    }

                    AppendAiUnitEntries(profile, aiBases, temporaryUnits, packageName);
                    AppendAiProductionBuildingEntries(profile, aiBases, temporaryUnits, packageName);

                    int totalDeckEntries = 0;
                    for (int baseIndex = 0; baseIndex < aiBases.Count; baseIndex++)
                    {
                        object aiBase = aiBases[baseIndex];
                        if (aiBase == null || aiBase.GetType() != _aiBaseType)
                        {
                            throw new InvalidDataException("Perfil AI " + profile.id +
                                ": aiBases contiene una entrada nula o de tipo inesperado.");
                        }

                        IList deckEntries = _aiBaseEntitiesInDeckField.GetValue(aiBase) as IList;
                        if (deckEntries == null)
                        {
                            throw new InvalidDataException("Perfil AI " + profile.id +
                                ": AiBase.entitiesInAiDeck es nulo.");
                        }

                        totalDeckEntries += deckEntries.Count;
                        for (int deckIndex = 0; deckIndex < deckEntries.Count; deckIndex++)
                        {
                            object entry = deckEntries[deckIndex];
                            if (entry == null || entry.GetType() != _aiDeckEntryType)
                            {
                                throw new InvalidDataException("Perfil AI " + profile.id +
                                    ": entitiesInAiDeck contiene una entrada nula o de tipo inesperado.");
                            }

                            string deckEntityId = _aiDeckEntryEntityIdField.GetValue(entry) as string;
                            if (String.IsNullOrWhiteSpace(deckEntityId))
                            {
                                throw new InvalidDataException("Perfil AI " + profile.id +
                                    ": la unidad de deck '" + deckEntityId + "' no está registrada ni se declara en este paquete.");
                            }

                            if (!IsDeckEntityNativeOrPackLocal(deckEntityId, temporaryUnits) &&
                                !DeclaresAiDeckEntityDependency(profile, deckEntityId, temporaryUnits))
                            {
                                throw new InvalidDataException("Perfil AI " + profile.id +
                                    ": la unidad de deck externa '" + deckEntityId +
                                    "' debe declararse en unitDependencies, unitEntries o " +
                                    "productionBuildingEntries para fijar su orden de carga.");
                            }

                            if (!IsDeckEntityAvailable(deckEntityId, temporaryUnits))
                            {
                                throw new InvalidDataException("Perfil AI " + profile.id +
                                    ": la unidad de deck '" + deckEntityId + "' no está registrada ni se declara en este paquete.");
                            }
                        }
                    }

                    if (totalDeckEntries == 0)
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id +
                            ": sus aiBases no contienen ninguna unidad en entitiesInAiDeck.");
                    }

                    string resourcePath = "AI/" + resourceName;
                    if (!String.IsNullOrWhiteSpace(profile.nativeTemplateId) &&
                        temporaryResources.ContainsKey(resourcePath))
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id +
                            ": la ruta de recurso " + resourcePath + " está duplicada en el paquete.");
                    }

                    if (String.IsNullOrWhiteSpace(profile.nativeTemplateId) || profile.scopeToReferencedStages)
                    {
                        if (temporaryResources.ContainsKey(resourcePath))
                        {
                            throw new InvalidDataException("Perfil AI " + profile.id +
                                ": la ruta de recurso " + resourcePath + " está duplicada en el paquete.");
                        }

                        temporaryResources.Add(resourcePath, prefab);
                    }

                    var behaviours = new List<object> { nativeBehaviours[0] };
                    Vector2Int profileStageRange;
                    if (scopedAiStageRanges.TryGetValue(profile.id, out profileStageRange))
                    {
                        temporaryProfiles.Add(new LoadedAiProfile(profile, definition.packId, resourceName,
                            behaviours, prefab, profileStageRange.x, profileStageRange.y));
                    }
                    else
                    {
                        temporaryProfiles.Add(new LoadedAiProfile(profile, definition.packId, resourceName,
                            behaviours, prefab));
                    }
                }

                ValidateStageGenerationCompatibility(temporaryStages, temporaryProfiles, packageName);

                lock (_gate)
                {
                    CommitPack(definition, temporaryBundles, temporaryBundleStreams, packBundleBytes,
                        temporaryResources, temporaryUnits, temporaryStages, temporaryProfiles);
                    _nativePrefabClones.AddRange(temporaryNativePrefabs);
                    _stageParameterClones.AddRange(temporaryStageParameterClones);
                    _blueprintPoolClones.AddRange(temporaryBlueprintPoolClones);
                    _landscapeClones.AddRange(temporaryLandscapeClones);
                    candidate.LoadedBytes = packBundleBytes;
                }
                return true;
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    candidate.StatusReason = exception.GetType().Name + ": " + exception.Message;
                }
                for (int prefabIndex = 0; prefabIndex < temporaryNativePrefabs.Count; prefabIndex++)
                {
                    GameObject prefab = temporaryNativePrefabs[prefabIndex];
                    if (prefab != null)
                    {
                        UnityEngine.Object.Destroy(prefab);
                    }
                }

                for (int stageIndex = 0; stageIndex < temporaryStageParameterClones.Count; stageIndex++)
                {
                    UnityEngine.Object stageParameters = temporaryStageParameterClones[stageIndex];
                    if (stageParameters != null)
                    {
                        UnityEngine.Object.Destroy(stageParameters);
                    }
                }

                for (int poolIndex = 0; poolIndex < temporaryBlueprintPoolClones.Count; poolIndex++)
                {
                    ScriptableObject poolClone = temporaryBlueprintPoolClones[poolIndex];
                    if (poolClone != null)
                    {
                        UnityEngine.Object.Destroy(poolClone);
                    }
                }

                for (int landscapeIndex = 0; landscapeIndex < temporaryLandscapeClones.Count; landscapeIndex++)
                {
                    ScriptableObject landscape = temporaryLandscapeClones[landscapeIndex];
                    if (landscape != null)
                    {
                        UnityEngine.Object.Destroy(landscape);
                    }
                }

                try
                {
                    if (manifestBundle != null)
                    {
                        if (!String.IsNullOrEmpty(manifestBundleRegistryKey))
                        {
                            _bundleRegistry.RemoveBundle(manifestBundleRegistryKey, manifestBundle);
                        }
                        manifestBundle.Unload(true);
                    }
                }
                catch { }
                try { if (manifestStream != null) manifestStream.Dispose(); } catch { }
                foreach (KeyValuePair<string, AssetBundle> entry in temporaryBundles)
                {
                    try
                    {
                        _bundleRegistry.RemoveBundle(BundleRegistryKey(definition.packId, entry.Key), entry.Value);
                        if (entry.Value != null) entry.Value.Unload(true);
                    }
                    catch { }
                }

                for (int i = 0; i < temporaryBundleStreams.Count; i++)
                {
                    try { if (temporaryBundleStreams[i] != null) temporaryBundleStreams[i].Dispose(); } catch { }
                }

                Log("Paquete '" + (String.IsNullOrWhiteSpace(definition.packId) ? packageName : definition.packId) +
                    "' desactivado sin registrar contenido: " + exception.GetType().Name + ": " + exception.Message + "\n" + exception.StackTrace);
                return false;
            }
        }

        private static List<ContentAssetBundleDefinition> GetBundleLoadOrder(
            AssetBundleManifest unityManifest,
            ContentAssetBundleDefinition[] declaredBundles,
            string packageName)
        {
            if (unityManifest == null || declaredBundles == null || declaredBundles.Length == 0 ||
                declaredBundles.Length > ContentPackValidator.MaxAssetBundlesPerPack)
            {
                throw new InvalidDataException("El paquete '" + packageName +
                    "' no tiene una lista o un manifiesto de AssetBundles válido (máximo " +
                    ContentPackValidator.MaxAssetBundlesPerPack + " bundles).");
            }

            var bundlesByName = new Dictionary<string, ContentAssetBundleDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < declaredBundles.Length; i++)
            {
                ContentAssetBundleDefinition definition = declaredBundles[i];
                string fileName;
                if (definition == null || !ContentPackValidator.TryGetBundleFileName(definition.path, out fileName))
                {
                    throw new InvalidDataException("El paquete '" + packageName +
                        "' contiene una ruta de bundle fuera del formato bundles/<nombre>.bundle.");
                }

                string bundleName = fileName;
                if (bundlesByName.ContainsKey(bundleName))
                {
                    throw new InvalidDataException("El paquete '" + packageName +
                        "' declara dos bundles con el mismo nombre de archivo '" + bundleName + "'.");
                }

                bundlesByName.Add(bundleName, definition);
            }

            string[] manifestNames = unityManifest.GetAllAssetBundles();
            if (manifestNames == null || manifestNames.Length != bundlesByName.Count)
            {
                throw new InvalidDataException("El manifiesto de Unity del paquete '" + packageName +
                    "' no coincide con la lista assetBundles declarada.");
            }

            var manifestNameSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < manifestNames.Length; i++)
            {
                if (String.IsNullOrWhiteSpace(manifestNames[i]) ||
                    !manifestNameSet.Add(manifestNames[i]) || !bundlesByName.ContainsKey(manifestNames[i]))
                {
                    throw new InvalidDataException("El manifiesto de Unity del paquete '" + packageName +
                        "' contiene un bundle no declarado o duplicado: '" + manifestNames[i] + "'.");
                }
            }

            var states = new Dictionary<string, byte>(StringComparer.Ordinal);
            var loadOrder = new List<ContentAssetBundleDefinition>(bundlesByName.Count);
            for (int i = 0; i < declaredBundles.Length; i++)
            {
                string bundleName;
                ContentPackValidator.TryGetBundleFileName(declaredBundles[i].path, out bundleName);
                VisitBundleDependencies(bundleName, unityManifest, bundlesByName, states,
                    loadOrder, packageName);
            }

            return loadOrder;
        }

        private static long ValidateBundleStreamSize(
            FileStream stream,
            string relativePath,
            long maximumBytes,
            string description)
        {
            if (stream == null || !stream.CanSeek)
            {
                throw new InvalidDataException("El " + description + " '" + relativePath +
                    "' no tiene un stream legible y seekable.");
            }

            long length = stream.Length;
            if (length <= 0 || length > maximumBytes)
            {
                throw new InvalidDataException("El " + description + " '" + relativePath + "' ocupa " +
                    length + " bytes; el límite permitido es 1–" + maximumBytes + " bytes.");
            }

            return length;
        }

        private static void VisitBundleDependencies(
            string bundleName,
            AssetBundleManifest unityManifest,
            Dictionary<string, ContentAssetBundleDefinition> bundlesByName,
            Dictionary<string, byte> states,
            List<ContentAssetBundleDefinition> loadOrder,
            string packageName)
        {
            byte state;
            if (states.TryGetValue(bundleName, out state))
            {
                if (state == 1)
                {
                    throw new InvalidDataException("El manifiesto del paquete '" + packageName +
                        "' contiene una dependencia circular en '" + bundleName + "'.");
                }

                return;
            }

            ContentAssetBundleDefinition definition;
            if (!bundlesByName.TryGetValue(bundleName, out definition))
            {
                throw new InvalidDataException("El manifiesto del paquete '" + packageName +
                    "' requiere el bundle no declarado '" + bundleName + "'.");
            }

            states.Add(bundleName, 1);
            string[] dependencies = unityManifest.GetDirectDependencies(bundleName);
            if (dependencies != null)
            {
                for (int i = 0; i < dependencies.Length; i++)
                {
                    if (String.IsNullOrWhiteSpace(dependencies[i]))
                    {
                        throw new InvalidDataException("El manifiesto del paquete '" + packageName +
                            "' contiene un nombre de dependencia vacío para '" + bundleName + "'.");
                    }

                    VisitBundleDependencies(dependencies[i], unityManifest, bundlesByName,
                        states, loadOrder, packageName);
                }
            }

            states[bundleName] = 2;
            loadOrder.Add(definition);
        }

        private void CommitPack(
            ContentPackDefinition definition,
            Dictionary<string, AssetBundle> bundles,
            List<FileStream> bundleStreams,
            long bundleBytes,
            Dictionary<string, UnityEngine.Object> resources,
            List<LoadedUnit> units,
            List<LoadedStage> stages,
            List<LoadedAiProfile> profiles)
        {
            var profileById = new Dictionary<string, LoadedAiProfile>(StringComparer.Ordinal);
            for (int i = 0; i < profiles.Count; i++)
            {
                profileById.Add(profiles[i].Id, profiles[i]);
            }

            var localKnownIds = new List<string>();
            localKnownIds.Add(definition.packId);
            for (int i = 0; i < definition.assetBundles.Length; i++) localKnownIds.Add(definition.assetBundles[i].id);
            for (int i = 0; i < units.Count; i++) localKnownIds.Add(units[i].Id);
            for (int i = 0; i < stages.Count; i++) localKnownIds.Add(stages[i].Definition.id);
            for (int i = 0; i < profiles.Count; i++) localKnownIds.Add(profiles[i].Id);
            for (int i = 0; i < (definition.relics ?? new ContentRelicDefinition[0]).Length; i++)
            {
                localKnownIds.Add(definition.relics[i].id);
            }

            var contributionStages = new List<object>();
            var contributionAi = new List<object>();
            var poolScopeRegistrations = new List<BlueprintPoolScopeRegistration>();
            var blueprintUnitIds = new HashSet<string>(StringComparer.Ordinal);
            var startingBlueprintUnitIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < units.Count; i++)
            {
                bool isEligibleForStarting = units[i].Definition != null &&
                    (units[i].Definition.isAllowedAsStartingBlueprint ||
                     !units[i].Definition.neededExperienceLevel.HasValue ||
                     units[i].Definition.neededExperienceLevel.Value <= 0);

                if (isEligibleForStarting)
                {
                    startingBlueprintUnitIds.Add(units[i].Id);
                    if (!String.IsNullOrWhiteSpace(units[i].ProductionFactoryId))
                    {
                        startingBlueprintUnitIds.Add(units[i].ProductionFactoryId);
                    }
                }
                else if (units[i].IsStandaloneBuilding)
                {
                    object startingVal = _allowedStartingBlueprintField.GetValue(units[i].BalanceRow);
                    if (startingVal is bool && (bool)startingVal)
                    {
                        startingBlueprintUnitIds.Add(units[i].Id);
                    }
                }

                if (units[i].IsProductionBuilding)
                {
                    object allowedVal = _allowedBlueprintField.GetValue(units[i].BalanceRow);
                    if (allowedVal is bool && (bool)allowedVal)
                    {
                        blueprintUnitIds.Add(units[i].Id);
                    }
                }
                else if (String.IsNullOrWhiteSpace(units[i].ProductionFactoryId) && HasRoleFlag(units[i].BalanceRow, "Building"))
                {
                    object allowedVal = _allowedBlueprintField.GetValue(units[i].BalanceRow);
                    if (allowedVal is bool && (bool)allowedVal)
                    {
                        blueprintUnitIds.Add(units[i].Id);
                    }
                }
            }

            var seenBehaviourReferences = new HashSet<object>(ReferenceComparer.Instance);
            for (int i = 0; i < stages.Count; i++)
            {
                LoadedStage stage = stages[i];
                contributionStages.Add(stage.RuntimeStage);
                poolScopeRegistrations.AddRange(stage.BlueprintPoolScopes);
                string[] profileIds = stage.Definition.aiProfileIds ?? new string[0];
                for (int profileIndex = 0; profileIndex < profileIds.Length; profileIndex++)
                {
                    LoadedAiProfile profile;
                    if (!profileById.TryGetValue(profileIds[profileIndex], out profile))
                    {
                        _aiProfiles.TryGetValue(profileIds[profileIndex], out profile);
                    }

                    if (profile == null)
                    {
                        throw new InvalidDataException("El perfil AI " + profileIds[profileIndex] +
                            " no está cargado para la etapa " + stage.Definition.id + ".");
                    }

                    if (profile.ScopeToReferencedStages &&
                        !String.Equals(profile.PackId, definition.packId, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("La etapa " + stage.Definition.id +
                            " no puede ampliar el rango del perfil AI de etapa " + profile.Id +
                            " porque pertenece a otro paquete. Incluya las etapas asociadas en el paquete del perfil.");
                    }

                    for (int behaviourIndex = 0; behaviourIndex < profile.Behaviours.Count; behaviourIndex++)
                    {
                        object behaviour = profile.Behaviours[behaviourIndex];
                        if (seenBehaviourReferences.Add(behaviour))
                        {
                            contributionAi.Add(behaviour);
                        }
                    }
                }

                string[] unitIds = stage.Definition.unitIds ?? new string[0];
                for (int unitIndex = 0; unitIndex < unitIds.Length; unitIndex++)
                {
                    LoadedUnit unit;
                    if (TryGetLoadedUnit(unitIds[unitIndex], units, out unit))
                    {
                        if (unit.IsProductionBuilding)
                        {
                            throw new InvalidDataException("La etapa " + stage.Definition.id +
                                " debe declarar IDs de producto; no puede declarar directamente el edificio " +
                                unit.Id + ".");
                        }

                    }
                    else if (_nativeUnitIds.Contains(unitIds[unitIndex]))
                    {
                        if (_nativeFactoryIds.Contains(unitIds[unitIndex]))
                        {
                            throw new InvalidDataException("La etapa " + stage.Definition.id +
                                " debe declarar IDs de producto; no puede declarar directamente la fábrica nativa " +
                                unitIds[unitIndex] + ".");
                        }

                        // Existing game rows keep their authored flags; the stage pool still decides eligibility.
                    }
                    else
                    {
                        throw new InvalidDataException("La unidad " + unitIds[unitIndex] +
                            " de la etapa " + stage.Definition.id + " no está disponible.");
                    }
                }

                blueprintUnitIds.UnionWith(StageBlueprintFactoryIds.CollectLoaderFactoryIds(
                    stage.Definition,
                    delegate(string productId)
                    {
                        LoadedUnit unit;
                        if (!TryGetLoadedUnit(productId, units, out unit))
                        {
                            return null;
                        }

                        if (unit.IsProductionBuilding)
                        {
                            throw new InvalidDataException("La etapa " + stage.Definition.id +
                                " debe declarar IDs de producto; no puede declarar directamente el edificio " +
                                unit.Id + ".");
                        }

                        return String.IsNullOrWhiteSpace(unit.ProductionFactoryId)
                            ? unit.Id
                            : unit.ProductionFactoryId;
                    }));
            }

            for (int i = 0; i < units.Count; i++)
            {
                if (_units.ContainsKey(units[i].Id))
                {
                    throw new InvalidDataException("La unidad duplicada " + units[i].Id + " ya fue registrada.");
                }
            }

            for (int i = 0; i < profiles.Count; i++)
            {
                if (_aiProfiles.ContainsKey(profiles[i].Id))
                {
                    throw new InvalidDataException("El perfil AI duplicado " + profiles[i].Id + " ya fue registrado.");
                }

                if (_aiProfilesByResourceName.ContainsKey(profiles[i].ResourceName))
                {
                    throw new InvalidDataException("La ruta de prefab AI duplicada AI/" +
                        profiles[i].ResourceName + " ya está registrada.");
                }
            }

            _blueprintPoolScopeRegistry.ValidateCanRegister(poolScopeRegistrations);

            // All checks above complete before any pack-owned runtime registry is updated.
            _relicRegistrar.Register(definition.relics, delegate(ContentRelicDefinition relic)
            {
                AssetBundle bundle = GetBundle(bundles, relic.assetBundleId, definition.packId);
                return bundle.LoadAsset<Sprite>(relic.imageAssetName);
            }, _retainedRelicObjects);

            for (int i = 0; i < units.Count; i++)
            {
                _units.Add(units[i].Id, units[i]);
                _entityRows.Add(units[i].BalanceRow);
            }

            for (int i = 0; i < (definition.relics ?? new ContentRelicDefinition[0]).Length; i++)
            {
                _relicDefinitions.Add(definition.relics[i].id, definition.relics[i]);
            }

            _blueprintPoolScopeRegistry.Register(poolScopeRegistrations);

            for (int i = 0; i < profiles.Count; i++)
            {
                _aiProfiles.Add(profiles[i].Id, profiles[i]);
                _aiProfilesByResourceName.Add(profiles[i].ResourceName, profiles[i]);
            }

            for (int i = 0; i < stages.Count; i++)
            {
                _stages.Add(stages[i]);
                if (stages[i].Landscape != null)
                {
                    _landscapesByLocalizationId.Add(stages[i].Landscape.LocalizationId,
                        stages[i].Landscape);
                }

                List<object> stagePools = stages[i].BlueprintPools;
                for (int poolIndex = 0; poolIndex < stagePools.Count; poolIndex++)
                {
                    bool alreadyRegistered = false;
                    for (int existingIndex = 0; existingIndex < _blueprintPools.Count; existingIndex++)
                    {
                        if (System.Object.ReferenceEquals(_blueprintPools[existingIndex], stagePools[poolIndex]))
                        {
                            alreadyRegistered = true;
                            break;
                        }
                    }

                    if (!alreadyRegistered)
                    {
                        _blueprintPools.Add(stagePools[poolIndex]);
                    }
                }
            }

            foreach (KeyValuePair<string, UnityEngine.Object> resource in resources)
            {
                _resourceAssets.Add(resource.Key, resource.Value);
            }

            foreach (KeyValuePair<string, AssetBundle> bundle in bundles)
            {
                _assetBundles.Add(bundle.Value);
            }

            _assetBundleStreams.AddRange(bundleStreams);
            _loadedAssetBundleBytes += bundleBytes;

            _knownIds.Add(definition.packId);
            for (int i = 0; i < localKnownIds.Count; i++)
            {
                _knownIds.Add(localKnownIds[i]);
            }

            foreach (string unitId in blueprintUnitIds)
            {
                _blueprintUnitIds.Add(unitId);
            }

            foreach (string unitId in startingBlueprintUnitIds)
            {
                _startingBlueprintUnitIds.Add(unitId);
            }

            _contributions.Add(new ProgressionContentContribution(
                definition.packId, contributionStages, contributionAi));
            int productionBuildingCount = 0;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].IsProductionBuilding)
                {
                    productionBuildingCount++;
                }
            }

            Log("Paquete '" + definition.packId + "' cargado: " +
                (units.Count - productionBuildingCount) + " unidad(es), " + productionBuildingCount +
                " edificio(s) de producción, " +
                stages.Count + " etapa(s), " + profiles.Count + " perfil(es) AI.");
        }

        private void ApplyBlueprintEligibility()
        {
            foreach (string unitId in _blueprintUnitIds)
            {
                LoadedUnit unit;
                if (_units.TryGetValue(unitId, out unit))
                {
                    _allowedBlueprintField.SetValue(unit.BalanceRow, true);
                }
            }

            foreach (string unitId in _startingBlueprintUnitIds)
            {
                LoadedUnit unit;
                if (_units.TryGetValue(unitId, out unit))
                {
                    _allowedStartingBlueprintField.SetValue(unit.BalanceRow, true);
                }
            }
        }

        private HashSet<string> GetStageModUnitIds(
            ContentStageDefinition stage,
            List<LoadedUnit> currentPackUnits,
            HashSet<string> allowedPoolIds)
        {
            var scopedUnitIds = new HashSet<string>(StringComparer.Ordinal);
            var unitIds = stage.unitIds ?? new string[0];
            for (int i = 0; i < unitIds.Length; i++)
            {
                LoadedUnit unit;
                if (TryGetLoadedUnit(unitIds[i], currentPackUnits, out unit) && !unit.IsProductionBuilding)
                {
                    scopedUnitIds.Add(String.IsNullOrWhiteSpace(unit.ProductionFactoryId)
                        ? unit.Id
                        : unit.ProductionFactoryId);
                }
            }

            if (stage.blueprintPoolEntries != null)
            {
                for (int i = 0; i < stage.blueprintPoolEntries.Length; i++)
                {
                    ContentStagePoolEntryDefinition entry = stage.blueprintPoolEntries[i];
                    if (entry == null || !allowedPoolIds.Contains(entry.poolId))
                    {
                        throw new InvalidDataException("Etapa " + stage.id +
                            " declara un pool no presente en la plantilla nativa: " +
                            (entry == null ? "<nulo>" : entry.poolId));
                    }

                    for (int j = 0; entry.unitIds != null && j < entry.unitIds.Length; j++)
                    {
                        LoadedUnit unit;
                        if (TryGetLoadedUnit(entry.unitIds[j], currentPackUnits, out unit))
                        {
                            if (unit.IsProductionBuilding)
                            {
                                throw new InvalidDataException("La etapa " + stage.id +
                                    " declara un edificio de producción como producto de pool: " + entry.unitIds[j]);
                            }
                        }
                    }
                }
            }

            return scopedUnitIds;
        }

        private Dictionary<string, HashSet<string>> GetStagePoolUnitIds(
            ContentStageDefinition stage,
            HashSet<string> scopedUnitIds,
            List<LoadedUnit> currentPackUnits,
            IEnumerable<string> availablePoolIds)
        {
            return StageBlueprintPoolFactoryIds.CollectByPool(
                scopedUnitIds,
                availablePoolIds,
                stage.blueprintPoolEntries,
                delegate(string productId)
                {
                    LoadedUnit unit;
                    if (!TryGetLoadedUnit(productId, currentPackUnits, out unit))
                    {
                        return null;
                    }

                    if (unit.IsProductionBuilding)
                    {
                        throw new InvalidDataException("La etapa " + stage.id +
                            " declara un edificio de producción como producto de pool: " + productId);
                    }

                    return String.IsNullOrWhiteSpace(unit.ProductionFactoryId)
                        ? unit.Id
                        : unit.ProductionFactoryId;
                });
        }

        private void AddLoadedFactoryId(
            string unitId,
            List<LoadedUnit> currentPackUnits,
            HashSet<string> result,
            string stageId)
        {
            LoadedUnit unit;
            if (!TryGetLoadedUnit(unitId, currentPackUnits, out unit) || unit.IsProductionBuilding)
            {
                throw new InvalidDataException("La etapa " + stageId +
                    " referencia una unidad no cargada para su pool de blueprints: " + unitId);
            }

            result.Add(String.IsNullOrWhiteSpace(unit.ProductionFactoryId)
                ? unit.Id
                : unit.ProductionFactoryId);
        }

        private bool TryGetLoadedUnit(string unitId, List<LoadedUnit> temporaryUnits, out LoadedUnit unit)
        {
            if (temporaryUnits != null)
            {
                for (int i = 0; i < temporaryUnits.Count; i++)
                {
                    if (String.Equals(temporaryUnits[i].Id, unitId, StringComparison.Ordinal))
                    {
                        unit = temporaryUnits[i];
                        return true;
                    }
                }
            }

            return _units.TryGetValue(unitId, out unit);
        }

        private HashSet<string> LoadNativeBlueprintPoolIds()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            UnityEngine.Object[] pools = Resources.LoadAll("BlueprintPools", _blueprintPoolType);
            for (int i = 0; pools != null && i < pools.Length; i++)
            {
                UnityEngine.Object pool = pools[i];
                if (pool == null || pool.GetType() != _blueprintPoolType)
                {
                    continue;
                }

                string poolId = _blueprintPoolIdField.GetValue(pool) as string;
                if (!String.IsNullOrWhiteSpace(poolId))
                {
                    result.Add(poolId);
                }
            }

            return result;
        }

        private List<object> CloneStageBlueprintPools(
            UnityEngine.Object stageParameters,
            string packId,
            ContentStageDefinition stage,
            string packageName,
            List<object> validatedPools,
            HashSet<string> scopedUnitIds,
            List<LoadedUnit> currentPackUnits,
            HashSet<string> reservedPoolIds,
            List<ScriptableObject> temporaryClones,
            List<BlueprintPoolScopeRegistration> registrations)
        {
            ScriptableObject sourceList = _stageParametersBlueprintPoolListField.GetValue(stageParameters)
                as ScriptableObject;
            if (sourceList == null || sourceList.GetType() != _blueprintPoolListType)
            {
                throw new InvalidDataException("Etapa " + stage.id + " en '" + packageName +
                    "' no tiene una BlueprintPoolList clonable.");
            }

            ScriptableObject clonedList = UnityEngine.Object.Instantiate(sourceList);
            if (clonedList == null || clonedList.GetType() != _blueprintPoolListType)
            {
                throw new InvalidDataException("No se pudo clonar la lista de BlueprintPool de " + stage.id + ".");
            }

            temporaryClones.Add(clonedList);
            clonedList.name = "RCL_BlueprintPoolList_" + stage.id.Replace(':', '_');
            clonedList.hideFlags = HideFlags.HideAndDontSave;
            IList clonedPools = _blueprintPoolsField.GetValue(clonedList) as IList;
            if (clonedPools == null || clonedPools.Count != validatedPools.Count)
            {
                throw new InvalidDataException("La lista de BlueprintPool clonada para " + stage.id +
                    " cambió de forma inesperada.");
            }

            var sourcePoolIds = new HashSet<string>(StringComparer.Ordinal);
            for (int poolIndex = 0; poolIndex < validatedPools.Count; poolIndex++)
            {
                sourcePoolIds.Add(_blueprintPoolIdField.GetValue(validatedPools[poolIndex]) as string);
            }
            Dictionary<string, HashSet<string>> unitsByPool = GetStagePoolUnitIds(
                stage, scopedUnitIds, currentPackUnits, sourcePoolIds);
            var result = new List<object>(clonedPools.Count);
            for (int i = 0; i < clonedPools.Count; i++)
            {
                UnityEngine.Object sourcePool = clonedPools[i] as UnityEngine.Object;
                if (sourcePool == null || sourcePool.GetType() != _blueprintPoolType)
                {
                    throw new InvalidDataException("La lista clonada de la etapa " + stage.id +
                        " contiene un BlueprintPool inválido.");
                }

                UnityEngine.Object clonedPool = UnityEngine.Object.Instantiate(sourcePool);
                if (clonedPool == null || clonedPool.GetType() != _blueprintPoolType)
                {
                    throw new InvalidDataException("No se pudo clonar un BlueprintPool de la etapa " + stage.id + ".");
                }

                var poolClone = (ScriptableObject)clonedPool;
                temporaryClones.Add(poolClone);
                string originalPoolId = _blueprintPoolIdField.GetValue(sourcePool) as string;
                HashSet<string> poolUnitIds;
                if (!unitsByPool.TryGetValue(originalPoolId, out poolUnitIds))
                {
                    poolUnitIds = new HashSet<string>(StringComparer.Ordinal);
                }
                string scopedPoolId = CreateScopedBlueprintPoolId(
                    packId, stage.id, originalPoolId, reservedPoolIds);
                poolClone.name = "RCL_BlueprintPool_" + scopedPoolId.Substring("RCL_POOL_".Length, 16);
                poolClone.hideFlags = HideFlags.HideAndDontSave;
                _blueprintPoolIdField.SetValue(poolClone, scopedPoolId);
                clonedPools[i] = poolClone;

                bool exclusive = (bool)_blueprintPoolExclusiveField.GetValue(poolClone);
                var registration = new BlueprintPoolScopeRegistration(
                    scopedPoolId, stage.id, poolClone, poolUnitIds, exclusive, originalPoolId);
                if (exclusive) registration.SetEligibleExclusiveUnitIds(poolUnitIds);
                registrations.Add(registration);
                result.Add(poolClone);
            }

            _stageParametersBlueprintPoolListField.SetValue(stageParameters, clonedList);
            return result;
        }

        private string CreateScopedBlueprintPoolId(
            string packId,
            string stageId,
            string originalPoolId,
            HashSet<string> reservedPoolIds)
        {
            string identity = (packId ?? String.Empty) + "\n" + (stageId ?? String.Empty) + "\n" +
                (originalPoolId ?? String.Empty);
            byte[] digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(identity));
            }

            var token = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
            {
                token.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            string poolId = "RCL_POOL_" + token;
            if (_blueprintPoolScopeRegistry.ContainsPoolId(poolId) || !reservedPoolIds.Add(poolId))
            {
                throw new InvalidDataException("El ID de BlueprintPool generado " + poolId +
                    " ya está ocupado por otro pool.");
            }

            return poolId;
        }

        private object ReadSingleBalanceRow(UnityEngine.Object balanceAsset, string packageName, string unitId)
        {
            IList rows = _entityParametersField.GetValue(balanceAsset) as IList;
            if (rows == null || rows.Count != 1)
            {
                throw new InvalidDataException("Unidad " + unitId + " en '" + packageName +
                    "': el asset de balance debe contener exactamente una fila EntityBalancingParameters.");
            }

            object row = rows[0];
            if (row == null || row.GetType() != _entityRowType)
            {
                throw new InvalidDataException("El asset de balance de " + unitId +
                    " contiene una fila con tipo inesperado.");
            }

            string rowEntityId = _entityIdField.GetValue(row) as string;
            if (!String.Equals(rowEntityId, unitId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("El asset de balance de " + unitId +
                    " declara entityId '" + rowEntityId + "' en vez del ID de unidad del paquete.");
            }

            return row;
        }

        private object CopyNativeBalanceRow(string templateId, string packageName, string unitId)
        {
            IList rows = _nativeEntityRowsField.GetValue(null) as IList;
            IDictionary index = _entityIndexField.GetValue(null) as IDictionary;
            if (rows == null || index == null || !index.Contains(templateId))
            {
                throw new InvalidDataException("La unidad " + unitId + " en '" + packageName +
                    "' referencia una plantilla nativa no registrada: '" + templateId + "'.");
            }

            int rowIndex;
            try
            {
                rowIndex = Convert.ToInt32(index[templateId], CultureInfo.InvariantCulture);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("El índice de la plantilla nativa '" + templateId +
                    "' no es válido.", exception);
            }

            if (rowIndex < 0 || rowIndex >= rows.Count)
            {
                throw new InvalidDataException("El índice de la plantilla nativa '" + templateId +
                    "' sale de la lista de balance.");
            }

            // EntityBalancingParameters is verified as a value type above; reading through non-generic IList
            // boxes a copy. Overrides and namespaced identity fields are applied to that detached copy, which
            // RuntimeContentMutator later appends as a new row without changing rows[rowIndex].
            object row = rows[rowIndex];
            if (row == null || row.GetType() != _entityRowType ||
                !String.Equals(_entityIdField.GetValue(row) as string, templateId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("El índice de la plantilla nativa '" + templateId +
                    "' no apunta a su fila EntityBalancingParameters.");
            }

            return row;
        }

        private LoadedUnit CreateProductionBuilding(
            ContentUnitDefinition product,
            ContentProductionBuildingDefinition building,
            string packId,
            string packageName,
            Dictionary<string, AssetBundle> temporaryBundles,
            List<GameObject> temporaryNativePrefabs,
            Dictionary<string, UnityEngine.Object> temporaryResources)
        {
            if (product == null || building == null || String.IsNullOrWhiteSpace(product.id) ||
                String.IsNullOrWhiteSpace(building.id) || String.IsNullOrWhiteSpace(building.nativeTemplateId) ||
                String.Equals(product.id, building.id, StringComparison.Ordinal))
            {
                throw new InvalidDataException("La unidad o su productionBuilding contiene una referencia vacía o IDs " +
                    "iguales en '" + packageName + "'.");
            }

            object row = CopyNativeBalanceRow(building.nativeTemplateId, packageName, building.id);
            if (!HasProductionBuildingRoles(row))
            {
                throw new InvalidDataException("El nativeTemplateId '" + building.nativeTemplateId + "' de " +
                    building.id + " en '" + packageName + "' no tiene los roles Building y Factory.");
            }

            string nativeProductId = ReadFactoryProductId(row);
            if (String.IsNullOrWhiteSpace(nativeProductId) || !_nativeUnitIds.Contains(nativeProductId))
            {
                throw new InvalidDataException("La plantilla de fábrica " + building.nativeTemplateId +
                    " no declara un factoryForEntityId de producto nativo registrado.");
            }

            string nativePrefabPath = _prefabLocationField.GetValue(row) as string;
            string nativeImagePath = _imageLocationField.GetValue(row) as string;
            if (String.IsNullOrWhiteSpace(nativePrefabPath) || String.IsNullOrWhiteSpace(nativeImagePath))
            {
                throw new InvalidDataException("El edificio plantilla '" + building.nativeTemplateId +
                    "' no contiene rutas nativas de prefab e imagen válidas.");
            }

            GameObject nativeTemplate = Resources.Load<GameObject>(nativePrefabPath);
            if (nativeTemplate == null)
            {
                nativeTemplate = Resources.Load<GameObject>(nativePrefabPath.ToLowerInvariant());
            }

            if (nativeTemplate == null)
            {
                throw new InvalidDataException("El edificio " + building.id + " no pudo cargar el prefab nativo '" +
                    nativePrefabPath + "' de la plantilla " + building.nativeTemplateId + ".");
            }

            var localizationDefinition = new ContentUnitDefinition
            {
                id = building.id,
                nativeTemplateId = building.nativeTemplateId,
                assetBundleId = building.assetBundleId,
                cardImageAssetName = building.cardImageAssetName,
                visualAssetName = building.visualAssetName,
                visualRootPath = building.visualRootPath,
                localizations = building.localizations
            };
            ValidateUnitPrefab(nativeTemplate, localizationDefinition, packageName, false);

            GameObject visualTemplate = null;
            AssetBundle buildingAssetBundle = null;
            string imagePath = nativeImagePath;
            if (!String.IsNullOrWhiteSpace(building.cardImageAssetName) ||
                !String.IsNullOrWhiteSpace(building.visualAssetName))
            {
                buildingAssetBundle = GetBundle(temporaryBundles, building.assetBundleId, packageName);
            }
            if (!String.IsNullOrWhiteSpace(building.cardImageAssetName))
            {
                AssetBundle cardBundle = buildingAssetBundle ??
                    GetBundle(temporaryBundles, building.assetBundleId, packageName);
                Sprite cardImage = LoadRequiredAsset(cardBundle, building.cardImageAssetName,
                    typeof(Sprite), "production-building Sprite card image", packageName) as Sprite;
                imagePath = ResourceKey(building.id, "image");
                temporaryResources.Add(imagePath, cardImage);
            }

            if (!String.IsNullOrWhiteSpace(building.visualAssetName))
            {
                visualTemplate = LoadRequiredAsset(buildingAssetBundle, building.visualAssetName,
                    typeof(GameObject), "visual-only production-building GameObject prefab", packageName) as GameObject;
                ValidateVisualPrefab(visualTemplate, localizationDefinition, packageName,
                    "Edificio de producción");
            }

            GameObject nativePrefabClone = InstantiateDormantPrefab(nativeTemplate);
            if (nativePrefabClone == null)
            {
                throw new InvalidDataException("No se pudo clonar el prefab del edificio " + building.id + ".");
            }

            temporaryNativePrefabs.Add(nativePrefabClone);
            if (visualTemplate != null)
            {
                ApplyVisualOverlay(nativePrefabClone, visualTemplate, localizationDefinition, packageName,
                    "Edificio de producción", true);
            }

            EntityController clonedController = nativePrefabClone.GetComponent<EntityController>();
            if (clonedController == null)
            {
                throw new InvalidDataException("El clon nativo del edificio " + building.id +
                    " perdió su EntityController raíz.");
            }

            EntityControllerIdentity.Assign(_entityControllerIdField, clonedController, building.id,
                "Edificio de producción " + building.id + " en '" + packageName + "'");
            ApplyEntityBehavioralParameters(nativePrefabClone, null, building.gridSize.HasValue ? (float?)(building.gridSize.Value * 10f) : null,
                null, false, false, false, -1);
            EnsureMuzzleFlashHierarchy(nativePrefabClone);

            nativePrefabClone.name = "RCL_NativeFactory_" + building.id.Replace(':', '_');
            nativePrefabClone.SetActive(false);
            nativePrefabClone.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(nativePrefabClone);

            string prefabKey = ResourceKey(building.id, "prefab");
            _allowedBlueprintField.SetValue(row, true);
            ApplyBalanceOverrides(row, building.balanceOverrides, packageName, building.id, false, true);
            ApplyStructuredProductionBuildingParameters(row, building, product);
            _entityIdField.SetValue(row, building.id);
            _prefabLocationField.SetValue(row, prefabKey);
            _imageLocationField.SetValue(row, imagePath);
            _inactiveField.SetValue(row, false);
            ProductionBuildingEligibility.ApplyToFactoryRow(product, row,
                _allowedStartingBlueprintField, _allowedForAiField, _allowedForDemoField);
            _factoryForEntityIdField.SetValue(row, CreateNullableString(product.id));
            _copyUpgradesToEntityIdField.SetValue(row, CreateNullableString(product.id));
            string[] tagsToOffer = (building.systemTags != null && building.systemTags.Length > 0)
                ? building.systemTags
                : product.systemTags;
            if (tagsToOffer != null && tagsToOffer.Length > 0 && _offeredSystemTagsField != null)
            {
                int combinedTags = 0;
                foreach (string tagName in tagsToOffer)
                {
                    if (!String.IsNullOrWhiteSpace(tagName))
                    {
                        try
                        {
                            combinedTags |= Convert.ToInt32(Enum.Parse(_offeredSystemTagsField.FieldType, tagName, true));
                        }
                        catch (Exception) { }
                    }
                }
                object tagsEnum = Enum.ToObject(_offeredSystemTagsField.FieldType, combinedTags);
                _offeredSystemTagsField.SetValue(row, tagsEnum);
            }
            temporaryResources.Add(prefabKey, nativePrefabClone);

            int factoryPrespawn = 2;
            return new LoadedUnit(building.id, packId, prefabKey, imagePath,
                row, localizationDefinition, true, null, factoryPrespawn, false);
        }

        private object CreateNullableString(string value)
        {
            object nullableString = Activator.CreateInstance(_factoryForEntityIdField.FieldType);
            _nullableStringValueField.SetValue(nullableString, value);
            _nullableStringHasValueField.SetValue(nullableString, true);
            return nullableString;
        }

        private object CreateEmptyNullableString()
        {
            return Activator.CreateInstance(_factoryForEntityIdField.FieldType);
        }

        private string ReadFactoryProductId(object row)
        {
            object nullableString = _factoryForEntityIdField.GetValue(row);
            if (nullableString == null ||
                !(bool)_nullableStringHasValueField.GetValue(nullableString))
            {
                return null;
            }

            return _nullableStringValueField.GetValue(nullableString) as string;
        }

        private void ApplyStructuredUnitParameters(object row, ContentUnitDefinition unit)
        {
            if (row == null || unit == null)
            {
                return;
            }

            if (unit.combatValue.HasValue)
            {
                _combatValueField.SetValue(row, unit.combatValue.Value);
            }
            if (unit.gridSize.HasValue)
            {
                _gridSizeField.SetValue(row, unit.gridSize.Value);
            }
            if (unit.maxCapacity.HasValue && _maxCapacityField != null)
            {
                _maxCapacityField.SetValue(row, unit.maxCapacity.Value);
            }
            if (unit.cardModelScalingFactor.HasValue)
            {
                _cardModelScalingFactorField.SetValue(row, unit.cardModelScalingFactor.Value);
            }
            if (unit.coinsAmount.HasValue)
            {
                _coinsAmountField.SetValue(row, unit.coinsAmount.Value);
            }
            if (unit.firePointCount.HasValue)
            {
                _firePointCountField.SetValue(row, unit.firePointCount.Value);
            }
            if (!String.IsNullOrEmpty(unit.copyUpgradesToEntityId))
            {
                _copyUpgradesToEntityIdField.SetValue(row, CreateNullableString(unit.copyUpgradesToEntityId));
            }
            if (unit.roles != null && unit.roles.Length > 0 && _entityRolesField != null)
            {
                int combinedRoles = 0;
                foreach (string roleName in unit.roles)
                {
                    if (!String.IsNullOrWhiteSpace(roleName))
                    {
                        try
                        {
                            combinedRoles |= Convert.ToInt32(Enum.Parse(_entityRolesField.FieldType, roleName, true));
                        }
                        catch (Exception) { }
                    }
                }
                object rolesEnum = Enum.ToObject(_entityRolesField.FieldType, combinedRoles);
                _entityRolesField.SetValue(row, rolesEnum);
            }
            if (!String.IsNullOrEmpty(unit.attackType) && _attackTypeField != null)
            {
                try
                {
                    object attackTypeEnum = Enum.Parse(_attackTypeField.FieldType, unit.attackType, true);
                    _attackTypeField.SetValue(row, attackTypeEnum);
                }
                catch (Exception) { }
            }
            if (!String.IsNullOrEmpty(unit.skillType) && _skillTypeField != null)
            {
                try
                {
                    object skillTypeEnum = Enum.Parse(_skillTypeField.FieldType, unit.skillType, true);
                    _skillTypeField.SetValue(row, skillTypeEnum);
                }
                catch (Exception) { }
            }
            if (!String.IsNullOrEmpty(unit.rarity) && _rarityField != null)
            {
                try
                {
                    object rarityEnum = Enum.Parse(_rarityField.FieldType, unit.rarity, true);
                    _rarityField.SetValue(row, rarityEnum);
                }
                catch (Exception) { }
            }
            if (!String.IsNullOrEmpty(unit.tech) && _techField != null)
            {
                try
                {
                    object techEnum = Enum.Parse(_techField.FieldType, unit.tech, true);
                    _techField.SetValue(row, techEnum);
                }
                catch (Exception) { }
            }
            if (unit.neededExperienceLevel.HasValue && _neededExperienceLevelField != null)
            {
                _neededExperienceLevelField.SetValue(row, unit.neededExperienceLevel.Value);
            }
            if (unit.systemTags != null && unit.systemTags.Length > 0 && _offeredSystemTagsField != null)
            {
                int combinedTags = 0;
                foreach (string tagName in unit.systemTags)
                {
                    if (!String.IsNullOrWhiteSpace(tagName))
                    {
                        try
                        {
                            combinedTags |= Convert.ToInt32(Enum.Parse(_offeredSystemTagsField.FieldType, tagName, true));
                        }
                        catch (Exception) { }
                    }
                }
                object tagsEnum = Enum.ToObject(_offeredSystemTagsField.FieldType, combinedTags);
                _offeredSystemTagsField.SetValue(row, tagsEnum);
            }
            if (unit.activeSkills != null && unit.activeSkills.Length > 0 && unit.activeSkills[0] != null)
            {
                ContentActiveSkillDefinition primarySkill = unit.activeSkills[0];
                if (primarySkill.range.HasValue && _skillRangeField != null)
                {
                    if (_skillRangeField.FieldType == typeof(int))
                    {
                        _skillRangeField.SetValue(row, (int)Math.Round(primarySkill.range.Value));
                    }
                    else
                    {
                        _skillRangeField.SetValue(row, primarySkill.range.Value);
                    }
                }
                if (_skillManaCostField != null)
                {
                    if (_skillManaCostField.FieldType == typeof(int))
                    {
                        _skillManaCostField.SetValue(row, (int)Math.Round(primarySkill.manaCost));
                    }
                    else
                    {
                        _skillManaCostField.SetValue(row, primarySkill.manaCost);
                    }
                }
            }
        }

        private void ApplyStructuredProductionBuildingParameters(
            object row,
            ContentProductionBuildingDefinition building,
            ContentUnitDefinition product = null)
        {
            if (row == null || building == null)
            {
                return;
            }

            if (building.gridSize.HasValue && _gridSizeField != null)
            {
                _gridSizeField.SetValue(row, building.gridSize.Value);
            }
            if (building.maxCapacity.HasValue && _maxCapacityField != null)
            {
                _maxCapacityField.SetValue(row, building.maxCapacity.Value);
            }
            if (building.cost.HasValue && _costField != null)
            {
                _costField.SetValue(row, building.cost.Value);
            }
            if (building.buildTime.HasValue && _productionDurationField != null)
            {
                _productionDurationField.SetValue(row, building.buildTime.Value);
            }
            if (building.combatValue.HasValue && _combatValueField != null)
            {
                _combatValueField.SetValue(row, building.combatValue.Value);
            }
            if (building.cardModelScalingFactor.HasValue && _cardModelScalingFactorField != null)
            {
                _cardModelScalingFactorField.SetValue(row, building.cardModelScalingFactor.Value);
            }

            int? effectiveExp = building.neededExperienceLevel ?? (product != null ? product.neededExperienceLevel : null);
            if (!effectiveExp.HasValue && product != null && product.balanceOverrides != null)
            {
                for (int i = 0; i < product.balanceOverrides.Length; i++)
                {
                    if (product.balanceOverrides[i] != null &&
                        String.Equals(product.balanceOverrides[i].name, "neededExperienceLevel", StringComparison.OrdinalIgnoreCase))
                    {
                        int exp;
                        if (Int32.TryParse(product.balanceOverrides[i].value, out exp))
                        {
                            effectiveExp = exp;
                        }
                        break;
                    }
                }
            }
            if (effectiveExp.HasValue && _neededExperienceLevelField != null)
            {
                _neededExperienceLevelField.SetValue(row, effectiveExp.Value);
            }

            string effectiveRarity = !String.IsNullOrEmpty(building.rarity) ? building.rarity : (product != null ? product.rarity : null);
            if (String.IsNullOrEmpty(effectiveRarity) && product != null && product.balanceOverrides != null)
            {
                for (int i = 0; i < product.balanceOverrides.Length; i++)
                {
                    if (product.balanceOverrides[i] != null &&
                        String.Equals(product.balanceOverrides[i].name, "rarity", StringComparison.OrdinalIgnoreCase))
                    {
                        effectiveRarity = product.balanceOverrides[i].value;
                        break;
                    }
                }
            }
            if (!String.IsNullOrEmpty(effectiveRarity) && _rarityField != null)
            {
                try
                {
                    object rarityEnum = Enum.Parse(_rarityField.FieldType, effectiveRarity, true);
                    _rarityField.SetValue(row, rarityEnum);
                }
                catch (Exception) { }
            }

            string effectiveTech = !String.IsNullOrEmpty(building.tech) ? building.tech : (product != null ? product.tech : null);
            if (String.IsNullOrEmpty(effectiveTech) && product != null && product.balanceOverrides != null)
            {
                for (int i = 0; i < product.balanceOverrides.Length; i++)
                {
                    if (product.balanceOverrides[i] != null &&
                        String.Equals(product.balanceOverrides[i].name, "tech", StringComparison.OrdinalIgnoreCase))
                    {
                        effectiveTech = product.balanceOverrides[i].value;
                        break;
                    }
                }
            }
            if (!String.IsNullOrEmpty(effectiveTech) && _techField != null)
            {
                try
                {
                    object techEnum = Enum.Parse(_techField.FieldType, effectiveTech, true);
                    _techField.SetValue(row, techEnum);
                }
                catch (Exception) { }
            }
            if (building.roles != null && building.roles.Length > 0 && _entityRolesField != null)
            {
                int combinedRoles = 0;
                foreach (string roleName in building.roles)
                {
                    if (!String.IsNullOrWhiteSpace(roleName))
                    {
                        try
                        {
                            combinedRoles |= Convert.ToInt32(Enum.Parse(_entityRolesField.FieldType, roleName, true));
                        }
                        catch (Exception) { }
                    }
                }
                try
                {
                    combinedRoles |= Convert.ToInt32(Enum.Parse(_entityRolesField.FieldType, "Building", true));
                    combinedRoles |= Convert.ToInt32(Enum.Parse(_entityRolesField.FieldType, "Factory", true));
                }
                catch (Exception) { }
                object rolesEnum = Enum.ToObject(_entityRolesField.FieldType, combinedRoles);
                _entityRolesField.SetValue(row, rolesEnum);
            }
        }

        private void ApplyStructuredBuildingParameters(
            object row,
            ContentBuildingDefinition building)
        {
            if (row == null || building == null)
            {
                return;
            }

            if (building.gridSize.HasValue && _gridSizeField != null)
            {
                _gridSizeField.SetValue(row, building.gridSize.Value);
            }
            if (building.maxCapacity.HasValue && _maxCapacityField != null)
            {
                _maxCapacityField.SetValue(row, building.maxCapacity.Value);
            }
            if (building.cost.HasValue && _costField != null)
            {
                _costField.SetValue(row, building.cost.Value);
            }
            if (building.buildTime.HasValue && _productionDurationField != null)
            {
                _productionDurationField.SetValue(row, building.buildTime.Value);
            }
            if (building.combatValue.HasValue && _combatValueField != null)
            {
                _combatValueField.SetValue(row, building.combatValue.Value);
            }
            if (building.cardModelScalingFactor.HasValue && _cardModelScalingFactorField != null)
            {
                _cardModelScalingFactorField.SetValue(row, building.cardModelScalingFactor.Value);
            }
            if (building.firePointCount.HasValue && _firePointCountField != null)
            {
                _firePointCountField.SetValue(row, building.firePointCount.Value);
            }
            if (building.neededExperienceLevel.HasValue && _neededExperienceLevelField != null)
            {
                _neededExperienceLevelField.SetValue(row, building.neededExperienceLevel.Value);
            }
            if (building.damage1.HasValue && _damage1Field != null)
            {
                _damage1Field.SetValue(row, building.damage1.Value);
            }
            if (building.attackCooldown.HasValue && _attackCooldownField != null)
            {
                _attackCooldownField.SetValue(row, building.attackCooldown.Value);
            }
            if (building.weaponRange.HasValue && _weaponRangeField != null)
            {
                _weaponRangeField.SetValue(row, building.weaponRange.Value);
            }
            if (building.damage2.HasValue && _damage2Field != null)
            {
                _damage2Field.SetValue(row, building.damage2.Value);
            }
            if (building.attack2Cooldown.HasValue && _attack2CooldownField != null)
            {
                _attack2CooldownField.SetValue(row, building.attack2Cooldown.Value);
            }
            if (building.effectRadius1.HasValue && _effectRadius1Field != null)
            {
                _effectRadius1Field.SetValue(row, building.effectRadius1.Value);
            }
            if (building.effectRadius2.HasValue && _effectRadius2Field != null)
            {
                _effectRadius2Field.SetValue(row, building.effectRadius2.Value);
            }
            if (building.skillRange.HasValue && _skillRangeField != null)
            {
                _skillRangeField.SetValue(row, building.skillRange.Value);
            }
            if (building.skillManaCost.HasValue && _skillManaCostField != null)
            {
                _skillManaCostField.SetValue(row, building.skillManaCost.Value);
            }
            if (building.healAmount1.HasValue && _healAmount1Field != null)
            {
                _healAmount1Field.SetValue(row, building.healAmount1.Value);
            }
            if (building.healAmount2.HasValue && _healAmount2Field != null)
            {
                _healAmount2Field.SetValue(row, building.healAmount2.Value);
            }
            if (building.duration1.HasValue && _duration1Field != null)
            {
                _duration1Field.SetValue(row, building.duration1.Value);
            }
            if (building.duration2.HasValue && _duration2Field != null)
            {
                _duration2Field.SetValue(row, building.duration2.Value);
            }
            if (!String.IsNullOrEmpty(building.attackType) && _attackTypeField != null)
            {
                try
                {
                    object attackTypeEnum = Enum.Parse(_attackTypeField.FieldType, building.attackType, true);
                    _attackTypeField.SetValue(row, attackTypeEnum);
                }
                catch (Exception) { }
            }
            if (!String.IsNullOrEmpty(building.skillType) && _skillTypeField != null)
            {
                try
                {
                    object skillTypeEnum = Enum.Parse(_skillTypeField.FieldType, building.skillType, true);
                    _skillTypeField.SetValue(row, skillTypeEnum);
                }
                catch (Exception) { }
            }
            if (!String.IsNullOrEmpty(building.rarity) && _rarityField != null)
            {
                try
                {
                    object rarityEnum = Enum.Parse(_rarityField.FieldType, building.rarity, true);
                    _rarityField.SetValue(row, rarityEnum);
                }
                catch (Exception) { }
            }
            if (!String.IsNullOrEmpty(building.tech) && _techField != null)
            {
                try
                {
                    object techEnum = Enum.Parse(_techField.FieldType, building.tech, true);
                    _techField.SetValue(row, techEnum);
                }
                catch (Exception) { }
            }
            if (building.roles != null && building.roles.Length > 0 && _entityRolesField != null)
            {
                int combinedRoles = 0;
                foreach (string roleName in building.roles)
                {
                    if (!String.IsNullOrWhiteSpace(roleName))
                    {
                        try
                        {
                            combinedRoles |= Convert.ToInt32(Enum.Parse(_entityRolesField.FieldType, roleName, true));
                        }
                        catch (Exception) { }
                    }
                }
                try
                {
                    combinedRoles |= Convert.ToInt32(Enum.Parse(_entityRolesField.FieldType, "Building", true));
                }
                catch (Exception) { }
                object rolesEnum = Enum.ToObject(_entityRolesField.FieldType, combinedRoles);
                _entityRolesField.SetValue(row, rolesEnum);
            }
            if (building.systemTags != null && building.systemTags.Length > 0 && _offeredSystemTagsField != null)
            {
                int combinedTags = 0;
                foreach (string tagName in building.systemTags)
                {
                    if (!String.IsNullOrWhiteSpace(tagName))
                    {
                        try
                        {
                            combinedTags |= Convert.ToInt32(Enum.Parse(_offeredSystemTagsField.FieldType, tagName, true));
                        }
                        catch (Exception) { }
                    }
                }
                object tagsEnum = Enum.ToObject(_offeredSystemTagsField.FieldType, combinedTags);
                _offeredSystemTagsField.SetValue(row, tagsEnum);
            }
        }

        private LoadedUnit CreateStandaloneBuilding(
            ContentBuildingDefinition building,
            string packId,
            string packageName,
            Dictionary<string, AssetBundle> temporaryBundles,
            List<GameObject> temporaryNativePrefabs,
            Dictionary<string, UnityEngine.Object> temporaryResources)
        {
            if (building == null || String.IsNullOrWhiteSpace(building.id) ||
                String.IsNullOrWhiteSpace(building.nativeTemplateId))
            {
                throw new InvalidDataException("El edificio contiene una referencia vacía en '" + packageName + "'.");
            }

            object row = CopyNativeBalanceRow(building.nativeTemplateId, packageName, building.id);
            if (!HasRoleFlag(row, "Building"))
            {
                throw new InvalidDataException("El nativeTemplateId '" + building.nativeTemplateId + "' de " +
                    building.id + " en '" + packageName + "' no tiene el rol Building.");
            }

            string nativePrefabPath = _prefabLocationField.GetValue(row) as string;
            string nativeImagePath = _imageLocationField.GetValue(row) as string;
            if (String.IsNullOrWhiteSpace(nativePrefabPath) || String.IsNullOrWhiteSpace(nativeImagePath))
            {
                throw new InvalidDataException("El edificio plantilla '" + building.nativeTemplateId +
                    "' no contiene rutas nativas de prefab e imagen válidas.");
            }

            GameObject nativeTemplate = Resources.Load<GameObject>(nativePrefabPath);
            if (nativeTemplate == null)
            {
                nativeTemplate = Resources.Load<GameObject>(nativePrefabPath.ToLowerInvariant());
            }

            if (nativeTemplate == null)
            {
                throw new InvalidDataException("El edificio " + building.id + " no pudo cargar el prefab nativo '" +
                    nativePrefabPath + "' de la plantilla " + building.nativeTemplateId + ".");
            }

            var localizationDefinition = new ContentUnitDefinition
            {
                id = building.id,
                nativeTemplateId = building.nativeTemplateId,
                assetBundleId = building.assetBundleId,
                cardImageAssetName = building.cardImageAssetName,
                visualAssetName = building.visualAssetName,
                visualRootPath = building.visualRootPath,
                localizations = building.localizations,
                isAllowedAsStartingBlueprint = building.isAllowedAsStartingBlueprint ?? false,
                isAllowedForAi = building.isAllowedForAi ?? true,
                isAllowedForDemo = building.isAllowedForDemo ?? true,
                isForSpecialists = building.isForSpecialists ?? false
            };
            ValidateUnitPrefab(nativeTemplate, localizationDefinition, packageName, false);

            GameObject visualTemplate = null;
            AssetBundle buildingAssetBundle = null;
            string imagePath = nativeImagePath;
            if (!String.IsNullOrWhiteSpace(building.cardImageAssetName) ||
                !String.IsNullOrWhiteSpace(building.visualAssetName))
            {
                buildingAssetBundle = GetBundle(temporaryBundles, building.assetBundleId, packageName);
            }
            if (!String.IsNullOrWhiteSpace(building.cardImageAssetName))
            {
                AssetBundle cardBundle = buildingAssetBundle ??
                    GetBundle(temporaryBundles, building.assetBundleId, packageName);
                Sprite cardImage = LoadRequiredAsset(cardBundle, building.cardImageAssetName,
                    typeof(Sprite), "building Sprite card image", packageName) as Sprite;
                imagePath = ResourceKey(building.id, "image");
                temporaryResources.Add(imagePath, cardImage);
            }

            if (!String.IsNullOrWhiteSpace(building.visualAssetName))
            {
                visualTemplate = LoadRequiredAsset(buildingAssetBundle, building.visualAssetName,
                    typeof(GameObject), "visual-only building GameObject prefab", packageName) as GameObject;
                ValidateVisualPrefab(visualTemplate, localizationDefinition, packageName, "Edificio");
            }

            GameObject nativePrefabClone = InstantiateDormantPrefab(nativeTemplate);
            if (nativePrefabClone == null)
            {
                throw new InvalidDataException("No se pudo clonar el prefab del edificio " + building.id + ".");
            }

            temporaryNativePrefabs.Add(nativePrefabClone);
            if (visualTemplate != null)
            {
                ApplyVisualOverlay(nativePrefabClone, visualTemplate, localizationDefinition, packageName,
                    "Edificio", true);
            }

            EntityController clonedController = nativePrefabClone.GetComponent<EntityController>();
            if (clonedController == null)
            {
                throw new InvalidDataException("El clon nativo del edificio " + building.id +
                    " perdió su EntityController raíz.");
            }

            EntityControllerIdentity.Assign(_entityControllerIdField, clonedController, building.id,
                "Edificio " + building.id + " en '" + packageName + "'");
            ApplyEntityBehavioralParameters(nativePrefabClone, building.rotationDegreesPerSecond, building.collisionRadius,
                building.attackWarmUpTime, building.canAttackWhileMoving ?? false, building.shootAutomaticallyOnEnemiesWithinRange,
                building.defendPositionAggressivelyWhenIdle, building.rangeToChooseNewTargetAutomatically, building.minAttackRange, building.id);
            ApplyEntityAudioBindings(nativePrefabClone, buildingAssetBundle, building.selectionSoundAssetName,
                building.engineSoundAssetName, building.weaponSoundAssetName, building.id, packageName);
            ApplyEntityCombatEnhancements(nativePrefabClone, building.onHitStatusEffects, building.criticalHitChance, building.id);
            EnsureMuzzleFlashHierarchy(nativePrefabClone);

            nativePrefabClone.name = "RCL_NativeBuilding_" + building.id.Replace(':', '_');
            nativePrefabClone.SetActive(false);
            nativePrefabClone.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(nativePrefabClone);

            string prefabKey = ResourceKey(building.id, "prefab");
            ApplyBalanceOverrides(row, building.balanceOverrides, packageName, building.id, false, false);
            ApplyStructuredBuildingParameters(row, building);
            _entityIdField.SetValue(row, building.id);
            _prefabLocationField.SetValue(row, prefabKey);
            _imageLocationField.SetValue(row, imagePath);
            _inactiveField.SetValue(row, false);
            _factoryForEntityIdField.SetValue(row, CreateEmptyNullableString());
            _copyUpgradesToEntityIdField.SetValue(row, CreateEmptyNullableString());

            bool isAllowedAsBlueprint = building.isAllowedAsBlueprint ?? true;
            _allowedBlueprintField.SetValue(row, isAllowedAsBlueprint);
            _allowedStartingBlueprintField.SetValue(row, building.isAllowedAsStartingBlueprint ?? false);
            _allowedForAiField.SetValue(row, building.isAllowedForAi ?? true);
            _allowedForDemoField.SetValue(row, building.isAllowedForDemo ?? true);
            if (_isForSpecialistsField != null)
            {
                _isForSpecialistsField.SetValue(row, building.isForSpecialists ?? false);
            }

            temporaryResources.Add(prefabKey, nativePrefabClone);

            int buildingPrespawn = building.prespawnCount ?? 2;
            return new LoadedUnit(building.id, packId, prefabKey, imagePath,
                row, localizationDefinition, false, null, buildingPrespawn, true);
        }

        private bool HasProductionBuildingRoles(object row)
        {
            if (row == null || row.GetType() != _entityRowType)
            {
                return false;
            }

            object value = _entityRolesField.GetValue(row);
            if (!(value is Enum))
            {
                return false;
            }

            return HasRoleFlag(row, "Factory") && HasRoleFlag(row, "Building");
        }

        private bool HasRoleFlag(object row, string roleName)
        {
            if (row == null || row.GetType() != _entityRowType)
            {
                return false;
            }

            object value = _entityRolesField.GetValue(row);
            if (!(value is Enum))
            {
                return false;
            }

            try
            {
                Enum roles = (Enum)value;
                Enum requiredRole = (Enum)Enum.Parse(_entityRolesField.FieldType, roleName, false);
                return roles.HasFlag(requiredRole);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("No se pudo interpretar el rol " + roleName +
                    " en EntityBalancingParameters.", exception);
            }
        }

        private void ApplyBalanceOverrides(
            object row,
            ContentBalanceOverride[] overrides,
            string packageName,
            string unitId,
            bool isProduct,
            bool isFactoryRow)
        {
            if (overrides == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < overrides.Length; i++)
            {
                ContentBalanceOverride balanceOverride = overrides[i];
                if (balanceOverride == null || String.IsNullOrWhiteSpace(balanceOverride.name) ||
                    !seen.Add(balanceOverride.name))
                {
                    throw new InvalidDataException("Unidad " + unitId + " en '" + packageName +
                        "': hay un override de balance nulo, vacío o duplicado.");
                }

                FieldInfo field = FindField(_entityRowType, balanceOverride.name, InstanceFields);
                object value;
                if (field.FieldType == typeof(int))
                {
                    int parsed;
                    if (!Int32.TryParse(balanceOverride.value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out parsed) || parsed < 0 ||
                        (UnitBalanceOverrideMapper.IsNativeIntegerBalanceRowField(balanceOverride.name) &&
                            parsed > UnitBalanceOverrideMapper.MaximumContiguousSingleExactInteger))
                    {
                        throw new InvalidDataException("Unidad " + unitId + " en '" + packageName +
                            "': valor entero inválido para " + balanceOverride.name + ".");
                    }
                    value = parsed;
                }
                else if (field.FieldType == typeof(float))
                {
                    float parsed;
                    if (!Single.TryParse(balanceOverride.value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out parsed) || Single.IsNaN(parsed) ||
                        Single.IsInfinity(parsed) || parsed < 0f)
                    {
                        throw new InvalidDataException("Unidad " + unitId + " en '" + packageName +
                            "': valor decimal inválido para " + balanceOverride.name + ".");
                    }
                    value = parsed;
                }
                else if (field.FieldType.IsEnum)
                {
                    try
                    {
                        if (balanceOverride.name == "roles" || balanceOverride.name == "offeredSystemTags")
                        {
                            if (!BalanceOverrideFlags.TryApply(row, field, balanceOverride.name,
                                balanceOverride.value, isProduct, isFactoryRow,
                                out object updatedRow, out string flagDiagnostic))
                            {
                                throw new InvalidDataException(flagDiagnostic);
                            }
                            row = updatedRow;
                            continue;
                        }
                        else
                        {
                            value = Enum.Parse(field.FieldType, balanceOverride.value, true);
                        }
                    }
                    catch
                    {
                        throw new InvalidDataException("Unidad " + unitId + " en '" + packageName +
                            "': valor de enum inválido para " + balanceOverride.name + ".");
                    }
                }
                else
                {
                    throw new InvalidDataException("Unidad " + unitId + " en '" + packageName +
                        "': el campo " + balanceOverride.name + " no es un override compatible.");
                }

                field.SetValue(row, value);
            }
        }

        private void ValidateUnitPrefab(GameObject prefab, ContentUnitDefinition unit, string packageName)
        {
            ValidateUnitPrefab(prefab, unit, packageName, true);
        }

        private void ValidateUnitPrefab(
            GameObject prefab,
            ContentUnitDefinition unit,
            string packageName,
            bool validateAssetName)
        {
            if (prefab == null)
            {
                throw new InvalidDataException("Unidad " + unit.id + " en '" + packageName + "': el prefab está vacío.");
            }

            if (validateAssetName && !ContentPackValidator.PrefabReferenceMatchesObjectName(
                unit.prefabAssetName, prefab.name))
            {
                throw new InvalidDataException("Unidad " + unit.id + " en '" + packageName + "': el nombre interno del prefab '" +
                    prefab.name + "' no coincide con prefabAssetName '" + unit.prefabAssetName + "'.");
            }

            ValidateNoMissingPrefabComponents(prefab, packageName, "Unidad " + unit.id);
            if (prefab.GetComponent(_entityControllerType) == null)
            {
                throw new InvalidDataException("Unidad " + unit.id + " en '" + packageName +
                    "': el prefab debe tener EntityController en el GameObject raíz.");
            }

            if (prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                throw new InvalidDataException("Unidad " + unit.id + " en '" + packageName +
                    "': el prefab no contiene ningún Renderer en su jerarquía visual.");
            }
        }

        private static void ValidateVisualPrefab(
            GameObject prefab,
            ContentUnitDefinition unit,
            string packageName,
            string ownerLabel = "Unidad")
        {
            if (prefab == null)
            {
                throw new InvalidDataException(ownerLabel + " " + unit.id + " en '" + packageName +
                    "': el prefab visual está vacío.");
            }

            if (!ContentPackValidator.PrefabReferenceMatchesObjectName(unit.visualAssetName, prefab.name))
            {
                throw new InvalidDataException(ownerLabel + " " + unit.id + " en '" + packageName +
                    "': el nombre interno del prefab visual '" + prefab.name +
                    "' no coincide con el alias visualAssetName '" + unit.visualAssetName + "'.");
            }

            ValidateNoMissingPrefabComponents(prefab, packageName, "Visual de " + ownerLabel + " " + unit.id);
            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is MonoBehaviour)
                {
                    throw new InvalidDataException("Visual de " + ownerLabel + " " + unit.id + " en '" + packageName +
                        "': los modelos de bundle no pueden incluir componentes MonoBehaviour.");
                }
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            bool hasEnabledRenderer = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].enabled &&
                    IsActiveVisualHierarchy(renderers[i].transform, prefab.transform))
                {
                    hasEnabledRenderer = true;
                    break;
                }
            }

            if (!hasEnabledRenderer)
            {
                throw new InvalidDataException("Visual de " + ownerLabel + " " + unit.id + " en '" + packageName +
                    "': el prefab debe contener al menos un Renderer habilitado.");
            }
        }

        private static bool IsActiveVisualHierarchy(Transform target, Transform prefabRoot)
        {
            Transform current = target;
            while (current != null && !System.Object.ReferenceEquals(current, prefabRoot))
            {
                if (!current.gameObject.activeSelf)
                {
                    return false;
                }

                current = current.parent;
            }

            return current != null;
        }

        private static void ApplyVisualOverlay(
            GameObject nativePrefab,
            GameObject visualTemplate,
            ContentUnitDefinition unit,
            string packageName,
            string ownerLabel = "Unidad",
            bool meshRenderersOnly = false)
        {
            Transform target = FindUniqueTransformPath(nativePrefab.transform, unit.visualRootPath);
            if (target == null)
            {
                throw new InvalidDataException(ownerLabel + " " + unit.id + " en '" + packageName +
                    "': visualRootPath '" + unit.visualRootPath +
                    "' no identifica una única ruta dentro de la jerarquía del prefab nativo.");
            }

            Renderer[] renderersUnderTarget = target.GetComponentsInChildren<Renderer>(true);
            var originalRenderers = new List<Renderer>();
            for (int i = 0; i < renderersUnderTarget.Length; i++)
            {
                Renderer renderer = renderersUnderTarget[i];
                if (renderer != null && (!meshRenderersOnly ||
                    renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                {
                    originalRenderers.Add(renderer);
                }
            }

            if (originalRenderers.Count == 0)
            {
                throw new InvalidDataException(ownerLabel + " " + unit.id + " en '" + packageName +
                    "': visualRootPath '" + unit.visualRootPath + "' no contiene Renderer nativos que reemplazar.");
            }

            Material fallbackMaterial = FindNativeVisualMaterial(originalRenderers.ToArray());

            for (int i = 0; i < originalRenderers.Count; i++)
            {
                originalRenderers[i].enabled = false;
            }

            GameObject visualClone = UnityEngine.Object.Instantiate(visualTemplate, target, false);
            if (visualClone == null)
            {
                throw new InvalidDataException(ownerLabel + " " + unit.id + " en '" + packageName +
                    "': Unity no pudo instanciar el prefab visual.");
            }

            visualClone.name = "RCL_Visual_" + unit.id.Replace(':', '_');
            visualClone.transform.localPosition = Vector3.zero;
            visualClone.transform.localRotation = Quaternion.identity;

            // Invert parent lossy scale so visualClone does not inherit arbitrary parent bone scales
            Vector3 targetLossy = target.lossyScale;
            float invX = Mathf.Abs(targetLossy.x) > 1e-5f ? (1f / targetLossy.x) : 1f;
            float invY = Mathf.Abs(targetLossy.y) > 1e-5f ? (1f / targetLossy.y) : 1f;
            float invZ = Mathf.Abs(targetLossy.z) > 1e-5f ? (1f / targetLossy.z) : 1f;

            // Compensate for Blender FBX 0.01x unit scaling
            float maxMeshExtent = CalculateLocalMeshExtent(visualClone);
            float authoringCompensation = (maxMeshExtent > 0.0001f && maxMeshExtent < 0.8f) ? 100f : (maxMeshExtent <= 0.0001f ? 100f : 1f);

            visualClone.transform.localScale = new Vector3(
                invX * authoringCompensation,
                invY * authoringCompensation,
                invZ * authoringCompensation
            );

            ApplyFallbackVisualMaterial(visualClone, fallbackMaterial, unit, packageName, ownerLabel);
            visualClone.SetActive(true);
        }

        private static float CalculateLocalMeshExtent(GameObject model)
        {
            if (model == null) return 0f;
            float maxExtent = 0f;
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] != null && filters[i].sharedMesh != null)
                {
                    Mesh mesh = filters[i].sharedMesh;
                    Vector3 ext = mesh.bounds.extents;
                    if (ext.sqrMagnitude < 1e-6f)
                    {
                        try { mesh.RecalculateBounds(); } catch { }
                        ext = mesh.bounds.extents;
                    }
                    maxExtent = Mathf.Max(maxExtent, Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z)));
                }
            }
            SkinnedMeshRenderer[] skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinned.Length; i++)
            {
                if (skinned[i] != null)
                {
                    if (skinned[i].sharedMesh != null)
                    {
                        Mesh mesh = skinned[i].sharedMesh;
                        Vector3 ext = mesh.bounds.extents;
                        if (ext.sqrMagnitude < 1e-6f)
                        {
                            try { mesh.RecalculateBounds(); } catch { }
                            ext = mesh.bounds.extents;
                        }
                        maxExtent = Mathf.Max(maxExtent, Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z)));
                    }
                    Vector3 localExt = skinned[i].localBounds.extents;
                    maxExtent = Mathf.Max(maxExtent, Mathf.Max(localExt.x, Mathf.Max(localExt.y, localExt.z)));
                }
            }
            return maxExtent;
        }

        private static Transform FindUniqueTransformPath(Transform root, string path)
        {
            if (root == null || String.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string[] segments = path.Split('/');
            Transform current = root;
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                Transform match = null;
                for (int childIndex = 0; childIndex < current.childCount; childIndex++)
                {
                    Transform child = current.GetChild(childIndex);
                    if (!String.Equals(child.name, segments[segmentIndex], StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (match != null)
                    {
                        return null;
                    }

                    match = child;
                }

                if (match == null)
                {
                    return null;
                }

                current = match;
            }

            return current;
        }

        private static Material FindNativeVisualMaterial(Renderer[] renderers)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!(renderers[i] is MeshRenderer) && !(renderers[i] is SkinnedMeshRenderer))
                {
                    continue;
                }

                Material[] materials = renderers[i].sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (materials[materialIndex] != null)
                    {
                        return materials[materialIndex];
                    }
                }
            }

            return null;
        }

        private static void ApplyFallbackVisualMaterial(
            GameObject visualClone,
            Material fallbackMaterial,
            ContentUnitDefinition unit,
            string packageName,
            string ownerLabel = "Unidad")
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            Renderer[] renderers = visualClone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                bool hasAnyMaterial = false;
                if (materials != null && materials.Length > 0)
                {
                    for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        Material mat = materials[materialIndex];
                        if (mat != null)
                        {
                            hasAnyMaterial = true;
                            // Ensure shader is valid, supported, and not an error/fallback shader
                            if (mat.shader == null || !mat.shader.isSupported ||
                                mat.shader.name.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mat.shader.name.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (litShader != null && litShader.isSupported)
                                {
                                    mat.shader = litShader;
                                }
                                else if (fallbackMaterial != null && fallbackMaterial.shader != null && fallbackMaterial.shader.isSupported)
                                {
                                    mat.shader = fallbackMaterial.shader;
                                }
                            }

                            // Synchronize textures between _BaseMap and _MainTex so textures are never lost
                            Texture mainTex = null;
                            if (mat.HasProperty("_BaseMap"))
                            {
                                mainTex = mat.GetTexture("_BaseMap");
                            }
                            if (mainTex == null && mat.HasProperty("_MainTex"))
                            {
                                mainTex = mat.GetTexture("_MainTex");
                            }
                            if (mainTex != null)
                            {
                                if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == null)
                                {
                                    mat.SetTexture("_BaseMap", mainTex);
                                }
                                if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") == null)
                                {
                                    mat.SetTexture("_MainTex", mainTex);
                                }
                            }
                        }
                    }
                }

                if (!hasAnyMaterial)
                {
                    if (fallbackMaterial == null)
                    {
                        throw new InvalidDataException(ownerLabel + " " + unit.id + " en '" + packageName +
                            "': el modelo visual no tiene materiales y la plantilla nativa no ofrece un material MeshRenderer.");
                    }

                    renderers[i].sharedMaterial = fallbackMaterial;
                }
            }
        }

        private static GameObject InstantiateResourceTemplate(GameObject template, out GameObject holder)
        {
            holder = null;
            if (template == null)
            {
                return null;
            }

            holder = new GameObject("RCL_ResourceTemplateHolder");
            holder.hideFlags = HideFlags.HideAndDontSave;
            holder.SetActive(false);
            try
            {
                // Keep activeSelf on the stored template so a game Instantiate produces an active AI,
                // while its inactive parent suppresses Awake/OnEnable on this registry-only clone.
                GameObject clone = UnityEngine.Object.Instantiate(template, holder.transform, false);
                if (clone == null)
                {
                    UnityEngine.Object.Destroy(holder);
                    holder = null;
                    return null;
                }

                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = Quaternion.identity;
                clone.transform.localScale = Vector3.one;
                return clone;
            }
            catch
            {
                if (holder != null)
                {
                    UnityEngine.Object.Destroy(holder);
                }
                holder = null;
                throw;
            }
        }

        private static GameObject InstantiateDormantPrefab(GameObject template)
        {
            if (template == null)
            {
                return null;
            }

            var inactiveHolder = new GameObject("RCL_DormantTemplateHolder");
            inactiveHolder.SetActive(false);
            GameObject clone = null;
            try
            {
                // Instantiate beneath an inactive parent so active native prefabs do not
                // run Awake/OnEnable on the loader's stored template clone.
                clone = UnityEngine.Object.Instantiate(template, inactiveHolder.transform, false);
                if (clone != null)
                {
                    clone.SetActive(false);
                    clone.transform.SetParent(null, false);
                }

                return clone;
            }
            catch
            {
                if (clone != null)
                {
                    UnityEngine.Object.Destroy(clone);
                }

                throw;
            }
            finally
            {
                if (inactiveHolder != null)
                {
                    UnityEngine.Object.Destroy(inactiveHolder);
                }
            }
        }

        private UnityEngine.Object ResolveStageFallback(ContentStageDefinition stage,
            UnityEngine.Object nativeSourceParameters, List<object> worlds, string packageName)
        {
            object balancing = _gameBalancingAssetField.GetValue(null);
            IList fallbacks = balancing == null ? null : _fallbackLandscapeGeneratorsField.GetValue(balancing) as IList;
            if (fallbacks == null || fallbacks.Count == 0)
            {
                throw new InvalidDataException("Etapa " + stage.id + ": no hay generadores de respaldo nativos.");
            }

            bool isDemo = (bool)_gameIsDemoField.GetValue(balancing) && stage.isPartOfDemo;
            if (nativeSourceParameters != null)
            {
                foreach (FieldInfo profileField in _progressionFields)
                {
                    object profile = profileField.GetValue(balancing);
                    IList nativeStages = profile == null ? null : _progressionStagesField.GetValue(profile) as IList;
                    if (nativeStages == null) continue;
                    for (int i = 0; i < nativeStages.Count && i < fallbacks.Count; i++)
                    {
                        if (nativeStages[i] != null && System.Object.ReferenceEquals(
                            _stageParametersField.GetValue(nativeStages[i]), nativeSourceParameters) &&
                            IsCompatibleStageFallback(fallbacks[i], stage, worlds, isDemo))
                        {
                            return (UnityEngine.Object)fallbacks[i];
                        }
                    }
                }
            }

            // Bundled stages and templates absent from the progression use a native
            // fallback of compatible size/worlds, with stable list order breaking ties.
            UnityEngine.Object selected = null;
            double bestDistance = Double.MaxValue;
            double midpoint = (stage.mapSizeMin + stage.mapSizeMax) / 2.0;
            foreach (object candidate in fallbacks)
            {
                if (!IsCompatibleStageFallback(candidate, stage, worlds, isDemo)) continue;
                double distance = Math.Abs((int)_landscapeMapSizeField.GetValue(candidate) - midpoint);
                if (distance < bestDistance)
                {
                    selected = (UnityEngine.Object)candidate;
                    bestDistance = distance;
                }
            }

            if (selected == null)
            {
                throw new InvalidDataException("Etapa " + stage.id + " en '" + packageName +
                    "': ningún generador de respaldo nativo respeta su tamaño, demo y Worlds.");
            }

            return selected;
        }

        private bool IsCompatibleStageFallback(object candidate, ContentStageDefinition stage,
            List<object> worlds, bool isDemo)
        {
            if (!(candidate is UnityEngine.Object) || candidate.GetType() != _landscapeGeneratorType)
                return false;
            int size = (int)_landscapeMapSizeField.GetValue(candidate);
            if (size < stage.mapSizeMin || size > stage.mapSizeMax || size % 2 == 0 ||
                (isDemo && !(bool)_landscapeAllowedForDemoField.GetValue(candidate)))
                return false;

            IList restrictions = _landscapeRestrictedWorldsField.GetValue(candidate) as IList;
            if (restrictions == null) return false;
            if (restrictions.Count == 0) return true;
            foreach (object world in worlds)
            {
                if ((bool)_worldDeactivatedField.GetValue(world) ||
                    (isDemo && !(bool)_worldAllowedForDemoField.GetValue(world))) continue;
                bool found = false;
                foreach (object allowed in restrictions)
                    if (System.Object.ReferenceEquals(allowed, world)) { found = true; break; }
                if (!found) return false;
            }

            return true;
        }

        private UnityEngine.Object FindNativeStageTemplate(string templateId, string packageName, string stageId)
        {
            UnityEngine.Object[] candidates = Resources.FindObjectsOfTypeAll(_stageParametersType);
            UnityEngine.Object match = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                UnityEngine.Object candidate = candidates[i];
                if (candidate == null || !String.Equals(candidate.name, templateId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidDataException("Etapa " + stageId + " en '" + packageName +
                        "': la plantilla nativa '" + templateId + "' no es única entre los objetos cargados.");
                }

                match = candidate;
            }

            if (match == null)
            {
                throw new InvalidDataException("Etapa " + stageId + " en '" + packageName +
                    "': no se encontró la plantilla StageParametersScriptableObject '" + templateId +
                    "' en los recursos cargados del juego.");
            }

            return match;
        }

        private DefaultAiBehaviour FindNativeAiTemplate(string templateId, string packageName, string profileId)
        {
            UnityEngine.Object[] candidates = Resources.FindObjectsOfTypeAll(_defaultAiBehaviourType);
            DefaultAiBehaviour match = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                DefaultAiBehaviour candidate = candidates[i] as DefaultAiBehaviour;
                if (candidate == null || candidate.gameObject == null ||
                    !String.Equals(candidate.gameObject.name, templateId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null && !System.Object.ReferenceEquals(match, candidate))
                {
                    throw new InvalidDataException("Perfil AI " + profileId + " en '" + packageName +
                        "': la plantilla nativa '" + templateId + "' no es única entre los objetos cargados.");
                }

                match = candidate;
            }

            if (match == null)
            {
                throw new InvalidDataException("Perfil AI " + profileId + " en '" + packageName +
                    "': no se encontró DefaultAiBehaviour nativo '" + templateId + "'.");
            }

            return match;
        }

        private static void ValidateNoMissingPrefabComponents(GameObject prefab, string packageName, string owner)
        {
            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    throw new InvalidDataException(owner + " en '" + packageName +
                        "': el prefab contiene un script sin resolver en un componente de su jerarquía.");
                }
            }
        }

        private void AppendAiUnitEntries(
            ContentAiProfileDefinition profile,
            IList aiBases,
            List<LoadedUnit> temporaryUnits,
            string packageName)
        {
            ContentAiUnitEntryDefinition[] unitEntries = profile.unitEntries ??
                new ContentAiUnitEntryDefinition[0];
            if (unitEntries.Length == 0)
            {
                return;
            }

            if (String.IsNullOrWhiteSpace(profile.nativeTemplateId) || !profile.scopeToReferencedStages)
            {
                throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                    "': las entradas de unidad requieren un clon nativo aislado y limitado a sus etapas.");
            }

            for (int i = 0; i < unitEntries.Length; i++)
            {
                ContentAiUnitEntryDefinition definition = unitEntries[i];
                if (definition == null || String.IsNullOrWhiteSpace(definition.unitId))
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': la entrada de unidad " + i + " está vacía.");
                }

                if (!IsProductUnitAvailable(definition.unitId, temporaryUnits))
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': la unidad " + definition.unitId + " no está registrada ni disponible.");
                }

                if (!IsProductUnitAllowedForAi(definition.unitId, temporaryUnits))
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': la unidad " + definition.unitId +
                        " no tiene isAllowedForAi habilitado.");
                }

                if (definition.baseIndex < 0 || definition.baseIndex >= aiBases.Count)
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': baseIndex " + definition.baseIndex + " no existe en la plantilla nativa.");
                }

                if (definition.startingInAscensionLevel < 0 ||
                    definition.startingInAscensionLevel > ContentPackValidator.MaxAiUnitStartingAscensionLevel ||
                    definition.countAtStart < 1 ||
                    definition.countAtStart > ContentPackValidator.MaxAiUnitCountAtStart ||
                    definition.maxLiving < definition.countAtStart ||
                    definition.maxLiving > ContentPackValidator.MaxAiUnitMaxLiving)
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': la configuración de cantidad o ascensión de " + definition.unitId + " no es válida.");
                }

                object aiBase = aiBases[definition.baseIndex];
                if (aiBase == null || aiBase.GetType() != _aiBaseType)
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': la base " + definition.baseIndex + " tiene un tipo inesperado.");
                }

                IList deckEntries = _aiBaseEntitiesInDeckField.GetValue(aiBase) as IList;
                if (deckEntries == null)
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': entitiesInAiDeck es nulo en la base " + definition.baseIndex + ".");
                }

                for (int deckIndex = 0; deckIndex < deckEntries.Count; deckIndex++)
                {
                    object current = deckEntries[deckIndex];
                    if (current == null || current.GetType() != _aiDeckEntryType)
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                            "': una entrada nativa de entitiesInAiDeck tiene un tipo inesperado.");
                    }

                    string existingId = _aiDeckEntryEntityIdField.GetValue(current) as string;
                    if (String.Equals(existingId, definition.unitId, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                            "': la unidad " + definition.unitId + " ya aparece en el deck de la base " +
                            definition.baseIndex + ".");
                    }
                }

                object newEntry = Activator.CreateInstance(_aiDeckEntryType);
                _aiDeckEntryEntityIdField.SetValue(newEntry, definition.unitId);
                _aiDeckEntryStartingInAscensionLevelField.SetValue(
                    newEntry, definition.startingInAscensionLevel);

                object countPerStage = Activator.CreateInstance(_aiDeckEntryCountPerStageField.FieldType);
                IList counts = countPerStage as IList;
                if (counts == null)
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': no se pudo crear la lista countPerStage de EntityInAiDeck.");
                }

                // EntityInAiDeck clamps stage access to the last list item. A single pair therefore
                // supplies the same starting/max-living values throughout this scoped AI profile.
                counts.Add(new Vector2Int(definition.countAtStart, definition.maxLiving));
                _aiDeckEntryCountPerStageField.SetValue(newEntry, countPerStage);
                deckEntries.Add(newEntry);
            }
        }

        private void AppendAiProductionBuildingEntries(
            ContentAiProfileDefinition profile,
            IList aiBases,
            List<LoadedUnit> temporaryUnits,
            string packageName)
        {
            ContentAiProductionBuildingEntryDefinition[] entries = profile.productionBuildingEntries ??
                new ContentAiProductionBuildingEntryDefinition[0];
            if (entries.Length == 0)
            {
                return;
            }

            if (String.IsNullOrWhiteSpace(profile.nativeTemplateId) || !profile.scopeToReferencedStages)
            {
                throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                    "': las fábricas requieren un clon nativo aislado y limitado a sus etapas.");
            }

            for (int i = 0; i < entries.Length; i++)
            {
                ContentAiProductionBuildingEntryDefinition definition = entries[i];
                if (definition == null || String.IsNullOrWhiteSpace(definition.unitId))
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': la entrada de fábrica " + i + " está vacía.");
                }

                LoadedUnit product;
                if (!TryGetLoadedUnit(definition.unitId, temporaryUnits, out product) ||
                    product.IsProductionBuilding || String.IsNullOrWhiteSpace(product.ProductionFactoryId))
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': la unidad " + definition.unitId + " no tiene una fábrica de producción cargada.");
                }

                if (!product.Definition.isAllowedForAi)
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': la unidad " + definition.unitId + " no tiene isAllowedForAi habilitado.");
                }

                LoadedUnit factory;
                if (!TryGetLoadedUnit(product.ProductionFactoryId, temporaryUnits, out factory) ||
                    !factory.IsProductionBuilding || !(bool)_allowedForAiField.GetValue(factory.BalanceRow))
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': la fábrica " + product.ProductionFactoryId + " no está habilitada para AI.");
                }

                if (definition.baseIndex < 0 || definition.baseIndex >= aiBases.Count)
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': baseIndex " + definition.baseIndex + " no existe en la plantilla nativa.");
                }

                if (definition.startingInAscensionLevel < 0 ||
                    definition.startingInAscensionLevel > ContentPackValidator.MaxAiUnitStartingAscensionLevel ||
                    definition.maxLiving < 1 ||
                    definition.maxLiving > ContentPackValidator.MaxAiUnitMaxLiving)
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': la configuración de cantidad o ascensión de la fábrica " +
                        product.ProductionFactoryId + " no es válida.");
                }

                object aiBase = aiBases[definition.baseIndex];
                if (aiBase == null || aiBase.GetType() != _aiBaseType)
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': la base " + definition.baseIndex + " tiene un tipo inesperado.");
                }

                IList deckEntries = _aiBaseEntitiesInDeckField.GetValue(aiBase) as IList;
                if (deckEntries == null)
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': entitiesInAiDeck es nulo en la base " + definition.baseIndex + ".");
                }

                for (int deckIndex = 0; deckIndex < deckEntries.Count; deckIndex++)
                {
                    object current = deckEntries[deckIndex];
                    if (current == null || current.GetType() != _aiDeckEntryType)
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                            "': una entrada nativa de entitiesInAiDeck tiene un tipo inesperado.");
                    }

                    string existingId = _aiDeckEntryEntityIdField.GetValue(current) as string;
                    if (String.Equals(existingId, product.ProductionFactoryId, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                            "': la fábrica " + product.ProductionFactoryId + " ya aparece en el deck de la base " +
                            definition.baseIndex + ".");
                    }
                }

                object newEntry = Activator.CreateInstance(_aiDeckEntryType);
                _aiDeckEntryEntityIdField.SetValue(newEntry, product.ProductionFactoryId);
                _aiDeckEntryStartingInAscensionLevelField.SetValue(
                    newEntry, definition.startingInAscensionLevel);

                object countPerStage = Activator.CreateInstance(_aiDeckEntryCountPerStageField.FieldType);
                IList counts = countPerStage as IList;
                if (counts == null)
                {
                    throw new InvalidDataException("Perfil AI " + profile.id + " en '" + packageName +
                        "': no se pudo crear la lista countPerStage de EntityInAiDeck.");
                }

                // The native build helper sees the factory as constructable, but base setup does not
                // start one for free. The scheduled module can build it when the profile requests it.
                counts.Add(new Vector2Int(0, definition.maxLiving));
                _aiDeckEntryCountPerStageField.SetValue(newEntry, countPerStage);
                deckEntries.Add(newEntry);
            }
        }

        private bool DeclaresAiDeckEntityDependency(
            ContentAiProfileDefinition profile,
            string deckEntityId,
            List<LoadedUnit> temporaryUnits)
        {
            if (ContentPackValidator.DeclaresAiUnitDependency(profile, deckEntityId))
            {
                return true;
            }

            foreach (ContentAiProductionBuildingEntryDefinition entry in
                profile.productionBuildingEntries ?? new ContentAiProductionBuildingEntryDefinition[0])
            {
                if (entry == null || String.IsNullOrWhiteSpace(entry.unitId))
                {
                    continue;
                }

                LoadedUnit product;
                if (TryGetLoadedUnit(entry.unitId, temporaryUnits, out product) &&
                    !product.IsProductionBuilding &&
                    String.Equals(product.ProductionFactoryId, deckEntityId, StringComparison.Ordinal) &&
                    ContentPackValidator.DeclaresAiUnitDependency(profile, entry.unitId))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsProductUnitAvailable(string entityId, List<LoadedUnit> temporaryUnits)
        {
            if (_nativeUnitIds.Contains(entityId) && !_nativeFactoryIds.Contains(entityId))
            {
                return true;
            }

            LoadedUnit unit;
            return TryGetLoadedUnit(entityId, temporaryUnits, out unit) && !unit.IsProductionBuilding;
        }

        private bool IsProductUnitAllowedForAi(string entityId, List<LoadedUnit> temporaryUnits)
        {
            LoadedUnit loadedUnit;
            if (TryGetLoadedUnit(entityId, temporaryUnits, out loadedUnit))
            {
                return loadedUnit != null && !loadedUnit.IsProductionBuilding &&
                    loadedUnit.Definition != null && loadedUnit.Definition.isAllowedForAi;
            }

            if (!_nativeUnitIds.Contains(entityId) || _nativeFactoryIds.Contains(entityId))
            {
                return false;
            }

            IList rows = _nativeEntityRowsField.GetValue(null) as IList;
            IDictionary index = _entityIndexField.GetValue(null) as IDictionary;
            if (rows == null || index == null || !index.Contains(entityId))
            {
                return false;
            }

            object rowIndexValue = index[entityId];
            if (!(rowIndexValue is int))
            {
                return false;
            }

            int rowIndex = (int)rowIndexValue;

            if (rowIndex < 0 || rowIndex >= rows.Count)
            {
                return false;
            }

            object row = rows[rowIndex];
            return row != null && row.GetType() == _entityRowType &&
                String.Equals(_entityIdField.GetValue(row) as string, entityId, StringComparison.Ordinal) &&
                (bool)_allowedForAiField.GetValue(row);
        }

        private bool IsDeckEntityAvailable(string entityId, List<LoadedUnit> temporaryUnits)
        {
            if (_nativeUnitIds.Contains(entityId))
            {
                return true;
            }

            LoadedUnit unit;
            return TryGetLoadedUnit(entityId, temporaryUnits, out unit);
        }

        private bool IsDeckEntityNativeOrPackLocal(string entityId, List<LoadedUnit> temporaryUnits)
        {
            if (_nativeUnitIds.Contains(entityId))
            {
                return true;
            }

            if (temporaryUnits == null)
            {
                return false;
            }

            for (int i = 0; i < temporaryUnits.Count; i++)
            {
                if (String.Equals(temporaryUnits[i].Id, entityId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsString(string[] values, string expected)
        {
            if (values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (String.Equals(values[i], expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private List<object> ValidateStageParameters(
            UnityEngine.Object asset,
            string packageName,
            string stageId,
            out List<object> validatedWorlds)
        {
            validatedWorlds = new List<object>();
            Type type = asset.GetType();
            int levelCount = ReadIntField(type, asset, "levelCount");
            int maxWidth = ReadIntField(type, asset, "maxWidth");
            if (levelCount < 1 || maxWidth < 1)
            {
                throw new InvalidDataException("Etapa " + stageId + " en '" + packageName +
                    "' debe tener levelCount y maxWidth mayores que cero.");
            }

            object poolList = FindField(type, "blueprintPoolList", InstanceFields).GetValue(asset);
            object worldList = FindField(type, "worldList", InstanceFields).GetValue(asset);
            if (poolList == null || poolList.GetType() != _blueprintPoolListType ||
                worldList == null || worldList.GetType() != _worldListType)
            {
                throw new InvalidDataException("Etapa " + stageId +
                    " debe contener BlueprintPoolList y WorldList del tipo nativo esperado.");
            }

            IList pools = _blueprintPoolsField.GetValue(poolList) as IList;
            IList worlds = _worldsField.GetValue(worldList) as IList;
            if (pools == null || pools.Count == 0 || worlds == null || worlds.Count == 0)
            {
                throw new InvalidDataException("Etapa " + stageId +
                    " debe incluir al menos un BlueprintPool y un World.");
            }

            var result = new List<object>();
            var poolIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < pools.Count; i++)
            {
                object pool = pools[i];
                if (pool == null || pool.GetType() != _blueprintPoolType ||
                    (pool as UnityEngine.Object) == null)
                {
                    throw new InvalidDataException("Etapa " + stageId + " contiene un elemento que no es BlueprintPool.");
                }

                string poolId = _blueprintPoolIdField.GetValue(pool) as string;
                if (String.IsNullOrWhiteSpace(poolId) || !poolIds.Add(poolId))
                {
                    throw new InvalidDataException("Etapa " + stageId +
                        " contiene IDs de BlueprintPool vacíos o duplicados.");
                }

                if (IsNullOrDestroyedUnityObject(_blueprintFilterField.GetValue(pool)))
                {
                    throw new InvalidDataException("BlueprintPool " + poolId + " de la etapa " + stageId +
                        " no tiene un filtro de blueprint.");
                }

                MethodInfo refresh = pool.GetType().GetMethod("OnBlueprintFilterChanged", InstanceFields);
                if (refresh == null || refresh.GetParameters().Length != 0 || refresh.ReturnType != typeof(void))
                {
                    throw new InvalidDataException("BlueprintPool de la etapa " + stageId +
                        " no contiene OnBlueprintFilterChanged(): void.");
                }

                result.Add(pool);
            }

            for (int i = 0; i < worlds.Count; i++)
            {
                object world = worlds[i];
                Component map = world as Component;
                if (world == null || map == null || world.GetType() != _initMapType)
                {
                    throw new InvalidDataException("Etapa " + stageId +
                        " contiene un World nulo, de tipo inesperado o inactivo.");
                }

                object worldGenerator = _worldGeneratorField.GetValue(world);
                if (IsNullOrDestroyedUnityObject(worldGenerator))
                {
                    throw new InvalidDataException("Etapa " + stageId +
                        " referencia un InitMap sin WorldGenerator.");
                }

                ValidateNoMissingPrefabComponents(map.gameObject, packageName, "World de la etapa " + stageId);
                validatedWorlds.Add(world);
            }

            return result;
        }

        private LoadedLandscape CreateStageLandscape(
            ContentStageDefinition stage,
            string packageName,
            IList<object> stageWorlds,
            Dictionary<string, UnityEngine.Object> temporaryResources,
            List<ScriptableObject> temporaryLandscapeClones)
        {
            ContentStageLandscapeDefinition definition = stage.landscape;
            string resourceName;
            if (!ContentPackValidator.TryBuildLandscapeResourceName(stage.id, out resourceName))
            {
                throw new InvalidDataException("La etapa " + stage.id + " no produce un nombre Landscape RCL_ válido.");
            }

            string resourcePath = "LandscapeGenerators/" + resourceName;
            if (temporaryResources.ContainsKey(resourcePath) || _resourceAssets.ContainsKey(resourcePath) ||
                Resources.Load(resourcePath, _landscapeGeneratorType) != null)
            {
                throw new InvalidDataException("La ruta de recurso Landscape '" + resourcePath +
                    "' ya existe en el juego o en otro paquete.");
            }

            UnityEngine.Object template = FindNativeLandscapeTemplate(
                definition.nativeTemplateId, packageName, stage.id);
            UnityEngine.Object landscapeObject = UnityEngine.Object.Instantiate(template);
            if (landscapeObject == null || landscapeObject.GetType() != _landscapeGeneratorType)
            {
                if (landscapeObject != null)
                {
                    UnityEngine.Object.Destroy(landscapeObject);
                }
                throw new InvalidDataException("No se pudo clonar el LandscapeGenerator para " + stage.id + ".");
            }

            ScriptableObject landscapeClone = landscapeObject as ScriptableObject;
            if (landscapeClone == null)
            {
                UnityEngine.Object.Destroy(landscapeObject);
                throw new InvalidDataException("LandscapeGenerator no deriva de ScriptableObject en este build.");
            }

            temporaryLandscapeClones.Add(landscapeClone);
            landscapeClone.name = resourceName;
            landscapeClone.hideFlags = HideFlags.HideAndDontSave;
            _landscapeMapSizeField.SetValue(landscapeClone, definition.mapSize);
            _landscapeRandomSeedField.SetValue(landscapeClone, definition.randomSeed);
            _landscapeAlwaysUseSameSeedField.SetValue(landscapeClone, definition.alwaysUseSameSeed);
            _landscapeDeactivatedField.SetValue(landscapeClone, false);
            _landscapeAllowedForDemoField.SetValue(landscapeClone, stage.isPartOfDemo);
            string localizationId = LandscapeLocalizationId(resourceName);
            _landscapeNameLocaIdField.SetValue(landscapeClone, localizationId);

            IList restrictedWorlds = _landscapeRestrictedWorldsField.GetValue(landscapeClone) as IList;
            if (restrictedWorlds == null)
            {
                throw new InvalidDataException("LandscapeGenerator " + resourceName +
                    " contiene restrictedToWorlds nulo.");
            }
            restrictedWorlds.Clear();

            if (stageWorlds == null || stageWorlds.Count == 0)
            {
                throw new InvalidDataException("La etapa " + stage.id + " no tiene Worlds para validar su mapa.");
            }

            temporaryResources.Add(resourcePath, landscapeClone);
            return new LoadedLandscape(resourceName, resourcePath, localizationId,
                landscapeClone, definition);
        }

        private UnityEngine.Object FindNativeLandscapeTemplate(
            string templateId,
            string packageName,
            string stageId)
        {
            object balancing = _gameBalancingAssetField.GetValue(null);
            object progression = balancing == null ? null : _progressionFields[0].GetValue(balancing);
            IList landscapes = progression == null ? null :
                _progressionLandscapesField.GetValue(progression) as IList;
            if (landscapes == null)
            {
                throw new InvalidDataException("No se pudo leer la lista nativa de LandscapeGenerator para " +
                    "resolver " + templateId + " en " + stageId + ".");
            }

            UnityEngine.Object match = null;
            for (int i = 0; i < landscapes.Count; i++)
            {
                UnityEngine.Object candidate = landscapes[i] as UnityEngine.Object;
                if (candidate == null || candidate.GetType() != _landscapeGeneratorType ||
                    !String.Equals(candidate.name, templateId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null && !System.Object.ReferenceEquals(match, candidate))
                {
                    throw new InvalidDataException("La plantilla nativa Landscape '" + templateId +
                        "' es ambigua en la progresión default para la etapa " + stageId + ".");
                }

                match = candidate;
            }

            if (match == null)
            {
                throw new InvalidDataException("La etapa " + stageId + " en '" + packageName +
                    "' referencia un LandscapeGenerator nativo que no existe en la progresión default: '" +
                    templateId + "'.");
            }

            return match;
        }

        private static string LandscapeLocalizationId(string resourceName)
        {
            return resourceName.ToLowerInvariant() + "_name";
        }

        private void ValidateStageGenerationCompatibility(
            List<LoadedStage> candidateStages,
            List<LoadedAiProfile> candidateProfiles,
            string packageName)
        {
            if (candidateStages == null || candidateStages.Count == 0)
            {
                return;
            }

            object balancing = _gameBalancingAssetField.GetValue(null);
            if (balancing == null)
            {
                throw new InvalidDataException("No se pudo validar la generación de etapas en '" + packageName +
                    "': GameBalancingStore aún no tiene su GameBalancingScriptableObject.");
            }

            bool isDemo = (bool)_gameIsDemoField.GetValue(balancing);
            // MetaGame clamps CurrentAscensionLevel to maxAscensionLevel in every mode,
            // including demo; maxAscensionLevelDemo is not the runtime selection cap.
            int maxAscensionLevel = (int)_gameMaxAscensionLevelField.GetValue(balancing);
            object defaultProgression = _progressionFields[0].GetValue(balancing);
            object[] progressionProfiles = StageGenerationCompatibility.ResolveProgressionProfiles(
                defaultProgression,
                _progressionFields[1].GetValue(balancing),
                _progressionFields[2].GetValue(balancing));
            var profileById = new Dictionary<string, LoadedAiProfile>(StringComparer.Ordinal);
            for (int i = 0; i < candidateProfiles.Count; i++)
            {
                profileById.Add(candidateProfiles[i].Id, candidateProfiles[i]);
            }

            var loadedStages = new List<LoadedStage>(_stages.Count + candidateStages.Count);
            loadedStages.AddRange(_stages);
            loadedStages.AddRange(candidateStages);
            int validatedProgressionCount = 0;
            for (int progressionFieldIndex = 0; progressionFieldIndex < _progressionFields.Length;
                progressionFieldIndex++)
            {
                FieldInfo progressionField = _progressionFields[progressionFieldIndex];
                object progression = progressionProfiles[progressionFieldIndex];

                if (progression == null)
                {
                    continue;
                }

                IList nativeStages = _progressionStagesField.GetValue(progression) as IList;
                IList nativeAis = _progressionAisField.GetValue(progression) as IList;
                IList nativeLandscapes = _progressionLandscapesField.GetValue(progression) as IList;
                if (nativeStages == null || nativeAis == null || nativeLandscapes == null)
                {
                    throw new InvalidDataException("La progresión '" + progressionField.Name +
                        "' contiene una lista nula de stages, AI o LandscapeGenerator.");
                }

                List<StageGenerationAiCandidate> aiCandidates =
                    BuildProgressionAiCandidates(nativeAis, loadedStages, profileById, packageName);
                List<StageGenerationLandscapeCandidate> landscapeCandidates =
                    BuildLandscapeCandidates(nativeLandscapes, "progresión " + progressionField.Name);
                List<StageGenerationStageCandidate> stageCandidates =
                    BuildProgressionStageCandidates(nativeStages, loadedStages, packageName, progressionField.Name);
                var projectedStages = new ArrayList(nativeStages);
                foreach (LoadedStage loadedStage in loadedStages)
                {
                    bool exists = false;
                    foreach (object existingStage in projectedStages)
                        if (System.Object.ReferenceEquals(existingStage, loadedStage.RuntimeStage)) { exists = true; break; }
                    if (!exists) projectedStages.Add(loadedStage.RuntimeStage);
                }
                // Reject the pack before publishing any objects if an appended slot
                // could reach the game's positional fallback lookup without a mapping.
                StageFallbackSnapshot.Build(_fallbackLandscapeGeneratorsField.GetValue(balancing) as IList,
                    projectedStages, delegate(object runtimeStage)
                    {
                        foreach (LoadedStage loadedStage in loadedStages)
                            if (System.Object.ReferenceEquals(loadedStage.RuntimeStage, runtimeStage))
                                return loadedStage.FallbackLandscape;
                        return null;
                    });
                bool heatIsActive = progressionFieldIndex == 1;
                string diagnostic;
                if (!StageGenerationCompatibility.TryValidateStages(
                    progressionField.Name,
                    isDemo,
                    heatIsActive,
                    maxAscensionLevel,
                    stageCandidates,
                    aiCandidates,
                    landscapeCandidates,
                    out diagnostic))
                {
                    throw new InvalidDataException("Incompatibilidad de generación en el paquete '" +
                        packageName + "': " + diagnostic);
                }

                validatedProgressionCount++;
            }

            if (validatedProgressionCount == 0)
            {
                throw new InvalidDataException("No hay una progresión activa para validar las etapas del paquete '" +
                    packageName + "'.");
            }
        }

        private Dictionary<string, Vector2Int> BuildScopedAiStageRanges(
            List<LoadedStage> candidateStages,
            ContentAiProfileDefinition[] profileDefinitions,
            string packageName)
        {
            var targetStageIdsByProfile = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (profileDefinitions == null || candidateStages == null)
            {
                return new Dictionary<string, Vector2Int>(StringComparer.Ordinal);
            }

            for (int profileIndex = 0; profileIndex < profileDefinitions.Length; profileIndex++)
            {
                ContentAiProfileDefinition profile = profileDefinitions[profileIndex];
                if (profile == null || !profile.scopeToReferencedStages)
                {
                    continue;
                }

                var targetStageIds = new HashSet<string>(StringComparer.Ordinal);
                for (int stageIndex = 0; stageIndex < candidateStages.Count; stageIndex++)
                {
                    ContentStageDefinition stage = candidateStages[stageIndex].Definition;
                    string[] profileIds = stage.aiProfileIds ?? new string[0];
                    for (int referencedIndex = 0; referencedIndex < profileIds.Length; referencedIndex++)
                    {
                        if (String.Equals(profileIds[referencedIndex], profile.id, StringComparison.Ordinal))
                        {
                            targetStageIds.Add(stage.id);
                            break;
                        }
                    }
                }

                if (targetStageIds.Count == 0)
                {
                    throw new InvalidDataException("El perfil AI de etapa " + profile.id + " en '" + packageName +
                        "' debe estar asociado a una etapa declarada por el mismo paquete.");
                }

                targetStageIdsByProfile.Add(profile.id, targetStageIds);
            }

            var result = new Dictionary<string, Vector2Int>(StringComparer.Ordinal);
            if (targetStageIdsByProfile.Count == 0)
            {
                return result;
            }

            object balancing = _gameBalancingAssetField.GetValue(null);
            if (balancing == null)
            {
                throw new InvalidDataException("No se pudo calcular el rango de AI por etapa en '" + packageName +
                    "': GameBalancingStore aún no tiene su GameBalancingScriptableObject.");
            }

            var allStages = new List<LoadedStage>(_stages.Count + candidateStages.Count);
            allStages.AddRange(_stages);
            allStages.AddRange(candidateStages);
            var stageReferences = new List<object>(allStages.Count);
            for (int i = 0; i < allStages.Count; i++)
            {
                stageReferences.Add(allStages[i].RuntimeStage);
            }

            object defaultProgression = _progressionFields[0].GetValue(balancing);
            object[] progressionProfiles = StageGenerationCompatibility.ResolveProgressionProfiles(
                defaultProgression,
                _progressionFields[1].GetValue(balancing),
                _progressionFields[2].GetValue(balancing));
            int validatedProgressionCount = 0;
            for (int progressionIndex = 0; progressionIndex < progressionProfiles.Length; progressionIndex++)
            {
                object progression = progressionProfiles[progressionIndex];
                if (progression == null)
                {
                    continue;
                }

                IList progressionStages = _progressionStagesField.GetValue(progression) as IList;
                int[] stageIndices;
                string indexDiagnostic;
                if (!StageGenerationCompatibility.TryBuildStageIndices(
                    progressionStages,
                    stageReferences,
                    _progressionFields[progressionIndex].Name,
                    out stageIndices,
                    out indexDiagnostic))
                {
                    throw new InvalidDataException("No se pudo calcular el alcance de AI por etapa en '" + packageName +
                        "': " + indexDiagnostic);
                }

                var indexByStageId = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int stageIndex = 0; stageIndex < allStages.Count; stageIndex++)
                {
                    indexByStageId.Add(allStages[stageIndex].Definition.id, stageIndices[stageIndex]);
                }

                foreach (KeyValuePair<string, HashSet<string>> scopedProfile in targetStageIdsByProfile)
                {
                    var eligibleIndices = new List<int>(scopedProfile.Value.Count);
                    foreach (string stageId in scopedProfile.Value)
                    {
                        int stageIndex;
                        if (!indexByStageId.TryGetValue(stageId, out stageIndex))
                        {
                            throw new InvalidDataException("La etapa '" + stageId + "' asociada al perfil AI " +
                                scopedProfile.Key + " no aparece en la progresión '" +
                                _progressionFields[progressionIndex].Name + "'.");
                        }
                        eligibleIndices.Add(stageIndex);
                    }

                    int minimumStage;
                    int maximumStage;
                    string rangeDiagnostic;
                    if (!StageGenerationCompatibility.TryBuildContiguousStageRange(
                        eligibleIndices, out minimumStage, out maximumStage, out rangeDiagnostic))
                    {
                        throw new InvalidDataException("Las etapas asociadas al perfil AI de etapa " +
                            scopedProfile.Key + " no forman un rango válido: " + rangeDiagnostic);
                    }

                    var range = new Vector2Int(minimumStage, maximumStage);
                    Vector2Int existingRange;
                    if (result.TryGetValue(scopedProfile.Key, out existingRange) &&
                        (existingRange.x != range.x || existingRange.y != range.y))
                    {
                        throw new InvalidDataException("Las progresiones default, heat y first-run asignan índices " +
                            "distintos a las etapas del perfil AI " + scopedProfile.Key + ".");
                    }

                    result[scopedProfile.Key] = range;
                }

                validatedProgressionCount++;
            }

            if (validatedProgressionCount == 0)
            {
                throw new InvalidDataException("No hay una progresión activa para calcular los rangos AI en '" +
                    packageName + "'.");
            }

            return result;
        }

        private List<StageGenerationStageCandidate> BuildProgressionStageCandidates(
            IList progressionStages,
            IList<LoadedStage> loadedStages,
            string packageName,
            string progressionName)
        {
            var loadedStageReferences = new List<object>(loadedStages.Count);
            for (int i = 0; i < loadedStages.Count; i++)
            {
                loadedStageReferences.Add(loadedStages[i].RuntimeStage);
            }

            int[] expectedStageIndices;
            string indexDiagnostic;
            if (!StageGenerationCompatibility.TryBuildStageIndices(
                progressionStages, loadedStageReferences, progressionName,
                out expectedStageIndices, out indexDiagnostic))
            {
                throw new InvalidDataException(indexDiagnostic + " Package '" + packageName + "'.");
            }

            var result = new List<StageGenerationStageCandidate>(loadedStages.Count);
            for (int i = 0; i < loadedStages.Count; i++)
            {
                LoadedStage stage = loadedStages[i];
                result.Add(new StageGenerationStageCandidate
                {
                    Id = stage.Definition.id,
                    StageIndex = expectedStageIndices[i],
                    MapSizeMin = stage.Definition.mapSizeMin,
                    MapSizeMax = stage.Definition.mapSizeMax,
                    IsPartOfDemo = stage.Definition.isPartOfDemo,
                    Worlds = BuildWorldCandidates(stage.Worlds, stage.Definition.id),
                    Landscape = stage.Landscape == null
                        ? null
                        : BuildLandscapeCandidate(stage.Landscape.Asset)
                });
            }

            return result;
        }

        private List<StageGenerationAiCandidate> BuildProgressionAiCandidates(
            IList nativeAis,
            IList<LoadedStage> stages,
            Dictionary<string, LoadedAiProfile> candidateProfiles,
            string packageName)
        {
            var behaviours = new List<object>();
            var seenBehaviours = new HashSet<object>(ReferenceComparer.Instance);
            for (int i = 0; i < nativeAis.Count; i++)
            {
                AddAiBehaviourCandidate(nativeAis[i], behaviours, seenBehaviours,
                    "progresión nativa", packageName);
            }

            for (int stageIndex = 0; stageIndex < stages.Count; stageIndex++)
            {
                string[] profileIds = stages[stageIndex].Definition.aiProfileIds ?? new string[0];
                for (int profileIndex = 0; profileIndex < profileIds.Length; profileIndex++)
                {
                    LoadedAiProfile profile;
                    if (!candidateProfiles.TryGetValue(profileIds[profileIndex], out profile) &&
                        !_aiProfiles.TryGetValue(profileIds[profileIndex], out profile))
                    {
                        throw new InvalidDataException("El perfil AI '" + profileIds[profileIndex] +
                            "' no está cargado para la etapa '" + stages[stageIndex].Definition.id +
                            "' del paquete '" + packageName + "'.");
                    }

                    for (int behaviourIndex = 0; behaviourIndex < profile.Behaviours.Count; behaviourIndex++)
                    {
                        AddAiBehaviourCandidate(profile.Behaviours[behaviourIndex], behaviours,
                            seenBehaviours, profile.Id, packageName);
                    }
                }
            }

            var candidates = new List<StageGenerationAiCandidate>(behaviours.Count);
            for (int i = 0; i < behaviours.Count; i++)
            {
                object behaviour = behaviours[i];
                Vector2Int stageRange = (Vector2Int)_aiStageRangeField.GetValue(behaviour);
                Vector2Int ascensionRange = (Vector2Int)_aiAscensionRangeField.GetValue(behaviour);
                IList bases = _aiBasesField.GetValue(behaviour) as IList;
                var baseCandidates = new List<StageGenerationAiBaseCandidate>();
                if (bases != null)
                {
                    for (int baseIndex = 0; baseIndex < bases.Count; baseIndex++)
                    {
                        object aiBase = bases[baseIndex];
                        if (aiBase == null || aiBase.GetType() != _aiBaseType)
                        {
                            throw new InvalidDataException("AI '" + GetObjectName(behaviour) +
                                "' contiene una AiBase nula o de tipo inesperado.");
                        }

                        baseCandidates.Add(new StageGenerationAiBaseCandidate
                        {
                            StartingInStage = (int)_aiBaseStartingStageField.GetValue(aiBase),
                            StartingInAscensionLevel = (int)_aiBaseStartingAscensionField.GetValue(aiBase)
                        });
                    }
                }

                IList restrictedMaps = _aiRestrictedMapsField.GetValue(behaviour) as IList;
                if (restrictedMaps == null)
                {
                    throw new InvalidDataException("AI '" + GetObjectName(behaviour) +
                        "' contiene una lista restrictedToMaps nula.");
                }

                candidates.Add(new StageGenerationAiCandidate
                {
                    Id = GetObjectName(behaviour),
                    IsDeactivated = (bool)_aiDeactivatedField.GetValue(behaviour),
                    IsAllowedForDemo = (bool)_aiAllowedForDemoField.GetValue(behaviour),
                    StageMin = stageRange.x,
                    StageMax = stageRange.y,
                    AscensionMin = ascensionRange.x,
                    AscensionMax = ascensionRange.y,
                    Bases = baseCandidates,
                    RestrictedMaps = BuildRestrictedMapCandidates(restrictedMaps,
                        "AI " + GetObjectName(behaviour))
                });
            }

            return candidates;
        }

        private void AddAiBehaviourCandidate(
            object behaviour,
            List<object> behaviours,
            HashSet<object> seenBehaviours,
            string source,
            string packageName)
        {
            if (behaviour == null || behaviour.GetType() != _defaultAiBehaviourType)
            {
                throw new InvalidDataException("El paquete '" + packageName + "' encontró un DefaultAiBehaviour " +
                    "nulo o de tipo inesperado en " + source + ".");
            }

            if (seenBehaviours.Add(behaviour))
            {
                behaviours.Add(behaviour);
            }
        }

        private List<StageGenerationLandscapeCandidate> BuildLandscapeCandidates(
            IList landscapes,
            string source)
        {
            var candidates = new List<StageGenerationLandscapeCandidate>(landscapes.Count);
            for (int i = 0; i < landscapes.Count; i++)
            {
                object landscape = landscapes[i];
                if (landscape == null || landscape.GetType() != _landscapeGeneratorType)
                {
                    throw new InvalidDataException(source + " contiene un LandscapeGenerator nulo o de tipo inesperado.");
                }

                candidates.Add(BuildLandscapeCandidate(landscape));
            }

            return candidates;
        }

        private StageGenerationLandscapeCandidate BuildLandscapeCandidate(object landscape)
        {
            string landscapeName = GetObjectName(landscape);
            return new StageGenerationLandscapeCandidate
            {
                Id = landscapeName,
                MapSize = (int)_landscapeMapSizeField.GetValue(landscape),
                IsDeactivated = (bool)_landscapeDeactivatedField.GetValue(landscape),
                IsAllowedForDemo = (bool)_landscapeAllowedForDemoField.GetValue(landscape),
                RestrictedWorlds = BuildRestrictedWorldCandidates(
                    _landscapeRestrictedWorldsField.GetValue(landscape) as IList,
                    "LandscapeGenerator " + landscapeName)
            };
        }

        private List<StageGenerationLandscapeCandidate> BuildRestrictedMapCandidates(
            IList landscapes,
            string source)
        {
            if (landscapes == null)
            {
                throw new InvalidDataException(source + " contiene una lista restrictedToMaps nula.");
            }

            var candidates = new List<StageGenerationLandscapeCandidate>(landscapes.Count);
            for (int i = 0; i < landscapes.Count; i++)
            {
                object landscape = landscapes[i];
                if (landscape == null || landscape.GetType() != _landscapeGeneratorType)
                {
                    throw new InvalidDataException(source +
                        " contiene un LandscapeGenerator nulo o de tipo inesperado en restrictedToMaps.");
                }

                candidates.Add(BuildLandscapeCandidate(landscape));
            }

            return candidates;
        }

        private List<StageGenerationWorldCandidate> BuildRestrictedWorldCandidates(
            IList worlds,
            string source)
        {
            if (worlds == null)
            {
                return null;
            }

            var candidates = new List<StageGenerationWorldCandidate>(worlds.Count);
            for (int i = 0; i < worlds.Count; i++)
            {
                object world = worlds[i];
                if (world == null)
                {
                    candidates.Add(null);
                    continue;
                }

                if (world.GetType() != _initMapType)
                {
                    throw new InvalidDataException(source +
                        " contiene un World de tipo inesperado en restrictedToWorlds.");
                }

                candidates.Add(new StageGenerationWorldCandidate
                {
                    Id = GetObjectName(world),
                    IsDeactivated = (bool)_worldDeactivatedField.GetValue(world),
                    IsAllowedForDemo = (bool)_worldAllowedForDemoField.GetValue(world)
                });
            }

            return candidates;
        }

        private List<StageGenerationWorldCandidate> BuildWorldCandidates(
            IList worlds,
            string stageId)
        {
            if (worlds == null)
            {
                throw new InvalidDataException("La etapa '" + stageId + "' no tiene una lista de World validada.");
            }

            var candidates = new List<StageGenerationWorldCandidate>(worlds.Count);
            for (int i = 0; i < worlds.Count; i++)
            {
                object world = worlds[i];
                if (world == null || world.GetType() != _initMapType)
                {
                    throw new InvalidDataException("La etapa '" + stageId +
                        "' contiene un World nulo o de tipo inesperado.");
                }

                IList restrictedMaps = _worldRestrictedMapsField.GetValue(world) as IList;
                if (restrictedMaps == null)
                {
                    throw new InvalidDataException("World '" + GetObjectName(world) +
                        "' contiene una lista restrictedToMaps nula.");
                }

                candidates.Add(new StageGenerationWorldCandidate
                {
                    Id = GetObjectName(world),
                    IsDeactivated = (bool)_worldDeactivatedField.GetValue(world),
                    IsAllowedForDemo = (bool)_worldAllowedForDemoField.GetValue(world),
                    RestrictedMaps = BuildRestrictedMapCandidates(restrictedMaps,
                        "World " + GetObjectName(world))
                });
            }

            return candidates;
        }

        private static string GetObjectName(object value)
        {
            UnityEngine.Object unityObject = value as UnityEngine.Object;
            return unityObject == null || String.IsNullOrWhiteSpace(unityObject.name)
                ? "<sin nombre>"
                : unityObject.name;
        }

        private static bool IsNullOrDestroyedUnityObject(object value)
        {
            if (System.Object.ReferenceEquals(value, null))
            {
                return true;
            }

            UnityEngine.Object unityObject = value as UnityEngine.Object;
            return !System.Object.ReferenceEquals(unityObject, null) && unityObject == null;
        }

        private object CreateStage(ContentStageDefinition definition, UnityEngine.Object parameters, string packageName)
        {
            object stage = Activator.CreateInstance(_stageType);
            if (stage == null)
            {
                throw new InvalidDataException("No se pudo crear el contenedor Stage para " + definition.id + ".");
            }

            _stageParametersField.SetValue(stage, parameters);
            _stageMapSizeField.SetValue(stage, new Vector2Int(definition.mapSizeMin, definition.mapSizeMax));
            _stageDemoField.SetValue(stage, definition.isPartOfDemo);
            return stage;
        }

        private void ApplyStageParameterOverrides(
            UnityEngine.Object parameters,
            ContentStageParameterOverride[] overrides,
            string packageName,
            string stageId)
        {
            if (overrides == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool hasStartCountRangeOverride = false;
            for (int i = 0; i < overrides.Length; i++)
            {
                ContentStageParameterOverride stageOverride = overrides[i];
                if (stageOverride == null || String.IsNullOrWhiteSpace(stageOverride.name) ||
                    !seen.Add(stageOverride.name))
                {
                    throw new InvalidDataException("Etapa " + stageId + " en '" + packageName +
                        "': hay un override nulo, vacío o duplicado.");
                }

                FieldInfo field = FindField(_stageParametersType, stageOverride.name, InstanceFields);
                object value;
                if (field.FieldType == typeof(Vector2Int) &&
                    String.Equals(stageOverride.name, "startCountRange", StringComparison.Ordinal))
                {
                    int minimum;
                    int maximum;
                    if (!StageParameterOverrideParser.TryParseStartCountRange(
                        stageOverride.value, out minimum, out maximum))
                    {
                        throw new InvalidDataException("Etapa " + stageId + " en '" + packageName +
                            "': rango startCountRange inválido.");
                    }

                    value = new Vector2Int(minimum, maximum);
                    hasStartCountRangeOverride = true;
                }
                else if (field.FieldType == typeof(int))
                {
                    int parsed;
                    if (!Int32.TryParse(stageOverride.value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out parsed) ||
                        (stageOverride.name == "levelCount" && (parsed < 1 || parsed > 32)) ||
                        (stageOverride.name == "maxWidth" && (parsed < 1 || parsed > 64)) ||
                        (stageOverride.name != "levelCount" && stageOverride.name != "maxWidth" &&
                            (parsed < 0 || parsed > 1000000)))
                    {
                        throw new InvalidDataException("Etapa " + stageId + " en '" + packageName +
                            "': valor entero inválido para " + stageOverride.name + ".");
                    }

                    value = parsed;
                }
                else if (field.FieldType == typeof(float))
                {
                    float parsed;
                    if (!Single.TryParse(stageOverride.value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out parsed) || Single.IsNaN(parsed) ||
                        Single.IsInfinity(parsed) || parsed < 0f ||
                        (stageOverride.name == "twoWayBranchingProbability" && parsed > 1f) ||
                        (stageOverride.name != "twoWayBranchingProbability" && parsed > 1000f))
                    {
                        throw new InvalidDataException("Etapa " + stageId + " en '" + packageName +
                            "': valor decimal inválido para " + stageOverride.name + ".");
                    }

                    value = parsed;
                }
                else if (field.FieldType == typeof(bool))
                {
                    bool parsed;
                    if (!Boolean.TryParse(stageOverride.value, out parsed))
                    {
                        throw new InvalidDataException("Etapa " + stageId + " en '" + packageName +
                            "': valor booleano inválido para " + stageOverride.name + ".");
                    }

                    value = parsed;
                }
                else
                {
                    throw new InvalidDataException("Etapa " + stageId + " en '" + packageName +
                        "': el campo " + stageOverride.name + " no es un parámetro compatible.");
                }

                field.SetValue(parameters, value);
            }

            if (hasStartCountRangeOverride)
            {
                FieldInfo rangeField = FindField(_stageParametersType, "startCountRange", InstanceFields);
                FieldInfo widthField = FindField(_stageParametersType, "maxWidth", InstanceFields);
                Vector2Int range = (Vector2Int)rangeField.GetValue(parameters);
                int maximumWidth = (int)widthField.GetValue(parameters);
                if (!StageParameterOverrideParser.IsStartCountRangeWithinWidth(
                    range.x, range.y, maximumWidth))
                {
                    throw new InvalidDataException("Etapa " + stageId + " en '" + packageName +
                        "': startCountRange debe caber en maxWidth.");
                }
            }
        }

        private static UnityEngine.Object LoadRequiredAsset(
            AssetBundle bundle,
            string assetName,
            Type expectedType,
            string expectedDescription,
            string packageName)
        {
            UnityEngine.Object asset;
            try
            {
                // Request the expected type here instead of loading the bundle's main object
                // first. Texture assets may expose a Sprite sub-object under the same file name.
                asset = bundle.LoadAsset(assetName, expectedType);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("Paquete '" + packageName + "': no se pudo cargar el asset '" +
                    assetName + "': " + exception.Message);
            }

            if (asset == null || !expectedType.IsInstanceOfType(asset))
            {
                throw new InvalidDataException("Paquete '" + packageName + "': el asset '" + assetName +
                    "' falta o no es " + expectedDescription + ".");
            }

            return asset;
        }

        private static AssetBundle GetBundle(Dictionary<string, AssetBundle> bundles, string id, string packageName)
        {
            AssetBundle bundle;
            if (!bundles.TryGetValue(id ?? string.Empty, out bundle) || bundle == null)
            {
                throw new InvalidDataException("Paquete '" + packageName + "': bundle declarado no disponible: " + id);
            }

            return bundle;
        }

        private bool TryResolveBundleFile(string packageRoot, string relativePath, out string fullPath, out string failure)
        {
            fullPath = null;
            failure = null;
            if (String.IsNullOrWhiteSpace(relativePath) || relativePath.IndexOf('\\') >= 0 ||
                relativePath[0] == '/' || Path.IsPathRooted(relativePath))
            {
                failure = "Ruta de bundle inválida; solo se aceptan rutas relativas con '/'.";
                return false;
            }

            string root = Path.GetFullPath(packageRoot);
            string candidate = Path.GetFullPath(Path.Combine(root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                failure = "La ruta del bundle sale de la carpeta del paquete.";
                return false;
            }

            if (!IsRegularPackageFile(root, candidate, out failure))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }

        private bool TryOpenContainedFile(
            string packageRoot,
            string path,
            out FileStream stream,
            out string failure)
        {
            stream = null;
            failure = null;
            if (String.IsNullOrWhiteSpace(_resolvedPacksRoot))
            {
                failure = "La raíz física de paquetes no está fijada.";
                return false;
            }

            if (!IsRegularPackageFile(packageRoot, path, out failure))
            {
                return false;
            }

            try
            {
                using (SafeFileHandle packageHandle = OpenDirectoryHandle(packageRoot, out failure))
                {
                    if (packageHandle == null)
                    {
                        return false;
                    }

                    string finalPackagePath = GetFinalPath(packageHandle);
                    if (!IsPathWithin(_resolvedPacksRoot, finalPackagePath))
                    {
                        failure = "La carpeta del paquete resuelve fuera de la raíz de paquetes verificada.";
                        return false;
                    }

                    SafeFileHandle fileHandle = OpenRegularFileHandle(path, out failure);
                    if (fileHandle == null)
                    {
                        return false;
                    }

                    try
                    {
                        string finalFilePath = GetFinalPath(fileHandle);
                        if (!IsPathWithin(finalPackagePath, finalFilePath))
                        {
                            failure = "El archivo abierto resuelve fuera de la carpeta física del paquete.";
                            return false;
                        }

                        stream = new FileStream(fileHandle, FileAccess.Read);
                        fileHandle = null;
                        return true;
                    }
                    finally
                    {
                        if (fileHandle != null)
                        {
                            fileHandle.Dispose();
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                failure = "No se pudo abrir de forma segura el archivo del paquete: " +
                    exception.GetType().Name + ": " + exception.Message;
                if (stream != null)
                {
                    stream.Dispose();
                    stream = null;
                }

                return false;
            }
        }

        private static SafeFileHandle OpenDirectoryHandle(string path, out string failure)
        {
            failure = null;
            SafeFileHandle handle = NativeCreateFile(path, 0,
                ShareRead | ShareWrite | ShareDelete, IntPtr.Zero, OpenExisting,
                OpenReparsePoint | BackupSemantics, IntPtr.Zero);
            if (handle == null || handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                if (handle != null) handle.Dispose();
                failure = "No se pudo abrir la carpeta (Win32 " + error + ").";
                return null;
            }

            FileAttributeTagInfo information;
            if (!NativeGetFileInformationByHandleEx(handle, FileAttributeTagInfoClass,
                out information, (uint)Marshal.SizeOf(typeof(FileAttributeTagInfo))))
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                failure = "No se pudieron verificar los atributos de la carpeta (Win32 " + error + ").";
                return null;
            }

            if ((information.FileAttributes & DirectoryAttribute) == 0 ||
                (information.FileAttributes & ReparsePointAttribute) != 0)
            {
                handle.Dispose();
                failure = "La ruta no es una carpeta normal o es un reparse point.";
                return null;
            }

            return handle;
        }

        private static SafeFileHandle OpenRegularFileHandle(string path, out string failure)
        {
            failure = null;
            SafeFileHandle handle = NativeCreateFile(path, GenericRead, ShareRead,
                IntPtr.Zero, OpenExisting, OpenReparsePoint | SequentialScan, IntPtr.Zero);
            if (handle == null || handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                if (handle != null) handle.Dispose();
                failure = "No se pudo abrir el archivo (Win32 " + error + ").";
                return null;
            }

            FileAttributeTagInfo information;
            if (!NativeGetFileInformationByHandleEx(handle, FileAttributeTagInfoClass,
                out information, (uint)Marshal.SizeOf(typeof(FileAttributeTagInfo))))
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                failure = "No se pudieron verificar los atributos del archivo (Win32 " + error + ").";
                return null;
            }

            if ((information.FileAttributes & (DirectoryAttribute | ReparsePointAttribute)) != 0)
            {
                handle.Dispose();
                failure = "El archivo es una carpeta o un reparse point.";
                return null;
            }

            return handle;
        }

        private static string GetFinalPath(SafeFileHandle handle)
        {
            var buffer = new StringBuilder(512);
            uint required = NativeGetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            if (required == 0)
            {
                throw new IOException("GetFinalPathNameByHandle falló (Win32 " +
                    Marshal.GetLastWin32Error() + ").");
            }

            if (required >= buffer.Capacity)
            {
                buffer = new StringBuilder(checked((int)required + 1));
                required = NativeGetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                if (required == 0 || required >= buffer.Capacity)
                {
                    throw new IOException("No se pudo obtener la ruta física completa del handle.");
                }
            }

            return buffer.ToString();
        }

        private static bool IsPathWithin(string root, string candidate)
        {
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasNoReparsePointsInPath(string path, out string failure)
        {
            failure = null;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string current = Path.GetPathRoot(fullPath);
                if (String.IsNullOrEmpty(current))
                {
                    failure = "No se pudo identificar la raíz del volumen.";
                    return false;
                }

                if (Directory.Exists(current) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    failure = "La raíz del volumen es un reparse point.";
                    return false;
                }

                string remainder = fullPath.Substring(current.Length);
                string[] segments = remainder.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < segments.Length; i++)
                {
                    current = Path.Combine(current, segments[i]);
                    if (!Directory.Exists(current) && !File.Exists(current))
                    {
                        break;
                    }

                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        failure = "La ruta contiene un enlace, junction u otro reparse point.";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                failure = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static bool IsRegularPackageFile(string packageRoot, string candidate, out string failure)
        {
            failure = null;
            try
            {
                string root = Path.GetFullPath(packageRoot);
                string path = Path.GetFullPath(candidate);
                string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    failure = "La ruta no permanece dentro de la carpeta del paquete.";
                    return false;
                }

                if (!HasNoReparsePointsInPath(root, out failure))
                {
                    return false;
                }

                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                {
                    failure = "La carpeta del paquete no puede ser un enlace simbólico o junction.";
                    return false;
                }

                string remaining = path.Substring(prefix.Length);
                string[] segments = remaining.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                string current = root;
                for (int i = 0; i < segments.Length; i++)
                {
                    current = Path.Combine(current, segments[i]);
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        failure = "La ruta del paquete atraviesa un enlace o reparse point.";
                        return false;
                    }
                }

                if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.Directory) != 0)
                {
                    failure = "El archivo de paquete no existe o no es un archivo regular.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                failure = "No se pudo verificar la ruta del paquete: " + exception.Message;
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileAttributeTagInfo
        {
            public uint FileAttributes;
            public uint ReparseTag;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode,
            SetLastError = true, ExactSpelling = true)]
        private static extern SafeFileHandle NativeCreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true,
            ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeGetFileInformationByHandleEx(
            SafeFileHandle file,
            int fileInformationClass,
            out FileAttributeTagInfo fileInformation,
            uint bufferSize);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode,
            SetLastError = true, ExactSpelling = true)]
        private static extern uint NativeGetFinalPathNameByHandle(
            SafeFileHandle file,
            StringBuilder path,
            uint pathLength,
            uint flags);

        private static string BundleRegistryKey(string packId, string bundleId)
        {
            return (packId ?? String.Empty) + "\u001f" + (bundleId ?? String.Empty);
        }

        private static string ResourceKey(string entityId, string kind)
        {
            return "RogueCommandExpansionLoader/" + entityId.Replace(':', '/') + "/" + kind;
        }

        private void LogDiagnostics(string packageName, IReadOnlyList<ContentDiagnostic> diagnostics)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                Log("Paquete '" + packageName + "' desactivado: " + diagnostics[i]);
            }
        }

        private int ReadIntField(Type ownerType, object instance, string name)
        {
            FieldInfo field = FindField(ownerType, name, InstanceFields);
            if (field.FieldType != typeof(int))
            {
                throw new InvalidDataException("Campo " + ownerType.Name + "." + name + " no es int.");
            }

            return (int)field.GetValue(instance);
        }

        private FieldInfo RequireField(Type type, string name, Type fieldType)
        {
            FieldInfo field = FindField(type, name, InstanceFields);
            if (field.FieldType != fieldType || field.IsInitOnly)
            {
                throw new InvalidOperationException(type.Name + "." + name +
                    " no coincide con el tipo mutable esperado " + fieldType.FullName + ".");
            }

            return field;
        }

        private static bool EnumHasName(Type enumType, string name)
        {
            return enumType != null && enumType.IsEnum &&
                Array.IndexOf(Enum.GetNames(enumType), name) >= 0;
        }

        private Type FindUniqueType(string name)
        {
            Type[] types;
            try
            {
                types = _gameAssembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types;
            }

            Type match = null;
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null || !String.Equals(type.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException("El tipo de juego '" + name + "' es ambiguo.");
                }

                match = type;
            }

            if (match == null)
            {
                throw new InvalidOperationException("No se encontró el tipo de juego '" + name + "'.");
            }

            return match;
        }

        private static FieldInfo FindField(Type type, string name, BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null)
            {
                throw new InvalidOperationException("No se encontró el campo " + type.FullName + "." + name + ".");
            }

            return field;
        }

        private static bool IsExactList(Type collectionType, Type itemType)
        {
            return collectionType != null && collectionType.IsGenericType &&
                collectionType.GetGenericTypeDefinition() == typeof(List<>) &&
                collectionType.GetGenericArguments()[0] == itemType;
        }

        private void Log(string message)
        {
            if (message != null)
            {
                lock (_logGate)
                {
                    _recentLogs.Enqueue(DateTime.Now.ToString("HH:mm:ss") + " " + message);
                    while (_recentLogs.Count > 100)
                    {
                        _recentLogs.Dequeue();
                    }
                }
            }

            if (_log == null)
            {
                return;
            }

            try { _log("[ExpansionLoader] " + message); }
            catch { }
        }

        private sealed class RegistrySnapshot : IContentRegistrySnapshot
        {
            private readonly ContentPackRuntimeLoader _owner;
            private readonly HashSet<string> _declaredUnitIds;
            private readonly HashSet<string> _declaredProfileIds;

            public RegistrySnapshot(
                ContentPackRuntimeLoader owner,
                HashSet<string> declaredUnitIds,
                HashSet<string> declaredProfileIds)
            {
                _owner = owner;
                _declaredUnitIds = declaredUnitIds;
                _declaredProfileIds = declaredProfileIds;
            }

            public bool ContainsId(string id)
            {
                return _owner._nativeUnitIds.Contains(id) || _owner._knownIds.Contains(id);
            }

            public bool ContainsUnit(string id)
            {
                if ((_owner._nativeUnitIds.Contains(id) && !_owner._nativeFactoryIds.Contains(id)) ||
                    _declaredUnitIds.Contains(id))
                {
                    return true;
                }

                LoadedUnit unit;
                return _owner._units.TryGetValue(id, out unit) && !unit.IsProductionBuilding;
            }

            public bool ContainsAiProfile(string id)
            {
                return _owner._aiProfiles.ContainsKey(id) || _declaredProfileIds.Contains(id);
            }
        }

        private sealed class PackCandidate
        {
            public PackCandidate(string root, string name, ContentPackDefinition definition)
            {
                Root = root;
                Name = name;
                Definition = definition;
                Dependencies = new HashSet<PackCandidate>();
            }

            public string Root { get; private set; }
            public string Name { get; private set; }
            public ContentPackDefinition Definition { get; private set; }
            public ContentPackDefinition ValidatedDefinition { get; set; }
            public HashSet<PackCandidate> Dependencies { get; private set; }
            public bool Disabled { get; set; }
            public bool LoadAttempted { get; set; }
            public bool LoadSucceeded { get; set; }
            public string StatusReason { get; set; }
            public long LoadedBytes { get; set; }
        }

        public IEnumerable<PrespawnQuota> GetPrespawnQuotas()
        {
            lock (_gate)
            {
                var quotas = new List<PrespawnQuota>(_units.Count);
                foreach (LoadedUnit unit in _units.Values)
                {
                    quotas.Add(new PrespawnQuota(unit.Id, unit.PrespawnCount, unit.IsProductionBuilding || unit.IsStandaloneBuilding, unit.ProductionFactoryId));
                }
                return quotas;
            }
        }

        private void ApplyEntityBehavioralParameters(
            GameObject prefabClone,
            float? rotationDegreesPerSecond,
            float? collisionRadius,
            float? attackWarmUpTime,
            bool? canAttackWhileMoving,
            bool? shootAutomaticallyOnEnemiesWithinRange,
            bool? defendPositionAggressivelyWhenIdle,
            int? rangeToChooseNewTargetAutomatically,
            float? minAttackRange = null,
            string entityId = null)
        {
            if (prefabClone == null) return;
            Component controller = prefabClone.GetComponent(_entityControllerType);
            if (controller == null) return;

            if (minAttackRange.HasValue && minAttackRange.Value > 0f)
            {
                string id = entityId;
                if (string.IsNullOrEmpty(id) && _entityControllerIdField != null)
                {
                    id = _entityControllerIdField.GetValue(controller) as string;
                }
                if (!string.IsNullOrEmpty(id))
                {
                    RegisterMinAttackRange(id, minAttackRange.Value);
                }
            }

            if (rotationDegreesPerSecond.HasValue && _rotationDegreesPerSecondField != null)
            {
                _rotationDegreesPerSecondField.SetValue(controller, rotationDegreesPerSecond.Value);
            }
            if (collisionRadius.HasValue && _collisionRadiusField != null)
            {
                _collisionRadiusField.SetValue(controller, collisionRadius.Value);
            }
            if (attackWarmUpTime.HasValue && _attackWarmUpTimeField != null)
            {
                _attackWarmUpTimeField.SetValue(controller, attackWarmUpTime.Value);
            }
            if (canAttackWhileMoving.HasValue && _canAttackWhileMovingField != null)
            {
                _canAttackWhileMovingField.SetValue(controller, canAttackWhileMoving.Value);
            }
            if (shootAutomaticallyOnEnemiesWithinRange.HasValue && _shootAutomaticallyField != null)
            {
                _shootAutomaticallyField.SetValue(controller, shootAutomaticallyOnEnemiesWithinRange.Value);
            }
            if (defendPositionAggressivelyWhenIdle.HasValue && _defendPositionAggressivelyField != null)
            {
                _defendPositionAggressivelyField.SetValue(controller, defendPositionAggressivelyWhenIdle.Value);
            }
            if (rangeToChooseNewTargetAutomatically.HasValue && _rangeToChooseNewTargetField != null)
            {
                _rangeToChooseNewTargetField.SetValue(controller, rangeToChooseNewTargetAutomatically.Value);
            }
        }

        private void ApplyEntityCombatEnhancements(
            GameObject prefabClone,
            ContentOnHitStatusEffectDefinition[] onHitStatusEffects,
            float? criticalHitChance,
            string entityId)
        {
            if (prefabClone == null) return;
            Component controller = prefabClone.GetComponent(_entityControllerType);
            if (controller == null) return;

            string id = entityId;
            if (string.IsNullOrEmpty(id) && _entityControllerIdField != null)
            {
                id = _entityControllerIdField.GetValue(controller) as string;
            }

            if (!string.IsNullOrEmpty(id) && onHitStatusEffects != null && onHitStatusEffects.Length > 0)
            {
                RegisterOnHitStatusEffects(id, onHitStatusEffects);
            }

            if (criticalHitChance.HasValue && criticalHitChance.Value > 0f &&
                _critModType != null && _addCriticalHitChanceBonusMethod != null)
            {
                try
                {
                    object mod = Activator.CreateInstance(_critModType);
                    FieldInfo additionField = _critModType.GetField("addition", BindingFlags.Instance | BindingFlags.Public);
                    FieldInfo idField = _critModType.GetField("idToAvoidStacking", BindingFlags.Instance | BindingFlags.Public);
                    if (additionField != null) additionField.SetValue(mod, criticalHitChance.Value);
                    if (idField != null) idField.SetValue(mod, "rcl-crit-" + id);
                    _addCriticalHitChanceBonusMethod.Invoke(controller, new object[] { mod });
                }
                catch (Exception ex)
                {
                    _log?.Invoke("[ContentPackRuntimeLoader] Could not apply criticalHitChance to " + id + ": " + ex.Message);
                }
            }
        }

        private void ApplyEntityActiveSkills(
            GameObject prefabClone,
            ContentActiveSkillDefinition[] activeSkills,
            string entityId)
        {
            if (prefabClone == null || activeSkills == null || activeSkills.Length == 0) return;
            Component controller = prefabClone.GetComponent(_entityControllerType);
            if (controller == null) return;

            ContentActiveSkillDefinition skill = activeSkills[0];
            if (skill == null) return;

            if (_hasActiveSkillField != null)
            {
                _hasActiveSkillField.SetValue(controller, true);
            }

            if (_activeSkillOrProductionField != null)
            {
                Type enumType = _activeSkillOrProductionField.FieldType;
                if (enumType.IsEnum)
                {
                    _activeSkillOrProductionField.SetValue(controller, Enum.ToObject(enumType, 0));
                }
            }

            object targetOriginVal = null;
            if (_targetOriginType != null)
            {
                string targetOriginName = skill.targetOrigin;
                if (string.IsNullOrEmpty(targetOriginName))
                {
                    if (string.Equals(skill.skillType, "Target", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(skill.skillType, "TargetAoE", StringComparison.OrdinalIgnoreCase))
                    {
                        targetOriginName = "ChosenEntity";
                    }
                    else if (string.Equals(skill.skillType, "Position", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(skill.skillType, "Location", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(skill.skillType, "LocationAoE", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(skill.skillType, "LocationMultiTargets", StringComparison.OrdinalIgnoreCase))
                    {
                        targetOriginName = "ChosenLocation";
                    }
                    else
                    {
                        targetOriginName = "Self";
                    }
                }

                try
                {
                    targetOriginVal = Enum.Parse(_targetOriginType, targetOriginName, true);
                }
                catch
                {
                    targetOriginVal = Enum.ToObject(_targetOriginType, 0);
                }

                if (_activeSkillTypeField != null && targetOriginVal != null)
                {
                    _activeSkillTypeField.SetValue(controller, targetOriginVal);
                }
            }

            if (_skillInfoType != null && _skillInfoField != null && targetOriginVal != null)
            {
                try
                {
                    ConstructorInfo ctor = _skillInfoType.GetConstructor(new[] { _targetOriginType, typeof(float) });
                    if (ctor != null)
                    {
                        object skillInfoInstance = ctor.Invoke(new object[] { targetOriginVal, skill.manaCost });
                        _skillInfoField.SetValue(controller, skillInfoInstance);
                    }
                }
                catch (Exception ex)
                {
                    Log("Could not instantiate SkillInfo for " + entityId + ": " + ex.Message);
                }
            }

            bool helpAi = skill.helpAiToActivate ?? true;
            if (_helpAiToActivateSkillField != null)
            {
                _helpAiToActivateSkillField.SetValue(controller, helpAi);
            }

            float aiCooldown = skill.aiSkillCooldown.HasValue ? skill.aiSkillCooldown.Value : skill.cooldown;
            if (_aiSkillCooldownField != null)
            {
                _aiSkillCooldownField.SetValue(controller, aiCooldown);
            }

            if (helpAi && _skillActivationConditionType != null && _conditionToActivateSkillField != null)
            {
                try
                {
                    object condition = Activator.CreateInstance(_skillActivationConditionType);
                    FieldInfo nameField = _skillActivationConditionType.GetField("entityIdentifierName");
                    FieldInfo opField = _skillActivationConditionType.GetField("operation");
                    FieldInfo numField = _skillActivationConditionType.GetField("numberOfEntities");

                    string identName = "EnemiesInRange";
                    if (nameField != null) nameField.SetValue(condition, identName);
                    if (opField != null)
                    {
                        Type opEnumType = _skillActivationConditionType.GetNestedType("Operation");
                        if (opEnumType != null)
                        {
                            opField.SetValue(condition, Enum.Parse(opEnumType, "Greater"));
                        }
                    }
                    if (numField != null) numField.SetValue(condition, 0);

                    _conditionToActivateSkillField.SetValue(controller, condition);

                    if (_entityIdentifiersField != null && _entityIdentifierType != null)
                    {
                        System.Collections.IList idList = _entityIdentifiersField.GetValue(controller) as System.Collections.IList;
                        if (idList == null)
                        {
                            Type listType = typeof(List<>).MakeGenericType(_entityIdentifierType);
                            idList = Activator.CreateInstance(listType) as System.Collections.IList;
                            _entityIdentifiersField.SetValue(controller, idList);
                        }

                        bool hasIdent = false;
                        if (idList != null)
                        {
                            FieldInfo identNameField = _entityIdentifierType.GetField("name");
                            for (int i = 0; i < idList.Count; i++)
                            {
                                object item = idList[i];
                                if (item != null && identNameField != null &&
                                    string.Equals(identNameField.GetValue(item) as string, identName, StringComparison.OrdinalIgnoreCase))
                                {
                                    hasIdent = true;
                                    break;
                                }
                            }

                            if (!hasIdent)
                            {
                                object newIdent = Activator.CreateInstance(_entityIdentifierType);
                                if (identNameField != null) identNameField.SetValue(newIdent, identName);
                                FieldInfo radField = _entityIdentifierType.GetField("radius");
                                if (radField != null && radField.FieldType.IsEnum)
                                {
                                    try
                                    {
                                        radField.SetValue(newIdent, Enum.Parse(radField.FieldType, "SelfSkillRange"));
                                    }
                                    catch
                                    {
                                        // Ignore radius parse fallback
                                    }
                                }
                                idList.Add(newIdent);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("Could not configure conditionToActivateSkill for " + entityId + ": " + ex.Message);
                }
            }
        }

        private void ApplyEntityAudioBindings(
            GameObject prefabClone,
            AssetBundle bundle,
            string selectionSoundAssetName,
            string engineSoundAssetName,
            string weaponSoundAssetName,
            string entityId,
            string packageName)
        {
            if (prefabClone == null) return;
            Component controller = prefabClone.GetComponent(_entityControllerType);

            if (!String.IsNullOrWhiteSpace(selectionSoundAssetName) && bundle != null)
            {
                AudioClip selectionClip = LoadRequiredAsset(bundle, selectionSoundAssetName,
                    typeof(AudioClip), "AudioClip selection sound", packageName) as AudioClip;
                if (selectionClip != null && controller != null && _selectionSoundField != null)
                {
                    AudioSource audioSource = _selectionSoundField.GetValue(controller) as AudioSource;
                    if (audioSource == null)
                    {
                        audioSource = prefabClone.AddComponent<AudioSource>();
                        audioSource.playOnAwake = false;
                        _selectionSoundField.SetValue(controller, audioSource);
                    }
                    audioSource.clip = selectionClip;
                }
            }

            if (!String.IsNullOrWhiteSpace(engineSoundAssetName) && bundle != null)
            {
                AudioClip engineClip = LoadRequiredAsset(bundle, engineSoundAssetName,
                    typeof(AudioClip), "AudioClip engine sound", packageName) as AudioClip;
                if (engineClip != null && controller != null && _movementSoundAudioSourceField != null)
                {
                    AudioSource audioSource = _movementSoundAudioSourceField.GetValue(controller) as AudioSource;
                    if (audioSource == null)
                    {
                        audioSource = prefabClone.AddComponent<AudioSource>();
                        audioSource.playOnAwake = false;
                        audioSource.loop = true;
                        audioSource.spatialBlend = 1f;
                        _movementSoundAudioSourceField.SetValue(controller, audioSource);
                    }
                    audioSource.clip = engineClip;
                }
            }

            if (!String.IsNullOrWhiteSpace(weaponSoundAssetName) && bundle != null)
            {
                AudioClip weaponClip = LoadRequiredAsset(bundle, weaponSoundAssetName,
                    typeof(AudioClip), "AudioClip weapon sound", packageName) as AudioClip;
                if (weaponClip != null && controller is EntityController entityCtrl && entityCtrl.Events != null)
                {
                    foreach (EntityEvent evt in entityCtrl.Events)
                    {
                        if (evt.actions != null)
                        {
                            foreach (IEntityAction action in evt.actions)
                            {
                                if (action is ShootProjectile shoot)
                                {
                                    if (shoot.shotSoundAudioSource == null)
                                    {
                                        AudioSource audioSource = prefabClone.AddComponent<AudioSource>();
                                        audioSource.playOnAwake = false;
                                        audioSource.spatialBlend = 1f;
                                        shoot.shotSoundAudioSource = audioSource;
                                    }
                                    shoot.shotSoundAudioSource.clip = weaponClip;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void EnsureMuzzleFlashHierarchy(GameObject prefabClone)
        {
            if (prefabClone == null) return;
            EntityController controller = prefabClone.GetComponent<EntityController>();
            var firePointsTransforms = new List<Transform>();

            // 1. Recursive search for candidate muzzle sockets in the entire hierarchy
            List<Transform> candidateMuzzleSockets = FindTransformsRecursively(prefabClone.transform, t =>
            {
                if (t == null) return false;
                string name = t.name;
                string socketName;
                if (Runtime.Visual.RogueCommandSocketCatalog.TryResolveCanonicalSocketName(name, out socketName))
                {
                    if (socketName.StartsWith("Socket_Muzzle", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return name.StartsWith("Socket_Muzzle", StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith("FirePoint", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(name, "FirePoints", StringComparison.OrdinalIgnoreCase);
            });

            // 2. Discover all ShootProjectile actions on EntityController.Events
            var shootActions = new List<ShootProjectile>();
            if (controller != null && controller.Events != null)
            {
                foreach (EntityEvent evt in controller.Events)
                {
                    if (evt == null) continue;
                    if (evt.actions != null)
                    {
                        foreach (IEntityAction action in evt.actions)
                        {
                            if (action is ShootProjectile shoot)
                            {
                                shootActions.Add(shoot);
                            }
                        }
                    }
                    if (evt.conditionalActions != null)
                    {
                        foreach (ConditionalEntityActions cond in evt.conditionalActions)
                        {
                            if (cond != null && cond.actions != null)
                            {
                                foreach (IEntityAction action in cond.actions)
                                {
                                    if (action is ShootProjectile shoot)
                                    {
                                        shootActions.Add(shoot);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 3. Ensure every ShootProjectile has a valid non-null firePointsTransform
            foreach (ShootProjectile shoot in shootActions)
            {
                if (shoot.firePointsTransform == null)
                {
                    Transform bestSocket = null;
                    if (candidateMuzzleSockets.Count > 0)
                    {
                        bestSocket = candidateMuzzleSockets[0];
                    }
                    else
                    {
                        GameObject muzzleObj = new GameObject("Socket_MuzzlePrimary");
                        muzzleObj.transform.SetParent(prefabClone.transform, false);
                        muzzleObj.transform.localPosition = new Vector3(0f, 1f, 1f);
                        muzzleObj.transform.localRotation = Quaternion.identity;
                        bestSocket = muzzleObj.transform;
                        candidateMuzzleSockets.Add(bestSocket);
                    }
                    shoot.firePointsTransform = bestSocket;
                }

                if (!firePointsTransforms.Contains(shoot.firePointsTransform))
                {
                    firePointsTransforms.Add(shoot.firePointsTransform);
                }
            }

            // 4. Also validate any candidate muzzle sockets discovered in hierarchy
            foreach (Transform muzzle in candidateMuzzleSockets)
            {
                if (muzzle != null && !firePointsTransforms.Contains(muzzle))
                {
                    firePointsTransforms.Add(muzzle);
                }
            }

            // 5. Ensure valid ShootProjectile hierarchy on each firePointsTransform
            for (int f = 0; f < firePointsTransforms.Count; f++)
            {
                Transform fpTransform = firePointsTransforms[f];
                if (fpTransform == null) continue;

                if (fpTransform.childCount == 0)
                {
                    GameObject fp0 = new GameObject("FirePoint_0");
                    fp0.transform.SetParent(fpTransform, false);
                    fp0.transform.localPosition = Vector3.zero;
                    fp0.transform.localRotation = Quaternion.identity;
                }

                int count = fpTransform.childCount;
                for (int i = 0; i < count; i++)
                {
                    Transform firePoint = fpTransform.GetChild(i);
                    if (firePoint == null) continue;

                    if (firePoint.childCount < 1)
                    {
                        GameObject fallbackChild = new GameObject("MuzzleFlash_Fallback");
                        fallbackChild.transform.SetParent(firePoint, false);
                        fallbackChild.transform.localPosition = Vector3.zero;
                        fallbackChild.transform.localRotation = Quaternion.identity;
                        ParticleSystem ps = fallbackChild.AddComponent<ParticleSystem>();
                        var main = ps.main;
                        main.playOnAwake = false;
                        main.duration = 0.1f;
                        ps.Stop();
                    }
                    else
                    {
                        Transform firstChild = firePoint.GetChild(0);
                        if (firstChild != null && firstChild.GetComponent<ParticleSystem>() == null)
                        {
                            ParticleSystem ps = firstChild.gameObject.AddComponent<ParticleSystem>();
                            var main = ps.main;
                            main.playOnAwake = false;
                            main.duration = 0.1f;
                            ps.Stop();
                        }
                    }
                }
            }
        }

        private static List<Transform> FindTransformsRecursively(Transform root, Func<Transform, bool> predicate)
        {
            var results = new List<Transform>();
            if (root == null) return results;
            var queue = new Queue<Transform>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                if (predicate(current))
                {
                    results.Add(current);
                }
                for (int i = 0; i < current.childCount; i++)
                {
                    queue.Enqueue(current.GetChild(i));
                }
            }
            return results;
        }

        private sealed class LoadedUnit
        {
            public LoadedUnit(
                string id,
                string packId,
                string prefabKey,
                string imageKey,
                object balanceRow,
                ContentUnitDefinition definition,
                bool isProductionBuilding,
                string productionFactoryId,
                int prespawnCount = 20,
                bool isStandaloneBuilding = false)
            {
                Id = id;
                PackId = packId;
                PrefabKey = prefabKey;
                ImageKey = imageKey;
                BalanceRow = balanceRow;
                Definition = definition;
                IsProductionBuilding = isProductionBuilding;
                ProductionFactoryId = productionFactoryId;
                PrespawnCount = prespawnCount;
                IsStandaloneBuilding = isStandaloneBuilding;
            }

            public string Id { get; private set; }
            public string PackId { get; private set; }
            public string PrefabKey { get; private set; }
            public string ImageKey { get; private set; }
            public object BalanceRow { get; private set; }
            public ContentUnitDefinition Definition { get; private set; }
            public bool IsProductionBuilding { get; private set; }
            public string ProductionFactoryId { get; private set; }
            public int PrespawnCount { get; private set; }
            public bool IsStandaloneBuilding { get; private set; }
        }

        private sealed class LoadedStage
        {
            public LoadedStage(
                ContentStageDefinition definition,
                string packId,
                object runtimeStage,
                List<object> blueprintPools,
                List<object> worlds,
                LoadedLandscape landscape,
                List<BlueprintPoolScopeRegistration> blueprintPoolScopes,
                HashSet<string> blueprintFactoryIds,
                UnityEngine.Object fallbackLandscape)
            {
                Definition = definition;
                PackId = packId;
                RuntimeStage = runtimeStage;
                BlueprintPools = blueprintPools;
                Worlds = worlds;
                Landscape = landscape;
                BlueprintPoolScopes = blueprintPoolScopes;
                BlueprintFactoryIds = new HashSet<string>(blueprintFactoryIds, StringComparer.Ordinal);
                FallbackLandscape = fallbackLandscape;
            }

            public ContentStageDefinition Definition { get; private set; }
            public string PackId { get; private set; }
            public object RuntimeStage { get; private set; }
            public List<object> BlueprintPools { get; private set; }
            public List<object> Worlds { get; private set; }
            public LoadedLandscape Landscape { get; private set; }
            public List<BlueprintPoolScopeRegistration> BlueprintPoolScopes { get; private set; }
            public HashSet<string> BlueprintFactoryIds { get; private set; }
            public UnityEngine.Object FallbackLandscape { get; private set; }
        }

        private sealed class LoadedLandscape
        {
            public LoadedLandscape(
                string resourceName,
                string resourcePath,
                string localizationId,
                UnityEngine.Object asset,
                ContentStageLandscapeDefinition definition)
            {
                ResourceName = resourceName;
                ResourcePath = resourcePath;
                LocalizationId = localizationId;
                Asset = asset;
                Definition = definition;
            }

            public string ResourceName { get; private set; }
            public string ResourcePath { get; private set; }
            public string LocalizationId { get; private set; }
            public UnityEngine.Object Asset { get; private set; }
            public ContentStageLandscapeDefinition Definition { get; private set; }
        }

        private sealed class StageLandscapeMutation : IDisposable
        {
            private ContentPackRuntimeLoader _owner;

            public StageLandscapeMutation(ContentPackRuntimeLoader owner)
            {
                _owner = owner;
                Entries = new List<ScopedLandscapeListEntry>();
            }

            public List<ScopedLandscapeListEntry> Entries { get; private set; }

            public void Record(IList list, UnityEngine.Object landscape)
            {
                Entries.Add(new ScopedLandscapeListEntry(list, landscape));
            }

            public void Dispose()
            {
                ContentPackRuntimeLoader owner = _owner;
                if (owner == null)
                {
                    return;
                }

                _owner = null;
                owner.RestoreStageLandscapeMutation(this);
            }
        }

        private sealed class ScopedLandscapeListEntry
        {
            public ScopedLandscapeListEntry(IList list, UnityEngine.Object landscape)
            {
                List = list;
                Landscape = landscape;
            }

            public IList List { get; private set; }
            public UnityEngine.Object Landscape { get; private set; }
        }

        private sealed class LoadedAiProfile
        {
            public LoadedAiProfile(
                ContentAiProfileDefinition definition,
                string packId,
                string resourceName,
                List<object> behaviours,
                GameObject prefab,
                int stageMin = 0,
                int stageMax = -1)
            {
                Definition = definition;
                PackId = packId;
                ResourceName = resourceName;
                Behaviours = behaviours;
                Prefab = prefab;
                var moduleIds = new List<string>();
                ContentAiModuleDefinition[] modules = definition.modules ?? new ContentAiModuleDefinition[0];
                for (int i = 0; i < modules.Length; i++)
                {
                    if (modules[i] != null && !String.IsNullOrWhiteSpace(modules[i].moduleId))
                    {
                        moduleIds.Add(modules[i].moduleId);
                    }
                }
                ModuleIds = moduleIds.ToArray();
                StageMin = stageMin;
                StageMax = stageMax;
            }

            public ContentAiProfileDefinition Definition { get; private set; }
            public string PackId { get; private set; }
            public string Id { get { return Definition.id; } }
            public string ResourceName { get; private set; }
            public bool ScopeToReferencedStages { get { return Definition.scopeToReferencedStages; } }
            public string[] ModuleIds { get; private set; }
            public List<object> Behaviours { get; private set; }
            public GameObject Prefab { get; private set; }
            public int StageMin { get; private set; }
            public int StageMax { get; private set; }
        }

        private sealed class BoundAiRuntimeBehaviour
        {
            public BoundAiRuntimeBehaviour(
                IRogueCommandAiRuntimeBehaviour runtimeBehaviour,
                string failure,
                string profileId)
            {
                RuntimeBehaviour = runtimeBehaviour;
                Failed = !String.IsNullOrEmpty(failure);
                ProfileId = profileId;
            }

            public IRogueCommandAiRuntimeBehaviour RuntimeBehaviour { get; set; }
            public string ProfileId { get; private set; }
            public bool Initialized { get; set; }
            public bool Initializing { get; set; }
            public bool Failed { get; set; }
            public bool Destroyed { get; set; }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object left, object right) { return System.Object.ReferenceEquals(left, right); }
            public int GetHashCode(object value) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value); }
        }
    }
}
