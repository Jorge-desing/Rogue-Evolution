using System;
using System.Reflection;
using RogueCommandExpansionLoader.Runtime;

namespace RogueCommandExpansionLoader.Runtime
{
    internal sealed class RuntimeMutationManager
    {
        private readonly Action<string> _log;

        public RuntimeMutationManager(Action<string> logger)
        {
            _log = logger ?? (msg => { });
        }

        public bool MutateField(object target, string fieldName, object newValue)
        {
            if (target == null)
            {
                _log("Mutation failed: target object is null.");
                return false;
            }

            try
            {
                Type type = target.GetType();
                FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                if (field == null)
                {
                    _log($"Mutation failed: field '{fieldName}' not found on type {type.Name}.");
                    return false;
                }

                field.SetValue(target, newValue);
                return true;
            }
            catch (Exception ex)
            {
                _log($"Mutation failed for field '{fieldName}' on {target.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public bool MutateFloat(object target, string fieldName, float newValue)
        {
            return MutateField(target, fieldName, newValue);
        }

        public bool MutateActiveAiBehaviour(object manager, string fieldName, object newValue)
        {
            try
            {
                FieldInfo activeBehaviourField = manager.GetType().GetField("activeBehaviour", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (activeBehaviourField == null)
                {
                    _log("Mutation failed: AiBehaviourManager.activeBehaviour field not found.");
                    return false;
                }

                object activeBehaviour = activeBehaviourField.GetValue(manager);
                if (activeBehaviour == null)
                {
                    _log("Mutation failed: activeBehaviour is null.");
                    return false;
                }

                return MutateField(activeBehaviour, fieldName, newValue);
            }
            catch (Exception ex)
            {
                _log($"Mutation failed for active AI behaviour: {ex.Message}");
                return false;
            }
        }
    }
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

public sealed class AiBehaviourManager
{
    public AiBehaviour? activeBehaviour;
}

class Program
{
    static void Main()
    {
        Console.WriteLine("Starting Isolated RuntimeMutationManager verification...");
        var mutationManager = new RuntimeMutationManager(msg => Console.WriteLine($"[LOG] {msg}"));
        
        // Test Buff Mutation
        Console.WriteLine("Testing Buff Mutation...");
        var buff = new EntityValueModifier { value = 1.0f };
        bool buffSuccess = mutationManager.MutateFloat(buff, "value", 2.5f);
        if (!buffSuccess || buff.value != 2.5f) 
        {
            throw new Exception($"Buff value mutation failed. Success: {buffSuccess}, Value: {buff.value}");
        }
        Console.WriteLine("Buff Mutation: PASSED");

        // Test AI Behaviour Mutation
        Console.WriteLine("Testing AI Behaviour Mutation...");
        var behaviour = new AiBehaviour { tickRate = 1.0f, detectionRange = 10.0f };
        var manager = new AiBehaviourManager { activeBehaviour = behaviour };
        
        bool tickSuccess = mutationManager.MutateActiveAiBehaviour(manager, "tickRate", 0.5f);
        if (!tickSuccess || behaviour.tickRate != 0.5f) 
        {
            throw new Exception($"AI tickRate mutation failed. Success: {tickSuccess}, Value: {behaviour.tickRate}");
        }
        
        bool rangeSuccess = mutationManager.MutateActiveAiBehaviour(manager, "detectionRange", 20.0f);
        if (!rangeSuccess || behaviour.detectionRange != 20.0f) 
        {
            throw new Exception($"AI detectionRange mutation failed. Success: {rangeSuccess}, Value: {behaviour.detectionRange}");
        }
        Console.WriteLine("AI Behaviour Mutation: PASSED");

        Console.WriteLine("All verifications PASSED.");
    }
}
