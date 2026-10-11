// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first real-stream checks for the bounded duplex connection.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed partial class Ros2BridgeConnectionTests
    {
        private sealed class LoopbackPeer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Thread _thread;
            private readonly ManualResetEventSlim _done =
                new ManualResetEventSlim(false);
            private Exception _error;
            private int _connections;

            private LoopbackPeer(Action<Stream> behavior, int maxConnections)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start(1);
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _thread = new Thread(() =>
                {
                    try
                    {
                        for (var attempt = 1; ; attempt++)
                        {
                            using var client = _listener.AcceptTcpClient();
                            Interlocked.Increment(ref _connections);
                            try
                            {
                                using var stream = client.GetStream();
                                behavior(stream);
                                break;
                            }
                            catch (IOException) when (attempt < maxConnections)
                            {
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        _error = exception;
                    }
                    finally
                    {
                        _done.Set();
                    }
                })
                {
                    IsBackground = true,
                    Name = "Phase186E loopback peer",
                };
                _thread.Start();
            }

            internal int Port { get; }

            internal int Connections => Volatile.Read(ref _connections);

            internal static LoopbackPeer Start(
                Action<Stream> behavior)
                => new LoopbackPeer(behavior, maxConnections: 1);

            /// <summary>
            /// Restarts the scripted conversation on a new connection when the client
            /// drops the current one, as an auto-reconnecting runtime does after a
            /// handshake timeout.
            /// </summary>
            internal static LoopbackPeer StartAcceptingReconnects(
                Action<Stream> behavior)
                => new LoopbackPeer(behavior, maxConnections: 5);

            internal void AssertCompleted()
            {
                Assert.True(_done.Wait(TimeSpan.FromSeconds(3)));
                _thread.Join(TimeSpan.FromSeconds(1));
                Assert.Null(_error);
            }

            public void Dispose()
            {
                _listener.Stop();
                _done.Wait(TimeSpan.FromSeconds(1));
                _thread.Join(TimeSpan.FromSeconds(1));
                _done.Dispose();
            }
        }
    }
}
