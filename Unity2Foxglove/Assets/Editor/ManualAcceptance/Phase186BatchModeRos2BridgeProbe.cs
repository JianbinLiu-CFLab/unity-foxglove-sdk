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
        private const string ManualPointerRelativePath =
            "Library/Phase186Acceptance/current-run.json";
        private static readonly string SessionPrefix =
            "Unity2Foxglove.Phase186BatchModeRos2BridgeProbe."
            + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)
            + ".";

        private static bool _handlersAttached;
        private static Phase186RunConfiguration _configuration;

        [InitializeOnLoadMethod]
        private static void Register()
        {
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

        [MenuItem(
            "Foxglove/Manual Acceptance/Phase186/Prepare Current Bridge Run")]
        public static void PrepareCurrentManualRun()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "Prepare the Phase186 manual run in Edit Mode.");
            var pointer = Path.Combine(ProjectRoot(), ManualPointerRelativePath);
            var configuration = Phase186RunConfiguration.Load(pointer);
            if (!configuration.Manual)
                throw new InvalidDataException(
                    "The current run pointer does not name a manual case.");
            EditorSceneManager.OpenScene(
                Phase186Ros2BridgeAcceptanceBuilder.AcceptanceSceneAssetPath,
                OpenSceneMode.Single);
            Phase186Ros2BridgeAcceptanceBuilder.ConfigureOpenSceneForRun(
                configuration);
            Debug.Log(
                "PHASE186_MANUAL_SCENE_READY run=" + configuration.RunId
                + " case=" + configuration.CaseId
                + " tokenHash=" + configuration.TokenHash
                + " head=" + configuration.Head);
            SceneView.RepaintAll();
        }

        [MenuItem(
            "Foxglove/Manual Acceptance/Phase186/Prepare Current Bridge Run",
            validate = true)]
        private static bool CanPrepareCurrentManualRun()
            => !Application.isBatchMode
               && !EditorApplication.isPlayingOrWillChangePlaymode
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
                        StringComparison.Ordinal)
                    || topic.IndexOf("phase181", StringComparison.OrdinalIgnoreCase) >= 0
                    || topic.IndexOf("phase184", StringComparison.OrdinalIgnoreCase) >= 0)
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
