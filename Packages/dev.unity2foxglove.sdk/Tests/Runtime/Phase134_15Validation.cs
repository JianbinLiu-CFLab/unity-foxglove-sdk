// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-15 regression coverage for ROS2 .msg CDR schema helpers.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase134_15Validation.
    /// </summary>
    public static class Phase134_15Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-15: ROS2 Msg CDR Schemas ===");
            _passed = 0;

            CdrReaderPreflightsFixedArraysBeforeAllocation();
            CompressedBuildersRejectMissingRequiredPayloadFields();
            LaserScanCdrBuilderKeepsRequiredRangeAndFlexibleAngleGuards();
            PublisherSchemaNamesUseBuilderConstants();
            Ros2SchemaSetupFailsFastForNullRegistry();
            CdrPayloadValidatorUsesArgumentNullForNullPayload();
            SceneUpdateAndPointCloudWritersFailFastForFutureEnumExpansion();
            Ros2CdrWriterDocumentsNullSequenceSemanticsAndSupportsCapacityHints();

            Console.WriteLine($"Phase 134-15: {_passed} checks passed.");
        }

        private static void CdrReaderPreflightsFixedArraysBeforeAllocation()
        {
            var reader = new Ros2CdrReader(new byte[] { 0x00, 0x01, 0x00, 0x00 });
            Check(Throws<InvalidDataException>(() => reader.ReadFloat64Fixed(1000)),
                "134-15A-1: fixed float64 arrays reject impossible lengths before allocation");

            Check(Throws<ArgumentOutOfRangeException>(() => new Ros2CdrReader(new byte[] { 0x00, 0x01, 0x00, 0x00 })
                    .ReadFloat64Fixed(-1)),
                "134-15A-2: fixed float64 arrays reject negative lengths");

            var testReader = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Ros2CdrTestReader.cs");
            var fixedReader = Slice(testReader, "public double[] ReadFloat64Fixed(int length)", "        private void Align");
            Check(!fixedReader.Contains("Align(8);", StringComparison.Ordinal)
                  && fixedReader.Contains("values[i] = ReadFloat64();", StringComparison.Ordinal),
                "134-15A-3: test CDR fixed float64 reader relies on element self-alignment");
        }

        private static void CompressedBuildersRejectMissingRequiredPayloadFields()
        {
            Check(Throws<ArgumentException>(() => Ros2CdrCompressedImageBuilder.Serialize(0, "camera", null, "jpeg")),
                "134-15B-1: compressed image rejects null payload");
            Check(Throws<ArgumentException>(() => Ros2CdrCompressedImageBuilder.Serialize(0, "camera", Array.Empty<byte>(), "jpeg")),
                "134-15B-2: compressed image rejects empty payload");
            Check(Throws<ArgumentException>(() => Ros2CdrCompressedImageBuilder.Serialize(0, "camera", new byte[] { 1 }, null)),
                "134-15B-3: compressed image rejects null format");
            Check(Throws<ArgumentException>(() => Ros2CdrCompressedImageBuilder.Serialize(0, "camera", new byte[] { 1 }, " ")),
                "134-15B-4: compressed image rejects blank format");

            var frame = new PointCloudFrame { UnixNs = 1, FrameId = "map" };
            Check(Throws<ArgumentException>(() => Ros2CdrCompressedPointCloudBuilder.Serialize(frame, new byte[] { 1 }, null)),
                "134-15B-5: compressed point cloud rejects null format");
            Check(Throws<ArgumentException>(() => Ros2CdrCompressedPointCloudBuilder.Serialize(frame, new byte[] { 1 }, "")),
                "134-15B-6: compressed point cloud rejects empty format");
            Check(Ros2CdrCompressedPointCloudBuilder.Serialize(frame, new byte[] { 1 }).Length > 4,
                "134-15B-7: compressed point cloud default Draco format remains usable");
        }

        private static void LaserScanCdrBuilderKeepsRequiredRangeAndFlexibleAngleGuards()
        {
            Check(Throws<ArgumentNullException>(() => Ros2CdrLaserScanBuilder.Serialize(0, "laser", -1, 1, null)),
                "134-15C-1: CDR LaserScan rejects null ranges");
            Check(Throws<ArgumentOutOfRangeException>(() => Ros2CdrLaserScanBuilder.Serialize(0, "laser", double.NaN, 1, new[] { 1.0 })),
                "134-15C-2: CDR LaserScan rejects NaN start angles");
            Check(Throws<ArgumentOutOfRangeException>(() => Ros2CdrLaserScanBuilder.Serialize(0, "laser", -1, double.PositiveInfinity, new[] { 1.0 })),
                "134-15C-3: CDR LaserScan rejects infinite end angles");
            Check(Ros2CdrLaserScanBuilder.Serialize(0, "laser", 1, 1, new[] { 1.0 }).Length > 0,
                "134-15C-4: CDR LaserScan accepts single-beam equal angles");
            Check(Ros2CdrLaserScanBuilder.Serialize(0, "laser", 1, -1, new[] { 1.0 }).Length > 0,
                "134-15C-5: CDR LaserScan accepts reverse or wrapped angle ranges");

            var payload = Ros2CdrLaserScanBuilder.Serialize(0, "laser", -1, 1, new[] { 1.0 }, null);
            var reader = new Ros2CdrReader(payload);
            reader.ReadInt32();
            reader.ReadUInt32();
            Check(reader.ReadString() == "laser"
                  && reader.ReadFloat64Fixed(7).Length == 7
                  && reader.ReadFloat64() == -1
                  && reader.ReadFloat64() == 1
                  && reader.ReadFloat64Sequence().SequenceEqual(new[] { 1.0 })
                  && reader.ReadFloat64Sequence().Length == 0,
                "134-15C-6: CDR LaserScan still accepts null intensities as an empty optional sequence");
        }

        private static void PublisherSchemaNamesUseBuilderConstants()
        {
            Check(Ros2PublisherSchemaNames.FrameTransform == Ros2CdrFrameTransformBuilder.SchemaName,
                "134-15D-1: FrameTransform publisher schema name follows builder constant");
            Check(Ros2PublisherSchemaNames.SceneUpdate == Ros2CdrSceneUpdateBuilder.SchemaName,
                "134-15D-2: SceneUpdate publisher schema name follows builder constant");
            Check(Ros2PublisherSchemaNames.CompressedImage == Ros2CdrCompressedImageBuilder.SchemaName,
                "134-15D-3: CompressedImage publisher schema name follows builder constant");
            Check(Ros2PublisherSchemaNames.CameraCalibration == Ros2CdrCameraCalibrationBuilder.SchemaName,
                "134-15D-4: CameraCalibration publisher schema name follows builder constant");
            Check(Ros2PublisherSchemaNames.LaserScan == Ros2CdrLaserScanBuilder.SchemaName,
                "134-15D-5: LaserScan publisher schema name follows builder constant");
            Check(Ros2PublisherSchemaNames.PointCloud == Ros2CdrPointCloudBuilder.SchemaName,
                "134-15D-6: PointCloud publisher schema name follows builder constant");
            Check(Ros2PublisherSchemaNames.CompressedPointCloud == Ros2CdrCompressedPointCloudBuilder.SchemaName,
                "134-15D-7: CompressedPointCloud publisher schema name follows builder constant");

            var source = File.ReadAllText(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Publishing/Ros2PublisherSchemaNames.cs");
            Check(source.Contains("Ros2CdrFrameTransformBuilder.SchemaName")
                  && source.Contains("Ros2CdrCompressedPointCloudBuilder.SchemaName"),
                "134-15D-8: publisher schema-name source references builder constants directly");
        }

        private static void Ros2SchemaSetupFailsFastForNullRegistry()
        {
            Check(Throws<ArgumentNullException>(() => Ros2MsgSchemasSetup.RegisterSchemas(null)),
                "134-15E-1: ROS2 msg schema setup rejects null registries at the public boundary");
        }

        private static void CdrPayloadValidatorUsesArgumentNullForNullPayload()
        {
            Check(Throws<ArgumentNullException>(() => Ros2CdrPayloadValidator.Validate(null)),
                "134-15F-1: CDR payload validator reports null payloads with ArgumentNullException");
        }

        private static void SceneUpdateAndPointCloudWritersFailFastForFutureEnumExpansion()
        {
            var sceneSource = File.ReadAllText(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrSceneUpdateBuilder.cs");
            var pointCloudSource = File.ReadAllText(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrPointCloudBuilder.cs");
            Check(sceneSource.Contains("checked((byte)deletion.Type)"),
                "134-15G-1: SceneUpdate deletion enum conversion is checked");
            Check(pointCloudSource.Contains("writer.WriteUInt8(MapDatatype(field.Type));")
                  && pointCloudSource.Contains("checked((byte)type)")
                  && pointCloudSource.Contains("Unsupported PointCloud packed numeric type"),
                "134-15G-2: PointCloud field type enum conversion is checked through a rejecting helper");
            Check(!sceneSource.Contains("Phase 91 CDR smoke builder")
                  && sceneSource.Contains("serialization is not supported by this CDR builder"),
                "134-15G-3: SceneUpdate unsupported-field error is product-facing");

            var message = new SceneUpdateMessage
            {
                Entities =
                {
                    new SceneEntity
                    {
                        Id = "unsupported",
                        Arrows = { new ArrowPrimitive() }
                    }
                }
            };
            var ex = Catch<NotSupportedException>(() => Ros2CdrSceneUpdateBuilder.Serialize(message));
            Check(ex != null && ex.Message.Contains("SceneUpdate arrows serialization is not supported"),
                "134-15G-4: SceneUpdate unsupported primitive exception uses the product-facing message");
        }

        private static void Ros2CdrWriterDocumentsNullSequenceSemanticsAndSupportsCapacityHints()
        {
            var writer = new Ros2CdrWriter(1024);
            writer.WriteString(null);
            writer.WriteByteArray(Array.Empty<byte>());
            writer.WriteFloat64Sequence(Array.Empty<double>());
            writer.WriteUInt32Sequence(null);
            Check(writer.ToArray().Length > 4,
                "134-15H-1: CDR writer capacity-hint constructor preserves normal write behavior");
            Check(Throws<ArgumentNullException>(() => new Ros2CdrWriter().WriteByteArray((byte[])null)),
                "134-15H-2: CDR writer rejects null required byte arrays");
            Check(Throws<ArgumentNullException>(() => new Ros2CdrWriter().WriteFloat64Sequence(null)),
                "134-15H-3: CDR writer rejects null required float64 sequences");

            var source = File.ReadAllText(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Cdr/Ros2CdrWriter.cs");
            Check(source.Contains("approximate output capacity hint")
                  && source.Contains("Write a required uint8 sequence")
                  && source.Contains("Write a required float64 sequence")
                  && source.Contains("builders must reject null when the field is required"),
                "134-15H-4: CDR writer documents capacity hints and null sequence semantics");
        }

        private static bool Throws<T>(Action action)
            where T : Exception
        {
            return Catch<T>(action) != null;
        }

        private static T Catch<T>(Action action)
            where T : Exception
        {
            try
            {
                action();
                return null;
            }
            catch (T ex)
            {
                return ex;
            }
        }

        private static string Slice(string source, string startToken, string endToken)
        {
            var start = source.IndexOf(startToken, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
            return end < 0 ? source.Substring(start) : source.Substring(start, end - start);
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
