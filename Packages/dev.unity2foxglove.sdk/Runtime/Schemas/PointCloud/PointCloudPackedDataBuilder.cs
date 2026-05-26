// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/PointCloud
// Purpose: Shared packed PointCloud.data construction for JSON/protobuf/CDR builders.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Schemas.PointCloud
{
    /// <summary>Foxglove packed numeric type values used by PackedElementField.</summary>
    public enum PointCloudPackedNumericType
    {
        Unknown = 0,
        Uint8 = 1,
        Int8 = 2,
        Uint16 = 3,
        Int16 = 4,
        Uint32 = 5,
        Int32 = 6,
        Float32 = 7,
        Float64 = 8
    }

    /// <summary>One field inside a packed point-cloud element.</summary>
    public sealed class PointCloudPackedField
    {
        /// <summary>Create a packed field descriptor.</summary>
        public PointCloudPackedField(string name, uint offset, PointCloudPackedNumericType type)
        {
            Name = name ?? string.Empty;
            Offset = offset;
            Type = type;
        }

        /// <summary>Field name.</summary>
        public string Name { get; }
        /// <summary>Byte offset from the start of each point.</summary>
        public uint Offset { get; }
        /// <summary>Numeric storage type.</summary>
        public PointCloudPackedNumericType Type { get; }
    }

    /// <summary>Packed PointCloud.data bytes plus their field layout.</summary>
    public sealed class PointCloudPackedData
    {
        /// <summary>Create packed point-cloud data.</summary>
        public PointCloudPackedData(uint pointStride, IReadOnlyList<PointCloudPackedField> fields, byte[] data)
        {
            PointStride = pointStride;
            Fields = fields ?? Array.Empty<PointCloudPackedField>();
            Data = data ?? Array.Empty<byte>();
        }

        /// <summary>Bytes per packed point.</summary>
        public uint PointStride { get; }
        /// <summary>Field descriptors.</summary>
        public IReadOnlyList<PointCloudPackedField> Fields { get; }
        /// <summary>
        /// Raw packed point bytes owned by this value. Treat as read-only; callers
        /// that need to retain mutable data should clone it first.
        /// </summary>
        public byte[] Data { get; }
    }

    /// <summary>Builds the shared packed PointCloud.data layout.</summary>
    public static class PointCloudPackedDataBuilder
    {
        /// <summary>Maximum packed point-cloud byte buffer built in one call.</summary>
        public const int MaxPackedDataBytes = 64 * 1024 * 1024;

        /// <summary>Build shared packed point bytes and field descriptors for a frame.</summary>
        public static PointCloudPackedData Build(PointCloudFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var layout = PointCloudLayout.From(frame);
            var data = Pack(frame, layout);
            return new PointCloudPackedData(layout.Stride, layout.Fields, data);
        }

        private static byte[] Pack(PointCloudFrame frame, PointCloudLayout layout)
        {
            var capacity = ValidatePackedDataBudget(frame, layout);
            using (var stream = new MemoryStream(capacity))
            using (var writer = new BinaryWriter(stream))
            {
                foreach (var point in frame.Points)
                {
                    writer.Write(point.X);
                    writer.Write(point.Y);
                    writer.Write(point.Z);

                    if (layout.HasIntensity) writer.Write(point.Intensity ?? 0f);
                    if (layout.HasReflectivity) writer.Write(point.Reflectivity ?? 0f);
                    if (layout.HasRing) writer.Write(point.Ring ?? (ushort)0);
                    if (layout.HasTimeOffset) writer.Write(point.TimeOffsetSeconds ?? 0f);
                }

                return stream.ToArray();
            }
        }

        private static int ValidatePackedDataBudget(PointCloudFrame frame, PointCloudLayout layout)
        {
            var packedBytes = checked((long)frame.Points.Count * layout.Stride);
            if (packedBytes > MaxPackedDataBytes)
            {
                throw new InvalidOperationException(
                    $"PointCloud packed data exceeds {MaxPackedDataBytes} bytes ({packedBytes} requested).");
            }

            return (int)packedBytes;
        }

        private sealed class PointCloudLayout
        {
            public bool HasIntensity { get; private set; }
            public bool HasReflectivity { get; private set; }
            public bool HasRing { get; private set; }
            public bool HasTimeOffset { get; private set; }
            public uint Stride { get; private set; }
            public PointCloudPackedField[] Fields { get; private set; }

            public static PointCloudLayout From(PointCloudFrame frame)
            {
                var layout = new PointCloudLayout();
                foreach (var point in frame.Points)
                {
                    layout.HasIntensity |= point.Intensity.HasValue;
                    layout.HasReflectivity |= point.Reflectivity.HasValue;
                    layout.HasRing |= point.Ring.HasValue;
                    layout.HasTimeOffset |= point.TimeOffsetSeconds.HasValue;
                }

                var fields = new List<PointCloudPackedField>
                {
                    Field("x", 0, PointCloudPackedNumericType.Float32),
                    Field("y", 4, PointCloudPackedNumericType.Float32),
                    Field("z", 8, PointCloudPackedNumericType.Float32)
                };

                uint offset = 12;
                if (layout.HasIntensity) AddField(fields, "intensity", PointCloudPackedNumericType.Float32, ref offset, 4);
                if (layout.HasReflectivity) AddField(fields, "reflectivity", PointCloudPackedNumericType.Float32, ref offset, 4);
                if (layout.HasRing) AddField(fields, "ring", PointCloudPackedNumericType.Uint16, ref offset, 2);
                if (layout.HasTimeOffset) AddField(fields, "time_offset", PointCloudPackedNumericType.Float32, ref offset, 4);

                layout.Stride = offset;
                layout.Fields = fields.ToArray();
                return layout;
            }

            private static void AddField(List<PointCloudPackedField> fields, string name, PointCloudPackedNumericType type, ref uint offset, uint width)
            {
                fields.Add(Field(name, offset, type));
                offset += width;
            }

            private static PointCloudPackedField Field(string name, uint offset, PointCloudPackedNumericType type)
            {
                return new PointCloudPackedField(name, offset, type);
            }
        }
    }
}
