using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum StatusEffect
{
    SlowedDown,
    OnFire,
    Electrified,
    Regen,
    Panic,
    Marked,
    Turbo,
    Overclocked,
    Rush,
    Stealth,
    Robust,
    Armored,
    CombatBoost,
    Hack,
    Smoke,
    Stun,
    SquadEffect,
    Defender
}

public struct SpecificValueChange
{
    public enum AddType
    {
        Absolute = 0,
        Relative = 1
    }

    public enum ValidationType
    {
        UntilFrame = 0,
        UntilTimestamp = 1
    }

    public float ValueToAdd;
    public AddType Add;
    public ValidationType Validation;
    public int ValidUntilFrame;
    public float ValidUntilTimestamp;
    public string OriginatorEntityId;
}

public static class SetStatusEffect
{
    public enum DurationType
    {
        Unlimited = 0,
        Seconds = 1
    }
}

public sealed class EntityController : MonoBehaviour
{
    public enum ChangeableValue
    {
        CostOfProduct,
        ProductionDurationOfProduct,
        MaxHealth,
        MoveSpeed,
        SightRadius,
        Damage,
        WeaponRange,
        AttackCooldown,
        EffectRadius,
        SkillRange,
        HealAmount,
        GainCreditsAmount,
        MaxShield,
        MaxMana,
        SkillManaCost,
        Duration,
        MaxRank,
        MaxArmor,
        ArmorProtection
    }

    private readonly HashSet<UnitRole> _roles = new();
    public HashSet<string> AiTags { get; } = new(StringComparer.Ordinal);
    private readonly HashSet<StatusEffect> _activeStatusEffects = new();
    private readonly LinkedList<EntityCommand> _commandChain = new();
    public List<(StatusEffect Effect, SetStatusEffect.DurationType DurationType, float Duration)> AppliedStatusEffects { get; } = new();
    public List<(ChangeableValue Value, SpecificValueChange Change, bool IsStackable)> AppliedSpecificValueChanges { get; } = new();

    public float TimeOfLastDamage { get; set; } = -999f;

