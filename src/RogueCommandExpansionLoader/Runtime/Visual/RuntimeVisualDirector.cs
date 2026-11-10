using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>
    /// Autonomous in-game visual controller dynamically attached to EntityController instances.
    /// Performs zero-configuration socket auto-discovery to animate turrets, continuous radar sweeps,
    /// progressive damage states (<50% smoke, <25% fire and turret jams), factory deployment bay doors,
    /// and weapon recoil slide with muzzle flash feedback.
    /// </summary>
    public class RuntimeVisualDirector : MonoBehaviour
    {
        private EntityController _entity;
        private Transform _socketTurretPivot;
        private Transform _socketTurretPitch;
        private Transform _socketRadarMast;
        private Transform _socketDeployUnit;
        private Transform _socketDamageSmoke;
        private Transform _socketDamageFire;
        private Transform _socketEngineAudio;
        private Transform _socketWeaponAudio;
        private Transform _socketRecoilPrimary;
        private Transform _socketMuzzlePrimary;
        private Animator _animator;
        private RuntimeAnimatorController _cachedAnimatorController;
        private float _nextAnimatorParameterCacheRetryAt;
        private NativeRigRetargetProfile _nativeRigRetargetProfile;
        private readonly Dictionary<string, Transform> _socketHardpoints = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Transform> _attachmentGroups = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, UniqueSocketRoleBinding<Transform>> _socketRoleBindings =
            new Dictionary<string, UniqueSocketRoleBinding<Transform>>(StringComparer.OrdinalIgnoreCase);

        private GameObject _damagedMeshObject;
        private GameObject _intactMeshObject;
        private GameObject _smokeVfxInstance;
        private GameObject _fireVfxInstance;
        private ParticleSystem _authoredDamageSmoke;
        private ParticleSystem _authoredDamageFire;
        private bool _movementAudioBound;
        private bool _weaponAudioBound;
        private float _audioBindingStartedAt;
        private float _nextAudioBindingAttemptAt;

        private Transform _socketAntenna;
        private Transform _socketThrusterVectorL;
        private Transform _socketThrusterVectorR;
        private Quaternion _antennaBaseLocalRot = Quaternion.identity;
        private Quaternion _thrusterVectorBaseLocalRotL = Quaternion.identity;
        private Quaternion _thrusterVectorBaseLocalRotR = Quaternion.identity;
        private Vector2 _antennaWobbleAngle = Vector2.zero;
        private Vector2 _antennaWobbleVelocity = Vector2.zero;
        private Vector3 _lastVelocity = Vector3.zero;
        private Vector3 _lastAcceleration = Vector3.zero;
        private Vector3 _lastPosition = Vector3.zero;
        private bool _hasLastPosition;

        private Vector3 _recoilBaseLocalPos;
        private float _currentRecoilOffset;
        private float _radarRpm = 30f;
        private float _turretTraverseSpeed = 120f;
        private bool _isDamagedStateActive;
        private bool _isCriticalStateActive;
        private bool _isTurretJammed;
        private bool _isStunnedVisualActive;
        private bool _isOnFireVisualActive;
        private bool _isBoostedVisualActive;
        private string _activeTacticalStanceVisual = "Standard";
        private string _activeVisualVariantId;
        private string _activePresetName;
        private Color? _activeColorTint;
        private List<Renderer> _cachedRenderers;
        private readonly Dictionary<Renderer, RendererTintBaseline> _rendererTintBaselines =
            new Dictionary<Renderer, RendererTintBaseline>();
        private readonly List<GameObject> _activeVisualModuleInstances = new List<GameObject>();
        private Vector3 _intactMeshBaseLocalScale = Vector3.one;
        private bool _hasIntactMeshBaseLocalScale;
        private bool? _turretJammedOverride;
        private float? _doorOpenOverride;

        private sealed class RendererTintBaseline
        {
            public Material[] Materials;
            public MaterialPropertyBlock[] Blocks;
        }

        private static MethodInfo _hasActiveStatusEffectMethod;
        private static object _stunStatusEffect;
        private static object _onFireStatusEffect;
        private static object _combatBoostStatusEffect;
        private static object _overclockedStatusEffect;
        private static object _turboStatusEffect;
        [ThreadStatic] private static object[] _singleArgStatusEffect;
        [ThreadStatic] private static object[] _singleArgCandidate;
        private bool _hasMovementAnimationParameter;
        private bool _hasFireAnimationParameter;
        private bool _hasDeathAnimationParameter;
        private bool _deathAnimationTriggered;
        private bool _hasLastAnimationPosition;
        private Vector3 _lastAnimationPosition;
        private float _doorOpenProgress;
        private Vector3 _doorClosedLocalPos;

        private const string MovementAnimationParameter = "RCL_IsMoving";
        private const string FireAnimationParameter = "RCL_Fire";
        private const string DeathAnimationParameter = "RCL_Death";
        private const float AnimatorParameterCacheRetryInterval = 0.5f;

        private static PropertyInfo _healthRatioProp;
        private static PropertyInfo _currentHealthProp;
        private static PropertyInfo _maxHealthProp;
        private static PropertyInfo _isDestroyedProp;
        private static PropertyInfo _destroyHasBeenStartedProp;
        private static PropertyInfo _stillExistsProp;
        private static PropertyInfo _currentAttackTargetProp;
        private static PropertyInfo _currentFollowTargetProp;
        private static PropertyInfo _inProductionCountProp;
        private static PropertyInfo _productionInfoProp;
        private static MethodInfo _isEnemyMethod;

        private static FieldInfo _attackField;
        private static FieldInfo _timeOfLastShotField;
        private static FieldInfo _attackTargetField;
        private static FieldInfo _aimingField;
        private static FieldInfo _singleTargetActionTargetField;
        private static FieldInfo _currentPlayerCommandField;
        private static FieldInfo _lastAiCommandField;
        private static FieldInfo _cmdTargetField;
        private static FieldInfo _prodHasStartedField;
        private static FieldInfo _prodQueuedCountField;
        private static FieldInfo _movementSoundAudioSourceField;
        private static FieldInfo _shotSoundAudioSourceField;

        private float _lastShotTimestamp = -1f;

        public static void AttachIfApplicable(object entityObj)
        {
            if (entityObj == null) return;

            EntityController ec = entityObj as EntityController;
            if (ec == null) return;

            GameObject go = ec.gameObject;
            if (go == null || go.GetComponent<RuntimeVisualDirector>() != null) return;

            // Inspect hierarchy to confirm socket or custom visual presence
            if (HasCompatibleVisualSockets(go.transform))
            {
                RuntimeVisualDirector director = go.AddComponent<RuntimeVisualDirector>();
                director.Initialize(ec);
            }
        }

        public static void OnEntityReused(object entityObj)
        {
            EntityController entity = entityObj as EntityController;
            if (entity == null || entity.gameObject == null)
            {
                return;
            }

            RuntimeVisualDirector director = entity.gameObject.GetComponent<RuntimeVisualDirector>();
            if (director == null)
            {
                AttachIfApplicable(entity);
                director = entity.gameObject.GetComponent<RuntimeVisualDirector>();
            }

            if (director != null)
            {
                director.ResetForReuse(entity);
            }
        }

        private static bool HasCompatibleVisualSockets(Transform root)
        {
            if (root == null) return false;
            bool hasAnimator = false;
            Queue<Transform> queue = new Queue<Transform>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                string name = current.name;
                if (!hasAnimator && current.GetComponent<Animator>() != null)
                {
                    hasAnimator = true;
                }
                string ignoredSocketName;
                if (RogueCommandSocketCatalog.TryResolveCanonicalSocketName(name, out ignoredSocketName) ||
                    name.StartsWith("RCL_Visual_", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("_Damaged", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    queue.Enqueue(current.GetChild(i));
                }
            }

            return hasAnimator && root.name.StartsWith("RCL_", StringComparison.OrdinalIgnoreCase);
        }

        public void Initialize(EntityController entity)
        {
            _entity = entity;
            DiscoverSocketsAndMeshes(transform);
            EnsureCanonicalHardpointGroups();
            CacheAnimatorParameters();
            _nativeRigRetargetProfile = GetComponent<NativeRigRetargetProfile>();
            if (_nativeRigRetargetProfile != null)
            {
                _nativeRigRetargetProfile.Bind(_entity);
            }

            if (_intactMeshObject != null)
            {
                _intactMeshBaseLocalScale = _intactMeshObject.transform.localScale;
                _hasIntactMeshBaseLocalScale = true;
            }

            CacheTintRenderers();

            if (_socketRecoilPrimary != null)
            {
                _recoilBaseLocalPos = _socketRecoilPrimary.localPosition;
            }

            if (_socketDeployUnit != null)
            {
                _doorClosedLocalPos = _socketDeployUnit.localPosition;
            }

            CacheReflectionAccess();
            _audioBindingStartedAt = Time.unscaledTime;
        }

        private void ResetForReuse(EntityController entity)
        {
            _entity = entity;
            RefreshAnimatorParameterCacheIfNeeded();
            _deathAnimationTriggered = false;
            _isMoving = false;
            _lastShotTimestamp = -1f;
            _lastVelocity = Vector3.zero;
            _lastAcceleration = Vector3.zero;
            _hasLastPosition = false;
            _hasLastAnimationPosition = entity != null;
            if (entity != null && entity.transform != null)
            {
                _lastPosition = entity.transform.position;
                _lastAnimationPosition = entity.transform.position;
            }

            if (_animator != null)
            {
                try
                {
                    _animator.Rebind();
                }
                catch
                {
                }

                try
                {
                    if (_hasMovementAnimationParameter)
                    {
                        _animator.SetBool(MovementAnimationParameter, false);
                    }
                    if (_hasFireAnimationParameter)
                    {
                        _animator.ResetTrigger(FireAnimationParameter);
                    }
                    if (_hasDeathAnimationParameter)
                    {
                        _animator.ResetTrigger(DeathAnimationParameter);
                    }
                }
                catch
                {
                }
            }

            if (_nativeRigRetargetProfile != null)
            {
                _nativeRigRetargetProfile.ResetForReuse();
            }
        }

        private void CacheAnimatorParameters()
        {
            _hasMovementAnimationParameter = false;
            _hasFireAnimationParameter = false;
            _hasDeathAnimationParameter = false;

            Animator preferredAnimator = null;
            Transform[] hierarchyTransforms = GetComponentsInChildren<Transform>(true);
            for (int animatorIndex = 0; animatorIndex < hierarchyTransforms.Length; animatorIndex++)
            {
                Transform candidateTransform = hierarchyTransforms[animatorIndex];
                if (candidateTransform == null ||
                    !candidateTransform.name.StartsWith("RCL_Visual_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Animator candidateAnimator = candidateTransform.GetComponentInChildren<Animator>(true);
                if (candidateAnimator != null && candidateAnimator.runtimeAnimatorController != null)
                {
                    preferredAnimator = candidateAnimator;
                    break;
                }
            }

            _animator = preferredAnimator != null ? preferredAnimator : GetComponentInChildren<Animator>(true);
            RuntimeAnimatorController controller = _animator != null ? _animator.runtimeAnimatorController : null;
            if (controller == null)
            {
                _cachedAnimatorController = null;
                _nextAnimatorParameterCacheRetryAt = Time.unscaledTime + AnimatorParameterCacheRetryInterval;
                return;
            }

            _cachedAnimatorController = controller;
            _nextAnimatorParameterCacheRetryAt = 0f;

            foreach (AnimatorControllerParameter parameter in _animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Bool &&
                    string.Equals(parameter.name, MovementAnimationParameter, StringComparison.Ordinal))
                {
                    _hasMovementAnimationParameter = true;
                }
                else if (parameter.type == AnimatorControllerParameterType.Trigger &&
                    string.Equals(parameter.name, FireAnimationParameter, StringComparison.Ordinal))
                {
                    _hasFireAnimationParameter = true;
                }
                else if (parameter.type == AnimatorControllerParameterType.Trigger &&
                    string.Equals(parameter.name, DeathAnimationParameter, StringComparison.Ordinal))
                {
                    _hasDeathAnimationParameter = true;
                }
            }

            if (!_hasMovementAnimationParameter || !_hasFireAnimationParameter || !_hasDeathAnimationParameter)
            {
                _nextAnimatorParameterCacheRetryAt = Time.unscaledTime + AnimatorParameterCacheRetryInterval;
            }

            if (_entity != null)
            {
                _lastAnimationPosition = _entity.transform.position;
                _hasLastAnimationPosition = true;
            }
        }

        private void RefreshAnimatorParameterCacheIfNeeded()
        {
            RuntimeAnimatorController controller = _animator != null ? _animator.runtimeAnimatorController : null;
            if (controller != null)
            {
                if (!ReferenceEquals(controller, _cachedAnimatorController))
                {
                    CacheAnimatorParameters();
                    return;
                }
            }

            bool missingExpectedParameter = !_hasMovementAnimationParameter ||
                !_hasFireAnimationParameter ||
                !_hasDeathAnimationParameter;
            if ((controller == null || missingExpectedParameter) &&
                Time.unscaledTime >= _nextAnimatorParameterCacheRetryAt)
            {
                CacheAnimatorParameters();
            }
        }

        private void DiscoverSocketsAndMeshes(Transform root)
        {
            Queue<Transform> queue = new Queue<Transform>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                string name = current.name;

                string socketName;
                bool isKnownSocket = RogueCommandSocketCatalog.TryResolveCanonicalSocketName(name, out socketName);
                if (isKnownSocket &&
                    (string.Equals(socketName, "Socket_TurretPivot", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(socketName, "Socket_TurretYaw", StringComparison.OrdinalIgnoreCase)))
                {
                    AddSocketRoleCandidate("turret_azimuth", current);
                }
                else if (isKnownSocket && string.Equals(socketName, "Socket_TurretPitch", StringComparison.OrdinalIgnoreCase))
                {
                    AddSocketRoleCandidate("turret_pitch", current);
                }
                else if (isKnownSocket &&
                    (string.Equals(socketName, "Socket_RadarMast", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(socketName, "Socket_RadarDish", StringComparison.OrdinalIgnoreCase)))
                {
                    AddSocketRoleCandidate("radar_rotation", current);
                }
                else if (isKnownSocket &&
                    (string.Equals(socketName, "Socket_Deploy_Unit", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(socketName, "Socket_DeployUnit", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(socketName, "Socket_DeployRamp", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(socketName, "Socket_Deploy_Ramp", StringComparison.OrdinalIgnoreCase)))
                {
                    AddSocketRoleCandidate("deployment", current);
                }
                else if (isKnownSocket && string.Equals(socketName, "Socket_DamageSmoke", StringComparison.OrdinalIgnoreCase))
                {
                    AddSocketRoleCandidate("damage_smoke", current);
                }
                else if (isKnownSocket && string.Equals(socketName, "Socket_DamageFire", StringComparison.OrdinalIgnoreCase))
                {
                    AddSocketRoleCandidate("damage_fire", current);
                }
                else if (isKnownSocket && string.Equals(socketName, "Socket_EngineAudio", StringComparison.OrdinalIgnoreCase))
                {
                    AddSocketRoleCandidate("engine_audio", current);
                }
                else if (isKnownSocket && string.Equals(socketName, "Socket_WeaponAudio", StringComparison.OrdinalIgnoreCase))
                {
                    AddSocketRoleCandidate("weapon_audio", current);
                }
                else if (isKnownSocket && string.Equals(socketName, "Socket_RecoilPrimary", StringComparison.OrdinalIgnoreCase))
                {
                    AddSocketRoleCandidate("weapon_recoil", current);
                }
                else if (isKnownSocket && string.Equals(socketName, "Socket_MuzzlePrimary", StringComparison.OrdinalIgnoreCase))
                {
                    AddSocketRoleCandidate("weapon_muzzle", current);
                }
                else if (isKnownSocket && string.Equals(socketName, "Socket_Antenna", StringComparison.OrdinalIgnoreCase))
                {
                    AddSocketRoleCandidate("antenna", current);
                }
                else if (isKnownSocket && string.Equals(socketName, "Socket_ThrusterVector_L", StringComparison.OrdinalIgnoreCase))
                {
                    AddSocketRoleCandidate("thruster_left", current);
                }
                else if (isKnownSocket && string.Equals(socketName, "Socket_ThrusterVector_R", StringComparison.OrdinalIgnoreCase))
                {
                    AddSocketRoleCandidate("thruster_right", current);
                }
                else if (name.StartsWith("RCL_AttachmentGroup_", StringComparison.OrdinalIgnoreCase))
                {
                    string attachmentType = name.Substring("RCL_AttachmentGroup_".Length);
                    AddSocketRoleCandidate("attachment_group:" + NormalizeHardpointType(attachmentType), current);
                }
                else if (isKnownSocket && socketName.StartsWith("Socket_Hardpoint_", StringComparison.OrdinalIgnoreCase))
                {
                    string hpType = socketName.Substring("Socket_Hardpoint_".Length);
                    AddSocketRoleCandidate("hardpoint:" + NormalizeHardpointType(hpType), current);
                }
                else if (name.EndsWith("_Damaged", StringComparison.OrdinalIgnoreCase))
                {
                    _damagedMeshObject = current.gameObject;
                    _damagedMeshObject.SetActive(false); // Initially intact
                }
                else if (_intactMeshObject == null && (name.IndexOf("Model", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Visual", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Hull", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _intactMeshObject = current.gameObject;
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    queue.Enqueue(current.GetChild(i));
                }
            }

            _socketTurretPivot = GetSocketRoleValue("turret_azimuth");
            _socketTurretPitch = GetSocketRoleValue("turret_pitch");
            _socketRadarMast = GetSocketRoleValue("radar_rotation");
            _socketDeployUnit = GetSocketRoleValue("deployment");
            _socketDamageSmoke = GetSocketRoleValue("damage_smoke");
            _socketDamageFire = GetSocketRoleValue("damage_fire");
            _socketEngineAudio = GetSocketRoleValue("engine_audio");
            _socketWeaponAudio = GetSocketRoleValue("weapon_audio");
            _socketRecoilPrimary = GetSocketRoleValue("weapon_recoil");
            _socketMuzzlePrimary = GetSocketRoleValue("weapon_muzzle");
            _socketAntenna = GetSocketRoleValue("antenna");
            _socketThrusterVectorL = GetSocketRoleValue("thruster_left");
            _socketThrusterVectorR = GetSocketRoleValue("thruster_right");
            _authoredDamageSmoke = GetUniqueParticleSystemUnder(_socketDamageSmoke);
            _authoredDamageFire = GetUniqueParticleSystemUnder(_socketDamageFire);

            if (_socketAntenna != null)
            {
                _antennaBaseLocalRot = _socketAntenna.localRotation;
            }
            if (_socketThrusterVectorL != null)
            {
                _thrusterVectorBaseLocalRotL = _socketThrusterVectorL.localRotation;
            }
            if (_socketThrusterVectorR != null)
            {
                _thrusterVectorBaseLocalRotR = _socketThrusterVectorR.localRotation;
            }

            foreach (KeyValuePair<string, UniqueSocketRoleBinding<Transform>> role in _socketRoleBindings)
            {
                if (role.Value.Value == null)
                {
                    continue;
                }

                const string hardpointPrefix = "hardpoint:";
                if (role.Key.StartsWith(hardpointPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    _socketHardpoints[role.Key.Substring(hardpointPrefix.Length)] = role.Value.Value;
                    continue;
                }

                const string attachmentGroupPrefix = "attachment_group:";
                if (role.Key.StartsWith(attachmentGroupPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    _attachmentGroups[role.Key.Substring(attachmentGroupPrefix.Length)] = role.Value.Value;
                }
            }

            if (_intactMeshObject == null && transform != null)
            {
                foreach (Transform t in transform.GetComponentsInChildren<Transform>(true))
                {
                    if (t != transform &&
                        t.GetComponent<Renderer>() != null &&
                        !t.name.StartsWith("Socket_", StringComparison.OrdinalIgnoreCase) &&
                        !t.name.EndsWith("_Damaged", StringComparison.OrdinalIgnoreCase))
                    {
                        _intactMeshObject = t.gameObject;
                        break;
                    }
                }
            }
        }

        private void AddSocketRoleCandidate(string role, Transform candidate)
        {
            UniqueSocketRoleBinding<Transform> binding;
            if (!_socketRoleBindings.TryGetValue(role, out binding))
            {
                binding = new UniqueSocketRoleBinding<Transform>();
                _socketRoleBindings.Add(role, binding);
            }

            binding.Add(candidate);
        }

        private void EnsureCanonicalHardpointGroups()
        {
            string[] hardpointTypes = { "ERA", "APS", "Sponsons", "FuelDrums" };
            for (int i = 0; i < hardpointTypes.Length; i++)
            {
                string type = hardpointTypes[i];
                UniqueSocketRoleBinding<Transform> socketRole;
                if (!_socketRoleBindings.TryGetValue("hardpoint:" + type, out socketRole) ||
                    socketRole == null || socketRole.IsAmbiguous || socketRole.Value == null)
                {
                    continue;
                }

                UniqueSocketRoleBinding<Transform> groupRole;
                _socketRoleBindings.TryGetValue("attachment_group:" + type, out groupRole);
                if (groupRole != null && groupRole.IsAmbiguous) continue;

                Transform group = groupRole != null ? groupRole.Value : null;
                if (group == null)
                {
                    GameObject groupObject = new GameObject("RCL_AttachmentGroup_" + type);
                    group = groupObject.transform;
                    group.SetParent(socketRole.Value, false);
                    group.localPosition = Vector3.zero;
                    group.localRotation = Quaternion.identity;
                    group.localScale = Vector3.one;
                    AddSocketRoleCandidate("attachment_group:" + type, group);
                }
                else if (group.parent != socketRole.Value)
                {
                    ReparentAttachmentGroupPreservingChildren(group, socketRole.Value);
                }

                _attachmentGroups[type] = group;
            }
        }

        private void ReparentAttachmentGroupPreservingChildren(Transform group, Transform socket)
        {
            if (group == null || socket == null) return;
            var children = new List<Transform>();
            while (group.childCount > 0)
            {
                Transform child = group.GetChild(0);
                children.Add(child);
                child.SetParent(transform, true);
            }

            group.SetParent(socket, false);
            group.localPosition = Vector3.zero;
            group.localRotation = Quaternion.identity;
            group.localScale = Vector3.one;
            for (int i = 0; i < children.Count; i++)
            {
                Transform child = children[i];
                if (child != null) child.SetParent(group, true);
            }
        }

        private Transform GetSocketRoleValue(string role)
        {
            UniqueSocketRoleBinding<Transform> binding;
            return _socketRoleBindings.TryGetValue(role, out binding) ? binding.Value : null;
        }

        private static ParticleSystem GetUniqueParticleSystemUnder(Transform socket)
        {
            if (socket == null) return null;
            ParticleSystem[] systems = socket.GetComponentsInChildren<ParticleSystem>(true);
            return systems != null && systems.Length == 1 ? systems[0] : null;
        }

        private void UpdateNativeAudioBindings()
        {
            if (_movementAudioBound && _weaponAudioBound) return;
            if (Time.unscaledTime - _audioBindingStartedAt > 10.0f ||
                Time.unscaledTime < _nextAudioBindingAttemptAt)
            {
                return;
            }

            _nextAudioBindingAttemptAt = Time.unscaledTime + 0.5f;
            TryBindNativeAudioSources();
        }

        private void TryBindNativeAudioSources()
        {
            if (_entity == null) return;

            if (!_movementAudioBound && _movementSoundAudioSourceField != null && _socketEngineAudio != null)
            {
                AudioSource source = GetUniqueAudioSourceUnder(_socketEngineAudio);
                if (source != null)
                {
                    Component target = FindNearestComponent(
                        transform.GetComponentsInChildren(_movementSoundAudioSourceField.DeclaringType, true),
                        _socketEngineAudio.position);
                    _movementAudioBound = TryAssignAudioSource(
                        target, _movementSoundAudioSourceField, source);
                }
            }

            if (!_weaponAudioBound && _shotSoundAudioSourceField != null && _socketWeaponAudio != null)
            {
                AudioSource source = GetUniqueAudioSourceUnder(_socketWeaponAudio);
                if (source != null)
                {
                    Component target = FindNearestComponent(
                        transform.GetComponentsInChildren(_shotSoundAudioSourceField.DeclaringType, true),
                        _socketWeaponAudio.position);
                    _weaponAudioBound = TryAssignAudioSource(
                        target, _shotSoundAudioSourceField, source);
                }
            }
        }

        private static AudioSource GetUniqueAudioSourceUnder(Transform socket)
        {
            if (socket == null) return null;
            AudioSource[] sources = socket.GetComponentsInChildren<AudioSource>(true);
            return sources != null && sources.Length == 1 ? sources[0] : null;
        }

        private static Component FindNearestComponent(Component[] candidates, Vector3 point)
        {
            if (candidates == null || candidates.Length == 0) return null;

            Component nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < candidates.Length; i++)
            {
                Component candidate = candidates[i];
                if (candidate == null || candidate.transform == null) continue;
                float distance = (candidate.transform.position - point).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private static bool TryAssignAudioSource(Component target, FieldInfo field, AudioSource source)
        {
            if (target == null || field == null || source == null || field.FieldType != typeof(AudioSource))
            {
                return false;
            }

            try
            {
                field.SetValue(target, source);
                return ReferenceEquals(field.GetValue(target), source);
            }
            catch
            {
                return false;
            }
        }

        private static void CacheReflectionAccess()
        {
            if (_healthRatioProp != null) return;
            Type ecType = typeof(EntityController);
            _healthRatioProp = ecType.GetProperty("HealthRatio", BindingFlags.Public | BindingFlags.Instance);
            _currentHealthProp = ecType.GetProperty("CurrentHealth", BindingFlags.Public | BindingFlags.Instance);
            _maxHealthProp = ecType.GetProperty("MaxHealth", BindingFlags.Public | BindingFlags.Instance);
            _isDestroyedProp = ecType.GetProperty("IsDestroyed", BindingFlags.Public | BindingFlags.Instance);
            _destroyHasBeenStartedProp = ecType.GetProperty("DestroyHasBeenStarted", BindingFlags.Public | BindingFlags.Instance);
            _stillExistsProp = ecType.GetProperty("StillExists", BindingFlags.Public | BindingFlags.Instance);
            _currentAttackTargetProp = ecType.GetProperty("CurrentAttackTarget", BindingFlags.Public | BindingFlags.Instance);
            _currentFollowTargetProp = ecType.GetProperty("CurrentFollowTarget", BindingFlags.Public | BindingFlags.Instance);
            _inProductionCountProp = ecType.GetProperty("InProductionCount", BindingFlags.Public | BindingFlags.Instance);
            _productionInfoProp = ecType.GetProperty("ProductionInfo", BindingFlags.Public | BindingFlags.Instance);
            _isEnemyMethod = ecType.GetMethod("IsEnemy", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { ecType }, null);

            Type statusEffectType = ecType.Assembly.GetType("StatusEffect", false);
            if (statusEffectType != null)
            {
                _hasActiveStatusEffectMethod = ecType.GetMethod("HasActiveStatusEffect",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { statusEffectType }, null);
                try { _stunStatusEffect = Enum.Parse(statusEffectType, "Stun", true); } catch { }
                try { _onFireStatusEffect = Enum.Parse(statusEffectType, "OnFire", true); } catch { }
                try { _combatBoostStatusEffect = Enum.Parse(statusEffectType, "CombatBoost", true); } catch { }
                try { _overclockedStatusEffect = Enum.Parse(statusEffectType, "Overclocked", true); } catch { }
                try { _turboStatusEffect = Enum.Parse(statusEffectType, "Turbo", true); } catch { }
            }

            Type movementType = ecType.Assembly.GetType("EntityFlockingMovement", false);
            if (movementType != null)
            {
                FieldInfo movementAudioField = movementType.GetField(
                    "_movementSoundAudioSource", BindingFlags.NonPublic | BindingFlags.Instance);
                if (movementAudioField != null && movementAudioField.FieldType == typeof(AudioSource))
                {
                    _movementSoundAudioSourceField = movementAudioField;
                }
            }

            Type projectileType = ecType.Assembly.GetType("ShootProjectile", false);
            if (projectileType != null)
            {
                FieldInfo shotAudioField = projectileType.GetField(
                    "shotSoundAudioSource", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (shotAudioField != null && shotAudioField.FieldType == typeof(AudioSource))
                {
                    _shotSoundAudioSourceField = shotAudioField;
                }
            }

            _attackField = ecType.GetField("_attack", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_attackField != null)
            {
                Type attackType = _attackField.FieldType;
                _timeOfLastShotField = attackType.GetField("_timeOfLastShot", BindingFlags.NonPublic | BindingFlags.Instance);
                _attackTargetField = attackType.GetField("_attackTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                _aimingField = attackType.GetField("_aiming", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            Type staType = ecType.Assembly.GetType("SingleTargetAction");
            if (staType != null)
            {
                _singleTargetActionTargetField = staType.GetField("target", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }

            _currentPlayerCommandField = ecType.GetField("_currentPlayerCommand", BindingFlags.NonPublic | BindingFlags.Instance);
            _lastAiCommandField = ecType.GetField("_lastAiCommand", BindingFlags.NonPublic | BindingFlags.Instance);
            Type cmdType = ecType.Assembly.GetType("EntityCommand");
            if (cmdType != null)
            {
                _cmdTargetField = cmdType.GetField("target", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }

            Type prodType = ecType.Assembly.GetType("ProductionInfo");
            if (prodType != null)
            {
                _prodHasStartedField = prodType.GetField("productionHasStarted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _prodQueuedCountField = prodType.GetField("queuedCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
        }

        private void Update()
        {
            if (_entity == null) return;

            RefreshAnimatorParameterCacheIfNeeded();
            bool isDead = IsEntityDead();
            UpdateHealthAndDamageTransitions(isDead);
            if (isDead)
            {
                if (_nativeRigRetargetProfile != null)
                {
                    _nativeRigRetargetProfile.SuspendForDeath();
                }
                return;
            }

            UpdateNativeAudioBindings();
            UpdateMotionSample(Time.deltaTime);
            UpdateLocomotionAnimation();
            UpdateStatusEffectAndStanceReactions();
            UpdateRadarSweep();
            UpdateTurretAiming();
            UpdateFactoryDeployAnimation();
            UpdateAutomaticWeaponFeedback();
            UpdateRecoilRecovery();
            UpdateAntennaWobble(Time.deltaTime);
            UpdateVtolThrusterVectoring(Time.deltaTime);
        }

        private bool _isMoving;

        private void UpdateLocomotionAnimation()
        {
            if (_animator == null || !_hasMovementAnimationParameter || _entity == null) return;

            Vector3 currentPosition = _entity.transform.position;
            float deltaTime = Time.deltaTime;
            float positionDeltaSquared = _hasLastAnimationPosition
                ? (currentPosition - _lastAnimationPosition).sqrMagnitude
                : 0f;
            float velocitySquared = _hasLastPosition ? _lastVelocity.sqrMagnitude : 0f;
            bool isMoving = LocomotionAnimationPolicy.IsMoving(
                positionDeltaSquared,
                deltaTime,
                velocitySquared);

            _isMoving = isMoving;
            _animator.SetBool(MovementAnimationParameter, isMoving);
            _lastAnimationPosition = currentPosition;
            _hasLastAnimationPosition = true;
        }

        private void UpdateAutomaticWeaponFeedback()
        {
            if (_attackField == null || _timeOfLastShotField == null) return;

            try
            {
                object attackObj = _attackField.GetValue(_entity);
                if (attackObj != null)
                {
                    float shotTime = (float)_timeOfLastShotField.GetValue(attackObj);
                    if (_lastShotTimestamp < 0f)
                    {
                        _lastShotTimestamp = shotTime;
                    }
                    else if (shotTime > _lastShotTimestamp)
                    {
                        _lastShotTimestamp = shotTime;
                        TriggerWeaponFeedback();
                    }
                }
            }
            catch { }
        }

        private MaterialPropertyBlock _propertyBlock;

        private void UpdateHealthAndDamageTransitions()
        {
            UpdateHealthAndDamageTransitions(IsEntityDead());
        }

        private void UpdateHealthAndDamageTransitions(bool isDead)
        {
            float ratio = GetHealthRatio();

            // 1. Reactive Armor (ERA) Detonation on initial heavy impact (<= 75% HP)
            if (ratio <= 0.75f && !isDead)
            {
                Transform eraVisual;
                if (TryGetHardpointVisual("ERA", out eraVisual) && eraVisual.gameObject.activeSelf)
                {
                    Transform effectAnchor = GetHardpointSocket("ERA") ?? transform;
                    Vector3 effectPosition = effectAnchor.position;
                    Quaternion effectRotation = effectAnchor.rotation;
                    bool hidesEffectAnchor = eraVisual == effectAnchor;
                    eraVisual.gameObject.SetActive(false);
                    if (hidesEffectAnchor)
                    {
                        RuntimeVisualVfxFallback.TriggerMuzzleFlashAt(transform, effectPosition, effectRotation);
                    }
                    else
                    {
                        RuntimeVisualVfxFallback.TriggerMuzzleFlash(effectAnchor, this);
                    }
                }
            }
            else if (ratio > 0.85f && !isDead)
            {
                Transform eraSocket;
                if (TryGetHardpointVisual("ERA", out eraSocket) && !eraSocket.gameObject.activeSelf)
                {
                    eraSocket.gameObject.SetActive(true);
                }
            }

            // 2. Moderate Damage (<= 50% HP): Smoke particle spawner & Damaged mesh transition
            if (ratio <= 0.50f && !isDead)
            {
                if (!_isDamagedStateActive)
                {
                    _isDamagedStateActive = true;
                    if (_authoredDamageSmoke != null)
                    {
                        _authoredDamageSmoke.Play(true);
                    }
                    else
                    {
                        Transform smokeMount = _socketDamageSmoke ?? transform;
                        _smokeVfxInstance = RuntimeVisualVfxFallback.SpawnDamageSmoke(smokeMount);
                    }

                    if (_damagedMeshObject != null)
                    {
                        _damagedMeshObject.SetActive(true);
                        if (_intactMeshObject != null)
                        {
                            _intactMeshObject.SetActive(false);
                        }
                    }
                    else
                    {
                        ApplyProceduralScorch(true);
                    }

                    // Auxiliary Fuel Drums jettison / smoke on heavy damage
                    Transform fuelVisual;
                    if (TryGetHardpointVisual("FuelDrums", out fuelVisual) && fuelVisual.gameObject.activeSelf)
                    {
                        Transform effectAnchor = GetHardpointSocket("FuelDrums") ?? transform;
                        Vector3 effectPosition = effectAnchor.position;
                        Quaternion effectRotation = effectAnchor.rotation;
                        bool hidesEffectAnchor = fuelVisual == effectAnchor;
                        fuelVisual.gameObject.SetActive(false);
                        if (hidesEffectAnchor)
                        {
                            RuntimeVisualVfxFallback.SpawnDamageSmokeAt(transform, effectPosition, effectRotation);
                        }
                        else
                        {
                            RuntimeVisualVfxFallback.SpawnDamageSmoke(effectAnchor);
                        }
                    }
                }
            }
            else if (ratio > 0.50f && !isDead && _isDamagedStateActive)
            {
                // Health recovered from moderate damage via field repairs
                _isDamagedStateActive = false;
                if (_authoredDamageSmoke != null)
                {
                    _authoredDamageSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                if (_smokeVfxInstance != null)
                {
                    UnityEngine.Object.Destroy(_smokeVfxInstance);
                    _smokeVfxInstance = null;
                }
                if (_damagedMeshObject != null)
                {
                    _damagedMeshObject.SetActive(false);
                    if (_intactMeshObject != null)
                    {
                        _intactMeshObject.SetActive(true);
                    }
                }
                else
                {
                    ApplyProceduralScorch(false);
                }

                Transform fuelSocket;
                if (TryGetHardpointVisual("FuelDrums", out fuelSocket) && !fuelSocket.gameObject.activeSelf)
                {
                    fuelSocket.gameObject.SetActive(true);
                }
            }

            // 3. Critical Damage (<= 25% HP): Fire particles, APS burnout, and jammed turret tilt
            if (ratio <= 0.25f && !isDead)
            {
                if (!_isCriticalStateActive)
                {
                    _isCriticalStateActive = true;
                    if (_authoredDamageFire != null)
                    {
                        _authoredDamageFire.Play(true);
                    }
                    else
                    {
                        Transform fireMount = _socketDamageFire ?? transform;
                        _fireVfxInstance = RuntimeVisualVfxFallback.SpawnDamageFire(fireMount, _entity);
                    }

                    if (_socketTurretPivot != null && !_isTurretJammed)
                    {
                        _isTurretJammed = true;
                        _socketTurretPivot.localRotation *= Quaternion.Euler(4.5f, 0f, 3.8f);
                    }

                    // APS radar burnout
                    Transform apsSocket;
                    if (TryGetHardpointVisual("APS", out apsSocket))
                    {
                        apsSocket.gameObject.SetActive(false);
                    }
                }
            }
            else if (ratio > 0.25f && !isDead && _isCriticalStateActive)
            {
                // Health recovered from critical damage
                _isCriticalStateActive = false;
                if (_authoredDamageFire != null)
                {
                    _authoredDamageFire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                if (_fireVfxInstance != null)
                {
                    UnityEngine.Object.Destroy(_fireVfxInstance);
                    _fireVfxInstance = null;
                }
                if (_socketTurretPivot != null && _isTurretJammed)
                {
                    _isTurretJammed = false;
                    _socketTurretPivot.localRotation = Quaternion.identity;
                }

                Transform apsSocket;
                if (TryGetHardpointVisual("APS", out apsSocket) && !apsSocket.gameObject.activeSelf)
                {
                    apsSocket.gameObject.SetActive(true);
                }
            }

            // 4. Destruction cleanup
            if (isDead)
            {
                if (_authoredDamageFire != null)
                {
                    _authoredDamageFire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
                if (_authoredDamageSmoke != null)
                {
                    _authoredDamageSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }

                if (!_deathAnimationTriggered && _animator != null && _hasDeathAnimationParameter)
                {
                    _deathAnimationTriggered = true;
                    if (_hasMovementAnimationParameter)
                    {
                        _animator.SetBool(MovementAnimationParameter, false);
                    }
                    _animator.SetTrigger(DeathAnimationParameter);
                }

                if (_fireVfxInstance != null)
                {
                    UnityEngine.Object.Destroy(_fireVfxInstance, 4.0f);
                    _fireVfxInstance = null;
                }
                if (_smokeVfxInstance != null)
                {
                    UnityEngine.Object.Destroy(_smokeVfxInstance, 8.0f);
                    _smokeVfxInstance = null;
                }
            }
        }

        private void ApplyProceduralScorch(bool active)
        {
            if (transform == null) return;
            if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();

            Renderer[] renderers = transform.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (r.name.StartsWith("Socket_", StringComparison.OrdinalIgnoreCase)) continue;
                if (active)
                {
                    r.GetPropertyBlock(_propertyBlock);
                    _propertyBlock.SetColor("_Color", new Color(0.38f, 0.35f, 0.33f));
                    r.SetPropertyBlock(_propertyBlock);
                }
                else
                {
                    r.SetPropertyBlock(null);
                }
            }
        }

        private void UpdateStatusEffectAndStanceReactions()
        {
            if (_entity == null || IsEntityDead()) return;

            bool hasStun = false;
            bool hasOnFire = false;
            bool hasBoost = false;

            try
            {
                if (_entity.HasActiveStatusEffect(StatusEffect.Stun)) hasStun = true;
                if (_entity.HasActiveStatusEffect(StatusEffect.OnFire)) hasOnFire = true;
                if (_entity.HasActiveStatusEffect(StatusEffect.CombatBoost)) hasBoost = true;
                if (!hasBoost && _entity.HasActiveStatusEffect(StatusEffect.Overclocked)) hasBoost = true;
                if (!hasBoost && _entity.HasActiveStatusEffect(StatusEffect.Turbo)) hasBoost = true;
            }
            catch
            {
                if (_hasActiveStatusEffectMethod != null)
                {
                    try
                    {
                        if (_singleArgStatusEffect == null) _singleArgStatusEffect = new object[1];
                        if (_stunStatusEffect != null)
                        {
                            _singleArgStatusEffect[0] = _stunStatusEffect;
                            if ((bool)_hasActiveStatusEffectMethod.Invoke(_entity, _singleArgStatusEffect)) hasStun = true;
                        }
                        if (_onFireStatusEffect != null)
                        {
                            _singleArgStatusEffect[0] = _onFireStatusEffect;
                            if ((bool)_hasActiveStatusEffectMethod.Invoke(_entity, _singleArgStatusEffect)) hasOnFire = true;
                        }
                        if (_combatBoostStatusEffect != null)
                        {
                            _singleArgStatusEffect[0] = _combatBoostStatusEffect;
                            if ((bool)_hasActiveStatusEffectMethod.Invoke(_entity, _singleArgStatusEffect)) hasBoost = true;
                        }
                        if (!hasBoost && _overclockedStatusEffect != null)
                        {
                            _singleArgStatusEffect[0] = _overclockedStatusEffect;
                            if ((bool)_hasActiveStatusEffectMethod.Invoke(_entity, _singleArgStatusEffect)) hasBoost = true;
                        }
                        if (!hasBoost && _turboStatusEffect != null)
                        {
                            _singleArgStatusEffect[0] = _turboStatusEffect;
                            if ((bool)_hasActiveStatusEffectMethod.Invoke(_entity, _singleArgStatusEffect)) hasBoost = true;
                        }
                        _singleArgStatusEffect[0] = null;
                    }
                    catch
                    {
                        if (_singleArgStatusEffect != null) _singleArgStatusEffect[0] = null;
                    }
                }
            }

            _isStunnedVisualActive = hasStun;
            _isOnFireVisualActive = hasOnFire;
            _isBoostedVisualActive = hasBoost;

            if (hasStun)
            {
                _isTurretJammed = true;
                if (_animator != null && _hasMovementAnimationParameter)
                {
                    _animator.SetBool(MovementAnimationParameter, false);
                }
            }

            if (hasOnFire && !_isCriticalStateActive)
            {
                if (_authoredDamageFire != null && !_authoredDamageFire.isPlaying)
                {
                    _authoredDamageFire.Play(true);
                }
                else if (_fireVfxInstance == null)
                {
                    Transform fireMount = _socketDamageFire ?? transform;
                    _fireVfxInstance = RuntimeVisualVfxFallback.SpawnDamageFire(fireMount, _entity);
                }
                ApplyProceduralScorch(true);
            }

            try
            {
                string key = _entity.UniqueEntityId.ToString();
                string stance = RuntimeGameplayCommandDirector.GetUnitTacticalStance(key);
                if (!string.IsNullOrEmpty(stance))
                {
                    SetTacticalStanceVisual(stance);
                }
            }
            catch { }
        }

        private void UpdateRadarSweep()
        {
            if (_socketRadarMast == null || IsEntityDead() || _isStunnedVisualActive) return;
            float effectiveRpm = _isBoostedVisualActive ? _radarRpm * 2.5f : _radarRpm;
            float degPerSec = effectiveRpm * 6.0f;
            _socketRadarMast.Rotate(Vector3.up, degPerSec * Time.deltaTime, Space.Self);
        }

        private void UpdateTurretAiming()
        {
            if (_isMoving) return;
            if (string.Equals(_activeTacticalStanceVisual, "HoldFire", StringComparison.OrdinalIgnoreCase)) return;
            bool effectiveJammed = _turretJammedOverride.HasValue ? _turretJammedOverride.Value : (_isTurretJammed || _isStunnedVisualActive);
            if (_socketTurretPivot == null || effectiveJammed || IsEntityDead()) return;

            Transform target = ResolveEntityTarget();
            if (target == null) return;

            Vector3 toTarget = target.position - _socketTurretPivot.position;
            toTarget.y = 0f; // Horizontal planar azimuth

            // Turrets only track hostile targets inside the native weapon range.
            // ResolveEntityTarget already verified hostility via IsEnemy; this gate
            // adds the range (and min-range deadzone) check before any rotation.
            float weaponRangeCells = 0f;
            try
            {
                weaponRangeCells = _entity != null ? _entity.WeaponRange : 0f;
            }
            catch
            {
                return;
            }
            float? minRangeCells = null;
            try
            {
                string entityId = _entity != null ? _entity.EntityId : null;
                if (!string.IsNullOrEmpty(entityId) && RuntimeHooks.MinAttackRangeResolver != null)
                {
                    minRangeCells = RuntimeHooks.MinAttackRangeResolver(entityId);
                }
            }
            catch { }
            float worldDistance = toTarget.magnitude;
            if (!TurretAimPolicy.IsInWeaponRange(worldDistance, weaponRangeCells, minRangeCells))
            {
                return;
            }

            if (toTarget.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toTarget, Vector3.up);
                float effectiveTraverseSpeed = string.Equals(_activeTacticalStanceVisual, "Aggressive", StringComparison.OrdinalIgnoreCase)
                    ? _turretTraverseSpeed * 1.5f
                    : _turretTraverseSpeed;
                _socketTurretPivot.rotation = Quaternion.RotateTowards(
                    _socketTurretPivot.rotation, targetRot, effectiveTraverseSpeed * Time.deltaTime);
            }
        }

        private void UpdateFactoryDeployAnimation()
        {
            if (_socketDeployUnit == null || IsEntityDead()) return;

            float targetProgress;
            if (_doorOpenOverride.HasValue)
            {
                targetProgress = Mathf.Clamp01(_doorOpenOverride.Value);
            }
            else
            {
                bool isProducing = CheckIfProducing();
                targetProgress = isProducing ? 1.0f : 0.0f;
            }
            _doorOpenProgress = Mathf.MoveTowards(_doorOpenProgress, targetProgress, Time.deltaTime * 1.8f);

            // Door slides upward along local Y
            _socketDeployUnit.localPosition = _doorClosedLocalPos + new Vector3(0f, _doorOpenProgress * 1.4f, 0f);
        }

        private void UpdateRecoilRecovery()
        {
            if (_socketRecoilPrimary == null) return;

            if (_currentRecoilOffset > 0.001f)
            {
                _currentRecoilOffset = Mathf.Lerp(_currentRecoilOffset, 0f, Time.deltaTime * 12f);
                _socketRecoilPrimary.localPosition = _recoilBaseLocalPos - new Vector3(0f, 0f, _currentRecoilOffset);
            }
            else if (_socketRecoilPrimary.localPosition != _recoilBaseLocalPos)
            {
                _socketRecoilPrimary.localPosition = _recoilBaseLocalPos;
            }
        }

        public bool IsDamagedStateActive => _isDamagedStateActive;
        public bool IsCriticalStateActive => _isCriticalStateActive;
        public bool IsTurretJammed => _turretJammedOverride.HasValue ? _turretJammedOverride.Value : _isTurretJammed;
        public float CurrentRecoilOffset => _currentRecoilOffset;
        public float DoorOpenProgress => _doorOpenProgress;
        public bool IsStunnedVisualActive => _isStunnedVisualActive;
        public bool IsOnFireVisualActive => _isOnFireVisualActive;
        public bool IsBoostedVisualActive => _isBoostedVisualActive;
        public string ActiveTacticalStanceVisual => _activeTacticalStanceVisual;
        public string ActiveVisualVariantId => _activeVisualVariantId;
        public string ActivePresetName => _activePresetName;
        public Color? ActiveColorTint => _activeColorTint;

        public bool TryApplyVisualVariant(RuntimeUnitVisualVariant variant)
        {
            if (variant == null)
            {
                ResetVisualVariant();
                return true;
            }

            var stagedModules = new List<KeyValuePair<GameObject, bool>>();
            var stagedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                IReadOnlyList<RuntimeUnitVisualModule> modules = variant.Modules;
                if (modules != null)
                {
                    for (int i = 0; i < modules.Count; i++)
                    {
                        RuntimeUnitVisualModule module = modules[i];
                        if (module == null || module.Prefab == null)
                        {
                            DestroyStagedVisualModules(stagedModules);
                            return false;
                        }

                        string hardpointType;
                        Transform mount;
                        if (!RogueCommandSocketCatalog.TryResolveHardpointType(module.SlotId, out hardpointType) ||
                            !stagedSlots.Add(hardpointType) ||
                            !TryGetVisualModuleMount(hardpointType, out mount))
                        {
                            DestroyStagedVisualModules(stagedModules);
                            return false;
                        }

                        bool shouldBeActive = module.Prefab.activeSelf;
                        GameObject instance = UnityEngine.Object.Instantiate(module.Prefab) as GameObject;
                        if (instance == null)
                        {
                            DestroyStagedVisualModules(stagedModules);
                            return false;
                        }

                        instance.SetActive(false);
                        instance.name = "RCL_RuntimeModule_" + hardpointType + "_" +
                            SanitizeVisualVariantName(variant.VariantId);
                        instance.transform.SetParent(mount, false);
                        stagedModules.Add(new KeyValuePair<GameObject, bool>(instance, shouldBeActive));
                        ApplyColorTintToRenderers(
                            instance.GetComponentsInChildren<Renderer>(true), variant.ColorTint);
                    }
                }
            }
            catch
            {
                DestroyStagedVisualModules(stagedModules);
                return false;
            }

            // All module references and mounts have been resolved. Commit by retiring only
            // instances owned by the previous visual variant, preserving group active state.
            RemoveActiveVisualModules();
            for (int i = 0; i < stagedModules.Count; i++)
            {
                GameObject instance = stagedModules[i].Key;
                if (instance == null) continue;
                _activeVisualModuleInstances.Add(instance);
                instance.SetActive(stagedModules[i].Value);
            }

            _activeVisualVariantId = variant.VariantId;
            _activePresetName = variant.PresetName;
            _activeColorTint = variant.ColorTint;
            ApplyColorTintToRenderers(variant.ColorTint);

            if (_intactMeshObject != null)
            {
                if (!_hasIntactMeshBaseLocalScale)
                {
                    _intactMeshBaseLocalScale = _intactMeshObject.transform.localScale;
                    _hasIntactMeshBaseLocalScale = true;
                }
                float scale = variant.Scale > 0.01f ? variant.Scale : 1f;
                _intactMeshObject.transform.localScale = _intactMeshBaseLocalScale * scale;
            }

            return true;
        }

        public void ResetVisualVariant()
        {
            RemoveActiveVisualModules();
            _activeVisualVariantId = null;
            _activePresetName = null;
            _activeColorTint = null;
            ApplyColorTintToRenderers(new Color(1f, 1f, 1f, 1f));
            if (_intactMeshObject != null && _hasIntactMeshBaseLocalScale)
            {
                _intactMeshObject.transform.localScale = _intactMeshBaseLocalScale;
            }
        }

        public int ActiveVisualModuleCount => _activeVisualModuleInstances.Count;

        private bool TryGetVisualModuleMount(string hardpointType, out Transform mount)
        {
            mount = null;
            UniqueSocketRoleBinding<Transform> groupRole;
            if (_socketRoleBindings.TryGetValue("attachment_group:" + hardpointType, out groupRole) &&
                groupRole != null && groupRole.IsAmbiguous)
            {
                return false;
            }

            Transform socket;
            Transform group;
            if (!_socketHardpoints.TryGetValue(hardpointType, out socket) || socket == null ||
                !_attachmentGroups.TryGetValue(hardpointType, out group) || group == null)
            {
                return false;
            }

            mount = group;
            return true;
        }

        private static string SanitizeVisualVariantName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "variant";
            return value.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
        }

        private void DestroyStagedVisualModules(List<KeyValuePair<GameObject, bool>> stagedModules)
        {
            if (stagedModules == null) return;
            for (int i = 0; i < stagedModules.Count; i++)
            {
                GameObject instance = stagedModules[i].Key;
                if (instance != null)
                {
                    RestoreAndForgetTintRenderers(instance.GetComponentsInChildren<Renderer>(true));
                    UnityEngine.Object.Destroy(instance);
                }
            }
            stagedModules.Clear();
        }

        private void RemoveActiveVisualModules()
        {
            for (int i = 0; i < _activeVisualModuleInstances.Count; i++)
            {
                GameObject instance = _activeVisualModuleInstances[i];
                if (instance == null) continue;
                RestoreAndForgetTintRenderers(instance.GetComponentsInChildren<Renderer>(true));
                instance.SetActive(false);
                UnityEngine.Object.Destroy(instance);
            }
            _activeVisualModuleInstances.Clear();
        }

        private void CacheTintRenderers()
        {
            if (_cachedRenderers != null) return;
            _cachedRenderers = new List<Renderer>();
            Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer renderer = allRenderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer) continue;
                string rendererName = renderer.name ?? string.Empty;
                if (rendererName.StartsWith("RCL_RuntimeModule_", StringComparison.OrdinalIgnoreCase) ||
                    rendererName.EndsWith("_COLLIDER", StringComparison.OrdinalIgnoreCase) ||
                    rendererName.EndsWith("_ShieldBubble", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _cachedRenderers.Add(renderer);
            }
        }

        private void ApplyColorTintToRenderers(Color tintColor)
        {
            CacheTintRenderers();
            ApplyColorTintToRenderers(_cachedRenderers, tintColor);
        }

        private void ApplyColorTintToRenderers(IReadOnlyList<Renderer> renderers, Color tintColor)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Count; i++)
            {
                ApplyColorTintToRenderer(renderers[i], tintColor);
            }
        }

        private void ApplyColorTintToRenderer(Renderer renderer, Color tintColor)
        {
            if (renderer == null) return;
            RendererTintBaseline baseline = GetOrCaptureTintBaseline(renderer);
            bool restore = IsWhiteTint(tintColor);
            for (int materialIndex = 0; materialIndex < baseline.Blocks.Length; materialIndex++)
            {
                MaterialPropertyBlock baselineBlock = baseline.Blocks[materialIndex];
                if (baseline.Materials.Length == 0)
                {
                    renderer.SetPropertyBlock(baselineBlock);
                    continue;
                }

                renderer.SetPropertyBlock(baselineBlock, materialIndex);
                if (restore) continue;

                Material material = baseline.Materials[materialIndex];
                if (material == null) continue;
                var workingBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(workingBlock, materialIndex);
                bool usesTeamColorMask = material.HasProperty("_TeamColor");
                if (usesTeamColorMask)
                {
                    workingBlock.SetColor("_TeamColor", tintColor);
                }
                else if (material.HasProperty("_BaseColor"))
                {
                    workingBlock.SetColor("_BaseColor", material.GetColor("_BaseColor") * tintColor);
                }
                else if (material.HasProperty("_Color"))
                {
                    workingBlock.SetColor("_Color", material.GetColor("_Color") * tintColor);
                }
                renderer.SetPropertyBlock(workingBlock, materialIndex);
            }
        }

        private RendererTintBaseline GetOrCaptureTintBaseline(Renderer renderer)
        {
            RendererTintBaseline baseline;
            if (_rendererTintBaselines.TryGetValue(renderer, out baseline)) return baseline;

            Material[] materials = renderer.sharedMaterials ?? Array.Empty<Material>();
            var blocks = new MaterialPropertyBlock[Math.Max(1, materials.Length)];
            for (int i = 0; i < blocks.Length; i++)
            {
                blocks[i] = new MaterialPropertyBlock();
                if (materials.Length == 0)
                {
                    renderer.GetPropertyBlock(blocks[i]);
                }
                else
                {
                    renderer.GetPropertyBlock(blocks[i], i);
                }
            }

            baseline = new RendererTintBaseline { Materials = materials, Blocks = blocks };
            _rendererTintBaselines.Add(renderer, baseline);
            return baseline;
        }

        private void RestoreAndForgetTintRenderers(IReadOnlyList<Renderer> renderers)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                RendererTintBaseline baseline;
                if (!_rendererTintBaselines.TryGetValue(renderer, out baseline)) continue;
                for (int materialIndex = 0; materialIndex < baseline.Blocks.Length; materialIndex++)
                {
                    if (baseline.Materials.Length == 0) renderer.SetPropertyBlock(baseline.Blocks[materialIndex]);
                    else renderer.SetPropertyBlock(baseline.Blocks[materialIndex], materialIndex);
                }
                _rendererTintBaselines.Remove(renderer);
            }
        }

        private static bool IsWhiteTint(Color tintColor)
        {
            return Math.Abs(tintColor.r - 1f) < 0.0001f &&
                Math.Abs(tintColor.g - 1f) < 0.0001f &&
                Math.Abs(tintColor.b - 1f) < 0.0001f &&
                Math.Abs(tintColor.a - 1f) < 0.0001f;
        }

        public void SetTacticalStanceVisual(string stance)
        {
            if (string.IsNullOrEmpty(stance)) return;
            string previousStance = _activeTacticalStanceVisual;
            _activeTacticalStanceVisual = stance;

            bool wasDefensive = string.Equals(previousStance, "Defensive", StringComparison.OrdinalIgnoreCase);
            bool isDefensive = string.Equals(stance, "Defensive", StringComparison.OrdinalIgnoreCase);

            bool wasAggressive = string.Equals(previousStance, "Aggressive", StringComparison.OrdinalIgnoreCase);
            bool isAggressive = string.Equals(stance, "Aggressive", StringComparison.OrdinalIgnoreCase);

            if (isDefensive && !wasDefensive)
            {
                SetAttachmentActive("Armor", true);
                SetAttachmentActive("Shield", true);
                SetAttachmentActive("APS", true);
                SetAttachmentActive("ERA", true);
            }
            else if (!isDefensive && wasDefensive)
            {
                SetAttachmentActive("Armor", false);
                SetAttachmentActive("Shield", false);
                SetAttachmentActive("APS", false);
                SetAttachmentActive("ERA", false);
            }

            if (isAggressive && !wasAggressive)
            {
                SetAttachmentActive("Sponsons", true);
                SetAttachmentActive("FuelDrums", true);
            }
            else if (!isAggressive && wasAggressive)
            {
                SetAttachmentActive("Sponsons", false);
                SetAttachmentActive("FuelDrums", false);
            }
        }

        public void SetTurretJammedOverride(bool? jammed)
        {
            _turretJammedOverride = jammed;
            if (jammed.HasValue && jammed.Value && _socketTurretPivot != null)
            {
                _socketTurretPivot.localRotation *= Quaternion.Euler(4.5f, 0f, 3.8f);
            }
        }

        public void SetDoorOpenOverride(float? progress)
        {
            _doorOpenOverride = progress;
        }

        public bool TrySetAttachmentState(string attachmentType, bool active)
        {
            if (string.IsNullOrWhiteSpace(attachmentType)) return false;
            Transform visual;
            if (TryGetHardpointVisual(attachmentType, out visual) && visual != null)
            {
                visual.gameObject.SetActive(active);
                return true;
            }
            return false;
        }

        public void TriggerFootstepFeedback(string footSocketName = null)
        {
            Transform foot = null;
            if (!string.IsNullOrWhiteSpace(footSocketName))
            {
                Transform[] all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (string.Equals(all[i].name, footSocketName, StringComparison.OrdinalIgnoreCase))
                    {
                        foot = all[i];
                        break;
                    }
                }
            }
            if (foot == null)
            {
                Transform[] all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].name.StartsWith("Socket_FootContact", StringComparison.OrdinalIgnoreCase))
                    {
                        foot = all[i];
                        break;
                    }
                }
            }
            if (foot != null)
            {
                RuntimeVisualVfxFallback.TriggerMuzzleFlashAt(transform, foot.position, Quaternion.identity);
            }
        }

        public string GetVisualStatusSummary()
        {
            string variant = _activeVisualVariantId ?? "none";
            string preset = _activePresetName ?? "default";
            return $"damaged={_isDamagedStateActive},critical={_isCriticalStateActive},jammed={IsTurretJammed},stunned={_isStunnedVisualActive},fire={_isOnFireVisualActive},boosted={_isBoostedVisualActive},stance={_activeTacticalStanceVisual},variant={variant},preset={preset},modules={ActiveVisualModuleCount},recoil={_currentRecoilOffset:F2},door={_doorOpenProgress:F2}";
        }

        public void TriggerWeaponFeedback(float recoilDistance = 0.22f)
        {
            _currentRecoilOffset = recoilDistance;
            if (_socketRecoilPrimary != null)
            {
                _socketRecoilPrimary.localPosition = _recoilBaseLocalPos - new Vector3(0f, 0f, _currentRecoilOffset);
            }

            if (_socketMuzzlePrimary != null)
            {
                RuntimeVisualVfxFallback.TriggerMuzzleFlash(_socketMuzzlePrimary, this, 0.07f);
            }

            if (_animator != null && _hasFireAnimationParameter && !_deathAnimationTriggered)
            {
                _animator.SetTrigger(FireAnimationParameter);
            }
        }

        public void SetAttachmentActive(string attachmentType, bool active)
        {
            Transform visual;
            if (TryGetHardpointVisual(attachmentType, out visual))
            {
                visual.gameObject.SetActive(active);
            }
        }

        private bool TryGetHardpointVisual(string attachmentType, out Transform visual)
        {
            string normalizedType = NormalizeHardpointType(attachmentType);
            Transform groupVisual;
            if (!_attachmentGroups.TryGetValue(normalizedType, out groupVisual) || groupVisual == null)
            {
                groupVisual = null;
            }

            Transform hardpointSocket;
            if (!_socketHardpoints.TryGetValue(normalizedType, out hardpointSocket) || hardpointSocket == null)
            {
                hardpointSocket = null;
            }

            UniqueSocketRoleBinding<Transform> groupRole;
            _socketRoleBindings.TryGetValue("attachment_group:" + normalizedType, out groupRole);
            return UniqueSocketRoleBinding<Transform>.TryResolveVisual(
                groupRole, groupVisual, hardpointSocket, out visual);
        }

        private Transform GetHardpointSocket(string attachmentType)
        {
            Transform socket;
            return _socketHardpoints.TryGetValue(NormalizeHardpointType(attachmentType), out socket) ? socket : null;
        }

        private static string NormalizeHardpointType(string attachmentType)
        {
            string compact = (attachmentType ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Trim();
            switch (compact.ToLowerInvariant())
            {
                case "era": return "ERA";
                case "aps": return "APS";
                case "sponson":
                case "sponsons": return "Sponsons";
                case "fueldrum":
                case "fueldrums":
                case "fuel":
                case "drums": return "FuelDrums";
                default: return attachmentType ?? string.Empty;
            }
        }

        private bool IsHostileTarget(EntityController candidate)
        {
            if (candidate == null || candidate.gameObject == null || !candidate.gameObject.activeInHierarchy) return false;
            try
            {
                if (candidate.IsDestroyed || candidate.DestroyHasBeenStarted) return false;
                if (_stillExistsProp != null && !(bool)_stillExistsProp.GetValue(candidate, null)) return false;
                if (_isDestroyedProp != null && (bool)_isDestroyedProp.GetValue(candidate, null)) return false;
                if (_destroyHasBeenStartedProp != null && (bool)_destroyHasBeenStartedProp.GetValue(candidate, null)) return false;
                if (_isEnemyMethod != null)
                {
                    if (_singleArgCandidate == null) _singleArgCandidate = new object[1];
                    _singleArgCandidate[0] = candidate;
                    bool result = (bool)_isEnemyMethod.Invoke(_entity, _singleArgCandidate);
                    _singleArgCandidate[0] = null;
                    return result;
                }
            }
            catch
            {
                if (_singleArgCandidate != null) _singleArgCandidate[0] = null;
            }
            return false; // Unknown hostility is not enough to authorize visual targeting.
        }

        private Transform ResolveEntityTarget()
        {
            if (_entity == null) return null;
            try
            {
                // 1. Direct public CurrentAttackTarget on EntityController (authoritative hostile combat target)
                if (_currentAttackTargetProp != null)
                {
                    EntityController atkTgt = _currentAttackTargetProp.GetValue(_entity, null) as EntityController;
                    if (IsHostileTarget(atkTgt)) return atkTgt.transform;
                }

                // 2. EntityAttack._attackTarget
                if (_attackField != null && _attackTargetField != null)
                {
                    object atk = _attackField.GetValue(_entity);
                    if (atk != null)
                    {
                        EntityController atkTgt = _attackTargetField.GetValue(atk) as EntityController;
                        if (IsHostileTarget(atkTgt)) return atkTgt.transform;
                    }
                }

                // 3. SingleTargetAction._aiming target
                if (_attackField != null && _aimingField != null && _singleTargetActionTargetField != null)
                {
                    object atk = _attackField.GetValue(_entity);
                    if (atk != null)
                    {
                        object aiming = _aimingField.GetValue(atk);
                        if (aiming != null)
                        {
                            EntityController aimingTgt = _singleTargetActionTargetField.GetValue(aiming) as EntityController;
                            if (IsHostileTarget(aimingTgt)) return aimingTgt.transform;
                        }
                    }
                }

                // 4. Direct public CurrentFollowTarget on EntityController (only aim if hostile)
                if (_currentFollowTargetProp != null)
                {
                    EntityController flwTgt = _currentFollowTargetProp.GetValue(_entity, null) as EntityController;
                    if (IsHostileTarget(flwTgt)) return flwTgt.transform;
                }

                // 5. Player and AI Command fallbacks (only aim if hostile)
                if (_currentPlayerCommandField != null && _cmdTargetField != null)
                {
                    object cmd = _currentPlayerCommandField.GetValue(_entity);
                    if (cmd != null)
                    {
                        EntityController cmdTgt = _cmdTargetField.GetValue(cmd) as EntityController;
                        if (IsHostileTarget(cmdTgt)) return cmdTgt.transform;
                    }
                }
                if (_lastAiCommandField != null && _cmdTargetField != null)
                {
                    object cmd = _lastAiCommandField.GetValue(_entity);
                    if (cmd != null)
                    {
                        EntityController cmdTgt = _cmdTargetField.GetValue(cmd) as EntityController;
                        if (IsHostileTarget(cmdTgt)) return cmdTgt.transform;
                    }
                }
            }
            catch { }

            return null;
        }

        private bool CheckIfProducing()
        {
            if (_entity == null) return false;
            try
            {
                // 1. InProductionCount on EntityController (Nullable<int>)
                if (_inProductionCountProp != null)
                {
                    object val = _inProductionCountProp.GetValue(_entity, null);
                    if (val is int count && count > 0) return true;
                }

                // 2. ProductionInfo on EntityController
                if (_productionInfoProp != null)
                {
                    object info = _productionInfoProp.GetValue(_entity, null);
                    if (info != null)
                    {
                        if (_prodHasStartedField != null && (bool)_prodHasStartedField.GetValue(info)) return true;
                        if (_prodQueuedCountField != null && (int)_prodQueuedCountField.GetValue(info) > 0) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private float GetHealthRatio()
        {
            if (_entity == null) return 1f;
            try
            {
                if (_entity.MaxHealth > 0.001f)
                {
                    return Mathf.Clamp01(_entity.CurrentHealth / _entity.MaxHealth);
                }
                if (_healthRatioProp != null)
                {
                    return (float)_healthRatioProp.GetValue(_entity, null);
                }
            }
            catch { }

            float current = GetCurrentHealth();
            float max = Mathf.Max(1f, GetMaxHealth());
            return Mathf.Clamp01(current / max);
        }

        private float GetCurrentHealth()
        {
            if (_entity != null)
            {
                try { return _entity.CurrentHealth; } catch { }
                if (_currentHealthProp != null)
                {
                    try { return (float)_currentHealthProp.GetValue(_entity, null); } catch { }
                }
            }
            return 100f;
        }

        private float GetMaxHealth()
        {
            if (_entity != null)
            {
                try { return _entity.MaxHealth; } catch { }
                if (_maxHealthProp != null)
                {
                    try { return (float)_maxHealthProp.GetValue(_entity, null); } catch { }
                }
            }
            return 100f;
        }

        private bool IsEntityDead()
        {
            if (_entity == null) return true;
            try
            {
                if (_entity.IsDestroyed || _entity.DestroyHasBeenStarted) return true;
                if (_stillExistsProp != null && !(bool)_stillExistsProp.GetValue(_entity, null)) return true;
                if (_isDestroyedProp != null && (bool)_isDestroyedProp.GetValue(_entity, null)) return true;
                if (_destroyHasBeenStartedProp != null && (bool)_destroyHasBeenStartedProp.GetValue(_entity, null)) return true;
            }
            catch { }
            return false;
        }

        private void UpdateMotionSample(float dt)
        {
            if (dt <= 0f || _entity == null) return;

            Vector3 currentPos = _entity.transform.position;
            if (!_hasLastPosition)
            {
                _lastPosition = currentPos;
                _lastVelocity = Vector3.zero;
                _lastAcceleration = Vector3.zero;
                _hasLastPosition = true;
                return;
            }

            Vector3 velocity = (currentPos - _lastPosition) / dt;
            _lastAcceleration = (velocity - _lastVelocity) / dt;
            _lastPosition = currentPos;
            _lastVelocity = velocity;
        }

        private void UpdateAntennaWobble(float dt)
        {
            if (_socketAntenna == null || dt <= 0f || _entity == null) return;

            // Transform acceleration into vehicle local coordinates
            Vector3 localAccel = _entity.transform.InverseTransformDirection(_lastAcceleration);

            // 2D spring-damper model on local X (pitch/roll) and Y/Z
            float springK = 32f;
            float damping = 4.5f;

            float targetPitch = Mathf.Clamp(-localAccel.z * 1.5f, -25f, 25f);
            float targetRoll = Mathf.Clamp(localAccel.x * 1.5f, -25f, 25f);

            _antennaWobbleVelocity.x += (targetPitch - _antennaWobbleAngle.x) * springK * dt - _antennaWobbleVelocity.x * damping * dt;
            _antennaWobbleVelocity.y += (targetRoll - _antennaWobbleAngle.y) * springK * dt - _antennaWobbleVelocity.y * damping * dt;

            _antennaWobbleAngle += _antennaWobbleVelocity * dt;

            _socketAntenna.localRotation = _antennaBaseLocalRot * Quaternion.Euler(_antennaWobbleAngle.x, 0f, _antennaWobbleAngle.y);
        }

        private void UpdateVtolThrusterVectoring(float dt)
        {
            if (_socketThrusterVectorL == null && _socketThrusterVectorR == null) return;
            if (dt <= 0f || _entity == null) return;

            Vector3 velocity = _lastVelocity;
            Vector3 localVel = _entity.transform.InverseTransformDirection(velocity);
            float forwardSpeed = localVel.z;

            // Tilt forward up to 40 degrees when cruising forward, or back when braking
            float targetTilt = Mathf.Clamp(forwardSpeed * 5.0f, -15f, 40f);
            Quaternion tiltRot = Quaternion.Euler(targetTilt, 0f, 0f);

            if (_socketThrusterVectorL != null)
            {
                Quaternion targetRot = _thrusterVectorBaseLocalRotL * tiltRot;
                _socketThrusterVectorL.localRotation = Quaternion.Slerp(_socketThrusterVectorL.localRotation, targetRot, dt * 6f);
            }
            if (_socketThrusterVectorR != null)
            {
                Quaternion targetRot = _thrusterVectorBaseLocalRotR * tiltRot;
                _socketThrusterVectorR.localRotation = Quaternion.Slerp(_socketThrusterVectorR.localRotation, targetRot, dt * 6f);
            }
        }
    }
}
