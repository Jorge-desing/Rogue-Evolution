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

public sealed class EntityController : Component
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
    public bool IsProductionPossible(int balance) => true;
    public void Produce(bool instant = false, bool forFree = false, bool noEvent = false) { }
    public UnitRole Role { get; set; }
    public int aiBaseIndex { get; set; }
    public float CurrentHealth { get; set; } = 100f;
    public float MaxHealth { get; set; } = 100f;
    public float DeltaHealth => MaxHealth - CurrentHealth;
    public float CurrentArmor { get; set; } = 0f;
    public float MaxArmor { get; set; } = 0f;
    public float DeltaArmor => MaxArmor - CurrentArmor;
    public bool HasShield { get; set; }
    public float CurrentShield { get; set; } = 0f;
    public float MaxShield { get; set; } = 0f;
    public int CurrentRank { get; set; } = 0;
    public int MaxRank { get; set; } = 3;
    public void Heal(float amount, EntityController originator) { CurrentHealth = System.Math.Min(CurrentHealth + amount, MaxHealth); }
    public void RepairArmor(float amount, EntityController originator, bool doNotFire = false) { CurrentArmor = System.Math.Min(CurrentArmor + amount, MaxArmor); }
    public void ChargeShield(float amount, bool displayDeltaInBar = true) { CurrentShield = System.Math.Min(CurrentShield + amount, MaxShield); }
    public void RankUp(int amount) { CurrentRank = System.Math.Min(CurrentRank + amount, MaxRank); }
    public bool IsEnemy(EntityController other) => other != null && (IsControlledByPlayer != other.IsControlledByPlayer || IsControlledByAi != other.IsControlledByAi);
    public void SetActiveStatusEffect(StatusEffect effect, SetStatusEffect.DurationType durationType, float duration)
    {
        AppliedStatusEffects.Add((effect, durationType, duration));
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
    public string EntityId { get => entityId; set => entityId = value; }
    public string entityId { get; set; }
    public string name { get => gameObject?.name ?? string.Empty; set { if (gameObject != null) gameObject.name = value; } }
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
        new() { command = Command.Attack };

    public static EntityCommand AttackMoveOnTarget(EntityController target, Role role) =>
        new() { command = Command.AttackMoveOnTarget, role = role };

    public static EntityCommand Move(Vector3 destination, Vector2Int? clickPositionCell = null, Role role = Role.None) =>
        new() { command = Command.Move, destination = destination, role = role };

    public static EntityCommand ActiveSkillWithoutTarget() =>
        new() { command = Command.ActiveSkillWithoutTarget };

    public static EntityCommand ActiveSkillOnTarget(EntityController target) =>
        new() { command = Command.ActiveSkillOnTarget };

    public static EntityCommand ActiveSkillAtPosition(Vector3 position) =>
        new() { command = Command.ActiveSkillAtPosition, destination = position };
}

public enum TargetOrigin
{
    Self,
    ChosenEntity,
    ChosenLocation
}

public class SkillInfo
{
    public TargetOrigin targetOrigin { get; set; } = TargetOrigin.Self;
    public float manaCost { get; set; } = 0f;
}

public sealed class WorldGrid
{
    public static WorldGrid Instance { get; set; } = new();
    public Vector3 ClampToWorld(Vector3 position) => position;
    public bool IsWithinWorld(Vector3 position) => true;
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
    public EntityController EngineeringHqEntity { get; set; }

    public HashSet<EntityController> PlayerUnits() => _playerUnits;
    public HashSet<EntityController> PlayerEntities() => _playerUnits;
    public HashSet<EntityController> AiUnits() => _aiUnits;
    public IEnumerable<EntityController> Entities() => _playerUnits.Concat(_aiUnits).Concat(_aiBuildings);
    public HashSet<EntityController> Harvesters => _harvesters;
    public HashSet<EntityController> PlayerRefineries() => _playerRefineries;
    public HashSet<EntityController> PlayerBuildingsKnownToAi() => _playerBuildingsKnownToAi;
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
        EngineeringHqEntity = null;
        ThrowOnAiBuildings = false;
    }
}