    public bool IsDestroyed { get; set; }
    public bool DestroyHasBeenStarted { get; set; }
    public bool IsControlledByPlayer { get; set; }
    public bool IsControlledByAi { get; set; }
    public bool IsUnit { get; set; }
    public bool CanAttackAndIsAttackEnabled { get; set; }
    public bool CanMoveAndMovementIsEnabled { get; set; }
    public bool IsCombatUnit { get; set; }
    public bool IsEngineer { get; set; }
    public float HealAmount1 { get; set; } = 0f;
    public bool canNotBeHealed { get; set; } = false;
    public void MoveTo(Vector3 destination, bool countsAsMoveCommand = true) { Position = destination; }
    public bool canNotBeAttacked { get; set; }
    public bool canNotBeTargetedByAi { get; set; }
    public bool doNotRegisterGlobally { get; set; }
    public bool CommandChainExecutionIsPaused { get; set; }
    public bool HasSkillActivationInCommandChain { get; set; }
    public bool IsTargetInRange { get; set; }
    public bool IsBuilding { get; set; }
    public bool IsFactory { get; set; }
    public int? InProductionCount { get; set; }
    private ProductionInfo _productionInfo;
    private float _lastAiSkillActivationCheckTime;
    public float LastAiSkillActivationCheckTimeForTest
    {
        get => _lastAiSkillActivationCheckTime;
        set => _lastAiSkillActivationCheckTime = value;
    }
    public bool HideProductionInfoProperty { get; set; }
    public ProductionInfo ProductionInfo
    {
        get => HideProductionInfoProperty ? null : _productionInfo;
        set => _productionInfo = value;
    }
    private void FixedUpdate() { }
    public bool IsProductionPossible(int balance) => true;
    public int ProduceCallCount { get; private set; }
    public void Produce(bool instant = false, bool forFree = false, bool noEvent = false)
    {
        ProduceCallCount++;
        InProductionCount = (InProductionCount ?? 0) + 1;
    }
    public UnitRole Role { get; set; }
    public int aiBaseIndex { get; set; }
    public float CurrentHealth { get; set; } = 100f;
    public float MaxHealth { get; set; } = 100f;
    public float DeltaHealth => MaxHealth - CurrentHealth;
    public float CurrentArmor { get; set; } = 0f;
    public float MaxArmor { get; set; } = 0f;
    public float DeltaArmor => MaxArmor - CurrentArmor;
    public bool HasActiveSkill { get; set; } = true;
    private bool? _hasShieldOverride;
    public bool HasShield
    {
        get => _hasShieldOverride ?? (MaxShield > 0f);
        set => _hasShieldOverride = value;
    }
    public float CurrentShield { get; set; } = 0f;
    public float MaxShield { get; set; } = 0f;
    public int CurrentRank { get; set; } = 0;
    public int MaxRank { get; set; } = 3;
    public void Heal(float amount, EntityController originator = null, bool doNotFireOnHasHealed = false, bool doNotFireOnBeingHealed = false)
    {
        CurrentHealth = System.Math.Min(CurrentHealth + amount, MaxHealth);
        if (originator == null && CurrentHealth > 0f && !doNotFireOnHasHealed)
        {
            throw new NullReferenceException("EntityController.Heal requires a non-null originator unless callbacks are suppressed.");
        }
    }
    public void RepairArmor(float amount, EntityController originator = null, bool doNotFire = false)
    {
        float currentArmor = CurrentArmor;
        CurrentArmor = System.Math.Min(CurrentArmor + amount, MaxArmor);
        if (originator == null && currentArmor < CurrentArmor && !doNotFire)
        {
            throw new NullReferenceException("EntityController.RepairArmor requires a non-null originator unless callbacks are suppressed.");
        }
    }
    public void ChargeShield(float amount, bool displayDeltaInBar = true)
    {
        if (!HasShield) return;
        CurrentShield = System.Math.Min(CurrentShield + amount, MaxShield);
    }
    public void TakeDamage(float amount, EntityController originator = null, bool doNotFireOnHasDealtDamage = false, bool ignoreArmor = false)
    {
        float damageRemaining = amount;
        if (!ignoreArmor && CurrentArmor > 0f)
        {
            float armorAbsorbed = System.Math.Min(CurrentArmor, damageRemaining);
            CurrentArmor -= armorAbsorbed;
            damageRemaining -= armorAbsorbed;
        }
        CurrentHealth = System.Math.Max(0f, CurrentHealth - damageRemaining);
    }
    public void RankUp(int amount) { CurrentRank = System.Math.Min(CurrentRank + amount, MaxRank); }
    public bool IsEnemy(EntityController other) => other != null && (IsControlledByPlayer != other.IsControlledByPlayer || IsControlledByAi != other.IsControlledByAi);
    public void SetActiveStatusEffect(StatusEffect effect, SetStatusEffect.DurationType durationType, float duration)
    {
        AppliedStatusEffects.Add((effect, durationType, duration));
        _activeStatusEffects.Add(effect);
    }
    public bool HasActiveStatusEffect(StatusEffect effect) => _activeStatusEffects.Contains(effect);
    public void RemoveStatusEffect(StatusEffect effect)
    {
        _activeStatusEffects.Remove(effect);
        AppliedStatusEffects.RemoveAll(x => x.Effect == effect);
    }
    public void AddSpecificValueChange(ChangeableValue changeableValue, SpecificValueChange change, bool isStackable = true)
    {
        if (!isStackable)
        {
            AppliedSpecificValueChanges.RemoveAll(c =>
                c.Value == changeableValue &&
                string.Equals(c.Change.OriginatorEntityId, change.OriginatorEntityId, System.StringComparison.Ordinal));
        }
        AppliedSpecificValueChanges.Add((changeableValue, change, isStackable));
    }
    public float MoveSpeed { get; set; } = 10f;
    public float WeaponRange { get; set; } = 15f;
    public float Damage1 { get; set; } = 10f;
    public float Damage2 { get; set; } = 0f;
    public float Attack1Cooldown { get; set; } = 1f;
    public float Attack2Cooldown { get; set; } = 1f;
    public string EntityId { get => entityId; set => entityId = value; }
    public string entityId { get; set; }
    public int UniqueEntityId { get; set; }
    public Vector2Int CurrentCell { get; set; }
    public Vector3 Position { get; set; }
    public EntityController CurrentAttackTarget { get; set; }
    public EntityCommand.Command CurrentPlayerCommandName { get; set; }
    public LinkedList<EntityCommand> CommandChainForTest { get { return _commandChain; } }
    public List<Vector3> PatrolPositions { get; } = new();
    public int ClearPatrolPositionsCallCount { get; private set; }
    public EntityCommand? LastExecutedCommand { get; private set; }
    public int ExecuteCommandCallCount { get; private set; }
    public CommandProcessingType LastCommandProcessingType { get; private set; }
    public string CurrentCommandTag { get; private set; }
    public EntityController CaptainOfThisEntity { get; set; }
    public SkillInfo SkillInfo { get; set; }
    public float CurrentMana { get; set; } = 100f;

