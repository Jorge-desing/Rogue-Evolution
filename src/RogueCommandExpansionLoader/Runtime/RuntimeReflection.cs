using System;
using System.Collections.Generic;
using System.Reflection;

namespace RogueCommandExpansionLoader.Runtime
{
    internal static class RuntimeReflection
    {
        private const BindingFlags AllMethods = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
        private const BindingFlags AllFields = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        public static Type FindUniqueType(Assembly assembly, string simpleName)
        {
            List<Type> matches = new List<Type>();
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types;
            }

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type != null && String.Equals(type.Name, simpleName, StringComparison.Ordinal))
                {
                    matches.Add(type);
                }
            }

            if (matches.Count != 1)
            {
                throw new InvalidOperationException("Expected one type named '" + simpleName +
                    "', found " + matches.Count + ".");
            }

            return matches[0];
        }

        public static FieldInfo FindField(Type type, string name, BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null)
            {
                throw new InvalidOperationException("Required field '" + type.FullName + "." + name +
                    "' was not found.");
            }

            return field;
        }

        public static MethodInfo FindUniqueMethod(
            Type type,
            string name,
            Func<MethodInfo, bool> shapePredicate)
        {
            List<MethodInfo> matches = new List<MethodInfo>();
            MethodInfo[] methods = type.GetMethods(AllMethods);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (String.Equals(method.Name, name, StringComparison.Ordinal) &&
                    shapePredicate(method))
                {
                    matches.Add(method);
                }
            }

            if (matches.Count != 1)
            {
                var candidateSignatures = new List<string>();
                for (int i = 0; i < methods.Length; i++)
                {
                    if (String.Equals(methods[i].Name, name, StringComparison.Ordinal))
                    {
                        candidateSignatures.Add(DescribeMethod(methods[i]));
                    }
                }

                throw new InvalidOperationException("Expected one method '" + type.FullName + "." +
                    name + "' with the analyzed shape, found " + matches.Count + ". Candidate signatures: " +
                    (candidateSignatures.Count == 0 ? "<none>" : String.Join("; ", candidateSignatures)));
            }

            return matches[0];
        }

        private static string DescribeMethod(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            var signature = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameterType = parameters[i].ParameterType;
                signature[i] = (parameterType.FullName ?? parameterType.ToString()) +
                    (parameters[i].IsOut ? " out" : (parameterType.IsByRef ? " ref" : String.Empty));
            }

            return (method.ReturnType.FullName ?? method.ReturnType.ToString()) + " " + method.Name +
                "(" + String.Join(", ", signature) + ")";
        }

        public static bool HasSimpleName(Type type, string name)
        {
            return type != null && String.Equals(type.Name, name, StringComparison.Ordinal);
        }

        public static bool IsExactList(Type collectionType, Type elementType)
        {
            return collectionType != null && collectionType.IsGenericType &&
                collectionType.GetGenericTypeDefinition() == typeof(List<>) &&
                collectionType.GetGenericArguments()[0] == elementType;
        }

    }
}
