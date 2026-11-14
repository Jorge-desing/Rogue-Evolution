using System;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>
    /// Diagnostic runtime animation control for testing Animator state transitions,
    /// triggers (RCL_IsMoving, RCL_Fire, RCL_Death), and RCL_SpeedMultiplier
    /// on a target entity without screenshots or manual gameplay setup.
    /// </summary>
    public sealed class AnimationControlResult
    {
        public bool Success { get; }
        public string EntityKeyId { get; }
        public string Action { get; }
        public bool HasAnimator { get; }
        public bool IsMoving { get; }
        public float SpeedMultiplier { get; }
        public string ActiveClipName { get; }
        public string Message { get; }

        public AnimationControlResult(
            bool success,
            string entityKeyId,
            string action,
            bool hasAnimator,
            bool isMoving,
            float speedMultiplier,
            string activeClipName,
            string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            Action = action ?? string.Empty;
            HasAnimator = hasAnimator;
            IsMoving = isMoving;
            SpeedMultiplier = speedMultiplier;
            ActiveClipName = activeClipName ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    public static class RuntimeAnimationControlDirector
    {
        public static AnimationControlResult OrderUnitAnimationControl(
            string entityKeyId,
            string action,
            float speedMultiplier = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new AnimationControlResult(false, string.Empty, action, false, false, 1f, string.Empty, "entityKeyId is required.");
            }
            if (string.IsNullOrWhiteSpace(action))
            {
                return new AnimationControlResult(false, entityKeyId, string.Empty, false, false, 1f, string.Empty, "action is required (Walk, Idle, Fire, Death, SetSpeed, Reset).");
            }
            if (float.IsNaN(speedMultiplier) || float.IsInfinity(speedMultiplier))
            {
                speedMultiplier = 1.0f;
            }
            float clampedSpeed = Math.Max(0.1f, Math.Min(5.0f, speedMultiplier));

            ExistingControllers existing = null;
            try { existing = ExistingControllers.Instance; } catch { }
            EntityController target = existing != null
                ? RuntimeEntityKeyResolver.FindUniqueEntityByKey(existing, entityKeyId)
                : null;
            if (target == null)
            {
                return new AnimationControlResult(false, entityKeyId, action, false, false, clampedSpeed, string.Empty, "Target entity not found.");
            }

            Animator animator = ResolvePreferredAnimator(target.gameObject);
            if (animator == null)
            {
                return new AnimationControlResult(false, entityKeyId, action, false, false, clampedSpeed, string.Empty, "Target entity has no Animator.");
            }

            return ApplyAnimatorAction(entityKeyId, animator, action, clampedSpeed);
        }

        public static Animator ResolvePreferredAnimator(GameObject root)
        {
            if (root == null) return null;

            Transform rootTf = root.transform;
            int childCount = rootTf != null ? rootTf.childCount : 0;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = rootTf.GetChild(i);
                if (child != null && child.name != null && child.name.StartsWith("RCL_Visual_", StringComparison.Ordinal))
                {
                    Animator modAnim = child.GetComponentInChildren<Animator>(true);
                    if (modAnim != null) return modAnim;
                }
            }

            return root.GetComponentInChildren<Animator>(true);
        }

        public static AnimationControlResult ApplyAnimatorAction(
            string entityKeyId,
            Animator animator,
            string action,
            float speedMultiplier)
        {
            if (animator == null)
            {
                return new AnimationControlResult(false, entityKeyId, action, false, false, speedMultiplier, string.Empty, "Animator is null.");
            }

            string normalized = (action ?? string.Empty).Trim().ToLowerInvariant();
            bool isMoving = false;
            try { isMoving = animator.GetBool("RCL_IsMoving"); } catch { }

            switch (normalized)
            {
                case "walk":
                case "drive":
                case "move":
                    TrySetBool(animator, "RCL_IsMoving", true);
                    TrySetFloat(animator, "RCL_SpeedMultiplier", speedMultiplier);
                    isMoving = true;
                    break;

                case "idle":
                case "stop":
                    TrySetBool(animator, "RCL_IsMoving", false);
                    TrySetFloat(animator, "RCL_SpeedMultiplier", speedMultiplier);
                    isMoving = false;
                    break;

                case "fire":
                case "firerecoil":
                    TrySetTrigger(animator, "RCL_Fire");
                    break;

                case "death":
                    TrySetBool(animator, "RCL_IsMoving", false);
                    TrySetTrigger(animator, "RCL_Death");
                    isMoving = false;
                    break;

                case "setspeed":
                case "speed":
                    TrySetFloat(animator, "RCL_SpeedMultiplier", speedMultiplier);
                    break;

                case "reset":
                    TrySetBool(animator, "RCL_IsMoving", false);
                    TryResetTrigger(animator, "RCL_Fire");
                    TryResetTrigger(animator, "RCL_Death");
                    TrySetFloat(animator, "RCL_SpeedMultiplier", 1.0f);
                    speedMultiplier = 1.0f;
                    isMoving = false;
                    break;

                default:
                    return new AnimationControlResult(
                        false,
                        entityKeyId,
                        action,
                        true,
                        isMoving,
                        speedMultiplier,
                        ReadActiveClipName(animator),
                        $"Unsupported animation action '{action}'. Use Walk, Idle, Fire, Death, SetSpeed, or Reset.");
            }

            return new AnimationControlResult(
                true,
                entityKeyId,
                action,
                true,
                isMoving,
                speedMultiplier,
                ReadActiveClipName(animator),
                $"Applied animation action '{action}' (speedMultiplier={speedMultiplier:F2}).");
        }

        private static string ReadActiveClipName(Animator animator)
        {
            try
            {
                AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
                if (clips != null && clips.Length > 0 && clips[0].clip != null)
                {
                    return clips[0].clip.name ?? string.Empty;
                }
            }
            catch { }
            return string.Empty;
        }

        private static void TrySetBool(Animator animator, string name, bool value)
        {
            try { animator.SetBool(name, value); } catch { }
        }

        private static void TrySetFloat(Animator animator, string name, float value)
        {
            try { animator.SetFloat(name, value); } catch { }
        }

        private static void TrySetTrigger(Animator animator, string name)
        {
            try { animator.SetTrigger(name); } catch { }
        }

        private static void TryResetTrigger(Animator animator, string name)
        {
            try { animator.ResetTrigger(name); } catch { }
        }
    }
}
