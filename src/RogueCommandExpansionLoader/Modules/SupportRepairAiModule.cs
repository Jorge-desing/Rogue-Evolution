using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Coordinates autonomous field repair and logistical support for AI engineer and healer units.
    /// Monitors damaged friendly units and structures, prioritizing critical infrastructure and harvesters.
    /// </summary>
    public sealed class SupportRepairAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = SupportRepairParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            SupportRepairSettings settings;
            return SupportRepairParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            SupportRepairSettings settings;
            string diagnostic;
            if (!SupportRepairParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new SupportRepairRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class SupportRepairRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const string CommandTagPrefix = "rcl-supportrepair-";
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly SupportRepairSettings _settings;
            private string _commandTag;
            private float _nextRepairTickAt;
            private bool _stopped;

            public SupportRepairRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                SupportRepairSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _commandTag = CommandTagPrefix + _gameBehaviour.GetInstanceID();
                _nextRepairTickAt = 0f;
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }

                float now = Time.time;
                if (now < _nextRepairTickAt)
                {
                    return;
                }

                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    _nextRepairTickAt = now + 1.0f;
                    return;
                }

                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    _nextRepairTickAt = now + 1.0f;
                    return;
                }

                // Collect available support repairers
                var repairers = new List<EntityController>();
                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted ||
                        unit.IsBuilding || !unit.IsControlledByAi || unit.canNotBeAttacked)
                    {
                        continue;
                    }

                    if (unit.IsEngineer || unit.HasRole(UnitRole.Engineer) || unit.HealAmount1 > 0f)
                    {
                        repairers.Add(unit);
                    }
                }

                if (repairers.Count == 0)
                {
                    _nextRepairTickAt = now + 2.0f;
                    return;
                }

                // Collect damaged friendly candidates
                var damagedCandidates = new List<EntityController>();
                float scanRadiusSq = _settings.RepairScanRadius * _settings.RepairScanRadius;

                // Check units
                foreach (EntityController candidate in aiUnits)
                {
                    if (IsEligibleRepairTarget(candidate))
                    {
                        damagedCandidates.Add(candidate);
                    }
                }

                // Check buildings
                HashSet<EntityController> aiBuildings = existing.AiBuildings();
                if (aiBuildings != null)
                {
                    foreach (EntityController building in aiBuildings)
                    {
                        if (IsEligibleRepairTarget(building))
                        {
                            damagedCandidates.Add(building);
                        }
                    }
                }

                if (damagedCandidates.Count == 0)
                {
                    _nextRepairTickAt = now + 1.5f;
                    return;
                }

                // Sort candidates by priority: harvesters/buildings first if configured, then lowest health ratio
                damagedCandidates.Sort((a, b) =>
                {
                    if (_settings.PrioritizeHarvestersAndBuildings)
                    {
                        bool aPriority = a.IsBuilding || a.HasRole(UnitRole.Harvester);
                        bool bPriority = b.IsBuilding || b.HasRole(UnitRole.Harvester);
                        if (aPriority != bPriority)
                        {
                            return aPriority ? -1 : 1;
                        }
                    }

                    float aRatio = a.MaxHealth > 0.001f ? (a.CurrentHealth / a.MaxHealth) : 1.0f;
                    float bRatio = b.MaxHealth > 0.001f ? (b.CurrentHealth / b.MaxHealth) : 1.0f;
                    return aRatio.CompareTo(bRatio);
                });

                // Assign repairers respecting saturation limits
                var assignedCounts = new Dictionary<int, int>();
                for (int i = 0; i < repairers.Count; i++)
                {
                    EntityController repairer = repairers[i];
                    Vector3 repPos = repairer.Position;

                    EntityController bestTarget = null;
                    float bestDistSq = Single.MaxValue;

                    for (int j = 0; j < damagedCandidates.Count; j++)
                    {
                        EntityController target = damagedCandidates[j];
                        int currentAssigned = 0;
                        assignedCounts.TryGetValue(target.UniqueEntityId, out currentAssigned);
                        if (currentAssigned >= _settings.MaxAssignedRepairersPerTarget)
                        {
                            continue;
                        }

                        float distSq = (target.Position - repPos).sqrMagnitude;
                        if (distSq <= scanRadiusSq && distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            bestTarget = target;
                        }
                    }

                    if (bestTarget != null)
                    {
                        int count = 0;
                        assignedCounts.TryGetValue(bestTarget.UniqueEntityId, out count);
                        assignedCounts[bestTarget.UniqueEntityId] = count + 1;

                        // If not right next to target, dispatch movement toward target
                        if (bestDistSq > 16.0f) // > 4m distance
                        {
                            try
                            {
                                repairer.MoveTo(bestTarget.Position, true);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning("[RogueCommandExpansionLoader] Support repair could not direct '" +
                                    repairer.gameObject.name + "': " + ex.Message);
                            }
                        }
                    }
                }

                _nextRepairTickAt = now + _settings.CooldownSeconds;
            }

            private bool IsEligibleRepairTarget(EntityController candidate)
            {
                if (candidate == null || candidate.IsDestroyed || candidate.DestroyHasBeenStarted ||
                    !candidate.IsControlledByAi || candidate.canNotBeHealed || candidate.MaxHealth <= 0.001f)
                {
                    return false;
                }

                float healthRatio = candidate.CurrentHealth / candidate.MaxHealth;
                return healthRatio < _settings.HealthThresholdRatio;
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _commandTag = null;
            }
        }
    }
}
