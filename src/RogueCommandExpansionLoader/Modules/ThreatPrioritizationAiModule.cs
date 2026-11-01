using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

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

        internal sealed class ThreatPrioritizationRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly ThreatPrioritizationSettings _settings;
            private readonly string _commandTag;
            private float _nextCheckAt;
            private bool _stopped;
            private int _lastProcessedAiUnitCount;
            private int _lastProcessedAlliedUnitCount;
            private int _lastRetargetedCount;
            private int _totalRetargetCommandCount;
            private HashSet<EntityController> _autoControlledAlliedUnits;

            private static readonly object _registryLock = new object();
            private static readonly List<ThreatPrioritizationRuntimeBehaviour> _activeBehaviours =
                new List<ThreatPrioritizationRuntimeBehaviour>();

            public ThreatPrioritizationRuntimeBehaviour(DefaultAiBehaviour gameBehaviour, ThreatPrioritizationSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
                _commandTag = "roguecommand.expansionloader.threat-prioritization." + Guid.NewGuid().ToString("N");
                lock (_registryLock)
                {
                    _activeBehaviours.Add(this);
                }
            }

            public static int ActiveBehaviourCount
            {
                get { lock (_registryLock) { return _activeBehaviours.Count; } }
            }

            public static int AggregateLastProcessedAiUnitCount
            {
                get
                {
                    lock (_registryLock)
                    {
                        int sum = 0;
                        foreach (ThreatPrioritizationRuntimeBehaviour behaviour in _activeBehaviours)
                        {
                            if (behaviour != null && !behaviour._stopped)
                            {
                                sum += behaviour._lastProcessedAiUnitCount;
                            }
                        }
                        return sum;
                    }
                }
            }

            public static int AggregateLastProcessedAlliedUnitCount
            {
                get
                {
                    lock (_registryLock)
                    {
                        int sum = 0;
                        foreach (ThreatPrioritizationRuntimeBehaviour behaviour in _activeBehaviours)
                        {
                            if (behaviour != null && !behaviour._stopped)
                            {
                                sum += behaviour._lastProcessedAlliedUnitCount;
                            }
                        }
                        return sum;
                    }
                }
            }

            public static int AggregateLastRetargetedCount
            {
                get
                {
                    lock (_registryLock)
                    {
                        int sum = 0;
                        foreach (ThreatPrioritizationRuntimeBehaviour behaviour in _activeBehaviours)
                        {
                            if (behaviour != null && !behaviour._stopped)
                            {
                                sum += behaviour._lastRetargetedCount;
                            }
                        }
                        return sum;
                    }
                }
            }

            public static int AggregateTotalRetargetCommandCount
            {
                get
                {
                    lock (_registryLock)
                    {
                        int sum = 0;
                        foreach (ThreatPrioritizationRuntimeBehaviour behaviour in _activeBehaviours)
                        {
                            if (behaviour != null && !behaviour._stopped)
                            {
                                sum += behaviour._totalRetargetCommandCount;
                            }
                        }
                        return sum;
                    }
                }
            }

            public static void ResetThreatPrioritizationRegistryForTests()
            {
                lock (_registryLock)
                {
                    _activeBehaviours.Clear();
                }
            }

            public void OnInitialized()
            {
                _nextCheckAt = 0f;
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }
                float now = Time.time;
                if (now < _nextCheckAt)
                {
                    return;
                }
                _nextCheckAt = now + _settings.CheckIntervalSeconds;
                _lastRetargetedCount = 0;

                try
                {
                    ExistingControllers existing = ExistingControllers.Instance;
                    if (existing == null)
                    {
                        return;
                    }

                    HashSet<EntityController> aiUnits = existing.AiUnits();
                    int aiUnitCount = aiUnits != null ? aiUnits.Count : 0;
                    _lastProcessedAiUnitCount = aiUnitCount;

                    HashSet<EntityController> playerUnits = existing.PlayerUnits();
                    int alliedUnitCount = playerUnits != null ? playerUnits.Count : 0;
                    _lastProcessedAlliedUnitCount = alliedUnitCount;

                    // Track allied units observed by this module for diagnostics only.
                    // Do not mutate faction/control identity here: player (enemy) units must
                    // remain IsControlledByPlayer=true so other modules and IsEnemy checks
                    // keep treating them as hostile targets.
                    HashSet<EntityController> autoControlledAllies = new HashSet<EntityController>();
                    if (playerUnits != null && playerUnits.Count > 0)
                    {
                        foreach (EntityController ally in playerUnits)
                        {
                            if (ally != null)
                            {
                                autoControlledAllies.Add(ally);
                            }
                        }
                    }
                    _autoControlledAlliedUnits = autoControlledAllies;

                    // Evaluate and retarget AI combat units
                    if (aiUnits == null || aiUnits.Count == 0)
                    {
                        return;
                    }

                    HashSet<EntityController> playerEntities = existing.PlayerEntities() ?? playerUnits;
                    if (playerEntities == null || playerEntities.Count == 0)
                    {
                        return;
                    }

                    int retargetBudget = _settings.MaxRetargetsPerTick;

                    foreach (EntityController unit in aiUnits)
                    {
                        if (retargetBudget <= 0)
                        {
                            break;
                        }

                        if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted)
                        {
                            continue;
                        }

                        if (_settings.BaseIndex >= 0 && unit.aiBaseIndex != _settings.BaseIndex)
                        {
                            continue;
                        }

                        if (!unit.IsCombatUnit || !unit.CanAttackAndIsAttackEnabled)
                        {
                            continue;
                        }

                        // Specialize targeting per allied unit role: engagement envelope, distance
                        // weighting, and whether the unit even takes combat targets this tick.
                        ThreatPrioritizationRoleProfile roleProfile = ThreatPrioritizationRoleProfile.ResolveFor(unit);
                        if (!roleProfile.EngagesCombat)
                        {
                            continue;
                        }
                        float unitEngagementRadius = _settings.EngagementRadius * roleProfile.EngagementRangeMultiplier;
                        float engagementRadiusSq = unitEngagementRadius * unitEngagementRadius;

                        EntityController bestCandidate = null;
                        float bestScore = Single.NegativeInfinity;

                        foreach (EntityController candidate in playerEntities)
                        {
                            if (candidate == null ||
                                candidate.IsDestroyed ||
                                candidate.DestroyHasBeenStarted ||
                                candidate.canNotBeAttacked ||
                                candidate.canNotBeTargetedByAi)
                            {
                                continue;
                            }

                            if (!unit.IsEnemy(candidate))
                            {
                                continue;
                            }

                            float distSq = (candidate.Position - unit.Position).sqrMagnitude;
                            if (distSq > engagementRadiusSq)
                            {
                                continue;
                            }

                            float score = ComputeThreatScore(candidate, distSq, _settings.PriorityMode, roleProfile.DistanceWeight);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestCandidate = candidate;
                            }
                        }

                        if (bestCandidate == null)
                        {
                            continue;
                        }

                        // Hysteresis: compare against current target
                        EntityController currentTarget = unit.CurrentAttackTarget;
                        bool hasValidCurrentTarget = currentTarget != null &&
                            !currentTarget.IsDestroyed &&
                            !currentTarget.DestroyHasBeenStarted &&
                            !currentTarget.canNotBeAttacked &&
                            !currentTarget.canNotBeTargetedByAi &&
                            unit.IsEnemy(currentTarget);

                        if (hasValidCurrentTarget)
                        {
                            if (currentTarget == bestCandidate)
                            {
                                continue; // Already attacking best candidate
                            }

                            float curDistSq = (currentTarget.Position - unit.Position).sqrMagnitude;
                            if (curDistSq <= engagementRadiusSq * 1.44f)
                            {
                                float currentScore = ComputeThreatScore(currentTarget, curDistSq, _settings.PriorityMode, roleProfile.DistanceWeight);
                                // Require best candidate to beat current target by retarget threshold
                                if (bestScore <= currentScore * _settings.RetargetThreshold)
                                {
                                    continue;
                                }
                            }
                        }

                        if (unit.CurrentCommandIsTaggedWith(_commandTag) && unit.CurrentAttackTarget == bestCandidate)
                        {
                            continue;
                        }

                        EntityCommand attackCmd = EntityCommand.AttackMoveOnTarget(
                            bestCandidate,
                            EntityCommand.Role.ThisCommandWantsPathToBeCleared);
                        attackCmd.tag = _commandTag;

                        retargetBudget--;
                        try
                        {
                            unit.ExecuteCommand(attackCmd, EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                            _lastRetargetedCount++;
                            _totalRetargetCommandCount++;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning("[ThreatPrioritization] Failed to execute attack on " + unit.name + ": " + ex.Message);
                        }
                    }
                }
                catch
                {
                    // Guarded: diagnostics and retargeting must never break the tick.
                }
            }

            internal static float ComputeThreatScore(
                EntityController candidate,
                float distSq,
                string priorityMode,
                float distanceWeight)
            {
                float health = Mathf.Max(0.1f, candidate.CurrentHealth);
                float maxHealth = Mathf.Max(1f, candidate.MaxHealth);
                float healthFraction = Mathf.Clamp(health / maxHealth, 0f, 1f);

                float dps = 0f;
                try
                {
                    if (candidate.Attack1Cooldown > 0.01f && candidate.Damage1 > 0f)
                    {
                        dps += candidate.Damage1 / candidate.Attack1Cooldown;
                    }
                    if (candidate.Attack2Cooldown > 0.01f && candidate.Damage2 > 0f)
                    {
                        dps += candidate.Damage2 / candidate.Attack2Cooldown;
                    }
                }
                catch
                {
                    // Fallback
                }
                if (dps <= 0f)
                {
                    dps = candidate.IsCombatUnit ? 15f : 3f;
                }

                float roleBonus = 0f;
                if (candidate.HasRole(UnitRole.MassDestruction)) roleBonus += 45f;
                if (candidate.HasRole(UnitRole.Sniper)) roleBonus += 35f;
                if (candidate.HasRole(UnitRole.Turret)) roleBonus += 30f;
                if (candidate.HasRole(UnitRole.FrontalAttacker)) roleBonus += 25f;
                if (candidate.HasRole(UnitRole.Melee)) roleBonus += 20f;
                if (candidate.HasRole(UnitRole.Tank)) roleBonus += 10f;

                float rankBonus = candidate.CurrentRank * 10f;
                float distPenalty = distSq * 0.03f * Mathf.Max(0f, distanceWeight);

                if (string.Equals(priorityMode, "LowestHealth", StringComparison.OrdinalIgnoreCase))
                {
                    // Finisher priority: rewards missing HP percentage and low absolute HP
                    return ((1f - healthFraction) * 120f) + (500f / Mathf.Max(10f, health)) + (candidate.IsCombatUnit ? 25f : 0f) - distPenalty;
                }
                else if (string.Equals(priorityMode, "HighestDps", StringComparison.OrdinalIgnoreCase))
                {
                    // Pure damage dealer priority: eliminate highest DPS threats first
                    return (dps * 5f) + (roleBonus * 0.5f) + rankBonus - distPenalty;
                }
                else
                {
                    // HighestThreat default: composite threat combining DPS, high-impact roles, rank, and health
                    return (dps * 3f) + roleBonus + rankBonus + (candidate.MaxHealth * 0.1f) - distPenalty;
                }
            }

            public string CommandTag
            {
                get { return _commandTag; }
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

            public int LastRetargetedCount
            {
                get { return _lastRetargetedCount; }
            }

            public int TotalRetargetCommandCount
            {
                get { return _totalRetargetCommandCount; }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                lock (_registryLock)
                {
                    _activeBehaviours.Remove(this);
                }
            }
        }
    }
}
