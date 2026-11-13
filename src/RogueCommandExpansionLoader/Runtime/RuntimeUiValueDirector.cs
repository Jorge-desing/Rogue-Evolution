using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace RogueCommandExpansionLoader.Runtime
{
    public static class RuntimeUiValueDirector
    {
        public static string SetValue(string path, int id, string kind, string valueJson, bool submit)
        {
            bool invoked = false;
            object before = null;
            try
            {
                if (valueJson != null && valueJson.StartsWith("base64:", StringComparison.Ordinal))
                    valueJson = new System.Text.UTF8Encoding(false, true).GetString(Convert.FromBase64String(valueJson.Substring(7)));
                // The installed bridge can retain one string-argument escape
                // layer. Normalize that known wire form once, before parsing.
                // Ordinary JSON (including embedded backslashes) stays intact.
                if (kind == "text" && valueJson != null && valueJson.StartsWith("\\\"", StringComparison.Ordinal) && valueJson.EndsWith("\\\"", StringComparison.Ordinal))
                    valueJson = Newtonsoft.Json.JsonConvert.DeserializeObject<string>("\"" + valueJson + "\"");
                JToken value = JToken.Parse(valueJson);
                JObject census = JObject.Parse(RuntimeUiAccessibility.GetCensusJson(path, false, 100));
                var rows = ((JArray)census["candidates"]).OfType<JObject>().Where(r => (string)r["hierarchyPath"] == path).ToArray();
                if (rows.Length != 1 || (int)rows[0]["gameObjectId"] != id || (string)rows[0]["scene"] != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
                    throw new ArgumentException("Target is missing, stale, ambiguous or outside the active scene");
                JObject row = rows[0];
                if (!(bool)row["interactable"] || !(bool)row["visible"]) throw new ArgumentException("Target is not visible and interactable");
                var objects = UnityEngine.Object.FindObjectsOfType<Transform>().Where(t => t.gameObject.GetInstanceID() == id).ToArray();
                if (objects.Length != 1) throw new ArgumentException("Target disappeared");
                GameObject go = objects[0].gameObject;
                Component[] controls = go.GetComponents<Component>().Where(c => c != null && (
                    (kind == "slider" && c is Slider) || (kind == "toggle" && c is Toggle) ||
                    (kind == "switch" && c is Michsky.UI.Reach.SwitchManager) ||
                    (kind == "dropdown" && (c is Dropdown || c is Michsky.UI.Reach.Dropdown)) ||
                    (kind == "text" && c.GetType().FullName == "TMPro.TMP_InputField"))).ToArray();
                if (controls.Length != 1 || !(controls[0] is Behaviour behaviour) || !behaviour.isActiveAndEnabled)
                    throw new ArgumentException("Expected one enabled component of the requested kind");
                if (controls[0] is Selectable selectedControl && !selectedControl.IsInteractable())
                    throw new ArgumentException("Requested value component is not interactable");
                before = row["value"];
                Func<object> read;
                if (kind == "slider" && go.GetComponent<Slider>() is Slider slider)
                {
                    if (value.Type != JTokenType.Float && value.Type != JTokenType.Integer) throw new ArgumentException("Expected number");
                    float number = value.Value<float>();
                    if (float.IsNaN(number) || float.IsInfinity(number) || number < slider.minValue || number > slider.maxValue || (slider.wholeNumbers && number != Mathf.Round(number))) throw new ArgumentException("Slider value outside native bounds");
                    invoked = true; slider.value = number; read = () => slider.value;
                }
                else if (kind == "toggle" && go.GetComponent<Toggle>() is Toggle toggle)
                {
                    if (value.Type != JTokenType.Boolean) throw new ArgumentException("Expected boolean");
                    invoked = true; toggle.isOn = value.Value<bool>(); read = () => toggle.isOn;
                }
                else if (kind == "switch" && go.GetComponent<Michsky.UI.Reach.SwitchManager>() is Michsky.UI.Reach.SwitchManager sw)
                {
                    if (value.Type != JTokenType.Boolean || !sw.isInteractable || !sw.isActiveAndEnabled) throw new ArgumentException("Expected interactable switch and boolean");
                    before = sw.isOn;
                    if (sw.isOn != value.Value<bool>()) { invoked = true; if (value.Value<bool>()) sw.SetOn(); else sw.SetOff(); }
                    read = () => sw.isOn;
                }
                else if (kind == "dropdown" && go.GetComponent<Dropdown>() is Dropdown dropdown)
                {
                    if (value.Type != JTokenType.Integer || value.Value<int>() < 0 || value.Value<int>() >= dropdown.options.Count) throw new ArgumentException("Invalid option index");
                    invoked = true; dropdown.value = value.Value<int>(); read = () => dropdown.value;
                }
                else if (kind == "dropdown" && go.GetComponent<Michsky.UI.Reach.Dropdown>() is Michsky.UI.Reach.Dropdown native)
                {
                    if (!native.isActiveAndEnabled || value.Type != JTokenType.Integer || value.Value<int>() < 0 || value.Value<int>() >= native.items.Count) throw new ArgumentException("Invalid option index or disabled dropdown");
                    var item = native.items[value.Value<int>()];
                    if (item.isInvisible || item.itemButton == null || !item.itemButton.isInteractable) throw new ArgumentException("Native option is unavailable");
                    invoked = true; item.itemButton.InvokeOnClick(); read = () => native.selectedItemIndex;
                }
                else if (kind == "text")
                {
                    Component input = go.GetComponents<Component>().SingleOrDefault(c => c != null && c.GetType().FullName == "TMPro.TMP_InputField");
                    if (!(input is Selectable selectable) || !selectable.isActiveAndEnabled || !selectable.IsInteractable() || value.Type != JTokenType.String) throw new ArgumentException("Expected interactable TMP input and string");
                    Type type = input.GetType();
                    if ((bool)type.GetProperty("readOnly").GetValue(input)) throw new ArgumentException("Input is read-only");
                    string text = value.Value<string>();
                    int limit = (int)type.GetProperty("characterLimit").GetValue(input);
                    if (text.Length > 4096 || (limit > 0 && text.Length > limit)) throw new ArgumentException("Text exceeds input limit");
                    invoked = true; type.GetProperty("text").SetValue(input, text);
                    if (submit)
                    {
                        object evt = type.GetProperty("onEndEdit").GetValue(input);
                        string acceptedText = (string)type.GetProperty("text").GetValue(input);
                        if (acceptedText != text) throw new ArgumentException("Native validation changed text; inspect state before submitting");
                        evt.GetType().GetMethod("Invoke", new[] { typeof(string) }).Invoke(evt, new object[] { acceptedText });
                    }
                    read = () => type.GetProperty("text").GetValue(input);
                }
                else throw new ArgumentException("Control type does not match requested kind");
                object after = read();
                bool observed = kind == "slider"
                    ? Math.Abs(Convert.ToDouble(after) - value.Value<double>()) <= 0.000001 * Math.Max(1, Math.Abs(value.Value<double>()))
                    : JToken.DeepEquals(JToken.FromObject(after), value);
                return new JObject { ["success"] = observed, ["nativeCallInvoked"] = invoked, ["effectObserved"] = observed, ["before"] = before == null ? JValue.CreateNull() : JToken.FromObject(before), ["after"] = JToken.FromObject(after) }.ToString();
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["nativeCallInvoked"] = invoked, ["effectObserved"] = false, ["error"] = ex.GetType().Name + ": " + ex.Message }.ToString();
            }
        }
    }
}
