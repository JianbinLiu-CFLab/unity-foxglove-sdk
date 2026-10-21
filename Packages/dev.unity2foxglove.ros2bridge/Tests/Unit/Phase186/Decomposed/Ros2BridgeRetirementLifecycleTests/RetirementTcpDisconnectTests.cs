// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first ownership tests for bounded ROS2 Bridge worker retirement.

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
        [Fact]
        public void RealTcpDisconnectWakesBlockedPreparationBeforeFinalDispose()
        {
            const int joinTimeoutMs = 1000;
            const int ioTimeoutMs = 20000;
            Assert.True(ioTimeoutMs > joinTimeoutMs * 4);
            Assert.True(
                ioTimeoutMs > RealTcpWaitTimeout.TotalMilliseconds * 2,
                "The I/O timeout must remain well above the real TCP rendezvous budget");
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var requestReceived = new ManualResetEventSlim(false);
            var peerClosed = new ManualResetEventSlim(false);
            var serverDone = new ManualResetEventSlim(false);
            Exception serverFailure = null;
            var server = new Thread(() =>
            {
                try
                {
                    using var accepted = listener.AcceptTcpClient();
                    accepted.ReceiveTimeout = ioTimeoutMs + 1000;
                    using var stream = accepted.GetStream();
                    var buffer = new byte[1024];
                    while (true)
                    {
                        int read;
                        try
                        {
                            read = stream.Read(buffer, 0, buffer.Length);
                        }
                        catch (IOException exception)
                            when (IsPeerClose(exception))
                        {
                            peerClosed.Set();
                            break;
                        }

                        if (read == 0)
                        {
                            peerClosed.Set();
                            break;
                        }
                        requestReceived.Set();
                    }
                }
                catch (Exception exception)
                {
                    serverFailure = exception;
                }
                finally
                {
                    serverDone.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Phase186 real TCP wake probe"
            };
            server.Start();

            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sink = new ObservedTcpPreparationSink();
            var runtime = Runtime(
                owner,
                () => sink,
                generation: 33,
                joinTimeoutMs: joinTimeoutMs,
                port: port,
                sendTimeoutMs: ioTimeoutMs);
            try
            {
                runtime.Start(enabled: true, autoConnect: true);
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Pending,
                    runtime.PreparePublisher(
                        "/phase186/real_tcp_wake",
                        "phase186_msgs/msg/RealTcpWake",
                        FoxRunResolvedQos.SensorData,
                        out _));
                Wait(
                    sink.ExchangeEntered,
                    "real TCP preparation exchange did not start",
                    RealTcpWaitTimeout);
                Wait(
                    requestReceived,
                    "loopback peer did not receive the preparation request",
                    RealTcpWaitTimeout);

                var stopwatch = Stopwatch.StartNew();
                runtime.Stop();
                stopwatch.Stop();

                Assert.True(
                    stopwatch.Elapsed < TimeSpan.FromSeconds(2),
                    "Stop exceeded the bounded real TCP wake interval");
                Wait(
                    sink.ExchangeExited,
                    "real TCP preparation exchange did not exit");
                Wait(
                    peerClosed,
                    "loopback peer did not observe TCP close");
                Assert.True(serverDone.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(server.Join(TimeSpan.FromSeconds(5)));
                Assert.Null(serverFailure);
                Assert.Equal(
                    Ros2BridgeRuntimeLifecycleState.Stopped,
                    runtime.LifecycleState);
                Assert.Equal(0, owner.OccupiedCount);
                Assert.Equal(0, owner.RetiredCount);
                Assert.True(sink.DisconnectCount >= 1);
                Assert.Equal(1, sink.DisposeCount);
                Assert.False(sink.DisposedWhileExchangeActive);

                runtime.Dispose();
                Assert.Equal(1, sink.DisposeCount);
            }
            finally
            {
                runtime.Dispose();
                listener.Stop();
                if (server.IsAlive)
                    server.Join(TimeSpan.FromSeconds(6));
            }
        }

        [Fact]
        public void RealTcpPeerCloseReconnectsWithoutInflatingFrameFailures()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(2);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var firstAccepted = new ManualResetEventSlim(false);
            var allowFirstClose = new ManualResetEventSlim(false);
            var firstClosed = new ManualResetEventSlim(false);
            var secondAccepted = new ManualResetEventSlim(false);
            var publishReceived = new ManualResetEventSlim(false);
            var peerClosedAfterStop = new ManualResetEventSlim(false);
            var serverDone = new ManualResetEventSlim(false);
            Exception serverFailure = null;
            var server = new Thread(() =>
            {
                try
                {
                    using (var accepted = listener.AcceptTcpClient())
                    {
                        firstAccepted.Set();
                        if (!allowFirstClose.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "test did not authorize the first peer close");
                        }
                        accepted.Client.LingerState =
                            new LingerOption(enable: true, seconds: 0);
                    }
                    firstClosed.Set();

                    using var replacement = listener.AcceptTcpClient();
                    secondAccepted.Set();
                    replacement.ReceiveTimeout = 6000;
                    using var stream = replacement.GetStream();
                    var buffer = new byte[1024];
                    while (true)
                    {
                        int read;
                        try
                        {
                            read = stream.Read(buffer, 0, buffer.Length);
                        }
                        catch (IOException exception)
                            when (IsPeerClose(exception))
                        {
                            peerClosedAfterStop.Set();
                            break;
                        }

                        if (read == 0)
                        {
                            peerClosedAfterStop.Set();
                            break;
                        }
                        publishReceived.Set();
                    }
                }
                catch (Exception exception)
                {
                    serverFailure = exception;
                }
                finally
                {
                    serverDone.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Phase186 real TCP peer-close probe"
            };
            server.Start();

            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sink = new PeerCloseReconnectTcpSink();
            var runtime = Runtime(
                owner,
                () => sink,
                generation: 34,
                joinTimeoutMs: 1000,
                port: port,
                sendTimeoutMs: 1000);
            try
            {
                runtime.Start(enabled: true, autoConnect: true);
                Wait(firstAccepted, "loopback peer did not accept first connection");
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.IsConnected,
                    TimeSpan.FromSeconds(5)));
                allowFirstClose.Set();
                Wait(firstClosed, "loopback peer did not close first connection");
                Wait(
                    sink.PlannedReconnectFailure,
                    "runtime did not observe peer close and retry");
                Wait(
                    sink.ReconnectBlocked,
                    "runtime did not begin the bounded replacement connect");

                Assert.True(runtime.TryEnqueue(
                    Frame("/phase186/peer_close", sequence: 1),
                    out var reason), reason);
                var disconnected = runtime.GetStatsSnapshot();
                Assert.False(disconnected.Connected);
                Assert.Equal(0, disconnected.SentFrames);
                Assert.Equal(0, disconnected.FailedFrames);
                Assert.Equal(1, disconnected.QueuedFrames);

                sink.AllowReconnect.Set();
                Wait(
                    secondAccepted,
                    "loopback peer did not accept replacement connection");
                Wait(
                    publishReceived,
                    "queued frame did not survive peer-close reconnect",
                    RealTcpWaitTimeout);
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.GetStatsSnapshot().SentFrames == 1,
                    TimeSpan.FromSeconds(2)));
                var reconnected = runtime.GetStatsSnapshot();
                Assert.True(reconnected.Connected);
                Assert.Equal(0, reconnected.FailedFrames);
                Assert.Equal(0, reconnected.QueuedFrames);

                runtime.Stop();
                Wait(
                    peerClosedAfterStop,
                    "replacement peer did not observe Stop");
                Assert.True(serverDone.Wait(TimeSpan.FromSeconds(2)));
                Assert.True(server.Join(TimeSpan.FromSeconds(1)));
                Assert.Null(serverFailure);
                Assert.Equal(0, owner.OccupiedCount);
                Assert.Equal(0, owner.RetiredCount);
                Assert.Equal(1, sink.DisposeCount);
                Assert.True(sink.DisconnectCount >= 2);

                runtime.Dispose();
                Assert.Equal(1, sink.DisposeCount);
            }
            finally
            {
                allowFirstClose.Set();
                sink.AllowReconnect.Set();
                runtime.Dispose();
                listener.Stop();
                if (server.IsAlive)
                    server.Join(TimeSpan.FromSeconds(7));
            }
        }

        [Fact]
        public void DelayedExitCompletesRetirementAndAllowsExclusiveRestart()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(2);
            var firstSink = ControlledSink.BlockedConnect();
            using var first = Runtime(owner, () => firstSink, generation: 40, joinTimeoutMs: 20);

            first.Start(enabled: true, autoConnect: true);
            Wait(firstSink.ConnectEntered, "first worker did not enter Connect");
            first.Stop();
            Assert.Equal(1, owner.RetiredCount);

            var blockedCreated = 0;
            using var blocked = Runtime(
                owner,
                () =>
                {
                    Interlocked.Increment(ref blockedCreated);
                    return ControlledSink.Healthy();
                },
                generation: 41);
            Assert.False(blocked.TryStart(true, true, out var blockedReason));
            Assert.Contains("exclusive", blockedReason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, Volatile.Read(ref blockedCreated));

            firstSink.ReleaseConnect.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () => owner.OccupiedCount == 0,
                    TimeSpan.FromSeconds(2)),
                "delayed worker did not complete retirement");
            Assert.Equal(1, firstSink.DisposeCount);

            var replacementSink = ControlledSink.Healthy();
            using var replacement = Runtime(
                owner,
                () => replacementSink,
                generation: 42);
            Assert.True(replacement.TryStart(true, true, out var reason), reason);
            Wait(replacementSink.ConnectEntered, "replacement worker did not connect");
            replacement.Stop();

            Assert.Equal(1, replacementSink.DisposeCount);
            Assert.Equal(0, owner.OccupiedCount);
        }

        [Fact]
        public void WarmedExclusiveTimeoutConversionAllocatesNothing()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            Assert.True(owner.TryReserveExclusive(
                ProviderId,
                FoxRunTransportDirection.Publish,
                generation: 50,
                workerCount: 1,
                out var reservation));
            var lease = new DetachedLeaseProbe();
            reservation.WarmUpTimeoutConversionForCurrentThread();

            var before = GC.GetAllocatedBytesForCurrentThread();
            Assert.True(reservation.TryConvertToRetired(
                0,
                lease,
                "phase186-allocation-probe",
                retainedBytes: 0,
                retainedResources: 1));
            var after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(before, after);
            Assert.True(reservation.TryCompleteRetired(0));
            Assert.Equal(1, lease.DisposeCount);
            Assert.Equal(0, owner.OccupiedCount);
        }

    }
}
