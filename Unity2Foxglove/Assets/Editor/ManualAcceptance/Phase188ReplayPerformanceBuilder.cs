// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.IO;
using UnityEditor;

namespace Unity2Foxglove
{
    /// <summary>Batch-mode Editor probe for the Phase188 replay snapshot path.</summary>
    public static class Phase188ReplayPerformanceBuilder
    {
        public static void Run()
        {
            try
            {
                var fixture = GetArgument("-phase188Fixture");
                var output = GetArgument("-phase188Output");
                if (string.IsNullOrWhiteSpace(fixture) || string.IsNullOrWhiteSpace(output))
                    throw new ArgumentException("-phase188Fixture and -phase188Output are required.");

                using var engine = new McapReplayEngine();
                engine.Load(fixture);
                var operations = new List<object>();
                var result = new List<McapMessage>();
                var cursorSequence = 0L;
                double terminalElapsed = 0;
                long terminalReturned = 0, terminalEligible = 0, terminalSkipped = 0;
                long terminalDecompressed = 0, terminalHeaders = 0, terminalCopies = 0, terminalBytes = 0;
                string terminalDigest = null;
                var midpoint = engine.StartTimeNs + ((engine.EndTimeNs - engine.StartTimeNs) / 2);
                var targets = new[]
                {
                    Tuple.Create("first", engine.StartTimeNs),
                    Tuple.Create("forward-middle", midpoint),
                    Tuple.Create("forward-end", engine.EndTimeNs),
                    Tuple.Create("backward-middle", midpoint),
                    Tuple.Create("forward-late-jump", engine.EndTimeNs)
                };
                foreach (var target in targets)
                {
                    result.Clear();
                    var stopwatch = Stopwatch.StartNew();
                    engine.Snapshot(target.Item2, result);
                    stopwatch.Stop();
                    var metrics = engine.LastSnapshotMetrics;
                    terminalElapsed = stopwatch.Elapsed.TotalMilliseconds;
                    terminalReturned = result.Count;
                    terminalEligible = metrics.EligibleChunks;
                    terminalSkipped = metrics.SkippedChunks;
                    terminalDecompressed = metrics.DecompressedChunks;
                    terminalHeaders = metrics.HeadersScanned;
                    terminalCopies = metrics.PayloadCopies;
                    terminalBytes = metrics.PayloadBytesCopied;
                    terminalDigest = ComputeSnapshotDigest(result);
                    operations.Add(new
                    {
                        name = target.Item1,
                        requestedCursorSequence = ++cursorSequence,
                        requestedTimeNs = target.Item2,
                        snapshotDigestSha256 = terminalDigest,
                        elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                        returnedMessages = result.Count,
                        eligibleChunks = metrics.EligibleChunks,
                        skippedChunks = metrics.SkippedChunks,
                        decompressedChunks = metrics.DecompressedChunks,
                        headersScanned = metrics.HeadersScanned,
                        payloadCopies = metrics.PayloadCopies,
                        payloadBytesCopied = metrics.PayloadBytesCopied
                    });
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                File.WriteAllText(output, JsonConvert.SerializeObject(new
                {
                    fixture,
                    operations,
                    operationCount = operations.Count,
                    finalSnapshotDigestSha256 = terminalDigest,
                    lifecycle = new
                    {
                        preparationUnityFree = true,
                        ownerThreadApplication = "editor",
                        deterministicSequence = cursorSequence,
                        disposedByUsingScope = true
                    },
                    // Preserve the original top-level structural counters for
                    // existing consumers; they describe the terminal query.
                    elapsedMilliseconds = terminalElapsed,
                    returnedMessages = terminalReturned,
                    eligibleChunks = terminalEligible,
                    skippedChunks = terminalSkipped,
                    decompressedChunks = terminalDecompressed,
                    headersScanned = terminalHeaders,
                    payloadCopies = terminalCopies,
                    payloadBytesCopied = terminalBytes
                }, Formatting.Indented));
                UnityEngine.Debug.Log("PHASE188_EDITOR_PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError("PHASE188_EDITOR_FAIL " + exception.GetType().Name + ": " + exception.Message);
                EditorApplication.Exit(1);
            }
        }

        private static string GetArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                    return args[i + 1];
            return null;
        }

        private static string ComputeSnapshotDigest(IReadOnlyList<McapMessage> messages)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(messages.Count);
                for (var i = 0; i < messages.Count; i++)
                {
                    var message = messages[i];
                    writer.Write(message.ChannelId);
                    writer.Write(message.Sequence);
                    writer.Write(message.LogTime);
                    writer.Write(message.PublishTime);
                    if (message.Data == null)
                        writer.Write(-1);
                    else
                    {
                        writer.Write(message.Data.Length);
                        writer.Write(message.Data);
                    }
                }
            }
            stream.Position = 0;
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
#endif
