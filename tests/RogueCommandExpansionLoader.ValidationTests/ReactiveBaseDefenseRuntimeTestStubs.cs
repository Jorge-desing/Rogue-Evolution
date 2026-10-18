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
    public int aiBaseIndex { get; set; }
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

    public bool HasRole(UnitRole role) => _roles.Contains(role);

    public bool CurrentCommandIsTaggedWith(string tag) =>
        tag != null && string.Equals(CurrentCommandTag, tag, System.StringComparison.Ordinal);

    public void ExecuteCommand(EntityCommand command, CommandProcessingType processingType)
    {
        LastExecutedCommand = command;
        LastCommandProcessingType = processingType;
        CurrentCommandTag = command.tag;
    }
}

public enum UnitRole
{
    Spawn,
    Skill
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

    private readonly HashSet<EntityController> _playerUnits = new();
    private readonly HashSet<EntityController> _aiUnits = new();

    public HashSet<EntityController> PlayerUnits() => _playerUnits;
    public HashSet<EntityController> AiUnits() => _aiUnits;

    public void Reset()
    {
        _playerUnits.Clear();
        _aiUnits.Clear();
    }
}
