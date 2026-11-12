using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    public sealed class ParticleVfxRow
    {
        public string EntityKeyId { get; }
        public int ParticleSystemCount { get; }
        public int ActiveParticleSystemCount { get; }
        public int TrailRendererCount { get; }
        public int LineRendererCount { get; }
        public IReadOnlyList<string> ParticleSystemNames { get; }

        public ParticleVfxRow(
            string entityKeyId,
            int particleSystemCount,
            int activeParticleSystemCount,
            int trailRendererCount,
            int lineRendererCount,
            IReadOnlyList<string> particleSystemNames)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            ParticleSystemCount = particleSystemCount < 0 ? 0 : particleSystemCount;
            ActiveParticleSystemCount = activeParticleSystemCount < 0 ? 0 : activeParticleSystemCount;
            TrailRendererCount = trailRendererCount < 0 ? 0 : trailRendererCount;
            LineRendererCount = lineRendererCount < 0 ? 0 : lineRendererCount;
            ParticleSystemNames = particleSystemNames ?? new List<string>().AsReadOnly();
        }
    }

    public sealed class ParticleVfxCensus
    {
        public IReadOnlyList<ParticleVfxRow> Rows { get; }
        public int OmittedRowCount { get; }

        public ParticleVfxCensus(IReadOnlyList<ParticleVfxRow> rows, int omittedRowCount)
        {
            Rows = rows ?? new List<ParticleVfxRow>().AsReadOnly();
            OmittedRowCount = omittedRowCount < 0 ? 0 : omittedRowCount;
        }
    }

    public static class RuntimeParticleVfxCensus
    {
        public static ParticleVfxCensus Current(int maxRows = 64)
        {
            var rows = new List<ParticleVfxRow>();
            int omitted = 0;
            int clampedMax = Math.Max(1, Math.Min(maxRows, 256));

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null) return new ParticleVfxCensus(rows.AsReadOnly(), 0);

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

            return new ParticleVfxCensus(rows.AsReadOnly(), omitted);
        }

        private static ParticleVfxRow CollectRow(EntityController unit)
        {
            string key = ResolveKey(unit);
            GameObject go = null;
            try { go = unit.gameObject; } catch { go = null; }

            int psCount = 0;
            int psActive = 0;
            int trailCount = 0;
            int lineCount = 0;
            var psNames = new List<string>();

            if (go != null)
            {
                ParticleSystem[] pss = null;
                try { pss = go.GetComponentsInChildren<ParticleSystem>(true); } catch { pss = null; }
                if (pss != null)
                {
                    foreach (ParticleSystem ps in pss)
                    {
                        if (ps == null) continue;
                        psCount++;
                        try { if (ps.isPlaying) psActive++; } catch { }
                        try { if (!string.IsNullOrWhiteSpace(ps.name) && !psNames.Contains(ps.name)) psNames.Add(ps.name); } catch { }
                    }
                }

                TrailRenderer[] trails = null;
                try { trails = go.GetComponentsInChildren<TrailRenderer>(true); } catch { trails = null; }
                if (trails != null) trailCount = trails.Length;

                LineRenderer[] lines = null;
                try { lines = go.GetComponentsInChildren<LineRenderer>(true); } catch { lines = null; }
                if (lines != null) lineCount = lines.Length;
            }

            return new ParticleVfxRow(
                key,
                psCount,
                psActive,
                trailCount,
                lineCount,
                psNames.AsReadOnly());
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