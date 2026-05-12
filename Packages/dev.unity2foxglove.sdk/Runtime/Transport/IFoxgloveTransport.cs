// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: Transport abstraction — connection lifecycle, text/binary send and receive, and client connect/disconnect events.

using System;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Abstraction over the Foxglove WebSocket transport.
    /// Handles connection lifecycle and message sending/receiving.
    /// </summary>
    public interface IFoxgloveTransport : IDisposable
    {
        /// <summary>Whether the server is currently listening for connections.</summary>
        bool IsRunning { get; }

        /// <summary>Start listening for Foxglove clients.</summary>
        void Start(string host, int port);

        /// <summary>Stop the server and disconnect all clients.</summary>
        void Stop();

        /// <summary>Send a JSON text message to all connected clients.</summary>
        void BroadcastText(string json);

        /// <summary>Send binary data to all connected clients.</summary>
        void BroadcastBinary(byte[] data);

        /// <summary>Send a JSON text message to a specific client.</summary>
        void SendText(uint clientId, string json);

        /// <summary>Send binary data to a specific client.</summary>
        void SendBinary(uint clientId, byte[] data);

        /// <summary>Invoked when a Foxglove client connects.</summary>
        event Action<uint> OnClientConnected;

        /// <summary>Invoked when a client disconnects.</summary>
        event Action<uint> OnClientDisconnected;

        /// <summary>Invoked when a JSON text message is received from a client.</summary>
        event Action<uint, string> OnTextReceived;

        /// <summary>Invoked when binary data is received from a client.</summary>
        event Action<uint, byte[]> OnBinaryReceived;
    }

    /// <summary>
    /// Optional transport extension for live publish data that may be dropped
    /// under per-client backpressure. Public transport API remains unchanged;
    /// implementations that do not support priority fall back to SendBinary.
    /// </summary>
    internal interface IPrioritizedFoxgloveTransport
    {
        void SendDataBinary(uint clientId, byte[] data);
        void BroadcastDataBinary(byte[] data);
    }

    /// <summary>
    /// Optional transport extension used when playback seeks to an older
    /// timestamp. Implementations may drop queued data frames while preserving
    /// control frames so stale pre-seek data cannot arrive after a reset.
    /// </summary>
    internal interface IReplayResettableFoxgloveTransport
    {
        void ClearDataQueues();
    }
}
