using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Runtime manager for battlefield environmental hazard zones (firestorms, acid pools,
    /// EMP fields, cryo mists, and nanite repair fields).
    /// Observes the project rule: trigger colliders (isTrigger = true) with amortized
    /// tick rates (>= 0.5s) to preserve flocking pathfinding and frame rate.
    /// </summary>
    public static class RuntimeHazardManager
    {
        private static readonly object _zonesLock = new object();
        private static readonly List<ActiveHazardZone> _activeZones = new List<ActiveHazardZone>();
        private static int _zoneIdSequence = 1;

        public const float MinimumTickInterval = 0.5f;
        public const float MinimumRadius = 2.0f;
        public const float MaximumRadius = 50.0f;
        public const float MinimumDuration = 1.0f;
        public const float MaximumDuration = 120.0f;

        public sealed class ActiveHazardZone
        {
            public ActiveHazardZone(
                string zoneId,
                string hazardType,
                float centerX,
                float centerZ,
                float radius,
                float durationSeconds,
                float expireTime,
                float tickDamage,
                float tickInterval,
                GameObject triggerObject)
            {
                ZoneId = zoneId ?? string.Empty;
                HazardType = hazardType ?? "Firestorm";
                CenterX = centerX;
                CenterZ = centerZ;
                Radius = radius;
                DurationSeconds = durationSeconds;
                ExpireTime = expireTime;
                TickDamage = tickDamage;
                TickInterval = tickInterval;
                NextTickTime = 0f;
                AffectedEntitiesLastTick = 0;
                TriggerObject = triggerObject;
            }

            public string ZoneId { get; }
            public string HazardType { get; }
            public float CenterX { get; }
            public float CenterZ { get; }
            public float Radius { get; }
            public float DurationSeconds { get; }
            public float ExpireTime { get; set; }
            public float TickDamage { get; }
            public float TickInterval { get; }
            public float NextTickTime { get; set; }
            public int AffectedEntitiesLastTick { get; set; }
            public GameObject TriggerObject { get; set; }
        }

        public static int ActiveZoneCount
        {
            get
            {
                lock (_zonesLock)
                {
                    return _activeZones.Count;
                }
            }
        }

        public static bool TrySpawnHazardZone(
            string hazardType,
            float x,
            float z,
            float radius,
            float durationSeconds,
            float tickDamage,
            out string zoneId)
        {
            string sanitizedType = string.IsNullOrWhiteSpace(hazardType) ? "Firestorm" : hazardType.Trim();
            if (float.IsNaN(x) || float.IsInfinity(x) ||
                float.IsNaN(z) || float.IsInfinity(z) ||
                float.IsNaN(radius) || float.IsInfinity(radius) ||
                float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) ||
                float.IsNaN(tickDamage) || float.IsInfinity(tickDamage) ||
                radius <= 0f || durationSeconds <= 0f || tickDamage < 0f)
            {
                zoneId = string.Empty;
                return false;
            }

            float clampedRadius = Mathf.Clamp(radius, MinimumRadius, MaximumRadius);
            float clampedDuration = Mathf.Clamp(durationSeconds, MinimumDuration, MaximumDuration);
            float clampedDamage = Mathf.Max(0f, tickDamage);
            float tickInterval = MinimumTickInterval; // Strictly >= 0.5s per AGENTS.md

            float now = 0f;
            try
            {
                now = Time.time;
            }
            catch
            {
                // Fallback for offline / headless tests
            }

            GameObject triggerGo = null;
            try
            {
                triggerGo = new GameObject("RC_HazardZone_" + sanitizedType);
                triggerGo.transform.position = new Vector3(x, 0f, z);
                SphereCollider collider = triggerGo.AddComponent<SphereCollider>();
                if (collider != null)
                {
                    collider.isTrigger = true;
                    collider.radius = clampedRadius;
                }
            }
            catch
            {
                // Trigger GameObject creation is best-effort (e.g. in test runner stubs)
            }

            lock (_zonesLock)
            {
                zoneId = "hazard_" + (_zoneIdSequence++);
                var zone = new ActiveHazardZone(
                    zoneId,
                    sanitizedType,
                    x,
                    z,
                    clampedRadius,
                    clampedDuration,
                    now + clampedDuration,
                    clampedDamage,
                    tickInterval,
                    triggerGo);

                _activeZones.Add(zone);
            }

            return true;
        }

        public static void UpdateTick(float now)
        {
            if (float.IsNaN(now) || float.IsInfinity(now))
            {
                return;
            }

            List<ActiveHazardZone> currentZones;
            lock (_zonesLock)
            {
                if (_activeZones.Count == 0)
                {
                    return;
                }

                // Remove expired zones
                for (int i = _activeZones.Count - 1; i >= 0; i--)
                {
                    ActiveHazardZone zone = _activeZones[i];
                    if (now >= zone.ExpireTime)
                    {
                        if (zone.TriggerObject != null)
                        {
                            try
                            {
                                UnityEngine.Object.Destroy(zone.TriggerObject);
                            }
                            catch
                            {
                            }
                        }
                        _activeZones.RemoveAt(i);
                    }
                }

                currentZones = new List<ActiveHazardZone>(_activeZones);
            }

            if (currentZones.Count == 0)
            {
                return;
            }

            ExistingControllers existing = null;
            try
            {
                existing = ExistingControllers.Instance;
            }
            catch
            {
            }

            if (existing == null)
            {
                return;
            }

            var candidates = new List<EntityController>();
            try
            {
                HashSet<EntityController> playerUnits = existing.PlayerUnits();
                if (playerUnits != null)
                {
                    candidates.AddRange(playerUnits);
                }
                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits != null)
                {
                    candidates.AddRange(aiUnits);
                }
            }
            catch
            {
            }

            foreach (ActiveHazardZone zone in currentZones)
            {
                if (now < zone.NextTickTime)
                {
                    continue;
                }

                zone.NextTickTime = now + zone.TickInterval;
                float radiusSq = zone.Radius * zone.Radius;
                int affectedCount = 0;

                foreach (EntityController entity in candidates)
                {
                    if (entity == null || entity.IsDestroyed || entity.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    Vector3 pos;
                    try
                    {
                        pos = entity.Position;
                    }
                    catch
                    {
                        continue;
                    }

                    float dx = pos.x - zone.CenterX;
                    float dz = pos.z - zone.CenterZ;
                    if ((dx * dx + dz * dz) > radiusSq)
                    {
                        continue;
                    }

                    // Apply hazard effect
                    ApplyHazardEffect(entity, zone);
                    affectedCount++;
                }

                zone.AffectedEntitiesLastTick = affectedCount;
            }
        }

        private static void ApplyHazardEffect(EntityController entity, ActiveHazardZone zone)
        {
            try
            {
                string type = zone.HazardType;
                if (string.Equals(type, "NaniteField", StringComparison.OrdinalIgnoreCase))
                {
                    // Beneficial repair/heal field: applies to friendly player entities
                    if (!entity.IsControlledByAi)
                    {
                        if (zone.TickDamage > 0f)
                        {
                            entity.Heal(zone.TickDamage, null);
                            entity.RepairArmor(zone.TickDamage * 0.5f, null, false);
                        }
                        entity.SetActiveStatusEffect(StatusEffect.Regen, SetStatusEffect.DurationType.Seconds, 1.0f);
                    }
                    return;
                }

                // Offensive hazards: affect hostile/AI entities only to protect friendly flocking
                if (entity.IsControlledByPlayer && !entity.IsControlledByAi)
                {
                    return;
                }
                else if (string.Equals(type, "CryoMist", StringComparison.OrdinalIgnoreCase))
                {
                    entity.SetActiveStatusEffect(StatusEffect.SlowedDown, SetStatusEffect.DurationType.Seconds, 1.0f);
                    if (zone.TickDamage > 0f)
                    {
                        entity.TakeDamage(zone.TickDamage * 0.5f, null);
                    }
                }
                else if (string.Equals(type, "EmpField", StringComparison.OrdinalIgnoreCase))
                {
                    entity.SetActiveStatusEffect(StatusEffect.Stun, SetStatusEffect.DurationType.Seconds, 1.0f);
                    if (zone.TickDamage > 0f)
                    {
                        entity.TakeDamage(zone.TickDamage * 1.5f, null);
                    }
                }
                else if (string.Equals(type, "AcidPool", StringComparison.OrdinalIgnoreCase))
                {
                    entity.SetActiveStatusEffect(StatusEffect.Marked, SetStatusEffect.DurationType.Seconds, 1.0f);
                    if (zone.TickDamage > 0f)
                    {
                        entity.TakeDamage(zone.TickDamage, null, false, true); // True ignores armor or erodes armor
                    }
                }
                else
                {
                    // Default: Firestorm
                    entity.SetActiveStatusEffect(StatusEffect.OnFire, SetStatusEffect.DurationType.Seconds, 1.5f);
                    if (zone.TickDamage > 0f)
                    {
                        entity.TakeDamage(zone.TickDamage, null);
                    }
                }
            }
            catch
            {
                // Fail-closed
            }
        }

        public static int ClearAll()
        {
            int clearedCount = 0;
            lock (_zonesLock)
            {
                clearedCount = _activeZones.Count;
                foreach (ActiveHazardZone zone in _activeZones)
                {
                    if (zone.TriggerObject != null)
                    {
                        try
                        {
                            UnityEngine.Object.Destroy(zone.TriggerObject);
                        }
                        catch
                        {
                        }
                    }
                }
                _activeZones.Clear();
            }
            return clearedCount;
        }

        public static IReadOnlyList<HazardZoneDiagnosticRow> GetActiveHazardRows(int maxRows, out int omittedCount)
        {
            float now = 0f;
            try
            {
                now = Time.time;
            }
            catch
            {
            }

            var rows = new List<HazardZoneDiagnosticRow>();
            lock (_zonesLock)
            {
                int total = _activeZones.Count;
                int countToTake = Math.Min(total, maxRows);
                omittedCount = Math.Max(0, total - countToTake);

                for (int i = 0; i < countToTake; i++)
                {
                    ActiveHazardZone zone = _activeZones[i];
                    float remaining = Mathf.Max(0f, zone.ExpireTime - now);
                    rows.Add(new HazardZoneDiagnosticRow(
                        zone.ZoneId,
                        zone.HazardType,
                        zone.CenterX,
                        zone.CenterZ,
                        zone.Radius,
                        zone.TickDamage,
                        zone.TickInterval,
                        remaining,
                        zone.AffectedEntitiesLastTick));
                }
            }

            return rows.AsReadOnly();
        }
    }
}