    public void SetCaptain(EntityController captain) { CaptainOfThisEntity = captain; }
    public void AddAiTag(string tag) { if (!string.IsNullOrWhiteSpace(tag)) AiTags.Add(tag); }
    public void RemoveAiTag(string tag) { if (tag != null) AiTags.Remove(tag); }
    public void RemoveAllAiTags() { AiTags.Clear(); }
    public void RemoveCaptain(EntityController captain) { if (CaptainOfThisEntity == captain) CaptainOfThisEntity = null; }

    public enum CommandProcessingType
    {
        ExecuteBeforeCurrentCommand,
        ExecuteAndCancelCurrentCommand,
        ExecuteAndDeleteCommandChain,
        AppendToCommandChain
    }

    public bool HasRole(UnitRole role) => _roles.Contains(role) || (Role & role) == role;

    public bool CurrentCommandIsTaggedWith(string tag) =>
        tag != null && string.Equals(CurrentCommandTag, tag, System.StringComparison.Ordinal);

    public void ClearPatrolPositions()
    {
        PatrolPositions.Clear();
        ClearPatrolPositionsCallCount++;
    }

    public void AddPatrolPosition(Vector3 position) => PatrolPositions.Add(position);

    public bool ArePatrolPositionsEmpty() => PatrolPositions.Count < 1;

    public void ExecuteCommand(EntityCommand command, CommandProcessingType processingType)
    {
        LastExecutedCommand = command;
        ExecuteCommandCallCount++;
        LastCommandProcessingType = processingType;
        CurrentCommandTag = command.tag;
        if (command.target != null)
        {
            CurrentAttackTarget = command.target;
        }
        if (command.command == EntityCommand.Command.Move)
        {
            MoveTo(command.destination);
        }
    }
}

[System.Flags]
public enum UnitRole : int
{
    None = 0,
    Spawn = 1,
    Harvester = 2,
    FrontalAttacker = 4,
    MassDestruction = 8,
    Fire = 16,
    Sniper = 32,
    Supporter = 64,
    Rock = 128,
    Robo = 256,
    Spawner = 512,
    PCXCard = 1024,
    Building = 2048,
    Factory = 4096,
    Turret = 8192,
    Refinery = 16384,
    Unit = 32768,
    Heal = 65536,
    Specialist = 131072,
    Skill = 262144,
    Engineer = 524288,
    Bomb = 1048576,
    Scout = 2097152,
    Chest = 4194304,
    Tank = 8388608,
    Vehicle = 16777216,
    Mech = 33554432,
    Tree = 67108864,
    Melee = 134217728,
    Research = 268435456,
    Crystal = 536870912,
    Builder = 1073741824,
    Drop = int.MinValue,
    All = -1
}

