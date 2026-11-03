using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of canonical Socket_* transforms discovered
    /// on living units. Supports clean-room verification of socket authoring rules
    /// without visual captures.
    /// </summary>
    public sealed class SocketAuditRow
    {
        public string EntityKeyId { get; }
        public int SocketCount { get; }
        public IReadOnlyList<string> SocketNames { get; }
        public int CanonicalSocketCount { get; }
        public int AmbiguousSocketCount { get; }
        public bool HasMuzzlePrimary { get; }
        public bool HasDamageSmoke { get; }
        public bool HasFootContact { get; }
        public bool HasEngineAudio { get; }

        public SocketAuditRow(
            string entityKeyId,
            int socketCount,
            IReadOnlyList<string> socketNames,
            int canonicalSocketCount,
            int ambiguousSocketCount,
            bool hasMuzzlePrimary,
            bool hasDamageSmoke,
            bool hasFootContact,
            bool hasEngineAudio)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            SocketCount = socketCount < 0 ? 0 : socketCount;
            SocketNames = socketNames ?? new List<string>().AsReadOnly();
            CanonicalSocketCount = canonicalSocketCount < 0 ? 0 : canonicalSocketCount;
            AmbiguousSocketCount = ambiguousSocketCount < 0 ? 0 : ambiguousSocketCount;
            HasMuzzlePrimary = hasMuzzlePrimary;
            HasDamageSmoke = hasDamageSmoke;
            HasFootContact = hasFootContact;
            HasEngineAudio = hasEngineAudio;
        }
    }

    public sealed class SocketAuditCensus
    {
        public IReadOnlyList<SocketAuditRow> Rows { get; }
        public int OmittedRowCount { get; }

        public SocketAuditCensus(IReadOnlyList<SocketAuditRow> rows, int omittedRowCount)
        {
            Rows = rows ?? new List<SocketAuditRow>().AsReadOnly();
            OmittedRowCount = omittedRowCount < 0 ? 0 : omittedRowCount;
        }
    }

    public static class RuntimeSocketAuditCensus
    {
        private static readonly string[] CanonicalPrefixes = new[]
        {
            "Socket_MuzzlePrimary", "Socket_DamageSmoke", "Socket_DamageFire",
            "Socket_FootContact", "Socket_EngineAudio", "Socket_WeaponAudio",
            "Socket_RadarMast", "Socket_Antenna", "Socket_Hardpoint"
        };

        public static SocketAuditCensus Current(int maxRows = 64)
        {
            var rows = new List<SocketAuditRow>();
            int omitted = 0;
            int clampedMax = Math.Max(1, Math.Min(maxRows, 256));

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null) return new SocketAuditCensus(rows.AsReadOnly(), 0);

                var units = new List<EntityController>();
                try { if (existing.PlayerUnits() != null) units.AddRange(existing.PlayerUnits()); } catch { }
                try { if (existing.AiUnits() != null) units.AddRange(existing.AiUnits()); } catch { }

                foreach (EntityController unit in units)
                {
                    if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted) continue;
                    if (rows.Count >= clampedMax) { omitted++; continue; }
                    try { rows.Add(CollectRow(unit)); }
                    catch { omitted++; }
                }
            }
            catch { }

            return new SocketAuditCensus(rows.AsReadOnly(), omitted);
        }

        private static SocketAuditRow CollectRow(EntityController unit)
        {
            string key = ResolveKey(unit);
            GameObject go = null;
            try { go = unit.gameObject; } catch { go = null; }

            var socketNames = new List<string>();
            int canonical = 0;
            int ambiguous = 0;
            bool hasMuzzle = false, hasSmoke = false, hasFoot = false, hasEngine = false;

            if (go != null)
            {
                Transform[] transforms = null;
                try { transforms = go.GetComponentsInChildren<Transform>(true); } catch { transforms = null; }
                if (transforms != null)
                {
                    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (Transform t in transforms)
                    {
                        if (t == null) continue;
                        string name = t.name ?? string.Empty;
                        if (!name.StartsWith("Socket_", StringComparison.Ordinal)) continue;
                        socketNames.Add(name);
                        counts[name] = counts.TryGetValue(name, out int c) ? c + 1 : 1;
                        if (name.StartsWith("Socket_MuzzlePrimary", StringComparison.Ordinal)) hasMuzzle = true;
                        if (name.StartsWith("Socket_DamageSmoke", StringComparison.Ordinal)) hasSmoke = true;
                        if (name.StartsWith("Socket_FootContact", StringComparison.Ordinal)) hasFoot = true;
                        if (name.StartsWith("Socket_EngineAudio", StringComparison.Ordinal)) hasEngine = true;
                        bool isCanonical = false;
                        foreach (string prefix in CanonicalPrefixes)
                        {
                            if (name.StartsWith(prefix, StringComparison.Ordinal)) { isCanonical = true; break; }
                        }
                        if (isCanonical) canonical++;
                    }
                    foreach (var kv in counts)
                    {
                        if (kv.Value > 1) ambiguous += kv.Value - 1;
                    }
                }
            }

            return new SocketAuditRow(
                key,
                socketNames.Count,
                socketNames.Distinct().ToList().AsReadOnly(),
                canonical,
                ambiguous,
                hasMuzzle,
                hasSmoke,
                hasFoot,
                hasEngine);
        }

        private static string ResolveKey(EntityController unit)
        {
            string key = string.Empty;
            try
            {
                PropertyInfo prop = unit.GetType().GetProperty("EntityId") ?? unit.GetType().GetProperty("entityId");
                if (prop != null) key = prop.GetValue(unit, null) as string ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    FieldInfo field = unit.GetType().GetField("entityId") ?? unit.GetType().GetField("EntityId");
                    if (field != null) key = field.GetValue(unit) as string ?? string.Empty;
                }
            }
            catch { }
            if (string.IsNullOrWhiteSpace(key)) key = unit != null ? unit.name : string.Empty;
            return key;
        }
    }
}