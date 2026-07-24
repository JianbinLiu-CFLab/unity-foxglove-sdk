// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Pins ROS2 topic publisher ownership to exported contract lifecycle.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
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

        private static FoxTopicContract Exported(string topic)
            => new FoxTopicContract(
                topic,
                "phase184.Qos",
                "json",
                "phase184.Qos",
                "phase184-qos",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

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

        private sealed class RecordingPublisher : IRos2TopicPublisher
        {
            public RecordingPublisher(string topic) => Topic = topic;
            public string Topic { get; }
            public int DisposeCalls { get; private set; }
            public bool TryPublish(byte[] jsonPayload, ulong timestampNs, out string error)
            {
                error = string.Empty;
                return true;
            }
            public void Dispose() => DisposeCalls++;
        }
    }
}
#endif
