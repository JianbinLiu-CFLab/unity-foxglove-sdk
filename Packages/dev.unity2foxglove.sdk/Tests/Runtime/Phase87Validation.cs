// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 87 validation for CompressedPointCloud / Draco spike scaffolding.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the Phase 87 compressed point-cloud spike boundary.
    /// </summary>
    public static class Phase87Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 87: CompressedPointCloud Draco Spike ===");
            _passed = 0;

            VerifyCompressedPointCloudBuilder();
            VerifyHelperProtocol();
            VerifySpikePublisherSource();
            VerifyNativeHelperArtifacts();
            VerifyNoBundledDracoBinaries();

            Console.WriteLine($"Phase 87: {_passed} checks passed.");
        }

        private static void VerifyCompressedPointCloudBuilder()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/CompressedPointCloudMessageBuilder.cs");
            Check(!string.IsNullOrEmpty(source),
                "87A-1: CompressedPointCloud builder exists");
            Check(source.Contains("DracoFormat = \"draco\"")
                  && source.Contains("Foxglove.CompressedPointCloud")
                  && source.Contains("ByteString.CopyFrom"),
                "87A-2: builder targets foxglove.CompressedPointCloud with draco payload bytes");

            var builderType = FindType("Foxglove.Schemas.CompressedPointCloudMessageBuilder");
            Check(builderType != null,
                "87A-3: CompressedPointCloud builder type is loadable");

            var frame = new PointCloudFrame
            {
                UnixNs = 1_700_000_123_456_789_012UL,
                FrameId = "unity_lidar"
            };
            frame.Points.Add(new PointCloudPoint(1f, 2f, 3f));
            var payload = new byte[] { 1, 2, 3, 4, 5 };

            var create = builderType.GetMethod("CreateProtobuf", BindingFlags.Public | BindingFlags.Static);
            Check(create != null,
                "87A-4: builder exposes CreateProtobuf");
            var message = (Foxglove.CompressedPointCloud)create.Invoke(null, new object[] { frame, payload });
            Check(message.Timestamp.Seconds == 1_700_000_123L
                  && message.Timestamp.Nanos == 456_789_012
                  && message.FrameId == "unity_lidar"
                  && message.Format == "draco"
                  && message.Data.ToByteArray().SequenceEqual(payload)
                  && message.Pose.Position.X == 0
                  && message.Pose.Orientation.W == 1,
                "87A-5: builder maps timestamp, frame, identity pose, draco format, and payload");

            var serialize = builderType.GetMethod("SerializeProtobuf", BindingFlags.Public | BindingFlags.Static);
            Check(serialize != null,
                "87A-6: builder exposes SerializeProtobuf");
            var bytes = (byte[])serialize.Invoke(null, new object[] { frame, payload });
            var parsed = Foxglove.CompressedPointCloud.Parser.ParseFrom(bytes);
            Check(parsed.Format == "draco" && parsed.Data.ToByteArray().SequenceEqual(payload),
                "87A-7: serialized CompressedPointCloud round-trips");
        }

        private static void VerifyHelperProtocol()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudEncoderSidecar.cs");
            Check(!string.IsNullOrEmpty(source),
                "87B-1: Draco sidecar/protocol source exists");
            Check(source.Contains("class DracoPointCloudHelperProtocol")
                  && source.Contains("BuildXyzFramePayload")
                  && source.Contains("Write(point.X)")
                  && source.Contains("Write(point.Y)")
                  && source.Contains("Write(point.Z)"),
                "87B-2: helper protocol writes PointCloudFrame points as XYZ float32 records");
            Check(source.Contains("uint32 point_count") || source.Contains("writer.Write((uint)frame.Points.Count)"),
                "87B-3: helper protocol prefixes point_count as uint32");
            Check(source.Contains("MaxPayloadBytes") && source.Contains("ReadLittleEndianLength") && source.Contains("payloadLength <= 0"),
                "87B-4: sidecar reads bounded length-prefixed Draco payloads");

            var protocolType = FindType("Foxglove.Schemas.PointCloud.DracoPointCloudHelperProtocol");
            Check(protocolType != null,
                "87B-5: helper protocol type is loadable");
            var build = protocolType.GetMethod("BuildXyzFramePayload", BindingFlags.Public | BindingFlags.Static);
            Check(build != null,
                "87B-6: helper protocol exposes BuildXyzFramePayload");

            var frame = new PointCloudFrame();
            frame.Points.Add(new PointCloudPoint(1.5f, -2f, 3.25f) { Intensity = 99f, Ring = 7 });
            frame.Points.Add(new PointCloudPoint(4f, 5f, 6f));
            var bytes = (byte[])build.Invoke(null, new object[] { frame });
            Check(bytes.Length == 4 + 2 * 12
                  && BitConverter.ToUInt32(bytes, 0) == 2
                  && BitConverter.ToSingle(bytes, 4) == 1.5f
                  && BitConverter.ToSingle(bytes, 8) == -2f
                  && BitConverter.ToSingle(bytes, 12) == 3.25f
                  && BitConverter.ToSingle(bytes, 16) == 4f
                  && BitConverter.ToSingle(bytes, 20) == 5f
                  && BitConverter.ToSingle(bytes, 24) == 6f,
                "87B-7: helper protocol output is uint32 count followed by contiguous XYZ float32 data");
        }

        private static void VerifySpikePublisherSource()
        {
            var raw = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var compressed = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedPointCloudPublisher.cs");
            Check(!string.IsNullOrEmpty(compressed),
                "87C-1: compressed point-cloud spike publisher source exists");
            Check(compressed.Contains("class FoxgloveCompressedPointCloudPublisher : FoxglovePointCloudPublisher")
                  && compressed.Contains("DefaultTopic => \"/unity/point_cloud_draco\"")
                  && compressed.Contains("SchemaNameOverride => \"foxglove.CompressedPointCloud\"")
                  && compressed.Contains("SupportsJsonEncoding => false"),
                "87C-2: spike publisher uses isolated topic and protobuf-only CompressedPointCloud schema");
            Check(compressed.Contains("DracoPointCloudNativeEncoder")
                  && compressed.Contains("CompressedPointCloudMessageBuilder.SerializeProtobuf")
                  && compressed.Contains("PublishProto"),
                "87C-3: spike publisher encodes through Draco native plugin and publishes CompressedPointCloud protobuf");
            Check(compressed.Contains("LogDracoFailure")
                  && compressed.Contains("publishes nothing")
                  && !compressed.Contains("PointCloudMessageBuilder.CreateJson")
                  && !compressed.Contains("Foxglove.PointCloud"),
                "87C-4: spike publisher logs failure and does not fall back to raw PointCloud");
            Check(raw.Contains("protected virtual string DefaultTopic")
                  && raw.Contains("protected virtual string SchemaNameOverride")
                  && raw.Contains("protected virtual void PublishPreparedFrame")
                  && raw.Contains("protected virtual PointCloudFrame PrepareFrameForQoS"),
                "87C-5: raw point-cloud publisher exposes reusable QoS/publish hooks");
        }

        private static void VerifyNativeHelperArtifacts()
        {
            var readme = ReadRepoText("Scripts/native/draco_probe/README.md");
            var source = ReadRepoText("Scripts/native/draco_probe/draco_probe_encoder.cpp");
            Check(!string.IsNullOrEmpty(readme) && !string.IsNullOrEmpty(source),
                "87D-1: Draco native helper README and source exist");
            Check(readme.Contains("uint32 point_count")
                  && readme.Contains("float32 x")
                  && readme.Contains("uint32 payload_length")
                  && readme.Contains("stderr"),
                "87D-2: helper README documents stdin/stdout protocol");
            Check(readme.Contains("POINT_CLOUD")
                  && readme.Contains("11-bit")
                  && readme.Contains("compression level 7")
                  && readme.Contains("Draco source tag")
                  && readme.Contains("Result States"),
                "87D-3: helper README documents encoder settings and result states");
            Check(source.Contains("draco::PointCloud")
                  && source.Contains("GeometryAttribute::POSITION")
                  && source.Contains("SetAttributeQuantization")
                  && source.Contains("SetSpeedOptions")
                  && source.Contains("payload_length"),
                "87D-4: helper source encodes POINT_CLOUD position data and writes length-prefixed payloads");
        }

        private static void VerifyNoBundledDracoBinaries()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var forbiddenExtensions = new[] { ".dll", ".exe", ".lib", ".a", ".so", ".dylib" };
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
                .Where(path => forbiddenExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .ToArray();

            Check(forbidden.Length == 0,
                "87E-1: no spike helper Draco binaries are bundled under Packages or Unity2Foxglove/Assets");
        }

        private static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, throwOnError: false))
                .FirstOrDefault(type => type != null);
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
                throw new FileNotFoundException("Required repository file is missing.", path);
            return File.ReadAllText(path);
        }
    }
}
