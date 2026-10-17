using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Contracts
{
    /// <summary>
    /// Compiled AI extension point. Modules live in the loader assembly; content
    /// packs select a registered ID and provide data-only string parameters.
    /// </summary>
    public interface IRogueCommandAiModule
    {
        string Id { get; }
        int ApiVersion { get; }

        bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic);

        /// <summary>
        /// Creates per-level logic bound to the selected AI prefab instance.
        /// The game-owned DefaultAiBehaviour remains responsible for Unity's AI lifecycle.
        /// </summary>
        IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters);
    }

    /// <summary>
    /// Per-instance logic created after the game's AI component has initialized. Tick is called
    /// after the native AI Update on Unity's main thread. OnDestroyed can run before or during
    /// OnInitialized when module creation or initialization fails, including for behaviours later
    /// in the profile that have not initialized yet. Implementations must make cleanup safe in
    /// those states and tolerate it being called once by the composite lifecycle.
    /// </summary>
    public interface IRogueCommandAiRuntimeBehaviour
    {
        void OnInitialized();
        void Tick();
        void OnDestroyed();
    }
}
