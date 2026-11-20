// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Live ros2cs-backed replacements for the R2FU Unity facades used by native bridges.

using System;
using System.Collections.Generic;
using System.Threading;

namespace ROS2
{
    /// <summary>Spins live ros2cs nodes on one background executor thread, like ROS2UnityCore.</summary>
    public class ROS2UnityComponent
    {
        private readonly object _nodesGate = new object();
        private readonly object _spinGate = new object();
        private readonly List<INode> _nodes = new List<INode>();
        private Thread _executor;
        private volatile bool _stopping;

        public bool Ok() => Ros2cs.Ok();

        public ROS2Node CreateNode(string name)
        {
            lock (_spinGate)
            {
                var node = Ros2cs.CreateNode(name);
                lock (_nodesGate)
                    _nodes.Add(node);
                return new ROS2Node(node, _spinGate);
            }
        }

        public void RemoveNode(ROS2Node node) => TryRemoveNode(node);

        public bool TryRemoveNode(ROS2Node node, bool dispose = true)
        {
            lock (_spinGate)
            {
                lock (_nodesGate)
                    _nodes.Remove(node.Inner);
                Ros2cs.RemoveNode(node.Inner);
            }
            return true;
        }

        internal void StartExecutor()
        {
            _executor = new Thread(Spin) { IsBackground = true, Name = "pointcloud2-probe-ros2cs-executor" };
            _executor.Start();
        }

        internal void StopExecutor()
        {
            _stopping = true;
            if (_executor != null && !_executor.Join(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("ROS2 executor thread did not stop.");
        }

        private void Spin()
        {
            var snapshot = new List<INode>();
            while (!_stopping)
            {
                lock (_spinGate)
                {
                    lock (_nodesGate)
                    {
                        snapshot.Clear();
                        snapshot.AddRange(_nodes);
                    }

                    if (snapshot.Count > 0)
                        Ros2cs.SpinOnce(snapshot, 0.0001);
                }

                if (snapshot.Count == 0)
                    Thread.Sleep(1);
            }
        }
    }

    /// <summary>R2FU ROS2Node facade over a live ros2cs node; entity changes never overlap a spin.</summary>
    public class ROS2Node : IDisposable
    {
        private readonly object _spinGate;

        internal ROS2Node(INode inner, object spinGate)
        {
            Inner = inner;
            _spinGate = spinGate;
        }

        internal INode Inner { get; }

        public Subscription<T> CreateSubscription<T>(string topicName, Action<T> callback, QualityOfServiceProfile qos = null)
            where T : Message, new()
        {
            lock (_spinGate)
                return Inner.CreateSubscription<T>(topicName, callback, qos ?? new QualityOfServiceProfile());
        }

        public bool RemoveSubscription(ISubscriptionBase subscription)
        {
            lock (_spinGate)
                return Inner.RemoveSubscription(subscription);
        }

        public Publisher<T> CreatePublisher<T>(string topicName)
            where T : Message, new()
        {
            lock (_spinGate)
                return Inner.CreatePublisher<T>(topicName);
        }

        public Publisher<T> CreatePublisher<T>(string topicName, QualityOfServiceProfile qos)
            where T : Message, new()
        {
            lock (_spinGate)
                return Inner.CreatePublisher<T>(topicName, qos);
        }

        public bool RemovePublisher(IPublisherBase publisher)
        {
            lock (_spinGate)
                return Inner.RemovePublisher(publisher);
        }

        public void Dispose() { }
    }
}
