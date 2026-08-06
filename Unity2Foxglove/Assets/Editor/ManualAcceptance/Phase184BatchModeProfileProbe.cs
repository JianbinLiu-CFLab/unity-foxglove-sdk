// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase184
// Purpose: Owns only Editor Play Mode for one correlated Phase184-G run.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity2Foxglove.ManualAcceptance;
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
    private static bool _playModeExitQueued;
    private static bool _editorExitQueued;
    private static string _caseId = string.Empty;
    private static string _token = string.Empty;
    private static double _startedAt = -1d;
    private static bool _terminalPassObserved;
    private static bool _terminalStateValid = true;
    private static double _terminalPassAt = -1d;
    private static string[] _requiredWorkerResultPaths = Array.Empty<string>();

    [InitializeOnLoadMethod]
    private static void RegisterFromCommandLine()
    {
        Phase184ManualProfilePreflight.EnsureRegistered();
        if (!IsRequestedBatchRun())
            return;
        AttachHandlers();
        if (!SessionState.GetBool(SessionKey("requested"), false))
        {
            Run();
            return;
        }

        if (SessionState.GetBool(SessionKey("exit-requested"), false))
        {
            ResumeRequestedExit();
            return;
        }
        if (_startedAt < 0d)
        {
            RequestEditorExit(2, "missing-startup-deadline");
            return;
        }
        if (SessionState.GetBool(SessionKey("play-entry-retry-queued"), false))
            EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
        else if (SessionState.GetBool(SessionKey("play-entry-pending"), false))
            EditorApplication.delayCall += RetryCanceledPlayEntry;
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
        SessionState.SetBool(SessionKey("play-entry-pending"), false);
        SessionState.SetBool(SessionKey("play-entry-retry-queued"), false);
        SessionState.EraseString(SessionKey("exit-outcome"));
        _playModeExitQueued = false;
        _editorExitQueued = false;
        ResetTerminalState();
        SetStartedAt(EditorApplication.timeSinceStartup);
        SchedulePlayEntryAttempt();
    }

    private static void AttachHandlers()
    {
        RestoreRunIdentity();
        RestoreRunState();
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
        if (!IsRequestedBatchRun()
            || SessionState.GetBool(SessionKey("exit-requested"), false))
        {
            return;
        }
        SessionState.SetBool(SessionKey("play-entry-retry-queued"), false);
        if (StartupDeadlineExpired())
        {
            RequestEditorExit(2, "startup-deadline");
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            SchedulePlayEntryAttempt();
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        SessionState.SetBool(SessionKey("play-entry-pending"), false);
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
            SessionState.SetBool(SessionKey("play-entry-pending"), true);
            EditorApplication.EnterPlaymode();
            EditorApplication.delayCall += RetryCanceledPlayEntry;
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

    private static void RestoreRunState()
    {
        if (_startedAt < 0d
            && TryRestoreTime("started-at", out var restoredStartedAt))
        {
            _startedAt = restoredStartedAt;
        }

        if (!_terminalPassObserved)
        {
            _terminalPassObserved = SessionState.GetBool(
                SessionKey("terminal-pass-observed"),
                false);
        }
        if (!_terminalPassObserved)
            return;

        _terminalStateValid =
            TryRestoreTime("terminal-pass-at", out _terminalPassAt)
            && TryRestoreWorkerResultPaths(out _requiredWorkerResultPaths);
    }

    private static void SetStartedAt(double value)
    {
        _startedAt = value;
        PersistTime("started-at", value);
    }

    private static void PersistTime(string suffix, double value)
    {
        SessionState.SetString(
            SessionKey(suffix),
            value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static bool TryRestoreTime(string suffix, out double value)
    {
        var serialized = SessionState.GetString(SessionKey(suffix), string.Empty);
        return double.TryParse(
                serialized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value)
            && !double.IsNaN(value)
            && !double.IsInfinity(value)
            && value >= 0d;
    }

    private static bool TryRestoreWorkerResultPaths(out string[] paths)
    {
        paths = Array.Empty<string>();
        try
        {
            var serialized = SessionState.GetString(
                SessionKey("worker-result-paths"),
                string.Empty);
            if (string.IsNullOrWhiteSpace(serialized))
                return false;
            var values = JArray.Parse(serialized);
            var restored = new List<string>();
            foreach (var value in values)
            {
                var path = (string)value;
                if (string.IsNullOrWhiteSpace(path))
                    return false;
                restored.Add(Path.GetFullPath(path));
            }
            if (restored.Count == 0)
                return false;
            paths = restored.ToArray();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ResetTerminalState()
    {
        _terminalPassObserved = false;
        _terminalStateValid = true;
        _terminalPassAt = -1d;
        _requiredWorkerResultPaths = Array.Empty<string>();
        SessionState.SetBool(SessionKey("terminal-pass-observed"), false);
        SessionState.EraseString(SessionKey("terminal-pass-at"));
        SessionState.EraseString(SessionKey("worker-result-paths"));
    }

    private static bool StartupDeadlineExpired()
        => _startedAt < 0d
           || EditorApplication.timeSinceStartup - _startedAt
           >= StartupAndEvidenceTimeoutSeconds;

    private static void SchedulePlayEntryAttempt()
    {
        if (SessionState.GetBool(SessionKey("exit-requested"), false)
            || SessionState.GetBool(
                SessionKey("play-entry-retry-queued"),
                false))
        {
            return;
        }
        SessionState.SetBool(SessionKey("play-entry-retry-queued"), true);
        EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
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
            SessionState.SetBool(SessionKey("play-entry-pending"), false);
            SessionState.SetBool(SessionKey("play-entry-retry-queued"), false);
            SetStartedAt(EditorApplication.timeSinceStartup);
            Debug.Log("PHASE184G_BATCH_PLAY_ENTERED case=" + _caseId);
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
        var outcome = SessionState.GetString(
            SessionKey("exit-outcome"),
            "resumed-editor-exit");
        RequestEditorExit(exitCode, outcome);
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

        if (StartupDeadlineExpired())
        {
            RequestEditorExit(2, "startup-deadline");
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

        Debug.Log(
            "PHASE184G_BATCH_PLAY_RETRY case=" + _caseId
            + " reason=" + reason
            + " attempt=" + retries
            + " maximum=" + MaximumPlayEntryRetries);
        SchedulePlayEntryAttempt();
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
            if (!_terminalStateValid)
            {
                RequestExit(7, "restored-worker-state-invalid");
                return;
            }
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
        if (!StartupDeadlineExpired())
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
            SessionState.SetString(
                SessionKey("worker-result-paths"),
                new JArray(_requiredWorkerResultPaths).ToString(Formatting.None));
            PersistTime("terminal-pass-at", _terminalPassAt);
            SessionState.SetBool(SessionKey("terminal-pass-observed"), true);
            _terminalPassObserved = true;
            _terminalStateValid = true;
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
        if (_requiredWorkerResultPaths == null
            || _requiredWorkerResultPaths.Length == 0)
        {
            return false;
        }
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
        var searchStart = 0;
        while (searchStart < condition.Length)
        {
            var index = condition.IndexOf(marker, searchStart, StringComparison.Ordinal);
            if (index < 0)
                return false;
            if (index > 0 && !char.IsWhiteSpace(condition[index - 1]))
            {
                searchStart = index + marker.Length;
                continue;
            }

            var end = index + marker.Length;
            if (end == condition.Length || char.IsWhiteSpace(condition[end]))
                return true;
            searchStart = end;
        }

        return false;
    }

    private static void RequestExit(int exitCode, string outcome)
    {
        if (SessionState.GetBool(SessionKey("exit-requested"), false))
            return;
        SessionState.SetBool(SessionKey("exit-requested"), true);
        SessionState.SetInt(SessionKey("exit-code"), exitCode);
        SessionState.SetString(SessionKey("exit-outcome"), outcome);
        Debug.Log(
            "PHASE184G_BATCH_EXIT case=" + _caseId
            + " outcome=" + outcome
            + " exitCode=" + exitCode);
        if (EditorApplication.isPlaying)
            SchedulePlayModeExit();
        else
            RequestEditorExit(exitCode, outcome);
    }

    private static void SchedulePlayModeExit()
    {
        if (_playModeExitQueued)
            return;
        _playModeExitQueued = true;
        EditorApplication.delayCall += ExitPlayModeNow;
        try
        {
            QuiesceAcceptanceSources();
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "PHASE184G_BATCH_QUIESCE_FAIL case=" + _caseId
                + " type=" + exception.GetType().Name);
        }
    }

    private static void ExitPlayModeNow()
    {
        _playModeExitQueued = false;
        if (!SessionState.GetBool(SessionKey("exit-requested"), false))
            return;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.ExitPlaymode();
            return;
        }
        ResumeRequestedExit();
    }

    private static void QuiesceAcceptanceSources()
    {
        var routes = UnityEngine.Object.FindObjectsByType<Phase184AcceptanceRoute>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        var disabled = 0;
        foreach (var route in routes)
        {
            if (route == null || !route.isActiveAndEnabled)
                continue;
            route.enabled = false;
            disabled++;
        }
        Debug.Log(
            "PHASE184G_BATCH_SOURCES_QUIESCED case=" + _caseId
            + " count=" + disabled);
    }

    private static void RequestEditorExit(int exitCode, string outcome)
    {
        if (!SessionState.GetBool(SessionKey("exit-requested"), false))
        {
            SessionState.SetBool(SessionKey("exit-requested"), true);
            SessionState.SetInt(SessionKey("exit-code"), exitCode);
            SessionState.SetString(SessionKey("exit-outcome"), outcome);
        }
        var committedExitCode = SessionState.GetInt(
            SessionKey("exit-code"),
            exitCode);
        var committedOutcome = SessionState.GetString(
            SessionKey("exit-outcome"),
            outcome);
        if (_editorExitQueued)
            return;
        _editorExitQueued = true;
        Debug.Log(
            "PHASE184G_BATCH_EDITOR_EXIT case=" + _caseId
            + " outcome=" + committedOutcome
            + " exitCode=" + committedExitCode);
        DetachHandlers();
        EditorApplication.delayCall += () =>
        {
            _editorExitQueued = false;
            EditorApplication.Exit(committedExitCode);
        };
    }

    private static void ResumeRequestedExit()
    {
        if (!SessionState.GetBool(SessionKey("exit-requested"), false))
            return;
        if (EditorApplication.isPlaying)
        {
            SchedulePlayModeExit();
            return;
        }
        RequestEditorExit(
            SessionState.GetInt(SessionKey("exit-code"), 3),
            SessionState.GetString(
                SessionKey("exit-outcome"),
                "resumed-editor-exit"));
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
