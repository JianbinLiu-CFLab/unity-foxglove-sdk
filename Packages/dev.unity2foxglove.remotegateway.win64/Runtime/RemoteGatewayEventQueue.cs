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

        internal static RemoteGatewayEvent ConnectionStatus(RemoteGatewayNativeMethods.FoxgloveConnectionStatus status)
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
                    return _events.Count;
            }
        }

        internal bool TryEnqueue(RemoteGatewayEvent item)
        {
            lock (_gate)
            {
                if (_events.Count >= _capacity)
                    DropOldest();

                _events.Enqueue(item);
                return true;
            }
        }

        internal bool TryDequeue(out RemoteGatewayEvent item)
        {
            lock (_gate)
            {
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
