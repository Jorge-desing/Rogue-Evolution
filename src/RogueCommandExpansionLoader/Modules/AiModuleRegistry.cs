using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Registry for trusted code compiled into the loader assembly. Packs can
    /// reference IDs here but cannot add or load executable assemblies.
    /// </summary>
    public sealed class AiModuleRegistry : IAiModuleValidator
    {
        public const int CurrentApiVersion = 1;

        private readonly Dictionary<string, IRogueCommandAiModule> modules =
            new Dictionary<string, IRogueCommandAiModule>(StringComparer.Ordinal);

        public void Register(IRogueCommandAiModule module)
        {
            if (module == null)
            {
                throw new ArgumentNullException("module");
            }

            if (string.IsNullOrWhiteSpace(module.Id))
            {
                throw new ArgumentException("AI module IDs cannot be empty.", "module");
            }

            if (module.ApiVersion != CurrentApiVersion)
            {
                throw new InvalidOperationException("AI module " + module.Id + " uses API version " + module.ApiVersion + "; expected " + CurrentApiVersion + ".");
            }

            if (!IsNamespacedId(module.Id))
            {
                throw new ArgumentException("AI module IDs must be namespaced lowercase IDs.", "module");
            }

            if (modules.ContainsKey(module.Id))
            {
                throw new InvalidOperationException("AI module ID is already registered: " + module.Id);
            }

            modules.Add(module.Id, module);
        }

        public bool TryValidate(string moduleId, ContentModuleParameter[] parameters, out string diagnostic)
        {
            IRogueCommandAiModule module;
            if (!modules.TryGetValue(moduleId ?? string.Empty, out module))
            {
                diagnostic = "Unknown compiled AI module: " + (moduleId ?? "<null>");
                return false;
            }

            if (module.ApiVersion != CurrentApiVersion)
            {
                diagnostic = "AI module " + moduleId + " has an incompatible API version.";
                return false;
            }

            try
            {
                return module.TryValidateParameters(parameters ?? new ContentModuleParameter[0], out diagnostic);
            }
            catch (Exception exception)
            {
                diagnostic = "AI module validator threw " + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public bool TryCreateBehaviour(
            string moduleId,
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters,
            out IRogueCommandAiRuntimeBehaviour behaviour,
            out string diagnostic)
        {
            behaviour = null;
            IRogueCommandAiModule module;
            if (!modules.TryGetValue(moduleId ?? string.Empty, out module))
            {
                diagnostic = "Unknown compiled AI module: " + (moduleId ?? "<null>");
                return false;
            }

            if (gameBehaviour == null)
            {
                diagnostic = "A game-owned DefaultAiBehaviour instance is required.";
                return false;
            }

            if (!TryValidate(moduleId, parameters, out diagnostic))
            {
                return false;
            }

            try
            {
                behaviour = module.CreateBehaviour(gameBehaviour, parameters ?? new ContentModuleParameter[0]);
                if (behaviour == null)
                {
                    behaviour = null;
                    diagnostic = "AI module " + moduleId + " did not create a runtime behaviour.";
                    return false;
                }

                diagnostic = null;
                return true;
            }
            catch (Exception exception)
            {
                behaviour = null;
                diagnostic = "AI module factory threw " + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static bool IsNamespacedId(string value)
        {
            int separator = value.IndexOf(':');
            if (separator <= 0 || separator == value.Length - 1 || separator != value.LastIndexOf(':'))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool valid = c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == ':' || c == '.' || c == '_' || c == '-';
                if (!valid || (i == 0 && !(c >= 'a' && c <= 'z')) || (i == separator + 1 && !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
