// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Regression coverage for independent live WebSocket and typed native-bus fanout.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.FoxRun
{
    public sealed class FoxgloveLogHubNativeFanoutTests
    {
        [Fact]
        public void TriggerDispatchesTypedBusWhenWebSocketIsStopped()
        {
            var fixture = new HubFixture(isRunning: false);
            fixture.Subscribe();

            Assert.True(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
            Assert.Equal(1, fixture.Source.BusPublishes);
            Assert.Equal(1, fixture.BusDeliveries);
            Assert.Equal(1, fixture.Source.MarkPublishedCalls);
        }

        [Fact]
        public void TriggerDispatchesLiveAndTypedBusExactlyOnceWhenBothRoutesAreAvailable()
        {
            var fixture = new HubFixture(isRunning: true);
            fixture.Subscribe();

            Assert.True(fixture.Trigger());
            Assert.Equal(1, fixture.Source.LivePublishes);
            Assert.Equal(1, fixture.Source.BusPublishes);
            Assert.Equal(1, fixture.BusDeliveries);
            Assert.Equal(1, fixture.Source.MarkPublishedCalls);
        }

        [Fact]
        public void RecoverableLiveFailureDoesNotBlockTypedBusDispatch()
        {
            var fixture = new HubFixture(isRunning: true);
            fixture.Source.ThrowOnLivePublish = true;
            fixture.Subscribe();

            Assert.True(fixture.Trigger());
            Assert.Equal(1, fixture.Source.LivePublishes);
            Assert.Equal(1, fixture.Source.BusPublishes);
            Assert.Equal(1, fixture.BusDeliveries);
            Assert.Equal(1, fixture.Source.MarkPublishedCalls);
        }

        [Fact]
        public void TriggerDoesNotAdvancePolicyWhenNeitherLiveNorTypedBusRouteNeedsOutput()
        {
            var fixture = new HubFixture(isRunning: false);

            Assert.False(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
            Assert.Equal(0, fixture.Source.BusPublishes);
            Assert.Equal(0, fixture.Source.MarkPublishedCalls);
        }

        [Fact]
        public void WarmedWebSocketOnlyTriggerDoesNotAllocatePerDispatch()
        {
            var fixture = new WebSocketOnlyHubFixture();
            Assert.True(fixture.Trigger());
            fixture.Source.Reset();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 64; index++)
            {
                if (!fixture.Trigger())
                    throw new InvalidOperationException("Expected warmed live-only trigger to dispatch.");
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(64, fixture.Source.LivePublishes);
        }

        private sealed class HubFixture
        {
            private static readonly FieldInfo ManagerField = typeof(FoxgloveLogHub).GetField(
                "_mgr",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo TriggerMethod = typeof(FoxgloveLogHub).GetMethod(
                "TriggerSource",
                BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly FoxgloveLogHub _hub = new FoxgloveLogHub();
            private readonly FoxgloveManager _manager;

            public HubFixture(bool isRunning)
            {
                Assert.NotNull(ManagerField);
                Assert.NotNull(TriggerMethod);
                _manager = new FoxgloveManager
                {
                    IsRunning = isRunning,
                    NowNs = 123_456_789UL
                };
                ManagerField.SetValue(_hub, _manager);
                Source = new FanoutSource();
            }

            public FanoutSource Source { get; }
            public int BusDeliveries { get; private set; }

            public void Subscribe()
            {
                _hub.TopicBus.Subscribe<int>(FanoutSource.Topic, _ => BusDeliveries++);
            }

            public bool Trigger()
            {
                return (bool)TriggerMethod.Invoke(
                    _hub,
                    new object[] { Source, 0 });
            }
        }

        private sealed class WebSocketOnlyHubFixture
        {
            private static readonly FieldInfo ManagerField = typeof(FoxgloveLogHub).GetField(
                "_mgr",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo TriggerMethod = typeof(FoxgloveLogHub).GetMethod(
                "TriggerSource",
                BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly Func<IFoxgloveLogSource, int, bool> _trigger;

            public WebSocketOnlyHubFixture()
            {
                Assert.NotNull(ManagerField);
                Assert.NotNull(TriggerMethod);
                var hub = new FoxgloveLogHub();
                ManagerField.SetValue(
                    hub,
                    new FoxgloveManager
                    {
                        IsRunning = true,
                        NowNs = 123_456_789UL,
                    });
                _trigger = (Func<IFoxgloveLogSource, int, bool>)Delegate.CreateDelegate(
                    typeof(Func<IFoxgloveLogSource, int, bool>),
                    hub,
                    TriggerMethod);
                Source = new WebSocketOnlySource();
            }

            public WebSocketOnlySource Source { get; }

            public bool Trigger()
                => _trigger(Source, 0);
        }

        private sealed class FanoutSource : IFoxgloveLogSource, IFoxgloveTopicBusSource, IFoxgloveTopicBusDemandSource, IFoxgloveLogPolicySource
        {
            internal const string Topic = "/phase181/native-fanout";
            private static readonly FoxTopicContract Contract = new FoxTopicContract(
                Topic,
                "phase181.Fanout",
                "json",
                "phase181.Fanout",
                "phase181-fanout",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

            public int FoxgloveLog_TopicCount => 1;
            public int LivePublishes { get; private set; }
            public int BusPublishes { get; private set; }
            public int MarkPublishedCalls { get; private set; }
            public bool ThrowOnLivePublish { get; set; }

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
            {
                Assert.Equal(0, index);
                return new FoxgloveLogTopicInfo(Topic, 30f, FoxRunPublishMode.OnTrigger, 0f, 0f);
            }

            public void FoxgloveLog_Publish(int topicIndex, FoxgloveManager manager, ulong nowNs)
            {
                Assert.Equal(0, topicIndex);
                Assert.NotNull(manager);
                Assert.Equal(123_456_789UL, nowNs);
                LivePublishes++;
                if (ThrowOnLivePublish)
                    throw new InvalidOperationException("expected live route failure");
            }

            public void FoxgloveLog_PublishToBus(int topicIndex, FoxTopicBus bus, ulong nowNs)
            {
                Assert.Equal(0, topicIndex);
                Assert.NotNull(bus);
                Assert.Equal(123_456_789UL, nowNs);
                BusPublishes++;
                var payload = 181;
                bus.Publish(Contract, nowNs, in payload, "phase181-test");
            }

            public bool FoxgloveLog_HasBusSubscribers(int topicIndex, FoxTopicBus bus)
            {
                Assert.Equal(0, topicIndex);
                return bus != null && bus.HasSubscribers(Topic);
            }

            public bool FoxgloveLog_ShouldPublish(int topicIndex, double nowSeconds)
            {
                Assert.Equal(0, topicIndex);
                return true;
            }

            public void FoxgloveLog_MarkPublished(int topicIndex, double nowSeconds)
            {
                Assert.Equal(0, topicIndex);
                MarkPublishedCalls++;
            }
        }

        private sealed class WebSocketOnlySource : IFoxgloveLogSource
        {
            public int FoxgloveLog_TopicCount => 1;
            public int LivePublishes { get; private set; }

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
            {
                return new FoxgloveLogTopicInfo(
                    "/phase181/websocket-only",
                    30f,
                    FoxRunPublishMode.OnTrigger,
                    0f,
                    0f);
            }

            public void FoxgloveLog_Publish(int topicIndex, FoxgloveManager manager, ulong nowNs)
            {
                LivePublishes++;
            }

            public void Reset()
            {
                LivePublishes = 0;
            }
        }
    }
}
#endif
