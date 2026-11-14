using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Runtime.Ai;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Runs an ordered collection of compiled AI modules against one game-owned AI instance.
    /// Lifecycle callbacks are dispatched once and cleanup runs in reverse order.
    /// When one or more behaviours implement <see cref="ICoordinatedAiRuntimeBehaviour"/>,
    /// shares a single amortized <see cref="AiBattlefieldSnapshot"/> and arbitrates competing
    /// <see cref="AiIntentProposal"/> entries through <see cref="AiCommandArbiterPolicy"/>.
    /// </summary>
    internal sealed class CompositeAiRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
    {
        public const float DefaultRetargetHysteresisMultiplier = 1.15f;
        public const int DefaultMaxArbitratedDispatchesPerTick = 12;

        private static readonly object _activeCompositesLock = new object();
        private static readonly List<CompositeAiRuntimeBehaviour> _activeComposites =
            new List<CompositeAiRuntimeBehaviour>();

        private readonly string _profileId;
        private readonly List<string> _moduleIds;
        private readonly List<IRogueCommandAiRuntimeBehaviour> _behaviours;
        private readonly List<ICoordinatedAiRuntimeBehaviour> _coordinatedBehaviours;
        private readonly List<string> _coordinatedModuleIds;
        private readonly RuntimeAiBattlefieldCollector _battlefieldCollector;
        private readonly AiCommandArbiterState _arbiterState;
        private readonly List<AiIntentProposal> _proposalBuffer;
        private AiBattlefieldSnapshot _lastBattlefieldSnapshot = AiBattlefieldSnapshot.Empty;
        private AiDoctrineWeights _lastDoctrineWeights = AiDoctrineWeights.Skirmish;
        private AiArbitrationResult _lastArbitrationResult;
        private bool _initialized;
        private bool _destroyed;

        public CompositeAiRuntimeBehaviour(
            string profileId,
            IList<string> moduleIds,
            IList<IRogueCommandAiRuntimeBehaviour> behaviours)
        {
            if (moduleIds == null)
            {
                throw new ArgumentNullException("moduleIds");
            }

            if (behaviours == null)
            {
                throw new ArgumentNullException("behaviours");
            }

            if (moduleIds.Count != behaviours.Count)
            {
                throw new ArgumentException("Each AI runtime behaviour must have one module ID.");
            }

            _profileId = profileId ?? "<unknown>";
            _moduleIds = new List<string>(moduleIds.Count);
            _behaviours = new List<IRogueCommandAiRuntimeBehaviour>(behaviours.Count);
            _coordinatedBehaviours = new List<ICoordinatedAiRuntimeBehaviour>();
            _coordinatedModuleIds = new List<string>();
            for (int i = 0; i < behaviours.Count; i++)
            {
                if (String.IsNullOrWhiteSpace(moduleIds[i]) || behaviours[i] == null)
                {
                    throw new ArgumentException("AI runtime modules and their IDs cannot be null or empty.");
                }

                _moduleIds.Add(moduleIds[i]);
                _behaviours.Add(behaviours[i]);
                if (behaviours[i] is ICoordinatedAiRuntimeBehaviour coordinated)
                {
                    _coordinatedBehaviours.Add(coordinated);
                    _coordinatedModuleIds.Add(moduleIds[i]);
                }
            }

            _battlefieldCollector = new RuntimeAiBattlefieldCollector(baseIndex: -1);
            _arbiterState = new AiCommandArbiterState();
            _proposalBuffer = new List<AiIntentProposal>();
        }

        public AiBattlefieldSnapshot LastBattlefieldSnapshot
        {
            get { return _lastBattlefieldSnapshot; }
        }

        public AiArbitrationResult LastArbitrationResult
        {
            get { return _lastArbitrationResult; }
        }

        internal AiCommandArbiterState ArbiterState
        {
            get { return _arbiterState; }
        }

        public void OnInitialized()
        {
            if (_destroyed)
            {
                throw new InvalidOperationException("Cannot initialize a destroyed AI module stack.");
            }

            if (_initialized)
            {
                return;
            }

            for (int i = 0; i < _behaviours.Count; i++)
            {
                if (_destroyed)
                {
                    return;
                }

                try
                {
                    _behaviours[i].OnInitialized();
                }
                catch (Exception exception)
                {
                    var cleanupFailures = new List<Exception>();
                    if (!_destroyed)
                    {
                        _destroyed = true;
                        lock (_activeCompositesLock)
                        {
                            _activeComposites.Remove(this);
                        }

                        for (int cleanupIndex = _behaviours.Count - 1; cleanupIndex >= 0; cleanupIndex--)
                        {
                            Exception cleanupFailure = TryDestroyAt(cleanupIndex, "initialization rollback");
                            if (cleanupFailure != null)
                            {
                                cleanupFailures.Add(cleanupFailure);
                            }
                        }
                    }

                    var initializationFailure = new InvalidOperationException(
                        "AI module '" + _moduleIds[i] + "' failed to initialize for profile '" +
                        _profileId + "'.", exception);
                    if (cleanupFailures.Count == 0)
                    {
                        throw initializationFailure;
                    }

                    var failures = new List<Exception>(cleanupFailures.Count + 1);
                    failures.Add(initializationFailure);
                    failures.AddRange(cleanupFailures);
                    throw new AggregateException(
                        "AI module initialization failed and one or more modules also failed during cleanup.",
                        failures);
                }
            }

            if (_destroyed)
            {
                return;
            }

            _initialized = true;
            lock (_activeCompositesLock)
            {
                if (!_activeComposites.Contains(this))
                {
                    _activeComposites.Add(this);
                }
            }
        }

        internal static void NotifyBattlefieldRegistryReset()
        {
            lock (_activeCompositesLock)
            {
                for (int i = 0; i < _activeComposites.Count; i++)
                {
                    CompositeAiRuntimeBehaviour instance = _activeComposites[i];
                    if (instance == null || instance._destroyed)
                    {
                        continue;
                    }

                    instance._arbiterState.Clear();
                    instance._battlefieldCollector.Reset();
                    instance._lastBattlefieldSnapshot = AiBattlefieldSnapshot.Empty;
                    instance._lastArbitrationResult = null;
                }
            }
        }

        internal static void NotifyUnitLeaseInvalidated(int unitId)
        {
            if (unitId <= 0)
            {
                return;
            }

            lock (_activeCompositesLock)
            {
                for (int i = 0; i < _activeComposites.Count; i++)
                {
                    CompositeAiRuntimeBehaviour instance = _activeComposites[i];
                    if (instance == null || instance._destroyed)
                    {
                        continue;
                    }

                    instance._arbiterState.ReleaseLease(unitId);
                }
            }
        }

        internal static void NotifyUnitCommandExecuted(int unitId, string commandTag)
        {
            if (unitId <= 0)
            {
                return;
            }

            lock (_activeCompositesLock)
            {
                for (int i = 0; i < _activeComposites.Count; i++)
                {
                    CompositeAiRuntimeBehaviour instance = _activeComposites[i];
                    if (instance == null || instance._destroyed)
                    {
                        continue;
                    }

                    instance._arbiterState.ReleaseLeaseIfTagDiffers(unitId, commandTag);
                }
            }
        }

        internal static AiArchitectureCensus BuildArchitectureCensus(int maxTrackedInstances)
        {
            int cap = Math.Max(1, maxTrackedInstances);
            lock (_activeCompositesLock)
            {
                var rows = new List<AiArchitectureInstanceDiagnosticRow>();
                int omitted = 0;
                int activeCount = 0;
                int totalCoord = 0;
                int totalHazards = 0;
                int totalApproved = 0;
                int totalConflicts = 0;
                int totalLockout = 0;
                int totalHysteresis = 0;
                int totalBudgetOmitted = 0;

                for (int i = 0; i < _activeComposites.Count; i++)
                {
                    CompositeAiRuntimeBehaviour instance = _activeComposites[i];
                    if (instance == null || instance._destroyed || !instance._initialized)
                    {
                        continue;
                    }

                    activeCount++;
                    int coordCount = instance._coordinatedBehaviours.Count;
                    totalCoord += coordCount;

                    AiBattlefieldSnapshot snap = instance._lastBattlefieldSnapshot ?? AiBattlefieldSnapshot.Empty;
                    int hazardCount = snap.HazardZones != null ? snap.HazardZones.Count : 0;
                    totalHazards += hazardCount;

                    AiArbitrationResult arb = instance._lastArbitrationResult;
                    int approved = arb != null ? arb.ApprovedOrders.Count : 0;
                    int conflicts = arb != null ? arb.ConflictsResolvedCount : 0;
                    int lockout = arb != null ? arb.LockoutBlockedCount : 0;
                    int hysteresis = arb != null ? arb.HysteresisBlockedCount : 0;
                    int budgetOmitted = arb != null ? arb.BudgetOmittedCount : 0;

                    totalApproved += approved;
                    totalConflicts += conflicts;
                    totalLockout += lockout;
                    totalHysteresis += hysteresis;
                    totalBudgetOmitted += budgetOmitted;

                    if (rows.Count < cap)
                    {
                        rows.Add(new AiArchitectureInstanceDiagnosticRow(
                            profileId: instance._profileId,
                            coordinatedModuleCount: coordCount,
                            activeDoctrine: instance._lastDoctrineWeights.DoctrineName ?? "Standard",
                            trackedAiCombatUnits: snap.TotalAiCombatUnits,
                            trackedHostileCombatUnits: snap.TotalHostileCombatUnits,
                            underHarvesterOrBasePressure: snap.UnderHarvesterOrBasePressure,
                            approvedOrdersLastTick: approved,
                            conflictsResolvedLastTick: conflicts,
                            lockoutBlockedLastTick: lockout,
                            hysteresisBlockedLastTick: hysteresis,
                            budgetOmittedLastTick: budgetOmitted,
                            trackedHazardZones: hazardCount));
                    }
                    else
                    {
                        omitted++;
                    }
                }

                return new AiArchitectureCensus(
                    omittedInstanceCount: omitted,
                    activeCompositeCount: activeCount,
                    totalCoordinatedModuleCount: totalCoord,
                    totalApprovedOrdersLastTick: totalApproved,
                    totalConflictsResolvedLastTick: totalConflicts,
                    totalLockoutBlockedLastTick: totalLockout,
                    totalHysteresisBlockedLastTick: totalHysteresis,
                    totalBudgetOmittedLastTick: totalBudgetOmitted,
                    instances: rows.AsReadOnly(),
                    totalTrackedHazardZones: totalHazards);
            }
        }

        public void Tick()
        {
            if (_destroyed || !_initialized)
            {
                return;
            }

            for (int i = 0; i < _behaviours.Count; i++)
            {
                if (_destroyed)
                {
                    return;
                }

                try
                {
                    _behaviours[i].Tick();
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "AI module '" + _moduleIds[i] + "' failed during Tick for profile '" +
                        _profileId + "'.", exception);
                }
            }

            if (!_destroyed && _coordinatedBehaviours.Count > 0)
            {
                ExecuteCoordinatedArbitrationPass();
            }
        }

        private void ExecuteCoordinatedArbitrationPass()
        {
            _proposalBuffer.Clear();
            _lastBattlefieldSnapshot = _battlefieldCollector.CollectCurrentSnapshot();
            _lastDoctrineWeights = StrategicDoctrinePolicy.Evaluate(_lastBattlefieldSnapshot);

            for (int i = 0; i < _coordinatedBehaviours.Count; i++)
            {
                if (_destroyed)
                {
                    return;
                }

                try
                {
                    _coordinatedBehaviours[i].CollectProposals(
                        _lastBattlefieldSnapshot,
                        _lastDoctrineWeights,
                        _proposalBuffer);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "AI module '" + _coordinatedModuleIds[i] + "' failed during CollectProposals for profile '" +
                        _profileId + "'.", exception);
                }
            }

            // A completed or replaced native command must not keep its retarget protection.
            foreach (AiUnitSnapshot snapshot in _lastBattlefieldSnapshot.AiUnits)
            {
                int unitId = snapshot.UniqueEntityId;
                if (!_arbiterState.TryGetLease(unitId, out AiCommandArbiterState.UnitLease lease) ||
                    !_battlefieldCollector.TryResolveAiEntity(unitId, out EntityController unit) ||
                    unit == null)
                {
                    continue;
                }

                if (unit.CurrentPlayerCommandName == EntityCommand.Command.DoNothing ||
                    !unit.CurrentCommandIsTaggedWith(lease.CommandTag))
                {
                    _arbiterState.ReleaseLease(unitId);
                }
            }

            _lastArbitrationResult = AiCommandArbiterPolicy.Arbitrate(
                _arbiterState,
                now: Time.time,
                retargetHysteresisMultiplier: DefaultRetargetHysteresisMultiplier,
                maxDispatchesPerTick: DefaultMaxArbitratedDispatchesPerTick,
                proposals: _proposalBuffer,
                tryDispatch: DispatchApprovedProposal,
                keepsNativeOrder: proposal => proposal.Category != AiIntentCategory.SupportLogistics ||
                    (_battlefieldCollector.TryResolveAiEntity(proposal.UnitId, out EntityController unit) &&
                     unit != null && unit.CurrentCommandIsTaggedWith(proposal.CommandTag)));
        }

        private bool DispatchApprovedProposal(AiIntentProposal proposal)
        {
            if (!_battlefieldCollector.TryResolveAiEntity(proposal.UnitId, out EntityController unit) ||
                unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted || !unit.IsControlledByAi)
            {
                return false;
            }

            if (proposal.RequiresEmptyQueue &&
                (unit.CurrentPlayerCommandName != EntityCommand.Command.DoNothing ||
                 !RuntimeAiBattlefieldCollector.TryReadQueuedCommands(unit, out bool hasQueue) ||
                 hasQueue))
            {
                return false;
            }

            try
            {
                if (HasActiveOrderFromAnotherProfile(unit, proposal.UnitId))
                {
                    return false;
                }

                EntityCommand command;
                EntityController.CommandProcessingType processingType =
                    EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand;
                if (proposal.Category == AiIntentCategory.SurvivalKite)
                {
                    command = EntityCommand.Move(
                        new Vector3(proposal.DestX, unit.Position.y, proposal.DestZ),
                        null,
                        EntityCommand.Role.ThisCommandWantsPathToBeCleared);
                }
                else if (proposal.Category == AiIntentCategory.SupportLogistics)
                {
                    if (!_battlefieldCollector.TryResolveAiEntity(proposal.TargetEntityId, out EntityController patient) ||
                        patient == null || patient == unit || patient.IsDestroyed || patient.DestroyHasBeenStarted ||
                        !patient.IsControlledByAi || unit.IsEnemy(patient))
                    {
                        return false;
                    }

                    bool heal = unit.HealAmount1 > 0f && !patient.canNotBeHealed && patient.DeltaHealth > 0.01f;
                    bool repair = (unit.IsEngineer || unit.HasRole(UnitRole.Engineer)) && patient.DeltaArmor > 0.01f;
                    if (!heal && !repair)
                    {
                        return false;
                    }

                    if (WithinHorizontalRange(unit.Position, patient.Position, 16f))
                    {
                        if (unit.CurrentCommandIsTaggedWith(proposal.CommandTag))
                        {
                            unit.ExecuteCommand(EntityCommand.Stop(),
                                EntityController.CommandProcessingType.ExecuteAndCancelCurrentCommand);
                            _arbiterState.ReleaseLease(proposal.UnitId);
                        }

                        if (heal) patient.Heal(unit.HealAmount1, unit);
                        if (repair) patient.RepairArmor(6f, unit, false);
                        return true;
                    }

                    command = EntityCommand.Move(patient.Position);
                    if (unit.CurrentCommandIsTaggedWith(proposal.CommandTag))
                    {
                        processingType = EntityController.CommandProcessingType.ExecuteAndCancelCurrentCommand;
                    }
                }
                else if (proposal.Category == AiIntentCategory.TacticalSkill)
                {
                    SkillInfo skillInfo = unit.SkillInfo;
                    if (skillInfo == null || unit.HasSkillActivationInCommandChain ||
                        unit.CurrentMana < skillInfo.manaCost)
                    {
                        return false;
                    }
                    RuntimeUnitMechanicsDirector.TryGetProfile(unit.EntityId, out UnitRuntimeMechanicsProfile profile);
                    string trigger = profile?.AiActivationTrigger;
                    bool lowHealth = string.Equals(trigger, "LowHealth", StringComparison.OrdinalIgnoreCase);
                    bool combatEngaged = string.Equals(trigger, "CombatEngaged", StringComparison.OrdinalIgnoreCase);
                    if (lowHealth && !(unit.MaxHealth > 0f && unit.CurrentHealth <= unit.MaxHealth * 0.5f))
                    {
                        return false;
                    }

                    if (combatEngaged)
                    {
                        EntityController engaged = unit.CurrentAttackTarget;
                        if (!unit.IsTargetInRange || engaged == null || engaged.IsDestroyed || engaged.DestroyHasBeenStarted ||
                            engaged.canNotBeTargetedByAi || !unit.IsEnemy(engaged) ||
                            (skillInfo.targetOrigin != TargetOrigin.Self && engaged.UniqueEntityId != proposal.TargetEntityId))
                        {
                            return false;
                        }
                    }

                    if (skillInfo.targetOrigin == TargetOrigin.Self)
                    {
                        if (!lowHealth && !combatEngaged &&
                            (!TryResolveSkillHostile(unit, proposal.TargetEntityId, out EntityController nearby) ||
                             !WithinHorizontalRange(unit.Position, nearby.Position, unit.SqrSightRadius)))
                        {
                            return false;
                        }

                        command = EntityCommand.ActiveSkillWithoutTarget();
                    }
                    else
                    {
                        if (!TryResolveSkillHostile(unit, proposal.TargetEntityId, out EntityController skillTarget) ||
                            (!unit.SkillRangeIsUnlimited &&
                             !(skillInfo.targetOrigin == TargetOrigin.ChosenLocation && unit.activateSkillAtOnceIndependentOfSkillRadius) &&
                             !WithinHorizontalRange(unit.Position, skillTarget.Position, unit.SqrSkillRange)))
                        {
                            return false;
                        }

                        switch (skillInfo.targetOrigin)
                        {
                            case TargetOrigin.ChosenEntity:
                                command = EntityCommand.ActiveSkillOnTarget(skillTarget);
                                break;
                            case TargetOrigin.ChosenLocation:
                                command = EntityCommand.ActiveSkillAtPosition(skillTarget.Position);
                                break;
                            default:
                                return false;
                        }
                    }
                }
                else if (proposal.Category == AiIntentCategory.FlankingManeuver)
                {
                    command = EntityCommand.AttackMoveOnPosition(
                        new Vector3(proposal.DestX, unit.Position.y, proposal.DestZ),
                        EntityCommand.Role.ThisCommandWantsPathToBeCleared,
                        proposal.ModuleId);
                }
                else if (proposal.TargetEntityId >= 0 &&
                         _battlefieldCollector.TryResolveHostileEntity(proposal.TargetEntityId, out EntityController hostileTarget) &&
                         hostileTarget != null)
                {
                    command = EntityCommand.AttackMoveOnTarget(
                        hostileTarget,
                        EntityCommand.Role.ThisCommandWantsPathToBeCleared);
                }
                else
                {
                    command = EntityCommand.AttackMoveOnPosition(
                        new Vector3(proposal.DestX, unit.Position.y, proposal.DestZ),
                        EntityCommand.Role.ThisCommandWantsPathToBeCleared,
                        proposal.ModuleId);
                }

                command.tag = proposal.CommandTag;
                unit.ExecuteCommand(command, processingType);
                return true;
            }
            catch
            {
                // Guarded: a single native dispatch failure must not abort the composite tick.
                return false;
            }
        }

        private bool HasActiveOrderFromAnotherProfile(EntityController unit, int unitId)
        {
            if (unit.CurrentPlayerCommandName == EntityCommand.Command.DoNothing)
            {
                return false;
            }

            lock (_activeCompositesLock)
            {
                foreach (CompositeAiRuntimeBehaviour profile in _activeComposites)
                {
                    if (!ReferenceEquals(profile, this) &&
                        profile._arbiterState.TryGetLease(unitId, out AiCommandArbiterState.UnitLease lease) &&
                        unit.CurrentCommandIsTaggedWith(lease.CommandTag))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void OnDestroyed()
        {
            if (_destroyed)
            {
                return;
            }

            _destroyed = true;
            lock (_activeCompositesLock)
            {
                _activeComposites.Remove(this);
            }

            _proposalBuffer.Clear();
            _arbiterState.Clear();
            _battlefieldCollector.Reset();
            var cleanupFailures = new List<Exception>();
            for (int i = _behaviours.Count - 1; i >= 0; i--)
            {
                Exception cleanupFailure = TryDestroyAt(i, "destruction");
                if (cleanupFailure != null)
                {
                    cleanupFailures.Add(cleanupFailure);
                }
            }

            if (cleanupFailures.Count == 1)
            {
                throw cleanupFailures[0];
            }

            if (cleanupFailures.Count > 1)
            {
                throw new AggregateException(
                    "Multiple AI modules failed to clean up for profile '" + _profileId + "'.",
                    cleanupFailures);
            }
        }

        private Exception TryDestroyAt(int index, string phase)
        {
            try
            {
                _behaviours[index].OnDestroyed();
                return null;
            }
            catch (Exception exception)
            {
                return new InvalidOperationException(
                    "AI module '" + _moduleIds[index] + "' failed to clean up during " + phase +
                    " for profile '" + _profileId + "'.", exception);
            }
        }
        private bool TryResolveSkillHostile(EntityController unit, int targetId, out EntityController target)
        {
            target = null;
            return targetId >= 0 && _battlefieldCollector.TryResolveHostileEntity(targetId, out target) &&
                target != null && !target.IsDestroyed && !target.DestroyHasBeenStarted &&
                !target.canNotBeTargetedByAi && unit.IsEnemy(target);
        }

        private static bool WithinHorizontalRange(Vector3 source, Vector3 target, float squaredRange)
        {
            float dx = target.x - source.x;
            float dz = target.z - source.z;
            return dx * dx + dz * dz <= squaredRange;
        }

    }
}
