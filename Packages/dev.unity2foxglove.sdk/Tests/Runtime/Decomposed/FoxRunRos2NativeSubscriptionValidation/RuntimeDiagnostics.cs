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
        private static void VerifyRuntimeDiagnosticsSurface()
        {
            var diagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionDiagnostics.cs");
            var binding = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionBinding.cs");
            var backendBoundary = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/IFoxRunRos2NativeBackend.cs");
            var hub = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionHub.cs");
            var optionalInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRunRos2SubscriptionDiagnosticsInspector.cs");
            var optionalInspectorAsmdef = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/Unity2Foxglove.Ros2ForUnity.Native.Editor.asmdef");
            var providerDrawer = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRunR2fuProviderDrawer.cs");
            var providerDrawerRegistry = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/Manager/FoxRunTransportProviderDrawerRegistry.cs");
            var publishInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/Manager/FoxgloveManagerEditor.PublishData.cs");
            var subscribeInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/Manager/FoxgloveManagerEditor.SubscribeData.cs");
            var runtimeTopicsInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/Manager/FoxgloveManagerEditor.FoxServices.cs");
            var catalog = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/FoxRun/FoxRunSubscriptionCatalog.cs");
            var runtimeTopicSummary = PhaseValidationSourceHelpers.SourceMethod(
                runtimeTopicsInspector,
                "private static void DrawFoxRunTopicSummary(FoxgloveManager manager)");

            Check(diagnostics.Contains(
                      "public readonly struct FoxRunRos2SubscriptionDiagnosticSnapshot",
                      StringComparison.Ordinal)
                  && diagnostics.Contains(
                      "public static class FoxRunRos2SubscriptionRuntimeDiagnostics",
                      StringComparison.Ordinal)
                  && diagnostics.Contains("GetSnapshots", StringComparison.Ordinal)
                   && diagnostics.Contains("LastErrorMessage", StringComparison.Ordinal)
                   && diagnostics.Contains(
                       "LastErrorMessage = FoxRunRos2PublicDiagnostic.Describe(binding.Error);",
                       StringComparison.Ordinal)
                   && binding.Contains(
                       "Diagnostic = FoxRunRos2PublicDiagnostic.Describe(error);",
                       StringComparison.Ordinal)
                   && backendBoundary.Contains(
                       "internal static class FoxRunRos2PublicDiagnostic",
                       StringComparison.Ordinal)
                   && !hub.Contains("exception.Message", StringComparison.Ordinal)
                   && diagnostics.Contains("new LoggedDiagnostic(snapshot.ContractId, snapshot.Error)", StringComparison.Ordinal)
                   && diagnostics.Contains("_lastLogged.Add(signature)", StringComparison.Ordinal)
                   && diagnostics.Contains("ReconcileLoggedDiagnosticsForContract(snapshot.ContractId)", StringComparison.Ordinal)
                   && diagnostics.Contains("HasSnapshotForDiagnostic", StringComparison.Ordinal),
                "native diagnostics publish stable non-secret error descriptions and debounce warnings by contract plus error code");

            Check(binding.Contains("Stopwatch.GetTimestamp", StringComparison.Ordinal)
                  && binding.Contains("_lastReceiveStopwatchTimestamp", StringComparison.Ordinal)
                  && binding.Contains("_lastApplyStopwatchTimestamp", StringComparison.Ordinal)
                  && binding.Contains("Interlocked.Exchange", StringComparison.Ordinal)
                  && hub.Contains("Environment.GetEnvironmentVariable(\"ROS_DISTRO\")", StringComparison.Ordinal)
                  && hub.Contains("Environment.GetEnvironmentVariable(\"RMW_IMPLEMENTATION\")", StringComparison.Ordinal)
                  && (hub.Contains("rmw_fastrtps_cpp", StringComparison.Ordinal)
                      || diagnostics.Contains("rmw_fastrtps_cpp", StringComparison.Ordinal))
                  && (hub.Contains("rmw_zenoh_cpp", StringComparison.Ordinal)
                      || diagnostics.Contains("rmw_zenoh_cpp", StringComparison.Ordinal))
                  && hub.Contains("GetDiagnosticSnapshots", StringComparison.Ordinal)
                  && !binding.Contains("UnityEngine.", StringComparison.Ordinal),
                "native callback diagnostics use managed Stopwatch timestamps and capture active ROS runtime identity only after readiness");

            Check(optionalInspector.Contains(
                      "public static void DrawFoxRunNativeSubscriptionDiagnostics()",
                      StringComparison.Ordinal)
                  && optionalInspector.Contains("FoxRunRos2SubscriptionRuntimeDiagnostics.GetSnapshots", StringComparison.Ordinal)
                  && optionalInspector.Contains("CopyableField", StringComparison.Ordinal)
                  && (hub.Contains("ROS2 Native / FastDDS (DDS)", StringComparison.Ordinal)
                      || diagnostics.Contains("ROS2 Native / FastDDS (DDS)", StringComparison.Ordinal))
                  && (hub.Contains("ROS2 Native / Zenoh", StringComparison.Ordinal)
                      || diagnostics.Contains("ROS2 Native / Zenoh", StringComparison.Ordinal))
                  && providerDrawer.Contains(
                      "IFoxRunTransportProviderDrawer",
                      StringComparison.Ordinal)
                   && providerDrawer.Contains(
                       "FoxRunRos2SubscriptionDiagnosticsInspector",
                       StringComparison.Ordinal)
                   && providerDrawer.Contains(
                       ".DrawFoxRunNativeSubscriptionDiagnostics();",
                       StringComparison.Ordinal)
                   && providerDrawerRegistry.Contains(
                       "IReadOnlyList<IFoxRunTransportProviderDrawer> Capture()",
                       StringComparison.Ordinal)
                   && publishInspector.Contains(
                       "FoxRunTransportProviderDrawerRegistry.Capture()",
                       StringComparison.Ordinal)
                   && optionalInspectorAsmdef.Contains(
                       "Unity2Foxglove.Ros2ForUnity.Native",
                       StringComparison.Ordinal)
                   && optionalInspectorAsmdef.Contains(
                       "Unity.FoxgloveSDK",
                       StringComparison.Ordinal)
                   && optionalInspectorAsmdef.Contains(
                       "Unity2Foxglove.Ros2ForUnity.Editor",
                       StringComparison.Ordinal)
                   && optionalInspectorAsmdef.Contains(
                       "UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                       StringComparison.Ordinal)
                   && !subscribeInspector.Contains(
                       "R2fu",
                       StringComparison.OrdinalIgnoreCase),
                "the R2FU Provider drawer directly owns native diagnostics while the core Inspector remains Provider-neutral");

            Check(runtimeTopicsInspector.Contains("FoxRun Runtime Topics", StringComparison.Ordinal)
                  && runtimeTopicsInspector.Contains("Publish Topics", StringComparison.Ordinal)
                  && runtimeTopicsInspector.Contains("Subscribe Topics", StringComparison.Ordinal)
                  && !runtimeTopicsInspector.Contains("ROS2", StringComparison.OrdinalIgnoreCase)
                  && !runtimeTopicsInspector.Contains("FoxRunEndpointResolver", StringComparison.Ordinal)
                  && runtimeTopicSummary.Contains(
                      "No generated FoxRun topic metadata is registered in this domain.",
                      StringComparison.Ordinal)
                  && catalog.Contains("IsWebSocketEncoding", StringComparison.Ordinal)
                  && catalog.Contains("SubscribeTransportId", StringComparison.Ordinal)
                  && catalog.Contains("selectedIds.Length != 1", StringComparison.Ordinal)
                  && !catalog.Contains("cdr", StringComparison.OrdinalIgnoreCase),
                "core Runtime Topics and subscription catalog remain Provider-neutral while R2FU status stays in its drawer");
        }

        private static void VerifyManualOwnershipProbe()
        {
            const string probePath =
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase179Ros2OwnershipProbe.cs";
            var probe = PhaseValidationSourceHelpers.ReadRequiredRepoText(probePath);
            var probeMeta = PhaseValidationSourceHelpers.ReadRequiredRepoText(probePath + ".meta");
            var diagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionDiagnostics.cs");
            var binding = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionBinding.cs");
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

    }
}
