// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.FoxgloveSDK.Schemas.PointCloud
{
    /// <summary>One neutral PointCloud2 field descriptor.</summary>
    public sealed class PointCloud2InboundField
    {
        public PointCloud2InboundField(string name, uint offset, byte datatype, uint count = 1)
        {
            Name = name ?? string.Empty;
            Offset = offset;
            Datatype = datatype;
            Count = count;
        }

        public string Name { get; }
        public uint Offset { get; }
        public byte Datatype { get; }
        public uint Count { get; }
    }

    /// <summary>Schema-neutral PointCloud2 message copied from a ROS callback.</summary>
    public sealed class PointCloud2InboundMessage
    {
        public PointCloud2InboundMessage(
            ulong unixNs,
            string frameId,
            uint height,
            uint width,
            IReadOnlyList<PointCloud2InboundField> fields,
            uint pointStep,
            uint rowStep,
            byte[] data,
            bool isBigEndian,
            bool isDense)
        {
            UnixNs = unixNs;
            FrameId = frameId ?? string.Empty;
            Height = height;
            Width = width;
            Fields = fields ?? Array.Empty<PointCloud2InboundField>();
            PointStep = pointStep;
            RowStep = rowStep;
            Data = data ?? Array.Empty<byte>();
            IsBigEndian = isBigEndian;
            IsDense = isDense;
        }

        public ulong UnixNs { get; }
        public string FrameId { get; }
        public uint Height { get; }
        public uint Width { get; }
        public IReadOnlyList<PointCloud2InboundField> Fields { get; }
        public uint PointStep { get; }
        public uint RowStep { get; }
        public byte[] Data { get; }
        public bool IsBigEndian { get; }
        public bool IsDense { get; }
    }

    /// <summary>Maps a copied PointCloud2 message into the SDK packed frame contract.</summary>
    public static class PointCloud2InboundMapper
    {
        public const int MaxFieldCount = 128;
        public const int MaxFieldNameBytes = 256;
        public const int MaxFieldMetadataBytes = 16 * 1024;

        public static bool TryMap(
            PointCloud2InboundMessage message,
            long maxBytes,
            string topic,
            out PackedPointCloudFrame frame,
            out string error)
        {
            frame = null;
            error = string.Empty;
            if (message == null)
                return Reject("message is null", out error);
            if (maxBytes <= 0)
                return Reject("maxBytes must be positive", out error);
            if (message.IsBigEndian)
                return Reject("big-endian PointCloud2 is not supported", out error);
            if (message.Height == 0 || message.Width == 0 || message.PointStep == 0)
                return Reject("height, width, and pointStep must be positive", out error);

            ulong expectedRowStep;
            ulong expectedBytes;
            try
            {
                expectedRowStep = checked((ulong)message.Width * message.PointStep);
                expectedBytes = checked(expectedRowStep * message.Height);
            }
            catch (OverflowException)
            {
                return Reject("PointCloud2 dimensions overflow", out error);
            }

            if (message.RowStep < expectedRowStep)
                return Reject("rowStep is smaller than width * pointStep", out error);
            try
            {
                expectedBytes = checked((ulong)message.RowStep * message.Height);
            }
            catch (OverflowException)
            {
                return Reject("PointCloud2 row dimensions overflow", out error);
            }
            if ((ulong)message.Data.Length != expectedBytes)
                return Reject("data length does not equal height * rowStep", out error);
            if (expectedBytes > (ulong)maxBytes)
                return Reject("PointCloud2 frame exceeds maxBytes", out error);

            if (!TryMapFields(message, out var fields, out error))
                return false;

            var copiedData = new byte[checked((int)(expectedRowStep * message.Height))];
            CopyPaddedRows(message.Data, (int)message.RowStep, copiedData, (int)expectedRowStep, (int)message.Height);
            frame = new PackedPointCloudFrame(
                message.UnixNs,
                message.FrameId,
                message.Height,
                message.Width,
                fields,
                message.PointStep,
                copiedData,
                message.IsDense,
                topic);
            return true;
        }

        /// <summary>
        /// Maps a compact message while transferring ownership of its data buffer to the frame.
        /// The data buffer must come from <see cref="PointCloudPackedByteBufferPool.Rent(int)"/>
        /// and the returned frame must be disposed exactly once.
        /// </summary>
        public static bool TryMapOwned(
            PointCloud2InboundMessage message,
            long maxBytes,
            string topic,
            out PackedPointCloudFrame frame,
            out string error)
            => TryMapCore(message, maxBytes, topic, copyData: false, ownsPooledData: true, out frame, out error);

        private static bool TryMapCore(
            PointCloud2InboundMessage message,
            long maxBytes,
            string topic,
            bool copyData,
            bool ownsPooledData,
            out PackedPointCloudFrame frame,
            out string error)
        {
            frame = null;
            error = string.Empty;
            if (message == null)
                return Reject("message is null", out error);
            if (maxBytes <= 0)
                return Reject("maxBytes must be positive", out error);
            if (message.IsBigEndian)
                return Reject("big-endian PointCloud2 is not supported", out error);
            if (message.Height == 0 || message.Width == 0 || message.PointStep == 0)
                return Reject("height, width, and pointStep must be positive", out error);

            ulong expectedRowStep;
            ulong expectedBytes;
            try
            {
                expectedRowStep = checked((ulong)message.Width * message.PointStep);
                expectedBytes = checked(expectedRowStep * message.Height);
            }
            catch (OverflowException)
            {
                return Reject("PointCloud2 dimensions overflow", out error);
            }

            if (message.RowStep < expectedRowStep)
                return Reject("rowStep is smaller than width * pointStep", out error);
            try
            {
                expectedBytes = checked((ulong)message.RowStep * message.Height);
            }
            catch (OverflowException)
            {
                return Reject("PointCloud2 row dimensions overflow", out error);
            }
            if ((ulong)message.Data.Length != expectedBytes)
                return Reject("data length does not equal height * rowStep", out error);
            if (expectedBytes > (ulong)maxBytes)
                return Reject("PointCloud2 frame exceeds maxBytes", out error);
            if (message.RowStep != expectedRowStep)
                return Reject("owned PointCloud2 data must be compacted before mapping", out error);

            if (!TryMapFields(message, out var fields, out error))
                return false;

            var data = copyData ? new byte[message.Data.Length] : message.Data;
            if (copyData)
                Buffer.BlockCopy(message.Data, 0, data, 0, data.Length);
            frame = new PackedPointCloudFrame(
                message.UnixNs,
                message.FrameId,
                message.Height,
                message.Width,
                fields,
                message.PointStep,
                data,
                message.IsDense,
                topic,
                ownsPooledData: ownsPooledData);
            return true;
        }

        private static bool TryMapFields(
            PointCloud2InboundMessage message,
            out List<PointCloudPackedField> fields,
            out string error)
        {
            fields = null;
            error = string.Empty;
            if (message.Fields.Count > MaxFieldCount)
                return Reject("PointCloud2 field metadata exceeds the field count limit", out error);

            var metadataBytes = 0;
            fields = new List<PointCloudPackedField>(message.Fields.Count);
            foreach (var field in message.Fields)
            {
                if (field == null || field.Count != 1)
                    return Reject("PointCloud2 fields must have count 1", out error);
                var nameBytes = Encoding.UTF8.GetByteCount(field.Name ?? string.Empty);
                if (nameBytes > MaxFieldNameBytes)
                    return Reject("PointCloud2 field metadata name exceeds the limit", out error);
                try
                {
                    metadataBytes = checked(metadataBytes + nameBytes + 16);
                }
                catch (OverflowException)
                {
                    return Reject("PointCloud2 field metadata exceeds the limit", out error);
                }
                if (metadataBytes > MaxFieldMetadataBytes)
                    return Reject("PointCloud2 field metadata exceeds the limit", out error);
                if (!TryMapDatatype(field.Datatype, out var type, out var width))
                    return Reject("PointCloud2 field has an unknown datatype", out error);
                if ((ulong)field.Offset + width > message.PointStep)
                    return Reject("PointCloud2 field exceeds pointStep", out error);
                fields.Add(new PointCloudPackedField(field.Name, field.Offset, type));
            }

            return true;
        }

        private static void CopyPaddedRows(byte[] source, int sourceRowStep, byte[] destination, int destinationRowStep, int height)
        {
            if (sourceRowStep == destinationRowStep)
            {
                Buffer.BlockCopy(source, 0, destination, 0, destination.Length);
                return;
            }

            for (var row = 0; row < height; row++)
            {
                Buffer.BlockCopy(
                    source,
                    checked(row * sourceRowStep),
                    destination,
                    checked(row * destinationRowStep),
                    destinationRowStep);
            }
        }

        private static bool Reject(string message, out string error)
        {
            error = message;
            return false;
        }

        private static bool TryMapDatatype(byte datatype, out PointCloudPackedNumericType type, out ulong width)
        {
            width = datatype switch
            {
                1 or 2 => 1UL,
                3 or 4 => 2UL,
                5 or 6 or 7 => 4UL,
                8 => 8UL,
                _ => 0UL
            };
            type = datatype switch
            {
                1 => PointCloudPackedNumericType.Int8,
                2 => PointCloudPackedNumericType.Uint8,
                3 => PointCloudPackedNumericType.Int16,
                4 => PointCloudPackedNumericType.Uint16,
                5 => PointCloudPackedNumericType.Int32,
                6 => PointCloudPackedNumericType.Uint32,
                7 => PointCloudPackedNumericType.Float32,
                8 => PointCloudPackedNumericType.Float64,
                _ => PointCloudPackedNumericType.Unknown
            };
            return type != PointCloudPackedNumericType.Unknown;
        }
    }

    /// <summary>Bounded latest-wins handoff from a callback thread to the main thread.</summary>
    public sealed class PackedPointCloudFrameSlot : IDisposable
    {
        private readonly object _sync = new object();
        private PackedPointCloudFrame _latest;
        private bool _disposed;
        private long _received;
        private long _delivered;
        private long _dropped;
        private long _rejected;
        private string _lastError = string.Empty;

        public PackedPointCloudFrameSlot(int capacity = 1)
        {
            if (capacity != 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Only a one-frame latest-wins slot is supported.");
        }

        public PackedPointCloudSlotStats Stats
        {
            get
            {
                lock (_sync)
                    return new PackedPointCloudSlotStats(_received, _delivered, _dropped, _rejected, _lastError);
            }
        }

        public void RecordRejected(string error)
        {
            lock (_sync)
            {
                _rejected++;
                _lastError = error ?? string.Empty;
            }
        }

        public bool Publish(PackedPointCloudFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            lock (_sync)
            {
                if (_disposed)
                {
                    _rejected++;
                    _lastError = "slot is disposed";
                    frame.RecycleData();
                    return false;
                }

                _received++;
                if (_latest != null)
                {
                    _dropped++;
                    _latest.RecycleData();
                }
                _latest = frame;
                return true;
            }
        }

        public bool TryTake(out PackedPointCloudFrame frame)
        {
            lock (_sync)
            {
                frame = _latest;
                _latest = null;
                if (frame == null)
                    return false;
                _delivered++;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _latest?.RecycleData();
                _latest = null;
            }
        }
    }

    /// <summary>Observed counters for one inbound point-cloud slot.</summary>
    public readonly struct PackedPointCloudSlotStats
    {
        public PackedPointCloudSlotStats(long received, long delivered, long dropped, long rejected, string lastError = "")
        {
            Received = received;
            Delivered = delivered;
            Dropped = dropped;
            Rejected = rejected;
            LastError = lastError ?? string.Empty;
        }

        public long Received { get; }
        public long Delivered { get; }
        public long Dropped { get; }
        public long Rejected { get; }
        public string LastError { get; }
    }
}