public struct EntityCommand
{
    public string tag;
    public Vector3 destination;
    public string intent;
    public Role role;
    public Command command;
    public int intValue;
    public EntityController target;

    public enum Command
    {
        DoNothing = 0,
        Move = 1,
        FollowTarget = 2,
        Attack = 3,
        AttackMoveOnTarget = 4,
        AttackMoveOnPosition = 5,
        ActiveSkillWithoutTarget = 6,
        ActiveSkillOnTarget = 7,
        ActiveSkillAtPosition = 8,
        FollowPosition = 9,
        PlaceBuilding = 10,
        HoldPosition = 11,
        AddAiTag = 12,
        RemoveAllAiTags = 13,
        RemoveAiTag = 14,
        WaitUntilTimestamp = 15,
        SetTempChangedOriginalMoveSpeed = 16,
        ResetTempChangedOriginalMoveSpeed = 17,
        GiveAiGroupId = 18,
        Stop = 19,
        Patrol = 20
    }

    public enum Role
    {
        None = 0,
        ThisCommandWantsPathToBeCleared = 1,
        PathClearingCommand = 2,
        GetUnstuckCommand = 3
    }

    public static EntityCommand AttackMoveOnPosition(Vector3 destination, Role role, string intent)
    {
        return new EntityCommand
        {
            destination = destination,
            role = role,
            intent = intent,
            command = Command.AttackMoveOnPosition
        };
    }

    public static EntityCommand Patrol(int patrolIndex) =>
        new() { command = Command.Patrol, intValue = patrolIndex };

    public static EntityCommand Stop() =>
        new() { command = Command.Stop };

    public static EntityCommand Attack(EntityController target) =>
        new() { command = Command.Attack, target = target };

    public static EntityCommand AttackMoveOnTarget(EntityController target, Role role) =>
        new() { command = Command.AttackMoveOnTarget, role = role, target = target };

    public static EntityCommand Move(Vector3 destination, Vector2Int? clickPositionCell = null, Role role = Role.None) =>
        new() { command = Command.Move, destination = destination, role = role };

    public static EntityCommand ActiveSkillWithoutTarget() =>
        new() { command = Command.ActiveSkillWithoutTarget };

    public static EntityCommand ActiveSkillOnTarget(EntityController target) =>
        new() { command = Command.ActiveSkillOnTarget, target = target };

    public static EntityCommand ActiveSkillAtPosition(Vector3 position) =>
        new() { command = Command.ActiveSkillAtPosition, destination = position };
}

public enum TargetOrigin
{
    Self,
    ChosenEntity,
    ChosenLocation
}

public sealed class ProductionInfo
{
    public string entityId;
    public int queuedCount;
}

public class SkillInfo
{
    public TargetOrigin targetOrigin { get; set; } = TargetOrigin.Self;
    public float manaCost { get; set; } = 0f;
}

public sealed class WorldGrid
{
    public static WorldGrid Instance { get; set; } = new();
    public float WorldWidth = 200f;
    public float WorldHeight = 200f;
    public int HorizontalCellCount = 100;
    public int VerticalCellCount = 100;
    public float CellSize = 2f;
    public Vector2Int PlayerStartCell { get; set; } = new(10, 10);
    public Vector2Int AiStartCell { get; set; } = new(90, 90);
    public Vector3 ClampToWorld(Vector3 position) => new(Mathf.Clamp(position.x, 0f, WorldWidth), position.y, Mathf.Clamp(position.z, 0f, WorldHeight));
    public bool IsWithinWorld(Vector3 position) => position.x >= 0f && position.z >= 0f && position.x <= WorldWidth && position.z <= WorldHeight;
    public bool IsBorderCell(Vector2Int cell) => cell.x <= 0 || cell.y <= 0 || cell.x >= HorizontalCellCount - 1 || cell.y >= VerticalCellCount - 1;
    public float Height(Vector2Int cell) => 0f;
}

