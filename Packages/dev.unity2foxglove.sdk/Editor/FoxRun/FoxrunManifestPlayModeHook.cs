// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Refreshes FoxRun canonical manifest artifacts before Editor Play Mode.

using System;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    [InitializeOnLoad]
    internal static class FoxrunManifestPlayModeHook
    {
        static FoxrunManifestPlayModeHook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode)
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Debug.LogWarning("[FoxRun] Skipping canonical manifest refresh before Play Mode while Unity is compiling or updating assets.");
                return;
            }

            try
            {
                var manifest = FoxrunCodeGenerator.GenerateManifestFilesOnly();
                Debug.Log("[FoxRun] Refreshed canonical manifest and schema info before Play Mode: " + manifest.GlobalManifestHash);
            }
            catch (Exception ex)
            {
                Debug.LogError("[FoxRun] Failed to refresh canonical manifest before Play Mode:\n" + ex);
            }
        }
    }
}
