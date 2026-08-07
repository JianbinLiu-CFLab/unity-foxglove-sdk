// Copyright 2019-2021 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
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

using UnityEngine;
using System;

namespace ROS2
{

/// <summary>
/// An abstract base class for ROS2-enabled sensor.
/// </summary>
public abstract class ISensor : MonoBehaviour
{
    /// <summary>
    /// The desired update frequency for the sensor. The maximum can be the rate with which FixedUpdate is called,
    /// which depends on the physics step (usually 50 or 100 times per second).
    /// </summary>
    public double desiredUpdateFreq = 25.0;

    /// <summary>
    /// The frameID corresponds to the ROS frame_id element of the header and is important
    /// for transformations
    /// </summary>
    public string frameID = "sensor";

    /// <summary>
    /// A topic to which the sensor publishes. Only one per sensor. Don't add the namespace of
    /// the agent name, it is handled externally (i.e. sensor does not know to what object it belongs).
    /// </summary>
    public string topicName = "";

    /// <summary>
    /// Controls whether sensor is publishing messages
    /// </summary>
    public bool publishing = false;

    /// <summary>
    /// Creates sensor publishers and registers it in the executor so that it publishes when new data is available
    /// </summary>
    /// <param name="ros2Unity"> Central ros2 monobehavior for Unity </param>
    /// <param name="node"> ros2 node that will publish sensor data </param>
    /// <param name="agentName"> name of the agent (vehicle) to be added to the sensor publish namespace </param>
    public abstract void CreateROSParticipants(ROS2UnityComponent ros2Unity, ROS2Node node, string agentName);

    /// <summary>
    /// Returns the constructed frame name, taking in account the agent name(space)
    /// </summary>
    public abstract string frameName();
}

/// <summary>
/// A base template class for the sensor. The type is the message type of sensor data.
/// </summary>
public abstract class Sensor<T> : ISensor where T : class, MessageWithHeader, new()
{
    /// <summary>
    /// Acquires the value by performing sensor type characteristic computations (e.g. raycasts).
    /// Implemented in subclasses.
    /// </summary>
    /// <returns>The message which contains the sensor data.
    /// Mind that the header for message is handled in a generic way by this class.</returns>
    protected abstract T AcquireValue();

    /// <summary>
    /// Returns true when there is a new data available from sensor.
    /// </summary>
    protected abstract bool HasNewData();

    protected double desiredFrameTime = 0.0;
    private const double MinimumFrequencyHz = 0.001;
    private double cachedDesiredUpdateFreq = Double.NaN;
    private PublisherOwnership publisherOwnership;
    private ROS2UnityComponent ros2UnityComponent;
    private string ownerAgentName;
    private string cachedFrameName;
    private bool rosParticipantsDisposed = true;

    private T readings;
    private bool newReadings;
    private readonly object readingsMutex = new object();

    private sealed class PublisherOwnership
    {
        internal PublisherOwnership(ROS2Node node, Publisher<T> publisher)
        {
            Node = node;
            Publisher = publisher;
        }

        internal ROS2Node Node { get; }
        internal Publisher<T> Publisher { get; }
        internal int ActiveCalls { get; set; }
        internal bool Retired { get; set; }
        internal bool RemovalClaimed { get; set; }
    }

    public override string frameName()
    {
        if (cachedFrameName != null)
        {
            return cachedFrameName;
        }

        return String.IsNullOrEmpty(ownerAgentName) ? frameID : ownerAgentName + "/" + frameID;
    }

    /// <summary>
    /// Visualises the effects of the sensor. It doesn't make sense for some sensor and the
    /// default implementation is empty.
    /// </summary>
    protected virtual void VisualiseEffects()
    {
    }

    /// <summary>
    /// When parameters in editor change (i.e. frequency),
    /// this function is called to calculate new frame time.
    /// </summary>
    protected virtual void OnValidate()
    {
        cachedFrameName = null;
        CalculateFrameTime();
    }

