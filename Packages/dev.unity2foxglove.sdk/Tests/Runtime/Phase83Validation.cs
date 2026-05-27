// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 83 validation for raw PointCloud QoS budget, LOD,
// and demand-gated preparation.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates first-layer raw PointCloud QoS controls without adding
    /// Draco, compressed point clouds, or new transports.
    /// </summary>
    public static class Phase83Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 83: Point Cloud QoS Layer 1 ===");
            _passed = 0;

            VerifyPackedStride();
            VerifyEffectiveBudget();
            VerifyUniformSampling();
            VerifyPublisherSourceIntegration();

            Console.WriteLine($"Phase 83: {_passed} checks passed.");
        }

        private static void VerifyPackedStride()
        {
            var xyz = new PointCloudFrame();
            xyz.Points.Add(new PointCloudPoint(1, 2, 3));
            Check(PointCloudQoS.ComputePackedStride(xyz) == 12,
                "83A-1: XYZ-only packed stride is 12 bytes");

            var optional = new PointCloudFrame();
            optional.Points.Add(new PointCloudPoint(1, 2, 3)
            {
                Intensity = 0.5f,
                Reflectivity = 0.25f,
                Ring = 7,
                TimeOffsetSeconds = 0.001f
            });
            Check(PointCloudQoS.ComputePackedStride(optional) == 26,
                "83A-2: optional packed fields add intensity +4, reflectivity +4, ring +2, time_offset +4");

            var mixed = new PointCloudFrame();
            mixed.Points.Add(new PointCloudPoint(1, 2, 3));
            mixed.Points.Add(new PointCloudPoint(4, 5, 6) { Ring = 2 });
            Check(PointCloudQoS.ComputePackedStride(mixed) == 14,
                "83A-3: stride is frame-wide when any point has an optional field");
        }

        private static void VerifyEffectiveBudget()
        {
            Check(PointCloudQoS.ComputeEffectivePointBudget(1000, 4096, 0, 12) == 1000,
                "83B-1: disabled byte budget preserves source count below max points");
            Check(PointCloudQoS.ComputeEffectivePointBudget(5000, 4096, 0, 12) == 4096,
                "83B-2: max points clamps oversized frames");
            Check(PointCloudQoS.ComputeEffectivePointBudget(5000, 4096, 1200, 12) == 100,
                "83B-3: max packed bytes can be stricter than max points");
            Check(PointCloudQoS.ComputeEffectivePointBudget(5000, 50, 1200, 12) == 50,
                "83B-4: max points can be stricter than max packed bytes");
            Check(PointCloudQoS.ComputeEffectivePointBudget(10, 4096, 11, 12) == 0,
                "83B-5: byte budget below one point skips publish");
        }

        private static void VerifyUniformSampling()
        {
            Check(PointCloudQoS.BuildUniformSampleIndices(10, 4).SequenceEqual(new[] { 0, 3, 6, 9 }),
                "83C-1: uniform sampling preserves first and last point");
            Check(PointCloudQoS.BuildUniformSampleIndices(5, 5).SequenceEqual(new[] { 0, 1, 2, 3, 4 }),
                "83C-2: uniform sampling returns all points when budget covers the frame");
            Check(PointCloudQoS.BuildUniformSampleIndices(5, 1).SequenceEqual(new[] { 0 }),
                "83C-3: single-point budget keeps the first point deterministically");
            Check(PointCloudQoS.BuildUniformSampleIndices(0, 4).Length == 0,
                "83C-4: empty source produces no sample indices");
        }

        private static void VerifyPublisherSourceIntegration()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");

            Check(source.Contains("_maxPackedBytes"),
                "83D-1: point cloud publisher exposes max packed byte budget");
            Check(source.Contains("_samplingMode"),
                "83D-2: point cloud publisher exposes sampling mode");
            Check(source.Contains("_logQosDrops"),
                "83D-3: point cloud publisher exposes optional QoS logging");
            Check(source.Contains("PrepareFrameForQoS"),
                "83D-4: point cloud publisher uses QoS preparation path");
            Check(source.Contains("PointCloudSamplingMode"),
                "83D-5: point cloud publisher uses explicit sampling mode");

            var update = Slice(source, "protected virtual void Update()", "protected virtual void PublishPreparedFrame");
            CheckOrdered(update, "ShouldPreparePublishPayload()", "PrepareFrameForQoS", "83D-6: Update preflights demand before QoS copy");
            CheckOrdered(update, "ShouldPreparePublishPayload()", "CreateFrameFromTransforms", "83D-7: Update preflights demand before transform scan");
            CheckOrdered(update, "ShouldPreparePublishPayload()", "_pendingFrame = null", "83D-8: Update preflights demand before pending-frame consumption");

            var publishFrame = Slice(source, "public void PublishFrame", "protected virtual void Update()");
            CheckOrdered(publishFrame, "ShouldPreparePublishPayload()", "PrepareFrameForQoS", "83D-9: PublishFrame preflights demand before QoS copy");
            CheckOrdered(publishFrame, "ShouldPreparePublishPayload()", "PublishPreparedFrame", "83D-10: PublishFrame preflights demand before publish");
            Check(publishFrame.Contains("frame == null") && publishFrame.Contains("_manager == null"),
                "83D-11: PublishFrame keeps null and manager guards");

            var setFrame = Slice(source, "public void SetFrame", "public void PublishFrame");
            Check(setFrame.Contains("_pendingFrame != null") && setFrame.Contains("_pendingFrame = frame"),
                "83D-12: SetFrame documents and preserves last-value-wins pending frame replacement");
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
