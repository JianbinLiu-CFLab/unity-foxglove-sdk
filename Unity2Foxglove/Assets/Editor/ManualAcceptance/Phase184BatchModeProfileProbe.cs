// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase184
// Purpose: Owns only Editor Play Mode for one correlated Phase184-G run.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Process = System.Diagnostics.Process;

namespace Unity2Foxglove
{
public static class Phase184BatchModeProfileProbe
{
    private const string ExecuteMethodName =
        "Unity2Foxglove.Phase184BatchModeProfileProbe.Run";
    private const string RunConfigArgument = "-phase184RunConfig";
    private const double StartupAndEvidenceTimeoutSeconds = 900d;
    private const double WorkerResultDrainTimeoutSeconds = 30d;
    private const int MaximumPlayEntryRetries = 3;
    private static readonly string[] WorkerRoles =
    {
        "foxglove-client",
        "ros2-peer",
        "graph-observer",
    };
    private static readonly string SessionPrefix =
        "Unity2Foxglove.Phase184BatchModeProfileProbe."
        + Process.GetCurrentProcess().Id + ".";

    private static bool _handlersAttached;
    private static string _caseId = string.Empty;
    private static string _token = string.Empty;
    private static double _startedAt;
    private static bool _terminalPassObserved;
    private static double _terminalPassAt;
    private static string[] _requiredWorkerResultPaths = Array.Empty<string>();

    [InitializeOnLoadMethod]
    private static void RegisterFromCommandLine()
    {
        Phase184ManualProfilePreflight.EnsureRegistered();
        if (!IsRequestedBatchRun())
            return;
        AttachHandlers();
        if (!SessionState.GetBool(SessionKey("requested"), false))
            Run();
    }

    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            throw new InvalidOperationException(
                "Phase184BatchModeProfileProbe only runs in Unity Batch mode.");
        }
        AttachHandlers();
        if (SessionState.GetBool(SessionKey("requested"), false))
            return;

