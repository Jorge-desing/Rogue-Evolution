#nullable enable
using UnityEngine;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.RegularExpressions;
using RogueCommandExpansionLoader.Compatibility;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Modules;
using RogueCommandExpansionLoader.Runtime;
using RogueCommandExpansionLoader.Runtime.Visual;

internal static partial class ValidationTestRunner
{
    internal static IEnumerable<(string Name, Action Run)> GetAiTacticalModulesValidationTests() => new (string Name, Action Run)[]
    {
    ("harvester-escort AI parser supplies bounded defaults", () =>
    {
        if (!HarvesterEscortParameterParser.TryParse(null, out HarvesterEscortSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.BaseIndex != 0 ||
            defaults.EscortRadiusCells != 8 || defaults.ScanIntervalSeconds != 5f ||
            defaults.MaxEscorts != 3 || !defaults.EngageAttackers)
        {
            throw new InvalidOperationException("The harvester-escort AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaryValues =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "escortRadiusCells", value = "64" },
            new ContentModuleParameter { name = "scanIntervalSeconds", value = "60" },
            new ContentModuleParameter { name = "maxEscorts", value = "16" },
            new ContentModuleParameter { name = "engageAttackers", value = "false" }
        ];
        if (!HarvesterEscortParameterParser.TryParse(boundaryValues, out HarvesterEscortSettings maximums,
            out diagnostic) || maximums.BaseIndex != 31 || maximums.EscortRadiusCells != 64 ||
            maximums.ScanIntervalSeconds != 60f || maximums.MaxEscorts != 16 || maximums.EngageAttackers)
        {
            throw new InvalidOperationException("The harvester-escort AI parser rejected valid upper-bound values.");
        }
    }),
    ("harvester-escort AI parser rejects invalid and duplicate parameters", () =>
    {
        string[] invalidInputs =
        [
            "baseIndex=32",
            "escortRadiusCells=0",
            "scanIntervalSeconds=0",
            "maxEscorts=0",
            "engageAttackers=maybe",
            "unknown=value"
        ];

        for (int i = 0; i < invalidInputs.Length; i++)
        {
            string[] parts = invalidInputs[i].Split('=');
            ContentModuleParameter[] parameters =
            [
                new ContentModuleParameter { name = parts[0], value = parts[1] }
            ];

            if (HarvesterEscortParameterParser.TryParse(parameters, out _, out _))
            {
                throw new InvalidOperationException("The harvester-escort AI parser accepted invalid input " + i + ".");
            }
        }

        ContentModuleParameter[] duplicate =
        [
            new ContentModuleParameter { name = "baseIndex", value = "0" },
            new ContentModuleParameter { name = "baseIndex", value = "1" }
        ];
        if (HarvesterEscortParameterParser.TryParse(duplicate, out _, out _))
        {
            throw new InvalidOperationException("The harvester-escort AI parser accepted a duplicate parameter.");
        }
    }),
    ("patrol-and-intercept AI parser applies documented defaults and accepts upper bounds", () =>
    {
        if (!PatrolAndInterceptParameterParser.TryParse(null, out PatrolAndInterceptSettings defaults,
                out string diagnostic) || diagnostic != null || defaults.BaseIndex != 0 ||
            defaults.PatrolRadiusCells != 15 || defaults.InterceptRadiusCells != 10 ||
            defaults.ScanIntervalSeconds != 8f || defaults.MaxPatrolUnits != 3 ||
            defaults.PatrolMode != "AttackMove")
        {
            throw new InvalidOperationException("Patrol-and-intercept defaults do not match the content contract.");
        }

        ContentModuleParameter[] upperBounds =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "patrolRadiusCells", value = "32" },
            new ContentModuleParameter { name = "interceptRadiusCells", value = "32" },
            new ContentModuleParameter { name = "scanIntervalSeconds", value = "60" },
            new ContentModuleParameter { name = "maxPatrolUnits", value = "8" }
        ];
        if (!PatrolAndInterceptParameterParser.TryParse(upperBounds,
                out PatrolAndInterceptSettings maximums, out diagnostic) || diagnostic != null ||
            maximums.BaseIndex != 31 || maximums.PatrolRadiusCells != 32 ||
            maximums.InterceptRadiusCells != 32 || maximums.ScanIntervalSeconds != 60f ||
            maximums.MaxPatrolUnits != 8)
        {
            throw new InvalidOperationException("Patrol-and-intercept rejected a documented upper bound.");
        }
    }),
    ("patrol-and-intercept parser accepts both supported patrol modes", () =>
    {
        foreach (string mode in new[] { "AttackMove", "Native" })
        {
            ContentModuleParameter[] parameters =
            [new ContentModuleParameter { name = "patrolMode", value = mode }];
            if (!PatrolAndInterceptParameterParser.TryParse(parameters,
                    out PatrolAndInterceptSettings settings, out string diagnostic) ||
                diagnostic != null || settings.PatrolMode != mode)
            {
                throw new InvalidOperationException("Patrol-and-intercept rejected supported mode " + mode + ".");
            }
        }
    }),
    ("patrol-and-intercept parser rejects invalid or duplicate patrol modes", () =>
    {
        ContentModuleParameter[][] invalidModes =
        [
            [new ContentModuleParameter { name = "patrolMode", value = "" }],
            [new ContentModuleParameter { name = "patrolMode", value = "Unknown" }],
            [
                new ContentModuleParameter { name = "patrolMode", value = "Native" },
                new ContentModuleParameter { name = "patrolMode", value = "AttackMove" }
            ]
        ];
        for (int i = 0; i < invalidModes.Length; i++)
        {
            if (PatrolAndInterceptParameterParser.TryParse(invalidModes[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("Patrol-and-intercept accepted invalid patrol mode input " + i + ".");
            }
        }
    }),
    ("patrol-and-intercept AI parser rejects invalid and duplicate parameters", () =>
    {
        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "unknown", value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "0" }, new ContentModuleParameter { name = "baseIndex", value = "1" }],
            [null!],
            [new ContentModuleParameter { name = "baseIndex", value = "32" }],
            [new ContentModuleParameter { name = "patrolRadiusCells", value = "4" }],
            [new ContentModuleParameter { name = "patrolRadiusCells", value = "33" }],
            [new ContentModuleParameter { name = "interceptRadiusCells", value = "4" }],
            [new ContentModuleParameter { name = "interceptRadiusCells", value = "33" }],
            [new ContentModuleParameter { name = "scanIntervalSeconds", value = "1" }],
            [new ContentModuleParameter { name = "scanIntervalSeconds", value = "61" }],
            [new ContentModuleParameter { name = "maxPatrolUnits", value = "0" }],
            [new ContentModuleParameter { name = "maxPatrolUnits", value = "9" }]
        ];
        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (PatrolAndInterceptParameterParser.TryParse(invalidInputs[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("Patrol-and-intercept accepted invalid input " + i + ".");
            }
        }
    }),
    ("patrol waypoint selection chooses the nearest reachable distinct cell", () =>
    {
        UnityEngine.Vector2Int[] cells =
        [
            new UnityEngine.Vector2Int(8, 0),
            new UnityEngine.Vector2Int(3, 0),
            new UnityEngine.Vector2Int(1, 1)
        ];
        if (!PatrolAndInterceptWaypointResolver.TrySelectNearest(
                new UnityEngine.Vector2Int(0, 0), cells, 5, out int selected) || selected != 2)
        {
            throw new InvalidOperationException("Patrol did not choose the nearest non-current waypoint inside its radius.");
        }

        if (PatrolAndInterceptWaypointResolver.TrySelectNearest(
                new UnityEngine.Vector2Int(0, 0),
                [new UnityEngine.Vector2Int(0, 0), new UnityEngine.Vector2Int(6, 0)], 5, out _))
        {
            throw new InvalidOperationException("Patrol accepted only a no-op or out-of-radius waypoint.");
        }

        if (PatrolAndInterceptWaypointResolver.TrySelectNearest(
                new UnityEngine.Vector2Int(Int32.MaxValue, Int32.MaxValue),
                [new UnityEngine.Vector2Int(Int32.MinValue, Int32.MinValue)], 5, out _))
        {
            throw new InvalidOperationException("Extreme cell coordinates overflowed the squared-distance check.");
        }
    }),
    ("native patrol route selects nearest distinct cells within the output cap", () =>
    {
        UnityEngine.Vector2Int[] cells =
        [
            new UnityEngine.Vector2Int(1, 0),
            new UnityEngine.Vector2Int(1, 0),
            new UnityEngine.Vector2Int(0, 1),
            new UnityEngine.Vector2Int(3, 0),
            new UnityEngine.Vector2Int(0, 0),
            new UnityEngine.Vector2Int(6, 0)
        ];
        int[] selected = new int[8];
        int count = PatrolAndInterceptWaypointResolver.SelectNearestRoute(
            new UnityEngine.Vector2Int(0, 0), cells, 5, selected);
        if (count != 3 || selected[0] != 2 || selected[1] != 0 || selected[2] != 3)
        {
            throw new InvalidOperationException("Native patrol route did not select unique nearest cells in deterministic order.");
        }

        int[] capped = new int[2];
        count = PatrolAndInterceptWaypointResolver.SelectNearestRoute(
            new UnityEngine.Vector2Int(0, 0), cells, 5, capped);
        if (count != 2 || capped[0] != 2 || capped[1] != 0)
        {
            throw new InvalidOperationException("Native patrol route exceeded its caller-provided output capacity.");
        }
    }),
    ("patrol-and-intercept default still dispatches attack-move waypoint orders", () =>
    {
        ExistingControllers.Instance.Reset();
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            UnityEngine.Time.time = 10f;
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                CurrentCell = new UnityEngine.Vector2Int(10, 10),
                Position = new UnityEngine.Vector3(10f, 0f, 10f)
            });
            var defender = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                CurrentCell = new UnityEngine.Vector2Int(9, 10)
            };
            ExistingControllers.Instance.AiUnits().Add(defender);

