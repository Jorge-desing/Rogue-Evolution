using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Fail-closed CIL transpilers for Rogue Command's combat damage, critical hits,
    /// projectile instantiation, status effect duration, and Burst flocking movement speed.
    /// Compiled in both the runtime plugin and isolated ValidationTests without direct
    /// Assembly-CSharp coupling.
    /// </summary>
    public static class NativeCombatMovementTranspilers
    {
        private static readonly FieldInfo LabelsField =
            typeof(CodeInstruction).GetField("labels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static IEnumerable<CodeInstruction> EntityControllerTakeDamageTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "EntityController", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "TakeDamage", StringComparison.Ordinal) ||
                original.ReturnType != typeof(void) ||
                parameters.Length != 4 ||
                parameters[0].ParameterType != typeof(float) ||
                parameters[1].ParameterType != original.DeclaringType ||
                parameters[2].ParameterType != typeof(bool) ||
                parameters[3].ParameterType != typeof(bool))
            {
                throw new InvalidOperationException(
                    "EntityController.TakeDamage transpiler requires instance void TakeDamage(float, EntityController, bool, bool).");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnEntityTakeDamageTranspilerHook),
                typeof(float),
                typeof(float), typeof(object), typeof(object), typeof(bool));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("EntityController.TakeDamage transpiler received an empty instruction stream.");
            }

            int matches = 0;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                CodeInstruction current = list[i];
                CodeInstruction next = list[i + 1];
                if (IsLdarg1(current) &&
                    next.opcode == OpCodes.Stfld &&
                    next.operand is FieldInfo field &&
                    string.Equals(field.Name, "amount", StringComparison.Ordinal) &&
                    field.FieldType == typeof(float))
                {
                    matches++;
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_2));
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Ldarg_S, (byte)4));
                    list.Insert(i + 4, new CodeInstruction(OpCodes.Call, hook));
                    i += 4;
                }
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "EntityController.TakeDamage transpiler expected exactly one closure amount store (ldarg.1 -> stfld amount); found " + matches + ".");
            }

            return list;
        }

        public static IEnumerable<CodeInstruction> EntityControllerLandsCriticalHitTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "EntityController", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "LandsCriticalHit", StringComparison.Ordinal) ||
                original.ReturnType != typeof(bool) ||
                parameters.Length != 2 ||
                parameters[0].ParameterType != original.DeclaringType ||
                !string.Equals(parameters[1].ParameterType.Name, "EventPayload", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "EntityController.LandsCriticalHit transpiler requires instance bool LandsCriticalHit(EntityController, EventPayload).");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnEntityLandsCriticalHitTranspilerHook),
                typeof(float),
                typeof(float), typeof(object), typeof(object));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("EntityController.LandsCriticalHit transpiler received an empty instruction stream.");
            }

            int matches = 0;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                CodeInstruction current = list[i];
                CodeInstruction next = list[i + 1];
                if ((current.opcode == OpCodes.Call || current.opcode == OpCodes.Callvirt) &&
                    current.operand is MethodInfo called &&
                    called.DeclaringType != null &&
                    string.Equals(called.DeclaringType.Name, "Random", StringComparison.Ordinal) &&
                    string.Equals(called.Name, "get_value", StringComparison.Ordinal) &&
                    IsLdloc0(next))
                {
                    matches++;
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Ldarg_1));
                    list.Insert(i + 4, new CodeInstruction(OpCodes.Call, hook));
                    i += 4;
                }
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "EntityController.LandsCriticalHit transpiler expected exactly one Random.get_value -> ldloc.0 anchor; found " + matches + ".");
            }

            return list;
        }

        public static IEnumerable<CodeInstruction> ShootProjectileShootFromFirepointTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "ShootProjectile", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "ShootFromFirepoint", StringComparison.Ordinal) ||
                original.ReturnType != typeof(void) ||
                parameters.Length != 1 ||
                parameters[0].ParameterType != typeof(int))
            {
                throw new InvalidOperationException(
                    "ShootProjectile.ShootFromFirepoint transpiler requires instance void ShootFromFirepoint(int).");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnShootProjectileSpawnedTranspilerHook),
                typeof(void),
                typeof(object), typeof(object), typeof(int));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("ShootProjectile.ShootFromFirepoint transpiler received an empty instruction stream.");
            }

            int matches = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction current = list[i];
                if ((current.opcode == OpCodes.Call || current.opcode == OpCodes.Callvirt) &&
                    current.operand is MethodInfo called &&
                    called.DeclaringType != null &&
                    string.Equals(called.DeclaringType.Name, "Object", StringComparison.Ordinal) &&
                    string.Equals(called.Name, "Instantiate", StringComparison.Ordinal))
                {
                    matches++;
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Dup));
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Ldarg_1));
                    list.Insert(i + 4, new CodeInstruction(OpCodes.Call, hook));
                    i += 4;
                }
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "ShootProjectile.ShootFromFirepoint transpiler expected exactly one Object.Instantiate call; found " + matches + ".");
            }

            return list;
        }

        public static IEnumerable<CodeInstruction> EntityControllerSetActiveStatusEffectTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "EntityController", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "SetActiveStatusEffect", StringComparison.Ordinal) ||
                original.ReturnType != typeof(void) ||
                parameters.Length != 3 ||
                !parameters[0].ParameterType.IsEnum ||
                !parameters[1].ParameterType.IsEnum ||
                parameters[2].ParameterType != typeof(float))
            {
                throw new InvalidOperationException(
                    "EntityController.SetActiveStatusEffect transpiler requires instance void SetActiveStatusEffect(StatusEffect, DurationType, float).");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnEntitySetActiveStatusEffectTranspilerHook),
                typeof(float),
                typeof(object), typeof(int), typeof(int), typeof(float));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("EntityController.SetActiveStatusEffect transpiler received an empty instruction stream.");
            }

            int unlimitedChecks = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction current = list[i];
                if ((current.opcode == OpCodes.Call || current.opcode == OpCodes.Callvirt) &&
                    current.operand is MethodInfo called &&
                    string.Equals(called.Name, "IsUnlimited", StringComparison.Ordinal))
                {
                    unlimitedChecks++;
                }
            }

            if (unlimitedChecks != 1)
            {
                throw new InvalidOperationException(
                    "EntityController.SetActiveStatusEffect transpiler expected exactly one IsUnlimited call; found " + unlimitedChecks + ".");
            }

            var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
            TransferLabels(list[0], ldarg0);
            list.Insert(0, ldarg0);
            list.Insert(1, new CodeInstruction(OpCodes.Ldarg_1));
            list.Insert(2, new CodeInstruction(OpCodes.Ldarg_2));
            list.Insert(3, new CodeInstruction(OpCodes.Ldarg_3));
            list.Insert(4, new CodeInstruction(OpCodes.Call, hook));
            list.Insert(5, new CodeInstruction(OpCodes.Starg_S, (byte)3));

            return list;
        }

        public static IEnumerable<CodeInstruction> EntityFlockingMovementGetMovementInfoTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "EntityFlockingMovement", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "GetMovementInfo", StringComparison.Ordinal) ||
                original.ReturnType == null ||
                !string.Equals(original.ReturnType.Name, "MovingEntityInfo", StringComparison.Ordinal) ||
                parameters.Length != 1 ||
                parameters[0].ParameterType != typeof(int))
            {
                throw new InvalidOperationException(
                    "EntityFlockingMovement.GetMovementInfo transpiler requires instance MovingEntityInfo GetMovementInfo(int).");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnFlockingGetMovementInfoSpeedTranspilerHook),
                typeof(float),
                typeof(float), typeof(object));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("EntityFlockingMovement.GetMovementInfo transpiler received an empty instruction stream.");
            }

            int matches = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction current = list[i];
                if ((current.opcode == OpCodes.Call || current.opcode == OpCodes.Callvirt) &&
                    current.operand is MethodInfo called &&
                    called.DeclaringType != null &&
                    string.Equals(called.DeclaringType.Name, "EntityController", StringComparison.Ordinal) &&
                    string.Equals(called.Name, "get_MoveSpeed", StringComparison.Ordinal))
                {
                    matches++;
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Call, hook));
                    i += 2;
                }
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "EntityFlockingMovement.GetMovementInfo transpiler expected exactly one EntityController.get_MoveSpeed call; found " + matches + ".");
            }

            return list;
        }

        private static bool IsLdarg1(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldarg_1)
            {
                return true;
            }
            if ((instruction.opcode == OpCodes.Ldarg_S || instruction.opcode == OpCodes.Ldarg) &&
                instruction.operand is IConvertible conv)
            {
                try { return conv.ToInt32(null) == 1; }
                catch { return false; }
            }
            return false;
        }

        private static bool IsLdloc0(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldloc_0)
            {
                return true;
            }
            if ((instruction.opcode == OpCodes.Ldloc_S || instruction.opcode == OpCodes.Ldloc) &&
                instruction.operand is IConvertible conv)
            {
                try { return conv.ToInt32(null) == 0; }
                catch { return false; }
            }
            return false;
        }

        private static MethodInfo RequireStaticHook(string name, Type returnType, params Type[] paramTypes)
        {
            MethodInfo hook = typeof(RuntimeHarmonyPatches).GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Static);
            if (hook == null || hook.DeclaringType != typeof(RuntimeHarmonyPatches) ||
                !hook.IsStatic || hook.ContainsGenericParameters || hook.ReturnType != returnType)
            {
                throw new InvalidOperationException("Missing or invalid RuntimeHarmonyPatches hook '" + name + "'.");
            }

            ParameterInfo[] actual = hook.GetParameters();
            if (actual.Length != paramTypes.Length)
            {
                throw new InvalidOperationException("Unexpected parameter count on hook '" + name + "'.");
            }
            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i].ParameterType != paramTypes[i])
                {
                    throw new InvalidOperationException("Unexpected parameter type on hook '" + name + "' at index " + i + ".");
                }
            }

            return hook;
        }

        private static void TransferLabels(CodeInstruction source, CodeInstruction destination)
        {
            if (source == null || destination == null || LabelsField == null)
            {
                return;
            }

            try
            {
                if (LabelsField.GetValue(source) is IList srcList &&
                    LabelsField.GetValue(destination) is IList dstList &&
                    srcList.Count > 0)
                {
                    for (int i = 0; i < srcList.Count; i++)
                    {
                        dstList.Add(srcList[i]);
                    }
                    srcList.Clear();
                }
            }
            catch
            {
            }
        }
    }
}
