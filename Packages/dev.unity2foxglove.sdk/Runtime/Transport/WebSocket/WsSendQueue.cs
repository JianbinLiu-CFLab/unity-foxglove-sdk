// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/WebSocket
// Purpose: Per-client WebSocket send queue with control/data priority and
// bounded backpressure behavior.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>Priority class used to preserve control frames ahead of data frames.</summary>
    internal enum FramePriority
    {
        Control,
        Data
    }

    /// <summary>Outbound WebSocket frame queued for serialized transmission.</summary>
    internal readonly struct QueuedFrame
    {
        public QueuedFrame(byte opcode, byte[] payload, FramePriority priority)
        {
            Opcode = opcode;
            Payload = payload ?? Array.Empty<byte>();
            Priority = priority;
            SizeBytes = Payload.Length;
        }

        public byte Opcode { get; }
        public byte[] Payload { get; }
        public FramePriority Priority { get; }
        public int SizeBytes { get; }
    }

    /// <summary>Result of attempting to enqueue a WebSocket frame under queue limits.</summary>
    internal readonly struct EnqueueResult
    {
        public EnqueueResult(
            bool accepted,
            bool shouldDisconnect,
            int droppedDataFrames,
            long totalDroppedDataFrames,
            bool shouldLogDataDrop)
        {
            Accepted = accepted;
            ShouldDisconnect = shouldDisconnect;
            DroppedDataFrames = droppedDataFrames;
            TotalDroppedDataFrames = totalDroppedDataFrames;
            ShouldLogDataDrop = shouldLogDataDrop;
        }

        public bool Accepted { get; }
        public bool ShouldDisconnect { get; }
        public int DroppedDataFrames { get; }
        public long TotalDroppedDataFrames { get; }
        public bool ShouldLogDataDrop { get; }
    }

    /// <summary>
    /// Bounded per-client send queue with separate control and data lanes.
    /// Data frames may be dropped under backpressure; control frames are preserved.
    /// </summary>
    internal sealed class WsSendQueue
    {
        private const int SendQueueWaitMs = 100;
        private const long DropLogIntervalFrames = 1000;

        private readonly object _lock = new object();
        private readonly Queue<QueuedFrame> _controlFrames = new Queue<QueuedFrame>();
        private readonly Queue<QueuedFrame> _dataFrames = new Queue<QueuedFrame>();
        private readonly int _maxFrames;
        private readonly int _maxQueuedBytes;
        private int _queuedBytes;
        private bool _completed;
        private long _droppedDataFrames;

        public WsSendQueue(int maxFrames, int maxQueuedBytes)
        {
            _maxFrames = ManagedWebSocketOptions.NormalizeMaxQueuedFrames(maxFrames);
            _maxQueuedBytes = ManagedWebSocketOptions.NormalizeMaxQueuedBytes(maxQueuedBytes);
        }

        public int Count
        {
            get { lock (_lock) return CountLocked; }
        }

        public int QueuedBytes
        {
            get { lock (_lock) return _queuedBytes; }
        }

        public bool IsCompleted
        {
            get { lock (_lock) return _completed; }
        }

        public EnqueueResult Enqueue(QueuedFrame frame)
        {
            lock (_lock)
            {
                if (_completed)
                    return new EnqueueResult(false, false, 0, _droppedDataFrames, false);

                var dropped = 0;
                var droppedBefore = _droppedDataFrames;
                // Preserve control frames by discarding stale data first.
                // If a control frame still cannot fit, the caller disconnects
                // the slow client so one socket cannot block protocol traffic.
                while (!CanFitLocked(frame) && _dataFrames.Count > 0)
                {
                    DropOldestDataLocked();
                    dropped++;
                }

                if (!CanFitLocked(frame))
                {
                    if (frame.Priority == FramePriority.Control)
                    {
                        return new EnqueueResult(
                            false,
                            true,
                            dropped,
                            _droppedDataFrames,
                            ShouldLogDrop(droppedBefore, _droppedDataFrames));
                    }

                    dropped++;
                    _droppedDataFrames++;
                    return new EnqueueResult(
                        false,
                        false,
                        dropped,
                        _droppedDataFrames,
                        ShouldLogDrop(droppedBefore, _droppedDataFrames));
                }

                if (frame.Priority == FramePriority.Control)
                    _controlFrames.Enqueue(frame);
                else
                    _dataFrames.Enqueue(frame);

                _queuedBytes += frame.SizeBytes;
                Monitor.Pulse(_lock);

                return new EnqueueResult(
                    true,
                    false,
                    dropped,
                    _droppedDataFrames,
                    ShouldLogDrop(droppedBefore, _droppedDataFrames));
            }
        }

        public bool TryDequeue(out QueuedFrame frame)
        {
            lock (_lock)
                return TryDequeueLocked(out frame);
        }

        public bool WaitToDequeue(CancellationToken ct, out QueuedFrame frame)
        {
            lock (_lock)
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    if (TryDequeueLocked(out frame))
                        return true;
                    if (_completed)
                        return false;

                    Monitor.Wait(_lock, SendQueueWaitMs);
                }
            }
        }

        public void Complete()
        {
            lock (_lock)
            {
                if (_completed) return;
                _completed = true;
                Monitor.PulseAll(_lock);
            }
        }

        public int ClearDataFrames()
        {
            lock (_lock)
            {
                var dropped = _dataFrames.Count;
                while (_dataFrames.Count > 0)
                {
                    var frame = _dataFrames.Dequeue();
                    _queuedBytes -= frame.SizeBytes;
                }

                _droppedDataFrames += dropped;
                if (CountLocked == 0) Monitor.PulseAll(_lock);
                return dropped;
            }
        }

        public bool WaitUntilEmpty(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            lock (_lock)
            {
                while (CountLocked > 0)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        return false;

                    Monitor.Wait(_lock, remaining);
                }

                return true;
            }
        }

        public long DroppedDataFrames
        {
            get { lock (_lock) return _droppedDataFrames; }
        }

        public long DroppedDataFramesSnapshot
        {
            get { lock (_lock) return _droppedDataFrames; }
        }

        public WsSendQueueSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new WsSendQueueSnapshot
                {
                    QueuedFrames = CountLocked,
                    QueuedControlFrames = _controlFrames.Count,
                    QueuedDataFrames = _dataFrames.Count,
                    QueuedBytes = _queuedBytes,
                    DroppedDataFrames = _droppedDataFrames
                };
            }
        }

        internal struct WsSendQueueSnapshot
        {
            public int QueuedFrames;
            public int QueuedControlFrames;
            public int QueuedDataFrames;
            public int QueuedBytes;
            public long DroppedDataFrames;
        }

        private int CountLocked => _controlFrames.Count + _dataFrames.Count;

        private bool CanFitLocked(QueuedFrame frame)
        {
            return CountLocked + 1 <= _maxFrames
                && _queuedBytes + frame.SizeBytes <= _maxQueuedBytes;
        }

        private bool TryDequeueLocked(out QueuedFrame frame)
        {
            if (_controlFrames.Count > 0)
            {
                frame = _controlFrames.Dequeue();
                _queuedBytes -= frame.SizeBytes;
                if (CountLocked == 0) Monitor.PulseAll(_lock);
                return true;
            }

            if (_dataFrames.Count > 0)
            {
                frame = _dataFrames.Dequeue();
                _queuedBytes -= frame.SizeBytes;
                if (CountLocked == 0) Monitor.PulseAll(_lock);
                return true;
            }

            frame = default;
            return false;
        }

        private void DropOldestDataLocked()
        {
            var dropped = _dataFrames.Dequeue();
            _queuedBytes -= dropped.SizeBytes;
            _droppedDataFrames++;
        }

        private static bool ShouldLogDrop(long before, long after)
        {
            return after > before && (before == 0 || after / DropLogIntervalFrames > before / DropLogIntervalFrames);
        }
    }
}
