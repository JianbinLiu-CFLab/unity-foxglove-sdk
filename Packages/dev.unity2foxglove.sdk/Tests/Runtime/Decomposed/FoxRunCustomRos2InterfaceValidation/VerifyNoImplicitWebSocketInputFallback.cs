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
        private static void VerifyNoImplicitWebSocketInputFallback(
            string validator,
            string emitter,
            string r2fuAnalyzerPipeline,
            string manifestBuilder)
        {
            Check(validator.Contains("InvalidDirectionalTransportId", StringComparison.Ordinal)
                  && validator.Contains("CanEncodingReachWebSocket(member)", StringComparison.Ordinal)
                  && validator.Contains(
                      "FoxRun Encoding applies only to the foxglove.websocket Provider.",
                      StringComparison.Ordinal)
                  && !validator.Contains("CustomNativeBidirectionalContractDiagnosticId", StringComparison.Ordinal)
                  && !validator.Contains("NativeSubscribeDiagnosticId", StringComparison.Ordinal)
                  && !validator.Contains("NativeEncodingDiagnosticId", StringComparison.Ordinal)
                  && !validator.Contains("NativeSourcePublishDiagnosticId", StringComparison.Ordinal),
                "181A-7: neutral validation rejects WebSocket-only encoding on Provider-only routes without restoring legacy ROS diagnostics");

            Check(emitter.Contains("webSocketInputMembers", StringComparison.Ordinal)
                  && emitter.Contains(
                      ".Where(member => member.GeneratesWebSocketCodec)",
                      StringComparison.Ordinal)
                  && !emitter.Contains("Ros2InputDispatchEmitter", StringComparison.Ordinal)
                  && r2fuAnalyzerPipeline.Contains(
                      "private const string ProviderId = \"unity2foxglove.r2fu\"",
                      StringComparison.Ordinal)
                  && r2fuAnalyzerPipeline.Contains("SelectedForSubscribe", StringComparison.Ordinal)
                  && r2fuAnalyzerPipeline.Contains("SelectedForPublish", StringComparison.Ordinal)
                  && r2fuAnalyzerPipeline.Contains(
                      "Ros2InputDispatchEmitter.EmitConditionalPartial",
                      StringComparison.Ordinal)
                  && manifestBuilder.Contains(
                      ".Where(member => member.GeneratesWebSocketCodec)",
                      StringComparison.Ordinal)
                  && manifestBuilder.Contains("member.PublishTransportIds", StringComparison.Ordinal)
                  && manifestBuilder.Contains("member.SubscribeTransportId", StringComparison.Ordinal)
                  && manifestBuilder.Contains(
                      "member.Flow == (int)FoxRunFlow.PublishAndSubscribe",
                      StringComparison.Ordinal),
                "181A-8: the R2FU analyzer emits only selected Provider partials while core manifests retain neutral routing and WebSocket codec eligibility");
        }

        private static void VerifyDiagnosticRanges(
            string diagnostics,
            string unshippedLedger,
            string shippedLedger,
            string r2fuDiagnostics,
            string r2fuUnshippedLedger)
        {
            var expectedIds = new[]
            {
                "FOXR2F008",
                "FOXR2F009",
                "FOXR2F010",
                "FOXR2F011",
                "FOXR2F012",
                "FOXR2F013",
            };
            Check(expectedIds.All(id =>
                      r2fuDiagnostics.Contains("\"" + id + "\"", StringComparison.Ordinal))
                  && expectedIds.All(id =>
                      r2fuUnshippedLedger.Contains(
                          id + " | FoxRun.R2FU |",
                          StringComparison.Ordinal))
                  && !r2fuUnshippedLedger.Contains("FOXRUN", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN402\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN606\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN607\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN608\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN612\"", StringComparison.Ordinal)
                  && unshippedLedger.Contains(
                      "FOXRUN204, FOXRUN207-FOXRUN212, FOXRUN402, FOXRUN606-FOXRUN608, and FOXRUN611-FOXRUN615 are retired before release with the removed closed transport model and remain permanently reserved.",
                      StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN205\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN206\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN214\"", StringComparison.Ordinal)
                  && unshippedLedger.Contains("FOXRUN205 | FoxRun | Error | Retired before release;", StringComparison.Ordinal)
                  && unshippedLedger.Contains("FOXRUN206 | FoxRun | Error | Retired before release;", StringComparison.Ordinal)
                  && unshippedLedger.Contains("FOXRUN214 | FoxRun | Error | Retired before release;", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN036\"", StringComparison.Ordinal)
                  && shippedLedger.Contains("FOXRUN036 | FoxRun |", StringComparison.Ordinal)
                  && unshippedLedger.Contains("FOXRUN036 | FoxRun | Error | Retired;", StringComparison.Ordinal),
                "181A-9: R2FU owns active custom/directional diagnostics while legacy core IDs and FOXRUN036 stay reserved");
        }

        private static void VerifyStaticSourcePackageBoundary(
            string interfaceIdentity,
            string interfaceRenderer,
            string interfaceWriter,
            string interfacePreflight,
            string interfaceCommand,
            string sourceGeneratorProject,
            string staticPackageJson,
            string staticLock,
            string staticCmake,
            string manifestBuilder,
            string manifestModel,
            string manifestJsonWriter,
            string descriptorWriter)
        {
            Check(interfaceIdentity.Contains("UnityPackageId = \"dev.unity2foxglove.foxrun.ros2.interfaces\"", StringComparison.Ordinal)
                  && interfaceIdentity.Contains("BuildRosPackageName(string currentPackageName", StringComparison.Ordinal)
                  && interfaceIdentity.Contains("TryParseRosPackageRevision", StringComparison.Ordinal)
                  && !interfaceIdentity.Contains("ROS2.", StringComparison.Ordinal),
                "181B-1: one ROS-free static interface identity freezes a chosen package stem across explicit revisions");

            Check(interfaceRenderer.Contains("rosidl_generate_interfaces", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("ament_cmake", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("rosidl_default_generators", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("builtin_interfaces/Time foxrun_stamp", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("foxrun_origin_id", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("foxrun_sequence", StringComparison.Ordinal)
                  && !interfaceRenderer.Contains("Typesupport", StringComparison.Ordinal),
                "181B-2: renderer emits deterministic source-only ROS interfaces with envelope origin sequence and stamp fields");

            Check(interfaceWriter.Contains("build", StringComparison.Ordinal)
                  && interfaceWriter.Contains("phase181", StringComparison.Ordinal)
                  && interfaceWriter.Contains("interface-generation", StringComparison.Ordinal)
                  && interfaceWriter.Contains("File.Replace", StringComparison.Ordinal)
                  && interfaceWriter.Contains("RestoreBackup", StringComparison.Ordinal)
                  && interfaceWriter.Contains("RevisionRequired", StringComparison.Ordinal)
                  && !interfaceWriter.Contains("ROS2.", StringComparison.Ordinal),
                "181B-3: writer stages outside Packages and preserves the prior source package on cancellation or failure");

            Check(interfacePreflight.Contains("NotRequired", StringComparison.Ordinal)
                  && interfacePreflight.Contains("ReadyForBuild", StringComparison.Ordinal)
                  && interfacePreflight.Contains("MissingSource", StringComparison.Ordinal)
                  && interfacePreflight.Contains("StaleSource", StringComparison.Ordinal)
                  && interfacePreflight.Contains("InvalidSource", StringComparison.Ordinal)
                  && interfacePreflight.Contains("RevisionRequired", StringComparison.Ordinal)
                  && interfacePreflight.Contains("intentionally does not share", StringComparison.Ordinal)
                  && !interfacePreflight.Contains("using ROS2", StringComparison.Ordinal)
                  && !interfacePreflight.Contains("DllImport", StringComparison.Ordinal)
                  && !interfacePreflight.Contains("Assembly.Load", StringComparison.Ordinal)
                  && !interfacePreflight.Contains("Ros2ForUnityRuntimeSelection", StringComparison.Ordinal)
                  && !interfacePreflight.Contains("RMW_IMPLEMENTATION", StringComparison.Ordinal),
                "181B-4: R2FU source preflight has independent typed states and performs no RMW or native loading");

            Check(interfaceCommand.Contains("--check", StringComparison.Ordinal)
                  && interfaceCommand.Contains("--generate", StringComparison.Ordinal)
                  && interfaceCommand.Contains("--next-revision", StringComparison.Ordinal)
                  && interfaceCommand.Contains("ExecuteFromCommandLine", StringComparison.Ordinal)
                  && interfaceCommand.Contains("CollectReflectionGenerationModelForTransportProviders", StringComparison.Ordinal)
                  && interfaceCommand.Contains("EditorApplication.Exit", StringComparison.Ordinal),
                "181B-5: explicit R2FU Editor and batch command never lets Play Mode or source generation mutate the package");

            var staticRosPackageName = ReadRequiredJsonString(staticLock, "rosPackageName");
            Check(staticPackageJson.Contains("\"name\": \"dev.unity2foxglove.foxrun.ros2.interfaces\"", StringComparison.Ordinal)
                  && staticLock.Contains("\"lockSchemaVersion\":1", StringComparison.Ordinal)
                  && staticLock.Contains("\"interfaceDigest\":\"", StringComparison.Ordinal)
                  && staticCmake.Contains("project(" + staticRosPackageName + ")", StringComparison.Ordinal)
                  && staticCmake.Contains("rosidl_generate_interfaces", StringComparison.Ordinal),
                "181B-6: tracked static UPM package CMake identity follows the lock-selected portable ROS package revision");

            Check(interfaceRenderer.Contains(
                      "FoxRunRos2InterfaceIdentity.BuildEnvelopeMessageName",
                      StringComparison.Ordinal)
                  && interfaceRenderer.Contains("shape.CanonicalIdentity", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("shape.PayloadIdentity", StringComparison.Ordinal)
                  && manifestModel.Contains("PublishTransportIds", StringComparison.Ordinal)
                  && manifestModel.Contains("SubscribeTransportId", StringComparison.Ordinal)
                  && manifestJsonWriter.Contains("publishTransportIds", StringComparison.Ordinal)
                  && manifestJsonWriter.Contains("subscribeTransportId", StringComparison.Ordinal)
                  && descriptorWriter.Contains("publishTransportIds", StringComparison.Ordinal)
                  && descriptorWriter.Contains("subscribeTransportId", StringComparison.Ordinal),
                "181B-7: R2FU owns DTO envelope identity while core manifest and descriptor evidence carry neutral Provider selection");

            Check(!sourceGeneratorProject.Contains("FoxRunRos2InterfaceDigest.cs", StringComparison.Ordinal)
                  && !sourceGeneratorProject.Contains("FoxRunRos2InterfaceLock.cs", StringComparison.Ordinal)
                  && !sourceGeneratorProject.Contains("FoxRunRos2InterfaceJsonWriter.cs", StringComparison.Ordinal)
                  && !descriptorWriter.Contains("using Unity.FoxgloveSDK.Components;", StringComparison.Ordinal),
                "181B-8: static-package editor helpers stay out of the analyzer compile surface");
        }

        private static void VerifyRegistryEntry()
        {
            var entries = PhaseValidationRegistry.All
                .Where(entry => string.Equals(entry.Flag, "--phase181", StringComparison.Ordinal))
                .ToArray();
            var handlerEntries = PhaseValidationRegistry.All
                .Where(entry => entry.Run == (Action)Validate)
                .ToArray();
            var defaultEntries = PhaseValidationRegistry.DefaultValidations(includeLocalEvidence: false)
                .Where(entry => string.Equals(entry.Flag, "--phase181", StringComparison.Ordinal))
                .ToArray();

            Check(entries.Length == 1
                  && handlerEntries.Length == 1
                  && ReferenceEquals(entries[0], handlerEntries[0])
                  && entries[0].Category == ValidationCategory.CiSafe
                  && entries[0].IncludeInDefault
                  && entries[0].Evidence == (ValidationEvidence.Behavior | ValidationEvidence.Structural)
                  && defaultEntries.Length == 1
                  && string.Equals(entries[0].Name, "FoxRun custom R2FU interface interop release gate", StringComparison.Ordinal),
                "181F-9: Phase181 updates its one registry entry to the default behavior and structural release gate");
        }

        private static void VerifyTypesupportAddOnActivation(
            string transaction,
            string runtimeSelection,
            string defineInstaller,
            string playModeGuard)
        {
            Check(transaction.Contains("EvaluateActive", StringComparison.Ordinal)
                  && transaction.Contains("WriteAtomically", StringComparison.Ordinal)
                  && transaction.Contains("File.Replace", StringComparison.Ordinal)
                  && transaction.Contains("Client.Resolve", StringComparison.Ordinal) == false
                  && transaction.Contains("property.Name == StaticInterfacePackageId", StringComparison.Ordinal)
                  // The transaction documents Unity ownership of the lock;
                  // reject an actual packages-lock.json write path instead of
                  // treating that explanatory text as a violation.
                  && transaction.Contains("packages-lock.json", StringComparison.Ordinal) == false
                  && transaction.Contains("NativePluginRelativeDirectory", StringComparison.Ordinal),
                "181C-1: add-on transaction validates manifest-owned candidates and never writes Unity lock state");

            Check(runtimeSelection.Contains("CustomTypesupportPackagePrefix", StringComparison.Ordinal)
                  && runtimeSelection.Contains("SwitchActiveCustomTypesupportPackage", StringComparison.Ordinal)
                  && runtimeSelection.Contains("BuildCleanRestartPath", StringComparison.Ordinal)
                  && runtimeSelection.Contains("GetCustomTypesupportRequiringEditorRestart", StringComparison.Ordinal)
                  && runtimeSelection.Contains("SessionCustomTypesupportIdentityKey", StringComparison.Ordinal),
                "181C-2: runtime selection treats the add-on identity and plugin path as restart-sensitive native state");

            Check(defineInstaller.Contains("CustomTypesupportCompileSymbol", StringComparison.Ordinal)
                  && defineInstaller.Contains("GetActiveCustomTypesupportSelection", StringComparison.Ordinal)
                  && defineInstaller.Contains("RemoveSymbol(parts, Ros2ForUnityRuntimeSelection.CustomTypesupportCompileSymbol)", StringComparison.Ordinal),
                "181C-3: custom compile symbol is enabled only for one validated active add-on");

            Check(playModeGuard.Contains("GetActiveCustomTypesupportSelection", StringComparison.Ordinal)
                  && playModeGuard.Contains("GetCustomTypesupportRequiringEditorRestart", StringComparison.Ordinal)
                  && playModeGuard.Contains("custom ROS2 typesupport is not ready", StringComparison.Ordinal),
                "181C-4: Play Mode rechecks custom add-on readiness before native initialization");
        }

        private static void VerifyTypedNativeTransport(
            string customTransportHost,
            string customPublisherHub,
            string customPublisherBinding,
            string subscriptionBinding,
            string customOutboundPolicy,
            string customOutboundBudgetPolicy,
            string customMapperEmitter,
            string customPublishEmitter,
            string foxgloveLogHub,
            string r2fuTransportProvider)
        {
            Check(customTransportHost.Contains("TryAcquireSubscriptionBackend", StringComparison.Ordinal)
                  && customTransportHost.Contains("TryAcquirePublisherBackend", StringComparison.Ordinal)
                  && customTransportHost.Contains("unity2foxglove_foxrun_custom", StringComparison.Ordinal)
                  && customTransportHost.Contains("ReleaseLease", StringComparison.Ordinal),
                "181D-1: custom typed input and output retain one demand-created node lease host");

            var resolveQos = customPublisherHub.IndexOf("contract.ResolveQos(", StringComparison.Ordinal);
            var resolveTopology = customPublisherHub.IndexOf("ShouldRegisterNativePublisher(", StringComparison.Ordinal);
            var readinessGate = customPublisherHub.IndexOf("!readiness.IsReady", StringComparison.Ordinal);
            var acquireBackend = customPublisherHub.IndexOf("TryAcquirePublisherBackend", StringComparison.Ordinal);
            var beginPublisher = customPublisherHub.IndexOf(
                "FoxRunRos2CustomOriginRegistry.BeginPublisher",
                StringComparison.Ordinal);
            Check(customPublisherHub.Contains("IFoxRunRos2CustomPublisherSource", StringComparison.Ordinal)
                  && resolveQos >= 0
                  && resolveTopology > resolveQos
                  && readinessGate > resolveTopology
                  && acquireBackend > readinessGate
                  && beginPublisher > acquireBackend,
                "181D-2: custom output demand resolves QoS and native targets independently of subscription sessions and fails closed before endpoint creation");

            var stopStart = customPublisherBinding.IndexOf("internal void Stop()", StringComparison.Ordinal);
            var stopEnd = customPublisherBinding.IndexOf("private bool OnBusEnvelope", StringComparison.Ordinal);
            var stopBody = stopStart >= 0 && stopEnd > stopStart
                ? customPublisherBinding.Substring(stopStart, stopEnd - stopStart)
                : string.Empty;
            var unsubscribe = stopBody.IndexOf("_bus.Unsubscribe", StringComparison.Ordinal);
            var detachToken = stopBody.IndexOf("Interlocked.Exchange(ref _token, null)", StringComparison.Ordinal);
            var removePublisher = stopBody.IndexOf("TryRemovePublisher(token)", StringComparison.Ordinal);
            var releaseNode = stopBody.IndexOf("_backend.ReleaseNodeOwnership", StringComparison.Ordinal);
            var removeHelper = customPublisherBinding.IndexOf(
                "private void TryRemovePublisher", StringComparison.Ordinal);
            var backendRemove = removeHelper >= 0
                ? customPublisherBinding.IndexOf("_backend.RemovePublisher(token)", removeHelper, StringComparison.Ordinal)
                : -1;
            Check(customPublisherBinding.Contains("FoxTopicBus", StringComparison.Ordinal)
                  && customPublisherBinding.Contains("_bus.Subscribe", StringComparison.Ordinal)
                  && customPublisherBinding.Contains("FoxRunRos2CustomSequenceSource", StringComparison.Ordinal)
                  && customPublisherBinding.Contains("FoxRunRos2CustomOutboundMappingPolicy.CreateContext", StringComparison.Ordinal)
                  && unsubscribe >= 0
                  && detachToken > unsubscribe
                  && removePublisher > detachToken
                  && releaseNode > removePublisher
                  && backendRemove > removeHelper
                  && customPublisherBinding.IndexOf(
                      "FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception)",
                      backendRemove,
                      StringComparison.Ordinal) > backendRemove,
                "181D-3: typed publisher unsubscribes, detaches its endpoint token, and protects native teardown before node release");

            Check(subscriptionBinding.Contains("dropBeforeApply", StringComparison.Ordinal)
                  && subscriptionBinding.Contains("SameOriginDropCount", StringComparison.Ordinal)
                  && subscriptionBinding.Contains(
                      "_slot.TryApplyLatest(_decideOwned, _applyOwned, _clearOwned)",
                      StringComparison.Ordinal)
                  && subscriptionBinding.Contains(
                      "return FoxRunRos2PendingDecision.Drop",
                      StringComparison.Ordinal),
                "181D-4: custom P&S drops its own origin through the policy decision after bounded callback copying and before DTO apply");

            Check(customOutboundBudgetPolicy.Contains(
                      "MaximumBytes = 4L * 1024L * 1024L",
                      StringComparison.Ordinal)
                  && customOutboundPolicy.Contains(
                      "FoxRunRos2CustomOutboundBudgetPolicy.MaximumBytes",
                      StringComparison.Ordinal)
                  && !customOutboundPolicy.Contains("foxRunRos2NativeCopyBudget", StringComparison.Ordinal)
                  && customMapperEmitter.Contains("FoxRunRos2CustomEnvelopeTimestamp.TryFromUnixNanoseconds", StringComparison.Ordinal)
                  && customMapperEmitter.Contains("__FoxRunRos2CustomDisposeEnvelope", StringComparison.Ordinal)
                  && customPublishEmitter.Contains("__FoxRunRos2CustomMapDtoToEnvelope", StringComparison.Ordinal),
                "181D-5: outbound mapping uses its fixed 4 MiB cap, timestamp bounds, and exact-once generated disposal");

            Check(foxgloveLogHub.Contains("SelectsWebSocket(info)", StringComparison.Ordinal)
                  && foxgloveLogHub.Contains("IFoxgloveTopicBusSource", StringComparison.Ordinal)
                  && foxgloveLogHub.Contains("busSource", StringComparison.Ordinal)
                  && foxgloveLogHub.Contains(".FoxgloveLog_PublishToBus(", StringComparison.Ordinal)
                  && foxgloveLogHub.Contains("IFoxgloveTopicSinkSource", StringComparison.Ordinal)
                  && r2fuTransportProvider.Contains("FoxRunRos2CustomPublisherHub", StringComparison.Ordinal)
                  && r2fuTransportProvider.Contains("FoxRunRos2SubscriptionHub", StringComparison.Ordinal)
                  && r2fuTransportProvider.Contains(
                      "not untyped byte payloads",
                      StringComparison.Ordinal),
                "181D-6: core fans out WebSocket, typed-bus, and neutral sink routes while R2FU owns generated typed ROS2 bindings");
        }
    }
}
