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

using UnityEngine;
using System;

namespace ROS2
{

/// <summary>
/// Historical abstract MonoBehaviour base class for ROS2-enabled sensors.
/// </summary>
/// <remarks>
/// Despite the I-prefix this is not a C# interface; derive from it instead of implementing it.
/// The name is preserved for Unity serialization and downstream script compatibility.
/// </remarks>
public abstract class ISensor : MonoBehaviour
{
    /// <summary>
    /// Default sensor frequency in Hz used by inspector-created sensors.
    /// </summary>
    private const double DefaultDesiredUpdateFrequencyHz = 25.0;

    /// <summary>
    /// The desired update frequency for the sensor. The base class refreshes desiredFrameTime when this value
    /// changes at runtime, but subclasses must still use desiredFrameTime in HasNewData() to apply the limit.
    /// </summary>
    public double desiredUpdateFreq = DefaultDesiredUpdateFrequencyHz;

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
    /// Controls whether sensor is publishing messages. External writes only gate publishing; disabling or
    /// destroying the component is still responsible for unregistering the executor callback.
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
    /// Subclasses are responsible for applying desiredFrameTime if they want frequency gating.
    /// </summary>
    protected abstract bool HasNewData();

    /// <summary>
    /// Desired seconds between sensor readings. The base class calculates this value but does not gate HasNewData().
    /// </summary>
    protected double desiredFrameTime = 0.0;

    // Avoid division by zero and near-zero update intervals when users edit desiredUpdateFreq in the Inspector.
    private const double MinimumFrequencyHz = 0.001;
    private double cachedDesiredUpdateFreq = Double.NaN;
    private Publisher<T> publisher;
    private ROS2UnityComponent ros2UnityComponent;
    private ROS2Node ros2Node;
    private string ownerAgentName;
    private string cachedFrameName;

    private T readings;
    private bool newReadings;
    private readonly object readingsMutex = new object();

    public override string frameName()
    {
        if (cachedFrameName != null)
        {
            return cachedFrameName;
        }

        if (String.IsNullOrEmpty(ownerAgentName))
        {
            return frameID;
        }
        return ownerAgentName + "/" + frameID;
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

        ownerAgentName = agentName;
        cachedFrameName = String.IsNullOrEmpty(ownerAgentName) ? frameID : ownerAgentName + "/" + frameID;
        ros2UnityComponent = ros2Unity;
        ros2Node = node;
        string nsName = agentName.Replace(" ", "_");
        publisher = node.CreateSensorPublisher<T>(nsName + "/" + topicName);
        ros2UnityComponent.RegisterExecutable(ExecutorThreadSensorPublishAction);
        publishing = true;
    }

    /// <summary>
    /// Sensor sampling and timestamping run on Unity main thread in Update(); this executor-thread method only publishes a cached reading.
    /// The message header timestamp is therefore acquisition/update time, not executor publish time.
    /// </summary>
    internal void ExecutorThreadSensorPublishAction()
    {
        T readingToPublish = null;
        Publisher<T> publisherToUse = null;
        ROS2UnityComponent componentToUse = null;
        ROS2Node nodeToUse = null;

        lock (readingsMutex)
        {
            if (newReadings && publisher != null && publishing && ros2Node != null && !ros2Node.IsDisposed)
            {
                readingToPublish = readings;
                newReadings = false;
                publisherToUse = publisher;
                componentToUse = ros2UnityComponent;
                nodeToUse = ros2Node;
            }
        }

        if (readingToPublish == null || componentToUse == null || nodeToUse == null || !componentToUse.Ok())
        {
            return;
        }

        publisherToUse.Publish(readingToPublish);
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

        if (!publishing || publisher == null || ros2Node == null || ros2Node.IsDisposed ||
            ros2UnityComponent == null || !ros2UnityComponent.Ok())
        {
            return;
        }

        // HasNewData() and AcquireValue() may call Unity APIs; keep them on the Unity main thread.
        if (!HasNewData())
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
        if (!ros2Node.TryUpdateROSTimestamp(ref acquiredHeader))
        {
            UnregisterExecutable();
            return;
        }

        lock (readingsMutex)
        {
            readings = acquiredReading;
            newReadings = true;
        }
    }

    /// <summary>
    /// Initialize header and calculate frame time
    /// </summary>
    void Awake()
    {
        // Publishing starts only after CreateROSParticipants creates a real ROS publisher.
        publishing = false;
        CalculateFrameTime();
    }

    /// <summary>
    /// Unregisters the executor callback before Unity disables this component, then invokes subclass cleanup.
    /// </summary>
    void OnDisable()
    {
        UnregisterExecutable();
        OnSensorDisable();
    }

    /// <summary>
    /// Unregisters the executor callback before Unity destroys this component, then invokes subclass cleanup.
    /// </summary>
    void OnDestroy()
    {
        UnregisterExecutable();
        OnSensorDestroy();
    }

    /// <summary>
    /// Optional subclass hook invoked after the executor callback has been unregistered during OnDisable().
    /// </summary>
    protected virtual void OnSensorDisable()
    {
    }

    /// <summary>
    /// Optional subclass hook invoked after the executor callback has been unregistered during OnDestroy().
    /// </summary>
    protected virtual void OnSensorDestroy()
    {
    }

    private void UnregisterExecutable()
    {
        ROS2UnityComponent componentToUnregister = null;
        lock (readingsMutex)
        {
            componentToUnregister = ros2UnityComponent;
            ros2UnityComponent = null;
            ros2Node = null;
            publisher = null;
            readings = null;
            newReadings = false;
        }

        if (componentToUnregister != null)
        {
            componentToUnregister.UnregisterExecutable(ExecutorThreadSensorPublishAction);
        }

        publishing = false;
    }

    /// <summary>
    /// Sensor frequency is used to calculate frame time, based on desired frequency and the bounds.
    /// </summary>
    void CalculateFrameTime()
    {
        double maxFrameFreq = 1.0 / Time.fixedDeltaTime;
        if (desiredUpdateFreq > maxFrameFreq)
        {
            Debug.LogWarning("Desired frame rate of " + desiredUpdateFreq + " can't be met, "
                            + "physics frequency is " + maxFrameFreq);
            desiredUpdateFreq = maxFrameFreq;  //Can't go faster than physics
        }
        if (desiredUpdateFreq < MinimumFrequencyHz)
        {
            Debug.LogWarning("Minimum frequency of " + MinimumFrequencyHz
                             + " applied instead of " + desiredUpdateFreq);
            desiredUpdateFreq = MinimumFrequencyHz;
        }
        desiredFrameTime = 1.0 / desiredUpdateFreq;
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
