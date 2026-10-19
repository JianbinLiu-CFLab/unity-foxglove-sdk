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
    /// <summary>
    /// Locks the ROS-free custom DTO model, the selected typesupport boundary,
    /// and the closed-generic Phase181 native transport seams.
    /// </summary>
    internal static partial class FoxRunCustomRos2InterfaceValidation
    {
        private static void VerifyTypesupportPreflightPresentation(
            string discovery,
            string preflight,
            string inspector,
            string managerR2fuRuntimeInspector,
            string interfaceCommand)
        {
            var requiredStates = new[]
            {
                "NotRequired", "MissingSource", "StaleSource", "MissingAddOn", "MultipleAddOns",
                "DistributionMismatch", "DigestMismatch", "InvalidManifest", "InvalidInventory",
                "MissingManagedType", "MissingCatalog", "DuplicateCatalog", "UnsupportedRmw", "Settling", "Ready",
            };
            Check(requiredStates.All(state => preflight.Contains(state, StringComparison.Ordinal))
                  && discovery.Contains("InvalidateCache", StringComparison.Ordinal)
                  && !discovery.Contains("Client.Resolve", StringComparison.Ordinal)
                  && !discovery.Contains("Assembly.Load", StringComparison.Ordinal)
                  && !discovery.Contains("NativeLibrary.Load", StringComparison.Ordinal)
                  && !discovery.Contains("Ros2cs.Init", StringComparison.Ordinal)
                  && !discovery.Contains("CreateNode", StringComparison.Ordinal)
                  && !discovery.Contains("using ROS2", StringComparison.Ordinal),
                "181E-1: metadata-only preflight has distinct bounded readiness states and no resolver or native load path");

            Check(inspector.Contains("Custom FoxRun ROS 2 Interface", StringComparison.Ordinal)
                  && inspector.Contains("Install and Select Matching Typesupport Add-On", StringComparison.Ordinal)
                  && inspector.Contains("FilterCandidatesForRuntime", StringComparison.Ordinal)
                  && inspector.Contains("runtime.RosDistro", StringComparison.Ordinal)
                  && inspector.Contains("runtime.Platform", StringComparison.Ordinal)
                  && !inspector.Contains("PendingAddOnByProject", StringComparison.Ordinal)
                  && !inspector.Contains("EditorGUILayout.Popup(\"Matching Add-On\"", StringComparison.Ordinal)
                  && inspector.Contains("var selectionChangeBlocked = EditorApplication.isPlayingOrWillChangePlaymode", StringComparison.Ordinal)
                  && inspector.Contains("|| EditorApplication.isCompiling", StringComparison.Ordinal)
                  && inspector.Contains("|| EditorApplication.isUpdating", StringComparison.Ordinal)
                  && inspector.Contains("catch (InvalidOperationException exception)", StringComparison.Ordinal)
                  && inspector.Contains("+ exception.Message", StringComparison.Ordinal)
                  && inspector.Contains("Generate ROS2 Interface Source Package", StringComparison.Ordinal)
                  && inspector.Contains("Validate ROS2 Interface Source Package", StringComparison.Ordinal)
                  && inspector.Contains("Open ROS2 Interface Source Package", StringComparison.Ordinal)
                  && !inspector.Contains("Client.Resolve", StringComparison.Ordinal)
                  && !inspector.Contains("packages-lock.json", StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "IFoxRunTransportProviderDrawer",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "FoxRunTransportProviderDrawerRegistry.Register",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "FoxRunRos2CustomTypesupportInspector",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      ".DrawCustomTypesupportPreflight(",
                      StringComparison.Ordinal)
                  && interfaceCommand.Contains("ValidateFromMenu", StringComparison.Ordinal)
                   && interfaceCommand.Contains("OpenSourcePackageFromMenu", StringComparison.Ordinal),
                "181E-2: the R2FU Provider drawer directly owns one custom-interface inspector without a core reflection seam");

            Check(managerR2fuRuntimeInspector.Contains(
                      "CollectReflectionGenerationModelForTransportProviders",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "member.GeneratesRos2NativeRegistration",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "FoxRunRos2ContractKind.CustomDto",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "member.Ros2CustomDtoShape",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      ".PayloadIdentity",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "DirectionalPolicyLabel(member)",
                      StringComparison.Ordinal),
                "181E-3: the R2FU drawer derives custom typesupport demand from the current Provider generation model and directional policy");

            Check(managerR2fuRuntimeInspector.Contains(
                      "manager.GetComponent<",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "FoxRunRos2TransportProvider>()",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "Undo.AddComponent<",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "PrefabUtility",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "RecordPrefabInstancePropertyModifications",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "EditorSceneManager.MarkSceneDirty",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "if (provider == null)",
                      StringComparison.Ordinal),
                "181E-4: the Provider drawer owns ordinary component creation and renders preflight only for its attached R2FU Provider");
        }

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

        private static void VerifyInteropAutomationReleaseGate(
            string peerProtocol,
            string peerHelper,
            string linuxPeer,
            string matrixProfiles,
            string runCi,
            string dotnetWorkflow,
            string packageWorkflow)
        {
            Check(peerProtocol.Contains("SUMMARY_SCHEMA_VERSION", StringComparison.Ordinal)
                  && peerProtocol.Contains("write_summary_atomic", StringComparison.Ordinal)
                  && peerProtocol.Contains("FAIL_INTERFACE_DIGEST", StringComparison.Ordinal)
                  && !peerProtocol.Contains("shell=True", StringComparison.Ordinal)
                  && !peerProtocol.Contains("os.system", StringComparison.Ordinal),
                "181F-5: common peer protocol persists redacted correlated evidence without shell execution");

            Check(peerHelper.Contains("build_player_environment", StringComparison.Ordinal)
                  && peerHelper.Contains("require_player_exit_code", StringComparison.Ordinal)
                  && peerHelper.Contains("--probe-role", StringComparison.Ordinal)
                  && peerHelper.Contains("--surface", StringComparison.Ordinal)
                  && peerHelper.Contains("FAIL_GRAPH_EVIDENCE", StringComparison.Ordinal)
                  && peerHelper.Contains("create_typed_worker_endpoints", StringComparison.Ordinal)
                  && peerHelper.Contains("observe_no_late_unity_apply", StringComparison.Ordinal)
                  && peerHelper.Contains("FAIL_LATE_APPLY", StringComparison.Ordinal)
                  && peerHelper.Contains("FAIL_PEER_RUNTIME", StringComparison.Ordinal)
                  && !peerHelper.Contains("shell=True", StringComparison.Ordinal)
                  && !peerHelper.Contains("os.system", StringComparison.Ordinal),
                "181F-6: Windows Editor and Player paths reuse the strict generated-envelope protocol and bounded endpoint teardown");

            Check(linuxPeer.Contains("stage_or_verify_locked_ros_source", StringComparison.Ordinal)
                  && linuxPeer.Contains("build_linux_worker_environment", StringComparison.Ordinal)
                  && linuxPeer.Contains("--role", StringComparison.Ordinal)
                  && linuxPeer.Contains("--surface", StringComparison.Ordinal)
                  && !linuxPeer.Contains("shell=True", StringComparison.Ordinal)
                  && !linuxPeer.Contains("os.system", StringComparison.Ordinal),
                "181F-7: Linux peer keeps source/workspace ownership explicit and imports only its built generated package");

            Check(matrixProfiles.Contains("DEFAULT_READY_TIMEOUT_SECONDS = 300", StringComparison.Ordinal)
                  && matrixProfiles.Contains("PHASE181_HUMBLE_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS", StringComparison.Ordinal)
                  && matrixProfiles.Contains("PHASE181_LYRICAL_ZENOH_WINDOWS_LOCAL_EDITOR_PASS", StringComparison.Ordinal)
                  && matrixProfiles.Contains("write_profile_failure_summary", StringComparison.Ordinal)
                  && !matrixProfiles.Contains("shell=True", StringComparison.Ordinal),
                "181F-8: four named Windows-local wrappers preserve a bounded String-first and owned-Zenoh path");

            Check(runCi.Contains("phase181-ros2-regression", StringComparison.Ordinal)
                  && runCi.Contains("PHASE181_ROS2_PEER_REGRESSION", StringComparison.Ordinal)
                  && runCi.Contains("validate_foxrun_custom_typesupport_addon.py", StringComparison.Ordinal)
                  && dotnetWorkflow.Contains("Run Phase181 custom ROS2 acceptance helper regressions", StringComparison.Ordinal)
                  && packageWorkflow.Contains("Validate Phase181 custom ROS2 typesupport add-ons", StringComparison.Ordinal),
                "181F-10: public CI runs protocol regressions and all tracked custom typesupport preflight validators");
        }

        private static string ReadSplitPythonSource(string relativePath)
            => PhaseValidationSourceHelpers.ReadSplitPythonSource(relativePath);

        private static void VerifyRuntimeBatchSelection(string runtimeBatchSelection)
        {
            Check(runtimeBatchSelection.Contains("public static void SelectFromCommandLine()", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("-phase181Ros2Distro", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("-phase181Ros2CommunicationMode", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Ros2ForUnityCustomTypesupportSelectionTransaction.Apply", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Ros2ForUnityRuntimeSelection.SetCommunicationMode", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Client.Resolve()", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Events.registeredPackages", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("PackageInfo.FindForPackageName", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("[InitializeOnLoadMethod]", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("SessionState.SetString", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForEditor();", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("EditorApplication.Exit(0);", StringComparison.Ordinal),
                "181F-21: an explicit Unity Batch selector applies one validated runtime/add-on and communication-mode transaction before each isolated acceptance row");
        }

        private static void VerifyPublicOperationalDocumentation(
            string acceptanceSampleReadme,
            string ros2SmokeReadme,
            string r2fuPackageJson)
        {
            Check(acceptanceSampleReadme.Contains("exactly one matching", StringComparison.OrdinalIgnoreCase)
                  && acceptanceSampleReadme.Contains("static interface lock", StringComparison.OrdinalIgnoreCase)
                  && acceptanceSampleReadme.Contains("PublishAndSubscribe", StringComparison.Ordinal)
                  && r2fuPackageJson.Contains("FoxRun Custom ROS2 Interface", StringComparison.Ordinal),
                "181F-11: package sample exposes the locked custom DTO workflow without treating typesupport as an implicit runtime fallback");

            Check(ros2SmokeReadme.Contains("Phase181 Windows-local Editor bring-up", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("phase181_humble_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("phase181_lyrical_zenoh_acceptance.py", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("exactly one matching add-on", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("echo-on-apply", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("same-origin", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("FixedRate", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("not individually recorded to MCAP", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("Linux", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("Player", StringComparison.Ordinal),
                "181F-12: public custom-interface instructions distinguish local bring-up, certification, origin policy, and MCAP semantics");
        }

        private static string ReadRequiredJsonString(string json, string propertyName)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.GetString()))
            {
                throw new InvalidOperationException(
                    "[FAIL] Phase181 static interface lock lacks a non-empty string property: " + propertyName);
            }

            return property.GetString();
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
