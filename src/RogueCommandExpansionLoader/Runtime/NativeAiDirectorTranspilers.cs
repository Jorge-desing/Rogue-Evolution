using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Fail-closed CIL transpilers for Rogue Command's AI spawning, constructable deck
    /// assembly, and unit/AI target acquisition paths.
    /// Compiled in both the runtime plugin and isolated ValidationTests without direct
    /// Assembly-CSharp coupling, using reflection-based label transfer for netstandard2.1.
    /// </summary>
    public static class NativeAiDirectorTranspilers
    {
        private static readonly FieldInfo LabelsField =
            typeof(CodeInstruction).GetField("labels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static IEnumerable<CodeInstruction> AiCreateUnitTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "AiBehaviour", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "CreateUnit", StringComparison.Ordinal) ||
                original.ReturnType != typeof(void) ||
                parameters.Length != 3 ||
                parameters[0].ParameterType != typeof(string) ||
                !string.Equals(parameters[1].ParameterType.Name, "Vector3", StringComparison.Ordinal) ||
                parameters[2].ParameterType != typeof(int))
            {
                throw new InvalidOperationException(
                    "AiBehaviour.CreateUnit transpiler requires instance void CreateUnit(string, Vector3, int).");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnAiBehaviourCreateUnitTranspilerHook),
                typeof(void),
                typeof(object), typeof(string), typeof(int));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("AiBehaviour.CreateUnit transpiler received an empty instruction stream.");
            }

            int matches = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction inst = list[i];
                if ((inst.opcode == OpCodes.Call || inst.opcode == OpCodes.Callvirt) &&
                    inst.operand is MethodInfo called &&
                    called.DeclaringType != null &&
                    string.Equals(called.DeclaringType.Name, "EntityFactory", StringComparison.Ordinal) &&
                    string.Equals(called.Name, "InstantiateEntity", StringComparison.Ordinal))
                {
                    matches++;
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Dup));
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_1));
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Ldarg_3));
                    list.Insert(i + 4, new CodeInstruction(OpCodes.Call, hook));
                    i += 4;
                }
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "AiBehaviour.CreateUnit transpiler expected exactly one EntityFactory.InstantiateEntity call; found " + matches + ".");
            }

            return list;
        }

        public static IEnumerable<CodeInstruction> AiCreateBuildingTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "AiBehaviour", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "CreateBuilding", StringComparison.Ordinal) ||
                original.ReturnType == null ||
                !string.Equals(original.ReturnType.Name, "EntityController", StringComparison.Ordinal) ||
                parameters.Length != 3 ||
                parameters[0].ParameterType != typeof(string) ||
                !string.Equals(parameters[1].ParameterType.Name, "Vector3", StringComparison.Ordinal) ||
                parameters[2].ParameterType != typeof(int))
            {
                throw new InvalidOperationException(
                    "AiBehaviour.CreateBuilding transpiler requires instance EntityController CreateBuilding(string, Vector3, int).");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnAiBehaviourCreateBuildingTranspilerHook),
                typeof(void),
                typeof(object), typeof(string), typeof(int));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("AiBehaviour.CreateBuilding transpiler received an empty instruction stream.");
            }

            int retCount = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ret)
                {
                    retCount++;
                    var dup = new CodeInstruction(OpCodes.Dup);
                    TransferLabels(list[i], dup);
                    list.Insert(i, dup);
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_1));
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_3));
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Call, hook));
                    i += 4;
                }
            }

            if (retCount < 1)
            {
                throw new InvalidOperationException("AiBehaviour.CreateBuilding transpiler expected at least one Ret instruction.");
            }

            return list;
        }

        public static IEnumerable<CodeInstruction> DefaultAiConstructableEntitiesTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "DefaultAiBehaviour", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "ConstructableEntities", StringComparison.Ordinal) ||
                original.GetParameters().Length != 0 ||
                original.ReturnType == null ||
                !original.ReturnType.IsGenericType ||
                original.ReturnType.GetGenericTypeDefinition() != typeof(List<>) ||
                !string.Equals(original.ReturnType.GetGenericArguments()[0].Name, "EntityInAiDeck", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "DefaultAiBehaviour.ConstructableEntities transpiler requires instance List<EntityInAiDeck> ConstructableEntities().");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnDefaultAiConstructableEntitiesTranspilerHook),
                typeof(void),
                typeof(object), typeof(object));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("DefaultAiBehaviour.ConstructableEntities transpiler received an empty instruction stream.");
            }

            int retCount = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ret)
                {
                    retCount++;
                    var dup = new CodeInstruction(OpCodes.Dup);
                    TransferLabels(list[i], dup);
                    list.Insert(i, dup);
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Call, hook));
                    i += 3;
                }
            }

            if (retCount < 1)
            {
                throw new InvalidOperationException("DefaultAiBehaviour.ConstructableEntities transpiler expected at least one Ret instruction.");
            }

            return list;
        }

        public static IEnumerable<CodeInstruction> EntityAttackClosestEntityTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            MethodInfo original = __originalMethod as MethodInfo;
            ParameterInfo[] parameters = original == null ? Array.Empty<ParameterInfo>() : original.GetParameters();
            if (original == null || original.IsStatic ||
                original.DeclaringType == null ||
                !string.Equals(original.DeclaringType.Name, "EntityAttack", StringComparison.Ordinal) ||
                !string.Equals(original.Name, "ClosestEntity", StringComparison.Ordinal) ||
                original.ReturnType == null ||
                !string.Equals(original.ReturnType.Name, "EntityController", StringComparison.Ordinal) ||
                parameters.Length != 2 ||
                !parameters[0].ParameterType.IsGenericType ||
                parameters[0].ParameterType.GetGenericTypeDefinition() != typeof(List<>) ||
                parameters[0].ParameterType.GetGenericArguments()[0] != original.ReturnType ||
                parameters[1].ParameterType != typeof(bool))
            {
                throw new InvalidOperationException(
                    "EntityAttack.ClosestEntity transpiler requires instance EntityController ClosestEntity(List<EntityController>, bool).");
            }

            FieldInfo entityControllerField = original.DeclaringType.GetField(
                "_entityController",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (entityControllerField == null || entityControllerField.FieldType != original.ReturnType)
            {
                throw new InvalidOperationException(
                    "EntityAttack.ClosestEntity transpiler requires private EntityController _entityController field.");
            }

            MethodInfo hook = RequireStaticHook(
                nameof(RuntimeHarmonyPatches.OnEntityAttackClosestEntityTranspilerHook),
                typeof(object),
                typeof(object), typeof(object), typeof(object));

            var list = new List<CodeInstruction>(instructions);
            if (list.Count == 0)
            {
                throw new InvalidOperationException("EntityAttack.ClosestEntity transpiler received an empty instruction stream.");
            }

            int retCount = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ret)
                {
                    retCount++;
                    var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0);
                    TransferLabels(list[i], ldarg0);
                    list.Insert(i, ldarg0);
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, entityControllerField));
                    list.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_1));
                    list.Insert(i + 3, new CodeInstruction(OpCodes.Call, hook));
                    list.Insert(i + 4, new CodeInstruction(OpCodes.Castclass, original.ReturnType));
                    i += 5;
                }
            }

            if (retCount < 1)
            {
                throw new InvalidOperationException("EntityAttack.ClosestEntity transpiler expected at least one Ret instruction.");
            }

            return list;
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
