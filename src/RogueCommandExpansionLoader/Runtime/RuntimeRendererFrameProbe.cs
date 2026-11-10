using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Runtime.Visual;
using UnityEngine;
using UnityEngine.Rendering;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Opt-in, bounded render callback telemetry for a live entity's skinned renderers.
    /// It reads camera/Animator state and sampled BakeMesh vertices; it never reads pixels.
    /// </summary>
    internal static class RuntimeRendererFrameProbe
    {
        private const int MaxRenderersPerProbe = 4;
        internal const int MaxComparedVertices = 128;
        private const int DefaultSampleLimit = 12;
        private const int MinSampleLimit = 2;
        private const int MaxSampleLimit = 24;
        private const float MaxProbeDurationSeconds = 20f;
        private const float RetainedResultSeconds = 30f;

        private static readonly object Gate = new object();
        private static readonly Plane[] CameraFrustumPlanes = new Plane[6];
        private static readonly Dictionary<string, ProbeSession> Sessions =
            new Dictionary<string, ProbeSession>(StringComparer.Ordinal);
        private static bool CameraCallbackAttached;

        internal static string StartJson(string entityKeyId, string rendererNameContains, int sampleLimit)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return SerializeError("entityKeyId must not be empty.");
            }

            int boundedSampleLimit = sampleLimit <= 0
                ? DefaultSampleLimit
                : Math.Max(MinSampleLimit, Math.Min(MaxSampleLimit, sampleLimit));

            EntityController entity;
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                entity = RuntimeVisualCensus.FindEntityByKey(existing, entityKeyId.Trim());
            }
            catch (Exception ex)
            {
                return SerializeError("Could not resolve entity: " + ex.GetType().Name);
            }

            if (entity == null || entity.gameObject == null)
            {
                return SerializeError("Entity was not found among live controllers.");
            }

            List<SkinnedMeshRenderer> renderers;
            try
            {
                SkinnedMeshRenderer[] candidates = entity.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                string filter = string.IsNullOrWhiteSpace(rendererNameContains) ? null : rendererNameContains.Trim();
                renderers = new List<SkinnedMeshRenderer>(candidates != null ? candidates.Length : 0);
                if (candidates != null)
                {
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        SkinnedMeshRenderer renderer = candidates[i];
                        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                            renderer.sharedMesh == null ||
                            (filter != null && renderer.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0))
                        {
                            continue;
                        }

                        renderers.Add(renderer);
                    }
                }

                renderers.Sort(CompareRenderers);
            }
            catch (Exception ex)
            {
                return SerializeError("Could not enumerate skinned renderers: " + ex.GetType().Name);
            }

            if (renderers.Count == 0)
            {
                return SerializeError("No active skinned renderer matched the entity and optional renderer filter.");
            }

            lock (Gate)
            {
                foreach (ProbeSession existing in Sessions.Values)
                {
                    if (existing.Active)
                    {
                        return SerializeError("Only one renderer frame probe may be active at a time.");
                    }
                }
            }

            string token = Guid.NewGuid().ToString("N");
            float startedAt = SafeUnscaledTime();
            var session = new ProbeSession(token, entityKeyId.Trim(), boundedSampleLimit, startedAt);
            int selectedCount = Math.Min(MaxRenderersPerProbe, renderers.Count);
            session.OmittedRendererCount = Math.Max(0, renderers.Count - selectedCount);
            session.Renderers.Capacity = selectedCount;
            for (int i = 0; i < selectedCount; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                session.Renderers.Add(new RendererProbeRecord(
                    Clip(renderer.name, 128),
                    Clip(renderer.sharedMesh.name, 128),
                    renderer.sharedMesh.vertexCount,
                    renderer.GetInstanceID()));
            }

            lock (Gate)
            {
                Sessions[token] = session;
            }

            try
            {
                Animator fallbackAnimator = entity.GetComponentInChildren<Animator>(true);
                NativeRigRetargetProfile retarget = entity.GetComponent<NativeRigRetargetProfile>();
                RuntimeVisualDirector director = entity.GetComponent<RuntimeVisualDirector>();
                for (int i = 0; i < selectedCount; i++)
                {
                    SkinnedMeshRenderer renderer = renderers[i];
                    Animator animator = renderer.GetComponentInParent<Animator>();
                    if (animator == null) animator = fallbackAnimator;

                    RuntimeRendererFrameProbeBehaviour behaviour =
                        renderer.gameObject.AddComponent<RuntimeRendererFrameProbeBehaviour>();
                    session.Renderers[i].Behaviour = behaviour;
                    behaviour.Configure(token, i, animator, retarget, director);
                }

                RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
                RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
                CameraCallbackAttached = true;
            }
            catch (Exception ex)
            {
                StopSession(session, startedAt);
                lock (Gate) Sessions.Remove(token);
                return SerializeError("Could not attach the temporary probe: " + ex.GetType().Name);
            }

            return SerializeSession(session, "active", true);
        }

        internal static string ReadJson(string token)
        {
            if (!TryGetSession(token, out ProbeSession session))
            {
                return SerializeError("Probe token was not found or has expired.");
            }

            float now = SafeUnscaledTime();
            if (session.Active && now - session.StartedAt >= MaxProbeDurationSeconds)
            {
                StopSession(session, now);
            }

            return SerializeSession(session, session.Active ? "active" : "stoppedOrExpired", false);
        }

        internal static string StopJson(string token)
        {
            if (!TryGetSession(token, out ProbeSession session))
            {
                return SerializeError("Probe token was not found or has expired.");
            }

            StopSession(session, SafeUnscaledTime());
            string result = SerializeSession(session, "stopped", false);
            lock (Gate) Sessions.Remove(session.Token);
            return result;
        }

        internal static void Tick(float unscaledTime)
        {
            if (!IsFinite(unscaledTime)) return;

            List<ProbeSession> toStop = null;
            List<string> toRemove = null;
            lock (Gate)
            {
                foreach (KeyValuePair<string, ProbeSession> pair in Sessions)
                {
                    ProbeSession session = pair.Value;
                    if (session.Active && unscaledTime - session.StartedAt >= MaxProbeDurationSeconds)
                    {
                        if (toStop == null) toStop = new List<ProbeSession>();
                        toStop.Add(session);
                    }
                    else if (!session.Active && session.RemoveAfter > 0f && unscaledTime >= session.RemoveAfter)
                    {
                        if (toRemove == null) toRemove = new List<string>();
                        toRemove.Add(pair.Key);
                    }
                }

                if (toRemove != null)
                {
                    for (int i = 0; i < toRemove.Count; i++) Sessions.Remove(toRemove[i]);
                }
            }

            if (toStop != null)
            {
                for (int i = 0; i < toStop.Count; i++) StopSession(toStop[i], unscaledTime);
            }
        }

        internal static bool TryGetSession(string token, out ProbeSession session)
        {
            session = null;
            if (string.IsNullOrWhiteSpace(token)) return false;
            lock (Gate) return Sessions.TryGetValue(token.Trim(), out session);
        }

        internal static bool IsActive(string token)
        {
            if (!TryGetSession(token, out ProbeSession session)) return false;
            return session.Active;
        }

        private static void StopSession(ProbeSession session, float now)
        {
            if (session == null || !session.Active) return;
            session.Active = false;
            session.RemoveAfter = now + RetainedResultSeconds;
            if (CameraCallbackAttached)
            {
                try { RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering; } catch { }
                CameraCallbackAttached = false;
            }

            for (int i = 0; i < session.Renderers.Count; i++)
            {
                RuntimeRendererFrameProbeBehaviour behaviour = session.Renderers[i].Behaviour;
                session.Renderers[i].Behaviour = null;
                if (behaviour != null)
                {
                    try { UnityEngine.Object.Destroy(behaviour); } catch { }
                }
            }
        }

        private static void HandleEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null) return;

            ProbeSession session = null;
            lock (Gate)
            {
                foreach (ProbeSession candidate in Sessions.Values)
                {
                    if (!candidate.Active) continue;
                    session = candidate;
                    break;
                }
            }

            if (session == null) return;

            session.CameraRenderCallbackCount++;
            if (camera.cameraType != CameraType.Game)
            {
                session.NonGameCameraCallbackCount++;
                return;
            }

            session.GameCameraRenderCallbackCount++;
            if (!camera.isActiveAndEnabled)
            {
                session.InactiveGameCameraCallbackCount++;
                return;
            }

            if (camera.targetTexture != null)
            {
                session.RenderTextureCameraCallbackCount++;
                return;
            }

            session.ScreenGameCameraRenderCallbackCount++;
            try
            {
                GeometryUtility.CalculateFrustumPlanes(camera, CameraFrustumPlanes);
            }
            catch
            {
                session.FrustumCalculationFailureCount++;
                return;
            }

            for (int i = 0; i < session.Renderers.Count; i++)
            {
                RuntimeRendererFrameProbeBehaviour behaviour = session.Renderers[i].Behaviour;
                if (behaviour != null)
                {
                    behaviour.CaptureAfterCameraRender(camera, CameraFrustumPlanes);
                }
                else
                {
                    session.MissingObserverCount++;
                }
            }
        }

        internal static bool TryBeginSample(string token, int rendererIndex)
        {
            if (!TryGetSession(token, out ProbeSession session) || !session.Active ||
                rendererIndex < 0 || rendererIndex >= session.Renderers.Count)
            {
                return false;
            }

            RendererProbeRecord record = session.Renderers[rendererIndex];
            if (record.Samples.Count >= session.SampleLimit)
            {
                int frame = Time.frameCount;
                if (record.LastOmittedSampleFrame != frame)
                {
                    record.OmittedSampleCount++;
                    record.LastOmittedSampleFrame = frame;
                }
                return false;
            }

            return true;
        }

        internal static bool RecordSample(string token, int rendererIndex, ProbeRenderSample sample)
        {
            if (!TryGetSession(token, out ProbeSession session) || !session.Active ||
                rendererIndex < 0 || rendererIndex >= session.Renderers.Count)
            {
                return false;
            }

            RendererProbeRecord record = session.Renderers[rendererIndex];
            if (record.Samples.Count >= session.SampleLimit) return false;
            record.Samples.Add(sample);
            return true;
        }

        internal static void MarkRendererDestroyed(string token, int rendererIndex)
        {
            if (!TryGetSession(token, out ProbeSession session) ||
                rendererIndex < 0 || rendererIndex >= session.Renderers.Count)
            {
                return;
            }

            session.Renderers[rendererIndex].RendererDestroyed = true;
            session.Renderers[rendererIndex].Behaviour = null;
        }

        private static string SerializeSession(ProbeSession session, string status, bool includePolicy)
        {
            int omittedSamples = 0;
            int totalSamples = 0;
            for (int i = 0; i < session.Renderers.Count; i++)
            {
                omittedSamples += session.Renderers[i].OmittedSampleCount;
                totalSamples += session.Renderers[i].Samples.Count;
            }
            var rendererRows = new List<Dictionary<string, object>>(session.Renderers.Count);
            for (int r = 0; r < session.Renderers.Count; r++)
            {
                RendererProbeRecord renderer = session.Renderers[r];
                var sampleRows = new List<Dictionary<string, object>>(renderer.Samples.Count);
                for (int s = 0; s < renderer.Samples.Count; s++)
                {
                    sampleRows.Add(renderer.Samples[s].ToJson());
                }

                rendererRows.Add(new Dictionary<string, object>
                {
                    ["name"] = renderer.Name,
                    ["meshName"] = renderer.MeshName,
                    ["meshVertexCount"] = renderer.MeshVertexCount,
                    ["rendererInstanceId"] = renderer.InstanceId,
                    ["rendererDestroyed"] = renderer.RendererDestroyed,
                    ["omittedSampleCount"] = renderer.OmittedSampleCount,
                    ["samples"] = sampleRows
                });
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "rendererFrameProbe",
                ["success"] = true,
                ["status"] = status,
                ["token"] = session.Token,
                ["entityKeyId"] = session.EntityKeyId,
                ["elapsedSeconds"] = Math.Max(0f, SafeUnscaledTime() - session.StartedAt),
                ["omittedRendererCount"] = session.OmittedRendererCount,
                ["omittedSampleCount"] = omittedSamples,
                ["totalSampleCount"] = totalSamples,
                ["rendererCount"] = session.Renderers.Count,
                ["cameraRenderCallbackCount"] = session.CameraRenderCallbackCount,
                ["gameCameraRenderCallbackCount"] = session.GameCameraRenderCallbackCount,
                ["screenGameCameraRenderCallbackCount"] = session.ScreenGameCameraRenderCallbackCount,
                ["nonGameCameraCallbackCount"] = session.NonGameCameraCallbackCount,
                ["inactiveGameCameraCallbackCount"] = session.InactiveGameCameraCallbackCount,
                ["renderTextureCameraCallbackCount"] = session.RenderTextureCameraCallbackCount,
                ["frustumCalculationFailureCount"] = session.FrustumCalculationFailureCount,
                ["frustumRejectedRendererCount"] = session.FrustumRejectedRendererCount,
                ["layerCulledRendererCount"] = session.LayerCulledRendererCount,
                ["inactiveRendererCount"] = session.InactiveRendererCount,
                ["missingObserverCount"] = session.MissingObserverCount,
                ["cameraFilter"] = "CameraType.Game with no targetTexture",
                ["renderCallbackSource"] = "RenderPipelineManager.endCameraRendering",
                ["deformationSource"] = "SkinnedMeshRenderer.BakeMesh sampled local vertices",
                ["pixelCapture"] = false,
                ["renderers"] = rendererRows
            };

            if (includePolicy)
            {
                view["sampleLimitPerRenderer"] = session.SampleLimit;
                view["maximumDurationSeconds"] = MaxProbeDurationSeconds;
            }

            return AgentDiagnosticsFormatter.SerializePrimitivePayload(view);
        }

        private static string SerializeError(string message)
        {
            return AgentDiagnosticsFormatter.SerializePrimitivePayload(new Dictionary<string, object>
            {
                ["kind"] = "rendererFrameProbe",
                ["success"] = false,
                ["error"] = message ?? "Unknown probe error.",
                ["pixelCapture"] = false
            });
        }

        private static int CompareRenderers(SkinnedMeshRenderer left, SkinnedMeshRenderer right)
        {
            int vertexCountOrder = right.sharedMesh.vertexCount.CompareTo(left.sharedMesh.vertexCount);
            return vertexCountOrder != 0
                ? vertexCountOrder
                : StringComparer.OrdinalIgnoreCase.Compare(left.name, right.name);
        }

        internal static string Clip(string value, int maximumLength)
        {
            value = value ?? string.Empty;
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float SafeUnscaledTime()
        {
            try { return Time.unscaledTime; } catch { return 0f; }
        }

        internal sealed class ProbeSession
        {
            internal readonly string Token;
            internal readonly string EntityKeyId;
            internal readonly int SampleLimit;
            internal readonly float StartedAt;
            internal readonly List<RendererProbeRecord> Renderers = new List<RendererProbeRecord>();
            internal bool Active = true;
            internal int OmittedRendererCount;
            internal int CameraRenderCallbackCount;
            internal int GameCameraRenderCallbackCount;
            internal int ScreenGameCameraRenderCallbackCount;
            internal int NonGameCameraCallbackCount;
            internal int InactiveGameCameraCallbackCount;
            internal int RenderTextureCameraCallbackCount;
            internal int FrustumCalculationFailureCount;
            internal int FrustumRejectedRendererCount;
            internal int LayerCulledRendererCount;
            internal int InactiveRendererCount;
            internal int MissingObserverCount;
            internal float RemoveAfter;

            internal ProbeSession(string token, string entityKeyId, int sampleLimit, float startedAt)
            {
                Token = token;
                EntityKeyId = entityKeyId;
                SampleLimit = sampleLimit;
                StartedAt = startedAt;
            }
        }

        internal sealed class RendererProbeRecord
        {
            internal readonly string Name;
            internal readonly string MeshName;
            internal readonly int MeshVertexCount;
            internal readonly int InstanceId;
            internal readonly List<ProbeRenderSample> Samples = new List<ProbeRenderSample>();
            internal RuntimeRendererFrameProbeBehaviour Behaviour;
            internal bool RendererDestroyed;
            internal int OmittedSampleCount;
            internal int LastSampleFrame = -1;
            internal int LastOmittedSampleFrame = -1;

            internal RendererProbeRecord(string name, string meshName, int meshVertexCount, int instanceId)
            {
                Name = name;
                MeshName = meshName;
                MeshVertexCount = meshVertexCount;
                InstanceId = instanceId;
            }
        }

        internal sealed class ProbeRenderSample
        {
            internal int Frame;
            internal float ScaledTime;
            internal float UnscaledTime;
            internal string CameraName = string.Empty;
            internal int CameraInstanceId;
            internal bool BoundsIntersectsCameraFrustum;
            internal bool LayerIncludedByCameraCullingMask;
            internal int CameraTargetDisplay;
            internal bool RendererEnabled;
            internal bool RendererIsVisible;
            internal Vector3 BoundsCenter;
            internal Vector3 BoundsSize;
            internal bool AnimatorActive;
            internal string ActiveClip = string.Empty;
            internal int AnimatorStateHash;
            internal float NormalizedTime;
            internal float AnimatorSpeed;
            internal bool IsMoving;
            internal bool DeathRetirementPending;
            internal bool RetargetPresent;
            internal bool RetargetBound;
            internal bool RetargetSuspended;
            internal int BoundMappingCount;
            internal int ComparedVertexCount;
            internal int SampledVertexCount;
            internal bool DeformationDeltaAvailable;
            internal float MaximumLocalVertexDelta;
            internal float MeanLocalVertexDelta;

            internal Dictionary<string, object> ToJson()
            {
                return new Dictionary<string, object>
                {
                    ["frame"] = Frame,
                    ["scaledTime"] = ScaledTime,
                    ["unscaledTime"] = UnscaledTime,
                    ["cameraName"] = CameraName,
                    ["cameraInstanceId"] = CameraInstanceId,
                    ["cameraRenderCallbackObserved"] = true,
                    ["boundsIntersectsCameraFrustum"] = BoundsIntersectsCameraFrustum,
                    ["layerIncludedByCameraCullingMask"] = LayerIncludedByCameraCullingMask,
                    ["cameraTargetDisplay"] = CameraTargetDisplay,
                    ["cameraTargetTextureAssigned"] = false,
                    ["rendererEnabled"] = RendererEnabled,
                    ["rendererIsVisible"] = RendererIsVisible,
                    ["boundsCenter"] = VectorJson(BoundsCenter),
                    ["boundsSize"] = VectorJson(BoundsSize),
                    ["animatorActive"] = AnimatorActive,
                    ["activeClip"] = ActiveClip,
                    ["animatorStateHash"] = AnimatorStateHash,
                    ["normalizedTime"] = NormalizedTime,
                    ["animatorSpeed"] = AnimatorSpeed,
                    ["isMoving"] = IsMoving,
                    ["deathRetirementPending"] = DeathRetirementPending,
                    ["retargetPresent"] = RetargetPresent,
                    ["retargetBound"] = RetargetBound,
                    ["retargetSuspended"] = RetargetSuspended,
                    ["boundMappingCount"] = BoundMappingCount,
                    ["deformationDeltaAvailable"] = DeformationDeltaAvailable,
                    ["comparedVertexCount"] = ComparedVertexCount,
                    ["sampledVertexCount"] = SampledVertexCount,
                    ["maximumLocalVertexDelta"] = MaximumLocalVertexDelta,
                    ["meanLocalVertexDelta"] = MeanLocalVertexDelta
                };
            }

            private static float[] VectorJson(Vector3 value) => new[]
            {
                value.x,
                value.y,
                value.z
            };
        }
    }

    public sealed class RuntimeRendererFrameProbeBehaviour : MonoBehaviour
    {
        private string _token;
        private int _rendererIndex;
        private SkinnedMeshRenderer _renderer;
        private Animator _animator;
        private NativeRigRetargetProfile _retarget;
        private RuntimeVisualDirector _director;
        private Mesh _bakedMesh;
        private List<Vector3> _vertices;
        private Vector3[] _previousSamplePoints;
        private readonly List<AnimatorClipInfo> _currentClips = new List<AnimatorClipInfo>(4);
        private int _previousSamplePointCount;

        internal void Configure(
            string token,
            int rendererIndex,
            Animator animator,
            NativeRigRetargetProfile retarget,
            RuntimeVisualDirector director)
        {
            _token = token;
            _rendererIndex = rendererIndex;
            _renderer = GetComponent<SkinnedMeshRenderer>();
            _animator = animator;
            _retarget = retarget;
            _director = director;
            _bakedMesh = new Mesh { name = "RCL_RendererFrameProbe_BakedMesh", hideFlags = HideFlags.HideAndDontSave };
            _vertices = new List<Vector3>(RuntimeRendererFrameProbe.MaxComparedVertices * 2);
            _previousSamplePoints = new Vector3[RuntimeRendererFrameProbe.MaxComparedVertices];
        }

        internal void CaptureAfterCameraRender(Camera camera, Plane[] cameraFrustumPlanes)
        {
            if (string.IsNullOrEmpty(_token) || _renderer == null || camera == null ||
                cameraFrustumPlanes == null || !RuntimeRendererFrameProbe.IsActive(_token)) return;
            int frame = Time.frameCount;
            if (RuntimeRendererFrameProbe.TryGetSession(_token, out RuntimeRendererFrameProbe.ProbeSession session))
            {
                if (_rendererIndex < 0 || _rendererIndex >= session.Renderers.Count) return;
                RuntimeRendererFrameProbe.RendererProbeRecord record = session.Renderers[_rendererIndex];
                if (record.LastSampleFrame == frame) return;

                if (!_renderer.enabled || !_renderer.gameObject.activeInHierarchy)
                {
                    session.InactiveRendererCount++;
                    return;
                }

                if ((camera.cullingMask & (1 << _renderer.gameObject.layer)) == 0)
                {
                    session.LayerCulledRendererCount++;
                    return;
                }

                if (!GeometryUtility.TestPlanesAABB(cameraFrustumPlanes, _renderer.bounds))
                {
                    session.FrustumRejectedRendererCount++;
                    return;
                }
            }
            else
            {
                return;
            }

            if (!RuntimeRendererFrameProbe.TryBeginSample(_token, _rendererIndex)) return;
            session.Renderers[_rendererIndex].LastSampleFrame = frame;

            bool deltaAvailable;
            int comparedVertexCount;
            int sampledVertexCount;
            float maximumDelta;
            float meanDelta;
            CaptureDeformationDelta(out deltaAvailable, out comparedVertexCount, out sampledVertexCount, out maximumDelta, out meanDelta);

            var sample = new RuntimeRendererFrameProbe.ProbeRenderSample
            {
                Frame = frame,
                ScaledTime = FiniteOrZero(Time.time),
                UnscaledTime = FiniteOrZero(Time.unscaledTime),
                CameraName = RuntimeRendererFrameProbe.Clip(camera.name, 128),
                CameraInstanceId = camera.GetInstanceID(),
                BoundsIntersectsCameraFrustum = true,
                LayerIncludedByCameraCullingMask = true,
                CameraTargetDisplay = camera.targetDisplay,
                RendererEnabled = _renderer.enabled && _renderer.gameObject.activeInHierarchy,
                RendererIsVisible = _renderer.isVisible,
                BoundsCenter = _renderer.bounds.center,
                BoundsSize = _renderer.bounds.size,
                AnimatorActive = _animator != null && _animator.isActiveAndEnabled,
                AnimatorSpeed = _animator != null ? FiniteOrZero(_animator.speed) : 0f,
                DeathRetirementPending = _director != null && _director.DeathVisualRetirementPending,
                RetargetPresent = _retarget != null,
                RetargetBound = _retarget != null && _retarget.IsBound,
                RetargetSuspended = _retarget != null && _retarget.IsSuspended,
                BoundMappingCount = _retarget != null ? _retarget.BoundMappingCount : 0,
                DeformationDeltaAvailable = deltaAvailable,
                ComparedVertexCount = comparedVertexCount,
                SampledVertexCount = sampledVertexCount,
                MaximumLocalVertexDelta = maximumDelta,
                MeanLocalVertexDelta = meanDelta
            };

            if (_animator != null)
            {
                try
                {
                    AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
                    sample.AnimatorStateHash = state.fullPathHash;
                    sample.NormalizedTime = FiniteOrZero(state.normalizedTime);
                    sample.IsMoving = _animator.GetBool("RCL_IsMoving");
                    _currentClips.Clear();
                    _animator.GetCurrentAnimatorClipInfo(0, _currentClips);
                    if (_currentClips.Count > 0 && _currentClips[0].clip != null)
                    {
                        sample.ActiveClip = RuntimeRendererFrameProbe.Clip(_currentClips[0].clip.name, 128);
                    }
                }
                catch
                {
                    // A controller may be tearing down while a destruction probe is recording.
                }
            }

            RuntimeRendererFrameProbe.RecordSample(_token, _rendererIndex, sample);
        }

        private void CaptureDeformationDelta(
            out bool deltaAvailable,
            out int comparedVertexCount,
            out int sampledVertexCount,
            out float maximumDelta,
            out float meanDelta)
        {
            deltaAvailable = false;
            comparedVertexCount = 0;
            sampledVertexCount = 0;
            maximumDelta = 0f;
            meanDelta = 0f;
            if (_renderer == null || _renderer.sharedMesh == null || _bakedMesh == null || _vertices == null) return;

            try
            {
                _renderer.BakeMesh(_bakedMesh);
                _vertices.Clear();
                _bakedMesh.GetVertices(_vertices);
                int vertexCount = _vertices.Count;
                if (vertexCount <= 0) return;

                int stride = Math.Max(1, (vertexCount + RuntimeRendererFrameProbe.MaxComparedVertices - 1) / RuntimeRendererFrameProbe.MaxComparedVertices);
                float totalDelta = 0f;
                int sampleIndex = 0;
                for (int vertexIndex = 0; vertexIndex < vertexCount && sampleIndex < RuntimeRendererFrameProbe.MaxComparedVertices; vertexIndex += stride)
                {
                    Vector3 current = _vertices[vertexIndex];
                    if (sampleIndex < _previousSamplePointCount)
                    {
                        float delta = Vector3.Distance(_previousSamplePoints[sampleIndex], current);
                        totalDelta += delta;
                        if (delta > maximumDelta) maximumDelta = delta;
                    }

                    _previousSamplePoints[sampleIndex] = current;
                    sampleIndex++;
                }

                sampledVertexCount = sampleIndex;
                comparedVertexCount = Math.Min(sampleIndex, _previousSamplePointCount);
                deltaAvailable = _previousSamplePointCount == sampleIndex && sampleIndex > 0;
                if (deltaAvailable)
                {
                    meanDelta = totalDelta / sampleIndex;
                }

                _previousSamplePointCount = sampleIndex;
            }
            catch
            {
                // Mesh bake failure does not erase the independent render callback evidence.
            }
        }

        private void OnDestroy()
        {
            if (_renderer == null)
            {
                RuntimeRendererFrameProbe.MarkRendererDestroyed(_token, _rendererIndex);
            }
            if (_bakedMesh != null)
            {
                try { UnityEngine.Object.Destroy(_bakedMesh); } catch { }
                _bakedMesh = null;
            }
        }

        private static float FiniteOrZero(float value) => RuntimeRendererFrameProbe.IsFinite(value) ? value : 0f;
    }
}
