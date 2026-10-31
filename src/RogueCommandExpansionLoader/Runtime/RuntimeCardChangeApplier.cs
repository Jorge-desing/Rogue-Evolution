using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure data describing a single in-game card change request. This mirrors the
    /// fields of the game's <c>CardChangeScriptableObject</c> so that mod-authored
    /// stat deltas can be routed through the game's native data-driven card-change
    /// pipeline (<c>EntityBalancingStore.AddInGameCardChange</c>) instead of
    /// custom Harmony patches, per the project's mutator/relic policy.
    /// </summary>
    public struct CardChangeRequest
    {
        public string ValueToChange { get; set; }
        public string Operation { get; set; }
        public float Value { get; set; }
        public bool MultiplyValueWithCurrentHeatLevel { get; set; }
        public bool MultiplyValueWithMetaProgressionMultiplier { get; set; }
        public string Side { get; set; }
        public bool ChangeableValueHasToBeGreaterZero { get; set; }
        public string[] OnlyForTheseEntityIds { get; set; }
        public string[] EntityIdsNotAllowed { get; set; }
        public string SourceId { get; set; }

        public static CardChangeRequest Create(
            string valueToChange,
            string operation,
            float value,
            bool multiplyValueWithCurrentHeatLevel = false,
            bool multiplyValueWithMetaProgressionMultiplier = false,
            string side = "Friendly",
            bool changeableValueHasToBeGreaterZero = false,
            string[] onlyForTheseEntityIds = null,
            string[] entityIdsNotAllowed = null,
            string sourceId = "rcl-card-change")
        {
            return new CardChangeRequest
            {
                ValueToChange = valueToChange ?? string.Empty,
                Operation = string.IsNullOrWhiteSpace(operation) ? "Add" : operation,
                Value = value,
                MultiplyValueWithCurrentHeatLevel = multiplyValueWithCurrentHeatLevel,
                MultiplyValueWithMetaProgressionMultiplier = multiplyValueWithMetaProgressionMultiplier,
                Side = string.IsNullOrWhiteSpace(side) ? "Friendly" : side,
                ChangeableValueHasToBeGreaterZero = changeableValueHasToBeGreaterZero,
                OnlyForTheseEntityIds = onlyForTheseEntityIds ?? new string[0],
                EntityIdsNotAllowed = entityIdsNotAllowed ?? new string[0],
                SourceId = string.IsNullOrWhiteSpace(sourceId) ? "rcl-card-change" : sourceId
            };
        }
    }

    /// <summary>
    /// Guarded runtime applier that routes a <see cref="CardChangeRequest"/> through
    /// the game's native <c>EntityBalancingStore.AddInGameCardChange</c> pipeline.
    /// All reflection is resolved lazily and failures degrade to no-ops so that
    /// diagnostics and tests never throw when the game assembly is unavailable.
    /// </summary>
    public static class RuntimeCardChangeApplier
    {
        private static bool _reflectionResolved;
        private static Type _entityBalancingStoreType;
        private static MethodInfo _addInGameCardChangeMethod;
        private static Type _cardChangeScriptableObjectType;
        private static Type _cardIdType;
        private static ConstructorInfo _cardIdConstructor;
        private static object _cardIdBlueprintValue;
        private static Type _changeableValueType;
        private static Type _changeOperationType;
        private static Type _changeSideType;
        private static readonly BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags StaticFields =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static void EnsureReflection(Assembly gameAssembly)
        {
            if (_reflectionResolved)
            {
                return;
            }
            _reflectionResolved = true;

            try
            {
                if (gameAssembly == null)
                {
                    return;
                }

                _entityBalancingStoreType = gameAssembly.GetType("EntityBalancingStore", false);
                if (_entityBalancingStoreType == null)
                {
                    return;
                }

                _changeableValueType = _entityBalancingStoreType.GetNestedType("ChangeableValue", InstanceFields)
                    ?? _entityBalancingStoreType.GetNestedType("ChangeableValue", StaticFields)
                    ?? gameAssembly.GetType("EntityBalancingStore+ChangeableValue", false)
                    ?? gameAssembly.GetType("EntityController+ChangeableValue", false);

                _cardChangeScriptableObjectType = gameAssembly.GetType("CardChangeScriptableObject", false);
                if (_cardChangeScriptableObjectType != null)
                {
                    _changeOperationType = _cardChangeScriptableObjectType.GetNestedType("ChangeOperation", InstanceFields)
                        ?? _cardChangeScriptableObjectType.GetNestedType("ChangeOperation", StaticFields)
                        ?? gameAssembly.GetType("CardChangeScriptableObject+ChangeOperation", false);
                    _changeSideType = _cardChangeScriptableObjectType.GetNestedType("ChangeSide", InstanceFields)
                        ?? _cardChangeScriptableObjectType.GetNestedType("ChangeSide", StaticFields)
                        ?? gameAssembly.GetType("CardChangeScriptableObject+ChangeSide", false);
                }

                _cardIdType = gameAssembly.GetType("CardId", false);
                if (_cardIdType != null)
                {
                    Type cardTypeNested = _cardIdType.GetNestedType("CardType", InstanceFields)
                        ?? _cardIdType.GetNestedType("CardType", StaticFields);
                    if (cardTypeNested != null && cardTypeNested.IsEnum)
                    {
                        try
                        {
                            _cardIdBlueprintValue = Enum.Parse(cardTypeNested, "Blueprint", true);
                        }
                        catch
                        {
                            string[] names = Enum.GetNames(cardTypeNested);
                            if (names != null && names.Length > 0)
                            {
                                _cardIdBlueprintValue = Enum.Parse(cardTypeNested, names[0], true);
                            }
                        }
                    }

                    if (_cardIdBlueprintValue != null)
                    {
                        ConstructorInfo[] constructors = _cardIdType.GetConstructors();
                        for (int i = 0; i < constructors.Length; i++)
                        {
                            ParameterInfo[] parameters = constructors[i].GetParameters();
                            if (parameters.Length == 2 &&
                                parameters[0].ParameterType.IsEnum &&
                                parameters[1].ParameterType == typeof(string))
                            {
                                _cardIdConstructor = constructors[i];
                                break;
                            }
                        }
                    }
                }

                if (_entityBalancingStoreType != null)
                {
                    MethodInfo[] methods = _entityBalancingStoreType.GetMethods(StaticFields);
                    for (int i = 0; i < methods.Length; i++)
                    {
                        if (!string.Equals(methods[i].Name, "AddInGameCardChange", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        ParameterInfo[] parameters = methods[i].GetParameters();
                        if (parameters.Length == 3 &&
                            parameters[0].ParameterType == typeof(int) &&
                            _cardChangeScriptableObjectType != null &&
                            parameters[1].ParameterType == _cardChangeScriptableObjectType &&
                            _cardIdType != null &&
                            parameters[2].ParameterType == _cardIdType)
                        {
                            _addInGameCardChangeMethod = methods[i];
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Guarded: reflection failures degrade to no-ops.
            }
        }

        public static bool IsAvailable(Assembly gameAssembly)
        {
            EnsureReflection(gameAssembly);
            return _addInGameCardChangeMethod != null &&
                   _cardChangeScriptableObjectType != null &&
                   _cardIdConstructor != null &&
                   _cardIdBlueprintValue != null;
        }

        /// <summary>
        /// Applies a card change to the entity identified by <paramref name="uniqueEntityId"/>.
        /// Returns true when the native pipeline accepted the request.
        /// </summary>
        public static bool ApplyCardChange(Assembly gameAssembly, int uniqueEntityId, CardChangeRequest request)
        {
            EnsureReflection(gameAssembly);
            if (_addInGameCardChangeMethod == null ||
                _cardChangeScriptableObjectType == null ||
                _cardIdConstructor == null ||
                _cardIdBlueprintValue == null ||
                uniqueEntityId <= 0)
            {
                return false;
            }

            try
            {
                object cardChange = ScriptableObject.CreateInstance(_cardChangeScriptableObjectType);
                if (cardChange == null)
                {
                    return false;
                }

                SetEnumField(cardChange, "valueToChange", _changeableValueType, request.ValueToChange);
                SetEnumField(cardChange, "operation", _changeOperationType, request.Operation);
                SetField(cardChange, "value", request.Value);
                SetField(cardChange, "multiplyValueWithCurrentHeatLevel", request.MultiplyValueWithCurrentHeatLevel);
                SetField(cardChange, "multiplyValueWithMetaProgressionMultiplier", request.MultiplyValueWithMetaProgressionMultiplier);
                SetField(cardChange, "changeableValueHasToBeGreaterZero", request.ChangeableValueHasToBeGreaterZero);
                if (_changeSideType != null)
                {
                    SetEnumField(cardChange, "side", _changeSideType, request.Side);
                }
                if (request.OnlyForTheseEntityIds != null && request.OnlyForTheseEntityIds.Length > 0)
                {
                    SetField(cardChange, "onlyForTheseEntityIds", new List<string>(request.OnlyForTheseEntityIds));
                }
                if (request.EntityIdsNotAllowed != null && request.EntityIdsNotAllowed.Length > 0)
                {
                    SetField(cardChange, "entityIdsNotAllowed", new List<string>(request.EntityIdsNotAllowed));
                }

                object source = _cardIdConstructor.Invoke(new object[] { _cardIdBlueprintValue, request.SourceId });
                _addInGameCardChangeMethod.Invoke(null, new object[] { uniqueEntityId, cardChange, source });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Convenience overload that resolves the unique entity id from a live
        /// <c>EntityController</c> via reflection before applying.
        /// </summary>
        public static bool ApplyCardChange(Assembly gameAssembly, object entityController, CardChangeRequest request)
        {
            if (entityController == null)
            {
                return false;
            }
            int uniqueId = ReadUniqueEntityId(entityController);
            if (uniqueId <= 0)
            {
                return false;
            }
            return ApplyCardChange(gameAssembly, uniqueId, request);
        }

        private static int ReadUniqueEntityId(object entityController)
        {
            try
            {
                Type type = entityController.GetType();
                PropertyInfo property = type.GetProperty("UniqueEntityId", InstanceFields);
                if (property != null && property.PropertyType == typeof(int))
                {
                    object value = property.GetValue(entityController, null);
                    if (value is int intValue)
                    {
                        return intValue;
                    }
                }
                FieldInfo field = type.GetField("UniqueEntityId", InstanceFields)
                    ?? type.GetField("_uniqueEntityId", InstanceFields)
                    ?? type.GetField("uniqueEntityId", InstanceFields);
                if (field != null && field.FieldType == typeof(int))
                {
                    object value = field.GetValue(entityController);
                    if (value is int intValue)
                    {
                        return intValue;
                    }
                }
            }
            catch
            {
            }
            return -1;
        }

        private static void SetField(object target, string name, object value)
        {
            if (target == null)
            {
                return;
            }
            FieldInfo field = target.GetType().GetField(name, InstanceFields);
            if (field == null)
            {
                return;
            }
            if (value != null && !field.FieldType.IsInstanceOfType(value))
            {
                return;
            }
            try
            {
                field.SetValue(target, value);
            }
            catch
            {
            }
        }

        private static void SetEnumField(object target, string name, Type enumType, string value)
        {
            if (target == null || enumType == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            FieldInfo field = target.GetType().GetField(name, InstanceFields);
            if (field == null || !field.FieldType.IsEnum)
            {
                return;
            }
            try
            {
                object parsed = Enum.Parse(enumType, value, true);
                field.SetValue(target, parsed);
            }
            catch
            {
            }
        }
    }
}
