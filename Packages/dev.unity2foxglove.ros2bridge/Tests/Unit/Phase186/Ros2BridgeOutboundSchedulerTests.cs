// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first coverage for the Bridge-local bounded outbound scheduler.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeOutboundSchedulerTests
    {
        [Fact]
        public void FullIdentityGetsCollisionFreeSessionKeysAndContractsDrainRoundRobin()
        {
            using var scheduler = new Ros2BridgeOutboundScheduler(
                U2R2ProtocolLimits.Default,
                sessionGeneration: 77);
            var a1 = Frame("/phase186/scheduler/a", 1);
            var a2 = Frame("/phase186/scheduler/a", 2);
            var b1 = Frame("/phase186/scheduler/b", 1);
            var b2 = Frame("/phase186/scheduler/b", 2);
            var noQos = Frame("/phase186/scheduler/qos", 1);
            var defaultQos = Frame(
                "/phase186/scheduler/qos",
                2,
                FoxRunResolvedQos.Default);
            var sensorQos = Frame(
                "/phase186/scheduler/qos",
                3,
                FoxRunResolvedQos.SensorData);

            foreach (var frame in new[]
                     {
                         a1, a2, b1, b2, noQos, defaultQos, sensorQos
                     })
            {
                Assert.Equal(
                    Ros2BridgeOutboundEnqueueDisposition.Accepted,
                    scheduler.Enqueue(
                        frame,
                        U2R2QueueOverflowPolicy.Reject));
            }

            var drained = Drain(scheduler);
            Assert.Equal(
                new[]
                {
                    a1, b1, noQos, defaultQos, sensorQos, a2, b2
                }.Select(Identity),
                drained.Select(item => Identity(item.Frame)));
            Assert.All(
                drained,
                item => Assert.Equal(77UL, item.Key.Generation));
            Assert.Equal(
                5,
                drained.Select(item => item.Key.ContractId).Distinct().Count());
            Assert.Equal(
                drained[0].Key,
                drained[5].Key);
            Assert.Equal(
                drained[1].Key,
                drained[6].Key);
        }

        [Fact]
        public void EnqueueReservesTwiceTheMeasuredWireAndLeaseKeepsRawWireAndSource()
        {
            var frame = Frame("/phase186/scheduler/wire", 1);
            var measurement = Ros2BridgeFrameWriter.Measure(frame);
            var requiredTransient =
                checked(2UL * (ulong)measurement.TotalWireBytes);
            var rejectedLimits = LimitsForMeasurement(
                measurement,
                requiredTransient - 1);
            using (var rejected = new Ros2BridgeOutboundScheduler(
                       rejectedLimits,
                       sessionGeneration: 1))
            {
                Assert.Equal(
                    Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected,
                    rejected.Enqueue(
                        frame,
                        U2R2QueueOverflowPolicy.Reject));
                Assert.Equal(0UL, rejected.DataQueuedDepth);
                Assert.Equal(0UL, rejected.TransientBytes);
            }

            var acceptedLimits = LimitsForMeasurement(
                measurement,
                requiredTransient);
            using var accepted = new Ros2BridgeOutboundScheduler(
                acceptedLimits,
                sessionGeneration: 2);
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.Accepted,
                accepted.Enqueue(
                    frame,
                    U2R2QueueOverflowPolicy.Reject));
            Assert.Equal(0UL, accepted.TransientBytes);
            Assert.True(accepted.TryBeginWrite(out var lease));
            using (lease)
            {
                Assert.NotSame(frame, lease.SourceFrame);
                Assert.Equal(frame.Topic, lease.SourceFrame.Topic);
                Assert.Equal(frame.SchemaName, lease.SourceFrame.SchemaName);
                Assert.Equal(frame.Qos, lease.SourceFrame.Qos);
                Assert.Equal(
                    frame.PayloadMemory.ToArray(),
                    lease.SourceFrame.PayloadMemory.ToArray());
                Assert.True(MemoryMarshal.TryGetArray(
                    lease.WireBytes,
                    out ArraySegment<byte> wireSegment));
                Assert.True(MemoryMarshal.TryGetArray(
                    lease.SourceFrame.PayloadMemory,
                    out ArraySegment<byte> payloadSegment));
                Assert.Same(wireSegment.Array, payloadSegment.Array);
                Assert.Equal(
                    Ros2BridgeFrameWriter.Write(frame),
                    lease.WireBytes.ToArray());
                lease.Complete();
            }
        }

        [Fact]
        public void DropAndReplaceAffectOnlyTheOffendingContract()
        {
            var limits = U2R2ProtocolLimits.Default.With(
                ("maxPerContractQueueDepth", 2UL));
            using var scheduler = new Ros2BridgeOutboundScheduler(
                limits,
                sessionGeneration: 3);
            var cold = Frame("/phase186/scheduler/cold", 1);
            var hot1 = Frame("/phase186/scheduler/hot", 1);
            var hot2 = Frame("/phase186/scheduler/hot", 2);
            var hot3 = Frame("/phase186/scheduler/hot", 3);
            var hot4 = Frame("/phase186/scheduler/hot", 4);

            AssertAccepted(scheduler, cold);
            AssertAccepted(scheduler, hot1);
            AssertAccepted(scheduler, hot2);
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.DroppedOldest,
                scheduler.Enqueue(
                    hot3,
                    U2R2QueueOverflowPolicy.DropOldest));
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.ReplacedLatest,
                scheduler.Enqueue(
                    hot4,
                    U2R2QueueOverflowPolicy.ReplaceLatest));

            var drained = Drain(scheduler)
                .Select(item => Identity(item.Frame))
                .ToArray();
            Assert.Equal(
                new[] { cold, hot2, hot4 }.Select(Identity),
                drained);
            Assert.True(
                scheduler.TryGetContractCounters(
                    cold,
                    out var coldCounters));
            Assert.Equal(1UL, coldCounters.Accepted);
            Assert.Equal(0UL, coldCounters.Dropped);
            Assert.True(
                scheduler.TryGetContractCounters(
                    hot1,
                    out var hotCounters));
            Assert.Equal(4UL, hotCounters.Accepted);
            Assert.Equal(1UL, hotCounters.Dropped);
            Assert.Equal(1UL, hotCounters.Replaced);
        }

        [Fact]
        public void WriteLeaseIsUniqueAndRecoverableFaultSettlesOnlyItsFrame()
        {
            using var scheduler = new Ros2BridgeOutboundScheduler(
                U2R2ProtocolLimits.Default,
                sessionGeneration: 4);
            var first = Frame("/phase186/scheduler/fault", 1);
            var second = Frame("/phase186/scheduler/fault", 2);
            AssertAccepted(scheduler, first);
            AssertAccepted(scheduler, second);

            Assert.True(scheduler.TryBeginWrite(out var firstLease));
            Assert.False(scheduler.TryBeginWrite(out _));
            firstLease.Fault(new InvalidOperationException("send failed"));
            Assert.False(scheduler.IsFaulted);
            Assert.Throws<InvalidOperationException>(
                () => firstLease.Complete());

            Assert.True(scheduler.TryBeginWrite(out var secondLease));
            secondLease.Complete();
            Assert.Equal(2UL, scheduler.Counters.Accepted);
            Assert.Equal(1UL, scheduler.Counters.Sent);
            Assert.Equal(1UL, scheduler.Counters.Faulted);
            Assert.Equal(0UL, scheduler.InFlightBytes);
        }

        [Fact]
        public void ControlReservationSurvivesDataSaturationAndCloseReportsClearedData()
        {
            var limits = U2R2ProtocolLimits.Default.With(
                ("maxPerContractQueueDepth", 1UL));
            using var scheduler = new Ros2BridgeOutboundScheduler(
                limits,
                sessionGeneration: 5);
            var first = Frame("/phase186/scheduler/close/a", 1);
            var second = Frame("/phase186/scheduler/close/b", 1);
            AssertAccepted(scheduler, first);
            AssertAccepted(scheduler, second);
            Assert.True(scheduler.TryReserveControl(1, out var reservation));
            reservation.Commit(
                U2R2OutboundFrame.Control(
                    "phase186-control",
                    new byte[] { 0x42 }));

            Assert.True(scheduler.TryBeginWrite(out var controlLease));
            Assert.True(controlLease.IsControl);
            controlLease.Complete();

            var expectedBytes = checked(
                (ulong)Ros2BridgeFrameWriter.Measure(first).TotalWireBytes
                + (ulong)Ros2BridgeFrameWriter.Measure(second).TotalWireBytes);
            var close = scheduler.Close();
            Assert.Equal(2UL, close.ClearedDataDepth);
            Assert.Equal(expectedBytes, close.ClearedDataBytes);
            Assert.Equal(close, scheduler.LastCloseResult);
            Assert.Equal(default, scheduler.Close());
            Assert.True(
                scheduler.TryGetTerminalState(
                    out var closeFault));
            Assert.Null(closeFault);
            Assert.Equal(2UL, scheduler.Counters.Dropped);
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.RejectedAfterStop,
                scheduler.Enqueue(
                    Frame("/phase186/scheduler/closed", 1),
                    U2R2QueueOverflowPolicy.Reject));
            Assert.Equal(1UL, scheduler.Counters.RejectedAfterStop);
        }

        [Fact]
        public void ContractBoundOversizeAndExplicitDisposalFailureHaveExactCounters()
        {
            var limits = U2R2ProtocolLimits.Default.With(
                ("maxContracts", 1UL));
            using var scheduler = new Ros2BridgeOutboundScheduler(
                limits,
                sessionGeneration: 6);
            var first = Frame("/phase186/scheduler/bounded/a", 1);
            var second = Frame("/phase186/scheduler/bounded/b", 1);
            AssertAccepted(scheduler, first);
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected,
                scheduler.Enqueue(
                    second,
                    U2R2QueueOverflowPolicy.Reject));
            scheduler.RecordDisposalFailure(first);

            Assert.Equal(1UL, scheduler.Counters.Accepted);
            Assert.Equal(1UL, scheduler.Counters.BackpressureRejected);
            Assert.Equal(1UL, scheduler.Counters.DisposalFailures);
            Assert.True(
                scheduler.TryGetContractCounters(
                    first,
                    out var firstCounters));
            Assert.Equal(1UL, firstCounters.DisposalFailures);
            Assert.False(
                scheduler.TryGetContractCounters(
                    second,
                    out _));
        }

        [Fact]
        public void RejectedProvisionalContractDoesNotConsumeContractCapacity()
        {
            var first = Frame("/phase186/scheduler/provisional/a", 1);
            var rejected = Frame("/phase186/scheduler/provisional/b", 1);
            var replacement = Frame("/phase186/scheduler/provisional/c", 1);
            var measurement = Ros2BridgeFrameWriter.Measure(first);
            var wireBytes = checked((ulong)measurement.TotalWireBytes);
            var limits = U2R2ProtocolLimits.Default.With(
                ("maxContracts", 2UL),
                ("maxHeaderBytes", (ulong)measurement.HeaderBytes),
                ("maxPayloadBytes", (ulong)measurement.PayloadBytes),
                ("maxTransientBytes", checked(wireBytes * 2)),
                ("maxInFlightBytes", wireBytes),
                ("maxPerContractQueueDepth", 1UL),
                ("maxPerContractQueueBytes", wireBytes),
                ("maxTotalQueueDepth", 9UL),
                (
                    "maxQueuedBytes",
                    checked(
                        U2R2ProtocolLimits.Default
                            .ReservedControlQueueBytes
                        + wireBytes)));
            using var scheduler = new Ros2BridgeOutboundScheduler(
                limits,
                sessionGeneration: 7);

            AssertAccepted(scheduler, first);
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected,
                scheduler.Enqueue(
                    rejected,
                    U2R2QueueOverflowPolicy.Reject));
            Assert.False(
                scheduler.TryGetContractCounters(
                    rejected,
                    out _));

            Assert.Single(Drain(scheduler));
            AssertAccepted(scheduler, replacement);
            Assert.Equal(
                new[] { replacement }.Select(Identity),
                Drain(scheduler)
                    .Select(item => Identity(item.Frame)));
        }

        [Fact]
        public void DepthAndByteBudgetsRejectWithoutAccountingDrift()
        {
            var a1 = Frame("/phase186/scheduler/capacity/a", 1);
            var a2 = Frame("/phase186/scheduler/capacity/a", 2);
            var b1 = Frame("/phase186/scheduler/capacity/b", 1);
            var c1 = Frame("/phase186/scheduler/capacity/c", 1);
            var measurement = Ros2BridgeFrameWriter.Measure(a1);
            var wireBytes = checked((ulong)measurement.TotalWireBytes);
            var limits = U2R2ProtocolLimits.Default.With(
                ("maxContracts", 3UL),
                ("maxHeaderBytes", (ulong)measurement.HeaderBytes),
                ("maxPayloadBytes", (ulong)measurement.PayloadBytes),
                ("maxTransientBytes", checked(wireBytes * 2)),
                ("maxInFlightBytes", wireBytes),
                ("maxPerContractQueueDepth", 2UL),
                ("maxPerContractQueueBytes", wireBytes),
                ("reservedControlQueueDepth", 1UL),
                ("reservedControlQueueBytes", wireBytes),
                ("controlBurstLimit", 1UL),
                ("maxTotalQueueDepth", 3UL),
                ("maxQueuedBytes", checked(wireBytes * 3)));
            using var scheduler = new Ros2BridgeOutboundScheduler(
                limits,
                sessionGeneration: 8);

            AssertAccepted(scheduler, a1);
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected,
                scheduler.Enqueue(
                    a2,
                    U2R2QueueOverflowPolicy.Reject));
            AssertAccepted(scheduler, b1);
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected,
                scheduler.Enqueue(
                    c1,
                    U2R2QueueOverflowPolicy.Reject));

            Assert.Equal(2UL, scheduler.DataQueuedDepth);
            Assert.Equal(checked(wireBytes * 2), scheduler.QueuedBytes);
            Assert.Equal(2UL, scheduler.Counters.Accepted);
            Assert.Equal(
                2UL,
                scheduler.Counters.BackpressureRejected);
            Assert.Equal(
                new[] { a1, b1 }.Select(Identity),
                Drain(scheduler)
                    .Select(item => Identity(item.Frame)));
            Assert.Equal(0UL, scheduler.QueuedBytes);
            Assert.Equal(0UL, scheduler.InFlightBytes);
        }

        [Fact]
        public void OversizeAndTerminalFaultAreFailClosedAndExactlyCounted()
        {
            var baseline = Frame("/phase186/scheduler/size", 1);
            var measurement = Ros2BridgeFrameWriter.Measure(baseline);
            var wireBytes = checked((ulong)measurement.TotalWireBytes);
            var limits = U2R2ProtocolLimits.Default.With(
                ("maxHeaderBytes", (ulong)measurement.HeaderBytes),
                ("maxPayloadBytes", (ulong)measurement.PayloadBytes),
                ("maxTransientBytes", checked(wireBytes * 2)),
                ("maxInFlightBytes", wireBytes),
                ("maxPerContractQueueBytes", wireBytes),
                ("reservedControlQueueBytes", wireBytes),
                ("maxQueuedBytes", checked(wireBytes * 2)));
            using var scheduler = new Ros2BridgeOutboundScheduler(
                limits,
                sessionGeneration: 9);
            var oversized = Frame(
                "/phase186/scheduler/size/header_too_long",
                1);

            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.Oversize,
                scheduler.Enqueue(
                    oversized,
                    U2R2QueueOverflowPolicy.Reject));
            AssertAccepted(scheduler, baseline);
            var terminal = new InvalidOperationException(
                "encoder invariant");
            scheduler.Fault(terminal);

            Assert.True(scheduler.IsFaulted);
            Assert.True(scheduler.IsClosed);
            Assert.Same(terminal, scheduler.TerminalFault);
            Assert.True(
                scheduler.TryGetTerminalState(
                    out var observedTerminal));
            Assert.Same(terminal, observedTerminal);
            Assert.Equal(1UL, scheduler.Counters.Oversize);
            Assert.Equal(1UL, scheduler.Counters.Faulted);
            Assert.Equal(1UL, scheduler.Counters.Dropped);
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.Faulted,
                scheduler.Enqueue(
                    baseline,
                    U2R2QueueOverflowPolicy.Reject));
            Assert.Equal(2UL, scheduler.Counters.Faulted);
            Assert.False(
                scheduler.TryGetContractCounters(
                    oversized,
                    out _));
        }

        [Fact]
        public void UnsettledDisposeReleasesAuthorityAndRecordsFault()
        {
            using var scheduler = new Ros2BridgeOutboundScheduler(
                U2R2ProtocolLimits.Default,
                sessionGeneration: 10);
            var frame = Frame("/phase186/scheduler/dispose", 1);
            AssertAccepted(scheduler, frame);
            Assert.True(scheduler.TryBeginWrite(out var lease));

            lease.Dispose();
            lease.Dispose();

            Assert.Equal(0UL, scheduler.InFlightBytes);
            Assert.Equal(1UL, scheduler.Counters.Accepted);
            Assert.Equal(0UL, scheduler.Counters.Sent);
            Assert.Equal(1UL, scheduler.Counters.Faulted);
            Assert.Equal(0UL, scheduler.Counters.DisposalFailures);
            Assert.False(scheduler.TryBeginWrite(out _));
        }

        [Fact]
        public void WriteLeaseCarriesLegacyMetadataAndDropSettlesExactlyOnce()
        {
            using var scheduler = new Ros2BridgeOutboundScheduler(
                U2R2ProtocolLimits.Default,
                sessionGeneration: 11);
            var frame = Frame("/phase186/scheduler/metadata", 1);
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.Accepted,
                scheduler.Enqueue(
                    frame,
                    U2R2QueueOverflowPolicy.Reject,
                    requiresPreparation: true,
                    enqueueConnectionGeneration: 42));
            Assert.True(scheduler.TryBeginWrite(out var lease));

            Assert.True(lease.RequiresPreparation);
            Assert.Equal(42L, lease.EnqueueConnectionGeneration);
            Assert.NotSame(frame, lease.SourceFrame);
            Assert.Equal(
                Identity(frame),
                Identity(lease.SourceFrame));
            lease.Drop();

            Assert.Equal(1UL, scheduler.Counters.Accepted);
            Assert.Equal(1UL, scheduler.Counters.Dropped);
            Assert.Equal(0UL, scheduler.Counters.Sent);
            Assert.Equal(0UL, scheduler.Counters.Faulted);
            Assert.Equal(0UL, scheduler.InFlightBytes);
            Assert.Throws<InvalidOperationException>(
                () => lease.Drop());
            Assert.Throws<InvalidOperationException>(
                () => lease.Complete());
        }

        private static U2R2ProtocolLimits LimitsForMeasurement(
            Ros2BridgeFrameMeasurement measurement,
            ulong transientBytes)
        {
            var maximumFrame = checked(
                16UL
                + (ulong)measurement.HeaderBytes
                + (ulong)measurement.PayloadBytes);
            return U2R2ProtocolLimits.Default.With(
                ("maxHeaderBytes", (ulong)measurement.HeaderBytes),
                ("maxPayloadBytes", (ulong)measurement.PayloadBytes),
                ("maxTransientBytes", transientBytes),
                ("maxInFlightBytes", maximumFrame),
                ("maxPerContractQueueBytes", maximumFrame),
                ("reservedControlQueueBytes", maximumFrame),
                ("maxQueuedBytes", checked(maximumFrame * 2)));
        }

        private static Ros2BridgeFrame Frame(
            string topic,
            ulong sequence,
            FoxRunResolvedQos? qos = null)
            => Ros2BridgeFrame.CreateValidated(
                topic,
                "phase186_msgs/msg/Scheduled",
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: sequence,
                sequence,
                payload: new[] { checked((byte)sequence) },
                qos);

        private static void AssertAccepted(
            Ros2BridgeOutboundScheduler scheduler,
            Ros2BridgeFrame frame)
            => Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.Accepted,
                scheduler.Enqueue(
                    frame,
                    U2R2QueueOverflowPolicy.Reject));

        private static string Identity(Ros2BridgeFrame frame)
        {
            var prefix = frame.Topic
                         + "|"
                         + frame.SchemaName
                         + "|"
                         + frame.Sequence
                         + "|";
            if (!frame.Qos.HasValue)
                return prefix + "no-qos";
            var qos = frame.Qos.Value;
            return prefix
                   + (int)qos.Profile
                   + ":"
                   + (int)qos.Reliability
                   + ":"
                   + (int)qos.Durability
                   + ":"
                   + (int)qos.History
                   + ":"
                   + qos.Depth;
        }

        private static List<DrainedFrame> Drain(
            Ros2BridgeOutboundScheduler scheduler)
        {
            var drained = new List<DrainedFrame>();
            while (scheduler.TryBeginWrite(out var lease))
            {
                using (lease)
                {
                    Assert.False(lease.IsControl);
                    drained.Add(
                        new DrainedFrame(
                            lease.SourceFrame,
                            lease.ContractKey));
                    lease.Complete();
                }
            }
            return drained;
        }

        private readonly struct DrainedFrame
        {
            internal DrainedFrame(
                Ros2BridgeFrame frame,
                U2R2ContractKey key)
            {
                Frame = frame;
                Key = key;
            }

            internal Ros2BridgeFrame Frame { get; }
            internal U2R2ContractKey Key { get; }
        }
    }
}
