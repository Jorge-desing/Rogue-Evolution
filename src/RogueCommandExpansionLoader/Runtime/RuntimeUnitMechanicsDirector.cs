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
        public string AiActivationTrigger { get; set; }
        public ContentUnitHealingDefinition Healing { get; set; }
        public ContentUnitAuraDefinition Aura { get; set; }
        public string[] BaselineStatusEffects { get; set; }
        public ContentUnitStatusEffectDefinition[] StatusEffects { get; set; }
        public ContentCombatBoostDefinition[] CombatBoosts { get; set; }
        public ContentUnitExperienceDefinition Experience { get; set; }
        public ContentUnitSynergyDefinition[] Synergies { get; set; }
        public ContentUnitStealthDefinition Stealth { get; set; }
        public ContentUnitCommandDefinition Command { get; set; }
        public ContentUnitExperienceRewardDefinition ExperienceReward { get; set; }
        public ContentUnitAiBehaviorDefinition AiBehavior { get; set; }
        public float? MinAttackRange { get; set; }
        public float? CriticalHitChance { get; set; }
        public ContentOnHitStatusEffectDefinition[] OnHitStatusEffects { get; set; }
        public ContentUnitShieldRegenDefinition ShieldRegen { get; set; }
        public ContentUnitVampiricLeechDefinition VampiricLeech { get; set; }
        public ContentUnitTauntDefinition Taunt { get; set; }
        public ContentUnitShieldOverloadNovaDefinition ShieldOverloadNova { get; set; }
        public string DefaultTacticalStance { get; set; }
        public string[] StartingAffixes { get; set; }
        public string DefaultLoadout { get; set; }
        public int? StartingVeterancyRank { get; set; }

        public bool HasRuntimeComponentMechanics =>
            (Healing != null && (Healing.healAmount > 0f || Healing.repairAmount > 0f) && Healing.healRange > 0f) ||
            (AiBehavior != null && !string.IsNullOrWhiteSpace(AiBehavior.aiTag)) ||
            (Aura != null && Aura.auraRadius > 0f && !string.IsNullOrWhiteSpace(Aura.auraType)) ||
            (BaselineStatusEffects != null && BaselineStatusEffects.Length > 0) ||
            (StatusEffects != null && StatusEffects.Length > 0) ||
            (Synergies != null && Synergies.Length > 0) ||
            (Stealth != null && Stealth.cloakDuration > 0f && Stealth.stealthLevel > 0f) ||
            (Command != null && Command.commandRange > 0f) ||
            (ShieldRegen != null && ShieldRegen.rechargeRate > 0f) ||
            (Taunt != null && Taunt.tauntRadius > 0f) ||
            (ShieldOverloadNova != null && ShieldOverloadNova.novaRadius > 0f) ||
            (!string.IsNullOrWhiteSpace(DefaultTacticalStance) && !string.Equals(DefaultTacticalStance, "Standard", StringComparison.OrdinalIgnoreCase)) ||
            (StartingAffixes != null && StartingAffixes.Length > 0) ||
            (!string.IsNullOrWhiteSpace(DefaultLoadout) && !string.Equals(DefaultLoadout, "Standard", StringComparison.OrdinalIgnoreCase)) ||
            (StartingVeterancyRank.HasValue && StartingVeterancyRank.Value > 0);

        public bool HasAiBehaviorMechanics =>
            AiBehavior != null &&
            (!string.IsNullOrWhiteSpace(AiBehavior.aiTag) ||
             AiBehavior.unitCap > 0 ||
             AiBehavior.combatUnitCap > 0 ||
             AiBehavior.skillActivationCheckInterval > 0f);

        public bool HasOnDamageDealtMechanics =>
            (CombatBoosts != null && CombatBoosts.Length > 0) ||
            (Experience != null && Experience.maxLevel > 0) ||
            (ExperienceReward != null && ExperienceReward.experienceAmount > 0) ||
            (VampiricLeech != null && VampiricLeech.siphonRatio > 0f);

        public bool HasAnyMechanics =>
            HasRuntimeComponentMechanics ||
            HasAiBehaviorMechanics ||
            HasOnDamageDealtMechanics ||
            (!string.IsNullOrWhiteSpace(AiActivationTrigger) &&
                !string.Equals(AiActivationTrigger, "EnemiesInRange", StringComparison.OrdinalIgnoreCase)) ||
            (MinAttackRange.HasValue && MinAttackRange.Value > 0f) ||
            (CriticalHitChance.HasValue && CriticalHitChance.Value > 0f) ||
            (OnHitStatusEffects != null && OnHitStatusEffects.Length > 0) ||
            !string.IsNullOrWhiteSpace(DefaultTacticalStance) ||
            (StartingAffixes != null && StartingAffixes.Length > 0) ||
            !string.IsNullOrWhiteSpace(DefaultLoadout) ||
            (StartingVeterancyRank.HasValue && StartingVeterancyRank.Value > 0);
    }

    /// <summary>
    /// Executes native runtime mechanics for units declaring <c>healing</c>, <c>aura</c>,
    /// <c>baselineStatusEffects</c>, <c>statusEffects</c>, <c>combatBoosts</c>, <c>experience</c>, <c>synergies</c>,
    /// <c>stealth</c>, <c>command</c>, and <c>experienceReward</c> in <c>content.json</c>,
    /// plus bounded tactical pulses dispatched by the runtime command director.
    /// </summary>
    public sealed class RuntimeUnitMechanicsDirector : MonoBehaviour
    {
        private static readonly Dictionary<string, UnitRuntimeMechanicsProfile> _profiles =
            new Dictionary<string, UnitRuntimeMechanicsProfile>(StringComparer.Ordinal);

        private static readonly Dictionary<int, int> _entityCombatXp =
            new Dictionary<int, int>();

        private static readonly Dictionary<int, float> _nativeAiSkillLogicalLastChecks =
            new Dictionary<int, float>();

        private static readonly HashSet<string> _appliedCardChangeBoostKeys =
            new HashSet<string>(StringComparer.Ordinal);

        private sealed class DynamicCommandAuraState
        {
            public object CaptainObj;
            public string CaptainKeyId;
            public string CaptainEntityId;
            public bool IsControlledByPlayer;
            public string AuraType;
            public float Radius;
            public float EndTime;
            public int LinkedFollowerCount;
            public readonly List<object> CreatedFollowerLinks = new List<object>();
        }

        private static readonly Dictionary<string, DynamicCommandAuraState> _activeDynamicCommandAuras =
            new Dictionary<string, DynamicCommandAuraState>(StringComparer.Ordinal);

        private static bool _reflectionResolved;
        private static PropertyInfo _entityIdProperty;
        private static FieldInfo _entityIdField;
        private static PropertyInfo _uniqueEntityIdProperty;
        private static PropertyInfo _isDestroyedProperty;
        private static PropertyInfo _destroyStartedProperty;
        private static PropertyInfo _isControlledByPlayerProperty;
        private static PropertyInfo _isControlledByAiProperty;
        private static PropertyInfo _isUnitProperty;
        private static PropertyInfo _isCombatUnitProperty;
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
        private static PropertyInfo _timeOfLastDamageProperty;
        private static PropertyInfo _captainOfThisEntityProperty;
        private static MethodInfo _isEnemyMethod;
        private static MethodInfo _healMethod;
        private static MethodInfo _removeCaptainMethod;
        private static readonly Dictionary<int, List<string>> _appliedBaselineEffects = new Dictionary<int, List<string>>();
        private static readonly Dictionary<int, List<string>> _appliedTimedStatusEffects = new Dictionary<int, List<string>>();
        private static MethodInfo _repairArmorMethod;
        private static MethodInfo _chargeShieldMethod;
        private static MethodInfo _rankUpMethod;
        private static MethodInfo _setCaptainMethod;
        private static MethodInfo _addAiTagMethod;
        private static FieldInfo _lastAiSkillActivationCheckTimeField;
        private static PropertyInfo _productionInfoProperty;
        private static FieldInfo _productionInfoField;
        private static FieldInfo _productionEntityIdField;
        private static FieldInfo _productionQueuedCountField;
        private static MethodInfo _attackMethod;
        private static MethodInfo _takeDamageMethod;
        private static MethodInfo _setActiveStatusEffectMethod;
        private static MethodInfo _addSpecificValueChangeMethod;
        private static Type _statusEffectEnumType;
        private static Type _durationTypeEnumType;
        private static Type _changeableValueEnumType;
        private static Type _specificValueChangeType;
        private static Type _specificAddTypeEnumType;
        private static Type _specificValidationTypeEnumType;
        private static FieldInfo _svcValueToAddField;
        private static FieldInfo _svcAddField;
        private static FieldInfo _svcValidationField;
        private static FieldInfo _svcValidUntilTimestampField;
        private static FieldInfo _svcOriginatorEntityIdField;
        private static Type _bankType;
        private static MethodInfo _bankDepositMethod;
        private static Type _existingControllersType;
        private static PropertyInfo _existingControllersInstanceProp;
        private static MethodInfo _entitiesMethod;

        private object _entityController;
        private UnitRuntimeMechanicsProfile _profile;
        private float _nextHealCheckTime;
        private float _nextAuraCheckTime;
        private float _nextSynergyCheckTime;
        private float _nextStealthCheckTime;
        private float _nextCommandCheckTime;
        private float _nextShieldRegenCheckTime;
        private float _lastShieldRegenTime;
        private float _nextTauntCheckTime;
        private float _nextNovaAvailableTime;
        private float _previousShield;
        private bool _hasInitializedShield;
        private bool _baselineApplied;
        private bool _defaultStanceApplied;
        private bool _startingAffixesApplied;
        private bool _defaultLoadoutApplied;
        private bool _startingVeterancyApplied;

        public static int RegisteredProfileCount
        {
            get
            {
                lock (_profiles)
                {
                    return _profiles.Count;
                }
            }
        }

        public static int RegisteredCombatXpCount
        {
            get
            {
                lock (_entityCombatXp)
                {
                    return _entityCombatXp.Count;
                }
            }
        }

        public readonly struct CombatXpEntry
        {
            public CombatXpEntry(int entityKeyId, int experience)
            {
                EntityKeyId = entityKeyId;
                Experience = experience;
            }

            public int EntityKeyId { get; }
            public int Experience { get; }
        }

        public static IReadOnlyList<string> RegisteredProfileIds
        {
            get
            {
                lock (_profiles)
                {
                    var ids = new List<string>(_profiles.Keys);
                    ids.Sort(StringComparer.Ordinal);
                    return ids;
                }
            }
        }

        public static int AppliedBaselineEffectsCount
        {
            get
            {
                lock (_appliedBaselineEffects)
                {
                    return _appliedBaselineEffects.Count;
                }
            }
        }

        public static IReadOnlyList<string> AppliedBaselineEffectNames
        {
            get
            {
                var names = new SortedSet<string>(StringComparer.Ordinal);
                lock (_appliedBaselineEffects)
                {
                    foreach (List<string> list in _appliedBaselineEffects.Values)
                    {
                        if (list == null)
                        {
                            continue;
                        }

                        for (int i = 0; i < list.Count; i++)
                        {
                            string name = list[i];
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                names.Add(name);
                            }
                        }
                    }
                }

                return new List<string>(names);
            }
        }

        public static IReadOnlyList<CombatXpEntry> CombatXpEntries
        {
            get
            {
                lock (_entityCombatXp)
                {
                    var entries = new List<CombatXpEntry>(_entityCombatXp.Count);
                    foreach (KeyValuePair<int, int> pair in _entityCombatXp)
                    {
                        entries.Add(new CombatXpEntry(pair.Key, pair.Value));
                    }

                    entries.Sort(delegate(CombatXpEntry a, CombatXpEntry b)
                    {
                        return b.Experience.CompareTo(a.Experience);
                    });
                    return entries;
                }
            }
        }

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

        /// <summary>
        /// Applies the loader-owned per-product AI production caps before the native
        /// IsProductionPossible gate. The native UnitCap is global, so it is never
        /// mutated for a unit declaration.
        /// </summary>
        public static bool ShouldBlockAiProduction(object producerObj)
        {
            try
            {
                if (producerObj == null)
                {
                    return false;
                }

                EnsureReflection(producerObj.GetType());
                if (_isControlledByAiProperty == null || !ReadBoolProperty(_isControlledByAiProperty, producerObj))
                {
                    return false;
                }

                object productionInfo = ReadProductionInfo(producerObj);
                string productId = ReadProductionEntityId(productionInfo);
                UnitRuntimeMechanicsProfile profile;
                if (string.IsNullOrWhiteSpace(productId) || !TryGetProfile(productId, out profile) ||
                    profile == null || profile.AiBehavior == null)
                {
                    return false;
                }

                ContentUnitAiBehaviorDefinition aiBehavior = profile.AiBehavior;
                bool isCombatUnit = IsCombatProduct(productId);
                int effectiveCap = RuntimeUnitAiBehaviorPolicy.EffectiveCap(
                    isCombatUnit, aiBehavior.unitCap, aiBehavior.combatUnitCap);
                if (effectiveCap <= 0)
                {
                    return false;
                }

                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return false;
                }

                int livingCount = 0;
                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits != null)
                {
                    foreach (object unit in aiUnits)
                    {
                        if (unit == null || !IsEntityAlive(unit) ||
                            !string.Equals(ReadEntityId(unit), productId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        livingCount++;
                    }
                }

                int queuedCount = 0;
                HashSet<EntityController> aiBuildings;
                try
                {
                    aiBuildings = existing.AiBuildings();
                }
                catch
                {
                    return false;
                }

                if (aiBuildings != null)
                {
                    foreach (object building in aiBuildings)
                    {
                        if (building == null || !IsEntityAlive(building))
                        {
                            continue;
                        }

                        object buildingProductionInfo = ReadProductionInfo(building);
                        if (!string.Equals(ReadProductionEntityId(buildingProductionInfo), productId,
                            StringComparison.Ordinal))
                        {
                            continue;
                        }

                        queuedCount += ReadProductionQueuedCount(buildingProductionInfo);
                        if (livingCount + queuedCount >= effectiveCap)
                        {
                            return true;
                        }
                    }
                }

                return !RuntimeUnitAiBehaviorPolicy.AllowsProduction(
                    livingCount, queuedCount, isCombatUnit,
                    aiBehavior.unitCap, aiBehavior.combatUnitCap);
            }
            catch
            {
                // An unavailable optional cap path must not block native production.
                return false;
            }
        }

        public static void ResetNativeAiSkillActivationInterval(object entityObj)
        {
            try
            {
                if (entityObj == null)
                {
                    return;
                }

                EnsureReflection(entityObj.GetType());
                if (_lastAiSkillActivationCheckTimeField == null)
                {
                    return;
                }

                UnitRuntimeMechanicsProfile profile;
                if (!TryGetProfile(ReadEntityId(entityObj), out profile) || profile == null ||
                    profile.AiBehavior == null || profile.AiBehavior.skillActivationCheckInterval <= 0f)
                {
                    return;
                }

                lock (_nativeAiSkillLogicalLastChecks)
                {
                    _nativeAiSkillLogicalLastChecks[GetCombatExperienceKey(entityObj)] = Time.time;
                }
                _lastAiSkillActivationCheckTimeField.SetValue(entityObj, Time.time);
            }
            catch
            {
                // A pooled controller reset must never block re-association.
            }
        }

        /// <summary>
        /// Bridges the declared interval to EntityController.FixedUpdate's native
        /// 0.25-second gate without modifying the native const or global state.
        /// </summary>
        public static void ApplyNativeAiSkillActivationInterval(object entityObj, bool afterNativeUpdate)
        {
            try
            {
                if (entityObj == null)
                {
                    return;
                }

                EnsureReflection(entityObj.GetType());
                if (_lastAiSkillActivationCheckTimeField == null)
                {
                    return;
                }

                string entityId = ReadEntityId(entityObj);
                UnitRuntimeMechanicsProfile profile;
                if (string.IsNullOrWhiteSpace(entityId) || !TryGetProfile(entityId, out profile) ||
                    profile == null || profile.AiBehavior == null)
                {
                    return;
                }

                float configuredInterval = profile.AiBehavior.skillActivationCheckInterval;
                if (float.IsNaN(configuredInterval) || float.IsInfinity(configuredInterval) ||
                    configuredInterval <= 0f ||
                    configuredInterval > RuntimeUnitAiBehaviorPolicy.MaxSkillActivationCheckInterval)
                {
                    return;
                }

                float now = Time.time;
                object rawLast = _lastAiSkillActivationCheckTimeField.GetValue(entityObj);
                float lastCheck = rawLast == null ? 0f : Convert.ToSingle(rawLast);
                if (!afterNativeUpdate && configuredInterval > RuntimeUnitAiBehaviorPolicy.NativeSkillActivationCheckInterval)
                {
                    int logicalKey = GetCombatExperienceKey(entityObj);
                    float logicalLastCheck;
                    lock (_nativeAiSkillLogicalLastChecks)
                    {
                        if (!_nativeAiSkillLogicalLastChecks.TryGetValue(logicalKey, out logicalLastCheck))
                        {
                            logicalLastCheck = lastCheck > now + 0.01f
                                ? lastCheck - (configuredInterval - RuntimeUnitAiBehaviorPolicy.NativeSkillActivationCheckInterval)
                                : lastCheck;
                            _nativeAiSkillLogicalLastChecks[logicalKey] = logicalLastCheck;
                        }
                    }

                    if (RuntimeUnitAiBehaviorPolicy.ShouldRunAiSkillCheck(
                        now, logicalLastCheck, configuredInterval))
                    {
                        _lastAiSkillActivationCheckTimeField.SetValue(
                            entityObj, now - RuntimeUnitAiBehaviorPolicy.NativeSkillActivationCheckInterval);
                    }
                    else
                    {
                        _lastAiSkillActivationCheckTimeField.SetValue(
                            entityObj,
                            RuntimeUnitAiBehaviorPolicy.DelayedNativeAiSkillCheckTimestamp(
                                logicalLastCheck, configuredInterval));
                    }
                }
                else if (!afterNativeUpdate && RuntimeUnitAiBehaviorPolicy.ShouldForceNativeAiSkillCheck(
                    now, lastCheck, configuredInterval))
                {
                    _lastAiSkillActivationCheckTimeField.SetValue(
                        entityObj, now - RuntimeUnitAiBehaviorPolicy.NativeSkillActivationCheckInterval);
                }
                else if (afterNativeUpdate && RuntimeUnitAiBehaviorPolicy.ShouldDelayNativeAiSkillCheck(
                    now, lastCheck, configuredInterval))
                {
                    lock (_nativeAiSkillLogicalLastChecks)
                    {
                        _nativeAiSkillLogicalLastChecks[GetCombatExperienceKey(entityObj)] = now;
                    }
                    _lastAiSkillActivationCheckTimeField.SetValue(
                        entityObj,
                        RuntimeUnitAiBehaviorPolicy.DelayedNativeAiSkillCheckTimestamp(
                            lastCheck, configuredInterval));
                }
            }
            catch
            {
                // Optional per-unit cadence must never break the native FixedUpdate.
            }
        }

        private static object ReadProductionInfo(object entityObj)
        {
            if (_productionInfoProperty != null)
            {
                object productionInfo = _productionInfoProperty.GetValue(entityObj, null);
                if (productionInfo != null)
                {
                    return productionInfo;
                }
            }

            return _productionInfoField == null ? null : _productionInfoField.GetValue(entityObj);
        }

        private static string ReadProductionEntityId(object productionInfo)
        {
            if (productionInfo == null)
            {
                return null;
            }

            if (_productionEntityIdField == null || _productionEntityIdField.DeclaringType != productionInfo.GetType())
            {
                _productionEntityIdField = productionInfo.GetType().GetField(
                    "entityId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            return _productionEntityIdField == null ? null : _productionEntityIdField.GetValue(productionInfo) as string;
        }

        private static int ReadProductionQueuedCount(object productionInfo)
        {
            if (productionInfo == null)
            {
                return 0;
            }

            if (_productionQueuedCountField == null || _productionQueuedCountField.DeclaringType != productionInfo.GetType())
            {
                _productionQueuedCountField = productionInfo.GetType().GetField(
                    "queuedCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            if (_productionQueuedCountField == null)
            {
                return 0;
            }

            object value = _productionQueuedCountField.GetValue(productionInfo);
            return value == null ? 0 : Math.Max(0, Convert.ToInt32(value));
        }

        private static bool IsCombatProduct(string entityId)
        {
            return EntityBalancingStore.HasRole(entityId, UnitRole.FrontalAttacker) ||
                EntityBalancingStore.HasRole(entityId, UnitRole.Supporter) ||
                EntityBalancingStore.HasRole(entityId, UnitRole.Fire) ||
                EntityBalancingStore.HasRole(entityId, UnitRole.Sniper) ||
                EntityBalancingStore.HasRole(entityId, UnitRole.MassDestruction);
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

            lock (_nativeAiSkillLogicalLastChecks)
            {
                _nativeAiSkillLogicalLastChecks.Clear();
            }

            lock (_activeDynamicCommandAuras)
            {
                foreach (DynamicCommandAuraState state in _activeDynamicCommandAuras.Values)
                {
                    ReleaseCommandAuraFollowerLinks(state);
                }
                _activeDynamicCommandAuras.Clear();
            }

            lock (_appliedCardChangeBoostKeys)
            {
                _appliedCardChangeBoostKeys.Clear();
            }

            lock (_appliedBaselineEffects)
            {
                _appliedBaselineEffects.Clear();
            }

            lock (_appliedTimedStatusEffects)
            {
                _appliedTimedStatusEffects.Clear();
            }
        }

        public static int GetCombatXpForEntity(object entityObj)
        {
            if (entityObj == null)
            {
                return 0;
            }

            int uniqueId = GetCombatExperienceKey(entityObj);
            lock (_entityCombatXp)
            {
                int xp;
                _entityCombatXp.TryGetValue(uniqueId, out xp);
                return xp;
            }
        }

        public static bool TryGrantCombatXp(object entityObj, int xpAmount, out int resultingXp, out int resultingRank)
        {
            resultingXp = 0;
            resultingRank = 0;
            if (entityObj == null || xpAmount <= 0)
            {
                return false;
            }

            EnsureReflection(entityObj.GetType());
            int uniqueId = GetCombatExperienceKey(entityObj);
            int currentRank = ReadIntProperty(_currentRankProperty, entityObj, 0);
            int maxRank = ReadIntProperty(_maxRankProperty, entityObj, 3);
            int threshold = 3;

            string entityId = ReadEntityId(entityObj);
            UnitRuntimeMechanicsProfile profile;
            if (TryGetProfile(entityId, out profile) && profile != null && profile.Experience != null)
            {
                if (profile.Experience.experiencePerLevel > 0)
                {
                    threshold = profile.Experience.experiencePerLevel * 3;
                }
                if (profile.Experience.maxLevel > 0)
                {
                    maxRank = profile.Experience.maxLevel;
                }
            }

            int currentXp;
            int levelsToPromote = 0;
            lock (_entityCombatXp)
            {
                _entityCombatXp.TryGetValue(uniqueId, out currentXp);
                currentXp += xpAmount;
                while (currentXp >= threshold && (currentRank + levelsToPromote) < maxRank)
                {
                    currentXp -= threshold;
                    levelsToPromote++;
                }
                _entityCombatXp[uniqueId] = currentXp;
            }

            if (levelsToPromote > 0)
            {
                if (_rankUpMethod != null)
                {
                    try
                    {
                        _rankUpMethod.Invoke(entityObj, new object[] { levelsToPromote });
                    }
                    catch
                    {
                    }
                }

                ApplyUnlimitedStatusEffect(entityObj, "CombatBoost");
                float maxHp = ReadFloatProperty(_maxHealthProperty, entityObj, 100f);
                InvokeHeal(entityObj, Mathf.Max(5f, maxHp * 0.10f * levelsToPromote), entityObj);
            }

            resultingXp = currentXp;
            resultingRank = ReadIntProperty(_currentRankProperty, entityObj, currentRank + levelsToPromote);
            return true;
        }

        public static bool TrySetUnitVeterancyRank(object entityObj, int targetRank, out int resultingRank)
        {
            resultingRank = 0;
            if (entityObj == null)
            {
                return false;
            }

            EnsureReflection(entityObj.GetType());
            int currentRank = ReadIntProperty(_currentRankProperty, entityObj, 0);
            int maxRank = ReadIntProperty(_maxRankProperty, entityObj, 3);
            string entityId = ReadEntityId(entityObj);
            UnitRuntimeMechanicsProfile profile;
            if (TryGetProfile(entityId, out profile) && profile != null && profile.Experience != null)
            {
                if (profile.Experience.maxLevel > 0)
                {
                    maxRank = profile.Experience.maxLevel;
                }
            }
            int clampedTarget = (int)Mathf.Clamp((float)targetRank, 0f, (float)maxRank);

            if (clampedTarget > currentRank)
            {
                int diff = clampedTarget - currentRank;
                if (_rankUpMethod != null)
                {
                    try
                    {
                        _rankUpMethod.Invoke(entityObj, new object[] { diff });
                    }
                    catch
                    {
                    }
                }

                ApplyUnlimitedStatusEffect(entityObj, "CombatBoost");
                float maxHp = ReadFloatProperty(_maxHealthProperty, entityObj, 100f);
                InvokeHeal(entityObj, Mathf.Max(5f, maxHp * 0.10f * diff), entityObj);
            }

            resultingRank = ReadIntProperty(_currentRankProperty, entityObj, clampedTarget);
            return true;
        }

        public static void GetUnitVeterancyStats(
            object entityObj,
            out int currentRank,
            out int maxRank,
            out int combatXp,
            out int xpToNextRank,
            out float hpBonusMult,
            out float dmgBonusMult)
        {
            if (entityObj == null)
            {
                currentRank = 0;
                maxRank = 0;
                combatXp = 0;
                xpToNextRank = 0;
                hpBonusMult = 1.0f;
                dmgBonusMult = 1.0f;
                return;
            }

            EnsureReflection(entityObj.GetType());
            currentRank = ReadIntProperty(_currentRankProperty, entityObj, 0);
            maxRank = ReadIntProperty(_maxRankProperty, entityObj, 3);
            int uniqueId = GetCombatExperienceKey(entityObj);
            lock (_entityCombatXp)
            {
                _entityCombatXp.TryGetValue(uniqueId, out combatXp);
            }

            int threshold = 3;
            string entityId = ReadEntityId(entityObj);
            UnitRuntimeMechanicsProfile profile;
            if (TryGetProfile(entityId, out profile) && profile != null && profile.Experience != null)
            {
                if (profile.Experience.experiencePerLevel > 0)
                {
                    threshold = profile.Experience.experiencePerLevel * 3;
                }
                if (profile.Experience.maxLevel > 0)
                {
                    maxRank = profile.Experience.maxLevel;
                }
            }

            xpToNextRank = currentRank >= maxRank ? 0 : Mathf.Max(0, threshold - combatXp);
            hpBonusMult = 1.0f + (currentRank * 0.10f);
            dmgBonusMult = 1.0f + (currentRank * 0.10f);
        }

        public static bool TryDispatchDynamicCommandAura(
            object captainObj,
            string auraType,
            float radius,
            float durationSeconds,
            out int linkedFollowersCount)
        {
            linkedFollowersCount = 0;
            if (captainObj == null)
            {
                return false;
            }

            EnsureReflection(captainObj.GetType());
            float clampedRadius = Mathf.Clamp(radius, 3.0f, 50.0f);
            float clampedDuration = Mathf.Clamp(durationSeconds, 1.0f, 60.0f);
            string sanitizedAura = string.IsNullOrWhiteSpace(auraType) ? "Inspire" : auraType.Trim();

            Vector3 selfPos = ReadVector3Property(_positionProperty, captainObj);
            float rangeSq = clampedRadius * clampedRadius;
            string captainEntityId = ReadEntityId(captainObj) ?? "captain";
            string captainKeyId = ReadIntProperty(_uniqueEntityIdProperty, captainObj, -1).ToString();
            bool isPlayer = ReadBoolProperty(_isControlledByPlayerProperty, captainObj);
            var createdLinks = new List<object>();
            var previouslyCreatedLinks = new List<object>();
            lock (_activeDynamicCommandAuras)
            {
                if (_activeDynamicCommandAuras.TryGetValue(captainKeyId, out DynamicCommandAuraState existing))
                {
                    previouslyCreatedLinks.AddRange(existing.CreatedFollowerLinks);
                }
            }

            IEnumerable entities = GetActiveEntities();
            if (entities != null)
            {
                foreach (object candidate in entities)
                {
                    if (candidate == null || ReferenceEquals(candidate, captainObj) || !IsEntityAlive(candidate) || IsEnemy(captainObj, candidate))
                    {
                        continue;
                    }

                    if (_isUnitProperty != null && !ReadBoolProperty(_isUnitProperty, candidate))
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

                    if (_setCaptainMethod != null)
                    {
                        try
                        {
                            object curCap = _captainOfThisEntityProperty != null ? _captainOfThisEntityProperty.GetValue(candidate, null) : null;
                            if (curCap == null)
                            {
                                _setCaptainMethod.Invoke(candidate, new[] { captainObj });
                                createdLinks.Add(candidate);
                            }
                            else if (ReferenceEquals(curCap, captainObj) && ContainsReference(previouslyCreatedLinks, candidate))
                            {
                                createdLinks.Add(candidate);
                            }
                        }
                        catch
                        {
                        }
                    }

                    string tag = "cmd_aura:" + captainKeyId;
                    if (string.Equals(sanitizedAura, "Aegis", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(sanitizedAura, "Defend", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyTimedStatChange(candidate, "ArmorProtection", 0.20f, clampedDuration, tag + ":arm");
                        ApplyTimedStatusEffect(candidate, "Defender", clampedDuration);
                    }
                    else if (string.Equals(sanitizedAura, "Swiftness", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(sanitizedAura, "Move", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyTimedStatChange(candidate, "MoveSpeed", 0.20f, clampedDuration, tag + ":spd");
                        ApplyTimedStatusEffect(candidate, "Turbo", clampedDuration);
                    }
                    else
                    {
                        // Inspire / Attack
                        ApplyTimedStatChange(candidate, "Damage", 0.15f, clampedDuration, tag + ":dmg");
                        ApplyTimedStatusEffect(candidate, "SquadEffect", clampedDuration);
                    }

                    linkedFollowersCount++;
                    if (linkedFollowersCount >= 16)
                    {
                        break;
                    }
                }
            }

            float now = 0f;
            try
            {
                now = Time.time;
            }
            catch
            {
            }

            lock (_activeDynamicCommandAuras)
            {
                if (_activeDynamicCommandAuras.TryGetValue(captainKeyId, out DynamicCommandAuraState existing))
                {
                    ReleaseCommandAuraFollowerLinks(existing, createdLinks);
                }

                var state = new DynamicCommandAuraState
                {
                    CaptainObj = captainObj,
                    CaptainKeyId = captainKeyId,
                    CaptainEntityId = captainEntityId,
                    IsControlledByPlayer = isPlayer,
                    AuraType = sanitizedAura,
                    Radius = clampedRadius,
                    EndTime = now + clampedDuration,
                    LinkedFollowerCount = linkedFollowersCount
                };
                state.CreatedFollowerLinks.AddRange(createdLinks);
                _activeDynamicCommandAuras[captainKeyId] = state;
            }

            return true;
        }

        private static bool ContainsReference(List<object> items, object target)
        {
            if (items == null || target == null)
            {
                return false;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], target))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReleaseCommandAuraFollowerLinks(DynamicCommandAuraState state, List<object> preservedLinks = null)
        {
            if (state == null || state.CreatedFollowerLinks == null || state.CreatedFollowerLinks.Count == 0)
            {
                return;
            }

            for (int i = 0; i < state.CreatedFollowerLinks.Count; i++)
            {
                object follower = state.CreatedFollowerLinks[i];
                if (follower == null)
                {
                    continue;
                }

                if (preservedLinks != null && ContainsReference(preservedLinks, follower))
                {
                    continue;
                }

                try
                {
                    if (_captainOfThisEntityProperty != null)
                    {
                        object curCap = _captainOfThisEntityProperty.GetValue(follower, null);
                        if (ReferenceEquals(curCap, state.CaptainObj))
                        {
                            if (_removeCaptainMethod != null)
                            {
                                _removeCaptainMethod.Invoke(follower, new[] { state.CaptainObj });
                            }
                            else
                            {
                                _captainOfThisEntityProperty.SetValue(follower, null, null);
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            state.CreatedFollowerLinks.Clear();
        }

        public static bool TryClearDynamicCommandAura(object captainObj, out int clearedFollowersCount)
        {
            clearedFollowersCount = 0;
            if (captainObj == null)
            {
                return false;
            }

            EnsureReflection(captainObj.GetType());
            string captainKeyId = ReadIntProperty(_uniqueEntityIdProperty, captainObj, -1).ToString();

            lock (_activeDynamicCommandAuras)
            {
                if (_activeDynamicCommandAuras.TryGetValue(captainKeyId, out DynamicCommandAuraState state))
                {
                    clearedFollowersCount = state.CreatedFollowerLinks.Count;
                    ReleaseCommandAuraFollowerLinks(state);
                    _activeDynamicCommandAuras.Remove(captainKeyId);
                }
            }

            IEnumerable entities = GetActiveEntities();
            if (entities != null && _captainOfThisEntityProperty != null)
            {
                foreach (object candidate in entities)
                {
                    if (candidate == null || ReferenceEquals(candidate, captainObj))
                    {
                        continue;
                    }

                    try
                    {
                        object curCap = _captainOfThisEntityProperty.GetValue(candidate, null);
                        if (ReferenceEquals(curCap, captainObj))
                        {
                            if (_removeCaptainMethod != null)
                            {
                                _removeCaptainMethod.Invoke(candidate, new[] { captainObj });
                            }
                            else
                            {
                                _captainOfThisEntityProperty.SetValue(candidate, null, null);
                            }
                            clearedFollowersCount++;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return true;
        }

        public static IReadOnlyList<CommandAuraDiagnosticRow> GetActiveCommandAuras(int maxRows, out int omittedCount)
        {
            float now = 0f;
            try
            {
                now = Time.time;
            }
            catch
            {
            }

            var list = new List<CommandAuraDiagnosticRow>();
            lock (_activeDynamicCommandAuras)
            {
                var expiredAuras = new List<DynamicCommandAuraState>();
                foreach (KeyValuePair<string, DynamicCommandAuraState> pair in _activeDynamicCommandAuras)
                {
                    if (now >= pair.Value.EndTime)
                    {
                        expiredAuras.Add(pair.Value);
                    }
                }

                for (int i = 0; i < expiredAuras.Count; i++)
                {
                    DynamicCommandAuraState expired = expiredAuras[i];
                    ReleaseCommandAuraFollowerLinks(expired);
                    _activeDynamicCommandAuras.Remove(expired.CaptainKeyId);
                }

                int total = _activeDynamicCommandAuras.Count;
                int toTake = Math.Min(total, maxRows);
                omittedCount = Math.Max(0, total - toTake);
                int taken = 0;
                foreach (KeyValuePair<string, DynamicCommandAuraState> pair in _activeDynamicCommandAuras)
                {
                    if (taken++ >= toTake)
                    {
                        break;
                    }

                    DynamicCommandAuraState state = pair.Value;
                    list.Add(new CommandAuraDiagnosticRow(
                        state.CaptainKeyId,
                        state.CaptainEntityId,
                        state.IsControlledByPlayer,
                        state.LinkedFollowerCount,
                        state.Radius,
                        state.AuraType,
                        Mathf.Max(0f, state.EndTime - now)));
                }
            }

            return list.AsReadOnly();
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
            if (profile.StatusEffects != null && profile.StatusEffects.Length > 0)
            {
                parts.Add("Efectos temporales: " + profile.StatusEffects.Length);
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
            if (profile.Synergies != null && profile.Synergies.Length > 0)
            {
                parts.Add("Sinergias: " + profile.Synergies.Length);
            }
            if (profile.Stealth != null && profile.Stealth.cloakDuration > 0f && profile.Stealth.stealthLevel > 0f)
            {
                parts.Add("Sigilo: " + profile.Stealth.cloakDuration.ToString("0.#") + "s");
            }
            if (profile.Command != null && profile.Command.commandRange > 0f)
            {
                parts.Add("Mando: " + (string.IsNullOrWhiteSpace(profile.Command.commandType) ? "Squad" : profile.Command.commandType) +
                    " (" + profile.Command.commandRange.ToString("0.#") + "m)");
            }
            if (profile.ExperienceReward != null && profile.ExperienceReward.experienceAmount > 0)
            {
                parts.Add("Recompensa: +" + profile.ExperienceReward.experienceAmount);
            }
            if (profile.ShieldRegen != null && profile.ShieldRegen.rechargeRate > 0f)
            {
                parts.Add("Regen Escudo: +" + profile.ShieldRegen.rechargeRate.ToString("0.#") + "/s (" + profile.ShieldRegen.rechargeDelay.ToString("0.#") + "s delay)");
            }
            if (profile.VampiricLeech != null && profile.VampiricLeech.siphonRatio > 0f)
            {
                parts.Add("Drenaje Vampírico: " + (profile.VampiricLeech.siphonRatio * 100f).ToString("0") + "%");
            }
            if (profile.Taunt != null && profile.Taunt.tauntRadius > 0f)
            {
                parts.Add("Provocación (Taunt): " + profile.Taunt.tauntRadius.ToString("0.#") + "m");
            }
            if (profile.ShieldOverloadNova != null && profile.ShieldOverloadNova.novaRadius > 0f)
            {
                parts.Add("Nova EMP: " + profile.ShieldOverloadNova.damage.ToString("0") + " dmg (" + profile.ShieldOverloadNova.novaRadius.ToString("0.#") + "m)");
            }
            if (!string.IsNullOrWhiteSpace(profile.DefaultTacticalStance))
            {
                parts.Add("Postura: " + profile.DefaultTacticalStance);
            }
            if (profile.StartingAffixes != null && profile.StartingAffixes.Length > 0)
            {
                parts.Add("Afijos: " + string.Join(", ", profile.StartingAffixes));
            }
            if (!string.IsNullOrWhiteSpace(profile.DefaultLoadout))
            {
                parts.Add("Loadout: " + profile.DefaultLoadout);
            }
            if (profile.StartingVeterancyRank.HasValue && profile.StartingVeterancyRank.Value > 0)
            {
                parts.Add("Rango Inicial: " + profile.StartingVeterancyRank.Value);
            }

            return parts.Count > 0 ? string.Join(" • ", parts) : null;
        }

        internal static int GetEntityTrackingKey(object entityObj)
        {
            return GetCombatExperienceKey(entityObj);
        }

        public static void OnBattlefieldRegistryReset()
        {
            lock (_entityCombatXp)
            {
                _entityCombatXp.Clear();
            }

            lock (_nativeAiSkillLogicalLastChecks)
            {
                _nativeAiSkillLogicalLastChecks.Clear();
            }

            lock (_activeDynamicCommandAuras)
            {
                foreach (DynamicCommandAuraState state in _activeDynamicCommandAuras.Values)
                {
                    ReleaseCommandAuraFollowerLinks(state);
                }
                _activeDynamicCommandAuras.Clear();
            }

            lock (_appliedCardChangeBoostKeys)
            {
                _appliedCardChangeBoostKeys.Clear();
            }

            lock (_appliedBaselineEffects)
            {
                _appliedBaselineEffects.Clear();
            }

            lock (_appliedTimedStatusEffects)
            {
                _appliedTimedStatusEffects.Clear();
            }
        }

        private static int GetCombatExperienceKey(object entityObj)
        {
            if (entityObj == null)
            {
                return 0;
            }

            EnsureReflection(entityObj.GetType());
            int uniqueId = ReadIntProperty(_uniqueEntityIdProperty, entityObj, -1);
            return uniqueId >= 0 ? uniqueId : entityObj.GetHashCode();
        }

        private static void RemoveCombatExperience(object entityObj)
        {
            if (entityObj == null)
            {
                return;
            }

            try
            {
                int uniqueId = GetCombatExperienceKey(entityObj);
                lock (_entityCombatXp)
                {
                    _entityCombatXp.Remove(uniqueId);
                }
            }
            catch
            {
                // Lifecycle cleanup must not interfere with native destruction.
            }
        }

        private static void RemoveAppliedCardChangeBoostKeys(object entityObj)
        {
            if (entityObj == null)
            {
                return;
            }

            try
            {
                EnsureReflection(entityObj.GetType());
                int uniqueId = ReadIntProperty(_uniqueEntityIdProperty, entityObj, -1);
                if (uniqueId > 0)
                {
                    string prefix = uniqueId.ToString() + "|";
                    lock (_appliedCardChangeBoostKeys)
                    {
                        _appliedCardChangeBoostKeys.RemoveWhere(k => k.StartsWith(prefix, StringComparison.Ordinal));
                    }
                }
            }
            catch
            {
                // Key cleanup must not interfere with native destruction.
            }
        }

        private static void RemoveDynamicCommandAuraForEntity(object entityObj)
        {
            if (entityObj == null)
            {
                return;
            }
            try
            {
                EnsureReflection(entityObj.GetType());
                string captainKeyId = ReadIntProperty(_uniqueEntityIdProperty, entityObj, -1).ToString();
                lock (_activeDynamicCommandAuras)
                {
                    if (_activeDynamicCommandAuras.TryGetValue(captainKeyId, out DynamicCommandAuraState state))
                    {
                        ReleaseCommandAuraFollowerLinks(state);
                        _activeDynamicCommandAuras.Remove(captainKeyId);
                    }
                }
            }
            catch
            {
            }
        }

        public static void OnEntityDestroyed(object entityObj)
        {
            RemoveCombatExperience(entityObj);
            RemoveAppliedCardChangeBoostKeys(entityObj);
            ClearRuntimeMechanicsByObject(entityObj);
            RemoveDynamicCommandAuraForEntity(entityObj);
            try
            {
                int key = GetCombatExperienceKey(entityObj);
                lock (_nativeAiSkillLogicalLastChecks)
                {
                    _nativeAiSkillLogicalLastChecks.Remove(key);
                }
                if (key != 0)
                {
                    RuntimeGameplayCommandDirector.OnEntityDestroyed(key.ToString());
                }
            }
            catch
            {
                // Destruction cleanup must never interfere with native destruction.
            }
        }

        /// <summary>
        /// Invoked when an entity dies (Harmony postfix on EntityController.Die).
        /// Cleans up runtime mechanics state and combat experience tracking so
        /// a dying unit does not retain stale buffs/auras or reward duplicates.
        /// </summary>
        public static void OnEntityDied(object entityObj)
        {
            try
            {
                RemoveCombatExperience(entityObj);
                RemoveAppliedCardChangeBoostKeys(entityObj);
                ClearRuntimeMechanicsByObject(entityObj);
                RemoveDynamicCommandAuraForEntity(entityObj);
                int key = GetCombatExperienceKey(entityObj);
                lock (_nativeAiSkillLogicalLastChecks)
                {
                    _nativeAiSkillLogicalLastChecks.Remove(key);
                }
                if (key != 0)
                {
                    RuntimeGameplayCommandDirector.OnEntityDestroyed(key.ToString());
                }
            }
            catch
            {
                // Death cleanup must never interfere with native destruction.
            }
        }

        /// <summary>
        /// Invoked when an entity is healed (Harmony postfix on EntityController.Heal).
        /// Extension point for healing-aura synchronization and on-heal status effects.
        /// </summary>
        public static void OnEntityHealed(object entityObj, float amount)
        {
            if (entityObj == null || amount <= 0f)
            {
                return;
            }

            try
            {
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
                if (uniqueId <= 0)
                {
                    return;
                }

                string healKey = uniqueId.ToString() + "|heal";
                bool alreadyApplied;
                lock (_appliedCardChangeBoostKeys)
                {
                    alreadyApplied = _appliedCardChangeBoostKeys.Contains(healKey);
                    if (!alreadyApplied)
                    {
                        _appliedCardChangeBoostKeys.Add(healKey);
                    }
                }
                if (alreadyApplied)
                {
                    return;
                }

                Assembly assembly = entityObj.GetType().Assembly;
                CardChangeRequest request = CardChangeRequest.Create(
                    "MaxHealth",
                    "Add",
                    amount * 0.1f,
                    false,
                    null,
                    "Friendly",
                    true,
                    null,
                    null,
                    "heal:" + entityId);
                RuntimeCardChangeApplier.ApplyCardChange(assembly, uniqueId, request);
            }
            catch
            {
                // Guarded: healing augmentation must never break native healing.
            }
        }

        /// <summary>
        /// Invoked when an entity's armor is repaired (Harmony postfix on
        /// EntityController.RepairArmor). Extension point for repair-aura sync.
        /// </summary>
        public static void OnEntityRepairedArmor(object entityObj, float amount)
        {
            if (entityObj == null || amount <= 0f)
            {
                return;
            }

            try
            {
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
                if (uniqueId <= 0)
                {
                    return;
                }

                string repairKey = uniqueId.ToString() + "|repair";
                bool alreadyApplied;
                lock (_appliedCardChangeBoostKeys)
                {
                    alreadyApplied = _appliedCardChangeBoostKeys.Contains(repairKey);
                    if (!alreadyApplied)
                    {
                        _appliedCardChangeBoostKeys.Add(repairKey);
                    }
                }
                if (alreadyApplied)
                {
                    return;
                }

                Assembly assembly = entityObj.GetType().Assembly;
                CardChangeRequest request = CardChangeRequest.Create(
                    "MaxArmor",
                    "Add",
                    amount * 0.1f,
                    false,
                    null,
                    "Friendly",
                    true,
                    null,
                    null,
                    "repair:" + entityId);
                RuntimeCardChangeApplier.ApplyCardChange(assembly, uniqueId, request);
            }
            catch
            {
                // Guarded: repair augmentation must never break native repair.
            }
        }

        private static void ClearRuntimeMechanicsByObject(object entityObj)
        {
            if (entityObj == null)
            {
                return;
            }

            try
            {
                Type entityType = entityObj.GetType();
                PropertyInfo gameObjectProperty = entityType.GetProperty("gameObject",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (gameObjectProperty != null)
                {
                    GameObject gameObject = gameObjectProperty.GetValue(entityObj, null) as GameObject;
                    ClearRuntimeMechanics(gameObject);
                    return;
                }

                FieldInfo gameObjectField = entityType.GetField("gameObject",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (gameObjectField != null)
                {
                    GameObject gameObject = gameObjectField.GetValue(entityObj) as GameObject;
                    ClearRuntimeMechanics(gameObject);
                }
            }
            catch
            {
                // Best-effort cleanup; never throw from a death hook.
            }
        }

        private static void ClearRuntimeMechanics(GameObject rootObject)
        {
            if (rootObject == null)
            {
                return;
            }

            RuntimeUnitMechanicsDirector director = rootObject.GetComponent<RuntimeUnitMechanicsDirector>();
            if (director != null)
            {
                director.ClearRuntimeState();
            }
        }

        private void ClearRuntimeState()
        {
            if (_entityController != null)
            {
                try
                {
                    int keyInt = GetCombatExperienceKey(_entityController);
                    string keyStr = keyInt.ToString();
                    RevertBaselineStatusEffects(_entityController, keyInt);
                    RevertTimedStatusEffects(_entityController, keyInt);
                    RuntimeGameplayCommandDirector.TryRemoveUnitAffix(keyStr, "All");
                    RuntimeGameplayCommandDirector.ResetUnitTacticalStance(keyStr);
                    RuntimeGameplayCommandDirector.TryApplyUnitLoadout(keyStr, "Standard");
                }
                catch { }
            }
            _entityController = null;
            _profile = null;
            _nextHealCheckTime = 0f;
            _nextAuraCheckTime = 0f;
            _nextSynergyCheckTime = 0f;
            _nextStealthCheckTime = 0f;
            _nextCommandCheckTime = 0f;
            _nextShieldRegenCheckTime = 0f;
            _lastShieldRegenTime = 0f;
            _nextTauntCheckTime = 0f;
            _nextNovaAvailableTime = 0f;
            _previousShield = 0f;
            _hasInitializedShield = false;
            _baselineApplied = false;
            _defaultStanceApplied = false;
            _startingAffixesApplied = false;
            _defaultLoadoutApplied = false;
            _startingVeterancyApplied = false;
        }

        private static void RevertBaselineStatusEffects(object entityObj, int key)
        {
            List<string> applied;
            lock (_appliedBaselineEffects)
            {
                if (!_appliedBaselineEffects.TryGetValue(key, out applied) || applied == null || applied.Count == 0)
                {
                    return;
                }
                _appliedBaselineEffects.Remove(key);
            }

            EnsureReflection(entityObj.GetType());
            for (int i = 0; i < applied.Count; i++)
            {
                string effectName = applied[i];
                if (string.IsNullOrWhiteSpace(effectName))
                {
                    continue;
                }
                try
                {
                    if (_statusEffectEnumType == null)
                    {
                        continue;
                    }
                    object parsedEffect = Enum.Parse(_statusEffectEnumType, effectName, true);
                    if (entityObj.GetType().GetMethod("RemoveStatusEffect") != null)
                    {
                        entityObj.GetType().GetMethod("RemoveStatusEffect").Invoke(entityObj, new[] { parsedEffect });
                    }
                }
                catch
                {
                    // Best-effort cleanup; never block entity reuse.
                }
            }
        }

        private static void RevertTimedStatusEffects(object entityObj, int key)
        {
            if (entityObj == null)
            {
                return;
            }

            List<string> applied;
            lock (_appliedTimedStatusEffects)
            {
                if (!_appliedTimedStatusEffects.TryGetValue(key, out applied) || applied == null || applied.Count == 0)
                {
                    return;
                }
                _appliedTimedStatusEffects.Remove(key);
            }

            EnsureReflection(entityObj.GetType());
            MethodInfo removeStatusEffect = entityObj.GetType().GetMethod("RemoveStatusEffect",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (removeStatusEffect == null || _statusEffectEnumType == null)
            {
                return;
            }

            for (int i = 0; i < applied.Count; i++)
            {
                string effectName = applied[i];
                if (string.IsNullOrWhiteSpace(effectName))
                {
                    continue;
                }

                try
                {
                    object parsedEffect = Enum.Parse(_statusEffectEnumType, effectName, true);
                    removeStatusEffect.Invoke(entityObj, new[] { parsedEffect });
                }
                catch
                {
                    // Best-effort cleanup; never block entity reuse.
                }
            }
        }

        private static void RevertCommandLinks(object entityObj)
        {
            if (entityObj == null || _captainOfThisEntityProperty == null)
            {
                return;
            }

            IEnumerable entities = GetActiveEntities();
            if (entities == null)
            {
                return;
            }

            foreach (object candidate in entities)
            {
                if (candidate == null || ReferenceEquals(candidate, entityObj))
                {
                    continue;
                }
                try
                {
                    object currentCaptain = _captainOfThisEntityProperty.GetValue(candidate, null);
                    if (!ReferenceEquals(currentCaptain, entityObj))
                    {
                        continue;
                    }
                    if (_removeCaptainMethod != null)
                    {
                        _removeCaptainMethod.Invoke(candidate, new[] { entityObj });
                    }
                    else
                    {
                        _captainOfThisEntityProperty.SetValue(candidate, null, null);
                    }
                }
                catch
                {
                    // Best-effort cleanup; never block entity reuse.
                }
            }
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
                int combatExperienceKey = GetCombatExperienceKey(entityObj);
                RevertBaselineStatusEffects(entityObj, combatExperienceKey);
                RevertTimedStatusEffects(entityObj, combatExperienceKey);
                RevertCommandLinks(entityObj);
                try
                {
                    string resetKey = combatExperienceKey.ToString();
                    RuntimeGameplayCommandDirector.TryRemoveUnitAffix(resetKey, "All");
                    RuntimeGameplayCommandDirector.ResetUnitTacticalStance(resetKey);
                }
                catch { }

                string entityId = ReadEntityId(entityObj);
                if (string.IsNullOrWhiteSpace(entityId))
                {
                    ClearRuntimeMechanics(rootObject);
                    return;
                }

                UnitRuntimeMechanicsProfile profile;
                if (!TryGetProfile(entityId, out profile) || profile == null)
                {
                    ClearRuntimeMechanics(rootObject);
                    return;
                }

                ResetNativeAiSkillActivationInterval(entityObj);

                if (profile.BaselineStatusEffects != null && profile.BaselineStatusEffects.Length > 0)
                {
                    ApplyBaselineStatusEffects(entityObj, profile.BaselineStatusEffects);
                    lock (_appliedBaselineEffects)
                    {
                        _appliedBaselineEffects[combatExperienceKey] = new List<string>(profile.BaselineStatusEffects);
                    }
                }

                if (!profile.HasRuntimeComponentMechanics)
                {
                    ClearRuntimeMechanics(rootObject);
                    return;
                }

                RuntimeUnitMechanicsDirector director = rootObject.GetComponent<RuntimeUnitMechanicsDirector>();
                if (director == null)
                {
                    director = rootObject.AddComponent<RuntimeUnitMechanicsDirector>();
                }

                director.InitializeOrRefresh(entityObj, profile);
                AttachVisualDirectorIfAvailable(entityObj);
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
                float targetHealth = ReadFloatProperty(_currentHealthProperty, targetObj, 1f);
                bool targetKilled = targetHealth <= 0f || ReadBoolProperty(_destroyStartedProperty, targetObj);

                string originatorId = ReadEntityId(originatorObj);
                UnitRuntimeMechanicsProfile profile = null;
                if (!string.IsNullOrWhiteSpace(originatorId))
                {
                    TryGetProfile(originatorId, out profile);
                }

                if (profile != null && profile.HasOnDamageDealtMechanics && IsEntityAlive(originatorObj) &&
                    IsEnemy(originatorObj, targetObj))
                {
                    // 1. Combat Boosts on hit (visual status effect + exact numeric SpecificValueChange)
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
                            string boostOriginatorId = "boost:" + (string.IsNullOrWhiteSpace(boost.id) ? i.ToString() : boost.id);
                            ApplyTimedStatChange(originatorObj, boost.statName, boost.multiplier - 1f, boost.duration, boostOriginatorId);

                            // Permanent card-change augmentation (native data-driven pipeline):
                            // the first time a given combat boost triggers for a unit, also apply
                            // it as a persistent card change so the unit keeps the edge after the
                            // timed effect expires. Tracked per (unit, boost) to avoid stacking.
                            try
                            {
                               int originatorUniqueId = ReadIntProperty(_uniqueEntityIdProperty, originatorObj, -1);
                               if (originatorUniqueId > 0)
                               {
                                   string boostKey = originatorUniqueId.ToString() + "|" + (string.IsNullOrWhiteSpace(boost.id) ? i.ToString() : boost.id);
                                   bool alreadyApplied;
                                   lock (_appliedCardChangeBoostKeys)
                                   {
                                       alreadyApplied = _appliedCardChangeBoostKeys.Contains(boostKey);
                                       if (!alreadyApplied)
                                       {
                                           _appliedCardChangeBoostKeys.Add(boostKey);
                                       }
                                   }
                                   if (!alreadyApplied)
                                   {
                                       Assembly boostAssembly = originatorObj.GetType().Assembly;
                                       CardChangeRequest cardRequest = CardChangeRequest.Create(
                                           boost.statName,
                                           "Add",
                                           boost.multiplier - 1f,
                                           true,
                                           null,
                                           "Friendly",
                                           false,
                                           null,
                                           null,
                                           boostOriginatorId);
                                       RuntimeCardChangeApplier.ApplyCardChange(boostAssembly, originatorUniqueId, cardRequest);
                                   }
                               }
                            }
                            catch
                            {
                               // Guarded: card-change augmentation must never break the boost.
                            }
                        }
                    }

                    // 2. Experience & Veterancy RankUp
                    if (profile.Experience != null && profile.Experience.maxLevel > 0)
                    {
                        int xpGain = targetKilled ? 3 : 1;
                        if (profile.Experience.experienceGainMultiplier > 0f)
                        {
                            xpGain = Mathf.Max(1, Mathf.RoundToInt(xpGain * profile.Experience.experienceGainMultiplier));
                        }

                        int uniqueId = GetCombatExperienceKey(originatorObj);
                        int threshold = Mathf.Max(1, profile.Experience.experiencePerLevel > 0
                            ? profile.Experience.experiencePerLevel * 3
                            : 3);

                        int currentRank = ReadIntProperty(_currentRankProperty, originatorObj, 0);
                        int maxRank = ReadIntProperty(_maxRankProperty, originatorObj, profile.Experience.maxLevel);
                        int maxAllowedRank = Mathf.Min(maxRank, profile.Experience.maxLevel);

                        bool shouldPromote = false;
                        if (currentRank < maxAllowedRank)
                        {
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
                        }

                        if (shouldPromote)
                        {
                            if (_rankUpMethod != null)
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

                            // Persistent card-change augmentation on promotion (native data-driven
                            // pipeline): the first time a unit reaches a new rank, also grant a
                            // permanent MaxHealth bonus so the veterancy edge survives. Tracked
                            // per (unit, rank) to avoid stacking.
                            try
                            {
                                Assembly rankAssembly = originatorObj.GetType().Assembly;
                                int rankUniqueId = ReadIntProperty(_uniqueEntityIdProperty, originatorObj, -1);
                                if (rankUniqueId > 0)
                                {
                                    string rankKey = rankUniqueId.ToString() + "|rankup|" + (currentRank + 1).ToString();
                                    bool alreadyApplied;
                                    lock (_appliedCardChangeBoostKeys)
                                    {
                                        alreadyApplied = _appliedCardChangeBoostKeys.Contains(rankKey);
                                        if (!alreadyApplied)
                                        {
                                            _appliedCardChangeBoostKeys.Add(rankKey);
                                        }
                                    }
                                    if (!alreadyApplied)
                                    {
                                        float maxHp = ReadFloatProperty(_maxHealthProperty, originatorObj, 100f);
                                        float rankBonus = profile.Experience.levelUpBonus > 0f ? profile.Experience.levelUpBonus : 0.1f;
                                        CardChangeRequest rankRequest = CardChangeRequest.Create(
                                            "MaxHealth",
                                            "Add",
                                            Mathf.Max(1f, maxHp * rankBonus * 0.5f),
                                            false,
                                            null,
                                            "Friendly",
                                            true,
                                            null,
                                            null,
                                            "rankup:" + originatorId);
                                        RuntimeCardChangeApplier.ApplyCardChange(rankAssembly, rankUniqueId, rankRequest);
                                    }
                                }
                            }
                            catch
                            {
                                // Guarded: rank-up augmentation must never break promotion.
                            }
                        }
                    }

                    // 3. ExperienceReward on kill (when attacker has ExperienceReward)
                    if (targetKilled && profile.ExperienceReward != null && profile.ExperienceReward.experienceAmount > 0 &&
                        ReadBoolProperty(_isControlledByPlayerProperty, originatorObj) &&
                        IsEnemy(originatorObj, targetObj))
                    {
                        float mult = profile.ExperienceReward.bonusMultiplier > 0f ? profile.ExperienceReward.bonusMultiplier : 1f;
                        float reward = Mathf.Max(1f, (float)Math.Round(profile.ExperienceReward.experienceAmount * mult));
                        InvokeBankDeposit("Player", reward);

                        // Persistent card-change augmentation on bounty kill (native
                        // data-driven pipeline): the first bounty kill grants a
                        // permanent Damage1 bonus to the killer. Tracked per unit.
                        try
                        {
                            Assembly bountyAssembly = originatorObj.GetType().Assembly;
                            int bountyUniqueId = ReadIntProperty(_uniqueEntityIdProperty, originatorObj, -1);
                            if (bountyUniqueId > 0)
                            {
                                string bountyKey = bountyUniqueId.ToString() + "|bounty";
                                bool alreadyApplied;
                                lock (_appliedCardChangeBoostKeys)
                                {
                                    alreadyApplied = _appliedCardChangeBoostKeys.Contains(bountyKey);
                                    if (!alreadyApplied)
                                    {
                                        _appliedCardChangeBoostKeys.Add(bountyKey);
                                    }
                                }
                                if (!alreadyApplied)
                                {
                                    float bountyDamage = Mathf.Max(1f, reward * 0.5f);
                                    CardChangeRequest bountyRequest = CardChangeRequest.Create(
                                        "Damage1",
                                        "Add",
                                        bountyDamage,
                                        false,
                                        null,
                                        "Friendly",
                                        false,
                                        null,
                                        null,
                                        "bounty:" + originatorId);
                                    RuntimeCardChangeApplier.ApplyCardChange(bountyAssembly, bountyUniqueId, bountyRequest);
                                }
                            }
                        }
                        catch
                        {
                            // Guarded: bounty augmentation must never break reward.
                        }
                    }

                    // 4. Vampiric Leech on dealt damage
                    if (profile.VampiricLeech != null && profile.VampiricLeech.siphonRatio > 0f)
                    {
                        float siphon = amount * profile.VampiricLeech.siphonRatio;
                        if (profile.VampiricLeech.maxSiphonPerHit > 0f && siphon > profile.VampiricLeech.maxSiphonPerHit)
                        {
                            siphon = profile.VampiricLeech.maxSiphonPerHit;
                        }

                        if (siphon > 0.05f)
                        {
                            bool restoredShield = false;
                            if (profile.VampiricLeech.siphonToShield && ReadBoolProperty(_hasShieldProperty, originatorObj))
                            {
                                float curShield = ReadFloatProperty(_currentShieldProperty, originatorObj, 0f);
                                float maxShield = ReadFloatProperty(_maxShieldProperty, originatorObj, 0f);
                                if (curShield < maxShield - 0.1f)
                                {
                                    InvokeChargeShield(originatorObj, siphon);
                                    restoredShield = true;
                                }
                            }

                            if (!restoredShield)
                            {
                                InvokeHeal(originatorObj, siphon, originatorObj);
                            }

                            ApplyTimedStatusEffect(originatorObj, "Vampirism", 0.75f);

                            // Persistent card-change augmentation on siphon (native data-driven
                            // pipeline): the first time a unit siphons, grant a permanent
                            // Damage1 bonus so the leech edge survives. Tracked per unit.
                            try
                            {
                                Assembly leechAssembly = originatorObj.GetType().Assembly;
                                int leechUniqueId = ReadIntProperty(_uniqueEntityIdProperty, originatorObj, -1);
                                if (leechUniqueId > 0)
                                {
                                    string leechKey = leechUniqueId.ToString() + "|leech";
                                    bool alreadyApplied;
                                    lock (_appliedCardChangeBoostKeys)
                                    {
                                        alreadyApplied = _appliedCardChangeBoostKeys.Contains(leechKey);
                                        if (!alreadyApplied)
                                        {
                                            _appliedCardChangeBoostKeys.Add(leechKey);
                                        }
                                    }
                                    if (!alreadyApplied)
                                    {
                                        float leechDamage = Mathf.Max(0.5f, amount * profile.VampiricLeech.siphonRatio * 0.5f);
                                        CardChangeRequest leechRequest = CardChangeRequest.Create(
                                            "Damage1",
                                            "Add",
                                            leechDamage,
                                            false,
                                            null,
                                            "Friendly",
                                            false,
                                            null,
                                            null,
                                            "leech:" + originatorId);
                                        RuntimeCardChangeApplier.ApplyCardChange(leechAssembly, leechUniqueId, leechRequest);
                                    }
                                }
                            }
                            catch
                            {
                                // Guarded: leech augmentation must never break siphon.
                            }
                        }
                    }
                }

                // 5. Target bounty reward when killed by a player unit
                if (targetKilled && ReadBoolProperty(_isControlledByPlayerProperty, originatorObj) &&
                    IsEnemy(originatorObj, targetObj))
                {
                    string targetId = ReadEntityId(targetObj);
                    UnitRuntimeMechanicsProfile targetProfile;
                    if (!string.IsNullOrWhiteSpace(targetId) &&
                        TryGetProfile(targetId, out targetProfile) &&
                        targetProfile != null &&
                        targetProfile.ExperienceReward != null &&
                        targetProfile.ExperienceReward.experienceAmount > 0)
                    {
                        float mult = targetProfile.ExperienceReward.bonusMultiplier > 0f ? targetProfile.ExperienceReward.bonusMultiplier : 1f;
                        float bounty = Mathf.Max(1f, (float)Math.Round(targetProfile.ExperienceReward.experienceAmount * mult));
                        InvokeBankDeposit("Player", bounty);
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
            _nextSynergyCheckTime = Time.time + 0.5f;
            _nextStealthCheckTime = Time.time + 0.5f;
            _nextCommandCheckTime = Time.time + 0.5f;
            _nextShieldRegenCheckTime = Time.time + 0.5f;
            _lastShieldRegenTime = Time.time;
            _nextTauntCheckTime = Time.time + 1.0f;
            _nextNovaAvailableTime = Time.time;
            _previousShield = ReadFloatProperty(_currentShieldProperty, entityController, 0f);
            _hasInitializedShield = true;
            ApplyConfiguredAiBehavior(entityController, profile.AiBehavior);
            List<string> appliedTimedStatusEffects = ApplyConfiguredStatusEffects(entityController, profile.StatusEffects);
            int combatExperienceKey = GetCombatExperienceKey(entityController);
            lock (_appliedTimedStatusEffects)
            {
                if (appliedTimedStatusEffects.Count == 0)
                {
                    _appliedTimedStatusEffects.Remove(combatExperienceKey);
                }
                else
                {
                    _appliedTimedStatusEffects[combatExperienceKey] = appliedTimedStatusEffects;
                }
            }
            _baselineApplied = true;
            _defaultStanceApplied = false;
            _startingAffixesApplied = false;
            _defaultLoadoutApplied = false;
            _startingVeterancyApplied = false;
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

                if (!_defaultStanceApplied && !string.IsNullOrWhiteSpace(_profile.DefaultTacticalStance))
                {
                    int key = GetCombatExperienceKey(_entityController);
                    var stanceResult = RuntimeGameplayCommandDirector.TrySetUnitTacticalStance(key.ToString(), _profile.DefaultTacticalStance);
                    if (stanceResult.Success)
                    {
                        _defaultStanceApplied = true;
                    }
                }

                if (!_startingAffixesApplied && _profile.StartingAffixes != null && _profile.StartingAffixes.Length > 0)
                {
                    int key = GetCombatExperienceKey(_entityController);
                    bool allApplied = true;
                    for (int i = 0; i < _profile.StartingAffixes.Length; i++)
                    {
                        string affix = _profile.StartingAffixes[i];
                        if (string.IsNullOrWhiteSpace(affix)) continue;
                        var affixResult = RuntimeGameplayCommandDirector.TryApplyUnitAffix(key.ToString(), affix);
                        if (!affixResult.Success)
                        {
                            allApplied = false;
                        }
                    }
                    if (allApplied)
                    {
                        _startingAffixesApplied = true;
                    }
                }

                if (!_defaultLoadoutApplied && !string.IsNullOrWhiteSpace(_profile.DefaultLoadout))
                {
                    int key = GetCombatExperienceKey(_entityController);
                    var loadoutResult = RuntimeGameplayCommandDirector.TryApplyUnitLoadout(key.ToString(), _profile.DefaultLoadout.Trim());
                    if (loadoutResult.Success)
                    {
                        _defaultLoadoutApplied = true;
                    }
                }

                if (!_startingVeterancyApplied && _profile.StartingVeterancyRank.HasValue && _profile.StartingVeterancyRank.Value > 0)
                {
                    if (TrySetUnitVeterancyRank(_entityController, _profile.StartingVeterancyRank.Value, out _))
                    {
                        _startingVeterancyApplied = true;
                    }
                }

                if (!_baselineApplied && _profile.BaselineStatusEffects != null && _profile.BaselineStatusEffects.Length > 0)
                {
                    ApplyBaselineStatusEffects(_entityController, _profile.BaselineStatusEffects);
                    int key = GetCombatExperienceKey(_entityController);
                    lock (_appliedBaselineEffects)
                    {
                        _appliedBaselineEffects[key] = new List<string>(_profile.BaselineStatusEffects);
                    }
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

                if (_profile.Synergies != null &&
                    _profile.Synergies.Length > 0 &&
                    now >= _nextSynergyCheckTime)
                {
                    _nextSynergyCheckTime = now + 1.0f;
                    ExecuteSynergyPulse(_entityController, _profile.Synergies);
                }

                if (_profile.Stealth != null &&
                    _profile.Stealth.cloakDuration > 0f &&
                    _profile.Stealth.stealthLevel > 0f &&
                    now >= _nextStealthCheckTime)
                {
                    _nextStealthCheckTime = now + 1.5f;
                    ExecuteStealthPulse(_entityController, _profile.Stealth, now);
                }

                if (_profile.Command != null &&
                    _profile.Command.commandRange > 0f &&
                    now >= _nextCommandCheckTime)
                {
                    _nextCommandCheckTime = now + 1.0f;
                    ExecuteCommandPulse(_entityController, _profile.Command);
                }

                if (_profile.ShieldRegen != null &&
                    _profile.ShieldRegen.rechargeRate > 0f &&
                    now >= _nextShieldRegenCheckTime)
                {
                    _nextShieldRegenCheckTime = now + 0.25f;
                    ExecuteShieldRegeneration(_entityController, _profile.ShieldRegen, now);
                }

                if (_profile.Taunt != null &&
                    _profile.Taunt.tauntRadius > 0f &&
                    now >= _nextTauntCheckTime)
                {
                    _nextTauntCheckTime = now + Mathf.Max(0.5f, _profile.Taunt.pulseInterval);
                    ExecuteTauntPulse(_entityController, _profile.Taunt);
                }

                if (_profile.ShieldOverloadNova != null &&
                    _profile.ShieldOverloadNova.novaRadius > 0f)
                {
                    float currentShield = ReadFloatProperty(_currentShieldProperty, _entityController, 0f);
                    if (_hasInitializedShield && _previousShield > 0.1f && currentShield <= 0.05f && now >= _nextNovaAvailableTime)
                    {
                        _nextNovaAvailableTime = now + Mathf.Max(1.0f, _profile.ShieldOverloadNova.cooldown);
                        ExecuteShieldOverloadNova(_entityController, _profile.ShieldOverloadNova);
                    }
                    _previousShield = currentShield;
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

        private static void ExecuteSynergyPulse(object self, ContentUnitSynergyDefinition[] synergies)
        {
            if (synergies == null || synergies.Length == 0)
            {
                return;
            }

            IEnumerable entities = GetActiveEntities();
            if (entities == null)
            {
                return;
            }

            Vector3 selfPos = ReadVector3Property(_positionProperty, self);
            string selfEntityId = ReadEntityId(self) ?? "synergy-unit";

            for (int s = 0; s < synergies.Length; s++)
            {
                ContentUnitSynergyDefinition synergy = synergies[s];
                if (synergy == null || synergy.radius <= 0f || synergy.multiplier <= 0f ||
                    string.IsNullOrWhiteSpace(synergy.statName) ||
                    synergy.targetUnitIds == null || synergy.targetUnitIds.Length == 0)
                {
                    continue;
                }

                float worldRadius = synergy.radius <= 25f ? synergy.radius * 10f : synergy.radius;
                float radiusSq = worldRadius * worldRadius;
                string originatorTag = "synergy:" + (string.IsNullOrWhiteSpace(synergy.id) ? selfEntityId + ":" + s : synergy.id);

                int buffedCount = 0;
                foreach (object candidate in entities)
                {
                    if (candidate == null || ReferenceEquals(candidate, self) || !IsEntityAlive(candidate) || IsEnemy(self, candidate))
                    {
                        continue;
                    }

                    string candidateId = ReadEntityId(candidate);
                    if (string.IsNullOrWhiteSpace(candidateId) || !MatchesSynergyTarget(candidateId, synergy.targetUnitIds))
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

                    ApplyTimedStatChange(candidate, synergy.statName, synergy.multiplier - 1f, 2.0f, originatorTag);
                    ApplyTimedStatusEffect(candidate, "SquadEffect", 1.5f);

                    buffedCount++;
                    if (buffedCount >= 12)
                    {
                        break;
                    }
                }
            }
        }

        private static bool MatchesSynergyTarget(string candidateId, string[] targetUnitIds)
        {
            for (int i = 0; i < targetUnitIds.Length; i++)
            {
                string target = targetUnitIds[i];
                if (string.Equals(target, "*", StringComparison.Ordinal) ||
                    string.Equals(target, candidateId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static void ExecuteStealthPulse(object self, ContentUnitStealthDefinition stealth, float now)
        {
            if (stealth == null || stealth.cloakDuration <= 0f || stealth.stealthLevel <= 0f)
            {
                return;
            }

            float lastDamageTime = ReadFloatProperty(_timeOfLastDamageProperty, self, -999f);
            float combatGraceSeconds = Mathf.Clamp(4.0f - stealth.detectionReduction * 2.0f, 1.5f, 4.0f);
            if ((now - lastDamageTime) >= combatGraceSeconds)
            {
                float duration = Mathf.Clamp(stealth.cloakDuration, 1.5f, 15.0f);
                ApplyTimedStatusEffect(self, "Stealth", duration);
            }
        }

        private static void ExecuteCommandPulse(object self, ContentUnitCommandDefinition command)
        {
            if (command == null || command.commandRange <= 0f)
            {
                return;
            }

            IEnumerable entities = GetActiveEntities();
            if (entities == null)
            {
                return;
            }

            Vector3 selfPos = ReadVector3Property(_positionProperty, self);
            float worldRange = command.commandRange <= 25f ? command.commandRange * 10f : command.commandRange;
            float rangeSq = worldRange * worldRange;
            int maxFollowers = command.maxCommandChain > 0 ? Mathf.Min(command.maxCommandChain, 16) : 4;
            string selfId = ReadEntityId(self) ?? "commander";
            string originatorTag = "command:" + selfId;

            int linkedCount = 0;
            foreach (object candidate in entities)
            {
                if (candidate == null || ReferenceEquals(candidate, self) || !IsEntityAlive(candidate) || IsEnemy(self, candidate))
                {
                    continue;
                }

                if (_isUnitProperty != null && !ReadBoolProperty(_isUnitProperty, candidate))
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

                if (_setCaptainMethod != null)
                {
                    try
                    {
                        object currentCaptain = _captainOfThisEntityProperty != null
                            ? _captainOfThisEntityProperty.GetValue(candidate, null)
                            : null;
                        if (currentCaptain == null || ReferenceEquals(currentCaptain, self))
                        {
                            _setCaptainMethod.Invoke(candidate, new[] { self });
                        }
                    }
                    catch
                    {
                    }
                }

                string cmdType = command.commandType;
                if (string.Equals(cmdType, "Attack", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyTimedStatChange(candidate, "Damage", 0.12f, 2.0f, originatorTag + ":dmg");
                }
                else if (string.Equals(cmdType, "Defend", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyTimedStatChange(candidate, "ArmorProtection", 0.15f, 2.0f, originatorTag + ":arm");
                    ApplyTimedStatusEffect(candidate, "Defender", 1.5f);
                }
                else if (string.Equals(cmdType, "Move", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyTimedStatChange(candidate, "MoveSpeed", 0.15f, 2.0f, originatorTag + ":spd");
                }
                else
                {
                    ApplyTimedStatChange(candidate, "Damage", 0.10f, 2.0f, originatorTag + ":sup");
                }

                ApplyTimedStatusEffect(candidate, "SquadEffect", 1.5f);
                linkedCount++;
                if (linkedCount >= maxFollowers)
                {
                    break;
                }
            }
        }

        /// <summary>

        /// Executes a bounded tactical pulse using native status, healing, shield, and damage paths.

        /// </summary>

        public static bool TryExecuteTacticalPulse(

            object sourceObj,

            string pulseType,

            float radius,

            float magnitude,

            float durationSeconds,

            int maxTargets,

            out string normalizedPulseType,

            out int affectedEntityCount,

            out float damageApplied,

            out float healingApplied,

            out int statusEffectApplicationCount,

            out string message)

        {

            normalizedPulseType = string.IsNullOrWhiteSpace(pulseType) ? "Shockwave" : pulseType.Trim();

            affectedEntityCount = 0;

            damageApplied = 0f;

            healingApplied = 0f;

            statusEffectApplicationCount = 0;

            message = string.Empty;

            if (sourceObj == null)

            {

                message = "Pulse source is not available.";

                return false;

            }


            if (string.Equals(normalizedPulseType, "Shock", StringComparison.OrdinalIgnoreCase) ||

                string.Equals(normalizedPulseType, "Emp", StringComparison.OrdinalIgnoreCase))

            {

                normalizedPulseType = "Shockwave";

            }

            else if (string.Equals(normalizedPulseType, "Inspire", StringComparison.OrdinalIgnoreCase))

            {

                normalizedPulseType = "Rally";

            }


            if (!string.Equals(normalizedPulseType, "Shockwave", StringComparison.OrdinalIgnoreCase) &&

                !string.Equals(normalizedPulseType, "Rally", StringComparison.OrdinalIgnoreCase) &&

                !string.Equals(normalizedPulseType, "Repair", StringComparison.OrdinalIgnoreCase))

            {

                message = "Unknown tactical pulse type '" + normalizedPulseType + "'.";

                return false;

            }


            EnsureReflection(sourceObj.GetType());

            float clampedRadius = Mathf.Clamp(radius, 3f, 50f);

            float clampedMagnitude = Mathf.Clamp(magnitude, 0f, 1000f);

            float clampedDuration = Mathf.Clamp(durationSeconds, 0.5f, 30f);

            int clampedMaxTargets = Mathf.Clamp(maxTargets, 1, 16);

            IEnumerable entities = GetActiveEntities();

            if (entities == null)

            {

                message = "Active entity registry is not available.";

                return false;

            }


            TriggerVisualFeedback(sourceObj, 0.35f);

            Vector3 sourcePosition = ReadVector3Property(_positionProperty, sourceObj);

            float radiusSquared = clampedRadius * clampedRadius;

            foreach (object candidate in entities)

            {

                if (candidate == null || ReferenceEquals(candidate, sourceObj) || !IsEntityAlive(candidate))

                {

                    continue;

                }


                if (_isUnitProperty != null && !ReadBoolProperty(_isUnitProperty, candidate))

                {

                    continue;

                }


                bool enemy = IsEnemy(sourceObj, candidate);

                bool shockwave = string.Equals(normalizedPulseType, "Shockwave", StringComparison.OrdinalIgnoreCase);

                if (shockwave != enemy)

                {

                    continue;

                }


                Vector3 candidatePosition = ReadVector3Property(_positionProperty, candidate);

                float dx = candidatePosition.x - sourcePosition.x;

                float dz = candidatePosition.z - sourcePosition.z;

                if ((dx * dx + dz * dz) > radiusSquared)

                {

                    continue;

                }


                if (shockwave)

                {

                    ApplyTimedStatusEffect(candidate, "Stun", clampedDuration);

                    ApplyTimedStatusEffect(candidate, "SlowedDown", clampedDuration * 1.5f);

                    statusEffectApplicationCount += 2;

                    if (clampedMagnitude > 0f)

                    {

                        InvokeTakeDamage(candidate, clampedMagnitude, sourceObj);

                        damageApplied += clampedMagnitude;

                    }

                }

                else if (string.Equals(normalizedPulseType, "Rally", StringComparison.OrdinalIgnoreCase))

                {

                    ApplyTimedStatusEffect(candidate, "CombatBoost", clampedDuration);

                    ApplyTimedStatusEffect(candidate, "Turbo", clampedDuration);

                    ApplyTimedStatChange(candidate, "Damage", 0.10f, clampedDuration, "tactical-pulse:rally");

                    ApplyTimedStatChange(candidate, "MoveSpeed", 0.10f, clampedDuration, "tactical-pulse:rally");

                    statusEffectApplicationCount += 2;

                    if (clampedMagnitude > 0f)

                    {

                        InvokeHeal(candidate, clampedMagnitude, sourceObj);

                        healingApplied += clampedMagnitude;

                    }

                }

                else

                {

                    ApplyTimedStatusEffect(candidate, "Robust", clampedDuration);

                    statusEffectApplicationCount++;

                    float currentHealth = ReadFloatProperty(_currentHealthProperty, candidate, 0f);

                    float maxHealth = ReadFloatProperty(_maxHealthProperty, candidate, 0f);

                    if (clampedMagnitude > 0f && maxHealth > currentHealth)

                    {

                        float amount = Mathf.Min(clampedMagnitude, maxHealth - currentHealth);

                        InvokeHeal(candidate, amount, sourceObj);

                        healingApplied += amount;

                    }

                    if (ReadBoolProperty(_hasShieldProperty, candidate))

                    {

                        float currentShield = ReadFloatProperty(_currentShieldProperty, candidate, 0f);

                        float maxShield = ReadFloatProperty(_maxShieldProperty, candidate, 0f);

                        if (clampedMagnitude > 0f && maxShield > currentShield)

                        {

                            float amount = Mathf.Min(clampedMagnitude, maxShield - currentShield);

                            InvokeChargeShield(candidate, amount);

                            healingApplied += amount;

                        }

                    }

                }


                affectedEntityCount++;

                if (affectedEntityCount >= clampedMaxTargets)

                {

                    break;

                }

            }


            message = "Tactical pulse '" + normalizedPulseType + "' affected " + affectedEntityCount +

                " unit(s).";

            return true;

        }


        private void ExecuteShieldRegeneration(object self, ContentUnitShieldRegenDefinition regen, float now)
        {
            if (regen == null || regen.rechargeRate <= 0f)
            {
                return;
            }

            float dt = Mathf.Clamp(now - _lastShieldRegenTime, 0.05f, 1.0f);
            _lastShieldRegenTime = now;

            float lastDamageTime = ReadFloatProperty(_timeOfLastDamageProperty, self, -999f);
            if ((now - lastDamageTime) < regen.rechargeDelay)
            {
                return;
            }

            float curShield = ReadFloatProperty(_currentShieldProperty, self, 0f);
            float maxShield = ReadFloatProperty(_maxShieldProperty, self, 0f);
            if (maxShield > 0f && curShield < maxShield - 0.01f)
            {
                float needed = maxShield - curShield;
                float amount = Mathf.Min(regen.rechargeRate * dt, needed);
                if (curShield <= 0.01f && regen.burstAmount > 0f)
                {
                    amount += Mathf.Min(regen.burstAmount, needed - amount);
                }
                if (amount > 0f)
                {
                    InvokeChargeShield(self, amount);
                }
            }
        }

        private static void ExecuteTauntPulse(object self, ContentUnitTauntDefinition taunt)
        {
            if (taunt == null || taunt.tauntRadius <= 0f)
            {
                return;
            }

            IEnumerable entities = GetActiveEntities();
            if (entities == null)
            {
                return;
            }

            Vector3 selfPos = ReadVector3Property(_positionProperty, self);
            float worldRadius = taunt.tauntRadius <= 25f ? taunt.tauntRadius * 10f : taunt.tauntRadius;
            float radiusSq = worldRadius * worldRadius;
            float duration = taunt.duration > 0f ? taunt.duration : 2.5f;
            int maxTargets = taunt.maxTargets > 0 ? taunt.maxTargets : 6;

            ApplyTimedStatusEffect(self, "Defender", duration);

            int tauntedCount = 0;
            foreach (object candidate in entities)
            {
                if (candidate == null || !IsEntityAlive(candidate) || !IsEnemy(self, candidate))
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

                if (_attackMethod != null)
                {
                    try
                    {
                        _attackMethod.Invoke(candidate, new object[] { self });
                    }
                    catch
                    {
                    }
                }

                ApplyTimedStatusEffect(candidate, "Taunt", duration);
                tauntedCount++;
                if (tauntedCount >= maxTargets)
                {
                    break;
                }
            }
        }

        private static void ExecuteShieldOverloadNova(object self, ContentUnitShieldOverloadNovaDefinition nova)
        {
            if (nova == null || nova.novaRadius <= 0f)
            {
                return;
            }
            TriggerVisualFeedback(self, 0.35f);

            IEnumerable entities = GetActiveEntities();
            if (entities == null)
            {
                return;
            }

            Vector3 selfPos = ReadVector3Property(_positionProperty, self);
            float worldRadius = nova.novaRadius <= 25f ? nova.novaRadius * 10f : nova.novaRadius;
            float radiusSq = worldRadius * worldRadius;
            float stunDur = nova.stunDuration > 0f ? nova.stunDuration : 2.0f;

            foreach (object candidate in entities)
            {
                if (candidate == null || !IsEntityAlive(candidate) || !IsEnemy(self, candidate))
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

                ApplyTimedStatusEffect(candidate, "Stun", stunDur);
                ApplyTimedStatusEffect(candidate, "SlowedDown", stunDur * 1.5f);

                if (nova.damage > 0f)
                {
                    InvokeTakeDamage(candidate, nova.damage, self);
                }
            }
        }

        public static void ApplyTimedStatChange(
            object targetObj,
            string statName,
            float relativeDelta,
            float durationSeconds,
            string originatorId,
            bool replaceExisting = false)
        {
            if (targetObj == null || string.IsNullOrWhiteSpace(statName) || durationSeconds <= 0f ||
                (!replaceExisting && Math.Abs(relativeDelta) < 0.0001f))
            {
                return;
            }

            EnsureReflection(targetObj.GetType());
            if (_addSpecificValueChangeMethod == null || _changeableValueEnumType == null || _specificValueChangeType == null)
            {
                return;
            }

            string enumName = MapStatNameToChangeableValue(statName, ref relativeDelta);
            if (enumName == null)
            {
                return;
            }

            try
            {
                object changeableValue = Enum.Parse(_changeableValueEnumType, enumName, true);
                object change = Activator.CreateInstance(_specificValueChangeType);

                if (_svcValueToAddField != null)
                {
                    _svcValueToAddField.SetValue(change, relativeDelta);
                }
                if (_svcAddField != null && _specificAddTypeEnumType != null)
                {
                    // AddType.Relative == 1
                    _svcAddField.SetValue(change, Enum.ToObject(_specificAddTypeEnumType, 1));
                }
                if (_svcValidationField != null && _specificValidationTypeEnumType != null)
                {
                    // ValidationType.UntilTimestamp == 1
                    _svcValidationField.SetValue(change, Enum.ToObject(_specificValidationTypeEnumType, 1));
                }
                if (_svcValidUntilTimestampField != null)
                {
                    _svcValidUntilTimestampField.SetValue(change, Time.time + durationSeconds);
                }
                if (_svcOriginatorEntityIdField != null)
                {
                    _svcOriginatorEntityIdField.SetValue(change, originatorId ?? "rcl-stat-mod");
                }

                _addSpecificValueChangeMethod.Invoke(targetObj, new object[] { changeableValue, change, false });
            }
            catch
            {
            }
        }

        private static string MapStatNameToChangeableValue(string statName, ref float relativeDelta)
        {
            if (string.IsNullOrWhiteSpace(statName))
            {
                return null;
            }

            string trimmed = statName.Trim();
            if (string.Equals(trimmed, "Damage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Damage1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "AttackDamage", StringComparison.OrdinalIgnoreCase))
            {
                return "Damage";
            }
            if (string.Equals(trimmed, "AttackSpeed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "FireRate", StringComparison.OrdinalIgnoreCase))
            {
                // Higher AttackSpeed multiplier (e.g. +25% => +0.25) reduces AttackCooldown (-20%)
                float mult = 1f + relativeDelta;
                if (mult > 0.1f)
                {
                    relativeDelta = (1f / mult) - 1f;
                }
                return "AttackCooldown";
            }
            if (string.Equals(trimmed, "AttackCooldown", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Attack1Cooldown", StringComparison.OrdinalIgnoreCase))
            {
                return "AttackCooldown";
            }
            if (string.Equals(trimmed, "MoveSpeed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Speed", StringComparison.OrdinalIgnoreCase))
            {
                return "MoveSpeed";
            }
            if (string.Equals(trimmed, "WeaponRange", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Range", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "AttackRange", StringComparison.OrdinalIgnoreCase))
            {
                return "WeaponRange";
            }
            if (string.Equals(trimmed, "MaxHealth", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Health", StringComparison.OrdinalIgnoreCase))
            {
                return "MaxHealth";
            }
            if (string.Equals(trimmed, "MaxArmor", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Armor", StringComparison.OrdinalIgnoreCase))
            {
                return "MaxArmor";
            }
            if (string.Equals(trimmed, "ArmorProtection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Defense", StringComparison.OrdinalIgnoreCase))
            {
                return "ArmorProtection";
            }
            if (string.Equals(trimmed, "MaxShield", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Shield", StringComparison.OrdinalIgnoreCase))
            {
                return "MaxShield";
            }
            if (string.Equals(trimmed, "HealAmount", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "HealAmount1", StringComparison.OrdinalIgnoreCase))
            {
                return "HealAmount";
            }
            if (string.Equals(trimmed, "SightRadius", StringComparison.OrdinalIgnoreCase))
            {
                return "SightRadius";
            }
            if (string.Equals(trimmed, "EffectRadius", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "EffectRadius1", StringComparison.OrdinalIgnoreCase))
            {
                return "EffectRadius";
            }
            if (string.Equals(trimmed, "SkillRange", StringComparison.OrdinalIgnoreCase))
            {
                return "SkillRange";
            }

            return null;
        }

        private static void AttachVisualDirectorIfAvailable(object entityObj)
        {
            if (entityObj == null) return;
            try
            {
                Type vdType = Type.GetType("RogueCommandExpansionLoader.Runtime.Visual.RuntimeVisualDirector, RogueCommandExpansionLoader", false);
                if (vdType != null)
                {
                    MethodInfo attach = vdType.GetMethod("AttachIfApplicable", BindingFlags.Public | BindingFlags.Static);
                    if (attach != null)
                    {
                        attach.Invoke(null, new object[] { entityObj });
                    }
                }
            }
            catch { }
        }

        private static void TriggerVisualFeedback(object entityObj, float recoilDistance)
        {
            if (entityObj == null) return;
            try
            {
                Component comp = entityObj as Component;
                if (comp == null) return;
                Component vd = null;
                Component[] comps = comp.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] != null && comps[i].GetType().Name == "RuntimeVisualDirector")
                    {
                        vd = comps[i];
                        break;
                    }
                }
                if (vd == null && comp.gameObject != null)
                {
                    Component[] childComps = comp.gameObject.GetComponentsInChildren<Component>(true);
                    for (int i = 0; i < childComps.Length; i++)
                    {
                        if (childComps[i] != null && childComps[i].GetType().Name == "RuntimeVisualDirector")
                        {
                            vd = childComps[i];
                            break;
                        }
                    }
                }
                if (vd != null)
                {
                    MethodInfo m = vd.GetType().GetMethod("TriggerWeaponFeedback", BindingFlags.Public | BindingFlags.Instance);
                    if (m != null)
                    {
                        m.Invoke(vd, new object[] { recoilDistance });
                    }
                }
            }
            catch { }
        }

        private static void InvokeBankDeposit(string accountName, float amount)
        {
            if (_bankDepositMethod == null || string.IsNullOrWhiteSpace(accountName) || amount <= 0f)
            {
                return;
            }

            try
            {
                _bankDepositMethod.Invoke(null, new object[] { accountName, amount });
            }
            catch
            {
            }
        }

        private static void ApplyConfiguredAiBehavior(
            object entityObj,
            ContentUnitAiBehaviorDefinition aiBehavior)
        {
            if (entityObj == null || aiBehavior == null || string.IsNullOrWhiteSpace(aiBehavior.aiTag) ||
                _addAiTagMethod == null)
            {
                return;
            }

            try
            {
                _addAiTagMethod.Invoke(entityObj, new object[] { aiBehavior.aiTag });
            }
            catch
            {
                // Best-effort: an unsupported optional AI tag must not block instantiation.
            }
        }

        private static List<string> ApplyConfiguredStatusEffects(
            object entityObj,
            ContentUnitStatusEffectDefinition[] statusEffects)
        {
            var applied = new List<string>();
            if (entityObj == null || statusEffects == null || statusEffects.Length == 0)
            {
                return applied;
            }

            EnsureReflection(entityObj.GetType());
            int limit = Math.Min(statusEffects.Length, 16);
            for (int i = 0; i < limit; i++)
            {
                ContentUnitStatusEffectDefinition effect = statusEffects[i];
                if (effect == null || string.IsNullOrWhiteSpace(effect.effectType) ||
                    effect.duration <= 0f || float.IsNaN(effect.duration) || float.IsInfinity(effect.duration))
                {
                    continue;
                }

                float duration = Mathf.Clamp(effect.duration, 0.05f, 300f);
                if (ApplyTimedStatusEffect(entityObj, effect.effectType, duration))
                {
                    applied.Add(effect.effectType);
                }
            }

            return applied;
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

        private static bool ApplyTimedStatusEffect(object entityObj, string effectName, float durationSeconds)
        {
            if (_setActiveStatusEffectMethod == null || _statusEffectEnumType == null || _durationTypeEnumType == null ||
                entityObj == null || string.IsNullOrWhiteSpace(effectName) || durationSeconds <= 0f)
            {
                return false;
            }

            try
            {
                object parsedEffect = Enum.Parse(_statusEffectEnumType, effectName, true);
                object durationSecondsEnum = Enum.ToObject(_durationTypeEnumType, 1);
                _setActiveStatusEffectMethod.Invoke(entityObj, new object[] { parsedEffect, durationSecondsEnum, durationSeconds });
                return true;
            }
            catch
            {
                return false;
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

        private static void InvokeTakeDamage(object target, float amount, object originator)
        {
            if (_takeDamageMethod == null || target == null || amount <= 0f)
            {
                return;
            }

            try
            {
                ParameterInfo[] parameters = _takeDamageMethod.GetParameters();
                if (parameters.Length == 4)
                {
                    _takeDamageMethod.Invoke(target, new object[] { amount, originator, true, false });
                }
                else if (parameters.Length == 2)
                {
                    _takeDamageMethod.Invoke(target, new object[] { amount, originator });
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
            _isControlledByPlayerProperty = entityType.GetProperty("IsControlledByPlayer", flags);
            _isControlledByAiProperty = entityType.GetProperty("IsControlledByAi", flags);
            _isUnitProperty = entityType.GetProperty("IsUnit", flags);
            _isCombatUnitProperty = entityType.GetProperty("IsCombatUnit", flags);
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
            _timeOfLastDamageProperty = entityType.GetProperty("TimeOfLastDamage", flags);
            _captainOfThisEntityProperty = entityType.GetProperty("CaptainOfThisEntity", flags);

            _isEnemyMethod = entityType.GetMethod("IsEnemy", flags, null, new[] { entityType }, null);
            _healMethod = entityType.GetMethod("Heal", flags);
            _repairArmorMethod = entityType.GetMethod("RepairArmor", flags);
            _chargeShieldMethod = entityType.GetMethod("ChargeShield", flags);
            _rankUpMethod = entityType.GetMethod("RankUp", flags, null, new[] { typeof(int) }, null);
            _setCaptainMethod = entityType.GetMethod("SetCaptain", flags, null, new[] { entityType }, null);
            _removeCaptainMethod = entityType.GetMethod("RemoveCaptain", flags, null, new[] { entityType }, null);
            _addAiTagMethod = entityType.GetMethod("AddAiTag", flags, null, new[] { typeof(string) }, null);
            _lastAiSkillActivationCheckTimeField = entityType.GetField(
                "_lastAiSkillActivationCheckTime", flags);
            _productionInfoProperty = entityType.GetProperty("ProductionInfo", flags);
            _productionInfoField = entityType.GetField("_productionInfo", flags);
            Type productionInfoType = _productionInfoProperty == null
                ? (_productionInfoField == null ? null : _productionInfoField.FieldType)
                : _productionInfoProperty.PropertyType;
            if (productionInfoType != null)
            {
                _productionEntityIdField = productionInfoType.GetField("entityId", flags);
                _productionQueuedCountField = productionInfoType.GetField("queuedCount", flags);
            }
            _attackMethod = entityType.GetMethod("Attack", flags, null, new[] { entityType }, null);
            _takeDamageMethod = entityType.GetMethod("TakeDamage", flags);

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

            _changeableValueEnumType = entityType.GetNestedType("ChangeableValue", BindingFlags.Public | BindingFlags.NonPublic)
                ?? asm.GetType("EntityController+ChangeableValue")
                ?? asm.GetType("EntityController/ChangeableValue");
            _specificValueChangeType = asm.GetType("SpecificValueChange");
            if (_specificValueChangeType != null)
            {
                _specificAddTypeEnumType = _specificValueChangeType.GetNestedType("AddType", BindingFlags.Public | BindingFlags.NonPublic)
                    ?? asm.GetType("SpecificValueChange+AddType");
                _specificValidationTypeEnumType = _specificValueChangeType.GetNestedType("ValidationType", BindingFlags.Public | BindingFlags.NonPublic)
                    ?? asm.GetType("SpecificValueChange+ValidationType");
                _svcValueToAddField = _specificValueChangeType.GetField("ValueToAdd", flags);
                _svcAddField = _specificValueChangeType.GetField("Add", flags);
                _svcValidationField = _specificValueChangeType.GetField("Validation", flags);
                _svcValidUntilTimestampField = _specificValueChangeType.GetField("ValidUntilTimestamp", flags);
                _svcOriginatorEntityIdField = _specificValueChangeType.GetField("OriginatorEntityId", flags);

                if (_changeableValueEnumType != null)
                {
                    _addSpecificValueChangeMethod = entityType.GetMethod("AddSpecificValueChange",
                        flags,
                        null,
                        new[] { _changeableValueEnumType, _specificValueChangeType, typeof(bool) },
                        null);
                }
            }

            _bankType = asm.GetType("Bank");
            if (_bankType != null)
            {
                _bankDepositMethod = _bankType.GetMethod("Deposit",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string), typeof(float) },
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

        internal static bool IsHostileTarget(object attacker, object target)
        {
            if (attacker == null || target == null)
            {
                return false;
            }

            try
            {
                EnsureReflection(attacker.GetType());
                return IsEnemy(attacker, target);
            }
            catch
            {
                return false;
            }
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
