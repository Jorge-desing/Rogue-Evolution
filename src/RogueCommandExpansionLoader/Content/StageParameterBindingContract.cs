using System;
using System.Reflection;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>
    /// Resolves the stage-parameter fields that the loader actually mutates.
    /// Keeping this contract separate makes its reflected field shape testable offline.
    /// </summary>
    internal sealed class StageParameterBindingContract
    {
        private StageParameterBindingContract(
            FieldInfo blueprintPoolListField,
            FieldInfo firstLevelCardsField,
            FieldInfo defaultCenterField)
        {
            BlueprintPoolListField = blueprintPoolListField;
            FirstLevelCardsField = firstLevelCardsField;
            DefaultCenterField = defaultCenterField;
        }

        internal FieldInfo BlueprintPoolListField { get; private set; }
        internal FieldInfo FirstLevelCardsField { get; private set; }
        internal FieldInfo DefaultCenterField { get; private set; }

        internal static StageParameterBindingContract Resolve(
            Type stageParametersType,
            Type blueprintPoolListType,
            BindingFlags bindingFlags)
        {
            if (stageParametersType == null)
            {
                throw new ArgumentNullException("stageParametersType");
            }
            if (blueprintPoolListType == null)
            {
                throw new ArgumentNullException("blueprintPoolListType");
            }

            return new StageParameterBindingContract(
                RequireMutableField(stageParametersType, "blueprintPoolList", blueprintPoolListType, bindingFlags),
                RequireMutableField(stageParametersType, "fillFirstLevelWithCardRewards", typeof(bool), bindingFlags),
                RequireMutableField(stageParametersType, "putDefaultCardRewardInCenter", typeof(bool), bindingFlags));
        }

        private static FieldInfo RequireMutableField(
            Type ownerType,
            string name,
            Type expectedType,
            BindingFlags bindingFlags)
        {
            FieldInfo field = ownerType.GetField(name, bindingFlags);
            if (field == null)
            {
                throw new InvalidOperationException("No se encontró el campo " + ownerType.FullName + "." + name + ".");
            }
            if (field.FieldType != expectedType || field.IsStatic || field.IsInitOnly)
            {
                throw new InvalidOperationException(ownerType.Name + "." + name +
                    " no coincide con el tipo mutable esperado " + expectedType.FullName + ".");
            }

            return field;
        }
    }
}