    /// <summary>
    /// An entry point for the per-frame processing done in subclass
    /// </summary>
    protected virtual void OnUpdate() {}

    /// <summary>
    /// See superclass definition
    /// </summary>
    public override void CreateROSParticipants(ROS2UnityComponent ros2Unity, ROS2Node node, string agentName)
    {
        if (!ros2Unity.Ok())
        {
            throw new System.InvalidOperationException("Publisher for sensor can't be created when node is not OK");
        }

        if (String.IsNullOrEmpty(topicName))
        {
            throw new System.InvalidOperationException("Topic name not set for the sensor " + this);
        }

        lock (readingsMutex)
        {
            if (publisherOwnership != null)
            {
                throw new System.InvalidOperationException("ROS participants have already been created for sensor " + this);
            }
        }

        ownerAgentName = agentName;
        cachedFrameName = String.IsNullOrEmpty(ownerAgentName) ? frameID : ownerAgentName + "/" + frameID;
        string nsName = (agentName ?? String.Empty).Replace(" ", "_");
        var createdPublisher = node.CreateSensorPublisher<T>(nsName + "/" + topicName);
        lock (readingsMutex)
        {
            ros2UnityComponent = ros2Unity;
            publisherOwnership = new PublisherOwnership(node, createdPublisher);
            rosParticipantsDisposed = false;
        }
        ros2Unity.RegisterExecutable(ExecutorThreadSensorPublishAction);
    }

    /// <summary>
    /// This is executed in an executor thread (through RegisterExecutable)
    /// Sensor fequency is indirectly handed through newReadings, which are acquired at a requested
    /// frequency if possible (e. g. due to simulation resource constraints)
    /// </summary>
    internal void ExecutorThreadSensorPublishAction()
    {
        T readingToPublish;
        PublisherOwnership ownershipToUse;
        Publisher<T> publisherToUse;
        lock (readingsMutex)
        {
            if (rosParticipantsDisposed || !(publisherOwnership != null && publishing) ||
                !newReadings || publisherOwnership.Node == null || publisherOwnership.Node.IsDisposed)
            {
                return;
            }

            readingToPublish = readings;
            ownershipToUse = publisherOwnership;
            publisherToUse = ownershipToUse.Publisher;
            newReadings = false;
            ownershipToUse.ActiveCalls++;
        }

        try
        {
            publisherToUse.Publish(readingToPublish);
        }
        finally
        {
            CompletePublisherCall(ownershipToUse);
        }
    }

    /// <summary>
    /// Once each frame, visualise effects of the sensor (if any). Visualisation
    /// rate is independent of publishing/acquisition rate, which happen at the sensor
    /// frequency instead of the app frame rate.
    /// </summary>
    void Update()
    {
        VisualiseEffects();
        OnUpdate();
        UpdateReadingOnMainThread();
    }

    private void UpdateReadingOnMainThread()
    {
        RefreshDesiredFrameTimeIfNeeded();

        PublisherOwnership ownershipToUse;
        ROS2UnityComponent componentToUse;
        ROS2Node nodeToUse;
        lock (readingsMutex)
        {
            if (rosParticipantsDisposed || !publishing || publisherOwnership == null ||
                publisherOwnership.Node == null || publisherOwnership.Node.IsDisposed)
            {
                return;
            }

            ownershipToUse = publisherOwnership;
            componentToUse = ros2UnityComponent;
            nodeToUse = ownershipToUse.Node;
        }

        if (componentToUse == null || !componentToUse.Ok() || !HasNewData())
        {
            return;
        }

        T acquiredReading = AcquireValue();
        if (acquiredReading == null)
        {
            return;
        }

        acquiredReading.SetHeaderFrame(frameName());
        MessageWithHeader acquiredHeader = acquiredReading;
        if (!nodeToUse.TryUpdateROSTimestamp(ref acquiredHeader))
        {
            DisposeRosParticipants();
            return;
        }

        lock (readingsMutex)
        {
            if (rosParticipantsDisposed || !ReferenceEquals(publisherOwnership, ownershipToUse))
            {
                return;
            }

            readings = acquiredReading;
            newReadings = true;
        }
    }

