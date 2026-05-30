// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: No-op transport used when Foxglove WebSocket output is disabled.

using System;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// No-op transport used when the Foxglove WebSocket output is disabled
    /// (Output Mode "Foxglove WebSocket" unchecked / transport mode None). It lets
    /// <see cref="Core.FoxgloveRuntime"/> construct and run so recording, replay,
    /// the ROS2 Bridge, and the R2FU policy flag keep working without serving a
    /// WebSocket. All sends are dropped; no clients ever connect.
    /// </summary>
    public sealed class NullFoxgloveTransport : IFoxgloveTransport
    {
        public bool IsRunning => false;

        public void Start(string host, int port) { }
        public void Stop() { }
        public void BroadcastText(string json) { }
        public void BroadcastBinary(byte[] data) { }
        public void SendText(uint clientId, string json) { }
        public void SendBinary(uint clientId, byte[] data) { }

#pragma warning disable 67 // events are part of the interface but never raised by a no-op transport
        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<uint, string> OnTextReceived;
        public event Action<uint, byte[]> OnBinaryReceived;
#pragma warning restore 67

        public void Dispose() { }
    }
}
