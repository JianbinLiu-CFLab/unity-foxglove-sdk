// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 55 regression coverage for MCAP replay hardening,
// bounded history queries, replay lifecycle cleanup, and docs drift.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates fixes from the MCAP recording/replay/timeline review.
    /// </summary>
    public static class Phase55Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 55: MCAP Replay Hardening ===");
            _passed = 0;

            VerifyOversizedChunkUncompressedSizeIsRejected();
            VerifyNoCompressionChunkSizeMismatchIsRejected();
            VerifyHistoryLimitIsAppliedInsideReplayEngine();
            VerifyPlaybackClockRestartsFromBeginningAfterEnded();
            VerifyTruncatedInnerChunkRecordThrows();
            VerifyReplayDisableClearsSummaryMaps();
            VerifyReplayAdapterUsesEnableDisableSubscriptionLifecycle();
            VerifyRemoteTimelineResearchNoteMatchesBoundedHistoryStatus();

            Console.WriteLine($"Phase 55: {_passed} checks passed.");
        }

        private static void VerifyOversizedChunkUncompressedSizeIsRejected()
        {
            using var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteChunk(
                    startTime: 0,
                    endTime: 0,
                    uncompressedSize: 64UL * 1024 * 1024 + 1UL,
                    uncompressedCrc: 0,
                    compression: "",
                    compressedSize: 1,
                    records: new byte[] { 0 });
            }

            ms.Position = 0;
            var reader = new McapReader(ms);
            Check(Throws<InvalidDataException>(() => reader.ReadChunkRecords(0, 0, out _)),
                "55A-1: oversized declared chunk uncompressed size is rejected before allocation");
        }

        private static void VerifyNoCompressionChunkSizeMismatchIsRejected()
        {
            Check(Throws<InvalidDataException>(() => McapCompression.Decompress("", new byte[] { 1 }, 2)),
                "55A-2: uncompressed MCAP chunks must match their declared uncompressed size");
        }

        private static void VerifyHistoryLimitIsAppliedInsideReplayEngine()
        {
            var method = typeof(McapReplayEngine).GetMethod(
                "History",
                new[] { typeof(ulong), typeof(ulong), typeof(List<McapMessage>), typeof(int) });
            Check(method != null, "55B-1: McapReplayEngine exposes a capped History query");

            var tmp = CreateTempTimedMcap(6000);
            try
            {
                using var engine = new McapReplayEngine();
                engine.Load(tmp);
                var result = new List<McapMessage>();
                method.Invoke(engine, new object[] { 0UL, ulong.MaxValue, result, 100 });

                Check(result.Count == 100, "55B-2: capped History keeps at most the requested count");
                Check(result[0].LogTime == 5901UL && result[result.Count - 1].LogTime == 6000UL,
                    "55B-3: capped History keeps the latest messages in chronological order");
            }
            finally
            {
                File.Delete(tmp);
            }

            var replayController = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");
            Check(replayController.Contains("History(fromNs, clampedTo, _panelHistoryBuffer, ScrubHistoryMaxMessagesPerRequest"),
                "55B-4: ReplayController pushes the panel history cap into the engine query");
        }

        private static void VerifyPlaybackClockRestartsFromBeginningAfterEnded()
        {
            var clock = new PlaybackClock();
            var t0 = new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc);
            clock.EnableRange(1000UL, 2000UL);
            clock.Tick(t0);
            clock.Play();
            clock.Tick(t0.AddSeconds(1));

            Check(clock.NowNs == 2000UL && clock.ToState(false, "ended").Status == 3,
                "55C-1: playback clock reaches Ended at range end");

            clock.Play();

            Check(clock.NowNs == 1000UL && clock.ToState(false, "restart").Status == 0,
                "55C-2: Play from Ended resets playback clock to the range start");
        }

        private static void VerifyTruncatedInnerChunkRecordThrows()
        {
            var engine = new McapReplayEngine();
            SetPrivateField(engine, "_summary", new McapFileSummary
            {
                Statistics = new McapStatistics { MessageStartTime = 0, MessageEndTime = 10 },
                ChunkIndexes = new List<McapChunkIndex>
                {
                    new McapChunkIndex { MessageStartTime = 0, MessageEndTime = 10 }
                }
            });
            SetPrivateField(engine, "_currentChunkIdx", 0);
            SetPrivateField(engine, "_currentUncompressed", BuildChunkRecordHeader(McapWriter.OpcodeMessage, 16));
            SetPrivateField(engine, "_readOffset", 0);
            SetPrivateProperty(engine, "IsLoaded", true);
            SetPrivateProperty(engine, "CanSeek", true);
            SetPrivateProperty(engine, "EndTimeNs", 10UL);
            SetPrivateProperty(engine, "CurrentStatus", McapReplayEngine.Status.Playing);

            Check(Throws<InvalidDataException>(() => engine.Tick(10)),
                "55D-1: truncated chunk inner records throw InvalidDataException");
        }

        private static void VerifyReplayDisableClearsSummaryMaps()
        {
            var controller = new ReplayController(new ConsoleLogger());
            SetPrivateField(controller, "_summarySchemas", new Dictionary<ushort, McapSchema>
            {
                [1] = new McapSchema { Id = 1, Name = "old" }
            });
            SetPrivateField(controller, "_channelTopicMap", new Dictionary<ushort, string>
            {
                [1] = "/old"
            });

            controller.Disable();

            Check(GetPrivateField<Dictionary<ushort, McapSchema>>(controller, "_summarySchemas") == null,
                "55E-1: Disable clears replay summary schema map");
            Check(GetPrivateField<Dictionary<ushort, string>>(controller, "_channelTopicMap") == null,
                "55E-2: Disable clears replay channel topic map");
        }

        private static void VerifyReplayAdapterUsesEnableDisableSubscriptionLifecycle()
        {
            var adapter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs");
            var startBody = ExtractMethodBody(adapter, "private void Start");
            var onDisableBody = ExtractMethodBody(adapter, "private void OnDisable");
            var unsubscribeBody = ExtractMethodBody(adapter, "private void UnsubscribeReplay");

            Check(adapter.Contains("private void OnEnable()") && adapter.Contains("private void OnDisable()"),
                "55F-1: replay adapter has explicit OnEnable/OnDisable lifecycle hooks");
            Check(!startBody.Contains("OnReplayMessage +="),
                "55F-2: replay adapter does not subscribe from Start only");
            Check(onDisableBody.Contains("UnsubscribeReplay()") && unsubscribeBody.Contains("OnReplayMessageContext -="),
                "55F-3: replay adapter unsubscribes when the component is disabled");
        }

        private static void VerifyRemoteTimelineResearchNoteMatchesBoundedHistoryStatus()
        {
            var note = ReadRepoText("docs/research-remote-timeline-scene-reproduction.md");

            Check(!note.Contains("bounded panel history reconstruction remains future work"),
                "55G-1: research note no longer says bounded panel history itself is future work");
            Check(note.Contains("bounded server-push history") && note.Contains("large-MCAP scrub latency"),
                "55G-2: research note distinguishes implemented bounded history from remaining large-MCAP work");
        }

        private static string CreateTempTimedMcap(int messageCount)
        {
            var tmp = Path.GetTempFileName();
            using (var fs = new FileStream(tmp, FileMode.Truncate, FileAccess.Write))
            {
                var recorder = new McapRecorder(fs);
                recorder.AddChannel(1, "/phase55", "json", "", "", "");
                for (var i = 1; i <= messageCount; i++)
                    recorder.WriteMessage(1, (ulong)i, Encoding.UTF8.GetBytes("{\"i\":" + i + "}"));
                recorder.Close();
            }
            return tmp;
        }

        private static byte[] BuildChunkRecordHeader(byte opcode, ulong length)
        {
            var bytes = new byte[9];
            bytes[0] = opcode;
            for (var i = 0; i < 8; i++)
                bytes[1 + i] = (byte)((length >> (8 * i)) & 0xFF);
            return bytes;
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

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
        }

        private static void SetPrivateProperty(object target, string name, object value)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
                throw new MissingMemberException(target.GetType().FullName, name);
            property.SetValue(target, value);
        }
    }
}
