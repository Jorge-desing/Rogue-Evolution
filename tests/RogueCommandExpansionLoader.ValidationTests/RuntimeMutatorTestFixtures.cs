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
}

public sealed class EntityControllerIdentityTestController
{
    public string entityId;
}

public static class EntityBalancingStore
{
    public static List<EntityBalancingParameters> EntityBalancingParametersList = new();
    public static Dictionary<string, int> ParameterListIndexOf = new(StringComparer.Ordinal);
    public static Dictionary<string, string> FactoryEntityIdOf = new(StringComparer.Ordinal);

    public static void Reset(IEqualityComparer<string> indexComparer = null)
    {
        EntityBalancingParametersList.Clear();
        ParameterListIndexOf = new Dictionary<string, int>(indexComparer ?? StringComparer.Ordinal);
        FactoryEntityIdOf.Clear();
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

public sealed class StageParametersScriptableObject
{
}

public sealed class DefaultAiBehaviour
{
}

internal sealed class RuntimeMutatorTestProvider : IRuntimeContentProvider
{
    public List<object> EntityRows { get; } = new();
    public List<ProgressionContentContribution> Contributions { get; } = new();
    public int EntityRowsRegisteredCount { get; private set; }

    public string TargetGameVersion => "1.0.9";

    public void OnEntityRegistryInitialized()
    {
    }

    public bool PrepareContentForRuntime() => true;

    public void OnEntityRowsRegistered() => EntityRowsRegisteredCount++;

    public IEnumerable<object> GetEntityParameterRows() => EntityRows;

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

internal sealed class ThrowOnFirstProductHashComparer : IEqualityComparer<string>
{
    private readonly string _targetId;

    public ThrowOnFirstProductHashComparer(string targetId)
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
