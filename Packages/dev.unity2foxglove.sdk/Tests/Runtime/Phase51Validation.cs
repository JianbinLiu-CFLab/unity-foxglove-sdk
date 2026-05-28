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
    public static class Phase51Validation
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
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
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
            Check(!drain.Contains("_pending.ToList()"),
                "51B-5: DrainCompleted does not allocate LINQ snapshots while holding ownership");
            Check(drain.Contains("RemovePendingCall"),
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

            first.Complete("json", Encoding.UTF8.GetBytes("{}"));
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
            var catchIndex = start.IndexOf("catch", StringComparison.Ordinal);
            var detachIndex = start.IndexOf("_recording.DetachFromSession()", StringComparison.Ordinal);
            Check(start.Contains("finally") && detachIndex > catchIndex,
                "51B-15b: runtime Start rollback detaches recording in a cleanup path that survives dispose failures");
            Check(start.Contains("startup dispose failure", StringComparison.OrdinalIgnoreCase),
                "51B-15c: runtime Start rollback preserves the original startup exception if session dispose also fails");
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

            var transportSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/IFoxgloveTransport.cs");
            var backendSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs");
            Check(transportSource.Contains("BroadcastDataBinary")
                  && Regex.IsMatch(backendSource, @"BroadcastBinary[\s\S]*FramePriority\.Control")
                  && Regex.IsMatch(backendSource, @"BroadcastDataBinary[\s\S]*FramePriority\.Data"),
                "51B-20: broadcast binary API distinguishes reliable control from droppable data");
        }

        private static void VerifySessionCachesPrioritizedTransportCapability()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var publish = ExtractMethodBody(source, "public void Publish(uint channelId, byte[] payload, ulong logTimeNs)");
            var replay = ExtractMethodBody(source, "internal void PublishReplay");

            Check(source.Contains("_prioritizedTransport") && source.Contains("_transport as IPrioritizedFoxgloveTransport"),
                "51B-20a: FoxgloveSession caches prioritized transport capability at construction");
            Check(!publish.Contains("_transport is IPrioritizedFoxgloveTransport")
                  && publish.Contains("_prioritizedTransport"),
                "51B-20b: live publish hot path uses cached prioritized transport capability");
            Check(!replay.Contains("_transport is IPrioritizedFoxgloveTransport")
                  && replay.Contains("_prioritizedTransport"),
                "51B-20c: replay publish hot path uses cached prioritized transport capability");
        }

        private static void VerifyClientIdAllocationCannotWrapToZero()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs");
            Check(source.Contains("long _nextClientId") && source.Contains("AllocateClientId")
                  && source.Contains("uint.MaxValue") && !source.Contains("(uint)Interlocked.Increment(ref _nextClientId)"),
                "51B-21: ManagedWsBackend allocates nonzero client ids without uint wraparound");
        }

        private static void VerifySubscribeBroadcastFailuresAreCaught()
        {
            var transport = new Phase51ThrowingSendTransport();
            var session = new FoxgloveSession("phase51", transport, new FixedClock(1), new DefaultSchemaRegistry());
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/phase51/topic", Encoding = "json" });

            transport.RaiseText(7, "{\"op\":\"subscribeConnectionGraph\"}");
            transport.ThrowOnSendText = true;

            Check(!ThrowsAny(() => transport.RaiseText(7, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":11,\"channelId\":1}]}")),
                "51B-22: subscribe catches connection graph broadcast failures");
            Check(transport.SendTextThrowCount > 0,
                "51B-23: subscribe test exercised the throwing graph broadcast path");

            Check(!ThrowsAny(() => transport.RaiseText(7, "{\"op\":\"unsubscribe\",\"subscriptionIds\":[11]}")),
                "51B-24: unsubscribe catches connection graph broadcast failures");
        }

        private static void VerifyRecordingControllerDetachesSessionAtomically()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/RecordingController.cs");
            Check(source.Contains("Interlocked.Exchange(ref _session, null)"),
                "51B-25: RecordingController detaches the session with an atomic exchange");
            Check(source.Contains("Volatile.Write(ref _session, session)")
                  && source.Contains("Volatile.Write(ref _session, null)"),
                "51B-26: RecordingController publishes session attach/detach state with volatile writes");
        }

        private static void VerifyManagerDocumentsStopServerDetachOrder()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var stop = ExtractMethodBody(source, "private void StopServer");
            var stopIndex = source.IndexOf("private void StopServer", StringComparison.Ordinal);
            var detachIndex = source.IndexOf("transport.OnClientConnected -=", stopIndex, StringComparison.Ordinal);
            var runtimeStopIndex = source.IndexOf("_runtime.Stop()", stopIndex, StringComparison.Ordinal);
            Check(stop.Contains("capture and detach") || stop.Contains("Capture and detach"),
                "51B-27: StopServer documents that callbacks are detached before runtime Stop clears Session");
            Check(detachIndex >= 0 && runtimeStopIndex >= 0 && detachIndex < runtimeStopIndex,
                "51B-28: StopServer detaches transport callbacks before runtime Stop");
        }

        private static void VerifySessionGraphMetadataFlushIsSeparatedAndDirtyGated()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionGraphHandler.cs");
            Check(source.Contains("FlushMetadataSnapshotIfDirty"),
                "51B-29: SessionGraphHandler separates websocket broadcast from MCAP metadata flush");
            var flush = ExtractMethodBody(source, "FlushMetadataSnapshotIfDirty");
            Check(flush.Contains("Interlocked.CompareExchange(ref _dirty, 0, 1)")
                  && flush.Contains("WriteMetadata")
                  && flush.Contains("Volatile.Write(ref _dirty, 1)"),
                "51B-30: graph metadata writes remain dirty-gated");
        }

        private static void VerifyRuntimeTickLockBoundaryIsDocumented()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var tick = ExtractMethodBody(source, "public void Tick");
            Check(tick.Contains("broadcastLiveTime") && tick.Contains("session.BroadcastTime()"),
                "51B-31: live time broadcast runs outside the playback control lock");
            Check(tick.Contains("Replay work intentionally stays inside _playbackControlLock"),
                "51B-32: replay lock boundary documents why snapshot publishing remains serialized");
        }

        private static void VerifyReplayTickSupportsCallerOwnedBuffer()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            Check(source.Contains("Tick(ulong nowNs, List<McapMessage>") && source.Contains("result.Clear()"),
                "51C-1: McapReplayEngine supports caller-owned Tick buffer reuse");

            var replaySource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");
            Check(replaySource.Contains("_replayTickBuffer") && replaySource.Contains(".Tick(nowNs, _replayTickBuffer)"),
                "51C-2: ReplayController reuses a replay tick message buffer");
        }

        private static void VerifyMcapReplayAvoidsPendingHeadRemovalAndDeadSeekGuard()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var tick = ExtractMethodBody(source, "public List<McapMessage> Tick(ulong nowNs, List<McapMessage> result)");
            var popPending = ExtractMethodBody(source, "private McapMessage PopPending");
            var seek = ExtractMethodBody(source, "public void Seek");
            Check(source.Contains("_pendingHeadIndex"),
                "51C-2c: McapReplayEngine tracks a pending head index for O(1) front dequeue");
            Check(!tick.Contains("_pending.RemoveAt(0)") && !popPending.Contains("RemoveAt(0)"),
                "51C-2d: replay pending dequeue path avoids List.RemoveAt(0)");
            Check(!seek.Contains("_currentChunkIdx < -1"),
                "51C-2e: replay seek no longer carries an unreachable chunk-index guard");
        }

        private static void VerifyMcapReplayHistoryCapAvoidsRepeatedFullSort()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var addHistory = ExtractMethodBody(source, "private static void AddHistoryMessage");
            Check(addHistory.Contains("FindHistoryInsertIndex") && !addHistory.Contains("result.Sort(CompareMessages)"),
                "51C-2f: capped history retention inserts chronologically instead of sorting on every overflow");
        }

        private static void VerifyMcapReplayUsesLoggerForCrcWarnings()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            Check(source.Contains("IFoxgloveLogger") && source.Contains("_logger.LogWarning"),
                "51C-2g: McapReplayEngine routes CRC warnings through IFoxgloveLogger");
            Check(!source.Contains("Console.Error.WriteLine"),
                "51C-2h: McapReplayEngine does not write CRC warnings directly to stderr");
        }

        private static void VerifyMcapWriterAvoidsPerRecordToArray()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Writer/McapWriter.cs");
            Check(source.Contains("WriteBufferedRecord") && source.Contains("TryGetBuffer"),
                "51C-2a: McapWriter writes buffered records without forcing MemoryStream.ToArray");

            var commonRecordMethods = new[]
            {
                "public void WriteHeader",
                "public void WriteSchema",
                "public void WriteChannel",
                "public void WriteMessage",
                "public void WriteMetadata",
                "public void WriteDataEnd",
                "public void WriteFooter",
                "public void WriteSummaryOffset"
            };
            foreach (var method in commonRecordMethods)
            {
                var body = ExtractMethodBody(source, method);
                Check(!body.Contains("new MemoryStream") && !body.Contains(".ToArray()"),
                    $"51C-2b: {method} avoids per-record MemoryStream allocation and ToArray copy");
            }
        }

        private static void VerifyMcapRecorderAvoidsChunkToArrayCopy()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapRecorder.cs");
            Check(!source.Contains("_chunkBuf.ToArray()") && source.Contains("TryGetBuffer"),
                "51C-3: McapRecorder FlushChunk avoids the raw chunk ToArray copy");
        }

        private static void VerifyMcapRecorderAllChannelWriteStatesTracksSeenInline()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapRecorder.cs");
            var method = ExtractMethodBody(source, "IEnumerable<ChannelWriteState> AllChannelWriteStates");
            Check(method.Contains("if (seen.Add(m.McapId))")
                  && !Regex.IsMatch(method, @"foreach\s*\(var\s+m\s+in\s+_chMap\.Values\)\s*seen\.Add\(m\.McapId\)"),
                "51C-3b: McapRecorder tracks channel ids during the first AllChannelWriteStates pass");
        }

        private static void VerifyConnectionGraphSnapshotAvoidsLinqInsideLock()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/ConnectionGraphRegistry.cs");
            var snapshot = ExtractMethodBody(source, "public ConnectionGraphUpdate GetSnapshot");
            Check(!snapshot.Contains(".Select(") && !snapshot.Contains(".ToList()"),
                "51C-3c: connection graph snapshot avoids LINQ allocation while holding the registry lock");
            Check(source.Contains("CopyTopology"),
                "51C-3d: connection graph snapshot uses explicit topology copy helpers");
        }

        private static void VerifySubscriptionRegistryRemoveChannelUsesReverseIndex()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/SubscriptionRegistry.cs");
            var removeChannel = ExtractMethodBody(source, "public List<(uint clientId, uint subscriptionId, uint channelId)> RemoveChannel");
            Check(source.Contains("_byChannel"),
                "51C-3e: subscription registry maintains a channel-to-subscriber reverse index");
            Check(removeChannel.Contains("_byChannel.TryGetValue")
                  && !removeChannel.Contains("new List<uint>")
                  && !removeChannel.Contains("foreach (var (clientId, subs) in _clients)"),
                "51C-3f: RemoveChannel removes via reverse index without per-client removal buffers");
        }

        private static void VerifyUnityAllocationFixes()
        {
            var cube = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveSceneCubePublisher.cs");
            Check(cube.Contains("_renderer") && cube.Contains("_propertyBlock")
                  && ExtractMethodBody(cube, "ApplyColorToRenderer").Contains("_propertyBlock"),
                "51C-4: SceneCube publisher caches renderer and MaterialPropertyBlock");

            var laser = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs");
            Check(laser.Contains("_syntheticRanges") && laser.Contains("BuildSyntheticRanges"),
                "51C-5: LaserScan publisher caches synthetic ranges");

            var demo = ReadRepoText("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs");
            Check(demo.Contains("_cachedCube") && demo.Contains("private GameObject FindCube()"),
                "51C-6: demo setup caches cube lookup");

            var hub = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            Check(hub.Contains("ScanIntervalSeconds") && hub.Contains("RegisterSource"),
                "51C-7: FoxRun hub has explicit registration hooks and named scan fallback interval");
        }

        private static void VerifySourceEmitterUsesThisAccess()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "Phase51Source", new[]
            {
                new FoxgloveSourceEmitter.TopicMember("Position", "UnityEngine.Vector3", "/phase51", 10f, ""),
                new FoxgloveSourceEmitter.TopicMember("_health", "System.Single", "/phase51/health", 10f, "")
            });

            Check(source.Contains("[\"x\"] = this.Position.x") && source.Contains("[\"health\"] = this._health"),
                "51D-1: generated value expressions use explicit this. member access");
        }

        private static void VerifyFoxRunMixedPolicyDiagnostic()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            Check(source.Contains("FOXRUN005") && source.Contains("Mixed") && source.Contains("PublishMode"),
                "51D-2: source generator reports mixed same-topic publish policy diagnostics");
        }

        private static void VerifyFileOrganizationDecisionIsRecorded()
        {
            var sceneCubePublisher = RepoPath("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveSceneCubePublisher.cs");
            var transformPublisher = RepoPath("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveTransformPublisher.cs");
            Check(File.Exists(sceneCubePublisher) && File.Exists(sceneCubePublisher + ".meta")
                && File.Exists(transformPublisher) && File.Exists(transformPublisher + ".meta"),
                "51D-3: file organization decision preserves publisher file locations and Unity .meta files");
        }

        private static byte[] BuildServiceCallFrame(uint serviceId, uint callId, string json)
        {
            var enc = Encoding.UTF8.GetBytes("json");
            var payload = Encoding.UTF8.GetBytes(json);
            var frame = new byte[13 + enc.Length + payload.Length];
            frame[0] = ClientOpcode.ServiceCallRequest;
            BinaryEncoding.WriteU32LE(frame, 1, serviceId);
            BinaryEncoding.WriteU32LE(frame, 5, callId);
            BinaryEncoding.WriteU32LE(frame, 9, (uint)enc.Length);
            Buffer.BlockCopy(enc, 0, frame, 13, enc.Length);
            Buffer.BlockCopy(payload, 0, frame, 13 + enc.Length, payload.Length);
            return frame;
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            var index = source.IndexOf(methodName, StringComparison.Ordinal);
            if (index < 0) return "";
            var brace = source.IndexOf('{', index);
            if (brace < 0) return "";
            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(brace, i - brace + 1);
                }
            }
            return source.Substring(brace);
        }

        private static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath));
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repo root.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static bool ThrowsAny(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is TException)
            {
                return true;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }

        private sealed class FixedClock : IFoxgloveClock
        {
            public FixedClock(ulong nowNs) => NowNs = nowNs;
            public ulong NowNs { get; }
        }

        private sealed class Phase51FakeTransport : IFoxgloveTransport, IPrioritizedFoxgloveTransport
        {
            public readonly List<(uint ClientId, string Text)> SentText = new List<(uint, string)>();
            public readonly List<(uint ClientId, byte[] Data)> ControlBinary = new List<(uint, byte[])>();
            public readonly List<(uint ClientId, byte[] Data)> DataBinary = new List<(uint, byte[])>();
            public readonly List<byte[]> ControlBroadcastBinary = new List<byte[]>();
            public readonly List<byte[]> DataBroadcastBinary = new List<byte[]>();

            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void BroadcastText(string json) => SentText.Add((uint.MaxValue, json));
            public void BroadcastBinary(byte[] data) => ControlBroadcastBinary.Add(data);
            public void BroadcastDataBinary(byte[] data) => DataBroadcastBinary.Add(data);
            public void SendText(uint clientId, string json) => SentText.Add((clientId, json));
            public void SendBinary(uint clientId, byte[] data) => ControlBinary.Add((clientId, data));
            public void SendDataBinary(uint clientId, byte[] data) => DataBinary.Add((clientId, data));
            public void Dispose() => Stop();
            public void RaiseText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void RaiseBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
            public void RaiseConnected(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void RaiseDisconnected(uint clientId) => OnClientDisconnected?.Invoke(clientId);
        }

        private sealed class Phase51ThrowingSendTransport : IFoxgloveTransport
        {
            public bool ThrowOnSendText;
            public int SendTextThrowCount;
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json)
            {
                if (!ThrowOnSendText)
                    return;

                SendTextThrowCount++;
                throw new InvalidOperationException("phase51 send text failure");
            }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() => Stop();
            public void RaiseText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void RaiseBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
            public void RaiseConnected(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void RaiseDisconnected(uint clientId) => OnClientDisconnected?.Invoke(clientId);
        }

        private sealed class ThrowingStartTransport : IFoxgloveTransport
        {
            private int _handlerBalance;
            public bool ThrowOnStart = true;
            public int HandlerBalance => _handlerBalance;
            public bool IsRunning { get; private set; }

            private event Action<uint> ClientConnected;
            private event Action<uint> ClientDisconnected;
            private event Action<uint, string> TextReceived;
            private event Action<uint, byte[]> BinaryReceived;

            public event Action<uint> OnClientConnected
            {
                add { _handlerBalance++; ClientConnected += value; }
                remove { _handlerBalance--; ClientConnected -= value; }
            }

            public event Action<uint> OnClientDisconnected
            {
                add { _handlerBalance++; ClientDisconnected += value; }
                remove { _handlerBalance--; ClientDisconnected -= value; }
            }

            public event Action<uint, string> OnTextReceived
            {
                add { _handlerBalance++; TextReceived += value; }
                remove { _handlerBalance--; TextReceived -= value; }
            }

            public event Action<uint, byte[]> OnBinaryReceived
            {
                add { _handlerBalance++; BinaryReceived += value; }
                remove { _handlerBalance--; BinaryReceived -= value; }
            }

            public void Start(string host, int port)
            {
                if (ThrowOnStart)
                    throw new InvalidOperationException("phase51 start failure");
                IsRunning = true;
            }

            public void Stop() => IsRunning = false;
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() => Stop();
        }
    }
}
