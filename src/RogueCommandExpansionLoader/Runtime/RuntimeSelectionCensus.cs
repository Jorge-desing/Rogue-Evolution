using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of active player selection, control groups, and selected commands.
    /// Decoupled from Unity types to support clean-room MCP telemetry and offline validation.
    /// </summary>
    public sealed class SelectionCensus
    {
        public int OmittedSelectedCount { get; }
        public int SelectedCount { get; }
        public IReadOnlyList<string> SelectedEntityKeys { get; }
        public IReadOnlyList<string> SelectedEntityIds { get; }
        public bool HasSelectedAiEntity { get; }
        public string SelectedAiEntityKey { get; }
        public bool OnlyBuildingsSelected { get; }
        public bool OnlyEntitiesThatCannotAttackSelected { get; }
        public int ActiveSkillSelectedCount { get; }
        public IReadOnlyList<int> ControlGroupCounts { get; }
        public bool IsSelectionReady { get; }

        public SelectionCensus(
            int omittedSelectedCount,
            int selectedCount,
            IReadOnlyList<string> selectedEntityKeys,
            IReadOnlyList<string> selectedEntityIds,
            bool hasSelectedAiEntity,
            string selectedAiEntityKey,
            bool onlyBuildingsSelected,
            bool onlyEntitiesThatCannotAttackSelected,
            int activeSkillSelectedCount,
            IReadOnlyList<int> controlGroupCounts,
            bool isSelectionReady)
        {
            OmittedSelectedCount = omittedSelectedCount;
            SelectedCount = selectedCount;
            SelectedEntityKeys = selectedEntityKeys ?? Array.Empty<string>();
            SelectedEntityIds = selectedEntityIds ?? Array.Empty<string>();
            HasSelectedAiEntity = hasSelectedAiEntity;
            SelectedAiEntityKey = selectedAiEntityKey ?? string.Empty;
            OnlyBuildingsSelected = onlyBuildingsSelected;
            OnlyEntitiesThatCannotAttackSelected = onlyEntitiesThatCannotAttackSelected;
            ActiveSkillSelectedCount = activeSkillSelectedCount;
            ControlGroupCounts = controlGroupCounts ?? Array.Empty<int>();
            IsSelectionReady = isSelectionReady;
        }

        public static SelectionCensus Empty { get; } =
            new SelectionCensus(0, 0, Array.Empty<string>(), Array.Empty<string>(), false, string.Empty, false, false, 0, new int[10], false);
    }

    /// <summary>
    /// Diagnostic collector that inspects the live <see cref="PlayerUnitController"/>
    /// across game runtime and test stubs without throwing.
    /// </summary>
    public static class RuntimeSelectionCensus
    {
        private const int MaxSelectedEntitiesToReport = 32;

        public static SelectionCensus Current()
        {
            try
            {
                object controller = ResolvePlayerUnitController();
                if (controller == null)
                {
                    return SelectionCensus.Empty;
                }

                Type type = controller.GetType();
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

                // Selected GameObjects
                FieldInfo selectedField = type.GetField("_selectedGameObjects", flags);
                var selectedSet = selectedField?.GetValue(controller) as IEnumerable<GameObject>;

                var keys = new List<string>();
                var entityIds = new List<string>();
                int totalCount = 0;

                if (selectedSet != null)
                {
                    foreach (GameObject go in selectedSet)
                    {
                        if (go == null) continue;
                        totalCount++;

                        if (keys.Count < MaxSelectedEntitiesToReport)
                        {
                            string key = go.name ?? string.Empty;
                            string id = string.Empty;

                            try
                            {
                                EntityController ec = go.GetComponent<EntityController>();
                                if (ec != null)
                                {
                                    id = ec.EntityId ?? string.Empty;
                                    if (!string.IsNullOrEmpty(ec.EntityId))
                                    {
                                        key = ec.EntityId;
                                    }
                                }
                            }
                            catch
                            {
                                // Degrade gracefully
                            }

                            keys.Add(key);
                            entityIds.Add(id);
                        }
                    }
                }

                int omittedCount = Math.Max(0, totalCount - MaxSelectedEntitiesToReport);

                // Selected AI entity
                FieldInfo selectedAiField = type.GetField("_selectedAiEntityController", flags);
                var selectedAi = selectedAiField?.GetValue(controller) as EntityController;
                bool hasSelectedAi = selectedAi != null;
                string selectedAiKey = selectedAi != null ? (selectedAi.EntityId ?? selectedAi.name ?? string.Empty) : string.Empty;

                // Flags
                bool onlyBuildings = ReadBool(controller, type, flags, "OnlyBuildingsSelected");
                bool onlyCannotAttack = ReadBool(controller, type, flags, "OnlyEntitiesThatCannotAttackSelected");

                // Active skill selected
                FieldInfo activeSkillsField = type.GetField("_selectedEntitiesWithActiveSkill", flags);
                int activeSkillCount = GetCollectionCount(activeSkillsField?.GetValue(controller));

                // Control groups
                int[] groupCounts = new int[10];
                FieldInfo controlGroupsField = type.GetField("_controlGroups", flags);
                if (controlGroupsField?.GetValue(controller) is Array groupsArray)
                {
                    for (int i = 0; i < Math.Min(10, groupsArray.Length); i++)
                    {
                        groupCounts[i] = GetCollectionCount(groupsArray.GetValue(i));
                    }
                }

                return new SelectionCensus(
                    omittedCount,
                    totalCount,
                    keys,
                    entityIds,
                    hasSelectedAi,
                    selectedAiKey,
                    onlyBuildings,
                    onlyCannotAttack,
                    activeSkillCount,
                    groupCounts,
                    true);
            }
            catch
            {
                return SelectionCensus.Empty;
            }
        }

        private static object ResolvePlayerUnitController()
        {
            try
            {
                Type type = typeof(PlayerUnitController);
                FieldInfo instanceField = type.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object instance = instanceField?.GetValue(null);
                if (instance != null)
                {
                    return instance;
                }

                return UnityEngine.Object.FindObjectOfType<PlayerUnitController>();
            }
            catch
            {
                return null;
            }
        }

        private static bool ReadBool(object target, Type type, BindingFlags flags, string memberName)
        {
            try
            {
                PropertyInfo prop = type.GetProperty(memberName, flags);
                if (prop != null)
                {
                    return Convert.ToBoolean(prop.GetValue(target, null));
                }

                FieldInfo field = type.GetField(memberName, flags);
                if (field != null)
                {
                    return Convert.ToBoolean(field.GetValue(target));
                }
            }
            catch
            {
                // Degrade gracefully
            }

            return false;
        }

        private static int GetCollectionCount(object collection)
        {
            if (collection == null) return 0;
            if (collection is System.Collections.ICollection col) return col.Count;

            PropertyInfo countProp = collection.GetType().GetProperty("Count");
            if (countProp != null)
            {
                try
                {
                    return Convert.ToInt32(countProp.GetValue(collection, null));
                }
                catch { }
            }

            if (collection is System.Collections.IEnumerable enumerable)
            {
                int count = 0;
                var enumerator = enumerable.GetEnumerator();
                while (enumerator.MoveNext()) count++;
                return count;
            }

            return 0;
        }
    }
}
