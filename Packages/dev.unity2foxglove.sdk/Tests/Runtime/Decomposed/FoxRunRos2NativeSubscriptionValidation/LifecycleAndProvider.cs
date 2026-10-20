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
        private static void VerifyIndependentManagerSubscriptionSession()
        {
            var manager = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var session = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.FoxRunSubscriptionSession.cs");
            var onEnable = PhaseValidationSourceHelpers.SourceMethod(manager, "private void OnEnable()");
            var update = PhaseValidationSourceHelpers.SourceMethod(manager, "private void Update()");
            var onDisable = PhaseValidationSourceHelpers.SourceMethod(manager, "private void OnDisable()");
            var onDestroy = PhaseValidationSourceHelpers.SourceMethod(manager, "private void OnDestroy()");
            var sync = PhaseValidationSourceHelpers.SourceMethod(
                session,
                "private void SyncFoxRunSubscriptionSession()");

            Check(OccursBefore(
                      onEnable,
                      "BeginFoxRunSubscriptionSessionIfNeeded();",
                      "StartServer();")
                  && update.Contains("SyncFoxRunSubscriptionSession();", StringComparison.Ordinal)
                  && sync.Contains("if (_enableFoxRunInbound)", StringComparison.Ordinal)
                  && sync.Contains("BeginFoxRunSubscriptionSessionIfNeeded();", StringComparison.Ordinal)
                  && sync.Contains("EndFoxRunSubscriptionSession();", StringComparison.Ordinal)
                  && OccursBefore(
                      onDisable,
                      "EndFoxRunSubscriptionSession,",
                      "StopServer(restoreLivePublishers: true),")
                  && OccursBefore(
                      onDestroy,
                      "EndFoxRunSubscriptionSession,",
                      "StopServer(restoreLivePublishers: true),"),
                "Manager enable, update, and teardown own the subscription session lifecycle");

            var startServer = PhaseValidationSourceHelpers.SourceMethod(
                server,
                "public void StartServer()");
            var stopServer = PhaseValidationSourceHelpers.SourceMethod(
                server,
                "private void StopServer(bool restoreLivePublishers)");
            Check(OccursBefore(
                      startServer,
                      "BeginFoxRunPublishSessionIfNeeded();",
                      "BeginFoxRunSubscriptionSessionIfNeeded();")
                  && OccursBefore(
                      startServer,
                      "BeginFoxRunSubscriptionSessionIfNeeded();",
                      "if (!_foxgloveOutputEnabled)")
                  && !stopServer.Contains("EndFoxRunSubscriptionSession", StringComparison.Ordinal)
                  && !stopServer.Contains("EndFoxRunPublishSession", StringComparison.Ordinal),
                "WebSocket start ensures both Manager-lifetime directional snapshots before the output gate and restart preserves them");
        }

        private static void VerifyLegacyProviderMigrationDefaultsToWebSocket()
        {
            var inbound = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var providers = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs");
            var managerMigration = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.FoxRunPolicyMigration.cs");
            var attribute = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Attributes/FoxRunAttribute.cs");

            Check(providers.Contains(
                      "_foxRunPublishTransportIds =",
                      StringComparison.Ordinal)
                  && providers.Contains(
                      "_foxRunSubscribeTransportId =",
                      StringComparison.Ordinal)
                  && providers.Contains(
                      "FoxgloveWebSocketTransport.Id",
                      StringComparison.Ordinal)
                  && providers.Contains(
                      "string.IsNullOrWhiteSpace(_foxRunSubscribeTransportId)",
                      StringComparison.Ordinal)
                  && inbound.Contains(
                      "DefaultFoxRunSubscribeTransportId =>",
                      StringComparison.Ordinal)
                  && inbound.Contains(
                      "ConfiguredFoxRunSubscribeTransportId",
                      StringComparison.Ordinal)
                  && managerMigration.Contains(
                      "ISerializationCallbackReceiver.OnAfterDeserialize()",
                      StringComparison.Ordinal)
                  && !managerMigration.Contains(
                      "_defaultFoxRunSubscriptionSource",
                      StringComparison.Ordinal)
                  && attribute.Contains(
                      "PublishTransportIds",
                      StringComparison.Ordinal)
                  && attribute.Contains(
                      "SubscribeTransportId",
                      StringComparison.Ordinal)
                  && !attribute.Contains("FoxRunEndpoint", StringComparison.Ordinal),
                "neutral Provider selections default to WebSocket and the retired serialized endpoint model is absent");
        }

        private static void VerifyExistingR2fuSinkRemainsOutboundOnly()
        {
            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var legacySink = Path.Combine(
                repoRoot,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Ros2R2FUTopicSink.cs");
            var provider = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2TransportProvider.cs");
            Check(!File.Exists(legacySink)
                  && provider.Contains(
                      "public sealed class FoxRunRos2TransportProvider",
                      StringComparison.Ordinal)
                  && provider.Contains(
                      "FoxRunTransportCapabilities.Publish",
                      StringComparison.Ordinal)
                  && provider.Contains(
                      "FoxRunTransportCapabilities.Subscribe",
                      StringComparison.Ordinal)
                  && provider.Contains(
                      "FoxRunTransportPublishResult Publish(",
                      StringComparison.Ordinal)
                  && provider.Contains(
                      "FoxRunTransportSubscribeResult Subscribe(",
                      StringComparison.Ordinal),
                "the retired outbound-only sink is gone and one R2FU Provider owns both directions");
        }

        private static void VerifyTypedGenerationAndNativeCatalogExclusion()
        {
            var emitter = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/Ros2InputDispatchEmitter.cs");
            var router = PhaseValidationSourceHelpers.ReadRegisteredCSharpSource(
                CorePackageRoot + "/Runtime/Components/FoxRun/FoxRunInputRouter.cs",
                CorePackageRoot + "/Runtime/Components/FoxRun/Decomposed/FoxRunInputRouter/Dispatch.cs",
                CorePackageRoot + "/Runtime/Components/FoxRun/Decomposed/FoxRunInputRouter/Lifetime.cs");
            var catalog = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/FoxRun/FoxRunSubscriptionCatalog.cs");

            var registerIndex = emitter.IndexOf(
                ": \"Register<\" + typeName + \">(\"",
                StringComparison.Ordinal);
            var copyIndex = emitter.IndexOf(
                "static (source, budget) => __FoxRunRos2Copy_",
                registerIndex < 0 ? 0 : registerIndex,
                StringComparison.Ordinal);
            var applyIndex = emitter.IndexOf(
                "owned => __FoxRunRos2Apply_",
                registerIndex < 0 ? 0 : registerIndex,
                StringComparison.Ordinal);
            Check(registerIndex >= 0
                  && copyIndex > registerIndex
                  && applyIndex > copyIndex
                  && !emitter.Contains("MakeGenericMethod", StringComparison.Ordinal)
                  && !emitter.Contains("Activator", StringComparison.Ordinal)
                  && !emitter.Contains("dynamic", StringComparison.Ordinal)
                  && !emitter.Contains("EnqueueRaw", StringComparison.Ordinal)
                  && emitter.Contains(".TryEnqueueOwnedAfterAdmission(", StringComparison.Ordinal),
                "generated native registration is closed-generic and supplies owned copy before apply without raw enqueue");

            Check(router.Contains("info.SubscribeTransportId", StringComparison.Ordinal)
                  && router.Contains("!info.SupportsWebSocket", StringComparison.Ordinal)
                  && router.Contains(
                      "FoxgloveWebSocketTransport.Id",
                      StringComparison.Ordinal)
                  && catalog.Contains(
                      "selectedIds.Length != 1",
                      StringComparison.Ordinal)
                  && catalog.Contains(
                      "FoxgloveWebSocketTransport.Id",
                      StringComparison.Ordinal)
                  && catalog.Contains(
                      "bindings.Any(binding => !binding.SupportsWebSocket)",
                      StringComparison.Ordinal)
                  && !catalog.Contains("\"cdr\"", StringComparison.OrdinalIgnoreCase),
                "byte router and subscription catalog exclude effective native contracts and never advertise cdr");
        }

    }
}
