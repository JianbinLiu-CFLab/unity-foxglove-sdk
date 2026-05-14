// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/Security
// Purpose: Minimal HTTP root CA distributor for first-time WSS trust
// bootstrap, with SHA-256 fingerprint display.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Tiny HTTP responder that serves a root CA file and an informational
    /// setup page. Trust still depends on comparing the displayed SHA-256
    /// fingerprint before importing the CA.
    /// </summary>
    public sealed class FoxgloveCertificateDistributor : IDisposable
    {
        /// <summary>Maximum root CA file size served by the local setup helper.</summary>
        private const int MaxCertificateFileBytes = 1024 * 1024;
        /// <summary>Maximum HTTP request-line length accepted by the tiny local distributor.</summary>
        private const int MaxRequestLineBytes = 4096;
        private readonly string _rootCaPath;
        private readonly string _rootCaPemPath;
        private readonly IFoxgloveLogger _logger;
        private TcpListener _listener;
        private CancellationTokenSource _cts;

        public FoxgloveCertificateDistributor(string rootCaPath, string rootCaPemPath = null, IFoxgloveLogger logger = null)
        {
            _rootCaPath = rootCaPath ?? string.Empty;
            _rootCaPemPath = rootCaPemPath ?? string.Empty;
            _logger = logger ?? new ConsoleLogger();
        }

        /// <summary>Whether the HTTP listener is currently active.</summary>
        public bool IsRunning => _listener != null;

        /// <summary>SHA-256 fingerprint of the configured root CA file.</summary>
        public string RootCaSha256Fingerprint => ComputeSha256Fingerprint(_rootCaPath);

        /// <summary>Start serving the configured root CA file.</summary>
        public void Start(string host, int port)
        {
            if (_listener != null)
                throw new InvalidOperationException("Certificate distributor already started.");

            if (string.IsNullOrWhiteSpace(_rootCaPath) || !File.Exists(_rootCaPath))
                throw new InvalidOperationException("Root CA file is required for certificate distribution.");

            var address = ResolveBindAddress(host);
            _listener = new TcpListener(address, port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _cts = new CancellationTokenSource();
            _listener.Start();
            _ = Task.Run(() => AcceptLoop(_cts.Token));
        }

        /// <summary>Stop accepting requests and release the listener port.</summary>
        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        /// <summary>Stop the listener and release resources.</summary>
        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _cts = null;
        }

        /// <summary>Compute a colon-separated SHA-256 fingerprint for a file.</summary>
        public static string ComputeSha256Fingerprint(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return string.Empty;

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(File.ReadAllBytes(path));
            return BitConverter.ToString(hash).Replace("-", ":");
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClient(client, ct), ct);
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested) { break; }
                catch (Exception) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.LogError($"Certificate distributor accept error: {ex.Message}");
                }
            }
        }

        private void HandleClient(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                try
                {
                    stream.ReadTimeout = 5000;
                    stream.WriteTimeout = 5000;
                    var requestLine = ReadLine(stream, MaxRequestLineBytes);
                    if (string.IsNullOrEmpty(requestLine))
                        return;

                    DrainHeaders(stream);
                    var parts = requestLine.Split(' ');
                    if (parts.Length < 2 || parts[0] != "GET")
                    {
                        WriteText(stream, "405 Method Not Allowed", "text/plain", "Only GET is supported.");
                        return;
                    }

                    if (parts[1] == "/" || parts[1].StartsWith("/?", StringComparison.Ordinal))
                    {
                        WriteText(stream, "200 OK", "text/html; charset=utf-8", BuildRootPage());
                        return;
                    }

                    if (parts[1] == "/rootCA.crt")
                    {
                        WriteFile(stream, _rootCaPath, "application/x-x509-ca-cert");
                        return;
                    }

                    if (parts[1] == "/rootCA.pem" && !string.IsNullOrWhiteSpace(_rootCaPemPath))
                    {
                        WriteFile(stream, _rootCaPemPath, "application/x-pem-file");
                        return;
                    }

                    WriteText(stream, "404 Not Found", "text/plain", "Not found.");
                }
                catch (IOException) { }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    _logger.LogError($"Certificate distributor client error: {ex.Message}");
                }
            }
        }

        private string BuildRootPage()
        {
            var fingerprint = RootCaSha256Fingerprint;
            return "<!doctype html><html><head><meta charset=\"utf-8\"><title>Unity2Foxglove Root CA</title></head>"
                + "<body><h1>Unity2Foxglove Root CA</h1>"
                + "<p>Download the root CA only if you trust this Unity process.</p>"
                + "<p>Verify this SHA-256 fingerprint before importing:</p>"
                + $"<pre>{fingerprint}</pre>"
                + "<p><a href=\"/rootCA.crt\">Download rootCA.crt</a></p>"
                + "</body></html>";
        }

        private static void WriteFile(Stream stream, string path, string contentType)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                WriteText(stream, "404 Not Found", "text/plain", "File not found.");
                return;
            }

            var info = new FileInfo(path);
            if (info.Length > MaxCertificateFileBytes)
            {
                WriteText(stream, "413 Payload Too Large", "text/plain", "Certificate file is too large.");
                return;
            }

            var bytes = File.ReadAllBytes(path);
            WriteHeader(stream, "200 OK", contentType, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static void WriteText(Stream stream, string status, string contentType, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            WriteHeader(stream, status, contentType, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static void WriteHeader(Stream stream, string status, string contentType, int contentLength)
        {
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\n"
                + $"Content-Type: {contentType}\r\n"
                + $"Content-Length: {contentLength}\r\n"
                + "Connection: close\r\n"
                + "\r\n");
            stream.Write(header, 0, header.Length);
        }

        /// <summary>Resolve the configured bind host without using DNS for loopback aliases.</summary>
        private static IPAddress ResolveBindAddress(string host)
        {
            if (string.IsNullOrWhiteSpace(host) ||
                string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "127.0.0.1", StringComparison.Ordinal))
                return IPAddress.Loopback;

            if (string.Equals(host, "0.0.0.0", StringComparison.Ordinal))
                return IPAddress.Any;

            if (string.Equals(host, "::", StringComparison.Ordinal))
                return IPAddress.IPv6Any;

            return IPAddress.Parse(host);
        }

        private static void DrainHeaders(Stream stream)
        {
            while (true)
            {
                var line = ReadLine(stream, MaxRequestLineBytes);
                if (string.IsNullOrEmpty(line))
                    return;
            }
        }

        private static string ReadLine(Stream stream, int maxBytes)
        {
            var sb = new StringBuilder();
            var bytesRead = 0;
            while (true)
            {
                var b = stream.ReadByte();
                if (b < 0)
                    return sb.Length > 0 ? sb.ToString() : null;

                bytesRead++;
                if (bytesRead > maxBytes)
                    throw new InvalidDataException("HTTP request line exceeds maximum length.");

                if (b == '\r')
                {
                    var next = stream.ReadByte();
                    if (next == '\n')
                        break;
                    if (next >= 0)
                        sb.Append((char)next);
                }
                else if (b == '\n')
                {
                    break;
                }
                else
                {
                    sb.Append((char)b);
                }
            }

            return sb.ToString();
        }
    }
}
