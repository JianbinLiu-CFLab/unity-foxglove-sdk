// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140E point-cloud hot-path allocation checks.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{


public sealed partial class PointCloudHotPathAllocationTests
    {
        [Fact]
        public void PackedPointCloudFrameTracksValidCountSeparatelyFromPublishedPointSlots()
        {
            var data = new byte[90];
            var fields = new[] { new PointCloudPackedField("x", 0, PointCloudPackedNumericType.Float32) };

            var frame = new PackedPointCloudFrame(
                unixNs: 1UL,
                frameId: "lidar",
                height: 1U,
                width: 3U,
                fields: fields,
                pointStep: 30U,
                data: data,
                isDense: false,
                validCount: 2);

            Assert.Equal(3U, frame.Width);
            Assert.Equal(90U, frame.RowStep);
            Assert.Equal(2, frame.ValidCount);
            Assert.False(frame.IsDense);
        }

        [Fact]
        public void PointCloud2BuilderCanPackReferenceFrameWithZeroedTimeOffsets()
        {
            var points = new[]
            {
                new VirtualLidarPointData { X = 1f, Y = 2f, Z = 3f, AcquisitionX = 9f, AcquisitionY = 9f, AcquisitionZ = 9f, HasAcquisitionFrame = 1, Intensity = 0.25f, Reflectivity = 0.5f, Ring = 7, TimeOffsetSeconds = 0.001f, IsValid = 1 },
                new VirtualLidarPointData { X = 4f, Y = 5f, Z = 6f, AcquisitionX = 8f, AcquisitionY = 8f, AcquisitionZ = 8f, HasAcquisitionFrame = 1, Intensity = 0.75f, Reflectivity = 0.125f, Ring = 8, TimeOffsetSeconds = 0.002f, IsValid = 1 }
            };

            var packed = PackedPointCloudDataBuilder.BuildVirtualLidarFullStride(
                points,
                pointCount: points.Length,
                emitAbsoluteTimeNs: true,
                useAcquisitionFrameCoordinates: false,
                zeroTimeOffset: true);

            Assert.Equal(30U, packed.PointStride);
            Assert.Equal(60, packed.Data.Length);
            using var reader = new BinaryReader(new MemoryStream(packed.Data));
            AssertPoint(reader, 1f, 2f, 3f, 0.25f, 0.5f, 7, 0f, 0U);
            AssertPoint(reader, 4f, 5f, 6f, 0.75f, 0.125f, 8, 0f, 0U);
            Assert.Equal(packed.Data.Length, reader.BaseStream.Position);
        }

        [Fact]
        public void DracoEncoderUsesPooledXyzWithoutSizingOutputFromRentalLength()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudNativeEncoder.cs");

            Assert.Contains("ArrayPool<float>.Shared.Rent", source, StringComparison.Ordinal);
            Assert.Contains("ArrayPool<float>.Shared.Return", source, StringComparison.Ordinal);
            Assert.Contains("finally", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new float[checked(pointCount * 3)]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("xyz.Length * sizeof(float)", source, StringComparison.Ordinal);
            Assert.Contains("pointCount * XyzBytesPerPoint", source, StringComparison.Ordinal);
            Assert.Contains("validCount * XyzBytesPerPoint", source, StringComparison.Ordinal);
            Assert.Contains("GCHandle.Alloc(xyz, GCHandleType.Pinned)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void DracoMsgPackPathDoesNotFallbackToProtobuf()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Draco.cs");

            Assert.Contains("TryPublishComponentMessagePackDraco(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("&& TryPublishComponentMessagePackDraco", source, StringComparison.Ordinal);
            Assert.Contains("PublishProto(result.WebSocketPayload", source, StringComparison.Ordinal);
        }

        private static void AssertPoint(
            BinaryReader reader,
            float x,
            float y,
            float z,
            float intensity,
            float reflectivity,
            ushort ring,
            float timeOffsetSeconds,
            uint absoluteTimeNs)
        {
            Assert.Equal(x, reader.ReadSingle());
            Assert.Equal(y, reader.ReadSingle());
            Assert.Equal(z, reader.ReadSingle());
            Assert.Equal(intensity, reader.ReadSingle());
            Assert.Equal(reflectivity, reader.ReadSingle());
            Assert.Equal(ring, reader.ReadUInt16());
            Assert.Equal(timeOffsetSeconds, reader.ReadSingle());
            Assert.Equal(absoluteTimeNs, reader.ReadUInt32());
        }

        private static void PopulateLidarPoints(VirtualLidarPointData[] points, int validModulo)
        {
            for (var i = 0; i < points.Length; i++)
            {
                var valid = i % validModulo != 0;
                points[i] = new VirtualLidarPointData
                {
                    X = i,
                    Y = i * 0.5f,
                    Z = i * 0.25f,
                    AcquisitionX = i,
                    AcquisitionY = i * 0.5f,
                    AcquisitionZ = i * 0.25f,
                    HasAcquisitionFrame = 1,
                    Intensity = 0.5f,
                    Reflectivity = 0.25f,
                    Ring = (ushort)(i % 128),
                    TimeOffsetSeconds = i * 0.000001f,
                    IsValid = valid ? (byte)1 : (byte)0
                };
            }
        }

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }
            return count;
        }

        private static readonly string RepoRoot = FindRepoRoot();

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                    || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }
}
