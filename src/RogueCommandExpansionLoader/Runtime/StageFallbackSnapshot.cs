using System;
using System.Collections;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>Builds a positional fallback list without changing the game's shared list.</summary>
    public static class StageFallbackSnapshot
    {
        public static IList Build(IList nativeFallbacks, IList stages, Func<object, object> resolveFallback)
        {
            if (nativeFallbacks == null || stages == null || resolveFallback == null)
            {
                throw new ArgumentNullException("Fallback list, stages, and resolver are required.");
            }

            if (stages.Count <= nativeFallbacks.Count)
            {
                return nativeFallbacks;
            }

            IList snapshot = Activator.CreateInstance(nativeFallbacks.GetType()) as IList;
            if (snapshot == null || snapshot.IsReadOnly || snapshot.IsFixedSize)
            {
                throw new InvalidOperationException("The game's fallback list cannot produce a writable snapshot.");
            }

            foreach (object fallback in nativeFallbacks)
            {
                snapshot.Add(fallback);
            }

            for (int index = nativeFallbacks.Count; index < stages.Count; index++)
            {
                object fallback = resolveFallback(stages[index]);
                if (fallback == null)
                {
                    throw new InvalidOperationException("No validated fallback exists for stage index " + index + ".");
                }

                // A repeated generator still needs its own positional slot.
                snapshot.Add(fallback);
            }

            return snapshot;
        }
    }
}
