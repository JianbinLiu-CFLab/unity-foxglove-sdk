// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: FoxRun hub cadence, trigger, and deferred source-mutation behavior.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.FoxRun
{
    /// <summary>Drives the FoxRun hub frame loop and observes publishes per source.</summary>
    public sealed class FoxgloveLogHubSchedulingTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        [Fact]
        /// <summary>An explicit topic rate overrides the inherited Manager default rate.</summary>
        public void ExplicitHzOverridesInheritedDefaultRate()
        {
            using var fixture = new Fixture(defaultRateHz: 4f);
            var source = fixture.Add(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: true);

            fixture.RunFrames(8, 0.25);

            Assert.Equal(2, source.Publishes);
        }

        [Fact]
        /// <summary>An omitted topic rate inherits the Manager default rate.</summary>
        public void OmittedHzInheritsManagerDefaultRate()
        {
            using var fixture = new Fixture(defaultRateHz: 4f);
            var source = fixture.Add(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: false);

            fixture.RunFrames(8, 0.25);

            Assert.Equal(8, source.Publishes);
        }

        [Fact]
        /// <summary>An explicit non-positive rate disables scheduled publishing instead of publishing every frame.</summary>
        public void ExplicitNonPositiveRateDisablesScheduledPublishing()
        {
            using var fixture = new Fixture(defaultRateHz: 4f);
            var source = fixture.Add(FoxRunPolicy.FixedRate, hz: 0f, hasExplicitHz: true);

            fixture.RunFrames(8, 0.25);

            Assert.Equal(0, source.Publishes);
        }

        [Fact]
        /// <summary>Trigger topics publish on every trigger without frame cadence; scheduled topics reject triggers.</summary>
        public void TriggerPublishesOnlyTriggerTopicsAndBypassesCadence()
        {
            using var fixture = new Fixture(defaultRateHz: 4f);
            var triggered = fixture.Add(FoxRunPolicy.Trigger, hz: 1f, hasExplicitHz: true);
            var scheduled = fixture.Add(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: true);

            Assert.False(FoxgloveLogHub.Trigger(scheduled, 0));
            Assert.Equal(0, scheduled.Publishes);

            fixture.RunFrames(3, 0d);
            Assert.Equal(0, triggered.Publishes);

            Assert.True(FoxgloveLogHub.Trigger(triggered, 0));
            Assert.True(FoxgloveLogHub.Trigger(triggered, 0));
            Assert.True(FoxgloveLogHub.Trigger(triggered, 0));
            Assert.Equal(3, triggered.Publishes);
        }

        [Fact]
        /// <summary>Unregistering while sources are enumerated defers removal until the enumeration ends.</summary>
        public void UnregisterDuringEnumerationDefersRemoval()
        {
            using var fixture = new Fixture(defaultRateHz: 4f);
            var source = fixture.Add(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: true);
            var sources = fixture.Sources;

            fixture.SetIterating(true);
            FoxgloveLogHub.UnregisterSource(source);
            Assert.True(sources.Contains(source));

            fixture.SetIterating(false);
            fixture.Invoke("ApplyDeferred");
            Assert.False(sources.Contains(source));
        }

        [Fact]
        /// <summary>A registration deferred by an aborted frame is applied before the next frame schedules sources.</summary>
        public void DeferredRegistrationFromAbortedFrameAppliesBeforeNextSchedulingPass()
        {
            using var fixture = new Fixture(defaultRateHz: 4f);
            var late = fixture.Create(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: true);
            var thrower = fixture.Add(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: true);
            thrower.BeforeShouldPublish = () =>
            {
                thrower.BeforeShouldPublish = null;
                FoxgloveLogHub.RegisterSource(late);
                throw new InvalidOperationException("source failed during the frame");
            };

            UnityEngine.Time.realtimeSinceStartupAsDouble = 0d;
            var frameFailure = Assert.Throws<TargetInvocationException>(() => fixture.Invoke("Update"));
            Assert.IsType<InvalidOperationException>(frameFailure.InnerException);
            Assert.False(fixture.Sources.Contains(late));

            UnityEngine.Time.realtimeSinceStartupAsDouble = 0.25d;
            fixture.Invoke("Update");

            Assert.Equal(1, late.Publishes);
        }

        private sealed class Fixture : IDisposable
        {
            private static readonly FieldInfo InstanceField =
                typeof(FoxgloveLogHub).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);

            private readonly FoxgloveLogHub _hub = new FoxgloveLogHub();
            private int _nextSource;

            internal Fixture(float defaultRateHz)
            {
                var manager = new FoxgloveManager
                {
                    ActiveFoxRunPublishEncoding = FoxRunEncoding.MessagePack,
                    ActiveFoxRunDefaultPublishRateHz = defaultRateHz,
                    ActiveFoxRunPublishSessionPolicy = new FoxRunPublishSessionPolicy(
                        sessionGeneration: 7,
                        sessionActive: true,
                        publishTransportIds: new[] { FoxgloveWebSocketTransport.TransportId },
                        webSocketEncoding: FoxRunEncoding.MessagePack,
                        defaultPublishRateHz: defaultRateHz,
                        defaultDeliveryPolicy: FoxRunDeliveryPolicy.ProviderDefault),
                    ActiveFoxRunTransportSession = new FoxRunTransportSessionSnapshot(
                        generation: 7,
                        publishTransports: Array.Empty<IFoxRunTransportSession>(),
                        subscribeTransport: null,
                        allSessions: Array.Empty<IFoxRunTransportSession>())
                };
                SetField("_manager", manager);
                SetField("_enableFallbackSceneScan", false);
                InstanceField.SetValue(null, _hub);
            }

            internal IDictionary Sources => (IDictionary)GetField("_sources").GetValue(_hub);

            internal CountingSource Create(FoxRunPolicy policy, float hz, bool hasExplicitHz)
                => new CountingSource(_nextSource++, policy, hz, hasExplicitHz);

            internal CountingSource Add(FoxRunPolicy policy, float hz, bool hasExplicitHz)
            {
                var source = Create(policy, hz, hasExplicitHz);
                Assert.True((bool)Invoke("AddSourceNow", source));
                return source;
            }

            internal void RunFrames(int count, double frameSeconds)
            {
                for (var frame = 0; frame < count; frame++)
                {
                    UnityEngine.Time.realtimeSinceStartupAsDouble = frame * frameSeconds;
                    Invoke("Update");
                }
            }

            internal void SetIterating(bool value) => SetField("_iterating", value);

            internal object Invoke(string method, params object[] arguments)
            {
                var info = typeof(FoxgloveLogHub).GetMethod(method, InstanceFlags);
                Assert.NotNull(info);
                return info.Invoke(_hub, arguments.Length == 0 ? null : arguments);
            }

            public void Dispose()
            {
                InstanceField.SetValue(null, null);
                UnityEngine.Time.realtimeSinceStartupAsDouble = 0d;
                _hub.TopicSinkRouter.Dispose();
            }

            private static FieldInfo GetField(string name)
            {
                var field = typeof(FoxgloveLogHub).GetField(name, InstanceFlags);
                Assert.NotNull(field);
                return field;
            }

            private void SetField(string name, object value) => GetField(name).SetValue(_hub, value);
        }

        private sealed class CountingSource :
            IFoxgloveLogSource,
            IFoxgloveTopicContractSource,
            IFoxgloveLogPolicySource
        {
            private readonly FoxgloveLogTopicInfo _info;
            private readonly FoxTopicContract _contract;

            internal CountingSource(int id, FoxRunPolicy policy, float hz, bool hasExplicitHz)
            {
                var topic = "/plan198/hub-scheduling/" + id;
                FoxgloveLog_Origin = "plan198-hub-scheduling-" + id;
                _info = new FoxgloveLogTopicInfo(
                    topic,
                    hz,
                    policy,
                    0f,
                    FoxRunFlow.Publish,
                    publishTransportIds: null,
                    subscribeTransportId: null,
                    declaredEncoding: (FoxRunEncoding)0,
                    hasExplicitEncoding: false,
                    deliveryPolicy: FoxRunDeliveryPolicy.ProviderDefault,
                    hasExplicitDeliveryPolicy: false,
                    hasExplicitHz: hasExplicitHz);
                _contract = new FoxTopicContract(
                    topic,
                    string.Empty,
                    "msgpack",
                    "plan198.hub-scheduling",
                    FoxgloveLog_Origin,
                    FoxTopicVisibility.LocalOnly,
                    FoxTopicWriterPolicy.SingleWriter);
            }

            internal int Publishes { get; private set; }

            internal Action BeforeShouldPublish { get; set; }

            public int FoxgloveLog_TopicCount => 1;

            public string FoxgloveLog_Origin { get; }

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
                => index == 0 ? _info : throw new ArgumentOutOfRangeException(nameof(index));

            public FoxTopicContract FoxgloveLog_GetContract(int index)
                => index == 0 ? _contract : throw new ArgumentOutOfRangeException(nameof(index));

            public void FoxgloveLog_Publish(int topicIndex, FoxgloveManager manager, ulong nowNs)
                => Publishes++;

            public bool FoxgloveLog_ShouldPublish(int topicIndex, double nowSeconds)
            {
                BeforeShouldPublish?.Invoke();
                return true;
            }

            public void FoxgloveLog_MarkPublished(int topicIndex, double nowSeconds)
            {
            }
        }
    }
}
#endif
