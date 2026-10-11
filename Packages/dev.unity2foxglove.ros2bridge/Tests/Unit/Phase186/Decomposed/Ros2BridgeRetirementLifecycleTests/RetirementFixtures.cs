// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186/Decomposed/Ros2BridgeRetirementLifecycleTests
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed partial class Ros2BridgeRetirementLifecycleTests
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakRetirementFixture CreateTimedOutWeakReferenceFixture()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sink = ControlledSink.BlockedConnect();
            var manager = new object();
            var provider = new object();
            var callback = new object();
            Func<IRos2BridgeSink> factory = () =>
            {
                GC.KeepAlive(manager);
                GC.KeepAlive(provider);
                GC.KeepAlive(callback);
                return sink;
            };
            var fixture = new WeakRetirementFixture(
                new WeakReference(manager),
                new WeakReference(provider),
                new WeakReference(callback),
                owner,
                sink);
            var runtime = Runtime(
                owner,
                factory,
                generation: 55,
                joinTimeoutMs: 20);
            runtime.Start(true, true);
            Wait(sink.ConnectEntered, "weak-reference worker did not enter Connect");
            runtime.Dispose();
            return fixture;
        }

        private static void ForceCollection()
        {
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private static bool IsQueue(Type type)
            => type.IsGenericType
               && type.GetGenericTypeDefinition() == typeof(Queue<>);

        private static Ros2BridgeRuntime Runtime(
            FoxRunTransportRetirementOwner owner,
            Func<IRos2BridgeSink> sinkFactory,
            ulong generation,
            int joinTimeoutMs = 250,
            int port = 19484,
            int sendTimeoutMs = 100)
            => new Ros2BridgeRuntime(
                "127.0.0.1",
                port,
                queueCapacity: 8,
                reconnectIntervalMs: 10,
                sendTimeoutMs,
                sinkFactory,
                owner,
                ProviderId,
                FoxRunTransportDirection.Publish,
                generation,
                joinTimeoutMs);

        private static Ros2BridgeFrame Frame(
            string topic,
            ulong sequence)
            => Ros2BridgeFrame.CreateOwned(
                topic,
                "phase186_msgs/msg/Lifecycle",
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: sequence,
                sequence,
                payload: new byte[] { 0x00 });

        private static readonly TimeSpan RealTcpWaitTimeout =
            TimeSpan.FromSeconds(5);

        private static void Wait(
            ManualResetEventSlim signal,
            string failure,
            TimeSpan? timeout = null)
        {
            Assert.True(signal.Wait(timeout ?? TimeSpan.FromSeconds(2)), failure);
        }

        private static bool IsPeerClose(IOException exception)
        {
            return exception.InnerException is SocketException socket
                   && (socket.SocketErrorCode == SocketError.ConnectionReset
                       || socket.SocketErrorCode == SocketError.ConnectionAborted
                       || socket.SocketErrorCode == SocketError.Shutdown);
        }

        private sealed class ControlledSink : IRos2BridgeSink
        {
            private readonly bool _blockConnect;
            private readonly bool _blockSend;
            private readonly bool _failConnect;
            private int _failSend;
            private int _connected;
            private int _connectCount;
            private int _sendCount;
            private int _disconnectCount;
            private int _disposeCount;

            private ControlledSink(
                bool blockConnect,
                bool failConnect = false,
                bool failSend = false,
                bool blockSend = false)
            {
                _blockConnect = blockConnect;
                _blockSend = blockSend;
                _failConnect = failConnect;
                _failSend = failSend ? 1 : 0;
            }

            internal static ControlledSink Healthy() => new ControlledSink(false);
            internal static ControlledSink BlockedConnect() => new ControlledSink(true);
            internal static ControlledSink BlockedSend()
                => new ControlledSink(false, blockSend: true);
            internal static ControlledSink FailingConnect()
                => new ControlledSink(false, failConnect: true);
            internal static ControlledSink FailingSend()
                => new ControlledSink(false, failSend: true);

            internal ManualResetEventSlim ConnectEntered { get; } =
                new ManualResetEventSlim(false);
            internal ManualResetEventSlim ReleaseConnect { get; } =
                new ManualResetEventSlim(false);
            internal ManualResetEventSlim SendEntered { get; } =
                new ManualResetEventSlim(false);
            internal ManualResetEventSlim ReleaseSend { get; } =
                new ManualResetEventSlim(false);

            public bool IsConnected => Volatile.Read(ref _connected) != 0;
            internal int ConnectCount => Volatile.Read(ref _connectCount);
            internal int SendCount => Volatile.Read(ref _sendCount);
            internal int DisconnectCount => Volatile.Read(ref _disconnectCount);
            internal int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Connect(string host, int port, int timeoutMs)
            {
                Interlocked.Increment(ref _connectCount);
                ConnectEntered.Set();
                if (_failConnect)
                    throw new InvalidOperationException("test connect failure");
                if (_blockConnect
                    && !ReleaseConnect.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("test connect release timed out");
                }
                Volatile.Write(ref _connected, 1);
            }

            public void Send(Ros2BridgeFrame frame, int timeoutMs)
            {
                Interlocked.Increment(ref _sendCount);
                SendEntered.Set();
                if (_blockSend
                    && !ReleaseSend.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("test send release timed out");
                }
                if (Interlocked.Exchange(ref _failSend, 0) != 0)
                    throw new InvalidOperationException("test writer failure");
            }

            public void Disconnect()
            {
                Interlocked.Increment(ref _disconnectCount);
                Volatile.Write(ref _connected, 0);
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
                Volatile.Write(ref _connected, 0);
            }
        }

        private sealed class BlockedPreparationSink :
            IRos2BridgeSink,
            IRos2BridgePublisherPreparationTransport
        {
            private int _connected;
            private int _requestLength;

            internal ManualResetEventSlim ConnectEntered { get; } =
                new ManualResetEventSlim(false);
            internal ManualResetEventSlim ExchangeEntered { get; } =
                new ManualResetEventSlim(false);
            internal ManualResetEventSlim ReleaseExchange { get; } =
                new ManualResetEventSlim(false);
            internal int RequestLength => Volatile.Read(ref _requestLength);
            public bool IsConnected => Volatile.Read(ref _connected) != 0;

            public void Connect(string host, int port, int timeoutMs)
            {
                Volatile.Write(ref _connected, 1);
                ConnectEntered.Set();
            }

            public void Send(Ros2BridgeFrame frame, int timeoutMs)
            {
            }

            public byte[] ExchangePublisherPreparation(
                byte[] request,
                int timeoutMs)
            {
                Volatile.Write(ref _requestLength, request.Length);
                ExchangeEntered.Set();
                if (!ReleaseExchange.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "test preparation release timed out");
                }

                var parsed =
                    Ros2BridgePublisherPreparationCodec.ParseRequest(request);
                return Ros2BridgePublisherPreparationCodec.WriteResponseForTests(
                    parsed.RequestId,
                    "ok");
            }

            public void Disconnect()
                => Volatile.Write(ref _connected, 0);

            public void Dispose()
                => Volatile.Write(ref _connected, 0);
        }

        private sealed class ObservedTcpPreparationSink :
            IRos2BridgeSink,
            IRos2BridgePublisherPreparationTransport
        {
            private readonly Ros2BridgeTcpClient _inner =
                new Ros2BridgeTcpClient();
            private int _activeExchanges;
            private int _disconnectCount;
            private int _disposeCount;
            private int _disposedWhileExchangeActive;

            internal ManualResetEventSlim ExchangeEntered { get; } =
                new ManualResetEventSlim(false);
            internal ManualResetEventSlim ExchangeExited { get; } =
                new ManualResetEventSlim(false);
            internal int DisconnectCount =>
                Volatile.Read(ref _disconnectCount);
            internal int DisposeCount => Volatile.Read(ref _disposeCount);
            internal bool DisposedWhileExchangeActive =>
                Volatile.Read(ref _disposedWhileExchangeActive) != 0;
            public bool IsConnected => _inner.IsConnected;

            public void Connect(string host, int port, int timeoutMs)
                => _inner.Connect(host, port, timeoutMs);

            public void Send(Ros2BridgeFrame frame, int timeoutMs)
                => _inner.Send(frame, timeoutMs);

            public byte[] ExchangePublisherPreparation(
                byte[] request,
                int timeoutMs)
            {
                Interlocked.Increment(ref _activeExchanges);
                ExchangeEntered.Set();
                try
                {
                    return _inner.ExchangePublisherPreparation(
                        request,
                        timeoutMs);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeExchanges);
                    ExchangeExited.Set();
                }
            }

            public void Disconnect()
            {
                Interlocked.Increment(ref _disconnectCount);
                _inner.Disconnect();
            }

            public void Dispose()
            {
                if (Volatile.Read(ref _activeExchanges) != 0)
                    Volatile.Write(ref _disposedWhileExchangeActive, 1);
                Interlocked.Increment(ref _disposeCount);
                _inner.Dispose();
            }
        }

        private sealed class PeerCloseReconnectTcpSink : IRos2BridgeSink
        {
            private readonly Ros2BridgeTcpClient _inner =
                new Ros2BridgeTcpClient();
            private int _connectCount;
            private int _disconnectCount;
            private int _disposeCount;

            internal ManualResetEventSlim PlannedReconnectFailure { get; } =
                new ManualResetEventSlim(false);
            internal ManualResetEventSlim ReconnectBlocked { get; } =
                new ManualResetEventSlim(false);
            internal ManualResetEventSlim AllowReconnect { get; } =
                new ManualResetEventSlim(false);
            internal int DisconnectCount =>
                Volatile.Read(ref _disconnectCount);
            internal int DisposeCount => Volatile.Read(ref _disposeCount);
            public bool IsConnected => _inner.IsConnected;

            public void Connect(string host, int port, int timeoutMs)
            {
                var attempt = Interlocked.Increment(ref _connectCount);
                if (attempt == 2)
                {
                    PlannedReconnectFailure.Set();
                    throw new SocketException(
                        (int)SocketError.ConnectionRefused);
                }
                if (attempt == 3)
                {
                    ReconnectBlocked.Set();
                    if (!AllowReconnect.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "test reconnect release timed out");
                    }
                }

                _inner.Connect(host, port, Math.Max(timeoutMs, 5000));
            }

            public void Send(Ros2BridgeFrame frame, int timeoutMs)
                => _inner.Send(frame, timeoutMs);

            public void Disconnect()
            {
                Interlocked.Increment(ref _disconnectCount);
                _inner.Disconnect();
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
                _inner.Dispose();
            }
        }

        private sealed class DetachedLeaseProbe : IFoxRunDetachedRetirementLease
        {
            private int _disposeCount;
            internal int DisposeCount => Volatile.Read(ref _disposeCount);
            public void Dispose() => Interlocked.Increment(ref _disposeCount);
        }

        private sealed class BlockingDetachedLease : IFoxRunDetachedRetirementLease
        {
            internal ManualResetEventSlim DisposeEntered { get; } =
                new ManualResetEventSlim(false);
            internal ManualResetEventSlim ReleaseDispose { get; } =
                new ManualResetEventSlim(false);

            public void Dispose()
            {
                DisposeEntered.Set();
                if (!ReleaseDispose.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("test cleanup release timed out");
            }
        }

        private sealed class ThrowingDetachedLease : IFoxRunDetachedRetirementLease
        {
            public void Dispose()
                => throw new InvalidOperationException("cleanup-visible");
        }

        private sealed class WeakRetirementFixture
        {
            internal WeakRetirementFixture(
                WeakReference manager,
                WeakReference provider,
                WeakReference callback,
                FoxRunTransportRetirementOwner owner,
                ControlledSink sink)
            {
                Manager = manager;
                Provider = provider;
                Callback = callback;
                Owner = owner;
                Sink = sink;
            }

            internal WeakReference Manager { get; }
            internal WeakReference Provider { get; }
            internal WeakReference Callback { get; }
            internal FoxRunTransportRetirementOwner Owner { get; }
            internal ControlledSink Sink { get; }
        }
    }
}
