using System;

namespace UnityEngine
{
    public class Object { }
    public class MonoBehaviour : Object { public bool isActiveAndEnabled = true; }
    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute { public MinAttribute(float value) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string fileName; public string menuName; }
    public static class Time { public static float unscaledDeltaTime; public static float deltaTime; }
    public static class Debug
    {
        public static void LogWarning(string message, Object context) { }
        public static void LogError(string message, Object context) { throw new InvalidOperationException(message); }
    }
}
namespace UnityEngine.Events
{
    public class UnityEvent { public void Invoke() { } }
    public class UnityEvent<T> { public void Invoke(T value) { } }
}
namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class MenuItem : Attribute { public MenuItem(string path) { } }
    public static class Selection { public static UnityEngine.Object activeObject; }
    public static class EditorUtility { public static void SetDirty(UnityEngine.Object value) { } }
    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object => null;
        public static bool IsValidFolder(string path) => true;
        public static void CreateFolder(string parent, string name) { }
        public static void CreateAsset(UnityEngine.Object value, string path) { }
        public static void SaveAssets() { }
    }
}
