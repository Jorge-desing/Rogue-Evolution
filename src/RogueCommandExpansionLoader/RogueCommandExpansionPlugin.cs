using System;
using BepInEx;
using RogueCommandExpansionLoader.Compatibility;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Modules;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine.SceneManagement;

namespace RogueCommandExpansionLoader
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class RogueCommandExpansionPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "roguecommand.expansionloader";
        public const string PluginName = "Rogue Command Expansion Loader";
        public const string PluginVersion = "1.1.0";

        private ContentPackRuntimeLoader _contentLoader;
        private RuntimeOverlayUi _overlay;
        private bool _sceneDiagnosticsSubscribed;

        private void Awake()
        {
            UnityEngine.Application.runInBackground = true;

            string gameRoot = Paths.GameRootPath;
            string compatibilityFailure;
            if (!GameCompatibility.TryValidate(gameRoot, out compatibilityFailure))
            {
                Logger.LogWarning("[ExpansionLoader] Se desactivó por compatibilidad: " + compatibilityFailure);
                ShowOverlay(null, null, compatibilityFailure);
                return;
            }

            try
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.activeSceneChanged += OnActiveSceneChanged;
                _sceneDiagnosticsSubscribed = true;
                Logger.LogInfo("[ExpansionLoader] Scene transition diagnostics subscribed.");

                var modules = new AiModuleRegistry();
                modules.Register(new TraceDefaultAiModule());
                modules.Register(new ScheduledBaseBuildAiModule());
                modules.Register(new ReactiveBaseDefenseAiModule());
                modules.Register(new HarvesterRaiderAiModule());
                modules.Register(new SiegeAssaultAiModule());

                string packsRoot = System.IO.Path.Combine(
                    Paths.PluginPath,
                    "RogueCommandExpansionLoader",
                    "content",
                    "packs");
                _contentLoader = new ContentPackRuntimeLoader(
                    packsRoot,
                    typeof(DefaultAiBehaviour).Assembly,
                    modules,
                    message => Logger.LogInfo(message));

                RuntimePatchInstallationResult result = RuntimePatchInstaller.Install(
                    gameRoot,
                    typeof(DefaultAiBehaviour).Assembly,
                    _contentLoader,
                    message => Logger.LogInfo(message));

                if (!result.Success)
                {
                    Logger.LogError("[ExpansionLoader] No se instaló ningún patch: " + result.Message);
                    ShowOverlay(_contentLoader, result, result.Message);
                    return;
                }

                Logger.LogInfo("[ExpansionLoader] " + result.Message);
                ShowOverlay(_contentLoader, result, null);
            }
            catch (Exception exception)
            {
                _contentLoader = null;
                string failure = exception.GetType().Name + ": " + exception.Message;
                Logger.LogError("[ExpansionLoader] Error al iniciar: " + failure);
                ShowOverlay(null, null, failure);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo("[ExpansionLoader] Scene loaded: '" + scene.name + "' (buildIndex=" +
                scene.buildIndex + ", mode=" + mode + ").");
        }

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            Logger.LogInfo("[ExpansionLoader] Active scene changed: '" + previous.name + "' -> '" +
                next.name + "'.");
        }

        private void OnDestroy()
        {
            if (_sceneDiagnosticsSubscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.activeSceneChanged -= OnActiveSceneChanged;
                _sceneDiagnosticsSubscribed = false;
            }

            if (_contentLoader != null)
            {
                string message;
                if (RuntimePatchInstaller.Uninstall(_contentLoader, out message))
                {
                    Logger.LogInfo("[ExpansionLoader] Shutdown: " + message);
                }
                else
                {
                    Logger.LogError("[ExpansionLoader] Shutdown could not remove Harmony hooks: " + message);
                }

                _contentLoader.ShutdownRuntimeBehaviours();
            }
        }

        private void ShowOverlay(
            IRuntimeContentProvider provider,
            RuntimePatchInstallationResult patchResult,
            string compatibilityError)
        {
            try
            {
                if (_overlay == null)
                {
                    _overlay = gameObject.AddComponent<RuntimeOverlayUi>();
                    DontDestroyOnLoad(gameObject);
                }

                _overlay.Initialize(
                    provider,
                    PluginVersion,
                    patchResult,
                    compatibilityError,
                    message => Logger.LogWarning(message));
            }
            catch (Exception exception)
            {
                Logger.LogWarning("[ExpansionLoader] No se pudo inicializar el inspector: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }
    }
}
