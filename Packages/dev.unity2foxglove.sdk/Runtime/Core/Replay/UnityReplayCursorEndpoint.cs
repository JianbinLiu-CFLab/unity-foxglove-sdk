// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Optional loopback HTTP endpoint for Foxglove extension cursor metadata.

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>Runtime options for the optional Unity replay cursor endpoint.</summary>
    public readonly struct UnityReplayCursorEndpointOptions
    {
        /// <summary>Default disabled, loopback-only endpoint settings.</summary>
        public static readonly UnityReplayCursorEndpointOptions Default =
            new UnityReplayCursorEndpointOptions(false, "127.0.0.1", 8892, "/v1/replay-cursor", string.Empty, 2048);

        /// <summary>Whether the endpoint should listen.</summary>
        public bool Enabled { get; }

        /// <summary>Host to bind. Defaults to loopback.</summary>
        public string Host { get; }

        /// <summary>TCP port to bind.</summary>
        public int Port { get; }

        /// <summary>HTTP path accepted by the endpoint.</summary>
        public string Path { get; }

        /// <summary>Optional bearer token. Empty disables token checks.</summary>
        public string BearerToken { get; }

        /// <summary>Maximum accepted request body size in bytes.</summary>
        public int MaxBodyBytes { get; }

        /// <summary>Create endpoint options.</summary>
        public UnityReplayCursorEndpointOptions(
            bool enabled,
            string host,
            int port,
            string path,
            string bearerToken,
            int maxBodyBytes)
        {
            Enabled = enabled;
            Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            Port = port;
            Path = NormalizePath(path);
            BearerToken = bearerToken ?? string.Empty;
            MaxBodyBytes = maxBodyBytes > 0 ? maxBodyBytes : Default.MaxBodyBytes;
        }

        /// <summary>Return true when this host is safe for the default loopback-only endpoint.</summary>
        public bool IsLoopbackAllowedHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/v1/replay-cursor";
            }

            return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        }
    }

    /// <summary>Result returned by the runtime queue handler used by the endpoint.</summary>
    public readonly struct UnityReplayCursorEndpointQueueResult
    {
        /// <summary>Whether the cursor was accepted or intentionally ignored as a duplicate.</summary>
        public bool Success { get; }

        /// <summary>Human-readable status message.</summary>
        public string Message { get; }

        /// <summary>Create a queue result.</summary>
        public UnityReplayCursorEndpointQueueResult(bool success, string message)
        {
            Success = success;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// Small loopback HTTP server for Phase139D cursor metadata. The endpoint
    /// never touches Unity objects directly; it only validates and forwards
    /// parsed requests to the runtime-owned queue handler.
    /// </summary>
    public sealed class UnityReplayCursorEndpoint : IDisposable
    {
        private readonly IFoxgloveLogger _logger;
        private HttpListener _listener;
        private Func<ReplayCursorRequest, UnityReplayCursorEndpointQueueResult> _queue;
        private volatile bool _running;
        private UnityReplayCursorEndpointOptions _options;

        /// <summary>Create an endpoint with an optional logger.</summary>
        public UnityReplayCursorEndpoint(IFoxgloveLogger logger = null)
        {
            _logger = logger;
        }

        /// <summary>Whether the endpoint is currently listening.</summary>
        public bool IsRunning => _running;

        /// <summary>Start listening if options are enabled.</summary>
        public void Start(
            UnityReplayCursorEndpointOptions options,
            Func<ReplayCursorRequest, UnityReplayCursorEndpointQueueResult> queue)
        {
            Stop();
            if (!options.Enabled)
            {
                return;
            }

            if (!options.IsLoopbackAllowedHost(options.Host))
            {
                throw new InvalidOperationException("Replay cursor endpoint only supports loopback hosts by default.");
            }

            if (queue == null)
            {
                throw new ArgumentNullException(nameof(queue));
            }

            _options = options;
            _queue = queue;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://{options.Host}:{options.Port}/");
            _listener.Start();
            _running = true;
            ThreadPool.QueueUserWorkItem(_ => ListenLoop());
        }

        /// <summary>Stop listening and release the socket.</summary>
        public void Stop()
        {
            _running = false;
            var listener = _listener;
            _listener = null;
            _queue = null;
            if (listener == null)
            {
                return;
            }

            try
            {
                listener.Stop();
            }
            catch
            {
                // Stop is best-effort during Unity lifecycle teardown.
            }

            listener.Close();
        }

        private void ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext context = null;
                try
                {
                    context = _listener?.GetContext();
                    if (context != null)
                    {
                        Handle(context);
                    }
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("Replay cursor endpoint request failed: " + ex.Message);
                    if (context != null)
                    {
                        TryWrite(context, 500, "{\"error\":\"internal error\"}");
                    }
                }
            }
        }

        private void Handle(HttpListenerContext context)
        {
            if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
            {
                TryWrite(context, 403, "{\"error\":\"loopback only\"}");
                return;
            }

            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.Request.Url.AbsolutePath, _options.Path, StringComparison.Ordinal))
            {
                TryWrite(context, 404, "{\"error\":\"not found\"}");
                return;
            }

            if (!IsAuthorized(context.Request))
            {
                TryWrite(context, 401, "{\"error\":\"unauthorized\"}");
                return;
            }

            if (context.Request.ContentLength64 > _options.MaxBodyBytes)
            {
                TryWrite(context, 413, "{\"error\":\"request body too large\"}");
                return;
            }

            var body = ReadBody(context.Request);
            if (body == null)
            {
                TryWrite(context, 413, "{\"error\":\"request body too large\"}");
                return;
            }

            if (!ReplayCursorRequest.TryParseJson(body, out var request, out var error))
            {
                TryWrite(context, 400, "{\"error\":\"" + Escape(error) + "\"}");
                return;
            }

            var result = _queue?.Invoke(request) ?? new UnityReplayCursorEndpointQueueResult(false, "Cursor queue is unavailable.");
            TryWrite(
                context,
                result.Success ? 202 : 409,
                "{\"accepted\":" + (result.Success ? "true" : "false") + ",\"message\":\"" + Escape(result.Message) + "\"}");
        }

        private bool IsAuthorized(HttpListenerRequest request)
        {
            if (string.IsNullOrEmpty(_options.BearerToken))
            {
                return true;
            }

            return string.Equals(
                request.Headers["Authorization"],
                "Bearer " + _options.BearerToken,
                StringComparison.Ordinal);
        }

        private string ReadBody(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
            var buffer = new char[_options.MaxBodyBytes + 1];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            if (read > _options.MaxBodyBytes)
            {
                return null;
            }

            return new string(buffer, 0, read);
        }

        private static void TryWrite(HttpListenerContext context, int statusCode, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            try
            {
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                context.Response.ContentEncoding = Encoding.UTF8;
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }

        private static string Escape(string value)
            => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>Stop the endpoint.</summary>
        public void Dispose() => Stop();
    }
}
