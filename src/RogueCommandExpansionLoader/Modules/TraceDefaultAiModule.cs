using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Sample module used by the sample pack. It inherits the game's default AI
    /// and reports initialization so pack integration can be verified in logs.
    /// </summary>
    public sealed class TraceDefaultAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = "sample:trace-default";

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            parameters = parameters ?? new ContentModuleParameter[0];
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "logTag" };
            for (int i = 0; i < parameters.Length; i++)
            {
                ContentModuleParameter parameter = parameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.name) || !allowed.Contains(parameter.name))
                {
                    diagnostic = "Module " + ModuleId + " accepts only the optional string parameter 'logTag'.";
                    return false;
                }
            }

            diagnostic = null;
            return true;
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            string logTag = "sample-pack";
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i] != null && string.Equals(parameters[i].name, "logTag", StringComparison.Ordinal))
                    {
                        logTag = parameters[i].value ?? string.Empty;
                    }
                }
            }

            return new TraceDefaultAiRuntimeBehaviour(gameBehaviour, logTag);
        }
    }

    internal sealed class TraceDefaultAiRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
    {
        private readonly DefaultAiBehaviour gameBehaviour;
        private readonly string logTag;

        public TraceDefaultAiRuntimeBehaviour(DefaultAiBehaviour gameBehaviour, string logTag)
        {
            this.gameBehaviour = gameBehaviour ?? throw new ArgumentNullException("gameBehaviour");
            this.logTag = logTag ?? string.Empty;
        }

        public void OnInitialized()
        {
            UnityEngine.Debug.Log("[RogueCommandExpansionLoader] Sample AI module initialized for '" +
                gameBehaviour.gameObject.name + "' (" + (logTag.Length == 0 ? "sample-pack" : logTag) + ").");
        }

        public void Tick()
        {
            // The sample deliberately observes the real game-owned component without
            // creating a second AiBehaviour or competing for DefaultAiBehaviour._instance.
        }

        public void OnDestroyed()
        {
        }
    }
}
