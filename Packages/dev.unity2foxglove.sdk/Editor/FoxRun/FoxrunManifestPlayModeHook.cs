// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Refreshes FoxRun canonical manifest artifacts before Editor Play Mode.

using System;
using System.IO;
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
                var schemaInfoPath = Path.Combine(
                    Application.dataPath,
                    "Generated/FoxRun",
                    FoxRunSchemaInfoWriter.SchemaInfoFileName);
                var previousSchemaInfo = ReadExistingText(schemaInfoPath);
                var manifest = FoxrunCodeGenerator.GenerateManifestFilesOnly();
                var aggregate = Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts(manifest);
                Debug.Log("[FoxRun] Refreshed canonical manifest, schema info, and SDK schema manifest before Play Mode: " +
                          manifest.GlobalManifestHash + " / " + aggregate.SdkSchemaManifestHash);

                if (!string.Equals(previousSchemaInfo, ReadExistingText(schemaInfoPath), StringComparison.Ordinal))
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    EditorApplication.isPlaying = false;
                    Debug.LogWarning(
                        "[FoxRun] Generated FoxRunSchemaInfo.g.cs changed before Play Mode. " +
                        "Unity must recompile it before runtime schema consumers can use the new manifest hash. " +
                        "Play Mode was canceled; press Play again after compilation finishes.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[FoxRun] Failed to refresh canonical manifest before Play Mode:\n" + ex);
            }
        }

        private static string ReadExistingText(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }
}