        SessionState.SetBool(SessionKey("requested"), true);
        SessionState.SetBool(SessionKey("exit-requested"), false);
        SessionState.SetInt(SessionKey("play-entry-retries"), 0);
        _terminalPassObserved = false;
        _requiredWorkerResultPaths = Array.Empty<string>();
        _startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
    }

    private static void AttachHandlers()
    {
        RestoreRunIdentity();
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

        try
        {
            var config = LoadRunConfig();
            _caseId = (string)config["case"] ?? string.Empty;
            _token = (string)config["token"] ?? string.Empty;
            PersistRunIdentity();
            EditorSceneManager.OpenScene(
                Phase184FoxRunProfileAcceptanceBuilder.AcceptanceSceneAssetPath);
            Phase184FoxRunProfileAcceptanceBuilder.ConfigureOpenSceneForRun(config);
            Debug.Log(
                "PHASE184G_BATCH_SCENE_OPENED case=" + _caseId
                + " scene="
                + Phase184FoxRunProfileAcceptanceBuilder.AcceptanceSceneAssetPath);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "PHASE184G_BATCH_PREPLAY_FAIL type="
                + exception.GetType().Name);
            RequestEditorExit(5, "invalid-preplay");
        }
    }

    private static void PersistRunIdentity()
    {
        SessionState.SetString(SessionKey("case"), _caseId);
        SessionState.SetString(SessionKey("token"), _token);
    }

    private static void RestoreRunIdentity()
    {
        if (string.IsNullOrEmpty(_caseId))
            _caseId = SessionState.GetString(SessionKey("case"), string.Empty);
        if (string.IsNullOrEmpty(_token))
            _token = SessionState.GetString(SessionKey("token"), string.Empty);
    }

    private static JObject LoadRunConfig()
    {
        var path = ReadCommandLineValue(RunConfigArgument);
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Missing -phase184RunConfig.");
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists || info.Length <= 0 || info.Length > 1024 * 1024)
            throw new InvalidOperationException("Run config is missing, empty, or oversized.");
        var config = JObject.Parse(File.ReadAllText(info.FullName));
        var caseId = (string)config["case"] ?? string.Empty;
        var token = (string)config["token"] ?? string.Empty;
        if (!IsKnownCase(caseId) || !IsSafeToken(token))
            throw new InvalidOperationException("Run config case/token is malformed.");
        return config;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            _startedAt = EditorApplication.timeSinceStartup;
            Debug.Log("PHASE184G_BATCH_PLAY_ENTERED case=" + _caseId);
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        if (!SessionState.GetBool(SessionKey("exit-requested"), false))
        {
            var retries = SessionState.GetInt(SessionKey("play-entry-retries"), 0) + 1;
            SessionState.SetInt(SessionKey("play-entry-retries"), retries);
            if (retries > MaximumPlayEntryRetries)
            {
                RequestEditorExit(6, "play-entry-retry-limit");
                return;
            }
            EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
            return;
        }

        var exitCode = SessionState.GetInt(SessionKey("exit-code"), 3);
        DetachHandlers();
        EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
    }

    private static void OnEditorUpdate()
    {
        if (!IsRequestedBatchRun()
            || SessionState.GetBool(SessionKey("exit-requested"), false))
        {
            return;
        }
        if (_terminalPassObserved)
        {
            if (AllRequiredWorkerResultsReady())
            {
                RequestExit(0, "case-proof-and-worker-results-complete");
                return;
            }
            if (EditorApplication.timeSinceStartup - _terminalPassAt
                >= WorkerResultDrainTimeoutSeconds)
            {
                Debug.LogError(
                    "PHASE184G_BATCH_DRAIN_TIMEOUT case=" + _caseId);
                RequestExit(7, "worker-result-drain-timeout");
            }
            return;
        }
        if (EditorApplication.timeSinceStartup - _startedAt
            < StartupAndEvidenceTimeoutSeconds)
        {
            return;
        }

        Debug.LogError(
            "PHASE184G_BATCH_TIMEOUT case=" + _caseId);
        RequestExit(2, "evidence-timeout");
    }

    private static void OnLogMessage(string condition, string _, LogType type)
    {
        if (!IsRequestedBatchRun()
            || string.IsNullOrEmpty(condition))
        {
            return;
        }
        if (condition.IndexOf("PHASE184G_CONTEXT_FAIL", StringComparison.Ordinal) >= 0)
        {
            RequestExit(3, "context-failure");
            return;
        }
        if (!HasExactRunToken(condition))
            return;
        if (condition.IndexOf("PHASE184G_CASE_FAIL", StringComparison.Ordinal) >= 0)
        {
            RequestExit(3, "component-failure");
            return;
        }
        if (condition.IndexOf("PHASE184G_CASE_PASS", StringComparison.Ordinal) >= 0)
            BeginSuccessfulExit();
    }

    private static void BeginSuccessfulExit()
    {
        if (_terminalPassObserved)
            return;
        try
        {
            var config = LoadRunConfig();
            var resultFiles = config["resultFiles"] as JObject
                ?? throw new InvalidOperationException(
                    "Run config resultFiles are missing.");
            var paths = new List<string>();
            foreach (var role in WorkerRoles)
            {
                if (resultFiles[role] == null)
                    continue;
                var path = (string)resultFiles[role];
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new InvalidOperationException(
                        "Worker result path is malformed.");
                }
                paths.Add(Path.GetFullPath(path));
            }
            if (paths.Count == 0)
            {
                throw new InvalidOperationException(
                    "Run config declares no required worker results.");
            }
            _requiredWorkerResultPaths = paths.ToArray();
            _terminalPassAt = EditorApplication.timeSinceStartup;
            _terminalPassObserved = true;
            Debug.Log(
                "PHASE184G_BATCH_DRAINING case=" + _caseId
                + " workers=" + paths.Count);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "PHASE184G_BATCH_DRAIN_CONFIG_FAIL type="
                + exception.GetType().Name);
            RequestExit(7, "worker-result-drain-config");
        }
    }

    private static bool AllRequiredWorkerResultsReady()
    {
        foreach (var path in _requiredWorkerResultPaths)
        {
            try
            {
                var result = new FileInfo(path);
                if (!result.Exists || result.Length <= 0)
                    return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasExactRunToken(string condition)
    {
        var marker = "token=" + _token;
        var index = condition.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return false;
        var end = index + marker.Length;
        return end == condition.Length || char.IsWhiteSpace(condition[end]);
    }

    private static void RequestExit(int exitCode, string outcome)
    {
        if (SessionState.GetBool(SessionKey("exit-requested"), false))
            return;
        SessionState.SetBool(SessionKey("exit-requested"), true);
        SessionState.SetInt(SessionKey("exit-code"), exitCode);
        Debug.Log(
            "PHASE184G_BATCH_EXIT case=" + _caseId
            + " outcome=" + outcome
            + " exitCode=" + exitCode);
        if (EditorApplication.isPlaying)
            EditorApplication.delayCall += EditorApplication.ExitPlaymode;
        else
            RequestEditorExit(exitCode, outcome);
    }

    private static void RequestEditorExit(int exitCode, string outcome)
    {
        SessionState.SetBool(SessionKey("exit-requested"), true);
        SessionState.SetInt(SessionKey("exit-code"), exitCode);
        Debug.Log(
            "PHASE184G_BATCH_EDITOR_EXIT case=" + _caseId
            + " outcome=" + outcome
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

    private static bool IsKnownCase(string value)
        => value == "foxglove-profile"
           || value == "multi-target"
           || value == "degraded-target"
           || value == "qos-contract"
           || value == "stream-640hz";

    private static bool IsSafeToken(string token)
    {
        if (string.IsNullOrEmpty(token)
            || token.Length < 18
            || token.Length > 70
            || !token.StartsWith("p184g_", StringComparison.Ordinal))
        {
            return false;
        }
        for (var index = 6; index < token.Length; index++)
        {
            if (!char.IsLetterOrDigit(token[index]))
                return false;
        }
        return true;
    }

    private static string ReadCommandLineValue(string name)
    {
        var arguments = Environment.GetCommandLineArgs();
        for (var index = 0; index + 1 < arguments.Length; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                return arguments[index + 1];
        }
        return string.Empty;
    }

    private static string SessionKey(string suffix)
        => SessionPrefix + suffix;
}

/// <summary>
/// Applies only the helper-owned manual run's visible Manager selection while
/// the user remains the sole owner of interactive Editor and Play Mode.
/// </summary>
[InitializeOnLoad]
internal static class Phase184ManualProfilePreflight
{
    private const string ManualCaseSessionKey =
        "Unity2Foxglove.Phase184ManualProfilePreflight.Case";
    private const string ManualTokenSessionKey =
        "Unity2Foxglove.Phase184ManualProfilePreflight.Token";
    private static bool _registered;
    private static string _manualCase = string.Empty;
    private static string _manualToken = string.Empty;

    static Phase184ManualProfilePreflight()
    {
        EnsureRegistered();
    }

    internal static void EnsureRegistered()
    {
        if (_registered)
            return;
        _registered = true;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (Application.isBatchMode)
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            RestoreManualIdentity();
            if (!string.IsNullOrEmpty(_manualCase)
                && !string.IsNullOrEmpty(_manualToken))
            {
                Debug.Log(
                    "PHASE184G_MANUAL_PLAY_ENTERED case=" + _manualCase
                    + " token=" + _manualToken);
            }
            return;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            RestoreManualIdentity();
            if (!string.IsNullOrEmpty(_manualCase)
                && !string.IsNullOrEmpty(_manualToken))
            {
                Debug.Log(
                    "PHASE184G_MANUAL_PLAY_EXITED case=" + _manualCase
                    + " token=" + _manualToken);
            }
            _manualCase = string.Empty;
            _manualToken = string.Empty;
            SessionState.EraseString(ManualCaseSessionKey);
            SessionState.EraseString(ManualTokenSessionKey);
            return;
        }
        if (state != PlayModeStateChange.ExitingEditMode)
            return;
        try
        {
            var repository = Directory.GetParent(
                Directory.GetParent(Application.dataPath)?.FullName
                ?? string.Empty)?.FullName;
            if (string.IsNullOrWhiteSpace(repository))
                return;
            var pointerPath = Path.Combine(
                repository,
                "build",
                "phase184",
                "acceptance",
                "manual-active.json");
            if (!File.Exists(pointerPath))
                return;
            var pointer = JObject.Parse(File.ReadAllText(pointerPath));
            var configPath = (string)pointer["runConfig"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
                throw new InvalidOperationException("manual-active.json has no live run config.");
            var config = JObject.Parse(File.ReadAllText(configPath));
            _manualCase = (string)config["case"] ?? string.Empty;
            _manualToken = (string)config["token"] ?? string.Empty;
            SessionState.SetString(ManualCaseSessionKey, _manualCase);
            SessionState.SetString(ManualTokenSessionKey, _manualToken);
            Phase184FoxRunProfileAcceptanceBuilder.ConfigureOpenSceneForRun(config);
            Debug.Log(
                "PHASE184G_MANUAL_PROFILE_PREPARED case="
                + _manualCase
                + " token=" + _manualToken);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "PHASE184G_MANUAL_PROFILE_PREPARE_FAIL type="
                + exception.GetType().Name);
            EditorApplication.delayCall += EditorApplication.ExitPlaymode;
        }
    }

    private static void RestoreManualIdentity()
    {
        if (string.IsNullOrEmpty(_manualCase))
            _manualCase = SessionState.GetString(ManualCaseSessionKey, string.Empty);
        if (string.IsNullOrEmpty(_manualToken))
            _manualToken = SessionState.GetString(ManualTokenSessionKey, string.Empty);
    }
}
}
#endif
