// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Pins ROS2 topic publisher ownership to exported contract lifecycle.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    public sealed class Ros2R2FUTopicSinkLifecycleTests
    {
        [Fact]
        public void UnregisterDisposesPublisherAndLaterRegisterCreatesFreshEndpoint()
        {
            var context = new FakeContext();
            var factory = new RecordingFactory();
            using var sink = new Ros2R2FUTopicSink(context, factory);
            var contract = Exported("/phase184/qos");

            sink.Register(contract);
            var first = Assert.Single(factory.Publishers);

            ((IFoxTopicSinkContractLifecycle)sink).Unregister(contract.Topic);

            Assert.Equal(1, first.DisposeCalls);

            sink.Register(contract);

            Assert.Equal(2, factory.Publishers.Count);
            Assert.NotSame(first, factory.Publishers[1]);
            Assert.Equal(0, factory.Publishers[1].DisposeCalls);
        }

        [Fact]
        public void ResolvedRegistrationPassesNativeQosAndPublisherReceivesJsonBytes()
        {
            var context = new FakeContext();
            var factory = new RecordingQosFactory();
            using var sink = new Ros2R2FUTopicSink(context, factory);
            var contract = Exported("/phase184/native-json");
            var resolved = Resolved(FoxRunResolvedQos.SensorData);

            ((IFoxTopicResolvedContractSink)sink).Register(contract, resolved);
            var payload = Encoding.UTF8.GetBytes("{\"value\":7}");

            Assert.Equal(FoxRunResolvedQos.SensorData, Assert.Single(factory.Qos));
            Assert.True(sink.TryPublish(
                contract,
                184_700UL,
                payload,
                "origin",
                out var reason), reason);
            Assert.Same(payload, Assert.Single(factory.Publishers).LastPayload);
        }

        [Fact]
        public void LegacyFactoryFailsClosedForNonDefaultQos()
        {
            var context = new FakeContext();
            var factory = new RecordingFactory();
            var warnings = new List<string>();
            using var sink = new Ros2R2FUTopicSink(context, factory, log: warnings.Add);
            var contract = Exported("/phase184/non-default");

            ((IFoxTopicResolvedContractSink)sink).Register(
                contract,
                Resolved(FoxRunResolvedQos.SensorData));

            Assert.Empty(factory.Publishers);
            Assert.False(sink.IsReady(contract, out var reason));
            Assert.Contains("No ROS2 publisher", reason, StringComparison.Ordinal);
            Assert.Contains(
                warnings,
                warning => warning.Contains("QoS-aware", StringComparison.Ordinal));
        }

        [Fact]
        public void QosChangeForSameTopicDisposesAndRebuildsPublisher()
        {
            var context = new FakeContext();
            var factory = new RecordingQosFactory();
            using var sink = new Ros2R2FUTopicSink(context, factory);
            var contract = Exported("/phase184/qos-rebuild");

            ((IFoxTopicResolvedContractSink)sink).Register(
                contract,
                Resolved(FoxRunResolvedQos.Default));
            var first = Assert.Single(factory.Publishers);

            ((IFoxTopicResolvedContractSink)sink).Register(
                contract,
                Resolved(FoxRunResolvedQos.SystemDefault));

            Assert.Equal(1, first.DisposeCalls);
            Assert.Equal(2, factory.Publishers.Count);
            Assert.Equal(
                new[] { FoxRunResolvedQos.Default, FoxRunResolvedQos.SystemDefault },
                factory.Qos);
        }

        private static FoxTopicContract Exported(string topic)
            => new FoxTopicContract(
                topic,
                "phase184.Qos",
                "json",
                "phase184.Qos",
                "phase184-qos",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

        private static FoxRunResolvedPublishContract Resolved(FoxRunResolvedQos nativeQos)
        {
            var info = new FoxgloveLogTopicInfo(
                "/phase184/qos",
                10f,
                FoxRunPolicy.Change,
                0f,
                FoxRunFlow.Publish,
                declaredSource: 0,
                hasExplicitSource: false,
                declaredTargets: FoxRunEndpoint.Ros2Native,
                hasExplicitTargets: true,
                declaredEncoding: 0,
                hasExplicitEncoding: false,
                qosProfile: 0,
                hasExplicitQosProfile: false,
                qosReliability: 0,
                hasExplicitReliability: false,
                qosDurability: 0,
                hasExplicitDurability: false,
                qosHistory: 0,
                hasExplicitHistory: false,
                qosDepth: 0,
                hasExplicitDepth: false,
                hasExplicitHz: false);
            Assert.True(FoxRunResolvedPublishContract.TryResolve(
                info,
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.JSON,
                nativeQos,
                FoxRunResolvedQos.Default,
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.JSON,
                out var resolved,
                out var diagnostic), diagnostic);
            return resolved;
        }

        private sealed class FakeContext : IUnity2FoxgloveRos2Context
        {
            public bool IsAvailable => true;
            public Unity2FoxgloveRos2Status Status => Unity2FoxgloveRos2Status.Ready;
            public string StatusMessage => "ready";
            public IUnity2FoxgloveRos2Node CreateNode(string nodeName) => new FakeNode(nodeName);
            public void Dispose() { }
        }

        private sealed class FakeNode : IUnity2FoxgloveRos2Node
        {
            public FakeNode(string name) => Name = name;
            public string Name { get; }
            public IUnity2FoxgloveRos2Publisher<T> CreatePublisher<T>(string topic)
                => throw new NotSupportedException();
            public IUnity2FoxgloveRos2Subscription CreateSubscription<T>(string topic, Action<T> callback)
                => throw new NotSupportedException();
            public void Dispose() { }
        }

        private sealed class RecordingFactory : IRos2TopicPublisherFactory
        {
            public List<RecordingPublisher> Publishers { get; } = new List<RecordingPublisher>();

            public bool TryCreate(
                FoxTopicContract contract,
                IUnity2FoxgloveRos2Node node,
                out IRos2TopicPublisher publisher,
                out string reason)
            {
                var created = new RecordingPublisher(contract.Topic);
                Publishers.Add(created);
                publisher = created;
                reason = string.Empty;
                return true;
            }
        }

        private sealed class RecordingQosFactory :
            IRos2TopicPublisherFactory,
            IRos2QosAwareTopicPublisherFactory
        {
            public List<RecordingPublisher> Publishers { get; } = new List<RecordingPublisher>();
            public List<FoxRunResolvedQos> Qos { get; } = new List<FoxRunResolvedQos>();

            public bool TryCreate(
                FoxTopicContract contract,
                IUnity2FoxgloveRos2Node node,
                out IRos2TopicPublisher publisher,
                out string reason)
                => Create(contract, FoxRunResolvedQos.Default, out publisher, out reason);

            public bool TryCreate(
                FoxTopicContract contract,
                FoxRunResolvedQos qos,
                IUnity2FoxgloveRos2Node node,
                out IRos2TopicPublisher publisher,
                out string reason)
                => Create(contract, qos, out publisher, out reason);

            private bool Create(
                FoxTopicContract contract,
                FoxRunResolvedQos qos,
                out IRos2TopicPublisher publisher,
                out string reason)
            {
                var created = new RecordingPublisher(contract.Topic);
                Publishers.Add(created);
                Qos.Add(qos);
                publisher = created;
                reason = string.Empty;
                return true;
            }
        }

        private sealed class RecordingPublisher : IRos2TopicPublisher
        {
            public RecordingPublisher(string topic) => Topic = topic;
            public string Topic { get; }
            public int DisposeCalls { get; private set; }
            public byte[] LastPayload { get; private set; }
            public bool TryPublish(byte[] jsonPayload, ulong timestampNs, out string error)
            {
                LastPayload = jsonPayload;
                error = string.Empty;
                return true;
            }
            public void Dispose() => DisposeCalls++;
        }
    }
}
#endif
