// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first durable retirement for blocked duplex workers.

using System;
using System.Collections.Concurrent;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeConnectionRetirementTests
    {
        [Fact]
        public void BlockedReaderRetainsExclusiveSlotUntilItActuallyExits()
        {
            var owner =
                FoxRunTransportRetirementOwner.CreateForTests(
                    capacity: 2);
            var providerId = new FoxRunTransportId(
                "unity2foxglove.ros2bridge.retirement");
            Assert.True(owner.TryReserveExclusive(
                providerId,
                FoxRunTransportDirection.Subscribe,
                generation: 7,
                workerCount: 2,
                out var reservation));
            using var transport = new BlockingReadTransport();
            var connection = new Ros2BridgeConnection(
                transport,
                U2R2ProtocolLimits.Default,
                requiresSubscription: true,
                writerCapacity: 2,
                pendingCapacity: 2,
                timeoutMs: 1000,
                retirement: reservation,
                readerRetirementIndex: 0,
                writerRetirementIndex: 1,
                retirementIdentity: "phase186/duplex");
            connection.Start();

            connection.Dispose();

            Assert.Equal(1, owner.RetiredCount);
            Assert.Equal(1, owner.OccupiedCount);
            Assert.False(owner.TryReserveExclusive(
                providerId,
                FoxRunTransportDirection.Subscribe,
                generation: 8,
                workerCount: 2,
                out _));

            transport.ReleaseBlockedRead();
            Assert.True(SpinWait.SpinUntil(
                () => owner.OccupiedCount == 0,
                TimeSpan.FromSeconds(2)));
            Assert.True(transport.IsDisposed);
            Assert.True(owner.TryReserveExclusive(
                providerId,
                FoxRunTransportDirection.Subscribe,
                generation: 8,
                workerCount: 2,
                out var replacement));
            replacement.Dispose();
        }

        private sealed class BlockingReadTransport :
            IRos2BridgeSessionTransport
        {
            private readonly BlockingCollection<byte[]> _responses =
                new BlockingCollection<byte[]>();
            private readonly ManualResetEventSlim _releaseRead =
                new ManualResetEventSlim(false);
            private int _readCount;

            public bool IsConnected => true;

            internal bool IsDisposed { get; private set; }

            public void BeginV2(
                U2R2ProtocolLimits limits,
                int timeoutMs)
            {
            }

            public void WriteV2(
                ReadOnlyMemory<byte> wireBytes,
                U2R2ProtocolLimits limits,
                int timeoutMs)
            {
                var request = U2R2ProtocolCodec.ParseV2(
                    U2R2ProtocolCodec.DecodeFrame(
                        wireBytes.ToArray(),
                        limits));
                if (request.Operation != U2R2Operation.Hello)
                    return;
                _responses.Add(
                    U2R2ProtocolCodec.EncodeFrame(
                        new JObject
                        {
                            ["capabilities"] =
                                new JArray(
                                    "publish",
                                    "subscribe"),
                            ["connectionGeneration"] = 19,
                            ["op"] = "hello_ack",
                            ["protocolVersion"] = 2,
                            ["requestId"] = request.RequestId,
                            ["sessionId"] =
                                "phase186-retirement",
                            ["status"] = "ok",
                        },
                        Array.Empty<byte>(),
                        limits));
            }

            public byte[] ReadV2(
                U2R2ProtocolLimits limits,
                int timeoutMs)
            {
                if (Interlocked.Increment(ref _readCount) == 1)
                {
                    return _responses.Take();
                }
                _releaseRead.Wait();
                throw new ObjectDisposedException(
                    nameof(BlockingReadTransport));
            }

            public void Close()
            {
            }

            internal void ReleaseBlockedRead()
                => _releaseRead.Set();

            public void Dispose()
            {
                IsDisposed = true;
                _responses.Dispose();
                _releaseRead.Dispose();
            }
        }
    }
}
