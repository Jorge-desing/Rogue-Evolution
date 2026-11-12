using System;
using System.Collections.Generic;
using System.Reflection;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Minimal reflection wrapper for Steamworks per-app Cloud state. It never
    /// reads or writes save files and never changes the account-wide setting.
    /// </summary>
    public static class RuntimeSteamCloudController
    {
        private const string RemoteStorageTypeName = "Steamworks.SteamRemoteStorage";
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        public static string GetStateJson()
        {
            return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(ReadState());
        }

        public static string SetAppEnabledJson(bool enabled, bool explicitUserAuthorization)
        {
            if (!explicitUserAuthorization)
            {
                return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["requestedAppEnabled"] = enabled,
                    ["message"] = "Steam Cloud app setting requires explicit user authorization"
                });
            }

            Dictionary<string, object> before = ReadState();
            if (!(bool)before["success"])
            {
                before["requestedAppEnabled"] = enabled;
                before["explicitUserAuthorization"] = true;
                return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(before);
            }

            bool wasEnabled = (bool)before["enabledForApp"];
            bool changed = wasEnabled != enabled;
            Dictionary<string, object> after = before;
            if (changed)
            {
                bool initializedHere;
                Type remoteStorage;
                string initializationError;
                if (!TryBeginApi(out remoteStorage, out initializedHere, out initializationError))
                {
                    return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(new Dictionary<string, object>
                    {
                        ["success"] = false,
                        ["requestedAppEnabled"] = enabled,
                        ["explicitUserAuthorization"] = true,
                        ["changed"] = false,
                        ["error"] = initializationError
                    });
                }
                try
                {
                    MethodInfo setter = FindMethod(remoteStorage, "SetCloudEnabledForApp", typeof(void), typeof(bool));
                    setter.Invoke(null, new object[] { enabled });
                    after = ReadStateWithinSession(remoteStorage);
                }
                catch (Exception ex)
                {
                    after = null;
                    return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(new Dictionary<string, object>
                    {
                        ["success"] = false,
                        ["requestedAppEnabled"] = enabled,
                        ["explicitUserAuthorization"] = true,
                        ["changed"] = false,
                        ["error"] = "Steamworks setter failed: " + Unwrap(ex).GetType().Name
                    });
                }
                finally
                {
                    EndApi(remoteStorage, initializedHere);
                }
            }

            after["requestedAppEnabled"] = enabled;
            after["explicitUserAuthorization"] = true;
            after["changed"] = changed;
            if ((bool)after["success"])
            {
                bool verified = (bool)after["enabledForApp"] == enabled;
                after["success"] = verified;
                after["verified"] = verified;
                if (!verified) after["message"] = "Steamworks returned without matching the requested per-app Cloud state";
            }
            return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(after);
        }

        private static Dictionary<string, object> ReadState()
        {
            bool initializedHere;
            Type remoteStorage;
            string initializationError;
            if (!TryBeginApi(out remoteStorage, out initializedHere, out initializationError))
            {
                return new Dictionary<string, object>
                {
                    ["kind"] = "steamCloudState",
                    ["success"] = false,
                    ["scope"] = "Rogue Command app only",
                    ["error"] = initializationError
                };
            }

            try
            {
                return ReadStateWithinSession(remoteStorage);
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    ["kind"] = "steamCloudState",
                    ["success"] = false,
                    ["scope"] = "Rogue Command app only",
                    ["error"] = "Steamworks Cloud state unavailable: " + Unwrap(ex).GetType().Name
                };
            }
            finally
            {
                EndApi(remoteStorage, initializedHere);
            }
        }

        private static Dictionary<string, object> ReadStateWithinSession(Type remoteStorage)
        {
            MethodInfo accountMethod = FindMethod(remoteStorage, "IsCloudEnabledForAccount", typeof(bool));
            MethodInfo appMethod = FindMethod(remoteStorage, "IsCloudEnabledForApp", typeof(bool));
            return new Dictionary<string, object>
            {
                ["kind"] = "steamCloudState",
                ["success"] = true,
                ["scope"] = "Rogue Command app only",
                ["enabledForAccount"] = (bool)accountMethod.Invoke(null, null),
                ["enabledForApp"] = (bool)appMethod.Invoke(null, null),
                ["source"] = "Steamworks ISteamRemoteStorage"
            };
        }

        private static bool TryBeginApi(out Type remoteStorage, out bool initializedHere, out string error)
        {
            remoteStorage = null;
            initializedHere = false;
            error = string.Empty;
            try
            {
                remoteStorage = FindRemoteStorageType();
                MethodInfo appMethod = FindMethod(remoteStorage, "IsCloudEnabledForApp", typeof(bool));
                appMethod.Invoke(null, null);
                return true;
            }
            catch (Exception ex)
            {
                Exception cause = Unwrap(ex);
                if (!(cause is InvalidOperationException)
                    || cause.Message == null
                    || cause.Message.IndexOf("Steamworks is not initialized", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    error = "Steamworks Cloud API unavailable: " + cause.GetType().Name;
                    return false;
                }
            }

            try
            {
                Type steamApi = FindType("Steamworks.SteamAPI");
                MethodInfo init = FindMethod(steamApi, "Init", typeof(bool));
                if (!(bool)init.Invoke(null, null))
                {
                    error = "SteamAPI.Init returned false; app Cloud setting was not changed";
                    return false;
                }
                initializedHere = true;
                remoteStorage = FindRemoteStorageType();
                return true;
            }
            catch (Exception ex)
            {
                error = "SteamAPI initialization failed: " + Unwrap(ex).GetType().Name;
                return false;
            }
        }

        private static void EndApi(Type remoteStorage, bool initializedHere)
        {
            if (!initializedHere) return;
            try
            {
                Type steamApi = FindType("Steamworks.SteamAPI");
                MethodInfo shutdown = FindMethod(steamApi, "Shutdown", typeof(void));
                shutdown.Invoke(null, null);
            }
            catch
            {
            }
        }

        private static Type FindRemoteStorageType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type found = assemblies[i] != null ? assemblies[i].GetType(RemoteStorageTypeName, false) : null;
                if (found != null) return found;
            }
            throw new TypeLoadException(RemoteStorageTypeName);
        }

        private static Type FindType(string typeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type found = assemblies[i] != null ? assemblies[i].GetType(typeName, false) : null;
                if (found != null) return found;
            }
            throw new TypeLoadException(typeName);
        }

        private static MethodInfo FindMethod(Type declaringType, string name, Type returnType, params Type[] parameters)
        {
            if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));
            MethodInfo method = declaringType.GetMethod(name, PublicStatic, null, parameters ?? Type.EmptyTypes, null);
            if (method == null || method.ReturnType != returnType || !method.IsStatic)
            {
                throw new MissingMethodException(declaringType.FullName, name);
            }
            return method;
        }

        private static Exception Unwrap(Exception exception)
        {
            TargetInvocationException invocation = exception as TargetInvocationException;
            return invocation != null && invocation.InnerException != null ? invocation.InnerException : exception;
        }
    }
}
