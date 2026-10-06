// Inert API substitutes for testing plugin logic; no Unity or game code executes here.
using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace BepInEx
{
    public class BepInPlugin : Attribute { public BepInPlugin(string a, string b, string c) {} }
    public class BepInProcess : Attribute { public BepInProcess(string a) {} }
    public class BaseUnityPlugin { public TestLogger Logger = new TestLogger(); }
    public class TestLogger { public void LogInfo(object x) {} public void LogWarning(object x) { Console.WriteLine(x); } }
}
namespace UnityEngine
{
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int x) {} }
    public class Object
    {
        public bool destroyed;
        public HideFlags hideFlags;
        public static void Destroy(Object value) { if (value != null) value.destroyed = true; }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || (!ReferenceEquals(a, null) && a.destroyed);
            bool bn = ReferenceEquals(b, null) || (!ReferenceEquals(b, null) && b.destroyed);
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public enum HideFlags { HideAndDontSave }
    public class Shader : Object
    {
        public string name; public bool isSupported = true;
        public static bool unavailable;
        public static Shader Find(string name) { return unavailable ? null : new Shader { name = name }; }
    }
    public class Material : Object
    {
        public string name; public Shader shader;
        public Material() {}
        public Material(Shader value) { shader = value; }
        public Dictionary<string,int> properties = new Dictionary<string,int>();
        public bool HasProperty(string key) { return key == "_ColorMask"; }
        public void SetInt(string key, int value) { properties[key] = value; }
        public int passCount { get { return 1; } }
        public string GetPassName(int index) { return "Default"; }
        public void SetShaderPassEnabled(string name, bool enabled) {}
    }
    public class Transform { public string name; public Transform parent; }
    public class GameObject
    {
        public string name;
        public bool activeInHierarchy = true;
        public Transform transform;
        public SceneManagement.Scene scene = new SceneManagement.Scene { valid = true, isLoaded = true, name = "TestScene" };
    }
    public class Renderer : Object
    {
        public int id;
        public bool enabled = true;
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        private Material[] shared = new Material[0];
        public Material[] sharedMaterials { get { return (Material[])shared.Clone(); } set { shared = (Material[])value.Clone(); } }
#if UNITY_INSTANCE_ID_OBSOLETE
        [Obsolete("GetInstanceID is deprecated. Use GetEntityId instead.")]
#endif
        public int GetInstanceID() { return id; }
    }
    public class SkinnedMeshRenderer : Renderer {}
    public class Camera {}
    public static class Resources
    {
        public static List<Renderer> world = new List<Renderer>(); public static int scans;
        public static T[] FindObjectsOfTypeAll<T>() where T : Object
        { scans++; var list = new List<T>(); foreach (Renderer r in world) if (r != null) list.Add((T)(Object)r); return list.ToArray(); }
    }
    public static class Time { public static float realtimeSinceStartup; }
    public static class Application { public static bool runInBackground; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public bool valid, isLoaded; public string name; public bool IsValid() { return valid; } }
    public enum LoadSceneMode { Single }
    public static class SceneManager
    {
        public static event Action<Scene, LoadSceneMode> sceneLoaded;
        public static void Load() { if (sceneLoaded != null) sceneLoaded(new Scene(), LoadSceneMode.Single); }
    }
}
namespace UnityEngine.Rendering
{
    public struct ScriptableRenderContext {}
    public static class RenderPipelineManager
    {
        public static event Action<ScriptableRenderContext, UnityEngine.Camera> beginCameraRendering;
        public static void Render() { if (beginCameraRendering != null) beginCameraRendering(new ScriptableRenderContext(), new UnityEngine.Camera()); }
    }
}
