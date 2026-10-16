// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural guard for the Phase181 custom ROS2 DTO interface boundary.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity.FoxgloveSDK.Tests
{
    internal static partial class FoxRunCustomRos2InterfaceValidation
    {
        private static void VerifyEditModeCustomContractSnapshot(
            string foxRunCodeGenerator,
            string managerR2fuRuntimeInspector)
        {
            var snapshotStart = foxRunCodeGenerator.IndexOf(
                "CollectReflectionGenerationModelForTransportProviders()",
                StringComparison.Ordinal);
            var snapshotEnd = snapshotStart >= 0
                ? foxRunCodeGenerator.IndexOf(
                    "public static FoxRunSchemaInfoVerification VerifyGeneratedSchemaInfoFiles()",
                    snapshotStart,
                    StringComparison.Ordinal)
                : -1;
            var snapshotBody = snapshotStart >= 0 && snapshotEnd > snapshotStart
                ? foxRunCodeGenerator.Substring(snapshotStart, snapshotEnd - snapshotStart)
                : string.Empty;
            Check(snapshotBody.Contains("ScanFoxRunMembers(", StringComparison.Ordinal)
                  && snapshotBody.Contains("ignoreReflectionTypeLoadExceptions: true", StringComparison.Ordinal)
                  && snapshotBody.Contains("LowerReflectionMembers(", StringComparison.Ordinal)
                  && snapshotBody.Contains("ValidateGenerationModel(", StringComparison.Ordinal)
                  && !snapshotBody.Contains("WriteManifestFiles", StringComparison.Ordinal)
                  && !snapshotBody.Contains("WriteSchemaInfoFiles", StringComparison.Ordinal)
                  && !snapshotBody.Contains("WriteDescriptorFile", StringComparison.Ordinal),
                "181F-17: core exposes a validated neutral reflection snapshot without rewriting generated evidence");

            Check(snapshotBody.Contains("logWarnings: false", StringComparison.Ordinal)
                  && foxRunCodeGenerator.Contains(
                      "private static void ValidateGenerationModel(FoxRunGenerationModel model, bool logWarnings = true)",
                      StringComparison.Ordinal),
                "181F-19: Provider snapshot validation preserves blocking diagnostics without replaying advisory warnings into the Unity Console");

            Check(managerR2fuRuntimeInspector.Contains(
                      "CollectCustomTypesupportContracts()",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "CollectReflectionGenerationModelForTransportProviders()",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      ".Select(FoxRunR2fuTopicMember.Create)",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "FoxRunRos2ContractKind.CustomDto",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "BuildEnvelopeMessageName(",
                      StringComparison.Ordinal)
                  && !managerR2fuRuntimeInspector.Contains(
                      "GetGeneratedCustomNativeContracts()",
                      StringComparison.Ordinal),
                "181F-18: R2FU preflight projects current custom contracts from the neutral snapshot instead of stale generated-schema state");
        }

        private static void VerifyAcceptanceSurface(
            string sample,
            string importedSample,
            string sampleReadme,
            string r2fuPackageJson,
            string acceptanceComponent,
            string playerBuilder,
            string batchProbe,
            string typesupportPluginImporterBuilder)
        {
            Check(sample.Contains("FoxRunFlow.Publish", StringComparison.Ordinal)
                  && sample.Contains("FoxRunFlow.Subscribe", StringComparison.Ordinal)
                  && sample.Contains("FoxRunFlow.PublishAndSubscribe", StringComparison.Ordinal)
                  && sample.Contains("PublishTransportIds = new[]", StringComparison.Ordinal)
                  && sample.Contains("SubscribeTransportId =", StringComparison.Ordinal)
                  && sample.Contains("FoxRunRos2TransportProvider.IdValue", StringComparison.Ordinal)
                  && sample.Contains("FoxgloveWebSocketTransport.Id", StringComparison.Ordinal)
                  && sample.Contains("Encoding = FoxRunEncoding.JSON", StringComparison.Ordinal)
                  && sample.Contains("byte[]", StringComparison.Ordinal)
                  && sample.Contains("List<long>", StringComparison.Ordinal)
                  && sample.Contains("int?", StringComparison.Ordinal),
                "181F-1: source-only sample exercises locked custom DTO output, input, and directional P&S contracts");

            Check(importedSample.Contains("public sealed class Phase181State", StringComparison.Ordinal)
                  && importedSample.Contains("public sealed class Phase181NestedState", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures", StringComparison.Ordinal)
                  && !acceptanceComponent.Contains("public sealed class Phase181State", StringComparison.Ordinal)
                  && !acceptanceComponent.Contains("public sealed class Phase181NestedState", StringComparison.Ordinal),
                "181F-13: the imported sample is the sole Unity compile-surface owner of the locked custom DTO identity");

            const string bidirectionalField = "[SerializeField] private Phase181State _nativeInputWebSocketOutput";
            const string bidirectionalOwnership =
                "Native ROS2 is the inbound source while Foxglove is the explicit JSON output target.";
            Check(sample.Contains(bidirectionalField, StringComparison.Ordinal)
                  && importedSample.Contains(bidirectionalField, StringComparison.Ordinal)
                  && sample.Contains(bidirectionalOwnership, StringComparison.Ordinal)
                  && importedSample.Contains(bidirectionalOwnership, StringComparison.Ordinal)
                  && !sample.Contains("FOXRUN400", StringComparison.Ordinal)
                  && !importedSample.Contains("FOXRUN400", StringComparison.Ordinal),
                "181F-14: valid full-duplex declarations compile without a per-use warning while the sample documents ownership");

            const string inputPortField = "[SerializeField] private Phase181State _inputPort";
            const string inputPortView = "public Phase181State NativeInputPort => _inputPort;";
            Check(sample.Contains(inputPortField, StringComparison.Ordinal)
                  && sample.Contains(inputPortView, StringComparison.Ordinal)
                  && importedSample.Contains(inputPortField, StringComparison.Ordinal)
                  && importedSample.Contains(inputPortView, StringComparison.Ordinal)
                  && !sample.Contains("_nativeSubscribe", StringComparison.Ordinal)
                  && !importedSample.Contains("_nativeSubscribe", StringComparison.Ordinal),
                "181F-15: the Subscribe sample member communicates input-port authority and is observably consumed");

            Check(sampleReadme.Contains("static interface lock", StringComparison.OrdinalIgnoreCase)
                  && sampleReadme.Contains("Windows-local", StringComparison.Ordinal)
                  && r2fuPackageJson.Contains("FoxRun Custom ROS2 Interface", StringComparison.Ordinal)
                  && r2fuPackageJson.Contains("Samples~/FoxRun Custom ROS2 Interface", StringComparison.Ordinal),
                "181F-2: package sample documents its immutable interface identity and has a discoverable import entry");

            var requiredMarkers = new[]
            {
                "PHASE181_CUSTOM_ROS2_READY",
                "PHASE181_CUSTOM_INTERFACE_READY",
                "PHASE181_CUSTOM_ROS2_PUBLISHED",
                "PHASE181_CUSTOM_ROS2_APPLIED",
                "PHASE181_CUSTOM_ROS2_SAME_ORIGIN_DROPPED",
                "PHASE181_CUSTOM_ROS2_PASS",
                "PHASE181_CUSTOM_ROS2_FAIL",
                "PHASE181_CUSTOM_ROS2_UNAVAILABLE",
            };
            Check(requiredMarkers.All(marker => acceptanceComponent.Contains(marker, StringComparison.Ordinal))
                  && acceptanceComponent.Contains("FoxRunRos2SubscriptionAcceptanceDiagnostics", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("GenerateRunToken", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("IsCorrelatedInitialPayload", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("IsNullEmptyRemotePayload", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("_correlatedSubscribeApplied", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("_correlatedBidirectionalFinalApplied", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("boundedFields + \" token=\" + SanitizeToken(_runToken)", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("10f,\n                600f", StringComparison.Ordinal)
                  && !acceptanceComponent.Contains("using ROS2", StringComparison.Ordinal)
                  && !acceptanceComponent.Contains("CreateNode", StringComparison.Ordinal)
                  && !acceptanceComponent.Contains("Ros2cs.Init", StringComparison.Ordinal),
                "181F-3: acceptance component correlates the exact custom DTO proof through bounded diagnostics without creating a ROS2 node");

            const string acceptanceInputPort = "[SerializeField] private Phase181State _inputPort";
            const string unavailableGuard = "#if !(UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES)";
            Check(acceptanceComponent.IndexOf(acceptanceInputPort, StringComparison.Ordinal) >= 0
                  && acceptanceComponent.IndexOf(acceptanceInputPort, StringComparison.Ordinal)
                     < acceptanceComponent.IndexOf(unavailableGuard, StringComparison.Ordinal)
                  && acceptanceComponent.Contains("public Phase181State InputPort => _inputPort;", StringComparison.Ordinal)
                  && acceptanceComponent.Contains(
                      "The peer protocol explicitly owns the native inbound/output-loop evidence.",
                      StringComparison.Ordinal)
                  && !acceptanceComponent.Contains("FOXRUN400", StringComparison.Ordinal),
                "181F-16: acceptance contracts stay generator-visible before add-on selection, while native bindings remain conditional");

            Check(playerBuilder.Contains("CreateAcceptanceScene", StringComparison.Ordinal)
                  && playerBuilder.Contains("BuildWindowsStandalone64", StringComparison.Ordinal)
                  && playerBuilder.Contains("--phase181-custom-ros2-player-auto-quit", StringComparison.Ordinal)
                  && playerBuilder.Contains("EnsurePathWithinBuildRoot", StringComparison.Ordinal)
                  && playerBuilder.Contains("PlayerEnvironmentKeys", StringComparison.Ordinal)
                  && playerBuilder.Contains("ResolveStaticInterfaceLock", StringComparison.Ordinal)
                  && playerBuilder.Contains("UNITY2FOXGLOVE_FOXRUN_INTERFACE_DIGEST", StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive",
                      StringComparison.Ordinal),
                "181F-4: acceptance builder preserves interactive scenes and creates a batch-safe bounded Player artifact below the repository build root");

            Check(batchProbe.Contains("Phase181BatchModeCustomRos2InteropProbe", StringComparison.Ordinal)
                  && batchProbe.Contains("Phase181CustomRos2InterfacePlayerBuilder.AcceptanceSceneAssetPath", StringComparison.Ordinal)
                  && batchProbe.Contains("Application.logMessageReceived", StringComparison.Ordinal)
                  && batchProbe.Contains("PHASE181_CUSTOM_ROS2_SAME_ORIGIN_DROPPED", StringComparison.Ordinal)
                  && batchProbe.Contains("PHASE181_BATCH_CUSTOM_ROS2_PROBE_PLAY_RETRY", StringComparison.Ordinal)
                  && batchProbe.Contains("MaximumPlayEntryRetries", StringComparison.Ordinal)
                  && batchProbe.Contains("play-entry-retry-limit", StringComparison.Ordinal)
                  && batchProbe.Contains("EditorApplication.ExitPlaymode", StringComparison.Ordinal)
                  && batchProbe.Contains("EditorApplication.Exit", StringComparison.Ordinal)
                  && !batchProbe.Contains("using ROS2", StringComparison.Ordinal)
                  && !batchProbe.Contains("CreateNode", StringComparison.Ordinal),
                "181F-19: the Batch-only Editor probe drives the tracked custom-interface scene and accepts only its bounded terminal evidence markers");

            Check(typesupportPluginImporterBuilder.Contains(
                      "var stageFileName = Path.GetFileName(uniqueFolder) + \"_\" + Path.GetFileName(input);",
                      StringComparison.Ordinal)
                  && typesupportPluginImporterBuilder.Contains(
                      "var stageAssetPath = uniqueFolder + \"/\" + stageFileName;",
                      StringComparison.Ordinal),
                "181F-20: candidate PluginImporter staging gives each DLL a Batch-unique file name, avoiding collisions with active add-on plugins");
        }

    }
}
