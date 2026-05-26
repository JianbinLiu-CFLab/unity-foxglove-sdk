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
            _serverCertificate?.Dispose();
            _serverCertificate = _tlsOptions.LoadCertificate();
            base.Start(host, port);
        }

        /// <summary>Dispose the active certificate after stopping the listener.</summary>
        public override void Dispose()
        {
            base.Dispose();
            _serverCertificate?.Dispose();
            _serverCertificate = null;
        }

        /// <summary>Authenticate the accepted TCP stream as a TLS server stream.</summary>
        protected override Stream CreateClientStream(TcpClient tcpClient)
        {
            var sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);
            sslStream.AuthenticateAsServer(
                _serverCertificate,
                clientCertificateRequired: false,
                enabledSslProtocols: SslProtocols.None,
                checkCertificateRevocation: false);
            return sslStream;
        }
    }
}
