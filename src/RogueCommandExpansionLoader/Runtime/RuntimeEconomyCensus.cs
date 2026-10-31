using System;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of match economy and progression parameters.
    /// Captures player and AI crystal balances, current attack wave number, and
    /// match elapsed time without game type coupling.
    /// </summary>
    public sealed class EconomyCensus
    {
        public int PlayerCredits { get; }
        public int AiCredits { get; }
        public int AttackWaveNumber { get; }
        public float ElapsedGameTimeSeconds { get; }
        public int HarvesterCount { get; }

        public EconomyCensus(
            int playerCredits,
            int aiCredits,
            int attackWaveNumber,
            float elapsedGameTimeSeconds,
            int harvesterCount)
        {
            PlayerCredits = playerCredits;
            AiCredits = aiCredits;
            AttackWaveNumber = attackWaveNumber;
            ElapsedGameTimeSeconds = elapsedGameTimeSeconds;
            HarvesterCount = harvesterCount;
        }

        public static EconomyCensus Empty { get; } =
            new EconomyCensus(0, 0, 0, 0f, 0);
    }

    /// <summary>
    /// Safely inspects the live bank balance, attack wave number, and match timer
    /// across game and test stubs.
    /// </summary>
    public static class RuntimeEconomyCensus
    {
        public static EconomyCensus Current()
        {
            try
            {
                int playerCredits = 0;
                int aiCredits = 0;
                try
                {
                    playerCredits = Bank.ActualBalance("Player");
                    aiCredits = Bank.ActualBalance("AI");
                }
                catch
                {
                    // Degrade gracefully if Bank is not ready
                }

                int attackWave = 0;
                try
                {
                    attackWave = AiBehaviourManager.CurrentAttackWaveNumber;
                }
                catch
                {
                    // Degrade gracefully if AiBehaviourManager is not ready
                }

                int harvesterCount = 0;
                try
                {
                    ExistingControllers existing = ExistingControllers.Instance;
                    if (existing != null && existing.Harvesters != null)
                    {
                        harvesterCount = existing.Harvesters.Count;
                    }
                }
                catch
                {
                    // Degrade gracefully
                }

                float time = 0f;
                try
                {
                    time = Time.time;
                }
                catch
                {
                    // Degrade gracefully
                }

                return new EconomyCensus(playerCredits, aiCredits, attackWave, time, harvesterCount);
            }
            catch
            {
                return EconomyCensus.Empty;
            }
        }
    }
}
