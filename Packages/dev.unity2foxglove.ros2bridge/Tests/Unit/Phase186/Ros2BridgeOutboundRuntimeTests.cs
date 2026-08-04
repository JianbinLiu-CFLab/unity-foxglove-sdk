// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first runtime integration tests for fair Bridge publish scheduling.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeOutboundRuntimeTests
    {
        [Fact]
        public void WorkerDrainsContractsRoundRobinWithoutReorderingEachContract()
        {
            var sink = new GatedRecordingSink();
            using var runtime = Runtime(sink, queueCapacity: 8);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.True(
                sink.ConnectEntered.Wait(TimeSpan.FromSeconds(2)),
                "worker did not enter Connect");

            Enqueue(runtime, Frame("/phase186/fair/a", 1));
            Enqueue(runtime, Frame("/phase186/fair/a", 2));
            Enqueue(runtime, Frame("/phase186/fair/a", 3));
            Enqueue(runtime, Frame("/phase186/fair/b", 1));
            Enqueue(runtime, Frame("/phase186/fair/b", 2));

            sink.ReleaseConnect.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () => sink.SentCount == 5,
                    TimeSpan.FromSeconds(3)),
                "worker did not drain all scheduled frames");

            Assert.Equal(
                new[]
                {
                    "/phase186/fair/a:1",
                    "/phase186/fair/b:1",
                    "/phase186/fair/a:2",
                    "/phase186/fair/b:2",
                    "/phase186/fair/a:3",
                },
                sink.CaptureOrder());
        }

        [Fact]
        public void HotContractOverflowNeverEvictsColdContract()
        {
            var sink = new GatedRecordingSink();
            using var runtime = Runtime(sink, queueCapacity: 8);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.True(
                sink.ConnectEntered.Wait(TimeSpan.FromSeconds(2)),
                "worker did not enter Connect");

            Enqueue(runtime, Frame("/phase186/overflow/cold", 1));
            for (ulong sequence = 1; sequence <= 9; sequence++)
                Enqueue(runtime, Frame("/phase186/overflow/hot", sequence));

            var saturated = runtime.GetStatsSnapshot();
            Assert.Equal(10, saturated.AcceptedFrames);
            Assert.Equal(8, saturated.QueuedFrames);
            Assert.Equal(2, saturated.DroppedFrames);
            Assert.Equal(0, saturated.ReplacedFrames);
            Assert.True(saturated.QueuedBytes > 8);

            sink.ReleaseConnect.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () => sink.SentCount == 8,
                    TimeSpan.FromSeconds(3)),
                "worker did not retain the cold frame plus the hot contract's bounded tail");

            var order = sink.CaptureOrder();
            Assert.Contains(
                "/phase186/overflow/cold:1",
                order,
                StringComparer.Ordinal);
            Assert.DoesNotContain(
                "/phase186/overflow/hot:1",
                order,
                StringComparer.Ordinal);
            Assert.DoesNotContain(
                "/phase186/overflow/hot:2",
                order,
                StringComparer.Ordinal);
            Assert.Equal(
                Enumerable.Range(3, 7)
                    .Select(value =>
                        "/phase186/overflow/hot:" + value)
                    .OrderBy(value => value, StringComparer.Ordinal),
                order.Where(value =>
                        value.StartsWith(
                            "/phase186/overflow/hot:",
                            StringComparison.Ordinal))
                    .OrderBy(value => value, StringComparer.Ordinal));
            Assert.True(
                SpinWait.SpinUntil(
                    () => runtime.GetStatsSnapshot().SentFrames == 8,
                    TimeSpan.FromSeconds(1)),
                "worker did not settle every drained write lease");
            var drained = runtime.GetStatsSnapshot();
            Assert.Equal(10, drained.AcceptedFrames);
            Assert.Equal(8, drained.SentFrames);
            Assert.Equal(2, drained.DroppedFrames);
            Assert.Equal(0, drained.QueuedFrames);
            Assert.Equal(0, drained.QueuedBytes);
        }

        [Fact]
        public void ProviderStatsExposeFullWireOwnershipAndPersistAfterStop()
        {
            var sink = new RawWireRecordingSink();
            using var runtime = Runtime(sink, queueCapacity: 8);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.True(
                sink.ConnectEntered.Wait(TimeSpan.FromSeconds(2)),
                "worker did not enter Connect");
            var frame = Frame("/phase186/stats/raw", 1);
            var expectedWire = Ros2BridgeFrameWriter.Write(frame);

            Enqueue(runtime, frame);
            var queued = runtime.GetStatsSnapshot();
            Assert.Equal(1, queued.AcceptedFrames);
            Assert.Equal(1, queued.QueuedFrames);
            Assert.Equal(expectedWire.Length, queued.QueuedBytes);
            Assert.Equal(0, queued.TransientBytes);
            Assert.Equal(0, queued.InFlightBytes);

            sink.ReleaseConnect.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () => sink.RawSendCount == 1,
                    TimeSpan.FromSeconds(3)),
                "worker did not write the owned wire frame");
            Assert.Equal(expectedWire, sink.CaptureWire());
            Assert.Equal(0, sink.LegacySendCount);

            runtime.Stop();
            Assert.False(
                runtime.TryEnqueue(frame, out var afterStopReason));
            Assert.Contains(
                "disabled",
                afterStopReason,
                StringComparison.OrdinalIgnoreCase);
            var stopped = runtime.GetStatsSnapshot();
            Assert.Equal(1, stopped.AcceptedFrames);
            Assert.Equal(1, stopped.SentFrames);
            Assert.Equal(0, stopped.QueuedFrames);
            Assert.Equal(0, stopped.QueuedBytes);
            Assert.Equal(0, stopped.TransientBytes);
            Assert.Equal(0, stopped.InFlightBytes);
            Assert.Equal(0, stopped.ReplacedFrames);
            Assert.Equal(0, stopped.OversizeFrames);
            Assert.Equal(0, stopped.BackpressureRejectedFrames);
            Assert.Equal(1, stopped.RejectedAfterStopFrames);
            Assert.Equal(0, stopped.FaultedFrames);
            Assert.Equal(0, stopped.DisposalFailures);
        }

        [Fact]
        public void TerminalSchedulerFaultCleansOnceAndStopsWorkerProgress()
        {
            var sink = new GatedRecordingSink();
            using var runtime = Runtime(sink, queueCapacity: 8);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.True(
                sink.ConnectEntered.Wait(TimeSpan.FromSeconds(2)),
                "worker did not enter Connect");

            Enqueue(runtime, Frame("/phase187/terminal", 1));
            var scheduler = OutboundScheduler(runtime);
            var worker = WorkerThread(runtime);
            Assert.True(worker.IsAlive);
            var terminal = new InvalidOperationException(
                "encoder invariant");
            scheduler.Fault(terminal);
            scheduler.Fault(new InvalidOperationException("duplicate"));

            Assert.True(scheduler.IsClosed);
            Assert.True(scheduler.IsFaulted);
            Assert.Same(terminal, scheduler.TerminalFault);
            Assert.Equal(1UL, scheduler.Counters.Faulted);
            Assert.Equal(1UL, scheduler.Counters.Dropped);
            Assert.Equal(1UL, scheduler.LastCloseResult.ClearedDataDepth);
            Assert.True(
                runtime.GetPublisherObservationSnapshot().SchedulerTerminal);

            var status = Ros2BridgeTransportStatusMapper.Create(
                generation: 187,
                FoxRunTransportCapabilities.Publish,
                runtime.LifecycleState,
                runtime.GetStatsSnapshot(),
                runtime.HasInboundPipeline,
                runtime.GetPublisherObservationSnapshot(),
                Ros2BridgeSubscriptionObservationSnapshot.Empty);
            Assert.Equal(
                FoxRunTransportObservedState.Failed,
                status.Publish.State);
            Assert.Contains(
                status.Diagnostics,
                diagnostic => diagnostic.Code == "ROS2BRIDGE007");

            sink.ReleaseConnect.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () => !runtime.IsConnected
                          && sink.DisconnectCount == 1
                          && !worker.IsAlive,
                    TimeSpan.FromSeconds(2)),
                "terminal scheduler did not stop connected worker progress");
            Assert.False(
                SpinWait.SpinUntil(
                    () => sink.ConnectCount > 1
                          || sink.DisconnectCount > 1,
                    TimeSpan.FromMilliseconds(200)),
                "terminal scheduler was observed more than once");
            Assert.Equal(1UL, scheduler.Counters.Faulted);
            Assert.Equal(1UL, scheduler.Counters.Dropped);
            Assert.Contains(
                "encoder invariant",
                runtime.GetStatsSnapshot().LastError,
                StringComparison.Ordinal);
        }

        [Fact]
        public void EmptySchedulerWaitRemainsRetryable()
        {
            var sink = new GatedRecordingSink();
            using var runtime = Runtime(sink, queueCapacity: 8);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.True(
                sink.ConnectEntered.Wait(TimeSpan.FromSeconds(2)),
                "worker did not enter Connect");
            sink.ReleaseConnect.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () => runtime.IsConnected,
                    TimeSpan.FromSeconds(2)),
                "worker did not connect");

            Assert.False(
                SpinWait.SpinUntil(
                    () => !runtime.IsConnected
                          || sink.DisconnectCount != 0,
                    TimeSpan.FromMilliseconds(200)),
                "an empty retryable scheduler stopped the worker");
            Enqueue(runtime, Frame("/phase187/retryable", 1));
            Assert.True(
                SpinWait.SpinUntil(
                    () => sink.SentCount == 1,
                    TimeSpan.FromSeconds(2)),
                "worker did not resume after retryable empty polling");
        }

        private static Ros2BridgeRuntime Runtime(
            IRos2BridgeSink sink,
            int queueCapacity)
            => new Ros2BridgeRuntime(
                "127.0.0.1",
                19484,
                queueCapacity,
                reconnectIntervalMs: 10,
                sendTimeoutMs: 100,
                sinkFactory: () => sink);

        private static Ros2BridgeFrame Frame(
            string topic,
            ulong sequence)
            => Ros2BridgeFrame.CreateOwned(
                topic,
                "phase186_msgs/msg/FairPublish",
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: sequence,
                sequence,
                payload: new[] { checked((byte)sequence) });

        private static void Enqueue(
            Ros2BridgeRuntime runtime,
            Ros2BridgeFrame frame)
        {
            Assert.True(
                runtime.TryEnqueue(frame, out var reason),
                reason);
        }

        private static Ros2BridgeOutboundScheduler OutboundScheduler(
            Ros2BridgeRuntime runtime)
        {
            var run = WorkerLease(runtime);
            var outboundField = typeof(Ros2BridgeWorkerLease).GetField(
                "_outbound",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return Assert.IsType<Ros2BridgeOutboundScheduler>(
                outboundField?.GetValue(run));
        }

        private static Thread WorkerThread(
            Ros2BridgeRuntime runtime)
        {
            var run = WorkerLease(runtime);
            var workerField = typeof(Ros2BridgeWorkerLease).GetField(
                "_worker",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return Assert.IsType<Thread>(
                workerField?.GetValue(run));
        }

        private static Ros2BridgeWorkerLease WorkerLease(
            Ros2BridgeRuntime runtime)
        {
            var runField = typeof(Ros2BridgeRuntime).GetField(
                "_run",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return Assert.IsType<Ros2BridgeWorkerLease>(
                runField?.GetValue(runtime));
        }

        private sealed class GatedRecordingSink : IRos2BridgeSink
        {
            private readonly object _gate = new object();
            private readonly List<Ros2BridgeFrame> _sent =
                new List<Ros2BridgeFrame>();
            private int _connected;
            private int _connectCount;
            private int _disconnectCount;

            internal ManualResetEventSlim ConnectEntered { get; } =
                new ManualResetEventSlim(false);

            internal ManualResetEventSlim ReleaseConnect { get; } =
                new ManualResetEventSlim(false);

            public bool IsConnected =>
                Volatile.Read(ref _connected) != 0;

            internal int SentCount
            {
                get
                {
                    lock (_gate)
                        return _sent.Count;
                }
            }

            internal int ConnectCount =>
                Volatile.Read(ref _connectCount);

            internal int DisconnectCount =>
                Volatile.Read(ref _disconnectCount);

            public void Connect(
                string host,
                int port,
                int timeoutMs)
            {
                _ = host;
                _ = port;
                _ = timeoutMs;
                Interlocked.Increment(ref _connectCount);
                ConnectEntered.Set();
                if (!ReleaseConnect.Wait(TimeSpan.FromSeconds(3)))
                {
                    throw new TimeoutException(
                        "test did not release Bridge connect");
                }

                Volatile.Write(ref _connected, 1);
            }

            public void Send(
                Ros2BridgeFrame frame,
                int timeoutMs)
            {
                _ = timeoutMs;
                lock (_gate)
                    _sent.Add(frame);
            }

            public void Disconnect()
            {
                Interlocked.Increment(ref _disconnectCount);
                Volatile.Write(ref _connected, 0);
                ReleaseConnect.Set();
            }

            public void Dispose()
            {
                Disconnect();
                ConnectEntered.Dispose();
                ReleaseConnect.Dispose();
            }

            internal string[] CaptureOrder()
            {
                lock (_gate)
                {
                    return _sent
                        .Select(frame =>
                            frame.Topic + ":" + frame.Sequence)
                        .ToArray();
                }
            }
        }

        private sealed class RawWireRecordingSink :
            IRos2BridgeSink,
            IRos2BridgeRawWireSink
        {
            private readonly object _gate = new object();
            private byte[] _wire;
            private int _connected;
            private int _legacySendCount;
            private int _rawSendCount;

            internal ManualResetEventSlim ConnectEntered { get; } =
                new ManualResetEventSlim(false);

            internal ManualResetEventSlim ReleaseConnect { get; } =
                new ManualResetEventSlim(false);

            public bool IsConnected =>
                Volatile.Read(ref _connected) != 0;

            internal int LegacySendCount =>
                Volatile.Read(ref _legacySendCount);

            internal int RawSendCount =>
                Volatile.Read(ref _rawSendCount);

            public void Connect(
                string host,
                int port,
                int timeoutMs)
            {
                _ = host;
                _ = port;
                _ = timeoutMs;
                ConnectEntered.Set();
                if (!ReleaseConnect.Wait(TimeSpan.FromSeconds(3)))
                {
                    throw new TimeoutException(
                        "test did not release Bridge connect");
                }
                Volatile.Write(ref _connected, 1);
            }

            public void Send(
                Ros2BridgeFrame frame,
                int timeoutMs)
            {
                _ = frame;
                _ = timeoutMs;
                Interlocked.Increment(ref _legacySendCount);
            }

            public void SendWire(
                ReadOnlyMemory<byte> wireBytes,
                int timeoutMs)
            {
                _ = timeoutMs;
                lock (_gate)
                    _wire = wireBytes.ToArray();
                Interlocked.Increment(ref _rawSendCount);
            }

            public void Disconnect()
            {
                Volatile.Write(ref _connected, 0);
                ReleaseConnect.Set();
            }

            public void Dispose()
            {
                Disconnect();
                ConnectEntered.Dispose();
                ReleaseConnect.Dispose();
            }

            internal byte[] CaptureWire()
            {
                lock (_gate)
                    return _wire == null ? Array.Empty<byte>() : (byte[])_wire.Clone();
            }
        }
    }
}
