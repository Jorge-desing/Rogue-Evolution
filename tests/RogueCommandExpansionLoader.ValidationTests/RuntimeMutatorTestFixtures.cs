using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Modules;
using RogueCommandExpansionLoader.Runtime;

public struct NullableString
{
    public string value;
    public bool hasValue;
}

public struct EntityBalancingParameters
{
    public string entityId;
    public NullableString factoryForEntityId;
    // The 25 per-unit balance row fields, mirroring the pinned
    // EntityBalancingParameters shape read by EntityBalancingStore.GetOriginal*.
    public int cost;
    public float productionDuration;
    public int maxCapacity;
    public int maxHealth;
    public int maxShield;
    public int maxMana;
    public int maxRank;
    public float moveSpeed;
    public int sightRadius;
    public float damage1;
    public float damage2;
    public float weaponRange;
    public float attackCooldown;
    public float attack2Cooldown;
    public float effectRadius1;
    public float effectRadius2;
    public int skillRange;
    public float healAmount1;
    public float healAmount2;
    public float gainCreditsAmount;
    public float skillManaCost;
    public float duration1;
    public float duration2;
    public float maxArmor;
    public float armorProtection;
    public UnitRole roles;
    public SystemTags offeredSystemTags;
}

[Flags]
public enum SystemTags
{
    None = 0,
    Melee = 1,
    Shield = 2,
    Spawn = 4,
    Armor = 8,
    Mech = 16,
    Robo = 32,
    Heal = 64,
    Turret = 128,
    Fire = 256,
    Bounty = 512,
    Panic = 1024,
    Slow = 2048,
    Stun = 4096,
    Teleport = 8192,
    AoE = 16384,
    ManaUse = 32768,
    Rush = 65536,
    Turbo = 131072,
    Strength = 262144,
    Robust = 524288,
    SkillRangeUse = 1048576,
    Bomb = 2097152,
    Tank = 4194304,
    Vehicle = 8388608,
    EffectDuration = 16777216,
    SpawnDuration = 33554432,
    CriticalHits = 67108864,
    Stealth = 134217728,
    Zombo = 268435456,
    Sniper = 536870912,
    Regen = 1073741824,
    EconomySupport = int.MinValue,
    All = -1
}

public enum Rarity
{
    Common,
    Rare,
    UltraRare
}

public sealed class EntityControllerIdentityTestController
{
    public string entityId;
}

public static class EntityBalancingStore
{
    public enum ChangeableValue
    {
        MaxHealth,
        MoveSpeed
    }

    public static List<EntityBalancingParameters> EntityBalancingParametersList = new();
    public static Dictionary<string, int> ParameterListIndexOf = new(StringComparer.Ordinal);
    public static Dictionary<string, string> FactoryEntityIdOf = new(StringComparer.Ordinal);
    public static Dictionary<string, UnityEngine.Sprite> ImageOf = new(StringComparer.Ordinal);
    public static Dictionary<string, Dictionary<ChangeableValue, int>> ChangeableIntValueCache = new(StringComparer.Ordinal);
    public static Dictionary<string, Dictionary<ChangeableValue, float>> ChangeableFloatValueCache = new(StringComparer.Ordinal);
    public static HashSet<string> BuildingEntityIds = new(StringComparer.Ordinal);
    public static Dictionary<string, UnitRole> RolesOf = new(StringComparer.Ordinal);
    public static Dictionary<string, float> MaxManaOf = new(StringComparer.Ordinal);
    public static Dictionary<string, int> CostOf = new(StringComparer.Ordinal);
    public static Dictionary<string, int> MaxHealthOf = new(StringComparer.Ordinal);
    public static Dictionary<string, int> MaxShieldOf = new(StringComparer.Ordinal);
    public static Dictionary<string, int> MaxRankOf = new(StringComparer.Ordinal);
    public static Dictionary<string, float> MaxArmorOf = new(StringComparer.Ordinal);
    public static Dictionary<string, float> MoveSpeedOf = new(StringComparer.Ordinal);
    public static Dictionary<string, float> WeaponRangeOf = new(StringComparer.Ordinal);
    public static Dictionary<string, float> DpsOf = new(StringComparer.Ordinal);
    public static Dictionary<string, float> Damage1Of = new(StringComparer.Ordinal);
    public static Dictionary<string, float> Damage2Of = new(StringComparer.Ordinal);