public static class Bank
{
    public static List<(string Account, float Amount)> Deposits { get; } = new();
    public static int ActualBalance(string tag) => 1000;
    public static void Deposit(string accountName, float amount)
    {
        Deposits.Add((accountName, amount));
    }
    public static void Reset() => Deposits.Clear();
}

public sealed class ExistingControllers
{
    public static ExistingControllers Instance { get; } = new();
    public bool ThrowOnAiBuildings { get; set; }

    private readonly HashSet<EntityController> _playerUnits = new();
    private readonly HashSet<EntityController> _aiUnits = new();
    private readonly HashSet<EntityController> _aiBuildings = new();
    private readonly HashSet<EntityController> _harvesters = new();
    private readonly HashSet<EntityController> _playerRefineries = new();
    private readonly HashSet<EntityController> _playerBuildingsKnownToAi = new();
    private readonly HashSet<EntityController> _playerBuildings = new();
    public EntityController EngineeringHqEntity { get; set; }

    public HashSet<EntityController> PlayerUnits() => _playerUnits;
    public HashSet<EntityController> PlayerEntities() => _playerUnits;
    public HashSet<EntityController> AiUnits() => _aiUnits;
    public IEnumerable<EntityController> Entities() => _playerUnits.Concat(_aiUnits).Concat(_aiBuildings);
    public HashSet<EntityController> Harvesters => _harvesters;
    public HashSet<EntityController> PlayerRefineries() => _playerRefineries;
    public HashSet<EntityController> PlayerBuildingsKnownToAi() => _playerBuildingsKnownToAi;
    public HashSet<EntityController> PlayerBuildings() => _playerBuildings;
    public EntityController EngineeringHq() => EngineeringHqEntity;
    public HashSet<EntityController> AiBuildings()
    {
        if (ThrowOnAiBuildings)
        {
            throw new System.InvalidOperationException("simulated AI building registry failure");
        }

        return _aiBuildings;
    }

    public void Reset()
    {
        _playerUnits.Clear();
        _aiUnits.Clear();
        _aiBuildings.Clear();
        _harvesters.Clear();
        _playerRefineries.Clear();
        _playerBuildingsKnownToAi.Clear();
        _playerBuildings.Clear();
        EngineeringHqEntity = null;
        ThrowOnAiBuildings = false;
    }
}

public class PlayerUnitController : MonoBehaviour
{
    private static PlayerUnitController _instance = new PlayerUnitController();
    public static PlayerUnitController Instance => _instance ??= new PlayerUnitController();

    private readonly HashSet<GameObject> _selectedGameObjects = new();
    private EntityController _selectedAiEntityController;
    public EntityController SelectedAiEntityController => _selectedAiEntityController;
    private readonly List<EntityController> _selectedEntitiesWithActiveSkill = new();
    private readonly HashSet<GameObject>[] _controlGroups = new HashSet<GameObject>[10];

    public bool OnlyBuildingsSelected { get; set; }
    public bool OnlyEntitiesThatCannotAttackSelected { get; set; }

    public PlayerUnitController()
    {
        for (int i = 0; i < 10; i++)
        {
            _controlGroups[i] = new HashSet<GameObject>();
        }
        _instance = this;
    }

    public void Select(GameObject go)
    {
        _selectedGameObjects.Clear();
        if (go != null) _selectedGameObjects.Add(go);
    }

    public void AddToSelection(GameObject go)
    {
        if (go != null) _selectedGameObjects.Add(go);
    }

    public void Deselect(GameObject go, bool flag)
    {
        if (go != null) _selectedGameObjects.Remove(go);
    }

    public void DeselectAllUnits()
    {
        _selectedGameObjects.Clear();
        _selectedAiEntityController = null;
        _selectedEntitiesWithActiveSkill.Clear();
    }

