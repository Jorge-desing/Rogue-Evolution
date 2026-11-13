using Newtonsoft.Json.Linq;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine;

int checks = 0;
void Check(bool condition) { checks++; if (!condition) throw new Exception("Failed assertion " + checks); }
JObject Set(string kind, string value, bool submit = false, int id = 1) => JObject.Parse(RuntimeUiValueDirector.SetValue("Menu::/Control", id, kind, value, submit));
void Setup(Component control) { ObjectStore.Components = new[] {control}; ObjectStore.Row = JObject.Parse("{ 'hierarchyPath':'Menu::/Control','gameObjectId':1,'scene':'Menu','interactable':true,'visible':true,'value':0 }"); }
var slider = new UnityEngine.UI.Slider(); Setup(slider);
Check((bool)Set("slider", "0.5")["success"] && slider.value == 0.5f);
Check(!(bool)Set("slider", "2")["nativeCallInvoked"]);
Check(!(bool)Set("slider", "0.5", id:2)["nativeCallInvoked"]);
ObjectStore.Row["visible"] = false; Check(!(bool)Set("slider", "0.5")["nativeCallInvoked"]);
Setup(slider); slider.isActiveAndEnabled = false; Check(!(bool)Set("slider", "0.5")["nativeCallInvoked"]);
var sw = new Michsky.UI.Reach.SwitchManager(); Setup(sw);
Check((bool)Set("switch", "true")["success"] && sw.calls == 1);
Check((bool)Set("switch", "true")["success"] && sw.calls == 1);
var native = new Michsky.UI.Reach.Dropdown(); native.items.Add(new()); native.items[0].itemButton.action = () => native.selectedItemIndex = 0; Setup(native);
Check((bool)Set("dropdown", "0")["success"] && native.items[0].itemButton.calls == 1);
native.items[0].isInvisible = true; Check(!(bool)Set("dropdown", "0")["nativeCallInvoked"]);
var input = new TMPro.TMP_InputField(); Setup(input);
Check((bool)Set("text", "\"hello\"", true)["success"] && input.onEndEdit.calls == 1);
input.readOnly = true; Check(!(bool)Set("text", "\"other\"")["nativeCallInvoked"]);
input.readOnly = false; input.validate = true;
var result = Set("text", "\"other\"", true);
Check((bool)result["nativeCallInvoked"] && !(bool)result["effectObserved"] && input.onEndEdit.calls == 1);
Setup(sw); ObjectStore.Components = new Component[] {sw, new Michsky.UI.Reach.SwitchManager()};
Check(!(bool)Set("switch", "false")["nativeCallInvoked"]);
Console.WriteLine($"PASS {checks} simulated director assertions; no Unity runtime evidence");

namespace UnityEngine {
 public static class ObjectStore { public static Component[] Components = Array.Empty<Component>(); public static JObject Row; }
 public class Object { public static T[] FindObjectsOfType<T>() where T:class => new[]{new Transform()} as T[] ?? Array.Empty<T>(); }
 public class Component:Object { public GameObject gameObject = new(); public Type GetTypeForTest()=>GetType(); }
 public class Behaviour:Component { public bool isActiveAndEnabled = true; }
 public class Transform:Component {}
 public class GameObject { public int GetInstanceID()=>1; public T[] GetComponents<T>() => ObjectStore.Components.OfType<T>().ToArray(); public T GetComponent<T>() => GetComponents<T>().FirstOrDefault(); }
 public static class Mathf { public static float Round(float x)=>(float)Math.Round(x); }
}
namespace UnityEngine.SceneManagement { public struct Scene { public string name=>"Menu"; } public static class SceneManager { public static Scene GetActiveScene()=>new(); } }
namespace UnityEngine.UI {
 public class Selectable:Behaviour { public bool interactable=true; public bool IsInteractable()=>interactable; }
 public class Slider:Selectable { public float value; public float minValue=0, maxValue=1; public bool wholeNumbers; }
 public class Toggle:Selectable { public bool isOn; }
 public class Dropdown:Selectable { public int value; public List<string> options=new(); }
}
namespace Michsky.UI.Reach {
 public class SwitchManager:Behaviour { public bool isOn, isInteractable=true; public int calls; public void SetOn(){isOn=true;calls++;} public void SetOff(){isOn=false;calls++;} }
 public class ButtonManager { public bool isInteractable=true; public int calls; public Action action; public void InvokeOnClick(){calls++;action?.Invoke();} }
 public class Dropdown:Behaviour { public int selectedItemIndex=-1; public List<Item> items=new(); public class Item { public bool isInvisible; public ButtonManager itemButton=new(); } }
}
namespace TMPro {
 public class TMP_InputField:UnityEngine.UI.Selectable { private string current=""; public bool readOnly {get;set;} public int characterLimit {get;set;} public bool validate; public string text {get=>current;set=>current=validate?"changed":value;} public EndEvent onEndEdit {get;}=new(); }
 public class EndEvent { public int calls; public void Invoke(string text){calls++;} }
}
namespace RogueCommandExpansionLoader.Runtime { public static class RuntimeUiAccessibility { public static string GetCensusJson(string path,bool inactive,int limit)=>new JObject{["candidates"]=new JArray(ObjectStore.Row.DeepClone())}.ToString(); } }
