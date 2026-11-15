using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct ScheduledBaseBuildSettings
    {
        public string EntityId;
        public int BaseIndex;
        public float IntervalSeconds;
        public int MaximumSuccessfulBuilds;
        public int MaximumAttempts;
    }

    /// <summary>
    /// Parses the data-only configuration for the scheduled base-build AI module.
    /// Kept independent of Unity so its limits can be validated offline.
    /// </summary>
    internal static class ScheduledBaseBuildParameterParser
    {
        internal const string ModuleId = "sample:scheduled-base-build";

        private const int DefaultBaseIndex = 0;
        private const float DefaultIntervalSeconds = 8f;
        private const int DefaultMaximumSuccessfulBuilds = 1;
        private const int DefaultMaximumAttempts = 3;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out ScheduledBaseBuildSettings settings,
            out string diagnostic)
        {
            settings = default(ScheduledBaseBuildSettings);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            parameters = parameters ?? new ContentModuleParameter[0];

            for (int i = 0; i < parameters.Length; i++)
            {
                ContentModuleParameter parameter = parameters[i];
                if (parameter == null || parameter.name == null || parameter.value == null)
                {
                    diagnostic = "Module parameters cannot be null.";
                    return false;
                }

                if (parameter.name != "entityId" && parameter.name != "baseIndex" &&
                    parameter.name != "intervalSeconds" && parameter.name != "maxSuccessfulBuilds" &&
                    parameter.name != "maxAttempts")
                {
                    diagnostic = "Unknown parameter '" + parameter.name + "' for module " + ModuleId + ".";
                    return false;
                }

                if (values.ContainsKey(parameter.name))
                {
                    diagnostic = "Parameter '" + parameter.name + "' may only be supplied once.";
                    return false;
                }

                values.Add(parameter.name, parameter.value);
            }

            string entityId;
            if (values.TryGetValue("entityId", out entityId) && !IsSafeEntityId(entityId))
            {
                diagnostic = "entityId must be a non-empty game entity ID containing only ASCII letters, digits, ':', '.', '_', or '-'.";
                return false;
            }

            int baseIndex;
            if (!TryGetInteger(values, "baseIndex", DefaultBaseIndex, 0, 31, out baseIndex))
            {
                diagnostic = "baseIndex must be an integer between 0 and 31.";
                return false;
            }

            float intervalSeconds;
            if (!TryGetFloat(values, "intervalSeconds", DefaultIntervalSeconds, 1f, 600f,
                out intervalSeconds))
            {
                diagnostic = "intervalSeconds must be a finite number between 1 and 600.";
                return false;
            }

            int maximumSuccessfulBuilds;
            if (!TryGetInteger(values, "maxSuccessfulBuilds", DefaultMaximumSuccessfulBuilds,
                1, 32, out maximumSuccessfulBuilds))
            {
                diagnostic = "maxSuccessfulBuilds must be an integer between 1 and 32.";
                return false;
            }

            int maximumAttempts;
            if (!TryGetInteger(values, "maxAttempts", DefaultMaximumAttempts, 1, 128,
                    out maximumAttempts) || maximumAttempts < maximumSuccessfulBuilds)
            {
                diagnostic = "maxAttempts must be an integer between maxSuccessfulBuilds and 128.";
                return false;
            }

            settings = new ScheduledBaseBuildSettings
            {
                EntityId = entityId,
                BaseIndex = baseIndex,
                IntervalSeconds = intervalSeconds,
                MaximumSuccessfulBuilds = maximumSuccessfulBuilds,
                MaximumAttempts = maximumAttempts
            };
            diagnostic = null;
            return true;
        }

        private static bool TryGetInteger(
            Dictionary<string, string> values,
            string name,
            int defaultValue,
            int minimum,
            int maximum,
            out int result)
        {
            string value;
            if (!values.TryGetValue(name, out value))
            {
                result = defaultValue;
                return true;
            }

            return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) &&
                result >= minimum && result <= maximum;
        }

        private static bool TryGetFloat(
            Dictionary<string, string> values,
            string name,
            float defaultValue,
            float minimum,
            float maximum,
            out float result)
        {
            string value;
            if (!values.TryGetValue(name, out value))
            {
                result = defaultValue;
                return true;
            }

            return Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                !Single.IsNaN(result) && !Single.IsInfinity(result) && result >= minimum && result <= maximum;
        }

        private static bool IsSafeEntityId(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 128)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool valid = character >= 'a' && character <= 'z' || character >= 'A' && character <= 'Z' ||
                    character >= '0' && character <= '9' || character == ':' || character == '.' ||
                    character == '_' || character == '-';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal struct ScheduledBaseBuildCandidate
    {
        public string EntityId;
        public bool IsBuilding;
        public int MaxLiving;
        public int LivingCount;
    }

    internal static class ScheduledBaseBuildLimitPolicy
    {
        public static bool HasRemainingCapacity(int livingCount, int maxLiving)
        {
            return livingCount >= 0 && maxLiving > 0 && livingCount < maxLiving;
        }

        public static bool IsModNamespacedEntity(string entityId)
        {
            return !String.IsNullOrWhiteSpace(entityId) && entityId.IndexOf(':') >= 0;
        }

        /// <summary>
        /// Selects the next constructable building candidate.
        /// When requestedEntityId is omitted, prioritizes mod factories (namespaced IDs) that
        /// currently have remaining living capacity over native buildings such as PCXBaseControl.
        /// </summary>
        public static int SelectNextCandidateIndex(
            IList<ScheduledBaseBuildCandidate> candidates,
            string requestedEntityId)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return -1;
            }

            if (requestedEntityId != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    ScheduledBaseBuildCandidate candidate = candidates[i];
                    if (candidate.IsBuilding &&
                        String.Equals(candidate.EntityId, requestedEntityId, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }

                return -1;
            }

            int firstModWithCapacity = -1;
            int firstNativeWithCapacity = -1;
            int firstModValid = -1;
            int firstNativeValid = -1;

            for (int i = 0; i < candidates.Count; i++)
            {
                ScheduledBaseBuildCandidate candidate = candidates[i];
                if (!candidate.IsBuilding || String.IsNullOrWhiteSpace(candidate.EntityId) || candidate.MaxLiving < 1)
                {
                    continue;
                }

                bool isMod = IsModNamespacedEntity(candidate.EntityId);
                bool hasCapacity = HasRemainingCapacity(candidate.LivingCount, candidate.MaxLiving);

                if (isMod)
                {
                    if (firstModValid < 0)
                    {
                        firstModValid = i;
                    }

                    if (hasCapacity && firstModWithCapacity < 0)
                    {
                        firstModWithCapacity = i;
                    }
                }
                else
                {
                    if (firstNativeValid < 0)
                    {
                        firstNativeValid = i;
                    }

                    if (hasCapacity && firstNativeWithCapacity < 0)
                    {
                        firstNativeWithCapacity = i;
                    }
                }
            }

            if (firstModWithCapacity >= 0)
            {
                return firstModWithCapacity;
            }

            if (firstNativeWithCapacity >= 0)
            {
                return firstNativeWithCapacity;
            }

            return firstModValid >= 0 ? firstModValid : firstNativeValid;
        }

        /// <summary>
        /// Interleaves namespaced entity IDs round-robin by pack prefix (before ':') so multiple active packs
        /// receive balanced representation rather than alphabetical starvation.
        /// </summary>
        public static List<string> InterleaveEntityIdsByPack(IReadOnlyList<string> orderedIds)
        {
            var result = new List<string>();
            if (orderedIds == null || orderedIds.Count == 0)
            {
                return result;
            }

            var packOrder = new List<string>();
            var buckets = new Dictionary<string, Queue<string>>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < orderedIds.Count; i++)
            {
                string id = orderedIds[i];
                if (String.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    continue;
                }

                int colonIndex = id.IndexOf(':');
                string packPrefix = colonIndex > 0 ? id.Substring(0, colonIndex) : String.Empty;
                Queue<string> queue;
                if (!buckets.TryGetValue(packPrefix, out queue))
                {
                    queue = new Queue<string>();
                    buckets.Add(packPrefix, queue);
                    packOrder.Add(packPrefix);
                }

                queue.Enqueue(id);
            }

            bool remaining = true;
            while (remaining)
            {
                remaining = false;
                for (int i = 0; i < packOrder.Count; i++)
                {
                    Queue<string> queue = buckets[packOrder[i]];
                    if (queue.Count > 0)
                    {
                        result.Add(queue.Dequeue());
                        if (queue.Count > 0)
                        {
                            remaining = true;
                        }
                    }
                }
            }

            return result;
        }
    }
}
