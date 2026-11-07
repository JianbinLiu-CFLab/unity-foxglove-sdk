using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Unity.FoxgloveSDK.Schemas.Imu
{
    public readonly struct ImuInboundMessage
    {
        public ImuInboundMessage(
            ulong unixNs,
            string frameId,
            Vector3 linearAcceleration,
            Vector3 angularVelocity,
            Quaternion orientation,
            bool hasOrientation,
            bool hasLinearAcceleration = true,
            bool hasAngularVelocity = true)
        {
            UnixNs = unixNs;
            FrameId = frameId ?? string.Empty;
            LinearAcceleration = linearAcceleration;
            AngularVelocity = angularVelocity;
            Orientation = orientation;
            HasOrientation = hasOrientation;
            HasLinearAcceleration = hasLinearAcceleration;
            HasAngularVelocity = hasAngularVelocity;
        }

        public ulong UnixNs { get; }
        public string FrameId { get; }
        public Vector3 LinearAcceleration { get; }
        public Vector3 AngularVelocity { get; }
        public Quaternion Orientation { get; }
        public bool HasOrientation { get; }
        public bool HasLinearAcceleration { get; }
        public bool HasAngularVelocity { get; }
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
            try
            {
                frame = new ImuNativeFrame(
                    message.UnixNs,
                    message.FrameId,
                    message.LinearAcceleration,
                    message.AngularVelocity,
                    message.Orientation,
                    message.HasOrientation,
                    message.HasLinearAcceleration,
                    message.HasAngularVelocity);
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
        private readonly Queue<ImuNativeFrame> _frames;
        private bool _disposed;
        private long _received;
        private long _delivered;
        private long _dropped;
        private long _rejected;
        private string _lastError = string.Empty;

        public ImuNativeFrameSlot(int capacity = 256)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _frames = new Queue<ImuNativeFrame>(capacity);
            Capacity = capacity;
        }

        public int Capacity { get; }

        public int Count
        {
            get
            {
                lock (_sync)
                    return _frames.Count;
            }
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
                if (_frames.Count >= Capacity)
                {
                    _frames.Dequeue();
                    _dropped++;
                }
                _frames.Enqueue(frame);
                return true;
            }
        }

        public bool TryTake(out ImuNativeFrame frame)
        {
            lock (_sync)
            {
                if (_frames.Count == 0)
                {
                    frame = null;
                    return false;
                }
                frame = _frames.Dequeue();
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
                _frames.Clear();
            }
        }
    }

    public sealed class ImuInboundLifecycleState
    {
        private long _generation;

        public long CurrentGeneration
            => Volatile.Read(ref _generation);

        public long BeginGeneration()
            => Interlocked.Increment(ref _generation);

        public bool IsCurrent(long generation)
            => Volatile.Read(ref _generation) == generation;
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
