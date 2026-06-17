// Copyright 2019-2021 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using UnityEngine;

namespace ROS2
{

/// <summary>
/// A class representing a ros2 node. Multiple nodes can be used. Node can be removed by GC when not used anymore,
/// but will also be removed properly with Ros2cs Shutdown, which ROS2 for Unity performs on application quit
/// The node should be constructed through ROS2UnityComponent class, which also handles spinning
/// </summary>
public class ROS2Node : IDisposable
{
    internal INode node;
    public ROS2Clock clock { get; private set; }
    public string name { get; }
    private readonly object mutex = new object();
    private volatile bool disposed;

    internal bool IsDisposed
    {
        get
        {
            lock (mutex)
            {
                return disposed;
            }
        }
    }

    // Use ROS2UnityComponent to create a node
    internal ROS2Node(string unityROS2NodeName = "unity_ros2_node")
    {
        name = unityROS2NodeName;
        node = Ros2cs.CreateNode(name);
        clock = new ROS2Clock();
    }

    public void Dispose()
    {
        INode nodeToDispose = null;
        ROS2Clock clockToDispose = null;
        lock (mutex)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            nodeToDispose = node;
            clockToDispose = clock;
            node = null;
            clock = null;
        }

        try
        {
            if (nodeToDispose != null && Ros2cs.Ok())
            {
                Ros2cs.RemoveNode(nodeToDispose);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            if (clockToDispose != null)
            {
                try
                {
                    clockToDispose.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }

    internal bool TryUpdateROSTimestamp(ref MessageWithHeader message)
    {
        if (disposed)
        {
            return false;
        }

        ROS2Clock clockToUse = null;
        lock (mutex)
        {
            if (disposed || clock == null)
            {
                return false;
            }

            clockToUse = clock;
        }

        try
        {
            clockToUse.UpdateROSTimestamp(ref message);
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ThrowIfUninitialized(string callContext)
    {
        lock (mutex)
        {
            if (disposed || node == null || !Ros2cs.Ok())
            {
                throw new InvalidOperationException("Ros2 For Unity is not initialized, can't " + callContext);
            }
        }
    }

    private TResult WithLiveNode<TResult>(string callContext, Func<INode, TResult> action)
    {
        lock (mutex)
        {
            ThrowIfUninitialized(callContext);
            return action(node);
        }
    }

    /// <summary>
    /// Create a publisher with QoS suitable for sensor data
    /// </summary>
    /// <returns>The publisher</returns>
    /// <param name="topicName">topic that will be used for publishing</param>
    public Publisher<T> CreateSensorPublisher<T>(string topicName) where T : Message, new()
    {
        // ros2cs copies QoS settings during publisher creation; this temporary profile only configures that call.
        using (QualityOfServiceProfile sensorProfile = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA))
        {
            return CreatePublisher<T>(topicName, sensorProfile);
        }
    }

    /// <summary>
    /// Create a publisher with indicated QoS.
    /// </summary>
    /// <returns>The publisher</returns>
    /// <param name="topicName">topic that will be used for publishing</param>
    /// <param name="qos">QoS for publishing. If no QoS is selected, it will default to reliable, keep 10 last</param>
    public Publisher<T> CreatePublisher<T>(string topicName, QualityOfServiceProfile qos = null) where T : Message, new()
    {
        return WithLiveNode("create publisher", liveNode => liveNode.CreatePublisher<T>(topicName, qos));
    }

    /// <summary>
    /// Create a subscription
    /// </summary>
    /// <returns>The subscription</returns>
    /// <param name="topicName">topic to subscribe to</param>
    /// <param name="qos">QoS for subscription. If no QoS is selected, it will default to reliable, keep 10 last</param>
    public Subscription<T> CreateSubscription<T>(string topicName, Action<T> callback,
        QualityOfServiceProfile qos = null) where T : Message, new()
    {
        if (qos != null)
        {
            return WithLiveNode("create subscription", liveNode => liveNode.CreateSubscription<T>(topicName, callback, qos));
        }

        using (QualityOfServiceProfile defaultQos = new QualityOfServiceProfile(QosPresetProfile.DEFAULT))
        {
            return WithLiveNode("create subscription", liveNode => liveNode.CreateSubscription<T>(topicName, callback, defaultQos));
        }
    }


    /// <summary>
    /// Remove existing subscription (returned earlier with CreateSubscription)
    /// </summary>
    /// <returns>The whether subscription was found (e. g. false if removed earlier elsewhere) </returns>
    /// <param name="subscription">subscrition to remove, returned from CreateSubscription</param>
    public bool RemoveSubscription(ISubscriptionBase subscription)
    {
        return WithLiveNode("remove subscription", liveNode => liveNode.RemoveSubscription(subscription));
    }

    public bool RemoveSubscription<T>(ISubscriptionBase subscription)
    {
        return RemoveSubscription(subscription);
    }

    /// <summary>
    /// Remove existing publisher
    /// </summary>
    /// <returns>The whether publisher was found (e. g. false if removed earlier elsewhere) </returns>
    /// <param name="publisher">publisher to remove, returned from CreatePublisher or CreateSensorPublisher</param>
    public bool RemovePublisher(IPublisherBase publisher)
    {
        return WithLiveNode("remove publisher", liveNode => liveNode.RemovePublisher(publisher));
    }

    public bool RemovePublisher<T>(IPublisherBase publisher)
    {
        return RemovePublisher(publisher);
    }

    /// <inheritdoc cref="INode.CreateService"/>
    public Service<I, O> CreateService<I, O>(string topic, Func<I, O> callback, QualityOfServiceProfile qos = null)
        where I : Message, new()
        where O : Message, new()
    {
        return WithLiveNode("create service", liveNode => liveNode.CreateService<I, O>(topic, callback, qos));
    }

    /// <inheritdoc cref="INode.RemoveService"/>
    public bool RemoveService(IServiceBase service)
    {
        return WithLiveNode("remove service", liveNode => liveNode.RemoveService(service));
    }

    /// <inheritdoc cref="INode.CreateClient"/>
    public Client<I, O> CreateClient<I, O>(string topic, QualityOfServiceProfile qos = null)
        where I : Message, new()
        where O : Message, new()
    {
        return WithLiveNode("create client", liveNode => liveNode.CreateClient<I, O>(topic, qos));
    }

    /// <inheritdoc cref="INode.RemoveClient"/>
    public bool RemoveClient(IClientBase client)
    {
        return WithLiveNode("remove client", liveNode => liveNode.RemoveClient(client));
    }
}

}  // namespace ROS2
