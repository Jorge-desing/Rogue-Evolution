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
        private Transform _socketRecoilPrimary;
        private Transform _socketMuzzlePrimary;
        private readonly Dictionary<string, Transform> _socketHardpoints = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);

        private GameObject _damagedMeshObject;
        private GameObject _intactMeshObject;
        private GameObject _smokeVfxInstance;
        private GameObject _fireVfxInstance;

        private Vector3 _recoilBaseLocalPos;
        private float _currentRecoilOffset;
        private float _radarRpm = 30f;
        private float _turretTraverseSpeed = 120f;
        private bool _isDamagedStateActive;
        private bool _isCriticalStateActive;
        private bool _isTurretJammed;
        private float _doorOpenProgress;
        private Vector3 _doorClosedLocalPos;

        private static PropertyInfo _currentHealthProp;
        private static PropertyInfo _maxHealthProp;
        private static PropertyInfo _isDestroyedProp;
        private static FieldInfo _destroyHasBeenStartedField;

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

        private static bool HasCompatibleVisualSockets(Transform root)
        {
            if (root == null) return false;
            Queue<Transform> queue = new Queue<Transform>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                string name = current.name;
                if (name.StartsWith("Socket_", StringComparison.OrdinalIgnoreCase) ||
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

            return false;
        }

        public void Initialize(EntityController entity)
        {
            _entity = entity;
            DiscoverSocketsAndMeshes(transform);

            if (_socketRecoilPrimary != null)
            {
                _recoilBaseLocalPos = _socketRecoilPrimary.localPosition;
            }

            if (_socketDeployUnit != null)
            {
                _doorClosedLocalPos = _socketDeployUnit.localPosition;
            }

            CacheReflectionAccess();
        }

        private void DiscoverSocketsAndMeshes(Transform root)
        {
            Queue<Transform> queue = new Queue<Transform>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                string name = current.name;

                if (name.IndexOf("TurretPivot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("TurretYaw", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_socketTurretPivot == null) _socketTurretPivot = current;
                }
                else if (name.IndexOf("TurretPitch", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_socketTurretPitch == null) _socketTurretPitch = current;
                }
                else if (name.IndexOf("RadarMast", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("RadarDish", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_socketRadarMast == null) _socketRadarMast = current;
                }
                else if (name.IndexOf("Deploy_Unit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("DeployUnit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_socketDeployUnit == null) _socketDeployUnit = current;
                }
                else if (name.IndexOf("DamageSmoke", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_socketDamageSmoke == null) _socketDamageSmoke = current;
                }
                else if (name.IndexOf("DamageFire", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_socketDamageFire == null) _socketDamageFire = current;
                }
                else if (name.IndexOf("RecoilPrimary", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_socketRecoilPrimary == null) _socketRecoilPrimary = current;
                }
                else if (name.IndexOf("MuzzlePrimary", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_socketMuzzlePrimary == null) _socketMuzzlePrimary = current;
                }
                else if (name.StartsWith("Socket_Hardpoint_", StringComparison.OrdinalIgnoreCase))
                {
                    string hpType = name.Substring("Socket_Hardpoint_".Length);
                    _socketHardpoints[hpType] = current;
                }
                else if (name.EndsWith("_Damaged", StringComparison.OrdinalIgnoreCase))
                {
                    _damagedMeshObject = current.gameObject;
                    _damagedMeshObject.SetActive(false); // Initially intact
                }
                else if (name.StartsWith("RCL_Visual_", StringComparison.OrdinalIgnoreCase) && _intactMeshObject == null)
                {
                    _intactMeshObject = current.gameObject;
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    queue.Enqueue(current.GetChild(i));
                }
            }
        }

        private static void CacheReflectionAccess()
        {
            if (_currentHealthProp != null) return;
            Type ecType = typeof(EntityController);
            _currentHealthProp = ecType.GetProperty("CurrentHealth", BindingFlags.Public | BindingFlags.Instance);
            _maxHealthProp = ecType.GetProperty("MaxHealth", BindingFlags.Public | BindingFlags.Instance);
            _isDestroyedProp = ecType.GetProperty("IsDestroyed", BindingFlags.Public | BindingFlags.Instance);
            _destroyHasBeenStartedField = ecType.GetField("DestroyHasBeenStarted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private void Update()
        {
            if (_entity == null) return;

            UpdateHealthAndDamageTransitions();
            UpdateRadarSweep();
            UpdateTurretAiming();
            UpdateFactoryDeployAnimation();
            UpdateRecoilRecovery();
        }

        private void UpdateHealthAndDamageTransitions()
        {
            float currentHp = GetCurrentHealth();
            float maxHp = Mathf.Max(1f, GetMaxHealth());
            float ratio = currentHp / maxHp;

            bool isDead = IsEntityDead();

            // 1. Moderate Damage (<50% HP): Smoke particle spawner
            if (ratio <= 0.50f && !_isDamagedStateActive && !isDead)
            {
                _isDamagedStateActive = true;
                Transform smokeMount = _socketDamageSmoke ?? transform;
                _smokeVfxInstance = RuntimeVisualVfxFallback.SpawnDamageSmoke(smokeMount);

                if (_damagedMeshObject != null)
                {
                    _damagedMeshObject.SetActive(true);
                    if (_intactMeshObject != null)
                    {
                        _intactMeshObject.SetActive(false);
                    }
                }
            }

            // 2. Critical Damage (<25% HP): Fire particles and jammed turret tilt
            if (ratio <= 0.25f && !_isCriticalStateActive && !isDead)
            {
                _isCriticalStateActive = true;
                Transform fireMount = _socketDamageFire ?? transform;
                _fireVfxInstance = RuntimeVisualVfxFallback.SpawnDamageFire(fireMount);

                if (_socketTurretPivot != null && !_isTurretJammed)
                {
                    _isTurretJammed = true;
                    // Apply realistic mechanical seizure tilt
                    _socketTurretPivot.localRotation *= Quaternion.Euler(4.5f, 0f, 3.8f);
                }
            }

            // 3. Destruction cleanup
            if (isDead)
            {
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

        private void UpdateRadarSweep()
        {
            if (_socketRadarMast == null || IsEntityDead()) return;
            _socketRadarMast.Rotate(Vector3.up, _radarRpm * Time.deltaTime, Space.Self);
        }

        private void UpdateTurretAiming()
        {
            if (_socketTurretPivot == null || _isTurretJammed || IsEntityDead()) return;

            Transform target = ResolveEntityTarget();
            if (target == null) return;

            Vector3 toTarget = target.position - _socketTurretPivot.position;
            toTarget.y = 0f; // Horizontal planar azimuth

            if (toTarget.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toTarget, Vector3.up);
                _socketTurretPivot.rotation = Quaternion.RotateTowards(
                    _socketTurretPivot.rotation, targetRot, _turretTraverseSpeed * Time.deltaTime);
            }
        }

        private void UpdateFactoryDeployAnimation()
        {
            if (_socketDeployUnit == null || IsEntityDead()) return;

            bool isProducing = CheckIfProducing();
            float targetProgress = isProducing ? 1.0f : 0.0f;
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
        }

        public void SetAttachmentActive(string attachmentType, bool active)
        {
            Transform socket;
            if (_socketHardpoints.TryGetValue(attachmentType, out socket) && socket != null)
            {
                socket.gameObject.SetActive(active);
            }
        }

        private Transform ResolveEntityTarget()
        {
            try
            {
                // Inspect EntityController fields for target
                FieldInfo aiCmdField = typeof(EntityController).GetField("_lastAiCommand", BindingFlags.NonPublic | BindingFlags.Instance);
                if (aiCmdField != null)
                {
                    object cmd = aiCmdField.GetValue(_entity);
                    if (cmd != null)
                    {
                        FieldInfo tField = cmd.GetType().GetField("target");
                        if (tField != null)
                        {
                            EntityController targetEc = tField.GetValue(cmd) as EntityController;
                            if (targetEc != null && targetEc.gameObject != null)
                            {
                                return targetEc.transform;
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private bool CheckIfProducing()
        {
            try
            {
                FieldInfo prodField = typeof(EntityController).GetField("_productionInfo", BindingFlags.NonPublic | BindingFlags.Instance);
                if (prodField != null)
                {
                    object info = prodField.GetValue(_entity);
                    if (info != null)
                    {
                        FieldInfo isProdField = info.GetType().GetField("isProducing");
                        if (isProdField != null)
                        {
                            return (bool)isProdField.GetValue(info);
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private float GetCurrentHealth()
        {
            if (_currentHealthProp != null && _entity != null)
            {
                try { return (float)_currentHealthProp.GetValue(_entity, null); } catch { }
            }
            return 100f;
        }

        private float GetMaxHealth()
        {
            if (_maxHealthProp != null && _entity != null)
            {
                try { return (float)_maxHealthProp.GetValue(_entity, null); } catch { }
            }
            return 100f;
        }

        private bool IsEntityDead()
        {
            if (_entity == null) return true;
            try
            {
                if (_isDestroyedProp != null && (bool)_isDestroyedProp.GetValue(_entity, null)) return true;
                if (_destroyHasBeenStartedField != null && (bool)_destroyHasBeenStartedField.GetValue(_entity)) return true;
            }
            catch { }
            return false;
        }
    }
}
