// Copyright 2019-2021 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Fork modifications:
// - Added disposal-safe node facade helpers and timestamp update support for sensor utilities.
// - Centralized node liveness checks through WithLiveNode() before delegating to ros2cs.
// - Preserved compatibility overloads for existing generic remove-call sites.
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
/// A class representing a ros2 node. Multiple nodes can be used. Callers must dispose the node
/// or remove it through ROS2UnityComponent to release it before application quit.
/// The node should be constructed through ROS2UnityComponent class, which also handles spinning
/// </summary>
public class ROS2Node : IDisposable
{
    private const string DefaultNodeName = "unity_ros2_node"; // Fallback only; callers creating multiple nodes should pass unique names.

    internal INode node;
    /// <summary>
    /// ROS clock owned by this node and disposed together with it.
    /// </summary>
    public ROS2Clock clock { get; private set; }

    /// <summary>
    /// ROS node name used when the underlying ros2cs node was created.
    /// </summary>
    public string name { get; }
    private readonly object mutex = new object();
    private volatile bool disposed;

    /// <summary>
    /// Returns whether this facade has disposed its underlying ros2cs node.
    /// </summary>
    internal bool IsDisposed
    {
        get { return disposed; }
    }

    // Use ROS2UnityComponent to create a node
    internal ROS2Node(string unityROS2NodeName = DefaultNodeName)
    {
        name = unityROS2NodeName;
        node = Ros2cs.CreateNode(name);
        clock = new ROS2Clock();
    }

    /// <summary>
    /// Releases the underlying ros2cs node and this node's owned ROS clock.
    /// </summary>
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

    /// <summary>
    /// Attempts to stamp a header message with current ROS time.
    /// Returns false without throwing if the node or clock has already been disposed.
    /// </summary>
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

    private void ThrowIfUninitializedLocked(string callContext)
    {
        if (disposed || node == null || !Ros2cs.Ok())
        {
            throw new InvalidOperationException("Ros2 For Unity is not initialized, can't " + callContext);
        }
    }

    // Captures a live node under mutex, then executes ros2cs work outside the lock.
    // This keeps Dispose from being blocked by long native create/remove calls.
    private TResult WithLiveNode<TResult>(string callContext, Func<INode, TResult> action)
    {
        INode liveNode;
        lock (mutex)
        {
            ThrowIfUninitializedLocked(callContext);
            liveNode = node;
        }

        return action(liveNode);
    }

    /// <summary>
    /// Create a publisher with QoS suitable for sensor data
    /// </summary>
    /// <returns>The publisher</returns>
    /// <param name="topicName">topic that will be used for publishing</param>
    public Publisher<T> CreateSensorPublisher<T>(string topicName) where T : Message, new()
    {
        // ros2cs copies QoS settings during publisher creation; set the sensor-data policies
        // explicitly because some Windows runtime builds do not map the SENSOR_DATA preset.
        using (QualityOfServiceProfile sensorProfile = new QualityOfServiceProfile(QosPresetProfile.SENSOR_DATA))
        {
            sensorProfile.SetPolicies(
                HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
                1,
                ReliabilityPolicy.QOS_POLICY_RELIABILITY_BEST_EFFORT,
                DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE);
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
        if (qos != null)
        {
            return WithLiveNode("create publisher", liveNode => liveNode.CreatePublisher<T>(topicName, qos));
        }

        using (QualityOfServiceProfile defaultQos = new QualityOfServiceProfile(QosPresetProfile.DEFAULT))
        {
            // ros2cs CreatePublisher expects an explicit QoS profile; the default profile is copied during creation.
            return WithLiveNode("create publisher", liveNode => liveNode.CreatePublisher<T>(topicName, defaultQos));
        }
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
            // ros2cs CreateSubscription expects an explicit QoS profile; the default profile is copied during creation.
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
        if (disposed)
            return false;

        return WithLiveNode("remove subscription", liveNode => liveNode.RemoveSubscription(subscription));
    }

    /// <summary>
    /// Backward-compatibility overload; <typeparamref name="T"/> is unused and delegates to the non-generic overload.
    /// </summary>
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
        if (disposed)
            return false;

        return WithLiveNode("remove publisher", liveNode => liveNode.RemovePublisher(publisher));
    }

    /// <summary>
    /// Backward-compatibility overload; <typeparamref name="T"/> is unused and delegates to the non-generic overload.
    /// </summary>
    public bool RemovePublisher<T>(IPublisherBase publisher)
    {
        return RemovePublisher(publisher);
    }

    /// <inheritdoc cref="INode.CreateService"/>
    public Service<I, O> CreateService<I, O>(string topic, Func<I, O> callback, QualityOfServiceProfile qos = null)
        where I : Message, new()
        where O : Message, new()
    {
        if (qos != null)
            return WithLiveNode("create service", liveNode => liveNode.CreateService<I, O>(topic, callback, qos));

        using (QualityOfServiceProfile defaultQos = new QualityOfServiceProfile(QosPresetProfile.DEFAULT))
        {
            return WithLiveNode("create service", liveNode => liveNode.CreateService<I, O>(topic, callback, defaultQos));
        }
    }

    /// <inheritdoc cref="INode.RemoveService"/>
    public bool RemoveService(IServiceBase service)
    {
        if (disposed)
            return false;

        return WithLiveNode("remove service", liveNode => liveNode.RemoveService(service));
    }

    /// <inheritdoc cref="INode.CreateClient"/>
    public Client<I, O> CreateClient<I, O>(string topic, QualityOfServiceProfile qos = null)
        where I : Message, new()
        where O : Message, new()
    {
        if (qos != null)
            return WithLiveNode("create client", liveNode => liveNode.CreateClient<I, O>(topic, qos));

        using (QualityOfServiceProfile defaultQos = new QualityOfServiceProfile(QosPresetProfile.DEFAULT))
        {
            return WithLiveNode("create client", liveNode => liveNode.CreateClient<I, O>(topic, defaultQos));
        }
    }

    /// <inheritdoc cref="INode.RemoveClient"/>
    public bool RemoveClient(IClientBase client)
    {
        if (disposed)
            return false;

        return WithLiveNode("remove client", liveNode => liveNode.RemoveClient(client));
    }
}

}  // namespace ROS2
