using System;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>
    /// Runtime-only retarget configuration attached to a loader-created native prefab clone.
    /// RuntimeVisualDirector binds it on a live entity; LateUpdate copies only mapped local pose
    /// deltas after Animator evaluation. Unmapped target transforms remain owned by the Animator.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class NativeRigRetargetProfile : MonoBehaviour
    {
        [SerializeField] private string _visualRootPath = string.Empty;
        [SerializeField] private string[] _sourcePaths = Array.Empty<string>();
        [SerializeField] private string[] _targetPaths = Array.Empty<string>();
        [SerializeField] private float _sourceToTargetPositionScale = 1f;

        private EntityController _entity;
        private BoundMapping[] _bindings = Array.Empty<BoundMapping>();
        private int _configuredMappingCount;
        private bool _isBound;
        private bool _isSuspended;
        private bool _bindingFailureLogged;

        private sealed class BoundMapping
        {
            public Transform Source;
            public Transform Target;
            public Vector3 SourceRestPosition;
            public Quaternion SourceRestRotation;
            public Vector3 TargetRestPosition;
            public Quaternion TargetRestRotation;
        }

        public bool IsBound
        {
            get { return _isBound; }
        }

        public int BoundMappingCount
        {
            get { return _bindings == null ? 0 : _bindings.Length; }
        }

        public int ConfiguredMappingCount
        {
            get { return _configuredMappingCount; }
        }

        public bool IsSuspended
        {
            get { return _isSuspended; }
        }

        public void Configure(
            string[] sourcePaths,
            string[] targetPaths,
            string visualRootPath,
            float sourceToTargetPositionScale)
        {
            int count = sourcePaths == null ? 0 : sourcePaths.Length;
            if (count == 0 || targetPaths == null || targetPaths.Length != count)
            {
                throw new ArgumentException("Native rig retarget requires equally sized, non-empty source and target path arrays.");
            }

            _visualRootPath = visualRootPath ?? string.Empty;
            _sourcePaths = new string[count];
            _targetPaths = new string[count];
            for (int i = 0; i < count; i++)
            {
                _sourcePaths[i] = sourcePaths[i] ?? string.Empty;
                _targetPaths[i] = targetPaths[i] ?? string.Empty;
            }

            _sourceToTargetPositionScale = IsFinitePositive(sourceToTargetPositionScale)
                ? sourceToTargetPositionScale
                : 1f;
            _configuredMappingCount = count;
            _entity = null;
            _bindings = Array.Empty<BoundMapping>();
            _isBound = false;
            _isSuspended = false;
            _bindingFailureLogged = false;
        }

        public bool Bind(EntityController entity)
        {
            if (entity == null || entity.transform == null)
            {
                return FailBinding("the live EntityController root is missing");
            }

            if (_isBound && _entity == entity)
            {
                return true;
            }

            if (_sourcePaths == null || _targetPaths == null || _sourcePaths.Length == 0 ||
                _sourcePaths.Length != _targetPaths.Length)
            {
                return FailBinding("the serialized mapping arrays are empty or have different lengths");
            }

            Transform visualRoot = FindUniqueTransformPath(entity.transform, _visualRootPath);
            if (visualRoot == null)
            {
                return FailBinding("visual root path '" + _visualRootPath + "' did not resolve uniquely");
            }

            var bindings = new BoundMapping[_sourcePaths.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                Transform source = FindUniqueTransformPath(entity.transform, _sourcePaths[i]);
                if (source == null)
                {
                    return FailBinding("source path '" + _sourcePaths[i] + "' did not resolve uniquely");
                }

                Transform target = FindUniqueTransformPath(visualRoot, _targetPaths[i]);
                if (target == null)
                {
                    return FailBinding("target path '" + _targetPaths[i] + "' did not resolve uniquely beneath the visual root");
                }

                bindings[i] = new BoundMapping
                {
                    Source = source,
                    Target = target,
                    SourceRestPosition = source.localPosition,
                    SourceRestRotation = source.localRotation,
                    TargetRestPosition = target.localPosition,
                    TargetRestRotation = target.localRotation
                };
            }

            _entity = entity;
            _bindings = bindings;
            _isBound = true;
            _isSuspended = false;
            _bindingFailureLogged = false;
            return true;
        }

        public void SuspendForDeath()
        {
            _isSuspended = true;
        }

        /// <summary>
        /// Restores the authored source and target bind poses after native pool initialization,
        /// then resumes retargeting for the new logical unit lifetime.
        /// </summary>
        public void ResetForReuse()
        {
            if (!_isBound || _bindings == null)
            {
                _isSuspended = false;
                return;
            }

            for (int i = 0; i < _bindings.Length; i++)
            {
                BoundMapping binding = _bindings[i];
                if (binding == null || binding.Source == null || binding.Target == null)
                {
                    continue;
                }

                binding.Source.localPosition = binding.SourceRestPosition;
                binding.Source.localRotation = binding.SourceRestRotation;
                binding.Target.localPosition = binding.TargetRestPosition;
                binding.Target.localRotation = binding.TargetRestRotation;
            }

            _isSuspended = false;
        }

        private void LateUpdate()
        {
            if (!_isBound || _isSuspended || _entity == null || _bindings == null)
            {
                return;
            }

            if (_entity.IsDestroyed || _entity.DestroyHasBeenStarted)
            {
                _isSuspended = true;
                return;
            }

            for (int i = 0; i < _bindings.Length; i++)
            {
                BoundMapping binding = _bindings[i];
                if (binding == null || binding.Source == null || binding.Target == null)
                {
                    continue;
                }

                binding.Target.localPosition = NativeRigRetargetPoseMath.RetargetLocalPosition(
                    binding.SourceRestPosition,
                    binding.Source.localPosition,
                    binding.TargetRestPosition,
                    _sourceToTargetPositionScale);
                binding.Target.localRotation = NativeRigRetargetPoseMath.RetargetLocalRotation(
                    binding.SourceRestRotation,
                    binding.Source.localRotation,
                    binding.TargetRestRotation);
            }
        }

        private bool FailBinding(string reason)
        {
            _entity = null;
            _bindings = Array.Empty<BoundMapping>();
            _isBound = false;
            _isSuspended = true;
            if (!_bindingFailureLogged)
            {
                _bindingFailureLogged = true;
                Debug.LogWarning("[RogueCommandExpansionLoader] Native rig retarget disabled on '" +
                    name + "': " + reason + ".");
            }

            return false;
        }

        private static Transform FindUniqueTransformPath(Transform root, string path)
        {
            if (root == null || String.IsNullOrWhiteSpace(path) || path.IndexOf('\\') >= 0 ||
                path.StartsWith("/", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal))
            {
                return null;
            }

            string[] segments = path.Split('/');
            Transform current = root;
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                if (String.IsNullOrWhiteSpace(segments[segmentIndex]) ||
                    segments[segmentIndex] == "." || segments[segmentIndex] == "..")
                {
                    return null;
                }

                Transform match = null;
                for (int childIndex = 0; childIndex < current.childCount; childIndex++)
                {
                    Transform child = current.GetChild(childIndex);
                    if (!String.Equals(child.name, segments[segmentIndex], StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (match != null)
                    {
                        return null;
                    }

                    match = child;
                }

                if (match == null)
                {
                    return null;
                }

                current = match;
            }

            return current;
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
