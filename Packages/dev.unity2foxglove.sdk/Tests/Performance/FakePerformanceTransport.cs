// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Performance
// Purpose: Deterministic in-memory fake transport for performance benchmarks.

using System;

namespace Unity.FoxgloveSDK.Performance
{
    public sealed class FakePerformanceTransport : Transport.IFoxgloveTransport, Transport.IPrioritizedFoxgloveTransport
    {
        public bool IsRunning => true;

        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<uint, string> OnTextReceived;
        public event Action<uint, byte[]> OnBinaryReceived;

        public int BinaryCount { get; private set; }
        public int DataBinaryCount { get; private set; }

        public void Start(string host, int port) { }
        public void Stop() { }
        public void SendText(uint clientId, string json) { }
        public void BroadcastText(string json) { }
        public void SendBinary(uint clientId, byte[] data) { BinaryCount++; }
        public void BroadcastBinary(byte[] data) { BinaryCount++; }
        public void SendDataBinary(uint clientId, byte[] data) { DataBinaryCount++; }
        public void BroadcastDataBinary(byte[] data) { DataBinaryCount++; }
        public void Dispose() { }

        public void SimulateConnect(uint clientId, string subProtocol = "foxglove.sdk.v1")
        {
            OnClientConnected?.Invoke(clientId);
        }

        public void SimulateSubscribe(uint clientId, uint subId, uint channelId)
        {
            var json = $"{{\"op\":\"subscribe\",\"subscriptions\":[{{\"id\":{subId},\"channelId\":{channelId}}}]}}";
            OnTextReceived?.Invoke(clientId, json);
        }

        public void ResetCounters()
        {
            BinaryCount = 0;
            DataBinaryCount = 0;
        }
    }
}
