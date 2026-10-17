using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Boundary between the loader's manifest/asset pipeline and game-specific runtime adapters.
    /// Implementations must keep returned Unity assets stable for the lifetime of a registration.
    /// TryLoadResource must resolve from mod-owned assets and must not call Resources.Load for the
    /// same requested path.
    /// </summary>
    public interface IRuntimeContentProvider
    {
        string TargetGameVersion { get; }

        /// <summary>Called after the native EntityBalancingStore finishes its first load.</summary>
        void OnEntityRegistryInitialized();

        /// <summary>
        /// Loads validated packs on the Unity thread immediately before the first stage-map or
        /// entity-factory operation. Returns false until the native entity registry is ready.
        /// </summary>
        bool PrepareContentForRuntime();

        /// <summary>Called after content entity rows have been appended to the game registry.</summary>
        void OnEntityRowsRegistered();

        IEnumerable<object> GetEntityParameterRows();

        IEnumerable<ProgressionContentContribution> GetProgressionContributions(
            object gameBalancingScriptableObject);

        /// <summary>Dispatches a data-bound module when an instantiated game's AI updates.</summary>
        void OnAiBehaviourInitialized(object gameBehaviour);
        void OnAiBehaviourUpdated(object gameBehaviour);

        /// <summary>Releases module state when an instantiated game's AI is destroyed.</summary>
        void OnAiBehaviourDestroyed(object gameBehaviour);

        bool TryLoadResource(string resourcePath, out UnityEngine.Object asset);

        /// <summary>Returns an immutable diagnostic snapshot of all discovered packs and runtime content.</summary>
        LoaderDiagnosticsSnapshot GetDiagnosticsSnapshot();
    }

    /// <summary>
    /// Runtime references to objects loaded by the content pipeline. Values are kept
    /// object-typed here so this layer does not bind to Assembly-CSharp at compile time.
    /// </summary>
    public sealed class ProgressionContentContribution
    {
        public ProgressionContentContribution(
            string key,
            IEnumerable<object> stages,
            IEnumerable<object> aiBehaviours)
        {
            Key = key;
            Stages = stages;
            AiBehaviours = aiBehaviours;
        }

        /// <summary>A stable, unique key for this contribution within the provider.</summary>
        public string Key { get; private set; }

        /// <summary>Game Stage instances that wrap loaded StageParametersScriptableObject assets.</summary>
        public IEnumerable<object> Stages { get; private set; }

        /// <summary>DefaultAiBehaviour instances created by the core AI-module bridge.</summary>
        public IEnumerable<object> AiBehaviours { get; private set; }
    }

    public sealed class RuntimePatchInstallationResult
    {
        internal RuntimePatchInstallationResult(bool success, bool alreadyInstalled, string message)
        {
            Success = success;
            AlreadyInstalled = alreadyInstalled;
            Message = message;
        }

        public bool Success { get; private set; }

        public bool AlreadyInstalled { get; private set; }

        public string Message { get; private set; }
    }
}
