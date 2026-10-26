namespace UnityEngine
{
    public struct Vector2Int
    {
        public int x;
        public int y;

        public Vector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public float sqrMagnitude => x * x + y * y + z * z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => new(0f, 0f, 0f);
        public static Vector3 forward => new(0f, 0f, 1f);
        public static Vector3 operator /(Vector3 v, float f) => new(v.x / f, v.y / f, v.z / f);
        public static Vector3 operator /(Vector3 v, int i) => new(v.x / i, v.y / i, v.z / i);

        public static Vector3 operator -(Vector3 left, Vector3 right) =>
            new(left.x - right.x, left.y - right.y, left.z - right.z);

        public static Vector3 operator +(Vector3 left, Vector3 right) =>
            new(left.x + right.x, left.y + right.y, left.z + right.z);

        public static Vector3 operator *(Vector3 v, float f) =>
            new(v.x * f, v.y * f, v.z * f);

        public static Vector3 operator *(float f, Vector3 v) =>
            new(v.x * f, v.y * f, v.z * f);

        public Vector3 normalized
        {
            get
            {
                float mag = (float)System.Math.Sqrt(sqrMagnitude);
                return mag > 1e-5f ? new(x / mag, y / mag, z / mag) : new(0f, 0f, 0f);
            }
        }

        public static float Distance(Vector3 left, Vector3 right) =>
            (float)System.Math.Sqrt((left - right).sqrMagnitude);
    }

    public struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z) => new Quaternion();
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }

    public class Object
    {
    }

    public class Sprite : Object
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

    public class Component : Object
    {
        public GameObject gameObject { get; set; } = new();
        public Transform transform { get; set; } = new();
    }

    public class MonoBehaviour : Component
    {
    }

    public sealed class GameObject : Object
    {
        private readonly System.Collections.Generic.List<Component> _components = new();
        public string name { get; set; } = string.Empty;
        public string tag { get; set; } = "Enemy";
        public bool activeInHierarchy { get; set; } = true;

        public GameObject(string name = "")
        {
            this.name = name;
        }

        public T GetComponent<T>() where T : Component
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T match)
                {
                    return match;
                }
            }

            return null;
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var comp = new T
            {
                gameObject = this
            };
            _components.Add(comp);
            return comp;
        }
    }

    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);

        public static int Max(int a, int b) => a > b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int RoundToInt(float f) => (int)System.Math.Round(f);
    }

    public sealed class Transform : Object
    {
        public Vector3 position { get; set; }
    }

    public static class Time
    {
        public static float time { get; set; }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
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
