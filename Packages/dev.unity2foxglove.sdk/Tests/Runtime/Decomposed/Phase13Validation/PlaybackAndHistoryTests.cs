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
        static void TestPausedReplaySeekAppliesSceneOnlySnapshot()
        {
            var tmp = CreateTempMcap(3, 1_000_000UL);
            var transport = new Phase13FakeTransport();
            var rt = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            var sceneMessageCount = 0;
            string sceneTopic = null;
            try
            {
                rt.EnableReplay(tmp);
                rt.OnReplayMessage += (topic, _) =>
                {
                    sceneMessageCount++;
                    sceneTopic = topic;
                };
                rt.Start("paused-seek-scene-only", "127.0.0.1", GetFreeLoopbackPort());

                var replayChannelId = (uint)(McapReplayEngine.ReplayChannelIdBase | 1);
                transport.SimulateConnect(7);
                transport.SimulateText(7,
                    "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":" + replayChannelId + "}]}");

                transport.ClearBinary(7);
                transport.SimulateBinary(7, BuildPlaybackControlRequest(command: 1, hasSeek: true, seekNs: 2_500_000UL));
                rt.Tick();

                var messageFrames = 0;
                foreach (var frame in transport.SentBinaryFrames(7))
                {
                    if (BinaryEncoding.TryDecodeServerMessageData(frame, out _, out _, out _))
                        messageFrames++;
                }

                Assert(sceneMessageCount == 1,
                    "Paused replay seek applies one scene-only snapshot message (expected=1, actual=" + sceneMessageCount + ")");
                Assert(sceneTopic == "/test",
                    "Paused replay seek forwards the snapshot topic to Unity scene listeners");
                Assert(messageFrames == 0,
                    "Paused replay seek scene snapshot is not published as Foxglove MessageData");
            }
            finally
            {
                rt.Dispose();
                File.Delete(tmp);
            }
        }

        static void TestActivePausedScrubDoesNotEmitPanelHistoryUntilSettled()
        {
            var second = 1_000_000_000UL;
            var tmp = CreateTempTimedMcap(1 * second, 10 * second, 20 * second, 40 * second);
            var transport = new Phase13FakeTransport();
            var clock = new Phase13FakeClock();
            var rt = new FoxgloveRuntime(transport, clock, new DefaultSchemaRegistry());
            var sceneMessageCount = 0;
            try
            {
                rt.EnableReplay(tmp);
                rt.OnReplayMessage += (_, _) => sceneMessageCount++;
                rt.Start("active-paused-scrub", "127.0.0.1", GetFreeLoopbackPort());

                var replayChannelId = (uint)(McapReplayEngine.ReplayChannelIdBase | 1);
                transport.SimulateConnect(7);
                transport.SimulateText(7,
                    "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":" + replayChannelId + "}]}");
                transport.ClearBinary(7);

                transport.SimulateBinary(7, BuildPlaybackControlRequest(command: 1, hasSeek: true, seekNs: 40 * second));
                rt.Tick();
                clock.AdvanceNs(100_000_000UL);
                rt.Tick();
                Assert(CountMessageFrames(transport.SentBinaryFrames(7)) == 0,
                    "Active paused scrub does not publish panel history before the settled debounce");

                transport.ClearBinary(7);
                transport.SimulateBinary(7, BuildPlaybackControlRequest(command: 1, hasSeek: true, seekNs: 20 * second));
                rt.Tick();
                clock.AdvanceNs(100_000_000UL);
                rt.Tick();
                Assert(CountMessageFrames(transport.SentBinaryFrames(7)) == 0,
                    "Superseded paused scrub keeps panel history suppressed while drag is still active");

                clock.AdvanceNs(ReplayController.ScrubHistoryDebounceNs - 100_000_000UL);
                rt.Tick();

                var logTimes = MessageLogTimes(transport.SentBinaryFrames(7));
                Assert(logTimes.Count == 3
                    && logTimes[0] == 1 * second
                    && logTimes[1] == 10 * second
                    && logTimes[2] == 20 * second,
                    "Settled paused scrub publishes bounded chronological panel history");
                Assert(sceneMessageCount >= 2,
                    "Active paused scrub still updates the Unity scene immediately for each seek");
            }
            finally
            {
                rt.Dispose();
                File.Delete(tmp);
            }
        }

        static void TestReplayPlayInvalidatesPanelHistoryDeltaWatermark()
        {
            var second = 1_000_000_000UL;
            var tmp = CreateTempTimedMcap(1 * second, 10 * second, 20 * second, 40 * second);
            var transport = new Phase13FakeTransport();
            var clock = new Phase13FakeClock();
            var rt = new FoxgloveRuntime(transport, clock, new DefaultSchemaRegistry());
            try
            {
                rt.EnableReplay(tmp);
                rt.Start("play-invalidates-history-watermark", "127.0.0.1", GetFreeLoopbackPort());

                var replayChannelId = (uint)(McapReplayEngine.ReplayChannelIdBase | 1);
                transport.SimulateConnect(7);
                transport.SimulateText(7,
                    "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":" + replayChannelId + "}]}");
                transport.ClearBinary(7);

                transport.SimulateBinary(7, BuildPlaybackControlRequest(command: 1, hasSeek: true, seekNs: 20 * second));
                rt.Tick();
                clock.AdvanceNs(ReplayController.ScrubHistoryDebounceNs);
                rt.Tick();
                Assert(MessageLogTimes(transport.SentBinaryFrames(7)).Count == 3,
                    "Initial settled paused seek publishes history through the seek time");

                transport.SimulateBinary(7, BuildPlaybackControlRequest(command: 0, hasSeek: false, seekNs: 0));
                rt.Tick();
                transport.ClearBinary(7);

                transport.SimulateBinary(7, BuildPlaybackControlRequest(command: 1, hasSeek: true, seekNs: 40 * second));
                rt.Tick();
                clock.AdvanceNs(ReplayController.ScrubHistoryDebounceNs);
                rt.Tick();

                var logTimes = MessageLogTimes(transport.SentBinaryFrames(7));
                Assert(logTimes.Count == 3
                    && logTimes[0] == 10 * second
                    && logTimes[1] == 20 * second
                    && logTimes[2] == 40 * second,
                    "Replay Play invalidates paused history delta watermark so the next paused seek rebuilds the bounded window");
            }
            finally
            {
                rt.Dispose();
                File.Delete(tmp);
            }
        }

        static void TestReplaySubscribeRequestsPanelHistoryBackfill()
        {
            var transport = new Phase13FakeTransport();
            var runtime = new Phase13ReplayRuntimeContext();
            var session = new FoxgloveSession("late-replay-subscribe", transport);
            try
            {
                session.SetRuntimeContext(runtime);
                session.RegisterChannel(new AdvertiseChannel
                {
                    Id = 1,
                    Topic = "/replay",
                    Encoding = "json",
                    SchemaName = "",
                    Schema = ""
                });

                transport.SimulateConnect(7);
                transport.SimulateText(7,
                    "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}");

                Assert(runtime.BackfillRequests == 1,
                    "Replay subscribe requests panel-history backfill for late subscribers");
            }
            finally
            {
                session.Dispose();
            }
        }

        static void TestReplayLateSubscribeBackfillsMissedPanelHistory()
        {
            var second = 1_000_000_000UL;
            var tmp = CreateTempTimedMcap(1 * second, 2 * second, 3 * second, 4 * second);
            var transport = new Phase13FakeTransport();
            var clock = new Phase13FakeClock();
            var rt = new FoxgloveRuntime(transport, clock, new DefaultSchemaRegistry());
            try
            {
                rt.EnableReplay(tmp);
                rt.Start("late-replay-panel-subscribe", "127.0.0.1", GetFreeLoopbackPort());

                var replayChannelId = (uint)(McapReplayEngine.ReplayChannelIdBase | 1);
                transport.SimulateConnect(7);
                transport.SimulateBinary(7, BuildPlaybackControlRequest(command: 1, hasSeek: true, seekNs: 3 * second));
                rt.Tick();

                clock.AdvanceNs(ReplayController.ScrubHistoryDebounceNs);
                rt.Tick();
                transport.ClearBinary(7);

                transport.SimulateText(7,
                    "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":" + replayChannelId + "}]}");
                Assert(CountMessageFrames(transport.SentBinaryFrames(7)) == 0,
                    "Late replay subscribe defers panel-history backfill until debounce");

                clock.AdvanceNs(ReplayController.ScrubHistoryDebounceNs);
                rt.Tick();

                var logTimes = MessageLogTimes(transport.SentBinaryFrames(7));
                Assert(logTimes.Count == 3
                    && logTimes[0] == 1 * second
                    && logTimes[1] == 2 * second
                    && logTimes[2] == 3 * second,
                    "Late replay subscribe backfills missed panel history through the current replay time");
            }
            finally
            {
                rt.Dispose();
                File.Delete(tmp);
            }
        }

        static void TestReplaySuppressesRuntimeLivePublish()
        {
            var tmp = CreateTempMcap(1, 1_000_000UL);
            var transport = new Phase13FakeTransport();
            var rt = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            try
            {
                rt.EnableReplay(tmp);
                rt.SetReplayLiveSuppression(true);
                rt.Start("replay-live-gate", "127.0.0.1", GetFreeLoopbackPort());
                transport.SimulateConnect(7);
                rt.ReplayPause();

                rt.RegisterChannel(new AdvertiseChannel
                {
                    Id = 42,
                    Topic = "/live",
                    Encoding = "json",
                    SchemaName = "",
                    Schema = ""
                });
                transport.SimulateText(7,
                    "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":420,\"channelId\":42}]}");
                transport.ClearBinary(7);

                rt.Publish(42, Encoding.UTF8.GetBytes("{}"), 123UL);

                var messageFrames = 0;
                foreach (var frame in transport.SentBinaryFrames(7))
                {
                    if (BinaryEncoding.TryDecodeServerMessageData(frame, out _, out _, out _))
                        messageFrames++;
                }

                Assert(messageFrames == 0,
                    "Replay mode suppresses runtime live publish frames (expected=0, actual=" + messageFrames + ")");
            }
            finally
            {
                rt.Dispose();
                File.Delete(tmp);
            }
        }

        static void TestReplaySuppressesRuntimeLiveChannelAdvertise()
        {
            var tmp = CreateTempMcap(1, 1_000_000UL);
            var transport = new Phase13FakeTransport();
            var rt = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            try
            {
                rt.EnableReplay(tmp);
                rt.SetReplayLiveSuppression(true);
                rt.Start("replay-live-advertise-gate", "127.0.0.1", GetFreeLoopbackPort());
                rt.ReplayPause();

                var textCountBefore = transport.SentText.Count;
                rt.RegisterChannel(new AdvertiseChannel
                {
                    Id = 42,
                    Topic = "/live",
                    Encoding = "json",
                    SchemaName = "",
                    Schema = ""
                });

                Assert(transport.SentText.Count == textCountBefore,
                    "Replay mode suppresses runtime live channel advertisement");
            }
            finally
            {
                rt.Dispose();
                File.Delete(tmp);
            }
        }

    }
}
