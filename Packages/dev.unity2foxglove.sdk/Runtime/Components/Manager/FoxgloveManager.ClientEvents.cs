// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Main-thread delivery queue for Foxglove client transport events.

using System.Collections.Generic;
using System.Threading;
using Unity.FoxgloveSDK.Core;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private const int MaxQueuedClientLifecycleEvents = 4096;
        private const int MaxQueuedClientEvents = 4096;
        private const long MaxQueuedClientEventPayloadBytes = 16L * 1024L * 1024L;
        private const long ClientEventOverflowWarningIntervalTicks = 5L * 1000L * 1000L * 10L;

        private readonly BoundedEventQueue<ClientEvent> _clientLifecycleEvents =
            new(MaxQueuedClientLifecycleEvents, 0, MeasureClientEventPayloadBytes);
        private readonly BoundedEventQueue<ClientEvent> _clientMessageEvents =
            new(MaxQueuedClientEvents, MaxQueuedClientEventPayloadBytes, MeasureClientEventPayloadBytes);
        private readonly List<ClientEvent> _clientEventDrainScratch = new();
        private readonly ClientEventDispatchState _clientEventDispatchState = new();

        /// <summary>
        /// Queues a transport connect event for main-thread delivery.
        /// </summary>
        /// <param name="id">Connected Foxglove client identifier.</param>
        private void EnqueueConnect(uint id) =>
            EnqueueClientLifecycleEvent(ClientEvent.Connect(
                Volatile.Read(ref _connectionState.ChannelSessionGeneration), id));

        /// <summary>
        /// Queues a transport disconnect event for main-thread delivery.
        /// </summary>
        /// <param name="id">Disconnected Foxglove client identifier.</param>
        private void EnqueueDisconnect(uint id) =>
            EnqueueClientLifecycleEvent(ClientEvent.Disconnect(
                Volatile.Read(ref _connectionState.ChannelSessionGeneration), id));

        private void EnqueueClientLifecycleEvent(ClientEvent evt)
        {
            if (_clientLifecycleEvents.TryEnqueue(evt, out var overflow))
            {
                return;
            }

            WarnClientEventQueueOverflow(evt, overflow);
        }

        private void EnqueueClientMessageEvent(ClientEvent evt)
        {
            if (_clientMessageEvents.TryEnqueue(evt, out var overflow))
            {
                return;
            }

            WarnClientEventQueueOverflow(evt, overflow);
        }

        private void WarnClientEventQueueOverflow(ClientEvent evt, BoundedEventQueueOverflow overflow)
        {
            var nowTicks = System.DateTime.UtcNow.Ticks;
            if (!WarningDebouncer.TryUpdateCooldown(
                    ref _warningDebounceState.LastClientEventOverflowWarningTicks,
                    nowTicks,
                    ClientEventOverflowWarningIntervalTicks))
            {
                return;
            }

            var eventKind = evt.IsMessage ? "message" : evt.IsConnect ? "connect" : "disconnect";
            Debug.LogWarning(
                "[Foxglove] Dropped client " + eventKind
                + " event because the Unity main-thread event queue is full. queuedEvents="
                + overflow.QueuedFrames
                + " queuedPayloadBytes="
                + overflow.QueuedBytes
                + " rejectedPayloadBytes="
                + overflow.RejectedBytes
                + " droppedEvents="
                + overflow.DroppedCount
                + " droppedPayloadBytes="
                + overflow.DroppedBytes
                + " limits="
                + (evt.IsMessage ? MaxQueuedClientEvents : MaxQueuedClientLifecycleEvents)
                + "/"
                + (evt.IsMessage ? MaxQueuedClientEventPayloadBytes : 0)
                + " bytes.");
        }

        private static int MeasureClientEventPayloadBytes(ClientEvent evt)
        {
            return evt.IsMessage ? evt.Payload?.Length ?? 0 : 0;
        }

        private void DrainClientEventQueue(BoundedEventQueue<ClientEvent> queue)
        {
            var generation = Volatile.Read(ref _connectionState.ChannelSessionGeneration);
            System.Func<bool> isLive = () =>
                Volatile.Read(ref _connectionState.ChannelSessionGeneration)
                == generation;
            queue.DrainTo(_clientEventDrainScratch);
            try
            {
                foreach (var evt in _clientEventDrainScratch)
                {
                    // A transport callback may already be in flight when the
                    // manager detaches it during Stop.  Its stamped event must
                    // never be delivered to the next session epoch.
                    if (!ClientEventGenerationGate.IsCurrent(evt.Generation, generation))
                        continue;

                    bool dispatched;
                    if (evt.IsMessage)
                    {
                        dispatched = _clientEventDispatchState.InvokeIfLive(
                            isLive,
                            () => _clientEventDispatchState.Invoke(
                                OnClientMessage,
                                evt.ClientId,
                                evt.ChannelId,
                                evt.Topic,
                                evt.Payload,
                                WarnClientEventSubscriberFailure),
                            () => _clientEventDispatchState.Invoke(
                                OnClientMessageWithEncoding,
                                evt.ClientId,
                                evt.ChannelId,
                                evt.Topic,
                                evt.Encoding,
                                evt.Payload,
                                WarnClientEventSubscriberFailure));
                    }
                    else if (evt.IsConnect)
                    {
                        dispatched = _clientEventDispatchState.InvokeIfLive(
                            isLive,
                            () => _clientEventDispatchState.Invoke(
                                OnClientConnected,
                                evt.ClientId,
                                WarnClientEventSubscriberFailure),
                            null);
                    }
                    else
                    {
                        dispatched = _clientEventDispatchState.InvokeIfLive(
                            isLive,
                            () => _clientEventDispatchState.Invoke(
                                OnClientDisconnected,
                                evt.ClientId,
                                WarnClientEventSubscriberFailure),
                            null);
                    }

                    if (!dispatched)
                        break;
                }
            }
            finally
            {
                _clientEventDrainScratch.Clear();
            }
        }

        private static void WarnClientEventSubscriberFailure(
            System.Exception exception)
        {
            Debug.LogWarning(
                "[Foxglove] Client event subscriber threw '"
                + exception.GetType().FullName
                + "'; remaining subscribers and queued events continue.");
        }

        private void ClearClientEvents()
        {
            _clientLifecycleEvents.Clear();
            _clientMessageEvents.Clear();
        }
    }

    /// <summary>
    /// Transport event queued for main-thread delivery.
    /// </summary>
    internal readonly struct ClientEvent
    {
        private ClientEvent(ulong generation, uint clientId, uint channelId, string topic, string encoding, byte[] payload, bool isConnect, bool isMessage)
        {
            Generation = generation;
            ClientId = clientId;
            ChannelId = channelId;
            Topic = topic;
            Encoding = encoding;
            Payload = payload;
            IsConnect = isConnect;
            IsMessage = isMessage;
        }

        public static ClientEvent Connect(uint clientId) =>
            Connect(0, clientId);

        public static ClientEvent Connect(ulong generation, uint clientId) =>
            new(generation, clientId, 0, null, null, null, isConnect: true, isMessage: false);

        public static ClientEvent Disconnect(uint clientId) =>
            Disconnect(0, clientId);

        public static ClientEvent Disconnect(ulong generation, uint clientId) =>
            new(generation, clientId, 0, null, null, null, isConnect: false, isMessage: false);

        public static ClientEvent Message(uint clientId, uint channelId, string topic, string encoding, byte[] payload) =>
            Message(0, clientId, channelId, topic, encoding, payload);

        public static ClientEvent Message(ulong generation, uint clientId, uint channelId, string topic, string encoding, byte[] payload) =>
            new(generation, clientId, channelId, topic, encoding, payload, isConnect: false, isMessage: true);

        /// <summary>
        /// Session generation captured by the producer callback.  Zero is
        /// reserved for legacy test/factory callers; live Manager callbacks
        /// always use the non-zero generation assigned at StartServer.
        /// </summary>
        public readonly ulong Generation;

        /// <summary>
        /// Foxglove client identifier associated with the event.
        /// </summary>
        public readonly uint ClientId;

        /// <summary>
        /// Client-advertised channel identifier for message events.
        /// </summary>
        public readonly uint ChannelId;

        /// <summary>
        /// Client-advertised topic name for message events.
        /// </summary>
        public readonly string Topic;

        /// <summary>
        /// Encoding advertised by the client channel that carried a message event.
        /// </summary>
        public readonly string Encoding;

        /// <summary>
        /// Client-published payload bytes for message events.
        /// </summary>
        public readonly byte[] Payload;

        /// <summary>
        /// True when the event represents a client connection.
        /// </summary>
        public readonly bool IsConnect;

        /// <summary>
        /// True when the event represents a client-published message.
        /// </summary>
        public readonly bool IsMessage;
    }
}
