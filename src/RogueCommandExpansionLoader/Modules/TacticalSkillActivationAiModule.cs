using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Tactical AI module that monitors combat units with active skills and executes them
    /// when enemies are detected within range.
    /// Uses native EntityCommand.ActiveSkill* commands with ExecuteBeforeCurrentCommand.
    /// </summary>
    public sealed class TacticalSkillActivationAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = TacticalSkillActivationParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            TacticalSkillActivationSettings settings;
            return TacticalSkillActivationParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            TacticalSkillActivationSettings settings;
            string diagnostic;
            if (!TacticalSkillActivationParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new TacticalSkillActivationRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class TacticalSkillActivationRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const float LowHealthActivationThreshold = 0.5f;
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly TacticalSkillActivationSettings _settings;
            private float _nextCheckAt;
            private bool _stopped;

            public TacticalSkillActivationRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                TacticalSkillActivationSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
                _nextCheckAt = 0f;
                _stopped = false;
            }

            public void OnInitialized()
            {
                _nextCheckAt = Time.time + _settings.CooldownSeconds;
            }

            public void Tick()
            {
                if (_stopped || Time.time < _nextCheckAt)
                {
                    return;
                }

                _nextCheckAt = Time.time + _settings.CooldownSeconds;
                TriggerTacticalSkills();
            }

            public void OnDestroyed()
            {
                _stopped = true;
            }

            private static bool IsHostile(EntityController source, EntityController candidate)
            {
                try
                {
                    return source != null && candidate != null && source.IsEnemy(candidate);
                }
                catch
                {
                    return false;
                }
            }

            private void TriggerTacticalSkills()
            {
                if (ExistingControllers.Instance == null)
                {
                    return;
                }

                HashSet<EntityController> aiUnits = ExistingControllers.Instance.AiUnits();
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return;
                }

                HashSet<EntityController> playerEntities = ExistingControllers.Instance.PlayerEntities();
                float triggerRadiusSq = _settings.EnemyTriggerRadiusCells * _settings.EnemyTriggerRadiusCells;

                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted ||
                        !unit.IsControlledByAi || !unit.IsUnit)
                    {
                        continue;
                    }

                    if (_settings.BaseIndex >= 0 && unit.aiBaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    SkillInfo skillInfo = unit.SkillInfo;
                    if (skillInfo == null)
                    {
                        continue;
                    }

                    string activationTrigger = "EnemiesInRange";
                    if (RuntimeUnitMechanicsDirector.TryGetProfile(unit.EntityId, out UnitRuntimeMechanicsProfile mechanicsProfile) &&
                        mechanicsProfile != null && !string.IsNullOrWhiteSpace(mechanicsProfile.AiActivationTrigger))
                    {
                        activationTrigger = mechanicsProfile.AiActivationTrigger;
                    }

                    bool lowHealthTrigger = string.Equals(activationTrigger, "LowHealth", StringComparison.OrdinalIgnoreCase);
                    bool combatEngaged = string.Equals(activationTrigger, "CombatEngaged", StringComparison.OrdinalIgnoreCase);
                    EntityController engagedTarget = combatEngaged ? unit.CurrentAttackTarget : null;
                    if (combatEngaged && (!unit.IsTargetInRange || !IsEligibleEnemy(unit, engagedTarget)))
                    {
                        continue;
                    }
                    if (lowHealthTrigger && (unit.MaxHealth <= 0f ||
                        unit.CurrentHealth / unit.MaxHealth > LowHealthActivationThreshold))
                    {
                        continue;
                    }

                    // Check if already executing or queued for skill
                    if (unit.HasSkillActivationInCommandChain ||
                        unit.CurrentCommandIsTaggedWith("RCL_Skill_" + unit.UniqueEntityId))
                    {
                        continue;
                    }

                    // Check mana requirement
                    if (skillInfo.manaCost > unit.CurrentMana)
                    {
                        continue;
                    }

                    // Check mana ratio requirement
                    float maxMana = EntityBalancingStore.MaxMana(unit.EntityId);
                    if (maxMana > 0f)
                    {
                        float currentManaRatio = unit.CurrentMana / maxMana;
                        if (currentManaRatio < _settings.MinManaRatio)
                        {
                            continue;
                        }
                    }

                    // Self-targeted LowHealth skills do not need a nearby enemy.
                    EntityController nearestEnemy = engagedTarget;
                    if (!combatEngaged && (!lowHealthTrigger || skillInfo.targetOrigin != TargetOrigin.Self))
                    {
                        float nearestDistSq = float.MaxValue;
                        if (playerEntities != null)
                        {
                            foreach (EntityController enemy in playerEntities)
                            {
                                if (!IsEligibleEnemy(unit, enemy))
                                {
                                    continue;
                                }

                                float distSq = (unit.Position - enemy.Position).sqrMagnitude;
                                if (distSq <= triggerRadiusSq && distSq < nearestDistSq)
                                {
                                    nearestDistSq = distSq;
                                    nearestEnemy = enemy;
                                }
                            }
                        }

                        if (nearestEnemy == null)
                        {
                            continue;
                        }
                    }

                    // Execute appropriate active skill command based on origin
                    EntityCommand command = default;
                    bool hasCommand = false;
                    switch (skillInfo.targetOrigin)
                    {
                        case TargetOrigin.Self:
                            command = EntityCommand.ActiveSkillWithoutTarget();
                            hasCommand = true;
                            break;
                        case TargetOrigin.ChosenEntity:
                            command = EntityCommand.ActiveSkillOnTarget(nearestEnemy);
                            hasCommand = true;
                            break;
                        case TargetOrigin.ChosenLocation:
                            command = EntityCommand.ActiveSkillAtPosition(nearestEnemy.Position);
                            hasCommand = true;
                            break;
                    }

                    if (hasCommand)
                    {
                        command.tag = "RCL_Skill_" + unit.UniqueEntityId;
                        unit.ExecuteCommand(command, EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                    }
                }
            }

            private static bool IsEligibleEnemy(EntityController unit, EntityController enemy)
            {
                return enemy != null && !enemy.IsDestroyed && !enemy.DestroyHasBeenStarted &&
                    !enemy.canNotBeTargetedByAi && IsHostile(unit, enemy);
            }
        }
    }
}
