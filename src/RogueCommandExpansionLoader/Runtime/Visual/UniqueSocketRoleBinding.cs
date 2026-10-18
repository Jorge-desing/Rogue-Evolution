using System;

namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>
    /// Resolves a behavior role only when its hierarchy contains one distinct transform.
    /// Multiple aliases for the same role are ambiguous and therefore disable that role.
    /// </summary>
    public sealed class UniqueSocketRoleBinding<T> where T : class
    {
        public T Value { get; private set; }
        public bool IsAmbiguous { get; private set; }

        public void Add(T candidate)
        {
            if (candidate == null || IsAmbiguous)
            {
                return;
            }

            if (Value == null)
            {
                Value = candidate;
                return;
            }

            if (!ReferenceEquals(Value, candidate))
            {
                Value = null;
                IsAmbiguous = true;
            }
        }

        public static bool TryResolveVisual(
            UniqueSocketRoleBinding<T> preferredRole,
            T uniquePreferredVisual,
            T fallbackVisual,
            out T visual)
        {
            visual = null;
            if (preferredRole != null && preferredRole.IsAmbiguous)
            {
                return false;
            }

            if (uniquePreferredVisual != null)
            {
                visual = uniquePreferredVisual;
                return true;
            }

            if (fallbackVisual != null)
            {
                visual = fallbackVisual;
                return true;
            }

            return false;
        }
    }
}
