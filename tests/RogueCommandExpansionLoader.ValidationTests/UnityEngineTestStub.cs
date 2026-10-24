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

        public static Vector3 forward => new(0f, 0f, 1f);

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

    public sealed class GameObject : Object
    {
        public string name { get; set; } = string.Empty;
        public string tag { get; set; } = "Enemy";

        public GameObject(string name = "")
        {
            this.name = name;
        }
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
