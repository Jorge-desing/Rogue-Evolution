using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Forms tactical squad formations by binding escort units as followers to heavy anchor units (Captains).
    /// Leverages native EntityController.SetCaptain and EntityController.RemoveCaptain.
    /// </summary>
    public sealed class SquadFormationAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = SquadFormationParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            SquadFormationSettings settings;
            return SquadFormationParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            SquadFormationSettings settings;
            string diagnostic;
            if (!SquadFormationParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new SquadFormationRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class SquadFormationRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly SquadFormationSettings _settings;
            private readonly Dictionary<EntityController, EntityController> _moduleCaptainLinks =
                new Dictionary<EntityController, EntityController>();
            private float _nextCheckAt;
            private bool _stopped;

            public SquadFormationRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                SquadFormationSettings settings)
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
                UpdateSquadFormations();
            }

            public void OnDestroyed()
            {
                _stopped = true;
                ReleaseModuleCaptainLinks();
            }

            private void UpdateSquadFormations()
            {
                HashSet<EntityController> aiUnits = ExistingControllers.Instance != null
                    ? ExistingControllers.Instance.AiUnits()
                    : null;

                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return;
                }

                var captains = new List<EntityController>();
                var unassignedFollowers = new List<EntityController>();
                var followerCounts = new Dictionary<int, int>();

                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null || unit.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    if (_settings.BaseIndex >= 0 && unit.aiBaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    // Check if unit is a captain candidate
                    bool isAnchor = EntityBalancingStore.HasRole(unit.EntityId, GetUnitRoleEnum(_settings.AnchorRole));
                    if (isAnchor)
                    {
                        captains.Add(unit);
                        if (!followerCounts.ContainsKey(unit.UniqueEntityId))
                        {
                            followerCounts[unit.UniqueEntityId] = 0;
                        }
                    }
                    else
                    {
                        // Check existing captain validity
                        if (unit.CaptainOfThisEntity != null)
                        {
                            EntityController currentCaptain = unit.CaptainOfThisEntity;
                            EntityController ownedCaptain;
                            bool ownedByModule = _moduleCaptainLinks.TryGetValue(unit, out ownedCaptain);
                            if (ownedByModule && !ReferenceEquals(ownedCaptain, currentCaptain))
                            {
                                _moduleCaptainLinks.Remove(unit);
                                ownedByModule = false;
                            }

                            if (currentCaptain.DestroyHasBeenStarted)
                            {
                                if (ownedByModule)
                                {
                                    unit.RemoveCaptain(currentCaptain);
                                    _moduleCaptainLinks.Remove(unit);
                                    unassignedFollowers.Add(unit);
                                }
                            }
                            else
                            {
                                int captainId = currentCaptain.UniqueEntityId;
                                int count;
                                followerCounts.TryGetValue(captainId, out count);
                                followerCounts[captainId] = count + 1;
                            }
                        }
                        else
                        {
                            _moduleCaptainLinks.Remove(unit);
                            unassignedFollowers.Add(unit);
                        }
                    }
                }

                if (captains.Count == 0 || unassignedFollowers.Count == 0)
                {
                    return;
                }

                float maxRadiusSq = _settings.FormationRadiusCells * _settings.FormationRadiusCells;

                // Assign unassigned followers to the nearest eligible captain
                foreach (EntityController follower in unassignedFollowers)
                {
                    EntityController bestCaptain = null;
                    float bestDistSq = float.MaxValue;

                    for (int i = 0; i < captains.Count; i++)
                    {
                        EntityController captain = captains[i];
                        int currentCount;
                        followerCounts.TryGetValue(captain.UniqueEntityId, out currentCount);
                        if (currentCount >= _settings.MaxFollowersPerCaptain)
                        {
                            continue;
                        }

                        float distSq = (follower.Position - captain.Position).sqrMagnitude;
                        if (distSq <= maxRadiusSq && distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            bestCaptain = captain;
                        }
                    }

                    if (bestCaptain != null)
                    {
                        follower.SetCaptain(bestCaptain);
                        _moduleCaptainLinks[follower] = bestCaptain;
                        followerCounts[bestCaptain.UniqueEntityId]++;
                    }
                }
            }

            private void ReleaseModuleCaptainLinks()
            {
                foreach (KeyValuePair<EntityController, EntityController> link in _moduleCaptainLinks)
                {
                    EntityController follower = link.Key;
                    EntityController captain = link.Value;
                    if (follower != null && ReferenceEquals(follower.CaptainOfThisEntity, captain))
                    {
                        follower.RemoveCaptain(captain);
                    }
                }

                _moduleCaptainLinks.Clear();
            }

            private static UnitRole GetUnitRoleEnum(string roleName)
            {
                try
                {
                    return (UnitRole)Enum.Parse(typeof(UnitRole), roleName, true);
                }
                catch (Exception)
                {
                    return UnitRole.Tank;
                }
            }
        }
    }
}
