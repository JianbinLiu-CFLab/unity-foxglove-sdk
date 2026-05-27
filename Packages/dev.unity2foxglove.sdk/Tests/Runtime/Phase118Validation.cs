// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 118 validation for MCAP DataLoader hardening and performance harness coverage.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase118Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 118: MCAP DataLoader Hardening And Performance Baseline ===");
            _passed = 0;

            VerifyInitializationAndQueryHardening();
            VerifyBackfillEdges();
            VerifyPerformanceHarnessCoverage();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 118: {_passed} checks passed.");
        }

        private static void VerifyInitializationAndQueryHardening()
        {
            using var ms = CreateIndexedFixture();
            using var loader = new McapDataLoader(ms, leaveOpen: true);
            var first = loader.Initialize();
            var second = loader.Initialize();
            Check(object.ReferenceEquals(first, second), "118-A1: Initialize preserves cached same-reference behavior");

            first.Channels.Clear();
            var messages = loader.CreateIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase118/a" }
            }).ToList();
            CheckTimes(messages, new ulong[] { 10, 30, 50 },
                "118-A2: caller-mutated initialization DTO does not corrupt topic iteration");
            Check(messages.All(m => m.Topic == "/phase118/a" && m.SchemaId == 1),
                "118-A3: caller-mutated initialization DTO does not corrupt message channel projection");

            Check(loader.CreateIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase118/missing" }
            }).Count() == 0, "118-A4: unknown topic filter returns empty");
            Check(loader.CreateIterator(new McapDataLoaderQuery
            {
                ChannelIds = new List<ushort> { 99 }
            }).Count() == 0, "118-A5: unknown channel filter returns empty");
            Check(loader.CreateIterator(new McapDataLoaderQuery
            {
                StartTimeNs = 50,
                EndTimeNs = 20
            }).Count() == 0, "118-A6: inverted time range returns empty");
        }

        private static void VerifyBackfillEdges()
        {
            using (var ms = CreateIndexedFixture())
            using (var loader = new McapDataLoader(ms, leaveOpen: true))
            {
                loader.Initialize();
                Check(loader.GetBackfill(new McapDataLoaderBackfillQuery
                {
                    TimeNs = 5,
                    ChannelIds = new List<ushort> { 1, 2 }
                }).Count == 0, "118-B1: backfill before start returns empty");

                CheckChannelTimes(loader.GetBackfill(new McapDataLoaderBackfillQuery
                {
                    TimeNs = 50,
                    ChannelIds = new List<ushort> { 1, 2 }
                }), new[] { "1:50", "2:40" },
                    "118-B2: backfill target time is inclusive");

                CheckChannelTimes(loader.GetBackfill(new McapDataLoaderBackfillQuery
                {
                    TimeNs = 999,
                    ChannelIds = new List<ushort> { 1, 2 }
                }), new[] { "1:50", "2:60" },
                    "118-B3: backfill after end returns latest per channel");
            }

            using (var sparse = CreateSparseFixture())
            using (var loader = new McapDataLoader(sparse, leaveOpen: true))
            {
                loader.Initialize();
                CheckChannelTimes(loader.GetBackfill(new McapDataLoaderBackfillQuery
                {
                    TimeNs = 100,
                    ChannelIds = new List<ushort> { 1, 2, 3 }
                }), new[] { "1:10", "3:90" },
                    "118-B4: sparse backfill returns only channels with hits");
            }

            using (var direct = CreateDirectFixture())
            using (var loader = new McapDataLoader(direct, leaveOpen: true))
            {
                loader.Initialize();
                CheckChannelTimes(loader.GetBackfill(new McapDataLoaderBackfillQuery
                {
                    TimeNs = 45,
                    Topics = new List<string> { "/phase118/direct/a", "/phase118/direct/b" }
                }), new[] { "1:30", "2:40" },
                    "118-B5: backfill works over Phase117 direct-message fallback");
            }
        }

        private static void VerifyPerformanceHarnessCoverage()
        {
            var result = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Performance/PerformanceResult.cs");
            foreach (var field in new[]
            {
                "fixtureKind", "fixturePath", "channelCount", "schemaCount",
                "selectedChannelCount", "selectedTopicCount", "queryStartTimeNs",
                "queryEndTimeNs", "returnedMessageCount", "backfillHitCount", "fixtureBytes",
                "thresholdsEvaluated", "thresholdNotes"
            })
            {
                Check(result.Contains(field, StringComparison.Ordinal),
                    "118-C1: performance result schema exposes " + field);
            }

            var runner = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Performance/PerformanceRunner.cs");
            foreach (var scenario in new[]
            {
                "McapDataLoaderInitializeIndexed",
                "McapDataLoaderInitializeDirect",
                "McapDataLoaderIterateAllIndexed",
                "McapDataLoaderIterateTopicFilterIndexed",
                "McapDataLoaderIterateTimeWindowIndexed",
                "McapDataLoaderIterateAllDirect",
                "McapDataLoaderBackfillIndexed",
                "McapDataLoaderBackfillSparse"
            })
            {
                Check(runner.Contains(scenario, StringComparison.Ordinal),
                    "118-C2: performance quick mode includes " + scenario);
            }

            Check(runner.Contains("build\", \"performance\", \"fixtures", StringComparison.Ordinal)
                  && runner.Contains("CreateDataLoaderIndexedFixture", StringComparison.Ordinal)
                  && runner.Contains("CreateDataLoaderDirectFixture", StringComparison.Ordinal)
                  && runner.Contains("CreateDataLoaderSparseFixture", StringComparison.Ordinal),
                "118-C3: performance harness creates deterministic DataLoader fixtures under build/performance/fixtures");

            Check(runner.Contains("ApplyThresholds(result, thresholds)", StringComparison.Ordinal)
                  && runner.Contains("RunThresholdSelfTest", StringComparison.Ordinal)
                  && runner.Contains("ResolveThresholdConfigForMode", StringComparison.Ordinal)
                  && runner.Contains("passPathOk", StringComparison.Ordinal)
                  && !runner.Contains("_activeThresholds", StringComparison.Ordinal)
                  && runner.Contains("maxAllocatedBytesPerMessage", StringComparison.Ordinal)
                  && runner.Contains("minMessagesPerSecond", StringComparison.Ordinal),
                "118-C4: performance harness enforces configurable mode-aware regression thresholds without static threshold state");

            var entryPoint = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Performance/Program.cs");
            Check(entryPoint.Contains("--thresholds", StringComparison.Ordinal)
                  && entryPoint.Contains("--threshold-self-test", StringComparison.Ordinal)
                  && entryPoint.Contains("Performance thresholds:", StringComparison.Ordinal)
                  && entryPoint.Contains("could not be loaded; using built-in", StringComparison.Ordinal),
                "118-C5: performance entry point exposes threshold config, self-test gates, and bad-config fallback");

            var thresholds = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Performance/performance-thresholds.json");
            Check(thresholds.Contains("\"defaults\"", StringComparison.Ordinal)
                  && thresholds.Contains("\"scenarios\"", StringComparison.Ordinal)
                  && thresholds.Contains("\"modes\"", StringComparison.Ordinal)
                  && thresholds.Contains("\"full\"", StringComparison.Ordinal)
                  && thresholds.Contains("\"maxElapsedMs\"", StringComparison.Ordinal)
                  && thresholds.Contains("\"minMessagesPerSecond\"", StringComparison.Ordinal)
                  && thresholds.Contains("\"maxAllocatedBytesTotal\"", StringComparison.Ordinal)
                  && thresholds.Contains("\"maxGen0Collections\"", StringComparison.Ordinal)
                  && thresholds.Contains("\"maxGen1Collections\"", StringComparison.Ordinal),
                "118-C6: default performance threshold file is checked in with quick/full mode coverage");
        }

        private static void VerifyValidationWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("--phase118", StringComparison.Ordinal)
                  && registry.Contains("Phase118Validation.Validate", StringComparison.Ordinal),
                "118-D1: PhaseValidationRegistry wires --phase118");
            Check(project.Contains("Phase118Validation.cs", StringComparison.Ordinal),
                "118-D2: runtime test project compiles Phase118Validation");
        }

        private static MemoryStream CreateIndexedFixture()
        {
            var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase118/a", "json", "phase118.A", "jsonschema", "{}");
                recorder.AddChannel(2, "/phase118/b", "json", "phase118.B", "jsonschema", "{}");
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

        private static MemoryStream CreateSparseFixture()
        {
            var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase118/sparse/a", "json", "phase118.A", "jsonschema", "{}");
                recorder.AddChannel(2, "/phase118/sparse/empty", "json", "phase118.Empty", "jsonschema", "{}");
                recorder.AddChannel(3, "/phase118/sparse/c", "json", "phase118.C", "jsonschema", "{}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"a\":10}"));
                recorder.WriteMessage(3, 90, Encoding.UTF8.GetBytes("{\"c\":90}"));
                recorder.Close();
            }

            ms.Position = 0;
            return ms;
        }

        private static MemoryStream CreateDirectFixture()
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase118-direct");
                writer.WriteSchema(1, "phase118.Direct", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase118/direct/a", "json", new Dictionary<string, string>());
                writer.WriteChannel(2, 1, "/phase118/direct/b", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 10, 10, Encoding.UTF8.GetBytes("{\"a\":10}"));
                writer.WriteMessage(2, 1, 20, 20, Encoding.UTF8.GetBytes("{\"b\":20}"));
                writer.WriteMessage(1, 2, 30, 30, Encoding.UTF8.GetBytes("{\"a\":30}"));
                writer.WriteMessage(2, 2, 40, 40, Encoding.UTF8.GetBytes("{\"b\":40}"));
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            ms.Position = 0;
            return ms;
        }

        private static void CheckTimes(List<McapDataLoaderMessage> messages, ulong[] expected, string name)
        {
            var actual = messages.Select(m => m.LogTime).ToArray();
            Check(actual.SequenceEqual(expected), name);
        }

        private static void CheckChannelTimes(IReadOnlyList<McapDataLoaderMessage> messages, string[] expected, string name)
        {
            var actual = messages.Select(m => m.ChannelId + ":" + m.LogTime).ToArray();
            Check(actual.SequenceEqual(expected), name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase118 file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            return root;
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
