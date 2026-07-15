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

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public static T FindFirstObjectByType<T>() where T : class => null;
        public static T[] FindObjectsByType<T>(FindObjectsInactive inactive, FindObjectsSortMode sortMode)
            => Array.Empty<T>();
        public static void DestroyImmediate(Object value) { }
        public static void DontDestroyOnLoad(Object value) { }
    }

    public class GameObject : Object
    {
        public GameObject(string objectName) { name = objectName; }
        public T AddComponent<T>() where T : new() => new T();
    }

    public class MonoBehaviour : Object
    {
        public bool isActiveAndEnabled { get; set; }
        public GameObject gameObject { get; set; }
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
        public bool SuppressLivePublishersForReplay { get; set; }
        public ulong NowNs { get; set; }
    }
}
