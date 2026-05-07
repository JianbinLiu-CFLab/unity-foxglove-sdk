// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validate CSWSH Origin guard — no-Origin clients are accepted,
// disallowed browser origins are rejected with 403, and allowed origins
// pass through to subprotocol negotiation.

using System;
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
            backend.Start("127.0.0.1", 18791);

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol("foxglove.sdk.v1");
                // ClientWebSocket does not send Origin by default — this is correct
                var cts = new CancellationTokenSource(5000);
                ws.ConnectAsync(new Uri("ws://127.0.0.1:18791/"), cts.Token).GetAwaiter().GetResult();
                Assert(ws.State == WebSocketState.Open, "No Origin: connection accepted");
                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
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
            backend.Start("127.0.0.1", 18792);

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol("foxglove.sdk.v1");
                ws.Options.SetRequestHeader("Origin", "http://localhost:3000");
                var cts = new CancellationTokenSource(5000);
                ws.ConnectAsync(new Uri("ws://127.0.0.1:18792/"), cts.Token).GetAwaiter().GetResult();
                Assert(ws.State == WebSocketState.Open, "Allowed Origin: connection accepted");
                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
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
        /// A WebSocket client that sends an Origin header NOT in the
        /// allowlist must be rejected with HTTP 403 Forbidden.
        /// </summary>
        static void TestDisallowedOriginRejected()
        {
            var backend = new ManagedWsBackend();
            // Default allowlist is empty — all browser origins are rejected
            backend.Start("127.0.0.1", 18793);

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol("foxglove.sdk.v1");
                ws.Options.SetRequestHeader("Origin", "https://evil.example.com");
                var cts = new CancellationTokenSource(5000);
                try
                {
                    ws.ConnectAsync(new Uri("ws://127.0.0.1:18793/"), cts.Token).GetAwaiter().GetResult();
                    // If we get here, the server accepted the connection — that's a failure
                    Assert(false, "Disallowed Origin: connection should have been rejected");
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
            backend.Start("127.0.0.1", 18794);

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol("foxglove.sdk.v1");
                ws.Options.SetRequestHeader("Origin", "file://");
                var cts = new CancellationTokenSource(5000);
                ws.ConnectAsync(new Uri("ws://127.0.0.1:18794/"), cts.Token).GetAwaiter().GetResult();
                Assert(ws.State == WebSocketState.Open, "File Origin: connection accepted");
                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
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
    }
}
