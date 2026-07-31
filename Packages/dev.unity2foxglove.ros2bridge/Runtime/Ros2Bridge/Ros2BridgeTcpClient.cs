// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Loopback TCP sink for the experimental Unity-to-ROS2 bridge.

using System;
using System.Net;
using System.Net.Sockets;

namespace Unity2Foxglove.Ros2Bridge
{
    /// <summary>Sends U2R2 bridge frames to a loopback-only ROS 2 sidecar.</summary>
    public sealed class Ros2BridgeTcpClient :
        IRos2BridgeSink,
        IRos2BridgePublisherPreparationTransport
    {
        private readonly object _gate = new object();
        private TcpClient _client;
        private int _sendTimeoutMs;

        public bool IsConnected
        {
            get
            {
                TcpClient client;
                lock (_gate)
                    client = _client;
                if (client == null || !client.Connected)
                    return false;

                try
                {
                    var socket = client.Client;
                    return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
                }
                catch (SocketException)
                {
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
        }

        public static void ValidateLoopbackHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("ROS 2 bridge host must be non-empty.", nameof(host));
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return;
            if (!IPAddress.TryParse(host, out var address) || !IPAddress.IsLoopback(address))
                throw new ArgumentException("Phase 94 ROS 2 bridge only accepts loopback hosts.", nameof(host));
        }

        public void Connect(string host, int port, int timeoutMs)
        {
            ValidateLoopbackHost(host);
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "ROS 2 bridge port must be in 1..65535.");
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "ROS 2 bridge connect timeout must be positive.");

            DisposeClient();
            var client = new TcpClient();
            try
            {
                lock (_gate)
                    _client = client;
                var task = client.ConnectAsync(host, port);
                if (!task.Wait(timeoutMs))
                    throw new TimeoutException("Timed out connecting to ROS 2 bridge sidecar.");

                client.NoDelay = true;
                client.Client.SendTimeout = timeoutMs;
                lock (_gate)
                    _sendTimeoutMs = timeoutMs;
                client = null;
            }
            catch
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_client, client))
                        _client = null;
                }
                client?.Dispose();
                throw;
            }
        }

        public void Send(Ros2BridgeFrame frame, int timeoutMs)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            TcpClient client;
            lock (_gate)
                client = _client;
            if (client == null || !client.Connected)
                throw new InvalidOperationException("ROS 2 bridge TCP client is not connected.");
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "ROS 2 bridge send timeout must be positive.");

            var socket = client.Client;
            if (_sendTimeoutMs != timeoutMs)
            {
                socket.SendTimeout = timeoutMs;
                lock (_gate)
                    _sendTimeoutMs = timeoutMs;
            }

            var stream = client.GetStream();
            Ros2BridgeFrameWriter.Write(frame, stream);
            stream.Flush();
        }

        public byte[] ExchangePublisherPreparation(byte[] request, int timeoutMs)
        {
            if (request == null || request.Length == 0)
                throw new ArgumentException("Publisher preparation request is empty.", nameof(request));
            TcpClient client;
            lock (_gate)
                client = _client;
            if (client == null || !client.Connected)
                throw new InvalidOperationException("ROS 2 bridge TCP client is not connected.");
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            var socket = client.Client;
            socket.SendTimeout = timeoutMs;
            socket.ReceiveTimeout = timeoutMs;
            _sendTimeoutMs = timeoutMs;
            var stream = client.GetStream();
            stream.Write(request, 0, request.Length);
            stream.Flush();
            return Ros2BridgePublisherPreparationCodec.ReadFrame(stream);
        }

        public void Disconnect()
        {
            TcpClient client;
            lock (_gate)
                client = _client;
            if (client == null)
                return;

            try
            {
                try
                {
                    client.Client.Shutdown(SocketShutdown.Both);
                }
                catch (SocketException)
                {
                    // A failed or peer-closed socket is already waking I/O.
                }
                client.Client.Close();
            }
            catch (ObjectDisposedException)
            {
                // Repeated wake is idempotent; final wrapper disposal is separate.
            }
        }

        public void Dispose() => DisposeClient();

        private void DisposeClient()
        {
            TcpClient client;
            lock (_gate)
            {
                client = _client;
                _client = null;
                _sendTimeoutMs = 0;
            }
            client?.Dispose();
        }
    }
}
