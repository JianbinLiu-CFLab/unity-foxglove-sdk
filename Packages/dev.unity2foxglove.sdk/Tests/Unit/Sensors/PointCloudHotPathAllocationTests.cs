// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140E point-cloud hot-path allocation checks.

using System;
using System.IO;
using Foxglove.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "140E")]
    [Trait("Domain", "Sensors")]
    public sealed class PointCloudHotPathAllocationTests
    {
        [Fact]
        public void PointCloud2BuilderUsesOwnedArrayWithoutPooledFrameData()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloud2PackedDataBuilder.cs");

            Assert.Contains("var data = new byte[capacity];", source, StringComparison.Ordinal);
            Assert.DoesNotContain("stream.ToArray()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ArrayPool<byte>.Shared.Rent", source, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloud2BuilderWritesPackedBytesWithoutStreamWriters()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloud2PackedDataBuilder.cs");

            Assert.DoesNotContain("new MemoryStream", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new BinaryWriter", source, StringComparison.Ordinal);
            Assert.DoesNotContain("BitConverter.TryWriteBytes", source, StringComparison.Ordinal);
            Assert.Contains("WriteSingleLittleEndian", source, StringComparison.Ordinal);
            Assert.Contains("BitConverter.SingleToInt32Bits", source, StringComparison.Ordinal);
            Assert.Contains("BinaryPrimitives.WriteUInt16LittleEndian", source, StringComparison.Ordinal);
            Assert.Contains("BinaryPrimitives.WriteUInt32LittleEndian", source, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloud2NativeWorkerPoolsDeskewScratchButNotFinalFrameData()
        {
            var worker = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs");
            var compensator = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudMotionCompensator.cs");

            Assert.Contains("ArrayPool<VirtualLidarPointData>.Shared.Rent", worker, StringComparison.Ordinal);
            Assert.Contains("ArrayPool<VirtualLidarPointData>.Shared.Return", worker, StringComparison.Ordinal);
            Assert.Contains("finally", worker, StringComparison.Ordinal);
            Assert.Contains("TryCompensateVirtualLidarInto", worker, StringComparison.Ordinal);
            Assert.Contains("TryCompensateVirtualLidarInto", compensator, StringComparison.Ordinal);
            Assert.DoesNotContain("Dictionary<uint, Matrix4x4>", compensator, StringComparison.Ordinal);
            Assert.Contains("TryInterpolateMonotonic", compensator, StringComparison.Ordinal);
            Assert.Contains("lastOffsetNs", compensator, StringComparison.Ordinal);
            Assert.DoesNotContain("ArrayPool<byte>.Shared.Rent", worker, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloud2BuilderUsesArraySpecializedVirtualLidarPackPath()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloud2PackedDataBuilder.cs");

            Assert.Contains("BuildVirtualLidarFullStride(VirtualLidarPointData[] points", source, StringComparison.Ordinal);
            Assert.Contains("var validCount = CountValid(points, pointCount);", source, StringComparison.Ordinal);
            Assert.Contains("var capacity = ValidatePackedDataBudget(validCount, stride);", source, StringComparison.Ordinal);
            Assert.Contains("private static int CountValid(VirtualLidarPointData[] points, int pointCount)", source, StringComparison.Ordinal);
            Assert.Contains("validCount++;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Array.Resize(ref data, offset);", source, StringComparison.Ordinal);
            Assert.Contains("for (var i = 0; i < pointCount; i++)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void VirtualLidarNativeSnapshotsArePooledAcrossWorkerOwnershipPaths()
        {
            var scheduler = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");
            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var pool = Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/VirtualLidarPointSnapshotPool.cs");
            var payloads = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerPayloads.cs");
            var backgroundPipeline = Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundEncodePipeline.cs");
            var pointCloudPipeline = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudEncodePipeline.cs");

            Assert.Contains("VirtualLidarPointSnapshotPool.Rent(scanBuffers.EffectiveRayCount)", scheduler, StringComparison.Ordinal);
            Assert.DoesNotContain("new VirtualLidarPointData[scanBuffers.EffectiveRayCount]", scheduler, StringComparison.Ordinal);
            Assert.Contains("EnsureActiveScanSnapshotCapacity()", lidar, StringComparison.Ordinal);
            Assert.Contains("ReleaseActiveScanSnapshot()", lidar, StringComparison.Ordinal);
            Assert.DoesNotContain("new VirtualLidarPointData[_scanBuffers.EffectiveRayCount]", lidar, StringComparison.Ordinal);
            Assert.Contains("ArrayPool<VirtualLidarPointData>.Shared.Rent", pool, StringComparison.Ordinal);
            Assert.Contains("ArrayPool<VirtualLidarPointData>.Shared.Return", pool, StringComparison.Ordinal);

            Assert.Contains("RecycleSourceSnapshot()", payloads, StringComparison.Ordinal);
            Assert.Contains("VirtualLidarPointSnapshotPool.Return", payloads, StringComparison.Ordinal);
            Assert.Contains("Action<TRequest> onDropRequest", backgroundPipeline, StringComparison.Ordinal);
            Assert.Contains("Action<TResult> onDropResult", backgroundPipeline, StringComparison.Ordinal);
            Assert.Contains("DropRequest(replacedRequest)", backgroundPipeline, StringComparison.Ordinal);
            Assert.Contains("DropResult(droppedResult)", backgroundPipeline, StringComparison.Ordinal);
            Assert.Contains("result.Request.RecycleSourceSnapshot", pointCloudPipeline, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloud2BuilderPacksOnlyValidPointsIntoExactSizedData()
        {
            var points = new[]
            {
                new VirtualLidarPointData { X = 1f, Y = 2f, Z = 3f, Intensity = 0.25f, Reflectivity = 0.5f, Ring = 7, TimeOffsetSeconds = 0.001f, IsValid = 1 },
                new VirtualLidarPointData { X = 99f, Y = 99f, Z = 99f, IsValid = 0 },
                new VirtualLidarPointData { X = 4f, Y = 5f, Z = 6f, Intensity = 0.75f, Reflectivity = 0.125f, Ring = 8, TimeOffsetSeconds = 0.002f, IsValid = 1 }
            };

            var packed = PointCloud2PackedDataBuilder.BuildVirtualLidarFullStride(points, emitAbsoluteTimeNs: true);

            Assert.Equal(30U, packed.PointStride);
            Assert.Equal(60, packed.Data.Length);
            using var reader = new BinaryReader(new MemoryStream(packed.Data));
            AssertPoint(reader, 1f, 2f, 3f, 0.25f, 0.5f, 7, 0.001f, 1_000_000U);
            AssertPoint(reader, 4f, 5f, 6f, 0.75f, 0.125f, 8, 0.002f, 2_000_000U);
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

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }
    }
}
