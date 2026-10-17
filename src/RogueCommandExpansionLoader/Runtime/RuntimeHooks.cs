using System;
using System.Collections.Generic;
using System.Reflection;
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

        public static bool BlueprintNamePrefix(string __0, ref string __result)
        {
            return TryResolveUnitLocalization(__0, false, ref __result);
        }

        public static bool BlueprintDescriptionPrefix(string __0, ref string __result)
        {
            return TryResolveUnitLocalization(__0, true, ref __result);
        }

        private static bool TryResolveUnitLocalization(string unitId, bool description, ref string result)
        {
            IRuntimeContentProvider provider;
            Action<string> logger;
            lock (Gate)
            {
                provider = _provider;
                logger = _log;
            }

            if (provider == null)
            {
                return true;
            }

            try
            {
                string value;
                if (!provider.TryGetUnitLocalization(unitId, description, out value))
                {
                    return true;
                }

                result = value ?? String.Empty;
                return false;
            }
            catch (Exception exception)
            {
                if (logger != null)
                {
                    logger("Could not resolve localized text for unit '" + unitId + "': " +
                        exception.GetType().Name + ": " + exception.Message);
                }

                return true;
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
        private static UnityEngine.Object _fallbackTmpSettings;

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

                if (String.Equals(path, "TMP Settings", StringComparison.Ordinal) ||
                    String.Equals(path, "TMP Settings.asset", StringComparison.Ordinal))
                {
                    if (_fallbackTmpSettings == null && typeof(ScriptableObject).IsAssignableFrom(systemTypeInstance))
                    {
                        try
                        {
                            ScriptableObject settings = ScriptableObject.CreateInstance(systemTypeInstance);
                            settings.name = "Fallback TMP Settings";
                            settings.hideFlags = HideFlags.HideAndDontSave;

                            // 1. m_defaultStyleSheet with "Normal" style initialized
                            FieldInfo styleSheetField = systemTypeInstance.GetField("m_defaultStyleSheet",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (styleSheetField != null && typeof(ScriptableObject).IsAssignableFrom(styleSheetField.FieldType))
                            {
                                ScriptableObject sheet = ScriptableObject.CreateInstance(styleSheetField.FieldType);
                                sheet.name = "Fallback TMP Style Sheet";
                                sheet.hideFlags = HideFlags.HideAndDontSave;
                                MethodInfo refreshStyles = styleSheetField.FieldType.GetMethod("RefreshStyles",
                                    BindingFlags.Public | BindingFlags.Instance);
                                if (refreshStyles != null)
                                {
                                    refreshStyles.Invoke(sheet, null);
                                }
                                styleSheetField.SetValue(settings, sheet);
                            }

                            // 2. m_linebreakingRules initialized with empty dictionaries
                            FieldInfo linebreakingField = systemTypeInstance.GetField("m_linebreakingRules",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (linebreakingField != null)
                            {
                                Type tableType = linebreakingField.FieldType;
                                object tableInstance = Activator.CreateInstance(tableType);
                                FieldInfo leadingField = tableType.GetField("leadingCharacters", BindingFlags.Public | BindingFlags.Instance);
                                FieldInfo followingField = tableType.GetField("followingCharacters", BindingFlags.Public | BindingFlags.Instance);
                                if (leadingField != null)
                                {
                                    leadingField.SetValue(tableInstance, Activator.CreateInstance(leadingField.FieldType));
                                }
                                if (followingField != null)
                                {
                                    followingField.SetValue(tableInstance, Activator.CreateInstance(followingField.FieldType));
                                }
                                linebreakingField.SetValue(settings, tableInstance);
                            }

                            // 3. m_fallbackFontAssets non-null list
                            FieldInfo fallbackFontsField = systemTypeInstance.GetField("m_fallbackFontAssets",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fallbackFontsField != null)
                            {
                                fallbackFontsField.SetValue(settings, Activator.CreateInstance(fallbackFontsField.FieldType));
                            }

                            // 4. Safe non-null path strings
                            FieldInfo fontPathField = systemTypeInstance.GetField("m_defaultFontAssetPath",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fontPathField != null) fontPathField.SetValue(settings, string.Empty);
                            FieldInfo spritePathField = systemTypeInstance.GetField("m_defaultSpriteAssetPath",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (spritePathField != null) spritePathField.SetValue(settings, string.Empty);
                            FieldInfo stylePathField = systemTypeInstance.GetField("m_StyleSheetsResourcePath",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (stylePathField != null) stylePathField.SetValue(settings, string.Empty);
                            FieldInfo gradPathField = systemTypeInstance.GetField("m_defaultColorGradientPresetsPath",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (gradPathField != null) gradPathField.SetValue(settings, string.Empty);

                            // 5. Assign static s_Instance so TMP_Settings.instance returns immediately
                            FieldInfo instanceField = systemTypeInstance.GetField("s_Instance",
                                BindingFlags.NonPublic | BindingFlags.Static);
                            if (instanceField != null)
                            {
                                instanceField.SetValue(null, settings);
                            }

                            _fallbackTmpSettings = settings;
                        }
                        catch
                        {
                            // Fallback creation failure passes through to engine.
                        }
                    }

                    if (_fallbackTmpSettings != null && systemTypeInstance.IsInstanceOfType(_fallbackTmpSettings))
                    {
                        __result = _fallbackTmpSettings;
                        return false;
                    }
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
