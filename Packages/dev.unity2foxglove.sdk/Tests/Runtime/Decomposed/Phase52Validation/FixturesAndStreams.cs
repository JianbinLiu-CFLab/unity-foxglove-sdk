// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase192 source section for Phase 52 validation.

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
    public static partial class Phase52Validation
    {
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

            /// <summary>
            /// Validation method for Start.
            /// </summary>
            /// <param name="host">Host address used by the validation client or listener.</param>
            /// <param name="port">TCP port used by the validation client or listener.</param>
            public void Start(string host, int port) { }
            /// <summary>
            /// Validation method for Stop.
            /// </summary>
            public void Stop() { }
            /// <summary>
            /// Validation method for BroadcastText.
            /// </summary>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void BroadcastText(string json) { }
            /// <summary>
            /// Validation method for BroadcastBinary.
            /// </summary>
            /// <param name="data">Binary payload used by the transport stub.</param>
            public void BroadcastBinary(byte[] data) { }
            /// <summary>
            /// Validation method for SendText.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void SendText(uint clientId, string json) { }
            /// <summary>
            /// Validation method for SendBinary.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="data">Binary payload used by the transport stub.</param>
            public void SendBinary(uint clientId, byte[] data) { }
            /// <summary>
            /// Validation method for AddAllowedOrigin.
            /// </summary>
            /// <param name="origin">Reference point for the seek operation.</param>
            public void AddAllowedOrigin(string origin) => AddedOrigin = origin;
            /// <summary>
            /// Validation method for ClearAllowedOrigins.
            /// </summary>
            public void ClearAllowedOrigins() => ClearCalled = true;
            /// <summary>
            /// Validation method for Dispose.
            /// </summary>
            public void Dispose() { }
        }

        private sealed class Phase52CaptureLogger : IFoxgloveLogger
        {
            public int WarningCount { get; private set; }
            public int ErrorCount { get; private set; }
            public string LastWarning { get; private set; }
            public string LastError { get; private set; }

            /// <summary>
            /// Validation method for LogWarning.
            /// </summary>
            /// <param name="message">Diagnostic message recorded by the test double.</param>
            public void LogWarning(string message)
            {
                WarningCount++;
                LastWarning = message ?? string.Empty;
            }

            /// <summary>
            /// Validation method for LogError.
            /// </summary>
            /// <param name="message">Diagnostic message recorded by the test double.</param>
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

            /// <summary>
            /// Validation method for Flush.
            /// </summary>
            public override void Flush() { }

            /// <summary>
            /// Validation method for Read.
            /// </summary>
            /// <param name="buffer">Buffer used by the stream operation.</param>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="count">Number of bytes to read or write.</param>
            /// <returns>The value produced by the validation helper.</returns>
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new AggregateException(new ObjectDisposedException("MobileAuthenticatedStream"));
            }

            /// <summary>
            /// Validation method for Seek.
            /// </summary>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="origin">Reference point for the seek operation.</param>
            /// <returns>The value produced by the validation helper.</returns>
            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            /// <summary>
            /// Validation method for SetLength.
            /// </summary>
            /// <param name="value">Requested value for the stream operation.</param>
            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            /// <summary>
            /// Validation method for Write.
            /// </summary>
            /// <param name="buffer">Buffer used by the stream operation.</param>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="count">Number of bytes to read or write.</param>
            public override void Write(byte[] buffer, int offset, int count) { }
        }

        private sealed class Phase52ThrowingReadStream : Stream
        {
            private readonly Exception _exception;

            public Phase52ThrowingReadStream(Exception exception)
            {
                _exception = exception;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <summary>
            /// Validation method for Flush.
            /// </summary>
            public override void Flush() { }

            /// <summary>
            /// Validation method for Read.
            /// </summary>
            /// <param name="buffer">Buffer used by the stream operation.</param>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="count">Number of bytes to read or write.</param>
            /// <returns>The value produced by the validation helper.</returns>
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw _exception;
            }

            /// <summary>
            /// Validation method for Seek.
            /// </summary>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="origin">Reference point for the seek operation.</param>
            /// <returns>The value produced by the validation helper.</returns>
            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            /// <summary>
            /// Validation method for SetLength.
            /// </summary>
            /// <param name="value">Requested value for the stream operation.</param>
            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            /// <summary>
            /// Validation method for Write.
            /// </summary>
            /// <param name="buffer">Buffer used by the stream operation.</param>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="count">Number of bytes to read or write.</param>
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

            /// <summary>
            /// Validation method for Create.
            /// </summary>
            /// <returns>The value produced by the validation helper.</returns>
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

            /// <summary>
            /// Validation method for Dispose.
            /// </summary>
            public void Dispose()
            {
                try { Directory.Delete(DirectoryPath, recursive: true); } catch { }
            }
        }
    }
}
