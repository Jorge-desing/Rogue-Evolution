using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Shared bounded live-unit and hierarchy primitives for the standalone visual
    /// censuses. Keeping the traversal here prevents each endpoint from walking the
    /// same Unity hierarchy through several GetComponentsInChildren calls.
    /// </summary>
    internal static class RuntimeVisualCensusSupport
    {
        internal const int MaxHierarchyNodesPerUnit = 4096;
        internal const int MaxRendererComponentsPerUnit = 256;
        internal const int MaxLodGroupsPerUnit = 256;
        internal const int MaxMaterialSlotsPerUnit = 512;
        internal const int MaxMeshInspectionsPerUnit = 32;
        internal const int MaxColorSamplesPerUnit = 4096;
        internal const int MaxDiagnosticNamesPerUnit = 256;

        private static readonly object EntityKeyAccessorGate = new object();
        private static readonly Dictionary<Type, EntityKeyAccessor> EntityKeyAccessors =
            new Dictionary<Type, EntityKeyAccessor>();

        internal static int ClampRowLimit(int maxRows)
        {
            return Math.Max(1, Math.Min(maxRows, 256));
        }

        internal static List<EntityController> CollectLivingUnits(int maxRows, out int omitted)
        {
            int limit = ClampRowLimit(maxRows);
            var units = new List<EntityController>(limit);
            var seen = new HashSet<EntityController>(ReferenceEqualityComparer<EntityController>.Instance);
            omitted = 0;

            ExistingControllers existing = null;
            try { existing = ExistingControllers.Instance; } catch { }
            if (existing == null)
            {
                return units;
            }

            try { AppendLivingUnits(existing.PlayerUnits(), units, seen, limit, ref omitted); } catch { }
            try { AppendLivingUnits(existing.AiUnits(), units, seen, limit, ref omitted); } catch { }
            return units;
        }

        private static void AppendLivingUnits(
            IEnumerable<EntityController> source,
            List<EntityController> units,
            HashSet<EntityController> seen,
            int limit,
            ref int omitted)
        {
            if (source == null) return;

            foreach (EntityController unit in source)
            {
                if (unit == null || !seen.Add(unit)) continue;

                bool living;
                try
                {
                    living = !unit.IsDestroyed && !unit.DestroyHasBeenStarted;
                }
                catch
                {
                    continue;
                }

                if (!living) continue;
                if (units.Count < limit)
                {
                    units.Add(unit);
                }
                else
                {
                    omitted++;
                }
            }
        }

        internal static string ResolveEntityKey(EntityController unit)
        {
            if (unit == null) return string.Empty;

            try
            {
                EntityKeyAccessor accessor = GetEntityKeyAccessor(unit.GetType());
                string value = accessor.Read(unit);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            catch
            {
                // Fall through to the stable Unity object name.
            }

            try { return unit.name ?? string.Empty; } catch { return string.Empty; }
        }

        internal static bool IsLoaderOwned(string entityKey)
        {
            return !string.IsNullOrEmpty(entityKey) && entityKey.IndexOf(':') >= 0;
        }

        internal static VisualHierarchySnapshot CaptureHierarchy(GameObject root)
        {
            return VisualHierarchySnapshot.Capture(root);
        }

        internal static Mesh ResolveMesh(Renderer renderer)
        {
            if (renderer == null) return null;

            try
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) return filter.sharedMesh;
            }
            catch { }

            try
            {
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                return skinned != null ? skinned.sharedMesh : null;
            }
            catch { return null; }
        }

        internal static string ResolveShaderName(Material material)
        {
            try
            {
                return material != null && material.shader != null
                    ? material.shader.name ?? string.Empty
                    : string.Empty;
            }
            catch { return string.Empty; }
        }

        internal static bool HasMaterialProperty(Material material, string propertyName)
        {
            if (material == null || string.IsNullOrEmpty(propertyName)) return false;
            try { return material.HasProperty(propertyName); } catch { return false; }
        }

        internal static Texture GetTexture(Material material, string propertyName)
        {
            if (!HasMaterialProperty(material, propertyName)) return null;
            try { return material.GetTexture(propertyName); } catch { return null; }
        }

        internal static bool AddUniqueBounded(
            List<string> values,
            HashSet<string> seen,
            string value,
            int maximum,
            ref bool truncated)
        {
            if (string.IsNullOrWhiteSpace(value) || seen.Contains(value)) return false;
            if (values.Count >= maximum)
            {
                truncated = true;
                return false;
            }

            seen.Add(value);
            values.Add(value);
            return true;
        }

        private static EntityKeyAccessor GetEntityKeyAccessor(Type type)
        {
            lock (EntityKeyAccessorGate)
            {
                EntityKeyAccessor accessor;
                if (EntityKeyAccessors.TryGetValue(type, out accessor)) return accessor;

                accessor = EntityKeyAccessor.Create(type);
                EntityKeyAccessors[type] = accessor;
                return accessor;
            }
        }

        private sealed class EntityKeyAccessor
        {
            private readonly PropertyInfo _property;
            private readonly FieldInfo _field;

            private EntityKeyAccessor(PropertyInfo property, FieldInfo field)
            {
                _property = property;
                _field = field;
            }

            internal static EntityKeyAccessor Create(Type type)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                PropertyInfo property = type.GetProperty("EntityId", flags) ?? type.GetProperty("entityId", flags);
                FieldInfo field = type.GetField("entityId", flags) ?? type.GetField("EntityId", flags);
                return new EntityKeyAccessor(property, field);
            }

            internal string Read(EntityController unit)
            {
                if (_property != null)
                {
                    string value = _property.GetValue(unit, null) as string;
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }

                return _field != null ? _field.GetValue(unit) as string : string.Empty;
            }
        }
    }

    internal sealed class VisualHierarchySnapshot
    {
        internal readonly List<Transform> Transforms = new List<Transform>();
        internal readonly List<Renderer> Renderers = new List<Renderer>();
        internal readonly List<LODGroup> LodGroups = new List<LODGroup>();
        internal bool HierarchyTruncated { get; private set; }
        internal bool RendererListTruncated { get; private set; }
        internal bool LodListTruncated { get; private set; }

        private VisualHierarchySnapshot()
        {
        }

        internal static VisualHierarchySnapshot Capture(GameObject root)
        {
            var snapshot = new VisualHierarchySnapshot();
            if (root == null) return snapshot;

            var pending = new List<Transform>(64);
            try
            {
                if (root.transform != null) pending.Add(root.transform);
            }
            catch
            {
                return snapshot;
            }

            while (pending.Count > 0)
            {
                Transform current = pending[pending.Count - 1];
                pending.RemoveAt(pending.Count - 1);
                if (current == null) continue;

                if (snapshot.Transforms.Count >= RuntimeVisualCensusSupport.MaxHierarchyNodesPerUnit)
                {
                    snapshot.HierarchyTruncated = true;
                    break;
                }

                snapshot.Transforms.Add(current);
                GameObject node = null;
                try { node = current.gameObject; } catch { }
                if (node != null)
                {
                    AppendComponents(node, snapshot);
                }

                try
                {
                    for (int childIndex = current.childCount - 1; childIndex >= 0; childIndex--)
                    {
                        Transform child = current.GetChild(childIndex);
                        if (child != null) pending.Add(child);
                    }
                }
                catch
                {
                    snapshot.HierarchyTruncated = true;
                }
            }

            return snapshot;
        }

        private static void AppendComponents(GameObject node, VisualHierarchySnapshot snapshot)
        {
            try
            {
                Renderer[] renderers = node.GetComponents<Renderer>();
                if (renderers != null)
                {
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Renderer renderer = renderers[i];
                        if (renderer == null) continue;
                        if (snapshot.Renderers.Count >= RuntimeVisualCensusSupport.MaxRendererComponentsPerUnit)
                        {
                            snapshot.RendererListTruncated = true;
                            break;
                        }
                        snapshot.Renderers.Add(renderer);
                    }
                }
            }
            catch { snapshot.RendererListTruncated = true; }

            try
            {
                LODGroup[] lodGroups = node.GetComponents<LODGroup>();
                if (lodGroups != null)
                {
                    for (int i = 0; i < lodGroups.Length; i++)
                    {
                        LODGroup lod = lodGroups[i];
                        if (lod == null) continue;
                        if (snapshot.LodGroups.Count >= RuntimeVisualCensusSupport.MaxLodGroupsPerUnit)
                        {
                            snapshot.LodListTruncated = true;
                            break;
                        }
                        snapshot.LodGroups.Add(lod);
                    }
                }
            }
            catch { snapshot.LodListTruncated = true; }
        }
    }

    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        internal static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

        public bool Equals(T left, T right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(T value)
        {
            return value == null ? 0 : RuntimeHelpers.GetHashCode(value);
        }
    }
}
