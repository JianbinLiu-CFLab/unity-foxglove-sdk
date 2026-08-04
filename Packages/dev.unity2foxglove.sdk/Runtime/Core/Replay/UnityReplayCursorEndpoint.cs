// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Optional loopback HTTP endpoint for Foxglove extension cursor metadata.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>Runtime options for the optional Unity replay cursor endpoint.</summary>
    public readonly struct UnityReplayCursorEndpointOptions
    {
        private static readonly string[] DefaultAllowedCorsOrigins =
        {
            "https://app.foxglove.dev",
            "https://studio.foxglove.dev"
        };

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

        /// <summary>Browser origins allowed to call the loopback endpoint.</summary>
        public IReadOnlyList<string> AllowedCorsOrigins { get; }

        /// <summary>Create endpoint options.</summary>
        public UnityReplayCursorEndpointOptions(
            bool enabled,
            string host,
            int port,
            string path,
            string bearerToken,
            int maxBodyBytes)
            : this(enabled, host, port, path, bearerToken, maxBodyBytes, DefaultAllowedCorsOrigins)
        {
        }

        /// <summary>Create endpoint options with an explicit browser-origin allow-list.</summary>
        public UnityReplayCursorEndpointOptions(
            bool enabled,
            string host,
            int port,
            string path,
            string bearerToken,
            int maxBodyBytes,
            IEnumerable<string> allowedCorsOrigins)
        {
            Enabled = enabled;
            Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            Port = port;
            Path = NormalizePath(path);
            BearerToken = bearerToken ?? string.Empty;
            MaxBodyBytes = maxBodyBytes > 0 ? maxBodyBytes : Default.MaxBodyBytes;
            AllowedCorsOrigins = NormalizeAllowedCorsOrigins(allowedCorsOrigins);
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

        private static IReadOnlyList<string> NormalizeAllowedCorsOrigins(IEnumerable<string> origins)
        {
            var result = new List<string>();
            if (origins != null)
            {
                foreach (var origin in origins)
                {
                    if (string.IsNullOrWhiteSpace(origin))
                    {
                        continue;
                    }

                    result.Add(origin.Trim().TrimEnd('/'));
                }
            }

            return result.Count == 0 ? DefaultAllowedCorsOrigins : result;
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
        private const int WorkerJoinTimeoutMilliseconds = 500;

        private static readonly byte[] AcceptedCursorResponseBytes =
            Encoding.UTF8.GetBytes("{\"accepted\":true,\"message\":\"Cursor accepted.\"}");
        private static readonly byte[] DuplicateCursorResponseBytes =
            Encoding.UTF8.GetBytes("{\"accepted\":false,\"message\":\"Duplicate cursor ignored.\"}");

        private readonly struct CorsDecision
        {
            public CorsDecision(bool allowed, string responseOrigin)
            {
                Allowed = allowed;
                ResponseOrigin = responseOrigin ?? string.Empty;
            }

            public bool Allowed { get; }

            public string ResponseOrigin { get; }
        }

        private sealed class WorkerGeneration
        {
            private int _stopRequested;

            public WorkerGeneration(
                HttpListener listener,
                UnityReplayCursorEndpointOptions options,
                Func<ReplayCursorRequest, UnityReplayCursorEndpointQueueResult> queue,
                Func<ReplayCursorState> stateProvider)
            {
                Listener = listener ?? throw new ArgumentNullException(nameof(listener));
                Options = options;
                Queue = queue ?? throw new ArgumentNullException(nameof(queue));
                StateProvider = stateProvider;
            }

            public HttpListener Listener { get; }
            public UnityReplayCursorEndpointOptions Options { get; }
            public Func<ReplayCursorRequest, UnityReplayCursorEndpointQueueResult> Queue { get; }
            public Func<ReplayCursorState> StateProvider { get; }
            public Thread Worker { get; set; }
            public bool StopRequested => Volatile.Read(ref _stopRequested) != 0;

            public void RequestStop() => Interlocked.Exchange(ref _stopRequested, 1);
        }

        private readonly IFoxgloveLogger _logger;
        private readonly object _lifecycleGate = new object();
        private volatile WorkerGeneration _generation;
        private volatile bool _running;

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
            Func<ReplayCursorRequest, UnityReplayCursorEndpointQueueResult> queue,
            Func<ReplayCursorState> stateProvider = null)
        {
            lock (_lifecycleGate)
            {
                StopNoLock();
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

                var listener = new HttpListener();
                listener.Prefixes.Add($"http://{options.Host}:{options.Port}/");
                try
                {
                    listener.Start();
                    var generation = new WorkerGeneration(listener, options, queue, stateProvider);
                    generation.Worker = new Thread(() => ListenLoop(generation))
                    {
                        IsBackground = true,
                        Name = "Unity replay cursor endpoint"
                    };

                    _generation = generation;
                    _running = true;
                    try
                    {
                        generation.Worker.Start();
                    }
                    catch
                    {
                        _running = false;
                        _generation = null;
                        generation.RequestStop();
                        throw;
                    }
                }
                catch
                {
                    try
                    {
                        listener.Close();
                    }
                    catch
                    {
                        // Preserve the original startup failure.
                    }
                    throw;
                }
            }
        }

        /// <summary>Stop listening and release the socket.</summary>
        public void Stop()
        {
            lock (_lifecycleGate)
            {
                StopNoLock();
            }
        }

        private void StopNoLock()
        {
            _running = false;
            var generation = _generation;
            if (generation == null)
            {
                return;
            }

            generation.RequestStop();

            try
            {
                generation.Listener.Stop();
            }
            catch
            {
                // Stop is best-effort during Unity lifecycle teardown.
            }

            try
            {
                generation.Listener.Close();
            }
            catch
            {
                // Close is best-effort during Unity lifecycle teardown.
            }

            if (generation.Worker != null
                && generation.Worker != Thread.CurrentThread
                && generation.Worker.IsAlive
                && !generation.Worker.Join(WorkerJoinTimeoutMilliseconds))
            {
                _logger?.LogWarning(
                    "Replay cursor endpoint worker did not retire within the bounded stop wait.");
            }

            _generation = null;
        }

        private void ListenLoop(WorkerGeneration generation)
        {
            try
            {
                while (!generation.StopRequested)
                {
                    HttpListenerContext context = null;
                    try
                    {
                        context = generation.Listener.GetContext();
                        if (context != null)
                        {
                            if (generation.StopRequested)
                            {
                                return;
                            }

                            Handle(generation, context);
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
                        if (generation.StopRequested)
                        {
                            return;
                        }

                        _logger?.LogWarning("Replay cursor endpoint request failed: " + ex.Message);
                        if (context != null)
                        {
                            var cors = ResolveCors(generation, context.Request);
                            TryWrite(context, 500, "{\"error\":\"internal error\"}", cors);
                        }
                    }
                }
            }
            finally
            {
                if (ReferenceEquals(_generation, generation))
                {
                    _running = false;
                }
            }
        }

        private void Handle(WorkerGeneration generation, HttpListenerContext context)
        {
            var options = generation.Options;
            var cors = ResolveCors(generation, context.Request);
            if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
            {
                TryWrite(context, 403, "{\"error\":\"loopback only\"}", cors);
                return;
            }

            if (!string.Equals(context.Request.Url.AbsolutePath, options.Path, StringComparison.Ordinal))
            {
                TryWrite(context, 404, "{\"error\":\"not found\"}", cors);
                return;
            }

            if (!cors.Allowed)
            {
                TryWrite(context, 403, "{\"error\":\"origin not allowed\"}", cors);
                return;
            }

            if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                TryWrite(context, 204, string.Empty, cors);
                return;
            }

            if (!IsAuthorized(context.Request, options))
            {
                TryWrite(context, 401, "{\"error\":\"unauthorized\"}", cors);
                return;
            }

            if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                if (generation.StopRequested)
                {
                    return;
                }

                var state = generation.StateProvider?.Invoke()
                            ?? ReplayCursorState.Unavailable("Replay cursor state provider is unavailable.");
                TryWrite(context, 200, state.ToJson(), cors);
                return;
            }

            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                TryWrite(context, 405, "{\"error\":\"method not allowed\"}", cors);
                return;
            }

            if (context.Request.ContentLength64 > options.MaxBodyBytes)
            {
                TryWrite(context, 413, "{\"error\":\"request body too large\"}", cors);
                return;
            }

            var body = ReadBody(generation, context.Request);
            if (body == null)
            {
                TryWrite(context, 413, "{\"error\":\"request body too large\"}", cors);
                return;
            }

            if (!ReplayCursorRequest.TryParseJson(body, out var request, out var error))
            {
                TryWrite(context, 400, "{\"error\":" + JsonEscape(error) + "}", cors);
                return;
            }

            if (generation.StopRequested)
            {
                return;
            }

            var result = generation.Queue(request);
            if (result.Success && string.Equals(result.Message, "Cursor accepted.", StringComparison.Ordinal))
            {
                TryWrite(context, 202, AcceptedCursorResponseBytes, cors);
                return;
            }
            if (!result.Success && string.Equals(result.Message, "Duplicate cursor ignored.", StringComparison.Ordinal))
            {
                TryWrite(context, 409, DuplicateCursorResponseBytes, cors);
                return;
            }

            TryWrite(
                context,
                result.Success ? 202 : 409,
                "{\"accepted\":" + (result.Success ? "true" : "false") + ",\"message\":" + JsonEscape(result.Message) + "}",
                cors);
        }

        private static bool IsAuthorized(
            HttpListenerRequest request,
            UnityReplayCursorEndpointOptions options)
        {
            if (string.IsNullOrEmpty(options.BearerToken))
            {
                return true;
            }

            return string.Equals(
                request.Headers["Authorization"],
                "Bearer " + options.BearerToken,
                StringComparison.Ordinal);
        }

        private CorsDecision ResolveCors(
            WorkerGeneration generation,
            HttpListenerRequest request)
        {
            var origin = request?.Headers["Origin"];
            if (string.IsNullOrWhiteSpace(origin))
            {
                return new CorsDecision(true, string.Empty);
            }

            if (!IsCorsOriginAllowed(origin, generation.Options.AllowedCorsOrigins))
            {
                return new CorsDecision(false, string.Empty);
            }

            return new CorsDecision(true, NormalizeOriginForHeader(origin));
        }

        private static bool IsCorsOriginAllowed(
            string origin,
            IReadOnlyList<string> allowedCorsOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                return true;
            }

            if (!TryGetOriginBounds(origin, out var start, out var length))
            {
                return false;
            }

            foreach (var allowedOrigin in allowedCorsOrigins)
            {
                if (length == allowedOrigin.Length
                    && string.Compare(origin, start, allowedOrigin, 0, length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private string ReadBody(
            WorkerGeneration generation,
            HttpListenerRequest request)
        {
            var encoding = request.ContentEncoding ?? Encoding.UTF8;
            var maxBodyBytes = generation.Options.MaxBodyBytes;
            var buffer = ArrayPool<byte>.Shared.Rent(maxBodyBytes + 1);
            try
            {
                var total = 0;
                int read;
                while ((read = request.InputStream.Read(buffer, total, buffer.Length - total)) > 0)
                {
                    total += read;
                    if (total > maxBodyBytes)
                    {
                        return null;
                    }
                }

                return encoding.GetString(buffer, 0, total);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void TryWrite(HttpListenerContext context, int statusCode, string body, CorsDecision cors)
        {
            body ??= string.Empty;
            TryWrite(context, statusCode, Encoding.UTF8.GetBytes(body), cors);
        }

        private void TryWrite(HttpListenerContext context, int statusCode, byte[] bytes, CorsDecision cors)
        {
            bytes ??= Array.Empty<byte>();
            try
            {
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                context.Response.ContentEncoding = Encoding.UTF8;
                if (!string.IsNullOrEmpty(cors.ResponseOrigin))
                {
                    context.Response.Headers["Access-Control-Allow-Origin"] = cors.ResponseOrigin;
                }

                context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                context.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type";
                context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
                context.Response.Headers["Cache-Control"] = "no-store";
                context.Response.ContentLength64 = bytes.Length;
                if (bytes.Length > 0)
                {
                    context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                }
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }

        private static string JsonEscape(string value)
            => JsonConvert.ToString(value ?? string.Empty);

        private static bool TryGetOriginBounds(string origin, out int start, out int length)
        {
            start = 0;
            length = 0;
            if (string.IsNullOrWhiteSpace(origin))
            {
                return false;
            }

            var end = origin.Length - 1;
            while (start <= end && char.IsWhiteSpace(origin[start]))
            {
                start++;
            }

            while (end >= start && char.IsWhiteSpace(origin[end]))
            {
                end--;
            }

            while (end >= start && origin[end] == '/')
            {
                end--;
            }

            if (end < start)
            {
                return false;
            }

            length = end - start + 1;
            return true;
        }

        private static string NormalizeOriginForHeader(string origin)
        {
            if (!TryGetOriginBounds(origin, out var start, out var length))
            {
                return string.Empty;
            }

            return start == 0 && length == origin.Length ? origin : origin.Substring(start, length);
        }

        /// <summary>Stop the endpoint.</summary>
        public void Dispose() => Stop();
    }
}
