// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 68 validation for the MCAP indexed reader surface.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the public MCAP indexed reader API and its local query
    /// behavior.
    /// </summary>
    public static class Phase68Validation
    {
        private static int _passed;

        /// <summary>
        /// Runs all Phase 68 validation checks.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 68: MCAP Indexed Reader Surface ===");
            _passed = 0;

            VerifyIndexedReaderTypeExists();
            VerifyPublicApiShape();
            VerifySummaryEnumeration();
            VerifyIndexedRecordHelpers();
            VerifyMessageQueries();
            VerifyStreamOwnership();
            VerifyOpenReadDisposesStreamWhenSummaryReadFails();
            VerifyMalformedChunkIsolation();

            Console.WriteLine($"Phase 68: {_passed} checks passed.");
        }

        /// <summary>
        /// Runs a manual smoke check against an externally recorded MCAP file.
        /// </summary>
        public static void ValidateExternalMcapSmoke(
            string filePath,
            List<string> requiredTopics = null,
            int maxMessages = 5,
            int minMessages = 1)
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 68: External MCAP Indexed Reader Smoke ===");
            _passed = 0;

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("External MCAP path is required.", nameof(filePath));

            var fullPath = Path.GetFullPath(filePath);
            Check(File.Exists(fullPath), "68S-1: external MCAP file exists");

            requiredTopics = requiredTopics != null && requiredTopics.Count > 0
                ? requiredTopics
                : new List<string> { "/tf", "/scene" };
            maxMessages = maxMessages > 0 ? maxMessages : 5;
            minMessages = minMessages > 0 ? minMessages : 1;

            using var indexed = McapIndexedReader.OpenRead(fullPath);
            Check(indexed.Summary != null, "68S-2: indexed reader opens the MCAP summary");
            Console.WriteLine($"MCAP: {fullPath}");
            Console.WriteLine($"Size: {new FileInfo(fullPath).Length} bytes");
            Console.WriteLine($"Channels: {indexed.Channels.Count}");
            Console.WriteLine($"Chunks: {indexed.Summary.ChunkIndexes.Count}");

            Check(indexed.Channels.Count > 0, "68S-3: summary exposes at least one channel");
            Check(indexed.Summary.ChunkIndexes.Count > 0, "68S-4: summary exposes chunk indexes");

            var latest = indexed.ReadMessages(new McapReadOptions
            {
                MaxMessages = maxMessages
            });
            Check(latest.Count >= minMessages,
                $"68S-5: latest query returns at least {minMessages} message(s)");
            Check(latest.Count <= maxMessages,
                $"68S-6: latest query honors MaxMessages={maxMessages}");
            CheckMessagesSorted(latest, "68S-7: latest query returns chronological messages");

            var inverted = indexed.ReadMessages(new McapReadOptions
            {
                StartTimeNs = ulong.MaxValue,
                EndTimeNs = 0
            });
            Check(inverted.Count == 0, "68S-8: inverted time range returns empty");

            foreach (var topic in requiredTopics)
            {
                var hasTopic = indexed.Channels.Any(c => c.Topic == topic);
                Check(hasTopic, $"68S-topic: summary contains {topic}");

                var topicMessages = indexed.ReadMessages(new McapReadOptions
                {
                    Topics = new List<string> { topic },
                    MaxMessages = maxMessages
                });
                Check(topicMessages.Count >= minMessages,
                    $"68S-topic: {topic} query returns at least {minMessages} message(s)");
                CheckMessagesSorted(topicMessages, $"68S-topic: {topic} query is chronological");
                Console.WriteLine($"Topic {topic}: {topicMessages.Count} sampled message(s)");
            }

            Console.WriteLine($"Phase 68 external MCAP smoke: {_passed} checks passed.");
        }

        private static void VerifyIndexedReaderTypeExists()
        {
            var type = Type.GetType("Unity.FoxgloveSDK.IO.McapIndexedReader, FoxgloveSdk.Tests");
            Check(type != null, "68A-1: McapIndexedReader type exists");
        }

        private static void VerifyPublicApiShape()
        {
            var options = Type.GetType("Unity.FoxgloveSDK.IO.McapReadOptions, FoxgloveSdk.Tests");
            Check(options != null, "68A-2: McapReadOptions type exists");
            Check(options.GetField("StartTimeNs") != null, "68A-3: McapReadOptions exposes StartTimeNs");
            Check(options.GetField("EndTimeNs") != null, "68A-4: McapReadOptions exposes EndTimeNs");
            Check(options.GetField("Topics") != null, "68A-5: McapReadOptions exposes Topics");
            Check(options.GetField("ChannelIds") != null, "68A-6: McapReadOptions exposes ChannelIds");
            Check(options.GetField("MaxMessages") != null, "68A-7: McapReadOptions exposes MaxMessages");

            var reader = Type.GetType("Unity.FoxgloveSDK.IO.McapIndexedReader, FoxgloveSdk.Tests");
            Check(typeof(IDisposable).IsAssignableFrom(reader), "68A-8: McapIndexedReader implements IDisposable");
            Check(reader.GetConstructor(new[] { typeof(Stream), typeof(bool) }) != null,
                "68A-9: McapIndexedReader has Stream/leaveOpen constructor");
            Check(reader.GetMethod("OpenRead", new[] { typeof(string) }) != null,
                "68A-10: McapIndexedReader exposes OpenRead(string)");
            var messageList = typeof(List<>).MakeGenericType(typeof(McapMessage));
            Check(reader.GetMethod("ReadMessages", new[] { options, messageList }) != null,
                "68A-11: McapIndexedReader exposes ReadMessages options overload");
            Check(reader.GetMethod("ReadAttachment", new[] { typeof(McapAttachmentIndex) }) != null,
                "68A-12: McapIndexedReader exposes ReadAttachment");
            Check(reader.GetMethod("ReadMetadata", new[] { typeof(McapMetadataIndex) }) != null,
                "68A-13: McapIndexedReader exposes ReadMetadata");
        }

        private static void VerifySummaryEnumeration()
        {
            using var ms = CreateSummaryFixture();
            using var indexed = new McapIndexedReader(ms, leaveOpen: true);

            Check(indexed.Summary != null, "68B-1: indexed reader exposes summary");
            Check(indexed.Channels.Count == 2, "68B-2: indexed reader exposes two channels");
            Check(indexed.Channels.Any(c => c.Topic == "/phase68/a"),
                "68B-3: summary channels include /phase68/a");
            Check(indexed.Channels.Any(c => c.Topic == "/phase68/b"),
                "68B-4: summary channels include /phase68/b");
            Check(indexed.AttachmentIndexes.Count == 1,
                "68B-5: indexed reader exposes attachment index");
            Check(indexed.MetadataIndexes.Count == 1,
                "68B-6: indexed reader exposes metadata index");
        }

        private static void VerifyIndexedRecordHelpers()
        {
            using var ms = CreateSummaryFixture();
            using var indexed = new McapIndexedReader(ms, leaveOpen: true);

            var attachment = indexed.ReadAttachment(indexed.AttachmentIndexes[0]);
            Check(attachment.Name == "phase68.txt", "68C-1: attachment name roundtrips");
            Check(Encoding.UTF8.GetString(attachment.Data) == "phase68",
                "68C-2: attachment payload roundtrips");
            Check(attachment.CrcValid, "68C-3: attachment CRC is valid");

            var metadata = indexed.ReadMetadata(indexed.MetadataIndexes[0]);
            Check(metadata.Name == "phase68.metadata", "68C-4: metadata name roundtrips");
            Check(metadata.Metadata.TryGetValue("value", out var value) && value.Contains("\"ok\":true"),
                "68C-5: metadata value roundtrips");
        }

        private static MemoryStream CreateSummaryFixture()
        {
            var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms))
            {
                recorder.AddChannel(1, "/phase68/a", "json", "phase68.A", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(2, "/phase68/b", "json", "phase68.B", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"a\":1}"));
                recorder.WriteMessage(2, 20, Encoding.UTF8.GetBytes("{\"b\":2}"));
                recorder.WriteMetadata("phase68.metadata", "{\"ok\":true}");
                recorder.AddAttachment("phase68.txt", "text/plain", Encoding.UTF8.GetBytes("phase68"), logTimeNs: 123, createTimeNs: 0);
                recorder.Close();
            }

            ms.Position = 0;
            return ms;
        }

        private static void VerifyMessageQueries()
        {
            using var ms = CreateMessageQueryFixture();
            using var indexed = new McapIndexedReader(ms, leaveOpen: true);

            var all = indexed.ReadMessages();
            CheckTimes(all, new ulong[] { 10, 20, 30, 40, 50, 60 },
                "68D-1: default query returns all messages sorted by log time");

            var topicA = indexed.ReadMessages(new McapReadOptions
            {
                Topics = new List<string> { "/phase68/a" }
            });
            CheckTimes(topicA, new ulong[] { 10, 30, 50 },
                "68D-2: topic filter returns only topic A");

            var channelB = indexed.ReadMessages(new McapReadOptions
            {
                ChannelIds = new List<ushort> { 2 }
            });
            CheckTimes(channelB, new ulong[] { 20, 40, 60 },
                "68D-3: channel filter returns only channel 2");

            var union = indexed.ReadMessages(new McapReadOptions
            {
                Topics = new List<string> { "/phase68/a" },
                ChannelIds = new List<ushort> { 2 }
            });
            CheckTimes(union, new ulong[] { 10, 20, 30, 40, 50, 60 },
                "68D-4: topic and channel filters use union semantics");

            var timeRange = indexed.ReadMessages(new McapReadOptions
            {
                StartTimeNs = 20,
                EndTimeNs = 50
            });
            CheckTimes(timeRange, new ulong[] { 20, 30, 40, 50 },
                "68D-5: inclusive time range filters messages");

            var inverted = indexed.ReadMessages(new McapReadOptions
            {
                StartTimeNs = 50,
                EndTimeNs = 20
            });
            Check(inverted.Count == 0, "68D-6: inverted time range returns empty");

            var unknown = indexed.ReadMessages(new McapReadOptions
            {
                Topics = new List<string> { "/phase68/missing" }
            });
            Check(unknown.Count == 0, "68D-7: unknown topic returns empty");

            var latest = indexed.ReadMessages(new McapReadOptions
            {
                MaxMessages = 2
            });
            CheckTimes(latest, new ulong[] { 50, 60 },
                "68D-8: MaxMessages keeps latest matches in chronological order");

            var reusable = new List<McapMessage> { new McapMessage { LogTime = 999 } };
            var returned = indexed.ReadMessages(new McapReadOptions
            {
                Topics = new List<string> { "/phase68/a" }
            }, reusable);
            Check(object.ReferenceEquals(reusable, returned), "68D-9: reusable result list is returned");
            CheckTimes(reusable, new ulong[] { 10, 30, 50 },
                "68D-10: reusable result list is cleared before filling");
        }

        private static MemoryStream CreateMessageQueryFixture()
        {
            var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms))
            {
                recorder.AddChannel(1, "/phase68/a", "json", "phase68.A", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(2, "/phase68/b", "json", "phase68.B", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"a\":10}"));
                recorder.WriteMessage(2, 20, Encoding.UTF8.GetBytes("{\"b\":20}"));
                recorder.WriteMessage(1, 30, Encoding.UTF8.GetBytes("{\"a\":30}"));
                recorder.WriteMessage(2, 40, Encoding.UTF8.GetBytes("{\"b\":40}"));
                recorder.WriteMessage(1, 50, Encoding.UTF8.GetBytes("{\"a\":50}"));
                recorder.WriteMessage(2, 60, Encoding.UTF8.GetBytes("{\"b\":60}"));
                recorder.Close();
            }

            ms.Position = 0;
            return ms;
        }

        private static void CheckTimes(List<McapMessage> messages, ulong[] expected, string name)
        {
            var actual = messages.Select(m => m.LogTime).ToArray();
            Check(actual.SequenceEqual(expected), name);
        }

        private static void CheckMessagesSorted(List<McapMessage> messages, string name)
        {
            var sorted = messages
                .OrderBy(m => m.LogTime)
                .ThenBy(m => m.ChannelId)
                .ThenBy(m => m.Sequence)
                .ThenBy(m => m.PublishTime)
                .ToList();
            Check(messages.SequenceEqual(sorted), name);
        }

        private static void VerifyStreamOwnership()
        {
            Check(Throws<ArgumentNullException>(() => new McapIndexedReader(null)),
                "68E-1: constructor rejects null stream");
            Check(Throws<NotSupportedException>(() => new McapIndexedReader(new NonSeekableStream(CreateSummaryFixture()))),
                "68E-2: constructor rejects non-seekable stream");

            var callerOwned = CreateSummaryFixture();
            using (var indexed = new McapIndexedReader(callerOwned, leaveOpen: true))
            {
                Check(indexed.Summary != null, "68E-3: leaveOpen reader opens caller stream");
            }
            Check(callerOwned.CanRead && callerOwned.CanSeek,
                "68E-4: leaveOpen true keeps stream open after Dispose");
            callerOwned.Dispose();

            var owned = CreateSummaryFixture();
            var ownedReader = new McapIndexedReader(owned, leaveOpen: false);
            ownedReader.Dispose();
            Check(!owned.CanRead, "68E-5: leaveOpen false disposes owned stream");
        }

        private static void VerifyMalformedChunkIsolation()
        {
            var corruptedBytes = CreateTwoChunkFixtureBytes(out var firstChunkEndTime, out var secondChunkStartTime);

            using (var firstOnly = new McapIndexedReader(new MemoryStream(corruptedBytes), leaveOpen: false))
            {
                var firstMessages = firstOnly.ReadMessages(new McapReadOptions
                {
                    StartTimeNs = 0,
                    EndTimeNs = firstChunkEndTime
                });
                CheckTimes(firstMessages, new ulong[] { 10 },
                    "68F-1: query skips out-of-range corrupted chunk");
            }

            using (var includesCorrupt = new McapIndexedReader(new MemoryStream(corruptedBytes), leaveOpen: false))
            {
                Check(Throws<InvalidDataException>(() => includesCorrupt.ReadMessages(new McapReadOptions
                {
                    StartTimeNs = secondChunkStartTime,
                    EndTimeNs = ulong.MaxValue
                })), "68F-2: query including corrupted chunk throws");
            }
        }

        private static void VerifyOpenReadDisposesStreamWhenSummaryReadFails()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase68_invalid_" + Guid.NewGuid().ToString("N") + ".mcap");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("not an mcap"));

            try
            {
                Check(Throws<Exception>(() => McapIndexedReader.OpenRead(path)),
                    "68E-6: OpenRead invalid file throws");
                File.Delete(path);
                Check(!File.Exists(path), "68E-7: OpenRead releases invalid file handle after failure");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static byte[] CreateTwoChunkFixtureBytes(out ulong firstChunkEndTime, out ulong secondChunkStartTime)
        {
            using var clean = new MemoryStream();
            using (var recorder = new McapRecorder(clean, chunkSizeBytes: 80))
            {
                recorder.AddChannel(1, "/phase68/corrupt", "json", "phase68.Corrupt", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes(new string('a', 64)));
                recorder.WriteMessage(1, 100, Encoding.UTF8.GetBytes(new string('b', 64)));
                recorder.Close();
            }

            clean.Position = 0;
            var summary = new McapReader(clean).ReadSummary();
            Check(summary.ChunkIndexes.Count >= 2, "68F-0: malformed fixture has at least two chunks");

            var firstChunk = summary.ChunkIndexes[0];
            var secondChunk = summary.ChunkIndexes[1];
            firstChunkEndTime = firstChunk.MessageEndTime;
            secondChunkStartTime = secondChunk.MessageStartTime;

            var bytes = clean.ToArray();
            var corruptOffset = checked((int)(secondChunk.ChunkStartOffset + secondChunk.ChunkLength - 1));
            bytes[corruptOffset] ^= 0x01;
            return bytes;
        }

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private sealed class NonSeekableStream : Stream
        {
            private readonly Stream _inner;

            public NonSeekableStream(Stream inner)
            {
                _inner = inner;
            }

            public override bool CanRead => _inner.CanRead;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
                _inner.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return _inner.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
