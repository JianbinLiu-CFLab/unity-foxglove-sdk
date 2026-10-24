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
        private const string RunConfigArgument = "-phase186RunConfig";
        private const double TimeoutSeconds = 900d;
        private const double ManualPreparationTimeoutSeconds = 900d;
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
            if (SessionState.GetBool(Key("requested"), false)
                && _configuration == null
                && !TryRestoreBatchConfiguration())
            {
                return;
            }
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

    }
}
#endif
