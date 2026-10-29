using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ROS2
{

internal partial class ROS2ForUnity
{
    internal void DestroyROS2ForUnity()
    {
        bool shouldShutdown = false;
        lock (initMutex)
        {
            if (!ownsReference)
            {
                return;
            }

            ownsReference = false;
            if (referenceCount > 0)
            {
                referenceCount--;
            }

            if (referenceCount == 0)
            {
                shouldShutdown = TryBeginShutdownLocked();
            }
        }

        if (shouldShutdown)
        {
            CompleteShutdownShared();
        }
    }

    public void Dispose()
    {
        DestroyROS2ForUnity();
        GC.SuppressFinalize(this);
    }

    private static void ShutdownShared()
    {
        bool shouldShutdown = false;
        lock (initMutex)
        {
            shouldShutdown = TryBeginShutdownLocked();
        }

        if (shouldShutdown)
        {
            CompleteShutdownShared();
        }
    }

    private static bool TryBeginShutdownLocked()
    {
        if (shutdownInProgress)
        {
            return false;
        }

        shutdownInProgress = true;
        if (!isInitialized)
        {
            referenceCount = 0;
            shutdownInProgress = false;
            return false;
        }

        return true;
    }

    internal static void RetryPendingShutdown()
    {
        bool restoreOnly;
        lock (initMutex)
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
        lock (initMutex)
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
        lock (initMutex)
        {
            if (!isInitialized)
            {
                referenceCount = 0;
                bool environmentRestored = Ros2ForUnityProcessEnvironmentLease.Restore(
                    Environment.OSVersion.Platform == PlatformID.Win32NT);
                if (environmentRestored)
                {
                    shutdownInProgress = false;
                    shutdownRetryAttempts = 0;
                    pathConfigured = false;
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
                        Ros2cs.Shutdown();
                        nativeShutdownCompleted = true;
                        nativeShutdownSucceeded = true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        retryNativeShutdown = true;
                    }
                }

                if (nativeShutdownSucceeded)
                {
                    try
                    {
#if UNITY_EDITOR
                        UnregisterEditorHandlers();
#endif
                        UnregisterCtrlCHandlerStatic();
                        Ros2ForUnityNativePluginBootstrap.ResetNativeLibraryRegistration();
                        isInitialized = false;
                        referenceCount = 0;
                        nativeShutdownCompleted = false;
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        retryNativeShutdown = true;
                    }
                }

                if (!isInitialized)
                {
                    referenceCount = 0;
                    bool environmentRestored = Ros2ForUnityProcessEnvironmentLease.Restore(
                        Environment.OSVersion.Platform == PlatformID.Win32NT);
                    if (environmentRestored)
                    {
                        shutdownInProgress = false;
                        shutdownRetryAttempts = 0;
                        pathConfigured = false;
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

    private static void UnregisterCtrlCHandlerStatic()
    {
#if ENABLE_MONO
        if (consoleCancelHandler != null)
        {
            Console.CancelKeyPress -= consoleCancelHandler;
            consoleCancelHandler = null;
        }
#endif
    }

#if UNITY_EDITOR
    private static void RegisterEditorHandlers()
    {
        if (editorHandlersRegistered)
        {
            return;
        }

        EditorApplication.playModeStateChanged += EditorPlayStateChanged;
        EditorApplication.quitting += ShutdownShared;
        AssemblyReloadEvents.beforeAssemblyReload += ShutdownShared;
        editorHandlersRegistered = true;
    }

    private static void UnregisterEditorHandlers()
    {
        if (!editorHandlersRegistered)
        {
            return;
        }

        EditorApplication.playModeStateChanged -= EditorPlayStateChanged;
        EditorApplication.quitting -= ShutdownShared;
        AssemblyReloadEvents.beforeAssemblyReload -= ShutdownShared;
        editorHandlersRegistered = false;
    }

    private static void EditorPlayStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            ShutdownShared();
        }
    }
#endif
}

}  // namespace ROS2
