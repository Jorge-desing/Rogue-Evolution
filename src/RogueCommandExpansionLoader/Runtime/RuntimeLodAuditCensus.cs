using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    public sealed class LodAuditRow
    {
        public string EntityKeyId { get; }
        public int LodGroupCount { get; }
        public int LodLevelCount { get; }
        public bool HasLodGroup { get; }
        public IReadOnlyList<string> RendererNames { get; }

        public LodAuditRow(
            string entityKeyId,
            int lodGroupCount,
            int lodLevelCount,
            bool hasLodGroup,
            IReadOnlyList<string> rendererNames)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            LodGroupCount = lodGroupCount < 0 ? 0 : lodGroupCount;
            LodLevelCount = lodLevelCount < 0 ? 0 : lodLevelCount;
            HasLodGroup = hasLodGroup;
            RendererNames = rendererNames ?? new List<string>().AsReadOnly();
        }
    }

    public sealed class LodAuditCensus
    {
        public IReadOnlyList<LodAuditRow> Rows { get; }
        public int OmittedRowCount { get; }

        public LodAuditCensus(IReadOnlyList<LodAuditRow> rows, int omittedRowCount)
        {
            Rows = rows ?? new List<LodAuditRow>().AsReadOnly();
            OmittedRowCount = omittedRowCount < 0 ? 0 : omittedRowCount;
        }
    }

    public static class RuntimeLodAuditCensus
    {
        public static LodAuditCensus Current(int maxRows = 64)
        {
            var rows = new List<LodAuditRow>();
            int omitted = 0;
            int clampedMax = Math.Max(1, Math.Min(maxRows, 256));

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null) return new LodAuditCensus(rows.AsReadOnly(), 0);

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

            return new LodAuditCensus(rows.AsReadOnly(), omitted);
        }

        private static LodAuditRow CollectRow(EntityController unit)
        {
            string key = ResolveKey(unit);
            GameObject go = null;
            try { go = unit.gameObject; } catch { go = null; }

            int groupCount = 0;
            int levelCount = 0;
            bool hasGroup = false;
            var rendererNames = new List<string>();

            if (go != null)
            {
                LODGroup[] groups = null;
                try { groups = go.GetComponentsInChildren<LODGroup>(true); } catch { groups = null; }
                if (groups != null)
                {
                    groupCount = groups.Length;
                    hasGroup = groups.Length > 0;
                    foreach (LODGroup g in groups)
                    {
                        if (g == null) continue;
                        try { levelCount += g.lodCount; } catch { levelCount += 1; }
                    }
                }

                Renderer[] renderers = null;
                try { renderers = go.GetComponentsInChildren<Renderer>(true); } catch { renderers = null; }
                if (renderers != null)
                {
                    foreach (Renderer r in renderers)
                    {
                        if (r == null) continue;
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(r.name) && !rendererNames.Contains(r.name))
                                rendererNames.Add(r.name);
                        }
                        catch { }
                    }
                }
            }

            if (levelCount == 0 && groupCount == 0) levelCount = 1;

            return new LodAuditRow(
                key,
                groupCount,
                levelCount,
                hasGroup,
                rendererNames.AsReadOnly());
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