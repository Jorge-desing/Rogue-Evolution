using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Tactical AI module that leverages runtime gameplay mechanics (Command Aura, Elite Affixes,
    /// Veterancy XP, Tactical Stances, Unit Loadouts, and AI Doctrines) to coordinate AI squads.
    /// </summary>
    public sealed class AdaptiveCommanderAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = AdaptiveCommanderParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            AdaptiveCommanderSettings settings;
            return AdaptiveCommanderParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            AdaptiveCommanderSettings settings;
            string diagnostic;
            if (!AdaptiveCommanderParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new AdaptiveCommanderRuntimeBehaviour(gameBehaviour, settings);
        }

        internal sealed class AdaptiveCommanderRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly AdaptiveCommanderSettings _settings;
            private float _nextScanAt;
            private bool _stopped;
            private string _activeCommanderKeyId;
            private readonly HashSet<string> _loadoutInitializedKeys = new HashSet<string>(StringComparer.Ordinal);

            public AdaptiveCommanderRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                AdaptiveCommanderSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
                _nextScanAt = 0f;
                _stopped = false;
                _activeCommanderKeyId = null;
            }

            public void OnInitialized()
            {
                _nextScanAt = 0f;
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }

                float now = Time.time;
                if (now < _nextScanAt)
                {
                    return;
                }

                _nextScanAt = now + _settings.ScanIntervalSeconds;

                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return;
                }

                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return;
                }

                var eligibleUnits = new List<EntityController>();
                EntityController bestCommander = null;
                float bestVitality = -1f;

                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted ||
                        !unit.IsControlledByAi || !unit.IsUnit || !unit.IsCombatUnit ||
                        !unit.CanAttackAndIsAttackEnabled || !unit.CanMoveAndMovementIsEnabled ||
                        unit.IsBuilding || unit.canNotBeAttacked)
                    {
                        continue;
                    }

                    if (_settings.BaseIndex >= 0 && unit.aiBaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    eligibleUnits.Add(unit);

                    float vitality = GetCombinedMaxVitality(unit);
                    if (vitality > bestVitality)
                    {
                        bestVitality = vitality;
                        bestCommander = unit;
                    }
                }

                if (eligibleUnits.Count == 0 || bestCommander == null)
                {
                    return;
                }

                string commanderKey = bestCommander.UniqueEntityId.ToString(CultureInfo.InvariantCulture);
                if (!string.Equals(_activeCommanderKeyId, commanderKey, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(_activeCommanderKeyId))
                    {
                        RuntimeGameplayCommandDirector.TryClearCommandAuraLinks(_activeCommanderKeyId);
                    }
                    _activeCommanderKeyId = commanderKey;
                    RuntimeGameplayCommandDirector.TryApplyUnitAffix(commanderKey, _settings.CommanderAffix);
                }

                if (_settings.XpGrantPerPulse > 0)
                {
                    int resultingXp;
                    int resultingRank;
                    RuntimeUnitMechanicsDirector.TryGrantCombatXp(bestCommander, _settings.XpGrantPerPulse, out resultingXp, out resultingRank);
                }

                RuntimeGameplayCommandDirector.TryDispatchCommandAuraPulse(
                    commanderKey,
                    _settings.AuraType,
                    _settings.CommandRadius,
                    Math.Max(1.0f, _settings.ScanIntervalSeconds * 2.0f));

                for (int i = 0; i < eligibleUnits.Count; i++)
                {
                    EntityController squadUnit = eligibleUnits[i];
                    string unitKey = squadUnit.UniqueEntityId.ToString(CultureInfo.InvariantCulture);

                    if (!_loadoutInitializedKeys.Contains(unitKey))
                    {
                        _loadoutInitializedKeys.Add(unitKey);
                        if (!string.Equals(_settings.PreferredLoadout, "Standard", StringComparison.OrdinalIgnoreCase))
                        {
                            RuntimeGameplayCommandDirector.TryApplyUnitLoadout(unitKey, _settings.PreferredLoadout);
                        }
                    }

                    float hpRatio = GetHealthRatio(squadUnit);
                    string targetStance = "Standard";
                    if (hpRatio >= _settings.HealthyStanceThreshold)
                    {
                        targetStance = "Aggressive";
                    }
                    else if (hpRatio <= _settings.DamagedStanceThreshold)
                    {
                        targetStance = "Defensive";
                    }

                    string currentStance = RuntimeGameplayCommandDirector.GetUnitTacticalStance(unitKey);
                    if (!string.Equals(currentStance, targetStance, StringComparison.OrdinalIgnoreCase))
                    {
                        RuntimeGameplayCommandDirector.TrySetUnitTacticalStance(unitKey, targetStance);
                    }
                }

                if (_settings.AdaptiveDoctrine)
                {
                    int confirmedHostiles = CountConfirmedHostileUnits(existing, bestCommander);
                    if (eligibleUnits.Count >= confirmedHostiles)
                    {
                        RuntimeGameplayCommandDirector.TrySetAiDoctrine("Assault", 1.25f);
                    }
                    else
                    {
                        RuntimeGameplayCommandDirector.TrySetAiDoctrine("Fortify", 0.90f);
                    }
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                if (!string.IsNullOrEmpty(_activeCommanderKeyId))
                {
                    RuntimeGameplayCommandDirector.TryClearCommandAuraLinks(_activeCommanderKeyId);
                    _activeCommanderKeyId = null;
                }
                _loadoutInitializedKeys.Clear();
            }

            private static float GetCombinedMaxVitality(EntityController unit)
            {
                if (unit == null)
                {
                    return 0f;
                }

                float maxHp = Math.Max(0f, unit.MaxHealth);
                float curHp = Math.Max(0f, unit.CurrentHealth);
                float maxArmor = Math.Max(0f, unit.MaxArmor);
                return curHp + maxHp * 0.5f + maxArmor * 0.5f;
            }

            private static float GetHealthRatio(EntityController unit)
            {
                if (unit == null || unit.MaxHealth <= 0.001f)
                {
                    return 1.0f;
                }

                float ratio = unit.CurrentHealth / unit.MaxHealth;
                if (float.IsNaN(ratio) || float.IsInfinity(ratio))
                {
                    return 1.0f;
                }
                return Math.Max(0f, Math.Min(1f, ratio));
            }

            private static int CountConfirmedHostileUnits(ExistingControllers existing, EntityController referenceUnit)
            {
                if (existing == null || referenceUnit == null || referenceUnit.IsEnemy == null)
                {
                    return 0;
                }

                HashSet<EntityController> playerEntities = existing.PlayerEntities();
                if (playerEntities == null || playerEntities.Count == 0)
                {
                    return 0;
                }

                int count = 0;
                foreach (EntityController candidate in playerEntities)
                {
                    if (candidate == null || candidate.IsDestroyed || candidate.DestroyHasBeenStarted ||
                        !candidate.IsControlledByPlayer || !candidate.IsUnit || !candidate.IsCombatUnit ||
                        candidate.IsBuilding || candidate.canNotBeAttacked || candidate.canNotBeTargetedByAi)
                    {
                        continue;
                    }

                    bool isHostile = false;
                    try
                    {
                        isHostile = referenceUnit.IsEnemy(candidate);
                    }
                    catch
                    {
                        isHostile = false;
                    }

                    if (isHostile)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }
}
