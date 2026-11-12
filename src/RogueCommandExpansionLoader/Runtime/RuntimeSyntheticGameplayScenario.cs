using System;
using System.Collections.Generic;
using System.Globalization;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Parses bounded primitive arguments used by the MCP bridge when a reflection
    /// endpoint cannot safely materialize generic CLR collections. The parser is
    /// deliberately independent of Unity and external JSON libraries.
    /// </summary>
    public static class RuntimeBridgeArgumentParser
    {
        public static bool TryParseStringCsv(
            string csv,
            int minimumCount,
            int maximumCount,
            out IReadOnlyList<string> values,
            out string diagnostic)
        {
            values = new List<string>().AsReadOnly();
            diagnostic = string.Empty;
            if (string.IsNullOrWhiteSpace(csv))
            {
                diagnostic = "CSV argument cannot be empty.";
                return false;
            }

            string[] rawValues = csv.Split(new[] { ',', ';' }, StringSplitOptions.None);
            var parsed = new List<string>(rawValues.Length);
            for (int i = 0; i < rawValues.Length; i++)
            {
                string value = rawValues[i] == null ? string.Empty : rawValues[i].Trim();
                if (value.Length == 0)
                {
                    diagnostic = "CSV argument contains an empty entry.";
                    return false;
                }
                parsed.Add(value);
            }

            if (parsed.Count < minimumCount || parsed.Count > maximumCount)
            {
                diagnostic = "CSV argument must contain between " + minimumCount + " and " + maximumCount + " entries.";
                return false;
            }

            values = parsed.AsReadOnly();
            return true;
        }

        public static bool TryParseFloatCsv(
            string csv,
            int minimumCount,
            int maximumCount,
            out IReadOnlyList<float> values,
            out string diagnostic)
        {
            values = new List<float>().AsReadOnly();
            diagnostic = string.Empty;
            IReadOnlyList<string> stringValues;
            if (!TryParseStringCsv(csv, minimumCount, maximumCount, out stringValues, out diagnostic))
            {
                return false;
            }

            var parsed = new List<float>(stringValues.Count);
            for (int i = 0; i < stringValues.Count; i++)
            {
                float value;
                if (!float.TryParse(
                        stringValues[i],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value) || float.IsNaN(value) || float.IsInfinity(value))
                {
                    diagnostic = "CSV argument contains a non-finite number at index " + i + ".";
                    return false;
                }
                parsed.Add(value);
            }

            values = parsed.AsReadOnly();
            return true;
        }

        public static string JoinCsv(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    parts.Add(values[i].Trim());
                }
            }
            return string.Join(",", parts.ToArray());
        }
    }

    /// <summary>
    /// Pure, bounded plan for the repeatable synthetic gameplay seam. It contains
    /// only primitive CLR values and never touches Unity or the live registry.
    /// </summary>
    public sealed class SyntheticGameplayScenarioPlan
    {
        private static readonly string[] SupportedFormations =
        {
            "Line", "Wedge", "Column", "Box", "Surround",
        };

        private SyntheticGameplayScenarioPlan(
            IReadOnlyList<string> unitIds,
            string formationType,
            float targetX,
            float targetZ,
            float spacing)
        {
            UnitIds = unitIds;
            FormationType = formationType;
            TargetX = targetX;
            TargetZ = targetZ;
            Spacing = spacing;
        }

        public IReadOnlyList<string> UnitIds { get; }
        public string FormationType { get; }
        public float TargetX { get; }
        public float TargetZ { get; }
        public float Spacing { get; }

        public static bool TryCreate(
            string unitIdsCsv,
            string formationType,
            float targetX,
            float targetZ,
            float spacing,
            out SyntheticGameplayScenarioPlan plan,
            out string diagnostic)
        {
            plan = null;
            diagnostic = string.Empty;
            IReadOnlyList<string> unitIds;
            if (!RuntimeBridgeArgumentParser.TryParseStringCsv(unitIdsCsv, 1, 8, out unitIds, out diagnostic))
            {
                return false;
            }

            if (float.IsNaN(targetX) || float.IsInfinity(targetX) ||
                float.IsNaN(targetZ) || float.IsInfinity(targetZ) ||
                float.IsNaN(spacing) || float.IsInfinity(spacing))
            {
                diagnostic = "Synthetic scenario coordinates and spacing must be finite.";
                return false;
            }

            string normalizedFormation = NormalizeFormation(formationType);
            if (normalizedFormation.Length == 0)
            {
                diagnostic = "Unsupported formation type. Supported: Line, Wedge, Column, Box, Surround.";
                return false;
            }

            if (spacing < 1.5f || spacing > 25.0f)
            {
                diagnostic = "Synthetic scenario spacing must be between 1.5 and 25 meters.";
                return false;
            }

            plan = new SyntheticGameplayScenarioPlan(
                unitIds,
                normalizedFormation,
                targetX,
                targetZ,
                spacing);
            return true;
        }

        public static string NormalizeFormation(string formationType)
        {
            if (string.IsNullOrWhiteSpace(formationType))
            {
                return string.Empty;
            }

            string candidate = formationType.Trim();
            for (int i = 0; i < SupportedFormations.Length; i++)
            {
                if (string.Equals(candidate, SupportedFormations[i], StringComparison.OrdinalIgnoreCase))
                {
                    return SupportedFormations[i];
                }
            }
            return string.Empty;
        }
    }

    /// <summary>
    /// Primitive result for the one-call synthetic gameplay seam. The two bounded
    /// collections are capped by the plan; omission is still reported first.
    /// </summary>
    public sealed class SyntheticGameplayScenarioResult
    {
        public SyntheticGameplayScenarioResult(
            bool success,
            bool pausedPrecondition,
            int requestedUnitCount,
            int spawnedUnitCount,
            int stanceAffectedCount,
            int formationTargetedCount,
            int formationDispatchedCount,
            int loadoutSuccessCount,
            int affixSuccessCount,
            int veterancySuccessCount,
            int visualFeedbackSuccessCount,
            int auraLinkedFollowerCount,
            int pulseAffectedEntityCount,
            int activeHazardCount,
            int mechanicsInstanceCount,
            int destroyedSyntheticEntityCount,
            int omittedFailureCount,
            string formationType,
            IReadOnlyList<string> createdEntityKeys,
            IReadOnlyList<string> failures,
            string message)
        {
            Success = success;
            PausedPrecondition = pausedPrecondition;
            RequestedUnitCount = Math.Max(0, requestedUnitCount);
            SpawnedUnitCount = Math.Max(0, spawnedUnitCount);
            StanceAffectedCount = Math.Max(0, stanceAffectedCount);
            FormationTargetedCount = Math.Max(0, formationTargetedCount);
            FormationDispatchedCount = Math.Max(0, formationDispatchedCount);
            LoadoutSuccessCount = Math.Max(0, loadoutSuccessCount);
            AffixSuccessCount = Math.Max(0, affixSuccessCount);
            VeterancySuccessCount = Math.Max(0, veterancySuccessCount);
            VisualFeedbackSuccessCount = Math.Max(0, visualFeedbackSuccessCount);
            AuraLinkedFollowerCount = Math.Max(0, auraLinkedFollowerCount);
            PulseAffectedEntityCount = Math.Max(0, pulseAffectedEntityCount);
            ActiveHazardCount = Math.Max(0, activeHazardCount);
            MechanicsInstanceCount = Math.Max(0, mechanicsInstanceCount);
            DestroyedSyntheticEntityCount = Math.Max(0, destroyedSyntheticEntityCount);
            OmittedFailureCount = Math.Max(0, omittedFailureCount);
            FormationType = formationType ?? string.Empty;
            CreatedEntityKeys = createdEntityKeys ?? new List<string>().AsReadOnly();
            Failures = failures ?? new List<string>().AsReadOnly();
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public bool PausedPrecondition { get; }
        public int RequestedUnitCount { get; }
        public int SpawnedUnitCount { get; }
        public int StanceAffectedCount { get; }
        public int FormationTargetedCount { get; }
        public int FormationDispatchedCount { get; }
        public int LoadoutSuccessCount { get; }
        public int AffixSuccessCount { get; }
        public int VeterancySuccessCount { get; }
        public int VisualFeedbackSuccessCount { get; }
        public int AuraLinkedFollowerCount { get; }
        public int PulseAffectedEntityCount { get; }
        public int ActiveHazardCount { get; }
        public int MechanicsInstanceCount { get; }
        public int DestroyedSyntheticEntityCount { get; }
        public int OmittedFailureCount { get; }
        public string FormationType { get; }
        public IReadOnlyList<string> CreatedEntityKeys { get; }
        public IReadOnlyList<string> Failures { get; }
        public string Message { get; }
    }
}
