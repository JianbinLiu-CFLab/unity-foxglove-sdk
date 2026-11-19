// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/NativeCompileStubs
// Purpose: Compile-only source stubs for R2FU and Unity APIs not shipped in managed DLLs.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;

namespace ROS2
{
    public class ROS2UnityComponent
    {
        internal Func<ROS2Node, bool> TryRemoveNodeResult { get; set; } = _ => true;
        internal int CreateNodeCalls { get; private set; }

        public bool Ok() => true;

        public ROS2Node CreateNode(string name)
        {
            CreateNodeCalls++;
            return new ROS2Node();
        }

        public void RemoveNode(ROS2Node node) { }
        public bool TryRemoveNode(ROS2Node node, bool dispose = true) => TryRemoveNodeResult(node);
    }

    public class ROS2Node : IDisposable
    {
        private const string DefaultNodeName = "unity_ros2_node";

        internal ROS2Node(string unityROS2NodeName = DefaultNodeName) { }

        public Subscription<T> CreateSubscription<T>(
            string topicName,
            Action<T> callback,
            QualityOfServiceProfile qos = null)
            where T : Message, new()
            => throw new NotSupportedException("Compile-only stub.");

        internal Func<ISubscriptionBase, bool> RemoveSubscriptionResult { get; set; } = _ => false;

        public bool RemoveSubscription(ISubscriptionBase subscription) => RemoveSubscriptionResult(subscription);

        public Publisher<T> CreatePublisher<T>(string topicName)
            where T : Message, new()
            => throw new NotSupportedException("Compile-only stub.");

        public Publisher<T> CreatePublisher<T>(
            string topicName,
            QualityOfServiceProfile qos)
            where T : Message, new()
            => throw new NotSupportedException("Compile-only stub.");

        public bool RemovePublisher(IPublisherBase publisher) => false;
        public void Dispose() { }
    }

}
#endif
