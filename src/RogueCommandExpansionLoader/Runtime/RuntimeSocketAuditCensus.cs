using System;
using System.Collections.Generic;
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
        public bool HierarchyTruncated { get; }
        public bool SocketNameListBounded { get; }

        public SocketAuditRow(
            string entityKeyId,
            int socketCount,
            IReadOnlyList<string> socketNames,
            int canonicalSocketCount,
            int ambiguousSocketCount,
            bool hasMuzzlePrimary,
            bool hasDamageSmoke,
            bool hasFootContact,
            bool hasEngineAudio,
            bool hierarchyTruncated = false,
            bool socketNameListBounded = false)
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
            HierarchyTruncated = hierarchyTruncated;
            SocketNameListBounded = socketNameListBounded;
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

            try
            {
                List<EntityController> units = RuntimeVisualCensusSupport.CollectLivingUnits(maxRows, out omitted);
                for (int i = 0; i < units.Count; i++)
                {
                    try { rows.Add(CollectRow(units[i])); }
                    catch { omitted++; }
                }
            }
            catch
            {
                omitted++;
            }

            return new SocketAuditCensus(rows.AsReadOnly(), omitted);
        }

        private static SocketAuditRow CollectRow(EntityController unit)
        {
            string key = RuntimeVisualCensusSupport.ResolveEntityKey(unit);
            GameObject go = null;
            try { go = unit.gameObject; } catch { go = null; }

            var socketNames = new List<string>();
            int socketCount = 0;
            int canonical = 0;
            int ambiguous = 0;
            bool hasMuzzle = false, hasSmoke = false, hasFoot = false, hasEngine = false;
            bool hierarchyTruncated = false;
            bool socketNameListBounded = false;

            if (go != null)
            {
                VisualHierarchySnapshot snapshot = RuntimeVisualCensusSupport.CaptureHierarchy(go);
                hierarchyTruncated = snapshot.HierarchyTruncated;
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Transform transform in snapshot.Transforms)
                {
                    if (transform == null) continue;
                    string name = transform.name ?? string.Empty;
                    if (!name.StartsWith("Socket_", StringComparison.Ordinal)) continue;

                    socketCount++;
                    RuntimeVisualCensusSupport.AddUniqueBounded(
                        socketNames,
                        seenNames,
                        name,
                        RuntimeVisualCensusSupport.MaxDiagnosticNamesPerUnit,
                        ref socketNameListBounded);
                    counts[name] = counts.TryGetValue(name, out int count) ? count + 1 : 1;
                    if (name.StartsWith("Socket_MuzzlePrimary", StringComparison.Ordinal)) hasMuzzle = true;
                    if (name.StartsWith("Socket_DamageSmoke", StringComparison.Ordinal)) hasSmoke = true;
                    if (name.StartsWith("Socket_FootContact", StringComparison.Ordinal)) hasFoot = true;
                    if (name.StartsWith("Socket_EngineAudio", StringComparison.Ordinal)) hasEngine = true;
                    for (int prefixIndex = 0; prefixIndex < CanonicalPrefixes.Length; prefixIndex++)
                    {
                        if (name.StartsWith(CanonicalPrefixes[prefixIndex], StringComparison.Ordinal))
                        {
                            canonical++;
                            break;
                        }
                    }
                }

                foreach (KeyValuePair<string, int> pair in counts)
                {
                    if (pair.Value > 1) ambiguous += pair.Value - 1;
                }
                if (snapshot.RendererListTruncated || snapshot.LodListTruncated)
                {
                    socketNameListBounded = true;
                }
            }

            return new SocketAuditRow(
                key,
                socketCount,
                socketNames.AsReadOnly(),
                canonical,
                ambiguous,
                hasMuzzle,
                hasSmoke,
                hasFoot,
                hasEngine,
                hierarchyTruncated,
                socketNameListBounded);
        }

    }
}