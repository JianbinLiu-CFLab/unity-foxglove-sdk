// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates IRuntimeContext indirection, recording/replay controllers, McapBinaryReader bounds checks, client publish auto-increment, seek boundary behavior, coordinate roundtrip, and handler non-accumulation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{

    /// <summary>
    /// Fake transport for Phase 13 that tracks <c>IsRunning</c> state.
    /// </summary>
    public sealed partial class Phase13FakeTransport : IFoxgloveTransport, IPrioritizedFoxgloveTransport, IReplayResettableFoxgloveTransport, IClientDataQueueResettableFoxgloveTransport, IFoxgloveTransportStatsProvider
    {
        private readonly object _gate = new object();
        public readonly List<string> SentText = new List<string>();
        private readonly Dictionary<uint, List<byte[]>> _sentBinary = new Dictionary<uint, List<byte[]>>();
        public int ClearDataQueuesCount { get; private set; }
        public int ControlBinaryCount { get; private set; }
        public int DataBinaryCount { get; private set; }
        public int ControlBroadcastBinaryCount { get; private set; }
        public int DataBroadcastBinaryCount { get; private set; }
        public bool StatsSupported { get; set; } = true;
        public int SimulatedQueuedFrames { get; set; }
        public int SimulatedQueuedBytes { get; set; }
        public bool IsRunning { get; private set; }
        public void Start(string host, int port) { IsRunning = true; }
        public void Stop() { IsRunning = false; }
        public void Dispose() { Stop(); }
        public void BroadcastText(string json) { lock (_gate) SentText.Add(json); }
        public void BroadcastBinary(byte[] data)
        {
            List<uint> clientIds;
            lock (_gate)
            {
                ControlBroadcastBinaryCount++;
                clientIds = new List<uint>(_sentBinary.Keys);
            }
            foreach (var clientId in clientIds)
                SendBinary(clientId, data);
        }
        public void SendText(uint clientId, string json) { lock (_gate) SentText.Add(json); }
        public void SendBinary(uint clientId, byte[] data)
        {
            lock (_gate)
            {
                ControlBinaryCount++;
                if (!_sentBinary.TryGetValue(clientId, out var frames))
                {
                    frames = new List<byte[]>();
                    _sentBinary[clientId] = frames;
                }
                frames.Add(data);
            }
        }
        public void BroadcastDataBinary(byte[] data)
        {
            List<uint> clientIds;
            lock (_gate)
            {
                DataBroadcastBinaryCount++;
                clientIds = new List<uint>(_sentBinary.Keys);
            }
            foreach (var clientId in clientIds)
                SendDataBinary(clientId, data);
        }
        public void SendDataBinary(uint clientId, byte[] data)
        {
            lock (_gate)
            {
                DataBinaryCount++;
                if (!_sentBinary.TryGetValue(clientId, out var frames))
                {
                    frames = new List<byte[]>();
                    _sentBinary[clientId] = frames;
                }
                frames.Add(data);
            }
        }
        public IReadOnlyList<byte[]> SentBinaryFrames(uint clientId)
        {
            lock (_gate)
                return _sentBinary.TryGetValue(clientId, out var frames) ? frames.ToArray() : Array.Empty<byte[]>();
        }
        public void ClearBinary(uint clientId)
        {
            lock (_gate)
            {
                if (_sentBinary.TryGetValue(clientId, out var frames))
                    frames.Clear();
            }
        }
        public void ResetPriorityCounters()
        {
            lock (_gate)
            {
                ControlBinaryCount = 0;
                DataBinaryCount = 0;
                ControlBroadcastBinaryCount = 0;
                DataBroadcastBinaryCount = 0;
            }
        }
        public void SimulateConnect(uint clientId)
        {
            lock (_gate)
            {
                if (!_sentBinary.ContainsKey(clientId))
                    _sentBinary[clientId] = new List<byte[]>();
            }
            OnClientConnected?.Invoke(clientId);
        }
        public void ClearDataQueues() { lock (_gate) ClearDataQueuesCount++; }
        public void ClearDataQueue(uint clientId) { ClearBinary(clientId); }
        public TransportStatsSnapshot GetStatsSnapshot()
        {
            if (!StatsSupported)
                return TransportStatsSnapshot.Unsupported;

            var clients = new List<TransportClientStats>();
            lock (_gate)
            {
                foreach (var clientId in _sentBinary.Keys)
                {
                    clients.Add(new TransportClientStats
                    {
                        ClientId = clientId,
                        QueuedFrames = SimulatedQueuedFrames,
                        QueuedBytes = SimulatedQueuedBytes
                    });
                }
            }

            return new TransportStatsSnapshot
            {
                Supported = true,
                IsRunning = IsRunning,
                ActiveClientCount = clients.Count,
                Clients = clients
            };
        }
        public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        public void SimulateBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<uint, string> OnTextReceived;
        public event Action<uint, byte[]> OnBinaryReceived;
    }
}
