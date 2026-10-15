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
    /// <remarks>Phase192 source sections: RetirementTcpDisconnectTests.cs, RetirementLeaseTests.cs, RetirementControlledSink.cs</remarks>
    public sealed partial class Ros2BridgeRetirementLifecycleTests
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
            Assert.True(
                SpinWait.SpinUntil(
                    () => sink.IsConnected && runtime.IsConnected,
                    TimeSpan.FromSeconds(2)),
                "worker did not complete connection");
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
            Assert.True(
                SpinWait.SpinUntil(
                    () => sink.IsConnected && runtime.IsConnected,
                    TimeSpan.FromSeconds(2)),
                "worker did not complete connection");
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
                    TimeSpan.FromSeconds(5)));
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
                    TimeSpan.FromSeconds(5)));
            }
        }

    }
}
