# Plan: Add `sample:harvester-escort` compiled AI module

## Goal

Add a new compiled, bounded AI module that protects AI harvesters by dispatching idle combat units from the configured AI base, while preserving the existing command-tag, capacity, and cleanup invariants.

## Current context / assumptions

- The loader already supports compiled modules through `IRogueCommandAiModule`, `AiModuleRegistry`, and `CompositeAiRuntimeBehaviour`.
- Existing modules such as `HarvesterRaiderAiModule` and `RetreatAndRegroupAiModule` provide the parser/runtime pattern to copy.
- `ExistingControllers.Instance.Harvesters` and `ExistingControllers.Instance.AiUnits()` are the correct runtime sources for harvesters and AI combat units.
- `EntityCommand.AttackMoveOnPosition(...)` is the safe dispatch primitive used by other modules.
- The new module must be registered in `RogueCommandExpansionPlugin.Awake()` before packs are loaded.
- Tests are in `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs` and currently validate parsers, not live Unity behavior.

## Architecture / proposed approach

Create a parameter parser and module class following the existing `RetreatAndRegroup*` shape, then implement a timer-gated runtime behaviour that finds the nearest threatened AI harvester, selects up to `maxEscorts` untagged combat units from the configured base, and issues tagged attack-move commands. Register the module and add parser tests before implementation, following TDD.

## Step-by-step tasks

### Task 1: Create the parameter contract

**File:** `src/RogueCommandExpansionLoader/Modules/HarvesterEscortParameters.cs`

