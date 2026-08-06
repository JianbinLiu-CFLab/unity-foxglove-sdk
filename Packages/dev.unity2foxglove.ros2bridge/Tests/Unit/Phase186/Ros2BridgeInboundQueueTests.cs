// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first bounded inbound ownership and sequence checks.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeInboundQueueTests
    {
        [Fact]
        public void OwnedSliceOverflowUsesTheDocumentedRangeFailure()
        {
            var contract = Contract(11, "binding-a");

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Ros2BridgeInboundFrame.CreateOwned(
                    contract,
                    "phase186-session",
                    connectionGeneration: 19,
                    messageId: 1,
                    sequence: 1,
                    receiveTimeNs: 2,
                    new byte[1],
                    payloadOffset: int.MaxValue,
                    payloadLength: 1,
                    release: _ => { }));
        }

        [Fact]
        public void LogicalSliceIsCopiedOnceAppliedAndReturnedExactlyOnce()
        {
            var pool = new TrackingPool(extraCapacity: 32);
            var contract = Contract(11, "binding-a");
            using var queue = Queue(
                new[] { contract },
                maxPayloadBytes: 16,
                maxTotalBytes: 64,
                maxPerContractDepth: 2,
                maxPerContractBytes: 32);
            var source = new byte[]
            {
                0xee, 0xee,
                0x00, 0x01, 0x00, 0x00, 0x2a,
                0xdd, 0xdd,
            };
            var frame = Ros2BridgeInboundFrame.CopyOwned(
                contract,
                "phase186-session",
                connectionGeneration: 19,
                messageId: 1,
                sequence: 1,
                receiveTimeNs: 2,
                new ReadOnlyMemory<byte>(source, 2, 5),
                pool);

            Assert.Equal(
                Ros2BridgeSessionResultState.Accepted,
                queue.TryAccept(frame).State);
            Assert.Equal(1, pool.RentCount);
            Assert.Equal(1, pool.Outstanding);
            Assert.True(queue.TryBeginApply(out var apply));
            using (apply)
            {
                Assert.True(apply.CanApply);
                Assert.Equal(
                    new byte[] { 0, 1, 0, 0, 0x2a },
                    apply.Frame.Payload.ToArray());
                apply.MarkApplied();
            }

            var stats = queue.GetStatsSnapshot();
            Assert.Equal(1, stats.Received);
            Assert.Equal(1, stats.Accepted);
            Assert.Equal(1, stats.Applied);
            Assert.Equal(0, stats.QueuedBytes);
            Assert.Equal(0, stats.InFlightBytes);
            Assert.Equal(0, pool.Outstanding);
            Assert.Equal(1, pool.ReturnCount);
        }

        [Fact]
        public void ReplacementNeverEvictsAnotherContract()
        {
            var first = Contract(11, "binding-a", "/phase186/a");
            var second = Contract(12, "binding-b", "/phase186/b");
            var released = new List<ulong>();
            using var queue = Queue(
                new[] { first, second },
                maxPayloadBytes: 8,
                maxTotalBytes: 16,
                maxPerContractDepth: 1,
                maxPerContractBytes: 8);

            Assert.True(queue.TryAccept(
                Frame(first, sequence: 1, released)).IsAccepted);
            Assert.True(queue.TryAccept(
                Frame(second, sequence: 1, released)).IsAccepted);
            Assert.True(queue.TryAccept(
                Frame(first, sequence: 2, released)).IsAccepted);

            Assert.Equal(new[] { 1UL }, released);
            Assert.True(queue.TryBeginApply(out var secondApply));
            using (secondApply)
            {
                Assert.Equal(12UL, secondApply.Frame.Contract.ContractId);
                secondApply.MarkApplied();
            }
            Assert.True(queue.TryBeginApply(out var firstApply));
            using (firstApply)
            {
                Assert.Equal(11UL, firstApply.Frame.Contract.ContractId);
                Assert.Equal(2UL, firstApply.Frame.Sequence);
                firstApply.MarkApplied();
            }
            Assert.False(queue.TryBeginApply(out _));

            var stats = queue.GetStatsSnapshot();
            Assert.Equal(3, stats.Accepted);
            Assert.Equal(1, stats.Replaced);
            Assert.Equal(2, stats.Applied);
        }

        [Fact]
        public void SequenceFaultIsStickyContractLocalAndObservable()
        {
            var contract = Contract(11, "binding-a");
            var healthy = Contract(12, "binding-b");
            var released = new List<ulong>();
            using var queue = Queue(
                new[] { contract, healthy },
                maxPayloadBytes: 5,
                maxTotalBytes: 20,
                maxPerContractDepth: 4,
                maxPerContractBytes: 20);

            Assert.True(queue.TryAccept(
                Frame(contract, sequence: 1, released)).IsAccepted);
            Assert.Equal(
                Ros2BridgeSessionResultState.Rejected,
                queue.TryAccept(
                    Frame(contract, sequence: 3, released)).State);
            Assert.Equal(
                Ros2BridgeSessionResultState.Rejected,
                queue.TryAccept(
                    Frame(contract, sequence: 2, released)).State);
            Assert.Equal(
                Ros2BridgeSessionResultState.Rejected,
                queue.TryAccept(
                    Frame(
                        healthy,
                        sequence: 1,
                        released,
                        payloadLength: 6)).State);
            Assert.True(queue.TryAccept(
                Frame(healthy, sequence: 1, released)).IsAccepted);
            Assert.Equal(3, released.Count);

            Assert.True(queue.TryBeginApply(out var firstApply));
            using (firstApply)
            {
                Assert.Equal(11UL, firstApply.Frame.Contract.ContractId);
                Assert.Equal(1UL, firstApply.Frame.Sequence);
                firstApply.MarkApplied();
            }
            Assert.True(queue.TryBeginApply(out var healthyApply));
            using (healthyApply)
            {
                Assert.Equal(12UL, healthyApply.Frame.Contract.ContractId);
                Assert.Equal(1UL, healthyApply.Frame.Sequence);
                healthyApply.MarkApplied();
            }
            Assert.False(queue.TryBeginApply(out _));

            var stats = queue.GetStatsSnapshot();
            Assert.Equal(5, stats.Received);
            Assert.Equal(2, stats.Accepted);
            Assert.Equal(1, stats.SequenceGaps);
            Assert.Equal(1, stats.StaleSequences);
            Assert.Equal(1, stats.Oversize);
            Assert.Equal(5, released.Count);
        }

        [Fact]
        public void StaleSessionUnknownContractAndStopDisposeOwnership()
        {
            var active = Contract(11, "binding-a");
            var unknown = Contract(12, "binding-b");
            var released = new List<ulong>();
            using var queue = Queue(
                new[] { active },
                maxPayloadBytes: 8,
                maxTotalBytes: 16,
                maxPerContractDepth: 2,
                maxPerContractBytes: 16);

            Assert.Equal(
                Ros2BridgeSessionResultState.Faulted,
                queue.TryAccept(
                    Frame(
                        active,
                        sequence: 1,
                        released,
                        connectionGeneration: 18)).State);
            Assert.Equal(
                Ros2BridgeSessionResultState.Faulted,
                queue.TryAccept(
                    Frame(unknown, sequence: 1, released)).State);
            Assert.True(queue.TryAccept(
                Frame(active, sequence: 1, released)).IsAccepted);

            queue.Stop();

            Assert.Equal(3, released.Count);
            Assert.Equal(
                Ros2BridgeSessionResultState.Rejected,
                queue.TryAccept(
                    Frame(active, sequence: 2, released)).State);
            var stats = queue.GetStatsSnapshot();
            Assert.Equal(1, stats.RejectedAfterStop);
            Assert.Equal(4, released.Count);
            Assert.False(queue.TryBeginApply(out _));
        }

        [Fact]
        public void ResolutionRejectionIsObservableWithoutDegradingSession()
        {
            var contract = Contract(11, "binding-a");
            using var queue = Queue(
                new[] { contract },
                maxPayloadBytes: 8,
                maxTotalBytes: 16,
                maxPerContractDepth: 2,
                maxPerContractBytes: 16);

            queue.RecordResolutionRejection(
                "The inbound Bridge message belongs to a released contract.");

            var stats = queue.GetStatsSnapshot();
            Assert.Equal(1, stats.Received);
            Assert.Equal(1, stats.ResolutionRejections);
            Assert.False(stats.HasSessionDeliveryFailure);
            Assert.Contains("released contract", stats.LastDiagnostic);
        }

        [Fact]
        public void ReconnectRevokesInFlightApplyAndDecodeFailureStillReleases()
        {
            var contract = Contract(11, "binding-a");
            var released = new List<ulong>();
            using var queue = Queue(
                new[] { contract },
                maxPayloadBytes: 8,
                maxTotalBytes: 16,
                maxPerContractDepth: 2,
                maxPerContractBytes: 16);
            Assert.True(queue.TryAccept(
                Frame(contract, sequence: 1, released)).IsAccepted);
            Assert.True(queue.TryBeginApply(out var apply));

            queue.BeginSession(
                "phase186-next",
                connectionGeneration: 20,
                new Ros2BridgeSessionContractSnapshot(
                    generation: 7,
                    new[] { contract }));

            Assert.False(apply.CanApply);
            apply.MarkDecodeFailure("reconnect revoked apply");
            apply.Dispose();

            Assert.Single(released);
            var stats = queue.GetStatsSnapshot();
            Assert.Equal(1, stats.DecodeFailures);
            Assert.Equal(0, stats.InFlightBytes);
            Assert.False(stats.HasSessionDeliveryFailure);
            Assert.Empty(stats.LastDiagnostic);
        }

        [Fact]
        public void InvalidSessionSnapshotDoesNotDisplaceCurrentOwnedFrames()
        {
            var active = Contract(11, "binding-a");
            var invalid = Contract(
                12,
                "binding-b",
                direction: FoxRunTransportDirection.Publish);
            var released = new List<ulong>();
            using var queue = Queue(
                new[] { active },
                maxPayloadBytes: 8,
                maxTotalBytes: 16,
                maxPerContractDepth: 2,
                maxPerContractBytes: 16);
            Assert.True(queue.TryAccept(
                Frame(active, sequence: 1, released)).IsAccepted);

            Assert.Throws<ArgumentException>(
                () => queue.BeginSession(
                    "phase186-invalid",
                    connectionGeneration: 20,
                    new Ros2BridgeSessionContractSnapshot(
                        generation: 7,
                        new[] { invalid })));

            Assert.Empty(released);
            Assert.True(queue.TryBeginApply(out var apply));
            using (apply)
            {
                Assert.Equal(11UL, apply.Frame.Contract.ContractId);
                apply.MarkApplied();
            }
            Assert.Single(released);
        }

        [Fact]
        public void ExhaustedSessionEpochDoesNotDisplaceCurrentOwnedFrames()
        {
            var contract = Contract(11, "binding-a");
            var released = new List<ulong>();
            using var queue = Queue(
                new[] { contract },
                maxPayloadBytes: 8,
                maxTotalBytes: 16,
                maxPerContractDepth: 2,
                maxPerContractBytes: 16);
            Assert.True(queue.TryAccept(
                Frame(contract, sequence: 1, released)).IsAccepted);
            var epoch = RequiredField("_epoch");
            epoch.SetValue(queue, long.MaxValue);

            try
            {
                Assert.Throws<InvalidOperationException>(
                    () => queue.BeginSession(
                        "phase186-next",
                        connectionGeneration: 20,
                        new Ros2BridgeSessionContractSnapshot(
                            generation: 7,
                            new[] { contract })));
            }
            finally
            {
                epoch.SetValue(queue, 1L);
            }

            Assert.Empty(released);
            Assert.True(queue.TryBeginApply(out var apply));
            using (apply)
            {
                Assert.Equal("phase186-session", apply.Frame.SessionId);
                apply.MarkApplied();
            }
            Assert.Single(released);
        }

        [Fact]
        public void OverflowingAdmissionAccountingRejectsAndReleasesIncomingFrame()
        {
            var contract = Contract(11, "binding-a");
            var released = new List<ulong>();
            using var queue = Queue(
                new[] { contract },
                maxPayloadBytes: 8,
                maxTotalBytes: long.MaxValue,
                maxPerContractDepth: 2,
                maxPerContractBytes: long.MaxValue);
            Assert.True(queue.TryAccept(
                Frame(contract, sequence: 1, released)).IsAccepted);

            var usageMap = RequiredField("_usage").GetValue(queue);
            Assert.NotNull(usageMap);
            var indexer = usageMap.GetType().GetProperty("Item");
            Assert.NotNull(indexer);
            var usage = indexer.GetValue(usageMap, new object[] { 11UL });
            Assert.NotNull(usage);
            var bytes = usage.GetType().GetField(
                "Bytes",
                BindingFlags.Instance | BindingFlags.NonPublic
                | BindingFlags.Public);
            Assert.NotNull(bytes);
            bytes.SetValue(usage, long.MaxValue);
            var incoming = Frame(contract, sequence: 2, released);

            try
            {
                var result = queue.TryAccept(incoming);
                Assert.Equal(
                    Ros2BridgeSessionResultState.Rejected,
                    result.State);
                Assert.Contains("capacity", result.Reason);
                Assert.Equal(new[] { 2UL }, released);
            }
            finally
            {
                bytes.SetValue(usage, 4L);
                incoming.Dispose();
            }

            Assert.True(queue.TryBeginApply(out var apply));
            using (apply)
            {
                Assert.Equal(1UL, apply.Frame.Sequence);
                apply.MarkApplied();
            }
        }

        [Fact]
        public void ReconnectClearsSessionFailureWithoutResettingLifetimeCounters()
        {
            var contract = Contract(11, "binding-a");
            var released = new List<ulong>();
            using var queue = Queue(
                new[] { contract },
                maxPayloadBytes: 8,
                maxTotalBytes: 16,
                maxPerContractDepth: 2,
                maxPerContractBytes: 16);
            Assert.True(queue.TryAccept(
                Frame(contract, sequence: 1, released)).IsAccepted);
            Assert.Equal(
                Ros2BridgeSessionResultState.Rejected,
                queue.TryAccept(
                    Frame(contract, sequence: 3, released)).State);
            Assert.True(
                queue.GetStatsSnapshot().HasSessionDeliveryFailure);

            queue.BeginSession(
                "phase186-next",
                connectionGeneration: 20,
                new Ros2BridgeSessionContractSnapshot(
                    generation: 7,
                    new[] { contract }));

            var recovered = queue.GetStatsSnapshot();
            Assert.Equal(1, recovered.SequenceGaps);
            Assert.False(recovered.HasSessionDeliveryFailure);
            Assert.Empty(recovered.LastDiagnostic);
        }

        [Fact]
        public void RevokingOneContractDropsOnlyItsQueuedAndInFlightOwnership()
        {
            var first = Contract(11, "binding-a", "/phase186/a");
            var second = Contract(12, "binding-b", "/phase186/b");
            var released = new List<ulong>();
            using var queue = Queue(
                new[] { first, second },
                maxPayloadBytes: 8,
                maxTotalBytes: 32,
                maxPerContractDepth: 2,
                maxPerContractBytes: 16);
            Assert.True(queue.TryAccept(
                Frame(first, sequence: 1, released)).IsAccepted);
            Assert.True(queue.TryBeginApply(out var firstApply));
            Assert.True(queue.TryAccept(
                Frame(first, sequence: 2, released)).IsAccepted);
            Assert.True(queue.TryAccept(
                Frame(second, sequence: 1, released)).IsAccepted);

            Assert.True(queue.TryRevokeContract(first, out var reason));
            Assert.Empty(reason);
            Assert.False(firstApply.CanApply);
            Assert.Equal(new[] { 2UL }, released);
            firstApply.MarkDecodeFailure("contract revoked");
            firstApply.Dispose();
            Assert.Equal(new[] { 2UL, 1UL }, released);

            Assert.Equal(
                Ros2BridgeSessionResultState.Faulted,
                queue.TryAccept(
                    Frame(first, sequence: 3, released)).State);
            Assert.True(queue.TryBeginApply(out var secondApply));
            using (secondApply)
            {
                Assert.True(secondApply.CanApply);
                Assert.Equal(12UL, secondApply.Frame.Contract.ContractId);
                Assert.Equal(1UL, secondApply.Frame.Sequence);
                secondApply.MarkApplied();
            }
            Assert.False(queue.TryBeginApply(out _));
            Assert.Equal(new[] { 2UL, 1UL, 3UL, 1UL }, released);
        }

        private static Ros2BridgeInboundQueue Queue(
            Ros2BridgeSessionContract[] contracts,
            int maxPayloadBytes,
            long maxTotalBytes,
            int maxPerContractDepth,
            long maxPerContractBytes)
        {
            var queue = new Ros2BridgeInboundQueue(
                new Ros2BridgeInboundQueueLimits(
                    maxPayloadBytes,
                    maxTotalBytes,
                    maxPerContractDepth,
                    maxPerContractBytes));
            queue.BeginSession(
                "phase186-session",
                connectionGeneration: 19,
                new Ros2BridgeSessionContractSnapshot(
                    generation: 7,
                    contracts));
            return queue;
        }

        private static Ros2BridgeInboundFrame Frame(
            Ros2BridgeSessionContract contract,
            ulong sequence,
            ICollection<ulong> released,
            int payloadLength = 4,
            ulong connectionGeneration = 19)
        {
            var storage = new byte[payloadLength];
            if (payloadLength >= 4)
            {
                storage[0] = 0;
                storage[1] = 1;
            }
            return Ros2BridgeInboundFrame.CreateOwned(
                contract,
                "phase186-session",
                connectionGeneration,
                messageId: sequence,
                sequence,
                receiveTimeNs: sequence,
                storage,
                payloadOffset: 0,
                payloadLength,
                _ => released.Add(sequence));
        }

        private static Ros2BridgeSessionContract Contract(
            ulong contractId,
            string bindingId,
            string topic = "/phase186/inbound",
            FoxRunTransportDirection direction =
                FoxRunTransportDirection.Subscribe)
            => new Ros2BridgeSessionContract(
                new FoxRunTransportId(
                    "unity2foxglove.ros2bridge"),
                direction,
                topic,
                "phase186_msgs/msg/Inbound",
                FoxRunResolvedQos.Default,
                bindingId,
                contractId,
                generation: 7);

        private static FieldInfo RequiredField(string name)
            => typeof(Ros2BridgeInboundQueue).GetField(
                   name,
                   BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException(
                   "Required inbound queue field is missing: " + name);

        private sealed class TrackingPool : IRos2BridgeBytePool
        {
            private readonly int _extraCapacity;
            private readonly HashSet<byte[]> _outstanding =
                new HashSet<byte[]>();

            internal TrackingPool(int extraCapacity)
            {
                _extraCapacity = extraCapacity;
            }

            internal int RentCount { get; private set; }

            internal int ReturnCount { get; private set; }

            internal int Outstanding => _outstanding.Count;

            public byte[] Rent(int minimumLength)
            {
                var storage = Enumerable
                    .Repeat(
                        (byte)0xee,
                        checked(minimumLength + _extraCapacity))
                    .ToArray();
                Assert.True(_outstanding.Add(storage));
                RentCount++;
                return storage;
            }

            public void Return(byte[] storage)
            {
                Assert.True(_outstanding.Remove(storage));
                ReturnCount++;
            }
        }
    }
}
