// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Transport boundary for the experimental Unity-to-ROS2 bridge.

using System;

namespace Unity2Foxglove.Ros2Bridge
{
    /// <summary>Sink for already-serialized ROS 2 bridge frames.</summary>
    public interface IRos2BridgeSink : IDisposable
    {
        /// <summary>Whether the sink currently has an active transport connection.</summary>
        bool IsConnected { get; }

        /// <summary>
        /// Connect or enable delivery for the configured endpoint.
        /// Implementations may use a constructor-configured transport timeout for the
        /// actual connection attempt; <paramref name="timeoutMs"/> must be positive and
        /// is the per-call deadline only for direct transports.
        /// </summary>
        void Connect(string host, int port, int timeoutMs);

        /// <summary>
        /// Send or enqueue an already-serialized bridge frame.
        /// Implementations that enqueue to a worker may validate <paramref name="timeoutMs"/>
        /// while using their configured worker send timeout for the actual socket write.
        /// </summary>
        void Send(Ros2BridgeFrame frame, int timeoutMs);

        /// <summary>Disconnect or disable delivery.</summary>
        void Disconnect();
    }

    /// <summary>
    /// Optional bidirectional capability required for the Phase184 per-publisher
    /// preparation handshake. A legacy send-only sink does not imply readiness.
    /// </summary>
    public interface IRos2BridgePublisherPreparationTransport
    {
        byte[] ExchangePublisherPreparation(byte[] request, int timeoutMs);
    }

    /// <summary>
    /// Internal zero-reencode seam for the Bridge runtime's already-owned
    /// bounded U2R2 wire frames.
    /// </summary>
    internal interface IRos2BridgeRawWireSink
    {
        void SendWire(ReadOnlyMemory<byte> wireBytes, int timeoutMs);
    }
}
