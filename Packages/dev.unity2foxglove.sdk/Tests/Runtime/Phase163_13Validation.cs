// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-13 validation for protobuf/JSON builder review fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_13Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-13: Protobuf and JSON Builders ===");
            _passed = 0;

            ProtobufDescriptorSubsetsAreDependencyFirst();
            NegativeAbsolutePointTimeOffsetsThrow();
            TimestampConversionDoesNotOverflowAtUlongMax();
            CameraAutoIntrinsicsSupportsProtobuf();
            PointCloudBuildResultPayloadsAreIndependentCopies();
            SourceShapeGuards();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-13: {_passed} checks passed.");
        }

        private static void ProtobufDescriptorSubsetsAreDependencyFirst()
        {
            var valid = ProtobufDescriptorOrderingFixture.TryValidate(
                out var checkedSubsets,
                out var checkedDependencies,
                out var orderingFailures);

            Check(valid,
                $"163-13A-1: protobuf descriptor subsets order dependencies before dependents "
                + $"({checkedSubsets} subsets, {checkedDependencies} dependency edges, {orderingFailures} ordering failures)");
            Check(checkedSubsets >= 40,
                "163-13A-2: protobuf descriptor ordering validation inspected bundled schemas");
        }

        private static void NegativeAbsolutePointTimeOffsetsThrow()
        {
            Check(Throws<ArgumentOutOfRangeException>(() => PointCloudPackedDataBuilder.TimeOffsetSecondsToNanoseconds(-0.01f)),
                "163-13B-1: negative absolute point time offsets fail loudly");
            Check(PointCloudPackedDataBuilder.TimeOffsetSecondsToNanoseconds(0.001f) == 1_000_000u,
                "163-13B-2: positive point time offsets still convert to nanoseconds");
        }

        private static void TimestampConversionDoesNotOverflowAtUlongMax()
        {
            var timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(ulong.MaxValue);
            Check(timestamp.Seconds >= 0 && timestamp.Nanos >= 0,
                "163-13C-1: ulong.MaxValue nanoseconds converts without negative protobuf timestamp fields");
        }

        private static void CameraAutoIntrinsicsSupportsProtobuf()
        {
            var json = CameraCalibrationMessageBuilder.CreateAutoIntrinsics(
                42,
                "camera",
                640,
                480,
                90);
            var protobuf = CameraCalibrationMessageBuilder.CreateAutoIntrinsicsProtobuf(
                42,
                "camera",
                640,
                480,
                90);

            Check(protobuf.K.Count == 9 && protobuf.R.Count == 9 && protobuf.P.Count == 12,
                "163-13D-1: auto-intrinsics protobuf builder emits fixed calibration matrices");
            Check(Math.Abs(json.K[0] - protobuf.K[0]) < 1e-9
                  && Math.Abs(json.K[2] - protobuf.K[2]) < 1e-9
                  && Math.Abs(json.P[5] - protobuf.P[5]) < 1e-9,
                "163-13D-2: JSON and protobuf auto-intrinsics use the same pinhole model");
        }

        private static void PointCloudBuildResultPayloadsAreIndependentCopies()
        {
            var frame = new PointCloudFrame { UnixNs = 100, FrameId = "map" };
            frame.Points.Add(new PointCloudPoint(1, 2, 3)
            {
                Intensity = 4,
                TimeOffsetSeconds = 0.001f
            });

            var result = PointCloudMessageBuilder.Build(frame);
            var jsonData = result.Json.Data;
            var protoData = result.Protobuf.Data.ToByteArray();

            result.Data[0] = (byte)(result.Data[0] ^ 0xFF);

            Check(string.Equals(result.Json.Data, jsonData, StringComparison.Ordinal),
                "163-13E-1: mutating returned point bytes does not alter JSON payload string");
            Check(result.Protobuf.Data.ToByteArray().SequenceEqual(protoData),
                "163-13E-2: mutating returned point bytes does not alter protobuf ByteString");
        }

        private static void SourceShapeGuards()
        {
            var pointCloud = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/PointCloudMessageBuilder.cs");
            var cameraCalibration = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/CameraCalibrationMessageBuilder.cs");
            var foxRunJson = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunJsonSchemaBuilder.cs");

            Check(pointCloud.Contains("already copied these bytes", StringComparison.Ordinal),
                "163-13F-1: point-cloud build result documents payload copy semantics");
            Check(cameraCalibration.Contains("CreateAutoIntrinsicsProtobuf", StringComparison.Ordinal)
                  && cameraCalibration.Contains("CreateAutoIntrinsicsArrays", StringComparison.Ordinal),
                "163-13F-2: camera calibration exposes shared auto-intrinsics for protobuf");
            Check(foxRunJson.Contains("JSON has no NaN/Infinity literal", StringComparison.Ordinal),
                "163-13F-3: FoxRun nullable-number schema documents non-finite sentinel rationale");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_13Validation.cs", StringComparison.Ordinal),
                "163-13G-1: runtime test project compiles Phase163_13Validation");
            Check(registry.Contains("--phase163-13", StringComparison.Ordinal)
                  && registry.Contains("Phase163_13Validation.Validate", StringComparison.Ordinal),
                "163-13G-2: validation registry exposes --phase163-13");
        }

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
