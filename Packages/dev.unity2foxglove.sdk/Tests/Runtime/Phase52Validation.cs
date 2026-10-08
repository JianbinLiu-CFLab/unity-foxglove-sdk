// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validate Phase 52 Unity-native WSS/TLS transport extension,
// shared stream-based WebSocket core, token gating, certificate
// distribution, and secure/plain transport regressions.

using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Reflection;
using System.Threading;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase52Validation.
    /// </summary>
    public static partial class Phase52Validation
    {
        private const int TestTimeoutMs = 5000;
        private const int CertificateDistributorIdleTimeoutTestMs = 100;
        private const int QuietLogWaitMs = 250;
        private const string ValidToken = "phase52-secret";
        private const string WrongToken = "phase52-wrong";
        private static int _passCount;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 52 Tests ---");
            _passCount = 0;

            TestStatsExposeQueueCapacity();
            TestRuntimeOriginGuardUsesInterface();
            TestTokenGateRedactsAndCompares();
            TestWrongTokenRejectedBeforeUpgrade();
            TestTlsOptionsValidateDeterministicPfx();
            TestWssHandshakeAndServerInfo();
            TestWebSocketHandshakeRejectsUnsupportedVersion();
            TestWssTlsHandshakeAbortIsQuietByDefault();
            TestWssTlsHandshakeAbortCanBeLoggedWhenEnabled();
            TestWssOriginGuardRejectsDisallowedOrigin();
            TestSecureStopStartReleasesPort();
            TestReceiveLoopIgnoresSslStreamDisposalRace();
            TestFrameCodecTreatsRemoteAbortAggregateAsCleanEnd();
            TestWebSocketFrameProtocolDecodesMaskedClientFrame();
            TestWebSocketFrameProtocolRejectsInvalidClientFrames();
            TestSendQueueZeroByteLimitKeepsProtocolFramesUsable();
            TestClientConnectedExceptionDisconnectsRegisteredClient();
            TestManagedBackendStopDisposesCancellationSource();
            TestHostedFoxgloveWebUrlMatchesOfficialSdk();
            TestRemoteFileDesktopUrlMatchesFoxgloveShareableLinks();
            TestManagerDefaultsAllowFoxgloveWebOrigin();
            TestCertificateDistributorServesRootAndFingerprint();
            TestCertificateDistributorIgnoresIdleClientTimeout();
            TestManagerSourceClosesBackendSelectionPath();

            Console.WriteLine($"Phase 52: {_passCount} checks passed.");
        }

        private static void TestStatsExposeQueueCapacity()
        {
            using var backend = new ManagedWsBackend();
            var snap = backend.GetStatsSnapshot();

            Check(snap.Supported, "52A-1: managed backend stats supported");
            Check(snap.MaxQueuedFramesPerClient == ManagedWebSocketOptions.DefaultMaxQueuedFrames,
                "52A-1b: stats expose max queued frames per client");
            Check(snap.MaxQueuedBytesPerClient == ManagedWebSocketOptions.DefaultMaxQueuedBytes,
                "52A-1c: stats expose max queued bytes per client");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            Check(!source.Contains("ManagedWsBackend.MaxQueuedFrames"),
                "52A-1d: replay headroom no longer reads ManagedWsBackend.MaxQueuedFrames");
            Check(!source.Contains("ManagedWsBackend.MaxQueuedBytes"),
                "52A-1e: replay headroom no longer reads ManagedWsBackend.MaxQueuedBytes");
        }

        private static void TestRuntimeOriginGuardUsesInterface()
        {
            var transport = new Phase52OriginTransport();
            using var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());

            runtime.AddAllowedOrigin("https://allowed.example");
            runtime.ClearAllowedOrigins();

            Check(transport.AddedOrigin == "https://allowed.example",
                "52A-2: runtime forwards AddAllowedOrigin through origin guard interface");
            Check(transport.ClearCalled,
                "52A-2b: runtime forwards ClearAllowedOrigins through origin guard interface");
        }

        private static void TestTokenGateRedactsAndCompares()
        {
            var options = new ManagedWebSocketOptions { SharedToken = ValidToken };

            Check(options.RequireToken, "52E-1: non-empty shared token enables gate");
            Check(options.IsTokenAccepted(ValidToken), "52E-1b: exact token accepted");
            Check(!options.IsTokenAccepted(WrongToken), "52E-1c: wrong token rejected");
            Check(!options.IsTokenAccepted(null), "52E-1d: missing token rejected");

            var redacted = ManagedWebSocketOptions.RedactUrl($"wss://127.0.0.1:8765?token={ValidToken}&foo=bar");
            Check(!redacted.Contains(ValidToken), "52E-1e: raw token removed from redacted URL");
            Check(redacted.Contains("token=REDACTED"), "52E-1f: redacted URL keeps token marker");
        }

        private static void TestWrongTokenRejectedBeforeUpgrade()
        {
            var port = GetFreeTcpPort();
            using var backend = new ManagedWsBackend(new ManagedWebSocketOptions { SharedToken = ValidToken });
            backend.Start("127.0.0.1", port);

            var response = SendRawHandshake(
                port,
                $"/?token={WrongToken}",
                origin: null,
                useTls: false,
                serverName: null);

            Check(response.StartsWith("HTTP/1.1 401", StringComparison.Ordinal),
                "52E-2: wrong token rejected before WebSocket upgrade");
            Check(!response.Contains(WrongToken), "52E-2b: rejection response does not echo provided token");
        }

        private static void TestTlsOptionsValidateDeterministicPfx()
        {
            using var fixture = Phase52CertificateFixture.Create();
            var options = new FoxgloveTlsOptions
            {
                CertificatePfxPath = fixture.PfxPath,
                CertificatePassword = fixture.Password
            };

            using var cert = options.LoadCertificate();
            Check(cert.HasPrivateKey, "52B-1: generated deterministic test PFX loads with private key");

            var wrongPassword = new FoxgloveTlsOptions
            {
                CertificatePfxPath = fixture.PfxPath,
                CertificatePassword = "wrong-password"
            };
            CheckThrows<InvalidOperationException>(() => wrongPassword.LoadCertificate(),
                "52B-1b: wrong PFX password rejected");

            var missing = new FoxgloveTlsOptions
            {
                CertificatePfxPath = Path.Combine(fixture.DirectoryPath, "missing.pfx"),
                CertificatePassword = fixture.Password
            };
            CheckThrows<InvalidOperationException>(() => missing.LoadCertificate(),
                "52B-1c: missing PFX path rejected");
        }

        private static void TestWssHandshakeAndServerInfo()
        {
            using var fixture = Phase52CertificateFixture.Create();
            var port = GetFreeTcpPort();
            var tls = new FoxgloveTlsOptions
            {
                CertificatePfxPath = fixture.PfxPath,
                CertificatePassword = fixture.Password
            };
            using var backend = new ManagedWssBackend(tls);
            using var runtime = new FoxgloveRuntime(backend, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase52-wss", "127.0.0.1", port);

            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            using var ssl = CreateClientSslStream(client);
            ssl.AuthenticateAsClient("localhost");
            ssl.ReadTimeout = TestTimeoutMs;
            ssl.WriteTimeout = TestTimeoutMs;

            WriteHandshake(ssl, "/", origin: null);
            var response = ReadHttpResponse(ssl);
            Check(response.StartsWith("HTTP/1.1 101", StringComparison.Ordinal),
                "52B-2: WSS handshake upgrades after TLS");

            var firstTextFrame = ReadServerTextFrame(ssl);
            Check(firstTextFrame.Contains("\"op\":\"serverInfo\""),
                "52B-2b: WSS client receives serverInfo frame");
        }

        private static void TestWebSocketHandshakeRejectsUnsupportedVersion()
        {
            var port = GetFreeTcpPort();
            using var backend = new ManagedWsBackend();
            backend.Start("127.0.0.1", port);

            var response = SendRawHandshake(
                port,
                "/",
                origin: null,
                useTls: false,
                serverName: null,
                websocketVersion: "12");

            Check(response.StartsWith("HTTP/1.1 426", StringComparison.Ordinal),
                "52B-2b1: unsupported WebSocket version is rejected with 426");
            Check(response.Contains("Sec-WebSocket-Version: 13", StringComparison.Ordinal),
                "52B-2b2: unsupported WebSocket version response advertises version 13");
        }

        private static void TestWssTlsHandshakeAbortIsQuietByDefault()
        {
            using var fixture = Phase52CertificateFixture.Create();
            var port = GetFreeTcpPort();
            var logger = new Phase52CaptureLogger();
            using var backend = new ManagedWssBackend(
                new FoxgloveTlsOptions
                {
                    CertificatePfxPath = fixture.PfxPath,
                    CertificatePassword = fixture.Password
                },
                logger: logger);
            backend.Start("127.0.0.1", port);

            ConnectAndCloseTcp(port);

            WaitUntil(() => logger.WarningCount + logger.ErrorCount > 0, QuietLogWaitMs);
            Check(logger.ErrorCount == 0,
                "52B-2c: WSS TLS handshake abort is not logged as a server error");
            Check(logger.WarningCount == 0,
                "52B-2d: WSS TLS handshake abort is quiet by default");
        }

        private static void TestWssTlsHandshakeAbortCanBeLoggedWhenEnabled()
        {
            using var fixture = Phase52CertificateFixture.Create();
            var port = GetFreeTcpPort();
            var logger = new Phase52CaptureLogger();
            using var backend = new ManagedWssBackend(
                new FoxgloveTlsOptions
                {
                    CertificatePfxPath = fixture.PfxPath,
                    CertificatePassword = fixture.Password
                },
                new ManagedWebSocketOptions
                {
                    LogPreHandshakeClientDisconnects = true
                },
                logger);
            backend.Start("127.0.0.1", port);

            ConnectAndCloseTcp(port);

            WaitUntil(() => logger.WarningCount + logger.ErrorCount > 0, TestTimeoutMs);
            Check(logger.ErrorCount == 0,
                "52B-2e: WSS TLS handshake abort opt-in logging is not a server error");
            Check(logger.WarningCount > 0
                  && logger.LastWarning.Contains("TLS/WebSocket handshake"),
                "52B-2f: WSS TLS handshake abort opt-in warning names the handshake stage");
        }

        private static void TestWssOriginGuardRejectsDisallowedOrigin()
        {
            using var fixture = Phase52CertificateFixture.Create();
            var port = GetFreeTcpPort();
            using var backend = new ManagedWssBackend(new FoxgloveTlsOptions
            {
                CertificatePfxPath = fixture.PfxPath,
                CertificatePassword = fixture.Password
            });
            backend.Start("127.0.0.1", port);

            var response = SendRawHandshake(
                port,
                "/",
                origin: "https://evil.example",
                useTls: true,
                serverName: "localhost");

            Check(response.StartsWith("HTTP/1.1 403", StringComparison.Ordinal),
                "52A-3: WSS disallowed browser Origin rejected");
        }

        private static void TestSecureStopStartReleasesPort()
        {
            using var fixture = Phase52CertificateFixture.Create();
            var port = GetFreeTcpPort();
            var tls = new FoxgloveTlsOptions
            {
                CertificatePfxPath = fixture.PfxPath,
                CertificatePassword = fixture.Password
            };

            using (var first = new ManagedWssBackend(tls))
            {
                first.Start("127.0.0.1", port);
                first.Stop();
            }

            using (var second = new ManagedWssBackend(tls))
            {
                second.Start("127.0.0.1", port);
                Check(second.IsRunning, "52B-3: secure backend restarts on same port after stop");
                second.Stop();
            }
        }

        private static void TestReceiveLoopIgnoresSslStreamDisposalRace()
        {
            var logger = new Phase52CaptureLogger();
            using var backend = new ManagedWsBackend(logger);
            using var tcpClient = new TcpClient();
            var stream = new Phase52DisposedReadStream();
            var conn = new WsConnection(
                tcpClient,
                stream,
                ManagedWebSocketOptions.DefaultMaxQueuedFrames,
                ManagedWebSocketOptions.DefaultMaxQueuedBytes);

            try
            {
                var receiveLoop = typeof(ManagedWsBackend).GetMethod(
                    "ReceiveLoop",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                receiveLoop.Invoke(backend, new object[] { 1u, conn, CancellationToken.None });
            }
            finally
            {
                conn.Dispose();
                stream.Dispose();
            }

            Check(logger.ErrorCount == 0,
                "52B-4: receive loop ignores SSL stream disposal race during shutdown");
        }

        private static void TestFrameCodecTreatsRemoteAbortAggregateAsCleanEnd()
        {
            using var stream = new Phase52ThrowingReadStream(
                new AggregateException(
                    new IOException(
                        "Unable to write data to the transport connection.",
                        new SocketException((int)SocketError.ConnectionAborted))));

            var cleanEnd = false;
            try
            {
                cleanEnd = !WsFrameCodec.TryReadFrame(stream, out var frame) && frame == null;
            }
            catch (AggregateException)
            {
                cleanEnd = false;
            }

            Check(cleanEnd,
                "52B-4b: WSS remote abort aggregate is treated as a clean stream end");
        }
    }
}