    public static void DeselectAll_Static() => Instance.DeselectAllUnits();

    public void SelectAllCombatUnits()
    {
        DeselectAllUnits();
        foreach (var u in ExistingControllers.Instance.PlayerUnits())
        {
            if (u.gameObject != null) _selectedGameObjects.Add(u.gameObject);
        }
    }

    public void SelectAllHarvesters()
    {
        DeselectAllUnits();
        foreach (var h in ExistingControllers.Instance.Harvesters)
        {
            if (h.gameObject != null) _selectedGameObjects.Add(h.gameObject);
        }
    }

    public void SelectIdleHarvesters() => SelectAllHarvesters();
    public void SelectSkillBuildings() => DeselectAllUnits();
    public void SelectAllFactories() => DeselectAllUnits();

    public static bool SetControlGroup_Static(int number)
    {
        if (number < 0 || number >= 10) return false;
        Instance._controlGroups[number].Clear();
        foreach (var go in Instance._selectedGameObjects)
        {
            Instance._controlGroups[number].Add(go);
        }
        return true;
    }

    public static void SelectControlGroup_Static(int number)
    {
        if (number < 0 || number >= 10) return;
        Instance.DeselectAllUnits();
        foreach (var go in Instance._controlGroups[number])
        {
            Instance._selectedGameObjects.Add(go);
        }
    }

    public static void Stop_Static() { }
    public static void HoldPosition_Static() { }
    public static void AttackMove_Static(Vector3 pos) { }
    public static bool IsAnyEntitySelected_Static() => Instance._selectedGameObjects.Count > 0;
    public static bool IsSelected_Static(string id) => false;
}

public class UpdateFogOfWar : MonoBehaviour
{
    private static UpdateFogOfWar _instance = new UpdateFogOfWar();
    public static UpdateFogOfWar Instance => _instance ??= new UpdateFogOfWar();

    public bool disableFogOfWar;
    public float updateIntervalInSeconds = 0.5f;
    private object _blendAmountTexture = new object();

    public UpdateFogOfWar()
    {
        _instance = this;
    }

    public static bool RevealedStaticCalled { get; set; }
    public static void Reveal_Static()
    {
        RevealedStaticCalled = true;
    }
}

public class FinishLevel : MonoBehaviour
{
    private static FinishLevel _instance;
    public UnityEngine.Audio.AudioMixer audioMixer = new UnityEngine.Audio.AudioMixer();
    public static FinishLevel Instance => _instance ??= new FinishLevel();

    private static bool _isFinishing;
    public static bool WonCalled { get; set; }
    public static bool LostCalled { get; set; }

    public FinishLevel()
    {
        _instance = this;
    }

    public static bool IsFinishing_Static() => _isFinishing;
    public static void Win_Static(Vector3 pos, AiBehaviour beatenAi)
    {
        WonCalled = true;
        _isFinishing = true;
    }

    public static void Lose_Static(Vector3 pos)
    {
        LostCalled = true;
        _isFinishing = true;
    }

    public void Win() => Win_Static(Vector3.zero, null);
    public void Lose() => Lose_Static(Vector3.zero);
    public static void Reset()
    {
        _isFinishing = false;
        WonCalled = false;
        LostCalled = false;
    }
}

public static class EntityFactory
{
    public static Func<string, Vector3, EntityController, string, string, Transform, UnitRole, bool, string, EntityController> CustomInstantiate { get; set; }

    public static EntityController InstantiateEntity(
        string entityId,
        Vector3 position,
        EntityController originEntity,
        string tag = "",
        string name = "",
        Transform parentTransform = null,
        UnitRole additionalRoles = UnitRole.None,
        bool hasBeenCalledFromAbove = false,
        string instantiationInfo = "")
    {
        if (CustomInstantiate != null)
        {
            return CustomInstantiate(entityId, position, originEntity, tag, name, parentTransform, additionalRoles, hasBeenCalledFromAbove, instantiationInfo);
        }

        var ec = new EntityController
        {
            EntityId = string.IsNullOrEmpty(name) ? entityId : name,
            CurrentHealth = 100f,
            MaxHealth = 100f,
            tag = string.IsNullOrEmpty(tag) ? "Player" : tag
        };
        ec.transform.position = position;
        return ec;
    }
}

