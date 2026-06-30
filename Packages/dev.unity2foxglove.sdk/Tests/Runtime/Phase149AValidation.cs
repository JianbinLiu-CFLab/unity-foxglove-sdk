// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 149A validation for lazy MCAP file-order iteration.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase149AValidation
    {
        private static int _passCount;
        private static byte[] _indexedFixtureBytes;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 149A Tests ---");
            _passCount = 0;

            VerifyPublicApiShape();
            VerifyReaderLazyMatchesEagerFileOrder();
            VerifyReaderLazyFiltersAndLimitsInFileOrder();
            VerifyDataLoaderLazyIteratorMapsMessages();
            VerifyUnindexedLatestBeforeScansCorrectLatestPerChannel();
            VerifyLazyReaderRejectsSortedOrders();
            VerifyLazyReaderFallsBackForUnindexedFiles();
            VerifyLazyEnumerablesAreSinglePass();
            VerifyDisposedLoaderStopsLazyEnumeration();
            VerifyEmptyDataLoaderLazyIteratorIsSinglePass();
            VerifySourceShapePreventsFakeLazyImplementation();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 149A: " + _passCount + " checks passed.\n");
        }

        private static void VerifyPublicApiShape()
        {
            Check(typeof(McapIndexedReader).GetMethod("EnumerateMessages", new[] { typeof(McapReadOptions) }) != null,
                "149A-1: McapIndexedReader exposes lazy message enumeration");
            Check(typeof(McapDataLoader).GetMethod("CreateLazyIterator", new[] { typeof(McapDataLoaderQuery) }) != null,
                "149A-2: McapDataLoader exposes lazy iterator creation");
        }

        private static void VerifyReaderLazyMatchesEagerFileOrder()
        {
            using var ms = CreateIndexedFixture();
            using var reader = new McapIndexedReader(ms, leaveOpen: true);
            var options = new McapReadOptions
            {
                Order = McapReadOrder.FileOrder,
                MaxMessages = 0
            };

            var eager = reader.ReadMessages(options).Select(message => message.LogTime).ToArray();
            var lazy = reader.EnumerateMessages(options).Select(message => message.LogTime).ToArray();

            Check(eager.SequenceEqual(new ulong[] { 50, 10, 40, 20, 30 }),
                "149A-3: eager FileOrder fixture is intentionally not log-time sorted");
            Check(lazy.SequenceEqual(eager),
                "149A-4: lazy reader matches eager FileOrder output");
        }

        private static void VerifyReaderLazyFiltersAndLimitsInFileOrder()
        {
            using var ms = CreateIndexedFixture();
            using var reader = new McapIndexedReader(ms, leaveOpen: true);
            var messages = reader.EnumerateMessages(new McapReadOptions
            {
                Order = McapReadOrder.FileOrder,
                Topics = new List<string> { "/phase149a/a" },
                StartTimeNs = 30,
                EndTimeNs = 50,
                MaxMessages = 2
            }).ToList();

            Check(messages.Select(message => message.LogTime).SequenceEqual(new ulong[] { 50, 40 }),
                "149A-5: lazy reader applies topic, time, and MaxMessages filters without sorting");
        }

        private static void VerifyDataLoaderLazyIteratorMapsMessages()
        {
            using var ms = CreateIndexedFixture();
            using var loader = new McapDataLoader(ms, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var messages = loader.CreateLazyIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase149a/b" },
                MaxMessages = 0
            }).ToList();

            Check(messages.Select(message => message.LogTime).SequenceEqual(new ulong[] { 10, 20 }),
                "149A-6: DataLoader lazy iterator preserves matching file order");
            Check(messages.All(message => message.Topic == "/phase149a/b" && message.MessageEncoding == "json"),
                "149A-7: DataLoader lazy iterator maps channel metadata");
        }

        private static void VerifyLazyReaderRejectsSortedOrders()
        {
            using var ms = CreateIndexedFixture();
            using var reader = new McapIndexedReader(ms, leaveOpen: true);

            Check(Throws<NotSupportedException>(() => reader.EnumerateMessages(new McapReadOptions
                {
                    Order = McapReadOrder.LogTimeAscending
                })),
                "149A-10: lazy reader rejects log-time ascending requests");
            Check(Throws<NotSupportedException>(() => reader.EnumerateMessages(new McapReadOptions
                {
                    Order = McapReadOrder.LogTimeDescending
                })),
                "149A-11: lazy reader rejects log-time descending requests");
        }

        private static void VerifyUnindexedLatestBeforeScansCorrectLatestPerChannel()
        {
            using var ms = CreateUnindexedMultiChannelFixture();
            using var reader = new McapIndexedReader(ms, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var latest = reader.ReadLatestBefore(new McapReadOptions
            {
                EndTimeNs = 30
            });

            Check(latest.Select(message => message.ChannelId).SequenceEqual(new ushort[] { 1, 2 })
                    && latest.Select(message => message.LogTime).SequenceEqual(new ulong[] { 30, 20 })
                    && latest.Select(message => Encoding.UTF8.GetString(message.Data)).SequenceEqual(new[] { "{\"value\":\"a30\"}", "{\"value\":\"b20\"}" }),
                "149A-8: unindexed latest-before returns the correct per-channel latest messages from out-of-order file data");

            var exclusive = reader.ReadLatestBefore(new McapReadOptions
            {
                EndTimeNs = 40,
                UseOfficialEndTimeSemantics = true
            });
            Check(exclusive.Select(message => message.LogTime).SequenceEqual(new ulong[] { 30, 20 }),
                "149A-9: unindexed latest-before honors official exclusive end-time semantics");
        }

        private static void VerifyLazyReaderFallsBackForUnindexedFiles()
        {
            using var ms = CreateUnindexedFixture();
            using var reader = new McapIndexedReader(ms, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var messages = reader.EnumerateMessages(new McapReadOptions
            {
                Order = McapReadOrder.FileOrder
            }).ToList();

            Check(messages.Select(message => message.LogTime).SequenceEqual(new ulong[] { 1 })
                    && messages.Select(message => Encoding.UTF8.GetString(message.Data)).SequenceEqual(new[] { "{\"value\":\"unindexed\"}" }),
                "149A-12: lazy reader falls back to sequential scan when chunk indexes are absent");

            ms.Position = 0;
            using var strictReader = new McapIndexedReader(ms, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            Check(Throws<InvalidOperationException>(() => strictReader.EnumerateMessages(new McapReadOptions
                  {
                      Order = McapReadOrder.FileOrder,
                      AllowLinearFallback = false
                  }).ToList()),
                "149A-13: lazy reader rejects files without chunk indexes when AllowLinearFallback=false");
        }

        private static void VerifyLazyEnumerablesAreSinglePass()
        {
            using var readerStream = CreateIndexedFixture();
            using var reader = new McapIndexedReader(readerStream, leaveOpen: true);
            var readerLazy = reader.EnumerateMessages(new McapReadOptions
            {
                Order = McapReadOrder.FileOrder
            });
            using (readerLazy.GetEnumerator())
            {
                Check(Throws<InvalidOperationException>(() => readerLazy.GetEnumerator()),
                    "149A-14: reader lazy enumerable rejects a second enumeration");
            }

            using var loaderStream = CreateIndexedFixture();
            using var loader = new McapDataLoader(loaderStream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var loaderLazy = loader.CreateLazyIterator(new McapDataLoaderQuery { MaxMessages = 0 });
            using (loaderLazy.GetEnumerator())
            {
                Check(Throws<InvalidOperationException>(() => loaderLazy.GetEnumerator()),
                    "149A-15: DataLoader lazy enumerable rejects a second enumeration");
            }
        }

        private static void VerifyDisposedLoaderStopsLazyEnumeration()
        {
            using var ms = CreateIndexedFixture();
            var loader = new McapDataLoader(ms, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var lazy = loader.CreateLazyIterator(new McapDataLoaderQuery { MaxMessages = 0 });
            using var enumerator = lazy.GetEnumerator();

            loader.Dispose();

            Check(Throws<ObjectDisposedException>(() => enumerator.MoveNext()),
                "149A-16: lazy DataLoader iterator observes disposal before first read");
        }

        private static void VerifyEmptyDataLoaderLazyIteratorIsSinglePass()
        {
            using var ms = CreateIndexedFixture();
            using var loader = new McapDataLoader(ms, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var lazy = loader.CreateLazyIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase149a/missing" },
                MaxMessages = 0
            });

            using (lazy.GetEnumerator())
            {
                Check(Throws<InvalidOperationException>(() => lazy.GetEnumerator()),
                    "149A-17: empty DataLoader lazy iterator keeps the single-pass contract");
            }
        }

        private static void VerifySourceShapePreventsFakeLazyImplementation()
        {
            var dataLoader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs");
            var indexedReader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapIndexedReader.cs");
            var dataLoaderLazy = SourceMember(
                dataLoader,
                "public IEnumerable<McapDataLoaderMessage> CreateLazyIterator");
            var readerLazyCore = SourceMember(
                indexedReader,
                "private IEnumerable<McapMessage> EnumerateMessagesCore");
            var indexedFileOrderCore = SourceMember(
                indexedReader,
                "private IEnumerable<McapMessage> EnumerateIndexedMessagesInFileOrder");

            Check(dataLoaderLazy.Contains("McapLazyMessageEnumerable", StringComparison.Ordinal)
                    && !dataLoaderLazy.Contains("ReadMessages(", StringComparison.Ordinal),
                "149A-18: DataLoader lazy API does not materialize through ReadMessages");
            Check(readerLazyCore.Contains("EnumerateMessagesInFileOrder", StringComparison.Ordinal)
                    && !readerLazyCore.Contains("ReadChunkRecords", StringComparison.Ordinal),
                "149A-19: indexed lazy core delegates to the file-order message pipeline");
            Check(indexedFileOrderCore.Contains("yield return", StringComparison.Ordinal)
                    && indexedFileOrderCore.Contains("ReadChunkRecords", StringComparison.Ordinal)
                    && indexedFileOrderCore.Contains("EnumerateChunkMessages", StringComparison.Ordinal),
                "149A-20: shared indexed file-order pipeline yields from chunk reads");
            Check(!readerLazyCore.Contains("ApplyOrderingAndLimit", StringComparison.Ordinal)
                    && !readerLazyCore.Contains("ReadLinearMessages", StringComparison.Ordinal),
                "149A-21: lazy core avoids eager sorting and keeps fallback outside the core iterator loop");
            Check(!indexedReader.Contains("_linearMessagesCache", StringComparison.Ordinal)
                    && indexedReader.Contains("VisitSequentialMessages", StringComparison.Ordinal),
                "149A-22: unindexed latest-before scans without retaining a reader-wide message cache");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase149a"),
                "149A-23: validation registry exposes Phase 149A");
        }

        private static MemoryStream CreateIndexedFixture()
        {
            _indexedFixtureBytes ??= CreateIndexedFixtureBytes();
            return new MemoryStream(_indexedFixtureBytes, writable: false);
        }

        private static byte[] CreateIndexedFixtureBytes()
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, null, chunkSizeBytes: 96, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase149a/a", "json", "phase149a.A", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(2, "/phase149a/b", "json", "phase149a.B", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 50, Payload("a50"));
                recorder.WriteMessage(2, 10, Payload("b10"));
                recorder.WriteMessage(1, 40, Payload("a40"));
                recorder.WriteMessage(2, 20, Payload("b20"));
                recorder.WriteMessage(1, 30, Payload("a30"));
                recorder.Close();
            }

            return ms.ToArray();
        }

        private static MemoryStream CreateUnindexedFixture()
        {
            var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, null, new McapWriterOptions
                {
                    UseChunking = false
                }, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase149a/unindexed", "json", "phase149a.Unindexed", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 1, Payload("unindexed"));
                recorder.Close();
            }

            ms.Position = 0;
            return ms;
        }

        private static MemoryStream CreateUnindexedMultiChannelFixture()
        {
            var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, null, new McapWriterOptions
                {
                    UseChunking = false,
                    IndexTypes = McapIndexTypes.None
                }, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase149a/latest-a", "json", "phase149a.LatestA", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(2, "/phase149a/latest-b", "json", "phase149a.LatestB", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 30, Payload("a30"));
                recorder.WriteMessage(2, 20, Payload("b20"));
                recorder.WriteMessage(1, 10, Payload("a10"));
                recorder.WriteMessage(2, 40, Payload("b40"));
                recorder.WriteMessage(1, 25, Payload("a25"));
                recorder.Close();
            }

            ms.Position = 0;
            return ms;
        }

        private static byte[] Payload(string value)
            => Encoding.UTF8.GetBytes("{\"value\":\"" + value + "\"}");

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath));
        }

        private static string SourceMember(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;
            var braceStart = source.IndexOf('{', start);
            if (braceStart < 0)
                return string.Empty;

            var depth = 0;
            for (var i = braceStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            return source.Substring(start);
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

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
