// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 88 validation for Draco CompressedPointCloud evidence tooling.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the Phase 88 evidence-gate tooling around the Phase 87 Draco spike.
    /// </summary>
    public static class Phase88Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 88: CompressedPointCloud Draco Evidence Gate ===");
            _passed = 0;

            VerifyCompressedDracoProbe();
            VerifyMcapInspectionTooling();
            VerifyDualPublisherSmokeSource();
            VerifyEvidenceTemplate();
            VerifyNoBundledDracoArtifacts();

            Console.WriteLine($"Phase 88: {_passed} checks passed.");
        }

        private static void VerifyCompressedDracoProbe()
        {
            var source = ReadRepoText("Scripts/smoke/compressed_pointcloud_draco_probe.py");
            Check(!string.IsNullOrEmpty(source),
                "88A-1: compressed Draco smoke probe exists");
            Check(source.Contains("DEFAULT_TOPIC = \"/unity/point_cloud_draco\"")
                  && source.Contains("foxglove.sdk.v1"),
                "88A-2: probe defaults to /unity/point_cloud_draco and uses Foxglove subprotocol");
            Check(source.Contains("EXPECTED_SCHEMA_NAME = \"foxglove.CompressedPointCloud\"")
                  && source.Contains("EXPECTED_ENCODING = \"protobuf\"")
                  && source.Contains("EXPECTED_SCHEMA_ENCODING = \"protobuf\""),
                "88A-3: probe validates schemaName, encoding, and schemaEncoding");
            Check(source.Contains("COMPRESSED_POINTCLOUD_DATA_TAG = 34")
                  && source.Contains("COMPRESSED_POINTCLOUD_FORMAT_TAG = 42")
                  && source.Contains("COMPRESSED_POINTCLOUD_DATA_FIELD = 4")
                  && source.Contains("COMPRESSED_POINTCLOUD_FORMAT_FIELD = 5"),
                "88A-4: probe documents protobuf field numbers and tag bytes");
            Check(source.Contains("decode_compressed_pointcloud_payload")
                  && source.Contains("malformed_payload_count")
                  && source.Contains("observed_format_values"),
                "88A-5: probe decodes payloads and reports malformed/format evidence");
            Check(source.Contains("--self-test")
                  && source.Contains("run_self_test")
                  && source.Contains("format=draco"),
                "88A-6: probe exposes an offline decoder self-test");
            Check(source.Contains("foxglove.PointCloud")
                  && source.Contains("WRONG_SCHEMA")
                  && source.Contains("WRONG_ENCODING"),
                "88A-7: probe clearly rejects raw PointCloud or non-protobuf channels");
        }

        private static void VerifyMcapInspectionTooling()
        {
            var source = ReadRepoText("Scripts/smoke/compressed_pointcloud_mcap_inspect.py");
            Check(!string.IsNullOrEmpty(source),
                "88B-1: compressed point-cloud MCAP inspection script exists");
            Check(source.Contains("/unity/point_cloud")
                  && source.Contains("/unity/point_cloud_draco")
                  && source.Contains("foxglove.PointCloud")
                  && source.Contains("foxglove.CompressedPointCloud"),
                "88B-2: MCAP inspector checks raw and compressed channels");
            Check(source.Contains("encoding == \"protobuf\"")
                  && source.Contains("schema_encoding == \"protobuf\""),
                "88B-3: MCAP inspector validates compressed channel encoding and schemaEncoding");
            Check(source.Contains("decode_compressed_pointcloud_payload")
                  && source.Contains("COMPRESSED_POINTCLOUD_DATA_TAG = 34")
                  && source.Contains("COMPRESSED_POINTCLOUD_FORMAT_TAG = 42"),
                "88B-4: MCAP inspector decodes compressed payload data and format fields");
        }

        private static void VerifyDualPublisherSmokeSource()
        {
            var source = ReadRepoText("Unity2Foxglove/Assets/Scripts/PointCloud/Phase88PointCloudFanoutSource.cs");
            Check(!string.IsNullOrEmpty(source),
                "88C-1: Phase88 fanout smoke source exists");
            Check(source.Contains("FoxglovePointCloudPublisher _rawPublisher")
                  && source.Contains("FoxgloveCompressedPointCloudPublisher _compressedPublisher")
                  && source.Contains("_rawPublisher.SetFrame(frame)")
                  && source.Contains("_compressedPublisher.SetFrame(frame)"),
                "88C-2: fanout source feeds raw and compressed publishers from one frame");
            Check(source.Contains("_pointCount = 1000")
                  && source.Contains("XYZ-only")
                  && !source.Contains("Intensity =")
                  && !source.Contains("Ring ="),
                "88C-3: fanout source is deterministic 1000-point XYZ-only evidence input");
            Check(!source.Contains("GetComponent<FoxglovePointCloudPublisher>()"),
                "88C-4: fanout source avoids ambiguous inherited component lookup");
        }

        private static void VerifyEvidenceTemplate()
        {
            var source = ReadRepoText("Scripts/smoke/phase88_draco_evidence_template.md");
            Check(!string.IsNullOrEmpty(source),
                "88D-1: tracked Phase88 evidence template exists");
            Check(source.Contains("Developer/50 Phase88 Compressed PointCloud Draco Evidence Gate.md")
                  && source.Contains("Final Verdict")
                  && source.Contains("GREEN")
                  && source.Contains("YELLOW")
                  && source.Contains("RED")
                  && source.Contains("BLOCKED"),
                "88D-2: evidence template names target note and verdict states");
            Check(source.Contains("git status --short --branch")
                  && source.Contains("Draco Source")
                  && source.Contains("Raw Probe Output")
                  && source.Contains("Compressed Probe Output")
                  && source.Contains("MCAP Byte-Level Inspection")
                  && source.Contains("Synchronous Native Plugin Caveat"),
                "88D-3: evidence template covers required Phase88 evidence headings");
            Check(source.Contains("same generated PointCloudFrame")
                  && source.Contains("parameter-equivalent"),
                "88D-4: evidence template records same-frame versus parameter-equivalent input");
        }

        private static void VerifyNoBundledDracoArtifacts()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var forbiddenBinaryExtensions = new[] { ".exe", ".lib", ".a", ".so", ".dylib" };
            var forbiddenSourceExtensions = new[] { ".h", ".hpp", ".cc", ".cpp", ".c" };
            var checkedRoots = new[]
            {
                Path.Combine(root, "Packages"),
                Path.Combine(root, "Unity2Foxglove", "Assets")
            };

            var forbidden = checkedRoots
                .Where(Directory.Exists)
                .SelectMany(checkedRoot => Directory.EnumerateFiles(checkedRoot, "*", SearchOption.AllDirectories))
                .Where(path => Path.GetFileName(path).IndexOf("draco", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(path => !path.EndsWith(
                    Path.Combine("Runtime", "Plugins", "Windows", "x86_64", "Unity2FoxgloveDracoNative.dll"),
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => forbiddenBinaryExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                               || forbiddenSourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .ToArray();

            Check(forbidden.Length == 0,
                "88E-1: no Draco helper executables, import libraries, or vendored native source are bundled under Packages or Unity2Foxglove/Assets");
        }

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