    public static List<string> AllEntityIdsOfUnits() => new(RolesOf.Keys);
    public static List<string> AllEntityIdsOfBuildings() => new(BuildingEntityIds);
    public static List<string> AllEntityIdsOfFactories() => new(FactoryEntityIdOf.Values);
    public static List<string> AllEntityIds()
    {
        var set = new HashSet<string>(RolesOf.Keys, StringComparer.Ordinal);
        set.UnionWith(BuildingEntityIds);
        return new List<string>(set);
    }
    public static bool IsBuilding(string entityId) => BuildingEntityIds.Contains(entityId);
    public static int Cost(string entityId, bool includeModified = false) =>
        CostOf.TryGetValue(entityId, out var c) ? c : 100;
    public static bool HasRole(string entityId, UnitRole role) =>
        RolesOf.TryGetValue(entityId, out var r) ? (r & role) == role : false;
    public static float MaxMana(string entityId, bool includeModified = false) =>
        MaxManaOf.TryGetValue(entityId, out var m) ? m : 100f;
    public static int MaxHealth(string entityId, bool includeModified = false) =>
        MaxHealthOf.TryGetValue(entityId, out var v) ? v : 100;
    public static int MaxShield(string entityId, bool includeModified = false) =>
        MaxShieldOf.TryGetValue(entityId, out var v) ? v : 0;
    public static int MaxRank(string entityId, bool includeModified = false) =>
        MaxRankOf.TryGetValue(entityId, out var v) ? v : 1;
    public static float MaxArmor(string entityId, bool includeModified = false) =>
        MaxArmorOf.TryGetValue(entityId, out var v) ? v : 0f;
    public static float MoveSpeed(string entityId, bool includeModified = false) =>
        MoveSpeedOf.TryGetValue(entityId, out var v) ? v : 1f;
    public static float WeaponRange(string entityId, bool includeModified = false) =>
        WeaponRangeOf.TryGetValue(entityId, out var v) ? v : 0f;
    public static float Dps(string entityId) =>
        DpsOf.TryGetValue(entityId, out var v) ? v : 0f;
    public static float Damage1(string entityId, bool includeModified = false) =>
        Damage1Of.TryGetValue(entityId, out var v) ? v : 0f;
    public static float Damage2(string entityId, bool includeModified = false) =>
        Damage2Of.TryGetValue(entityId, out var v) ? v : 0f;

    public static void InvalidateCache(string entityId)
    {
        ChangeableIntValueCache[entityId].Clear();
        ChangeableFloatValueCache[entityId].Clear();
    }

    public static void Reset(IEqualityComparer<string> indexComparer = null)
    {
        EntityBalancingParametersList.Clear();
        ParameterListIndexOf = new Dictionary<string, int>(indexComparer ?? StringComparer.Ordinal);
        FactoryEntityIdOf.Clear();
        ImageOf.Clear();
        ChangeableIntValueCache = new Dictionary<string, Dictionary<ChangeableValue, int>>(StringComparer.Ordinal);
        ChangeableFloatValueCache = new Dictionary<string, Dictionary<ChangeableValue, float>>(StringComparer.Ordinal);
        BuildingEntityIds.Clear();
        RolesOf.Clear();
        MaxManaOf.Clear();
        CostOf = new Dictionary<string, int>(StringComparer.Ordinal);
        MaxHealthOf = new Dictionary<string, int>(StringComparer.Ordinal);
        MaxShieldOf = new Dictionary<string, int>(StringComparer.Ordinal);
        MaxRankOf = new Dictionary<string, int>(StringComparer.Ordinal);
        MaxArmorOf = new Dictionary<string, float>(StringComparer.Ordinal);
        MoveSpeedOf = new Dictionary<string, float>(StringComparer.Ordinal);
        WeaponRangeOf = new Dictionary<string, float>(StringComparer.Ordinal);
        DpsOf = new Dictionary<string, float>(StringComparer.Ordinal);
        Damage1Of = new Dictionary<string, float>(StringComparer.Ordinal);
        Damage2Of = new Dictionary<string, float>(StringComparer.Ordinal);
    }
}

