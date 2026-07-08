// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Unity.FoxgloveSDK.RemoteGateway.Native
{
    internal sealed class RemoteGatewayHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal RemoteGatewayHandle()
            : base(true)
        {
        }

        internal RemoteGatewayHandle(IntPtr nativeHandle)
            : base(true)
        {
            SetHandle(nativeHandle);
        }

        internal RemoteGatewayNativeMethods.FoxgloveConnectionStatus ConnectionStatus
            => IsInvalid
                ? RemoteGatewayNativeMethods.FoxgloveConnectionStatus.Shutdown
                : RemoteGatewayNativeMethods.GatewayConnectionStatus(handle);

        internal ulong SinkId
            => IsInvalid ? 0UL : RemoteGatewayNativeMethods.GatewaySinkId(handle);

        protected override bool ReleaseHandle()
        {
            var result = RemoteGatewayNativeMethods.GatewayStop(handle);
            handle = IntPtr.Zero;
            return result == RemoteGatewayNativeMethods.FoxgloveError.Ok
                   || result == RemoteGatewayNativeMethods.FoxgloveError.SinkClosed;
        }
    }
}
