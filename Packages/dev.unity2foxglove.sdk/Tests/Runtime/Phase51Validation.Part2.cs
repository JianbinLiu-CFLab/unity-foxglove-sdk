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
    public static partial class Phase51Validation
    {
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
            var detachCallIndex = stop.IndexOf(
                "DetachTransportForwarders(cleanupSession)",
                StringComparison.Ordinal);
            var runtimeStopIndex = stop.IndexOf("_runtime.Stop,", StringComparison.Ordinal);
            var detachImplementationIndex = source.IndexOf(
                "transport.OnClientConnected -=",
                stopIndex,
                StringComparison.Ordinal);
            Check(stop.Contains("capture and detach") || stop.Contains("Capture and detach"),
                "51B-27: StopServer documents that callbacks are detached before runtime Stop clears Session");
            Check(stop.Contains("FoxgloveManagerTeardownState.RunStopServer(", StringComparison.Ordinal)
                  && detachCallIndex >= 0
                  && runtimeStopIndex >= 0
                  && detachCallIndex < runtimeStopIndex
                  && detachImplementationIndex >= 0,
                "51B-28: StopServer detaches transport callbacks before the centralized runtime Stop step");
        }

        private static void VerifySessionGraphMetadataFlushIsSeparatedAndDirtyGated()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionGraphHandler.cs");
            var broadcast = PhaseValidationSourceHelpers.SourceMethod(source, "BroadcastUpdate");
            var flush = PhaseValidationSourceHelpers.SourceMethod(source, "FlushClaimedMetadataSnapshot");
            Check(broadcast.Contains("FlushClaimedMetadataSnapshot(recorder, json)", StringComparison.Ordinal)
                  && flush.Contains("WriteMetadata", StringComparison.Ordinal)
                  && !flush.Contains("_transport.SendText", StringComparison.Ordinal),
                "51B-29: SessionGraphHandler separates websocket broadcast from MCAP metadata flush");
            var claimIndex = broadcast.IndexOf(
                "Interlocked.CompareExchange(ref _dirty, 0, 1)",
                StringComparison.Ordinal);
            var snapshotIndex = broadcast.IndexOf(
                "JsonConvert.SerializeObject(_graph.GetSnapshot())",
                StringComparison.Ordinal);
            Check(claimIndex >= 0
                  && snapshotIndex > claimIndex
                  && flush.Contains("Volatile.Write(ref _dirty, 1)", StringComparison.Ordinal),
                "51B-30: graph metadata writes remain dirty-gated");
        }

        private static void VerifyRuntimeTickLockBoundaryIsDocumented()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/TickCoordinator.cs");
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

            var replaySource = PhaseValidationSourceHelpers.ReadReplayControllerSources();
            Check(replaySource.Contains("_replayTickBuffer") && replaySource.Contains(".Tick(nowNs, _replayTickBuffer)"),
                "51C-2: ReplayController reuses a replay tick message buffer");
        }

        private static void VerifyMcapReplayAvoidsPendingHeadRemovalAndDeadSeekGuard()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var pendingQueue = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayPendingQueue.cs");
            var tick = ExtractMethodBody(source, "public List<McapMessage> Tick(ulong nowNs, List<McapMessage> result)");
            var popPending = ExtractMethodBody(source, "private McapMessage PopPending");
            var seek = ExtractMethodBody(source, "public void Seek");
            Check(source.Contains("private readonly McapReplayPendingQueue _pending = new()", StringComparison.Ordinal)
                  && pendingQueue.Contains("private int _headIndex", StringComparison.Ordinal),
                "51C-2c: McapReplayEngine tracks a pending head index for O(1) front dequeue");
            Check(!tick.Contains("_pending.RemoveAt(0)")
                  && !popPending.Contains("RemoveAt(0)")
                  && !pendingQueue.Contains("RemoveAt(0)", StringComparison.Ordinal),
                "51C-2d: replay pending dequeue path avoids List.RemoveAt(0)");
            Check(!seek.Contains("_currentChunkIdx < -1"),
                "51C-2e: replay seek no longer carries an unreachable chunk-index guard");
        }

        private static void VerifyMcapReplayHistoryCapAvoidsRepeatedFullSort()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var history = ExtractMethodBody(source, "public List<McapMessage> History(ulong fromTimeNs, ulong toTimeNs, List<McapMessage> result, int maxMessages)");
            var trimHistory = ExtractMethodBody(source, "private static void TrimHistoryToLatestMessages");
            Check(history.Contains("result.Sort(CompareMessages)")
                  && history.Contains("TrimHistoryToLatestMessages(result, maxMessages)")
                  && trimHistory.Contains("result.RemoveRange(0, result.Count - maxMessages)")
                  && !source.Contains("private static void AddHistoryMessage", StringComparison.Ordinal)
                  && !source.Contains("FindHistoryInsertIndex", StringComparison.Ordinal),
                "51C-2f: capped history retention sorts once after load and trims the latest messages");
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
            var source = PhaseValidationSourceHelpers.ReadMcapRecorderSources();
            Check(!source.Contains("_chunkBuf.ToArray()") && source.Contains("TryGetBuffer"),
                "51C-3: McapRecorder FlushChunk avoids the raw chunk ToArray copy");
        }

        private static void VerifyMcapRecorderAllChannelWriteStatesTracksSeenInline()
        {
            var source = PhaseValidationSourceHelpers.ReadMcapRecorderSources();
            var method = ExtractMethodBody(source, "List<ChannelWriteState> FillAndGetScratchChannelWriteStates");
            Check(method.Contains("if (_seenChannelIds.Add(m.McapId))")
                  && method.Contains("_allChannelWriteStates.Add(m)")
                  && method.Contains("Monitor.IsEntered(_lock)")
                  && !method.Contains("new HashSet<ushort>")
                  && !Regex.IsMatch(method, @"foreach\s*\(var\s+m\s+in\s+_serverChannelWriteStates\.Values\)\s*_seenChannelIds\.Add\(m\.McapId\)"),
                "51C-3b: McapRecorder tracks channel ids during the scratch channel-state pass");
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
                new FoxgloveSourceEmitter.TopicMember(
                    "Position",
                    "UnityEngine.Vector3",
                    "/phase51",
                    10f,
                    "",
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    typeShape: FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Vector3))),
                new FoxgloveSourceEmitter.TopicMember("_health", "System.Single", "/phase51/health", 10f, "")
            });

            Check(
                source.Contains("__foxRunCapture_0_0 = this.Position;")
                && source.Contains("__foxRunCapture_1_0 = this._health;")
                && source.Contains("[\"x\"] = __foxRunCapture_0_0.x")
                && source.Contains("[\"health\"] = __foxRunCapture_1_0"),
                "51D-1: generated capture uses explicit this. member access and serialization reuses the one-sample capture");
        }

        private static void VerifyFoxRunMixedPolicyDiagnostic()
        {
            var source = PhaseValidationSourceHelpers.ReadFoxgloveLogSourceGeneratorSources();
            Check(source.Contains("FOXRUN005") && source.Contains("Mixed") && source.Contains("Policy"),
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
    }
}