Create:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct HarvesterEscortSettings
    {
        public int BaseIndex;
        public int EscortRadiusCells;
        public float ScanIntervalSeconds;
        public int MaxEscorts;
        public bool EngageAttackers;
    }

    internal static class HarvesterEscortParameterParser
    {
        internal const string ModuleId = "sample:harvester-escort";

        private const int DefaultBaseIndex = 0;
        private const int DefaultEscortRadiusCells = 8;
        private const float DefaultScanIntervalSeconds = 5f;
        private const int DefaultMaxEscorts = 3;
        private const bool DefaultEngageAttackers = true;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out HarvesterEscortSettings settings,
            out string diagnostic)
        {
            settings = default(HarvesterEscortSettings);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            parameters = parameters ?? new ContentModuleParameter[0];

            for (int i = 0; i < parameters.Length; i++)
            {
                ContentModuleParameter parameter = parameters[i];
                if (parameter == null || parameter.name == null || parameter.value == null)
                {
                    diagnostic = "Module parameters cannot be null.";
                    return false;
                }

                if (parameter.name != "baseIndex" &&
                    parameter.name != "escortRadiusCells" &&
                    parameter.name != "scanIntervalSeconds" &&
                    parameter.name != "maxEscorts" &&
                    parameter.name != "engageAttackers")
                {
                    diagnostic = "Unknown parameter '" + parameter.name + "' for module " + ModuleId + ".";
                    return false;
                }

                if (values.ContainsKey(parameter.name))
                {
                    diagnostic = "Parameter '" + parameter.name + "' may only be supplied once.";
                    return false;
                }

                values.Add(parameter.name, parameter.value);
            }

            int baseIndex;
            if (!TryGetInteger(values, "baseIndex", DefaultBaseIndex, 0, 31, out baseIndex))
            {
                diagnostic = "baseIndex must be an integer between 0 and 31.";
                return false;
            }

            int escortRadiusCells;
            if (!TryGetInteger(values, "escortRadiusCells", DefaultEscortRadiusCells, 1, 64, out escortRadiusCells))
            {
                diagnostic = "escortRadiusCells must be an integer between 1 and 64.";
                return false;
            }

            float scanIntervalSeconds;
            if (!TryGetFloat(values, "scanIntervalSeconds", DefaultScanIntervalSeconds, 1f, 60f, out scanIntervalSeconds))
            {
                diagnostic = "scanIntervalSeconds must be a finite number between 1 and 60.";
                return false;
            }

            int maxEscorts;
            if (!TryGetInteger(values, "maxEscorts", DefaultMaxEscorts, 1, 16, out maxEscorts))
            {
                diagnostic = "maxEscorts must be an integer between 1 and 16.";
                return false;
            }

            bool engageAttackers;
            if (!TryGetBoolean(values, "engageAttackers", DefaultEngageAttackers, out engageAttackers))
            {
                diagnostic = "engageAttackers must be true or false.";
                return false;
            }

            settings = new HarvesterEscortSettings
            {
                BaseIndex = baseIndex,
                EscortRadiusCells = escortRadiusCells,
                ScanIntervalSeconds = scanIntervalSeconds,
                MaxEscorts = maxEscorts,
                EngageAttackers = engageAttackers
            };
            diagnostic = null;
            return true;
        }

        private static bool TryGetInteger(
            Dictionary<string, string> values,
            string name,
            int defaultValue,
            int minimum,
            int maximum,
            out int result)
        {
            string value;
            if (!values.TryGetValue(name, out value))
            {
                result = defaultValue;
                return true;
            }

            return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) &&
                result >= minimum && result <= maximum;
        }

        private static bool TryGetFloat(
            Dictionary<string, string> values,
            string name,
            float defaultValue,
            float minimum,
            float maximum,
            out float result)
        {
            string value;
            if (!values.TryGetValue(name, out value))
            {
                result = defaultValue;
                return true;
            }

            return Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                !Single.IsNaN(result) && !Single.IsInfinity(result) &&
                result >= minimum && result <= maximum;
        }

        private static bool TryGetBoolean(
            Dictionary<string, string> values,
            string name,
            bool defaultValue,
            out bool result)
        {
            string value;
            if (!values.TryGetValue(name, out value))
            {
                result = defaultValue;
                return true;
            }

            if (String.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (String.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = default(bool);
            return false;
        }
    }
}
```

### Task 2: Add failing parser tests

**File:** `tests/RogueCommandExpansionLoader.ValidationTests/Program.cs`

Add after the existing retreat-and-regroup parser tests:

```csharp
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
```

**Verification command:**

```powershell
dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .
```

Expected before implementation: compilation failure because `HarvesterEscortParameterParser` does not exist yet.

### Task 3: Implement the module

**File:** `src/RogueCommandExpansionLoader/Modules/HarvesterEscortAiModule.cs`

Create the module and runtime behaviour:

```csharp
using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    public sealed class HarvesterEscortAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = HarvesterEscortParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            HarvesterEscortSettings settings;
            return HarvesterEscortParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            HarvesterEscortSettings settings;
            string diagnostic;
            if (!HarvesterEscortParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new HarvesterEscortRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class HarvesterEscortRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const string CommandTagPrefix = "roguecommand.expansionloader.harvester-escort.";
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly HarvesterEscortSettings _settings;
            private string _commandTag;
            private float _nextScanAt;
            private bool _stopped;

            public HarvesterEscortRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                HarvesterEscortSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _commandTag = CommandTagPrefix + _gameBehaviour.GetInstanceID();
                _nextScanAt = Time.time;
                Debug.Log("[RogueCommandExpansionLoader] Harvester-escort AI attached to '" +
                    _gameBehaviour.gameObject.name + "' for base " + _settings.BaseIndex + ".");
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }

                float now = Time.time;
                if (now < _nextScanAt)
                {
                    return;
                }

                _nextScanAt = now + _settings.ScanIntervalSeconds;

                EntityController threatenedHarvester;
                EntityController attacker;
                if (!TryFindThreat(out threatenedHarvester, out attacker))
                {
                    return;
                }

                List<EntityController> escorts = SelectEscorts(threatenedHarvester.Position, attacker.Position);
                if (escorts.Count == 0)
                {
                    return;
                }

                Vector3 destination = _settings.EngageAttackers && attacker != null
                    ? attacker.Position
                    : threatenedHarvester.Position;

                int dispatched = 0;
                for (int i = 0; i < escorts.Count; i++)
                {
                    EntityController escort = escorts[i];
                    EntityCommand command = EntityCommand.AttackMoveOnPosition(
                        destination,
                        EntityCommand.Role.None,
                        "Harvester escort");

                    command.tag = _commandTag;
                    try
                    {
                        escort.ExecuteCommand(command,
                            EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                        dispatched++;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[RogueCommandExpansionLoader] Harvester escort could not dispatch '" +
                            escort.gameObject.name + "': " + exception.GetType().Name + ": " +
                            exception.Message);
                    }
                }

                if (dispatched > 0)
                {
                    Debug.Log("[RogueCommandExpansionLoader] AI base " + _settings.BaseIndex +
                        " dispatched " + dispatched + " harvester escorts toward " + destination + ".");
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _commandTag = null;
            }

            private bool TryFindThreat(out EntityController harvester, out EntityController attacker)
            {
                harvester = null;
                attacker = null;
                float bestDistance = float.MaxValue;

                HashSet<EntityController> harvesters = ExistingControllers.Instance.Harvesters;
                if (harvesters == null || harvesters.Count == 0)
                {
                    return false;
                }

                foreach (EntityController candidateHarvester in harvesters)
                {
                    if (candidateHarvester == null ||
                        candidateHarvester.IsDestroyed ||
                        candidateHarvester.DestroyHasBeenStarted ||
                        !candidateHarvester.IsControlledByAi ||
                        !candidateHarvester.IsUnit ||
                        candidateHarvester.aiBaseIndex != _settings.BaseIndex ||
                        candidateHarvester.HasRole(UnitRole.Harvester) == false)
                    {
                        continue;
                    }

                    foreach (EntityController enemy in ExistingControllers.Instance.PlayerUnits)
                    {
                        if (enemy == null ||
                            enemy.IsDestroyed ||
                            enemy.DestroyHasBeenStarted ||
                            !enemy.IsControlledByPlayer ||
                            !enemy.IsUnit ||
                            enemy.canNotBeAttacked ||
                            enemy.canNotBeTargetedByAi ||
                            enemy.doNotRegisterGlobally)
                        {
                            continue;
                        }

                        float distance = Vector3.Distance(candidateHarvester.Position, enemy.Position);
                        if (distance <= _settings.EscortRadiusCells && distance < bestDistance)
                        {
                            bestDistance = distance;
                            harvester = candidateHarvester;
                            attacker = enemy;
                        }
                    }
                }

                return harvester != null && attacker != null;
            }

            private List<EntityController> SelectEscorts(Vector3 harvesterPosition, Vector3 attackerPosition)
            {
                var escorts = new List<EntityController>();
                HashSet<EntityController> aiUnits = ExistingControllers.Instance.AiUnits();
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return escorts;
                }

                var candidates = new List<EntityController>();
                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null ||
                        unit.IsDestroyed ||
                        unit.DestroyHasBeenStarted ||
                        !unit.IsControlledByAi ||
                        !unit.IsUnit ||
                        unit.aiBaseIndex != _settings.BaseIndex ||
                        !unit.CanAttackAndIsAttackEnabled ||
                        !unit.CanMoveAndMovementIsEnabled ||
                        unit.HasRole(UnitRole.Harvester) ||
                        unit.HasRole(UnitRole.Refinery) ||
                        unit.HasRole(UnitRole.Engineer) ||
                        unit.HasRole(UnitRole.Builder))
                    {
                        continue;
                    }

                    if (unit.CurrentCommandIsTaggedWith(_commandTag))
                    {
                        continue;
                    }

                    candidates.Add(unit);
                }

                Vector3 reference = _settings.EngageAttackers ? attackerPosition : harvesterPosition;
                candidates.Sort((a, b) =>
                {
                    float distanceA = Vector3.Distance(a.Position, reference);
                    float distanceB = Vector3.Distance(b.Position, reference);
                    return distanceA.CompareTo(distanceB);
                });

                int take = Math.Min(_settings.MaxEscorts, candidates.Count);
                for (int i = 0; i < take; i++)
                {
                    escorts.Add(candidates[i]);
                }

                return escorts;
            }
        }
    }
}
```

### Task 4: Register the module

**File:** `src/RogueCommandExpansionLoader/RogueCommandExpansionPlugin.cs`

Add after `modules.Register(new RetreatAndRegroupAiModule());`:

```csharp
modules.Register(new HarvesterEscortAiModule());
```

### Task 5: Verify build and tests

**Command:**

```powershell
dotnet run --project tests/RogueCommandExpansionLoader.ValidationTests -- .
```

Expected: all existing checks pass, plus the two new harvester-escort parser checks; only the known Windows symlink-privilege skip should remain.

**Command:**

```powershell
dotnet build src/RogueCommandExpansionLoader/RogueCommandExpansionLoader.csproj -c Release
```

Expected: build succeeds with 0 warnings and 0 errors.

### Task 6: Document the module

**File:** `content/schema/README.md`

Add to the AI module list:

```text
`sample:harvester-escort` (`HarvesterEscortAiModule.cs`) watches AI harvesters from the configured base, selects the nearest threatened harvester, and dispatches up to `maxEscorts` idle combat units with tagged attack-move commands. Parameters: `baseIndex` 0–31 (default 0), `escortRadiusCells` 1–64 (default 8), `scanIntervalSeconds` 1–60 (default 5), `maxEscorts` 1–16 (default 3), and `engageAttackers` (default true).
```

### Task 7: Commit and push

```powershell
git add src/RogueCommandExpansionLoader/Modules/HarvesterEscortParameters.cs src/RogueCommandExpansionLoader/Modules/HarvesterEscortAiModule.cs src/RogueCommandExpansionLoader/RogueCommandExpansionPlugin.cs tests/RogueCommandExpansionLoader.ValidationTests/Program.cs content/schema/README.md
git commit -m "feat(ai): add harvester-escort compiled module"
git push origin codex/rogue-unit-balance-20261018
```

Expected: commit succeeds and remote branch advances.

## Risks, tradeoffs, and open questions

- `ExistingControllers.Instance.Harvesters` may include units that are not harvesters if native tags are inconsistent; the implementation additionally requires `HasRole(UnitRole.Harvester)` for candidate harvesters.
- The behaviour treats `escortRadiusCells` as world units, matching other modules, but the exact native cell size is not verified; this is static implementation, not runtime proof.
- No Steam run validates dispatch, command tagging, or cleanup for this module.
- The module does not add a native AI deck; it only reuses units already present for the configured base.
- If `EngageAttackers` is false, escorts move to the harvester position rather than the attacker, which may be less effective but avoids overcommitting.
- The parser test count in the handoff may need updating after implementation.