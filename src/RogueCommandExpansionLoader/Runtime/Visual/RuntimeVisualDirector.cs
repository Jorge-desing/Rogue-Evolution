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

        private static bool HasCompatibleVisualSockets(Transform root)
        {
            if (root == null) return false;
            Queue<Transform> queue = new Queue<Transform>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                string name = current.name;
                if (name.IndexOf("Socket_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("RCL_Visual_", StringComparison.OrdinalIgnoreCase) >= 0 ||
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
                         name.IndexOf("DeployUnit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("DeployRamp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("Deploy_Ramp", StringComparison.OrdinalIgnoreCase) >= 0)
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
                else if (name.IndexOf("Socket_Hardpoint_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int hpIdx = name.IndexOf("Socket_Hardpoint_", StringComparison.OrdinalIgnoreCase);
                    string hpType = name.Substring(hpIdx + "Socket_Hardpoint_".Length);
                    _socketHardpoints[hpType] = current;
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
            _isEnemyMethod = ecType.GetMethod("IsEnemy", BindingFlags.Public | BindingFlags.Instance);

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

            UpdateHealthAndDamageTransitions();
            UpdateRadarSweep();
            UpdateTurretAiming();
            UpdateFactoryDeployAnimation();
            UpdateAutomaticWeaponFeedback();
            UpdateRecoilRecovery();
        }

        private void UpdateAutomaticWeaponFeedback()
        {
            if (_socketRecoilPrimary == null && _socketMuzzlePrimary == null) return;
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
            float ratio = GetHealthRatio();
            bool isDead = IsEntityDead();

            // 1. Reactive Armor (ERA) Detonation on initial heavy impact (<= 75% HP)
            if (ratio <= 0.75f && !isDead)
            {
                Transform eraSocket;
                if (_socketHardpoints.TryGetValue("ERA", out eraSocket) && eraSocket != null && eraSocket.gameObject.activeSelf)
                {
                    eraSocket.gameObject.SetActive(false);
                    RuntimeVisualVfxFallback.TriggerMuzzleFlash(eraSocket, this);
                }
            }
            else if (ratio > 0.85f && !isDead)
            {
                Transform eraSocket;
                if (_socketHardpoints.TryGetValue("ERA", out eraSocket) && eraSocket != null && !eraSocket.gameObject.activeSelf)
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
                    else
                    {
                        ApplyProceduralScorch(true);
                    }

                    // Auxiliary Fuel Drums jettison / smoke on heavy damage
                    Transform fuelSocket;
                    if (_socketHardpoints.TryGetValue("FuelDrums", out fuelSocket) && fuelSocket != null && fuelSocket.gameObject.activeSelf)
                    {
                        fuelSocket.gameObject.SetActive(false);
                        RuntimeVisualVfxFallback.SpawnDamageSmoke(fuelSocket);
                    }
                }
            }
            else if (ratio > 0.50f && !isDead && _isDamagedStateActive)
            {
                // Health recovered from moderate damage via field repairs
                _isDamagedStateActive = false;
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
                if (_socketHardpoints.TryGetValue("FuelDrums", out fuelSocket) && fuelSocket != null && !fuelSocket.gameObject.activeSelf)
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
                    Transform fireMount = _socketDamageFire ?? transform;
                    _fireVfxInstance = RuntimeVisualVfxFallback.SpawnDamageFire(fireMount, _entity);

                    if (_socketTurretPivot != null && !_isTurretJammed)
                    {
                        _isTurretJammed = true;
                        _socketTurretPivot.localRotation *= Quaternion.Euler(4.5f, 0f, 3.8f);
                    }

                    // APS radar burnout
                    Transform apsSocket;
                    if (_socketHardpoints.TryGetValue("APS", out apsSocket) && apsSocket != null)
                    {
                        apsSocket.gameObject.SetActive(false);
                    }
                }
            }
            else if (ratio > 0.25f && !isDead && _isCriticalStateActive)
            {
                // Health recovered from critical damage
                _isCriticalStateActive = false;
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
                if (_socketHardpoints.TryGetValue("APS", out apsSocket) && apsSocket != null && !apsSocket.gameObject.activeSelf)
                {
                    apsSocket.gameObject.SetActive(true);
                }
            }

            // 4. Destruction cleanup
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

        private void UpdateRadarSweep()
        {
            if (_socketRadarMast == null || IsEntityDead()) return;
            // Calibrate to true 30 RPM: 30 * 360 deg / 60 sec = 180 deg/sec
            float degPerSec = _radarRpm * 6.0f;
            _socketRadarMast.Rotate(Vector3.up, degPerSec * Time.deltaTime, Space.Self);
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

        private bool IsHostileTarget(EntityController candidate)
        {
            if (candidate == null || candidate.gameObject == null) return false;
            try
            {
                if (_isDestroyedProp != null && (bool)_isDestroyedProp.GetValue(candidate, null)) return false;
                if (_stillExistsProp != null && !(bool)_stillExistsProp.GetValue(candidate, null)) return false;
                if (_destroyHasBeenStartedProp != null && (bool)_destroyHasBeenStartedProp.GetValue(candidate, null)) return false;
                if (_isEnemyMethod != null)
                {
                    return (bool)_isEnemyMethod.Invoke(_entity, new object[] { candidate });
                }
            }
            catch { }
            return true; // Fallback if reflection fails
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
                if (_stillExistsProp != null && !(bool)_stillExistsProp.GetValue(_entity, null)) return true;
                if (_isDestroyedProp != null && (bool)_isDestroyedProp.GetValue(_entity, null)) return true;
                if (_destroyHasBeenStartedProp != null && (bool)_destroyHasBeenStartedProp.GetValue(_entity, null)) return true;
            }
            catch { }
            return false;
        }
    }
}
