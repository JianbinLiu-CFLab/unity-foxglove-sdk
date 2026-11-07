using System;
using System.Numerics;

namespace Unity.FoxgloveSDK.Schemas.Imu
{
    public sealed class ImuInboundMessage
    {
        public ImuInboundMessage(
            ulong unixNs,
            string frameId,
            Vector3 linearAcceleration,
            Vector3 angularVelocity,
            Quaternion orientation,
            bool hasOrientation)
        {
            UnixNs = unixNs;
            FrameId = frameId ?? string.Empty;
            LinearAcceleration = linearAcceleration;
            AngularVelocity = angularVelocity;
            Orientation = orientation;
            HasOrientation = hasOrientation;
        }

        public ulong UnixNs { get; }
        public string FrameId { get; }
        public Vector3 LinearAcceleration { get; }
        public Vector3 AngularVelocity { get; }
        public Quaternion Orientation { get; }
        public bool HasOrientation { get; }
    }

    public static class ImuInboundMapper
    {
        public static bool TryMap(
            ImuInboundMessage message,
            out ImuNativeFrame frame,
            out string error)
        {
            frame = null;
            error = string.Empty;
            if (message == null)
            {
                error = "message is null";
                return false;
            }

            try
            {
                frame = new ImuNativeFrame(
                    message.UnixNs,
                    message.FrameId,
                    message.LinearAcceleration,
                    message.AngularVelocity,
                    message.Orientation,
                    message.HasOrientation);
                return true;
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }

    public sealed class ImuNativeFrameSlot : IDisposable
    {
        private readonly object _sync = new object();
        private ImuNativeFrame _latest;
        private bool _disposed;
        private long _received;
        private long _delivered;
        private long _dropped;
        private long _rejected;
        private string _lastError = string.Empty;

        public ImuNativeFrameSlot(int capacity = 1)
        {
            if (capacity != 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Only a one-frame latest-wins slot is supported.");
        }

        public ImuNativeFrameSlotStats Stats
        {
            get
            {
                lock (_sync)
                    return new ImuNativeFrameSlotStats(_received, _delivered, _dropped, _rejected, _lastError);
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

        public bool Publish(ImuNativeFrame frame)
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

        public bool TryTake(out ImuNativeFrame frame)
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

    public readonly struct ImuNativeFrameSlotStats
    {
        public ImuNativeFrameSlotStats(
            long received,
            long delivered,
            long dropped,
            long rejected,
            string lastError = "")
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
