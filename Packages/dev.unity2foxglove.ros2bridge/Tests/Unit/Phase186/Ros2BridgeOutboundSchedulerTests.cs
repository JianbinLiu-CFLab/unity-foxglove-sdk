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
    public sealed partial class Ros2BridgeOutboundSchedulerTests
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
        public void EnqueueReservesOneOwnedWireBufferAndLeaseKeepsRawWireAndSource()
        {
            var frame = Frame("/phase186/scheduler/wire", 1);
            var measurement = Ros2BridgeFrameWriter.Measure(frame);
            var requiredTransient =
                checked((ulong)measurement.TotalWireBytes);
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
        public void PreparedWireReservationIsReleasedOnCancelAndCloseRace()
        {
            var frame = Frame("/phase186/scheduler/prepared", 1);
            var wireBytes = checked(
                (ulong)Ros2BridgeFrameWriter.Measure(frame).TotalWireBytes);
            using var scheduler = new Ros2BridgeOutboundScheduler(
                U2R2ProtocolLimits.Default,
                sessionGeneration: 3);

            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.Accepted,
                scheduler.PrepareEnqueue(frame, out var cancelled));
            Assert.Equal(wireBytes, scheduler.TransientBytes);
            cancelled.Dispose();
            Assert.Equal(0UL, scheduler.TransientBytes);
            Assert.Equal(0UL, scheduler.DataQueuedDepth);

            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.Accepted,
                scheduler.PrepareEnqueue(frame, out var raced));
            Assert.Equal(wireBytes, scheduler.TransientBytes);
            scheduler.Close();
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.RejectedAfterStop,
                scheduler.CommitPrepared(
                    raced,
                    U2R2QueueOverflowPolicy.Reject));
            Assert.Equal(0UL, scheduler.TransientBytes);
            Assert.Equal(0UL, scheduler.DataQueuedDepth);
            Assert.Equal(1UL, scheduler.Counters.RejectedAfterStop);
        }

        [Fact]
        public void TrustedAuthorityPathTakesOwnershipButPublicFactoryClones()
        {
            var key = new U2R2ContractKey(1, 1);
            var ownedBytes = new byte[] { 0x01, 0x02 };
            var owned = U2R2OutboundFrame.DataOwned(
                "owned",
                key,
                1,
                ownedBytes);
            Assert.True(MemoryMarshal.TryGetArray(
                owned.Bytes,
                out ArraySegment<byte> ownedSegment));
            Assert.Same(ownedBytes, ownedSegment.Array);

            var callerBytes = new byte[] { 0x03, 0x04 };
            var defensive = U2R2OutboundFrame.Data(
                "public",
                key,
                2,
                callerBytes);
            Assert.True(MemoryMarshal.TryGetArray(
                defensive.Bytes,
                out ArraySegment<byte> defensiveSegment));
            Assert.NotSame(callerBytes, defensiveSegment.Array);
            callerBytes[0] = 0xff;
            Assert.Equal(0x03, defensive.Bytes.Span[0]);
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

    }
}
