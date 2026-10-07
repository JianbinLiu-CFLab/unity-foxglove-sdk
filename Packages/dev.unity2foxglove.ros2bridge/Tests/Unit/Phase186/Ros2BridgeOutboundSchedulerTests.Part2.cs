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
