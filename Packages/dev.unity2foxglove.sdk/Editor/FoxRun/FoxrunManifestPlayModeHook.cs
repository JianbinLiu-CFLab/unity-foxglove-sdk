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
                EditorApplication.isPlaying = false;
                Debug.LogWarning("[FoxRun] Skipping canonical manifest refresh before Play Mode while Unity is compiling or updating assets. Play Mode was canceled.");
                return;
            }

            try
            {
                Unity2FoxgloveSchemaEvidenceSettings.SyncOpenSceneManagers();
                var refresh = FoxrunCodeGenerator.GenerateManifestFilesOnlyWithResult();
                var aggregate = Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts(refresh.Manifest);
                Debug.Log("[FoxRun] Refreshed canonical manifest, schema info, and SDK schema manifest before Play Mode: " +
                          refresh.Manifest.GlobalManifestHash + " / " + aggregate.SdkSchemaManifestHash);

                if (refresh.SchemaInfoChanged)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    EditorApplication.isPlaying = false;
                    // The manifest, schema info, and descriptor are refreshed as
                    // one artifact set, then Unity recompiles before the next Play attempt
                    // observes the generated schema-info constants.
                    Debug.LogWarning(
                        "[FoxRun] Generated FoxRunSchemaInfo.g.cs changed before Play Mode. " +
                        "Unity must recompile it before runtime schema consumers can use the new manifest hash. " +
                        "Play Mode was canceled; press Play again after compilation finishes.");
                }
            }
            catch (Exception ex)
            {
                EditorApplication.isPlaying = false;
                Debug.LogError("[FoxRun] Failed to refresh canonical manifest before Play Mode:\n" + ex);
            }
        }

    }
}