public static class Tags
{
    public const string Player = "Player";
    public const string Ai = "Ai";
    public const string Neutral = "Neutral";
    public static bool IsPlayer(string tag) => string.Equals(tag, Player, StringComparison.OrdinalIgnoreCase);
    public static bool IsAi(string tag) => string.Equals(tag, Ai, StringComparison.OrdinalIgnoreCase);
}

public class CameraZoom : MonoBehaviour
{
    public float minFov = 20f;
    public float maxFov = 80f;
    public float defaultFov = 45f;
    public float zoomSpeed = 5f;
}

public class Harvest : Component
{
    public float _currentLoad = 25f;
    public float _loadFraction = 0.25f;
    public int MaxValue = 100;
    public string _currentState = "Mining";

    public static bool ClaimCalled { get; set; }
    public static bool RefineryCalled { get; set; }

    public void ClaimClosestCrystalAndMoveToIt()
    {
        ClaimCalled = true;
    }

    public void MoveToClosestRefinery()
    {
        RefineryCalled = true;
    }

    public void Stop() { }
    public void Pause() { }
    public void Resume() { }
}



public static class AudioMixerHelper
{
    public static float MasterSlider { get; set; } = 0.8f;
    public static float MusicSlider { get; set; } = 0.6f;
    public static float GameplaySlider { get; set; } = 0.9f;
    public static float UiSlider { get; set; } = 0.7f;

    public static void SetMasterVolume(UnityEngine.Audio.AudioMixer m, float v) => MasterSlider = v;
    public static void SetMusicVolume(UnityEngine.Audio.AudioMixer m, float v) => MusicSlider = v;
    public static void SetGameplayVolume(UnityEngine.Audio.AudioMixer m, float v) => GameplaySlider = v;
    public static void SetUiSelectionSoundsVolume(UnityEngine.Audio.AudioMixer m, float v) => UiSlider = v;

    public static float GetMasterSliderValue(UnityEngine.Audio.AudioMixer m) => MasterSlider;
    public static float GetMusicSliderValue(UnityEngine.Audio.AudioMixer m) => MusicSlider;
    public static float GetGameplaySliderValue(UnityEngine.Audio.AudioMixer m) => GameplaySlider;
    public static float GetUiSelectionSoundsSliderValue(UnityEngine.Audio.AudioMixer m) => UiSlider;
}

public static class UpgradeBalancingStore
{
    public static System.Collections.Generic.List<string> Upgrades = new System.Collections.Generic.List<string>
    {
        "Upgrade_HullReinforcement",
        "Upgrade_KineticPenetrator",
        "Upgrade_ShieldOvercharge"
    };

    public static System.Collections.Generic.List<string> AllUpgradeIds() => Upgrades;
}

public static class ResearchBalancingStore
{
    public static System.Collections.Generic.List<string> Research = new System.Collections.Generic.List<string>
    {
        "Research_AdvancedRobotics",
        "Research_NanoFabrication"
    };

    public static System.Collections.Generic.List<string> AllResearchIds() => Research;
}

public static class RelicBalancingStore
{
    public static System.Collections.Generic.List<string> Relics = new System.Collections.Generic.List<string>
    {
        "Relic_FoundryCounterweight",
        "Relic_OverclockMatrix",
        "Relic_ReactiveCore"
    };

    public static System.Collections.Generic.List<string> AllRelicIds() => Relics;
}


