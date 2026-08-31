// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Disposable bounded HTTP server for Foxglove Remote Data Loader MCAP access.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Serves one configured MCAP file using the Phase139B Remote Data Loader routes.</summary>
    public sealed class RemoteMcapHttpServer : IDisposable
    {
        private const int MinTcpPort = 1;
        private const int MaxTcpPort = 65535;
        private const int MaxConcurrentRequests = 8;
        private static readonly TimeSpan StartupProbeTimeout = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan DisposeWaitTimeout = TimeSpan.FromMilliseconds(50);

        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stop;
        private readonly Task _loop;
        private bool _disposed;

        private RemoteMcapHttpServer(RemoteMcapHttpOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (options.Port < MinTcpPort || options.Port > MaxTcpPort)
                throw new ArgumentOutOfRangeException(nameof(options.Port), "Remote MCAP HTTP port must be between 1 and 65535.");
            if (string.IsNullOrEmpty(options.McapPath))
                throw new ArgumentException("Remote MCAP HTTP server requires one MCAP path.", nameof(options));
            if (!RemoteMcapHttpOptions.IsLoopbackHost(options.Host)
                && string.IsNullOrWhiteSpace(options.RequiredBearerToken))
            {
                throw new ArgumentException(
                    "Remote MCAP non-loopback hosts require a bearer token before the listener can start.",
                    nameof(options));
            }

            Options = options;
            BaseUrl = options.BaseUrl;

            var source = new RemoteMcapDataSourcePrototype(
                options.McapPath,
                options.SourceId,
                options.ManifestName,
                options.RequiredBearerToken,
                options.MaxInMemoryDataBytes,
                options.DataRoute,
                options.DirectFileRoute);
            var router = new RemoteMcapHttpRouter(source);

            var stop = new CancellationTokenSource();
            HttpListener listener = null;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add(BaseUrl + "/");
                listener.Start();

                _stop = stop;
                _listener = listener;
                _loop = Task.Run(() => ListenLoopAsync(router, _stop.Token));
            }
            catch
            {
                stop.Dispose();
                try { listener?.Close(); } catch { /* best effort after failed start */ }
                throw;
            }
        }

        /// <summary>Options used to start this server.</summary>
        public RemoteMcapHttpOptions Options { get; }

        /// <summary>Normalized listener base URL for manifest and data routes.</summary>
        public string BaseUrl { get; }

        /// <summary>True while the listener has not been disposed and is still accepting connections.</summary>
        public bool IsRunning => !_disposed && _listener.IsListening;

        /// <summary>Starts a disposable Remote Data Loader server.</summary>
        public static RemoteMcapHttpServer Start(RemoteMcapHttpOptions options)
        {
            return new RemoteMcapHttpServer(options);
        }

        /// <summary>
        /// Returns whether a TCP listener appears to be accepting connections at
        /// the base URL. This synchronous probe can block for up to 500 ms; do
        /// not call it from Unity frame-critical paths.
        /// </summary>
        public static bool IsListening(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl))
                return false;

            try
            {
                var uri = new Uri(baseUrl, UriKind.Absolute);
                using (var client = new TcpClient())
                {
                    var connect = client.ConnectAsync(uri.Host, uri.Port);
                    return connect.Wait(StartupProbeTimeout) && client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Asynchronously probes whether a TCP listener appears to be accepting connections at the base URL.</summary>
        public static async Task<bool> IsListeningAsync(string baseUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(baseUrl))
                return false;

            try
            {
                var uri = new Uri(baseUrl, UriKind.Absolute);
                using (var client = new TcpClient())
                {
                    var connect = client.ConnectAsync(uri.Host, uri.Port);
                    var timeout = Task.Delay(StartupProbeTimeout, cancellationToken);
                    if (await Task.WhenAny(connect, timeout).ConfigureAwait(false) != connect)
                        return false;

                    await connect.ConfigureAwait(false);
                    return client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Stops the listener and waits briefly for the request loop to exit.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _stop.Cancel();
            try { _listener.Close(); } catch { /* best effort during shutdown */ }
            try
            {
                if (_loop.Wait(DisposeWaitTimeout))
                {
                    _stop.Dispose();
                }
                else
                {
                    _loop.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
                }
            }
            catch
            {
                // Listener close wakes the loop with an exception; keep shutdown best-effort.
                _loop.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
            }
        }

        private async Task ListenLoopAsync(RemoteMcapHttpRouter router, CancellationToken token)
        {
            var activeRequests = new List<Task>(MaxConcurrentRequests);
            try
            {
                while (!token.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        if (token.IsCancellationRequested || !_listener.IsListening)
                            break;
                        continue;
                    }

                    RemoveCompletedRequests(activeRequests);
                    if (activeRequests.Count >= MaxConcurrentRequests)
                    {
                        RejectBusy(context);
                        continue;
                    }

                    // Do not execute the handler inline on the sole accept-loop
                    // continuation.  /v1/data performs synchronous range
                    // construction before its first await; scheduling it first
                    // makes the request visible to admission accounting and
                    // leaves the listener free to accept the next context.
                    activeRequests.Add(ScheduleRequest(
                        () => HandleRequestAsync(router, context, token)));
                }
            }
            finally
            {
                if (activeRequests.Count > 0)
                {
                    try
                    {
                        await Task.WhenAll(activeRequests).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Every request owns its response cleanup; shutdown remains best effort.
                    }
                }
            }
        }

        private static async Task HandleRequestAsync(
            RemoteMcapHttpRouter router,
            HttpListenerContext context,
            CancellationToken token)
        {
            try
            {
                await router.HandleAsync(context, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                try { context.Response.OutputStream.Close(); } catch { /* disconnected/cancelled */ }
            }
            catch
            {
                try
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.OutputStream.Close();
                }
                catch
                {
                    // If the client disconnected, the request is already over.
                }
            }
        }

        /// <summary>
        /// Schedules one request away from the accept-loop continuation.  Kept
        /// internal so the unit boundary can verify that a synchronously
        /// entering async handler is tracked before it blocks.
        /// </summary>
        internal static Task ScheduleRequest(Func<Task> requestHandler)
        {
            if (requestHandler == null)
                throw new ArgumentNullException(nameof(requestHandler));
            return Task.Run(requestHandler);
        }

        private static void RemoveCompletedRequests(List<Task> requests)
        {
            for (var index = requests.Count - 1; index >= 0; index--)
            {
                if (requests[index].IsCompleted)
                    requests.RemoveAt(index);
            }
        }

        private static void RejectBusy(HttpListenerContext context)
        {
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                context.Response.ContentLength64 = 0;
                context.Response.OutputStream.Close();
            }
            catch
            {
                // A disconnected overload request needs no further cleanup.
            }
        }
    }
}
