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
    public sealed partial class PointCloudHotPathAllocationTests
    {
        [Fact]
        public void DracoMsgPackFailureNeverPublishesProtobufFallback()
        {
            var msgPackCalls = 0;
            var protobufCalls = 0;

            Assert.False(DracoWebSocketPublicationPolicy.Publish(
                PublisherEffectiveEncoding.MsgPack,
                () =>
                {
                    msgPackCalls++;
                    return false;
                },
                () => protobufCalls++));
            Assert.Equal(1, msgPackCalls);
            Assert.Equal(0, protobufCalls);

            Assert.True(DracoWebSocketPublicationPolicy.Publish(
                PublisherEffectiveEncoding.Protobuf,
                () => { msgPackCalls++; return false; },
                () => protobufCalls++));
            Assert.Equal(1, msgPackCalls);
            Assert.Equal(1, protobufCalls);
        }

        [Fact]
        public void PointCloud2BuilderUsesOwnedArrayWithoutPooledFrameData()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PackedPointCloudDataBuilder.cs");

            Assert.Contains("var data = new byte[capacity];", source, StringComparison.Ordinal);
            Assert.DoesNotContain("stream.ToArray()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ArrayPool<byte>.Shared.Rent", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ProtobufCompositeDecodersParseNestedPayloadOnlyOnce()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunProtobufWire.cs");

            Assert.DoesNotContain("TryReadOptionalFloat(payload", source, StringComparison.Ordinal);
            Assert.Equal(4, CountOccurrences(source, "TryReadCompositeFields(payload"));
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
        public void PackedPointCloudEventDispatchContinuesAfterASubscriberThrows()
        {
            var frame = new PackedPointCloudFrame(
                unixNs: 1UL,
                frameId: "lidar",
                height: 1U,
                width: 1U,
                fields: Array.Empty<PointCloudPackedField>(),
                pointStep: 1U,
                data: new byte[1],
                isDense: true);
            var laterSubscriberCalls = 0;
            Exception observedFailure = null;
            Action<PackedPointCloudFrame> subscribers = _ =>
                throw new InvalidOperationException("first subscriber failed");
            subscribers += observedFrame =>
            {
                Assert.Same(frame, observedFrame);
                laterSubscriberCalls++;
            };

            PointCloudFrameEventDispatcher.Invoke(
                subscribers,
                frame,
                failure => observedFailure = failure);

            Assert.Equal(1, laterSubscriberCalls);
            Assert.IsType<InvalidOperationException>(observedFailure);
        }

        [Fact]
        public void PointCloudEncodePipelineRejectsNonPositiveFailureWarningIntervals()
        {
            PointCloudEncodePipeline<PackedPointCloudRequest, PackedPointCloudResult> pipeline = null;
            try
            {
                var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    pipeline = new PointCloudEncodePipeline<PackedPointCloudRequest, PackedPointCloudResult>(
                        threadName: "point-cloud-interval-test",
                        completedCapacity: 1,
                        workerStopWaitMs: 0,
                        encode: _ => null,
                        isSuccess: result => result.Success,
                        failureMessage: result => result.Error,
                        formatFailureWarning: message => message,
                        publishCompleted: _ => { },
                        logWarning: _ => { },
                        logDropDiagnostic: _ => { },
                        replacedPendingWarning: "replaced",
                        queueFailureMessagePrefix: "queue: ",
                        droppedCompletedWarning: count => count.ToString(),
                        workerShutdownWarning: "shutdown",
                        failureWarningIntervalFrames: 0));

                Assert.Equal("failureWarningIntervalFrames", error.ParamName);
            }
            finally
            {
                pipeline?.Dispose();
            }
        }

        [Fact]
        public void PackedPointCloudWorkerRecordsRawPayloadBuildTimingWhenEnabled()
        {
            var points = VirtualLidarPointSnapshotPool.Rent(1);
            points[0] = new VirtualLidarPointData
            {
                X = 1f,
                Y = 2f,
                Z = 3f,
                IsValid = 1
            };
            var request = new PackedPointCloudRequest(
                points,
                lidarPointCount: 1,
                unixNs: 1UL,
                frameId: "lidar",
                emitAbsoluteTimeNs: false,
                publishWebSocket: false,
                publishProvider: false,
                publishNativeFrame: true,
                webSocketEncoding: PublisherEffectiveEncoding.Protobuf,
                logPerformanceDiagnostics: true);
            PackedPointCloudResult result = null;

            try
            {
                result = PointCloudWorkerEncoders.EncodePackedPointCloudRequest(request);

                Assert.True(result.Success, result.Error);
                Assert.True(
                    result.RawPayloadBuildMs > 0d,
                    "Enabled raw payload diagnostics must report a measured duration.");
            }
            finally
            {
                result?.RecycleResultPayloads();
                request.RecycleSourceSnapshot();
            }
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