public sealed class GameBalancingScriptableObject
{
    public LevelProgressionParametersScriptableObject levelProgressionParameters = new();
    public LevelProgressionParametersScriptableObject levelProgressionParametersHeat = new();
    public LevelProgressionParametersScriptableObject levelProgressionParametersFirstRun = new();
}

public sealed class LevelProgressionParametersScriptableObject
{
    public List<Stage> stages = new();
    public List<DefaultAiBehaviour> defaultAis = new();
}

public sealed class Stage
{
    public StageParametersScriptableObject stageParameters = new();
}

public struct StageCardReward
{
}

public sealed class StageParametersScriptableObject
{
    public bool fillFirstLevelWithCardRewards;
    public bool putDefaultCardRewardInCenter;
    public StageCardReward cardReward;
    public BlueprintPoolList blueprintPoolList = new();
}

public sealed class BlueprintPoolList : UnityEngine.ScriptableObject
{
    public List<BlueprintPool> blueprintPools = new();
}

// RuntimeHarmonyPatches binds this typed return to Rogue Command's global BlueprintPool type.
public sealed class BlueprintPool : UnityEngine.ScriptableObject
{
    public string poolId;
}

public sealed class LandscapeGenerator
{
}

public sealed class MutableBalanceRow
{
    public float moveSpeed;
    public float armorValue;
    public float damage;
    public float weaponRange;
    public float detectionRange;
    public float healAmount;
}

public sealed class EntityValueModifier
{
    public float value;
}

public sealed class AiBehaviour
{
    public float tickRate = 1.0f;
    public float detectionRange = 10.0f;
}

public class AiBehaviourManager : UnityEngine.MonoBehaviour
{
    private static AiBehaviourManager _instance;
    public static AiBehaviourManager Instance => _instance ??= new AiBehaviourManager();

    public AiBehaviour activeBehaviour;
    public static readonly Priority_Queue.SimplePriorityQueue<string> ConstructionQueue = new();
    public static int CurrentAttackWaveNumber { get; set; } = 3;
    public float tickDuration = 0.5f;
    public bool showAiTags = false;
    public bool showCurrentCommands = false;

    public System.Collections.Generic.List<object> attackCommands = new System.Collections.Generic.List<object> { new object() };
    public System.Collections.Generic.List<object> defendCommands = new System.Collections.Generic.List<object>();
    public System.Collections.Generic.List<object> scoutCommands = new System.Collections.Generic.List<object>();
    public System.Collections.Generic.List<object> buildCommands = new System.Collections.Generic.List<object>();
    public System.Collections.Generic.List<object> produceCommands = new System.Collections.Generic.List<object>();
    public System.Collections.Generic.List<object> otherCommands = new System.Collections.Generic.List<object>();

    public AiBehaviourManager()
    {
        _instance = this;
    }

    public static void IncrementAttackWaveNumber_Static()
    {
        CurrentAttackWaveNumber++;
    }

    public static void ToggleShowAiTags_Static()
    {
        Instance.showAiTags = !Instance.showAiTags;
    }

    public static void ToggleCurrentCommands_Static()
    {
        Instance.showCurrentCommands = !Instance.showCurrentCommands;
    }

