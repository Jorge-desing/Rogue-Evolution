using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime.Ai
{
    /// <summary>
    /// Collects live battlefield state from <see cref="ExistingControllers"/> once per amortized window
    /// and projects it into an immutable <see cref="AiBattlefieldSnapshot"/>.
    /// Never mutates <c>IsControlledByPlayer</c> or <c>IsControlledByAi</c>.
    /// </summary>
    public sealed class RuntimeAiBattlefieldCollector
    {
        public const float DefaultRefreshIntervalSeconds = 0.20f;

        private static readonly FieldInfo NativeCommandChainField = ResolveNativeCommandChainField();

        private readonly int _baseIndex;
        private readonly float _refreshIntervalSeconds;
        private readonly Dictionary<int, EntityController> _aiEntityById = new Dictionary<int, EntityController>();
        private readonly Dictionary<int, EntityController> _hostileEntityById = new Dictionary<int, EntityController>();
        private AiBattlefieldSnapshot _lastSnapshot = AiBattlefieldSnapshot.Empty;
        private float _lastCollectionTime = -999f;

        public RuntimeAiBattlefieldCollector(
            int baseIndex = -1,
            float refreshIntervalSeconds = DefaultRefreshIntervalSeconds)
        {
            _baseIndex = baseIndex;
            _refreshIntervalSeconds = float.IsNaN(refreshIntervalSeconds) || float.IsInfinity(refreshIntervalSeconds)
                ? DefaultRefreshIntervalSeconds
                : Math.Max(0f, refreshIntervalSeconds);
        }

        public AiBattlefieldSnapshot LastSnapshot
        {
            get { return _lastSnapshot; }
        }

        public void Reset()
        {
            _aiEntityById.Clear();
            _hostileEntityById.Clear();
            _lastSnapshot = AiBattlefieldSnapshot.Empty;
            _lastCollectionTime = -999f;
        }

        public bool TryResolveAiEntity(int uniqueEntityId, out EntityController entity)
        {
            return _aiEntityById.TryGetValue(uniqueEntityId, out entity) &&
                   entity != null &&
                   !entity.IsDestroyed &&
                   !entity.DestroyHasBeenStarted;
        }

        public bool TryResolveHostileEntity(int uniqueEntityId, out EntityController entity)
        {
            return _hostileEntityById.TryGetValue(uniqueEntityId, out entity) &&
                   entity != null &&
                   !entity.IsDestroyed &&
                   !entity.DestroyHasBeenStarted;
        }

        public AiBattlefieldSnapshot CollectCurrentSnapshot(bool forceRefresh = false)
        {
            float now = Time.time;
            if (!forceRefresh &&
                _lastCollectionTime >= 0f &&
                now >= _lastCollectionTime &&
                (now - _lastCollectionTime) < _refreshIntervalSeconds)
            {
                return _lastSnapshot;
            }

            _lastCollectionTime = now;
            _aiEntityById.Clear();
            _hostileEntityById.Clear();

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    _lastSnapshot = AiBattlefieldSnapshot.Empty;
                    return _lastSnapshot;
                }

                var aiSnapshots = new List<AiUnitSnapshot>();
                EntityController referenceAiUnit = null;

                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits != null)
                {
                    foreach (EntityController unit in aiUnits)
                    {
                        if (!IsValidAiEntity(unit))
                        {
                            continue;
                        }

                        if (_baseIndex >= 0 && unit.aiBaseIndex != _baseIndex)
                        {
                            continue;
                        }

                        if (referenceAiUnit == null)
                        {
                            referenceAiUnit = unit;
                        }

                        _aiEntityById[unit.UniqueEntityId] = unit;
                        aiSnapshots.Add(ProjectEntity(unit));
                    }
                }

                try
                {
                    HashSet<EntityController> aiBuildings = existing.AiBuildings();
                    if (aiBuildings != null)
                    {
                        foreach (EntityController building in aiBuildings)
                        {
                            if (!IsValidAiEntity(building))
                            {
                                continue;
                            }

                            if (_baseIndex >= 0 && building.aiBaseIndex != _baseIndex)
                            {
                                continue;
                            }

                            if (referenceAiUnit == null)
                            {
                                referenceAiUnit = building;
                            }

                            if (!_aiEntityById.ContainsKey(building.UniqueEntityId))
                            {
                                _aiEntityById[building.UniqueEntityId] = building;
                                aiSnapshots.Add(ProjectEntity(building));
                            }
                        }
                    }
                }
                catch
                {
                    // Guard against optional AiBuildings registry failure
                }

                var hostileSnapshots = new List<AiUnitSnapshot>();
                HashSet<EntityController> playerEntities = existing.PlayerEntities() ?? existing.PlayerUnits();
                if (playerEntities != null)
                {
                    foreach (EntityController candidate in playerEntities)
                    {
                        if (!IsValidHostileCandidate(candidate, referenceAiUnit))
                        {
                            continue;
                        }

                        _hostileEntityById[candidate.UniqueEntityId] = candidate;
                        hostileSnapshots.Add(ProjectEntity(candidate));
                    }
                }

                int waveNumber = RuntimeAiDirectorCensus.Current().CurrentAttackWaveNumber;
                _lastSnapshot = AiBattlefieldBlackboardBuilder.BuildSnapshot(
                    timestamp: now,
                    baseIndex: _baseIndex,
                    currentWaveNumber: waveNumber,
                    aiUnits: aiSnapshots,
                    hostileUnits: hostileSnapshots);
                return _lastSnapshot;
            }
            catch
            {
                _lastSnapshot = AiBattlefieldSnapshot.Empty;
                return _lastSnapshot;
            }
        }

        private static bool IsValidAiEntity(EntityController entity)
        {
            return entity != null &&
                   !entity.IsDestroyed &&
                   !entity.DestroyHasBeenStarted &&
                   entity.IsControlledByAi &&
                   !entity.doNotRegisterGlobally;
        }

        private static bool IsValidHostileCandidate(EntityController candidate, EntityController referenceAiUnit)
        {
            if (candidate == null ||
                candidate.IsDestroyed ||
                candidate.DestroyHasBeenStarted ||
                !candidate.IsControlledByPlayer ||
                candidate.canNotBeAttacked ||
                candidate.canNotBeTargetedByAi ||
                candidate.doNotRegisterGlobally)
            {
                return false;
            }

            if (referenceAiUnit != null)
            {
                try
                {
                    if (referenceAiUnit.IsEnemy == null || !referenceAiUnit.IsEnemy(candidate))
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        private static AiUnitSnapshot ProjectEntity(EntityController entity)
        {
            float dps = 0f;
            try
            {
                if (entity.Attack1Cooldown > 0.01f && entity.Damage1 > 0f)
                {
                    dps += entity.Damage1 / entity.Attack1Cooldown;
                }

                if (entity.Attack2Cooldown > 0.01f && entity.Damage2 > 0f)
                {
                    dps += entity.Damage2 / entity.Attack2Cooldown;
                }
            }
            catch
            {
                dps = 0f;
            }

            if (dps <= 0f && entity.IsCombatUnit)
            {
                dps = 12f;
            }

            float minAttackRange = 0f;
            if (!string.IsNullOrWhiteSpace(entity.EntityId) &&
                RuntimeUnitMechanicsDirector.TryGetProfile(entity.EntityId, out UnitRuntimeMechanicsProfile profile) &&
                profile != null &&
                profile.MinAttackRange.HasValue &&
                profile.MinAttackRange.Value > 0f)
            {
                minAttackRange = profile.MinAttackRange.Value;
            }

            bool isHarvester = entity.HasRole(UnitRole.Harvester);
            bool isSupport = entity.IsEngineer || entity.HasRole(UnitRole.Supporter) || entity.HasRole(UnitRole.Heal);
            bool hasQueued = HasQueuedCommands(entity);
            int targetId = entity.CurrentAttackTarget != null ? entity.CurrentAttackTarget.UniqueEntityId : -1;

            Vector3 pos = entity.Position;
            return new AiUnitSnapshot(
                uniqueEntityId: entity.UniqueEntityId,
                entityId: entity.EntityId ?? string.Empty,
                baseIndex: entity.aiBaseIndex,
                posX: pos.x,
                posZ: pos.z,
                currentHealth: entity.CurrentHealth,
                maxHealth: entity.MaxHealth,
                currentShield: entity.CurrentShield,
                maxShield: entity.MaxShield,
                dpsEstimate: dps,
                weaponRange: entity.WeaponRange,
                minAttackRange: minAttackRange,
                moveSpeed: entity.MoveSpeed,
                roleFlags: (int)entity.Role,
                isControlledByAi: entity.IsControlledByAi,
                isControlledByPlayer: entity.IsControlledByPlayer,
                isCombatUnit: entity.IsCombatUnit && entity.CanAttackAndIsAttackEnabled,
                isHarvester: isHarvester,
                isBuilding: entity.IsBuilding,
                isSupportUnit: isSupport,
                hasQueuedCommands: hasQueued,
                currentCommandTag: string.Empty,
                currentTargetId: targetId);
        }

        private static bool HasQueuedCommands(EntityController entity)
        {
            if (entity == null || NativeCommandChainField == null)
            {
                return false;
            }

            try
            {
                LinkedList<EntityCommand> chain = NativeCommandChainField.GetValue(entity) as LinkedList<EntityCommand>;
                return chain != null && chain.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static FieldInfo ResolveNativeCommandChainField()
        {
            try
            {
                FieldInfo field = typeof(EntityController).GetField(
                    "_commandChain",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                return field != null &&
                       field.DeclaringType == typeof(EntityController) &&
                       field.IsPrivate &&
                       !field.IsStatic &&
                       field.FieldType == typeof(LinkedList<EntityCommand>)
                    ? field
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
