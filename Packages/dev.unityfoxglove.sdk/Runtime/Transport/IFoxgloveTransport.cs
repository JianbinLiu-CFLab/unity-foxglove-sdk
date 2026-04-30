using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Abstraction over the Foxglove WebSocket transport.
    /// Handles connection lifecycle and message sending/receiving.
    /// </summary>
    public interface IFoxgloveTransport
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
}
