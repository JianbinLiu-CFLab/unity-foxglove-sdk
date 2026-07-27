// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Prevent Play Mode after switching native R2FU runtime packages in the same Editor process.

#if UNITY_EDITOR
using System;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
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
            EditorApplication.hierarchyChanged += InvalidateNativeDemandCache;
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
        private const string Ros2Namespace = "ROS2";
        private const string Ros2UnityComponentSuffix = "Unity" + "Component";
        private const string Ros2ForUnitySuffix = "ForUnity";
        private const string Ros2NativeEnabledSerializedProperty =
            "_ros2NativeEnabled";
        private const string FoxRunInboundEnabledSerializedProperty =
            "_enableFoxRunInbound";
        private const string FoxRunEndpointSerializedProperty =
            "_defaultFoxRunSubscriptionSource";
        private const string FoxRunPublishTargetsSerializedProperty =
            "_defaultFoxRunPublishTargets";
        private const string GeneratedFoxRunSchemaInfoTypeName =
            "Unity.FoxgloveSDK.Generated.FoxRunSchemaInfo";
        private const string FoxRunRos2SubscriptionHubTypeName =
            "Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionHub";
        private const string FoxRunRos2CustomPublisherHubTypeName =
            "Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CustomPublisherHub";
        private const double NativeReloadUnlockDelaySeconds = 2.0;
        private const double ZenohRouterProbeCacheSeconds = 2.0;

        private static bool _reloadAssembliesLockedForR2fu;
        private static double _unlockReloadAssembliesAfter;
        private static bool _nativeDemandCacheValid;
        private static bool _cachedNativeDemand;
        private static bool _zenohRouterProcessCacheValid;
        private static bool _cachedZenohRouterProcessRunning;
        private static double _zenohRouterProcessCacheUntil;

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
            // Another earlier Play Mode callback may cancel entry (for example,
            // while refreshing generated FoxRun schema constants). Do not take
            // the native reload lock after Unity has already abandoned entry.
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ScheduleReloadAssembliesUnlock();
                return;
            }

            // Inspector property edits do not necessarily trigger hierarchyChanged.
            // Re-scan at the stable pre-Play boundary before any R2FU Ok() path.
            InvalidateNativeDemandCache();
            var hasNativeDemand = HasR2fuNativeDemand();
            if (!hasNativeDemand
                && !_reloadAssembliesLockedForR2fu
                && !SessionState.GetBool(ReloadAssembliesLockedForR2fuKey, false))
            {
                return;
            }

            var projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            var status = Ros2ForUnityRuntimeSelection.GetStatus(projectDirectory);
            if (hasNativeDemand && (status == null || status.SelectedRuntime == null))
            {
                var statusDiagnostic = status == null || string.IsNullOrWhiteSpace(status.Diagnostic)
                    ? "Select one valid ROS2 For Unity runtime package and resolve packages before entering Play Mode."
                    : status.Diagnostic;
                var diagnostic = "No selected ROS2 For Unity runtime is available for native demand. "
                    + statusDiagnostic;
                Debug.LogError(diagnostic);
                if (!Application.isBatchMode)
                    EditorUtility.DisplayDialog("ROS2 For Unity runtime required", diagnostic, "OK");
                EditorApplication.isPlaying = false;
                return;
            }

            if (hasNativeDemand)
            {
                var customTypesupport = Ros2ForUnityRuntimeSelection.GetActiveCustomTypesupportSelection(projectDirectory);
                if (!customTypesupport.IsReady
                    && customTypesupport.Code != Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly)
                {
                    var diagnostic = "FoxRun custom ROS2 typesupport is not ready for native demand: "
                        + customTypesupport.Code + ". Resolve the selected base runtime/add-on pair before entering Play Mode.";
                    Debug.LogError(diagnostic);
                    if (!Application.isBatchMode)
                        EditorUtility.DisplayDialog("FoxRun custom ROS2 typesupport not ready", diagnostic, "OK");
                    EditorApplication.isPlaying = false;
                    return;
                }
            }

            var runtimePackage = Ros2ForUnityRuntimeSelection.GetRuntimePackageRequiringEditorRestart(status);
            var communicationMode = Ros2ForUnityRuntimeSelection.GetCommunicationModeRequiringEditorRestart(status);
            var zenohRouterEndpoint = Ros2ForUnityRuntimeSelection.GetZenohRouterEndpointRequiringEditorRestart(status);
            var customTypesupportPackage = Ros2ForUnityRuntimeSelection.GetCustomTypesupportRequiringEditorRestart(status);
            if (string.IsNullOrWhiteSpace(runtimePackage)
                && string.IsNullOrWhiteSpace(communicationMode)
                && string.IsNullOrWhiteSpace(zenohRouterEndpoint)
                && string.IsNullOrWhiteSpace(customTypesupportPackage))
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
            else if (!string.IsNullOrWhiteSpace(communicationMode))
            {
                Debug.LogError(
                    "Unity2Foxglove ROS2 For Unity communication mode was switched to "
                    + communicationMode
                    + " in this Unity Editor process. Restart Unity before entering Play Mode so stale native ROS2 RMW DLLs are unloaded.");
            }
            else if (!string.IsNullOrWhiteSpace(zenohRouterEndpoint))
            {
                Debug.LogError(
                    "Unity2Foxglove ROS2 For Unity Zenoh router endpoint was switched to "
                    + zenohRouterEndpoint
                    + " in this Unity Editor process. Restart Unity before entering Play Mode so the native Zenoh session uses one frozen endpoint.");
            }
            else
            {
                Debug.LogError(
                    "Unity2Foxglove FoxRun custom ROS2 typesupport was switched to "
                    + customTypesupportPackage
                    + " in this Unity Editor process. Restart Unity before entering Play Mode so stale custom native typesupport DLLs are unloaded.");
            }

            EditorApplication.isPlaying = false;
        }

        private static bool TryGetMissingZenohRouterDiagnostic(
            Ros2ForUnityRuntimeSelectionStatus status,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if (status == null || status.SelectedRuntime == null)
                return false;
            if (!HasR2fuNativeDemand())
                return false;

            var communicationMode = Ros2ForUnityRuntimeSelection.GetCommunicationModeForRuntime(status.SelectedRuntime);
            var rmwImplementation = Ros2ForUnityRuntimeSelection.GetRmwImplementationForCommunicationMode(
                status.SelectedRuntime,
                communicationMode);
            if (!string.Equals(
                    rmwImplementation,
                    Ros2ForUnityRuntimeSelection.ZenohRmwImplementation,
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
            var now = EditorApplication.timeSinceStartup;
            if (_zenohRouterProcessCacheValid && now < _zenohRouterProcessCacheUntil)
                return _cachedZenohRouterProcessRunning;

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception)
            {
                CacheZenohRouterProcessResult(false, now);
                return false;
            }

            var running = false;
            foreach (var process in processes)
            {
                using (process)
                {
                    if (IsZenohRouterProcess(process))
                    {
                        running = true;
                        break;
                    }
                }
            }

            CacheZenohRouterProcessResult(running, now);
            return running;
        }

        private static void CacheZenohRouterProcessResult(bool running, double now)
        {
            _cachedZenohRouterProcessRunning = running;
            _zenohRouterProcessCacheValid = true;
            _zenohRouterProcessCacheUntil = now + ZenohRouterProbeCacheSeconds;
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

        private static bool HasR2fuNativeDemand()
        {
            if (_nativeDemandCacheValid)
                return _cachedNativeDemand;

            var hasDemand = false;
            var hasExplicitNativeContract = HasGeneratedExplicitNativeSubscriptionContract();
            var hasExplicitNativePublishContract =
                HasGeneratedExplicitNativePublishContract();
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
                var subscriptionsEnabled = serialized.FindProperty(FoxRunInboundEnabledSerializedProperty);
                var defaultProvider = serialized.FindProperty(FoxRunEndpointSerializedProperty);
                var defaultPublishTargets = serialized.FindProperty(
                    FoxRunPublishTargetsSerializedProperty);
                if (ros2NativeEnabled != null
                    && ros2NativeEnabled.propertyType == SerializedPropertyType.Boolean
                    && subscriptionsEnabled != null
                    && subscriptionsEnabled.propertyType == SerializedPropertyType.Boolean
                    && defaultProvider != null
                    && defaultProvider.propertyType == SerializedPropertyType.Enum
                    && defaultPublishTargets != null
                    && defaultPublishTargets.propertyType == SerializedPropertyType.Enum)
                {
                    var provider = defaultProvider.intValue == (int)FoxRunEndpoint.Ros2Native
                        ? FoxRunEndpoint.Ros2Native
                        : FoxRunEndpoint.Foxglove;
                    if (FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                            nativeOutputEnabled: ros2NativeEnabled.boolValue,
                            defaultPublishTargets:
                                (FoxRunEndpoint)defaultPublishTargets.intValue,
                            hasExplicitNativePublishContract:
                                hasExplicitNativePublishContract,
                            subscriptionsEnabled: subscriptionsEnabled.boolValue,
                            defaultSubscriptionSource: provider,
                            hasExplicitNativeContract: hasExplicitNativeContract))
                    {
                        hasDemand = true;
                        break;
                    }
                    continue;
                }

                try
                {
                    var output = ReadBoolProperty(type, behaviour, "Ros2NativeEnabled");
                    var subscriptions = ReadBoolProperty(type, behaviour, "EnableFoxRunInbound");
                    var provider = ReadSourceProperty(
                        type,
                        behaviour,
                        "DefaultFoxRunSubscriptionSource");
                    var publishTargets = ReadEndpointProperty(
                        type,
                        behaviour,
                        "DefaultFoxRunPublishTargets",
                        FoxRunEndpoint.Foxglove);
                    if (FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                            nativeOutputEnabled: output,
                            defaultPublishTargets: publishTargets,
                            hasExplicitNativePublishContract:
                                hasExplicitNativePublishContract,
                            subscriptionsEnabled: subscriptions,
                            defaultSubscriptionSource: provider,
                            hasExplicitNativeContract: hasExplicitNativeContract))
                    {
                        hasDemand = true;
                        break;
                    }
                }
                catch (Exception)
                {
                    // Ignore editor reflection races while Unity is entering Play Mode.
                }
            }

            _cachedNativeDemand = hasDemand;
            _nativeDemandCacheValid = true;
            return hasDemand;
        }

        private static bool HasGeneratedExplicitNativeSubscriptionContract()
        {
            var generatedType = FindLoadedType(GeneratedFoxRunSchemaInfoTypeName);
            var bindingsField = generatedType?.GetField(
                "SubscriptionBindings",
                BindingFlags.Public | BindingFlags.Static);
            if (!(bindingsField?.GetValue(null) is System.Collections.IEnumerable bindings))
                return false;

            foreach (var item in bindings)
            {
                var provider = item?.GetType().GetProperty(
                    "DeclaredSource",
                    BindingFlags.Instance | BindingFlags.Public)?.GetValue(item, null);
                if (provider is FoxRunEndpoint typed
                    && typed == FoxRunEndpoint.Ros2Native)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasGeneratedExplicitNativePublishContract()
        {
            var generatedType = FindLoadedType(GeneratedFoxRunSchemaInfoTypeName);
            var contractsField = generatedType?.GetField(
                "CustomNativeContracts",
                BindingFlags.Public | BindingFlags.Static);
            if (!(contractsField?.GetValue(null) is System.Collections.IEnumerable contracts))
                return false;

            foreach (var item in contracts)
            {
                if (item == null)
                    continue;
                var itemType = item.GetType();
                var flow = itemType.GetProperty(
                    "Flow",
                    BindingFlags.Instance | BindingFlags.Public)?.GetValue(item, null) as string;
                var targets = itemType.GetProperty(
                    "DeclaredTargets",
                    BindingFlags.Instance | BindingFlags.Public)?.GetValue(item, null);
                var publishes = string.Equals(flow, "Publish", StringComparison.Ordinal)
                                || string.Equals(
                                    flow,
                                    "PublishAndSubscribe",
                                    StringComparison.Ordinal);
                if (publishes
                    && targets is FoxRunEndpoint typed
                    && typed != 0
                    && (typed & FoxRunEndpoint.Ros2Native) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReadBoolProperty(Type type, object instance, string propertyName)
        {
            var property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property != null
                   && property.PropertyType == typeof(bool)
                   && (bool)property.GetValue(instance, null);
        }

        private static FoxRunEndpoint ReadSourceProperty(
            Type type,
            object instance,
            string propertyName)
            => ReadEndpointProperty(
                type,
                instance,
                propertyName,
                FoxRunEndpoint.Foxglove) == FoxRunEndpoint.Ros2Native
                ? FoxRunEndpoint.Ros2Native
                : FoxRunEndpoint.Foxglove;

        private static FoxRunEndpoint ReadEndpointProperty(
            Type type,
            object instance,
            string propertyName,
            FoxRunEndpoint fallback)
        {
            var property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property != null
                   && property.PropertyType == typeof(FoxRunEndpoint)
                   && property.GetValue(instance, null) is FoxRunEndpoint endpoint
                ? endpoint
                : fallback;
        }

        private static void InvalidateNativeDemandCache()
        {
            _nativeDemandCacheValid = false;
        }

        private static void OnCompilationStarted(object context)
        {
            InvalidateNativeDemandCache();
            _zenohRouterProcessCacheValid = false;
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
            InvalidateNativeDemandCache();
            _zenohRouterProcessCacheValid = false;
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
            if (!HasR2fuNativeDemand()
                && !_reloadAssembliesLockedForR2fu
                && !SessionState.GetBool(ReloadAssembliesLockedForR2fuKey, false))
            {
                return false;
            }

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
            if (!_reloadAssembliesLockedForR2fu && !HasR2fuNativeDemand())
                return;

            if (_reloadAssembliesLockedForR2fu)
                return;

            if (SessionState.GetBool(ReloadAssembliesLockedForR2fuKey, false))
            {
                // A forced recompile can reset this static flag while Unity's
                // editor-level reload lock remains active. Do not take a second lock.
                _reloadAssembliesLockedForR2fu = true;
                return;
            }

            EditorApplication.LockReloadAssemblies();
            _reloadAssembliesLockedForR2fu = true;
            SessionState.SetBool(ReloadAssembliesLockedForR2fuKey, true);

            Debug.Log(
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

            // Remove FoxRun endpoints while their shared R2FU node is still
            // alive. Waiting until the next MonoBehaviour Update would let
            // ShutdownShared dispose the node first, turning an otherwise
            // clean Play Mode exit into a false teardown failure.
            TryInvokeStatic(
                FoxRunRos2SubscriptionHubTypeName,
                "StopForNativeRuntimeShutdown");
            TryInvokeStatic(
                FoxRunRos2CustomPublisherHubTypeName,
                "StopForNativeRuntimeShutdown");
            var stoppedExecutors = TryInvokeStatic(Ros2Namespace + ".ROS2" + Ros2UnityComponentSuffix, "StopAllExecutorsForRosShutdown");
            var shutdownShared = TryInvokeStatic(Ros2Namespace + ".ROS2" + Ros2ForUnitySuffix, "ShutdownShared");
            if (!stoppedExecutors && !shutdownShared)
                return;

            Debug.Log(
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
