using System;
using System.Collections.Generic;
using System.Reflection;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Provides functionality to mutate existing game object instances at runtime.
    /// </summary>
    public sealed class RuntimeMutationManager
    {
        private readonly Action<string> _log;
        private readonly Dictionary<Type, Dictionary<string, FieldInfo>> _fieldCache =
            new Dictionary<Type, Dictionary<string, FieldInfo>>();

        public RuntimeMutationManager(Action<string> logger)
        {
            _log = logger ?? (msg => { });
        }

        private FieldInfo GetCachedField(Type type, string fieldName)
        {
            Dictionary<string, FieldInfo> typeCache;
            if (!_fieldCache.TryGetValue(type, out typeCache))
            {
                typeCache = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                _fieldCache[type] = typeCache;
            }

            FieldInfo field;
            if (typeCache.TryGetValue(fieldName, out field))
            {
                return field;
            }

            field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            typeCache[fieldName] = field;
            return field;
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
                FieldInfo field = GetCachedField(type, fieldName);

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
                Type managerType = manager.GetType();
                FieldInfo activeBehaviourField = GetCachedField(managerType, "activeBehaviour");

                if (activeBehaviourField == null)
                {
                    foreach (var altName in new[] { "m_activeBehaviour", "_activeBehaviour", "activeBehaviourInstance", "behaviour" })
                    {
                        activeBehaviourField = GetCachedField(managerType, altName);
                        if (activeBehaviourField != null) break;
                    }
                }

                if (activeBehaviourField == null)
                {
                    _log("Mutation failed: AiBehaviourManager active behaviour field not found.");
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
