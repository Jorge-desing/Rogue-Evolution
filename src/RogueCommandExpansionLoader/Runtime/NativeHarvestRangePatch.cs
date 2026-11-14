using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    public static class NativeHarvestRangePatch
    {
        private static readonly MethodInfo FilterMethod = typeof(NativeHarvestRangePatch).GetMethod(nameof(FilterCandidates));
        private static readonly Dictionary<Type, HarvestAccessors> Accessors = new Dictionary<Type, HarvestAccessors>();

        private sealed class HarvestAccessors
        {
            internal readonly FieldInfo Self, Grid, GivenPosition, Cells;
            internal HarvestAccessors(Type type)
            {
                Self = RequiredField(type, "_self", typeof(EntityController));
                Grid = RequiredField(type, "_grid", typeof(WorldGrid));
                GivenPosition = RequiredField(type, "_givenHarvestPosition", typeof(Vector3?));
                Cells = RequiredField(type, "_possibleCrystalCells", typeof(List<Vector2Int>));
            }
            private static FieldInfo RequiredField(Type type, string name, Type fieldType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null || field.FieldType != fieldType)
                    throw new InvalidOperationException("Native Harvest field drifted: " + name);
                return field;
            }
        }

        private static HarvestAccessors GetAccessors(Type type)
        {
            lock (Accessors)
            {
                if (!Accessors.TryGetValue(type, out HarvestAccessors result))
                    Accessors.Add(type, result = new HarvestAccessors(type));
                return result;
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            if (__originalMethod == null || __originalMethod.DeclaringType == null || __originalMethod.IsStatic ||
                __originalMethod.DeclaringType.Name != "Harvest" ||
                __originalMethod.Name != "ClaimClosestCrystalAndMoveToIt" ||
                __originalMethod.GetParameters().Length != 0 ||
                !(__originalMethod is MethodInfo target) || target.ReturnType != typeof(void))
                throw new InvalidOperationException("Native Harvest search signature drifted.");
            GetAccessors(__originalMethod.DeclaringType);

            var result = new List<CodeInstruction>();
            int matches = 0;
            MethodInfo addRange = typeof(List<Vector2Int>).GetMethod("AddRange", new[] { typeof(IEnumerable<Vector2Int>) });
            foreach (CodeInstruction instruction in instructions)
            {
                result.Add(instruction);
                if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) &&
                    Equals(instruction.operand, addRange))
                {
                    matches++;
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Call, FilterMethod));
                }
            }
            if (matches != 1) throw new InvalidOperationException("Expected one native Harvest crystal-list population site.");
            return result;
        }

        public static void FilterCandidates(object harvest)
        {
            try
            {
                if (harvest == null) return;
                HarvestAccessors accessors = GetAccessors(harvest.GetType());
                EntityController self = accessors.Self.GetValue(harvest) as EntityController;
                if (self == null || !self.HasRole(UnitRole.Harvester) ||
                    !RuntimeUnitMechanicsDirector.TryGetProfile(self.EntityId, out UnitRuntimeMechanicsProfile profile)) return;
                var gathering = profile.ResourceGathering;
                if (gathering == null || !(gathering.gatherRange > 0f) || float.IsInfinity(gathering.gatherRange) ||
                    (!string.Equals(gathering.resourceType, "Crystal", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(gathering.resourceType, "Crystals", StringComparison.OrdinalIgnoreCase))) return;
                var cells = accessors.Cells.GetValue(harvest) as List<Vector2Int>;
                WorldGrid grid = accessors.Grid.GetValue(harvest) as WorldGrid;
                if (cells == null || grid == null) return;
                Vector3? given = accessors.GivenPosition.GetValue(harvest) as Vector3?;
                FilterCells(cells, grid.World2Grid(given ?? self.Position), gathering.gatherRange);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[RogueCommandExpansionLoader] Native harvest range could not filter candidates: " + exception.Message);
            }
        }

        public static void FilterCells(List<Vector2Int> cells, Vector2Int anchor, float range)
        {
            if (cells == null || !(range > 0f) || float.IsInfinity(range)) return;
            double squaredRange = (double)range * range;
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                double dx = (double)cells[i].x - anchor.x;
                double dy = (double)cells[i].y - anchor.y;
                if (dx * dx + dy * dy > squaredRange) cells.RemoveAt(i);
            }
        }
    }
}
