using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure diagnostic telemetry row for an entity's combat experience, rank tier, and veterancy bonus.
    /// Decoupled from Unity types to support offline testing and MCP inspection.
    /// </summary>
    public readonly struct UnitVeterancyDiagnosticRow
    {
        public UnitVeterancyDiagnosticRow(
            string entityKeyId,
            string entityId,
            bool isControlledByPlayer,
            int currentRank,
            int maxRank,
            int combatXp,
            int xpToNextRank,
            float healthBonusMultiplier,
            float damageBonusMultiplier)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            IsControlledByPlayer = isControlledByPlayer;
            CurrentRank = currentRank;
            MaxRank = maxRank;
            CombatXp = combatXp;
            XpToNextRank = xpToNextRank;
            HealthBonusMultiplier = healthBonusMultiplier;
            DamageBonusMultiplier = damageBonusMultiplier;
        }

        public string EntityKeyId { get; }
        public string EntityId { get; }
        public bool IsControlledByPlayer { get; }
        public int CurrentRank { get; }
        public int MaxRank { get; }
        public int CombatXp { get; }
        public int XpToNextRank { get; }
        public float HealthBonusMultiplier { get; }
        public float DamageBonusMultiplier { get; }
    }

    /// <summary>
    /// Pure diagnostic snapshot of veterancy levels, combat experience, and rank tiers across living battlefield units.
    /// Omission counter placed strictly first to respect bounded trace caps.
    /// </summary>
    public sealed class VeterancyCensus
    {
        public int OmittedUnitCount { get; }
        public int TotalVeterancyUnitsCount { get; }
        public int TotalLivingUnitsCount { get; }
        public int VeteranRankUnitsCount { get; }
        public IReadOnlyList<UnitVeterancyDiagnosticRow> Units { get; }

        public VeterancyCensus(
            int omittedUnitCount,
            int totalVeterancyUnitsCount,
            int totalLivingUnitsCount,
            int veteranRankUnitsCount,
            IReadOnlyList<UnitVeterancyDiagnosticRow> units)
        {
            OmittedUnitCount = omittedUnitCount;
            TotalVeterancyUnitsCount = totalVeterancyUnitsCount;
            TotalLivingUnitsCount = totalLivingUnitsCount;
            VeteranRankUnitsCount = veteranRankUnitsCount;
            Units = units ?? new List<UnitVeterancyDiagnosticRow>().AsReadOnly();
        }

        public static VeterancyCensus Empty { get; } =
            new VeterancyCensus(0, 0, 0, 0, new List<UnitVeterancyDiagnosticRow>().AsReadOnly());
    }

    /// <summary>
    /// Queries combat experience points, veterancy ranks, and stat bonuses across living units.
    /// </summary>
    public static class RuntimeVeterancyCensus
    {
        public const int MaxTrackedUnits = 32;

        public static VeterancyCensus Current()
        {
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return VeterancyCensus.Empty;
                }

                var candidateEntities = new List<EntityController>();
                HashSet<EntityController> playerUnits = existing.PlayerUnits();
                if (playerUnits != null)
                {
                    candidateEntities.AddRange(playerUnits);
                }
                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits != null)
                {
                    candidateEntities.AddRange(aiUnits);
                }

                int totalLiving = 0;
                int veteranRankCount = 0;
                var allVeterancyRows = new List<UnitVeterancyDiagnosticRow>();

                for (int i = 0; i < candidateEntities.Count; i++)
                {
                    EntityController entity = candidateEntities[i];
                    if (entity == null || entity.IsDestroyed || entity.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    totalLiving++;

                    int currentRank, maxRank, combatXp, xpToNextRank;
                    float hpBonus, dmgBonus;
                    RuntimeUnitMechanicsDirector.GetUnitVeterancyStats(
                        entity,
                        out currentRank,
                        out maxRank,
                        out combatXp,
                        out xpToNextRank,
                        out hpBonus,
                        out dmgBonus);

                    if (currentRank > 0)
                    {
                        veteranRankCount++;
                    }

                    string keyId = entity.UniqueEntityId > 0 ? entity.UniqueEntityId.ToString() : entity.EntityId;
                    var row = new UnitVeterancyDiagnosticRow(
                        keyId,
                        entity.EntityId ?? string.Empty,
                        entity.IsControlledByPlayer,
                        currentRank,
                        maxRank,
                        combatXp,
                        xpToNextRank,
                        hpBonus,
                        dmgBonus);

                    allVeterancyRows.Add(row);
                }

                // Sort: rank descending, then combat XP descending
                allVeterancyRows.Sort(delegate(UnitVeterancyDiagnosticRow a, UnitVeterancyDiagnosticRow b)
                {
                    int rankComp = b.CurrentRank.CompareTo(a.CurrentRank);
                    if (rankComp != 0) return rankComp;
                    return b.CombatXp.CompareTo(a.CombatXp);
                });

                int totalVeterancyUnits = allVeterancyRows.Count;
                int toTake = Math.Min(totalVeterancyUnits, MaxTrackedUnits);
                int omittedCount = Math.Max(0, totalVeterancyUnits - toTake);

                var cappedRows = new List<UnitVeterancyDiagnosticRow>(toTake);
                for (int i = 0; i < toTake; i++)
                {
                    cappedRows.Add(allVeterancyRows[i]);
                }

                return new VeterancyCensus(
                    omittedCount,
                    totalVeterancyUnits,
                    totalLiving,
                    veteranRankCount,
                    cappedRows.AsReadOnly());
            }
            catch
            {
                return VeterancyCensus.Empty;
            }
        }
    }
}
