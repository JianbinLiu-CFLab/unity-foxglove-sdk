// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 134-6 managed WebSocket active-client admission bounds.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_6Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-6: Managed WebSocket transport backpressure hardening ===");
            _passed = 0;

            VerifyManagedWebSocketClientBudgetSurface();
            VerifyManagedBackendRejectsOverBudgetClient();

            Console.WriteLine($"Phase 134-6: {_passed} checks passed.");
        }

        private static void VerifyManagedWebSocketClientBudgetSurface()
        {
            Check(ManagedWebSocketOptions.DefaultMaxClients > 0,
                "134-6A-1: managed WebSocket options define a positive default max-client budget");
            Check(ManagedWebSocketOptions.NormalizeMaxClients(0) == ManagedWebSocketOptions.DefaultMaxClients,
                "134-6A-2: zero max-client config normalizes to a usable default");
            Check(ManagedWebSocketOptions.NormalizeMaxClients(2) == 2,
                "134-6A-3: explicit positive max-client config is preserved");

            using var backend = new ManagedWsBackend(new ManagedWebSocketOptions { MaxClients = 2 });
            var snap = backend.GetStatsSnapshot();
            Check(snap.MaxClients == 2, "134-6A-4: transport stats expose configured max-client budget");
            Check(snap.TotalRejectedClients == 0, "134-6A-5: transport stats expose rejected-client counter");

            var backendSource = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs");
            Check(backendSource.Contains("private readonly object _clientAdmissionLock", StringComparison.Ordinal)
                  && backendSource.Contains("private bool TryRegisterClient", StringComparison.Ordinal),
                "134-6A-6: backend registers active clients through a bounded admission gate");
            var handshakeSource = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsHandshakeHandler.cs");
            Check(backendSource.Contains("HasClientCapacityForHandshake", StringComparison.Ordinal)
                  && backendSource.Contains("_handshakeHandler.Handshake(stream, HasClientCapacityForHandshake)", StringComparison.Ordinal)
                  && handshakeSource.Contains("HTTP/1.1 503 Service Unavailable", StringComparison.Ordinal),
                "134-6A-7: over-budget handshakes receive an explicit capacity response");
        }

        private static void VerifyManagedBackendRejectsOverBudgetClient()
        {
            var port = GetFreeTcpPort();
            var logger = new CaptureLogger();
            using var backend = new ManagedWsBackend(new ManagedWebSocketOptions { MaxClients = 1 }, logger);
            TcpClient firstClient = null;

            try
            {
                backend.Start("127.0.0.1", port);
                firstClient = ConnectRawWebSocketClient(port);
                Check(WaitFor(() => backend.GetStatsSnapshot().ActiveClientCount == 1, 2000),
                    "134-6B-1: first client becomes active");

                var rejectedResponse = SendRawHandshake(port);
                Check(!rejectedResponse.StartsWith("HTTP/1.1 101", StringComparison.Ordinal),
                    "134-6B-2: second client is not upgraded when max-client budget is full");
                Check(rejectedResponse.StartsWith("HTTP/1.1 503", StringComparison.Ordinal),
                    "134-6B-3: second client receives capacity rejection response");

                var snap = backend.GetStatsSnapshot();
                Check(snap.ActiveClientCount == 1,
                    "134-6B-4: rejected client does not increase active client count");
                Check(snap.TotalAcceptedClients == 1,
                    "134-6B-5: rejected client does not increment accepted-client count");
                Check(snap.TotalRejectedClients >= 1,
                    "134-6B-6: rejected-client counter increments");
                Check(logger.WarningText.Contains("active client limit", StringComparison.Ordinal),
                    "134-6B-7: capacity rejection is logged with context");
            }
            finally
            {
                try { firstClient?.Close(); } catch { }
                try { firstClient?.Dispose(); } catch { }
                backend.Stop();
            }
        }

        private static TcpClient ConnectRawWebSocketClient(int port)
        {
            var client = new TcpClient
            {
                SendTimeout = 2000,
                ReceiveTimeout = 2000
            };
            client.Connect("127.0.0.1", port);
            var stream = client.GetStream();
            WriteHandshake(stream, port);
            var response = ReadHttpResponse(stream);
            if (!response.StartsWith("HTTP/1.1 101", StringComparison.Ordinal))
                throw new Exception("Raw WebSocket handshake failed: " + response);
            return client;
        }

        private static string SendRawHandshake(int port)
        {
            using var client = new TcpClient
            {
                SendTimeout = 2000,
                ReceiveTimeout = 2000
            };
            client.Connect("127.0.0.1", port);
            var stream = client.GetStream();
            WriteHandshake(stream, port);
            return ReadHttpResponse(stream);
        }

        private static void WriteHandshake(NetworkStream stream, int port)
        {
            var key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            var request =
                "GET / HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{port}\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Key: {key}\r\n" +
                "Sec-WebSocket-Version: 13\r\n" +
                $"Sec-WebSocket-Protocol: {Subprotocol.SdkV1}\r\n" +
                "\r\n";

            var bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ReadHttpResponse(NetworkStream stream)
        {
            var buffer = new byte[4096];
            var offset = 0;
            while (offset < buffer.Length)
            {
                var b = stream.ReadByte();
                if (b < 0)
                    break;

                buffer[offset++] = (byte)b;
                if (offset >= 4
                    && buffer[offset - 4] == '\r'
                    && buffer[offset - 3] == '\n'
                    && buffer[offset - 2] == '\r'
                    && buffer[offset - 1] == '\n')
                    break;
            }

            return Encoding.ASCII.GetString(buffer, 0, offset);
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;

                Thread.Sleep(10);
            }

            return condition();
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath));
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git"))
                    || Directory.Exists(Path.Combine(dir, "Packages")))
                    return dir;

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private sealed class CaptureLogger : IFoxgloveLogger
        {
            public string WarningText { get; private set; } = string.Empty;

            public void LogWarning(string message)
            {
                WarningText += message + "\n";
            }

            public void LogError(string message) { }
        }
    }
}
