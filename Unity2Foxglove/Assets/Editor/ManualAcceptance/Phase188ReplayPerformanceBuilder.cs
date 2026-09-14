// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
                var result = new List<McapMessage>();
                var stopwatch = Stopwatch.StartNew();
                engine.Snapshot(engine.EndTimeNs, result);
                stopwatch.Stop();
                var metrics = engine.LastSnapshotMetrics;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                File.WriteAllText(output, JsonConvert.SerializeObject(new
                {
                    fixture,
                    elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    returnedMessages = result.Count,
                    eligibleChunks = metrics.EligibleChunks,
                    skippedChunks = metrics.SkippedChunks,
                    decompressedChunks = metrics.DecompressedChunks,
                    headersScanned = metrics.HeadersScanned,
                    payloadCopies = metrics.PayloadCopies,
                    payloadBytesCopied = metrics.PayloadBytesCopied
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
    }
}
#endif
