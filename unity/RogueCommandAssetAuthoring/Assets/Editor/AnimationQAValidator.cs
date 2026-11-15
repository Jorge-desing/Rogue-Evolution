using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    /// <summary>
    /// Unity Editor QA suite for Rogue Command unit animations:
    /// 1. Automated batchmode/MenuItem contract audit across all rigged *Visual.prefab assets.
    /// 2. Zero-dependency SceneView Animation Preview Scrubber using UnityEditor.AnimationMode.
    /// </summary>
    public static class AnimationQAValidator
    {
        private const float RootMotionTolerance = 1e-4f;
        private const float LoopSeamTolerance = 1e-3f;
        private const float MotionPresenceEpsilon = 1e-4f;

        [MenuItem("Rogue Command/QA/Audit All Unit Animations")]
        public static void AuditAllUnitAnimationsMenu()
        {
            AuditAllUnitAnimations(throwOnFailure: false, strictLoopSeams: false);
        }

        [MenuItem("Rogue Command/QA/Audit All Unit Animations (Strict Loop Seams)")]
        public static void AuditAllUnitAnimationsStrictMenu()
        {
            AuditAllUnitAnimations(throwOnFailure: false, strictLoopSeams: true);
        }

        public static void AuditAllUnitAnimationsBatch()
        {
            AuditAllUnitAnimations(throwOnFailure: true, strictLoopSeams: false);
        }

        public static void AuditAllUnitAnimationsStrictBatch()
        {
            AuditAllUnitAnimations(throwOnFailure: true, strictLoopSeams: true);
        }

        public static int AuditAllUnitAnimations(bool throwOnFailure, bool strictLoopSeams = false)
        {
            string[] searchRoots = new[]
            {
                "Assets/RogueCommandExpansionSample",
                "Assets/TacticalOperations"
            };

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", searchRoots);
            int auditedAnimators = 0;
            List<string> failures = new List<string>();
            List<string> warnings = new List<string>();

            foreach (string guid in prefabGuids.Distinct())
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("Visual.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                Animator[] animators = prefab.GetComponentsInChildren<Animator>(true);
                if (animators.Length == 0)
                {
                    continue;
                }

                auditedAnimators++;
                List<string> prefabWarnings = new List<string>();
                List<string> prefabIssues = AuditPrefabAnimator(prefab, animators, prefabWarnings);
                foreach (string issue in prefabIssues)
                {
                    failures.Add($"{path}: {issue}");
                }
                foreach (string warn in prefabWarnings)
                {
                    if (strictLoopSeams)
                    {
                        failures.Add($"{path}: {warn}");
                    }
                    else
                    {
                        warnings.Add($"{path}: {warn}");
                    }
                }
            }

            if (warnings.Count > 0)
            {
                Debug.LogWarning($"[AnimationQA] {warnings.Count} loop-seam warning(s) across {auditedAnimators} animated prefabs (run Strict mode to fail on these):\n - "
                    + string.Join("\n - ", warnings.Take(25))
                    + (warnings.Count > 25 ? $"\n ... and {warnings.Count - 25} more." : string.Empty));
            }

            if (failures.Count > 0)
            {
                string summary = $"[AnimationQA] FAILED ({failures.Count} issues across {auditedAnimators} animated prefabs):\n - "
                    + string.Join("\n - ", failures);
                Debug.LogError(summary);
                if (throwOnFailure)
                {
                    throw new InvalidOperationException(summary);
                }
                return 1;
            }

            Debug.Log($"[AnimationQA] PASSED: {auditedAnimators} animated *Visual.prefab assets verified (Generic Avatar, no root motion, RCL_* params, Bone_Root=0, FireRecoil dual-chain; loop-seam warnings={warnings.Count}).");
            return 0;
        }

        public static List<string> AuditPrefabAnimator(
            GameObject prefab,
            Animator[] animators,
            List<string> warnings = null)
        {
            List<string> issues = new List<string>();
            if (animators.Length != 1)
            {
                issues.Add($"Expected exactly 1 Animator on prefab root/hierarchy, found {animators.Length}.");
                return issues;
            }

            Animator animator = animators[0];
            if (animator.applyRootMotion)
            {
                issues.Add("Animator.applyRootMotion must be false.");
            }
            if (animator.avatar == null || !animator.avatar.isValid || animator.avatar.isHuman)
            {
                issues.Add("Animator must have a valid Generic (non-Humanoid) Avatar.");
            }

            AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
            if (controller == null)
            {
                issues.Add("Animator.runtimeAnimatorController is missing or not an AnimatorController.");
                return issues;
            }

            VerifyParameter(controller, "RCL_IsMoving", AnimatorControllerParameterType.Bool, issues);
            VerifyParameter(controller, "RCL_Fire", AnimatorControllerParameterType.Trigger, issues);
            VerifyParameter(controller, "RCL_Death", AnimatorControllerParameterType.Trigger, issues);
            VerifyParameter(controller, "RCL_SpeedMultiplier", AnimatorControllerParameterType.Float, issues);

            HashSet<AnimationClip> auditedClips = new HashSet<AnimationClip>();
            foreach (AnimationClip clip in controller.animationClips)
            {
                if (clip == null || !auditedClips.Add(clip))
                {
                    continue;
                }
                AuditClip(clip, issues, warnings);
            }

            return issues;
        }

        private static void VerifyParameter(
            AnimatorController controller,
            string paramName,
            AnimatorControllerParameterType expectedType,
            List<string> issues)
        {
            AnimatorControllerParameter match = controller.parameters.FirstOrDefault(p => p.name == paramName);
            if (match == null)
            {
                issues.Add($"AnimatorController '{controller.name}' is missing parameter '{paramName}'.");
                return;
            }
            if (match.type != expectedType)
            {
                issues.Add($"Parameter '{paramName}' expected {expectedType} but was {match.type}.");
            }
        }

        private static string ExtractActionRoleToken(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                return string.Empty;
            }
            string tail = clipName.Contains("|")
                ? clipName.Substring(clipName.LastIndexOf('|') + 1)
                : clipName;
            int actionIdx = tail.IndexOf("Action_", StringComparison.OrdinalIgnoreCase);
            if (actionIdx >= 0)
            {
                return tail.Substring(actionIdx + "Action_".Length).Trim().ToLowerInvariant();
            }
            return tail.Trim().ToLowerInvariant();
        }

        private static void AuditClip(AnimationClip clip, List<string> issues, List<string> warnings)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            string roleToken = ExtractActionRoleToken(clip.name);
            bool isLooping = roleToken == "idle" || roleToken == "walk" || roleToken == "drive";
            bool isFireRecoil = roleToken.Contains("firerecoil") || roleToken == "fire";

            bool hasWeaponMotion = false;
            bool hasBodyMotion = false;

            foreach (EditorCurveBinding binding in bindings)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                {
                    continue;
                }

                string pathLower = (binding.path ?? string.Empty).ToLowerInvariant();
                string propLower = (binding.propertyName ?? string.Empty).ToLowerInvariant();

                // 1. In-Place Invariant: Bone_Root must have zero position delta
                if (pathLower.EndsWith("bone_root") && propLower.StartsWith("m_localposition"))
                {
                    float minV = float.MaxValue;
                    float maxV = float.MinValue;
                    foreach (Keyframe kf in curve.keys)
                    {
                        if (kf.value < minV) minV = kf.value;
                        if (kf.value > maxV) maxV = kf.value;
                    }
                    if (maxV - minV > RootMotionTolerance)
                    {
                        issues.Add($"Clip '{clip.name}' violates In-Place invariant on '{binding.path}.{binding.propertyName}' (delta={maxV - minV:F5}).");
                    }
                }

                // 2. Loop Seam Closure on Idle/Walk/Drive
                if (isLooping && clip.length > 0f)
                {
                    float startVal = curve.Evaluate(0f);
                    float endVal = curve.Evaluate(clip.length);
                    if (Mathf.Abs(startVal - endVal) > LoopSeamTolerance)
                    {
                        string msg = $"Clip '{clip.name}' loop seam mismatch on '{binding.path}.{binding.propertyName}' (start={startVal:F4}, end={endVal:F4}).";
                        if (warnings != null)
                        {
                            warnings.Add(msg);
                        }
                        else
                        {
                            issues.Add(msg);
                        }
                    }
                }

                // 3. FireRecoil dual-chain check
                if (isFireRecoil)
                {
                    float range = GetCurveRange(curve);
                    if (range > MotionPresenceEpsilon)
                    {
                        if (pathLower.Contains("barrel") || pathLower.Contains("turret") || pathLower.Contains("muzzle"))
                        {
                            hasWeaponMotion = true;
                        }
                        if (pathLower.Contains("bone_hull") || pathLower.Contains("bone_pelvis"))
                        {
                            hasBodyMotion = true;
                        }
                    }
                }
            }

            if (isFireRecoil)
            {
                if (!hasWeaponMotion)
                {
                    issues.Add($"Clip '{clip.name}' has no measurable weapon chain motion.");
                }
                if (!hasBodyMotion)
                {
                    issues.Add($"Clip '{clip.name}' has no measurable body (Bone_Hull/Bone_Pelvis) motion.");
                }
            }
        }

        private static float GetCurveRange(AnimationCurve curve)
        {
            float minV = float.MaxValue;
            float maxV = float.MinValue;
            foreach (Keyframe kf in curve.keys)
            {
                if (kf.value < minV) minV = kf.value;
                if (kf.value > maxV) maxV = kf.value;
            }
            return curve.length > 0 ? (maxV - minV) : 0f;
        }
    }

    /// <summary>
    /// Lightweight SceneView animation scrubber for Rogue Command unit prefabs.
    /// Uses UnityEditor.AnimationMode to sample clips without entering Play Mode.
    /// </summary>
    public class AnimationPreviewScrubberWindow : EditorWindow
    {
        private GameObject _targetObject;
        private AnimationClip[] _clips = Array.Empty<AnimationClip>();
        private int _selectedClipIndex;
        private float _normalizedTime;
        private bool _isPlaying;
        private double _lastEditorTime;

        [MenuItem("Rogue Command/QA/Animation Preview Scrubber")]
        public static void ShowWindow()
        {
            GetWindow<AnimationPreviewScrubberWindow>("RCL Anim Scrubber");
        }

        private void OnDisable()
        {
            StopPreview();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Rogue Command Animation QA Scrubber", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _targetObject = (GameObject)EditorGUILayout.ObjectField("Scene Instance", _targetObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshClips();
            }

            if (_targetObject == null)
            {
                EditorGUILayout.HelpBox("Drag an instantiated *Visual unit GameObject from the Hierarchy to scrub its Animator clips in SceneView.", MessageType.Info);
                return;
            }

            if (_clips.Length == 0)
            {
                EditorGUILayout.HelpBox("No AnimationClips found on the target Animator's RuntimeAnimatorController.", MessageType.Warning);
                return;
            }

            string[] clipNames = _clips.Select(c => c != null ? c.name : "<null>").ToArray();
            _selectedClipIndex = EditorGUILayout.Popup("Clip", Mathf.Clamp(_selectedClipIndex, 0, _clips.Length - 1), clipNames);
            AnimationClip activeClip = _clips[_selectedClipIndex];

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_isPlaying ? "Pause" : "Play"))
            {
                _isPlaying = !_isPlaying;
                _lastEditorTime = EditorApplication.timeSinceStartup;
                if (_isPlaying && !AnimationMode.InAnimationMode())
                {
                    AnimationMode.StartAnimationMode();
                }
            }
            if (GUILayout.Button("Stop & Reset"))
            {
                StopPreview();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            _normalizedTime = EditorGUILayout.Slider("Normalized Time", _normalizedTime, 0f, 1f);
            if (EditorGUI.EndChangeCheck() && activeClip != null)
            {
                SampleClip(activeClip, _normalizedTime * activeClip.length);
            }

            if (activeClip != null)
            {
                EditorGUILayout.LabelField($"Duration: {activeClip.length:F3}s | FrameRate: {activeClip.frameRate:F1} fps | Loop: {activeClip.isLooping}");
            }
        }

        private void Update()
        {
            if (!_isPlaying || _targetObject == null || _clips.Length == 0)
            {
                return;
            }
            AnimationClip activeClip = _clips[Mathf.Clamp(_selectedClipIndex, 0, _clips.Length - 1)];
            if (activeClip == null || activeClip.length <= 0f)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            _normalizedTime = (_normalizedTime + (dt / activeClip.length)) % 1.0f;
            SampleClip(activeClip, _normalizedTime * activeClip.length);
            Repaint();
        }

        private void RefreshClips()
        {
            StopPreview();
            if (_targetObject == null)
            {
                _clips = Array.Empty<AnimationClip>();
                return;
            }
            Animator animator = _targetObject.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                _clips = animator.runtimeAnimatorController.animationClips.Distinct().ToArray();
            }
            else
            {
                _clips = Array.Empty<AnimationClip>();
            }
            _selectedClipIndex = 0;
            _normalizedTime = 0f;
        }

        private void SampleClip(AnimationClip clip, float timeSeconds)
        {
            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(_targetObject, clip, timeSeconds);
            AnimationMode.EndSampling();
            SceneView.RepaintAll();
        }

        private void StopPreview()
        {
            _isPlaying = false;
            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }
            SceneView.RepaintAll();
        }
    }
}
