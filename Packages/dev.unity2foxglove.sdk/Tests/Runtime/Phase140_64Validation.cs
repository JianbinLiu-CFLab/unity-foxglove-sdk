// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-64 regression coverage for point-cloud, LaserScan, and Draco allocation optimizations.

using System;
using System.IO;
using System.Linq;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_64Validation.
    /// </summary>
    public static class Phase140_64Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-64: PointCloud, LaserScan, and Draco Optimization ===");
            _passed = 0;

            PointCloudPackedLayoutReusePreservesPayload();
            PointCloudQoSReducerReusesVoxelBuffers();
            PointCloudPublisherReusesQoSLayoutForPackedBuilders();
            NativeDracoRateLimiterCachesInterval();
            LaserScanPublisherCachesAngleRadians();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-64: {_passed} checks passed.");
        }

        private static void PointCloudPackedLayoutReusePreservesPayload()
        {
            var frame = new PointCloudFrame
            {
                UnixNs = 123UL,
                FrameId = "lidar",
                EmitAbsoluteTimeNs = true
            };
            frame.Points.Add(new PointCloudPoint(1f, 2f, 3f)
            {
                Intensity = 0.5f,
                Reflectivity = 0.25f,
                Ring = 7,
                TimeOffsetSeconds = 0.001f
            });
            frame.Points.Add(new PointCloudPoint(4f, 5f, 6f));

            var defaultPacked = PointCloudPackedDataBuilder.Build(frame);
            var layout = PointCloudPackedDataBuilder.BuildLayout(frame);
            var reusedPacked = PointCloudPackedDataBuilder.Build(frame, layout);

            Check(defaultPacked.PointStride == reusedPacked.PointStride
                  && defaultPacked.Fields.Count == reusedPacked.Fields.Count
                  && defaultPacked.Data.SequenceEqual(reusedPacked.Data),
                "140-64A-1: reused PointCloud layout produces the same packed payload as default scanning");
            Check(PointCloudMessageBuilder.SerializeProtobuf(frame)
                    .SequenceEqual(PointCloudMessageBuilder.SerializeProtobuf(frame, layout)),
                "140-64A-2: protobuf PointCloud serialization preserves payload when reusing a QoS layout");
        }

        private static void PointCloudQoSReducerReusesVoxelBuffers()
        {
            var qos = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/PointCloudQoS.cs");
            var reducer = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudQoSReducer.cs");

            Check(qos.Contains("internal static void BuildVoxelSampleIndices(", StringComparison.Ordinal)
                  && qos.Contains("indices.Clear()", StringComparison.Ordinal)
                  && qos.Contains("seen.Clear()", StringComparison.Ordinal),
                "140-64B-1: PointCloudQoS exposes an internal reusable voxel-index fill path");
            Check(reducer.Contains("private readonly List<int> _voxelSampleIndices", StringComparison.Ordinal)
                  && reducer.Contains("private readonly HashSet<PointCloudQoS.VoxelKey> _voxelKeys", StringComparison.Ordinal)
                  && reducer.Contains("PointCloudQoS.BuildVoxelSampleIndices(frame, voxelSizeMeters, _voxelSampleIndices, _voxelKeys)", StringComparison.Ordinal),
                "140-64B-2: PointCloudQoSReducer reuses voxel List/HashSet buffers on the hot path");
        }

        private static void PointCloudPublisherReusesQoSLayoutForPackedBuilders()
        {
            var reducer = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudQoSReducer.cs");
            var publisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");

            Check(reducer.Contains("var sourceLayout = PointCloudPackedDataBuilder.BuildLayout(frame)", StringComparison.Ordinal)
                  && reducer.Contains("packedLayout = sourceLayout", StringComparison.Ordinal)
                  && reducer.Contains("out PointCloudPackedDataBuilder.PointCloudLayout packedLayout", StringComparison.Ordinal),
                "140-64C-1: PointCloudQoSReducer returns the scanned packed layout with the prepared frame");
            Check(publisher.Contains("PointCloudMessageBuilder.SerializeProtobuf(frame, packedLayout)", StringComparison.Ordinal)
                  && publisher.Contains("Ros2CdrPointCloudBuilder.Serialize(frame, packedLayout)", StringComparison.Ordinal)
                  && publisher.Contains("Ros2CdrSensorPointCloud2Builder.Serialize(frame, packedLayout)", StringComparison.Ordinal)
                  && publisher.Contains("PointCloudPackedDataBuilder.Build(frame, packedLayout)", StringComparison.Ordinal),
                "140-64C-2: PointCloud publisher reuses QoS layout for downstream packed builders");
        }

        private static void NativeDracoRateLimiterCachesInterval()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var method = Slice(source, "private bool ShouldQueueVirtualLidarDracoFrame", "private ulong ResolveNativeDracoPublishIntervalNs");

            Check(source.Contains("_cachedNativeDracoMaxPublishRateHz", StringComparison.Ordinal)
                  && source.Contains("_cachedNativeDracoPublishIntervalNs", StringComparison.Ordinal)
                  && source.Contains("private ulong ResolveNativeDracoPublishIntervalNs", StringComparison.Ordinal),
                "140-64D-1: native Draco rate limiter has cached rate-to-interval state");
            Check(method.Contains("ResolveNativeDracoPublishIntervalNs(rateHz)", StringComparison.Ordinal)
                  && !method.Contains("Math.Round(1_000_000_000d / rateHz)", StringComparison.Ordinal),
                "140-64D-2: native Draco hot path uses the cached interval resolver");
        }

        private static void LaserScanPublisherCachesAngleRadians()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs");
            var update = Slice(source, "private void Update()", "private void RefreshCachedAngles()");

            Check(source.Contains("_cachedStartAngleRadians", StringComparison.Ordinal)
                  && source.Contains("_cachedEndAngleRadians", StringComparison.Ordinal)
                  && source.Contains("private void RefreshCachedAngles()", StringComparison.Ordinal),
                "140-64E-1: LaserScan publisher caches degree-to-radian conversion state");
            Check(update.Contains("RefreshCachedAngles()", StringComparison.Ordinal)
                  && update.Contains("_cachedStartAngleRadians", StringComparison.Ordinal)
                  && update.Contains("_cachedEndAngleRadians", StringComparison.Ordinal)
                  && !update.Contains("_startAngleDegrees * Math.PI / 180.0", StringComparison.Ordinal),
                "140-64E-2: LaserScan Update publishes with cached radians instead of recomputing inline");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase140_64Validation.cs", StringComparison.Ordinal),
                "140-64F-1: test project compiles Phase140_64Validation");
            Check(registry.Contains("Ci(\"--phase140-64\", \"Phase 140-64\", Phase140_64Validation.Validate", StringComparison.Ordinal),
                "140-64F-2: validation registry exposes --phase140-64");
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static string Slice(string source, string startToken, string endToken)
        {
            var start = source.IndexOf(startToken, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] Missing start token: " + startToken);

            var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;

            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
