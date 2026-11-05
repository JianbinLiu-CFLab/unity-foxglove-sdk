// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Unity.FoxgloveSDK.Schemas.Camera
{
    /// <summary>Schema-neutral CompressedImage message copied from a ROS callback.</summary>
    public sealed class CompressedImageInboundMessage
    {
        public CompressedImageInboundMessage(ulong unixNs, string frameId, byte[] data, string format)
        {
            UnixNs = unixNs;
            FrameId = frameId ?? string.Empty;
            Data = data ?? Array.Empty<byte>();
            Format = format ?? string.Empty;
        }

        public ulong UnixNs { get; }
        public string FrameId { get; }
        public byte[] Data { get; }
        public string Format { get; }
    }

    /// <summary>Maps compressed ROS image data into the SDK camera frame contract.</summary>
    public static class CompressedImageInboundMapper
    {
        public static bool TryMap(
            CompressedImageInboundMessage message,
            long maxBytes,
            out SensorCompressedImageFrame frame,
            out string error)
        {
            frame = null;
            error = string.Empty;
            if (message == null)
                return Reject("message is null", out error);
            if (maxBytes <= 0)
                return Reject("maxBytes must be positive", out error);
            if (message.Data.Length > maxBytes)
                return Reject("CompressedImage data exceeds maxBytes", out error);
            if (!IsSupportedFormat(message.Format))
                return Reject("CompressedImage format must be jpeg, jpg, or png", out error);

            var copiedData = new byte[message.Data.Length];
            Buffer.BlockCopy(message.Data, 0, copiedData, 0, copiedData.Length);
            frame = new SensorCompressedImageFrame(
                message.UnixNs,
                message.FrameId,
                copiedData,
                NormalizeFormat(message.Format));
            return true;
        }

        private static bool IsSupportedFormat(string format)
            => string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase)
               || string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase)
               || string.Equals(format, "png", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeFormat(string format)
            => string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase)
                ? "jpeg"
                : format.ToLowerInvariant();

        private static bool Reject(string message, out string error)
        {
            error = message;
            return false;
        }
    }

    /// <summary>Bounded latest-wins handoff from a callback thread to the main thread.</summary>
    public sealed class SensorCompressedImageFrameSlot : IDisposable
    {
        private readonly object _sync = new object();
        private SensorCompressedImageFrame _latest;
        private bool _disposed;
        private long _received;
        private long _delivered;
        private long _dropped;
        private long _rejected;
        private string _lastError = string.Empty;

        public CompressedImageSlotStats Stats
        {
            get
            {
                lock (_sync)
                    return new CompressedImageSlotStats(_received, _delivered, _dropped, _rejected, _lastError);
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

        public bool Publish(SensorCompressedImageFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            lock (_sync)
            {
                if (_disposed)
                {
                    _rejected++;
                    _lastError = "slot is disposed";
                    return false;
                }

                _received++;
                if (_latest != null)
                    _dropped++;
                _latest = frame;
                return true;
            }
        }

        public bool TryTake(out SensorCompressedImageFrame frame)
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
                _latest = null;
            }
        }
    }

    /// <summary>Observed counters for one compressed-image inbound slot.</summary>
    public readonly struct CompressedImageSlotStats
    {
        public CompressedImageSlotStats(long received, long delivered, long dropped, long rejected, string lastError)
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
