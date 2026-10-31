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
            private int _lastProcessedAiUnitCount;
            private int _lastProcessedAlliedUnitCount;
            private System.Collections.Generic.HashSet<EntityController> _autoControlledAlliedUnits;

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

                // Safe, non-destructive cadence: count live AI units so the module
                // is observable and ready for target reassignment in a follow-up
                // that has REA-verified EntityController target members.
                try
                {
                    int aiUnitCount = 0;
                    int alliedUnitCount = 0;
                    System.Collections.Generic.HashSet<EntityController> autoControlledAllies = new System.Collections.Generic.HashSet<EntityController>();
                    ExistingControllers existing = ExistingControllers.Instance;
                    if (existing != null)
                    {
                        System.Collections.Generic.HashSet<EntityController> aiUnits = existing.AiUnits();
                        if (aiUnits != null)
                        {
                            aiUnitCount = aiUnits.Count;
                        }
                        System.Collections.Generic.HashSet<EntityController> playerUnits = existing.PlayerUnits();
                        if (playerUnits != null)
                        {
                            alliedUnitCount = playerUnits.Count;
                            // Specialized for allied units: mark them as auto-controlled by this module
                            // so the player does not need to micromanage them. Actual control
                            // reassignment is deferred to a REA-verified follow-up.
                            foreach (EntityController ally in playerUnits)
                            {
                                if (ally != null)
                                {
                                    autoControlledAllies.Add(ally);
                                }
                            }
                        }
                    }
                    _lastProcessedAiUnitCount = aiUnitCount;
                    _lastProcessedAlliedUnitCount = alliedUnitCount;
                    _autoControlledAlliedUnits = autoControlledAllies;
                }
                catch
                {
                    // Guarded: diagnostics must never break the tick.
                }
            }

            public int LastProcessedAiUnitCount
            {
                get { return _lastProcessedAiUnitCount; }
            }

            public int LastProcessedAlliedUnitCount
            {
                get { return _lastProcessedAlliedUnitCount; }
            }

            public int AutoControlledAlliedUnitCount
            {
                get { return _autoControlledAlliedUnits != null ? _autoControlledAlliedUnits.Count : 0; }
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