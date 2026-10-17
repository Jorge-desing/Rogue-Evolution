namespace UnityEngine
{
    public class Object
    {
    }

    public enum HideFlags
    {
        HideAndDontSave = 61
    }

    public class ScriptableObject : Object
    {
        public string name { get; set; } = string.Empty;
        public HideFlags hideFlags { get; set; }

        public static ScriptableObject CreateInstance(System.Type type)
        {
            return (ScriptableObject)System.Activator.CreateInstance(type)!;
        }
    }

    public static class Resources
    {
        public static Object Load(string path, System.Type type) => null;
    }

    internal static class Application
    {
        public static string unityVersion { get; set; } = "2021.3.45f2";
    }
}
