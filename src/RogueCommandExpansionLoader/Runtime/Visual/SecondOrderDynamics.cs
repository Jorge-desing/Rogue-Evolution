using System;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>
    /// Tuning tuple (f, z, r) for Second-Order Dynamics procedural motion:
    /// - FrequencyHz (f): natural response frequency in cycles/sec (&gt; 0).
    /// - Damping (z): damping ratio (0 &lt; z &lt; 1 underdamped with mechanical settle, z &gt;= 1 overdamped).
    /// - InitialResponse (r): initial velocity kick (&gt; 0 immediate, &lt; 0 anticipation, 0 smooth).
    /// Based on the semi-implicit Euler second-order solver popularized by t3ssel8r.
    /// </summary>
    public readonly struct SecondOrderDynamicsParameters
    {
        public readonly float FrequencyHz;
        public readonly float Damping;
        public readonly float InitialResponse;

        public SecondOrderDynamicsParameters(float frequencyHz, float damping, float initialResponse)
        {
            FrequencyHz = float.IsNaN(frequencyHz) || float.IsInfinity(frequencyHz) || frequencyHz <= 0.05f
                ? 1.0f
                : frequencyHz;
            Damping = float.IsNaN(damping) || float.IsInfinity(damping) || damping < 0f
                ? 0.5f
                : damping;
            InitialResponse = float.IsNaN(initialResponse) || float.IsInfinity(initialResponse)
                ? 0f
                : initialResponse;
        }
    }

    /// <summary>
    /// Role-calibrated second-order dynamics presets for units across light, medium, heavy, SAM, and walker archetypes.
    /// </summary>
    public static class SecondOrderDynamicsPresets
    {
        public readonly struct RoleRigProfile
        {
            public readonly SecondOrderDynamicsParameters Recoil;
            public readonly SecondOrderDynamicsParameters TurretAim;
            public readonly SecondOrderDynamicsParameters ChassisTilt;
            public readonly SecondOrderDynamicsParameters Antenna;
            public readonly SecondOrderDynamicsParameters ThrusterVector;

            public RoleRigProfile(
                SecondOrderDynamicsParameters recoil,
                SecondOrderDynamicsParameters turretAim,
                SecondOrderDynamicsParameters chassisTilt,
                SecondOrderDynamicsParameters antenna,
                SecondOrderDynamicsParameters thrusterVector)
            {
                Recoil = recoil;
                TurretAim = turretAim;
                ChassisTilt = chassisTilt;
                Antenna = antenna;
                ThrusterVector = thrusterVector;
            }
        }

        public static readonly RoleRigProfile Light = new RoleRigProfile(
            recoil: new SecondOrderDynamicsParameters(5.5f, 0.52f, 1.35f),
            turretAim: new SecondOrderDynamicsParameters(4.5f, 0.72f, 0.90f),
            chassisTilt: new SecondOrderDynamicsParameters(3.6f, 0.55f, 0.65f),
            antenna: new SecondOrderDynamicsParameters(3.8f, 0.25f, 1.25f),
            thrusterVector: new SecondOrderDynamicsParameters(3.6f, 0.68f, 0.95f));

        public static readonly RoleRigProfile Medium = new RoleRigProfile(
            recoil: new SecondOrderDynamicsParameters(4.2f, 0.58f, 1.20f),
            turretAim: new SecondOrderDynamicsParameters(3.5f, 0.75f, 0.80f),
            chassisTilt: new SecondOrderDynamicsParameters(2.8f, 0.62f, 0.50f),
            antenna: new SecondOrderDynamicsParameters(3.2f, 0.28f, 1.15f),
            thrusterVector: new SecondOrderDynamicsParameters(3.0f, 0.75f, 0.85f));

        public static readonly RoleRigProfile Heavy = new RoleRigProfile(
            recoil: new SecondOrderDynamicsParameters(2.8f, 0.64f, 0.95f),
            turretAim: new SecondOrderDynamicsParameters(2.2f, 0.82f, 0.60f),
            chassisTilt: new SecondOrderDynamicsParameters(1.9f, 0.70f, 0.35f),
            antenna: new SecondOrderDynamicsParameters(2.4f, 0.34f, 0.95f),
            thrusterVector: new SecondOrderDynamicsParameters(2.2f, 0.82f, 0.70f));

        public static readonly RoleRigProfile Sam = new RoleRigProfile(
            recoil: new SecondOrderDynamicsParameters(3.4f, 0.60f, 1.05f),
            turretAim: new SecondOrderDynamicsParameters(4.0f, 0.70f, 0.85f),
            chassisTilt: new SecondOrderDynamicsParameters(2.5f, 0.66f, 0.45f),
            antenna: new SecondOrderDynamicsParameters(3.6f, 0.26f, 1.15f),
            thrusterVector: new SecondOrderDynamicsParameters(3.0f, 0.75f, 0.85f));

        public static readonly RoleRigProfile Walker = new RoleRigProfile(
            recoil: new SecondOrderDynamicsParameters(3.6f, 0.56f, 1.10f),
            turretAim: new SecondOrderDynamicsParameters(3.3f, 0.74f, 0.75f),
            chassisTilt: new SecondOrderDynamicsParameters(2.4f, 0.58f, 0.40f),
            antenna: new SecondOrderDynamicsParameters(3.0f, 0.30f, 1.10f),
            thrusterVector: new SecondOrderDynamicsParameters(2.8f, 0.75f, 0.80f));

        public static string ResolveRoleName(string entityOrObjectName)
        {
            if (string.IsNullOrWhiteSpace(entityOrObjectName))
            {
                return "Medium";
            }

            int colonIdx = entityOrObjectName.IndexOf(':');
            string unitToken = colonIdx >= 0 && colonIdx + 1 < entityOrObjectName.Length
                ? entityOrObjectName.Substring(colonIdx + 1)
                : entityOrObjectName;
            string lower = unitToken.ToLowerInvariant();

            if (lower.Contains("walker") || lower.Contains("biped") ||
                lower.Contains("hexapod") || lower.Contains("arachnid") ||
                lower.Contains("spider") || lower.Contains("mech"))
            {
                return "Walker";
            }

            if (lower.Contains("bulwark") || lower.Contains("gravitas") ||
                lower.Contains("colossus") || lower.Contains("overlord") ||
                lower.Contains("salvage") || lower.Contains("hauler") ||
                lower.Contains("siege") || lower.Contains("heavy"))
            {
                return "Heavy";
            }

            if (lower.Contains("skyguard") || lower.Contains("missile") ||
                lower == "sam" || lower.EndsWith("-sam", StringComparison.Ordinal) ||
                lower.EndsWith("_sam", StringComparison.Ordinal) || lower.Contains("sam_") ||
                lower.Contains("sam-"))
            {
                return "Sam";
            }

            if (lower.Contains("tidebreaker") || lower.Contains("hover") ||
                lower.Contains("fieldmedic") || lower.Contains("field-medic") ||
                lower.Contains("sandlance") || lower.Contains("skirmish") ||
                lower.Contains("rover") || lower.Contains("bike") ||
                lower.Contains("scout") || lower.Contains("light"))
            {
                return "Light";
            }

            return "Medium";
        }

        public static RoleRigProfile GetProfileForIdentifier(string entityOrObjectName)
        {
            switch (ResolveRoleName(entityOrObjectName))
            {
                case "Light": return Light;
                case "Heavy": return Heavy;
                case "Sam": return Sam;
                case "Walker": return Walker;
                default: return Medium;
            }
        }
    }

    /// <summary>
    /// Heap-free scalar second-order dynamics solver.
    /// </summary>
    public struct SecondOrderDynamicsFloat
    {
        private float _xp;
        private float _y;
        private float _yd;
        private float _k1;
        private float _k2;
        private float _k3;
        private bool _initialized;

        public float Value => _y;
        public float Velocity => _yd;
        public bool IsInitialized => _initialized;

        public SecondOrderDynamicsFloat(SecondOrderDynamicsParameters parameters, float initialValue = 0f)
        {
            _xp = 0f;
            _y = 0f;
            _yd = 0f;
            _k1 = 0f;
            _k2 = 0f;
            _k3 = 0f;
            _initialized = false;
            Configure(parameters);
            Reset(initialValue);
        }

        public void Configure(SecondOrderDynamicsParameters parameters)
        {
            float pi = (float)Math.PI;
            float w = 2f * pi * parameters.FrequencyHz;
            _k1 = parameters.Damping / (pi * parameters.FrequencyHz);
            _k2 = 1f / (w * w);
            _k3 = (parameters.InitialResponse * parameters.Damping) / w;
        }

        public void Reset(float initialValue = 0f)
        {
            float safe = IsFinite(initialValue) ? initialValue : 0f;
            _xp = safe;
            _y = safe;
            _yd = 0f;
            _initialized = true;
        }

        public void Impulse(float valueOffset, float velocityKick = 0f)
        {
            if (!_initialized)
            {
                Reset(0f);
            }

            if (IsFinite(valueOffset))
            {
                _y += valueOffset;
            }
            if (IsFinite(velocityKick))
            {
                _yd += velocityKick;
            }
        }

        public void ClampIdleZero(float valueEpsilon, float velocityEpsilon)
        {
            if (Math.Abs(_y) <= valueEpsilon && Math.Abs(_yd) <= velocityEpsilon)
            {
                _y = 0f;
                _yd = 0f;
            }
        }

        public float Update(float dt, float target)
        {
            if (!IsFinite(target))
            {
                return _y;
            }

            if (!_initialized)
            {
                Reset(target);
                return _y;
            }

            if (!IsFinite(dt) || dt <= 1e-6f)
            {
                return _y;
            }

            float xd = (target - _xp) / dt;
            _xp = target;
            return StepInternal(dt, target, xd);
        }

        public float Update(float dt, float target, float targetVelocity)
        {
            if (!IsFinite(target) || !IsFinite(targetVelocity))
            {
                return _y;
            }

            if (!_initialized)
            {
                Reset(target);
                return _y;
            }

            if (!IsFinite(dt) || dt <= 1e-6f)
            {
                return _y;
            }

            _xp = target;
            return StepInternal(dt, target, targetVelocity);
        }

        private float StepInternal(float dt, float x, float xd)
        {
            float k2Stable = Math.Max(
                _k2,
                Math.Max((dt * dt * 0.5f) + (dt * _k1 * 0.5f), dt * _k1));
            _y = _y + dt * _yd;
            _yd = _yd + dt * (x + _k3 * xd - _y - _k1 * _yd) / k2Stable;
            return _y;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }

    /// <summary>
    /// Heap-free 3D vector second-order dynamics solver.
    /// </summary>
    public struct SecondOrderDynamicsVector3
    {
        private SecondOrderDynamicsFloat _x;
        private SecondOrderDynamicsFloat _y;
        private SecondOrderDynamicsFloat _z;

        public Vector3 Value => new Vector3(_x.Value, _y.Value, _z.Value);
        public Vector3 Velocity => new Vector3(_x.Velocity, _y.Velocity, _z.Velocity);
        public bool IsInitialized => _x.IsInitialized && _y.IsInitialized && _z.IsInitialized;

        public SecondOrderDynamicsVector3(SecondOrderDynamicsParameters parameters, Vector3 initialValue)
        {
            _x = new SecondOrderDynamicsFloat(parameters, initialValue.x);
            _y = new SecondOrderDynamicsFloat(parameters, initialValue.y);
            _z = new SecondOrderDynamicsFloat(parameters, initialValue.z);
        }

        public void Configure(SecondOrderDynamicsParameters parameters)
        {
            _x.Configure(parameters);
            _y.Configure(parameters);
            _z.Configure(parameters);
        }

        public void Reset(Vector3 initialValue)
        {
            _x.Reset(initialValue.x);
            _y.Reset(initialValue.y);
            _z.Reset(initialValue.z);
        }

        public Vector3 Update(float dt, Vector3 target)
        {
            return new Vector3(
                _x.Update(dt, target.x),
                _y.Update(dt, target.y),
                _z.Update(dt, target.z));
        }
    }
}
