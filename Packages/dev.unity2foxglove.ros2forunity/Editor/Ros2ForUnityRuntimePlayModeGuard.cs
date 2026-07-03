// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Prevent Play Mode after switching native R2FU runtime packages in the same Editor process.

#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Process = System.Diagnostics.Process;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    [InitializeOnLoad]
    internal static class Ros2ForUnityRuntimePlayModeGuard
    {
        static Ros2ForUnityRuntimePlayModeGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private const string CompilationStartedWhileR2fuPlayModeKey =
            "Unity2Foxglove.R2FU.CompilationStartedWhilePlayMode";
        private const string ReloadAssembliesLockedForR2fuKey =
            "Unity2Foxglove.R2FU.ReloadAssembliesLockedForPlayMode";
        private const string FoxgloveManagerTypeName =
            "Unity.FoxgloveSDK.Components.FoxgloveManager";
        private const string Ros2NativeEnabledSerializedProperty =
            "_ros2NativeEnabled";
        private const double NativeReloadUnlockDelaySeconds = 2.0;

        private static bool _reloadAssembliesLockedForR2fu;
        private static double _unlockReloadAssembliesAfter;

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                OnExitingEditMode();
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                LockReloadAssembliesForNativePlayMode("entered Play Mode");
                return;
            }

            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                RequestNativeRuntimeShutdownBeforeReload("Play Mode exit");
                ScheduleReloadAssembliesUnlock();
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
                ScheduleReloadAssembliesUnlock();
        }

        private static void OnExitingEditMode()
        {
            var projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            var status = Ros2ForUnityRuntimeSelection.GetStatus(projectDirectory);
            var runtimePackage = Ros2ForUnityRuntimeSelection.GetRuntimePackageRequiringEditorRestart(status);
            var communicationMode = Ros2ForUnityRuntimeSelection.GetCommunicationModeRequiringEditorRestart(status);
            if (string.IsNullOrWhiteSpace(runtimePackage) && string.IsNullOrWhiteSpace(communicationMode))
            {
                if (TryGetMissingZenohRouterDiagnostic(status, out var zenohRouterDiagnostic))
                {
                    Debug.LogError(zenohRouterDiagnostic);
                    if (!Application.isBatchMode)
                    {
                        EditorUtility.DisplayDialog(
                            "ROS2 For Unity Zenoh router required",
                            zenohRouterDiagnostic,
                            "OK");
                    }

                    EditorApplication.isPlaying = false;
                    return;
                }

                Ros2ForUnityRuntimeSelection.BindActiveRuntimeForPlayMode(status);
                LockReloadAssembliesForNativePlayMode("entering Play Mode");
                return;
            }

            if (!string.IsNullOrWhiteSpace(runtimePackage))
            {
                Debug.LogError(
                    "Unity2Foxglove ROS2 For Unity runtime was switched to "
                    + runtimePackage
                    + " in this Unity Editor process. Restart Unity before entering Play Mode so stale native ROS2 runtime DLLs are unloaded.");
            }
            else
            {
                Debug.LogError(
                    "Unity2Foxglove ROS2 For Unity communication mode was switched to "
                    + communicationMode
                    + " in this Unity Editor process. Restart Unity before entering Play Mode so stale native ROS2 RMW DLLs are unloaded.");
            }

            EditorApplication.isPlaying = false;
        }

        private static bool TryGetMissingZenohRouterDiagnostic(
            Ros2ForUnityRuntimeSelectionStatus status,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if (status == null || status.SelectedRuntime == null || !status.SelectedRuntime.SupportsZenoh)
                return false;
            if (!HasR2fuNativeOutputDemand())
                return false;

            var communicationMode = Ros2ForUnityRuntimeSelection.GetCommunicationModeForRuntime(status.SelectedRuntime);
            if (!string.Equals(
                    communicationMode,
                    Ros2ForUnityRuntimeSelection.ZenohCommunicationMode,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (IsZenohRouterProcessRunning())
                return false;

            diagnostic =
                "Unity2Foxglove ROS2 For Unity is configured for Zenoh (rmw_zenoh_cpp), but no local Zenoh router process was detected. "
                + "Start rmw_zenohd before entering Play Mode, or switch the ROS2 For Unity Communication Mode back to FastDDS. "
                + "The Phase162 smoke helper only keeps its auto-started router alive while that helper is running; RViz left open after the helper exits no longer has a router.";
            return true;
        }

        private static bool IsZenohRouterProcessRunning()
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception)
            {
                return false;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    if (IsZenohRouterProcess(process))
                        return true;
                }
            }

            return false;
        }

        private static bool IsZenohRouterProcess(Process process)
        {
            try
            {
                if (LooksLikeZenohRouterProcess(process.ProcessName))
                    return true;
            }
            catch (Exception)
            {
                // Process metadata can disappear while Unity is enumerating processes.
            }

            try
            {
                if (LooksLikeZenohRouterProcess(process.MainModule?.FileName))
                    return true;
            }
            catch (Exception)
            {
                // Access to other-user/system process modules can be denied.
            }

            return false;
        }

        private static bool LooksLikeZenohRouterProcess(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && (value.IndexOf("rmw_zenohd", StringComparison.OrdinalIgnoreCase) >= 0
                       || value.IndexOf("zenohd", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasR2fuNativeOutputDemand()
        {
            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (!string.Equals(type.FullName, FoxgloveManagerTypeName, StringComparison.Ordinal))
                    continue;

                var gameObject = behaviour.gameObject;
                if (gameObject == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
                    continue;

                var serialized = new SerializedObject(behaviour);
                var ros2NativeEnabled = serialized.FindProperty(Ros2NativeEnabledSerializedProperty);
                if (ros2NativeEnabled != null && ros2NativeEnabled.propertyType == SerializedPropertyType.Boolean)
                {
                    if (ros2NativeEnabled.boolValue)
                        return true;
                    continue;
                }

                var property = type.GetProperty(
                    "Ros2NativeEnabled",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null || property.PropertyType != typeof(bool))
                    continue;

                try
                {
                    if ((bool)property.GetValue(behaviour, null))
                        return true;
                }
                catch (Exception)
                {
                    // Ignore editor reflection races while Unity is entering Play Mode.
                }
            }

            return false;
        }

        private static void OnCompilationStarted(object context)
        {
            Ros2ForUnityRuntimeSelection.InvalidateStatusCache();
            if (StopPlayModeBeforeNativeReload("script compilation"))
            {
                SessionState.SetBool(CompilationStartedWhileR2fuPlayModeKey, true);
                LockReloadAssembliesForNativePlayMode("script compilation");
                RequestNativeRuntimeShutdownBeforeReload("script compilation");
            }
        }

        private static void OnCompilationFinished(object context)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                SessionState.SetBool(CompilationStartedWhileR2fuPlayModeKey, false);
        }

        private static void OnBeforeAssemblyReload()
        {
            var compilationStartedWhilePlaying = SessionState.GetBool(CompilationStartedWhileR2fuPlayModeKey, false);
            if (!compilationStartedWhilePlaying && !EditorApplication.isPlaying)
                return;

            SessionState.SetBool(CompilationStartedWhileR2fuPlayModeKey, false);
            RequestNativeRuntimeShutdownBeforeReload(
                compilationStartedWhilePlaying
                    ? "script compilation assembly reload"
                    : "assembly reload");
            StopPlayModeBeforeNativeReload(
                compilationStartedWhilePlaying
                    ? "script compilation assembly reload"
                    : "assembly reload");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError(
                    "Unity2Foxglove ROS2 For Unity assembly reload is continuing before Play Mode fully exited. Native ROS2/RMW DLLs may still be loaded; restart Unity before entering Play Mode again.");
            }
        }

        private static bool StopPlayModeBeforeNativeReload(string reason)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                return false;

            var projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            if (!Ros2ForUnityRuntimeSelection.HasManifestRuntimePackage(projectDirectory))
                return false;

            Debug.LogError(
                "Unity2Foxglove ROS2 For Unity is active while Unity is starting "
                + reason
                + ". Exit Play Mode before changing scripts or packages; native ROS2/RMW DLLs cannot be safely unloaded during Play Mode.");

            EditorApplication.isPlaying = false;
            return true;
        }

        private static void LockReloadAssembliesForNativePlayMode(string reason)
        {
            var projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            if (!Ros2ForUnityRuntimeSelection.HasManifestRuntimePackage(projectDirectory))
                return;
            if (!_reloadAssembliesLockedForR2fu && !HasR2fuNativeOutputDemand())
                return;

            if (_reloadAssembliesLockedForR2fu)
                return;

            EditorApplication.LockReloadAssemblies();
            _reloadAssembliesLockedForR2fu = true;
            SessionState.SetBool(ReloadAssembliesLockedForR2fuKey, true);

            Debug.LogWarning(
                "Unity2Foxglove ROS2 For Unity locked editor assembly reloads while native ROS2/RMW DLLs are active ("
                + reason
                + "). Exit Play Mode before changing scripts; pending script reloads will resume after native shutdown.");
        }

        private static void ScheduleReloadAssembliesUnlock()
        {
            if (!_reloadAssembliesLockedForR2fu
                && !SessionState.GetBool(ReloadAssembliesLockedForR2fuKey, false))
            {
                return;
            }

            _unlockReloadAssembliesAfter = EditorApplication.timeSinceStartup + NativeReloadUnlockDelaySeconds;
            EditorApplication.update -= OnEditorUpdateUntilReloadUnlock;
            EditorApplication.update += OnEditorUpdateUntilReloadUnlock;
        }

        private static void OnEditorUpdateUntilReloadUnlock()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (EditorApplication.isUpdating)
                return;
            if (EditorApplication.timeSinceStartup < _unlockReloadAssembliesAfter)
                return;

            EditorApplication.update -= OnEditorUpdateUntilReloadUnlock;
            try
            {
                EditorApplication.UnlockReloadAssemblies();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "Unity2Foxglove ROS2 For Unity failed to unlock editor assembly reloads after Play Mode exit: "
                    + ex.GetType().Name
                    + ": "
                    + ex.Message);
            }

            _reloadAssembliesLockedForR2fu = false;
            SessionState.SetBool(ReloadAssembliesLockedForR2fuKey, false);
        }

        private static void RequestNativeRuntimeShutdownBeforeReload(string reason)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode
                && !SessionState.GetBool(ReloadAssembliesLockedForR2fuKey, false))
            {
                return;
            }

            var stoppedExecutors = TryInvokeStatic("ROS2.ROS2UnityComponent", "StopAllExecutorsForRosShutdown");
            var shutdownShared = TryInvokeStatic("ROS2.ROS2ForUnity", "ShutdownShared");
            if (!stoppedExecutors && !shutdownShared)
                return;

            Debug.LogWarning(
                "Unity2Foxglove ROS2 For Unity requested native ROS2 shutdown before "
                + reason
                + " to avoid unloading ROS2/RMW DLLs while executor threads are active.");
        }

        private static bool TryInvokeStatic(string typeName, string methodName)
        {
            var type = FindLoadedType(typeName);
            var method = type?.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                return false;

            try
            {
                method.Invoke(null, null);
                return true;
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                Debug.LogWarning(
                    "Unity2Foxglove ROS2 For Unity native shutdown hook failed: "
                    + typeName
                    + "."
                    + methodName
                    + " threw "
                    + inner.GetType().Name
                    + ": "
                    + inner.Message);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "Unity2Foxglove ROS2 For Unity native shutdown hook failed: "
                    + typeName
                    + "."
                    + methodName
                    + " threw "
                    + ex.GetType().Name
                    + ": "
                    + ex.Message);
            }

            return false;
        }

        private static Type FindLoadedType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(typeName, throwOnError: false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
#endif
