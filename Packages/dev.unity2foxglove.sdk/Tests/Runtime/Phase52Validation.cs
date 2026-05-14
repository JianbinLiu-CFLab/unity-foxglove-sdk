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
    public static class Phase52Validation
    {
        private const int TestTimeoutMs = 5000;
        private const string ValidToken = "phase52-secret";
        private const string WrongToken = "phase52-wrong";
        private static int _passCount;

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
            TestWssTlsHandshakeAbortIsWarning();
            TestWssOriginGuardRejectsDisallowedOrigin();
            TestSecureStopStartReleasesPort();
            TestReceiveLoopIgnoresSslStreamDisposalRace();
            TestWebSocketFrameProtocolRejectsInvalidClientFrames();
            TestManagedBackendStopDisposesCancellationSource();
            TestHostedFoxgloveWebUrlMatchesOfficialSdk();
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

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveSession.cs");
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

        private static void TestWssTlsHandshakeAbortIsWarning()
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

            using (var client = new TcpClient())
            {
                client.Connect("127.0.0.1", port);
            }

            WaitUntil(() => logger.WarningCount + logger.ErrorCount > 0, TestTimeoutMs);
            Check(logger.ErrorCount == 0,
                "52B-2c: WSS TLS handshake abort is not logged as a server error");
            Check(logger.WarningCount > 0
                  && logger.LastWarning.Contains("TLS/WebSocket handshake"),
                "52B-2d: WSS TLS handshake abort warning names the handshake stage");
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

        private static void TestWebSocketFrameProtocolRejectsInvalidClientFrames()
        {
            Check(ReadFrameFromBytes(BuildClientFrame(WsOpcode.Text, Encoding.UTF8.GetBytes("hi"), masked: false, fin: true)) == null,
                "52B-5: client frames without masking are rejected");

            Check(ReadFrameFromBytes(BuildClientFrame(WsOpcode.Ping, Encoding.UTF8.GetBytes("x"), masked: true, fin: false)) == null,
                "52B-5b: fragmented control frames are rejected");

            Check(ReadFrameFromBytes(BuildClientFrame(WsOpcode.Ping, new byte[126], masked: true, fin: true)) == null,
                "52B-5c: oversized control frames are rejected before dispatch");
        }

        private static void TestManagedBackendStopDisposesCancellationSource()
        {
            var port = GetFreeTcpPort();
            using var backend = new ManagedWsBackend();
            backend.Start("127.0.0.1", port);

            var ctsField = typeof(ManagedWsBackend).GetField("_cts", BindingFlags.Instance | BindingFlags.NonPublic);
            var firstCts = (CancellationTokenSource)ctsField.GetValue(backend);
            backend.Stop();

            CheckThrows<ObjectDisposedException>(() =>
            {
                var _ = firstCts.Token;
            }, "52B-6: Stop disposes the previous cancellation token source before restart");

            backend.Start("127.0.0.1", port);
            backend.Stop();
        }

        private static void TestManagerDefaultsAllowFoxgloveWebOrigin()
        {
            const string foxgloveWebOrigin = "https://app.foxglove.dev";
            var managerSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveManager.cs");

            Check(!managerSource.Contains("FoxgloveWebAppOrigin"),
                "52A-4: manager does not expose a single hard-coded Foxglove web origin constant");
            Check(managerSource.Contains($"_allowedBrowserOrigins = new() {{ \"{foxgloveWebOrigin}\" }}"),
                "52A-4b: new managers allow Foxglove web by default");
            Check(managerSource.Contains("_allowHostedFoxgloveWeb = true"),
                "52A-4b2: existing managers can allow hosted Foxglove web without serialized list migration");
            Check(managerSource.Contains("_runtime.AddAllowedOrigin(FoxgloveAppUrl.HostedWebBaseUrl)"),
                "52A-4b3: manager always syncs hosted Foxglove web origin when enabled");

            Check(ReadRepoText("Unity2Foxglove/Assets/Scenes/SampleScene.unity").Contains($"- {foxgloveWebOrigin}"),
                "52A-4c: demo SampleScene allows Foxglove web origin");
            Check(ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization/Scenes/BasicVisualization.unity")
                    .Contains($"- {foxgloveWebOrigin}"),
                "52A-4d: BasicVisualization sample allows Foxglove web origin");
            Check(ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scenes/FullDemoVisualization.unity")
                    .Contains($"- {foxgloveWebOrigin}"),
                "52A-4e: FullDemoVisualization sample allows Foxglove web origin");
        }

        private static void TestHostedFoxgloveWebUrlMatchesOfficialSdk()
        {
            var localPlain = FoxgloveAppUrl.BuildHostedWebSocketUrl("127.0.0.1", 8765, secure: false);
            Check(localPlain == "https://app.foxglove.dev?ds=foxglove-websocket&ds.url=ws%3A%2F%2F127.0.0.1%3A8765",
                "52A-5: hosted Foxglove Web URL uses official SDK plain ws loopback format");
            Check(!localPlain.Contains("wss%3A%2F%2F"),
                "52A-5b: local plain web URL does not force WSS");

            var secureWithLayout = FoxgloveAppUrl.BuildHostedWebSocketUrl(
                "my.robot.dev",
                9999,
                secure: true,
                layoutId: "lay_123",
                openInDesktop: true);
            Check(secureWithLayout == "https://app.foxglove.dev?ds=foxglove-websocket&ds.url=wss%3A%2F%2Fmy.robot.dev%3A9999&layoutId=lay_123&openIn=desktop",
                "52A-5c: hosted Foxglove Web URL can still represent WSS and layout/openIn options");

            var redacted = FoxgloveAppUrl.BuildHostedWebSocketUrl(
                "0.0.0.0",
                8765,
                secure: false,
                token: "secret token",
                redactToken: true);
            Check(redacted.Contains("ws%3A%2F%2F127.0.0.1%3A8765%3Ftoken%3DREDACTED"),
                "52A-5d: displayed web URL maps wildcard bind host to loopback and redacts token");

            var secureDocs = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/15_Secure_WSS.md");
            Check(secureDocs.Contains("official Foxglove SDK")
                  && secureDocs.Contains("WSS is not required for hosted Foxglove Web on local loopback"),
                "52A-5e: WSS docs explain official SDK plain loopback Web behavior");
        }

        private static void TestCertificateDistributorServesRootAndFingerprint()
        {
            using var fixture = Phase52CertificateFixture.Create();
            var port = GetFreeTcpPort();
            using var distributor = new FoxgloveCertificateDistributor(fixture.RootCaPath);
            distributor.Start("127.0.0.1", port);

            var rootPage = SendHttpGet(port, "/");
            Check(rootPage.StartsWith("HTTP/1.1 200", StringComparison.Ordinal),
                "52D-1: distributor root page returns 200");
            Check(rootPage.Contains(distributor.RootCaSha256Fingerprint),
                "52D-1b: root page includes SHA-256 fingerprint");

            var crt = SendHttpGet(port, "/rootCA.crt");
            Check(crt.StartsWith("HTTP/1.1 200", StringComparison.Ordinal),
                "52D-1c: root CA endpoint returns 200");
            Check(crt.Contains("application/x-x509-ca-cert"),
                "52D-1d: root CA endpoint uses certificate content type");

            var missing = SendHttpGet(port, "/missing");
            Check(missing.StartsWith("HTTP/1.1 404", StringComparison.Ordinal),
                "52D-1e: unknown distributor path returns 404");

            distributor.Stop();
            using var listener = new TcpListener(System.Net.IPAddress.Parse("127.0.0.1"), port);
            listener.Start();
            listener.Stop();
            Check(true, "52D-1f: distributor stop releases listener port");

            using var localhostDistributor = new FoxgloveCertificateDistributor(fixture.RootCaPath);
            localhostDistributor.Start("localhost", port);
            var localhostPage = SendHttpGet(port, "/");
            Check(localhostPage.StartsWith("HTTP/1.1 200", StringComparison.Ordinal),
                "52D-1g: distributor accepts localhost bind alias");
            localhostDistributor.Stop();
        }

        private static void TestCertificateDistributorIgnoresIdleClientTimeout()
        {
            using var fixture = Phase52CertificateFixture.Create();
            var port = GetFreeTcpPort();
            var logger = new Phase52CaptureLogger();
            using var distributor = new FoxgloveCertificateDistributor(fixture.RootCaPath, logger: logger);
            distributor.Start("127.0.0.1", port);

            using (var client = new TcpClient())
            {
                client.Connect("127.0.0.1", port);
                Thread.Sleep(TestTimeoutMs + 750);
            }

            distributor.Stop();
            Check(logger.ErrorCount == 0,
                "52D-1h: distributor ignores idle browser preconnect/read timeout as normal client IO");
        }

        private static void TestManagerSourceClosesBackendSelectionPath()
        {
            var managerSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveManager.cs");
            var runtimeSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs");
            var editorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxgloveManagerEditor.cs");
            var certGeneratorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxgloveLocalDevCertificateGenerator.cs");
            var demoSource = ReadRepoText("Unity2Foxglove/Assets/Scripts/FoxgloveDemoSetup.cs");

            Check(managerSource.Contains("_transportMode"), "52C-1: manager stores transport mode");
            Check(managerSource.Contains("CreateTransport"), "52C-1b: manager centralizes transport construction");
            Check(managerSource.Contains("new Core.FoxgloveRuntime(transport"),
                "52C-1c: manager injects selected transport into runtime");
            Check(managerSource.Contains("ManagedWssBackend"), "52C-1d: manager can construct secure backend");
            Check(editorSource.Contains("Security / WSS"), "52C-1e: Inspector exposes WSS security section");
            Check(editorSource.Contains("REDACTED"), "52C-1f: Inspector redacts token display");
            Check(managerSource.Contains("ValidateTransportConfiguration"),
                "52C-1g: manager preflights WSS certificate configuration before start");
            Check(managerSource.Contains("FoxgloveAppUrl.BuildWebSocketEndpoint(")
                  && !managerSource.Contains("url += $\"?token={_sharedToken}\""),
                "52C-1g2: manager connection URL redaction uses encoded endpoint builder");
            Check(!managerSource.Contains("if (!IsRunning)\r\n            {\r\n                StopCertificateDistributor();\r\n                return;\r\n            }") &&
                  !managerSource.Contains("if (!IsRunning)\n            {\n                StopCertificateDistributor();\n                return;\n            }"),
                "52C-1g3: StopServer still stops runtime so active recordings are finalized even if transport is already stopped");
            var stopIndex = runtimeSource.IndexOf("public void Stop()", StringComparison.Ordinal);
            var detachRecorderIndex = runtimeSource.IndexOf("session?.SetRecorder(null);", stopIndex, StringComparison.Ordinal);
            var disposeSessionIndex = runtimeSource.IndexOf("session?.Dispose();", stopIndex, StringComparison.Ordinal);
            var detachRecordingIndex = runtimeSource.IndexOf("_recording.DetachFromSession();", stopIndex, StringComparison.Ordinal);
            Check(stopIndex >= 0 &&
                  detachRecorderIndex > stopIndex &&
                  disposeSessionIndex > detachRecorderIndex &&
                  detachRecordingIndex > disposeSessionIndex,
                "52C-1g4: runtime detaches recorder from session and stops transport before finalizing recording");

            var startIndex = demoSource.IndexOf("private void Start()", StringComparison.Ordinal);
            var sessionGuardIndex = demoSource.IndexOf("_manager?.Runtime?.Session == null", startIndex, StringComparison.Ordinal);
            var runtimeCaptureIndex = demoSource.IndexOf("var rt = _manager.Runtime", startIndex, StringComparison.Ordinal);
            Check(startIndex >= 0 && sessionGuardIndex >= 0 && runtimeCaptureIndex >= 0 && sessionGuardIndex < runtimeCaptureIndex,
                "52C-1h: demo setup skips service wiring when manager start failed before session creation");
            Check(editorSource.Contains("Generate Local Dev Certificate")
                  && editorSource.Contains("GenerateLocalDevCertificate"),
                "52C-1i: Inspector exposes one-click local dev certificate generation");
            var drawFieldsIndex = editorSource.IndexOf("DrawSecureWebSocketFields(isSecure);", StringComparison.Ordinal);
            var generateButtonIndex = editorSource.IndexOf("GUILayout.Button(\"Generate Local Dev Certificate\")", StringComparison.Ordinal);
            Check(drawFieldsIndex >= 0 && generateButtonIndex > drawFieldsIndex,
                "52C-1i1: certificate generator button remains enabled before SecureWebSocket mode is selected");
            Check(editorSource.Contains("FoxgloveAppUrl.BuildHostedWebSocketUrl")
                  && editorSource.Contains("Copy Foxglove Web URL")
                  && editorSource.Contains("Open Foxglove Web"),
                "52C-1i2: Inspector exposes generated Foxglove Web URL actions");
            Check(editorSource.Contains("DrawProperty(\"_allowHostedFoxgloveWeb\")"),
                "52C-1i3: Inspector exposes hosted Foxglove Web origin toggle");
            Check(editorSource.Contains("StartEditorRootCaDistributor(")
                  && editorSource.Contains("result.RootCaPath")
                  && editorSource.Contains("Application.OpenURL(LocalRootCaPageUrl)"),
                "52C-1i4: Generate Local Dev Certificate starts and opens the editor Root CA page");
            Check(editorSource.Contains("AssemblyReloadEvents.beforeAssemblyReload")
                  && editorSource.Contains("PlayModeStateChange.ExitingEditMode"),
                "52C-1i5: editor Root CA distributor releases its port before reload or play mode handoff");
            Check(editorSource.Contains("Reveal Certificate Folder")
                  && editorSource.Contains("Copy Root CA SHA-256")
                  && editorSource.Contains("Copy Redacted WSS URL"),
                "52C-1j: Inspector exposes certificate helper actions");
            Check(editorSource.Contains("EnsureSecureSettingsVisible"),
                "52C-1j2: Inspector opens WSS settings when secure mode needs certificate setup");
            Check(certGeneratorSource.Contains("UserSettings/Unity2Foxglove/Certificates"),
                "52C-1k: local dev certificates are generated under ignored UserSettings");
            Check(!certGeneratorSource.Contains("certutil")
                  && !certGeneratorSource.Contains("security add-trusted-cert")
                  && !certGeneratorSource.Contains("update-ca-certificates"),
                "52C-1l: certificate generator does not silently import OS trust");
            Check(!certGeneratorSource.Contains("winget", StringComparison.OrdinalIgnoreCase)
                  && !certGeneratorSource.Contains("choco", StringComparison.OrdinalIgnoreCase)
                  && !certGeneratorSource.Contains("brew install", StringComparison.OrdinalIgnoreCase)
                  && !certGeneratorSource.Contains("apt install", StringComparison.OrdinalIgnoreCase),
                "52C-1l2: certificate generator does not silently install OpenSSL");
            Check(certGeneratorSource.Contains("BuiltInLocalDevCertificateBackend")
                  && certGeneratorSource.Contains("MonoSecurityLocalDevCertificateBackend")
                  && certGeneratorSource.Contains("Mono.Security.X509.X509CertificateBuilder")
                  && certGeneratorSource.Contains("Mono.Security.X509.PKCS12")
                  && certGeneratorSource.Contains("FoxgloveLocalDevCertificateOptions.BuiltIn"),
                "52C-1m: Editor certificate generator defaults to a Unity/Mono built-in backend");
            Check(!certGeneratorSource.Contains("new CertificateRequest"),
                "52C-1m1: built-in certificate backend does not depend on CertificateRequest");
            Check(certGeneratorSource.Contains("ToArray(context.IpAddresses),")
                  && certGeneratorSource.Contains("Array.Empty<string>()")
                  && !certGeneratorSource.Contains("Array.Empty<string>(),\n                ToArray(context.IpAddresses)"),
                "52C-1m1b: built-in certificate backend writes 127.0.0.1 as an IP SAN, not a URI SAN");
            Check(certGeneratorSource.Contains("KeyUsageOid")
                  && certGeneratorSource.Contains("0x03, 0x02, 0x02, 0xA4"),
                "52C-1m1c: built-in certificate backend includes digitalSignature, keyEncipherment, and keyCertSign");
            Check(certGeneratorSource.Contains("TypeLoadException")
                  && certGeneratorSource.Contains("MissingMethodException")
                  && certGeneratorSource.Contains("BuiltInUnavailable"),
                "52C-1m2: built-in certificate backend reports missing Unity profile APIs as unavailable");
            Check(certGeneratorSource.Contains("OpenSslLocalDevCertificateBackend")
                  && certGeneratorSource.Contains("OpenSslResolver")
                  && editorSource.Contains("Unity2Foxglove.LocalDevCertificate.Backend")
                  && editorSource.Contains("Choose OpenSSL"),
                "52C-1n: OpenSSL certificate backend is an explicitly selectable fallback");
            Check(certGeneratorSource.Contains("-macalg sha1")
                  && certGeneratorSource.Contains("-keypbe PBE-SHA1-3DES")
                  && certGeneratorSource.Contains("-certpbe PBE-SHA1-3DES"),
                "52C-1o: Editor certificate generator exports Unity Mono-compatible PKCS12");
        }

        private static string SendRawHandshake(
            int port,
            string target,
            string origin,
            bool useTls,
            string serverName)
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            client.ReceiveTimeout = TestTimeoutMs;
            client.SendTimeout = TestTimeoutMs;

            if (useTls)
            {
                using var ssl = CreateClientSslStream(client);
                ssl.AuthenticateAsClient(serverName ?? "localhost");
                ssl.ReadTimeout = TestTimeoutMs;
                ssl.WriteTimeout = TestTimeoutMs;
                WriteHandshake(ssl, target, origin);
                return ReadHttpResponse(ssl);
            }

            using var stream = client.GetStream();
            stream.ReadTimeout = TestTimeoutMs;
            stream.WriteTimeout = TestTimeoutMs;
            WriteHandshake(stream, target, origin);
            return ReadHttpResponse(stream);
        }

        private static string SendHttpGet(int port, string target)
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            using var stream = client.GetStream();
            stream.ReadTimeout = TestTimeoutMs;
            stream.WriteTimeout = TestTimeoutMs;
            var request = Encoding.ASCII.GetBytes(
                $"GET {target} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n");
            stream.Write(request, 0, request.Length);
            stream.Flush();
            return ReadToEnd(stream);
        }

        private static System.Net.Security.SslStream CreateClientSslStream(TcpClient client)
        {
            return new System.Net.Security.SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (_, _, _, _) => true);
        }

        private static void WriteHandshake(Stream stream, string target, string origin)
        {
            var sb = new StringBuilder();
            sb.Append($"GET {target} HTTP/1.1\r\n");
            sb.Append("Host: 127.0.0.1\r\n");
            sb.Append("Upgrade: websocket\r\n");
            sb.Append("Connection: Upgrade\r\n");
            sb.Append("Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n");
            sb.Append("Sec-WebSocket-Version: 13\r\n");
            sb.Append("Sec-WebSocket-Protocol: foxglove.sdk.v1\r\n");
            if (!string.IsNullOrEmpty(origin))
                sb.Append($"Origin: {origin}\r\n");
            sb.Append("\r\n");

            var bytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string ReadHttpResponse(Stream stream)
        {
            var bytes = new MemoryStream();
            var tail = new byte[4];
            while (true)
            {
                var value = stream.ReadByte();
                if (value < 0)
                    break;

                bytes.WriteByte((byte)value);
                tail[0] = tail[1];
                tail[1] = tail[2];
                tail[2] = tail[3];
                tail[3] = (byte)value;
                if (tail[0] == '\r' && tail[1] == '\n' && tail[2] == '\r' && tail[3] == '\n')
                    break;
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private static string ReadToEnd(Stream stream)
        {
            var buffer = new byte[1024];
            using var output = new MemoryStream();
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;
                output.Write(buffer, 0, read);
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }

        private static string ReadServerTextFrame(Stream stream)
        {
            var first = stream.ReadByte();
            var second = stream.ReadByte();
            if (first < 0 || second < 0)
                return string.Empty;

            var length = second & 0x7f;
            if (length == 126)
            {
                var ext = ReadExact(stream, 2);
                length = (ext[0] << 8) | ext[1];
            }
            else if (length == 127)
            {
                var ext = ReadExact(stream, 8);
                var longLength = 0L;
                foreach (var b in ext)
                    longLength = (longLength << 8) | b;
                if (longLength > int.MaxValue)
                    throw new InvalidDataException("Test frame is too large.");
                length = (int)longLength;
            }

            var payload = ReadExact(stream, length);
            return Encoding.UTF8.GetString(payload);
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
            }

            return buffer;
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void WaitUntil(Func<bool> predicate, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (predicate())
                    return;
                Thread.Sleep(10);
            }
        }

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                _passCount++;
                Console.WriteLine($"[PASS] {label}");
                return;
            }

            throw new Exception($"[FAIL] {label}");
        }

        private static void CheckThrows<TException>(Action action, string label)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                Check(true, label);
                return;
            }

            throw new Exception($"[FAIL] {label}");
        }

        private static WsFrame ReadFrameFromBytes(byte[] frameBytes)
        {
            using var stream = new MemoryStream(frameBytes);
            return WsFrameCodec.TryReadFrame(stream, out var frame) ? frame : null;
        }

        private static byte[] BuildClientFrame(byte opcode, byte[] payload, bool masked, bool fin)
        {
            payload ??= Array.Empty<byte>();
            using var ms = new MemoryStream();
            ms.WriteByte((byte)((fin ? 0x80 : 0x00) | opcode));

            var maskFlag = masked ? 0x80 : 0x00;
            if (payload.Length <= 125)
            {
                ms.WriteByte((byte)(maskFlag | payload.Length));
            }
            else if (payload.Length <= ushort.MaxValue)
            {
                ms.WriteByte((byte)(maskFlag | 126));
                ms.WriteByte((byte)(payload.Length >> 8));
                ms.WriteByte((byte)payload.Length);
            }
            else
            {
                throw new InvalidDataException("Test frame payload is too large.");
            }

            if (masked)
            {
                var mask = new byte[] { 0x12, 0x34, 0x56, 0x78 };
                ms.Write(mask, 0, mask.Length);
                for (var i = 0; i < payload.Length; i++)
                    ms.WriteByte((byte)(payload[i] ^ mask[i % mask.Length]));
            }
            else
            {
                ms.Write(payload, 0, payload.Length);
            }

            return ms.ToArray();
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string FindRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }

        private sealed class Phase52OriginTransport : IFoxgloveTransport, IOriginGuardedFoxgloveTransport
        {
            public string AddedOrigin { get; private set; }
            public bool ClearCalled { get; private set; }
            public bool IsRunning => false;
            public System.Collections.Generic.IReadOnlyCollection<string> AllowedOrigins =>
                Array.Empty<string>();
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) { }
            public void Stop() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void AddAllowedOrigin(string origin) => AddedOrigin = origin;
            public void ClearAllowedOrigins() => ClearCalled = true;
            public void Dispose() { }
        }

        private sealed class Phase52CaptureLogger : IFoxgloveLogger
        {
            public int WarningCount { get; private set; }
            public int ErrorCount { get; private set; }
            public string LastWarning { get; private set; }
            public string LastError { get; private set; }

            public void LogWarning(string message)
            {
                WarningCount++;
                LastWarning = message ?? string.Empty;
            }

            public void LogError(string message)
            {
                ErrorCount++;
                LastError = message ?? string.Empty;
            }
        }

        private sealed class Phase52DisposedReadStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new AggregateException(new ObjectDisposedException("MobileAuthenticatedStream"));
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count) { }
        }

        private sealed class Phase52CertificateFixture : IDisposable
        {
            private Phase52CertificateFixture(string directoryPath, string pfxPath, string rootCaPath, string password)
            {
                DirectoryPath = directoryPath;
                PfxPath = pfxPath;
                RootCaPath = rootCaPath;
                Password = password;
            }

            public string DirectoryPath { get; }
            public string PfxPath { get; }
            public string RootCaPath { get; }
            public string Password { get; }

            public static Phase52CertificateFixture Create()
            {
                var dir = Path.Combine(Path.GetTempPath(), "unity2foxglove-phase52-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                var pfx = Path.Combine(dir, "phase52-server.pfx");
                var root = Path.Combine(dir, "phase52-root.crt");
                const string password = "phase52-test-password";

                using var rsa = RSA.Create(2048);
                var request = new CertificateRequest(
                    "CN=localhost",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false));
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                using var cert = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddDays(7));
                File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pfx, password));
                File.WriteAllBytes(root, cert.Export(X509ContentType.Cert));

                return new Phase52CertificateFixture(dir, pfx, root, password);
            }

            public void Dispose()
            {
                try { Directory.Delete(DirectoryPath, recursive: true); } catch { }
            }
        }
    }
}
