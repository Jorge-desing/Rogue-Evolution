using System;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    internal static class ReactiveBaseDefenseThreatRange
    {
        internal static float DistanceSquared(Vector2Int first, Vector2Int second)
        {
            float x = first.x - second.x;
            float y = first.y - second.y;
            return x * x + y * y;
        }

        internal static bool IsWithinRadius(Vector2Int first, Vector2Int second, float radiusCells)
        {
            if (Single.IsNaN(radiusCells) || Single.IsInfinity(radiusCells) || radiusCells < 0f)
            {
                return false;
            }

            return DistanceSquared(first, second) <= radiusCells * radiusCells;
        }
    }
}
