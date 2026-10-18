using System;
using System.Reflection;

namespace RogueCommandExpansionLoader.Content
{
    internal struct ProductionBuildingEligibility
    {
        public bool IsAllowedAsStartingBlueprint { get; private set; }
        public bool IsAllowedForAi { get; private set; }
        public bool IsAllowedForDemo { get; private set; }

        public static ProductionBuildingEligibility FromProduct(ContentUnitDefinition product)
        {
            if (product == null)
            {
                throw new ArgumentNullException("product");
            }

            return new ProductionBuildingEligibility
            {
                IsAllowedAsStartingBlueprint = product.isAllowedAsStartingBlueprint,
                IsAllowedForAi = product.isAllowedForAi,
                IsAllowedForDemo = product.isAllowedForDemo
            };
        }

        public static void ApplyToFactoryRow(
            ContentUnitDefinition product,
            object factoryRow,
            FieldInfo allowedAsStartingBlueprintField,
            FieldInfo allowedForAiField,
            FieldInfo allowedForDemoField)
        {
            if (factoryRow == null)
            {
                throw new ArgumentNullException("factoryRow");
            }

            if (allowedAsStartingBlueprintField == null)
            {
                throw new ArgumentNullException("allowedAsStartingBlueprintField");
            }

            if (allowedForAiField == null)
            {
                throw new ArgumentNullException("allowedForAiField");
            }

            if (allowedForDemoField == null)
            {
                throw new ArgumentNullException("allowedForDemoField");
            }

            ProductionBuildingEligibility eligibility = FromProduct(product);
            allowedAsStartingBlueprintField.SetValue(factoryRow, eligibility.IsAllowedAsStartingBlueprint);
            allowedForAiField.SetValue(factoryRow, eligibility.IsAllowedForAi);
            allowedForDemoField.SetValue(factoryRow, eligibility.IsAllowedForDemo);
        }
    }
}
