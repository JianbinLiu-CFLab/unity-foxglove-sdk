namespace UnityEngine
{
    public class Object
    {
        protected T GetComponent<T>() where T : class => null;
        public static T FindObjectOfType<T>() where T : class => null;
    }

    public class MonoBehaviour : Object { }
    public sealed class DisallowMultipleComponent : System.Attribute { }
    public sealed class HeaderAttribute : System.Attribute { public HeaderAttribute(string value) { } }
    public sealed class TooltipAttribute : System.Attribute { public TooltipAttribute(string value) { } }
    public sealed class SerializeField : System.Attribute { }
    public sealed class MinAttribute : System.Attribute { public MinAttribute(float value) { } }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
    public static class Debug
    {
        public static void Log(string value) { }
        public static void LogWarning(string value) { }
    }
    public static class Application
    {
        public static bool isPlaying => true;
        public static event System.Action quitting
        {
            add { }
            remove { }
        }
    }
}

namespace AOT
{
    public sealed class MonoPInvokeCallbackAttribute : System.Attribute
    {
        public MonoPInvokeCallbackAttribute(System.Type type) { }
    }
}

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxgloveManager : UnityEngine.MonoBehaviour
    {
        public bool IsRunning { get; set; }
        public void SetMirrorSink(Unity.FoxgloveSDK.Core.IFoxgloveMirrorSink sink) { }
    }
}
