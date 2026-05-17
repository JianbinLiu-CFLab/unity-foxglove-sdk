// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Loopback TCP sink for the experimental Unity-to-ROS2 bridge.

using System;
using System.Net;
using System.Net.Sockets;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Sends U2R2 bridge frames to a localhost ROS 2 sidecar.</summary>
    public sealed class Ros2BridgeTcpClient : IRos2BridgeSink
    {
        private TcpClient _client;

        public bool IsConnected => _client != null && _client.Connected;

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

            Disconnect();
            var client = new TcpClient();
            var task = client.ConnectAsync(host, port);
            if (!task.Wait(timeoutMs))
            {
                client.Dispose();
                throw new TimeoutException("Timed out connecting to ROS 2 bridge sidecar.");
            }

            try
            {
                task.GetAwaiter().GetResult();
            }
            catch
            {
                client.Dispose();
                throw;
            }

            client.NoDelay = true;
            _client = client;
        }

        public void Send(Ros2BridgeFrame frame, int timeoutMs)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (_client == null || !_client.Connected)
                throw new InvalidOperationException("ROS 2 bridge TCP client is not connected.");
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "ROS 2 bridge send timeout must be positive.");

            var bytes = Ros2BridgeFrameWriter.Write(frame);
            var socket = _client.Client;
            socket.SendTimeout = timeoutMs;
            var offset = 0;
            while (offset < bytes.Length)
            {
                var sent = socket.Send(bytes, offset, bytes.Length - offset, SocketFlags.None);
                if (sent <= 0)
                    throw new InvalidOperationException("ROS 2 bridge socket closed during send.");
                offset += sent;
            }
        }

        public void Disconnect()
        {
            if (_client == null)
                return;

            try
            {
                _client.Close();
            }
            finally
            {
                _client.Dispose();
                _client = null;
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
