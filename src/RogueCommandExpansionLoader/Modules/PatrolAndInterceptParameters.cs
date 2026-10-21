using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct PatrolAndInterceptSettings
    {
        public int BaseIndex;
        public int PatrolRadiusCells;
        public int InterceptRadiusCells;
        public float ScanIntervalSeconds;
        public int MaxPatrolUnits;
    }

    internal static class PatrolAndInterceptParameterParser
    {
        internal const string ModuleId = "sample:patrol-and-intercept";

        public static bool TryParse(ContentModuleParameter[] parameters,
            out PatrolAndInterceptSettings settings, out string diagnostic)
        {
            settings = default(PatrolAndInterceptSettings);
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
                if (parameter.name != "baseIndex" && parameter.name != "patrolRadiusCells" &&
                    parameter.name != "interceptRadiusCells" && parameter.name != "scanIntervalSeconds" &&
                    parameter.name != "maxPatrolUnits")
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

            int baseIndex, patrolRadius, interceptRadius, maxUnits;
            float scanInterval;
            if (!TryInt(values, "baseIndex", 0, 0, 31, out baseIndex))
                return Fail("baseIndex must be an integer between 0 and 31.", out diagnostic);
            if (!TryInt(values, "patrolRadiusCells", 15, 5, 32, out patrolRadius))
                return Fail("patrolRadiusCells must be an integer between 5 and 32.", out diagnostic);
            if (!TryInt(values, "interceptRadiusCells", 10, 5, 32, out interceptRadius))
                return Fail("interceptRadiusCells must be an integer between 5 and 32.", out diagnostic);
            if (!TryFloat(values, "scanIntervalSeconds", 8f, 2f, 60f, out scanInterval))
                return Fail("scanIntervalSeconds must be a finite number between 2 and 60.", out diagnostic);
            if (!TryInt(values, "maxPatrolUnits", 3, 1, 8, out maxUnits))
                return Fail("maxPatrolUnits must be an integer between 1 and 8.", out diagnostic);

            settings = new PatrolAndInterceptSettings
            {
                BaseIndex = baseIndex,
                PatrolRadiusCells = patrolRadius,
                InterceptRadiusCells = interceptRadius,
                ScanIntervalSeconds = scanInterval,
                MaxPatrolUnits = maxUnits
            };
            diagnostic = null;
            return true;
        }

        private static bool Fail(string message, out string diagnostic)
        {
            diagnostic = message;
            return false;
        }

        private static bool TryInt(Dictionary<string, string> values, string name, int fallback,
            int minimum, int maximum, out int result)
        {
            string value;
            if (!values.TryGetValue(name, out value)) { result = fallback; return true; }
            return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) &&
                result >= minimum && result <= maximum;
        }

        private static bool TryFloat(Dictionary<string, string> values, string name, float fallback,
            float minimum, float maximum, out float result)
        {
            string value;
            if (!values.TryGetValue(name, out value)) { result = fallback; return true; }
            return Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                !Single.IsNaN(result) && !Single.IsInfinity(result) && result >= minimum && result <= maximum;
        }
    }

    internal static class PatrolAndInterceptWaypointResolver
    {
        public static bool TrySelectNearest(Vector2Int currentCell, IList<Vector2Int> waypointCells,
            int radiusCells, out int waypointIndex)
        {
            waypointIndex = -1;
            if (waypointCells == null || radiusCells < 1 || radiusCells > 32)
            {
                return false;
            }

            long maximumDistanceSquared = (long)radiusCells * radiusCells;
            long nearestDistanceSquared = Int64.MaxValue;
            for (int i = 0; i < waypointCells.Count; i++)
            {
                long deltaX = (long)waypointCells[i].x - currentCell.x;
                long deltaY = (long)waypointCells[i].y - currentCell.y;
                if (Math.Abs(deltaX) > radiusCells || Math.Abs(deltaY) > radiusCells)
                {
                    continue;
                }

                long distanceSquared = deltaX * deltaX + deltaY * deltaY;
                if (distanceSquared == 0 || distanceSquared > maximumDistanceSquared ||
                    distanceSquared >= nearestDistanceSquared)
                {
                    continue;
                }

                nearestDistanceSquared = distanceSquared;
                waypointIndex = i;
            }

            return waypointIndex >= 0;
        }
    }
}
