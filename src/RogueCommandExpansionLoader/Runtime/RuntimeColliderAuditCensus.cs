using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    public sealed class ColliderAuditRow
    {
        public string EntityKeyId { get; }
        public int ColliderCount { get; }
        public int TriggerColliderCount { get; }
        public int SolidColliderCount { get; }
        public IReadOnlyList<string> ColliderTypes { get; }
        public bool HasRigidbody { get; }
        public bool RigidbodyIsKinematic { get; }

        public ColliderAuditRow(
            string entityKeyId,
            int colliderCount,
            int triggerColliderCount,
            int solidColliderCount,
            IReadOnlyList<string> colliderTypes,
            bool hasRigidbody,
            bool rigidbodyIsKinematic)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            ColliderCount = colliderCount < 0 ? 0 : colliderCount;
            TriggerColliderCount = triggerColliderCount < 0 ? 0 : triggerColliderCount;
            SolidColliderCount = solidColliderCount < 0 ? 0 : solidColliderCount;
            ColliderTypes = colliderTypes ?? new List<string>().AsReadOnly();
            HasRigidbody = hasRigidbody;
            RigidbodyIsKinematic = rigidbodyIsKinematic;
        }
    }

    public sealed class ColliderAuditCensus
    {
        public IReadOnlyList<ColliderAuditRow> Rows { get; }
        public int OmittedRowCount { get; }

        public ColliderAuditCensus(IReadOnlyList<ColliderAuditRow> rows, int omittedRowCount)
        {
            Rows = rows ?? new List<ColliderAuditRow>().AsReadOnly();
            OmittedRowCount = omittedRowCount < 0 ? 0 : omittedRowCount;
        }
    }

    public static class RuntimeColliderAuditCensus
    {
        public static ColliderAuditCensus Current(int maxRows = 64)
        {
            var rows = new List<ColliderAuditRow>();
            int omitted = 0;
            int clampedMax = Math.Max(1, Math.Min(maxRows, 256));

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null) return new ColliderAuditCensus(rows.AsReadOnly(), 0);

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

            return new ColliderAuditCensus(rows.AsReadOnly(), omitted);
        }

        private static ColliderAuditRow CollectRow(EntityController unit)
        {
            string key = ResolveKey(unit);
            GameObject go = null;
            try { go = unit.gameObject; } catch { go = null; }

            int colliderCount = 0;
            int triggerCount = 0;
            int solidCount = 0;
            var types = new List<string>();
            bool hasRb = false;
            bool rbKinematic = false;

            if (go != null)
            {
                Collider[] cols = null;
                try { cols = go.GetComponentsInChildren<Collider>(true); } catch { cols = null; }
                if (cols != null)
                {
                    foreach (Collider c in cols)
                    {
                        if (c == null) continue;
                        colliderCount++;
                        try { if (c.isTrigger) triggerCount++; else solidCount++; } catch { solidCount++; }
                        try
                        {
                            string t = c.GetType().Name;
                            if (!types.Contains(t)) types.Add(t);
                        }
                        catch { }
                    }
                }

                Rigidbody rb = null;
                try { rb = go.GetComponent<Rigidbody>(); } catch { rb = null; }
                if (rb == null)
                {
                    try { rb = go.GetComponentInChildren<Rigidbody>(); } catch { rb = null; }
                }
                if (rb != null)
                {
                    hasRb = true;
                    try { rbKinematic = rb.isKinematic; } catch { rbKinematic = false; }
                }
            }

            return new ColliderAuditRow(
                key,
                colliderCount,
                triggerCount,
                solidCount,
                types.AsReadOnly(),
                hasRb,
                rbKinematic);
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