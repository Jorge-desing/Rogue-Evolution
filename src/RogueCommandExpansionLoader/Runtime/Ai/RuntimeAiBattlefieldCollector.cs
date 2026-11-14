using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime.Ai
{
    /// <summary>
    /// Collects live battlefield state from <see cref="ExistingControllers"/> and <see cref="RuntimeHazardManager"/>
    /// once per amortized window and projects it into an immutable <see cref="AiBattlefieldSnapshot"/>.
    /// Never mutates <c>IsControlledByPlayer</c> or <c>IsControlledByAi</c>.
    /// </summary>
    public sealed class RuntimeAiBattlefieldCollector
    {
        public const float DefaultRefreshIntervalSeconds = 0.20f;
        private const float LowHealthSkillTriggerThreshold = 0.50f;

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

                HashSet<EntityController> aiUnits = existing.AiUnits();
                int estimatedAi = (aiUnits != null ? aiUnits.Count : 16) + 8;
                var aiSnapshots = new List<AiUnitSnapshot>(estimatedAi);
                EntityController referenceAiUnit = null;

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

                HashSet<EntityController> playerEntities = existing.PlayerEntities() ?? existing.PlayerUnits();
                int estimatedHostile = playerEntities != null ? playerEntities.Count : 16;
                var hostileSnapshots = new List<AiUnitSnapshot>(estimatedHostile);
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

                var hazardSnapshots = new List<AiHazardZoneSnapshot>();
                try
                {
                    IReadOnlyList<HazardZoneDiagnosticRow> activeHazards =
                        RuntimeHazardManager.GetActiveHazardRows(AiBattlefieldBlackboardBuilder.DefaultMaxTrackedHazardZones * 2, out _);
                    if (activeHazards != null)
                    {
                        for (int i = 0; i < activeHazards.Count; i++)
                        {
                            HazardZoneDiagnosticRow hz = activeHazards[i];
                            bool beneficial = string.Equals(hz.HazardType, "NaniteField", StringComparison.OrdinalIgnoreCase);
                            // Note: in RuntimeHazardManager, NaniteField heals player entities only (!IsControlledByAi),
                            // while offensive hazards damage AI entities. Thus all default hazards are harmful or neutral to AI.
                            hazardSnapshots.Add(new AiHazardZoneSnapshot(
                                zoneId: hz.ZoneId,
                                hazardType: hz.HazardType,
                                centerX: hz.CenterX,
                                centerZ: hz.CenterZ,
                                radius: hz.Radius,
                                tickDamage: hz.TickDamage,
                                isBeneficialToAi: beneficial));
                        }
                    }
                }
                catch
                {
                    // Fail-closed if hazard census is unavailable
                }

                int waveNumber = RuntimeAiDirectorCensus.Current().CurrentAttackWaveNumber;
                _lastSnapshot = AiBattlefieldBlackboardBuilder.BuildSnapshot(
                    timestamp: now,
                    baseIndex: _baseIndex,
                    currentWaveNumber: waveNumber,
                    aiUnits: aiSnapshots,
                    hostileUnits: hostileSnapshots,
                    hazardZones: hazardSnapshots);
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
            string activationTrigger = "EnemiesInRange";
            if (!string.IsNullOrWhiteSpace(entity.EntityId) &&
                RuntimeUnitMechanicsDirector.TryGetProfile(entity.EntityId, out UnitRuntimeMechanicsProfile profile) &&
                profile != null)
            {
                if (profile.MinAttackRange.HasValue && profile.MinAttackRange.Value > 0f)
                {
                    minAttackRange = profile.MinAttackRange.Value;
                }

                if (!string.IsNullOrWhiteSpace(profile.AiActivationTrigger))
                {
                    activationTrigger = profile.AiActivationTrigger;
                }
            }

            bool hasReadySkill = false;
            bool skillTargetsSelf = false;
            float skillRange = 15f;
            try
            {
                SkillInfo skillInfo = entity.SkillInfo;
                if (skillInfo != null &&
                    !entity.HasSkillActivationInCommandChain &&
                    entity.CurrentMana >= skillInfo.manaCost)
                {
                    bool lowHealthTrigger = string.Equals(activationTrigger, "LowHealth", StringComparison.OrdinalIgnoreCase);
                    bool healthGateOk = !lowHealthTrigger ||
                        (entity.MaxHealth > 0f && (entity.CurrentHealth / entity.MaxHealth) <= LowHealthSkillTriggerThreshold);

                    bool combatGateOk = true;
                    if (string.Equals(activationTrigger, "CombatEngaged", StringComparison.OrdinalIgnoreCase))
                    {
                        EntityController target = entity.CurrentAttackTarget;
                        combatGateOk = entity.IsTargetInRange && target != null && !target.IsDestroyed &&
                            !target.DestroyHasBeenStarted && !target.canNotBeTargetedByAi && entity.IsEnemy(target);
                    }

                    if (healthGateOk && combatGateOk)
                    {
                        hasReadySkill = true;
                        skillTargetsSelf = skillInfo.targetOrigin == TargetOrigin.Self;
                        skillRange = Math.Max(6f, entity.WeaponRange > 0f ? entity.WeaponRange : 15f);
                    }
                }
            }
            catch
            {
                hasReadySkill = false;
            }

            bool isHarvester = entity.HasRole(UnitRole.Harvester);
            bool isSupport = entity.IsEngineer || entity.HasRole(UnitRole.Supporter) || entity.HasRole(UnitRole.Heal);
            bool hasQueued = HasQueuedCommands(entity);
            int targetId = entity.CurrentAttackTarget != null ? entity.CurrentAttackTarget.UniqueEntityId : -1;

            int statusFlags = 0;
            try
            {
                if (entity.HasActiveStatusEffect(StatusEffect.SlowedDown)) statusFlags |= (int)AiCombatStatusFlags.Slowed;
                if (entity.HasActiveStatusEffect(StatusEffect.Stun)) statusFlags |= (int)AiCombatStatusFlags.Stunned;
                if (entity.HasActiveStatusEffect(StatusEffect.OnFire)) statusFlags |= (int)AiCombatStatusFlags.Burning;
                if (entity.HasActiveStatusEffect(StatusEffect.Marked)) statusFlags |= (int)AiCombatStatusFlags.Vulnerable;
                if (entity.HasActiveStatusEffect(StatusEffect.Overclocked) || entity.HasActiveStatusEffect(StatusEffect.Turbo) || entity.HasActiveStatusEffect(StatusEffect.CombatBoost)) statusFlags |= (int)AiCombatStatusFlags.Overclocked;
                if (entity.HasActiveStatusEffect(StatusEffect.Defender) || entity.HasActiveStatusEffect(StatusEffect.Robust) || entity.HasActiveStatusEffect(StatusEffect.Armored) || entity.HasActiveStatusEffect(StatusEffect.SquadEffect)) statusFlags |= (int)AiCombatStatusFlags.Shielded;
            }
            catch
            {
                statusFlags = 0;
            }

            int affixFlags = 0;
            string currentCommandTag = string.Empty;
            try
            {
                string entityKey = entity.UniqueEntityId.ToString();
                IReadOnlyList<string> activeAffixes;
                if (RuntimeGameplayCommandDirector.TryGetActiveAffixes(entityKey, out activeAffixes) && activeAffixes != null)
                {
                    for (int a = 0; a < activeAffixes.Count; a++)
                    {
                        string affix = activeAffixes[a];
                        if (string.Equals(affix, "Swift", StringComparison.OrdinalIgnoreCase)) affixFlags |= (int)AiAffixFlags.Swift;
                        else if (string.Equals(affix, "Bulwark", StringComparison.OrdinalIgnoreCase)) affixFlags |= (int)AiAffixFlags.Bulwark;
                        else if (string.Equals(affix, "Juggernaut", StringComparison.OrdinalIgnoreCase)) affixFlags |= (int)AiAffixFlags.Juggernaut;
                        else if (string.Equals(affix, "Overcharged", StringComparison.OrdinalIgnoreCase)) affixFlags |= (int)AiAffixFlags.Overcharged;
                        else if (string.Equals(affix, "Vampiric", StringComparison.OrdinalIgnoreCase)) affixFlags |= (int)AiAffixFlags.Vampiric;
                        else if (string.Equals(affix, "Volatile", StringComparison.OrdinalIgnoreCase)) affixFlags |= (int)AiAffixFlags.Volatile;
                        else if (string.Equals(affix, "Shielded", StringComparison.OrdinalIgnoreCase)) affixFlags |= (int)AiAffixFlags.Shielded;
                    }
                }

                currentCommandTag = RuntimeGameplayCommandDirector.GetLastUnitCommandTag(entityKey);
                if (string.IsNullOrWhiteSpace(currentCommandTag))
                {
                    currentCommandTag = RuntimeGameplayCommandDirector.GetLastUnitCommandKind(entityKey);
                }
            }
            catch
            {
                affixFlags = 0;
            }

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
                currentCommandTag: currentCommandTag ?? string.Empty,
                currentTargetId: targetId,
                hasReadyActiveSkill: hasReadySkill,
                activeSkillTargetsSelf: skillTargetsSelf,
                activeSkillRange: skillRange,
                statusEffectFlags: statusFlags,
                affixFlags: affixFlags);
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
                return typeof(EntityController).GetField(
                    "_commandChain",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }
            catch
            {
                return null;
            }
        }
    }
}
