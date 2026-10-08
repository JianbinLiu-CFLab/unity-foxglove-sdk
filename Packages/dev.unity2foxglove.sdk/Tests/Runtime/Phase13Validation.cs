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
        private static int _passCount;
        private static readonly byte[] PlaybackControlRequestIdBytes = Encoding.UTF8.GetBytes("phase13-paused-seek");

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        /// <summary>
        /// Entry point: runs all Phase 13 tests covering IRuntimeContext
        /// indirection, recording/replay controllers, McapBinaryReader
        /// bounds checks, client publish auto-increment, seek boundary
        /// behavior, coordinate roundtrip, and handler non-accumulation.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 13 Tests ---");
            _passCount = 0;
            TestIruntimeContextIndirection();
            TestRecordingController();
            TestReplayController();
            TestReplayTickSortsCrossChannelLogTimes();
            TestReplayDefaultTickBudgetLimitsSeekBacklog();
            TestMcapBinaryReaderBounds();
            TestClientPublishAutoIncrementCid();
            TestSeekBeforeStartTime();
            TestSeekAtFirstChunkBoundary();
            TestClientPublishMessageIndex();
            TestClientPublishDoesNotCreateMixedTopicSchemas();
            TestCoordinateRoundtrip();
            TestReplayHandlerNoAccumulate();
            TestRuntimeReplayPlayAdvancesPlaybackClock();
            TestReplayAdvertisesProtobufSchemaAsBase64();
            TestReplayObjectAdapterRoutesProtobufBeforeJsonParse();
            TestReplayControllerSerializesReplayCursorAccess();
            TestPlaybackControlRunsOnRuntimeTick();
            TestPlaybackSeekTargetsDidSeekStateToRequestingClient();
            TestPausedReplaySeekFollowsOfficialPlaybackControlFlow();
            TestPausedReplaySeekAppliesSceneOnlySnapshot();
            TestActivePausedScrubDoesNotEmitPanelHistoryUntilSettled();
            TestReplayPlayInvalidatesPanelHistoryDeltaWatermark();
            TestReplaySubscribeRequestsPanelHistoryBackfill();
            TestReplayLateSubscribeBackfillsMissedPanelHistory();
            TestReplaySuppressesRuntimeLivePublish();
            TestReplaySuppressesRuntimeLiveChannelAdvertise();
            TestReplayUsesDataPriorityForSeekReset();
            Console.WriteLine($"Phase 13: {_passCount} checks passed.");
        }

        // ── IRuntimeContext indirection ──

        /// <summary>
        /// Verifies FoxgloveRuntime implements IRuntimeContext and a
        /// session can be started after receiving the context via
        /// <c>SetRuntimeContext</c>.
        /// </summary>
        static void TestIruntimeContextIndirection()
        {
            var rt = new FoxgloveRuntime();
            IRuntimeContext ctx = rt;
            Assert(ctx != null, "FoxgloveRuntime is IRuntimeContext");
            Assert(!ctx.PlaybackEnabled, "IRuntimeContext.PlaybackEnabled defaults to false");
            Assert(ctx.Assets != null, "IRuntimeContext.Assets is non-null");

            var s = new FoxgloveSession("test", new Phase13FakeTransport());
            s.SetRuntimeContext(ctx);
            s.Start("127.0.0.1", 9876);
            Assert(s.IsRunning, "Session started after SetRuntimeContext");
            s.Dispose();
        }

        // ── Controllers ──

        /// <summary>
        /// Enables recording, verifies the flag, then disables and
        /// verifies it returns to false.
        /// </summary>
        static void TestRecordingController()
        {
            var rt = new FoxgloveRuntime();
            var recordingPath = Path.GetTempFileName();
            try
            {
                Assert(rt.RecordingEnabled == false, "RecordingEnabled defaults to false");
                rt.EnableRecording(recordingPath, 1024 * 1024, "", "LeftHand");
                Assert(rt.RecordingEnabled == true, "RecordingEnabled after Enable");
                rt.DisableRecording();
                Assert(rt.RecordingEnabled == false, "RecordingEnabled after Disable");
            }
            finally
            {
                rt.Dispose();
                if (File.Exists(recordingPath))
                    File.Delete(recordingPath);
            }

            Assert(!File.Exists(recordingPath), "Recording fixture path is cleaned after controller disposal");
        }

        // Helper: create minimal MCAP in memory stream, write to temp file, return path
        static string CreateTempMcap(int messageCount, ulong intervalNs)
        {
            var tmp = Path.GetTempFileName();
            using (var fs = new FileStream(tmp, FileMode.Truncate, FileAccess.Write))
            {
                var rec = new McapRecorder(fs);
                rec.AddChannel(1, "/test", "json", "", "", "");
                for (int i = 0; i < messageCount; i++)
                    rec.WriteMessage(1, (ulong)(i + 1) * intervalNs, Encoding.UTF8.GetBytes("{}"));
                rec.Close();
            }
            return tmp;
        }

        static string CreateTempTimedMcap(params ulong[] logTimesNs)
        {
            var tmp = Path.GetTempFileName();
            using (var fs = new FileStream(tmp, FileMode.Truncate, FileAccess.Write))
            {
                var rec = new McapRecorder(fs);
                rec.AddChannel(1, "/test", "json", "", "", "");
                foreach (var logTimeNs in logTimesNs)
                    rec.WriteMessage(1, logTimeNs, Encoding.UTF8.GetBytes("{\"v\":" + logTimeNs + "}"));
                rec.Close();
            }
            return tmp;
        }

        /// <summary>
        /// Creates a temp MCAP, loads it into the replay engine, plays,
        /// ticks, and verifies messages are emitted at correct log times.
        /// </summary>
        static void TestReplayController()
        {
            var rt = new FoxgloveRuntime();
            Assert(rt.ReplayEnabled == false, "ReplayEnabled defaults to false");

            var tmp = CreateTempMcap(2, 1000UL * 1000 * 1000);
            try
            {
                using var engine = new McapReplayEngine();
                engine.Load(tmp);
                Assert(engine.IsLoaded, "ReplayEngine loaded");
                Assert(engine.CanSeek, "CanSeek true");
                Assert(engine.StartTimeNs == 1000UL * 1000 * 1000, "StartTime correct");
                Assert(engine.EndTimeNs == 2000UL * 1000 * 1000, "EndTime correct");

                engine.Play();
                Assert(engine.CurrentStatus == McapReplayEngine.Status.Playing, "Engine status Playing");

                var msgs = engine.Tick(1500UL * 1000 * 1000);
                Assert(msgs != null && msgs.Count > 0, "Tick emits messages when nowNs past first message");
                Assert(msgs[0].LogTime == 1000UL * 1000 * 1000, "First message logTime correct");

                msgs = engine.Tick(engine.EndTimeNs + 1);
                Assert(msgs != null && msgs.Count == 1, "Tick past all messages emits the remaining message exactly once");
                Assert(msgs[0].LogTime == 2000UL * 1000 * 1000, "Tick past all messages emits the final message");
            }
            finally { File.Delete(tmp); }
        }

        // ── McapBinaryReader bounds checks ──

        /// <summary>
        /// Verifies that replay ticks emit messages from different channels in
        /// global log-time order, not in channel-registration order.
        /// </summary>
        static void TestReplayTickSortsCrossChannelLogTimes()
        {
            var tmp = Path.GetTempFileName();
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Truncate, FileAccess.Write))
                {
                    var rec = new McapRecorder(fs);
                    rec.AddChannel(1, "/tf", "json", "", "", "");
                    rec.AddChannel(2, "/debug/position2", "json", "", "", "");
                    rec.WriteMessage(1, 100, Encoding.UTF8.GetBytes("{\"topic\":\"tf\"}"));
                    rec.WriteMessage(2, 90, Encoding.UTF8.GetBytes("{\"topic\":\"position\"}"));
                    rec.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(tmp);
                engine.Play();

                var messages = engine.Tick(100);
                Assert(messages.Count == 2, "Replay tick keeps cross-channel messages with earlier log times");
                Assert(messages[0].LogTime == 90 && messages[1].LogTime == 100,
                    "Replay tick emits cross-channel messages in global log time order");
            }
            finally
            {
                File.Delete(tmp);
            }
        }

        static void TestReplayDefaultTickBudgetLimitsSeekBacklog()
        {
            using var engine = new McapReplayEngine();
            Assert(engine.MaxMessagesPerTick == 8,
                "Replay default tick budget stays pinned at 8 messages per tick");
        }

        static void TestMcapBinaryReaderBounds()
        {
            try
            {
                int off = 0;
                McapBinaryReader.ReadU16LE(new byte[1], ref off);
                throw new Exception("Expected InvalidDataException for truncated U16");
            }
            catch (InvalidDataException) { Assert(true, "Truncated U16 throws InvalidDataException"); }

            try
            {
                int off = 0;
                McapBinaryReader.ReadString(new byte[3], ref off);
                throw new Exception("Expected InvalidDataException for truncated string length");
            }
            catch (InvalidDataException) { Assert(true, "Truncated string length throws InvalidDataException"); }

            try
            {
                int off = 0;
                var buf = new byte[8];
                buf[0] = 100; buf[1] = 0; buf[2] = 0; buf[3] = 0;
                McapBinaryReader.ReadString(buf, ref off);
                throw new Exception("Expected InvalidDataException for truncated string data");
            }
            catch (InvalidDataException) { Assert(true, "Truncated string data throws InvalidDataException"); }

            try
            {
                int off = 0;
                var buf = new byte[8];
                buf[0] = 100; buf[1] = 0; buf[2] = 0; buf[3] = 0;
                McapBinaryReader.ReadPrefixed(buf, ref off);
                throw new Exception("Expected InvalidDataException for truncated prefixed data");
            }
            catch (InvalidDataException) { Assert(true, "Truncated prefixed data throws InvalidDataException"); }

            try
            {
                int off = 0;
                var buf = new byte[4];
                buf[0] = 100; buf[1] = 0; buf[2] = 0; buf[3] = 0;
                McapBinaryReader.ReadMap(buf, ref off);
                throw new Exception("Expected InvalidDataException for truncated map");
            }
            catch (InvalidDataException) { Assert(true, "Truncated map throws InvalidDataException"); }
        }

        // ── ClientPublish auto-increment channel ID ──

        /// <summary>
        /// Writes multiple client-published messages through the
        /// recorder and verifies each distinct topic gets its own
        /// channel without collisions.
        /// </summary>
        static void TestClientPublishAutoIncrementCid()
        {
            var ms = new MemoryStream();
            var rec = new McapRecorder(ms);
            rec.WriteClientMessage(1, 10, 1000UL, new byte[] { 1, 2, 3 }, "/c/1");
            rec.WriteClientMessage(1, 20, 2000UL, new byte[] { 4, 5, 6 }, "/c/2");
            rec.WriteClientMessage(2, 10, 3000UL, new byte[] { 7, 8, 9 }, "/c/3");
            rec.Close();

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Assert(summary != null, "Summary non-null");
            Assert(summary.Channels != null, "Channels non-null");
            Assert(summary.Channels.Count == 3, "3 client channels registered (expected=3, actual=" + summary.Channels.Count + ")");

            var topics = new HashSet<string>();
            foreach (var ch in summary.Channels)
                topics.Add(ch.Topic);
            Assert(topics.Contains("/c/1"), "Channel topic /c/1 present");
            Assert(topics.Contains("/c/2"), "Channel topic /c/2 present");
            Assert(topics.Contains("/c/3"), "Channel topic /c/3 present");
        }

        // ── Seek boundary tests ──

        /// <summary>
        /// Seeks to a time before the first message, then ticks past it;
        /// verifies no messages are emitted before the first message
        /// timestamp.
        /// </summary>
        static void TestSeekBeforeStartTime()
        {
            var tmp = CreateTempMcap(2, 5000UL * 1000 * 1000);
            try
            {
                using var engine = new McapReplayEngine();
                engine.Load(tmp);
                engine.Play();
                engine.Seek(1000UL * 1000 * 1000);
                Assert(engine.CurrentTimeNs == 1000UL * 1000 * 1000, "Seek before start sets correct time");

                var msgs = engine.Tick(3000UL * 1000 * 1000);
                Assert(msgs.Count == 0, "No messages emitted before first message (expected=0, actual=" + msgs.Count + ")");
            }
            finally { File.Delete(tmp); }
        }

        /// <summary>
        /// Seeks exactly to the first message boundary, ticks, and
        /// verifies at least one message is emitted at or after the seek
        /// time.
        /// </summary>
        static void TestSeekAtFirstChunkBoundary()
        {
            var tmp = CreateTempMcap(2, 1000UL * 1000 * 1000);
            try
            {
                using var engine = new McapReplayEngine();
                engine.Load(tmp);
                engine.Play();

                engine.Seek(1000UL * 1000 * 1000);
                var msgs = engine.Tick(2000UL * 1000 * 1000);
                Assert(msgs.Count >= 1, "Tick after seek to first message boundary emits messages (expected>=1, actual=" + msgs.Count + ")");
                if (msgs.Count > 0)
                    Assert(msgs[0].LogTime >= 1000UL * 1000 * 1000, "First emitted message at or after seek time");
            }
            finally { File.Delete(tmp); }
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
        static byte[] BuildPlaybackControlRequest(byte command, bool hasSeek, ulong seekNs)
        {
            var requestIdBytes = PlaybackControlRequestIdBytes;
            var frame = new byte[19 + requestIdBytes.Length];
            frame[0] = ClientOpcode.PlaybackControlRequest;
            frame[1] = command;
            BinaryEncoding.WriteF32LE(frame, 2, 1f);
            frame[6] = hasSeek ? (byte)1 : (byte)0;
            BinaryEncoding.WriteU64LE(frame, 7, seekNs);
            BinaryEncoding.WriteU32LE(frame, 15, (uint)requestIdBytes.Length);
            Buffer.BlockCopy(requestIdBytes, 0, frame, 19, requestIdBytes.Length);
            return frame;
        }

        static string FirstServerInfoSessionId(List<string> texts)
        {
            return LastServerInfoSessionId(texts, 0, stopAfterFirst: true);
        }

static string LastServerInfoSessionId(List<string> texts, int startIndex, bool stopAfterFirst = false)
        {
            string result = null;
            for (var i = startIndex; i < texts.Count; i++)
            {
                if (!texts[i].Contains("\"serverInfo\"", StringComparison.Ordinal))
                    continue;
                var obj = JObject.Parse(texts[i]);
                if ((string)obj["op"] != "serverInfo")
                    continue;

                result = (string)obj["sessionId"];
                if (stopAfterFirst)
                    break;
            }

            return result;
        }

static bool ContainsAdvertiseForTopic(List<string> texts, int startIndex, string topic)
        {
            for (var i = startIndex; i < texts.Count; i++)
            {
                if (!texts[i].Contains("\"advertise\"", StringComparison.Ordinal))
                    continue;
                var obj = JObject.Parse(texts[i]);
                if ((string)obj["op"] != "advertise")
                    continue;

                foreach (var ch in (JArray)obj["channels"])
                {
                    if ((string)ch["topic"] == topic)
                        return true;
                }
            }

            return false;
        }
}




    public sealed partial class Phase13FakeTransport
    {
        public int SentBinaryFrameCount(uint clientId)
        {
            lock (_gate)
                return _sentBinary.TryGetValue(clientId, out var frames) ? frames.Count : 0;
        }
    }

}
