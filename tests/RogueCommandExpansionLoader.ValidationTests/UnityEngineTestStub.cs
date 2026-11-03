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
        private static int _nextInstanceId = 1000;
        private int _instanceId = System.Threading.Interlocked.Increment(ref _nextInstanceId);
        public int GetInstanceID() => _instanceId;
        public static T FindObjectOfType<T>() where T : class => null;
        public static void Destroy(Object obj) { }
        public static void Destroy(Object obj, float t) { }
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

    public struct Bounds
    {
        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
        public Vector3 extents { get; set; }

        public Bounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
            this.extents = size / 2f;
        }
    }

    public class Component : Object
    {
        public string name { get => gameObject?.name ?? string.Empty; set { if (gameObject != null) gameObject.name = value; } }
        private GameObject _gameObject;
        public GameObject gameObject
        {
            get
            {
                if (_gameObject == null)
                {
                    _gameObject = new GameObject();
                    _gameObject.AddComponentExisting(this);
                }
                return _gameObject;
            }
            set => _gameObject = value;
        }

        public Transform transform => gameObject?.transform;
        public string tag { get => gameObject?.tag ?? ""; set { if (gameObject != null) gameObject.tag = value; } }
        public T GetComponent<T>() where T : Component => gameObject?.GetComponent<T>();
        public T[] GetComponents<T>() where T : Component => gameObject?.GetComponents<T>() ?? System.Array.Empty<T>();
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component => gameObject?.GetComponentInChildren<T>(includeInactive);
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component => gameObject?.GetComponentsInChildren<T>(includeInactive) ?? System.Array.Empty<T>();
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

    public sealed class Transform : Component, System.Collections.IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localEulerAngles { get; set; }
        public Vector3 localScale { get; set; } = new Vector3(1f, 1f, 1f);
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        private Transform _parent;
        public Transform parent
        {
            get => _parent;
            set
            {
                if (_parent != null)
                {
                    _parent._children.Remove(this);
                }
                _parent = value;
                if (_parent != null && !_parent._children.Contains(this))
                {
                    _parent._children.Add(this);
                }
            }
        }
        public int childCount => _children.Count;
        private readonly System.Collections.Generic.List<Transform> _children = new();

        public Transform GetChild(int index) => (index >= 0 && index < _children.Count) ? _children[index] : null;
        public void AddChild(Transform child)
        {
            if (child != null && !_children.Contains(child))
            {
                child.parent = this;
            }
        }
        public System.Collections.IEnumerator GetEnumerator() => _children.GetEnumerator();
    }

    public struct Vector2
    {
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public class ParticleSystem : Component
    {
        public void Play() { }
        public void Stop() { }
        public bool isPlaying => false;
    }

    public class AudioSource : Component
    {
        public void Play() { }
        public void Stop() { }
        public bool isPlaying => false;
        public float pitch { get; set; } = 1f;
        public float volume { get; set; } = 1f;
        public void PlayOneShot(object clip, float volumeScale = 1f) { }
    }

    public class MaterialPropertyBlock
    {
        public void SetColor(string name, Color value) { }
        public void SetColor(int nameID, Color value) { }
        public void SetFloat(string name, float value) { }
        public void SetFloat(int nameID, float value) { }
        public void Clear() { }
    }

    public class Renderer : Component
    {
        public bool enabled { get; set; } = true;
        public bool isVisible { get; set; } = true;
        public Bounds bounds { get; set; } = new Bounds(Vector3.zero, new Vector3(1f, 1f, 1f));
        public Material[] sharedMaterials { get; set; } = System.Array.Empty<Material>();
        public Material[] materials { get => sharedMaterials; set => sharedMaterials = value; }
        public Material sharedMaterial { get => sharedMaterials != null && sharedMaterials.Length > 0 ? sharedMaterials[0] : null; set => sharedMaterials = value != null ? new[] { value } : System.Array.Empty<Material>(); }
        public Material material { get => sharedMaterial; set => sharedMaterial = value; }
        public void SetPropertyBlock(MaterialPropertyBlock properties) { }
        public void GetPropertyBlock(MaterialPropertyBlock properties) { }
    }

    public class MeshRenderer : Renderer
    {
    }

    public class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh { get; set; }
        public Transform rootBone { get; set; }
        public Transform[] bones { get; set; } = System.Array.Empty<Transform>();
    }

    public class ParticleSystemRenderer : Renderer
    {
    }

    public class MeshFilter : Component
    {
        public Mesh sharedMesh { get; set; }
        public Mesh mesh { get => sharedMesh; set => sharedMesh = value; }
    }

    public class Mesh : Object
    {
        public string name { get; set; } = string.Empty;
        public int vertexCount { get; set; }
        public int subMeshCount { get; set; } = 1;
        public Color[] colors { get; set; } = System.Array.Empty<Color>();
        public Color[] colors32 { get; set; } = System.Array.Empty<Color>();
    }

    public class Shader : Object
    {
        public string name { get; set; } = string.Empty;
    }

    public class Material : Object
    {
        public string name { get; set; } = string.Empty;
        public Shader shader { get; set; }
        public string[] shaderKeywords { get; set; } = System.Array.Empty<string>();
        public bool enableInstancing { get; set; }
    }

    public class LODGroup : Component
    {
        public int lodCount { get; set; } = 1;
    }

    public enum AnimatorControllerParameterType
    {
        Float = 1,
        Int = 3,
        Bool = 4,
        Trigger = 9
    }

    public class AnimatorControllerParameter
    {
        public string name { get; set; } = string.Empty;
        public AnimatorControllerParameterType type { get; set; }
        public bool defaultBool { get; set; }
        public float defaultFloat { get; set; }
        public int defaultInt { get; set; }
    }

    public class RuntimeAnimatorController : Object
    {
        public string name { get; set; } = string.Empty;
    }

    public class Avatar : Object
    {
        public string name { get; set; } = string.Empty;
        public bool isValid { get; set; } = true;
        public bool isHuman { get; set; } = false;
    }

    public class AnimationClip : Object
    {
        public string name { get; set; } = string.Empty;
        public float length { get; set; } = 1.0f;
    }

    public struct AnimatorClipInfo
    {
        public AnimationClip clip { get; set; }
        public float weight { get; set; }
    }

    public struct AnimatorStateInfo
    {
        public float normalizedTime { get; set; }
        public float speed { get; set; }
        public int shortNameHash { get; set; }
        private string _stateName;

        public AnimatorStateInfo(string stateName, float normalizedTime = 0f, float speed = 1f)
        {
            _stateName = stateName ?? string.Empty;
            this.normalizedTime = normalizedTime;
            this.speed = speed;
            this.shortNameHash = _stateName.GetHashCode();
        }

        public bool IsName(string name) => string.Equals(_stateName, name, System.StringComparison.OrdinalIgnoreCase);
    }

    public class Animator : Component
    {
        public bool isInitialized { get; set; } = true;
        public bool isActiveAndEnabled { get; set; } = true;
        public RuntimeAnimatorController runtimeAnimatorController { get; set; }
        public Avatar avatar { get; set; }
        public float speed { get; set; } = 1.0f;
        public AnimatorControllerParameter[] parameters { get; set; } = System.Array.Empty<AnimatorControllerParameter>();
        public AnimationClip activeClip { get; set; }
        public AnimatorStateInfo activeStateInfo { get; set; } = new AnimatorStateInfo("Base", 0f, 1f);

        private readonly System.Collections.Generic.Dictionary<string, bool> _bools = new(System.StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Generic.Dictionary<string, float> _floats = new(System.StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Generic.Dictionary<string, int> _ints = new(System.StringComparer.OrdinalIgnoreCase);

        public AnimatorClipInfo[] GetCurrentAnimatorClipInfo(int layerIndex)
        {
            if (activeClip != null)
            {
                return new[] { new AnimatorClipInfo { clip = activeClip, weight = 1f } };
            }
            return System.Array.Empty<AnimatorClipInfo>();
        }

        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layerIndex) => activeStateInfo;

        public bool GetBool(string name) => _bools.TryGetValue(name, out bool val) && val;
        public void SetBool(string name, bool value) => _bools[name] = value;

        public float GetFloat(string name) => _floats.TryGetValue(name, out float val) ? val : 0f;
        public void SetFloat(string name, float value) => _floats[name] = value;

        public int GetInteger(string name) => _ints.TryGetValue(name, out int val) ? val : 0;
        public void SetInteger(string name, int value) => _ints[name] = value;
    }

    public sealed class GameObject : Object
    {
        private readonly System.Collections.Generic.List<Component> _components = new();
        public string name { get; set; } = string.Empty;
        public string tag { get; set; } = "Enemy";
        public bool activeInHierarchy { get; set; } = true;
        public Transform transform { get; }
        public UnityEngine.SceneManagement.Scene scene { get; set; }

        public GameObject(string name = "")
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            _components.Add(transform);
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        }

        internal void AddComponentExisting(Component comp)
        {
            if (comp != null && !_components.Contains(comp))
            {
                _components.Add(comp);
            }
        }

        public Component GetComponent(string type)
        {
            if (string.IsNullOrEmpty(type)) return null;
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] != null &&
                    string.Equals(_components[i].GetType().Name, type, System.StringComparison.OrdinalIgnoreCase))
                {
                    return _components[i];
                }
            }
            return null;
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

        public T[] GetComponents<T>() where T : Component
        {
            var list = new System.Collections.Generic.List<T>();
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T match)
                {
                    list.Add(match);
                }
            }
            return list.ToArray();
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

        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component
        {
            T direct = GetComponent<T>();
            if (direct != null) return direct;
            if (transform != null)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    Transform child = transform.GetChild(i);
                    if (child != null)
                    {
                        T match = child.GetComponentInChildren<T>(includeInactive);
                        if (match != null) return match;
                    }
                }
            }
            return null;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
        {
            var list = new System.Collections.Generic.List<T>();
            list.AddRange(GetComponents<T>());
            if (transform != null)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    Transform child = transform.GetChild(i);
                    if (child != null)
                    {
                        list.AddRange(child.GetComponentsInChildren<T>(includeInactive));
                    }
                }
            }
            return list.ToArray();
        }
    }

    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);
        public static float Clamp01(float value) =>
            value < 0f ? 0f : (value > 1f ? 1f : value);
        public static int Clamp(int value, int min, int max) =>
            value < min ? min : (value > max ? max : value);

        public static int Max(int a, int b) => a > b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int RoundToInt(float f) => (int)System.Math.Round(f);
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
