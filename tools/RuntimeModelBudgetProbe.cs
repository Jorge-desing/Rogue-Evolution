// Temporary source for BepInEx MCP mod_inject_class.
// This is intentionally not compiled into RogueCommandExpansionLoader.
// Geometry is counted once and rechecked every 30 rendered frames to reject
// changes in the enabled camera-frustum renderer/LOD set without scanning the
// scene on every frame or adding observers to rendered objects.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class RuntimeModelBudgetProbeV3 : MonoBehaviour
{
    private struct GeometryCensus
    {
        public long triangles;
        public int rendererCount;
        public int candidateRendererCount;
        public int unsupportedRendererCount;
        public long signature;
    }

    private static RuntimeModelBudgetProbeV3 _controller;
    private static Camera _targetCamera;
    private static bool _captureActive;
    private static bool _eventSubscribed;
    private static int _requestedFrames;
    private static int _lastCapturedFrame = -1;
    private static GeometryCensus _initialGeometryCensus;
    private static bool _geometryLoadStable;
    private static bool _cameraStable;
    private static bool _timeScaleStayedPositive;
    private static int _geometryStabilityChecks;
    private static long _lastGeometrySignature;
    private static readonly List<double> _frameIntervalsMs = new List<double>();
    private static readonly List<int> _frameIds = new List<int>();
    private static string _lastError;
    private static string _sceneName;
    private static string _cameraName;
    private static int _cameraId;
    private static int _screenWidth;
    private static int _screenHeight;
    private static float _timeScaleAtStart;
    private static float _cameraFieldOfView;
    private static float _cameraOrthographicSize;
    private static bool _cameraOrthographic;
    private static float _cameraAspect;
    private static Rect _cameraRect;
    private static int _cameraCullingMask;
    private static string _cameraType;
    private static Vector3 _cameraPosition;
    private static Quaternion _cameraRotation;
    private static string _captureStartedUtc;

    public string BeginCapture(string targetCameraName, int requestedFrameCount)
    {
        if (_captureActive)
            return "{\"success\":false,\"error\":\"capture already active\"}";
        if (requestedFrameCount < 120 || requestedFrameCount > 3600)
            return "{\"success\":false,\"error\":\"requested frame count must be between 120 and 3600\"}";

        Camera camera = ResolveCamera(targetCameraName);
        if (camera == null)
            return "{\"success\":false,\"error\":\"active target camera not found\"}";
        if (camera.cameraType != CameraType.Game || camera.targetTexture != null)
            return "{\"success\":false,\"error\":\"target must be a Game camera rendering to the display\"}";
        if (camera.pixelWidth <= 0 || camera.pixelHeight <= 0)
            return "{\"success\":false,\"error\":\"target camera has no rendered pixel area\"}";

        _controller = this;
        _targetCamera = camera;
        _requestedFrames = requestedFrameCount;
        _lastCapturedFrame = -1;
        _initialGeometryCensus = new GeometryCensus();
        _geometryLoadStable = true;
        _cameraStable = true;
        _timeScaleStayedPositive = Time.timeScale > 0.0f;
        _geometryStabilityChecks = 0;
        _lastGeometrySignature = 0;
        _frameIntervalsMs.Clear();
        _frameIds.Clear();
        _lastError = null;
        _sceneName = camera.gameObject.scene.name;
        _cameraName = camera.name;
        _cameraId = camera.GetInstanceID();
        _screenWidth = camera.pixelWidth;
        _screenHeight = camera.pixelHeight;
        _timeScaleAtStart = Time.timeScale;
        _cameraFieldOfView = camera.fieldOfView;
        _cameraOrthographicSize = camera.orthographicSize;
        _cameraOrthographic = camera.orthographic;
        _cameraAspect = camera.aspect;
        _cameraRect = camera.rect;
        _cameraCullingMask = camera.cullingMask;
        _cameraType = camera.cameraType.ToString();
        _cameraPosition = camera.transform.position;
        _cameraRotation = camera.transform.rotation;
        _captureStartedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        _initialGeometryCensus = CountCameraFrustumTriangles(camera);
        _lastGeometrySignature = _initialGeometryCensus.signature;
        if (_initialGeometryCensus.triangles <= 0 || _initialGeometryCensus.rendererCount <= 0)
        {
            _lastError = "target camera frustum contains no supported triangle meshes";
            return "{\"success\":false,\"error\":\"" + Escape(_lastError) + "\",\"candidateMeshRenderers\":" +
                _initialGeometryCensus.candidateRendererCount.ToString(CultureInfo.InvariantCulture) + "}";
        }

        _captureActive = true;
        RenderPipelineManager.endContextRendering += OnEndContextRendering;
        _eventSubscribed = true;

        return "{\"success\":true,\"status\":\"collecting\",\"scene\":\"" + Escape(_sceneName) +
            "\",\"camera\":\"" + Escape(_cameraName) + "\",\"cameraInstanceId\":" + _cameraId.ToString(CultureInfo.InvariantCulture) +
            ",\"resolutionWidth\":" + _screenWidth.ToString(CultureInfo.InvariantCulture) +
            ",\"resolutionHeight\":" + _screenHeight.ToString(CultureInfo.InvariantCulture) +
            ",\"requestedFrames\":" + _requestedFrames.ToString(CultureInfo.InvariantCulture) +
            ",\"cameraFrustumMeshTriangles\":" + _initialGeometryCensus.triangles.ToString(CultureInfo.InvariantCulture) +
            ",\"cameraFrustumMeshRenderers\":" + _initialGeometryCensus.rendererCount.ToString(CultureInfo.InvariantCulture) + "}";
    }

    public string GetReportJson()
    {
        double p50IntervalMs = Percentile(_frameIntervalsMs, 0.50);
        double p95IntervalMs = Percentile(_frameIntervalsMs, 0.95);
        double maxIntervalMs = Maximum(_frameIntervalsMs);
        double minIntervalMs = Minimum(_frameIntervalsMs);
        bool complete = !_captureActive && _frameIntervalsMs.Count >= _requestedFrames;
        bool calibrationReady = complete && _initialGeometryCensus.triangles > 0 &&
            _timeScaleAtStart > 0.0f && _timeScaleStayedPositive && _cameraStable && _geometryLoadStable;

        StringBuilder json = new StringBuilder(2048 + _frameIntervalsMs.Count * 24);
        json.Append('{');
        json.Append("\"schema\":\"rogue-command/runtime-geometry-sample/v3\",");
        json.Append("\"status\":\"").Append(_captureActive ? "collecting" : (complete ? "complete" : "stopped")).Append("\",");
        json.Append("\"captureStartedUtc\":\"").Append(Escape(_captureStartedUtc ?? "")).Append("\",");
        json.Append("\"scene\":\"").Append(Escape(_sceneName ?? "")).Append("\",");
        json.Append("\"unityVersion\":\"").Append(Escape(Application.unityVersion)).Append("\",");
        json.Append("\"camera\":{\"name\":\"").Append(Escape(_cameraName ?? "")).Append("\",");
        json.Append("\"instanceId\":").Append(_cameraId.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"pixelWidth\":").Append(_screenWidth.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"pixelHeight\":").Append(_screenHeight.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"cameraType\":\"").Append(Escape(_cameraType ?? "")).Append('\"').Append(',');
        json.Append("\"usesRenderTexture\":false,");
        json.Append("\"fieldOfView\":").Append(Number(_cameraFieldOfView)).Append(',');
        json.Append("\"orthographic\":").Append(_cameraOrthographic ? "true" : "false").Append(',');
        json.Append("\"orthographicSize\":").Append(Number(_cameraOrthographicSize)).Append(',');
        json.Append("\"aspect\":").Append(Number(_cameraAspect)).Append(',');
        json.Append("\"cullingMask\":").Append(_cameraCullingMask.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"position\":[").Append(Number(_cameraPosition.x)).Append(',').Append(Number(_cameraPosition.y)).Append(',').Append(Number(_cameraPosition.z)).Append("],");
        json.Append("\"rotationEuler\":[").Append(Number(_cameraRotation.eulerAngles.x)).Append(',').Append(Number(_cameraRotation.eulerAngles.y)).Append(',').Append(Number(_cameraRotation.eulerAngles.z)).Append("]},");
        json.Append("\"capture\":{\"requestedFrames\":").Append(_requestedFrames.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"sampleCount\":").Append(_frameIntervalsMs.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"timeScaleAtStart\":").Append(Number(_timeScaleAtStart)).Append(',');
        json.Append("\"frameIntervalMetric\":\"UnityEngine.Time.unscaledDeltaTime\",");
        json.Append("\"frameIntervalUnit\":\"ms\",");
        json.Append("\"geometryMetric\":\"cameraFrustumMeshTriangles\",");
        json.Append("\"geometrySource\":\"enabled in-frustum MeshRenderer/SkinnedMeshRenderer sharedMesh topology; signature rechecked every 30 rendered frames\",");
        json.Append("\"candidateMeshRenderers\":").Append(_initialGeometryCensus.candidateRendererCount.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"countedMeshRenderers\":").Append(_initialGeometryCensus.rendererCount.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"unsupportedMeshRenderers\":").Append(_initialGeometryCensus.unsupportedRendererCount.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"geometryLoadStable\":").Append(_geometryLoadStable ? "true" : "false").Append(',');
        json.Append("\"geometryStabilityChecks\":").Append(_geometryStabilityChecks.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"geometrySignature\":").Append(_lastGeometrySignature.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"cameraStable\":").Append(_cameraStable ? "true" : "false").Append(',');
        json.Append("\"timeScaleStayedPositive\":").Append(_timeScaleStayedPositive ? "true" : "false").Append("},");
        json.Append("\"summary\":{\"p50UnscaledFrameIntervalMs\":").Append(Number(p50IntervalMs)).Append(',');
        json.Append("\"p95UnscaledFrameIntervalMs\":").Append(Number(p95IntervalMs)).Append(',');
        json.Append("\"minUnscaledFrameIntervalMs\":").Append(Number(minIntervalMs)).Append(',');
        json.Append("\"maxUnscaledFrameIntervalMs\":").Append(Number(maxIntervalMs)).Append(',');
        json.Append("\"cameraFrustumMeshTriangles\":").Append(_initialGeometryCensus.triangles.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"cameraFrustumMeshRenderers\":").Append(_initialGeometryCensus.rendererCount.ToString(CultureInfo.InvariantCulture)).Append("},");
        json.Append("\"fitSample\":{\"triangles\":").Append(_initialGeometryCensus.triangles.ToString(CultureInfo.InvariantCulture)).Append(',');
        json.Append("\"p95UnscaledFrameIntervalMs\":").Append(Number(p95IntervalMs)).Append(',');
        json.Append("\"calibrationReady\":").Append(calibrationReady ? "true" : "false").Append("},");
        json.Append("\"frameIntervalsMs\":");
        AppendDoubleArray(json, _frameIntervalsMs);
        json.Append(",\"frameIds\":");
        AppendIntArray(json, _frameIds);
        json.Append(",\"limitations\":[\"Geometry is an enabled camera-frustum estimate; occlusion, GPU-instanced draws, terrain-specific paths, particles, shadows, and draw amplification are not counted.\",\"The enabled renderer set is rechecked every 30 rendered frames to reject camera, visibility, or LOD-set changes; brief changes between checks can still be missed.\",\"Unscaled frame interval is realized frame pacing, not a CPU/GPU time breakdown.\",\"Each report is one stable geometry-load sample from a Game camera without a render texture.\"]");
        if (!String.IsNullOrEmpty(_lastError))
            json.Append(",\"error\":\"").Append(Escape(_lastError)).Append('"');
        json.Append('}');
        return json.ToString();
    }

    public string StopCapture()
    {
        StopObserving();
        return GetReportJson();
    }

    public string Cleanup()
    {
        string report = GetReportJson();
        StopObserving();
        if (_controller == this)
            _controller = null;
        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour != null && behaviour != this && behaviour.GetType().FullName == "RuntimeModelBudgetProbeV3")
                UnityEngine.Object.Destroy(behaviour);
        }
        UnityEngine.Object.Destroy(this);
        return report;
    }

    private static Camera ResolveCamera(string requestedName)
    {
        Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
        Camera fallback = null;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy)
                continue;
            if (String.Equals(candidate.name, requestedName, StringComparison.Ordinal))
                return candidate;
            if (String.IsNullOrEmpty(requestedName) && candidate.CompareTag("MainCamera"))
                fallback = candidate;
        }
        return fallback;
    }

    private static GeometryCensus CountCameraFrustumTriangles(Camera camera)
    {
        GeometryCensus census = new GeometryCensus();
        Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
        Plane[] planes = new Plane[6];
        GeometryUtility.CalculateFrustumPlanes(camera, planes);
        int cameraMask = camera.cullingMask;
        long signature = 1469598103934665603L;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsSupportedRenderer(renderer) || (cameraMask & (1 << renderer.gameObject.layer)) == 0 ||
                renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly || !GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                continue;

            census.candidateRendererCount++;
            UpdateSignature(ref signature, renderer.GetInstanceID());
            Mesh mesh = GetSharedMesh(renderer);
            if (mesh == null)
            {
                census.unsupportedRendererCount++;
                UpdateSignature(ref signature, -1);
                continue;
            }

            long triangles = CountTriangles(mesh);
            if (triangles <= 0)
            {
                census.unsupportedRendererCount++;
                UpdateSignature(ref signature, 0);
                continue;
            }
            census.triangles += triangles;
            census.rendererCount++;
            UpdateSignature(ref signature, mesh.GetInstanceID());
            UpdateSignature(ref signature, triangles);
        }
        census.signature = signature;
        return census;
    }

    private static void UpdateSignature(ref long signature, long value)
    {
        unchecked
        {
            signature ^= value;
            signature *= 1099511628211L;
        }
    }

    private static bool SameGeometryCensus(GeometryCensus left, GeometryCensus right)
    {
        return left.triangles == right.triangles &&
            left.rendererCount == right.rendererCount &&
            left.candidateRendererCount == right.candidateRendererCount &&
            left.unsupportedRendererCount == right.unsupportedRendererCount &&
            left.signature == right.signature;
    }

    private static bool CameraMatchesCapture(Camera camera)
    {
        if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy ||
            camera.GetInstanceID() != _cameraId || camera.gameObject.scene.name != _sceneName ||
            camera.cameraType != CameraType.Game || camera.targetTexture != null ||
            camera.pixelWidth != _screenWidth || camera.pixelHeight != _screenHeight ||
            camera.cullingMask != _cameraCullingMask || camera.orthographic != _cameraOrthographic)
            return false;

        const float scalarTolerance = 0.001f;
        const float positionTolerance = 0.001f;
        const float rotationToleranceDegrees = 0.05f;
        Rect rect = camera.rect;
        return Vector3.Distance(camera.transform.position, _cameraPosition) <= positionTolerance &&
            Quaternion.Angle(camera.transform.rotation, _cameraRotation) <= rotationToleranceDegrees &&
            Mathf.Abs(camera.fieldOfView - _cameraFieldOfView) <= scalarTolerance &&
            Mathf.Abs(camera.orthographicSize - _cameraOrthographicSize) <= scalarTolerance &&
            Mathf.Abs(camera.aspect - _cameraAspect) <= scalarTolerance &&
            Mathf.Abs(rect.x - _cameraRect.x) <= scalarTolerance &&
            Mathf.Abs(rect.y - _cameraRect.y) <= scalarTolerance &&
            Mathf.Abs(rect.width - _cameraRect.width) <= scalarTolerance &&
            Mathf.Abs(rect.height - _cameraRect.height) <= scalarTolerance;
    }

    private static void CheckCaptureStability(Camera camera, int sampleNumber)
    {
        if (!CameraMatchesCapture(camera))
            _cameraStable = false;
        if (Time.timeScale <= 0.0f)
            _timeScaleStayedPositive = false;

        if (sampleNumber % 30 != 0 && sampleNumber != _requestedFrames)
            return;

        GeometryCensus current = CountCameraFrustumTriangles(camera);
        _geometryStabilityChecks++;
        _lastGeometrySignature = current.signature;
        if (!SameGeometryCensus(_initialGeometryCensus, current))
            _geometryLoadStable = false;
    }

    private static bool IsSupportedRenderer(Renderer renderer)
    {
        return renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy &&
            (renderer is MeshRenderer || renderer is SkinnedMeshRenderer);
    }

    private static Mesh GetSharedMesh(Renderer renderer)
    {
        SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
        if (skinned != null)
            return skinned.sharedMesh;
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    private static long CountTriangles(Mesh mesh)
    {
        long triangles = 0;
        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            long indices = mesh.GetIndexCount(subMesh);
            MeshTopology topology = mesh.GetTopology(subMesh);
            if (topology == MeshTopology.Triangles)
                triangles += indices / 3;
            else if (topology == MeshTopology.Quads)
                triangles += (indices / 4) * 2;
        }
        return triangles;
    }

    private static void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (!_captureActive || _targetCamera == null || cameras == null)
            return;
        int currentFrame = Time.frameCount;
        if (_lastCapturedFrame == currentFrame)
            return;

        bool targetWasRendered = false;
        for (int i = 0; i < cameras.Count; i++)
        {
            Camera camera = cameras[i];
            if (camera != null && camera.GetInstanceID() == _cameraId)
            {
                targetWasRendered = true;
                break;
            }
        }
        if (!targetWasRendered)
            return;

        _lastCapturedFrame = currentFrame;
        int sampleNumber = _frameIntervalsMs.Count + 1;
        CheckCaptureStability(_targetCamera, sampleNumber);
        _frameIntervalsMs.Add(Time.unscaledDeltaTime * 1000.0);
        _frameIds.Add(currentFrame);
        if (_frameIntervalsMs.Count >= _requestedFrames)
            StopObserving();
    }

    private static void StopObserving()
    {
        _captureActive = false;
        if (_eventSubscribed)
        {
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
            _eventSubscribed = false;
        }
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values == null || values.Count == 0)
            return 0.0;
        List<double> sorted = new List<double>(values);
        sorted.Sort();
        int index = Math.Max(0, (int)Math.Ceiling(percentile * sorted.Count) - 1);
        return sorted[Math.Min(index, sorted.Count - 1)];
    }

    private static double Maximum(List<double> values)
    {
        if (values == null || values.Count == 0)
            return 0.0;
        double result = Double.MinValue;
        for (int i = 0; i < values.Count; i++)
            result = Math.Max(result, values[i]);
        return result;
    }

    private static double Minimum(List<double> values)
    {
        if (values == null || values.Count == 0)
            return 0.0;
        double result = Double.MaxValue;
        for (int i = 0; i < values.Count; i++)
            result = Math.Min(result, values[i]);
        return result;
    }

    private static void AppendDoubleArray(StringBuilder json, List<double> values)
    {
        json.Append('[');
        for (int i = 0; i < values.Count; i++)
        {
            if (i != 0)
                json.Append(',');
            json.Append(Number(values[i]));
        }
        json.Append(']');
    }

    private static void AppendIntArray(StringBuilder json, List<int> values)
    {
        json.Append('[');
        for (int i = 0; i < values.Count; i++)
        {
            if (i != 0)
                json.Append(',');
            json.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }
        json.Append(']');
    }

    private static string Number(double value)
    {
        if (Double.IsNaN(value) || Double.IsInfinity(value))
            return "0";
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string Escape(string value)
    {
        if (value == null)
            return String.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
