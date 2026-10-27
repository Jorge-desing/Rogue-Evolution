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
                MoveSpeed = 12f,
                WeaponRange = 20f,
                Position = new Vector3(10f, 0f, 10f)
            };
            var closePlayerEnemy = new EntityController
            {
                IsControlledByPlayer = true,
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
                IsEngineer = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            var damagedHarvester = new EntityController
            {
                IsControlledByAi = true,
                Role = UnitRole.Harvester,
                MaxHealth = 100f,
                CurrentHealth = 40f,
                Position = new Vector3(20f, 0f, 0f)
            };
            var damagedUnit = new EntityController
            {
                IsControlledByAi = true,
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
                IsEngineer = true,
                CurrentPlayerCommandName = EntityCommand.Command.Attack,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            var idleRepairer = new EntityController
            {
                IsControlledByAi = true,
                IsEngineer = true,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                Position = new UnityEngine.Vector3(1f, 0f, 0f)
            };
            var damagedHarvester = new EntityController
            {
                IsControlledByAi = true,
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
    };
}
