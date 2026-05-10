// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: Explicit IL2CPP-safe catalog of bundled official Foxglove
// protobuf schemas and their generated CLR message types.

using System;
using System.Collections.Generic;

namespace Foxglove.Schemas
{
    /// <summary>
    /// Describes one bundled official Foxglove protobuf schema.
    /// </summary>
    public sealed class FoxgloveProtoSchemaCatalogEntry
    {
        public FoxgloveProtoSchemaCatalogEntry(string schemaName, Type clrType, string category, bool hasDedicatedUnityPublisher, string note)
        {
            SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
            ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
            Category = category ?? "";
            HasDedicatedUnityPublisher = hasDedicatedUnityPublisher;
            Note = note ?? "";
        }

        /// <summary>Fully qualified Foxglove schema name, e.g. <c>foxglove.FrameTransform</c>.</summary>
        public string SchemaName { get; }

        /// <summary>Generated protobuf CLR message type.</summary>
        public Type ClrType { get; }

        /// <summary>Coarse user-facing schema category.</summary>
        public string Category { get; }

        /// <summary>Whether the package has a dedicated Unity publisher UX for this schema.</summary>
        public bool HasDedicatedUnityPublisher { get; }

        /// <summary>Short user-facing note for coverage documentation.</summary>
        public string Note { get; }
    }

