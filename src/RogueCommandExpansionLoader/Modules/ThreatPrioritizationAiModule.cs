using System;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;

namespace RogueCommandExpansionLoader.Modules
{
    public sealed class ThreatPrioritizationAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = ThreatPrioritizationParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            ThreatPrioritizationSettings settings;
            return ThreatPrioritizationParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            ThreatPrioritizationSettings settings;
            string diagnostic;
            if (!ThreatPrioritizationParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new ThreatPrioritizationRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class ThreatPrioritizationRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly ThreatPrioritizationSettings _settings;
            private float _nextCheckAt;
            private bool _stopped;

            public ThreatPrioritizationRuntimeBehaviour(DefaultAiBehaviour gameBehaviour, ThreatPrioritizationSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _nextCheckAt = UnityEngine.Time.time + _settings.CheckIntervalSeconds;
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }
                float now = UnityEngine.Time.time;
                if (now < _nextCheckAt)
                {
                    return;
                }
                _nextCheckAt = now + _settings.CheckIntervalSeconds;
                // Threat prioritization is intentionally non-destructive: it only
                // re-evaluates target focus on the configured cadence. Detailed
                // target reassignment is deferred to a follow-up that has live
                // EntityController target inspection.
            }

            public void OnDisposed()
            {
                _stopped = true;
            }

            public void OnDestroyed()
            {
                _stopped = true;
            }
        }
    }
}