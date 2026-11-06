using System;
using System.Collections.Generic;
using System.Globalization;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>One top-level key whose value differs between two snapshots.</summary>
    public readonly struct SnapshotDiffChange
    {
        public SnapshotDiffChange(string key, string beforeValue, string afterValue)
        {
            Key = key ?? string.Empty;
            BeforeValue = beforeValue ?? string.Empty;
            AfterValue = afterValue ?? string.Empty;
        }

        public string Key { get; }
        public string BeforeValue { get; }
        public string AfterValue { get; }
    }

    /// <summary>
    /// Result of a top-level snapshot comparison. Total counts are always exact;
    /// detail rows are bounded per category with the remainder reported as
    /// omission counters so tail truncation can never hide loss diagnostics.
    /// </summary>
    public sealed class SnapshotDiffResult
    {
        public SnapshotDiffResult(
            bool isValid,
            string error,
            int addedKeyCount,
            int removedKeyCount,
            int changedKeyCount,
            int omittedAddedCount,
            int omittedRemovedCount,
            int omittedChangedCount,
            int addedRowCount,
            int removedRowCount,
            int changedRowCount,
            IReadOnlyList<string> addedKeys,
            IReadOnlyList<string> removedKeys,
            IReadOnlyList<SnapshotDiffChange> changedKeys)
        {
            IsValid = isValid;
            Error = error ?? string.Empty;
            AddedKeyCount = addedKeyCount;
            RemovedKeyCount = removedKeyCount;
            ChangedKeyCount = changedKeyCount;
            OmittedAddedCount = omittedAddedCount;
            OmittedRemovedCount = omittedRemovedCount;
            OmittedChangedCount = omittedChangedCount;
            AddedRowCount = addedRowCount;
            RemovedRowCount = removedRowCount;
            ChangedRowCount = changedRowCount;
            AddedKeys = addedKeys ?? new List<string>().AsReadOnly();
            RemovedKeys = removedKeys ?? new List<string>().AsReadOnly();
            ChangedKeys = changedKeys ?? new List<SnapshotDiffChange>().AsReadOnly();
        }

        public bool IsValid { get; }
        public string Error { get; }
        public int AddedKeyCount { get; }
        public int RemovedKeyCount { get; }
        public int ChangedKeyCount { get; }
        /// <summary>Loss/omission counters placed first in serialization.</summary>
        public int OmittedAddedCount { get; }
        public int OmittedRemovedCount { get; }
        public int OmittedChangedCount { get; }
        public int AddedRowCount { get; }
        public int RemovedRowCount { get; }
        public int ChangedRowCount { get; }
        public int TotalChangeCount => AddedKeyCount + RemovedKeyCount + ChangedKeyCount;
        public IReadOnlyList<string> AddedKeys { get; }
        public IReadOnlyList<string> RemovedKeys { get; }
        public IReadOnlyList<SnapshotDiffChange> ChangedKeys { get; }

        public static SnapshotDiffResult Empty { get; } = new SnapshotDiffResult(
            true, string.Empty, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            new List<string>().AsReadOnly(),
            new List<string>().AsReadOnly(),
            new List<SnapshotDiffChange>().AsReadOnly());
    }

    /// <summary>
    /// Pure top-level comparison of two snapshot dictionaries. Null inputs are
    /// treated as empty dictionaries. Detail rows are capped per category at
    /// <c>maxChanges</c> (clamped to 1..128) with exact totals and omission
    /// counters preserved. JSON parsing lives in the diagnostics component;
    /// this type stays dependency-free so it runs offline in the harness.
    /// </summary>
    public static class RuntimeSnapshotDiff
    {
        private const int MinimumMaxChanges = 1;
        private const int MaximumMaxChanges = 128;

        public static SnapshotDiffResult DiffDictionaries(
            IDictionary<string, object> before,
            IDictionary<string, object> after,
            int maxChanges)
        {
            int limit = maxChanges;
            if (limit < MinimumMaxChanges)
            {
                limit = MinimumMaxChanges;
            }
            else if (limit > MaximumMaxChanges)
            {
                limit = MaximumMaxChanges;
            }

            IDictionary<string, object> safeBefore = before ?? new Dictionary<string, object>();
            IDictionary<string, object> safeAfter = after ?? new Dictionary<string, object>();

            var added = new List<string>();
            var removed = new List<string>();
            var changed = new List<SnapshotDiffChange>();
            try
            {
                foreach (string key in safeAfter.Keys)
                {
                    if (key == null)
                    {
                        continue;
                    }

                    if (!safeBefore.TryGetValue(key, out object beforeValue))
                    {
                        added.Add(key);
                    }
                    else if (!ValuesEqual(beforeValue, safeAfter[key]))
                    {
                        changed.Add(new SnapshotDiffChange(
                            key, FormatValue(beforeValue), FormatValue(safeAfter[key])));
                    }
                }

                foreach (string key in safeBefore.Keys)
                {
                    if (key != null && !safeAfter.ContainsKey(key))
                    {
                        removed.Add(key);
                    }
                }
            }
            catch (Exception exception)
            {
                return new SnapshotDiffResult(
                    false, exception.GetType().Name + ": " + exception.Message,
                    0, 0, 0, 0, 0, 0, 0, 0, 0,
                    new List<string>().AsReadOnly(),
                    new List<string>().AsReadOnly(),
                    new List<SnapshotDiffChange>().AsReadOnly());
            }

            added.Sort(StringComparer.Ordinal);
            removed.Sort(StringComparer.Ordinal);
            changed.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));

            return new SnapshotDiffResult(
                true, string.Empty,
                added.Count, removed.Count, changed.Count,
                Math.Max(0, added.Count - limit),
                Math.Max(0, removed.Count - limit),
                Math.Max(0, changed.Count - limit),
                Math.Min(added.Count, limit),
                Math.Min(removed.Count, limit),
                Math.Min(changed.Count, limit),
                SliceStrings(added, limit),
                SliceStrings(removed, limit),
                SliceChanges(changed, limit));
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left is IEquatable<object> equatable)
            {
                return equatable.Equals(right);
            }

            return left.Equals(right);
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return "<null>";
            }

            if (value is string text)
            {
                return text;
            }

            if (value is bool boolean)
            {
                return boolean ? "true" : "false";
            }

            if (value is float single)
            {
                return single.ToString("R", CultureInfo.InvariantCulture);
            }

            if (value is double number)
            {
                return number.ToString("R", CultureInfo.InvariantCulture);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static IReadOnlyList<string> SliceStrings(List<string> values, int limit)
        {
            if (values.Count <= limit)
            {
                return values.AsReadOnly();
            }

            return values.GetRange(0, limit).AsReadOnly();
        }

        private static IReadOnlyList<SnapshotDiffChange> SliceChanges(
            List<SnapshotDiffChange> values, int limit)
        {
            if (values.Count <= limit)
            {
                return values.AsReadOnly();
            }

            return values.GetRange(0, limit).AsReadOnly();
        }
    }
}
