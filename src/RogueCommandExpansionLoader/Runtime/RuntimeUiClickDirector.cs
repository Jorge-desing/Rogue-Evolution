using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Result of a coordinate-based click attempt. <see cref="Clicked"/> is true
    /// only when the click was actually delivered through the game's real input
    /// path (EventSystem + PointerEventData replay for UI, or a physics raycast
    /// + SendMessage OnMouseDown for 3D world objects). Failures carry a stop
    /// point so an agent can decide what to do next.
    /// </summary>
    public sealed class UiClickResult
    {
        public UiClickResult(bool clicked, string via, string targetName, string topHitName,
            float screenX, float screenY, string[] phases, string message)
        {
            Clicked = clicked;
            Via = via ?? string.Empty;
            TargetName = targetName ?? string.Empty;
            TopHitName = topHitName ?? string.Empty;
            ScreenX = screenX;
            ScreenY = screenY;
            Phases = phases ?? Array.Empty<string>();
            Message = message ?? string.Empty;
        }

        public bool Clicked { get; }
        public string Via { get; }
        public string TargetName { get; }
        public string TopHitName { get; }
        public float ScreenX { get; }
        public float ScreenY { get; }
        public string[] Phases { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Click by real screen coordinates instead of invoking handlers directly.
    ///
    /// UI elements: the target's <see cref="RectTransform"/> center is projected
    /// to screen pixels via <see cref="RectTransformUtility.WorldToScreenPoint"/>
    /// (through the ancestor Canvas worldCamera), raycast with
    /// <see cref="EventSystem.RaycastAll"/>, and — when the top hit belongs to
    /// the target — the exact pointer sequence
    /// StandaloneInputModule performs (pointerEnter → pointerDown →
    /// initializePotentialDrag → pointerUp → pointerClick) is replayed through
    /// <see cref="ExecuteEvents"/>. A click that would hit an unrelated occluder
    /// is not delivered; the occluder is reported instead (an agent must never
    /// silently click the wrong element).
    ///
    /// 3D world objects (e.g. intermission nodes): the camera projects the
    /// object to screen pixels, a <see cref="Physics"/> raycast verifies the
    /// object is actually under the point, and OnMouseEnter/OnMouseDown are
    /// delivered with <see cref="GameObject.SendMessage(string, SendMessageOptions)"/>
    /// — the same public messaging surface the engine itself uses for physics
    /// mouse picking, instead of direct method invocation.
    /// </summary>
    public static class RuntimeUiClickDirector
    {
        public const string ViaEventSystem = "EventSystemRaycast";
        public const string ViaPhysics = "PhysicsRaycast";

        /// <summary>
        /// Clicks a GameObject at real coordinates: UI path when a
        /// RectTransform is present, 3D physics path otherwise.
        /// </summary>
        public static UiClickResult TryClickGameObject(GameObject target)
        {
            if (target == null)
            {
                return Failed("target is null (stop point: target resolution).");
            }

            if (!target.activeInHierarchy)
            {
                return Failed("target '" + target.name + "' is not in the active hierarchy (stop point: target visibility).");
            }

            if (target.GetComponent<RectTransform>() != null)
            {
                return TryClickUiGameObject(target);
            }

            return TryClickWorldObject(target);
        }

        /// <summary>UI path: RectTransform → screen pixels → EventSystem.</summary>
        public static UiClickResult TryClickUiGameObject(GameObject target)
        {
            if (target == null)
            {
                return Failed("target is null (stop point: target resolution).");
            }

            if (!target.activeInHierarchy)
            {
                return Failed("target '" + target.name + "' is not in the active hierarchy (stop point: target visibility).");
            }

            RectTransform rectTransform = target.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                return new UiClickResult(false, ViaEventSystem, target.name, string.Empty, 0f, 0f, Array.Empty<string>(),
                    "target '" + target.name + "' has no RectTransform (stop point: UI targeting).");
            }

            Canvas canvas = FindAncestorCanvas(target);
            if (canvas == null)
            {
                return new UiClickResult(false, ViaEventSystem, target.name, string.Empty, 0f, 0f, Array.Empty<string>(),
                    "target '" + target.name + "' has no Canvas ancestor (stop point: canvas lookup).");
            }

            Vector3 worldCenter = rectTransform.TransformPoint(
                new Vector3(rectTransform.rect.center.x, rectTransform.rect.center.y, 0f));
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, worldCenter);

            if (!IsOnScreen(screenPoint))
            {
                return new UiClickResult(false, ViaEventSystem, target.name, string.Empty, screenPoint.x, screenPoint.y,
                    Array.Empty<string>(),
                    "target '" + target.name + "' centers off-screen at (" + Format(screenPoint.x) + ", " + Format(screenPoint.y) +
                    ") (stop point: screen projection).");
            }

            return ExecuteUiClickAtPoint(screenPoint, target);
        }

        /// <summary>World path: world position → screen pixels → physics raycast → engine message dispatch.</summary>
        public static UiClickResult TryClickWorldObject(GameObject target)
        {
            if (target == null)
            {
                return Failed("target is null (stop point: target resolution).");
            }

            if (!target.activeInHierarchy)
            {
                return Failed("target '" + target.name + "' is not in the active hierarchy (stop point: target visibility).");
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return new UiClickResult(false, ViaPhysics, target.name, string.Empty, 0f, 0f, Array.Empty<string>(),
                    "no main camera (stop point: camera lookup).");
            }

            Renderer renderer = target.GetComponent<Renderer>();
            Vector3 worldPoint = renderer != null ? renderer.bounds.center : target.transform.position;

            Vector3 projected = camera.WorldToScreenPoint(worldPoint);
            Vector2 screenPoint = new Vector2(projected.x, projected.y);

            if (projected.z < 0f)
            {
                return new UiClickResult(false, ViaPhysics, target.name, string.Empty, screenPoint.x, screenPoint.y,
                    Array.Empty<string>(),
                    "target '" + target.name + "' projects behind the camera (stop point: camera visibility).");
            }

            if (!IsOnScreen(screenPoint))
            {
                return new UiClickResult(false, ViaPhysics, target.name, string.Empty, screenPoint.x, screenPoint.y,
                    Array.Empty<string>(),
                    "target '" + target.name + "' centers off-screen at (" + Format(screenPoint.x) + ", " + Format(screenPoint.y) +
                    ") (stop point: screen projection).");
            }

            Ray ray = camera.ScreenPointToRay(screenPoint);
            if (!Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, 1000f, -1))
            {
                return new UiClickResult(false, ViaPhysics, target.name, string.Empty, screenPoint.x, screenPoint.y,
                    Array.Empty<string>(),
                    "physics raycast missed at (" + Format(screenPoint.x) + ", " + Format(screenPoint.y) +
                    ") (stop point: physics pick; target may have no collider or is fully occluded).");
            }

            GameObject hitGameObject = hit.transform != null ? hit.transform.gameObject : null;
            if (!IsInHierarchyOrSelf(hitGameObject, target))
            {
                return new UiClickResult(false, ViaPhysics, target.name, HitName(hitGameObject), screenPoint.x, screenPoint.y,
                    Array.Empty<string>(),
                    "occluded by '" + HitName(hitGameObject) + "' at (" + Format(screenPoint.x) + ", " + Format(screenPoint.y) +
                    ") (stop point: occlusion gate).");
            }

            // Deliver through the engine's own message dispatch, not direct
            // method invocation: this is the route physics mouse picking uses.
            hitGameObject.SendMessage("OnMouseEnter", SendMessageOptions.DontRequireReceiver);
            hitGameObject.SendMessage("OnMouseDown", SendMessageOptions.DontRequireReceiver);

            return new UiClickResult(true, ViaPhysics, target.name, HitName(hitGameObject),
                screenPoint.x, screenPoint.y, new[] { "OnMouseEnter", "OnMouseDown" },
                "clicked '" + target.name + "' by real coordinates (WorldToScreenPoint + Physics.Raycast → SendMessage OnMouseEnter/OnMouseDown).");
        }

        /// <summary>Point mode: click whatever element is on top at screen pixels (x, y).</summary>
        public static UiClickResult TryClickAtScreenPoint(Vector2 screenPoint)
        {
            if (!IsOnScreen(screenPoint))
            {
                return new UiClickResult(false, ViaEventSystem, string.Empty, string.Empty, screenPoint.x, screenPoint.y,
                    Array.Empty<string>(),
                    "point (" + Format(screenPoint.x) + ", " + Format(screenPoint.y) + ") is outside the screen (stop point: screen bounds).");
            }

            return ExecuteUiClickAtPoint(screenPoint, null);
        }

        // ---- Core UI replay ----

        private static UiClickResult ExecuteUiClickAtPoint(Vector2 screenPoint, GameObject expectedTarget)
        {
            string targetName = expectedTarget != null ? expectedTarget.name : string.Empty;

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return new UiClickResult(false, ViaEventSystem, targetName, string.Empty, screenPoint.x, screenPoint.y,
                    Array.Empty<string>(), "no EventSystem.current (stop point: input system).");
            }

            PointerEventData pointer = new PointerEventData(eventSystem)
            {
                position = screenPoint,
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                clickTime = Time.unscaledTime,
            };

            List<RaycastResult> raycastResults = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, raycastResults);
            if (raycastResults == null || raycastResults.Count == 0)
            {
                return new UiClickResult(false, ViaEventSystem, targetName, string.Empty, screenPoint.x, screenPoint.y,
                    Array.Empty<string>(),
                    "no UI element hit at (" + Format(screenPoint.x) + ", " + Format(screenPoint.y) + ") (stop point: raycast).");
            }

            RaycastResult top = raycastResults[0];
            GameObject topGameObject = top.gameObject;
            pointer.pointerCurrentRaycast = top;

            if (expectedTarget != null && !IsInHierarchyOrSelf(topGameObject, expectedTarget))
            {
                return new UiClickResult(false, ViaEventSystem, targetName, HitName(topGameObject),
                    screenPoint.x, screenPoint.y, Array.Empty<string>(),
                    "occluded by '" + HitName(topGameObject) + "' at (" + Format(screenPoint.x) + ", " + Format(screenPoint.y) +
                    ") (stop point: occlusion gate); a real click here would not reach the target.");
            }

            List<string> phases = new List<string>();

            // Hover (the move that precedes a real click).
            pointer.pointerEnter = topGameObject;
            ExecuteEvents.ExecuteHierarchy(topGameObject, pointer, ExecuteEvents.pointerEnterHandler);
            phases.Add("pointerEnter");

            // Press, mirroring StandaloneInputModule.ProcessMousePress:
            // the input module reports the object that handled pointerDown, and
            // falls back to the click-handler object when there is no down
            // handler (widgets like Michsky's only implement enter/click).
            pointer.pointerPressRaycast = pointer.pointerCurrentRaycast;
            pointer.eligibleForClick = true;
            GameObject downHandler = ExecuteEvents.ExecuteHierarchy(topGameObject, pointer, ExecuteEvents.pointerDownHandler);
            GameObject clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(topGameObject);
            GameObject effectivePress = downHandler ?? clickHandler;
            if (effectivePress == null)
            {
                return new UiClickResult(false, ViaEventSystem, targetName, HitName(topGameObject),
                    screenPoint.x, screenPoint.y, phases.ToArray(),
                    "no IPointerDownHandler or IPointerClickHandler on the hit chain of '" + HitName(topGameObject) + "' (stop point: interactivity).");
            }

            pointer.pointerPress = effectivePress;
            pointer.pointerClick = clickHandler;
            if (downHandler != null)
            {
                phases.Add("pointerDown");
            }

            pointer.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(topGameObject);
            if (pointer.pointerDrag != null)
            {
                ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.initializePotentialDrag);
                phases.Add("initializePotentialDrag");
            }

            // Release (mirrors StandaloneInputModule.ReleaseMouse): up on the
            // press handler, then the click, gated on pointerClick == the
            // chain's click handler and eligibility.
            if (downHandler != null)
            {
                ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
                phases.Add("pointerUp");
            }

            GameObject releaseClickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(topGameObject);
            if (pointer.pointerClick != null && pointer.pointerClick == releaseClickHandler && pointer.eligibleForClick)
            {
                ExecuteEvents.Execute(pointer.pointerClick, pointer, ExecuteEvents.pointerClickHandler);
                phases.Add("pointerClick");
            }
            else
            {
                return new UiClickResult(false, ViaEventSystem, targetName, HitName(topGameObject),
                    screenPoint.x, screenPoint.y, phases.ToArray(),
                    "no IPointerClickHandler on the hit chain of '" + HitName(topGameObject) + "' (stop point: click delivery).");
            }

            return new UiClickResult(true, ViaEventSystem, targetName, HitName(topGameObject),
                screenPoint.x, screenPoint.y, phases.ToArray(),
                "clicked '" + HitName(topGameObject) + "'" + (targetName.Length > 0 ? " (target '" + targetName + "')" : string.Empty) +
                " by real coordinates via EventSystem raycast (pointerEnter→" +
                (downHandler != null ? "pointerDown→pointerUp→pointerClick" : "pointerClick") + ").");
        }

        // ---- Pure seams (testable without a scene) ----

        /// <summary>
        /// Resolves a GameObject by Unity instance ID across both ID spaces:
        /// GameObject/Transform IDs and component (e.g. MonoBehaviour) IDs.
        /// The intermission census stores component IDs, so a GameObject-only
        /// scan can never resolve census rows.
        /// </summary>
        internal static GameObject ResolveGameObjectByInstanceId(
            int instanceId,
            Func<IEnumerable<Component>> getAllComponents,
            Func<IEnumerable<Transform>> getAllTransforms)
        {
            if (instanceId == 0)
            {
                return null;
            }

            IEnumerable<Component> components = getAllComponents != null ? getAllComponents() : null;
            if (components != null)
            {
                foreach (Component component in components)
                {
                    if (component != null && component.GetInstanceID() == instanceId && component.gameObject != null)
                    {
                        return component.gameObject;
                    }
                }
            }

            IEnumerable<Transform> transforms = getAllTransforms != null ? getAllTransforms() : null;
            if (transforms != null)
            {
                foreach (Transform transform in transforms)
                {
                    if (transform != null && transform.gameObject != null
                        && transform.gameObject.GetInstanceID() == instanceId)
                    {
                        return transform.gameObject;
                    }
                }
            }

            return null;
        }

        internal static bool IsInHierarchyOrSelf(GameObject candidate, GameObject parent)
        {
            if (candidate == null || parent == null)
            {
                return false;
            }

            for (GameObject current = candidate; current != null;
                 current = current.transform != null && current.transform.parent != null
                     ? current.transform.parent.gameObject
                     : null)
            {
                if (current == parent)
                {
                    return true;
                }
            }

            return false;
        }

        private static Canvas FindAncestorCanvas(GameObject gameObject)
        {
            for (GameObject current = gameObject; current != null;
                 current = current.transform != null && current.transform.parent != null
                     ? current.transform.parent.gameObject
                     : null)
            {
                Canvas canvas = current.GetComponent<Canvas>();
                if (canvas != null)
                {
                    return canvas;
                }
            }

            return null;
        }

        private static bool IsOnScreen(Vector2 point)
        {
            return point.x >= 0f && point.x <= Screen.width && point.y >= 0f && point.y <= Screen.height;
        }

        private static string HitName(GameObject gameObject)
        {
            return gameObject != null ? gameObject.name : "<null>";
        }

        private static string Format(float value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static UiClickResult Failed(string message)
        {
            return new UiClickResult(false, string.Empty, string.Empty, string.Empty, 0f, 0f, Array.Empty<string>(), message);
        }
    }
}
