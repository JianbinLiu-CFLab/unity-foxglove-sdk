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
