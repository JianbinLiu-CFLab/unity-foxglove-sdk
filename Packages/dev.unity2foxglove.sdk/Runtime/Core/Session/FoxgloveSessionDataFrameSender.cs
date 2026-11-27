// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Session
// Purpose: Route one live data message through the selected transport capability.

using System;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>Routes session data through single-frame or segmented transport capabilities.</summary>
    internal readonly struct FoxgloveSessionDataFrameSender
    {
        private readonly IFoxgloveTransport _transport;
        private readonly IPrioritizedFoxgloveTransport _prioritizedTransport;
        private readonly ISegmentedDataFoxgloveTransport _segmentedTransport;
        private readonly IFoxgloveLogger _logger;

        public FoxgloveSessionDataFrameSender(
            IFoxgloveTransport transport,
            IPrioritizedFoxgloveTransport prioritizedTransport,
            IFoxgloveLogger logger)
        {
            _transport = transport;
            _prioritizedTransport = prioritizedTransport;
            _segmentedTransport = transport as ISegmentedDataFoxgloveTransport;
            _logger = logger;
        }

        public bool SupportsSegments => _segmentedTransport != null;

        public void Send(
            uint clientId,
            uint subscriptionId,
            uint channelId,
            string topic,
            string source,
            ulong logTimeNs,
            byte[] payload,
            byte[] sharedBody)
        {
            if (_segmentedTransport != null)
            {
                if (FoxgloveReplayTrace.TryFrame(source, topic, logTimeNs, clientId, subscriptionId, channelId, "data", out var trace))
                    _logger.LogWarning(trace);
                var header = BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, Array.Empty<byte>());
                _segmentedTransport.SendDataBinary(clientId, header, sharedBody);
                return;
            }

            var frame = BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, payload);
            if (_prioritizedTransport != null)
            {
                if (FoxgloveReplayTrace.TryFrame(source, topic, logTimeNs, clientId, subscriptionId, channelId, "data", out var trace))
                    _logger.LogWarning(trace);
                _prioritizedTransport.SendDataBinary(clientId, frame);
                return;
            }

            if (FoxgloveReplayTrace.TryFrame(source, topic, logTimeNs, clientId, subscriptionId, channelId, "fallback-control", out var fallbackTrace))
                _logger.LogWarning(fallbackTrace);
            _transport.SendBinary(clientId, frame);
        }
    }
}
