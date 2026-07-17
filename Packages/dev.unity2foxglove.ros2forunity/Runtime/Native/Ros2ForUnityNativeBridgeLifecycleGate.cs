// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Shared lifecycle gate for R2FU native bridge startup and shutdown windows.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
#endif

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal static class Ros2ForUnityNativeBridgeLifecycleGate
    {
        // Keep early Editor Play Mode out of native bootstrap so scene restore,
        // domain reload, and backup-scene cleanup cannot race ROS2 initialization.
        private const double EditorPlayModeStableDelaySeconds = 3.0;

        private static volatile bool _applicationQuitting;
        private static volatile bool _nativeReloadWindow;
        private static volatile bool _isStablePlayModeScene;
        private static volatile int _lastRefreshedActiveSceneHandle = int.MinValue;
        private static volatile int _lastRefreshedSceneCount = -1;
        private static volatile int _unsafeSceneHandleCount;
        private static volatile bool _sceneStateDirty = true;
        private static int[] _unsafeSceneHandles = Array.Empty<int>();

#if UNITY_EDITOR
        private static volatile bool _editorEnteredPlayMode;
        private static volatile bool _editorQuitting;
        private static volatile bool _editorAssemblyReloading;
        private static volatile bool _editorCompiling;
        private static volatile bool _editorPlayModeTransition;
        private static double _editorEnteredPlayModeAt;
#endif

        internal static bool IsStablePlayModeScene => _isStablePlayModeScene;

        internal static bool CanBootstrapBridge
        {
            get
            {
                RefreshSceneStateIfNeeded();
                return !_nativeReloadWindow
                       && _isStablePlayModeScene
                       && !_applicationQuitting
                       && !IsHardEditorShutdownWindow
                       && IsActiveSceneCacheCurrent;
            }
        }

        internal static bool CanInitializeNativeRuntimeForBridge(Scene ownerScene)
        {
            RefreshSceneStateIfNeeded();
            return !_nativeReloadWindow
                   && _isStablePlayModeScene
                   && IsActiveSceneCacheCurrent
                   && !IsBridgeSceneUnsafe(ownerScene);
        }

        internal static bool IsShuttingDownForBridge(Scene ownerScene)
            => _nativeReloadWindow
               || !_isStablePlayModeScene
               || !IsActiveSceneCacheCurrent || IsBridgeSceneUnsafe(ownerScene);

        internal static bool IsBridgeSceneUnsafe(Scene scene)
        {
            var handle = scene.handle;
            var unsafeHandles = _unsafeSceneHandles;
            var unsafeCount = _unsafeSceneHandleCount;
            for (var i = 0; i < unsafeCount; i++)
            {
                if (unsafeHandles[i] == handle)
                    return true;
            }

            return false;
        }

        // Scene lifecycle callbacks can lag one Update behind Unity backup-scene
        // swaps. Keep bridge entry fail-closed if the cached active scene changed.
        private static bool IsActiveSceneCacheCurrent
            => SceneManager.GetActiveScene().handle == _lastRefreshedActiveSceneHandle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForSubsystemRegistration()
        {
            ResetState();
            RegisterRuntimeEvents();
            RefreshSceneState();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RefreshAfterSceneLoad()
        {
            RegisterRuntimeEvents();
            RefreshSceneState();
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InitializeEditorLifecycleGate()
        {
            RegisterRuntimeEvents();
            RegisterEditorEvents();
            ResetEditorState();
            RefreshSceneState();
        }
#endif

        private static void RegisterRuntimeEvents()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshSceneState();
        }

        private static void OnActiveSceneChanged(Scene previous, Scene current)
        {
            RefreshSceneState();
        }

        private static void OnApplicationQuitting()
        {
            _applicationQuitting = true;
            RefreshNativeReloadWindow();
        }

        private static void ResetState()
        {
            _applicationQuitting = false;
            _nativeReloadWindow = false;
            _isStablePlayModeScene = false;
            _lastRefreshedActiveSceneHandle = int.MinValue;
            _lastRefreshedSceneCount = -1;
            _unsafeSceneHandleCount = 0;
            _sceneStateDirty = true;
            _unsafeSceneHandles = Array.Empty<int>();
#if UNITY_EDITOR
            ResetEditorState();
#endif
        }

#if UNITY_EDITOR
        private static void RegisterEditorEvents()
        {
            EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnEditorPlayModeStateChanged;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            EditorApplication.hierarchyChanged -= OnEditorHierarchyChanged;
            EditorApplication.hierarchyChanged += OnEditorHierarchyChanged;
            EditorSceneManager.sceneOpened -= OnEditorSceneOpened;
            EditorSceneManager.sceneOpened += OnEditorSceneOpened;
            EditorSceneManager.sceneClosed -= OnEditorSceneClosed;
            EditorSceneManager.sceneClosed += OnEditorSceneClosed;
        }

        private static void ResetEditorState()
        {
            _editorEnteredPlayMode = false;
            _editorQuitting = false;
            _editorAssemblyReloading = false;
            _editorCompiling = false;
            _editorPlayModeTransition = false;
            _editorEnteredPlayModeAt = 0.0;
            EditorApplication.update -= OnEditorUpdateUntilPlayModeStable;
        }

        private static void OnEditorQuitting()
        {
            _editorQuitting = true;
            RefreshNativeReloadWindow();
        }

        private static void OnBeforeAssemblyReload()
        {
            _editorAssemblyReloading = true;
            RefreshNativeReloadWindow();
        }

        private static void OnCompilationStarted(object context)
        {
            _editorCompiling = true;
            RefreshNativeReloadWindow();
            RefreshSceneState();
        }

        private static void OnCompilationFinished(object context)
        {
            _editorCompiling = false;
            RefreshNativeReloadWindow();
            RefreshSceneState();
        }

        private static void OnEditorHierarchyChanged()
        {
            _isStablePlayModeScene = false;
            _sceneStateDirty = true;
        }

        private static void OnEditorSceneOpened(Scene scene, OpenSceneMode mode)
        {
            _isStablePlayModeScene = false;
            RefreshSceneState();
        }

        private static void OnEditorSceneClosed(Scene scene)
        {
            _isStablePlayModeScene = false;
            RefreshSceneState();
        }

        private static void OnEditorPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    _editorEnteredPlayMode = true;
                    _editorEnteredPlayModeAt = EditorApplication.timeSinceStartup;
                    _editorPlayModeTransition = true;
                    _applicationQuitting = false;
                    _editorAssemblyReloading = false;
                    EditorApplication.update -= OnEditorUpdateUntilPlayModeStable;
                    EditorApplication.update += OnEditorUpdateUntilPlayModeStable;
                    break;

                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    _editorEnteredPlayMode = false;
                    _editorPlayModeTransition = true;
                    _isStablePlayModeScene = false;
                    break;
            }

            RefreshNativeReloadWindow();
            RefreshSceneState();
        }

        private static void OnEditorUpdateUntilPlayModeStable()
        {
            if (!_editorEnteredPlayMode)
            {
                EditorApplication.update -= OnEditorUpdateUntilPlayModeStable;
                return;
            }

            if (EditorApplication.timeSinceStartup - _editorEnteredPlayModeAt < EditorPlayModeStableDelaySeconds)
                return;

            _editorPlayModeTransition = false;
            EditorApplication.update -= OnEditorUpdateUntilPlayModeStable;
            RefreshNativeReloadWindow();
            RefreshSceneState();
        }
