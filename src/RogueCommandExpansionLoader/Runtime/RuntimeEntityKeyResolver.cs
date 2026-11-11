using System;
using System.Collections.Generic;
using System.Globalization;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Resolves live entities by their unique runtime key. Legacy entity IDs and
    /// object names remain accepted only when they identify exactly one entity.
    /// </summary>
    internal static class RuntimeEntityKeyResolver
    {
        public static EntityController FindUniqueEntityByKey(ExistingControllers existing, string key)
        {
            if (existing == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            List<EntityController> candidates = GetKnownLiveEntities(existing);
            string normalizedKey = key.Trim();
            bool isUniqueId = int.TryParse(
                normalizedKey,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int requestedUniqueId) && requestedUniqueId > 0;

            EntityController match = null;
            for (int index = 0; index < candidates.Count; index++)
            {
                EntityController candidate = candidates[index];
                if (candidate == null || candidate.IsDestroyed || candidate.DestroyHasBeenStarted)
                {
                    continue;
                }

                bool matches = isUniqueId
                    ? candidate.UniqueEntityId == requestedUniqueId
                    : string.Equals(candidate.EntityId ?? string.Empty, normalizedKey, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(candidate.name ?? string.Empty, normalizedKey, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    continue;
                }

                if (match != null && !ReferenceEquals(match, candidate))
                {
                    return null;
                }

                match = candidate;
            }

            return match;
        }

        internal static List<EntityController> GetKnownLiveEntities(ExistingControllers existing)
        {
            var candidates = new List<EntityController>();
            var seenInstanceIds = new HashSet<int>();

            if (existing == null)
            {
                return candidates;
            }

            try
            {
                AddCandidates(candidates, seenInstanceIds, existing.PlayerUnits());
                AddCandidates(candidates, seenInstanceIds, existing.AiUnits());
                AddCandidates(candidates, seenInstanceIds, existing.AiBuildings());
                AddCandidates(candidates, seenInstanceIds, existing.PlayerBuildings());
                AddCandidates(candidates, seenInstanceIds, existing.PlayerBuildingsKnownToAi());

                EntityController hq = existing.EngineeringHq();
                if (hq != null)
                {
                    AddCandidate(candidates, seenInstanceIds, hq);
                }

                AddCandidates(candidates, seenInstanceIds, existing.Harvesters);
            }
            catch
            {
                // Keep already collected candidates; callers still fail closed on ambiguity.
            }

            return candidates;
        }

        private static void AddCandidates(
            List<EntityController> destination,
            HashSet<int> seenInstanceIds,
            IEnumerable<EntityController> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (EntityController candidate in source)
            {
                AddCandidate(destination, seenInstanceIds, candidate);
            }
        }

        private static void AddCandidate(
            List<EntityController> destination,
            HashSet<int> seenInstanceIds,
            EntityController candidate)
        {
            if (candidate == null || candidate.IsDestroyed || candidate.DestroyHasBeenStarted)
            {
                return;
            }

            int instanceId;
            try
            {
                instanceId = candidate.GetInstanceID();
            }
            catch
            {
                return;
            }

            if (seenInstanceIds.Add(instanceId))
            {
                destination.Add(candidate);
            }
        }
    }
}
