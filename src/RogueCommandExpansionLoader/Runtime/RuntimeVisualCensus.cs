using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using RogueCommandExpansionLoader.Runtime.Visual;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Telemetry summary row capturing in-game render, rig, and animation evidence for one living unit.
    /// Pure, decoupled from Unity types to support clean-room MCP serialization and offline verification.
    /// </summary>
    public readonly struct VisualUnitDiagnosticRow
    {
        public VisualUnitDiagnosticRow(
            string entityKeyId,
            string entityId,
            bool isControlledByPlayer,
            string rigType,
            bool hasAnimator,
            bool isAnimatorActive,
            string activeClipName,
            float clipNormalizedTime,
            float clipSpeed,
            bool isMovingParam,
            bool fireTriggerParam,
            bool deathTriggerParam,
            int rendererCount,
            int totalVertexCount,
            string primaryShaderName,
            bool hasPbrKeywords,
            bool isGpuInstanced,
            bool isVisible,
            IReadOnlyList<string> discoveredSockets,
            int socketAmbiguityCount)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            IsControlledByPlayer = isControlledByPlayer;
            RigType = rigType ?? "StaticVehicle";
            HasAnimator = hasAnimator;
            IsAnimatorActive = isAnimatorActive;
            ActiveClipName = activeClipName ?? string.Empty;
            ClipNormalizedTime = clipNormalizedTime;
            ClipSpeed = clipSpeed;
            IsMovingParam = isMovingParam;
            FireTriggerParam = fireTriggerParam;
            DeathTriggerParam = deathTriggerParam;
            RendererCount = rendererCount;
            TotalVertexCount = totalVertexCount;
            PrimaryShaderName = primaryShaderName ?? string.Empty;
            HasPbrKeywords = hasPbrKeywords;
            IsGpuInstanced = isGpuInstanced;
            IsVisible = isVisible;
            DiscoveredSockets = discoveredSockets ?? new List<string>().AsReadOnly();
            SocketAmbiguityCount = socketAmbiguityCount;
        }

        public string EntityKeyId { get; }
        public string EntityId { get; }
        public bool IsControlledByPlayer { get; }
        public string RigType { get; }
        public bool HasAnimator { get; }
        public bool IsAnimatorActive { get; }
        public string ActiveClipName { get; }
        public float ClipNormalizedTime { get; }
        public float ClipSpeed { get; }
        public bool IsMovingParam { get; }
        public bool FireTriggerParam { get; }
        public bool DeathTriggerParam { get; }
        public int RendererCount { get; }
        public int TotalVertexCount { get; }
        public string PrimaryShaderName { get; }
        public bool HasPbrKeywords { get; }
        public bool IsGpuInstanced { get; }
        public bool IsVisible { get; }
        public IReadOnlyList<string> DiscoveredSockets { get; }
        public int SocketAmbiguityCount { get; }
    }

    /// <summary>
    /// Pure snapshot of in-game render, rig, and animation telemetry across living units.
    /// Omission counter placed strictly first to uphold clean-room bounded trace invariants.
    /// </summary>
    public sealed class VisualRenderCensus
    {
        public int OmittedUnitCount { get; }
        public int TotalTrackedUnits { get; }
        public int SkinnedMeshCount { get; }
        public int StaticMeshCount { get; }
        public int AnimatedUnitCount { get; }
        public int PbrShaderCount { get; }
        public IReadOnlyList<VisualUnitDiagnosticRow> UnitRows { get; }

        public VisualRenderCensus(
            int omittedUnitCount,
            int totalTrackedUnits,
            int skinnedMeshCount,
            int staticMeshCount,
            int animatedUnitCount,
            int pbrShaderCount,
            IReadOnlyList<VisualUnitDiagnosticRow> unitRows)
        {
            OmittedUnitCount = omittedUnitCount;
            TotalTrackedUnits = totalTrackedUnits;
            SkinnedMeshCount = skinnedMeshCount;
            StaticMeshCount = staticMeshCount;
            AnimatedUnitCount = animatedUnitCount;
            PbrShaderCount = pbrShaderCount;
            UnitRows = unitRows ?? new List<VisualUnitDiagnosticRow>().AsReadOnly();
        }

        public static VisualRenderCensus Empty { get; } =
            new VisualRenderCensus(0, 0, 0, 0, 0, 0, new List<VisualUnitDiagnosticRow>().AsReadOnly());
    }

    /// <summary>
    /// Pure diagnostic DTO for deep inspection of an individual renderer on a unit.
    /// </summary>
    public readonly struct VisualRendererDetailRow
    {
        public VisualRendererDetailRow(
            string name,
            string rendererType,
            bool enabled,
            bool isVisible,
            string meshName,
            int vertexCount,
            int submeshCount,
            IReadOnlyList<string> materialNames,
            IReadOnlyList<string> shaderNames,
            IReadOnlyList<string> shaderKeywords,
            bool isGpuInstanced)
        {
            Name = name ?? string.Empty;
            RendererType = rendererType ?? string.Empty;
            Enabled = enabled;
            IsVisible = isVisible;
            MeshName = meshName ?? string.Empty;
            VertexCount = vertexCount;
            SubmeshCount = submeshCount;
            MaterialNames = materialNames ?? new List<string>().AsReadOnly();
            ShaderNames = shaderNames ?? new List<string>().AsReadOnly();
            ShaderKeywords = shaderKeywords ?? new List<string>().AsReadOnly();
            IsGpuInstanced = isGpuInstanced;
        }

        public string Name { get; }
        public string RendererType { get; }
        public bool Enabled { get; }
        public bool IsVisible { get; }
        public string MeshName { get; }
        public int VertexCount { get; }
        public int SubmeshCount { get; }
        public IReadOnlyList<string> MaterialNames { get; }
        public IReadOnlyList<string> ShaderNames { get; }
        public IReadOnlyList<string> ShaderKeywords { get; }
        public bool IsGpuInstanced { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO for deep inspection of a canonical or custom socket anchor.
    /// </summary>
    public readonly struct VisualSocketDetailRow
    {
        public VisualSocketDetailRow(
            string socketName,
            string canonicalRole,
            float localPosX,
            float localPosY,
            float localPosZ,
            float localRotX,
            float localRotY,
            float localRotZ,
            bool isAmbiguous,
            int duplicateCount)
        {
            SocketName = socketName ?? string.Empty;
            CanonicalRole = canonicalRole ?? string.Empty;
            LocalPosX = localPosX;
            LocalPosY = localPosY;
            LocalPosZ = localPosZ;
            LocalRotX = localRotX;
            LocalRotY = localRotY;
            LocalRotZ = localRotZ;
            IsAmbiguous = isAmbiguous;
            DuplicateCount = duplicateCount;
        }

        public string SocketName { get; }
        public string CanonicalRole { get; }
        public float LocalPosX { get; }
        public float LocalPosY { get; }
        public float LocalPosZ { get; }
        public float LocalRotX { get; }
        public float LocalRotY { get; }
        public float LocalRotZ { get; }
        public bool IsAmbiguous { get; }
        public int DuplicateCount { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO for an animator state machine parameter.
    /// </summary>
    public readonly struct VisualAnimationParameterRow
    {
        public VisualAnimationParameterRow(string name, string parameterType, string value)
        {
            Name = name ?? string.Empty;
            ParameterType = parameterType ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Name { get; }
        public string ParameterType { get; }
        public string Value { get; }
    }

    /// <summary>
    /// Bounded runtime state for an optional native-to-visual rig pose bridge.
    /// </summary>
    public readonly struct VisualNativeRigRetargetState
    {
        public VisualNativeRigRetargetState(
            bool present,
            bool isBound,
            bool isSuspended,
            int configuredMappingCount,
            int boundMappingCount)
        {
            Present = present;
            IsBound = isBound;
            IsSuspended = isSuspended;
            ConfiguredMappingCount = Math.Max(0, configuredMappingCount);
            BoundMappingCount = Math.Max(0, boundMappingCount);
        }

        public bool Present { get; }
        public bool IsBound { get; }
        public bool IsSuspended { get; }
        public int ConfiguredMappingCount { get; }
        public int BoundMappingCount { get; }
    }

    /// <summary>
    /// Scalar local transform sample used by native rig retarget diagnostics; no Unity objects are retained.
    /// </summary>
    public readonly struct VisualRetargetLocalPoseRow
    {
        public VisualRetargetLocalPoseRow(Vector3 position, Quaternion rotation)
        {
            PositionX = position.x;
            PositionY = position.y;
            PositionZ = position.z;
            RotationX = rotation.x;
            RotationY = rotation.y;
            RotationZ = rotation.z;
            RotationW = rotation.w;
        }

        public float PositionX { get; }
        public float PositionY { get; }
        public float PositionZ { get; }
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }
        public float RotationW { get; }
    }

    /// <summary>
    /// Bounded source, target, and computed pose evidence for one mapped retarget transform.
    /// </summary>
    public readonly struct VisualNativeRigRetargetMappingRow
    {
        public VisualNativeRigRetargetMappingRow(
            string sourcePath,
            string targetPath,
            bool sourceAvailable,
            bool targetAvailable,
            VisualRetargetLocalPoseRow sourcePose,
            VisualRetargetLocalPoseRow targetPose,
            VisualRetargetLocalPoseRow expectedTargetPose)
        {
            SourcePath = sourcePath ?? string.Empty;
            TargetPath = targetPath ?? string.Empty;
            SourceAvailable = sourceAvailable;
            TargetAvailable = targetAvailable;
            SourcePose = sourcePose;
            TargetPose = targetPose;
            ExpectedTargetPose = expectedTargetPose;
        }

        public string SourcePath { get; }
        public string TargetPath { get; }
        public bool SourceAvailable { get; }
        public bool TargetAvailable { get; }
        public VisualRetargetLocalPoseRow SourcePose { get; }
        public VisualRetargetLocalPoseRow TargetPose { get; }
        public VisualRetargetLocalPoseRow ExpectedTargetPose { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO containing deep visual render, rig, and animation evidence for a specific entity.
    /// </summary>
    public sealed class DetailedEntityVisualDiagnostic
    {
        public bool Success { get; }
        public string EntityKeyId { get; }
        public string EntityId { get; }
        public string Message { get; }

        // Render Evidence
        public int RendererCount { get; }
        public int TotalVertexCount { get; }
        public int SubmeshCount { get; }
        public float BoundsSizeX { get; }
        public float BoundsSizeY { get; }
        public float BoundsSizeZ { get; }
        public bool HasLODGroup { get; }
        public int LODCount { get; }
        public IReadOnlyList<VisualRendererDetailRow> Renderers { get; }

        // Rig & Hierarchy Evidence
        public string RigType { get; }
        public string RootBoneName { get; }
        public int BoneCount { get; }
        public IReadOnlyList<string> BoneNames { get; }
        public IReadOnlyList<VisualSocketDetailRow> Sockets { get; }

        // Animation Evidence
        public bool HasAnimator { get; }
        public bool IsAnimatorActive { get; }
        public string ControllerName { get; }
        public string AvatarName { get; }
        public bool AvatarIsValid { get; }
        public string CurrentStateName { get; }
        public string ActiveClipName { get; }
        public float ClipLength { get; }
        public float NormalizedTime { get; }
        public float Speed { get; }
        public IReadOnlyList<VisualAnimationParameterRow> Parameters { get; }
        public VisualNativeRigRetargetState NativeRigRetarget { get; }
        public int NativeRigRetargetOmittedMappingCount { get; }
        public IReadOnlyList<VisualNativeRigRetargetMappingRow> NativeRigRetargetMappings { get; }

        // Visual Director Evidence
        public bool HasVisualDirector { get; }
        public bool IsDamagedVfxActive { get; }
        public bool IsCriticalVfxActive { get; }
        public bool IsTurretJammed { get; }
        public float RecoilOffset { get; }
        public float DoorOpenProgress { get; }

        public DetailedEntityVisualDiagnostic(
            bool success,
            string entityKeyId,
            string entityId,
            string message,
            int rendererCount,
            int totalVertexCount,
            int submeshCount,
            float boundsSizeX,
            float boundsSizeY,
            float boundsSizeZ,
            bool hasLodGroup,
            int lodCount,
            IReadOnlyList<VisualRendererDetailRow> renderers,
            string rigType,
            string rootBoneName,
            int boneCount,
            IReadOnlyList<string> boneNames,
            IReadOnlyList<VisualSocketDetailRow> sockets,
            bool hasAnimator,
            bool isAnimatorActive,
            string controllerName,
            string avatarName,
            bool avatarIsValid,
            string currentStateName,
            string activeClipName,
            float clipLength,
            float normalizedTime,
            float speed,
            IReadOnlyList<VisualAnimationParameterRow> parameters,
            bool hasVisualDirector,
            bool isDamagedVfxActive,
            bool isCriticalVfxActive,
            bool isTurretJammed,
            float recoilOffset,
            float doorOpenProgress,
            VisualNativeRigRetargetState nativeRigRetarget = default(VisualNativeRigRetargetState),
            int nativeRigRetargetOmittedMappingCount = 0,
            IReadOnlyList<VisualNativeRigRetargetMappingRow> nativeRigRetargetMappings = null)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            Message = message ?? string.Empty;
            RendererCount = rendererCount;
            TotalVertexCount = totalVertexCount;
            SubmeshCount = submeshCount;
            BoundsSizeX = boundsSizeX;
            BoundsSizeY = boundsSizeY;
            BoundsSizeZ = boundsSizeZ;
            HasLODGroup = hasLodGroup;
            LODCount = lodCount;
            Renderers = renderers ?? new List<VisualRendererDetailRow>().AsReadOnly();
            RigType = rigType ?? "StaticVehicle";
            RootBoneName = rootBoneName ?? string.Empty;
            BoneCount = boneCount;
            BoneNames = boneNames ?? new List<string>().AsReadOnly();
            Sockets = sockets ?? new List<VisualSocketDetailRow>().AsReadOnly();
            HasAnimator = hasAnimator;
            IsAnimatorActive = isAnimatorActive;
            ControllerName = controllerName ?? string.Empty;
            AvatarName = avatarName ?? string.Empty;
            AvatarIsValid = avatarIsValid;
            CurrentStateName = currentStateName ?? string.Empty;
            ActiveClipName = activeClipName ?? string.Empty;
            ClipLength = clipLength;
            NormalizedTime = normalizedTime;
            Speed = speed;
            Parameters = parameters ?? new List<VisualAnimationParameterRow>().AsReadOnly();
            NativeRigRetarget = nativeRigRetarget;
            NativeRigRetargetOmittedMappingCount = Math.Max(0, nativeRigRetargetOmittedMappingCount);
            NativeRigRetargetMappings = nativeRigRetargetMappings ?? new List<VisualNativeRigRetargetMappingRow>().AsReadOnly();
            HasVisualDirector = hasVisualDirector;
            IsDamagedVfxActive = isDamagedVfxActive;
            IsCriticalVfxActive = isCriticalVfxActive;
            IsTurretJammed = isTurretJammed;
            RecoilOffset = recoilOffset;
            DoorOpenProgress = doorOpenProgress;
        }

        public static DetailedEntityVisualDiagnostic Failed(string entityKeyId, string message) =>
            new DetailedEntityVisualDiagnostic(
                false, entityKeyId, string.Empty, message,
                0, 0, 0, 0f, 0f, 0f, false, 0, new List<VisualRendererDetailRow>().AsReadOnly(),
                "None", string.Empty, 0, new List<string>().AsReadOnly(),
                new List<VisualSocketDetailRow>().AsReadOnly(),
                false, false, string.Empty, string.Empty, false,
                string.Empty, string.Empty, 0f, 0f, 0f,
                new List<VisualAnimationParameterRow>().AsReadOnly(),
                false, false, false, false, 0f, 0f);
    }

    /// <summary>
    /// Pure DTO capturing load-time visual, rig, shader, and socket audit telemetry for a unit/building prefab.
    /// </summary>
    public readonly struct LoadedPrefabVisualAuditRow
    {
        public LoadedPrefabVisualAuditRow(
            string entityId,
            string prefabName,
            string rigType,
            string rootBoneName,
            int boneCount,
            int rendererCount,
            int totalVertexCount,
            int submeshCount,
            string primaryShaderName,
            bool hasPbrKeywords,
            bool isGpuInstanced,
            bool hasAnimator,
            bool avatarIsValid,
            IReadOnlyList<string> discoveredSockets,
            int socketAmbiguityCount,
            bool hasMuzzlePrimary,
            bool hasFirePoint0,
            bool hasWeaponAudio,
            bool hasEngineAudio,
            bool hasDamageSmoke)
        {
            EntityId = entityId ?? string.Empty;
            PrefabName = prefabName ?? string.Empty;
            RigType = rigType ?? "StaticVehicle";
            RootBoneName = rootBoneName ?? string.Empty;
            BoneCount = boneCount;
            RendererCount = rendererCount;
            TotalVertexCount = totalVertexCount;
            SubmeshCount = submeshCount;
            PrimaryShaderName = primaryShaderName ?? string.Empty;
            HasPbrKeywords = hasPbrKeywords;
            IsGpuInstanced = isGpuInstanced;
            HasAnimator = hasAnimator;
            AvatarIsValid = avatarIsValid;
            DiscoveredSockets = discoveredSockets ?? new List<string>().AsReadOnly();
            SocketAmbiguityCount = socketAmbiguityCount;
            HasMuzzlePrimary = hasMuzzlePrimary;
            HasFirePoint0 = hasFirePoint0;
            HasWeaponAudio = hasWeaponAudio;
            HasEngineAudio = hasEngineAudio;
            HasDamageSmoke = hasDamageSmoke;
        }

        public string EntityId { get; }
        public string PrefabName { get; }
        public string RigType { get; }
        public string RootBoneName { get; }
        public int BoneCount { get; }
        public int RendererCount { get; }
        public int TotalVertexCount { get; }
        public int SubmeshCount { get; }
        public string PrimaryShaderName { get; }
        public bool HasPbrKeywords { get; }
        public bool IsGpuInstanced { get; }
        public bool HasAnimator { get; }
        public bool AvatarIsValid { get; }
        public IReadOnlyList<string> DiscoveredSockets { get; }
        public int SocketAmbiguityCount { get; }
        public bool HasMuzzlePrimary { get; }
        public bool HasFirePoint0 { get; }
        public bool HasWeaponAudio { get; }
        public bool HasEngineAudio { get; }
        public bool HasDamageSmoke { get; }
    }

    /// <summary>
    /// Pure snapshot of load-time visual audits across all unit and building prefabs registered by the loader.
    /// Omission counter placed strictly first to uphold clean-room bounded trace invariants.
    /// </summary>
    public sealed class LoadedPrefabVisualAuditCensus
    {
        public int OmittedUnitCount { get; }
        public int TotalAuditedPrefabs { get; }
        public int SkinnedPrefabCount { get; }
        public int AnimatedPrefabCount { get; }
        public int PbrPrefabCount { get; }
        public int AmbiguousSocketPrefabCount { get; }
        public IReadOnlyList<LoadedPrefabVisualAuditRow> PrefabRows { get; }

        public LoadedPrefabVisualAuditCensus(
            int omittedUnitCount,
            int totalAuditedPrefabs,
            int skinnedPrefabCount,
            int animatedPrefabCount,
            int pbrPrefabCount,
            int ambiguousSocketPrefabCount,
            IReadOnlyList<LoadedPrefabVisualAuditRow> prefabRows)
        {
            OmittedUnitCount = omittedUnitCount;
            TotalAuditedPrefabs = totalAuditedPrefabs;
            SkinnedPrefabCount = skinnedPrefabCount;
            AnimatedPrefabCount = animatedPrefabCount;
            PbrPrefabCount = pbrPrefabCount;
            AmbiguousSocketPrefabCount = ambiguousSocketPrefabCount;
            PrefabRows = prefabRows ?? new List<LoadedPrefabVisualAuditRow>().AsReadOnly();
        }

        public static LoadedPrefabVisualAuditCensus Empty { get; } =
            new LoadedPrefabVisualAuditCensus(0, 0, 0, 0, 0, 0, new List<LoadedPrefabVisualAuditRow>().AsReadOnly());
    }

    /// <summary>
    /// Reads living entities from <see cref="ExistingControllers"/> and extracts observable
    /// runtime evidence of geometry rendering, skeletal/transform rigging, socket bindings,
    /// and animator locomotion states, plus load-time prefab visual audits.
    /// </summary>
    public static class RuntimeVisualCensus
    {
        private const string PbrShaderName = "RogueCommand/VehiclePBR_VertexWear";
        private const string MovementAnimationParameter = "RCL_IsMoving";
        private const string FireAnimationParameter = "RCL_Fire";
        private const string DeathAnimationParameter = "RCL_Death";

        private static readonly object PrefabAuditGate = new object();
        private static readonly Dictionary<string, LoadedPrefabVisualAuditRow> AuditedPrefabsById =
            new Dictionary<string, LoadedPrefabVisualAuditRow>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GameObject> LoadedPrefabObjectsById =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        public static int AuditedPrefabCount
        {
            get
            {
                lock (PrefabAuditGate)
                {
                    return AuditedPrefabsById.Count;
                }
            }
        }

        public static void ClearLoadedPrefabAudits()
        {
            lock (PrefabAuditGate)
            {
                AuditedPrefabsById.Clear();
                LoadedPrefabObjectsById.Clear();
            }
        }

        public static bool TryGetLoadedPrefabAudit(string entityId, out LoadedPrefabVisualAuditRow row)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                row = default(LoadedPrefabVisualAuditRow);
                return false;
            }

            lock (PrefabAuditGate)
            {
                return AuditedPrefabsById.TryGetValue(entityId.Trim(), out row);
            }
        }

        public static LoadedPrefabVisualAuditRow RegisterLoadedPrefabVisualAudit(string entityId, GameObject prefabRoot)
        {
            if (string.IsNullOrWhiteSpace(entityId) || prefabRoot == null)
            {
                return default(LoadedPrefabVisualAuditRow);
            }

            string normalizedId = entityId.Trim();
            DetailedEntityVisualDiagnostic detailed = InspectGameObjectHierarchy(
                prefabRoot,
                "prefab:" + normalizedId,
                normalizedId,
                "Loaded prefab visual audit.");

            bool hasPbrKeywords = false;
            bool isInstanced = false;
            string primaryShader = string.Empty;
            if (detailed.Renderers != null)
            {
                for (int i = 0; i < detailed.Renderers.Count; i++)
                {
                    VisualRendererDetailRow r = detailed.Renderers[i];
                    if (r.IsGpuInstanced) isInstanced = true;
                    if (r.ShaderNames != null)
                    {
                        for (int s = 0; s < r.ShaderNames.Count; s++)
                        {
                            string sName = r.ShaderNames[s];
                            if (string.IsNullOrEmpty(primaryShader) && !string.IsNullOrEmpty(sName))
                            {
                                primaryShader = sName;
                            }
                            if (sName != null && (sName.IndexOf(PbrShaderName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                sName.IndexOf("VehiclePBR", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                primaryShader = sName;
                            }
                        }
                    }
                    if (r.ShaderKeywords != null)
                    {
                        for (int k = 0; k < r.ShaderKeywords.Count; k++)
                        {
                            string kw = r.ShaderKeywords[k];
                            if (string.Equals(kw, "_METALLICSPECGLOSSMAP", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(kw, "_NORMALMAP", StringComparison.OrdinalIgnoreCase))
                            {
                                hasPbrKeywords = true;
                            }
                        }
                    }
                }
            }

            var socketNames = new List<string>();
            int ambiguityCount = 0;
            bool hasMuzzlePrimary = false;
            bool hasFirePoint0 = false;
            bool hasWeaponAudio = false;
            bool hasEngineAudio = false;
            bool hasDamageSmoke = false;

            if (detailed.Sockets != null)
            {
                for (int i = 0; i < detailed.Sockets.Count; i++)
                {
                    VisualSocketDetailRow s = detailed.Sockets[i];
                    string role = !string.IsNullOrEmpty(s.CanonicalRole) ? s.CanonicalRole : s.SocketName;
                    if (!string.IsNullOrEmpty(role) && !socketNames.Contains(role))
                    {
                        socketNames.Add(role);
                    }
                    if (s.IsAmbiguous)
                    {
                        ambiguityCount++;
                    }
                    if (string.Equals(role, "Socket_MuzzlePrimary", StringComparison.OrdinalIgnoreCase)) hasMuzzlePrimary = true;
                    if (string.Equals(role, "FirePoint_0", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.SocketName, "FirePoint_0", StringComparison.OrdinalIgnoreCase)) hasFirePoint0 = true;
                    if (string.Equals(role, "Socket_WeaponAudio", StringComparison.OrdinalIgnoreCase)) hasWeaponAudio = true;
                    if (string.Equals(role, "Socket_EngineAudio", StringComparison.OrdinalIgnoreCase)) hasEngineAudio = true;
                    if (string.Equals(role, "Socket_DamageSmoke", StringComparison.OrdinalIgnoreCase)) hasDamageSmoke = true;
                }
            }

            var row = new LoadedPrefabVisualAuditRow(
                normalizedId,
                prefabRoot.name ?? string.Empty,
                detailed.RigType,
                detailed.RootBoneName,
                detailed.BoneCount,
                detailed.RendererCount,
                detailed.TotalVertexCount,
                detailed.SubmeshCount,
                primaryShader,
                hasPbrKeywords,
                isInstanced,
                detailed.HasAnimator,
                detailed.AvatarIsValid,
                socketNames.AsReadOnly(),
                ambiguityCount,
                hasMuzzlePrimary,
                hasFirePoint0,
                hasWeaponAudio,
                hasEngineAudio,
                hasDamageSmoke);

            lock (PrefabAuditGate)
            {
                AuditedPrefabsById[normalizedId] = row;
                LoadedPrefabObjectsById[normalizedId] = prefabRoot;
            }

            return row;
        }

        public static LoadedPrefabVisualAuditCensus GetLoadedPrefabAuditCensus(int maxEntries = 64)
        {
            int cap = maxEntries < 1 ? 1 : (maxEntries > 256 ? 256 : maxEntries);
            List<LoadedPrefabVisualAuditRow> allRows;
            lock (PrefabAuditGate)
            {
                allRows = new List<LoadedPrefabVisualAuditRow>(AuditedPrefabsById.Values);
            }

            allRows.Sort(delegate(LoadedPrefabVisualAuditRow a, LoadedPrefabVisualAuditRow b)
            {
                return string.Compare(a.EntityId, b.EntityId, StringComparison.Ordinal);
            });

            int total = allRows.Count;
            int skinnedCount = 0;
            int animatedCount = 0;
            int pbrCount = 0;
            int ambiguousCount = 0;
            var rows = new List<LoadedPrefabVisualAuditRow>();

            for (int i = 0; i < allRows.Count; i++)
            {
                LoadedPrefabVisualAuditRow r = allRows[i];
                if (string.Equals(r.RigType, "SkeletalArmature", StringComparison.OrdinalIgnoreCase))
                {
                    skinnedCount++;
                }
                if (r.HasAnimator)
                {
                    animatedCount++;
                }
                if (r.HasPbrKeywords ||
                    (r.PrimaryShaderName != null && r.PrimaryShaderName.IndexOf("VehiclePBR", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    pbrCount++;
                }
                if (r.SocketAmbiguityCount > 0)
                {
                    ambiguousCount++;
                }
                if (rows.Count < cap)
                {
                    rows.Add(r);
                }
            }

            int omitted = total > rows.Count ? total - rows.Count : 0;
            return new LoadedPrefabVisualAuditCensus(
                omitted,
                total,
                skinnedCount,
                animatedCount,
                pbrCount,
                ambiguousCount,
                rows.AsReadOnly());
        }

        public static VisualRenderCensus Current(int maxTrackedUnits = 32)
        {
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return VisualRenderCensus.Empty;
                }

                var candidates = new List<EntityController>();
                HashSet<EntityController> playerUnits = existing.PlayerUnits();
                if (playerUnits != null) candidates.AddRange(playerUnits);

                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits != null) candidates.AddRange(aiUnits);

                int totalCount = 0;
                int skinnedCount = 0;
                int staticMeshCount = 0;
                int animatedCount = 0;
                int pbrShaderCount = 0;

                var unitRows = new List<VisualUnitDiagnosticRow>();

                for (int i = 0; i < candidates.Count; i++)
                {
                    EntityController ec = candidates[i];
                    if (ec == null || ec.IsDestroyed || ec.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    totalCount++;
                    GameObject go = ec.gameObject;
                    if (go == null)
                    {
                        continue;
                    }

                    // Renderer inspection
                    Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                    int rendererCount = renderers != null ? renderers.Length : 0;
                    int totalVertexCount = 0;
                    bool hasSkinned = false;
                    bool hasStatic = false;
                    bool hasPbr = false;
                    bool isVisible = false;
                    bool isInstanced = false;
                    string primaryShader = string.Empty;

                    if (renderers != null)
                    {
                        for (int rIdx = 0; rIdx < renderers.Length; rIdx++)
                        {
                            Renderer r = renderers[rIdx];
                            if (r == null) continue;

                            if (r.isVisible) isVisible = true;

                            if (r is SkinnedMeshRenderer smr)
                            {
                                hasSkinned = true;
                                if (smr.sharedMesh != null)
                                {
                                    totalVertexCount += smr.sharedMesh.vertexCount;
                                }
                            }
                            else if (r is MeshRenderer)
                            {
                                hasStatic = true;
                                MeshFilter mf = r.GetComponent<MeshFilter>();
                                if (mf != null && mf.sharedMesh != null)
                                {
                                    totalVertexCount += mf.sharedMesh.vertexCount;
                                }
                            }

                            Material[] mats = r.sharedMaterials;
                            if (mats != null)
                            {
                                for (int mIdx = 0; mIdx < mats.Length; mIdx++)
                                {
                                    Material mat = mats[mIdx];
                                    if (mat == null) continue;

                                    if (mat.enableInstancing) isInstanced = true;

                                    string shaderName = mat.shader != null ? mat.shader.name : string.Empty;
                                    if (string.IsNullOrEmpty(primaryShader) && !string.IsNullOrEmpty(shaderName))
                                    {
                                        primaryShader = shaderName;
                                    }

                                    if (string.Equals(shaderName, PbrShaderName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        hasPbr = true;
                                        primaryShader = PbrShaderName;
                                    }
                                }
                            }
                        }
                    }

                    if (hasSkinned) skinnedCount++;
                    if (hasStatic && !hasSkinned) staticMeshCount++;
                    if (hasPbr) pbrShaderCount++;

                    // Rig classification
                    string rigType = ClassifyRig(go, renderers);

                    // Sockets inspection
                    var discoveredSockets = new List<string>();
                    int socketAmbiguities = 0;
                    InspectSockets(go, discoveredSockets, out socketAmbiguities);

                    // Animator inspection
                    Animator animator = ResolvePreferredAnimator(go);
                    bool hasAnimator = animator != null;
                    bool isAnimatorActive = hasAnimator && animator.isActiveAndEnabled;
                    string activeClipName = string.Empty;
                    float clipNormalizedTime = 0f;
                    float clipSpeed = 1f;
                    bool isMovingParam = false;
                    bool fireTriggerParam = false;
                    bool deathTriggerParam = false;

                    if (hasAnimator)
                    {
                        animatedCount++;
                        try
                        {
                            clipSpeed = animator.speed;
                            AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
                            if (clips != null && clips.Length > 0 && clips[0].clip != null)
                            {
                                activeClipName = clips[0].clip.name;
                            }

                            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                            clipNormalizedTime = stateInfo.normalizedTime;

                            isMovingParam = animator.GetBool(MovementAnimationParameter);
                            fireTriggerParam = animator.GetBool(FireAnimationParameter);
                            deathTriggerParam = animator.GetBool(DeathAnimationParameter);
                        }
                        catch
                        {
                            // Fail-safe degradation
                        }
                    }

                    if (unitRows.Count < maxTrackedUnits)
                    {
                        string entityKey = ec.UniqueEntityId > 0 ? ec.UniqueEntityId.ToString() : (ec.EntityId ?? go.name);
                        unitRows.Add(new VisualUnitDiagnosticRow(
                            entityKey,
                            ec.EntityId ?? string.Empty,
                            ec.IsControlledByPlayer,
                            rigType,
                            hasAnimator,
                            isAnimatorActive,
                            activeClipName,
                            clipNormalizedTime,
                            clipSpeed,
                            isMovingParam,
                            fireTriggerParam,
                            deathTriggerParam,
                            rendererCount,
                            totalVertexCount,
                            primaryShader,
                            hasPbr,
                            isInstanced,
                            isVisible,
                            discoveredSockets.AsReadOnly(),
                            socketAmbiguities));
                    }
                }

                int omittedCount = Math.Max(0, totalCount - unitRows.Count);
                return new VisualRenderCensus(
                    omittedCount,
                    totalCount,
                    skinnedCount,
                    staticMeshCount,
                    animatedCount,
                    pbrShaderCount,
                    unitRows.AsReadOnly());
            }
            catch
            {
                return VisualRenderCensus.Empty;
            }
        }

        public static DetailedEntityVisualDiagnostic InspectEntity(string entityKeyId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return DetailedEntityVisualDiagnostic.Failed(entityKeyId, "Entity key ID must not be empty.");
            }

            try
            {
                string trimmedKey = entityKeyId.Trim();
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null)
                {
                    EntityController entity = FindEntityByKey(existing, trimmedKey);
                    if (entity != null && entity.gameObject != null)
                    {
                        return InspectGameObjectHierarchy(
                            entity.gameObject,
                            entityKeyId,
                            entity.EntityId ?? string.Empty,
                            "Visual evidence successfully collected.");
                    }
                }

                GameObject prefabObj = null;
                lock (PrefabAuditGate)
                {
                    LoadedPrefabObjectsById.TryGetValue(trimmedKey, out prefabObj);
                }

                if (prefabObj != null)
                {
                    return InspectGameObjectHierarchy(
                        prefabObj,
                        "prefab:" + trimmedKey,
                        trimmedKey,
                        "Visual evidence collected from loaded prefab template.");
                }

                if (existing == null)
                {
                    return DetailedEntityVisualDiagnostic.Failed(entityKeyId, "ExistingControllers registry unavailable.");
                }

                return DetailedEntityVisualDiagnostic.Failed(entityKeyId, "Entity '" + entityKeyId + "' not found on battlefield.");
            }
            catch (Exception ex)
            {
                return DetailedEntityVisualDiagnostic.Failed(entityKeyId, "Inspection error: " + ex.Message);
            }
        }

        private static DetailedEntityVisualDiagnostic InspectGameObjectHierarchy(
            GameObject go,
            string entityKeyId,
            string entityId,
            string message)
        {
            if (go == null)
            {
                return DetailedEntityVisualDiagnostic.Failed(entityKeyId, "Target GameObject is null.");
            }

            try
            {
                // 1. Render Details
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                var rendererRows = new List<VisualRendererDetailRow>();
                int totalVertexCount = 0;
                int totalSubmeshCount = 0;
                float maxBoundsX = 0f;
                float maxBoundsY = 0f;
                float maxBoundsZ = 0f;

                if (renderers != null)
                {
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Renderer r = renderers[i];
                        if (r == null) continue;

                        string rName = r.name ?? string.Empty;
                        string rType = r.GetType().Name;
                        bool enabled = r.enabled;
                        bool isVisible = r.isVisible;
                        string meshName = string.Empty;
                        int vCount = 0;
                        int sCount = 0;

                        if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                        {
                            meshName = smr.sharedMesh.name;
                            vCount = smr.sharedMesh.vertexCount;
                            sCount = smr.sharedMesh.subMeshCount;
                        }
                        else if (r is MeshRenderer)
                        {
                            MeshFilter mf = r.GetComponent<MeshFilter>();
                            if (mf != null && mf.sharedMesh != null)
                            {
                                meshName = mf.sharedMesh.name;
                                vCount = mf.sharedMesh.vertexCount;
                                sCount = mf.sharedMesh.subMeshCount;
                            }
                        }

                        totalVertexCount += vCount;
                        totalSubmeshCount += sCount;

                        Bounds b = r.bounds;
                        if (b.size.x > maxBoundsX) maxBoundsX = b.size.x;
                        if (b.size.y > maxBoundsY) maxBoundsY = b.size.y;
                        if (b.size.z > maxBoundsZ) maxBoundsZ = b.size.z;

                        var matNames = new List<string>();
                        var shaderNames = new List<string>();
                        var keywordList = new List<string>();
                        bool isInstanced = false;

                        Material[] mats = r.sharedMaterials;
                        if (mats != null)
                        {
                            for (int mIdx = 0; mIdx < mats.Length; mIdx++)
                            {
                                Material mat = mats[mIdx];
                                if (mat == null) continue;
                                if (!string.IsNullOrEmpty(mat.name)) matNames.Add(mat.name);
                                if (mat.shader != null && !string.IsNullOrEmpty(mat.shader.name))
                                {
                                    shaderNames.Add(mat.shader.name);
                                }
                                if (mat.enableInstancing) isInstanced = true;
                                if (mat.shaderKeywords != null)
                                {
                                    for (int k = 0; k < mat.shaderKeywords.Length; k++)
                                    {
                                        string kw = mat.shaderKeywords[k];
                                        if (!string.IsNullOrEmpty(kw) && !keywordList.Contains(kw))
                                        {
                                            keywordList.Add(kw);
                                        }
                                    }
                                }
                            }
                        }

                        rendererRows.Add(new VisualRendererDetailRow(
                            rName,
                            rType,
                            enabled,
                            isVisible,
                            meshName,
                            vCount,
                            sCount,
                            matNames.AsReadOnly(),
                            shaderNames.AsReadOnly(),
                            keywordList.AsReadOnly(),
                            isInstanced));
                    }
                }

                LODGroup lod = go.GetComponentInChildren<LODGroup>(true);
                bool hasLod = lod != null;
                int lodCount = hasLod ? lod.lodCount : 0;

                // 2. Rig & Socket Details
                string rigType = ClassifyRig(go, renderers);
                string rootBoneName = string.Empty;
                var boneNames = new List<string>();
                InspectBones(go, renderers, out rootBoneName, boneNames);

                var socketRows = new List<VisualSocketDetailRow>();
                InspectDetailedSockets(go, socketRows);

                // 3. Animation Details
                Animator animator = ResolvePreferredAnimator(go);
                bool hasAnimator = animator != null;
                bool isAnimatorActive = hasAnimator && animator.isActiveAndEnabled;
                string controllerName = string.Empty;
                string avatarName = string.Empty;
                bool avatarValid = false;
                string currentStateName = string.Empty;
                string activeClipName = string.Empty;
                float clipLength = 0f;
                float normalizedTime = 0f;
                float speed = 1f;
                var paramRows = new List<VisualAnimationParameterRow>();

                if (hasAnimator)
                {
                    try
                    {
                        if (animator.runtimeAnimatorController != null)
                        {
                            controllerName = animator.runtimeAnimatorController.name ?? string.Empty;
                        }

                        if (animator.avatar != null)
                        {
                            avatarName = animator.avatar.name ?? string.Empty;
                            avatarValid = animator.avatar.isValid;
                        }

                        speed = animator.speed;

                        AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
                        if (clips != null && clips.Length > 0 && clips[0].clip != null)
                        {
                            activeClipName = clips[0].clip.name ?? string.Empty;
                            clipLength = clips[0].clip.length;
                        }

                        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                        normalizedTime = stateInfo.normalizedTime;

                        if (animator.parameters != null)
                        {
                            for (int p = 0; p < animator.parameters.Length; p++)
                            {
                                AnimatorControllerParameter param = animator.parameters[p];
                                if (param == null || string.IsNullOrEmpty(param.name)) continue;

                                string pType = param.type.ToString();
                                string valStr = string.Empty;
                                if (param.type == AnimatorControllerParameterType.Bool)
                                {
                                    valStr = animator.GetBool(param.name) ? "true" : "false";
                                }
                                else if (param.type == AnimatorControllerParameterType.Float)
                                {
                                    valStr = animator.GetFloat(param.name).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                else if (param.type == AnimatorControllerParameterType.Int)
                                {
                                    valStr = animator.GetInteger(param.name).ToString();
                                }
                                else if (param.type == AnimatorControllerParameterType.Trigger)
                                {
                                    valStr = animator.GetBool(param.name) ? "triggered" : "idle";
                                }

                                paramRows.Add(new VisualAnimationParameterRow(param.name, pType, valStr));
                            }
                        }
                    }
                    catch
                    {
                        // Fail-safe degradation
                    }
                }

                NativeRigRetargetProfile retargetProfile = go.GetComponent<NativeRigRetargetProfile>();
                VisualNativeRigRetargetState nativeRigRetargetState = retargetProfile == null
                    ? default(VisualNativeRigRetargetState)
                    : new VisualNativeRigRetargetState(
                        true,
                        retargetProfile.IsBound,
                        retargetProfile.IsSuspended,
                        retargetProfile.ConfiguredMappingCount,
                        retargetProfile.BoundMappingCount);
                var nativeRetargetMappings = new List<VisualNativeRigRetargetMappingRow>();
                int omittedNativeRetargetMappingCount = 0;
                if (retargetProfile != null)
                {
                    var nativeMappingSamples = new List<NativeRigRetargetMappingSample>();
                    omittedNativeRetargetMappingCount = retargetProfile.CopyDiagnosticMappingSamples(
                        nativeMappingSamples,
                        NativeRigRetargetProfile.MaxDiagnosticMappingSamples);
                    for (int i = 0; i < nativeMappingSamples.Count; i++)
                    {
                        NativeRigRetargetMappingSample sample = nativeMappingSamples[i];
                        nativeRetargetMappings.Add(new VisualNativeRigRetargetMappingRow(
                            sample.SourcePath,
                            sample.TargetPath,
                            sample.SourceAvailable,
                            sample.TargetAvailable,
                            new VisualRetargetLocalPoseRow(sample.SourceLocalPosition, sample.SourceLocalRotation),
                            new VisualRetargetLocalPoseRow(sample.TargetLocalPosition, sample.TargetLocalRotation),
                            new VisualRetargetLocalPoseRow(sample.ExpectedTargetLocalPosition, sample.ExpectedTargetLocalRotation)));
                    }
                }

                // 4. RuntimeVisualDirector Details
                bool hasDirector;
                bool isDamaged;
                bool isCritical;
                bool isJammed;
                float recoil;
                float doorProgress;
                InspectVisualDirector(go, out hasDirector, out isDamaged, out isCritical, out isJammed, out recoil, out doorProgress);

                return new DetailedEntityVisualDiagnostic(
                    true,
                    entityKeyId,
                    entityId ?? string.Empty,
                    message ?? "Visual evidence successfully collected.",
                    renderers != null ? renderers.Length : 0,
                    totalVertexCount,
                    totalSubmeshCount,
                    maxBoundsX,
                    maxBoundsY,
                    maxBoundsZ,
                    hasLod,
                    lodCount,
                    rendererRows.AsReadOnly(),
                    rigType,
                    rootBoneName,
                    boneNames.Count,
                    boneNames.AsReadOnly(),
                    socketRows.AsReadOnly(),
                    hasAnimator,
                    isAnimatorActive,
                    controllerName,
                    avatarName,
                    avatarValid,
                    currentStateName,
                    activeClipName,
                    clipLength,
                    normalizedTime,
                    speed,
                    paramRows.AsReadOnly(),
                    hasDirector,
                    isDamaged,
                    isCritical,
                    isJammed,
                    recoil,
                    doorProgress,
                    nativeRigRetargetState,
                    omittedNativeRetargetMappingCount,
                    nativeRetargetMappings.AsReadOnly());
            }
            catch (Exception ex)
            {
                return DetailedEntityVisualDiagnostic.Failed(entityKeyId, "Inspection error: " + ex.Message);
            }
        }

        private static string ClassifyRig(GameObject go, Renderer[] renderers)
        {
            if (go == null) return "StaticVehicle";

            // 1. SkeletalArmature check: has SkinnedMeshRenderer with bones or contains Bone_* transforms
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] is SkinnedMeshRenderer smr)
                    {
                        if (smr.bones != null && smr.bones.Length > 0)
                        {
                            return "SkeletalArmature";
                        }
                    }
                }
            }

            Transform[] allTransforms = go.GetComponentsInChildren<Transform>(true);
            if (allTransforms != null)
            {
                bool hasRotHor = false;
                bool hasRotVert = false;
                bool hasLegs = false;

                for (int i = 0; i < allTransforms.Length; i++)
                {
                    Transform t = allTransforms[i];
                    if (t == null) continue;
                    string name = t.name ?? string.Empty;

                    if (name.StartsWith("Bone_", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Armature", StringComparison.OrdinalIgnoreCase))
                    {
                        return "SkeletalArmature";
                    }

                    if (name.IndexOf("RotHor", StringComparison.OrdinalIgnoreCase) >= 0) hasRotHor = true;
                    if (name.IndexOf("RotVert", StringComparison.OrdinalIgnoreCase) >= 0) hasRotVert = true;
                    if (name.IndexOf("LEGS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Legs", StringComparison.OrdinalIgnoreCase) >= 0) hasLegs = true;
                }

                if (hasRotHor || hasRotVert || hasLegs)
                {
                    return "NativeTransformRig";
                }
            }

            return "StaticVehicle";
        }

        private static void InspectBones(GameObject go, Renderer[] renderers, out string rootBoneName, List<string> boneNames)
        {
            rootBoneName = string.Empty;
            if (go == null) return;

            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] is SkinnedMeshRenderer smr)
                    {
                        if (smr.rootBone != null && string.IsNullOrEmpty(rootBoneName))
                        {
                            rootBoneName = smr.rootBone.name ?? string.Empty;
                        }

                        if (smr.bones != null)
                        {
                            for (int b = 0; b < smr.bones.Length; b++)
                            {
                                Transform bone = smr.bones[b];
                                if (bone != null && !string.IsNullOrEmpty(bone.name) && !boneNames.Contains(bone.name))
                                {
                                    boneNames.Add(bone.name);
                                }
                            }
                        }
                    }
                }
            }

            if (boneNames.Count == 0)
            {
                Transform[] transforms = go.GetComponentsInChildren<Transform>(true);
                if (transforms != null)
                {
                    for (int i = 0; i < transforms.Length; i++)
                    {
                        Transform t = transforms[i];
                        if (t != null && t.name.StartsWith("Bone_", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrEmpty(rootBoneName) &&
                                (string.Equals(t.name, "Bone_Root", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(t.name, "Bone_Pelvis", StringComparison.OrdinalIgnoreCase)))
                            {
                                rootBoneName = t.name;
                            }
                            if (!boneNames.Contains(t.name))
                            {
                                boneNames.Add(t.name);
                            }
                        }
                    }
                }
            }
        }

        private static void InspectSockets(GameObject go, List<string> discoveredSockets, out int socketAmbiguityCount)
        {
            socketAmbiguityCount = 0;
            if (go == null) return;

            Transform[] transforms = go.GetComponentsInChildren<Transform>(true);
            if (transforms == null) return;

            var roleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform t = transforms[i];
                if (t == null) continue;
                string tName = t.name ?? string.Empty;

                string canonicalRole;
                if (RogueCommandSocketCatalog.TryResolveCanonicalSocketName(tName, out canonicalRole))
                {
                    if (!discoveredSockets.Contains(canonicalRole))
                    {
                        discoveredSockets.Add(canonicalRole);
                    }

                    int count;
                    roleCounts.TryGetValue(canonicalRole, out count);
                    roleCounts[canonicalRole] = count + 1;
                }
                else if (tName.StartsWith("FirePoint_", StringComparison.OrdinalIgnoreCase))
                {
                    if (!discoveredSockets.Contains(tName))
                    {
                        discoveredSockets.Add(tName);
                    }

                    int count;
                    roleCounts.TryGetValue(tName, out count);
                    roleCounts[tName] = count + 1;
                }
            }

            foreach (KeyValuePair<string, int> pair in roleCounts)
            {
                if (pair.Value > 1)
                {
                    socketAmbiguityCount++;
                }
            }
        }

        private static void InspectDetailedSockets(GameObject go, List<VisualSocketDetailRow> socketRows)
        {
            if (go == null) return;

            Transform[] transforms = go.GetComponentsInChildren<Transform>(true);
            if (transforms == null) return;

            var roleOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform t = transforms[i];
                if (t == null) continue;
                string tName = t.name ?? string.Empty;

                string canonicalRole;
                if (RogueCommandSocketCatalog.TryResolveCanonicalSocketName(tName, out canonicalRole))
                {
                    int count;
                    roleOccurrences.TryGetValue(canonicalRole, out count);
                    roleOccurrences[canonicalRole] = count + 1;
                }
                else if (tName.StartsWith("FirePoint_", StringComparison.OrdinalIgnoreCase))
                {
                    int count;
                    roleOccurrences.TryGetValue(tName, out count);
                    roleOccurrences[tName] = count + 1;
                }
            }

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform t = transforms[i];
                if (t == null) continue;
                string tName = t.name ?? string.Empty;

                string canonicalRole;
                bool isSocket = RogueCommandSocketCatalog.TryResolveCanonicalSocketName(tName, out canonicalRole);
                if (!isSocket && tName.StartsWith("FirePoint_", StringComparison.OrdinalIgnoreCase))
                {
                    canonicalRole = tName;
                    isSocket = true;
                }

                if (isSocket)
                {
                    int totalForRole = roleOccurrences.TryGetValue(canonicalRole, out int c) ? c : 1;
                    bool isAmbiguous = totalForRole > 1;

                    Vector3 pos = t.localPosition;
                    Vector3 rot = t.localEulerAngles;

                    socketRows.Add(new VisualSocketDetailRow(
                        tName,
                        canonicalRole,
                        pos.x,
                        pos.y,
                        pos.z,
                        rot.x,
                        rot.y,
                        rot.z,
                        isAmbiguous,
                        totalForRole));
                }
            }
        }

        private static Animator ResolvePreferredAnimator(GameObject go)
        {
            if (go == null) return null;

            Transform[] transforms = go.GetComponentsInChildren<Transform>(true);
            if (transforms != null)
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform candidate = transforms[i];
                    if (candidate != null && candidate.name.StartsWith("RCL_Visual_", StringComparison.OrdinalIgnoreCase))
                    {
                        Animator anim = candidate.GetComponentInChildren<Animator>(true);
                        if (anim != null && anim.runtimeAnimatorController != null)
                        {
                            return anim;
                        }
                    }
                }
            }

            return go.GetComponentInChildren<Animator>(true);
        }

        private static void InspectVisualDirector(
            GameObject go,
            out bool hasDirector,
            out bool isDamaged,
            out bool isCritical,
            out bool isJammed,
            out float recoil,
            out float doorProgress)
        {
            hasDirector = false;
            isDamaged = false;
            isCritical = false;
            isJammed = false;
            recoil = 0f;
            doorProgress = 0f;

            if (go == null) return;

            try
            {
                Component director = go.GetComponent("RuntimeVisualDirector");
                if (director != null)
                {
                    hasDirector = true;
                    Type dirType = director.GetType();
                    BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                    FieldInfo damF = dirType.GetField("_isDamagedStateActive", flags);
                    if (damF != null) isDamaged = Convert.ToBoolean(damF.GetValue(director));

                    FieldInfo critF = dirType.GetField("_isCriticalStateActive", flags);
                    if (critF != null) isCritical = Convert.ToBoolean(critF.GetValue(director));

                    FieldInfo jamF = dirType.GetField("_isTurretJammed", flags);
                    if (jamF != null) isJammed = Convert.ToBoolean(jamF.GetValue(director));

                    FieldInfo recF = dirType.GetField("_currentRecoilOffset", flags);
                    if (recF != null) recoil = Convert.ToSingle(recF.GetValue(director));

                    FieldInfo doorF = dirType.GetField("_doorOpenProgress", flags);
                    if (doorF != null) doorProgress = Convert.ToSingle(doorF.GetValue(director));
                }
            }
            catch
            {
                // Degrade gracefully
            }
        }

        internal static EntityController FindEntityByKey(ExistingControllers existing, string key)
        {
            return RuntimeEntityKeyResolver.FindUniqueEntityByKey(existing, key);
        }
    }
}
