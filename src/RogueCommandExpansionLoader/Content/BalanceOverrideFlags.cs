using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>
    /// Parses the native comma-separated flag enums used to classify blueprint roles and tags.
    /// Keep names synchronized with the pinned game's UnitRole and SystemTags enums.
    /// </summary>
    public static class BalanceOverrideFlags
    {
        private static readonly HashSet<string> UnitRoles = new HashSet<string>(StringComparer.Ordinal)
        {
            "None", "Spawn", "Harvester", "FrontalAttacker", "MassDestruction", "Fire", "Sniper", "Supporter",
            "Rock", "Robo", "Spawner", "PCXCard", "Building", "Factory", "Turret", "Refinery", "Unit", "Heal",
            "Specialist", "Skill", "Engineer", "Bomb", "Scout", "Chest", "Tank", "Vehicle", "Mech", "Tree",
            "Melee", "Research", "Crystal", "Builder", "Drop", "All"
        };

        private static readonly HashSet<string> SystemTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "None", "Melee", "Shield", "Spawn", "Armor", "Mech", "Robo", "Heal", "Turret", "Fire", "Bounty",
            "Panic", "Slow", "Stun", "Teleport", "AoE", "ManaUse", "Rush", "Turbo", "Strength", "Robust",
            "SkillRangeUse", "Bomb", "Tank", "Vehicle", "EffectDuration", "SpawnDuration", "CriticalHits", "Stealth",
            "Zombo", "Sniper", "Regen", "EconomySupport", "All"
        };

        public static bool TryParse(
            string fieldName,
            string value,
            bool isProduct,
            bool isFactoryRow,
            out HashSet<string> flags,
            out string diagnostic)
        {
            flags = new HashSet<string>(StringComparer.Ordinal);
            diagnostic = null;
            HashSet<string> allowedNames;
            if (String.Equals(fieldName, "roles", StringComparison.Ordinal))
            {
                allowedNames = UnitRoles;
            }
            else if (String.Equals(fieldName, "offeredSystemTags", StringComparison.Ordinal))
            {
                allowedNames = SystemTags;
            }
            else
            {
                diagnostic = "Only roles and offeredSystemTags are flag-valued balance fields.";
                return false;
            }

            if (String.IsNullOrWhiteSpace(value))
            {
                diagnostic = "Flag values must contain at least one native enum name.";
                return false;
            }

            string[] names = value.Split(',');
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i].Trim();
                if (name.Length == 0 || !allowedNames.Contains(name))
                {
                    diagnostic = "Unknown or empty " + fieldName + " flag '" + name + "'.";
                    return false;
                }

                if (!flags.Add(name))
                {
                    diagnostic = "Duplicate " + fieldName + " flag '" + name + "'.";
                    return false;
                }
            }

            if ((flags.Contains("None") || flags.Contains("All")) && flags.Count != 1)
            {
                diagnostic = "None and All cannot be combined with other " + fieldName + " flags.";
                return false;
            }

            if (String.Equals(fieldName, "roles", StringComparison.Ordinal))
            {
                if (isProduct && (flags.Contains("Factory") || flags.Contains("Building") || flags.Contains("All")))
                {
                    diagnostic = "A product unit cannot declare the Building, Factory or All role.";
                    return false;
                }

                if (!isProduct && !isFactoryRow)
                {
                    diagnostic = "Production-building roles can only be declared on a factory row.";
                    return false;
                }
                if (isFactoryRow && (!flags.Contains("Building") || !flags.Contains("Factory")))
                {
                    diagnostic = "A production building must retain both Building and Factory roles.";
                    return false;
                }
            }

            return true;
        }

        public static bool TryCreateEnumValue(
            Type enumType,
            string fieldName,
            string value,
            bool isProduct,
            bool isFactoryRow,
            out object enumValue,
            out string diagnostic)
        {
            enumValue = null;
            if (enumType == null || !enumType.IsEnum || Enum.GetUnderlyingType(enumType) != typeof(int))
            {
                diagnostic = "The game flag enum must use a verified Int32 underlying type.";
                return false;
            }

            bool roles = String.Equals(fieldName, "roles", StringComparison.Ordinal);
            if ((roles && !String.Equals(enumType.Name, "UnitRole", StringComparison.Ordinal)) ||
                (!roles && !String.Equals(enumType.Name, "SystemTags", StringComparison.Ordinal)))
            {
                diagnostic = "The flag field does not match the verified game enum type.";
                return false;
            }

            if (!TryParse(fieldName, value, isProduct, isFactoryRow, out HashSet<string> flags, out diagnostic))
            {
                return false;
            }

            int combined = 0;
            foreach (string flag in flags)
            {
                if (!Enum.IsDefined(enumType, flag))
                {
                    diagnostic = "The pinned game enum is missing flag '" + flag + "'.";
                    return false;
                }

                combined |= Convert.ToInt32(Enum.Parse(enumType, flag, false), CultureInfo.InvariantCulture);
            }

            enumValue = Enum.ToObject(enumType, combined);
            diagnostic = null;
            return true;
        }
        public static bool TryApply(
            object row,
            FieldInfo field,
            string fieldName,
            string value,
            bool isProduct,
            bool isFactoryRow,
            out object updatedRow,
            out string diagnostic)
        {
            updatedRow = row;
            if (row == null || field == null || field.IsStatic ||
                field.DeclaringType == null || !field.DeclaringType.IsInstanceOfType(row))
            {
                diagnostic = "A matching instance row and enum field are required.";
                return false;
            }

            if (isFactoryRow != !isProduct)
            {
                diagnostic = "Product and factory row context must agree.";
                return false;
            }

            if (String.Equals(fieldName, "roles", StringComparison.Ordinal) &&
                !isProduct && !isFactoryRow)
            {
                diagnostic = "Production-building roles can only be applied to a factory row.";
                return false;
            }

            if (!TryCreateEnumValue(field.FieldType, fieldName, value, isProduct, isFactoryRow,
                    out object enumValue, out diagnostic))
            {
                return false;
            }
            try
            {
                field.SetValue(row, enumValue);
                updatedRow = row;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "Could not assign native flags field: " + exception.GetType().Name;
                return false;
            }
        }
    }
}
