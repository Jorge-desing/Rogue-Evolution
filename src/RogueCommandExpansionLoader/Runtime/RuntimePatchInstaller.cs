using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RogueCommandExpansionLoader.Compatibility;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Installs the loader's Harmony hooks after checking the exact analyzed game build and all
    /// target method shapes. Game-specific patch targets and registry shapes are resolved by reflection.
    /// </summary>
    public static class RuntimePatchInstaller
    {
        public const string HarmonyOwnerId = "RogueCommand.ExpansionLoader.Runtime";

        private static readonly object Gate = new object();
        private static Harmony _harmony;
        private static IRuntimeContentProvider _provider;
        private static RuntimeContentMutator _mutator;
        private static Assembly _gameAssembly;
        private static bool _cleanupPending;
        private static int _installedPatchCount;
        private static List<string> _installedPatchNames;

        public static RuntimePatchInstallationResult Install(
            string gameRoot,
            Assembly gameAssembly,
            IRuntimeContentProvider provider,
            Action<string> logger)
        {
            lock (Gate)
            {
                if (_harmony != null)
                {
                    if (_cleanupPending)
                    {
                        try
                        {
                            _harmony.UnpatchSelf();
                            RuntimeHooks.Clear(_mutator);
                            ClearInstallationState();
                        }
                        catch (Exception exception)
                        {
                            return Failed("A previous Harmony rollback is still pending and could not be retried: " +
                                exception.Message);
                        }
                    }
                    else
                    {
                        if (object.ReferenceEquals(_provider, provider) &&
                            object.ReferenceEquals(_gameAssembly, gameAssembly))
                        {
                            return new RuntimePatchInstallationResult(true, true,
                                "Runtime hooks are already installed for this provider and game assembly.");
                        }

                        return new RuntimePatchInstallationResult(false, false,
                            "Runtime hooks are already bound to a different provider or game assembly.");
                    }
                }

                if (provider == null)
                {
                    return Failed("A runtime content provider is required.");
                }

                string compatibilityFailure;
                if (!GameCompatibility.TryValidate(gameRoot, out compatibilityFailure))
                {
                    return Failed("Build guard rejected installation: " + compatibilityFailure);
                }

                string guardFailure;
                if (!RuntimeBuildGuard.TryValidate(gameRoot, provider.TargetGameVersion,
                    gameAssembly, out guardFailure))
                {
                    return Failed("Build guard rejected installation: " + guardFailure);
                }

                RuntimeContentMutator mutator;
                IList<PatchPlan> patches;
                try
                {
                    mutator = new RuntimeContentMutator(gameAssembly, provider, logger);
                    patches = ResolvePatchPlan(gameAssembly);
                }
                catch (Exception exception)
                {
                    return Failed("Runtime target validation failed: " + exception.Message);
                }

                Harmony harmony = null;
                try
                {
                    RuntimeHooks.Bind(mutator, provider, logger);
                    RuntimeHooks.OnEntityInstantiated = delegate(object entityObj)
                    {
                        Visual.RuntimeVisualDirector.AttachIfApplicable(entityObj);
                        RuntimeUnitMechanicsDirector.AttachIfApplicable(entityObj);
                        RuntimeGameplayCommandDirector.ApplyAiProfileUnitDefaultsToEntity(entityObj);
                    };
                    harmony = new Harmony(HarmonyOwnerId);
                    var installedNames = new List<string>();
                    for (int i = 0; i < patches.Count; i++)
                    {
                        PatchPlan patch = patches[i];
                        HarmonyMethod prefix = patch.Prefix == null ? null : new HarmonyMethod(patch.Prefix);
                        HarmonyMethod postfix = patch.Postfix == null ? null : new HarmonyMethod(patch.Postfix);
                        HarmonyMethod transpiler = patch.Transpiler == null ? null : new HarmonyMethod(patch.Transpiler);
                        HarmonyMethod finalizer = patch.Finalizer == null ? null : new HarmonyMethod(patch.Finalizer);
                        MethodInfo patched = harmony.Patch(patch.Target, prefix, postfix, transpiler, finalizer, null);
                        if (patched != null)
                        {
                            installedNames.Add(DescribePatch(patch));
                        }
                    }

                    _harmony = harmony;
                    _provider = provider;
                    _mutator = mutator;
                    _gameAssembly = gameAssembly;
                    _installedPatchNames = installedNames;
                    _installedPatchCount = installedNames.Count;
                    Log(logger, "Installed " + patches.Count + " verified Harmony hooks for Rogue Command " +
                        RuntimeBuildGuard.ExpectedGameVersion + ".");
                    return new RuntimePatchInstallationResult(true, false,
                        "Runtime hooks installed for Rogue Command " + RuntimeBuildGuard.ExpectedGameVersion + ".");
                }
                catch (Exception exception)
                {
                    Exception rollbackFailure = null;
                    if (harmony != null)
                    {
                        try
                        {
                            harmony.UnpatchSelf();
                        }
                        catch (Exception rollbackException)
                        {
                            rollbackFailure = rollbackException;
                        }
                    }

                    RuntimeHooks.Clear(mutator);
                    if (rollbackFailure != null)
                    {
                        // Keep the Harmony instance so Install/Uninstall can retry clearing the
                        // owner-scoped patches instead of reporting a complete rollback.
                        _harmony = harmony;
                        _provider = null;
                        _mutator = null;
                        _gameAssembly = null;
                        _cleanupPending = true;
                        return Failed("Harmony hook installation failed and rollback is pending. Install error: " +
                            exception.Message + "; cleanup error: " + rollbackFailure.Message);
                    }

                    return Failed("Harmony hook installation failed and was rolled back: " + exception.Message);
                }
            }
        }

        public static bool Uninstall(IRuntimeContentProvider expectedProvider, out string message)
        {
            lock (Gate)
            {
                if (_harmony == null)
                {
                    message = "Runtime hooks are not installed.";
                    return true;
                }

                if (!_cleanupPending && (expectedProvider == null ||
                    !object.ReferenceEquals(_provider, expectedProvider)))
                {
                    message = "Runtime hooks are bound to a different provider; they were left installed.";
                    return false;
                }

                try
                {
                    _harmony.UnpatchSelf();
                    RuntimeHooks.Clear(_mutator);
                    ClearInstallationState();
                    message = "Runtime hooks removed.";
                    return true;
                }
                catch (Exception exception)
                {
                    // A failed unpatch can leave Harmony callbacks installed. Detach their
                    // provider immediately so any surviving hook falls through to game behavior;
                    // retain only the owner-scoped Harmony state so a later Uninstall/Install can
                    // retry cleanup without keeping a destroyed provider alive.
                    RuntimeHooks.Clear(_mutator);
                    _provider = null;
                    _mutator = null;
                    _gameAssembly = null;
                    _cleanupPending = true;
                    message = "Could not remove runtime hooks: " + exception.Message;
                    return false;
                }
            }
        }

        private static IList<PatchPlan> ResolvePatchPlan(Assembly gameAssembly)
        {
            Type entityStore = RuntimeReflection.FindUniqueType(gameAssembly, "EntityBalancingStore");
            Type gameBalancingStore = RuntimeReflection.FindUniqueType(gameAssembly, "GameBalancingStore");
            Type gameBalancing = RuntimeReflection.FindUniqueType(gameAssembly,
                "GameBalancingScriptableObject");
            Type levelProgression = RuntimeReflection.FindUniqueType(gameAssembly, "LevelProgression");
            // CreateLevelMap receives a Stage entry. Its stageParameters field points to the
            // StageParametersScriptableObject loaded from a content bundle.
            Type stage = RuntimeReflection.FindUniqueType(gameAssembly, "Stage");
            Type stageMap = RuntimeReflection.FindUniqueType(gameAssembly, "StageMap");
            Type entityFactory = RuntimeReflection.FindUniqueType(gameAssembly, "EntityFactory");
            Type aiBehaviour = RuntimeReflection.FindUniqueType(gameAssembly, "AiBehaviour");
            Type defaultAiBehaviour = RuntimeReflection.FindUniqueType(gameAssembly, "DefaultAiBehaviour");
            Type loca = RuntimeReflection.FindUniqueType(gameAssembly, "Loca");
            Type blueprintPool = RuntimeReflection.FindUniqueType(gameAssembly, "BlueprintPool");
            Type chooseCard = RuntimeReflection.FindUniqueType(gameAssembly, "ChooseCard");
            Type presentCardAsRewardEvent = RuntimeReflection.FindUniqueType(
                gameAssembly, "PresentCardAsRewardEvent");
            Type entityAttack = RuntimeReflection.FindUniqueType(gameAssembly, "EntityAttack");
            Type entityController = RuntimeReflection.FindUniqueType(gameAssembly, "EntityController");
            Type existingControllers = RuntimeReflection.FindUniqueType(gameAssembly, "ExistingControllers");

            MethodInfo init = RuntimeReflection.FindUniqueMethod(entityStore, "Init",
                delegate(MethodInfo method) { return method.GetParameters().Length == 0; });
            MethodInfo setScriptableObject = RuntimeReflection.FindUniqueMethod(gameBalancingStore,
                "SetScriptableObject", delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == gameBalancing;
                });
            MethodInfo createStageMap = RuntimeReflection.FindUniqueMethod(levelProgression,
                "CreateStageMap", delegate(MethodInfo method)
                {
                    return method.GetParameters().Length == 0;
                });
            MethodInfo stageMapToJson = RuntimeReflection.FindUniqueMethod(stageMap,
                "ToJson", HasStageMapToJsonShape);
            ConstructorInfo stageMapJsonConstructor = FindStageMapJsonConstructor(stageMap);
            MethodInfo createLevelMap = RuntimeReflection.FindUniqueMethod(levelProgression,
                "CreateLevelMap", delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 4 && parameters[0].ParameterType == stage &&
                        parameters[1].ParameterType == typeof(int) &&
                        parameters[2].ParameterType == typeof(bool) &&
                        !parameters[3].ParameterType.IsByRef && !parameters[3].ParameterType.IsPointer;
                });
            MethodInfo instantiateEntity = RuntimeReflection.FindUniqueMethod(entityFactory,
                "InstantiateEntity", HasInstantiateEntityShape);
            MethodInfo createEntityMesh = RuntimeReflection.FindUniqueMethod(entityFactory,
                "CreateEntityMesh", HasCreateEntityMeshShape);
            MethodInfo setupPrespawning = RuntimeReflection.FindUniqueMethod(entityFactory, "SetupPrespawning",
                delegate(MethodInfo method) { return method.GetParameters().Length == 0 && method.ReturnType == typeof(void); });
            MethodInfo setupAiPrespawning = RuntimeReflection.FindUniqueMethod(entityFactory, "SetupAiPrespawning",
                delegate(MethodInfo method) { return method.GetParameters().Length == 0 && method.ReturnType == typeof(void); });
            MethodInfo setupPrespawningPostfix = Hook("EntityFactorySetupPrespawningPostfix");
            MethodInfo setupAiPrespawningPostfix = Hook("EntityFactorySetupAiPrespawningPostfix");
            MethodInfo aiUpdate = RuntimeReflection.FindUniqueMethod(aiBehaviour, "Update",
                delegate(MethodInfo method)
                {
                    return method.GetParameters().Length == 0 && method.ReturnType == typeof(void);
                });
            MethodInfo aiInit = RuntimeReflection.FindUniqueMethod(defaultAiBehaviour, "Init",
                delegate(MethodInfo method)
                {
                    return method.GetParameters().Length == 0 && method.ReturnType == typeof(void);
                });
            MethodInfo aiDestroy = RuntimeReflection.FindUniqueMethod(defaultAiBehaviour, "OnDestroy",
                delegate(MethodInfo method)
                {
                    return method.GetParameters().Length == 0 && method.ReturnType == typeof(void);
                });
            MethodInfo blueprintName = RuntimeReflection.FindUniqueMethod(loca, "BlueprintName",
                HasSingleStringArgumentReturningString);
            MethodInfo blueprintDescription = RuntimeReflection.FindUniqueMethod(loca, "BlueprintDescription",
                HasSingleStringArgumentReturningString);
            MethodInfo skillDescription = RuntimeReflection.FindUniqueMethod(loca, "SkillDescription",
                HasSingleStringArgumentReturningString);
            MethodInfo relicName = RuntimeReflection.FindUniqueMethod(loca, "RelicName",
                HasSingleStringArgumentReturningString);
            MethodInfo relicDescription = RuntimeReflection.FindUniqueMethod(loca, "RelicDescription",
                HasSingleStringArgumentReturningString);
            MethodInfo upgradeName = RuntimeReflection.FindUniqueMethod(loca, "UpgradeName",
                HasSingleStringArgumentReturningString);
            MethodInfo upgradeDescription = RuntimeReflection.FindUniqueMethod(loca, "UpgradeDescription",
                HasSingleStringArgumentReturningString);
            MethodInfo globalLocalization = RuntimeReflection.FindUniqueMethod(loca, "Global",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.IsStatic && method.ReturnType == typeof(string) && parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(string[]);
                });
            MethodInfo fallbackLandscapes = RuntimeReflection.FindUniqueMethod(gameBalancingStore,
                "get_FallbackLandscapeGenerators", delegate(MethodInfo method)
                {
                    return method.IsStatic && method.GetParameters().Length == 0 &&
                        method.ReturnType == typeof(List<global::LandscapeGenerator>);
                });
            MethodInfo outlyingStartAiBuildingDistance = RuntimeReflection.FindUniqueMethod(
                gameBalancingStore, "MinDistanceOfOutlyingStartAiBuildingsToPlayerStart",
                delegate(MethodInfo method)
                {
                    return method.IsStatic && method.GetParameters().Length == 0 &&
                        method.ReturnType == typeof(int);
                });
            MethodInfo blueprintPoolEligibility = RuntimeReflection.FindUniqueMethod(
                blueprintPool, "IsInPool", HasBlueprintPoolEligibilityShape);
            MethodInfo blueprintPoolFind = RuntimeReflection.FindUniqueMethod(
                blueprintPool, "FindBlueprintPool", HasBlueprintPoolFindShape);
            MethodInfo blueprintPoolExclusive = RuntimeReflection.FindUniqueMethod(
                blueprintPool, "ExclusiveBlueprints", HasBlueprintPoolExclusiveShape);
            MethodInfo presentCardsToChoose = RuntimeReflection.FindUniqueMethod(
                chooseCard, "PresentCardsToChoose", HasPresentCardsToChooseShape);
            MethodInfo instantiateCardsWithDelay = RuntimeReflection.FindUniqueMethod(
                chooseCard, "InstantiateCardsWithDelay", HasInstantiateCardsWithDelayShape);
            MethodInfo blueprintPoolCandidates = RuntimeReflection.FindUniqueMethod(
                blueprintPool, "BlueprintsInPool", HasBlueprintPoolCandidatesShape);
            MethodInfo presentCardAsRewardRecord = RuntimeReflection.FindUniqueMethod(
                presentCardAsRewardEvent, "Record", HasPresentCardAsRewardRecordShape);
            MethodInfo allAllowedBlueprints = RuntimeReflection.FindUniqueMethod(
                entityStore, "AllEntityIdsAllowedAsBlueprints", HasGlobalBlueprintRewardFallbackShape);
            MethodInfo resourcesLoadTyped = FindResourcesLoadTyped();
            MethodInfo resourcesLoadUntyped = FindResourcesLoadUntyped();

            MethodInfo entityInitPostfix = Hook("EntityBalancingStoreInitPostfix");
            MethodInfo setScriptableObjectPostfix = Hook("SetScriptableObjectPostfix");
            MethodInfo stageMapPrefix = Hook("CreateStageMapPrefix");
            MethodInfo stageMapPostfix = Hook("CreateStageMapPostfix");
            MethodInfo stageMapJsonConstructorPostfix = Hook("StageMapJsonConstructorPostfix");
            MethodInfo stageMapToJsonPostfix = Hook("StageMapToJsonPostfix");
            MethodInfo levelMapPrefix = Hook("CreateLevelMapPrefix");
            MethodInfo levelMapFinalizer = Hook("CreateLevelMapFinalizer");
            MethodInfo instantiatePrefix = Hook("InstantiateEntityPrefix");
            MethodInfo instantiatePostfix = Hook("InstantiateEntityPostfix");
            MethodInfo entityMeshPrefix = Hook("CreateEntityMeshPrefix");
            MethodInfo aiUpdatePostfix = Hook("AiBehaviourUpdatePostfix");
            MethodInfo aiInitPostfix = Hook("DefaultAiBehaviourInitPostfix");
            MethodInfo aiDestroyPostfix = Hook("AiBehaviourDestroyPostfix");
            MethodInfo blueprintNamePrefix = Hook("BlueprintNamePrefix");
            MethodInfo blueprintDescriptionPrefix = Hook("BlueprintDescriptionPrefix");
            MethodInfo skillDescriptionPrefix = Hook("SkillDescriptionPrefix");
            MethodInfo relicNamePrefix = Hook("RelicNamePrefix");
            MethodInfo relicDescriptionPrefix = Hook("RelicDescriptionPrefix");
            MethodInfo upgradeNamePrefix = Hook("UpgradeNamePrefix");
            MethodInfo upgradeDescriptionPrefix = Hook("UpgradeDescriptionPrefix");
            MethodInfo globalLocalizationPrefix = Hook("LandscapeLocalizationPrefix");
            MethodInfo globalLocalizationFinalizer = Hook("LandscapeLocalizationFinalizer");
            MethodInfo fallbackLandscapesPostfix = Hook("FallbackLandscapeGeneratorsPostfix");
            MethodInfo outlyingStartAiBuildingDistancePrefix = Hook("OutlyingStartAiBuildingDistancePrefix");
            MethodInfo blueprintPoolEligibilityPrefix = Hook("BlueprintPoolIsInPoolPrefix");
            MethodInfo blueprintPoolFindPrefix = Hook("BlueprintPoolFindPrefix");
            MethodInfo blueprintPoolExclusivePostfix = Hook("BlueprintPoolExclusiveBlueprintsPostfix");
            MethodInfo presentCardsToChoosePrefix = Hook("PresentCardsToChoosePrefix");
            MethodInfo presentCardsToChooseFinalizer = Hook("PresentCardsToChooseFinalizer");
            MethodInfo instantiateCardsWithDelayPrefix = Hook("InstantiateCardsWithDelayPrefix");
            MethodInfo blueprintPoolCandidatesPostfix = Hook("BlueprintPoolBlueprintsInPoolPostfix");
            MethodInfo presentCardAsRewardRecordPostfix = Hook("PresentCardAsRewardRecordPostfix");
            MethodInfo allAllowedBlueprintsPostfix = Hook("AllEntityIdsAllowedAsBlueprintsPostfix");
            MethodInfo isTargetInRange = RuntimeReflection.FindUniqueMethod(entityAttack, "IsTargetInRange",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == entityController && method.ReturnType == typeof(bool);
                });
            MethodInfo isTargetInRangePostfix = Hook("EntityAttackIsTargetInRangePostfix");
            MethodInfo entityDestroy = RuntimeReflection.FindUniqueMethod(entityController, "Destroy",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.ReturnType == typeof(void) && parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(bool) && parameters[1].ParameterType == entityController;
                });
            MethodInfo entityDestroyPostfix = Hook("EntityControllerDestroyPostfix");
            MethodInfo entityFixedUpdate = RuntimeReflection.FindUniqueMethod(entityController, "FixedUpdate",
                delegate(MethodInfo method)
                {
                    return method.ReturnType == typeof(void) && method.GetParameters().Length == 0;
                });
            MethodInfo entityFixedUpdatePrefix = Hook("EntityControllerFixedUpdatePrefix");
            MethodInfo entityFixedUpdatePostfix = Hook("EntityControllerFixedUpdatePostfix");
            MethodInfo isProductionPossible = RuntimeReflection.FindUniqueMethod(entityController,
                "IsProductionPossible", delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.ReturnType == typeof(bool) && parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(int);
                });
            MethodInfo isProductionPossiblePrefix = Hook("EntityControllerIsProductionPossiblePrefix");
            MethodInfo takeDamage = RuntimeReflection.FindUniqueMethod(entityController, "TakeDamage",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 4 && parameters[0].ParameterType == typeof(float) && parameters[1].ParameterType == entityController;
                });
            MethodInfo takeDamagePostfix = Hook("EntityTakeDamagePostfix");

            // Optional gameplay hooks: resolved defensively so a missing target never
            // disables the rest of the patch plan. These extend gameplay (death,
            // healing, armor repair) without breaking installation on builds where
            // the native method shape differs.
            MethodInfo entityDie = TryFindUniqueMethod(entityController, "Die",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.ReturnType == typeof(void) && parameters.Length <= 2;
                });
            MethodInfo entityDiePostfix = entityDie == null ? null : Hook("EntityControllerDiePostfix");
            MethodInfo entityHeal = TryFindUniqueMethod(entityController, "Heal",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length >= 1 && parameters[0].ParameterType == typeof(float);
                });
            MethodInfo entityHealPostfix = entityHeal == null ? null : Hook("EntityControllerHealPostfix");
            MethodInfo entityRepairArmor = TryFindUniqueMethod(entityController, "RepairArmor",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length >= 1 && parameters[0].ParameterType == typeof(float);
                });
            MethodInfo entityRepairArmorPostfix = entityRepairArmor == null ? null : Hook("EntityControllerRepairArmorPostfix");

            // Core Pillar 1: Unit Creation, Pooling/Reuse, Placement Ghosts & Factory Production
            MethodInfo createBuildingPlacementGhost = TryFindUniqueMethod(entityFactory, "CreateBuildingPlacementGhost",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.IsStatic && parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(string) &&
                        parameters[1].ParameterType == typeof(bool) &&
                        method.ReturnType.FullName == "UnityEngine.GameObject";
                });
            MethodInfo createBuildingPlacementGhostPrefix = createBuildingPlacementGhost == null ? null : Hook("CreateBuildingPlacementGhostPrefix");
            MethodInfo createBuildingPlacementGhostPostfix = createBuildingPlacementGhost == null ? null : Hook("CreateBuildingPlacementGhostPostfix");

            MethodInfo initEntityController = TryFindUniqueMethod(entityFactory, "InitEntityController",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.IsStatic && parameters.Length == 2 &&
                        parameters[0].ParameterType.FullName == "UnityEngine.GameObject" &&
                        parameters[1].ParameterType == entityController &&
                        method.ReturnType == entityController;
                });
            MethodInfo initEntityControllerPrefix = initEntityController == null ? null : Hook("InitEntityControllerPrefix");
            MethodInfo initEntityControllerPostfix = initEntityController == null ? null : Hook("InitEntityControllerPostfix");

            MethodInfo entityResetAndInitForReuse = TryFindUniqueMethod(entityController, "ResetAndInitForReuse",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return !method.IsStatic && method.ReturnType == typeof(void) && parameters.Length == 5 &&
                        parameters[0].ParameterType == typeof(Transform) &&
                        parameters[1].ParameterType == entityController &&
                        parameters[2].ParameterType.FullName == "UnityEngine.Vector3" &&
                        parameters[3].ParameterType == typeof(string) &&
                        parameters[4].ParameterType == typeof(string);
                });
            MethodInfo entityResetAndInitForReusePrefix = entityResetAndInitForReuse == null ? null : Hook("EntityControllerResetAndInitForReusePrefix");

            MethodInfo entityControllerInit = TryFindUniqueMethod(entityController, "Init",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return !method.IsStatic && method.ReturnType == typeof(void) && parameters.Length == 1 &&
                        parameters[0].ParameterType == entityController;
                });

            MethodInfo entityOnInstantiated = TryFindUniqueMethod(entityController, "OnHasBeenInstantiated",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return !method.IsStatic && method.ReturnType == typeof(void) && parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(bool);
                });
            MethodInfo entityOnInstantiatedForReuse = TryFindUniqueMethod(entityController, "OnHasBeenInstantiatedForReuse",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return !method.IsStatic && method.ReturnType == typeof(void) && parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(bool);
                });
            MethodInfo entityOnInstantiatedForReusePostfix = entityOnInstantiatedForReuse == null ? null : Hook("EntityControllerOnHasBeenInstantiatedForReusePostfix");

            MethodInfo entityProduce = TryFindUniqueMethod(entityController, "Produce",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return !method.IsStatic && method.ReturnType == typeof(void) && parameters.Length == 3 &&
                        parameters[0].ParameterType == typeof(bool) &&
                        parameters[1].ParameterType == typeof(bool) &&
                        parameters[2].ParameterType == typeof(bool);
                });
            MethodInfo entityProducePrefix = entityProduce == null ? null : Hook("EntityControllerProducePrefix");
            MethodInfo entityProducePostfix = entityProduce == null ? null : Hook("EntityControllerProducePostfix");

            // Core Pillar 2: Unit Registration & Battlefield Lifecycle (ExistingControllers)
            MethodInfo existingControllersInit = TryFindUniqueMethod(existingControllers, "Init",
                delegate(MethodInfo method)
                {
                    return !method.IsStatic && method.ReturnType == typeof(void) && method.GetParameters().Length == 0;
                });
            MethodInfo existingControllersInitPostfix = existingControllersInit == null ? null : Hook("ExistingControllersInitPostfix");

            MethodInfo existingControllersOnEntityCreated = TryFindUniqueMethod(existingControllers, "OnEntityHasBeenCreated",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return !method.IsStatic && method.ReturnType == typeof(void) && parameters.Length == 2 &&
                        parameters[0].ParameterType == entityController &&
                        parameters[1].ParameterType == typeof(bool);
                });
            MethodInfo existingControllersOnEntityCreatedPostfix = existingControllersOnEntityCreated == null ? null : Hook("ExistingControllersOnEntityCreatedPostfix");

            MethodInfo existingControllersOnEntityWillBeDestroyed = TryFindUniqueMethod(existingControllers, "OnEntityWillBeDestroyed",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return !method.IsStatic && method.ReturnType == typeof(void) && parameters.Length == 2 &&
                        parameters[0].ParameterType == entityController &&
                        parameters[1].ParameterType == entityController;
                });
            MethodInfo existingControllersOnEntityWillBeDestroyedPostfix = existingControllersOnEntityWillBeDestroyed == null ? null : Hook("ExistingControllersOnEntityWillBeDestroyedPostfix");

            // Core Pillar 3: Command Loop (ExecuteCommand, ExecuteNextCommand, ResetCurrentCommand, DeleteCommandChain)
            MethodInfo entityExecuteCommand = TryFindUniqueMethod(entityController, "ExecuteCommand",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return !method.IsStatic && method.ReturnType == typeof(void) && parameters.Length == 2 &&
                        RuntimeReflection.HasSimpleName(parameters[0].ParameterType, "EntityCommand") &&
                        parameters[1].ParameterType.IsEnum;
                });
            MethodInfo entityExecuteCommandPostfix = entityExecuteCommand == null ? null : Hook("EntityControllerExecuteCommandPostfix");

            MethodInfo entityExecuteNextCommand = TryFindUniqueMethod(entityController, "ExecuteNextCommand",
                delegate(MethodInfo method)
                {
                    return !method.IsStatic && method.ReturnType == typeof(void) && method.GetParameters().Length == 0;
                });
            MethodInfo entityExecuteNextCommandPostfix = entityExecuteNextCommand == null ? null : Hook("EntityControllerExecuteNextCommandPostfix");

            MethodInfo entityResetCurrentCommand = TryFindUniqueMethod(entityController, "ResetCurrentCommand",
                delegate(MethodInfo method)
                {
                    return !method.IsStatic && method.ReturnType == typeof(void) && method.GetParameters().Length == 0;
                });
            MethodInfo entityResetCurrentCommandPostfix = entityResetCurrentCommand == null ? null : Hook("EntityControllerResetCurrentCommandPostfix");

            MethodInfo entityDeleteCommandChain = TryFindUniqueMethod(entityController, "DeleteCommandChain",
                delegate(MethodInfo method)
                {
                    return !method.IsStatic && method.ReturnType == typeof(void) && method.GetParameters().Length == 0;
                });
            MethodInfo entityDeleteCommandChainPostfix = entityDeleteCommandChain == null ? null : Hook("EntityControllerDeleteCommandChainPostfix");
            MethodInfo loadPrefix = typeof(UnityResourcesLoadTypedPatch).GetMethod("Prefix",
                BindingFlags.Public | BindingFlags.Static);
            if (loadPrefix == null)
            {
                throw new InvalidOperationException("Unity Resources.Load(string, Type) prefix method was not found.");
            }
            MethodInfo untypedLoadPrefix = typeof(UnityResourcesLoadUntypedPatch).GetMethod("Prefix",
                BindingFlags.Public | BindingFlags.Static);
            if (untypedLoadPrefix == null)
            {
                throw new InvalidOperationException("Unity Resources.Load(string) prefix method was not found.");
            }

            MethodInfo instantiateTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityFactoryInstantiateEntityTranspiler));
            MethodInfo initEntityControllerTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityFactoryInitEntityControllerTranspiler));
            MethodInfo entityOnInstantiatedTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityControllerOnHasBeenInstantiatedTranspiler));
            MethodInfo entityResetTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityControllerResetAndInitForReuseTranspiler));
            MethodInfo entityOnInstantiatedForReuseTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityControllerOnHasBeenInstantiatedForReuseTranspiler));
            MethodInfo entityProduceTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityControllerProduceTranspiler));
            MethodInfo existingControllersInitTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.ExistingControllersInitTranspiler));
            MethodInfo existingControllersCreatedTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.ExistingControllersOnEntityHasBeenCreatedTranspiler));
            MethodInfo existingControllersDestroyedTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.ExistingControllersOnEntityWillBeDestroyedTranspiler));
            MethodInfo createBuildingPlacementGhostTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityFactoryCreateBuildingPlacementGhostTranspiler));
            MethodInfo entityInitTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityControllerInitTranspiler));
            MethodInfo entityDestroyTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityControllerDestroyTranspiler));
            MethodInfo entityExecuteCommandTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityControllerExecuteCommandTranspiler));
            MethodInfo entityExecuteNextCommandTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityControllerExecuteNextCommandTranspiler));
            MethodInfo entityResetCurrentCommandTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityControllerResetCurrentCommandTranspiler));
            MethodInfo entityDeleteCommandChainTranspiler = TranspilerHook(nameof(RuntimeTranspilerPatches.EntityControllerDeleteCommandChainTranspiler));

            List<PatchPlan> plan = new List<PatchPlan>();
            plan.Add(new PatchPlan(init, null, entityInitPostfix));
            plan.Add(new PatchPlan(setScriptableObject, null, setScriptableObjectPostfix));
            plan.Add(new PatchPlan(createStageMap, stageMapPrefix, stageMapPostfix));
            plan.Add(new PatchPlan(stageMapJsonConstructor, null, stageMapJsonConstructorPostfix));
            plan.Add(new PatchPlan(stageMapToJson, null, stageMapToJsonPostfix));
            plan.Add(new PatchPlan(createLevelMap, levelMapPrefix, null, levelMapFinalizer));
            plan.Add(new PatchPlan(instantiateEntity, instantiatePrefix, instantiatePostfix, instantiateTranspiler, null));
            plan.Add(new PatchPlan(createEntityMesh, entityMeshPrefix, null));
            plan.Add(new PatchPlan(aiUpdate, null, aiUpdatePostfix));
            plan.Add(new PatchPlan(aiInit, null, aiInitPostfix));
            plan.Add(new PatchPlan(aiDestroy, null, aiDestroyPostfix));
            plan.Add(new PatchPlan(blueprintName, blueprintNamePrefix, null));
            plan.Add(new PatchPlan(blueprintDescription, blueprintDescriptionPrefix, null));
            plan.Add(new PatchPlan(skillDescription, skillDescriptionPrefix, null));
            plan.Add(new PatchPlan(relicName, relicNamePrefix, null));
            plan.Add(new PatchPlan(relicDescription, relicDescriptionPrefix, null));
            plan.Add(new PatchPlan(upgradeName, upgradeNamePrefix, null));
            plan.Add(new PatchPlan(upgradeDescription, upgradeDescriptionPrefix, null));
            plan.Add(new PatchPlan(globalLocalization, globalLocalizationPrefix, null, globalLocalizationFinalizer));
            plan.Add(new PatchPlan(fallbackLandscapes, null, fallbackLandscapesPostfix));
            plan.Add(new PatchPlan(outlyingStartAiBuildingDistance,
                outlyingStartAiBuildingDistancePrefix, null));
            plan.Add(new PatchPlan(blueprintPoolEligibility, blueprintPoolEligibilityPrefix, null));
            plan.Add(new PatchPlan(blueprintPoolFind, blueprintPoolFindPrefix, null));
            plan.Add(new PatchPlan(blueprintPoolExclusive, null, blueprintPoolExclusivePostfix));
            plan.Add(new PatchPlan(presentCardsToChoose, presentCardsToChoosePrefix, null,
                presentCardsToChooseFinalizer));
            plan.Add(new PatchPlan(instantiateCardsWithDelay, instantiateCardsWithDelayPrefix, null));
            plan.Add(new PatchPlan(blueprintPoolCandidates, null, blueprintPoolCandidatesPostfix));
            plan.Add(new PatchPlan(presentCardAsRewardRecord, null, presentCardAsRewardRecordPostfix));
            plan.Add(new PatchPlan(allAllowedBlueprints, null, allAllowedBlueprintsPostfix));
            plan.Add(new PatchPlan(resourcesLoadTyped, loadPrefix, null));
            plan.Add(new PatchPlan(resourcesLoadUntyped, untypedLoadPrefix, null));
            plan.Add(new PatchPlan(setupPrespawning, null, setupPrespawningPostfix));
            plan.Add(new PatchPlan(setupAiPrespawning, null, setupAiPrespawningPostfix));
            plan.Add(new PatchPlan(isTargetInRange, null, isTargetInRangePostfix));
            plan.Add(new PatchPlan(entityDestroy, null, entityDestroyPostfix, entityDestroyTranspiler, null));
            plan.Add(new PatchPlan(entityFixedUpdate, entityFixedUpdatePrefix, entityFixedUpdatePostfix));
            plan.Add(new PatchPlan(isProductionPossible, isProductionPossiblePrefix, null));
            plan.Add(new PatchPlan(takeDamage, null, takeDamagePostfix));

            // Optional gameplay hooks (resolved defensively): death, healing, and armor
            // repair. These are additive and never disable the rest of the plan when
            // the native method shape differs.
            if (entityDie != null && entityDiePostfix != null)
            {
                plan.Add(new PatchPlan(entityDie, null, entityDiePostfix));
            }
            if (entityHeal != null && entityHealPostfix != null)
            {
                plan.Add(new PatchPlan(entityHeal, null, entityHealPostfix));
            }
            if (entityRepairArmor != null && entityRepairArmorPostfix != null)
            {
                plan.Add(new PatchPlan(entityRepairArmor, null, entityRepairArmorPostfix));
            }
            if (createBuildingPlacementGhost != null && (createBuildingPlacementGhostPrefix != null || createBuildingPlacementGhostPostfix != null || createBuildingPlacementGhostTranspiler != null))
            {
                plan.Add(new PatchPlan(createBuildingPlacementGhost, createBuildingPlacementGhostPrefix, createBuildingPlacementGhostPostfix, createBuildingPlacementGhostTranspiler, null));
            }
            if (initEntityController != null && (initEntityControllerPrefix != null || initEntityControllerPostfix != null || initEntityControllerTranspiler != null))
            {
                plan.Add(new PatchPlan(initEntityController, initEntityControllerPrefix, initEntityControllerPostfix, initEntityControllerTranspiler, null));
            }
            if (entityControllerInit != null && entityInitTranspiler != null)
            {
                plan.Add(new PatchPlan(entityControllerInit, null, null, entityInitTranspiler, null));
            }
            if (entityOnInstantiated != null && entityOnInstantiatedTranspiler != null)
            {
                plan.Add(new PatchPlan(entityOnInstantiated, null, null, entityOnInstantiatedTranspiler, null));
            }
            if (entityResetAndInitForReuse != null && entityResetAndInitForReusePrefix != null)
            {
                plan.Add(new PatchPlan(entityResetAndInitForReuse, entityResetAndInitForReusePrefix, null, entityResetTranspiler, null));
            }
            if (entityOnInstantiatedForReuse != null && (entityOnInstantiatedForReusePostfix != null || entityOnInstantiatedForReuseTranspiler != null))
            {
                plan.Add(new PatchPlan(entityOnInstantiatedForReuse, null, entityOnInstantiatedForReusePostfix, entityOnInstantiatedForReuseTranspiler, null));
            }
            if (entityProduce != null && (entityProducePrefix != null || entityProducePostfix != null || entityProduceTranspiler != null))
            {
                plan.Add(new PatchPlan(entityProduce, entityProducePrefix, entityProducePostfix, entityProduceTranspiler, null));
            }
            if (existingControllersInit != null && (existingControllersInitPostfix != null || existingControllersInitTranspiler != null))
            {
                plan.Add(new PatchPlan(existingControllersInit, null, existingControllersInitPostfix, existingControllersInitTranspiler, null));
            }
            if (existingControllersOnEntityCreated != null && existingControllersOnEntityCreatedPostfix != null)
            {
                plan.Add(new PatchPlan(existingControllersOnEntityCreated, null, existingControllersOnEntityCreatedPostfix, existingControllersCreatedTranspiler, null));
            }
            if (existingControllersOnEntityWillBeDestroyed != null && existingControllersOnEntityWillBeDestroyedPostfix != null)
            {
                plan.Add(new PatchPlan(existingControllersOnEntityWillBeDestroyed, null, existingControllersOnEntityWillBeDestroyedPostfix, existingControllersDestroyedTranspiler, null));
            }
            if (entityExecuteCommand != null && (entityExecuteCommandPostfix != null || entityExecuteCommandTranspiler != null))
            {
                plan.Add(new PatchPlan(entityExecuteCommand, null, entityExecuteCommandPostfix, entityExecuteCommandTranspiler, null));
            }
            if (entityExecuteNextCommand != null && (entityExecuteNextCommandPostfix != null || entityExecuteNextCommandTranspiler != null))
            {
                plan.Add(new PatchPlan(entityExecuteNextCommand, null, entityExecuteNextCommandPostfix, entityExecuteNextCommandTranspiler, null));
            }
            if (entityResetCurrentCommand != null && (entityResetCurrentCommandPostfix != null || entityResetCurrentCommandTranspiler != null))
            {
                plan.Add(new PatchPlan(entityResetCurrentCommand, null, entityResetCurrentCommandPostfix, entityResetCurrentCommandTranspiler, null));
            }
            if (entityDeleteCommandChain != null && (entityDeleteCommandChainPostfix != null || entityDeleteCommandChainTranspiler != null))
            {
                plan.Add(new PatchPlan(entityDeleteCommandChain, null, entityDeleteCommandChainPostfix, entityDeleteCommandChainTranspiler, null));
            }
            return plan;
        }

        private static bool HasSingleStringArgumentReturningString(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.IsStatic && method.ReturnType == typeof(string) && parameters.Length == 1 &&
                parameters[0].ParameterType == typeof(string);
        }

        private static bool HasStageMapToJsonShape(MethodInfo method)
        {
            return !method.IsStatic && method.ReturnType == typeof(string) &&
                method.GetParameters().Length == 0;
        }

        private static ConstructorInfo FindStageMapJsonConstructor(Type stageMapType)
        {
            ConstructorInfo[] constructors = stageMapType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            List<ConstructorInfo> matches = new List<ConstructorInfo>();
            for (int i = 0; i < constructors.Length; i++)
            {
                ParameterInfo[] parameters = constructors[i].GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                {
                    matches.Add(constructors[i]);
                }
            }

            if (matches.Count != 1)
            {
                throw new InvalidOperationException("Expected one StageMap(string) constructor, found " +
                    matches.Count + ".");
            }

            return matches[0];
        }

        private static bool HasBlueprintPoolEligibilityShape(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return !method.IsStatic && method.ReturnType == typeof(bool) && parameters.Length == 1 &&
                parameters[0].ParameterType == typeof(string);
        }

        private static bool HasBlueprintPoolFindShape(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.IsStatic && parameters.Length == 1 &&
                parameters[0].ParameterType == typeof(string) &&
                RuntimeReflection.HasSimpleName(method.ReturnType, "BlueprintPool");
        }

        private static bool HasBlueprintPoolExclusiveShape(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.IsStatic && parameters.Length == 1 &&
                parameters[0].ParameterType == typeof(string) &&
                method.ReturnType == typeof(HashSet<string>);
        }

        private static bool HasPresentCardsToChooseShape(MethodInfo method)
        {
            return !method.IsStatic && method.ReturnType == typeof(void) &&
                method.GetParameters().Length == 0;
        }

        private static bool HasInstantiateCardsWithDelayShape(MethodInfo method)
        {
            if (method.IsStatic || method.ReturnType != typeof(System.Collections.IEnumerator))
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType == typeof(List<string>);
        }

        private static bool HasBlueprintPoolCandidatesShape(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return !method.IsStatic && method.ReturnType == typeof(List<string>) &&
                parameters.Length == 2 && parameters[0].ParameterType == typeof(int?) &&
                parameters[1].ParameterType == typeof(bool);
        }

        private static bool HasPresentCardAsRewardRecordShape(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.IsStatic && method.ReturnType == typeof(void) && parameters.Length == 7 &&
                parameters[0].ParameterType == typeof(int) &&
                parameters[1].ParameterType == typeof(int) &&
                parameters[2].ParameterType == typeof(int) &&
                parameters[3].ParameterType == typeof(string) &&
                parameters[4].ParameterType == typeof(string) &&
                parameters[5].ParameterType == typeof(int) &&
                parameters[6].ParameterType == typeof(int);
        }

        private static bool HasGlobalBlueprintRewardFallbackShape(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.IsStatic && method.ReturnType == typeof(List<string>) &&
                parameters.Length == 6 &&
                parameters[0].ParameterType.IsEnum &&
                parameters[1].ParameterType == typeof(bool?) &&
                parameters[2].ParameterType == typeof(bool) &&
                parameters[3].ParameterType.IsEnum &&
                parameters[4].ParameterType == typeof(int?) &&
                parameters[5].ParameterType == typeof(int?);
        }

        private static bool HasInstantiateEntityShape(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 9 &&
                parameters[0].ParameterType == typeof(string) &&
                parameters[1].ParameterType.FullName == "UnityEngine.Vector3" &&
                RuntimeReflection.HasSimpleName(parameters[2].ParameterType, "EntityController") &&
                parameters[3].ParameterType == typeof(string) &&
                parameters[4].ParameterType == typeof(string) &&
                parameters[5].ParameterType == typeof(Transform) &&
                RuntimeReflection.HasSimpleName(parameters[6].ParameterType, "UnitRole") &&
                parameters[7].ParameterType == typeof(bool) &&
                parameters[8].ParameterType == typeof(string) &&
                RuntimeReflection.HasSimpleName(method.ReturnType, "EntityController");
        }

        private static bool HasCreateEntityMeshShape(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 3 &&
                parameters[0].ParameterType == typeof(string) &&
                parameters[1].ParameterType == typeof(Transform) &&
                parameters[2].ParameterType == typeof(float) &&
                method.ReturnType.FullName == "UnityEngine.GameObject";
        }

        private static MethodInfo FindResourcesLoadTyped()
        {
            MethodInfo[] methods = typeof(Resources).GetMethods(BindingFlags.Public | BindingFlags.Static);
            List<MethodInfo> matches = new List<MethodInfo>();
            for (int i = 0; i < methods.Length; i++)
            {
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (String.Equals(methods[i].Name, "Load", StringComparison.Ordinal) &&
                    parameters.Length == 2 && parameters[0].ParameterType == typeof(string) &&
                    parameters[1].ParameterType == typeof(Type) &&
                    methods[i].ReturnType == typeof(UnityEngine.Object))
                {
                    matches.Add(methods[i]);
                }
            }

            if (matches.Count != 1)
            {
                throw new InvalidOperationException("Expected one UnityEngine.Resources.Load(string, Type) " +
                    "returning UnityEngine.Object, found " + matches.Count + ".");
            }

            return matches[0];
        }

        private static MethodInfo FindResourcesLoadUntyped()
        {
            MethodInfo[] methods = typeof(Resources).GetMethods(BindingFlags.Public | BindingFlags.Static);
            List<MethodInfo> matches = new List<MethodInfo>();
            for (int i = 0; i < methods.Length; i++)
            {
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (String.Equals(methods[i].Name, "Load", StringComparison.Ordinal) &&
                    !methods[i].IsGenericMethodDefinition && parameters.Length == 1 &&
                    parameters[0].ParameterType == typeof(string) &&
                    methods[i].ReturnType == typeof(UnityEngine.Object))
                {
                    matches.Add(methods[i]);
                }
            }

            if (matches.Count != 1)
            {
                throw new InvalidOperationException("Expected one UnityEngine.Resources.Load(string) " +
                    "returning UnityEngine.Object, found " + matches.Count + ".");
            }

            return matches[0];
        }

        private static MethodInfo Hook(string name)
        {
            MethodInfo method = typeof(RuntimeHarmonyPatches).GetMethod(name,
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException("Harmony hook method '" + name + "' was not found.");
            }

            return method;
        }

        private static MethodInfo TranspilerHook(string name)
        {
            MethodInfo method = typeof(RuntimeTranspilerPatches).GetMethod(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException("Harmony transpiler method '" + name + "' was not found.");
            }

            return method;
        }

        private static string DescribePatch(PatchPlan patch)
        {
            if (patch.Postfix != null)
            {
                return patch.Postfix.Name;
            }

            if (patch.Prefix != null)
            {
                return patch.Prefix.Name;
            }

            if (patch.Transpiler != null)
            {
                return patch.Transpiler.Name;
            }

            if (patch.Finalizer != null)
            {
                return patch.Finalizer.Name;
            }

            return "patch";
        }

        private static RuntimePatchInstallationResult Failed(string message)
        {
            return new RuntimePatchInstallationResult(false, false, message);
        }

        public static bool IsHooksInstalled => _harmony != null;

        public static int InstalledHookCount => _installedPatchCount;

        public static IReadOnlyList<string> InstalledPatchNames =>
            _installedPatchNames ?? (IReadOnlyList<string>)Array.Empty<string>();

        private static void ClearInstallationState()
        {
            _harmony = null;
            _provider = null;
            _mutator = null;
            _gameAssembly = null;
            _cleanupPending = false;
            _installedPatchCount = 0;
            _installedPatchNames = null;
        }

        private static void Log(Action<string> logger, string message)
        {
            if (logger == null)
            {
                return;
            }

            try
            {
                logger("[ExpansionLoader] " + message);
            }
            catch
            {
                // Logging failures do not affect installation.
            }
        }

        private static MethodInfo TryFindUniqueMethod(Type type, string name, Func<MethodInfo, bool> shapePredicate)
        {
            if (type == null || string.IsNullOrEmpty(name) || shapePredicate == null)
            {
                return null;
            }

            try
            {
                return RuntimeReflection.FindUniqueMethod(type, name, shapePredicate);
            }
            catch
            {
                return null;
            }
        }

        private sealed class PatchPlan
        {
            public PatchPlan(MethodBase target, MethodInfo prefix, MethodInfo postfix, MethodInfo finalizer = null)
                : this(target, prefix, postfix, null, finalizer)
            {
            }

            public PatchPlan(MethodBase target, MethodInfo prefix, MethodInfo postfix, MethodInfo transpiler, MethodInfo finalizer)
            {
                Target = target;
                Prefix = prefix;
                Postfix = postfix;
                Transpiler = transpiler;
                Finalizer = finalizer;
            }

            public MethodBase Target { get; private set; }

            public MethodInfo Prefix { get; private set; }

            public MethodInfo Postfix { get; private set; }

            public MethodInfo Transpiler { get; private set; }

            public MethodInfo Finalizer { get; private set; }
        }
    }

    internal static class RuntimeTranspilerPatches
    {
        private static readonly FieldInfo LabelsField = typeof(CodeInstruction).GetField(
            "labels", BindingFlags.Public | BindingFlags.Instance);

        private static void ClearLabels(CodeInstruction instruction)
        {
            if (instruction == null || LabelsField == null)
            {
                return;
            }

            try
            {
                var list = LabelsField.GetValue(instruction) as System.Collections.IList;
                list?.Clear();
            }
            catch
            {
            }
        }

        private static void TransferLabels(CodeInstruction source, CodeInstruction destination)
        {
            if (source == null || destination == null || LabelsField == null)
            {
                return;
            }

            try
            {
                var srcList = LabelsField.GetValue(source) as System.Collections.IList;
                var dstList = LabelsField.GetValue(destination) as System.Collections.IList;
                if (srcList != null && dstList != null && srcList.Count > 0)
                {
                    for (int i = 0; i < srcList.Count; i++)
                    {
                        dstList.Add(srcList[i]);
                    }
                    srcList.Clear();
                }
            }
            catch
            {
            }
        }

        public static IEnumerable<CodeInstruction> EntityFactoryInstantiateEntityTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityInstantiatedTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var inst = list[i];
                    if (inst.opcode == OpCodes.Callvirt && inst.operand is MethodInfo mi && mi.Name == "OnHasBeenInstantiated")
                    {
                        if (i >= 2)
                        {
                            CodeInstruction loadEntity = list[i - 2].Clone();
                            CodeInstruction loadFlag = list[i - 1].Clone();
                            ClearLabels(loadEntity);
                            ClearLabels(loadFlag);

                            list.Insert(i + 1, loadEntity);
                            list.Insert(i + 2, loadFlag);
                            list.Insert(i + 3, new CodeInstruction(OpCodes.Call, hookMethod));
                            break;
                        }
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> ExistingControllersOnEntityHasBeenCreatedTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityRegisteredTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                        var ldarg1 = new CodeInstruction(OpCodes.Ldarg_1);
                        var ldarg2 = new CodeInstruction(OpCodes.Ldarg_2);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], ldarg0);

                        list.Insert(i, ldarg0);
                        list.Insert(i + 1, ldarg1);
                        list.Insert(i + 2, ldarg2);
                        list.Insert(i + 3, callHook);

                        i += 4;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> ExistingControllersOnEntityWillBeDestroyedTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityWillBeDestroyedTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null || list.Count == 0)
                {
                    return list;
                }

                var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                var ldarg1 = new CodeInstruction(OpCodes.Ldarg_1);
                var ldarg2 = new CodeInstruction(OpCodes.Ldarg_2);
                var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                TransferLabels(list[0], ldarg0);

                list.Insert(0, ldarg0);
                list.Insert(1, ldarg1);
                list.Insert(2, ldarg2);
                list.Insert(3, callHook);

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> EntityFactoryInitEntityControllerTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnInitEntityControllerTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var dup = new CodeInstruction(OpCodes.Dup);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], dup);

                        list.Insert(i, dup);
                        list.Insert(i + 1, callHook);

                        i += 2;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> EntityControllerOnHasBeenInstantiatedTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityHasBeenInstantiatedTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                        var ldarg1 = new CodeInstruction(OpCodes.Ldarg_1);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], ldarg0);

                        list.Insert(i, ldarg0);
                        list.Insert(i + 1, ldarg1);
                        list.Insert(i + 2, callHook);

                        i += 3;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> EntityControllerOnHasBeenInstantiatedForReuseTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityHasBeenInstantiatedForReuseTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                        var ldarg1 = new CodeInstruction(OpCodes.Ldarg_1);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], ldarg0);

                        list.Insert(i, ldarg0);
                        list.Insert(i + 1, ldarg1);
                        list.Insert(i + 2, callHook);

                        i += 3;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> EntityControllerProduceTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityProduceTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                        var ldarg1 = new CodeInstruction(OpCodes.Ldarg_1);
                        var ldarg2 = new CodeInstruction(OpCodes.Ldarg_2);
                        var ldarg3 = new CodeInstruction(OpCodes.Ldarg_3);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], ldarg0);

                        list.Insert(i, ldarg0);
                        list.Insert(i + 1, ldarg1);
                        list.Insert(i + 2, ldarg2);
                        list.Insert(i + 3, ldarg3);
                        list.Insert(i + 4, callHook);

                        i += 5;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> ExistingControllersInitTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityRegistryResetTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], ldarg0);

                        list.Insert(i, ldarg0);
                        list.Insert(i + 1, callHook);

                        i += 2;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> EntityControllerResetAndInitForReuseTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityResetForReuseTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], ldarg0);

                        list.Insert(i, ldarg0);
                        list.Insert(i + 1, callHook);

                        i += 2;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> EntityFactoryCreateBuildingPlacementGhostTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnCreateBuildingPlacementGhostTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var dup = new CodeInstruction(OpCodes.Dup);
                        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                        var ldarg1 = new CodeInstruction(OpCodes.Ldarg_1);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], dup);

                        list.Insert(i, dup);
                        list.Insert(i + 1, ldarg0);
                        list.Insert(i + 2, ldarg1);
                        list.Insert(i + 3, callHook);

                        i += 4;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> EntityControllerInitTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityInitTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                        var ldarg1 = new CodeInstruction(OpCodes.Ldarg_1);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], ldarg0);

                        list.Insert(i, ldarg0);
                        list.Insert(i + 1, ldarg1);
                        list.Insert(i + 2, callHook);

                        i += 3;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> EntityControllerDestroyTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo originalMethod = __originalMethod as MethodInfo;
            Type declaringType = originalMethod == null ? null : originalMethod.DeclaringType;
            ParameterInfo[] originalParameters = originalMethod == null
                ? new ParameterInfo[0]
                : originalMethod.GetParameters();
            if (originalMethod == null || originalMethod.IsStatic ||
                !String.Equals(originalMethod.Name, "Destroy", StringComparison.Ordinal) ||
                declaringType != typeof(EntityController) ||
                originalMethod.ReturnType != typeof(void) || originalParameters.Length != 2 ||
                originalParameters[0].ParameterType != typeof(bool) ||
                originalParameters[1].ParameterType != typeof(EntityController))
            {
                throw new InvalidOperationException(
                    "EntityController.Destroy transpiler requires the game's exact instance void Destroy(bool, EntityController) target.");
            }

            MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                nameof(RuntimeHarmonyPatches.OnEntityDestroyTranspilerHook),
                BindingFlags.Public | BindingFlags.Static);
            ParameterInfo[] hookParameters = hookMethod == null
                ? new ParameterInfo[0]
                : hookMethod.GetParameters();
            if (hookMethod == null || hookMethod.DeclaringType != typeof(RuntimeHarmonyPatches) ||
                !hookMethod.IsStatic || hookMethod.ContainsGenericParameters ||
                hookMethod.ReturnType != typeof(void) ||
                hookParameters.Length != 3 || hookParameters[0].ParameterType != typeof(object) ||
                hookParameters[1].ParameterType != typeof(bool) ||
                hookParameters[2].ParameterType != typeof(object))
            {
                throw new InvalidOperationException(
                    "EntityController.Destroy transpiler callback must be void(object, bool, object).");
            }

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("EntityController.Destroy transpiler received an empty instruction stream.");
            }

            var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
            var ldarg1 = new CodeInstruction(OpCodes.Ldarg_1);
            var ldarg2 = new CodeInstruction(OpCodes.Ldarg_2);
            var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

            TransferLabels(list[0], ldarg0);

            list.Insert(0, ldarg0);
            list.Insert(1, ldarg1);
            list.Insert(2, ldarg2);
            list.Insert(3, callHook);

            return list;
        }

        public static IEnumerable<CodeInstruction> EntityControllerExecuteCommandTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityExecuteCommandTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null || list.Count == 0)
                {
                    return list;
                }

                var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                TransferLabels(list[0], ldarg0);

                list.Insert(0, ldarg0);
                list.Insert(1, callHook);

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> EntityControllerExecuteNextCommandTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityExecuteNextCommandTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], ldarg0);

                        list.Insert(i, ldarg0);
                        list.Insert(i + 1, callHook);

                        i += 2;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> EntityControllerResetCurrentCommandTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityResetCurrentCommandTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], ldarg0);

                        list.Insert(i, ldarg0);
                        list.Insert(i + 1, callHook);

                        i += 2;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }

        public static IEnumerable<CodeInstruction> EntityControllerDeleteCommandChainTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo hookMethod = typeof(RuntimeHarmonyPatches).GetMethod(
                    nameof(RuntimeHarmonyPatches.OnEntityDeleteCommandChainTranspilerHook),
                    BindingFlags.Public | BindingFlags.Static);

                if (hookMethod == null)
                {
                    return list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Ret)
                    {
                        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                        var callHook = new CodeInstruction(OpCodes.Call, hookMethod);

                        TransferLabels(list[i], ldarg0);

                        list.Insert(i, ldarg0);
                        list.Insert(i + 1, callHook);

                        i += 2;
                    }
                }

                return list;
            }
            catch
            {
                return instructions;
            }
        }
    }
}
