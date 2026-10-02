// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140C camera JPEG allocation checks.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "140C")]
    [Trait("Domain", "Sensors")]
    public sealed class CameraJpegEncoderAllocationTests
    {
        [Fact]
        public void ManagedJpegEncoderUsesPooledFlipScratch()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/ManagedJpegEncoder.cs");

            Assert.Contains("ArrayPool<byte>.Shared.Rent", source, StringComparison.Ordinal);
            Assert.Contains("ArrayPool<byte>.Shared.Return", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new byte[expectedBytes]", source, StringComparison.Ordinal);
        }

        [Fact]
        public void VerticalFlipMatchesPreflippedInput()
        {
            const int width = 3;
            const int height = 2;
            var rgb24 = new byte[]
            {
                255, 0, 0, 0, 255, 0, 0, 0, 255,
                7, 11, 13, 17, 19, 23, 29, 31, 37
            };
            var preflipped = FlipRows(rgb24, width, height);

            var encodedFromInternalFlip = ManagedJpegEncoder.EncodeRgb24(rgb24, width, height, 90, flipVertical: true);
            var encodedFromPreflippedInput = ManagedJpegEncoder.EncodeRgb24(preflipped, width, height, 90, flipVertical: false);

            Assert.Equal(encodedFromPreflippedInput, encodedFromInternalFlip);
        }

        [Fact]
        public void LiveOrphanedWorkerBlocksPipelineRestart()
        {
            using var releaseOrphan = new ManualResetEventSlim(false);
            var orphan = new Thread(() => releaseOrphan.Wait()) { IsBackground = true };
            orphan.Start();

            var pipeline = new CameraJpegPipeline(() => 1, workerStopWaitMs: 1);
            var orphanField = typeof(CameraJpegPipeline).GetField(
                "_orphanedWorker",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(orphanField);
            orphanField.SetValue(pipeline, orphan);

            try
            {
                Assert.False(pipeline.Start());
                Assert.Contains("previous JPEG worker", pipeline.LastStartError, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                releaseOrphan.Set();
                Assert.True(orphan.Join(TimeSpan.FromSeconds(2)));
                pipeline.Dispose();
            }
        }

        [Fact]
        public void CameraReadbackTimingKeepsTheNewestRequestsAndClearForgetsThem()
        {
            var timing = new CameraReadbackTiming();
            var oneSecondAgo = System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency;
            for (var request = 1UL; request <= 9UL; request++)
                timing.Remember(request, oneSecondAgo);

            Assert.Equal(0d, timing.TakeLatencyMs(1UL));
            Assert.True(timing.TakeLatencyMs(2UL) >= 1_000d);
            Assert.Equal(0d, timing.TakeLatencyMs(2UL));
            Assert.True(timing.TakeLatencyMs(9UL) >= 1_000d);

            timing.Clear();
            Assert.Equal(0d, timing.TakeLatencyMs(3UL));
            timing.Remember(10UL, oneSecondAgo);
            Assert.True(timing.TakeLatencyMs(10UL) >= 1_000d);
        }

        [Fact]
        public void CameraPipelineResizeMigratesNewestRequestsUnderOneQueueGate()
        {
            using var pipeline = new CameraJpegPipeline(() => 1, workerStopWaitMs: 1);
            pipeline.Configure(3, 1);
            pipeline.Queue(Request(1));
            pipeline.Queue(Request(2));
            pipeline.Queue(Request(3));

            pipeline.Configure(2, 1);

            var queueField = typeof(CameraJpegPipeline).GetField(
                "_encodeQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(queueField);
            var queue = (DropOldestBoundedQueue<JpegEncodeRequest>)queueField.GetValue(pipeline);
            var pending = queue.DrainSnapshot();
            Assert.Equal(2, pending.Length);
            Assert.Equal(2UL, pending[0].CaptureUnixNs);
            Assert.Equal(3UL, pending[1].CaptureUnixNs);
        }

        [Fact]
        public void JpegQueueAdmitsNewestFrameAndReportsReplacedFrame()
        {
            using var pipeline = new CameraJpegPublishPipeline(() => 1, new CameraPublishDiagnostics());
            pipeline.EnsureQueues(1, 1);
            var drops = 0;

            Assert.True(pipeline.TryQueueFrame(
                new byte[] { 1, 2, 3 },
                1UL,
                1,
                1,
                publishWebSocket: false,
                publishProvider: false,
                publishNativeFrame: false,
                PublisherEffectiveEncoding.Json,
                readbackLatencyMs: 0d,
                jpegQuality: 90,
                frameId: "frame",
                maxEncodedBytes: 0,
                onEncodeQueueDrop: () => drops++));
            Assert.True(pipeline.TryQueueFrame(
                new byte[] { 4, 5, 6 },
                2UL,
                1,
                1,
                publishWebSocket: false,
                publishProvider: false,
                publishNativeFrame: false,
                PublisherEffectiveEncoding.Json,
                readbackLatencyMs: 0d,
                jpegQuality: 90,
                frameId: "frame",
                maxEncodedBytes: 0,
                onEncodeQueueDrop: () => drops++));

            Assert.Equal(1, drops);
            Assert.Equal(1, pipeline.EncodeQueueDepth);
        }

        [Fact]
        public void ResizingBoundedQueueRetainsNewestItemsForMigration()
        {
            var queue = new DropOldestBoundedQueue<int>(3);
            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);

            var snapshot = queue.DrainSnapshot();
            var resized = new DropOldestBoundedQueue<int>(2);
            for (var index = Math.Max(0, snapshot.Length - resized.Capacity); index < snapshot.Length; index++)
                resized.Enqueue(snapshot[index]);

            Assert.Equal(new[] { 2, 3 }, new[]
            {
                Dequeue(resized),
                Dequeue(resized)
            });
            Assert.Equal(0, queue.Count);
        }

        private static JpegEncodeRequest Request(ulong timestampNs)
            => new JpegEncodeRequest(
                new byte[3],
                1,
                1,
                90,
                timestampNs,
                "frame",
                publishWebSocket: false,
                publishProvider: false,
                publishNativeFrame: false,
                PublisherEffectiveEncoding.Json,
                maxEncodedBytes: 0,
                generation: 1,
                jpegWorkerGeneration: 0);

        private static int Dequeue(DropOldestBoundedQueue<int> queue)
        {
            Assert.True(queue.TryDequeue(out var value));
            return value;
        }

        private static byte[] FlipRows(byte[] source, int width, int height)
        {
            var stride = checked(width * 3);
            var flipped = new byte[checked(stride * height)];
            for (var y = 0; y < height; y++)
                Buffer.BlockCopy(source, y * stride, flipped, (height - 1 - y) * stride, stride);
            return flipped;
        }

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "README.md"))
                        && Directory.Exists(Path.Combine(dir.FullName, "Unity2Foxglove"))
                        && Directory.Exists(Path.Combine(dir.FullName, "Packages")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }
    }
}
