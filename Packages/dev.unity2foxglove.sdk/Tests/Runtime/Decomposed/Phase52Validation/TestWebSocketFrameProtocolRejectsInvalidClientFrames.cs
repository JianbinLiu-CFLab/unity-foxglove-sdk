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
        private static void TestWebSocketFrameProtocolRejectsInvalidClientFrames()
        {
            Check(ReadFrameFromBytes(BuildClientFrame(WsOpcode.Text, Encoding.UTF8.GetBytes("hi"), masked: false, fin: true)) == null,
                "52B-5: client frames without masking are rejected");

            Check(ReadFrameFromBytes(BuildClientFrame(WsOpcode.Ping, Encoding.UTF8.GetBytes("x"), masked: true, fin: false)) == null,
                "52B-5b: fragmented control frames are rejected");

            Check(ReadFrameFromBytes(BuildClientFrame(WsOpcode.Ping, new byte[126], masked: true, fin: true)) == null,
                "52B-5c: oversized control frames are rejected before dispatch");

            using (var oversized = new MemoryStream(BuildOversizedMaskedFrameHeader()))
            {
                var rejected = !WsFrameCodec.TryReadFrame(oversized, out var frame) && frame == null;
                Check(rejected && oversized.Position == WsFrameCodec.MaxFrameHeaderBytes,
                    "52B-5c2: oversized masked data frames are rejected before reading mask bytes");
            }

            Check(ReadFrameFromBytes(BuildClientFrame(WsOpcode.Text, Encoding.UTF8.GetBytes("hi"), masked: true, fin: true, reservedBits: 0x40)) == null,
                "52B-5d: client frames with RSV bits are rejected when no extension is negotiated");

            var continuation = ReadFrameFromBytes(BuildClientFrame(0x0, Encoding.UTF8.GetBytes("tail"), masked: true, fin: true));
            Check(continuation != null && continuation.Opcode == WsOpcode.Continuation,
                "52B-5e: continuation frames decode for transport-level fragmentation handling");

            Check(ReadFrameFromBytes(BuildClientFrame(0x3, Encoding.UTF8.GetBytes("reserved"), masked: true, fin: true)) == null,
                "52B-5f: reserved data opcodes are rejected");
        }

        private static void TestWebSocketFrameProtocolDecodesMaskedClientFrame()
        {
            var payload = Encoding.UTF8.GetBytes("known masked client frame");
            var frame = ReadFrameFromBytes(BuildClientFrame(WsOpcode.Binary, payload, masked: true, fin: true));

            Check(frame != null, "52B-4c: masked client frame decodes");
            Check(frame != null && frame.Fin, "52B-4d: decoded client frame preserves FIN");
            Check(frame != null && frame.Opcode == WsOpcode.Binary,
                "52B-4e: decoded client frame preserves opcode");
            Check(frame != null && frame.Payload.SequenceEqual(payload),
                "52B-4f: decoded client frame payload is unmasked");
        }

        private static void TestSendQueueZeroByteLimitKeepsProtocolFramesUsable()
        {
            var queue = new WsSendQueue(maxFrames: 1, maxQueuedBytes: 0);
            var result = queue.Enqueue(new QueuedFrame(
                WsOpcode.Text,
                Encoding.UTF8.GetBytes(new string('s', 128)),
                FramePriority.Control));

            Check(result.Accepted && !result.ShouldDisconnect,
                "52B-5f1: zero configured send-queue byte limit falls back to a usable default");
            Check(queue.QueuedBytes == 128,
                "52B-5f2: normalized send-queue byte limit accounts for queued protocol bytes");
            Check(ManagedWebSocketOptions.NormalizeMaxQueuedBytes(0) == ManagedWebSocketOptions.DefaultMaxQueuedBytes,
                "52B-5f3: queue byte-limit normalization is shared with transport stats");
        }

        private static void TestClientConnectedExceptionDisconnectsRegisteredClient()
        {
            var port = GetFreeTcpPort();
            var logger = new Phase52CaptureLogger();
            using var backend = new ManagedWsBackend(logger);
            backend.OnClientConnected += _ => throw new InvalidOperationException("phase52 connected event failure");
            backend.Start("127.0.0.1", port);

            using var client = new TcpClient();
            client.ReceiveTimeout = TestTimeoutMs;
            client.SendTimeout = TestTimeoutMs;
            client.Connect("127.0.0.1", port);
            using var stream = client.GetStream();
            stream.ReadTimeout = TestTimeoutMs;
            stream.WriteTimeout = TestTimeoutMs;

            WriteHandshake(stream, "/", origin: null);
            var response = ReadHttpResponse(stream);
            Check(response.StartsWith("HTTP/1.1 101", StringComparison.Ordinal),
                "52B-5g: test client completes the WebSocket upgrade before connected event failure");

            WaitUntil(() => backend.GetStatsSnapshot().ActiveClientCount == 0, TestTimeoutMs);
            Check(backend.GetStatsSnapshot().ActiveClientCount == 0,
                "52B-5h: connected event failures disconnect the already registered client");
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
            var managerSource = ReadManagerSource();

            Check(!managerSource.Contains("FoxgloveWebAppOrigin"),
                "52A-4: manager does not expose a single hard-coded Foxglove web origin constant");
            Check(managerSource.Contains("_allowedBrowserOrigins = new();"),
                "52A-4b: new managers keep custom browser origins separate from hosted Foxglove web");
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

        private static void TestRemoteFileDesktopUrlMatchesFoxgloveShareableLinks()
        {
            var remoteFile = FoxgloveAppUrl.BuildRemoteFileDesktopUrl("http://127.0.0.1:8891/v1/files/local-mcap.mcap");
            Check(remoteFile == "foxglove://open?ds=remote-file&ds.url=http%3A%2F%2F127.0.0.1%3A8891%2Fv1%2Ffiles%2Flocal-mcap.mcap",
                "52A-5f: remote file desktop URL uses Foxglove shareable link remote-file datasource");

            var withSpace = FoxgloveAppUrl.BuildRemoteFileDesktopUrl("http://127.0.0.1:8891/v1/files/my run.mcap");
            Check(withSpace.Contains("my%20run.mcap"),
                "52A-5g: remote file desktop URL escapes the MCAP URL for ds.url");
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
            using var distributor = new FoxgloveCertificateDistributor(
                fixture.RootCaPath,
                logger: logger,
                clientIoTimeoutMs: CertificateDistributorIdleTimeoutTestMs);
            distributor.Start("127.0.0.1", port);

            using (var client = new TcpClient())
            {
                client.Connect("127.0.0.1", port);
                WaitUntil(() => logger.ErrorCount > 0, CertificateDistributorIdleTimeoutTestMs + 500);
            }

            distributor.Stop();
            Check(logger.ErrorCount == 0,
                "52D-1h: distributor ignores idle browser preconnect/read timeout as normal client IO");
        }

        private static void TestManagerSourceClosesBackendSelectionPath()
        {
            var managerSource = ReadManagerSource();
            var runtimeSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var editorMainSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var editorSource = PhaseValidationSourceHelpers.ReadFoxgloveManagerEditorSources();
            var certGeneratorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Certificates/FoxgloveLocalDevCertificateGenerator.cs");
            var demoSource = ReadRepoText("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs");

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
            var stopServerSource = PhaseValidationSourceHelpers.SourceMethod(
                managerSource,
                "private void StopServer(bool restoreLivePublishers)");
            var notRunningIndex = stopServerSource.IndexOf("if (!IsRunning)", StringComparison.Ordinal);
            var ownershipPredicateIndex = stopServerSource.IndexOf(
                "FoxgloveManagerTeardownState.ShouldRunStopServer(",
                StringComparison.Ordinal);
            var pendingCleanupIndex = stopServerSource.IndexOf(
                "_runtime?.HasPendingSessionCleanup ?? false",
                StringComparison.Ordinal);
            var earlyReturnIndex = ownershipPredicateIndex >= 0
                ? stopServerSource.IndexOf("return;", ownershipPredicateIndex, StringComparison.Ordinal)
                : -1;
            // StopServer delegates its cleanup tail to the shared teardown
            // state. The runtime Stop method group must remain after the only
            // early return, whose predicate now preserves retired-session
            // ownership instead of looking only at the active Session slot.
            var runtimeStopIndex = stopServerSource.IndexOf("_runtime.Stop,", StringComparison.Ordinal);
            var teardownHelperIndex = stopServerSource.IndexOf(
                "FoxgloveManagerTeardownState.RunStopServer(",
                StringComparison.Ordinal);
            var earlyReturnCount = stopServerSource.Split(
                new[] { "return;" },
                StringSplitOptions.None).Length - 1;
            Check(notRunningIndex >= 0
                  && ownershipPredicateIndex > notRunningIndex
                  && pendingCleanupIndex > ownershipPredicateIndex
                  && earlyReturnIndex > pendingCleanupIndex
                   && teardownHelperIndex > earlyReturnIndex
                   && runtimeStopIndex > teardownHelperIndex
                   && earlyReturnCount == 1,
                "52C-1g3: StopServer still stops runtime so active recordings are finalized even if transport is already stopped");
            var stopIndex = runtimeSource.IndexOf("public void Stop()", StringComparison.Ordinal);
            var nextMethodIndex = runtimeSource.IndexOf("public void RegisterChannel", stopIndex, StringComparison.Ordinal);
            var stopSource = stopIndex >= 0 && nextMethodIndex > stopIndex
                ? runtimeSource.Substring(stopIndex, nextMethodIndex - stopIndex)
                : string.Empty;
            var firstDetachRecordingIndex = stopSource.IndexOf("_recording.DetachFromSession", StringComparison.Ordinal);
            var disposeSessionIndex = stopSource.IndexOf("session?.Dispose()", StringComparison.Ordinal);
            Check(firstDetachRecordingIndex >= 0 &&
                  disposeSessionIndex > firstDetachRecordingIndex &&
                  stopSource.IndexOf("_recording.DetachFromSession", firstDetachRecordingIndex + 1, StringComparison.Ordinal)
                   < 0,
                "52C-1g4: runtime detaches recorder exactly once before disposing the session");

            var startIndex = demoSource.IndexOf("private void Start()", StringComparison.Ordinal);
            var sessionGuardIndex = demoSource.IndexOf("_manager?.Runtime?.Session == null", startIndex, StringComparison.Ordinal);
            if (sessionGuardIndex < 0)
                sessionGuardIndex = demoSource.IndexOf("runtime?.Session == null", startIndex, StringComparison.Ordinal);
            var runtimeCaptureIndex = demoSource.IndexOf("var rt = _manager.Runtime", startIndex, StringComparison.Ordinal);
            if (runtimeCaptureIndex < 0)
                runtimeCaptureIndex = demoSource.IndexOf("var rt = runtime", startIndex, StringComparison.Ordinal);
            Check(startIndex >= 0 && sessionGuardIndex >= 0 && runtimeCaptureIndex >= 0 && sessionGuardIndex < runtimeCaptureIndex,
                "52C-1h: demo setup skips service wiring when manager start failed before session creation");
            Check(editorSource.Contains("Generate Local Dev Certificate")
                  && editorSource.Contains("GenerateLocalDevCertificate"),
                "52C-1i: Inspector exposes one-click local dev certificate generation");
            var connectionSection = PhaseValidationSourceHelpers.SourceMethod(editorMainSource, "private void DrawConnectionSecuritySection");
            var secureToolsSection = PhaseValidationSourceHelpers.SourceMethod(editorSource, "private void DrawSecureWebSocketSection");
            var drawFieldsIndex = connectionSection.IndexOf("DrawSecureWebSocketFields(isSecure);", StringComparison.Ordinal);
            var secureToolsIndex = connectionSection.IndexOf("DrawSecureWebSocketSection(isSecure);", StringComparison.Ordinal);
            var generateButtonIndex = secureToolsSection.IndexOf("GUILayout.Button(\"Generate Local Dev Certificate\")", StringComparison.Ordinal);
            Check(drawFieldsIndex >= 0 && secureToolsIndex > drawFieldsIndex && generateButtonIndex >= 0,
                "52C-1i1: certificate generator button remains enabled before SecureWebSocket mode is selected");
            Check(editorSource.Contains("FoxgloveAppUrl.BuildHostedWebSocketUrl")
                  && editorSource.Contains("Copy Web URL")
                  && editorSource.Contains("Open Web"),
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
    }
}
