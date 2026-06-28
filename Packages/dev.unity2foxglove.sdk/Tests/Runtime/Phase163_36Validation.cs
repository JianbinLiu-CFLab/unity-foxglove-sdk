// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-36 Unity demo scene and project-settings review closure.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_36Validation
    {
        private const string ReplayAdapterGuid = "745c54bef9ea4071a9b783828520c63d";
        private const string PlaceholderReplayAdapterGuid = "b3d0f1e2a4b5c6d7e8f9a0b1c2d3e4f5";

        public static void Validate()
        {
            var repoRoot = Phase16Validation.FindRepoRoot()
                           ?? throw new DirectoryNotFoundException("Could not locate repository root.");

            VerifyReplayAdapterGuid(repoRoot);
            VerifySampleSceneDefaults(repoRoot);
            VerifyAcceptanceSceneRunsSmokes(repoRoot);
            VerifyProjectIdentityAndDocs(repoRoot);
            VerifyValidationWiring(repoRoot);

            Console.WriteLine("Phase 163-36: Unity scene and project settings checks passed.");
        }

        private static void VerifyReplayAdapterGuid(string repoRoot)
        {
            var meta = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs.meta");
            Check(meta.Contains("guid: " + ReplayAdapterGuid, StringComparison.Ordinal)
                  && !meta.Contains(PlaceholderReplayAdapterGuid, StringComparison.Ordinal),
                "163-36A-1: replay adapter script meta uses an opaque random GUID");

            foreach (var scenePath in new[]
                     {
                         "Unity2Foxglove/Assets/Scenes/SampleScene.unity",
                         "Unity2Foxglove/Assets/Scenes/Phase138_Foxglove_MCAP_Smoke.unity"
                     })
            {
                var scene = Read(repoRoot, scenePath);
                Check(scene.Contains("guid: " + ReplayAdapterGuid, StringComparison.Ordinal)
                      && !scene.Contains(PlaceholderReplayAdapterGuid, StringComparison.Ordinal),
                    "163-36A-2: " + scenePath + " references the current replay adapter GUID");
            }
        }

        private static void VerifySampleSceneDefaults(string repoRoot)
        {
            var sampleScene = Read(repoRoot, "Unity2Foxglove/Assets/Scenes/SampleScene.unity");

            Check(!sampleScene.Contains("Randomized probe values.", StringComparison.Ordinal)
                  && !sampleScene.Contains("random 334", StringComparison.Ordinal)
                  && sampleScene.Contains("_lastStatus: Reset probe values.", StringComparison.Ordinal)
                  && sampleScene.Contains("textValue: hello 115F", StringComparison.Ordinal),
                "163-36B-1: SampleScene manual probe stores deterministic reset defaults");

            Check(sampleScene.Contains("m_Sun: {fileID: 410087040}", StringComparison.Ordinal),
                "163-36B-2: SampleScene links its Directional Light as sun source");
            Check(Read(repoRoot, "Unity2Foxglove/Assets/Scenes/RViz2_Smokes.unity")
                    .Contains("m_Sun: {fileID: 73391073}", StringComparison.Ordinal),
                "163-36B-3: RViz2 smoke scene links its Directional Light as sun source");
            Check(Read(repoRoot, "Unity2Foxglove/Assets/Scenes/Phase138_Foxglove_MCAP_Smoke.unity")
                    .Contains("m_Sun: {fileID: 176082739}", StringComparison.Ordinal),
                "163-36B-4: Phase138 smoke scene links its Directional Light as sun source");

            foreach (var required in new[]
                     {
                         "Unity.FoxgloveSDK.Proto::Unity.FoxgloveSDK.Components.FoxgloveCameraPublisher\r\n  _manager: {fileID: 346350281}\r\n  _topic: /unity/camera",
                         "Unity.FoxgloveSDK.Proto::Unity.FoxgloveSDK.Components.FoxglovePointCloudPublisher\r\n  _manager: {fileID: 346350281}\r\n  _topic: /unity/point_cloud",
                         "Unity.FoxgloveSDK::Unity.FoxgloveSDK.Components.FoxgloveSceneCubePublisher\r\n  _manager: {fileID: 346350281}\r\n  _topic: /scene",
                         "Unity.FoxgloveSDK::Unity.FoxgloveSDK.Components.FoxgloveTransformPublisher\r\n  _manager: {fileID: 346350281}\r\n  _topic: /tf",
                         "Unity.FoxgloveSDK::Unity.FoxgloveSDK.Components.FoxgloveReplayObjectAdapter\r\n  _manager: {fileID: 346350281}"
                     })
            {
                Check(ContainsUnityBlock(sampleScene, required),
                    "163-36B-5: SampleScene has explicit manager/topic block for " + required.Split('\r')[0]);
            }
        }

        private static void VerifyAcceptanceSceneRunsSmokes(string repoRoot)
        {
            var scene = Read(repoRoot, "Unity2Foxglove/Assets/Scenes/Phase110Acceptance.unity");
            Check(IsEnabledMonoBehaviour(scene, "Assembly-CSharp::Phase106Ros2ForUnityAcceptance")
                  && IsEnabledMonoBehaviour(scene, "Assembly-CSharp::Phase110Ros2ForUnityStringSmoke"),
                "163-36C-1: Phase110 acceptance scene enables its short smoke components");
        }

        private static void VerifyProjectIdentityAndDocs(string repoRoot)
        {
            var project = Read(repoRoot, "Unity2Foxglove/ProjectSettings/ProjectSettings.asset");
            Check(project.Contains("companyName: CFLab", StringComparison.Ordinal)
                  && project.Contains("productName: Unity2Foxglove", StringComparison.Ordinal)
                  && project.Contains("Standalone: dev.unity2foxglove.demo", StringComparison.Ordinal)
                  && !project.Contains("Untiy2Foxglove", StringComparison.Ordinal)
                  && !project.Contains("DefaultCompany", StringComparison.Ordinal)
                  && !project.Contains("com.Unity-Technologies.com.unity.template", StringComparison.Ordinal),
                "163-36D-1: demo project identity no longer uses template or misspelled values");

            var editorSettings = Read(repoRoot, "Unity2Foxglove/ProjectSettings/EditorSettings.asset");
            Check(editorSettings.Contains("m_EnterPlayModeOptionsEnabled: 1", StringComparison.Ordinal)
                  && editorSettings.Contains("m_EnterPlayModeOptions: 0", StringComparison.Ordinal),
                "163-36D-2: fast play options are explicit None, so domain and scene reload remain enabled");

            var readme = Read(repoRoot, "Unity2Foxglove/README.md");
            Check(readme.Contains("Lyrical Win64 runtime package", StringComparison.Ordinal)
                  && readme.Contains("To use Humble or Jazzy instead", StringComparison.Ordinal)
                  && readme.Contains("Only `Assets/Scenes/SampleScene.unity` is included", StringComparison.Ordinal)
                  && readme.Contains("Editor/manual smoke scenes", StringComparison.Ordinal),
                "163-36D-3: demo README documents ROS2 runtime default and build-scene boundary");
        }

        private static void VerifyValidationWiring(string repoRoot)
        {
            var phase17 = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase17Validation.cs");
            Check(phase17.Contains("Unity2Foxglove/ProjectSettings", StringComparison.Ordinal),
                "163-36E-1: Phase17 absolute-path scan covers demo ProjectSettings");

            var project = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase163_36Validation.cs", StringComparison.Ordinal),
                "163-36E-2: runtime test project compiles Phase163_36Validation");
            Check(registry.Contains("Ci(\"--phase163-36\", \"Phase 163-36\", Phase163_36Validation.Validate", StringComparison.Ordinal),
                "163-36E-3: validation registry exposes --phase163-36");
        }

        private static bool ContainsUnityBlock(string text, string block)
            => text.Contains(block, StringComparison.Ordinal)
               || text.Contains(block.Replace("\r\n", "\n"), StringComparison.Ordinal);

        private static bool IsEnabledMonoBehaviour(string scene, string editorClassIdentifier)
        {
            var classIndex = scene.IndexOf("m_EditorClassIdentifier: " + editorClassIdentifier, StringComparison.Ordinal);
            if (classIndex < 0)
                return false;

            var blockStart = scene.LastIndexOf("--- !u!114", classIndex, StringComparison.Ordinal);
            if (blockStart < 0)
                return false;

            var block = scene.Substring(blockStart, classIndex - blockStart);
            return block.Contains("m_Enabled: 1", StringComparison.Ordinal);
        }

        private static string Read(string repoRoot, string relativePath)
            => File.ReadAllText(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception("[FAIL] " + description);

            Console.WriteLine("[PASS] " + description);
        }
    }
}
