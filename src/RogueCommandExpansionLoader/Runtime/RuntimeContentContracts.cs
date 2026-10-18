using System.Collections.Generic;
using System;
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

        /// <summary>Resolves a localized name or description for a registered mod unit.</summary>
        bool TryGetUnitLocalization(string unitId, bool description, out string value);

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

    /// <summary>Optional bridge for localized ability text on mod units.</summary>
    public interface IUnitSkillLocalizationRuntimeProvider
    {
        bool TryGetUnitSkillDescription(string unitId, out string value);
    }

    /// <summary>
    /// Optional bridge for a stage-owned procedural landscape. The returned scope is temporary
    /// and must be ended from a Harmony finalizer so game collections are restored after failures.
    /// </summary>
    public interface IStageLandscapeRuntimeProvider
    {
        IDisposable BeginStageLandscapeScope(object stage);
        bool TryGetLandscapeLocalization(string localizationId, out string value);
    }

    public interface IStageFallbackRuntimeProvider
    {
        object GetFallbackLandscapeSnapshot(object nativeFallbacks);
    }

    /// <summary>
    /// Optional bridge for extending the native outlying AI building spacing band only when the
    /// active progression stage is owned by this content provider.
    /// </summary>
    public interface IStageOwnedAiPlacementDistanceRuntimeProvider
    {
        bool TryGetOutlyingStartAiBuildingsDistance(out int distance);
    }

    /// <summary>Pure policy helper for the native stage-distance bands and appended pack stages.</summary>
    internal static class StageOwnedAiPlacementDistanceResolver
    {
        public const int FirstAppendedStageIndex = 9;

        public static bool TryResolve<TLoadedStage>(
            int stageIndex,
            object activeStage,
            IEnumerable<TLoadedStage> loadedStages,
            Func<TLoadedStage, object> getRuntimeStage,
            int finalNativeBandDistance,
            out int distance)
        {
            distance = 0;
            if (stageIndex < FirstAppendedStageIndex || activeStage == null || loadedStages == null ||
                getRuntimeStage == null)
            {
                return false;
            }

            foreach (TLoadedStage loadedStage in loadedStages)
            {
                if (object.ReferenceEquals(activeStage, getRuntimeStage(loadedStage)))
                {
                    distance = finalNativeBandDistance;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Optional bridge for stage-owned clones of the game's existing BlueprintPools. Implementations
    /// scope only mod-unit rows; the game's authored pool filter remains responsible for eligibility.
    /// </summary>
    public interface IBlueprintPoolRuntimeProvider
    {
        bool TryFindBlueprintPool(string poolId, out object pool);

        bool TryGetBlueprintPoolLocalizationId(string poolId, out string sourcePoolId);

        bool TryGetBlueprintPoolId(object pool, out string poolId);

        /// <summary>
        /// Returns true when the unit is loader-owned and sets whether the game's IsInPool method
        /// should run for this exact pool instance.
        /// </summary>
        bool TryGetBlueprintPoolDecision(object pool, string unitId, out bool runOriginal);

        void AddExclusiveBlueprints(string otherThanPoolId, HashSet<string> result);
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
