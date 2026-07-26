// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase181
// Purpose: Batch-mode Editor driver for the custom ROS2 interop scene.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.ManualAcceptance;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Process = System.Diagnostics.Process;

/// <summary>
/// Drives the tracked Phase181 acceptance scene only when named as a batch-mode
/// execute method. It observes the component's bounded evidence markers rather
/// than creating a ROS node, reading peer files, or changing package selection.
/// </summary>
public static class Phase181BatchModeCustomRos2InteropProbe
{
    private const string ExecuteMethodName =
        "Phase181BatchModeCustomRos2InteropProbe.Run";
    private const double EvidenceTimeoutSeconds = 480.0;
    private const double CompletionDwellSeconds = 3.0;
    private const int MaximumPlayEntryRetries = 3;
    private const string EnvelopeTypeName =
        "unity2foxglove_foxrun_interfaces_v1.msg.Phase181State48D288ED82F1Envelope";
    private const string GeneratedAssemblyName = "unity2foxglove_foxrun_interfaces_v1_assembly";

    private static readonly Regex SafeNativeLibraryName = new Regex(
        @"(?<![A-Za-z0-9_])(?:unity2foxglove_[A-Za-z0-9_]+|[A-Za-z0-9_]+\.dll)(?![A-Za-z0-9_])",
        RegexOptions.CultureInvariant);

    private static readonly Regex SafeWin32ErrorCode = new Regex(
        @"\bWin32 error (?<code>[0-9]{1,5})\b",
        RegexOptions.CultureInvariant);

    private static readonly string SessionPrefix =
        "Unity2Foxglove.Phase181BatchModeCustomRos2InteropProbe."
        + Process.GetCurrentProcess().Id + ".";

    private static bool _handlersAttached;
    private static bool _runtimeReady;
    private static bool _interfaceReady;
    private static bool _publishArmed;
    private static bool _subscribeApplied;
    private static int _bidirectionalAppliedCount;
    private static bool _sameOriginDropped;
    private static double _playStartedAt;
    private static double _completionObservedAt;

    [InitializeOnLoadMethod]
    private static void RegisterFromCommandLine()
    {
        if (!IsRequestedBatchRun())
            return;

        AttachHandlers();
        if (!SessionState.GetBool(SessionKey("requested"), false))
        {
            Run();
            return;
        }

        if (SessionState.GetBool(SessionKey("play-entry-retry-queued"), false))
            EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
        else if (SessionState.GetBool(SessionKey("play-entry-pending"), false))
            EditorApplication.delayCall += RetryCanceledPlayEntry;
    }

