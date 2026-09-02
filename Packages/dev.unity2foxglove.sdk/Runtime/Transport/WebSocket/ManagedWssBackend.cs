// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/WebSocket
// Purpose: Unity-native secure WebSocket backend that performs TLS with
// SslStream, then reuses the managed Stream-based WebSocket core.

using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Threading;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// TLS-enabled managed WebSocket backend. This class owns certificate
    /// loading and TLS authentication; frame handling, Origin Guard, token
    /// gating, queues, and stats are inherited from <see cref="ManagedWsBackend"/>.
    /// </summary>
    public class ManagedWssBackend : ManagedWsBackend
    {
        private readonly FoxgloveTlsOptions _tlsOptions;
        private X509Certificate2 _serverCertificate;

        public ManagedWssBackend(
            FoxgloveTlsOptions tlsOptions,
            ManagedWebSocketOptions webSocketOptions = null,
            IFoxgloveLogger logger = null)
            : base(webSocketOptions ?? new ManagedWebSocketOptions(), logger)
        {
            _tlsOptions = tlsOptions ?? throw new System.ArgumentNullException(nameof(tlsOptions));
        }

        /// <summary>Load the configured certificate before opening the listener.</summary>
        public override void Start(string host, int port)
        {
            lock (LifecycleLock)
            {
                // Do not tear down the certificate owned by a live listener
                // before the base lifecycle gate reports the duplicate start.
                // A failed repeated Start must leave the active TLS generation
                // usable for subsequent handshakes.
                if (IsRunning)
                    throw new System.InvalidOperationException("Server already started");

                DisposeServerCertificate();
                _serverCertificate = _tlsOptions.LoadCertificate();
                try
                {
                    base.Start(host, port);
                }
                catch
                {
                    DisposeServerCertificate();
                    throw;
                }
            }
        }

        /// <summary>Stop the listener and release the active server certificate.</summary>
        public override void Stop()
        {
            // ManagedWsBackend owns the lifecycle gate and invokes the derived
            // release hook only after all client/handshake waits complete. Do
            // not hold the gate across those waits: a callback may call Stop
            // reentrantly and a concurrent Start must observe stop-in-progress.
            base.Stop();
        }

        /// <summary>Dispose the active certificate after stopping the listener.</summary>
        public override void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// A pre-handshake capacity rejection cannot write plaintext HTTP to a
        /// TLS client. The base backend closes the socket without a response;
        /// an in-handshake rejection still uses the authenticated SslStream.
        /// </summary>
        protected override bool SupportsPlaintextCapacityResponse => false;

        private void DisposeServerCertificate()
        {
            _serverCertificate?.Dispose();
            _serverCertificate = null;
        }

        protected override void OnStopCompletedUnderLifecycleLock()
        {
            DisposeServerCertificate();
        }

        /// <summary>Authenticate the accepted TCP stream as a TLS server stream.</summary>
        protected override Stream CreateClientStream(TcpClient tcpClient)
            => CreateClientStream(tcpClient, CancellationToken.None);

        /// <summary>Authenticate TLS while honoring the bounded handshake cancellation.</summary>
        protected override Stream CreateClientStream(TcpClient tcpClient, CancellationToken handshakeCancellation)
        {
            var sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);
            using var cancellationRegistration = handshakeCancellation.Register(
                () =>
                {
                    try { sslStream.Dispose(); } catch { }
                });
            try
            {
                // Local development certificates are commonly self-signed and have no CRL/OCSP endpoint.
                sslStream.AuthenticateAsServer(
                    _serverCertificate,
                    clientCertificateRequired: false,
                    enabledSslProtocols: SslProtocols.None,
                    checkCertificateRevocation: false);
                handshakeCancellation.ThrowIfCancellationRequested();
                return sslStream;
            }
            catch
            {
                try { sslStream.Dispose(); } catch { }
                throw;
            }
        }
    }
}