    /// <summary>
    /// Static schema catalog for the bundled official Foxglove protobuf snapshot.
    /// Kept explicit instead of reflection-discovered so IL2CPP behavior remains predictable.
    /// </summary>
    public static class FoxgloveProtoSchemaCatalog
    {
        private static readonly FoxgloveProtoSchemaCatalogEntry[] EntriesArray =
        {
            Entry("foxglove.ArrowPrimitive", typeof(Foxglove.ArrowPrimitive), "visualization", false, "Scene primitive."),
            Entry("foxglove.CameraCalibration", typeof(Foxglove.CameraCalibration), "image", false, "Camera intrinsic and distortion calibration."),
            Entry("foxglove.CircleAnnotation", typeof(Foxglove.CircleAnnotation), "annotation", false, "Image annotation primitive."),
            Entry("foxglove.Color", typeof(Foxglove.Color), "geometry", false, "Shared RGBA color primitive."),
            Entry("foxglove.CompressedImage", typeof(Foxglove.CompressedImage), "image", true, "Dedicated camera publisher supports JSON and protobuf."),
            Entry("foxglove.CompressedPointCloud", typeof(Foxglove.CompressedPointCloud), "point cloud", false, "Compressed point cloud payload."),
            Entry("foxglove.CompressedVideo", typeof(Foxglove.CompressedVideo), "image", false, "Compressed video payload."),
            Entry("foxglove.CubePrimitive", typeof(Foxglove.CubePrimitive), "visualization", false, "Scene primitive."),
            Entry("foxglove.CylinderPrimitive", typeof(Foxglove.CylinderPrimitive), "visualization", false, "Scene primitive."),
            Entry("foxglove.FrameTransform", typeof(Foxglove.FrameTransform), "transform", true, "Dedicated transform publisher supports JSON and protobuf."),
            Entry("foxglove.FrameTransforms", typeof(Foxglove.FrameTransforms), "transform", false, "Batch transform message."),
            Entry("foxglove.GeoJSON", typeof(Foxglove.GeoJSON), "location", false, "GeoJSON overlay payload."),
            Entry("foxglove.Grid", typeof(Foxglove.Grid), "grid", false, "2D grid payload."),
            Entry("foxglove.ImageAnnotations", typeof(Foxglove.ImageAnnotations), "annotation", false, "Collection of image annotations."),
            Entry("foxglove.JointState", typeof(Foxglove.JointState), "robot state", false, "Single joint state."),
            Entry("foxglove.JointStates", typeof(Foxglove.JointStates), "robot state", false, "Batch joint state message."),
            Entry("foxglove.KeyValuePair", typeof(Foxglove.KeyValuePair), "metadata", false, "Shared metadata key/value primitive."),
            Entry("foxglove.LaserScan", typeof(Foxglove.LaserScan), "range", false, "Laser scan payload."),
            Entry("foxglove.LinePrimitive", typeof(Foxglove.LinePrimitive), "visualization", false, "Scene primitive."),
            Entry("foxglove.LocationFix", typeof(Foxglove.LocationFix), "location", false, "Single geospatial fix."),
            Entry("foxglove.LocationFixes", typeof(Foxglove.LocationFixes), "location", false, "Batch geospatial fix message."),
            Entry("foxglove.Log", typeof(Foxglove.Log), "debug", true, "Used by client log and FoxRun debug logging paths."),
            Entry("foxglove.ModelPrimitive", typeof(Foxglove.ModelPrimitive), "visualization", false, "Scene primitive for mesh/model references or inline data."),
            Entry("foxglove.Odometry", typeof(Foxglove.Odometry), "robot state", false, "Pose and velocity estimate."),
            Entry("foxglove.PackedElementField", typeof(Foxglove.PackedElementField), "layout", false, "Packed binary field descriptor."),
            Entry("foxglove.Point2", typeof(Foxglove.Point2), "geometry", false, "2D point primitive."),
            Entry("foxglove.Point3", typeof(Foxglove.Point3), "geometry", false, "3D point primitive."),
            Entry("foxglove.Point3InFrame", typeof(Foxglove.Point3InFrame), "geometry", false, "3D point with frame and timestamp."),
            Entry("foxglove.PointCloud", typeof(Foxglove.PointCloud), "point cloud", false, "Uncompressed point cloud payload."),
            Entry("foxglove.PointsAnnotation", typeof(Foxglove.PointsAnnotation), "annotation", false, "Image annotation primitive."),
            Entry("foxglove.Pose", typeof(Foxglove.Pose), "geometry", false, "Position and orientation primitive."),
            Entry("foxglove.PoseInFrame", typeof(Foxglove.PoseInFrame), "geometry", false, "Pose with frame and timestamp."),
            Entry("foxglove.PosesInFrame", typeof(Foxglove.PosesInFrame), "geometry", false, "Batch poses with frame and timestamp."),
            Entry("foxglove.Quaternion", typeof(Foxglove.Quaternion), "geometry", false, "Quaternion primitive."),
            Entry("foxglove.RawAudio", typeof(Foxglove.RawAudio), "audio", false, "Raw audio payload."),
            Entry("foxglove.RawImage", typeof(Foxglove.RawImage), "image", false, "Raw image payload."),
            Entry("foxglove.SceneEntity", typeof(Foxglove.SceneEntity), "visualization", false, "Scene entity with primitives."),
            Entry("foxglove.SceneEntityDeletion", typeof(Foxglove.SceneEntityDeletion), "visualization", false, "Scene deletion command."),
            Entry("foxglove.SceneUpdate", typeof(Foxglove.SceneUpdate), "visualization", true, "Dedicated scene cube publisher supports JSON and protobuf."),
            Entry("foxglove.SpherePrimitive", typeof(Foxglove.SpherePrimitive), "visualization", false, "Scene primitive."),
            Entry("foxglove.TextAnnotation", typeof(Foxglove.TextAnnotation), "annotation", false, "Image annotation primitive."),
            Entry("foxglove.TextPrimitive", typeof(Foxglove.TextPrimitive), "visualization", false, "Scene primitive."),
            Entry("foxglove.TriangleListPrimitive", typeof(Foxglove.TriangleListPrimitive), "visualization", false, "Scene primitive."),
            Entry("foxglove.Vector2", typeof(Foxglove.Vector2), "geometry", false, "2D vector primitive."),
            Entry("foxglove.Vector3", typeof(Foxglove.Vector3), "geometry", false, "3D vector primitive."),
            Entry("foxglove.VoxelGrid", typeof(Foxglove.VoxelGrid), "grid", false, "3D voxel grid payload.")
        };

        /// <summary>Read-only list of all bundled official protobuf schemas.</summary>
        public static IReadOnlyList<FoxgloveProtoSchemaCatalogEntry> Entries { get; } = Array.AsReadOnly(EntriesArray);

        /// <summary>Find a catalog entry by schema name.</summary>
        public static bool TryGet(string schemaName, out FoxgloveProtoSchemaCatalogEntry entry)
        {
            foreach (var candidate in EntriesArray)
            {
                if (string.Equals(candidate.SchemaName, schemaName, StringComparison.Ordinal))
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private static FoxgloveProtoSchemaCatalogEntry Entry(string schemaName, Type clrType, string category, bool hasDedicatedUnityPublisher, string note)
        {
            return new FoxgloveProtoSchemaCatalogEntry(schemaName, clrType, category, hasDedicatedUnityPublisher, note);
        }
    }
}
