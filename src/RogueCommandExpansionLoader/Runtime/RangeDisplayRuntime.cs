using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Renders optional, independent range guides for the player's current selection.
    /// This component owns only its child line renderers and does not patch game methods.
    /// </summary>
    public sealed class RangeDisplayRuntime : MonoBehaviour
    {
        private const float RefreshInterval = 0.2f;
        private const int DefaultSegments = 48;

        private readonly RangeDisplaySelectionTracker<int> _selectionTracker =
            new RangeDisplaySelectionTracker<int>();
        private readonly Dictionary<int, EntityCircles> _circles = new Dictionary<int, EntityCircles>();

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<KeyCode> _toggleKey;
        private ConfigEntry<bool> _showWeapon;
        private ConfigEntry<bool> _showSkill;
        private ConfigEntry<bool> _showEffects;
        private ConfigEntry<bool> _showSight;
        private ConfigEntry<float> _lineWidth;
        private ConfigEntry<int> _segments;
        private ConfigEntry<float> _heightOffset;
        private Action<string> _logWarning;
        private Material _sharedMaterial;
        private float _nextRefreshAt;
        private bool _failureLogged;

        public void Initialize(ConfigFile config, Action<string> logWarning)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            _logWarning = logWarning;
            _enabled = config.Bind("RangeDisplay", "Enabled", false,
                "Draw range circles for selected player units and buildings.");
            _toggleKey = config.Bind("RangeDisplay", "ToggleKey", KeyCode.F11,
                "Toggle range circles without opening the configuration file.");
            _showWeapon = config.Bind("RangeDisplay", "ShowWeaponRange", true,
                "Show non-melee weapon ranges in red.");
            _showSkill = config.Bind("RangeDisplay", "ShowSkillRange", true,
                "Show finite active-skill ranges in blue.");
            _showEffects = config.Bind("RangeDisplay", "ShowEffectRadius", true,
                "Show effect radii in orange.");
            _showSight = config.Bind("RangeDisplay", "ShowSightRange", false,
                "Show sight radii in grey.");
            _lineWidth = config.Bind("RangeDisplay", "LineWidth", 0.08f,
                "World-space width of each range line.");
            _segments = config.Bind("RangeDisplay", "Segments", DefaultSegments,
                "Circle tessellation segments (8-128).");
            _heightOffset = config.Bind("RangeDisplay", "HeightOffset", 0.15f,
                "Raise circles above the unit's position to reduce z-fighting.");
        }

        /// <summary>Live enabled state, readable by an agent without a visual capture.</summary>
        public bool IsEnabled => _enabled != null && _enabled.Value;

        /// <summary>Currently bound toggle key, readable by an agent without a visual capture.</summary>
        public string ToggleKey => _toggleKey != null ? _toggleKey.Value.ToString() : "F11";

        /// <summary>Number of entities currently showing range circles, readable by an agent.</summary>
        public int TrackedEntityCount => _circles.Count;

        private void Update()
        {
            if (_enabled == null || _toggleKey == null)
            {
                return;
            }

            if (Input.GetKeyDown(_toggleKey.Value))
            {
                _enabled.Value = !_enabled.Value;
                _nextRefreshAt = 0f;
            }

            if (!_enabled.Value)
            {
                if (_circles.Count != 0)
                {
                    ClearCircles();
                }
                return;
            }

            if (Time.unscaledTime < _nextRefreshAt)
            {
                UpdateCirclePositions();
                return;
            }

            _nextRefreshAt = Time.unscaledTime + RefreshInterval;
            try
            {
                RefreshSelection();
            }
            catch (Exception exception)
            {
                ClearCircles();
                _enabled.Value = false;
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    LogWarning("[ExpansionLoader] Range display paused after " +
                        exception.GetType().Name + ": " + exception.Message);
                }
            }
        }

        private void RefreshSelection()
        {
            EntityController[] entities = UnityEngine.Object.FindObjectsOfType<EntityController>();
            var selected = new List<EntityController>();
            var selectedIds = new List<int>();
            for (int i = 0; i < entities.Length; i++)
            {
                EntityController entity = entities[i];
                if (entity != null && entity.IsSelected && entity.IsControlledByPlayer && !entity.IsDestroyed)
                {
                    selected.Add(entity);
                    selectedIds.Add(entity.UniqueEntityId);
                }
            }

            RangeDisplaySelectionDelta<int> delta = _selectionTracker.Update(selectedIds);
            for (int i = 0; i < delta.Removed.Length; i++)
            {
                RemoveEntity(delta.Removed[i]);
            }

            RangeDisplayOptions options = new RangeDisplayOptions(
                _showWeapon.Value,
                _showSkill.Value,
                _showEffects.Value,
                _showSight.Value);
            for (int i = 0; i < selected.Count; i++)
            {
                EntityController entity = selected[i];
                if (entity == null)
                {
                    continue;
                }

                RangeDisplayEntityState state = new RangeDisplayEntityState(
                    entity.IsSelected,
                    entity.IsControlledByPlayer,
                    entity.IsDestroyed,
                    entity.CanAttack,
                    entity.melee,
                    entity.WeaponRange,
                    entity.HasActiveSkill,
                    entity.SkillRangeIsUnlimited,
                    entity.SkillRange,
                    entity.EffectRadius1,
                    entity.EffectRadius2,
                    entity.SightRadius);
                SyncEntity(entity, RangeDisplayPolicy.Resolve(state, options));
            }
        }

        private void SyncEntity(EntityController entity, RangeDisplaySpec[] specs)
        {
            int entityId = entity.UniqueEntityId;
            if (!_circles.TryGetValue(entityId, out EntityCircles entityCircles))
            {
                entityCircles = new EntityCircles(entity.transform);
                _circles.Add(entityId, entityCircles);
            }
            else
            {
                entityCircles.Transform = entity.transform;
            }

            Dictionary<RangeDisplayKind, CircleRenderer> circles = entityCircles.Circles;
            for (int i = 0; i < specs.Length; i++)
            {
                RangeDisplaySpec spec = specs[i];
                if (!circles.TryGetValue(spec.Kind, out CircleRenderer circle) || circle == null ||
                    circle.GameObject == null)
                {
                    circle = CreateCircle(entity, spec.Kind, spec.Radius);
                    if (circle == null)
                    {
                        continue;
                    }
                    circles[spec.Kind] = circle;
                }
                else if (circle.NeedsGeometryUpdate(spec.Radius, _segments.Value, _heightOffset.Value))
                {
                    circle.SetGeometry(spec.Radius, _segments.Value, _heightOffset.Value);
                }

                circle.Renderer.widthMultiplier = Mathf.Clamp(_lineWidth.Value, 0.005f, 1f);
                circle.UpdatePosition(entity.transform.position);
            }

            var keep = new HashSet<RangeDisplayKind>();
            for (int i = 0; i < specs.Length; i++)
            {
                keep.Add(specs[i].Kind);
            }

            var removeKinds = new List<RangeDisplayKind>();
            foreach (KeyValuePair<RangeDisplayKind, CircleRenderer> entry in circles)
            {
                if (!keep.Contains(entry.Key))
                {
                    DestroyCircle(entry.Value);
                    removeKinds.Add(entry.Key);
                }
            }
            for (int i = 0; i < removeKinds.Count; i++)
            {
                circles.Remove(removeKinds[i]);
            }
            if (circles.Count == 0)
            {
                _circles.Remove(entityId);
            }
        }

        private CircleRenderer CreateCircle(EntityController entity, RangeDisplayKind kind, float radius)
        {
            Material material = GetSharedMaterial();
            if (material == null)
            {
                return null;
            }

            var circleObject = new GameObject("RCL_RangeDisplay_" + kind);
            try
            {
                circleObject.transform.SetParent(entity.transform, false);
                var line = circleObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = true;
                line.positionCount = 0;
                line.sharedMaterial = material;
                line.startColor = GetColor(kind);
                line.endColor = GetColor(kind);
                line.widthMultiplier = Mathf.Clamp(_lineWidth.Value, 0.005f, 1f);
                line.enabled = true;

                var circle = new CircleRenderer(circleObject, line, kind, radius);
                circle.SetGeometry(radius, _segments.Value, _heightOffset.Value);
                circle.UpdatePosition(entity.transform.position);
                return circle;
            }
            catch
            {
                if (circleObject != null)
                {
                    UnityEngine.Object.Destroy(circleObject);
                }
                throw;
            }
        }

        private Material GetSharedMaterial()
        {
            if (_sharedMaterial != null)
            {
                return _sharedMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Unlit/Color") ??
                            Shader.Find("Sprites/Default");
            if (shader == null)
            {
                throw new InvalidOperationException("No compatible line shader is available.");
            }

            _sharedMaterial = new Material(shader)
            {
                name = "RCL Range Display (shared)"
            };
            return _sharedMaterial;
        }

        private void UpdateCirclePositions()
        {
            foreach (EntityCircles entityEntry in _circles.Values)
            {
                Transform entityTransform = entityEntry.Transform;
                if (entityTransform == null)
                {
                    continue;
                }
                foreach (CircleRenderer circle in entityEntry.Circles.Values)
                {
                    if (circle != null && circle.GameObject != null)
                    {
                        circle.UpdatePosition(entityTransform.position);
                    }
                }
            }
        }

        private void RemoveEntity(int entityId)
        {
            if (!_circles.TryGetValue(entityId, out EntityCircles entityCircles))
            {
                return;
            }
            foreach (CircleRenderer circle in entityCircles.Circles.Values)
            {
                DestroyCircle(circle);
            }
            _circles.Remove(entityId);
        }

        private void ClearCircles()
        {
            foreach (EntityCircles entityCircles in _circles.Values)
            {
                foreach (CircleRenderer circle in entityCircles.Circles.Values)
                {
                    DestroyCircle(circle);
                }
            }
            _circles.Clear();
            _selectionTracker.Update(Array.Empty<int>());
        }

        private static void DestroyCircle(CircleRenderer circle)
        {
            if (circle != null && circle.GameObject != null)
            {
                UnityEngine.Object.Destroy(circle.GameObject);
            }
        }

        private void OnDisable()
        {
            ClearCircles();
        }

        private void OnDestroy()
        {
            ClearCircles();
            if (_sharedMaterial != null)
            {
                UnityEngine.Object.Destroy(_sharedMaterial);
                _sharedMaterial = null;
            }
        }

        private void LogWarning(string message)
        {
            if (_logWarning == null)
            {
                return;
            }
            try
            {
                _logWarning(message);
            }
            catch
            {
                // A logging failure must not escape into Unity's update loop.
            }
        }

        private static Color GetColor(RangeDisplayKind kind)
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

        private sealed class EntityCircles
        {
            public EntityCircles(Transform entityTransform)
            {
                Transform = entityTransform;
                Circles = new Dictionary<RangeDisplayKind, CircleRenderer>();
            }

            public Transform Transform { get; set; }
            public Dictionary<RangeDisplayKind, CircleRenderer> Circles { get; }
        }

        private sealed class CircleRenderer
        {
            private Vector3[] _localPoints = Array.Empty<Vector3>();
            private Vector3[] _worldPoints = Array.Empty<Vector3>();
            private int _segments;
            private float _heightOffset;

            public CircleRenderer(GameObject gameObject, LineRenderer renderer, RangeDisplayKind kind, float radius)
            {
                GameObject = gameObject;
                Renderer = renderer;
                Kind = kind;
                Radius = radius;
            }

            public GameObject GameObject { get; }
            public LineRenderer Renderer { get; }
            public RangeDisplayKind Kind { get; }
            public float Radius { get; private set; }

            public bool NeedsGeometryUpdate(float radius, int segments, float heightOffset)
            {
                return Math.Abs(Radius - radius) > 0.001f || _segments != Mathf.Clamp(segments, 8, 128) ||
                       Math.Abs(_heightOffset - Mathf.Clamp(heightOffset, 0.01f, 1f)) > 0.001f;
            }

            public void SetGeometry(float radius, int segments, float heightOffset)
            {
                int safeSegments = Mathf.Clamp(segments, 8, 128);
                float safeHeightOffset = Mathf.Clamp(heightOffset, 0.01f, 1f);
                Vector3[] points = RangeDisplayGeometry.CreateCirclePoints(radius, safeSegments, safeHeightOffset);
                ReplaceBuffers(points);
                Radius = radius;
                _segments = safeSegments;
                _heightOffset = safeHeightOffset;
                Renderer.positionCount = _worldPoints.Length;
                Renderer.SetPositions(_worldPoints);
            }

            public void UpdatePosition(Vector3 center)
            {
                for (int i = 0; i < _localPoints.Length; i++)
                {
                    _worldPoints[i] = new Vector3(
                        center.x + _localPoints[i].x,
                        center.y + _localPoints[i].y,
                        center.z + _localPoints[i].z);
                }
                if (Renderer != null && _worldPoints.Length > 0)
                {
                    Renderer.SetPositions(_worldPoints);
                }
            }

            private void ReplaceBuffers(Vector3[] points)
            {
                _localPoints = new Vector3[points.Length];
                Array.Copy(points, _localPoints, points.Length);
                _worldPoints = new Vector3[points.Length];
            }
        }
    }
}
