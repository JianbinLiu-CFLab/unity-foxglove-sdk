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
    public sealed class Ros2BridgeRetirementLifecycleTests
    {
        private static readonly FoxRunTransportId ProviderId =
            new FoxRunTransportId("unity2foxglove.ros2bridge.retirement-tests");

        [Fact]
        public void LifecycleIsSerializedAndReadyDoesNotMeanConnected()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sink = ControlledSink.BlockedConnect();
            using var runtime = Runtime(owner, () => sink, generation: 1, joinTimeoutMs: 1000);

            Assert.Equal(Ros2BridgeRuntimeLifecycleState.Stopped, runtime.LifecycleState);
            runtime.Start(enabled: true, autoConnect: true);
            Wait(sink.ConnectEntered, "worker did not enter Connect");

            Assert.Equal(Ros2BridgeRuntimeLifecycleState.Ready, runtime.LifecycleState);
            Assert.False(runtime.IsConnected);

            Exception stopFailure = null;
            var stop = new Thread(() =>
            {
                try
                {
                    runtime.Stop();
                }
                catch (Exception exception)
                {
                    stopFailure = exception;
                }
            });
            stop.Start();
            Assert.True(
                SpinWait.SpinUntil(
                    () => runtime.LifecycleState == Ros2BridgeRuntimeLifecycleState.Stopping,
                    TimeSpan.FromSeconds(1)),
                "runtime never entered Stopping");
            sink.ReleaseConnect.Set();
            Assert.True(stop.Join(TimeSpan.FromSeconds(2)), "Stop did not complete");

            Assert.Null(stopFailure);
            Assert.Equal(Ros2BridgeRuntimeLifecycleState.Stopped, runtime.LifecycleState);
            Assert.Equal(0, owner.OccupiedCount);
        }

        [Fact]
        public void DuplicateStartStopAndDisposeAreIdempotentButStartAfterDisposeFails()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sink = ControlledSink.Healthy();
            var runtime = Runtime(owner, () => sink, generation: 2);

            runtime.Start(enabled: true, autoConnect: true);
            runtime.Start(enabled: true, autoConnect: true);
            Wait(sink.ConnectEntered, "worker did not connect");
            runtime.Stop();
            runtime.Stop();
            runtime.Dispose();
            runtime.Dispose();

            Assert.Equal(1, sink.ConnectCount);
            Assert.Equal(1, sink.DisposeCount);
            Assert.Equal(0, owner.OccupiedCount);
            Assert.Throws<ObjectDisposedException>(
                () => runtime.Start(enabled: true, autoConnect: true));
        }

        [Fact]
        public void InitialAutoConnectDisabledFreezesEnabledIdleStateWithoutCreatingSink()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var created = 0;
            using var runtime = Runtime(
                owner,
                () =>
                {
                    Interlocked.Increment(ref created);
                    return ControlledSink.Healthy();
                },
                generation: 3);

            runtime.Start(enabled: true, autoConnect: false);

            var stats = runtime.GetStatsSnapshot();
            Assert.True(stats.Enabled);
            Assert.False(stats.Connected);
            Assert.Equal(0, stats.QueuedFrames);
            Assert.False(runtime.TryEnqueue(
                Frame("/phase186/disabled", sequence: 1),
                out var reason));
            Assert.Contains(
                "auto-connect is disabled",
                reason,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, Volatile.Read(ref created));
            Assert.Equal(0, owner.OccupiedCount);

            runtime.Stop();
            Assert.False(runtime.GetStatsSnapshot().Enabled);
        }

        [Fact]
        public void DisablingAutoConnectOnReadyStopsWorkerAndClosesAdmission()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sink = ControlledSink.Healthy();
            using var runtime = Runtime(owner, () => sink, generation: 4);

            runtime.Start(enabled: true, autoConnect: true);
            Wait(sink.ConnectEntered, "worker did not connect");
            Assert.True(runtime.TryEnqueue(
                Frame("/phase186/before_disable", sequence: 1),
                out var firstReason), firstReason);
            Assert.True(SpinWait.SpinUntil(
                () => sink.SendCount == 1,
                TimeSpan.FromSeconds(2)));

            runtime.Start(enabled: true, autoConnect: false);

            Assert.Equal(
                Ros2BridgeRuntimeLifecycleState.Stopped,
                runtime.LifecycleState);
            Assert.Equal(1, sink.DisposeCount);
            Assert.Equal(0, owner.OccupiedCount);
            Assert.True(runtime.GetStatsSnapshot().Enabled);
            Assert.False(runtime.TryEnqueue(
                Frame("/phase186/after_disable", sequence: 2),
                out var disabledReason));
            Assert.Contains(
                "auto-connect is disabled",
                disabledReason,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, sink.SendCount);
        }

        [Fact]
        public void PublicRuntimeStatsAccumulateAcrossWorkerLeaseRestarts()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sinks = new List<ControlledSink>();
            using var runtime = Runtime(
                owner,
                () =>
                {
                    var sink = ControlledSink.Healthy();
                    lock (sinks)
                        sinks.Add(sink);
                    return sink;
                },
                generation: 5);

            runtime.Start(enabled: true, autoConnect: true);
            ControlledSink first;
            lock (sinks)
                first = sinks[0];
            Wait(first.ConnectEntered, "first worker did not connect");
            Assert.True(runtime.TryEnqueue(
                Frame("/phase186/stats/first", sequence: 1),
                out var firstReason), firstReason);
            Assert.True(SpinWait.SpinUntil(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                TimeSpan.FromSeconds(2)));
            runtime.Stop();
            Assert.Equal(1, runtime.GetStatsSnapshot().SentFrames);

            runtime.Start(enabled: true, autoConnect: true);
            ControlledSink second;
            lock (sinks)
                second = sinks[1];
            Wait(second.ConnectEntered, "second worker did not connect");
            Assert.Equal(1, runtime.GetStatsSnapshot().SentFrames);

            Assert.True(runtime.TryEnqueue(
                Frame("/phase186/stats/second", sequence: 2),
                out var secondReason), secondReason);
            Assert.True(SpinWait.SpinUntil(
                () => runtime.GetStatsSnapshot().SentFrames == 2,
                TimeSpan.FromSeconds(2)));
            runtime.Stop();

            Assert.Equal(2, runtime.GetStatsSnapshot().SentFrames);
            Assert.Equal(0, owner.OccupiedCount);
        }

        [Fact]
        public void CapacityExhaustionStartsNoWorkerAndCreatesNoSink()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            Assert.True(owner.TryReserveExclusive(
                ProviderId,
                FoxRunTransportDirection.Publish,
                generation: 10,
                workerCount: 1,
                out var blocker));
            var created = 0;
            using var runtime = Runtime(
                owner,
                () =>
                {
                    Interlocked.Increment(ref created);
                    return ControlledSink.Healthy();
                },
                generation: 11);

            Assert.False(runtime.TryStart(
                enabled: true,
                autoConnect: true,
                out var reason));
            Assert.Contains("retirement", reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, Volatile.Read(ref created));
            Assert.Equal(Ros2BridgeRuntimeLifecycleState.Stopped, runtime.LifecycleState);
            Assert.Equal(1, owner.OccupiedCount);

            Assert.True(blocker.TryReturn(0));
            blocker.Dispose();
        }

        [Fact]
        public void HealthyJoinDisposesWorkerResourcesAndReturnsTokenExactlyOnce()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sink = ControlledSink.Healthy();
            using var runtime = Runtime(owner, () => sink, generation: 20);

            runtime.Start(enabled: true, autoConnect: true);
            Wait(sink.ConnectEntered, "worker did not connect");
            runtime.Stop();
            runtime.Stop();

            Assert.Equal(1, sink.DisconnectCount);
            Assert.Equal(1, sink.DisposeCount);
            Assert.Equal(0, owner.OccupiedCount);
            Assert.Equal(0, owner.RetiredCount);
        }

        [Fact]
        public void JoinTimeoutConvertsOriginalReservationWithoutDisposingLiveResources()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sink = ControlledSink.BlockedConnect();
            using var runtime = Runtime(owner, () => sink, generation: 30, joinTimeoutMs: 20);

            runtime.Start(enabled: true, autoConnect: true);
            Wait(sink.ConnectEntered, "worker did not enter Connect");
            runtime.Stop();

            Assert.Equal(1, owner.OccupiedCount);
            Assert.Equal(1, owner.RetiredCount);
            Assert.Equal(0, sink.DisposeCount);
            var retired = Assert.Single(owner.CaptureRetired());
            Assert.Equal(ProviderId, retired.ProviderId);
            Assert.Equal(FoxRunTransportDirection.Publish, retired.Direction);
            Assert.Equal(30UL, retired.Generation);
            Assert.True(retired.RetainedResources > 0);
        }

        [Fact]
        public void JoinTimeoutReportsOwnedPayloadWhileSendRemainsInFlight()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sink = ControlledSink.BlockedSend();
            using var runtime = Runtime(
                owner,
                () => sink,
                generation: 31,
                joinTimeoutMs: 20);
            var payload = new byte[4096];

            runtime.Start(enabled: true, autoConnect: true);
            Wait(sink.ConnectEntered, "worker did not connect");
            Assert.True(runtime.TryEnqueue(
                Ros2BridgeFrame.CreateOwned(
                    "/phase186/retained_payload",
                    "phase186_msgs/msg/RetainedPayload",
                    Ros2BridgeFrame.CdrEncoding,
                    logTimeNs: 31,
                    sequence: 1,
                    payload),
                out var reason), reason);
            Wait(sink.SendEntered, "worker did not enter Send");
            runtime.Stop();

            try
            {
                var retired = Assert.Single(owner.CaptureRetired());
                Assert.True(
                    retired.RetainedBytes >= payload.Length,
                    "retirement diagnostics omitted the in-flight owned payload");
                Assert.True(
                    retired.RetainedResources >= 5,
                    "retirement diagnostics omitted the in-flight frame resource");
            }
            finally
            {
                sink.ReleaseSend.Set();
                Assert.True(SpinWait.SpinUntil(
                    () => owner.OccupiedCount == 0,
                    TimeSpan.FromSeconds(2)));
            }
        }

        [Fact]
        public void JoinTimeoutReportsInFlightPublisherPreparationOwnership()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sink = new BlockedPreparationSink();
            using var runtime = Runtime(
                owner,
                () => sink,
                generation: 32,
                joinTimeoutMs: 20);

            runtime.Start(enabled: true, autoConnect: true);
            Wait(sink.ConnectEntered, "worker did not connect");
            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase186/retained_preparation",
                    "phase186_msgs/msg/RetainedPreparation",
                    FoxRunResolvedQos.SensorData,
                    out _));
            Wait(
                sink.ExchangeEntered,
                "worker did not enter publisher preparation exchange");
            runtime.Stop();

            try
            {
                var retired = Assert.Single(owner.CaptureRetired());
                Assert.True(
                    retired.RetainedBytes >= sink.RequestLength,
                    "retirement diagnostics omitted the in-flight preparation request");
                Assert.True(
                    retired.RetainedResources >= 7,
                    "retirement diagnostics omitted the in-flight preparation resources");
            }
            finally
            {
                sink.ReleaseExchange.Set();
                Assert.True(SpinWait.SpinUntil(
                    () => owner.OccupiedCount == 0,
                    TimeSpan.FromSeconds(2)));
            }
        }

        [Fact]
        public void RealTcpDisconnectWakesBlockedPreparationBeforeFinalDispose()
        {
            const int joinTimeoutMs = 1000;
            const int ioTimeoutMs = 5000;
            Assert.True(ioTimeoutMs > joinTimeoutMs * 4);
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
                    "real TCP preparation exchange did not start");
                Wait(
                    requestReceived,
                    "loopback peer did not receive the preparation request");

                var stopwatch = Stopwatch.StartNew();
                runtime.Stop();
                stopwatch.Stop();

                Assert.True(
                    stopwatch.Elapsed < TimeSpan.FromSeconds(2),
                    "Stop exceeded the bounded real TCP wake interval");
                Wait(
                    sink.ExchangeExited,
                    "real TCP preparation exchange did not exit");
                Wait(peerClosed, "loopback peer did not observe TCP close");
                Assert.True(serverDone.Wait(TimeSpan.FromSeconds(2)));
                Assert.True(server.Join(TimeSpan.FromSeconds(1)));
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
                    TimeSpan.FromSeconds(2)));
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
                    "queued frame did not survive peer-close reconnect");
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

        [Fact]
        public void ExclusiveKeyStaysOwnedUntilRetiredLeaseCleanupReturns()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(2);
            Assert.True(owner.TryReserveExclusive(
                ProviderId,
                FoxRunTransportDirection.Publish,
                generation: 51,
                workerCount: 1,
                out var reservation));
            var lease = new BlockingDetachedLease();
            Assert.True(reservation.TryConvertToRetired(
                0,
                lease,
                "phase186-blocking-cleanup",
                retainedBytes: 0,
                retainedResources: 1));

            bool completed = false;
            var completion = new Thread(
                () => completed = reservation.TryCompleteRetired(0));
            completion.Start();
            Wait(lease.DisposeEntered, "retired cleanup did not begin");

            try
            {
                Assert.Equal(1, owner.OccupiedCount);
                Assert.False(owner.TryReserveExclusive(
                    ProviderId,
                    FoxRunTransportDirection.Publish,
                    generation: 52,
                    workerCount: 1,
                    out _));
            }
            finally
            {
                lease.ReleaseDispose.Set();
                Assert.True(completion.Join(TimeSpan.FromSeconds(2)));
            }
            Assert.True(completed);
            Assert.Equal(0, owner.OccupiedCount);
            Assert.True(owner.TryReserveExclusive(
                ProviderId,
                FoxRunTransportDirection.Publish,
                generation: 53,
                workerCount: 1,
                out var replacement));
            Assert.True(replacement.TryReturn(0));
        }

        [Fact]
        public void ThrowingRetiredCleanupIsObservableAndStillReleasesExclusiveKey()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            Assert.True(owner.TryReserveExclusive(
                ProviderId,
                FoxRunTransportDirection.Publish,
                generation: 54,
                workerCount: 1,
                out var reservation));
            Assert.True(reservation.TryConvertToRetired(
                0,
                new ThrowingDetachedLease(),
                "phase186-throwing-cleanup",
                retainedBytes: 0,
                retainedResources: 1));

            var failure = Assert.Throws<InvalidOperationException>(
                () => reservation.TryCompleteRetired(0));

            Assert.Equal("cleanup-visible", failure.Message);
            Assert.Equal(0, owner.OccupiedCount);
            Assert.True(owner.TryReserveExclusive(
                ProviderId,
                FoxRunTransportDirection.Publish,
                generation: 55,
                workerCount: 1,
                out var replacement));
            Assert.True(replacement.TryReturn(0));
        }

        [Fact]
        public void WorkerLeaseOwnsEveryWorkerReachableQueueSignalSinkAndCounter()
        {
            var shellFields = typeof(Ros2BridgeRuntime).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.DoesNotContain(
                shellFields,
                field => typeof(WaitHandle).IsAssignableFrom(field.FieldType)
                         || typeof(Thread).IsAssignableFrom(field.FieldType)
                         || typeof(IRos2BridgeSink).IsAssignableFrom(field.FieldType)
                         || IsQueue(field.FieldType));

            var leaseFields = typeof(Ros2BridgeWorkerLease).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.Contains(
                leaseFields,
                field => typeof(AutoResetEvent).IsAssignableFrom(field.FieldType));
            Assert.Contains(
                leaseFields,
                field => typeof(IRos2BridgeSink).IsAssignableFrom(field.FieldType));
            Assert.Contains(
                leaseFields,
                field => IsQueue(field.FieldType));
            Assert.Contains(
                leaseFields,
                field => field.FieldType
                    == typeof(Ros2BridgeOutboundScheduler));
            Assert.Contains(leaseFields, field => field.Name == "_sentFrames");
            Assert.Contains(leaseFields, field => field.Name == "_droppedFrames");
            Assert.Contains(leaseFields, field => field.Name == "_failedFrames");
        }

        [Fact]
        public void TimedOutLeaseRetainsNoManagerProviderOrFactoryCallback()
        {
            var fixture = CreateTimedOutWeakReferenceFixture();

            ForceCollection();

            Assert.False(fixture.Manager.IsAlive);
            Assert.False(fixture.Provider.IsAlive);
            Assert.False(fixture.Callback.IsAlive);
            Assert.Equal(1, fixture.Owner.RetiredCount);
            Assert.Equal(0, fixture.Sink.DisposeCount);

            fixture.Sink.ReleaseConnect.Set();
            Assert.True(SpinWait.SpinUntil(
                () => fixture.Owner.OccupiedCount == 0,
                TimeSpan.FromSeconds(2)));
            Assert.Equal(1, fixture.Sink.DisposeCount);
        }

        [Fact]
        public void ShutdownRaceHasOneTerminalOwner()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sink = ControlledSink.BlockedConnect();
            using var runtime = Runtime(
                owner,
                () => sink,
                generation: 60,
                joinTimeoutMs: 1000);
            runtime.Start(true, true);
            Wait(sink.ConnectEntered, "worker did not enter Connect");

            Exception failure = null;
            var stop = new Thread(() =>
            {
                try
                {
                    runtime.Stop();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            stop.Start();
            Assert.True(SpinWait.SpinUntil(
                () => runtime.LifecycleState
                      == Ros2BridgeRuntimeLifecycleState.Stopping,
                TimeSpan.FromSeconds(1)));
            sink.ReleaseConnect.Set();
            Assert.True(stop.Join(TimeSpan.FromSeconds(2)));

            Assert.Null(failure);
            Assert.Equal(1, sink.DisposeCount);
            Assert.Equal(0, owner.OccupiedCount);
            Assert.Equal(0, owner.RetiredCount);
        }

        [Fact]
        public void RecoverableConnectAndWriterFailuresDoNotLeakCapacity()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var connectFailure = ControlledSink.FailingConnect();
            using (var runtime = Runtime(
                       owner,
                       () => connectFailure,
                       generation: 70))
            {
                runtime.Start(true, true);
                Wait(connectFailure.ConnectEntered, "connect failure did not run");
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.GetStatsSnapshot().LastError.Length > 0,
                    TimeSpan.FromSeconds(1)));
                runtime.Stop();
            }
            Assert.Equal(1, connectFailure.DisposeCount);
            Assert.Equal(0, owner.OccupiedCount);

            var writerFailure = ControlledSink.FailingSend();
            using (var runtime = Runtime(
                       owner,
                       () => writerFailure,
                       generation: 71))
            {
                runtime.Start(true, true);
                Wait(writerFailure.ConnectEntered, "writer sink did not connect");
                Assert.True(runtime.TryEnqueue(
                    Ros2BridgeFrame.CreateOwned(
                        "/phase186/failure",
                        "phase186_msgs/msg/Failure",
                        Ros2BridgeFrame.CdrEncoding,
                        logTimeNs: 1,
                        sequence: 1,
                        payload: new byte[] { 0x00 }),
                    out var reason), reason);
                Wait(writerFailure.SendEntered, "writer failure did not run");
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.GetStatsSnapshot().FailedFrames >= 1,
                    TimeSpan.FromSeconds(1)));
                runtime.Stop();
            }
            Assert.Equal(1, writerFailure.DisposeCount);
            Assert.Equal(0, owner.OccupiedCount);
        }

        [Fact]
        public void RepeatedStartStopCyclesHaveZeroRetirementGrowth()
        {
            const int cycles = 24;
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            var sinks = new List<ControlledSink>();
            using var runtime = Runtime(
                owner,
                () =>
                {
                    var sink = ControlledSink.Healthy();
                    lock (sinks)
                        sinks.Add(sink);
                    return sink;
                },
                generation: 80);

            for (var i = 0; i < cycles; i++)
            {
                runtime.Start(true, true);
                ControlledSink sink;
                lock (sinks)
                    sink = sinks[i];
                Wait(sink.ConnectEntered, "cycle worker did not connect");
                runtime.Stop();
                Assert.Equal(0, owner.OccupiedCount);
                Assert.Equal(0, owner.RetiredCount);
            }

            Assert.Equal(cycles, sinks.Count);
            Assert.All(sinks, sink => Assert.Equal(1, sink.DisposeCount));
        }

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

        private static void Wait(ManualResetEventSlim signal, string failure)
        {
            Assert.True(signal.Wait(TimeSpan.FromSeconds(2)), failure);
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

                _inner.Connect(host, port, timeoutMs);
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
