// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Runtime regressions for frozen Provider selection and hidden MCAP semantics.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.FoxRun
{
    public sealed class FoxgloveLogHubProviderSessionTests
    {
        private static readonly FieldInfo InstanceField =
            typeof(FoxgloveLogHub).GetField(
                "_instance",
                BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly FieldInfo ManagerField =
            typeof(FoxgloveLogHub).GetField(
                "_manager",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo AddSourceMethod =
            typeof(FoxgloveLogHub).GetMethod(
                "AddSourceNow",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo TryPublishMethod =
            typeof(FoxgloveLogHub).GetMethod(
                "TryPublish",
                BindingFlags.Instance | BindingFlags.NonPublic);

        [Fact]
        public void InheritedPublishUsesFrozenWebSocketSelection()
        {
            using var fixture = new Fixture(
                activeProviderIds: new[]
                {
                    FoxgloveWebSocketTransport.TransportId
                },
                nextSessionProviderIds: new[]
                {
                    new FoxRunTransportId("unity2foxglove.r2fu")
                });

            Assert.True(fixture.Publish());
            Assert.Equal(1, fixture.Source.WebSocketPublishes);
            Assert.Equal(0, fixture.Source.RecordingReadinessChecks);
            Assert.Equal(0, fixture.Source.RecordingPublishes);
            Assert.Equal(1, fixture.Source.WebSocketEncodingSets);
        }

        [Fact]
        public void HiddenRecordingCannotConsumeUnavailableSelectedProvider()
        {
            using var fixture = new Fixture(
                activeProviderIds: new[]
                {
                    new FoxRunTransportId("unity2foxglove.r2fu")
                },
                nextSessionProviderIds: new[]
                {
                    FoxgloveWebSocketTransport.TransportId
                });

            Assert.False(fixture.Publish());
            Assert.Equal(0, fixture.Source.WebSocketPublishes);
            Assert.Equal(1, fixture.Source.RecordingReadinessChecks);
            Assert.Equal(1, fixture.Source.RecordingPublishes);
            Assert.Equal(1, fixture.Source.CaptureEnds);
            Assert.Equal(0, fixture.Source.WebSocketEncodingSets);
        }

        [Fact]
        public void ProviderlessDeclarationMayReportRecordingOnlySuccess()
        {
            using var fixture = new Fixture(
                activeProviderIds: Array.Empty<FoxRunTransportId>(),
                nextSessionProviderIds: new[]
                {
                    FoxgloveWebSocketTransport.TransportId
                });

            Assert.True(fixture.Publish());
            Assert.Equal(0, fixture.Source.WebSocketPublishes);
            Assert.Equal(1, fixture.Source.RecordingReadinessChecks);
            Assert.Equal(1, fixture.Source.RecordingPublishes);
            Assert.Equal(0, fixture.Source.WebSocketEncodingSets);
        }

        [Fact]
        public void RosOnlyInheritedPublishRegistersAdditiveSinkAsJson()
        {
            using var fixture = new Fixture(
                activeProviderIds: new[]
                {
                    new FoxRunTransportId("unity2foxglove.r2fu")
                },
                nextSessionProviderIds: new[]
                {
                    FoxgloveWebSocketTransport.TransportId
                },
                addSink: true);

            var registered =
                Assert.Single(fixture.Sink.Registered);
            Assert.Equal("json", registered.Encoding);
            Assert.True(fixture.Publish());
            var published =
                Assert.Single(fixture.Sink.Published);
            Assert.Equal("json", published.Encoding);
            Assert.Equal(0, fixture.Source.WebSocketPublishes);
            Assert.Equal(0, fixture.Source.WebSocketEncodingSets);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly FoxgloveLogHub _hub;

            internal Fixture(
                IReadOnlyList<FoxRunTransportId> activeProviderIds,
                IReadOnlyList<FoxRunTransportId> nextSessionProviderIds,
                bool addSink = false)
            {
                if (InstanceField == null
                    || ManagerField == null
                    || AddSourceMethod == null
                    || TryPublishMethod == null)
                {
                    throw new InvalidOperationException(
                        "FoxgloveLogHub private test surface changed.");
                }

                Source = new RecordingSource(
                    addSink
                        ? FoxTopicVisibility.Exported
                        : FoxTopicVisibility.LocalOnly);
                var manager = new FoxgloveManager
                {
                    ActiveFoxRunPublishEncoding =
                        FoxRunEncoding.MessagePack,
                    ActiveFoxRunPublishSessionPolicy =
                        new FoxRunPublishSessionPolicy(
                            sessionGeneration: 7,
                            sessionActive: true,
                            publishTransportIds:
                                activeProviderIds,
                            webSocketEncoding:
                                FoxRunEncoding.MessagePack,
                            defaultPublishRateHz: 10f,
                            defaultDeliveryPolicy:
                                FoxRunDeliveryPolicy
                                    .ProviderDefault),
                    ConfiguredFoxRunPublishTransportIds =
                        nextSessionProviderIds
                };

                _hub = new FoxgloveLogHub();
                ManagerField.SetValue(_hub, manager);
                InstanceField.SetValue(null, _hub);
                if (addSink)
                {
                    Sink = new RecordingSink();
                    _hub.TopicSinkRouter.AddSink(Sink);
                }

                Assert.True(
                    (bool)AddSourceMethod.Invoke(
                        _hub,
                        new object[] { Source }));
            }

            internal RecordingSource Source { get; }
            internal RecordingSink Sink { get; }

            internal bool Publish()
                => (bool)TryPublishMethod.Invoke(
                    _hub,
                    new object[]
                    {
                        Source,
                        0,
                        true
                    });

            public void Dispose()
            {
                InstanceField.SetValue(null, null);
                _hub.TopicSinkRouter.Dispose();
            }
        }

        private sealed class RecordingSource :
            IFoxgloveLogSource,
            IFoxgloveTopicContractSource,
            IFoxgloveTopicSinkSource,
            IFoxglovePublishCaptureSource,
            IFoxglovePublishRecordingSource,
            IFoxRunWebSocketCaptureSource
        {
            private readonly FoxTopicContract _contract;

            internal RecordingSource(
                FoxTopicVisibility visibility)
            {
                _contract = new FoxTopicContract(
                    "/phase186/frozen-provider",
                    string.Empty,
                    "msgpack",
                    "phase186.frozen-provider",
                    "phase186-frozen-provider",
                    visibility,
                    FoxTopicWriterPolicy.SingleWriter);
            }

            public int WebSocketPublishes { get; private set; }
            public int RecordingReadinessChecks { get; private set; }
            public int RecordingPublishes { get; private set; }
            public int CaptureEnds { get; private set; }
            public int WebSocketEncodingSets { get; private set; }

            public int FoxgloveLog_TopicCount => 1;

            public string FoxgloveLog_Origin =>
                "phase186-frozen-provider-source";

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(
                int index)
                => index == 0
                    ? new FoxgloveLogTopicInfo(
                        _contract.Topic,
                        0f,
                        FoxRunPolicy.Trigger,
                        0f,
                        FoxRunFlow.Publish,
                        publishTransportIds: null,
                        subscribeTransportId: null,
                        declaredEncoding:
                            (FoxRunEncoding)0,
                        hasExplicitEncoding: false,
                        deliveryPolicy:
                            FoxRunDeliveryPolicy
                                .ProviderDefault,
                        hasExplicitDeliveryPolicy:
                            false,
                        hasExplicitHz: false)
                    : throw new ArgumentOutOfRangeException(
                        nameof(index));

            public FoxTopicContract FoxgloveLog_GetContract(
                int index)
                => index == 0
                    ? _contract
                    : throw new ArgumentOutOfRangeException(
                        nameof(index));

            public void FoxgloveLog_Publish(
                int topicIndex,
                FoxgloveManager manager,
                ulong nowNs)
                => WebSocketPublishes++;

            public bool FoxgloveLog_BeginCapture(
                int topicIndex)
                => true;

            public void FoxgloveLog_EndCapture(
                int topicIndex)
                => CaptureEnds++;

            public bool FoxgloveLog_IsRecordingReady(
                int topicIndex,
                FoxgloveManager manager,
                out string reason)
            {
                RecordingReadinessChecks++;
                reason = string.Empty;
                return true;
            }

            public bool FoxgloveLog_RecordCaptured(
                int topicIndex,
                FoxgloveManager manager,
                ulong nowNs,
                out string reason)
            {
                RecordingPublishes++;
                reason = string.Empty;
                return true;
            }

            public void FoxgloveLog_SetWebSocketEncoding(
                int topicIndex,
                FoxRunEncoding encoding)
            {
                WebSocketEncodingSets++;
            }

            public void FoxgloveLog_PublishToSinks(
                int topicIndex,
                FoxTopicSinkRouter router,
                ulong nowNs)
                => router.PublishCompatible(
                    _contract,
                    FoxRunEncoding.JSON,
                    nowNs,
                    new byte[] { 0x7b, 0x7d },
                    FoxgloveLog_Origin);
        }

        private sealed class RecordingSink : IFoxTopicSink
        {
            public string Name => "phase186-recording-sink";

            public FoxTopicSinkCapabilities Capabilities =>
                FoxTopicSinkCapabilities.Test;

            public List<FoxTopicContract> Registered { get; } =
                new List<FoxTopicContract>();

            public List<FoxTopicContract> Published { get; } =
                new List<FoxTopicContract>();

            public void Register(FoxTopicContract contract)
                => Registered.Add(contract);

            public void Publish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin)
                => Published.Add(contract);

            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
#endif
