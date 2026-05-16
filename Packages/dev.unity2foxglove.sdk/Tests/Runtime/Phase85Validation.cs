// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 85 validation for point-cloud Inspector UX and smoke evidence.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates Phase 85 source-level UX and smoke-evidence artifacts.
    /// </summary>
    public static class Phase85Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 85: Point Cloud Inspector and Smoke Evidence ===");
            _passed = 0;

            VerifyPointCloudInspector();
            VerifySmokeProbe();
            VerifyPointCloudSmokeSource();

            Console.WriteLine($"Phase 85: {_passed} checks passed.");
        }

        private static void VerifyPointCloudInspector()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");
            Check(!string.IsNullOrEmpty(source),
                "85A-1: dedicated point-cloud publisher editor exists");
            Check(source.Contains("[CustomEditor(typeof(FoxglovePointCloudPublisher))]"),
                "85A-2: editor targets FoxglovePointCloudPublisher");
            Check(source.Contains("General") && source.Contains("Point Sources") && source.Contains("Point Cloud QoS"),
                "85A-3: editor groups point-cloud workflow sections");
            Check(source.Contains("Publish Rate") && source.Contains("Encoding Policy"),
                "85A-4: editor preserves shared publisher policy sections");
            Check(source.Contains("_voxelSizeMeters") && source.Contains("PointCloudSamplingMode.VoxelGrid"),
                "85A-5: editor conditionally handles voxel size for VoxelGrid");
            CheckOrdered(source, "PointCloudSamplingMode.VoxelGrid", "Voxel Size Meters",
                "85A-6: voxel-size label is tied to VoxelGrid branch");
            Check(source.Contains("Effective Publish Rate Hz") && source.Contains("Supported Encodings") && source.Contains("Effective Encoding"),
                "85A-7: editor preserves resolved rate and encoding summaries");
            Check(source.Contains("PointCloud.data") && source.Contains("first source point") && source.Contains("no live subscriber"),
                "85A-8: editor explains byte budget, voxel representative policy, and demand gating");
        }

        private static void VerifySmokeProbe()
        {
            var source = ReadRepoText("Scripts/smoke/pointcloud_qos_probe.py");
            Check(!string.IsNullOrEmpty(source),
                "85B-1: point-cloud QoS smoke probe exists");
            Check(source.Contains("FOXGLOVE_SUBPROTOCOL") && source.Contains("foxglove.sdk.v1"),
                "85B-2: probe uses Foxglove WebSocket subprotocol");
            Check(source.Contains("DEFAULT_TOPIC = \"/unity/point_cloud\""),
                "85B-3: probe defaults to /unity/point_cloud");
            Check(source.Contains("\"op\": \"subscribe\"") && source.Contains("MESSAGE_DATA_OPCODE"),
                "85B-4: probe subscribes and decodes MessageData frames");
            Check(source.Contains("POINT_STRIDE_TAG = 37") && source.Contains("POINT_DATA_TAG = 50"),
                "85B-5: probe knows point_stride and data protobuf tags");
            Check(source.Contains("read_varint") && source.Contains("skip_field"),
                "85B-6: probe parses protobuf wire format instead of raw byte searching");
            Check(source.Contains("point_stride") && source.Contains("pointStride") && source.Contains("base64"),
                "85B-7: probe supports JSON point-cloud payloads");
            Check(source.Contains("estimated_point_count") && source.Contains("avg_point_count"),
                "85B-8: probe reports estimated point counts");
        }

        private static void VerifyPointCloudSmokeSource()
        {
            var source = ReadRepoText("Unity2Foxglove/Assets/Scripts/PointCloudSmokeSource.cs");
            Check(!string.IsNullOrEmpty(source),
                "85C-1: point-cloud smoke source component exists");
            Check(source.Contains("class PointCloudSmokeSource") && source.Contains("FoxglovePointCloudPublisher"),
                "85C-2: smoke source feeds FoxglovePointCloudPublisher");
            Check(source.Contains("[RequireComponent(typeof(FoxglovePointCloudPublisher))]"),
                "85C-3: smoke source is colocated with point-cloud publisher");
            Check(source.Contains("_pointCount = 1000") && source.Contains("SetFrame("),
                "85C-4: smoke source defaults to 1000 points and pushes frames");
            Check(source.Contains("PointCloudFrame") && source.Contains("PointCloudPoint") && source.Contains("_frameId = \"unity_world\""),
                "85C-5: smoke source builds unity_world point-cloud frames");
            Check(source.Contains("_animate") && source.Contains("_includeIntensity"),
                "85C-6: smoke source supports visual motion and optional intensity");
        }

        private static void CheckOrdered(string text, string before, string after, string name)
        {
            Check(IndexOf(text, before) >= 0 && IndexOf(text, after) >= 0 && IndexOf(text, before) < IndexOf(text, after), name);
        }

        private static int IndexOf(string text, string pattern)
            => text.IndexOf(pattern, StringComparison.Ordinal);

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
    }
}
