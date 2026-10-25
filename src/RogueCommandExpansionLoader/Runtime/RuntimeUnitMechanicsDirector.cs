using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RogueCommandExpansionLoader.Content;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    public sealed class UnitRuntimeMechanicsProfile
    {
        public string EntityId { get; set; }
        public ContentUnitHealingDefinition Healing { get; set; }
        public ContentUnitAuraDefinition Aura { get; set; }
        public string[] BaselineStatusEffects { get; set; }
        public ContentCombatBoostDefinition[] CombatBoosts { get; set; }
        public ContentUnitExperienceDefinition Experience { get; set; }
        public float? MinAttackRange { get; set; }
        public float? CriticalHitChance { get; set; }
        public ContentOnHitStatusEffectDefinition[] OnHitStatusEffects { get; set; }

        public bool HasRuntimeComponentMechanics =>
            (Healing != null && (Healing.healAmount > 0f || Healing.repairAmount > 0f) && Healing.healRange > 0f) ||
            (Aura != null && Aura.auraRadius > 0f && !string.IsNullOrWhiteSpace(Aura.auraType)) ||
            (BaselineStatusEffects != null && BaselineStatusEffects.Length > 0);

        public bool HasOnDamageDealtMechanics =>
            (CombatBoosts != null && CombatBoosts.Length > 0) ||
            (Experience != null && Experience.maxLevel > 0);

        public bool HasAnyMechanics =>
            HasRuntimeComponentMechanics ||
            HasOnDamageDealtMechanics ||
            (MinAttackRange.HasValue && MinAttackRange.Value > 0f) ||
            (CriticalHitChance.HasValue && CriticalHitChance.Value > 0f) ||
            (OnHitStatusEffects != null && OnHitStatusEffects.Length > 0);
    }

    /// <summary>
    /// Executes native runtime mechanics for units declaring <c>healing</c>, <c>aura</c>,
    /// <c>baselineStatusEffects</c>, <c>combatBoosts</c>, and <c>experience</c> in <c>content.json</c>.
    /// </summary>
    public sealed class RuntimeUnitMechanicsDirector : MonoBehaviour
    {
        private static readonly Dictionary<string, UnitRuntimeMechanicsProfile> _profiles =
            new Dictionary<string, UnitRuntimeMechanicsProfile>(StringComparer.Ordinal);

        private static readonly Dictionary<int, int> _entityCombatXp =
            new Dictionary<int, int>();

        private static bool _reflectionResolved;
        private static PropertyInfo _entityIdProperty;
        private static FieldInfo _entityIdField;
        private static PropertyInfo _uniqueEntityIdProperty;
        private static PropertyInfo _isDestroyedProperty;
        private static PropertyInfo _destroyStartedProperty;
        private static PropertyInfo _positionProperty;
        private static PropertyInfo _currentHealthProperty;
        private static PropertyInfo _maxHealthProperty;
        private static PropertyInfo _currentArmorProperty;
        private static PropertyInfo _maxArmorProperty;
        private static PropertyInfo _currentShieldProperty;
        private static PropertyInfo _maxShieldProperty;
        private static PropertyInfo _hasShieldProperty;
        private static PropertyInfo _currentRankProperty;
        private static PropertyInfo _maxRankProperty;
        private static MethodInfo _isEnemyMethod;
        private static MethodInfo _healMethod;
        private static MethodInfo _repairArmorMethod;
        private static MethodInfo _chargeShieldMethod;
        private static MethodInfo _rankUpMethod;
        private static MethodInfo _setActiveStatusEffectMethod;
        private static Type _statusEffectEnumType;
        private static Type _durationTypeEnumType;
        private static Type _existingControllersType;
        private static PropertyInfo _existingControllersInstanceProp;
        private static MethodInfo _entitiesMethod;

        private object _entityController;
        private UnitRuntimeMechanicsProfile _profile;
        private float _nextHealCheckTime;
        private float _nextAuraCheckTime;
        private bool _baselineApplied;

        public static void RegisterProfile(string entityId, UnitRuntimeMechanicsProfile profile)
        {
            if (string.IsNullOrWhiteSpace(entityId) || profile == null || !profile.HasAnyMechanics)
            {
                return;
            }

            lock (_profiles)
            {
                _profiles[entityId] = profile;
            }
        }

        public static bool TryGetProfile(string entityId, out UnitRuntimeMechanicsProfile profile)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                profile = null;
                return false;
            }

            lock (_profiles)
            {
                return _profiles.TryGetValue(entityId, out profile);
            }
        }

        public static void ClearProfiles()
        {
            lock (_profiles)
            {
                _profiles.Clear();
            }

            lock (_entityCombatXp)
            {
                _entityCombatXp.Clear();
            }
        }

        public static string FormatMechanicsSummary(string entityId)
        {
            UnitRuntimeMechanicsProfile profile;
            if (!TryGetProfile(entityId, out profile) || profile == null)
            {
                return null;
            }

            var parts = new List<string>(6);
            if (profile.MinAttackRange.HasValue && profile.MinAttackRange.Value > 0f)
            {
                parts.Add("MinRange: " + profile.MinAttackRange.Value.ToString("0.#") + "m");
            }
            if (profile.CriticalHitChance.HasValue && profile.CriticalHitChance.Value > 0f)
            {
                parts.Add("Crit: " + (profile.CriticalHitChance.Value * 100f).ToString("0") + "%");
            }
            if (profile.OnHitStatusEffects != null && profile.OnHitStatusEffects.Length > 0)
            {
                var names = new List<string>(profile.OnHitStatusEffects.Length);
                for (int i = 0; i < profile.OnHitStatusEffects.Length; i++)
                {
                    if (profile.OnHitStatusEffects[i] != null && !string.IsNullOrWhiteSpace(profile.OnHitStatusEffects[i].statusEffect))
                    {
                        names.Add(profile.OnHitStatusEffects[i].statusEffect);
                    }
                }
                if (names.Count > 0)
                {
                    parts.Add("OnHit: " + string.Join("/", names));
                }
            }
            if (profile.BaselineStatusEffects != null && profile.BaselineStatusEffects.Length > 0)
            {
                parts.Add("Pasivos: " + string.Join(", ", profile.BaselineStatusEffects));
            }
            if (profile.Healing != null && (profile.Healing.healAmount > 0f || profile.Healing.repairAmount > 0f))
            {
                parts.Add("Curación: " + profile.Healing.healAmount.ToString("0") + " HP / " +
                    profile.Healing.repairAmount.ToString("0") + " Arm (" + profile.Healing.healRange.ToString("0.#") + "m)");
            }
            if (profile.Aura != null && profile.Aura.auraRadius > 0f && !string.IsNullOrWhiteSpace(profile.Aura.auraType))
            {
                parts.Add("Aura: " + profile.Aura.auraType + " (" + profile.Aura.auraRadius.ToString("0.#") + "m)");
            }
            if (profile.CombatBoosts != null && profile.CombatBoosts.Length > 0)
            {
                parts.Add("Boosts: " + profile.CombatBoosts.Length);
            }
            if (profile.Experience != null && profile.Experience.maxLevel > 0)
            {
                parts.Add("Veteranía: Rango " + profile.Experience.maxLevel);
            }

            return parts.Count > 0 ? string.Join(" • ", parts) : null;
        }

        public static void AttachIfApplicable(object entityObj)
        {
            if (entityObj == null)
            {
                return;
            }

            try
            {
                Component entityComponent = entityObj as Component;
                GameObject rootObject = entityComponent != null ? entityComponent.gameObject : entityObj as GameObject;
                if (rootObject == null)
                {
                    return;
                }

                EnsureReflection(entityObj.GetType());
                string entityId = ReadEntityId(entityObj);
                if (string.IsNullOrWhiteSpace(entityId))
                {
                    return;
                }

                UnitRuntimeMechanicsProfile profile;
                if (!TryGetProfile(entityId, out profile) || profile == null)
                {
                    return;
                }

                int uniqueId = ReadIntProperty(_uniqueEntityIdProperty, entityObj, -1);
                if (uniqueId >= 0)
                {
                    lock (_entityCombatXp)
                    {
                        _entityCombatXp.Remove(uniqueId);
                    }
                }

                if (profile.BaselineStatusEffects != null && profile.BaselineStatusEffects.Length > 0)
                {
                    ApplyBaselineStatusEffects(entityObj, profile.BaselineStatusEffects);
                }

                if (!profile.HasRuntimeComponentMechanics)
                {
                    return;
                }

                RuntimeUnitMechanicsDirector director = rootObject.GetComponent<RuntimeUnitMechanicsDirector>();
                if (director == null)
                {
                    director = rootObject.AddComponent<RuntimeUnitMechanicsDirector>();
                }

                director.InitializeOrRefresh(entityObj, profile);
            }
            catch
            {
                // Isolated from entity instantiation path.
            }
        }

        public static void OnEntityDealtDamage(object targetObj, float amount, object originatorObj)
        {
            if (targetObj == null || originatorObj == null || amount <= 0f)
            {
                return;
            }

            try
            {
                EnsureReflection(originatorObj.GetType());
                string originatorId = ReadEntityId(originatorObj);
                if (string.IsNullOrWhiteSpace(originatorId))
                {
                    return;
                }

                UnitRuntimeMechanicsProfile profile;
                if (!TryGetProfile(originatorId, out profile) || profile == null || !profile.HasOnDamageDealtMechanics)
                {
                    return;
                }

                if (!IsEntityAlive(originatorObj))
                {
                    return;
                }

                // 1. Combat Boosts on hit
                if (profile.CombatBoosts != null && profile.CombatBoosts.Length > 0)
                {
                    for (int i = 0; i < profile.CombatBoosts.Length; i++)
                    {
                        ContentCombatBoostDefinition boost = profile.CombatBoosts[i];
                        if (boost == null || boost.duration <= 0f || boost.multiplier <= 0f)
                        {
                            continue;
                        }

                        string statusName = "CombatBoost";
                        if (!string.IsNullOrWhiteSpace(boost.statName))
                        {
                            if (boost.statName.IndexOf("Speed", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                boost.statName.IndexOf("Move", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                statusName = "Turbo";
                            }
                            else if (boost.statName.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     boost.statName.IndexOf("Cooldown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     boost.statName.IndexOf("Rate", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                statusName = "Overclocked";
                            }
                            else if (boost.statName.IndexOf("Armor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     boost.statName.IndexOf("Defense", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                statusName = "Robust";
                            }
                        }

                        ApplyTimedStatusEffect(originatorObj, statusName, boost.duration);
                    }
                }

                // 2. Experience & Veterancy RankUp
                if (profile.Experience != null && profile.Experience.maxLevel > 0)
                {
                    float targetHealth = ReadFloatProperty(_currentHealthProperty, targetObj, 1f);
                    bool targetKilled = targetHealth <= 0f || ReadBoolProperty(_destroyStartedProperty, targetObj);
                    int xpGain = targetKilled ? 3 : 1;
                    if (profile.Experience.experienceGainMultiplier > 0f)
                    {
                        xpGain = Mathf.Max(1, Mathf.RoundToInt(xpGain * profile.Experience.experienceGainMultiplier));
                    }

                    int uniqueId = ReadIntProperty(_uniqueEntityIdProperty, originatorObj, originatorObj.GetHashCode());
                    int threshold = Mathf.Max(1, profile.Experience.experiencePerLevel > 0
                        ? profile.Experience.experiencePerLevel * 3
                        : 3);

                    bool shouldPromote = false;
                    lock (_entityCombatXp)
                    {
                        int currentXp;
                        _entityCombatXp.TryGetValue(uniqueId, out currentXp);
                        currentXp += xpGain;
                        if (currentXp >= threshold)
                        {
                            currentXp -= threshold;
                            shouldPromote = true;
                        }
                        _entityCombatXp[uniqueId] = currentXp;
                    }

                    if (shouldPromote)
                    {
                        int currentRank = ReadIntProperty(_currentRankProperty, originatorObj, 0);
                        int maxRank = ReadIntProperty(_maxRankProperty, originatorObj, profile.Experience.maxLevel);
                        if (currentRank < Mathf.Min(maxRank, profile.Experience.maxLevel) && _rankUpMethod != null)
                        {
                            _rankUpMethod.Invoke(originatorObj, new object[] { 1 });
                        }

                        ApplyUnlimitedStatusEffect(originatorObj, "CombatBoost");
                        if (profile.Experience.levelUpBonus > 0f)
                        {
                            float maxHp = ReadFloatProperty(_maxHealthProperty, originatorObj, 100f);
                            float bonusHeal = Mathf.Max(5f, maxHp * profile.Experience.levelUpBonus);
                            InvokeHeal(originatorObj, bonusHeal, originatorObj);
                            if (ReadBoolProperty(_hasShieldProperty, originatorObj))
                            {
                                InvokeChargeShield(originatorObj, bonusHeal * 0.5f);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Isolated from combat damage pipeline.
            }
        }

        private void InitializeOrRefresh(object entityController, UnitRuntimeMechanicsProfile profile)
        {
            _entityController = entityController;
            _profile = profile;
            _nextHealCheckTime = Time.time + 0.5f;
            _nextAuraCheckTime = Time.time + 0.5f;
            _baselineApplied = true;
        }

        public void TickMechanics() => Update();

        private void Update()
        {
            if (_entityController == null || _profile == null)
            {
                return;
            }

            try
            {
                if (!IsEntityAlive(_entityController))
                {
                    return;
                }

                float now = Time.time;

                if (!_baselineApplied && _profile.BaselineStatusEffects != null && _profile.BaselineStatusEffects.Length > 0)
                {
                    ApplyBaselineStatusEffects(_entityController, _profile.BaselineStatusEffects);
                    _baselineApplied = true;
                }

                if (_profile.Healing != null &&
                    (_profile.Healing.healAmount > 0f || _profile.Healing.repairAmount > 0f) &&
                    _profile.Healing.healRange > 0f &&
                    now >= _nextHealCheckTime)
                {
                    float rate = _profile.Healing.healRate > 0.05f ? _profile.Healing.healRate : 1f;
                    _nextHealCheckTime = now + Mathf.Clamp(1f / rate, 0.25f, 5f);
                    ExecuteHealingPulse(_entityController, _profile.Healing);
                }

                if (_profile.Aura != null &&
                    _profile.Aura.auraRadius > 0f &&
                    !string.IsNullOrWhiteSpace(_profile.Aura.auraType) &&
                    now >= _nextAuraCheckTime)
                {
                    _nextAuraCheckTime = now + 1.0f;
                    ExecuteAuraPulse(_entityController, _profile.Aura);
                }
            }
            catch
            {
                // Keep MonoBehaviour update resilient.
            }
        }

        private static void ExecuteHealingPulse(object self, ContentUnitHealingDefinition healing)
        {
            IEnumerable entities = GetActiveEntities();
            if (entities == null)
            {
                return;
            }

            Vector3 selfPos = ReadVector3Property(_positionProperty, self);
            float worldRange = healing.healRange <= 25f ? healing.healRange * 10f : healing.healRange;
            float rangeSq = worldRange * worldRange;

            object bestHealTarget = null;
            float lowestHealthRatio = 1f;

            object bestArmorTarget = null;
            float lowestArmorRatio = 1f;

            foreach (object candidate in entities)
            {
                if (candidate == null || !IsEntityAlive(candidate) || IsEnemy(self, candidate))
                {
                    continue;
                }

                Vector3 pos = ReadVector3Property(_positionProperty, candidate);
                float dx = pos.x - selfPos.x;
                float dz = pos.z - selfPos.z;
                if ((dx * dx + dz * dz) > rangeSq)
                {
                    continue;
                }

                if (healing.healAmount > 0f)
                {
                    float curHp = ReadFloatProperty(_currentHealthProperty, candidate, 0f);
                    float maxHp = ReadFloatProperty(_maxHealthProperty, candidate, 0f);
                    if (maxHp > 0f && curHp < maxHp - 0.1f)
                    {
                        float ratio = curHp / maxHp;
                        if (ratio < lowestHealthRatio)
                        {
                            lowestHealthRatio = ratio;
                            bestHealTarget = candidate;
                        }
                    }
                }

                if (healing.repairAmount > 0f)
                {
                    float curArmor = ReadFloatProperty(_currentArmorProperty, candidate, 0f);
                    float maxArmor = ReadFloatProperty(_maxArmorProperty, candidate, 0f);
                    if (maxArmor > 0f && curArmor < maxArmor - 0.1f)
                    {
                        float ratio = curArmor / maxArmor;
                        if (ratio < lowestArmorRatio)
                        {
                            lowestArmorRatio = ratio;
                            bestArmorTarget = candidate;
                        }
                    }
                }
            }

            if (bestHealTarget != null && healing.healAmount > 0f)
            {
                InvokeHeal(bestHealTarget, healing.healAmount, self);
            }

            if (bestArmorTarget != null && healing.repairAmount > 0f)
            {
                InvokeRepairArmor(bestArmorTarget, healing.repairAmount, self);
            }
        }

        private static void ExecuteAuraPulse(object self, ContentUnitAuraDefinition aura)
        {
            if (!aura.affectsAllies && !aura.affectsEnemies)
            {
                return;
            }

            IEnumerable entities = GetActiveEntities();
            if (entities == null)
            {
                return;
            }

            Vector3 selfPos = ReadVector3Property(_positionProperty, self);
            float worldRadius = aura.auraRadius <= 25f ? aura.auraRadius * 10f : aura.auraRadius;
            float radiusSq = worldRadius * worldRadius;
            string auraType = aura.auraType;

            int affectedCount = 0;
            foreach (object candidate in entities)
            {
                if (candidate == null || !IsEntityAlive(candidate))
                {
                    continue;
                }

                bool isEnemy = IsEnemy(self, candidate);
                if (isEnemy && !aura.affectsEnemies)
                {
                    continue;
                }
                if (!isEnemy && !aura.affectsAllies)
                {
                    continue;
                }

                Vector3 pos = ReadVector3Property(_positionProperty, candidate);
                float dx = pos.x - selfPos.x;
                float dz = pos.z - selfPos.z;
                if ((dx * dx + dz * dz) > radiusSq)
                {
                    continue;
                }

                if (string.Equals(auraType, "Buff", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyTimedStatusEffect(candidate, "CombatBoost", 1.5f);
                    ApplyTimedStatusEffect(candidate, "Robust", 1.5f);
                }
                else if (string.Equals(auraType, "Debuff", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyTimedStatusEffect(candidate, "SlowedDown", 1.5f);
                    ApplyTimedStatusEffect(candidate, "Marked", 1.5f);
                }
                else if (string.Equals(auraType, "Heal", StringComparison.OrdinalIgnoreCase) && !isEnemy)
                {
                    float amount = aura.buffAmount > 0f ? aura.buffAmount : 4f;
                    float curHp = ReadFloatProperty(_currentHealthProperty, candidate, 0f);
                    float maxHp = ReadFloatProperty(_maxHealthProperty, candidate, 0f);
                    if (maxHp > 0f && curHp < maxHp - 0.1f)
                    {
                        InvokeHeal(candidate, amount, self);
                    }
                }
                else if (string.Equals(auraType, "Shield", StringComparison.OrdinalIgnoreCase) && !isEnemy)
                {
                    float amount = aura.buffAmount > 0f ? aura.buffAmount : 5f;
                    if (ReadBoolProperty(_hasShieldProperty, candidate))
                    {
                        float curShield = ReadFloatProperty(_currentShieldProperty, candidate, 0f);
                        float maxShield = ReadFloatProperty(_maxShieldProperty, candidate, 0f);
                        if (maxShield > 0f && curShield < maxShield - 0.1f)
                        {
                            InvokeChargeShield(candidate, amount);
                        }
                    }
                    else
                    {
                        float curArmor = ReadFloatProperty(_currentArmorProperty, candidate, 0f);
                        float maxArmor = ReadFloatProperty(_maxArmorProperty, candidate, 0f);
                        if (maxArmor > 0f && curArmor < maxArmor - 0.1f)
                        {
                            InvokeRepairArmor(candidate, amount, self);
                        }
                    }
                }

                affectedCount++;
                if (affectedCount >= 16)
                {
                    break;
                }
            }
        }

        public static void ApplyBaselineStatusEffects(object entityObj, string[] statusEffects)
        {
            if (entityObj == null || statusEffects == null || statusEffects.Length == 0)
            {
                return;
            }

            EnsureReflection(entityObj.GetType());
            for (int i = 0; i < statusEffects.Length; i++)
            {
                string effectName = statusEffects[i];
                if (!string.IsNullOrWhiteSpace(effectName))
                {
                    ApplyUnlimitedStatusEffect(entityObj, effectName);
                }
            }
        }

        private static void ApplyUnlimitedStatusEffect(object entityObj, string effectName)
        {
            if (_setActiveStatusEffectMethod == null || _statusEffectEnumType == null || _durationTypeEnumType == null)
            {
                return;
            }

            try
            {
                object parsedEffect = Enum.Parse(_statusEffectEnumType, effectName, true);
                object durationUnlimited = Enum.ToObject(_durationTypeEnumType, 0);
                _setActiveStatusEffectMethod.Invoke(entityObj, new object[] { parsedEffect, durationUnlimited, 0f });
            }
            catch
            {
            }
        }

        private static void ApplyTimedStatusEffect(object entityObj, string effectName, float durationSeconds)
        {
            if (_setActiveStatusEffectMethod == null || _statusEffectEnumType == null || _durationTypeEnumType == null || durationSeconds <= 0f)
            {
                return;
            }

            try
            {
                object parsedEffect = Enum.Parse(_statusEffectEnumType, effectName, true);
                object durationSecondsEnum = Enum.ToObject(_durationTypeEnumType, 1);
                _setActiveStatusEffectMethod.Invoke(entityObj, new object[] { parsedEffect, durationSecondsEnum, durationSeconds });
            }
            catch
            {
            }
        }

        private static void InvokeHeal(object target, float amount, object originator)
        {
            if (_healMethod == null || target == null || amount <= 0f)
            {
                return;
            }

            try
            {
                ParameterInfo[] parameters = _healMethod.GetParameters();
                if (parameters.Length == 4)
                {
                    _healMethod.Invoke(target, new object[] { amount, originator, false, false });
                }
                else if (parameters.Length == 2)
                {
                    _healMethod.Invoke(target, new object[] { amount, originator });
                }
            }
            catch
            {
            }
        }

        private static void InvokeRepairArmor(object target, float amount, object originator)
        {
            if (_repairArmorMethod == null || target == null || amount <= 0f)
            {
                return;
            }

            try
            {
                ParameterInfo[] parameters = _repairArmorMethod.GetParameters();
                if (parameters.Length == 3)
                {
                    _repairArmorMethod.Invoke(target, new object[] { amount, originator, false });
                }
                else if (parameters.Length == 2)
                {
                    _repairArmorMethod.Invoke(target, new object[] { amount, originator });
                }
            }
            catch
            {
            }
        }

        private static void InvokeChargeShield(object target, float amount)
        {
            if (_chargeShieldMethod == null || target == null || amount <= 0f)
            {
                return;
            }

            try
            {
                ParameterInfo[] parameters = _chargeShieldMethod.GetParameters();
                if (parameters.Length == 2)
                {
                    _chargeShieldMethod.Invoke(target, new object[] { amount, true });
                }
                else if (parameters.Length == 1)
                {
                    _chargeShieldMethod.Invoke(target, new object[] { amount });
                }
            }
            catch
            {
            }
        }

        private static IEnumerable GetActiveEntities()
        {
            if (_existingControllersInstanceProp == null || _entitiesMethod == null)
            {
                return null;
            }

            try
            {
                object instance = _existingControllersInstanceProp.GetValue(null, null);
                if (instance == null)
                {
                    return null;
                }
                return _entitiesMethod.Invoke(instance, null) as IEnumerable;
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureReflection(Type entityType)
        {
            if (_reflectionResolved && _entityIdProperty != null)
            {
                return;
            }

            if (entityType == null)
            {
                return;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _entityIdProperty = entityType.GetProperty("EntityId", flags);
            _entityIdField = entityType.GetField("entityId", flags);
            _uniqueEntityIdProperty = entityType.GetProperty("UniqueEntityId", flags);
            _isDestroyedProperty = entityType.GetProperty("IsDestroyed", flags);
            _destroyStartedProperty = entityType.GetProperty("DestroyHasBeenStarted", flags);
            _positionProperty = entityType.GetProperty("Position", flags);
            _currentHealthProperty = entityType.GetProperty("CurrentHealth", flags);
            _maxHealthProperty = entityType.GetProperty("MaxHealth", flags);
            _currentArmorProperty = entityType.GetProperty("CurrentArmor", flags);
            _maxArmorProperty = entityType.GetProperty("MaxArmor", flags);
            _currentShieldProperty = entityType.GetProperty("CurrentShield", flags);
            _maxShieldProperty = entityType.GetProperty("MaxShield", flags);
            _hasShieldProperty = entityType.GetProperty("HasShield", flags);
            _currentRankProperty = entityType.GetProperty("CurrentRank", flags);
            _maxRankProperty = entityType.GetProperty("MaxRank", flags);

            _isEnemyMethod = entityType.GetMethod("IsEnemy", flags, null, new[] { entityType }, null);
            _healMethod = entityType.GetMethod("Heal", flags);
            _repairArmorMethod = entityType.GetMethod("RepairArmor", flags);
            _chargeShieldMethod = entityType.GetMethod("ChargeShield", flags);
            _rankUpMethod = entityType.GetMethod("RankUp", flags, null, new[] { typeof(int) }, null);

            Assembly asm = entityType.Assembly;
            _statusEffectEnumType = asm.GetType("StatusEffect");
            _durationTypeEnumType = asm.GetType("SetStatusEffect+DurationType") ?? asm.GetType("SetStatusEffect/DurationType");
            if (_statusEffectEnumType != null && _durationTypeEnumType != null)
            {
                _setActiveStatusEffectMethod = entityType.GetMethod("SetActiveStatusEffect",
                    flags,
                    null,
                    new[] { _statusEffectEnumType, _durationTypeEnumType, typeof(float) },
                    null);
            }

            _existingControllersType = asm.GetType("ExistingControllers");
            if (_existingControllersType != null)
            {
                _existingControllersInstanceProp = _existingControllersType.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _entitiesMethod = _existingControllersType.GetMethod("Entities", flags, null, Type.EmptyTypes, null);
            }

            _reflectionResolved = true;
        }

        private static string ReadEntityId(object entityObj)
        {
            if (entityObj == null)
            {
                return null;
            }

            if (_entityIdProperty != null && _entityIdProperty.DeclaringType != null &&
                _entityIdProperty.DeclaringType.IsInstanceOfType(entityObj))
            {
                return _entityIdProperty.GetValue(entityObj, null) as string;
            }

            if (_entityIdField != null && _entityIdField.DeclaringType != null &&
                _entityIdField.DeclaringType.IsInstanceOfType(entityObj))
            {
                return _entityIdField.GetValue(entityObj) as string;
            }

            PropertyInfo prop = entityObj.GetType().GetProperty("EntityId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
            {
                return prop.GetValue(entityObj, null) as string;
            }

            FieldInfo field = entityObj.GetType().GetField("entityId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null ? field.GetValue(entityObj) as string : null;
        }

        private static bool IsEntityAlive(object entityObj)
        {
            if (entityObj == null)
            {
                return false;
            }

            if (ReadBoolProperty(_isDestroyedProperty, entityObj) ||
                ReadBoolProperty(_destroyStartedProperty, entityObj))
            {
                return false;
            }

            Component comp = entityObj as Component;
            if (comp != null && (comp.gameObject == null || !comp.gameObject.activeInHierarchy))
            {
                return false;
            }

            return true;
        }

        private static bool IsEnemy(object self, object other)
        {
            if (self == null || other == null)
            {
                return false;
            }

            if (_isEnemyMethod != null &&
                _isEnemyMethod.DeclaringType != null &&
                _isEnemyMethod.DeclaringType.IsInstanceOfType(self))
            {
                try
                {
                    return (bool)_isEnemyMethod.Invoke(self, new[] { other });
                }
                catch
                {
                }
            }

            Component c1 = self as Component;
            Component c2 = other as Component;
            if (c1 != null && c2 != null && c1.gameObject != null && c2.gameObject != null)
            {
                return !string.Equals(c1.gameObject.tag, c2.gameObject.tag, StringComparison.Ordinal);
            }

            return false;
        }

        private static bool ReadBoolProperty(PropertyInfo prop, object target)
        {
            if (prop == null || target == null)
            {
                return false;
            }
            try
            {
                if (prop.DeclaringType != null && prop.DeclaringType.IsInstanceOfType(target))
                {
                    return (bool)prop.GetValue(target, null);
                }
            }
            catch
            {
            }
            return false;
        }

        private static int ReadIntProperty(PropertyInfo prop, object target, int fallback)
        {
            if (prop == null || target == null)
            {
                return fallback;
            }
            try
            {
                if (prop.DeclaringType != null && prop.DeclaringType.IsInstanceOfType(target))
                {
                    return (int)prop.GetValue(target, null);
                }
            }
            catch
            {
            }
            return fallback;
        }

        private static float ReadFloatProperty(PropertyInfo prop, object target, float fallback)
        {
            if (prop == null || target == null)
            {
                return fallback;
            }
            try
            {
                if (prop.DeclaringType != null && prop.DeclaringType.IsInstanceOfType(target))
                {
                    return Convert.ToSingle(prop.GetValue(target, null));
                }
            }
            catch
            {
            }
            return fallback;
        }

        private static Vector3 ReadVector3Property(PropertyInfo prop, object target)
        {
            if (prop != null && target != null)
            {
                try
                {
                    if (prop.DeclaringType != null && prop.DeclaringType.IsInstanceOfType(target))
                    {
                        return (Vector3)prop.GetValue(target, null);
                    }
                }
                catch
                {
                }
            }

            Component comp = target as Component;
            return comp != null && comp.transform != null ? comp.transform.position : Vector3.zero;
        }
    }
}
