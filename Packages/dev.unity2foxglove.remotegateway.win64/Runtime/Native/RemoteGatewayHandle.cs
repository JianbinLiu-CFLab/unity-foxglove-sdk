// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Unity.FoxgloveSDK.RemoteGateway.Native
{
    internal sealed class RemoteGatewayHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly Func<RemoteGatewayHandle, RemoteGatewayNativeMethods.FoxgloveConnectionStatus> _connectionStatus;
        private readonly Func<RemoteGatewayHandle, ulong> _sinkId;
        private readonly Func<IntPtr, RemoteGatewayNativeMethods.FoxgloveError> _stop;
        internal RemoteGatewayHandle()
            : base(true)
        {
            _connectionStatus = RemoteGatewayNativeMethods.GatewayConnectionStatus;
            _sinkId = RemoteGatewayNativeMethods.GatewaySinkId;
            _stop = RemoteGatewayNativeMethods.GatewayStop;
        }

        internal RemoteGatewayHandle(IntPtr nativeHandle)
            : this(
                nativeHandle,
                RemoteGatewayNativeMethods.GatewayConnectionStatus,
                RemoteGatewayNativeMethods.GatewaySinkId,
                RemoteGatewayNativeMethods.GatewayStop)
        {
        }

        internal RemoteGatewayHandle(
            IntPtr nativeHandle,
            Func<RemoteGatewayHandle, RemoteGatewayNativeMethods.FoxgloveConnectionStatus> connectionStatus,
            Func<RemoteGatewayHandle, ulong> sinkId,
            Func<IntPtr, RemoteGatewayNativeMethods.FoxgloveError> stop)
            : base(true)
        {
            _connectionStatus = connectionStatus ?? throw new ArgumentNullException(nameof(connectionStatus));
            _sinkId = sinkId ?? throw new ArgumentNullException(nameof(sinkId));
            _stop = stop ?? throw new ArgumentNullException(nameof(stop));
            SetHandle(nativeHandle);
        }

        internal RemoteGatewayNativeMethods.FoxgloveConnectionStatus ConnectionStatus
        {
            get
            {
                if (IsClosed || IsInvalid)
                {
                    return RemoteGatewayNativeMethods.FoxgloveConnectionStatus.Shutdown;
                }

                try
                {
                    return _connectionStatus(this);
                }
                catch (ObjectDisposedException)
                {
                    return RemoteGatewayNativeMethods.FoxgloveConnectionStatus.Shutdown;
                }
            }
        }

        internal ulong SinkId
        {
            get
            {
                if (IsClosed || IsInvalid)
                {
                    return 0UL;
                }

                try
                {
                    return _sinkId(this);
                }
                catch (ObjectDisposedException)
                {
                    return 0UL;
                }
            }
        }

        protected override bool ReleaseHandle()
        {
            var result = _stop(handle);
            handle = IntPtr.Zero;
            return result == RemoteGatewayNativeMethods.FoxgloveError.Ok
                   || result == RemoteGatewayNativeMethods.FoxgloveError.SinkClosed;
        }
    }
}
