using System;
using System.Reflection;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Provides functionality to mutate existing game object instances at runtime.
    /// </summary>
    internal sealed class RuntimeMutationManager
    {
        private readonly Action<string> _log;

        public RuntimeMutationManager(Action<string> logger)
        {
            _log = logger ?? (msg => { });
        }

        /// <summary>
        /// Mutates a field on a target object.
        /// </summary>
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

        /// <summary>
        /// Specifically mutates a float value on an EntityValueModifier or similar object.
        /// </summary>
        public bool MutateFloat(object target, string fieldName, float newValue)
        {
            return MutateField(target, fieldName, newValue);
        }

        /// <summary>
        /// Mutates an AI Behaviour instance held by an AiBehaviourManager.
        /// </summary>
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
