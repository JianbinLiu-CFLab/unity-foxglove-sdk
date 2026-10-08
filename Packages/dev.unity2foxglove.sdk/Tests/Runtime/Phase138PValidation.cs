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
    public static partial class Phase138PValidation
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
                "Packages",
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
            Check(scheduler.Contains("LidarScanTiming.AcquisitionOffsetSeconds(", StringComparison.Ordinal)
                  && scheduler.Contains("timeOffset", StringComparison.Ordinal)
                  && scheduler.Contains("fixedDeltaTimeSeconds", StringComparison.Ordinal),
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
                Check(ThrowsWithMessage<InvalidDataException>(() =>
                {
                    using var reader = new McapStreamingReader(stream, leaveOpen: true, limits);
                    reader.Read();
                }, "must not appear inside a Chunk"),
                    "138P-12F: streaming reader rejects forbidden Attachment records inside chunks");
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

        private static string ReadCameraPublisherSources()
        {
            const string dir = "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers";
            var output = new StringBuilder();
            foreach (var file in Directory.GetFiles(dir, "FoxgloveCameraPublisher*.cs"))
                output.AppendLine(File.ReadAllText(file));
            return output.ToString();
        }
}
}
