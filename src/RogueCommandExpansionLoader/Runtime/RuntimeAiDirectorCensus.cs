using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of native AI Director state, attack wave progress,
    /// and active tactical command queue counts. Decoupled from Unity types.
    /// </summary>
    public sealed class AiDirectorCensus
    {
        public bool IsAiDirectorAvailable { get; }
        public int CurrentAttackWaveNumber { get; }
        public float TickDuration { get; }
        public bool ShowAiTags { get; }
        public bool ShowCurrentCommands { get; }
        public int AttackCommandCount { get; }
        public int DefendCommandCount { get; }
        public int ScoutCommandCount { get; }
        public int BuildCommandCount { get; }
        public int ProduceCommandCount { get; }
        public int OtherCommandCount { get; }
        public string ConstructionQueueSummary { get; }

        public AiDirectorCensus(
            bool isAiDirectorAvailable,
            int currentAttackWaveNumber,
            float tickDuration,
            bool showAiTags,
            bool showCurrentCommands,
            int attackCommandCount,
            int defendCommandCount,
            int scoutCommandCount,
            int buildCommandCount,
            int produceCommandCount,
            int otherCommandCount,
            string constructionQueueSummary)
        {
            IsAiDirectorAvailable = isAiDirectorAvailable;
            CurrentAttackWaveNumber = currentAttackWaveNumber;
            TickDuration = tickDuration;
            ShowAiTags = showAiTags;
            ShowCurrentCommands = showCurrentCommands;
            AttackCommandCount = attackCommandCount;
            DefendCommandCount = defendCommandCount;
            ScoutCommandCount = scoutCommandCount;
            BuildCommandCount = buildCommandCount;
            ProduceCommandCount = produceCommandCount;
            OtherCommandCount = otherCommandCount;
            ConstructionQueueSummary = constructionQueueSummary ?? string.Empty;
        }

        public static AiDirectorCensus Unavailable { get; } =
            new AiDirectorCensus(false, 0, 0f, false, false, 0, 0, 0, 0, 0, 0, string.Empty);
    }

    /// <summary>
    /// Diagnostic collector that inspects the live <see cref="AiBehaviourManager"/>
    /// without throwing exceptions.
    /// </summary>
    public static class RuntimeAiDirectorCensus
    {
        private static readonly BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static AiDirectorCensus Current()
        {
            try
            {
                object manager = ResolveAiBehaviourManager();
                if (manager == null)
                {
                    return AiDirectorCensus.Unavailable;
                }

                Type type = manager.GetType();

                int waveNumber = ReadInt(manager, type, "CurrentAttackWaveNumber");
                float tickDuration = ReadFloat(manager, type, "tickDuration");
                bool showTags = ReadBool(manager, type, "showAiTags");
                bool showCommands = ReadBool(manager, type, "showCurrentCommands");

                int attackCount = GetCollectionCount(ReadMemberValue(manager, type, "attackCommands"));
                int defendCount = GetCollectionCount(ReadMemberValue(manager, type, "defendCommands"));
                int scoutCount = GetCollectionCount(ReadMemberValue(manager, type, "scoutCommands"));
                int buildCount = GetCollectionCount(ReadMemberValue(manager, type, "buildCommands"));
                int produceCount = GetCollectionCount(ReadMemberValue(manager, type, "produceCommands"));
                int otherCount = GetCollectionCount(ReadMemberValue(manager, type, "otherCommands"));

                string queueSummary = ReadConstructionQueueSummary(type);

                return new AiDirectorCensus(
                    true,
                    waveNumber,
                    tickDuration,
                    showTags,
                    showCommands,
                    attackCount,
                    defendCount,
                    scoutCount,
                    buildCount,
                    produceCount,
                    otherCount,
                    queueSummary);
            }
            catch
            {
                return AiDirectorCensus.Unavailable;
            }
        }

        private static object ResolveAiBehaviourManager()
        {
            try
            {
                Type type = typeof(AiBehaviourManager);
                FieldInfo instanceField = type.GetField("_instance", Flags);
                object instance = instanceField?.GetValue(null);
                if (instance != null)
                {
                    return instance;
                }

                return UnityEngine.Object.FindObjectOfType<AiBehaviourManager>();
            }
            catch
            {
                return null;
            }
        }

        private static string ReadConstructionQueueSummary(Type type)
        {
            try
            {
                MethodInfo m = type.GetMethod("ConstructionQueueToString_Static", Flags);
                if (m != null)
                {
                    return m.Invoke(null, null) as string ?? string.Empty;
                }
            }
            catch { }

            return string.Empty;
        }

        private static object ReadMemberValue(object target, Type type, string name)
        {
            try
            {
                PropertyInfo prop = type.GetProperty(name, Flags);
                if (prop != null)
                {
                    MethodInfo getter = prop.GetGetMethod(true);
                    if (getter != null && getter.IsStatic)
                    {
                        return prop.GetValue(null, null);
                    }
                    return prop.GetValue(target, null);
                }

                FieldInfo field = type.GetField(name, Flags);
                if (field != null)
                {
                    if (field.IsStatic) return field.GetValue(null);
                    return field.GetValue(target);
                }
            }
            catch { }
            return null;
        }

        private static int ReadInt(object target, Type type, string name)
        {
            try
            {
                object val = ReadMemberValue(target, type, name);
                if (val != null) return Convert.ToInt32(val);
            }
            catch { }
            return 0;
        }

        private static float ReadFloat(object target, Type type, string name)
        {
            try
            {
                object val = ReadMemberValue(target, type, name);
                if (val != null) return Convert.ToSingle(val);
            }
            catch { }
            return 0f;
        }

        private static bool ReadBool(object target, Type type, string name)
        {
            try
            {
                object val = ReadMemberValue(target, type, name);
                if (val != null) return Convert.ToBoolean(val);
            }
            catch { }
            return false;
        }

        private static int GetCollectionCount(object collection)
        {
            if (collection == null) return 0;
            if (collection is ICollection col) return col.Count;

            PropertyInfo countProp = collection.GetType().GetProperty("Count");
            if (countProp != null)
            {
                try
                {
                    return Convert.ToInt32(countProp.GetValue(collection, null));
                }
                catch { }
            }

            if (collection is IEnumerable enumerable)
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
