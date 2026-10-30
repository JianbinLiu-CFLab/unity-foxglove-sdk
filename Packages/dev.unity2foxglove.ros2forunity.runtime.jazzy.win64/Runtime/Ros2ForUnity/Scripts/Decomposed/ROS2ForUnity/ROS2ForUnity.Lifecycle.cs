using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ROS2
{

internal partial class ROS2ForUnity
{
    internal void DestroyROS2ForUnity()
    {
        UnregisterCallbacks();

        var shouldShutdown = false;
        lock (lifecycleGate)
        {
            if (!ownsLifecycle)
                return;

            ownsLifecycle = false;
            ownerCount = Math.Max(0, ownerCount - 1);
            shouldShutdown = ownerCount == 0 && isInitialized && !shutdownInProgress;
            if (shouldShutdown)
                shutdownInProgress = true;
        }

        if (shouldShutdown)
            CompleteShutdownShared();
    }

    private static void ShutdownShared()
    {
        bool shouldShutdown;
        lock (lifecycleGate)
        {
            if (shutdownInProgress)
                return;

            shouldShutdown = isInitialized;
            if (shouldShutdown)
                shutdownInProgress = true;
            else
                ownerCount = 0;
        }

        if (shouldShutdown)
            CompleteShutdownShared();
    }

    internal static void RetryPendingShutdown()
    {
        bool restoreOnly;
        lock (lifecycleGate)
        {
            if (!shutdownInProgress)
                return;

            restoreOnly = !isInitialized;
        }

        if (restoreOnly)
            FinishShutdownShared();
        else
            CompleteShutdownShared();
    }

    internal static bool IsShutdownCompleteForEditor()
    {
        lock (lifecycleGate)
        {
            return !isInitialized && !shutdownInProgress;
        }
    }

    private static void CompleteShutdownShared()
    {
        if (!ROS2UnityComponent.StopAllExecutorsForRosShutdown())
        {
#if UNITY_EDITOR
            ScheduleShutdownRetry();
#else
            TryCompleteShutdownWithBoundedRetry();
#endif
            return;
        }

        FinishShutdownShared();
    }

#if UNITY_EDITOR
    private static void ScheduleShutdownRetry()
    {
        if (shutdownRetryScheduled || !shutdownInProgress)
            return;

        if (shutdownRetryAttempts >= MaxShutdownRetryAttempts)
        {
            Debug.LogError("ROS2 For Unity shutdown deferred: executor threads remain active after bounded retries.");
            return;
        }

        shutdownRetryAttempts++;
        shutdownRetryScheduled = true;
        EditorApplication.delayCall += RetryShutdownOnMainThread;
    }

    private static void RetryShutdownOnMainThread()
    {
        EditorApplication.delayCall -= RetryShutdownOnMainThread;
        shutdownRetryScheduled = false;
        if (!shutdownInProgress)
            return;

        if (ROS2UnityComponent.StopAllExecutorsForRosShutdown())
        {
            FinishShutdownShared();
            return;
        }

        ScheduleShutdownRetry();
    }
#else
    private static void TryCompleteShutdownWithBoundedRetry()
    {
        for (int attempt = 0; attempt < MaxShutdownRetryAttempts - 1; attempt++)
        {
            if (ROS2UnityComponent.StopAllExecutorsForRosShutdown())
            {
                FinishShutdownShared();
                return;
            }
        }

        Debug.LogError("ROS2 For Unity shutdown deferred: executor threads remain active after bounded retries.");
    }
#endif

    private static void FinishShutdownShared()
    {
        bool retryEnvironmentRestore = false;
        bool retryNativeShutdown = false;
        lock (lifecycleGate)
        {
            if (!isInitialized)
            {
                ownerCount = 0;
                bool environmentRestored = Ros2ForUnityProcessEnvironmentLease.Restore(
                    Environment.OSVersion.Platform == PlatformID.Win32NT);
                if (environmentRestored)
                {
                    shutdownInProgress = false;
                    shutdownRetryAttempts = 0;
                }
                else
                {
                    shutdownInProgress = true;
                    shutdownRetryAttempts = 0;
                    retryEnvironmentRestore = true;
                    Debug.LogError("ROS2 For Unity process environment lease could not be fully restored; shutdown remains pending.");
                }
            }
            else
            {
                LogRuntimeInfoWithoutStackTrace("Shutting down Ros2 For Unity");
                bool nativeShutdownSucceeded = nativeShutdownCompleted;
                if (!nativeShutdownSucceeded)
                {
                    try
                    {
                        SuppressRos2csFinalizer();
                        Ros2cs.Shutdown();
                        nativeShutdownCompleted = true;
                        nativeShutdownSucceeded = true;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        retryNativeShutdown = true;
                    }
                }

                if (nativeShutdownSucceeded)
                {
                    try
                    {
                        Ros2ForUnityNativePluginBootstrap.ResetNativeLibraryRegistration();
                        isInitialized = false;
                        ownerCount = 0;
                        nativeShutdownCompleted = false;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        retryNativeShutdown = true;
                    }
                }

                if (!isInitialized)
                {
                    ownerCount = 0;
                    bool environmentRestored = Ros2ForUnityProcessEnvironmentLease.Restore(
                        Environment.OSVersion.Platform == PlatformID.Win32NT);
                    if (environmentRestored)
                    {
                        shutdownInProgress = false;
                        shutdownRetryAttempts = 0;
                    }
                    else
                    {
                        shutdownInProgress = true;
                        shutdownRetryAttempts = 0;
                        retryEnvironmentRestore = true;
                        Debug.LogError("ROS2 For Unity process environment lease could not be fully restored; shutdown remains pending.");
                    }
                }
                else
                {
                    shutdownInProgress = true;
                    shutdownRetryAttempts = 0;
                    retryNativeShutdown = true;
                }
            }
        }

#if UNITY_EDITOR
        if (retryEnvironmentRestore || retryNativeShutdown)
            ScheduleShutdownRetry();
#endif
    }

    private static void LogRuntimeInfoWithoutStackTrace(string message)
    {
        Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
    }

#if UNITY_EDITOR
    void EditorPlayStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            DestroyROS2ForUnity();
        }
    }
#endif
}

}  // namespace ROS2
