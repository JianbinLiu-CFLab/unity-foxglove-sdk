// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140E point-cloud hot-path allocation checks.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{


public sealed partial class PointCloudHotPathAllocationTests
    {
        [Fact]
        public void PointCloudPackedByteBufferPoolRemovesEmptyExactSizeBuckets()
        {
            var packedBuilder = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloudPackedDataBuilder.cs");

            Assert.Contains("BuffersBySize.Remove(length);", packedBuilder, StringComparison.Ordinal);
            Assert.Contains("BuffersBySize.Remove(emptySizeToRemove.Value);", packedBuilder, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "emptySizeToRemove = pair.Key;\n                        evicted = true;",
                packedBuilder.Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.True(
                packedBuilder.IndexOf("if (RetainedBytes + buffer.Length > MaxRetainedBytes)", StringComparison.Ordinal)
                < packedBuilder.IndexOf("if (!BuffersBySize.TryGetValue(buffer.Length", StringComparison.Ordinal));
        }

        [Fact]
        public void PackedPointCloudWorkerKeepsRawSlotWidthStableForPoolReuse()
        {
            var worker = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs");

            var rawPackStart = worker.IndexOf("var rawPackStart", StringComparison.Ordinal);
            var rawPackCall = worker.IndexOf(
                "var packed = PackedPointCloudDataBuilder.BuildVirtualLidarFullStridePooled",
                rawPackStart,
                StringComparison.Ordinal);
            var nextPayloadStage = worker.IndexOf("if (request.HasMotionCompensation)", rawPackCall, StringComparison.Ordinal);

            Assert.True(rawPackStart >= 0);
            Assert.True(rawPackCall >= rawPackStart);
            Assert.True(nextPayloadStage > rawPackCall);

            var rawPackBlock = worker.Substring(rawPackCall, nextPayloadStage - rawPackCall);
            Assert.Contains("useAcquisitionFrameCoordinates: true", rawPackBlock, StringComparison.Ordinal);
            Assert.Contains("preserveSourcePointCount: true", rawPackBlock, StringComparison.Ordinal);
            Assert.Contains("preferPooledBufferRetention: true", rawPackBlock, StringComparison.Ordinal);
            Assert.Contains("width: checked((uint)packed.PointCount)", worker, StringComparison.Ordinal);
            Assert.Contains("isDense: packed.ValidPointCount == packed.PointCount", worker, StringComparison.Ordinal);
            Assert.Contains("validCount: packed.ValidPointCount", worker, StringComparison.Ordinal);
        }

        [Fact]
        public void PackedPointCloudWorkerKeepsDeskewSlotWidthStableForPoolReuse()
        {
            var worker = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs");

            var scanReferenceStart = worker.IndexOf(
                "private static PackedPointCloudFrame BuildScanReferenceDeskewedPackedPointCloudFrame",
                StringComparison.Ordinal);
            var scanReferenceEnd = worker.IndexOf(
                "private static PackedPointCloudFrame BuildPackedPointCloudFrame(",
                scanReferenceStart,
                StringComparison.Ordinal);
            Assert.True(scanReferenceStart >= 0);
            Assert.True(scanReferenceEnd > scanReferenceStart);
            var scanReferenceBlock = worker.Substring(scanReferenceStart, scanReferenceEnd - scanReferenceStart);
            Assert.Contains("preserveSourcePointCount: true", scanReferenceBlock, StringComparison.Ordinal);

            var legacyCompensationStart = worker.IndexOf("TryCompensateVirtualLidarInto", StringComparison.Ordinal);
            var legacyPackCall = worker.IndexOf(
                "var compensatedPacked = PackedPointCloudDataBuilder.BuildVirtualLidarFullStridePooled",
                legacyCompensationStart,
                StringComparison.Ordinal);
            var legacyFrameBuild = worker.IndexOf(
                "motionCompensatedNativeFrame = BuildPackedPointCloudFrame",
                legacyPackCall,
                StringComparison.Ordinal);
            Assert.True(legacyCompensationStart >= 0);
            Assert.True(legacyPackCall > legacyCompensationStart);
            Assert.True(legacyFrameBuild > legacyPackCall);
            var legacyPackBlock = worker.Substring(legacyPackCall, legacyFrameBuild - legacyPackCall);
            Assert.Contains("preserveSourcePointCount: true", legacyPackBlock, StringComparison.Ordinal);
            Assert.Contains("preferPooledBufferRetention: true", legacyPackBlock, StringComparison.Ordinal);
        }

        [Fact]
        public void PackedPointCloudWorkerDoesNotReportSuccessAfterDeskewFailure()
        {
            var worker = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("success = string.IsNullOrEmpty(error);", worker, StringComparison.Ordinal);
            Assert.Contains("payloadBytes = nativeFrame.Data.Length;", worker, StringComparison.Ordinal);
            Assert.Contains("VirtualLidarPointData does not own managed references", worker, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloud2PreferredPooledBuffersCanEvictNoisyExactSizes()
        {
            PointCloudPackedByteBufferPool.ClearCachedBuffers();
            try
            {
                const int preferredLength = 983040;
                for (var i = 0; i < 80; i++)
                    PointCloudPackedByteBufferPool.Return(new byte[900000 + i]);

                PointCloudPackedByteBufferPool.Return(new byte[preferredLength], preferRetention: true);

                var rented = PointCloudPackedByteBufferPool.Rent(preferredLength, out var reused);

                Assert.True(reused);
                Assert.Equal(preferredLength, rented.Length);
            }
            finally
            {
                PointCloudPackedByteBufferPool.ClearCachedBuffers();
            }
        }

        [Fact]
        public void PackedPointCloudStableSourceWidthPoolConvergesAcrossVariableValidCounts()
        {
            PointCloudPackedByteBufferPool.ClearCachedBuffers();
            try
            {
                const int pointCount = 4096;
                const int expectedBytes = pointCount * 30;
                var points = new VirtualLidarPointData[pointCount];

                PopulateLidarPoints(points, validModulo: 2);
                var warmup = PackedPointCloudDataBuilder.BuildVirtualLidarFullStridePooled(
                    points,
                    pointCount,
                    emitAbsoluteTimeNs: true,
                    collectTimings: true,
                    out _,
                    useAcquisitionFrameCoordinates: true,
                    preserveSourcePointCount: true,
                    preferPooledBufferRetention: true);
                PointCloudPackedByteBufferPool.Return(warmup.Data, preferRetention: true);

                var reusedCount = 0;
                const int measuredRuns = 20;
                for (var run = 0; run < measuredRuns; run++)
                {
                    PopulateLidarPoints(points, validModulo: 2 + run % 5);
                    var packed = PackedPointCloudDataBuilder.BuildVirtualLidarFullStridePooled(
                        points,
                        pointCount,
                        emitAbsoluteTimeNs: true,
                        collectTimings: true,
                        out var timings,
                        useAcquisitionFrameCoordinates: true,
                        preserveSourcePointCount: true,
                        preferPooledBufferRetention: true);

                    Assert.Equal(expectedBytes, timings.BufferLength);
                    Assert.Equal(pointCount, packed.PointCount);
                    Assert.InRange(packed.ValidPointCount, 1, pointCount - 1);
                    if (timings.BufferReused)
                        reusedCount++;

                    PointCloudPackedByteBufferPool.Return(packed.Data, preferRetention: true);
                }

                Assert.True(reusedCount >= 19, $"Expected stable-width raw buffers to reuse after warmup; reused {reusedCount}/{measuredRuns}.");
            }
            finally
            {
                PointCloudPackedByteBufferPool.ClearCachedBuffers();
            }
        }

        [Fact]
        public void PointCloud2BuilderUsesArraySpecializedVirtualLidarPackPath()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PackedPointCloudDataBuilder.cs");

            Assert.Contains("BuildVirtualLidarFullStride(VirtualLidarPointData[] points", source, StringComparison.Ordinal);
            Assert.Contains("var validCount = CountValid(points, pointCount);", source, StringComparison.Ordinal);
            Assert.Contains("var packedPointCount = preserveSourcePointCount ? pointCount : validCount;", source, StringComparison.Ordinal);
            Assert.Contains("var capacity = ValidatePackedDataBudget(packedPointCount, stride);", source, StringComparison.Ordinal);
            Assert.Contains("private static int CountValid(VirtualLidarPointData[] points, int pointCount)", source, StringComparison.Ordinal);
            Assert.Contains("validCount++;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Array.Resize(ref data, offset);", source, StringComparison.Ordinal);
            Assert.Contains("for (var i = 0; i < pointCount; i++)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void VirtualLidarNativeSnapshotsArePooledAcrossWorkerOwnershipPaths()
        {
            var scheduler = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");
            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var pool = Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/VirtualLidarPointSnapshotPool.cs");
            var payloads = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerPayloads.cs");
            var backgroundPipeline = Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundEncodePipeline.cs");
            var pointCloudPipeline = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudEncodePipeline.cs");

            Assert.Contains("VirtualLidarPointSnapshotPool.Rent(scanBuffers.EffectiveRayCount)", scheduler, StringComparison.Ordinal);
            Assert.DoesNotContain("new VirtualLidarPointData[scanBuffers.EffectiveRayCount]", scheduler, StringComparison.Ordinal);
            Assert.Contains("EnsureActiveScanSnapshotCapacity()", lidar, StringComparison.Ordinal);
            Assert.Contains("ReleaseActiveScanSnapshot()", lidar, StringComparison.Ordinal);
            Assert.DoesNotContain("new VirtualLidarPointData[_scanBuffers.EffectiveRayCount]", lidar, StringComparison.Ordinal);
            Assert.Contains("ArrayPool<VirtualLidarPointData>.Shared.Rent", pool, StringComparison.Ordinal);
            Assert.Contains("ArrayPool<VirtualLidarPointData>.Shared.Return", pool, StringComparison.Ordinal);

            Assert.Contains("RecycleSourceSnapshot()", payloads, StringComparison.Ordinal);
            Assert.Contains("VirtualLidarPointSnapshotPool.Return", payloads, StringComparison.Ordinal);
            Assert.Contains("Action<TRequest> onDropRequest", backgroundPipeline, StringComparison.Ordinal);
            Assert.Contains("Action<TResult> onDropResult", backgroundPipeline, StringComparison.Ordinal);
            Assert.Contains("DropRequest(replacedRequest)", backgroundPipeline, StringComparison.Ordinal);
            Assert.Contains("DropResult(droppedResult)", backgroundPipeline, StringComparison.Ordinal);
            Assert.Contains("result.Request.RecycleSourceSnapshot", pointCloudPipeline, StringComparison.Ordinal);
        }

        [Fact]
        public void VirtualLidarFreezesOutputRepresentationUntilScanBoundary()
        {
            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanFramePublisher.cs");

            Assert.Contains("private VirtualLidarScanRepresentation _activeScanRepresentation;", lidar, StringComparison.Ordinal);
            Assert.Contains("_activeScanRepresentation = new VirtualLidarScanRepresentation(", lidar, StringComparison.Ordinal);
            Assert.Contains("_activeScanRepresentation.UseNativeSnapshot", lidar, StringComparison.Ordinal);
            Assert.Contains("_activeScanRepresentation.RequiresNativeAcquisitionFrame", lidar, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Time.fixedDeltaTime,\n                    UseNativePointCloudSnapshotPath(),",
                lidar,
                StringComparison.Ordinal);
            Assert.Contains("AppendValidSnapshotPoints", publisher, StringComparison.Ordinal);
            Assert.Contains("snapshot = null;", publisher, StringComparison.Ordinal);
            Assert.Contains("snapshotCount = 0;", publisher, StringComparison.Ordinal);
        }

        [Fact]
        public void NativeSnapshotFallbackPreservesEveryValidPointExactlyOnce()
        {
            var representation = new VirtualLidarScanRepresentation(
                useNativeSnapshot: true,
                requiresNativeAcquisitionFrame: false);
            var frame = new PointCloudFrame();
            var snapshot = new[]
            {
                new VirtualLidarPointData
                {
                    X = 1f,
                    Y = 2f,
                    Z = 3f,
                    Intensity = 4f,
                    Reflectivity = 5f,
                    TimeOffsetSeconds = 0.01f,
                    Ring = 6,
                    IsValid = 1
                },
                new VirtualLidarPointData { X = 99f, IsValid = 0 },
                new VirtualLidarPointData
                {
                    X = 7f,
                    Y = 8f,
                    Z = 9f,
                    Intensity = 10f,
                    Reflectivity = 11f,
                    TimeOffsetSeconds = 0.02f,
                    Ring = 12,
                    IsValid = 1
                }
            };

            var appended = representation.AppendValidSnapshotPoints(frame, snapshot, snapshot.Length);

            Assert.True(representation.UseNativeSnapshot);
            Assert.False(representation.RequiresNativeAcquisitionFrame);
            Assert.Equal(2, appended);
            Assert.Equal(2, frame.Points.Count);
            Assert.Equal(1f, frame.Points[0].X);
            Assert.Equal(6, frame.Points[0].Ring);
            Assert.Equal(7f, frame.Points[1].X);
            Assert.Equal(12, frame.Points[1].Ring);
        }

        [Fact]
        public void PointCloudEncodeWorkerStaysWarmAcrossIdleScanBoundaries()
        {
            var backgroundPipeline = Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundEncodePipeline.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("AutoResetEvent _workerSignal", backgroundPipeline, StringComparison.Ordinal);
            Assert.Contains("_workerSignal.Set();", backgroundPipeline, StringComparison.Ordinal);
            Assert.Contains("_workerSignal.WaitOne();", backgroundPipeline, StringComparison.Ordinal);
            Assert.DoesNotContain("if (request == null)\n                        {\n                            _worker.MarkStoppedIfCurrentLocked(workerGeneration);\n                            return;\n                        }", backgroundPipeline, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloudBackpressureDropsUseDiagnosticLogInsteadOfWarningStackTraces()
        {
            var pointCloudPipeline = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudEncodePipeline.cs");
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("private readonly Action<string> _logDropDiagnostic;", pointCloudPipeline, StringComparison.Ordinal);
            Assert.Contains("_logDropDiagnostic(_replacedPendingWarning);", pointCloudPipeline, StringComparison.Ordinal);
            Assert.Contains("_logDropDiagnostic(_droppedCompletedWarning(droppedCompletedResults));", pointCloudPipeline, StringComparison.Ordinal);
            Assert.DoesNotContain("_logWarning(_replacedPendingWarning);", pointCloudPipeline, StringComparison.Ordinal);
            Assert.DoesNotContain("_logWarning(_droppedCompletedWarning(droppedCompletedResults));", pointCloudPipeline, StringComparison.Ordinal);
            Assert.Contains("private static void LogPointCloudDropDiagnostic(string message)", publisher, StringComparison.Ordinal);
            Assert.Contains("Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, \"{0}\", message ?? string.Empty);", publisher, StringComparison.Ordinal);
            Assert.Contains("Debug.LogWarning,\n                    LogPointCloudDropDiagnostic,\n                    \"[Foxglove] Draco point-cloud encode request replaced", publisher, StringComparison.Ordinal);
            Assert.Contains("Debug.LogWarning,\n                    LogPointCloudDropDiagnostic,\n                    \"[Foxglove] PackedPointCloud native request replaced", publisher, StringComparison.Ordinal);
            Assert.DoesNotContain("Debug.LogWarning,\n                    Debug.Log,\n                    \"[Foxglove] PackedPointCloud native request replaced", publisher, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloud2BuilderPacksOnlyValidPointsIntoExactSizedData()
        {
            var points = new[]
            {
                new VirtualLidarPointData { X = 1f, Y = 2f, Z = 3f, Intensity = 0.25f, Reflectivity = 0.5f, Ring = 7, TimeOffsetSeconds = 0.001f, IsValid = 1 },
                new VirtualLidarPointData { X = 99f, Y = 99f, Z = 99f, IsValid = 0 },
                new VirtualLidarPointData { X = 4f, Y = 5f, Z = 6f, Intensity = 0.75f, Reflectivity = 0.125f, Ring = 8, TimeOffsetSeconds = 0.002f, IsValid = 1 }
            };

            var packed = PackedPointCloudDataBuilder.BuildVirtualLidarFullStride(points, emitAbsoluteTimeNs: true);

            Assert.Equal(30U, packed.PointStride);
            Assert.Equal(60, packed.Data.Length);
            using var reader = new BinaryReader(new MemoryStream(packed.Data));
            AssertPoint(reader, 1f, 2f, 3f, 0.25f, 0.5f, 7, 0.001f, 1_000_000U);
            AssertPoint(reader, 4f, 5f, 6f, 0.75f, 0.125f, 8, 0.002f, 2_000_000U);
            Assert.Equal(packed.Data.Length, reader.BaseStream.Position);
        }

        [Fact]
        public void PointCloud2BuilderCanPreserveSourceWidthWithInvalidNanRows()
        {
            var points = new[]
            {
                new VirtualLidarPointData { X = 1f, Y = 2f, Z = 3f, Intensity = 0.25f, Reflectivity = 0.5f, Ring = 7, TimeOffsetSeconds = 0.001f, IsValid = 1 },
                new VirtualLidarPointData { X = 99f, Y = 99f, Z = 99f, IsValid = 0 },
                new VirtualLidarPointData { X = 4f, Y = 5f, Z = 6f, Intensity = 0.75f, Reflectivity = 0.125f, Ring = 8, TimeOffsetSeconds = 0.002f, IsValid = 1 }
            };

            var packed = PackedPointCloudDataBuilder.BuildVirtualLidarFullStride(
                points,
                pointCount: points.Length,
                emitAbsoluteTimeNs: true,
                preserveSourcePointCount: true);

            Assert.Equal(30U, packed.PointStride);
            Assert.Equal(90, packed.Data.Length);
            Assert.Equal(3, packed.PointCount);
            Assert.Equal(2, packed.ValidPointCount);
            using var reader = new BinaryReader(new MemoryStream(packed.Data));
            AssertPoint(reader, 1f, 2f, 3f, 0.25f, 0.5f, 7, 0.001f, 1_000_000U);
            Assert.True(float.IsNaN(reader.ReadSingle()));
            Assert.True(float.IsNaN(reader.ReadSingle()));
            Assert.True(float.IsNaN(reader.ReadSingle()));
            Assert.Equal(0f, reader.ReadSingle());
            Assert.Equal(0f, reader.ReadSingle());
            Assert.Equal((ushort)0, reader.ReadUInt16());
            Assert.Equal(0f, reader.ReadSingle());
            Assert.Equal(0U, reader.ReadUInt32());
            AssertPoint(reader, 4f, 5f, 6f, 0.75f, 0.125f, 8, 0.002f, 2_000_000U);
            Assert.Equal(packed.Data.Length, reader.BaseStream.Position);
        }




    }
}
