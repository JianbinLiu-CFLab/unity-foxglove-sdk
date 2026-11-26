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
    /// <remarks>Helper names are unique on purpose: the source-text inventory binds unresolved calls by method name across all test sources.</remarks>
    public sealed class FoxgloveLogHubSchedulingTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        [Fact]
        /// <summary>An explicit topic rate overrides the inherited Manager default rate.</summary>
        public void ExplicitHzOverridesInheritedDefaultRate()
            => WithSchedulingHub(4f, hub =>
            {
                var source = hub.AddCountingSource(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: true);

                hub.RunHubFrames(8, 0.25);

                Assert.Equal(2, source.Publishes);
            });

        [Fact]
        /// <summary>An omitted topic rate inherits the Manager default rate.</summary>
        public void OmittedHzInheritsManagerDefaultRate()
            => WithSchedulingHub(4f, hub =>
            {
                var source = hub.AddCountingSource(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: false);

                hub.RunHubFrames(8, 0.25);

                Assert.Equal(8, source.Publishes);
            });

        [Fact]
        /// <summary>An explicit non-positive rate disables scheduled publishing instead of publishing every frame.</summary>
        public void ExplicitNonPositiveRateDisablesScheduledPublishing()
            => WithSchedulingHub(4f, hub =>
            {
                var source = hub.AddCountingSource(FoxRunPolicy.FixedRate, hz: 0f, hasExplicitHz: true);

                hub.RunHubFrames(8, 0.25);

                Assert.Equal(0, source.Publishes);
            });

        [Fact]
        /// <summary>Trigger topics publish on every trigger without frame cadence; scheduled topics reject triggers.</summary>
        public void TriggerPublishesOnlyTriggerTopicsAndBypassesCadence()
            => WithSchedulingHub(4f, hub =>
            {
                var triggered = hub.AddCountingSource(FoxRunPolicy.Trigger, hz: 1f, hasExplicitHz: true);
                var scheduled = hub.AddCountingSource(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: true);

                Assert.False(FoxgloveLogHub.Trigger(scheduled, 0));
                Assert.Equal(0, scheduled.Publishes);

                hub.RunHubFrames(3, 0d);
                Assert.Equal(0, triggered.Publishes);

                Assert.True(FoxgloveLogHub.Trigger(triggered, 0));
                Assert.True(FoxgloveLogHub.Trigger(triggered, 0));
                Assert.True(FoxgloveLogHub.Trigger(triggered, 0));
                Assert.Equal(3, triggered.Publishes);
            });

        [Fact]
        /// <summary>Unregistering while sources are enumerated defers removal until the enumeration ends.</summary>
        public void UnregisterDuringEnumerationDefersRemoval()
            => WithSchedulingHub(4f, hub =>
            {
                var source = hub.AddCountingSource(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: true);
                var sources = hub.RegisteredHubSources;

                hub.SetHubIterating(true);
                FoxgloveLogHub.UnregisterSource(source);
                Assert.True(sources.Contains(source));

                hub.SetHubIterating(false);
                hub.InvokeHubMethod("ApplyDeferred");
                Assert.False(sources.Contains(source));
            });

        [Fact]
        /// <summary>A registration deferred by an aborted frame is applied before the next frame schedules sources.</summary>
        public void DeferredRegistrationFromAbortedFrameAppliesBeforeNextSchedulingPass()
            => WithSchedulingHub(4f, hub =>
            {
                var late = hub.CreateCountingSource(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: true);
                var thrower = hub.AddCountingSource(FoxRunPolicy.FixedRate, hz: 1f, hasExplicitHz: true);
                thrower.BeforeShouldPublish = () =>
                {
                    thrower.BeforeShouldPublish = null;
                    FoxgloveLogHub.RegisterSource(late);
                    throw new InvalidOperationException("source failed during the frame");
                };

                UnityEngine.Time.realtimeSinceStartupAsDouble = 0d;
                var frameFailure = Assert.Throws<TargetInvocationException>(() => hub.InvokeHubMethod("Update"));
                Assert.IsType<InvalidOperationException>(frameFailure.InnerException);
                Assert.False(hub.RegisteredHubSources.Contains(late));

                UnityEngine.Time.realtimeSinceStartupAsDouble = 0.25d;
                hub.InvokeHubMethod("Update");

                Assert.Equal(1, late.Publishes);
            });

        private static void WithSchedulingHub(float defaultRateHz, Action<SchedulingHubHarness> body)
        {
            var harness = new SchedulingHubHarness(defaultRateHz);
            try
            {
                body(harness);
            }
            finally
            {
                harness.ReleaseSchedulingHub();
            }
        }

        private sealed class SchedulingHubHarness
        {
            private static readonly FieldInfo InstanceField =
                typeof(FoxgloveLogHub).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);

            private readonly FoxgloveLogHub _hub = new FoxgloveLogHub();
            private int _nextSource;

            internal SchedulingHubHarness(float defaultRateHz)
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
                SetHubField("_manager", manager);
                SetHubField("_enableFallbackSceneScan", false);
                InstanceField.SetValue(null, _hub);
            }

            internal IDictionary RegisteredHubSources => (IDictionary)HubField("_sources").GetValue(_hub);

            internal CountingSource CreateCountingSource(FoxRunPolicy policy, float hz, bool hasExplicitHz)
                => new CountingSource(_nextSource++, policy, hz, hasExplicitHz);

            internal CountingSource AddCountingSource(FoxRunPolicy policy, float hz, bool hasExplicitHz)
            {
                var source = CreateCountingSource(policy, hz, hasExplicitHz);
                Assert.True((bool)InvokeHubMethod("AddSourceNow", source));
                return source;
            }

            internal void RunHubFrames(int count, double frameSeconds)
            {
                for (var frame = 0; frame < count; frame++)
                {
                    UnityEngine.Time.realtimeSinceStartupAsDouble = frame * frameSeconds;
                    InvokeHubMethod("Update");
                }
            }

            internal void SetHubIterating(bool value) => SetHubField("_iterating", value);

            internal object InvokeHubMethod(string method, params object[] arguments)
            {
                var info = typeof(FoxgloveLogHub).GetMethod(method, InstanceFlags);
                Assert.NotNull(info);
                return info.Invoke(_hub, arguments.Length == 0 ? null : arguments);
            }

            internal void ReleaseSchedulingHub()
            {
                InstanceField.SetValue(null, null);
                UnityEngine.Time.realtimeSinceStartupAsDouble = 0d;
                _hub.TopicSinkRouter.Dispose();
            }

            private static FieldInfo HubField(string name)
            {
                var field = typeof(FoxgloveLogHub).GetField(name, InstanceFlags);
                Assert.NotNull(field);
                return field;
            }

            private void SetHubField(string name, object value) => HubField(name).SetValue(_hub, value);
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
