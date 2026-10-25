using System;
using System.Collections.Generic;
using System.Reflection;
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
                    RuntimeHooks.OnEntityInstantiated = Visual.RuntimeVisualDirector.AttachIfApplicable;
                    harmony = new Harmony(HarmonyOwnerId);
                    for (int i = 0; i < patches.Count; i++)
                    {
                        PatchPlan patch = patches[i];
                        HarmonyMethod prefix = patch.Prefix == null ? null : new HarmonyMethod(patch.Prefix);
                        HarmonyMethod postfix = patch.Postfix == null ? null : new HarmonyMethod(patch.Postfix);
                        HarmonyMethod finalizer = patch.Finalizer == null ? null : new HarmonyMethod(patch.Finalizer);
                        harmony.Patch(patch.Target, prefix, postfix, null, finalizer, null);
                    }

                    _harmony = harmony;
                    _provider = provider;
                    _mutator = mutator;
                    _gameAssembly = gameAssembly;
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
            MethodInfo takeDamage = RuntimeReflection.FindUniqueMethod(entityController, "TakeDamage",
                delegate(MethodInfo method)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 4 && parameters[0].ParameterType == typeof(float) && parameters[1].ParameterType == entityController;
                });
            MethodInfo takeDamagePostfix = Hook("EntityTakeDamagePostfix");
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

            List<PatchPlan> plan = new List<PatchPlan>();
            plan.Add(new PatchPlan(init, null, entityInitPostfix));
            plan.Add(new PatchPlan(setScriptableObject, null, setScriptableObjectPostfix));
            plan.Add(new PatchPlan(createStageMap, stageMapPrefix, stageMapPostfix));
            plan.Add(new PatchPlan(stageMapJsonConstructor, null, stageMapJsonConstructorPostfix));
            plan.Add(new PatchPlan(stageMapToJson, null, stageMapToJsonPostfix));
            plan.Add(new PatchPlan(createLevelMap, levelMapPrefix, null, levelMapFinalizer));
            plan.Add(new PatchPlan(instantiateEntity, instantiatePrefix, instantiatePostfix));
            plan.Add(new PatchPlan(createEntityMesh, entityMeshPrefix, null));
            plan.Add(new PatchPlan(aiUpdate, null, aiUpdatePostfix));
            plan.Add(new PatchPlan(aiInit, null, aiInitPostfix));
            plan.Add(new PatchPlan(aiDestroy, null, aiDestroyPostfix));
            plan.Add(new PatchPlan(blueprintName, blueprintNamePrefix, null));
            plan.Add(new PatchPlan(blueprintDescription, blueprintDescriptionPrefix, null));
            plan.Add(new PatchPlan(skillDescription, skillDescriptionPrefix, null));
            plan.Add(new PatchPlan(relicName, relicNamePrefix, null));
            plan.Add(new PatchPlan(relicDescription, relicDescriptionPrefix, null));
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
            plan.Add(new PatchPlan(takeDamage, null, takeDamagePostfix));
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

        private static RuntimePatchInstallationResult Failed(string message)
        {
            return new RuntimePatchInstallationResult(false, false, message);
        }

        private static void ClearInstallationState()
        {
            _harmony = null;
            _provider = null;
            _mutator = null;
            _gameAssembly = null;
            _cleanupPending = false;
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

        private sealed class PatchPlan
        {
            public PatchPlan(MethodBase target, MethodInfo prefix, MethodInfo postfix, MethodInfo finalizer = null)
            {
                Target = target;
                Prefix = prefix;
                Postfix = postfix;
                Finalizer = finalizer;
            }

            public MethodBase Target { get; private set; }

            public MethodInfo Prefix { get; private set; }

            public MethodInfo Postfix { get; private set; }

            public MethodInfo Finalizer { get; private set; }
        }
    }
}
