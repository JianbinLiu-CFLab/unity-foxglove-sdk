// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: TLS certificate loading and validation options for the
// Unity-native secure WebSocket backend.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Certificate configuration for <see cref="ManagedWssBackend"/>.
    /// The first Phase 52 implementation supports password-protected PFX files.
    /// </summary>
    public sealed class FoxgloveTlsOptions
    {
        /// <summary>Path to a PFX file containing the server certificate and private key.</summary>
        public string CertificatePfxPath { get; set; } = string.Empty;

        /// <summary>Optional PFX password.</summary>
        public string CertificatePassword { get; set; } = string.Empty;

        /// <summary>Load and validate the configured PFX certificate.</summary>
        public X509Certificate2 LoadCertificate()
        {
            if (string.IsNullOrWhiteSpace(CertificatePfxPath))
                throw new InvalidOperationException("WSS certificate PFX path is required.");

            if (!File.Exists(CertificatePfxPath))
                throw new InvalidOperationException($"WSS certificate PFX was not found: {CertificatePfxPath}");

            X509Certificate2 cert;
            try
            {
#pragma warning disable SYSLIB0057 // Unity-compatible PFX loading; X509CertificateLoader is not available on all Unity targets.
                cert = new X509Certificate2(
                    CertificatePfxPath,
                    CertificatePassword ?? string.Empty,
                    X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("WSS certificate PFX could not be loaded. Check the path and password.", ex);
            }

            var now = DateTime.UtcNow;
            if (!cert.HasPrivateKey)
            {
                cert.Dispose();
                throw new InvalidOperationException("WSS certificate PFX must contain a private key.");
            }

            if (now < cert.NotBefore.ToUniversalTime() || now > cert.NotAfter.ToUniversalTime())
            {
                cert.Dispose();
                throw new InvalidOperationException("WSS certificate is not valid at the current time.");
            }

            return cert;
        }
    }
}
