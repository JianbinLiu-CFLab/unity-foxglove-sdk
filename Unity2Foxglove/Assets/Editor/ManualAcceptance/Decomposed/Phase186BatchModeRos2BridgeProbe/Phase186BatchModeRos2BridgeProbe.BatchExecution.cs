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
    public static partial class Phase186BatchModeRos2BridgeProbe
    {
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
            if (_configuration == null)
            {
                RequestExit(5, "configuration-unavailable");
                return;
            }
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

        private static bool TryRestoreBatchConfiguration()
        {
            try
            {
                _configuration = LoadFromCommandLine();
                return true;
            }
            catch (Exception exception)
            {
                AttachHandlers();
                RequestExit(5, "configuration-unavailable");
                Debug.LogException(exception);
                return false;
            }
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

    }
}
#endif