    /// <summary>
    /// Initialize header and calculate frame time
    /// </summary>
    void Awake()
    {
        CalculateFrameTime();
    }

    void OnDisable()
    {
        DisposeRosParticipants();
    }

    void OnDestroy()
    {
        DisposeRosParticipants();
    }

    private void DisposeRosParticipants()
    {
        // U2F-LOCAL-PATCH: unregister the executor action before serialized publisher teardown.
        ROS2UnityComponent componentToUnregister;
        lock (readingsMutex)
        {
            if (rosParticipantsDisposed)
            {
                return;
            }

            rosParticipantsDisposed = true;
            componentToUnregister = ros2UnityComponent;
        }

        if (componentToUnregister != null)
        {
            componentToUnregister.UnregisterExecutable(ExecutorThreadSensorPublishAction);
        }

        ROS2Node nodeToUse = null;
        Publisher<T> publisherToRemove = null;
        PublisherOwnership ownershipToRetire;
        lock (readingsMutex)
        {
            ros2UnityComponent = null;
            readings = null;
            newReadings = false;
            cachedFrameName = null;
            ownershipToRetire = publisherOwnership;
            publisherOwnership = null;
            if (ownershipToRetire != null)
            {
                ownershipToRetire.Retired = true;
                if (ownershipToRetire.ActiveCalls == 0)
                {
                    ownershipToRetire.RemovalClaimed = true;
                    nodeToUse = ownershipToRetire.Node;
                    publisherToRemove = ownershipToRetire.Publisher;
                }
            }
        }

        RemovePublisherSafely(nodeToUse, publisherToRemove);
    }

    private void CompletePublisherCall(PublisherOwnership ownership)
    {
        ROS2Node nodeToUse = null;
        Publisher<T> publisherToRemove = null;
        lock (readingsMutex)
        {
            ownership.ActiveCalls--;
            if (ownership.ActiveCalls == 0 && ownership.Retired && !ownership.RemovalClaimed)
            {
                ownership.RemovalClaimed = true;
                nodeToUse = ownership.Node;
                publisherToRemove = ownership.Publisher;
            }
        }

        RemovePublisherSafely(nodeToUse, publisherToRemove);
    }

    private static void RemovePublisherSafely(
        ROS2Node nodeToUse,
        Publisher<T> publisherToRemove)
    {
        if (nodeToUse == null || publisherToRemove == null || nodeToUse.IsDisposed)
        {
            return;
        }

        try
        {
            nodeToUse.RemovePublisher<T>(publisherToRemove);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to remove ROS2 sensor publisher during cleanup: " + ex.Message);
        }
    }

    /// <summary>
    /// Sensor frequency is used to calculate frame time, based on desired frequency and the bounds.
    /// </summary>
    void CalculateFrameTime()
    {
        var clampedUpdateFreq = desiredUpdateFreq;
        double maxFrameFreq = 1.0 / Time.fixedDeltaTime;
        if (clampedUpdateFreq > maxFrameFreq)
        {
            Debug.LogWarning("Desired frame rate of " + clampedUpdateFreq + " can't be met, "
                            + "physics frequency is " + maxFrameFreq);
            clampedUpdateFreq = maxFrameFreq;  //Can't go faster than physics
        }
        if (clampedUpdateFreq < MinimumFrequencyHz)
        {
            Debug.LogWarning("Minimum frequency of " + MinimumFrequencyHz
                             + " applied instead of " + clampedUpdateFreq);
            clampedUpdateFreq = MinimumFrequencyHz;
        }
        desiredFrameTime = 1.0 / clampedUpdateFreq;
        cachedDesiredUpdateFreq = desiredUpdateFreq;
    }

    private void RefreshDesiredFrameTimeIfNeeded()
    {
        if (!desiredUpdateFreq.Equals(cachedDesiredUpdateFreq))
        {
            CalculateFrameTime();
        }
    }
}

}  // namespace ROS2
