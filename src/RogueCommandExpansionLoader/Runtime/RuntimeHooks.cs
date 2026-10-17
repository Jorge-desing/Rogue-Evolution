using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Static entry points used by Harmony. Hook bodies catch provider/reflection failures so a
    /// content problem does not replace the game's normal execution path.
    /// </summary>
    internal static class RuntimeHooks
    {
        private static readonly object Gate = new object();
        private static RuntimeContentMutator _mutator;
        private static IRuntimeContentProvider _provider;
        private static Action<string> _log;

        public static void Bind(
            RuntimeContentMutator mutator,
            IRuntimeContentProvider provider,
            Action<string> logger)
        {
            lock (Gate)
            {
                _mutator = mutator;
                _provider = provider;
                _log = logger;
            }
        }

        public static void Clear(RuntimeContentMutator expectedMutator)
        {
            lock (Gate)
            {
                if (!object.ReferenceEquals(_mutator, expectedMutator))
                {
                    return;
                }

                _mutator = null;
                _provider = null;
                _log = null;
            }
        }

        public static void AfterEntityBalancingInit()
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            if (provider != null)
            {
                SafeCall(delegate { provider.OnEntityRegistryInitialized(); });
            }
        }

        public static void AfterSetScriptableObject(object[] arguments)
        {
            RuntimeContentMutator mutator = GetMutator();
            if (mutator == null || arguments == null || arguments.Length == 0)
            {
                return;
            }

            SafeCall(delegate { mutator.ApplyProgressionFrom(arguments[0]); });
        }

        public static void BeforeProgressionMap()
        {
            RuntimeContentMutator mutator;
            if (TryPrepareRuntimeContent(out mutator))
            {
                bool rowsRegistered = false;
                SafeCall(delegate { rowsRegistered = mutator.RegisterEntityRows(); });
                if (rowsRegistered)
                {
                    SafeCall(delegate { mutator.ApplyCachedProgression(); });
                }
            }
        }

        public static void BeforeEntityFactory()
        {
            RuntimeContentMutator mutator;
            if (TryPrepareRuntimeContent(out mutator))
            {
                SafeCall(delegate { mutator.RegisterEntityRows(); });
            }
        }

        private static bool TryPrepareRuntimeContent(out RuntimeContentMutator mutator)
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                mutator = _mutator;
                provider = _provider;
            }

            if (mutator == null || provider == null)
            {
                return false;
            }

            bool ready = false;
            SafeCall(delegate { ready = provider.PrepareContentForRuntime(); });
            return ready;
        }

        public static void DefaultAiBehaviourInitPostfix(object __instance)
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            if (provider != null && __instance != null)
            {
                SafeCall(delegate { provider.OnAiBehaviourInitialized(__instance); });
            }
        }

        public static void AiBehaviourUpdatePostfix(object __instance)
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            if (provider != null && __instance != null)
            {
                SafeCall(delegate { provider.OnAiBehaviourUpdated(__instance); });
            }
        }

        public static void AiBehaviourDestroyPostfix(object __instance)
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            if (provider != null && __instance != null)
            {
                SafeCall(delegate { provider.OnAiBehaviourDestroyed(__instance); });
            }
        }

        public static bool TryLoadResource(string resourcePath, out UnityEngine.Object asset)
        {
            asset = null;
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            if (provider == null || String.IsNullOrEmpty(resourcePath))
            {
                return false;
            }

            try
            {
                if (provider.TryLoadResource(resourcePath, out asset) && asset != null)
                {
                    return true;
                }
            }
            catch (Exception exception)
            {
                Log("Resource provider failed for '" + resourcePath + "': " + exception.Message);
            }

            asset = null;
            return false;
        }

        private static RuntimeContentMutator GetMutator()
        {
            lock (Gate)
            {
                return _mutator;
            }
        }

        private static void SafeCall(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Log("Runtime hook failed safely: " + exception.Message);
            }
        }

        private static void Log(string message)
        {
            Action<string> logger;
            lock (Gate)
            {
                logger = _log;
            }

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
                // Logging failures are isolated from the game thread.
            }
        }
    }

    internal static class RuntimeHarmonyPatches
    {
        public static void EntityBalancingStoreInitPostfix()
        {
            RuntimeHooks.AfterEntityBalancingInit();
        }

        public static void SetScriptableObjectPostfix(object[] __args)
        {
            RuntimeHooks.AfterSetScriptableObject(__args);
        }

        public static void CreateStageMapPrefix()
        {
            RuntimeHooks.BeforeProgressionMap();
        }

        public static void CreateLevelMapPrefix()
        {
            RuntimeHooks.BeforeProgressionMap();
        }

        public static void InstantiateEntityPrefix()
        {
            RuntimeHooks.BeforeEntityFactory();
        }

        public static void CreateEntityMeshPrefix()
        {
            RuntimeHooks.BeforeEntityFactory();
        }

        public static void DefaultAiBehaviourInitPostfix(object __instance)
        {
            RuntimeHooks.DefaultAiBehaviourInitPostfix(__instance);
        }

        public static void AiBehaviourUpdatePostfix(object __instance)
        {
            RuntimeHooks.AiBehaviourUpdatePostfix(__instance);
        }

        public static void AiBehaviourDestroyPostfix(object __instance)
        {
            RuntimeHooks.AiBehaviourDestroyPostfix(__instance);
        }
    }

    internal static class UnityResourcesLoadTypedPatch
    {
        /// <summary>
        /// Patch the shared Resources.Load(string, Type) implementation used by generic and
        /// non-generic wrappers. Every requested asset type follows one type-checked path.
        /// </summary>
        public static bool Prefix(string path, Type systemTypeInstance, ref UnityEngine.Object __result)
        {
            if (String.IsNullOrEmpty(path) || systemTypeInstance == null)
            {
                return true;
            }

            // Protect against a provider accidentally re-entering Resources.Load for its own path.
            if (!ResourceLoadRecursionGuard.TryEnter(path))
            {
                return true;
            }

            try
            {
                UnityEngine.Object asset;
                if (RuntimeHooks.TryLoadResource(path, out asset) &&
                    systemTypeInstance.IsInstanceOfType(asset))
                {
                    __result = asset;
                    return false;
                }

                return true;
            }
            finally
            {
                ResourceLoadRecursionGuard.Exit(path);
            }
        }
    }

    internal static class ResourceLoadRecursionGuard
    {
        [ThreadStatic]
        private static HashSet<string> _activePaths;

        public static bool TryEnter(string path)
        {
            if (_activePaths == null)
            {
                _activePaths = new HashSet<string>(StringComparer.Ordinal);
            }

            return _activePaths.Add(path);
        }

        public static void Exit(string path)
        {
            if (_activePaths != null)
            {
                _activePaths.Remove(path);
            }
        }
    }
}
