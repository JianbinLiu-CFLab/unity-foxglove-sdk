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
        private static void VerifyDiagnosticIdSemanticRanges()
        {
            var diagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.Diagnostics.cs");
            var unshipped = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/AnalyzerReleases.Unshipped.md");
            var r2fuDiagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/FoxRunR2fuDiagnostics.cs");
            var r2fuUnshipped = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/AnalyzerReleases.Unshipped.md");
            var subscribe = new[]
            {
                new KeyValuePair<string, string>("UnsupportedInboundShape", "FOXRUN200"),
                new KeyValuePair<string, string>("InboundNaming", "FOXRUN202"),
                new KeyValuePair<string, string>("InboundTargetNotWritable", "FOXRUN203"),
                new KeyValuePair<string, string>("SharedInboundTargetNotWritable", "FOXRUN203"),
                new KeyValuePair<string, string>("InvalidStreamDeclaration", "FOXRUN215"),
                new KeyValuePair<string, string>("StreamInitializerMissing", "FOXRUN216"),
            };
            var infrastructure = new[]
            {
                new KeyValuePair<string, string>("InvalidFoxRunFlow", "FOXRUN600"),
                new KeyValuePair<string, string>("InvalidEncoding", "FOXRUN602"),
                new KeyValuePair<string, string>("InvalidProtobufFieldNumber", "FOXRUN603"),
                new KeyValuePair<string, string>("MixedTopicEncoding", "FOXRUN604"),
                new KeyValuePair<string, string>("DuplicateProtobufFieldNumber", "FOXRUN605"),
                new KeyValuePair<string, string>("TriggerRateConflict", "FOXRUN609"),
                new KeyValuePair<string, string>("GeneratedMethodConflict", "FOXRUN610"),
                new KeyValuePair<string, string>("UnsupportedMessagePackShape", "FOXRUN616"),
                new KeyValuePair<string, string>("MessagePackProtobufFieldNumber", "FOXRUN617"),
                new KeyValuePair<string, string>("MessagePackInboundTopology", "FOXRUN618"),
                new KeyValuePair<string, string>("MessagePackSchedule", "FOXRUN619"),
                new KeyValuePair<string, string>("InvalidTransportSelection", "FOXRUN620"),
                new KeyValuePair<string, string>("InvalidDirectionalTransport", "FOXRUN621"),
                new KeyValuePair<string, string>("InvalidDeliveryPolicy", "FOXRUN622"),
            };
            var retiredClosedTransport = new[]
            {
                "FOXRUN204",
                "FOXRUN207",
                "FOXRUN208",
                "FOXRUN209",
                "FOXRUN210",
                "FOXRUN211",
                "FOXRUN212",
                "FOXRUN402",
                "FOXRUN606",
                "FOXRUN607",
                "FOXRUN608",
                "FOXRUN611",
                "FOXRUN612",
                "FOXRUN613",
                "FOXRUN614",
                "FOXRUN615",
            };
            var r2fuOwned = Enumerable.Range(1, 16)
                .Select(number => "FOXR2F" + number.ToString("000"))
                .ToArray();

            Check(diagnostics.Contains("#region FoxRun publish diagnostics (FOXRUN001-199)", StringComparison.Ordinal)
                  && diagnostics.Contains("#region FoxRun Subscribe diagnostics (FOXRUN200-399)", StringComparison.Ordinal)
                  && diagnostics.Contains("#region FoxRun cross-direction diagnostics (FOXRUN600+)", StringComparison.Ordinal)
                  && !diagnostics.Contains("PublishAndSubscribe diagnostics", StringComparison.Ordinal),
                "core FoxRun diagnostics retain only neutral publish, subscribe, and cross-direction ranges");

            var expected = subscribe.Concat(infrastructure).ToArray();
            Check(expected.All(pair => HasDiagnosticDescriptorId(diagnostics, pair.Key, pair.Value))
                  && expected.All(pair => unshipped.Contains(pair.Value + " | FoxRun |", StringComparison.Ordinal))
                  && !diagnostics.Contains("IgnoredSubscribePolicy", StringComparison.Ordinal)
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN201",
                      "Retired before release; subscription policy now applies symmetrically and this ID is permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN400",
                      "Retired before release; valid PublishAndSubscribe is an explicit flow and ownership guidance belongs in documentation, so this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN401",
                      "Retired before release; directional profiles resolve full-duplex encodings independently and this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN205",
                      "Retired before release; Ros2Native is now a legal Subscribe or PublishAndSubscribe Source and this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN206",
                      "Retired before release; directional Encoding legality now uses FOXRUN612 and this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN214",
                      "Retired before release; directional Source legality now uses FOXRUN612 and this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN213",
                      "Retired before release; explicit QoS without a ROS 2 direction now fails with FOXRUN614 and this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN601",
                      "Retired before release with the removed Unless declaration and remains permanently reserved.")
                  && unshipped.Contains(
                      "FOXRUN204, FOXRUN207-FOXRUN212, FOXRUN402, FOXRUN606-FOXRUN608, and FOXRUN611-FOXRUN615 are retired before release with the removed closed transport model and remain permanently reserved.",
                      StringComparison.Ordinal)
                  && retiredClosedTransport.All(id =>
                      !diagnostics.Contains(id, StringComparison.Ordinal))
                  && r2fuOwned.All(id =>
                      r2fuDiagnostics.Contains(id, StringComparison.Ordinal)
                      && r2fuUnshipped.Contains(
                          id + " | FoxRun.R2FU |",
                          StringComparison.Ordinal))
                  && !r2fuUnshipped.Contains("FOXRUN", StringComparison.Ordinal),
                "core reserves removed ROS diagnostics while the R2FU analyzer owns executable FOXR2F diagnostics");
        }

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
            var binding = PhaseValidationSourceHelpers.ReadFoxRunRos2SubscriptionBindingSources();
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

    }
}
