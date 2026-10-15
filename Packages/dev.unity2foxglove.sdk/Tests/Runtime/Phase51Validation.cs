// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 51 regression coverage for maintainability, lifecycle,
// diagnostics, and optimization follow-up fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates Phase 51 maintainability and optimization closure.
    /// </summary>
    /// <remarks>Phase192 source sections: VerifySessionCachesPrioritizedTransportCapability.cs, RepoPath.cs</remarks>
    public static partial class Phase51Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 51: Maintainability and Optimization Closure ===");
            _passed = 0;

            VerifyMcapMagicCannotBeExternallyCorrupted();
            VerifyBinaryEncodingOversizedLengthsFailSafely();
            VerifyProtobufRegistrationCatchLogsWarning();
            VerifyServiceCallTerminalStateIsIdempotent();
            VerifyServiceUnregisterRemovesHandler();
            VerifyServiceDrainUsesSingleOwnershipPass();
            VerifyServiceRegistryRejectsDuplicatePendingCallIds();
            VerifyServiceRegistryTracksPendingCountsWithoutLinqScan();
            VerifyServiceDrainFailsRegisteredServiceWithoutHandler();
            VerifyServiceRequestJsonParsedOnce();
            VerifyParameterSubscriptionSemantics();
            VerifyParameterBroadcastIncludesClientZero();
            VerifyAssetRegistryDoesIoOutsideLock();
            VerifyRuntimeStartRollbackAfterTransportFailure();
            VerifyClearSessionClearsTransientServiceCallsOnly();
            VerifyBinaryPriorityContracts();
            VerifySessionCachesPrioritizedTransportCapability();
            VerifyClientIdAllocationCannotWrapToZero();
            VerifySubscribeBroadcastFailuresAreCaught();
            VerifyRecordingControllerDetachesSessionAtomically();
            VerifyManagerDocumentsStopServerDetachOrder();
            VerifySessionGraphMetadataFlushIsSeparatedAndDirtyGated();
            VerifyRuntimeTickLockBoundaryIsDocumented();
            VerifyReplayTickSupportsCallerOwnedBuffer();
            VerifyMcapReplayAvoidsPendingHeadRemovalAndDeadSeekGuard();
            VerifyMcapReplayHistoryCapAvoidsRepeatedFullSort();
            VerifyMcapReplayUsesLoggerForCrcWarnings();
            VerifyMcapWriterAvoidsPerRecordToArray();
            VerifyMcapRecorderAvoidsChunkToArrayCopy();
            VerifyMcapRecorderAllChannelWriteStatesTracksSeenInline();
            VerifyConnectionGraphSnapshotAvoidsLinqInsideLock();
            VerifySubscriptionRegistryRemoveChannelUsesReverseIndex();
            VerifyUnityAllocationFixes();
            VerifySourceEmitterUsesThisAccess();
            VerifyFoxRunMixedPolicyDiagnostic();
            VerifyFileOrganizationDecisionIsRecorded();

            Console.WriteLine($"Phase 51: {_passed} checks passed.");
        }

        private static void VerifyMcapMagicCannotBeExternallyCorrupted()
        {
            var externalMagic = McapWriter.Magic;
            externalMagic[0] = 0x00;

            using var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
                writer.WriteMagic();

            var bytes = ms.ToArray();
            Check(bytes.Length >= McapWriter.MagicLength && bytes[0] == 0x89,
                "51A-1: mutating McapWriter.Magic from caller code cannot corrupt future writes");
            Check(McapBinaryReader.MatchesMagic(bytes, 0),
                "51A-2: magic matcher uses immutable internal magic bytes");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Writer/McapWriter.cs");
            Check(!source.Contains("public static readonly byte[] Magic"),
                "51A-3: McapWriter no longer exposes a public mutable static byte array");
        }

        private static void VerifyBinaryEncodingOversizedLengthsFailSafely()
        {
            var serviceFrame = new byte[13];
            serviceFrame[0] = ClientOpcode.ServiceCallRequest;
            BinaryEncoding.WriteU32LE(serviceFrame, 9, 0xFFFFFFF8U);
            Check(!ThrowsAny(() => BinaryEncoding.TryDecodeClientServiceCallRequest(
                    serviceFrame, out _, out _, out _, out _)),
                "51A-4: oversized service encoding length does not throw");
            Check(!BinaryEncoding.TryDecodeClientServiceCallRequest(serviceFrame, out _, out _, out _, out _),
                "51A-5: oversized service encoding length returns false");

            var playbackFrame = new byte[19];
            playbackFrame[0] = ClientOpcode.PlaybackControlRequest;
            BinaryEncoding.WriteU32LE(playbackFrame, 15, 0xFFFFFFF8U);
            Check(!ThrowsAny(() => BinaryEncoding.TryDecodePlaybackControlRequest(
                    playbackFrame, out _, out _, out _, out _, out _)),
                "51A-6: oversized playback request id length does not throw");
            Check(!BinaryEncoding.TryDecodePlaybackControlRequest(playbackFrame, out _, out _, out _, out _, out _),
                "51A-7: oversized playback request id length returns false");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Protocol/BinaryEncoding.cs");
            Check(source.Contains("encodingLength > int.MaxValue") && source.Contains("idLen > int.MaxValue"),
                "51A-8: BinaryEncoding guards uint lengths before int casts");
        }

        private static void VerifyProtobufRegistrationCatchLogsWarning()
        {
            var source = PhaseValidationSourceHelpers.ReadFoxgloveRuntimeSources();
            Check(source.Contains("catch (Exception ex)") && source.Contains("_logger.LogWarning")
                  && source.Contains("protobuf", StringComparison.OrdinalIgnoreCase),
                "51A-9: protobuf registration failures are logged as non-fatal warnings");
        }

        private static void VerifyServiceCallTerminalStateIsIdempotent()
        {
            var call = new FoxgloveServiceCall();
            call.Complete("json", new byte[] { 1 });
            call.Fail("late failure");
            call.Complete("json", new byte[] { 2 });

            Check(call.IsCompleted && call.FailureMessage == null
                  && call.ResponsePayload.Length == 1 && call.ResponsePayload[0] == 1,
                "51B-1: first service terminal state wins");
        }

        private static void VerifyServiceUnregisterRemovesHandler()
        {
            var registry = new FoxgloveServiceRegistry();
            var id = registry.Register(new ServiceDescriptor { Name = "/demo", Type = "demo" },
                _ => JToken.Parse("{}"));
            Check(registry.GetHandler(id) != null, "51B-2: registered service handler is visible before unregister");
            Check(registry.Unregister(id), "51B-3: service unregister reports success");
            Check(registry.GetHandler(id) == null, "51B-4: unregister removes service handler delegate");
        }

        private static void VerifyServiceDrainUsesSingleOwnershipPass()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Services/FoxgloveServiceRegistry.cs");
            var drain = ExtractMethodBody(source, "DrainCompleted");
            var drainTo = ExtractMethodBody(source, "DrainCompletedTo");
            Check(!drain.Contains("_pending.ToList()"),
                "51B-5: DrainCompleted does not allocate LINQ snapshots while holding ownership");
            Check((drain + drainTo).Contains("RemovePendingCall"),
                "51B-6: DrainCompleted removes completed calls through the pending ownership helper");
        }

        private static void VerifyServiceRegistryRejectsDuplicatePendingCallIds()
        {
            var registry = new FoxgloveServiceRegistry();
            var firstPayload = Encoding.UTF8.GetBytes("{\"first\":true}");
            var secondPayload = Encoding.UTF8.GetBytes("{\"second\":true}");

            Check(registry.TryEnqueue(1, 10, 7, "json", firstPayload, out var first, out var firstError)
                  && first != null && firstError == null,
                "51B-6a: first service call enqueue succeeds");
            Check(!registry.TryEnqueue(2, 10, 7, "json", secondPayload, out var duplicate, out var duplicateError)
                  && duplicate == null && duplicateError.Contains("Duplicate", StringComparison.OrdinalIgnoreCase),
                "51B-6b: duplicate service call id for the same client is rejected");

            registry.CompleteResponse(7, 10, "json", Encoding.UTF8.GetBytes("{}"));
            var drained = registry.DrainCompleted();
            Check(drained.Count == 1
                  && drained[0].ServiceId == 1
                  && Encoding.UTF8.GetString(drained[0].Payload).Contains("first"),
                "51B-6c: duplicate enqueue cannot overwrite the original pending call");
        }

        private static void VerifyServiceRegistryTracksPendingCountsWithoutLinqScan()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Services/FoxgloveServiceRegistry.cs");
            Check(source.Contains("_pendingCountByClient"),
                "51B-6d: service registry maintains per-client pending call counts");
            Check(!source.Contains("_pending.Keys.Count"),
                "51B-6e: service enqueue no longer scans all pending keys under lock");
        }

        private static void VerifyServiceDrainFailsRegisteredServiceWithoutHandler()
        {
            var transport = new Phase51FakeTransport();
            var services = new FoxgloveServiceRegistry();
            var serviceId = services.Register(new ServiceDescriptor { Name = "/phase51/no_handler", Type = "phase51.NoHandler" });
            var session = new FoxgloveSession("phase51", transport, new FixedClock(10),
                new DefaultSchemaRegistry(), null, new FoxgloveParameterStore(), services);

            transport.RaiseBinary(3, BuildServiceCallFrame(serviceId, 777, "{}"));
            Check(services.GetPendingCalls().Count == 1,
                "51B-6f: registered service without handler can receive a pending call");

            session.DrainServiceCalls();

            var failure = transport.SentText
                .Select(t => JObject.Parse(t.Text))
                .FirstOrDefault(j => j["op"]?.ToString() == "serviceCallFailure");
            Check(failure != null && failure["message"]?.ToString().Contains("No handler", StringComparison.OrdinalIgnoreCase) == true,
                "51B-6g: service drain immediately fails calls with no registered handler");
            Check(services.GetPendingCalls().Count == 0,
                "51B-6h: no-handler service failures are removed from the pending queue during drain");
        }

        private static void VerifyServiceRequestJsonParsedOnce()
        {
            var callSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Services/FoxgloveServiceCall.cs");
            var registrySource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Services/FoxgloveServiceRegistry.cs");
            var sessionSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Services.cs");
            var handle = ExtractMethodBody(sessionSource, "private void HandleServiceCallRequest");
            var drain = ExtractMethodBody(sessionSource, "public void DrainServiceCalls");

            Check(callSource.Contains("JsonPayload"),
                "51B-6i: service call model can carry an already parsed JSON payload");
            Check(registrySource.Contains("JToken jsonPayload") && registrySource.Contains("JsonPayload = jsonPayload"),
                "51B-6j: service registry stores the parsed service request payload with the pending call");
            Check(handle.Contains("parsedPayload = JToken.Parse") && handle.Contains("parsedPayload"),
                "51B-6k: service request handler parses JSON once at ingress");
            Check(drain.Contains("call.JsonPayload") && !drain.Contains("Encoding.UTF8.GetString(call.Payload)")
                  && !drain.Contains("JToken.Parse(payloadStr)"),
                "51B-6l: service drain reuses parsed JSON instead of decoding and parsing the payload again");
        }

        private static void VerifyParameterSubscriptionSemantics()
        {
            var subs = new ParameterSubscriptionRegistry();
            subs.Subscribe(10, null);
            subs.Subscribe(10, new[] { "/only" });
            Check(subs.IsSubscribed(10, "/other"),
                "51B-7: named subscribe after subscribe-all keeps all-subscription semantics");

            subs.Unsubscribe(10, new[] { "/only" });
            Check(subs.IsSubscribed(10, "/other"),
                "51B-8: named unsubscribe does not erase subscribe-all semantics");

            subs.Unsubscribe(10, null);
            Check(!subs.IsSubscribed(10, "/other"),
                "51B-9: empty unsubscribe clears all parameter subscriptions");
        }

        private static void VerifyParameterBroadcastIncludesClientZero()
        {
            var transport = new Phase51FakeTransport();
            var parameters = new FoxgloveParameterStore();
            parameters.Register("/phase51", JToken.FromObject(1), "number", true);
            var session = new FoxgloveSession("phase51", transport, new FixedClock(1),
                new DefaultSchemaRegistry(), null, parameters, new FoxgloveServiceRegistry());

            transport.RaiseText(0, JsonConvert.SerializeObject(new SubscribeParameterUpdates
            {
                ParameterNames = new List<string> { "/phase51" }
            }));
            session.BroadcastParameterValues(new[] { "/phase51" });

            Check(transport.SentText.Any(t => t.ClientId == 0 && t.Text.Contains("phase51")),
                "51B-10: runtime-originated parameter broadcasts can reach client id 0");
        }

        private static void VerifyAssetRegistryDoesIoOutsideLock()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Assets/FoxgloveAssetRegistry.cs");
            var lockBlocks = Regex.Matches(source, @"lock\s*\(_lock\)\s*\{[\s\S]*?\n\s*\}");
            var lockText = string.Join("\n", lockBlocks.Cast<Match>().Select(m => m.Value));
            Check(!lockText.Contains("File.Exists") && !lockText.Contains("Directory.Exists")
                  && !lockText.Contains("new FileInfo") && !lockText.Contains("ReadAllBytes"),
                "51B-11: asset registry does filesystem IO outside root-map lock");
        }

        private static void VerifyRuntimeStartRollbackAfterTransportFailure()
        {
            var transport = new ThrowingStartTransport();
            var runtime = new FoxgloveRuntime(transport, new FixedClock(1), new DefaultSchemaRegistry());

            Check(Throws<InvalidOperationException>(() => runtime.Start("phase51", "127.0.0.1", 0)),
                "51B-12: failing transport Start propagates the startup failure");
            Check(runtime.Session == null && !runtime.IsRunning,
                "51B-13: failed runtime Start rolls back active session state");
            Check(transport.HandlerBalance == 0,
                "51B-14: failed runtime Start disposes session event subscriptions");

            transport.ThrowOnStart = false;
            runtime.Start("phase51", "127.0.0.1", 0);
            Check(runtime.Session != null && runtime.IsRunning,
                "51B-15: runtime can start successfully after rollback");
            runtime.Dispose();

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var start = ExtractMethodBody(source, "public void Start");
            var cleanup = ExtractMethodBody(source, "private void RunStopCleanup");
            var detachIndex = cleanup.IndexOf("_recording.DetachFromSession", StringComparison.Ordinal);
            var disposeIndex = cleanup.IndexOf("session?.Dispose()", StringComparison.Ordinal);
            Check(start.Contains("RunStopCleanup(ref cleanupFailure, session)", StringComparison.Ordinal)
                  && cleanup.Contains("RuntimeStopCleanupStep.Recording", StringComparison.Ordinal)
                  && cleanup.Contains("RuntimeStopCleanupStep.Session", StringComparison.Ordinal)
                  && detachIndex >= 0 && disposeIndex > detachIndex,
                "51B-15b: runtime Start rollback runs recording detach before session disposal through retryable cleanup steps");
            Check(start.Contains("throw;", StringComparison.Ordinal)
                  && !start.Contains("ExceptionDispatchInfo.Capture(startException).Throw()", StringComparison.Ordinal)
                  && start.Contains("preserving the original Start exception", StringComparison.OrdinalIgnoreCase)
                  && cleanup.Contains("ref firstFailure", StringComparison.Ordinal),
                "51B-15c: runtime Start rollback preserves the original startup exception while retaining cleanup failures");
        }

        private static void VerifyClearSessionClearsTransientServiceCallsOnly()
        {
            var registry = new FoxgloveServiceRegistry();
            var id = registry.Register(new ServiceDescriptor { Name = "/phase51", Type = "phase51" },
                _ => JToken.Parse("{}"));
            registry.Enqueue(id, 99, 7, "json", Encoding.UTF8.GetBytes("{}"));

            var session = new FoxgloveSession("phase51", new Phase51FakeTransport(), new FixedClock(1),
                new DefaultSchemaRegistry(), null, new FoxgloveParameterStore(), registry);
            session.ClearSession();

            Check(registry.GetById(id) != null,
                "51B-16: ClearSession keeps runtime-owned service definitions");
            Check(registry.GetPendingCalls().Count == 0,
                "51B-17: ClearSession removes transient pending service calls");
        }

        private static void VerifyBinaryPriorityContracts()
        {
            var transport = new Phase51FakeTransport();
            var services = new FoxgloveServiceRegistry();
            var serviceId = services.Register(new ServiceDescriptor { Name = "/phase51", Type = "phase51" },
                _ => JToken.Parse("{\"ok\":true}"));
            var session = new FoxgloveSession("phase51", transport, new FixedClock(10),
                new DefaultSchemaRegistry(), null, new FoxgloveParameterStore(), services);

            transport.RaiseBinary(3, BuildServiceCallFrame(serviceId, 123, "{}"));
            session.DrainServiceCalls();
            Check(transport.ControlBinary.Count == 1 && transport.DataBinary.Count == 0,
                "51B-18: service responses use reliable control binary priority");

            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/data", Encoding = "json" });
            transport.RaiseText(3, JsonConvert.SerializeObject(new SubscribeMessage
            {
                Subscriptions = new List<Subscription> { new Subscription { Id = 44, ChannelId = 1 } }
            }));
            session.Publish(1, Encoding.UTF8.GetBytes("{}"), 10);
            Check(transport.DataBinary.Count == 1,
                "51B-19: live MessageData publish uses data binary priority");

            var transportSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/Abstractions/IFoxgloveTransport.cs");
            var backendSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs");
            Check(transportSource.Contains("BroadcastDataBinary")
                  && Regex.IsMatch(backendSource, @"BroadcastBinary[\s\S]*FramePriority\.Control")
                  && Regex.IsMatch(backendSource, @"BroadcastDataBinary[\s\S]*FramePriority\.Data"),
                "51B-20: broadcast binary API distinguishes reliable control from droppable data");
        }
    }
}
