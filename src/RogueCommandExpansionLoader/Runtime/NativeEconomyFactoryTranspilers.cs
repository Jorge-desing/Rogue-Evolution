using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Fail-closed CIL transpilers for Rogue Command's harvesting extraction/deposit loop,
    /// central Bank crystal deposits, factory unit production inheritance, and building/queue
    /// refund paths. Compiled in both the runtime plugin and isolated ValidationTests without
    /// direct Assembly-CSharp coupling.
    /// </summary>
    public static class NativeEconomyFactoryTranspilers
    {
        public static IEnumerable<CodeInstruction> HarvestUpdateTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "Harvest", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "Update", StringComparison.Ordinal) ||
                original.ReturnType == null ||
                !string.Equals(original.ReturnType.Name, "UpdateStatus", StringComparison.Ordinal) ||
                parameters.Length != 0)
            {
                throw new InvalidOperationException(
                    "Harvest.Update transpiler requires instance UpdateStatus Update().");
            }

            MethodInfo extractionHook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnHarvestExtractionRateTranspilerHook),
                typeof(float),
                typeof(float), typeof(object));
            MethodInfo depositHook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnHarvestDepositLoadTranspilerHook),
                typeof(float),
                typeof(float), typeof(object));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("Harvest.Update transpiler received an empty instruction stream.");
            }

            int extractionMatches = 0;
            for (int i = 0; i + 3 < list.Count; i++)
            {
                if (IsLdarg0(list[i]) &&
                    list[i + 1].opcode == OpCodes.Ldfld &&
                    list[i + 1].operand is FieldInfo selfField &&
                    string.Equals(selfField.Name, "_self", StringComparison.Ordinal) &&
                    (list[i + 2].opcode == OpCodes.Callvirt || list[i + 2].opcode == OpCodes.Call) &&
                    list[i + 2].operand is MethodInfo getMaxArmor &&
                    getMaxArmor.DeclaringType != null &&
                    string.Equals(getMaxArmor.DeclaringType.Name, "EntityController", StringComparison.Ordinal) &&
                    string.Equals(getMaxArmor.Name, "get_MaxArmor", StringComparison.Ordinal) &&
                    list[i + 3].opcode == OpCodes.Mul)
                {
                    extractionMatches++;
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 4, new CodeInstruction(OpCodes.Call, extractionHook));
                    i += 2;
                }
            }

            int depositMatches = 0;
            for (int i = 0; i + 2 < list.Count; i++)
            {
                if (IsLdarg0(list[i]) &&
                    list[i + 1].opcode == OpCodes.Ldfld &&
                    list[i + 1].operand is FieldInfo loadField &&
                    string.Equals(loadField.Name, "_currentLoad", StringComparison.Ordinal) &&
                    loadField.FieldType == typeof(float) &&
                    (list[i + 2].opcode == OpCodes.Call || list[i + 2].opcode == OpCodes.Callvirt) &&
                    list[i + 2].operand is MethodInfo depositMethod &&
                    depositMethod.DeclaringType != null &&
                    string.Equals(depositMethod.DeclaringType.Name, "Bank", StringComparison.Ordinal) &&
                    string.Equals(depositMethod.Name, "Deposit", StringComparison.Ordinal))
                {
                    depositMatches++;
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Call, depositHook));
                    i += 2;
                }
            }

            if (extractionMatches != 1 || depositMatches != 1)
            {
                throw new InvalidOperationException(
                    "Harvest.Update transpiler expected 1 extraction site and 1 deposit site; found extraction=" +
                    extractionMatches + ", deposit=" + depositMatches + ".");
            }

            return list;
        }

        public static IEnumerable<CodeInstruction> BankDepositTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || !original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "Bank", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "Deposit", StringComparison.Ordinal) ||
                original.ReturnType != typeof(void) ||
                parameters.Length != 2 ||
                parameters[0].ParameterType != typeof(string) ||
                parameters[1].ParameterType != typeof(float))
            {
                throw new InvalidOperationException(
                    "Bank.Deposit transpiler requires static void Deposit(string, float).");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnBankDepositTranspilerHook),
                typeof(float),
                typeof(float), typeof(string));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("Bank.Deposit transpiler received an empty instruction stream.");
            }

            int matches = 0;
            for (int i = 0; i + 2 < list.Count; i++)
            {
                if (IsLdloc0(list[i]) &&
                    IsLdarg1(list[i + 1]) &&
                    list[i + 2].opcode == OpCodes.Add)
                {
                    matches++;
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Call, hook));
                    i += 2;
                }
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "Bank.Deposit transpiler expected exactly one ldloc.0 -> ldarg.1 -> add sequence; found " + matches + ".");
            }

            return list;
        }

        public static IEnumerable<CodeInstruction> EntityControllerUpdateProductionTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "EntityController", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "UpdateProduction", StringComparison.Ordinal) ||
                original.ReturnType != typeof(void) ||
                parameters.Length != 0)
            {
                throw new InvalidOperationException(
                    "EntityController.UpdateProduction transpiler requires instance void UpdateProduction().");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnFactoryUpdateProductionTranspilerHook),
                typeof(void),
                typeof(object), typeof(object));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("EntityController.UpdateProduction transpiler received an empty instruction stream.");
            }

            int matches = 0;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                CodeInstruction current = list[i];
                if ((current.opcode == OpCodes.Call || current.opcode == OpCodes.Callvirt) &&
                    current.operand is MethodInfo called &&
                    called.DeclaringType != null &&
                    string.Equals(called.DeclaringType.Name, "EntityFactory", StringComparison.Ordinal) &&
                    string.Equals(called.Name, "InstantiateEntity", StringComparison.Ordinal) &&
                    called.ReturnType != typeof(void) &&
                    IsStloc(list[i + 1]))
                {
                    matches++;
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Dup));
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Call, hook));
                    i += 3;
                }
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "EntityController.UpdateProduction transpiler expected exactly one EntityFactory.InstantiateEntity -> stloc anchor; found " + matches + ".");
            }

            return list;
        }

        public static IEnumerable<CodeInstruction> EntityControllerDestroyAndRepayTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "EntityController", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "DestroyAndRepay", StringComparison.Ordinal) ||
                original.ReturnType != typeof(void) ||
                parameters.Length != 0)
            {
                throw new InvalidOperationException(
                    "EntityController.DestroyAndRepay transpiler requires instance void DestroyAndRepay().");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnEntityDestroyAndRepayTranspilerHook),
                typeof(float),
                typeof(float), typeof(object));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("EntityController.DestroyAndRepay transpiler received an empty instruction stream.");
            }

            int matches = 0;
            for (int i = 0; i + 3 < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldfld &&
                    list[i].operand is FieldInfo paidField &&
                    string.Equals(paidField.Name, "crystalAmountThePlayerPaidForIt", StringComparison.Ordinal) &&
                    paidField.FieldType == typeof(int) &&
                    list[i + 1].opcode == OpCodes.Conv_R4 &&
                    list[i + 2].opcode == OpCodes.Mul &&
                    (list[i + 3].opcode == OpCodes.Call || list[i + 3].opcode == OpCodes.Callvirt) &&
                    list[i + 3].operand is MethodInfo depositMethod &&
                    depositMethod.DeclaringType != null &&
                    string.Equals(depositMethod.DeclaringType.Name, "Bank", StringComparison.Ordinal) &&
                    string.Equals(depositMethod.Name, "Deposit", StringComparison.Ordinal))
                {
                    matches++;
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 4, new CodeInstruction(OpCodes.Call, hook));
                    i += 2;
                }
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "EntityController.DestroyAndRepay transpiler expected exactly one crystalAmountThePlayerPaidForIt -> conv.r4 -> mul -> Bank.Deposit anchor; found " + matches + ".");
            }

            return list;
        }

        public static IEnumerable<CodeInstruction> EntityControllerAbortProductionTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "EntityController", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "AbortProduction", StringComparison.Ordinal) ||
                original.ReturnType != typeof(void) ||
                parameters.Length != 0)
            {
                throw new InvalidOperationException(
                    "EntityController.AbortProduction transpiler requires instance void AbortProduction().");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnFactoryAbortProductionTranspilerHook),
                typeof(float),
                typeof(float), typeof(object));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("EntityController.AbortProduction transpiler received an empty instruction stream.");
            }

            int matches = 0;
            for (int i = 0; i + 2 < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldfld &&
                    list[i].operand is FieldInfo costField &&
                    costField.DeclaringType != null &&
                    string.Equals(costField.DeclaringType.Name, "ProductionInfo", StringComparison.Ordinal) &&
                    string.Equals(costField.Name, "cost", StringComparison.Ordinal) &&
                    costField.FieldType == typeof(int) &&
                    list[i + 1].opcode == OpCodes.Conv_R4 &&
                    (list[i + 2].opcode == OpCodes.Call || list[i + 2].opcode == OpCodes.Callvirt) &&
                    list[i + 2].operand is MethodInfo depositMethod &&
                    depositMethod.DeclaringType != null &&
                    string.Equals(depositMethod.DeclaringType.Name, "Bank", StringComparison.Ordinal) &&
                    string.Equals(depositMethod.Name, "Deposit", StringComparison.Ordinal))
                {
                    matches++;
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Call, hook));
                    i += 2;
                }
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "EntityController.AbortProduction transpiler expected exactly one ProductionInfo.cost -> conv.r4 -> Bank.Deposit anchor; found " + matches + ".");
            }

            return list;
        }

        private static bool IsLdarg0(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldarg_0)
            {
                return true;
            }
            if ((instruction.opcode == OpCodes.Ldarg_S || instruction.opcode == OpCodes.Ldarg) &&
                instruction.operand is IConvertible conv)
            {
                try { return conv.ToInt32(null) == 0; }
                catch { return false; }
            }
            return false;
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

        private static bool IsStloc(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Stloc_0 ||
                instruction.opcode == OpCodes.Stloc_1 ||
                instruction.opcode == OpCodes.Stloc_2 ||
                instruction.opcode == OpCodes.Stloc_3 ||
                instruction.opcode == OpCodes.Stloc_S ||
                instruction.opcode == OpCodes.Stloc;
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
    }
}
