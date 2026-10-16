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

    }
}
