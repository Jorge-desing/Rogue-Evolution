using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime.Ai
{
    /// <summary>
    /// Pure 8-ray spatial vector steering policy that computes safe retreat and evacuation destinations
    /// while avoiding active <see cref="AiHazardZoneSnapshot"/> circles and hostile threat concentrations.
    /// Free of Unity and Assembly-CSharp types for deterministic offline testing.
    /// </summary>
    public static class AiSafeVectorPolicy
    {
        private const float HazardSafetyMargin = 2.5f;

        private static readonly (float Cos, float Sin)[] EightDirections = new[]
        {
            (1.0f, 0.0f),
            (0.70710678f, 0.70710678f),
            (0.0f, 1.0f),
            (-0.70710678f, 0.70710678f),
            (-1.0f, 0.0f),
            (-0.70710678f, -0.70710678f),
            (0.0f, -1.0f),
            (0.70710678f, -0.70710678f)
        };

        /// <summary>
        /// Evaluates 8 candidate directions around <paramref name="unit"/> at <paramref name="desiredDistance"/>
        /// and returns the destination with the highest spatial safety score (maximizing separation from
        /// <paramref name="primaryThreat"/>, hostile centroid, and harmful hazard zones).
        /// </summary>
        public static (float DestX, float DestZ) ComputeSafeRetreatDestination(
            AiUnitSnapshot unit,
            AiUnitSnapshot primaryThreat,
            AiBattlefieldSnapshot snapshot,
            float desiredDistance = 10.0f)
        {
            float dist = float.IsNaN(desiredDistance) || float.IsInfinity(desiredDistance)
                ? 10.0f
                : Math.Max(4.0f, desiredDistance);

            // Preferred baseline vector: directly away from primaryThreat (or away from hostile centroid)
            float awayX = unit.PosX - primaryThreat.PosX;
            float awayZ = unit.PosZ - primaryThreat.PosZ;
            float awayLen = (float)Math.Sqrt(awayX * awayX + awayZ * awayZ);
            if (awayLen < 0.01f && snapshot != null)
            {
                awayX = unit.PosX - snapshot.HostileCentroidX;
                awayZ = unit.PosZ - snapshot.HostileCentroidZ;
                awayLen = (float)Math.Sqrt(awayX * awayX + awayZ * awayZ);
            }

            if (awayLen < 0.01f)
            {
                awayX = -1.0f;
                awayZ = 0.0f;
            }
            else
            {
                awayX /= awayLen;
                awayZ /= awayLen;
            }

            IReadOnlyList<AiHazardZoneSnapshot> hazards = snapshot != null ? snapshot.HazardZones : null;
            IReadOnlyList<AiUnitSnapshot> hostiles = snapshot != null ? snapshot.HostileUnits : null;

            float bestScore = float.MinValue;
            float bestX = unit.PosX + awayX * dist;
            float bestZ = unit.PosZ + awayZ * dist;

            for (int i = 0; i < EightDirections.Length; i++)
            {
                float dirX = EightDirections[i].Cos;
                float dirZ = EightDirections[i].Sin;
                float candX = unit.PosX + dirX * dist;
                float candZ = unit.PosZ + dirZ * dist;

                // Alignment with preferred away-from-threat direction: [-1, +1] -> [-30, +30]
                float alignment = (dirX * awayX) + (dirZ * awayZ);
                float score = alignment * 30.0f;

                // Distance from primary threat
                float dxThreat = candX - primaryThreat.PosX;
                float dzThreat = candZ - primaryThreat.PosZ;
                float distThreat = (float)Math.Sqrt(dxThreat * dxThreat + dzThreat * dzThreat);
                score += distThreat * 1.5f;

                // Penalize proximity to harmful hazard zones along the midpoint and endpoint
                if (hazards != null)
                {
                    float midX = unit.PosX + dirX * (dist * 0.5f);
                    float midZ = unit.PosZ + dirZ * (dist * 0.5f);

                    for (int h = 0; h < hazards.Count; h++)
                    {
                        AiHazardZoneSnapshot hz = hazards[h];
                        if (hz.IsBeneficialToAi)
                        {
                            continue;
                        }

                        if (hz.ContainsPoint(candX, candZ, HazardSafetyMargin))
                        {
                            float penetration = (hz.Radius + HazardSafetyMargin) -
                                (float)Math.Sqrt((candX - hz.CenterX) * (candX - hz.CenterX) + (candZ - hz.CenterZ) * (candZ - hz.CenterZ));
                            score -= 500.0f + Math.Max(0f, penetration) * 40.0f;
                        }
                        else
                        {
                            float clearance = hz.DistanceToPerimeter(candX, candZ);
                            score += Math.Min(12.0f, clearance);
                        }

                        if (hz.ContainsPoint(midX, midZ, 1.0f))
                        {
                            score -= 220.0f;
                        }
                    }
                }

                // Penalize moving into secondary hostiles
                if (hostiles != null)
                {
                    for (int e = 0; e < hostiles.Count; e++)
                    {
                        AiUnitSnapshot hostile = hostiles[e];
                        float edx = candX - hostile.PosX;
                        float edz = candZ - hostile.PosZ;
                        float edistSq = edx * edx + edz * edz;
                        if (edistSq < 36.0f)
                        {
                            score -= (36.0f - edistSq) * 1.5f;
                        }
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = candX;
                    bestZ = candZ;
                }
            }

            return (bestX, bestZ);
        }

        /// <summary>
        /// Evaluates 8 candidate directions around <paramref name="unit"/> at <paramref name="desiredDistance"/>
        /// and returns the destination that maximizes tactical survival: retreating away from <paramref name="primaryThreat"/>
        /// and hazard zones while applying an attractive bias toward friendly rally structures (HQ/base buildings),
        /// field repairers (support units), or the friendly combat cluster centroid.
        /// </summary>
        public static (float DestX, float DestZ) ComputeTacticalRetreatDestination(
            AiUnitSnapshot unit,
            AiUnitSnapshot primaryThreat,
            AiBattlefieldSnapshot snapshot,
            float desiredDistance = 12.0f)
        {
            float dist = float.IsNaN(desiredDistance) || float.IsInfinity(desiredDistance)
                ? 12.0f
                : Math.Max(4.0f, desiredDistance);

            // 1. Preferred baseline vector: directly away from primaryThreat (or away from hostile centroid)
            float awayX = unit.PosX - primaryThreat.PosX;
            float awayZ = unit.PosZ - primaryThreat.PosZ;
            float awayLen = (float)Math.Sqrt(awayX * awayX + awayZ * awayZ);
            if (awayLen < 0.01f && snapshot != null)
            {
                awayX = unit.PosX - snapshot.HostileCentroidX;
                awayZ = unit.PosZ - snapshot.HostileCentroidZ;
                awayLen = (float)Math.Sqrt(awayX * awayX + awayZ * awayZ);
            }

            if (awayLen < 0.01f)
            {
                awayX = -1.0f;
                awayZ = 0.0f;
            }
            else
            {
                awayX /= awayLen;
                awayZ /= awayLen;
            }

            // 2. Resolve friendly rally anchor (closest support unit, defensive building, or friendly combat centroid)
            float rallyX = snapshot != null ? snapshot.AiCentroidX : unit.PosX;
            float rallyZ = snapshot != null ? snapshot.AiCentroidZ : unit.PosZ;
            bool hasRallyPoint = false;
            float bestRallyDistSq = float.MaxValue;

            if (snapshot != null && snapshot.AiUnits != null)
            {
                for (int i = 0; i < snapshot.AiUnits.Count; i++)
                {
                    AiUnitSnapshot ally = snapshot.AiUnits[i];
                    if (ally.UniqueEntityId == unit.UniqueEntityId)
                    {
                        continue;
                    }

                    // Prioritize support units or defensive buildings as tactical rally anchors
                    if (ally.IsSupportUnit || ally.IsBuilding)
                    {
                        float adx = ally.PosX - unit.PosX;
                        float adz = ally.PosZ - unit.PosZ;
                        float adistSq = adx * adx + adz * adz;
                        if (adistSq < bestRallyDistSq)
                        {
                            bestRallyDistSq = adistSq;
                            rallyX = ally.PosX;
                            rallyZ = ally.PosZ;
                            hasRallyPoint = true;
                        }
                    }
                }
            }

            if (!hasRallyPoint && snapshot != null && snapshot.TotalAiCombatUnits > 1)
            {
                rallyX = snapshot.AiCentroidX;
                rallyZ = snapshot.AiCentroidZ;
                hasRallyPoint = true;
            }

            float rallyDirX = 0f;
            float rallyDirZ = 0f;
            float rallyDist = 0f;
            if (hasRallyPoint)
            {
                float toRallyX = rallyX - unit.PosX;
                float toRallyZ = rallyZ - unit.PosZ;
                rallyDist = (float)Math.Sqrt(toRallyX * toRallyX + toRallyZ * toRallyZ);
                if (rallyDist > 0.01f)
                {
                    rallyDirX = toRallyX / rallyDist;
                    rallyDirZ = toRallyZ / rallyDist;
                }
            }

            IReadOnlyList<AiHazardZoneSnapshot> hazards = snapshot != null ? snapshot.HazardZones : null;
            IReadOnlyList<AiUnitSnapshot> hostiles = snapshot != null ? snapshot.HostileUnits : null;

            float bestScore = float.MinValue;
            float bestX = unit.PosX + awayX * dist;
            float bestZ = unit.PosZ + awayZ * dist;

            for (int i = 0; i < EightDirections.Length; i++)
            {
                float dirX = EightDirections[i].Cos;
                float dirZ = EightDirections[i].Sin;
                float candX = unit.PosX + dirX * dist;
                float candZ = unit.PosZ + dirZ * dist;

                // Alignment with away-from-threat direction
                float awayAlignment = (dirX * awayX) + (dirZ * awayZ);
                float score = awayAlignment * 25.0f;

                // Distance from primary threat
                float dxThreat = candX - primaryThreat.PosX;
                float dzThreat = candZ - primaryThreat.PosZ;
                float distThreat = (float)Math.Sqrt(dxThreat * dxThreat + dzThreat * dzThreat);
                score += distThreat * 1.5f;

                // Alignment with rally anchor direction
                if (rallyDist > 0.01f)
                {
                    float rallyAlignment = (dirX * rallyDirX) + (dirZ * rallyDirZ);
                    score += rallyAlignment * 20.0f;

                    float candRallyDx = candX - rallyX;
                    float candRallyDz = candZ - rallyZ;
                    float candRallyDist = (float)Math.Sqrt(candRallyDx * candRallyDx + candRallyDz * candRallyDz);
                    float distReduction = rallyDist - candRallyDist;
                    score += distReduction * 0.8f;
                }

                // Penalize proximity to harmful hazard zones along midpoint and endpoint
                if (hazards != null)
                {
                    float midX = unit.PosX + dirX * (dist * 0.5f);
                    float midZ = unit.PosZ + dirZ * (dist * 0.5f);

                    for (int h = 0; h < hazards.Count; h++)
                    {
                        AiHazardZoneSnapshot hz = hazards[h];
                        if (hz.IsBeneficialToAi)
                        {
                            continue;
                        }

                        if (hz.ContainsPoint(candX, candZ, HazardSafetyMargin))
                        {
                            float penetration = (hz.Radius + HazardSafetyMargin) -
                                (float)Math.Sqrt((candX - hz.CenterX) * (candX - hz.CenterX) + (candZ - hz.CenterZ) * (candZ - hz.CenterZ));
                            score -= 500.0f + Math.Max(0f, penetration) * 40.0f;
                        }
                        else
                        {
                            float clearance = hz.DistanceToPerimeter(candX, candZ);
                            score += Math.Min(12.0f, clearance);
                        }

                        if (hz.ContainsPoint(midX, midZ, 1.0f))
                        {
                            score -= 220.0f;
                        }
                    }
                }

                // Penalize moving into secondary hostiles
                if (hostiles != null)
                {
                    for (int e = 0; e < hostiles.Count; e++)
                    {
                        AiUnitSnapshot hostile = hostiles[e];
                        float edx = candX - hostile.PosX;
                        float edz = candZ - hostile.PosZ;
                        float edistSq = edx * edx + edz * edz;
                        if (edistSq < 36.0f)
                        {
                            score -= (36.0f - edistSq) * 1.5f;
                        }
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = candX;
                    bestZ = candZ;
                }
            }

            return (bestX, bestZ);
        }
    }
}
