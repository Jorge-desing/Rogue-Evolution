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

    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static bool operator ==(Color left, Color right) =>
            left.r == right.r && left.g == right.g && left.b == right.b && left.a == right.a;

        public static bool operator !=(Color left, Color right) => !(left == right);

        public override bool Equals(object obj) => obj is Color other && this == other;

        public override int GetHashCode()
        {
            int hash = 17;
            hash = (hash * 31) + r.GetHashCode();
            hash = (hash * 31) + g.GetHashCode();
            hash = (hash * 31) + b.GetHashCode();
            hash = (hash * 31) + a.GetHashCode();
            return hash;
        }
    }

    public class Camera : Component
    {
        private static Camera _main;
        public static Camera main
        {
            get => _main ??= new Camera();
            set => _main = value;
        }

        public float fieldOfView { get; set; } = 45f;
        public bool orthographic { get; set; } = false;
        public float orthographicSize { get; set; } = 10f;
    }

    public class Object
    {
        public static T FindObjectOfType<T>() where T : class => null;
        public static void Destroy(Object obj) { }
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
        public string tag { get => gameObject?.tag ?? ""; set { if (gameObject != null) gameObject.tag = value; } }
        public T GetComponent<T>() where T : Component => gameObject?.GetComponent<T>();
    }

    public class MonoBehaviour : Component
    {
    }

    public class Collider : Component
    {
        public bool isTrigger { get; set; }
    }

    public class SphereCollider : Collider
    {
        public float radius { get; set; }
    }

    public sealed class GameObject : Object
    {
        private readonly System.Collections.Generic.List<Component> _components = new();
        public string name { get; set; } = string.Empty;
        public string tag { get; set; } = "Enemy";
        public bool activeInHierarchy { get; set; } = true;
        public Transform transform { get; set; } = new();
        public UnityEngine.SceneManagement.Scene scene { get; set; }

        public GameObject(string name = "")
        {
            this.name = name;
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
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
        public static int Clamp(int value, int min, int max) =>
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
        public static float timeScale { get; set; } = 1.0f;
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

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name { get; set; }
        public bool isLoaded { get; set; }
        public int handle { get; set; }

        public Scene(string name, bool isLoaded, int handle)
        {
            this.name = name;
            this.isLoaded = isLoaded;
            this.handle = handle;
        }

        public bool IsValid() => !string.IsNullOrEmpty(name);
    }

    public static class SceneManager
    {
        public static string ActiveSceneName { get; set; } = "MainDesertScene";
        public static int ActiveSceneHandle { get; set; } = 1;
        public static Scene GetActiveScene() => new Scene { name = ActiveSceneName, isLoaded = true, handle = ActiveSceneHandle };
        public static void MoveGameObjectToScene(GameObject gameObject, Scene scene) => gameObject.scene = scene;
    }
}

namespace UnityEngine.Audio
{
    public class AudioMixer : Object
    {
        public bool SetFloat(string name, float value) => true;
        public bool GetFloat(string name, out float value) { value = 0f; return true; }
    }
}
