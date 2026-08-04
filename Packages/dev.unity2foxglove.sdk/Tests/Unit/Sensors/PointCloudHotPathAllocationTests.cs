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
    [Trait("Phase", "140E")]
    [Trait("Domain", "Sensors")]
    public sealed class PointCloudHotPathAllocationTests
    {
        [Fact]
        public void PointCloud2BuilderUsesOwnedArrayWithoutPooledFrameData()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PackedPointCloudDataBuilder.cs");

            Assert.Contains("var data = new byte[capacity];", source, StringComparison.Ordinal);
            Assert.DoesNotContain("stream.ToArray()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ArrayPool<byte>.Shared.Rent", source, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloud2BuilderWritesPackedBytesWithoutStreamWriters()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PackedPointCloudDataBuilder.cs");

            Assert.DoesNotContain("new MemoryStream", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new BinaryWriter", source, StringComparison.Ordinal);
            Assert.DoesNotContain("BitConverter.TryWriteBytes", source, StringComparison.Ordinal);
            Assert.Contains("WriteSingleLittleEndian", source, StringComparison.Ordinal);
            Assert.Contains("BitConverter.SingleToInt32Bits", source, StringComparison.Ordinal);
            Assert.Contains("BinaryPrimitives.WriteUInt16LittleEndian", source, StringComparison.Ordinal);
            Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian", source, StringComparison.Ordinal);
        }

        [Fact]
        public void PackedPointCloudWorkerPoolsDeskewScratchAndFinalFrameData()
        {
            var worker = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs");
            var compensator = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudMotionCompensator.cs");
            var packedBuilder = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PackedPointCloudDataBuilder.cs");
            var frame = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PackedPointCloudFrame.cs");
            var payloads = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerPayloads.cs");
            var pointCloudPipeline = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudEncodePipeline.cs");

            Assert.Contains("ArrayPool<VirtualLidarPointData>.Shared.Rent", worker, StringComparison.Ordinal);
            Assert.Contains("ArrayPool<VirtualLidarPointData>.Shared.Return", worker, StringComparison.Ordinal);
            Assert.Contains("finally", worker, StringComparison.Ordinal);
            Assert.Contains("TryCompensateVirtualLidarInto", worker, StringComparison.Ordinal);
            Assert.Contains("TryCompensateVirtualLidarInto", compensator, StringComparison.Ordinal);
            Assert.DoesNotContain("Dictionary<uint, Matrix4x4>", compensator, StringComparison.Ordinal);
            Assert.Contains("TryInterpolateMonotonic", compensator, StringComparison.Ordinal);
            Assert.Contains("lastOffsetNs", compensator, StringComparison.Ordinal);
            Assert.DoesNotContain("ArrayPool<byte>.Shared.Rent", worker, StringComparison.Ordinal);
            Assert.Contains("BuildVirtualLidarFullStridePooled", worker, StringComparison.Ordinal);
            Assert.Contains("PointCloudPackedByteBufferPool.Rent", packedBuilder, StringComparison.Ordinal);
            Assert.Contains("PointCloudPackedByteBufferPool.Return", packedBuilder, StringComparison.Ordinal);
            Assert.Contains("ownsPooledData", frame, StringComparison.Ordinal);
            Assert.Contains("internal void RecycleData()", frame, StringComparison.Ordinal);
            Assert.Contains("RecycleResultPayloads()", payloads, StringComparison.Ordinal);
            Assert.Contains("NativeFrame?.RecycleData()", payloads, StringComparison.Ordinal);
            Assert.Contains("MotionCompensatedNativeFrame?.RecycleData()", payloads, StringComparison.Ordinal);
            Assert.Contains("result.RecycleResultPayloads()", pointCloudPipeline, StringComparison.Ordinal);
        }

        [Fact]
        public void PackedPointCloudDeskewRateGateRunsBeforeMotionRequestCreation()
        {
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var nativePublisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.PackedPointCloud.cs");
            var motionPublisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.MotionCompensation.cs");
            var editor = Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");

            Assert.Contains("_deskewedPackedPointCloudMaxPublishRateHz = 2f", publisher, StringComparison.Ordinal);
            Assert.Contains("Deskewed Max Rate Hz", editor, StringComparison.Ordinal);
            Assert.Contains("ShouldQueueDeskewedPackedPointCloudFrame(unixNs)", nativePublisher, StringComparison.Ordinal);
            Assert.Contains("var queueDeskewedOutput = motionSettings.EmitDeskewedOutput", nativePublisher, StringComparison.Ordinal);
            Assert.Contains("var motionCompensation = queueDeskewedOutput", nativePublisher, StringComparison.Ordinal);
            Assert.True(
                nativePublisher.IndexOf("ShouldQueueDeskewedPackedPointCloudFrame(unixNs)", StringComparison.Ordinal)
                < nativePublisher.IndexOf("TryCreateMotionCompensationRequest(", StringComparison.Ordinal));
            Assert.Contains("FoxgloveTimeUtil.NowUnixTimeNs()", motionPublisher, StringComparison.Ordinal);
            Assert.Contains("_lastDeskewedPackedPointCloudPublishUnixNs = timestampNs", motionPublisher, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloud2DeskewRateGateUsesSeparateDiagnosticCounter()
        {
            var diagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudPublishDiagnostics.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var motionPublisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.MotionCompensation.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("private int _deskewRateSkips;", diagnostics, StringComparison.Ordinal);
            Assert.Contains("public void RecordDeskewRateSkip(bool enabled", diagnostics, StringComparison.Ordinal);
            Assert.Contains("deskewRateSkip={9}", diagnostics, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref _deskewRateSkips, 0);", diagnostics, StringComparison.Ordinal);
            Assert.Contains("_diagnostics.RecordDeskewRateSkip(_logPerformanceDiagnostics);", motionPublisher, StringComparison.Ordinal);
            Assert.DoesNotContain("_diagnostics.RecordDrop(_logPerformanceDiagnostics);\n                return false;", motionPublisher, StringComparison.Ordinal);
        }

        [Fact]
        public void NativeDracoRateGateUsesSeparateDiagnosticCounter()
        {
            var diagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudPublishDiagnostics.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var dracoPublisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Draco.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("private int _dracoRateSkips;", diagnostics, StringComparison.Ordinal);
            Assert.Contains("public void RecordDracoRateSkip(bool enabled", diagnostics, StringComparison.Ordinal);
            Assert.Contains("dracoRateSkip={8}", diagnostics, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref _dracoRateSkips, 0);", diagnostics, StringComparison.Ordinal);
            Assert.Contains("_diagnostics.RecordDracoRateSkip(_logPerformanceDiagnostics);", dracoPublisher, StringComparison.Ordinal);
            Assert.DoesNotContain("_diagnostics.RecordDrop(_logPerformanceDiagnostics);\n                return false;", dracoPublisher, StringComparison.Ordinal);
        }

        [Fact]
        public void SpikeDracoSidecarIsObsoleteAndNotUsedByProductionPublishers()
        {
            var sidecar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudEncoderSidecar.cs");
            var publishers = string.Concat(
                Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs"),
                Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Draco.cs"),
                Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs"));

            Assert.Contains("[Obsolete(\"DracoPointCloudEncoderSidecar is spike-only. Use DracoPointCloudNativeEncoder for production.\", false)]", sidecar, StringComparison.Ordinal);
            Assert.DoesNotContain("DracoPointCloudEncoderSidecar", publishers, StringComparison.Ordinal);
            Assert.Contains("DracoPointCloudNativeEncoder", publishers, StringComparison.Ordinal);
        }

        [Fact]
        public void NativeDracoRateGateThrottlesTenHzInputAndResetsOnBackwardClockJump()
        {
            const ulong tenHzStepNs = 100_000_000UL;
            const ulong sixHzIntervalNs = 166_666_667UL;
            const ulong startNs = 1_700_000_000_000_000_000UL;
            var lastPublishNs = 0UL;
            var acceptedInputIndexes = new List<int>();

            for (var i = 0; i <= 10; i++)
            {
                var timestampNs = startNs + (ulong)i * tenHzStepNs;
                if (PointCloudPublishRateGate.ShouldPublish(ref lastPublishNs, timestampNs, sixHzIntervalNs))
                    acceptedInputIndexes.Add(i);
            }

            Assert.Equal(new[] { 0, 2, 4, 6, 8, 10 }, acceptedInputIndexes);

            var jumpedBackwardNs = startNs + tenHzStepNs / 2UL;
            Assert.True(PointCloudPublishRateGate.ShouldPublish(ref lastPublishNs, jumpedBackwardNs, sixHzIntervalNs));
            Assert.Equal(jumpedBackwardNs, lastPublishNs);
            Assert.False(PointCloudPublishRateGate.ShouldPublish(ref lastPublishNs, jumpedBackwardNs + tenHzStepNs, sixHzIntervalNs));
            Assert.True(PointCloudPublishRateGate.ShouldPublish(ref lastPublishNs, jumpedBackwardNs + 2UL * tenHzStepNs, sixHzIntervalNs));
        }

        [Fact]
        public void NativeDracoRateGateRejectsZeroInterval()
        {
            var lastPublishNs = 0UL;

            Assert.Throws<ArgumentOutOfRangeException>(
                () => PointCloudPublishRateGate.ShouldPublish(ref lastPublishNs, 1UL, 0UL));
        }

        [Fact]
        public void PointCloud2PooledDeskewBuffersArePreferredOverOneShotRawSizes()
        {
            var packedBuilder = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloudPackedDataBuilder.cs");
            var pointCloud2Builder = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PackedPointCloudDataBuilder.cs");
            var worker = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs");
            var payloads = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerPayloads.cs");
            var frame = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PackedPointCloudFrame.cs");

            Assert.Contains("preferRetention", packedBuilder, StringComparison.Ordinal);
            Assert.Contains("MaxPreferredSizes", packedBuilder, StringComparison.Ordinal);
            Assert.Contains("EvictNonPreferredBuffersFor", packedBuilder, StringComparison.Ordinal);
            Assert.Contains("preferPooledBufferRetention", pointCloud2Builder, StringComparison.Ordinal);
            Assert.Contains("useAcquisitionFrameCoordinates: true", worker, StringComparison.Ordinal);
            Assert.Contains("preserveSourcePointCount: true", worker, StringComparison.Ordinal);
            Assert.Contains("preferPooledBufferRetention: true", worker, StringComparison.Ordinal);
            Assert.True(
                payloads.IndexOf("MotionCompensatedNativeFrame?.RecycleData()", StringComparison.Ordinal)
                < payloads.IndexOf("NativeFrame?.RecycleData()", StringComparison.Ordinal));
            Assert.Contains("PointCloudPackedByteBufferPool.Return(Data, _preferPooledDataRetention)", frame, StringComparison.Ordinal);
        }

        [Fact]
        public void ManagedPointCloudSerializationRecyclesPooledPackBuffers()
        {
            var packedBuilder = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloudPackedDataBuilder.cs");
            var messageBuilder = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/PointCloudMessageBuilder.cs");
            var rosPointCloud = Text("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrPointCloudBuilder.cs");
            var rosPointCloud2 = Text("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrSensorPointCloud2Builder.cs");

            Assert.Contains("internal void RecycleData()", packedBuilder, StringComparison.Ordinal);
            Assert.Contains("public static PointCloudPackedData BuildPooled", packedBuilder, StringComparison.Ordinal);
            Assert.Contains("PointCloudPackedByteBufferPool.Rent(capacity)", packedBuilder, StringComparison.Ordinal);
            Assert.Contains("PointCloudPackedByteBufferPool.Return(data)", packedBuilder, StringComparison.Ordinal);
            Assert.Contains("packed.RecycleData();", messageBuilder, StringComparison.Ordinal);
            Assert.Contains("packed.Dispose();", rosPointCloud, StringComparison.Ordinal);
            Assert.Contains("packed.Dispose();", rosPointCloud2, StringComparison.Ordinal);
            Assert.Contains("return Build(frame, layout);", packedBuilder, StringComparison.Ordinal);
        }

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

        [Fact]
        public void PackedPointCloudFrameTracksValidCountSeparatelyFromPublishedPointSlots()
        {
            var data = new byte[90];
            var fields = new[] { new PointCloudPackedField("x", 0, PointCloudPackedNumericType.Float32) };

            var frame = new PackedPointCloudFrame(
                unixNs: 1UL,
                frameId: "lidar",
                height: 1U,
                width: 3U,
                fields: fields,
                pointStep: 30U,
                data: data,
                isDense: false,
                validCount: 2);

            Assert.Equal(3U, frame.Width);
            Assert.Equal(90U, frame.RowStep);
            Assert.Equal(2, frame.ValidCount);
            Assert.False(frame.IsDense);
        }

        [Fact]
        public void PointCloud2BuilderCanPackReferenceFrameWithZeroedTimeOffsets()
        {
            var points = new[]
            {
                new VirtualLidarPointData { X = 1f, Y = 2f, Z = 3f, AcquisitionX = 9f, AcquisitionY = 9f, AcquisitionZ = 9f, HasAcquisitionFrame = 1, Intensity = 0.25f, Reflectivity = 0.5f, Ring = 7, TimeOffsetSeconds = 0.001f, IsValid = 1 },
                new VirtualLidarPointData { X = 4f, Y = 5f, Z = 6f, AcquisitionX = 8f, AcquisitionY = 8f, AcquisitionZ = 8f, HasAcquisitionFrame = 1, Intensity = 0.75f, Reflectivity = 0.125f, Ring = 8, TimeOffsetSeconds = 0.002f, IsValid = 1 }
            };

            var packed = PackedPointCloudDataBuilder.BuildVirtualLidarFullStride(
                points,
                pointCount: points.Length,
                emitAbsoluteTimeNs: true,
                useAcquisitionFrameCoordinates: false,
                zeroTimeOffset: true);

            Assert.Equal(30U, packed.PointStride);
            Assert.Equal(60, packed.Data.Length);
            using var reader = new BinaryReader(new MemoryStream(packed.Data));
            AssertPoint(reader, 1f, 2f, 3f, 0.25f, 0.5f, 7, 0f, 0U);
            AssertPoint(reader, 4f, 5f, 6f, 0.75f, 0.125f, 8, 0f, 0U);
            Assert.Equal(packed.Data.Length, reader.BaseStream.Position);
        }

        [Fact]
        public void DracoEncoderUsesPooledXyzWithoutSizingOutputFromRentalLength()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudNativeEncoder.cs");

            Assert.Contains("ArrayPool<float>.Shared.Rent", source, StringComparison.Ordinal);
            Assert.Contains("ArrayPool<float>.Shared.Return", source, StringComparison.Ordinal);
            Assert.Contains("finally", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new float[checked(pointCount * 3)]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("xyz.Length * sizeof(float)", source, StringComparison.Ordinal);
            Assert.Contains("pointCount * XyzBytesPerPoint", source, StringComparison.Ordinal);
            Assert.Contains("validCount * XyzBytesPerPoint", source, StringComparison.Ordinal);
            Assert.Contains("GCHandle.Alloc(xyz, GCHandleType.Pinned)", source, StringComparison.Ordinal);
        }

        private static void AssertPoint(
            BinaryReader reader,
            float x,
            float y,
            float z,
            float intensity,
            float reflectivity,
            ushort ring,
            float timeOffsetSeconds,
            uint absoluteTimeNs)
        {
            Assert.Equal(x, reader.ReadSingle());
            Assert.Equal(y, reader.ReadSingle());
            Assert.Equal(z, reader.ReadSingle());
            Assert.Equal(intensity, reader.ReadSingle());
            Assert.Equal(reflectivity, reader.ReadSingle());
            Assert.Equal(ring, reader.ReadUInt16());
            Assert.Equal(timeOffsetSeconds, reader.ReadSingle());
            Assert.Equal(absoluteTimeNs, reader.ReadUInt32());
        }

        private static void PopulateLidarPoints(VirtualLidarPointData[] points, int validModulo)
        {
            for (var i = 0; i < points.Length; i++)
            {
                var valid = i % validModulo != 0;
                points[i] = new VirtualLidarPointData
                {
                    X = i,
                    Y = i * 0.5f,
                    Z = i * 0.25f,
                    AcquisitionX = i,
                    AcquisitionY = i * 0.5f,
                    AcquisitionZ = i * 0.25f,
                    HasAcquisitionFrame = 1,
                    Intensity = 0.5f,
                    Reflectivity = 0.25f,
                    Ring = (ushort)(i % 128),
                    TimeOffsetSeconds = i * 0.000001f,
                    IsValid = valid ? (byte)1 : (byte)0
                };
            }
        }

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static readonly string RepoRoot = FindRepoRoot();

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                    || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }
}
