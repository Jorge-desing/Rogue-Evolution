using System;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of active camera viewport, world position, and zoom settings.
    /// Decoupled from Unity types to support clean-room MCP telemetry and offline validation.
    /// </summary>
    public sealed class CameraCensus
    {
        public bool IsCameraAvailable { get; }
        public float PositionX { get; }
        public float PositionY { get; }
        public float PositionZ { get; }
        public float FieldOfView { get; }
        public float MinFov { get; }
        public float MaxFov { get; }
        public float DefaultFov { get; }
        public bool IsOrthographic { get; }
        public float OrthographicSize { get; }

        public CameraCensus(
            bool isCameraAvailable,
            float positionX,
            float positionY,
            float positionZ,
            float fieldOfView,
            float minFov,
            float maxFov,
            float defaultFov,
            bool isOrthographic,
            float orthographicSize)
        {
            IsCameraAvailable = isCameraAvailable;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            FieldOfView = fieldOfView;
            MinFov = minFov;
            MaxFov = maxFov;
            DefaultFov = defaultFov;
            IsOrthographic = isOrthographic;
            OrthographicSize = orthographicSize;
        }

        public static CameraCensus Unavailable { get; } =
            new CameraCensus(false, 0f, 0f, 0f, 0f, 0f, 0f, 0f, false, 0f);
    }

    /// <summary>
    /// Diagnostic collector that inspects the live main camera and <see cref="CameraZoom"/>
    /// without throwing exceptions.
    /// </summary>
    public static class RuntimeCameraCensus
    {
        private static readonly BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static CameraCensus Current()
        {
            try
            {
                Camera cam = ResolveMainCamera();
                if (cam == null)
                {
                    return CameraCensus.Unavailable;
                }

                Vector3 pos = cam.transform != null ? cam.transform.position : Vector3.zero;
                float fov = cam.fieldOfView;
                bool isOrtho = cam.orthographic;
                float orthoSize = cam.orthographicSize;

                float minFov = 20f;
                float maxFov = 80f;
                float defFov = 45f;

                try
                {
                    object zoomComp = cam.GetComponent<CameraZoom>() ?? UnityEngine.Object.FindObjectOfType<CameraZoom>();
                    if (zoomComp != null)
                    {
                        Type zType = zoomComp.GetType();
                        FieldInfo minF = zType.GetField("minFov", Flags);
                        if (minF != null) minFov = Convert.ToSingle(minF.GetValue(zoomComp));
                        FieldInfo maxF = zType.GetField("maxFov", Flags);
                        if (maxF != null) maxFov = Convert.ToSingle(maxF.GetValue(zoomComp));
                        FieldInfo defF = zType.GetField("defaultFov", Flags);
                        if (defF != null) defFov = Convert.ToSingle(defF.GetValue(zoomComp));
                    }
                }
                catch { }

                return new CameraCensus(
                    true,
                    pos.x,
                    pos.y,
                    pos.z,
                    fov,
                    minFov,
                    maxFov,
                    defFov,
                    isOrtho,
                    orthoSize);
            }
            catch
            {
                return CameraCensus.Unavailable;
            }
        }

        private static Camera ResolveMainCamera()
        {
            try
            {
                Camera cam = Camera.main;
                if (cam != null) return cam;

                Type pucType = typeof(PlayerUnitController);
                FieldInfo instanceField = pucType.GetField("_instance", Flags);
                object puc = instanceField?.GetValue(null) ?? UnityEngine.Object.FindObjectOfType<PlayerUnitController>();
                if (puc != null)
                {
                    FieldInfo camField = pucType.GetField("_mainCam", Flags);
                    var candidate = camField?.GetValue(puc) as Camera;
                    if (candidate != null) return candidate;
                }

                return UnityEngine.Object.FindObjectOfType<Camera>();
            }
            catch
            {
                return null;
            }
        }
    }
}
