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
            SessionState.SetString(
                Key("manual-prepare-started-at"),
                EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture));
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
            if (double.TryParse(
                    SessionState.GetString(Key("manual-prepare-started-at"), string.Empty),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var manualStartedAt)
                && EditorApplication.timeSinceStartup - manualStartedAt
                    >= ManualPreparationTimeoutSeconds)
            {
                FailManualPreparation(
                    Path.Combine(ProjectRoot(), ManualPointerRelativePath),
                    new TimeoutException("Phase186 manual preparation exceeded its bounded timeout."));
                return;
            }
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

    }
}
#endif
