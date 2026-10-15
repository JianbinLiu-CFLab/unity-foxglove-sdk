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

    }
}
