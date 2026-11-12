using System;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    internal enum RangeDisplayKind
    {
        Weapon,
        Skill,
        Effect1,
        Effect2,
        Sight
    }

    internal readonly struct RangeDisplaySpec
    {
        public RangeDisplaySpec(RangeDisplayKind kind, float radius)
        {
            Kind = kind;
            Radius = radius;
        }

        public RangeDisplayKind Kind { get; }
        public float Radius { get; }
    }

    internal readonly struct RangeDisplayEntityState
    {
        public RangeDisplayEntityState(
            bool isSelected,
            bool isPlayerControlled,
            bool isDestroyed,
            bool canAttack,
            bool isMelee,
            float weaponRange,
            bool hasActiveSkill,
            bool skillRangeIsUnlimited,
            float skillRange,
            float effectRadius1,
            float effectRadius2,
            float sightRadius)
        {
            IsSelected = isSelected;
            IsPlayerControlled = isPlayerControlled;
            IsDestroyed = isDestroyed;
            CanAttack = canAttack;
            IsMelee = isMelee;
            WeaponRange = weaponRange;
            HasActiveSkill = hasActiveSkill;
            SkillRangeIsUnlimited = skillRangeIsUnlimited;
            SkillRange = skillRange;
            EffectRadius1 = effectRadius1;
            EffectRadius2 = effectRadius2;
            SightRadius = sightRadius;
        }

        public bool IsSelected { get; }
        public bool IsPlayerControlled { get; }
        public bool IsDestroyed { get; }
        public bool CanAttack { get; }
        public bool IsMelee { get; }
        public float WeaponRange { get; }
        public bool HasActiveSkill { get; }
        public bool SkillRangeIsUnlimited { get; }
        public float SkillRange { get; }
        public float EffectRadius1 { get; }
        public float EffectRadius2 { get; }
        public float SightRadius { get; }
    }

    internal readonly struct RangeDisplayOptions
    {
        public RangeDisplayOptions(bool showWeapon, bool showSkill, bool showEffects, bool showSight)
        {
            ShowWeapon = showWeapon;
            ShowSkill = showSkill;
            ShowEffects = showEffects;
            ShowSight = showSight;
        }

        public bool ShowWeapon { get; }
        public bool ShowSkill { get; }
        public bool ShowEffects { get; }
        public bool ShowSight { get; }

        public static RangeDisplayOptions Default => new RangeDisplayOptions(
            showWeapon: true,
            showSkill: true,
            showEffects: true,
            showSight: false);
    }

    internal readonly struct RangeDisplaySelectionDelta<T>
    {
        public RangeDisplaySelectionDelta(T[] added, T[] removed)
        {
            Added = added;
            Removed = removed;
        }

        public T[] Added { get; }
        public T[] Removed { get; }
    }

    internal sealed class RangeDisplaySelectionTracker<T>
    {
        private readonly System.Collections.Generic.HashSet<T> _previous = new System.Collections.Generic.HashSet<T>();

        public RangeDisplaySelectionDelta<T> Update(
            System.Collections.Generic.IEnumerable<T> current,
            System.Collections.Generic.IComparer<T> comparer = null)
        {
            var next = current == null
                ? new System.Collections.Generic.HashSet<T>()
                : new System.Collections.Generic.HashSet<T>(current);
            var added = new System.Collections.Generic.List<T>();
            var removed = new System.Collections.Generic.List<T>();

            foreach (T value in next)
            {
                if (!_previous.Contains(value))
                {
                    added.Add(value);
                }
            }
            foreach (T value in _previous)
            {
                if (!next.Contains(value))
                {
                    removed.Add(value);
                }
            }

            comparer = comparer ?? System.Collections.Generic.Comparer<T>.Default;
            added.Sort(comparer);
            removed.Sort(comparer);
            _previous.Clear();
            _previous.UnionWith(next);
            return new RangeDisplaySelectionDelta<T>(added.ToArray(), removed.ToArray());
        }
    }

    internal static class RangeDisplayGeometry
    {
        private const int MinimumSegments = 8;
        private const int MaximumSegments = 256;

        public static UnityEngine.Vector3[] CreateCirclePoints(float radius, int segments, float heightOffset)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
            if (segments < MinimumSegments || segments > MaximumSegments)
            {
                throw new ArgumentOutOfRangeException(nameof(segments));
            }
            if (float.IsNaN(heightOffset) || float.IsInfinity(heightOffset))
            {
                throw new ArgumentOutOfRangeException(nameof(heightOffset));
            }

            var points = new UnityEngine.Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                double angle = 2.0 * Math.PI * i / segments;
                points[i] = new UnityEngine.Vector3(
                    radius * (float)Math.Cos(angle),
                    heightOffset,
                    radius * (float)Math.Sin(angle));
            }
            return points;
        }
    }

    internal static class RangeDisplayPolicy
    {
        private const float MinimumVisibleRadius = 0.3f;
        private const float DuplicateEffectTolerance = 0.05f;
        private const float WorldUnitsPerCell = 10f;

        public static RangeDisplaySpec[] Resolve(
            RangeDisplayEntityState entity,
            RangeDisplayOptions options,
            bool enabled = true)
        {
            if (!enabled || !entity.IsSelected || !entity.IsPlayerControlled || entity.IsDestroyed)
            {
                return Array.Empty<RangeDisplaySpec>();
            }

            var specs = new System.Collections.Generic.List<RangeDisplaySpec>(5);
            if (options.ShowWeapon && entity.CanAttack && !entity.IsMelee)
            {
                AddIfVisible(specs, RangeDisplayKind.Weapon, entity.WeaponRange * WorldUnitsPerCell);
            }

            if (options.ShowSkill && entity.HasActiveSkill && !entity.SkillRangeIsUnlimited)
            {
                AddIfVisible(specs, RangeDisplayKind.Skill, entity.SkillRange * WorldUnitsPerCell);
            }

            if (options.ShowEffects)
            {
                AddIfVisible(specs, RangeDisplayKind.Effect1, entity.EffectRadius1 * WorldUnitsPerCell);
                if (!IsFinite(entity.EffectRadius1) || !IsFinite(entity.EffectRadius2) ||
                    Math.Abs(entity.EffectRadius2 - entity.EffectRadius1) > DuplicateEffectTolerance)
                {
                    AddIfVisible(specs, RangeDisplayKind.Effect2, entity.EffectRadius2 * WorldUnitsPerCell);
                }
            }

            if (options.ShowSight)
            {
                AddIfVisible(specs, RangeDisplayKind.Sight, entity.SightRadius * WorldUnitsPerCell);
            }

            return specs.ToArray();
        }

        private static void AddIfVisible(
            System.Collections.Generic.List<RangeDisplaySpec> specs,
            RangeDisplayKind kind,
            float radius)
        {
            if (IsFinite(radius) && radius > MinimumVisibleRadius * WorldUnitsPerCell)
            {
                specs.Add(new RangeDisplaySpec(kind, radius));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        // Authored per-kind colors for the world-space range guides. Kept here (rather than inside the
        // MonoBehaviour) so the color contract is a pure, unit-testable policy the live diagnostics
        // bridge can read without instantiating the range display at runtime.
        public static Color RangeColor(RangeDisplayKind kind)
        {
            switch (kind)
            {
                case RangeDisplayKind.Weapon:
                    return new Color(0.95f, 0.18f, 0.12f, 0.82f);
                case RangeDisplayKind.Skill:
                    return new Color(0.12f, 0.48f, 0.95f, 0.82f);
                case RangeDisplayKind.Effect1:
                    return new Color(0.98f, 0.48f, 0.08f, 0.82f);
                case RangeDisplayKind.Effect2:
                    return new Color(0.96f, 0.72f, 0.18f, 0.82f);
                default:
                    return new Color(0.72f, 0.76f, 0.82f, 0.62f);
            }
        }
    }
}
