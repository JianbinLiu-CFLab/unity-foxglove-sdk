// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validate CSWSH Origin guard — no-Origin clients are accepted,
// disallowed browser origins are rejected with 403, and allowed origins
// pass through to subprotocol negotiation.

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase28Validation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 28 Tests ---");
            _passCount = 0;
            TestNoOriginAllowed();
            TestAllowedOriginAccepted();
            TestFullWebPageUrlAllowlistNormalizesToOrigin();
            TestDisallowedOriginRejected();
            TestFileOriginAllowed();
            Console.WriteLine($"Phase 28: {_passCount} checks passed.");
        }

        /// <summary>
        /// A WebSocket client that does NOT send an Origin header (like
        /// Foxglove Desktop or CLI tools) must be accepted with a valid
        /// subprotocol.
        /// </summary>
        static void TestNoOriginAllowed()
        {
            var backend = new ManagedWsBackend();
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol("foxglove.sdk.v1");
                // ClientWebSocket does not send Origin by default — this is correct
                var cts = new CancellationTokenSource(5000);
                ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token).GetAwaiter().GetResult();
                Assert(ws.State == WebSocketState.Open, "No Origin: connection accepted");
                CloseClientWebSocketForCleanup(ws);
            }
            catch (WebSocketException ex)
            {
                Assert(false, $"No Origin: unexpected error: {ex.Message}");
            }
            finally
            {
                backend.Dispose();
            }
        }

        /// <summary>
        /// A WebSocket client that sends an Origin header matching the
        /// allowlist must be accepted.
        /// </summary>
        static void TestAllowedOriginAccepted()
        {
            var backend = new ManagedWsBackend();
            backend.AddAllowedOrigin("http://localhost:3000");
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol("foxglove.sdk.v1");
                ws.Options.SetRequestHeader("Origin", "http://localhost:3000");
                var cts = new CancellationTokenSource(5000);
                ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token).GetAwaiter().GetResult();
                Assert(ws.State == WebSocketState.Open, "Allowed Origin: connection accepted");
                CloseClientWebSocketForCleanup(ws);
            }
            catch (WebSocketException ex)
            {
                Assert(false, $"Allowed Origin: unexpected error: {ex.Message}");
            }
            finally
            {
                backend.Dispose();
            }
        }

        /// <summary>
        /// Users often copy the full Foxglove Web address, including project,
        /// layout, and query parameters. The transport must store only the
        /// browser Origin part for CSWSH matching.
        /// </summary>
        static void TestFullWebPageUrlAllowlistNormalizesToOrigin()
        {
            var backend = new ManagedWsBackend();
            backend.AddAllowedOrigin("https://app.foxglove.dev/cf-lab/p/prj_0eKcTwvTR2XowsUv/view?layoutId=lay_0eMDFZm9GZOZLDOT&ds=foxglove-websocket");
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);

            try
            {
                Assert(backend.AllowedOrigins.Contains("https://app.foxglove.dev"),
                    "Full URL allowlist: stored browser origin only");

                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol("foxglove.sdk.v1");
                ws.Options.SetRequestHeader("Origin", "https://app.foxglove.dev");
                var cts = new CancellationTokenSource(5000);
                ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token).GetAwaiter().GetResult();
                Assert(ws.State == WebSocketState.Open, "Full URL allowlist: origin connection accepted");
                CloseClientWebSocketForCleanup(ws);
            }
            catch (WebSocketException ex)
            {
                Assert(false, $"Full URL allowlist: unexpected error: {ex.Message}");
            }
            finally
            {
                backend.Dispose();
            }
        }

        /// <summary>
        /// A WebSocket client that sends an Origin header NOT in the
        /// allowlist must be rejected with HTTP 403 Forbidden.
        /// </summary>
        static void TestDisallowedOriginRejected()
        {
            var backend = new ManagedWsBackend();
            // Default allowlist is empty — all browser origins are rejected
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);

            try
            {
                try
                {
                    var response = SendRawHandshake(port, "https://evil.example.com");
                    Assert(response.StartsWith("HTTP/1.1 403", StringComparison.Ordinal),
                        "Disallowed Origin: raw handshake receives HTTP 403");
                    Assert(response.Contains("403 Forbidden", StringComparison.Ordinal),
                        "Disallowed Origin: response names 403 Forbidden");
                }
                catch (WebSocketException)
                {
                    // Expected — server refuses the connection
                    Assert(true, "Disallowed Origin: connection rejected");
                }
            }
            finally
            {
                backend.Dispose();
            }
        }

        /// <summary>
        /// A WebSocket client that sends <c>Origin: file://</c> (as Foxglove
        /// Desktop Electron does) must be accepted without being on the
        /// allowlist, since file:// origins are local non-browser sources.
        /// </summary>
        static void TestFileOriginAllowed()
        {
            var backend = new ManagedWsBackend();
            // Default allowlist is empty — but file:// should bypass the guard
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol("foxglove.sdk.v1");
                ws.Options.SetRequestHeader("Origin", "file://");
                var cts = new CancellationTokenSource(5000);
                ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token).GetAwaiter().GetResult();
                Assert(ws.State == WebSocketState.Open, "File Origin: connection accepted");
                CloseClientWebSocketForCleanup(ws);
            }
            catch (WebSocketException ex)
            {
                Assert(false, $"File Origin: unexpected error: {ex.Message}");
            }
            finally
            {
                backend.Dispose();
            }
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string SendRawHandshake(int port, string origin)
        {
            using var tcp = new TcpClient();
            tcp.ReceiveTimeout = 5000;
            tcp.SendTimeout = 5000;
            tcp.Connect(IPAddress.Loopback, port);

            using var stream = tcp.GetStream();
            var key = Convert.ToBase64String(new byte[]
            {
                0x31, 0x32, 0x33, 0x34,
                0x35, 0x36, 0x37, 0x38,
                0x39, 0x30, 0x31, 0x32,
                0x33, 0x34, 0x35, 0x36
            });
            var request =
                "GET / HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{port}\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Version: 13\r\n" +
                $"Sec-WebSocket-Key: {key}\r\n" +
                "Sec-WebSocket-Protocol: foxglove.sdk.v1\r\n" +
                $"Origin: {origin}\r\n\r\n";

            var requestBytes = Encoding.ASCII.GetBytes(request);
            stream.Write(requestBytes, 0, requestBytes.Length);

            var responseBytes = new byte[1024];
            var read = stream.Read(responseBytes, 0, responseBytes.Length);
            return Encoding.ASCII.GetString(responseBytes, 0, read);
        }

        private static void CloseClientWebSocketForCleanup(ClientWebSocket ws)
        {
            if (ws == null)
                return;

            try
            {
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (WebSocketException)
            {
                // Some runtimes observe the server-side TCP close before the
                // WebSocket close handshake completes. The Origin assertion has
                // already run, so this is cleanup rather than test behavior.
            }
            catch (InvalidOperationException)
            {
                // Already closing/closed is also cleanup.
            }
            finally
            {
                ws.Dispose();
            }
        }
    }
}
