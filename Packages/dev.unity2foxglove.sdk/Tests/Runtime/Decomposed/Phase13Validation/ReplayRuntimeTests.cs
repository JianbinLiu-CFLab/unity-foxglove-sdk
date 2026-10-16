// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase192 source section for Phase 13 validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class Phase13Validation
    {
        /// <summary>
        /// After three Stop/Start cycles, firing a replay message must
        /// invoke the handler exactly once (no duplicate subscriptions).
        /// </summary>
        static void TestReplayHandlerNoAccumulate()
        {
            int count = 0;
            var clock = new Phase13FakeClock();
            var rt = new FoxgloveRuntime(new Phase13FakeTransport(), clock, new DefaultSchemaRegistry());
            rt.OnReplayMessage += (t, d) => count++;
            rt.Start("test", "127.0.0.1", GetFreeLoopbackPort());
            rt.Stop();
            rt.Start("test", "127.0.0.1", GetFreeLoopbackPort());
            rt.Stop();
            rt.Start("test", "127.0.0.1", GetFreeLoopbackPort());

            // Fire replay via internal test hook
            rt.FireReplayForTests("/tf", new byte[] { });

            Assert(count == 1, "HandlerNoAccum: trigger fires once after 3 Stop/Start cycles (expected=1, actual=" + count + ")");
            rt.Dispose();
        }

        /// <summary>
        /// Runtime-level ReplayPlay must start the playback clock as well
        /// as the replay engine. Otherwise auto-play replay remains frozen
        /// at the first MCAP timestamp and cannot drive scene adapters.
        /// </summary>
        static void TestRuntimeReplayPlayAdvancesPlaybackClock()
        {
            var tmp = CreateTempMcap(2, 1_000_000UL);
            var rt = new FoxgloveRuntime(new Phase13FakeTransport(), new SystemClock(), new DefaultSchemaRegistry());
            int count = 0;
            try
            {
                rt.EnableReplay(tmp);
                rt.OnReplayMessage += (topic, payload) => count++;
                rt.Start("replay-clock-test", "127.0.0.1", GetFreeLoopbackPort());
                rt.ReplayPlay();
                for (var i = 0; i < 200 && count < 2; i++)
                {
                    Thread.Sleep(5);
                    rt.Tick();
                }

                Assert(count == 2, "Runtime ReplayPlay advances playback clock and emits both messages (expected=2, actual=" + count + ")");
            }
            finally
            {
                rt.Dispose();
                File.Delete(tmp);
            }
        }

        /// <summary>
        /// MCAP stores protobuf schemas as raw FileDescriptorSet bytes, while
        /// Foxglove WebSocket advertise messages expect the schema string to be
        /// base64. Replay must convert the bytes back to base64 instead of
        /// treating them as UTF-8 text.
        /// </summary>
        static void TestReplayAdvertisesProtobufSchemaAsBase64()
        {
            var tmp = Path.GetTempFileName();
            var schemaBytes = new byte[] { 0x0A, 0x05, (byte)'s', (byte)'c', (byte)'e', (byte)'n', (byte)'e', 0xFF };
            var expectedSchema = Convert.ToBase64String(schemaBytes);
            var transport = new Phase13FakeTransport();
            var rt = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Truncate, FileAccess.Write))
                {
                    var rec = new McapRecorder(fs);
                    rec.AddChannel(1, "/scene", "protobuf", "foxglove.SceneUpdate", "protobuf", expectedSchema);
                    rec.WriteMessage(1, 1_000_000UL, new byte[] { 0x12, 0x00 });
                    rec.Close();
                }

                rt.EnableReplay(tmp);
                rt.Start("protobuf-replay-schema", "127.0.0.1", GetFreeLoopbackPort());

                JObject replayChannel = null;
                foreach (var text in transport.SentText)
                {
                    var obj = JObject.Parse(text);
                    if ((string)obj["op"] != "advertise") continue;
                    foreach (var ch in (JArray)obj["channels"])
                    {
                        if ((string)ch["topic"] == "/scene")
                        {
                            replayChannel = (JObject)ch;
                            break;
                        }
                    }
                    if (replayChannel != null) break;
                }

                Assert(replayChannel != null, "Replay protobuf channel is advertised");
                Assert((string)replayChannel["encoding"] == "protobuf", "Replay protobuf channel keeps message encoding");
                Assert((string)replayChannel["schemaEncoding"] == "protobuf", "Replay protobuf channel keeps schemaEncoding protobuf");
                Assert((string)replayChannel["schema"] == expectedSchema,
                    "Replay protobuf schema is advertised as base64, not raw UTF-8");
            }
            finally
            {
                rt.Dispose();
                File.Delete(tmp);
            }
        }

        static void TestReplayObjectAdapterRoutesProtobufBeforeJsonParse()
        {
            if (!TryReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs", out var source))
                return;
            Assert(source.Contains("OnReplayMessage(ReplayMessageContext context)", StringComparison.Ordinal)
                && source.Contains("ResolveBehavior(context", StringComparison.Ordinal),
                "Replay adapter routes by replay message context and behavior before protobuf parsing");
            Assert(source.Contains("ReplayChannelBehavior.FrameTransformPose", StringComparison.Ordinal)
                && source.Contains("ReplayChannelBehavior.ScenePrimitivePose", StringComparison.Ordinal),
                "Replay adapter ignores non-pose replay messages through behavior classification");
            Assert(source.Contains("ParseProtobuf(GetFrameTransformProtoTypeName", StringComparison.Ordinal),
                "Replay adapter supports frame-transform protobuf payloads through optional assembly reflection");
            Assert(source.Contains("ParseProtobuf(\"Foxglove.SceneUpdate\"", StringComparison.Ordinal),
                "Replay adapter supports scene-update protobuf payloads through optional assembly reflection");
            Assert(!source.Contains("global::Foxglove.", StringComparison.Ordinal),
                "Replay adapter does not directly reference the optional protobuf assembly");
        }

        static void TestReplayControllerSerializesReplayCursorAccess()
        {
            var source = PhaseValidationSourceHelpers.ReadReplayControllerSources();

            Assert(source.Contains("private readonly object _replayEngineLock", StringComparison.Ordinal),
                "Replay controller has a dedicated replay cursor synchronization lock");
            Assert(CountOccurrences(source, "lock (_replayEngineLock)") >= 5,
                "Replay controller serializes Tick/Snapshot/Seek/Play/Pause access to the MCAP cursor");
        }

        static void TestPlaybackControlRunsOnRuntimeTick()
        {
            if (!TryReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Connection.cs", out var sessionSource)
                || !TryReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionPlaybackHandler.cs", out var playbackHandlerSource)
                || !TryReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs", out var runtimeSource)
                || !TryReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/TickCoordinator.cs", out var tickSource))
                return;

            var replaySource = PhaseValidationSourceHelpers.ReadReplayControllerSources();

            Assert(playbackHandlerSource.Contains("_pendingPlaybackControls.Enqueue", StringComparison.Ordinal)
                && playbackHandlerSource.Contains("public void Drain()", StringComparison.Ordinal)
                && sessionSource.Contains("_playback.Drain()", StringComparison.Ordinal),
                "Playback control requests are queued by the transport thread and drained by runtime Tick");
            var drainIndex = tickSource.IndexOf("DrainPlaybackControls()", StringComparison.Ordinal);
            var clockTickIndex = tickSource.IndexOf("playbackClock.Tick()", StringComparison.Ordinal);
            Assert(drainIndex >= 0 && clockTickIndex > drainIndex,
                "Runtime drains playback controls before advancing replay time");
            Assert(replaySource.Contains("PublishMessages(session, messages, nowNs, \"Tick\", forwardToScene: true)", StringComparison.Ordinal),
                "Replay Tick broadcasts playback time before replay message frames");
        }

        static void TestPlaybackSeekTargetsDidSeekStateToRequestingClient()
        {
            var tmp = CreateTempMcap(2, 1_000_000UL);
            var transport = new Phase13FakeTransport();
            var clock = new Phase13FakeClock();
            var rt = new FoxgloveRuntime(transport, clock, new DefaultSchemaRegistry());
            try
            {
                rt.EnableReplay(tmp);
                rt.Start("playback-state-broadcast", "127.0.0.1", GetFreeLoopbackPort());
                transport.SimulateConnect(7);
                transport.SimulateConnect(8);
                transport.ClearBinary(7);
                transport.ClearBinary(8);

                transport.SimulateBinary(7, BuildPlaybackControlRequest(command: 1, hasSeek: true, seekNs: 1_000_000UL));
                rt.Tick();

                var requestingFrames = transport.SentBinaryFrames(7);
                Assert(CountPlaybackStateFrames(requestingFrames) == 1,
                    "Playback seek sends didSeek PlaybackState to the requesting client");
                Assert(CountPlaybackStateFrames(transport.SentBinaryFrames(8)) == 0,
                    "Playback seek does not leak request-correlated PlaybackState to other clients");
                Assert(TryDecodePlaybackState(requestingFrames[0], out _, out _, out _, out var didSeek, out _)
                    && didSeek,
                    "Targeted PlaybackState preserves didSeek=true for the requesting client");
            }
            finally
            {
                rt.Dispose();
                File.Delete(tmp);
            }
        }

        static void TestPausedReplaySeekFollowsOfficialPlaybackControlFlow()
        {
            var tmp = CreateTempMcap(3, 1_000_000UL);
            var transport = new Phase13FakeTransport();
            var rt = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            try
            {
                rt.EnableReplay(tmp);
                rt.Start("paused-seek-snapshot", "127.0.0.1", GetFreeLoopbackPort());

                var replayChannelId = (uint)(McapReplayEngine.ReplayChannelIdBase | 1);
                transport.SimulateConnect(7);
                var originalSessionId = FirstServerInfoSessionId(transport.SentText);
                transport.SimulateText(7,
                    "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":" + replayChannelId + "}]}");

                transport.ClearBinary(7);
                var textCountBeforeSeek = transport.SentText.Count;
                transport.SimulateBinary(7, BuildPlaybackControlRequest(command: 1, hasSeek: true, seekNs: 2_500_000UL));

                var immediateMessageFrames = 0;
                var immediatePlaybackStateFrames = 0;
                foreach (var frame in transport.SentBinaryFrames(7))
                {
                    if (frame.Length > 0 && frame[0] == ServerOpcode.PlaybackState)
                        immediatePlaybackStateFrames++;
                    if (BinaryEncoding.TryDecodeServerMessageData(frame, out _, out _, out _))
                        immediateMessageFrames++;
                }

                Assert(!string.IsNullOrEmpty(originalSessionId), "Paused replay seek starts from a session id");
                Assert(transport.SentText.Count == textCountBeforeSeek,
                    "Paused replay seek preserves the active WebSocket session and subscriptions");
                Assert(transport.ClearDataQueuesCount == 0,
                    "Paused replay seek does not mutate transport queues from the WebSocket receive thread");
                Assert(immediatePlaybackStateFrames == 0,
                    "Paused replay seek queues playback state until runtime Tick");
                Assert(immediateMessageFrames == 0, "Paused replay seek defers snapshot publication until runtime Tick");

                var deferredStartIndex = transport.SentBinaryFrameCount(7);
                rt.Tick();

                var deferredPlaybackStateFrames = 0;
                var messageFrames = 0;
                var timeFrames = 0;
                var frames = transport.SentBinaryFrames(7);
                for (var i = deferredStartIndex; i < frames.Count; i++)
                {
                    var frame = frames[i];
                    if (frame.Length > 0 && frame[0] == ServerOpcode.PlaybackState)
                        deferredPlaybackStateFrames++;
                    if (frame.Length > 0 && frame[0] == ServerOpcode.Time)
                        timeFrames++;
                    if (BinaryEncoding.TryDecodeServerMessageData(frame, out _, out _, out _))
                        messageFrames++;
                }

                Assert(transport.ClearDataQueuesCount == 1,
                    "Paused replay seek clears stale queued data during runtime Tick");
                Assert(deferredPlaybackStateFrames == 1,
                    "Paused replay seek sends playback state from runtime Tick");
                Assert(timeFrames == 0,
                    "Paused replay seek follows the official SDK and does not broadcast Time frames");
                Assert(messageFrames == 0,
                    "Paused replay seek follows the official SDK and does not publish snapshot MessageData");
            }
            finally
            {
                rt.Dispose();
                File.Delete(tmp);
            }
        }

    }
}
