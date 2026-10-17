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
                    harmony = new Harmony(HarmonyOwnerId);
                    for (int i = 0; i < patches.Count; i++)
                    {
                        PatchPlan patch = patches[i];
                        HarmonyMethod prefix = patch.Prefix == null ? null : new HarmonyMethod(patch.Prefix);
                        HarmonyMethod postfix = patch.Postfix == null ? null : new HarmonyMethod(patch.Postfix);
                        harmony.Patch(patch.Target, prefix, postfix);
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

        public static bool Uninstall(out string message)
        {
            lock (Gate)
            {
                if (_harmony == null)
                {
                    message = "Runtime hooks are not installed.";
                    return true;
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
            Type entityFactory = RuntimeReflection.FindUniqueType(gameAssembly, "EntityFactory");
            Type aiBehaviour = RuntimeReflection.FindUniqueType(gameAssembly, "AiBehaviour");
            Type defaultAiBehaviour = RuntimeReflection.FindUniqueType(gameAssembly, "DefaultAiBehaviour");

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
            MethodInfo resourcesLoadTyped = FindResourcesLoadTyped();

            MethodInfo entityInitPostfix = Hook("EntityBalancingStoreInitPostfix");
            MethodInfo setScriptableObjectPostfix = Hook("SetScriptableObjectPostfix");
            MethodInfo stageMapPrefix = Hook("CreateStageMapPrefix");
            MethodInfo levelMapPrefix = Hook("CreateLevelMapPrefix");
            MethodInfo instantiatePrefix = Hook("InstantiateEntityPrefix");
            MethodInfo entityMeshPrefix = Hook("CreateEntityMeshPrefix");
            MethodInfo aiUpdatePostfix = Hook("AiBehaviourUpdatePostfix");
            MethodInfo aiInitPostfix = Hook("DefaultAiBehaviourInitPostfix");
            MethodInfo aiDestroyPostfix = Hook("AiBehaviourDestroyPostfix");
            MethodInfo loadPrefix = typeof(UnityResourcesLoadTypedPatch).GetMethod("Prefix",
                BindingFlags.Public | BindingFlags.Static);
            if (loadPrefix == null)
            {
                throw new InvalidOperationException("Unity Resources.Load(string, Type) prefix method was not found.");
            }

            List<PatchPlan> plan = new List<PatchPlan>();
            plan.Add(new PatchPlan(init, null, entityInitPostfix));
            plan.Add(new PatchPlan(setScriptableObject, null, setScriptableObjectPostfix));
            plan.Add(new PatchPlan(createStageMap, stageMapPrefix, null));
            plan.Add(new PatchPlan(createLevelMap, levelMapPrefix, null));
            plan.Add(new PatchPlan(instantiateEntity, instantiatePrefix, null));
            plan.Add(new PatchPlan(createEntityMesh, entityMeshPrefix, null));
            plan.Add(new PatchPlan(aiUpdate, null, aiUpdatePostfix));
            plan.Add(new PatchPlan(aiInit, null, aiInitPostfix));
            plan.Add(new PatchPlan(aiDestroy, null, aiDestroyPostfix));
            plan.Add(new PatchPlan(resourcesLoadTyped, loadPrefix, null));
            return plan;
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
            public PatchPlan(MethodBase target, MethodInfo prefix, MethodInfo postfix)
            {
                Target = target;
                Prefix = prefix;
                Postfix = postfix;
            }

            public MethodBase Target { get; private set; }

            public MethodInfo Prefix { get; private set; }

            public MethodInfo Postfix { get; private set; }
        }
    }
}
