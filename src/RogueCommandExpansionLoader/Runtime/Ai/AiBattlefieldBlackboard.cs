using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime.Ai
{
    /// <summary>
    /// Immutable battlefield perception frame shared across coordinated AI modules in a single tick.
    /// Omission counters are placed strictly first to respect bounded trace formatting rules.
    /// </summary>
    public sealed class AiBattlefieldSnapshot
    {
        public int OmittedAiUnitCount { get; }
        public int OmittedHostileUnitCount { get; }
        public float Timestamp { get; }
        public int BaseIndex { get; }
        public int CurrentWaveNumber { get; }
        public int TotalAiCombatUnits { get; }
        public int TotalAiHarvesters { get; }
        public int TotalAiBuildings { get; }
        public int TotalHostileCombatUnits { get; }
        public float TotalAiCombatPower { get; }
        public float TotalHostileCombatPower { get; }
        public float HostileCentroidX { get; }
        public float HostileCentroidZ { get; }
        public bool UnderHarvesterOrBasePressure { get; }
        public IReadOnlyList<AiUnitSnapshot> AiUnits { get; }
        public IReadOnlyList<AiUnitSnapshot> HostileUnits { get; }

        public AiBattlefieldSnapshot(
            float timestamp,
            int baseIndex,
            int currentWaveNumber,
            int omittedAiUnitCount,
            int omittedHostileUnitCount,
            int totalAiCombatUnits,
            int totalAiHarvesters,
            int totalAiBuildings,
            int totalHostileCombatUnits,
            float totalAiCombatPower,
            float totalHostileCombatPower,
            float hostileCentroidX,
            float hostileCentroidZ,
            bool underHarvesterOrBasePressure,
            IReadOnlyList<AiUnitSnapshot> aiUnits,
            IReadOnlyList<AiUnitSnapshot> hostileUnits)
        {
            OmittedAiUnitCount = Math.Max(0, omittedAiUnitCount);
            OmittedHostileUnitCount = Math.Max(0, omittedHostileUnitCount);
            Timestamp = float.IsNaN(timestamp) || float.IsInfinity(timestamp) ? 0f : timestamp;
            BaseIndex = baseIndex;
            CurrentWaveNumber = Math.Max(0, currentWaveNumber);
            TotalAiCombatUnits = Math.Max(0, totalAiCombatUnits);
            TotalAiHarvesters = Math.Max(0, totalAiHarvesters);
            TotalAiBuildings = Math.Max(0, totalAiBuildings);
            TotalHostileCombatUnits = Math.Max(0, totalHostileCombatUnits);
            TotalAiCombatPower = Math.Max(0f, totalAiCombatPower);
            TotalHostileCombatPower = Math.Max(0f, totalHostileCombatPower);
            HostileCentroidX = float.IsNaN(hostileCentroidX) || float.IsInfinity(hostileCentroidX) ? 0f : hostileCentroidX;
            HostileCentroidZ = float.IsNaN(hostileCentroidZ) || float.IsInfinity(hostileCentroidZ) ? 0f : hostileCentroidZ;
            UnderHarvesterOrBasePressure = underHarvesterOrBasePressure;
            AiUnits = aiUnits ?? Array.Empty<AiUnitSnapshot>();
            HostileUnits = hostileUnits ?? Array.Empty<AiUnitSnapshot>();
        }

        public static AiBattlefieldSnapshot Empty { get; } = new AiBattlefieldSnapshot(
            timestamp: 0f,
            baseIndex: -1,
            currentWaveNumber: 0,
            omittedAiUnitCount: 0,
            omittedHostileUnitCount: 0,
            totalAiCombatUnits: 0,
            totalAiHarvesters: 0,
            totalAiBuildings: 0,
            totalHostileCombatUnits: 0,
            totalAiCombatPower: 0f,
            totalHostileCombatPower: 0f,
            hostileCentroidX: 0f,
            hostileCentroidZ: 0f,
            underHarvesterOrBasePressure: false,
            aiUnits: Array.Empty<AiUnitSnapshot>(),
            hostileUnits: Array.Empty<AiUnitSnapshot>());
    }

    /// <summary>
    /// Pure builder that aggregates pre-filtered AI and hostile entity snapshots into a single
    /// <see cref="AiBattlefieldSnapshot"/> with bounded list caps and threat vector metrics.
    /// </summary>
    public static class AiBattlefieldBlackboardBuilder
    {
        public const int DefaultMaxTrackedUnitsPerSide = 64;
        public const float DefaultPressureRadiusSquared = 18f * 18f;

        public static AiBattlefieldSnapshot BuildSnapshot(
            float timestamp,
            int baseIndex,
            int currentWaveNumber,
            IReadOnlyList<AiUnitSnapshot> aiUnits,
            IReadOnlyList<AiUnitSnapshot> hostileUnits,
            int maxTrackedUnitsPerSide = DefaultMaxTrackedUnitsPerSide)
        {
            int cap = Math.Max(1, maxTrackedUnitsPerSide);

            var boundedAi = new List<AiUnitSnapshot>();
            var boundedHostiles = new List<AiUnitSnapshot>();
            var protectedIds = new HashSet<int>();
            var protectedPositions = new List<(float X, float Z)>();

            int omittedAi = 0;
            int totalAiCombat = 0;
            int totalAiHarvesters = 0;
            int totalAiBuildings = 0;
            float totalAiPower = 0f;

            if (aiUnits != null)
            {
                for (int i = 0; i < aiUnits.Count; i++)
                {
                    AiUnitSnapshot unit = aiUnits[i];
                    if (baseIndex >= 0 && unit.BaseIndex >= 0 && unit.BaseIndex != baseIndex)
                    {
                        continue;
                    }

                    if (unit.IsCombatUnit)
                    {
                        totalAiCombat++;
                        totalAiPower += unit.CombinedCombatPower;
                    }

                    if (unit.IsHarvester)
                    {
                        totalAiHarvesters++;
                        protectedIds.Add(unit.UniqueEntityId);
                        protectedPositions.Add((unit.PosX, unit.PosZ));
                    }

                    if (unit.IsBuilding)
                    {
                        totalAiBuildings++;
                        protectedIds.Add(unit.UniqueEntityId);
                        protectedPositions.Add((unit.PosX, unit.PosZ));
                    }

                    if (boundedAi.Count < cap)
                    {
                        boundedAi.Add(unit);
                    }
                    else
                    {
                        omittedAi++;
                    }
                }
            }

            int omittedHostiles = 0;
            int totalHostileCombat = 0;
            float totalHostilePower = 0f;
            float sumHostileX = 0f;
            float sumHostileZ = 0f;
            int centroidCount = 0;
            bool underPressure = false;

            if (hostileUnits != null)
            {
                for (int i = 0; i < hostileUnits.Count; i++)
                {
                    AiUnitSnapshot hostile = hostileUnits[i];
                    if (hostile.IsCombatUnit)
                    {
                        totalHostileCombat++;
                        totalHostilePower += hostile.CombinedCombatPower;
                    }

                    sumHostileX += hostile.PosX;
                    sumHostileZ += hostile.PosZ;
                    centroidCount++;

                    if (!underPressure)
                    {
                        if (hostile.CurrentTargetId >= 0 && protectedIds.Contains(hostile.CurrentTargetId))
                        {
                            underPressure = true;
                        }
                        else
                        {
                            for (int p = 0; p < protectedPositions.Count; p++)
                            {
                                float dx = hostile.PosX - protectedPositions[p].X;
                                float dz = hostile.PosZ - protectedPositions[p].Z;
                                if (dx * dx + dz * dz <= DefaultPressureRadiusSquared)
                                {
                                    underPressure = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (boundedHostiles.Count < cap)
                    {
                        boundedHostiles.Add(hostile);
                    }
                    else
                    {
                        omittedHostiles++;
                    }
                }
            }

            float centroidX = centroidCount > 0 ? sumHostileX / centroidCount : 0f;
            float centroidZ = centroidCount > 0 ? sumHostileZ / centroidCount : 0f;

            return new AiBattlefieldSnapshot(
                timestamp: timestamp,
                baseIndex: baseIndex,
                currentWaveNumber: currentWaveNumber,
                omittedAiUnitCount: omittedAi,
                omittedHostileUnitCount: omittedHostiles,
                totalAiCombatUnits: totalAiCombat,
                totalAiHarvesters: totalAiHarvesters,
                totalAiBuildings: totalAiBuildings,
                totalHostileCombatUnits: totalHostileCombat,
                totalAiCombatPower: totalAiPower,
                totalHostileCombatPower: totalHostilePower,
                hostileCentroidX: centroidX,
                hostileCentroidZ: centroidZ,
                underHarvesterOrBasePressure: underPressure,
                aiUnits: boundedAi.AsReadOnly(),
                hostileUnits: boundedHostiles.AsReadOnly());
        }
    }
}