#endif

        private static void RefreshNativeReloadWindow()
        {
            _nativeReloadWindow = _applicationQuitting || IsEditorLifecycleWindow;
        }

        private static void RefreshSceneStateIfNeeded()
        {
            var activeScene = SceneManager.GetActiveScene();
            var sceneCount = SceneManager.sceneCount;
            if (_sceneStateDirty
                || activeScene.handle != _lastRefreshedActiveSceneHandle
                || sceneCount != _lastRefreshedSceneCount)
            {
                RefreshSceneState(activeScene, sceneCount);
            }
        }

        private static void RefreshSceneState()
            => RefreshSceneState(SceneManager.GetActiveScene(), SceneManager.sceneCount);

        private static void RefreshSceneState(Scene activeScene, int sceneCount)
        {
            var activeSceneStable = IsStableUserScene(activeScene);
            var unsafeCount = BuildUnsafeSceneHandles(activeScene, sceneCount, out var anyBackupSceneLoaded);

            _lastRefreshedActiveSceneHandle = activeScene.handle;
            _lastRefreshedSceneCount = sceneCount;
            _unsafeSceneHandleCount = unsafeCount;
            _isStablePlayModeScene = Application.isPlaying
                                     && activeSceneStable
                                     && !anyBackupSceneLoaded
                                     && !_applicationQuitting
                                     && !IsHardEditorShutdownWindow;
            _sceneStateDirty = false;
            RefreshNativeReloadWindow();
        }

        private static int BuildUnsafeSceneHandles(Scene activeScene, int sceneCount, out bool anyBackupSceneLoaded)
        {
            var count = 0;
            var handles = EnsureUnsafeSceneHandleCapacity(Math.Max(sceneCount, 1));
            anyBackupSceneLoaded = false;

            for (var i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!IsBackupScene(scene))
                    continue;

                anyBackupSceneLoaded = true;
                handles[count++] = scene.handle;
            }

            if (IsBackupScene(activeScene) && !ContainsHandle(handles, count, activeScene.handle))
            {
                anyBackupSceneLoaded = true;
                if (count == _unsafeSceneHandles.Length)
                    handles = EnsureUnsafeSceneHandleCapacity(count + 1);
                handles[count++] = activeScene.handle;
            }

            return count;
        }

        private static int[] EnsureUnsafeSceneHandleCapacity(int requiredCapacity)
        {
            if (_unsafeSceneHandles.Length < requiredCapacity)
                Array.Resize(ref _unsafeSceneHandles, requiredCapacity);

            return _unsafeSceneHandles;
        }

        private static bool ContainsHandle(int[] handles, int count, int handle)
        {
            for (var i = 0; i < count; i++)
            {
                if (handles[i] == handle)
                    return true;
            }

            return false;
        }

        private static bool IsBackupScene(Scene scene)
        {
            var path = scene.path ?? string.Empty;
            var name = scene.name ?? string.Empty;
            return path.StartsWith("Temp/__Backupscenes/", StringComparison.Ordinal)
                   || path.Contains("__Backupscenes", StringComparison.Ordinal)
                   || name.Contains("__Backupscenes", StringComparison.Ordinal)
                   || name.EndsWith(".backup", StringComparison.Ordinal);
        }

        private static bool IsStableUserScene(Scene scene)
        {
            var path = scene.path ?? string.Empty;
            return scene.isLoaded
                   && (path.StartsWith("Assets/", StringComparison.Ordinal)
                       || path.StartsWith("Packages/", StringComparison.Ordinal));
        }

        private static bool IsHardEditorShutdownWindow
        {
            get
            {
#if UNITY_EDITOR
                return _editorQuitting || _editorAssemblyReloading || _editorCompiling;
#else
                return false;
#endif
            }
        }

        private static bool IsEditorLifecycleWindow
        {
            get
            {
#if UNITY_EDITOR
                return _editorQuitting
                       || _editorAssemblyReloading
                       || _editorCompiling
                       || _editorPlayModeTransition
                       || !_editorEnteredPlayMode;
#else
                return false;
#endif
            }
        }
    }
}
#endif