    public static string ConstructionQueueToString_Static()
    {
        return "Factory_Assault x1, Turret_Heavy x2";
    }
}

namespace Priority_Queue
{
    public sealed class SimplePriorityQueue<TItem> : IEnumerable<TItem>
    {
        private readonly List<TItem> _items = new();
        public int Count => _items.Count;
        public void Enqueue(TItem item, float priority) { if (!_items.Contains(item)) _items.Add(item); }
        public bool Contains(TItem item) => _items.Contains(item);
        public TItem Dequeue() { var item = _items[0]; _items.RemoveAt(0); return item; }
        public void Clear() => _items.Clear();
        public IEnumerator<TItem> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}

public sealed class DefaultAiBehaviour

{
    public static List<EntityInAiDeck> ActiveConstructableEntities { get; set; } = new();
    public static Func<string, int, EntityController> CreateBuildingHandler { get; set; }
    public static bool TickHasStarted { get; set; }
    public static int ActualAiStartBaseCount { get; set; } = 1;

    public UnityEngine.GameObject gameObject { get; set; } = new("test-ai");
    public UnityEngine.Transform transform { get; set; } = new();
    public int instanceId { get; set; } = 1;

    public int GetInstanceID() => instanceId;

    public static List<EntityInAiDeck> ConstructableEntities_Static() => ActiveConstructableEntities;
    public static bool HasTickStarted_Static() => TickHasStarted;
    public static int ActualAiStartBaseCount_Static() => ActualAiStartBaseCount;
    public static EntityController CreateBuilding_Static(string entityId, int baseIndex) =>
        CreateBuildingHandler?.Invoke(entityId, baseIndex);

