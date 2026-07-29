// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunMessagePackTransactionTests
    {
        [Fact]
        [Trait("Phase", "185-C")]
        public void StreamReservationIsSingleFlightAndConsumesRateOnCancel()
        {
            long now = 100;
            var stream = new FoxRunStream<OwnedProbe>(
                new FoxRunStreamOptions(
                    capacity: 1,
                    maxBatch: 1,
                    maxInputHz: 10,
                    overflow: FoxRunStreamOverflowPolicy.DropNewest),
                () => now,
                timestampFrequency: 1000);
            var ingress =
                Assert.IsAssignableFrom<IFoxRunStreamInputIngress<OwnedProbe>>(
                    stream);

            Assert.True(ingress.TryReserveInput(out var first));
            Assert.False(ingress.TryReserveInput(out _));
            ingress.CancelInput(first);
            Assert.False(ingress.TryReserveInput(out _));

            now += 100;
            Assert.True(ingress.TryReserveInput(out var second));
            var probe = new OwnedProbe();
            Assert.True(
                ingress.CommitOwnedInput(
                    second,
                    probe,
                    value => value.Dispose()));
            Assert.True(stream.TryTake(out var sample));
            sample.Dispose();
            Assert.Equal(1, probe.DisposeCount);
        }

        [Fact]
        [Trait("Phase", "185-C")]
        public void CommitAfterDisposeRejectsAndReleasesExactlyOnce()
        {
            var stream = new FoxRunStream<OwnedProbe>();
            var ingress =
                (IFoxRunStreamInputIngress<OwnedProbe>)stream;
            Assert.True(ingress.TryReserveInput(out var reservation));
            stream.Dispose();
            var probe = new OwnedProbe();

            Assert.False(
                ingress.CommitOwnedInput(
                    reservation,
                    probe,
                    value => value.Dispose()));
            Assert.Equal(1, probe.DisposeCount);
            Assert.Equal(0, stream.Count);
        }

        [Fact]
        [Trait("Phase", "185-C")]
        public void CommitAfterCancelRejectsAndReleasesExactlyOnce()
        {
            var stream = new FoxRunStream<OwnedProbe>();
            var ingress = (IFoxRunStreamInputIngress<OwnedProbe>)stream;
            Assert.True(ingress.TryReserveInput(out var reservation));
            ingress.CancelInput(reservation);
            var probe = new OwnedProbe();

            Assert.False(
                ingress.CommitOwnedInput(
                    reservation,
                    probe,
                    value => value.Dispose()));
            Assert.Equal(1, probe.DisposeCount);
            Assert.Equal(0, stream.Count);
        }

        [Theory]
        [Trait("Phase", "185-C")]
        [InlineData(FoxRunStreamOverflowPolicy.DropNewest)]
        [InlineData(FoxRunStreamOverflowPolicy.DropOldest)]
        public void ReservedCommitAppliesOverflowWithExactlyOnceOwnership(
            FoxRunStreamOverflowPolicy overflow)
        {
            long now = 100;
            var stream = new FoxRunStream<OwnedProbe>(
                new FoxRunStreamOptions(
                    capacity: 1,
                    maxInputHz: 1000,
                    maxBatch: 1,
                    overflow),
                () => now,
                timestampFrequency: 1000);
            var ingress = (IFoxRunStreamInputIngress<OwnedProbe>)stream;
            var first = new OwnedProbe();
            var second = new OwnedProbe();

            Assert.True(ingress.TryReserveInput(out var firstReservation));
            Assert.True(
                ingress.CommitOwnedInput(
                    firstReservation,
                    first,
                    value => value.Dispose()));
            now++;
            Assert.True(ingress.TryReserveInput(out var secondReservation));
            var secondAccepted = ingress.CommitOwnedInput(
                secondReservation,
                second,
                value => value.Dispose());

            if (overflow == FoxRunStreamOverflowPolicy.DropNewest)
            {
                Assert.False(secondAccepted);
                Assert.Equal(0, first.DisposeCount);
                Assert.Equal(1, second.DisposeCount);
            }
            else
            {
                Assert.True(secondAccepted);
                Assert.Equal(1, first.DisposeCount);
                Assert.Equal(0, second.DisposeCount);
            }

            Assert.True(stream.TryTake(out var sample));
            Assert.Same(
                overflow == FoxRunStreamOverflowPolicy.DropNewest
                    ? first
                    : second,
                sample.Value);
            sample.Dispose();
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, second.DisposeCount);
        }

        [Fact]
        [Trait("Phase", "185-C")]
        public void RouterFreezesPayloadLimitAndUsesTransactionalIndexSpace()
        {
            const string topic = "/phase185/frozen";
            var source = new TransactionSource(topic);
            var router = new FoxRunInputRouter(maxPayloadBytes: 4)
            {
                DefaultSubscriptionEncoding = FoxRunEncoding.MessagePack,
                DefaultSubscriptionSource = FoxRunEndpoint.Foxglove
            };
            RegisterManifest(source, topic);
            try
            {
                router.Register(source);
                router.MaxPayloadBytes = 64;

                Assert.Equal(
                    FoxRunInputDispatchStatus.PayloadTooLarge,
                    router.Dispatch(
                        topic,
                        new byte[5],
                        "msgpack",
                        1d).Status);
                Assert.Equal(0, source.LegacyStageCount);
                Assert.Equal(0, source.TransactionStageCount);

                router.Unregister(source);
                Assert.Equal(new[] { 0 }, source.ClearedTransactions);
                router.Register(source);

                Assert.Equal(
                    FoxRunInputDispatchStatus.Staged,
                    router.Dispatch(
                        topic,
                        new byte[5],
                        "msgpack",
                        2d).Status);
                Assert.Equal(0, source.LegacyStageCount);
                Assert.Equal(1, source.TransactionStageCount);
                Assert.Equal(64, source.LastLimits.MaxStringBytes);
            }
            finally
            {
                router.Unregister(source);
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        private static void RegisterManifest(
            TransactionSource source,
            string topic)
        {
            FoxRunSchemaInfoRegistry.ClearForTests();
            var declaringType = source.GetType().FullName!.Replace('+', '.');
            FoxRunSchemaInfoRegistry.RegisterGenerated(
                new FoxRunSchemaManifestInfo(
                    5,
                    "Unity2Foxglove",
                    "FoxRun",
                    1,
                    "phase185-transaction",
                    "phase185-transaction",
                    new[]
                    {
                        new FoxRunSchemaTypeInfo(
                            declaringType,
                            new[]
                            {
                                new FoxRunSchemaContractInfo(
                                    declaringType,
                                    topic,
                                    string.Empty,
                                    "msgpack",
                                    "msgpack",
                                    "msgpack",
                                    "policy",
                                    "FixedRate",
                                    10f,
                                    0f,
                                    Array.Empty<FoxRunSchemaFieldInfo>(),
                                    flow: "Subscribe",
                                    subscribeAvailable: true)
                            })
                    }));
        }

        private sealed class TransactionSource :
            IFoxgloveInputSource,
            IFoxgloveTransactionalInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            internal TransactionSource(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(
                    topic,
                    FoxRunEncoding.MessagePack,
                    FoxRunFlow.Subscribe,
                    FoxRunEndpoint.Foxglove,
                    supportsWebSocket: true,
                    supportsRos2Native: false);
            }

            internal int LegacyStageCount { get; private set; }
            internal int TransactionStageCount { get; private set; }
            internal FoxgloveMsgPackReadLimits LastLimits { get; private set; }
            internal List<int> ClearedTransactions { get; } = new();

            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index)
                => _topic;
            public bool FoxgloveInput_TryStage(
                int topicIndex,
                byte[] payload,
                string encoding,
                out string error)
            {
                LegacyStageCount++;
                error = string.Empty;
                return true;
            }
            public int FoxgloveInput_Flush(
                double nowSeconds,
                int inheritedSubscribeRateHz)
                => 0;

            public int FoxgloveInput_TransactionCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTransaction(
                int transactionIndex)
                => _topic;
            public bool FoxgloveInput_TryStageTransaction(
                int transactionIndex,
                byte[] payload,
                FoxgloveMsgPackReadLimits limits,
                out string error)
            {
                TransactionStageCount++;
                LastLimits = limits;
                error = string.Empty;
                return true;
            }
            public void FoxgloveInput_ClearTransaction(int transactionIndex)
                => ClearedTransactions.Add(transactionIndex);
        }

        private sealed class OwnedProbe : IDisposable
        {
            internal int DisposeCount { get; private set; }
            public void Dispose() => DisposeCount++;
        }
    }
}
