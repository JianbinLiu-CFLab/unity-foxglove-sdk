// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase192 source section for the FoxRun native ROS2 subscription validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity.FoxgloveSDK.Tests
{
    internal static partial class FoxRunRos2NativeSubscriptionValidation
    {
        private static void VerifyInteropAcceptanceSurface()
        {
            var sample = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/Phase179FoxRunRos2NativeSubscribe.cs");
            var sampleReadme = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/README.md");
            var importedSampleReadme = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/FoxRun ROS2 Native Subscribe/README.md");
            var packageReadme = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/README.md");
            var packageJson = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/package.json");
            var acceptance = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase179FoxRunRos2NativeSubscribeAcceptance.cs");
            var playerBuilder = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase179AcceptancePlayerBuilder.cs");

            Check(sample.Contains("std_msgs.msg.String", StringComparison.Ordinal)
                  && sample.Contains("geometry_msgs.msg.Twist", StringComparison.Ordinal)
                  && sample.Contains("sensor_msgs.msg.Joy", StringComparison.Ordinal)
                  && sample.Contains("sensor_msgs.msg.Imu", StringComparison.Ordinal)
                  && CountOccurrences(
                      sample,
                      "FoxRunRos2TransportProvider.IdValue") == 4
                  && !sample.Contains("FoxRunEndpoint", StringComparison.Ordinal)
                  && sample.Contains("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && sample.Contains("CopyBounded", StringComparison.Ordinal)
                  && packageJson.Contains("FoxRun ROS2 Native Subscribe", StringComparison.Ordinal),
                "public sample exposes four explicit R2FU typed Subscribe contracts with bounded Inspector copies");

            Check(sampleReadme.Contains("phase179_humble_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("phase179_jazzy_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("phase179_lyrical_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("phase179_lyrical_zenoh_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("ros2-windows/ros2_<distro>", StringComparison.Ordinal)
                  && sampleReadme.Contains("PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING", StringComparison.Ordinal)
                  && sampleReadme.Contains("ZENOH_SESSION_CONFIG_URI", StringComparison.Ordinal)
                  && sampleReadme.Contains("first `Ok()`", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("phase179_humble_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("phase179_jazzy_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("phase179_lyrical_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("phase179_lyrical_zenoh_acceptance.py", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("ros2-windows/ros2_<distro>", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("ZENOH_SESSION_CONFIG_URI", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("first `Ok()`", StringComparison.Ordinal),
                "source and imported native-subscribe samples document the four named Linux interop profiles, pending half-evidence, repo-local Windows CLI, and Zenoh first-initialization boundary");

            Check(playerBuilder.Contains("PackageAssetPathToAbsolutePath", StringComparison.Ordinal)
                  && playerBuilder.Contains("ProjectAssetPathToAbsolutePath", StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "Path.Combine(RepositoryRoot(), assetPath.Replace('/', Path.DirectorySeparatorChar))",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "Path.Combine(ProjectRoot(), assetPath.Replace('/', Path.DirectorySeparatorChar))",
                      StringComparison.Ordinal),
                "sample metadata generator resolves package sources from the repository root and temporary Assets staging from the Unity project root");

            Check(playerBuilder.Contains("CleanupStaleSampleMetadataStaging", StringComparison.Ordinal)
                  && playerBuilder.Contains("IsRecognizedSampleMetadataStaging", StringComparison.Ordinal)
                  && playerBuilder.Contains("Unexpected Phase179 metadata staging content", StringComparison.Ordinal)
                  && OccursBefore(
                      playerBuilder,
                      "CleanupStaleSampleMetadataStaging();",
                      "Directory.CreateDirectory(stagingSampleAbsolutePath)"),
                "sample metadata generator recovers only its recognized stale staging root before retrying Unity generation");

            Check(playerBuilder.Contains("SampleMetadataStagingAssetPath", StringComparison.Ordinal)
                  && playerBuilder.Contains("FileUtil.CopyFileOrDirectory", StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "AssetDatabase.DeleteAsset(SampleMetadataStagingAssetPath)",
                      StringComparison.Ordinal)
                  && !playerBuilder.Contains("AssetDatabase.ImportAsset(assetPath", StringComparison.Ordinal),
                "sample metadata generator stages source files under Assets so Unity generates sidecars before package copy-back");

            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var phase179AssetPaths = new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe",
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/Phase179FoxRunRos2NativeSubscribe.cs",
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/README.md",
                "Unity2Foxglove/Assets/Scenes/Phase179FoxRunRos2NativeSubscribeAcceptance.unity",
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase179AcceptancePlayerBuilder.cs",
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase179FoxRunRos2NativeSubscribeAcceptance.cs",
            };
            var phase179MetadataPaths = phase179AssetPaths
                .Select(path => Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar)) + ".meta")
                .ToArray();
            var phase179ScriptMetadataPaths = new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/Phase179FoxRunRos2NativeSubscribe.cs.meta",
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase179AcceptancePlayerBuilder.cs.meta",
                "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/FoxRun ROS2 Native Subscribe/Phase179FoxRunRos2NativeSubscribe.cs.meta",
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase179FoxRunRos2NativeSubscribeAcceptance.cs.meta",
            }
                .Select(path => Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar)))
                .ToArray();
            var phase179Guids = phase179MetadataPaths
                .Where(File.Exists)
                .Select(ReadUnityAssetGuid)
                .ToArray();
            Check(phase179Guids.Length == phase179MetadataPaths.Length
                  && phase179Guids.All(guid => !string.IsNullOrEmpty(guid))
                  && phase179Guids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == phase179Guids.Length
                  && phase179Guids.All(guid => CountUnityAssetGuidOccurrences(repoRoot, guid) == 1)
                  && phase179ScriptMetadataPaths.All(HasCompleteUnityScriptMetadata),
                "Unity-generated Phase179 sample, scene, and manual-acceptance metadata use valid unique GUIDs and complete script importers");

            var refreshReadyMarker = RequiredRos2ForUnityMethod(
                acceptance,
                "private void EmitReadyMarkerWhenRuntimeIsObserved()",
                "Phase179FoxRunRos2NativeSubscribeAcceptance.cs");
            Check(refreshReadyMarker.Contains(
                      "snapshot.State != FoxRunRos2SubscriptionBindingState.Ready",
                      StringComparison.Ordinal)
                  && refreshReadyMarker.Contains(
                      "snapshot.State != FoxRunRos2SubscriptionBindingState.Receiving",
                      StringComparison.Ordinal)
                  && refreshReadyMarker.Contains("_readyMarkerEmitted = false;", StringComparison.Ordinal)
                  && refreshReadyMarker.Contains(
                      "Waiting for native ROS2 String subscription registration.",
                      StringComparison.Ordinal)
                  && refreshReadyMarker.Contains(
                      "Native ROS2 String subscription registered: ",
                      StringComparison.Ordinal),
                "acceptance READY evidence requires a live String binding and clears a stale ready state when that binding disappears");

            Check(acceptance.Contains("PHASE179_ROS2_INBOUND_READY", StringComparison.Ordinal)
                  && acceptance.Contains("PHASE179_ROS2_INBOUND_APPLIED", StringComparison.Ordinal)
                  && acceptance.Contains("PHASE179_ROS2_INBOUND_COMPLETE", StringComparison.Ordinal)
                  && acceptance.Contains("value=", StringComparison.Ordinal)
                  && acceptance.Contains("received=", StringComparison.Ordinal)
                  && acceptance.Contains("replaced=", StringComparison.Ordinal)
                  && acceptance.Contains("--phase179-player-auto-quit", StringComparison.Ordinal)
                  && acceptance.Contains("--phase179-token", StringComparison.Ordinal)
                  && acceptance.Contains("--phase179-player-burst-final-sequence", StringComparison.Ordinal)
                  && acceptance.Contains("Application.Quit(exitCode)", StringComparison.Ordinal)
                  && acceptance.Contains("TryParseBurstValue", StringComparison.Ordinal)
                  && acceptance.Contains("MaximumMarkerTextLength", StringComparison.Ordinal)
                  && acceptance.Contains("CopySafeText", StringComparison.Ordinal)
                  && acceptance.Contains("ContainsSensitiveMarkerText", StringComparison.Ordinal)
                  && acceptance.Contains("sequence == total - 1", StringComparison.Ordinal)
                  && acceptance.Contains("private string _activeCorrelationToken", StringComparison.Ordinal)
                  && acceptance.Contains("CaptureCorrelationToken(ExtractCorrelationToken(data))", StringComparison.Ordinal)
                   && acceptance.Contains("CaptureCorrelationToken(frameId)", StringComparison.Ordinal)
                   && acceptance.Contains("IsSafeCorrelationToken(_activeCorrelationToken)", StringComparison.Ordinal)
                   && acceptance.Contains("private string _readyRunToken = string.Empty;", StringComparison.Ordinal)
                   && acceptance.Contains("_readyRunToken = Guid.NewGuid().ToString(\"N\");", StringComparison.Ordinal)
                   && acceptance.Contains("PlayerOrFallbackToken(_readyRunToken)", StringComparison.Ordinal)
                   && acceptance.Contains("+ \"|\" + markerToken + \"|\" + valueJson", StringComparison.Ordinal)
                   && !acceptance.Contains("CreateSubscription", StringComparison.Ordinal)
                   && !acceptance.Contains("CreateNode", StringComparison.Ordinal),
                "acceptance receiver emits bounded redacted correlation markers, uses a new READY token for each activation, carries a current peer token to tokenless Twist evidence, preserves a terminal latest-wins burst value, and auto-quits only from its main-thread surface");

            Check(playerBuilder.Contains("NewSceneSetup.EmptyScene, NewSceneMode.Additive", StringComparison.Ordinal)
                  && playerBuilder.Contains("Refusing to overwrite", StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "SceneManager.MoveGameObjectToScene(managerObject, acceptanceScene)",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "SceneManager.MoveGameObjectToScene(receiverObject, acceptanceScene)",
                      StringComparison.Ordinal)
                  && !playerBuilder.Contains(
                      "SceneManager.SetActiveScene(acceptanceScene)",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains("_foxgloveOutputEnabled", StringComparison.Ordinal)
                  && playerBuilder.Contains("_enableFoxRunInbound", StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "manager.GetComponent<FoxRunRos2TransportProvider>()",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "manager.gameObject.AddComponent<FoxRunRos2TransportProvider>()",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "manager.ConfigureFoxRunTransports(",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "FoxRunRos2TransportProvider.IdValue",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "UnityEditor.PackageManager.PackageInfo.FindForPackageName",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains("RuntimeSupport", StringComparison.Ordinal)
                  && playerBuilder.Contains("SceneManager.GetSceneByPath", StringComparison.Ordinal)
                  && playerBuilder.Contains("FindComponentsInScene", StringComparison.Ordinal)
                  && playerBuilder.Contains("AssetDatabase.AssetPathToGUID", StringComparison.Ordinal)
                  && playerBuilder.Contains("GenerateSampleMetadata", StringComparison.Ordinal)
                  && playerBuilder.Contains("SampleMetadataAssetPaths", StringComparison.Ordinal)
                  && playerBuilder.Contains("ValidateSampleMetadata", StringComparison.Ordinal)
                  && playerBuilder.Contains("build", StringComparison.Ordinal)
                  && playerBuilder.Contains("Phase179FoxRunRos2NativeSubscribe.exe", StringComparison.Ordinal),
                "tracked scene builder fails closed, obtains Unity-generated sample metadata, verifies the actual acceptance scene, records one resolved runtime, and keeps Player output under repository build paths");

            Check(sampleReadme.Contains("FOXR2F007", StringComparison.Ordinal)
                  && sampleReadme.Contains("Foxglove Desktop is unrelated", StringComparison.Ordinal)
                  && sampleReadme.Contains(
                      "Generated custom DTO native Publish, Subscribe, and diagnostic full duplex",
                      StringComparison.Ordinal)
                  && sampleReadme.Contains("Subscribe-only `FoxRunStream<T>`", StringComparison.Ordinal)
                  && sampleReadme.Contains("4 MiB", StringComparison.Ordinal)
                  && sampleReadme.Contains("1 KiB", StringComparison.Ordinal)
                  && sampleReadme.Contains("CopyFailed", StringComparison.Ordinal)
                  && packageReadme.Contains("WindowsStandalone64", StringComparison.Ordinal)
                  && packageReadme.Contains("ROS domain IDs are discovery isolation, not authentication", StringComparison.Ordinal)
                  && packageReadme.Contains("FOXR2F007", StringComparison.Ordinal)
                  && packageReadme.Contains("4 MiB", StringComparison.Ordinal)
                  && packageReadme.Contains("CopyFailed", StringComparison.Ordinal),
                "public native-subscribe documentation states assembly, transport, copy-budget, security, Player, custom-message, and stream boundaries");
        }

    }
}
