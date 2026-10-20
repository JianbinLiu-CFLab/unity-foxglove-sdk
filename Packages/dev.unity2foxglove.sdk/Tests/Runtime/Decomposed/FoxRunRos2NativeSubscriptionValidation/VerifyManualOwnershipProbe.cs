// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Guards the FoxRun native subscription policy and dependency boundary.

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
        private static void VerifyManualOwnershipProbe()
        {
            const string probePath =
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase179Ros2OwnershipProbe.cs";
            var probe = PhaseValidationSourceHelpers.ReadRequiredRepoText(probePath);
            var probeMeta = PhaseValidationSourceHelpers.ReadRequiredRepoText(probePath + ".meta");
            var diagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionDiagnostics.cs");
            var binding = PhaseValidationSourceHelpers.ReadFoxRunRos2SubscriptionBindingSources();
            var slot = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2OwnedLatestSlot.cs");
            var armBurst = RequiredRos2ForUnityMethod(
                probe,
                "public void ArmBurstAttempt()",
                "Phase179Ros2OwnershipProbe.cs");
            var emitBurst = RequiredRos2ForUnityMethod(
                probe,
                "private void TryEmitBurstLatestMarker",
                "Phase179Ros2OwnershipProbe.cs");
            var observe = RequiredRos2ForUnityMethod(
                probe,
                "private void ObserveGeneratedOwnedCopy()",
                "Phase179Ros2OwnershipProbe.cs");
            var attachSessionObserver = RequiredRos2ForUnityMethod(
                probe,
                "private void AttachManagerSessionPolicyObserver()",
                "Phase179Ros2OwnershipProbe.cs");
            var detachSessionObserver = RequiredRos2ForUnityMethod(
                probe,
                "private void DetachManagerSessionPolicyObserver()",
                "Phase179Ros2OwnershipProbe.cs");
            var captureSessionPolicy = RequiredRos2ForUnityMethod(
                probe,
                "private void CaptureManagerSessionPolicy(",
                "Phase179Ros2OwnershipProbe.cs");
            var onEnable = RequiredRos2ForUnityMethod(
                probe,
                "private void OnEnable()",
                "Phase179Ros2OwnershipProbe.cs");
            var onDisable = RequiredRos2ForUnityMethod(
                probe,
                "private void OnDisable()",
                "Phase179Ros2OwnershipProbe.cs");
            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var probeMetaAbsolute = Path.Combine(
                repoRoot,
                (probePath + ".meta").Replace('/', Path.DirectorySeparatorChar));
            var probeGuid = ReadUnityAssetGuid(probeMetaAbsolute);

            Check(probe.Contains("/foxrun/phase179/string", StringComparison.Ordinal)
                  && probe.Contains("Mode = FoxRunFlow.Subscribe", StringComparison.Ordinal)
                  && probe.Contains(
                      "SubscribeTransportId =",
                      StringComparison.Ordinal)
                  && probe.Contains(
                      "FoxRunRos2TransportProvider.IdValue",
                      StringComparison.Ordinal)
                  && probe.Contains("private std_msgs.msg.String inputString;", StringComparison.Ordinal)
                  && probe.Contains(
                      "BorrowedLifetimeEvidence => borrowedLifetimeEvidence",
                      StringComparison.Ordinal),
                "manual ownership probe declares an explicit R2FU String subscription contract");

            Check(probe.Contains("[Header(\"Captured Subscription Session Policy\")]", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private bool capturedSessionEnabled;", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private ulong capturedSessionGeneration;", StringComparison.Ordinal)
                  && probe.Contains(
                      "[SerializeField] private string capturedDefaultSubscribeTransportId",
                      StringComparison.Ordinal)
                  && probe.Contains(
                      "[SerializeField] private FoxRunEncoding capturedFoxgloveEncoding",
                      StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private FoxRunDeliveryReliability capturedDeliveryReliability", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private FoxRunDeliveryDurability capturedDeliveryDurability", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private FoxRunDeliveryHistory capturedDeliveryHistory", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private int capturedDeliveryDepth", StringComparison.Ordinal)
                  && probe.Contains("FoxRunSubscriptionSessionChanged +=", StringComparison.Ordinal)
                  && probe.Contains("FoxRunSubscriptionSessionChanged -=", StringComparison.Ordinal)
                  && OccursBefore(
                      onEnable,
                      "AttachManagerSessionPolicyObserver();",
                      "#if UNITY2FOXGLOVE_ROS2_FOR_UNITY")
                  && onDisable.Contains("DetachManagerSessionPolicyObserver();", StringComparison.Ordinal)
                  && attachSessionObserver.Contains(
                      "ActiveFoxRunSubscriptionSessionPolicy",
                      StringComparison.Ordinal)
                  && !attachSessionObserver.Contains("Find", StringComparison.Ordinal)
                  && detachSessionObserver.Contains(
                      "FoxRunSubscriptionSessionChanged -=",
                      StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.SubscriptionsEnabled", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.SessionGeneration", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.DefaultProvider.Value", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.WebSocketEncoding", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.DefaultDeliveryPolicy.Reliability", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.DefaultDeliveryPolicy.Durability", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.DefaultDeliveryPolicy.History", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.DefaultDeliveryPolicy.Depth", StringComparison.Ordinal)
                  && !captureSessionPolicy.Contains("Create", StringComparison.Ordinal)
                  && !captureSessionPolicy.Contains("Selector", StringComparison.Ordinal),
                "manual ownership probe captures the immutable neutral Manager session through symmetric event observation only");

            Check(probe.Contains("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && probe.Contains("ROS2 native subscription support is unavailable", StringComparison.Ordinal)
                  && probe.Contains("nextFrameReadableCount", StringComparison.Ordinal)
                  && probe.Contains("bindingReceivedCount", StringComparison.Ordinal)
                  && probe.Contains("bindingReplacedCount", StringComparison.Ordinal)
                  && probe.Contains("bindingAppliedCount", StringComparison.Ordinal)
                  && probe.Contains("bindingPendingCount", StringComparison.Ordinal)
                  && probe.Contains("disableNoApplyPassCount", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_APPLIED", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_NEXT_FRAME_READABLE", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_BURST_ARMED", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_BURST_LATEST", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_DISABLE_ARMED", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_DISABLE_CLEAN", StringComparison.Ordinal),
                "manual ownership probe exposes bounded copied-value, latest-wins, and disable-clean evidence");

            Check(diagnostics.Contains(
                      "public readonly struct FoxRunRos2SubscriptionAcceptanceSnapshot",
                      StringComparison.Ordinal)
                  && diagnostics.Contains(
                      "public readonly struct FoxRunRos2AcceptanceAttemptSnapshot",
                      StringComparison.Ordinal)
                  && diagnostics.Contains(
                      "public static class FoxRunRos2SubscriptionAcceptanceDiagnostics",
                      StringComparison.Ordinal)
                  && diagnostics.Contains("public static FoxRunRos2AcceptanceArmStatus ArmAttempt", StringComparison.Ordinal)
                  && diagnostics.Contains("public static bool TryGetAttempt", StringComparison.Ordinal)
                  && diagnostics.Contains("public static bool EndAttempt", StringComparison.Ordinal)
                  && diagnostics.Contains("public static bool TryCompleteAcceptanceAttempt", StringComparison.Ordinal)
                  && diagnostics.Contains("IsSingleApplyLatestWinsComplete", StringComparison.Ordinal)
                  && diagnostics.Contains("Received == Replaced + Applied", StringComparison.Ordinal)
                  && probe.Contains(
                      "FoxRunRos2SubscriptionAcceptanceDiagnostics.TryGet",
                      StringComparison.Ordinal)
                  && probe.Contains("bindingPendingCount > 0", StringComparison.Ordinal)
                  && probe.Contains("_disablePendingArmed", StringComparison.Ordinal),
                "managed diagnostics expose cumulative cleanup evidence and an explicit exact acceptance attempt");

            Check(armBurst.Contains(
                      "FoxRunRos2SubscriptionAcceptanceDiagnostics.ArmAttempt",
                      StringComparison.Ordinal)
                  && armBurst.Contains("FoxRunRos2AcceptanceArmStatus.Armed", StringComparison.Ordinal)
                  && armBurst.Contains("CopyAttemptSnapshot", StringComparison.Ordinal)
                  && emitBurst.Contains("!burstArmed", StringComparison.Ordinal)
                  && emitBurst.Contains("SessionMatchesArmedToken", StringComparison.Ordinal)
                  && probe.Contains("TryCompleteArmedBurstAttempt", StringComparison.Ordinal)
                  && probe.Contains("TryCompleteAcceptanceAttempt", StringComparison.Ordinal)
                  && probe.Contains("_latestAttemptSnapshot.Epoch != completedEpoch", StringComparison.Ordinal)
                  && probe.Contains("IsSingleApplyLatestWinsComplete", StringComparison.Ordinal)
                  && !emitBurst.Contains("TryGetAttempt", StringComparison.Ordinal)
                  && !emitBurst.Contains("EndAttempt", StringComparison.Ordinal)
                  && probe.Contains("finally", StringComparison.Ordinal)
                  && probe.Contains("FoxRunRos2SubscriptionAcceptanceDiagnostics.EndAttempt", StringComparison.Ordinal)
                  && !probe.Contains("Burst Baseline", StringComparison.Ordinal)
                  && !probe.Contains("_burstBaseline", StringComparison.Ordinal)
                  && !probe.Contains("HasPositiveBurstProgressFrom", StringComparison.Ordinal)
                  && !probe.Contains("receivedDelta=", StringComparison.Ordinal)
                  && !probe.Contains("inferredReplaced", StringComparison.OrdinalIgnoreCase),
                "manual burst acceptance arms an idle epoch and requires exact one-apply latest-wins accounting for the matching token");

            Check(binding.Contains("private long _acceptanceAdmission", StringComparison.Ordinal)
                  && binding.Contains("_slot.PendingCount != 0", StringComparison.Ordinal)
                  && binding.Contains("_acceptanceCallbacksInFlight", StringComparison.Ordinal)
                  && binding.Contains("AcceptanceCompleting", StringComparison.Ordinal)
                  && binding.Contains("AcceptanceCompleted", StringComparison.Ordinal)
                  && binding.Contains("TryCompleteAcceptanceAttempt", StringComparison.Ordinal)
                  && binding.Contains("var acceptanceEpoch = Volatile.Read(ref _acceptanceAdmission)", StringComparison.Ordinal)
                  && binding.Contains("out var replacedPending", StringComparison.Ordinal)
                  && binding.Contains("Interlocked.Increment(ref _acceptanceReplaced)", StringComparison.Ordinal)
                  && slot.Contains("out bool replacedPending", StringComparison.Ordinal)
                  && slot.Contains("replacedPending = true", StringComparison.Ordinal),
                "acceptance admission excludes pending or in-flight history and counts actual pending replacement from callback entry");

            Check(probe.Contains("private int _lastObservedLength", StringComparison.Ordinal)
                  && probe.Contains("private ulong _lastObservedFingerprint", StringComparison.Ordinal)
                  && probe.Contains("private int _pendingNextFrameLength", StringComparison.Ordinal)
                  && probe.Contains("private ulong _pendingNextFrameFingerprint", StringComparison.Ordinal)
                  && !probe.Contains("_lastObservedValue", StringComparison.Ordinal)
                  && !probe.Contains("_pendingNextFrameValue", StringComparison.Ordinal)
                  && OccursBefore(
                      observe,
                      "if (_burstCompletionPending)",
                      "currentFingerprint == _lastObservedFingerprint")
                  && OccursBefore(observe, "currentFingerprint == _lastObservedFingerprint", "TryParseBurstValue(")
                  && probe.Contains("private bool CanEmitMarker", StringComparison.Ordinal)
                  && probe.Contains("!_evidenceComplete", StringComparison.Ordinal)
                  && CountOccurrences(probe, "if (CanEmitMarker)") >= 6,
                "manual probe retains only bounded Inspector strings, fingerprints unchanged values, and stops constructing capped evidence markers");

            Check(!string.IsNullOrEmpty(probeGuid)
                  && probeMeta.Contains("MonoImporter:", StringComparison.Ordinal)
                  && CountUnityAssetGuidOccurrences(repoRoot, probeGuid) == 1,
                "manual ownership probe metadata keeps one unique 32-hex Unity GUID and a complete MonoImporter block");

            Check(!probe.Contains("CreateSubscription", StringComparison.Ordinal)
                  && !probe.Contains("CreateNode", StringComparison.Ordinal)
                  && !probe.Contains("ROS2Node", StringComparison.Ordinal)
                  && !probe.Contains("ISubscription", StringComparison.Ordinal)
                  && !probe.Contains("OnRos2", StringComparison.Ordinal)
                  && !probe.Contains("rawCallback", StringComparison.OrdinalIgnoreCase)
                  && !probe.Contains("Debug.Log(inbound", StringComparison.Ordinal)
                  && probe.Contains("MaximumMarkersPerEnable", StringComparison.Ordinal),
                "manual ownership probe uses only the generated host and bounds diagnostics without retaining callback objects");
        }

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
