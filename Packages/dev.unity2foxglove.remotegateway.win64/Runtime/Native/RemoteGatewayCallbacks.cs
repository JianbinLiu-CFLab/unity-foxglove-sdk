// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using System.Threading;
using AOT;

namespace Unity.FoxgloveSDK.RemoteGateway.Native
{
    internal sealed class RemoteGatewayCallbacks : IDisposable
    {
        private static readonly RemoteGatewayNativeMethods.ConnectionStatusChangedCallback s_onConnectionStatusChanged = OnConnectionStatusChanged;
        private static readonly RemoteGatewayNativeMethods.ChannelCallback s_onSubscribe = OnSubscribe;
        private static readonly RemoteGatewayNativeMethods.ChannelCallback s_onUnsubscribe = OnUnsubscribe;
        private static readonly RemoteGatewayNativeMethods.MessageDataCallback s_onMessageData = OnMessageData;
        private static readonly RemoteGatewayNativeMethods.ChannelCallback s_onClientAdvertise = OnClientAdvertise;
        private static readonly RemoteGatewayNativeMethods.ChannelCallback s_onClientUnadvertise = OnClientUnadvertise;
        private static readonly RemoteGatewayNativeMethods.GetParametersCallback s_onGetParameters = OnGetParameters;
        private static readonly RemoteGatewayNativeMethods.SetParametersCallback s_onSetParameters = OnSetParameters;
        private static readonly RemoteGatewayNativeMethods.ParametersCallback s_onParametersSubscribe = OnParametersSubscribe;
        private static readonly RemoteGatewayNativeMethods.ParametersCallback s_onParametersUnsubscribe = OnParametersUnsubscribe;
        private static readonly RemoteGatewayNativeMethods.ConnectionGraphCallback s_onConnectionGraphSubscribe = OnConnectionGraphSubscribe;
        private static readonly RemoteGatewayNativeMethods.ConnectionGraphCallback s_onConnectionGraphUnsubscribe = OnConnectionGraphUnsubscribe;

        private readonly RemoteGatewayEventQueue _queue;
        private GCHandle _selfHandle;
        private int _disposed;

        internal RemoteGatewayCallbacks(RemoteGatewayEventQueue queue)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _selfHandle = GCHandle.Alloc(this);
        }

        internal RemoteGatewayNativeMethods.FoxgloveGatewayCallbacks CreateNative()
        {
            ThrowIfDisposed();

            return new RemoteGatewayNativeMethods.FoxgloveGatewayCallbacks
            {
                Context = GCHandle.ToIntPtr(_selfHandle),
                OnConnectionStatusChanged = s_onConnectionStatusChanged,
                OnSubscribe = s_onSubscribe,
                OnUnsubscribe = s_onUnsubscribe,
                OnMessageData = s_onMessageData,
                OnClientAdvertise = s_onClientAdvertise,
                OnClientUnadvertise = s_onClientUnadvertise,
                OnGetParameters = s_onGetParameters,
                OnSetParameters = s_onSetParameters,
                OnParametersSubscribe = s_onParametersSubscribe,
                OnParametersUnsubscribe = s_onParametersUnsubscribe,
                OnConnectionGraphSubscribe = s_onConnectionGraphSubscribe,
                OnConnectionGraphUnsubscribe = s_onConnectionGraphUnsubscribe
            };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            // The owner must call blocking GatewayStop first. Native callbacks
            // receive this GCHandle as their context and may still be in flight
            // until the gateway has fully stopped. Keep the handle allocated
            // after disposal so a late native callback can still resolve the
            // managed object and fail closed instead of dereferencing a freed
            // GCHandle context during Editor reload or process shutdown.
        }

