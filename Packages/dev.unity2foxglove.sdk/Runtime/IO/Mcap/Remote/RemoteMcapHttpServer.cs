// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Disposable loopback HTTP server for Foxglove Remote Data Loader MCAP access.

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Serves one configured MCAP file on loopback using the Phase139B Remote Data Loader routes.</summary>
    public sealed class RemoteMcapHttpServer : IDisposable
    {
        private const int MinTcpPort = 1;
        private const int MaxTcpPort = 65535;
        private static readonly TimeSpan StartupProbeTimeout = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan DisposeWaitTimeout = TimeSpan.FromMilliseconds(50);

        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
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

            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _loop = Task.Run(() => ListenLoopAsync(router, _stop.Token));
        }

        /// <summary>Options used to start this server.</summary>
        public RemoteMcapHttpOptions Options { get; }

        /// <summary>Normalized loopback base URL for manifest and data routes.</summary>
        public string BaseUrl { get; }

        /// <summary>True while the listener has not been disposed and is still accepting connections.</summary>
        public bool IsRunning => !_disposed && _listener.IsListening;

        /// <summary>Starts a disposable loopback Remote Data Loader server.</summary>
        public static RemoteMcapHttpServer Start(RemoteMcapHttpOptions options)
        {
            return new RemoteMcapHttpServer(options);
        }

        /// <summary>Returns whether a TCP listener appears to be accepting connections at the base URL.</summary>
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

        /// <summary>Stops the listener and waits briefly for the request loop to exit.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _stop.Cancel();
            try { _listener.Close(); } catch { /* best effort during shutdown */ }
            try { _loop.Wait(DisposeWaitTimeout); } catch { /* listener close wakes the loop with an exception */ }
            _stop.Dispose();
        }

        private async Task ListenLoopAsync(RemoteMcapHttpRouter router, CancellationToken token)
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

                try
                {
                    await router.HandleAsync(context).ConfigureAwait(false);
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
        }
    }
}
