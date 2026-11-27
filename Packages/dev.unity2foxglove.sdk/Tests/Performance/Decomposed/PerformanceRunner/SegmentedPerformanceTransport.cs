// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Performance
// Purpose: Count segmented fanout bytes without materializing a full frame.

using System;

namespace Unity.FoxgloveSDK.Performance
{
    internal sealed class SegmentedPerformanceTransport : Transport.IFoxgloveTransport, Transport.IPrioritizedFoxgloveTransport, Transport.ISegmentedDataFoxgloveTransport
    {
        public bool IsRunning => true;

        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<uint, string> OnTextReceived;
        public event Action<uint, byte[]> OnBinaryReceived;

        public int DataBinaryCount { get; private set; }
        public long DataBinaryBytes { get; private set; }

        public void Start(string host, int port) { }
        public void Stop() { }
        public void SendText(uint clientId, string json) { }
        public void BroadcastText(string json) { }
        public void SendBinary(uint clientId, byte[] data) { }
        public void BroadcastBinary(byte[] data) { }
        public void SendDataBinary(uint clientId, byte[] data)
        {
            DataBinaryCount++;
            DataBinaryBytes += data?.LongLength ?? 0;
        }

        public void SendDataBinary(uint clientId, byte[] header, byte[] sharedBody)
        {
            DataBinaryCount++;
            DataBinaryBytes += (header?.LongLength ?? 0) + (sharedBody?.LongLength ?? 0);
        }

        public void BroadcastDataBinary(byte[] data)
        {
            DataBinaryCount++;
            DataBinaryBytes += data?.LongLength ?? 0;
        }

        public void Dispose() { }

        public void Plan199ConnectClient(uint clientId) => OnClientConnected?.Invoke(clientId);

        public void Plan199ReceiveSubscribe(uint clientId, uint subscriptionId)
        {
            OnTextReceived?.Invoke(
                clientId,
                $"{{\"op\":\"subscribe\",\"subscriptions\":[{{\"id\":{subscriptionId},\"channelId\":1}}]}}");
        }

        public void Plan199ResetCounters()
        {
            DataBinaryCount = 0;
            DataBinaryBytes = 0;
        }
    }
}
