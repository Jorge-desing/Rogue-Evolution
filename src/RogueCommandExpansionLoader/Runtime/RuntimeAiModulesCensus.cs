using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Modules;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable diagnostic row for a registered compiled AI module.
    /// Contains only primitive values to decouple cleanly from Unity and game types.
    /// </summary>
    public readonly struct AiModuleDiagnosticRow
    {
        public AiModuleDiagnosticRow(string moduleId, int apiVersion, bool isRegistered)
        {
            ModuleId = moduleId ?? string.Empty;
            ApiVersion = apiVersion;
            IsRegistered = isRegistered;
        }

        public string ModuleId { get; }
        public int ApiVersion { get; }
        public bool IsRegistered { get; }
    }

    /// <summary>
    /// Snapshot of all compiled AI modules registered in the active registry.
    /// </summary>
    public sealed class AiModulesCensus
    {
        public int RegisteredModuleCount { get; }
        public IReadOnlyList<AiModuleDiagnosticRow> ModuleRows { get; }

        public AiModulesCensus(int registeredModuleCount, IReadOnlyList<AiModuleDiagnosticRow> moduleRows)
        {
            RegisteredModuleCount = registeredModuleCount;
            ModuleRows = moduleRows ?? new List<AiModuleDiagnosticRow>().AsReadOnly();
        }

        public static AiModulesCensus Empty { get; } =
            new AiModulesCensus(0, new List<AiModuleDiagnosticRow>().AsReadOnly());
    }

    /// <summary>
    /// Reads registered AI modules from <see cref="AiModuleRegistry.Active"/> and projects
    /// them into an agent-readable <see cref="AiModulesCensus"/>.
    /// </summary>
    public static class RuntimeAiModulesCensus
    {
        public static AiModulesCensus Current()
        {
            try
            {
                AiModuleRegistry registry = AiModuleRegistry.Active;
                if (registry == null || registry.RegisteredModules == null)
                {
                    return AiModulesCensus.Empty;
                }

                var rows = new List<AiModuleDiagnosticRow>();
                foreach (IRogueCommandAiModule module in registry.RegisteredModules)
                {
                    if (module == null)
                    {
                        continue;
                    }

                    rows.Add(new AiModuleDiagnosticRow(module.Id, module.ApiVersion, true));
                }

                return new AiModulesCensus(rows.Count, rows.AsReadOnly());
            }
            catch
            {
                return AiModulesCensus.Empty;
            }
        }
    }
}
