using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Screenless, bounded discovery and interaction for Unity UI. It exposes
    /// accessible text and stable hierarchy paths instead of image captures.
    /// Clicks still travel through RuntimeUiClickDirector's EventSystem path.
    /// </summary>
    public static class RuntimeUiAccessibility
    {
        private const int MaxResultsLimit = 100;
        private const int MaxAmbiguousCandidates = 12;
        private const string MichskyButtonManagerTypeName = "Michsky.UI.Reach.ButtonManager";

        public static string GetCensusJson(string query, bool includeInactive, int maxResults)
        {
            string needle = (query ?? string.Empty).Trim();
            int cap = Mathf.Clamp(maxResults <= 0 ? 32 : maxResults, 1, MaxResultsLimit);
            try
            {
                List<Dictionary<string, object>> all = CollectCandidates(needle, includeInactive, false, null);
                all.Sort(CompareCandidates);
                List<Dictionary<string, object>> candidates = new List<Dictionary<string, object>>();
                int count = Math.Min(cap, all.Count);
                for (int i = 0; i < count; i++) candidates.Add(all[i]);

                Dictionary<string, object> result = new Dictionary<string, object>
                {
                    ["kind"] = "uiAccessibilityCensus",
                    ["success"] = true,
                    ["totalCandidateCount"] = all.Count,
                    ["omittedCandidateCount"] = Math.Max(0, all.Count - count),
                    ["source"] = "Unity UI accessibility census; no screenshot captured",
                    ["scene"] = SceneManager.GetActiveScene().name ?? string.Empty,
                    ["frame"] = SafeFrameCount(),
                    ["applicationFocused"] = SafeApplicationFocused(),
                    ["runInBackground"] = SafeRunInBackground(),
                    ["screenWidth"] = Screen.width,
                    ["screenHeight"] = Screen.height,
                    ["query"] = needle,
                    ["includeInactive"] = includeInactive,
                    ["candidates"] = candidates
                };
                return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(result);
            }
            catch (Exception ex)
            {
                return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = "UI census failed closed: " + ex.GetType().Name
                });
            }
        }

        public static string TryClickTextJson(string label, bool exactMatch, string hierarchyPath)
        {
            string requested = (label ?? string.Empty).Trim();
            string requestedPath = (hierarchyPath ?? string.Empty).Trim();
            if (requested.Length == 0 && requestedPath.Length == 0)
            {
                return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["message"] = "label or hierarchyPath is required"
                });
            }

            try
            {
                List<Dictionary<string, object>> all = CollectCandidates(string.Empty, false, false, null);
                List<Dictionary<string, object>> matches = new List<Dictionary<string, object>>();
                for (int i = 0; i < all.Count; i++)
                {
                    Dictionary<string, object> row = all[i];
                    string rowLabel = row["label"] as string ?? string.Empty;
                    string rowPath = row["hierarchyPath"] as string ?? string.Empty;
                    if (requestedPath.Length > 0 && !string.Equals(rowPath, requestedPath, StringComparison.Ordinal)) continue;
                    bool labelMatches = requested.Length == 0 || (exactMatch
                        ? string.Equals(rowLabel, requested, StringComparison.OrdinalIgnoreCase)
                        : Contains(rowLabel, requested));
                    if (labelMatches) matches.Add(row);
                }
                matches.Sort(CompareCandidates);
                if (matches.Count != 1)
                {
                    List<Dictionary<string, object>> candidates = new List<Dictionary<string, object>>();
                    int count = Math.Min(MaxAmbiguousCandidates, matches.Count);
                    for (int i = 0; i < count; i++) candidates.Add(matches[i]);
                    return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(new Dictionary<string, object>
                    {
                        ["success"] = false,
                        ["label"] = requested,
                        ["exactMatch"] = exactMatch,
                        ["hierarchyPath"] = requestedPath,
                        ["matchCount"] = matches.Count,
                        ["candidates"] = candidates,
                        ["message"] = matches.Count == 0
                            ? "no active UI control matched the requested label"
                            : "label is ambiguous; pass the hierarchyPath returned by the UI census"
                    });
                }

                Dictionary<string, object> candidate = matches[0];
                if (!(bool)candidate["interactable"])
                {
                    return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(new Dictionary<string, object>
                    {
                        ["success"] = false,
                        ["candidate"] = candidate,
                        ["message"] = "matched control is not interactable"
                    });
                }
                if (!(bool)candidate["onScreen"])
                {
                    return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(new Dictionary<string, object>
                    {
                        ["success"] = false,
                        ["candidate"] = candidate,
                        ["message"] = "matched control is outside the screen bounds"
                    });
                }
                int instanceId = (int)candidate["gameObjectId"];
                GameObject target = ResolveGameObject(instanceId);
                if (target == null)
                {
                    return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(new Dictionary<string, object>
                    {
                        ["success"] = false,
                        ["candidate"] = candidate,
                        ["message"] = "matched control disappeared before click delivery"
                    });
                }

                UiClickResult click = RuntimeUiClickDirector.TryClickGameObject(target);
                Dictionary<string, object> output = new Dictionary<string, object>
                {
                    ["success"] = click.Clicked,
                    ["source"] = "Unity UI text resolution; no screenshot captured",
                    ["candidate"] = candidate,
                    ["click"] = new Dictionary<string, object>
                    {
                        ["clicked"] = click.Clicked,
                        ["via"] = click.Via,
                        ["targetName"] = click.TargetName,
                        ["topHitName"] = click.TopHitName,
                        ["screenX"] = click.ScreenX,
                        ["screenY"] = click.ScreenY,
                        ["phases"] = click.Phases ?? Array.Empty<string>(),
                        ["message"] = click.Message
                    }
                };
                return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(output);
            }
            catch (Exception ex)
            {
                return AgentDiagnosticsFormatter.SerializeUiAccessibilityPayload(new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["label"] = requested,
                    ["error"] = "UI text click failed closed: " + ex.GetType().Name
                });
            }
        }

        private static List<Dictionary<string, object>> CollectCandidates(string query, bool includeInactive, bool exactMatch, string hierarchyPath)
        {
            List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
            HashSet<int> seenGameObjects = new HashSet<int>();
            Selectable[] selectables = UnityEngine.Object.FindObjectsOfType<Selectable>(includeInactive);
            if (selectables != null)
            {
                for (int i = 0; i < selectables.Length; i++)
                {
                    Selectable selectable = selectables[i];
                    if (selectable == null || selectable.gameObject == null) continue;

                    bool enabled = false;
                    bool interactable = false;
                    try { enabled = selectable.enabled; } catch { }
                    try { interactable = selectable.IsInteractable(); } catch { }
                    AddCandidate(rows, seenGameObjects, selectable, selectable.gameObject, query, includeInactive,
                        exactMatch, hierarchyPath, selectable.GetType().Name, enabled, interactable);
                }
            }

            // Some native controls implement pointer handlers without exposing
            // a Unity Selectable. Include canvas-backed click handlers so
            // agents can discover them and use the same EventSystem route.
            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(includeInactive);
            if (behaviours != null)
            {
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour == null || behaviour.gameObject == null) continue;
                    Type type = behaviour.GetType();
                    bool isMichskyButton = string.Equals(type.FullName, MichskyButtonManagerTypeName, StringComparison.Ordinal);
                    bool isNativePointerControl = behaviour is IPointerClickHandler && IsCanvasUiControl(behaviour);
                    if (!isMichskyButton && !isNativePointerControl) continue;

                    bool enabled = false;
                    try { enabled = behaviour.enabled; } catch { }
                    bool interactable;
                    if (isMichskyButton)
                    {
                        interactable = ReadBooleanMember(behaviour, "isInteractable");
                        Selectable targetButton = ReadInstanceMember(behaviour, "targetButton") as Selectable;
                        if (targetButton != null)
                        {
                            try { interactable = interactable && targetButton.IsInteractable(); }
                            catch { interactable = false; }
                        }
                    }
                    else
                    {
                        object nativeInteractable = ReadInstanceMember(behaviour, "isInteractable") ??
                            ReadInstanceMember(behaviour, "interactable");
                        interactable = nativeInteractable is bool nativeState ? nativeState : enabled;
                    }

                    AddCandidate(rows, seenGameObjects, behaviour, behaviour.gameObject, query, includeInactive,
                        exactMatch, hierarchyPath, type.Name, enabled, interactable);
                }
            }

            return rows;
        }

        private static bool IsCanvasUiControl(MonoBehaviour behaviour)
        {
            if (behaviour == null || behaviour.GetComponent<RectTransform>() == null) return false;
            for (Transform current = behaviour.transform; current != null; current = current.parent)
            {
                if (current.GetComponent<Canvas>() != null) return true;
            }
            return false;
        }

        private static void AddCandidate(
            List<Dictionary<string, object>> rows,
            HashSet<int> seenGameObjects,
            Component control,
            GameObject gameObject,
            string query,
            bool includeInactive,
            bool exactMatch,
            string hierarchyPath,
            string controlType,
            bool enabled,
            bool canInteract)
        {
            if (control == null || gameObject == null) return;
            bool active = gameObject.activeInHierarchy;
            if (!includeInactive && !active) return;

            string path = (gameObject.scene.name ?? string.Empty) + "::" + GetHierarchyPath(gameObject.transform);
            if (!string.IsNullOrEmpty(hierarchyPath) && !string.Equals(path, hierarchyPath, StringComparison.Ordinal)) return;
            string label = GetAccessibleLabel(control);
            if (!Matches(query, label, gameObject.name, path, exactMatch)) return;

            int instanceId = gameObject.GetInstanceID();
            if (!seenGameObjects.Add(instanceId))
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    Dictionary<string, object> existing = rows[i];
                    if ((int)existing["gameObjectId"] != instanceId) continue;
                    if (string.IsNullOrWhiteSpace(existing["label"] as string) && !string.IsNullOrWhiteSpace(label))
                    {
                        existing["label"] = label;
                    }
                    existing["enabled"] = (bool)existing["enabled"] || enabled;
                    existing["interactable"] = (bool)existing["interactable"] || (active && enabled && canInteract);
                    return;
                }
                return;
            }

            RectTransform rect = gameObject.GetComponent<RectTransform>();
            Dictionary<string, object> screenRect = ReadScreenRect(rect, out bool onScreen);
            bool interactable = active && enabled && canInteract;
            bool visible = active && onScreen && IsCanvasVisible(control);
            rows.Add(new Dictionary<string, object>
            {
                ["label"] = label,
                ["gameObjectName"] = gameObject.name ?? string.Empty,
                ["hierarchyPath"] = path,
                ["scene"] = gameObject.scene.name ?? string.Empty,
                ["gameObjectId"] = instanceId,
                ["selectableType"] = controlType,
                ["controlType"] = controlType,
                ["active"] = active,
                ["enabled"] = enabled,
                ["interactable"] = interactable,
                ["visible"] = visible,
                ["selected"] = EventSystemSelectionIs(gameObject),
                ["onScreen"] = onScreen,
                ["screenRect"] = screenRect
            });
        }

        private static bool Matches(string query, string label, string name, string path, bool exactMatch)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (exactMatch)
            {
                return string.Equals(label, query, StringComparison.OrdinalIgnoreCase);
            }
            return Contains(label, query) || Contains(name, query) || Contains(path, query);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetAccessibleLabel(Component control)
        {
            if (control == null || control.gameObject == null) return string.Empty;
            string directLabel = ReadInstanceMember(control, "buttonText") as string;
            if (!string.IsNullOrWhiteSpace(directLabel)) return directLabel.Trim();
            try
            {
                Text text = control.GetComponentInChildren<Text>(true);
                if (text != null && !string.IsNullOrWhiteSpace(text.text)) return text.text.Trim();
            }
            catch { }

            // TMP is read by its public text property without taking a hard
            // assembly dependency, so projects without TMP can still load.
            try
            {
                Component[] children = control.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < children.Length; i++)
                {
                    Component child = children[i];
                    if (child == null) continue;
                    Type type = child.GetType();
                    if (type.Name != "TMP_Text" && type.Name != "TextMeshProUGUI" && type.Name != "TextMeshPro") continue;
                    PropertyInfo textProperty = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                    if (textProperty == null || textProperty.PropertyType != typeof(string) || !textProperty.CanRead) continue;
                    string value = textProperty.GetValue(child, null) as string;
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }
            }
            catch { }
            return string.Empty;
        }

        private static object ReadInstanceMember(Component component, string memberName)
        {
            if (component == null || string.IsNullOrEmpty(memberName)) return null;
            try
            {
                Type type = component.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo field = type.GetField(memberName, flags);
                if (field != null) return field.GetValue(component);
                PropertyInfo property = type.GetProperty(memberName, flags);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(component, null);
                }
            }
            catch { }
            return null;
        }

        private static bool ReadBooleanMember(Component component, string memberName)
        {
            object value = ReadInstanceMember(component, memberName);
            return value is bool result && result;
        }

        private static Dictionary<string, object> ReadScreenRect(RectTransform rect, out bool onScreen)
        {
            onScreen = false;
            if (rect == null) return null;
            try
            {
                Canvas canvas = rect.GetComponentInParent<Canvas>();
                Camera camera = RuntimeUiScreenProjection.GetProjectionCamera(canvas);
                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                float minX = float.PositiveInfinity;
                float minY = float.PositiveInfinity;
                float maxX = float.NegativeInfinity;
                float maxY = float.NegativeInfinity;
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                    minX = Math.Min(minX, point.x);
                    minY = Math.Min(minY, point.y);
                    maxX = Math.Max(maxX, point.x);
                    maxY = Math.Max(maxY, point.y);
                }
                onScreen = maxX > 0f && minX < Screen.width && maxY > 0f && minY < Screen.height;
                return new Dictionary<string, object>
                {
                    ["xMin"] = RoundFinite(minX),
                    ["yMin"] = RoundFinite(minY),
                    ["xMax"] = RoundFinite(maxX),
                    ["yMax"] = RoundFinite(maxY),
                    ["width"] = RoundFinite(maxX - minX),
                    ["height"] = RoundFinite(maxY - minY)
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool IsCanvasVisible(Component control)
        {
            if (control == null || control.transform == null) return false;
            float alpha = 1f;
            for (Transform current = control.transform; current != null; current = current.parent)
            {
                Canvas canvas = current.GetComponent<Canvas>();
                if (canvas != null && !canvas.enabled) return false;
                CanvasGroup group = current.GetComponent<CanvasGroup>();
                if (group != null)
                {
                    alpha *= Mathf.Clamp01(group.alpha);
                    if (alpha <= 0.001f) return false;
                    if (group.ignoreParentGroups) break;
                }
            }
            return alpha > 0.001f;
        }

        private static double RoundFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0d : Math.Round(value, 2);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            List<string> parts = new List<string>();
            for (Transform current = transform; current != null; current = current.parent)
            {
                parts.Add(current.name ?? string.Empty);
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static int CompareCandidates(Dictionary<string, object> left, Dictionary<string, object> right)
        {
            int label = string.Compare(left["label"] as string, right["label"] as string, StringComparison.OrdinalIgnoreCase);
            return label != 0 ? label : string.Compare(left["hierarchyPath"] as string, right["hierarchyPath"] as string, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EventSystemSelectionIs(GameObject target)
        {
            try { return UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject == target; }
            catch { return false; }
        }

        private static int SafeFrameCount()
        {
            try { return Time.frameCount; } catch { return -1; }
        }

        private static bool SafeApplicationFocused()
        {
            try { return Application.isFocused; } catch { return false; }
        }

        private static bool SafeRunInBackground()
        {
            try { return Application.runInBackground; } catch { return false; }
        }

        private static GameObject ResolveGameObject(int instanceId)
        {
            GameObject[] gameObjects = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
            if (gameObjects != null)
            {
                for (int i = 0; i < gameObjects.Length; i++)
                {
                    GameObject gameObject = gameObjects[i];
                    if (gameObject != null && gameObject.GetInstanceID() == instanceId) return gameObject;
                }
            }
            return null;
        }
    }
}