namespace RogueCommandExpansionLoader.Runtime.Visual
{
    public sealed class RuntimeVisualDirector : Component
    {
        public static void AttachIfApplicable(object entityObj) { }
        public static System.Action<object> OnEntityReusedAction;
        public static void OnEntityReused(object entityObj) => OnEntityReusedAction?.Invoke(entityObj);
        public bool IsTurretJammed { get; set; }
        public float CurrentRecoilOffset { get; set; }
        public float DoorOpenProgress { get; set; }
        public bool IsStunnedVisualActive { get; set; }
        public bool IsOnFireVisualActive { get; set; }
        public bool IsBoostedVisualActive { get; set; }
        public bool DeathAnimationTriggeredForDestruction { get; private set; }
        public bool CanBeginDeathVisualRetirement { get; set; }
        public bool DeathVisualRetirementPending { get; private set; }
        public int DeathVisualRetirementAttemptCount { get; private set; }
        public string ActiveTacticalStanceVisual { get; set; } = "Standard";
        public string ActiveVisualVariantId { get; set; }
        public string ActivePresetName { get; set; }
        public int ActiveVisualModuleCount { get; set; }
        public Color? ActiveColorTint { get; set; }
        public readonly Dictionary<string, bool> AttachmentStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public void TriggerDeathAnimationForDestruction()
        {
            DeathAnimationTriggeredForDestruction = true;
        }

        public bool TryBeginDeathVisualRetirementForDestruction()
        {
            DeathVisualRetirementAttemptCount++;
            if (!CanBeginDeathVisualRetirement || !DeathAnimationTriggeredForDestruction)
            {
                return false;
            }

            DeathVisualRetirementPending = true;
            return true;
        }

        public bool TryApplyVisualVariant(RuntimeUnitVisualVariant variant)
        {
            if (variant == null)
            {
                ResetVisualVariant();
                return true;
            }
            ActiveVisualVariantId = variant.VariantId;
            ActivePresetName = variant.PresetName;
            ActiveColorTint = variant.ColorTint;
            return true;
        }

        public void ResetVisualVariant()
        {
            ActiveVisualVariantId = null;
            ActivePresetName = null;
            ActiveColorTint = null;
        }

        public void SetTacticalStanceVisual(string stance)
        {
            if (string.IsNullOrEmpty(stance)) return;
            bool wasDefensive = string.Equals(ActiveTacticalStanceVisual, "Defensive", StringComparison.OrdinalIgnoreCase);
            bool isDefensive = string.Equals(stance, "Defensive", StringComparison.OrdinalIgnoreCase);
            ActiveTacticalStanceVisual = stance;
            if (isDefensive && !wasDefensive)
            {
                SetAttachmentActive("Armor", true);
                SetAttachmentActive("Shield", true);
                SetAttachmentActive("APS", true);
            }
            else if (!isDefensive && wasDefensive)
            {
                SetAttachmentActive("Armor", false);
                SetAttachmentActive("Shield", false);
                SetAttachmentActive("APS", false);
            }
        }

        public void SetAttachmentActive(string attachmentType, bool active)
        {
            AttachmentStates[attachmentType] = active;
        }

        public void SetTurretJammedOverride(bool? jammed) => IsTurretJammed = jammed ?? false;
        public void SetDoorOpenOverride(float? progress) => DoorOpenProgress = progress ?? 0f;
        public void TriggerWeaponFeedback(float recoil = 0.22f) => CurrentRecoilOffset = recoil;
        public void TriggerVisualFeedback(string feedbackType, float magnitude = 0.22f)
        {
            if (feedbackType == "CritImpact" || feedbackType == "Recoil")
            {
                CurrentRecoilOffset = magnitude;
            }
        }
        public bool TrySetAttachmentState(string attachmentName, bool active)
        {
            AttachmentStates[attachmentName] = active;
            return true;
        }
        public void TriggerFootstepFeedback(string footTag) { }
        public string GetVisualStatusSummary() => "Status=OK,Jammed=" + IsTurretJammed;
    }
}