    public static void ResetRuntimeState()
    {
        ActiveConstructableEntities = new List<EntityInAiDeck>();
        CreateBuildingHandler = null;
        TickHasStarted = false;
        ActualAiStartBaseCount = 1;
    }
}

public struct EntityInAiDeck
{
    public string entityId;
    public int MaxLiving { get; set; }
}

public sealed class OpenReward
{
    public string blueprintPoolId;
}

public static class Game
{
    public static bool AtLeastOneBlueprintHasBeenAddedByPlayer = true;
    public static List<string> CardsInDeck { get; set; } = new();
    public static List<OpenReward> OpenCardRewards = new();
    public static List<string> Relics { get; set; } = new();
    public static int CurrentLevel { get; set; } = 1;
    public static bool IsLastLevel { get; set; } = false;
    public static bool IsFirstLevelOrGameHasBeenResetted { get; set; } = false;
    public static object StageMap { get; set; }
    public static string CurrentLevelProgressionMode { get; set; } = "Default";
}

public sealed class MetaGame
{
    public static MetaGame Instance { get; set; } = new();
    public int CurrentExperienceLevel { get; set; }
    public int CurrentAscensionLevel { get; set; }
    public int CurrentHeat { get; set; }
    public string CurrentAscensionAndHeatLevelString { get; set; } = "Ascension 0";
}

namespace Shapes2D
{
    public static class Game { }
}

internal sealed class RuntimeMutatorTestProvider : IRuntimeContentProvider, IBlueprintPoolRuntimeProvider,
    IBlueprintRewardFactoryScopeProvider, IGuaranteedModCardRewardProvider
{
    public HashSet<string> ModProducts { get; } = new(StringComparer.Ordinal);
    public string GuaranteedModProduct { get; set; }
    public bool RequireRegisteredModProduct { get; set; }
    public string LastPoolId { get; private set; }
    public bool LastStartingSelection { get; private set; }
    public int LastCurrentXpLevel { get; private set; }
    public HashSet<string> LastExcludedProductIds { get; private set; } = new(StringComparer.Ordinal);
    public bool IsModUnitProduct(string productId) => !string.IsNullOrEmpty(productId) && ModProducts.Contains(productId);
    public bool TryGetGuaranteedModRewardProduct(
        string poolId,
        bool isStartingSelection,
        int currentXpLevel,
        ICollection<string> excludedProductIds,
        out string modProductId)
    {
        LastPoolId = poolId;
        LastStartingSelection = isStartingSelection;
        LastCurrentXpLevel = currentXpLevel;
        LastExcludedProductIds = excludedProductIds == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(excludedProductIds, StringComparer.Ordinal);
        modProductId = GuaranteedModProduct;
        return !string.IsNullOrEmpty(modProductId) &&
            (!RequireRegisteredModProduct || EntityBalancingStore.ParameterListIndexOf.ContainsKey(modProductId));
    }
    public Dictionary<string, string> PoolLocalizationAliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<object, string> PoolIds { get; } = new();
    public BlueprintRewardFactoryScope RewardFactoryScope { get; set; }
    public bool ThrowOnRewardScope { get; set; }
    public bool TryGetBlueprintPoolLocalizationId(string poolId, out string sourcePoolId)
    {
        sourcePoolId = null;
        return poolId != null && PoolLocalizationAliases.TryGetValue(poolId, out sourcePoolId);
    }
    public bool TryFindBlueprintPool(string poolId, out object pool) { pool = null; return false; }
    public bool TryGetBlueprintPoolId(object pool, out string poolId)
    {
        poolId = null;
        return pool != null && PoolIds.TryGetValue(pool, out poolId);
    }
    public bool TryGetBlueprintPoolDecision(object pool, string unitId, out bool runOriginal) { runOriginal = true; return false; }
    public void AddExclusiveBlueprints(string otherThanPoolId, HashSet<string> result) { }
    public bool TryGetBlueprintRewardFactoryScope(out BlueprintRewardFactoryScope scope)
    {
        if (ThrowOnRewardScope) throw new InvalidOperationException("simulated reward scope resolution failure");
        scope = RewardFactoryScope;
        return scope != null;
    }
    public BlueprintRewardFactoryScope CreateFailClosedBlueprintRewardFactoryScope()
    {
        return RewardFactoryScope == null ? null : RewardFactoryScope.CreateFailClosedScope();
    }
    public void OnStageMapCreated(object stageMap) { }
    public void OnStageMapDeserialized(object stageMap, string json) { }
    public void OnProgressionContentApplied() { }
    public string AppendStageMapOrder(object stageMap, string json) { return json; }

    public List<object> EntityRows { get; } = new();
    public List<ProgressionContentContribution> Contributions { get; } = new();
    public List<BalanceOverride> BalanceOverrides { get; } = new();
    public int EntityRowsRegisteredCount { get; private set; }

    public string TargetGameVersion => "1.0.9";

    public void OnEntityRegistryInitialized()
    {
    }

    public bool PrepareContentForRuntime() => true;

    public void OnEntityRowsRegistered() => EntityRowsRegisteredCount++;

    public IEnumerable<BalanceOverride> GetBalanceOverrides() => BalanceOverrides;

    public IEnumerable<object> GetEntityParameterRows() => EntityRows;

    public bool TryGetUnitLocalization(string unitId, bool description, out string value)
    {
        value = null;
        return false;
    }

    public IEnumerable<ProgressionContentContribution> GetProgressionContributions(object gameBalancingScriptableObject) => Contributions;

    public void OnAiBehaviourInitialized(object gameBehaviour)
    {
    }

    public void OnAiBehaviourUpdated(object gameBehaviour)
    {
    }

    public void OnAiBehaviourDestroyed(object gameBehaviour)
    {
    }

    public bool TryLoadResource(string resourcePath, out UnityEngine.Object asset)
    {
        asset = null;
        return false;
    }

    public LoaderDiagnosticsSnapshot GetDiagnosticsSnapshot() => null;
}

internal static class RuntimeMutatorTestData
{
    public static EntityBalancingParameters Row(string entityId, string productId = null)
    {
        return new EntityBalancingParameters
        {
            entityId = entityId,
            factoryForEntityId = productId == null
                ? default(NullableString)
                : new NullableString { value = productId, hasValue = true }
        };
    }
}

internal sealed class ThrowOnFirstTargetHashComparer : IEqualityComparer<string>
{
    private readonly string _targetId;

