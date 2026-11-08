using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Runtime.Ai;
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

            _lastArbitrationResult = AiCommandArbiterPolicy.Arbitrate(
                _arbiterState,
                now: Time.time,
                retargetHysteresisMultiplier: DefaultRetargetHysteresisMultiplier,
                maxDispatchesPerTick: DefaultMaxArbitratedDispatchesPerTick,
                proposals: _proposalBuffer);

            IReadOnlyList<AiIntentProposal> approved = _lastArbitrationResult.ApprovedOrders;
            for (int i = 0; i < approved.Count; i++)
            {
                DispatchApprovedProposal(approved[i]);
            }
        }

        private void DispatchApprovedProposal(AiIntentProposal proposal)
        {
            if (!_battlefieldCollector.TryResolveAiEntity(proposal.UnitId, out EntityController unit) ||
                unit == null)
            {
                return;
            }

            if (proposal.RequiresEmptyQueue)
            {
                if (unit.CurrentPlayerCommandName != EntityCommand.Command.DoNothing)
                {
                    return;
                }

                bool hasQueue = false;
                IReadOnlyList<AiUnitSnapshot> aiUnits = _lastBattlefieldSnapshot.AiUnits;
                for (int i = 0; i < aiUnits.Count; i++)
                {
                    if (aiUnits[i].UniqueEntityId == proposal.UnitId)
                    {
                        hasQueue = aiUnits[i].HasQueuedCommands;
                        break;
                    }
                }

                if (hasQueue)
                {
                    return;
                }
            }

            try
            {
                EntityCommand command;
                if (proposal.Category == AiIntentCategory.SurvivalKite)
                {
                    command = EntityCommand.Move(
                        new Vector3(proposal.DestX, unit.Position.y, proposal.DestZ),
                        null,
                        EntityCommand.Role.ThisCommandWantsPathToBeCleared);
                }
                else if (proposal.Category == AiIntentCategory.TacticalSkill)
                {
                    if (proposal.TargetEntityId >= 0 &&
                        _battlefieldCollector.TryResolveHostileEntity(proposal.TargetEntityId, out EntityController skillTarget) &&
                        skillTarget != null)
                    {
                        command = EntityCommand.ActiveSkillOnTarget(skillTarget);
                    }
                    else
                    {
                        command = EntityCommand.ActiveSkillWithoutTarget();
                    }
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
                unit.ExecuteCommand(command, EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
            }
            catch
            {
                // Guarded: a single native dispatch failure must not abort the composite tick.
            }
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
    }
}
