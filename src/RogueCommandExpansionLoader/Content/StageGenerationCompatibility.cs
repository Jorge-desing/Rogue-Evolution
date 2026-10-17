using System;
using System.Collections;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>
    /// Mirrors the candidate filters used by LevelProgression.GetCurrentLevelMap for a stage.
    /// The snapshots are intentionally Unity-free so the selection rules can be exercised offline.
    /// </summary>
    internal static class StageGenerationCompatibility
    {
        public static object[] ResolveProgressionProfiles(object defaultProfile, object heatProfile, object firstRunProfile)
        {
            return new[]
            {
                defaultProfile,
                heatProfile ?? defaultProfile,
                firstRunProfile ?? defaultProfile
            };
        }

        public static bool TryBuildContiguousStageRange(
            IList<int> stageIndices,
            out int minimum,
            out int maximum,
            out string diagnostic)
        {
            minimum = -1;
            maximum = -1;
            diagnostic = null;
            if (stageIndices == null || stageIndices.Count == 0)
            {
                diagnostic = "At least one referenced stage index is required.";
                return false;
            }

            var sorted = new List<int>(stageIndices.Count);
            for (int i = 0; i < stageIndices.Count; i++)
            {
                if (stageIndices[i] < 0)
                {
                    diagnostic = "Referenced stage indices cannot be negative.";
                    return false;
                }
                sorted.Add(stageIndices[i]);
            }

            sorted.Sort();
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] != sorted[i - 1] + 1)
                {
                    diagnostic = "Referenced stages must be unique and contiguous.";
                    return false;
                }
            }

            minimum = sorted[0];
            maximum = sorted[sorted.Count - 1];
            return true;
        }

        public static bool TryBuildStageIndices(
            IList progressionStages,
            IList<object> loadedStageReferences,
            string progressionName,
            out int[] stageIndices,
            out string diagnostic)
        {
            stageIndices = Array.Empty<int>();
            diagnostic = null;
            if (progressionStages == null || loadedStageReferences == null)
            {
                diagnostic = "Progression '" + progressionName + "' is missing native stages or loaded stage references.";
                return false;
            }

            int alreadyRegisteredStageCount = 0;
            for (int progressionIndex = 0; progressionIndex < progressionStages.Count; progressionIndex++)
            {
                for (int loadedIndex = 0; loadedIndex < loadedStageReferences.Count; loadedIndex++)
                {
                    if (ReferenceEquals(progressionStages[progressionIndex], loadedStageReferences[loadedIndex]))
                    {
                        alreadyRegisteredStageCount++;
                        break;
                    }
                }
            }

            int firstContentStageIndex = progressionStages.Count - alreadyRegisteredStageCount;
            if (firstContentStageIndex < 0)
            {
                diagnostic = "Progression '" + progressionName +
                    "' contains more loader stages than registered stage entries.";
                return false;
            }

            stageIndices = new int[loadedStageReferences.Count];
            for (int loadedIndex = 0; loadedIndex < loadedStageReferences.Count; loadedIndex++)
            {
                object loadedStage = loadedStageReferences[loadedIndex];
                if (loadedStage == null)
                {
                    stageIndices = Array.Empty<int>();
                    diagnostic = "Progression '" + progressionName + "' contains a null loaded stage reference.";
                    return false;
                }

                int expectedStageIndex = firstContentStageIndex + loadedIndex;
                int registeredStageIndex = -1;
                for (int progressionIndex = 0; progressionIndex < progressionStages.Count; progressionIndex++)
                {
                    if (ReferenceEquals(progressionStages[progressionIndex], loadedStage))
                    {
                        registeredStageIndex = progressionIndex;
                        break;
                    }
                }

                if (registeredStageIndex >= 0 && registeredStageIndex != expectedStageIndex)
                {
                    stageIndices = Array.Empty<int>();
                    diagnostic = "Progression '" + progressionName + "' has an unexpected content stage order.";
                    return false;
                }

                stageIndices[loadedIndex] = expectedStageIndex;
            }

            return true;
        }

        public static bool TryValidate(
            string stageId,
            string progressionName,
            int stageIndex,
            int mapSizeMin,
            int mapSizeMax,
            bool isDemo,
            bool heatIsActive,
            int maxAscensionLevel,
            IList<StageGenerationAiCandidate> ais,
            IList<StageGenerationWorldCandidate> worlds,
            IList<StageGenerationLandscapeCandidate> landscapes,
            out string diagnostic)
        {
            diagnostic = null;
            if (String.IsNullOrWhiteSpace(stageId) || String.IsNullOrWhiteSpace(progressionName))
            {
                diagnostic = "Stage and progression names are required for generation validation.";
                return false;
            }

            if (stageIndex < 0 || mapSizeMin < 0 || mapSizeMax < mapSizeMin)
            {
                diagnostic = "Stage '" + stageId + "' has an invalid index or mapSizeRange [" +
                    mapSizeMin + ", " + mapSizeMax + "] in progression '" + progressionName + "'.";
                return false;
            }

            if (maxAscensionLevel < 0 || maxAscensionLevel > 100)
            {
                diagnostic = "Progression '" + progressionName + "' has an unsupported maxAscensionLevel " +
                    maxAscensionLevel + ".";
                return false;
            }

            if (ais == null || worlds == null || landscapes == null)
            {
                diagnostic = "Progression '" + progressionName + "' is missing AI, World, or LandscapeGenerator data.";
                return false;
            }

            // Heat can only be selected after the player reaches the configured ascension cap.
            // MetaGame clamps the active ascension to that cap, so lower values are unreachable
            // while heat is active and must not invalidate an otherwise usable Heat profile.
            int firstAscensionLevel = heatIsActive ? maxAscensionLevel : 0;
            for (int ascensionLevel = firstAscensionLevel; ascensionLevel <= maxAscensionLevel; ascensionLevel++)
            {
                List<StageGenerationAiCandidate> eligibleAis = new List<StageGenerationAiCandidate>();
                for (int aiIndex = 0; aiIndex < ais.Count; aiIndex++)
                {
                    StageGenerationAiCandidate ai = ais[aiIndex];
                    if (IsAiEligible(ai, stageIndex, ascensionLevel, heatIsActive, isDemo))
                    {
                        eligibleAis.Add(ai);
                    }
                }

                string context = "etapa '" + stageId + "' en progresión '" + progressionName +
                    "' (índice " + stageIndex + ", ascensión " + ascensionLevel +
                    (heatIsActive ? ", Heat activo" : ", Heat inactivo") +
                    (isDemo ? ", demo" : ", juego completo") + ")";
                if (eligibleAis.Count == 0)
                {
                    diagnostic = "No hay AI activa y válida para " + context + ".";
                    return false;
                }

                List<StageGenerationWorldCandidate> eligibleWorlds = new List<StageGenerationWorldCandidate>();
                for (int worldIndex = 0; worldIndex < worlds.Count; worldIndex++)
                {
                    StageGenerationWorldCandidate world = worlds[worldIndex];
                    if (world != null && !world.IsDeactivated && (!isDemo || world.IsAllowedForDemo))
                    {
                        eligibleWorlds.Add(world);
                    }
                }

                if (eligibleWorlds.Count == 0)
                {
                    diagnostic = "No hay World activa y permitida para " + context + ".";
                    return false;
                }

                for (int aiIndex = 0; aiIndex < eligibleAis.Count; aiIndex++)
                {
                    StageGenerationAiCandidate ai = eligibleAis[aiIndex];
                    for (int worldIndex = 0; worldIndex < eligibleWorlds.Count; worldIndex++)
                    {
                        StageGenerationWorldCandidate world = eligibleWorlds[worldIndex];
                        if (!HasCompatibleLandscape(ai, world, landscapes, mapSizeMin, mapSizeMax, isDemo))
                        {
                            diagnostic = "No hay LandscapeGenerator compatible con mapSizeRange [" +
                                mapSizeMin + ", " + mapSizeMax + "] para " + context +
                                ", AI '" + ai.Id + "' y World '" + world.Id + "'.";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        public static bool ShouldValidateStage(bool isDemo, bool isPartOfDemo)
        {
            return !isDemo || isPartOfDemo;
        }

        public static bool TryValidateStages(
            string progressionName,
            bool isDemo,
            bool heatIsActive,
            int maxAscensionLevel,
            IList<StageGenerationStageCandidate> stages,
            IList<StageGenerationAiCandidate> ais,
            IList<StageGenerationLandscapeCandidate> landscapes,
            out string diagnostic)
        {
            diagnostic = null;
            if (stages == null)
            {
                diagnostic = "Stage candidates are missing for progression '" + progressionName + "'.";
                return false;
            }

            for (int i = 0; i < stages.Count; i++)
            {
                StageGenerationStageCandidate stage = stages[i];
                if (stage == null)
                {
                    diagnostic = "Progression '" + progressionName + "' contains a null stage candidate.";
                    return false;
                }

                if (!ShouldValidateStage(isDemo, stage.IsPartOfDemo))
                {
                    continue;
                }

                if (!TryValidate(stage.Id, progressionName, stage.StageIndex,
                    stage.MapSizeMin, stage.MapSizeMax, isDemo, heatIsActive, maxAscensionLevel,
                    ais, stage.Worlds, landscapes, out diagnostic))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAiEligible(
            StageGenerationAiCandidate ai,
            int stageIndex,
            int ascensionLevel,
            bool heatIsActive,
            bool isDemo)
        {
            if (ai == null || ai.IsDeactivated || (isDemo && !ai.IsAllowedForDemo) ||
                stageIndex < ai.StageMin || stageIndex > ai.StageMax ||
                (!heatIsActive && (ascensionLevel < ai.AscensionMin || ascensionLevel > ai.AscensionMax)) ||
                ai.Bases == null)
            {
                return false;
            }

            for (int i = 0; i < ai.Bases.Count; i++)
            {
                StageGenerationAiBaseCandidate aiBase = ai.Bases[i];
                if (aiBase != null && stageIndex >= aiBase.StartingInStage &&
                    ascensionLevel >= aiBase.StartingInAscensionLevel)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCompatibleLandscape(
            StageGenerationAiCandidate ai,
            StageGenerationWorldCandidate world,
            IList<StageGenerationLandscapeCandidate> landscapes,
            int mapSizeMin,
            int mapSizeMax,
            bool isDemo)
        {
            IList<StageGenerationLandscapeCandidate> selectedMaps = ai.RestrictedMaps;
            bool explicitMapRestriction = selectedMaps != null && selectedMaps.Count > 0;
            if (!explicitMapRestriction)
            {
                selectedMaps = world.RestrictedMaps;
                explicitMapRestriction = selectedMaps != null && selectedMaps.Count > 0;
            }

            if (!explicitMapRestriction)
            {
                selectedMaps = landscapes;
            }

            bool foundCompatibleMap = false;
            for (int i = 0; i < selectedMaps.Count; i++)
            {
                StageGenerationLandscapeCandidate landscape = selectedMaps[i];
                if (landscape == null)
                {
                    return false;
                }

                if (landscape.MapSize < mapSizeMin || landscape.MapSize > mapSizeMax)
                {
                    continue;
                }

                bool isEligibleInDefaultList = !landscape.IsDeactivated &&
                    (!isDemo || landscape.IsAllowedForDemo);
                if (!isEligibleInDefaultList)
                {
                    if (explicitMapRestriction)
                    {
                        // CreateLevelMap uses AI/World restricted maps directly; it does not
                        // reapply the default activation and demo filters to those lists.
                        return false;
                    }

                    continue;
                }

                // Every in-range map can be selected randomly, so each applicable World
                // restriction must be safe rather than merely having one valid entry.
                if (!HasUsableRestrictedWorld(landscape.RestrictedWorlds,
                        world.RestrictedMaps == null || world.RestrictedMaps.Count == 0, isDemo))
                {
                    return false;
                }

                foundCompatibleMap = true;
            }

            return foundCompatibleMap;
        }

        private static bool HasUsableRestrictedWorld(
            IList<StageGenerationWorldCandidate> restrictedWorlds,
            bool mapWorldRestrictionIsUsed,
            bool isDemo)
        {
            // A null list cannot be safely selected by the game's map-generation path.
            if (restrictedWorlds == null)
            {
                return false;
            }

            // CreateLevelMap reads Count before checking whether the stage World has
            // its own restricted map list. A non-null list is only enumerated when
            // that World-level restriction is absent.
            if (!mapWorldRestrictionIsUsed)
            {
                return true;
            }

            // An empty list means the LandscapeGenerator does not override the stage's WorldList.
            if (restrictedWorlds.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < restrictedWorlds.Count; i++)
            {
                StageGenerationWorldCandidate world = restrictedWorlds[i];
                if (world == null || world.IsDeactivated || (isDemo && !world.IsAllowedForDemo))
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal sealed class StageGenerationAiCandidate
    {
        public string Id;
        public bool IsDeactivated;
        public bool IsAllowedForDemo;
        public int StageMin;
        public int StageMax;
        public int AscensionMin;
        public int AscensionMax;
        public IList<StageGenerationAiBaseCandidate> Bases;
        public IList<StageGenerationLandscapeCandidate> RestrictedMaps;
    }

    internal sealed class StageGenerationStageCandidate
    {
        public string Id;
        public int StageIndex;
        public int MapSizeMin;
        public int MapSizeMax;
        public bool IsPartOfDemo;
        public IList<StageGenerationWorldCandidate> Worlds;
    }

    internal sealed class StageGenerationAiBaseCandidate
    {
        public int StartingInStage;
        public int StartingInAscensionLevel;
    }

    internal sealed class StageGenerationWorldCandidate
    {
        public string Id;
        public bool IsDeactivated;
        public bool IsAllowedForDemo;
        public IList<StageGenerationLandscapeCandidate> RestrictedMaps;
    }

    internal sealed class StageGenerationLandscapeCandidate
    {
        public string Id;
        public int MapSize;
        public bool IsDeactivated;
        public bool IsAllowedForDemo;
        public IList<StageGenerationWorldCandidate> RestrictedWorlds;
    }
}
