// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.RemoteGateway.Native;

namespace Unity.FoxgloveSDK.RemoteGateway
{
    internal enum RemoteGatewayEventKind
    {
        ConnectionStatusChanged,
        ClientSubscribed,
        ClientUnsubscribed,
        ClientMessage,
        ClientAdvertised,
        ClientUnadvertised,
        ParametersRequested,
        ParametersSetRequested,
        ParametersSubscribed,
        ParametersUnsubscribed,
        ConnectionGraphSubscribed,
        ConnectionGraphUnsubscribed
    }

    internal struct RemoteGatewayEvent
    {
        internal readonly RemoteGatewayEventKind Kind;
        internal readonly RemoteGatewayNativeMethods.FoxgloveConnectionStatus ConnectionStatus;
        internal readonly uint ClientId;
        internal readonly UIntPtr PayloadLength;

        internal RemoteGatewayEvent(RemoteGatewayEventKind kind)
            : this(kind, default, 0U, UIntPtr.Zero)
        {
        }

        private RemoteGatewayEvent(
            RemoteGatewayEventKind kind,
            RemoteGatewayNativeMethods.FoxgloveConnectionStatus connectionStatus,
            uint clientId,
            UIntPtr payloadLength)
        {
            Kind = kind;
            ConnectionStatus = connectionStatus;
            ClientId = clientId;
            PayloadLength = payloadLength;
        }

        internal static RemoteGatewayEvent ConnectionStatusChanged(RemoteGatewayNativeMethods.FoxgloveConnectionStatus status)
            => new RemoteGatewayEvent(RemoteGatewayEventKind.ConnectionStatusChanged, status, 0U, UIntPtr.Zero);

        internal static RemoteGatewayEvent ClientEvent(RemoteGatewayEventKind kind, uint clientId)
            => new RemoteGatewayEvent(kind, default, clientId, UIntPtr.Zero);

        internal static RemoteGatewayEvent ClientMessage(uint clientId, UIntPtr payloadLength)
            => new RemoteGatewayEvent(RemoteGatewayEventKind.ClientMessage, default, clientId, payloadLength);
    }

    internal sealed class RemoteGatewayEventQueue
    {
        private readonly object _gate = new object();
        private readonly Queue<RemoteGatewayEvent> _events;
        private readonly int _capacity;
        private long _droppedCount;
        private bool _hasLatestConnectionStatus;
        private RemoteGatewayEvent _latestConnectionStatus;

        internal RemoteGatewayEventQueue(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

            _capacity = capacity;
            _events = new Queue<RemoteGatewayEvent>(capacity);
        }

        internal long DroppedCount
        {
            get
            {
                lock (_gate)
                    return _droppedCount;
            }
        }

        internal int Count
        {
            get
            {
                lock (_gate)
                    return _events.Count + (_hasLatestConnectionStatus ? 1 : 0);
            }
        }

        internal bool TryEnqueue(RemoteGatewayEvent item)
        {
            lock (_gate)
            {
                if (item.Kind == RemoteGatewayEventKind.ConnectionStatusChanged)
                {
                    if (!_hasLatestConnectionStatus && _events.Count >= _capacity)
                        DropOldest();

                    var replacedStatus = _hasLatestConnectionStatus;
                    _latestConnectionStatus = item;
                    _hasLatestConnectionStatus = true;
                    if (replacedStatus)
                        _droppedCount++;

                    // Connection status is a latest-value signal, not a FIFO
                    // workload item. Keeping it in a dedicated slot prevents
                    // unsupported callback traffic from evicting health state;
                    // reserve one bounded-queue slot for the latest value.
                    // The status itself was stored, so report acceptance even
                    // when an older FIFO item or status was displaced.
                    return true;
                }

                var fifoCapacity = _capacity - (_hasLatestConnectionStatus ? 1 : 0);
                if (fifoCapacity < 1)
                {
                    // A capacity of one reserves the sole slot for the latest
                    // connection status. Non-status work is intentionally
                    // rejected rather than evicting that health signal.
                    _droppedCount++;
                    return false;
                }

                var droppedOldest = false;
                if (_events.Count >= fifoCapacity)
                {
                    DropOldest();
                    droppedOldest = true;
                }

                _events.Enqueue(item);
                return !droppedOldest;
            }
        }

        internal bool TryDequeue(out RemoteGatewayEvent item)
        {
            lock (_gate)
            {
                if (_hasLatestConnectionStatus)
                {
                    item = _latestConnectionStatus;
                    _latestConnectionStatus = default;
                    _hasLatestConnectionStatus = false;
                    return true;
                }

                if (_events.Count == 0)
                {
                    item = default;
                    return false;
                }

                item = _events.Dequeue();
                return true;
            }
        }

        internal int DrainTo(List<RemoteGatewayEvent> destination, int maxCount)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (maxCount < 1)
                return 0;

            var drained = 0;
            lock (_gate)
            {
                if (_hasLatestConnectionStatus)
                {
                    destination.Add(_latestConnectionStatus);
                    _latestConnectionStatus = default;
                    _hasLatestConnectionStatus = false;
                    drained++;
                }

                while (drained < maxCount && _events.Count > 0)
                {
                    destination.Add(_events.Dequeue());
                    drained++;
                }
            }

            return drained;
        }

        private void DropOldest()
        {
            if (_events.Count > 0)
            {
                _events.Dequeue();
                _droppedCount++;
            }
        }
    }
}
