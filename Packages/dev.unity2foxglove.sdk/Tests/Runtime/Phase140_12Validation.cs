// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-12 schema registry and message definition review fixes.

using System;
using System.IO;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for schema registry and message definition
    /// hardening found in Phase 140-12.
    /// </summary>
    public static class Phase140_12Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 140-12 schema registry review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-12: schema registry and message definition review fixes ===");
            _passed = 0;

            Ros2CatalogNullRegistryThrows();
            ProtobufDescriptorSubsetsAreBuiltInDeterministicOrder();
            ImuDescriptorBytesAreCached();
            DefaultSchemaRegistrySerializesDictionaryAccess();
            PointCloud2RowStepOverflowHasClearArgumentError();
            RawImageJsonOmissionIsDocumented();
            NativePointCloudLayoutsAreCachedAndReadOnly();
            ManagedPointCloudLayoutAvoidsIntermediateList();
            SchemaRegistryNormalizesEncodingOnce();
            SchemaRegistryEncodingBehaviorIsPreserved();
            RawImageRgb8UsesFastPath();
            RawImageEncodingBehaviorIsPreserved();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-12: {_passed} checks passed.");
        }

        private static void Ros2CatalogNullRegistryThrows()
        {
            CheckThrowsArgumentNull(
                () => FoxgloveRos2MsgSchemaCatalog.RegisterSchemas(null),
                "registry",
                "140-12A-1: Foxglove ROS2 catalog rejects null registry");
            CheckThrowsArgumentNull(
                () => Ros2StandardMsgSchemaCatalog.RegisterSchemas(null),
                "registry",
                "140-12A-2: standard ROS2 catalog rejects null registry");
        }

        private static void ProtobufDescriptorSubsetsAreBuiltInDeterministicOrder()
        {
            var valid = ProtobufDescriptorOrderingFixture.TryValidate(
                out var checkedSubsets,
                out var checkedDependencies,
                out var orderingFailures);

            Check(valid,
                $"140-12B-1: protobuf descriptor subsets emit dependencies before dependents "
                + $"({checkedSubsets} subsets, {checkedDependencies} dependency edges, {orderingFailures} ordering failures)");
        }

        private static void ImuDescriptorBytesAreCached()
        {
            Check(ReferenceEquals(ImuSchema.FileDescriptorSetData, ImuSchema.FileDescriptorSetData),
                "140-12C-1: handwritten IMU descriptor bytes are decoded once and reused");
        }

        private static void DefaultSchemaRegistrySerializesDictionaryAccess()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Registry/ISchemaRegistry.cs");

            Check(source.Contains("private readonly object _gate", StringComparison.Ordinal)
                  && source.Contains("lock (_gate)", StringComparison.Ordinal),
                "140-12D-1: DefaultSchemaRegistry serializes dictionary access");
        }

        private static void PointCloud2RowStepOverflowHasClearArgumentError()
        {
            try
            {
                _ = new PackedPointCloudFrame(
                    0UL,
                    "map",
                    1U,
                    uint.MaxValue,
                    Array.Empty<PointCloudPackedField>(),
                    2U,
                    Array.Empty<byte>(),
                    true);
            }
            catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "width")
            {
                Pass("140-12E-1: PointCloud2 row-step overflow reports width as the invalid argument");
                return;
            }

            throw new Exception("[FAIL] 140-12E-1: PointCloud2 row-step overflow reports width as the invalid argument");
        }

        private static void RawImageJsonOmissionIsDocumented()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Registry/FoxgloveSchemaDefinitions.cs");

            Check(source.Contains("RawImage is intentionally omitted", StringComparison.Ordinal)
                  && source.Contains("protobuf-only", StringComparison.Ordinal),
                "140-12F-1: RawImage JSON schema omission is documented beside core JSON schema registration");
        }

        private static void NativePointCloudLayoutsAreCachedAndReadOnly()
        {
            var points = new[]
            {
                new VirtualLidarPointData { IsValid = 1 }
            };
            var first = PackedPointCloudDataBuilder.BuildVirtualLidarFullStride(points, emitAbsoluteTimeNs: false);
            var second = PackedPointCloudDataBuilder.BuildVirtualLidarFullStride(points, emitAbsoluteTimeNs: false);
            var timed = PackedPointCloudDataBuilder.BuildVirtualLidarFullStride(points, emitAbsoluteTimeNs: true);

            Check(ReferenceEquals(first.Fields, second.Fields)
                  && first.Fields is not PointCloudPackedField[]
                  && first.Fields.Count == 7
                  && timed.Fields.Count == 8,
                "140-12H-1: native point-cloud layouts are cached without exposing mutable arrays");
        }

        private static void ManagedPointCloudLayoutAvoidsIntermediateList()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloudPackedDataBuilder.cs");
            var layout = SourceBetween(source, "public sealed class PointCloudLayout", "        }\n    }\n}");
            Check(!layout.Contains("new List<PointCloudPackedField>", StringComparison.Ordinal)
                  && !layout.Contains("fields.ToArray()", StringComparison.Ordinal),
                "140-12H-2: managed point-cloud layout allocates only its final field array");
        }

        private static void SchemaRegistryNormalizesEncodingOnce()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Registry/ISchemaRegistry.cs");
            var makeKey = SourceBetween(source, "private static string MakeKey", "private static string NormalizeEncoding");
            Check(!makeKey.Contains("NormalizeEncoding", StringComparison.Ordinal),
                "140-12H-3: schema registry key construction does not normalize an already-normalized encoding");
        }

        private static void SchemaRegistryEncodingBehaviorIsPreserved()
        {
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry { Name = "phase140.MixedEncoding", Encoding = "ProToBuF" });
            Check(registry.TryGetSchema("phase140.MixedEncoding", "PROTOBUF", out var entry)
                  && entry.Encoding == "protobuf",
                "140-12H-4: schema registry preserves case-insensitive encoding lookup");
        }

        private static void RawImageRgb8UsesFastPath()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Camera/SensorRawImageFrame.cs");
            Check(source.Contains("string.Equals(encoding, \"rgb8\", StringComparison.Ordinal)", StringComparison.Ordinal),
                "140-12H-5: exact rgb8 camera frames bypass string normalization");
        }

        private static void RawImageEncodingBehaviorIsPreserved()
        {
            var frame = new SensorRawImageFrame(0UL, "camera", 1, 1, new byte[3], " RGB8 ");
            Check(frame.Encoding == "rgb8",
                "140-12H-6: raw image encoding still accepts padded mixed-case rgb8");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase140_12Validation.cs", StringComparison.Ordinal),
                "140-12G-1: test project compiles Phase140_12Validation");
            Check(registry.Contains("--phase140-12", StringComparison.Ordinal)
                  && registry.Contains("Phase140_12Validation.Validate", StringComparison.Ordinal),
                "140-12G-2: validation registry exposes --phase140-12");
        }

        private static void CheckThrowsArgumentNull(Action action, string parameterName, string description)
        {
            try
            {
                action();
            }
            catch (ArgumentNullException ex) when (ex.ParamName == parameterName)
            {
                Pass(description);
                return;
            }

            throw new Exception("[FAIL] " + description);
        }

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception("[FAIL] " + description);
            Pass(description);
        }

        private static void Pass(string description)
        {
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
                throw new InvalidOperationException("Could not locate Phase140-12 source markers.");
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