            behaviour = new PatrolAndInterceptAiModule().CreateBehaviour(
                new DefaultAiBehaviour(), null);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (!defender.LastExecutedCommand.HasValue ||
                defender.LastExecutedCommand.Value.command != EntityCommand.Command.AttackMoveOnPosition ||
                defender.LastExecutedCommand.Value.destination.x != 10f ||
                defender.LastExecutedCommand.Value.destination.z != 10f ||
                defender.PatrolPositions.Count != 0)
            {
                throw new InvalidOperationException("The default patrol mode no longer uses attack-move waypoint orders.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("native patrol mode assigns capped routes only to idle defenders", () =>
    {
        ExistingControllers.Instance.Reset();
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            UnityEngine.Time.time = 10f;
            var targetBuilding = new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                CurrentCell = new UnityEngine.Vector2Int(10, 10),
                Position = new UnityEngine.Vector3(10f, 0f, 10f),
                UniqueEntityId = 900
            };
            ExistingControllers.Instance.AiBuildings().Add(targetBuilding);
            for (int i = 1; i < 10; i++)
            {
                ExistingControllers.Instance.AiBuildings().Add(new EntityController
                {
                    IsControlledByAi = true,
                    IsBuilding = true,
                    aiBaseIndex = 0,
                    CurrentCell = new UnityEngine.Vector2Int(10 + i, 10),
                    Position = new UnityEngine.Vector3(10f + i, 0f, 10f),
                    UniqueEntityId = 900 + i
                });
            }

            var threat = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CurrentAttackTarget = targetBuilding,
                CurrentCell = new UnityEngine.Vector2Int(10, 11),
                Position = new UnityEngine.Vector3(30f, 0f, 40f),
                UniqueEntityId = 930
            };
            ExistingControllers.Instance.PlayerUnits().Add(threat);

            var idleA = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                CurrentCell = new UnityEngine.Vector2Int(9, 10),
                UniqueEntityId = 920
            };
            var idleB = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                CurrentCell = new UnityEngine.Vector2Int(8, 10),
                UniqueEntityId = 921
            };
            var idleC = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                CurrentCell = new UnityEngine.Vector2Int(13, 10),
                UniqueEntityId = 923
            };
            var moving = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.Move,
                CurrentCell = new UnityEngine.Vector2Int(9, 11),
                UniqueEntityId = 922
            };
            idleA.AddPatrolPosition(new UnityEngine.Vector3(-1f, 0f, -1f));
            idleB.AddPatrolPosition(new UnityEngine.Vector3(-2f, 0f, -2f));
            idleC.AddPatrolPosition(new UnityEngine.Vector3(-4f, 0f, -4f));
            moving.AddPatrolPosition(new UnityEngine.Vector3(-3f, 0f, -3f));
            ExistingControllers.Instance.AiUnits().Add(idleA);
            ExistingControllers.Instance.AiUnits().Add(idleB);
            ExistingControllers.Instance.AiUnits().Add(idleC);
            ExistingControllers.Instance.AiUnits().Add(moving);

            behaviour = new PatrolAndInterceptAiModule().CreateBehaviour(
                new DefaultAiBehaviour { instanceId = 902 },
                [
                    new ContentModuleParameter { name = "patrolMode", value = "Native" },
                    new ContentModuleParameter { name = "patrolRadiusCells", value = "32" },
                    new ContentModuleParameter { name = "maxPatrolUnits", value = "2" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (!idleA.LastExecutedCommand.HasValue ||
                idleA.LastExecutedCommand.Value.command != EntityCommand.Command.AttackMoveOnPosition ||
                idleA.LastExecutedCommand.Value.destination.x != threat.Position.x ||
                idleA.LastExecutedCommand.Value.destination.z != threat.Position.z ||
                !idleA.CurrentCommandIsTaggedWith(idleA.LastExecutedCommand.Value.tag) ||
                idleA.ClearPatrolPositionsCallCount != 0 || idleA.PatrolPositions.Count != 1)
            {
                throw new InvalidOperationException("Native patrol mode changed the existing attack-move intercept behavior.");
            }

            EntityController[] remainingIdleDefenders = [idleB, idleC];
            int nativePatrolDispatches = 0;
            int untouchedIdleDefenders = 0;
            foreach (EntityController defender in remainingIdleDefenders)
            {
                if (defender.LastExecutedCommand?.command == EntityCommand.Command.Patrol)
                {
                    nativePatrolDispatches++;
                    if (defender.LastExecutedCommand.Value.intValue != 0 ||
                        defender.LastCommandProcessingType != EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand ||
                        !defender.CurrentCommandIsTaggedWith(defender.LastExecutedCommand.Value.tag) ||
                        defender.ClearPatrolPositionsCallCount != 1 || defender.PatrolPositions.Count != 8 ||
                        defender.PatrolPositions[0].x != 10f || defender.PatrolPositions[7].x != 17f)
                    {
                        throw new InvalidOperationException("Native patrol did not install the bounded nearest route at index zero.");
                    }
                    continue;
                }

                if (defender.LastExecutedCommand.HasValue || defender.ClearPatrolPositionsCallCount != 0 ||
                    defender.PatrolPositions.Count != 1)
                {
                    throw new InvalidOperationException("Native patrol changed an idle defender after reaching its shared dispatch cap.");
                }
                untouchedIdleDefenders++;
            }

            if (nativePatrolDispatches != 1 || untouchedIdleDefenders != 1 || moving.LastExecutedCommand.HasValue ||
                moving.ClearPatrolPositionsCallCount != 0 || moving.PatrolPositions.Count != 1)
            {
                throw new InvalidOperationException("Native patrol exceeded its cap, used an invalid route, or changed a moving defender.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("native patrol mode preserves a queued Move behind DoNothing", () =>
    {
        ExistingControllers.Instance.Reset();
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            UnityEngine.Time.time = 10f;
            var building = new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                CurrentCell = new UnityEngine.Vector2Int(10, 10),
                Position = new UnityEngine.Vector3(10f, 0f, 10f),
                UniqueEntityId = 900
            };
            ExistingControllers.Instance.AiBuildings().Add(building);

            var defender = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                CurrentCell = new UnityEngine.Vector2Int(9, 10),
                UniqueEntityId = 924
            };
            defender.CommandChainForTest.AddLast(new EntityCommand
            {
                command = EntityCommand.Command.Move,
                destination = new UnityEngine.Vector3(20f, 0f, 20f)
            });
            defender.AddPatrolPosition(new UnityEngine.Vector3(-5f, 0f, -5f));
            ExistingControllers.Instance.AiUnits().Add(defender);

            behaviour = new PatrolAndInterceptAiModule().CreateBehaviour(
                new DefaultAiBehaviour { instanceId = 903 },
                [
                    new ContentModuleParameter { name = "patrolMode", value = "Native" },
                    new ContentModuleParameter { name = "patrolRadiusCells", value = "15" },
                    new ContentModuleParameter { name = "maxPatrolUnits", value = "1" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();

            LinkedListNode<EntityCommand>? firstQueuedCommand = defender.CommandChainForTest.First;
            if (defender.LastExecutedCommand.HasValue || defender.ExecuteCommandCallCount != 0 ||
                defender.ClearPatrolPositionsCallCount != 0 || defender.PatrolPositions.Count != 1 ||
                defender.PatrolPositions[0].x != -5f || defender.CommandChainForTest.Count != 1 ||
                firstQueuedCommand == null || firstQueuedCommand.Value.command != EntityCommand.Command.Move ||
                firstQueuedCommand.Value.destination.x != 20f || firstQueuedCommand.Value.destination.z != 20f)
            {
                throw new InvalidOperationException("Native patrol superseded a queued Move behind a DoNothing current command.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("factory-reinforcement AI parameter parser supplies bounded defaults and validates limits", () =>
    {
        if (!FactoryReinforcementParameterParser.TryParse(null, out FactoryReinforcementSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.BaseIndex != -1 ||
            defaults.CheckIntervalSeconds != 10f || defaults.MaxQueuePerFactory != 1 ||
            defaults.MaxStandingArmy != 25 || !defaults.RequireEconomy || defaults.MinGameTimeSeconds != 15f)
        {
            throw new InvalidOperationException("The factory-reinforcement AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaries =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "checkIntervalSeconds", value = "120" },
            new ContentModuleParameter { name = "maxQueuePerFactory", value = "5" },
            new ContentModuleParameter { name = "maxStandingArmy", value = "100" },
            new ContentModuleParameter { name = "requireEconomy", value = "false" },
            new ContentModuleParameter { name = "minGameTimeSeconds", value = "600" }
        ];
        if (!FactoryReinforcementParameterParser.TryParse(boundaries, out FactoryReinforcementSettings maximums,
            out diagnostic) || diagnostic != null || maximums.BaseIndex != 31 ||
            maximums.CheckIntervalSeconds != 120f || maximums.MaxQueuePerFactory != 5 ||
            maximums.MaxStandingArmy != 100 || maximums.RequireEconomy != false || maximums.MinGameTimeSeconds != 600f)
        {
            throw new InvalidOperationException("The factory-reinforcement AI parser rejected valid upper-bound values.");
        }
    }),
    ("factory-reinforcement AI parameter parser rejects invalid values and unknown parameters", () =>
    {
        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "unknownParam", value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "0" }, new ContentModuleParameter { name = "baseIndex", value = "1" }],
            [null!],
            [new ContentModuleParameter { name = "baseIndex", value = "-2" }],
            [new ContentModuleParameter { name = "baseIndex", value = "32" }],
            [new ContentModuleParameter { name = "checkIntervalSeconds", value = "0.5" }],
            [new ContentModuleParameter { name = "checkIntervalSeconds", value = "121" }],
            [new ContentModuleParameter { name = "maxQueuePerFactory", value = "0" }],
            [new ContentModuleParameter { name = "maxQueuePerFactory", value = "6" }],
            [new ContentModuleParameter { name = "maxStandingArmy", value = "0" }],
            [new ContentModuleParameter { name = "maxStandingArmy", value = "101" }],
            [new ContentModuleParameter { name = "requireEconomy", value = "maybe" }],
            [new ContentModuleParameter { name = "minGameTimeSeconds", value = "-1" }],
            [new ContentModuleParameter { name = "minGameTimeSeconds", value = "601" }]
        ];

        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (FactoryReinforcementParameterParser.TryParse(invalidInputs[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("The factory-reinforcement AI parser accepted invalid input " + i + ".");
            }
        }
    }),
    ("flanking-strike AI parameter parser supplies bounded defaults and validates limits", () =>
    {
        if (!FlankingStrikeParameterParser.TryParse(null, out FlankingStrikeSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.BaseIndex != 0 ||
            defaults.CooldownSeconds != 35f || defaults.SquadSize != 3 ||
            defaults.FlankAngleDegrees != 60f || defaults.FlankDistance != 35f ||
            defaults.MinGameTimeSeconds != 30f || defaults.TargetPreference != "Refinery")
        {
            throw new InvalidOperationException("The flanking-strike AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaries =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "300" },
            new ContentModuleParameter { name = "squadSize", value = "12" },
            new ContentModuleParameter { name = "flankAngleDegrees", value = "90" },
            new ContentModuleParameter { name = "flankDistance", value = "120" },
            new ContentModuleParameter { name = "minGameTimeSeconds", value = "600" },
            new ContentModuleParameter { name = "targetPreference", value = "Harvester" }
        ];
        if (!FlankingStrikeParameterParser.TryParse(boundaries, out FlankingStrikeSettings maximums,
            out diagnostic) || diagnostic != null || maximums.BaseIndex != 31 ||
            maximums.CooldownSeconds != 300f || maximums.SquadSize != 12 ||
            maximums.FlankAngleDegrees != 90f || maximums.FlankDistance != 120f ||
            maximums.MinGameTimeSeconds != 600f || maximums.TargetPreference != "Harvester")
        {
            throw new InvalidOperationException("The flanking-strike AI parser rejected valid upper-bound values.");
        }
    }),
    ("flanking-strike AI parameter parser rejects invalid values and unknown parameters", () =>
    {
        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "unknownParam", value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "0" }, new ContentModuleParameter { name = "baseIndex", value = "1" }],
            [null!],
            [new ContentModuleParameter { name = "baseIndex", value = "-1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "32" }],
            [new ContentModuleParameter { name = "cooldownSeconds", value = "4" }],
            [new ContentModuleParameter { name = "cooldownSeconds", value = "301" }],
            [new ContentModuleParameter { name = "squadSize", value = "0" }],
            [new ContentModuleParameter { name = "squadSize", value = "13" }],
            [new ContentModuleParameter { name = "flankAngleDegrees", value = "14" }],
            [new ContentModuleParameter { name = "flankAngleDegrees", value = "91" }],
            [new ContentModuleParameter { name = "flankDistance", value = "9" }],
            [new ContentModuleParameter { name = "flankDistance", value = "121" }],
            [new ContentModuleParameter { name = "minGameTimeSeconds", value = "-1" }],
            [new ContentModuleParameter { name = "minGameTimeSeconds", value = "601" }],
            [new ContentModuleParameter { name = "targetPreference", value = "InvalidPref" }]
        ];

        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (FlankingStrikeParameterParser.TryParse(invalidInputs[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("The flanking-strike AI parser accepted invalid input " + i + ".");
            }
        }
    }),
    ("flanking-strike ignores non-combat AI units even when attack and movement are enabled", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var target = new EntityController
            {
                IsControlledByPlayer = true,
                IsBuilding = true,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(10f, 0f, 0f),
                UniqueEntityId = 731
            };
            var supportA = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = false,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero,
                UniqueEntityId = 732
            };
            var supportB = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = false,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                Position = new UnityEngine.Vector3(1f, 0f, 0f),
                UniqueEntityId = 733
            };
            ExistingControllers.Instance.PlayerBuildingsKnownToAi().Add(target);
            ExistingControllers.Instance.AiUnits().Add(supportA);
            ExistingControllers.Instance.AiUnits().Add(supportB);

            behaviour = new FlankingStrikeAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "baseIndex", value = "0" },
                    new ContentModuleParameter { name = "squadSize", value = "2" },
                    new ContentModuleParameter { name = "minGameTimeSeconds", value = "0" },
                    new ContentModuleParameter { name = "targetPreference", value = "Refinery" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (supportA.ExecuteCommandCallCount != 0 || supportB.ExecuteCommandCallCount != 0)
            {
                throw new InvalidOperationException("Flanking strike dispatched non-combat AI units.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("new AI modules factory-reinforcement and flanking-strike are registered and validate parameters", () =>
    {
        var registry = new AiModuleRegistry();
        registry.Register(new FactoryReinforcementAiModule());
        registry.Register(new FlankingStrikeAiModule());

        if (!registry.TryValidate("sample:factory-reinforcement", null, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Factory reinforcement module failed validation: " + diagnostic);
        }

        if (!registry.TryValidate("sample:flanking-strike", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Flanking strike module failed validation: " + diagnostic);
        }
    }),
    ("focus-fire AI module parses defaults and validates limits", () =>
    {
        if (!FocusFireParameterParser.TryParse(null, out FocusFireSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Focus-fire default parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != -1 || settings.CheckIntervalSeconds != 3f || settings.MinUnitsInSquad != 2 || settings.PriorityMode != "LowestHealth")
        {
            throw new InvalidOperationException("Focus-fire default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "baseIndex", value = "1" },
            new ContentModuleParameter { name = "checkIntervalSeconds", value = "5.0" },
            new ContentModuleParameter { name = "squadRadius", value = "25.0" },
            new ContentModuleParameter { name = "minUnitsInSquad", value = "4" },
            new ContentModuleParameter { name = "priorityMode", value = "HighestThreat" },
            new ContentModuleParameter { name = "targetRole", value = "Sniper" }
        };
        if (!FocusFireParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Focus-fire custom parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != 1 || settings.SquadRadius != 25f || settings.PriorityMode != "HighestThreat")
        {
            throw new InvalidOperationException("Focus-fire parsed custom values incorrect.");
        }

        var invalidParams = new[]
        {
            new ContentModuleParameter { name = "priorityMode", value = "InvalidMode" }
        };
        if (FocusFireParameterParser.TryParse(invalidParams, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Focus-fire should reject invalid priorityMode.");
        }
    }),
    ("focus-fire ignores non-combat AI units even when attack and movement are enabled", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var supportA = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = false,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                Position = UnityEngine.Vector3.zero,
                UniqueEntityId = 701
            };
            var supportB = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = false,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                Position = new UnityEngine.Vector3(1f, 0f, 0f),
                UniqueEntityId = 702
            };
            var playerTarget = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                Position = new UnityEngine.Vector3(2f, 0f, 0f),
                UniqueEntityId = 703
            };
            ExistingControllers.Instance.AiUnits().Add(supportA);
            ExistingControllers.Instance.AiUnits().Add(supportB);
            ExistingControllers.Instance.PlayerEntities().Add(playerTarget);

            behaviour = new FocusFireAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [new ContentModuleParameter { name = "checkIntervalSeconds", value = "1" }]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 1f;
            behaviour.Tick();

            if (supportA.ExecuteCommandCallCount != 0 || supportB.ExecuteCommandCallCount != 0)
            {
                throw new InvalidOperationException("Focus-fire dispatched non-combat AI units.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("focus-fire rejects a non-hostile candidate from the player entity registry", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var aiA = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                Position = UnityEngine.Vector3.zero,
                UniqueEntityId = 711
            };
            var aiB = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                Position = new UnityEngine.Vector3(1f, 0f, 0f),
                UniqueEntityId = 712
            };
            var nonHostileCandidate = new EntityController
            {
                IsControlledByAi = true,
                IsControlledByPlayer = false,
                IsUnit = true,
                IsCombatUnit = true,
                Position = new UnityEngine.Vector3(2f, 0f, 0f),
                UniqueEntityId = 713
            };
            ExistingControllers.Instance.AiUnits().Add(aiA);
            ExistingControllers.Instance.AiUnits().Add(aiB);
            ExistingControllers.Instance.PlayerEntities().Add(nonHostileCandidate);

            behaviour = new FocusFireAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [new ContentModuleParameter { name = "checkIntervalSeconds", value = "1" }]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 1f;
            behaviour.Tick();

            if (aiA.ExecuteCommandCallCount != 0 || aiB.ExecuteCommandCallCount != 0)
            {
                throw new InvalidOperationException("Focus-fire targeted a non-hostile entity.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("construction-expansion AI module parses defaults and validates limits", () =>
    {
        if (!ConstructionExpansionParameterParser.TryParse(null, out ConstructionExpansionSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Construction-expansion default parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != 0 || settings.CheckIntervalSeconds != 12f || settings.DesiredCount != 2 || settings.BuildingId != "PCXTurret")
        {
            throw new InvalidOperationException("Construction-expansion default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "baseIndex", value = "2" },
            new ContentModuleParameter { name = "checkIntervalSeconds", value = "20.0" },
            new ContentModuleParameter { name = "buildingId", value = "PCXRefinery" },
            new ContentModuleParameter { name = "desiredCount", value = "3" },
            new ContentModuleParameter { name = "queuePriority", value = "15" },
            new ContentModuleParameter { name = "minSurplusCrystals", value = "300" }
        };
        if (!ConstructionExpansionParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Construction-expansion custom parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != 2 || settings.BuildingId != "PCXRefinery" || settings.MinSurplusCrystals != 300)
        {
            throw new InvalidOperationException("Construction-expansion parsed values incorrect.");
        }

        var invalidParams = new[]
        {
            new ContentModuleParameter { name = "desiredCount", value = "0" }
        };
        if (ConstructionExpansionParameterParser.TryParse(invalidParams, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Construction-expansion should reject desiredCount < 1.");
        }
    }),
    ("tactical-regroup AI module parses defaults and validates limits", () =>
    {
        if (!TacticalRegroupParameterParser.TryParse(null, out TacticalRegroupSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-regroup default parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != -1 || settings.CheckIntervalSeconds != 4f || settings.HealthRetreatThreshold != 0.30f || settings.RegroupMinSquadSize != 3)
        {
            throw new InvalidOperationException("Tactical-regroup default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "baseIndex", value = "1" },
            new ContentModuleParameter { name = "healthRetreatThreshold", value = "0.25" },
            new ContentModuleParameter { name = "regroupMinSquadSize", value = "5" },
            new ContentModuleParameter { name = "regroupDistanceToBase", value = "30.0" }
        };
        if (!TacticalRegroupParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-regroup custom parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != 1 || settings.HealthRetreatThreshold != 0.25f || settings.RegroupMinSquadSize != 5)
        {
            throw new InvalidOperationException("Tactical-regroup parsed values incorrect.");
        }

        var invalidParams = new[]
        {
            new ContentModuleParameter { name = "healthRetreatThreshold", value = "0.80" }
        };
        if (TacticalRegroupParameterParser.TryParse(invalidParams, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Tactical-regroup should reject retreat threshold > 0.60.");
        }
    }),
    ("focus-fire rejects an unknown targetRole when target-role priority is selected", () =>
    {
        var parameters = new[]
        {
            new ContentModuleParameter { name = "priorityMode", value = "TargetRole" },
            new ContentModuleParameter { name = "targetRole", value = "NotAUnitRole" }
        };
        if (FocusFireParameterParser.TryParse(parameters, out _, out string diagnostic) ||
            String.IsNullOrWhiteSpace(diagnostic))
        {
            throw new InvalidOperationException("Focus-fire should reject an unknown targetRole.");
        }
    }),
    ("tactical-regroup does not count another base as regroup reinforcement", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var wounded = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 20f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(20f, 0f, 0f)
            };
            ExistingControllers.Instance.AiUnits().Add(wounded);
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            });
            behaviour = new TacticalRegroupAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "baseIndex", value = "0" },
                    new ContentModuleParameter { name = "checkIntervalSeconds", value = "4" },
                    new ContentModuleParameter { name = "regroupMinSquadSize", value = "2" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 4f;
            behaviour.Tick();
            if (!wounded.LastExecutedCommand.HasValue ||
                wounded.LastExecutedCommand.Value.command != EntityCommand.Command.Move)
            {
                throw new InvalidOperationException("The wounded unit did not receive its initial retreat order.");
            }

            var otherBaseReinforcements = new List<EntityController>();
            for (int i = 0; i < 2; i++)
            {
                var reinforcement = new EntityController
                {
                    IsControlledByAi = true,
                    IsUnit = true,
                    IsCombatUnit = true,
                    CanAttackAndIsAttackEnabled = true,
                    CanMoveAndMovementIsEnabled = true,
                    aiBaseIndex = 1,
                    Position = wounded.Position
                };
                otherBaseReinforcements.Add(reinforcement);
                ExistingControllers.Instance.AiUnits().Add(reinforcement);
            }

            int initialOrderCount = wounded.ExecuteCommandCallCount;
            UnityEngine.Time.time = 8f;
            behaviour.Tick();
            foreach (EntityController reinforcement in otherBaseReinforcements)
            {
                ExistingControllers.Instance.AiUnits().Remove(reinforcement);
            }
            UnityEngine.Time.time = 12f;
            behaviour.Tick();
            if (wounded.ExecuteCommandCallCount != initialOrderCount)
            {
                throw new InvalidOperationException("Units from another AI base incorrectly released the retreat state.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("tactical-regroup cancels its retreat command after reinforcement arrives", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var wounded = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 20f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(20f, 0f, 0f)
            };
            ExistingControllers.Instance.AiUnits().Add(wounded);
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            });

            behaviour = new TacticalRegroupAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "baseIndex", value = "0" },
                    new ContentModuleParameter { name = "checkIntervalSeconds", value = "4" },
                    new ContentModuleParameter { name = "regroupMinSquadSize", value = "2" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 4f;
            behaviour.Tick();
            if (!wounded.LastExecutedCommand.HasValue ||
                wounded.LastExecutedCommand.Value.command != EntityCommand.Command.Move)
            {
                throw new InvalidOperationException("The wounded unit did not receive its initial retreat order.");
            }

            for (int i = 0; i < 2; i++)
            {
                ExistingControllers.Instance.AiUnits().Add(new EntityController
                {
                    IsControlledByAi = true,
                    IsUnit = true,
                    IsCombatUnit = true,
                    CanAttackAndIsAttackEnabled = true,
                    CanMoveAndMovementIsEnabled = true,
                    aiBaseIndex = 0,
                    Position = wounded.Position
                });
            }

            UnityEngine.Time.time = 8f;
            behaviour.Tick();
            if (wounded.ExecuteCommandCallCount != 2 ||
                wounded.LastExecutedCommand?.command != EntityCommand.Command.Stop ||
                wounded.LastCommandProcessingType != EntityController.CommandProcessingType.ExecuteAndCancelCurrentCommand)
            {
                throw new InvalidOperationException("Reinforcement should cancel the active retreat command.");
            }

            int stopOrderCount = wounded.ExecuteCommandCallCount;
            UnityEngine.Time.time = 12f;
            behaviour.Tick();
            if (wounded.ExecuteCommandCallCount != stopOrderCount)
            {
                throw new InvalidOperationException("The retreat command was reissued while the unit remained reinforced.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("tactical-regroup ignores immobile or non-unit reinforcement candidates", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var wounded = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 20f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(20f, 0f, 0f)
            };
            ExistingControllers.Instance.AiUnits().Add(wounded);
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            });
            behaviour = new TacticalRegroupAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "baseIndex", value = "0" },
                    new ContentModuleParameter { name = "checkIntervalSeconds", value = "4" },
                    new ContentModuleParameter { name = "regroupMinSquadSize", value = "2" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 4f;
            behaviour.Tick();
            if (wounded.LastExecutedCommand?.command != EntityCommand.Command.Move)
            {
                throw new InvalidOperationException("The wounded unit did not receive its initial retreat order.");
            }

            ExistingControllers.Instance.AiUnits().Add(new EntityController
            {
                IsControlledByAi = true,
                IsUnit = false,
                IsBuilding = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                Position = wounded.Position
            });
            ExistingControllers.Instance.AiUnits().Add(new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = false,
                aiBaseIndex = 0,
                Position = wounded.Position
            });

            UnityEngine.Time.time = 8f;
            behaviour.Tick();
            if (wounded.ExecuteCommandCallCount != 1 ||
                wounded.LastExecutedCommand?.command != EntityCommand.Command.Move)
            {
                throw new InvalidOperationException("Non-unit or immobile entities incorrectly satisfied the reinforcement threshold.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("tactical-regroup preserves another module's active command when regrouping", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var wounded = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 20f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(20f, 0f, 0f)
            };
            ExistingControllers.Instance.AiUnits().Add(wounded);
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            });

            behaviour = new TacticalRegroupAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "baseIndex", value = "0" },
                    new ContentModuleParameter { name = "checkIntervalSeconds", value = "4" },
                    new ContentModuleParameter { name = "regroupMinSquadSize", value = "2" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 4f;
            behaviour.Tick();
            if (wounded.LastExecutedCommand?.command != EntityCommand.Command.Move)
            {
                throw new InvalidOperationException("The wounded unit did not receive its initial retreat order.");
            }

            EntityCommand otherModuleCommand = EntityCommand.Attack(wounded);
            otherModuleCommand.tag = "roguecommand.expansionloader.focus-fire.test";
            wounded.ExecuteCommand(otherModuleCommand, EntityController.CommandProcessingType.ExecuteAndCancelCurrentCommand);
            int commandCountBeforeRegroup = wounded.ExecuteCommandCallCount;
            for (int i = 0; i < 2; i++)
            {
                ExistingControllers.Instance.AiUnits().Add(new EntityController
                {
                    IsControlledByAi = true,
                    IsUnit = true,
                    IsCombatUnit = true,
                    CanAttackAndIsAttackEnabled = true,
                    CanMoveAndMovementIsEnabled = true,
                    aiBaseIndex = 0,
                    Position = wounded.Position
                });
            }

            UnityEngine.Time.time = 8f;
            behaviour.Tick();
            if (wounded.ExecuteCommandCallCount != commandCountBeforeRegroup ||
                wounded.LastExecutedCommand?.command != EntityCommand.Command.Attack ||
                !wounded.CurrentCommandIsTaggedWith("roguecommand.expansionloader.focus-fire.test"))
            {
                throw new InvalidOperationException("Regroup cancelled a command owned by another module.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("tactical AI modules focus-fire, construction-expansion, and tactical-regroup are registered", () =>
    {
        var registry = new AiModuleRegistry();
        registry.Register(new FocusFireAiModule());
        registry.Register(new ConstructionExpansionAiModule());
        registry.Register(new TacticalRegroupAiModule());

        if (!registry.TryValidate("sample:focus-fire", null, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Focus-fire module failed registry validation: " + diagnostic);
        }
        if (!registry.TryValidate("sample:construction-expansion", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Construction-expansion module failed registry validation: " + diagnostic);
        }
        if (!registry.TryValidate("sample:tactical-regroup", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-regroup module failed registry validation: " + diagnostic);
        }
    }),
    ("squad-formation AI module parses defaults and validates limits", () =>
    {
        if (!SquadFormationParameterParser.TryParse(null, out SquadFormationSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Squad-formation default parse failed: " + diagnostic);
        }
        if (settings.AnchorRole != "Tank" || settings.MaxFollowersPerCaptain != 4 || settings.FormationRadiusCells != 10f || settings.CooldownSeconds != 5f || settings.BaseIndex != -1)
        {
            throw new InvalidOperationException("Squad-formation default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "anchorRole", value = "Mech" },
            new ContentModuleParameter { name = "maxFollowersPerCaptain", value = "6" },
            new ContentModuleParameter { name = "formationRadiusCells", value = "20" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "2.5" },
            new ContentModuleParameter { name = "baseIndex", value = "1" }
        };
        if (!SquadFormationParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Squad-formation custom parse failed: " + diagnostic);
        }
        if (settings.AnchorRole != "Mech" || settings.MaxFollowersPerCaptain != 6 || settings.FormationRadiusCells != 20f || settings.BaseIndex != 1)
        {
            throw new InvalidOperationException("Squad-formation parsed values incorrect.");
        }

        var invalidParams = new[]
        {
            new ContentModuleParameter { name = "maxFollowersPerCaptain", value = "25" }
        };
        if (SquadFormationParameterParser.TryParse(invalidParams, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Squad-formation should reject maxFollowersPerCaptain > 16.");
        }

        var invalidRoleParams = new[]
        {
            new ContentModuleParameter { name = "anchorRole", value = "InvalidUnitRole" }
        };
        if (SquadFormationParameterParser.TryParse(invalidRoleParams, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Squad-formation should reject invalid anchorRole.");
        }
    }),
    ("squad-formation AI module binds followers to anchor captain and releases on destroy", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        EntityBalancingStore.Reset();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            EntityBalancingStore.RolesOf["TankUnit"] = UnitRole.Tank;
            EntityBalancingStore.RolesOf["TrooperUnit"] = UnitRole.Unit;

            var captain = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "TankUnit",
                UniqueEntityId = 101,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            };
            var follower = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "TrooperUnit",
                UniqueEntityId = 102,
                aiBaseIndex = 0,
                Position = new UnityEngine.Vector3(5f, 0f, 0f)
            };

            ExistingControllers.Instance.AiUnits().Add(captain);
            ExistingControllers.Instance.AiUnits().Add(follower);

            behaviour = new SquadFormationAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "anchorRole", value = "Tank" },
                    new ContentModuleParameter { name = "baseIndex", value = "0" },
                    new ContentModuleParameter { name = "cooldownSeconds", value = "2" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 2f;
            behaviour.Tick();

            if (follower.CaptainOfThisEntity != captain)
            {
                throw new InvalidOperationException("Follower was not bound to captain.");
            }

            behaviour.OnDestroyed();
            if (follower.CaptainOfThisEntity != null)
            {
                throw new InvalidOperationException("Follower captain was not released upon OnDestroyed().");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
            EntityBalancingStore.Reset();
        }
    }),
    ("squad-formation preserves native captain links when the module is destroyed", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        EntityBalancingStore.Reset();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            EntityBalancingStore.RolesOf["ModuleCaptain"] = UnitRole.Tank;
            EntityBalancingStore.RolesOf["NativeCaptain"] = UnitRole.Unit;
            EntityBalancingStore.RolesOf["ModuleFollower"] = UnitRole.Unit;
            EntityBalancingStore.RolesOf["NativeFollower"] = UnitRole.Unit;

            var moduleCaptain = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "ModuleCaptain",
                UniqueEntityId = 121,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            };
            var moduleFollower = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "ModuleFollower",
                UniqueEntityId = 122,
                aiBaseIndex = 0,
                Position = new UnityEngine.Vector3(2f, 0f, 0f)
            };
            var nativeCaptain = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "NativeCaptain",
                UniqueEntityId = 123,
                aiBaseIndex = 0,
                Position = new UnityEngine.Vector3(100f, 0f, 0f)
            };
            var nativeFollower = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "NativeFollower",
                UniqueEntityId = 124,
                aiBaseIndex = 0,
                Position = new UnityEngine.Vector3(100f, 0f, 1f),
                CaptainOfThisEntity = nativeCaptain
            };

            ExistingControllers.Instance.AiUnits().Add(moduleCaptain);
            ExistingControllers.Instance.AiUnits().Add(moduleFollower);
            ExistingControllers.Instance.AiUnits().Add(nativeCaptain);
            ExistingControllers.Instance.AiUnits().Add(nativeFollower);

            behaviour = new SquadFormationAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "anchorRole", value = "Tank" },
                    new ContentModuleParameter { name = "baseIndex", value = "0" },
                    new ContentModuleParameter { name = "cooldownSeconds", value = "1" },
                    new ContentModuleParameter { name = "formationRadiusCells", value = "10" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 1f;
            behaviour.Tick();

            if (moduleFollower.CaptainOfThisEntity != moduleCaptain ||
                nativeFollower.CaptainOfThisEntity != nativeCaptain)
            {
                throw new InvalidOperationException("Squad formation setup did not establish both captain relationships.");
            }

            behaviour.OnDestroyed();
            if (moduleFollower.CaptainOfThisEntity != null || nativeFollower.CaptainOfThisEntity != nativeCaptain)
            {
                throw new InvalidOperationException("Squad formation cleanup removed a native captain relationship.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
            EntityBalancingStore.Reset();
        }
    }),
    ("tactical-skill-activation AI module parses defaults and validates limits", () =>
    {
        if (!TacticalSkillActivationParameterParser.TryParse(null, out TacticalSkillActivationSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-skill-activation default parse failed: " + diagnostic);
        }
        if (settings.EnemyTriggerRadiusCells != 10f || settings.MinManaRatio != 0.5f || settings.CooldownSeconds != 4f || settings.BaseIndex != -1)
        {
            throw new InvalidOperationException("Tactical-skill-activation default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "enemyTriggerRadiusCells", value = "18" },
            new ContentModuleParameter { name = "minManaRatio", value = "0.3" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "1.5" },
            new ContentModuleParameter { name = "baseIndex", value = "2" }
        };
        if (!TacticalSkillActivationParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-skill-activation custom parse failed: " + diagnostic);
        }
        if (settings.EnemyTriggerRadiusCells != 18f || settings.MinManaRatio != 0.3f || settings.BaseIndex != 2)
        {
            throw new InvalidOperationException("Tactical-skill-activation parsed values incorrect.");
        }

        var invalidRadius = new[]
        {
            new ContentModuleParameter { name = "enemyTriggerRadiusCells", value = "100" }
        };
        if (TacticalSkillActivationParameterParser.TryParse(invalidRadius, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Tactical-skill-activation should reject triggerRadius > 64.");
        }

        var invalidMana = new[]
        {
            new ContentModuleParameter { name = "minManaRatio", value = "1.5" }
        };
        if (TacticalSkillActivationParameterParser.TryParse(invalidMana, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Tactical-skill-activation should reject minManaRatio > 1.0.");
        }
    }),
    ("tactical-skill-activation LowHealth trigger waits for low health and needs no nearby enemy for self skills", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        EntityBalancingStore.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var aiCaster = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "MedicUnit",
                UniqueEntityId = 401,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                CurrentMana = 100f,
                SkillInfo = new SkillInfo { targetOrigin = TargetOrigin.Self }
            };
            var playerEnemy = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                UniqueEntityId = 402,
                Position = new UnityEngine.Vector3(3f, 0f, 0f)
            };
            ExistingControllers.Instance.AiUnits().Add(aiCaster);
            ExistingControllers.Instance.PlayerEntities().Add(playerEnemy);

            var profile = new UnitRuntimeMechanicsProfile
            {
                EntityId = "MedicUnit",
                AiActivationTrigger = "LowHealth"
            };
            RuntimeUnitMechanicsDirector.RegisterProfile("MedicUnit", profile);

            behaviour = new TacticalSkillActivationAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "enemyTriggerRadiusCells", value = "10" },
                    new ContentModuleParameter { name = "cooldownSeconds", value = "1" },
                    new ContentModuleParameter { name = "baseIndex", value = "0" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 1f;
            behaviour.Tick();

            if (aiCaster.LastExecutedCommand.HasValue)
            {
                throw new InvalidOperationException("LowHealth skill fired while caster was at full health.");
            }

            ExistingControllers.Instance.PlayerEntities().Clear();
            aiCaster.CurrentHealth = 40f;
            UnityEngine.Time.time = 2f;
            behaviour.Tick();

            if (!aiCaster.LastExecutedCommand.HasValue ||
                aiCaster.LastExecutedCommand.Value.command != EntityCommand.Command.ActiveSkillWithoutTarget)
            {
                throw new InvalidOperationException("LowHealth self skill did not fire when wounded without a nearby enemy.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
            EntityBalancingStore.Reset();
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("tactical-skill-activation ignores a non-hostile entry in the player registry", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        EntityBalancingStore.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var caster = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                UniqueEntityId = 451,
                EntityId = "ai:skill-caster",
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero,
                CurrentMana = 100f,
                SkillInfo = new SkillInfo { targetOrigin = TargetOrigin.ChosenEntity }
            };
            var nonHostile = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                UniqueEntityId = 452,
                EntityId = "ai:non-hostile",
                Position = new UnityEngine.Vector3(2f, 0f, 0f)
            };
            ExistingControllers.Instance.AiUnits().Add(caster);
            ExistingControllers.Instance.PlayerEntities().Add(nonHostile);

            behaviour = new TacticalSkillActivationAiModule().CreateBehaviour(
                new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "enemyTriggerRadiusCells", value = "10" },
                    new ContentModuleParameter { name = "cooldownSeconds", value = "1" },
                    new ContentModuleParameter { name = "baseIndex", value = "0" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 1f;
            behaviour.Tick();

            if (caster.ExecuteCommandCallCount != 0)
            {
                throw new InvalidOperationException("Tactical skill activation targeted a non-hostile entity.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
            EntityBalancingStore.Reset();
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("tactical-skill-activation AI module dispatches active skill when enemy is detected in range", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        EntityBalancingStore.Reset();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var aiCaster = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "MageUnit",
                UniqueEntityId = 201,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero,
                CurrentMana = 100f,
                SkillInfo = new SkillInfo { targetOrigin = TargetOrigin.ChosenEntity }
            };
            var playerEnemy = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                UniqueEntityId = 301,
                Position = new UnityEngine.Vector3(8f, 0f, 0f)
            };

            ExistingControllers.Instance.AiUnits().Add(aiCaster);
            ExistingControllers.Instance.PlayerEntities().Add(playerEnemy);

            behaviour = new TacticalSkillActivationAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "enemyTriggerRadiusCells", value = "12" },
                    new ContentModuleParameter { name = "cooldownSeconds", value = "1" },
                    new ContentModuleParameter { name = "baseIndex", value = "0" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 1f;
            behaviour.Tick();

            if (!aiCaster.LastExecutedCommand.HasValue ||
                aiCaster.LastExecutedCommand.Value.command != EntityCommand.Command.ActiveSkillOnTarget)
            {
                throw new InvalidOperationException("Tactical-skill-activation did not execute ActiveSkillOnTarget command.");
            }
            if (aiCaster.LastCommandProcessingType != EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand)
            {
                throw new InvalidOperationException("Tactical-skill-activation did not use ExecuteBeforeCurrentCommand.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
            EntityBalancingStore.Reset();
        }
    }),
    ("wave-escalation AI module parses defaults and validates limits", () =>
    {
        if (!WaveEscalationParameterParser.TryParse(null, out WaveEscalationSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Wave-escalation default parse failed: " + diagnostic);
        }
        if (settings.WaveIntervalSeconds != 60f || settings.MaxWaves != 10 || settings.BaseIndex != -1)
        {
            throw new InvalidOperationException("Wave-escalation default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "waveIntervalSeconds", value = "30" },
            new ContentModuleParameter { name = "maxWaves", value = "5" },
            new ContentModuleParameter { name = "baseIndex", value = "0" }
        };
        if (!WaveEscalationParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Wave-escalation custom parse failed: " + diagnostic);
        }
        if (settings.WaveIntervalSeconds != 30f || settings.MaxWaves != 5 || settings.BaseIndex != 0)
        {
            throw new InvalidOperationException("Wave-escalation parsed values incorrect.");
        }

        var invalidInterval = new[]
        {
            new ContentModuleParameter { name = "waveIntervalSeconds", value = "3" }
        };
        if (WaveEscalationParameterParser.TryParse(invalidInterval, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Wave-escalation should reject waveIntervalSeconds < 5.");
        }

        var invalidMaxWaves = new[]
        {
            new ContentModuleParameter { name = "maxWaves", value = "150" }
        };
        if (WaveEscalationParameterParser.TryParse(invalidMaxWaves, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Wave-escalation should reject maxWaves > 100.");
        }
    }),
    ("threat-prioritization AI module parses defaults and validates limits", () =>
    {
        if (!ThreatPrioritizationParameterParser.TryParse(null, out ThreatPrioritizationSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Threat-prioritization default parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != -1 || settings.CheckIntervalSeconds != 3f || settings.PriorityMode != "HighestThreat" || settings.EngagementRadius != 35f || settings.RetargetThreshold != 1.25f || settings.MaxRetargetsPerTick != 8)
        {
            throw new InvalidOperationException("Threat-prioritization default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "baseIndex", value = "2" },
            new ContentModuleParameter { name = "checkIntervalSeconds", value = "5" },
            new ContentModuleParameter { name = "priorityMode", value = "LowestHealth" }
        };
        if (!ThreatPrioritizationParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Threat-prioritization custom parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != 2 || settings.CheckIntervalSeconds != 5f || settings.PriorityMode != "LowestHealth")
        {
            throw new InvalidOperationException("Threat-prioritization parsed values incorrect.");
        }

        var invalidInterval = new[]
        {
            new ContentModuleParameter { name = "checkIntervalSeconds", value = "0.1" }
        };
        if (ThreatPrioritizationParameterParser.TryParse(invalidInterval, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject checkIntervalSeconds < 0.5.");
        }

        var invalidMode = new[]
        {
            new ContentModuleParameter { name = "priorityMode", value = "Invalid" }
        };
        if (ThreatPrioritizationParameterParser.TryParse(invalidMode, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject invalid priorityMode.");
        }

        var invalidRadius = new[]
        {
            new ContentModuleParameter { name = "engagementRadius", value = "2" }
        };
        if (ThreatPrioritizationParameterParser.TryParse(invalidRadius, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject engagementRadius < 5.");
        }

        var invalidDuplicate = new[]
        {
            new ContentModuleParameter { name = "priorityMode", value = "HighestThreat" },
            new ContentModuleParameter { name = "priorityMode", value = "LowestHealth" }
        };
        if (ThreatPrioritizationParameterParser.TryParse(invalidDuplicate, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject duplicate parameters.");
        }

        var dpsParams = new[]
        {
            new ContentModuleParameter { name = "priorityMode", value = "HighestDps" },
            new ContentModuleParameter { name = "retargetThreshold", value = "1.5" },
            new ContentModuleParameter { name = "maxRetargetsPerTick", value = "16" },
            new ContentModuleParameter { name = "engagementRadius", value = "50" }
        };
        if (!ThreatPrioritizationParameterParser.TryParse(dpsParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Threat-prioritization custom HighestDps parse failed: " + diagnostic);
        }
        if (settings.PriorityMode != "HighestDps" || settings.RetargetThreshold != 1.5f || settings.MaxRetargetsPerTick != 16 || settings.EngagementRadius != 50f)
        {
            throw new InvalidOperationException("Threat-prioritization HighestDps parsed values incorrect.");
        }

        var invalidIntervalUpper = new[] { new ContentModuleParameter { name = "checkIntervalSeconds", value = "35" } };
        if (ThreatPrioritizationParameterParser.TryParse(invalidIntervalUpper, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject checkIntervalSeconds > 30.");
        }

        var invalidRadiusUpper = new[] { new ContentModuleParameter { name = "engagementRadius", value = "150" } };
        if (ThreatPrioritizationParameterParser.TryParse(invalidRadiusUpper, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject engagementRadius > 120.");
        }

        var invalidThresholdLower = new[] { new ContentModuleParameter { name = "retargetThreshold", value = "0.8" } };
        if (ThreatPrioritizationParameterParser.TryParse(invalidThresholdLower, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject retargetThreshold < 1.0.");
        }

        var invalidThresholdUpper = new[] { new ContentModuleParameter { name = "retargetThreshold", value = "3.5" } };
        if (ThreatPrioritizationParameterParser.TryParse(invalidThresholdUpper, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject retargetThreshold > 3.0.");
        }

        var invalidBudgetLower = new[] { new ContentModuleParameter { name = "maxRetargetsPerTick", value = "0" } };
        if (ThreatPrioritizationParameterParser.TryParse(invalidBudgetLower, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject maxRetargetsPerTick < 1.");
        }

        var invalidBudgetUpper = new[] { new ContentModuleParameter { name = "maxRetargetsPerTick", value = "40" } };
        if (ThreatPrioritizationParameterParser.TryParse(invalidBudgetUpper, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject maxRetargetsPerTick > 32.");
        }

        var invalidBaseLower = new[] { new ContentModuleParameter { name = "baseIndex", value = "-2" } };
        if (ThreatPrioritizationParameterParser.TryParse(invalidBaseLower, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject baseIndex < -1.");
        }

        var invalidBaseUpper = new[] { new ContentModuleParameter { name = "baseIndex", value = "35" } };
        if (ThreatPrioritizationParameterParser.TryParse(invalidBaseUpper, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject baseIndex > 31.");
        }

        var invalidUnknown = new[] { new ContentModuleParameter { name = "unknownParam", value = "foo" } };
        if (ThreatPrioritizationParameterParser.TryParse(invalidUnknown, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Threat-prioritization should reject unknown parameters.");
        }
    }),
    ("threat-prioritization AI module prioritizes high-threat and finisher targets dynamically", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            // Scenario 1: HighestThreat mode prioritizes lethal sniper over chaff
            var aiAttacker = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                UniqueEntityId = 501,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            };
            var playerChaff = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 601,
                Position = new UnityEngine.Vector3(5f, 0f, 0f),
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Damage1 = 2f,
                Attack1Cooldown = 2f
            };
            var playerSniper = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 602,
                Position = new UnityEngine.Vector3(15f, 0f, 0f),
                CurrentHealth = 60f,
                MaxHealth = 60f,
                Damage1 = 45f,
                Attack1Cooldown = 1f,
                Role = UnitRole.Sniper
            };

            ExistingControllers.Instance.AiUnits().Add(aiAttacker);
            ExistingControllers.Instance.PlayerEntities().Add(playerChaff);
            ExistingControllers.Instance.PlayerEntities().Add(playerSniper);

            var module = new ThreatPrioritizationAiModule();
            behaviour = module.CreateBehaviour(new DefaultAiBehaviour(),
            [
                new ContentModuleParameter { name = "priorityMode", value = "HighestThreat" },
                new ContentModuleParameter { name = "checkIntervalSeconds", value = "1" },
                new ContentModuleParameter { name = "engagementRadius", value = "40" }
            ]);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (aiAttacker.ExecuteCommandCallCount == 0 || !aiAttacker.LastExecutedCommand.HasValue)
            {
                throw new InvalidOperationException("AI attacker was not commanded to attack prioritized threat.");
            }
            if (aiAttacker.CurrentAttackTarget != playerSniper)
            {
                throw new InvalidOperationException("AI attacker did not prioritize player sniper over chaff in HighestThreat mode.");
            }

            var runtime = (ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour)behaviour;
            if (runtime.LastProcessedAiUnitCount != 1 || runtime.LastRetargetedCount != 1)
            {
                throw new InvalidOperationException("Runtime behaviour metrics mismatch after HighestThreat tick.");
            }

            // Scenario 2: LowestHealth finisher mode prioritizes low health finisher
            var playerWounded = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 603,
                Position = new UnityEngine.Vector3(8f, 0f, 0f),
                CurrentHealth = 10f,
                MaxHealth = 100f,
                Damage1 = 5f,
                Attack1Cooldown = 1f
            };
            ExistingControllers.Instance.PlayerEntities().Add(playerWounded);

            var finisherModule = new ThreatPrioritizationAiModule();
            var finisherBehaviour = finisherModule.CreateBehaviour(new DefaultAiBehaviour(),
            [
                new ContentModuleParameter { name = "priorityMode", value = "LowestHealth" },
                new ContentModuleParameter { name = "checkIntervalSeconds", value = "1" },
                new ContentModuleParameter { name = "engagementRadius", value = "40" }
            ]);
            finisherBehaviour.OnInitialized();
            finisherBehaviour.Tick();

            if (aiAttacker.CurrentAttackTarget != playerWounded)
            {
                throw new InvalidOperationException("AI attacker did not prioritize wounded enemy in LowestHealth mode.");
            }

            // Scenario 3: BaseIndex filtering
            var aiOtherBase = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 502,
                aiBaseIndex = 2,
                Position = UnityEngine.Vector3.zero
            };
            ExistingControllers.Instance.AiUnits().Add(aiOtherBase);

            var baseSpecificModule = new ThreatPrioritizationAiModule();
            var baseSpecificBehaviour = baseSpecificModule.CreateBehaviour(new DefaultAiBehaviour(),
            [
                new ContentModuleParameter { name = "baseIndex", value = "0" },
                new ContentModuleParameter { name = "checkIntervalSeconds", value = "1" }
            ]);
            baseSpecificBehaviour.OnInitialized();
            baseSpecificBehaviour.Tick();

            if (aiOtherBase.ExecuteCommandCallCount > 0)
            {
                throw new InvalidOperationException("Unit belonging to base 2 was retargeted by base 0 module.");
            }

            // Scenario 4: HighestDps mode prioritizes lethal glass cannon over bullet sponge
            ExistingControllers.Instance.Reset();
            var aiDpsAttacker = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                UniqueEntityId = 503,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            };
            ExistingControllers.Instance.AiUnits().Add(aiDpsAttacker);

            var playerGlassCannon = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 701,
                Position = new UnityEngine.Vector3(10f, 0f, 0f),
                CurrentHealth = 40f,
                MaxHealth = 40f,
                Damage1 = 90f,
                Attack1Cooldown = 1f
            };
            var playerBulletSponge = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 702,
                Position = new UnityEngine.Vector3(6f, 0f, 0f),
                CurrentHealth = 800f,
                MaxHealth = 800f,
                Damage1 = 4f,
                Attack1Cooldown = 2f
            };
            ExistingControllers.Instance.PlayerEntities().Add(playerGlassCannon);
            ExistingControllers.Instance.PlayerEntities().Add(playerBulletSponge);

            var dpsModule = new ThreatPrioritizationAiModule();
            var dpsBehaviour = dpsModule.CreateBehaviour(new DefaultAiBehaviour(),
            [
                new ContentModuleParameter { name = "priorityMode", value = "HighestDps" },
                new ContentModuleParameter { name = "checkIntervalSeconds", value = "1" },
                new ContentModuleParameter { name = "engagementRadius", value = "40" }
            ]);
            dpsBehaviour.OnInitialized();
            dpsBehaviour.Tick();

            if (aiDpsAttacker.CurrentAttackTarget != playerGlassCannon)
            {
                throw new InvalidOperationException("AI attacker did not prioritize high-DPS glass cannon in HighestDps mode.");
            }

            // Scenario 5: Hysteresis prevents target flapping when candidate does not beat current by threshold
            var playerMarginalThreat = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 703,
                Position = new UnityEngine.Vector3(10f, 0f, 0f),
                CurrentHealth = 40f,
                MaxHealth = 40f,
                Damage1 = 95f,
                Attack1Cooldown = 1f
            };
            ExistingControllers.Instance.PlayerEntities().Add(playerMarginalThreat);

            int commandsBeforeHysteresis = aiDpsAttacker.ExecuteCommandCallCount;
            UnityEngine.Time.time += 2f;
            dpsBehaviour.Tick();

            if (aiDpsAttacker.CurrentAttackTarget != playerGlassCannon || aiDpsAttacker.ExecuteCommandCallCount != commandsBeforeHysteresis)
            {
                throw new InvalidOperationException("Hysteresis failed: target flapped when new threat was within retargetThreshold margin.");
            }

            // When an overwhelming threat appears (> 1.25x), retargeting triggers
            var playerExtremeThreat = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 704,
                Position = new UnityEngine.Vector3(10f, 0f, 0f),
                CurrentHealth = 40f,
                MaxHealth = 40f,
                Damage1 = 200f,
                Attack1Cooldown = 1f
            };
            ExistingControllers.Instance.PlayerEntities().Add(playerExtremeThreat);

            UnityEngine.Time.time += 2f;
            dpsBehaviour.Tick();

            if (aiDpsAttacker.CurrentAttackTarget != playerExtremeThreat)
            {
                throw new InvalidOperationException("Retargeting failed to switch when new threat exceeded retargetThreshold.");
            }

            // Scenario 6: maxRetargetsPerTick budget caps commands per tick
            ExistingControllers.Instance.Reset();
            var ai1 = new EntityController { IsControlledByAi = true, IsUnit = true, IsCombatUnit = true, CanAttackAndIsAttackEnabled = true, UniqueEntityId = 801, Position = UnityEngine.Vector3.zero };
            var ai2 = new EntityController { IsControlledByAi = true, IsUnit = true, IsCombatUnit = true, CanAttackAndIsAttackEnabled = true, UniqueEntityId = 802, Position = UnityEngine.Vector3.zero };
            var ai3 = new EntityController { IsControlledByAi = true, IsUnit = true, IsCombatUnit = true, CanAttackAndIsAttackEnabled = true, UniqueEntityId = 803, Position = UnityEngine.Vector3.zero };
            ExistingControllers.Instance.AiUnits().Add(ai1);
            ExistingControllers.Instance.AiUnits().Add(ai2);
            ExistingControllers.Instance.AiUnits().Add(ai3);
            ExistingControllers.Instance.PlayerEntities().Add(playerExtremeThreat);

            var budgetModule = new ThreatPrioritizationAiModule();
            var budgetBehaviour = budgetModule.CreateBehaviour(new DefaultAiBehaviour(),
            [
                new ContentModuleParameter { name = "maxRetargetsPerTick", value = "2" },
                new ContentModuleParameter { name = "checkIntervalSeconds", value = "1" }
            ]);
            budgetBehaviour.OnInitialized();
            budgetBehaviour.Tick();

            var budgetRuntime = (ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour)budgetBehaviour;
            if (budgetRuntime.LastRetargetedCount != 2)
            {
                throw new InvalidOperationException("maxRetargetsPerTick budget was not respected; expected 2 retargets but got " + budgetRuntime.LastRetargetedCount);
            }

            // Scenario 7: Out-of-range candidate is ignored
            ExistingControllers.Instance.Reset();
            var aiRanger = new EntityController { IsControlledByAi = true, IsUnit = true, IsCombatUnit = true, CanAttackAndIsAttackEnabled = true, UniqueEntityId = 901, Position = UnityEngine.Vector3.zero };
            ExistingControllers.Instance.AiUnits().Add(aiRanger);

            var playerFarAway = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 902,
                Position = new UnityEngine.Vector3(100f, 0f, 0f),
                CurrentHealth = 10f,
                MaxHealth = 10f,
                Damage1 = 500f,
                Attack1Cooldown = 1f
            };
            ExistingControllers.Instance.PlayerEntities().Add(playerFarAway);

            var rangeModule = new ThreatPrioritizationAiModule();
            var rangeBehaviour = rangeModule.CreateBehaviour(new DefaultAiBehaviour(),
            [
                new ContentModuleParameter { name = "engagementRadius", value = "35" },
                new ContentModuleParameter { name = "checkIntervalSeconds", value = "1" }
            ]);
            rangeBehaviour.OnInitialized();
            rangeBehaviour.Tick();

            if (aiRanger.ExecuteCommandCallCount > 0)
            {
                throw new InvalidOperationException("AI attacker targeted an enemy candidate located outside engagement radius.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("threat-prioritization role profile specializes targeting by allied unit class", () =>
    {
        var sniperProfile = ThreatPrioritizationRoleProfile.Resolve(new EntityController { Role = UnitRole.Sniper }.Role);
        var meleeProfile = ThreatPrioritizationRoleProfile.Resolve(new EntityController { Role = UnitRole.Melee }.Role);
        var supporterProfile = ThreatPrioritizationRoleProfile.Resolve(new EntityController { Role = UnitRole.Supporter }.Role);
        var genericProfile = ThreatPrioritizationRoleProfile.Resolve(new EntityController { Role = UnitRole.None }.Role);

        if (sniperProfile.EngagementRangeMultiplier <= 1.0f || sniperProfile.DistanceWeight >= 1.0f)
            throw new InvalidOperationException("Sniper profile must extend range and reduce distance penalty.");
        if (meleeProfile.EngagementRangeMultiplier >= 1.0f || meleeProfile.DistanceWeight <= 1.0f)
            throw new InvalidOperationException("Melee profile must close range and increase distance penalty.");
        if (supporterProfile.EngagesCombat)
            throw new InvalidOperationException("Supporter profile must not engage combat targets.");
        if (!genericProfile.EngagesCombat || genericProfile.EngagementRangeMultiplier != 1.0f || genericProfile.DistanceWeight != 1.0f)
            throw new InvalidOperationException("Generic allied unit must keep default targeting behavior.");

        if (!ThreatPrioritizationRoleProfile.ResolveFor(new EntityController { Role = UnitRole.Sniper }).Equals(sniperProfile))
            throw new InvalidOperationException("ResolveFor diverged from Resolve for Sniper.");
        if (!ThreatPrioritizationRoleProfile.ResolveFor(null).Equals(genericProfile))
            throw new InvalidOperationException("ResolveFor(null) must degrade to default.");
    }),
    ("threat-prioritization sniper allied unit engages target beyond default engagement radius", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var sniperAttacker = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                UniqueEntityId = 9501,
                aiBaseIndex = 0,
                Role = UnitRole.Sniper,
                Position = UnityEngine.Vector3.zero
            };
            ExistingControllers.Instance.AiUnits().Add(sniperAttacker);

            var distantEnemy = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 9601,
                Position = new UnityEngine.Vector3(45f, 0f, 0f),
                CurrentHealth = 40f,
                MaxHealth = 40f,
                Damage1 = 30f,
                Attack1Cooldown = 1f
            };
            ExistingControllers.Instance.PlayerEntities().Add(distantEnemy);

            var module = new ThreatPrioritizationAiModule();
            behaviour = module.CreateBehaviour(new DefaultAiBehaviour(),
            [
                new ContentModuleParameter { name = "engagementRadius", value = "35" },
                new ContentModuleParameter { name = "checkIntervalSeconds", value = "1" }
            ]);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (sniperAttacker.ExecuteCommandCallCount == 0 || sniperAttacker.CurrentAttackTarget != distantEnemy)
                throw new InvalidOperationException("Sniper allied unit failed to engage a target within its extended (1.4x) engagement radius.");
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("threat-prioritization supporter allied unit is never retargeted onto enemies", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var supporter = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                UniqueEntityId = 9502,
                aiBaseIndex = 0,
                Role = UnitRole.Supporter,
                Position = UnityEngine.Vector3.zero
            };
            ExistingControllers.Instance.AiUnits().Add(supporter);

            var nearbyEnemy = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 9602,
                Position = new UnityEngine.Vector3(5f, 0f, 0f),
                CurrentHealth = 30f,
                MaxHealth = 30f,
                Damage1 = 10f,
                Attack1Cooldown = 1f
            };
            ExistingControllers.Instance.PlayerEntities().Add(nearbyEnemy);

            var module = new ThreatPrioritizationAiModule();
            behaviour = module.CreateBehaviour(new DefaultAiBehaviour(),
            [
                new ContentModuleParameter { name = "engagementRadius", value = "35" },
                new ContentModuleParameter { name = "checkIntervalSeconds", value = "1" }
            ]);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (supporter.ExecuteCommandCallCount > 0)
                throw new InvalidOperationException("Supporter allied unit was retargeted onto an enemy despite not engaging combat.");
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("wave-escalation ignores non-combat AI units during assault dispatch", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        AiBehaviourManager.CurrentAttackWaveNumber = 0;
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var hq = new EntityController
            {
                IsControlledByPlayer = true,
                IsBuilding = true,
                EntityId = "EngineeringHq",
                Position = new UnityEngine.Vector3(20f, 0f, 0f)
            };
            var supportA = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = false,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero,
                UniqueEntityId = 741
            };
            var supportB = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = false,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                Position = new UnityEngine.Vector3(1f, 0f, 0f),
                UniqueEntityId = 742
            };
            ExistingControllers.Instance.EngineeringHqEntity = hq;
            ExistingControllers.Instance.AiUnits().Add(supportA);
            ExistingControllers.Instance.AiUnits().Add(supportB);

            behaviour = new WaveEscalationAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "waveIntervalSeconds", value = "10" },
                    new ContentModuleParameter { name = "maxWaves", value = "1" },
                    new ContentModuleParameter { name = "baseIndex", value = "0" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 10f;
            behaviour.Tick();

            if (supportA.ExecuteCommandCallCount != 0 || supportB.ExecuteCommandCallCount != 0)
            {
                throw new InvalidOperationException("Wave escalation dispatched non-combat AI units.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
            AiBehaviourManager.CurrentAttackWaveNumber = 0;
        }
    }),
    ("wave-escalation AI module increments attack wave counter and orders assault", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        AiBehaviourManager.CurrentAttackWaveNumber = 0;
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var hq = new EntityController
            {
                IsControlledByPlayer = true,
                IsBuilding = true,
                EntityId = "EngineeringHq",
                Position = new UnityEngine.Vector3(50f, 0f, 0f)
            };
            ExistingControllers.Instance.EngineeringHqEntity = hq;

            var aiUnit = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                UniqueEntityId = 501,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            };
            ExistingControllers.Instance.AiUnits().Add(aiUnit);

            behaviour = new WaveEscalationAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "waveIntervalSeconds", value = "10" },
                    new ContentModuleParameter { name = "maxWaves", value = "3" },
                    new ContentModuleParameter { name = "baseIndex", value = "0" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 10f;
            behaviour.Tick();

            if (AiBehaviourManager.CurrentAttackWaveNumber != 1)
            {
                throw new InvalidOperationException("Wave escalation did not increment CurrentAttackWaveNumber.");
            }
            if (!aiUnit.LastExecutedCommand.HasValue ||
                aiUnit.LastExecutedCommand.Value.command != EntityCommand.Command.AttackMoveOnTarget)
            {
                throw new InvalidOperationException("Wave escalation did not dispatch AttackMoveOnTarget to HQ.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
            AiBehaviourManager.CurrentAttackWaveNumber = 0;
        }
    }),
    ("tactical AI modules squad-formation, tactical-skill-activation, and wave-escalation are registered", () =>
    {
        var registry = new AiModuleRegistry();
        registry.Register(new SquadFormationAiModule());
        registry.Register(new TacticalSkillActivationAiModule());
        registry.Register(new CounterBatteryAiModule());
        registry.Register(new WaveEscalationAiModule());
        registry.Register(new ThreatPrioritizationAiModule());

        if (!registry.TryValidate("sample:counter-battery", null, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Counter-battery module failed registry validation: " + diagnostic);
        }
        if (!registry.TryValidate("sample:squad-formation", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Squad-formation module failed registry validation: " + diagnostic);
        }
        if (!registry.TryValidate("sample:tactical-skill-activation", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-skill-activation module failed registry validation: " + diagnostic);
        }
        if (!registry.TryValidate("sample:wave-escalation", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Wave-escalation module failed registry validation: " + diagnostic);
        }
        if (!registry.TryValidate("sample:threat-prioritization", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Threat-prioritization module failed registry validation: " + diagnostic);
        }
    }),
    ("counter-battery AI module parses defaults and validates limits", () =>
    {
        if (!CounterBatteryParameterParser.TryParse(null, out CounterBatterySettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Counter-battery default parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != -1 || settings.ThreatTriggerDistance != 25.0f || settings.MinInterceptorSpeed != 10.0f ||
            settings.MaxInterceptors != 4 || settings.CooldownSeconds != 8.0f)
        {
            throw new InvalidOperationException("Counter-battery default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "baseIndex", value = "1" },
            new ContentModuleParameter { name = "threatTriggerDistance", value = "30" },
            new ContentModuleParameter { name = "minInterceptorSpeed", value = "12" },
            new ContentModuleParameter { name = "maxInterceptors", value = "6" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "5" }
        };
        if (!CounterBatteryParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Counter-battery custom parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != 1 || settings.ThreatTriggerDistance != 30f || settings.MinInterceptorSpeed != 12f ||
            settings.MaxInterceptors != 6 || settings.CooldownSeconds != 5f)
        {
            throw new InvalidOperationException("Counter-battery parsed values incorrect.");
        }

        var invalidDist = new[] { new ContentModuleParameter { name = "threatTriggerDistance", value = "100" } };
        if (CounterBatteryParameterParser.TryParse(invalidDist, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Counter-battery should reject threatTriggerDistance > 80.");
        }

        var invalidSpeed = new[] { new ContentModuleParameter { name = "minInterceptorSpeed", value = "2" } };
        if (CounterBatteryParameterParser.TryParse(invalidSpeed, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Counter-battery should reject minInterceptorSpeed < 5.");
        }
    }),
    ("counter-battery AI module dispatches fast interceptors against long-range threat", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        EntityBalancingStore.Reset();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var fastDefender = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                EntityId = "FastDefender",
                UniqueEntityId = 501,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero,
                MoveSpeed = 15f
            };
            var slowDefender = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "SlowDefender",
                UniqueEntityId = 502,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero,
                MoveSpeed = 6f
            };
            var playerArtillery = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                UniqueEntityId = 601,
                Position = new UnityEngine.Vector3(35f, 0f, 0f),
                WeaponRange = 35f
            };

            ExistingControllers.Instance.AiUnits().Add(fastDefender);
            ExistingControllers.Instance.AiUnits().Add(slowDefender);
            ExistingControllers.Instance.PlayerEntities().Add(playerArtillery);

            behaviour = new CounterBatteryAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "threatTriggerDistance", value = "20" },
                    new ContentModuleParameter { name = "minInterceptorSpeed", value = "10" },
                    new ContentModuleParameter { name = "maxInterceptors", value = "2" },
                    new ContentModuleParameter { name = "cooldownSeconds", value = "5" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (fastDefender.ExecuteCommandCallCount == 0 || fastDefender.LastExecutedCommand == null)
            {
                throw new InvalidOperationException("Fast defender was not dispatched against artillery threat.");
            }
            if (slowDefender.ExecuteCommandCallCount > 0)
            {
                throw new InvalidOperationException("Slow defender was dispatched despite not meeting minInterceptorSpeed.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("counter-battery ignores non-combat AI units even when they are fast", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var nonCombatInterceptor = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = false,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                EntityId = "FastSupport",
                UniqueEntityId = 511,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero,
                MoveSpeed = 20f
            };
            var playerArtillery = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                EntityId = "PlayerArtillery",
                UniqueEntityId = 611,
                Position = new UnityEngine.Vector3(35f, 0f, 0f),
                WeaponRange = 35f
            };
            ExistingControllers.Instance.AiUnits().Add(nonCombatInterceptor);
            ExistingControllers.Instance.PlayerEntities().Add(playerArtillery);

            behaviour = new CounterBatteryAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "threatTriggerDistance", value = "20" },
                    new ContentModuleParameter { name = "minInterceptorSpeed", value = "10" },
                    new ContentModuleParameter { name = "maxInterceptors", value = "1" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (nonCombatInterceptor.ExecuteCommandCallCount != 0)
            {
                throw new InvalidOperationException("Counter-battery dispatched a non-combat AI unit.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("counter-battery ignores a short min-attack-range hint below the threat threshold", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        CounterBatteryAiModule.MinAttackRangeResolver = _ => 4.5f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var fastDefender = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                EntityId = "FastDefender",
                UniqueEntityId = 521,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero,
                MoveSpeed = 15f
            };
            var playerUnit = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                EntityId = "PlayerUnit",
                UniqueEntityId = 621,
                Position = new UnityEngine.Vector3(35f, 0f, 0f),
                WeaponRange = 15f
            };
            ExistingControllers.Instance.AiUnits().Add(fastDefender);
            ExistingControllers.Instance.PlayerEntities().Add(playerUnit);

            behaviour = new CounterBatteryAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "threatTriggerDistance", value = "20" },
                    new ContentModuleParameter { name = "minInterceptorSpeed", value = "10" },
                    new ContentModuleParameter { name = "maxInterceptors", value = "1" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (fastDefender.ExecuteCommandCallCount != 0)
            {
                throw new InvalidOperationException(
                    "Counter-battery treated a short minAttackRange dead-zone as long-range artillery.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            CounterBatteryAiModule.MinAttackRangeResolver = null;
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("tactical AI modules skirmish-kiting and support-repair are registered and validated", () =>
    {
        var registry = new AiModuleRegistry();
        registry.Register(new SkirmishKitingAiModule());
        registry.Register(new SupportRepairAiModule());

        if (!registry.TryValidate("sample:skirmish-kiting", null, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Skirmish-kiting module failed registry validation: " + diagnostic);
        }
        if (!registry.TryValidate("sample:support-repair", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Support-repair module failed registry validation: " + diagnostic);
        }
    }),
    ("skirmish kiting AI module parses defaults, custom limits and commands retreat when enemy encroaches", () =>
    {
        if (!SkirmishKitingParameterParser.TryParse(null, out SkirmishKitingSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Skirmish kiting default parse failed: " + diagnostic);
        }
        if (settings.RetreatDistanceRatio != 0.70f || settings.PreferredEngagementRatio != 0.90f ||
            settings.MinKiteSpeed != 8.0f || settings.FleeDistance != 8.0f || settings.MaxKiteTargets != 8)
        {
            throw new InvalidOperationException("Skirmish kiting defaults unexpected values.");
        }

        var invalidParams = new[] { new ContentModuleParameter { name = "retreatDistanceRatio", value = "0.99" }, new ContentModuleParameter { name = "preferredEngagementRatio", value = "0.60" } };
        if (SkirmishKitingParameterParser.TryParse(invalidParams, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Skirmish kiting should reject retreatRatio >= preferredRatio.");
        }

        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var fastKiteUnit = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                MoveSpeed = 12f,
                WeaponRange = 20f,
                Position = new Vector3(10f, 0f, 10f)
            };
            var closePlayerEnemy = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                Position = new Vector3(12f, 0f, 10f)
            };

            ExistingControllers.Instance.Reset();
            ExistingControllers.Instance.AiUnits().Add(fastKiteUnit);
            ExistingControllers.Instance.PlayerEntities().Add(closePlayerEnemy);

            behaviour = new SkirmishKitingAiModule().CreateBehaviour(new DefaultAiBehaviour(), null);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (fastKiteUnit.Position.x >= 10f)
            {
                throw new InvalidOperationException("Fast kite unit did not retreat away from encroaching threat: pos=" + fastKiteUnit.Position);
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("support repair AI module parses parameters and dispatches repairers to damaged priority friendlies", () =>
    {
        if (!SupportRepairParameterParser.TryParse(null, out SupportRepairSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Support repair default parse failed: " + diagnostic);
        }
        if (settings.HealthThresholdRatio != 0.85f || settings.RepairScanRadius != 25.0f ||
            !settings.PrioritizeHarvestersAndBuildings || settings.MaxAssignedRepairersPerTarget != 2)
        {
            throw new InvalidOperationException("Support repair defaults unexpected values.");
        }

        var invalidThreshold = new[] { new ContentModuleParameter { name = "healthThresholdRatio", value = "1.5" } };
        if (SupportRepairParameterParser.TryParse(invalidThreshold, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Support repair should reject healthThresholdRatio > 0.99.");
        }

        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var engineer = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsEngineer = true,
                CanMoveAndMovementIsEnabled = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var damagedHarvester = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                Role = UnitRole.Harvester,
                MaxHealth = 100f,
                CurrentHealth = 40f,
                Position = new Vector3(20f, 0f, 0f)
            };
            var damagedUnit = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                MaxHealth = 100f,
                CurrentHealth = 30f,
                Position = new Vector3(0f, 0f, 5f)
            };

            ExistingControllers.Instance.Reset();
            ExistingControllers.Instance.AiUnits().Add(engineer);
            ExistingControllers.Instance.AiUnits().Add(damagedHarvester);
            ExistingControllers.Instance.AiUnits().Add(damagedUnit);

            behaviour = new SupportRepairAiModule().CreateBehaviour(new DefaultAiBehaviour(), null);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (engineer.Position.x != 20f || engineer.Position.z != 0f)
            {
                throw new InvalidOperationException("Engineer did not prioritize the damaged harvester over the nearer damaged unit: pos=" + engineer.Position);
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("support repair AI preserves unrelated attack commands and queues repair movement", () =>
    {
        ExistingControllers.Instance.Reset();
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            UnityEngine.Time.time = 10f;
            var busyRepairer = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsEngineer = true,
                CanMoveAndMovementIsEnabled = true,
                CurrentPlayerCommandName = EntityCommand.Command.Attack,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            var idleRepairer = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsEngineer = true,
                CanMoveAndMovementIsEnabled = true,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                Position = new UnityEngine.Vector3(1f, 0f, 0f)
            };
            var damagedHarvester = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                Role = UnitRole.Harvester,
                MaxHealth = 100f,
                CurrentHealth = 40f,
                Position = new UnityEngine.Vector3(20f, 0f, 0f)
            };

            ExistingControllers.Instance.AiUnits().Add(busyRepairer);
            ExistingControllers.Instance.AiUnits().Add(idleRepairer);
            ExistingControllers.Instance.AiUnits().Add(damagedHarvester);

            behaviour = new SupportRepairAiModule().CreateBehaviour(new DefaultAiBehaviour(), null);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (busyRepairer.ExecuteCommandCallCount != 0)
            {
                throw new InvalidOperationException("Support repair overwrote an unrelated attack command.");
            }
            if (!idleRepairer.LastExecutedCommand.HasValue ||
                idleRepairer.LastExecutedCommand.Value.command != EntityCommand.Command.Move ||
                idleRepairer.LastExecutedCommand.Value.destination.x != damagedHarvester.Position.x ||
                idleRepairer.LastCommandProcessingType != EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand)
            {
                throw new InvalidOperationException("Repair movement was not queued ahead of the idle repairer's current order.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("HUB-D Phase 1: AiUnitSnapshot and AiBattlefieldBlackboard compute pure metrics, threat centroids, and omission counters", () =>
    {
        var allyA = new RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot(
            uniqueEntityId: 101,
            entityId: "sample-native:scout-tank",
            baseIndex: 0,
            posX: 10f,
            posZ: 20f,
            currentHealth: 40f,
            maxHealth: 100f,
            currentShield: 25f,
            maxShield: 50f,
            dpsEstimate: 30f,
            weaponRange: 14f,
            minAttackRange: 0f,
            moveSpeed: 12f,
            roleFlags: (int)UnitRole.Scout,
            isControlledByAi: true,
            isControlledByPlayer: false,
            isCombatUnit: true,
            isHarvester: false,
            isBuilding: false,
            isSupportUnit: false,
            hasQueuedCommands: false,
            currentCommandTag: "tag-a",
            currentTargetId: 201);

        if (Math.Abs(allyA.HealthRatio - 0.4f) > 0.0001f ||
            Math.Abs(allyA.ShieldRatio - 0.5f) > 0.0001f ||
            !allyA.IsIdleAndQueueEmpty(false) ||
            allyA.IsIdleAndQueueEmpty(true))
        {
            throw new InvalidOperationException("AiUnitSnapshot ratios or queue check did not match expected pure calculation.");
        }

        var allyHarvester = new RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot(
            uniqueEntityId: 102,
            entityId: "native:harvester",
            baseIndex: 0,
            posX: 0f,
            posZ: 0f,
            currentHealth: 50f,
            maxHealth: 100f,
            currentShield: 0f,
            maxShield: 0f,
            dpsEstimate: 0f,
            weaponRange: 0f,
            minAttackRange: 0f,
            moveSpeed: 6f,
            roleFlags: (int)UnitRole.Harvester,
            isControlledByAi: true,
            isControlledByPlayer: false,
            isCombatUnit: false,
            isHarvester: true,
            isBuilding: false,
            isSupportUnit: false,
            hasQueuedCommands: false,
            currentCommandTag: string.Empty,
            currentTargetId: -1);

        var hostileA = new RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot(
            uniqueEntityId: 201,
            entityId: "player:striker",
            baseIndex: -1,
            posX: 4f,
            posZ: 0f,
            currentHealth: 80f,
            maxHealth: 100f,
            currentShield: 0f,
            maxShield: 0f,
            dpsEstimate: 45f,
            weaponRange: 12f,
            minAttackRange: 0f,
            moveSpeed: 8f,
            roleFlags: (int)UnitRole.FrontalAttacker,
            isControlledByAi: false,
            isControlledByPlayer: true,
            isCombatUnit: true,
            isHarvester: false,
            isBuilding: false,
            isSupportUnit: false,
            hasQueuedCommands: false,
            currentCommandTag: string.Empty,
            currentTargetId: 102);

        var hostileB = new RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot(
            uniqueEntityId: 202,
            entityId: "player:artillery",
            baseIndex: -1,
            posX: 16f,
            posZ: 0f,
            currentHealth: 60f,
            maxHealth: 60f,
            currentShield: 0f,
            maxShield: 0f,
            dpsEstimate: 55f,
            weaponRange: 28f,
            minAttackRange: 10f,
            moveSpeed: 5f,
            roleFlags: (int)UnitRole.Sniper,
            isControlledByAi: false,
            isControlledByPlayer: true,
            isCombatUnit: true,
            isHarvester: false,
            isBuilding: false,
            isSupportUnit: false,
            hasQueuedCommands: false,
            currentCommandTag: string.Empty,
            currentTargetId: -1);

        var snapshot = RogueCommandExpansionLoader.Runtime.Ai.AiBattlefieldBlackboardBuilder.BuildSnapshot(
            timestamp: 12.5f,
            baseIndex: 0,
            currentWaveNumber: 3,
            aiUnits: new[] { allyA, allyHarvester },
            hostileUnits: new[] { hostileA, hostileB },
            maxTrackedUnitsPerSide: 1);

        if (snapshot.OmittedAiUnitCount != 1 ||
            snapshot.OmittedHostileUnitCount != 1 ||
            snapshot.TotalAiCombatUnits != 1 ||
            snapshot.TotalAiHarvesters != 1 ||
            snapshot.TotalHostileCombatUnits != 2 ||
            !snapshot.UnderHarvesterOrBasePressure ||
            Math.Abs(snapshot.HostileCentroidX - 10f) > 0.01f ||
            Math.Abs(snapshot.HostileCentroidZ - 0f) > 0.01f)
        {
            throw new InvalidOperationException("AiBattlefieldBlackboardBuilder did not compute expected omission counts, centroid, or pressure state.");
        }
    }),
    ("HUB-D Phase 1: StrategicDoctrinePolicy and TacticalUtilityScorer evaluate Lanchester balance, dead-zone kiting, and role affinity", () =>
    {
        var pressuredSnapshot = new RogueCommandExpansionLoader.Runtime.Ai.AiBattlefieldSnapshot(
            timestamp: 20f,
            baseIndex: 0,
            currentWaveNumber: 2,
            omittedAiUnitCount: 0,
            omittedHostileUnitCount: 0,
            totalAiCombatUnits: 2,
            totalAiHarvesters: 1,
            totalAiBuildings: 2,
            totalHostileCombatUnits: 5,
            totalAiCombatPower: 80f,
            totalHostileCombatPower: 250f,
            hostileCentroidX: 15f,
            hostileCentroidZ: 15f,
            underHarvesterOrBasePressure: true,
            aiUnits: Array.Empty<RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot>(),
            hostileUnits: Array.Empty<RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot>());

        RogueCommandExpansionLoader.Runtime.Ai.AiDoctrineWeights fortifyWeights =
            RogueCommandExpansionLoader.Runtime.Ai.StrategicDoctrinePolicy.Evaluate(pressuredSnapshot);

        if (fortifyWeights.DoctrineName != "Fortify" ||
            fortifyWeights.SurvivalWeight <= fortifyWeights.AssaultWeight ||
            fortifyWeights.DefenseWeight <= fortifyWeights.AssaultWeight)
        {
            throw new InvalidOperationException("StrategicDoctrinePolicy did not select Fortify when outnumbered and under base/harvester pressure.");
        }

        var dominantSnapshot = new RogueCommandExpansionLoader.Runtime.Ai.AiBattlefieldSnapshot(
            timestamp: 60f,
            baseIndex: 0,
            currentWaveNumber: 4,
            omittedAiUnitCount: 0,
            omittedHostileUnitCount: 0,
            totalAiCombatUnits: 8,
            totalAiHarvesters: 2,
            totalAiBuildings: 4,
            totalHostileCombatUnits: 2,
            totalAiCombatPower: 400f,
            totalHostileCombatPower: 90f,
            hostileCentroidX: 50f,
            hostileCentroidZ: 50f,
            underHarvesterOrBasePressure: false,
            aiUnits: Array.Empty<RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot>(),
            hostileUnits: Array.Empty<RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot>());

        RogueCommandExpansionLoader.Runtime.Ai.AiDoctrineWeights assaultWeights =
            RogueCommandExpansionLoader.Runtime.Ai.StrategicDoctrinePolicy.Evaluate(dominantSnapshot);

        if (assaultWeights.DoctrineName != "Assault" ||
            assaultWeights.AssaultWeight <= assaultWeights.DefenseWeight)
        {
            throw new InvalidOperationException("StrategicDoctrinePolicy did not select Assault when AI forces strongly overmatch hostiles.");
        }

        var artilleryUnit = new RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot(
            uniqueEntityId: 301,
            entityId: "sample-native:mortar-carrier",
            baseIndex: 0,
            posX: 0f,
            posZ: 0f,
            currentHealth: 100f,
            maxHealth: 100f,
            currentShield: 0f,
            maxShield: 0f,
            dpsEstimate: 40f,
            weaponRange: 30f,
            minAttackRange: 12f,
            moveSpeed: 8f,
            roleFlags: (int)UnitRole.Sniper,
            isControlledByAi: true,
            isControlledByPlayer: false,
            isCombatUnit: true,
            isHarvester: false,
            isBuilding: false,
            isSupportUnit: false,
            hasQueuedCommands: false,
            currentCommandTag: string.Empty,
            currentTargetId: -1);

        var encroachingEnemy = new RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot(
            uniqueEntityId: 401,
            entityId: "player:brawler",
            baseIndex: -1,
            posX: 5f,
            posZ: 0f,
            currentHealth: 120f,
            maxHealth: 120f,
            currentShield: 0f,
            maxShield: 0f,
            dpsEstimate: 35f,
            weaponRange: 6f,
            minAttackRange: 0f,
            moveSpeed: 10f,
            roleFlags: (int)UnitRole.Melee,
            isControlledByAi: false,
            isControlledByPlayer: true,
            isCombatUnit: true,
            isHarvester: false,
            isBuilding: false,
            isSupportUnit: false,
            hasQueuedCommands: false,
            currentCommandTag: string.Empty,
            currentTargetId: 301);

        float deadZoneKiteScore = RogueCommandExpansionLoader.Runtime.Ai.TacticalUtilityScorer.ScoreSurvivalOrKite(
            artilleryUnit, encroachingEnemy, fortifyWeights);
        float standardEngagementScore = RogueCommandExpansionLoader.Runtime.Ai.TacticalUtilityScorer.ScoreCombatTarget(
            artilleryUnit, encroachingEnemy, fortifyWeights);

        if (deadZoneKiteScore <= standardEngagementScore || deadZoneKiteScore < 100f)
        {
            throw new InvalidOperationException("TacticalUtilityScorer did not prioritize dead-zone kiting above standard attack when enemy is inside minAttackRange.");
        }
    }),
    ("HUB-D Phase 1: AiCommandArbiterPolicy enforces per-unit uniqueness, hysteresis, lockout windows, and global tick budget", () =>
    {
        var state = new RogueCommandExpansionLoader.Runtime.Ai.AiCommandArbiterState();

        var kiteProposal = new RogueCommandExpansionLoader.Runtime.Ai.AiIntentProposal(
            moduleId: "sample:skirmish-kiting",
            commandTag: "tag.kite",
            unitId: 501,
            targetEntityId: -1,
            destX: -10f,
            destZ: 0f,
            category: RogueCommandExpansionLoader.Runtime.Ai.AiIntentCategory.SurvivalKite,
            utilityScore: 140f,
            lockoutDurationSeconds: 2.0f,
            requiresEmptyQueue: false);

        var siegeProposal = new RogueCommandExpansionLoader.Runtime.Ai.AiIntentProposal(
            moduleId: "sample:siege-assault",
            commandTag: "tag.siege",
            unitId: 501,
            targetEntityId: 901,
            destX: 50f,
            destZ: 50f,
            category: RogueCommandExpansionLoader.Runtime.Ai.AiIntentCategory.SiegeAndFormation,
            utilityScore: 85f,
            lockoutDurationSeconds: 0.5f,
            requiresEmptyQueue: false);

        var secondUnitProposal = new RogueCommandExpansionLoader.Runtime.Ai.AiIntentProposal(
            moduleId: "sample:threat-prioritization",
            commandTag: "tag.threat",
            unitId: 502,
            targetEntityId: 902,
            destX: 20f,
            destZ: 20f,
            category: RogueCommandExpansionLoader.Runtime.Ai.AiIntentCategory.CombatTargeting,
            utilityScore: 95f,
            lockoutDurationSeconds: 0.5f,
            requiresEmptyQueue: false);

        RogueCommandExpansionLoader.Runtime.Ai.AiArbitrationResult tick1 =
            RogueCommandExpansionLoader.Runtime.Ai.AiCommandArbiterPolicy.Arbitrate(
                state,
                now: 10.0f,
                retargetHysteresisMultiplier: 1.20f,
                maxDispatchesPerTick: 1,
                proposals: new[] { siegeProposal, kiteProposal, secondUnitProposal });

        if (tick1.ApprovedOrders.Count != 1 ||
            tick1.ApprovedOrders[0].UnitId != 501 ||
            tick1.ApprovedOrders[0].ModuleId != "sample:skirmish-kiting" ||
            tick1.ConflictsResolvedCount != 1 ||
            tick1.BudgetOmittedCount != 1)
        {
            throw new InvalidOperationException("AiCommandArbiterPolicy Tick 1 did not pick highest utility or enforce global dispatch budget.");
        }

        var prematureOverrideProposal = new RogueCommandExpansionLoader.Runtime.Ai.AiIntentProposal(
            moduleId: "sample:threat-prioritization",
            commandTag: "tag.threat",
            unitId: 501,
            targetEntityId: 902,
            destX: 20f,
            destZ: 20f,
            category: RogueCommandExpansionLoader.Runtime.Ai.AiIntentCategory.CombatTargeting,
            utilityScore: 150f,
            lockoutDurationSeconds: 0.5f,
            requiresEmptyQueue: false);

        RogueCommandExpansionLoader.Runtime.Ai.AiArbitrationResult tick2 =
            RogueCommandExpansionLoader.Runtime.Ai.AiCommandArbiterPolicy.Arbitrate(
                state,
                now: 11.0f,
                retargetHysteresisMultiplier: 1.20f,
                maxDispatchesPerTick: 4,
                proposals: new[] { prematureOverrideProposal });

        if (tick2.ApprovedOrders.Count != 0 || tick2.LockoutBlockedCount != 1)
        {
            throw new InvalidOperationException("AiCommandArbiterPolicy Tick 2 did not block lower-priority override during active survival lockout window.");
        }

        var weakRetargetProposal = new RogueCommandExpansionLoader.Runtime.Ai.AiIntentProposal(
            moduleId: "sample:skirmish-kiting",
            commandTag: "tag.kite",
            unitId: 501,
            targetEntityId: -2,
            destX: -12f,
            destZ: 2f,
            category: RogueCommandExpansionLoader.Runtime.Ai.AiIntentCategory.SurvivalKite,
            utilityScore: 150f,
            lockoutDurationSeconds: 1.0f,
            requiresEmptyQueue: false);

        RogueCommandExpansionLoader.Runtime.Ai.AiArbitrationResult tick3 =
            RogueCommandExpansionLoader.Runtime.Ai.AiCommandArbiterPolicy.Arbitrate(
                state,
                now: 12.5f,
                retargetHysteresisMultiplier: 1.20f,
                maxDispatchesPerTick: 4,
                proposals: new[] { weakRetargetProposal });

        if (tick3.ApprovedOrders.Count != 0 || tick3.HysteresisBlockedCount != 1)
        {
            throw new InvalidOperationException("AiCommandArbiterPolicy Tick 3 did not enforce retarget hysteresis threshold.");
        }
    }),
    ("HUB-D Phase 2: RuntimeAiBattlefieldCollector projects live ExistingControllers into AiBattlefieldSnapshot without mutating control flags", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            UnityEngine.Time.time = 25.0f;

            var aiCombat = new EntityController
            {
                UniqueEntityId = 701,
                EntityId = "sample-native:scout-tank",
                IsControlledByAi = true,
                IsControlledByPlayer = false,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 75f,
                MaxHealth = 100f,
                CurrentShield = 10f,
                MaxShield = 20f,
                Damage1 = 20f,
                Attack1Cooldown = 1.0f,
                WeaponRange = 16f,
                MoveSpeed = 11f,
                Role = UnitRole.Scout,
                Position = new Vector3(12f, 0f, 8f)
            };
            aiCombat.CommandChainForTest.AddLast(new EntityCommand { command = EntityCommand.Command.Move });

            var aiHarvester = new EntityController
            {
                UniqueEntityId = 702,
                EntityId = "native:harvester",
                IsControlledByAi = true,
                IsControlledByPlayer = false,
                IsUnit = true,
                Role = UnitRole.Harvester,
                aiBaseIndex = 0,
                CurrentHealth = 90f,
                MaxHealth = 100f,
                Position = new Vector3(0f, 0f, 0f)
            };

            var playerAttacker = new EntityController
            {
                UniqueEntityId = 801,
                EntityId = "player:tank",
                IsControlledByPlayer = true,
                IsControlledByAi = false,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Damage1 = 30f,
                Attack1Cooldown = 1.0f,
                WeaponRange = 14f,
                MoveSpeed = 9f,
                Role = UnitRole.Tank,
                CurrentAttackTarget = aiHarvester,
                Position = new Vector3(5f, 0f, 0f)
            };

            ExistingControllers.Instance.AiUnits().Add(aiCombat);
            ExistingControllers.Instance.AiUnits().Add(aiHarvester);
            ExistingControllers.Instance.PlayerUnits().Add(playerAttacker);

            var collector = new RogueCommandExpansionLoader.Runtime.Ai.RuntimeAiBattlefieldCollector(baseIndex: 0);
            RogueCommandExpansionLoader.Runtime.Ai.AiBattlefieldSnapshot snapshot = collector.CollectCurrentSnapshot();

            if (snapshot.TotalAiCombatUnits != 1 ||
                snapshot.TotalAiHarvesters != 1 ||
                snapshot.TotalHostileCombatUnits != 1 ||
                !snapshot.UnderHarvesterOrBasePressure ||
                !collector.TryResolveAiEntity(701, out EntityController resolvedAi) ||
                resolvedAi != aiCombat ||
                !collector.TryResolveHostileEntity(801, out EntityController resolvedHostile) ||
                resolvedHostile != playerAttacker)
            {
                throw new InvalidOperationException("RuntimeAiBattlefieldCollector failed to project live entities or resolve lookup handles.");
            }

            RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot capturedCombat =
                snapshot.AiUnits.Single(u => u.UniqueEntityId == 701);
            if (!capturedCombat.HasQueuedCommands ||
                Math.Abs(capturedCombat.HealthRatio - 0.75f) > 0.001f ||
                !aiCombat.IsControlledByAi || aiCombat.IsControlledByPlayer ||
                !playerAttacker.IsControlledByPlayer || playerAttacker.IsControlledByAi)
            {
                throw new InvalidOperationException("RuntimeAiBattlefieldCollector did not read private _commandChain queue or mutated control flags.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("HUB-D Phase 2: CompositeAiRuntimeBehaviour coordinates ICoordinatedAiRuntimeBehaviour modules through shared blackboard and arbiter while preserving legacy modules", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            UnityEngine.Time.time = 30.0f;

            var aiUnit = new EntityController
            {
                UniqueEntityId = 950,
                EntityId = "sample-native:skirmish-bike",
                IsControlledByAi = true,
                IsControlledByPlayer = false,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 30f,
                MaxHealth = 100f,
                Damage1 = 15f,
                Attack1Cooldown = 1.0f,
                WeaponRange = 14f,
                MoveSpeed = 14f,
                Role = UnitRole.Scout,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                Position = new Vector3(10f, 0f, 10f)
            };

            var hostileUnit = new EntityController
            {
                UniqueEntityId = 960,
                EntityId = "player:heavy",
                IsControlledByPlayer = true,
                IsControlledByAi = false,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CurrentHealth = 150f,
                MaxHealth = 150f,
                Damage1 = 40f,
                Attack1Cooldown = 1.0f,
                WeaponRange = 12f,
                MoveSpeed = 7f,
                Role = UnitRole.Tank,
                Position = new Vector3(12f, 0f, 10f)
            };

            ExistingControllers.Instance.AiUnits().Add(aiUnit);
            ExistingControllers.Instance.PlayerUnits().Add(hostileUnit);

            int legacyTickCount = 0;
            var legacyModule = new DelegatingTestAiRuntimeBehaviour(() => legacyTickCount++);

            // Module 1 proposes lower-priority CombatTargeting (score 75) onto hostile 960
            var combatModule = new CoordinatedTestAiRuntimeBehaviour((snapshot, weights, proposals) =>
            {
                proposals.Add(new RogueCommandExpansionLoader.Runtime.Ai.AiIntentProposal(
                    moduleId: "sample:threat-prioritization",
                    commandTag: "tag.combat",
                    unitId: 950,
                    targetEntityId: 960,
                    destX: hostileUnit.Position.x,
                    destZ: hostileUnit.Position.z,
                    category: RogueCommandExpansionLoader.Runtime.Ai.AiIntentCategory.CombatTargeting,
                    utilityScore: 75f,
                    lockoutDurationSeconds: 0.5f,
                    requiresEmptyQueue: false));
            });

            // Module 2 proposes higher-priority SurvivalKite (score 160) away from hostile to (0, 10)
            var kiteModule = new CoordinatedTestAiRuntimeBehaviour((snapshot, weights, proposals) =>
            {
                proposals.Add(new RogueCommandExpansionLoader.Runtime.Ai.AiIntentProposal(
                    moduleId: "sample:skirmish-kiting",
                    commandTag: "tag.kite",
                    unitId: 950,
                    targetEntityId: -1,
                    destX: 0f,
                    destZ: 10f,
                    category: RogueCommandExpansionLoader.Runtime.Ai.AiIntentCategory.SurvivalKite,
                    utilityScore: 160f,
                    lockoutDurationSeconds: 2.0f,
                    requiresEmptyQueue: false));
            });

            var composite = new CompositeAiRuntimeBehaviour(
                profileId: "test:hub-d-profile",
                moduleIds: new[] { "sample:trace-default", "sample:threat-prioritization", "sample:skirmish-kiting" },
                behaviours: new IRogueCommandAiRuntimeBehaviour[] { legacyModule, combatModule, kiteModule });

            composite.OnInitialized();
            composite.Tick();

            // Verify legacy module ticked AND only the winning coordinated proposal (kite move to 0,10) executed on aiUnit
            if (legacyTickCount != 1 ||
                aiUnit.ExecuteCommandCallCount != 1 ||
                !aiUnit.LastExecutedCommand.HasValue ||
                aiUnit.LastExecutedCommand.Value.command != EntityCommand.Command.Move ||
                Math.Abs(aiUnit.LastExecutedCommand.Value.destination.x - 0f) > 0.01f ||
                aiUnit.CurrentCommandTag != "tag.kite" ||
                composite.LastArbitrationResult == null ||
                composite.LastArbitrationResult.ConflictsResolvedCount != 1 ||
                composite.LastArbitrationResult.ApprovedOrders.Count != 1)
            {
                throw new InvalidOperationException("CompositeAiRuntimeBehaviour did not arbitrate coordinated modules into a single winning native command.");
            }

            composite.OnDestroyed();
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("HUB-D Phase 3: TacticalDirectorParameterParser validates defaults, upper bounds, and rejects invalid/duplicate parameters", () =>
    {
        if (!TacticalDirectorParameterParser.TryParse(null, out TacticalDirectorSettings defaults, out string diagnostic) ||
            diagnostic != null ||
            defaults.BaseIndex != -1 ||
            Math.Abs(defaults.EvalIntervalSeconds - 0.5f) > 0.001f ||
            Math.Abs(defaults.EngagementRadius - 40.0f) > 0.001f ||
            Math.Abs(defaults.DefenseRadius - 20.0f) > 0.001f ||
            Math.Abs(defaults.RetreatHealthRatio - 0.30f) > 0.001f ||
            defaults.MaxProposalsPerUnit != 2 ||
            !defaults.EnableSurvivalKiting ||
            !defaults.EnableBaseDefense ||
            !defaults.EnableFocusTargeting)
        {
            throw new InvalidOperationException("TacticalDirectorParameterParser did not return expected defaults.");
        }

        ContentModuleParameter[] customParams =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "evalIntervalSeconds", value = "10.0" },
            new ContentModuleParameter { name = "engagementRadius", value = "120.0" },
            new ContentModuleParameter { name = "defenseRadius", value = "60.0" },
            new ContentModuleParameter { name = "retreatHealthRatio", value = "0.60" },
            new ContentModuleParameter { name = "maxProposalsPerUnit", value = "4" },
            new ContentModuleParameter { name = "enableSurvivalKiting", value = "false" },
            new ContentModuleParameter { name = "enableBaseDefense", value = "true" },
            new ContentModuleParameter { name = "enableFocusTargeting", value = "false" }
        ];

        if (!TacticalDirectorParameterParser.TryParse(customParams, out TacticalDirectorSettings custom, out diagnostic) ||
            diagnostic != null ||
            custom.BaseIndex != 31 ||
            Math.Abs(custom.EvalIntervalSeconds - 10.0f) > 0.001f ||
            Math.Abs(custom.EngagementRadius - 120.0f) > 0.001f ||
            Math.Abs(custom.DefenseRadius - 60.0f) > 0.001f ||
            Math.Abs(custom.RetreatHealthRatio - 0.60f) > 0.001f ||
            custom.MaxProposalsPerUnit != 4 ||
            custom.EnableSurvivalKiting ||
            !custom.EnableBaseDefense ||
            custom.EnableFocusTargeting)
        {
            throw new InvalidOperationException("TacticalDirectorParameterParser rejected valid upper-bound parameters.");
        }

        string[] invalidPairs =
        [
            "baseIndex=32",
            "evalIntervalSeconds=0.05",
            "engagementRadius=4.0",
            "defenseRadius=2.0",
            "retreatHealthRatio=0.95",
            "maxProposalsPerUnit=0",
            "enableSurvivalKiting=invalid",
            "unknownParam=1"
        ];

        foreach (string pair in invalidPairs)
        {
            string[] kv = pair.Split('=');
            var bad = new[] { new ContentModuleParameter { name = kv[0], value = kv[1] } };
            if (TacticalDirectorParameterParser.TryParse(bad, out _, out _))
            {
                throw new InvalidOperationException("TacticalDirectorParameterParser accepted invalid parameter: " + pair);
            }
        }
    }),
    ("HUB-D Phase 3: TacticalDirectorAiModule is registered, coordinates dead-zone kite + combat focus, and populates RuntimeAiArchitectureCensus with omission counters first", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            UnityEngine.Time.time = 50.0f;

            AiModuleRegistry registry = AiModuleRegistry.CreateDefault();
            if (!registry.RegisteredModuleIds.Contains(TacticalDirectorAiModule.ModuleId))
            {
                throw new InvalidOperationException("TacticalDirectorAiModule is not registered in AiModuleRegistry.CreateDefault().");
            }

            RuntimeUnitMechanicsDirector.RegisterProfile(
                "sample-native:test-artillery",
                new UnitRuntimeMechanicsProfile
                {
                    EntityId = "sample-native:test-artillery",
                    MinAttackRange = 12f
                });

            var aiArtillery = new EntityController
            {
                UniqueEntityId = 1101,
                EntityId = "sample-native:test-artillery",
                IsControlledByAi = true,
                IsControlledByPlayer = false,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                Damage1 = 45f,
                Attack1Cooldown = 1.0f,
                WeaponRange = 30f,
                MoveSpeed = 9f,
                Role = UnitRole.Sniper,
                Position = new Vector3(10f, 0f, 10f)
            };

            var aiBrawler = new EntityController
            {
                UniqueEntityId = 1102,
                EntityId = "sample-native:scout-tank",
                IsControlledByAi = true,
                IsControlledByPlayer = false,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 120f,
                MaxHealth = 120f,
                Damage1 = 25f,
                Attack1Cooldown = 1.0f,
                WeaponRange = 14f,
                MoveSpeed = 10f,
                Role = UnitRole.Tank,
                Position = new Vector3(25f, 0f, 10f)
            };

            var encroachingHostile = new EntityController
            {
                UniqueEntityId = 1201,
                EntityId = "player:raider",
                IsControlledByPlayer = true,
                IsControlledByAi = false,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CurrentHealth = 90f,
                MaxHealth = 100f,
                Damage1 = 30f,
                Attack1Cooldown = 1.0f,
                WeaponRange = 10f,
                MoveSpeed = 11f,
                Role = UnitRole.FrontalAttacker,
                Position = new Vector3(14f, 0f, 10f)
            };

            ExistingControllers.Instance.AiUnits().Add(aiArtillery);
            ExistingControllers.Instance.AiUnits().Add(aiBrawler);
            ExistingControllers.Instance.PlayerUnits().Add(encroachingHostile);

            var directorModule = new TacticalDirectorAiModule();
            IRogueCommandAiRuntimeBehaviour directorBehaviour = directorModule.CreateBehaviour(
                new DefaultAiBehaviour { instanceId = 5001 },
                null);

            var composite = new CompositeAiRuntimeBehaviour(
                profileId: "sample-reactive-ai:base-defense",
                moduleIds: new[] { TacticalDirectorAiModule.ModuleId },
                behaviours: new[] { directorBehaviour });

            composite.OnInitialized();
            composite.Tick();

            if (!aiArtillery.LastExecutedCommand.HasValue ||
                aiArtillery.LastExecutedCommand.Value.command != EntityCommand.Command.Move ||
                aiArtillery.LastExecutedCommand.Value.destination.x >= 10f)
            {
                throw new InvalidOperationException("TacticalDirectorAiModule did not dispatch dead-zone kite Move for artillery unit.");
            }

            if (!aiBrawler.LastExecutedCommand.HasValue ||
                aiBrawler.LastExecutedCommand.Value.command != EntityCommand.Command.AttackMoveOnTarget ||
                aiBrawler.LastExecutedCommand.Value.target != encroachingHostile)
            {
                throw new InvalidOperationException("TacticalDirectorAiModule did not dispatch AttackMoveOnTarget for brawler unit.");
            }

            RogueCommandExpansionLoader.Runtime.Ai.AiArchitectureCensus census =
                RogueCommandExpansionLoader.Runtime.Ai.RuntimeAiArchitectureCensus.Current();
            string json = AgentDiagnosticsFormatter.SerializeAiArchitectureCensus(census);

            int omittedIdx = json.IndexOf("\"omittedInstanceCount\"", StringComparison.Ordinal);
            int rowsIdx = json.IndexOf("\"instances\"", StringComparison.Ordinal);
            if (census.ActiveCompositeCount < 1 ||
                census.TotalApprovedOrdersLastTick < 2 ||
                omittedIdx < 0 ||
                rowsIdx < 0 ||
                omittedIdx > rowsIdx)
            {
                throw new InvalidOperationException("RuntimeAiArchitectureCensus or JSON serializer did not report active HUB-D state with omission counters first: " + json);
            }

            composite.OnDestroyed();
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("HUB-D Phase 4: AiHazardZoneSnapshot, AiUnitSnapshot skill readiness, and AiBattlefieldBlackboardBuilder project hazard zones with omission counters first", () =>
    {
        var hazardA = new RogueCommandExpansionLoader.Runtime.Ai.AiHazardZoneSnapshot(
            zoneId: "hazard_1",
            hazardType: "Firestorm",
            centerX: 10f,
            centerZ: 10f,
            radius: 6f,
            tickDamage: 25f,
            isBeneficialToAi: false);

        var hazardB = new RogueCommandExpansionLoader.Runtime.Ai.AiHazardZoneSnapshot(
            zoneId: "hazard_2",
            hazardType: "AcidPool",
            centerX: 30f,
            centerZ: 30f,
            radius: 5f,
            tickDamage: 18f,
            isBeneficialToAi: false);

        if (!hazardA.ContainsPoint(12f, 10f) ||
            hazardA.ContainsPoint(20f, 10f) ||
            hazardA.DistanceToPerimeter(10f, 10f) >= 0f)
        {
            throw new InvalidOperationException("AiHazardZoneSnapshot geometry checks failed.");
        }

        var skillUnit = new RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot(
            uniqueEntityId: 401,
            entityId: "sample-native:skill-caster",
            baseIndex: 0,
            posX: 10f,
            posZ: 10f,
            currentHealth: 90f,
            maxHealth: 100f,
            currentShield: 0f,
            maxShield: 0f,
            dpsEstimate: 30f,
            weaponRange: 14f,
            minAttackRange: 0f,
            moveSpeed: 9f,
            roleFlags: (int)UnitRole.FrontalAttacker,
            isControlledByAi: true,
            isControlledByPlayer: false,
            isCombatUnit: true,
            isHarvester: false,
            isBuilding: false,
            isSupportUnit: false,
            hasQueuedCommands: false,
            currentCommandTag: string.Empty,
            currentTargetId: -1,
            hasReadyActiveSkill: true,
            activeSkillTargetsSelf: false,
            activeSkillRange: 16f);

        if (!skillUnit.HasReadyActiveSkill ||
            skillUnit.ActiveSkillTargetsSelf ||
            Math.Abs(skillUnit.ActiveSkillRange - 16f) > 0.01f)
        {
            throw new InvalidOperationException("AiUnitSnapshot did not preserve active skill readiness properties.");
        }

        var snap = RogueCommandExpansionLoader.Runtime.Ai.AiBattlefieldBlackboardBuilder.BuildSnapshot(
            timestamp: 15f,
            baseIndex: 0,
            currentWaveNumber: 2,
            aiUnits: new[] { skillUnit },
            hostileUnits: Array.Empty<RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot>(),
            hazardZones: new[] { hazardA, hazardB },
            maxTrackedUnitsPerSide: 8,
            maxTrackedHazardZones: 1);

        if (snap.OmittedHazardZoneCount != 1 ||
            snap.TotalHarmfulHazardZones != 2 ||
            snap.HazardZones.Count != 1 ||
            snap.HazardZones[0].ZoneId != "hazard_1")
        {
            throw new InvalidOperationException("AiBattlefieldBlackboardBuilder did not cap and count hazard zones with OmittedHazardZoneCount.");
        }
    }),
    ("HUB-D Phase 5: AiSafeVectorPolicy avoids hazard zones during retreat and TacticalUtilityScorer dampens overkill and scores skills/support", () =>
    {
        var unit = new RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot(
            uniqueEntityId: 501,
            entityId: "sample-native:mortar",
            baseIndex: 0,
            posX: 10f,
            posZ: 10f,
            currentHealth: 85f,
            maxHealth: 100f,
            currentShield: 0f,
            maxShield: 0f,
            dpsEstimate: 40f,
            weaponRange: 26f,
            minAttackRange: 10f,
            moveSpeed: 9f,
            roleFlags: (int)UnitRole.Sniper,
            isControlledByAi: true,
            isControlledByPlayer: false,
            isCombatUnit: true,
            isHarvester: false,
            isBuilding: false,
            isSupportUnit: false,
            hasQueuedCommands: false,
            currentCommandTag: string.Empty,
            currentTargetId: -1,
            hasReadyActiveSkill: true,
            activeSkillTargetsSelf: false,
            activeSkillRange: 18f);

        var threatEast = new RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot(
            uniqueEntityId: 601,
            entityId: "player:raider",
            baseIndex: -1,
            posX: 14f,
            posZ: 10f,
            currentHealth: 40f,
            maxHealth: 100f,
            currentShield: 0f,
            maxShield: 0f,
            dpsEstimate: 35f,
            weaponRange: 10f,
            minAttackRange: 0f,
            moveSpeed: 10f,
            roleFlags: (int)UnitRole.FrontalAttacker,
            isControlledByAi: false,
            isControlledByPlayer: true,
            isCombatUnit: true,
            isHarvester: false,
            isBuilding: false,
            isSupportUnit: false,
            hasQueuedCommands: false,
            currentCommandTag: string.Empty,
            currentTargetId: 501);

        var blockingHazardWest = new RogueCommandExpansionLoader.Runtime.Ai.AiHazardZoneSnapshot(
            zoneId: "hazard_west",
            hazardType: "Firestorm",
            centerX: 0f,
            centerZ: 10f,
            radius: 7f,
            tickDamage: 40f,
            isBeneficialToAi: false);

        var snapshot = RogueCommandExpansionLoader.Runtime.Ai.AiBattlefieldBlackboardBuilder.BuildSnapshot(
            timestamp: 30f,
            baseIndex: 0,
            currentWaveNumber: 3,
            aiUnits: new[] { unit },
            hostileUnits: new[] { threatEast },
            hazardZones: new[] { blockingHazardWest });

        (float safeX, float safeZ) = RogueCommandExpansionLoader.Runtime.Ai.AiSafeVectorPolicy.ComputeSafeRetreatDestination(
            unit,
            threatEast,
            snapshot,
            desiredDistance: 10f);

        if (blockingHazardWest.ContainsPoint(safeX, safeZ, margin: 1.0f))
        {
            throw new InvalidOperationException($"AiSafeVectorPolicy routed unit into blocking Firestorm hazard at ({safeX}, {safeZ}).");
        }

        var brawler = new RogueCommandExpansionLoader.Runtime.Ai.AiUnitSnapshot(
            uniqueEntityId: 502,
            entityId: "sample-native:brawler",
            baseIndex: 0,
            posX: 12f,
            posZ: 12f,
            currentHealth: 100f,
            maxHealth: 100f,
            currentShield: 0f,
            maxShield: 0f,
            dpsEstimate: 35f,
            weaponRange: 14f,
            minAttackRange: 0f,
            moveSpeed: 8f,
            roleFlags: (int)UnitRole.Tank,
            isControlledByAi: true,
            isControlledByPlayer: false,
            isCombatUnit: true,
            isHarvester: false,
            isBuilding: false,
            isSupportUnit: false,
            hasQueuedCommands: false,
            currentCommandTag: string.Empty,
            currentTargetId: -1);

        float freshScore = RogueCommandExpansionLoader.Runtime.Ai.TacticalUtilityScorer.ScoreCombatTargetWithOverkill(
            brawler,
            threatEast,
            RogueCommandExpansionLoader.Runtime.Ai.AiDoctrineWeights.Assault,
            alreadyCommittedDamage: 0f);
        float overkillScore = RogueCommandExpansionLoader.Runtime.Ai.TacticalUtilityScorer.ScoreCombatTargetWithOverkill(
            brawler,
            threatEast,
            RogueCommandExpansionLoader.Runtime.Ai.AiDoctrineWeights.Assault,
            alreadyCommittedDamage: 80f);

        if (overkillScore >= freshScore * 0.5f)
        {
            throw new InvalidOperationException("ScoreCombatTargetWithOverkill did not dampen score when committed damage exceeds target vitality.");
        }
    }),
    ("HUB-D Phase 6: SquadOperationsDirectorAiModule validates parameters and cooperates with TacticalDirectorAiModule on hazard evacuation, skills, and support", () =>
    {
        if (!SquadOperationsDirectorParameterParser.TryParse(null, out SquadOperationsDirectorSettings defaults, out string diag) ||
            diag != null ||
            defaults.BaseIndex != -1 ||
            !defaults.EnableSupportLogistics ||
            !defaults.EnableCounterBattery ||
            !defaults.EnableTacticalSkills)
        {
            throw new InvalidOperationException("SquadOperationsDirectorParameterParser failed default validation.");
        }

        if (SquadOperationsDirectorParameterParser.TryParse(
                new[] { new ContentModuleParameter { name = "supportRadius", value = "999" } },
                out _,
                out _))
        {
            throw new InvalidOperationException("SquadOperationsDirectorParameterParser accepted out-of-range supportRadius.");
        }

        ExistingControllers.Instance.Reset();
        RuntimeHazardManager.ClearAll();
        try
        {
            UnityEngine.Time.time = 80.0f;

            AiModuleRegistry registry = AiModuleRegistry.CreateDefault();
            if (!registry.RegisteredModuleIds.Contains(SquadOperationsDirectorAiModule.ModuleId))
            {
                throw new InvalidOperationException("SquadOperationsDirectorAiModule is not registered in AiModuleRegistry.CreateDefault().");
            }

            RuntimeHazardManager.TrySpawnHazardZone("AcidPool", 10f, 10f, 6f, 30f, 25f, out _);

            var aiTrappedInHazard = new EntityController
            {
                UniqueEntityId = 2101,
                EntityId = "sample-native:scout-tank",
                IsControlledByAi = true,
                IsControlledByPlayer = false,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 95f,
                MaxHealth = 100f,
                Damage1 = 20f,
                Attack1Cooldown = 1.0f,
                WeaponRange = 14f,
                MoveSpeed = 10f,
                Role = UnitRole.Tank,
                Position = new Vector3(11f, 0f, 10f)
            };

            var aiEngineer = new EntityController
            {
                UniqueEntityId = 2102,
                EntityId = "sample-native:support-rig",
                IsControlledByAi = true,
                IsControlledByPlayer = false,
                IsUnit = true,
                IsCombatUnit = false,
                IsEngineer = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 100f,
                MaxHealth = 100f,
                MoveSpeed = 9f,
                Role = UnitRole.Supporter,
                Position = new Vector3(30f, 0f, 30f)
            };

            var damagedHarvester = new EntityController
            {
                UniqueEntityId = 2103,
                EntityId = "sample-native:harvester",
                IsControlledByAi = true,
                IsControlledByPlayer = false,
                IsUnit = true,
                IsCombatUnit = false,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 35f,
                MaxHealth = 100f,
                MoveSpeed = 6f,
                Role = UnitRole.Harvester,
                Position = new Vector3(38f, 0f, 30f)
            };

            var distantHostile = new EntityController
            {
                UniqueEntityId = 2201,
                EntityId = "player:sniper",
                IsControlledByPlayer = true,
                IsControlledByAi = false,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CurrentHealth = 80f,
                MaxHealth = 80f,
                Damage1 = 30f,
                Attack1Cooldown = 1.0f,
                WeaponRange = 22f,
                MoveSpeed = 7f,
                Role = UnitRole.Sniper,
                Position = new Vector3(28f, 0f, 10f)
            };

            ExistingControllers.Instance.AiUnits().Add(aiTrappedInHazard);
            ExistingControllers.Instance.AiUnits().Add(aiEngineer);
            ExistingControllers.Instance.AiUnits().Add(damagedHarvester);
            ExistingControllers.Instance.PlayerUnits().Add(distantHostile);

            var tacticalDirector = new TacticalDirectorAiModule().CreateBehaviour(
                new DefaultAiBehaviour { instanceId = 6001 },
                null);
            var squadDirector = new SquadOperationsDirectorAiModule().CreateBehaviour(
                new DefaultAiBehaviour { instanceId = 6001 },
                null);

            var composite = new CompositeAiRuntimeBehaviour(
                profileId: "sample-reactive-ai:combined-hubd2",
                moduleIds: new[] { TacticalDirectorAiModule.ModuleId, SquadOperationsDirectorAiModule.ModuleId },
                behaviours: new[] { tacticalDirector, squadDirector });

            composite.OnInitialized();
            composite.Tick();

            if (!aiTrappedInHazard.LastExecutedCommand.HasValue ||
                aiTrappedInHazard.LastExecutedCommand.Value.command != EntityCommand.Command.Move)
            {
                throw new InvalidOperationException("TacticalDirectorAiModule did not evacuate unit trapped inside AcidPool hazard.");
            }

            if (!aiEngineer.LastExecutedCommand.HasValue ||
                aiEngineer.LastExecutedCommand.Value.command != EntityCommand.Command.Move)
            {
                throw new InvalidOperationException("SquadOperationsDirectorAiModule did not dispatch support engineer to damaged harvester.");
            }

            RogueCommandExpansionLoader.Runtime.Ai.AiArchitectureCensus census =
                RogueCommandExpansionLoader.Runtime.Ai.RuntimeAiArchitectureCensus.Current();
            if (census.TotalTrackedHazardZones < 1)
            {
                throw new InvalidOperationException("RuntimeAiArchitectureCensus did not report TrackedHazardZones.");
            }

            composite.OnDestroyed();
        }
        finally
        {
            RuntimeHazardManager.ClearAll();
            ExistingControllers.Instance.Reset();
        }
    }),
    };

    private sealed class DelegatingTestAiRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
    {
        private readonly Action _onTick;
        public DelegatingTestAiRuntimeBehaviour(Action onTick) => _onTick = onTick;
        public void OnInitialized() { }
        public void Tick() => _onTick();
        public void OnDestroyed() { }
    }

    private sealed class CoordinatedTestAiRuntimeBehaviour : RogueCommandExpansionLoader.Contracts.ICoordinatedAiRuntimeBehaviour
    {
        private readonly Action<RogueCommandExpansionLoader.Runtime.Ai.AiBattlefieldSnapshot, RogueCommandExpansionLoader.Runtime.Ai.AiDoctrineWeights, IList<RogueCommandExpansionLoader.Runtime.Ai.AiIntentProposal>> _propose;

        public CoordinatedTestAiRuntimeBehaviour(
            Action<RogueCommandExpansionLoader.Runtime.Ai.AiBattlefieldSnapshot, RogueCommandExpansionLoader.Runtime.Ai.AiDoctrineWeights, IList<RogueCommandExpansionLoader.Runtime.Ai.AiIntentProposal>> propose)
        {
            _propose = propose;
        }

        public void OnInitialized() { }
        public void Tick() { }
        public void OnDestroyed() { }

        public void CollectProposals(
            RogueCommandExpansionLoader.Runtime.Ai.AiBattlefieldSnapshot snapshot,
            RogueCommandExpansionLoader.Runtime.Ai.AiDoctrineWeights doctrineWeights,
            IList<RogueCommandExpansionLoader.Runtime.Ai.AiIntentProposal> proposals)
        {
            _propose(snapshot, doctrineWeights, proposals);
        }
    }
}
