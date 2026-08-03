// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first ownership and immutability checks for Bridge sessions.

using System;
using System.Collections;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeSessionContractTests
    {
        [Fact]
        public void SnapshotFreezesContractsAndRejectsDuplicateWireIds()
        {
            var first = Contract(contractId: 11, bindingId: "binding-a");
            var second = Contract(contractId: 12, bindingId: "binding-b");
            var snapshot = new Ros2BridgeSessionContractSnapshot(
                generation: 7,
                new[] { second, first });

            Assert.Equal(7UL, snapshot.Generation);
            Assert.Equal(new[] { 11UL, 12UL }, snapshot.Contracts
                .Select(contract => contract.ContractId)
                .ToArray());
            Assert.Throws<NotSupportedException>(
                () => ((IList)snapshot.Contracts).Add(first));

            var duplicate = Contract(
                contractId: 11,
                bindingId: "binding-c",
                topic: "/phase186/other");
            Assert.Throws<ArgumentException>(
                () => new Ros2BridgeSessionContractSnapshot(
                    generation: 7,
                    new[] { first, duplicate }));
        }

        [Fact]
        public void ContractRequiresCompleteImmutableIdentity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Contract(contractId: 0, bindingId: "binding-a"));
            Assert.Throws<ArgumentException>(
                () => Contract(contractId: 1, bindingId: string.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Ros2BridgeSessionContract(
                    new FoxRunTransportId(
                        "unity2foxglove.ros2bridge"),
                    FoxRunTransportDirection.Subscribe,
                    "/phase186/session",
                    "phase186_msgs/msg/Session",
                    FoxRunResolvedQos.Default,
                    "binding-a",
                    contractId: 1,
                    generation: 0));
        }

        [Fact]
        public void InboundFrameExposesOnlyLogicalSliceAndReleasesOnce()
        {
            var storage = Enumerable.Repeat((byte)0xee, 32).ToArray();
            storage[5] = 0;
            storage[6] = 1;
            storage[7] = 0;
            storage[8] = 0;
            var releases = 0;
            var frame = Ros2BridgeInboundFrame.CreateOwned(
                Contract(contractId: 17, bindingId: "binding-frame"),
                sessionId: "phase186-session",
                connectionGeneration: 9,
                messageId: 3,
                sequence: 4,
                receiveTimeNs: 5,
                storage,
                payloadOffset: 5,
                payloadLength: 4,
                release: _ => releases++);

            Assert.Equal(
                new byte[] { 0, 1, 0, 0 },
                frame.Payload.ToArray());
            Assert.Equal(4, frame.PayloadLength);

            frame.Dispose();
            frame.Dispose();

            Assert.Equal(1, releases);
            Assert.Throws<ObjectDisposedException>(
                () => frame.Payload.ToArray());
        }

        private static Ros2BridgeSessionContract Contract(
            ulong contractId,
            string bindingId,
            string topic = "/phase186/session")
            => new Ros2BridgeSessionContract(
                new FoxRunTransportId(
                    "unity2foxglove.ros2bridge"),
                FoxRunTransportDirection.Subscribe,
                topic,
                "phase186_msgs/msg/Session",
                FoxRunResolvedQos.Default,
                bindingId,
                contractId,
                generation: 7);
    }
}
