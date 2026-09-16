// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity2Foxglove
{
    /// <summary>Bounded batch diagnostic for the maintained Phase189 acceptance scene.</summary>
    public static class Phase189ComponentMessagePackBatchProbe
    {
        public static void RunFromCommandLine() => Run();

        [MenuItem("Foxglove/Manual Acceptance/Phase189/Run Batch Diagnostic")]
        public static void Run()
        {
            Phase189ComponentMessagePackAcceptanceBuilder.BuildAndValidate();
            var scene = EditorSceneManager.OpenScene(
                Phase189ComponentMessagePackAcceptanceBuilder.AcceptanceSceneAssetPath,
                OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var controllerCount = roots.SelectMany(x => x.GetComponentsInChildren<Unity2Foxglove.ManualAcceptance.Phase189ComponentMessagePackAcceptance>(true)).Count();
            var managerCount = roots.SelectMany(x => x.GetComponentsInChildren<Unity.FoxgloveSDK.Components.FoxgloveManager>(true)).Count();
            if (controllerCount != 1 || managerCount != 1)
                throw new InvalidDataException("Phase189 batch diagnostic requires exactly one maintained controller and manager.");
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? throw new DirectoryNotFoundException("Unity project root missing.");
            var repoRoot = Directory.GetParent(projectRoot)?.FullName
                ?? throw new DirectoryNotFoundException("Repository root missing above Unity project.");
            var report = Path.Combine(repoRoot, "build", "phase189", "manual", "batch-diagnostic.json");
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report, "{\"verdict\":\"PASS\",\"scene\":\"" + Phase189ComponentMessagePackAcceptanceBuilder.AcceptanceSceneAssetPath + "\",\"controllerCount\":1,\"managerCount\":1,\"cleanup\":true}\n");
            Debug.Log("PHASE189_BATCH_DIAGNOSTIC_PASS report=" + report);
        }
    }
}
#endif