    /// <summary>
    /// Batch entry point:
    /// <c>-executeMethod Phase181BatchModeCustomRos2InteropProbe.Run</c>.
    /// The caller owns the selected R2FU package and every ROS environment
    /// variable. This probe owns only Editor Play Mode lifetime.
    /// </summary>
    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            throw new InvalidOperationException(
                "Phase181BatchModeCustomRos2InteropProbe only runs in a Unity batch-mode process.");
        }

        AttachHandlers();
        if (SessionState.GetBool(SessionKey("requested"), false))
            return;

        SessionState.SetBool(SessionKey("requested"), true);
        SessionState.SetInt(SessionKey("play-entry-retries"), 0);
        SessionState.SetBool(SessionKey("play-entry-pending"), false);
        SessionState.SetBool(SessionKey("play-entry-retry-queued"), false);
        EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
    }

    private static void AttachHandlers()
    {
        if (_handlersAttached)
            return;

        _handlersAttached = true;
        Application.logMessageReceived -= OnLogMessage;
        Application.logMessageReceived += OnLogMessage;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
    }

    private static void DetachHandlers()
    {
        if (!_handlersAttached)
            return;

        _handlersAttached = false;
        Application.logMessageReceived -= OnLogMessage;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.update -= OnEditorUpdate;
    }

    private static void OpenSceneAndEnterPlayMode()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        SessionState.SetBool(SessionKey("play-entry-pending"), false);
        SessionState.SetBool(SessionKey("play-entry-retry-queued"), false);
        ResetEvidence();
        EditorSceneManager.OpenScene(Phase181CustomRos2InterfacePlayerBuilder.AcceptanceSceneAssetPath);
        if (!NormalizeLegacyDuplicateReceiver())
        {
            Debug.LogError("PHASE181_BATCH_CUSTOM_ROS2_PROBE_EXIT outcome=invalid-acceptance-scene exitCode=5");
            EditorApplication.delayCall += () => EditorApplication.Exit(5);
            return;
        }
        if (!ProbeGeneratedMessageNativeLoad())
        {
            Debug.LogError("PHASE181_BATCH_CUSTOM_ROS2_PROBE_EXIT outcome=native-message-load-failure exitCode=4");
            EditorApplication.delayCall += () => EditorApplication.Exit(4);
            return;
        }
        if (IsNativeLoadOnly())
        {
            Debug.Log("PHASE181_BATCH_CUSTOM_ROS2_PROBE_EXIT outcome=native-message-load-only exitCode=0");
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        Debug.Log(
            "PHASE181_BATCH_CUSTOM_ROS2_PROBE_SCENE_OPENED scene="
            + Phase181CustomRos2InterfacePlayerBuilder.AcceptanceSceneAssetPath);
        SessionState.SetBool(SessionKey("play-entry-pending"), true);
        EditorApplication.EnterPlaymode();
        EditorApplication.delayCall += RetryCanceledPlayEntry;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(SessionKey("play-entry-pending"), false);
            SessionState.SetBool(SessionKey("play-entry-retry-queued"), false);
            _playStartedAt = EditorApplication.timeSinceStartup;
            _completionObservedAt = 0.0;
            Debug.Log("PHASE181_BATCH_CUSTOM_ROS2_PROBE_PLAY_ENTERED");
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        if (!SessionState.GetBool(SessionKey("exit-requested"), false))
        {
            QueuePlayEntryRetry("editor-returned-before-entry");
            return;
        }

        var exitCode = SessionState.GetInt(SessionKey("exit-code"), 3);
        DetachHandlers();
        EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
    }

    private static void RetryCanceledPlayEntry()
    {
        if (!IsRequestedBatchRun()
            || SessionState.GetBool(SessionKey("exit-requested"), false)
            || !SessionState.GetBool(SessionKey("play-entry-pending"), false)
            || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RetryCanceledPlayEntry;
            return;
        }

        QueuePlayEntryRetry("play-canceled-before-edit-mode-transition");
    }

    private static void QueuePlayEntryRetry(string reason)
    {
        if (SessionState.GetBool(SessionKey("play-entry-retry-queued"), false))
            return;

        var retries = SessionState.GetInt(SessionKey("play-entry-retries"), 0) + 1;
        SessionState.SetInt(SessionKey("play-entry-retries"), retries);
        if (retries > MaximumPlayEntryRetries)
        {
            SessionState.SetBool(SessionKey("play-entry-pending"), false);
            RequestEditorExit(6, "play-entry-retry-limit");
            return;
        }

        SessionState.SetBool(SessionKey("play-entry-retry-queued"), true);
        Debug.Log(
            "PHASE181_BATCH_CUSTOM_ROS2_PROBE_PLAY_RETRY"
            + " reason=" + reason
            + " attempt=" + retries
            + " maximum=" + MaximumPlayEntryRetries);
        EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
    }

    private static void OnEditorUpdate()
    {
        if (!IsRequestedBatchRun() || !EditorApplication.isPlaying)
            return;

        if (HasCompletePeerProof())
        {
            if (_completionObservedAt <= 0.0)
            {
                _completionObservedAt = EditorApplication.timeSinceStartup;
                Debug.Log("PHASE181_BATCH_CUSTOM_ROS2_PROBE_EVIDENCE_COMPLETE");
            }
            else if (EditorApplication.timeSinceStartup - _completionObservedAt >= CompletionDwellSeconds)
            {
                RequestExit(0, "peer-proof-complete");
            }
            return;
        }

        if (_playStartedAt > 0.0
            && EditorApplication.timeSinceStartup - _playStartedAt >= EvidenceTimeoutSeconds)
        {
            Debug.LogError(
                "PHASE181_BATCH_CUSTOM_ROS2_PROBE_TIMEOUT evidence=" + DescribeEvidence());
            RequestExit(2, "peer-proof-timeout");
        }
    }

    private static void OnLogMessage(string condition, string _, LogType type)
    {
        if (!IsRequestedBatchRun() || string.IsNullOrEmpty(condition))
            return;

        if (condition.IndexOf("PHASE181_CUSTOM_ROS2_FAIL", StringComparison.Ordinal) >= 0
            || condition.IndexOf("PHASE181_CUSTOM_ROS2_UNAVAILABLE", StringComparison.Ordinal) >= 0)
        {
            Debug.LogError("PHASE181_BATCH_CUSTOM_ROS2_PROBE_COMPONENT_FAILURE");
            RequestExit(3, "component-failure");
            return;
        }

        _runtimeReady |= condition.IndexOf("PHASE181_CUSTOM_ROS2_READY", StringComparison.Ordinal) >= 0;
        _interfaceReady |= condition.IndexOf("PHASE181_CUSTOM_INTERFACE_READY", StringComparison.Ordinal) >= 0;
        _publishArmed |= condition.IndexOf("PHASE181_CUSTOM_ROS2_PUBLISHED", StringComparison.Ordinal) >= 0;
        _sameOriginDropped |= condition.IndexOf(
                                  "PHASE181_CUSTOM_ROS2_SAME_ORIGIN_DROPPED",
                                  StringComparison.Ordinal) >= 0;

        if (condition.IndexOf("PHASE181_CUSTOM_ROS2_APPLIED", StringComparison.Ordinal) < 0)
            return;

        if (condition.IndexOf("topic=/foxrun/phase181/custom/subscribe", StringComparison.Ordinal) >= 0)
            _subscribeApplied = true;
        if (condition.IndexOf("topic=/foxrun/phase181/custom/bidirectional", StringComparison.Ordinal) >= 0)
            _bidirectionalAppliedCount++;
    }

    private static bool HasCompletePeerProof()
        => _runtimeReady
           && _interfaceReady
           && _publishArmed
           && _subscribeApplied
           && _bidirectionalAppliedCount >= 2
           && _sameOriginDropped;

    private static string DescribeEvidence()
        => "runtime=" + _runtimeReady
           + " interface=" + _interfaceReady
           + " publish=" + _publishArmed
           + " subscribe=" + _subscribeApplied
           + " bidirectionalApplied=" + _bidirectionalAppliedCount
           + " sameOriginDropped=" + _sameOriginDropped;

    private static void ResetEvidence()
    {
        _runtimeReady = false;
        _interfaceReady = false;
        _publishArmed = false;
        _subscribeApplied = false;
        _bidirectionalAppliedCount = 0;
        _sameOriginDropped = false;
        _playStartedAt = 0.0;
        _completionObservedAt = 0.0;
    }

    private static bool NormalizeLegacyDuplicateReceiver()
    {
        var acceptanceScene = SceneManager.GetSceneByPath(
            Phase181CustomRos2InterfacePlayerBuilder.AcceptanceSceneAssetPath);
        if (!acceptanceScene.IsValid() || !acceptanceScene.isLoaded)
            return false;

        var managers = new List<FoxgloveManager>();
        var receivers = new List<Phase181FoxRunCustomRos2InterfaceAcceptance>();
        foreach (var root in acceptanceScene.GetRootGameObjects())
        {
            managers.AddRange(root.GetComponentsInChildren<FoxgloveManager>(includeInactive: true));
            receivers.AddRange(root.GetComponentsInChildren<Phase181FoxRunCustomRos2InterfaceAcceptance>(includeInactive: true));
        }

        if (managers.Count != 1)
            return false;
        if (receivers.Count == 1)
            return true;
        if (receivers.Count != 2)
            return false;

        Phase181FoxRunCustomRos2InterfaceAcceptance managerAttached = null;
        var standaloneCount = 0;
        foreach (var receiver in receivers)
        {
            if (receiver.gameObject == managers[0].gameObject)
                managerAttached = receiver;
            else
                standaloneCount++;
        }
        if (managerAttached == null || standaloneCount != 1)
            return false;

        UnityEngine.Object.DestroyImmediate(managerAttached);
        if (!EditorSceneManager.SaveScene(acceptanceScene))
            return false;
        Debug.Log("PHASE181_BATCH_CUSTOM_ROS2_PROBE_SCENE_NORMALIZED removed=manager-attached-duplicate-receiver");
        return true;
    }

    /// <summary>
    /// Isolates native-message activation before Play Mode. This is a batch-only
    /// diagnosis seam: it never changes package selection or scene data, and
    /// reports only a bounded exception-class chain plus a safe DLL/simple-name
    /// token (never an arbitrary backend message or filesystem path).
    /// </summary>
    private static bool ProbeGeneratedMessageNativeLoad()
    {
        try
        {
            var assembly = Assembly.Load(GeneratedAssemblyName);
            var messageType = assembly.GetType(EnvelopeTypeName, throwOnError: true);
            Debug.Log(
                "PHASE181_BATCH_NATIVE_MESSAGE_IMPORTS libraries="
                + DescribeDeclaredNativeImports(messageType));
            var instance = Activator.CreateInstance(messageType);
            var disposable = instance as IDisposable;
            if (disposable != null)
                disposable.Dispose();
            Debug.Log("PHASE181_BATCH_NATIVE_MESSAGE_LOAD_OK");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "PHASE181_BATCH_NATIVE_MESSAGE_LOAD_FAILURE detail="
                + DescribeNativeLoadFailure(exception));
            return false;
        }
    }

    private static string DescribeNativeLoadFailure(Exception exception)
    {
        var typeChain = string.Empty;
        var library = string.Empty;
        var win32Error = string.Empty;
        var current = exception;
        for (var depth = 0; current != null && depth < 4; depth++)
        {
            typeChain = string.IsNullOrEmpty(typeChain)
                ? current.GetType().Name
                : typeChain + ">" + current.GetType().Name;
            if (string.IsNullOrEmpty(library))
            {
                var match = SafeNativeLibraryName.Match(current.Message ?? string.Empty);
                if (match.Success)
                    library = match.Value;
            }
            if (string.IsNullOrEmpty(win32Error))
            {
                var match = SafeWin32ErrorCode.Match(current.Message ?? string.Empty);
                if (match.Success)
                    win32Error = match.Groups["code"].Value;
            }
            current = current.InnerException;
        }

        return "types=" + typeChain
               + " library=" + (string.IsNullOrEmpty(library) ? "none" : library)
               + " win32=" + (string.IsNullOrEmpty(win32Error) ? "none" : win32Error);
    }

    private static string DescribeDeclaredNativeImports(Type messageType)
    {
        var result = string.Empty;
        var types = messageType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
        for (var typeIndex = 0; typeIndex <= types.Length; typeIndex++)
        {
            var current = typeIndex == types.Length ? messageType : types[typeIndex];
            var methods = current.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            for (var methodIndex = 0; methodIndex < methods.Length; methodIndex++)
            {
                var import = Attribute.GetCustomAttribute(methods[methodIndex], typeof(DllImportAttribute))
                    as DllImportAttribute;
                if (import == null || string.IsNullOrEmpty(import.Value))
                    continue;

                var match = SafeNativeLibraryName.Match(import.Value);
                if (!match.Success || result.IndexOf(match.Value, StringComparison.Ordinal) >= 0)
                    continue;

                result = string.IsNullOrEmpty(result) ? match.Value : result + "," + match.Value;
                if (result.Length >= 512)
                    return result;
            }
        }

        return string.IsNullOrEmpty(result) ? "none" : result;
    }

    private static void RequestExit(int exitCode, string outcome)
    {
        if (SessionState.GetBool(SessionKey("exit-requested"), false))
            return;

        SessionState.SetBool(SessionKey("exit-requested"), true);
        SessionState.SetInt(SessionKey("exit-code"), exitCode);
        Debug.Log(
            "PHASE181_BATCH_CUSTOM_ROS2_PROBE_EXIT outcome=" + outcome
            + " exitCode=" + exitCode);
        EditorApplication.delayCall += EditorApplication.ExitPlaymode;
    }

    private static void RequestEditorExit(int exitCode, string outcome)
    {
        if (SessionState.GetBool(SessionKey("exit-requested"), false))
            return;

        SessionState.SetBool(SessionKey("exit-requested"), true);
        SessionState.SetInt(SessionKey("exit-code"), exitCode);
        Debug.LogError(
            "PHASE181_BATCH_CUSTOM_ROS2_PROBE_EXIT outcome=" + outcome
            + " exitCode=" + exitCode);
        DetachHandlers();
        EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
    }

    private static bool IsRequestedBatchRun()
    {
        if (!Application.isBatchMode)
            return false;

        var arguments = Environment.GetCommandLineArgs();
        for (var index = 0; index < arguments.Length; index++)
        {
            if (string.Equals(arguments[index], ExecuteMethodName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsNativeLoadOnly()
    {
        var arguments = Environment.GetCommandLineArgs();
        for (var index = 0; index < arguments.Length; index++)
        {
            if (string.Equals(arguments[index], "-phase181NativeLoadOnly", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string SessionKey(string suffix)
        => SessionPrefix + suffix;
}
#endif
