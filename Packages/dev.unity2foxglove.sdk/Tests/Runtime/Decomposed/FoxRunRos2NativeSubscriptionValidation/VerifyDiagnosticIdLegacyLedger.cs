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
        private static void VerifyDiagnosticIdLegacyLedger()
        {
            var diagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.Diagnostics.cs");
            var shipped = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/AnalyzerReleases.Shipped.md");
            var unshipped = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/AnalyzerReleases.Unshipped.md");
            var retired = new[]
            {
                new KeyValuePair<string, string>("FOXRUN023", "FOXRUN600"),
                new KeyValuePair<string, string>("FOXRUN024", "FOXRUN200"),
                new KeyValuePair<string, string>("FOXRUN025", "FOXRUN201"),
                new KeyValuePair<string, string>("FOXRUN026", "FOXRUN400"),
                new KeyValuePair<string, string>("FOXRUN027", "FOXRUN202"),
                new KeyValuePair<string, string>("FOXRUN028", "FOXRUN203"),
                new KeyValuePair<string, string>("FOXRUN029", "FOXRUN601"),
                new KeyValuePair<string, string>("FOXRUN030", "FOXRUN602"),
                new KeyValuePair<string, string>("FOXRUN031", "FOXRUN603"),
                new KeyValuePair<string, string>("FOXRUN032", "FOXRUN604"),
                new KeyValuePair<string, string>("FOXRUN033", "FOXRUN605"),
                new KeyValuePair<string, string>("FOXRUN034", "FOXRUN401"),
                new KeyValuePair<string, string>("FOXRUN035", "FOXRUN204"),
                new KeyValuePair<string, string>("FOXRUN036", "FOXRUN205"),
                new KeyValuePair<string, string>("FOXRUN037", "FOXRUN206"),
                new KeyValuePair<string, string>("FOXRUN038", "FOXRUN207"),
                new KeyValuePair<string, string>("FOXRUN039", "FOXRUN208"),
                new KeyValuePair<string, string>("FOXRUN040", "FOXRUN209"),
                new KeyValuePair<string, string>("FOXRUN041", "FOXRUN210"),
                new KeyValuePair<string, string>("FOXRUN042", "FOXRUN211"),
                new KeyValuePair<string, string>("FOXRUN043", "FOXRUN212"),
                new KeyValuePair<string, string>("FOXRUN044", "FOXRUN213"),
            };
            var activeNeutralReplacements = new[]
            {
                "FOXRUN200",
                "FOXRUN202",
                "FOXRUN203",
                "FOXRUN600",
                "FOXRUN602",
                "FOXRUN603",
                "FOXRUN604",
                "FOXRUN605",
            };

            Check(diagnostics.Contains(
                      "Legacy FoxRun diagnostic IDs 023 through 044 and unshipped IDs 201, 205, 206, 214, 400, 401, and 601 are permanently retired and must never be reused.",
                      StringComparison.Ordinal)
                  && retired.All(pair => shipped.Contains(pair.Key + " | FoxRun |", StringComparison.Ordinal))
                  && retired.All(pair => HasReleaseTrackingRow(
                      unshipped,
                      pair.Key,
                      "Retired; renumbered as " + pair.Value + " and permanently reserved."))
                  && activeNeutralReplacements.All(id =>
                      unshipped.Contains(
                          id + " | FoxRun |",
                          StringComparison.Ordinal))
                  && unshipped.Contains(
                      "FOXRUN204, FOXRUN207-FOXRUN212, FOXRUN402, FOXRUN606-FOXRUN608, and FOXRUN611-FOXRUN615 are retired before release with the removed closed transport model and remain permanently reserved.",
                      StringComparison.Ordinal),
                "FoxRun diagnostic migration retains shipped history while neutral replacements stay active and closed-transport replacements stay reserved");
        }

        private static void VerifyOptionalCompilationLanes()
        {
            var props = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/FoxgloveSdk.TestSurface.props");
            var unitProject = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/Unit/FoxgloveSdk.UnitTests.csproj");
            var runtimeProject = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var nativeStub = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/NativeCompileStubs/Ros2ForUnityNativeCompileStubs.cs");

            Check(props.Contains("Runtime/Native/**/*.cs", StringComparison.Ordinal)
                  && props.Contains("Runtime/Native/FoxRun/**/*.cs", StringComparison.Ordinal)
                  && props.Contains("Ros2ForUnityNativeBridgeLifecycleGate.cs", StringComparison.Ordinal)
                  && props.Contains("ros2cs_common.dll", StringComparison.Ordinal)
                  && props.Contains("ros2cs_core.dll", StringComparison.Ordinal)
                  && props.Contains("std_msgs_assembly.dll", StringComparison.Ordinal)
                  && props.Contains("geometry_msgs_assembly.dll", StringComparison.Ordinal)
                  && props.Contains("sensor_msgs_assembly.dll", StringComparison.Ordinal),
                "shared test surface removes broad Native sources, re-includes FoxRun plus lifecycle gate, and pins Jazzy managed references");

            Check(unitProject.Contains("IncludeRos2ForUnityNative", StringComparison.Ordinal)
                  && unitProject.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && unitProject.Contains("ValidatePhase179NativeCompileSurface", StringComparison.Ordinal)
                  && unitProject.Contains("Ros2ForUnityNativeCompileStubs.cs", StringComparison.Ordinal)
                  && runtimeProject.Contains("IncludeRos2ForUnityNative", StringComparison.Ordinal)
                  && runtimeProject.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && runtimeProject.Contains("ValidatePhase179NativeCompileSurface", StringComparison.Ordinal)
                  && runtimeProject.Contains("Ros2ForUnityNativeCompileStubs.cs", StringComparison.Ordinal),
                "unit and runtime Native lanes imply adapter compilation, activate the define, and reject vacuous compile sets");

            Check(nativeStub.Contains("class ROS2UnityComponent", StringComparison.Ordinal)
                  && nativeStub.Contains("class ROS2Node", StringComparison.Ordinal)
                  && nativeStub.Contains("CreateSubscription<T>", StringComparison.Ordinal)
                  && nativeStub.Contains("RemoveSubscription(ISubscriptionBase", StringComparison.Ordinal)
                  && nativeStub.Contains("where T : Message, new()", StringComparison.Ordinal),
                "Native compile-only stubs expose only the source-owned R2FU node and subscription seam");
        }

        private static void VerifyNativeHostLifecycleBoundary()
        {
            const string nativeRoot =
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/";
            var hub = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                nativeRoot + "FoxRun/FoxRunRos2SubscriptionHub.cs");
            var binding = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                nativeRoot + "FoxRun/FoxRunRos2SubscriptionBinding.cs");
            var backend = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                nativeRoot + "FoxRun/Ros2ForUnityFoxRunInboundBackend.cs");
            var lifecycleGate = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                nativeRoot + "Ros2ForUnityNativeBridgeLifecycleGate.cs");
            var provider = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                nativeRoot + "FoxRun/FoxRunRos2TransportProvider.cs");

            var update = RequiredRos2ForUnityMethod(hub, "private void Update()", "FoxRunRos2SubscriptionHub.cs");
            var applySessionPolicy = RequiredRos2ForUnityMethod(
                hub,
                "private void ApplySessionPolicy(FoxRunSubscriptionSessionPolicy policy)",
                "FoxRunRos2SubscriptionHub.cs");
            var ensureNode = RequiredRos2ForUnityMethod(
                hub,
                "private bool TryEnsureNodeOwner",
                "FoxRunRos2SubscriptionHub.cs");
            var scan = RequiredRos2ForUnityMethod(
                hub,
                "private void ScanAndReconcile()",
                "FoxRunRos2SubscriptionHub.cs");
            var addBinding = RequiredRos2ForUnityMethod(
                hub,
                "private void AddBinding<T>",
                "FoxRunRos2SubscriptionHub.cs");
            var nativeAdmission = RequiredRos2ForUnityMethod(
                hub,
                "internal bool CanUseNativeRuntimeNow()",
                "FoxRunRos2SubscriptionHub.cs");
            var beginShutdown = RequiredRos2ForUnityMethod(
                hub,
                "private void BeginShutdown()",
                "FoxRunRos2SubscriptionHub.cs");
            var onDisable = RequiredRos2ForUnityMethod(
                hub,
                "private void OnDisable()",
                "FoxRunRos2SubscriptionHub.cs");
            var callback = RequiredRos2ForUnityMethod(
                binding,
                "private void OnBorrowedMessage",
                "FoxRunRos2SubscriptionBinding.cs");
            var stop = RequiredRos2ForUnityMethod(
                binding,
                "private void StopCore",
                "FoxRunRos2SubscriptionBinding.cs");
            var backendRegister = RequiredRos2ForUnityMethod(
                backend,
                "public FoxRunRos2NativeBackendRegistration Register<T>",
                "Ros2ForUnityFoxRunInboundBackend.cs");
            var bootstrapGateStart = lifecycleGate.IndexOf(
                "internal static bool CanBootstrapBridge",
                StringComparison.Ordinal);
            var bootstrapGateEnd = lifecycleGate.IndexOf(
                "internal static bool CanInitializeNativeRuntimeForBridge",
                bootstrapGateStart,
                StringComparison.Ordinal);
            var bootstrapGate = bootstrapGateStart >= 0 && bootstrapGateEnd > bootstrapGateStart
                ? lifecycleGate.Substring(bootstrapGateStart, bootstrapGateEnd - bootstrapGateStart)
                : string.Empty;

            Check(bootstrapGate.Contains("!_nativeReloadWindow", StringComparison.Ordinal),
                "native subscription bootstrap remains closed while the native reload window would immediately shut its new host down");

            Check(provider.Contains(
                      "GetOrAddOwnedHub<FoxRunRos2SubscriptionHub>()",
                      StringComparison.Ordinal)
                  && provider.Contains(
                      "RegisterFoxRunTransportProvider(this)",
                      StringComparison.Ordinal)
                  && !hub.Contains(
                      "RuntimeInitializeOnLoadMethod",
                      StringComparison.Ordinal)
                  && !hub.Contains("HideAndDontSave", StringComparison.Ordinal),
                "native subscription host is Manager-local Provider-owned without a global bootstrap object");

            var lifecycleWindow = update.IndexOf(
                "IsShuttingDownForBridge(gameObject.scene)",
                StringComparison.Ordinal);
            var lifecycleRefresh = lifecycleWindow < 0
                ? -1
                : update.IndexOf("CanBootstrapBridge", lifecycleWindow, StringComparison.Ordinal);
            var lifecyclePause = lifecycleWindow < 0
                ? -1
                : update.IndexOf("PauseForLifecycleWindow();", lifecycleWindow, StringComparison.Ordinal);
            var lifecycleShutdown = lifecycleWindow < 0
                ? -1
                : update.IndexOf("BeginShutdown();", lifecycleWindow, StringComparison.Ordinal);
            Check(OccursBefore(update, "_stopping", "BeginShutdown();")
                  && lifecycleWindow >= 0
                  && lifecycleRefresh > lifecycleWindow
                  && lifecyclePause > lifecycleRefresh
                  && lifecycleShutdown < 0
                  && hub.Contains("private void PauseForLifecycleWindow()", StringComparison.Ordinal),
                "a transient dirty-scene lifecycle gate refreshes before permanent shutdown and pauses native bindings without orphaning the Hub");

            var nativeGate = ensureNode.IndexOf(
                "CanInitializeNativeRuntimeForBridge(gameObject.scene)",
                StringComparison.Ordinal);
            Check(nativeGate >= 0
                  && nativeGate < ensureNode.IndexOf("_nodeOwner", StringComparison.Ordinal)
                  && nativeGate < ensureNode.IndexOf("GetComponent<", StringComparison.Ordinal)
                  && nativeGate < ensureNode.IndexOf("AddComponent<", StringComparison.Ordinal)
                  && nativeGate < ensureNode.IndexOf(".Ok()", StringComparison.Ordinal)
                  && nativeGate < ensureNode.IndexOf("CreateNode(", StringComparison.Ordinal)
                  && OccursBefore(
                      scan,
                      "CanInitializeNativeRuntimeForBridge",
                      "Binding.TryRegister()")
                  && OccursBefore(
                      addBinding,
                      "CanInitializeNativeRuntimeForBridge",
                      "binding.TryRegister()")
                  && hub.Contains(
                      "internal sealed class FoxRunRos2NativeRuntimeAdmission",
                      StringComparison.Ordinal)
                  && nativeAdmission.Contains(
                      "_activeSession.ReadGeneration() >= 0",
                      StringComparison.Ordinal)
                  && nativeAdmission.Contains(
                      "CanInitializeNativeRuntimeForBridge(",
                      StringComparison.Ordinal)
                  && nativeAdmission.Contains("_ownerScene", StringComparison.Ordinal)
                  && ensureNode.Contains(
                      "new FoxRunRos2NativeRuntimeAdmission(",
                      StringComparison.Ordinal)
                  && ensureNode.Contains(
                      "admission.CanUseNativeRuntimeNow",
                      StringComparison.Ordinal)
                  && OccursBefore(
                      backendRegister,
                      "_canUseNativeRuntime()",
                      "_driver.CreateSubscription("),
                "every component lookup, R2FU readiness check, node creation, and subscription registration is lifecycle-gated");

            Check(OccursBefore(beginShutdown, "_stopping = true", "SetManager(null);")
                  && OccursBefore(beginShutdown, "SetManager(null);", "StopBindingsAndNode();")
                  && beginShutdown.Contains(
                      "Application.quitting -= OnApplicationQuitting;",
                      StringComparison.Ordinal)
                  && OccursBefore(onDisable, "_stopping = true", "BeginShutdown();")
                  && OccursBefore(stop, "Volatile.Write(ref _stopping, 1)", "_slot.BeginStop")
                  && OccursBefore(stop, "_slot.BeginStop", "_backend.RemoveSubscription")
                  && OccursBefore(stop, "_backend.RemoveSubscription", "_slot.Stop")
                  && OccursBefore(stop, "_slot.Stop", "ReleaseNodeIfClaimed"),
                "shutdown closes callback admission, detaches subscriptions, drains owned copies, releases the node, and unsubscribes events in order");

            var forbiddenCallbackTokens = new[]
            {
                "UnityEngine.Object",
                "FindObject",
                "GetComponent",
                "AddComponent",
                "ROS2UnityComponent.Ok",
                "CreateNode",
                "CreateSubscription",
                "Debug.Log",
                "SceneManager",
            };
            Check(forbiddenCallbackTokens.All(
                      token => callback.IndexOf(token, StringComparison.Ordinal) < 0)
                  && hub.Contains("_activeSession.ReadGeneration", StringComparison.Ordinal)
                  && !addBinding.Contains("ActiveGeneration,", StringComparison.Ordinal)
                  && !nativeAdmission.Contains(
                      "FoxRunRos2SubscriptionHub",
                      StringComparison.Ordinal)
                  && backendRegister.Contains("_driver.CreateSubscription(", StringComparison.Ordinal)
                  && backendRegister.Contains("callback,", StringComparison.Ordinal),
                "executor callbacks retain only binding-owned managed state and never reacquire Unity or R2FU runtime services");

            var dispatchesLegacySource = scan.Contains(
                "source.Native.FoxRunRos2RegisterSubscriptions(registrar);",
                StringComparison.Ordinal)
                || scan.Contains(
                    "source.Native?.FoxRunRos2RegisterSubscriptions(registrar);",
                    StringComparison.Ordinal);
            Check(applySessionPolicy.Contains("_activeSession.Activate", StringComparison.Ordinal)
                  && !applySessionPolicy.Contains("TryEnsureNodeOwner", StringComparison.Ordinal)
                  && scan.Contains("_sources.Clear();", StringComparison.Ordinal)
                  && scan.Contains("FoxRunRos2SourceDiscovery.TryGet", StringComparison.Ordinal)
                  && dispatchesLegacySource,
                "default-native session demand is admitted for preflight while zero discovered native sources create no subscription binding");
        }

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
    }
}
