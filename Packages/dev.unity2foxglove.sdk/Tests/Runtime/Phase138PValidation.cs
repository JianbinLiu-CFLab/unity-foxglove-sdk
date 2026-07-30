// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138P code-review remediation regression coverage.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Regression checks for Phase 138P code-review remediation.
    /// </summary>
    public static class Phase138PValidation
    {
        private static int _passed;

        /// <summary>Runs all Phase 138P validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138P: Code Review Remediation ===");
            _passed = 0;

            SourceHygieneHasNoPhase138OPollution();
            VirtualImuUsesAxialAngularVelocityConversion();
            CameraVideoAccessUnitsUseResolvedFrameId();
            LidarScanTimingConvertsNormalizedOffsetsToSeconds();
            SensorExtrinsicMathRoundTripsAndComposes();
            NativePointCloud2PayloadFiltersInvalidRowsAndKeepsSeconds();
            SubscriptionRegistryUsesSetReverseIndex();
            McapCompressionPublicHelperRejectsOversizedOutput();
            McapReaderRejectsTooLowSummaryStart();
            StreamingReaderLimitsAttachmentsAndMetadata();
            PacketizersRejectOversizedPendingAccessUnits();
            VideoSidecarStderrReadersAreBounded();
            WorkerTimeoutPathsUseGenerations();
            CameraReadbackAndWorkerSignalsHaveExplicitLifecycleBoundaries();
            PackedPointCloudTfRotationConventionIsExplicit();

            Console.WriteLine($"Phase 138P: {_passed} checks passed.");
        }

        private static void SourceHygieneHasNoPhase138OPollution()
        {
            var pollutionRoots = new[]
            {
                "Packages/dev.unity2foxglove.sdk",
                "Packages/dev.unity2foxglove.ros2forunity",
                "Scripts",
                "Unity2Foxglove/Assets/Samples"
            };
            var forbidden = new[]
            {
                string.Concat("Summary text", " for this member"),
                string.Concat("TODO ", "XML"),
                string.Concat("TODO ", "placeholder"),
                "\uFFFD",
                "\u922B",
                "\u951F"
            };

            var result = ScanTrackedSourceHygiene(pollutionRoots, forbidden);

            Check(result.Polluted.Count == 0,
                "138P-1: tracked source has no placeholder XML comments, replacement characters, or known mojibake tokens"
                + (result.Polluted.Count == 0 ? "" : " (" + string.Join(", ", result.Polluted.Take(8)) + ")"));
            Check(result.Utf8BomFiles.Count == 0,
                "138P-2: tracked source files have no UTF-8 BOM"
                + (result.Utf8BomFiles.Count == 0 ? "" : " (" + string.Join(", ", result.Utf8BomFiles.Take(8)) + ")"));
        }

        private static void VirtualImuUsesAxialAngularVelocityConversion()
        {
            var converter = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Utilities/CoordinateConverter.cs");
            var imu = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var createSample = ExtractMethod(imu, "private static ImuSample CreateSample");

            Check(converter.Contains("UnityToFoxgloveAngularVelocity", StringComparison.Ordinal)
                  && converter.Contains("new UnityEngine.Vector3(-angular.z, angular.x, -angular.y)", StringComparison.Ordinal),
                "138P-3: CoordinateConverter exposes axial angular-velocity mapping");
            Check(createSample.Contains("UnityToFoxgloveAngularVelocity(angularBody)", StringComparison.Ordinal)
                  && !createSample.Contains("UnityToFoxglovePosition(angularBody)", StringComparison.Ordinal),
                "138P-4: VirtualImu converts gyro data with axial-vector mapping");
        }

        private static void CameraVideoAccessUnitsUseResolvedFrameId()
        {
            var camera = ReadCameraPublisherSources();
            var publishVideo = ExtractMethod(camera, "private void PublishVideoAccessUnit");

            Check(publishVideo.Contains("ResolveFrameId()", StringComparison.Ordinal)
                  && !publishVideo.Contains("_frameId,", StringComparison.Ordinal),
                "138P-5: compressed video access units use resolved sensor frame id");
        }

        private static void LidarScanTimingConvertsNormalizedOffsetsToSeconds()
        {
            var timingType = Type.GetType("Unity.FoxgloveSDK.Sensors.Lidar.LidarScanTiming, FoxgloveSdk.Tests");
            Check(timingType != null, "138P-6A: LiDAR timing helper exists in Unity-free runtime");

            var method = timingType.GetMethod("NormalizedOffsetToSeconds", BindingFlags.Public | BindingFlags.Static);
            Check(method != null, "138P-6B: LiDAR timing helper exposes NormalizedOffsetToSeconds");

            var halfAtTenHz = (float)method.Invoke(null, new object[] { 0.5f, 10.0 });
            Check(Math.Abs(halfAtTenHz - 0.05f) < 1e-6f,
                "138P-6C: normalized scan offsets are scaled by scan period");

            var scheduler = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");
            Check(scheduler.Contains("LidarScanTiming.NormalizedOffsetToSeconds(timeOffset, scanPattern.ScanRateHz)", StringComparison.Ordinal),
                "138P-6D: VirtualLidar stores point offsets as seconds before packing");
        }

        private static void SensorExtrinsicMathRoundTripsAndComposes()
        {
            var mathType = Type.GetType("Unity.FoxgloveSDK.Sensors.Lidar.LidarExtrinsicMath, FoxgloveSdk.Tests");
            Check(mathType != null, "138P-7A: Unity-free LiDAR extrinsic math helper exists");

            var sourceType = Type.GetType("Unity.FoxgloveSDK.Sensors.Lidar.LidarTIlExtrinsic, FoxgloveSdk.Tests");
            Check(sourceType != null, "138P-7B: LiDAR extrinsic value type is available");

            var compose = mathType.GetMethod("Compose", BindingFlags.Public | BindingFlags.Static);
            var invert = mathType.GetMethod("Invert", BindingFlags.Public | BindingFlags.Static);
            var transformPoint = mathType.GetMethod("TransformPoint", BindingFlags.Public | BindingFlags.Static);
            Check(compose != null && invert != null && transformPoint != null,
                "138P-7C: LiDAR extrinsic helper exposes compose, invert, and point transform");

            dynamic a = Activator.CreateInstance(sourceType,
                new System.Numerics.Vector3(1.0f, 2.0f, -0.5f),
                System.Numerics.Quaternion.CreateFromYawPitchRoll(0.2f, -0.1f, 0.3f));
            dynamic b = Activator.CreateInstance(sourceType,
                new System.Numerics.Vector3(-0.2f, 0.7f, 0.4f),
                System.Numerics.Quaternion.CreateFromYawPitchRoll(-0.4f, 0.05f, 0.15f));
            dynamic c = Activator.CreateInstance(sourceType,
                new System.Numerics.Vector3(0.6f, -0.3f, 1.1f),
                System.Numerics.Quaternion.CreateFromYawPitchRoll(0.1f, 0.25f, -0.2f));

            dynamic identity = compose.Invoke(null, new object[] { a, invert.Invoke(null, new object[] { a }) });
            Check(NearlyZero((System.Numerics.Vector3)identity.TranslationMeters)
                  && NearlyIdentity((System.Numerics.Quaternion)identity.Rotation),
                "138P-7D: composing an extrinsic with its inverse round-trips to identity");

            dynamic left = compose.Invoke(null, new object[] { compose.Invoke(null, new object[] { a, b }), c });
            dynamic right = compose.Invoke(null, new object[] { a, compose.Invoke(null, new object[] { b, c }) });
            var p = new System.Numerics.Vector3(0.25f, -1.5f, 0.75f);
            var leftPoint = (System.Numerics.Vector3)transformPoint.Invoke(null, new object[] { left, p });
            var rightPoint = (System.Numerics.Vector3)transformPoint.Invoke(null, new object[] { right, p });
            Check(NearlyEqual(leftPoint, rightPoint),
                "138P-7E: LiDAR extrinsic composition is associative on sample points");
        }

        private static void NativePointCloud2PayloadFiltersInvalidRowsAndKeepsSeconds()
        {
            var nativePoints = new[]
            {
                new VirtualLidarPointData { X = 1f, Y = 2f, Z = 3f, Intensity = 0.5f, Reflectivity = 0.25f, Ring = 7, TimeOffsetSeconds = 0.05f, IsValid = 1 },
                new VirtualLidarPointData { X = 9f, Y = 9f, Z = 9f, Intensity = 9f, Reflectivity = 9f, Ring = 99, TimeOffsetSeconds = 9f, IsValid = 0 },
                new VirtualLidarPointData { X = 4f, Y = 5f, Z = 6f, Intensity = 0.75f, Reflectivity = 0.5f, Ring = 8, TimeOffsetSeconds = 0.075f, IsValid = 1 }
            };

            var packed = PackedPointCloudDataBuilder.BuildVirtualLidarFullStride(nativePoints, emitAbsoluteTimeNs: true);
            Check(packed.Data.Length == 60, "138P-8A: native PointCloud2 packing keeps only valid rows");
            Check(ReadSingle(packed.Data, 22) == 0.05f, "138P-8B: first valid row stores seconds offset");
            Check(ReadUInt32(packed.Data, 26) == ExpectedNanoseconds(0.05f), "138P-8C: absolute t field is derived from seconds");
            Check(ReadSingle(packed.Data, 52) == 0.075f, "138P-8D: second valid row stores seconds offset after compaction");
            Check(ReadUInt32(packed.Data, 56) == ExpectedNanoseconds(0.075f), "138P-8E: compacted second row absolute t is derived from seconds");

            var packedBuilder = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloudPackedDataBuilder.cs");
            var nsMethod = ExtractMethod(packedBuilder, "internal static uint TimeOffsetSecondsToNanoseconds");
            Check(!nsMethod.Contains("decimal", StringComparison.Ordinal),
                "138P-8F: hot-path time-offset nanosecond conversion avoids decimal arithmetic");

            var converter = typeof(PointCloudPackedDataBuilder).GetMethod(
                "TimeOffsetSecondsToNanoseconds",
                BindingFlags.NonPublic | BindingFlags.Static);
            Check((uint)converter.Invoke(null, new object[] { 0.0000000006f }) == 1U,
                "138P-8G: nanosecond conversion rounds sub-nanosecond offsets away from zero");
            Check((uint)converter.Invoke(null, new object[] { 1.2345678f }) == ExpectedNanoseconds(1.2345678f),
                "138P-8H: nanosecond conversion uses double-first precision");
            Check((uint)converter.Invoke(null, new object[] { 4.294967f }) == ExpectedNanoseconds(4.294967f),
                "138P-8I: nanosecond conversion avoids premature float-product clamp");
            Check((uint)converter.Invoke(null, new object[] { 10.0f }) == uint.MaxValue,
                "138P-8J: nanosecond conversion clamps oversized offsets");
            Check((uint)converter.Invoke(null, new object[] { float.NaN }) == 0U,
                "138P-8K: nanosecond conversion rejects invalid offsets");

            var managed = new PointCloudFrame { EmitAbsoluteTimeNs = true };
            managed.Points.Add(new PointCloudPoint(1f, 2f, 3f) { Intensity = 0.5f, Reflectivity = 0.25f, Ring = 7, TimeOffsetSeconds = 0.05f });
            managed.Points.Add(new PointCloudPoint(4f, 5f, 6f) { Intensity = 0.75f, Reflectivity = 0.5f, Ring = 8, TimeOffsetSeconds = 0.075f });
            var managedPacked = PointCloudPackedDataBuilder.Build(managed);
            Check(ReadUInt32(managedPacked.Data, 26) == ReadUInt32(packed.Data, 26)
                  && ReadUInt32(managedPacked.Data, 56) == ReadUInt32(packed.Data, 56),
                "138P-8L: managed and native PointCloud packers emit matching absolute t fields");
        }

        private static void SubscriptionRegistryUsesSetReverseIndex()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/SubscriptionRegistry.cs");
            Check(source.Contains("Dictionary<uint, HashSet<(uint clientId, uint subscriptionId)>>", StringComparison.Ordinal)
                  && !source.Contains("new HashSet<(uint, uint)>()", StringComparison.Ordinal)
                  && !source.Contains("subscribers.Contains((clientId, subscriptionId))", StringComparison.Ordinal),
                "138P-9A: SubscriptionRegistry reverse index is set-backed without per-copy dedupe allocation");

            var registry = new SubscriptionRegistry();
            Check(registry.TryAddSubscriptions(1, new[] { (10U, 42U), (10U, 42U) }, out _, out _),
                "138P-9B: duplicate subscribe batch is accepted after dedupe");
            var subscribers = registry.GetSubscribersForChannel(42);
            Check(subscribers.Count == 1 && subscribers[0].clientId == 1U && subscribers[0].subscriptionId == 10U,
                "138P-9C: duplicate subscription appears once in channel snapshot");
            var removed = registry.RemoveChannel(42);
            Check(removed.Count == 1 && !registry.HasSubscribersForChannel(42) && registry.ClientCount == 0,
                "138P-9D: set-backed reverse index removes channel subscribers cleanly");
        }

        private static void McapCompressionPublicHelperRejectsOversizedOutput()
        {
            var overload = typeof(McapCompression).GetMethod(
                "Decompress",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), typeof(byte[]), typeof(int), typeof(int) },
                modifiers: null);
            Check(overload != null, "138P-10A: public MCAP decompress helper exposes max-output overload");

            Check(Throws<InvalidDataException>(() =>
                overload.Invoke(null, new object[] { "", new byte[] { 1, 2 }, 2, 1 })),
                "138P-10B: public MCAP decompress helper rejects oversized output before returning");
            Check(McapCompression.Decompress("", new byte[] { 1, 2 }, 2).Length == 2,
                "138P-10C: existing three-argument decompress overload remains compatible");
        }

        private static void McapReaderRejectsTooLowSummaryStart()
        {
            using var stream = CreateMcapWithPatchedSummaryStart(1UL);
            Check(ThrowsWithMessage<InvalidDataException>(
                    () => new McapReader(stream).ReadSummary(),
                    "summary_start"),
                "138P-11: MCAP footer rejects non-zero summary_start before the data section");
        }

        private static void StreamingReaderLimitsAttachmentsAndMetadata()
        {
            var limitsType = typeof(McapSequentialReadLimits);
            Check(limitsType.GetField("MaxAttachmentRecords") != null
                  && limitsType.GetField("MaxAttachmentBytes") != null
                  && limitsType.GetField("MaxMetadataRecords") != null
                  && limitsType.GetField("MaxMetadataBytes") != null,
                "138P-12A: sequential MCAP limits include attachment and metadata caps");

            using (var stream = CreateStreamingMcap(attachmentCount: 2, attachmentBytes: 4, metadataCount: 0, metadataValueBytes: 0))
            {
                var limits = McapSequentialReadLimits.UnlimitedForTests;
                SetField(limits, "MaxAttachmentRecords", 1);
                Check(Throws<InvalidOperationException>(() =>
                {
                    using var reader = new McapStreamingReader(stream, leaveOpen: true, limits);
                    reader.Read();
                }), "138P-12B: streaming reader enforces attachment count cap");
            }

            using (var stream = CreateStreamingMcap(attachmentCount: 1, attachmentBytes: 8, metadataCount: 0, metadataValueBytes: 0))
            {
                var limits = McapSequentialReadLimits.UnlimitedForTests;
                SetField(limits, "MaxAttachmentBytes", 4L);
                Check(Throws<InvalidOperationException>(() =>
                {
                    using var reader = new McapStreamingReader(stream, leaveOpen: true, limits);
                    reader.Read();
                }), "138P-12C: streaming reader enforces attachment payload-byte cap");
            }

            using (var stream = CreateStreamingMcap(attachmentCount: 0, attachmentBytes: 0, metadataCount: 1, metadataValueBytes: 16))
            {
                var limits = McapSequentialReadLimits.UnlimitedForTests;
                SetField(limits, "MaxMetadataBytes", 4L);
                Check(Throws<InvalidOperationException>(() =>
                {
                    using var reader = new McapStreamingReader(stream, leaveOpen: true, limits);
                    reader.Read();
                }), "138P-12D: streaming reader enforces metadata payload-byte cap");
            }

            using (var stream = CreateStreamingMcap(attachmentCount: 0, attachmentBytes: 0, metadataCount: 2, metadataValueBytes: 4))
            {
                var limits = McapSequentialReadLimits.UnlimitedForTests;
                SetField(limits, "MaxMetadataRecords", 1);
                Check(Throws<InvalidOperationException>(() =>
                {
                    using var reader = new McapStreamingReader(stream, leaveOpen: true, limits);
                    reader.Read();
                }), "138P-12E: streaming reader enforces metadata count cap");
            }

            using (var stream = CreateStreamingMcap(attachmentCount: 1, attachmentBytes: 8, metadataCount: 0, metadataValueBytes: 0, insideChunk: true))
            {
                var limits = McapSequentialReadLimits.UnlimitedForTests;
                SetField(limits, "MaxAttachmentBytes", 4L);
                Check(Throws<InvalidOperationException>(() =>
                {
                    using var reader = new McapStreamingReader(stream, leaveOpen: true, limits);
                    reader.Read();
                }), "138P-12F: streaming reader enforces chunk attachment byte cap");
            }

            using (var stream = CreateStreamingMcap(attachmentCount: 0, attachmentBytes: 0, metadataCount: 1, metadataValueBytes: 16, insideChunk: true))
            {
                var limits = McapSequentialReadLimits.UnlimitedForTests;
                SetField(limits, "MaxMetadataBytes", 4L);
                Check(Throws<InvalidOperationException>(() =>
                {
                    using var reader = new McapStreamingReader(stream, leaveOpen: true, limits);
                    reader.Read();
                }), "138P-12G: streaming reader enforces chunk metadata byte cap");
            }
        }

        private static void PacketizersRejectOversizedPendingAccessUnits()
        {
            VerifyPacketizerLimit(
                "Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer, FoxgloveSdk.Tests",
                Concat(Nal(9, 0xf0), Nal(1, Repeat(0x55, 64))),
                "138P-13A: H.264 packetizer bounds pending access-unit bytes");
            VerifyPacketizerLimit(
                "Foxglove.Schemas.Video.H265AnnexBAccessUnitPacketizer, FoxgloveSdk.Tests",
                Concat(H265Nal(35, 0x50), H265Nal(1, Repeat(0x66, 64))),
                "138P-13B: H.265 packetizer bounds pending access-unit bytes");
        }

        private static void VideoSidecarStderrReadersAreBounded()
        {
            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs"
            })
            {
                var source = Read(path);
                Check(source.Contains("ReadBoundedDiagnosticStream", StringComparison.Ordinal)
                      && !source.Contains("ReadLineAsync()", StringComparison.Ordinal),
                    "138P-14: sidecar stderr reader is chunked and bounded: " + Path.GetFileName(path));
            }

            Check(typeof(FfmpegH264EncoderOptions).GetField("MaxStderrLineBytes") != null
                  && Type.GetType("Foxglove.Schemas.Video.FfmpegH265EncoderOptions, FoxgloveSdk.Tests")?.GetField("MaxStderrLineBytes") != null
                  && Type.GetType("Foxglove.Schemas.Video.OpenH264EncoderOptions, FoxgloveSdk.Tests")?.GetField("MaxStderrLineBytes") != null,
                "138P-14B: video sidecar options expose stderr byte caps");
        }

        private static void WorkerTimeoutPathsUseGenerations()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var cameraPublishPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPublishPipeline.cs");
            var cameraPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPipeline.cs");
            var pointcloud = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var pointcloudPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudEncodePipeline.cs");
            var pipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundEncodePipeline.cs");

            Check(camera.Contains("CameraJpegPublishPipeline _jpegPublishPipeline", StringComparison.Ordinal)
                  && cameraPublishPipeline.Contains("CameraJpegPipeline _jpegPipeline", StringComparison.Ordinal)
                  && cameraPipeline.Contains("_workerGeneration", StringComparison.Ordinal)
                  && cameraPipeline.Contains("WorkerGeneration", StringComparison.Ordinal)
                  && cameraPipeline.Contains("Interlocked.Increment(ref _workerGeneration)", StringComparison.Ordinal)
                  && cameraPipeline.Contains("request.Generation != _currentCaptureGeneration()", StringComparison.Ordinal)
                  && cameraPipeline.Contains("request.JpegWorkerGeneration != workerGeneration", StringComparison.Ordinal),
                "138P-15A: camera JPEG worker timeout/restart is generation-guarded");
            Check(pipeline.Contains("_worker.ShouldStopLocked(workerGeneration)", StringComparison.Ordinal)
                  && pipeline.Contains("request.Generation", StringComparison.Ordinal)
                  && pipeline.Contains("_worker.InvalidateTimedOutWorkerLocked()", StringComparison.Ordinal)
                  && pointcloudPipeline.Contains("BackgroundEncodePipeline<TRequest, TResult> _pipeline", StringComparison.Ordinal)
                  && pointcloud.Contains("PointCloudEncodePipeline<DracoEncodeRequest, DracoEncodeResult> _dracoEncodePipeline", StringComparison.Ordinal)
                  && pointcloud.Contains("PointCloudEncodePipeline<PackedPointCloudRequest, PackedPointCloudResult> _packedPointCloudPipeline", StringComparison.Ordinal),
                "138P-15B: pointcloud workers timeout/restart are generation-guarded");

            var lifecycle = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundWorkerLifecycle.cs");
            Check(lifecycle.Contains("internal sealed class BackgroundWorkerLifecycle", StringComparison.Ordinal)
                  && lifecycle.Contains("StartOrReuseLocked", StringComparison.Ordinal)
                  && lifecycle.Contains("InvalidateTimedOutWorkerLocked", StringComparison.Ordinal)
                  && pipeline.Contains("private readonly BackgroundWorkerLifecycle _worker", StringComparison.Ordinal)
                  && pipeline.Contains("StartOrReuseLocked(out startWorker)", StringComparison.Ordinal)
                  && pipeline.Contains("InvalidateTimedOutWorkerLocked()", StringComparison.Ordinal)
                  && !pointcloud.Contains("_dracoEncodeWorkerGeneration", StringComparison.Ordinal)
                  && !pointcloud.Contains("_packedPointCloudWorkerGeneration", StringComparison.Ordinal)
                  && !pointcloud.Contains("_dracoEncodeWorkerRunning", StringComparison.Ordinal)
                  && !pointcloud.Contains("_packedPointCloudWorkerRunning", StringComparison.Ordinal),
                "138P-15E: pointcloud worker lifecycle state is centralized in a small helper");
        }

        private static void CameraReadbackAndWorkerSignalsHaveExplicitLifecycleBoundaries()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var cameraPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPipeline.cs");
            var readback = ExtractMethod(camera, "private void OnReadbackComplete");
            var completeCount = CountOccurrences(readback, "CompletePendingReadback()");
            Check(readback.Contains("finally", StringComparison.Ordinal)
                  && completeCount == 1
                  && readback.IndexOf("CompletePendingReadback()", StringComparison.Ordinal)
                     > readback.IndexOf("finally", StringComparison.Ordinal)
                  && readback.IndexOf("CompletePendingReadback()", StringComparison.Ordinal)
                     > readback.IndexOf("SubmitVideoFrame", StringComparison.Ordinal)
                  && readback.IndexOf("CompletePendingReadback()", StringComparison.Ordinal)
                     > readback.IndexOf("QueueJpegFrame", StringComparison.Ordinal)
                  && readback.IndexOf("CompletePendingReadback()", StringComparison.Ordinal)
                     > readback.IndexOf("PublishJpegFrame", StringComparison.Ordinal),
                "138P-15C: camera readback drains pending count after request data is consumed");

            var ensureWorker = ExtractMethod(cameraPipeline, "public bool Start");
            var stopWorker = ExtractMethod(camera, "private void StopJpegWorker");
            var loop = ExtractMethod(cameraPipeline, "private void EncodeJpegWorkerLoop");
            Check(ensureWorker.Contains("new AutoResetEvent(false)", StringComparison.Ordinal)
                  && ensureWorker.Contains("EncodeJpegWorkerLoop(workerGeneration, workerSignal)", StringComparison.Ordinal)
                  && loop.Contains("AutoResetEvent workerSignal", StringComparison.Ordinal)
                  && loop.Contains("workerSignal.WaitOne", StringComparison.Ordinal)
                  && loop.Contains("finally", StringComparison.Ordinal)
                  && loop.Contains("workerSignal.Dispose()", StringComparison.Ordinal)
                  && !loop.Contains("_workerSignal?.WaitOne", StringComparison.Ordinal)
                  && !stopWorker.Contains("_workerSignal.Dispose()", StringComparison.Ordinal),
                "138P-15D: camera JPEG worker signal is owned by one worker generation");
        }

        private static void PackedPointCloudTfRotationConventionIsExplicit()
        {
            var publisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var bridge = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs");

            Check(publisher.Contains("TF anchor rotation in ROS roll/pitch/yaw degrees", StringComparison.Ordinal)
                  && publisher.Contains("PackedPointCloudTfRotationRos", StringComparison.Ordinal),
                "138P-16A: PointCloud2 native TF anchor rotation has an explicit ROS convention");
            Check(bridge.Contains("PackedPointCloudTfRotationRos", StringComparison.Ordinal),
                "138P-16B: R2FU PointCloud2 bridge publishes the explicit ROS TF anchor rotation");
        }

        private static void VerifyPacketizerLimit(string typeName, byte[] oversizedStream, string label)
        {
            var type = Type.GetType(typeName);
            Check(type != null, label + " type exists");
            var ctor = type.GetConstructor(new[] { typeof(int) });
            Check(ctor != null, label + " constructor exists");
            var packetizer = ctor.Invoke(new object[] { 16 });
            var append = type.GetMethod("Append", new[] { typeof(byte[]) });
            Check(append != null, label + " byte-array Append overload exists");
            append.Invoke(packetizer, new object[] { oversizedStream });

            var dropped = Convert.ToInt64(type.GetProperty("DroppedAccessUnits")?.GetValue(packetizer) ?? 0L);
            var lastError = Convert.ToString(type.GetProperty("LastError")?.GetValue(packetizer) ?? "");
            Check(dropped > 0 && lastError.Contains("exceeds", StringComparison.OrdinalIgnoreCase), label);
        }

        private static SourceHygieneScanResult ScanTrackedSourceHygiene(string[] pollutionRoots, string[] forbiddenTokens)
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".py", ".md"
            };
            var scanRoots = new[] { "Packages", "Scripts", "Unity2Foxglove/Assets/Samples" };
            var result = new SourceHygieneScanResult();
            foreach (var root in scanRoots)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (!extensions.Contains(Path.GetExtension(path)))
                        continue;

                    var normalizedPath = path.Replace('\\', '/');
                    var shouldCheckPollution = IsUnderAnyRoot(normalizedPath, pollutionRoots);
                    var file = ReadSourceHygieneFile(path, shouldCheckPollution);
                    if (file.HasUtf8Bom)
                        result.Utf8BomFiles.Add(normalizedPath);
                    if (shouldCheckPollution
                        && forbiddenTokens.Any(token => file.Text.Contains(token, StringComparison.Ordinal)))
                    {
                        result.Polluted.Add(normalizedPath);
                    }
                }
            }

            return result;
        }

        private static SourceHygieneFile ReadSourceHygieneFile(string path, bool readText)
        {
            Span<byte> header = stackalloc byte[3];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var hasUtf8Bom = stream.Read(header) == header.Length
                             && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF;
            if (!readText)
                return new SourceHygieneFile(hasUtf8Bom, string.Empty);

            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096);
            return new SourceHygieneFile(hasUtf8Bom, reader.ReadToEnd());
        }

        private static bool IsUnderAnyRoot(string normalizedPath, string[] roots)
        {
            foreach (var root in roots)
            {
                var normalizedRoot = root.Replace('\\', '/').TrimEnd('/');
                if (normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class SourceHygieneScanResult
        {
            public readonly List<string> Polluted = new List<string>();
            public readonly List<string> Utf8BomFiles = new List<string>();
        }

        private readonly struct SourceHygieneFile
        {
            public SourceHygieneFile(bool hasUtf8Bom, string text)
            {
                HasUtf8Bom = hasUtf8Bom;
                Text = text;
            }

            public bool HasUtf8Bom { get; }
            public string Text { get; }
        }

        private static MemoryStream CreateMcapWithPatchedSummaryStart(ulong summaryStart)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase138p-summary-start");
                writer.WriteDataEnd();
                var validSummaryStart = (ulong)writer.Position;
                writer.WriteSchema(1, "phase138p.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteFooter(validSummaryStart, 0, 0);
                writer.WriteMagic();
            }

            var bytes = stream.ToArray();
            var footerOffset = bytes.Length
                               - McapWriter.MagicLength
                               - McapWriter.RecordHeaderLength
                               - McapWriter.FooterContentLength;
            WriteU64LE(bytes, footerOffset + 1 + sizeof(ulong), summaryStart);
            return new MemoryStream(bytes);
        }

        private static MemoryStream CreateStreamingMcap(
            int attachmentCount,
            int attachmentBytes,
            int metadataCount,
            int metadataValueBytes,
            bool insideChunk = false)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase138p-streaming-limits");
                if (insideChunk)
                {
                    using var inner = new MemoryStream();
                    using (var innerWriter = new McapWriter(inner, leaveOpen: true))
                        WriteStreamingLimitRecords(innerWriter, attachmentCount, attachmentBytes, metadataCount, metadataValueBytes);
                    var records = inner.ToArray();
                    writer.WriteChunk(0, 0, (ulong)records.Length, 0, "", (ulong)records.Length, records);
                }
                else
                {
                    WriteStreamingLimitRecords(writer, attachmentCount, attachmentBytes, metadataCount, metadataValueBytes);
                }

                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static void WriteStreamingLimitRecords(
            McapWriter writer,
            int attachmentCount,
            int attachmentBytes,
            int metadataCount,
            int metadataValueBytes)
        {
            for (var i = 0; i < attachmentCount; i++)
                writer.WriteAttachment((ulong)i, (ulong)i, "attachment" + i + ".bin", "application/octet-stream", new byte[attachmentBytes]);
            for (var i = 0; i < metadataCount; i++)
                writer.WriteMetadata("metadata" + i, new Dictionary<string, string> { ["value"] = new string('x', metadataValueBytes) });
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        private static byte[] Nal(byte type, params byte[] payload)
            => Concat(new byte[] { 0, 0, 0, 1, type }, payload);

        private static byte[] H265Nal(byte type, params byte[] payload)
            => Concat(new byte[] { 0, 0, 0, 1, (byte)(type << 1), 1 }, payload);

        private static byte[] Repeat(byte value, int count)
        {
            var bytes = new byte[count];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = value;
            return bytes;
        }

        private static byte[] Concat(params byte[][] parts)
        {
            var length = parts.Sum(part => part?.Length ?? 0);
            var result = new byte[length];
            var offset = 0;
            foreach (var part in parts)
            {
                if (part == null)
                    continue;
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }

            return result;
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static float ReadSingle(byte[] data, int offset)
            => BitConverter.ToSingle(data, offset);

        private static uint ReadUInt32(byte[] data, int offset)
            => BitConverter.ToUInt32(data, offset);

        private static uint ExpectedNanoseconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
                return 0u;

            var nanoseconds = Math.Round((double)seconds * 1_000_000_000d, MidpointRounding.AwayFromZero);
            return nanoseconds >= uint.MaxValue ? uint.MaxValue : (uint)nanoseconds;
        }

        private static void WriteU64LE(byte[] data, int offset, ulong value)
        {
            for (var i = 0; i < 8; i++)
                data[offset + i] = (byte)(value >> (8 * i));
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is T)
            {
                return true;
            }
            catch (T)
            {
                return true;
            }
        }

        private static bool ThrowsWithMessage<T>(Action action, string expectedMessagePart) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T ex)
            {
                return ex.Message.IndexOf(expectedMessagePart, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static bool NearlyZero(System.Numerics.Vector3 value)
            => value.Length() < 1e-4f;

        private static bool NearlyEqual(System.Numerics.Vector3 left, System.Numerics.Vector3 right)
            => System.Numerics.Vector3.Distance(left, right) < 1e-4f;

        private static bool NearlyIdentity(System.Numerics.Quaternion value)
            => Math.Abs(Math.Abs(System.Numerics.Quaternion.Dot(value, System.Numerics.Quaternion.Identity)) - 1f) < 1e-4f;

        private static string ExtractMethod(string source, string signatureStart)
        {
            var start = source.IndexOf(signatureStart, StringComparison.Ordinal);
            if (start < 0)
                return "";

            var brace = source.IndexOf('{', start);
            if (brace < 0)
                return "";

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            return source.Substring(start);
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static string ReadCameraPublisherSources()
        {
            const string dir = "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers";
            var output = new StringBuilder();
            foreach (var file in Directory.GetFiles(dir, "FoxgloveCameraPublisher*.cs"))
                output.AppendLine(File.ReadAllText(file));
            return output.ToString();
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
