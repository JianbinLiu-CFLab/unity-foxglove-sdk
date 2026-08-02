// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase186
// Purpose: Owns one token-correlated Unity Play Mode acceptance run.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity2Foxglove
{
    public static class Phase186BatchModeRos2BridgeProbe
    {
        private const string RunConfigArgument = "-phase186RunConfig";
        private const double TimeoutSeconds = 900d;
        internal const string ManualPointerRelativePath =
            "Library/Phase186Acceptance/current-run.json";
        private const int ManualPreparationMaxSchemaRefreshes = 3;
        private static readonly string SessionPrefix =
            "Unity2Foxglove.Phase186BatchModeRos2BridgeProbe."
            + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)
            + ".";

        private static bool _handlersAttached;
        private static bool _manualPreparationQueued;
        private static Phase186RunConfiguration _configuration;

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnManualPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnManualPlayModeStateChanged;
            if (!Application.isBatchMode)
            {
                ResumePendingManualPreparation();
                return;
            }
            if (!Application.isBatchMode || !HasArgument(RunConfigArgument))
                return;
            AttachHandlers();
            if (!SessionState.GetBool(Key("requested"), false))
            {
                Run();
                return;
            }
            if (SessionState.GetBool(Key("exit-requested"), false))
            {
                EditorApplication.delayCall += CompleteRequestedExit;
                return;
            }
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
        }

        /// <summary>Unity Batch entry point.</summary>
        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException(
                    "Phase186 Batch probe requires Unity Batch mode.");
            AttachHandlers();
            if (SessionState.GetBool(Key("requested"), false))
                return;
            _configuration = LoadFromCommandLine();
            SessionState.SetBool(Key("requested"), true);
            SessionState.SetBool(Key("exit-requested"), false);
            SessionState.SetBool(Key("terminal"), false);
            SessionState.SetString(
                Key("started-at"),
                DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
        }

        /// <summary>Batch proof for the exact manual-pointer authority path.</summary>
        public static void ValidateManualPointerInBatch()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException(
                    "Phase186 manual pointer validation requires Batch mode.");
            var pointer = Path.Combine(ProjectRoot(), ManualPointerRelativePath);
            var configuration =
                Phase186RunConfiguration.LoadManualPointer(pointer);
            if (!configuration.Manual)
                throw new InvalidDataException(
                    "The current run pointer does not name a manual case.");
            Debug.Log(
                "PHASE186_MANUAL_POINTER_BATCH_PASS run=" + configuration.RunId
                + " case=" + configuration.CaseId
                + " tokenHash=" + configuration.TokenHash
                + " head=" + configuration.Head);
        }

        [MenuItem(
            "Foxglove/Manual Acceptance/Phase186/Prepare Current Bridge Run")]
        public static void PrepareCurrentManualRun()
        {
            if (Application.isBatchMode
                || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating)
                throw new InvalidOperationException(
                    "Prepare the Phase186 manual run in idle Edit Mode.");
            var pointer = Path.Combine(ProjectRoot(), ManualPointerRelativePath);
            try
            {
                var configuration =
                    Phase186RunConfiguration.LoadManualPointer(pointer);
                if (!configuration.Manual)
                    throw new InvalidDataException(
                        "The current run pointer does not name a manual case.");
                BeginManualPreparation(configuration);
                EditorSceneManager.OpenScene(
                    Phase186Ros2BridgeAcceptanceBuilder.AcceptanceSceneAssetPath,
                    OpenSceneMode.Single);
                Debug.Log(
                    "PHASE186_MANUAL_SCENE_PREPARING run=" + configuration.RunId
                    + " case=" + configuration.CaseId
                    + " tokenHash=" + configuration.TokenHash
                    + " head=" + configuration.Head);
                SceneView.RepaintAll();
                QueueManualPreparation();
            }
            catch (Exception exception)
            {
                FailManualPreparation(pointer, exception);
            }
        }

        private static void BeginManualPreparation(
            Phase186RunConfiguration configuration)
        {
            SessionState.SetBool(Key("manual-prepare-pending"), true);
            SessionState.SetString(Key("manual-prepare-run"), configuration.RunId);
            SessionState.SetString(Key("manual-prepare-case"), configuration.CaseId);
            SessionState.SetString(
                Key("manual-prepare-token-hash"),
                configuration.TokenHash);
            SessionState.SetString(Key("manual-prepare-head"), configuration.Head);
            SessionState.SetInt(Key("manual-prepare-refreshes"), 0);
        }

        private static void ResumePendingManualPreparation()
        {
            if (SessionState.GetBool(Key("manual-prepare-pending"), false))
                QueueManualPreparation();
        }

        private static void OnManualPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
                return;
            var pointer = Path.Combine(ProjectRoot(), ManualPointerRelativePath);
            if (!Phase186RunConfiguration.TryReadManualPointerIdentity(
                    pointer,
                    out var runId,
                    out var caseId,
                    out var tokenHash,
                    out var head))
            {
                return;
            }
            Debug.Log(
                "PHASE186_MANUAL_PLAY_EXITED run=" + runId
                + " case=" + caseId
                + " tokenHash=" + tokenHash
                + " head=" + head);
        }

        private static void QueueManualPreparation()
        {
            if (_manualPreparationQueued)
                return;
            _manualPreparationQueued = true;
            EditorApplication.delayCall += ContinueManualPreparation;
        }

        private static void ContinueManualPreparation()
        {
            _manualPreparationQueued = false;
            if (!SessionState.GetBool(Key("manual-prepare-pending"), false))
                return;
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating)
            {
                QueueManualPreparation();
                return;
            }

            try
            {
                var pointer = Path.Combine(ProjectRoot(), ManualPointerRelativePath);
                var configuration =
                    Phase186RunConfiguration.LoadManualPointer(pointer);
                ValidateManualPreparationIdentity(configuration);
                var manifestRefresh =
                    FoxrunCodeGenerator.GenerateManifestFilesOnlyWithResult();
                AssetDatabase.SaveAssets();
                if (manifestRefresh.SchemaInfoChanged)
                {
                    var refreshes = SessionState.GetInt(
                        Key("manual-prepare-refreshes"),
                        0);
                    if (refreshes >= ManualPreparationMaxSchemaRefreshes)
                    {
                        throw new InvalidOperationException(
                            "Phase186 manual schema generation did not stabilize after "
                            + ManualPreparationMaxSchemaRefreshes.ToString(
                                CultureInfo.InvariantCulture)
                            + " refreshes.");
                    }
                    refreshes++;
                    SessionState.SetInt(
                        Key("manual-prepare-refreshes"),
                        refreshes);
                    Debug.Log(
                        "PHASE186_MANUAL_SCENE_PREPARING run=" + configuration.RunId
                        + " case=" + configuration.CaseId
                        + " tokenHash=" + configuration.TokenHash
                        + " head=" + configuration.Head
                        + " schemaRefresh=" + refreshes.ToString(
                            CultureInfo.InvariantCulture));
                    QueueManualPreparation();
                    AssetDatabase.Refresh(
                        ImportAssetOptions.ForceSynchronousImport);
                    return;
                }

                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    QueueManualPreparation();
                    return;
                }
                Phase186Ros2BridgeAcceptanceBuilder.ConfigureOpenSceneForRun(
                    configuration);
                CompleteManualPreparation(configuration, manifestRefresh);
            }
            catch (Exception exception)
            {
                FailManualPreparation(
                    Path.Combine(ProjectRoot(), ManualPointerRelativePath),
                    exception);
            }
        }

        private static void CompleteManualPreparation(
            Phase186RunConfiguration configuration,
            FoxrunCodeGenerator.FoxRunManifestRefreshResult manifestRefresh)
        {
            ClearManualPreparation();
            Debug.Log(
                "PHASE186_MANUAL_SCENE_READY run=" + configuration.RunId
                + " case=" + configuration.CaseId
                + " tokenHash=" + configuration.TokenHash
                + " head=" + configuration.Head
                + " manifest=" + manifestRefresh.Manifest.GlobalManifestHash
                + " schemaInfoChanged=false");
            SceneView.RepaintAll();
        }

        private static void ValidateManualPreparationIdentity(
            Phase186RunConfiguration configuration)
        {
            if (!configuration.Manual
                || !string.Equals(
                    SessionState.GetString(Key("manual-prepare-run"), string.Empty),
                    configuration.RunId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    SessionState.GetString(Key("manual-prepare-case"), string.Empty),
                    configuration.CaseId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    SessionState.GetString(
                        Key("manual-prepare-token-hash"),
                        string.Empty),
                    configuration.TokenHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    SessionState.GetString(Key("manual-prepare-head"), string.Empty),
                    configuration.Head,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The pending Phase186 manual preparation identity changed.");
            }
        }

        private static void ClearManualPreparation()
        {
            SessionState.SetBool(Key("manual-prepare-pending"), false);
            SessionState.SetString(Key("manual-prepare-run"), string.Empty);
            SessionState.SetString(Key("manual-prepare-case"), string.Empty);
            SessionState.SetString(Key("manual-prepare-token-hash"), string.Empty);
            SessionState.SetString(Key("manual-prepare-head"), string.Empty);
            SessionState.SetInt(Key("manual-prepare-refreshes"), 0);
        }

        private static void FailManualPreparation(
            string pointer,
            Exception exception)
        {
            var runId = SessionState.GetString(
                Key("manual-prepare-run"),
                string.Empty);
            var caseId = SessionState.GetString(
                Key("manual-prepare-case"),
                string.Empty);
            var tokenHash = SessionState.GetString(
                Key("manual-prepare-token-hash"),
                string.Empty);
            var head = SessionState.GetString(
                Key("manual-prepare-head"),
                string.Empty);
            if ((string.IsNullOrWhiteSpace(runId)
                 || string.IsNullOrWhiteSpace(caseId)
                 || string.IsNullOrWhiteSpace(tokenHash)
                 || string.IsNullOrWhiteSpace(head))
                && Phase186RunConfiguration.TryReadManualPointerIdentity(
                    pointer,
                    out var pointerRunId,
                    out var pointerCaseId,
                    out var pointerTokenHash,
                    out var pointerHead))
            {
                runId = pointerRunId;
                caseId = pointerCaseId;
                tokenHash = pointerTokenHash;
                head = pointerHead;
            }
            ClearManualPreparation();
            Debug.LogError(
                "PHASE186_MANUAL_SCENE_PREPARE_FAIL run=" + runId
                + " case=" + caseId
                + " tokenHash=" + tokenHash
                + " head=" + head
                + " reason=" + exception.GetType().Name);
            Debug.LogException(exception);
        }

        [MenuItem(
            "Foxglove/Manual Acceptance/Phase186/Prepare Current Bridge Run",
            validate = true)]
        private static bool CanPrepareCurrentManualRun()
            => !Application.isBatchMode
               && !EditorApplication.isPlayingOrWillChangePlaymode
               && !EditorApplication.isCompiling
               && !EditorApplication.isUpdating
               && !SessionState.GetBool(Key("manual-prepare-pending"), false)
               && File.Exists(Path.Combine(ProjectRoot(), ManualPointerRelativePath));

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
            if (SessionState.GetBool(Key("exit-requested"), false)
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
                return;
            }
            try
            {
                _configuration ??= LoadFromCommandLine();
                EditorSceneManager.OpenScene(
                    Phase186Ros2BridgeAcceptanceBuilder.AcceptanceSceneAssetPath,
                    OpenSceneMode.Single);
                Phase186Ros2BridgeAcceptanceBuilder.ConfigureOpenSceneForRun(
                    _configuration);
                Debug.Log(
                    "PHASE186_ACCEPTANCE_SCENE_READY run=" + _configuration.RunId
                    + " case=" + _configuration.CaseId
                    + " tokenHash=" + _configuration.TokenHash
                    + " head=" + _configuration.Head);
                SessionState.SetBool(Key("play-pending"), true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RequestExit(5, "preplay-failed");
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetBool(Key("play-pending"), false);
                Debug.Log("PHASE186_ACCEPTANCE_PLAY_ENTERED");
                return;
            }
            if (state == PlayModeStateChange.EnteredEditMode
                && SessionState.GetBool(Key("exit-requested"), false))
            {
                EditorApplication.delayCall += CompleteRequestedExit;
            }
        }

        private static void OnEditorUpdate()
        {
            if (!SessionState.GetBool(Key("requested"), false))
                return;
            if (SessionState.GetBool(Key("exit-requested"), false))
            {
                if (EditorApplication.isPlaying)
                    EditorApplication.ExitPlaymode();
                else
                    CompleteRequestedExit();
                return;
            }
            if (!TryGetStartedAt(out var startedAt)
                || (DateTime.UtcNow - startedAt).TotalSeconds > TimeoutSeconds)
            {
                RequestExit(4, "timeout");
            }
        }

        private static void OnLogMessage(
            string condition,
            string _stackTrace,
            LogType _type)
        {
            if (!SessionState.GetBool(Key("requested"), false)
                || SessionState.GetBool(Key("exit-requested"), false))
            {
                return;
            }
            _configuration ??= LoadFromCommandLine();
            var line = (condition ?? string.Empty).Trim();
            var expected = _configuration.Manual
                ? _configuration.ManualTerminalMarker("PASS")
                : _configuration.AutomaticTerminalMarker("PASS");
            if (string.Equals(line, expected, StringComparison.Ordinal))
            {
                SessionState.SetBool(Key("terminal"), true);
                RequestExit(0, "pass");
                return;
            }
            if (line.StartsWith("PHASE186_ACCEPTANCE_PASS ", StringComparison.Ordinal)
                || line.StartsWith("PHASE186_MANUAL_COMPLETE ", StringComparison.Ordinal))
            {
                RequestExit(6, "stale-or-foreign-terminal");
                return;
            }
            if (line.StartsWith(
                    "PHASE186_ACCEPTANCE_CONTEXT_FAIL",
                    StringComparison.Ordinal)
                || line.StartsWith("PHASE186_ACCEPTANCE_FAIL ", StringComparison.Ordinal))
            {
                RequestExit(7, "unity-context-failed");
            }
        }

        private static void RequestExit(int exitCode, string outcome)
        {
            if (SessionState.GetBool(Key("exit-requested"), false))
                return;
            SessionState.SetBool(Key("exit-requested"), true);
            SessionState.SetInt(Key("exit-code"), exitCode);
            SessionState.SetString(Key("outcome"), outcome ?? "unknown");
            Debug.Log(
                "PHASE186_ACCEPTANCE_BATCH_EXIT_REQUEST code=" + exitCode
                + " outcome=" + (outcome ?? "unknown"));
            if (EditorApplication.isPlaying)
                EditorApplication.ExitPlaymode();
            else
                EditorApplication.delayCall += CompleteRequestedExit;
        }

        private static void CompleteRequestedExit()
        {
            if (!SessionState.GetBool(Key("exit-requested"), false))
                return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorApplication.isPlaying)
                    EditorApplication.ExitPlaymode();
                return;
            }
            var exitCode = SessionState.GetInt(Key("exit-code"), 8);
            var outcome = SessionState.GetString(Key("outcome"), "unknown");
            Debug.Log(
                "PHASE186_ACCEPTANCE_BATCH_EXIT code=" + exitCode
                + " outcome=" + outcome);
            DetachHandlers();
            EditorApplication.Exit(exitCode);
        }

        private static Phase186RunConfiguration LoadFromCommandLine()
        {
            var path = ReadArgumentValue(RunConfigArgument);
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException(
                    "Phase186 Batch probe requires -phase186RunConfig.");
            return Phase186RunConfiguration.Load(path);
        }

        private static bool HasArgument(string name)
            => Array.Exists(
                Environment.GetCommandLineArgs(),
                value => string.Equals(value, name, StringComparison.Ordinal));

        private static string ReadArgumentValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                    return args[i + 1];
            }
            return null;
        }

        private static bool TryGetStartedAt(out DateTime startedAt)
        {
            startedAt = default;
            var value = SessionState.GetString(Key("started-at"), string.Empty);
            return long.TryParse(
                       value,
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out var ticks)
                   && ticks > 0
                   && TryCreateUtc(ticks, out startedAt);
        }

        private static bool TryCreateUtc(long ticks, out DateTime value)
        {
            try
            {
                value = new DateTime(ticks, DateTimeKind.Utc);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                value = default;
                return false;
            }
        }

        private static string Key(string suffix) => SessionPrefix + suffix;

        private static string ProjectRoot()
            => Path.GetDirectoryName(Application.dataPath)
               ?? throw new DirectoryNotFoundException(
                   "Unity project root could not be resolved.");
    }

    internal sealed class Phase186RunConfiguration
    {
        private static readonly HashSet<string> ExpectedKeys =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "schemaVersion", "runId", "token", "tokenHash", "caseId",
                "rowId", "runtimeRowId", "distro", "rmw", "manual", "head", "repository",
                "projectPath", "outputRoot", "bridgeHost", "bridgePort",
                "foxgloveHost", "foxglovePort",
                "domainId", "interfaceType", "interfaceDigest", "topics",
                "requiredActors", "unityLog", "externalGate", "exerciseGate", "createdAt",
            };

        private Phase186RunConfiguration()
        {
        }

        internal string RunId { get; private set; }
        internal string CaseId { get; private set; }
        internal string TokenHash { get; private set; }
        internal string Head { get; private set; }
        internal string[] Topics { get; private set; }
        internal bool Manual { get; private set; }
        internal int BridgePort { get; private set; }
        internal int FoxglovePort { get; private set; }
        internal string OutputRoot { get; private set; }
        internal string ExternalGate { get; private set; }
        internal string ExerciseGate { get; private set; }
        internal bool SlowMainThread =>
            string.Equals(
                CaseId,
                "slow-main-thread-640hz",
                StringComparison.Ordinal)
            || CaseId.StartsWith("manual-", StringComparison.Ordinal);

        internal static Phase186RunConfiguration LoadManualPointer(string path)
        {
            var fullPath = Path.GetFullPath(path ?? string.Empty);
            var currentProject = Normalize(
                Path.GetDirectoryName(Application.dataPath)
                ?? throw new DirectoryNotFoundException());
            var expectedPointer = Normalize(
                Path.Combine(
                    currentProject,
                    "Library",
                    "Phase186Acceptance",
                    "current-run.json"));
            if (!string.Equals(
                    Normalize(fullPath),
                    expectedPointer,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Phase186 manual pointer path differs from authority.");
            }

            var pointerBytes = File.ReadAllBytes(fullPath);
            var pointerJson = JObject.Parse(
                Encoding.UTF8.GetString(pointerBytes));
            var project = Normalize(RequireString(pointerJson, "projectPath"));
            var repository = Normalize(RequireString(pointerJson, "repository"));
            var output = Normalize(RequireString(pointerJson, "outputRoot"));
            var ownedRoot = Normalize(Path.Combine(repository, "build", "phase186"));
            if (!string.Equals(
                    project,
                    currentProject,
                    StringComparison.OrdinalIgnoreCase)
                || !IsBelow(output, ownedRoot))
            {
                throw new InvalidDataException(
                    "Phase186 manual pointer paths differ from authority.");
            }

            var authorityPath = Path.Combine(output, "run-config.json");
            var authorityBytes = File.ReadAllBytes(authorityPath);
            if (!pointerBytes.SequenceEqual(authorityBytes))
            {
                throw new InvalidDataException(
                    "Phase186 manual pointer differs from its run config.");
            }
            return Load(authorityPath);
        }

        internal static bool TryReadManualPointerIdentity(
            string path,
            out string runId,
            out string caseId,
            out string tokenHash,
            out string head)
        {
            runId = string.Empty;
            caseId = string.Empty;
            tokenHash = string.Empty;
            head = string.Empty;
            try
            {
                var fullPath = Path.GetFullPath(path ?? string.Empty);
                var file = new FileInfo(fullPath);
                if (!file.Exists || file.Length <= 0 || file.Length > 64 * 1024)
                    return false;
                var json = JObject.Parse(
                    File.ReadAllText(fullPath, Encoding.UTF8));
                var candidateRunId = RequireString(json, "runId");
                var candidateCaseId = RequireString(json, "caseId");
                var candidateTokenHash = RequireString(json, "tokenHash");
                var candidateHead = RequireString(json, "head");
                if (!candidateRunId.StartsWith(
                        "phase186h-",
                        StringComparison.Ordinal)
                    || candidateRunId.Length > 80
                    || !candidateCaseId.StartsWith(
                        "manual-",
                        StringComparison.Ordinal)
                    || !IsLowerHex(candidateTokenHash, 64)
                    || !IsLowerHex(candidateHead, 40))
                {
                    return false;
                }
                runId = candidateRunId;
                caseId = candidateCaseId;
                tokenHash = candidateTokenHash;
                head = candidateHead;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static Phase186RunConfiguration Load(string path)
        {
            var fullPath = Path.GetFullPath(path ?? string.Empty);
            var json = JObject.Parse(File.ReadAllText(fullPath, Encoding.UTF8));
            var actualKeys = new HashSet<string>(
                json.Properties().Select(property => property.Name),
                StringComparer.Ordinal);
            if (!actualKeys.SetEquals(ExpectedKeys))
                throw new InvalidDataException("Phase186 run config keys differ.");
            if (RequireInt(json, "schemaVersion") != 3)
                throw new InvalidDataException("Phase186 run config schema differs.");

            var token = RequireString(json, "token");
            var configuration = new Phase186RunConfiguration
            {
                RunId = RequireString(json, "runId"),
                CaseId = RequireString(json, "caseId"),
                TokenHash = RequireString(json, "tokenHash"),
                Head = RequireString(json, "head"),
                Topics = RequireStringArray(json, "topics", 1, 3),
                Manual = RequireBool(json, "manual"),
                BridgePort = RequireInt(json, "bridgePort"),
                FoxglovePort = RequireInt(json, "foxglovePort"),
                OutputRoot = RequireString(json, "outputRoot"),
                ExternalGate = RequireString(json, "externalGate"),
                ExerciseGate = RequireString(json, "exerciseGate"),
            };
            if (!configuration.RunId.StartsWith("phase186h-", StringComparison.Ordinal)
                || configuration.RunId.Length > 80
                || !IsLowerHex(configuration.TokenHash, 64)
                || !IsLowerHex(configuration.Head, 40)
                || !string.Equals(
                    configuration.TokenHash,
                    Sha256(token),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Phase186 run identity is malformed.");
            }
            if (configuration.Manual
                != configuration.CaseId.StartsWith("manual-", StringComparison.Ordinal))
                throw new InvalidDataException("Phase186 manual mode differs from case.");
            if (configuration.BridgePort < 1 || configuration.BridgePort > 65535
                || !string.Equals(
                    RequireString(json, "bridgeHost"),
                    "127.0.0.1",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Phase186 Bridge endpoint is invalid.");
            }
            if (configuration.FoxglovePort < 1
                || configuration.FoxglovePort > 65535
                || configuration.FoxglovePort == configuration.BridgePort
                || !string.Equals(
                    RequireString(json, "foxgloveHost"),
                    "127.0.0.1",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Phase186 Foxglove endpoint is invalid.");
            }
            if (!string.Equals(
                    RequireString(json, "interfaceDigest"),
                    Unity2Foxglove.ManualAcceptance
                        .Phase186Ros2BridgeAcceptance.InterfaceDigest,
                    StringComparison.Ordinal)
                || !string.Equals(
                    RequireString(json, "interfaceType"),
                    "unity2foxglove_foxrun_interfaces_v1/msg/"
                    + "Phase181State48D288ED82F1Envelope",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Phase181 interface identity differs.");
            }

            var project = Normalize(RequireString(json, "projectPath"));
            var currentProject = Normalize(
                Path.GetDirectoryName(Application.dataPath)
                ?? throw new DirectoryNotFoundException());
            var repository = Normalize(RequireString(json, "repository"));
            var output = Normalize(RequireString(json, "outputRoot"));
            var ownedRoot = Normalize(Path.Combine(repository, "build", "phase186"));
            if (!string.Equals(project, currentProject, StringComparison.OrdinalIgnoreCase)
                || !IsBelow(output, ownedRoot)
                || !string.Equals(
                    Path.GetFileName(output),
                    configuration.RunId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    Normalize(fullPath),
                    Normalize(Path.Combine(output, "run-config.json")),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Phase186 run paths differ from authority.");
            }
            if (!string.Equals(
                    Normalize(RequireString(json, "unityLog")),
                    Normalize(Path.Combine(output, "unity.log")),
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    Normalize(configuration.ExternalGate),
                    Normalize(Path.Combine(output, "unity-external-gate.json")),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Phase186 live evidence paths differ from authority.");
            }
            if (!string.Equals(
                    Normalize(configuration.ExerciseGate),
                    Normalize(Path.Combine(output, "unity-exercise-gate.json")),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Phase186 exercise evidence path differs from authority.");
            }
            if (!string.Equals(
                    ReadGitHead(repository),
                    configuration.Head,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Phase186 feature SHA is stale.");
            }
            foreach (var topic in configuration.Topics)
            {
                if (!topic.StartsWith(
                        "/foxrun/phase186/" + token + "/",
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Phase186 topic is stale or foreign.");
                }
            }
            if (configuration.Topics.Distinct(StringComparer.Ordinal).Count()
                != configuration.Topics.Length)
                throw new InvalidDataException("Phase186 topics contain duplicates.");
            return configuration;
        }

        internal string AutomaticTerminalMarker(string verdict)
            => "PHASE186_ACCEPTANCE_" + verdict
               + " run=" + RunId
               + " case=" + CaseId
               + " tokenHash=" + TokenHash
               + " head=" + Head
               + " verdict=" + verdict;

        internal string ManualTerminalMarker(string verdict)
            => "PHASE186_MANUAL_COMPLETE case=" + CaseId
               + " run=" + RunId
               + " tokenHash=" + TokenHash
               + " head=" + Head
               + " verdict=" + verdict;

        private static string RequireString(JObject json, string name)
        {
            var token = json[name];
            if (token?.Type != JTokenType.String)
                throw new InvalidDataException(name + " must be a string.");
            var value = (string)token;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 1024)
                throw new InvalidDataException(name + " is empty or unbounded.");
            return value;
        }

        private static int RequireInt(JObject json, string name)
        {
            var token = json[name];
            if (token?.Type != JTokenType.Integer)
                throw new InvalidDataException(name + " must be an integer.");
            return checked((int)(long)token);
        }

        private static bool RequireBool(JObject json, string name)
        {
            var token = json[name];
            if (token?.Type != JTokenType.Boolean)
                throw new InvalidDataException(name + " must be boolean.");
            return (bool)token;
        }

        private static string[] RequireStringArray(
            JObject json,
            string name,
            int minimum,
            int maximum)
        {
            if (!(json[name] is JArray array)
                || array.Count < minimum
                || array.Count > maximum)
            {
                throw new InvalidDataException(name + " has invalid cardinality.");
            }
            var result = new string[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i]?.Type != JTokenType.String)
                    throw new InvalidDataException(name + " contains a non-string.");
                result[i] = (string)array[i];
            }
            return result;
        }

        private static string Sha256(string value)
        {
            using var algorithm = SHA256.Create();
            var bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var item in bytes)
                builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static string ReadGitHead(string repository)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse HEAD",
                    WorkingDirectory = repository,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            if (!process.Start() || !process.WaitForExit(30000) || process.ExitCode != 0)
                throw new InvalidDataException("Current Git HEAD could not be read.");
            return process.StandardOutput.ReadToEnd().Trim();
        }

        private static string Normalize(string path)
            => Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        private static bool IsBelow(string path, string parent)
            => path.StartsWith(
                parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        private static bool IsLowerHex(string value, int length)
        {
            if (value == null || value.Length != length)
                return false;
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')))
                    return false;
            }
            return true;
        }
    }
}
#endif
