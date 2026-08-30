// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/Security
// Purpose: Minimal HTTP root CA distributor for first-time WSS trust
// bootstrap, with SHA-256 fingerprint display.

using System;
using System.Collections.Generic;
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
        /// <summary>Maximum HTTP headers accepted before the local distributor rejects the request.</summary>
        private const int MaxRequestHeaders = 100;
        private const int MaxConcurrentClients = 10;
        private const int StopAcceptLoopWaitMs = 1000;
        private const int StopClientHandlersWaitMs = 1000;
        private readonly string _rootCaPath;
        private readonly string _rootCaPemPath;
        private readonly IFoxgloveLogger _logger;
        private readonly int _clientIoTimeoutMs;
        private readonly object _lifecycleGate = new object();
        private readonly object _clientGate = new object();
        private readonly HashSet<TcpClient> _activeClients = new HashSet<TcpClient>();
        private readonly ManualResetEventSlim _clientHandlersIdle = new ManualResetEventSlim(true);
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptLoopTask;
        private string _rootCaSha256Fingerprint;
        // The fingerprint and download must be derived from one immutable
        // snapshot for the lifetime of a running distributor. Re-opening the
        // configured path for every request could otherwise serve bytes from a
        // different certificate generation than the page displayed.
        private byte[] _rootCaBytes;
        private int _activeClientHandlers;
        private int _running;
        private bool _acceptingClients;
        private bool _disposeClientHandlersIdleWhenIdle;
        private bool _disposed;

        public FoxgloveCertificateDistributor(
            string rootCaPath,
            string rootCaPemPath = null,
            IFoxgloveLogger logger = null,
            int clientIoTimeoutMs = 5000)
        {
            _rootCaPath = rootCaPath ?? string.Empty;
            _rootCaPemPath = rootCaPemPath ?? string.Empty;
            _logger = logger ?? new ConsoleLogger();
            _clientIoTimeoutMs = Math.Max(1, clientIoTimeoutMs);
        }

        /// <summary>Whether the HTTP listener is currently active.</summary>
        public bool IsRunning => Volatile.Read(ref _running) != 0;

        /// <summary>SHA-256 fingerprint of the configured root CA file.</summary>
        public string RootCaSha256Fingerprint
        {
            get
            {
                var cached = Volatile.Read(ref _rootCaSha256Fingerprint);
                if (cached != null)
                    return cached;

                var computed = ComputeSha256Fingerprint(_rootCaPath);
                Interlocked.CompareExchange(ref _rootCaSha256Fingerprint, computed, null);
                return _rootCaSha256Fingerprint;
            }
        }

        /// <summary>Start serving the configured root CA file.</summary>
        public void Start(string host, int port)
        {
            lock (_lifecycleGate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(FoxgloveCertificateDistributor));
                if (_listener != null || Volatile.Read(ref _running) != 0)
                    throw new InvalidOperationException("Certificate distributor already started.");

                if (string.IsNullOrWhiteSpace(_rootCaPath) || !File.Exists(_rootCaPath))
                    throw new InvalidOperationException("Root CA file is required for certificate distribution.");

                var rootCaBytes = ReadFileWithinLimit(_rootCaPath, MaxCertificateFileBytes);
                if (rootCaBytes == null)
                    throw new InvalidOperationException("Root CA file exceeds the certificate distribution size limit.");

                _rootCaSha256Fingerprint = ComputeSha256Fingerprint(rootCaBytes);
                Volatile.Write(ref _rootCaBytes, rootCaBytes);
                var address = TransportHostResolver.ResolveBindAddress(host);
                TcpListener listener = null;
                CancellationTokenSource cts = null;
                try
                {
                    listener = new TcpListener(address, port);
                    listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    cts = new CancellationTokenSource();
                    listener.Start();

                    lock (_clientGate)
                    {
                        if (_activeClientHandlers != 0 || _activeClients.Count != 0)
                            throw new InvalidOperationException("Certificate distributor client shutdown is incomplete.");

                        _acceptingClients = true;
                        _clientHandlersIdle.Set();
                    }

                    _listener = listener;
                    _cts = cts;
                    _acceptLoopTask = Task.Run(() => AcceptLoop(listener, cts.Token));
                    Volatile.Write(ref _running, 1);
                }
                catch
                {
                    Volatile.Write(ref _running, 0);
                    Volatile.Write(ref _rootCaBytes, null);
                    _rootCaSha256Fingerprint = null;
                    _listener = null;
                    _cts = null;
                    _acceptLoopTask = null;
                    lock (_clientGate)
                    {
                        _acceptingClients = false;
                        if (_activeClientHandlers == 0)
                            _clientHandlersIdle.Set();
                    }

                    try { cts?.Cancel(); } catch { }
                    try { listener?.Stop(); } catch { }
                    cts?.Dispose();
                    throw;
                }
            }
        }

        /// <summary>Stop accepting requests and release the listener port.</summary>
        public void Stop()
        {
            lock (_lifecycleGate)
            {
                if (_disposed)
                    return;

                StopNoLock();
            }
        }

        /// <summary>Stop the listener and release resources.</summary>
        public void Dispose()
        {
            var disposeClientHandlersIdle = false;
            lock (_lifecycleGate)
            {
                if (_disposed)
                    return;

                _ = StopNoLock();
                _disposed = true;
                lock (_clientGate)
                {
                    if (_activeClientHandlers == 0)
                    {
                        disposeClientHandlersIdle = true;
                    }
                    else
                    {
                        _disposeClientHandlersIdleWhenIdle = true;
                    }
                }
            }

            if (disposeClientHandlersIdle)
                _clientHandlersIdle.Dispose();
        }

        /// <summary>Compute a colon-separated SHA-256 fingerprint for a file.</summary>
        public static string ComputeSha256Fingerprint(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return string.Empty;

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", ":");
        }

        private bool StopNoLock()
        {
            Volatile.Write(ref _running, 0);
            var cts = _cts;
            _cts = null;
            var listener = _listener;
            _listener = null;

            TcpClient[] activeClients;
            lock (_clientGate)
            {
                // Close registration before taking the active-client snapshot. An
                // accept that raced cancellation can no longer reset the idle event.
                _acceptingClients = false;
                activeClients = new TcpClient[_activeClients.Count];
                _activeClients.CopyTo(activeClients);
            }

            try { cts?.Cancel(); } catch { }
            try { listener?.Stop(); } catch { }
            foreach (var client in activeClients)
            {
                try { client.Dispose(); } catch { }
            }

            WaitForShutdownTask(_acceptLoopTask, StopAcceptLoopWaitMs);
            _acceptLoopTask = null;

            // Accepted clients are actively closed above. Wait until every handler
            // has executed its finally block before shared synchronization is reused
            // by Start or disposed by Dispose.
            var handlersIdle = _clientHandlersIdle.Wait(
                StopClientHandlersWaitMs);
            if (!handlersIdle)
            {
                _logger.LogWarning(
                    "Certificate distributor client handlers did not stop within " +
                    StopClientHandlersWaitMs +
                    " ms; their synchronization remains owned until final exit.");
            }
            cts?.Dispose();
            Volatile.Write(ref _rootCaBytes, null);
            _rootCaSha256Fingerprint = null;
            return handlersIdle;
        }

        private async Task AcceptLoop(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    if (ct.IsCancellationRequested)
                    {
                        try { client.Dispose(); } catch { }
                        break;
                    }

                    if (!TryRegisterClient(client, ct, out var clientLimitReached))
                    {
                        if (clientLimitReached)
                        {
                            _logger.LogWarning(
                                $"Rejected certificate distributor client because active client limit {MaxConcurrentClients} is reached.");
                        }
                        try { client.Dispose(); } catch { }
                        continue;
                    }

                    try
                    {
                        _ = Task.Run(() =>
                        {
                            try { HandleClient(client, ct); }
                            finally { CompleteClientHandler(client); }
                        });
                    }
                    catch
                    {
                        CompleteClientHandler(client);
                        try { client.Dispose(); } catch { }
                        throw;
                    }
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested) { break; }
                catch (NullReferenceException) when (ct.IsCancellationRequested) { break; }
                catch (Exception) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.LogError($"Certificate distributor accept error: {ex.Message}");
                }
            }
        }

        private bool TryRegisterClient(
            TcpClient client,
            CancellationToken ct,
            out bool clientLimitReached)
        {
            clientLimitReached = false;
            lock (_clientGate)
            {
                if (!_acceptingClients || ct.IsCancellationRequested)
                    return false;

                if (Interlocked.Increment(ref _activeClientHandlers) > MaxConcurrentClients)
                {
                    clientLimitReached = true;
                    if (Interlocked.Decrement(ref _activeClientHandlers) == 0)
                        _clientHandlersIdle.Set();
                    return false;
                }

                _activeClients.Add(client);
                _clientHandlersIdle.Reset();
                return true;
            }
        }

        private void CompleteClientHandler(TcpClient client)
        {
            var disposeClientHandlersIdle = false;
            lock (_clientGate)
            {
                _activeClients.Remove(client);
                if (Interlocked.Decrement(ref _activeClientHandlers) == 0)
                {
                    _clientHandlersIdle.Set();
                    if (_disposeClientHandlersIdleWhenIdle)
                    {
                        _disposeClientHandlersIdleWhenIdle = false;
                        disposeClientHandlersIdle = true;
                    }
                }
            }

            if (disposeClientHandlersIdle)
                _clientHandlersIdle.Dispose();
        }

        private void HandleClient(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    stream.ReadTimeout = _clientIoTimeoutMs;
                    stream.WriteTimeout = _clientIoTimeoutMs;
                    var requestLine = ReadLine(stream, MaxRequestLineBytes);
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(requestLine))
                        return;

                    DrainHeaders(stream);
                    ct.ThrowIfCancellationRequested();
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
                        WriteFile(stream, Volatile.Read(ref _rootCaBytes), "application/x-x509-ca-cert");
                        return;
                    }

                    if (parts[1] == "/rootCA.pem" && !string.IsNullOrWhiteSpace(_rootCaPemPath))
                    {
                        WriteFile(stream, _rootCaPemPath, "application/x-pem-file");
                        return;
                    }

                    WriteText(stream, "404 Not Found", "text/plain", "Not found.");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
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

            var bytes = ReadFileWithinLimit(path, MaxCertificateFileBytes);
            if (bytes == null)
            {
                WriteText(stream, "413 Payload Too Large", "text/plain", "Certificate file is too large.");
                return;
            }

            WriteHeader(stream, "200 OK", contentType, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static void WriteFile(Stream stream, byte[] bytes, string contentType)
        {
            if (bytes == null)
            {
                WriteText(stream, "404 Not Found", "text/plain", "File not found.");
                return;
            }

            WriteHeader(stream, "200 OK", contentType, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static byte[] ReadFileWithinLimit(string path, int maxBytes)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                var read = file.Read(chunk, 0, chunk.Length);
                if (read == 0)
                    return buffer.ToArray();

                if (buffer.Length + read > maxBytes)
                    return null;

                buffer.Write(chunk, 0, read);
            }
        }

        private static string ComputeSha256Fingerprint(byte[] bytes)
        {
            if (bytes == null)
                return string.Empty;

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", ":");
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

        private static void DrainHeaders(Stream stream)
        {
            var headerCount = 0;
            while (true)
            {
                var line = ReadLine(stream, MaxRequestLineBytes);
                if (string.IsNullOrEmpty(line))
                    return;

                headerCount++;
                if (headerCount > MaxRequestHeaders)
                    throw new InvalidDataException("HTTP request contains too many headers.");
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
                    if (next >= 0)
                    {
                        bytesRead++;
                        if (bytesRead > maxBytes)
                            throw new InvalidDataException("HTTP request line exceeds maximum length.");
                    }

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

        private void WaitForShutdownTask(Task task, int timeoutMs)
        {
            if (task == null)
                return;

            try
            {
                task.Wait(Math.Max(0, timeoutMs));
            }
            catch (AggregateException ex)
            {
                foreach (var inner in ex.InnerExceptions)
                {
                    if (inner is OperationCanceledException
                        || inner is ObjectDisposedException
                        || inner is SocketException)
                        continue;

                    _logger.LogError($"Certificate distributor shutdown error: {inner.Message}");
                    break;
                }
            }
            catch (ObjectDisposedException) { }
        }
    }
}