    public ThrowOnFirstTargetHashComparer(string targetId)
    {
        _targetId = targetId;
    }

    public bool FailureTriggered { get; private set; }
    public int TargetHashCalls { get; private set; }

    public bool Equals(string left, string right) => StringComparer.Ordinal.Equals(left, right);

    public int GetHashCode(string value)
    {
        if (String.Equals(value, _targetId, StringComparison.Ordinal))
        {
            TargetHashCalls++;
            if (TargetHashCalls == 1)
            {
                FailureTriggered = true;
                throw new InvalidOperationException("Injected index insertion failure.");
            }
        }

        return StringComparer.Ordinal.GetHashCode(value);
    }
}

internal sealed class ThrowOnTargetHashCallComparer : IEqualityComparer<string>
{
    private readonly string _targetId;
    private readonly int _failureCall;

    public ThrowOnTargetHashCallComparer(string targetId, int failureCall)
    {
        _targetId = targetId;
        _failureCall = failureCall;
    }

    public bool FailureTriggered { get; private set; }
    public int TargetHashCalls { get; private set; }

    public bool Equals(string left, string right) => StringComparer.Ordinal.Equals(left, right);

    public int GetHashCode(string value)
    {
        if (String.Equals(value, _targetId, StringComparison.Ordinal))
        {
            TargetHashCalls++;
            if (TargetHashCalls == _failureCall)
            {
                FailureTriggered = true;
                throw new InvalidOperationException("Injected value-cache rollback failure.");
            }
        }

        return StringComparer.Ordinal.GetHashCode(value);
    }
}

internal sealed class TestAiModule : IRogueCommandAiModule
{
    public string Id { get; set; } = "test:module";
    public int ApiVersion { get; set; } = AiModuleRegistry.CurrentApiVersion;
    public bool AcceptParameters { get; set; } = true;
    public bool ThrowOnValidation { get; set; }
    public IRogueCommandAiRuntimeBehaviour Behaviour { get; set; }
    public DefaultAiBehaviour LastGameBehaviour { get; private set; }
    public ContentModuleParameter[] LastParameters { get; private set; }

    public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
    {
        if (ThrowOnValidation)
        {
            throw new InvalidOperationException("Injected AI parameter validation failure.");
        }

        diagnostic = AcceptParameters ? null : "Test module rejected its parameters.";
        return AcceptParameters;
    }

    public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
        DefaultAiBehaviour gameBehaviour,
        ContentModuleParameter[] parameters)
    {
        LastGameBehaviour = gameBehaviour;
        LastParameters = parameters;
        return Behaviour;
    }
}

internal sealed class RecordingAiRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
{
    private readonly IList<string> _events;
    private readonly string _id;

    public RecordingAiRuntimeBehaviour(IList<string> events, string id)
    {
        _events = events;
        _id = id;
    }

    public bool ThrowOnInitialize { get; set; }
    public bool ThrowOnTick { get; set; }
    public bool ThrowOnDestroy { get; set; }

    public void OnInitialized()
    {
        _events.Add(_id + ":init");
        if (ThrowOnInitialize)
        {
            throw new InvalidOperationException("Injected initialization failure.");
        }
    }

    public void Tick()
    {
        _events.Add(_id + ":tick");
        if (ThrowOnTick)
        {
            throw new InvalidOperationException("Injected tick failure.");
        }
    }

    public void OnDestroyed()
    {
        _events.Add(_id + ":destroy");
        if (ThrowOnDestroy)
        {
            throw new InvalidOperationException("Injected cleanup failure.");
        }
    }
}
