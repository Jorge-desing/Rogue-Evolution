using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Clean-room headless runtime explorer for agents: scene/asset/DontDestroyOnLoad object search,
    /// singleton discovery, deep type/implementation reflection, static/singleton member read/write,
    /// typed method invocation, and Harmony patch inspection without UI or screen captures.
    /// </summary>
    internal static class RuntimeExplorerDirector
    {
        private const BindingFlags AnyMemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        private const BindingFlags StaticOnlyFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        private static readonly string[] CommonSingletonMemberNames =
        {
            "Instance",
            "instance",
            "_instance",
            "_Instance",
            "s_instance",
            "s_Instance",
            "m_instance",
            "m_Instance",
            "Current",
            "current"
        };

        public static string SearchUnityObjectsJson(
            string nameFilter,
            string typeFilter,
            string sceneFilter,
            string childFilter,
            int limit)
        {
            int clampedLimit = Math.Max(1, Math.Min(limit <= 0 ? 50 : limit, 200));
            string normalizedSceneFilter = (sceneFilter ?? "any").Trim().ToLowerInvariant();
            string normalizedChildFilter = (childFilter ?? "any").Trim().ToLowerInvariant();

            Type targetType = typeof(UnityEngine.Object);
            string typeResolveWarning = null;
            if (!string.IsNullOrWhiteSpace(typeFilter))
            {
                Type resolved = ResolveType(typeFilter.Trim());
                if (resolved == null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "Could not resolve UnityEngine.Object type '" + typeFilter + "'."
                    });
                }

                if (!typeof(UnityEngine.Object).IsAssignableFrom(resolved))
                {
                    typeResolveWarning = "Type '" + resolved.FullName + "' does not derive from UnityEngine.Object; falling back to UnityEngine.Object.";
                }
                else
                {
                    targetType = resolved;
                }
            }

            UnityEngine.Object[] allObjects = FindAllUnityObjects(targetType);
            var loadedScenes = new List<object>();
            try
            {
                int sceneCount = SceneManager.sceneCount;
                for (int i = 0; i < sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    loadedScenes.Add(new
                    {
                        name = scene.name,
                        handle = scene.handle,
                        buildIndex = scene.buildIndex,
                        isLoaded = scene.isLoaded
                    });
                }
            }
            catch
            {
                // Keep scene enumeration resilient in test stubs or transitional states.
            }

            int totalMatched = 0;
            var items = new List<object>();
            for (int i = 0; i < allObjects.Length; i++)
            {
                UnityEngine.Object obj = allObjects[i];
                if (obj == null)
                {
                    continue;
                }

                string objName = GetUnityObjectName(obj);
                if (!string.IsNullOrEmpty(nameFilter) &&
                    (objName == null || objName.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                GameObject go = obj as GameObject;
                if (go == null && obj is Component comp)
                {
                    go = comp.gameObject;
                }

                string objSceneName = string.Empty;
                int objSceneHandle = 0;
                bool isAssetOrHidden = go == null;
                bool hasParent = false;
                string hierarchyPath = string.Empty;

                if (go != null)
                {
                    Scene objScene = go.scene;
                    objSceneName = objScene.name ?? string.Empty;
                    objSceneHandle = objScene.handle;
                    isAssetOrHidden = !objScene.IsValid() || objSceneHandle == 0 || objSceneHandle == -1;
                    hasParent = go.transform != null && go.transform.parent != null;
                    hierarchyPath = BuildHierarchyPath(go.transform);

                    if (!MatchesSceneFilter(objScene, objSceneHandle, isAssetOrHidden, normalizedSceneFilter))
                    {
                        continue;
                    }

                    if (!MatchesChildFilter(hasParent, normalizedChildFilter))
                    {
                        continue;
                    }
                }
                else
                {
                    if (normalizedSceneFilter != "any" && normalizedSceneFilter != "hide_and_dont_save")
                    {
                        continue;
                    }

                    if (normalizedChildFilter != "any")
                    {
                        continue;
                    }
                }

                totalMatched++;
                if (items.Count < clampedLimit)
                {
                    items.Add(new
                    {
                        instanceId = obj.GetInstanceID(),
                        name = objName ?? string.Empty,
                        typeFullName = obj.GetType().FullName,
                        sceneName = objSceneName,
                        sceneHandle = objSceneHandle,
                        isAssetOrHidden,
                        hasParent,
                        hierarchyPath
                    });
                }
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                targetType = targetType.FullName,
                sceneFilter = normalizedSceneFilter,
                childFilter = normalizedChildFilter,
                warning = typeResolveWarning,
                loadedScenes,
                totalMatched,
                returnedCount = items.Count,
                items
            });
        }

        public static string FindSingletonsJson(string typeFilter, int limit)
        {
            int clampedLimit = Math.Max(1, Math.Min(limit <= 0 ? 50 : limit, 200));
            var items = new List<object>();
            int totalMatched = 0;

            foreach (Type type in EnumerateAllTypes())
            {
                if (type.IsInterface || type.IsEnum || (type.IsAbstract && type.IsSealed))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(typeFilter) &&
                    (type.FullName == null || type.FullName.IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                if (TryResolveSingleton(type, out object instance, out string memberName, out string memberKind))
                {
                    totalMatched++;
                    if (items.Count < clampedLimit)
                    {
                        bool isUnityObject = instance is UnityEngine.Object;
                        int instanceId = isUnityObject ? ((UnityEngine.Object)instance).GetInstanceID() : 0;
                        items.Add(new
                        {
                            typeFullName = type.FullName,
                            assemblyName = type.Assembly.GetName().Name,
                            memberName,
                            memberKind,
                            isUnityObject,
                            instanceId,
                            valuePreview = SafeValuePreview(instance)
                        });
                    }
                }
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                typeFilter = typeFilter ?? string.Empty,
                totalMatched,
                returnedCount = items.Count,
                items
            });
        }

        public static string InspectTypeJson(string typeName, string memberFilter, bool includeNonPublic, int maxMembers)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return JsonConvert.SerializeObject(new { success = false, error = "typeName is required." });
            }

            Type type = ResolveType(typeName.Trim());
            if (type == null)
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Type '" + typeName + "' not found." });
            }

            int clampedMax = Math.Max(1, Math.Min(maxMembers <= 0 ? 100 : maxMembers, 400));
            BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            if (includeNonPublic)
            {
                flags |= BindingFlags.NonPublic;
            }

            var baseTypes = new List<string>();
            Type cursor = type.BaseType;
            while (cursor != null)
            {
                baseTypes.Add(cursor.FullName);
                cursor = cursor.BaseType;
            }

            var interfaces = new List<string>();
            foreach (Type iface in type.GetInterfaces())
            {
                interfaces.Add(iface.FullName);
            }

            var fields = new List<object>();
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (!MatchesFilter(field.Name, memberFilter))
                {
                    continue;
                }

                if (fields.Count >= clampedMax)
                {
                    break;
                }

                fields.Add(new
                {
                    name = field.Name,
                    fieldType = field.FieldType.FullName ?? field.FieldType.Name,
                    isStatic = field.IsStatic,
                    isPublic = field.IsPublic,
                    isReadOnly = field.IsInitOnly || field.IsLiteral
                });
            }

            var properties = new List<object>();
            foreach (PropertyInfo prop in type.GetProperties(flags))
            {
                if (!MatchesFilter(prop.Name, memberFilter))
                {
                    continue;
                }

                if (properties.Count >= clampedMax)
                {
                    break;
                }

                MethodInfo getter = prop.GetGetMethod(true);
                MethodInfo setter = prop.GetSetMethod(true);
                properties.Add(new
                {
                    name = prop.Name,
                    propertyType = prop.PropertyType.FullName ?? prop.PropertyType.Name,
                    canRead = prop.CanRead && getter != null,
                    canWrite = prop.CanWrite && setter != null,
                    isStatic = (getter != null && getter.IsStatic) || (setter != null && setter.IsStatic),
                    isPublic = (getter != null && getter.IsPublic) || (setter != null && setter.IsPublic)
                });
            }

            var methods = new List<object>();
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.IsSpecialName || !MatchesFilter(method.Name, memberFilter))
                {
                    continue;
                }

                if (methods.Count >= clampedMax)
                {
                    break;
                }

                ParameterInfo[] parameters = method.GetParameters();
                var paramDtos = new List<object>(parameters.Length);
                for (int i = 0; i < parameters.Length; i++)
                {
                    paramDtos.Add(new
                    {
                        name = parameters[i].Name,
                        type = parameters[i].ParameterType.FullName ?? parameters[i].ParameterType.Name
                    });
                }

                methods.Add(new
                {
                    name = method.Name,
                    returnType = method.ReturnType.FullName ?? method.ReturnType.Name,
                    isStatic = method.IsStatic,
                    isPublic = method.IsPublic,
                    parameters = paramDtos
                });
            }

            bool hasSingleton = TryResolveSingleton(type, out _, out string singletonMember, out string singletonKind);

            return JsonConvert.SerializeObject(new
            {
                success = true,
                fullName = type.FullName,
                assemblyName = type.Assembly.GetName().Name,
                isStatic = type.IsAbstract && type.IsSealed,
                isAbstract = type.IsAbstract,
                isInterface = type.IsInterface,
                isEnum = type.IsEnum,
                hasSingleton,
                singletonMember,
                singletonKind,
                baseTypes,
                interfaces,
                fields,
                properties,
                methods
            });
        }

        public static string FindImplementationsJson(string baseTypeName, int limit)
        {
            if (string.IsNullOrWhiteSpace(baseTypeName))
            {
                return JsonConvert.SerializeObject(new { success = false, error = "baseTypeName is required." });
            }

            Type baseType = ResolveType(baseTypeName.Trim());
            if (baseType == null)
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Base type '" + baseTypeName + "' not found." });
            }

            int clampedLimit = Math.Max(1, Math.Min(limit <= 0 ? 64 : limit, 200));
            int totalMatched = 0;
            var items = new List<object>();

            foreach (Type candidate in EnumerateAllTypes())
            {
                if (candidate == baseType || candidate.IsAbstract || candidate.IsInterface)
                {
                    continue;
                }

                bool assignable;
                try
                {
                    assignable = baseType.IsAssignableFrom(candidate);
                }
                catch
                {
                    assignable = false;
                }

                if (!assignable)
                {
                    continue;
                }

                totalMatched++;
                if (items.Count < clampedLimit)
                {
                    items.Add(new
                    {
                        fullName = candidate.FullName,
                        assemblyName = candidate.Assembly.GetName().Name
                    });
                }
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                baseType = baseType.FullName,
                totalMatched,
                returnedCount = items.Count,
                items
            });
        }

        public static string GetTypeOrSingletonMemberJson(string typeName, string memberName, bool useSingleton, int instanceId = 0)
        {
            if (!TryResolveTargetAndMember(typeName, memberName, useSingleton, instanceId, out Type type, out object target, out MemberInfo member, out string error))
            {
                return JsonConvert.SerializeObject(new { success = false, error });
            }

            try
            {
                object value;
                Type valueType;
                string memberKind;
                if (member is FieldInfo field)
                {
                    value = field.GetValue(field.IsStatic ? null : target);
                    valueType = field.FieldType;
                    memberKind = "field";
                }
                else if (member is PropertyInfo prop)
                {
                    MethodInfo getter = prop.GetGetMethod(true);
                    if (getter == null)
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = "Property '" + memberName + "' is write-only." });
                    }

                    value = prop.GetValue(getter.IsStatic ? null : target, null);
                    valueType = prop.PropertyType;
                    memberKind = "property";
                }
                else
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Unsupported member kind." });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    typeFullName = type.FullName,
                    memberName = member.Name,
                    memberKind,
                    valueType = valueType.FullName ?? valueType.Name,
                    isNull = value == null,
                    valueText = SafeValuePreview(value),
                    serializedValue = ToSerializableValue(value)
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new
                {
                    success = false,
                    error = "Failed reading '" + type.FullName + "." + memberName + "': " + (ex.InnerException ?? ex).Message
                });
            }
        }

        public static string SetTypeOrSingletonMemberJson(string typeName, string memberName, string valueText, bool useSingleton, int instanceId = 0)
        {
            if (!TryResolveTargetAndMember(typeName, memberName, useSingleton, instanceId, out Type type, out object target, out MemberInfo member, out string error))
            {
                return JsonConvert.SerializeObject(new { success = false, error });
            }

            try
            {
                if (member is FieldInfo field)
                {
                    if (field.IsLiteral)
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = "Field '" + memberName + "' is a compile-time constant." });
                    }

                    if (!TryParseTypedValue(valueText, field.FieldType, out object parsed, out string parseError))
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = parseError });
                    }

                    field.SetValue(field.IsStatic ? null : target, parsed);
                    object updated = field.GetValue(field.IsStatic ? null : target);
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        typeFullName = type.FullName,
                        memberName = field.Name,
                        memberKind = "field",
                        valueType = field.FieldType.FullName ?? field.FieldType.Name,
                        valueText = SafeValuePreview(updated)
                    });
                }

                if (member is PropertyInfo prop)
                {
                    MethodInfo setter = prop.GetSetMethod(true);
                    if (setter == null)
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = "Property '" + memberName + "' has no setter." });
                    }

                    if (!TryParseTypedValue(valueText, prop.PropertyType, out object parsed, out string parseError))
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = parseError });
                    }

                    prop.SetValue(setter.IsStatic ? null : target, parsed, null);
                    MethodInfo getter = prop.GetGetMethod(true);
                    object updated = getter != null ? prop.GetValue(getter.IsStatic ? null : target, null) : parsed;
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        typeFullName = type.FullName,
                        memberName = prop.Name,
                        memberKind = "property",
                        valueType = prop.PropertyType.FullName ?? prop.PropertyType.Name,
                        valueText = SafeValuePreview(updated)
                    });
                }

                return JsonConvert.SerializeObject(new { success = false, error = "Unsupported member kind." });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new
                {
                    success = false,
                    error = "Failed writing '" + type.FullName + "." + memberName + "': " + (ex.InnerException ?? ex).Message
                });
            }
        }

        public static string InvokeTypeOrSingletonMethodJson(string typeName, string methodName, string argsJson, bool useSingleton, int instanceId = 0)
        {
            if (string.IsNullOrWhiteSpace(methodName))
            {
                return JsonConvert.SerializeObject(new { success = false, error = "methodName is required." });
            }

            Type type = null;
            object target = null;
            if (instanceId != 0)
            {
                UnityEngine.Object instanceObj = FindUnityObjectByInstanceId(instanceId);
                if (instanceObj == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No UnityEngine.Object found with instanceId " + instanceId + "." });
                }
                target = instanceObj;
                type = string.IsNullOrWhiteSpace(typeName) ? instanceObj.GetType() : ResolveType(typeName.Trim());
                if (type == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Type '" + typeName + "' not found." });
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "typeName or instanceId is required." });
                }

                type = ResolveType(typeName.Trim());
                if (type == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Type '" + typeName + "' not found." });
                }

                if (useSingleton && !TryResolveSingleton(type, out target, out _, out _))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No live singleton instance found on '" + type.FullName + "'." });
                }
            }

            List<string> rawArgs;
            if (!TryParseArgsArray(argsJson, out rawArgs, out string argsError))
            {
                return JsonConvert.SerializeObject(new { success = false, error = argsError });
            }

            BindingFlags flags = (useSingleton || instanceId != 0) ? AnyMemberFlags : StaticOnlyFlags;
            MethodInfo[] allMethods = type.GetMethods(flags);
            var candidates = new List<MethodInfo>();
            for (int i = 0; i < allMethods.Length; i++)
            {
                MethodInfo m = allMethods[i];
                if (string.Equals(m.Name, methodName.Trim(), StringComparison.Ordinal) &&
                    !m.ContainsGenericParameters &&
                    m.GetParameters().Length == rawArgs.Count)
                {
                    candidates.Add(m);
                }
            }

            if (candidates.Count == 0)
            {
                return JsonConvert.SerializeObject(new
                {
                    success = false,
                    error = "No matching method '" + type.FullName + "." + methodName + "' with " + rawArgs.Count + " parameter(s)."
                });
            }

            MethodInfo chosenMethod = null;
            object[] convertedArgs = null;
            string lastParseError = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                MethodInfo candidate = candidates[i];
                if (!candidate.IsStatic && target == null)
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                object[] parsedValues = new object[parameters.Length];
                bool ok = true;
                for (int p = 0; p < parameters.Length; p++)
                {
                    if (!TryParseTypedValue(rawArgs[p], parameters[p].ParameterType, out object parsed, out string parseError))
                    {
                        lastParseError = parseError;
                        ok = false;
                        break;
                    }
                    parsedValues[p] = parsed;
                }

                if (ok)
                {
                    chosenMethod = candidate;
                    convertedArgs = parsedValues;
                    break;
                }
            }

            if (chosenMethod == null)
            {
                return JsonConvert.SerializeObject(new
                {
                    success = false,
                    error = lastParseError ?? "Could not bind arguments to any overload of '" + methodName + "'."
                });
            }

            try
            {
                object returnValue = chosenMethod.Invoke(chosenMethod.IsStatic ? null : target, convertedArgs);
                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    typeFullName = type.FullName,
                    methodName = chosenMethod.Name,
                    matchedSignature = FormatMethodSignature(chosenMethod),
                    returnType = chosenMethod.ReturnType.FullName ?? chosenMethod.ReturnType.Name,
                    returnValue = ToSerializableValue(returnValue),
                    returnPreview = SafeValuePreview(returnValue)
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new
                {
                    success = false,
                    error = "Invocation failed: " + (ex.InnerException ?? ex).Message
                });
            }
        }

        public static string InspectMethodPatchesJson(string typeName, string methodName)
        {
            var inspected = new List<object>();

            if (string.IsNullOrWhiteSpace(typeName))
            {
                foreach (MethodBase method in Harmony.GetAllPatchedMethods())
                {
                    if (method == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(methodName) &&
                        method.Name.IndexOf(methodName.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    Patches info = Harmony.GetPatchInfo(method);
                    inspected.Add(FormatPatchRecord(method, info));
                    if (inspected.Count >= 100)
                    {
                        break;
                    }
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    scope = "all_patched_methods",
                    count = inspected.Count,
                    methods = inspected
                });
            }

            Type type = ResolveType(typeName.Trim());
            if (type == null)
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Type '" + typeName + "' not found." });
            }

            MethodInfo[] methods = type.GetMethods(AnyMemberFlags);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.IsNullOrWhiteSpace(methodName) &&
                    !string.Equals(method.Name, methodName.Trim(), StringComparison.Ordinal))
                {
                    continue;
                }

                Patches info = Harmony.GetPatchInfo(method);
                if (info == null && string.IsNullOrWhiteSpace(methodName))
                {
                    continue;
                }

                inspected.Add(FormatPatchRecord(method, info));
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                scope = type.FullName,
                count = inspected.Count,
                methods = inspected
            });
        }

        internal static bool TryParseTypedValue(string rawValue, Type targetType, out object parsed, out string error)
        {
            parsed = null;
            error = null;

            if (targetType == null)
            {
                error = "Target type is null.";
                return false;
            }

            Type underlying = Nullable.GetUnderlyingType(targetType);
            if (underlying != null)
            {
                if (rawValue == null || string.Equals(rawValue.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                {
                    parsed = null;
                    return true;
                }

                targetType = underlying;
            }

            if (rawValue == null || (!targetType.IsValueType && string.Equals(rawValue.Trim(), "null", StringComparison.OrdinalIgnoreCase)))
            {
                if (targetType.IsValueType)
                {
                    error = "Cannot assign null to value type '" + targetType.FullName + "'.";
                    return false;
                }

                parsed = null;
                return true;
            }

            string trimmed = rawValue.Trim();

            if (targetType == typeof(string))
            {
                parsed = rawValue;
                return true;
            }

            if (targetType == typeof(bool))
            {
                if (bool.TryParse(trimmed, out bool b))
                {
                    parsed = b;
                    return true;
                }
                error = "Invalid bool value '" + rawValue + "'.";
                return false;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                {
                    parsed = v;
                    return true;
                }
                error = "Invalid int value '" + rawValue + "'.";
                return false;
            }

            if (targetType == typeof(long))
            {
                if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
                {
                    parsed = v;
                    return true;
                }
                error = "Invalid long value '" + rawValue + "'.";
                return false;
            }

            if (targetType == typeof(float))
            {
                if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && float.IsFinite(v))
                {
                    parsed = v;
                    return true;
                }
                error = "Invalid finite float value '" + rawValue + "'.";
                return false;
            }

            if (targetType == typeof(double))
            {
                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v))
                {
                    parsed = v;
                    return true;
                }
                error = "Invalid finite double value '" + rawValue + "'.";
                return false;
            }

            if (targetType == typeof(decimal))
            {
                if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v))
                {
                    parsed = v;
                    return true;
                }
                error = "Invalid decimal value '" + rawValue + "'.";
                return false;
            }

            if (targetType.IsEnum)
            {
                try
                {
                    parsed = Enum.Parse(targetType, trimmed, ignoreCase: true);
                    return true;
                }
                catch
                {
                    error = "Invalid enum value '" + rawValue + "' for " + targetType.FullName + ".";
                    return false;
                }
            }

            if (targetType == typeof(Vector2))
            {
                if (TryParseFloatTuple(trimmed, 2, out float[] f))
                {
                    parsed = new Vector2(f[0], f[1]);
                    return true;
                }
                error = "Invalid Vector2 '" + rawValue + "'. Expected 'x,y'.";
                return false;
            }

            if (targetType == typeof(Vector2Int))
            {
                string[] parts = trimmed.Split(',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) &&
                    int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                {
                    parsed = new Vector2Int(x, y);
                    return true;
                }
                error = "Invalid Vector2Int '" + rawValue + "'. Expected 'x,y'.";
                return false;
            }

            if (targetType == typeof(Vector3))
            {
                if (TryParseFloatTuple(trimmed, 3, out float[] f))
                {
                    parsed = new Vector3(f[0], f[1], f[2]);
                    return true;
                }
                error = "Invalid Vector3 '" + rawValue + "'. Expected 'x,y,z'.";
                return false;
            }

            if (targetType == typeof(Quaternion))
            {
                if (TryParseFloatTuple(trimmed, 4, out float[] f))
                {
                    parsed = new Quaternion(f[0], f[1], f[2], f[3]);
                    return true;
                }
                error = "Invalid Quaternion '" + rawValue + "'. Expected 'x,y,z,w'.";
                return false;
            }

            if (targetType == typeof(Color))
            {
                if (TryParseFloatTuple(trimmed, 4, out float[] f4))
                {
                    parsed = new Color(f4[0], f4[1], f4[2], f4[3]);
                    return true;
                }
                if (TryParseFloatTuple(trimmed, 3, out float[] f3))
                {
                    parsed = new Color(f3[0], f3[1], f3[2], 1f);
                    return true;
                }
                error = "Invalid Color '" + rawValue + "'. Expected 'r,g,b[,a]'.";
                return false;
            }

            if (targetType == typeof(Rect))
            {
                if (TryParseFloatTuple(trimmed, 4, out float[] f))
                {
                    parsed = new Rect(f[0], f[1], f[2], f[3]);
                    return true;
                }
                error = "Invalid Rect '" + rawValue + "'. Expected 'x,y,width,height'.";
                return false;
            }

            error = "Unsupported target type '" + targetType.FullName + "'.";
            return false;
        }

        internal static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            Type direct = Type.GetType(typeName, throwOnError: false);
            if (direct != null)
            {
                return direct;
            }

            Type simpleMatch = null;
            foreach (Type candidate in EnumerateAllTypes())
            {
                if (string.Equals(candidate.FullName, typeName, StringComparison.Ordinal))
                {
                    return candidate;
                }

                if (simpleMatch == null && string.Equals(candidate.Name, typeName, StringComparison.Ordinal))
                {
                    simpleMatch = candidate;
                }
            }

            return simpleMatch;
        }

        private static IEnumerable<Type> EnumerateAllTypes()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                Type[] types;
                try
                {
                    types = assemblies[a].GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                for (int t = 0; t < types.Length; t++)
                {
                    if (types[t] != null)
                    {
                        yield return types[t];
                    }
                }
            }
        }

        private static string GetUnityObjectName(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            if (obj is GameObject go)
            {
                return go.name ?? string.Empty;
            }

            if (obj is Component comp)
            {
                return comp.name ?? string.Empty;
            }

            PropertyInfo nameProp = obj.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.Instance);
            return nameProp != null ? (nameProp.GetValue(obj, null) as string) ?? string.Empty : string.Empty;
        }

        private static UnityEngine.Object[] FindAllUnityObjects(Type targetType)
        {
            try
            {
                return Resources.FindObjectsOfTypeAll(targetType) ?? Array.Empty<UnityEngine.Object>();
            }
            catch
            {
                return Array.Empty<UnityEngine.Object>();
            }
        }

        private static bool MatchesSceneFilter(Scene scene, int sceneHandle, bool isAssetOrHidden, string filter)
        {
            switch (filter)
            {
                case "any":
                    return true;
                case "active":
                    return !isAssetOrHidden && scene.isLoaded;
                case "dont_destroy_on_load":
                    return sceneHandle == -12 || string.Equals(scene.name, "DontDestroyOnLoad", StringComparison.OrdinalIgnoreCase);
                case "hide_and_dont_save":
                    return isAssetOrHidden;
                default:
                    return string.Equals(scene.name, filter, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool MatchesChildFilter(bool hasParent, string filter)
        {
            switch (filter)
            {
                case "root":
                    return !hasParent;
                case "has_parent":
                    return hasParent;
                default:
                    return true;
            }
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var segments = new Stack<string>();
            Transform current = transform;
            int guard = 0;
            while (current != null && guard++ < 64)
            {
                segments.Push(current.name ?? string.Empty);
                current = current.parent;
            }

            return "/" + string.Join("/", segments);
        }

        private static bool TryResolveSingleton(Type type, out object instance, out string memberName, out string memberKind)
        {
            instance = null;
            memberName = string.Empty;
            memberKind = string.Empty;

            for (int i = 0; i < CommonSingletonMemberNames.Length; i++)
            {
                string candidateName = CommonSingletonMemberNames[i];
                PropertyInfo prop = type.GetProperty(candidateName, StaticOnlyFlags);
                if (prop != null && prop.CanRead)
                {
                    try
                    {
                        object val = prop.GetValue(null, null);
                        if (val != null)
                        {
                            instance = val;
                            memberName = prop.Name;
                            memberKind = "property";
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }

                FieldInfo field = type.GetField(candidateName, StaticOnlyFlags);
                if (field != null)
                {
                    try
                    {
                        object val = field.GetValue(null);
                        if (val != null)
                        {
                            instance = val;
                            memberName = field.Name;
                            memberKind = "field";
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static UnityEngine.Object FindUnityObjectByInstanceId(int instanceId)
        {
            UnityEngine.Object[] all = FindAllUnityObjects(typeof(UnityEngine.Object));
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].GetInstanceID() == instanceId)
                {
                    return all[i];
                }
            }
            return null;
        }

        private static bool TryResolveTargetAndMember(
            string typeName,
            string memberName,
            bool useSingleton,
            int instanceId,
            out Type type,
            out object target,
            out MemberInfo member,
            out string error)
        {
            type = null;
            target = null;
            member = null;
            error = null;

            if (string.IsNullOrWhiteSpace(memberName))
            {
                error = "memberName is required.";
                return false;
            }

            if (instanceId != 0)
            {
                UnityEngine.Object instanceObj = FindUnityObjectByInstanceId(instanceId);
                if (instanceObj == null)
                {
                    error = "No UnityEngine.Object found with instanceId " + instanceId + ".";
                    return false;
                }

                target = instanceObj;
                type = string.IsNullOrWhiteSpace(typeName) ? instanceObj.GetType() : ResolveType(typeName.Trim());
                if (type == null)
                {
                    error = "Type '" + typeName + "' not found.";
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    error = "typeName or instanceId is required.";
                    return false;
                }

                type = ResolveType(typeName.Trim());
                if (type == null)
                {
                    error = "Type '" + typeName + "' not found.";
                    return false;
                }

                if (useSingleton && !TryResolveSingleton(type, out target, out _, out _))
                {
                    error = "No live singleton instance found on '" + type.FullName + "'.";
                    return false;
                }
            }

            BindingFlags flags = (useSingleton || instanceId != 0) ? AnyMemberFlags : StaticOnlyFlags;
            string trimmedMember = memberName.Trim();

            PropertyInfo prop = type.GetProperty(trimmedMember, flags);
            if (prop != null)
            {
                member = prop;
                return true;
            }

            FieldInfo field = type.GetField(trimmedMember, flags);
            if (field != null)
            {
                member = field;
                return true;
            }

            error = "Member '" + trimmedMember + "' not found on '" + type.FullName + "' (useSingleton=" + useSingleton + ").";
            return false;
        }

        private static bool TryParseFloatTuple(string text, int expectedCount, out float[] values)
        {
            values = null;
            string cleaned = text.Trim();
            if (cleaned.StartsWith("(", StringComparison.Ordinal) && cleaned.EndsWith(")", StringComparison.Ordinal))
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2);
            }

            string[] parts = cleaned.Split(',');
            if (parts.Length != expectedCount)
            {
                return false;
            }

            values = new float[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) || !float.IsFinite(parsed))
                {
                    return false;
                }
                values[i] = parsed;
            }

            return true;
        }

        private static bool TryParseArgsArray(string argsJson, out List<string> args, out string error)
        {
            args = new List<string>();
            error = null;
            if (string.IsNullOrWhiteSpace(argsJson))
            {
                return true;
            }

            string trimmed = argsJson.Trim();
            if (!trimmed.StartsWith("[", StringComparison.Ordinal) || !trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                error = "Invalid argsJson array: expected '[...]'.";
                return false;
            }

            string inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
            if (inner.Length == 0)
            {
                return true;
            }

            int i = 0;
            while (i < inner.Length)
            {
                while (i < inner.Length && char.IsWhiteSpace(inner[i]))
                {
                    i++;
                }

                if (i >= inner.Length)
                {
                    break;
                }

                if (inner[i] == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    bool closed = false;
                    while (i < inner.Length)
                    {
                        char ch = inner[i++];
                        if (ch == '\\' && i < inner.Length)
                        {
                            char esc = inner[i++];
                            switch (esc)
                            {
                                case '"': sb.Append('"'); break;
                                case '\\': sb.Append('\\'); break;
                                case '/': sb.Append('/'); break;
                                case 'b': sb.Append('\b'); break;
                                case 'f': sb.Append('\f'); break;
                                case 'n': sb.Append('\n'); break;
                                case 'r': sb.Append('\r'); break;
                                case 't': sb.Append('\t'); break;
                                case 'u':
                                    if (i + 4 <= inner.Length &&
                                        int.TryParse(inner.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                    {
                                        sb.Append((char)code);
                                        i += 4;
                                    }
                                    break;
                                default: sb.Append(esc); break;
                            }
                        }
                        else if (ch == '"')
                        {
                            closed = true;
                            break;
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                    }

                    if (!closed)
                    {
                        error = "Unterminated string literal in argsJson.";
                        return false;
                    }

                    args.Add(sb.ToString());
                }
                else
                {
                    int start = i;
                    while (i < inner.Length && inner[i] != ',')
                    {
                        i++;
                    }
                    string token = inner.Substring(start, i - start).Trim();
                    if (string.Equals(token, "null", StringComparison.Ordinal))
                    {
                        args.Add(null);
                    }
                    else
                    {
                        args.Add(token);
                    }
                }

                while (i < inner.Length && char.IsWhiteSpace(inner[i]))
                {
                    i++;
                }
                if (i < inner.Length)
                {
                    if (inner[i] != ',')
                    {
                        error = "Expected ',' between elements in argsJson.";
                        return false;
                    }
                    i++;
                }
            }

            return true;
        }

        private static class JsonConvert
        {
            public static string SerializeObject(object value)
            {
                var sb = new StringBuilder();
                WriteValue(sb, value);
                return sb.ToString();
            }

            private static void WriteValue(StringBuilder sb, object value)
            {
                if (value == null)
                {
                    sb.Append("null");
                    return;
                }

                if (value is bool b)
                {
                    sb.Append(b ? "true" : "false");
                    return;
                }

                if (value is string s)
                {
                    WriteString(sb, s);
                    return;
                }

                if (value is int || value is long || value is short || value is byte ||
                    value is uint || value is ulong || value is ushort || value is sbyte)
                {
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                }

                if (value is float f)
                {
                    sb.Append(float.IsFinite(f) ? f.ToString("R", CultureInfo.InvariantCulture) : "null");
                    return;
                }

                if (value is double d)
                {
                    sb.Append(double.IsFinite(d) ? d.ToString("R", CultureInfo.InvariantCulture) : "null");
                    return;
                }

                if (value is decimal dec)
                {
                    sb.Append(dec.ToString(CultureInfo.InvariantCulture));
                    return;
                }

                if (value is IEnumerable enumerable)
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in enumerable)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    return;
                }

                PropertyInfo[] props = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                sb.Append('{');
                bool firstProp = true;
                for (int i = 0; i < props.Length; i++)
                {
                    PropertyInfo prop = props[i];
                    if (!prop.CanRead) continue;
                    if (!firstProp) sb.Append(',');
                    firstProp = false;
                    WriteString(sb, prop.Name);
                    sb.Append(':');
                    WriteValue(sb, prop.GetValue(value, null));
                }
                sb.Append('}');
            }

            private static void WriteString(StringBuilder sb, string text)
            {
                sb.Append('"');
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ')
                            {
                                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
                sb.Append('"');
            }
        }

        private static bool MatchesFilter(string candidate, string filter)
        {
            return string.IsNullOrWhiteSpace(filter) ||
                (!string.IsNullOrEmpty(candidate) && candidate.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string SafeValuePreview(object value)
        {
            if (value == null)
            {
                return "null";
            }

            try
            {
                string str = value.ToString();
                if (str == null)
                {
                    return string.Empty;
                }
                return str.Length > 256 ? str.Substring(0, 256) + "..." : str;
            }
            catch (Exception ex)
            {
                return "<ToString failed: " + ex.Message + ">";
            }
        }

        private static object ToSerializableValue(object value)
        {
            if (value == null)
            {
                return null;
            }

            Type type = value.GetType();
            if (type.IsPrimitive || value is string || value is decimal)
            {
                return value;
            }

            if (type.IsEnum)
            {
                return value.ToString();
            }

            if (value is Vector2 v2)
            {
                return new { x = v2.x, y = v2.y };
            }

            if (value is Vector3 v3)
            {
                return new { x = v3.x, y = v3.y, z = v3.z };
            }

            if (value is Quaternion q)
            {
                return new { x = q.x, y = q.y, z = q.z, w = q.w };
            }

            if (value is Color c)
            {
                return new { r = c.r, g = c.g, b = c.b, a = c.a };
            }

            if (value is ICollection collection)
            {
                return new { count = collection.Count, preview = SafeValuePreview(value) };
            }

            return SafeValuePreview(value);
        }

        private static string FormatMethodSignature(MethodBase method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            var parts = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                parts[i] = (parameters[i].ParameterType.Name) + " " + parameters[i].Name;
            }

            return (method.DeclaringType != null ? method.DeclaringType.FullName + "." : string.Empty) +
                method.Name + "(" + string.Join(", ", parts) + ")";
        }

        private static object FormatPatchRecord(MethodBase method, Patches info)
        {
            return new
            {
                targetMethod = FormatMethodSignature(method),
                hasPatches = info != null,
                owners = info != null ? info.Owners : (IList<string>)Array.Empty<string>(),
                prefixes = FormatPatchList(info?.Prefixes),
                postfixes = FormatPatchList(info?.Postfixes),
                transpilers = FormatPatchList(info?.Transpilers),
                finalizers = FormatPatchList(info?.Finalizers)
            };
        }

        private static List<object> FormatPatchList(IEnumerable<Patch> patches)
        {
            var list = new List<object>();
            if (patches == null)
            {
                return list;
            }

            foreach (Patch p in patches)
            {
                list.Add(new
                {
                    owner = p.owner,
                    priority = p.priority,
                    index = p.index,
                    patchMethod = p.PatchMethod != null ? FormatMethodSignature(p.PatchMethod) : string.Empty
                });
            }

            return list;
        }
    }
}
