// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/AdapterCompileStubs
// Purpose: Compile-only Unity host surface for the optional facade and focused Native lanes.

using System;

namespace UnityEngine
{
    [Flags]
    public enum HideFlags
    {
        None = 0,
        HideAndDontSave = 1
    }

    public enum FindObjectsInactive { Exclude, Include }
    public enum FindObjectsSortMode { None }
    public enum RuntimeInitializeLoadType { SubsystemRegistration, AfterSceneLoad }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AddComponentMenuAttribute : Attribute
    {
        public AddComponentMenuAttribute(string menuName) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrderAttribute : Attribute
    {
        public DefaultExecutionOrderAttribute(int order) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public class Object
    {
        private static int s_nextId;
        private readonly int _instanceId = ++s_nextId;
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public int GetInstanceID() => _instanceId;
        public static T FindFirstObjectByType<T>() where T : class => null;
        public static T[] FindObjectsByType<T>(FindObjectsInactive inactive, FindObjectsSortMode sortMode)
            => Array.Empty<T>();
        public static void DestroyImmediate(Object value) { }
        public static void Destroy(Object value) { }
        public static void DontDestroyOnLoad(Object value) { }
    }

    public class GameObject : Object
    {
        public GameObject(string objectName) { name = objectName; }
        public UnityEngine.SceneManagement.Scene scene { get; set; }
        public T AddComponent<T>() where T : new() => new T();
    }

    public class MonoBehaviour : Object
    {
        public bool isActiveAndEnabled { get; set; }
        public GameObject gameObject { get; set; }
        public T GetComponent<T>() where T : class => null;
    }

    public static class Application
    {
        public static bool isPlaying { get; set; }
        public static event Action quitting;
    }

    public static class Time
    {
        public static float deltaTime { get; set; }
        public static float realtimeSinceStartup { get; set; }
        public static double realtimeSinceStartupAsDouble { get; set; }
    }

    public static class Debug
    {
        public static void LogWarning(object message) { }
    }
}

namespace Unity.Profiling
{
    public readonly struct ProfilerMarker
    {
        public ProfilerMarker(string name) { }
        public AutoScope Auto() => new AutoScope();

        public readonly struct AutoScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }

    public struct Scene
    {
        public int handle { get; set; }
        public string path { get; set; }
        public string name { get; set; }
        public bool isLoaded { get; set; }
    }

    public static class SceneManager
    {
        public static int sceneCount { get; set; }
        public static event Action<Scene, LoadSceneMode> sceneLoaded;
        public static event Action<Scene, Scene> activeSceneChanged;
        public static Scene GetActiveScene() => default;
        public static Scene GetSceneAt(int index) => default;
    }
}

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxgloveManager
    {
        public bool IsRunning { get; set; }
        public bool Ros2NativeEnabled { get; set; }
        public bool SuppressLivePublishersForReplay { get; set; }
        public ulong NowNs { get; set; }
        public FoxRunPublishSessionPolicy ActiveFoxRunPublishSessionPolicy { get; set; }
        public float ActiveFoxRunDefaultPublishRateHz { get; set; } = 10f;
        public FoxRunEndpoint ActiveFoxRunPublishTargets { get; set; } = FoxRunEndpoint.Foxglove;
        public FoxRunEncoding ActiveFoxRunPublishEncoding { get; set; } = FoxRunEncoding.Protobuf;
        public FoxRunEndpoint ActiveFoxRunSubscriptionSource { get; set; } = FoxRunEndpoint.Foxglove;
        public FoxRunEncoding ActiveFoxRunSubscriptionEncoding { get; set; } = FoxRunEncoding.Protobuf;
        public FoxRunResolvedQos DefaultFoxRunNativePublishQos { get; set; } =
            FoxRunResolvedQos.Default;
        public FoxRunResolvedQos ActiveFoxRunNativePublishQos =>
            ActiveFoxRunPublishSessionPolicy != null
            && ActiveFoxRunPublishSessionPolicy.SessionActive
                ? ActiveFoxRunPublishSessionPolicy.NativeRos2Qos
                : DefaultFoxRunNativePublishQos;
        public FoxRunResolvedQos ActiveFoxRunBridgePublishQos { get; set; } =
            FoxRunResolvedQos.Default;
        public FoxRunSubscriptionSessionPolicy ActiveFoxRunSubscriptionSessionPolicy { get; set; }
        public event Action<FoxRunPublishSessionPolicy> FoxRunPublishSessionChanged;
        public event Action<FoxRunSubscriptionSessionPolicy> FoxRunSubscriptionSessionChanged;

        public FoxRunEncoding ResolveFoxRunEncoding(
            FoxRunEncoding declaredEncoding,
            FoxRunFlow flow)
            => declaredEncoding == 0 ? ActiveFoxRunPublishEncoding : declaredEncoding;

        public void PublishJson<T>(
            string topic,
            string schemaName,
            T payload,
            ulong logTimeNs) { }

        public void PublishProto<T>(
            string topic,
            string schemaName,
            T payload,
            ulong logTimeNs) { }

        public void PublishFoxRunJsonBytes(
            string topic,
            string schemaName,
            byte[] payload,
            ulong logTimeNs) { }

        public bool TryPrepareFoxRunRos2BridgePublish(
            string topic,
            string schemaName,
            FoxRunResolvedQos qos,
            out string effectiveTopic,
            out string reason)
        {
            effectiveTopic = topic ?? string.Empty;
            reason = string.Empty;
            return false;
        }

        public bool TryPublishFoxRunRos2BridgeCdr(
            string topic,
            string schemaName,
            byte[] payload,
            ulong logTimeNs,
            FoxRunResolvedQos qos,
            out string reason)
        {
            reason = string.Empty;
            return false;
        }

        public bool TryPrepareFoxRunRos2Recording(
            string topic,
            string schemaName,
            string schemaContent,
            out uint channelId,
            out string reason)
        {
            channelId = 0;
            reason = string.Empty;
            return false;
        }

        public bool TryPublishFoxRunRos2Recording(
            string topic,
            string schemaName,
            string schemaContent,
            byte[] payload,
            ulong logTimeNs,
            out string reason)
        {
            reason = string.Empty;
            return false;
        }

        public void RaiseFoxRunPublishSessionChanged()
            => FoxRunPublishSessionChanged?.Invoke(ActiveFoxRunPublishSessionPolicy);
    }
}
