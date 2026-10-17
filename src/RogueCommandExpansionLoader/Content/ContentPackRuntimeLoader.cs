using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
    public sealed class ContentPackRuntimeLoader : IRuntimeContentProvider
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
        private readonly AiModuleRegistry _moduleRegistry;
        private readonly Action<string> _log;
        private readonly Type _entityRowType;
        private readonly Type _entityBalanceAssetType;
        private readonly Type _entityControllerType;
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
        private readonly FieldInfo _entityControllerIdField;
        private readonly FieldInfo _prefabLocationField;
        private readonly FieldInfo _imageLocationField;
        private readonly FieldInfo _inactiveField;
        private readonly FieldInfo _allowedBlueprintField;
        private readonly FieldInfo _stageParametersField;
        private readonly FieldInfo _stageMapSizeField;
        private readonly FieldInfo _stageDemoField;
        private readonly FieldInfo _gameBalancingAssetField;
        private readonly FieldInfo _gameIsDemoField;
        private readonly FieldInfo _gameMaxAscensionLevelField;
        private readonly FieldInfo[] _progressionFields;
        private readonly FieldInfo _progressionStagesField;
        private readonly FieldInfo _progressionAisField;
        private readonly FieldInfo _progressionLandscapesField;
        private readonly FieldInfo _stageParametersWorldListField;
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
        private readonly FieldInfo _landscapeDeactivatedField;
        private readonly FieldInfo _landscapeAllowedForDemoField;
        private readonly FieldInfo _entityIndexField;
        private readonly FieldInfo _nativeEntityRowsField;
        private readonly FieldInfo _aiDeactivatedField;
        private readonly FieldInfo _aiDeckEntryEntityIdField;
        private readonly FieldInfo _aiBasesField;
        private readonly FieldInfo _aiBaseEntitiesInDeckField;
        private readonly FieldInfo _worldsField;
        private readonly FieldInfo _worldGeneratorField;
        private readonly FieldInfo _blueprintPoolsField;
        private readonly FieldInfo _blueprintPoolIdField;
        private readonly FieldInfo _blueprintFilterField;

        private readonly List<object> _entityRows = new List<object>();
        private readonly List<ProgressionContentContribution> _contributions =
            new List<ProgressionContentContribution>();
        private readonly List<AssetBundle> _assetBundles = new List<AssetBundle>();
        private readonly List<FileStream> _assetBundleStreams = new List<FileStream>();
        private readonly List<GameObject> _nativePrefabClones = new List<GameObject>();
        private readonly List<ScriptableObject> _stageParameterClones = new List<ScriptableObject>();
        private readonly List<object> _blueprintPools = new List<object>();
        private readonly Dictionary<string, UnityEngine.Object> _resourceAssets =
            new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
        private readonly Dictionary<string, LoadedAiProfile> _aiProfiles =
            new Dictionary<string, LoadedAiProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, LoadedAiProfile> _aiProfilesByResourceName =
            new Dictionary<string, LoadedAiProfile>(StringComparer.Ordinal);
        private readonly Dictionary<object, BoundAiRuntimeBehaviour> _boundAiBehaviours =
            new Dictionary<object, BoundAiRuntimeBehaviour>(ReferenceComparer.Instance);
        private readonly Dictionary<string, LoadedUnit> _units =
            new Dictionary<string, LoadedUnit>(StringComparer.Ordinal);
        private readonly HashSet<string> _knownIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _nativeUnitIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _blueprintUnitIds = new HashSet<string>(StringComparer.Ordinal);
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
            _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException("moduleRegistry");
            _log = logger;

            _entityRowType = FindUniqueType("EntityBalancingParameters");
            _entityBalanceAssetType = FindUniqueType("EntityBalancingScriptableObject");
            _entityControllerType = FindUniqueType("EntityController");
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
            _entityControllerIdField = RequireField(_entityControllerType, "entityId", typeof(string));
            _prefabLocationField = RequireField(_entityRowType, "prefabLocation", typeof(string));
            _imageLocationField = RequireField(_entityRowType, "imageLocation", typeof(string));
            _inactiveField = RequireField(_entityRowType, "inactive", typeof(bool));
            _allowedBlueprintField = RequireField(_entityRowType, "isAllowedAsBlueprint", typeof(bool));
            _stageParametersField = RequireField(_stageType, "stageParameters", _stageParametersType);
            _stageMapSizeField = RequireField(_stageType, "mapSizeRange", typeof(Vector2Int));
            _stageDemoField = RequireField(_stageType, "isPartOfDemo", typeof(bool));
            Type gameBalancingStoreType = FindUniqueType("GameBalancingStore");
            _gameBalancingAssetField = FindField(gameBalancingStoreType,
                "_gameBalancingScriptableObject", StaticFields);
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
            _landscapeDeactivatedField = RequireField(landscapeGeneratorType, "isDeactivated", typeof(bool));
            _landscapeAllowedForDemoField = RequireField(landscapeGeneratorType,
                "isAllowedForDemo", typeof(bool));
            _aiBaseStartingStageField = RequireField(_aiBaseType, "startingInStage", typeof(int));
            _aiBaseStartingAscensionField = RequireField(_aiBaseType,
                "startingInAscensionLevel", typeof(int));
            _blueprintPoolsField = FindField(_blueprintPoolListType, "blueprintPools", InstanceFields);
            if (!IsExactList(_blueprintPoolsField.FieldType, _blueprintPoolType))
            {
                throw new InvalidOperationException("BlueprintPoolList.blueprintPools is not List<BlueprintPool>.");
            }
            _blueprintPoolIdField = RequireField(_blueprintPoolType, "poolId", typeof(string));
            _blueprintFilterField = FindField(_blueprintPoolType, "blueprintFilter", InstanceFields);

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

                LoadAllPacks();
                ApplyBlueprintEligibility();
                _runtimeContentReady = _loaded;
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

        public void OnEntityRowsRegistered()
        {
            lock (_gate)
            {
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
                    try { allowedAsBlueprint = (bool)_allowedBlueprintField.GetValue(unit.BalanceRow); }
                    catch { }
                    units.Add(new UnitDiagnosticReport(
                        unit.Id, unit.PackId, unit.PrefabKey, unit.ImageKey, allowedAsBlueprint));
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

            foreach (object key in index.Keys)
            {
                string id = key as string;
                if (!String.IsNullOrWhiteSpace(id))
                {
                    _nativeUnitIds.Add(id);
                }
            }
        }

        private void LoadAllPacks()
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
                    ResolvePackDependencies(candidate, unitOwners, profileOwners);
                }

                LoadPacksInDependencyOrder(candidates);

                Log("Carga finalizada: " + _contributions.Count + " paquete(s) de progresión y " +
                    _entityRows.Count + " unidad(es) nuevas.");
            }
            catch (Exception exception)
            {
                _loaded = false;
                _packRootError = exception.GetType().Name + ": " + exception.Message;
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
                for (int i = 0; i < definition.units.Length; i++)
                {
                    if (definition.units[i] != null) yield return definition.units[i].id;
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
        }

        private void LoadPacksInDependencyOrder(List<PackCandidate> candidates)
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
                        candidate.Disabled = true;
                        candidate.StatusReason = "depende del paquete '" + failedDependency.Name + "', que no pudo cargarse.";
                        Log("Paquete '" + candidate.Name + "' desactivado: " + candidate.StatusReason);
                        pending.RemoveAt(i);
                        advanced = true;
                        continue;
                    }

                    if (waitingForDependency)
                    {
                        continue;
                    }

                    candidate.LoadAttempted = true;
                    candidate.LoadSucceeded = LoadOnePack(candidate);
                    if (!candidate.LoadSucceeded)
                    {
                        candidate.Disabled = true;
                    }
                    else
                    {
                        candidate.StatusReason = null;
                    }

                    pending.RemoveAt(i);
                    advanced = true;
                }

                if (!advanced)
                {
                    for (int i = 0; i < pending.Count; i++)
                    {
                        pending[i].Disabled = true;
                        pending[i].StatusReason = "se detectó una dependencia circular entre paquetes.";
                        Log("Paquete '" + pending[i].Name + "' desactivado: " + pending[i].StatusReason);
                    }

                    break;
                }
            }
        }

        private bool LoadOnePack(PackCandidate candidate)
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
            var temporaryUnits = new List<LoadedUnit>();
            var temporaryStages = new List<LoadedStage>();
            var temporaryProfiles = new List<LoadedAiProfile>();
            AssetBundle manifestBundle = null;
            FileStream manifestStream = null;
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
                    manifestBundle = AssetBundle.LoadFromStream(manifestStream, 0, 0);

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
                        throw new InvalidDataException(pathFailure);
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
                    AssetBundle bundle = AssetBundle.LoadFromStream(stream, 0, 0);
                    if (bundle == null)
                    {
                        throw new InvalidDataException("AssetBundle no válido: " + bundleDefinition.path);
                    }

                    temporaryBundles.Add(bundleDefinition.id, bundle);
                }

                for (int i = 0; i < definition.units.Length; i++)
                {
                    ContentUnitDefinition unit = definition.units[i];
                    bool usesNativeTemplate = !String.IsNullOrWhiteSpace(unit.nativeTemplateId);
                    object row;
                    string prefabKey;
                    string imageKey;
                    if (usesNativeTemplate)
                    {
                        row = ReadNativeBalanceRow(unit.nativeTemplateId, packageName, unit.id);
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

                        _entityControllerIdField.SetValue(clonedController, unit.id);
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

                        ApplyBalanceOverrides(row, unit.balanceOverrides, packageName, unit.id);
                        _entityIdField.SetValue(row, unit.id);
                        _prefabLocationField.SetValue(row, prefabKey);
                        _imageLocationField.SetValue(row, imageKey);
                        _inactiveField.SetValue(row, false);
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

                        prefabKey = ResourceKey(unit.id, "prefab");
                        imageKey = ResourceKey(unit.id, "image");
                        ApplyBalanceOverrides(row, unit.balanceOverrides, packageName, unit.id);
                        _entityIdField.SetValue(row, unit.id);
                        _prefabLocationField.SetValue(row, prefabKey);
                        _imageLocationField.SetValue(row, imageKey);
                        _inactiveField.SetValue(row, false);

                        temporaryResources.Add(prefabKey, prefab);
                        temporaryResources.Add(imageKey, cardImage);
                    }

                    temporaryUnits.Add(new LoadedUnit(unit.id, definition.packId, prefabKey, imageKey, row));
                }

                for (int i = 0; i < definition.stages.Length; i++)
                {
                    ContentStageDefinition stage = definition.stages[i];
                    UnityEngine.Object parameters;
                    if (!String.IsNullOrWhiteSpace(stage.nativeTemplateId))
                    {
                        UnityEngine.Object nativeParameters = FindNativeStageTemplate(
                            stage.nativeTemplateId, packageName, stage.id);
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
                    object runtimeStage = CreateStage(stage, parameters, packageName);
                    temporaryStages.Add(new LoadedStage(stage, definition.packId, runtimeStage,
                        pools, stageWorlds));
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
                            if (String.IsNullOrWhiteSpace(deckEntityId) || !IsUnitAvailable(deckEntityId, temporaryUnits))
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
                    temporaryProfiles.Add(new LoadedAiProfile(profile, definition.packId, resourceName,
                        behaviours, prefab));
                }

                ValidateStageGenerationCompatibility(temporaryStages, temporaryProfiles, packageName);

                CommitPack(definition, temporaryBundles, temporaryBundleStreams, packBundleBytes, temporaryResources,
                    temporaryUnits, temporaryStages, temporaryProfiles);
                _nativePrefabClones.AddRange(temporaryNativePrefabs);
                _stageParameterClones.AddRange(temporaryStageParameterClones);
                candidate.LoadedBytes = packBundleBytes;
                return true;
            }
            catch (Exception exception)
            {
                candidate.StatusReason = exception.GetType().Name + ": " + exception.Message;
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

                try { if (manifestBundle != null) manifestBundle.Unload(true); } catch { }
                try { if (manifestStream != null) manifestStream.Dispose(); } catch { }
                foreach (AssetBundle bundle in temporaryBundles.Values)
                {
                    try { if (bundle != null) bundle.Unload(true); } catch { }
                }

                for (int i = 0; i < temporaryBundleStreams.Count; i++)
                {
                    try { if (temporaryBundleStreams[i] != null) temporaryBundleStreams[i].Dispose(); } catch { }
                }

                Log("Paquete '" + (String.IsNullOrWhiteSpace(definition.packId) ? packageName : definition.packId) +
                    "' desactivado sin registrar contenido: " + exception.GetType().Name + ": " + exception.Message);
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

            var contributionStages = new List<object>();
            var contributionAi = new List<object>();
            var blueprintUnitIds = new HashSet<string>(StringComparer.Ordinal);
            var localUnitIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < units.Count; i++)
            {
                localUnitIds.Add(units[i].Id);
            }

            var seenBehaviourReferences = new HashSet<object>(ReferenceComparer.Instance);
            for (int i = 0; i < stages.Count; i++)
            {
                LoadedStage stage = stages[i];
                contributionStages.Add(stage.RuntimeStage);
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
                    if (localUnitIds.Contains(unitIds[unitIndex]) || _units.ContainsKey(unitIds[unitIndex]))
                    {
                        blueprintUnitIds.Add(unitIds[unitIndex]);
                    }
                    else if (_nativeUnitIds.Contains(unitIds[unitIndex]))
                    {
                        // Existing game rows keep their authored flags; the stage pool still decides eligibility.
                    }
                    else
                    {
                        throw new InvalidDataException("La unidad " + unitIds[unitIndex] +
                            " de la etapa " + stage.Definition.id + " no está disponible.");
                    }
                }
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

            // All checks above complete before any pack-owned runtime registry is updated.
            for (int i = 0; i < units.Count; i++)
            {
                _units.Add(units[i].Id, units[i]);
                _entityRows.Add(units[i].BalanceRow);
            }

            for (int i = 0; i < profiles.Count; i++)
            {
                _aiProfiles.Add(profiles[i].Id, profiles[i]);
                _aiProfilesByResourceName.Add(profiles[i].ResourceName, profiles[i]);
            }

            for (int i = 0; i < stages.Count; i++)
            {
                _stages.Add(stages[i]);
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

            _contributions.Add(new ProgressionContentContribution(
                definition.packId, contributionStages, contributionAi));
            Log("Paquete '" + definition.packId + "' cargado: " + units.Count + " unidad(es), " +
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

        private object ReadNativeBalanceRow(string templateId, string packageName, string unitId)
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

            object row = rows[rowIndex];
            if (row == null || row.GetType() != _entityRowType ||
                !String.Equals(_entityIdField.GetValue(row) as string, templateId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("El índice de la plantilla nativa '" + templateId +
                    "' no apunta a su fila EntityBalancingParameters.");
            }

            return row;
        }

        private void ApplyBalanceOverrides(
            object row,
            ContentBalanceOverride[] overrides,
            string packageName,
            string unitId)
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
                        CultureInfo.InvariantCulture, out parsed) || parsed < 0)
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
                else
                {
                    throw new InvalidDataException("Unidad " + unitId + " en '" + packageName +
                        "': el campo " + balanceOverride.name + " no es un override numérico compatible.");
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

            if (validateAssetName && !String.Equals(prefab.name, unit.prefabAssetName, StringComparison.Ordinal))
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

        private static void ValidateVisualPrefab(GameObject prefab, ContentUnitDefinition unit, string packageName)
        {
            if (prefab == null)
            {
                throw new InvalidDataException("Unidad " + unit.id + " en '" + packageName +
                    "': el prefab visual está vacío.");
            }

            if (!String.Equals(prefab.name, unit.visualAssetName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unidad " + unit.id + " en '" + packageName +
                    "': el nombre interno del prefab visual '" + prefab.name +
                    "' no coincide con visualAssetName '" + unit.visualAssetName + "'.");
            }

            ValidateNoMissingPrefabComponents(prefab, packageName, "Visual de unidad " + unit.id);
            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is MonoBehaviour)
                {
                    throw new InvalidDataException("Visual de unidad " + unit.id + " en '" + packageName +
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
                throw new InvalidDataException("Visual de unidad " + unit.id + " en '" + packageName +
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
            string packageName)
        {
            Transform target = FindUniqueTransformPath(nativePrefab.transform, unit.visualRootPath);
            if (target == null)
            {
                throw new InvalidDataException("Unidad " + unit.id + " en '" + packageName +
                    "': visualRootPath '" + unit.visualRootPath +
                    "' no identifica una única ruta dentro de la jerarquía del prefab nativo.");
            }

            Renderer[] originalRenderers = target.GetComponentsInChildren<Renderer>(true);
            if (originalRenderers.Length == 0)
            {
                throw new InvalidDataException("Unidad " + unit.id + " en '" + packageName +
                    "': visualRootPath '" + unit.visualRootPath + "' no contiene Renderer nativos que reemplazar.");
            }

            Material fallbackMaterial = FindNativeVisualMaterial(originalRenderers);

            for (int i = 0; i < originalRenderers.Length; i++)
            {
                originalRenderers[i].enabled = false;
            }

            GameObject visualClone = UnityEngine.Object.Instantiate(visualTemplate, target, false);
            if (visualClone == null)
            {
                throw new InvalidDataException("Unidad " + unit.id + " en '" + packageName +
                    "': Unity no pudo instanciar el prefab visual.");
            }

            visualClone.name = "RCL_Visual_" + unit.id.Replace(':', '_');
            visualClone.transform.localPosition = Vector3.zero;
            visualClone.transform.localRotation = Quaternion.identity;
            visualClone.transform.localScale = Vector3.one;
            ApplyFallbackVisualMaterial(visualClone, fallbackMaterial, unit, packageName);
            visualClone.SetActive(true);
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
            string packageName)
        {
            Renderer[] renderers = visualClone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                bool hasMaterial = false;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (materials[materialIndex] != null)
                    {
                        hasMaterial = true;
                        break;
                    }
                }

                if (hasMaterial)
                {
                    continue;
                }

                if (fallbackMaterial == null)
                {
                    throw new InvalidDataException("Unidad " + unit.id + " en '" + packageName +
                        "': el modelo visual no tiene materiales y la plantilla nativa no ofrece un material MeshRenderer.");
                }

                renderers[i].sharedMaterial = fallbackMaterial;
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

        private bool IsUnitAvailable(string entityId, List<LoadedUnit> temporaryUnits)
        {
            if (_nativeUnitIds.Contains(entityId) || _units.ContainsKey(entityId))
            {
                return true;
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
                    Worlds = BuildWorldCandidates(stage.Worlds, stage.Definition.id)
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
                    RestrictedMapSizes = ReadRestrictedMapSizes(restrictedMaps,
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

                candidates.Add(new StageGenerationLandscapeCandidate
                {
                    Id = GetObjectName(landscape),
                    MapSize = (int)_landscapeMapSizeField.GetValue(landscape),
                    IsDeactivated = (bool)_landscapeDeactivatedField.GetValue(landscape),
                    IsAllowedForDemo = (bool)_landscapeAllowedForDemoField.GetValue(landscape)
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
                    RestrictedMapSizes = ReadRestrictedMapSizes(restrictedMaps,
                        "World " + GetObjectName(world))
                });
            }

            return candidates;
        }

        private IList<int> ReadRestrictedMapSizes(IList restrictedMaps, string source)
        {
            var mapSizes = new List<int>(restrictedMaps.Count);
            for (int i = 0; i < restrictedMaps.Count; i++)
            {
                object landscape = restrictedMaps[i];
                if (landscape == null || landscape.GetType() != _landscapeGeneratorType)
                {
                    throw new InvalidDataException(source +
                        " contiene un LandscapeGenerator nulo o de tipo inesperado en restrictedToMaps.");
                }

                mapSizes.Add((int)_landscapeMapSizeField.GetValue(landscape));
            }

            return mapSizes;
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
                if (field.FieldType == typeof(int))
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
                else
                {
                    throw new InvalidDataException("Etapa " + stageId + " en '" + packageName +
                        "': el campo " + stageOverride.name + " no es un parámetro numérico compatible.");
                }

                field.SetValue(parameters, value);
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
                asset = bundle.LoadAsset(assetName);
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
                return _owner._nativeUnitIds.Contains(id) || _owner._units.ContainsKey(id) ||
                    _declaredUnitIds.Contains(id);
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

        private sealed class LoadedUnit
        {
            public LoadedUnit(string id, string packId, string prefabKey, string imageKey, object balanceRow)
            {
                Id = id;
                PackId = packId;
                PrefabKey = prefabKey;
                ImageKey = imageKey;
                BalanceRow = balanceRow;
            }

            public string Id { get; private set; }
            public string PackId { get; private set; }
            public string PrefabKey { get; private set; }
            public string ImageKey { get; private set; }
            public object BalanceRow { get; private set; }
        }

        private sealed class LoadedStage
        {
            public LoadedStage(
                ContentStageDefinition definition,
                string packId,
                object runtimeStage,
                List<object> blueprintPools,
                List<object> worlds)
            {
                Definition = definition;
                PackId = packId;
                RuntimeStage = runtimeStage;
                BlueprintPools = blueprintPools;
                Worlds = worlds;
            }

            public ContentStageDefinition Definition { get; private set; }
            public string PackId { get; private set; }
            public object RuntimeStage { get; private set; }
            public List<object> BlueprintPools { get; private set; }
            public List<object> Worlds { get; private set; }
        }

        private sealed class LoadedAiProfile
        {
            public LoadedAiProfile(
                ContentAiProfileDefinition definition,
                string packId,
                string resourceName,
                List<object> behaviours,
                GameObject prefab)
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
            }

            public ContentAiProfileDefinition Definition { get; private set; }
            public string PackId { get; private set; }
            public string Id { get { return Definition.id; } }
            public string ResourceName { get; private set; }
            public bool ScopeToReferencedStages { get { return Definition.scopeToReferencedStages; } }
            public string[] ModuleIds { get; private set; }
            public List<object> Behaviours { get; private set; }
            public GameObject Prefab { get; private set; }
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
