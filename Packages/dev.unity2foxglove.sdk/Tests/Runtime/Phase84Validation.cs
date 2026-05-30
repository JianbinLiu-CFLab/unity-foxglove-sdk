// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 84 validation for raw PointCloud voxel-grid LOD.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates dependency-free voxel-grid LOD for raw foxglove.PointCloud.
    /// </summary>
    public static class Phase84Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 84: Point Cloud Voxel LOD ===");
            _passed = 0;

            VerifyVoxelModeValue();
            VerifyVoxelSampling();
            VerifyVoxelFallback();
            VerifyPublisherSourceIntegration();

            Console.WriteLine($"Phase 84: {_passed} checks passed.");
        }

        private static void VerifyVoxelModeValue()
        {
            Check((int)PointCloudSamplingMode.VoxelGrid == 2,
                "84A-1: VoxelGrid sampling mode keeps stable enum value 2");
        }

        private static void VerifyVoxelSampling()
        {
            var frame = new PointCloudFrame();
            var first = new PointCloudPoint(0.01f, 0.01f, 0.01f)
            {
                Intensity = 0.75f,
                Reflectivity = 0.25f,
                Ring = 7,
                TimeOffsetSeconds = 0.001f
            };
            var duplicate = new PointCloudPoint(0.09f, 0.09f, 0.09f)
            {
                Intensity = 0.1f,
                Ring = 8
            };
            var separate = new PointCloudPoint(0.11f, 0.01f, 0.01f);
            frame.Points.Add(first);
            frame.Points.Add(duplicate);
            frame.Points.Add(separate);

            var indices = PointCloudQoS.BuildVoxelSampleIndices(frame, 0.1f);
            Check(indices.SequenceEqual(new[] { 0, 2 }),
                "84B-1: voxel sampling keeps first source point per voxel in source order");
            Check(frame.Points[indices[0]].Intensity == 0.75f
                  && frame.Points[indices[0]].Reflectivity == 0.25f
                  && frame.Points[indices[0]].Ring == 7
                  && frame.Points[indices[0]].TimeOffsetSeconds == 0.001f,
                "84B-2: representative point at the source index preserves optional fields");

            var negative = new PointCloudFrame();
            negative.Points.Add(new PointCloudPoint(-0.01f, 0f, 0f));
            negative.Points.Add(new PointCloudPoint(0f, 0f, 0f));
            Check(PointCloudQoS.BuildVoxelSampleIndices(negative, 0.1f).SequenceEqual(new[] { 0, 1 }),
                "84B-3: negative coordinates use floor buckets, not truncation toward zero");
        }

        private static void VerifyVoxelFallback()
        {
            var frame = new PointCloudFrame();
            for (var i = 0; i < 5; i++)
                frame.Points.Add(new PointCloudPoint(i, 0, 0));

            Check(PointCloudQoS.BuildVoxelSampleIndices(frame, 0f).SequenceEqual(new[] { 0, 1, 2, 3, 4 }),
                "84C-1: non-positive voxel size is a safe no-op at helper level");
        }

        private static void VerifyPublisherSourceIntegration()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");

            Check(source.Contains("_voxelSizeMeters"),
                "84D-1: point cloud publisher exposes voxel size");
            Check(source.Contains("PointCloudSamplingMode.VoxelGrid"),
                "84D-2: point cloud publisher routes VoxelGrid mode");
            Check(source.Contains("BuildVoxelSampleIndices"),
                "84D-3: point cloud publisher delegates voxel sampling to PointCloudQoS");

            var prepare = Slice(source, "protected virtual PointCloudFrame PrepareFrameForQoS", "private void WarnPointCloudReduced");
            CheckOrdered(prepare, "PointCloudSamplingMode.VoxelGrid", "BuildVoxelSampleIndices",
                "84D-4: PrepareFrameForQoS checks VoxelGrid before voxel sampling");
            CheckOrdered(prepare, "BuildVoxelSampleIndices", "BuildUniformSampleIndices",
                "84D-5: voxel output can still fall back to uniform budget reduction");

            var update = Slice(source, "protected virtual void Update()", "protected virtual void PublishPreparedFrame");
            CheckOrdered(update, "ShouldPreparePublishPayload()", "PrepareFrameForQoS",
                "84D-6: Update preflights demand before voxel/QoS preparation");

            var publishFrame = Slice(source, "public void PublishFrame", "protected virtual void Update()");
            CheckOrdered(publishFrame, "ShouldPreparePublishPayload()", "PrepareFrameForQoS",
                "84D-7: PublishFrame preflights demand before voxel/QoS preparation");

            Check(source.Contains("PublishRawFrame")
                  && source.Contains("PointCloudMessageBuilder.SerializeProtobuf(frame)")
                  && source.Contains("PointCloudMessageBuilder.CreateJson(frame)")
                  && source.Contains("if (_outputMode == PointCloudOutputMode.Draco)")
                  && source.Contains("PublishDracoFrame(frame, unixNs)"),
                "84D-8: Phase 84 raw PointCloud path remains explicit while later Draco output is mode-gated");
        }

        private static void CheckOrdered(string text, string before, string after, string name)
        {
            Check(IndexOf(text, before) >= 0 && IndexOf(text, after) >= 0 && IndexOf(text, before) < IndexOf(text, after), name);
        }

        private static int IndexOf(string text, string pattern)
            => text.IndexOf(pattern, StringComparison.Ordinal);

        private static string Slice(string text, string start, string end)
        {
            var startIndex = text.IndexOf(start, StringComparison.Ordinal);
            if (startIndex < 0)
                return string.Empty;

            var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
            return endIndex < 0
                ? string.Empty
                : text.Substring(startIndex, endIndex - startIndex);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation fixture is missing: " + relativePath, path);

            return File.ReadAllText(path);
        }
    }
}
