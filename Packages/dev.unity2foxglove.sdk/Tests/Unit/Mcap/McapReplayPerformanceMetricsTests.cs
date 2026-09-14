// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace FoxgloveSdk.UnitTests.Mcap
{
    public sealed class McapReplayPerformanceMetricsTests
    {
        [Fact]
        public void SnapshotReportsStructuralCountersAndOwnedCopyCount()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-metrics-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(
                    stream,
                    null,
                    new McapWriterOptions
                    {
                        UseChunking = true,
                        ChunkSizeBytes = 128,
                        IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message
                    },
                    leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/metrics", "json", "phase188.Metrics", "jsonschema", "{}");
                    for (ulong time = 10; time <= 100; time += 10)
                        recorder.WriteMessage(1, time, new byte[] { (byte)time });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = engine.Snapshot(50, new List<McapMessage>());
                var metrics = engine.LastSnapshotMetrics;

                Assert.Single(result);
                Assert.Equal(50UL, result[0].LogTime);
                Assert.True(metrics.EligibleChunks > 0);
                Assert.True(metrics.DecompressedChunks > 0);
                Assert.True(metrics.HeadersScanned >= 5);
                Assert.True(metrics.CandidateUpdates >= 1);
                Assert.Equal(5, metrics.PayloadCopies);
                Assert.Equal(5, metrics.PayloadBytesCopied);
                Assert.Equal(1, metrics.ReturnedMessages);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
