// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-11 validation for MCAP DataLoader, Remote File, and Replay Engine review fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_11Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-11: DataLoader Remote File Replay Integration ===");
            _passed = 0;

            DirectFileStreamRejectsWrongSourceId();
            InMemoryDataResponseHonorsCap();
            RemoteRangeWriterPreservesChannelIds();
            LazyDecodedIteratorIsSinglePassAndDecodes();
            SourceShapeGuards();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-11: {_passed} checks passed.");
        }

        private static void DirectFileStreamRejectsWrongSourceId()
        {
            var path = CreateDirectFixture("sourceid");
            var source = new RemoteMcapDataSourcePrototype(path, "phase163-11", "Phase163-11", "");

            using (var response = source.GetDirectFileStream(new RemoteMcapRequest { SourceId = "other" }))
            {
                Check(response.Status == RemoteMcapResponseStatus.NotFound,
                    "163-11A-1: direct-file stream rejects mismatched source id");
                Check(response.DataStream == null,
                    "163-11A-2: direct-file source-id rejection does not open the file");
            }
        }

        private static void InMemoryDataResponseHonorsCap()
        {
            var path = TempMcapHelper.CreatePath("phase163_11_cap");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("0123456789"));
            var source = new RemoteMcapDataSourcePrototype(path, "phase163-11", "Phase163-11", "", maxInMemoryDataBytes: 4);

            var response = source.GetData(new RemoteMcapRequest { SourceId = "phase163-11" });
            Check(response.Status == RemoteMcapResponseStatus.Unsupported,
                "163-11B-1: in-memory data response rejects files over cap");
            Check(response.Data == null || response.Data.Length == 0,
                "163-11B-2: in-memory cap rejection returns no oversized payload");
        }

        private static void RemoteRangeWriterPreservesChannelIds()
        {
            var path = CreateDirectFixture("range");
            using (var slice = RemoteMcapRangeWriter.CreateSlice(
                       path,
                       new RemoteMcapRequest { SourceId = "phase163-11", StartTimeNs = 0, EndTimeNs = 100 },
                       maxInMemoryDataBytes: -1))
            using (var reader = new McapIndexedReader(slice, leaveOpen: true))
            {
                var summary = reader.Summary;
                var channelIds = summary.Channels.Select(channel => channel.Id).OrderBy(id => id).ToArray();
                Check(channelIds.SequenceEqual(new ushort[] { 7, 9 }),
                    "163-11C-1: remote range MCAP slice preserves original channel ids");
                Check(reader.ReadMessages().Select(message => message.ChannelId).Distinct().OrderBy(id => id).SequenceEqual(channelIds),
                    "163-11C-2: remote range messages reference preserved channel ids");
            }
        }

        private static void LazyDecodedIteratorIsSinglePassAndDecodes()
        {
            var path = CreateDirectFixture("lazydecoded");
            using (var loader = new McapDataLoader(path, McapSequentialReadLimits.UnlimitedForTests))
            {
                var lazy = loader.CreateLazyDecodedIterator(new McapDataLoaderQuery { MaxMessages = 0 });
                var decoded = lazy.ToList();
                Check(decoded.Count == 2 && decoded.All(message => message.Payload?.Kind == McapDecodedPayloadKind.Json),
                    "163-11D-1: lazy decoded iterator decodes matching messages");
                Check(Throws<InvalidOperationException>(() => lazy.ToList()),
                    "163-11D-2: lazy decoded iterator remains single-pass");
            }
        }

        private static void SourceShapeGuards()
        {
            var dataSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapDataSourcePrototype.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapHttpServer.cs");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDecodeRegistry.cs");
            var replay = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");

            Check(dataSource.Contains("ReadAllBytesWithinCap", StringComparison.Ordinal)
                  && dataSource.Contains("info.Length != loadLength", StringComparison.Ordinal),
                "163-11E-1: remote data source guards cap and manifest cache fingerprint");
            Check(server.Contains("ContinueWith(_ => _stop.Dispose()", StringComparison.Ordinal),
                "163-11E-2: remote HTTP dispose defers CTS disposal until loop completion when needed");
            Check(registry.Contains("BuiltInFactories = CreateBuiltInFactoriesLazy()", StringComparison.Ordinal)
                  && registry.Contains("GetBuiltInFactories()", StringComparison.Ordinal),
                "163-11E-3: decoder registry refreshes built-in factories on Unity runtime reload");
            Check(replay.Contains("Instances are not thread-safe", StringComparison.Ordinal)
                  && replay.Contains("pathological files with very large same-timestamp groups", StringComparison.Ordinal),
                "163-11E-4: replay engine documents single-thread and soft-cap contracts");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_11Validation.cs", StringComparison.Ordinal),
                "163-11F-1: runtime test project compiles Phase163_11Validation");
            Check(registry.Contains("--phase163-11", StringComparison.Ordinal)
                  && registry.Contains("Phase163_11Validation.Validate", StringComparison.Ordinal),
                "163-11F-2: validation registry exposes --phase163-11");
        }

        private static string CreateDirectFixture(string label)
        {
            var path = TempMcapHelper.CreatePath("phase163_11_" + label);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new McapWriter(fs))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase163-11");
                writer.WriteSchema(3, "phase163_11.Direct", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(9, 3, "/phase163_11/b", "json", new Dictionary<string, string>());
                writer.WriteChannel(7, 3, "/phase163_11/a", "json", new Dictionary<string, string>());
                writer.WriteMessage(7, 1, 10, 10, Encoding.UTF8.GetBytes("{\"a\":10}"));
                writer.WriteMessage(9, 1, 20, 20, Encoding.UTF8.GetBytes("{\"b\":20}"));
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            return path;
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

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
