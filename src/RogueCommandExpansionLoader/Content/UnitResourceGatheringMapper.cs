using System;
using System.Reflection;

namespace RogueCommandExpansionLoader.Content
{
    public static class UnitResourceGatheringMapper
    {
        public static void ApplyToRow(object row, ContentUnitDefinition unit)
        {
            ContentUnitResourceGatheringDefinition gathering = unit != null ? unit.resourceGathering : null;
            if (row == null || gathering == null) return;
            if (!string.IsNullOrWhiteSpace(gathering.resourceType) &&
                !string.Equals(gathering.resourceType, "Crystal", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(gathering.resourceType, "Crystals", StringComparison.OrdinalIgnoreCase)) return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type rowType = row.GetType();
            FieldInfo roles = rowType.GetField("roles", flags);
            if (roles == null || !roles.FieldType.IsEnum || !Enum.IsDefined(roles.FieldType, "Harvester")) return;
            long harvester = Convert.ToInt64(Enum.Parse(roles.FieldType, "Harvester"));
            if ((Convert.ToInt64(roles.GetValue(row)) & harvester) == 0) return;

            // Native Harvest.Update reads these exact row-backed properties. It owns
            // resource depletion, cargo, movement and refinery delivery.
            if (gathering.gatherRate > 0f && !float.IsInfinity(gathering.gatherRate))
                ApplyDefault(row, rowType, unit, "MaxArmor", "maxArmor", gathering.gatherRate);
            if (gathering.carryCapacity > 0)
            {
                if (gathering.carryCapacity > 16777216)
                    throw new InvalidOperationException("Resource carry capacity exceeds exact native Single integer precision.");
                ApplyDefault(row, rowType, unit, "GainCreditsAmount", "gainCreditsAmount", gathering.carryCapacity);
            }
        }

        private static void ApplyDefault(object row, Type rowType, ContentUnitDefinition unit,
            string parameterName, string fieldName, float value)
        {
            if (unit.customBalance != null && unit.customBalance.ContainsKey(parameterName)) return;
            if (unit.balanceOverrides != null)
            {
                foreach (ContentBalanceOverride entry in unit.balanceOverrides)
                    if (entry != null && string.Equals(entry.name, fieldName, StringComparison.OrdinalIgnoreCase)) return;
            }
            FieldInfo field = rowType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(float)) field.SetValue(row, value);
        }
    }
}
