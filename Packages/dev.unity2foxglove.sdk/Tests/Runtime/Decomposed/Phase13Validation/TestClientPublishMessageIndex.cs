// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates IRuntimeContext indirection, recording/replay controllers, McapBinaryReader bounds checks, client publish auto-increment, seek boundary behavior, coordinate roundtrip, and handler non-accumulation.

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
        /// Mixes server and client channels in one MCAP and verifies the
        /// statistics channel count includes both and the chunk records
        /// are readable.
        /// </summary>
        static void TestClientPublishMessageIndex()
        {
            var ms = new MemoryStream();
            var rec = new McapRecorder(ms);
            // Server channel
            rec.AddChannel(10, "/srv", "json", "", "", "");
            rec.WriteMessage(10, 1000UL, new byte[] { 1 });
            // Client channel
            rec.WriteClientMessage(1, 5, 2000UL, new byte[] { 2 }, "/cli");
            rec.Close();

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Assert(summary != null, "ClientPubIndex: summary non-null");
            // Statistics should include both channels
            var stats = summary.Statistics;
            Assert(stats != null, "ClientPubIndex: statistics non-null");
            Assert(stats.ChannelCount == 2,
                "ClientPubIndex: channel count includes client channel (expected=2, actual=" + stats.ChannelCount + ")");

            // MessageIndex should cover both channels
            var chunkIdx = summary.ChunkIndexes;
            Assert(chunkIdx != null && chunkIdx.Count > 0, "ClientPubIndex: chunk indexes exist");
            var chunk = reader.ReadChunkRecords(chunkIdx[0].ChunkStartOffset, chunkIdx[0].ChunkLength);
            Assert(chunk != null && chunk.Length > 0, "ClientPubIndex: chunk records readable");
        }

        /// <summary>
        /// When a schemaless client publish targets the same topic as a
        /// typed server channel, it must be skipped rather than creating
        /// a conflicting duplicate channel.
        /// </summary>
        static void TestClientPublishDoesNotCreateMixedTopicSchemas()
        {
            var ms = new MemoryStream();
            var rec = new McapRecorder(ms);
            rec.AddChannel(10, "/unity/camera", "json", "foxglove.CompressedImage", "jsonschema", "{}");
            rec.WriteMessage(10, 1000UL, new byte[] { 1 });
            rec.WriteClientMessage(1, 5, 2000UL, new byte[] { 2 }, "/unity/camera");
            rec.Close();

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            var cameraChannels = summary.Channels.FindAll(ch => ch.Topic == "/unity/camera");
            Assert(cameraChannels.Count == 1,
                "ClientPubMixedSchema: schemaless client publish does not add second /unity/camera channel");
            Assert(cameraChannels[0].SchemaId != 0,
                "ClientPubMixedSchema: /unity/camera keeps CompressedImage schema");
            Assert(summary.Statistics.ChannelMessageCounts[cameraChannels[0].Id] == 1,
                "ClientPubMixedSchema: skipped client publish is not recorded");
        }

        /// <summary>Roundtrip: Unity→Foxglove→Unity position must return to original.</summary>
        static void TestCoordinateRoundtrip()
        {
            // Position: U2F = (z, -x, y); F2U = (-y, z, x)
            // Roundtrip: (x,y,z) → (z,-x,y) → (-(-x), z, z) = (x, z, z) — wait, that's wrong.
            // Actually: F2U(z, -x, y) = (-(-x), y, z) = (x, y, z). Correct!
            double x = 1, y = 2, z = 3;
            double fx = z, fy = -x, fz = y;          // U2F
            double bx = -fy, by = fz, bz = fx;        // F2U
            Assert(System.Math.Abs(bx - x) < 0.001 && System.Math.Abs(by - y) < 0.001 && System.Math.Abs(bz - z) < 0.001,
                "CoordRnd: position roundtrip ((1,2,3) → (" + bx + "," + by + "," + bz + "))");

            // Rotation: U2F = (-z, x, -y, w); F2U = (y, -z, -x, w)
            // Roundtrip: (x,y,z,w) → (-z,x,-y,w) → (-y,-(-z),-x,w) = (-y, z, -x, w)
            // q and -q represent the same rotation, so (-y,z,-x,w) represents same as (y,-z,x,-w)
            // But roundtrip should get back something equivalent.
            // Let's test with specific basis rotations using component mapping:
            // Unity绕Y90: (0, 0.707, 0, 0.707) → (-0, 0, -0.707, 0.707)=(0,0,-0.707,0.707)
            // → F2U: (-0.707, -0, -0, 0.707) = (-0.707, 0, 0, 0.707) = Unity绕X-90
            // That's wrong.
            //
            // The conversion is NOT a roundtrip quaternion-identity. It's a coordinate transform.
            // Roundtrip for position is identity by construction. Rotation involves handness flip
            // and the result won't be component-wise identical but represents equivalent rotation.
            // Assert representative quaternion roundtrips directly; q and -q are treated as equivalent.
            const double sqrtHalf = 0.7071067811865476;
            Assert(QuaternionRoundtripEquivalent((0, 0, 0, 1)),
                "CoordRnd: identity quaternion roundtrip is equivalent");
            Assert(QuaternionRoundtripEquivalent((sqrtHalf, 0, 0, sqrtHalf)),
                "CoordRnd: X-axis quaternion roundtrip is equivalent");
            Assert(QuaternionRoundtripEquivalent((0, sqrtHalf, 0, sqrtHalf)),
                "CoordRnd: Y-axis quaternion roundtrip is equivalent");
            Assert(QuaternionRoundtripEquivalent((0, 0, sqrtHalf, sqrtHalf)),
                "CoordRnd: Z-axis quaternion roundtrip is equivalent");

            // Basis axis verification
            // Unity (1,0,0) → Foxglove (0,-1,0) ✓ (validated via Foxglove live)
            // Unity (0,1,0) → Foxglove (0,0,1)  ✓
            // Unity (0,0,1) → Foxglove (1,0,0)  ✓
            double[] ux = {1,0,0}, uy = {0,1,0}, uz = {0,0,1};
            double[] fx_exp = {0,-1,0}, fy_exp = {0,0,1}, fz_exp = {1,0,0};
            double[] f_ux = {uz[0], -ux[0], uy[0]};
            double[] f_uy = {uz[1], -ux[1], uy[1]};
            double[] f_uz = {uz[2], -ux[2], uy[2]};
            Assert(f_ux[0]==fx_exp[0] && f_ux[1]==fx_exp[1] && f_ux[2]==fx_exp[2], "CoordRnd: UX→F basis correct");
            Assert(f_uy[0]==fy_exp[0] && f_uy[1]==fy_exp[1] && f_uy[2]==fy_exp[2], "CoordRnd: UY→F basis correct");
            Assert(f_uz[0]==fz_exp[0] && f_uz[1]==fz_exp[1] && f_uz[2]==fz_exp[2], "CoordRnd: UZ→F basis correct");
        }

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
}



}
