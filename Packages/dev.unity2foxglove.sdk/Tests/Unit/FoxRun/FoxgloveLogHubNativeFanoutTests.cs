// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Regression coverage for independent live WebSocket and typed native-bus fanout.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Util;
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
        public void TriggerDuringReplaySuppressesBothLiveAndTypedBusRoutes()
        {
            var fixture = new HubFixture(isRunning: true, suppressLivePublishersForReplay: true);
            fixture.Subscribe();

            Assert.False(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
            Assert.Equal(0, fixture.Source.BusPublishes);
            Assert.Equal(0, fixture.BusDeliveries);
            Assert.Equal(0, fixture.Source.MarkPublishedCalls);
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

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(99)]
        public void ExplicitTriggerRejectsNonTriggerRetiredAndUnknownPolicies(int policyValue)
        {
            var fixture = new HubFixture(isRunning: true);
            fixture.Source.Policy = (FoxRunPolicy)policyValue;
            fixture.Subscribe();

            Assert.False(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
            Assert.Equal(0, fixture.Source.BusPublishes);
            Assert.Equal(0, fixture.BusDeliveries);
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

        [Fact]
        public void ExplicitQosWithInheritedAllFoxgloveProfileFailsBeforeLivePublish()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Foxglove,
                hasExplicitQos: true);

            Assert.False(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
        }

        [Fact]
        public void ExplicitQosDoesNotTreatDisabledInheritedNativeSubscriptionAsRos2Direction()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Foxglove,
                hasExplicitQos: true,
                flow: FoxRunFlow.PublishAndSubscribe,
                disabledNativeSubscription: true);

            Assert.False(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
        }

        [Fact]
        public void ExplicitQosWithInheritedAllFoxgloveProfileNeverRegistersExternalSink()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Foxglove,
                hasExplicitQos: true);
            var sink = new LifecycleRecordingSink();
            fixture.AddSink(sink);

            Assert.True(fixture.AddSource());
            Assert.Equal(0, sink.RegisterCalls);
        }

        [Fact]
        public void ExplicitQosWithInheritedNativeProfileRegistersExternalSink()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Ros2Native,
                hasExplicitQos: true);
            var sink = new LifecycleRecordingSink();
            fixture.AddSink(sink);

            Assert.True(fixture.AddSource());
            Assert.Equal(1, sink.RegisterCalls);
        }

        [Fact]
        public void PublishSessionChangesDisposeAndRecreateExternalSinkContract()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Ros2Native,
                hasExplicitQos: true);
            var sink = new LifecycleRecordingSink();
            fixture.AddSink(sink);
            Assert.True(fixture.AddSource());
            Assert.Equal(1, sink.RegisterCalls);

            fixture.ChangePublishTargets(FoxRunEndpoint.Foxglove);

            Assert.Equal(1, sink.UnregisterCalls);
            Assert.Equal(1, sink.RegisterCalls);

            fixture.ChangePublishTargets(FoxRunEndpoint.Ros2Native);

            Assert.Equal(1, sink.UnregisterCalls);
            Assert.Equal(2, sink.RegisterCalls);
        }

        [Fact]
        public void EndingPublishSessionDisposesWithoutRecreatingExternalSinkContract()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Ros2Native,
                hasExplicitQos: true);
            var sink = new LifecycleRecordingSink();
            fixture.AddSink(sink);
            Assert.True(fixture.AddSource());
            Assert.Equal(1, sink.RegisterCalls);

            fixture.EndPublishSession();

            Assert.Equal(1, sink.UnregisterCalls);
            Assert.Equal(1, sink.RegisterCalls);
        }

        [Fact]
        public void ScheduledPublishUsesFrozenProfileRateWhenDeclarationOmitsHz()
        {
            var fixture = new ScheduledRateFixture(
                inheritedRateHz: 2f,
                declaredRateHz: 10f,
                hasExplicitHz: false);

            Assert.True(fixture.Tick(0d));
            Assert.False(fixture.Tick(0.25d));
            Assert.True(fixture.Tick(0.5d));
            Assert.Equal(2, fixture.Source.LivePublishes);
        }

        [Fact]
        public void ScheduledPublishKeepsExplicitRateInsteadOfProfileRate()
        {
            var fixture = new ScheduledRateFixture(
                inheritedRateHz: 2f,
                declaredRateHz: 4f,
                hasExplicitHz: true);

            Assert.True(fixture.Tick(0d));
            Assert.True(fixture.Tick(0.25d));
            Assert.Equal(2, fixture.Source.LivePublishes);
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

            public HubFixture(bool isRunning, bool suppressLivePublishersForReplay = false)
            {
                Assert.NotNull(ManagerField);
                Assert.NotNull(TriggerMethod);
                _manager = new FoxgloveManager
                {
                    IsRunning = isRunning,
                    SuppressLivePublishersForReplay = suppressLivePublishersForReplay,
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
            private static readonly MethodInfo SetManagerMethod = typeof(FoxgloveLogHub).GetMethod(
                "SetManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo TriggerMethod = typeof(FoxgloveLogHub).GetMethod(
                "TriggerSource",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo AddSourceMethod = typeof(FoxgloveLogHub).GetMethod(
                "AddSourceNow",
                BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly Func<IFoxgloveLogSource, int, bool> _trigger;
            private readonly FoxgloveLogHub _hub;
            private readonly FoxgloveManager _manager;

            public WebSocketOnlyHubFixture(
                FoxRunEndpoint publishTargets = FoxRunEndpoint.Foxglove,
                bool hasExplicitQos = false,
                FoxRunFlow flow = FoxRunFlow.Publish,
                bool disabledNativeSubscription = false)
            {
                Assert.NotNull(SetManagerMethod);
                Assert.NotNull(TriggerMethod);
                Assert.NotNull(AddSourceMethod);
                _hub = new FoxgloveLogHub();
                _manager = new FoxgloveManager
                {
                    IsRunning = true,
                    NowNs = 123_456_789UL,
                    ActiveFoxRunPublishTargets = publishTargets,
                };
                if (disabledNativeSubscription)
                {
                    _manager.ActiveFoxRunSubscriptionSource = FoxRunEndpoint.Ros2Native;
                    _manager.ActiveFoxRunSubscriptionSessionPolicy =
                        FoxRunSubscriptionSessionPolicy.Disabled(0);
                }
                SetManagerMethod.Invoke(
                    _hub,
                    new object[] { _manager });
                _trigger = (Func<IFoxgloveLogSource, int, bool>)Delegate.CreateDelegate(
                    typeof(Func<IFoxgloveLogSource, int, bool>),
                    _hub,
                    TriggerMethod);
                Source = new WebSocketOnlySource(hasExplicitQos, flow);
            }

            public WebSocketOnlySource Source { get; }

            public bool Trigger()
                => _trigger(Source, 0);

            public bool AddSource()
                => (bool)AddSourceMethod.Invoke(_hub, new object[] { Source });

            public void AddSink(IFoxTopicSink sink)
                => _hub.TopicSinkRouter.AddSink(sink);

            public void ChangePublishTargets(FoxRunEndpoint targets)
            {
                _manager.ActiveFoxRunPublishTargets = targets;
                _manager.RaiseFoxRunPublishSessionChanged();
            }

            public void EndPublishSession()
            {
                _manager.ActiveFoxRunPublishSessionPolicy =
                    FoxRunPublishSessionPolicy.Disabled(1);
                _manager.RaiseFoxRunPublishSessionChanged();
            }
        }

        private sealed class ScheduledRateFixture
        {
            private static readonly MethodInfo SetManagerMethod = typeof(FoxgloveLogHub).GetMethod(
                "SetManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo ScheduledMethod = typeof(FoxgloveLogHub).GetMethod(
                "TryPublishScheduledTopic",
                BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly FoxgloveLogHub _hub = new FoxgloveLogHub();
            private readonly FoxgloveLogTopicInfo _topic;
            private FixedRatePublishState _timer;

            public ScheduledRateFixture(
                float inheritedRateHz,
                float declaredRateHz,
                bool hasExplicitHz)
            {
                Assert.NotNull(SetManagerMethod);
                Assert.NotNull(ScheduledMethod);
                var manager = new FoxgloveManager
                {
                    IsRunning = true,
                    NowNs = 123_456_789UL,
                    ActiveFoxRunDefaultPublishRateHz = inheritedRateHz
                };
                SetManagerMethod.Invoke(_hub, new object[] { manager });
                _topic = new FoxgloveLogTopicInfo(
                    ScheduledSource.Topic,
                    declaredRateHz,
                    FoxRunPolicy.FixedRate,
                    0f,
                    FoxRunFlow.Publish,
                    declaredSource: 0,
                    hasExplicitSource: false,
                    declaredTargets: 0,
                    hasExplicitTargets: false,
                    hasExplicitQos: false,
                    hasExplicitHz: hasExplicitHz);
                Source = new ScheduledSource(_topic);
            }

            public ScheduledSource Source { get; }

            public bool Tick(double nowSeconds)
            {
                var arguments = new object[]
                {
                    Source,
                    _topic,
                    0,
                    _timer,
                    123_456_789UL,
                    nowSeconds
                };
                var published = (bool)ScheduledMethod.Invoke(_hub, arguments);
                _timer = (FixedRatePublishState)arguments[3];
                return published;
            }
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
            public FoxRunPolicy Policy { get; set; } = FoxRunPolicy.Trigger;

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
            {
                Assert.Equal(0, index);
                return new FoxgloveLogTopicInfo(Topic, 30f, Policy, 0f);
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
            private readonly bool _hasExplicitQos;
            private readonly FoxRunFlow _flow;

            public WebSocketOnlySource(
                bool hasExplicitQos = false,
                FoxRunFlow flow = FoxRunFlow.Publish)
            {
                _hasExplicitQos = hasExplicitQos;
                _flow = flow;
            }

            public int FoxgloveLog_TopicCount => 1;
            public int LivePublishes { get; private set; }

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
            {
                return new FoxgloveLogTopicInfo(
                    "/phase181/websocket-only",
                    30f,
                    FoxRunPolicy.Trigger,
                    0f,
                    _flow,
                    declaredSource: 0,
                    hasExplicitSource: false,
                    declaredTargets: 0,
                    hasExplicitTargets: false,
                    hasExplicitQos: _hasExplicitQos);
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

        private sealed class ScheduledSource : IFoxgloveLogSource
        {
            internal const string Topic = "/phase184/rate/scheduled";

            private readonly FoxgloveLogTopicInfo _topic;

            public ScheduledSource(FoxgloveLogTopicInfo topic)
            {
                _topic = topic;
            }

            public int FoxgloveLog_TopicCount => 1;
            public int LivePublishes { get; private set; }

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
            {
                Assert.Equal(0, index);
                return _topic;
            }

            public void FoxgloveLog_Publish(
                int topicIndex,
                FoxgloveManager manager,
                ulong nowNs)
            {
                Assert.Equal(0, topicIndex);
                Assert.NotNull(manager);
                Assert.Equal(123_456_789UL, nowNs);
                LivePublishes++;
            }
        }

        private sealed class LifecycleRecordingSink : IFoxTopicSink, IFoxTopicSinkContractLifecycle
        {
            public string Name => "recording-lifecycle";
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.External;
            public int RegisterCalls { get; private set; }
            public int UnregisterCalls { get; private set; }

            public void Register(FoxTopicContract contract) => RegisterCalls++;
            public void Unregister(string topic) => UnregisterCalls++;
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin) { }
            public void Flush() { }
            public void Dispose() { }
        }
    }
}
#endif