        private void Enqueue(RemoteGatewayEvent item)
        {
            if (Volatile.Read(ref _disposed) == 0)
                _queue.TryEnqueue(item);
        }

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.ConnectionStatusChangedCallback))]
        private static void OnConnectionStatusChanged(
            IntPtr context,
            RemoteGatewayNativeMethods.FoxgloveConnectionStatus status)
        {
            var callbacks = TryGetCallbacks(context);
            if (callbacks == null)
                return;

            callbacks.Enqueue(RemoteGatewayEvent.ConnectionStatusChanged(status));
        }

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.ChannelCallback))]
        private static void OnSubscribe(IntPtr context, uint clientId, IntPtr channel)
            => EnqueueClientChannelEvent(context, RemoteGatewayEventKind.ClientSubscribed, clientId);

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.ChannelCallback))]
        private static void OnUnsubscribe(IntPtr context, uint clientId, IntPtr channel)
            => EnqueueClientChannelEvent(context, RemoteGatewayEventKind.ClientUnsubscribed, clientId);

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.MessageDataCallback))]
        private static void OnMessageData(
            IntPtr context,
            uint clientId,
            IntPtr channel,
            IntPtr payload,
            UIntPtr payloadLength)
        {
            var callbacks = TryGetCallbacks(context);
            if (callbacks == null)
                return;

            callbacks.Enqueue(RemoteGatewayEvent.ClientMessage(clientId, payloadLength));
        }

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.ChannelCallback))]
        private static void OnClientAdvertise(IntPtr context, uint clientId, IntPtr channel)
            => EnqueueClientChannelEvent(context, RemoteGatewayEventKind.ClientAdvertised, clientId);

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.ChannelCallback))]
        private static void OnClientUnadvertise(IntPtr context, uint clientId, IntPtr channel)
            => EnqueueClientChannelEvent(context, RemoteGatewayEventKind.ClientUnadvertised, clientId);

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.GetParametersCallback))]
        private static IntPtr OnGetParameters(
            IntPtr context,
            uint clientId,
            IntPtr requestId,
            IntPtr parameterNames,
            UIntPtr parameterNameCount)
        {
            // V1 advertises outbound-only capabilities. Parameter requests are
            // surfaced as diagnostics only; request payloads are intentionally
            // not decoded until remote parameter support is enabled.
            EnqueueClientChannelEvent(context, RemoteGatewayEventKind.ParametersRequested, clientId);
            return IntPtr.Zero;
        }

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.SetParametersCallback))]
        private static IntPtr OnSetParameters(
            IntPtr context,
            uint clientId,
            IntPtr requestId,
            IntPtr parameters)
        {
            // V1 advertises outbound-only capabilities. Parameter mutations are
            // surfaced as diagnostics only; request payloads are intentionally
            // not decoded until remote parameter support is enabled.
            EnqueueClientChannelEvent(context, RemoteGatewayEventKind.ParametersSetRequested, clientId);
            return IntPtr.Zero;
        }

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.ParametersCallback))]
        private static void OnParametersSubscribe(IntPtr context, IntPtr parameterNames, UIntPtr parameterNameCount)
            => EnqueueSimpleEvent(context, RemoteGatewayEventKind.ParametersSubscribed);

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.ParametersCallback))]
        private static void OnParametersUnsubscribe(IntPtr context, IntPtr parameterNames, UIntPtr parameterNameCount)
            => EnqueueSimpleEvent(context, RemoteGatewayEventKind.ParametersUnsubscribed);

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.ConnectionGraphCallback))]
        private static void OnConnectionGraphSubscribe(IntPtr context)
            => EnqueueSimpleEvent(context, RemoteGatewayEventKind.ConnectionGraphSubscribed);

        [MonoPInvokeCallback(typeof(RemoteGatewayNativeMethods.ConnectionGraphCallback))]
        private static void OnConnectionGraphUnsubscribe(IntPtr context)
            => EnqueueSimpleEvent(context, RemoteGatewayEventKind.ConnectionGraphUnsubscribed);

        private static void EnqueueClientChannelEvent(IntPtr context, RemoteGatewayEventKind kind, uint clientId)
        {
            var callbacks = TryGetCallbacks(context);
            if (callbacks == null)
                return;

            callbacks.Enqueue(RemoteGatewayEvent.ClientEvent(kind, clientId));
        }

        private static void EnqueueSimpleEvent(IntPtr context, RemoteGatewayEventKind kind)
        {
            var callbacks = TryGetCallbacks(context);
            if (callbacks == null)
                return;

            callbacks.Enqueue(new RemoteGatewayEvent(kind));
        }

        private static RemoteGatewayCallbacks TryGetCallbacks(IntPtr context)
        {
            if (context == IntPtr.Zero)
                return null;

            return GCHandle.FromIntPtr(context).Target as RemoteGatewayCallbacks;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(RemoteGatewayCallbacks));
        }
    }
}
