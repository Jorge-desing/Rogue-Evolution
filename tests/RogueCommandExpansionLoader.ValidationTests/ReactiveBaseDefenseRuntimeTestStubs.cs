using System.Collections.Generic;
using UnityEngine;

public sealed class EntityController
{
    private readonly HashSet<UnitRole> _roles = new();

    public bool IsDestroyed { get; set; }
    public bool DestroyHasBeenStarted { get; set; }
    public bool IsControlledByPlayer { get; set; }
    public bool IsControlledByAi { get; set; }
    public bool IsUnit { get; set; }
    public bool CanAttackAndIsAttackEnabled { get; set; }
    public bool CanMoveAndMovementIsEnabled { get; set; }
    public bool IsCombatUnit { get; set; }
    public bool IsEngineer { get; set; }
    public bool canNotBeAttacked { get; set; }
    public bool canNotBeTargetedByAi { get; set; }
    public bool doNotRegisterGlobally { get; set; }
    public bool CommandChainExecutionIsPaused { get; set; }
    public bool HasSkillActivationInCommandChain { get; set; }
    public bool IsTargetInRange { get; set; }
    public bool IsBuilding { get; set; }
    public UnitRole Role { get; set; }
    public int aiBaseIndex { get; set; }
public float CurrentHealth { get; set; } = 100f;
    public float MaxHealth { get; set; } = 100f;
    public string EntityId { get => entityId; set => entityId = value; }
    public string entityId { get; set; }
    public string name { get => gameObject?.name ?? string.Empty; set { if (gameObject != null) gameObject.name = value; } }
    public int UniqueEntityId { get; set; }
    public Vector2Int CurrentCell { get; set; }
    public Vector3 Position { get; set; }
    public EntityController CurrentAttackTarget { get; set; }
    public EntityCommand.Command CurrentPlayerCommandName { get; set; }
    public GameObject gameObject { get; set; } = new("test-entity");
    public EntityCommand LastExecutedCommand { get; private set; }
    public CommandProcessingType LastCommandProcessingType { get; private set; }
    public string CurrentCommandTag { get; private set; }

    public enum CommandProcessingType
    {
        ExecuteBeforeCurrentCommand
    }

    public bool HasRole(UnitRole role) => _roles.Contains(role) || (Role & role) == role;

    public bool CurrentCommandIsTaggedWith(string tag) =>
        tag != null && string.Equals(CurrentCommandTag, tag, System.StringComparison.Ordinal);

    public void ExecuteCommand(EntityCommand command, CommandProcessingType processingType)
    {
        LastExecutedCommand = command;
        LastCommandProcessingType = processingType;
        CurrentCommandTag = command.tag;
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

public sealed class EntityCommand
{
    public string tag;
    public Vector3 position;
    public string description;
    public Role role;

    public enum Command
    {
        DoNothing,
        Move
    }

    public enum Role
    {
        None
    }

    public static EntityCommand AttackMoveOnPosition(Vector3 position, Role role, string description)
    {
        return new EntityCommand { position = position, role = role, description = description };
    }
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
    public HashSet<EntityController> AiUnits() => _aiUnits;
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
