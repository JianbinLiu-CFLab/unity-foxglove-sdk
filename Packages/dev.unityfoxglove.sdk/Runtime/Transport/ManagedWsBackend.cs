using System;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Pure C# WebSocket server backend. The default transport for MVP.
    /// Full implementation deferred to Phase 1 (handshake + serverInfo).
    /// </summary>
    public class ManagedWsBackend : IFoxgloveTransport, IDisposable
    {
        // Phase 1 will use WebSocketSharp-netstandard to implement the server.
        // For now, this is a stub to prove the transport abstraction compiles.

        public bool IsRunning { get; private set; }

        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<uint, string> OnTextReceived;
        public event Action<uint, byte[]> OnBinaryReceived;

        public void Start(string host, int port)
        {
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void BroadcastText(string json)
        {
            // Phase 1: send to all connected clients
        }

        public void BroadcastBinary(byte[] data)
        {
            // Phase 1: send to all connected clients
        }

        public void SendText(uint clientId, string json)
        {
            // Phase 1: send to specific client
        }

        public void SendBinary(uint clientId, byte[] data)
        {
            // Phase 1: send to specific client
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
