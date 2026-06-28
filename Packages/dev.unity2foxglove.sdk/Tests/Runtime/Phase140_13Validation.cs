// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-13 protobuf builder and typed publisher review fixes.

using System;
using System.IO;
using Foxglove.Schemas;
using Foxglove.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for protobuf builders, typed publishers, and
    /// video/point-cloud sidecar hardening found in Phase 140-13.
    /// </summary>
    public static class Phase140_13Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 140-13 protobuf publisher review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-13: protobuf builders and typed publisher review fixes ===");
            _passed = 0;

            DracoNullFrameReturnsFailure();
            DescriptorSubsetsKeepDeterministicOrder();
            LegacyVideoRenderTextureIsDestroyed();
            MediaFoundationTimestampMapEvictsOneEntry();
            FfmpegTimestampPairingDocumentsAccessUnitAssumption();
            LegacyVideoPublisherIsObsolete();
            DeadJpegQueueMethodIsRemoved();
            LaserScanWrappedAnglesAreDocumented();
            PointCloudPackedDataAvoidsMemoryStreamCopy();
            LaserScanProtobufAvoidsListMaterialization();
            CameraCalibrationProtobufAvoidsListMaterialization();
            ProtobufDecoderFactoryCachesParsers();
            ImuBuilderComputesNestedMessageLengths();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-13: {_passed} checks passed.");
        }

        private static void DracoNullFrameReturnsFailure()
        {
            Check(!DracoPointCloudNativeEncoder.TryEncode(null, out var payload, out var error)
                  && payload == null
                  && !string.IsNullOrWhiteSpace(error),
                "140-13A-1: Draco native encoder reports null frames without throwing");
        }

        private static void DescriptorSubsetsKeepDeterministicOrder()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Registry/ProtobufSchemaRegistry.cs");

            Check(source.Contains("ordered.Add(fileName);", StringComparison.Ordinal)
                  && !source.Contains("neededFiles.OrderBy", StringComparison.Ordinal),
                "140-13B-1: protobuf descriptor subsets keep deterministic dependency-first ordering");
        }

        private static void LegacyVideoRenderTextureIsDestroyed()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs");

            Check(CountOccurrences(source, "Destroy(_captureRT)") >= 2,
                "140-13C-1: legacy compressed video publisher destroys replaced and disabled RenderTextures");
        }

        private static void MediaFoundationTimestampMapEvictsOneEntry()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");

            Check(source.Contains("_sampleTimestampOrder", StringComparison.Ordinal)
                  && source.Contains("EvictOldestSampleTimestamp", StringComparison.Ordinal)
                  && source.Contains("_sampleTimestampOrder.Enqueue(sampleTime)", StringComparison.Ordinal),
                "140-13D-1: Media Foundation timestamp map evicts oldest samples instead of bulk clearing");
        }

        private static void FfmpegTimestampPairingDocumentsAccessUnitAssumption()
        {
            CheckFfmpegTimestampComment("FfmpegH264EncoderSidecar.cs", "140-13E-1: FFmpeg H.264 timestamp pairing documents rawvideo PTS limitation");
            CheckFfmpegTimestampComment("FfmpegH265EncoderSidecar.cs", "140-13E-2: FFmpeg H.265 timestamp pairing documents rawvideo PTS limitation");
        }

        private static void CheckFfmpegTimestampComment(string fileName, string description)
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/" + fileName);

            Check(source.Contains("rawvideo pipe carries no per-frame PTS", StringComparison.Ordinal)
                  && source.Contains("accepted approximation", StringComparison.Ordinal),
                description);
        }

        private static void LegacyVideoPublisherIsObsolete()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs");

            Check(source.Contains("[Obsolete(\"Use FoxgloveCameraPublisher with CameraOutputMode.H264Ffmpeg.\", false)]", StringComparison.Ordinal),
                "140-13F-1: legacy compressed video publisher emits an obsolete warning");
        }

        private static void DeadJpegQueueMethodIsRemoved()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs");

            Check(!source.Contains("EnsureJpegQueues", StringComparison.Ordinal),
                "140-13G-1: dead JPEG queue initialization helper is removed");
        }

        private static void LaserScanWrappedAnglesAreDocumented()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/LaserScanMessageBuilder.cs");

            Check(source.Contains("Reverse or wrapped angle ranges are valid", StringComparison.Ordinal),
                "140-13H-1: LaserScan builder documents reverse or wrapped angle ranges");
        }

        private static void PointCloudPackedDataAvoidsMemoryStreamCopy()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloudPackedDataBuilder.cs");
            var pack = SourceBetween(source, "private static byte[] Pack", "internal static uint TimeOffsetSecondsToNanoseconds");

            Check(pack.Contains("new byte[capacity]", StringComparison.Ordinal)
                  && pack.Contains("new MemoryStream(data, 0, data.Length, true, true)", StringComparison.Ordinal)
                  && !pack.Contains("ToArray()", StringComparison.Ordinal),
                "140-13H-2: point-cloud packed data writes into its final byte array without MemoryStream copy");
        }

        private static void LaserScanProtobufAvoidsListMaterialization()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/LaserScanMessageBuilder.cs");
            var createProtobuf = SourceBetween(source, "public static Foxglove.LaserScan CreateProtobuf", "public static byte[] SerializeProtobuf");

            Check(createProtobuf.Contains("ToRequiredReadOnlyList", StringComparison.Ordinal)
                  && createProtobuf.Contains("ToReadOnlyListOrEmpty", StringComparison.Ordinal)
                  && !createProtobuf.Contains("ToRequiredList", StringComparison.Ordinal)
                  && !createProtobuf.Contains("ToListOrEmpty", StringComparison.Ordinal),
                "140-13H-3: LaserScan protobuf path avoids extra List materialization before RepeatedField copy");
        }

        private static void CameraCalibrationProtobufAvoidsListMaterialization()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/CameraCalibrationMessageBuilder.cs");
            var createProtobuf = SourceBetween(source, "public static Foxglove.CameraCalibration CreateProtobuf", "public static byte[] SerializeProtobuf");

            Check(createProtobuf.Contains("ToReadOnlyListOrEmpty", StringComparison.Ordinal)
                  && !createProtobuf.Contains("ToListOrEmpty", StringComparison.Ordinal),
                "140-13H-4: CameraCalibration protobuf path avoids extra List materialization before RepeatedField copy");
        }

        private static void ProtobufDecoderFactoryCachesParsers()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/DataLoader/McapFoxgloveProtobufDecoderFactory.cs");

            Check(source.Contains("s_parserCache", StringComparison.Ordinal)
                  && source.Contains("GetOrAdd", StringComparison.Ordinal),
                "140-13I-1: MCAP protobuf decoder factory caches reflected parsers");
        }

        private static void ImuBuilderComputesNestedMessageLengths()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/ImuMessageBuilder.cs");

            Check(!source.Contains("WriteLength(27)", StringComparison.Ordinal)
                  && !source.Contains("WriteLength(36)", StringComparison.Ordinal)
                  && source.Contains("ComputeDoubleMessagePayloadSize", StringComparison.Ordinal),
                "140-13J-1: IMU builder computes nested message payload lengths");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase140_13Validation.cs", StringComparison.Ordinal),
                "140-13K-1: test project compiles Phase140_13Validation");
            Check(registry.Contains("--phase140-13", StringComparison.Ordinal)
                  && registry.Contains("Phase140_13Validation.Validate", StringComparison.Ordinal),
                "140-13K-2: validation registry exposes --phase140-13");
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception("[FAIL] " + description);
            _passed++;
            Console.WriteLine("[PASS] " + description);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string SourceBetween(string source, string startMarker, string endMarker)
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            if (start < 0 || end < 0)
                throw new InvalidOperationException("Could not locate Phase140-13 source markers.");

            return source.Substring(start, end - start);
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
